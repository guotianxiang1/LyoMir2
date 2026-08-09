using SystemModule;

namespace RandSeedTraceHarness;

// The fixed-seed tracer. Wraps the proven Delphi LCG (SystemModule.DelphiRandom,
// = sub_403B4C) and records SeedBefore/SeedAfter around every draw. This is the
// exact shim the LIVE facade (M2Share.RandomNumber) will delegate to at cutover
// time via a default-OFF RngTraceSink (design in the staging doc); here it is
// driven directly by the deterministic-anchor golden generator.
//
// SystemModule-only by design: adds ZERO DelphiRandom references to GameSvr, so
// DelphiRandomDormantCompatCheck stays green and the in-flight edit batch is
// untouched.
public sealed class RngTracer
{
    private long _ordinal;
    private readonly List<RngTraceRecord> _log = new();
    public IReadOnlyList<RngTraceRecord> Log => _log;

    public RngTracer(uint startSeed) => DelphiRandom.Seed = startSeed;

    public int Random(int bound, RngDomain d, string cs, string owner)
    {
        uint before = DelphiRandom.Seed;
        int result = DelphiRandom.Random(bound);
        _log.Add(new(_ordinal++, d, cs, owner, RngApi.Random, bound, result, before, DelphiRandom.Seed));
        return result;
    }

    // Native Random(0): advances the seed once and returns 0.
    public int Advance(RngDomain d, string cs, string owner)
    {
        uint before = DelphiRandom.Seed;
        int result = DelphiRandom.Random(0);
        _log.Add(new(_ordinal++, d, cs, owner, RngApi.Advance, 0, result, before, DelphiRandom.Seed));
        return result;
    }

    // sub_43CD60: for lo<=hi -> lo + Random(hi-lo); a>b swap keeps the low bound
    // (NativePasRandomContract). Yuanbao id char = RandomRange('A','[') = 'A' + Random(26).
    public int RandomRange(int lo, int hi, RngDomain d, string cs, string owner)
    {
        int a = lo, b = hi;
        if (a > b) (a, b) = (b, a);
        uint before = DelphiRandom.Seed;
        int result = a + DelphiRandom.Random(b - a);
        long packed = ((long)(uint)hi << 32) | (uint)lo;
        _log.Add(new(_ordinal++, d, cs, owner, RngApi.RandomRange, packed, result, before, DelphiRandom.Seed));
        return result;
    }

    // Delphi Random floating path: nextSeed / 2^32. Result stored as the exact
    // integer nextSeed (== SeedAfter) so the LCG oracle can re-verify it.
    public double NextDouble(RngDomain d, string cs, string owner)
    {
        uint before = DelphiRandom.Seed;
        double result = DelphiRandom.NextDouble();
        _log.Add(new(_ordinal++, d, cs, owner, RngApi.NextDouble, 0,
            (long)(result * 4294967296.0), before, DelphiRandom.Seed));
        return result;
    }
}
