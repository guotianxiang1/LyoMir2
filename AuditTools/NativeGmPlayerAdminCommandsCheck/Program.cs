using GameSvr;

// Contract check for the dormant PLAYER/CHARACTER-SELF admin GM command family model
// (GameSvr/Services/NativeGmPlayerAdminCommands.cs), locked against the Hex-Rays-verified original
// dispatcher sub_622820 (single switch, table jpt_622B15 @0x00622B1C) in the unpacked M2Server image.
// Every command in this family is an INLINE case inside sub_622820; delegated sub-handlers were
// decompiled (padmin_subs.txt) to pin down the exact result ladders.

try
{
    VerifyDispatcherConstants();
    VerifyOffsetsAndColors();
    VerifyRegistry();

    VerifyAttackMode();
    VerifyMakeGo();
    VerifyChgPkZero();
    VerifyInComePk();
    VerifyChgBodyLuck();
    VerifyChgManKind();
    VerifyChgSex();
    VerifyChgHideState();
    VerifyIncSelfLv();
    VerifyPlayerRename();
    VerifyRelive();

    VerifyUnimplementedNoOp();

    Console.WriteLine(
        "PASS NativeGmPlayerAdminCommandsCheck dispatcher=sub_622820 table=0x622B1C max=750 " +
        "modeled=AttackMode/MakeGo/ChgPkZero/InComePk/ChgBodyLuck/ChgmanKind/ChgSex/ChgHideState/" +
        "IncSelfLv/PlayerRename/Relive registry=18 noop=ChgNewBie(225)");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"NativeGmPlayerAdminCommandsCheck FAIL: {ex.Message}");
    return 1;
}

// ---- single, non-overloaded helpers ----
static void Equal<T>(T expected, T actual, string msg)
{
    if (!System.Collections.Generic.EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"{msg}: expected <{expected}>, got <{actual}>");
}

static void Assert(bool cond, string msg)
{
    if (!cond) throw new Exception(msg);
}

static void VerifyDispatcherConstants()
{
    Equal(0x00622820u, NativeGmPlayerAdminCommands.DispatcherEa, "dispatcher ea");
    Equal(0x00621F28u, NativeGmPlayerAdminCommands.IndexLookupEa, "index lookup ea");
    Equal(0x00622B1Cu, NativeGmPlayerAdminCommands.JumpTableEa, "jump table ea");
    Equal(750, NativeGmPlayerAdminCommands.SwitchMaxIndex, "switch max index");
    Equal(0x0062B648u, NativeGmPlayerAdminCommands.DefaultCaseEa, "default case ea");
}

static void VerifyOffsetsAndColors()
{
    Equal(0x071, NativeGmPlayerAdminCommands.GenderOffset, "gender offset");
    Equal(0x072, NativeGmPlayerAdminCommands.JobOffset, "job offset");
    Equal(0x074, NativeGmPlayerAdminCommands.DeadFlagOffset, "dead flag offset");
    Equal(0x155, NativeGmPlayerAdminCommands.NameColorOffset, "name colour offset");
    Equal(0x160, NativeGmPlayerAdminCommands.PkPointOffset, "pk point offset");
    Equal(0x164, NativeGmPlayerAdminCommands.BodyLuckOffset, "body luck offset");
    Equal(0x278, NativeGmPlayerAdminCommands.LevelOffset, "level offset");
    Equal(0x1FC, NativeGmPlayerAdminCommands.LevelMirrorOffset, "level mirror offset");
    Equal(0x2E4, NativeGmPlayerAdminCommands.HideStateOffset, "hide state offset");
    Equal(0xAED, NativeGmPlayerAdminCommands.AttackModeOffset, "attack mode offset");
    Equal(0xBB0, NativeGmPlayerAdminCommands.HeroPtrOffset, "hero ptr offset");

    Equal(0xFFDB, NativeGmPlayerAdminCommands.ColorConfirm, "confirm colour");
    Equal(0x38FF, NativeGmPlayerAdminCommands.ColorRed, "red colour");
    Equal(0xFCFF, NativeGmPlayerAdminCommands.ColorSetPkEmpty, "setpk-empty colour");

    // Message IDENTS, not colours.  sub_766060 @0x766069 `mov word [ebp-6],cx` then
    // @0x76608E `mov word [ebx],ax` stores cx as the queued record's ident field.
    // 0x27B1 = delayed revive (GM @Relive @0x625A43 with a 500 ms delay @0x625A3E;
    // PAS dorelive sub_6E13C8 @0x6E13E9), 0x27B0 = the immediate notice (@0x6E1403).
    Equal(0x27B1, NativeGmPlayerAdminCommands.DelayedReviveIdent, "delayed-revive ident");
    Equal(0x27B0, NativeGmPlayerAdminCommands.ImmediateNoticeIdent, "immediate-notice ident");

    // domain constants proven by the decompiles
    Equal(5, NativeGmPlayerAdminCommands.AttackModeMax, "attack mode max");
    Equal(500, NativeGmPlayerAdminCommands.LevelHardCap, "level cap");
    Equal(1, NativeGmPlayerAdminCommands.LevelFloor, "level floor");
    Equal(-10, NativeGmPlayerAdminCommands.BodyLuckMin, "luck min");
    Equal(5, NativeGmPlayerAdminCommands.BodyLuckMax, "luck max");
    Equal(100, NativeGmPlayerAdminCommands.InComePkStep, "incomepk step");
}

