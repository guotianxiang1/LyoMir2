using GameSvr;

// Contract check for the dormant GUILD / CASTLE / SABAK (行会 / 沙巴克 / 攻城) GM command family model
// (GameSvr/Services/NativeGmGuildCastleCommands.cs), locked against the Hex-Rays-verified original
// dispatcher sub_622820 (single switch, table jpt_622B15 @0x00622B1C) in the unpacked M2Server image.
// Evidence dumps: D:/loym2/staging/update_clothes_4637_ida_work/{disp_decomp.txt,big622820.txt,
// handler_out.txt,all_strings.txt}. This is family 04: 25 commands, 19 with a dedicated case
// (11 real impl + 4 nullsub stubs + 4 "Invalid" stubs) and 6 registered no-ops (2 empty-case, 4 default).

try
{
    VerifyDispatcherConstants();
    VerifyRegistry();
    VerifyHandledByteSemantics();
    VerifyPureCoreForwards();
    VerifyCoreThenMsg();
    VerifyLookSaGold();
    VerifyChgCastleWar();
    VerifyWatchGuild();
    VerifyServerGuildSwitch();
    VerifyNullsubStubs();
    VerifyInvalidStubs();
    VerifyNoOps();

    Console.WriteLine(
        "PASS NativeGmGuildCastleCommandsCheck dispatcher=sub_622820 table=0x622B1C max=750 count=25 " +
        "impl=LookSaGold/DoTask/CallTaskMon/ChgMonAtt/ChgCastleOwner/ChgCastleWar/BeginAreaCastleMatch/" +
        "EndAreaCastleMatch/WatchGuild/serverguildswitch/weimanyuan " +
        "nullsubStub=GuildPoint/GuildWarOn/GuildWarOff/ReportGuildWar " +
        "invalidStub=SetGuildLord/GuildForbid/selfAddGuild/ReNameGuild " +
        "emptyNoop=MakeGuild/DelGuild defaultNoop=loadHeroStrike/ChgGuildValue/DreamCastleScore/ChgDoubleCastleWar " +
        "csharpDrift(legacy-perm10-except-ChgMonAtt;over=GuildWarOff/DelGuild/DreamCastleScore/ChgDoubleCastleWar;" +
        "under=CallTaskMon/BeginAreaCastleMatch/EndAreaCastleMatch;mismatch=ChgCastleWar/GuildForbid)");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"NativeGmGuildCastleCommandsCheck FAIL: {ex.Message}");
    return 1;
}

// Single generic equality assertion helper (no overloads).
static void Equal<T>(T actual, T expected, string what)
{
    if (!System.Collections.Generic.EqualityComparer<T>.Default.Equals(actual, expected))
        throw new Exception($"{what}: expected [{expected}] got [{actual}]");
}

