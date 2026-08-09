using System;
using System.Buffers.Binary;

namespace SystemModule.Packet
{
    /// <summary>
    /// Dormant codec and display state matrix for the native
    /// ClientAskYBDuanZao 121/1121 exchange. It performs no transport,
    /// player, NPC, account, forge, or script mutation.
    /// </summary>
    public static class YbDbForgeProgressProtocol
    {
        public const ushort RequestIdent = 121;
        public const ushort ResponseIdent = 1121;
        public const int ResponsePayloadSize = 32;
        public const int RoleNameOffset = 0;
        public const int RoleNameMaximumGbkBytes = 15;
        public const int CompletedCountOffset = 16;
        public const int ClaimedCountOffset = 20;
        public const int DoubleCompletedCountOffset = 24;
        public const int IgnoredTailOffset = 28;
        public const ushort FallbackMerchantMessageIdent = 643;

        public const string RequestUnavailableDialog =
            "元宝系统暂时关闭中...\\ \\ \\ <返回/@main>";
        public const string NotAppliedDialog =
            "还未申请锻造！\\ \\<返回/@main>";
        public const string FallbackMerchantPrefix = "NPC/";

        public static bool TryCreateRequest(YbDbLegacy77Identity identity,
            out YbDbLegacy77Frame frame, out string error)
        {
            frame = null;
            if (!YbDbLegacy77Codec.TryEncodeNativeIdentity(identity,
                    out var payload, out error))
                return false;

            frame = new YbDbLegacy77Frame(0, 0, RequestIdent, payload);
            return true;
        }

        public static InvocationDecision EvaluateInvocation(
            bool transportAccepted) => new(transportAccepted,
            !transportAccepted, false);

        public static bool TryDecodeResponse(YbDbLegacy77Frame frame,
            out Response response, out string error)
        {
            response = null;
            error = string.Empty;
            if (frame == null)
            {
                error = "forge-progress response frame is null";
                return false;
            }
            if (frame.Ident != ResponseIdent)
            {
                error = $"forge-progress response Ident must be {ResponseIdent}";
                return false;
            }

            var payload = frame.Payload ?? Array.Empty<byte>();
            if (payload.Length != ResponsePayloadSize)
            {
                error = $"forge-progress response payload must be " +
                        $"{ResponsePayloadSize} bytes";
                return false;
            }
            if (!YbDbLegacy77Codec.TryDecodeShortString(payload,
                    RoleNameOffset, RoleNameMaximumGbkBytes,
                    out var roleName, out error))
                return false;

            response = new Response(frame.QueryId, frame.Param, roleName,
                BinaryPrimitives.ReadInt32LittleEndian(
                    payload.AsSpan(CompletedCountOffset, sizeof(int))),
                BinaryPrimitives.ReadInt32LittleEndian(
                    payload.AsSpan(ClaimedCountOffset, sizeof(int))),
                BinaryPrimitives.ReadInt32LittleEndian(
                    payload.AsSpan(DoubleCompletedCountOffset, sizeof(int))),
                BinaryPrimitives.ReadInt32LittleEndian(
                    payload.AsSpan(IgnoredTailOffset, sizeof(int))));
            return true;
        }

        public static CompletionDecision EvaluateCompletion(Response response,
            bool roleOnline, bool currentNpcBound)
        {
            if (response == null || !roleOnline)
                return CompletionDecision.Ignore;

            return new CompletionDecision(true,
                currentNpcBound
                    ? OutputDisposition.CurrentNpcDialog
                    : OutputDisposition.MerchantSayNpcPrefix,
                BuildDialog(response));
        }

        public static string BuildDialog(Response response)
        {
            if (response == null) throw new ArgumentNullException(nameof(response));
            if (response.RequestedTotal == -1) return NotAppliedDialog;

            var completed = response.CompletedCount;
            var claimed = response.ClaimedCount;
            var text = response.RoleName +
                       " 您的元宝锻造金刚石信息如下：\\" +
                       "申请总数：" + response.RequestedTotal + " 颗\\" +
                       "已锻造完成数：" + completed + " 颗  ";
            text += response.DoubleCompletedCount > 0
                ? "其中双倍锻造完成数：" +
                  response.DoubleCompletedCount + " 颗  \\"
                : "\\";
            text += "已领取数：" + claimed + " 颗\\" +
                    "本次可领取数：" +
                    unchecked(completed - claimed) + " 颗\\" +
                    "尚未完成数：" +
                    unchecked(response.RequestedTotal - completed) + " 颗";
            text += completed > claimed
                ? "\\ \\您要领取吗？  <全部领取/@ybdzlq>  " +
                  "<只领取12颗/@ybdzlq_12>    <返回/@main>"
                : "\\ \\<返回/@main>";
            return text;
        }

        public readonly struct InvocationDecision
        {
            internal InvocationDecision(bool transportAccepted,
                bool showUnavailableDialog, bool createsPendingRequest)
            {
                TransportAccepted = transportAccepted;
                ShowUnavailableDialog = showUnavailableDialog;
                CreatesPendingRequest = createsPendingRequest;
            }

            public bool TransportAccepted { get; }
            public bool ShowUnavailableDialog { get; }
            public bool CreatesPendingRequest { get; }
        }

        public enum OutputDisposition
        {
            None,
            CurrentNpcDialog,
            MerchantSayNpcPrefix
        }

        public sealed class CompletionDecision
        {
            internal static CompletionDecision Ignore { get; } =
                new(false, OutputDisposition.None, string.Empty);

            internal CompletionDecision(bool display,
                OutputDisposition output, string dialog)
            {
                Display = display;
                Output = output;
                Dialog = dialog ?? string.Empty;
            }

            public bool Display { get; }
            public OutputDisposition Output { get; }
            public string Dialog { get; }
            public bool SendsAck => false;
            public bool MutatesPlayerOrAccount => false;
        }

        public sealed class Response
        {
            internal Response(int requestedTotal, int ignoredHeaderParam,
                string roleName, int completedCount, int claimedCount,
                int doubleCompletedCount, int ignoredTail)
            {
                RequestedTotal = requestedTotal;
                IgnoredHeaderParam = ignoredHeaderParam;
                RoleName = roleName;
                CompletedCount = completedCount;
                ClaimedCount = claimedCount;
                DoubleCompletedCount = doubleCompletedCount;
                IgnoredTail = ignoredTail;
            }

            public int RequestedTotal { get; }
            public int IgnoredHeaderParam { get; }
            public string RoleName { get; }
            public int CompletedCount { get; }
            public int ClaimedCount { get; }
            public int DoubleCompletedCount { get; }
            public int IgnoredTail { get; }
        }
    }
}