static void VerifyRegistry()
{
    // (command, exact table name, dispatch index, required permission, implemented)
    (GmPlayerAdminCommand cmd, string name, int idx, int perm, bool impl)[] expected =
    {
        (GmPlayerAdminCommand.AttackMode,   "AttackMode",   26,  0, true),
        (GmPlayerAdminCommand.MakeGo,       "MakeGo",       60,  3, true),
        (GmPlayerAdminCommand.SuperCome,    "supercome",    61,  3, true),
        (GmPlayerAdminCommand.ChgPkZero,    "ChgPkZero",    89,  4, true),
        (GmPlayerAdminCommand.ShowPk,       "ShowPk",       90,  4, true),
        (GmPlayerAdminCommand.InComePk,     "InComePk",     91,  4, true),
        (GmPlayerAdminCommand.ChgBodyLuck,  "ChgBodyLuck",  92,  4, true),
        (GmPlayerAdminCommand.ChgManKind,   "ChgmanKind",   97,  4, true),
        (GmPlayerAdminCommand.ChgSex,       "ChgSex",       98,  4, true),
        (GmPlayerAdminCommand.ChgNameClr,   "ChgNameClr",   99,  4, true),
        (GmPlayerAdminCommand.ChgHideState, "ChgHideState", 102, 4, true),
        (GmPlayerAdminCommand.IncSelfLv,    "IncSelfLv",    104, 4, true),
        (GmPlayerAdminCommand.PlayerRename, "PlayerRename", 105, 4, true),
        (GmPlayerAdminCommand.HeroRename,   "HeroRename",   106, 4, true),
        (GmPlayerAdminCommand.Relive,       "Relive",       193, 4, true),
        (GmPlayerAdminCommand.UpSelfGrade,  "UpSelfGrade",  217, 5, true),
        (GmPlayerAdminCommand.SetPkLv,      "SetPkLv",      259, 3, true),
        (GmPlayerAdminCommand.ChgNewBie,    "ChgNewBie",    225, 5, false),
    };

    Equal(expected.Length, NativeGmPlayerAdminCommands.All.Count, "registry count");

    foreach (var e in expected)
    {
        var info = NativeGmPlayerAdminCommands.Info(e.cmd);
        Equal(e.name, info.Name, $"{e.cmd} name");
        Equal(e.idx, info.DispatchIndex, $"{e.cmd} index");
        Equal(e.perm, info.RequiredPermission, $"{e.cmd} perm");
        Equal(e.impl, info.Implemented, $"{e.cmd} implemented");

        // index must be within the switch range
        Assert(info.DispatchIndex >= 0 && info.DispatchIndex <= NativeGmPlayerAdminCommands.SwitchMaxIndex,
            $"{e.cmd} index in switch range");

        // implemented => inline case (not the default label); no-op => exactly the default label
        if (e.impl)
            Assert(info.CaseAddress != NativeGmPlayerAdminCommands.DefaultCaseEa, $"{e.cmd} has a real case");
        else
            Equal(NativeGmPlayerAdminCommands.DefaultCaseEa, info.CaseAddress, $"{e.cmd} lands on def_622B15");
    }

    // exactly one no-op in this family
    int noop = 0;
    foreach (var info in NativeGmPlayerAdminCommands.All)
        if (!info.Implemented) noop++;
    Equal(1, noop, "exactly one no-op (ChgNewBie)");
}

static void VerifyAttackMode()
{
    // cycles 0..5 then wraps to 0; broadcasts feature; never a SysMsg
    Equal(1, NativeGmAttackMode.Evaluate(0).NewMode, "attackmode 0->1");
    Equal(4, NativeGmAttackMode.Evaluate(3).NewMode, "attackmode 3->4");
    Equal(5, NativeGmAttackMode.Evaluate(4).NewMode, "attackmode 4->5");
    Equal(0, NativeGmAttackMode.Evaluate(5).NewMode, "attackmode 5->0 (wrap)");
    var m = NativeGmAttackMode.Evaluate(2);
    Equal(2, m.OldMode, "attackmode old preserved");
    Assert(m.BroadcastsFeature, "attackmode broadcasts");
    Assert(!m.SendsSysMsg, "attackmode no sysmsg");
}

