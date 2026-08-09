using System.Collections.Concurrent;
using GameSvr.PasEngine;
using GameSvr.Services;

namespace GameSvr
{
    public partial class TPlayObject
    {
        internal const bool NativeNeedKeyBoxHasExactYbDb125Transport = false;
        internal const string NativeNeedKeyBoxYuanbaoSuccessCallback =
            "OpenValuedBox_OK2";

        private static readonly ConcurrentDictionary<long,
            NativeNeedKeyBoxYuanbaoSubmission>
            NativeNeedKeyBoxYuanbaoPendingByUserId = new();
        private static long s_nativeNeedKeyBoxYuanbaoCorrelationSeed;

        private long _nativeNeedKeyBoxYuanbaoCorrelation;

        internal long NativeNeedKeyBoxYuanbaoCorrelation =>
            Interlocked.Read(ref _nativeNeedKeyBoxYuanbaoCorrelation);

        internal static int NativeNeedKeyBoxYuanbaoSubmissionCount =>
            NativeNeedKeyBoxYuanbaoPendingByUserId.Count;

        // This closes the local transaction path. The original YBDB
        // 125/1125 transport remains a documented protocol residual.
        internal NativeNeedKeyBoxYuanbaoResult
            TrySubmitNativeNeedKeyBoxYuanbao(NormNpc npc)
        {
            var scriptHost = M2Share.PasEngine;
            if (npc == null || scriptHost == null ||
                !scriptHost.TryCaptureNpcInteraction(this, npc,
                    out var interaction, out _))
                return NativeNeedKeyBoxYuanbaoResult.EnqueueFailed;

            var result = TryBeginNativeNeedKeyBoxYuanbao(true,
                (ident, selector, param1, param2, amount) =>
                    TryEnqueueNativeNeedKeyBoxYuanbao(interaction, ident,
                        selector, param1, param2, amount));
            if (result == NativeNeedKeyBoxYuanbaoResult.Submitted)
                m_NPC = npc;
            return result;
        }

        internal static bool IsNativeNeedKeyBoxYuanbaoTuple(int ident,
            int selector, int param1, int param2, int amount)
        {
            return ident == NativeNeedKeyBoxYuanbaoIdent &&
                   selector == NativeNeedKeyBoxYuanbaoSelector &&
                   param1 == 0 && param2 == 0 &&
                   amount == NativeNeedKeyBoxYuanbaoAmount;
        }

        private bool TryEnqueueNativeNeedKeyBoxYuanbao(
            NpcPasScriptInteractionHandle interaction, int ident,
            int selector, int param1, int param2, int amount)
        {
            if (!IsNativeNeedKeyBoxYuanbaoTuple(ident, selector, param1,
                    param2, amount) || interaction == null ||
                !ReferenceEquals(interaction.Player, this) ||
                interaction.Npc == null ||
                !ReferenceEquals(interaction.Owner, M2Share.PasEngine) ||
                m_boGhost)
                return false;

            var userId = GetCachedNativeUserId();
            if (userId <= 0 || string.IsNullOrEmpty(m_sUserID) ||
                string.IsNullOrEmpty(m_sCharName))
                return false;

            var correlation = Interlocked.Increment(
                ref s_nativeNeedKeyBoxYuanbaoCorrelationSeed);
            var submission = new NativeNeedKeyBoxYuanbaoSubmission(
                correlation, userId, m_sUserID, m_sCharName, this,
                interaction);
            if (Interlocked.CompareExchange(
                    ref _nativeNeedKeyBoxYuanbaoCorrelation, correlation, 0) !=
                0)
                return false;

            if (!NativeNeedKeyBoxYuanbaoPendingByUserId.TryAdd(userId,
                    submission))
            {
                Interlocked.CompareExchange(
                    ref _nativeNeedKeyBoxYuanbaoCorrelation, 0, correlation);
                return false;
            }

            var request = NativeYuanbaoRequest.CreateScript(userId,
                submission.AccountName, submission.CharacterName, amount,
                NativeYuanbaoManager.SubtractOperation, null,
                result => CompleteNativeNeedKeyBoxYuanbao(submission,
                    result));
            try
            {
                if (NativeYuanbaoManager.Enqueue(request)) return true;
            }
            catch (Exception exception)
            {
                M2Share.ErrorMessage(
                    "[NativeNeedKeyBox] yuanbao enqueue failed: " +
                    exception.Message);
            }

            TryReleaseNativeNeedKeyBoxYuanbao(submission);
            return false;
        }

