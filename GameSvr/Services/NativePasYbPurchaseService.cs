using System.Collections.Concurrent;
using GameSvr.PasEngine;
using SystemModule;

namespace GameSvr.Services
{
    internal enum NativePasYbPurchaseRoute : byte
    {
        Npc = 0,
        TaskDispatch = 1,
        YbShop = 2
    }

    internal readonly struct NativePasYbValidatedArguments
    {
        internal string CallbackName { get; }
        internal byte[] CallbackBytes { get; }
        internal string Descriptor { get; }
        internal byte[] DescriptorBytes { get; }
        internal int TotalCost { get; }

        internal NativePasYbValidatedArguments(string callbackName,
            byte[] callbackBytes, string descriptor, byte[] descriptorBytes,
            int totalCost)
        {
            CallbackName = callbackName;
            CallbackBytes = callbackBytes;
            Descriptor = descriptor;
            DescriptorBytes = descriptorBytes;
            TotalCost = totalCost;
        }
    }

    internal static class NativePasYbPurchaseValidation
    {
        internal const int MaximumTotalExclusive = 65535;
        internal const int MaximumCallbackBytes = 13;
        internal const int MaximumDescriptorBytes = 20;

        internal static bool TryValidateNormal(string callbackName, int vsId,
            int unitPrice, int quantity,
            out NativePasYbValidatedArguments arguments)
        {
            arguments = default;
            return vsId > 20000 && TryValidateCommon(callbackName,
                string.Empty, unitPrice, quantity, out arguments);
        }

        internal static bool IsSupportedNormalNpc(TBaseObject npc)
        {
            return npc != null && npc.m_btRaceServer is 0 or Grobal2.RC_NPC;
        }

        internal static bool TryValidateYbShop(byte executionTag,
            string callbackName, string descriptor, int unitPrice,
            int quantity, out NativePasYbValidatedArguments arguments)
        {
            arguments = default;
            return executionTag != (byte)NativePasYbPurchaseRoute.Npc
                   && TryValidateCommon(callbackName, descriptor, unitPrice,
                       quantity, out arguments);
        }

        private static bool TryValidateCommon(string callbackName,
            string descriptor, int unitPrice, int quantity,
            out NativePasYbValidatedArguments arguments)
        {
            arguments = default;
            if (unitPrice <= 0 || quantity <= 0 || callbackName == null)
                return false;

            var total = (long)unitPrice * quantity;
            if (total <= 0 || total >= MaximumTotalExclusive)
                return false;

            var callbackNameBytes = HUtil32.GbkEncoding.GetBytes(callbackName);
            if (callbackNameBytes.Length is < 1 or > MaximumCallbackBytes)
                return false;

            var callbackBytes = new byte[callbackNameBytes.Length + 1];
            callbackBytes[0] = (byte)'@';
            Buffer.BlockCopy(callbackNameBytes, 0, callbackBytes, 1,
                callbackNameBytes.Length);

            var descriptorBytes = HUtil32.GbkEncoding.GetBytes(
                descriptor ?? string.Empty);
            if (descriptorBytes.Length > MaximumDescriptorBytes)
                descriptorBytes = descriptorBytes[..MaximumDescriptorBytes];
            var callback = HUtil32.GbkEncoding.GetString(callbackNameBytes);
            var truncatedDescriptor = HUtil32.GbkEncoding.GetString(
                descriptorBytes);
            arguments = new NativePasYbValidatedArguments(callback,
                callbackBytes, truncatedDescriptor, descriptorBytes,
                (int)total);
            return true;
        }
    }

