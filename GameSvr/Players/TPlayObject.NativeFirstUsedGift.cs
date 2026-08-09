using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        internal const int NativeFirstUsedGiftSuccess = 0;
        internal const int NativeFirstUsedGiftBagFull = 1;
        internal const int NativeFirstUsedGiftSystemError = 2;
        internal const int NativeFirstUsedGiftReserveError = 3;
        internal const int NativeFirstUsedGiftNotQualified = 4;

        internal const string NativeFirstUsedGiftFirstItem = "聚灵珠(小)";
        internal const string NativeFirstUsedGiftSecondItem = "双倍宝典";

        // Dormant until the original LoginCenter entitlement authority and
        // the external 6108 service are available. PAS must not call this.
        internal int RunNativeFirstUsedGiftStateMachine(
            Func<string, int> tryGrantItem)
        {
            if (!m_boNativeFirstUsedGiftQualified
                || m_nNativeYbDividendConsumed <= 0
                || m_btFirstUsedGiftStage != 0)
                return NativeFirstUsedGiftNotQualified;

            if (m_ItemList.Count > 44)
                return NativeFirstUsedGiftReserveError;

            var result = NativeFirstUsedGiftSuccess;
            var firstResult = tryGrantItem(NativeFirstUsedGiftFirstItem);
            if (firstResult == NativeFirstUsedGiftSuccess)
                m_btFirstUsedGiftStage = 1;
            else
                result = NormalizeNativeFirstUsedGiftGrantResult(firstResult);

            var secondResult = tryGrantItem(NativeFirstUsedGiftSecondItem);
            if (secondResult == NativeFirstUsedGiftSuccess)
                m_btFirstUsedGiftStage = 2;
            else
                result = NormalizeNativeFirstUsedGiftGrantResult(secondResult);

            return result;
        }

        internal static string GetNativeFirstUsedGiftResultMessage(int result)
        {
            return result switch
            {
                NativeFirstUsedGiftSuccess => "领奖成功",
                NativeFirstUsedGiftBagFull => "[错误]：你的包裹空位不足",
                NativeFirstUsedGiftSystemError => "[错误]：系统错误",
                NativeFirstUsedGiftReserveError => "[错误]：请至少预留2个以上包裹位置",
                _ => "[错误]：您不符合领奖条件"
            };
        }

        private static int NormalizeNativeFirstUsedGiftGrantResult(int result)
        {
            return result == NativeFirstUsedGiftBagFull
                ? NativeFirstUsedGiftBagFull
                : NativeFirstUsedGiftSystemError;
        }

        private int TryGrantNativeFirstUsedGiftItem(string itemName)
        {
            if (M2Share.UserEngine == null)
                return NativeFirstUsedGiftSystemError;

            TUserItem item = null;
            if (!M2Share.UserEngine.CopyToUserItemFromName(itemName, ref item)
                || item == null)
                return NativeFirstUsedGiftSystemError;

            var stdItem = M2Share.UserEngine.GetStdItem(item.wIndex);
            if (stdItem == null)
            {
                Dispose(item);
                return NativeFirstUsedGiftSystemError;
            }

            if (!AddItemToBag(item))
            {
                Dispose(item);
                return NativeFirstUsedGiftBagFull;
            }

            SendAddItem(item);
            M2Share.AddGameDataLog(string.Join('\t', 53, m_sMapName,
                m_nCurrX, m_nCurrY, m_sCharName, stdItem.Name,
                item.MakeIndex, 1, "下家奖励"));
            return NativeFirstUsedGiftSuccess;
        }
    }
}
