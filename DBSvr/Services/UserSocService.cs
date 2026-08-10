using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;
using SystemModule;
using SystemModule.Common;
using SystemModule.Packet;
using SystemModule.Sockets;
using DBSvr.Core;

namespace DBSvr
{
    /// <summary>
    /// 网关连接服务 (端口 5100)。
    /// 对应 Delphi 原版角色网关处理逻辑。
    /// 处理客户端选角流程: 查询/创建/删除/恢复/选择角色。
    /// </summary>
    public class UserSocService
    {
        private const int MaxGateConnections = 64;
        private const int MaxUserFrameLength = 64 * 1024;
        private const ushort SoftCloseQueryParam = 1;
        private const int MobileAdmissionPaymentState = 0;
        private const int MobileAdmissionPayMode = 5;
        private readonly IList<TGateInfo> _gateList;
        private Dictionary<string, int> _mapList;
        private readonly IPlayDataService _playDataService;
        private readonly IPlayRecordService _playRecordService;
        private readonly SensitiveWordFilter _sensitiveWordFilter;
        private readonly WhitelistService _whitelistService;
        private readonly ISocketServer _userSocket;
        private readonly LoginSvrService _loginService;
        private readonly GameSocService _gameSocService;
        private readonly ConfigManager _configManager;
        private readonly NativeUserAdmissionControl _nativeAdmission;
        // 角色改名三库级联（原版 fn_5A8DDC 主档 + fn_5A923C 的 22 条级联）
        private readonly INativeRenameCascadeService _renameCascade;
        private readonly object _gateLock = new();

        private static readonly ushort[] SupportedUserCommands =
        {
            Grobal2.CM_QUERYCHR,
            Grobal2.CM_NEWCHR,
            Grobal2.CM_DELCHR,
            Grobal2.CM_SELCHR,
            Grobal2.CM_QUERYDELCHR,
            Grobal2.CM_RESDELCHR,
            4004, // 手游认证
            4039, // CM_SELCHR_EXIT
            1018, // CM_LOGINNOTICEOK
            // 0xFB0 角色改名。原版内层派发 idx = 4016 - 0xFAC = 4 -> grp 5 ->
            // 0x5CE404 -> call fn_5CD2EC。本白名单是 fail-closed，
            // 不登记则请求根本进不到 switch，所以两处都必须加。
            Grobal2.CM_RENAMECHR4016,
        };

        public UserSocService(LoginSvrService loginService, IPlayRecordService playRecordService,
            IPlayDataService playDataService,
            SensitiveWordFilter sensitiveWordFilter, WhitelistService whitelistService,
            ConfigManager configManager, GameSocService gameSocService,
            NativeUserAdmissionControl nativeAdmission,
            INativeRenameCascadeService renameCascade)
        {
            _renameCascade = renameCascade
                ?? throw new ArgumentNullException(nameof(renameCascade));
            _loginService = loginService;
            _gameSocService = gameSocService;
            _playRecordService = playRecordService;
            _playDataService = playDataService;
            _sensitiveWordFilter = sensitiveWordFilter;
            _whitelistService = whitelistService;
            _configManager = configManager;
            _nativeAdmission = nativeAdmission
                ?? throw new ArgumentNullException(nameof(nativeAdmission));
            _gateList = new List<TGateInfo>();
            _mapList = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _userSocket = new ISocketServer(MaxGateConnections, 1024);
            _userSocket.OnClientConnect += UserSocketClientConnect;
            _userSocket.OnClientDisconnect += UserSocketClientDisconnect;
            _userSocket.OnClientRead += UserSocketClientRead;
            _userSocket.OnClientError += (s, e) => { Debug.WriteLine("UserSoc OnError: " + e?.ToString()); };
            _userSocket.Init();
            _sensitiveWordFilter.Load();
            _whitelistService.Load();
            LoadServerInfo();
            LoadChrNameList("DenyChrName.txt");
            _nativeAdmission.Attach(SnapshotNativeUserIps,
                DisconnectNativeGateByAddress, DrainNativeAdmissionQueue,
                DisconnectNativeUserByAccount,
                UpdateNativeOnlineAccountText,
                UpdateNativeOnlineAccountLoginTime);
        }

        private IReadOnlyList<string> SnapshotNativeUserIps()
        {
            var result = new List<string>();
            lock (_gateLock)
                foreach (var gate in _gateList)
                {
                    if (gate?.UserList == null) continue;
                    lock (gate.UserList)
                        foreach (var user in gate.UserList)
                            if (user != null) result.Add(user.sUserIPaddr ?? string.Empty);
                }
            return result;
        }

        private void DisconnectNativeGateByAddress(string gateAddress)
        {
            if (string.IsNullOrEmpty(gateAddress)) return;
            var targets = new List<TUserInfo>();
            lock (_gateLock)
                foreach (var gate in _gateList)
                {
                    if (gate?.UserList == null
                        || string.IsNullOrEmpty(gate.sGateaddr)
                        || !gate.sGateaddr.StartsWith(gateAddress,
                            StringComparison.Ordinal))
                        continue;
                    lock (gate.UserList)
                        foreach (var user in gate.UserList)
                            if (user != null)
                                targets.Add(user);
                }
            foreach (var target in targets)
            {
                try
                {
                    SendEncodedPacket(target,
                        Grobal2.SM_OUTOFCONNECTION_4018, 0, 0, 0, 0, null);
                }
                catch { }
            }
        }

        private void DisconnectNativeUserByAccount(byte[] accountBytes)
        {
            var key = NativeType3Protocol.NormalizePtidKey(
                accountBytes ?? Array.Empty<byte>());
            if (string.IsNullOrEmpty(key)) return;
            (TGateInfo Gate, TUserInfo User)? target = null;
            lock (_gateLock)
                foreach (var gate in _gateList)
                {
                    if (gate?.UserList == null) continue;
                    lock (gate.UserList)
                        foreach (var user in gate.UserList)
                        {
                            if (user == null) continue;
                            var userKey = NativeType3Protocol.NormalizePtidKey(
                                LegacyGbkText.Encode(user.sAccount ?? string.Empty));
                            if (userKey == key)
                            {
                                target = (gate, user);
                                break;
                            }
                        }
                    if (target.HasValue) break;
                }
            if (!target.HasValue) return;
            try
            {
                SendEncodedPacket(target.Value.User,
                    Grobal2.SM_OUTOFCONNECTION_4018, 0, 0, 0, 0, null);
            }
            catch { }
            var gateInfo = target.Value.Gate;
            lock (gateInfo.UserList)
                CloseUser(target.Value.User.sConnID, ref gateInfo);
        }

        private void UpdateNativeOnlineAccountText(byte[] accountBytes,
            string text)
        {
            lock (_gateLock)
                foreach (var gate in _gateList)
                {
                    if (gate?.UserList == null) continue;
                    lock (gate.UserList)
                        foreach (var user in gate.UserList)
                        {
                            if (user == null
                                || !NativeOnlineAccountProtocol.IsAccountMatch(
                                    accountBytes, user.sAccount))
                                continue;
                            user.NativeText102 = text ?? string.Empty;
                            return;
                        }
                }
        }

        private void UpdateNativeOnlineAccountLoginTime(byte[] accountBytes,
            ushort flag)
        {
            var found = false;
            var queryId = 0;
            var bits = 0L;
            lock (_gateLock)
                foreach (var gate in _gateList)
                {
                    if (gate?.UserList == null) continue;
                    lock (gate.UserList)
                        foreach (var user in gate.UserList)
                        {
                            if (user == null
                                || !NativeOnlineAccountProtocol.IsAccountMatch(
                                    accountBytes, user.sAccount))
                                continue;
                            bits = NativeOnlineAccountProtocol
                                .CreateLoginDateTimeBits(flag);
                            Interlocked.Exchange(
                                ref user.NativeLoginDateTimeBits, bits);
                            queryId = user.NativeQueryId;
                            found = true;
                            break;
                        }
                    if (found) break;
                }
            if (found)
                _loginService.SetPendingNativeLoginDateTimeBits(queryId, bits);
        }

        private void DrainNativeAdmissionQueue()
        {
            // The admission queue producer thresholds are still being closed.
            // Existing live users are not queue entries and must not be drained.
        }

        public void Start()
        {
            DBShare.MainOutMessage("=== DBSvr v3.0 [Full Rewrite] ===");
            _userSocket.Start(DBShare.g_sGateAddr, DBShare.g_nGatePort);
            DBShare.MainOutMessage($"数据库服务[{DBShare.g_sGateAddr}:{DBShare.g_nGatePort}]已启动.等待链接...");
        }

        public void Stop()
        {
            _userSocket.Shutdown();
            lock (_gateLock)
            {
                foreach (var gate in _gateList)
                {
                    if (gate?.UserList == null) continue;
                    lock (gate.UserList) gate.UserList.Clear();
                }
                _gateList.Clear();
            }
        }

