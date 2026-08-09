using GameSvr;

// Contract check for the dormant CURRENCY / LINGFU (灵符) / C2C GM command family model
// (GameSvr/Services/NativeGmCurrencyCommands.cs), locked against the Hex-Rays-verified original
// dispatcher sub_622820 (single switch, table jpt_622B15 @0x00622B1C) in the unpacked M2Server image.
// Family 06: 26 commands, 23 implemented / 3 registered no-op.

try
{
    VerifyDispatcherConstants();
    VerifyRegistry();
    VerifyForwarders();
    VerifyAddLinFu();
    VerifyLingfuChange();
    VerifyGuardedForwards();
    VerifyTargetResolve();
    VerifyReloadConfirm();
    VerifyServerSwitch();
    VerifyCreditCard();
    VerifyNoOps();

    Console.WriteLine(
        "PASS NativeGmCurrencyCommandsCheck dispatcher=sub_622820 table=0x622B1C max=750 " +
        "sinks=def_622B15@0x62B648+emptyExit@0x62B64C family06=26 impl=23 noop=3 " +
        "impl=YbBuyLf/CancelYBDeal/AddLinFu/ServerSwitch/ClearNickLinfu/CreditCard/LesCoin/AddCoin/" +
        "ChgUserLinFu/ChgUserLinFu2/chguserGlory/GiveSdNickLinfu/TransferCredit/ReloadC2CItems/SetLingfu3/" +
        "SetNickLF/SetGloryPoint/reshuaGP/SendYuanBaoText/c2ctest/c2cQuery/c2cOperate/loadEquipRecycle " +
        "noop=AddCardValue/SellC2CGoods/SetZillionCount " +
        "coreDeferred=ALL-23-impl (元宝 settlement YbBuyLf/TransferCredit external=YBDB-6108)");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"NativeGmCurrencyCommandsCheck FAIL: {ex.Message}");
    return 1;
}

static void Assert(bool cond, string msg)
{
    if (!cond) throw new Exception(msg);
}

static void Equal<T>(T actual, T expected, string label)
{
    if (!Equals(actual, expected))
        throw new Exception($"{label}: expected {expected}, got {actual}");
}

static void VerifyDispatcherConstants()
{
    Equal(NativeGmCurrencyCommands.DispatcherEa, 0x00622820u, "dispatcher ea");
    Equal(NativeGmCurrencyCommands.IndexLookupEa, 0x00621F28u, "index lookup ea");
    Equal(NativeGmCurrencyCommands.JumpTableEa, 0x00622B1Cu, "jump table ea");
    Equal(NativeGmCurrencyCommands.SwitchMaxIndex, 750, "switch max index");

    // TWO distinct no-op sinks
    Equal(NativeGmCurrencyCommands.DefaultCaseEa, 0x0062B648u, "default case sink");
    Equal(NativeGmCurrencyCommands.EmptyExitCaseEa, 0x0062B64Cu, "empty-exit sink");
    Assert(NativeGmCurrencyCommands.DefaultCaseEa != NativeGmCurrencyCommands.EmptyExitCaseEa,
        "sinks are distinct");

    // SysMsg vtable slot + the three observed colours
    Equal(NativeGmCurrencyCommands.SysMsgVtableOffset, 0xD4, "sysmsg vtable offset");
    Equal(NativeGmCurrencyCommands.ColorInfo, 0xFFDB, "info colour (-37)");
    Equal(NativeGmCurrencyCommands.ColorError, 0x38FF, "error colour (14591)");
    Equal(NativeGmCurrencyCommands.ColorNotice, 0xFCFF, "notice colour (-769)");

    // shim-proven constants
    Equal(NativeGmCurrencyCommands.LingfuSelfFieldOffset, 0xBD8, "AddLinFu self field offset");
    Equal(NativeGmCurrencyCommands.AddLinFuDefaultCount, 1, "AddLinFu default count");
    Equal(NativeGmCurrencyCommands.LingfuKindNormal, 0, "lingfu kind normal");
    Equal(NativeGmCurrencyCommands.LingfuKindExtended, 1, "lingfu kind extended");
    Equal(NativeGmCurrencyCommands.ServerSwitchCount, 37, "server switch count");
    Equal(NativeGmCurrencyCommands.ServerSwitchNickLingfuIndex, 9, "server switch nick-lingfu index");
    Equal(NativeGmCurrencyCommands.CreditCardSubcommandCount, 4, "creditcard subcommand count");
    Equal(NativeGmCurrencyCommands.ResolveByNameEa, 0x00652784u, "resolve-by-name helper ea");
}

