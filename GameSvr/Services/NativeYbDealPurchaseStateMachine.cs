namespace GameSvr.Services
{
    public enum NativeYbDealPurchaseDisposition
    {
        BuyerDebitPending,
        BuyerCallbackIgnored,
        BuyerDebitFailed,
        SellerResolutionPending,
        SellerResolutionIgnored,
        SellerResolutionFailed,
        SellerCreditPending,
        SellerCallbackIgnored,
        SellerCreditFailed,
        BuyerDeliveryTargetMissing,
        DeliveryFailed,
        Completed
    }

    public enum NativeYbDealAuditOutcome
    {
        Begin,
        Success,
        Failure
    }

    public enum NativeYbDealParty
    {
        Buyer,
        Seller
    }

    public sealed class NativeYbDealPurchaseContext
    {
        public NativeYbDealPurchaseContext(int orderId, int credit)
        {
            OrderId = orderId;
            Credit = credit;
        }

        public int OrderId { get; }
        public int Credit { get; }
    }

    public interface INativeYbDealPurchaseExactHost
    {
        void WriteAudit(int stage, NativeYbDealAuditOutcome outcome);
        void RequestBuyerDebit(NativeYbDealPurchaseContext context);
        void WriteOrderStatusBestEffort(
            NativeYbDealPurchaseContext context, string status);
        void BeginSellerResolution(NativeYbDealPurchaseContext context);
        void RequestSellerCredit(NativeYbDealPurchaseContext context);
        bool TryDeliverItems(NativeYbDealPurchaseContext context);
        void SendBuyerResult(NativeYbDealPurchaseContext context, int result);
        void NotifyAccountFailure(NativeYbDealPurchaseContext context,
            NativeYbDealParty party, int errorCode);
        void ArchiveHistoryBestEffort(NativeYbDealPurchaseContext context);
        void DeleteActiveOrderBestEffort(
            NativeYbDealPurchaseContext context);
    }

    /// <summary>
    /// Exact, dormant model of the classic CM 1254 YB consignment callbacks.
    /// It does not model the separate CM 1350..1363 / YBDB 310..323 surface.
    /// It deliberately has no persistence, retry, correlation, or local-balance
    /// owner. The original heap queue invokes each callback once and drops
    /// queued nodes on process destruction; replaying a callback replays its
    /// side effects.
    /// </summary>
    public static class NativeYbDealPurchaseStateMachine
    {
        public const string BuyerDebitedStatus = "Confrim";
        public const string SellerCreditedStatus = "GivedSellerYB";
        public const string DeliveredStatus = "True";

        public const int ConsignmentBeginAuditStage = 12;
        public const int BuyerDebitAuditStage = 13;
        public const int SellerCreditAuditStage = 14;
        public const int DeliverySuccessAuditStage = 15;
        public const int DeliveryFailureAuditStage = 16;
        public const int ConsignmentEndAuditStage = 17;
        public const int SellerResolutionFailureResult = -6;

        public static NativeYbDealPurchaseDisposition BeginValidatedPurchase(
            NativeYbDealPurchaseContext context,
            INativeYbDealPurchaseExactHost host)
        {
            Validate(context, host);
            host.WriteAudit(ConsignmentBeginAuditStage,
                NativeYbDealAuditOutcome.Begin);
            host.WriteAudit(BuyerDebitAuditStage,
                NativeYbDealAuditOutcome.Begin);
            host.RequestBuyerDebit(context);
            return NativeYbDealPurchaseDisposition.BuyerDebitPending;
        }

        public static NativeYbDealPurchaseDisposition CompleteBuyerDebit(
            NativeYbDealPurchaseContext context,
            INativeYbDealPurchaseExactHost host, bool buyerOwnerAvailable,
            bool isSubtractCallback, int accountResult)
        {
            Validate(context, host);
            if (!buyerOwnerAvailable || !isSubtractCallback)
                return NativeYbDealPurchaseDisposition.BuyerCallbackIgnored;

            if (accountResult != 0)
            {
                host.WriteAudit(BuyerDebitAuditStage,
                    NativeYbDealAuditOutcome.Failure);
                host.NotifyAccountFailure(context, NativeYbDealParty.Buyer,
                    accountResult);
                return NativeYbDealPurchaseDisposition.BuyerDebitFailed;
            }

            host.WriteOrderStatusBestEffort(context, BuyerDebitedStatus);
            host.WriteAudit(BuyerDebitAuditStage,
                NativeYbDealAuditOutcome.Success);
            host.BeginSellerResolution(context);
            return NativeYbDealPurchaseDisposition.SellerResolutionPending;
        }

        public static NativeYbDealPurchaseDisposition CompleteSellerResolution(
            NativeYbDealPurchaseContext context,
            INativeYbDealPurchaseExactHost host, bool buyerOwnerAvailable,
            bool sellerResolved)
        {
            Validate(context, host);
            if (!buyerOwnerAvailable)
                return NativeYbDealPurchaseDisposition.SellerResolutionIgnored;

            if (!sellerResolved)
            {
                host.SendBuyerResult(context, SellerResolutionFailureResult);
                return NativeYbDealPurchaseDisposition.SellerResolutionFailed;
            }

            host.WriteAudit(SellerCreditAuditStage,
                NativeYbDealAuditOutcome.Begin);
            host.RequestSellerCredit(context);
            return NativeYbDealPurchaseDisposition.SellerCreditPending;
        }

        public static NativeYbDealPurchaseDisposition CompleteSellerCredit(
            NativeYbDealPurchaseContext context,
            INativeYbDealPurchaseExactHost host, bool isAddCallback,
            int accountResult, bool buyerTargetAvailable,
            bool sellerNoticeAvailable)
        {
            Validate(context, host);
            if (!isAddCallback)
            {
                if (buyerTargetAvailable && accountResult < 0)
                    host.SendBuyerResult(context, accountResult);
                return NativeYbDealPurchaseDisposition.SellerCallbackIgnored;
            }

            if (accountResult != 0)
            {
                host.WriteAudit(SellerCreditAuditStage,
                    NativeYbDealAuditOutcome.Failure);
                host.WriteAudit(ConsignmentEndAuditStage,
                    NativeYbDealAuditOutcome.Failure);
                if (sellerNoticeAvailable)
                {
                    host.NotifyAccountFailure(context,
                        NativeYbDealParty.Seller, accountResult);
                }
                if (buyerTargetAvailable && accountResult < 0)
                    host.SendBuyerResult(context, accountResult);
                return NativeYbDealPurchaseDisposition.SellerCreditFailed;
            }

            host.WriteOrderStatusBestEffort(context, SellerCreditedStatus);
            host.WriteAudit(SellerCreditAuditStage,
                NativeYbDealAuditOutcome.Success);

            if (!buyerTargetAvailable)
                return NativeYbDealPurchaseDisposition.BuyerDeliveryTargetMissing;

            if (!host.TryDeliverItems(context))
            {
                host.WriteAudit(DeliveryFailureAuditStage,
                    NativeYbDealAuditOutcome.Failure);
                host.WriteAudit(ConsignmentEndAuditStage,
                    NativeYbDealAuditOutcome.Failure);
                return NativeYbDealPurchaseDisposition.DeliveryFailed;
            }

            host.WriteOrderStatusBestEffort(context, DeliveredStatus);
            host.SendBuyerResult(context, context.OrderId);
            host.ArchiveHistoryBestEffort(context);
            host.DeleteActiveOrderBestEffort(context);
            host.WriteAudit(DeliverySuccessAuditStage,
                NativeYbDealAuditOutcome.Success);
            host.WriteAudit(ConsignmentEndAuditStage,
                NativeYbDealAuditOutcome.Success);
            return NativeYbDealPurchaseDisposition.Completed;
        }

        private static void Validate(NativeYbDealPurchaseContext context,
            INativeYbDealPurchaseExactHost host)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (host == null) throw new ArgumentNullException(nameof(host));
        }
    }
}