        public int GetUserCount()
        {
            lock (_gateLock)
            {
                int nUserCount = 0;
                for (var i = 0; i < _gateList.Count; i++)
                {
                    var users = _gateList[i]?.UserList;
                    if (users == null) continue;
                    lock (users) nUserCount += users.Count;
                }
                return nUserCount;
            }
        }

        // ===================== 消息消费 (异步) =====================

        public Task StartConsumer() => Task.CompletedTask;

        private void ProcessGateMsg(TGateInfo gateInfo, string sText)
        {
            if (gateInfo?.UserList == null) return;
            lock (gateInfo.UserList)
            {
            string sData = string.Empty;
            string sConnId = string.Empty;

            while (sText.IndexOf("$", StringComparison.Ordinal) > 0)
            {
                sText = HUtil32.ArrestStringEx(sText, "%", "$", ref sData);
                if (string.IsNullOrEmpty(sData)) break;

                char type = sData[0];
                sData = sData.Substring(1);

                switch (type)
                {
                    case '-':
                        SendKeepAlivePacket(gateInfo.Socket);
                        break;

                    case 'A': // 数据包
                        sData = HUtil32.GetValidStr3(sData, ref sConnId, HUtil32.Backslash);
                        for (var i = 0; i < gateInfo.UserList.Count; i++)
                        {
                            var userInfo = gateInfo.UserList[i];
                            if (userInfo?.sConnID == sConnId)
                            {
                                if (userInfo.sText.Length + sData.Length > MaxUserFrameLength)
                                {
                                    DBShare.MainOutMessage($"网关用户[{sConnId}]数据帧超过{MaxUserFrameLength}字节，连接已关闭.");
                                    CloseUser(sConnId, ref gateInfo);
                                    break;
                                }

                                userInfo.sText += sData;
                                while (userInfo.sText.IndexOf('!') >= 0)
                                {
                                    var bufferedLength = userInfo.sText.Length;
                                    ProcessUserMsg(gateInfo, ref userInfo);
                                    if (userInfo.sText.Length >= bufferedLength) break;
                                }
                                break;
                            }
                        }
                        break;

                    case 'O':
                    case 'K': // 新连接
                        sData = HUtil32.GetValidStr3(sData, ref sConnId, HUtil32.Backslash);
                        OpenUser(sConnId, sData, ref gateInfo);
                        break;

                    case 'X':
                    case 'L': // 断开
                        CloseUser(sData, ref gateInfo);
                        break;
                }
            }
            }
        }

        // ===================== Socket 事件 =====================

        private void UserSocketClientConnect(object sender, AsyncUserToken e)
        {
            string sIPaddr = e.RemoteIPaddr;
            if (!DBShare.CheckServerIP(sIPaddr))
            {
                DBShare.MainOutMessage("非法网关连接: " + sIPaddr);
                e.Socket.Close();
                return;
            }

            var gateInfo = new TGateInfo
            {
                Socket = e.Socket,
                sGateaddr = sIPaddr,
                UserList = new List<TUserInfo>(),
                dwTick10 = HUtil32.GetTickCount(),
                nGateID = DBShare.GetGateID(sIPaddr)
            };
            int gateIndex;
            lock (_gateLock)
            {
                _gateList.Add(gateInfo);
                gateIndex = _gateList.Count - 1;
            }
            DBShare.MainOutMessage($"角色网关[{gateIndex}]({e.RemoteIPaddr}:{e.RemotePort})已打开...");
        }