static void VerifyMakeGo()
{
    var self = NativeGmMakeGo.Evaluate("", false);
    Equal(MakeGoBranch.RecalledSelf, self.Branch, "makego empty=self");
    Assert(self.Recalls && !self.SendsSysMsg, "makego self recalls silently");

    var notFound = NativeGmMakeGo.Evaluate("bob", false);
    Equal(MakeGoBranch.PlayerNotFound, notFound.Branch, "makego not-found branch");
    Assert(!notFound.Recalls && notFound.SendsSysMsg, "makego not-found messages, no recall");
    Equal(NativeGmPlayerAdminCommands.ColorRed, notFound.MessageColor, "makego not-found colour");

    var found = NativeGmMakeGo.Evaluate("bob", true);
    Equal(MakeGoBranch.RecalledPlayer, found.Branch, "makego found branch");
    Assert(found.Recalls && !found.SendsSysMsg, "makego found recalls silently");
}

static void VerifyChgPkZero()
{
    var ok = NativeGmChgPkZero.Evaluate(true);
    Equal(ChgPkZeroBranch.Cleared, ok.Branch, "chgpkzero cleared branch");
    Assert(ok.PkSetToZero && ok.RefreshesAppearance && ok.SendsConfirmText && ok.SendsSysMsg, "chgpkzero clears+confirms");
    Equal(NativeGmPlayerAdminCommands.ColorConfirm, ok.MessageColor, "chgpkzero colour");

    var miss = NativeGmChgPkZero.Evaluate(false);
    Equal(ChgPkZeroBranch.PlayerNotFound, miss.Branch, "chgpkzero not-found branch");
    Assert(!miss.PkSetToZero && !miss.SendsConfirmText, "chgpkzero not-found does not clear/format");
    Assert(miss.SendsSysMsg, "chgpkzero still calls sysmsg (empty)");
}

static void VerifyInComePk()
{
    var a = NativeGmInComePk.Evaluate(0);
    Equal(100, a.NewPkPoint, "incomepk 0->100");
    Assert(a.RefreshesAppearance, "incomepk 0 crosses bucket => refresh");
    Assert(!a.SendsSysMsg, "incomepk no sysmsg");

    Equal(150, NativeGmInComePk.Evaluate(50).NewPkPoint, "incomepk 50->150");
    Assert(NativeGmInComePk.Evaluate(150).RefreshesAppearance, "incomepk 150->250 bucket 2 => refresh");
    Assert(!NativeGmInComePk.Evaluate(250).RefreshesAppearance, "incomepk 250->350 bucket 3 => no refresh");
    Assert(NativeGmInComePk.Evaluate(199).RefreshesAppearance, "incomepk 199->299 bucket 2 => refresh");
}

static void VerifyChgBodyLuck()
{
    var miss = NativeGmChgBodyLuck.Evaluate(false, 0, 5);
    Equal(ChgBodyLuckBranch.PlayerNotFound, miss.Branch, "chgbodyluck not-found branch");
    Assert(!miss.LuckApplied, "chgbodyluck not-found no apply");
    Equal(NativeGmPlayerAdminCommands.ColorRed, miss.MessageColor, "chgbodyluck not-found colour");

    var add = NativeGmChgBodyLuck.Evaluate(true, 0, 3);
    Equal(ChgBodyLuckBranch.Applied, add.Branch, "chgbodyluck applied branch");
    Equal(3, add.NewLuck, "chgbodyluck additive 0+3");
    Equal(NativeGmPlayerAdminCommands.ColorConfirm, add.MessageColor, "chgbodyluck applied colour");

    Equal(5, NativeGmChgBodyLuck.Evaluate(true, 0, 100).NewLuck, "chgbodyluck clamp high +5");
    Equal(-10, NativeGmChgBodyLuck.Evaluate(true, 0, -100).NewLuck, "chgbodyluck clamp low -10");
    Equal(5, NativeGmChgBodyLuck.Evaluate(true, 4, 3).NewLuck, "chgbodyluck 4+3 clamp 5");
    Equal(-10, NativeGmChgBodyLuck.Evaluate(true, -8, -5).NewLuck, "chgbodyluck -8-5 clamp -10");
}

static void VerifyChgManKind()
{
    var w = NativeGmChgManKind.Evaluate(0);
    Equal(ChgManKindBranch.JobChanged, w.Branch, "chgmankind job0 branch");
    Assert(w.JobSet && w.RecalculatesStats && w.SendsSysMsg, "chgmankind job0 sets+recalcs+msg");
    Equal(0, w.NewJob, "chgmankind job0 index");
    Equal(NativeGmPlayerAdminCommands.ColorConfirm, w.MessageColor, "chgmankind colour");

    Equal(3, NativeGmChgManKind.Evaluate(3).NewJob, "chgmankind job3 index");

    var unknown = NativeGmChgManKind.Evaluate(-1);
    Equal(ChgManKindBranch.UnknownJob, unknown.Branch, "chgmankind unknown branch");
    Assert(!unknown.JobSet && !unknown.RecalculatesStats && !unknown.SendsSysMsg, "chgmankind unknown = silent no-op");
    Equal(-1, unknown.NewJob, "chgmankind unknown job = -1");

    Equal(ChgManKindBranch.UnknownJob, NativeGmChgManKind.Evaluate(4).Branch, "chgmankind out-of-range = unknown");
}

