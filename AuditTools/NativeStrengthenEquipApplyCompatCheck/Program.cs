using GameSvr;

// Contract check for the dormant CM_STRENGTHEN_EQUIP = 4466 apply stage model
// (GameSvr/Services/NativeStrengthenEquipApply.cs), locked against the Hex-Rays-verified originals
// sub_60FFDC (free), sub_60FF28 + sub_60FC1C (paid/async).

try
{
    VerifyConstants();
    VerifyFreePath();
    VerifyAsyncPath();
    VerifySuccessIsWParamOne();

    Console.WriteLine(
        "PASS NativeStrengthenEquipApplyCompatCheck ident=4466 success-wparam=1 free-fail=-1 " +
        "async=materialgone/resultfail-notice-only consume=base+materials add=result dormant=true");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"NativeStrengthenEquipApplyCompatCheck FAIL: {ex.Message}");
    return 1;
}

static void Assert(bool cond, string msg)
{
    if (!cond)
        throw new Exception(msg);
}

static void VerifyConstants()
{
    Assert(NativeStrengthenEquipApply.Ident == 4466, "ident");
    Assert(NativeStrengthenEquipApply.SuccessWParam == 1, "success wparam");
    Assert(NativeStrengthenEquipApply.FreeFailWParam == -1, "free fail wparam");
    Assert(NativeStrengthenEquipApply.VtblDeleteItem == 0x268, "vtbl delete");
    Assert(NativeStrengthenEquipApply.VtblAddItem == 0x248, "vtbl add");
    Assert(NativeStrengthenEquipApply.VtblSendDefMessage == 0x250, "vtbl send");
    Assert(NativeStrengthenEquipApply.AsyncFlagOffset == 0xBA6, "async flag offset");
}

static void VerifyFreePath()
{
    // Free success: result created -> SM(wParam=1), consume, add.
    var ok = NativeStrengthenEquipApply.Evaluate(isPaidPath: false, resultItemCreated: true, asyncMaterialsPresent: false);
    Assert(ok.Branch == NativeStrengthenEquipApplyBranch.FreeSuccess, "free success branch");
    Assert(ok.SendsStrengthenMessage && ok.MessageWParam == 1, "free success sends wParam 1");
    Assert(ok.ConsumesBaseAndMaterials && ok.AddsResultItem, "free success effects");

    // Free result-create fail: sends SM(wParam=-1), nothing consumed/added.
    var fail = NativeStrengthenEquipApply.Evaluate(isPaidPath: false, resultItemCreated: false, asyncMaterialsPresent: false);
    Assert(fail.Branch == NativeStrengthenEquipApplyBranch.FreeResultCreateFailed, "free fail branch");
    Assert(fail.SendsStrengthenMessage && fail.MessageWParam == -1, "free fail sends wParam -1");
    Assert(!fail.ConsumesBaseAndMaterials && !fail.AddsResultItem, "free fail no effects");
}

static void VerifyAsyncPath()
{
    // Async success: materials present + result created -> SM(wParam=1), consume, add.
    var ok = NativeStrengthenEquipApply.Evaluate(isPaidPath: true, resultItemCreated: true, asyncMaterialsPresent: true);
    Assert(ok.Branch == NativeStrengthenEquipApplyBranch.AsyncSuccess, "async success branch");
    Assert(ok.SendsStrengthenMessage && ok.MessageWParam == 1, "async success sends wParam 1");
    Assert(ok.ConsumesBaseAndMaterials && ok.AddsResultItem, "async success effects");

    // Async material vanished before the callback: local notice only, NO SM(4466), nothing consumed.
    var gone = NativeStrengthenEquipApply.Evaluate(isPaidPath: true, resultItemCreated: true, asyncMaterialsPresent: false);
    Assert(gone.Branch == NativeStrengthenEquipApplyBranch.AsyncMaterialGone, "async gone branch");
    Assert(!gone.SendsStrengthenMessage, "async gone sends no SM");
    Assert(!gone.ConsumesBaseAndMaterials && !gone.AddsResultItem, "async gone no effects");

    // Async result create fail: local notice only, NO SM, materials NOT consumed (delete is guarded by result).
    var rf = NativeStrengthenEquipApply.Evaluate(isPaidPath: true, resultItemCreated: false, asyncMaterialsPresent: true);
    Assert(rf.Branch == NativeStrengthenEquipApplyBranch.AsyncResultCreateFailed, "async result-fail branch");
    Assert(!rf.SendsStrengthenMessage, "async result-fail sends no SM");
    Assert(!rf.ConsumesBaseAndMaterials && !rf.AddsResultItem, "async result-fail no effects");
}

static void VerifySuccessIsWParamOne()
{
    // Both success paths converge on the same client signal: wParam = 1 (not 0).
    var free = NativeStrengthenEquipApply.Evaluate(false, true, false);
    var async = NativeStrengthenEquipApply.Evaluate(true, true, true);
    Assert(free.MessageWParam == 1 && async.MessageWParam == 1, "both successes wParam 1");
    Assert(free.SendsStrengthenMessage && async.SendsStrengthenMessage, "both successes send");
}