static void VerifyRegistry()
{
    // (command, name, index, perm, implemented, caseAddr, coreEa, coreDeferred)
    (GmCurrencyCommand cmd, string name, int idx, int perm, bool impl, uint caseAddr, uint coreEa, bool deferred)[] expected =
    {
        (GmCurrencyCommand.YbBuyLf,          "YbBuyLf",          32,  0, true,  0x00623B7Au, 0x0062E4A8u, true),
        (GmCurrencyCommand.CancelYBDeal,     "CancelYBDeal",     96,  4, true,  0x00624FF8u, 0x006D731Cu, true),
        (GmCurrencyCommand.AddLinFu,         "AddLinFu",         112, 4, true,  0x006251A8u, 0x004C7004u, true),
        (GmCurrencyCommand.ServerSwitch,     "ServerSwitch",     127, 4, true,  0x00625324u, 0x007D61FCu, true),
        (GmCurrencyCommand.AddCardValue,     "AddCardValue",     148, 4, false, NativeGmCurrencyCommands.DefaultCaseEa, 0u, false),
        (GmCurrencyCommand.ClearNickLinfu,   "ClearNickLinfu",   164, 4, true,  0x00625CABu, 0x006D3694u, true),
        (GmCurrencyCommand.CreditCard,       "CreditCard",       192, 4, true,  0x00625FFDu, 0x00724208u, true),
        (GmCurrencyCommand.LesCoin,          "LesCoin",          203, 5, true,  0x00625D63u, 0x006C69ECu, true),
        (GmCurrencyCommand.AddCoin,          "AddCoin",          204, 5, true,  0x00625D76u, 0x006C6B40u, true),
        (GmCurrencyCommand.ChgUserLinFu,     "ChgUserLinFu",     220, 5, true,  0x00625FADu, 0x006C78A8u, true),
        (GmCurrencyCommand.ChgUserLinFu2,    "ChgUserLinFu2",    221, 5, true,  0x00625FCDu, 0x006C78A8u, true),
        (GmCurrencyCommand.ChguserGlory,     "chguserGlory",     226, 5, true,  0x006261B2u, 0x006D2AD8u, true),
        (GmCurrencyCommand.GiveSdNickLinfu,  "GiveSdNickLinfu",  230, 5, true,  0x00626202u, 0x006D7050u, true),
        (GmCurrencyCommand.TransferCredit,   "TransferCredit",   249, 4, true,  0x006264A8u, 0x006E286Cu, true),
        (GmCurrencyCommand.ReloadC2CItems,   "ReloadC2CItems",   255, 5, true,  0x006264E3u, 0x0075516Cu, true),
        (GmCurrencyCommand.SetLingfu3,       "SetLingfu3",       260, 5, true,  0x006265FCu, 0x00714B48u, true),
        (GmCurrencyCommand.SellC2CGoods,     "SellC2CGoods",     262, 5, false, NativeGmCurrencyCommands.DefaultCaseEa, 0u, false),
        (GmCurrencyCommand.SetNickLF,        "SetNickLF",        267, 4, true,  0x0062678Bu, 0x0062EAE4u, true),
        (GmCurrencyCommand.SetGloryPoint,    "SetGloryPoint",    274, 5, true,  0x006269B8u, 0x006E2134u, true),
        (GmCurrencyCommand.ReshuaGP,         "reshuaGP",         277, 4, true,  0x00626B71u, 0x0063C1D4u, true),
        (GmCurrencyCommand.SendYuanBaoText,  "SendYuanBaoText",  334, 4, true,  0x00627D29u, 0x006EA1A4u, true),
        (GmCurrencyCommand.C2cTest,          "c2ctest",          372, 5, true,  0x00628242u, 0x006F228Cu, true),
        (GmCurrencyCommand.C2cQuery,         "c2cQuery",         376, 4, true,  0x006282B0u, 0x006F1A50u, true),
        (GmCurrencyCommand.C2cOperate,       "c2cOperate",       377, 5, true,  0x006282CFu, 0x006F1844u, true),
        (GmCurrencyCommand.LoadEquipRecycle, "loadEquipRecycle", 440, 4, true,  0x0062765Bu, 0x00752648u, true),
        (GmCurrencyCommand.SetZillionCount,  "SetZillionCount",  478, 4, false, NativeGmCurrencyCommands.DefaultCaseEa, 0u, false),
    };

    Equal(NativeGmCurrencyCommands.All.Count, expected.Length, "registry count");
    Equal(expected.Length, 26, "family-06 command count");

    int impl = 0, noop = 0;
    foreach (var e in expected)
    {
        var info = NativeGmCurrencyCommands.Info(e.cmd);
        Equal(info.Name, e.name, $"{e.cmd} name");
        Equal(info.DispatchIndex, e.idx, $"{e.cmd} index");
        Equal(info.RequiredPermission, e.perm, $"{e.cmd} perm");
        Equal(info.Implemented, e.impl, $"{e.cmd} implemented");
        Equal(info.CaseAddress, e.caseAddr, $"{e.cmd} case address");
        Equal(info.CoreEa, e.coreEa, $"{e.cmd} core ea");
        Equal(info.CoreBodyDeferred, e.deferred, $"{e.cmd} core deferred");
        Assert(info.DispatchIndex >= 0 && info.DispatchIndex <= NativeGmCurrencyCommands.SwitchMaxIndex,
            $"{e.cmd} index in switch range");
        if (e.impl)
        {
            Assert(info.CaseAddress != NativeGmCurrencyCommands.DefaultCaseEa, $"{e.cmd} has distinct case");
            Assert(info.CaseAddress != NativeGmCurrencyCommands.EmptyExitCaseEa, $"{e.cmd} not empty-exit");
            Assert(info.CoreBodyDeferred, $"{e.cmd} impl core deferred");
            impl++;
        }
        else
        {
            Equal(info.CaseAddress, NativeGmCurrencyCommands.DefaultCaseEa, $"{e.cmd} on default case");
            Equal(info.CoreEa, 0u, $"{e.cmd} no core");
            noop++;
        }
    }
    Equal(impl, 23, "implemented count");
    Equal(noop, 3, "noop count");
}

