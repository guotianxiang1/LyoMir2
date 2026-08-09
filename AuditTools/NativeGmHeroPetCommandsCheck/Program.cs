using GameSvr;

// Contract check for the dormant HERO / PET / SUMMON GM command family model
// (GameSvr/Services/NativeGmHeroPetCommands.cs), locked against the Hex-Rays-verified original dispatcher
// sub_622820 (single switch, table jpt_622B15 @0x00622B1C) in the unpacked M2Server image. Evidence dumps:
// staging/update_clothes_4637_ida_work/{disp_decomp.txt,big622820.txt,world_scan_out.txt,all_strings.txt}.

try
{
    VerifyDispatcherConstants();
    VerifyRegistry();
    VerifyCreateHero();
    VerifyMakeMyHero();
    VerifyCoreForwards();
    VerifySetCallHeroInterval();
    VerifyAddHeroExp();
    VerifyNoOps();

    Console.WriteLine(
        "PASS NativeGmHeroPetCommandsCheck dispatcher=sub_622820 table=0x622B1C max=750 " +
        "implemented=CreateHero/MakeMyHero/DelHero/CallHero/SetCallHero/SetCallHeroInterval/AddHeroExp/UpUserHeroLv " +
        "defaultNoop=SetPetSwitch/herostrike emptyNoop=LeaveHero/CreateProtectHero " +
        "csharpOverSends=SetPetSwitch/LeaveHero");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"NativeGmHeroPetCommandsCheck FAIL: {ex.Message}");
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
    Equal(NativeGmHeroPetCommands.DispatcherEa, 0x00622820u, "dispatcher ea");
    Equal(NativeGmHeroPetCommands.HandledByteSetEa, 0x0062284Du, "handled-byte set ea");
    Equal(NativeGmHeroPetCommands.IndexLookupEa, 0x00621F28u, "index lookup ea");
    Equal(NativeGmHeroPetCommands.JumpTableEa, 0x00622B1Cu, "jump table ea");
    Equal(NativeGmHeroPetCommands.SwitchMaxIndex, 750, "switch max index");
    Equal(NativeGmHeroPetCommands.DefaultCaseEa, 0x0062B648u, "default case ea");
    Equal(NativeGmHeroPetCommands.EmptyCaseEa, 0x0062B64Cu, "empty case ea");
    // the two no-op labels are distinct and adjacent (def_622B15 + 4 == loc_62B64C)
    Equal(NativeGmHeroPetCommands.DefaultCaseEa + 4, NativeGmHeroPetCommands.EmptyCaseEa, "def+4 == empty case");
    Equal(NativeGmHeroPetCommands.AddHeroExpFixedAmount, 100000000, "fixed hero exp");
    Equal(NativeGmHeroPetCommands.MainCallIntervalGlobalEa, 0x007D65E8u, "main interval global");
    Equal(NativeGmHeroPetCommands.ViceCallIntervalGlobalEa, 0x007D6368u, "vice interval global");
}

