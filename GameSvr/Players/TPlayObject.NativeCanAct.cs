namespace GameSvr
{
    public partial class TPlayObject
    {
        private const ushort NativeAlwaysAllowedBlockedSpell = 0x72;
        private const ushort NativeState26AllowedBlockedSpell = 0xD3;

        /// <summary>
        /// Native sub_6E6700 (TPlayer override): adds MOVE-15 cast lock check.
        /// </summary>
        internal override bool IsNativeCanActBlocked(int callerArg)
        {
            if (base.IsNativeCanActBlocked(callerArg)) return true;
            if (m_nNativeForcedMoveRemaining != 0) return true;  // Cast lock
            return false;
        }

        /// <summary>
        /// Native sub_7725FC, reached only when the player can-act slot returned
        /// false for CM_SPELL. Magic 0x72 is always admitted; 0xD3 is admitted
        /// only while body-state 0x1A is active.
        /// </summary>
        internal bool CanNativeSpellBypassCanActGate(int magicId)
        {
            ushort nativeMagicId = unchecked((ushort)magicId);
            return nativeMagicId == NativeAlwaysAllowedBlockedSpell
                || nativeMagicId == NativeState26AllowedBlockedSpell
                && HasNativeActiveState(0x1A);
        }
    }
}
