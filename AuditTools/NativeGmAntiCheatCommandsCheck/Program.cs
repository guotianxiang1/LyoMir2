using System.Collections;
using System.Reflection;
using GameSvr;
using GameSvr.CommandSystem;
using SystemModule;

// Contract check for the ANTICHEAT/IP/SECURITY GM command family model
// (GameSvr/Services/NativeGmAntiCheatCommands.cs), locked against the Hex-Rays-verified original
// dispatcher sub_622820 (single switch, table jpt_622B15 @0x00622B1C) in the unpacked M2Server image.
// Family 09 = 15 commands, ALL real handlers (0 no-ops).

try
{
    VerifyDispatcherConstants();
    VerifyRegistry();
    VerifyNoNoOps();
    VerifyForwardContracts();
    VerifyClearHackFlag();
    VerifyHackFlag();
    VerifyFlagCommandsRuntime();
    VerifyHackerpunish();
    VerifyClientVersion();
    VerifySetIpHumanMaxCount();
    VerifyReloadWhiteList();
    VerifyViewMonitor();
    VerifyReloadSmsUserList();

    Console.WriteLine(
        "PASS NativeGmAntiCheatCommandsCheck dispatcher=sub_622820 table=0x622B1C max=750 family=09 " +
        "commands=15 impl=15 noop=0 " +
        "recovered=ClearHackFlag/HackFlag " +
        "ladders=Hackerpunish/ClientVersion/SetIpHumanMaxCount/ReloadWhiteList/ViewMonitor/ReloadSmsUserList " +
        "deferred=MapUserInfo/IPHackFlag/IPOutSay/IPHumNum/IpBlackRoom/kickOutPtid/SetMonitor");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"NativeGmAntiCheatCommandsCheck FAIL: {ex}");
    return 1;
}

static void Assert(bool cond, string msg)
{
    if (!cond) throw new Exception(msg);
}

// The single generic equality helper used throughout (no overloaded local Equal).
static void Equal<T>(T actual, T expected, string msg)
{
    if (!System.Collections.Generic.EqualityComparer<T>.Default.Equals(actual, expected))
        throw new Exception($"{msg}: expected {expected}, got {actual}");
}

static void VerifyDispatcherConstants()
{
    Equal(NativeGmAntiCheatCommands.DispatcherEa, 0x00622820u, "dispatcher ea");
    Equal(NativeGmAntiCheatCommands.IndexLookupEa, 0x00621F28u, "index lookup ea");
    Equal(NativeGmAntiCheatCommands.JumpTableEa, 0x00622B1Cu, "jump table ea");
    Equal(NativeGmAntiCheatCommands.SwitchMaxIndex, 750, "switch max index");
    Equal(NativeGmAntiCheatCommands.DefaultCaseEa, 0x0062B648u, "default case ea (no-op sink #1)");
    Equal(NativeGmAntiCheatCommands.EmptyBodyNoOpEa, 0x0062B64Cu, "empty-body ea (no-op sink #2)");

    Equal(NativeGmAntiCheatCommands.ColorEcho, 0x38FF, "echo colour");
    Equal(NativeGmAntiCheatCommands.ColorNotice, 0xFFDB, "notice colour");

    Equal(NativeGmAntiCheatCommands.HackPunishModeGlobalEa, 0x007D6010u, "punish-mode global");
    Equal(NativeGmAntiCheatCommands.HackPunishModeNamesEa, 0x007D6FECu, "punish-mode names");
    Equal(NativeGmAntiCheatCommands.ClientVersionGlobalEa, 0x007D60D8u, "client-version global");
    Equal(NativeGmAntiCheatCommands.PlayerListEa, 0x007D6D50u, "player list global");
    Equal(NativeGmAntiCheatCommands.MonitorListEa, 0x007D62A4u, "monitor list global");
}