static void VerifyRegistry()
{
    // (command, name, index, perm, kind, overSends, underImpl)
    (GmHeroPetCommand cmd, string name, int idx, int perm, GmHeroPetHandlerKind kind, bool over, bool under)[] expected =
    {
        (GmHeroPetCommand.CreateHero,          "CreateHero",          246, 5, GmHeroPetHandlerKind.ImplementedCase, false, false),
        (GmHeroPetCommand.MakeMyHero,          "MakeMyHero",          147, 4, GmHeroPetHandlerKind.ImplementedCase, false, true),
        (GmHeroPetCommand.DelHero,             "DelHero",              81, 4, GmHeroPetHandlerKind.ImplementedCase, false, false),
        (GmHeroPetCommand.CallHero,            "CallHero",            250, 5, GmHeroPetHandlerKind.ImplementedCase, false, false),
        (GmHeroPetCommand.SetCallHero,         "SetCallHero",         270, 5, GmHeroPetHandlerKind.ImplementedCase, false, false),
        (GmHeroPetCommand.SetCallHeroInterval, "SetCallHeroInterval", 271, 5, GmHeroPetHandlerKind.ImplementedCase, false, false),
        (GmHeroPetCommand.AddHeroExp,          "AddHeroExp",          493, 4, GmHeroPetHandlerKind.ImplementedCase, false, false),
        (GmHeroPetCommand.UpUserHeroLv,        "UpUserHeroLv",        222, 5, GmHeroPetHandlerKind.ImplementedCase, false, false),
        (GmHeroPetCommand.SetPetSwitch,        "SetPetSwitch",        341, 4, GmHeroPetHandlerKind.DefaultNoOp,     true,  false),
        (GmHeroPetCommand.Herostrike,          "herostrike",          325, 5, GmHeroPetHandlerKind.DefaultNoOp,     false, false),
        (GmHeroPetCommand.LeaveHero,           "LeaveHero",           442, 4, GmHeroPetHandlerKind.EmptyCaseNoOp,   true,  false),
        (GmHeroPetCommand.CreateProtectHero,   "CreateProtectHero",   348, 4, GmHeroPetHandlerKind.EmptyCaseNoOp,   false, false),
    };

    Equal(NativeGmHeroPetCommands.All.Count, expected.Length, "registry count");
    foreach (var e in expected)
    {
        var info = NativeGmHeroPetCommands.Info(e.cmd);
        Equal(info.Name, e.name, $"{e.cmd} name");
        Equal(info.DispatchIndex, e.idx, $"{e.cmd} index");
        Equal(info.RequiredPermission, e.perm, $"{e.cmd} perm");
        Equal(info.HandlerKind, e.kind, $"{e.cmd} kind");
        Equal(info.CSharpStubOverSends, e.over, $"{e.cmd} over-sends flag");
        Equal(info.CSharpStubUnderImplements, e.under, $"{e.cmd} under-impl flag");
        Equal(info.DispatchIndex >= 0 && info.DispatchIndex <= NativeGmHeroPetCommands.SwitchMaxIndex, true,
            $"{e.cmd} index in switch range");

        // case address matches the terminal shape
        switch (e.kind)
        {
            case GmHeroPetHandlerKind.ImplementedCase:
                Equal(info.CaseAddress != NativeGmHeroPetCommands.DefaultCaseEa
                      && info.CaseAddress != NativeGmHeroPetCommands.EmptyCaseEa, true, $"{e.cmd} distinct case addr");
                break;
            case GmHeroPetHandlerKind.DefaultNoOp:
                Equal(info.CaseAddress, NativeGmHeroPetCommands.DefaultCaseEa, $"{e.cmd} on def_622B15");
                break;
            case GmHeroPetHandlerKind.EmptyCaseNoOp:
                Equal(info.CaseAddress, NativeGmHeroPetCommands.EmptyCaseEa, $"{e.cmd} on loc_62B64C");
                break;
        }
    }
}

static void VerifyCreateHero()
{
    // unmatched job token -> silent no-op, no core call
    foreach (var bad in new[] { 0, -1, 7, 99 })
    {
        var u = NativeGmCreateHero.Evaluate(bad);
        Equal(u.Branch, CreateHeroBranch.JobUnmatched, $"createhero unmatched {bad}");
        Equal(u.CallsCore, false, $"createhero unmatched no core {bad}");
        Equal(u.JobSelector, 0, $"createhero unmatched selector {bad}");
        Equal(u.SendsSysMsg, false, $"createhero unmatched silent {bad}");
    }

    // each of the six job selectors -> core sub_6C9C00, no message
    for (int sel = 1; sel <= 6; sel++)
    {
        var m = NativeGmCreateHero.Evaluate(sel);
        Equal(m.Branch, CreateHeroBranch.JobMatched, $"createhero matched {sel}");
        Equal(m.CallsCore, true, $"createhero matched core {sel}");
        Equal(m.JobSelector, sel, $"createhero selector {sel}");
        Equal(m.CoreEa, 0x006C9C00u, $"createhero core ea {sel}");
        Equal(m.CoreBodyDeferred, true, $"createhero core deferred {sel}");
        Equal(m.SendsSysMsg, false, $"createhero matched silent {sel}");
    }
}

static void VerifyMakeMyHero()
{
    var ok = NativeGmMakeMyHero.Evaluate(createSucceeded: true);
    Equal(ok.Branch, MakeMyHeroBranch.CreatedWithNotice, "makemyhero success branch");
    Equal(ok.CallsCreateCore, true, "makemyhero calls create core");
    Equal(ok.CreateCoreEa, 0x00604E3Cu, "makemyhero core ea");
    Equal(ok.SendsSysMsg, true, "makemyhero success sends notice");
    Equal(ok.MessageColor, 0xFCFF, "makemyhero notice colour");

    var fail = NativeGmMakeMyHero.Evaluate(createSucceeded: false);
    Equal(fail.Branch, MakeMyHeroBranch.CreateFailedSilent, "makemyhero fail branch");
    Equal(fail.CallsCreateCore, true, "makemyhero still attempts create");
    Equal(fail.SendsSysMsg, false, "makemyhero fail silent");
    Equal(fail.MessageColor, 0, "makemyhero fail no colour");
}

