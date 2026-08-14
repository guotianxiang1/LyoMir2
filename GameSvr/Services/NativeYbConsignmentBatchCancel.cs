using SystemModule;

namespace GameSvr.Services
{
    /// <summary>
    /// 寄售批量取消外部回调 — native 0x006F1EB8.
    /// Switch on callback kind (edx 0..5, 0x6F1EE4 cmp/jmp table):
    ///   0 — announce batch count ("取消寄售订单数量:")
    ///   1 — per-order seller reclaim success/fail with order id + errorCode
    ///   2 — per-order cancel success/fail
    ///   3 — claim yuanbao for seller success/fail
    ///   4 — refund buyer
    /// Clears write-pending [player+0x18C8] via caller 0x006F1BE8 prologue.
    /// </summary>
    internal static class NativeYbConsignmentBatchCancel
    {
        internal const int CallbackAnnounceCount = 0;
        internal const int CallbackSellerReclaim = 1;
        internal const int CallbackCancelOrder = 2;
        internal const int CallbackClaimSellerYb = 3;
        internal const int CallbackRefundBuyer = 4;

        internal static void HandleCallback(TPlayObject player, int callbackKind,
            int orderId, int errorCode, int batchCount, string detail)
        {
            if (player == null) return;

            player.ClearNativeYbConsignWritePending();

            var message = callbackKind switch
            {
                CallbackAnnounceCount when batchCount > 0 =>
                    $"取消寄售订单数量: {batchCount}",
                CallbackAnnounceCount =>
                    "取消寄售订单数量: 0",
                CallbackSellerReclaim when orderId > 0 && errorCode == 0 =>
                    $"替卖家领取 {orderId} 号订单元宝成功",
                CallbackSellerReclaim when orderId > 0 =>
                    $"替卖家领取 {orderId} 号订单元宝失败 errorCode: {errorCode}",
                CallbackCancelOrder when orderId > 0 && errorCode == 0 =>
                    $"取消寄售 {orderId} 号订单成功",
                CallbackCancelOrder when orderId > 0 =>
                    $"取消寄售 {orderId} 号订单失败 errorCode: {errorCode}",
                CallbackClaimSellerYb when orderId > 0 && errorCode == 0 =>
                    $"替卖家领取 {orderId} 号订单元宝成功",
                CallbackClaimSellerYb when orderId > 0 =>
                    $"替卖家领取 {orderId} 号订单元宝失败 errorCode: {errorCode}",
                CallbackRefundBuyer when orderId > 0 =>
                    $"返还买家 {orderId} 号订单" +
                    (errorCode == 0 ? "成功" : $"失败 errorCode: {errorCode}"),
                _ => detail ?? string.Empty
            };

            if (string.IsNullOrEmpty(message)) return;

            player.SendMsg(player, Grobal2.RM_SYSMESSAGE, 0,
                0xDB, 0xFF, 0, message);
        }
    }
}
