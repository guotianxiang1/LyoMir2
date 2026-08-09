namespace RandSeedTraceHarness;

// Deterministic-anchor golden generator. Emits the reproducible backbone of a
// canonical tick from the 423<->357 map — the draws whose values are fully
// determined by DelphiRandom + a fixed start seed (the dormant-modelled points:
// 4466 weighted, the four Random(0) advance shims, PAS random/randomrange, and
// Yuanbao-30). These are the golden anchors the atomic cutover must reproduce.
//
// The 4637 Randomize+Random(800) reset (sub_6A3928) and startup Randomize
// (sub_4034AC) are RESEED points whose value depends on the real perf-counter;
// they are validated structurally at cutover time, not pinned here (see staging
// design §7 nondeterminism).
public static class GoldenAnchors
{
    // Native dormant owner starts at image value 0 (stage1). Fixed for reproducibility.
    public const uint StartSeed = 0u;

    public static RngTracer BuildAnchorTrace()
    {
        var t = new RngTracer(StartSeed);

        // D1 special: 4466 强化 weighted output selection (sub_60F74C / TStrengthenEquipMgr).
        t.Random(1000, RngDomain.D1_Main, "Items/Strengthen.4466", "sub_60F74C");

        // D1 Random(0) advance shims (source-half §5).
        t.Advance(RngDomain.D1_Main, "Actors/NativeState26Effects.cs:339", "sub_403B4C:zero");
        t.Advance(RngDomain.D1_Main, "Players/NativeMagicProducers.cs:371", "sub_403B4C:zero");
        t.Advance(RngDomain.D1_Main, "Players/NativeMagicTower.Prize.cs:326", "sub_403B4C:zero");
        t.Advance(RngDomain.D1_Main, "Players/NativeMagicTower.Check.cs:101", "sub_403B4C:zero");

        // D3 / D1-sync PAS random/randomrange/no-arg (NativePasRandomContract).
        t.Random(100, RngDomain.D3_Pas, "PAS/random(n)", "TPsNpc");
        t.RandomRange(1, 6, RngDomain.D3_Pas, "PAS/randomrange(a,b)", "sub_43CD60");
        t.NextDouble(RngDomain.D3_Pas, "PAS/random(no-arg)", "TPsNpc");

        // D1 Yuanbao-30 context id: 30 x ('A' + Random(26)) = RandomRange('A','[').
        for (int i = 0; i < 30; i++)
            t.RandomRange(0x41, 0x5B, RngDomain.D1_Main, $"Yuanbao/ctxid[{i}]", "sub_711F08->sub_43CD60");

        return t;
    }
}
