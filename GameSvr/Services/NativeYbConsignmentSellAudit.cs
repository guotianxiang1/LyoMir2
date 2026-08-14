using System.Globalization;
using SystemModule;

namespace GameSvr.Services
{
    /// <summary>
    /// 元宝交易行出售审计 — native 0x0063CA4C.
    /// Formats `交易行出售: [%s] 数量: %d, 总价: %.1f元宝` (0x63CC44) then
    /// AddGameDataLog via sub_79D3D8 (dx=0x2D).
    /// Price divisor literal at 0x63CC38 = 100.0 (0x6384CA fdiv pattern family).
    /// </summary>
    internal static class NativeYbConsignmentSellAudit
    {
        internal const double PriceDivisor = 100.0;
        internal const int GameDataLogAction = 0x2D;

        internal static void WriteConsignmentSellLog(TPlayObject seller,
            string itemName, int quantity, int totalCredit)
        {
            if (seller == null || string.IsNullOrEmpty(itemName) || quantity <= 0)
                return;

            var totalYuanbao = totalCredit / PriceDivisor;
            var line = string.Format(CultureInfo.InvariantCulture,
                "交易行出售: [{0}] 数量: {1}, 总价: {2:F1}元宝",
                itemName, quantity, totalYuanbao);

            M2Share.AddGameDataLog(string.Join('\t', GameDataLogAction,
                seller.m_sMapName, seller.m_nCurrX, seller.m_nCurrY,
                seller.m_sCharName, itemName, quantity, totalCredit, line));
        }
    }
}