static void VerifyDispatcherConstants()
{
    Equal(NativeGmGuildCastleCommands.DispatcherEa, 0x00622820u, "dispatcher ea");
    Equal(NativeGmGuildCastleCommands.HandledByteSetEa, 0x0062284Du, "handled-byte set ea");
    Equal(NativeGmGuildCastleCommands.IndexLookupEa, 0x00621F28u, "index lookup ea");
    Equal(NativeGmGuildCastleCommands.JumpTableEa, 0x00622B1Cu, "jump table ea");
    Equal(NativeGmGuildCastleCommands.SwitchMaxIndex, 750, "switch max index");
    Equal(NativeGmGuildCastleCommands.DefaultCaseEa, 0x0062B648u, "default case ea");
    Equal(NativeGmGuildCastleCommands.EmptyCaseEa, 0x0062B64Cu, "empty case ea");
    // the two no-op labels are distinct and adjacent (def_622B15 + 4 == loc_62B64C)
    Equal(NativeGmGuildCastleCommands.DefaultCaseEa + 4, NativeGmGuildCastleCommands.EmptyCaseEa, "def+4 == empty case");
    Equal(NativeGmGuildCastleCommands.SysMsgVtableOffset, 0xD4, "sysmsg vtable offset");
    Equal(NativeGmGuildCastleCommands.ColorGreen, 0x38FF, "colour green");
    Equal(NativeGmGuildCastleCommands.ColorYellowNotice, 0xFFDB, "colour yellow");
    Equal(NativeGmGuildCastleCommands.ColorWhiteNotice, 0xFCFF, "colour white");
    Equal(NativeGmGuildCastleCommands.InvalidStringEa, 0x0062C638u, "Invalid string ea");
    Equal(NativeGmGuildCastleCommands.InvalidReplyText, "Invalid", "Invalid reply text");
    Equal(NativeGmGuildCastleCommands.CastleManagerGlobalEa, 0x007D6214u, "castle manager global");
    Equal(NativeGmGuildCastleCommands.ServerGuildSwitchGlobalEa, 0x007D600Cu, "server guild switch global");
    // the four guild-war nullsub stubs are consecutive 4-byte empty functions
    Equal(NativeGmGuildCastleCommands.NullsubGuildPointEa, 0x006C2908u, "nullsub GuildPoint");
    Equal(NativeGmGuildCastleCommands.NullsubGuildPointEa + 4, NativeGmGuildCastleCommands.NullsubGuildWarOnEa, "nullsub +4 GuildWarOn");
    Equal(NativeGmGuildCastleCommands.NullsubGuildWarOnEa + 4, NativeGmGuildCastleCommands.NullsubGuildWarOffEa, "nullsub +4 GuildWarOff");
    Equal(NativeGmGuildCastleCommands.NullsubGuildWarOffEa + 4, NativeGmGuildCastleCommands.NullsubReportGuildWarEa, "nullsub +4 ReportGuildWar");
}

