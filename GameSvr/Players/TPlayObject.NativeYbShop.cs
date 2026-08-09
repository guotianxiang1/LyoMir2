using GameSvr.Mall;
using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        internal const string YbShopExchangeLimitMessage =
            "每次兑换不能超过1000张。\\ \\ <返回/@main>";
        internal const string YbShopInsufficientLocalBalanceMessage =
            "您身上没有那么多的元宝。\\ \\ <返回/@main>";
        internal const string YbShopSubmitFailedMessage =
            "元宝系统暂时关闭中...\\ \\ \\ <返回/@main>";
        internal const string YbShopGoodsMissingMessage =
            "[失败]: 不存在你想购买的物品: 灵符";

        public void ClientYBbuyLF(NormNpc npc, int amount)
        {
            if (amount is < 1 or > 1000)
            {
                SendNativeYbShopScriptDialog(npc, YbShopExchangeLimitMessage);
                return;
            }
            if (amount > m_nGameGold)
            {
                SendNativeYbShopScriptDialog(npc,
                    YbShopInsufficientLocalBalanceMessage);
                return;
            }
            if (!TryGetNativeMailRecipientId(out var userId))
            {
                SendNativeYbShopScriptDialog(npc, YbShopGoodsMissingMessage);
                return;
            }
            var buyLogId = NativeYbShopPurchaseStore.Begin(userId, m_sUserID,
                m_sCharName, amount);
            if (buyLogId <= 0)
            {
                SendNativeYbShopScriptDialog(npc, YbShopGoodsMissingMessage);
                return;
            }
            MallManager.Instance.InvalidateHotItems();

            NativeYuanbaoRequest request = null;
            request = NativeYuanbaoRequest.CreateYbShop(userId, m_sUserID,
                m_sCharName, amount, buyLogId,
                result => CompleteNativeYbShopPurchase(request, result));
            request.SetBeforeOuterCompletionLog(
                result => PrepareNativeYbShopYuanbaoCompletion(request, result));
            if (NativeYuanbaoManager.Enqueue(request)) return;

            SendNativeYbShopScriptDialog(npc, YbShopSubmitFailedMessage);
        }

        private static void PrepareNativeYbShopYuanbaoCompletion(
            NativeYuanbaoRequest request, NativeYuanbaoResult result)
        {
            if (result.ErrorCode != 0) return;
            var online = ResolveNativeYbShopPlayer(request);
            if (online == null) return;
            online.m_nGameGold = result.Balance;
            online.RefreshNativeLingFu();
        }

        private static void CompleteNativeYbShopPurchase(NativeYuanbaoRequest request,
            NativeYuanbaoResult result)
        {
            var online = ResolveNativeYbShopPlayer(request);
            if (result.ErrorCode != 0)
            {
                if (online != null)
                    online.SendNativeYbShopFailure(
                        "[失败]: " + NativeYuanbaoManager.GetErrorText(
                            result.ErrorCode));
                return;
            }
            if (online == null)
            {
                NativeYbShopPurchaseStore.SetStatusBestEffort(
                    request.OrderId, false);
                return;
            }

            if (!online.GrantNativeYbShopLingFu(request.Amount))
            {
                NativeYbShopPurchaseStore.SetStatusBestEffort(
                    request.OrderId, false);
                return;
            }

            NativeYbShopPurchaseStore.AddConsumptionBestEffort(
                online.m_sUserID, request.Amount);
            online.AddNativeYbShopCreditValue2(request.Amount);
            NativeYbShopPurchaseStore.SetStatusBestEffort(request.OrderId, true);
        }

        private static TPlayObject ResolveNativeYbShopPlayer(
            NativeYuanbaoRequest request)
        {
            if (request == null || request.UserId <= 0) return null;
            var userEngine = M2Share.UserEngine;
            if (userEngine == null) return null;

            var account = HUtil32.GbkEncoding.GetString(
                request.AccountBytes ?? Array.Empty<byte>()).TrimEnd('\0', ' ');
            var characterName = HUtil32.GbkEncoding.GetString(
                request.CharacterNameBytes ?? Array.Empty<byte>()).TrimEnd('\0', ' ');
            if (account.Length == 0 || characterName.Length == 0) return null;

            foreach (var candidate in userEngine.PlayObjects)
            {
                if (candidate == null || candidate.m_boGhost
                    || candidate.GetCachedNativeUserId() != request.UserId
                    || !string.Equals(candidate.m_sUserID, account,
                        StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(candidate.m_sCharName, characterName,
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                return candidate;
            }
            return null;
        }

        private bool GrantNativeYbShopLingFu(int amount)
        {
            if (amount <= 0) return false;
            lock (m_CreditCard.SyncRoot)
                m_nLingFu = unchecked(m_nLingFu + amount);

            var nickLinFuState = M2Share.NickLinFuState;
            IncNativeNickLinFu(amount, nickLinFuState.Multiplier,
                nickLinFuState.Enabled);
            M2Share.AddGameDataLog(string.Join('\t', 51, m_sMapName,
                m_nCurrX, m_nCurrY, m_sCharName,
                NativeYbShopPurchaseStore.LingFuGoodsName, 222222, amount,
                "商城购入"));
            NotifyPlayerActivePoint(1,
                NativeYbShopPurchaseStore.LingFuGoodsName,
                amount, 0);
            RefreshNativeLingFu();
            SendNativeYbShopSuccess(amount);
            return true;
        }

        private void AddNativeYbShopCreditValue2(int amount)
        {
            var service = M2Share.CreditCardService ??
                          NativeCreditCardService.Disabled;
            if (amount <= 0 || !service.MonthlyLimitedEnabled) return;
            lock (m_CreditCard.SyncRoot)
            {
                m_CreditCard.Value2 = unchecked(m_CreditCard.Value2 + amount);
                if (m_CreditCard.Value2 < 0) m_CreditCard.Value2 = 0;
                m_CreditCard.Dirty = true;
                m_CreditCard.DirtyVersion++;
            }
        }

        private void SendNativeYbShopScriptDialog(TBaseObject npc, string message)
        {
            if (npc != null)
            {
                m_NPC = npc;
                SendMsg(npc, Grobal2.RM_MERCHANTSAY, 0, 0, 0, 0,
                    npc.m_sCharName + '/' + message);
                return;
            }
            SendDefMessage(Grobal2.SM_MERCHANTSAY, 0, 0, 0, 0,
                "NPC/" + message);
        }

        private void SendNativeYbShopFailure(string message)
        {
            if (m_NPC != null)
            {
                SendNativeYbShopScriptDialog(m_NPC, message);
                return;
            }
            SendDefMessage(Grobal2.SM_MERCHANTSAY, 0, 0, 0, 0,
                "NPC/" + message);
        }

        private void SendNativeYbShopSuccess(int amount)
        {
            var message = "您成功购买了" + amount + "张灵符";
            if (m_NPC != null)
            {
                SendNativeYbShopScriptDialog(m_NPC,
                    message + "。 \\ \\<返回/@Main>");
                return;
            }
            SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, 0xFF, 0xFC, 0,
                message);
        }
    }
}
