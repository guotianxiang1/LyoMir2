using System;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// 英雄合击4级升级系统（完整版）- 战神引擎 0x0078AF68
    /// 需要消耗"火龙之心"物品
    /// </summary>
    public partial class HeroObject
    {
        /// <summary>
        /// 升级英雄合击技能到4级（完整实现）
        /// </summary>
        public static bool NativeUpgradeUnionSkillLevel4(HeroObject hero)
        {
            if (hero == null)
                return false;

            try
            {
                // Phase 1: 类型检查
                if (hero.m_boGhost)
                    return false;

                // Phase 2: 获取合击技能对象 - 从技能列表中查找
                TUserMagic unionMagic = null;
                if (hero.m_HeroMagicList != null)
                {
                    foreach (var magic in hero.m_HeroMagicList)
                    {
                        if (magic != null && magic.wMagIdx >= 50 && magic.wMagIdx <= 55)
                        {
                            unionMagic = magic;
                            break;
                        }
                    }
                }

                // Phase 3: 检查技能对象存在且等级<4
                if (unionMagic == null)
                {
                    SendHeroMessage(hero, "你还没有学习合击技能", MsgColor.Red);
                    return false;
                }

                if (unionMagic.btLevel >= 4)
                {
                    SendHeroMessage(hero, "合击技能已达到最高等级", MsgColor.Red);
                    return false;
                }

                // Phase 4: 检查英雄等级要求
                int requiredLevel = unionMagic.btLevel switch
                {
                    0 => 40, 1 => 43, 2 => 45, 3 => 48, _ => 50
                };

                if (hero.HeroLevel < requiredLevel)
                {
                    string levelMsg = $"下一级合击技能提升需要英雄等级达到{requiredLevel}级";
                    SendHeroMessage(hero, levelMsg, MsgColor.Red);
                    return false;
                }

                // Phase 5: 查找并消耗"火龙之心"
                TPlayObject master = hero.m_Master as TPlayObject;
                if (master == null)
                    return false;

                const string FIRE_DRAGON_HEART = "火龙之心";
                var dragonHeart = FindMaterial(master, FIRE_DRAGON_HEART);
                if (dragonHeart == null)
                {
                    SendHeroMessage(hero, $"升级合击技能需要{FIRE_DRAGON_HEART}", MsgColor.Red);
                    return false;
                }

                // Phase 6: 升级技能等级
                byte newLevel = (byte)(unionMagic.btLevel + 1);
                unionMagic.btLevel = newLevel;

                // 消耗材料
                ConsumeMaterial(master, dragonHeart, "合击技能升级");

                // Phase 7: 格式化并发送成功消息
                string heroName = hero.m_sCharName;
                string successMsg = $"恭喜您的英雄{heroName}学会了{newLevel}级合击技能，威力大增！";

                // 发送全局消息
                SendBroadcast(successMsg);

                return true;
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[Exception] 英雄合击4级升级异常: {ex.Message}");
                return false;
            }
        }

        private static TUserItem FindMaterial(TPlayObject player, string itemName)
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

        private static void ConsumeMaterial(TPlayObject player, TUserItem item, string reason)
        {
            if (player?.m_ItemList == null || item == null)
                return;

            player.m_ItemList.Remove(item);

            string itemName = M2Share.UserEngine?.GetStdItemName(item.wIndex) ?? "Unknown";
            M2Share.AddGameDataLog(string.Join('\t', 10, player.m_sMapName,
                player.m_nCurrX, player.m_nCurrY, player.m_sCharName, itemName,
                unchecked((uint)item.MakeIndex), 1, reason));
        }

        private static void SendHeroMessage(HeroObject hero, string message, MsgColor color)
        {
            if (hero.m_Master is TPlayObject master)
            {
                master.SysMsg(message, color, MsgType.Hint);
            }
        }

        private static void SendBroadcast(string message)
        {
            if (M2Share.UserEngine != null)
            {
                M2Share.UserEngine.SendBroadCastMsg(message, MsgType.System);
            }
        }
    }
}
