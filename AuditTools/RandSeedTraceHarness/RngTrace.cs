namespace RandSeedTraceHarness;

// RandSeed cutover (task #78) — step 1: the fixed-seed per-call TRACE record.
// The Owner triple (Domain, CsCluster, NativeOwner) is the 630<->411 pairing key,
// sourced from randseed_consumer_map_{source,native_owner}_half_20260801.md.

public enum RngDomain { D1_Main, D2_Robots, D3_Pas, Startup }

public enum RngApi { Random, Advance /* native Random(0) */, RandomRange, NextDouble }

/// <summary>One RNG draw. Under a fixed start seed, (SeedBefore, Api, Bound) fully
/// determines (Result, SeedAfter), so the trace is reproducible and comparable
/// trace-for-trace — the acceptance property the atomic cutover is validated on.</summary>
public readonly record struct RngTraceRecord(
    long Ordinal,        // 0,1,2,... within the traced window
    RngDomain Domain,    // dispatch domain (source-half map §2)
    string CsCluster,    // C# owner: "Monsters/FireKingMonster.Run" etc.
    string NativeOwner,  // native sub_* or family: "sub_666A98" / "TGoldIngot"
    RngApi Api,
    long Bound,          // Random: the bound; RandomRange: (hi<<32)|lo packed; else 0
    long Result,
    uint SeedBefore,
    uint SeedAfter)
{
    public string ToLine() =>
        $"{Ordinal}\t{Domain}\t{CsCluster}\t{NativeOwner}\t{Api}\t{Bound}\t{Result}\t{SeedBefore:X8}\t{SeedAfter:X8}";
}
