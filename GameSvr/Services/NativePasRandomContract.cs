using SystemModule;

namespace GameSvr
{
    // Dormant model of the PAS interpreter random builtins' exact owner-side contract, mapping the
    // Delphi script primitives onto the global LCG (DelphiRandom). Live PasInterpreter.cs uses
    // Random.Shared (off the RandSeed owner) AND mishandles randomrange(a,b) for a > b. Correcting
    // both is part of the atomic RandSeed cutover (audit step 7, PAS half); this pins the contract.
    //
    //   random(n)        -> Random(n)                 (sub_403B4C @0x00403B4C)
    //   random           -> Double in [0,1)           (Delphi Random no-arg)
    //   randomrange(a,b) -> RandomRange(a,b)           (sub_43CD60 @0x0043CD60):
    //                          a <= b ? a + Random(b - a) : b + Random(a - b)
    //
    // The current C# randomrange does a + Random(b - a) unconditionally, which is wrong (throws or
    // draws a negative bound) when a > b; the original swaps to keep the low bound.
    public static class NativePasRandomContract
    {
        /// <summary>PAS random(n) — Delphi Random(n).</summary>
        public static int Random(int bound) => DelphiRandom.Random(bound);

        /// <summary>PAS random (no arg) — Delphi Random Double in [0,1).</summary>
        public static double RandomFloat() => DelphiRandom.NextDouble();

        /// <summary>PAS randomrange(a,b) — Delphi RandomRange (sub_43CD60), low-bound preserving.</summary>
        public static int RandomRange(int a, int b) =>
            a <= b ? a + DelphiRandom.Random(b - a) : b + DelphiRandom.Random(a - b);
    }
}
