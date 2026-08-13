using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Magic id 290. Outer arm 0x6BCC25. nTargetX is a facing 0..7
    /// (`cmp [ebp+0xC],0 / jl` and `cmp [ebp+0xC],7 / jg`, both signed).
    /// Out of range jumps to 0x6BCCA0 which writes [ebp-5]=0.
    /// In range, 0x6BCC31 stores the byte into [esi+0x154] then calls
    /// TPlayer VMT+0x22C = 0x6ED27C `33 C0 C3`, so Result is FALSE and
    /// the 0x769258 tail at 0x6BCC54 never runs. Facing is still updated.
    /// </summary>
    public partial class TBaseObject
    {
        internal bool TryActivateNativeSkill290(int targetX)
        {
            if (targetX < 0 || targetX > 7)
            {
                return false;
            }
            m_btDirection = unchecked((byte)targetX);
            return false;
        }
    }
}
