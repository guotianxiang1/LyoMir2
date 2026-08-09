using GameSvr;

// Contract check for the dormant ANTICHEAT/IP/SECURITY GM command family model
// (GameSvr/Services/NativeGmAntiCheatCommands.cs), locked against the Hex-Rays-verified original
// dispatcher sub_622820 (single switch, table jpt_622B15 @0x00622B1C) in the unpacked M2Server image.
// Family 09 = 15 commands, ALL real handlers (0 no-ops).

try
{
    VerifyDispatcherConstants();
    VerifyRegistry();
    VerifyNoNoOps();
    VerifyForwardContracts();
    VerifyHackerpunish();
    VerifyClientVersion();
    VerifySetIpHumanMaxCount();
    VerifyReloadWhiteList();
    VerifyViewMonitor();
    VerifyReloadSmsUserList();

    Console.WriteLine(
        "PASS NativeGmAntiCheatCommandsCheck dispatcher=sub_622820 table=0x622B1C max=750 family=09 " +
        "commands=15 impl=15 noop=0 " +
        "ladders=Hackerpunish/ClientVersion/SetIpHumanMaxCount/ReloadWhiteList/ViewMonitor/ReloadSmsUserList " +
        "forward=MapUserInfo/ClearHackFlag/HackFlag/IPHackFlag/IPOutSay/IPHumNum/IpBlackRoom/kickOutPtid/SetMonitor");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"NativeGmAntiCheatCommandsCheck FAIL: {ex.Message}");
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
        (GmAntiCheatCommand.ClearHackFlag,      "ClearHackFlag",      151, 4, 0x006255EE, 0x006D321C, GmAntiCheatShape.ForwardOnly,      0, false),
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
        Assert(info.CoreBodyDeferred, $"{e.cmd} core body deferred");
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
        Assert(info.Implemented && info.CoreBodyDeferred, $"{info.Command} impl+deferred");
}

static void VerifyForwardContracts()
{
    // ForwardOnly / ParseIntThenCore commands: recognized, gated, forward to a deferred core, no dispatcher SysMsg.
    (GmAntiCheatCommand cmd, uint coreEa, int args, bool parsesInt)[] fwd =
    {
        (GmAntiCheatCommand.MapUserInfo,   0x006D6698, 0, false),
        (GmAntiCheatCommand.ClearHackFlag, 0x006D321C, 0, false),
        (GmAntiCheatCommand.HackFlag,      0x006D440C, 2, false),
        (GmAntiCheatCommand.IPHackFlag,    0x006D45C8, 2, false),
        (GmAntiCheatCommand.IPOutSay,      0x006D4CA4, 2, false),
        (GmAntiCheatCommand.IPHumNum,      0x006E3498, 0, true),
        (GmAntiCheatCommand.IpBlackRoom,   0x006D49E4, 2, false),
        (GmAntiCheatCommand.KickOutPtid,   0x00651CBC, 0, false),
        (GmAntiCheatCommand.SetMonitor,    0x0079F908, 2, false),
    };
    foreach (var f in fwd)
    {
        var c = NativeGmAntiCheatCommands.ForwardContract(f.cmd);
        Equal(c.CoreAddress, f.coreEa, $"{f.cmd} forward core");
        Equal(c.CoreStringArgs, f.args, $"{f.cmd} forward args");
        Equal(c.ParsesLeadingInt, f.parsesInt, $"{f.cmd} forward parses int");
        Assert(c.CoreBodyDeferred, $"{f.cmd} forward deferred");
        Assert(!c.DispatcherSendsSysMsg, $"{f.cmd} forward silent");
    }

    // A dispatcher-ladder command must NOT be routed through the forward path.
    var threw = false;
    try { NativeGmAntiCheatCommands.ForwardContract(GmAntiCheatCommand.Hackerpunish); }
    catch (InvalidOperationException) { threw = true; }
    Assert(threw, "ladder command rejected by forward path");
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