static void VerifyRegistry()
{
    // (command, name, index, perm, caseAddr, coreAddr, shape, coreStringArgs, dispatcherSendsSysMsg)
    (GmAntiCheatCommand cmd, string name, int idx, int perm, uint caseEa, uint coreEa,
        GmAntiCheatShape shape, int args, bool msg)[] expected =
    {
        (GmAntiCheatCommand.MapUserInfo,        "MapUserInfo",        74,  3, 0x00624D3B, 0x006D6698, GmAntiCheatShape.ForwardOnly,      0, false),
        (GmAntiCheatCommand.ClearHackFlag,      "ClearHackFlag",      151, 4, 0x006255EE, 0x006D321C, GmAntiCheatShape.ForwardOnly,      1, false),
        (GmAntiCheatCommand.Hackerpunish,       "Hackerpunish",       152, 4, 0x006255FE, 0x00713890, GmAntiCheatShape.DispatcherLadder, 0, true),
        (GmAntiCheatCommand.HackFlag,           "HackFlag",           153, 4, 0x00625690, 0x006D440C, GmAntiCheatShape.ForwardOnly,      2, false),
        (GmAntiCheatCommand.IPHackFlag,         "IPHackFlag",         154, 4, 0x006256A3, 0x006D45C8, GmAntiCheatShape.ForwardOnly,      2, false),
        (GmAntiCheatCommand.IPOutSay,           "IPOutSay",           158, 4, 0x006258AC, 0x006D4CA4, GmAntiCheatShape.ForwardOnly,      2, false),
        (GmAntiCheatCommand.IPHumNum,           "IPHumNum",           160, 4, 0x006256B6, 0x006E3498, GmAntiCheatShape.ParseIntThenCore, 0, false),
        (GmAntiCheatCommand.IpBlackRoom,        "IpBlackRoom",        163, 4, 0x00625C98, 0x006D49E4, GmAntiCheatShape.ForwardOnly,      2, false),
        (GmAntiCheatCommand.ClientVersion,      "ClientVersion",      180, 4, 0x00625969, 0x00655954, GmAntiCheatShape.DispatcherLadder, 0, true),
        (GmAntiCheatCommand.KickOutPtid,        "kickOutPtid",        488, 4, 0x00629228, 0x00651CBC, GmAntiCheatShape.ForwardOnly,      0, false),
        (GmAntiCheatCommand.SetIpHumanMaxCount, "SetIpHumanMaxCount", 501, 4, 0x006293B5, 0x007130E8, GmAntiCheatShape.DispatcherLadder, 0, false),
        (GmAntiCheatCommand.ReloadWhiteList,    "ReloadWhiteList",    505, 4, 0x00629465, 0x007130E8, GmAntiCheatShape.DispatcherLadder, 0, false),
        (GmAntiCheatCommand.SetMonitor,         "SetMonitor",         510, 3, 0x006294EB, 0x0079F908, GmAntiCheatShape.ForwardOnly,      2, false),
        (GmAntiCheatCommand.ViewMonitor,        "ViewMonitor",        511, 3, 0x00629502, 0x0079F5C4, GmAntiCheatShape.DispatcherLadder, 1, true),
        (GmAntiCheatCommand.ReloadSmsUserList,  "ReloadSmsUserList",  516, 4, 0x006294A9, 0x006556F4, GmAntiCheatShape.DispatcherLadder, 0, true),
    };

    Equal(NativeGmAntiCheatCommands.All.Count, expected.Length, "registry count");
    Equal(expected.Length, 15, "family size");

    foreach (var e in expected)
    {
        var info = NativeGmAntiCheatCommands.Info(e.cmd);
        Equal(info.Name, e.name, $"{e.cmd} name");
        Equal(info.DispatchIndex, e.idx, $"{e.cmd} index");
        Equal(info.RequiredPermission, e.perm, $"{e.cmd} perm");
        Equal(info.CaseAddress, e.caseEa, $"{e.cmd} case addr");
        Equal(info.CoreAddress, e.coreEa, $"{e.cmd} core addr");
        Equal(info.Shape, e.shape, $"{e.cmd} shape");
        Equal(info.CoreStringArgs, e.args, $"{e.cmd} core string args");
        Equal(info.DispatcherSendsSysMsg, e.msg, $"{e.cmd} dispatcher SysMsg");
        Assert(info.Implemented, $"{e.cmd} implemented");
        Equal(info.CoreBodyDeferred,
            e.cmd != GmAntiCheatCommand.ClearHackFlag &&
            e.cmd != GmAntiCheatCommand.HackFlag,
            $"{e.cmd} core body deferred");
        Assert(info.DispatchIndex >= 0 && info.DispatchIndex <= NativeGmAntiCheatCommands.SwitchMaxIndex,
            $"{e.cmd} index in switch range");
        Assert(info.CaseAddress != NativeGmAntiCheatCommands.DefaultCaseEa, $"{e.cmd} not on no-op sink #1");
        Assert(info.CaseAddress != NativeGmAntiCheatCommands.EmptyBodyNoOpEa, $"{e.cmd} not on no-op sink #2");
    }
}

static void VerifyNoNoOps()
{
    // Family 09 is the only fully-implemented family: every command has a distinct real case body.
    Equal(NativeGmAntiCheatCommands.NoOpCount, 0, "family 09 no-op count");
    foreach (var info in NativeGmAntiCheatCommands.All)
    {
        Assert(info.Implemented, $"{info.Command} implemented");
        Equal(info.CoreBodyDeferred,
            info.Command != GmAntiCheatCommand.ClearHackFlag &&
            info.Command != GmAntiCheatCommand.HackFlag,
            $"{info.Command} recovered/deferred state");
    }
}

static void VerifyForwardContracts()
{
    // ForwardOnly / ParseIntThenCore commands: recognized, gated, forward to a deferred core, no dispatcher SysMsg.
    (GmAntiCheatCommand cmd, uint coreEa, int args, bool parsesInt,
        bool deferred)[] fwd =
    {
        (GmAntiCheatCommand.MapUserInfo,   0x006D6698, 0, false, true),
        (GmAntiCheatCommand.ClearHackFlag, 0x006D321C, 1, false, false),
        (GmAntiCheatCommand.HackFlag,      0x006D440C, 2, false, false),
        (GmAntiCheatCommand.IPHackFlag,    0x006D45C8, 2, false, true),
        (GmAntiCheatCommand.IPOutSay,      0x006D4CA4, 2, false, true),
        (GmAntiCheatCommand.IPHumNum,      0x006E3498, 0, true,  true),
        (GmAntiCheatCommand.IpBlackRoom,   0x006D49E4, 2, false, true),
        (GmAntiCheatCommand.KickOutPtid,   0x00651CBC, 0, false, true),
        (GmAntiCheatCommand.SetMonitor,    0x0079F908, 2, false, true),
    };
    foreach (var f in fwd)
    {
        var c = NativeGmAntiCheatCommands.ForwardContract(f.cmd);
        Equal(c.CoreAddress, f.coreEa, $"{f.cmd} forward core");
        Equal(c.CoreStringArgs, f.args, $"{f.cmd} forward args");
        Equal(c.ParsesLeadingInt, f.parsesInt, $"{f.cmd} forward parses int");
        Equal(c.CoreBodyDeferred, f.deferred,
            $"{f.cmd} forward recovered/deferred state");
        Assert(!c.DispatcherSendsSysMsg, $"{f.cmd} forward silent");
    }

    // A dispatcher-ladder command must NOT be routed through the forward path.
    var threw = false;
    try { NativeGmAntiCheatCommands.ForwardContract(GmAntiCheatCommand.Hackerpunish); }
    catch (InvalidOperationException) { threw = true; }
    Assert(threw, "ladder command rejected by forward path");
}

