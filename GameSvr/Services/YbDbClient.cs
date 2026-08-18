using System.Collections.Concurrent;
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using GameSvr.Configs;
using SystemModule;
using SystemModule.Packet;
using SystemModule.Sockets;

namespace GameSvr.Services
{
    public sealed class YbDbClient : IDisposable
    {
        public const int ServicePort = 6108;
        private const int ConnectionPulseIntervalMilliseconds = 10_000;
        private const int HeartbeatDisconnectThreshold = 30;
        private const int SendFlushIntervalMilliseconds = 150;
        private const int SendAggregateCapacity = 0x8000;
        private const int MaxPendingCreditEpochsPerRole = 16;

        private readonly record struct QueuedSend(long Generation, byte[] Data);
        private readonly record struct QueuedResponse(long Generation,
            YbDbLegacy77Frame Frame);
        private sealed record PendingCreditRequest(long Generation, int ObjectId,
            string Ptid, string RoleName,
            WeakReference<TPlayObject> Player);
        private sealed class PendingCreditEpoch
        {
            public PendingCreditEpoch(PendingCreditRequest request)
            {
                Request = request;
                OutstandingCount = 1;
            }

            public PendingCreditRequest Request { get; }
            public int OutstandingCount { get; set; }
        }

        private sealed record PendingOpenDealRequest(long Generation, int ObjectId,
            string Ptid, string RoleName, WeakReference<TPlayObject> Player);

        private static readonly Encoding StrictGbk;
        // The 6108 OpenYB authority is not verified in the current deployment. Keep
        // the codec and identity-bound completion path intact, but fail closed until
        // a service handshake proves that ident 112/1112 is supported.
        private static readonly bool NativeOpenDealAuthorityEnabled = false;
        public static YbDbClient Instance { get; }

        private readonly object _stateLock = new();
        private readonly object _parserLock = new();
        private readonly object _responseLock = new();
        private readonly IClientScoket _socket;
        private readonly YbDbLegacy77StreamParser _parser = new();
        private Queue<QueuedResponse> _responses = new();
        private readonly ConcurrentQueue<QueuedSend> _outbound = new();
        private readonly Dictionary<string, List<PendingCreditEpoch>>
            _creditRequests = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PendingOpenDealRequest>
            _openDealRequests = new(StringComparer.OrdinalIgnoreCase);
        private readonly byte[] _sendAggregate = new byte[SendAggregateCapacity];

        private string _host = "127.0.0.1";
        private Socket _currentSocket;
        private int _areaId;
        private int _groupId;
        private int _started;
        private int _connected;
        private int _missedHeartbeatCount;
        private long _connectionGeneration;
        private uint _lastConnectionPulse;
        private uint _lastSendFlushTick;
        private long _lastErrorLogAt;

        static YbDbClient()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            StrictGbk = Encoding.GetEncoding(936,
                EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            Instance = new YbDbClient();
        }

        private YbDbClient()
        {
            _socket = new IClientScoket();
            _socket.OnConnected += SocketConnected;
            _socket.OnDisconnected += SocketDisconnected;
            _socket.OnError += SocketError;
            _socket.ReceivedDatagram += SocketRead;
        }

        public bool Connected
        {
            get
            {
                lock (_stateLock) return _connected != 0;
            }
        }

        public void Start()
        {
            var configuredHost = M2Share.g_Config?.sYBDBAddr;
            if (string.IsNullOrWhiteSpace(configuredHost))
                configuredHost = M2Share.g_Config?.sDBAddr;
            configuredHost = string.IsNullOrWhiteSpace(configuredHost)
                ? "127.0.0.1"
                : configuredHost.Trim();
            LoadServerIdentity(out var areaId, out var groupId);

            lock (_stateLock)
            {
                if (_started != 0) return;
                _host = string.IsNullOrWhiteSpace(configuredHost)
                    ? "127.0.0.1"
                    : configuredHost;
                _areaId = areaId;
                _groupId = groupId;
                _socket.Host = _host;
                _socket.Port = ServicePort;
                _lastConnectionPulse = 0;
                _lastSendFlushTick = 0;
                _missedHeartbeatCount = 0;
                _connected = 0;
                _currentSocket = null;
                _started = 1;
                lock (_parserLock) _parser.Reset();
                ClearQueue(_outbound);
                ClearResponses();
                _creditRequests.Clear();
                _openDealRequests.Clear();
            }

            Pulse();
        }

