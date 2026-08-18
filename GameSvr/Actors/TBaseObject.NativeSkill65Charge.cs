using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Magic id 65, the CHARGE half of the 65..68 family. Outer arm
    /// 0x6BC9FE. Result is set TRUE at 0x6BCA02 BEFORE the cooldown
    /// probe, so a cooling-down cache still returns TRUE (no packet).
    /// </summary>
    public partial class TBaseObject
    {
        /// <summary>
        /// self+0xE8. Set here at 0x6BCA22 and by the war-hero decision path
        /// at 0x693111, consumed before cross-moon at 0x692C40, and cleared
        /// by the 66/67 release path at 0x74439C.
        /// </summary>
        internal byte m_btNativeChargedIndicator;

        /// <summary>
        /// 0x6BC9FE..0x6BCA41. The cooldown key is MagicId of the CACHED
        /// 65..68 record at self+0xC4 (`0x6BCA02 8B 86 C4 00 00 00` then
        /// `E8 2F BB E0 FF call 0x4C853C`), not the id being cast.
        /// </summary>
        internal bool TryActivateNativeSkill65Charge()
        {
            // 0x6BCA02 `C6 45 FB 01` runs first.
            TUserMagic cached = m_NativeChargedCounterMagic;
            if (cached?.MagicInfo == null)
            {
                return true;
            }

            int key = cached.MagicInfo.wMagicID;
            if (GetNativeColdTimeRemaining(key) != 0)
            {
                // 0x6BCA1A `85 C0` / 0x6BCA1C `0F 85 E0 02 00 00 jne 0x6BCD02`
                // with [ebp-5] already 1.
                return true;
            }

            m_btNativeChargedIndicator = 1;
            if (this is TPlayObject player)
            {
                player.SendDefMessage(Grobal2.SM_CHARGED_COUNTER,
                    0, 0, 0, 0, string.Empty);
            }
            return true;
        }
    }
}
