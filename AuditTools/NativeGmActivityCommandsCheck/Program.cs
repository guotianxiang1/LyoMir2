using GameSvr;

// Contract check for the dormant ACTIVITY / TITLE / RANK (活动 / 封号称号 / 排行) GM command family model
// (GameSvr/Services/NativeGmActivityCommands.cs), locked against the Hex-Rays-verified original dispatcher
// sub_622820 (single switch, table jpt_622B15 @0x00622B1C) in the unpacked M2Server image.
// Evidence dumps: D:/loym2/staging/update_clothes_4637_ida_work/{disp_decomp.txt,all_strings.txt}.
// This is family 07: 40 commands — 14 real implemented cases + 2 fixed-reply stubs + 24 default no-ops.

try
{
    VerifyDispatcherConstants();
    VerifyRegistry();
    VerifyHandledByteSemantics();
    VerifyPureCoreForwards();
    VerifySuccessMsgImpls();
    VerifyPushActIdent();
    VerifyAddVote();
    VerifySetGoldActLv();
    VerifySignInAct();
    VerifyMultiActionControllers();
    VerifyStubReplies();
    VerifyNoOps();

    Console.WriteLine(
        "PASS NativeGmActivityCommandsCheck dispatcher=sub_622820 table=0x622B1C max=750 modeled=39/40 " +
        "impl=14(givetitle/quxiaotitle/PushSingleTask/UpdateOrder/AddNWPresent/AddNWAccept/GetS/SetS/" +
        "SignInAct/PushActIdent/SimpleActCtrl/AddVote/GMActCtrl/setGoldActLv) " +
        "fixedReplyStub=2(GetGuildMember=Invalid/addGuildMem=CanNotInsertDirectly) defaultNoop=23 " +
        "(SetActScore idx264 modeled in item family) " +
        "csharp(NO perm drift;live=10;under=AddVote/GMActCtrl/setGoldActLv;" +
        "over=ReloadGoddessConfig/ReloadSnakeConf/SETACHIEVE/ReloadDailyActiveCfg;faithful=SignInAct)");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"NativeGmActivityCommandsCheck FAIL: {ex.Message}");
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
    Equal(NativeGmActivityCommands.DispatcherEa, 0x00622820u, "dispatcher ea");
    Equal(NativeGmActivityCommands.HandledByteSetEa, 0x0062284Du, "handled-byte set ea");
    Equal(NativeGmActivityCommands.IndexLookupEa, 0x00621F28u, "index lookup ea");
    Equal(NativeGmActivityCommands.JumpTableEa, 0x00622B1Cu, "jump table ea");
    Equal(NativeGmActivityCommands.SwitchMaxIndex, 750, "switch max index");
    Equal(NativeGmActivityCommands.DefaultCaseEa, 0x0062B648u, "default case ea");
    Equal(NativeGmActivityCommands.EmptyCaseEa, 0x0062B64Cu, "empty case ea");
    Equal(NativeGmActivityCommands.DefaultCaseEa + 4, NativeGmActivityCommands.EmptyCaseEa, "def+4 == empty case");
    Equal(NativeGmActivityCommands.SysMsgVtableOffset, 0xD4, "sysmsg vtable offset");
    Equal(NativeGmActivityCommands.ColorGreen, 0x38FF, "colour green");
    Equal(NativeGmActivityCommands.ColorYellowNotice, 0xFFDB, "colour yellow");
    Equal(NativeGmActivityCommands.InvalidReplyText, "Invalid", "Invalid reply text");
    Equal(NativeGmActivityCommands.CanNotInsertReplyText, "Can not insert directly", "CanNotInsert reply text");
    Equal(NativeGmActivityCommands.CoreFindCharEa, 0x00652784u, "find-char helper ea");
    Equal(NativeGmActivityCommands.CharGoldActLvOffset, 6173, "setGoldActLv char field offset");
}

