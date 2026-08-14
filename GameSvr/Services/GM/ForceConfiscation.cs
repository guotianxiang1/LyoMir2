using SystemModule;

namespace GameSvr.Services.GM
{
    /// <summary>
    /// GM命令强制没收物品服务 - Force item confiscation for GM commands.
    /// MVI Pattern: Model-View-Intent for item confiscation operations.
    /// </summary>
    public static class ForceConfiscation
    {
        /// <summary>
        /// 强制没收玩家装备栏物品 - Force confiscate equipped item from body slot.
        /// Delegates to NativeConfiscateBodyItem for body items.
        /// </summary>
        /// <param name="player">目标玩家</param>
        /// <param name="bodyPos">装备位置 (0-12)</param>
        /// <param name="operatorName">操作者名称(GM)</param>
        /// <returns>是否成功没收</returns>
        public static bool ConfiscateBodyItem(TPlayObject player, int bodyPos, string operatorName)
        {
            if (player?.m_UseItems == null)
                return false;

            if (bodyPos < 0 || bodyPos >= player.m_UseItems.Length)
                return false;

            var item = player.m_UseItems[bodyPos];
            if (item == null || item.wIndex <= 0)
                return false;

            var itemName = M2Share.UserEngine.GetStdItemName(item.wIndex) ?? string.Empty;
            var count = item.Dura > 0 ? item.Dura : (ushort)1;

            // Log GM confiscation action (type 0xAC)
            M2Share.AddGameDataLog(((char)0xAC) + "\t" + player.m_sMapName + "\t" +
                player.m_nCurrX + "\t" + player.m_nCurrY + "\t" + player.m_sCharName +
                "\t" + $"GM强制没收装备 {itemName} {count}" + "\t" + item.MakeIndex + "\t" + '1' + "\t" +
                operatorName);

            player.SendDelItems(item);
            item.wIndex = 0;
            player.RecalcAbilitys();
            return true;
        }

        /// <summary>
        /// 强制没收玩家背包物品 - Force confiscate item from inventory by index.
        /// </summary>
        /// <param name="player">目标玩家</param>
        /// <param name="itemIndex">物品索引</param>
        /// <param name="operatorName">操作者名称(GM)</param>
        /// <returns>是否成功没收</returns>
        public static bool ConfiscateBagItem(TPlayObject player, int itemIndex, string operatorName)
        {
            if (player?.m_ItemList == null)
                return false;

            if (itemIndex < 0 || itemIndex >= player.m_ItemList.Count)
                return false;

            var item = player.m_ItemList[itemIndex];
            if (item == null || item.wIndex <= 0)
                return false;

            var itemName = M2Share.UserEngine.GetStdItemName(item.wIndex) ?? string.Empty;
            var count = item.Dura > 0 ? item.Dura : (ushort)1;

            // Log GM confiscation action
            M2Share.AddGameDataLog(((char)0xAC) + "\t" + player.m_sMapName + "\t" +
                player.m_nCurrX + "\t" + player.m_nCurrY + "\t" + player.m_sCharName +
                "\t" + $"GM强制没收背包 {itemName} {count}" + "\t" + item.MakeIndex + "\t" + '1' + "\t" +
                operatorName);

            player.m_ItemList.RemoveAt(itemIndex);
            player.SendDelItems(item);
            return true;
        }

