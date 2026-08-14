using System;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// 首饰升级 — native sub_6D68AC @0x6D68AC (NPC parameter 3..8).
    /// Faithful gates reproduced:
    ///   NPC present (0x75EC20), player alive (0x6C7D88), NPC alive
    ///   Collect up to 5 "黑铁矿石" from bag (0x6D69E2 name match)
    ///   Charge message "首饰升级收取" @0x6D6DC0 before deduct
    ///   Success "你的首饰升级成功" @0x6D6DD8
    /// Upgrade table / item mutation at 0x6D6A3A+ depends on runtime config —
    /// withheld when recipe cannot be resolved (fail-closed).
    /// </summary>
    public partial class TPlayObject
    {
        private const string NativeJewelryUpgradeOreName = "黑铁矿石";
        private const int NativeJewelryUpgradeMaxOre = 5;
        private const int NativeJewelryUpgradeMinParam = 3;
        private const int NativeJewelryUpgradeMaxParam = 8;

        internal bool TryNativeJewelryUpgrade(NormNpc npc, int upgradeKind)
        {
            if (npc == null || npc.m_boGhost)
                return false;

            if (m_boDeath || m_boGhost)
                return false;

            if (upgradeKind < NativeJewelryUpgradeMinParam
                || upgradeKind > NativeJewelryUpgradeMaxParam)
                return false;

            if (!TryCollectNativeJewelryUpgradeOre(out var oreDura))
            {
                SysMsg("缺少黑铁矿石，无法升级首饰。", MsgColor.Red, MsgType.Hint);
                return false;
            }

            SysMsg("首饰升级收取", MsgColor.Green, MsgType.Hint);

            if (!TryApplyNativeJewelryUpgrade(upgradeKind, oreDura))
            {
                SysMsg("首饰升级失败。", MsgColor.Red, MsgType.Hint);
                return false;
            }

            SysMsg("你的首饰升级成功", MsgColor.Green, MsgType.Hint);
            return true;
        }

        private bool TryCollectNativeJewelryUpgradeOre(out int totalDura)
        {
            totalDura = 0;
            var items = m_ItemList;
            if (items == null)
                return false;

            var collected = 0;
            for (var i = items.Count - 1; i >= 0 && collected < NativeJewelryUpgradeMaxOre; i--)
            {
                var item = items[i];
                if (item == null)
                    continue;

                var std = M2Share.UserEngine?.GetStdItem(item.wIndex);
                if (std == null
                    || !string.Equals(std.Name, NativeJewelryUpgradeOreName,
                        StringComparison.Ordinal))
                    continue;

                totalDura += item.Dura;
                SendDelItems(item);
                items.RemoveAt(i);
                Dispose(item);
                collected++;
            }

            return collected > 0;
        }

        /// <summary>
        /// Item result mutation needs the native upgrade table keyed by
        /// upgradeKind — not derivable from the image. Fail-closed unless a
        /// future config loader supplies the mapping.
        /// </summary>
        private bool TryApplyNativeJewelryUpgrade(int upgradeKind, int oreDura)
        {
            // 0x6D6982: effective level gate (dec/sub 3 / jae reject) ties the
            // upgrade tier to player level bands — without the item-target half
            // at 0x6D6A3A we cannot mutate equipment faithfully.
            return false;
        }
    }
}
