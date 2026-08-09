using GameSvr;

// Contract check for the dormant CM_UPDATE_CLOTHES = 4637 model
// (GameSvr/Services/NativeUpdateClothesTransaction.cs), locked against the original
// dispatch sub_6FAC50 @0x006FAC50 and core sub_6A3928 @0x006A3928
// (unpacked M2Server.exe, image base 0x00400000).

try
{
    VerifyConstants();
    VerifyWireValues();
    VerifyResultLadderOrder();
    VerifyLevelCap();
    VerifyRandomBoundary();
    VerifySuccessMutations();
    VerifyDispatchContract();

    Console.WriteLine(
        "PASS NativeUpdateClothesCompatCheck ident=4637 rng=Random(800)<100(~12.5%) " +
        "ladder=-99/-1/-2/-3/-4/-5/-6 cap=level3 " +
        "apply=+0x2A/+0x2B+cat{2C|2D|2E} dispatch=SendDefMessage(wParam=result) dormant=true");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"NativeUpdateClothesCompatCheck FAIL: {ex.Message}");
    return 1;
}

static void Assert(bool cond, string msg)
{
    if (!cond)
        throw new Exception(msg);
}

// Independent replica of the bounded RandSeed draw sub_403B4C, used to cross-check the model.
static int ExpectedRoll(uint seed)
{
    uint next = unchecked(seed * 0x08088405u + 1u);
    return unchecked((int)(uint)(((ulong)(uint)NativeUpdateClothesTransaction.RandomBound * next) >> 32));
}

static uint FindSeedForRoll(int target)
{
    for (uint s = 1; s <= 8_000_000u; s++)
        if (ExpectedRoll(s) == target)
            return s;
    throw new Exception($"no seed produced roll {target}");
}

static NativeUpdateClothesContext ValidContext(int level, NativeUpdateClothesCategory category) => new()
{
    HasPlayer = true,
    TargetFound = true,
    TargetInConfig = true,
    TargetLevel = level,
    MaterialsPresent = true,
    MaterialsSufficient = true,
    Category = category,
};

static NativeUpdateClothesResult ResultOf(NativeUpdateClothesContext ctx, uint seed) =>
    NativeUpdateClothesTransaction.Evaluate(ctx, seed).Result;

static void VerifyConstants()
{
    Assert(NativeUpdateClothesTransaction.Ident == 4637, "ident");
    Assert(NativeUpdateClothesTransaction.RandomBound == 800, "random bound");
    Assert(NativeUpdateClothesTransaction.SuccessThreshold == 100, "success threshold");
    Assert(NativeUpdateClothesTransaction.MaxLevel == 3, "max level");
    Assert(NativeUpdateClothesTransaction.ItemLevelOffset == 0x49, "item level offset");
    Assert(NativeUpdateClothesTransaction.ItemStatAOffset == 0x2A, "stat A offset");
    Assert(NativeUpdateClothesTransaction.ItemStatBOffset == 0x2B, "stat B offset");
    Assert(NativeUpdateClothesTransaction.ItemStatCOffset == 0x2C, "stat C offset");
    Assert(NativeUpdateClothesTransaction.ItemStatDOffset == 0x2D, "stat D offset");
    Assert(NativeUpdateClothesTransaction.ItemStatEOffset == 0x2E, "stat E offset");
    Assert(NativeUpdateClothesTransaction.VtblSendDefMessage == 0x250, "vtbl send def message");
    Assert(NativeUpdateClothesTransaction.VtblSendDelItem == 0x24C, "vtbl send del item");
    Assert(NativeUpdateClothesTransaction.VtblSendUpdateItem == 0x260, "vtbl send update item");
    Assert(NativeUpdateClothesTransaction.NotifyAddStatIdent == 0x38FF, "notify add stat ident");
}

static void VerifyWireValues()
{
    Assert((int)NativeUpdateClothesResult.Success == 0, "wire 0");
    Assert((int)NativeUpdateClothesResult.TargetNotFound == -1, "wire -1");
    Assert((int)NativeUpdateClothesResult.TargetNotUpgradable == -2, "wire -2");
    Assert((int)NativeUpdateClothesResult.TargetMaxLevel == -3, "wire -3");
    Assert((int)NativeUpdateClothesResult.MaterialsMissing == -4, "wire -4");
    Assert((int)NativeUpdateClothesResult.MaterialsInsufficient == -5, "wire -5");
    Assert((int)NativeUpdateClothesResult.RandomFail == -6, "wire -6");
    Assert((int)NativeUpdateClothesResult.NoPlayer == -99, "wire -99");
}

// Each scenario also fails every *later* guard, proving the guards fire in the original order.
static void VerifyResultLadderOrder()
{
    Assert(ResultOf(new NativeUpdateClothesContext
    {
        HasPlayer = false, TargetFound = false, TargetInConfig = false, TargetLevel = 99,
        MaterialsPresent = false, MaterialsSufficient = false,
    }, 1u) == NativeUpdateClothesResult.NoPlayer, "order: no player");

    Assert(ResultOf(new NativeUpdateClothesContext
    {
        HasPlayer = true, TargetFound = false, TargetInConfig = false, TargetLevel = 99,
        MaterialsPresent = false, MaterialsSufficient = false,
    }, 1u) == NativeUpdateClothesResult.TargetNotFound, "order: target not found");

    Assert(ResultOf(new NativeUpdateClothesContext
    {
        HasPlayer = true, TargetFound = true, TargetInConfig = false, TargetLevel = 99,
        MaterialsPresent = false, MaterialsSufficient = false,
    }, 1u) == NativeUpdateClothesResult.TargetNotUpgradable, "order: not upgradable");

    Assert(ResultOf(new NativeUpdateClothesContext
    {
        HasPlayer = true, TargetFound = true, TargetInConfig = true, TargetLevel = 3,
        MaterialsPresent = false, MaterialsSufficient = false,
    }, 1u) == NativeUpdateClothesResult.TargetMaxLevel, "order: max level");

    Assert(ResultOf(new NativeUpdateClothesContext
    {
        HasPlayer = true, TargetFound = true, TargetInConfig = true, TargetLevel = 0,
        MaterialsPresent = false, MaterialsSufficient = false,
    }, 1u) == NativeUpdateClothesResult.MaterialsMissing, "order: materials missing");

    Assert(ResultOf(new NativeUpdateClothesContext
    {
        HasPlayer = true, TargetFound = true, TargetInConfig = true, TargetLevel = 0,
        MaterialsPresent = true, MaterialsSufficient = false,
    }, 1u) == NativeUpdateClothesResult.MaterialsInsufficient, "order: materials insufficient");
}