static void VerifyForwarders()
{
    // (outcome, expected core, forwardsSelf, parsesNumericArgs, mutates)
    (CurrencyForwardOutcome o, uint core, bool self, int parses, bool mutates)[] fwd =
    {
        (NativeGmCurrencyForwarders.YbBuyLf(),         0x0062E4A8u, true,  1, true),
        (NativeGmCurrencyForwarders.CancelYBDeal(),    0x006D731Cu, false, 0, true),
        (NativeGmCurrencyForwarders.ClearNickLinfu(),  0x006D3694u, false, 0, true),
        (NativeGmCurrencyForwarders.LesCoin(),         0x006C69ECu, false, 0, true),
        (NativeGmCurrencyForwarders.AddCoin(),         0x006C6B40u, false, 0, true),
        (NativeGmCurrencyForwarders.ChguserGlory(),    0x006D2AD8u, true,  0, true),
        (NativeGmCurrencyForwarders.GiveSdNickLinfu(), 0x006D7050u, false, 0, true),
        (NativeGmCurrencyForwarders.ReloadC2CItems(),  0x0075516Cu, false, 0, true),
        (NativeGmCurrencyForwarders.C2cTest(),         0x006F228Cu, false, 2, true),
        (NativeGmCurrencyForwarders.C2cQuery(),        0x006F1A50u, false, 1, false),
        (NativeGmCurrencyForwarders.C2cOperate(),      0x006F1844u, false, 2, true),
    };

    Equal(fwd.Length, 11, "forwarder count");
    foreach (var f in fwd)
    {
        Equal(f.o.CoreEa, f.core, "forwarder core ea");
        Equal(f.o.ForwardsSelf, f.self, "forwarder forwards-self");
        Equal(f.o.ParsesNumericArgs, f.parses, "forwarder parses-numeric-args");
        Equal(f.o.MutatesState, f.mutates, "forwarder mutates");
        Assert(f.o.CoreBodyDeferred, "forwarder core deferred");
        Assert(!f.o.ShimValidates, "forwarder shim does not validate");
        Assert(!f.o.ShimSendsSysMsg, "forwarder shim silent");
    }
}

static void VerifyAddLinFu()
{
    var o = NativeGmAddLinFu.Evaluate();
    Assert(o.WritesSelfField, "addlinfu writes self field");
    Equal(o.SelfFieldOffset, 0xBD8, "addlinfu self field offset");
    Assert(o.IsAdditive, "addlinfu additive");
    Assert(o.TargetsSelf, "addlinfu targets self");
    Equal(o.DefaultCount, 1, "addlinfu default count");
    Equal(o.NormalizeCoreEa, 0x004C7004u, "addlinfu normalize core");
    Equal(o.RefreshCoreEa, 0x006B99E4u, "addlinfu refresh core");
    Assert(o.CoreBodyDeferred, "addlinfu core deferred");
    Assert(o.MutatesState, "addlinfu mutates");
    Assert(!o.SendsSysMsg, "addlinfu silent");
}