static void VerifyChgSex()
{
    var r = NativeGmChgSex.Evaluate();
    Assert(r.TogglesGender && r.SendsSysMsg, "chgsex toggles + confirms");
    Equal(NativeGmPlayerAdminCommands.ColorConfirm, r.MessageColor, "chgsex colour");
}

static void VerifyChgHideState()
{
    var r = NativeGmChgHideState.Evaluate();
    Assert(r.TogglesHideFlag && r.BroadcastsState, "chghidestate toggles + broadcasts");
    Assert(!r.SendsSysMsg, "chghidestate sends NO sysmsg (unlike the live stub)");
}

static void VerifyIncSelfLv()
{
    Equal(200, NativeGmIncSelfLv.Evaluate(200).NewLevel, "incselflv 200");
    Equal(500, NativeGmIncSelfLv.Evaluate(600).NewLevel, "incselflv cap 500");
    Equal(1, NativeGmIncSelfLv.Evaluate(0).NewLevel, "incselflv floor 1 (from 0)");
    Equal(1, NativeGmIncSelfLv.Evaluate(-5).NewLevel, "incselflv floor 1 (from -5)");
    Equal(500, NativeGmIncSelfLv.Evaluate(500).NewLevel, "incselflv 500 stays");
    var r = NativeGmIncSelfLv.Evaluate(300);
    Assert(r.RecalculatesStats && !r.SendsSysMsg, "incselflv recalcs, no sysmsg");
}

static void VerifyPlayerRename()
{
    var hint = NativeGmPlayerRename.Evaluate("", false);
    Equal(PlayerRenameBranch.UsageHint, hint.Branch, "playerrename usage-hint branch");
    Assert(!hint.GrantsRenameChance, "playerrename empty grants nothing");
    Equal(NativeGmPlayerAdminCommands.ColorConfirm, hint.MessageColor, "playerrename hint colour");

    var miss = NativeGmPlayerRename.Evaluate("bob", false);
    Equal(PlayerRenameBranch.PlayerNotFound, miss.Branch, "playerrename not-found branch");
    Equal(NativeGmPlayerAdminCommands.ColorRed, miss.MessageColor, "playerrename not-found colour");

    var ok = NativeGmPlayerRename.Evaluate("bob", true);
    Equal(PlayerRenameBranch.Granted, ok.Branch, "playerrename granted branch");
    Assert(ok.GrantsRenameChance, "playerrename grants a rename chance");
}

static void VerifyRelive()
{
    var dead = NativeGmRelive.Evaluate(true);
    Equal(ReliveBranch.Revived, dead.Branch, "relive dead->revived");
    Assert(dead.PerformsRevive && dead.SendsNotice, "relive revives + notices");
    Assert(!dead.SendsSysMsg, "relive no direct sysmsg");

    var alive = NativeGmRelive.Evaluate(false);
    Equal(ReliveBranch.NotDead, alive.Branch, "relive alive->no-op");
    Assert(!alive.PerformsRevive && !alive.SendsNotice, "relive alive is silent no-op");
}

static void VerifyUnimplementedNoOp()
{
    var info = NativeGmPlayerAdminCommands.Info(GmPlayerAdminCommand.ChgNewBie);
    Assert(!info.Implemented, "ChgNewBie not implemented");
    Equal(NativeGmPlayerAdminCommands.DefaultCaseEa, info.CaseAddress, "ChgNewBie -> def_622B15");

    var noop = NativeGmPlayerAdminCommands.EvaluateUnimplemented(GmPlayerAdminCommand.ChgNewBie);
    Assert(noop.Recognized, "ChgNewBie recognized by table");
    Assert(noop.DispatchesToDefaultCase, "ChgNewBie dispatches to default case");
    Assert(!noop.MutatesState, "ChgNewBie mutates nothing");
    Assert(!noop.SendsResponse, "ChgNewBie sends nothing");

    // EvaluateUnimplemented must refuse an implemented command
    bool threw = false;
    try { NativeGmPlayerAdminCommands.EvaluateUnimplemented(GmPlayerAdminCommand.IncSelfLv); }
    catch (InvalidOperationException) { threw = true; }
    Assert(threw, "EvaluateUnimplemented(IncSelfLv) must throw");
}
