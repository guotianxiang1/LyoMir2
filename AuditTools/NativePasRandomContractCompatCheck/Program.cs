using GameSvr;
using SystemModule;

// Contract check for NativePasRandomContract, locked against sub_403B4C (Random) and
// sub_43CD60 (RandomRange).

try
{
    VerifyRandom();
    VerifyRandomRangeAscending();
    VerifyRandomRangeSwapped();
    VerifyRandomFloat();
    VerifyDeterminism();

    Console.WriteLine(
        "PASS NativePasRandomContractCompatCheck random(n)=sub_403B4C " +
        "randomrange=sub_43CD60(low-bound-preserving) randomfloat=[0,1) owner=DelphiRandom dormant=true");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"NativePasRandomContractCompatCheck FAIL: {ex.Message}");
    return 1;
}

static void Assert(bool cond, string msg)
{
    if (!cond)
        throw new Exception(msg);
}

static int Lcg(ref uint seed, int bound)
{
    seed = unchecked(seed * 0x08088405u + 1u);
    return unchecked((int)(uint)(((ulong)(uint)bound * seed) >> 32));
}

static void VerifyRandom()
{
    int[] bounds = { 1, 2, 10, 100, 1000, 0 };
    foreach (int bound in bounds)
        for (uint s = 1; s <= 30000u; s += 7)
        {
            uint replica = s;
            int expected = Lcg(ref replica, bound);
            DelphiRandom.Seed = s;
            Assert(NativePasRandomContract.Random(bound) == expected, $"random({bound}) seed {s}");
        }
}

static void VerifyRandomRangeAscending()
{
    (int a, int b)[] ranges = { (5, 10), (0, 100), (100, 100), (-20, 20) };
    foreach (var (a, b) in ranges)
        for (uint s = 1; s <= 20000u; s += 11)
        {
            uint replica = s;
            int expected = a + Lcg(ref replica, b - a);
            DelphiRandom.Seed = s;
            int actual = NativePasRandomContract.RandomRange(a, b);
            Assert(actual == expected, $"randomrange({a},{b}) seed {s}: {actual} != {expected}");
            if (b > a)
                Assert(actual >= a && actual < b, $"range [{a},{b}) seed {s} got {actual}");
        }
}

static void VerifyRandomRangeSwapped()
{
    // a > b must swap to keep the low bound (original sub_43CD60), unlike the current live code.
    (int a, int b)[] ranges = { (10, 5), (100, 0), (20, -20) };
    foreach (var (a, b) in ranges)
        for (uint s = 1; s <= 20000u; s += 11)
        {
            uint replica = s;
            int expected = b + Lcg(ref replica, a - b);
            DelphiRandom.Seed = s;
            int actual = NativePasRandomContract.RandomRange(a, b);
            Assert(actual == expected, $"randomrange swapped ({a},{b}) seed {s}: {actual} != {expected}");
            Assert(actual >= b && actual < a, $"swapped range [{b},{a}) seed {s} got {actual}");
        }
}

static void VerifyRandomFloat()
{
    for (uint s = 1; s <= 30000u; s += 7)
    {
        uint next = unchecked(s * 0x08088405u + 1u);
        double expected = next * (1.0 / 4294967296.0);
        DelphiRandom.Seed = s;
        double actual = NativePasRandomContract.RandomFloat();
        Assert(actual == expected, $"randomfloat seed {s}: {actual} != {expected}");
        Assert(actual >= 0.0 && actual < 1.0, $"randomfloat range seed {s} got {actual}");
    }
}

static void VerifyDeterminism()
{
    const uint seed = 55555u;
    DelphiRandom.Seed = seed;
    int a = NativePasRandomContract.Random(1000);
    DelphiRandom.Seed = seed;
    int b = NativePasRandomContract.Random(1000);
    Assert(a == b, "determinism random");

    DelphiRandom.Seed = seed;
    int ra = NativePasRandomContract.RandomRange(10, 5);
    DelphiRandom.Seed = seed;
    int rb = NativePasRandomContract.RandomRange(10, 5);
    Assert(ra == rb, "determinism randomrange");
}
