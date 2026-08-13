using GameSvr;

// Contract check for the dormant ITEM/EQUIPMENT GM command family model
// (GameSvr/Services/NativeGmItemCommands.cs), locked against the Hex-Rays-verified original
// dispatcher sub_622820 (single switch, table jpt_622B15 @0x00622B1C) in the unpacked M2Server image.

try
{
    VerifyDispatcherConstants();
    VerifyRegistry();
    VerifyShopItem();
    VerifyReLoadPItem();
    VerifyDecDuarg();
    VerifyCmdBind();
    VerifyForwarders();
    VerifyNoOps();

    Console.WriteLine(
        "PASS NativeGmItemCommandsCheck dispatcher=sub_622820 table=0x622B1C max=750 " +
        "implemented=ShopItem/ReLoadPItem/DelSelfItem/GetUserItem/GiveUserItem/DecDuarg/cmdBind/ClearBind " +
        "noop=EquipExchange/SetActScore " +
        "coreDeferred=ShopItem/ReLoadPItem/DelSelfItem/GetUserItem/GiveUserItem/DecDuarg/cmdBind/ClearBind");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"NativeGmItemCommandsCheck FAIL: {ex.Message}");
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
    Equal(NativeGmItemCommands.DispatcherEa, 0x00622820u, "dispatcher ea");
    Equal(NativeGmItemCommands.IndexLookupEa, 0x00621F28u, "index lookup ea");
    Equal(NativeGmItemCommands.JumpTableEa, 0x00622B1Cu, "jump table ea");
    Equal(NativeGmItemCommands.SwitchMaxIndex, 750, "switch max index");
    Equal(NativeGmItemCommands.DefaultCaseEa, 0x0062B648u, "default case ea");

    // shim-level proved constants
    Equal(NativeGmItemCommands.WeaponUseSlot, 1, "weapon use slot");
    Equal(NativeGmItemCommands.DecDuargValue, 100, "DecDuarg target dura");
    Equal(NativeGmItemCommands.CmdBindDefaultType, 1, "cmdBind default bind type");
    Equal(NativeGmItemCommands.ColorInfo, 0xFFDB, "info colour");
}

static void VerifyRegistry()
{
    // (command, name, index, perm, implemented, caseAddr, coreEa, coreDeferred)
    (GmItemCommand cmd, string name, int idx, int perm, bool impl, uint caseAddr, uint coreEa, bool deferred)[] expected =
    {
        (GmItemCommand.ShopItem,      "ShopItem",            85,  4, true,  0x00624DD6u, 0x0063B4E4u, true),
        (GmItemCommand.ReLoadPItem,   "ReLoadPItem",         86,  4, true,  0x00624E05u, 0x0074DEDCu, true),
        (GmItemCommand.DelSelfItem,   "DelSelfItem",         103, 4, true,  0x00625098u, 0x006C1C20u, true),
        (GmItemCommand.GetUserItem,   "GetUserItem",         109, 4, true,  0x00625152u, 0x006C22B0u, true),
        (GmItemCommand.GiveUserItem,  "GiveUserItem",        110, 4, true,  0x00625165u, 0x006C253Cu, true),
        (GmItemCommand.DecDuarg,      "DecDuarg",            113, 4, true,  0x006251D7u, 0x00784598u, true),
        (GmItemCommand.CmdBind,       "cmdBind",             140, 4, true,  0x006254FDu, 0x006C6408u, true),
        (GmItemCommand.ClearBind,     "ClearBind",           141, 4, true,  0x0062551Eu, 0x006C6608u, true),
        (GmItemCommand.EquipExchange, "EquipExchange",       557, 3, false, NativeGmItemCommands.DefaultCaseEa, 0u, false),
        (GmItemCommand.SetActScore,   "SetActScore",         264, 4, false, NativeGmItemCommands.DefaultCaseEa, 0u, false),
    };

    Equal(NativeGmItemCommands.All.Count, expected.Length, "registry count");
    foreach (var e in expected)
    {
        var info = NativeGmItemCommands.Info(e.cmd);
        Equal(info.Name, e.name, $"{e.cmd} name");
        Equal(info.DispatchIndex, e.idx, $"{e.cmd} index");
        Equal(info.RequiredPermission, e.perm, $"{e.cmd} perm");
        Equal(info.Implemented, e.impl, $"{e.cmd} implemented");
        Equal(info.CaseAddress, e.caseAddr, $"{e.cmd} case address");
        Equal(info.CoreEa, e.coreEa, $"{e.cmd} core ea");
        Equal(info.CoreBodyDeferred, e.deferred, $"{e.cmd} core deferred");
        Assert(info.DispatchIndex >= 0 && info.DispatchIndex <= NativeGmItemCommands.SwitchMaxIndex,
            $"{e.cmd} index in switch range");
        if (e.impl)
            Assert(info.CaseAddress != NativeGmItemCommands.DefaultCaseEa, $"{e.cmd} has distinct case");
        else
            Equal(info.CaseAddress, NativeGmItemCommands.DefaultCaseEa, $"{e.cmd} on default case");
    }
}

