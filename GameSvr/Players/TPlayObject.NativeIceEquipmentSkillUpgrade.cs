using System;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// 凝冰装备技能升级系统 - 战神引擎 0x0078988C
    /// MVI - 最小可行实现
    /// </summary>
    public partial class TPlayObject
    {
        // 战神引擎常量
        private const string ICE_MAGIC_DRAGON_CRYSTAL = "魔龙冰晶";
        private const int ICE_MAX_EQUIP_LEVEL = 10;

        /// <summary>
        /// 凝冰装备技能升级
        /// </summary>
        internal bool NativeUpgradeIceEquipmentSkill(int equipSlot)
        {
            if (equipSlot < 0 || equipSlot >= 13)
                return false;

            try
            {
                // Phase 1: 获取装备
                var equipment = (m_UseItems != null && equipSlot < m_UseItems.Length) ? m_UseItems[equipSlot] : null;
                if (equipment == null)
                {
                    SysMsg("该装备栏位没有装备", MsgColor.Red, MsgType.Hint);
                    return false;
                }

                // Phase 2: 检查是否是凝冰装备
                string itemName = M2Share.UserEngine?.GetStdItemName(equipment.wIndex) ?? "";
                if (!itemName.Contains("凝冰"))
                {
                    SysMsg("只有凝冰装备才能升级", MsgColor.Red, MsgType.Hint);
                    return false;
                }

                // Phase 3: 检查当前等级 (MVI: simplified)
                int currentLevel = 0; // TODO: read from item
                if (currentLevel >= ICE_MAX_EQUIP_LEVEL)
                {
                    SysMsg("凝冰装备已达到最高等级", MsgColor.Red, MsgType.Hint);
                    return false;
                }

                // Phase 4: 查找并消耗材料
                TUserItem crystal = null;
                if (m_ItemList != null)
                {
                    foreach (var item in m_ItemList)
                    {
                        if (item == null) continue;
                        string name = M2Share.UserEngine?.GetStdItemName(item.wIndex) ?? "";
                        if (string.Equals(name, ICE_MAGIC_DRAGON_CRYSTAL, StringComparison.Ordinal))
                        {
                            crystal = item;
                            break;
                        }
                    }
                }

                if (crystal == null)
                {
                    SysMsg($"升级需要{ICE_MAGIC_DRAGON_CRYSTAL}", MsgColor.Red, MsgType.Hint);
                    return false;
                }

                // Phase 5: 计算成功率
                int successRate = currentLevel switch
                {
                    0 => 90, 1 => 80, 2 => 70, 3 => 60, 4 => 50,
                    5 => 40, 6 => 30, 7 => 20, 8 => 10, 9 => 5, _ => 1
                };

                int roll = M2Share.RandomNumber.Random(100);

                // 消耗材料
                m_ItemList?.Remove(crystal);
                M2Share.AddGameDataLog(string.Join('\t', 10, m_sMapName,
                    m_nCurrX, m_nCurrY, m_sCharName, ICE_MAGIC_DRAGON_CRYSTAL,
                    unchecked((uint)crystal.MakeIndex), 1, "凝冰装备升级"));

                if (roll < successRate)
                {
                    // 成功升级 (TODO: write level back to item)
                    SysMsg($"凝冰装备升级成功，当前等级{currentLevel + 1}", MsgColor.Green, MsgType.Hint);
                    return true;
                }
                else
                {
                    SysMsg("凝冰装备升级失败", MsgColor.Red, MsgType.Hint);
                    return false;
                }
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[Exception] 凝冰装备升级异常: {ex.Message}");
                return false;
            }
        }
    }
}
