using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using DBSvr.Core;
using MySql.Data.MySqlClient;
using SystemModule;
using SystemModule.Packet;
using SystemModule.Sockets;

namespace DBSvr
{
    /// <summary>
    /// GameSvr 连接服务 (端口 6000)。
    /// 对应 Delphi 原版 TMainExecute + THumanDBManager + TBackSaveDB。
    /// </summary>
    public class GameSocService
    {
        private const int MaxGameServerConnections = 64;
        private readonly IList<TServerInfo> _serverList;
        private readonly IPlayDataService _playDataService;
        private readonly IPlayRecordService _playRecordService;
        private readonly IHeroDataService _heroDataService;
        private readonly IHeroRecordService _heroRecordService;
        private readonly IPetService _petService;
        private readonly IStorageService _storageService;
        private readonly ITransferAreaService _transferAreaService;
        private readonly SensitiveWordFilter _sensitiveWordFilter;
        private readonly WhitelistService _whitelistService;
        private readonly ConfigManager _configManager;
        private readonly NativeType2InitializationCache _nativeType2Cache;
        private readonly NativeType2RankingReloadCoordinator _nativeType2Rankings;
        private readonly NativeForceLevelService _nativeForceLevelService;
        private readonly NativeHeroLogicalCache _heroLogicalCache;
        private readonly NativeHumanLogicalCache _humanLogicalCache = new();
        private readonly NativeDominatorPetCache _nativeDominatorPets;
        private readonly NativeDominatorPetBackupQueue _nativePetBackup;
        private readonly NativeAccountStorageCache _nativeAccountStorage;
        private readonly INativeHallOfFameService _nativeHallOfFame;
        private readonly INativeAwardPlayerService _nativeAwardPlayers;
        private readonly NativeUserAdmissionControl _nativeAdmission;
        private readonly NativeRelationLogService _nativeRelationLog;
        private readonly INativeType2StdItemsImportService _stdItemsImport;
        private readonly IZongpaiService _zongpaiService;