static void VerifyRegistry()
{
    // (command, name, index, perm, kind, caseAddr, livePresent, livePerm, over, under, mismatch)
    (GmGuildCastleCommand cmd, string name, int idx, int perm, GmGuildCastleHandlerKind kind, uint addr,
        bool live, int livePerm, bool over, bool under, bool mismatch)[] expected =
    {
        (GmGuildCastleCommand.LookSaGold,           "LookSaGold",           71, 3, GmGuildCastleHandlerKind.ImplementedCase,     0x00624C36u, false,  0, false, false, false),
        (GmGuildCastleCommand.DoTask,               "DoTask",              100, 4, GmGuildCastleHandlerKind.ImplementedCase,     0x00625048u, false,  0, false, false, false),
        (GmGuildCastleCommand.CallTaskMon,          "CallTaskMon",         101, 4, GmGuildCastleHandlerKind.ImplementedCase,     0x0062505Bu, true,  10, false, true,  false),
        (GmGuildCastleCommand.ChgMonAtt,            "ChgMonAtt",           144, 4, GmGuildCastleHandlerKind.ImplementedCase,     0x00625551u, true,   4, false, false, false),
        (GmGuildCastleCommand.ChgCastleOwner,       "ChgCastleOwner",      215, 5, GmGuildCastleHandlerKind.ImplementedCase,     0x00625EC0u, false,  0, false, false, false),
        (GmGuildCastleCommand.ChgCastleWar,         "ChgCastleWar",        216, 5, GmGuildCastleHandlerKind.ImplementedCase,     0x00625ED5u, true,  10, false, false, true),
        (GmGuildCastleCommand.BeginAreaCastleMatch, "BeginAreaCastleMatch",394, 4, GmGuildCastleHandlerKind.ImplementedCase,     0x0062877Bu, true,  10, false, true,  false),
        (GmGuildCastleCommand.EndAreaCastleMatch,   "EndAreaCastleMatch",  395, 4, GmGuildCastleHandlerKind.ImplementedCase,     0x006287CCu, true,  10, false, true,  false),
        (GmGuildCastleCommand.WatchGuild,           "WatchGuild",          405, 3, GmGuildCastleHandlerKind.ImplementedCase,     0x00628851u, false,  0, false, false, false),
        (GmGuildCastleCommand.ServerGuildSwitch,    "serverguildswitch",   534, 4, GmGuildCastleHandlerKind.ImplementedCase,     0x00629697u, false,  0, false, false, false),
        (GmGuildCastleCommand.Weimanyuan,           "weimanyuan",          720, 4, GmGuildCastleHandlerKind.ImplementedCase,     0x0062A94Du, false,  0, false, false, false),

        (GmGuildCastleCommand.GuildPoint,           "GuildPoint",          115, 4, GmGuildCastleHandlerKind.StubbedNullsub,      0x0062520Du, false,  0, false, false, false),
        (GmGuildCastleCommand.GuildWarOn,           "GuildWarOn",          116, 4, GmGuildCastleHandlerKind.StubbedNullsub,      0x00625229u, false,  0, false, false, false),
        (GmGuildCastleCommand.GuildWarOff,          "GuildWarOff",         117, 4, GmGuildCastleHandlerKind.StubbedNullsub,      0x00625236u, true,  10, true,  false, false),
        (GmGuildCastleCommand.ReportGuildWar,       "ReportGuildWar",      118, 4, GmGuildCastleHandlerKind.StubbedNullsub,      0x00625243u, false,  0, false, false, false),

        (GmGuildCastleCommand.SetGuildLord,         "SetGuildLord",        265, 4, GmGuildCastleHandlerKind.StubbedInvalidReply, 0x006267C6u, false,  0, false, false, false),
        (GmGuildCastleCommand.GuildForbid,          "GuildForbid",         404, 4, GmGuildCastleHandlerKind.StubbedInvalidReply, 0x00628978u, true,  10, false, false, true),
        (GmGuildCastleCommand.SelfAddGuild,         "selfAddGuild",        453, 4, GmGuildCastleHandlerKind.StubbedInvalidReply, 0x00628B25u, false,  0, false, false, false),
        (GmGuildCastleCommand.ReNameGuild,          "ReNameGuild",         458, 4, GmGuildCastleHandlerKind.StubbedInvalidReply, 0x00628991u, false,  0, false, false, false),

        (GmGuildCastleCommand.MakeGuild,            "MakeGuild",           213, 5, GmGuildCastleHandlerKind.EmptyCaseNoOp,       0x0062B64Cu, false,  0, false, false, false),
        (GmGuildCastleCommand.DelGuild,             "DelGuild",            214, 5, GmGuildCastleHandlerKind.EmptyCaseNoOp,       0x0062B64Cu, true,  10, true,  false, false),

        (GmGuildCastleCommand.LoadHeroStrike,       "loadHeroStrike",      326, 4, GmGuildCastleHandlerKind.DefaultNoOp,         0x0062B648u, false,  0, false, false, false),
        (GmGuildCastleCommand.ChgGuildValue,        "ChgGuildValue",       336, 5, GmGuildCastleHandlerKind.DefaultNoOp,         0x0062B648u, false,  0, false, false, false),
        (GmGuildCastleCommand.DreamCastleScore,     "DreamCastleScore",    351, 4, GmGuildCastleHandlerKind.DefaultNoOp,         0x0062B648u, true,  10, true,  false, false),
        (GmGuildCastleCommand.ChgDoubleCastleWar,   "ChgDoubleCastleWar",  531, 5, GmGuildCastleHandlerKind.DefaultNoOp,         0x0062B648u, true,  10, true,  false, false),
    };

    Equal(NativeGmGuildCastleCommands.All.Count, expected.Length, "registry count");
    Equal(expected.Length, 25, "family-04 command count");

    int impl = 0, nullstub = 0, invstub = 0, empty = 0, def = 0, live = 0;
    foreach (var e in expected)
    {
        var info = NativeGmGuildCastleCommands.Info(e.cmd);
        Equal(info.Name, e.name, $"{e.cmd} name");
        Equal(info.DispatchIndex, e.idx, $"{e.cmd} index");
        Equal(info.RequiredPermission, e.perm, $"{e.cmd} perm");
        Equal(info.HandlerKind, e.kind, $"{e.cmd} kind");
        Equal(info.CaseAddress, e.addr, $"{e.cmd} case addr");
        Equal(info.CSharpLivePresent, e.live, $"{e.cmd} live present");
        Equal(info.CSharpLivePermission, e.livePerm, $"{e.cmd} live perm");
        Equal(info.CSharpStubOverSends, e.over, $"{e.cmd} over-sends flag");
        Equal(info.CSharpStubUnderImplements, e.under, $"{e.cmd} under-impl flag");
        Equal(info.CSharpBehaviorMismatch, e.mismatch, $"{e.cmd} behaviour-mismatch flag");
        Equal(info.DispatchIndex >= 0 && info.DispatchIndex <= NativeGmGuildCastleCommands.SwitchMaxIndex, true,
            $"{e.cmd} index in switch range");

        // ChgMonAtt is now wired at its native permission; older live commands
        // in this family still retain their historical permission-10 drift.
        if (e.live)
        {
            if (e.cmd == GmGuildCastleCommand.ChgMonAtt)
            {
                Equal(info.CSharpLivePermission, info.RequiredPermission,
                    $"{e.cmd} live permission parity");
            }
            else
            {
                Equal(info.CSharpLivePermission, 10,
                    $"{e.cmd} live perm is 10 (drift)");
                Equal(info.RequiredPermission != info.CSharpLivePermission,
                    true, $"{e.cmd} perm drift present");
            }
            live++;
        }

        // case-address terminal shape matches the handler kind
        switch (e.kind)
        {
            case GmGuildCastleHandlerKind.ImplementedCase:
            case GmGuildCastleHandlerKind.StubbedNullsub:
            case GmGuildCastleHandlerKind.StubbedInvalidReply:
                Equal(info.CaseAddress != NativeGmGuildCastleCommands.DefaultCaseEa
                      && info.CaseAddress != NativeGmGuildCastleCommands.EmptyCaseEa, true, $"{e.cmd} distinct case addr");
                break;
            case GmGuildCastleHandlerKind.EmptyCaseNoOp:
                Equal(info.CaseAddress, NativeGmGuildCastleCommands.EmptyCaseEa, $"{e.cmd} on loc_62B64C");
                break;
            case GmGuildCastleHandlerKind.DefaultNoOp:
                Equal(info.CaseAddress, NativeGmGuildCastleCommands.DefaultCaseEa, $"{e.cmd} on def_622B15");
                break;
        }

        switch (e.kind)
        {
            case GmGuildCastleHandlerKind.ImplementedCase: impl++; break;
            case GmGuildCastleHandlerKind.StubbedNullsub: nullstub++; break;
            case GmGuildCastleHandlerKind.StubbedInvalidReply: invstub++; break;
            case GmGuildCastleHandlerKind.EmptyCaseNoOp: empty++; break;
            case GmGuildCastleHandlerKind.DefaultNoOp: def++; break;
        }
    }

    Equal(impl, 11, "implemented-case count");
    Equal(nullstub, 4, "nullsub-stub count");
    Equal(invstub, 4, "invalid-stub count");
    Equal(empty, 2, "empty-case no-op count");
    Equal(def, 4, "default no-op count");
    Equal(impl + nullstub + invstub, 19, "dedicated-case count (inventory: 19 impl)");
    Equal(empty + def, 6, "registered no-op count (inventory: 6 noop)");
    Equal(live, 10, "live command count");
}

