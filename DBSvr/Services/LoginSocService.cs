using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using DBSvr.Core;
using SystemModule;
using SystemModule.Packet;
using SystemModule.Sockets;

namespace DBSvr
{
    /// <summary>
    /// LoginSvr 连接服务 (端口 5600)。
    /// 会话管理优化:
    ///   - ReaderWriterLockSlim: 99% 读操作不互斥
    ///   - Dictionary O(1) 查找代替线性扫描
    /// </summary>
    public class LoginSvrService
    {
        private const int MaxServerConnections = 64;
        private const int MaxServerFrameLength = 64 * 1024;
        private readonly ISocketServer _serverSocket;
        private readonly IClientScoket _nativeClient;
        private readonly YbDbLegacy77StreamParser _nativeParser = new();
        private readonly object _nativeParserLock = new();
        private readonly ConcurrentDictionary<int, PendingNativeAuth> _pendingNativeAuth = new();
        private readonly object _nativeControlLock = new();
        private readonly Queue<NativeControlItem> _pendingNativeControls = new();
        private Socket _nativeControlSocket;
        private long _nativeControlGeneration;
        private long _nativeRegistrationGeneration;
        private bool _nativeControlFlushing;
        private readonly ConcurrentDictionary<int, ServerReceiveState> _receiveStates = new();
        private readonly IList<TGlobaSessionInfo> GlobaSessionList;
        private readonly Dictionary<(string, int), TGlobaSessionInfo> _sessionDict;
        private readonly ReaderWriterLockSlim _rwLock = new(LockRecursionPolicy.NoRecursion);
        private readonly LoginGateTransportMode _mode;
        private readonly int _reconnectIntervalMs;
        private readonly int _authTimeoutMs;
        private readonly string _gameGateAddress;
        private readonly ushort _gameGatePort;
        private readonly ushort _zoneIndex;
        private readonly byte _groupIndex;
        private readonly string sIDAddr;
        private readonly int nIDPort;
        private long _lastConnectAttempt;
        private long _lastRegistrationTick;
        private int _nativeUserCount;
        private int _nativeRegistered;
        private int _started;

        public LoginGateTransportMode Mode => _mode;
        public bool IsNativeRegistered => Volatile.Read(ref _nativeRegistered) != 0;

        public LoginSvrService(ConfigManager configManager)
        {
            sIDAddr = configManager.ReadString("LoginGate", "IP", DBShare.sIDServerAddr);
            nIDPort = configManager.ReadInteger("LoginGate", "Port", DBShare.nIDServerPort);
            var modeName = configManager.ReadString("LoginGate", "Mode",
                nameof(LoginGateTransportMode.Native77Client));
            if (!Enum.TryParse(modeName, true, out _mode)
                || !Enum.IsDefined(typeof(LoginGateTransportMode), _mode))
                throw new InvalidOperationException($"unsupported LoginGate mode '{modeName}'");
            _reconnectIntervalMs = Math.Max(100,
                configManager.ReadInteger("LoginGate", "ReconnectIntervalMs", 20000));
            _authTimeoutMs = Math.Max(100,
                configManager.ReadInteger("LoginGate", "AuthTimeoutMs", 10000));
            if (DBShare.g_nPublicGatePort <= 0
                || DBShare.g_nPublicGatePort > ushort.MaxValue)
                throw new InvalidOperationException(
                    "GameGate public port must fit in one unsigned word");
            if (DBShare.nZoneIdx < 0 || DBShare.nZoneIdx > ushort.MaxValue)
                throw new InvalidOperationException(
                    "ZoneIdx must fit in one unsigned word");
            if (DBShare.nGroupIdx < 0 || DBShare.nGroupIdx > byte.MaxValue)
                throw new InvalidOperationException(
                    "GroupIdx must fit in one byte");
            _gameGateAddress = DBShare.g_sPublicGateAddr;
            _gameGatePort = (ushort)DBShare.g_nPublicGatePort;
            _zoneIndex = (ushort)DBShare.nZoneIdx;
            _groupIndex = (byte)DBShare.nGroupIdx;
            GlobaSessionList = new List<TGlobaSessionInfo>();
            _sessionDict = new Dictionary<(string, int), TGlobaSessionInfo>();

            if (_mode == LoginGateTransportMode.PrivateListener)
            {
                _serverSocket = new ISocketServer(MaxServerConnections, 1024);
                _serverSocket.OnClientConnect += (sender, e) =>
                    _receiveStates[e.ConnectionId] = new ServerReceiveState();
                _serverSocket.OnClientDisconnect += (sender, e) =>
                    _receiveStates.TryRemove(e.ConnectionId, out _);
                _serverSocket.OnClientRead += OnServerClientRead;
                _serverSocket.OnClientError += (_, _) => { };
                _serverSocket.Init();
            }
            else
            {
                _nativeClient = new IClientScoket { Host = sIDAddr, Port = nIDPort };
                _nativeClient.OnConnected += OnNativeConnected;
                _nativeClient.OnDisconnected += OnNativeDisconnected;
                _nativeClient.ReceivedDatagram += OnNativeData;
                _nativeClient.OnError += (_, e) =>
                    DBShare.MainOutMessage($"原版LoginGate连接错误: {e?.exception?.Message ?? e?.ErrorCode.ToString()}");
            }
        }

