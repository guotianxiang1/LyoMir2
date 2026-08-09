using RandSeedTraceHarness;

// RandSeed cutover (task #78) — step 1: fixed-seed per-call TRACE HARNESS = the
// acceptance instrument. Validates the deterministic-anchor golden: determinism,
// contiguity, the unbroken single-owner seed chain (the native single-thread
// invariant the cutover must reproduce), advance semantics, exactly-30 Yuanbao,
// and an INDEPENDENT LCG oracle (re-derives every draw from sub_403B4C).
//
// SystemModule-only; touches no GameSvr, adds no DelphiRandom ref to GameSvr
// (DelphiRandomDormantCompatCheck stays green). No live behavior change.

try
{
    var golden = GoldenAnchors.BuildAnchorTrace();
    var replay = GoldenAnchors.BuildAnchorTrace();

    AssertTraceEqual(golden.Log, replay.Log);   // determinism: same seed -> identical trace
    AssertContiguous(golden.Log);               // no missing / extra / reordered draw
    AssertUnbrokenChain(golden.Log);            // SeedAfter[i] == SeedBefore[i+1] (single owner)
    AssertAdvanceSemantics(golden.Log);         // Random(0) -> 0 and advances the seed
    AssertYuanbao30(golden.Log);                // exactly 30 sequential Yuanbao draws
    AssertLcgConsistency(golden.Log);           // independent oracle vs sub_403B4C

    foreach (var r in golden.Log)
        Console.WriteLine(r.ToLine());

    Console.WriteLine(
        $"PASS RandSeedTraceHarness anchors={golden.Log.Count} determinism=ok " +
        "chain=unbroken advance=step+0 yuanbao=30 lcg=independent-match dormant=true (EXIT 0)");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"RandSeedTraceHarness FAIL: {ex.Message}");
    return 1;
}

static void Assert(bool cond, string msg)
{
    if (!cond) throw new Exception(msg);
}

static void AssertTraceEqual(IReadOnlyList<RngTraceRecord> a, IReadOnlyList<RngTraceRecord> b)
{
    Assert(a.Count == b.Count, $"determinism: count {a.Count} != {b.Count}");
    for (int i = 0; i < a.Count; i++)
        Assert(a[i] == b[i], $"determinism: record {i} differs");
}

static void AssertContiguous(IReadOnlyList<RngTraceRecord> g)
{
    for (int i = 0; i < g.Count; i++)
        Assert(g[i].Ordinal == i, $"contiguity: ordinal {g[i].Ordinal} at index {i}");
}

static void AssertUnbrokenChain(IReadOnlyList<RngTraceRecord> g)
{
    for (int i = 0; i < g.Count; i++)
        Assert(g[i].SeedBefore != g[i].SeedAfter, $"ord {g[i].Ordinal}: draw did not advance seed");
    for (int i = 0; i + 1 < g.Count; i++)
        Assert(g[i].SeedAfter == g[i + 1].SeedBefore,
            $"chain break at ord {g[i].Ordinal}->{g[i + 1].Ordinal}: {g[i].SeedAfter:X8} != {g[i + 1].SeedBefore:X8}");
}

static void AssertAdvanceSemantics(IReadOnlyList<RngTraceRecord> g)
{
    foreach (var r in g)
        if (r.Api == RngApi.Advance)
            Assert(r.Result == 0, $"ord {r.Ordinal}: Random(0) returned {r.Result} (expected 0)");
}

static void AssertYuanbao30(IReadOnlyList<RngTraceRecord> g)
{
    var yb = g.Where(r => r.CsCluster.StartsWith("Yuanbao/")).ToList();
    Assert(yb.Count == 30, $"yuanbao: {yb.Count} draws (expected 30)");
    for (int i = 0; i + 1 < yb.Count; i++)
        Assert(yb[i + 1].Ordinal == yb[i].Ordinal + 1, "yuanbao: draws not contiguous");
    foreach (var r in yb)
    {
        Assert(r.Api == RngApi.RandomRange, $"ord {r.Ordinal}: yuanbao not RandomRange");
        Assert(r.Result >= 0x41 && r.Result < 0x5B, $"ord {r.Ordinal}: yuanbao char {r.Result} out of ['A','[')");
    }
}

// Independent replica of sub_403B4C advancing a local seed — re-derives every
// recorded draw so the tracer's outputs are not self-validated.
static void AssertLcgConsistency(IReadOnlyList<RngTraceRecord> g)
{
    foreach (var r in g)
    {
        uint next = unchecked(r.SeedBefore * 0x08088405u + 1u);
        switch (r.Api)
        {
            case RngApi.Random:
            case RngApi.Advance:
            {
                long expected = unchecked((int)(uint)(((ulong)(uint)r.Bound * next) >> 32));
                Assert(r.SeedAfter == next, $"ord {r.Ordinal}: lcg seed chain");
                Assert(r.Result == expected, $"ord {r.Ordinal}: lcg result {r.Result} != {expected}");
                break;
            }
            case RngApi.RandomRange:
            {
                int lo = (int)(r.Bound & 0xFFFFFFFF);
                int hi = (int)(r.Bound >> 32);
                int a = Math.Min(lo, hi), b = Math.Max(lo, hi);
                long inner = unchecked((int)(uint)(((ulong)(uint)(b - a) * next) >> 32));
                Assert(r.SeedAfter == next, $"ord {r.Ordinal}: lcg rr seed chain");
                Assert(r.Result == a + inner, $"ord {r.Ordinal}: lcg rr result");
                break;
            }
            case RngApi.NextDouble:
            {
                Assert(r.SeedAfter == next, $"ord {r.Ordinal}: lcg nd seed chain");
                Assert(r.Result == next, $"ord {r.Ordinal}: lcg nd result != seed");
                break;
            }
        }
    }
}