        /// <summary>
        /// Runtime session-extension blobs for type2 0x0177, keyed by the record's
        /// int identity. Mirrors the original THumanInfo+0x7C field, which the
        /// 战神 DBServer overwrites on each 0x0177 (0x5AD298 frees the old blob
        /// before copying the new one).
        /// </summary>
        private readonly Dictionary<int, byte[]> _nativeSessionExtBlobs = new();
        private readonly object _nativeSessionExtLock = new();
        private readonly ISocketServer _serverSocket;
        private readonly LoginSvrService _loginSvrService;
        private readonly object _serverListLock = new();
        private readonly object _heroCreateLock = new();
        private readonly object _nativeSaveMutationLock = new();
        private readonly object _nativeAccountRenameLock = new();
        private readonly object _nativeSaveQueueLock = new();
        private readonly Dictionary<int, NativeSaveWorkItem> _nativeSavePending = new();
        private readonly Queue<int> _nativeSaveOrder = new();
        private readonly Dictionary<int, long> _nativeSaveGenerations = new();
        private readonly HashSet<int> _nativeSaveTombstones = new();
        private Thread _nativeSaveThread;
        private bool _nativeSaveStopping;
        private readonly object _heroSaveQueueLock = new();
        private readonly Dictionary<string, NativeHeroSaveWorkItem>
            _heroSavePending = new(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<string> _heroSaveOrder = new();
        private readonly Dictionary<int, long> _heroSaveGenerations = new();
        private readonly HashSet<int> _heroSaveTombstones = new();
        private Thread _heroSaveThread;
        private bool _heroSaveStopping;
        private readonly NativeHeroSaveStateTracker _heroSaveState = new();
        private readonly NativeHeroAttachmentStateTracker _heroAttachmentState = new();
        private Func<string, string, byte[], bool> _nativeSwitchHandoffStore =
            static (_, _, _) => false;

        public GameSocService(LoginSvrService loginSvrService,
            IPlayDataService playDataService, IPlayRecordService playRecordService,
            IHeroDataService heroDataService, IHeroRecordService heroRecordService,
            IPetService petService, IStorageService storageService,
            ITransferAreaService transferAreaService,
            SensitiveWordFilter sensitiveWordFilter,
            WhitelistService whitelistService, ConfigManager configManager,
            NativeType2InitializationCache nativeType2Cache,
            NativeType2RankingReloadCoordinator nativeType2Rankings,
            NativeForceLevelService nativeForceLevelService,
            NativeHeroLogicalCache heroLogicalCache,
            NativeDominatorPetCache nativeDominatorPets,
            NativeDominatorPetBackupQueue nativePetBackup,
            NativeAccountStorageCache nativeAccountStorage,
            INativeHallOfFameService nativeHallOfFame,
            INativeAwardPlayerService nativeAwardPlayers,
            NativeUserAdmissionControl nativeAdmission,
            INativeType2StdItemsImportService stdItemsImport,
            IZongpaiService zongpaiService)
        {
            _loginSvrService = loginSvrService;
            _playDataService = playDataService;
            _playRecordService = playRecordService;
            _heroDataService = heroDataService;
            _heroRecordService = heroRecordService;
            _petService = petService;
            _storageService = storageService;
            _transferAreaService = transferAreaService;
            _sensitiveWordFilter = sensitiveWordFilter;
            _whitelistService = whitelistService;
            _configManager = configManager;
            _nativeType2Cache = nativeType2Cache;
            _nativeType2Rankings = nativeType2Rankings;
            _nativeForceLevelService = nativeForceLevelService;
            _heroLogicalCache = heroLogicalCache
                ?? throw new ArgumentNullException(nameof(heroLogicalCache));
            _nativeDominatorPets = nativeDominatorPets
                ?? throw new ArgumentNullException(nameof(nativeDominatorPets));
            _nativePetBackup = nativePetBackup
                ?? throw new ArgumentNullException(nameof(nativePetBackup));
            _nativeAccountStorage = nativeAccountStorage
                ?? throw new ArgumentNullException(nameof(nativeAccountStorage));
            _nativeHallOfFame = nativeHallOfFame
                ?? throw new ArgumentNullException(nameof(nativeHallOfFame));
            _nativeAwardPlayers = nativeAwardPlayers
                ?? throw new ArgumentNullException(nameof(nativeAwardPlayers));
            _nativeAdmission = nativeAdmission
                ?? throw new ArgumentNullException(nameof(nativeAdmission));
            _nativeRelationLog = new NativeRelationLogService(
                _playRecordService, _configManager);
            _stdItemsImport = stdItemsImport
                ?? throw new ArgumentNullException(nameof(stdItemsImport));
            _zongpaiService = zongpaiService
                ?? throw new ArgumentNullException(nameof(zongpaiService));
            _nativeType2Rankings.RankingsPublished +=
                BroadcastNativeType2Rankings;
            _serverList = new List<TServerInfo>();
            _serverSocket = new ISocketServer(MaxGameServerConnections, 1024);
            _serverSocket.OnClientConnect += ServerSocketClientConnect;
            _serverSocket.OnClientDisconnect += ServerSocketClientDisconnect;
            _serverSocket.OnClientRead += ServerSocketClientRead;
            _serverSocket.OnClientError += (s, e) => { Debug.WriteLine("GameSoc OnError: " + e?.ToString()); };
            _serverSocket.Init();
            EnsureNativeSaveWorker();
            EnsureHeroSaveWorker();
        }

        public void AttachNativeSwitchHandoffStore(
            Func<string, string, byte[], bool> store)
        {
            Volatile.Write(ref _nativeSwitchHandoffStore,
                store ?? ((_, _, _) => false));
        }

        public void Start()
        {
            EnsureNativeSaveWorker();
            EnsureHeroSaveWorker();
            _playDataService.LoadQuickList();
            _heroRecordService.LoadQuickList();
            _heroDataService.LoadQuickList();
            _nativeDominatorPets.LoadIndex(_petService);
            _nativeAccountStorage.LoadStorageIndex(_storageService);
            _nativeAccountStorage.StartSaveWorker(_storageService);
            _nativePetBackup.Start();
            _nativeRelationLog.Start();
            _serverSocket.Start(DBShare.sServerAddr, DBShare.nServerPort);
            DBShare.MainOutMessage($"数据库角色服务[{DBShare.sServerAddr}:{DBShare.nServerPort}]已启动.等待链接...");
        }

        public void Stop()
        {
            _serverSocket.Shutdown();
            Thread nativeSaveThread;
            lock (_nativeSaveQueueLock)
            {
                _nativeSaveStopping = true;
                Monitor.PulseAll(_nativeSaveQueueLock);
                nativeSaveThread = _nativeSaveThread;
            }
            if (nativeSaveThread?.IsAlive == true
                && Thread.CurrentThread != nativeSaveThread)
                nativeSaveThread.Join();
            lock (_nativeSaveQueueLock)
                if (ReferenceEquals(_nativeSaveThread, nativeSaveThread))
                    _nativeSaveThread = null;
            Thread heroSaveThread;
            lock (_heroSaveQueueLock)
            {
                _heroSaveStopping = true;
                Monitor.PulseAll(_heroSaveQueueLock);
                heroSaveThread = _heroSaveThread;
            }
            if (heroSaveThread?.IsAlive == true
                && Thread.CurrentThread != heroSaveThread)
                heroSaveThread.Join();
            lock (_heroSaveQueueLock)
                if (ReferenceEquals(_heroSaveThread, heroSaveThread))
                    _heroSaveThread = null;
            _nativeAccountStorage.StopSaveWorker();
            _nativePetBackup.Stop();
            _nativeRelationLog.Stop();
            lock (_serverListLock) _serverList.Clear();
        }

        private void EnsureNativeSaveWorker()
        {
            lock (_nativeSaveQueueLock)
            {
                if (_nativeSaveThread?.IsAlive == true) return;
                _nativeSaveStopping = false;
                _nativeSaveThread = new Thread(ProcessNativeSaveQueue)
                {
                    IsBackground = true,
                    Name = "DBSvr native save worker"
                };
                _nativeSaveThread.Start();
            }
        }

        private void EnsureHeroSaveWorker()
        {
            lock (_heroSaveQueueLock)
            {
                if (_heroSaveThread?.IsAlive == true) return;
                _heroSaveStopping = false;
                _heroSaveThread = new Thread(ProcessHeroSaveQueue)
                {
                    IsBackground = true,
                    Name = "DBSvr native hero save worker"
                };
                _heroSaveThread.Start();
            }
        }

        // ===================== Socket 事件 =====================

        private void ServerSocketClientConnect(object sender, AsyncUserToken e)
        {
            string sIPaddr = e.RemoteIPaddr;
            if (!DBShare.CheckServerIP(sIPaddr))
            {
                DBShare.MainOutMessage("非法服务器连接: " + sIPaddr);
                e.Socket.Close();
                return;
            }
            lock (_serverListLock) _serverList.Add(new TServerInfo
            {
                nSckHandle = (int)e.Socket.Handle,
                Socket = e.Socket
            });
        }

        private void ServerSocketClientDisconnect(object sender, AsyncUserToken e)
        {
            lock (_serverListLock)
            {
                for (var i = 0; i < _serverList.Count; i++)
                {
                    if (ReferenceEquals(_serverList[i].Socket, e.Socket))
                    {
                        _serverList.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        private void ServerSocketClientRead(object sender, AsyncUserToken e)
        {
            TServerInfo serverInfo = null;
            lock (_serverListLock)
            {
                for (var i = 0; i < _serverList.Count; i++)
                {
                    if (_serverList[i].nSckHandle != (int)e.Socket.Handle
                        || !ReferenceEquals(_serverList[i].Socket, e.Socket))
                        continue;
                    serverInfo = _serverList[i];
                    break;
                }
            }
            if (serverInfo == null) return;

            List<byte[]> privateFrames = null;
            List<LegacyDbServerFrame> nativeFrames = null;
            string frameError = string.Empty;
            lock (serverInfo.SyncRoot)
            {
                if (!serverInfo.WireModeDetector.TryAppend(
                        e.ReceiveBuffer, e.Offset, e.BytesReceived,
                        out var replay, out frameError))
                    goto InvalidFrame;
                if (replay.Length == 0) return;

                if (serverInfo.WireModeDetector.Mode == DbServerWireMode.PrivateRequestServer)
                {
                    if (!serverInfo.FrameParser.TryAppend(replay, 0, replay.Length,
                            out privateFrames, out frameError))
                        goto InvalidFrame;
                }
                else if (serverInfo.WireModeDetector.Mode == DbServerWireMode.NativeType12)
                {
                    nativeFrames = new List<LegacyDbServerFrame>();
                    try
                    {
                        serverInfo.NativeFrameParser.Append(replay, nativeFrames.Add);
                    }
                    catch (InvalidOperationException ex)
                    {
                        frameError = ex.Message;
                        goto InvalidFrame;
                    }
                }
            }

            if (privateFrames != null)
            {
                foreach (var frame in privateFrames)
                    ProcessServerPacket(serverInfo, frame);
            }
            if (nativeFrames != null)
            {
                foreach (var frame in nativeFrames)
                {
                    if (ProcessNativeServerFrame(serverInfo, frame, out frameError)) continue;
                    goto InvalidFrame;
                }
            }
            return;

        InvalidFrame:
            {
                DBShare.MainOutMessage($"[GameSoc] 非法数据帧 {e.RemoteIPaddr}: {frameError}");
                e.Socket.Close();
            }
        }

        private bool ProcessNativeServerFrame(TServerInfo serverInfo,
            LegacyDbServerFrame frame, out string error)
        {
            error = string.Empty;
            if (frame.Type == 1 && frame.Payload.Length < 0x48
                || frame.Type == 2 && frame.Payload.Length < 0x0C
                || frame.Type == 3 && frame.Payload.Length < 0x40)
            {
                return true;
            }

            if (frame.Type != 1 && frame.Type != 2 && frame.Type != 3)
            {
                DBShare.MainOutMessage(
                    $"[GameSoc] 原生6000暂不支持包类型 {frame.Type}, payload={frame.Payload.Length}");
                return true;
            }

            var command = BitConverter.ToUInt16(frame.Payload, 0);
            if (frame.Type == 1)
            {
                var serverType = unchecked((byte)Volatile.Read(
                    ref serverInfo.NativeServerType));
                if (!NativeDbServerProtocol.UsesNormalType1Dispatcher(serverType))
                {
                    if (serverType == 9
                        && command == NativeDbToolProtocol.DeleteCommand)
                    {
                        ProcessNativeDbToolDelete(serverInfo, frame);
                        return true;
                    }
                    if (serverType == 9
                        && command == NativeDbToolProtocol.HumanWriteCommand)
                    {
                        ProcessNativeDbToolHumanWrite(serverInfo, frame);
                        return true;
                    }
                    if (serverType == 9
                        && command == NativeDbToolProtocol.HumanReadCommand)
                    {
                        ProcessNativeDbToolHumanRead(serverInfo, frame);
                        return true;
                    }
                    if (serverType == 9
                        && command == NativeDbToolProtocol.HeroWriteCommand)
                    {
                        ProcessNativeDbToolHeroWrite(serverInfo, frame);
                        return true;
                    }
                    if (serverType == 9
                        && command == NativeDbToolProtocol.HeroReadCommand)
                    {
                        ProcessNativeDbToolHeroRead(serverInfo, frame);
                        return true;
                    }
                    DBShare.MainOutMessage(
                        NativeDbServerProtocol.IsDbToolType1Command(command)
                            ? $"[GameSoc] 原生DB工具type1指令尚未实现 0x{command:X4}"
                            : $"[GameSoc] 原生DB工具未知type1指令 0x{command:X4}");
                    return true;
                }
                if (NativeDbServerProtocol.IsSilentNormalType1Command(command,
                        serverType))
                    return true;
                if (command == NativeDbServerProtocol.SaveHumanCommand)
                {
                    ProcessNativeSaveHuman(frame);
                    return true;
                }
                if (command == NativeForceLevelProtocol.RequestCommand)
                {
                    ProcessNativeForceLevel(serverInfo, frame);
                    return true;
                }
                if (command == NativeMasterRelationProtocol.RequestCommand)
                {
                    ProcessNativeMasterRelation(serverInfo, frame);
                    return true;
                }
                if (command == NativeItemExtractionProtocol.RequestCommand)
                {
                    ProcessNativeItemExtraction(serverInfo, frame);
                    return true;
                }
                if (command == NativeAuxiliaryType1Protocol.RegisterCharacterNameCommand)
                {
                    ProcessNativeCharacterNameRegistration(frame);
                    return true;
                }
                if (command == NativeAuxiliaryType1Protocol.DynamicImageRequestCommand)
                {
                    ProcessNativeDynamicImageRequest(serverInfo, frame);
                    return true;
                }
                if (command is NativeDominatorPetProtocol.CreateCommand
                    or NativeDominatorPetProtocol.LoadCommand
                    or NativeDominatorPetProtocol.SaveCommand)
                {
                    ProcessNativeDominatorPet(serverInfo, frame, command);
                    return true;
                }
                if (command == NativeAccountStorageProtocol.LoadCommand)
                {
                    ProcessNativeAccountStorageLoad(serverInfo, frame);
                    return true;
                }
                if (command == NativeAccountStorageProtocol.SaveCommand)
                {
                    ProcessNativeAccountStorageSave(serverInfo, frame);
                    return true;
                }
                if (command is NativeGlobalRelayProtocol.RegistrationCommand
                    or NativeGlobalRelayProtocol.QueryCommand)
                {
                    ProcessNativeGlobalRelay(serverInfo, frame, command);
                    return true;
                }
                if (command is NativeGateReportProtocol.Type1RequestCommand
                    or NativeGateReportProtocol.Type2RequestCommand)
                {
                    ProcessNativeGateReport(frame);
                    return true;
                }
                if (command == NativeTransferScoreAccrualProtocol.RequestCommand)
                {
                    ProcessNativeTransferScoreAccrual(frame);
                    return true;
                }
                if (command == NativeSessionLookupProtocol.RequestCommand)
                {
                    ProcessNativeSessionLookup(serverInfo, frame);
                    return true;
                }
                if (command == NativeZongpaiProtocol.RequestCommand)
                {
                    ProcessNativeZongpai(serverInfo, frame);
                    return true;
                }
                if (command == NativeHallOfFameProtocol.RequestCommand)
                {
                    ProcessNativeHallOfFame(serverInfo, frame);
                    return true;
                }
                if (command == NativeTransferScoreProtocol.RequestCommand)
                {
                    ProcessNativeTransferScore(serverInfo, frame);
                    return true;
                }
                if (command == NativeAwardPlayerProtocol.RequestCommand)
                {
                    ProcessNativeAwardPlayer(serverInfo, frame);
                    return true;
                }
                if (command == NativeItemInjectionProtocol.MailRequestCommand)
                {
                    ProcessNativeMailItem(serverInfo, frame);
                    return true;
                }
                if (command == NativeItemInjectionProtocol.BagRequestCommand)
                {
                    ProcessNativeBagItem(serverInfo, frame);
                    return true;
                }
                if (command == NativeCharacterAdminProtocol.RestoreRequestCommand)
                {
                    ProcessNativeCharacterRestore(serverInfo, frame);
                    return true;
                }
                if (command == NativeCharacterAdminProtocol.LookupRequestCommand)
                {
                    ProcessNativeCharacterLookup(serverInfo, frame);
                    return true;
                }
                if (command == NativeSessionControlProtocol.DisconnectAccountCommand)
                {
                    ProcessNativeAccountDisconnect(frame);
                    return true;
                }
                if (command == NativeOnlineAccountProtocol.SetTextCommand)
                {
                    ProcessNativeOnlineAccountText(frame);
                    return true;
                }
                if (command == NativeOnlineAccountProtocol.SetLoginTimeCommand)
                {
                    ProcessNativeOnlineAccountLoginTime(frame);
                    return true;
                }
                if (command == NativeSessionControlProtocol.SetPlayStateCommand)
                {
                    ProcessNativeSessionPlayState(serverInfo, frame);
                    return true;
                }
                if (command == NativeCharacterBusyProtocol.Command)
                {
                    if (NativeCharacterBusyProtocol.TryDecode(frame,
                            out var characterName, out _))
                        _playRecordService.SetNativeCharacterBusy(characterName);
                    return true;
                }
                if (command == NativeHeroDbFrameCodec.DetachCommand)
                {
                    ProcessNativeHeroDetach(frame);
                    return true;
                }
                if (command >= NativeHeroDbFrameCodec.LoadCommand
                    && command <= NativeHeroDbFrameCodec.BuildThreeSlotCommand)
                    return ProcessNativeHeroFrame(serverInfo, frame, command, out error);

                DBShare.MainOutMessage(
                    $"[GameSoc] 原生6000暂不支持type1指令 0x{command:X4}, payload={frame.Payload.Length}");
                return true;
            }
            if (frame.Type == 3)
            {
                if (command == NativeType3Protocol.QueryCharactersCommand)
                {
                    ProcessNativeType3CharacterQuery(serverInfo, frame);
                    return true;
                }
                DBShare.MainOutMessage(
                    $"[GameSoc] 原生6000暂不支持包类型 {frame.Type}, command=0x{command:X4}");
                return true;
            }
            if (command != NativeDbServerProtocol.HeartbeatCommand)
            {
                if (command == NativeType2Protocol.ResetAllTransferLocksCommand)
                {
                    _playRecordService.ResetAllNativeTransferLocks();
                    return true;
                }
                if (command == NativeType2Protocol.ResetTransferLockCommand)
                {
                    if (NativeType2Protocol.TryDecode(frame, out var request,
                            out _)
                        && request.Suffix.Length != 0)
                        _playRecordService.ResetNativeTransferLock(
                            request.Suffix);
                    return true;
                }
                if (command == NativeType2Protocol.SetVipYbConsumeCommand)
                {
                    if (NativeType2Protocol.TryDecode(frame, out var request,
                            out _))
                        _configManager.SetVipYbConsume(request.Param2);
                    return true;
                }
                if (command == NativeType2AdmissionProtocol.DenyIpCommand)
                {
                    ProcessNativeType2DenyIp(frame);
                    return true;
                }
                if (command == NativeType2AdmissionProtocol.ControlCommand)
                {
                    ProcessNativeType2AdmissionControl(serverInfo, frame);
                    return true;
                }
                if (command == NativeRelationLogProtocol.Command)
                {
                    if (NativeType2Protocol.TryDecode(frame,
                            out var relationRequest, out _))
                        _nativeRelationLog.Process(relationRequest);
                    return true;
                }
                if (command == NativeType2StdItemsImportProtocol.RequestCommand)
                {
                    ProcessNativeType2StdItemsImport(serverInfo, frame);
                    return true;
                }
                if (command == NativeType2Protocol.RegisterCommand)
                {
                    ProcessNativeType2Registration(serverInfo, frame);
                    return true;
                }
                if (command == NativeType2Protocol.RankingReloadCommand)
                {
                    // Original 0x003E only advances its internal ranking cycle;
                    // it does not synchronously reload SQL or send a response.
                    return true;
                }
                if (command == NativeType2Protocol.RelayCommand)
                {
                    ProcessNativeType2Relay(serverInfo, frame);
                    return true;
                }
                if (command == NativeType2Protocol.LoginGateControlCommand)
                {
                    ProcessNativeType2LoginGateControl(frame);
                    return true;
                }
                if (command == NativeType2Protocol.WhitelistReloadCommand)
                {
                    ProcessNativeType2WhitelistReload(serverInfo, frame);
                    return true;
                }
                if (command == NativeType2SessionExtProtocol.RequestCommand)
                {
                    ProcessNativeType2SessionExt(serverInfo, frame);
                    return true;
                }
                if (NativeType2Protocol.IsSilentNoOpCommand(command))
                    return true;
                DBShare.MainOutMessage(
                    $"[GameSoc] 原生6000暂不支持指令 0x{command:X4}, payload={frame.Payload.Length}");
                return true;
            }
            if (!NativeDbServerProtocol.TryDecodeHeartbeat(frame,
                    out var heartbeat, out error))
                return false;

            serverInfo.NativeHeartbeatUninitializedWord = heartbeat.UninitializedWord;
            Volatile.Write(ref serverInfo.NativeHeartbeatState, heartbeat.State);
            Volatile.Write(ref serverInfo.NativeUserCount, heartbeat.UserCount);
            Volatile.Write(ref serverInfo.NativeHeartbeatTick, Environment.TickCount64);
            return true;
        }

        private void ProcessNativeType3CharacterQuery(TServerInfo sender,
            LegacyDbServerFrame frame)
        {
            if (!NativeType3Protocol.TryDecodeQuery(
                    frame, out var request, out var error))
            {
                DBShare.MainOutMessage("[GameSoc] 原生type3角色查询拒绝: " + error);
                return;
            }

            List<ChrIndexInfo> records;
            try
            {
                records = _playRecordService.QueryNativeType3ByPtid(request.PtidBytes)
                          ?? new List<ChrIndexInfo>();
            }
            catch (Exception ex)
            {
                DBShare.MainOutMessage(
                    "[GameSoc] 原生type3角色查询失败: " + ex.Message);
                records = new List<ChrIndexInfo>();
            }

            var characters = new List<NativeType3Character>(records.Count);
            foreach (var record in records)
            {
                if (record == null || record.DeleteState == 1) continue;
                characters.Add(new NativeType3Character
                {
                    UserId = record.UserId,
                    CharacterName = record.ChrName,
                    CharacterNameBytes = record.ChrNameBytes ?? Array.Empty<byte>(),
                    Level = record.Level,
                    Sex = record.Sex,
                    Job = record.Job
                });
            }
            if (!NativeType3Protocol.TryCreateQueryResponse(
                    request, characters, out var response, out error)
                || !LegacyDbServerFrameCodec.TryEncode(response,
                    out var wire, out error,
                    NativeDbServerProtocol.MaximumFrameLength))
            {
                DBShare.MainOutMessage(
                    "[GameSoc] 原生type3角色响应编码失败: " + error);
                return;
            }

            var senderGroup = unchecked((byte)Volatile.Read(
                ref sender.NativeServerType));
            List<TServerInfo> targets;
            lock (_serverListLock)
            {
                var candidates = new List<TServerInfo>();
                foreach (var peer in _serverList)
                {
                    if (peer?.Socket?.Connected != true
                        || peer.WireModeDetector.Mode
                        != DbServerWireMode.NativeType12) continue;
                    candidates.Add(peer);
                }
                targets = NativeType3Protocol.SelectBroadcastTargets(
                    senderGroup, candidates, peer => unchecked((byte)Volatile.Read(
                        ref peer.NativeServerType)));
            }

            foreach (var target in targets)
            {
                try { SendAll(target.Socket, wire); }
                catch (Exception ex) when (ex is SocketException
                                           || ex is ObjectDisposedException)
                {
                    DBShare.MainOutMessage(
                        $"[GameSoc] 原生type3角色响应发送失败: {ex.Message}");
                }
            }
        }

        private void BroadcastNativeType2Rankings()
        {
            var snapshot = _nativeType2Cache.Snapshot();
            if (snapshot.RankingsLoading) return;
            var frames = NativeType2InitializationProtocol.CreateSecondaryFrames(
                false, snapshot.Secondary);
            List<TServerInfo> targets;
            lock (_serverListLock)
            {
                targets = new List<TServerInfo>();
                foreach (var peer in _serverList)
                {
                    if (peer?.Socket?.Connected != true
                        || peer.WireModeDetector.Mode
                        != DbServerWireMode.NativeType12
                        || Volatile.Read(ref peer.NativeRegistrationInitialized) != 1
                        || Volatile.Read(ref peer.NativeServerType) == 9)
                        continue;
                    targets.Add(peer);
                }
            }

            foreach (var target in targets)
            {
                lock (target.Socket)
                {
                    if (target.Socket.Connected != true
                        || Volatile.Read(ref target.NativeRegistrationInitialized) != 1
                        || Volatile.Read(ref target.NativeServerType) == 9
                        || Volatile.Read(ref target.NativeRankingGenerationSent)
                        >= snapshot.RankingGeneration)
                        continue;
                    if (TrySendNativeType2FramesLocked(target.Socket, frames,
                            "排行广播"))
                        Volatile.Write(ref target.NativeRankingGenerationSent,
                            snapshot.RankingGeneration);
                }
            }
        }

        private void ProcessNativeType2Registration(TServerInfo sender,
            LegacyDbServerFrame frame)
        {
            if (!NativeType2Protocol.TryDecode(frame, out var request,
                    out var error)
                || !NativeType2Protocol.TryGetRegistrationServerType(
                    request, out var serverType))
                return;

            // Original writes the type on every valid registration before its
            // one-time initialization guard.
            Volatile.Write(ref sender.NativeServerType, serverType);
            if (Interlocked.CompareExchange(
                    ref sender.NativeRegistrationInitialized, 1, 0) != 0
                || serverType == 9)
                return;

            var snapshot = _nativeType2Cache.Snapshot();
            var frames = new List<LegacyDbServerFrame>
            {
                NativeType2InitializationProtocol.CreateGameGateSnapshot(
                    serverType,
                    NativeType2InitializationProtocol.ReadGameGates(
                        _configManager))
            };
            frames.AddRange(NativeType2InitializationProtocol
                .CreatePrimaryFrames(snapshot.Primary));
            frames.AddRange(NativeType2InitializationProtocol
                .CreateSecondaryFrames(snapshot.RankingsLoading,
                    snapshot.Secondary));

            lock (sender.Socket)
            {
                if (TrySendNativeType2FramesLocked(sender.Socket, frames,
                        "注册初始化")
                    && !snapshot.RankingsLoading)
                    Volatile.Write(ref sender.NativeRankingGenerationSent,
                        snapshot.RankingGeneration);
            }
        }

        private void ProcessNativeType2DenyIp(LegacyDbServerFrame frame)
        {
            if (NativeType2Protocol.TryDecode(frame, out var request, out _)
                && NativeType2AdmissionProtocol.TryDecodeDenyIp(
                    request, out var ip, out var value))
                _nativeAdmission.SetDenyIp(ip, value);
        }

        private void ProcessNativeType2AdmissionControl(TServerInfo sender,
            LegacyDbServerFrame frame)
        {
            if (!NativeType2Protocol.TryDecode(frame, out var request,
                    out _)
                || !NativeType2AdmissionProtocol.IsControlRequest(request))
                return;
            if (request.Param1 == 0)
                _nativeAdmission.RecountAndSetMaximum(request.Param2);
            else
                _nativeAdmission.SetQueueEnabled(request.Param2);

            var response = NativeType2AdmissionProtocol.CreateControlResponse(
                request);
            TrySendNativeResponse(sender, response, "0187准入控制");
            if (request.Param1 == 1 && request.Param2 == 0)
                _nativeAdmission.DrainQueue();
        }

        private void ProcessNativeType2StdItemsImport(TServerInfo sender,
            LegacyDbServerFrame frame)
        {
            if (!NativeType2Protocol.TryDecode(frame, out var request, out _)
                || !_stdItemsImport.TryImport(
                    request.Param2, out var notifications))
                return;
            foreach (var payload in notifications)
                TrySendNativeResponse(sender,
                    new LegacyDbServerFrame(2, 0, payload),
                    "0180物品增量");
        }

        private static bool TrySendNativeType2FramesLocked(Socket socket,
            IEnumerable<LegacyDbServerFrame> frames, string context)
        {
            foreach (var frame in frames)
            {
                if (!LegacyDbServerFrameCodec.TryEncode(frame, out var wire,
                        out var error, NativeDbServerProtocol.MaximumFrameLength))
                {
                    DBShare.MainOutMessage(
                        $"[GameSoc] 原生type2{context}编码失败: {error}");
                    return false;
                }
                try { SendAll(socket, wire); }
                catch (Exception ex) when (ex is SocketException
                                           || ex is ObjectDisposedException)
                {
                    DBShare.MainOutMessage(
                        $"[GameSoc] 原生type2{context}发送失败: {ex.Message}");
                    return false;
                }
            }
            return true;
        }

        private void ProcessNativeType2Relay(TServerInfo sender,
            LegacyDbServerFrame frame)
        {
            if (!NativeType2Protocol.TryDecode(frame, out var request, out var error)
                || !NativeType2Protocol.TryCreateRelayFrame(request,
                    unchecked((byte)Volatile.Read(ref sender.NativeServerType)),
                    out var response, out var targetType, out error)
                || !LegacyDbServerFrameCodec.TryEncode(response,
                    out var wire, out error, NativeDbServerProtocol.MaximumFrameLength))
            {
                DBShare.MainOutMessage("[GameSoc] 原生type2 relay拒绝: " + error);
                return;
            }

            var senderType = unchecked((byte)Volatile.Read(ref sender.NativeServerType));
            List<TServerInfo> targets;
            lock (_serverListLock)
            {
                targets = new List<TServerInfo>();
                foreach (var peer in _serverList)
                {
                    if (peer?.Socket?.Connected != true
                        || peer.WireModeDetector.Mode
                        != DbServerWireMode.NativeType12) continue;
                    var peerType = unchecked((byte)Volatile.Read(
                        ref peer.NativeServerType));
                    if (NativeType2Protocol.ShouldRelay(
                            senderType, peerType, targetType))
                        targets.Add(peer);
                }
            }

            foreach (var target in targets)
            {
                try { SendAll(target.Socket, wire); }
                catch (Exception ex) when (ex is SocketException
                                           || ex is ObjectDisposedException)
                {
                    DBShare.MainOutMessage(
                        $"[GameSoc] 原生type2 relay发送失败: {ex.Message}");
                }
            }
        }

        private void ProcessNativeType2LoginGateControl(
            LegacyDbServerFrame frame)
        {
            if (!NativeType2Protocol.TryDecode(frame, out var request,
                    out var error)
                || !NativeType2Protocol.TryGetLoginGateControlEnabled(
                    request, out var enabled))
            {
                DBShare.MainOutMessage(
                    "[GameSoc] 原生type2 LoginGate控制拒绝: " + error);
                return;
            }
            if (!_loginSvrService.QueueNativeType2Control(enabled))
                DBShare.MainOutMessage(
                    "[GameSoc] 原生type2 LoginGate控制等待Native77Client模式");
        }

        private void ProcessNativeType2WhitelistReload(TServerInfo sender,
            LegacyDbServerFrame frame)
        {
            if (!NativeType2Protocol.TryDecode(frame, out var request,
                    out var error))
            {
                DBShare.MainOutMessage(
                    "[GameSoc] 原生type2白名单重载拒绝: " + error);
                return;
            }
            if (!NativeType2Protocol.ShouldReloadWhiteLists(request)) return;

            _whitelistService.ReloadNativeWhiteLists();
            if (!NativeType2Protocol.TryCreateWhitelistReloadResponse(
                    request, out var response, out error)
                || !LegacyDbServerFrameCodec.TryEncode(response,
                    out var wire, out error,
                    NativeDbServerProtocol.MaximumFrameLength))
            {
                DBShare.MainOutMessage(
                    "[GameSoc] 原生type2白名单响应编码失败: " + error);
                return;
            }
            try { SendAll(sender.Socket, wire); }
            catch (Exception ex) when (ex is SocketException
                                       || ex is ObjectDisposedException)
            {
                DBShare.MainOutMessage(
                    "[GameSoc] 原生type2白名单响应发送失败: " + ex.Message);
            }
        }

        private void ProcessNativeForceLevel(TServerInfo sender,
            LegacyDbServerFrame frame)
        {
            if (!NativeForceLevelProtocol.TryDecodeRequest(
                    frame, out var request, out var error))
            {
                DBShare.MainOutMessage(
                    "[GameSoc] 原生ForceLv请求拒绝: " + error);
                return;
            }

            NativeForceLevelApplyResult applied;
            lock (_heroCreateLock)
            {
                applied = _nativeForceLevelService.ApplyDetailed(request);
                foreach (var mutation in applied.Mutations)
                {
                    if (mutation.Target == NativeForceLevelTarget.Player)
                        EnqueueNativeSave(new NativeSaveWorkItem(mutation));
                    else
                    {
                        EnqueueHeroSave(
                            NativeHeroSaveWorkItem.ForForceLevel(mutation));
                    }
                }
            }

            var response = NativeForceLevelProtocol.CreateResponse(
                request, applied.Result);
            if (!LegacyDbServerFrameCodec.TryEncode(response,
                    out var wire, out error,
                    NativeDbServerProtocol.MaximumFrameLength))
            {
                DBShare.MainOutMessage(
                    "[GameSoc] 原生ForceLv响应编码失败: " + error);
                return;
            }
            try { SendAll(sender.Socket, wire); }
            catch (Exception ex) when (ex is SocketException
                                       || ex is ObjectDisposedException)
            {
                DBShare.MainOutMessage(
                    "[GameSoc] 原生ForceLv响应发送失败: " + ex.Message);
            }
        }

        /// <summary>
        /// Native TYPE_B (type2) command 0x0177 (handler 0x599AEE): store the
        /// record's session-extension blob against its int identity and
        /// acknowledge with body command 0x13A echoing the two identity dwords.
        ///
        /// The original overwrites THumanInfo+0x7C (0x5AD298: free-old-then-copy)
        /// and, crucially, gates the store+ack on the human being LOADED in its
        /// runtime THumanDBManager registry (0x5ABC3C); on a miss it stays silent.
        /// C# DBSvr keeps no such int-keyed loaded-human registry, so the store
        /// itself is the presence here — faithful for the normal case where a
        /// GameServer only sends 0x0177 for a human DBServer already loaded. The
        /// documented residual: the original would stay silent for an unloaded
        /// human, and this path never runs at all in the deployed config (original
        /// Delphi DBServer). obj+0x7C is runtime-only state with no MySQL column
        /// and no consumer in the C# world.
        /// </summary>
        private void ProcessNativeType2SessionExt(TServerInfo sender,
            LegacyDbServerFrame frame)
        {
            if (!NativeType2SessionExtProtocol.TryDecodeRequest(
                    frame, out var request, out var error))
            {
                DBShare.MainOutMessage("[GameSoc] 原生type2 0177请求拒绝: " + error);
                return;
            }

            lock (_nativeSessionExtLock)
                _nativeSessionExtBlobs[request.Identity] = request.Blob;

            var ack = NativeType2SessionExtProtocol.CreateAck(request);
            if (!LegacyDbServerFrameCodec.TryEncode(ack, out var wire,
                    out error, NativeDbServerProtocol.MaximumFrameLength))
            {
                DBShare.MainOutMessage("[GameSoc] 原生type2 0177响应编码失败: " + error);
                return;
            }
            try { SendAll(sender.Socket, wire); }
            catch (Exception ex) when (ex is SocketException
                                       || ex is ObjectDisposedException)
            {
                DBShare.MainOutMessage(
                    "[GameSoc] 原生type2 0177响应发送失败: " + ex.Message);
            }
        }

        /// <summary>
        /// Native type1 0x0156 / 0x0173 (handlers 0x598E03 / 0x599231): the two
        /// <c>TGlobalSocket</c> ([0x5DA22C]) methods that report to the external
        /// global/cross-server service — 0x0156 either sends command 0x1F42 (8002)
        /// straight out or queues 0x274D (10061); 0x0173 queues 0x2750 (10064).
        ///
        /// That external service does not exist in this deployment (same class of
        /// dependency as YBDB 6108 / GlobalServer 6020), and the outbound record
        /// FIELDS are not reversed yet, so we decode and validate the request but
        /// emit nothing. Neither command ever answers the GameServer — both end at
        /// the shared exit 0x59953D — so staying silent is byte-faithful on the
        /// GameServer-facing side; only the outbound report is owed.
        /// </summary>
        private void ProcessNativeGlobalRelay(TServerInfo sender,
            LegacyDbServerFrame frame, ushort command)
        {
            string error;
            if (command == NativeGlobalRelayProtocol.RegistrationCommand)
            {
                var serverType = unchecked((byte)Volatile.Read(
                    ref sender.NativeServerType));
                if (!NativeGlobalRelayProtocol.TryDecodeRegistration(
                        frame, serverType, out _, out error))
                {
                    DBShare.MainOutMessage("[GameSoc] 原生0156请求拒绝: " + error);
                    return;
                }
                DBShare.MainOutMessage(
                    "[GameSoc] 原生0156已解析，原版此时经TGlobalSocket发8002"
                    + "或入队10061——外部全局服务未接入，按原版对GameServer侧静默");
                return;
            }

            if (!NativeGlobalRelayProtocol.TryDecodeQuery(frame, out _, out error))
            {
                DBShare.MainOutMessage("[GameSoc] 原生0173请求拒绝: " + error);
                return;
            }
            DBShare.MainOutMessage(
                "[GameSoc] 原生0173已解析，原版此时入队10064(0x41字节)"
                + "——外部全局服务未接入，按原版对GameServer侧静默");
        }

        /// <summary>
        /// Native type1 0x0192 / 0x0193 (handlers 0x5993A4 / 0x5993F6): validate the
        /// exact fixed tail length, look the header's +0x10 name up in the session
        /// table, and on a hit report to LoginGate (cmd 0x7DF / 0x7E0).
        ///
        /// Neither command ever answers the GameServer — both paths end at the
        /// common exit 0x59953D — so this handler is silent by design. The
        /// LoginGate report family (cmd 0x7DB-0x7E2) is not ported and the 0xF3 /
        /// 0x121 record FIELDS are not reversed yet, so we validate and log the
        /// owed report instead of fabricating a record.
        /// </summary>
        private void ProcessNativeGateReport(LegacyDbServerFrame frame)
        {
            if (!NativeGateReportProtocol.TryDecodeRequest(frame,
                    out var request, out var error))
            {
                // The original's length mismatch is a silent `jne` to 0x59953D;
                // logging here is diagnostic only and sends nothing.
                DBShare.MainOutMessage("[GameSoc] 原生0192/0193请求拒绝: " + error);
                return;
            }

            if (!_playRecordService.IsNativeCharacterNameOccupied(
                    request.LookupName))
                return; // miss → original does nothing at all

            DBShare.MainOutMessage(
                $"[GameSoc] 原生{NativeGateReportProtocol.GetRequestCommand(request.Kind):X4}"
                + "命中会话表，原版此时上报LoginGate(cmd 0x"
                + $"{NativeGateReportProtocol.GetLoginGateCommand(request.Kind):X3}"
                + $"，记录{NativeGateReportProtocol.GetRecordSize(request.Kind)}字节)"
                + "——该上报族未移植，对GameServer侧本就无应答");
        }

        /// <summary>
        /// Native type1 0x0174 (handler 0x599274): accrue one transfer-area score.
        /// The original queues a 0x27-byte record (0x595CF4) that its drain later
        /// turns into the additive upsert at 0x595FE8; we apply that same upsert
        /// directly through <see cref="ITransferAreaService.UpsertScore"/>, whose
        /// SQL already matches the original verbatim
        /// (<c>on duplicate key update ScoreN=ScoreN+delta</c>).
        ///
        /// The original also sends no reply for this command, so neither do we.
        /// </summary>
        private void ProcessNativeTransferScoreAccrual(LegacyDbServerFrame frame)
        {
            if (!NativeTransferScoreAccrualProtocol.TryDecodeRequest(
                    frame, out var request, out var error))
            {
                DBShare.MainOutMessage("[GameSoc] 原生0174转区积分请求拒绝: " + error);
                return;
            }

            // 0x596026-0x59602E: out-of-range indexes skip the delta assignment,
            // so all three columns accrue 0 — the row is still upserted.
            NativeTransferScoreAccrualProtocol.SpreadDelta(
                request.ScoreIndex, request.Delta,
                out var score1, out var score2, out var score3);

            var characterName = LegacyGbkText.Decode(request.CharacterName);
            if (string.IsNullOrEmpty(characterName)) return;
            try
            {
                _transferAreaService.UpsertScore(characterName,
                    score1, score2, score3);
            }
            catch (Exception ex)
            {
                // The original's queue drain only logs SQL failures and does not
                // roll back, so match that rather than propagating.
                DBShare.MainOutMessage(
                    "[GameSoc] 原生0174转区积分写入失败: " + ex.Message);
            }
        }

        /// <summary>
        /// Native type1 0x0151 (handler 0x598C1B): look the header's +0x10 name up
        /// in the online-session table, and when it is NOT present answer the
        /// requesting GameServer with the 0x54-byte frame built by 0x59A0FC.
        ///
        /// The original's hit branch instead reports to LoginGate (cmd 0x7DC via
        /// 0x5CF968) and sends no GameServer reply. That LoginGate report family
        /// (cmd 0x7DB-0x7E2) is not ported yet and its record fields are not
        /// reversed, so on a hit we stay silent — which matches the original's
        /// GameServer-facing behaviour exactly — and log that the report is owed
        /// rather than fabricate a frame.
        /// </summary>
        private void ProcessNativeSessionLookup(TServerInfo sender,
            LegacyDbServerFrame frame)
        {
            if (!NativeSessionLookupProtocol.TryDecodeRequest(frame,
                    out var request, out var error))
            {
                DBShare.MainOutMessage("[GameSoc] 原生0151请求拒绝: " + error);
                return;
            }

            // 0x5A1A40: key lowercased by 0x40AEF4 then hashed via 0x49BAA8 over
            // the manager's +0x90 table, whose inserts (0x5A15A9) use the same
            // key function — so this is a case-insensitive name lookup.
            if (_playRecordService.IsNativeCharacterNameOccupied(
                    request.LookupName))
            {
                DBShare.MainOutMessage(
                    "[GameSoc] 原生0151命中会话表，原版此时上报LoginGate(0x7DC)"
                    + "——该上报族未移植，按原版对GameServer侧静默");
                return;
            }

            // 0x59A0FC's [ebp+8] boolean and its body+0x25 ShortString come from
            // the miss path's `push 0 / push 0` at 0x598C6B — both zero.
            var response = NativeSessionLookupProtocol.CreateResponse(
                request, Array.Empty<byte>(), false);
            if (!LegacyDbServerFrameCodec.TryEncode(response, out var wire,
                    out error, NativeDbServerProtocol.MaximumFrameLength))
            {
                DBShare.MainOutMessage("[GameSoc] 原生0151响应编码失败: " + error);
                return;
            }
            try { SendAll(sender.Socket, wire); }
            catch (Exception ex) when (ex is SocketException
                                       || ex is ObjectDisposedException)
            {
                DBShare.MainOutMessage(
                    "[GameSoc] 原生0151响应发送失败: " + ex.Message);
            }
        }

        /// <summary>
        /// Native 宗派/师门 sub-protocol (type1 0x0170), mirroring the original
        /// 0x599206 → 0x59C51C → 0x594070 chain: dispatch on the sub-command,
        /// run the matching SQL worker, then reply either to the sender
        /// (0x49CB34) or by broadcast to every non-DB-tool GameServer (0x59E450).
        /// Sub-commands the original leaves unresolved here stay unhandled rather
        /// than getting an invented reply.
        /// </summary>
        private void ProcessNativeZongpai(TServerInfo sender,
            LegacyDbServerFrame frame)
        {
            if (!NativeZongpaiProtocol.TryDecodeRequest(frame,
                    out var request, out var error))
            {
                DBShare.MainOutMessage("[GameSoc] 原生0170宗派请求拒绝: " + error);
                return;
            }

            var masterName = LegacyGbkText.Decode(request.TailSlot00);
            var result = 0;
            switch (request.SubCommand)
            {
                case NativeZongpaiSubCommand.CreateMaster:
                    // 模板 0x592FD4 (len 101)：
                    //   insert into ZongpaiBase(MasterName, MasterLevel, StudentExp, UpdateTime)
                    //   values("%s", %d, %u, Now());
                    // TVarRec 自 [ebp-0x30]、每槽 8 字节，ecx=2（3 元素）：
                    //   0x592ED8  slot0 = [ebp-0x08]         type 0x0B -> %s MasterName
                    //   0x592EDF  movzx eax, word [ebp-0x0a] ★16 位零扩展
                    //   0x592EE3  slot1 = eax                type 0x00 -> %d MasterLevel
                    //   0x592EEA  slot2 = [ebp+0x08]         type 0x00 -> %u StudentExp
                    // 调用点 0x594213 定出各值的 tail 来源：
                    //   0x5941EE  mov eax,[eax+0x50] / 0x5941F1 push eax  ; tail+0x50 dword -> StudentExp
                    //   0x5941FB  add edx,0x35 / 0x5941FE call 0x404E5C   ; tail+0x35 Str   -> MasterName
                    //   0x59420C  mov cx, word ptr [eax+0x4c]             ; tail+0x4C WORD  -> MasterLevel
                    //
                    // ⚠️ 此前两处错：MasterLevel 传的是 TailValue50（应为 tail+0x4C 的低 16 位），
                    // 且 StudentExp 被在 SQL 里硬写 0（应为 tail+0x50）——一个错位、一个丢失。
                    result = _zongpaiService.CreateMaster(
                        LegacyGbkText.Decode(request.MasterNameSlot35),
                        request.CreateMasterLevel,
                        request.CreateMasterStudentExp) ? 0 : 1;
                    break;
                case NativeZongpaiSubCommand.AddMember:
                    // 模板 0x593140 (len 85)：
                    //   insert into ZongpaiMember(MasterName, MemberName, RoleName)
                    //   values("%s", "%s", "%s");
                    // TVarRec 数组自 [ebp-0x30] 起、每槽 8 字节，ecx=2 (高位索引，共 3 元素)：
                    //   0x5930D3  slot0 = [ebp-0x08]   -> %s#1 MasterName
                    //   0x5930DD  slot1 = [ebp+0x08]   -> %s#2 MemberName
                    //   0x5930E7  slot2 = [ebp-0x0C]   -> %s#3 RoleName
                    //   0x5930F9  mov eax,0x593140 / 0x5930FE call 0x40CF30 (Format)
                    //
                    // ⚠️ 此前 memberName 传 TailSlot10、roleName 传 TailSlot25，是**传反**。
                    // 三条同向证据：
                    //  (a) DDL 0x5BF0C4：`MemberName varchar(15)` / `RoleName varchar(20)`，
                    //      而槽容量是 tail+0x10 → 0x25-0x10=0x15 = 长度字节+20 ⇒ 容量 20 = RoleName；
                    //      tail+0x25 → 0x35-0x25=0x10 = 长度字节+15 ⇒ 容量 15 = MemberName。
                    //  (b) 同 DDL 有 `unique key MemberName_Index(MemberName)` —— MemberName
                    //      **单列唯一**。把可重复的 RoleName 写进该列，同一师门加入第二个成员
                    //      即撞唯一键 ⇒ 写坏数据，不只是字段错位。
                    //  (c) 本文件内部自相矛盾：RemoveMember(sub4) 与 UpdateMemberRole(sub5)
                    //      都用 TailSlot25 作 memberName、TailSlot10 作 roleName，唯 sub3 反着写。
                    result = _zongpaiService.AddMember(masterName,
                        LegacyGbkText.Decode(request.AddMemberMemberName),
                        LegacyGbkText.Decode(request.AddMemberRoleName)) ? 0 : 1;
                    break;
                case NativeZongpaiSubCommand.RemoveMember:
                    // 0x593198: where MasterName=tail+0x00 and MemberName=tail+0x25.
                    result = _zongpaiService.RemoveMember(masterName,
                        LegacyGbkText.Decode(request.TailSlot25)) ? 0 : 1;
                    break;
                case NativeZongpaiSubCommand.UpdateMemberRole:
                    // 0x5932A4: set RoleName=tail+0x10 where MasterName=tail+0x00
                    // and MemberName=tail+0x25.
                    result = _zongpaiService.UpdateMemberRole(masterName,
                        LegacyGbkText.Decode(request.TailSlot25),
                        LegacyGbkText.Decode(request.TailSlot10)) ? 0 : 1;
                    break;
                case NativeZongpaiSubCommand.UpdateStudentExp:
                    // worker 0x59356C。⚠️ 不是绝对赋值：
                    //   0x5935AC  call 0x591C8C        ; **饱和累加** StudentExp
                    //   0x5935B1  mov [ebp-0x18], eax  ; eax = 实际发放量
                    //   0x5935B4  cmp [ebp-0x18],0 / jbe 0x5935F2
                    //             ⇒ 实发量 <= 0 时**连 SQL 都不发**
                    //   0x5935C1  mov eax,[master+0x14] ; SQL 写的是**累加后的新总额**
                    // 上限 0xFFB43480 = 4290000000（0x591CA0/0x591CC6/0x591CD7）。
                    // 增量取 tail+0x4C，此处按 dword 读（sub 2 读同一偏移的 word，
                    // 是同一字段的两种宽度，不是两个字段 —— 0x5943BA vs 0x59420C）。
                    _zongpaiService.AddStudentExpSaturating(masterName,
                        request.StudentExpDelta);
                    // 原版 sub 6 **不回复**：case 分支 0x5943A3 不写 [ebp-0x10]，
                    // 而它在 0x5940A0 已初始化为 0 = ReplyMode None。
                    return;
                case NativeZongpaiSubCommand.UpdateStudentAndMasterExp:
                    // worker 0x593670。⚠️ 规格把它描述成「同时加师徒与师父经验」是**错的**，
                    // 字节说它是「扣师徒经验 → ÷10 → 加师父经验」的**转换**：
                    //   0x5936C2  call 0x591CF8        ; **扣减** StudentExp（不足即拒）
                    //   0x5936C7  test al,al / je      ; 拒则整段不做
                    //   0x59370F  mov ecx,0xa / div ecx ; ★师父经验 = 增量 ÷ 10（无符号）
                    //   0x59371D  call 0x591D28        ; 饱和累加 MasterExp
                    //   0x593725  cmp [ebp-0x1c],0 / jbe ; 实发量 <= 0 则不发第二条 SQL
                    // 两条 SQL 用的是**不同常量**：0x593790（StudentExp，带 UpdateTime）
                    // 与 0x5937EC（MasterExp，**不带** UpdateTime）。
                    {
                        var delta = request.ConvertExpAmount;
                        if (!_zongpaiService.SubtractStudentExp(masterName, delta))
                        {
                            // 0x5936C9 je 0x593763 ⇒ 扣减失败则不加师父经验。
                            result = 2;
                            break;
                        }
                        // 0x59370F..0x593716：div 是无符号除法，余数丢弃。
                        _zongpaiService.AddMasterExpSaturating(masterName, delta / 10);
                        result = 0;
                    }
                    break;
                case NativeZongpaiSubCommand.UpdateMasterExp:
                    // worker 0x59382C。⚠️ 是**扣减**不是累加也不是赋值：
                    //   0x59387A  call 0x591D94        ; 扣减 MasterExp（0x591DAA ja 不足即拒）
                    //   0x59387F  test al,al / je      ; 拒则不发 SQL
                    //   0x59388F  mov eax,[master+0x18] ; SQL 写扣减后的余额
                    // SQL 常量 0x5938F0（带 UpdateTime），与 sub 7 的 0x5937EC 不同。
                    result = _zongpaiService.SubtractMasterExp(masterName,
                        request.MasterExpDelta) ? 0 : 2;
                    break;
                case NativeZongpaiSubCommand.UpdateMasterLevel:
                    // 0x593944: MasterLevel update; the original answers with the
                    // 0x84-byte frame carrying a 48-byte record it fills in-place.
                    // That record's layout is not yet reversed, so reply with the
                    // result only and leave the record zeroed rather than invent it.
                    //
                    // ⚠️ 原实现用 request.TailValue50 当等级 —— **那是错的**。原版
                    // 完全忽略请求里的等级，改从活体角色记录取（逐字，worker 0x593944）：
                    //   0x593A7E  call 0x5ABC18            ; 按名字查活体角色记录
                    //   0x593A86  cmp [ebp-0x18],0 / je    ; 查不到 -> 整段跳过，不写库
                    //   0x593A8F  mov ax, word [eax+0x3e]  ; ★等级 = 活体对象 +0x3E
                    //   0x593A96  cmp ax, word [edx+0x20]  ; ⚠️ edx = [ebp+8] = **0x30 字节栈上出参**，
                    //                                        ; 不是宗派记录！记录在 [ebp-0x14]。
                    //                                        ; 该出参的 +0x20 由 0x593A58 从 rec+0x10 拷来，
                    //                                        ; 所以这里比的仍是记录里的等级值（rec+0x10）
                    //   0x593A9A  je 0x593AF5              ; ★相等则不写库（幂等短路）
                    //   0x593AA6  mov word [edx+0x20], ax  ; 写**出参**（回包用），不是写记录内存
                    //   0x593AB1  movzx eax, word [eax+0x20] ; SQL 参数用更新后的值
                    // 请求里的 tail+0x50 在整个 worker 里没有任何读取点。
                    // 宽度：word（movzx），DDL 是 MasterLevel smallint unsigned。
                    {
                        var levelOwner = request.MasterNameSlot35;
                        if (!_playRecordService.TryGetNativeCharacterByName(
                                levelOwner, out var levelChar))
                        {
                            // 0x593A8A je：查不到角色 -> 不写库、不改内存。
                            // 原版此时 [ebp-0x10] 保持进入 case 时的值（R=1），
                            // 故仍回包，只是不落库。
                            result = 1;
                        }
                        else
                        {
                            var liveLevel = (ushort)levelChar.Level;
                            result = _zongpaiService.UpdateMasterLevelFromLive(
                                LegacyGbkText.Decode(levelOwner),
                                liveLevel) ? 0 : 1;
                        }
                    }
                    SendNativeZongpaiReply(sender,
                        NativeZongpaiProtocol.CreateMasterLevelResponse(
                            request, result, ReadOnlySpan<byte>.Empty),
                        NativeZongpaiReplyMode.Sender);
                    return;
                case NativeZongpaiSubCommand.DeleteMaster:
                    // ⚠️ 原注释说「C# 的两表事务删除是忠实的」——**那是错的**，它只比对了
                    // 调用顺序，漏了原版的前置门，且删除范围比原版宽。
                    //
                    // 原版 worker 0x593F6C：
                    //   0x593F9B  call 0x49BAA8          ; 按 MasterName 查内存表记录
                    //   0x593FA3  cmp [ebp-0x10],0 / je  ; 查不到 -> 整段跳过
                    //   0x593FB3  call 0x591DC4          ; ★成员数门
                    //   0x593FB8  test al,al / je 0x59400D ; 不满足 -> **连删都不删**
                    //   0x593FCA  call 0x593198          ; delete ZongpaiMember（单行）
                    //   0x593FE7  call 0x40CF30 + 0x59403C ; delete zongpaibase（ecx=0，1 参）
                    //   0x594000  call 0x49B7EC          ; 移除内存表记录
                    //
                    // 门 0x591DC4 逐字：
                    //   0x591DD0  mov eax,[eax+4]        ; 成员容器
                    //   0x591DD3  mov edx,[eax]
                    //   0x591DD5  call dword [edx+0x14]  ; 虚调用 Count
                    //   0x591DD8  dec eax
                    //   0x591DD9  sete byte [ebp-5]      ; ⇒ Count **恰为 1**
                    //
                    // 两处背离：
                    //  (1) C# 无门 —— 任何成员数都允许解散；
                    //  (2) 原版删成员走 0x593198 =
                    //      `delete from ZongpaiMember where MasterName="%s" and MemberName="%s"`
                    //      （**单行**；因门保证只有 1 个成员，那一行即全部），
                    //      而 C# 是 `DELETE ... WHERE MasterName=@n`（**删光全部成员行**）。
                    //  在门成立时两者等价；门一缺，C# 就会在多成员师门上删光成员 ⇒ 写坏数据。
                    //
                    // 故在此补门：成员数不为 1 时按原版**静默不做任何删除**（result 保持 0，
                    // 原版 0x59400D 出口也不置错误码），并保留 C# 的 masterName 单参调用
                    // （门成立 ⇒ 唯一成员行的 MemberName 必然属于该 master，范围等价）。
                    if (_zongpaiService.CountMembers(
                            LegacyGbkText.Decode(request.MasterNameSlot35)) == 1)
                    {
                        result = _zongpaiService.DeleteMaster(
                            LegacyGbkText.Decode(request.MasterNameSlot35)) ? 0 : 1;
                    }
                    break;
                default:
                    // Sub-command 0 is the shared exit (no-op in original).
                    DBShare.MainOutMessage(
                        "[GameSoc] 原生0170宗派子命令暂未实现: "
                        + $"{(int)request.SubCommand}");
                    return;
                case NativeZongpaiSubCommand.Enumerate:
                {
                    // worker 0x5933CC：按 tail+0x35 的成员名，遍历内存宗派表查找
                    // 所属师父名（out1）与角色名（out2）。
                    // 结果通过 CreateEnumerateResponse 把 out1/out2 写回 tail+0x00/+0x10
                    // 再拷入标准回包（详见 NativeZongpaiProtocol.CreateEnumerateResponse）。
                    // 数据源：_zongpaiService.GetMasterByMemberName 查库，近似原版内存查。
                    // 原版结果码极性反（0=找到），CreateEnumerateResponse 内部处理。
                    var memberName = LegacyGbkText.Decode(request.EnumerateMemberName);
                    var members = _zongpaiService.LoadAllMembers();
                    var match = members.Find(m =>
                        string.Equals(m.MemberName, memberName,
                            StringComparison.Ordinal));
                    byte[] outMasterName = Array.Empty<byte>();
                    byte[] outRoleName = Array.Empty<byte>();
                    var found = match != null;
                    if (found)
                    {
                        // out1 = 师父名，来自宗派记录 +0x0C
                        //   （0x59346F `mov eax,[ebp-0xC]` / 0x593475 `mov edx,[edx+0xC]`；
                        //    +0x0C 由 ctor 0x591C60 `add eax,0xC` 写入 = 该记录的 MasterName）
                        // out2 = 角色名，来自成员容器里该成员对应值对象的首字段
                        //   （helper 0x591BE4：0x591C0A `call [ebx+0x8C]` 按名字查成员 →
                        //    0x591C1F `call [ecx+0x18]` 取值对象 → 0x591C28 `mov edx,[edx]`）
                        // 二者正对应 ZongpaiMemberInfo 的 MasterName / RoleName。
                        outMasterName = LegacyGbkText.Encode(match.MasterName);
                        outRoleName = LegacyGbkText.Encode(match.RoleName);
                    }
                    SendNativeZongpaiReply(sender,
                        NativeZongpaiProtocol.CreateEnumerateResponse(
                            request, found, outMasterName, outRoleName),
                        NativeZongpaiReplyMode.Sender);
                    return;
                }
                case NativeZongpaiSubCommand.QueryMembers:
                {
                    // worker 0x593B74：按 tail+0x00（QueryMembersMasterName）查成员列表，
                    // 每行一条 0x29 字节记录，字段布局见 NativeZongpaiProtocol.BuildMemberRecord。
                    // 回包的 body+0x25 回显 tail+0x35（QueryMembersEchoName）。
                    // count <= 0 时仍发空帧（0x594613 jle 在分配器之后）。
                    var queryName = LegacyGbkText.Decode(request.QueryMembersMasterName);
                    var allMembers = _zongpaiService.LoadAllMembers();
                    var rows = allMembers.FindAll(m =>
                        string.Equals(m.MasterName, queryName,
                            StringComparison.Ordinal));
                    var count = rows.Count;
                    byte[] recordBytes = Array.Empty<byte>();
                    if (count > 0)
                    {
                        recordBytes = new byte[count * NativeZongpaiProtocol.MemberRecordSize];
                        for (var i = 0; i < count; i++)
                        {
                            var row = rows[i];
                            // 0x593CC5 `call 0x5ABC18`：按 MemberName 查活体角色。
                            // 查不到 → level=0, online=false（0x593C9A/0x593CA3 清零）。
                            ushort level = 0;
                            var online = false;
                            if (_playRecordService.TryGetNativeCharacterByName(
                                    LegacyGbkText.Encode(row.MemberName),
                                    out var liveChar))
                            {
                                // 0x593CD6: mov ax,[live+0x3E] = Level。
                                level = (ushort)liveChar.Level;
                                // 0x593CE4: mov al,[live+0x25] = 在线标志（非 0 = 在线）。
                                // ChrIndexInfo 没有对应字段；NativeBusy ≈ 在线。
                                online = liveChar.NativeBusy;
                            }
                            var rec = NativeZongpaiProtocol.BuildMemberRecord(
                                LegacyGbkText.Encode(row.RoleName),
                                LegacyGbkText.Encode(row.MemberName),
                                level, online);
                            rec.CopyTo(recordBytes, i * NativeZongpaiProtocol.MemberRecordSize);
                        }
                    }
                    SendNativeZongpaiReply(sender,
                        NativeZongpaiProtocol.CreateMemberListResponse(
                            request, count, recordBytes),
                        NativeZongpaiReplyMode.Sender);
                    return;
                }
                case NativeZongpaiSubCommand.ReadNotice:
                {
                    // worker 0x593D30：按 HEADER+0x35（NoticeMasterName）查宗派记录，
                    // 取 record[+0x1C]（Delphi 内存里的 Notice 长串指针）写进 out 参数。
                    // 原版内存中 +0x1C 是 ZongpaiMasterInfo.Notice（blob 字节，DDL 0x5BEE34）。
                    // 0x594695 `cmp [ebp-0x24],0 / je` —— out 空则不发回包（不写 [ebp-0x10]）。
                    var noticeMaster = LegacyGbkText.Decode(request.NoticeMasterName);
                    var masterRec = _zongpaiService.GetMaster(noticeMaster);
                    var notice = masterRec?.Notice;
                    if (notice == null || notice.Length == 0)
                    {
                        // 0x593D5C je 0x593D6C：查不到或 Notice 指针为 nil ⇒ out 空串 ⇒ 不回包。
                        return;
                    }
                    SendNativeZongpaiReply(sender,
                        NativeZongpaiProtocol.CreateNoticeResponse(request, notice),
                        NativeZongpaiReplyMode.Sender);
                    return;
                }
                case NativeZongpaiSubCommand.ModifyNotice:
                {
                    // worker 0x593D70：
                    //   0x593DA0 `cmp [ebp+0xC],0x80 / jg` ⇒ tail 长度 > 0x80 则静默退出
                    //   0x593DB0 `mov eax,[eax+0x18]`（DB 管理器）/`call 0x49BAA8` 查 HEADER+0x35
                    //   0x593DBE `cmp [ebp-0x10],0 / je` ⇒ 查不到也静默退出
                    //   0x593DE0 format + SQL 事务把 tail 字节写进 Notice blob
                    //   0x593E80 `add eax,0x1c` / `mov edx,[edx]` / `call 0x404C4C`
                    //            ⇒ 把新 Notice 长串**也写回内存对象的 +0x1C**
                    //   回包正文 = in-memory 写回后的长串（sub 11 的路径），故 ReadNotice
                    //   (sub 11) 紧接着就能读到刚写入的值。
                    // sub 12 的 IsNoticeLengthAccepted 门在此显式检查，与 0x593DA7 一一对应。
                    var tailLen = request.Tail.Length;
                    if (!NativeZongpaiProtocol.IsNoticeLengthAccepted(tailLen))
                    {
                        // 0x593DA7 jg 0x593ECC ⇒ 既不落库也不回包。
                        return;
                    }
                    var noticeName = LegacyGbkText.Decode(request.NoticeMasterName);
                    var masterRec12 = _zongpaiService.GetMaster(noticeName);
                    if (masterRec12 == null)
                    {
                        // 0x593DC2 je 0x593ECC ⇒ 查不到静默退出。
                        return;
                    }
                    // 0x593E43 `call dword [ebx+0x10]`（Stream.Write）：把 tail 整块写入。
                    // ⚠️ ModifyNoticeText = request.Tail，内嵌 NUL 原样进 blob。
                    var newNotice = request.ModifyNoticeText;
                    if (!_zongpaiService.UpdateNotice(noticeName, newNotice))
                    {
                        // SQL 失败 ⇒ 按 0x593DF0 `dec eax / jne 0x593ECC`：
                        // UpdateNotice 返回码非 0 ⇒ 静默退出，不回包。
                        return;
                    }
                    // 回包：把刚写入的 Notice 原样返回（原版在 0x593E80 写回内存后
                    // sub 11 的路径读同一块内存发包，效果相同）。
                    // 0x594695 空判：写入成功则 Notice 必然非空，直接发。
                    SendNativeZongpaiReply(sender,
                        NativeZongpaiProtocol.CreateNoticeResponse(request, newNotice),
                        NativeZongpaiReplyMode.Sender);
                    return;
                }
            }

            var mode = NativeZongpaiProtocol.GetReplyMode(request.SubCommand);
            if (mode == NativeZongpaiReplyMode.None) return;
            SendNativeZongpaiReply(sender,
                NativeZongpaiProtocol.CreateStandardResponse(request, result),
                mode);
        }

        private void SendNativeZongpaiReply(TServerInfo sender,
            LegacyDbServerFrame response, NativeZongpaiReplyMode mode)
        {
            if (!LegacyDbServerFrameCodec.TryEncode(response, out var wire,
                    out var error, NativeDbServerProtocol.MaximumFrameLength))
            {
                DBShare.MainOutMessage("[GameSoc] 原生0170宗派响应编码失败: " + error);
                return;
            }

            if (mode == NativeZongpaiReplyMode.Sender)
            {
                TrySendNativeZongpai(sender, wire);
                return;
            }

            // 0x59E450 with dl==0: every peer except serverType 9 (DB tool).
            List<TServerInfo> targets;
            lock (_serverListLock)
            {
                targets = new List<TServerInfo>();
                foreach (var peer in _serverList)
                {
                    if (peer?.Socket?.Connected != true
                        || peer.WireModeDetector.Mode
                        != DbServerWireMode.NativeType12
                        || Volatile.Read(ref peer.NativeServerType) == 9)
                        continue;
                    targets.Add(peer);
                }
            }
            foreach (var target in targets) TrySendNativeZongpai(target, wire);
        }

        private static void TrySendNativeZongpai(TServerInfo target, byte[] wire)
        {
            if (target?.Socket == null) return;
            try { SendAll(target.Socket, wire); }
            catch (Exception ex) when (ex is SocketException
                                       || ex is ObjectDisposedException)
            {
                DBShare.MainOutMessage(
                    "[GameSoc] 原生0170宗派响应发送失败: " + ex.Message);
            }
        }

        private void ProcessNativeCharacterNameRegistration(
            LegacyDbServerFrame frame)
        {
            if (!NativeAuxiliaryType1Protocol.TryDecodeCharacterNameRegistration(
                    frame, out var characterName, out var error))
            {
                DBShare.MainOutMessage(
                    "[GameSoc] 原生0157人物名登记拒绝: " + error);
                return;
            }
            _playRecordService.RegisterNativeCharacterName(characterName);
        }

        private void ProcessNativeDynamicImageRequest(TServerInfo sender,
            LegacyDbServerFrame frame)
        {
            if (!NativeAuxiliaryType1Protocol.TryDecodeDynamicImageRequest(
                    frame, out var request, out var error))
            {
                DBShare.MainOutMessage(
                    "[GameSoc] 原生0159动态图片请求拒绝: " + error);
                return;
            }

            // The original always replies; an unavailable/empty image source is status 0.
            var response = NativeAuxiliaryType1Protocol
                .CreateDynamicImageResponse(request);
            if (!LegacyDbServerFrameCodec.TryEncode(response, out var wire,
                    out error, NativeDbServerProtocol.MaximumFrameLength))
            {
                DBShare.MainOutMessage(
                    "[GameSoc] 原生0159动态图片响应编码失败: " + error);
                return;
            }
            try { SendAll(sender.Socket, wire); }
            catch (Exception ex) when (ex is SocketException
                                       || ex is ObjectDisposedException)
            {
                DBShare.MainOutMessage(
                    "[GameSoc] 原生0159动态图片响应发送失败: " + ex.Message);
            }
        }

        private void ProcessNativeDominatorPet(TServerInfo sender,
            LegacyDbServerFrame frame, ushort command)
        {
            if (!NativeDominatorPetProtocol.TryDecodeRequest(frame, command,
                    out var request, out var error))
            {
                DBShare.MainOutMessage(
                    $"[GameSoc] 原生主宰宠物0x{command:X4}请求拒绝: " + error);
                return;
            }

            var ownerExists = _playRecordService.TryGetNativeCharacterByName(
                request.MasterName, out var owner);
            if (command == NativeDominatorPetProtocol.SaveCommand)
            {
                if (ownerExists)
                    _nativeDominatorPets.Save(_petService, owner.UserId,
                        request.MasterName, request.Data);
                return;
            }

            LegacyDbServerFrame response;
            if (command == NativeDominatorPetProtocol.CreateCommand)
            {
                var result = ownerExists
                    ? _nativeDominatorPets.Create(_petService, owner.UserId,
                        request.MasterName)
                    : -1;
                response = NativeDominatorPetProtocol.CreateCreateResponse(
                    request.MasterName, result);
            }
            else
            {
                var loaded = ownerExists
                    ? _nativeDominatorPets.Load(_petService, owner.UserId,
                        request.MasterName)
                    : new NativeDominatorPetLoadResult { Result = -1 };
                response = NativeDominatorPetProtocol.CreateLoadResponse(
                    request.MasterName, loaded.Result, loaded.Data);
            }

            if (!LegacyDbServerFrameCodec.TryEncode(response, out var wire,
                    out error, NativeDbServerProtocol.MaximumFrameLength))
            {
                DBShare.MainOutMessage(
                    "[GameSoc] 原生主宰宠物响应编码失败: " + error);
                return;
            }
            try { SendAll(sender.Socket, wire); }
            catch (Exception ex) when (ex is SocketException
                                       || ex is ObjectDisposedException)
            {
                DBShare.MainOutMessage(
                    "[GameSoc] 原生主宰宠物响应发送失败: " + ex.Message);
            }
        }

        private void ProcessNativeAccountStorageLoad(TServerInfo sender,
            LegacyDbServerFrame frame)
        {
            if (!NativeAccountStorageProtocol.TryDecode(frame,
                    NativeAccountStorageProtocol.LoadCommand,
                    out var request, out var error))
            {
                DBShare.MainOutMessage(
                    "[GameSoc] 原生016B账号仓库请求拒绝: " + error);
                return;
            }
            var loaded = _nativeAccountStorage.Load(
                _storageService, request.Account);
            var response = NativeAccountStorageProtocol.CreateLoadResponse(
                request, loaded.Result, loaded.Data);
            if (!LegacyDbServerFrameCodec.TryEncode(response, out var wire,
                    out error, NativeDbServerProtocol.MaximumFrameLength))
            {
                DBShare.MainOutMessage(
                    "[GameSoc] 原生016B账号仓库响应编码失败: " + error);
                return;
            }
            try { SendAll(sender.Socket, wire); }
            catch (Exception ex) when (ex is SocketException
                                       || ex is ObjectDisposedException)
            {
                DBShare.MainOutMessage(
                    "[GameSoc] 原生016B账号仓库响应发送失败: " + ex.Message);
            }
        }

        private void ProcessNativeAccountStorageSave(TServerInfo sender,
            LegacyDbServerFrame frame)
        {
            if (!NativeAccountStorageProtocol.TryDecode(frame,
                    NativeAccountStorageProtocol.SaveCommand,
                    out var request, out var error))
            {
                DBShare.MainOutMessage(
                    "[GameSoc] 原生016C账号仓库请求拒绝: " + error);
                return;
            }
            if (!_nativeAccountStorage.StageSave(
                    request.Account, request.Data)) return;
            var response = NativeAccountStorageProtocol.CreateSaveResponse(
                request);
            TrySendNativeResponse(sender, response, "016C账号仓库");
        }

        private void ProcessNativeHallOfFame(TServerInfo sender,
            LegacyDbServerFrame frame)
        {
            if (!NativeHallOfFameProtocol.TryDecode(frame, out var rank,
                    out var error))
            {
                DBShare.MainOutMessage(
                    "[GameSoc] 原生0172名人堂请求拒绝: " + error);
                return;
            }
            var record = _nativeHallOfFame.Load(rank);
            if (record == null
                || record.Length != NativeHallOfFameProtocol.RecordSize)
                return;
            var response = NativeHallOfFameProtocol.CreateResponse(rank, record);
            TrySendNativeResponse(sender, response, "0172名人堂");
        }

        private void ProcessNativeTransferScore(TServerInfo sender,
            LegacyDbServerFrame frame)
        {
            if (!NativeTransferScoreProtocol.TryDecode(frame,
                    out var request, out var error))
            {
                DBShare.MainOutMessage(
                    "[GameSoc] 原生0176跨区积分请求拒绝: " + error);
                return;
            }
            bool success;
            try
            {
                success = _transferAreaService.TryDeductNativeScore(
                    request.CharacterName, request.ScoreType, request.Amount);
            }
            catch { success = false; }
            var response = NativeTransferScoreProtocol.CreateResponse(
                request, success);
            TrySendNativeResponse(sender, response, "0176跨区积分");
        }

        private void ProcessNativeAwardPlayer(TServerInfo sender,
            LegacyDbServerFrame frame)
        {
            NativeAwardPlayerRequest request;
            bool success;
            if (!NativeAwardPlayerProtocol.TryDecode(frame, out request,
                    out _))
            {
                // The original returns result 0 for a non-24-byte attachment.
                if (frame.Payload.Length < NativeAwardPlayerProtocol.HeaderSize)
                    return;
                var header = frame.Payload.AsSpan(0,
                    NativeAwardPlayerProtocol.HeaderSize).ToArray();
                var padded = new byte[NativeAwardPlayerProtocol.HeaderSize
                                      + NativeAwardPlayerProtocol.BodySize];
                header.CopyTo(padded, 0);
                if (!NativeAwardPlayerProtocol.TryDecode(
                        new LegacyDbServerFrame(1, frame.Reserved, padded),
                        out request, out _))
                    return;
                success = false;
            }
            else
            {
                success = _nativeAwardPlayers.Insert(request);
            }
            TrySendNativeResponse(sender,
                NativeAwardPlayerProtocol.CreateResponse(request, success),
                "015B奖励人物");
        }

        private void ProcessNativeMailItem(TServerInfo sender,
            LegacyDbServerFrame frame)
        {
            if (!NativeItemInjectionProtocol.TryDecodeMail(frame,
                    out var request, out _))
                return;

            ushort result;
            if (!_playRecordService.TryGetNativeCharacterByName(
                    request.TargetName, out var target))
                result = 0;
            else if (target.NativeBusy)
                result = 4;
            else
            {
                var loaded = _humanLogicalCache.GetOrLoad(target.Idx,
                    () => LoadNativeHumanPersistence(target.Idx));
                if (loaded == null)
                    result = 3;
                else
                {
                    var item = request.Attachment.AsSpan(0,
                        NativeItemInjectionProtocol.ItemSize).ToArray();
                    result = MapMailInjectionResult(
                        _humanLogicalCache.TryInjectItem(target.Idx, item,
                            includeStorage: true,
                            persistence => EnqueueNativeSave(
                                new NativeSaveWorkItem(target.Idx,
                                    persistence))));
                }
            }

            TrySendNativeResponse(sender,
                NativeItemInjectionProtocol.CreateMailResponse(request, result),
                "0154物品投递");
        }

        private void ProcessNativeMasterRelation(TServerInfo sender,
            LegacyDbServerFrame frame)
        {
            if (!NativeMasterRelationProtocol.TryDecode(frame,
                    out var request, out _)
                || !_playRecordService.TryGetNativeCharacterByName(
                    request.StudentName, out var target)
                || target == null || target.DeleteState != 0
                || target.IsDelete || target.NativeBusy)
                return;

            var persistence = _humanLogicalCache.GetOrLoad(target.Idx,
                () => LoadNativeHumanPersistence(target.Idx));
            if (persistence == null
                || !NativeHumanLogicalCache.TryExtractRaw(persistence,
                    out var nativeData, out _))
                return;

            if (request.Subcommand
                == NativeMasterRelationProtocol.MarriageClearSubcommand)
            {
                // 战神 DBServer's marriage-clear branch (sub_5A8750 @0x5A8825) gates
                // ONLY on "the spouse slot is non-empty": 0x5A8844 `cmp byte [eax],0`
                // / 0x5A8847 `je done`, where eax is the social-block base returned by
                // 0x5804A4.  It never compares the stored spouse name against the
                // name carried in the request.  The reciprocal SequenceEqual that used
                // to live here rejected legitimate offline divorces whenever the two
                // spellings differed at all.  The emptiness gate itself now lives in
                // TryClearMarriageRelation, matching the native ordering (native reads
                // the slot through the same accessor before touching anything).
                // Note: subcmd 3 (master clear) is different — native DOES compare
                // there, case-insensitively via 0x40AFB0, and that check is retained.
                var marriageClear = _humanLogicalCache.TryClearMarriageRelation(
                    target.Idx, request.MasterName,
                    value => EnqueueNativeSave(
                        new NativeSaveWorkItem(target.Idx, value)));
                if (marriageClear == NativeHumanMasterRelationState.NotLoaded)
                    return;

                TrySendNativeResponse(sender,
                    NativeMasterRelationProtocol.CreateResetResponse(false),
                    "0152婚姻关系解除");
                return;
            }

            if (request.Subcommand == NativeMasterRelationProtocol.ResetSubcommand)
            {
                var level = (ushort)(nativeData[0x3C]
                                      | nativeData[0x3D] << 8);
                if (level < 35 || nativeData[0x16F] == 0)
                {
                    TrySendNativeResponse(sender,
                        NativeMasterRelationProtocol.CreateResetResponse(false),
                        "0152王师关系重设");
                    return;
                }

                _nativeRelationLog.ProcessMasterReset(request.MasterName,
                    request.StudentName);
                var update = _humanLogicalCache.TrySetMasterName(target.Idx,
                    request.MasterName, value => EnqueueNativeSave(
                        new NativeSaveWorkItem(target.Idx, value)));
                if (update == NativeHumanMasterRelationState.NotLoaded)
                    return;

                // The original acknowledges after the in-memory mutation even if
                // its asynchronous persistence worker later fails to write.
                TrySendNativeResponse(sender,
                    NativeMasterRelationProtocol.CreateResetResponse(true),
                    "0152王师关系重设");
                return;
            }

            var studentLength = nativeData[0];
            var masterOffset = NativeHumanDataCodec.MasterNameOffset;
            var storedMasterLength = nativeData[masterOffset];
            if (studentLength > 15
                || storedMasterLength > NativeHumanDataCodec.MasterNameCapacity)
                return;
            var storedMaster = nativeData.AsSpan(masterOffset + 1,
                storedMasterLength);
            if (!NativeMasterRelationProtocol.EqualsAsciiIgnoreCase(
                    storedMaster, request.MasterName))
            {
                TrySendNativeResponse(sender,
                    NativeMasterRelationProtocol.CreateResetResponse(false),
                    "0152王师关系解除");
                return;
            }

            var studentName = nativeData.AsSpan(1, studentLength).ToArray();
            _nativeRelationLog.ProcessMasterClear(request.MasterName,
                studentName);
            var clear = _humanLogicalCache.TryClearMasterRelation(target.Idx,
                request.MasterName, value => EnqueueNativeSave(
                    new NativeSaveWorkItem(target.Idx, value)));
            if (clear == NativeHumanMasterRelationState.NotLoaded)
                return;

            // The original subtype 3 returns the unchanged zeroed body even
            // when the relation was removed successfully.
            TrySendNativeResponse(sender,
                NativeMasterRelationProtocol.CreateResetResponse(false),
                "0152王师关系解除");
        }

        private void ProcessNativeItemExtraction(TServerInfo sender,
            LegacyDbServerFrame frame)
        {
            if (!NativeItemExtractionProtocol.TryDecode(frame,
                    out var request, out _))
                return;

            ushort status = 0;
            byte[] itemRecord = null;
            if (_playRecordService.TryGetNativeCharacterByName(
                    request.TargetName, out var target)
                && target != null)
            {
                lock (_nativeSaveMutationLock)
                {
                    if (target.DeleteState != 0 || target.IsDelete)
                        status = NativeItemExtractionProtocol.CharacterDeleted;
                    else if (target.NativeBusy)
                        status = NativeItemExtractionProtocol.CharacterBusy;
                    else if (_nativeDominatorPets.TryExtractItem(_petService,
                                 target.UserId, request.TargetName,
                                 request.MakeIndex, out itemRecord))
                        status = NativeItemExtractionProtocol.Success;
                    else
                    {
                        status = NativeItemExtractionProtocol.ItemNotFound;
                        var loaded = _humanLogicalCache.GetOrLoad(target.Idx,
                            () => LoadNativeHumanPersistence(target.Idx));
                        if (loaded != null)
                        {
                            var result = _humanLogicalCache.TryExtractItem(
                                target.Idx, request.MakeIndex,
                                value => EnqueueNativeSave(
                                    new NativeSaveWorkItem(target.Idx, value)),
                                out itemRecord);
                            if (result is NativeHumanItemExtractionState.Success
                                or NativeHumanItemExtractionState.SaveRejected)
                                status = NativeItemExtractionProtocol.Success;
                        }
                        if (status != NativeItemExtractionProtocol.Success
                            && _nativeAccountStorage.TryExtractOfflineItem(
                                _storageService, target.PTIDBytes,
                                request.MakeIndex, out itemRecord))
                            status = NativeItemExtractionProtocol.Success;
                    }
                }
            }

            // The original performs the hero lookup after every non-success
            // human result, including missing, deleted, and busy characters.
            if (status != NativeItemExtractionProtocol.Success
                && TryExtractNativeHeroItem(request.TargetName,
                    request.MakeIndex, out itemRecord))
                status = NativeItemExtractionProtocol.Success;

            TrySendNativeResponse(sender,
                NativeItemExtractionProtocol.CreateResponse(request, status,
                    itemRecord), "0153离线物品提取");
        }

        private bool TryExtractNativeHeroItem(byte[] masterName,
            int makeIndex, out byte[] itemRecord)
        {
            itemRecord = null;
            string decodedMaster;
            try { decodedMaster = LegacyGbkText.Decode(masterName); }
            catch (ArgumentException) { return false; }

            lock (_heroCreateLock)
            {
                var heroes = _heroRecordService.QueryHeroesByMaster(decodedMaster)
                             ?? new List<HeroIndexInfo>();
                heroes.AddRange(_heroRecordService.QueryDeletedHeroesByMaster(
                                    decodedMaster)
                                ?? new List<HeroIndexInfo>());
                var visited = new HashSet<int>();
                foreach (var hero in heroes)
                {
                    if (hero == null || hero.Idx <= 0 || !visited.Add(hero.Idx))
                        continue;
                    byte[] heroName;
                    try
                    {
                        heroName = hero.HeroNameBytes?.Length > 0
                            ? hero.HeroNameBytes
                            : LegacyGbkText.Encode(hero.HeroName);
                    }
                    catch (ArgumentException) { continue; }
                    var snapshot = _heroLogicalCache.GetOrLoad(hero.Idx,
                        () => LoadNativeDbToolHeroSnapshot(hero, heroName));
                    _heroAttachmentState.TryGetSlotPlusOne(hero.Idx,
                        out var selector);
                    if (snapshot == null
                        || !_heroLogicalCache.TryExtractItem(hero.Idx,
                            selector, makeIndex, out var updated,
                            out itemRecord))
                        continue;

                    // Native code returns the item after changing memory and does
                    // not wait for its background persistence worker.
                    _ = EnqueueHeroExactSnapshot(updated);
                    return true;
                }
                return false;
            }
        }

        private void ProcessNativeBagItem(TServerInfo sender,
            LegacyDbServerFrame frame)
        {
            if (!NativeItemInjectionProtocol.TryDecodeBag(frame,
                    out var request, out _))
                return;

            ushort result;
            if (!request.OuterLengthValid)
                result = 0;
            else if (!_playRecordService.TryGetNativeCharacterByName(
                         request.CharacterName, out var target))
                result = 5;
            else if (target.NativeBusy)
                result = 4;
            else
            {
                var loaded = _humanLogicalCache.GetOrLoad(target.Idx,
                    () => LoadNativeHumanPersistence(target.Idx));
                if (loaded == null)
                    result = 3;
                else if (request.Attachment.Length
                         != NativeItemInjectionProtocol.ItemSize)
                    result = 2;
                else
                    result = MapBagInjectionResult(
                        _humanLogicalCache.TryInjectItem(target.Idx,
                            request.Attachment, includeStorage: false,
                            persistence => EnqueueNativeSave(
                                new NativeSaveWorkItem(target.Idx,
                                    persistence))));
            }

            TrySendNativeResponse(sender,
                NativeItemInjectionProtocol.CreateBagResponse(request, result),
                "015A背包物品");
        }

        private static ushort MapMailInjectionResult(
            NativeHumanItemInjectionState state) => state switch
        {
            NativeHumanItemInjectionState.Success => 1,
            NativeHumanItemInjectionState.NotLoaded => 3,
            _ => 2
        };

        private static ushort MapBagInjectionResult(
            NativeHumanItemInjectionState state) => state switch
        {
            NativeHumanItemInjectionState.Success => 1,
            NativeHumanItemInjectionState.NotLoaded => 3,
            _ => 2
        };

        private void ProcessNativeCharacterRestore(TServerInfo sender,
            LegacyDbServerFrame frame)
        {
            if (!NativeCharacterAdminProtocol.TryDecodeRestore(frame,
                    out var request, out _))
                return;
            var restored = _playRecordService.TryRestoreNativeCharacter(
                request.TargetCharacter, out var character);
            if (restored)
            {
                _playDataService.RegisterNativeIndex(character.Idx,
                    character.ChrName);
                EnqueueNativeSave(
                    NativeSaveWorkItem.ForCharacterRestore(character.Idx));
            }
            TrySendNativeResponse(sender,
                NativeCharacterAdminProtocol.CreateRestoreResponse(
                    request, restored),
                "019A恢复人物");
        }

        private void ProcessNativeCharacterLookup(TServerInfo sender,
            LegacyDbServerFrame frame)
        {
            if (!NativeCharacterAdminProtocol.TryDecodeLookup(frame,
                    out var request, out _))
                return;
            ChrIndexInfo character;
            var found = request.Mode == 4
                ? _playRecordService.TryGetNativeCharacterByUserId(
                    NativeCharacterAdminProtocol.ReadLookupUserId(request),
                    out character)
                : _playRecordService.TryGetNativeCharacterByName(
                    request.CharacterName, out character);
            TrySendNativeResponse(sender,
                NativeCharacterAdminProtocol.CreateLookupResponse(
                    request, found ? character : null),
                "019B人物查询");
        }

        private void ProcessNativeAccountDisconnect(LegacyDbServerFrame frame)
        {
            if (NativeSessionControlProtocol.TryDecodeDisconnect(frame,
                    out var request, out _))
                _nativeAdmission.DisconnectAccount(request.Account);
        }

        private void ProcessNativeOnlineAccountText(LegacyDbServerFrame frame)
        {
            if (NativeOnlineAccountProtocol.TryDecodeText(frame,
                    out var request, out _))
                _nativeAdmission.UpdateOnlineAccountText(
                    request.Account, request.Text);
        }

        private void ProcessNativeOnlineAccountLoginTime(
            LegacyDbServerFrame frame)
        {
            if (NativeOnlineAccountProtocol.TryDecodeLoginTime(frame,
                    out var request, out _))
                _nativeAdmission.UpdateOnlineAccountLoginTime(
                    request.Account, request.Flag);
        }

        private void ProcessNativeDbToolDelete(TServerInfo sender,
            LegacyDbServerFrame frame)
        {
            if (!NativeDbToolProtocol.TryDecodeDelete(frame, out var request,
                    out _))
                return;
            var result = 0;
            try
            {
                result = (byte)request.Operation switch
                {
                    1 => TrySoftDeleteNativeDbToolHuman(request.NameBytes) ? 1 : 0,
                    2 => TryHardDeleteNativeDbToolHuman(request.NameBytes) ? 1 : 0,
                    3 => TryRestoreNativeDbToolHuman(request.NameBytes) ? 1 : 0,
                    4 => TrySoftDeleteNativeDbToolHero(request.HeroNameBytes) ? 1 : 0,
                    5 => TryHardDeleteNativeDbToolHero(request.HeroNameBytes) ? 1 : 0,
                    6 => TryRestoreNativeDbToolHero(request.HeroNameBytes) ? 1 : 0,
                    7 => TryRenameNativeDbToolAccount(request.AccountBytes,
                        request.NameBytes) ? 1 : 0,
                    _ => 0
                };
            }
            catch (Exception)
            {
                // The native tool returns a fixed failure response for operation errors.
            }
            TrySendNativeResponse(sender,
                NativeDbToolProtocol.CreateDeleteResponse(request, result),
                "DB工具0100生命周期");
        }

        private bool TrySoftDeleteNativeDbToolHuman(byte[] nameBytes)
        {
            lock (_nativeSaveMutationLock)
            {
                if (!_playRecordService.TryGetNativeCharacterByName(nameBytes,
                        out var character)
                    || character == null || character.IsSelect || character.IsDelete)
                    return false;
                return _playRecordService.Delete(character.ChrName);
            }
        }

        private bool TryHardDeleteNativeDbToolHuman(byte[] nameBytes)
        {
            lock (_nativeSaveMutationLock)
            {
                if (!_playRecordService.TryGetNativeCharacterByName(nameBytes,
                        out var character)
                    || character == null || character.IsSelect
                    || !_playRecordService.HardDelete(character.Idx))
                    return false;
                AdvanceNativeSaveGeneration(character.Idx, tombstone: true);
                _humanLogicalCache.Remove(character.Idx);
                _playDataService.UnregisterNativeIndex(character.Idx);
                return true;
            }
        }

        private bool TryRestoreNativeDbToolHuman(byte[] nameBytes)
        {
            lock (_nativeSaveMutationLock)
            {
                if (!_playRecordService.TryGetNativeCharacterByName(nameBytes,
                        out var current)
                    || current == null || !current.IsDelete
                    || !_playRecordService.TryRestoreNativeCharacter(nameBytes,
                        out var restored)
                    || restored == null)
                    return false;
                _playDataService.RegisterNativeIndex(restored.Idx,
                    restored.ChrName);
                EnqueueNativeSave(
                    NativeSaveWorkItem.ForCharacterRestore(restored.Idx));
                return true;
            }
        }

        private bool TrySoftDeleteNativeDbToolHero(byte[] heroNameBytes)
        {
            lock (_heroCreateLock)
            {
                if (!_heroRecordService.TryGetNativeHeroByName(heroNameBytes,
                        out var hero)
                    || hero == null || hero.IsDelete
                    || !_heroRecordService.DeleteHero(hero.Idx))
                    return false;
                AdvanceHeroSaveGeneration(hero.Idx, tombstone: false);
                _heroSaveState.Remove(hero.Idx);
                if (_heroLogicalCache.TryGet(hero.Idx, out var snapshot))
                {
                    var deleted = snapshot.WithIndexState(true,
                        snapshot.HeroType, snapshot.Consignation);
                    if (!EnqueueHeroLifecycleSnapshot(deleted))
                        DBShare.MainOutMessage(
                            $"[GameSoc] DB工具0100英雄软删除最终保存入队失败 idx={hero.Idx}");
                }
                else _heroLogicalCache.Remove(hero.Idx);
                return true;
            }
        }

        private bool TryHardDeleteNativeDbToolHero(byte[] heroNameBytes)
        {
            lock (_heroCreateLock)
            {
                if (!_heroRecordService.TryGetNativeHeroByName(heroNameBytes,
                        out var hero)
                    || hero == null)
                    return false;
                return HardDeleteNativeHeroIndex(hero.Idx);
            }
        }

        private bool HardDeleteNativeHeroIndex(int index)
        {
            lock (_heroCreateLock)
            {
                if (index <= 0 || !_heroRecordService.HardDeleteHero(index))
                    return false;
                AdvanceHeroSaveGeneration(index, tombstone: true);
                _heroLogicalCache.Remove(index);
                _heroSaveState.Remove(index);
                _heroAttachmentState.Remove(index);
                _heroDataService.UnregisterNativeIndex(index);
                return true;
            }
        }

        private bool TryRestoreNativeDbToolHero(byte[] heroNameBytes)
        {
            lock (_heroCreateLock)
            {
                if (!_heroRecordService.TryGetNativeHeroByName(heroNameBytes,
                        out var hero)
                    || hero == null || !hero.IsDelete
                    || !_heroRecordService.RestoreHero(hero.HeroName))
                    return false;
                AdvanceHeroSaveGeneration(hero.Idx, tombstone: false);
                _heroSaveState.Remove(hero.Idx);
                if (_heroLogicalCache.TryGet(hero.Idx, out var snapshot))
                {
                    var restored = snapshot.WithIndexState(false,
                        snapshot.HeroType, snapshot.Consignation);
                    if (!EnqueueHeroLifecycleSnapshot(restored))
                        DBShare.MainOutMessage(
                            $"[GameSoc] DB工具0100英雄恢复最终保存入队失败 idx={hero.Idx}");
                }
                else _heroLogicalCache.Remove(hero.Idx);
                return true;
            }
        }

        private bool TryRenameNativeDbToolAccount(byte[] oldAccount,
            byte[] newAccount)
        {
            if (oldAccount == null || oldAccount.Length == 0
                || newAccount == null || newAccount.Length == 0)
                return false;
            lock (_nativeAccountRenameLock)
            lock (_nativeSaveMutationLock)
            {
                NativeAccountRenameResult rename = null;
                if (!_nativeAccountStorage.TryRenameAccount(oldAccount,
                        newAccount, () =>
                        {
                            rename = _playRecordService.RenameNativeAccount(
                                oldAccount, newAccount);
                            return rename?.Success == true;
                        }))
                    return false;
                foreach (var index in rename.CharacterIndices)
                {
                    AdvanceNativeSaveGeneration(index, tombstone: false);
                    _humanLogicalCache.Remove(index);
                }
                return true;
            }
        }

        private void ProcessNativeDbToolHumanRead(TServerInfo sender,
            LegacyDbServerFrame frame)
        {
            if (!NativeDbToolProtocol.TryDecodeHumanRead(frame,
                    out var request, out _))
                return;
            var response = NativeDbToolProtocol.CreateReadFailure(request);
            try
            {
                if (_playRecordService.TryGetNativeCharacterByName(
                        request.NameBytes, out var character)
                    && character != null)
                {
                    var persistence = _humanLogicalCache.GetOrLoad(
                        character.Idx,
                        () => LoadNativeHumanPersistence(character.Idx));
                    if (persistence != null)
                    {
                        var account = character.PTIDBytes;
                        if (account == null || account.Length == 0)
                            account = LegacyGbkText.Encode(persistence.Account);
                        if (NativeDbToolProtocol.TryCreateReadSuccess(request,
                                account, persistence.DataBlob,
                                persistence.ScriptDataBlob,
                                out var success, out _))
                            response = success;
                    }
                }
            }
            catch (Exception)
            {
                // The original tool reports lookup/load failures as result zero.
            }
            TrySendNativeResponse(sender, response, "DB工具0102人物读取");
        }

        private void ProcessNativeDbToolHumanWrite(TServerInfo sender,
            LegacyDbServerFrame frame)
        {
            if (!NativeDbToolProtocol.TryDecodeHumanWrite(frame,
                    out var request, out _))
                return;
            var result = 0;
            try
            {
                if (NativeDbToolProtocol.TryCreateHumanWritePersistence(
                        request, out var persistence, out var decoded,
                        out var characterNameBytes, out _))
                {
                    if (!_playRecordService.TryGetNativeCharacterByName(
                            characterNameBytes, out var character)
                        || character == null)
                    {
                        var index = _playRecordService.CreateCharacter(
                            persistence.Account, persistence.CharacterName,
                            decoded.Data.btJob, decoded.Data.btSex,
                            decoded.Data.btHair, decoded.Data.Abil.Level);
                        if (index > 0)
                        {
                            ReviveNativeSaveIndex(index);
                            _playRecordService.RegisterNativeCharacterName(
                                characterNameBytes);
                            _playDataService.RegisterNativeIndex(index,
                                persistence.CharacterName);
                        }
                        _playRecordService.TryGetNativeCharacterByName(
                            characterNameBytes, out character);
                    }

                    if (character == null)
                        result = 6;
                    else if (character.IsSelect)
                        result = 8;
                    else if (_humanLogicalCache.TryStage(character.Idx,
                                 persistence,
                                 staged => EnqueueNativeSave(
                                     new NativeSaveWorkItem(
                                         character.Idx, staged))))
                        result = 1;
                }
            }
            catch (Exception)
            {
                // The original tool reports parse/load/save queue failures as zero.
            }
            TrySendNativeResponse(sender,
                NativeDbToolProtocol.CreateWriteResponse(request, result),
                "DB工具0101人物写入");
        }

        private void ProcessNativeDbToolHeroRead(TServerInfo sender,
            LegacyDbServerFrame frame)
        {
            if (!NativeDbToolProtocol.TryDecodeHeroRead(frame,
                    out var request, out _))
                return;
            var response = NativeDbToolProtocol.CreateReadFailure(request);
            try
            {
                if (_heroRecordService.TryGetNativeHeroByName(
                        request.NameBytes, out var hero)
                    && hero != null)
                {
                    var snapshot = _heroLogicalCache.ReadOrLoad(hero.Idx,
                        () => LoadNativeDbToolHeroSnapshot(
                            hero, request.NameBytes));
                    if (snapshot != null)
                    {
                        var master = hero.MasterNameBytes;
                        if (master == null || master.Length == 0)
                            master = LegacyGbkText.Encode(snapshot.MasterName);
                        if (NativeDbToolProtocol.TryCreateReadSuccess(request,
                                master, snapshot.Data, snapshot.DynamicData,
                                out var success, out _))
                            response = success;
                    }
                }
            }
            catch (Exception)
            {
                // The original tool reports lookup/load failures as result zero.
            }
            TrySendNativeResponse(sender, response, "DB工具0104英雄读取");
        }

        private void ProcessNativeDbToolHeroWrite(TServerInfo sender,
            LegacyDbServerFrame frame)
        {
            if (!NativeDbToolProtocol.TryDecodeHeroWrite(frame,
                    out var request, out _))
                return;
            var result = 0;
            try
            {
                if (NativeDbToolProtocol.TryCreateHeroWriteData(
                        request, out var writeData, out _))
                {
                    lock (_heroCreateLock)
                    {
                        if (!_heroRecordService.TryGetNativeHeroByName(
                                writeData.HeroNameBytes, out var hero)
                            || hero == null)
                            TryCreateNativeDbToolHeroIndex(
                                request, writeData, out hero);
                        if (hero != null)
                        {
                            var record = writeData.Record;
                            var state = _heroSaveState.SnapshotForSave(
                                hero.Idx, hero.IsDelete, hero.HeroType,
                                hero.Consignation, 0);
                            var snapshot = new NativeHeroLogicalSnapshot(
                                hero.Idx, record.MasterName, record.HeroName,
                                writeData.RecordBytes, writeData.Data,
                                writeData.DynamicData, state.IsDelete,
                                state.HeroType, state.Consignation, hero.Job,
                                record.Level, record.IndexExp, record.Sex,
                                record.IndexForceLv, record.IndexForceExp,
                                record.IndexSfLevel);
                            if (EnqueueHeroExactSnapshot(snapshot))
                                result = 1;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // The original tool reports parse/load/save queue failures as zero.
            }
            TrySendNativeResponse(sender,
                NativeDbToolProtocol.CreateWriteResponse(request, result),
                "DB工具0103英雄写入");
        }

        private bool TryCreateNativeDbToolHeroIndex(
            NativeDbToolWriteRequest request,
            NativeDbToolHeroWriteData writeData, out HeroIndexInfo hero)
        {
            hero = null;
            var record = writeData.Record;
            var code = record.Sex * 3 + record.Job + 1;
            if (!_sensitiveWordFilter.ValidateNativeHeroName(record.HeroName)
                || request.Option is < 1 or > 2 || code is < 1 or > 6
                || _playRecordService.Index(record.HeroName) >= 0
                || _heroRecordService.IsHeroNameExists(record.HeroName))
                return false;

            var heroes = _heroRecordService.QueryHeroesByMaster(
                record.MasterName);
            var activeCount = 0;
            foreach (var existing in heroes)
            {
                if (existing == null || existing.IsDelete) continue;
                if (existing.Consignation == 0) activeCount++;
                if (existing.HeroType == request.Option) activeCount = 2;
            }
            if (activeCount >= 2) return false;

            var index = _heroRecordService.CreateHero(record.MasterName,
                record.HeroName, request.Option, record.Job, record.Sex, 0);
            if (index <= 0) return false;
            ReviveHeroSaveIndex(index);
            _heroDataService.RegisterNativeIndex(index);
            if (_heroRecordService.TryGetNativeHeroByName(
                    writeData.HeroNameBytes, out hero)
                && hero != null)
                return true;
            if (!HardDeleteNativeHeroIndex(index))
            {
                DBShare.MainOutMessage(
                    $"[GameSoc] DB工具0103创建补偿失败 idx={index}");
            }
            hero = null;
            return false;
        }

        private NativeHeroLogicalSnapshot LoadNativeDbToolHeroSnapshot(
            HeroIndexInfo hero, byte[] requestedName)
        {
            if (hero == null || hero.Idx <= 0) return null;
            var blob = _heroDataService.LoadBlob(hero.Idx);
            if (blob.data == null || blob.dynData == null
                || blob.data.Length != NativeHeroDbFrameCodec.HeroRecordSize
                && blob.data.Length != NativeHeroBlobCodec.ThreeHeroRecordSize
                || !TryFindNativeHeroRecord(blob.data, requestedName,
                    out var selectedRecord)
                || !NativeHeroDbFrameCodec.TryCreateRecord(selectedRecord,
                    out var record, out _))
                return null;
            return new NativeHeroLogicalSnapshot(hero.Idx,
                hero.MasterName, hero.HeroName, selectedRecord, blob.data,
                blob.dynData, hero.IsDelete, hero.HeroType,
                hero.Consignation, hero.Job, hero.Level,
                unchecked((uint)hero.Exp), hero.Sex, record.IndexForceLv,
                record.IndexForceExp, record.IndexSfLevel);
        }

        private static bool TryFindNativeHeroRecord(byte[] data,
            byte[] requestedName, out byte[] record)
        {
            record = null;
            if (data == null || requestedName == null
                || data.Length != NativeHeroDbFrameCodec.HeroRecordSize
                && data.Length != NativeHeroBlobCodec.ThreeHeroRecordSize)
                return false;
            var requestedKey = NativeForceLevelProtocol
                .NormalizeCharacterNameKey(requestedName);
            for (var offset = 0; offset < data.Length;
                 offset += NativeHeroDbFrameCodec.HeroRecordSize)
            {
                var lengthOffset = offset
                                   + NativeHeroDbFrameCodec.HeroNameOffset;
                var length = data[lengthOffset];
                if (length > 15) continue;
                var name = data.AsSpan(lengthOffset + 1, length).ToArray();
                if (NativeForceLevelProtocol.NormalizeCharacterNameKey(name)
                    != requestedKey)
                    continue;
                record = data.AsSpan(offset,
                    NativeHeroDbFrameCodec.HeroRecordSize).ToArray();
                return true;
            }
            return false;
        }

        private void ProcessNativeSessionPlayState(TServerInfo sender,
            LegacyDbServerFrame frame)
        {
            if (!NativeSessionControlProtocol.TryDecodePlayState(frame,
                    out var request, out _)
                || !_playRecordService.TryGetNativeCharacterByUserId(
                    request.UserId, out var character))
                return;
            _loginSvrService.SetNativeAccountPlayState(
                character.PTID, request.State == 1);
            TrySendNativeResponse(sender,
                NativeSessionControlProtocol.CreatePlayStateResponse(request),
                "019E会话状态");
        }

        private static void TrySendNativeResponse(TServerInfo sender,
            LegacyDbServerFrame response, string context)
        {
            if (!LegacyDbServerFrameCodec.TryEncode(response, out var wire,
                    out var error, NativeDbServerProtocol.MaximumFrameLength))
            {
                DBShare.MainOutMessage(
                    $"[GameSoc] 原生{context}响应编码失败: {error}");
                return;
            }
            try { SendAll(sender.Socket, wire); }
            catch (Exception ex) when (ex is SocketException
                                       || ex is ObjectDisposedException)
            {
                DBShare.MainOutMessage(
                    $"[GameSoc] 原生{context}响应发送失败: {ex.Message}");
            }
        }

        private void ProcessNativeSaveHuman(LegacyDbServerFrame frame)
        {
            if (!NativeDbServerProtocol.TryDecodeSaveHuman(
                    frame, out var request, out var frameError))
            {
                DBShare.MainOutMessage("[GameSoc] 原生人物保存帧拒绝: " + frameError);
                return;
            }
            if (!NativeDbServerProtocol.TryCreateSavePersistenceData(
                    request, out var persistence, out var persistenceError))
            {
                DBShare.MainOutMessage(
                    "[GameSoc] 原生人物保存数据拒绝: " + persistenceError);
                return;
            }

            var characterName = persistence.CharacterName;

            try
            {
                var index = _playDataService.Index(request.CharacterName);
                if (index < 0)
                {
                    DBShare.MainOutMessage(
                        $"[GameSoc] 原生人物保存找不到角色 chr={characterName}");
                    return;
                }

                var staged = _humanLogicalCache.TryStage(index, persistence,
                    staged => EnqueueNativeSave(
                        new NativeSaveWorkItem(index, staged)));
                if (staged && NativeDbServerProtocol.TryExtractSwitchLoginExtension(
                        request, out var extension))
                {
                    Volatile.Read(ref _nativeSwitchHandoffStore)(
                        request.Account, request.CharacterName, extension);
                }
            }
            catch (Exception ex)
            {
                DBShare.MainOutMessage(
                    $"[GameSoc] 原生人物保存异常 chr={characterName}: {ex.Message}");
            }
        }

        private bool EnqueueNativeSave(NativeSaveWorkItem workItem)
        {
            if (workItem == null || workItem.Index <= 0) return false;
            lock (_nativeSaveQueueLock)
            {
                if (_nativeSaveStopping)
                {
                    DBShare.MainOutMessage(
                        $"[GameSoc] 原生人物保存停机期间拒绝 idx={workItem.Index}");
                    return false;
                }
                var index = workItem.Index;
                if (_nativeSaveTombstones.Contains(index)) return false;
                workItem.Generation = GetNativeSaveGeneration(index);
                if (_nativeSavePending.TryGetValue(index, out var pending))
                {
                    pending.ReplaceWith(workItem);
                    return true;
                }

                _nativeSavePending.Add(index, workItem);
                _nativeSaveOrder.Enqueue(index);
                Monitor.Pulse(_nativeSaveQueueLock);
                return true;
            }
        }

        private long GetNativeSaveGeneration(int index) =>
            _nativeSaveGenerations.TryGetValue(index, out var generation)
                ? generation : 0;

        private bool IsNativeSaveWorkCurrent(NativeSaveWorkItem item)
        {
            lock (_nativeSaveQueueLock)
                return item != null
                       && !_nativeSaveTombstones.Contains(item.Index)
                       && item.Generation == GetNativeSaveGeneration(item.Index);
        }

        private void AdvanceNativeSaveGeneration(int index, bool tombstone)
        {
            if (index <= 0) return;
            lock (_nativeSaveQueueLock)
            {
                var current = GetNativeSaveGeneration(index);
                _nativeSaveGenerations[index] = current == long.MaxValue
                    ? 0 : current + 1;
                if (tombstone) _nativeSaveTombstones.Add(index);
                _nativeSavePending.Remove(index);
            }
        }

        private void ReviveNativeSaveIndex(int index)
        {
            if (index <= 0) return;
            lock (_nativeSaveQueueLock)
            {
                var current = GetNativeSaveGeneration(index);
                _nativeSaveGenerations[index] = current == long.MaxValue
                    ? 0 : current + 1;
                _nativeSaveTombstones.Remove(index);
            }
        }

        private void ProcessNativeSaveQueue()
        {
            List<NativeSaveWorkItem> batch = null;
            var batchIndex = 0;
            while (true)
            {
                if (batch == null || batchIndex >= batch.Count)
                {
                    lock (_nativeSaveQueueLock)
                    {
                        while (_nativeSaveOrder.Count == 0
                               && !_nativeSaveStopping)
                            Monitor.Wait(_nativeSaveQueueLock);
                        if (_nativeSaveOrder.Count == 0 && _nativeSaveStopping)
                            return;
                        batch = new List<NativeSaveWorkItem>(
                            _nativeSaveOrder.Count);
                        while (_nativeSaveOrder.Count != 0)
                        {
                            var index = _nativeSaveOrder.Dequeue();
                            if (!_nativeSavePending.Remove(index, out var item))
                                continue;
                            batch.Add(item);
                        }
                    }
                    batchIndex = 0;
                }

                var processed = 0;
                var cycleLimit = Volatile.Read(ref _nativeSaveStopping)
                    ? 200 : 100;
                while (batchIndex < batch.Count && processed < cycleLimit)
                {
                    var workItem = batch[batchIndex];
                    if (PersistNativeSave(workItem))
                    {
                        batchIndex++;
                        processed++;
                        continue;
                    }
                    workItem.RetryCount++;
                    if (workItem.RetryCount == 11)
                        DBShare.MainOutMessage(
                            $"[GameSoc] 原生人物保存第11次失败 idx={workItem.Index}");
                    if (workItem.RetryCount < 20) break;
                    DBShare.MainOutMessage(
                        $"[GameSoc] 原生人物保存20次失败后丢弃 idx={workItem.Index}");
                    batchIndex++;
                    processed++;
                }
                Thread.Sleep(5);
            }
        }

        private bool PersistNativeSave(NativeSaveWorkItem workItem)
        {
            lock (_nativeSaveMutationLock)
            {
                if (!IsNativeSaveWorkCurrent(workItem)) return true;
                try
                {
                    if (workItem.RestoreDeleteState
                        && !_playRecordService.PersistNativeCharacterRestore(
                            workItem.Index))
                    {
                        DBShare.MainOutMessage(
                            $"[GameSoc] 原生人物恢复写入失败 idx={workItem.Index}");
                        return false;
                    }
                    var persistence = workItem.Persistence;
                    if (persistence != null
                        && !_playRecordService.UpdateNativeSaveIndex(
                            workItem.Index, persistence))
                    {
                        DBShare.MainOutMessage(
                            $"[GameSoc] 原生人物保存索引更新失败 chr={persistence.CharacterName}");
                        return false;
                    }
                    else if (persistence != null
                             && !_playDataService.SaveNativeBlobExact(
                                 workItem.Index, persistence))
                    {
                        DBShare.MainOutMessage(
                            $"[GameSoc] 原生人物保存写入失败 chr={persistence.CharacterName}");
                        return false;
                    }
                    else if (persistence != null)
                        _loginSvrService.SetSessionSaveRcd(persistence.Account);

                    if (persistence == null && workItem.ForceLevel.HasValue
                        && !_playRecordService.PersistNativeForceLevel(
                            workItem.Index, workItem.ForceLevel.Value))
                    {
                        DBShare.MainOutMessage(
                            $"[GameSoc] 原生人物ForceLv写入失败 idx={workItem.Index}");
                        return false;
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    DBShare.MainOutMessage(
                        $"[GameSoc] 原生人物保存异常 idx={workItem.Index}: " +
                        ex.Message);
                    return false;
                }
            }
        }

        private bool ProcessNativeHeroFrame(TServerInfo serverInfo,
            LegacyDbServerFrame frame, ushort command, out string error)
        {
            error = string.Empty;
            if (!LegacyDbServerFrameCodec.TryEncode(frame, out var requestFrame,
                    out error, NativeDbServerProtocol.MaximumFrameLength))
                return false;

            byte[] responseFrame;
            switch (command)
            {
                case NativeHeroDbFrameCodec.LoadCommand:
                    if (!NativeHeroDbFrameCodec.TryDecodeLoadRequest(
                            requestFrame, out var loadRequest, out error))
                        return RejectNativeHeroRequest(command, error);
                    if (!NativeHeroDbFrameCodec.TryEncodeLoadResponse(
                            LoadHeroRcd(loadRequest), out responseFrame, out error))
                        return false;
                    break;
                case NativeHeroDbFrameCodec.SaveCommand:
                    SaveHeroRcd(requestFrame);
                    // The original asynchronous hero-save path has no connection
                    // argument and therefore sends no acknowledgement.
                    return true;
                case NativeHeroDbFrameCodec.CreateCommand:
                    if (!NativeHeroDbFrameCodec.TryDecodeCreateRequest(
                            requestFrame, out var createRequest, out error))
                        return RejectNativeHeroRequest(command, error);
                    var createResponse = new NativeHeroCreateResponse
                    {
                        HeroType = createRequest.HeroType,
                        Result = CreateHeroRcd(createRequest),
                        MasterName = createRequest.MasterName,
                        HeroName = createRequest.HeroName
                    };
                    if (!NativeHeroDbFrameCodec.TryEncodeCreateResponse(
                            createResponse, out responseFrame, out error))
                        return false;
                    break;
                case NativeHeroDbFrameCodec.DeleteCommand:
                    if (!NativeHeroDbFrameCodec.TryDecodeDeleteRequest(
                            requestFrame, out var deleteRequest, out error))
                        return RejectNativeHeroRequest(command, error);
                    int deleteResult;
                    lock (_heroCreateLock)
                        deleteResult = DeleteHeroRcd(deleteRequest);
                    var deleteResponse = new NativeHeroDeleteResponse
                    {
                        Result = deleteResult,
                        Account = deleteRequest.Account,
                        MasterName = deleteRequest.MasterName,
                        HeroName = deleteRequest.HeroName
                    };
                    if (!NativeHeroDbFrameCodec.TryEncodeDeleteResponse(
                            deleteResponse, out responseFrame, out error))
                        return false;
                    break;
                case NativeHeroDbFrameCodec.RenameCommand:
                    if (!NativeHeroDbFrameCodec.TryDecodeRenameRequest(
                            requestFrame, out var renameRequest, out error))
                        return RejectNativeHeroRequest(command, error);
                    ushort renameResult;
                    lock (_heroCreateLock)
                        renameResult = RenameHeroRcd(renameRequest);
                    var renameResponse = new NativeHeroRenameResponse
                    {
                        Result = renameResult,
                        Code = renameRequest.Code,
                        MasterName = renameRequest.MasterName,
                        NewHeroName = renameRequest.NewHeroName
                    };
                    if (!NativeHeroDbFrameCodec.TryEncodeRenameResponse(
                            renameResponse, out responseFrame, out error))
                        return false;
                    break;
                case NativeHeroDbFrameCodec.ConsignedListCommand:
                    if (!NativeHeroDbFrameCodec.TryDecodeConsignedListRequest(
                            requestFrame, out var listRequest, out error))
                        return RejectNativeHeroRequest(command, error);
                    if (!NativeHeroDbFrameCodec.TryEncodeConsignedListResponse(
                            CreateConsignedListResponse(listRequest),
                            out responseFrame, out error))
                        return false;
                    break;
                case NativeHeroDbFrameCodec.RestoreConsignedCommand:
                    if (!NativeHeroDbFrameCodec.TryDecodeRestoreConsignedRequest(
                            requestFrame, out var restoreRequest, out error))
                        return RejectNativeHeroRequest(command, error);
                    if (!NativeHeroDbFrameCodec.TryEncodeRestoreConsignedResponse(
                            RestoreConsignedHero(restoreRequest),
                            out responseFrame, out error))
                        return false;
                    break;
                case NativeHeroDbFrameCodec.BuildThreeSlotCommand:
                    if (!NativeHeroDbFrameCodec.TryDecodeBuildThreeSlotRequest(
                            requestFrame, out var buildRequest, out error))
                        return RejectNativeHeroRequest(command, error);
                    if (!NativeHeroDbFrameCodec.TryEncodeBuildThreeSlotResponse(
                            BuildThreeSlotHero(buildRequest),
                            out responseFrame, out error))
                        return false;
                    break;
                default:
                    error = $"unsupported native hero command 0x{command:X4}";
                    return false;
            }

            try
            {
                SendAll(serverInfo.Socket, responseFrame);
                return true;
            }
            catch (SocketException ex)
            {
                error = $"native hero response send failed: {ex.Message}";
                return false;
            }
        }

        private static bool RejectNativeHeroRequest(ushort command, string error)
        {
            DBShare.MainOutMessage(
                $"[GameSoc] 原生英雄指令 0x{command:X4} 拒绝: {error}");
            return true;
        }

        public bool TrySendNativeHuman(string account, string characterName,
            NativeHumanSessionContext sessionContext)
        {
            var targets = new List<TServerInfo>();
            lock (_serverListLock)
            {
                foreach (var server in _serverList)
                {
                    if (IsNativeHumanFanoutTarget(server)) targets.Add(server);
                }
            }
            if (targets.Count == 0)
            {
                DBShare.MainOutMessage("[GameSoc] 没有原生6000 GameSvr连接");
                return false;
            }

            var index = _playDataService.Index(characterName);
            var persistence = index < 0
                ? null
                : _humanLogicalCache.GetOrLoad(index,
                    () => LoadNativeHumanPersistence(index));
            if (persistence == null
                || !NativeHumanLogicalCache.TryExtractRaw(persistence,
                    out var nativeData, out var nativeScriptData))
            {
                DBShare.MainOutMessage($"[GameSoc] 原生选角存档读取失败 chr={characterName}");
                return true;
            }
            if (!string.Equals(persistence.CharacterName, characterName,
                    StringComparison.Ordinal)
                || (!string.IsNullOrEmpty(persistence.Account)
                    && !string.Equals(persistence.Account, account,
                        StringComparison.Ordinal)))
            {
                DBShare.MainOutMessage(
                    $"[GameSoc] 原生选角存档归属不匹配 account={account} chr={characterName}");
                return true;
            }
            if (TryConsumeAwardPlayer(account, characterName))
                sessionContext.AuthFlags75 |= NativeDbServerProtocol.AwardPlayerFlag;

            foreach (var target in targets)
            {
                // suffix+0x40 = DB 时钟基准。sub_59DC1C 在 fan-out 循环内
                // 逐个调用 sub_5986CC，因此每个目标都在编码紧前重新求 Now()。
                sessionContext.DbClockBase = HUtil32.DateTimeToDouble(DateTime.Now);

                if (!NativeDbServerProtocol.TryCreateLoadHumanFrame(
                        account, characterName, nativeData, nativeScriptData,
                        sessionContext,
                        out var response, out var error)
                    || !LegacyDbServerFrameCodec.TryEncode(
                        response, out var wire, out error))
                {
                    DBShare.MainOutMessage(
                        $"[GameSoc] 原生选角响应编码失败 chr={characterName}: {error}");
                    continue;
                }

                var socket = target.Socket;
                if (socket == null) continue;
                try { SendAll(socket, wire); }
                catch (Exception ex) when (ex is SocketException
                                           || ex is ObjectDisposedException)
                {
                    DBShare.MainOutMessage(
                        $"[GameSoc] 原生选角响应发送失败 chr={characterName}: {ex.Message}");
                    RemoveNativeHumanFanoutTarget(target, socket);
                }
            }

            // 原版 sub_59DC1C 的返回值只表示 fan-out 列表非空；每目标的
            // 记录构造或发送是否成功都不改变该结果。
            return true;
        }

        private static bool IsNativeHumanFanoutTarget(TServerInfo server)
        {
            return server?.Socket != null
                   && server.WireModeDetector.Mode
                   == DbServerWireMode.NativeType12;
        }

        private void RemoveNativeHumanFanoutTarget(TServerInfo target,
            Socket socket)
        {
            lock (_serverListLock)
            {
                for (var i = 0; i < _serverList.Count; i++)
                {
                    if (!ReferenceEquals(_serverList[i], target)
                        || !ReferenceEquals(_serverList[i].Socket, socket))
                        continue;
                    _serverList.RemoveAt(i);
                    break;
                }
            }

            try { socket.Close(); }
            catch (ObjectDisposedException) { }
        }

        private NativeSavePersistenceData LoadNativeHumanPersistence(int index)
        {
            THumDataInfo human = null;
            if (_playDataService.Get(index, ref human) < 0
                || human?.Data == null)
                return null;
            if (human.NativeData?.Length != NativeHumanDataCodec.DataRecordSize
                && !NativeHumanDataCodec.TryEncode(human, out _, out _,
                    out var encodeError))
            {
                DBShare.MainOutMessage(
                    $"[GameSoc] 原生人物快照编码失败 idx={index}: {encodeError}");
                return null;
            }
            if (!NativeHumanLogicalCache.TryCreatePersistence(
                    human.Data.sAccount, human.Data.sCharName,
                    human.NativeData, human.NativeScriptData,
                    out var persistence, out var error))
            {
                DBShare.MainOutMessage(
                    $"[GameSoc] 原生人物快照创建失败 idx={index}: {error}");
                return null;
            }
            return persistence;
        }

        private static bool TryConsumeAwardPlayer(string ptid, string chrName)
        {
            try
            {
                using var conn = new MySqlConnection(DBShare.DBConnection);
                conn.Open();
                using (var session = new MySqlCommand(
                           "SET WAIT_TIMEOUT = 2073600;", conn))
                    session.ExecuteNonQuery();
                // 0x5A7B84 `Select Idx from awardplayers where PTID="%s" and
                // HumName="%s" and Status=1`（rc=-1 len=74）。
                // 三处修正：
                //  1) 库名 gamedata. → mir3.。原版整条链路先发 `use mir3;`
                //     (0x5BAD84 rc=-1 len=9) 再执行无前缀的语句，故真实库是 mir3。
                //     真库已核：mir3.awardplayers 存在，gamedata 里**没有**这张表
                //     （离线扫 datadir 的 .frm + 在线 SHOW TABLES 双证），
                //     所以此前指向 gamedata 的写入根本落不到原版读的那张表上。
                //  2) 去掉 LIMIT 1：原版无 LIMIT。PTID 在真库是 UNIQUE 索引
                //     （SHOW COLUMNS 显示 PTID char(20) UNI），本就至多一行，
                //     LIMIT 只是掩盖「若出现多行则数据已异常」这一事实。
                using var sel = new MySqlCommand(
                    "Select Idx from mir3.awardplayers "
                    + "where PTID=@p and HumName=@h and Status=1", conn);
                sel.Parameters.AddWithValue("@p", ptid);
                sel.Parameters.AddWithValue("@h", chrName);
                var value = sel.ExecuteScalar();
                if (value == null || value == DBNull.Value) return false;

                // 0x5ACDB8 `Update awardplayers set Status=2 where Idx=%d;`
                // （rc=-1 len=46）。
                //  3) 谓词只按 Idx，**不带 `and Status=1`**。多出来的 Status=1 会
                //     改变幂等性：原版对已是 Status=2 的行仍然「更新成功」
                //     （影响 0 行但语句成立），而加了 Status=1 之后重复领取会
                //     走进 false 分支。这是行为差异，不是防御性改进。
                using var upd = new MySqlCommand(
                    "Update mir3.awardplayers set Status=2 where Idx=@i;", conn);
                upd.Parameters.AddWithValue("@i", Convert.ToInt32(value));
                upd.ExecuteNonQuery();
                // 原版 0x5ACDB8 之后不检查影响行数（无 dec eax/jne 之类的门），
                // 拿到 Idx 即视为领取成功。故这里返回 true 而不是 rows>0。
                return true;
            }
            catch (Exception ex)
            {
                DBShare.MainOutMessage($"[AwardPlayer] 领取状态更新错误: {ex.Message}");
                return false;
            }
        }

        // ===================== 包处理 =====================

        private void ProcessServerPacket(TServerInfo serverInfo, byte[] data)
        {
            if (data.Length == 0) return;

            int nQueryId = 0;
            try
            {
                var requestPacket = Packets.ToPacket<RequestServerPacket>(data);
                if (requestPacket == null) return;

                nQueryId = requestPacket.QueryId;
                var packet = ProtoBufDecoder.DeSerialize<ServerMessagePacket>(
                    EDcode.DecodeBuff(requestPacket.Message));
                int packetLen = requestPacket.Message.Length + requestPacket.Packet.Length + 6;

                // 校验 Key
                if (packetLen >= Grobal2.DEFBLOCKSIZE && nQueryId > 0)
                {
                    var expectedCheckKey = HUtil32.MakeLong(nQueryId ^ 170, packetLen);
                    int ckVal = BitConverter.ToInt32(requestPacket.CheckKey);
                    FileLog($"[CheckKey] qid={nQueryId} expected={expectedCheckKey} actual={ckVal} pktLen={packetLen} msg={requestPacket.Message.Length} pkt={requestPacket.Packet.Length}");
                    if (ckVal == expectedCheckKey)
                    {
                        ProcessGameServerMsg(nQueryId, packet, requestPacket.Packet, serverInfo.Socket);
                        return;
                    }
                    FileLog($"[CheckKey] MISMATCH qid={nQueryId} expected={expectedCheckKey} actual={ckVal} — closing connection");
                    serverInfo.Socket.Close();
                    return;
                }
            }
            catch (Exception ex)
            {
                DBShare.MainOutMessage($"[GameSoc] 包解析错误: {ex.Message}");
            }

            // 失败响应
            var responsePack = new RequestServerPacket { QueryId = nQueryId };
            var messagePacket = new ServerMessagePacket(Grobal2.DBR_FAIL, 0, 0, 0, 0);
            responsePack.Message = EDcode.EncodeBuffer(ProtoBufDecoder.Serialize(messagePacket));
            SendRequest(serverInfo.Socket, responsePack);
        }

        private void ProcessGameServerMsg(int nQueryId, ServerMessagePacket packet, byte[] sData, Socket socket)
        {
            sData = EDcode.DecodeBuff(sData);

            switch (packet.Ident)
            {
                case Grobal2.DB_LOADHUMANRCD:
                    LoadHumanRcd(nQueryId, sData, socket);
                    break;
                case Grobal2.DB_SAVEHUMANRCD:
                    SaveHumanRcd(nQueryId, packet.Recog, sData, socket);
                    break;
                case Grobal2.DB_SAVEHUMANRCDEX:
                    SaveHumanRcdEx(nQueryId, sData, packet.Recog, socket);
                    break;
                case NativeHeroDbFrameCodec.LoadCommand:
                    LoadHeroRcd(nQueryId, sData, socket);
                    break;
                case NativeHeroDbFrameCodec.SaveCommand:
                    SendHeroSaveResponse(nQueryId, SaveHeroRcd(sData), socket);
                    break;
                case NativeHeroDbFrameCodec.CreateCommand:
                    CreateHeroRcd(nQueryId, sData, socket);
                    break;
                case NativeHeroDbFrameCodec.DeleteCommand:
                    DeleteHeroRcd(nQueryId, sData, socket);
                    break;
                case NativeHeroDbFrameCodec.RenameCommand:
                    RenameHeroRcd(nQueryId, sData, socket);
                    break;
                case NativeHeroDbFrameCodec.ConsignedListCommand:
                    ListConsignedHeroes(nQueryId, sData, socket);
                    break;
                case NativeHeroDbFrameCodec.RestoreConsignedCommand:
                    RestoreConsignedHero(nQueryId, sData, socket);
                    break;
                case NativeHeroDbFrameCodec.BuildThreeSlotCommand:
                    BuildThreeSlotHero(nQueryId, sData, socket);
                    break;

                default:
                    SendFail(nQueryId, socket);
                    break;
            }
        }

        // ===================== 加载角色存档 =====================

        [Conditional("DBSVR_PROTOCOL_TRACE")]
        private static void FileLog(string msg)
        {
            Debug.WriteLine(msg);
        }

        private void LoadHumanRcd(int queryId, byte[] data, Socket socket)
        {
            var loadHumanPacket = ProtoBufDecoder.DeSerialize<LoadHumDataPacket>(data);
            if (loadHumanPacket == null)
            {
                FileLog("[LoadHuman] deserialize null");
                SendLoadResult(queryId, -3, socket);
                return;
            }

            FileLog($"[LoadHuman] acc={loadHumanPacket.sAccount} chr={loadHumanPacket.sChrName} sess={loadHumanPacket.nSessionID} ip={loadHumanPacket.sUserAddr}");

            THumDataInfo HumanRCD = null;
            bool boFoundSession = false;
            int nCheckCode = -1;
            var internalRequest = IsInternalRecordRequest(loadHumanPacket);

            // 会话校验
            if (internalRequest)
            {
                // The native FrontEngine uses this exact sentinel when a trusted
                // GameSvr changes an offline character's gold balance.
                boFoundSession = true;
                nCheckCode = 1;
            }
            else if (!string.IsNullOrEmpty(loadHumanPacket.sAccount) && !string.IsNullOrEmpty(loadHumanPacket.sChrName))
            {
                nCheckCode = _loginSvrService.CheckSessionLoadRcd(
                    loadHumanPacket.sAccount, loadHumanPacket.sUserAddr,
                    loadHumanPacket.nSessionID, ref boFoundSession);

                FileLog($"[LoadHuman] sessCheck: nCheckCode={nCheckCode} boFound={boFoundSession}");

                if (!boFoundSession)
                {
                    FileLog($"[LoadHuman] ILLEGAL: acc={loadHumanPacket.sAccount} ip={loadHumanPacket.sUserAddr} sess={loadHumanPacket.nSessionID}");
                    DBShare.MainOutMessage("[非法请求] 帐号: " + loadHumanPacket.sAccount +
                        " IP: " + loadHumanPacket.sUserAddr +
                        " 标识: " + loadHumanPacket.nSessionID);
                }
            }

            FileLog($"[LoadHuman] before load: nCheckCode={nCheckCode} boFound={boFoundSession}");

            if ((nCheckCode == 1) || boFoundSession)
            {
                int nIndex = _playDataService.Index(loadHumanPacket.sChrName);
                if (nIndex >= 0)
                {
                    if (_playDataService.Get(nIndex, ref HumanRCD) < 0)
                        nCheckCode = -2; // 数据损坏
                }
                else
                {
                    // Missing data is corruption, not a new level-1 character. Reconstructing
                    // a record here destroys level/equipment state and masks the real failure.
                    var recordIndex = _playRecordService.Index(loadHumanPacket.sChrName);
                    DBShare.MainOutMessage($"[LoadHuman] 角色存档缺失 chr={loadHumanPacket.sChrName} recordIdx={recordIndex}");
                    nCheckCode = -3;
                }
            }

            FileLog($"[LoadHuman] final: nCheckCode={nCheckCode} boFound={boFoundSession} HumanRCD={(HumanRCD!=null)}");

            var responsePack = new RequestServerPacket { QueryId = queryId };
            if (nCheckCode == 1)
            {
                var loadHumData = new LoadHumanRcdResponsePacket
                {
                    sChrName = EDcode.EncodeString(loadHumanPacket.sChrName),
                    HumDataInfo = HumanRCD
                };
                var msg = new ServerMessagePacket(Grobal2.DBR_LOADHUMANRCD, 1, 0, 0, 1);
                responsePack.Message = EDcode.EncodeBuffer(ProtoBufDecoder.Serialize(msg));
                SendRequest(socket, responsePack, loadHumData);
            }
            else
            {
                var msg = new ServerMessagePacket(Grobal2.DBR_LOADHUMANRCD, nCheckCode, 0, 0, 0);
                responsePack.Message = EDcode.EncodeBuffer(ProtoBufDecoder.Serialize(msg));
                SendRequest(socket, responsePack);
            }
        }

        internal static bool IsInternalRecordRequest(LoadHumDataPacket packet)
        {
            return packet != null && packet.sAccount == "1" && packet.sUserAddr == "1" &&
                   packet.nSessionID == 1 && !string.IsNullOrEmpty(packet.sChrName);
        }

        private void SendLoadResult(int queryId, int result, Socket socket)
        {
            var responsePack = new RequestServerPacket { QueryId = queryId };
            var msg = new ServerMessagePacket(Grobal2.DBR_LOADHUMANRCD, result, 0, 0, 0);
            responsePack.Message = EDcode.EncodeBuffer(ProtoBufDecoder.Serialize(msg));
            SendRequest(socket, responsePack);
        }

        // ===================== 保存角色存档 =====================

        private void SaveHumanRcd(int queryId, int nRecog, byte[] sMsg, Socket socket)
        {
            SaveHumDataPacket saveHumDataPacket;
            try
            {
                saveHumDataPacket = ProtoBufDecoder.DeSerialize<SaveHumDataPacket>(sMsg);
            }
            catch (Exception ex)
            {
                DBShare.MainOutMessage($"[SaveHumanRcd] 数据包解码失败: {ex.Message}");
                SendSaveResult(queryId, false, socket);
                return;
            }
            if (saveHumDataPacket == null)
            {
                Console.WriteLine("保存玩家数据出错.");
                SendSaveResult(queryId, false, socket);
                return;
            }

            var sChrName = saveHumDataPacket.sCharName;
            var humanRcd = saveHumDataPacket.HumDataInfo;
            bool boError = humanRcd == null;

            if (!boError)
            {
                try
                {
                    int nIndex = _playDataService.Index(sChrName);
                    if (nIndex < 0)
                    {
                        // 新角色
                        humanRcd.Header.sName = sChrName;
                        boError = !_playDataService.Add(ref humanRcd);
                        nIndex = _playDataService.Index(sChrName);
                    }

                    if (!boError && nIndex >= 0)
                    {
                        humanRcd.Header.sName = sChrName;
                        byte[] serialized = ProtoBufDecoder.Serialize(humanRcd);
                        boError = !_playDataService.SaveBlob(nIndex, serialized, Array.Empty<byte>());

                        // Blob must be durable before the index advertises the new character state.
                        if (!boError && humanRcd.Data != null)
                        {
                            boError = !_playRecordService.UpdateCharIndex(nIndex,
                                humanRcd.Data.Abil?.Level ?? 1,
                                humanRcd.Data.Abil?.Exp ?? 0,
                                humanRcd.Data.btJob,
                                humanRcd.Data.btSex,
                                humanRcd.Data.ForceLv,
                                humanRcd.Data.ForceExp,
                                humanRcd.Data.FightPoints,
                                humanRcd.Data.sfLevel);
                        }
                    }
                    else if (nIndex < 0)
                    {
                        boError = true;
                    }
                }
                catch (Exception ex)
                {
                    boError = true;
                    DBShare.MainOutMessage($"[SaveHumanRcd] 保存失败 name={sChrName}: {ex.Message}");
                }
            }

            if (!boError)
            {
                _loginSvrService.SetSessionSaveRcd(saveHumDataPacket.sAccount);
            }
            SendSaveResult(queryId, !boError, socket);
        }

        private void SendSaveResult(int queryId, bool success, Socket socket)
        {
            var responsePack = new RequestServerPacket { QueryId = queryId };
            var msg = new ServerMessagePacket(Grobal2.DBR_SAVEHUMANRCD, success ? 1 : 0, 0, 0, 0);
            responsePack.Message = EDcode.EncodeBuffer(ProtoBufDecoder.Serialize(msg));
            SendRequest(socket, responsePack);
        }

        private void SaveHumanRcdEx(int nQueryId, byte[] sMsg, int nRecog, Socket socket)
        {
            // The legacy Delphi DB_SAVEHUMANRCDEX path aliases the ordinary save.
            // Its THumSession list was allocated but never populated in any native source.
            SaveHumanRcd(nQueryId, nRecog, sMsg, socket);
        }

        // ===================== 原生英雄存档 =====================

        private void LoadHeroRcd(int queryId, byte[] frame, Socket socket)
        {
            if (!NativeHeroDbFrameCodec.TryDecodeLoadRequest(
                    frame, out var request, out var error))
            {
                DBShare.MainOutMessage($"[HeroLoad] REJECT qid={queryId}: {error}");
                SendHeroLoadResponse(queryId,
                    new NativeHeroLoadResponse { Status = 0 }, socket);
                return;
            }

            var response = LoadHeroRcd(request);
            SendHeroLoadResponse(queryId, response, socket);
        }

        private NativeHeroLoadResponse LoadHeroRcd(NativeHeroLoadRequest request)
        {
            ushort status = 0;
            try
            {
                if (_playRecordService.Index(request.MasterName) < 0)
                    return HeroLoadFailure(status, request.MasterName);
                var heroes = _heroRecordService.QueryHeroesByMaster(request.MasterName);
                status = 10;
                var hero = SelectHeroIndex(heroes, request);
                if (hero == null) return HeroLoadFailure(status, request.MasterName);

                status = 11;
                if (_heroDataService.Index(hero.Idx) < 0)
                    return HeroLoadFailure(status, request.MasterName);
                var blob = _heroDataService.LoadBlob(hero.Idx);
                if (blob.data == null || blob.dynData == null)
                    return HeroLoadFailure(status, request.MasterName);

                status = 12;
                NativeHeroDbFrameCodec.TryDecodeDynamicData(
                    blob.dynData, out var dynamicData, out _);
                dynamicData ??= new NativeHeroDynamicData(
                    Array.Empty<NativeHeroDynamicSection>());
                if (hero.Job == byte.MaxValue
                    && blob.data.Length != NativeHeroBlobCodec.ThreeHeroRecordSize)
                    return HeroLoadFailure(status, request.MasterName);

                status = 13;
                if (!NativeHeroBlobCodec.TrySelectDataRecord(blob.data,
                        request.HeroSlot, hero.Job == byte.MaxValue,
                        out var recordData, out _))
                    return HeroLoadFailure(status, request.MasterName);

                if (!NativeHeroDbFrameCodec.TryCreateRecord(
                        recordData, out var record, out _))
                    return HeroLoadFailure(status, request.MasterName);
                if (!string.Equals(record.MasterName, request.MasterName,
                        StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrEmpty(record.HeroName))
                    return HeroLoadFailure(status, request.MasterName);

                _heroAttachmentState.MarkLoaded(hero.Idx, request.HeroSlot);

                return new NativeHeroLoadResponse
                {
                    Status = 1,
                    HeroName = record.HeroName,
                    MasterName = record.MasterName,
                    Record = record,
                    DynamicData = dynamicData,
                    RawDynamicData = blob.dynData
                };
            }
            catch (Exception ex)
            {
                DBShare.MainOutMessage(
                    $"[HeroLoad] REJECT master={request.MasterName} status={status}: {ex.Message}");
                return HeroLoadFailure(status, request.MasterName);
            }
        }

        private void ProcessNativeHeroDetach(LegacyDbServerFrame frame)
        {
            if (!LegacyDbServerFrameCodec.TryEncode(frame, out var requestFrame,
                    out _, NativeDbServerProtocol.MaximumFrameLength)
                || !NativeHeroDbFrameCodec.TryDecodeDetachRequest(requestFrame,
                    out var request, out _))
                return;

            try
            {
                var heroes = _heroRecordService.QueryHeroesByMaster(
                    request.MasterName);
                heroes.AddRange(_heroRecordService.QueryDeletedHeroesByMaster(
                    request.MasterName));
                _heroAttachmentState.ClearForDetach(request.MasterName,
                    request.HeroKind == 1, request.Mode, heroes);
            }
            catch (Exception ex)
            {
                DBShare.MainOutMessage(
                    $"[HeroDetach] ignored master={request.MasterName}: {ex.Message}");
            }
        }

        private static HeroIndexInfo SelectHeroIndex(
            IList<HeroIndexInfo> heroes, NativeHeroLoadRequest request)
        {
            if (heroes == null || heroes.Count > 2) return null;
            var hasSpecialHero = false;
            foreach (var hero in heroes)
            {
                if (hero != null && !hero.IsDelete && hero.Job == byte.MaxValue)
                {
                    hasSpecialHero = true;
                    break;
                }
            }
            foreach (var hero in heroes)
            {
                if (hero == null || hero.IsDelete) continue;
                if (request.HeroKind == 1)
                {
                    if (hero.Job == byte.MaxValue) return hero;
                    continue;
                }
                if (hero.Job != byte.MaxValue
                    && (hasSpecialHero || hero.Consignation == 0))
                    return hero;
            }
            return null;
        }

        private bool SaveHeroRcd(byte[] frame)
        {
            if (!NativeHeroDbFrameCodec.TryDecodeSaveRequest(
                    frame, out var request, out var error))
            {
                DBShare.MainOutMessage($"[HeroSave] REJECT: {error}");
                return false;
            }
            try
            {
                lock (_heroCreateLock)
                {
                    var heroes = _heroRecordService.QueryHeroesByMaster(
                        request.MasterName);
                    heroes.AddRange(_heroRecordService.QueryDeletedHeroesByMaster(
                        request.MasterName));
                    HeroIndexInfo hero = null;
                    foreach (var candidate in heroes)
                    {
                        if (candidate != null
                            && string.Equals(candidate.HeroName, request.HeroName,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            hero = candidate;
                            break;
                        }
                    }
                    if (hero == null)
                    {
                        DBShare.MainOutMessage(
                            $"[HeroSave] REJECT master={request.Record.MasterName} hero={request.Record.HeroName}: index row missing");
                        return false;
                    }
                    var stickyState = _heroSaveState.SnapshotForSave(
                        hero.Idx, hero.IsDelete, hero.HeroType,
                        hero.Consignation, request.SaveMode);
                    if (_heroDataService.Index(hero.Idx) < 0)
                    {
                        DBShare.MainOutMessage(
                            $"[HeroSave] REJECT master={request.Record.MasterName} hero={request.Record.HeroName}: data row missing");
                        return false;
                    }
                    if (!TryStageHeroLogicalSnapshot(hero, request,
                            stickyState, out var logicalSnapshot, out error))
                    {
                        DBShare.MainOutMessage(
                            $"[HeroSave] REJECT hero={request.HeroName}: {error}");
                        return false;
                    }
                    var workItem = new NativeHeroSaveWorkItem(hero.Idx,
                        request.HeroName, logicalSnapshot.Record,
                        logicalSnapshot.Data, logicalSnapshot.DynamicData,
                        request.SaveMode,
                        request.Param1, request.Param2, hero.IsDelete,
                        hero.HeroType, hero.Consignation,
                        logicalSnapshot.IndexJob,
                        request.Record.IndexForceLv);
                    workItem.ApplyState(stickyState);
                    workItem.SetLogicalSnapshot(logicalSnapshot);
                    if (!EnqueueHeroSave(workItem)) return false;
                    _heroLogicalCache.Set(logicalSnapshot);
                    _heroDataService.SetNativeForceLevelOverride(
                        hero.Idx, request.Record.IndexForceLv);
                    return true;
                }
            }
            catch (Exception ex)
            {
                DBShare.MainOutMessage(
                    $"[HeroSave] REJECT hero={request.Record.HeroName}: {ex.Message}");
                return false;
            }
        }

        private bool TryStageHeroLogicalSnapshot(HeroIndexInfo hero,
            NativeHeroSaveRequest request, NativeHeroSaveState state,
            out NativeHeroLogicalSnapshot staged, out string error)
        {
            staged = null;
            error = string.Empty;
            if (!_heroLogicalCache.TryGet(hero.Idx, out var current))
            {
                var loaded = _heroDataService.LoadBlob(hero.Idx);
                if (loaded.data == null || loaded.dynData == null)
                {
                    error = "logical source Data could not be loaded";
                    return false;
                }
                if (!NativeHeroBlobCodec.TrySelectDataRecord(loaded.data,
                        request.Record.Job, hero.Job == byte.MaxValue,
                        out var selectedRecord, out error)
                    || !NativeHeroDbFrameCodec.TryCreateRecord(selectedRecord,
                        out var selected, out error))
                    return false;
                current = new NativeHeroLogicalSnapshot(hero.Idx,
                    hero.MasterName, hero.HeroName, selectedRecord, loaded.data,
                    loaded.dynData, hero.IsDelete, hero.HeroType,
                    hero.Consignation, hero.Job, hero.Level,
                    unchecked((uint)hero.Exp), hero.Sex,
                    selected.IndexForceLv, selected.IndexForceExp,
                    selected.IndexSfLevel);
            }

            var record = request.Record.ToArray();
            byte[] data;
            if (current.IndexJob == byte.MaxValue || hero.Job == byte.MaxValue)
            {
                if (!NativeHeroBlobCodec.TryMergeDataRecord(current.Data,
                        record, true, out data, out error))
                    return false;
                if (!NativeHeroBlobCodec.TryApplyIndexForceLevel(data,
                        request.Record.IndexForceLv, out data, out error))
                    return false;
            }
            else
                data = (byte[])record.Clone();
            var dynamicData = request.RawDynamicData ?? Array.Empty<byte>();
            staged = new NativeHeroLogicalSnapshot(hero.Idx,
                request.Record.MasterName, request.Record.HeroName, record,
                data, dynamicData, state.IsDelete, state.HeroType,
                state.Consignation,
                current.IndexJob == byte.MaxValue
                    ? byte.MaxValue : request.Record.Job,
                request.Record.Level, request.Record.IndexExp,
                request.Record.Sex, request.Record.IndexForceLv,
                request.Record.IndexForceExp, request.Record.IndexSfLevel);
            return true;
        }

        private void SendHeroSaveResponse(int queryId, bool success, Socket socket)
        {
            var response = new RequestServerPacket { QueryId = queryId };
            response.Message = EDcode.EncodeBuffer(ProtoBufDecoder.Serialize(
                new ServerMessagePacket(NativeHeroDbFrameCodec.SaveCommand,
                    success ? 1 : 0, 0, 0, 0)));
            SendRequest(socket, response);
        }

        private bool EnqueueHeroSave(NativeHeroSaveWorkItem workItem)
        {
            if (workItem == null || workItem.Index <= 0) return false;
            var key = string.IsNullOrEmpty(workItem.HeroName)
                ? "\0" + workItem.Index : workItem.HeroName;
            lock (_heroSaveQueueLock)
            {
                if (_heroSaveStopping)
                {
                    DBShare.MainOutMessage(
                        $"[HeroSave] rejected during stop hero={workItem.HeroName}");
                    return false;
                }
                if (_heroSaveTombstones.Contains(workItem.Index)) return false;
                workItem.Generation = GetHeroSaveGeneration(workItem.Index);
                if (_heroSavePending.TryGetValue(key, out var pending))
                {
                    pending.ReplaceWith(workItem);
                    return true;
                }
                _heroSavePending.Add(key, workItem);
                _heroSaveOrder.Enqueue(key);
                Monitor.Pulse(_heroSaveQueueLock);
                return true;
            }
        }

        private long GetHeroSaveGeneration(int index) =>
            _heroSaveGenerations.TryGetValue(index, out var generation)
                ? generation : 0;

        private bool IsHeroSaveWorkCurrent(NativeHeroSaveWorkItem item)
        {
            lock (_heroSaveQueueLock)
                return item != null
                       && !_heroSaveTombstones.Contains(item.Index)
                       && item.Generation == GetHeroSaveGeneration(item.Index);
        }

        private void AdvanceHeroSaveGeneration(int index, bool tombstone)
        {
            if (index <= 0) return;
            lock (_heroSaveQueueLock)
            {
                var current = GetHeroSaveGeneration(index);
                _heroSaveGenerations[index] = current == long.MaxValue
                    ? 0 : current + 1;
                if (tombstone) _heroSaveTombstones.Add(index);
                var keys = new List<string>();
                foreach (var pair in _heroSavePending)
                    if (pair.Value.Index == index) keys.Add(pair.Key);
                foreach (var key in keys) _heroSavePending.Remove(key);
            }
        }

        private void ReviveHeroSaveIndex(int index)
        {
            if (index <= 0) return;
            lock (_heroSaveQueueLock)
            {
                var current = GetHeroSaveGeneration(index);
                _heroSaveGenerations[index] = current == long.MaxValue
                    ? 0 : current + 1;
                _heroSaveTombstones.Remove(index);
            }
        }

        private bool EnqueueHeroLifecycleSnapshot(
            NativeHeroLogicalSnapshot snapshot)
        {
            var workItem = new NativeHeroSaveWorkItem(snapshot.Index,
                snapshot.HeroName, snapshot.Record, snapshot.Data,
                snapshot.DynamicData, 0, 0, 0, snapshot.IsDelete,
                snapshot.HeroType, snapshot.Consignation, snapshot.IndexJob,
                snapshot.ForceLevel);
            workItem.SetLogicalSnapshot(snapshot);
            if (!EnqueueHeroSave(workItem)) return false;
            _heroLogicalCache.Set(snapshot);
            _heroDataService.SetNativeForceLevelOverride(snapshot.Index,
                snapshot.ForceLevel);
            return true;
        }

        private bool EnqueueHeroExactSnapshot(
            NativeHeroLogicalSnapshot snapshot)
        {
            var workItem = new NativeHeroSaveWorkItem(snapshot.Index,
                snapshot.HeroName, snapshot.Record, snapshot.Data,
                snapshot.DynamicData, 0, 0, 0, snapshot.IsDelete,
                snapshot.HeroType, snapshot.Consignation, snapshot.IndexJob,
                null, true);
            workItem.SetLogicalSnapshot(snapshot);
            if (!EnqueueHeroSave(workItem)) return false;
            _heroLogicalCache.Set(snapshot);
            _heroDataService.ClearNativeForceLevelOverride(snapshot.Index);
            return true;
        }

        private void ProcessHeroSaveQueue()
        {
            List<NativeHeroSaveWorkItem> batch = null;
            var batchIndex = 0;
            while (true)
            {
                if (batch == null || batchIndex >= batch.Count)
                {
                    lock (_heroSaveQueueLock)
                    {
                        while (_heroSaveOrder.Count == 0 && !_heroSaveStopping)
                            Monitor.Wait(_heroSaveQueueLock);
                        if (_heroSaveOrder.Count == 0 && _heroSaveStopping)
                            return;
                        batch = new List<NativeHeroSaveWorkItem>(_heroSaveOrder.Count);
                        while (_heroSaveOrder.Count != 0)
                        {
                            var key = _heroSaveOrder.Dequeue();
                            if (!_heroSavePending.Remove(key, out var item)) continue;
                            batch.Add(item);
                        }
                    }
                    batchIndex = 0;
                }

                var processed = 0;
                var cycleLimit = Volatile.Read(ref _heroSaveStopping) ? 200 : 100;
                while (batchIndex < batch.Count && processed < cycleLimit)
                {
                    var item = batch[batchIndex];
                    var saveResult = PersistHeroSave(item);
                    if (saveResult == NativeHeroSaveResult.Success)
                    {
                        batchIndex++;
                        processed++;
                        continue;
                    }

                    item.RetryCount++;
                    if (item.RetryCount == 11)
                    {
                        DBShare.MainOutMessage(
                            $"[HeroSave] retry 11 hero={item.HeroName}");
                    }
                    if (item.RetryCount < 20) break;
                    DBShare.MainOutMessage(
                        $"[HeroSave] dropped after 20 attempts hero={item.HeroName}");
                    batchIndex++;
                    processed++;
                }
                Thread.Sleep(5);
            }
        }

        private NativeHeroSaveResult PersistHeroSave(NativeHeroSaveWorkItem item)
        {
            NativeHeroSaveResult result;
            try
            {
                lock (_heroCreateLock)
                {
                    if (!IsHeroSaveWorkCurrent(item))
                        return NativeHeroSaveResult.Success;
                    result = item.HasRecord
                        ? _heroDataService.SaveRecordDetailed(item.Index,
                            item.Record, item.PreparedData, item.DynamicData,
                            item.IsDelete, item.HeroType, item.Consignation,
                            item.IndexJob, item.ForceLevelOverride,
                            item.ExactPrepared)
                        : _heroDataService.PersistNativeForceLevel(
                            item.Index, item.ForceLevelOverride.GetValueOrDefault());
                }
            }
            catch (Exception ex)
            {
                DBShare.MainOutMessage(
                    $"[HeroSave] persist exception hero={item.HeroName}: {ex.Message}");
                return NativeHeroSaveResult.RetryableFailure;
            }
            if (result != NativeHeroSaveResult.Success) return result;
            if (item.Param1 == 0 && item.Param2 == 0) return result;
            try { SendHeroSaveNotification(item.Param1, item.Param2); }
            catch (Exception ex)
            {
                DBShare.MainOutMessage(
                    $"[HeroSave] notification exception hero={item.HeroName}: {ex.Message}");
            }
            return result;
        }

        private void SendHeroSaveNotification(int param1, int param2)
        {
            var response = NativeDbServerProtocol.CreateHeroSaveNotification(
                param1, param2);
            if (!LegacyDbServerFrameCodec.TryEncode(response, out var wire,
                    out var error, NativeDbServerProtocol.MaximumFrameLength))
            {
                DBShare.MainOutMessage(
                    "[HeroSave] 0x013C notification encode failed: " + error);
                return;
            }

            List<TServerInfo> targets;
            lock (_serverListLock)
            {
                targets = new List<TServerInfo>();
                foreach (var peer in _serverList)
                {
                    if (peer?.Socket?.Connected != true
                        || peer.WireModeDetector.Mode
                        != DbServerWireMode.NativeType12) continue;
                    var serverType = unchecked((byte)Volatile.Read(
                        ref peer.NativeServerType));
                    if (NativeDbServerProtocol.ShouldReceiveHeroSaveNotification(
                            serverType))
                        targets.Add(peer);
                }
            }

            foreach (var target in targets)
            {
                try { SendAll(target.Socket, wire); }
                catch (Exception ex) when (ex is SocketException
                                           || ex is ObjectDisposedException)
                {
                    DBShare.MainOutMessage(
                        "[HeroSave] 0x013C notification send failed: "
                        + ex.Message);
                }
            }
        }

        private static string NormalizeAsciiUpper(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var chars = value.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
                if (chars[i] is >= 'a' and <= 'z') chars[i] -= (char)32;
            return new string(chars);
        }

        private void CreateHeroRcd(int queryId, byte[] frame, Socket socket)
        {
            if (!NativeHeroDbFrameCodec.TryDecodeCreateRequest(
                    frame, out var request, out var error))
            {
                DBShare.MainOutMessage($"[HeroCreate] REJECT qid={queryId}: {error}");
                SendFail(queryId, socket);
                return;
            }

            var response = new NativeHeroCreateResponse
            {
                HeroType = request.HeroType,
                Result = CreateHeroRcd(request),
                MasterName = request.MasterName,
                HeroName = request.HeroName
            };
            SendHeroCreateResponse(queryId, response, socket);
        }

        private int CreateHeroRcd(NativeHeroCreateRequest request)
        {
            lock (_heroCreateLock)
            {
                if (!_sensitiveWordFilter.ValidateNativeHeroName(request.HeroName))
                    return -1;
                if (request.HeroType is < 1 or > 2 || request.Code is < 1 or > 6)
                    return -5;
                if (_playRecordService.Index(request.HeroName) >= 0)
                    return -2;
                if (_heroRecordService.IsHeroNameExists(request.HeroName))
                    return -3;

                var heroes = _heroRecordService.QueryHeroesByMaster(request.MasterName);
                var activeCount = 0;
                foreach (var hero in heroes)
                {
                    if (hero == null || hero.IsDelete) continue;
                    if (hero.Consignation == 0) activeCount++;
                    if (hero.HeroType == request.HeroType) activeCount = 2;
                }
                if (activeCount >= 2) return -4;

                var job = (request.Code - 1) % 3;
                var sex = (request.Code - 1) / 3;
                var idx = _heroRecordService.CreateHero(request.MasterName,
                    request.HeroName, request.HeroType, job, sex, 0);
                if (idx <= 0)
                    return -6;
                ReviveHeroSaveIndex(idx);
                _heroDataService.RegisterNativeIndex(idx);

                if (!NativeHeroDbFrameCodec.TryCreateInitialRecord(
                        request, out var record, out var error)
                    || !_heroDataService.SaveRecord(idx, record.ToArray(), Array.Empty<byte>()))
                {
                    if (!HardDeleteNativeHeroIndex(idx))
                    {
                        DBShare.MainOutMessage(
                            $"[HeroCreate] compensation failed idx={idx} hero={request.HeroName}");
                    }
                    _heroLogicalCache.Remove(idx);
                    DBShare.MainOutMessage(
                        $"[HeroCreate] initial record failed hero={request.HeroName}: {error}");
                    return -6;
                }
                _heroLogicalCache.Remove(idx);
                return request.Code;
            }
        }

        private void DeleteHeroRcd(int queryId, byte[] frame, Socket socket)
        {
            if (!NativeHeroDbFrameCodec.TryDecodeDeleteRequest(
                    frame, out var request, out var error))
            {
                DBShare.MainOutMessage($"[HeroDelete] REJECT qid={queryId}: {error}");
                SendFail(queryId, socket);
                return;
            }

            int result;
            lock (_heroCreateLock)
            {
                result = DeleteHeroRcd(request);
            }
            var response = new NativeHeroDeleteResponse
            {
                Result = result,
                Account = request.Account,
                MasterName = request.MasterName,
                HeroName = request.HeroName
            };
            if (!NativeHeroDbFrameCodec.TryEncodeDeleteResponse(
                    response, out var responseFrame, out error))
                throw new InvalidOperationException(error);

            var responsePack = new RequestServerPacket { QueryId = queryId };
            responsePack.Message = EDcode.EncodeBuffer(ProtoBufDecoder.Serialize(
                new ServerMessagePacket(NativeHeroDbFrameCodec.DeleteResponseCommand,
                    result, 0, 0, 0)));
            responsePack.Packet = EDcode.EncodeBuffer(responseFrame);
            SendRequest(socket, responsePack);
        }

        private int DeleteHeroRcd(NativeHeroDeleteRequest request)
        {
            var heroes = _heroRecordService.QueryHeroesByMaster(request.MasterName);
            if (heroes.Count > 2) return 0;
            HeroIndexInfo selected = null;
            foreach (var hero in heroes)
            {
                if (hero == null || hero.IsDelete) continue;
                if (string.IsNullOrEmpty(request.HeroName))
                {
                    if (hero.Consignation == 0)
                    {
                        selected = hero;
                        break;
                    }
                }
                else if (string.Equals(hero.HeroName, request.HeroName,
                             StringComparison.OrdinalIgnoreCase))
                {
                    selected = hero;
                    break;
                }
            }
            if (selected != null)
            {
                _heroLogicalCache.TryGet(selected.Idx,
                    out var logicalSnapshot);
                if (!_heroRecordService.DeleteHero(selected.Idx)) return 0;
                AdvanceHeroSaveGeneration(selected.Idx, tombstone: false);
                _heroSaveState.Remove(selected.Idx);
                if (logicalSnapshot != null)
                {
                    var deleted = logicalSnapshot.WithIndexState(true,
                        logicalSnapshot.HeroType,
                        logicalSnapshot.Consignation);
                    if (!EnqueueHeroLifecycleSnapshot(deleted))
                        DBShare.MainOutMessage(
                            $"[HeroDelete] final snapshot enqueue failed idx={selected.Idx}");
                }
                else
                    _heroLogicalCache.Remove(selected.Idx);
                return 1;
            }

            if (!string.IsNullOrEmpty(request.HeroName))
            {
                foreach (var hero in _heroRecordService.QueryDeletedHeroesByMaster(
                             request.MasterName))
                {
                    if (hero != null && string.Equals(hero.HeroName,
                            request.HeroName, StringComparison.OrdinalIgnoreCase))
                        return 3;
                }
            }
            return 0;
        }

        private void RenameHeroRcd(int queryId, byte[] frame, Socket socket)
        {
            if (!NativeHeroDbFrameCodec.TryDecodeRenameRequest(
                    frame, out var request, out var error))
            {
                DBShare.MainOutMessage($"[HeroRename] REJECT qid={queryId}: {error}");
                SendFail(queryId, socket);
                return;
            }

            ushort result;
            lock (_heroCreateLock)
            {
                result = RenameHeroRcd(request);
            }
            var response = new NativeHeroRenameResponse
            {
                Result = result,
                Code = request.Code,
                MasterName = request.MasterName,
                NewHeroName = request.NewHeroName
            };
            if (!NativeHeroDbFrameCodec.TryEncodeRenameResponse(
                    response, out var responseFrame, out error))
                throw new InvalidOperationException(error);
            SendNativeHeroFrameResponse(queryId,
                NativeHeroDbFrameCodec.RenameResponseCommand, result,
                responseFrame, socket);
        }

        private ushort RenameHeroRcd(NativeHeroRenameRequest request)
        {
            if (string.IsNullOrEmpty(request.MasterName)
                || string.IsNullOrEmpty(request.NewHeroName))
                return 5;
            var heroes = _heroRecordService.QueryHeroesByMaster(request.MasterName);
            if (heroes.Count > 2) return 5;
            HeroIndexInfo selected = null;
            foreach (var hero in heroes)
            {
                if (hero == null || hero.IsDelete) continue;
                if (request.SelectionMode == 1)
                {
                    if (!string.Equals(hero.HeroName, request.OldHeroName,
                            StringComparison.OrdinalIgnoreCase))
                        continue;
                }
                else if (hero.Job != byte.MaxValue)
                {
                    continue;
                }
                selected = hero;
                break;
            }
            if (selected == null) return 5;

            var nameCheck = _sensitiveWordFilter.ValidateChrName(
                request.NewHeroName, DBShare.g_boEnglishNames);
            if (!nameCheck.valid) return 2;
            if (_playRecordService.Index(request.NewHeroName) >= 0) return 3;
            if (_heroRecordService.IsHeroNameExists(request.NewHeroName)) return 4;
            _heroLogicalCache.TryGet(selected.Idx, out var logicalSnapshot);
            if (!_heroRecordService.RenameHero(
                    selected.HeroName, request.NewHeroName, selected.Idx))
                return 5;
            if (logicalSnapshot != null)
            {
                if (logicalSnapshot.TryRenameHero(request.NewHeroName,
                        out var renamedSnapshot, out var renameError))
                {
                    if (!EnqueueHeroLifecycleSnapshot(renamedSnapshot))
                        DBShare.MainOutMessage(
                            $"[HeroRename] final snapshot enqueue failed idx={selected.Idx}");
                }
                else
                {
                    _heroLogicalCache.Remove(selected.Idx);
                    DBShare.MainOutMessage(
                        $"[HeroRename] logical snapshot failed idx={selected.Idx}: {renameError}");
                }
            }
            else
                _heroLogicalCache.Remove(selected.Idx);
            return 1;
        }

        private void ListConsignedHeroes(int queryId, byte[] frame, Socket socket)
        {
            if (!NativeHeroDbFrameCodec.TryDecodeConsignedListRequest(
                    frame, out var request, out var error))
            {
                DBShare.MainOutMessage($"[HeroConsignedList] REJECT qid={queryId}: {error}");
                SendFail(queryId, socket);
                return;
            }

            var response = CreateConsignedListResponse(request);
            if (!NativeHeroDbFrameCodec.TryEncodeConsignedListResponse(
                    response, out var responseFrame, out error))
                throw new InvalidOperationException(error);
            SendNativeHeroFrameResponse(queryId,
                NativeHeroDbFrameCodec.ConsignedListResponseCommand,
                response.Entries.Count, responseFrame, socket);
        }

        private NativeHeroConsignedListResponse CreateConsignedListResponse(
            NativeHeroConsignedListRequest request)
        {
            var entries = new List<NativeHeroConsignedListEntry>();
            var heroes = _heroRecordService.QueryHeroesByMaster(request.MasterName);
            if (heroes.Count <= 2)
            {
                foreach (var hero in heroes)
                {
                    if (hero == null || hero.IsDelete || hero.Consignation != 1) continue;
                    entries.Add(new NativeHeroConsignedListEntry
                    {
                        HeroName = hero.HeroName,
                        HeroType = hero.HeroType,
                        Job = hero.Job,
                        Level = hero.Level,
                        Sex = hero.Sex
                    });
                }
            }
            else
            {
                DBShare.MainOutMessage(
                    $"[HeroConsignedList] REJECT master={request.MasterName}: more than two active hero rows");
            }

            return new NativeHeroConsignedListResponse
            {
                MasterName = request.MasterName,
                Entries = entries
            };
        }

        private void RestoreConsignedHero(int queryId, byte[] frame, Socket socket)
        {
            if (!NativeHeroDbFrameCodec.TryDecodeRestoreConsignedRequest(
                    frame, out var request, out var error))
            {
                DBShare.MainOutMessage($"[HeroRestoreConsigned] REJECT qid={queryId}: {error}");
                SendFail(queryId, socket);
                return;
            }

            var response = RestoreConsignedHero(request);
            if (!NativeHeroDbFrameCodec.TryEncodeRestoreConsignedResponse(
                    response, out var responseFrame, out error))
                throw new InvalidOperationException(error);
            SendNativeHeroFrameResponse(queryId,
                NativeHeroDbFrameCodec.RestoreConsignedResponseCommand,
                response.Result, responseFrame, socket);
        }

        private NativeHeroRestoreConsignedResponse RestoreConsignedHero(
            NativeHeroRestoreConsignedRequest request)
        {
            int result = 0;
            var heroType = 0;
            lock (_heroCreateLock)
            {
                var heroes = _heroRecordService.QueryHeroesByMaster(request.MasterName);
                if (heroes.Count <= 2)
                {
                    var hasActiveHero = false;
                    HeroIndexInfo selected = null;
                    foreach (var hero in heroes)
                    {
                        if (hero == null || hero.IsDelete) continue;
                        if (hero.Consignation == 0) hasActiveHero = true;
                        if (string.Equals(hero.HeroName, request.HeroName,
                                StringComparison.OrdinalIgnoreCase))
                            selected = hero;
                    }
                    if (hasActiveHero)
                    {
                        result = 2;
                    }
                    else if (selected != null && selected.Consignation == 1
                             && _heroRecordService.SetHeroConsignation(selected.Idx, 1, 0))
                    {
                        result = 1;
                        heroType = selected.HeroType;
                        _heroSaveState.ClearConsignation(selected.Idx);
                        if (_heroLogicalCache.TryGet(selected.Idx,
                                out var logicalSnapshot))
                        {
                            var restored = logicalSnapshot.WithIndexState(
                                logicalSnapshot.IsDelete,
                                logicalSnapshot.HeroType, 0);
                            if (!EnqueueHeroLifecycleSnapshot(restored))
                                DBShare.MainOutMessage(
                                    $"[HeroRestoreConsigned] final snapshot enqueue failed idx={selected.Idx}");
                        }
                        else
                            _heroLogicalCache.Remove(selected.Idx);
                    }
                }
            }

            return new NativeHeroRestoreConsignedResponse
            {
                Result = result,
                HeroType = heroType,
                MasterName = request.MasterName,
                HeroName = request.HeroName
            };
        }

        private void BuildThreeSlotHero(int queryId, byte[] frame, Socket socket)
        {
            if (!NativeHeroDbFrameCodec.TryDecodeBuildThreeSlotRequest(
                    frame, out var request, out var error))
            {
                DBShare.MainOutMessage($"[HeroThreeSlot] REJECT qid={queryId}: {error}");
                SendFail(queryId, socket);
                return;
            }

            var response = BuildThreeSlotHero(request);
            if (!NativeHeroDbFrameCodec.TryEncodeBuildThreeSlotResponse(
                    response, out var responseFrame, out error))
                throw new InvalidOperationException(error);
            SendNativeHeroFrameResponse(queryId,
                NativeHeroDbFrameCodec.BuildThreeSlotResponseCommand,
                response.Result, responseFrame, socket);
        }

        private NativeHeroBuildThreeSlotResponse BuildThreeSlotHero(
            NativeHeroBuildThreeSlotRequest request)
        {
            ushort result;
            string heroName;
            NativeHeroLogicalSnapshot[] builtSnapshots;
            lock (_heroCreateLock)
            {
                result = _heroDataService.BuildThreeSlot(request.MasterName,
                    _heroLogicalCache.SnapshotAll(), out heroName,
                    out builtSnapshots);
                if (result == 1)
                {
                    foreach (var snapshot in builtSnapshots)
                    {
                        _heroLogicalCache.Set(snapshot);
                        var workItem = new NativeHeroSaveWorkItem(
                            snapshot.Index, snapshot.HeroName, snapshot.Record,
                            snapshot.Data, snapshot.DynamicData, 0, 0, 0,
                            snapshot.IsDelete, snapshot.HeroType,
                            snapshot.Consignation, snapshot.IndexJob,
                            snapshot.ForceLevel);
                        workItem.SetLogicalSnapshot(snapshot);
                        if (!EnqueueHeroSave(workItem))
                        {
                            result = 5;
                            break;
                        }
                        _heroSaveState.Remove(snapshot.Index);
                        _heroAttachmentState.Remove(snapshot.Index);
                    }
                }
            }
            return new NativeHeroBuildThreeSlotResponse
            {
                Result = result,
                MasterName = request.MasterName,
                HeroName = result == 1 ? heroName : string.Empty
            };
        }

        private void SendNativeHeroFrameResponse(int queryId, ushort command,
            int result, byte[] frame, Socket socket)
        {
            var packet = new RequestServerPacket { QueryId = queryId };
            packet.Message = EDcode.EncodeBuffer(ProtoBufDecoder.Serialize(
                new ServerMessagePacket(command, result, 0, 0, 0)));
            packet.Packet = EDcode.EncodeBuffer(frame);
            SendRequest(socket, packet);
        }

        private void SendHeroCreateResponse(int queryId,
            NativeHeroCreateResponse response, Socket socket)
        {
            if (!NativeHeroDbFrameCodec.TryEncodeCreateResponse(
                    response, out var frame, out var error))
                throw new InvalidOperationException(error);
            var packet = new RequestServerPacket { QueryId = queryId };
            packet.Message = EDcode.EncodeBuffer(ProtoBufDecoder.Serialize(
                new ServerMessagePacket(NativeHeroDbFrameCodec.CreateResponseCommand,
                    response.Result, response.HeroType, 0, 0)));
            packet.Packet = EDcode.EncodeBuffer(frame);
            SendRequest(socket, packet);
        }

        private static NativeHeroLoadResponse HeroLoadFailure(ushort status, string masterName)
            => new() { Status = status, MasterName = masterName };

        private void SendHeroLoadResponse(int queryId,
            NativeHeroLoadResponse response, Socket socket)
        {
            if (!NativeHeroDbFrameCodec.TryEncodeLoadResponse(
                    response, out var frame, out var error))
            {
                DBShare.MainOutMessage(
                    $"[HeroLoad] response encode failed qid={queryId}: {error}");
                response = new NativeHeroLoadResponse { Status = 0 };
                if (!NativeHeroDbFrameCodec.TryEncodeLoadResponse(
                        response, out frame, out error))
                    throw new InvalidOperationException(error);
            }

            var responsePack = new RequestServerPacket { QueryId = queryId };
            var outer = new ServerMessagePacket(
                NativeHeroDbFrameCodec.LoadResponseCommand, response.Status, 0, 0, 0);
            responsePack.Message = EDcode.EncodeBuffer(ProtoBufDecoder.Serialize(outer));
            responsePack.Packet = EDcode.EncodeBuffer(frame);
            SendRequest(socket, responsePack);
        }

        // ===================== 网络发送 =====================

        private void SendRequest(Socket socket, RequestServerPacket requestPacket)
        {
            int queryPart = HUtil32.MakeLong(
                requestPacket.QueryId ^ 170,
                requestPacket.Message.Length + (requestPacket.Packet?.Length ?? 0) + 6);
            requestPacket.CheckKey = EDcode.EncodeBuffer(BitConverter.GetBytes(queryPart));
            requestPacket.Packet ??= Array.Empty<byte>();
            var pk = requestPacket.GetBuffer();
            FileLog($"[SendResp] qid={requestPacket.QueryId} len={pk.Length} msg={requestPacket.Message?.Length} pkt={requestPacket.Packet?.Length}");
            SendAll(socket, pk);
        }

        private void SendRequest<T>(Socket socket, RequestServerPacket requestPacket, T packet) where T : class, new()
        {
            if (packet != null)
            {
                var serialized = ProtoBufDecoder.Serialize(packet);
                FileLog($"[SendResp<T>] ProtoBuf raw={serialized?.Length ?? -1}B");
                if (serialized == null)
                {
                    FileLog("[SendResp<T>] ProtoBuf serialize FAILED");
                    requestPacket.Packet = Array.Empty<byte>();
                }
                else requestPacket.Packet = EDcode.EncodeBuffer(serialized);
            }
            int s = HUtil32.MakeLong(requestPacket.QueryId ^ 170,
                requestPacket.Message.Length + requestPacket.Packet.Length + 6);
            requestPacket.CheckKey = EDcode.EncodeBuffer(BitConverter.GetBytes(s));
            var pk = requestPacket.GetBuffer();
            FileLog($"[SendResp<T>] qid={requestPacket.QueryId} len={pk.Length} msg={requestPacket.Message?.Length} pkt={requestPacket.Packet?.Length}");
            SendAll(socket, pk);
        }

        private void SendFail(int nQueryId, Socket socket)
        {
            var responsePack = new RequestServerPacket { QueryId = nQueryId };
            var msg = new ServerMessagePacket(Grobal2.DBR_FAIL, 0, 0, 0, 0);
            responsePack.Message = EDcode.EncodeBuffer(ProtoBufDecoder.Serialize(msg));
            SendRequest(socket, responsePack);
        }

        private static void SendAll(Socket socket, byte[] buffer)
        {
            lock (socket)
            {
                var offset = 0;
                while (offset < buffer.Length)
                {
                    var sent = socket.Send(buffer, offset, buffer.Length - offset, SocketFlags.None);
                    if (sent <= 0) throw new SocketException((int)SocketError.ConnectionReset);
                    offset += sent;
                }
            }
        }

        internal sealed class NativeSaveWorkItem
        {
            private NativeSaveWorkItem(int index)
            {
                Index = index;
                RestoreDeleteState = true;
            }

            public NativeSaveWorkItem(int index, NativeSavePersistenceData persistence)
            {
                Index = index;
                Persistence = persistence;
            }

            public NativeSaveWorkItem(NativeForceLevelMutation mutation)
            {
                if (mutation == null
                    || mutation.Target != NativeForceLevelTarget.Player)
                    throw new ArgumentException(
                        "player ForceLv mutation is required", nameof(mutation));
                Index = mutation.Index;
                ForceLevel = mutation.ForceLevel;
            }

            public int Index { get; }
            public NativeSavePersistenceData Persistence { get; private set; }
            public ushort? ForceLevel { get; private set; }
            public bool RestoreDeleteState { get; private set; }
            public long Generation { get; set; }
            public int RetryCount { get; set; }

            public static NativeSaveWorkItem ForCharacterRestore(int index) =>
                new(index);

            public void ReplaceWith(NativeSaveWorkItem newer)
            {
                if (newer.Persistence != null) Persistence = newer.Persistence;
                if (newer.ForceLevel.HasValue) ForceLevel = newer.ForceLevel;
                if (newer.RestoreDeleteState) RestoreDeleteState = true;
                Generation = newer.Generation;
            }
        }

        internal sealed class NativeHeroSaveWorkItem
        {
            public NativeHeroSaveWorkItem(int index, string heroName,
                byte[] record, byte[] preparedData, byte[] dynamicData,
                ushort saveMode,
                int param1, int param2, bool isDelete, int heroType,
                int consignation, int indexJob, ushort? forceLevelOverride,
                bool exactPrepared = false)
            {
                Index = index;
                HeroName = heroName ?? string.Empty;
                HasRecord = record != null;
                Record = record == null ? null : (byte[])record.Clone();
                PreparedData = preparedData == null
                    ? null : (byte[])preparedData.Clone();
                DynamicData = dynamicData == null
                    ? Array.Empty<byte>() : (byte[])dynamicData.Clone();
                SaveMode = saveMode;
                IsDelete = isDelete;
                HeroType = heroType;
                Consignation = consignation;
                IndexJob = indexJob;
                Param1 = param1;
                Param2 = param2;
                ForceLevelOverride = forceLevelOverride;
                ExactPrepared = exactPrepared;
            }

            private NativeHeroSaveWorkItem(NativeForceLevelMutation mutation)
            {
                Index = mutation.Index;
                HeroName = LegacyGbkText.Decode(mutation.CharacterNameBytes);
                Record = null;
                PreparedData = null;
                DynamicData = Array.Empty<byte>();
                ForceLevelOverride = mutation.ForceLevel;
            }

            public static NativeHeroSaveWorkItem ForForceLevel(
                NativeForceLevelMutation mutation)
            {
                if (mutation == null
                    || mutation.Target != NativeForceLevelTarget.Hero)
                    throw new ArgumentException(
                        "hero ForceLv mutation is required", nameof(mutation));
                return new NativeHeroSaveWorkItem(mutation);
            }

            public int Index { get; private set; }
            public string HeroName { get; private set; }
            public byte[] Record { get; private set; }
            public byte[] PreparedData { get; private set; }
            public byte[] DynamicData { get; private set; }
            public bool HasRecord { get; private set; }
            public ushort SaveMode { get; }
            public bool IsDelete { get; private set; }
            public int HeroType { get; private set; }
            public int Consignation { get; private set; }
            public int IndexJob { get; private set; }
            public int Param1 { get; private set; }
            public int Param2 { get; private set; }
            public ushort? ForceLevelOverride { get; private set; }
            public bool ExactPrepared { get; private set; }
            public long Generation { get; set; }
            public NativeHeroLogicalSnapshot LogicalSnapshot { get; private set; }
            public int RetryCount { get; set; }

            public void SetLogicalSnapshot(NativeHeroLogicalSnapshot snapshot) =>
                LogicalSnapshot = snapshot?.CloneSnapshot();

            public void ApplyState(NativeHeroSaveState state)
            {
                IsDelete = state.IsDelete;
                HeroType = state.HeroType;
                Consignation = state.Consignation;
            }

            public void ReplaceWith(NativeHeroSaveWorkItem newer)
            {
                Index = newer.Index;
                HeroName = newer.HeroName;
                if (newer.HasRecord)
                {
                    HasRecord = true;
                    Record = newer.Record;
                    PreparedData = newer.PreparedData;
                    DynamicData = newer.DynamicData;
                    IsDelete = newer.IsDelete;
                    HeroType = newer.HeroType;
                    Consignation = newer.Consignation;
                    IndexJob = newer.IndexJob;
                    Param1 = newer.Param1;
                    Param2 = newer.Param2;
                    LogicalSnapshot = newer.LogicalSnapshot?.CloneSnapshot();
                    ForceLevelOverride = newer.ForceLevelOverride;
                    ExactPrepared = newer.ExactPrepared;
                }
                if (newer.ForceLevelOverride.HasValue)
                {
                    ForceLevelOverride = newer.ForceLevelOverride;
                    if (!newer.HasRecord && LogicalSnapshot != null
                        && LogicalSnapshot.TryWithForceLevel(
                            newer.ForceLevelOverride.Value,
                            out var forcedSnapshot, out _))
                    {
                        LogicalSnapshot = forcedSnapshot;
                        Record = forcedSnapshot.Record;
                        PreparedData = forcedSnapshot.Data;
                    }
                }
                Generation = newer.Generation;
            }
        }

    }
}