static void VerifyHandledByteSemantics()
{
    // def_622B15 clears var_D to 0; every other shape leaves the optimistic 1
    Equal(NativeGmGuildCastleCommands.HandledByteStaysSet(GmGuildCastleHandlerKind.DefaultNoOp), false, "default clears handled byte");
    Equal(NativeGmGuildCastleCommands.HandledByteStaysSet(GmGuildCastleHandlerKind.EmptyCaseNoOp), true, "empty case keeps handled byte");
    Equal(NativeGmGuildCastleCommands.HandledByteStaysSet(GmGuildCastleHandlerKind.ImplementedCase), true, "impl keeps handled byte");
    Equal(NativeGmGuildCastleCommands.HandledByteStaysSet(GmGuildCastleHandlerKind.StubbedNullsub), true, "nullsub keeps handled byte");
    Equal(NativeGmGuildCastleCommands.HandledByteStaysSet(GmGuildCastleHandlerKind.StubbedInvalidReply), true, "invalid keeps handled byte");
}

static void VerifyPureCoreForwards()
{
    var doTask = NativeGmDoTask.Evaluate(30, 30);
    Equal(doTask.CallsCore, true, "dotask calls core");
    Equal(doTask.CoreEa, 0x006BFEF8u, "dotask core ea");
    Equal(doTask.CoreBodyDeferred, true, "dotask core deferred");
    Equal(doTask.SendsSysMsg, false, "dotask silent");

    var callMon = NativeGmCallTaskMon.Evaluate(30, 30, "Zuma", 5);
    Equal(callMon.CoreEa, 0x006C19D0u, "calltaskmon core ea");
    Equal(callMon.CoreBodyDeferred, true, "calltaskmon core deferred");
    Equal(callMon.SendsSysMsg, false, "calltaskmon silent");

    var chgOwner = NativeGmChgCastleOwner.Evaluate("Guild1");
    Equal(chgOwner.CoreEa, 0x006C74ECu, "chgcastleowner core ea");
    Equal(chgOwner.CoreBodyDeferred, true, "chgcastleowner core deferred");
    Equal(chgOwner.SendsSysMsg, false, "chgcastleowner silent");

    var wmy = NativeGmWeimanyuan.Evaluate();
    Equal(wmy.CoreEa, 0x006AE260u, "weimanyuan core ea");
    Equal(wmy.CoreBodyDeferred, true, "weimanyuan core deferred");
    Equal(wmy.SendsSysMsg, false, "weimanyuan silent");
}