static void VerifyClearHackFlag()
{
    var empty = NativeGmClearHackFlag.Evaluate(string.Empty,
        targetFound: false, targetTier: 0, targetPermission: 0,
        invokerCurrentDay: 123);
    Equal(empty.Branch, ClearHackFlagBranch.TargetNameEmpty,
        "ClearHackFlag empty-name branch");
    Assert(!empty.MutatesTarget && !empty.SendsSysMsg,
        "ClearHackFlag empty name is silent and non-mutating");

    var missing = NativeGmClearHackFlag.Evaluate("Nobody",
        targetFound: false, targetTier: 0, targetPermission: 0,
        invokerCurrentDay: 123);
    Equal(missing.Branch, ClearHackFlagBranch.TargetMissing,
        "ClearHackFlag missing-target branch");
    Assert(!missing.MutatesTarget && missing.SendsSysMsg,
        "ClearHackFlag missing target replies without mutation");
    Equal(missing.MessageColor, NativeGmAntiCheatCommands.ColorEcho,
        "ClearHackFlag missing-target color");
    Equal(missing.Message, "Nobody 不在线或不在本GS服务器",
        "ClearHackFlag exact missing-target text");

    var clear = NativeGmClearHackFlag.Evaluate("Target",
        targetFound: true, targetTier: 2, targetPermission: 0,
        invokerCurrentDay: 123);
    Equal(clear.Branch, ClearHackFlagBranch.Cleared,
        "ClearHackFlag clear branch");
    Assert(clear.MutatesTarget && clear.ClearsQuizState &&
           clear.RemovesTimedState25,
        "ClearHackFlag clear side effects");
    Equal(clear.StoredTier, (byte)0, "ClearHackFlag clear tier");
    Equal(clear.StoredExpiryDay, 0, "ClearHackFlag clear expiry");
    Equal(clear.MessageColor, NativeGmAntiCheatCommands.ColorNotice,
        "ClearHackFlag clear color");
    Equal(clear.Message, "清除 Target 使用非法外挂的限制成功",
        "ClearHackFlag exact clear text");

    var none = NativeGmClearHackFlag.Evaluate("Target",
        targetFound: true, targetTier: 0, targetPermission: 3,
        invokerCurrentDay: 123);
    Equal(none.Branch, ClearHackFlagBranch.NoRestriction,
        "ClearHackFlag ordinary unflagged branch");
    Assert(!none.MutatesTarget && !none.ClearsQuizState &&
           !none.RemovesTimedState25,
        "ClearHackFlag ordinary unflagged target stays unchanged");
    Equal(none.MessageColor, NativeGmAntiCheatCommands.ColorNotice,
        "ClearHackFlag no-restriction color");
    Equal(none.Message, "Target 没有受到外挂惩罚机制的限制",
        "ClearHackFlag exact no-restriction text");

    var privileged = NativeGmClearHackFlag.Evaluate("Target",
        targetFound: true, targetTier: 0, targetPermission: 4,
        invokerCurrentDay: 123);
    Equal(privileged.Branch, ClearHackFlagBranch.AppliedToPrivilegedTarget,
        "ClearHackFlag privileged native oddity branch");
    Assert(privileged.MutatesTarget && !privileged.ClearsQuizState &&
           !privileged.RemovesTimedState25,
        "ClearHackFlag privileged branch only writes penalty fields");
    Equal(privileged.StoredTier, (byte)3,
        "ClearHackFlag privileged tier");
    Equal(privileged.StoredExpiryDay, 123,
        "ClearHackFlag privileged expiry uses invoker day");
    Equal(privileged.MessageColor, NativeGmAntiCheatCommands.ColorEcho,
        "ClearHackFlag privileged color");
    Equal(privileged.Message, "设置 Target 使用非法外挂成功",
        "ClearHackFlag exact privileged text");
    Assert(!privileged.CoreBodyDeferred,
        "ClearHackFlag recovered core is not deferred");
}