        internal bool TryRequestOpenDeal(TPlayObject player)
        {
            if (!NativeOpenDealAuthorityEnabled || player == null) return false;

            var identity = new YbDbLegacy77Identity
            {
                Field0 = player.m_sUserID,
                Field11 = player.m_sUserID,
                RoleName = player.m_sCharName,
                Field48 = player.m_sIPaddr
            };
            if (!YbDbOpenDealProtocol.TryCreateRequest(identity,
                    out var request, out var requestError))
            {
                M2Share.ErrorMessage("[YBDB] OpenDeal 身份组包失败: " + requestError);
                return false;
            }
            if (!YbDbLegacy77Codec.TryEncode(request,
                    out var frame, out var frameError))
            {
                M2Share.ErrorMessage("[YBDB] OpenDeal 帧组包失败: " + frameError);
                return false;
            }
            if (!YbDbLegacy77Codec.TryDecodeShortString(request.Payload,
                    YbDbLegacy77Codec.IdentityRoleNameOffset,
                    YbDbLegacy77Codec.IdentityRoleNameCapacity,
                    out var wireRoleName, out _))
                return false;

            lock (_stateLock)
            {
                if (_started == 0 || _connected == 0 || _currentSocket == null)
                    return false;

                _openDealRequests[wireRoleName] = new PendingOpenDealRequest(
                    _connectionGeneration, player.ObjectId, player.m_sUserID,
                    wireRoleName, new WeakReference<TPlayObject>(player));
                _outbound.Enqueue(new QueuedSend(_connectionGeneration, frame));
            }
            return true;
        }

        public void Stop()
        {
            Socket socket;
            lock (_stateLock)
            {
                if (_started == 0) return;
                _started = 0;
                _connected = 0;
                _connectionGeneration++;
                _missedHeartbeatCount = 0;
                _lastConnectionPulse = 0;
                _lastSendFlushTick = 0;
                socket = _currentSocket;
                _currentSocket = null;
                lock (_parserLock) _parser.Reset();
                ClearQueue(_outbound);
                ClearResponses();
                _creditRequests.Clear();
                _openDealRequests.Clear();
            }

            if (socket == null)
                _socket.Disconnect();
            else
                _socket.Disconnect(socket);
        }

        public void Pulse()
        {
            var now = unchecked((uint)Environment.TickCount);
            string host = null;
            Socket currentSocket = null;
            long generation = 0;
            var connect = false;
            var heartbeat = false;
            var flush = false;
            var disconnect = false;
            lock (_stateLock)
            {
                if (_started == 0) return;
                if (_connected != 0)
                {
                    currentSocket = _currentSocket;
                    generation = _connectionGeneration;
                }
                if (unchecked(now - _lastConnectionPulse)
                    >= ConnectionPulseIntervalMilliseconds)
                {
                    _lastConnectionPulse = now;
                    if (_connected == 0)
                    {
                        host = _host;
                        connect = true;
                    }
                    else
                    {
                        heartbeat = true;
                    }
                }
                if (unchecked(now - _lastSendFlushTick)
                    >= SendFlushIntervalMilliseconds)
                {
                    _lastSendFlushTick = now;
                    flush = true;
                }
            }

            if (connect)
            {
                try
                {
                    _socket.ConnectReplacingPending(host, ServicePort);
                }
                catch (Exception ex)
                {
                    LogConnectionError("连接失败: " + ex.Message);
                }
                return;
            }

            if (heartbeat && EnqueueFrame(0, M2Share.nServerIndex + 1,
                    100, Array.Empty<byte>(), currentSocket, generation))
            {
                lock (_stateLock)
                {
                    if (IsCurrentSessionLocked(currentSocket, generation))
                    {
                        if (++_missedHeartbeatCount >= HeartbeatDisconnectThreshold)
                        {
                            _missedHeartbeatCount = 0;
                            _connected = 0;
                            disconnect = true;
                        }
                    }
                }
            }

            if (flush) DrainOutbound(currentSocket, generation);
            if (disconnect) _socket.Disconnect(currentSocket);
        }

