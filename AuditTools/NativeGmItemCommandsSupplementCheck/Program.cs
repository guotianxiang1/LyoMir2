using GameSvr;

// Contract check for the dormant ITEM/EQUIPMENT GM command family SUPPLEMENT model
// (GameSvr/Services/NativeGmItemCommandsSupplement.cs), locked against the Hex-Rays-verified original
// dispatcher sub_622820 (single switch, table jpt_622B15 @0x00622B1C) in the unpacked M2Server image.
//
// This complements NativeGmItemCommandsCheck: it covers the item/equip commands that file omits
// (StorageItem 167, GetBackItem 168, LookUserItemId 191, ChgEquipLevel 229, SetItemTimeOut 434) and the
// registered no-op SetEquipComposeAbil 499. Index/perm come from the command-table dump
// ida_award_case584_command_registry_20260720.txt and are cross-checked to the disassembly case labels.

try
{
    VerifyDispatcherConstants();
    VerifyRegistry();
    VerifyCasePtrMath();
    VerifyPermissionGate();
    VerifyForwarders();
    VerifySetItemTimeOut();
    VerifyNoOps();

    Console.WriteLine(
        "PASS NativeGmItemCommandsSupplementCheck dispatcher=sub_622820 table=0x622B1C max=750 " +
        "implemented=StorageItem/GetBackItem/LookUserItemId/ChgEquipLevel/SetItemTimeOut " +
        "noop=SetEquipComposeAbil " +
        "coreDeferred=StorageItem/GetBackItem/LookUserItemId/ChgEquipLevel/SetItemTimeOut");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"NativeGmItemCommandsSupplementCheck FAIL: {ex.Message}");
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
    Equal(NativeGmItemCommandsSupplement.DispatcherEa, 0x00622820u, "dispatcher ea");
    Equal(NativeGmItemCommandsSupplement.IndexLookupEa, 0x00621F28u, "index lookup ea");
    Equal(NativeGmItemCommandsSupplement.JumpTableEa, 0x00622B1Cu, "jump table ea");
    Equal(NativeGmItemCommandsSupplement.SwitchMaxIndex, 750, "switch max index");
    Equal(NativeGmItemCommandsSupplement.DefaultCaseEa, 0x0062B648u, "default case ea");
    Equal(NativeGmItemCommandsSupplement.EpilogueEa, 0x0062B64Cu, "epilogue ea");

    // command-record table facts (the source of the index/perm values)
    Equal(NativeGmItemCommandsSupplement.CommandTableEa, 0x007B4654u, "command table ea");
    Equal(NativeGmItemCommandsSupplement.CommandTableRecordStride, 0x78, "record stride");
    Equal(NativeGmItemCommandsSupplement.RecordIndexFieldOffset, 0x18, "record index offset");
    Equal(NativeGmItemCommandsSupplement.RecordPermFieldOffset, 0x1C, "record perm offset");

    // SetItemTimeOut shim-level parse constants
    Equal(NativeGmItemCommandsSupplement.SetItemTimeOutDelimiter, 0x20, "SetItemTimeOut delimiter");
}

static void VerifyRegistry()
{
    // (command, name, index, perm, implemented, caseAddr, coreEa, coreDeferred, fwdArgs)
    (GmItemSupCommand cmd, string name, int idx, int perm, bool impl, uint caseAddr, uint coreEa, bool deferred, int fwd)[] expected =
    {
        (GmItemSupCommand.StorageItem,    "StorageItem",    167, 4, true,  0x00625CF2u, 0x0062E730u, true,  0),
        (GmItemSupCommand.GetBackItem,    "GetBackItem",    168, 4, true,  0x00625D02u, 0x0062E7CCu, true,  2),
        (GmItemSupCommand.LookUserItemId, "LookUserItemId", 191, 4, true,  0x00625FEDu, 0x006D07C4u, true,  1),
        (GmItemSupCommand.ChgEquipLevel,  "ChgEquipLevel",  229, 5, true,  0x006261EFu, 0x006D6DECu, true,  2),
        (GmItemSupCommand.SetItemTimeOut, "SetItemTimeOut", 434, 4, true,  0x00627714u, 0x006BD8F8u, true,  2),
        (GmItemSupCommand.SetEquipComposeAbil, "SetEquipComposeAbil", 499, 5, false, NativeGmItemCommandsSupplement.DefaultCaseEa, 0u, false, 0),
    };

    Equal(NativeGmItemCommandsSupplement.All.Count, expected.Length, "registry count");
    foreach (var e in expected)
    {
        var info = NativeGmItemCommandsSupplement.Info(e.cmd);
        Equal(info.Name, e.name, $"{e.cmd} name");
        Equal(info.DispatchIndex, e.idx, $"{e.cmd} index");
        Equal(info.RequiredPermission, e.perm, $"{e.cmd} perm");
        Equal(info.Implemented, e.impl, $"{e.cmd} implemented flag");
        Equal(info.CaseAddress, e.caseAddr, $"{e.cmd} case address");
        Equal(info.CoreEa, e.coreEa, $"{e.cmd} core ea");
        Equal(info.CoreBodyDeferred, e.deferred, $"{e.cmd} core deferred");
        Equal(info.ForwardedArgCount, e.fwd, $"{e.cmd} forwarded arg count");

        Assert(info.DispatchIndex >= 0 && info.DispatchIndex <= NativeGmItemCommandsSupplement.SwitchMaxIndex,
            $"{e.cmd} index in switch range");
        // implemented => real case address + a deferred core; unimplemented => default label + no core
        if (e.impl)
        {
            Assert(info.CaseAddress != NativeGmItemCommandsSupplement.DefaultCaseEa, $"{e.cmd} has distinct case");
            Assert(info.CoreEa != 0 && info.CoreBodyDeferred, $"{e.cmd} core is deferred");
        }
        else
        {
            Equal(info.CaseAddress, NativeGmItemCommandsSupplement.DefaultCaseEa, $"{e.cmd} on default case");
            Assert(info.CoreEa == 0 && !info.CoreBodyDeferred, $"{e.cmd} has no core");
        }
    }
}