static void VerifyCoreThenMsg()
{
    var chgMon = NativeGmChgMonAtt.Evaluate();
    Equal(chgMon.CoreEa, 0x0067D3DCu, "chgmonatt core ea");
    Equal(chgMon.CoreBodyDeferred, false, "chgmonatt core implemented");
    Equal(chgMon.SendsSysMsg, true, "chgmonatt sends msg");
    Equal(chgMon.MessageColor, 0x38FF, "chgmonatt colour");

    var begin = NativeGmBeginAreaCastleMatch.Evaluate();
    Equal(begin.CoreEa, 0x0065D13Cu, "begin match core ea");
    Equal(begin.CoreBodyDeferred, true, "begin match core deferred");
    Equal(begin.SendsSysMsg, true, "begin match sends msg");
    Equal(begin.MessageColor, 0xFFDB, "begin match colour");

    var end = NativeGmEndAreaCastleMatch.Evaluate();
    Equal(end.CoreEa, 0x0065D158u, "end match core ea");
    Equal(end.CoreBodyDeferred, true, "end match core deferred");
    Equal(end.SendsSysMsg, true, "end match sends msg");
    Equal(end.MessageColor, 0xFFDB, "end match colour");
}

static void VerifyLookSaGold()
{
    var o = NativeGmLookSaGold.Evaluate();
    Equal(o.ReadsTotalFunds, true, "looksagold reads funds");
    Equal(o.ReadsTodayIncome, true, "looksagold reads income");
    Equal(o.ManagerGlobalEa, 0x007D6214u, "looksagold manager ea");
    Equal(o.TotalFundsOffset, 0x80, "looksagold funds offset");
    Equal(o.TodayIncomeOffset, 0x84, "looksagold income offset");
    Equal(o.MutatesState, false, "looksagold read-only");
    Equal(o.SendsSysMsg, true, "looksagold sends msg");
    Equal(o.MessageColor, 0xFFDB, "looksagold colour");
}