        /// <summary>
        /// 强制没收玩家指定名称的所有物品 - Confiscate all items by name.
        /// </summary>
        /// <param name="player">目标玩家</param>
        /// <param name="itemName">物品名称</param>
        /// <param name="operatorName">操作者名称(GM)</param>
        /// <returns>没收的物品数量</returns>
        public static int ConfiscateAllItemsByName(TPlayObject player, string itemName, string operatorName)
        {
            if (player?.m_ItemList == null || string.IsNullOrEmpty(itemName))
                return 0;

            int confiscatedCount = 0;

            // Search and confiscate from bag
            for (int i = player.m_ItemList.Count - 1; i >= 0; i--)
            {
                var item = player.m_ItemList[i];
                if (item != null && item.wIndex > 0)
                {
                    var currentItemName = M2Share.UserEngine.GetStdItemName(item.wIndex);
                    if (currentItemName != null && currentItemName.Equals(itemName, System.StringComparison.OrdinalIgnoreCase))
                    {
                        var count = item.Dura > 0 ? item.Dura : (ushort)1;
                        confiscatedCount += count;

                        M2Share.AddGameDataLog(((char)0xAC) + "\t" + player.m_sMapName + "\t" +
                            player.m_nCurrX + "\t" + player.m_nCurrY + "\t" + player.m_sCharName +
                            "\t" + $"GM强制没收(批量) {currentItemName} {count}" + "\t" + item.MakeIndex + "\t" + '1' + "\t" +
                            operatorName);

                        player.m_ItemList.RemoveAt(i);
                        player.SendDelItems(item);
                    }
                }
            }

            // Search and confiscate from equipped items
            if (player.m_UseItems != null)
            {
                for (int i = 0; i < player.m_UseItems.Length; i++)
                {
                    var item = player.m_UseItems[i];
                    if (item != null && item.wIndex > 0)
                    {
                        var currentItemName = M2Share.UserEngine.GetStdItemName(item.wIndex);
                        if (currentItemName != null && currentItemName.Equals(itemName, System.StringComparison.OrdinalIgnoreCase))
                        {
                            var count = item.Dura > 0 ? item.Dura : (ushort)1;
                            confiscatedCount += count;

                            M2Share.AddGameDataLog(((char)0xAC) + "\t" + player.m_sMapName + "\t" +
                                player.m_nCurrX + "\t" + player.m_nCurrY + "\t" + player.m_sCharName +
                                "\t" + $"GM强制没收(装备) {currentItemName} {count}" + "\t" + item.MakeIndex + "\t" + '1' + "\t" +
                                operatorName);

                            player.SendDelItems(item);
                            item.wIndex = 0;
                        }
                    }
                }

                if (confiscatedCount > 0)
                {
                    player.RecalcAbilitys();
                }
            }

            return confiscatedCount;
        }

        /// <summary>
        /// 强制没收玩家所有物品 - Confiscate all items from player.
        /// WARNING: This is a destructive operation.
        /// </summary>
        /// <param name="player">目标玩家</param>
        /// <param name="operatorName">操作者名称(GM)</param>
        /// <returns>没收的物品总数</returns>
        public static int ConfiscateAllItems(TPlayObject player, string operatorName)
        {
            if (player == null)
                return 0;

            int totalConfiscated = 0;

            // Confiscate all bag items
            if (player.m_ItemList != null)
            {
                for (int i = player.m_ItemList.Count - 1; i >= 0; i--)
                {
                    var item = player.m_ItemList[i];
                    if (item != null && item.wIndex > 0)
                    {
                        var itemName = M2Share.UserEngine.GetStdItemName(item.wIndex) ?? string.Empty;
                        var count = item.Dura > 0 ? item.Dura : (ushort)1;
                        totalConfiscated += count;

                        M2Share.AddGameDataLog(((char)0xAC) + "\t" + player.m_sMapName + "\t" +
                            player.m_nCurrX + "\t" + player.m_nCurrY + "\t" + player.m_sCharName +
                            "\t" + $"GM强制没收(全部) {itemName} {count}" + "\t" + item.MakeIndex + "\t" + '1' + "\t" +
                            operatorName);

                        player.m_ItemList.RemoveAt(i);
                        player.SendDelItems(item);
                    }
                }
            }

            // Confiscate all equipped items
            if (player.m_UseItems != null)
            {
                for (int i = 0; i < player.m_UseItems.Length; i++)
                {
                    var item = player.m_UseItems[i];
                    if (item != null && item.wIndex > 0)
                    {
                        var itemName = M2Share.UserEngine.GetStdItemName(item.wIndex) ?? string.Empty;
                        var count = item.Dura > 0 ? item.Dura : (ushort)1;
                        totalConfiscated += count;

                        M2Share.AddGameDataLog(((char)0xAC) + "\t" + player.m_sMapName + "\t" +
                            player.m_nCurrX + "\t" + player.m_nCurrY + "\t" + player.m_sCharName +
                            "\t" + $"GM强制没收(装备全部) {itemName} {count}" + "\t" + item.MakeIndex + "\t" + '1' + "\t" +
                            operatorName);

                        player.SendDelItems(item);
                        item.wIndex = 0;
                    }
                }

                if (totalConfiscated > 0)
                {
                    player.RecalcAbilitys();
                }
            }

            return totalConfiscated;
        }
    }
}
