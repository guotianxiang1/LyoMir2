using System.Collections.Generic;
using GameSvr;

// Contract check for the dormant CM_STRENGTHEN_EQUIP = 4466 execution front-half model
// (GameSvr/Services/NativeStrengthenEquipExec.cs), locked against the Hex-Rays-verified original
// sub_60F7AC @0x0060F7AC (unpacked M2Server.exe, image base 0x00400000).

try
{
    VerifyConstants();
    VerifyMaterialLoop();
    VerifyPostLoopLadder();
    VerifyCostAndPaths();
    VerifyPrecedence();

    Console.WriteLine(
        "PASS NativeStrengthenEquipExecCompatCheck ident=4466 " +
        "loop=notfound10/locked8/mixname9/instance-dedup " +
        "ladder=valid3/recipe4/guard6/basekey7/result2/gold5 " +
        "cost=(5-distinct)*price paid=cost>0 apply-stage=deferred dormant=true");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"NativeStrengthenEquipExecCompatCheck FAIL: {ex.Message}");
    return 1;
}

static void Assert(bool cond, string msg)
{
    if (!cond)
        throw new Exception(msg);
}

static NativeStrengthenEquipSlot Base(int matchWord) => new()
{
    Found = true, IsBase = true, BaseMatchWord = matchWord,
};

static NativeStrengthenEquipSlot Mat(string name, int instance, bool locked = false) => new()
{
    Found = true, IsBase = false, Locked = locked, Name = name, InstanceKey = instance,
};

static NativeStrengthenEquipSlot Missing() => new() { Found = false };

static NativeStrengthenEquipExecContext Ctx(
    IReadOnlyList<NativeStrengthenEquipSlot> slots, bool recipe, int key, int price,
    bool guard, bool result, int gold) => new()
{
    Slots = slots, RecipeFound = recipe, RecipeKey = key, UnitPrice = price,
    GuardPassed = guard, ResultSelected = result, PlayerGold = gold,
};

// A fully valid submission: one base (match key 5) + two same-name distinct material instances.
static NativeStrengthenEquipExecContext Valid(int price = 100, int gold = 100000) =>
    Ctx(new[] { Base(5), Mat("m", 1), Mat("m", 2) }, true, 5, price, true, true, gold);

static NativeStrengthenEquipExecCode Code(NativeStrengthenEquipExecContext ctx) =>
    NativeStrengthenEquipExec.Evaluate(ctx).Code;

static void VerifyConstants()
{
    Assert(NativeStrengthenEquipExec.Ident == 4466, "ident");
    Assert(NativeStrengthenEquipExec.VtblSendDefMessage == 0x250, "vtbl");
    Assert(NativeStrengthenEquipExec.PlayerGoldOffset == 0x760, "gold offset");
    Assert(NativeStrengthenEquipExec.BaseStdItemFlagOffset == 0x14, "base flag offset");
    Assert(NativeStrengthenEquipExec.BaseStdItemFlagValue == 0x40, "base flag value");
    Assert(NativeStrengthenEquipExec.BaseMatchByteOffset == 0x15, "base match offset");
    Assert(NativeStrengthenEquipExec.ItemLockWordOffset == 0x34, "lock offset");
    Assert(NativeStrengthenEquipExec.ItemInstanceKeyOffset == 0x20, "instance offset");
    Assert(NativeStrengthenEquipExec.RecipeKeyOffset == 0x08, "recipe key offset");
    Assert(NativeStrengthenEquipExec.RecipeWeightBoundOffset == 0x0A, "weight bound offset");

    Assert((int)NativeStrengthenEquipExecCode.FrontHalfPassed == 0, "wire 0");
    Assert((int)NativeStrengthenEquipExecCode.ResultNotSelected == 2, "wire 2");
    Assert((int)NativeStrengthenEquipExecCode.ValidationFailed == 3, "wire 3");
    Assert((int)NativeStrengthenEquipExecCode.RecipeNotFound == 4, "wire 4");
    Assert((int)NativeStrengthenEquipExecCode.InsufficientGold == 5, "wire 5");
    Assert((int)NativeStrengthenEquipExecCode.GuardRejected == 6, "wire 6");
    Assert((int)NativeStrengthenEquipExecCode.BaseKeyMismatch == 7, "wire 7");
    Assert((int)NativeStrengthenEquipExecCode.MaterialLocked == 8, "wire 8");
    Assert((int)NativeStrengthenEquipExecCode.MaterialNameMismatch == 9, "wire 9");
    Assert((int)NativeStrengthenEquipExecCode.MaterialNotFound == 10, "wire 10");
}

static void VerifyMaterialLoop()
{
    // A submitted id that resolves to nothing -> 10.
    Assert(Code(Ctx(new[] { Base(5), Mat("m", 1), Missing() }, true, 5, 100, true, true, 100000))
        == NativeStrengthenEquipExecCode.MaterialNotFound, "not found -> 10");

    // A locked material -> 8.
    Assert(Code(Ctx(new[] { Base(5), Mat("m", 1), Mat("m", 2, locked: true) }, true, 5, 100, true, true, 100000))
        == NativeStrengthenEquipExecCode.MaterialLocked, "locked -> 8");

    // Mixed material names -> 9.
    Assert(Code(Ctx(new[] { Base(5), Mat("a", 1), Mat("b", 2) }, true, 5, 100, true, true, 100000))
        == NativeStrengthenEquipExecCode.MaterialNameMismatch, "mixed name -> 9");

    // Same physical instance is de-duplicated: two submissions of instance 1 -> only 1 distinct
    // material -> validation fails (need >= 2).
    var dedup = NativeStrengthenEquipExec.Evaluate(
        Ctx(new[] { Base(5), Mat("m", 1), Mat("m", 1) }, true, 5, 100, true, true, 100000));
    Assert(dedup.Code == NativeStrengthenEquipExecCode.ValidationFailed, "instance dedup -> validation");
    Assert(dedup.DistinctMaterials == 1, "instance dedup distinct count");

    // Two same-name distinct instances -> loop clean, front-half passes.
    Assert(Code(Valid()) == NativeStrengthenEquipExecCode.FrontHalfPassed, "valid set passes loop");
}