static void VerifyShopItem()
{
    var o = NativeGmShopItem.Evaluate("火龙之心");
    Assert(o.QueriesShop, "shop: queries shop");
    Equal(o.QueryCoreEa, 0x0063B4E4u, "shop: core ea");
    Assert(o.CoreBodyDeferred, "shop: core deferred");
    Assert(!o.MutatesState, "shop: read-only");
    Assert(o.SendsSysMsg, "shop: always SysMsg");
    Equal(o.MessageColor, 0xFFDB, "shop: info colour");

    // the shim does not gate on the item name — empty name still evaluates identically
    var o2 = NativeGmShopItem.Evaluate("");
    Assert(o2.SendsSysMsg, "shop: empty name still SysMsg");
    Assert(!o2.MutatesState, "shop: empty still read-only");
}

static void VerifyReLoadPItem()
{
    var o = NativeGmReLoadPItem.Evaluate();
    Assert(o.ReloadsConfig, "reload: reloads config");
    Equal(o.ReloadCoreEa, 0x0074DEDCu, "reload: core ea");
    Assert(o.CoreBodyDeferred, "reload: core deferred");
    Assert(o.MutatesState, "reload: mutates global table");
    Assert(o.SendsSysMsg, "reload: always SysMsg");
    Equal(o.MessageColor, 0xFFDB, "reload: info colour");
}

static void VerifyDecDuarg()
{
    // weapon present -> core called, dura set to 100
    var a = NativeGmDecDuarg.Evaluate(weaponPresent: true);
    Equal(a.Branch, DecDuargBranch.WeaponDuraSet, "dec: weapon present branch");
    Assert(a.TargetsSelf, "dec: targets self");
    Equal(a.UseSlot, 1, "dec: weapon slot");
    Assert(a.CallsApplyCore, "dec: calls apply core");
    Equal(a.DuraValue, 100, "dec: dura value 100");
    Assert(a.CoreBodyDeferred, "dec: core deferred");
    Assert(!a.SendsSysMsg, "dec: silent");

    // weapon absent -> shim exits (jz epilogue), core not called
    var b = NativeGmDecDuarg.Evaluate(weaponPresent: false);
    Equal(b.Branch, DecDuargBranch.WeaponAbsent, "dec: absent branch");
    Assert(!b.CallsApplyCore, "dec: absent no core");
    Assert(!b.CoreBodyDeferred, "dec: absent no deferral");
    Assert(!b.SendsSysMsg, "dec: absent silent");
}