static void VerifyCoreForwards()
{
    var del = NativeGmDelHero.Evaluate();
    Equal(del.CallsCore, true, "delhero calls core");
    Equal(del.CoreEa, 0x006BF5ACu, "delhero core ea");
    Equal(del.SendsSysMsg, false, "delhero silent");
    Equal(del.CoreBodyDeferred, true, "delhero core deferred");

    var call = NativeGmCallHero.Evaluate(1, 2);
    Equal(call.CoreEa, 0x006C9D28u, "callhero core ea");
    Equal(call.SendsSysMsg, false, "callhero silent");

    var setcall = NativeGmSetCallHero.Evaluate(1, 2);
    Equal(setcall.CoreEa, 0x006C9D88u, "setcallhero core ea");
    Equal(setcall.SendsSysMsg, false, "setcallhero silent");

    var uplv = NativeGmUpUserHeroLv.Evaluate("Bob", 55);
    Equal(uplv.CoreEa, 0x006D1C20u, "upuserherolv core ea");
    Equal(uplv.SendsSysMsg, false, "upuserherolv silent");
    Equal(uplv.CoreBodyDeferred, true, "upuserherolv core deferred");
}

static void VerifySetCallHeroInterval()
{
    var o = NativeGmSetCallHeroInterval.Evaluate(mainInterval: 3000, viceInterval: 5000);
    Equal(o.MainInterval, 3000, "interval main value");
    Equal(o.ViceInterval, 5000, "interval vice value");
    Equal(o.WritesMainGlobal, true, "interval writes main global");
    Equal(o.WritesViceGlobal, true, "interval writes vice global");
    Equal(o.MainGlobalEa, 0x007D65E8u, "interval main global ea");
    Equal(o.ViceGlobalEa, 0x007D6368u, "interval vice global ea");
    Equal(o.SendsSysMsg, true, "interval confirms");
    Equal(o.MessageColor, 0x38FF, "interval notice colour");
}

static void VerifyAddHeroExp()
{
    var bad1 = NativeGmAddHeroExp.Evaluate("", true);
    Equal(bad1.Branch, AddHeroExpBranch.TargetInvalid, "addheroexp empty name");
    Equal(bad1.CallsCore, false, "addheroexp empty name no core");
    Equal(bad1.ExpGranted, 0, "addheroexp empty name no exp");

    var bad2 = NativeGmAddHeroExp.Evaluate("Bob", false);
    Equal(bad2.Branch, AddHeroExpBranch.TargetInvalid, "addheroexp offline");
    Equal(bad2.CallsCore, false, "addheroexp offline no core");

    var ok = NativeGmAddHeroExp.Evaluate("Bob", true);
    Equal(ok.Branch, AddHeroExpBranch.Applied, "addheroexp applied");
    Equal(ok.CallsCore, true, "addheroexp applied core");
    Equal(ok.CoreEa, 0x006E2CC0u, "addheroexp core ea");
    Equal(ok.ExpGranted, 100000000, "addheroexp fixed 1e8");
    Equal(ok.SendsSysMsg, false, "addheroexp silent");
}

static void VerifyNoOps()
{
    // default no-ops: handled byte cleared to 0
    foreach (var c in new[] { GmHeroPetCommand.SetPetSwitch, GmHeroPetCommand.Herostrike })
    {
        var o = NativeGmHeroPetCommands.EvaluateNoOp(c);
        Equal(o.Recognized, true, $"{c} recognized");
        Equal(o.HandlerKind, GmHeroPetHandlerKind.DefaultNoOp, $"{c} default kind");
        Equal(o.HandledByteSet, false, $"{c} handled byte cleared");
        Equal(o.MutatesState, false, $"{c} no effect");
        Equal(o.SendsResponse, false, $"{c} no response");
    }

    // empty-case no-ops: handled byte stays 1, still no effect / no message
    foreach (var c in new[] { GmHeroPetCommand.LeaveHero, GmHeroPetCommand.CreateProtectHero })
    {
        var o = NativeGmHeroPetCommands.EvaluateNoOp(c);
        Equal(o.Recognized, true, $"{c} recognized");
        Equal(o.HandlerKind, GmHeroPetHandlerKind.EmptyCaseNoOp, $"{c} empty-case kind");
        Equal(o.HandledByteSet, true, $"{c} handled byte set");
        Equal(o.MutatesState, false, $"{c} no effect");
        Equal(o.SendsResponse, false, $"{c} no response");
    }

    // implemented commands must NOT be routed through the no-op path
    var threw = false;
    try { NativeGmHeroPetCommands.EvaluateNoOp(GmHeroPetCommand.CreateHero); }
    catch (InvalidOperationException) { threw = true; }
    Equal(threw, true, "implemented command rejected by no-op path");
}
