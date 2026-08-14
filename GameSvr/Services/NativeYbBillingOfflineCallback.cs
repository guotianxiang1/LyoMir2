using SystemModule;

namespace GameSvr.Services
{
    /// <summary>
    /// 元宝预冻失败 / 计费返回时玩家离线 — native 0x0063BF9C.
    /// 账号解冻扣费失败 — native 0x0063C0C0.
    /// Both resolve role from billing frame +0x20, GetPlayObject; online path delegates
    /// to sub_6E3D14 / sub_6E3FAC; offline/failure writes AddGameDataLog (0x79D3D8 dx=0x2D).
    /// </summary>
    internal static class NativeYbBillingOfflineCallback
    {
        internal const string PrefreezeFailLog = "预冻返回失败";
        internal const string BillingOfflineLog = "计费返回时玩家已离线";
        internal const string UnfreezeFailLog = "解冻失败";
        internal const string UnfreezeChargeFailLog = "解冻扣费失败";
        internal const int GameDataLogAction = 0x2D;

        internal static void HandlePrefreezeBillingReturn(string roleName,
            string accountId, int billingGeneration, bool billingSucceeded,
            TPlayObject online)
        {
            if (online != null && billingSucceeded)
            {
                online.ApplyNativePrefreezeBillingSuccess(billingGeneration);
                return;
            }

            var reason = online == null ? BillingOfflineLog : PrefreezeFailLog;
            WriteBillingFailLog(roleName, accountId, reason);
            online?.ApplyNativePrefreezeBillingFailure(billingGeneration);
        }

        internal static void HandleAccountUnfreezeReturn(string roleName,
            string accountId, int resultCode, TPlayObject online)
        {
            if (online != null && resultCode > 0)
            {
                online.ApplyNativeAccountUnfreezeSuccess();
                return;
            }

            var reason = resultCode > 0 ? UnfreezeFailLog : UnfreezeChargeFailLog;
            WriteBillingFailLog(roleName, accountId, reason);
        }

        private static void WriteBillingFailLog(string roleName, string accountId,
            string reason)
        {
            M2Share.AddGameDataLog(string.Join('\t', GameDataLogAction,
                string.Empty, 0, 0, roleName ?? string.Empty,
                accountId ?? string.Empty, 0, 0, reason));
        }
    }
}