static void VerifyCmdBind()
{
    // numeric param -> parsed value used, no default
    var a = NativeGmCmdBind.Evaluate("2");
    Equal(a.BindType, 2, "bind: explicit type 2");
    Assert(!a.UsedDefault, "bind: no default for numeric");
    Assert(a.CallsCore, "bind: always calls core");
    Equal(a.CoreEa, 0x006C6408u, "bind: core ea");
    Assert(a.CoreBodyDeferred, "bind: core deferred");
    Assert(!a.ShimSendsSysMsg, "bind: shim silent");

    // empty param -> default 1
    var b = NativeGmCmdBind.Evaluate("");
    Equal(b.BindType, 1, "bind: default type 1");
    Assert(b.UsedDefault, "bind: used default");
    Assert(b.CallsCore, "bind: default still calls core");

    // non-numeric param -> default 1
    var c = NativeGmCmdBind.Evaluate("abc");
    Equal(c.BindType, 1, "bind: non-numeric default 1");
    Assert(c.UsedDefault, "bind: non-numeric used default");

    // type 0 is a valid parsed value (not the default)
    var d = NativeGmCmdBind.Evaluate("0");
    Equal(d.BindType, 0, "bind: explicit 0");
    Assert(!d.UsedDefault, "bind: 0 not default");
}

static void VerifyForwarders()
{
    // DelSelfItem: 2 forwarded args (itemName, count), self always forwarded, shim validates nothing
    var del = NativeGmItemForwarders.DelSelfItem();
    Equal(del.CoreEa, 0x006C1C20u, "del: core ea");
    Equal(del.ForwardedArgCount, 2, "del: 2 args");
    Assert(del.ForwardsSelf, "del: forwards self");
    Assert(!del.ShimValidates, "del: no shim validation");
    Assert(!del.ShimSendsSysMsg, "del: shim silent");
    Assert(del.CoreBodyDeferred, "del: core deferred");

    // GetUserItem: 2 forwarded args (charName, itemId)
    var get = NativeGmItemForwarders.GetUserItem();
    Equal(get.CoreEa, 0x006C22B0u, "get: core ea");
    Equal(get.ForwardedArgCount, 2, "get: 2 args");
    Assert(get.ForwardsSelf, "get: forwards self");
    Assert(!get.ShimValidates, "get: no shim validation");
    Assert(!get.ShimSendsSysMsg, "get: shim silent");

    // GiveUserItem: 3 forwarded args (charName, itemId, bindTime)
    var give = NativeGmItemForwarders.GiveUserItem();
    Equal(give.CoreEa, 0x006C253Cu, "give: core ea");
    Equal(give.ForwardedArgCount, 3, "give: 3 args (charName, itemId, bindTime)");
    Assert(give.ForwardsSelf, "give: forwards self");
    Assert(!give.ShimSendsSysMsg, "give: shim silent");

    // ClearBind: 1 forwarded arg (charName)
    var clear = NativeGmItemForwarders.ClearBind();
    Equal(clear.CoreEa, 0x006C6608u, "clear: core ea");
    Equal(clear.ForwardedArgCount, 1, "clear: 1 arg");
    Assert(clear.ForwardsSelf, "clear: forwards self");
    Assert(!clear.ShimSendsSysMsg, "clear: shim silent");
}

static void VerifyNoOps()
{
    GmItemCommand[] noop =
    {
        GmItemCommand.EquipExchange,
        GmItemCommand.SetActScore,
    };
    foreach (var c in noop)
    {
        var o = NativeGmItemCommands.EvaluateUnimplemented(c);
        Assert(o.Recognized && o.DispatchesToDefaultCase, $"{c}: recognized+default");
        Assert(!o.MutatesState && !o.SendsResponse, $"{c}: no effect / no response");
    }

    // implemented commands must NOT be routed through the unimplemented path
    var threw = false;
    try { NativeGmItemCommands.EvaluateUnimplemented(GmItemCommand.ShopItem); }
    catch (InvalidOperationException) { threw = true; }
    Assert(threw, "implemented command rejected by unimplemented path");
}
