using GameSvr;

// Contract check for the dormant ITEM/EQUIP/MAKE GM command family — SECOND SUPPLEMENT model
// (GameSvr/Services/NativeGmItemExtraCommands.cs), the 17 family-01 commands that slipped
// NativeGmItemCommands.cs + NativeGmItemCommandsSupplement.cs. Locked against the Hex-Rays-verified
// original dispatcher sub_622820 (single switch, table jpt_622B15 @0x00622B1C) in the unpacked M2Server.
// Evidence: D:/loym2/staging/update_clothes_4637_ida_work/{disp_decomp.txt,big622820.txt}.
// 17 commands = 9 implemented (thin shim -> deferred core) + 8 registered no-ops (def_622B15).

try
{
    VerifyDispatcherConstants();
    VerifyRegistry();
    VerifyForwarders();
    VerifyReloads();
    VerifySuperMerchant();
    VerifyCmdBindItem();
    VerifySetMaxButchCount();
    VerifyNoOps();

    Console.WriteLine(
        "PASS NativeGmItemExtraCommandsCheck dispatcher=sub_622820 table=0x622B1C max=750 count=17 " +
        "impl=9(ReloadunBindItem/make/SuperMerchant/reloadRndItem/reloadStditem/SetMaxButchCount/" +
        "cmdbinditem/ReloadQKbag/chgItemBindDay) noop=8(loadSuperSmelt/ClearAntiInfo/CreateAntiItem/" +
        "AddAntiSuper/ChgAntiNormal/reloadEquipSplit/EquipDrop/ReloadComposeConfig) " +
        "correction=ReloadunBindItem-is-impl-not-noop caseBranch!=core allMsg=0xFFDB");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"NativeGmItemExtraCommandsCheck FAIL: {ex.Message}");
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
    Equal(NativeGmItemExtraCommands.DispatcherEa, 0x00622820u, "dispatcher ea");
    Equal(NativeGmItemExtraCommands.IndexLookupEa, 0x00621F28u, "index lookup ea");
    Equal(NativeGmItemExtraCommands.JumpTableEa, 0x00622B1Cu, "jump table ea");
    Equal(NativeGmItemExtraCommands.SwitchMaxIndex, 750, "switch max index");
    Equal(NativeGmItemExtraCommands.DefaultCaseEa, 0x0062B648u, "default case ea");
    Equal(NativeGmItemExtraCommands.EpilogueEa, 0x0062B64Cu, "epilogue ea");
    Equal(NativeGmItemExtraCommands.SysMsgVtableOffset, 0xD4, "sysmsg vtable offset");
    Equal(NativeGmItemExtraCommands.ColorInfo, 0xFFDB, "colour info");
    // CasePtr math: slot 201 lives at 0x00622B1C + 201*4 = 0x00622E40
    Equal(NativeGmItemExtraCommands.CasePtr(201), 0x00622B1Cu + 201u * 4u, "CasePtr(201)");
    // reloadStditem globals / selector
    Equal(NativeGmItemExtraCommands.ReloadStdItemGlobalEa, 0x007D62DCu, "reloadStditem global");
    Equal(NativeGmItemExtraCommands.ReloadStdItemSelector, 0x180, "reloadStditem selector");
    // SetMaxButchCount config keys
    Equal(NativeGmItemExtraCommands.SetMaxButchGlobalEa, 0x007D6888u, "SetMaxButchCount global");
    Equal(NativeGmItemExtraCommands.SetMaxButchConfigSection, "MaxBeButchedCount", "SetMaxButchCount config section");
    Equal(NativeGmItemExtraCommands.SetMaxButchConfigKey, "setup", "SetMaxButchCount config key");
    Equal(NativeGmItemExtraCommands.SuperMerchantMgrGlobalEa, 0x007D6D10u, "SuperMerchant mgr global");
}

