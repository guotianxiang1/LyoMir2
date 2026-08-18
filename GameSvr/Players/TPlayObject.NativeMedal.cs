using SystemModule;
using GameSvr.PasEngine;

namespace GameSvr
{
    public partial class TPlayObject
    {
        internal const string NativeMedalBagFullMessage =
            "你无法携带更多的物品";
        internal const string NativeMedalRyInsufficientMessage =
            "你的荣誉值，声望值均不符合条件！";
        internal const string NativeMedalSwInsufficientMessage =
            "你的声望值不够！";
        internal const int NativeMedalSwFee = 640;

        internal void ExchangeNativeMedalByRy(NormNpc npc,
            string selectorText)
        {
            var selector = ParseNativeMedalSelector(selectorText);
            var itemIndex = unchecked(310 + selector);
            if (!IsNativeRyMedalIndex(itemIndex)) return;
            if (!IsEnoughBag())
            {
                SendNativeMedalDialog(npc, NativeMedalBagFullMessage);
                return;
            }

            if (!TryCreateNativeMedal(itemIndex, out var item, out var stdItem))
                return;

            var fee = 80 * stdItem.Shape;
            if (fee > m_nShengWan)
            {
                Dispose(item);
                SendNativeMedalDialog(npc, NativeMedalRyInsufficientMessage);
                return;
            }

            m_nShengWan = unchecked(m_nShengWan - fee);
            WriteNativeMedalFeeLog(fee);
            if (!AddItemToBag(item))
            {
                Dispose(item);
                return;
            }

            CompleteNativeMedalExchange(npc, item, stdItem.Name);
        }

        internal void ExchangeNativeMedalBySw(NormNpc npc,
            string selectorText)
        {
            if (m_nShengWan < NativeMedalSwFee)
            {
                SendNativeMedalDialog(npc, NativeMedalSwInsufficientMessage);
                return;
            }

            var selector = ParseNativeMedalSelector(selectorText);
            var itemIndex = unchecked(646 + selector);
            if (!IsNativeSwMedalIndex(itemIndex)) return;
            if (!IsEnoughBag())
            {
                SendNativeMedalDialog(npc, NativeMedalBagFullMessage);
                return;
            }

            if (!TryCreateNativeMedal(itemIndex, out var item, out var stdItem))
                return;

            m_nShengWan = unchecked(m_nShengWan - NativeMedalSwFee);
            if (!AddItemToBag(item))
            {
                Dispose(item);
                return;
            }

            WriteNativeMedalFeeLog(NativeMedalSwFee);
            CompleteNativeMedalExchange(npc, item, stdItem.Name);
        }

        private static bool IsNativeRyMedalIndex(int itemIndex) =>
            itemIndex is >= 311 and <= 330 or >= 4335 and <= 4338;

        private static bool IsNativeSwMedalIndex(int itemIndex) =>
            itemIndex is >= 697 and <= 701 or 4339;

        private static bool TryCreateNativeMedal(int itemIndex,
            out TUserItem item, out GoodItem stdItem)
        {
            item = null;
            stdItem = null;
            var userEngine = M2Share.UserEngine;
            if (userEngine == null) return false;
            stdItem = userEngine.GetStdItem(itemIndex);
            if (stdItem == null || string.IsNullOrEmpty(stdItem.Name) ||
                NativeItemFactory.GetClassName(stdItem) == null)
                return false;

            // Native mints through the class factory sub_74C338, so the pile
            // constructor's `mov word [esi+0x26],1` @0x788112 wins over the root
            // constructor's Dura = DuraMax @0x7837E2-E6 whenever the class descends
            // from TBasePileItem. Medal indices are not piles today, but the seed
            // must follow the class, not the caller.
            item = new TUserItem
            {
                wIndex = (ushort)itemIndex,
                MakeIndex = M2Share.GetItemNumber(),
                Dura = NativeItemFactory.IsPileItem(stdItem)
                    ? (ushort)1
                    : stdItem.DuraMax,
                DuraMax = stdItem.DuraMax
            };
            NativeSpecialDropItemRollCore.HydrateConstructorState(item,
                stdItem);
            return true;
        }

        private static int ParseNativeMedalSelector(string selectorText) =>
            PasApiBridge.TryParseNativeDelphiInteger(selectorText,
                out var selector)
                ? selector
                : 0;

        private void CompleteNativeMedalExchange(NormNpc npc, TUserItem item,
            string itemName)
        {
            SendAddItem(item);
            M2Share.AddGameDataLog(string.Join('\t', 9, m_sMapName,
                m_nCurrX, m_nCurrY, m_sCharName, itemName, item.MakeIndex, 1,
                npc?.m_sCharName ?? string.Empty));
            SendNativeMedalDialog(npc,
                "恭喜你兑换[" + itemName + "]成功！");
        }

        private void WriteNativeMedalFeeLog(int fee)
        {
            M2Share.AddGameDataLog(string.Join('\t', 37, m_sMapName,
                m_nCurrX, m_nCurrY, m_sCharName, "声望值", 33333333, fee,
                "系统消耗"));
        }

        private void SendNativeMedalDialog(NormNpc npc, string message)
        {
            if (npc == null) return;
            m_NPC = npc;
            SendMsg(npc, Grobal2.RM_MERCHANTSAY, 0, 0, 0, 0,
                (npc.m_sCharName ?? string.Empty) + "/" + message);
        }
    }
}
