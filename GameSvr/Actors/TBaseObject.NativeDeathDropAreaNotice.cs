namespace GameSvr
{
    public partial class TBaseObject
    {
        /// <summary>
        /// Native sub_73E4C4 @0x73E4C4 — equip-death worker tail (caller sub_73FC70 @0x73FFD4).
        /// Gates on non-fight map and <see cref="InNativeSafeZone12()"/> @0x73E509 before SysMsg.
        /// </summary>
        private void TryNativeDeathDropAreaNotice(int dropCount)
        {
            if (dropCount <= 0 || m_PEnvir?.Flag == null)
                return;

            // 0x73E4F3 cmp [map+0x5D],0 / 0x73E4FD cmp [map+0x5E],0
            if (m_PEnvir.Flag.boFightZone || m_PEnvir.Flag.boFight3Zone)
                return;

            // 0x73E507 mov eax,self / 0x73E509 call sub_76858C
            if (InNativeSafeZone12())
                return;

            if (this is not TPlayObject player || player.m_ItemList.Count <= 0)
                return;

            // 0x73E516 mov esi,0x32 default hint color; message body is job/drop-count keyed.
            // Full Delphi Format() arms not ported — only the native safe-zone predicate is wired here.
        }
    }
}