static void VerifyRegistry()
{
    // (command, name, index, perm, implemented, caseAddr, coreEa, coreDeferred)
    (GmItemExtraCommand cmd, string name, int idx, int perm, bool impl, uint addr, uint core, bool deferred)[] expected =
    {
        // ---- 9 implemented ----
        (GmItemExtraCommand.ReloadUnBindItem, "ReloadunBindItem", 166, 4, true, 0x00625CE2u, 0x0062E630u, true),
        (GmItemExtraCommand.Make,             "make",             201, 5, true, 0x00625D32u, 0x006BDA34u, true),
        (GmItemExtraCommand.SuperMerchant,    "SuperMerchant",    297, 5, true, 0x00626F32u, 0x0061668Cu, true),
        (GmItemExtraCommand.ReloadRndItem,    "reloadRndItem",    299, 4, true, 0x00626FD9u, 0x007524A8u, true),
        (GmItemExtraCommand.ReloadStdItem,    "reloadStditem",    443, 4, true, 0x00628AC6u, 0x00713094u, true),
        (GmItemExtraCommand.SetMaxButchCount, "SetMaxButchCount", 515, 4, true, 0x0062954Fu, 0x00790210u, true),
        (GmItemExtraCommand.CmdBindItem,      "cmdbinditem",      544, 4, true, 0x00625475u, 0x006C64C0u, true),
        (GmItemExtraCommand.ReloadQKbag,      "ReloadQKbag",      545, 4, true, 0x006254D8u, 0x007536B4u, true),
        (GmItemExtraCommand.ChgItemBindDay,   "chgItemBindDay",   562, 4, true, 0x00623BF7u, 0x006F33E4u, true),

        // ---- 8 registered no-ops ----
        (GmItemExtraCommand.LoadSuperSmelt,      "loadSuperSmelt",      311, 4, false, 0x0062B648u, 0u, false),
        (GmItemExtraCommand.ClearAntiInfo,       "ClearAntiInfo",       322, 5, false, 0x0062B648u, 0u, false),
        (GmItemExtraCommand.CreateAntiItem,      "CreateAntiItem",      324, 5, false, 0x0062B648u, 0u, false),
        (GmItemExtraCommand.AddAntiSuper,        "AddAntiSuper",        327, 5, false, 0x0062B648u, 0u, false),
        (GmItemExtraCommand.ChgAntiNormal,       "ChgAntiNormal",       421, 5, false, 0x0062B648u, 0u, false),
        (GmItemExtraCommand.ReloadEquipSplit,    "reloadEquipSplit",    447, 4, false, 0x0062B648u, 0u, false),
        (GmItemExtraCommand.EquipDrop,           "EquipDrop",           457, 4, false, 0x0062B648u, 0u, false),
        (GmItemExtraCommand.ReloadComposeConfig, "ReloadComposeConfig", 542, 4, false, 0x0062B648u, 0u, false),
    };

    Equal(NativeGmItemExtraCommands.All.Count, expected.Length, "registry count");
    Equal(expected.Length, 17, "family-01 remainder count");

    int impl = 0, noop = 0;
    var seenIdx = new System.Collections.Generic.HashSet<int>();
    foreach (var e in expected)
    {
        var info = NativeGmItemExtraCommands.Info(e.cmd);
        Equal(info.Name, e.name, $"{e.cmd} name");
        Equal(info.DispatchIndex, e.idx, $"{e.cmd} index");
        Equal(info.RequiredPermission, e.perm, $"{e.cmd} perm");
        Equal(info.Implemented, e.impl, $"{e.cmd} implemented");
        Equal(info.CaseAddress, e.addr, $"{e.cmd} case addr");
        Equal(info.CoreEa, e.core, $"{e.cmd} core ea");
        Equal(info.CoreBodyDeferred, e.deferred, $"{e.cmd} core deferred");
        Equal(info.DispatchIndex >= 0 && info.DispatchIndex <= NativeGmItemExtraCommands.SwitchMaxIndex, true,
            $"{e.cmd} index in switch range");
        Equal(seenIdx.Add(info.DispatchIndex), true, $"{e.cmd} index unique");

        if (e.impl)
        {
            // case-branch address must be distinct from the default sink, the epilogue, AND the core body
            Equal(info.CaseAddress != NativeGmItemExtraCommands.DefaultCaseEa, true, $"{e.cmd} case != default");
            Equal(info.CaseAddress != NativeGmItemExtraCommands.EpilogueEa, true, $"{e.cmd} case != epilogue");
            Equal(info.CaseAddress != info.CoreEa, true, $"{e.cmd} case-branch != core");
            Equal(info.CoreBodyDeferred, true, $"{e.cmd} core deferred");
            impl++;
        }
        else
        {
            Equal(info.CaseAddress, NativeGmItemExtraCommands.DefaultCaseEa, $"{e.cmd} on def_622B15");
            Equal(info.CoreEa, 0u, $"{e.cmd} no core");
            noop++;
        }
    }

    Equal(impl, 9, "implemented count");
    Equal(noop, 8, "registered no-op count");
    Equal(impl + noop, 17, "total remainder count");
}