        private void OnServerClientRead(object sender, AsyncUserToken e)
        {
            var state = _receiveStates.GetOrAdd(e.ConnectionId, _ => new ServerReceiveState());
            List<string> frames = new();
            bool overflow = false;
            lock (state.SyncRoot)
            {
                for (var i = 0; i < e.BytesReceived; i++)
                {
                    var value = e.ReceiveBuffer[e.Offset + i];
                    if (state.Buffer.Count == 0)
                    {
                        if (value == (byte)'(') state.Buffer.Add(value);
                        continue;
                    }

                    if (value == (byte)'(') state.Buffer.Clear();
                    state.Buffer.Add(value);
                    if (state.Buffer.Count > MaxServerFrameLength)
                    {
                        state.Buffer.Clear();
                        overflow = true;
                        break;
                    }

                    if (value == (byte)')')
                    {
                        frames.Add(HUtil32.GetString(state.Buffer.ToArray(), 1, state.Buffer.Count - 2));
                        state.Buffer.Clear();
                    }
                }
            }
            if (overflow)
            {
                DBShare.MainOutMessage($"5600连接[{e.ConnectionId}]帧超过{MaxServerFrameLength}字节，连接已关闭.");
                e.Socket.Close();
                return;
            }
            foreach (var frame in frames) ProcessSocketFrame(frame);
        }

        public void Start()
        {
            if (Interlocked.Exchange(ref _started, 1) != 0) return;
            if (_mode == LoginGateTransportMode.PrivateListener)
            {
                _serverSocket.Start(sIDAddr, nIDPort);
                Console.WriteLine($"LoginSvrService {sIDAddr}:{nIDPort} 监听已启动 (接受GameSvr会话连接)");
                return;
            }

            Interlocked.Exchange(ref _lastConnectAttempt,
                Environment.TickCount64 - _reconnectIntervalMs);
            CheckConnection();
            Console.WriteLine($"LoginSvrService 正在连接原版LoginGate {sIDAddr}:{nIDPort}");
        }

        public void Stop()
        {
            Interlocked.Exchange(ref _started, 0);
            if (_mode == LoginGateTransportMode.PrivateListener)
                _serverSocket.Shutdown();
            else
                _nativeClient.Disconnect();
            FailAllNativeAuth("native LoginGate stopped");
            _receiveStates.Clear();
            _rwLock.EnterWriteLock();
            try { GlobaSessionList.Clear(); _sessionDict.Clear(); }
            finally { _rwLock.ExitWriteLock(); }
        }

        public void CheckConnection()
        {
            if (_mode == LoginGateTransportMode.PrivateListener) return;
            SweepNativeAuthTimeouts();
            if (Volatile.Read(ref _started) == 0
                || _nativeClient.IsConnected || _nativeClient.IsBusy) return;

            var now = Environment.TickCount64;
            if (now - Interlocked.Read(ref _lastConnectAttempt) < _reconnectIntervalMs)
                return;
            Interlocked.Exchange(ref _lastConnectAttempt, now);
            _nativeClient.Connect();
        }