    internal sealed class NativePasYbPurchase
    {
        internal NativePasYbPurchaseRoute Route { get; }
        internal byte ExecutionTag => (byte)Route;
        internal long UserId { get; }
        internal string AccountName { get; }
        internal string CharacterName { get; }
        internal byte[] AccountBytes { get; }
        internal byte[] CharacterNameBytes { get; }
        internal string CallbackName { get; }
        internal byte[] CallbackBytes { get; }
        internal string Descriptor { get; }
        internal byte[] DescriptorBytes { get; }
        internal int VsId { get; }
        internal int UnitPrice { get; }
        internal int Quantity { get; }
        internal int TotalCost { get; }
        internal int BalanceSnapshot { get; }
        internal WeakReference<TPlayObject> OriginalPlayer { get; }
        internal NpcPasScriptInteractionHandle NpcInteraction { get; }
        internal int ScriptLogId { get; set; }

        internal NativePasYbPurchase(NativePasYbPurchaseRoute route,
            long userId, string accountName, string characterName,
            NativePasYbValidatedArguments arguments, int vsId, int unitPrice,
            int quantity, int balanceSnapshot, TPlayObject originalPlayer,
            NpcPasScriptInteractionHandle npcInteraction)
        {
            Route = route;
            UserId = userId;
            AccountName = accountName ?? string.Empty;
            CharacterName = characterName ?? string.Empty;
            AccountBytes = EncodeShortString(AccountName, 20);
            CharacterNameBytes = EncodeShortString(CharacterName, 15);
            CallbackName = arguments.CallbackName;
            CallbackBytes = arguments.CallbackBytes.ToArray();
            Descriptor = arguments.Descriptor;
            DescriptorBytes = arguments.DescriptorBytes.ToArray();
            VsId = vsId;
            UnitPrice = unitPrice;
            Quantity = quantity;
            TotalCost = arguments.TotalCost;
            BalanceSnapshot = balanceSnapshot;
            OriginalPlayer = originalPlayer == null
                ? null
                : new WeakReference<TPlayObject>(originalPlayer);
            NpcInteraction = npcInteraction;
        }

        private static byte[] EncodeShortString(string value, int maxBytes)
        {
            var bytes = HUtil32.GbkEncoding.GetBytes(value ?? string.Empty);
            return bytes.Length <= maxBytes ? bytes : bytes[..maxBytes];
        }
    }

    internal interface INativePasYbPurchaseStore
    {
        int Begin(NativePasYbPurchase purchase);
        void SetTrueBestEffort(int scriptLogId);
    }

    internal interface INativePasYbDebitQueue
    {
        bool Enqueue(NativePasYbPurchase purchase,
            Action<NativeYuanbaoResult> beforeCompletion,
            Action<NativeYuanbaoResult> completion);
    }

    internal interface INativePasYbPurchaseRuntime
    {
        void ApplyBalance(NativePasYbPurchase purchase,
            NativeYuanbaoResult result);
        void ReportFailure(NativePasYbPurchase purchase, int errorCode);
        bool InvokeCallback(NativePasYbPurchase purchase);
    }

    internal sealed class NativePasYbPurchaseTransaction
    {
        internal const int SnapshotInsufficient = -99;

        private readonly ConcurrentDictionary<long, NativePasYbPurchase> _pending
            = new();
        private readonly INativePasYbPurchaseStore _store;
        private readonly INativePasYbDebitQueue _debitQueue;
        private readonly INativePasYbPurchaseRuntime _runtime;
        private readonly Action<Action> _postCompletion;

        internal int PendingCount => _pending.Count;

        internal NativePasYbPurchaseTransaction(
            INativePasYbPurchaseStore store,
            INativePasYbDebitQueue debitQueue,
            INativePasYbPurchaseRuntime runtime,
            Action<Action> postCompletion)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _debitQueue = debitQueue ??
                          throw new ArgumentNullException(nameof(debitQueue));
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _postCompletion = postCompletion ??
                              throw new ArgumentNullException(nameof(postCompletion));
        }

        internal bool TryReserve(NativePasYbPurchase purchase)
        {
            return purchase != null && _pending.TryAdd(purchase.UserId, purchase);
        }

