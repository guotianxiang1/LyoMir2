using System.Text.RegularExpressions;

var root = FindRepositoryRoot();
var tests = new (string Name, Action Run)[]
{
    ("YBDB address fallback", CheckAddressFallback),
    ("fixed 6108 endpoint", CheckFixedPort),
    ("service lifecycle wiring", CheckLifecycleWiring),
    ("native heartbeat lifecycle", CheckNativeHeartbeatLifecycle),
    ("socket callback queue boundary", CheckSocketQueueBoundary),
    ("native queue and async send semantics", CheckNativeQueueAndSendSemantics),
    ("native identity field mapping", CheckIdentityFieldMapping),
    ("generation-atomic outbound enqueue", CheckAtomicOutboundEnqueue),
    ("user-engine completion dispatch", CheckCompletionDispatch),
    ("ClearNickLinfu command contract", CheckClearNickCommand),
    ("ClientQuestGetDiam dormant request", CheckQuestDiamondRequest),
    ("RefreshCredit dormant 103/1103 contract", CheckCreditContract),
    ("native credit login lifecycle", CheckCreditLoginLifecycle),
    ("native logout 104 dormant boundary", CheckNativeLogoutDormantBoundary),
    ("native forge-mode lifecycle", CheckNativeForgeModeLifecycle),
    ("1303 response messages", CheckResponseMessages),
    ("command audit ownership", CheckCommandAuditOwnership)
};

foreach (var test in tests)
{
    try
    {
        test.Run();
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException($"{test.Name}: {ex.Message}", ex);
    }
}

Console.WriteLine(
    $"YbDbClientCompatCheck PASS tests={tests.Length} port=6108 " +
    "responses=snapshot send=150ms-async ClearNickLinfu=permission4");
return;

void CheckAddressFallback()
{
    var config = Read("GameSvr", "Configs", "GameSvrConfig.cs");
    RequireMatch(config, @"\bstring\s+sYBDBAddr\b",
        "GameSvrConfig.sYBDBAddr is missing");

    var loader = Read("GameSvr", "Configs", "ServerConfig.cs");
    var dbRead = loader.IndexOf("\"DBAddr\"", StringComparison.Ordinal);
    var ybRead = loader.IndexOf("\"YBDBAddr\"", StringComparison.Ordinal);
    Assert(dbRead >= 0 && ybRead > dbRead,
        "[Server] YBDBAddr must be read after DBAddr");

    var region = Window(loader, ybRead, 260, 700);
    Require(region, "sYBDBAddr", "YBDBAddr is not assigned to the runtime config");
    Require(region, "sDBAddr", "empty YBDBAddr does not fall back to DBAddr");
    RequireMatch(region, @"string\.IsNullOr(?:WhiteSpace|Empty)\s*\(",
        "YBDBAddr fallback must also handle a present but empty value");
}

void CheckFixedPort()
{
    var service = ReadYbDbClient();
    var portDeclaration = Regex.Match(service,
        @"\bconst\s+int\s+(?<name>[A-Za-z_]\w*)\s*=\s*6108\s*;",
        RegexOptions.CultureInvariant);
    Assert(portDeclaration.Success, "YbDbClient must declare fixed port 6108");

    var portName = Regex.Escape(portDeclaration.Groups["name"].Value);
    Require(service, "M2Share.g_Config?.sYBDBAddr",
        "YbDbClient does not source its host from the YBDB address config");
    RequireMatch(service,
        $@"\.ConnectReplacingPending\s*\(\s*(?:M2Share\.g_Config\??\.sYBDBAddr|_host|host)\s*,\s*{portName}\s*\)",
        "YbDbClient does not connect its YBDB host to the fixed 6108 port");
    Reject(service, "nYBDBPort", "YBDB port was made configurable");
    Reject(service, "sYBDBPort", "YBDB port was made configurable");
}