        public bool RequestClearNickLinfu(TPlayObject player)
        {
            if (player == null) return false;

            var identity = new YbDbLegacy77Identity
            {
                Field0 = player.m_sUserID,
                Field11 = player.m_sUserID,
                RoleName = player.m_sCharName,
                Field48 = player.m_sIPaddr
            };
            if (!YbDbLegacy77Codec.TryEncodeNativeIdentity(identity,
                    out var payload, out var identityError))
            {
                M2Share.ErrorMessage("[YBDB] ClearNickLinfu 身份组包失败: "
                    + identityError);
                return false;
            }
            if (!YbDbLegacy77Codec.TryEncode(
                    new YbDbLegacy77Frame(5, 0, 303, payload),
                    out var frame, out var frameError))
            {
                M2Share.ErrorMessage("[YBDB] ClearNickLinfu 帧组包失败: "
                    + frameError);
                return false;
            }

            lock (_stateLock)
            {
                if (_started == 0 || _connected == 0 || _currentSocket == null)
                    return false;
                _outbound.Enqueue(new QueuedSend(_connectionGeneration, frame));
            }
            return true;
        }

        public bool RequestCredit(TPlayObject player)
        {
            if (player == null) return false;

            var identity = new YbDbLegacy77Identity
            {
                Field0 = player.m_sUserID,
                Field11 = player.m_sUserID,
                RoleName = player.m_sCharName,
                Field48 = player.m_sIPaddr
            };
            if (!YbDbCreditProtocol.TryCreateRefreshRequest(identity,
                    unchecked((ushort)player.m_nPayMent),
                    player.m_boNativeFirstUsedGiftQualified,
                    out var request, out var requestError))
            {
                M2Share.ErrorMessage("[YBDB] RefreshCredit 身份组包失败: "
                    + requestError);
                return false;
            }
            if (!YbDbLegacy77Codec.TryEncode(request,
                    out var frame, out var frameError))
            {
                M2Share.ErrorMessage("[YBDB] RefreshCredit 帧组包失败: "
                    + frameError);
                return false;
            }
            if (!YbDbLegacy77Codec.TryDecodeShortString(request.Payload,
                    YbDbLegacy77Codec.IdentityRoleNameOffset,
                    YbDbLegacy77Codec.IdentityRoleNameCapacity,
                    out var wireRoleName, out _))
                return false;

            return EnqueueCreditRequest(player, wireRoleName, frame);
        }

        public bool RequestInitialCredit(TPlayObject player)
        {
            if (player == null) return false;

            var identity = new YbDbLegacy77Identity
            {
                Field0 = player.m_sUserID,
                Field11 = player.m_sUserID,
                RoleName = player.m_sCharName,
                Field48 = player.m_sIPaddr
            };
            if (!YbDbCreditProtocol.TryCreateInitialRequest(identity,
                    unchecked((ushort)player.m_nPayMent),
                    player.m_boNativeFirstUsedGiftQualified,
                    out var request, out var requestError))
            {
                M2Share.ErrorMessage("[YBDB] 初始Credit身份组包失败: "
                    + requestError);
                return false;
            }
            if (!YbDbLegacy77Codec.TryEncode(request,
                    out var frame, out var frameError))
            {
                M2Share.ErrorMessage("[YBDB] 初始Credit帧组包失败: "
                    + frameError);
                return false;
            }
            if (!YbDbLegacy77Codec.TryDecodeShortString(request.Payload,
                    YbDbLegacy77Codec.IdentityRoleNameOffset,
                    YbDbLegacy77Codec.IdentityRoleNameCapacity,
                    out var wireRoleName, out _))
                return false;

            return EnqueueCreditRequest(player, wireRoleName, frame);
        }