        public bool TryAuthenticateNative(int queryId, string ticket,
            byte[] deviceId, string userIp, string deviceName,
            Action<NativeLoginGateAuthResponse, string, long> completion,
            out string error)
        {
            error = string.Empty;
            if (_mode != LoginGateTransportMode.Native77Client)
            {
                error = "native LoginGate authentication is not enabled";
                return false;
            }
            if (completion == null)
            {
                error = "native LoginGate completion is null";
                return false;
            }
            if (!_nativeClient.IsConnected)
            {
                error = "native LoginGate is disconnected";
                return false;
            }
            if (Volatile.Read(ref _nativeRegistered) == 0)
            {
                error = "native LoginGate registration is not acknowledged";
                return false;
            }
            if (DBShare.nZoneIdx < ushort.MinValue || DBShare.nZoneIdx > ushort.MaxValue
                || DBShare.nGroupIdx < ushort.MinValue || DBShare.nGroupIdx > ushort.MaxValue)
            {
                error = "native LoginGate area/group must fit in one word";
                return false;
            }
            if (!NativeLoginGateProtocol.TryCreateAuthRequest(queryId, ticket,
                    deviceId, userIp, deviceName,
                    (ushort)DBShare.nZoneIdx, (ushort)DBShare.nGroupIdx,
                    out var frame, out error)
                || !YbDbLegacy77Codec.TryEncode(frame, out var wire, out error))
                return false;

            var pending = new PendingNativeAuth(
                Environment.TickCount64 + _authTimeoutMs, completion);
            lock (_nativeControlLock)
            {
                if (!_pendingNativeAuth.TryAdd(queryId, pending))
                {
                    error = $"native LoginGate query {queryId} is already pending";
                    return false;
                }
            }
            if (_nativeClient.QueueSend(wire, null)) return true;

            lock (_nativeControlLock)
                _pendingNativeAuth.TryRemove(queryId, out _);
            error = "native LoginGate disconnected before authentication send";
            return false;
        }

        public bool SetPendingNativeLoginDateTimeBits(int queryId, long bits)
        {
            lock (_nativeControlLock)
            {
                if (!_pendingNativeAuth.TryGetValue(queryId, out var pending))
                    return false;
                pending.SetLoginDateTimeBits(bits);
                return true;
            }
        }

        private void OnNativeConnected(object sender, DSCClientConnectedEventArgs e)
        {
            if (Volatile.Read(ref _started) == 0)
            {
                _nativeClient.Disconnect(e?.socket);
                return;
            }
            if (e?.socket == null || !_nativeClient.IsCurrentConnection(e.socket))
                return;
            long generation;
            lock (_nativeControlLock)
            {
                if (!_nativeClient.IsCurrentConnection(e.socket)) return;
                generation = ++_nativeControlGeneration;
                _nativeControlSocket = e.socket;
                _nativeRegistrationGeneration = 0;
            }
            lock (_nativeParserLock) _nativeParser.Reset();
            Interlocked.Exchange(ref _nativeRegistered, 0);
            SendNativeRegistration(e.socket, generation);
            FlushNativeControls();
        }

        private void OnNativeDisconnected(object sender, DSCClientConnectedEventArgs e)
        {
            var currentGeneration = false;
            lock (_nativeControlLock)
            {
                if (ReferenceEquals(_nativeControlSocket, e?.socket))
                {
                    _nativeControlSocket = null;
                    _nativeControlGeneration++;
                    _nativeRegistrationGeneration = 0;
                    currentGeneration = true;
                }
            }
            if (!currentGeneration) return;
            lock (_nativeParserLock) _nativeParser.Reset();
            Interlocked.Exchange(ref _nativeRegistered, 0);
            FailAllNativeAuth("native LoginGate disconnected");
        }

        private void OnNativeData(object sender, DSCClientDataInEventArgs e)
        {
            if (e?.Socket == null) return;
            List<YbDbLegacy77Frame> frames = new();
            long generation;
            try
            {
                lock (_nativeControlLock)
                {
                    generation = _nativeControlGeneration;
                    if (!IsCurrentNativeGenerationNoLock(e.Socket, generation)) return;
                    lock (_nativeParserLock)
                        _nativeParser.Append(e.Buff.AsSpan(0, e.BuffLen), frames.Add);
                }
            }
            catch (Exception ex)
            {
                DBShare.MainOutMessage("原版LoginGate数据流错误: " + ex.Message);
                _nativeClient.Disconnect(e.Socket);
                return;
            }

            foreach (var frame in frames)
                ProcessNativeLoginGateFrame(frame, e.Socket, generation);
        }

