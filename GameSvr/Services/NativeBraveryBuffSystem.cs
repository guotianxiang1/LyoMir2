using System;
using SystemModule;

namespace GameSvr.Services
{
    /// <summary>
    /// 勇者之力限时buff系统 - 战神引擎 0x007490E4
    /// MVI - 最小可行实现
    /// </summary>
    public static class NativeBraveryBuffSystem
    {
        // 战神引擎常量
        private const string BRAVERY_TOKEN = "勇者令牌";
        private const int BUFF_DURATION_SECONDS = 3600; // 1小时

        /// <summary>
        /// 激活勇者之力buff
        /// </summary>
        public static bool ActivateBraveryBuff(TPlayObject player, int buffType)
        {
            if (player == null)
                return false;

            try
            {
                // Phase 1: 验证buff类型
                if (buffType < 0 || buffType > 5)
                    return false;

                // Phase 2: 检查是否已有buff
                if (HasBraveryBuff(player, buffType))
                {
                    player.SysMsg("勇者之力buff已激活", MsgColor.Red, MsgType.Hint);
                    return false;
                }

                // Phase 3: 查找并消耗勇者令牌
                var token = FindBuffToken(player, BRAVERY_TOKEN);
                if (token == null)
                {
                    player.SysMsg($"激活需要{BRAVERY_TOKEN}", MsgColor.Red, MsgType.Hint);
                    return false;
                }

                // Phase 4: 应用buff
                ApplyBraveryBuff(player, buffType, BUFF_DURATION_SECONDS);

                // Phase 5: 消耗令牌
                ConsumeBuffToken(player, token);

                // Phase 6: 发送成功消息
                string buffName = GetBuffName(buffType);
                player.SysMsg($"激活勇者之力：{buffName}，持续时间{BUFF_DURATION_SECONDS / 60}分钟", MsgColor.Green, MsgType.Hint);

                return true;
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[Exception] 勇者之力buff异常: {ex.Message}");
                return false;
            }
        }

        private static bool HasBraveryBuff(TPlayObject player, int buffType)
        {
            // MVI: 检查玩家是否已有该buff
            // 实际应该检查玩家的buff列表
            return false;
        }

        private static void ApplyBraveryBuff(TPlayObject player, int buffType, int duration)
        {
            // MVI: 应用buff效果
            // 实际应该添加到玩家的buff列表
            switch (buffType)
            {
                case 0: // 攻击力提升
                    player.m_Abil.DC = (ushort)(player.m_Abil.DC + 5);
                    break;
                case 1: // 魔法力提升
                    player.m_Abil.MC = (ushort)(player.m_Abil.MC + 5);
                    break;
                case 2: // 道术提升
                    player.m_Abil.SC = (ushort)(player.m_Abil.SC + 5);
                    break;
                case 3: // 防御力提升
                    player.m_Abil.AC = (ushort)(player.m_Abil.AC + 5);
                    break;
                case 4: // 魔御提升
                    player.m_Abil.MAC = (ushort)(player.m_Abil.MAC + 5);
                    break;
                case 5: // 全属性提升
                    player.m_Abil.DC = (ushort)(player.m_Abil.DC + 3);
                    player.m_Abil.MC = (ushort)(player.m_Abil.MC + 3);
                    player.m_Abil.SC = (ushort)(player.m_Abil.SC + 3);
                    break;
            }

            // TODO: 设置buff过期时间
        }

        private static string GetBuffName(int buffType)
        {
            return buffType switch
            {
                0 => "攻击强化",
                1 => "魔法强化",
                2 => "道术强化",
                3 => "防御强化",
                4 => "魔御强化",
                5 => "全能强化",
                _ => "未知buff"
            };
        }

        private static TUserItem FindBuffToken(TPlayObject player, string itemName)
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

        private static void ConsumeBuffToken(TPlayObject player, TUserItem item)
        {
            if (player?.m_ItemList == null || item == null)
                return;

            player.m_ItemList.Remove(item);

            string itemName = M2Share.UserEngine?.GetStdItemName(item.wIndex) ?? "Unknown";
            M2Share.AddGameDataLog(string.Join('\t', 10, player.m_sMapName,
                player.m_nCurrX, player.m_nCurrY, player.m_sCharName, itemName,
                unchecked((uint)item.MakeIndex), 1, "勇者之力buff"));
        }
    }
}