        private bool EnqueueCreditRequest(TPlayObject player,
            string wireRoleName, byte[] frame)
        {
            lock (_stateLock)
            {
                if (_started == 0 || _connected == 0 || _currentSocket == null)
                    return false;

                var generation = _connectionGeneration;
                if (!_creditRequests.TryGetValue(wireRoleName, out var requests))
                {
                    requests = new List<PendingCreditEpoch>();
                    _creditRequests.Add(wireRoleName, requests);
                }

                if (requests.Count != 0 && IsSameCreditRequestIdentity(
                        requests[^1].Request, generation, player))
                {
                    if (requests[^1].OutstandingCount == int.MaxValue)
                        return false;
                    requests[^1].OutstandingCount++;
                }
                else
                {
                    if (requests.Count >= MaxPendingCreditEpochsPerRole)
                        return false;
                    requests.Add(new PendingCreditEpoch(new PendingCreditRequest(
                        generation, player.ObjectId,
                        player.m_sUserID ?? string.Empty,
                        player.m_sCharName ?? string.Empty,
                        new WeakReference<TPlayObject>(player))));
                }

                _outbound.Enqueue(new QueuedSend(generation, frame));
            }
            return true;
        }

        private static bool IsSameCreditRequestIdentity(
            PendingCreditRequest request, long generation, TPlayObject player)
        {
            return request.Generation == generation
                   && request.ObjectId == player.ObjectId
                   && request.Player.TryGetTarget(out var requestedPlayer)
                   && ReferenceEquals(player, requestedPlayer)
                   && string.Equals(request.RoleName, player.m_sCharName,
                       StringComparison.OrdinalIgnoreCase)
                   && string.Equals(request.Ptid, player.m_sUserID,
                       StringComparison.OrdinalIgnoreCase);
        }

        public bool RequestQuestDiamond(TPlayObject player, int amount)
        {
            if (player == null) return false;

            var identity = new YbDbLegacy77Identity
            {
                Field0 = player.m_sUserID,
                Field11 = player.m_sUserID,
                RoleName = player.m_sCharName,
                Field48 = player.m_sIPaddr
            };
            if (!YbDbLegacy77Codec.TryEncodeNativeIdentity(identity,
                    out var payload, out var identityError))
            {
                M2Share.ErrorMessage("[YBDB] ClientQuestGetDiam 身份组包失败: "
                    + identityError);
                return false;
            }
            if (!YbDbLegacy77Codec.TryEncode(
                    new YbDbLegacy77Frame(0, amount, 122, payload),
                    out var frame, out var frameError))
            {
                M2Share.ErrorMessage("[YBDB] ClientQuestGetDiam 帧组包失败: "
                    + frameError);
                return false;
            }

            lock (_stateLock)
            {
                if (_started == 0 || _connected == 0 || _currentSocket == null)
                    return false;
                _outbound.Enqueue(new QueuedSend(_connectionGeneration, frame));
            }
            return true;
        }

        public bool RequestLingFuAccounting(TPlayObject player)
        {
            if (player == null
                || !player.TryGetNativeLingFuReasonBuckets(out var buckets))
                return false;

            var identity = new YbDbLegacy77Identity
            {
                Field0 = player.m_sUserID,
                Field11 = player.m_sUserID,
                RoleName = player.m_sCharName,
                Field48 = player.m_sIPaddr
            };
            if (!YbDbLegacy77Codec.TryEncodeNativeIdentity(identity,
                    out var identityPayload, out var identityError))
            {
                M2Share.ErrorMessage("[YBDB] DecLF 统计身份组包失败: "
                    + identityError);
                return false;
            }

            var payload = new byte[108];
            identityPayload.CopyTo(payload, 0);
            payload[64] = player.m_btJob;
            payload[65] = unchecked((byte)player.m_Abil.Level);
            payload[66] = unchecked((byte)player.m_nPayMent);
            payload[67] = unchecked((byte)player.m_nPayMode);
            for (var i = 0; i < buckets.Length; i++)
                BinaryPrimitives.WriteInt32LittleEndian(
                    payload.AsSpan(68 + i * sizeof(int), sizeof(int)), buckets[i]);

            if (!YbDbLegacy77Codec.TryEncode(
                    new YbDbLegacy77Frame(0, 0, 132, payload),
                    out var frame, out var frameError))
            {
                M2Share.ErrorMessage("[YBDB] DecLF 统计帧组包失败: "
                    + frameError);
                return false;
            }

            lock (_stateLock)
            {
                if (_started == 0 || _connected == 0 || _currentSocket == null)
                    return false;
                _outbound.Enqueue(new QueuedSend(_connectionGeneration, frame));
            }
            return true;
        }