static void VerifyChgCastleWar()
{
    var active = NativeGmChgCastleWar.Evaluate(warActive: true);
    Equal(active.Branch, ChgCastleWarBranch.WarActive_End, "chgcastlewar active branch");
    Equal(active.CallsEndCore, true, "chgcastlewar active ends war");
    Equal(active.EndCoreEa, 0x0065C080u, "chgcastlewar end core ea");
    Equal(active.CoreBodyDeferred, true, "chgcastlewar end deferred");
    Equal(active.WritesStartFlag, false, "chgcastlewar active no start flag");
    Equal(active.SendsSysMsg, false, "chgcastlewar active silent");

    var inactive = NativeGmChgCastleWar.Evaluate(warActive: false);
    Equal(inactive.Branch, ChgCastleWarBranch.WarInactive_Start, "chgcastlewar inactive branch");
    Equal(inactive.CallsEndCore, false, "chgcastlewar inactive no end");
    Equal(inactive.WritesStartFlag, true, "chgcastlewar inactive sets start flag");
    Equal(inactive.ManagerGlobalEa, 0x007D6214u, "chgcastlewar manager ea");
    Equal(inactive.StartFlagOffset, 43, "chgcastlewar start flag offset");
    Equal(inactive.CoreBodyDeferred, false, "chgcastlewar inactive not deferred");
    Equal(inactive.SendsSysMsg, true, "chgcastlewar inactive sends msg");
    Equal(inactive.MessageColor, 0xFFDB, "chgcastlewar inactive colour");
}

static void VerifyWatchGuild()
{
    var set = NativeGmWatchGuild.Evaluate("MyGuild");
    Equal(set.Branch, WatchGuildBranch.SetWatch, "watchguild set branch");
    Equal(set.SetsWatchGuild, true, "watchguild sets");
    Equal(set.ClearsWatchGuild, false, "watchguild set no clear");
    Equal(set.SendsSysMsg, true, "watchguild set sends msg");
    Equal(set.MessageColor, 0x38FF, "watchguild set colour");

    foreach (var empty in new[] { "", (string)null })
    {
        var clear = NativeGmWatchGuild.Evaluate(empty);
        Equal(clear.Branch, WatchGuildBranch.ClearWatch, "watchguild clear branch");
        Equal(clear.SetsWatchGuild, false, "watchguild clear no set");
        Equal(clear.ClearsWatchGuild, true, "watchguild clears");
        Equal(clear.SendsSysMsg, true, "watchguild clear sends msg");
        Equal(clear.MessageColor, 0x38FF, "watchguild clear colour");
    }
}

static void VerifyServerGuildSwitch()
{
    var open = NativeGmServerGuildSwitch.Evaluate(guardPass: true, param: 1);
    Equal(open.Branch, ServerGuildSwitchBranch.Open, "serverguildswitch open branch");
    Equal(open.WritesGlobal, true, "serverguildswitch open writes");
    Equal(open.GlobalEa, 0x007D600Cu, "serverguildswitch global ea");
    Equal(open.GlobalValue, 1, "serverguildswitch open value");
    Equal(open.SendsSysMsg, true, "serverguildswitch open sends msg");
    Equal(open.MessageColor, 0xFCFF, "serverguildswitch open colour");

    var close = NativeGmServerGuildSwitch.Evaluate(guardPass: true, param: 0);
    Equal(close.Branch, ServerGuildSwitchBranch.Close, "serverguildswitch close branch");
    Equal(close.WritesGlobal, true, "serverguildswitch close writes");
    Equal(close.GlobalValue, 0, "serverguildswitch close value");
    Equal(close.MessageColor, 0xFCFF, "serverguildswitch close colour");

    var noChange = NativeGmServerGuildSwitch.Evaluate(guardPass: true, param: 7);
    Equal(noChange.Branch, ServerGuildSwitchBranch.NoChange, "serverguildswitch nochange branch");
    Equal(noChange.WritesGlobal, false, "serverguildswitch nochange no write");
    Equal(noChange.SendsSysMsg, false, "serverguildswitch nochange silent");

    var blocked = NativeGmServerGuildSwitch.Evaluate(guardPass: false, param: 1);
    Equal(blocked.Branch, ServerGuildSwitchBranch.GuardBlocked, "serverguildswitch guard-block branch");
    Equal(blocked.WritesGlobal, false, "serverguildswitch guard-block no write");
    Equal(blocked.SendsSysMsg, true, "serverguildswitch guard-block sends msg");
    Equal(blocked.MessageColor, 0xFFDB, "serverguildswitch guard-block colour");
}