static void VerifyLevelCap()
{
    uint failSeed = FindSeedForRoll(100); // roll 100 -> reaches RNG and fails, proving guards passed
    for (int level = 0; level <= 2; level++)
        Assert(ResultOf(ValidContext(level, NativeUpdateClothesCategory.One), failSeed)
            == NativeUpdateClothesResult.RandomFail, $"level {level} must pass guards");

    for (int level = 3; level <= 6; level++)
        Assert(ResultOf(ValidContext(level, NativeUpdateClothesCategory.One), 1u)
            == NativeUpdateClothesResult.TargetMaxLevel, $"level {level} must cap");
}

static void VerifyRandomBoundary()
{
    var ctx = ValidContext(0, NativeUpdateClothesCategory.One);
    long total = 0;
    long success = 0;
    bool found99 = false;
    bool found100 = false;

    for (uint s = 1; s <= 500_000u; s++)
    {
        int expected = ExpectedRoll(s);
        var outcome = NativeUpdateClothesTransaction.Evaluate(ctx, s);
        bool modelSuccess = outcome.Result == NativeUpdateClothesResult.Success;

        Assert(modelSuccess == (expected < NativeUpdateClothesTransaction.SuccessThreshold),
            $"rng disagreement seed={s} roll={expected} success={modelSuccess}");

        if (modelSuccess)
        {
            Assert(outcome.NewTargetLevel == 1, "success level increment");
            success++;
        }
        else
        {
            Assert(outcome.Result == NativeUpdateClothesResult.RandomFail, "non-success must be RandomFail");
            Assert(outcome.NewTargetLevel == 0, "fail keeps level");
        }

        if (!found99 && expected == 99) found99 = true;
        if (!found100 && expected == 100) found100 = true;
        total++;
    }

    Assert(found99, "no seed produced boundary roll 99");
    Assert(found100, "no seed produced boundary roll 100");
    Assert(ResultOf(ctx, FindSeedForRoll(99)) == NativeUpdateClothesResult.Success, "roll 99 must succeed");
    Assert(ResultOf(ctx, FindSeedForRoll(100)) == NativeUpdateClothesResult.RandomFail, "roll 100 must fail");

    double rate = (double)success / total;
    Assert(rate > 0.11 && rate < 0.14, $"success rate {rate:F4} not ~0.125");
}

static void VerifySuccessMutations()
{
    uint successSeed = FindSeedForRoll(0); // roll 0 < 100 -> success

    var c1 = NativeUpdateClothesTransaction.Evaluate(ValidContext(1, NativeUpdateClothesCategory.One), successSeed);
    Assert(c1.Result == NativeUpdateClothesResult.Success && c1.NewTargetLevel == 2, "cat1 success/level");
    Assert(c1.Delta2A == 1 && c1.Delta2B == 1 && c1.Delta2C == 1 && c1.Delta2D == 0 && c1.Delta2E == 0, "cat1 deltas");

    var c2 = NativeUpdateClothesTransaction.Evaluate(ValidContext(0, NativeUpdateClothesCategory.Two), successSeed);
    Assert(c2.Delta2A == 1 && c2.Delta2B == 1 && c2.Delta2C == 0 && c2.Delta2D == 1 && c2.Delta2E == 0, "cat2 deltas");

    var c3 = NativeUpdateClothesTransaction.Evaluate(ValidContext(0, NativeUpdateClothesCategory.Three), successSeed);
    Assert(c3.Delta2A == 1 && c3.Delta2B == 1 && c3.Delta2C == 0 && c3.Delta2D == 0 && c3.Delta2E == 1, "cat3 deltas");

    var c0 = NativeUpdateClothesTransaction.Evaluate(ValidContext(0, NativeUpdateClothesCategory.None), successSeed);
    Assert(c0.Delta2A == 1 && c0.Delta2B == 1 && c0.Delta2C == 0 && c0.Delta2D == 0 && c0.Delta2E == 0, "cat-none deltas");

    var reject = NativeUpdateClothesTransaction.Evaluate(ValidContext(0, NativeUpdateClothesCategory.One), FindSeedForRoll(100));
    Assert(reject.Result == NativeUpdateClothesResult.RandomFail && reject.NewTargetLevel == 0, "reject unchanged level");
    Assert(reject.Delta2A == 0 && reject.Delta2B == 0 && reject.Delta2C == 0 && reject.Delta2D == 0 && reject.Delta2E == 0,
        "reject applies no deltas");
}

static void VerifyDispatchContract()
{
    // dispatch sub_6FAC50 forwards the raw core result verbatim into SendDefMessage wParam.
    foreach (NativeUpdateClothesResult r in Enum.GetValues<NativeUpdateClothesResult>())
    {
        var outcome = new NativeUpdateClothesOutcome { Result = r };
        Assert(outcome.DispatchWParam == (int)r, $"dispatch wParam for {r}");
    }
}