        private void ProcessNativeLoginGateFrame(YbDbLegacy77Frame frame,
            Socket socket, long generation)
        {
            if (NativeLoginGateProtocol.IsRegistrationResponse(frame))
            {
                lock (_nativeControlLock)
                {
                    if (!IsCurrentNativeGenerationNoLock(socket, generation)) return;
                    Interlocked.Exchange(ref _nativeRegistered, 1);
                }
                return;
            }
            if (frame.Ident == NativeLoginGateProtocol.ProbeRequestIdent)
            {
                if (!IsCurrentNativeGeneration(socket, generation)) return;
                if (NativeLoginGateProtocol.TryCreateProbeResponse(frame,
                        _gameGateAddress, _gameGatePort, _zoneIndex, _groupIndex,
                        out var response, out var error))
                    QueueNativeFrame(response, socket);
                else
                    DBShare.MainOutMessage("原版LoginGate探测帧错误: " + error);
                return;
            }
            // GDM_SDK_AUTH_RESPONSE_FAIL. LoginGate answers every 2018 with either
            // 1003 or 1004 and guarantees one within 20 s (uSDKAuth.pas:759); the
            // failure branches are :608 module-not-ready, :766 timeout and :1624
            // platform rejection. Dropping 1004 left the request to expire on our
            // own AuthTimeoutMs instead of failing immediately.
            if (frame.Ident == NativeLoginGateProtocol.AuthFailureIdent)
            {
                if (!NativeLoginGateProtocol.TryDecodeAuthFailure(frame,
                        out var failure, out var failureError))
                {
                    DBShare.MainOutMessage("原版LoginGate认证失败帧错误: " + failureError);
                    return;
                }
                PendingNativeAuth rejected;
                lock (_nativeControlLock)
                {
                    if (!IsCurrentNativeGenerationNoLock(socket, generation)) return;
                    _pendingNativeAuth.TryRemove(failure.QueryId, out rejected);
                }
                if (rejected != null)
                    InvokeNativeCompletion(rejected, null, failure.Describe());
                return;
            }
            if (frame.Ident != NativeLoginGateProtocol.AuthResponseIdent) return;
            if (!NativeLoginGateProtocol.TryDecodeAuthResponse(frame,
                    out var authResponse, out var authError))
            {
                DBShare.MainOutMessage("原版LoginGate认证响应错误: " + authError);
                return;
            }
            PendingNativeAuth pending;
            lock (_nativeControlLock)
            {
                if (!IsCurrentNativeGenerationNoLock(socket, generation)) return;
                _pendingNativeAuth.TryRemove(authResponse.QueryId, out pending);
            }
            if (pending != null)
                InvokeNativeCompletion(pending, authResponse, null);
        }

        private void SendNativeRegistration()
        {
            Socket socket;
            long generation;
            lock (_nativeControlLock)
            {
                socket = _nativeControlSocket;
                generation = _nativeControlGeneration;
            }
            if (socket != null) SendNativeRegistration(socket, generation);
        }

        private void SendNativeRegistration(Socket socket, long generation)
        {
            if (!NativeLoginGateProtocol.TryCreateRegistration(DBShare.sServerName,
                    Volatile.Read(ref _nativeUserCount), out var frame, out var error))
            {
                DBShare.MainOutMessage("原版LoginGate注册帧错误: " + error);
                return;
            }
            if (!QueueNativeFrame(frame, socket)) return;
            lock (_nativeControlLock)
            {
                if (!IsCurrentNativeGenerationNoLock(socket, generation)) return;
                _nativeRegistrationGeneration = generation;
                Interlocked.Exchange(ref _lastRegistrationTick, Environment.TickCount64);
            }
        }

        private bool QueueNativeFrame(YbDbLegacy77Frame frame, Socket socket)
        {
            return YbDbLegacy77Codec.TryEncode(frame, out var wire, out _)
                   && _nativeClient.QueueSend(wire, socket);
        }

        private bool IsCurrentNativeGeneration(Socket socket, long generation)
        {
            lock (_nativeControlLock)
                return IsCurrentNativeGenerationNoLock(socket, generation);
        }

        private bool IsCurrentNativeGenerationNoLock(Socket socket, long generation) =>
            generation == _nativeControlGeneration
            && ReferenceEquals(socket, _nativeControlSocket)
            && _nativeClient.IsCurrentConnection(socket);

        public bool QueueNativeType2Control(bool enabled)
        {
            if (_mode != LoginGateTransportMode.Native77Client) return false;
            var frame = NativeLoginGateProtocol.CreateType2Control(enabled);
            if (!YbDbLegacy77Codec.TryEncode(frame, out var wire, out _))
                return false;
            lock (_nativeControlLock)
                _pendingNativeControls.Enqueue(new NativeControlItem(wire));
            FlushNativeControls();
            return true;
        }

