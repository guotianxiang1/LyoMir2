using System;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// 英雄合击技能4级升级 - 战神引擎 0x0078AF68
    /// 需要消耗"火龙之心"物品
    /// </summary>
    public partial class HeroObject
    {
        // 战神引擎常量 (EA地址标注)
        private const string FIRE_DRAGON_HEART_NAME = "火龙之心"; // EA: 0x68DA7C
        private const ushort MSG_COLOR_RED = 0x38FF;            // EA: 0x78AFB8, 0x78B060
        private const byte MSG_CHANNEL = 0x64;                  // EA: 0x78B064

        // 对象偏移常量 (从反汇编提取)
        // Hero+0x6D4: m_NativeUnionMagic (合击技能对象指针)
        // Magic+0x0C: btLevel (技能等级 BYTE)
        // Hero+0x278: m_Abil.Level (英雄等级 WORD)
        // Hero+0x68C: 主人对象指针
        // Hero+0x106: 角色名称

        /// <summary>
        /// 尝试升级英雄合击技能到4级
        /// </summary>
        /// <param name="hero">英雄对象</param>
        /// <returns>是否成功升级</returns>
        internal static bool TryUpgradeUnionSkillToLevel4(HeroObject hero)
        {
            if (hero == null)
                return false;

            try
            {
                // Phase 1: 类型检查 (EA: 0x78AF8B-0x78AF9A)
                // 调用 0x404828 (Delphi的is操作符，检查类型@0x6855E4)
                if (hero.m_boGhost)
                    return false;

                // Phase 2: 获取合击技能对象 (EA: 0x78AFA0)
                // [hero+0x6D4] -> m_NativeUnionMagic
                var unionMagic = hero.m_NativeUnionMagic;

                // Phase 3: 检查技能对象存在且等级<4 (EA: 0x78AFA9-0x78AFB6)
                if (unionMagic == null)
                {
                    SendErrorMessage(hero, "你还没有学习合击技能");
                    return false;
                }

                if (unionMagic.btLevel >= 4)
                {
                    SendErrorMessage(hero, "合击技能已达到最高等级");
                    return false;
                }

                // Phase 4: 检查英雄等级要求 (EA: 0x78AFD0-0x78AFF4)
                // 从等级表读取所需等级: word[0x7D4DD8 + level*2]
                int requiredLevel = GetRequiredHeroLevel(unionMagic.btLevel);

                if (hero.m_Abil.Level < requiredLevel)
                {
                    // 发送等级不足消息 (EA: 0x78B073-0x78B0AD)
                    string levelMsg = $"下一级合击技能提升需要英雄等级达到{requiredLevel}级";
                    SendRedMessage(hero, levelMsg);
                    return false;
                }

                // Phase 5: 查找并消耗"火龙之心" (EA: 0x78AFF6-0x78B000)
                // 调用 0x4C853C 查找物品
                TPlayObject master = hero.m_Master as TPlayObject;
                if (master == null)
                    return false;

                var dragonHeart = FindAndConsumeItem(master, FIRE_DRAGON_HEART_NAME);
                if (dragonHeart == null)
                {
                    SendErrorMessage(hero, $"升级合击技能需要{FIRE_DRAGON_HEART_NAME}");
                    return false;
                }

                // Phase 6: 升级技能等级 (EA: 0x78B002-0x78B00B)
                // level++ 然后调用 0x745294
                byte newLevel = (byte)(unionMagic.btLevel + 1);
                unionMagic.btLevel = newLevel;

                // Phase 7: 格式化并发送成功消息 (EA: 0x78B010-0x78B06D)
                // 调用 0x40DCC0 (Delphi Format) 格式化消息
                string masterName = master.m_sCharName;
                string heroName = hero.m_sCharName;
                string successMsg = $"恭喜您的英雄{heroName}学会了{newLevel}级合击技能，威力大增！";

                // 发送到全服或附近玩家 (EA: 0x78B059-0x78B068)
                // 调用 0x5F701C, 颜色0x38FF, 通道0x64
                SendGlobalMessage(successMsg, MSG_COLOR_RED, MSG_CHANNEL);

                return true;
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[Exception] 英雄合击技能升级异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 从等级表获取所需英雄等级 (EA: 0x7D4DD8)
        /// </summary>
        private static int GetRequiredHeroLevel(int currentSkillLevel)
        {
            // 等级表: word[0x7D4DD8 + level*2]
            // 这个表需要从战神引擎数据段提取
            // 暂时使用推断值
            return currentSkillLevel switch
            {
                0 => 40,  // 1级需要40级
                1 => 43,  // 2级需要43级
                2 => 45,  // 3级需要45级
                3 => 48,  // 4级需要48级
                _ => 50
            };
        }

        /// <summary>
        /// 查找并消耗指定物品
        /// </summary>
        private static TUserItem FindAndConsumeItem(TPlayObject player, string itemName)
        {
            if (player?.m_ItemList == null)
                return null;

            for (int i = player.m_ItemList.Count - 1; i >= 0; i--)
            {
                var item = player.m_ItemList[i];
                if (item == null)
                    continue;

                string name = M2Share.UserEngine?.GetStdItemName(item.wIndex) ?? string.Empty;
                if (string.Equals(name, itemName, StringComparison.Ordinal))
                {
                    // 移除并释放物品
                    player.m_ItemList.RemoveAt(i);

                    // 记录日志
                    M2Share.AddGameDataLog(string.Join('\t', 10, player.m_sMapName,
                        player.m_nCurrX, player.m_nCurrY, player.m_sCharName, itemName,
                        unchecked((uint)item.MakeIndex), 1, "英雄合击技能升级"));

                    // 通知客户端删除物品

                    return item;
                }
            }

            return null;
        }

        /// <summary>
        /// 发送红色错误消息 (VMT+0xD4)
        /// </summary>
        private static void SendErrorMessage(HeroObject hero, string message)
        {
            if (hero.m_Master is TPlayObject master)
            {
                master.SysMsg(message, MsgColor.Red, MsgType.Hint);
            }
        }

        /// <summary>
        /// 发送红色消息
        /// </summary>
        private static void SendRedMessage(HeroObject hero, string message)
        {
            if (hero.m_Master is TPlayObject master)
            {
                master.SysMsg(message, MsgColor.Red, MsgType.Hint);
            }
        }

        /// <summary>
        /// 发送全局消息 (EA: 0x5F701C)
        /// </summary>
        private static void SendGlobalMessage(string message, ushort color, byte channel)
        {
            // 调用全局消息系统
            if (M2Share.UserEngine != null)
            {
                // 发送到所有在线玩家
                M2Share.UserEngine.SendBroadCastMsg(message, MsgType.System);
            }
        }
    }

    /// <summary>
    /// HeroObject 部分类扩展 - 合击技能相关字段
    /// </summary>
    public partial class HeroObject
    {
        // m_NativeUnionMagic is already defined in HeroObject.cs line 62
        // Removed duplicate definition to fix CS0102

        // m_Master is inherited from TBaseObject
        // Removed duplicate definition to fix CS0108
    }
}
