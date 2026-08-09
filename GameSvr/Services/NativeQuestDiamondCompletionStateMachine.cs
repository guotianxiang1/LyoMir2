using SystemModule.Packet;

namespace GameSvr
{
    public enum NativeQuestDiamondCompletionDisposition
    {
        InvalidFrameIgnored,
        NegativeResultNoAck,
        PositiveFailureAck,
        PositiveSuccessAck
    }

    /// <summary>
    /// Runtime dependencies for the native 1122 completion.  The interface
    /// deliberately has no request, account, object-id, or connection-generation
    /// key: the original callback resolves the current role by name each time.
    /// </summary>
    public interface INativeQuestDiamondCompletionHost
    {
        INativeQuestDiamondCompletionTarget FindCurrentRole(string roleName);
        int NextNativeRandom(int range);
        bool TrySelectBountyGbk(out byte[] descriptor);
        bool EnqueueAck(YbDbLegacy77Frame frame);
        void ReportGiveException(Exception exception);
    }

    public interface INativeQuestDiamondCompletionTarget
    {
        ushort Level { get; }
        bool IsDead { get; }
        bool IsReadyRun { get; }
        bool HasNpc { get; }

        void AddDiamondCacheUnchecked(int amount);
        void GrantExperience(int amount, bool shareWithHero,
            bool countAsFightExperience, int experienceMode);
        bool ExecuteRewardTokenGbk(ReadOnlyMemory<byte> descriptor);
        void ShowFailureDialog(string text);
        void ShowNpcSuccessDialog(string text);
        void RefreshCapital();
        void WriteGameLog(int type, string itemName, string reason,
            int count, string detail);
    }

    /// <summary>
    /// Exact, dependency-injected state machine for Ident 1122.  It remains
    /// dormant until the process-wide Delphi RandSeed owner and YBDB reconnect
    /// semantics are wired as one runtime unit.
    /// </summary>
    public static class NativeQuestDiamondCompletionStateMachine
    {
        public static NativeQuestDiamondCompletionDisposition Process(
            YbDbLegacy77Frame frame, INativeQuestDiamondCompletionHost host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (!NativeQuestDiamondProtocol.TryDecodeCompletion(frame,
                    out var completion, out _))
            {
                return NativeQuestDiamondCompletionDisposition.InvalidFrameIgnored;
            }

            var target = host.FindCurrentRole(completion.RoleName);
            var eligible = target != null && !target.IsDead && target.IsReadyRun;
            if (completion.Result <= 0)
            {
                if (eligible)
                    target.ShowFailureDialog(
                        NativeQuestDiamondProtocol.BuildFailureDialog(
                            completion.Result));
                return NativeQuestDiamondCompletionDisposition.NegativeResultNoAck;
            }

            var succeeded = eligible && TryGive(completion, target, host);
            _ = host.EnqueueAck(NativeQuestDiamondProtocol.CreateAck(
                completion.Result, succeeded));
            return succeeded
                ? NativeQuestDiamondCompletionDisposition.PositiveSuccessAck
                : NativeQuestDiamondCompletionDisposition.PositiveFailureAck;
        }

        private static bool TryGive(
            NativeQuestDiamondProtocol.Completion completion,
            INativeQuestDiamondCompletionTarget target,
            INativeQuestDiamondCompletionHost host)
        {
            var succeeded = false;
            try
            {
                var levelBase = NativeQuestDiamondProtocol.GetLevelExperienceBase(
                    target.Level);
                if (levelBase <= 0) return false;

                var total = unchecked(completion.FirstCount +
                                      completion.SecondCount);
                target.AddDiamondCacheUnchecked(total);

                var weightedCount = unchecked(completion.FirstCount +
                    unchecked(completion.SecondCount * 2));
                var weightedExperience = unchecked(levelBase * weightedCount);
                var randomBound = weightedExperience / 5;
                var randomValue = host.NextNativeRandom(randomBound);
                var experience = unchecked(weightedExperience - randomValue +
                                           weightedExperience / 10);
                target.GrantExperience(experience, true, true, 0);
                succeeded = true;

                var successDialog =
                    NativeQuestDiamondProtocol.BuildNpcSuccessDialog(
                        total, experience);
                if (total >= NativeQuestDiamondProtocol.BountyMinimumDiamondCount &&
                    host.TrySelectBountyGbk(out var bounty) && bounty != null)
                {
                    ExecuteRewardTokens(bounty, target);
                }

                if (target.HasNpc)
                    target.ShowNpcSuccessDialog(successDialog);
                target.RefreshCapital();
                target.WriteGameLog(NativeQuestDiamondProtocol.GameLogType,
                    NativeQuestDiamondProtocol.GameLogItemName,
                    NativeQuestDiamondProtocol.GameLogReason, total,
                    string.Empty);
            }
            catch (Exception ex)
            {
                host.ReportGiveException(ex);
            }

            return succeeded;
        }

        private static void ExecuteRewardTokens(byte[] descriptor,
            INativeQuestDiamondCompletionTarget target)
        {
            var offset = 0;
            while (offset < descriptor.Length)
            {
                var comma = Array.IndexOf(descriptor, (byte)',', offset);
                var end = comma < 0 ? descriptor.Length : comma;
                _ = target.ExecuteRewardTokenGbk(
                    new ReadOnlyMemory<byte>(descriptor, offset, end - offset));
                if (comma < 0) break;
                offset = comma + 1;
            }
        }
    }
}