        private void UserSocketClientDisconnect(object sender, AsyncUserToken e)
        {
            lock (_gateLock)
            {
                for (var i = 0; i < _gateList.Count; i++)
                {
                    if (_gateList[i]?.Socket == e.Socket)
                    {
                        DBShare.MainOutMessage($"角色网关[{i}]({e.RemoteIPaddr}:{e.RemotePort})已关闭...");
                        var users = _gateList[i].UserList;
                        if (users != null) lock (users) users.Clear();
                        _gateList.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        private void UserSocketClientRead(object sender, AsyncUserToken e)
        {
            TGateInfo gateInfo = null;
            int gateIndex = -1;
            lock (_gateLock)
            {
                for (var i = 0; i < _gateList.Count; i++)
                {
                    if (_gateList[i].Socket != e.Socket) continue;
                    gateInfo = _gateList[i];
                    gateIndex = i;
                    break;
                }
            }
            if (gateInfo == null) return;

            if (gateInfo.WireMode == TGateWireMode.Unknown)
            {
                var marker = e.ReceiveBuffer[e.Offset];
                if (marker == 0x77)
                    gateInfo.WireMode = TGateWireMode.Native77;
                else if (marker == (byte)'%')
                    gateInfo.WireMode = TGateWireMode.PrivatePercentDollar;
                else
                {
                    DBShare.MainOutMessage(
                        $"角色网关[{gateIndex}]未知协议标记: 0x{marker:X2}");
                    gateInfo.Socket.Close();
                    return;
                }
            }

            if (gateInfo.WireMode == TGateWireMode.Native77)
            {
                try
                {
                    gateInfo.NativeFrameParser.Append(
                        e.ReceiveBuffer.AsSpan(e.Offset, e.BytesReceived),
                        frame => ProcessNativeGateFrame(gateInfo, gateIndex, frame));
                }
                catch (Exception ex)
                {
                    DBShare.MainOutMessage(
                        $"角色网关[{gateIndex}]原版77帧错误: {ex.Message}");
                    gateInfo.Socket.Close();
                }
                return;
            }

            if (!gateInfo.FrameParser.TryAppend(e.ReceiveBuffer, e.Offset, e.BytesReceived,
                    out var frames, out var error))
            {
                DBShare.MainOutMessage($"角色网关[{gateIndex}]数据帧错误: {error}");
                gateInfo.Socket.Close();
                return;
            }

            foreach (var frame in frames)
                ProcessGateMsg(gateInfo, HUtil32.GetString(frame, 0, frame.Length));
        }

        private void ProcessNativeGateFrame(TGateInfo gateInfo, int gateIndex,
            YbDbLegacy77Frame frame)
        {
            var connId = frame.QueryId.ToString();
            if (frame.Ident == NativeGateControlProtocol.RegisterRequest)
            {
                if (frame.QueryId <= 0 || frame.QueryId > ushort.MaxValue
                    || gateIndex < 0 || gateIndex >= byte.MaxValue)
                    throw new InvalidOperationException(
                        "native gate registration route values are out of range");
                gateInfo.NativeRoutePort = (ushort)frame.QueryId;
                gateInfo.NativeRouteID = checked((byte)(gateIndex + 1));
            }
            if (frame.Ident == NativeGateControlProtocol.OpenRequest)
            {
                if (frame.QueryId <= 0 || frame.QueryId > ushort.MaxValue)
                    throw new InvalidOperationException(
                        "native gate connection id is out of range");
                var length = Array.IndexOf(frame.Payload, (byte)0);
                if (length < 0) length = frame.Payload.Length;
                var userIp = length == 0
                    ? string.Empty
                    : Encoding.ASCII.GetString(frame.Payload, 0, length);
                OpenUser(connId, userIp + "/" + gateInfo.sGateaddr, ref gateInfo);
            }
            else if (frame.Ident == NativeGateControlProtocol.CloseRequest)
            {
                CloseUser(connId, ref gateInfo);
            }
            else if (frame.Ident == NativeGateControlProtocol.DataRequest)
            {
                if (!LegacyGateDataCodec.TryDecodeRequest(frame,
                        out var dataMessage, out var dataError))
                {
                    throw new InvalidOperationException(dataError);
                }

                lock (gateInfo.UserList)
                {
                    var userInfo = gateInfo.UserList.FirstOrDefault(
                        user => user?.sConnID == connId);
                    if (userInfo == null)
                    {
                        DBShare.MainOutMessage(
                            $"角色网关[{gateIndex}]原版77数据引用未知连接: qid={frame.QueryId}");
                        return;
                    }

                    userInfo.WireMode = TGateWireMode.Native77;
                    userInfo.NativeQueryId = frame.QueryId;
                    var serverIdent = MobileCmdMap.ToServer(dataMessage.Ident);
                    ProcessDecodedUserPacket(gateInfo, ref userInfo, serverIdent,
                        dataMessage.Recog, dataMessage.Param,
                        EncodeRawBody(dataMessage.Body));
                }
                return;
            }

            if (!NativeGateControlProtocol.TryCreateResponse(
                    frame, gateIndex, out var response)) return;
            if (!YbDbLegacy77Codec.TryEncode(response, out var wire, out var error))
                throw new InvalidOperationException(error);
            SendAll(gateInfo.Socket, wire);
        }

        private static void SendAll(Socket socket, byte[] buffer)
        {
            lock (socket)
            {
                var offset = 0;
                while (offset < buffer.Length)
                {
                    var sent = socket.Send(buffer, offset,
                        buffer.Length - offset, SocketFlags.None);
                    if (sent <= 0)
                        throw new SocketException((int)SocketError.ConnectionReset);
                    offset += sent;
                }
            }
        }

        // ===================== 用户管理 =====================

        private void OpenUser(string sConnId, string sIP, ref TGateInfo gateInfo)
        {
            string sUserIPaddr = string.Empty;
            string sGateIPaddr = HUtil32.GetValidStr3(sIP, ref sUserIPaddr, HUtil32.Backslash);

            // 检查重复
            for (var i = 0; i < gateInfo.UserList.Count; i++)
            {
                if (gateInfo.UserList[i]?.sConnID == sConnId)
                    return;
            }

            gateInfo.UserList.Add(new TUserInfo
            {
                sAccount = string.Empty,
                sUserIPaddr = sUserIPaddr,
                sGateIPaddr = sGateIPaddr,
                sConnID = sConnId,
                nSessionID = 0,
                Socket = gateInfo.Socket,
                sText = string.Empty,
                dwTick34 = HUtil32.GetTickCount(),
                dwChrTick = HUtil32.GetTickCount(),
                boChrSelected = false,
                boChrQueryed = false,
                nSelGateID = gateInfo.nGateID,
                WireMode = gateInfo.WireMode,
                NativeQueryId = gateInfo.WireMode == TGateWireMode.Native77
                    ? HUtil32.Str_ToInt(sConnId, 0)
                    : 0,
                NativeConnectionId = gateInfo.WireMode == TGateWireMode.Native77
                    ? checked((ushort)HUtil32.Str_ToInt(sConnId, 0))
                    : (ushort)0,
                NativeAuthTick = 0,
                NativeAuthResponse = null,
                NativeText102 = string.Empty,
                NativeLoginDateTimeBits = 0,
                sReconnectID = string.Empty
            });
        }

        private void CloseUser(string sConnId, ref TGateInfo gateInfo)
        {
            for (var i = 0; i < gateInfo.UserList.Count; i++)
            {
                var userInfo = gateInfo.UserList[i];
                if (userInfo?.sConnID == sConnId)
                {
                    if (!_loginService.GetGlobaSessionStatus(userInfo.sAccount, userInfo.nSessionID))
                    {
                        _loginService.SendSocketMsg(Grobal2.SS_SOFTOUTSESSION,
                            userInfo.sAccount + "/" + userInfo.nSessionID);
                        _loginService.CloseSession(userInfo.sAccount, userInfo.nSessionID);
                    }
                    gateInfo.UserList.RemoveAt(i);
                    break;
                }
            }
        }

        // ===================== 消息解码与路由 =====================

        private void ProcessUserMsg(TGateInfo gateInfo, ref TUserInfo userInfo)
        {
            string sData = string.Empty;
            if (HUtil32.TagCount(userInfo.sText, '!') <= 0) return;

            userInfo.sText = HUtil32.ArrestStringEx(userInfo.sText, "#", "!", ref sData);
            if (string.IsNullOrEmpty(sData)) return;

            if (sData.StartsWith("#")) sData = sData.Substring(1);
            if (sData.Length < Grobal2.DEFBLOCKSIZE) return;

            if (!TryDecodeUserPacket(sData, out ushort ident, out int pktSessionId,
                    out ushort packetParam, out string body, out _)) return;

            ProcessDecodedUserPacket(gateInfo, ref userInfo, ident, pktSessionId,
                packetParam, body);
        }

        private void ProcessDecodedUserPacket(TGateInfo gateInfo, ref TUserInfo userInfo,
            ushort ident, int pktSessionId, ushort packetParam, string body)
        {
            switch (ident)
            {
                case 4004:
                    userInfo.nSessionID = pktSessionId > 0 ? pktSessionId : userInfo.nSessionID;
                    ProcessMobileLoginAuth(body, userInfo.nSessionID, ref userInfo, ref gateInfo);
                    break;

                case 4039: // CM_SELCHR_EXIT — 返回登录/退出选角
                    SendEncodedPacket(userInfo, 4039, 0, 0, 0, 0, null);
                    break;

                case Grobal2.CM_RENAMECHR4016: // 0xFB0 角色改名
                    ProcessRenameChr(body, packetParam, ref userInfo);
                    break;

                case Grobal2.CM_QUERYCHR:
                    if (!userInfo.boChrQueryed || (HUtil32.GetTickCount() - userInfo.dwChrTick) > 200)
                    {
                        userInfo.dwChrTick = HUtil32.GetTickCount();
                        var isSoftCloseQuery = packetParam == SoftCloseQueryParam;
                        if (QueryChr(body, ref userInfo, ref gateInfo, isSoftCloseQuery))
                        {
                            userInfo.boChrQueryed = true;
                            if (isSoftCloseQuery)
                            {
                                userInfo.boChrSelected = false;
                                SendEncodedPacket(userInfo, 4041, 0, 0, 0, 0, null);
                            }
                        }
                    }
                    break;

                case Grobal2.CM_NEWCHR:
                    Log($"[NewChr] START account={userInfo.sAccount} session={userInfo.nSessionID}");
                    if ((HUtil32.GetTickCount() - userInfo.dwChrTick) > 1000)
                    {
                        userInfo.dwChrTick = HUtil32.GetTickCount();
                        if (!string.IsNullOrEmpty(userInfo.sAccount) &&
                            _loginService.CheckSession(userInfo.sAccount, userInfo.sUserIPaddr, userInfo.nSessionID))
                        {
                            Log($"[NewChr] Session OK, calling NewChr...");
                            NewChr(body, ref userInfo);
                            if (userInfo.nSessionID > 0 && !string.IsNullOrEmpty(userInfo.sAccount))
                                QueryChr(EDcode.EncodeString(userInfo.sAccount + "/" + userInfo.nSessionID), ref userInfo, ref gateInfo);
                        }
                        else { Log($"[NewChr] Session FAILED, OutOfConnect"); OutOfConnect(userInfo); }
                    }
                    else Log($"[NewChr] Tick too fast, skip");
                    break;

                case Grobal2.CM_DELCHR:
                    if ((HUtil32.GetTickCount() - userInfo.dwChrTick) > 1000)
                    {
                        userInfo.dwChrTick = HUtil32.GetTickCount();
                        if (!string.IsNullOrEmpty(userInfo.sAccount) &&
                            _loginService.CheckSession(userInfo.sAccount, userInfo.sUserIPaddr, userInfo.nSessionID))
                        {
                            try { DelChr(body, ref userInfo); userInfo.boChrQueryed = false; }
                            catch (Exception ex) { Log($"[DelChr] Exception: {ex.Message}"); }
                            if (userInfo.nSessionID > 0 && !string.IsNullOrEmpty(userInfo.sAccount))
                                QueryChr(EDcode.EncodeString(userInfo.sAccount + "/" + userInfo.nSessionID), ref userInfo, ref gateInfo);
                        }
                        else OutOfConnect(userInfo);
                    }
                    break;

                case Grobal2.CM_QUERYDELCHR:
                    userInfo.dwChrTick = HUtil32.GetTickCount();
                    QueryDelChr(body, ref userInfo, ref gateInfo);
                    break;

                case Grobal2.CM_RESDELCHR:
                    if (!string.IsNullOrEmpty(userInfo.sAccount) &&
                        _loginService.CheckSession(userInfo.sAccount, userInfo.sUserIPaddr, userInfo.nSessionID))
                    {
                        userInfo.dwChrTick = HUtil32.GetTickCount();
                        ResDelChr(body, ref userInfo);
                        userInfo.boChrQueryed = true;
                        userInfo.dwChrTick = 0;
                        if (userInfo.nSessionID > 0)
                            QueryChr(EDcode.EncodeString(userInfo.sAccount + "/" + userInfo.nSessionID), ref userInfo, ref gateInfo);
                    }
                    else OutOfConnect(userInfo);
                    break;

                case Grobal2.CM_SELCHR:
                    if (userInfo.boChrQueryed)
                    {
                        var sessionOK = !string.IsNullOrEmpty(userInfo.sAccount) &&
                            _loginService.CheckSession(userInfo.sAccount, userInfo.sUserIPaddr, userInfo.nSessionID);
                        if (sessionOK)
                        {
                            if (SelectChr(body, gateInfo, ref userInfo))
                                userInfo.boChrSelected = true;
                        }
                        else OutOfConnect(userInfo);
                    }
                    break;
            }
        }

        // ===================== 手游登录认证 (4004) =====================

        private void ProcessMobileLoginAuth(string sData, int sessionId, ref TUserInfo userInfo, ref TGateInfo gateInfo)
        {
            Log($"[MobileAuth] START sDataLen={sData?.Length} sessionId={sessionId}");
            var body = DecodeRawBody(sData);
            if (_loginService.Mode == LoginGateTransportMode.Native77Client)
            {
                if (sessionId == 0
                    || !NativeMobileLoginAuthCodec.TryDecode(body,
                        out var nativeRequest, out var decodeError))
                {
                    Log("[MobileAuth] native request rejected: " + decodeError);
                    SendMobileLoginAuth(userInfo, -1, 0, "认证失败");
                    return;
                }

                var pendingUser = userInfo;
                var pendingGate = gateInfo;
                var queryId = userInfo.NativeQueryId;
                if (!_loginService.TryAuthenticateNative(queryId,
                        nativeRequest.Ticket, nativeRequest.DeviceId,
                        userInfo.sUserIPaddr, nativeRequest.DeviceName,
                        (response, error, loginDateTimeBits) =>
                            CompleteNativeMobileLoginAuth(
                                pendingUser, pendingGate, sessionId, response,
                                error, loginDateTimeBits),
                        out var sendError))
                {
                    Log("[MobileAuth] native send rejected: " + sendError);
                    SendMobileLoginAuth(userInfo, -1, 0, "认证失败");
                }
                return;
            }

            var ticket = ExtractCStr(body, 0);
            var account = ResolveMobileTicket(ticket);
            Log($"[MobileAuth] ticket={ticket?.Substring(0, Math.Min(8, ticket?.Length ?? 0))}... account={account} sessionId={sessionId}");

            if (string.IsNullOrEmpty(account) || sessionId == 0)
            {
                Log($"[MobileAuth] FAIL: emptyAccount={string.IsNullOrEmpty(account)} sessionZero={sessionId==0}");
                SendMobileLoginAuth(userInfo, -1, 0, "认证失败");
                return;
            }

            CompleteMobileLoginAuth(account, sessionId, ref userInfo, ref gateInfo);
        }

        private void CompleteNativeMobileLoginAuth(TUserInfo userInfo, TGateInfo gateInfo,
            int sessionId, NativeLoginGateAuthResponse response, string error,
            long loginDateTimeBits)
        {
            lock (gateInfo.UserList)
            {
                if (!gateInfo.UserList.Any(current => ReferenceEquals(current, userInfo))
                    || userInfo.nSessionID != sessionId
                    || response != null && userInfo.NativeQueryId != response.QueryId)
                    return;

                if (!string.IsNullOrEmpty(error) || response == null
                    || response.Status != NativeLoginGateProtocol.AuthSuccessStatus
                    || response.Version != NativeLoginGateProtocol.ProtocolVersion
                    || string.IsNullOrEmpty(response.Account))
                {
                    Log("[MobileAuth] native authentication failed: "
                        + (error ?? $"status={response?.Status} version={response?.Version}"));
                    SendMobileLoginAuth(userInfo, -1, 0, "认证失败");
                    return;
                }

                var currentUser = userInfo;
                var currentGate = gateInfo;
                userInfo.NativeAuthResponse = response;
                userInfo.NativeText102 = response.Text102;
                Interlocked.Exchange(ref userInfo.NativeLoginDateTimeBits,
                    loginDateTimeBits);
                userInfo.NativeAuthTick = HUtil32.GetTickCount();
                CompleteMobileLoginAuth(response.Account, sessionId,
                    ref currentUser, ref currentGate);
            }
        }

        private void CompleteMobileLoginAuth(string account, int sessionId,
            ref TUserInfo userInfo, ref TGateInfo gateInfo)
        {

            Log($"[MobileAuth] Opening session for {account}");
            _loginService.OpenMobileSession(account, userInfo.sUserIPaddr, sessionId);
            userInfo.sAccount = account;
            userInfo.nSessionID = sessionId;

            Log($"[MobileAuth] OK account={account} session={sessionId}");
            SendMobileLoginAuth(userInfo, 0, 1, null);

            if (QueryChr(EDcode.EncodeString(account + "/" + sessionId), ref userInfo, ref gateInfo))
                userInfo.boChrQueryed = true;

            SendEncodedPacket(userInfo, 4041, 0, 0, 0, 0, null); // 触发角色界面
        }

        // ===================== 查询角色 (CM_QUERYCHR) =====================

        private bool QueryChr(string sData, ref TUserInfo userInfo, ref TGateInfo gateInfo,
            bool allowSoftCloseSessionRestore = false)
        {
            string sAccount = string.Empty;
            string sSessionID = HUtil32.GetValidStr3(EDcode.DeCodeString(sData), ref sAccount, HUtil32.Backslash);
            int nSessionID = HUtil32.Str_ToInt(sSessionID, -2);

            Log($"[QueryChr] Account={sAccount} Session={nSessionID} IP={userInfo.sUserIPaddr}");

            var sessionValid = _loginService.CheckSession(sAccount, userInfo.sUserIPaddr, nSessionID);
            if (!sessionValid && allowSoftCloseSessionRestore && CanRestoreSoftCloseSession(
                    sAccount, nSessionID, SoftCloseQueryParam,
                    userInfo.sAccount, userInfo.nSessionID, userInfo.boChrSelected))
            {
                Log($"[SoftCloseQuery] Restore account={sAccount} session={nSessionID} conn={userInfo.sConnID}");
                _loginService.OpenMobileSession(sAccount, userInfo.sUserIPaddr, nSessionID);
                sessionValid = _loginService.CheckSession(sAccount, userInfo.sUserIPaddr, nSessionID);
            }

            if (!sessionValid)
            {
                SendEncodedPacket(userInfo, Grobal2.SM_QUERYCHR_FAIL,
                    0, 0, 1, 0, null);
                CloseUser(userInfo.sConnID, ref gateInfo);
                return false;
            }

            _loginService.SetGlobaSessionNoPlay(sAccount, nSessionID);
            userInfo.sAccount = sAccount;
            userInfo.nSessionID = nSessionID;

            // 从 user_index 直接查询角色列表
            var ptid = GetPtid(sAccount);
            var chrList = _playRecordService.QueryChrByPtid(ptid ?? sAccount);
            Log($"[QueryChr] Found {chrList.Count} chars for account={sAccount}");
            // Character rows are authoritative data. Encoding problems must never be
            // treated as permission to delete a character.

            int nChrCount = 0;
            int recSize = 20;
            byte[] chrBody = new byte[recSize * chrList.Count];
            foreach (var chr in chrList)
            {
                byte[] nb = System.Text.Encoding.GetEncoding(936).GetBytes(chr.ChrName ?? "");
                int off = nChrCount * recSize;
                chrBody[off] = (byte)Math.Min(nb.Length, 14);
                Buffer.BlockCopy(nb, 0, chrBody, off + 1, Math.Min(nb.Length, 14));
                chrBody[off + 16] = 1;
                chrBody[off + 17] = (byte)chr.Job;
                chrBody[off + 18] = (byte)chr.Sex;
                chrBody[off + 19] = (byte)chr.Level;
                nChrCount++;
            }
            Log($"[QueryChr] chrBody({chrBody.Length}B) hex={BitConverter.ToString(chrBody).Replace("-"," ")}");
            SendEncodedPacket(userInfo, Grobal2.SM_QUERYCHR,
                nChrCount > 0 ? 1 : 0, (ushort)nChrCount, 0, 0,
                PrepareNativeListBody(userInfo, chrBody));
            return true;
        }

        // ===================== 创建角色 (CM_NEWCHR) =====================

        private void NewChr(string sData, ref TUserInfo userInfo)
        {
            try
            {
                string sAccount = userInfo.sAccount;
                string sChrName = string.Empty;
                int nHair = 0, nJob = 0, nSex = 0;
                int nCode = -1;

                // 解析手游客户端二进制格式: [len][name_gbk][null][padding][hair][job][sex]
                var decBytes = DecodeRawBody(sData);
                Log($"[NewChr] sData({sData?.Length ?? 0})='{sData?.Substring(0, Math.Min(sData?.Length??0, 30))}...'");
                Log($"[NewChr] decBytes({decBytes.Length}B) hex={BitConverter.ToString(decBytes).Replace("-"," ")}");
                if (decBytes.Length >= 3)
                {
                    int nameLen = decBytes[0];
                    Log($"[NewChr] nameLen={nameLen}");
                    if (nameLen > 0 && nameLen + 1 < decBytes.Length)
                        sChrName = MobileCodec.Gbk.GetString(decBytes, 1, nameLen);

                    int statOff = 16;
                    if (statOff < decBytes.Length) nHair = decBytes[statOff];
                    if (statOff + 1 < decBytes.Length) nJob = decBytes[statOff + 1];
                    if (statOff + 2 < decBytes.Length) nSex = decBytes[statOff + 2];
                }

                // 名字校验 (使用敏感词过滤器 + GBK校验)
                if (string.IsNullOrEmpty(sChrName) || sChrName.Length < 2)
                    nCode = 0;
                else if (DBShare.boDenyChrName)
                {
                    var nameCheck = _sensitiveWordFilter.ValidateChrName(
                        sChrName, DBShare.g_boEnglishNames);
                    if (!nameCheck.valid) nCode = nameCheck.failCode;
                }
                Log($"[NewChr] After filter: nCode={nCode} chrName='{sChrName}' sAccount='{sAccount}'");
                if (nCode != -1) { /* 已判定失败 */ }
                else if (_playRecordService.IsChrNameExists(sChrName))
                {
                    nCode = 2; Log($"[NewChr] Name exists: {sChrName}");
                }
                else if (_playRecordService.ChrCountOfAccount(GetPtid(sAccount) ?? sAccount) >= 4)
                {
                    nCode = 3; Log($"[NewChr] Max chars reached for {sAccount}");
                }
                else if (_playRecordService.TodayCreateCount(GetPtid(sAccount) ?? sAccount) >= 4)
                {
                    nCode = 3; Log($"[NewChr] Daily limit reached for {sAccount}");
                }
                else
                {
                    var ptid = GetPtid(sAccount) ?? sAccount;

                    // GM创建: 检查是否为GM账号
                    bool isGmCreate = IsGmAccount(sAccount);
                    int initLevel = isGmCreate ? 40 : 1;
                    int adminLevel = isGmCreate ? 5 : 0;

                    int idx = _playRecordService.CreateCharacter(ptid, sChrName, nJob, nSex, nHair, initLevel);
                    if (idx > 0)
                    {
                        try
                        {
                            if (isGmCreate)
                            {
                                using var conn = new MySqlConnection(DBShare.DBConnection);
                                conn.Open();
                                using (var session = new MySqlCommand(
                                           "SET WAIT_TIMEOUT = 2073600;", conn))
                                    session.ExecuteNonQuery();
                                using var cmd = new MySqlCommand("UPDATE mir3.user_index SET AdminLevel=5, Level=40 WHERE idx=@i", conn);
                                cmd.Parameters.AddWithValue("@i", idx);
                                cmd.ExecuteNonQuery();
                            }
                            var humanRCD = new THumDataInfo();
                            humanRCD.Header.sName = sChrName;
                            humanRCD.Header.sAccount = ptid;
                            humanRCD.Data.sCharName = sChrName;
                            humanRCD.Data.sAccount = ptid;
                            humanRCD.Data.btSex = (byte)nSex;
                            humanRCD.Data.btJob = (byte)nJob;
                            humanRCD.Data.btHair = (byte)nHair;
                            humanRCD.Data.Abil.Level = (ushort)initLevel;
                            if (_playDataService.Add(ref humanRCD))
                            {
                                nCode = 1;
                                ProcessAwardPlayer(ptid, sChrName);
                            }
                            else
                            {
                                _playRecordService.HardDelete(idx);
                                nCode = 4;
                                DBShare.MainOutMessage($"[NewChr] 初始存档写入失败，已回滚 chr={sChrName} idx={idx}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _playRecordService.HardDelete(idx);
                            nCode = 4;
                            DBShare.MainOutMessage($"[NewChr] 初始存档异常，已回滚 chr={sChrName} idx={idx}: {ex.Message}");
                        }
                    }
                    else { nCode = 4; }
                }

                Log($"[NewChr] nCode={nCode} Chr={sChrName} Account={sAccount} Hair={nHair} Job={nJob} Sex={nSex}");

                ClientPacket msg;
                if (nCode == 1)
                    msg = Grobal2.MakeDefaultMsg(Grobal2.SM_NEWCHR_SUCCESS, 0, 0, 0, 0);
                else
                    msg = Grobal2.MakeDefaultMsg(Grobal2.SM_NEWCHR_FAIL, 0, nCode, 0, 0);

                Log($"[NewChr] SendUserSocket connected={userInfo.Socket?.Connected} connId={userInfo.sConnID} ident={msg.Ident}");
                SendEncodedPacket(userInfo, msg.Ident, msg.Recog,
                    msg.Param, msg.Tag, msg.Series, null);
            }
            catch (Exception ex)
            {
                Log($"[NewChr] Error: {ex}");
            }
        }

        // ===================== 删除角色 (CM_DELCHR) =====================

        private void DelChr(string sData, ref TUserInfo userInfo)
        {
            var sChrName = EDcode.DeCodeString(sData)?.TrimEnd('\0');
            Log($"[DelChr] name='{sChrName}' sAccount={userInfo.sAccount}");
            bool boCheck = false;

            int nIndex = _playRecordService.Index(sChrName);
            Log($"[DelChr] Index={nIndex}");
            if (nIndex >= 0)
            {
                var humRecord = _playRecordService.Get(nIndex, ref boCheck);
                Log($"[DelChr] Get boCheck={boCheck} account={humRecord?.sAccount}");
                if (boCheck && (humRecord.sAccount == userInfo.sAccount || humRecord.sAccount == GetPtid(userInfo.sAccount)))
                {
                    humRecord.boDeleted = true;
                    boCheck = _playRecordService.Update(nIndex, ref humRecord);
                    Log($"[DelChr] Update={boCheck}");
                }
            }

            var msg = boCheck
                ? Grobal2.MakeDefaultMsg(Grobal2.SM_DELCHR_SUCCESS, 0, 1, 0, 0)
                : Grobal2.MakeDefaultMsg(Grobal2.SM_DELCHR_FAIL, 0, 0, 0, 0);

            SendEncodedPacket(userInfo, msg.Ident, msg.Recog,
                msg.Param, msg.Tag, msg.Series, null);
        }

        // ===================== 角色改名 (0xFB0 = 4016) =====================

        /// <summary>
        /// 复刻原版校验层 <c>fn_5CD2EC</c>（0x5CD2EC..0x5CD543）+ 主档/级联
        /// <c>fn_5A8DDC</c> / <c>fn_5A923C</c>。
        ///
        /// 守卫顺序**照抄**，顺序本身有意义：
        ///   0x5CD303  cmp byte [ebp-9],0 / je 0x5CD335
        ///             ⇒ cl = (Recog == 0)，而 **cl==0 才走改名** ⇒ Recog != 0 才改名
        ///             （极性极易写反，规格专门标注过）
        ///   0x5CD335  新名为空 -> 直接退出，**不回包**（与"非法回 -1"是两种行为）
        ///   0x5CD34F  长度门 [4,14]                    -> err = -1
        ///   0x5CD35D  字符白名单 fn_5CCDE4             -> err = -1
        ///   0x5CD376  已有错误则**跳过** DB 调用（cmp err,0 / jne 0x5CD3B7）
        ///   0x5CD386  重名检查 0x5C22C8                -> err = -2
        ///   0x5CD3AF  call fn_5A8DDC（主档，失败即中断，级联一条都不跑）
        ///   0x5CD3B7  cmp err,1 / jne ⇒ 只有成功才发 77BBAA33 转发
        ///   0x5CD4A5  回包 opcode 与请求同为 0xFB0，错误码在记录首 dword
        /// </summary>
        private void ProcessRenameChr(string sData, ushort packetParam,
            ref TUserInfo userInfo)
        {
            // 0x5CD303/0x5CD307：cl = (Recog == 0)，cl == 0 时才是改名路径。
            // 即 Recog != 0 才改名；Recog == 0 走的是另一条（重发选角列表）分支。
            if (packetParam == 0)
            {
                Log("[RenameChr] Recog==0 -> 非改名分支(原版 0x5CD309)，忽略");
                return;
            }

            // 原版：新名来自 [ebp-8]，旧名来自 Self+0x48。
            // body 形如 "旧名/新名"（与本服务其它 EDcode 命令同构）；若只给一段，
            // 则按账号解析当前角色作为旧名。
            // ⚠️ TUserInfo **没有**当前角色名字段（我一度想当然写成 sChrName，
            // 编译即暴露）——不发明字段，旧名走账号查询。
            var decoded = EDcode.DeCodeString(sData)?.TrimEnd('\0') ?? string.Empty;
            string oldName;
            string newName;
            var slash = decoded.IndexOf('/');
            if (slash > 0)
            {
                oldName = decoded.Substring(0, slash);
                newName = decoded.Substring(slash + 1);
            }
            else
            {
                newName = decoded;
                oldName = ResolveAccountCharacterName(userInfo.sAccount);
            }

            var gbkNewName = LegacyGbkText.Encode(newName);

            // 0x5CD335 cmp dword [ebp-8],0 / je 0x5CD53B —— 空名字**不回包**。
            if (NativeRenameCharProtocol.IsEmptyName(gbkNewName))
            {
                Log("[RenameChr] 空名字 -> 原版 0x5CD339 直接退出且不回包");
                return;
            }

            if (string.IsNullOrEmpty(oldName))
            {
                // 原版 Self+0x48 为空时 0x5CD317 也是直接退出不回包。
                Log("[RenameChr] 会话无当前角色名 -> 不回包");
                return;
            }

            // 长度门 + 字符白名单 + 重名检查，逐条对应上面的 VA。
            var result = NativeRenameCharProtocol.Validate(gbkNewName,
                () => _playRecordService.Index(newName) >= 0);

            if (result == NativeRenameCharProtocol.ResultInitial)
            {
                // 主档先写。失败即中断 —— 这是原版唯一的安全性来源
                // （0x5A8F4A test al,al / 0x5A8F4C je 0x5A9162 -> 返回 -1，
                //   周边 19 张表一条都不动）。顺序不可颠倒。
                var idx = _playRecordService.Index(oldName);
                if (_renameCascade.RenameMasterRecords(idx, newName))
                {
                    // 22 条级联，fire-and-forget：原版每条 call 之后没有 test al,al，
                    // 且 fn_5A923C 无返回值、调用者 0x5A9134 无条件置 1
                    // ⇒ 级联全失败也照样回包"成功"。这里照抄该语义。
                    var applied = _renameCascade.RenameCascade(oldName, newName);
                    Log($"[RenameChr] '{oldName}' -> '{newName}' 级联 {applied}/22");
                    result = NativeRenameCharProtocol.ResultSuccess;
                }
                else
                {
                    result = NativeRenameCharProtocol.ResultInvalidName;
                }
            }

            if (result == NativeRenameCharProtocol.ResultSuccess)
            {
                // 原版 0x5CD3C8 在此更新 Self+0x48（会话态当前角色名）。
                // C# 的 TUserInfo 没有该字段，会话态角色名一律从库里解析，
                // 主档已改名故下次解析即得新名 —— 无需也不应新增字段。
                // 0x5CD3F2..0x5CD48F：改名成功才发 77BBAA33 内部转发（子命令 0x57）。
                // ⚠️ 该转发的出向通道（原版 [0x5DA0E0] 的 0x59E450）在本部署未接入，
                // 与 YBDB 6108 / GlobalServer 6020 同类。按原版对客户端侧仍回包，
                // 仅转发缺失，且**不伪造**一个通道。
                Log("[RenameChr] 原版此处发 77BBAA33 子命令 0x57 转发——外部通道未接入");
            }

            // 0x5CD49F..0x5CD4BF：回包 opcode 0xFB0，错误码在记录首 dword。
            SendEncodedPacket(userInfo, NativeRenameCharProtocol.ResponseCommand,
                result, 0, 0, 0, null);
        }

        /// <summary>
        /// 按账号解析当前角色名，替代原版的 Self+0x48 会话态字段。
        /// 账号下只有一个角色时返回它；多个或零个时返回空（调用方据此不回包，
        /// 与原版 0x5CD317 "Self+0x48 为空则直接退出"一致）。
        /// </summary>
        private string ResolveAccountCharacterName(string account)
        {
            if (string.IsNullOrEmpty(account)) return null;
            try
            {
                IList<TQuickID> list = new List<TQuickID>();
                var ptid = GetPtid(account) ?? account;
                _playRecordService.FindByAccount(ptid, ref list);
                if (list.Count == 1) return list[0].sChrName;
                if (list.Count == 0) return null;
                // 多角色时无法从 body 单段推断，交由客户端用 "旧名/新名" 形式指定。
                Log($"[RenameChr] 账号 {account} 有 {list.Count} 个角色，"
                    + "单段 body 无法判定旧名，需用 旧名/新名 形式");
                return null;
            }
            catch (Exception ex)
            {
                Log("[RenameChr] 解析当前角色名失败: " + ex.Message);
                return null;
            }
        }

        // ===================== 查询已删除角色 (CM_QUERYDELCHR) =====================

        private bool QueryDelChr(string sData, ref TUserInfo userInfo, ref TGateInfo gateInfo)
        {
            string sAccount = userInfo.sAccount;
            string ptid = GetPtid(sAccount) ?? sAccount;
            int nSessionID = userInfo.nSessionID;
            // CM_QUERYDELCHR 无 body，直接用 userInfo 中的账号信息
            Log($"[QueryDelChr] account={sAccount} ptid={ptid} session={nSessionID}");

            try
            {
                // 直接查 MySQL 获取完整 Job/Sex/Level
                var delList = new List<(string name, byte job, byte sex, byte level)>();
                using (var conn = new MySqlConnection(DBShare.DBConnection))
                {
                    conn.Open();
                    using (var session = new MySqlCommand(
                               "SET WAIT_TIMEOUT = 2073600;", conn))
                        session.ExecuteNonQuery();
                    using var cmd = new MySqlCommand(
                        "SELECT ChrName, Job, Sex, Level FROM mir3.user_index WHERE PTID=@p AND IsDelete=1 LIMIT 10", conn);
                    cmd.Parameters.AddWithValue("@p", ptid);
                    using var dr = cmd.ExecuteReader();
                    while (dr.Read())
                    {
                        var name = LegacyGbkText.Read(dr, "ChrName");
                        if (string.IsNullOrEmpty(name)) continue;
                        delList.Add((name, (byte)dr.GetInt32("Job"), (byte)dr.GetInt32("Sex"), (byte)dr.GetInt32("Level")));
                    }
                }
                int nChrCount = delList.Count;
                int recSize = 20;
                byte[] chrBody = new byte[recSize * nChrCount];
                for (int i = 0; i < nChrCount; i++)
                {
                    var nb = System.Text.Encoding.GetEncoding(936).GetBytes(delList[i].name);
                    int off = i * recSize;
                    chrBody[off] = (byte)Math.Min(nb.Length, 14);
                    Buffer.BlockCopy(nb, 0, chrBody, off + 1, Math.Min(nb.Length, 14));
                    chrBody[off + 16] = 1;
                    chrBody[off + 17] = delList[i].job;
                    chrBody[off + 18] = delList[i].sex;
                    chrBody[off + 19] = delList[i].level;
                }
                SendEncodedPacket(userInfo, Grobal2.SM_QUERYDELCHR,
                    nChrCount > 0 ? 1 : 0, (ushort)nChrCount, 0, 0,
                    PrepareNativeListBody(userInfo, chrBody));
            }
            catch (Exception ex) { Log($"[QueryDelChr] Error: {ex.Message}"); }
            return true;
        }

        // ===================== 恢复删除角色 (CM_RESDELCHR) =====================

        private void ResDelChr(string sData, ref TUserInfo userInfo)
        {
            string sAccount = userInfo.sAccount ?? "";
            string ptid = GetPtid(sAccount) ?? sAccount;
            var sChrName = EDcode.DeCodeString(sData)?.TrimEnd('\0');
            Log($"[ResDelChr] account={sAccount} ptid={ptid} chrName='{sChrName}'");
            bool boDataOK = false;

            if (!string.IsNullOrEmpty(sChrName))
            {
                if (_playRecordService.ChrCountOfAccount(ptid) < 4)
                {
                    IList<HumRecordData> deletedList = new List<HumRecordData>();
                    if (_playRecordService.FindDeletedByAccount(ptid, ref deletedList) >= 0)
                    {
                        for (int di = 0; di < deletedList.Count; di++)
                        {
                            var humRecord = deletedList[di];
                            if (!string.Equals(humRecord.sChrName, sChrName,
                                    StringComparison.Ordinal))
                                continue;

                            humRecord.boSelected = 1;
                            humRecord.boDeleted = false;
                            if (humRecord.Header != null)
                            {
                                humRecord.Header.boDeleted = false;
                                humRecord.Header.nSelectID = humRecord.boSelected;
                            }

                            var recordCopy = humRecord;
                            try
                            {
                                if (_playRecordService.Update(humRecord.Id, ref recordCopy))
                                    boDataOK = true;
                            }
                            catch (Exception ex) { Log($"[ResDelChr] Error: {ex.Message}"); }
                            break;
                        }
                    }
                }
            }

            var msg = boDataOK
                ? Grobal2.MakeDefaultMsg(Grobal2.SM_RESDELCHR_SUCCESS, 0, 1, 0, 0)
                : Grobal2.MakeDefaultMsg(Grobal2.SM_RESDELCHR_FAIL, 0, 0, 0, 0);

            SendEncodedPacket(userInfo, msg.Ident, msg.Recog,
                msg.Param, msg.Tag, msg.Series, null);
        }

        // ===================== 选择角色进入游戏 (CM_SELCHR) =====================

        private bool SelectChr(string sData, TGateInfo gateInfo, ref TUserInfo userInfo)
        {
            string sAccount = userInfo.sAccount ?? "";
            var sChrName = EDcode.DeCodeString(sData)?.TrimEnd('\0');
            if (string.IsNullOrEmpty(sChrName)) return false;

            bool boDataOK = false;

            // 更新选择状态
            IList<TQuickID> chrList = new List<TQuickID>();
            string ptid = GetPtid(sAccount) ?? sAccount;
            if (_playRecordService.FindByAccount(ptid, ref chrList) >= 0)
            {
                foreach (var qid in chrList)
                {
                    bool gotIt = false;
                    var humRecord = _playRecordService.GetBy(qid.nIndex, ref gotIt);
                    if (!gotIt) continue;

                    humRecord.boSelected = (byte)(humRecord.sChrName == sChrName ? 1 : 0);
                    _playRecordService.UpdateBy(qid.nIndex, ref humRecord);
                }
            }

            int recordIndex = _playRecordService.Index(sChrName);
            int dataIndex = _playDataService.Index(sChrName);
            if (recordIndex > 0 && dataIndex == recordIndex)
            {
                var ptid2 = GetPtid(sAccount) ?? sAccount;
                if (userInfo.WireMode == TGateWireMode.Native77)
                {
                    // The original DBServer pushes the selected record directly over port 6000
                    // before acknowledging 4017 on port 5100.
                    var auth = userInfo.NativeAuthResponse;
                    if (auth == null || userInfo.NativeConnectionId == 0
                                     || DBShare.nZoneIdx < 0
                                     || DBShare.nZoneIdx > ushort.MaxValue
                                     || DBShare.nGroupIdx < 0
                                     || DBShare.nGroupIdx > byte.MaxValue)
                    {
                        Log("[SelectChr] native session context is incomplete");
                        boDataOK = false;
                    }
                    else
                    {
                        var elapsed = userInfo.dwTick34 <= 0
                            ? 0u
                            : unchecked((uint)(HUtil32.GetTickCount()
                                               - userInfo.dwTick34));
                        var loginDateTimeBits = Interlocked.Read(
                            ref userInfo.NativeLoginDateTimeBits);
                        var context = new NativeHumanSessionContext
                        {
                            UserIp = userInfo.sUserIPaddr,
                            AuthText54 = auth.Text54,
                            AuthFlags75 = auth.Flags75,
                            AuthByte77 = auth.Byte77,
                            AuthByte78 = auth.Byte78,
                            SelectionState = 1,
                            GroupIndex = (byte)DBShare.nGroupIdx,
                            ZoneIndex = (ushort)DBShare.nZoneIdx,
                            ConnectionId = userInfo.NativeConnectionId,
                            LoginElapsedMilliseconds = elapsed,
                            AuthText81 = auth.Text81,
                            AuthText102 = userInfo.NativeText102,
                            SessionMode = 1,
                            CachedValue38 = unchecked((int)loginDateTimeBits),
                            CachedValue3C = unchecked(
                                (int)(loginDateTimeBits >> 32))
                        };
                        boDataOK = _gameSocService.TrySendNativeHuman(
                            ptid2, sChrName, context);
                    }
                }
                else
                {
                    boDataOK = _loginService.TrySendSocketMsg(Grobal2.SS_OPENSESSION,
                        $"{ptid2}/{userInfo.nSessionID}/{MobileAdmissionPaymentState}/" +
                        $"{MobileAdmissionPayMode}/{userInfo.sUserIPaddr}");
                }
                if (boDataOK)
                    _loginService.SetGlobaSessionPlay(userInfo.sAccount, userInfo.nSessionID);
            }

            if (boDataOK)
            {
                SendMobileSelectChr(userInfo, sChrName);
                return true;
            }

            SendEncodedPacket(userInfo, Grobal2.SM_STARTFAIL,
                0, 0, 0, 0, null);
            return false;
        }

        // ===================== 手游进游戏响应包 =====================

        private void SendMobileSelectChr(TUserInfo userInfo, string sChrName)
        {
            var body = userInfo.WireMode == TGateWireMode.Native77
                ? null
                : System.Text.Encoding.GetEncoding(936).GetBytes(sChrName ?? "");
            SendEncodedPacket(userInfo, Grobal2.SM_STARTPLAY, 0, 1, 0, 0, body);
        }

        // ===================== 路由 =====================

        private string GateRouteIP(string sGateIP, ref int nPort)
        {
            nPort = 7200; // 默认端口
            for (var i = 0; i < DBShare.g_RouteInfo.Length; i++)
            {
                var route = DBShare.g_RouteInfo[i];
                if (route == null) continue;
                if (route.sSelGateIP == sGateIP && route.nGateCount > 0)
                {
                    var nGateIndex = RandomNumber.GetInstance().Random(route.nGateCount);
                    nPort = route.nGameGatePort[nGateIndex];
                    return route.sGameGateIP[nGateIndex];
                }
            }
            return sGateIP;
        }

        private int GetMapIndex(string sMap)
        {
            if (string.IsNullOrEmpty(sMap)) return 0;
            return _mapList.TryGetValue(sMap, out int idx) ? idx : 0;
        }

        // ===================== 网络发送 =====================

        private void SendUserSocket(Socket socket, string sSessionID, string sSendMsg)
        {
            if (socket.Connected)
            {
                socket.SendText($"%{sSessionID}/#{sSendMsg}!$");
            }
        }

        private void SendKeepAlivePacket(Socket socket)
        {
            if (socket.Connected) socket.SendText("%++$");
        }

        private void OutOfConnect(TUserInfo userInfo)
        {
            SendEncodedPacket(userInfo, Grobal2.SM_OUTOFCONNECTION,
                0, 0, 0, 0, null);
        }

        // ===================== 手游编码辅助方法 =====================

        private void SendMobileLoginAuth(TUserInfo userInfo, int recog, ushort param, string message)
        {
            byte[] body = null;
            if (param == 1)
            {
                if (string.IsNullOrEmpty(userInfo.sReconnectID))
                    userInfo.sReconnectID = NativeLoginResultCodec.CreateReconnectId(
                        userInfo.sAccount);
                body = NativeLoginResultCodec.Encode(userInfo.sAccount, string.Empty,
                    DBShare.nZoneIdx, DBShare.nGroupIdx, userInfo.sReconnectID);
            }
            else if (!string.IsNullOrEmpty(message))
                body = MobileCodec.EncodeGbk(message);

            var tag = (ushort)0;
            if (userInfo.WireMode == TGateWireMode.Native77 && param == 1)
            {
                recog = 1;
                tag = 1;
            }
            SendEncodedPacket(userInfo, 4004, recog, param, tag, 0, body);
        }

        private void SendEncodedPacket(TUserInfo userInfo, ushort ident, int recog, ushort param, ushort tag, ushort series, byte[] body)
        {
            if (userInfo.WireMode == TGateWireMode.Native77)
            {
                var clientIdent = MobileCmdMap.ToClient(ident);
                var frame = LegacyGateDataCodec.CreateResponse(
                    userInfo.NativeQueryId, recog, clientIdent, param, tag, series, body);
                if (!YbDbLegacy77Codec.TryEncode(frame, out var wire, out var error))
                    throw new InvalidOperationException(error);
                SendAll(userInfo.Socket, wire);
                return;
            }

            var msg = Grobal2.MakeDefaultMsg(ident, recog, param, tag, series);
            var encoded = EDcode.EncodeMessage(msg);
            while (encoded.Length < Grobal2.DEFBLOCKSIZE)
                encoded += "0";
            if (body != null && body.Length > 0)
                encoded += EncodeRawBody(body);
            SendUserSocket(userInfo.Socket, userInfo.sConnID, encoded);
        }

        private static byte[] PrepareNativeListBody(TUserInfo userInfo, byte[] body)
        {
            if (userInfo.WireMode != TGateWireMode.Native77) return body;
            var terminated = new byte[(body?.Length ?? 0) + 1];
            if (body != null && body.Length > 0)
                Buffer.BlockCopy(body, 0, terminated, 0, body.Length);
            return terminated;
        }

        private static byte[] DecodeRawBody(string encoded)
        {
            if (string.IsNullOrEmpty(encoded)) return Array.Empty<byte>();
            var bytes = HUtil32.GetBytes(encoded);
            return Misc.Decode6BitBufDirect(bytes, bytes.Length);
        }

        private static string EncodeRawBody(byte[] body)
        {
            var enc = new byte[body.Length * 2 + 4];
            var len = Misc.Encode6BitBufDirect(body, body.Length, enc);
            return HUtil32.GetString(enc, 0, len);
        }

        private static string ExtractCStr(byte[] buf, int off)
        {
            if (buf == null) return string.Empty;
            var end = off;
            while (end < buf.Length && buf[end] != 0) end++;
            return end > off ? MobileCodec.Gbk.GetString(buf, off, end - off) : string.Empty;
        }

        // ===================== 解码 =====================

        private static bool CanRestoreSoftCloseSession(string requestedAccount, int requestedSessionId,
            ushort packetParam, string authenticatedAccount, int authenticatedSessionId, bool characterSelected)
        {
            return packetParam == SoftCloseQueryParam &&
                characterSelected &&
                requestedSessionId > 0 &&
                requestedSessionId == authenticatedSessionId &&
                !string.IsNullOrEmpty(requestedAccount) &&
                string.Equals(requestedAccount, authenticatedAccount, StringComparison.Ordinal);
        }

        private static bool TryDecodeUserPacket(string sData, out ushort packetIdent, out int packetSessionId,
            out ushort packetParam, out string body, out int headLen)
        {
            packetIdent = 0;
            packetSessionId = 0;
            packetParam = 0;
            body = string.Empty;
            headLen = 0;

            if (string.IsNullOrEmpty(sData)) return false;

            // 尝试 EDcode 16字节头 (标准格式)
            if (TryDecodeEDCodePacket(sData, Grobal2.DEFBLOCKSIZE, out packetIdent,
                    out packetSessionId, out packetParam, out body))
            {
                headLen = Grobal2.DEFBLOCKSIZE;
                return IsSupported(packetIdent);
            }

            // 尝试 Legend 格式
            if (TryDecodeLegendPacket(sData, out packetIdent, out packetSessionId, out packetParam, out body))
            {
                headLen = EDcode.LegendDefBlockSize;
                return IsSupported(packetIdent);
            }

            return false;
        }

        private static bool TryDecodeEDCodePacket(string sData, int headLen, out ushort ident,
            out int sessionId, out ushort param, out string body)
        {
            ident = 0;
            sessionId = 0;
            param = 0;
            body = string.Empty;

            if (sData.Length < headLen) return false;

            var header = sData.Substring(0, headLen);
            var packet = EDcode.DecodePacket(header);
            if (packet == null) return false;

            ident = packet.Ident;
            sessionId = packet.Recog;
            param = packet.Param;
            body = sData.Substring(headLen);
            return true;
        }

        private static bool TryDecodeLegendPacket(string sData, out ushort ident, out int sessionId,
            out ushort param, out string body)
        {
            ident = 0;
            sessionId = 0;
            param = 0;
            body = string.Empty;

            if (sData.Length < EDcode.LegendDefBlockSize) return false;

            var header = sData.Substring(0, EDcode.LegendDefBlockSize);
            var packet = EDcode.DecodeLegendPacket(header);
            if (packet == null) return false;

            ident = packet.Ident;
            sessionId = packet.SessionID;
            param = packet.Param;
            body = sData.Substring(EDcode.LegendDefBlockSize);
            return true;
        }

        private static bool IsSupported(ushort ident)
        {
            for (var i = 0; i < SupportedUserCommands.Length; i++)
                if (SupportedUserCommands[i] == ident) return true;
            return false;
        }

        // ===================== Ticket 解析 =====================

        private static string ResolveMobileTicket(string ticket)
        {
            if (string.IsNullOrEmpty(ticket)) return null;
            try
            {
                using var conn = new MySqlConnection(DBShare.DBConnection);
                conn.Open();
                using (var session = new MySqlCommand(
                           "SET WAIT_TIMEOUT = 2073600;", conn))
                    session.ExecuteNonQuery();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"SELECT t.pt_id FROM account.ticket t
                    WHERE BINARY t.ticket=BINARY @t AND t.create_time>@exp LIMIT 1";
                cmd.Parameters.AddWithValue("@t", ticket);
                cmd.Parameters.AddWithValue("@exp", DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 300);
                var result = cmd.ExecuteScalar()?.ToString();
                if (!string.IsNullOrEmpty(result)) return result;
            }
            catch (Exception ex)
            {
                DBShare.MainOutMessage("[MobileAuth] ticket解析失败: " + ex.Message);
            }
            // Ticket validation failed — do NOT fall through to use raw ticket as account name
            return null;
        }

        private static string GetPtid(string account)
        {
            try
            {
                using var conn = new MySqlConnection(DBShare.DBConnection);
                conn.Open();
                using (var session = new MySqlCommand(
                           "SET WAIT_TIMEOUT = 2073600;", conn))
                    session.ExecuteNonQuery();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT pt_id FROM account.normal WHERE uid=@uid LIMIT 1";
                cmd.Parameters.AddWithValue("@uid", account);
                return cmd.ExecuteScalar()?.ToString();
            }
            catch (Exception ex) { Debug.WriteLine("GetPtid failed: " + ex.Message); return null; }
        }

        /// <summary>
        /// 检查是否为GM账号 (通过 GameMaster.txt 列表判断)。
        /// 对应 Delphi 原版 GM创建: Level=40, AdminLevel=5。
        /// </summary>
        private static bool IsGmAccount(string account)
        {
            try
            {
                if (!File.Exists("GameMaster.txt")) return false;
                var lines = File.ReadAllLines("GameMaster.txt", System.Text.Encoding.GetEncoding("GBK"));
                foreach (var line in lines)
                {
                    var name = line.Trim();
                    if (string.Compare(name, account, StringComparison.OrdinalIgnoreCase) == 0)
                        return true;
                }
            }
            catch (Exception ex) { Debug.WriteLine("IsGmAccount failed: " + ex.Message); }
            return false;
        }

        /// <summary>
        /// 处理预注册奖励。
        [Conditional("DBSVR_PROTOCOL_TRACE")]
        private static void Log(string msg)
        {
            Debug.WriteLine(msg);
        }

        /// 对应 Delphi 原版 awardplayers 表:
        ///   SELECT * WHERE PTID=%s AND Status=0
        ///   UPDATE SET Status=1, HumName=%s WHERE Idx=%d
        /// </summary>
        private static void ProcessAwardPlayer(string ptid, string chrName)
        {
            try
            {
                using var conn = new MySqlConnection(DBShare.DBConnection);
                conn.Open();
                using (var session = new MySqlCommand(
                           "SET WAIT_TIMEOUT = 2073600;", conn))
                    session.ExecuteNonQuery();
                // 查找未领取的奖励
                using var sel = new MySqlCommand(
                    "SELECT Idx FROM gamedata.awardplayers WHERE PTID=@p AND Status=0 LIMIT 1", conn);
                sel.Parameters.AddWithValue("@p", ptid);
                var obj = sel.ExecuteScalar();
                if (obj != null && obj != DBNull.Value)
                {
                    int idx = Convert.ToInt32(obj);
                    using var upd = new MySqlCommand(
                        "UPDATE gamedata.awardplayers SET Status=1, HumName=@h WHERE Idx=@i", conn);
                    upd.Parameters.AddWithValue("@h", chrName);
                    upd.Parameters.AddWithValue("@i", idx);
                    upd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                DBShare.MainOutMessage($"[AwardPlayer] 错误: {ex.Message}");
            }
        }

        // ===================== 配置加载 =====================

        private void LoadServerInfo()
        {
            if (!File.Exists("ServerInfo.txt")) return;

            var loadList = new StringList();
            loadList.LoadFromFile("ServerInfo.txt");
            if (loadList.Count <= 0) return;

            int nRouteIdx = 0;
            for (var i = 0; i < loadList.Count; i++)
            {
                var sLineText = loadList[i].Trim();
                if (string.IsNullOrEmpty(sLineText) || sLineText.StartsWith(";")) continue;

                string sSelGateIPaddr = string.Empty;
                string sGameGate = HUtil32.GetValidStr3(sLineText, ref sSelGateIPaddr, new[] { " ", "\09" });
                if (string.IsNullOrEmpty(sGameGate) || string.IsNullOrEmpty(sSelGateIPaddr)) continue;

                DBShare.g_RouteInfo[nRouteIdx] = new TRouteInfo { sSelGateIP = sSelGateIPaddr.Trim() };
                int nGateIdx = 0;
                while (!string.IsNullOrEmpty(sGameGate) && nGateIdx < 8)
                {
                    string sGameGateIPaddr = string.Empty;
                    string sGameGatePort = string.Empty;
                    sGameGate = HUtil32.GetValidStr3(sGameGate, ref sGameGateIPaddr, new[] { " ", "\09" });
                    sGameGate = HUtil32.GetValidStr3(sGameGate, ref sGameGatePort, new[] { " ", "\09" });
                    DBShare.g_RouteInfo[nRouteIdx].sGameGateIP[nGateIdx] = sGameGateIPaddr.Trim();
                    DBShare.g_RouteInfo[nRouteIdx].nGameGatePort[nGateIdx] = HUtil32.Str_ToInt(sGameGatePort, 0);
                    nGateIdx++;
                }
                DBShare.g_RouteInfo[nRouteIdx].nGateCount = nGateIdx;
                nRouteIdx++;
            }

            // 加载地图配置
            DBShare.sMapFile = _configManager.ReadString("Setup", "MapInfoFile", string.Empty);
            _mapList.Clear();
            if (File.Exists(DBShare.sMapFile))
            {
                loadList.Clear();
                loadList.LoadFromFile(DBShare.sMapFile);
                for (var i = 0; i < loadList.Count; i++)
                {
                    string sLine = loadList[i];
                    if (string.IsNullOrEmpty(sLine) || sLine[0] != '[') continue;

                    string sMapName = string.Empty;
                    string sMapInfo = HUtil32.ArrestStringEx(sLine, "[", "]", ref sMapName);
                    sMapInfo = HUtil32.GetValidStr3(sMapName, ref sMapName, new[] { " ", "\09" });
                    string sServerIndex = HUtil32.GetValidStr3(sMapInfo, ref sMapInfo, new[] { " ", "\09" }).Trim();
                    _mapList[sMapName] = HUtil32.Str_ToInt(sServerIndex, 0);
                }
            }
        }

        private bool LoadChrNameList(string sFileName)
        {
            if (!File.Exists(sFileName)) return false;

            DBShare.DenyChrNameList.LoadFromFile(sFileName);
            int i = 0;
            while (i < DBShare.DenyChrNameList.Count)
            {
                if (string.IsNullOrEmpty(DBShare.DenyChrNameList[i].Trim()))
                {
                    DBShare.DenyChrNameList.RemoveAt(i);
                    continue;
                }
                i++;
            }
            return true;
        }
    }

    public class UsrSocMessage
    {
        public string Text;
        public TGateInfo GateInfo;
    }
}
