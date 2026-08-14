using System;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        internal bool TakeNativeDiamond(int amount)
        {
            if (amount <= 0) return false;

            var result = TryTakeNativeBagItem("金刚石", amount);
            RefreshNativeLingFu();
            return result;
        }

        private bool TryTakeNativeBagItem(string itemName, int amount)
        {
            if (!TryResolveNativeItem(itemName, out var itemIndex, out var stdItem))
                return false;

            // CRAFT-14: Material availability check (Pass 1) scans LOW -> HIGH (ascending)
            // to match regular crafting (Merchant.cs:2029). Consumption (Pass 2) below
            // remains HIGH -> LOW (descending) matching native sub_74054C at 0x740596.
            var available = 0;
            for (var index = 0; index < m_ItemList.Count; index++)
            {
                var item = m_ItemList[index];
                if (item == null || item.wIndex != itemIndex) continue;

                available = unchecked(available +
                    (IsNativeDiamondPileItem(stdItem) ? item.Dura : 1));
                if (available >= amount) break;
            }

            if (available < amount) return false;

            var consumed = 0;
            for (var index = m_ItemList.Count - 1;
                 index >= 0 && consumed < amount;
                 index--)
            {
                var item = m_ItemList[index];
                if (item == null || item.wIndex != itemIndex) continue;

                if (IsNativeDiamondPileItem(stdItem))
                {
                    var remaining = amount - consumed;
                    AddNativeDiamondTakeLog(stdItem.Name, item.MakeIndex,
                        remaining, "NPC收取" + remaining + "个");
                    if (remaining < item.Dura)
                    {
                        consumed = unchecked(consumed + remaining);
                        item.Dura = (ushort)(item.Dura - remaining);
                        SendDefMessage(Grobal2.SM_BAGITEMDURACHG,
                            EnsureClientItemId(item), item.Dura, item.DuraMax, 0,
                            string.Empty);
                    }
                    else
                    {
                        consumed = unchecked(consumed + item.Dura);
                        m_ItemList.RemoveAt(index);
                        SendDelItems(item);
                        Dispose(item);
                    }
                }
                else
                {
                    consumed++;
                    m_ItemList.RemoveAt(index);
                    AddNativeDiamondTakeLog(stdItem.Name, item.MakeIndex, 1,
                        "NPC收取");
                    SendDelItems(item);
                    Dispose(item);
                }
            }

            WeightChanged();
            return true;
        }

        private static bool IsNativeDiamondPileItem(GoodItem stdItem)
        {
            // CRAFT-14: native pile test is the INSTANCE kind byte [item+0x14]==7
            // (TBasePileItem.Create @0x788118), NOT template StdMode. StdMode 7 is the
            // TCharm family (NativeItemFactory case 7), never a pile — the prior
            // `StdMode == 7 ||` wrongly treated charms/gems as stackable.
            return NativeItemFactory.IsPileItem(stdItem);
        }

        private static bool TryResolveNativeItem(string itemName,
            out ushort itemIndex, out GoodItem stdItem)
        {
            itemIndex = 0;
            stdItem = null;
            var userEngine = M2Share.UserEngine;
            if (userEngine == null || string.IsNullOrEmpty(itemName)) return false;
            var resolvedIndex = userEngine.GetStdItemIdx(itemName);
            if (resolvedIndex <= 0 || resolvedIndex > ushort.MaxValue)
                return false;
            itemIndex = unchecked((ushort)resolvedIndex);
            stdItem = userEngine.GetStdItem(resolvedIndex);
            return stdItem != null;
        }

        private void AddNativeDiamondTakeLog(string itemName, int makeIndex,
            int itemCount, string description)
        {
            M2Share.AddGameDataLog(string.Join('\t', 10, m_sMapName,
                m_nCurrX, m_nCurrY, m_sCharName, itemName,
                unchecked((uint)makeIndex), itemCount, description));
        }
    }
}