        internal bool EnqueueNativeDbTransactionAck(
            YbDbLegacy77Frame frame)
        {
            if (frame == null || frame.QueryId !=
                    NativeType1YbTransactionAck.YbDbQueryId
                || frame.Payload.Length != 0
                || frame.Ident is not NativeType1YbTransactionAck.SuccessIdent
                    and not NativeType1YbTransactionAck.FailureIdent)
                return false;

            Socket currentSocket;
            long generation;
            lock (_stateLock)
            {
                if (_started == 0 || _connected == 0
                    || _currentSocket == null)
                    return false;
                currentSocket = _currentSocket;
                generation = _connectionGeneration;
            }

            return EnqueueFrame(frame.QueryId, frame.Param, frame.Ident,
                frame.Payload, currentSocket, generation);
        }

        internal bool TryEnqueueNativeItemMovementSms(byte[] payload)
        {
            if (payload == null
                || payload.Length != TBaseObject.NativeItemMovementSmsPayloadSize)
                return false;

            Socket currentSocket;
            long generation;
            lock (_stateLock)
            {
                if (_started == 0 || _connected == 0
                    || _currentSocket == null)
                    return false;
                currentSocket = _currentSocket;
                generation = _connectionGeneration;
            }

            return EnqueueFrame(0, 0,
                TBaseObject.NativeItemMovementSmsManagerIdent, payload,
                currentSocket, generation);
        }

        // Called only by the UserEngine thread.
        public void ProcessCompletions()
        {
            Queue<QueuedResponse> responses;
            lock (_responseLock)
            {
                responses = _responses;
                _responses = new Queue<QueuedResponse>();
            }

            while (responses.Count != 0)
            {
                var queued = responses.Dequeue();
                try
                {
                    lock (_stateLock)
                    {
                        if (!IsCurrentSessionLocked(_currentSocket, queued.Generation))
                            continue;
                        if (queued.Frame.Ident == 1100)
                        {
                            _missedHeartbeatCount = 0;
                            continue;
                        }
                    }
                    var frame = queued.Frame;
                    if (frame.Ident == YbDbCreditProtocol.ResponseIdent)
                    {
                        ProcessCreditResponse(frame, queued.Generation);
                        continue;
                    }
                    if (frame.Ident == YbDbOpenDealProtocol.ResponseIdent)
                    {
                        ProcessOpenDealResponse(frame, queued.Generation);
                        continue;
                    }
                    if (frame.Ident == YbDbForgeModeProtocol.ResponseIdent)
                    {
                        if (!YbDbForgeModeProtocol.TryDecodeResponse(frame,
                                out var forgeMode, out _))
                            continue;
                        M2Share.g_boYbDoubleForge = forgeMode.DoubleForging;
                        M2Share.MainOutMessage(forgeMode.ConsoleMessage,
                            messageColor: ConsoleColor.Green);
                        continue;
                    }
                    if (frame.Ident == 1132)
                    {
                        if (frame.QueryId != 1
                            || frame.Payload.Length != YbDbLegacy77Codec.IdentitySize
                            || !TryReadRoleName(frame.Payload, out var accountingRoleName))
                            continue;
                        M2Share.UserEngine?.GetPlayObject(accountingRoleName)
                            ?.ClearNativeLingFuReasonBuckets();
                        continue;
                    }
                    if (frame.Ident != 1303
                        || frame.Payload.Length != YbDbLegacy77Codec.IdentitySize
                        || frame.QueryId is not 5 and not 6)
                        continue;
                    if (!TryReadRoleName(frame.Payload, out var roleName)) continue;

                    var player = M2Share.UserEngine.GetPlayObject(roleName);
                    if (player == null) continue;

                    var text = frame.QueryId == 5
                        ? "成功清除所有的圣殿灵符"
                        : "成功清除所有的圣域灵符";
                    player.SendMsg(player, Grobal2.RM_SYSMESSAGE,
                        0, 0xDB, 0xFF, 0, text);
                }
                catch (Exception ex)
                {
                    M2Share.ErrorMessage(
                        $"[Exception]: GoldIngot Cmd={queued.Frame.Ident}  {ex.Message}");
                }
            }
        }