        private static void CompleteNativeNeedKeyBoxYuanbao(
            NativeNeedKeyBoxYuanbaoSubmission submission,
            NativeYuanbaoResult result)
        {
            if (!TryReleaseNativeNeedKeyBoxYuanbao(submission)) return;

            var online = ResolveNativeNeedKeyBoxYuanbaoPlayer(submission);
            if (online == null) return;
            if (result.ErrorCode != 0)
            {
                online.CompleteNativeNeedKeyBoxYuanbaoFailure();
                return;
            }

            if (!online.TryCompleteNativeNeedKeyBoxYuanbaoSuccess(out _))
                return;

            InvokeNativeNeedKeyBoxYuanbaoSuccessCallback(submission, online);
            var nickLinFuState = M2Share.NickLinFuState ??
                                 NativeNickLinFuState.Disabled;
            online.IncNativeNickLinFu(NativeNeedKeyBoxYuanbaoAmount,
                nickLinFuState.Multiplier, nickLinFuState.Enabled);
            NativeYbShopPurchaseStore.AddConsumptionBestEffort(
                online.m_sUserID, NativeNeedKeyBoxYuanbaoAmount);
            online.m_nGameGold = result.Balance;
            online.RefreshNativeLingFu();
            online.AddNativeYbShopCreditValue2(
                NativeNeedKeyBoxYuanbaoAmount);
        }

        private static void InvokeNativeNeedKeyBoxYuanbaoSuccessCallback(
            NativeNeedKeyBoxYuanbaoSubmission submission,
            TPlayObject online)
        {
            var interaction = submission?.NpcInteraction;
            if (interaction == null ||
                !ReferenceEquals(interaction.Player, online) ||
                !ReferenceEquals(online.m_NPC, interaction.Npc))
                return;

            _ = interaction.Owner.TryCallNpcProcedure(interaction,
                new[] { NativeNeedKeyBoxYuanbaoSuccessCallback }, out _);
        }

        private static bool TryReleaseNativeNeedKeyBoxYuanbao(
            NativeNeedKeyBoxYuanbaoSubmission submission)
        {
            if (submission == null ||
                !((ICollection<KeyValuePair<long,
                        NativeNeedKeyBoxYuanbaoSubmission>>)
                    NativeNeedKeyBoxYuanbaoPendingByUserId).Remove(
                    new KeyValuePair<long,
                        NativeNeedKeyBoxYuanbaoSubmission>(submission.UserId,
                        submission)))
                return false;

            if (submission.OriginalPlayer.TryGetTarget(out var original))
            {
                Interlocked.CompareExchange(
                    ref original._nativeNeedKeyBoxYuanbaoCorrelation, 0,
                    submission.Correlation);
            }
            return true;
        }

        private static TPlayObject ResolveNativeNeedKeyBoxYuanbaoPlayer(
            NativeNeedKeyBoxYuanbaoSubmission submission)
        {
            if (submission == null ||
                !submission.OriginalPlayer.TryGetTarget(out var original) ||
                original.m_boGhost)
                return null;

            var userEngine = M2Share.UserEngine;
            if (userEngine == null) return null;
            foreach (var candidate in userEngine.PlayObjects)
            {
                if (!ReferenceEquals(candidate, original) ||
                    candidate.m_boGhost ||
                    candidate.GetCachedNativeUserId() != submission.UserId ||
                    !string.Equals(candidate.m_sUserID,
                        submission.AccountName,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(candidate.m_sCharName,
                        submission.CharacterName,
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                return candidate;
            }
            return null;
        }

        private sealed class NativeNeedKeyBoxYuanbaoSubmission
        {
            internal NativeNeedKeyBoxYuanbaoSubmission(long correlation,
                long userId, string accountName, string characterName,
                TPlayObject originalPlayer,
                NpcPasScriptInteractionHandle npcInteraction)
            {
                Correlation = correlation;
                UserId = userId;
                AccountName = accountName;
                CharacterName = characterName;
                OriginalPlayer = new WeakReference<TPlayObject>(
                    originalPlayer);
                NpcInteraction = npcInteraction;
            }

            internal long Correlation { get; }
            internal long UserId { get; }
            internal string AccountName { get; }
            internal string CharacterName { get; }
            internal WeakReference<TPlayObject> OriginalPlayer { get; }
            internal NpcPasScriptInteractionHandle NpcInteraction { get; }
        }
    }
}