static void VerifyHackFlag()
{
    var usage = NativeGmHackFlag.Evaluate(string.Empty, "9",
        targetFound: false, currentDay: 100);
    Equal(usage.Branch, HackFlagBranch.Usage, "HackFlag empty-name branch");
    Assert(!usage.MutatesTarget, "HackFlag usage does not mutate target");
    Assert(usage.SendsSysMsg, "HackFlag usage replies");
    Equal(usage.MessageColor, NativeGmAntiCheatCommands.ColorNotice,
        "HackFlag usage color");
    Equal(usage.Message, NativeGmHackFlag.UsageMessage,
        "HackFlag exact usage text");

    var missing = NativeGmHackFlag.Evaluate("Nobody", "3",
        targetFound: false, currentDay: 100);
    Equal(missing.Branch, HackFlagBranch.TargetMissing,
        "HackFlag missing-target branch");
    Equal(missing.ParsedDays, 3, "HackFlag parses before lookup result");
    Assert(!missing.MutatesTarget && !missing.SendsSysMsg,
        "HackFlag missing target is silent and non-mutating");

    var clear = NativeGmHackFlag.Evaluate("Target", "not-a-number",
        targetFound: true, currentDay: 100);
    Equal(clear.Branch, HackFlagBranch.Cleared,
        "HackFlag invalid days defaults to clear");
    Equal(clear.ParsedDays, 0, "HackFlag invalid days default");
    Equal(clear.StoredTier, (byte)0, "HackFlag clear tier");
    Equal(clear.StoredExpiryDay, 0, "HackFlag clear expiry");
    Equal(clear.Message, "清除 Target 使用非法外挂的限制成功",
        "HackFlag exact clear text");
    Equal(clear.MessageColor, NativeGmAntiCheatCommands.ColorNotice,
        "HackFlag clear color");

    var missingDays = NativeGmHackFlag.Evaluate("Target", string.Empty,
        targetFound: true, currentDay: 100);
    Equal(missingDays.Branch, HackFlagBranch.Cleared,
        "HackFlag missing days defaults to clear");

    var explicitZero = NativeGmHackFlag.Evaluate("Target", "0",
        targetFound: true, currentDay: 100);
    Equal(explicitZero.Branch, HackFlagBranch.Cleared,
        "HackFlag explicit zero clears");

    var set = NativeGmHackFlag.Evaluate("Target", "3",
        targetFound: true, currentDay: 100);
    Equal(set.Branch, HackFlagBranch.Applied, "HackFlag positive branch");
    Equal(set.StoredTier, (byte)3, "HackFlag set tier");
    Equal(set.StoredExpiryDay, 104, "HackFlag currentDay+7-days");
    Equal(set.Message, "设置 Target 外挂惩罚 3 天成功",
        "HackFlag exact set text");
    Equal(set.MessageColor, NativeGmAntiCheatCommands.ColorEcho,
        "HackFlag set color");

    var echoed = NativeGmHackFlag.Evaluate("Target", "+003",
        targetFound: true, currentDay: 100);
    Equal(echoed.ParsedDays, 3, "HackFlag signed padded days parse");
    Equal(echoed.StoredExpiryDay, 104,
        "HackFlag signed padded days arithmetic");
    Equal(echoed.Message, "设置 Target 外挂惩罚 +003 天成功",
        "HackFlag set message preserves original days token");

    var negative = NativeGmHackFlag.Evaluate("Target", "-2",
        targetFound: true, currentDay: 100);
    Equal(negative.Branch, HackFlagBranch.Applied,
        "HackFlag negative days still sets");
    Equal(negative.StoredExpiryDay, 109,
        "HackFlag negative days arithmetic");
    Equal(NativeGmHackFlag.ComputeExpiryDay(int.MaxValue, -1),
        unchecked(int.MaxValue + 7 - -1),
        "HackFlag expiry uses unchecked Int32 arithmetic");
    Assert(!negative.CoreBodyDeferred, "HackFlag recovered core is not deferred");
}