        private void SocketConnected(object sender, DSCClientConnectedEventArgs e)
        {
            var socket = e?.socket;
            long generation = 0;
            var accepted = false;
            try
            {
                lock (_stateLock)
                {
                    if (_started != 0 && socket != null
                        && _socket.IsCurrentConnection(socket))
                    {
                        lock (_parserLock) _parser.Reset();
                        ClearQueue(_outbound);
                        ClearResponses();
                        _creditRequests.Clear();
                        _openDealRequests.Clear();
                        generation = ++_connectionGeneration;
                        _currentSocket = socket;
                        _missedHeartbeatCount = 0;
                        _connected = 1;
                        accepted = true;
                    }
                }

                if (!accepted)
                {
                    _socket.Disconnect(socket);
                    return;
                }
                if (!IsCurrentSession(socket, generation)) return;
                M2Share.MainOutMessage($"YB数据库服务器[{_host}:{ServicePort}]连接成功...",
                    messageColor: ConsoleColor.Green);

                if (!EnqueueFrame(0, M2Share.nServerIndex + 1, 100,
                        Array.Empty<byte>(), socket, generation))
                    return;
                if (!EnqueueFrame(_areaId, _groupId, 400,
                        Array.Empty<byte>(), socket, generation))
                    return;
                var forgeMode = YbDbForgeModeProtocol.CreateRequest(
                    M2Share.g_boYbDoubleForge);
                EnqueueFrame(forgeMode.QueryId, forgeMode.Param, forgeMode.Ident,
                    forgeMode.Payload, socket, generation);
            }
            catch (Exception ex)
            {
                LogConnectionError("连接初始化失败: " + ex.Message);
                _socket.Disconnect(socket);
            }
        }

        private void SocketDisconnected(object sender, DSCClientConnectedEventArgs e)
        {
            var shouldLog = false;
            try
            {
                lock (_stateLock)
                {
                    var disconnectedSocket = e?.socket;
                    if (disconnectedSocket != null && _currentSocket != null
                        && !ReferenceEquals(disconnectedSocket, _currentSocket))
                        return;
                    if (disconnectedSocket == null && _currentSocket != null)
                        return;

                    var wasConnected = _connected != 0;
                    _connected = 0;
                    _currentSocket = null;
                    _connectionGeneration++;
                    _missedHeartbeatCount = 0;
                    ClearQueue(_outbound);
                    lock (_parserLock) _parser.Reset();
                    ClearResponses();
                    _creditRequests.Clear();
                    _openDealRequests.Clear();
                    shouldLog = wasConnected && _started != 0;
                }
                if (shouldLog)
                    M2Share.ErrorMessage($"YB数据库服务器[{_host}:{ServicePort}]断开连接...");
            }
            catch (Exception ex)
            {
                LogConnectionError("断线处理失败: " + ex.Message);
            }
        }

        private void SocketError(object sender, DSCClientErrorEventArgs e)
        {
            try
            {
                var reason = ((SocketError)e.ErrorCode) switch
                {
                    System.Net.Sockets.SocketError.ConnectionRefused => "拒绝连接",
                    System.Net.Sockets.SocketError.ConnectionReset => "关闭连接",
                    System.Net.Sockets.SocketError.TimedOut => "连接超时",
                    _ => ((SocketError)e.ErrorCode).ToString()
                };
                LogConnectionError(reason);
            }
            catch (Exception ex)
            {
                LogConnectionError("错误回调失败: " + ex.Message);
            }
        }