static void VerifyNullsubStubs()
{
    (GmGuildCastleCommand cmd, uint ea)[] cases =
    {
        (GmGuildCastleCommand.GuildPoint,     0x006C2908u),
        (GmGuildCastleCommand.GuildWarOn,     0x006C290Cu),
        (GmGuildCastleCommand.GuildWarOff,    0x006C2910u),
        (GmGuildCastleCommand.ReportGuildWar, 0x006C2914u),
    };
    foreach (var c in cases)
    {
        var o = NativeGmGuildWarStub.Evaluate(c.cmd);
        Equal(o.CallsNullsub, true, $"{c.cmd} calls nullsub");
        Equal(o.NullsubEa, c.ea, $"{c.cmd} nullsub ea");
        Equal(o.CoreBodyDeferred, false, $"{c.cmd} nullsub not deferred (verified empty)");
        Equal(o.MutatesState, false, $"{c.cmd} no effect");
        Equal(o.SendsSysMsg, false, $"{c.cmd} no message");
    }

    // an implemented command must not be routed through the nullsub-stub path
    var threw = false;
    try { NativeGmGuildWarStub.Evaluate(GmGuildCastleCommand.ChgCastleWar); }
    catch (InvalidOperationException) { threw = true; }
    Equal(threw, true, "non-nullsub command rejected by nullsub path");
}

static void VerifyInvalidStubs()
{
    foreach (var c in new[] { GmGuildCastleCommand.SetGuildLord, GmGuildCastleCommand.GuildForbid,
                              GmGuildCastleCommand.SelfAddGuild, GmGuildCastleCommand.ReNameGuild })
    {
        var o = NativeGmGuildInvalidStub.Evaluate(c);
        Equal(o.SendsSysMsg, true, $"{c} sends Invalid");
        Equal(o.MessageText, "Invalid", $"{c} message text");
        Equal(o.MessageColor, 0x38FF, $"{c} message colour");
        Equal(o.MessageTextEa, 0x0062C638u, $"{c} Invalid string ea");
        Equal(o.MutatesState, false, $"{c} no effect");
    }

    var threw = false;
    try { NativeGmGuildInvalidStub.Evaluate(GmGuildCastleCommand.LookSaGold); }
    catch (InvalidOperationException) { threw = true; }
    Equal(threw, true, "non-invalid command rejected by invalid-reply path");
}

static void VerifyNoOps()
{
    // default no-ops: handled byte cleared to 0 (DispatchesToDefaultCase == true)
    foreach (var c in new[] { GmGuildCastleCommand.LoadHeroStrike, GmGuildCastleCommand.ChgGuildValue,
                              GmGuildCastleCommand.DreamCastleScore, GmGuildCastleCommand.ChgDoubleCastleWar })
    {
        var o = NativeGmGuildCastleCommands.EvaluateNoOp(c);
        Equal(o.Recognized, true, $"{c} recognized");
        Equal(o.DispatchesToDefaultCase, true, $"{c} to def_622B15");
        Equal(o.MutatesState, false, $"{c} no effect");
        Equal(o.SendsResponse, false, $"{c} no response");
    }

    // empty-case no-ops: handled byte stays 1 (DispatchesToDefaultCase == false), still no effect / no msg
    foreach (var c in new[] { GmGuildCastleCommand.MakeGuild, GmGuildCastleCommand.DelGuild })
    {
        var o = NativeGmGuildCastleCommands.EvaluateNoOp(c);
        Equal(o.Recognized, true, $"{c} recognized");
        Equal(o.DispatchesToDefaultCase, false, $"{c} on empty case (not def)");
        Equal(o.MutatesState, false, $"{c} no effect");
        Equal(o.SendsResponse, false, $"{c} no response");
    }

    // implemented / stubbed commands must NOT be routed through the no-op path
    var threw = false;
    try { NativeGmGuildCastleCommands.EvaluateNoOp(GmGuildCastleCommand.ChgCastleWar); }
    catch (InvalidOperationException) { threw = true; }
    Equal(threw, true, "implemented command rejected by no-op path");
}
