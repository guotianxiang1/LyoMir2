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
        private const int MobileAdmissionPaymentState = 2;
        private const int MobileAdmissionPayMode = 5;
        private readonly IList<TGateInfo> _gateList;
        private Dictionary<string, int> _mapList;
        private readonly IPlayDataService _playDataService;
        private readonly IPlayRecordService _playRecordService;
        private readonly IHeroRecordService _heroRecordService;
        private readonly SensitiveWordFilter _sensitiveWordFilter;
        private readonly WhitelistService _whitelistService;
        private readonly ISocketServer _userSocket;
        private readonly LoginSvrService _loginService;
        private readonly GameSocService _gameSocService;
        private readonly ConfigManager _configManager;
        private readonly NativeUserAdmissionControl _nativeAdmission;
        private readonly NativeYbConsumeGate _nativeYbConsumeGate;
        private readonly NativeAdmissionQueue _nativeAdmissionQueue = new();
        private readonly NativeSelectEntryProtocol _selectEntryProtocol;
        private readonly NativeAccountOwnerRegistry _nativeAccountOwners;
        private readonly object _nativeAccountTakeoverSync = new();
        private readonly object _nativeAdmissionRefreshSync = new();
        private readonly ManualResetEvent _nativeAdmissionRefreshStop = new(false);
        private Thread _nativeAdmissionRefreshThread;
        // 角色改名三库级联（原版 fn_5A8DDC 主档 + fn_5A923C 的 22 条级联）
        private readonly INativeRenameCascadeService _renameCascade;
        private readonly object _gateLock = new();

        // Native UserSoc workers receive Delphi AnsiString values built with
        // LStrFromPChar/strlen (0x404DF0).  That conversion terminates at the
        // first NUL, not only at a final padding terminator.  Keep this rule
        // isolated to the Native77 transport; the private percent/dollar
        // transport retains its established TrimEnd compatibility behavior.
        private static string DecodeNativeCString(string encoded)
        {
            var decoded = EDcode.DeCodeString(encoded)?.TrimEnd('\0')
                ?? string.Empty;
            var nul = decoded.IndexOf('\0');
            return nul >= 0 ? decoded.Substring(0, nul) : decoded;
        }

        private static string DecodeUserBodyString(string encoded,
            TUserInfo userInfo)
        {
            if (userInfo != null
                && userInfo.WireMode == TGateWireMode.Native77)
                return DecodeNativeCString(encoded);
            return EDcode.DeCodeString(encoded)?.TrimEnd('\0')
                ?? string.Empty;
        }

        /// <summary>
        /// ⚠️ 这个名单**不是**全局 fail-closed 闸门。我先前在这里写过
        /// 「不登记则请求根本进不到 switch」——那句话是错的，已按调用点推翻：
        ///
        /// <see cref="IsSupported"/> 只在 <see cref="TryDecodeUserPacket"/> 内被调用两次，
        /// 把关的是两条**文本编码**路径：EDcode 16 字节头、Legend 格式。
        /// 而生产线路是原版 0x77 帧 —— 它走
        ///   0x77 帧解析 -> MobileCmdMap.ToServer(dataMessage.Ident)
        ///              -> ProcessDecodedUserPacket(...)
        /// （见本文件 :496-499），**完全绕过本名单**。
        /// 所以在 native 线上，可达性判据只有一条：switch 里有没有活的 case。
        ///
        /// 另一个必须知道的陷阱：MobileCmdMap.ToServer 会在进 switch **之前**
        /// 把 4002/4012/4013/4014/4015/4017 改写成 104/101/102/105/106/103。
        /// 故 `case 4012` 这类写法是**死代码**（假红）。4016 不在该映射表里，
        /// ToServer 的兜底是 `TryGetValue ? v : clientIdent`（原值透传），
        /// 所以 `case Grobal2.CM_RENAMECHR4016` 能命中 —— 这是巧合而非设计，
        /// 若日后把 4016 加进映射表，本文件的 case 会立刻变成死代码。
        /// </summary>
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
            Grobal2.CM_LOGIN_ALREADY_ONLINE,
            1018, // CM_LOGINNOTICEOK
            // 0xFB0 角色改名（原版内层派发 idx = 4016 - 0xFAC = 4 -> grp 5 ->
            // 0x5CE404 -> call fn_5CD2EC）。登记在此只对文本线路有效；
            // native 0x77 线靠上面 switch 里的 case 生效。
            Grobal2.CM_RENAMECHR4016,
        };

        public UserSocService(LoginSvrService loginService, IPlayRecordService playRecordService,
            IPlayDataService playDataService, IHeroRecordService heroRecordService,
            SensitiveWordFilter sensitiveWordFilter, WhitelistService whitelistService,
            ConfigManager configManager, GameSocService gameSocService,
            NativeUserAdmissionControl nativeAdmission,
            NativeYbConsumeGate nativeYbConsumeGate,
            INativeRenameCascadeService renameCascade,
            NativeSelectEntryProtocol selectEntryProtocol,
            NativeAccountOwnerRegistry nativeAccountOwners)
        {
            _renameCascade = renameCascade
                ?? throw new ArgumentNullException(nameof(renameCascade));
            _loginService = loginService;
            _gameSocService = gameSocService;
            _playRecordService = playRecordService;
            _playDataService = playDataService;
            _heroRecordService = heroRecordService;
            _sensitiveWordFilter = sensitiveWordFilter;
            _whitelistService = whitelistService;
            _configManager = configManager;
            _nativeAdmission = nativeAdmission
                ?? throw new ArgumentNullException(nameof(nativeAdmission));
            _nativeYbConsumeGate = nativeYbConsumeGate
                ?? throw new ArgumentNullException(nameof(nativeYbConsumeGate));
            _selectEntryProtocol = selectEntryProtocol
                ?? throw new ArgumentNullException(nameof(selectEntryProtocol));
            _nativeAccountOwners = nativeAccountOwners
                ?? throw new ArgumentNullException(nameof(nativeAccountOwners));
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
            _nativeAdmission.Attach(_nativeAccountOwners.SnapshotOwnerIps,
                DisconnectNativeGateByAddress, DrainNativeAdmissionQueue,
                DisconnectNativeUserByAccount,
                UpdateNativeOnlineAccountText,
                UpdateNativeOnlineAccountLoginTime,
                _nativeAccountOwners.WithOwnerIpSnapshot);
            _gameSocService.AttachNativeSwitchHandoffStore(
                StoreNativeSwitchHandoff);
            _gameSocService.AttachNativeSaveContinuation(
                ProcessNativeSaveContinuation);
        }

        private bool StoreNativeSwitchHandoff(string account,
            string characterName, byte[] extension)
        {
            if (string.IsNullOrEmpty(account)
                || string.IsNullOrEmpty(characterName)
                || extension == null
                || extension.Length != NativeDbServerProtocol.LoginExtensionSize)
                return false;
            lock (_gateLock)
            {
                foreach (var gate in _gateList)
                {
                    if (gate?.UserList == null) continue;
                    lock (gate.UserList)
                    {
                        foreach (var user in gate.UserList)
                        {
                            if (user == null
                                || user.WireMode != TGateWireMode.Native77
                                || !string.Equals(user.sAccount, account,
                                    StringComparison.OrdinalIgnoreCase))
                                continue;
                            if (user.NativeSwitchHandoff.TryStore(
                                    characterName, extension))
                                return true;
                        }
                    }
                }
            }
            return false;
        }

        private void ProcessNativeSaveContinuation(
            NativeSaveHumanContinuation continuation)
        {
            if (continuation == null
                || string.IsNullOrEmpty(continuation.Account)
                || string.IsNullOrEmpty(continuation.CharacterName))
                return;

            TUserInfo user = null;
            TGateInfo gate = null;
            lock (_gateLock)
            {
                foreach (var candidateGate in _gateList)
                {
                    if (candidateGate?.UserList == null) continue;
                    lock (candidateGate.UserList)
                    {
                        var candidate = candidateGate.UserList.FirstOrDefault(
                            item => item != null
                                && item.WireMode == TGateWireMode.Native77
                                && string.Equals(item.sAccount,
                                    continuation.Account,
                                    StringComparison.Ordinal)
                                && item.NativeSessionState != 7);
                        if (candidate == null) continue;
                        user = candidate;
                        gate = candidateGate;
                        break;
                    }
                    if (user != null) break;
                }
            }

            if (user == null || gate == null) return;

            // Native name mismatch is checked before the event switch.  A
            // pending state (5) silently ignores a stale callback; any other
            // state follows the native close path.
            var nameMatches = string.Equals(user.NativeCurrentCharName,
                continuation.CharacterName, StringComparison.Ordinal);
            if (!nameMatches)
            {
                if (user.NativeSessionState == 5) return;
                OutOfConnect(user, gate);
                return;
            }

            switch (continuation.Event)
            {
                case 1:
                    user.NativeRenameLatch = 0;
                    user.NativePendingRenameName = null;
                    user.NativeSessionState = 5;
                    SendCharacterList(user.sAccount, user);
                    user.boChrQueryed = true;
                    break;

                case NativeDbServerProtocol.SwitchSaveMode:
                    if (continuation.LoginExtension == null
                        || continuation.LoginExtension.Length
                            != NativeDbServerProtocol.LoginExtensionSize)
                    {
                        OutOfConnect(user, gate);
                        break;
                    }

                    var selected = SelectChr(
                        EDcode.EncodeString(continuation.CharacterName),
                        gate, ref user, out var handled,
                        continuation.LoginExtension);
                    if (selected) user.boChrSelected = true;
                    if (!handled) OutOfConnect(user, gate);
                    break;

                case 3:
                    OutOfConnect(user, gate);
                    break;

                default:
                    // The native router has no event-specific action for
                    // unknown non-zero values.
                    break;
            }
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
            lock (_nativeAccountTakeoverSync)
            {
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
                                    LegacyGbkText.Encode(
                                        user.sAccount ?? string.Empty));
                                if (userKey == key)
                                {
                                    target = (gate, user);
                                    break;
                                }
                            }
                        if (target.HasValue) break;
                    }
                if (!target.HasValue) return;
                if (TryRemoveNativeRoute(target.Value.Gate,
                        target.Value.User.NativeConnectionId,
                        target.Value.User, user =>
                        {
                            try
                            {
                                SendEncodedPacket(user,
                                    Grobal2.SM_OUTOFCONNECTION_4018,
                                    0, 0, 0, 0, null);
                            }
                            catch { }
                            return true;
                        }, false, false, out var removedUser))
                    CloseManagedLoginSession(removedUser);
            }
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
            lock (_nativeAccountTakeoverSync)
                DispatchNativeAdmissionActions(_nativeAdmissionQueue.Drain());
        }

        private IReadOnlyList<NativeAdmissionQueueAction>
            PrepareNativeAdmission(TUserInfo user)
        {
            user.NativeQueueBypass = false;
            user.NativeQueueEnqueueTick = 0;
            user.NativeQueuePosition = 0;
            if (!NativeAdmissionQueue.ShouldQueue(
                    _nativeAdmission.QueueEnabled,
                    _nativeAccountOwners.Count,
                    DBShare.NativeCountLimit,
                    _loginService.NativeAdmissionCapacity))
                return Array.Empty<NativeAdmissionQueueAction>();

            user.NativeQueueBypass = _nativeYbConsumeGate.IsExempt(
                user.sAccount, DBShare.VipYBConsume);
            if (user.NativeQueueBypass)
                return Array.Empty<NativeAdmissionQueueAction>();
            return _nativeAdmissionQueue.Enqueue(user,
                unchecked((uint)HUtil32.GetTickCount()));
        }

        private void ReleaseNativeAdmission(TUserInfo user)
        {
            if (user == null || !user.NativeAdmissionManaged) return;
            var actions = _nativeAdmissionQueue.RemoveForConnection(user,
                unchecked((uint)HUtil32.GetTickCount()));
            user.NativeAdmissionManaged = false;
            DispatchNativeAdmissionActions(actions);
        }

        private void CleanupNativeAdmissionAndOwnership(TUserInfo user,
            bool preservePendingTakeover)
        {
            CleanupNativeAdmissionAndOwnership(user, preservePendingTakeover,
                null);
        }

        private void CleanupNativeAdmissionAndOwnership(TUserInfo user,
            bool preservePendingTakeover, string accountOverride = null)
        {
            if (user == null) return;
            // 4041 clears Self+0x24 before the terminal sender runs.  Keep the
            // original key only for registry/IP cleanup; the user object still
            // follows the native cleared state.
            var cleanupAccount = accountOverride ?? user.sAccount;
            lock (_nativeAccountTakeoverSync)
            {
                var wasManaged = user.NativeAdmissionManaged;
                ReleaseNativeAdmission(user);
                if (preservePendingTakeover)
                    _nativeAccountOwners.ReleaseRegisteredOwnerByKey(
                        cleanupAccount);
                else if (wasManaged)
                    _nativeAccountOwners.ReleaseForConnection(
                        cleanupAccount, user);
                if (accountOverride == null)
                    ReleaseNativeIpAdmission(user);
                else
                    ReleaseNativeIpAdmission(user, accountOverride);
            }
        }

        private void DispatchNativeAdmissionActions(
            IReadOnlyList<NativeAdmissionQueueAction> actions)
        {
            if (actions == null) return;
            foreach (var action in actions)
            {
                var user = action?.User;
                if (user == null) continue;
                try
                {
                    if (action.Kind
                        == NativeAdmissionQueueActionKind.Position)
                    {
                        SendEncodedPacket(user, 4332, 0,
                            action.Position, action.QueueCount,
                            action.Series, null);
                    }
                    else
                    {
                        // Native drain sends 4018 on the user wire only. Route
                        // cleanup remains the connection callback's job.
                        SendEncodedPacket(user,
                            Grobal2.SM_OUTOFCONNECTION_4018,
                            0, 0, 0, 0, null);
                    }
                }
                catch (Exception ex)
                {
                    Log("[UserSoc] native admission notification failed: "
                        + ex.Message);
                }
            }
        }

        public void Start()
        {
            DBShare.MainOutMessage("=== DBSvr v3.0 [Full Rewrite] ===");
            _userSocket.Start(DBShare.g_sGateAddr, DBShare.g_nGatePort);
            StartNativeAdmissionRefreshWorker();
            DBShare.MainOutMessage($"数据库服务[{DBShare.g_sGateAddr}:{DBShare.g_nGatePort}]已启动.等待链接...");
        }

        public void Stop()
        {
            StopNativeAdmissionRefreshWorker();
            _userSocket.Shutdown();
            lock (_nativeAccountTakeoverSync)
            {
                var usersToCleanup = new List<TUserInfo>();
                var gatesToComplete = new List<TGateInfo>();
                lock (_gateLock)
                {
                    foreach (var gate in _gateList)
                    {
                        if (gate?.UserList == null) continue;
                        lock (gate.UserList)
                        {
                            foreach (var user in gate.UserList)
                            {
                                if (user == null) continue;
                                lock (user.NativeRouteLifecycleSync)
                                {
                                    user.NativeRouteActive = false;
                                    user.NativeSwitchHandoff.Reset();
                                }
                                usersToCleanup.Add(user);
                            }
                            gate.UserList.Clear();
                            gate.NativeRoutes.Clear();
                        }
                        gatesToComplete.Add(gate);
                    }
                    _gateList.Clear();
                }
                foreach (var user in usersToCleanup)
                    CleanupNativeAdmissionAndOwnership(user, false);
                foreach (var gate in gatesToComplete)
                    gate.NativeOutboundQueue?.Complete();
                _nativeAccountOwners.Clear();
                _nativeAdmission.ClearNativeIpCounts();
            }
        }

        private void StartNativeAdmissionRefreshWorker()
        {
            lock (_nativeAdmissionRefreshSync)
            {
                if (_nativeAdmissionRefreshThread?.IsAlive == true) return;
                _nativeAdmissionRefreshStop.Reset();
                _nativeAdmissionRefreshThread = new Thread(
                    ProcessNativeAdmissionRefresh)
                {
                    IsBackground = true,
                    Name = "DBSvr native admission refresh"
                };
                _nativeAdmissionRefreshThread.Start();
            }
        }

        private void StopNativeAdmissionRefreshWorker()
        {
            Thread worker;
            lock (_nativeAdmissionRefreshSync)
            {
                worker = _nativeAdmissionRefreshThread;
                if (worker == null) return;
                _nativeAdmissionRefreshStop.Set();
            }

            if (!ReferenceEquals(worker, Thread.CurrentThread)) worker.Join();
            lock (_nativeAdmissionRefreshSync)
                if (ReferenceEquals(_nativeAdmissionRefreshThread, worker))
                    _nativeAdmissionRefreshThread = null;
        }

        private void ProcessNativeAdmissionRefresh()
        {
            do
            {
                DispatchNativeAdmissionActions(_nativeAdmissionQueue.Refresh(
                    unchecked((uint)HUtil32.GetTickCount())));
            } while (!_nativeAdmissionRefreshStop.WaitOne(10));
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
            gateInfo.NativeOutboundQueue = new NativeGateOutboundQueue(
                frame => SendAll(e.Socket, frame),
                ex => Log("[UserSoc] native gate outbound failed: "
                    + ex.Message));
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
            lock (_nativeAccountTakeoverSync)
            {
                var usersToCleanup = new List<TUserInfo>();
                TGateInfo disconnectedGate = null;
                lock (_gateLock)
                {
                    for (var i = 0; i < _gateList.Count; i++)
                    {
                        if (_gateList[i]?.Socket == e.Socket)
                        {
                            var users = _gateList[i].UserList;
                            if (users != null) lock (users)
                            {
                                foreach (var user in users)
                                {
                                    if (user == null) continue;
                                    lock (user.NativeRouteLifecycleSync)
                                    {
                                        user.NativeRouteActive = false;
                                        user.NativeSwitchHandoff.Reset();
                                    }
                                    usersToCleanup.Add(user);
                                }
                                users.Clear();
                            }
                            _gateList[i].NativeRoutes.Clear();
                            disconnectedGate = _gateList[i];
                            _gateList.RemoveAt(i);
                            break;
                        }
                    }
                }
                foreach (var user in usersToCleanup)
                    CleanupNativeAdmissionAndOwnership(user, false);
                disconnectedGate?.NativeOutboundQueue?.Complete();
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
                if (frame.QueryId <= 0 || gateIndex < 0)
                    throw new InvalidOperationException(
                        "native gate registration route values are out of range");
                var routePort = frame.QueryId;
                gateInfo.NativeRoutePort = routePort;
                var assignedGateId = DBShare.NativeGameGateRegistrations
                    .ResolveForRegistration(gateInfo.NativeRouteID,
                        gateInfo.sGateaddr, routePort);
                if (assignedGateId == 0)
                {
                    DBShare.MainOutMessage(
                        $"角色网关[{gateIndex}]未匹配配置: "
                        + $"{gateInfo.sGateaddr}:{routePort}");
                    gateInfo.Socket.Close();
                    return;
                }
                gateInfo.NativeRouteID = assignedGateId;
            }
            if (frame.Ident == NativeGateControlProtocol.OpenRequest)
            {
                if (frame.QueryId < 0 || frame.QueryId > ushort.MaxValue)
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

                if (frame.QueryId < 0 || frame.QueryId > ushort.MaxValue
                    || !gateInfo.NativeRoutes.TryGetNewest(
                        unchecked((ushort)frame.QueryId), out var userInfo))
                {
                    DBShare.MainOutMessage(
                        $"角色网关[{gateIndex}]原版77数据引用未知连接: qid={frame.QueryId}");
                    return;
                }

                lock (userInfo.NativeRouteLifecycleSync)
                {
                    if (!userInfo.NativeRouteActive) return;
                    userInfo.WireMode = TGateWireMode.Native77;
                    userInfo.NativeQueryId = frame.QueryId;
                }
                var serverIdent = MobileCmdMap.ToServer(dataMessage.Ident);
                ProcessDecodedUserPacket(gateInfo, ref userInfo, serverIdent,
                    dataMessage.Recog, dataMessage.Param,
                    EncodeRawBody(dataMessage.Body));
                return;
            }

            if (!NativeGateControlProtocol.TryCreateResponse(
                    frame, gateInfo.NativeRouteID, out var response)) return;
            if (!YbDbLegacy77Codec.TryEncode(response, out var wire, out var error))
                throw new InvalidOperationException(error);
            QueueNativeGateFrame(gateInfo, wire);
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
            lock (gateInfo.UserList)
            {
                string sUserIPaddr = string.Empty;
                string sGateIPaddr = HUtil32.GetValidStr3(sIP, ref sUserIPaddr, HUtil32.Backslash);
                var routeId = HUtil32.Str_ToInt(sConnId, 0);
                var nativeConnectionId = routeId is >= 0 and <= ushort.MaxValue
                    ? (ushort)routeId
                    : (ushort)0;

                // The private text protocol historically rejected duplicate
                // handles. Native 2.08 route keys are a head-insert multimap.
                if (gateInfo.WireMode != TGateWireMode.Native77)
                {
                    for (var i = 0; i < gateInfo.UserList.Count; i++)
                        if (gateInfo.UserList[i]?.sConnID == sConnId)
                            return;
                }

                var userInfo = new TUserInfo
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
                    // The C# GameGate uses its percent/dollar route id for the
                    // same client that it exposes to GameSvr as the native 77
                    // ConnID. Preserve that identity across both gate wire modes.
                    NativeQueryId = nativeConnectionId,
                    NativeConnectionId = nativeConnectionId,
                    NativeRouteActive = true,
                    NativeGateOwner = gateInfo,
                    NativeAuthTick = 0,
                    NativeAuthResponse = null,
                    NativeText102 = string.Empty,
                    NativeLoginDateTimeBits = 0,
                    sReconnectID = string.Empty
                };
                gateInfo.UserList.Add(userInfo);
                if (gateInfo.WireMode == TGateWireMode.Native77)
                    gateInfo.NativeRoutes.Register(nativeConnectionId, userInfo);
            }
        }

        private static void QueueNativeGateFrame(TGateInfo gateInfo,
            byte[] frame)
        {
            if (gateInfo?.NativeOutboundQueue == null
                || !gateInfo.NativeOutboundQueue.TryEnqueue(frame))
                throw new InvalidOperationException(
                    "native GameGate outbound queue is closed");
        }

        private void CloseUser(string sConnId, ref TGateInfo gateInfo)
        {
            if (gateInfo.WireMode == TGateWireMode.Native77)
            {
                if (int.TryParse(sConnId, out var nativeRouteId)
                    && nativeRouteId is >= 0 and <= ushort.MaxValue
                    && TryRemoveNativeRoute(gateInfo, (ushort)nativeRouteId,
                        null, null, false, false, out var nativeUser))
                    CloseManagedLoginSession(nativeUser);
                return;
            }

            TUserInfo removedUser = null;
            lock (gateInfo.UserList)
            {
                for (var i = 0; i < gateInfo.UserList.Count; i++)
                {
                    var userInfo = gateInfo.UserList[i];
                    if (userInfo?.sConnID == sConnId)
                    {
                        lock (userInfo.NativeRouteLifecycleSync)
                        {
                            userInfo.NativeRouteActive = false;
                            userInfo.NativeSwitchHandoff.Reset();
                        }
                        gateInfo.UserList.RemoveAt(i);
                        removedUser = userInfo;
                        break;
                    }
                }
            }
            if (removedUser != null)
                CloseManagedLoginSession(removedUser);
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
            if (userInfo.WireMode == TGateWireMode.Native77
                && userInfo.NativeSessionState == 7)
                return;

            // 排队位次门。原版在**两级 opcode 表之前**就拦，逐字（状态 5 入口）：
            //   0x5CE307  mov eax, [ebp-4]            ; Self
            //   0x5CE30A  cmp word [eax+0x9c], 0      ; 排队位次
            //   0x5CE312  jbe 0x5CE323                ; == 0 -> 正常派发
            //   0x5CE314  mov eax, [ebp-0x1c]         ; 报文头
            //   0x5CE317  cmp word [eax+4], 0xfc7     ; 位次 > 0 时只认 4039
            //   0x5CE31D  jne 0x5CE481                ; ★否则跳**静默**出口
            //   0x5CE323  movzx eax,[eax+4] / add eax,0xfffff054
            //   0x5CE32F  cmp eax,0x1d / ja 0x5CE46C  ; opcode 越界才走 4018 腿
            //
            // ⚠️ 这两条出口语义相反，我一度把它们混为一谈：
            //   0x5CE481 = 静默丢弃（排队中发了不该发的 opcode）
            //   0x5CE46C = 回 ident 4018（opcode 根本不在 0xFAC..0xFC9 表里）
            // 所以被排队门拦下的请求**不回包**，不能走上面那个 default 分支。
            //
            // 位次的唯一写者 0x5CFC90，其后 0x5CFC97 `cmp word [ebp-6],0xa / ja`
            // ⇒ 位次 <= 10 才发通知（opcode 0x10EC = 4332，帧由 0x5CFC24 组装：
            //   12 字节记录、ident=edx、Recog=ecx、三个 word 参数取自栈，
            //   经 call dword [ebx+0x60] 发出，与改名回包同一个虚发送）。
            //
            // 4039 在 MobileCmdMap 里是恒等映射（_toServer[4039] = 4039），
            // 故进 switch 时仍是 4039；Grobal2 没有对应常量，用字面量。
            if (userInfo.WireMode == TGateWireMode.Native77
                && userInfo.NativeSessionState == 5
                && userInfo.NativeQueuePosition > 0
                && ident != 4039)
            {
                Log($"[UserSoc] 排队中(位次 {userInfo.NativeQueuePosition})"
                    + $" 丢弃 opcode {ident}（原版 0x5CE31D 跳静默出口，不回包）");
                return;
            }

            switch (ident)
            {
                case 4004:
                    // State 5 does not dispatch 4004 in the native table. It
                    // falls through the common 4018/terminal route-clear leg.
                    // The account check also fails closed for the partial C#
                    // state model before a second account can overwrite the
                    // owner key held by this UserSoc.
                    if (userInfo.WireMode == TGateWireMode.Native77
                        && (userInfo.NativeSessionState != 0
                            || !string.IsNullOrEmpty(userInfo.sAccount)))
                    {
                        OutOfConnect(userInfo, gateInfo);
                        break;
                    }
                    userInfo.nSessionID = pktSessionId > 0 ? pktSessionId : userInfo.nSessionID;
                    ProcessMobileLoginAuth(body, userInfo.nSessionID, ref userInfo, ref gateInfo);
                    break;

                case 4039: // CM_SELCHR_EXIT — 返回登录/退出选角
                    SendEncodedPacket(userInfo, 4039, 0, 0, 0, 0, null);
                    break;

                case Grobal2.CM_LOGIN_ALREADY_ONLINE:
                    // Native fn_5D0714 branches on Param's low byte and
                    // returns false when that byte is zero after clearing the
                    // account/character state; the outer dispatcher then
                    // emits exactly one 4018. The non-zero leg replaces the
                    // proven account owner and clears the displaced route.
                    if (NativeLoginAlreadyOnlineProtocol
                        .UsesReturnToLoginLeg(packetParam))
                    {
                        // The native handler clears Self+0x24 before the
                        // terminal 4018 sender.  Preserve that key out of
                        // band so route teardown can still release the
                        // account/IP admission entries.
                        var cleanupAccount = userInfo.sAccount;
                        NativeLoginAlreadyOnlineProtocol.ResetForReturnToLogin(
                            userInfo);
                        OutOfConnect(userInfo, gateInfo, cleanupAccount);
                    }
                    else
                    {
                        ProcessNativeAccountTakeover(userInfo, gateInfo);
                    }
                    break;

                case Grobal2.CM_RENAMECHR4016: // 0xFB0 角色改名
                    if (!ProcessRenameChr(body, pktSessionId, gateInfo,
                            ref userInfo)
                        && userInfo.WireMode == TGateWireMode.Native77)
                        OutOfConnect(userInfo, gateInfo);
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
                    if (userInfo.WireMode == TGateWireMode.Native77)
                    {
                        if (!string.IsNullOrEmpty(userInfo.sAccount)
                            && _loginService.CheckSession(userInfo.sAccount,
                                userInfo.sUserIPaddr, userInfo.nSessionID))
                        {
                            var handled = ProcessNativeNewChr(body,
                                ref userInfo, out var created);
                            if (!handled)
                                OutOfConnect(userInfo, gateInfo);
                            else if (created)
                                SendCharacterList(userInfo.sAccount, userInfo);
                        }
                        else
                        {
                            OutOfConnect(userInfo, gateInfo);
                        }
                        break;
                    }

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
                        else { Log($"[NewChr] Session FAILED, OutOfConnect"); OutOfConnect(userInfo, gateInfo); }
                    }
                    else Log($"[NewChr] Tick too fast, skip");
                    break;

                case Grobal2.CM_DELCHR:
                    // Native 0x5CE3A7 has no elapsed-tick gate; the worker is
                    // entered for every non-empty request. Keep the legacy
                    // private transport's compatibility throttle unchanged.
                    var nativeDelete = userInfo.WireMode == TGateWireMode.Native77;
                    if (nativeDelete
                        || (HUtil32.GetTickCount() - userInfo.dwChrTick) > 1000)
                    {
                        if (!nativeDelete)
                            userInfo.dwChrTick = HUtil32.GetTickCount();
                        if (!string.IsNullOrEmpty(userInfo.sAccount) &&
                            _loginService.CheckSession(userInfo.sAccount, userInfo.sUserIPaddr, userInfo.nSessionID))
                        {
                            _lastDelChrResendList = false;
                            var deleteHandled = true;
                            try
                            {
                                deleteHandled = DelChr(body, ref userInfo);
                                userInfo.boChrQueryed = false;
                            }
                            catch (Exception ex) { Log($"[DelChr] Exception: {ex.Message}"); }
                            // 0x5CC927 cmp word [ebp-0x16],1 / 0x5CC92C jne 0x5CC997
                            // ⇒ 原版**只有删除真的成功**才重建并重发角色列表。
                            // 原来这里无条件重发，配额用尽/已待删/不是本账号时也会多发一帧。
                            if (deleteHandled && _lastDelChrResendList &&
                                userInfo.nSessionID > 0 && !string.IsNullOrEmpty(userInfo.sAccount))
                                QueryChr(EDcode.EncodeString(userInfo.sAccount + "/" + userInfo.nSessionID), ref userInfo, ref gateInfo);

                            // Native worker fn_5CC8B8 returns false when the
                            // name length is >14 bytes; the common dispatcher
                            // then emits the single 4018 terminal packet.
                            if (!deleteHandled && nativeDelete)
                                OutOfConnect(userInfo, gateInfo);
                        }
                        else OutOfConnect(userInfo, gateInfo);
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
                    else OutOfConnect(userInfo, gateInfo);
                    break;

                case Grobal2.CM_SELCHR:
                    if (userInfo.boChrQueryed)
                    {
                        var sessionOK = !string.IsNullOrEmpty(userInfo.sAccount) &&
                            _loginService.CheckSession(userInfo.sAccount, userInfo.sUserIPaddr, userInfo.nSessionID);
                        if (sessionOK)
                        {
                            var selected = SelectChr(body, gateInfo,
                                ref userInfo, out var handled);
                            if (selected)
                                userInfo.boChrSelected = true;
                            if (!handled
                                && userInfo.WireMode == TGateWireMode.Native77)
                                OutOfConnect(userInfo, gateInfo);
                        }
                        else OutOfConnect(userInfo, gateInfo);
                    }
                    break;

                default:
                    // ⚠️ 此前没有 default 分支 —— 未建模的 opcode 被**静默丢弃**。
                    // 原版不是静默的，逐字（内层派发的汇合出口）：
                    //   0x5CE46C  cmp byte [ebp-0xd], 0   ; handled 标志
                    //   0x5CE470  jne 0x5CE481            ; 已处理则跳过
                    //   0x5CE472  mov ecx, 0xfb2          ; ★0xFB2 = 4018
                    //   0x5CE477  xor edx, edx            ; dl = 0
                    //   0x5CE47C  call 0x5CC7B4
                    //   0x5CE481  xor eax,eax             ; ← 真正的静默出口在这里
                    // 所以未处理的 opcode 会收到一个 **ident=4018** 的回包。
                    //
                    // 0x5CC7B4 的完整语义（同一函数也被 grp7 = opcode 0xFC7/4039 复用，
                    // 见 0x5CE445 `mov ecx,0xfc7 / xor edx,edx / call 0x5CC7B4`）：
                    //   0x5CC7C7  cmp byte [self+8], 7 / je 0x5CC863
                    //             ; 已是状态 7 则只做收尾（印证 byte[Self+8] 值域含 7，
                    //             ;  不是我先前写的 0..5）
                    //   0x5CC7D6  mov edx,0xc / call 0x4036E8   ; 12 字节记录清零
                    //   0x5CC7E0  mov ax,[ebp-0xc] / mov [ebp-0x18],ax
                    //             ; ident = ecx 低 16 位；Recog/Param 保持 0
                    //   0x5CC7FC  call dword [ebx+0x60]         ; 与改名回包同一个虚发送
                    //   0x5CC7FF  cmp byte [ebp-5],0 / jne      ; dl==0 才继续下面
                    //   0x5CC822  mov dx,0x271c / call 0x5D1CF8 ; 推 10012 消息
                    //   0x5CC82E  cmp byte [self+8],6 / jne     ; 仅状态 6
                    //   0x5CC837  cmp byte [self+0x19],0 / jbe
                    //   0x5CC857  call 0x59D70C                 ; 经 [[0x5DA0E0]] 外发
                    //   0x5CC85F  mov byte [self+8], 7          ; ★状态推进到 7
                    //
                    Log($"[UserSoc] 未建模 opcode {ident} -> 回 4018 并关闭原生会话");
                    if (userInfo.WireMode == TGateWireMode.Native77)
                        OutOfConnect(userInfo, gateInfo);
                    else
                        SendEncodedPacket(userInfo,
                            Grobal2.SM_OUTOFCONNECTION_4018,
                            0, 0, 0, 0, null);
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
                var decodeError = string.Empty;
                if (sessionId == 0
                    || !NativeMobileLoginAuthCodec.TryDecode(body,
                        out var nativeRequest, out decodeError))
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
            // Removed ResolveMobileTicket per account_schema_ownership_20260811.md —
            // ticket resolution belongs to LoginGate, not DBServer. Use ticket as-is.
            var account = ticket;
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
            TUserInfo currentUser;
            TGateInfo currentGate;
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

                currentUser = userInfo;
                currentGate = gateInfo;
                userInfo.NativeAuthResponse = response;
                userInfo.NativeText102 = response.Text102;
                Interlocked.Exchange(ref userInfo.NativeLoginDateTimeBits,
                    loginDateTimeBits);
                userInfo.NativeAuthTick = HUtil32.GetTickCount();
            }
            CompleteMobileLoginAuth(response.Account, sessionId,
                ref currentUser, ref currentGate);
        }

        private void CompleteMobileLoginAuth(string account, int sessionId,
            ref TUserInfo userInfo, ref TGateInfo gateInfo)
        {

            Log($"[MobileAuth] Opening session for {account}");
            _loginService.OpenMobileSession(account, userInfo.sUserIPaddr, sessionId);
            if (_loginService.Mode == LoginGateTransportMode.Native77Client
                && !_loginService.TrySendSocketMsg(Grobal2.SS_OPENSESSION,
                    $"{account}/{sessionId}/{MobileAdmissionPaymentState}/" +
                    $"{MobileAdmissionPayMode}/{userInfo.sUserIPaddr}"))
            {
                _loginService.CloseSession(account, sessionId);
                Log("[MobileAuth] GameSvr session channel is unavailable");
                SendMobileLoginAuth(userInfo, -1, 0, "认证失败");
                return;
            }
            userInfo.sAccount = account;
            userInfo.nSessionID = sessionId;

            Log($"[MobileAuth] OK account={account} session={sessionId}");
            SendMobileLoginAuth(userInfo, 0, 1, null);

            var querySucceeded = QueryChr(
                EDcode.EncodeString(account + "/" + sessionId),
                ref userInfo, ref gateInfo);
            if (querySucceeded)
                userInfo.boChrQueryed = true;

            var ownerExists = false;
            var admissionRejected = false;
            var admissionAborted = false;
            if (querySucceeded
                && _loginService.Mode == LoginGateTransportMode.Native77Client
                && userInfo.WireMode == TGateWireMode.Native77)
            {
                lock (_nativeAccountTakeoverSync)
                {
                    lock (userInfo.NativeRouteLifecycleSync)
                    {
                        if (!userInfo.NativeRouteActive
                            || !ReferenceEquals(userInfo.NativeGateOwner,
                                gateInfo))
                        {
                            admissionAborted = true;
                        }
                        else
                        {
                            ownerExists = _nativeAccountOwners
                                .HasRegisteredAccount(account);
                            if (!ownerExists)
                            {
                                var queueActions = PrepareNativeAdmission(userInfo);
                                DispatchNativeAdmissionActions(queueActions);
                                ownerExists = !_nativeAccountOwners.TryClaim(account,
                                    userInfo, out _);
                                if (!ownerExists)
                                {
                                    userInfo.NativeAdmissionManaged = true;
                                    Interlocked.Exchange(
                                        ref userInfo.NativeIpCounted, 1);
                                    if (_nativeAdmission.TryIncrementNativeOwnerIp(
                                            userInfo.sUserIPaddr,
                                            DBShare.MaxSingleIpHumanCount,
                                            IsNativeSingleIpException))
                                    {
                                        userInfo.NativeSessionState = 5;
                                    }
                                    else
                                    {
                                        admissionRejected = true;
                                    }
                                }
                                else if (userInfo.NativeQueuePosition > 0)
                                {
                                    userInfo.NativeAdmissionManaged = true;
                                    ReleaseNativeAdmission(userInfo);
                                }
                            }
                        }
                    }
                    if (admissionRejected)
                        TrySendNativeTerminalRoute(userInfo,
                            Grobal2.SM_OUTOFCONNECTION_4018);
                }
            }

            if (admissionAborted)
            {
                CloseManagedLoginSession(userInfo);
                return;
            }
            if (admissionRejected)
                return;

            SendEncodedPacket(userInfo,
                NativeLoginAlreadyOnlineProtocol.Command,
                ownerExists ? 1 : 0, 0, 0, 0, null);
        }

        private void ProcessNativeAccountTakeover(TUserInfo candidate,
            TGateInfo gateInfo)
        {
            if (candidate == null
                || candidate.WireMode != TGateWireMode.Native77
                || _loginService.Mode != LoginGateTransportMode.Native77Client)
                return;

            var admissionRejected = false;
            NativeAccountOwnerTakeover takeover;

            // The native manager executes this owner-map transition as one
            // ordered state machine. Serialize confirmations before taking a
            // candidate lifecycle lock so two gates cannot expose a managed
            // PendingOwner state or invert the displaced-owner lock order.
            lock (_nativeAccountTakeoverSync)
            {
                lock (candidate.NativeRouteLifecycleSync)
                    if (candidate.WireMode != TGateWireMode.Native77
                        || !candidate.NativeRouteActive
                        || string.IsNullOrEmpty(candidate.sAccount))
                        return;

                // fn_5D0714 performs the 0x5A2C40 membership probe first. A
                // miss is handled silently. fn_5A14A8 then performs a second
                // lookup and still installs the candidate if that owner
                // disappeared between the two calls.
                if (!_nativeAccountOwners.HasRegisteredAccount(
                        candidate.sAccount))
                    return;
                if (!_nativeAccountOwners.TryBeginObservedTakeover(
                        candidate.sAccount, candidate, out takeover))
                    return;

                var displaced = takeover.DisplacedOwner;
                if (displaced != null)
                {
                    var displacedGate = displaced.NativeGateOwner;
                    var displacedRoute = displaced.NativeConnectionId;
                    if (!TryRemoveNativeRoute(displacedGate, displacedRoute,
                            displaced, removed =>
                            {
                                lock (removed.NativeRouteLifecycleSync)
                                {
                                    if (removed.WireMode
                                        == TGateWireMode.Native77)
                                    {
                                        if (removed.NativeSessionState != 7)
                                        {
                                            try
                                            {
                                                SendEncodedPacket(removed,
                                                    NativeLoginAlreadyOnlineProtocol
                                                        .KickoutCommand,
                                                    0, 0, 0, 0, null);
                                            }
                                            catch (Exception ex)
                                            {
                                                Log("[UserSoc] 4040 displaced-owner send failed: "
                                                    + ex.Message);
                                            }
                                            removed.NativeSessionState = 7;
                                        }
                                    }
                                    else
                                    {
                                        removed.NativeSessionState = 7;
                                    }
                                }
                                return true;
                            }, true, true, out var removedDisplaced)
                        || !ReferenceEquals(removedDisplaced, displaced))
                        {
                            _nativeAccountOwners.CancelTakeover(takeover);
                            return;
                        }
                }

                var takeoverCompleted = false;
                lock (candidate.NativeRouteLifecycleSync)
                {
                    if (!candidate.NativeRouteActive)
                    {
                        _nativeAccountOwners.CancelTakeover(takeover);
                        return;
                    }
                    var queueActions = PrepareNativeAdmission(candidate);
                    DispatchNativeAdmissionActions(queueActions);
                    takeoverCompleted = _nativeAccountOwners
                        .CompleteTakeover(takeover);
                    if (takeoverCompleted)
                    {
                        candidate.NativeAdmissionManaged = true;
                        Interlocked.Exchange(
                            ref candidate.NativeIpCounted, 1);
                        if (_nativeAdmission.TryIncrementNativeOwnerIp(
                                candidate.sUserIPaddr,
                                DBShare.MaxSingleIpHumanCount,
                                IsNativeSingleIpException))
                        {
                            candidate.NativeSessionState = 5;
                        }
                        else
                        {
                            admissionRejected = true;
                        }
                    }
                    else if (candidate.NativeQueuePosition > 0)
                    {
                        candidate.NativeAdmissionManaged = true;
                        ReleaseNativeAdmission(candidate);
                    }
                }
                if (admissionRejected)
                    TrySendNativeTerminalRoute(candidate,
                        Grobal2.SM_OUTOFCONNECTION_4018);
            }

        }

        private bool IsNativeSingleIpException(string ip) =>
            _whitelistService.IsNativeWhiteListed(ip)
            || _whitelistService.IsNativeGameMasterIpAllowed(ip);

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
            // Removed GetPtid per account_schema_ownership_20260811.md — the native
            // DBServer does not map uid->pt_id; it uses sAccount directly.
            SendCharacterList(sAccount, userInfo);
            return true;
        }

        private void SendCharacterList(string account, TUserInfo userInfo)
        {
            var chrList = _playRecordService.QueryChrByPtid(account);
            Log($"[QueryChr] Found {chrList.Count} chars for account={account}");
            // Character rows are authoritative data. Encoding problems must never be
            // treated as permission to delete a character.

            int nChrCount = Math.Min(chrList.Count,
                NativeCharacterListCodec.MaxRows);
            NativePendingAward pendingAward = null;
            if (userInfo.WireMode == TGateWireMode.Native77
                && nChrCount < NativeCharacterListCodec.MaxRows)
                TryReadPendingAward(account, out pendingAward);
            var nativeRowCount = nChrCount + (pendingAward == null ? 0 : 1);
            byte[] chrBody = new byte[
                NativeCharacterListCodec.RowSize * nativeRowCount];
            for (int i = 0; i < nChrCount; i++)
            {
                var chr = chrList[i];
                byte[] nb = System.Text.Encoding.GetEncoding(936).GetBytes(chr.ChrName ?? "");
                int off = i * NativeCharacterListCodec.RowSize;
                NativeCharacterListCodec.WriteRow(
                    chrBody.AsSpan(off, NativeCharacterListCodec.RowSize),
                    nb, chr.Job, chr.Sex, chr.Level);
            }
            if (pendingAward != null)
            {
                var off = nChrCount * NativeCharacterListCodec.RowSize;
                NativeCharacterListCodec.WriteRow(
                    chrBody.AsSpan(off, NativeCharacterListCodec.RowSize),
                    ReadOnlySpan<byte>.Empty, pendingAward.Job,
                    pendingAward.Sex, pendingAward.Level);
            }
            ushort selectedIndex = 0;
            for (int i = 0; i < nChrCount; i++)
            {
                if (chrList[i].IsSelect)
                    selectedIndex = (ushort)i;
            }
            Log($"[QueryChr] chrBody({chrBody.Length}B) hex={BitConverter.ToString(chrBody).Replace("-"," ")}");
            SendEncodedPacket(userInfo, Grobal2.SM_QUERYCHR,
                1, (ushort)nativeRowCount, selectedIndex, 0, chrBody);
        }

        // ===================== 创建角色 (CM_NEWCHR) =====================

        private bool ProcessNativeNewChr(string sData,
            ref TUserInfo userInfo, out bool created)
        {
            created = false;
            var body = DecodeRawBody(sData);
            if (!NativeNewCharacterProtocol.TryDecode(body, out var request))
                return false;

            var result = NativeNewCharacterProtocol.ValidateFixedGates(
                request,
                creationDisabled: false,
                managerCreationDisabled: false,
                allowJobThree: false);
            var characterName = string.Empty;
            try
            {
                if (result == NativeNewCharacterProtocol.ResultPending)
                {
                    characterName = LegacyGbkText.Decode(request.NameBytes);
                    // The native 0x5CCDE4 gate has already validated the raw
                    // GBK bytes.  Do not apply the separate managed hero-name
                    // policy here: its prefix/byte rules are not present in
                    // the 2.08 CM_NEWCHR path and would reject valid names.
                }

                if (result == NativeNewCharacterProtocol.ResultPending
                    && (_playRecordService.IsChrNameExists(characterName)
                        || _heroRecordService.IsHeroNameExists(characterName)))
                    result = NativeNewCharacterProtocol.ResultDuplicateOrFailure;

                if (result == NativeNewCharacterProtocol.ResultPending
                    && _loginService.IsNativeAccountCrossServerLocked(
                        userInfo.sAccount))
                    result = NativeNewCharacterProtocol.ResultAccountLocked;

                if (result == NativeNewCharacterProtocol.ResultPending)
                {
                    var job = (int)request.Job;
                    var sex = (int)request.Sex;
                    var level = 0;
                    var activeCharacterCount = _playRecordService
                        .ChrCountOfAccount(userInfo.sAccount);
                    var hasAward = TryReadPendingAward(userInfo.sAccount,
                        out var award);
                    if (hasAward)
                    {
                        job = award.Job;
                        sex = award.Sex;
                        level = award.Level;
                        MarkPendingAwardClaimed(award.Idx, characterName);
                    }
                    else if (activeCharacterCount >= 4)
                    {
                        result = NativeNewCharacterProtocol.ResultCharacterLimit;
                    }

                    if (result == NativeNewCharacterProtocol.ResultPending)
                    {
                        var idx = _playRecordService.CreateCharacter(
                            userInfo.sAccount, characterName, job, sex,
                            hair: 0, level: level);
                        if (idx > 0)
                        {
                            var human = new THumDataInfo();
                            human.Header.sName = characterName;
                            human.Header.sAccount = userInfo.sAccount;
                            human.Data.sCharName = characterName;
                            human.Data.sAccount = userInfo.sAccount;
                            human.Data.btSex = (byte)sex;
                            human.Data.btJob = (byte)job;
                            human.Data.btHair = 0;
                            human.Data.Abil.Level = unchecked((ushort)level);
                            if (_playDataService.Add(ref human))
                            {
                                result = NativeNewCharacterProtocol.ResultSuccess;
                            }
                            else
                            {
                                _playRecordService.HardDelete(idx);
                                result = NativeNewCharacterProtocol
                                    .ResultDuplicateOrFailure;
                            }
                        }
                        else
                        {
                            result = NativeNewCharacterProtocol
                                .ResultDuplicateOrFailure;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result = NativeNewCharacterProtocol.ResultDuplicateOrFailure;
                DBShare.MainOutMessage(
                    $"[NativeNewChr] chr={characterName} account={userInfo.sAccount}: {ex.Message}");
            }

            SendEncodedPacket(userInfo, NativeNewCharacterProtocol.Command,
                0, result, 0, 0, null);
            created = result == NativeNewCharacterProtocol.ResultSuccess;
            var auth = userInfo.NativeAuthResponse;
            if (created
                && auth != null
                && NativeRelationLogProtocol.TryReadAuthField24(
                    auth.RawPayload, out var field24)
                && NativeRelationLogProtocol.TryReadAuthField28(
                    auth.RawPayload, out var field28))
            {
                // Native 0x5CD0D2/0x5CD0E1 passes the request's raw name and
                // the UserSoc Self+0x24/0x28 fields to 0x590A54.  Queue this
                // only when the authenticated Native77 record is available;
                // restore paths without that record remain fail-closed.
                _gameSocService.EnqueueNativeNewCharacterRelationLog(
                    field24, field28, request.Sex, request.NameBytes);
            }
            return true;
        }

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
                else if (_playRecordService.ChrCountOfAccount(sAccount) >= 4)
                {
                    nCode = 3; Log($"[NewChr] Max chars reached for {sAccount}");
                }
                else if (_playRecordService.TodayCreateCount(sAccount) >= 4)
                {
                    nCode = 3; Log($"[NewChr] Daily limit reached for {sAccount}");
                }
                else
                {
                    var ptid = sAccount;

                    // GM创建: 检查是否为GM账号
                    bool isGmCreate = IsGmAccount(sAccount);
                    int initLevel = isGmCreate ? 40 : 1;
                    int adminLevel = isGmCreate ? 5 : 0;

                    int idx = _playRecordService.CreateCharacter(ptid, sChrName, nJob, nSex, nHair, initLevel);
                    if (idx > 0)
                    {
                        try
                        {
                            // CreateGMCharacter (via _playRecordService) already sets Level=40
                            // and AdminLevel=5 in the INSERT per native 0x5A8124, so no UPDATE needed here.
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

        /// <summary>
        /// 原版删除角色 worker <c>fn_5A5978</c>（0x5A5978..0x5A5AB5）的返回码。
        /// 调用方 <c>fn_5CC8B8</c> 把它原样放进回包的 Param
        /// （<c>0x5CC8EF mov [ebp-0x16],ax</c> → <c>0x5CC90C mov [ebp-0x1E],ax</c>）。
        /// </summary>
        private enum NativeDelChrResult : ushort
        {
            /// <summary>0：名字没查到，或查到但不属于本账号。worker 的初值。</summary>
            NotFoundOrNotOwner = 0,

            /// <summary>1：删除成功（唯一会重发角色列表的码）。</summary>
            Deleted = 1,

            /// <summary>2：当日配额已用尽（0x5A5A36 cmp [eax+0x10],4 / jge）。</summary>
            QuotaExhausted = 2,

            /// <summary>3：该角色已处于待删除状态（0x5A5A3F cmp byte [eax+0x37],1 / je）。</summary>
            AlreadyPending = 3,

            /// <summary>
            /// 6：角色被跨服操作锁定。门在 <c>0x5A59F8 call 0x5AD85C</c>，读的是
            /// 角色记录的 <c>rec+0x1E</c>（0x5AD886 mov al,[eax+0x1e]）。
            /// 该标志只有一个置位者 0x5AEB94 和一个清位者 0x5AEBA8，
            /// 且两者各自只有一个调用点（0x5AD822 / 0x5AD852），最终都来自
            /// <c>0x59C970</c> —— 那个函数随后发一个 <c>0x33AABB77</c> 链路帧、
            /// 内层 ident <c>0x13D</c>（0x59CA3B mov word [eax],0x13d），即 ISM 跨服链路。
            /// 故语义 = 「该角色正被跨服转移/操作占用」。
            /// </summary>
            LockedByCrossServer = 6,

            /// <summary>
            /// 7：全局禁删开关打开。门在 <c>0x5A59DD mov eax,[0x5DA03C］/
            /// 0x5A59E2 cmp byte [eax],0 / je</c>，非 0 即拒。
            /// 该全局在 CODE 段有 12 处读点（同样的 <c>cmp byte [eax],0</c> 模式），
            /// 是一个进程级布尔开关。
            /// ⚠️ BLOCKED：**开关的生产者尚未定位**（写者不在可读 CODE 里，
            /// 引用它的配置装载函数被 VMP 虚拟化）。因此本 C# 侧没有对应变量，
            /// 该码恒不产生 —— 等价于「开关关闭」，与默认放行一致，不是伪造；
            /// 但「运营把它打开」这一路径是已知缺口，定位到生产者后再补。
            /// </summary>
            GloballyDisabled = 7,
        }

        /// <summary>
        /// 复刻 <c>CM_DELCHR</c>：worker <c>fn_5A5978</c> + 调用方 <c>fn_5CC8B8</c>。
        ///
        /// 原版把「判定」和「回包」分在两个函数里，回包形状由 fn_5CC8B8 决定：
        ///   0x5CC8D1  cmp eax,0x0E / jg  ⇒ 角色名 &gt; 14 字节直接退出，**不回包**
        ///   0x5CC902  mov word [ebp-0x20], 0x0FAD   ; ident 恒为 0x0FAD
        ///   0x5CC90C  mov word [ebp-0x1E], ax       ; worker 返回码放进 Param
        ///   0x5CC924  call dword [ebx+0x60]         ; 发包（无论成败都发这一个 ident）
        ///   0x5CC927  cmp word [ebp-0x16],1 / jne 0x5CC997
        ///                                           ; **只有码==1 才继续**重建并重发角色列表
        ///
        /// 故原版**没有**成功/失败两个不同 ident：一律 0x0FAD，区别只在 Param。
        /// 这里保留仓库既有的 SM_DELCHR_* 常量做映射（MobileCmdMap 把两者都映回
        /// 客户端 4013），但语义按原版统一成「一个 ident + 返回码」。
        /// </summary>
        private bool DelChr(string sData, ref TUserInfo userInfo)
        {
            var sChrName = DecodeUserBodyString(sData, userInfo);
            Log($"[DelChr] name='{sChrName}' sAccount={userInfo.sAccount}");

            // 0x5CC8C9 call 0x404EB8 (Length) / 0x5CC8D1 cmp eax,0x0E / 0x5CC8D4 jg
            // ⇒ 名字长度 > 14 时**静默退出，不回包**。原版量的是 GBK 字节数
            // （Delphi 的 Length 对 AnsiString 就是字节数），不是字符数。
            var nameBytes = LegacyGbkText.Encode(sChrName ?? string.Empty);
            if (nameBytes.Length > 0x0E)
            {
                Log($"[DelChr] name too long ({nameBytes.Length} bytes) -> native leaves handler unhandled");
                return false;
            }

            var result = DelChrWorker(sChrName, ref userInfo);
            Log($"[DelChr] result={result} ({(ushort)result})");

            // 0x5CC902/0x5CC90C/0x5CC924：ident 恒 0x0FAD，返回码进 Param。
            SendEncodedPacket(userInfo, Grobal2.SM_DELCHR_SUCCESS, 0,
                (ushort)result, 0, 0, null);

            // 0x5CC927 cmp word [ebp-0x16],1 / 0x5CC92C jne ⇒ 仅成功才重发列表。
            // 调用方（case CM_DELCHR）据此决定是否 QueryChr。
            _lastDelChrResendList = result == NativeDelChrResult.Deleted;
            return true;
        }

        /// <summary>
        /// 是否需要在 <c>DelChr</c> 之后重发角色列表。
        /// 复刻 <c>0x5CC927 cmp word [ebp-0x16],1 / jne 0x5CC997</c>：
        /// 只有 worker 返回 1（真的删掉了）才重建列表并发 0x0FAA。
        /// </summary>
        private bool _lastDelChrResendList;

        /// <summary>
        /// 复刻 worker <c>fn_5A5978</c>（0x5A5978..0x5A5AB5）的判定顺序。
        /// 顺序本身有意义，逐条对应：
        ///   0x5A599B  返回码初值 0
        ///   0x5A59AA  call 0x5AEF10        ; 按名字在 self+0x14 索引里找角色记录
        ///   0x5A59B6  找不到 → 直接返回 0
        ///   0x5A59D0  call 0x40AFB0        ; 比账号（**大小写不敏感**，a-z 折叠）
        ///   0x5A59D7  jne → 返回 0         ; 不是本账号的角色
        ///   0x5A59DD  全局开关 0x5DA03C    ; 非 0 → 返回 7
        ///   0x5A59F8  call 0x5AD85C        ; 跨服锁定 rec+0x1E → 返回 6
        ///   0x5A5A11  call 0x4034B0        ; today
        ///   0x5A5A21  跨日则计数清零
        ///   0x5A5A36  cmp [act+0x10],4 / jge → 返回 2
        ///   0x5A5A3F  cmp byte [rec+0x37],1 / je → 返回 3   ; 已在待删
        ///   0x5A5A4D  lastDay = today
        ///   0x5A5A55  count++
        ///   0x5A5A5B  byte[rec+0x36] = 0
        ///   0x5A5A62  byte[rec+0x37] = 1   ; 置待删标志
        ///   0x5A5A77  call vmt+0x14        ; 落库
        ///   0x5A5A7A  返回码 1
        ///
        /// ⚠️ 记账在置标志**之前**且无回滚：即使落库失败，当日配额也已消耗一个。
        /// 这是原版行为，不要「修正」。
        /// </summary>
        private NativeDelChrResult DelChrWorker(string sChrName, ref TUserInfo userInfo)
        {
            // 0x5A59AA：按名字找角色。找不到 → 0x5A59B6 返回 0。
            int nIndex = _playRecordService.Index(sChrName);
            if (nIndex < 0) return NativeDelChrResult.NotFoundOrNotOwner;

            bool boCheck = false;
            var humRecord = _playRecordService.Get(nIndex, ref boCheck);
            if (!boCheck || humRecord == null) return NativeDelChrResult.NotFoundOrNotOwner;

            // 0x5A59D0 call 0x40AFB0：原版这个比较把 'a'..'z' 减 0x20 折叠后再比，
            // 即**大小写不敏感**（0x40AFD6/0x40AFDB/0x40AFE0）。
            // 原来这里用 == 是大小写敏感的，会把 "Abc" 和 "abc" 判成不同账号。
            var ptid = userInfo.sAccount;
            bool owned =
                string.Equals(humRecord.sAccount, userInfo.sAccount, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(ptid) &&
                 string.Equals(humRecord.sAccount, ptid, StringComparison.OrdinalIgnoreCase));
            if (!owned) return NativeDelChrResult.NotFoundOrNotOwner;

            // 0x5A59DD 全局禁删开关 0x5DA03C → 返回 7。
            // BLOCKED：生产者未定位（见 NativeDelChrResult.GloballyDisabled 注释），
            // 故此处不产生 7，等价于开关关闭。

            // 0x5A59F8 call 0x5AD85C → 返回 6。
            // 0x5AD85C 读取账号记录 +0x1E；其唯一已证生产者是 0x019E
            // 跨服锁定链，C# 侧由 LoginSocService 保留同一 transient PTID
            // 状态。原生顺序是在全局禁删门之后、每日配额之前，故不能让
            // 锁定角色消耗删除配额，也不能把该状态并入 NativeBusy。
            if (_loginService.IsNativeAccountCrossServerLocked(
                    humRecord.sAccount))
                return NativeDelChrResult.LockedByCrossServer;

            // 0x5A5A3F cmp byte [rec+0x37],1 / je → 返回 3。
            // ⚠️ 原版这一判在配额门**之后**，但在记账之前；顺序见下。
            // 这里先取出待删状态，实际判定保持原版位置。
            bool alreadyPending = humRecord.boDeleted;

            // 0x5A5A11..0x5A5A3A：跨日重置 + 配额门（上限 4，jge）。
            // 配额键用账号：原版配额挂在账号对象上（0x5A5A1C mov eax,[eax] 解一层），
            // 不是挂在连接上，所以断线重连不会刷新配额。
            var quotaKey = !string.IsNullOrEmpty(ptid) ? ptid : userInfo.sAccount;

            // 原版的门与记账是同一段代码的两半：先判 >=4 拒（不记账），
            // 再判已待删拒（同样不记账），最后才 lastDay=today / count++。
            // 故这里必须「先只读判门，通过后再消费」，不能一上来就 TryConsume，
            // 否则 AlreadyPending 那条路径会被错误地扣掉一个配额。
            if (NativeDelCharQuota.UsedToday(quotaKey) >= NativeDelCharQuota.DailyLimit)
                return NativeDelChrResult.QuotaExhausted;

            if (alreadyPending) return NativeDelChrResult.AlreadyPending;

            // 0x5A5A4D/0x5A5A55：记账（lastDay = today, count++），在置标志之前。
            if (!NativeDelCharQuota.TryConsume(quotaKey))
                return NativeDelChrResult.QuotaExhausted;

            // 0x5A5A5B byte[rec+0x36]=0 / 0x5A5A62 byte[rec+0x37]=1 / 0x5A5A77 落库。
            // 原版**无回滚**：落库失败也不退配额、也仍然返回 1 之外的码前已扣数。
            // isSelect 与 isDelete 是同一条原生成功腿上的两个写点；不能只
            // 标记删除而保留旧的选中位，否则重载缓存时会把待删角色当成当前角色。
            humRecord.boSelected = 0;
            if (humRecord.Header != null)
            {
                humRecord.Header.nSelectID = 0;
                humRecord.Header.boDeleted = true;
            }
            humRecord.boDeleted = true;
            bool updated = _playRecordService.Update(nIndex, ref humRecord);
            Log($"[DelChr] Update={updated} quotaUsed={NativeDelCharQuota.UsedToday(quotaKey)}");

            // 0x5A5A7A：置标志成功即返回 1。原版把落库的返回值丢弃
            // （0x5A5A77 call vmt+0x14 之后直接 0x5A5A7A mov word [ebp-0xe],1），
            // 所以这里也不能因为 updated==false 就改码 —— 否则客户端会看到
            // 原版永远不会发的组合。
            return NativeDelChrResult.Deleted;
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
        private bool ProcessRenameChr(string sData, int packetRecog,
            TGateInfo gateInfo, ref TUserInfo userInfo)
        {
            // 极性判据逐字（分支体 0x5CE404 -> 校验层 0x5CD2EC）：
            //   0x5CE412  mov eax, [ebp-0x1c]   ; eax = 报文头指针
            //   0x5CE415  cmp dword ptr [eax],0 ; 比的是 **dword[msg+0]**
            //   0x5CE418  sete cl               ; cl = (dword[msg+0] == 0)
            //   0x5CE41E  call 0x5CD2EC
            //   0x5CD303  cmp byte [ebp-9],0 / 0x5CD307 je 0x5CD335
            //             ⇒ cl == 0 才走改名 ⇒ **dword[msg+0] != 0 才改名**
            //
            // dword[msg+0] 就是 Recog：LegacyGateType18 的 body 布局把 Recog 写在
            // 偏移 0（WriteInt32LittleEndian(span(0,4), Recog)），Param 在偏移 6。
            //
            // ⚠️ 我此前传的是 packetParam —— **读错字段**。原版读 Recog，
            // 而 Param 是另一个偏移（+6），两者无关。已按字节改为 Recog。
            if (packetRecog == 0)
            {
                var pendingName = NativeSelectEntryProtocol
                    .BeginRenameContinuation(userInfo);
                if (string.IsNullOrEmpty(pendingName)) return false;

                var selected = SelectChr(EDcode.EncodeString(pendingName),
                    gateInfo, ref userInfo, out var handled);
                if (selected)
                    userInfo.boChrSelected = true;
                return handled;
            }

            // Native 2.08 takes the new name from [ebp-8] and the old name
            // exclusively from Self+0x48.  The old slash/account fallback is
            // retained only for the private legacy transport.
            var decoded = DecodeUserBodyString(sData, userInfo);
            string oldName;
            string newName;
            if (userInfo.WireMode == TGateWireMode.Native77)
            {
                oldName = userInfo.NativePendingRenameName;
                newName = decoded;
            }
            else
            {
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
            }

            var gbkNewName = LegacyGbkText.Encode(newName);

            // 0x5CD335 cmp dword [ebp-8],0 / je 0x5CD53B —— 空名字**不回包**。
            if (NativeRenameCharProtocol.IsEmptyName(gbkNewName))
            {
                Log("[RenameChr] 空名字 -> 原版 0x5CD339 直接退出且不回包");
                return false;
            }

            if (string.IsNullOrEmpty(oldName))
            {
                // 原版 Self+0x48 为空时 0x5CD317 也是直接退出不回包。
                Log("[RenameChr] 会话无当前角色名 -> 不回包");
                return false;
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
                // 0x5CD3C4 sets the latch and 0x5CD497 clears Self+0x48.
                // Failed/empty rename attempts retain the pending name for retry.
                NativeSelectEntryProtocol.CompleteRename(userInfo);
                // 0x5CD3F2..0x5CD48F：改名成功才发 77BBAA33 内部转发（子命令 0x57）。
                // ⚠️ 该转发的出向通道（原版 [0x5DA0E0] 的 0x59E450）在本部署未接入，
                // 与 YBDB 6108 / GlobalServer 6020 同类。按原版对客户端侧仍回包，
                // 仅转发缺失，且**不伪造**一个通道。
                Log("[RenameChr] 原版此处发 77BBAA33 子命令 0x57 转发——外部通道未接入");
            }

            // 0x5CD49F..0x5CD4BF：回包 opcode 0xFB0，错误码在记录首 dword。
            SendEncodedPacket(userInfo, NativeRenameCharProtocol.ResponseCommand,
                result, 0, 0, 0, null);
            if (result == NativeRenameCharProtocol.ResultSuccess)
                SendCharacterList(userInfo.sAccount, userInfo);
            return true;
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
                var ptid = account;
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
            string ptid = sAccount;
            int nSessionID = userInfo.nSessionID;
            // CM_QUERYDELCHR 无 body，直接用 userInfo 中的账号信息
            Log($"[QueryDelChr] account={sAccount} ptid={ptid} session={nSessionID}");

            try
            {
                // 直接查 MySQL 获取完整 Job/Sex/Level
                var delList = new List<(string name, byte job, byte sex, int level)>();
                using (var conn = new MySqlConnection(DBShare.DBConnection))
                {
                    conn.Open();
                    using (var session = new MySqlCommand(
                               "SET WAIT_TIMEOUT = 2073600;", conn))
                        session.ExecuteNonQuery();
                    // LIMIT 200, not 10. The original builds this list purely in
                    // memory (fn_5A5398) and its cap is a BUFFER size, not a SQL
                    // clause -- there is no per-account character SELECT in the
                    // binary at all:
                    //   005A53E2  b8a00f0000      mov eax, 0xfa0   ; 4000 bytes
                    //   005A53F2  66c700c800      mov word [eax], 0xc8   ; = 200
                    //   005A541C  668138c800      cmp word [eax], 0xc8
                    //   005A5421  0f83cd000000    jae 0x5a54f4     ; stop at 200
                    // 0xFA0 / 0xC8 == 20 bytes per row, which is exactly the
                    // recSize used below -- independent corroboration of the cap.
                    // The original stops at 200 silently; it does not error, so a
                    // LIMIT is the faithful way to express the same ceiling.
                    // LIMIT 10 silently hid characters 11..200 from the client.
                    using var cmd = new MySqlCommand(
                        "SELECT ChrName, Job, Sex, Level FROM mir3.user_index WHERE PTID=@p AND IsDelete=1 LIMIT 200", conn);
                    cmd.Parameters.AddWithValue("@p", ptid);
                    using var dr = cmd.ExecuteReader();
                    while (dr.Read())
                    {
                        var name = LegacyGbkText.Read(dr, "ChrName");
                        if (string.IsNullOrEmpty(name)) continue;
                        delList.Add((name, (byte)dr.GetInt32("Job"),
                            (byte)dr.GetInt32("Sex"), dr.GetInt32("Level")));
                    }
                }
                int nChrCount = Math.Min(delList.Count,
                    NativeCharacterListCodec.MaxRows);
                byte[] chrBody = new byte[
                    NativeCharacterListCodec.RowSize * nChrCount];
                for (int i = 0; i < nChrCount; i++)
                {
                    var nb = System.Text.Encoding.GetEncoding(936).GetBytes(delList[i].name);
                    int off = i * NativeCharacterListCodec.RowSize;
                    NativeCharacterListCodec.WriteRow(
                        chrBody.AsSpan(off, NativeCharacterListCodec.RowSize),
                        nb, delList[i].job, delList[i].sex, delList[i].level);
                }
                SendEncodedPacket(userInfo, Grobal2.SM_QUERYDELCHR,
                    1, (ushort)nChrCount, 0, 0, chrBody);
            }
            catch (Exception ex) { Log($"[QueryDelChr] Error: {ex.Message}"); }
            return true;
        }

        // ===================== 恢复删除角色 (CM_RESDELCHR) =====================

        private void ResDelChr(string sData, ref TUserInfo userInfo)
        {
            string sAccount = userInfo.sAccount ?? "";
            string ptid = sAccount;
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

        private bool SelectChr(string sData, TGateInfo gateInfo,
            ref TUserInfo userInfo, out bool handled,
            byte[] directLoginExtension = null)
        {
            handled = false;
            string sAccount = userInfo.sAccount ?? "";
            var sChrName = DecodeUserBodyString(sData, userInfo);
            if (string.IsNullOrEmpty(sChrName)) return false;

            bool boDataOK = false;
            var nativeWire = userInfo.WireMode == TGateWireMode.Native77;
            ushort nativeFailureCode = 0;

            // The native 2.08 handler verifies that the requested name belongs
            // to the authenticated account before it classifies/loads the
            // character.  Keep the lookup result separate from the mutation so
            // an invalid cross-account request cannot clear the account's
            // existing selection bits.
            IList<TQuickID> chrList = new List<TQuickID>();
            string ptid = sAccount;
            var accountLookupSucceeded =
                _playRecordService.FindByAccount(ptid, ref chrList) >= 0;
            var accountRecords = new List<HumRecordData>(chrList.Count);
            if (accountLookupSucceeded)
            {
                foreach (var qid in chrList)
                {
                    bool gotIt = false;
                    var humRecord = _playRecordService.GetBy(qid.nIndex, ref gotIt);
                    if (!gotIt) continue;
                    accountRecords.Add(humRecord);
                }
            }

            var ownedRecord = accountRecords.FirstOrDefault(record =>
                string.Equals(record?.sChrName, sChrName,
                    StringComparison.Ordinal));
            ChrIndexInfo nativeRecord = null;
            var hasNativeRecord = nativeWire
                && _playRecordService.TryGetNativeCharacterByName(
                    LegacyGbkText.Encode(sChrName), out nativeRecord)
                && nativeRecord != null;
            var nativeDeletedRecordOwned = hasNativeRecord
                && string.Equals(nativeRecord.PTID, sAccount,
                    StringComparison.Ordinal)
                && (nativeRecord.DeleteState != 0 || nativeRecord.IsDelete);
            if (nativeWire && (!accountLookupSucceeded || ownedRecord == null))
            {
                // The native character record keeps deleted rows in the
                // account registry.  Its +0x37 byte is bDeleted, and the
                // loader eventually reports code 4 for that state.  A deleted
                // owned row passes this ownership gate, but the native loader
                // checks both result-6 locks before it checks bDeleted.
                if (!nativeDeletedRecordOwned)
                {
                    // Native ownership failure leaves the handler unhandled;
                    // the dispatcher emits one 4018 and closes the session.
                    // Do not invent a 4017 loader code for this pre-loader leg.
                    Log($"[SelectChr] native character is not owned by account: {sChrName}");
                    return false;
                }
            }

            if (nativeWire)
            {
                var entryStatus = _selectEntryProtocol.Classify(sChrName);
                if (entryStatus == NativeSelectEntryStatus.Denied)
                {
                    Log($"[SelectChr] native select-entry denied: {sChrName}");
                    return false;
                }

                if (entryStatus == NativeSelectEntryStatus.MustRename
                    && userInfo.NativeRenameLatch == 0)
                {
                    SendEncodedPacket(userInfo,
                        NativeRenameCharProtocol.RequestCommand,
                        0, 0, 0, 0, null);
                    NativeSelectEntryProtocol.BeginRenamePrompt(
                        userInfo, sChrName);
                    handled = true;
                    return false;
                }

                if (entryStatus == NativeSelectEntryStatus.NameNotAllowed)
                {
                    // Native fn_5CD544 status=3 sends a 4017/Param=1
                    // acknowledgement, then a 658 notice formatted from the
                    // NewZone template. Its common loader exit sets handled=1
                    // after the response, so the outer dispatcher must not
                    // append 4018/close.
                    SendEncodedPacket(userInfo,
                        Grobal2.CM_SELCHR4017, 0, 1, 0, 0, null);
                    if (_selectEntryProtocol.TryFormatNewZoneNotice(
                            sChrName, out var notice))
                    {
                        SendEncodedPacket(userInfo, Grobal2.SM_SENDNOTICE,
                            0, 0, 0, 0,
                            Encoding.GetEncoding(936).GetBytes(notice));
                    }
                    handled = true;
                    Log($"[SelectChr] native NewZone/AdminList rejection: {sChrName}");
                    return false;
                }

                // fn_5A777C's first proven result-6 predicate is
                // record+0x38 != 0.  The native type-3 cache maps that byte to
                // IsTransLock (the transfer-lock writer sets the same offset),
                // so this leg is handled with no synchronous packet.  Do not
                // add a retry/timeout/state-5 wait: the recovered image does
                // not prove such a continuation in this call path.
                if (hasNativeRecord && nativeRecord.IsTransLock)
                {
                    Log($"[SelectChr] native loader result 6 (record lock): {sChrName}");
                    handled = true;
                    return false;
                }

                // fn_5AD85C reads account-record+0x1E using the selected
                // character's PTID. Native 0x019E is its only proven writer:
                // state 1 sets the lock and every other state clears it.
                var nativeAccount = hasNativeRecord
                    && !string.IsNullOrEmpty(nativeRecord.PTID)
                    ? nativeRecord.PTID
                    : sAccount;
                if (_loginService.IsNativeAccountCrossServerLocked(nativeAccount))
                {
                    Log($"[SelectChr] native loader result 6 (account lock): {sChrName}");
                    handled = true;
                    return false;
                }

                // fn_5A777C checks record+0x37 only after both result-6
                // predicates.  Result 4 is a synchronous 4017 with Param=4.
                if (nativeDeletedRecordOwned)
                {
                    Log($"[SelectChr] native character is deleted: {sChrName}");
                    SendEncodedPacket(userInfo, Grobal2.SM_STARTFAIL,
                        0, 4, 0, 0, null);
                    handled = true;
                    return false;
                }
            }

            if (nativeWire)
                userInfo.NativeCurrentCharName = sChrName;

            // 更新选择状态 only after the native ownership gate succeeds.
            foreach (var humRecord in accountRecords)
            {
                humRecord.boSelected = (byte)(
                    string.Equals(humRecord.sChrName, sChrName,
                        StringComparison.Ordinal) ? 1 : 0);
                var updatedRecord = humRecord;
                _playRecordService.UpdateBy(updatedRecord.Id, ref updatedRecord);
            }

            int recordIndex = nativeWire && ownedRecord != null
                ? ownedRecord.Id
                : _playRecordService.Index(sChrName);
            int dataIndex = _playDataService.Index(sChrName);
            if (recordIndex > 0 && dataIndex == recordIndex)
            {
                var ptid2 = sAccount;
                var useNativeBackends = _loginService.Mode
                                        == LoginGateTransportMode.Native77Client;
                if (useNativeBackends)
                {
                    // The original DBServer pushes the selected record directly over port 6000
                    // before acknowledging 4017 on port 5100.
                    var auth = userInfo.NativeAuthResponse;
                    var nativeGateIndex = gateInfo?.NativeRouteID ?? 0;
                    if (auth == null || userInfo.NativeConnectionId == 0
                                     || nativeGateIndex < 1
                                     || nativeGateIndex > 32
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
                        // P0-1 BLOCKED: AuthByte56 bit4 (IsNetCafeUser) 需 DBServer 证实的
                        // [0x5D9B04]+0x78 IP 名单 + 0x5C9A24 IndexOf 门；未移植前保持 0。
                        // 接线点：命中名单时 AuthByte56 |= 0x10 — 见 docs/dbsvr_p0p1_gap_closure_20260814.md
                        var context = new NativeHumanSessionContext
                        {
                            UserIp = userInfo.sUserIPaddr,
                            AuthText54 = auth.Text54,
                            AuthFlags75 = auth.Flags75,
                            AuthByte77 = auth.Byte77,
                            AuthByte78 = auth.Byte78,
                            GateIndex = (byte)nativeGateIndex,
                            GroupIndex = (byte)DBShare.nGroupIdx,
                            ZoneIndex = (ushort)DBShare.nZoneIdx,
                            ConnectionId = userInfo.NativeConnectionId,
                            LoginElapsedMilliseconds = elapsed,
                            AuthText81 = auth.Text81,
                            AuthText102 = userInfo.NativeText102,
                            SessionMode = 1,
                            CachedValue38 = unchecked((int)loginDateTimeBits),
                            CachedValue3C = unchecked(
                                (int)(loginDateTimeBits >> 32)),
                            LoginExtension = directLoginExtension
                                ?? userInfo.NativeSwitchHandoff.Consume()
                        };
                        var nativeFanoutOK = _gameSocService.TrySendNativeHuman(
                            ptid2, sChrName, context, out nativeFailureCode);
                        // TrySendNativeHuman retains the native sub_59DC1C
                        // fan-out bool (non-empty target list).  A loader
                        // failure code still vetoes the 4017 success leg.
                        boDataOK = nativeFanoutOK && nativeFailureCode == 0;
                    }
                }
                else
                {
                    boDataOK = _loginService.TrySendSocketMsg(Grobal2.SS_OPENSESSION,
                        $"{ptid2}/{userInfo.nSessionID}/{MobileAdmissionPaymentState}/" +
                        $"{MobileAdmissionPayMode}/{userInfo.sUserIPaddr}");
                }
                if (boDataOK && useNativeBackends)
                {
                    userInfo.NativeSwitchHandoff.SetCurrentCharacter(sChrName);
                    userInfo.NativeSessionState = 6;
                }
                if (boDataOK)
                    _loginService.SetGlobaSessionPlay(userInfo.sAccount, userInfo.nSessionID);
            }

            if (boDataOK)
            {
                if (nativeWire)
                    NativeSelectEntryProtocol.CompleteSelection(
                        userInfo, sChrName);
                SendMobileSelectChr(userInfo, sChrName);
                handled = true;
                return true;
            }

            // Native fn_5A777C reports code 5 for a user-data load/validation
            // failure.  Preserve the legacy zero result for unclassified
            // forwarding/session failures until their native predicates are
            // proven.
            SendEncodedPacket(userInfo, Grobal2.SM_STARTFAIL,
                0, nativeWire ? nativeFailureCode : (ushort)0,
                0, 0, null);
            handled = true;
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

        private void OutOfConnect(TUserInfo userInfo, TGateInfo gateInfo)
        {
            OutOfConnect(userInfo, gateInfo, null);
        }

        private void OutOfConnect(TUserInfo userInfo, TGateInfo gateInfo,
            string accountOverride)
        {
            // The 2.08 UserSoc image emits 0xFB2 (4018) on its native
            // 0x77 line.  Keep the legacy 528 ident only for the private
            // percent/dollar transport, whose contract is outside that
            // native capture.
            var nativeWire = userInfo.WireMode == TGateWireMode.Native77;
            var ident = (ushort)(nativeWire
                ? Grobal2.SM_OUTOFCONNECTION_4018
                : Grobal2.SM_OUTOFCONNECTION);
            if (!nativeWire)
            {
                SendEncodedPacket(userInfo, ident,
                    0, 0, 0, 0, null);
                return;
            }

            SendNativeTerminalPacket(userInfo, gateInfo, ident,
                accountOverride);
        }

        private void SendNativeTerminalPacket(TUserInfo userInfo,
            TGateInfo gateInfo, ushort ident)
        {
            TrySendNativeTerminalRoute(userInfo, ident);
        }

        private void SendNativeTerminalPacket(TUserInfo userInfo,
            TGateInfo gateInfo, ushort ident, string accountOverride)
        {
            TrySendNativeTerminalRoute(userInfo, ident, accountOverride);
        }

        private bool TrySendNativeTerminalRoute(TUserInfo userInfo,
            ushort ident)
        {
            return TrySendNativeTerminalRoute(userInfo, ident, null);
        }

        private bool TrySendNativeTerminalRoute(TUserInfo userInfo,
            ushort ident, string accountOverride)
        {
            if (userInfo == null) return false;
            return TryRemoveNativeRoute(userInfo.NativeGateOwner,
                userInfo.NativeConnectionId, userInfo, removed =>
                {
                    lock (removed.NativeRouteLifecycleSync)
                    {
                        if (!removed.NativeRouteActive
                            || removed.NativeSessionState == 7)
                            return false;

                        try
                        {
                            SendEncodedPacket(removed, ident,
                                0, 0, 0, 0, null);
                        }
                        catch (Exception ex)
                        {
                            Log("[UserSoc] terminal packet send failed: "
                                + ex.Message);
                        }
                        removed.NativeSessionState = 7;
                        return true;
                    }
                }, true, false, out _, accountOverride);
        }

        private void CloseManagedLoginSession(TUserInfo userInfo)
        {
            if (userInfo == null) return;
            if (_loginService.GetGlobaSessionStatus(
                    userInfo.sAccount, userInfo.nSessionID)) return;
            _loginService.SendSocketMsg(Grobal2.SS_SOFTOUTSESSION,
                userInfo.sAccount + "/" + userInfo.nSessionID);
            _loginService.CloseSession(userInfo.sAccount, userInfo.nSessionID);
        }

        private bool TryRemoveNativeRoute(TGateInfo gateInfo, ushort routeId,
            TUserInfo expectedUser, Func<TUserInfo, bool> beforeDetach,
            bool sendRouteClear,
            bool preservePendingTakeover,
            out TUserInfo removedUser)
        {
            return TryRemoveNativeRoute(gateInfo, routeId, expectedUser,
                beforeDetach, sendRouteClear, preservePendingTakeover,
                out removedUser, null);
        }

        private bool TryRemoveNativeRoute(TGateInfo gateInfo, ushort routeId,
            TUserInfo expectedUser, Func<TUserInfo, bool> beforeDetach,
            bool sendRouteClear,
            bool preservePendingTakeover,
            out TUserInfo removedUser, string accountOverride)
        {
            lock (_nativeAccountTakeoverSync)
            {
                removedUser = null;
                if (gateInfo?.UserList == null) return false;

                lock (_gateLock)
                {
                    if (!_gateList.Contains(gateInfo)) return false;
                    lock (gateInfo.UserList)
                    {
                        if (!gateInfo.NativeRoutes.TryGetNewest(routeId,
                                out var currentUser)
                            || expectedUser != null
                            && !ReferenceEquals(currentUser, expectedUser)
                            || beforeDetach != null
                            && !beforeDetach(currentUser)
                            || !gateInfo.NativeRoutes.TryRemoveNewest(routeId,
                                currentUser, out removedUser))
                            return false;

                        lock (removedUser.NativeRouteLifecycleSync)
                        {
                            if (sendRouteClear
                                && !string.IsNullOrEmpty(removedUser.sAccount)
                                && removedUser.NativeSessionState != 7)
                            {
                                try
                                {
                                    SendEncodedPacket(removedUser,
                                        Grobal2.SM_OUTOFCONNECTION_4018,
                                        0, 0, 0, 0, null);
                                }
                                catch (Exception ex)
                                {
                                    Log("[UserSoc] native route callback terminal send failed: "
                                        + ex.Message);
                                }
                                removedUser.NativeSessionState = 7;
                            }

                            removedUser.NativeRouteActive = false;
                            removedUser.NativeSwitchHandoff.Reset();
                        }

                        for (var i = 0; i < gateInfo.UserList.Count; i++)
                            if (ReferenceEquals(gateInfo.UserList[i], removedUser))
                            {
                                gateInfo.UserList.RemoveAt(i);
                                break;
                            }
                    }
                }

                CleanupNativeAdmissionAndOwnership(removedUser,
                    preservePendingTakeover, accountOverride);
                if (sendRouteClear
                    && NativeLoginAlreadyOnlineProtocol.TryCreateRouteClearFrame(
                        routeId, out var routeClear))
                {
                    try { QueueNativeGateFrame(gateInfo, routeClear); }
                    catch (Exception ex)
                    {
                        Log("[UserSoc] command-12 route clear failed: "
                            + ex.Message);
                    }
                }
                return true;
            }
        }

        // ===================== 手游编码辅助方法 =====================

        private void ReleaseNativeIpAdmission(TUserInfo userInfo)
        {
            ReleaseNativeIpAdmission(userInfo, null);
        }

        private void ReleaseNativeIpAdmission(TUserInfo userInfo,
            string accountOverride)
        {
            if (userInfo == null
                || Interlocked.Exchange(ref userInfo.NativeIpCounted, 0) == 0)
                return;
            _nativeAdmission.ReleaseNativeConnection(
                accountOverride ?? userInfo.sAccount, userInfo.sUserIPaddr);
        }

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
                QueueNativeGateFrame(userInfo.NativeGateOwner, wire);
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

        // ResolveMobileTicket / GetPtid REMOVED per account_schema_ownership_20260811.md:
        // account.ticket and account.normal belong to the login platform (logincenter Lua
        // + LoginGate C#), NOT to DBServer. The native DBServer binary has 0 hits for
        // these table names. The methods here were invented, contradicting the original's
        // architecture. Callers that still need ticket resolution should route through
        // LoginGate's IMobileTicketStore, not have DBServer directly query account.*.

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

        private sealed class NativePendingAward
        {
            public int Idx { get; init; }
            public byte Job { get; init; }
            public byte Sex { get; init; }
            public byte Level { get; init; }
        }

        private static bool TryReadPendingAward(string ptid,
            out NativePendingAward award)
        {
            award = null;
            try
            {
                using var conn = new MySqlConnection(DBShare.DBConnection);
                conn.Open();
                using var cmd = new MySqlCommand(
                    "Select Idx, Job, Level, Sex from mir3.awardplayers where PTID=@p and Status=0",
                    conn);
                cmd.Parameters.AddWithValue("@p", ptid);
                using var reader = cmd.ExecuteReader();
                if (!reader.Read()) return false;
                award = new NativePendingAward
                {
                    Idx = Convert.ToInt32(reader["Idx"]),
                    Job = unchecked((byte)Convert.ToInt32(reader["Job"])),
                    Sex = unchecked((byte)Convert.ToInt32(reader["Sex"])),
                    Level = unchecked((byte)Convert.ToInt32(reader["Level"]))
                };
                return true;
            }
            catch (Exception ex)
            {
                DBShare.MainOutMessage(
                    $"[AwardPlayer] 查询未领取记录错误: {ex.Message}");
                return false;
            }
        }

        private static void MarkPendingAwardClaimed(int idx, string chrName)
        {
            try
            {
                using var conn = new MySqlConnection(DBShare.DBConnection);
                conn.Open();
                using var cmd = new MySqlCommand(
                    "Update mir3.awardplayers Set Status=1, HumName=@h where Idx=@i;",
                    conn);
                cmd.Parameters.Add(LegacyGbkText.Parameter("@h", chrName));
                cmd.Parameters.AddWithValue("@i", idx);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                DBShare.MainOutMessage(
                    $"[AwardPlayer] 领取状态更新错误: {ex.Message}");
            }
        }

        [Conditional("DBSVR_PROTOCOL_TRACE")]
        private static void Log(string msg)
        {
            Debug.WriteLine(msg);
        }

        /// <summary>
        /// 对应 Delphi 原版 awardplayers 表:
        ///   SELECT * WHERE PTID=%s AND Status=0
        ///   UPDATE SET Status=1, HumName=%s WHERE Idx=%d
        /// </summary>
        private static void ProcessAwardPlayer(string ptid, string chrName)
        {
            if (TryReadPendingAward(ptid, out var award))
                MarkPendingAwardClaimed(award.Idx, chrName);
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
