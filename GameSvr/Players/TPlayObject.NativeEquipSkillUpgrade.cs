using System;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// 凝冰装备技能升级 — native sub_78988C @0x78988C.
    /// Requires item name "凝冰结晶" (@0x6AC87C), equipped item with 凝冰 skill
    /// (0x7467BC / 0x746818), consumes one crystal, bumps skill tier, Recalc
    /// (vtable +0x8C), SysMsg "恭喜你将凝冰lv1提升至凝冰lv2" @0x789988.
    /// </summary>
    public partial class TPlayObject
    {
        private const string NativeIceSkillCrystalName = "凝冰结晶";
        internal const uint NativeEquipIceSkillUpgradeEa = 0x0078988C;

        internal bool TryNativeEquipIceSkillUpgrade()
        {
            if (!TryFindBagItemByName(NativeIceSkillCrystalName, out var crystal))
            {
                SysMsg("对不起，你没有佩戴拥有凝冰技能的装备，或者凝冰技能已升级，或者凝",
                    MsgColor.Red, MsgType.Hint);
                return false;
            }

            if (!TryGetNativeEquippedIceSkill(out var magic))
            {
                SysMsg("对不起，你没有佩戴拥有凝冰技能的装备，或者凝冰技能已升级，或者凝",
                    MsgColor.Red, MsgType.Hint);
                return false;
            }

            if (magic.btLevel >= 1)
            {
                SysMsg("对不起，你没有佩戴拥有凝冰技能的装备，或者凝冰技能已升级，或者凝",
                    MsgColor.Red, MsgType.Hint);
                return false;
            }

            if (!DeleteBagItem(crystal))
                return false;

            magic.btLevel = 1;
            RecalcAbilitys();
            SysMsg("恭喜你将凝冰lv1提升至凝冰lv2", MsgColor.Green, MsgType.Hint);
            return true;
        }

        private bool TryGetNativeEquippedIceSkill(out TUserMagic magic)
        {
            magic = null;
            // 0x7467BC — scan use-items for magic id 191 (凝冰)
            const int nativeIceMagicId = 191;
            var list = m_MagicList;
            if (list == null)
                return false;

            for (var i = 0; i < list.Count; i++)
            {
                var m = list[i];
                if (m?.MagicInfo != null && m.MagicInfo.wMagicID == nativeIceMagicId)
                {
                    magic = m;
                    return true;
                }
            }

            return false;
        }

        private bool TryFindBagItemByName(string name, out TUserItem item)
        {
            item = null;
            var items = m_ItemList;
            if (items == null)
                return false;

            for (var i = items.Count - 1; i >= 0; i--)
            {
                var bagItem = items[i];
                if (bagItem == null)
                    continue;
                var std = M2Share.UserEngine?.GetStdItem(bagItem.wIndex);
                if (std != null && string.Equals(std.Name, name, StringComparison.Ordinal))
                {
                    item = bagItem;
                    return true;
                }
            }

            return false;
        }

        internal bool TryFindBagItemByStdName(string itemName, out TUserItem item)
            => TryFindBagItemByName(itemName, out item);

        internal bool DeleteBagItem(TUserItem item)
        {
            if (item == null || m_ItemList == null)
                return false;
            if (!m_ItemList.Remove(item))
                return false;
            SendDelItems(item);
            Dispose(item);
            return true;
        }
    }
}