void CheckLifecycleWiring()
{
    var service = ReadYbDbClient();
    foreach (var method in new[] { "Start", "Stop", "Pulse" })
        ExtractMethod(service, method);

    var server = Read("GameSvr", "GameServer.cs");
    var start = ExtractMethod(server, "StartService");
    RequireMatch(start, @"YbDbClient\.Instance\.Start\s*\(\s*\)\s*;",
        "ServerBase.StartService does not start YbDbClient");

    var stop = ExtractMethod(server, "Stop");
    var stopYb = stop.IndexOf("YbDbClient.Instance.Stop", StringComparison.Ordinal);
    var flushYb = stop.IndexOf("YbDbClient.Instance.FlushPendingSendsSynchronously",
        StringComparison.Ordinal);
    var stopUsers = stop.IndexOf("UserEngine?.Stop", StringComparison.Ordinal);
    if (stopUsers < 0)
        stopUsers = stop.IndexOf("UserEngine.Stop", StringComparison.Ordinal);
    var saveUsers = stop.IndexOf("SaveHumanRcd(player, 3)",
        StringComparison.Ordinal);
    Assert(stopYb >= 0, "ServerBase.Stop does not stop YbDbClient");
    Assert(stopUsers >= 0 && saveUsers > stopUsers,
        "shutdown does not snapshot players after stopping UserEngine");
    Assert(flushYb > saveUsers,
        "shutdown does not flush DecLF accounting after player snapshots");
    Assert(stopYb > flushYb,
        "YbDbClient stops before the final synchronous accounting flush");

    var timer = Read("GameSvr", "TimedService.cs");
    var serviceTimer = ExtractMethod(timer, "ServiceTimer");
    RequireMatch(serviceTimer, @"YbDbClient\.Instance\.Pulse\s*\(\s*\)\s*;",
        "TimedService.ServiceTimer does not pulse YbDbClient");
}

void CheckSocketQueueBoundary()
{
    var service = ReadYbDbClient();
    RequireMatch(service,
        @"Queue\s*<\s*QueuedResponse\s*>",
        "received frames are not stored with their connection generation");

    var subscription = Regex.Match(service,
        @"ReceivedDatagram\s*\+=\s*(?<handler>[A-Za-z_]\w*)\s*;",
        RegexOptions.CultureInvariant);
    Assert(subscription.Success, "YBDB receive callback subscription is missing");
    var callback = ExtractMethod(service, subscription.Groups["handler"].Value);
    Require(callback, ".Append", "receive callback does not feed the stream parser");
    Require(callback, ".Enqueue", "receive callback does not enqueue decoded frames");
    Require(callback, "QueuedResponse(generation, frame)",
        "receive callback loses the connection generation");
    Require(callback, "ReferenceEquals(e.Socket",
        "receive callback does not reject stale sockets");
    RequireMatch(callback, @"Disconnect\s*\(\s*e\??\.Socket\s*\)",
        "parse failure can disconnect a newer socket");

    var socket = Read("SystemModule", "Sockets", "AsyncSocketClient", "IClientScoket.cs");
    RequireMatch(socket,
        @"IsCurrentConnected\s*\(\s*state\.Socket\s*\)[\s\S]{0,240}?ReceivedDatagram",
        "socket layer raises data from a stale connection");

    foreach (var forbidden in new[]
             {
                 "GetPlayObject", "PlayObjects", "TPlayObject", "UserEngine",
                 "ProcessCompletions", "SendMsg", "SysMsg"
             })
    {
        Reject(callback, forbidden,
            $"receive callback crosses into player processing via {forbidden}");
    }
}

void CheckNativeHeartbeatLifecycle()
{
    var service = ReadYbDbClient();
    RequireMatch(service,
        @"ConnectionPulseIntervalMilliseconds\s*=\s*10_000",
        "native shared connection/heartbeat interval is not 10 seconds");
    RequireMatch(service,
        @"HeartbeatDisconnectThreshold\s*=\s*30",
        "native heartbeat disconnect threshold is not 30");
    var completions = ExtractMethod(service, "ProcessCompletions");
    RequireMatch(completions,
        @"queued\.Frame\.Ident\s*==\s*1100[\s\S]{0,180}?_missedHeartbeatCount",
        "Ident 1100 does not acknowledge the heartbeat");
    var pulse = ExtractMethod(service, "Pulse");
    RequireMatch(pulse,
        @"unchecked\s*\(\s*now\s*-\s*_lastConnectionPulse\s*\)",
        "connection pulse does not preserve native UInt32 wraparound timing");
    RequireMatch(pulse, @"\+\+_missedHeartbeatCount",
        "Pulse does not count unanswered heartbeats");
    Require(pulse, "_socket.Disconnect(currentSocket)",
        "Pulse does not disconnect after native heartbeat timeout");
    Require(pulse, "ConnectReplacingPending",
        "10-second retry does not replace a pending connection");
    Reject(pulse, "_socket.IsBusy",
        "an IsBusy guard suppresses the native 10-second replacement retry");
}