static void VerifyFlagCommandsRuntime()
{
    PrepareRuntimeFiles();
    var oldConfig = M2Share.g_Config;
    var oldProcessMsgCriticalSection = M2Share.ProcessMsgCriticalSection;
    var oldObjectManager = M2Share.ObjectManager;
    var oldRandomNumber = M2Share.RandomNumber;
    var oldUserEngine = M2Share.UserEngine;
    var oldLogStringList = M2Share.LogStringList;
    var config = oldConfig ?? new GameSvrConfig();
    var oldTestServer = config.boTestServer;

    try
    {
        M2Share.g_Config = config;
        config.boTestServer = false;
        M2Share.ProcessMsgCriticalSection = new object();
        M2Share.ObjectManager = new ObjectManager();
        M2Share.RandomNumber = RandomNumber.GetInstance();
        M2Share.UserEngine = new UserEngine();
        M2Share.LogStringList = new ArrayList();

        var gm = new TPlayObject
        {
            m_sCharName = "GameMaster",
            m_btPermission = 4,
            m_boReadyRun = true,
        };
        var originalMap = new Envirnoment { sMapName = "stay-put" };
        var ready = new TPlayObject
        {
            m_sCharName = "ReadyTarget",
            m_boReadyRun = true,
            m_PEnvir = originalMap,
        };
        var ghost = new TPlayObject
        {
            m_sCharName = "GhostTarget",
            m_boReadyRun = true,
            m_boGhost = true,
            m_btNativeCheatPenaltyTier = 1,
            m_nNativeCheatPenaltyExpiryDay = 11,
        };
        var notReady = new TPlayObject
        {
            m_sCharName = "NotReadyTarget",
            m_boReadyRun = false,
            m_btNativeCheatPenaltyTier = 2,
            m_nNativeCheatPenaltyExpiryDay = 22,
        };
        AddOnline(M2Share.UserEngine, ready, ghost, notReady);

        var clearAttribute = typeof(ClearHackFlagCommand)
            .GetCustomAttribute<GameCommandAttribute>();
        var clearMethod = typeof(ClearHackFlagCommand).GetMethod(
            nameof(ClearHackFlagCommand.ClearHackFlag));
        Assert(clearAttribute != null && clearMethod != null,
            "ClearHackFlag live registration metadata");
        Equal(clearAttribute.nPermissionMin, 4,
            "ClearHackFlag live native permission");
        var clearCommand = new ClearHackFlagCommand();
        clearCommand.Register(clearAttribute, clearMethod);

        ready.m_btNativeCheatPenaltyTier = 1;
        ready.m_nNativeCheatPenaltyExpiryDay = 77;
        gm.m_btPermission = 3;
        Equal(clearCommand.Handle("ReadyTarget", gm),
            "该命令需要4级GM才能使用",
            "ClearHackFlag permission 3 rejected by normal gate");
        Equal(ready.m_btNativeCheatPenaltyTier, (byte)1,
            "ClearHackFlag permission rejection preserves tier");
        Equal(ready.m_nNativeCheatPenaltyExpiryDay, 77,
            "ClearHackFlag permission rejection preserves expiry");
        Equal(gm.m_MsgList.Count, 0,
            "ClearHackFlag permission gate does not invoke body");

        gm.m_btPermission = 4;
        Equal<string>(null, clearCommand.Handle(string.Empty, gm),
            "ClearHackFlag empty-name return");
        Equal(gm.m_MsgList.Count, 0,
            "ClearHackFlag empty name is silent");

        Equal<string>(null,
            clearCommand.Handle("Missing ignored extra tokens", gm),
            "ClearHackFlag missing-target return");
        Equal(gm.m_MsgList.Count, 1,
            "ClearHackFlag missing target one message");
        Equal(gm.m_MsgList[0].Buff,
            "Missing 不在线或不在本GS服务器",
            "ClearHackFlag exact missing-target text");
        Equal(gm.m_MsgList[0].nParam1, 0xFF,
            "ClearHackFlag missing-target foreground");
        Equal(gm.m_MsgList[0].nParam2, 0x38,
            "ClearHackFlag missing-target background");

        gm.m_MsgList.Clear();
        clearCommand.Handle("GhostTarget", gm);
        clearCommand.Handle("NotReadyTarget", gm);
        Equal(ghost.m_btNativeCheatPenaltyTier, (byte)1,
            "ClearHackFlag ghost target tier unchanged");
        Equal(ghost.m_nNativeCheatPenaltyExpiryDay, 11,
            "ClearHackFlag ghost target expiry unchanged");
        Equal(notReady.m_btNativeCheatPenaltyTier, (byte)2,
            "ClearHackFlag non-ReadyRun tier unchanged");
        Equal(notReady.m_nNativeCheatPenaltyExpiryDay, 22,
            "ClearHackFlag non-ReadyRun expiry unchanged");
        Equal(gm.m_MsgList.Count, 2,
            "ClearHackFlag ghost/non-ready missing messages");
        Equal(gm.m_MsgList[0].Buff,
            "GhostTarget 不在线或不在本GS服务器",
            "ClearHackFlag ghost exact message");
        Equal(gm.m_MsgList[1].Buff,
            "NotReadyTarget 不在线或不在本GS服务器",
            "ClearHackFlag non-ready exact message");

        gm.m_MsgList.Clear();
        ready.m_MsgList.Clear();
        ready.m_btPermission = 0;
        ready.m_btNativeCheatPenaltyTier = 2;
        ready.m_nNativeCheatPenaltyExpiryDay = 99;
        SetNativeQuizField(ready, "m_nNativeQuizCooldown", 17);
        SetNativeQuizField(ready, "m_nNativeQuizAnswerCount", 18);
        ready.SetNativeActiveState(25);
        clearCommand.Handle("readytarget ignored extra tokens", gm);
        Equal(ready.m_btNativeCheatPenaltyTier, (byte)0,
            "ClearHackFlag clears tier");
        Equal(ready.m_nNativeCheatPenaltyExpiryDay, 0,
            "ClearHackFlag clears expiry");
        Equal(GetNativeQuizField(ready, "m_nNativeQuizCooldown"), 0,
            "ClearHackFlag clears quiz cooldown");
        Equal(GetNativeQuizField(ready, "m_nNativeQuizAnswerCount"), 0,
            "ClearHackFlag clears quiz answer count");
        Assert(!ready.HasNativeActiveState(25),
            "ClearHackFlag removes native state 25");
        Equal(gm.m_MsgList.Count, 1,
            "ClearHackFlag clear one message");
        Equal(gm.m_MsgList[0].Buff,
            "清除 readytarget 使用非法外挂的限制成功",
            "ClearHackFlag clear preserves supplied target token");
        Equal(gm.m_MsgList[0].nParam1, 0xDB,
            "ClearHackFlag clear foreground");
        Equal(gm.m_MsgList[0].nParam2, 0xFF,
            "ClearHackFlag clear background");
        Assert(ReferenceEquals(ready.m_PEnvir, originalMap),
            "ClearHackFlag does not move target");
        Equal(M2Share.LogStringList.Count, 0,
            "ClearHackFlag emits no game-data log");
        Equal(ready.m_MsgList.Count, 0,
            "ClearHackFlag sends no target message");

        gm.m_MsgList.Clear();
        ready.m_btPermission = 3;
        ready.m_btNativeCheatPenaltyTier = 0;
        ready.m_nNativeCheatPenaltyExpiryDay = 77;
        SetNativeQuizField(ready, "m_nNativeQuizCooldown", 27);
        SetNativeQuizField(ready, "m_nNativeQuizAnswerCount", 28);
        ready.SetNativeActiveState(25);
        clearCommand.Handle("ReadyTarget", gm);
        Equal(ready.m_btNativeCheatPenaltyTier, (byte)0,
            "ClearHackFlag unflagged ordinary tier unchanged");
        Equal(ready.m_nNativeCheatPenaltyExpiryDay, 77,
            "ClearHackFlag unflagged ordinary expiry unchanged");
        Equal(GetNativeQuizField(ready, "m_nNativeQuizCooldown"), 27,
            "ClearHackFlag unflagged ordinary quiz cooldown unchanged");
        Equal(GetNativeQuizField(ready, "m_nNativeQuizAnswerCount"), 28,
            "ClearHackFlag unflagged ordinary answer count unchanged");
        Assert(ready.HasNativeActiveState(25),
            "ClearHackFlag unflagged ordinary state 25 unchanged");
        Equal(gm.m_MsgList[0].Buff,
            "ReadyTarget 没有受到外挂惩罚机制的限制",
            "ClearHackFlag exact no-restriction message");
        Equal(gm.m_MsgList[0].nParam1, 0xDB,
            "ClearHackFlag no-restriction foreground");
        Equal(gm.m_MsgList[0].nParam2, 0xFF,
            "ClearHackFlag no-restriction background");

        gm.m_MsgList.Clear();
        ready.m_btPermission = 4;
        ready.m_btNativeCheatPenaltyTier = 0;
        ready.m_nNativeCheatPenaltyExpiryDay = 77;
        SetNativeQuizField(ready, "m_nNativeQuizCooldown", 37);
        SetNativeQuizField(ready, "m_nNativeQuizAnswerCount", 38);
        var localNow = DateTime.Now.ToOADate();
        gm.m_dNativeDbClockOffset = localNow - 123.25;
        ready.m_dNativeDbClockOffset = localNow - 456.25;
        clearCommand.Handle("ReadyTarget ignored", gm);
        Equal(ready.m_btNativeCheatPenaltyTier, (byte)3,
            "ClearHackFlag privileged native oddity sets tier 3");
        Equal(ready.m_nNativeCheatPenaltyExpiryDay, 123,
            "ClearHackFlag privileged expiry uses invoking GM day");
        Assert(ready.m_nNativeCheatPenaltyExpiryDay != 456,
            "ClearHackFlag privileged expiry does not use target day");
        Equal(GetNativeQuizField(ready, "m_nNativeQuizCooldown"), 37,
            "ClearHackFlag privileged quiz cooldown unchanged");
        Equal(GetNativeQuizField(ready, "m_nNativeQuizAnswerCount"), 38,
            "ClearHackFlag privileged answer count unchanged");
        Assert(ready.HasNativeActiveState(25),
            "ClearHackFlag privileged state 25 unchanged");
        Equal(gm.m_MsgList[0].Buff,
            "设置 ReadyTarget 使用非法外挂成功",
            "ClearHackFlag exact privileged message");
        Equal(gm.m_MsgList[0].nParam1, 0xFF,
            "ClearHackFlag privileged foreground");
        Equal(gm.m_MsgList[0].nParam2, 0x38,
            "ClearHackFlag privileged background");

        gm.m_MsgList.Clear();
        ready.m_btPermission = 0;
        ready.ClearNativeActiveState(25);
        ready.m_dNativeDbClockOffset = 0;
        gm.m_dNativeDbClockOffset = 0;

        var attribute = typeof(HackFlagCommand)
            .GetCustomAttribute<GameCommandAttribute>();
        var method = typeof(HackFlagCommand).GetMethod(
            nameof(HackFlagCommand.HackFlag));
        Assert(attribute != null && method != null,
            "HackFlag live registration metadata");
        Equal(attribute.nPermissionMin, 4,
            "HackFlag live native permission");
        var command = new HackFlagCommand();
        command.Register(attribute, method);

        Equal<string>(null, command.Handle(string.Empty, gm),
            "HackFlag usage return");
        Equal(gm.m_MsgList.Count, 1, "HackFlag usage message count");
        Equal(gm.m_MsgList[0].Buff, NativeGmHackFlag.UsageMessage,
            "HackFlag live exact usage text");
        Equal(gm.m_MsgList[0].nParam1, 0xDB,
            "HackFlag usage foreground");
        Equal(gm.m_MsgList[0].nParam2, 0xFF,
            "HackFlag usage background");

        gm.m_MsgList.Clear();
        Equal<string>(null, command.Handle("Missing 3", gm),
            "HackFlag missing target return");
        Equal(gm.m_MsgList.Count, 0,
            "HackFlag missing target is silent");

        command.Handle("GhostTarget 3", gm);
        command.Handle("NotReadyTarget 3", gm);
        Equal(ghost.m_btNativeCheatPenaltyTier, (byte)1,
            "HackFlag ghost target tier unchanged");
        Equal(ghost.m_nNativeCheatPenaltyExpiryDay, 11,
            "HackFlag ghost target expiry unchanged");
        Equal(notReady.m_btNativeCheatPenaltyTier, (byte)2,
            "HackFlag non-ReadyRun tier unchanged");
        Equal(notReady.m_nNativeCheatPenaltyExpiryDay, 22,
            "HackFlag non-ReadyRun expiry unchanged");
        Equal(gm.m_MsgList.Count, 0,
            "HackFlag ghost/non-ready paths are silent");

        ready.m_btNativeCheatPenaltyTier = 2;
        ready.m_nNativeCheatPenaltyExpiryDay = 99;
        command.Handle("readytarget not-a-number", gm);
        Equal(ready.m_btNativeCheatPenaltyTier, (byte)0,
            "HackFlag invalid days clears tier");
        Equal(ready.m_nNativeCheatPenaltyExpiryDay, 0,
            "HackFlag invalid days clears expiry");
        Equal(gm.m_MsgList[0].Buff,
            "清除 readytarget 使用非法外挂的限制成功",
            "HackFlag clear preserves supplied target token");
        Equal(gm.m_MsgList[0].nParam1, 0xDB,
            "HackFlag clear foreground");
        Equal(gm.m_MsgList[0].nParam2, 0xFF,
            "HackFlag clear background");

        gm.m_MsgList.Clear();
        ready.m_btNativeCheatPenaltyTier = 2;
        ready.m_nNativeCheatPenaltyExpiryDay = 99;
        command.Handle("ReadyTarget", gm);
        Equal(ready.m_btNativeCheatPenaltyTier, (byte)0,
            "HackFlag missing days clears tier");
        Equal(ready.m_nNativeCheatPenaltyExpiryDay, 0,
            "HackFlag missing days clears expiry");
        Equal(gm.m_MsgList[0].Buff,
            "清除 ReadyTarget 使用非法外挂的限制成功",
            "HackFlag missing days exact clear message");

        gm.m_MsgList.Clear();
        ready.m_btNativeCheatPenaltyTier = 2;
        ready.m_nNativeCheatPenaltyExpiryDay = 99;
        command.Handle("ReadyTarget 0", gm);
        Equal(ready.m_btNativeCheatPenaltyTier, (byte)0,
            "HackFlag explicit zero clears tier");
        Equal(ready.m_nNativeCheatPenaltyExpiryDay, 0,
            "HackFlag explicit zero clears expiry");
        Equal(gm.m_MsgList[0].Buff,
            "清除 ReadyTarget 使用非法外挂的限制成功",
            "HackFlag explicit zero exact clear message");

        gm.m_MsgList.Clear();
        var getCurrentDay = typeof(TPlayObject).GetMethod(
            "GetNativeTruncDaysOnline",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(
                "TPlayObject.GetNativeTruncDaysOnline");
        var dayBefore = (int)getCurrentDay.Invoke(ready, null)!;
        command.Handle("ReadyTarget +003 ignored extra tokens", gm);
        var dayAfter = (int)getCurrentDay.Invoke(ready, null)!;
        Equal(ready.m_btNativeCheatPenaltyTier, (byte)3,
            "HackFlag positive days sets tier 3");
        Assert(ready.m_nNativeCheatPenaltyExpiryDay ==
                   NativeGmHackFlag.ComputeExpiryDay(dayBefore, 3) ||
               ready.m_nNativeCheatPenaltyExpiryDay ==
                   NativeGmHackFlag.ComputeExpiryDay(dayAfter, 3),
            "HackFlag positive days stores currentDay+7-days");
        Equal(gm.m_MsgList.Count, 1,
            "HackFlag positive days one message");
        Equal(gm.m_MsgList[0].Buff,
            "设置 ReadyTarget 外挂惩罚 +003 天成功",
            "HackFlag preserves days token and ignores extra parameters");
        Equal(gm.m_MsgList[0].nParam1, 0xFF,
            "HackFlag set foreground");
        Equal(gm.m_MsgList[0].nParam2, 0x38,
            "HackFlag set background");
        Assert(ReferenceEquals(ready.m_PEnvir, originalMap),
            "HackFlag does not move target to black room");
        Equal(M2Share.LogStringList.Count, 0,
            "HackFlag emits no game-data log");
        Equal(ready.m_MsgList.Count, 0,
            "HackFlag sends no target message");

        gm.m_MsgList.Clear();
        dayBefore = (int)getCurrentDay.Invoke(ready, null)!;
        command.Handle("ReadyTarget -2", gm);
        dayAfter = (int)getCurrentDay.Invoke(ready, null)!;
        Assert(ready.m_nNativeCheatPenaltyExpiryDay ==
                   NativeGmHackFlag.ComputeExpiryDay(dayBefore, -2) ||
               ready.m_nNativeCheatPenaltyExpiryDay ==
                   NativeGmHackFlag.ComputeExpiryDay(dayAfter, -2),
            "HackFlag negative days follows set branch");
        Equal(gm.m_MsgList[0].Buff,
            "设置 ReadyTarget 外挂惩罚 -2 天成功",
            "HackFlag negative exact message");

        gm.m_MsgList.Clear();
        ready.m_btNativeCheatPenaltyTier = 1;
        ready.m_nNativeCheatPenaltyExpiryDay = 123;
        gm.m_btPermission = 3;
        Equal(command.Handle("ReadyTarget 5", gm),
            "该命令需要4级GM才能使用",
            "HackFlag permission 3 rejected by normal gate");
        Equal(ready.m_btNativeCheatPenaltyTier, (byte)1,
            "HackFlag permission rejection preserves tier");
        Equal(ready.m_nNativeCheatPenaltyExpiryDay, 123,
            "HackFlag permission rejection preserves expiry");
        Equal(gm.m_MsgList.Count, 0,
            "HackFlag permission gate does not invoke body");

        gm.m_btPermission = 4;
        M2Share.UserEngine = null;
        clearCommand.ClearHackFlag(new[] { "ReadyTarget" }, gm);
        Equal(gm.m_MsgList.Count, 0,
            "ClearHackFlag null engine is silent");
        clearCommand.ClearHackFlag(null, null);
        command.HackFlag(new[] { "ReadyTarget", "5" }, gm);
        Equal(gm.m_MsgList.Count, 0,
            "HackFlag null engine is silent");
        command.HackFlag(null, null);
    }
    finally
    {
        config.boTestServer = oldTestServer;
        M2Share.g_Config = oldConfig;
        M2Share.ProcessMsgCriticalSection = oldProcessMsgCriticalSection;
        M2Share.ObjectManager = oldObjectManager;
        M2Share.RandomNumber = oldRandomNumber;
        M2Share.UserEngine = oldUserEngine;
        M2Share.LogStringList = oldLogStringList;
    }
}

static void VerifyHackerpunish()
{
    // input 0/1/2 -> stored byte 1/2/3, normalized 0/1/2; any other nonzero -> record-only reset.
    var m0 = NativeGmHackerpunish.Evaluate(0);
    Equal(m0.Branch, HackerpunishBranch.RecordOnly, "punish 0 branch");
    Equal(m0.NormalizedMode, 0, "punish 0 mode");
    Equal(m0.StoredModeByte, 1, "punish 0 byte");

    var m1 = NativeGmHackerpunish.Evaluate(1);
    Equal(m1.Branch, HackerpunishBranch.ForbidRecord, "punish 1 branch");
    Equal(m1.NormalizedMode, 1, "punish 1 mode");
    Equal(m1.StoredModeByte, 2, "punish 1 byte");

    var m2 = NativeGmHackerpunish.Evaluate(2);
    Equal(m2.Branch, HackerpunishBranch.PeaceMode, "punish 2 branch");
    Equal(m2.NormalizedMode, 2, "punish 2 mode");
    Equal(m2.StoredModeByte, 3, "punish 2 byte");

    foreach (var bad in new[] { 3, 7, -1, 99 })
    {
        var mx = NativeGmHackerpunish.Evaluate(bad);
        Equal(mx.Branch, HackerpunishBranch.InvalidResetToRecordOnly, $"punish {bad} branch");
        Equal(mx.NormalizedMode, 0, $"punish {bad} mode");
        Equal(mx.StoredModeByte, 1, $"punish {bad} byte");
    }

    foreach (var o in new[] { m0, m1, m2 })
    {
        Assert(o.CallsApplyCore, "punish calls apply core");
        Assert(o.SendsSysMsg, "punish always SysMsg");
        Equal(o.MessageColor, NativeGmAntiCheatCommands.ColorEcho, "punish colour");
    }
}

static void VerifyClientVersion()
{
    var none = NativeGmClientVersion.Evaluate(0);
    Equal(none.Branch, ClientVersionBranch.NoMismatch, "version no-mismatch branch");
    Assert(none.SetsServerVersion, "version always sets global");
    Assert(none.SendsConfirm, "version always confirms");
    Equal(none.ConfirmColor, NativeGmAntiCheatCommands.ColorNotice, "version confirm colour");
    Assert(!none.SendsMismatchNotice, "version no second msg when count 0");
    Equal(none.MismatchCount, 0, "version count 0");

    var some = NativeGmClientVersion.Evaluate(5);
    Equal(some.Branch, ClientVersionBranch.HasMismatch, "version mismatch branch");
    Assert(some.SendsMismatchNotice, "version second msg when count > 0");
    Equal(some.MismatchNoticeColor, NativeGmAntiCheatCommands.ColorEcho, "version mismatch colour");
    Equal(some.MismatchCount, 5, "version count 5");
}

static void VerifySetIpHumanMaxCount()
{
    var empty = NativeGmSetIpHumanMaxCount.Evaluate(paramPresent: false, count: 42);
    Equal(empty.Branch, SetIpHumanMaxCountBranch.ParamEmpty, "ipmax empty branch");
    Assert(!empty.CallsConfigCore, "ipmax empty no core");
    Equal(empty.ConfigValue, 0, "ipmax empty value");
    Assert(!empty.SendsSysMsg, "ipmax silent");

    var set = NativeGmSetIpHumanMaxCount.Evaluate(paramPresent: true, count: 42);
    Equal(set.Branch, SetIpHumanMaxCountBranch.Applied, "ipmax applied branch");
    Assert(set.CallsConfigCore, "ipmax applied core");
    Equal(set.ConfigValue, 42, "ipmax applied value");
    Assert(!set.SendsSysMsg, "ipmax applied silent");
}

static void VerifyReloadWhiteList()
{
    var o = NativeGmReloadWhiteList.Evaluate();
    Assert(o.CallsConfigCore, "whitelist calls config core");
    Equal(o.ConfigValue, 0, "whitelist value 0");
    Assert(!o.SendsSysMsg, "whitelist dispatcher silent");
}

static void VerifyViewMonitor()
{
    var o = NativeGmViewMonitor.Evaluate();
    Assert(o.CallsViewCore, "viewmon calls view core");
    Assert(o.SendsSysMsg, "viewmon always replies");
    Equal(o.MessageColor, NativeGmAntiCheatCommands.ColorNotice, "viewmon colour");
}

static void VerifyReloadSmsUserList()
{
    var ok = NativeGmReloadSmsUserList.Evaluate(reloadOk: true);
    Equal(ok.Branch, ReloadSmsUserListBranch.Success, "sms success branch");
    Assert(ok.SendsSysMsg, "sms success msg");
    Equal(ok.MessageColor, NativeGmAntiCheatCommands.ColorNotice, "sms success colour");

    var fail = NativeGmReloadSmsUserList.Evaluate(reloadOk: false);
    Equal(fail.Branch, ReloadSmsUserListBranch.Failure, "sms fail branch");
    Assert(fail.SendsSysMsg, "sms fail msg");
    Equal(fail.MessageColor, NativeGmAntiCheatCommands.ColorNotice, "sms fail colour");
}

static void AddOnline(UserEngine engine, params TPlayObject[] players)
{
    var field = typeof(UserEngine).GetField("m_PlayObjectList",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(UserEngine).FullName,
            "m_PlayObjectList");
    if (field.GetValue(engine) is not IList<TPlayObject> online)
        throw new InvalidOperationException("unexpected online-player list");
    foreach (var player in players)
        online.Add(player);
}

static FieldInfo NativeQuizField(string name)
{
    return typeof(TPlayObject).GetField(name,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(TPlayObject).FullName, name);
}

static void SetNativeQuizField(TPlayObject player, string name, int value)
{
    NativeQuizField(name).SetValue(player, value);
}

static int GetNativeQuizField(TPlayObject player, string name)
{
    return (int)(NativeQuizField(name).GetValue(player)
        ?? throw new InvalidOperationException($"{name} was null"));
}

static void PrepareRuntimeFiles()
{
    var runtimeDirectory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(runtimeDirectory, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "String.ini"),
        "[String]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "Command.conf"),
        "[Command]" + Environment.NewLine);
    var shareDirectory = Path.Combine(Path.GetFullPath(
        Path.Combine(runtimeDirectory, "..")), "Share");
    Directory.CreateDirectory(shareDirectory);
    File.WriteAllText(Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"),
        "[Integer]" + Environment.NewLine);
}