static void VerifyRegistry()
{
    // (command, name, index, perm, kind, caseAddr, livePresent, livePerm, over, under)
    (GmActivityCommand cmd, string name, int idx, int perm, GmActivityHandlerKind kind, uint addr,
        bool live, int livePerm, bool over, bool under)[] expected =
    {
        // ---- 14 implemented cases ----
        (GmActivityCommand.Givetitle,      "givetitle",      142, 4, GmActivityHandlerKind.ImplementedCase,   0x0062552Eu, false, 0, false, false),
        (GmActivityCommand.Quxiaotitle,    "quxiaotitle",    143, 4, GmActivityHandlerKind.ImplementedCase,   0x00625541u, false, 0, false, false),
        (GmActivityCommand.PushSingleTask, "PushSingleTask", 197, 4, GmActivityHandlerKind.ImplementedCase,   0x006287F1u, false, 0, false, false),
        (GmActivityCommand.UpdateOrder,    "UpdateOrder",    200, 5, GmActivityHandlerKind.ImplementedCase,   0x00625D19u, false, 0, false, false),
        (GmActivityCommand.AddNWPresent,   "AddNWPresent",   231, 5, GmActivityHandlerKind.ImplementedCase,   0x00626215u, false, 0, false, false),
        (GmActivityCommand.AddNWAccept,    "AddNWAccept",    232, 5, GmActivityHandlerKind.ImplementedCase,   0x00626272u, false, 0, false, false),
        (GmActivityCommand.GetS,           "GetS",           236, 3, GmActivityHandlerKind.ImplementedCase,   0x00625E13u, false, 0, false, false),
        (GmActivityCommand.SetS,           "SetS",           238, 5, GmActivityHandlerKind.ImplementedCase,   0x00625E45u, false, 0, false, false),
        (GmActivityCommand.SignInAct,      "SignInAct",      263, 4, GmActivityHandlerKind.ImplementedCase,   0x006266F8u, true,  4, false, false),
        (GmActivityCommand.PushActIdent,   "PushActIdent",   284, 5, GmActivityHandlerKind.ImplementedCase,   0x00626E88u, false, 0, false, false),
        (GmActivityCommand.SimpleActCtrl,  "SimpleActCtrl",  314, 4, GmActivityHandlerKind.ImplementedCase,   0x00627754u, false, 0, false, false),
        (GmActivityCommand.AddVote,        "AddVote",        331, 5, GmActivityHandlerKind.ImplementedCase,   0x0062791Eu, true,  5, false, true),
        (GmActivityCommand.GMActCtrl,      "GMActCtrl",      345, 4, GmActivityHandlerKind.ImplementedCase,   0x00627A37u, true,  4, false, true),
        (GmActivityCommand.SetGoldActLv,   "setGoldActLv",   496, 5, GmActivityHandlerKind.ImplementedCase,   0x006292A1u, true,  5, false, true),

        // ---- 2 fixed-reply stubs ----
        (GmActivityCommand.GetGuildMember, "GetGuildMember", 355, 3, GmActivityHandlerKind.StubbedFixedReply, 0x00627FBCu, false, 0, false, false),
        (GmActivityCommand.AddGuildMem,    "addGuildMem",    444, 3, GmActivityHandlerKind.StubbedFixedReply, 0x00628AE1u, false, 0, false, false),

        // ---- 23 default no-ops (SetActScore idx264 is modeled in the item family, not here) ----
        (GmActivityCommand.SetTitle,            "SetTitle",            286, 5, GmActivityHandlerKind.DefaultNoOp, 0x0062B648u, false, 0, false, false),
        (GmActivityCommand.DelTitle,            "DelTitle",            287, 5, GmActivityHandlerKind.DefaultNoOp, 0x0062B648u, false, 0, false, false),
        (GmActivityCommand.QueryTitle,          "QueryTitle",          288, 5, GmActivityHandlerKind.DefaultNoOp, 0x0062B648u, false, 0, false, false),
        (GmActivityCommand.CurTitle,            "CurTitle",            289, 5, GmActivityHandlerKind.DefaultNoOp, 0x0062B648u, false, 0, false, false),
        (GmActivityCommand.DeliverTitle,        "DeliverTitle",        290, 5, GmActivityHandlerKind.DefaultNoOp, 0x0062B648u, false, 0, false, false),
        (GmActivityCommand.DrawTitle,           "DrawTitle",           291, 5, GmActivityHandlerKind.DefaultNoOp, 0x0062B648u, false, 0, false, false),
        (GmActivityCommand.OpenTitle,           "OpenTitle",           292, 5, GmActivityHandlerKind.DefaultNoOp, 0x0062B648u, false, 0, false, false),
        (GmActivityCommand.TitleDetail,         "TitleDetail",         293, 5, GmActivityHandlerKind.DefaultNoOp, 0x0062B648u, false, 0, false, false),
        (GmActivityCommand.LoadTitle,           "LoadTitle",           294, 4, GmActivityHandlerKind.DefaultNoOp, 0x0062B648u, false, 0, false, false),
        (GmActivityCommand.PayScore,            "PayScore",            295, 4, GmActivityHandlerKind.DefaultNoOp, 0x0062B648u, true,  4, false, false),
        (GmActivityCommand.ExecTitleCmd,        "ExecTitleCmd",        302, 5, GmActivityHandlerKind.DefaultNoOp, 0x0062B648u, false, 0, false, false),
        (GmActivityCommand.QueryZFF,            "QueryZFF",            303, 5, GmActivityHandlerKind.DefaultNoOp, 0x0062B648u, false, 0, false, false),
        (GmActivityCommand.ShowPayScore,        "showPayScore",        310, 4, GmActivityHandlerKind.DefaultNoOp, 0x0062B648u, true,  4, false, false),
        (GmActivityCommand.ReloadGoddessConfig, "ReloadGoddessConfig", 438, 4, GmActivityHandlerKind.DefaultNoOp, 0x0062B648u, true,  4, true,  false),
        (GmActivityCommand.GoddessVote,         "GoddessVote",         439, 4, GmActivityHandlerKind.DefaultNoOp, 0x0062B648u, false, 0, false, false),
        (GmActivityCommand.ReloadSnakeConf,     "ReloadSnakeConf",     512, 4, GmActivityHandlerKind.DefaultNoOp, 0x0062B648u, true,  4, true,  false),
        (GmActivityCommand.ViewSnakeCost,       "ViewSnakeCost",       513, 4, GmActivityHandlerKind.DefaultNoOp, 0x0062B648u, false, 0, false, false),
        (GmActivityCommand.SetAchieve,          "SETACHIEVE",          543, 5, GmActivityHandlerKind.DefaultNoOp, 0x0062B648u, true,  5, true,  false),
        (GmActivityCommand.Achievement,         "achievement",         546, 3, GmActivityHandlerKind.DefaultNoOp, 0x0062B648u, false, 0, false, false),
        (GmActivityCommand.DaAddTimes,          "DaAddTimes",          558, 4, GmActivityHandlerKind.DefaultNoOp, 0x0062B648u, false, 0, false, false),
        (GmActivityCommand.DaClear,             "DaClear",             559, 4, GmActivityHandlerKind.DefaultNoOp, 0x0062B648u, false, 0, false, false),
        (GmActivityCommand.SetActiveValue,      "SetActiveValue",      560, 4, GmActivityHandlerKind.DefaultNoOp, 0x0062B648u, false, 0, false, false),
        (GmActivityCommand.ReloadDailyActiveCfg,"ReloadDailyActiveCfg",561, 4, GmActivityHandlerKind.DefaultNoOp, 0x0062B648u, true,  4, true,  false),
    };

    Equal(NativeGmActivityCommands.All.Count, expected.Length, "registry count");
    Equal(expected.Length, 39, "family-07 commands modeled here (40 total; SetActScore in item model)");

    int impl = 0, stub = 0, def = 0, live = 0, over = 0, under = 0;
    var seenIdx = new System.Collections.Generic.HashSet<int>();
    foreach (var e in expected)
    {
        var info = NativeGmActivityCommands.Info(e.cmd);
        Equal(info.Name, e.name, $"{e.cmd} name");
        Equal(info.DispatchIndex, e.idx, $"{e.cmd} index");
        Equal(info.RequiredPermission, e.perm, $"{e.cmd} perm");
        Equal(info.HandlerKind, e.kind, $"{e.cmd} kind");
        Equal(info.CaseAddress, e.addr, $"{e.cmd} case addr");
        Equal(info.CSharpLivePresent, e.live, $"{e.cmd} live present");
        Equal(info.CSharpLivePermission, e.livePerm, $"{e.cmd} live perm");
        Equal(info.CSharpStubOverSends, e.over, $"{e.cmd} over-sends flag");
        Equal(info.CSharpStubUnderImplements, e.under, $"{e.cmd} under-impl flag");
        Equal(info.DispatchIndex >= 0 && info.DispatchIndex <= NativeGmActivityCommands.SwitchMaxIndex, true,
            $"{e.cmd} index in switch range");
        Equal(seenIdx.Add(info.DispatchIndex), true, $"{e.cmd} index unique");

        // family 07 has NO perm drift: every live command's declared perm equals the original record perm
        if (e.live)
        {
            Equal(info.CSharpLivePermission, info.RequiredPermission, $"{e.cmd} live perm matches original (no drift)");
            live++;
        }
        if (e.over) over++;
        if (e.under) under++;

        // case-address terminal shape matches the handler kind
        switch (e.kind)
        {
            case GmActivityHandlerKind.ImplementedCase:
            case GmActivityHandlerKind.StubbedFixedReply:
                Equal(info.CaseAddress != NativeGmActivityCommands.DefaultCaseEa
                      && info.CaseAddress != NativeGmActivityCommands.EmptyCaseEa, true, $"{e.cmd} distinct case addr");
                break;
            case GmActivityHandlerKind.DefaultNoOp:
                Equal(info.CaseAddress, NativeGmActivityCommands.DefaultCaseEa, $"{e.cmd} on def_622B15");
                break;
        }

        switch (e.kind)
        {
            case GmActivityHandlerKind.ImplementedCase: impl++; break;
            case GmActivityHandlerKind.StubbedFixedReply: stub++; break;
            case GmActivityHandlerKind.DefaultNoOp: def++; break;
        }
    }

    Equal(impl, 14, "implemented-case count");
    Equal(stub, 2, "fixed-reply-stub count");
    Equal(def, 23, "default no-op count (SetActScore modeled in item family)");
    Equal(impl + stub + def, 39, "total family-07 modeled here (40 total)");
    Equal(live, 10, "live command count");
    Equal(over, 4, "over-sends count");
    Equal(under, 3, "under-implements count");
}

