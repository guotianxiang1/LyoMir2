using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        internal const string NativeYbDealLogHeader =
            " \\ 最后一笔购买记录：\\";
        internal const string NativeYbDealLogEmpty = "没有交易记录";
        internal const string NativeYbDealLogNoInfo =
            " 没有交易信息。 \\ \\<返回/@Main>";

        /// <summary>
        /// 元宝寄售最后一笔购买记录 NPC — native 0x00637DBC.
        /// Gate: script param &gt;= 0xAF; player online; builds formatted text then
        /// sub_6D40A0 merchant dialog. Empty store yields 0x637F78 "没有交易记录".
        /// </summary>
        internal string BuildNativeYbLastDealLogText(string sellerFilter,
            string buyerFilter)
        {
            var record = NativeYbDealLogQuery.TryGetLastBuyerDeal(m_sCharName);
            if (record == null)
                return NativeYbDealLogEmpty;

            if (!string.IsNullOrEmpty(sellerFilter)
                && !string.Equals(record.SellerName, sellerFilter,
                    System.StringComparison.OrdinalIgnoreCase))
                return NativeYbDealLogNoInfo;

            if (!string.IsNullOrEmpty(buyerFilter)
                && !string.Equals(record.BuyerName, buyerFilter,
                    System.StringComparison.OrdinalIgnoreCase))
                return NativeYbDealLogNoInfo;

            return NativeYbDealLogHeader + record.FormatLine();
        }

        internal void ShowNativeYbLastDealLog(NormNpc npc, int scriptParam,
            string sellerFilter, string buyerFilter)
        {
            if (npc == null || scriptParam < 0xAF) return;
            m_NPC = npc;
            var text = BuildNativeYbLastDealLogText(sellerFilter, buyerFilter);
            SendNativeYbNpcDialog(npc, text);
        }
    }

    internal static class NativeYbDealLogQuery
    {
        internal sealed class LastDealRecord
        {
            internal string SellerName { get; init; } = string.Empty;
            internal string BuyerName { get; init; } = string.Empty;
            internal int Credit { get; init; }
            internal int ItemCount { get; init; }

            internal string FormatLine() =>
                $"卖家:{SellerName} 买家:{BuyerName} 价格:{Credit} 数量:{ItemCount}";
        }

        internal static LastDealRecord TryGetLastBuyerDeal(string charName)
        {
            if (string.IsNullOrEmpty(charName)) return null;
            var page = NativeYbConsignmentQuery.Store.Page(
                NativeYbConsignmentQuery.CmBuyerHistory, charName, 1);
            if (page == null || page.Count <= 0) return null;
            var row = page[0];
            return new LastDealRecord
            {
                SellerName = row.CounterpartyName,
                BuyerName = charName,
                Credit = row.Credit,
                ItemCount = row.ItemCount
            };
        }
    }
}
