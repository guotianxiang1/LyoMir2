using System.IO;
using GameSvr;

// Contract check for the dormant synthesis-recipe loader NativeStrengthenRecipeStore
// (GameSvr/Services/NativeStrengthenRecipeStore.cs) + the gated-fallback wiring of the live
// CM_STRENGTHEN_EQUIP_QUEST 4465 case. Parse round-trip on ASCII fixtures (structure only), the
// fail-safe/empty behavior, and the gate default. See staging/cm_strengthen_recipe_loader_plan_20260801.md.

try
{
    VerifyGateDefaultOff();
    VerifyParseLineSkips();
    VerifyParseLineStandardRow();
    VerifyParseLineEmptyParamColumn();
    VerifyStoreEmptyAndFailSafe();
    VerifyShippedFileSoft();

    System.Console.WriteLine(
        "PASS NativeStrengthenRecipeStoreCheck gate=SupportsStrengthenRecipes(default OFF) " +
        "parse=result/base/param/[required]/[weight:opts] failsafe=missing->0 " +
        "wiring=4465 gated-fallback (dormant until sub_7548D8 idat confirm)");
    return 0;
}
catch (System.Exception ex)
{
    System.Console.Error.WriteLine($"NativeStrengthenRecipeStoreCheck FAIL: {ex.Message}");
    return 1;
}

static void Assert(bool cond, string msg)
{
    if (!cond) throw new System.Exception(msg);
}

static void Equal<T>(T actual, T expected, string label)
{
    if (!Equals(actual, expected))
        throw new System.Exception($"{label}: expected {expected}, got {actual}");
}

static void VerifyGateDefaultOff()
{
    // Gate OFF by default -> the live 4465 case keeps its fail-closed stub (no behavior change).
    Assert(!NativeStrengthenRecipeStore.SupportsStrengthenRecipes, "gate must default OFF");
}

static void VerifyParseLineSkips()
{
    Equal(NativeStrengthenRecipeStore.ParseLine(null), null, "null -> null");
    Equal(NativeStrengthenRecipeStore.ParseLine(""), null, "empty -> null");
    Equal(NativeStrengthenRecipeStore.ParseLine("   "), null, "blank -> null");
    Equal(NativeStrengthenRecipeStore.ParseLine("# a comment header"), null, "comment -> null");
    Equal(NativeStrengthenRecipeStore.ParseLine("ONLYONECOL"), null, "single column -> null");
    Equal(NativeStrengthenRecipeStore.ParseLine("RESULT\t"), null, "empty base -> null");
}

static void VerifyParseLineStandardRow()
{
    // <result> \t <base> \t <param=2> \t [required]  [weight:opt|opt|opt]
    var r = NativeStrengthenRecipeStore.ParseLine("RESULT\tBASE\t2\t[MAT]    [1:A|B|C]");
    Assert(r != null, "standard row parses");
    Equal(r.ResultName, "RESULT", "result name");
    Equal(r.BaseName, "BASE", "base name");
    Equal(r.Param, 2, "param (col3)");
    Equal(r.Key, 2, "key == param");
    Equal(r.RequiredMaterials.Count, 1, "required count");
    Equal(r.RequiredMaterials[0], "MAT", "required[0]");
    Equal(r.ResultWeight, 1, "result weight");
    Equal(r.ResultOptions.Count, 3, "option count");
    Equal(r.ResultOptions[0], "A", "opt0");
    Equal(r.ResultOptions[2], "C", "opt2");
}

static void VerifyParseLineEmptyParamColumn()
{
    // Some rows carry an empty column before the numeric param (e.g. "R \t B \t \t 8 \t [..]  [..]").
    var r = NativeStrengthenRecipeStore.ParseLine("R\tB\t\t8\t[M1|M2]    [3:X|Y]");
    Assert(r != null, "empty-param-column row parses");
    Equal(r.BaseName, "B", "base name (empty col2 skipped)");
    Equal(r.Param, 8, "param picked from later column");
    Equal(r.RequiredMaterials.Count, 2, "two required materials");
    Equal(r.ResultWeight, 3, "weight 3");
    Equal(r.ResultOptions.Count, 2, "two options");
}

static void VerifyStoreEmptyAndFailSafe()
{
    var store = new NativeStrengthenRecipeStore();
    Equal(store.Count, 0, "fresh store empty");
    Assert(!store.TryGetRecipe("anything", out _), "empty store: no recipe");
    Assert(!store.TryGetRecipe(null, out _), "null name: no recipe");

    // fail-safe: missing/blank path -> 0, no throw.
    Equal(store.Load(null), 0, "Load(null) -> 0");
    Equal(store.Load(""), 0, "Load(\"\") -> 0");
    Equal(store.Load(@"Z:\does\not\exist\SuperEquipSmeltNew.txt"), 0, "Load(missing) -> 0");
    Equal(NativeStrengthenRecipeStore.DefaultConfigPath(null), null, "DefaultConfigPath(null) -> null");

    // Shared store stays dormant (Reload only happens at flip-time / via the reload hook).
    Assert(NativeStrengthenRecipeStore.Shared != null, "Shared store exists");
}

// Soft: if a shipped SuperEquipSmeltNew.txt can be found under a known staging tree, prove the real
// GBK/TAB file loads into >=1 recipe. Skipped (not failed) when the file is absent at check time.
static void VerifyShippedFileSoft()
{
    string[] candidates =
    {
        @"D:\loym2\staging\current-regression\Share\config\SuperEquipSmeltNew.txt",
        @"D:\loym2\staging\p6-root-current\Share\config\SuperEquipSmeltNew.txt",
    };
    foreach (var path in candidates)
    {
        if (!File.Exists(path)) continue;
        var store = new NativeStrengthenRecipeStore();
        int n = store.Load(path);
        Assert(n > 0, $"shipped file parsed >=1 recipe ({path})");
        System.Console.WriteLine($"  [soft] loaded {n} recipes from {path}");
        return;
    }
    System.Console.WriteLine("  [soft] no shipped SuperEquipSmeltNew.txt found at check time — skipped");
}