void CheckNativeQueueAndSendSemantics()
{
    var service = ReadYbDbClient();
    RequireMatch(service, @"SendFlushIntervalMilliseconds\s*=\s*150",
        "native 150ms send flush interval is missing");
    RequireMatch(service, @"SendAggregateCapacity\s*=\s*0x8000",
        "native 0x8000 send aggregate is missing");
    Reject(service, "MaximumPendingResponses",
        "the native completed-frame queue has no 4096-frame cap");
    Reject(service, "_pendingResponseCount",
        "the native completed-frame queue is not count-limited");

    var completions = ExtractMethod(service, "ProcessCompletions");
    RequireMatch(completions,
        @"responses\s*=\s*_responses\s*;[\s\S]{0,120}?_responses\s*=\s*new\s+Queue\s*<\s*QueuedResponse\s*>",
        "ProcessCompletions does not atomically detach a queue snapshot");
    RequireMatch(completions,
        @"catch\s*\(\s*Exception\s+[A-Za-z_]\w*\s*\)[\s\S]{0,180}?GoldIngot Cmd=",
        "completed frames are not isolated by the native per-frame exception boundary");

    var drain = ExtractMethod(service, "DrainOutbound");
    RejectMatch(drain, @"count\s*<\s*64",
        "outbound draining retains the non-native 64-frame cap");
    Require(drain, "_outbound.Count",
        "outbound flush does not snapshot the currently pending frames");
    Require(drain, "QueueSend",
        "outbound flush still uses the synchronous socket send path");

    var socket = Read("SystemModule", "Sockets", "AsyncSocketClient",
        "IClientScoket.cs");
    var queuedSend = ExtractMethod(socket, "QueueSend");
    Require(queuedSend, "new QueuedSendItem(buffer, completion)",
        "queued socket data is not transferred to an owned queue item");
    RequireMatch(queuedSend,
        @"SendOrder\.Reserve\(\)[\s\S]{0,160}?item\.Publish\(ticket\)[\s\S]{0,160}?Queue\.Enqueue\(item\)",
        "queued socket ticket is not published in reserve/enqueue order");
    Require(socket, "Buffer.BlockCopy(buffer, 0, Buffer, 0, buffer.Length)",
        "queued socket data is not copied into owned storage");
    var sender = ExtractMethod(socket, "ProcessQueuedSendsAsync");
    Require(sender, "SendAsync",
        "queued socket writes are not asynchronous");
    Require(sender, "0x2000",
        "queued socket writes do not use the native 8KiB submission size");
}

void CheckIdentityFieldMapping()
{
    var player = Read("GameSvr", "Players", "TPlayObject.Base.cs");
    RequireMatch(player, @"public\s+string\s+m_sLoginAccount\s*=\s*string\.Empty\s*;",
        "TPlayObject does not retain the compatibility login-account field");

    var userEngine = Read("GameSvr", "UsrSystem", "UsrEngn.cs");
    var makeNewHuman = ExtractMethod(userEngine, "ProcessHumans_MakeNewHuman");
    RequireMatch(makeNewHuman,
        @"PlayObject\.m_sLoginAccount\s*=\s*UserOpenInfo\.LoadUser\.sAccount\s*;",
        "UsrEngn does not populate the compatibility login-account field");
    Reject(makeNewHuman,
        "PlayObject.m_sUserID = UserOpenInfo.LoadUser.sAccount;",
        "UsrEngn overwrites the native PTID with the login account");
    var getHumData = ExtractMethod(userEngine, "GetHumData");
    RequireMatch(getHumData,
        @"PlayObject\.m_sUserID\s*=\s*HumData\.sAccount\s*;",
        "UsrEngn does not retain the native PTID in m_sUserID");

    var service = ReadYbDbClient();
    foreach (var methodName in new[]
             {
                 "RequestClearNickLinfu", "RequestCredit",
                 "RequestInitialCredit", "RequestQuestDiamond",
                 "RequestLingFuAccounting"
             })
    {
        var request = ExtractMethod(service, methodName);
        RequireMatch(request, @"Field0\s*=\s*player\.m_sUserID\s*,",
            $"{methodName} does not source the narrow PTID slot from m_sUserID");
        RequireMatch(request, @"Field11\s*=\s*player\.m_sUserID\s*,",
            $"{methodName} does not source the full PTID slot from m_sUserID");
        if (methodName is "RequestCredit" or "RequestInitialCredit")
            Require(request, "YbDbCreditProtocol.TryCreate",
                $"{methodName} does not use the strict native credit codec");
        else
            Require(request, "TryEncodeNativeIdentity",
                $"{methodName} does not use native CP936 byte truncation");
    }
}

void CheckAtomicOutboundEnqueue()
{
    var service = ReadYbDbClient();
    foreach (var methodName in new[]
             {
                 "RequestClearNickLinfu", "RequestQuestDiamond",
                 "RequestLingFuAccounting"
             })
    {
        var request = ExtractMethod(service, methodName);
        RequireMatch(request,
            @"lock\s*\(\s*_stateLock\s*\)\s*\{(?:(?!\})[\s\S])*?" +
            @"_started\s*==\s*0\s*\|\|\s*_connected\s*==\s*0" +
            @"(?:(?!\})[\s\S])*?" +
            @"_outbound\.Enqueue\s*\(\s*new\s+QueuedSend\s*\(",
            $"{methodName} does not validate and enqueue under one state lock");
    }

    var enqueueFrame = ExtractMethod(service, "private bool EnqueueFrame");
    RequireMatch(enqueueFrame,
        @"lock\s*\(\s*_stateLock\s*\)\s*\{(?:(?!\})[\s\S])*?" +
        @"IsCurrentSessionLocked\s*\((?:(?!\})[\s\S])*?" +
        @"_outbound\.Enqueue\s*\(\s*new\s+QueuedSend\s*\(",
        "EnqueueFrame validates generation and enqueues under one state lock");
}

