using SystemModule;

// Contract check for DelphiRandomNumberFacade (RandSeed cutover step 4, dormant): the facade
// overloads must map exactly onto the original Delphi bounded draw sub_403B4C.

try
{
    VerifyBoundMatchesLcg();
    VerifyZeroBound();
    VerifyNegativeBound();
    VerifyMinMaxHalfOpen();
    VerifyGetRandomInclusive();
    VerifyAdvance();
    VerifyDeterminism();

    Console.WriteLine(
        "PASS DelphiRandomNumberFacadeCompatCheck bound=sub_403B4C zero=advance+0 " +
        "negative=uint minmax=[min,max) getrandom=[min,max] advance=step+0 dormant=true");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"DelphiRandomNumberFacadeCompatCheck FAIL: {ex.Message}");
    return 1;
}

static void Assert(bool cond, string msg)
{
    if (!cond)
        throw new Exception(msg);
}

// Independent replica of sub_403B4C advancing a local seed.
static int Lcg(ref uint seed, int bound)
{
    seed = unchecked(seed * 0x08088405u + 1u);
    return unchecked((int)(uint)(((ulong)(uint)bound * seed) >> 32));
}

static void VerifyBoundMatchesLcg()
{
    int[] bounds = { 1, 2, 5, 100, 800, 1000, 65535, 0x7FFFFFFF };
    foreach (int bound in bounds)
    {
        for (uint s = 1; s <= 20000u; s += 7)
        {
            uint replica = s;
            int expected = Lcg(ref replica, bound);
            DelphiRandom.Seed = s;
            int actual = DelphiRandomNumberFacade.Random(bound);
            Assert(actual == expected, $"bound={bound} seed={s} expected={expected} actual={actual}");
            Assert(DelphiRandom.Seed == replica, $"seed advance mismatch bound={bound} seed={s}");
        }
    }
}

static void VerifyZeroBound()
{
    for (uint s = 1; s <= 5000u; s++)
    {
        uint replica = s;
        int expected = Lcg(ref replica, 0);
        DelphiRandom.Seed = s;
        int actual = DelphiRandomNumberFacade.Random(0);
        Assert(actual == 0, $"zero bound must return 0 (seed {s})");
        Assert(expected == 0, "replica zero bound 0");
        Assert(DelphiRandom.Seed == replica, $"zero bound must still advance seed (seed {s})");
    }
}

static void VerifyNegativeBound()
{
    int[] negatives = { -1, -2, -100, -32768, int.MinValue };
    foreach (int bound in negatives)
    {
        for (uint s = 1; s <= 10000u; s += 13)
        {
            uint replica = s;
            int expected = Lcg(ref replica, bound);
            DelphiRandom.Seed = s;
            int actual = DelphiRandomNumberFacade.Random(bound);
            Assert(actual == expected, $"negative bound={bound} seed={s} expected={expected} actual={actual}");
        }
    }
}

static void VerifyMinMaxHalfOpen()
{
    (int min, int max)[] ranges = { (5, 10), (0, 800), (100, 100), (-50, 50), (1000, 2000) };
    foreach (var (min, max) in ranges)
    {
        for (uint s = 1; s <= 10000u; s += 11)
        {
            uint replica = s;
            int expected = min + Lcg(ref replica, max - min);
            DelphiRandom.Seed = s;
            int actual = DelphiRandomNumberFacade.Random(min, max);
            Assert(actual == expected, $"min={min} max={max} seed={s} expected={expected} actual={actual}");
            // [min, max) — result stays below max when the range is non-empty.
            if (max > min)
                Assert(actual >= min && actual < max, $"range bound [{min},{max}) seed={s} got {actual}");
        }
    }
}

static void VerifyGetRandomInclusive()
{
    (int min, int max)[] ranges = { (1, 6), (0, 0), (10, 20), (1, 1) };
    foreach (var (min, max) in ranges)
    {
        for (uint s = 1; s <= 10000u; s += 11)
        {
            uint replica = s;
            int expected = min + Lcg(ref replica, max - min + 1);
            DelphiRandom.Seed = s;
            int actual = DelphiRandomNumberFacade.GetRandomNumber(min, max);
            Assert(actual == expected, $"getrandom min={min} max={max} seed={s} expected={expected} actual={actual}");
            // inclusive of max.
            Assert(actual >= min && actual <= max, $"getrandom range [{min},{max}] seed={s} got {actual}");
        }
    }
}

static void VerifyAdvance()
{
    for (uint s = 1; s <= 5000u; s++)
    {
        uint replica = s;
        Lcg(ref replica, 0);
        DelphiRandom.Seed = s;
        int r = DelphiRandomNumberFacade.Advance();
        Assert(r == 0, $"advance returns 0 (seed {s})");
        Assert(DelphiRandom.Seed == replica, $"advance steps seed once (seed {s})");
    }
}

static void VerifyDeterminism()
{
    const uint start = 123456u;
    var first = new int[64];
    DelphiRandom.Seed = start;
    for (int i = 0; i < first.Length; i++)
        first[i] = DelphiRandomNumberFacade.Random(1000);

    DelphiRandom.Seed = start;
    for (int i = 0; i < first.Length; i++)
        Assert(DelphiRandomNumberFacade.Random(1000) == first[i], $"determinism at {i}");
}