        private void FlushNativeControls()
        {
            lock (_nativeControlLock)
            {
                if (_nativeControlFlushing || _pendingNativeControls.Count == 0
                    || _nativeClient?.IsConnected != true
                    || _nativeControlSocket == null
                    || _nativeRegistrationGeneration != _nativeControlGeneration) return;
                _nativeControlFlushing = true;
            }

            var failedGeneration = long.MinValue;
            try
            {
                while (true)
                {
                    List<NativeControlItem> submissions;
                    Socket socket;
                    long generation;
                    lock (_nativeControlLock)
                    {
                        if (_pendingNativeControls.Count == 0
                            || _nativeClient?.IsConnected != true
                            || _nativeControlSocket == null
                            || _nativeRegistrationGeneration != _nativeControlGeneration) return;
                        generation = _nativeControlGeneration;
                        if (generation == failedGeneration) return;
                        socket = _nativeControlSocket;
                        submissions = new List<NativeControlItem>();
                        foreach (var item in _pendingNativeControls)
                        {
                            if (item.Completed
                                || item.SubmittedGeneration == generation) continue;
                            item.SubmittedGeneration = generation;
                            submissions.Add(item);
                        }
                    }

                    if (submissions.Count == 0) return;
                    var submissionFailed = false;
                    for (var i = 0; i < submissions.Count; i++)
                    {
                        var item = submissions[i];
                        if (_nativeClient.QueueSend(item.Wire, socket,
                                success => CompleteNativeControlSend(
                                    item, socket, generation, success))) continue;
                        lock (_nativeControlLock)
                        {
                            for (var pending = i; pending < submissions.Count; pending++)
                                if (submissions[pending].SubmittedGeneration == generation)
                                    submissions[pending].SubmittedGeneration = 0;
                        }
                        submissionFailed = true;
                        failedGeneration = generation;
                        break;
                    }
                    if (!submissionFailed) continue;
                }
            }
            finally
            {
                var flushAgain = false;
                lock (_nativeControlLock)
                {
                    _nativeControlFlushing = false;
                    if (_nativeClient?.IsConnected == true
                        && _nativeControlSocket != null
                        && _nativeRegistrationGeneration == _nativeControlGeneration)
                    {
                        foreach (var item in _pendingNativeControls)
                        {
                            if (!item.Completed
                                && item.SubmittedGeneration != _nativeControlGeneration
                                && _nativeControlGeneration != failedGeneration)
                            {
                                flushAgain = true;
                                break;
                            }
                        }
                    }
                }
                if (flushAgain)
                    ThreadPool.QueueUserWorkItem(_ => FlushNativeControls());
            }
        }

        private void CompleteNativeControlSend(NativeControlItem item,
            Socket socket, long generation, bool success)
        {
            var flushAgain = false;
            lock (_nativeControlLock)
            {
                if (generation != _nativeControlGeneration
                    || !ReferenceEquals(socket, _nativeControlSocket)
                    || item.SubmittedGeneration != generation) return;
                if (!success)
                {
                    item.SubmittedGeneration = 0;
                    return;
                }
                item.Completed = true;
                while (_pendingNativeControls.Count != 0
                       && _pendingNativeControls.Peek().Completed)
                    _pendingNativeControls.Dequeue();
                flushAgain = Volatile.Read(ref _started) != 0;
            }
            if (flushAgain) FlushNativeControls();
        }

        private void SweepNativeAuthTimeouts()
        {
            var now = Environment.TickCount64;
            foreach (var pair in _pendingNativeAuth)
            {
                PendingNativeAuth pending = null;
                lock (_nativeControlLock)
                {
                    if (pair.Value.DeadlineTick <= now)
                        _pendingNativeAuth.TryRemove(pair.Key, out pending);
                }
                if (pending == null) continue;
                InvokeNativeCompletion(pending, null, "native LoginGate authentication timed out");
            }
        }

        private void FailAllNativeAuth(string error)
        {
            var pendingItems = new List<PendingNativeAuth>();
            lock (_nativeControlLock)
                foreach (var pair in _pendingNativeAuth)
                    if (_pendingNativeAuth.TryRemove(pair.Key, out var pending))
                        pendingItems.Add(pending);
            foreach (var pending in pendingItems)
                InvokeNativeCompletion(pending, null, error);
        }

        private static void InvokeNativeCompletion(PendingNativeAuth pending,
            NativeLoginGateAuthResponse response, string error)
        {
            try
            {
                pending.Completion(response, error,
                    pending.ReadLoginDateTimeBits());
            }
            catch (Exception ex)
            {
                DBShare.MainOutMessage("原版LoginGate认证回调错误: " + ex.Message);
            }
        }