        internal void Stage(NativePasYbPurchase purchase)
        {
            if (!IsPending(purchase)) return;
            if ((long)purchase.BalanceSnapshot - purchase.TotalCost < 0)
            {
                FailBeforeDebit(purchase, SnapshotInsufficient);
                return;
            }

            int scriptLogId;
            try
            {
                scriptLogId = _store.Begin(purchase);
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage(
                    "[NativePasYbPurchase] begin failed: " + ex.Message);
                scriptLogId = -1;
            }
            if (scriptLogId <= 0)
            {
                FailBeforeDebit(purchase, NativeYuanbaoManager.SqlFailure);
                return;
            }

            purchase.ScriptLogId = scriptLogId;
            bool accepted;
            try
            {
                accepted = _debitQueue.Enqueue(purchase,
                    result => BeforeDebitCompletion(purchase, result),
                    result => CompleteDebit(purchase, result));
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage(
                    "[NativePasYbPurchase] debit enqueue failed: " + ex.Message);
                accepted = false;
            }
            if (!accepted)
                FailBeforeDebit(purchase, NativeYuanbaoManager.SqlFailure);
        }

        private void BeforeDebitCompletion(NativePasYbPurchase purchase,
            NativeYuanbaoResult result)
        {
            if (result.ErrorCode == 0 && IsPending(purchase))
                _runtime.ApplyBalance(purchase, result);
        }

        private void CompleteDebit(NativePasYbPurchase purchase,
            NativeYuanbaoResult result)
        {
            if (!TryRelease(purchase)) return;
            if (result.ErrorCode != 0)
            {
                _runtime.ReportFailure(purchase, result.ErrorCode);
                return;
            }

            try
            {
                _ = _runtime.InvokeCallback(purchase);
            }
            finally
            {
                // Native finalizes every successful debit, even for Nil/False callbacks.
                _store.SetTrueBestEffort(purchase.ScriptLogId);
            }
        }

        private void FailBeforeDebit(NativePasYbPurchase purchase, int errorCode)
        {
            if (!TryRelease(purchase)) return;
            _postCompletion(() => _runtime.ReportFailure(purchase, errorCode));
        }

        private bool IsPending(NativePasYbPurchase purchase)
        {
            return purchase != null
                   && _pending.TryGetValue(purchase.UserId, out var current)
                   && ReferenceEquals(current, purchase);
        }

