namespace SystemModule
{
    // Facade mapping the RandomNumber API surface onto the Delphi LCG (DelphiRandom).
    // POIS-26: RandomNumber now delegates here, so this is the live gameplay path and no
    // longer a dormant model. Cutting over changes every probability consumer's values at
    // once, which is the point - the original tunes its rates on this generator's bias.
    //
    // Original bounded draw sub_403B4C @0x00403B4C:
    //   nextSeed = seed * 0x08088405 + 1;  result = high32((uint32)bound * nextSeed)
    //   Random(0) advances the seed once and returns 0; a negative bound participates as its
    //   UInt32 bit pattern. DelphiRandom already implements this exactly.
    public static class DelphiRandomNumberFacade
    {
        /// <summary>Delphi Random(bound). Zero advances once and returns 0; negative uses the UInt32 pattern.</summary>
        public static int Random(int bound) => DelphiRandom.Random(bound);

        /// <summary>
        /// RandomNumber.Random(min, max) convenience — half-open [min, max): min + Random(max - min).
        /// Delphi has no two-arg Random; this is the C# facade's own contract, expressed over the LCG.
        /// </summary>
        public static int Random(int minValue, int maxValue) =>
            minValue + DelphiRandom.Random(maxValue - minValue);

        /// <summary>
        /// RandomNumber.GetRandomNumber(min, max) — inclusive of max: min + Random(max - min + 1)
        /// (mirrors the live facade's random.Next(min, max + 1)).
        /// </summary>
        public static int GetRandomNumber(int minValue, int maxValue) =>
            minValue + DelphiRandom.Random(maxValue - minValue + 1);

        /// <summary>
        /// Zero-bound advance: consume one RandSeed step and return 0 (original Random(0) semantics),
        /// the precise replacement for the current parameterless .Random() advance shims.
        /// </summary>
        public static int Advance() => DelphiRandom.Random(0);
    }
}