static void VerifyHandledByteSemantics()
{
    // def_622B15 clears var_D to 0; every other shape leaves the optimistic 1
    Equal(NativeGmActivityCommands.HandledByteStaysSet(GmActivityHandlerKind.DefaultNoOp), false, "default clears handled byte");
    Equal(NativeGmActivityCommands.HandledByteStaysSet(GmActivityHandlerKind.ImplementedCase), true, "impl keeps handled byte");
    Equal(NativeGmActivityCommands.HandledByteStaysSet(GmActivityHandlerKind.StubbedFixedReply), true, "stub keeps handled byte");
}

static void VerifyPureCoreForwards()
{
    var give = NativeGmGiveTitle.Evaluate("Hero", "King");
    Equal(give.CallsCore, true, "givetitle calls core");
    Equal(give.CoreEa, 0x006C66C4u, "givetitle core ea");
    Equal(give.CoreBodyDeferred, true, "givetitle core deferred");
    Equal(give.SendsSysMsg, false, "givetitle silent");

    Equal(NativeGmQuxiaoTitle.Evaluate("Hero").CoreEa, 0x006C67D4u, "quxiaotitle core ea");
    Equal(NativeGmQuxiaoTitle.Evaluate("Hero").SendsSysMsg, false, "quxiaotitle silent");

    Equal(NativeGmUpdateOrder.Evaluate().CoreEa, 0x00713094u, "updateorder core ea");
    Equal(NativeGmUpdateOrder.Evaluate().SendsSysMsg, false, "updateorder silent");

    Equal(NativeGmGetS.Evaluate(1).CoreEa, 0x006CD6E8u, "gets core ea");
    Equal(NativeGmGetS.Evaluate(1).SendsSysMsg, false, "gets silent");

    Equal(NativeGmSetS.Evaluate(1, 2).CoreEa, 0x006CDA0Cu, "sets core ea");
    Equal(NativeGmSetS.Evaluate(1, 2).SendsSysMsg, false, "sets silent");
}

