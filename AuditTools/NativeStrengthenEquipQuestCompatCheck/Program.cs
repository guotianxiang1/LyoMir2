using GameSvr;

// Contract check for the dormant CM_STRENGTHEN_EQUIP_QUEST = 4465 model
// (GameSvr/Services/NativeStrengthenEquipQuest.cs), locked against the original
// read-only handler sub_60F5C0 @0x0060F5C0 (unpacked M2Server.exe, image base 0x00400000).

try
{
    VerifyConstants();
    VerifyLadderOrder();
    VerifyFieldPopulation();
    VerifySuccessBody();
    VerifyLowWordMasking();

    Console.WriteLine(
        "PASS NativeStrengthenEquipQuestCompatCheck ident=4465 readonly " +
        "ladder=0/2/3/4/1 wparam=recipeKey-after-recipe extra=after-guard " +
        "body=count*2-words dormant=true");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"NativeStrengthenEquipQuestCompatCheck FAIL: {ex.Message}");
    return 1;
}

static void Assert(bool cond, string msg)
{
    if (!cond)
        throw new Exception(msg);
}

static NativeStrengthenEquipQuestContext Ctx(
    bool targetFound, bool recipeFound, int recipeKey, bool guardPassed, int extra, int[] looks) => new()
{
    TargetFound = targetFound,
    RecipeFound = recipeFound,
    RecipeKey = recipeKey,
    GuardPassed = guardPassed,
    ExtraValue = extra,
    MaterialLooks = looks,
};

static NativeStrengthenEquipQuestResult ResultOf(NativeStrengthenEquipQuestContext ctx) =>
    NativeStrengthenEquipQuest.Evaluate(ctx).Result;

static void VerifyConstants()
{
    Assert(NativeStrengthenEquipQuest.Ident == 4465, "ident");
    Assert(NativeStrengthenEquipQuest.VtblSendBuffer == 0x254, "vtbl send buffer");
    Assert(NativeStrengthenEquipQuest.RecipeKeyOffset == 0x08, "recipe key offset");
    Assert(NativeStrengthenEquipQuest.RecipeMaterialsOffset == 0x04, "recipe materials offset");

    Assert((int)NativeStrengthenEquipQuestResult.TargetNotFound == 0, "wire 0");
    Assert((int)NativeStrengthenEquipQuestResult.Success == 1, "wire 1");
    Assert((int)NativeStrengthenEquipQuestResult.RecipeNotFound == 2, "wire 2");
    Assert((int)NativeStrengthenEquipQuestResult.GuardRejected == 3, "wire 3");
    Assert((int)NativeStrengthenEquipQuestResult.NoMaterials == 4, "wire 4");
}

// Each scenario also fails every later lookup, proving guards fire in the original order.
static void VerifyLadderOrder()
{
    Assert(ResultOf(Ctx(false, false, 0, false, 0, null))
        == NativeStrengthenEquipQuestResult.TargetNotFound, "order: target not found");
    Assert(ResultOf(Ctx(true, false, 0, false, 0, null))
        == NativeStrengthenEquipQuestResult.RecipeNotFound, "order: recipe not found");
    Assert(ResultOf(Ctx(true, true, 7, false, 9, new[] { 1, 2 }))
        == NativeStrengthenEquipQuestResult.GuardRejected, "order: guard rejected");
    Assert(ResultOf(Ctx(true, true, 7, true, 9, Array.Empty<int>()))
        == NativeStrengthenEquipQuestResult.NoMaterials, "order: no materials");
    Assert(ResultOf(Ctx(true, true, 7, true, 9, new[] { 5, 6, 7 }))
        == NativeStrengthenEquipQuestResult.Success, "order: success");
}

static void VerifyFieldPopulation()
{
    // esi (wParam) is 0 until the recipe resolves.
    var notFound = NativeStrengthenEquipQuest.Evaluate(Ctx(false, false, 0, false, 0, null));
    Assert(notFound.DispatchWParam == 0 && notFound.ExtraWord == 0 && notFound.ReturnedCount == 0
        && notFound.BodyByteLength == 0 && notFound.ReturnedLooks == null, "not-found fields");

    var noRecipe = NativeStrengthenEquipQuest.Evaluate(Ctx(true, false, 0, false, 0, null));
    Assert(noRecipe.DispatchWParam == 0, "no-recipe keeps wParam 0");

    // guard-rejected: wParam = key, but var_C (extra) and count are still 0.
    var guard = NativeStrengthenEquipQuest.Evaluate(Ctx(true, true, 7, false, 9, new[] { 1, 2 }));
    Assert(guard.DispatchWParam == 7 && guard.ExtraWord == 0 && guard.ReturnedCount == 0
        && guard.BodyByteLength == 0, "guard-rejected fields");

    // no-materials: guard passed so var_C is populated, but count/body stay 0.
    var noMat = NativeStrengthenEquipQuest.Evaluate(Ctx(true, true, 7, true, 9, Array.Empty<int>()));
    Assert(noMat.DispatchWParam == 7 && noMat.ExtraWord == 9 && noMat.ReturnedCount == 0
        && noMat.BodyByteLength == 0, "no-materials fields");
}

static void VerifySuccessBody()
{
    var success = NativeStrengthenEquipQuest.Evaluate(Ctx(true, true, 7, true, 0x1F9, new[] { 0x11, 0x22, 0x33 }));
    Assert(success.Result == NativeStrengthenEquipQuestResult.Success, "success result");
    Assert(success.DispatchWParam == 7, "success wParam");
    Assert(success.ExtraWord == 0x1F9, "success extra");
    Assert(success.ReturnedCount == 3, "success count");
    Assert(success.BodyByteLength == 6, "success body length count*2");
    Assert(success.ReturnedLooks != null && success.ReturnedLooks.Count == 3, "success body present");
    Assert(success.ReturnedLooks[0] == 0x11 && success.ReturnedLooks[1] == 0x22 && success.ReturnedLooks[2] == 0x33,
        "success body order");
}

static void VerifyLowWordMasking()
{
    // var_C is sent as a word; look entries are words -> both masked to 16 bits.
    var extraMask = NativeStrengthenEquipQuest.Evaluate(Ctx(true, true, 7, true, 0x1_0009, new[] { 1 }));
    Assert(extraMask.ExtraWord == 9, "extra masked to low word");

    var lookMask = NativeStrengthenEquipQuest.Evaluate(Ctx(true, true, 7, true, 0, new[] { 0x1_ABCD }));
    Assert(lookMask.ReturnedLooks[0] == 0xABCD, "look masked to low word");
}