static void VerifyLingfuChange()
{
    var normal = NativeGmChgUserLinFu.Normal();
    Equal(normal.SharedCoreEa, 0x006C78A8u, "chguserlinfu shared core");
    Equal(normal.Discriminator, 0, "chguserlinfu normal discriminator");
    Assert(!normal.IsExtended, "chguserlinfu normal not extended");
    Assert(normal.ParsesCount, "chguserlinfu parses count");
    Assert(normal.CoreBodyDeferred, "chguserlinfu core deferred");
    Assert(!normal.ShimSendsSysMsg, "chguserlinfu shim silent");

    var extended = NativeGmChgUserLinFu.Extended();
    Equal(extended.SharedCoreEa, 0x006C78A8u, "chguserlinfu2 shared core (same)");
    Equal(extended.Discriminator, 1, "chguserlinfu2 extended discriminator");
    Assert(extended.IsExtended, "chguserlinfu2 is extended");

    // the ONLY difference between 220 and 221 is the discriminator; the core ea is identical
    Equal(normal.SharedCoreEa, extended.SharedCoreEa, "220 and 221 share the core");
    Assert(normal.Discriminator != extended.Discriminator, "220 vs 221 discriminator differs");
}

static void VerifyGuardedForwards()
{
    // TransferCredit: amount != 0 -> forward sub_6E286C; amount == 0 -> notice SysMsg, no forward
    var tcOk = NativeGmTransferCredit.Evaluate(amountNonZero: true);
    Assert(tcOk.CallsCore && !tcOk.SendsErrorSysMsg, "transfercredit nonzero forwards");
    Equal(tcOk.CoreEa, 0x006E286Cu, "transfercredit core");
    Assert(tcOk.MutatesStateWhenSatisfied, "transfercredit mutates when satisfied");
    var tcZero = NativeGmTransferCredit.Evaluate(amountNonZero: false);
    Assert(!tcZero.CallsCore && tcZero.SendsErrorSysMsg, "transfercredit zero refuses");
    Equal(tcZero.ErrorColor, 0xFCFF, "transfercredit refusal colour (notice)");

    // SetNickLF: ratio present -> forward sub_62EAE4; absent -> error SysMsg
    var lfOk = NativeGmSetNickLF.Evaluate(ratioPresent: true);
    Assert(lfOk.CallsCore && !lfOk.SendsErrorSysMsg, "setnicklf present forwards");
    Equal(lfOk.CoreEa, 0x0062EAE4u, "setnicklf core");
    var lfNo = NativeGmSetNickLF.Evaluate(ratioPresent: false);
    Assert(!lfNo.CallsCore && lfNo.SendsErrorSysMsg, "setnicklf absent refuses");
    Equal(lfNo.ErrorColor, 0x38FF, "setnicklf refusal colour (error)");

    // SendYuanBaoText: content present -> broadcast sub_6EA1A4; absent -> error SysMsg; never mutates state
    var ybOk = NativeGmSendYuanBaoText.Evaluate(contentPresent: true);
    Assert(ybOk.CallsCore && !ybOk.SendsErrorSysMsg, "sendyuanbaotext present broadcasts");
    Equal(ybOk.CoreEa, 0x006EA1A4u, "sendyuanbaotext core");
    Assert(!ybOk.MutatesStateWhenSatisfied, "sendyuanbaotext broadcast-only (no mutation)");
    var ybNo = NativeGmSendYuanBaoText.Evaluate(contentPresent: false);
    Assert(!ybNo.CallsCore && ybNo.SendsErrorSysMsg, "sendyuanbaotext absent refuses");
    Equal(ybNo.ErrorColor, 0x38FF, "sendyuanbaotext refusal colour (error)");
}

