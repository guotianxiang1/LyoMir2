using System.Collections.Generic;
using SystemModule;

namespace GameSvr.Services
{
    /// <summary>
    /// NPC script <c>TakeFullDuraItem</c> = <c>sub_6DFE08</c>：全-or-无收取满持久背包物。
    /// </summary>
    public static class NativeTakeFullDuraItem
    {
        public const uint CoreEa = 0x006DFE08;

        /// <summary>
        /// Returns true only when at least <paramref name="count"/> matching full-dura
        /// slots exist and all are removed (0x6DFED8 success path).
        /// </summary>
        public static bool Execute(TPlayObject player, string itemName, int count)
        {
            if (player?.m_ItemList == null || count <= 0 ||
                string.IsNullOrWhiteSpace(itemName))
                return false;

            var stdIndex = M2Share.UserEngine?.GetStdItemIdx(itemName) ?? -1;
            if (stdIndex <= 0)
                return false;

            var indices = new List<int>();
            for (var i = 0; i < player.m_ItemList.Count; i++)
            {
                var item = player.m_ItemList[i];
                if (item == null || item.wIndex != stdIndex)
                    continue;
                // 0x6DFEAA cmp word[+0x26], word[+0x28]
                if (item.Dura != item.DuraMax)
                    continue;
                indices.Add(i);
                if (indices.Count >= count)
                    break;
            }

            // 0x6DFECF cmp found, requested / jne fail
            if (indices.Count < count)
                return false;

            // Remove high-to-low indices (0x6DFEDC backward loop).
            for (var n = indices.Count - 1; n >= 0; n--)
            {
                var idx = indices[n];
                if (idx < 0 || idx >= player.m_ItemList.Count)
                    continue;
                var item = player.m_ItemList[idx];
                if (item == null)
                    continue;
                player.m_ItemList.RemoveAt(idx);
                player.SendDelItems(item);
                M2Share.AddGameDataLog("10" + "\t" + player.m_sMapName + "\t" +
                    player.m_nCurrX + "\t" + player.m_nCurrY + "\t" +
                    player.m_sCharName + "\t" + itemName + "\t" +
                    item.MakeIndex + "\t" + "1" + "\t" + "0");
                player.Dispose(item);
            }

            player.WeightChanged();
            return true;
        }

        public static int ExecuteCounting(TPlayObject player, string itemName, int count)
        {
            if (!Execute(player, itemName, count))
                return 0;
            return count;
        }
    }
}
