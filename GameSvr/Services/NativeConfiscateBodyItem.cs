using SystemModule;

namespace GameSvr.Services
{
    /// <summary>
    /// NPC script <c>ConfiscateBodyItem</c> = <c>sub_6E0500</c>：强制没收指定穿戴位。
    /// </summary>
    public static class NativeConfiscateBodyItem
    {
        public const uint CoreEa = 0x006E0500;
        private const string LogReason = "NPC强制没收"; // 0x6E059C

        /// <summary>
        /// Native always returns true (0x6E056B mov bl,1); logs type 0xAC when slot occupied.
        /// </summary>
        public static bool Execute(TPlayObject player, int bodyPos, NormNpc npc)
        {
            if (player?.m_UseItems == null)
                return true;

            if (bodyPos < 0 || bodyPos >= player.m_UseItems.Length)
                return true;

            var item = player.m_UseItems[bodyPos];
            if (item == null || item.wIndex <= 0)
                return true;

            var itemName = M2Share.UserEngine.GetStdItemName(item.wIndex) ?? string.Empty;

            // 0x6E0530..0x6E055E: MakeIndex, Dura, literal reason,
            // raw StdItem name, type 0xAC, then sub_768BE0.
            M2Share.AddNativeGameDataLog(player, 0xAC, itemName,
                item.MakeIndex, item.Dura, LogReason);

            player.SendDelItems(item);
            item.wIndex = 0;
            player.RecalcAbilitys();
            return true;
        }
    }
}