        private void ProcessSocketFrame(string frame)
        {
            string sCode = string.Empty;
            string sBody = HUtil32.GetValidStr3(frame, ref sCode, HUtil32.Backslash);
            switch (HUtil32.Str_ToInt(sCode, 0))
            {
                case Grobal2.SS_OPENSESSION: ProcessAddSession(sBody); break;
                case Grobal2.SS_CLOSESESSION:
                case Grobal2.SS_SOFTOUTSESSION: ProcessDelSession(sBody); break;
                case Grobal2.SS_SERVERINFO: ProcessServerInfo(sBody); break;
                case Grobal2.SS_KEEPALIVE: break;
            }
        }

        public void SendSocketMsg(short wIdent, string sMsg)
        {
            TrySendSocketMsg(wIdent, sMsg);
        }

        public bool TrySendSocketMsg(short wIdent, string sMsg)
        {
            if (_mode == LoginGateTransportMode.Native77Client)
            {
                DBShare.MainOutMessage($"原版LoginGate模式不支持私有5600指令 {wIdent}");
                return false;
            }
            return BroadcastFrame($"({wIdent}/{sMsg})");
        }

        private bool BroadcastFrame(string frame)
        {
            var sockets = _serverSocket?.GetClientSockets();
            if (sockets == null) return false;
            var sent = false;
            foreach (var socket in sockets)
            {
                try
                {
                    if (socket?.Connected == true)
                    {
                        socket.SendText(frame);
                        sent = true;
                    }
                }
                catch
                {
                    // A disconnected peer will be removed by the socket server.
                }
            }
            return sent;
        }

        // ===================== 读操作 (EnterReadLock — 不互斥) =====================

        public bool CheckSession(string sAccount, string sIPaddr, int nSessionID)
        {
            _rwLock.EnterUpgradeableReadLock();
            try
            {
                if (_sessionDict.TryGetValue((sAccount, nSessionID), out var session) && session != null)
                {
                    if (!SessionIPMatches(session.sIPaddr, sIPaddr)) return false;
                    _rwLock.EnterWriteLock();
                    try { TouchSession(session); }
                    finally { _rwLock.ExitWriteLock(); }
                    return true;
                }
                ProtocolTrace($"[DBSessionMiss] Account={sAccount} Session={nSessionID} Total={GlobaSessionList.Count}");
                return false;
            }
            finally { _rwLock.ExitUpgradeableReadLock(); }
        }

        public int CheckSessionLoadRcd(string sAccount, string sIPaddr, int nSessionID, ref bool boFoundSession)
        {
            int result = -1;
            boFoundSession = false;
            _rwLock.EnterUpgradeableReadLock();
            try
            {
                if (_sessionDict.TryGetValue((sAccount, nSessionID), out var session) && session != null &&
                    SessionIPMatches(session.sIPaddr, sIPaddr))
                {
                    boFoundSession = true;
                    _rwLock.EnterWriteLock();
                    try
                    {
                        session.boLoadRcd = true;
                        TouchSession(session);
                    }
                    finally { _rwLock.ExitWriteLock(); }
                    result = 1; // 允许重复加载 (GameSvr 重启后重连)
                }
            }
            finally { _rwLock.ExitUpgradeableReadLock(); }
            return result;
        }

        public bool SetSessionSaveRcd(string sAccount)
        {
            _rwLock.EnterWriteLock();
            try
            {
                foreach (var s in GlobaSessionList)
                {
                    if (s != null && s.sAccount == sAccount)
                    {
                        s.boLoadRcd = false;
                        TouchSession(s);
                        return true;
                    }
                }
                return false;
            }
            finally { _rwLock.ExitWriteLock(); }
        }

        public bool GetGlobaSessionStatus(string sAccount, int nSessionID)
        {
            _rwLock.EnterReadLock();
            try
            {
                return _sessionDict.TryGetValue((sAccount, nSessionID), out var session) &&
                    session != null && session.boStartPlay;
            }
            finally { _rwLock.ExitReadLock(); }
        }

        public bool GetSession(string sAccount, string sIPaddr)
        {
            _rwLock.EnterReadLock();
            try
            {
                foreach (var s in GlobaSessionList)
                    if (s != null && s.sAccount == sAccount && s.sIPaddr == sIPaddr) return true;
                return false;
            }
            finally { _rwLock.ExitReadLock(); }
        }

        // ===================== 写操作 (EnterWriteLock) =====================

