using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;
using SystemModule.Packet;

namespace GameSvr
{
    /// <summary>
    /// Dormant codec and calculation plan for native MakeDiamondWithYB.
    /// It deliberately performs no transport, player, database, or PAS mutation.
    /// </summary>
    public static class NativeMakeDiamondWithYbProtocol
    {
        public const ushort RequestIdent = 120;
        public const ushort CompletionIdent = 1120;
        public const int MinimumRequestAmount = 1;
        public const int MaximumRequestAmount = 300;
        public const int RequestPayloadSize = YbDbLegacy77Codec.IdentitySize;
        public const int RequestFrameSize =
            YbDbLegacy77Codec.HeaderSize + RequestPayloadSize;
        public const int CompletionPayloadSize = 32;
        public const int RoleNameMaximumGbkBytes = 15;

        public const ushort InvalidRequestClientIdent = 546;
        public const int InvalidRequestClientRecog = -2;
        public const int InternalRefreshIdent = 10054;
        public const int GameLogType = 30;
        public const int GameLogMakeIndex = 111111;
        public const int RequestUnavailableMessageParam = 0x38FF;
        public const ushort FallbackMerchantMessageIdent = 643;

        public const string RequestUnavailableMessage =
            "元宝系统暂时关闭中...";
        public const string FallbackMerchantPrefix = "NPC/";
        public const string GameLogItemName = "元宝";
        public const string GameLogReason = "申请元宝锻造";
        public const string SuccessMessage =
            "恭喜您申请元宝锻造金刚石成功。";
        public const string RewardMessagePrefix =
            "\\ \\ 并获得了锻造奖品：<";
        public const string RewardMessageSuffix = ">。";
        public const string ExitCommand = "\\ \\<离开/@exit>";

        private static readonly ReadOnlyCollection<SuccessStep>
            BaseSuccessSteps = Array.AsReadOnly(new[]
            {
                SuccessStep.ApplyAuthoritativeSnapshot,
                SuccessStep.QueueFirstInternalRefresh,
                SuccessStep.ApplyCreditCardValue2,
                SuccessStep.ApplyNickLingFu,
                SuccessStep.AccumulateYbConsume,
                SuccessStep.WriteGameDataLog,
                SuccessStep.SelectRewardDescriptor,
                SuccessStep.QueueSecondInternalRefresh,
                SuccessStep.ShowSuccessDialog
            });

        public static bool IsValidRequestAmount(int amount)
        {
            return amount >= MinimumRequestAmount &&
                   amount <= MaximumRequestAmount;
        }

        public static InvalidRequestReply CreateInvalidRequestReply()
        {
            return new InvalidRequestReply(InvalidRequestClientIdent,
                InvalidRequestClientRecog, 0, 0, 0, Array.Empty<byte>());
        }

        public static bool TryCreateRequestFrame(int amount,
            YbDbLegacy77Identity identity, out YbDbLegacy77Frame frame,
            out string error)
        {
            frame = null;
            error = string.Empty;
            if (!IsValidRequestAmount(amount))
            {
                error = $"MakeDiamondWithYB amount must be " +
                        $"{MinimumRequestAmount}..{MaximumRequestAmount}";
                return false;
            }
            if (!YbDbLegacy77Codec.TryEncodeNativeIdentity(identity,
                    out var payload, out error))
            {
                return false;
            }

            frame = new YbDbLegacy77Frame(0, amount, RequestIdent, payload);
            return true;
        }

        public static bool TryEncodeRequest(int amount,
            YbDbLegacy77Identity identity, out byte[] data, out string error)
        {
            data = null;
            if (!TryCreateRequestFrame(amount, identity, out var frame,
                    out error))
            {
                return false;
            }
            return YbDbLegacy77Codec.TryEncode(frame, out data, out error);
        }