static void VerifyForwarders()
{
    var rub = NativeGmItemExtraForwarders.ReloadUnBindItem();
    Equal(rub.CoreEa, 0x0062E630u, "ReloadunBindItem core ea");
    Equal(rub.ForwardedArgCount, 0, "ReloadunBindItem arg count");
    Equal(rub.ShimSendsSysMsg, false, "ReloadunBindItem silent");
    Equal(rub.CoreBodyDeferred, true, "ReloadunBindItem core deferred");

    var mk = NativeGmItemExtraForwarders.Make();
    Equal(mk.CoreEa, 0x006BDA34u, "make core ea");
    Equal(mk.ForwardedArgCount, 2, "make arg count");
    Equal(mk.ShimSendsSysMsg, false, "make silent");

    var rs = NativeGmItemExtraForwarders.ReloadStdItem();
    Equal(rs.CoreEa, 0x00713094u, "reloadStditem core ea");
    Equal(rs.ForwardedArgCount, 0, "reloadStditem arg count");
    Equal(rs.ShimSendsSysMsg, false, "reloadStditem silent");

    var ci = NativeGmItemExtraForwarders.ChgItemBindDay();
    Equal(ci.CoreEa, 0x006F33E4u, "chgItemBindDay core ea");
    Equal(ci.ForwardedArgCount, 1, "chgItemBindDay arg count");
    Equal(ci.ShimSendsSysMsg, false, "chgItemBindDay silent");
}

static void VerifyReloads()
{
    var qk = NativeGmItemExtraReloads.ReloadQKbag();
    Equal(qk.CoreEa, 0x007536B4u, "ReloadQKbag core ea");
    Equal(qk.CoreBodyDeferred, true, "ReloadQKbag core deferred");
    Equal(qk.MutatesState, true, "ReloadQKbag reloads config");
    Equal(qk.SendsSysMsg, true, "ReloadQKbag always sends msg");
    Equal(qk.MessageColor, 0xFFDB, "ReloadQKbag colour");
    Equal(qk.MessageVariesByResult, false, "ReloadQKbag fixed message");

    var rr = NativeGmItemExtraReloads.ReloadRndItem();
    Equal(rr.CoreEa, 0x007524A8u, "reloadRndItem core ea");
    Equal(rr.SendsSysMsg, true, "reloadRndItem always sends msg");
    Equal(rr.MessageColor, 0xFFDB, "reloadRndItem colour");
    Equal(rr.MessageVariesByResult, true, "reloadRndItem message varies by result");
}

static void VerifySuperMerchant()
{
    foreach (var bad in new[]
    {
        NativeGmSuperMerchant.Evaluate(0, 1, 1, true),
        NativeGmSuperMerchant.Evaluate(1, 0, 1, true),
        NativeGmSuperMerchant.Evaluate(1, 1, 0, true),
    })
    {
        Equal(bad.Branch, SuperMerchantBranch.BadArgs, "supermerchant badargs branch");
        Equal(bad.CallsCore, false, "supermerchant badargs no core");
        Equal(bad.SendsSysMsg, true, "supermerchant badargs usage msg");
        Equal(bad.MessageColor, 0xFFDB, "supermerchant badargs colour");
    }

    var absent = NativeGmSuperMerchant.Evaluate(1, 1, 5, managerPresent: false);
    Equal(absent.Branch, SuperMerchantBranch.MgrAbsent, "supermerchant mgr-absent branch");
    Equal(absent.CallsCore, false, "supermerchant mgr-absent no core");
    Equal(absent.SendsSysMsg, false, "supermerchant mgr-absent silent");

    var applied = NativeGmSuperMerchant.Evaluate(1, 1, 5, managerPresent: true);
    Equal(applied.Branch, SuperMerchantBranch.Applied, "supermerchant applied branch");
    Equal(applied.CallsCore, true, "supermerchant applied calls core");
    Equal(applied.CoreEa, 0x0061668Cu, "supermerchant core ea");
    Equal(applied.CoreBodyDeferred, true, "supermerchant core deferred");
    Equal(applied.MgrGlobalEa, 0x007D6D10u, "supermerchant mgr global");
    Equal(applied.SendsSysMsg, true, "supermerchant applied sends msg");
    Equal(applied.MessageColor, 0xFFDB, "supermerchant applied colour");
}