static void VerifySuccessMsgImpls()
{
    var push = NativeGmPushSingleTask.Evaluate(5);
    Equal(push.CoreEa, 0x00656924u, "pushsingletask core ea");
    Equal(push.CoreBodyDeferred, true, "pushsingletask core deferred");
    Equal(push.SendsSysMsgOnSuccess, true, "pushsingletask msg on success");
    Equal(push.MessageColor, 0x38FF, "pushsingletask colour green");

    var present = NativeGmAddNWPresent.Evaluate(3, "gift");
    Equal(present.CoreEa, 0x00603948u, "addnwpresent core ea");
    Equal(present.MessageColor, 0xFFDB, "addnwpresent colour");

    var accept = NativeGmAddNWAccept.Evaluate(3, "gift");
    Equal(accept.CoreEa, 0x006036B8u, "addnwaccept core ea");
    Equal(accept.MessageColor, 0xFFDB, "addnwaccept colour");
}

static void VerifyPushActIdent()
{
    var ok = NativeGmPushActIdent.Evaluate(ptidPresent: true, namePresent: true, actNum: 7);
    Equal(ok.Branch, PushActIdentBranch.Valid_CallCore, "pushactident valid branch");
    Equal(ok.CallsCore, true, "pushactident valid calls core");
    Equal(ok.CoreEa, 0x006E4030u, "pushactident core ea");
    Equal(ok.CoreBodyDeferred, true, "pushactident core deferred");
    Equal(ok.SendsSysMsg, false, "pushactident valid silent");

    foreach (var bad in new[]
    {
        NativeGmPushActIdent.Evaluate(false, true, 7),
        NativeGmPushActIdent.Evaluate(true, false, 7),
        NativeGmPushActIdent.Evaluate(true, true, -1),
    })
    {
        Equal(bad.Branch, PushActIdentBranch.Invalid_Usage, "pushactident invalid branch");
        Equal(bad.CallsCore, false, "pushactident invalid no core");
        Equal(bad.SendsSysMsg, true, "pushactident invalid sends usage");
        Equal(bad.MessageColor, 0xFFDB, "pushactident usage colour");
    }
}