        public void SetGlobaSessionNoPlay(string sAccount, int nSessionID)
        {
            _rwLock.EnterWriteLock();
            try
            {
                if (_sessionDict.TryGetValue((sAccount, nSessionID), out var session) && session != null)
                {
                    session.boStartPlay = false;
                    TouchSession(session);
                }
            }
            finally { _rwLock.ExitWriteLock(); }
        }

        public void SetGlobaSessionPlay(string sAccount, int nSessionID)
        {
            _rwLock.EnterWriteLock();
            try
            {
                if (_sessionDict.TryGetValue((sAccount, nSessionID), out var session) && session != null)
                {
                    session.boStartPlay = true;
                    TouchSession(session);
                }
            }
            finally { _rwLock.ExitWriteLock(); }
        }

        public void SetNativeAccountPlayState(string account, bool playing)
        {
            var key = NativeType3Protocol.NormalizePtidKey(account);
            _rwLock.EnterWriteLock();
            try
            {
                foreach (var session in GlobaSessionList)
                {
                    if (session == null
                        || NativeType3Protocol.NormalizePtidKey(
                            session.sAccount) != key)
                        continue;
                    session.boStartPlay = playing;
                    TouchSession(session);
                }
            }
            finally { _rwLock.ExitWriteLock(); }
        }

        public void OpenMobileSession(string sAccount, string sIPaddr, int nSessionID)
        {
            if (string.IsNullOrEmpty(sAccount) || nSessionID == 0) return;
            var supersededSessionIds = new List<int>();
            _rwLock.EnterWriteLock();
            try
            {
                if (_sessionDict.TryGetValue((sAccount, nSessionID), out var existing) && existing != null)
                {
                    existing.sIPaddr = sIPaddr;
                    existing.dwAddTick = HUtil32.GetTickCount();
                    existing.dAddDate = DateTime.Now;
                    return;
                }
                for (var i = GlobaSessionList.Count - 1; i >= 0; i--)
                {
                    var stale = GlobaSessionList[i];
                    if (stale == null || !string.Equals(stale.sAccount, sAccount,
                            StringComparison.OrdinalIgnoreCase)) continue;
                    supersededSessionIds.Add(stale.nSessionID);
                    _sessionDict.Remove((stale.sAccount, stale.nSessionID));
                    GlobaSessionList.RemoveAt(i);
                }
                var session = new TGlobaSessionInfo
                {
                    sAccount = sAccount, sIPaddr = sIPaddr, nSessionID = nSessionID,
                    boStartPlay = false, boLoadRcd = false,
                    dwAddTick = HUtil32.GetTickCount(), dAddDate = DateTime.Now
                };
                GlobaSessionList.Add(session);
                _sessionDict[(sAccount, nSessionID)] = session;
            }
            finally { _rwLock.ExitWriteLock(); }

            foreach (var staleSessionId in supersededSessionIds)
                SendSocketMsg(Grobal2.SS_KICKUSER, sAccount + "/" + staleSessionId);
        }

        public void ClearTimeoutSession()
        {
            var cutoff = DateTime.Now.AddMinutes(-40);
            _rwLock.EnterWriteLock();
            try
            {
                for (var i = GlobaSessionList.Count - 1; i >= 0; i--)
                {
                    var s = GlobaSessionList[i];
                    if (s != null && s.dAddDate < cutoff)
                    {
                        _sessionDict.Remove((s.sAccount, s.nSessionID));
                        GlobaSessionList.RemoveAt(i);
                    }
                }
            }
            finally { _rwLock.ExitWriteLock(); }
        }

        public void CloseSession(string sAccount, int nSessionID)
        {
            _rwLock.EnterWriteLock();
            try
            {
                for (var i = 0; i < GlobaSessionList.Count; i++)
                {
                    var s = GlobaSessionList[i];
                    if (s != null && s.nSessionID == nSessionID && s.sAccount == sAccount)
                    {
                        GlobaSessionList.RemoveAt(i);
                        _sessionDict.Remove((sAccount, nSessionID));
                        break;
                    }
                }
            }
            finally { _rwLock.ExitWriteLock(); }
        }