void CheckCompletionDispatch()
{
    var service = ReadYbDbClient();
    var completions = ExtractMethod(service, "ProcessCompletions");
    Require(completions, ".Dequeue",
        "ProcessCompletions does not drain its detached receive snapshot");

    var userEngine = Read("GameSvr", "UsrSystem", "UsrEngn.cs");
    var processData = ExtractMethod(userEngine, "PrcocessData");
    RequireMatch(processData,
        @"YbDbClient\.Instance\.ProcessCompletions\s*\(\s*\)\s*;",
        "the UserEngine thread does not process YBDB completions");
}

void CheckClearNickCommand()
{
    var command = Read("GameSvr", "Command", "Commands",
        "ClearNickLinfuCommand.cs");
    RequireMatch(command,
        @"GameCommand\s*\(\s*\""ClearNickLinfu\""\s*,\s*\""[^\""\r\n]*\""\s*,\s*(?:\""\""\s*,\s*)?4\s*\)",
        "ClearNickLinfu must have permission 4 and no help/argument contract");

    var body = ExtractMethod(command, "ClearNickLinfu");
    Reject(body, "Params", "ClearNickLinfu still reads command parameters");
    Reject(body, "GetPlayObject", "ClearNickLinfu still targets another player");
    var requestCall = Regex.Match(body,
        @"YbDbClient\.Instance\.(?<method>[A-Za-z_]\w*)\s*\(\s*PlayObject\s*\)",
        RegexOptions.CultureInvariant);
    Assert(requestCall.Success,
        "ClearNickLinfu does not submit the executing player to YbDbClient");

    foreach (var forbidden in new[]
             {
                 "NativeCommandFailure.Report", "SysMsg", "SendMsg",
                 "成功清除所有的圣殿灵符", "成功清除所有的圣域灵符"
             })
    {
        Reject(body, forbidden,
            $"ClearNickLinfu reports a result before the 1303 response via {forbidden}");
    }

    var request = ExtractMethod(ReadYbDbClient(),
        requestCall.Groups["method"].Value);
    RequireMatch(request,
        @"_started\s*==\s*0\s*\|\|\s*_connected\s*==\s*0[\s\S]{0,180}?return\s+false\s*;",
        "a disconnected YBDB request is not rejected silently");
    Reject(request, "SendMsg", "YBDB request reports a result before its response");
    Reject(request, "SysMsg", "YBDB request reports a result before its response");
    Reject(request, "RM_SYSMESSAGE",
        "YBDB request emits a system message before its response");
}

void CheckQuestDiamondRequest()
{
    var service = ReadYbDbClient();
    var request = ExtractMethod(service, "RequestQuestDiamond");
    RequireMatch(request,
        @"new\s+YbDbLegacy77Frame\s*\(\s*0\s*,\s*amount\s*,\s*122\s*,\s*payload\s*\)",
        "ClientQuestGetDiam is not encoded as QueryId=0/Param=amount/Ident=122");
    RequireMatch(request,
        @"_started\s*==\s*0\s*\|\|\s*_connected\s*==\s*0[\s\S]{0,180}?return\s+false\s*;",
        "disconnected ClientQuestGetDiam is not rejected");
    Require(request, "_connectionGeneration",
        "ClientQuestGetDiam is not bound to the current connection generation");
    foreach (var forbidden in new[]
             {
                 "SendMsg", "SysMsg", "TakeNativeDiamond", "GetNativeDiamondCount",
                 "m_nGameGold", "m_nGamePoint", "m_ScriptVVars", "m_ScriptSVars"
             })
    {
        Reject(request, forbidden,
            $"dormant ClientQuestGetDiam request performs local work via {forbidden}");
    }

    var bridge = Read("GameSvr", "ScriptSystem", "PasEngine", "PasApiBridge.cs");
    RequireMatch(bridge,
        @"case\s+\""clientquestgetdiam\""\s*:\s*return\s+RejectUnsupportedNativeApi\(out\s+result\)\s*;",
        "ClientQuestGetDiam PAS surface was opened before Ident 1122 closure");
}