static void VerifyAddVote()
{
    var notFound = NativeGmAddVote.Evaluate(charFound: false, votes: 5, voteType: 1);
    Equal(notFound.Branch, AddVoteBranch.CharNotFound, "addvote notfound branch");
    Equal(notFound.CallsCore, false, "addvote notfound no core");
    Equal(notFound.SendsSysMsg, true, "addvote notfound sends msg");
    Equal(notFound.MessageColor, 0xFFDB, "addvote notfound colour");
    Equal(notFound.FindCharEa, 0x00652784u, "addvote find-char ea");

    foreach (var bad in new[]
    {
        NativeGmAddVote.Evaluate(true, 0, 1),
        NativeGmAddVote.Evaluate(true, 5, 0),
    })
    {
        Equal(bad.Branch, AddVoteBranch.BadArgs, "addvote badargs branch");
        Equal(bad.CallsCore, false, "addvote badargs no core");
        Equal(bad.SendsSysMsg, true, "addvote badargs sends msg");
    }

    var applied = NativeGmAddVote.Evaluate(true, 5, 1);
    Equal(applied.Branch, AddVoteBranch.Applied, "addvote applied branch");
    Equal(applied.CallsCore, true, "addvote applied calls core");
    Equal(applied.CoreEa, 0x006EAE18u, "addvote core ea");
    Equal(applied.CoreBodyDeferred, true, "addvote core deferred");
    Equal(applied.SendsSysMsg, false, "addvote applied silent");
}

static void VerifySetGoldActLv()
{
    var ok = NativeGmSetGoldActLv.Evaluate(level: 3);
    Equal(ok.Applied, true, "setgoldactlv applied");
    Equal(ok.WritesCharField, true, "setgoldactlv writes field");
    Equal(ok.CharFieldOffset, 6173, "setgoldactlv field offset");
    Equal(ok.FindCharEa, 0x00652784u, "setgoldactlv find-char ea");
    Equal(ok.CoreBodyDeferred, false, "setgoldactlv inline (not deferred)");
    Equal(ok.SendsSysMsg, true, "setgoldactlv sends msg");
    Equal(ok.MessageColor, 0xFFDB, "setgoldactlv colour");

    foreach (var lv in new[] { 0, -1 })
    {
        var no = NativeGmSetGoldActLv.Evaluate(lv);
        Equal(no.Applied, false, "setgoldactlv non-positive no apply");
        Equal(no.WritesCharField, false, "setgoldactlv non-positive no write");
        Equal(no.SendsSysMsg, false, "setgoldactlv non-positive silent");
    }
}

