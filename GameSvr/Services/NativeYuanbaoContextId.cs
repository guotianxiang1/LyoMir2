using SystemModule;

namespace GameSvr
{
    // Dormant model of the original yuanbao context-id generator sub_711F08 @0x00711F08:
    //   30 iterations (ebx = 0x1E); each char = RandomRange('A', '[') via sub_43CD60 @0x0043CD60,
    //   which for lo <= hi returns lo + Random(hi - lo) = 0x41 + Random(0x5B - 0x41) = 65 + Random(26)
    //   (Random = sub_403B4C). The 30 letters are appended into one AnsiString.
    //   Consumes exactly 30 sequential RandSeed draws on the single game-loop owner.
    //
    // The live NativeYuanbaoManager.CreateContextIdForEnqueue uses Random.Shared.Next('A','Z'+1):
    // the letter RANGE matches (26 letters A..Z) but it draws from Random.Shared, off the RandSeed
    // owner thread, so it neither reproduces the original values nor advances the global sequence.
    // Correcting it is part of the atomic RandSeed cutover (audit step 7); this models the exact
    // owner-side contract, dormant, with the seed injected rather than taken from the live owner.
    public static class NativeYuanbaoContextId
    {
        public const int Length = 30;           // ebx = 0x1E
        public const int FirstLetter = 0x41;    // 'A'
        public const int PastLastLetter = 0x5B; // '['
        public const int LetterCount = PastLastLetter - FirstLetter; // 26

        /// <summary>
        /// Generate the 30-byte context id from an owner-thread RandSeed value, consuming exactly
        /// 30 Random(26) draws (each char = 'A' + Random(26)). Dormant: seed is injected.
        /// </summary>
        public static byte[] Generate(uint ownerSeed)
        {
            DelphiRandom.Seed = ownerSeed;
            var bytes = new byte[Length];
            for (int i = 0; i < Length; i++)
                bytes[i] = (byte)(FirstLetter + DelphiRandom.Random(LetterCount));
            return bytes;
        }
    }
}