void CheckCreditContract()
{
    var service = ReadYbDbClient();
    var request = ExtractMethod(service, "RequestCredit");
    Require(request, "YbDbCreditProtocol.TryCreateRefreshRequest",
        "RefreshCredit does not use the strict 103 request codec");
    Assert(Count(request, "m_sUserID") >= 2,
        "RefreshCredit PTID is not copied into both native identity slots");
    Reject(request, "m_sLoginAccount",
        "RefreshCredit incorrectly sources a wire identity slot from m_sLoginAccount");
    Require(request, "m_boNativeFirstUsedGiftQualified",
        "RefreshCredit Param bit16 does not use the dedicated qualification bit");
    RequireMatch(request, @"unchecked\s*\(\s*\(ushort\)player\.m_nPayMent\s*\)",
        "RefreshCredit Param low16 is not the native payment word");
    Reject(request, "m_CreditCard",
        "RefreshCredit request reuses local CreditCard state");

    Require(service, "WeakReference<TPlayObject>",
        "RefreshCredit pending identity retains or loses the player object");
    Require(service, "List<PendingCreditEpoch>",
        "RefreshCredit retries are not collapsed into identity epochs");
    Require(service, "OutstandingCount",
        "RefreshCredit retries have no bounded per-identity counter");
    Require(service, "MaxPendingCreditEpochsPerRole",
        "RefreshCredit relog identity epochs have no hard per-role bound");
    Reject(service, "Queue<PendingCreditRequest>",
        "RefreshCredit still allocates one managed identity per 15-second retry");
    Assert(Count(service, "_creditRequests.Clear();") >= 4,
        "RefreshCredit pending identities are not cleared at every session boundary");
    var completion = ExtractMethod(service, "ProcessCreditResponse");
    Require(completion, "YbDbCreditProtocol.TryDecodeResponse",
        "1103 does not use strict 32-byte decoding");
    Require(completion, "TryTakeCreditRequest",
        "1103 does not require a pending request identity");
    Require(completion, "GetPlayObject(snapshot.RoleName)",
        "1103 does not resolve the response role");
    Require(completion, "player.ObjectId != request.ObjectId",
        "1103 does not verify the current object id");
    Require(completion, "ReferenceEquals(player, requestedPlayer)",
        "1103 does not verify the exact online player instance");
    Require(completion, "player.m_sUserID, request.Ptid",
        "1103 does not verify PTID");
    Reject(completion, "request.LoginAccount",
        "1103 retains a non-native second account identity");
    Require(completion, "ApplyNativeYb1103Snapshot",
        "1103 does not apply the independent account snapshot");
    Reject(completion, "m_CreditCard",
        "1103 response reuses local CreditCard state");
    var take = ExtractMethod(service, "TryTakeCreditRequest");
    Require(take, "generation != _connectionGeneration",
        "1103 pending identity is not isolated by socket generation");

    var bridge = Read("GameSvr", "ScriptSystem", "PasEngine", "PasApiBridge.cs");
    Reject(bridge, "YbDbClient.Instance.RequestCredit",
        "RefreshCredit PAS entry was opened before the external authority closed");
    Reject(bridge, "RequestInitialCredit",
        "initial credit loading was incorrectly exposed through PAS");
}

