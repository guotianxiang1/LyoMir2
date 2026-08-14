using System;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// 合击子技能升级系统 - 战神引擎 0x006CE6F4
    /// MVI - 最小可行实现
    /// </summary>
    public partial class HeroObject
    {
        // 战神引擎常量
        private const string FIRE_CLOUD_FRAGMENT = "火云石碎片";

        /// <summary>
        /// 升级合击子技能
        /// </summary>
        public static bool NativeUpgradeUnionSubSkill(HeroObject hero, int skillSlot)
        {
            if (hero == null)
                return false;

            try
            {
                // Phase 1: 验证技能槽位
                if (skillSlot < 0 || skillSlot >= 3)
                    return false;

                // Phase 2: 检查英雄类型
                if (hero.m_boGhost)
                    return false;

                // Phase 3: 获取合击技能对象
                var unionMagic = hero.m_HeroMagicList?.Count > 0 ? hero.m_HeroMagicList[0] : null;
                if (unionMagic == null)
                {
                    SendHeroMsg(hero, "你还没有学习合击技能", MsgColor.Red);
                    return false;
                }

                // Phase 4: 检查当前等级
                if (unionMagic.btLevel >= 4)
                {
                    SendHeroMsg(hero, "合击技能已达到最高等级", MsgColor.Red);
                    return false;
                }

                // Phase 5: 查找并消耗材料
                TPlayObject master = hero.m_Master as TPlayObject;
                if (master == null)
                    return false;

                var fragment = FindHeroMaterial(master, FIRE_CLOUD_FRAGMENT);
                if (fragment == null)
                {
                    SendHeroMsg(hero, $"升级需要{FIRE_CLOUD_FRAGMENT}", MsgColor.Red);
                    return false;
                }

                // Phase 6: 计算成功率
                int successRate = unionMagic.btLevel switch
                {
                    0 => 80, 1 => 60, 2 => 40, 3 => 20, _ => 10
                };

                int roll = M2Share.RandomNumber.Random(100);

                // 消耗材料
                ConsumeHeroMaterial(master, fragment, "合击子技能升级");

                if (roll < successRate)
                {
                    // 成功升级
                    unionMagic.btLevel++;
                    SendHeroMsg(hero, $"合击子技能升级成功，当前等级{unionMagic.btLevel}", MsgColor.Green);
                    return true;
                }
                else
                {
                    // 失败
                    SendHeroMsg(hero, "合击子技能升级失败", MsgColor.Red);
                    return false;
                }
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[Exception] 合击子技能升级异常: {ex.Message}");
                return false;
            }
        }

        private static TUserItem FindHeroMaterial(TPlayObject player, string itemName)
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

        private static void ConsumeHeroMaterial(TPlayObject player, TUserItem item, string reason)
        {
            if (player?.m_ItemList == null || item == null)
                return;

            player.m_ItemList.Remove(item);

            string itemName = M2Share.UserEngine?.GetStdItemName(item.wIndex) ?? "Unknown";
            M2Share.AddGameDataLog(string.Join('\t', 10, player.m_sMapName,
                player.m_nCurrX, player.m_nCurrY, player.m_sCharName, itemName,
                unchecked((uint)item.MakeIndex), 1, reason));
        }

        private static void SendHeroMsg(HeroObject hero, string message, MsgColor color)
        {
            if (hero.m_Master is TPlayObject master)
            {
                master.SysMsg(message, color, MsgType.Hint);
            }
        }
    }
}
