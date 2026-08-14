using System;

namespace GameSvr.StatusEffects
{
    /// <summary>
    /// Native body-state persistence filter
    ///
    /// Source: bodystate-persist-filter-polarity.md
    /// Function: sub_791D54(al=stateId): Boolean
    ///
    /// CRITICAL: Bitmap polarity is INVERTED
    /// - Bitmap bit SET means NOT persisted (excluded from ScriptData)
    /// - Bitmap bit CLEAR means persisted
    /// - IDs > 0x67 (104+) are persisted (skip bt with CF=0)
    ///
    /// Proven by LIVE GOLDEN DATA (252/252 records satisfy corrected reading)
    ///
    /// Raw bytes:
    /// 791D54  cmp al,0x67            ; >0x67 skips the bt entirely
    /// 791D56  ja  0x791D62
    /// 791D58  and eax,0x7F
    /// 791D5B  bt  dword [0x791D6C],eax   ; CF = bit
    /// 791D62  jae 0x791D67               ; CF=0 (bit CLEAR) -> True (persist)
    /// 791D64  xor eax,eax / ret          ; CF=1 (bit SET)   -> False (exclude)
    /// 791D67  mov al,1 / ret
    ///
    /// Bitmap @0x791D6C: FF FF 0F 90 00 00 FE FF FF 00 C0 03 40 00 00 00
    /// </summary>
    public static class BodyStatePersistFilter
    {
        // Bitmap @0x791D6C from native binary
        // Bits SET = excluded from persistence (50 states)
        // Bits CLEAR = persisted (57 states)
        private static readonly byte[] ExclusionBitmap = new byte[]
        {
            0xFF, 0xFF, 0x0F, 0x90, 0x00, 0x00, 0xFE, 0xFF,
            0xFF, 0x00, 0xC0, 0x03, 0x40, 0x00, 0x00, 0x00
        };

        /// <summary>
        /// Persisted set: 57 states where bitmap bit is CLEAR
        /// IDs: 20,21,22,23,24,25,26,27,29,30,32..48,72..85,90..101,103,104,105,106
        ///
        /// Excluded set: 50 states where bitmap bit is SET
        /// IDs: 0..19, 28, 31, 49..71, 86..89, 102
        ///
        /// Semantics: excluded set is poisons (stPoisonBlue/Yellow/Green, csVioletPoision),
        /// stStone/stFreezeForever, mount states (csZaiMaShang/csZaiBieRenMaShang),
        /// csFatal — exactly the states you would NOT want re-applied at login
        /// </summary>
        public static bool ShouldPersist(byte stateId)
        {
            // IDs > 0x67 (104/105/106) skip the bt and return true
            // EA: 791D54 cmp al,0x67 / 791D56 ja 0x791D62
            if (stateId > 0x67)
                return true;

            // Mask to 7 bits
            // EA: 791D58 and eax,0x7F
            int bitIndex = stateId & 0x7F;

            // Test bit in exclusion bitmap
            // EA: 791D5B bt dword [0x791D6C],eax
            int byteIndex = bitIndex >> 3;  // div 8
            int bitOffset = bitIndex & 0x07; // mod 8
            bool bitSet = (ExclusionBitmap[byteIndex] & (1 << bitOffset)) != 0;

            // Inverted polarity: bit CLEAR means persist
            // EA: 791D62 jae 0x791D67  (CF=0 -> True)
            // EA: 791D64 xor eax,eax / ret  (CF=1 -> False)
            return !bitSet;
        }

        /// <summary>
        /// Get all persisted state IDs (for validation/debugging)
        /// Returns the 57 states that should be saved to ScriptData
        /// </summary>
        public static byte[] GetPersistedStateIds()
        {
            var result = new System.Collections.Generic.List<byte>();

            for (byte i = 0; i <= 106; i++)
            {
                if (ShouldPersist(i))
                    result.Add(i);
            }

            return result.ToArray();
        }

        /// <summary>
        /// Get all excluded state IDs (for validation/debugging)
        /// Returns the 50 states that should NOT be saved
        /// </summary>
        public static byte[] GetExcludedStateIds()
        {
            var result = new System.Collections.Generic.List<byte>();

            for (byte i = 0; i <= 106; i++)
            {
                if (!ShouldPersist(i))
                    result.Add(i);
            }

            return result.ToArray();
        }
    }
}