void CheckCreditLoginLifecycle()
{
    var protocol = Read("SystemModule", "Packet", "YbDbCreditProtocol.cs");
    RequireMatch(protocol, @"InitialQueryId\s*=\s*0\s*;",
        "initial credit QueryId is not zero");
    var initialCodec = ExtractMethod(protocol, "TryCreateInitialRequest");
    Require(initialCodec, "InitialQueryId",
        "initial credit codec does not use QueryId zero");

    var service = ReadYbDbClient();
    var initialRequest = ExtractMethod(service, "RequestInitialCredit");
    Require(initialRequest, "TryCreateInitialRequest",
        "initial login does not use the strict QueryId-zero codec");
    Require(initialRequest, "EnqueueCreditRequest",
        "initial login bypasses the generation-bound pending request queue");
    var enqueue = ExtractMethod(service, "EnqueueCreditRequest");
    RequireMatch(enqueue,
        @"_started\s*==\s*0\s*\|\|\s*_connected\s*==\s*0[\s\S]{0,180}?return\s+false\s*;",
        "disconnected initial credit request is not rejected silently");
    Reject(initialRequest, "SendMsg",
        "initial credit request emits UI before a response");
    Reject(initialRequest, "SysMsg",
        "initial credit request emits UI before a response");
    Require(enqueue, "IsSameCreditRequestIdentity",
        "same-player 15-second retries do not reuse their identity epoch");
    Require(enqueue, "OutstandingCount++",
        "same-player 15-second retries still allocate pending identities");
    RequireMatch(enqueue,
        @"requests\.Count\s*>=\s*MaxPendingCreditEpochsPerRole[\s\S]{0,100}?return\s+false",
        "relog identity epoch overflow is not fail-closed");

    var take = ExtractMethod(service, "TryTakeCreditRequest");
    Require(take, "--epoch.OutstandingCount",
        "1103 does not consume collapsed repeated requests in FIFO order");

    var player = Read("GameSvr", "Players", "TPlayObject.NativeYbCredit.cs");
    var begin = ExtractMethod(player, "BeginNativeYbCreditLoad");
    RequireMatch(begin,
        @"RequestInitialCredit\s*\(\s*this\s*\)\s*&&\s*bo6AB[\s\S]{0,100}?m_boNativeYbAccountLoaded\s*=\s*true",
        "reconnect login does not use the native send-success loaded shortcut");
    var initialTick = begin.IndexOf("m_dwNativeYbInitialRetryTick = currentTick",
        StringComparison.Ordinal);
    var refreshTick = begin.IndexOf("m_dwNativeYbRefreshTick = currentTick",
        StringComparison.Ordinal);
    Assert(initialTick >= 0 && refreshTick > initialTick,
        "login does not initialize the 15s and 10s ticks in native order");

    var periodic = ExtractMethod(player, "RunNativeYbCreditLoad");
    Require(periodic, "NativeYbInitialRetryIntervalMilliseconds",
        "periodic initial load is not throttled by the native 15-second interval");
    var advance = periodic.IndexOf(
        "m_dwNativeYbInitialRetryTick = currentTick", StringComparison.Ordinal);
    var loadedGate = periodic.IndexOf("!m_boNativeYbAccountLoaded",
        StringComparison.Ordinal);
    var request = periodic.IndexOf("RequestInitialCredit(this)",
        StringComparison.Ordinal);
    Assert(advance >= 0 && loadedGate > advance && request > loadedGate,
        "periodic initial load does not advance tick before the loaded gate/request");

    var logon = ExtractMethod(Read("GameSvr", "Players", "TPlayObject.Base.cs"),
        "UserLogon");
    var beginCall = logon.IndexOf("BeginNativeYbCreditLoad", StringComparison.Ordinal);
    var authCall = logon.IndexOf("SendNativeAuthenticationStatus", StringComparison.Ordinal);
    var pasCall = logon.IndexOf("TryCallScriptLabel", StringComparison.Ordinal);
    Assert(beginCall >= 0 && authCall > beginCall && pasCall > authCall,
        "initial credit request is not ordered before auth/honor/PAS login tail");

    var run = ExtractMethod(Read("GameSvr", "Players", "TPlayObject.Message.cs"),
        "Run");
    Require(run, "RunNativeYbCreditLoad",
        "TPlayObject.Run does not execute the 15-second initial load retry");
}

void CheckNativeLogoutDormantBoundary()
{
    var protocol = Read("SystemModule", "Packet", "YbDbLogoutProtocol.cs");
    RequireMatch(protocol, @"RequestIdent\s*=\s*104",
        "native logout protocol Ident is not 104");
    RequireMatch(protocol, @"QueryId\s*=\s*0",
        "native logout protocol QueryId is not zero");
    RequireMatch(protocol, @"Param\s*=\s*0",
        "native logout protocol Param is not zero");
    Require(protocol, "TryEncodeNativeIdentity",
        "native logout protocol does not emit the 64-byte identity");
    Reject(protocol, "ResponseIdent",
        "native logout invented a response Ident");
    Reject(protocol, "1104",
        "native logout incorrectly treats 1104 as an acknowledgement");
    Require(protocol, "EvaluateSaveInvocation",
        "native logout has no dormant original save-route model");
    Require(protocol, "BlocksSaveOnLogoutFailure => false",
        "native logout incorrectly blocks character persistence");
    Require(protocol, "RegistersPendingRequest => false",
        "native logout invented pending state");
    Require(protocol, "WaitsForAcknowledgement => false",
        "native logout invented an acknowledgement wait");

    var service = ReadYbDbClient();
    Reject(service, "RequestPlayerLogout",
        "104 runtime sender was opened without the authoritative 6108 service");
    Reject(service, "YbDbLogoutProtocol",
        "dormant 104 codec was wired into YbDbClient");

    var player = Read("GameSvr", "Players", "TPlayObject.NativeYbCredit.cs");
    Reject(player, "TryBeginNativeYbLogout",
        "player carries live 104 one-shot state without authority");
    Reject(player, "_nativeYbLogoutAttempted",
        "player carries live 104 state without authority");

    var userEngine = Read("GameSvr", "UsrSystem", "UsrEngn.cs");
    Reject(userEngine, "RequestPlayerLogout",
        "character persistence emits dormant 104");
    Reject(userEngine, "SaveFinalHumanRcd",
        "unsupported final-save 104 route remains live");
    Reject(userEngine, "notifyNativeLogout",
        "unsupported final-save routing flag remains live");

    var process = ExtractMethod(userEngine, "ProcessPlayObjectData");
    Equal(1, Count(process, "SaveHumanRcd(PlayObject)"),
        "periodic save no longer uses the ordinary persistence path");
    Equal(1, Count(process, "SelectNativeExitSaveMode(PlayObject)"),
        "final player save does not use the native exit-mode selector");
    var exitMode = ExtractMethod(userEngine, "SelectNativeExitSaveMode");
    Require(exitMode, "playObject.m_boSwitchData",
        "exit-mode selector does not prioritize switch-data persistence");
    Require(exitMode, "return 2;",
        "switch-data exit does not select native mode 2");
    Require(exitMode, "playObject.m_boReconnection",
        "exit-mode selector does not distinguish reconnection");
    Require(exitMode, "(ushort)1 : (ushort)3",
        "exit-mode selector does not map reconnection/ordinary exits to modes 1/3");
    var aiProcess = ExtractMethod(userEngine, "ProcessAiPlayObjectData");
    Equal(1, Count(aiProcess, "SaveHumanRcd(PlayObject, 3)"),
        "final AI-player save does not use exact mode-3 persistence");
    Reject(process, "SaveFinalHumanRcd",
        "final ghost cleanup still exposes unsupported 104 routing");

    var shutdown = ExtractMethod(Read("GameSvr", "GameServer.cs"), "Stop");
    Require(shutdown, "SaveHumanRcd(player, 3)",
        "shutdown does not use the ordinary dormant persistence path");
    Reject(shutdown, "SaveFinalHumanRcd",
        "shutdown emits unsupported native logout");

    var completions = ExtractMethod(service, "ProcessCompletions");
    Reject(completions, "1104",
        "ProcessCompletions incorrectly dispatches 1104 as logout ACK");
}