static void VerifyTargetResolve()
{
    // SetLingfu3: not found -> error; found+query(count==-1) -> no write; found+set -> write
    var s3NotFound = NativeGmSetLingfu3.Evaluate(targetFound: false, countIsQuery: false);
    Equal(s3NotFound.Branch, TargetResolveBranch.TargetNotFound, "setlingfu3 not-found branch");
    Assert(s3NotFound.SendsErrorSysMsg && !s3NotFound.CallsSetCore && !s3NotFound.MutatesState,
        "setlingfu3 not-found: error, no core, no mutate");

    var s3Query = NativeGmSetLingfu3.Evaluate(targetFound: true, countIsQuery: true);
    Equal(s3Query.Branch, TargetResolveBranch.Query, "setlingfu3 query branch");
    Assert(!s3Query.CallsSetCore && !s3Query.MutatesState && s3Query.SendsConfirmSysMsg,
        "setlingfu3 query: no write, confirm");

    var s3Set = NativeGmSetLingfu3.Evaluate(targetFound: true, countIsQuery: false);
    Equal(s3Set.Branch, TargetResolveBranch.Set, "setlingfu3 set branch");
    Assert(s3Set.CallsSetCore && s3Set.MutatesState, "setlingfu3 set: write");
    Equal(s3Set.SetCoreEa, 0x00714B48u, "setlingfu3 set core");
    Equal(s3Set.ResolveHelperEa, 0x00652784u, "setlingfu3 resolve helper");

    // SetGloryPoint: no query mode; found -> set, absent -> error
    var gpSet = NativeGmSetGloryPoint.Evaluate(targetFound: true);
    Equal(gpSet.Branch, TargetResolveBranch.Set, "setglorypoint set branch");
    Assert(gpSet.CallsSetCore && gpSet.MutatesState, "setglorypoint set: write");
    Equal(gpSet.SetCoreEa, 0x006E2134u, "setglorypoint set core");
    var gpNo = NativeGmSetGloryPoint.Evaluate(targetFound: false);
    Equal(gpNo.Branch, TargetResolveBranch.TargetNotFound, "setglorypoint not-found branch");
    Assert(gpNo.SendsErrorSysMsg && !gpNo.CallsSetCore, "setglorypoint not-found: error, no core");
}

static void VerifyReloadConfirm()
{
    var gp = NativeGmReloadConfirm.ReshuaGP();
    Equal(gp.ReloadCoreEa, 0x0063C1D4u, "reshuagp reload core");
    Assert(gp.MutatesState && gp.SendsSysMsg, "reshuagp reloads + confirms");
    Assert(gp.ConfirmsOnlyOnSuccess, "reshuagp confirms only on success");
    Equal(gp.MessageColor, 0xFFDB, "reshuagp confirm colour");

    var er = NativeGmReloadConfirm.LoadEquipRecycle();
    Equal(er.ReloadCoreEa, 0x00752648u, "loadequiprecycle reload core");
    Assert(er.MutatesState && er.SendsSysMsg, "loadequiprecycle reloads + confirms");
    Assert(!er.ConfirmsOnlyOnSuccess, "loadequiprecycle always confirms");
}

static void VerifyServerSwitch()
{
    var o = NativeGmServerSwitch.Evaluate();
    Equal(o.SwitchTableEa, 0x007D61FCu, "serverswitch table ea");
    Equal(o.SwitchCount, 37, "serverswitch count");
    Equal(o.ExcludedSwitchIndex, 9, "serverswitch excluded index (nick-lingfu)");
    Assert(o.TogglesGlobalSwitch && o.MutatesState, "serverswitch toggles global");
    Equal(o.ExcludedErrorColor, 0x38FF, "serverswitch blocked-path colour");
    Assert(o.CoreBodyDeferred, "serverswitch apply deferred");
}

static void VerifyCreditCard()
{
    var o = NativeGmCreditCard.Evaluate();
    Equal(o.SubcommandCount, 4, "creditcard subcommand count");
    Equal(o.FlagStateEa, 0x007D7038u, "creditcard flag state ea");
    Equal(o.PrimaryCoreEa, 0x00724208u, "creditcard primary core");
    Equal(o.SecondaryCoreEa, 0x00724490u, "creditcard secondary core");
    Assert(o.MutatesState && o.SendsStateSysMsg, "creditcard mutates + messages");
    Assert(o.CoreBodyDeferred, "creditcard core deferred");
}

static void VerifyNoOps()
{
    GmCurrencyCommand[] noop =
    {
        GmCurrencyCommand.AddCardValue,
        GmCurrencyCommand.SellC2CGoods,
        GmCurrencyCommand.SetZillionCount,
    };
    foreach (var c in noop)
    {
        var o = NativeGmCurrencyCommands.EvaluateUnimplemented(c);
        Assert(o.Recognized && o.DispatchesToDefaultCase, $"{c}: recognized + default");
        Assert(!o.MutatesState && !o.SendsResponse, $"{c}: no effect / no response");
    }

    // implemented commands must NOT be routed through the unimplemented path
    var threw = false;
    try { NativeGmCurrencyCommands.EvaluateUnimplemented(GmCurrencyCommand.AddCoin); }
    catch (InvalidOperationException) { threw = true; }
    Assert(threw, "implemented command rejected by unimplemented path");
}