        public static bool TryDecodeCompletion(YbDbLegacy77Frame frame,
            out Completion completion, out string error)
        {
            completion = null;
            error = string.Empty;
            if (frame == null)
            {
                error = "MakeDiamondWithYB completion frame is null";
                return false;
            }
            if (frame.Ident != CompletionIdent)
            {
                error = $"MakeDiamondWithYB completion ident must be " +
                        CompletionIdent;
                return false;
            }

            var payload = frame.Payload ?? Array.Empty<byte>();
            if (payload.Length != CompletionPayloadSize)
            {
                error = $"MakeDiamondWithYB completion payload must be " +
                        $"{CompletionPayloadSize} bytes";
                return false;
            }
            if (!YbDbLegacy77Codec.TryDecodeShortString(payload, 0,
                    RoleNameMaximumGbkBytes, out var roleName, out error))
            {
                return false;
            }

            completion = new Completion(frame.QueryId, roleName,
                BinaryPrimitives.ReadInt32LittleEndian(
                    payload.AsSpan(16, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(
                    payload.AsSpan(20, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(
                    payload.AsSpan(24, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(
                    payload.AsSpan(28, 4)));
            return true;
        }

        public static string BuildFailureDialog(int resultCode)
        {
            if (resultCode > 0)
                throw new ArgumentOutOfRangeException(nameof(resultCode));

            return resultCode switch
            {
                -2 => "[失败]：您的元宝数不足！ \\ \\<返回/@main>",
                -3 => "[失败]：您的上次的锻造尚未完成 \\ \\<返回/@main>",
                -4 => "[失败]：请先取回您上次锻造的金刚石 \\ \\<返回/@main>",
                _ => "[失败]：系统错误: Code=" +
                     resultCode.ToString(CultureInfo.InvariantCulture)
            };
        }

        /// <summary>
        /// Models the native online-player and current-NPC routing boundary.
        /// A missing current NPC changes only the dialog delivery route; unlike
        /// NewBieGiftConsume it does not reject a positive completion.
        /// </summary>
        public static CompletionRouteDecision EvaluateCompletionRoute(
            Completion completion, bool roleOnline, bool currentNpcBound)
        {
            if (completion == null || !roleOnline)
                return CompletionRouteDecision.Ignore;

            var output = currentNpcBound
                ? DialogOutputDisposition.CurrentNpcDialog
                : DialogOutputDisposition.MerchantSayNpcPrefix;
            return completion.ResultCode > 0
                ? new CompletionRouteDecision(true, true, output,
                    string.Empty)
                : new CompletionRouteDecision(true, false, output,
                    BuildFailureDialog(completion.ResultCode));
        }

        public static int GetRewardDescriptorIndex(int resultCode)
        {
            if (resultCode < 50) return -1;
            return resultCode <= 300 ? 0 : 1;
        }

        public static RewardPlan ParseRewardDescriptor(string descriptor)
        {
            if (string.IsNullOrEmpty(descriptor)) return null;

            var separator = descriptor.IndexOf(':');
            var name = separator < 0
                ? descriptor
                : descriptor.Substring(0, separator);
            var countText = separator < 0
                ? string.Empty
                : descriptor.Substring(separator + 1);
            if (!int.TryParse(countText, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var count))
            {
                count = 1;
            }

            var kind = name switch
            {
                "经验" => RewardKind.Experience,
                "声望" => RewardKind.Reputation,
                "金刚石" => RewardKind.Diamond,
                _ => RewardKind.StandardItem
            };
            return new RewardPlan(descriptor, name, count, kind);
        }

        /// <summary>
        /// Builds the exact local action plan after a positive 1120 result.
        /// RewardDescriptor must already have been resolved by the native
        /// configuration object for RewardDescriptorIndex. It is ignored when
        /// that index is -1; the source remains uncoupled from this helper.
        /// </summary>
        public static bool TryBuildSuccessPlan(Completion completion,
            SuccessContext context, out SuccessPlan plan, out string error)
        {
            plan = null;
            error = string.Empty;
            if (completion == null)
            {
                error = "MakeDiamondWithYB completion is null";
                return false;
            }
            if (completion.ResultCode <= 0)
            {
                error = "MakeDiamondWithYB success plan requires a positive result";
                return false;
            }
            if (context == null)
            {
                error = "MakeDiamondWithYB success context is null";
                return false;
            }

            var yuanbaoIncrease = unchecked(completion.CurrentYuanbao -
                                             context.PreviousYuanbao);
            var increaseMessage = context.AccountPreviouslyInitialized &&
                                  yuanbaoIncrease > 0
                ? yuanbaoIncrease.ToString(CultureInfo.InvariantCulture) +
                  " 个元宝增加"
                : string.Empty;

            var creditCardApplied = context.CreditCardBonusEnabled;
            var creditCardValue2After = context.CreditCardValue2;
            if (creditCardApplied)
            {
                creditCardValue2After = unchecked(creditCardValue2After +
                                                  completion.ResultCode);
                if (creditCardValue2After < 0) creditCardValue2After = 0;
            }

            var nickLingFuDelta = unchecked(completion.ResultCode *
                                            context.LingFuMultiplier);
            var nickLingFuBalanceAfter = context.NickLingFuBalance;
            var nickLingFuCumulativeAfter = context.NickLingFuCumulative;
            var nickLingFuMessage = string.Empty;
            if (context.NickLingFuEnabled)
            {
                nickLingFuBalanceAfter = unchecked(nickLingFuBalanceAfter +
                                                   nickLingFuDelta);
                nickLingFuCumulativeAfter = unchecked(
                    nickLingFuCumulativeAfter + nickLingFuDelta);
                nickLingFuMessage = "您获得了" +
                    nickLingFuDelta.ToString(CultureInfo.InvariantCulture) +
                    "张圣殿灵符";
            }

            var rewardIndex = GetRewardDescriptorIndex(
                completion.ResultCode);
            var reward = rewardIndex < 0
                ? null
                : ParseRewardDescriptor(context.RewardDescriptor);
            var successDialog = SuccessMessage;
            if (reward != null)
            {
                successDialog += RewardMessagePrefix + reward.Descriptor +
                                 RewardMessageSuffix;
            }
            successDialog += ExitCommand;

            var steps = new List<SuccessStep>(BaseSuccessSteps.Count + 2);
            if (!string.IsNullOrEmpty(increaseMessage))
                steps.Add(SuccessStep.ShowYuanbaoIncreaseNotice);
            foreach (var step in BaseSuccessSteps)
            {
                if (step == SuccessStep.QueueSecondInternalRefresh &&
                    reward != null)
                {
                    steps.Add(SuccessStep.GrantConfiguredReward);
                }
                steps.Add(step);
            }

            plan = new SuccessPlan(completion, increaseMessage,
                creditCardApplied, creditCardValue2After,
                nickLingFuDelta, context.NickLingFuEnabled,
                nickLingFuBalanceAfter, nickLingFuCumulativeAfter,
                nickLingFuMessage, context.YbConsumePtId, rewardIndex,
                reward, successDialog, Array.AsReadOnly(steps.ToArray()));
            return true;
        }

        public enum SuccessStep
        {
            ShowYuanbaoIncreaseNotice,
            ApplyAuthoritativeSnapshot,
            QueueFirstInternalRefresh,
            ApplyCreditCardValue2,
            ApplyNickLingFu,
            AccumulateYbConsume,
            WriteGameDataLog,
            SelectRewardDescriptor,
            GrantConfiguredReward,
            QueueSecondInternalRefresh,
            ShowSuccessDialog
        }

        public enum RewardKind
        {
            Experience,
            Reputation,
            Diamond,
            StandardItem
        }

        public enum DialogOutputDisposition
        {
            None,
            CurrentNpcDialog,
            MerchantSayNpcPrefix
        }

        public sealed class CompletionRouteDecision
        {
            internal static CompletionRouteDecision Ignore { get; } =
                new(false, false, DialogOutputDisposition.None,
                    string.Empty);

            internal CompletionRouteDecision(bool roleResolved,
                bool executePositiveSideEffects,
                DialogOutputDisposition dialogOutput, string failureDialog)
            {
                RoleResolved = roleResolved;
                ExecutePositiveSideEffects = executePositiveSideEffects;
                DialogOutput = dialogOutput;
                FailureDialog = failureDialog ?? string.Empty;
            }

            public bool RoleResolved { get; }
            public bool ExecutePositiveSideEffects { get; }
            public DialogOutputDisposition DialogOutput { get; }
            public string FailureDialog { get; }
            public bool SendsYbDbAcknowledgement => false;
        }

        public sealed class InvalidRequestReply
        {
            internal InvalidRequestReply(ushort ident, int recog, int param1,
                int param2, int param3, byte[] payload)
            {
                Ident = ident;
                Recog = recog;
                Param1 = param1;
                Param2 = param2;
                Param3 = param3;
                Payload = payload;
            }

            public ushort Ident { get; }
            public int Recog { get; }
            public int Param1 { get; }
            public int Param2 { get; }
            public int Param3 { get; }
            public byte[] Payload { get; }
        }

        public sealed class Completion
        {
            internal Completion(int resultCode, string roleName,
                int currentYuanbao, int totalConsumedYuanbao,
                int durationSeconds, int dividendConsumed)
            {
                ResultCode = resultCode;
                RoleName = roleName;
                CurrentYuanbao = currentYuanbao;
                TotalConsumedYuanbao = totalConsumedYuanbao;
                DurationSeconds = durationSeconds;
                DividendConsumed = dividendConsumed;
            }

            public int ResultCode { get; }
            public string RoleName { get; }
            public int CurrentYuanbao { get; }
            public int TotalConsumedYuanbao { get; }
            public int DurationSeconds { get; }
            public int DividendConsumed { get; }
        }

        public sealed class SuccessContext
        {
            public bool AccountPreviouslyInitialized { get; set; }
            public int PreviousYuanbao { get; set; }
            public bool CreditCardBonusEnabled { get; set; }
            public int CreditCardValue2 { get; set; }
            public int LingFuMultiplier { get; set; } = 1;
            public bool NickLingFuEnabled { get; set; }
            public int NickLingFuBalance { get; set; }
            public int NickLingFuCumulative { get; set; }
            public string YbConsumePtId { get; set; } = string.Empty;
            public string RewardDescriptor { get; set; } = string.Empty;
        }

        public sealed class RewardPlan
        {
            internal RewardPlan(string descriptor, string name, int count,
                RewardKind kind)
            {
                Descriptor = descriptor;
                Name = name;
                Count = count;
                Kind = kind;
            }

            public string Descriptor { get; }
            public string Name { get; }
            public int Count { get; }
            public RewardKind Kind { get; }
            public int ExperienceDelta => Kind == RewardKind.Experience
                ? Count : 0;
            public int ReputationDelta => Kind == RewardKind.Reputation
                ? Count : 0;
            public int DiamondDelta => Kind == RewardKind.Diamond
                ? Count : 0;
            public int StandardItemCreateAttempts =>
                Kind == RewardKind.StandardItem && Count > 0 ? Count : 0;
        }

        public sealed class SuccessPlan
        {
            internal SuccessPlan(Completion snapshot,
                string yuanbaoIncreaseMessage, bool creditCardValue2Applied,
                int creditCardValue2After, int nickLingFuDelta,
                bool nickLingFuApplied, int nickLingFuBalanceAfter,
                int nickLingFuCumulativeAfter, string nickLingFuMessage,
                string ybConsumePtId, int rewardDescriptorIndex,
                RewardPlan reward,
                string successDialog,
                ReadOnlyCollection<SuccessStep> orderedSteps)
            {
                Snapshot = snapshot;
                YuanbaoIncreaseMessage = yuanbaoIncreaseMessage;
                CreditCardValue2Applied = creditCardValue2Applied;
                CreditCardValue2After = creditCardValue2After;
                NickLingFuDelta = nickLingFuDelta;
                NickLingFuApplied = nickLingFuApplied;
                NickLingFuBalanceAfter = nickLingFuBalanceAfter;
                NickLingFuCumulativeAfter = nickLingFuCumulativeAfter;
                NickLingFuMessage = nickLingFuMessage;
                YbConsumePtId = ybConsumePtId;
                RewardDescriptorIndex = rewardDescriptorIndex;
                Reward = reward;
                SuccessDialog = successDialog;
                OrderedSteps = orderedSteps;
            }

            public Completion Snapshot { get; }
            public string YuanbaoIncreaseMessage { get; }
            public bool CreditCardValue2Applied { get; }
            public bool CreditCardMarkedDirty => CreditCardValue2Applied;
            public int CreditCardValue2After { get; }
            public int NickLingFuDelta { get; }
            public bool NickLingFuApplied { get; }
            public int NickLingFuBalanceAfter { get; }
            public int NickLingFuCumulativeAfter { get; }
            public string NickLingFuMessage { get; }
            public string YbConsumePtId { get; }
            public int YbConsumeDelta => Snapshot.ResultCode;
            public int LogType => GameLogType;
            public string LogItemName => GameLogItemName;
            public string LogReason => GameLogReason;
            public int LogQuantity => Snapshot.ResultCode;
            public int LogMakeIndex => GameLogMakeIndex;
            public int RewardDescriptorIndex { get; }
            public RewardPlan Reward { get; }
            public int InternalRefreshCount => 2;
            public bool SendsYbDbAcknowledgement => false;
            public string SuccessDialog { get; }
            public IReadOnlyList<SuccessStep> OrderedSteps { get; }
        }
    }
}