static void VerifySignInAct()
{
    var open = NativeGmSignInAct.Evaluate("开启活动");
    Equal(open.Branch, SignInActBranch.Open, "signinact open branch");
    Equal(open.CallsCore, true, "signinact open calls core");
    Equal(open.CoreEa, 0x00616FFCu, "signinact open core ea");
    Equal(open.MessageColor, 0xFFDB, "signinact open colour");

    var close = NativeGmSignInAct.Evaluate("关闭活动");
    Equal(close.Branch, SignInActBranch.Close, "signinact close branch");
    Equal(close.CoreEa, 0x00616BA0u, "signinact close core ea");
    Equal(close.MessageColor, 0xFFDB, "signinact close colour");

    var usage = NativeGmSignInAct.Evaluate("blah");
    Equal(usage.Branch, SignInActBranch.Usage, "signinact usage branch");
    Equal(usage.CallsCore, false, "signinact usage no core");
    Equal(usage.MessageColor, 0x38FF, "signinact usage colour green");
}

static void VerifyMultiActionControllers()
{
    var simple = NativeGmSimpleActCtrl.Evaluate();
    Equal(simple.SendsSysMsg, true, "simpleactctrl sends msg");
    Equal(simple.MessageColor, 0xFFDB, "simpleactctrl colour");
    Equal(simple.CoreBodyDeferred, true, "simpleactctrl cores deferred");
    Equal(simple.SubCommandCoreEas.Length, 3, "simpleactctrl core count");
    Equal(simple.SubCommandCoreEas[0], 0x00723D78u, "simpleactctrl reload ea");
    Equal(simple.SubCommandCoreEas[1], 0x00724014u, "simpleactctrl status ea");
    Equal(simple.SubCommandCoreEas[2], 0x00723E64u, "simpleactctrl toggle ea");

    var gm = NativeGmGMActCtrl.Evaluate();
    Equal(gm.SendsSysMsg, true, "gmactctrl sends msg");
    Equal(gm.MessageColor, 0xFFDB, "gmactctrl colour");
    Equal(gm.SubCommandCoreEas.Length, 5, "gmactctrl core count");
    Equal(gm.SubCommandCoreEas[0], 0x00611948u, "gmactctrl reload ea");
    Equal(gm.SubCommandCoreEas[1], 0x006122B8u, "gmactctrl stop ea");
    Equal(gm.SubCommandCoreEas[2], 0x00612230u, "gmactctrl start ea");
    Equal(gm.SubCommandCoreEas[3], 0x00612260u, "gmactctrl status ea");
    Equal(gm.SubCommandCoreEas[4], 0x00612290u, "gmactctrl action ea");
}

static void VerifyStubReplies()
{
    var ggm = NativeGmActivityStubReply.Evaluate(GmActivityCommand.GetGuildMember);
    Equal(ggm.SendsSysMsg, true, "getguildmember sends msg");
    Equal(ggm.MessageText, "Invalid", "getguildmember text");
    Equal(ggm.MessageColor, 0x38FF, "getguildmember colour");
    Equal(ggm.MutatesState, false, "getguildmember no effect");

    var agm = NativeGmActivityStubReply.Evaluate(GmActivityCommand.AddGuildMem);
    Equal(agm.SendsSysMsg, true, "addguildmem sends msg");
    Equal(agm.MessageText, "Can not insert directly", "addguildmem text");
    Equal(agm.MessageColor, 0x38FF, "addguildmem colour");
    Equal(agm.MutatesState, false, "addguildmem no effect");

    // a real-impl command must not be routed through the stub-reply path
    var threw = false;
    try { NativeGmActivityStubReply.Evaluate(GmActivityCommand.AddVote); }
    catch (InvalidOperationException) { threw = true; }
    Equal(threw, true, "non-stub command rejected by stub-reply path");
}

static void VerifyNoOps()
{
    // all 24 no-ops are the default sink: handled byte cleared to 0, no effect, no message
    foreach (var info in NativeGmActivityCommands.All)
    {
        if (info.HandlerKind != GmActivityHandlerKind.DefaultNoOp)
            continue;
        var o = NativeGmActivityCommands.EvaluateNoOp(info.Command);
        Equal(o.Recognized, true, $"{info.Command} recognized");
        Equal(o.DispatchesToDefaultCase, true, $"{info.Command} to def_622B15");
        Equal(o.MutatesState, false, $"{info.Command} no effect");
        Equal(o.SendsResponse, false, $"{info.Command} no response");
    }

    // implemented / stub commands must NOT be routed through the no-op path
    var threw = false;
    try { NativeGmActivityCommands.EvaluateNoOp(GmActivityCommand.SignInAct); }
    catch (InvalidOperationException) { threw = true; }
    Equal(threw, true, "implemented command rejected by no-op path");
}