        private bool TryRelease(NativePasYbPurchase purchase)
        {
            return purchase != null &&
                   ((ICollection<KeyValuePair<long, NativePasYbPurchase>>)
                       _pending).Remove(new KeyValuePair<long,
                       NativePasYbPurchase>(purchase.UserId, purchase));
        }
    }

    internal static class NativePasYbPurchaseService
    {
        private static readonly object WorkSync = new();
        private static readonly Queue<NativePasYbPurchase> Work = new();
        private static readonly ConcurrentQueue<Action> Completions = new();
        private static readonly NativePasYbPurchaseTransaction Transaction =
            new(new NativePasYbPurchaseSqlStore(),
                new NativePasYbProductionDebitQueue(),
                new NativePasYbProductionRuntime(),
                action => Completions.Enqueue(action));
        private static bool _workerRunning;

        internal static bool TrySubmitNormal(TPlayObject player, NormNpc npc,
            string callbackName, int vsId, int unitPrice, int quantity)
        {
            return TrySubmitNormal(player, npc, callbackName, vsId, unitPrice,
                quantity, TrySubmit);
        }

        internal static bool TrySubmitNormal(TPlayObject player, NormNpc npc,
            string callbackName, int vsId, int unitPrice, int quantity,
            Func<NativePasYbPurchase, bool> submit)
        {
            if (player == null || npc == null
                || !NativePasYbPurchaseValidation.IsSupportedNormalNpc(npc)
                || submit == null
                || !NativePasYbPurchaseValidation.TryValidateNormal(
                    callbackName, vsId, unitPrice, quantity, out var arguments))
                return false;

            var purchase = new NativePasYbPurchase(
                NativePasYbPurchaseRoute.Npc,
                player.GetCachedNativeUserId(), player.m_sUserID,
                player.m_sCharName, arguments, vsId, unitPrice, quantity,
                player.m_nGameGold, player, null);
            var accepted = submit(purchase);
            if (accepted) player.m_NPC = npc;
            return accepted;
        }

        internal static bool TrySubmitYbShop(TPlayObject player,
            byte executionTag, string callbackName, string descriptor,
            int vsId, int unitPrice, int quantity)
        {
            return TrySubmitYbShop(player, executionTag, callbackName,
                descriptor, vsId, unitPrice, quantity, TrySubmit);
        }

        internal static bool TrySubmitYbShop(TPlayObject player,
            byte executionTag, string callbackName, string descriptor,
            int vsId, int unitPrice, int quantity,
            Func<NativePasYbPurchase, bool> submit)
        {
            if (player == null || submit == null
                || !NativePasYbPurchaseValidation.TryValidateYbShop(
                    executionTag, callbackName, descriptor, unitPrice,
                    quantity, out var arguments))
                return false;

            var purchase = new NativePasYbPurchase(
                (NativePasYbPurchaseRoute)executionTag,
                player.GetCachedNativeUserId(), player.m_sUserID,
                player.m_sCharName, arguments, vsId, unitPrice, quantity,
                player.m_nGameGold, player, null);
            return submit(purchase);
        }

        internal static void ProcessCompletions()
        {
            while (Completions.TryDequeue(out var completion))
            {
                try
                {
                    completion();
                }
                catch (Exception ex)
                {
                    M2Share.ErrorMessage(
                        "[NativePasYbPurchase] completion failed: " + ex.Message);
                }
            }
        }

        private static bool TrySubmit(NativePasYbPurchase purchase)
        {
            if (!Transaction.TryReserve(purchase)) return false;
            lock (WorkSync)
            {
                Work.Enqueue(purchase);
                if (_workerRunning) return true;
                _workerRunning = true;
                _ = Task.Run(DrainWork);
            }
            return true;
        }

        private static void DrainWork()
        {
            while (true)
            {
                NativePasYbPurchase purchase;
                lock (WorkSync)
                {
                    if (Work.Count == 0)
                    {
                        _workerRunning = false;
                        return;
                    }
                    purchase = Work.Dequeue();
                }
                Transaction.Stage(purchase);
            }
        }
    }

    internal sealed class NativePasYbPurchaseSqlStore : INativePasYbPurchaseStore
    {
        public int Begin(NativePasYbPurchase purchase) =>
            NativePasYbPurchaseStore.Begin(purchase);

        public void SetTrueBestEffort(int scriptLogId) =>
            NativePasYbPurchaseStore.SetTrueBestEffort(scriptLogId);
    }

    internal sealed class NativePasYbProductionDebitQueue : INativePasYbDebitQueue
    {
        public bool Enqueue(NativePasYbPurchase purchase,
            Action<NativeYuanbaoResult> beforeCompletion,
            Action<NativeYuanbaoResult> completion)
        {
            var request = NativeYuanbaoRequest.CreatePasScriptPurchase(
                purchase.UserId, purchase.AccountName, purchase.CharacterName,
                purchase.TotalCost, purchase.ScriptLogId, purchase.VsId,
                purchase.Quantity, purchase.CallbackBytes,
                purchase.DescriptorBytes, beforeCompletion, completion);
            return NativeYuanbaoManager.Enqueue(request);
        }
    }

    internal sealed class NativePasYbProductionRuntime : INativePasYbPurchaseRuntime
    {
        public void ApplyBalance(NativePasYbPurchase purchase,
            NativeYuanbaoResult result)
        {
            var online = ResolveOnline(purchase);
            if (!IsOriginalPlayerOnline(purchase, online)) return;
            online.m_nGameGold = result.Balance;
            online.RefreshNativeLingFu();
        }

        public void ReportFailure(NativePasYbPurchase purchase, int errorCode)
        {
            var online = ResolveOnline(purchase);
            if (!IsOriginalPlayerOnline(purchase, online))
            {
                NativeYbBillingOfflineCallback.HandlePrefreezeBillingReturn(
                    purchase.CharacterName, purchase.AccountName,
                    purchase.ScriptLogId, false, null);
                return;
            }
            var error = errorCode == NativePasYbPurchaseTransaction
                    .SnapshotInsufficient
                ? NativeYuanbaoManager.GetErrorText(
                    NativeYuanbaoManager.InsufficientBalance)
                : NativeYuanbaoManager.GetErrorText(errorCode);
            online.SysMsg(error, MsgColor.Red, MsgType.Hint);
        }

        public bool InvokeCallback(NativePasYbPurchase purchase)
        {
            var online = ResolveOnline(purchase);
            if (!IsOriginalPlayerOnline(purchase, online))
                return false;

            var pasEngine = M2Share.PasEngine;
            if (pasEngine == null) return false;
            PasValue result;
            bool invoked;
            var arguments = BuildCallbackArguments(purchase);
            switch (purchase.Route)
            {
                case NativePasYbPurchaseRoute.Npc:
                    if (!TryGetCurrentNpcCallbackTarget(purchase, online,
                            out var currentNpc))
                        return false;
                    invoked = pasEngine.TryCallNpcProcedure(
                        currentNpc, new[] { purchase.CallbackName }, online,
                        out result, arguments);
                    break;
                case NativePasYbPurchaseRoute.TaskDispatch:
                    invoked = pasEngine.TryCallTaskDispatchProcedure(online,
                        purchase.CallbackName, out result, arguments);
                    break;
                case NativePasYbPurchaseRoute.YbShop:
                    invoked = pasEngine.TryCallYbShopProcedure(online,
                        purchase.CallbackName, out result, arguments);
                    break;
                default:
                    return false;
            }
            return invoked && result.AsBool();
        }

        internal static bool TryGetCurrentNpcCallbackTarget(
            NativePasYbPurchase purchase, TPlayObject online,
            out NormNpc currentNpc)
        {
            currentNpc = null;
            if (purchase?.Route != NativePasYbPurchaseRoute.Npc
                || !IsOriginalPlayerOnline(purchase, online))
                return false;

            currentNpc = online.m_NPC as NormNpc;
            return NativePasYbPurchaseValidation.IsSupportedNormalNpc(
                currentNpc);
        }

        private static bool IsOriginalPlayerOnline(
            NativePasYbPurchase purchase, TPlayObject online)
        {
            return online != null && purchase?.OriginalPlayer != null
                   && purchase.OriginalPlayer.TryGetTarget(out var original)
                   && ReferenceEquals(online, original);
        }

        internal static PasValue[] BuildCallbackArguments(
            NativePasYbPurchase purchase)
        {
            if (purchase.Route == NativePasYbPurchaseRoute.YbShop)
            {
                return new[]
                {
                    PasValue.FromString(purchase.Descriptor),
                    PasValue.FromInt(purchase.UnitPrice),
                    PasValue.FromInt(purchase.Quantity)
                };
            }
            return new[]
            {
                PasValue.FromInt(purchase.UnitPrice),
                PasValue.FromInt(purchase.Quantity)
            };
        }

        private static TPlayObject ResolveOnline(NativePasYbPurchase purchase)
        {
            if (purchase == null) return null;
            var userEngine = M2Share.UserEngine;
            if (userEngine == null) return null;
            foreach (var candidate in userEngine.PlayObjects)
            {
                if (candidate == null || candidate.m_boGhost
                    || candidate.GetCachedNativeUserId() != purchase.UserId
                    || !string.Equals(candidate.m_sUserID, purchase.AccountName,
                        StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(candidate.m_sCharName,
                        purchase.CharacterName,
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                return candidate;
            }
            return null;
        }
    }
}
