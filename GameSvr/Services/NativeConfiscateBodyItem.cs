using SystemModule;

namespace GameSvr.Services
{
    /// <summary>
    /// NPC script <c>ConfiscateBodyItem</c> = <c>sub_6E0500</c>：强制没收指定穿戴位。
    /// </summary>
    public static class NativeConfiscateBodyItem
    {
        public const uint CoreEa = 0x006E0500;
        private const string LogFmt = "NPC强制没收 %s %d"; // 0x6E059C

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
            var count = item.Dura > 0 ? item.Dura : (ushort)1;

            // 0x6E0558 mov dx,0xAC / call 0x768BE0
            M2Share.AddGameDataLog(((char)0xAC) + "\t" + player.m_sMapName + "\t" +
                player.m_nCurrX + "\t" + player.m_nCurrY + "\t" + player.m_sCharName +
                "\t" + string.Format(LogFmt.Replace("%s", "{0}").Replace("%d", "{1}"),
                    itemName, count) + "\t" + item.MakeIndex + "\t" + '1' + "\t" +
                (npc?.m_sCharName ?? string.Empty));

            player.SendDelItems(item);
            item.wIndex = 0;
            player.RecalcAbilitys();
            return true;
        }
    }
}