void CheckResponseMessages()
{
    var service = ReadYbDbClient();
    var completions = ExtractMethod(service, "ProcessCompletions");
    Require(completions, "1303", "ProcessCompletions does not filter ident 1303");

    const string temple = "成功清除所有的圣殿灵符";
    const string realm = "成功清除所有的圣域灵符";
    RequireMatch(completions,
        @"(?:case\s+5\s*:|\b5\s*=>|QueryId\s*==\s*5)[\s\S]{0,260}?" + temple,
        "QueryId 5 is not mapped to the exact original message");
    var explicitSix = Regex.IsMatch(completions,
        @"(?:case\s+6\s*:|\b6\s*=>|QueryId\s*==\s*6)[\s\S]{0,260}?" + realm,
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    var filteredTernarySix = Regex.IsMatch(completions,
        @"QueryId\s+is\s+not\s+5\s+and\s+not\s+6[\s\S]{0,520}?" +
        @"QueryId\s*==\s*5[\s\S]{0,160}?" + temple +
        @"[\s\S]{0,160}?:\s*\""" + realm + @"\""",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    Assert(explicitSix || filteredTernarySix,
        "QueryId 6 is not mapped to the exact original message");
    RequireMatch(completions,
        @"(?<player>[A-Za-z_]\w*)\.SendMsg\s*\(\s*\k<player>\s*,\s*" +
        @"Grobal2\.RM_SYSMESSAGE\s*,\s*0\s*,\s*0xDB\s*,\s*0xFF\s*,\s*0\s*,",
        "1303 success is not sent as RM_SYSMESSAGE with colors 0xDB/0xFF");
    Reject(completions, ".SysMsg(",
        "1303 success uses SysMsg, which can alter the original message surface");

    Equal(1, Count(service, temple),
        "temple success text must exist only in completion handling");
    Equal(1, Count(service, realm),
        "realm success text must exist only in completion handling");
}

void CheckNativeForgeModeLifecycle()
{
    var share = Read("GameSvr", "M2Share.cs");
    RequireMatch(share,
        @"public\s+static\s+volatile\s+bool\s+g_boYbDoubleForge\s*=\s*false\s*;",
        "forge-mode bit2 owner is not a process-runtime M2 global defaulting to single");

    var service = ReadYbDbClient();
    var connected = ExtractMethod(service, "SocketConnected");
    var request100 = connected.IndexOf(
        "EnqueueFrame(0, M2Share.nServerIndex + 1, 100",
        StringComparison.Ordinal);
    var request400 = connected.IndexOf(
        "EnqueueFrame(_areaId, _groupId, 400", StringComparison.Ordinal);
    var create108 = connected.IndexOf(
        "YbDbForgeModeProtocol.CreateRequest", StringComparison.Ordinal);
    var request108 = connected.IndexOf(
        "EnqueueFrame(forgeMode.QueryId, forgeMode.Param, forgeMode.Ident",
        StringComparison.Ordinal);
    Assert(request100 >= 0 && request400 > request100
        && create108 > request400 && request108 > create108,
        "SocketConnected does not preserve native 100 -> 400 -> 108 order");
    Require(connected, "M2Share.g_boYbDoubleForge",
        "request 108 does not read the M2 runtime forge-mode bit");

    var completions = ExtractMethod(service, "ProcessCompletions");
    Require(completions, "YbDbForgeModeProtocol.ResponseIdent",
        "ProcessCompletions does not dispatch response 1108");
    Require(completions, "YbDbForgeModeProtocol.TryDecodeResponse",
        "response 1108 does not use the exact protocol decoder");
    Require(completions,
        "M2Share.g_boYbDoubleForge = forgeMode.DoubleForging",
        "response 1108 does not authoritatively update the runtime bit");
    Require(completions, "M2Share.MainOutMessage(forgeMode.ConsoleMessage",
        "response 1108 does not emit the native mode log");

    var protocol = Read("SystemModule", "Packet", "YbDbForgeModeProtocol.cs");
    Require(protocol, "==> 开启元宝双倍锻造", "double-mode log text drifted");
    Require(protocol, "==> 元宝单倍锻造", "single-mode log text drifted");

    foreach (var methodName in new[] { "Start", "Stop", "SocketDisconnected" })
    {
        Reject(ExtractMethod(service, methodName), "g_boYbDoubleForge",
            $"{methodName} invents a forge-mode reset absent from native lifecycle");
    }
}

void CheckCommandAuditOwnership()
{
    var audit = Read("AuditTools", "CommandAuditCheck", "Program.cs");
    var protectedStart = audit.IndexOf("var protectedFiles", StringComparison.Ordinal);
    var protectedEnd = audit.IndexOf("foreach (var fileName in protectedFiles)",
        StringComparison.Ordinal);
    Assert(protectedStart >= 0 && protectedEnd > protectedStart,
        "could not isolate CommandAuditCheck protectedFiles");
    var protectedFiles = audit[protectedStart..protectedEnd];
    Reject(protectedFiles, "ClearNickLinfuCommand.cs",
        "ClearNickLinfu remains in CommandAuditCheck protectedFiles");
}

string ReadYbDbClient() => Read("GameSvr", "Services", "YbDbClient.cs");

string Read(params string[] relativeParts)
{
    var path = relativeParts.Aggregate(root, Path.Combine);
    if (!File.Exists(path))
        throw new FileNotFoundException($"required source is missing: {path}", path);
    return File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
}

static string ExtractMethod(string source, string methodName)
{
    var declaration = Regex.Match(source,
        $@"\b{Regex.Escape(methodName)}\s*\([^;{{}}]*\)\s*\{{",
        RegexOptions.CultureInvariant);
    if (!declaration.Success)
        throw new InvalidOperationException($"method {methodName} was not found");

    var openBrace = declaration.Index + declaration.Length - 1;
    var depth = 0;
    for (var i = openBrace; i < source.Length; i++)
    {
        if (source[i] == '{') depth++;
        if (source[i] != '}') continue;
        depth--;
        if (depth == 0) return source[openBrace..(i + 1)];
    }
    throw new InvalidOperationException($"method {methodName} has no closing brace");
}

static string Window(string source, int center, int before, int after)
{
    var start = Math.Max(0, center - before);
    var length = Math.Min(source.Length - start, before + after);
    return source.Substring(start, length);
}

static int Count(string source, string value)
{
    var count = 0;
    for (var offset = 0;;)
    {
        var index = source.IndexOf(value, offset, StringComparison.Ordinal);
        if (index < 0) return count;
        count++;
        offset = index + value.Length;
    }
}

static string FindRepositoryRoot()
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        for (var directory = new DirectoryInfo(start);
             directory != null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameSvr", "GameSvr.csproj")))
                return directory.FullName;
        }
    }
    throw new DirectoryNotFoundException(
        "repository root containing GameSvr/GameSvr.csproj was not found");
}

static void Require(string source, string value, string message)
{
    if (!source.Contains(value, StringComparison.Ordinal))
        throw new InvalidOperationException(message);
}

static void Reject(string source, string value, string message)
{
    if (source.Contains(value, StringComparison.Ordinal))
        throw new InvalidOperationException(message);
}

static void RequireMatch(string source, string pattern, string message)
{
    if (!Regex.IsMatch(source, pattern,
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))
        throw new InvalidOperationException(message);
}

static void RejectMatch(string source, string pattern, string message)
{
    if (Regex.IsMatch(source, pattern,
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))
        throw new InvalidOperationException(message);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual, string name) where T : IEquatable<T>
{
    if (!expected.Equals(actual))
        throw new InvalidOperationException(
            $"{name}: expected {expected}, got {actual}");
}
