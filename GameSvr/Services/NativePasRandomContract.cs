using SystemModule;

namespace GameSvr
{
    // The PAS interpreter random builtins, mapped onto the global LCG (DelphiRandom).
    //
    //   random(n)        -> Random(n)                 (sub_403B4C @0x00403B4C)
    //   random           -> Double in [0,1)           (sub_403B68, seed * 2^-32 from [0x007A206C])
    //   randomrange(a,b) -> RandomRange(a,b)           (sub_43CD60 @0x0043CD60):
    //
    //   0043CD6F  3B 45 F8  cmp eax,[ebp-8]      ; eax = a, [ebp-8] = b
    //   0043CD72  7E 13     jle 0x0043CD87
    //   0043CD74            eax = a - b; call 0x00403B4C; add eax,b   ; a > b: b + Random(a-b)
    //   0043CD87            eax = b - a; call 0x00403B4C; add eax,a   ; a <= b: a + Random(b-a)
    //
    // The swap matters: without it a > b draws a negative bound instead of keeping the low bound.
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