static void VerifyCmdBindItem()
{
    var applied = NativeGmCmdBindItem.Evaluate(charNamePresent: true, itemIdPresent: true);
    Equal(applied.Branch, CmdBindItemBranch.Applied, "cmdbinditem applied branch");
    Equal(applied.CallsCore, true, "cmdbinditem applied calls core");
    Equal(applied.CoreEa, 0x006C64C0u, "cmdbinditem core ea");
    Equal(applied.CoreBodyDeferred, true, "cmdbinditem core deferred");
    Equal(applied.SendsSysMsg, false, "cmdbinditem applied silent");

    foreach (var usage in new[]
    {
        NativeGmCmdBindItem.Evaluate(false, true),
        NativeGmCmdBindItem.Evaluate(true, false),
    })
    {
        Equal(usage.Branch, CmdBindItemBranch.Usage, "cmdbinditem usage branch");
        Equal(usage.CallsCore, false, "cmdbinditem usage no core");
        Equal(usage.SendsSysMsg, true, "cmdbinditem usage sends msg");
        Equal(usage.MessageColor, 0xFFDB, "cmdbinditem usage colour");
    }
}

static void VerifySetMaxButchCount()
{
    var ok = NativeGmSetMaxButchCount.Evaluate(guardPass: true);
    Equal(ok.Branch, SetMaxButchCountBranch.Applied, "setmaxbutch applied branch");
    Equal(ok.WritesGlobal, true, "setmaxbutch writes global");
    Equal(ok.GlobalEa, 0x007D6888u, "setmaxbutch global ea");
    Equal(ok.PersistsConfig, true, "setmaxbutch persists config");
    Equal(ok.ConfigSection, "MaxBeButchedCount", "setmaxbutch config section");
    Equal(ok.ConfigKey, "setup", "setmaxbutch config key");
    Equal(ok.ManagerGetterEa, 0x00790210u, "setmaxbutch mgr getter ea");
    Equal(ok.SendsSysMsg, true, "setmaxbutch sends msg");
    Equal(ok.MessageColor, 0xFFDB, "setmaxbutch colour");

    var blocked = NativeGmSetMaxButchCount.Evaluate(guardPass: false);
    Equal(blocked.Branch, SetMaxButchCountBranch.GuardFailed, "setmaxbutch guard-fail branch");
    Equal(blocked.WritesGlobal, false, "setmaxbutch guard-fail no write");
    Equal(blocked.PersistsConfig, false, "setmaxbutch guard-fail no persist");
    Equal(blocked.SendsSysMsg, true, "setmaxbutch guard-fail usage msg");
}

static void VerifyNoOps()
{
    // all 8 no-ops: recognized, route to def_622B15, mutate nothing, send nothing
    foreach (var info in NativeGmItemExtraCommands.All)
    {
        if (info.Implemented)
            continue;
        var o = NativeGmItemExtraCommands.EvaluateUnimplemented(info.Command);
        Equal(o.Recognized, true, $"{info.Command} recognized");
        Equal(o.DispatchesToDefaultCase, true, $"{info.Command} to def_622B15");
        Equal(o.MutatesState, false, $"{info.Command} no effect");
        Equal(o.SendsResponse, false, $"{info.Command} no response");
    }

    // an implemented command must NOT be routed through the no-op path
    var threw = false;
    try { NativeGmItemExtraCommands.EvaluateUnimplemented(GmItemExtraCommand.Make); }
    catch (InvalidOperationException) { threw = true; }
    Equal(threw, true, "implemented command rejected by no-op path");
}