        private void ProcessAddSession(string sData)
        {
            string sAccount = string.Empty, s10 = string.Empty, s14 = string.Empty, s18 = string.Empty, sIPaddr = string.Empty;
            sData = HUtil32.GetValidStr3(sData, ref sAccount, HUtil32.Backslash);
            sData = HUtil32.GetValidStr3(sData, ref s10, HUtil32.Backslash);
            sData = HUtil32.GetValidStr3(sData, ref s14, HUtil32.Backslash);
            sData = HUtil32.GetValidStr3(sData, ref s18, HUtil32.Backslash);
            sData = HUtil32.GetValidStr3(sData, ref sIPaddr, HUtil32.Backslash);
            int nSessionID = HUtil32.Str_ToInt(s10, 0);

            _rwLock.EnterWriteLock();
            try
            {
                var key = (sAccount, nSessionID);
                if (_sessionDict.TryGetValue(key, out var existing) && existing != null)
                {
                    existing.sIPaddr = sIPaddr;
                    existing.dwAddTick = HUtil32.GetTickCount();
                    existing.dAddDate = DateTime.Now;
                    return;
                }

                var session = new TGlobaSessionInfo
                {
                    sAccount = sAccount, sIPaddr = sIPaddr, nSessionID = nSessionID,
                    boStartPlay = false, boLoadRcd = false,
                    dwAddTick = HUtil32.GetTickCount(), dAddDate = DateTime.Now
                };
                GlobaSessionList.Add(session);
                _sessionDict[key] = session;
            }
            finally { _rwLock.ExitWriteLock(); }
        }

        private void ProcessDelSession(string sData)
        {
            string sAccount = string.Empty;
            sData = HUtil32.GetValidStr3(sData, ref sAccount, HUtil32.Backslash);
            int nSessionID = HUtil32.Str_ToInt(sData, 0);
            CloseSession(sAccount, nSessionID);
        }

        private static void ProcessServerInfo(string sData)
        {
            ProtocolTrace($"[5600] GameSvr信息: {sData}");
        }

        [Conditional("DBSVR_PROTOCOL_TRACE")]
        private static void ProtocolTrace(string message) => Debug.WriteLine(message);

        private static bool SessionIPMatches(string expected, string actual)
        {
            if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(actual)) return false;
            if (IPAddress.TryParse(expected, out var expectedIP) && IPAddress.TryParse(actual, out var actualIP))
            {
                if (expectedIP.IsIPv4MappedToIPv6) expectedIP = expectedIP.MapToIPv4();
                if (actualIP.IsIPv4MappedToIPv6) actualIP = actualIP.MapToIPv4();
                return expectedIP.Equals(actualIP);
            }
            return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
        }

        private static void TouchSession(TGlobaSessionInfo session)
        {
            session.dwAddTick = HUtil32.GetTickCount();
            session.dAddDate = DateTime.Now;
        }

        private string GetSessionSnapshotNoLock(int maxCount)
        {
            if (GlobaSessionList.Count <= 0) return "<empty>";
            string snapshot = string.Empty;
            int count = 0;
            foreach (var s in GlobaSessionList)
            {
                if (s == null) continue;
                if (!string.IsNullOrEmpty(snapshot)) snapshot += ",";
                snapshot += s.sAccount + "/" + s.nSessionID;
                if (++count >= maxCount) break;
            }
            return string.IsNullOrEmpty(snapshot) ? "<empty>" : snapshot;
        }

        public void SendKeepAlivePacket(int userCount)
        {
            if (_mode == LoginGateTransportMode.Native77Client)
            {
                Volatile.Write(ref _nativeUserCount, Math.Max(0, userCount));
                if (_nativeClient.IsConnected
                    && Environment.TickCount64 - Interlocked.Read(ref _lastRegistrationTick) >= 9000)
                    SendNativeRegistration();
                return;
            }
            SendSocketMsg(Grobal2.SS_SERVERINFO, DBShare.sServerName + "/99/" + userCount);
        }

        private sealed class ServerReceiveState
        {
            public readonly object SyncRoot = new();
            public readonly List<byte> Buffer = new();
        }

        private sealed class PendingNativeAuth
        {
            private long _loginDateTimeBits;

            public PendingNativeAuth(long deadlineTick,
                Action<NativeLoginGateAuthResponse, string, long> completion)
            {
                DeadlineTick = deadlineTick;
                Completion = completion;
            }

            public long DeadlineTick { get; }
            public Action<NativeLoginGateAuthResponse, string, long> Completion { get; }

            public void SetLoginDateTimeBits(long bits) =>
                Interlocked.Exchange(ref _loginDateTimeBits, bits);

            public long ReadLoginDateTimeBits() =>
                Interlocked.Read(ref _loginDateTimeBits);
        }

        private sealed class NativeControlItem
        {
            public NativeControlItem(byte[] wire) => Wire = wire;

            public byte[] Wire { get; }
            public long SubmittedGeneration { get; set; }
            public bool Completed { get; set; }
        }
    }

    public enum LoginGateTransportMode
    {
        PrivateListener,
        Native77Client
    }
}
