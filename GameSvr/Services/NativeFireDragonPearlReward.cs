using System;
using SystemModule;

namespace GameSvr.Services
{
    /// <summary>
    /// 火龙珠奖励系统 - 战神引擎 0x00751018
    /// MVI - 最小可行实现
    /// </summary>
    public static class NativeFireDragonPearlReward
    {
        // 战神引擎常量
        private const string FIRE_DRAGON_PEARL = "火龙珠";
        private const ushort MSG_COLOR_GREEN = 0xFFDB;

        /// <summary>
        /// 处理火龙珠奖励
        /// </summary>
        public static bool ProcessFireDragonPearlReward(TPlayObject player, int rewardType)
        {
            if (player == null)
                return false;

            // Phase 1: 验证奖励类型
            if (rewardType < 0 || rewardType > 10)
                return false;

            // Phase 2: 查找火龙珠
            var pearl = FindItem(player, FIRE_DRAGON_PEARL);
            if (pearl == null)
            {
                player.SysMsg($"需要{FIRE_DRAGON_PEARL}", MsgColor.Red, MsgType.Hint);
                return false;
            }

            // Phase 3: 根据类型给予奖励
            bool success = GrantReward(player, rewardType);

            if (success)
            {
                // 消耗火龙珠
                ConsumeItem(player, pearl);
                player.SysMsg("获得火龙珠奖励", MsgColor.Green, MsgType.Hint);
            }

            return success;
        }

        private static TUserItem FindItem(TPlayObject player, string itemName)
        {
            if (player?.m_ItemList == null)
                return null;

            foreach (var item in player.m_ItemList)
            {
                if (item == null)
                    continue;

                string name = M2Share.UserEngine?.GetStdItemName(item.wIndex) ?? string.Empty;
                if (string.Equals(name, itemName, StringComparison.Ordinal))
                    return item;
            }

            return null;
        }

        private static void ConsumeItem(TPlayObject player, TUserItem item)
        {
            if (player?.m_ItemList == null || item == null)
                return;

            player.m_ItemList.Remove(item);

            // 记录日志
            string itemName = M2Share.UserEngine?.GetStdItemName(item.wIndex) ?? "Unknown";
            M2Share.AddGameDataLog(string.Join('\t', 10, player.m_sMapName,
                player.m_nCurrX, player.m_nCurrY, player.m_sCharName, itemName,
                unchecked((uint)item.MakeIndex), 1, "火龙珠奖励"));
        }

        private static bool GrantReward(TPlayObject player, int rewardType)
        {
            // MVI: 简化奖励逻辑
            switch (rewardType)
            {
                case 0: // 经验奖励
                    player.GainExp(100000);
                    return true;
                case 1: // 金币奖励
                    player.IncGold(50000);
                    return true;
                default:
                    return false;
            }
        }
    }
}