static void VerifyCasePtrMath()
{
    // jump-table slot address must be jpt_622B15 + index*4 for every command
    foreach (var info in NativeGmItemCommandsSupplement.All)
    {
        var expected = 0x00622B1Cu + (uint)info.DispatchIndex * 4;
        Equal(NativeGmItemCommandsSupplement.CasePtr(info.DispatchIndex), expected, $"{info.Command} case_ptr");
    }
    // spot-check known slots
    Equal(NativeGmItemCommandsSupplement.CasePtr(167), 0x00622DB8u, "StorageItem case_ptr");
    Equal(NativeGmItemCommandsSupplement.CasePtr(499), 0x006232E8u, "SetEquipComposeAbil case_ptr");
    Equal(NativeGmItemCommandsSupplement.CasePtr(0), NativeGmItemCommandsSupplement.JumpTableEa, "slot 0 = table base");
}

static void VerifyPermissionGate()
{
    // sub_621F28: dispatch iff callerPerm >= requiredPerm; else 0 -> def_622B15 (silent)
    Assert(NativeGmItemCommandsSupplement.PermitsDispatch(5, 4), "perm 5 >= 4 dispatches");
    Assert(NativeGmItemCommandsSupplement.PermitsDispatch(4, 4), "perm 4 >= 4 dispatches (boundary)");
    Assert(!NativeGmItemCommandsSupplement.PermitsDispatch(3, 4), "perm 3 < 4 -> silent default");
    Assert(!NativeGmItemCommandsSupplement.PermitsDispatch(4, 5), "perm 4 < 5 (ChgEquipLevel) -> silent default");
}

static void VerifyForwarders()
{
    // pure forwarder shims: forward self, no validation, no SysMsg, deferred core
    var storage = NativeGmItemSupForwarders.StorageItem();
    Equal(storage.CoreEa, 0x0062E730u, "StorageItem core ea");
    Equal(storage.ForwardedArgCount, 0, "StorageItem fwd args");

    var getBack = NativeGmItemSupForwarders.GetBackItem();
    Equal(getBack.CoreEa, 0x0062E7CCu, "GetBackItem core ea");
    Equal(getBack.ForwardedArgCount, 2, "GetBackItem fwd args");

    var look = NativeGmItemSupForwarders.LookUserItemId();
    Equal(look.CoreEa, 0x006D07C4u, "LookUserItemId core ea");
    Equal(look.ForwardedArgCount, 1, "LookUserItemId fwd args");

    var chg = NativeGmItemSupForwarders.ChgEquipLevel();
    Equal(chg.CoreEa, 0x006D6DECu, "ChgEquipLevel core ea");
    Equal(chg.ForwardedArgCount, 2, "ChgEquipLevel fwd args");

    foreach (var o in new[] { storage, getBack, look, chg })
    {
        Assert(o.ForwardsSelf, "forwarder forwards self");
        Assert(o.CoreBodyDeferred, "forwarder core deferred");
        Assert(!o.ShimValidates, "forwarder shim does not validate");
        Assert(!o.ShimSendsSysMsg, "forwarder shim sends no SysMsg");
    }
}

static void VerifySetItemTimeOut()
{
    var o = NativeGmSetItemTimeOut.Evaluate();
    Assert(o.TokenizesArg, "SetItemTimeOut tokenizes param1");
    Equal(o.Delimiter, 0x20, "SetItemTimeOut delimiter is space");
    Assert(o.ParsesIntArg, "SetItemTimeOut parses param0 to int");
    Assert(o.CallsCore, "SetItemTimeOut calls core");
    Equal(o.CoreEa, 0x006BD8F8u, "SetItemTimeOut core ea");
    Assert(o.CoreBodyDeferred, "SetItemTimeOut core deferred");
    Assert(o.ForwardsSelf, "SetItemTimeOut forwards self");
    Assert(!o.ShimSendsSysMsg, "SetItemTimeOut shim sends no SysMsg");
}

static void VerifyNoOps()
{
    GmItemSupCommand[] noop = { GmItemSupCommand.SetEquipComposeAbil };
    foreach (var c in noop)
    {
        var o = NativeGmItemCommandsSupplement.EvaluateUnimplemented(c);
        Assert(o.Recognized && o.DispatchesToDefaultCase, $"{c}: recognized + default");
        Assert(!o.MutatesState && !o.SendsResponse, $"{c}: no effect / no response");
    }

    // implemented commands must NOT be routed through the unimplemented path
    var threw = false;
    try { NativeGmItemCommandsSupplement.EvaluateUnimplemented(GmItemSupCommand.StorageItem); }
    catch (InvalidOperationException) { threw = true; }
    Assert(threw, "implemented command rejected by unimplemented path");
}
