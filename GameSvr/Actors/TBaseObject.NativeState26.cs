using SystemModule;

namespace GameSvr
{
    public partial class TBaseObject
    {
        private const byte NativeState26Type = 26;
        private const uint NativeState26Mask = 1u << NativeState26Type;
        // Whole low word. SetBodyState no longer splits ownership of bits 21..31
        // with m_wStatusTimeArr, so every bit the timed layer sets has to survive
        // the next GetCharStatus() rebuild. The legacy array is OR'd on top of this
        // in GetCharStatus, so a state stays lit until both carriers have expired.
        private const long NativePersistentLowStateMask = 0xFFFFFFFFL;

        private uint m_dwNativeState26Deadline;

        internal bool TryApplyNativeState26(int durationSeconds,
            int value = 0, byte flag = 0)
        {
            return AddTimedAbilityInternal(NativeState26Type, value,
                unchecked(durationSeconds * 1000), flag);
        }

        private static bool IsBlockedByNativeState16(byte internalType)
        {
            return internalType is 0 or 13 or 24 or NativeState26Type
                or 28 or 29 or 30 or 31;
        }

        private bool IsNativeState26DeadlineActive(int now)
        {
            return unchecked((uint)now) < m_dwNativeState26Deadline;
        }

        private void ApplyNativeTimedAbilityMutation(byte internalType)
        {
            if (internalType != NativeState26Type ||
                m_wEffectResistance < 125)
            {
                return;
            }

            var seconds = (m_wEffectResistance - 125) / 25 + 4;
            if (seconds > 10)
            {
                seconds = m_wNativeState26DeadlineBonus + 10;
            }

            m_dwNativeState26Deadline = unchecked(
                (uint)HUtil32.GetTickCount() + (uint)(seconds * 1000));
        }
    }
}