static void VerifyPostLoopLadder()
{
    // No base -> validation 3.
    Assert(Code(Ctx(new[] { Mat("m", 1), Mat("m", 2) }, true, 5, 100, true, true, 100000))
        == NativeStrengthenEquipExecCode.ValidationFailed, "no base -> 3");
    // Two bases -> validation 3 (baseCount must be exactly 1).
    Assert(Code(Ctx(new[] { Base(5), Base(5), Mat("m", 1), Mat("m", 2) }, true, 5, 100, true, true, 100000))
        == NativeStrengthenEquipExecCode.ValidationFailed, "two bases -> 3");
    // Fewer than 2 distinct materials -> 3.
    Assert(Code(Ctx(new[] { Base(5), Mat("m", 1) }, true, 5, 100, true, true, 100000))
        == NativeStrengthenEquipExecCode.ValidationFailed, "one material -> 3");

    // Recipe not found -> 4.
    Assert(Code(Ctx(new[] { Base(5), Mat("m", 1), Mat("m", 2) }, false, 5, 100, true, true, 100000))
        == NativeStrengthenEquipExecCode.RecipeNotFound, "no recipe -> 4");
    // Guard rejected -> 6.
    Assert(Code(Ctx(new[] { Base(5), Mat("m", 1), Mat("m", 2) }, true, 5, 100, false, true, 100000))
        == NativeStrengthenEquipExecCode.GuardRejected, "guard -> 6");
    // Base key mismatch -> 7 (base match word != recipe key).
    Assert(Code(Ctx(new[] { Base(9), Mat("m", 1), Mat("m", 2) }, true, 5, 100, true, true, 100000))
        == NativeStrengthenEquipExecCode.BaseKeyMismatch, "base key -> 7");
    // Result not selected -> 2.
    Assert(Code(Ctx(new[] { Base(5), Mat("m", 1), Mat("m", 2) }, true, 5, 100, true, false, 100000))
        == NativeStrengthenEquipExecCode.ResultNotSelected, "no result -> 2");
    // Insufficient gold -> 5 (cost 300 > gold 100).
    Assert(Code(Ctx(new[] { Base(5), Mat("m", 1), Mat("m", 2) }, true, 5, 100, true, true, 100))
        == NativeStrengthenEquipExecCode.InsufficientGold, "gold -> 5");

    // Base key comparison is by low word.
    Assert(Code(Ctx(new[] { Base(0x1_0005), Mat("m", 1), Mat("m", 2) }, true, 5, 100, true, true, 100000))
        == NativeStrengthenEquipExecCode.FrontHalfPassed, "base key low-word match");
}

static void VerifyCostAndPaths()
{
    // distinct = 2 -> cost = (5-2)*100 = 300 (paid path).
    var paid = NativeStrengthenEquipExec.Evaluate(Valid(price: 100, gold: 100000));
    Assert(paid.Code == NativeStrengthenEquipExecCode.FrontHalfPassed, "paid passes");
    Assert(paid.Cost == 300, "cost (5-2)*100");
    Assert(paid.IsPaidPath, "paid path flag");
    Assert(paid.DistinctMaterials == 2 && paid.BaseCount == 1, "paid classification");

    // distinct = 5 -> cost = (5-5)*price = 0 (free path).
    var freeSlots = new List<NativeStrengthenEquipSlot>
    {
        Base(5), Mat("m", 1), Mat("m", 2), Mat("m", 3), Mat("m", 4), Mat("m", 5),
    };
    var free = NativeStrengthenEquipExec.Evaluate(Ctx(freeSlots, true, 5, 100, true, true, 100000));
    Assert(free.Code == NativeStrengthenEquipExecCode.FrontHalfPassed, "free passes");
    Assert(free.Cost == 0 && !free.IsPaidPath, "free path zero cost");
    Assert(free.DistinctMaterials == 5, "free distinct count");
}

static void VerifyPrecedence()
{
    // A material-loop error (not found) precedes every post-loop failure even when recipe/guard/etc.
    // would also fail.
    Assert(Code(Ctx(new[] { Missing() }, false, 0, 0, false, false, 0))
        == NativeStrengthenEquipExecCode.MaterialNotFound, "loop error precedes post-loop");

    // Validation precedes recipe: no base and no recipe -> 3, not 4.
    Assert(Code(Ctx(new[] { Mat("m", 1), Mat("m", 2) }, false, 0, 0, false, false, 0))
        == NativeStrengthenEquipExecCode.ValidationFailed, "validation precedes recipe");

    // Guard precedes base-key: guard false and base-key wrong -> 6, not 7.
    Assert(Code(Ctx(new[] { Base(9), Mat("m", 1), Mat("m", 2) }, true, 5, 100, false, false, 0))
        == NativeStrengthenEquipExecCode.GuardRejected, "guard precedes base-key");

    // Result precedes gold: no result and insufficient gold -> 2, not 5.
    Assert(Code(Ctx(new[] { Base(5), Mat("m", 1), Mat("m", 2) }, true, 5, 100, true, false, 0))
        == NativeStrengthenEquipExecCode.ResultNotSelected, "result precedes gold");
}
