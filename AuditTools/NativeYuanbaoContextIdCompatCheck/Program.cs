using GameSvr;
using SystemModule;

// Contract check for the dormant NativeYuanbaoContextId model, locked against the original
// sub_711F08 @0x00711F08 (30 x RandomRange('A','[') = 65 + Random(26)).

try
{
    VerifyConstants();
    VerifyMatchesLcgReplica();
    VerifyLetterRangeAndLength();
    VerifyConsumesThirtyDraws();
    VerifyDeterminism();

    Console.WriteLine(
        "PASS NativeYuanbaoContextIdCompatCheck len=30 char='A'+Random(26) " +
        "draws=30 owner=DelphiRandom dormant=true");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"NativeYuanbaoContextIdCompatCheck FAIL: {ex.Message}");
    return 1;
}

static void Assert(bool cond, string msg)
{
    if (!cond)
        throw new Exception(msg);
}

// Independent replica: 30 x (65 + high32(26 * (seed = seed*0x08088405+1))).
static byte[] Replica(uint seed, out uint finalSeed)
{
    var bytes = new byte[30];
    for (int i = 0; i < 30; i++)
    {
        seed = unchecked(seed * 0x08088405u + 1u);
        int r = unchecked((int)(uint)(((ulong)26u * seed) >> 32));
        bytes[i] = (byte)(65 + r);
    }
    finalSeed = seed;
    return bytes;
}

static void VerifyConstants()
{
    Assert(NativeYuanbaoContextId.Length == 30, "length");
    Assert(NativeYuanbaoContextId.FirstLetter == 0x41, "first letter A");
    Assert(NativeYuanbaoContextId.PastLastLetter == 0x5B, "past last '['");
    Assert(NativeYuanbaoContextId.LetterCount == 26, "letter count");
}

static void VerifyMatchesLcgReplica()
{
    for (uint s = 1; s <= 200000u; s += 7)
    {
        var expected = Replica(s, out _);
        var actual = NativeYuanbaoContextId.Generate(s);
        Assert(actual.Length == expected.Length, $"length seed {s}");
        for (int i = 0; i < expected.Length; i++)
            Assert(actual[i] == expected[i], $"byte {i} seed {s}: {actual[i]} != {expected[i]}");
    }
}

static void VerifyLetterRangeAndLength()
{
    for (uint s = 1; s <= 50000u; s += 3)
    {
        var id = NativeYuanbaoContextId.Generate(s);
        Assert(id.Length == 30, $"length {s}");
        foreach (var b in id)
            Assert(b >= (byte)'A' && b <= (byte)'Z', $"letter range seed {s} byte {b}");
    }
}

static void VerifyConsumesThirtyDraws()
{
    for (uint s = 1; s <= 20000u; s += 11)
    {
        Replica(s, out uint expectedFinal);
        NativeYuanbaoContextId.Generate(s);
        Assert(DelphiRandom.Seed == expectedFinal,
            $"must advance seed exactly 30 draws (seed {s}): {DelphiRandom.Seed} != {expectedFinal}");
    }
}

static void VerifyDeterminism()
{
    const uint seed = 987654u;
    var a = NativeYuanbaoContextId.Generate(seed);
    var b = NativeYuanbaoContextId.Generate(seed);
    for (int i = 0; i < a.Length; i++)
        Assert(a[i] == b[i], $"determinism at {i}");
}
