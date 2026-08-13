using SystemModule;

namespace GameSvr
{
    // The original yuanbao context-id generator sub_711F08 @0x00711F08:
    //
    //   00711F1F  BB 1E 00 00 00  mov ebx,0x1E          ; 30 characters
    //   00711F24  BA 5B 00 00 00  mov edx,0x5B          ; '['
    //   00711F29  B8 41 00 00 00  mov eax,0x41          ; 'A'
    //   00711F2E  E8 2D AE D2 FF  call 0x0043CD60       ; RandomRange -> 'A' + Random(26)
    //   00711F38  call 0x004056E8 / 00711F42 call 0x004057D8   ; char -> string, append
    //   00711F47  4B 75 DA        dec ebx / jne 0x00711F24
    //
    // Consumes exactly 30 sequential RandSeed draws.
    public static class NativeYuanbaoContextId
    {
        public const int Length = 30;           // ebx = 0x1E
        public const int FirstLetter = 0x41;    // 'A'
        public const int PastLastLetter = 0x5B; // '['
        public const int LetterCount = PastLastLetter - FirstLetter; // 26

        /// <summary>
        /// Generate the 30-byte context id from the live RandSeed owner, consuming exactly
        /// 30 Random(26) draws (each char = 'A' + Random(26)).
        /// </summary>
        public static byte[] Generate()
        {
            var bytes = new byte[Length];
            for (int i = 0; i < Length; i++)
                bytes[i] = (byte)(FirstLetter + DelphiRandom.Random(LetterCount));
            return bytes;
        }

        /// <summary>Same draw sequence from an explicitly injected seed, for replay checks.</summary>
        public static byte[] Generate(uint ownerSeed)
        {
            DelphiRandom.Seed = ownerSeed;
            return Generate();
        }
    }
}
