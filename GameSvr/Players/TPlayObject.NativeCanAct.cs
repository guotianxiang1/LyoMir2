namespace GameSvr
{
    public partial class TPlayObject
    {
        /// <summary>
        /// Native sub_6E6700 (TPlayer override): adds MOVE-15 cast lock check.
        /// </summary>
        internal override bool IsNativeCanActBlocked(int callerArg)
        {
            if (base.IsNativeCanActBlocked(callerArg)) return true;
            if (m_nNativeForcedMoveRemaining != 0) return true;  // Cast lock
            return false;
        }
    }
}