        private void SocketRead(object sender, DSCClientDataInEventArgs e)
        {
            Exception parseException = null;
            try
            {
                if (e?.Buff == null || e.BuffLen <= 0) return;
                lock (_stateLock)
                {
                    if (_started == 0 || _connected == 0
                        || !ReferenceEquals(e.Socket, _currentSocket)
                        || !_socket.IsCurrentConnection(e.Socket))
                        return;
                    var generation = _connectionGeneration;
                    lock (_parserLock)
                    {
                        try
                        {
                            _parser.Append(e.Buff.AsSpan(0, e.BuffLen), frame =>
                            {
                                lock (_responseLock)
                                    _responses.Enqueue(new QueuedResponse(generation, frame));
                            });
                        }
                        catch (Exception ex)
                        {
                            parseException = ex;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                parseException = ex;
            }
            if (parseException == null) return;
            LogConnectionError("返回帧解析失败: " + parseException.Message);
            _socket.Disconnect(e?.Socket);
        }

        private void DrainOutbound(Socket currentSocket, long generation)
        {
            var aggregateLength = 0;
            var pending = _outbound.Count;
            for (var count = 0; count < pending
                && _outbound.TryDequeue(out var queued); count++)
            {
                if (queued.Generation != generation
                    || !IsCurrentSession(currentSocket, generation)) continue;
                if (queued.Data.Length >= SendAggregateCapacity)
                {
                    FlushSendAggregate(currentSocket, ref aggregateLength);
                    _socket.QueueSend(queued.Data, currentSocket);
                    continue;
                }
                if (aggregateLength + queued.Data.Length > SendAggregateCapacity)
                    FlushSendAggregate(currentSocket, ref aggregateLength);
                Buffer.BlockCopy(queued.Data, 0, _sendAggregate,
                    aggregateLength, queued.Data.Length);
                aggregateLength += queued.Data.Length;
            }
            FlushSendAggregate(currentSocket, ref aggregateLength);
        }

        private void FlushSendAggregate(Socket currentSocket, ref int length)
        {
            if (length == 0) return;
            var data = new byte[length];
            Buffer.BlockCopy(_sendAggregate, 0, data, 0, length);
            length = 0;
            _socket.QueueSend(data, currentSocket);
        }

        public void FlushPendingSendsSynchronously()
        {
            Socket currentSocket;
            long generation;
            lock (_stateLock)
            {
                if (_started == 0 || _connected == 0 || _currentSocket == null)
                    return;
                currentSocket = _currentSocket;
                generation = _connectionGeneration;
            }

            var pending = _outbound.Count;
            for (var count = 0; count < pending
                 && _outbound.TryDequeue(out var queued); count++)
            {
                if (queued.Generation != generation
                    || !IsCurrentSession(currentSocket, generation))
                    continue;
                _socket.Send(queued.Data, currentSocket);
            }
        }

        private bool EnqueueFrame(int queryId, int param, ushort ident,
            byte[] payload, Socket currentSocket, long generation)
        {
            if (!YbDbLegacy77Codec.TryEncode(
                    new YbDbLegacy77Frame(queryId, param, ident, payload),
                    out var data, out var error))
            {
                M2Share.ErrorMessage("[YBDB] 组包失败: " + error);
                return false;
            }

            lock (_stateLock)
            {
                if (!IsCurrentSessionLocked(currentSocket, generation)) return false;
                _outbound.Enqueue(new QueuedSend(generation, data));
                return true;
            }
        }

        private bool IsCurrentSession(Socket socket, long generation)
        {
            lock (_stateLock) return IsCurrentSessionLocked(socket, generation);
        }

        private bool IsCurrentSessionLocked(Socket socket, long generation) =>
            _started != 0 && _connected != 0
            && generation == _connectionGeneration
            && ReferenceEquals(socket, _currentSocket);

        private static bool TryReadRoleName(ReadOnlySpan<byte> payload,
            out string roleName)
        {
            roleName = string.Empty;
            if (payload.Length != YbDbLegacy77Codec.IdentitySize) return false;
            var length = payload[YbDbLegacy77Codec.IdentityRoleNameOffset];
            if (length > YbDbLegacy77Codec.IdentityRoleNameCapacity) return false;
            try
            {
                roleName = StrictGbk.GetString(payload.Slice(
                    YbDbLegacy77Codec.IdentityRoleNameOffset + 1, length));
                return true;
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
        }

        private void ProcessCreditResponse(YbDbLegacy77Frame frame,
            long generation)
        {
            if (!YbDbCreditProtocol.TryDecodeResponse(frame,
                    out var snapshot, out _))
                return;
            if (!TryTakeCreditRequest(snapshot.RoleName, generation,
                    out var request))
                return;

            var player = M2Share.UserEngine?.GetPlayObject(snapshot.RoleName);
            if (player == null || player.m_boGhost
                || player.ObjectId != request.ObjectId
                || !request.Player.TryGetTarget(out var requestedPlayer)
                || !ReferenceEquals(player, requestedPlayer)
                || !string.Equals(player.m_sCharName, request.RoleName,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(player.m_sUserID, request.Ptid,
                    StringComparison.OrdinalIgnoreCase))
                return;

            player.ApplyNativeYb1103Snapshot(snapshot.CurrentYuanbao,
                snapshot.TotalConsumed, snapshot.RemainingSeconds,
                snapshot.DividendConsumed, snapshot.ResponseParamIsOne);
        }

        private void ProcessOpenDealResponse(YbDbLegacy77Frame frame,
            long generation)
        {
            if (!YbDbOpenDealProtocol.TryDecodeResponse(frame,
                    out var result, out _))
                return;
            if (!TryTakeOpenDealRequest(result.RoleName, generation,
                    out var request))
                return;

            var player = M2Share.UserEngine?.GetPlayObject(result.RoleName);
            if (player == null || player.m_boGhost
                || player.ObjectId != request.ObjectId
                || !request.Player.TryGetTarget(out var requestedPlayer)
                || !ReferenceEquals(player, requestedPlayer)
                || !string.Equals(player.m_sCharName, request.RoleName,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(player.m_sUserID, request.Ptid,
                    StringComparison.OrdinalIgnoreCase))
                return;

            player.ApplyNativeOpenYbDealDbResult(result);
        }

        private bool TryTakeOpenDealRequest(string roleName, long generation,
            out PendingOpenDealRequest request)
        {
            request = null;
            lock (_stateLock)
            {
                if (generation != _connectionGeneration
                    || !_openDealRequests.TryGetValue(roleName, out var pending))
                    return false;
                if (pending.Generation != generation)
                    return false;
                _openDealRequests.Remove(roleName);
                request = pending;
                return true;
            }
        }

        private bool TryTakeCreditRequest(string roleName, long generation,
            out PendingCreditRequest request)
        {
            request = null;
            lock (_stateLock)
            {
                if (generation != _connectionGeneration
                    || !_creditRequests.TryGetValue(roleName, out var requests))
                    return false;

                while (requests.Count != 0
                       && requests[0].Request.Generation != generation)
                    requests.RemoveAt(0);
                if (requests.Count != 0)
                {
                    var epoch = requests[0];
                    request = epoch.Request;
                    if (--epoch.OutstandingCount == 0)
                        requests.RemoveAt(0);
                }
                if (requests.Count == 0) _creditRequests.Remove(roleName);
                return request != null;
            }
        }

        private static void LoadServerIdentity(out int areaId, out int groupId)
        {
            areaId = 0;
            groupId = 0;
            try
            {
                var shareDirectory = Path.GetFullPath(Path.Combine(M2Share.sRootPath,
                    M2Share.g_Config.sBaseDir));
                var fileName = Path.Combine(shareDirectory, "serverinfo.ini");
                if (!File.Exists(fileName) || new FileInfo(fileName).Length == 0) return;
                var serverInfo = new ServerInfoLoader(fileName);
                areaId = serverInfo.AreaID;
                groupId = serverInfo.GroupID;
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage("[YBDB] 读取 serverinfo.ini 失败: " + ex.Message);
            }
        }

        private void LogConnectionError(string detail)
        {
            var now = Environment.TickCount64;
            lock (_stateLock)
            {
                if (_lastErrorLogAt != 0 && now - _lastErrorLogAt < 30_000) return;
                _lastErrorLogAt = now;
            }
            M2Share.ErrorMessage($"YB数据库服务器[{_host}:{ServicePort}]{detail}...");
        }

        private void ClearResponses()
        {
            lock (_responseLock) _responses.Clear();
        }

        private static void ClearQueue<T>(ConcurrentQueue<T> queue)
        {
            while (queue.TryDequeue(out _)) { }
        }

        public void Dispose()
        {
            Stop();
            _socket.OnConnected -= SocketConnected;
            _socket.OnDisconnected -= SocketDisconnected;
            _socket.OnError -= SocketError;
            _socket.ReceivedDatagram -= SocketRead;
        }
    }
}
