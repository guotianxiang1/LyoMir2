using System;
using System.Buffers.Binary;

namespace SystemModule.Packet
{
    /// <summary>
    /// Dormant codec and state matrix for the native NewBieGiftConsume
    /// 125/1125 transaction. It performs no transport, account, script,
    /// player, database, reward, or ACK mutation.
    /// </summary>
    public static class YbDbNewBieGiftConsumeProtocol
    {
        public const ushort RequestIdent = 125;
        public const ushort ResponseIdent = 1125;
        public const ushort SuccessAckIdent = 105;
        public const ushort FailureAckIdent = 106;
        public const int Operation = 10100;
        public const int Cost = 5;
        public const int ResponsePayloadSize = 32;
        public const int RoleNameOffset = 0;
        public const int RoleNameMaximumGbkBytes = 15;
        public const int CurrentYuanbaoOffset = 16;
        public const int TotalConsumedOffset = 20;
        public const int RemainingSecondsOffset = 24;
        public const int DividendConsumedOffset = 28;
        public const ushort FallbackMerchantMessageIdent = 643;

        public const string SuccessCallback = "@NewBieGiftConsumeOk";
        public const string InsufficientYuanbaoDialog =
            "对不起，您没有那么多的元宝";
        public const string FallbackMerchantPrefix = "NPC/";

        public static InvocationDecision EvaluateInvocation(
            int cachedYuanbao, bool sharedIdent125Pending,
            bool transportAccepted)
        {
            if (cachedYuanbao < Cost)
                return new InvocationDecision(false, false, false, false);

            // The wrapper always invokes the common builder once the cached
            // balance gate passes. The builder only reaches transport while
            // the shared Ident-125 byte is clear, and sets that byte only
            // when the transport accepts the frame.
            var attemptTransport = !sharedIdent125Pending;
            return new InvocationDecision(true, true, attemptTransport,
                attemptTransport && transportAccepted);
        }

        public static bool TryCreateRequest(YbDbLegacy77Identity identity,
            out YbDbLegacy77Frame frame, out string error)
        {
            frame = null;
            if (!YbDbLegacy77Codec.TryEncodeNativeIdentity(identity,
                    out var payload, out error))
                return false;

            frame = new YbDbLegacy77Frame(Operation, Cost,
                RequestIdent, payload);
            return true;
        }

        public static bool TryDecodeResponse(YbDbLegacy77Frame frame,
            out Response response, out string error)
        {
            response = null;
            error = string.Empty;
            if (frame == null)
            {
                error = "newbie-gift response frame is null";
                return false;
            }
            if (frame.Ident != ResponseIdent)
            {
                error = $"newbie-gift response Ident must be {ResponseIdent}";
                return false;
            }

            var payload = frame.Payload ?? Array.Empty<byte>();
            if (payload.Length != ResponsePayloadSize)
            {
                error = $"newbie-gift response payload must be " +
                        $"{ResponsePayloadSize} bytes";
                return false;
            }
            if (!YbDbLegacy77Codec.TryDecodeShortString(payload,
                    RoleNameOffset, RoleNameMaximumGbkBytes,
                    out var roleName, out error))
                return false;

            response = new Response(frame.QueryId, frame.Param, roleName,
                BinaryPrimitives.ReadInt32LittleEndian(
                    payload.AsSpan(CurrentYuanbaoOffset, sizeof(int))),
                BinaryPrimitives.ReadInt32LittleEndian(
                    payload.AsSpan(TotalConsumedOffset, sizeof(int))),
                BinaryPrimitives.ReadInt32LittleEndian(
                    payload.AsSpan(RemainingSecondsOffset, sizeof(int))),
                BinaryPrimitives.ReadInt32LittleEndian(
                    payload.AsSpan(DividendConsumedOffset, sizeof(int))));
            return true;
        }

        public static CompletionDecision EvaluateCompletion(
            Response response, bool roleOnline, bool currentNpcBound)
        {
            if (response == null || response.Operation != Operation)
                return CompletionDecision.Ignore;

            if (response.Result <= 0)
            {
                var showDialog = roleOnline && response.Result == -1;
                return new CompletionDecision(AckDisposition.None,
                    roleOnline, false, showDialog,
                    !showDialog
                        ? FailureOutputDisposition.None
                        : currentNpcBound
                            ? FailureOutputDisposition.CurrentNpcDialog
                            : FailureOutputDisposition.MerchantSayNpcPrefix,
                    Array.Empty<SuccessStep>());
            }

            if (!roleOnline || !currentNpcBound)
            {
                return new CompletionDecision(AckDisposition.Failure,
                    roleOnline, false, false,
                    FailureOutputDisposition.None,
                    Array.Empty<SuccessStep>());
            }

            return new CompletionDecision(AckDisposition.Success,
                true, true, false, FailureOutputDisposition.None, new[]
                {
                    SuccessStep.ClearSharedIdent125Pending,
                    SuccessStep.InvokeNpcCallback,
                    SuccessStep.ApplyNativeConsumeLingFuBonus,
                    SuccessStep.RecordNativeYbConsume,
                    SuccessStep.ApplyAuthoritativeAccountSnapshot,
                    SuccessStep.TryAccumulateCreditCardValue2,
                    SuccessStep.SendSuccessAck
                });
        }

        public static bool TryCreateAck(int transactionToken, bool succeeded,
            out YbDbLegacy77Frame frame, out string error)
        {
            frame = null;
            error = string.Empty;
            if (transactionToken <= 0)
            {
                error = "newbie-gift ACK requires a positive transaction token";
                return false;
            }

            frame = new YbDbLegacy77Frame(ResponseIdent, transactionToken,
                succeeded ? SuccessAckIdent : FailureAckIdent,
                Array.Empty<byte>());
            return true;
        }

        public readonly struct InvocationDecision
        {
            public InvocationDecision(bool pascalReturnValue,
                bool invokeCommonRequestBuilder, bool attemptTransportSend,
                bool setSharedIdent125Pending)
            {
                PascalReturnValue = pascalReturnValue;
                InvokeCommonRequestBuilder = invokeCommonRequestBuilder;
                AttemptTransportSend = attemptTransportSend;
                SetSharedIdent125Pending = setSharedIdent125Pending;
            }

            public bool PascalReturnValue { get; }
            public bool InvokeCommonRequestBuilder { get; }
            public bool AttemptTransportSend { get; }
            public bool SetSharedIdent125Pending { get; }
        }

        public enum AckDisposition
        {
            None,
            Success,
            Failure
        }

        public enum SuccessStep
        {
            ClearSharedIdent125Pending,
            InvokeNpcCallback,
            ApplyNativeConsumeLingFuBonus,
            RecordNativeYbConsume,
            ApplyAuthoritativeAccountSnapshot,
            TryAccumulateCreditCardValue2,
            SendSuccessAck
        }

        public enum FailureOutputDisposition
        {
            None,
            CurrentNpcDialog,
            MerchantSayNpcPrefix
        }

        public sealed class CompletionDecision
        {
            internal static CompletionDecision Ignore { get; } =
                new(AckDisposition.None, false, false, false,
                    FailureOutputDisposition.None,
                    Array.Empty<SuccessStep>());

            internal CompletionDecision(AckDisposition ack,
                bool clearSharedPending, bool invokeCallback,
                bool showInsufficientYuanbaoDialog,
                FailureOutputDisposition failureOutput,
                SuccessStep[] successSteps)
            {
                Ack = ack;
                ClearSharedPending = clearSharedPending;
                InvokeCallback = invokeCallback;
                ShowInsufficientYuanbaoDialog =
                    showInsufficientYuanbaoDialog;
                FailureOutput = failureOutput;
                SuccessSteps = successSteps ?? Array.Empty<SuccessStep>();
            }

            public AckDisposition Ack { get; }
            public bool ClearSharedPending { get; }
            public bool InvokeCallback { get; }
            public bool ShowInsufficientYuanbaoDialog { get; }
            public FailureOutputDisposition FailureOutput { get; }
            public SuccessStep[] SuccessSteps { get; }
        }

        public sealed class Response
        {
            internal Response(int result, int operation, string roleName,
                int currentYuanbao, int totalConsumed,
                int remainingSeconds, int dividendConsumed)
            {
                Result = result;
                Operation = operation;
                RoleName = roleName;
                CurrentYuanbao = currentYuanbao;
                TotalConsumed = totalConsumed;
                RemainingSeconds = remainingSeconds;
                DividendConsumed = dividendConsumed;
            }

            public int Result { get; }
            public int Operation { get; }
            public string RoleName { get; }
            public int CurrentYuanbao { get; }
            public int TotalConsumed { get; }
            public int RemainingSeconds { get; }
            public int DividendConsumed { get; }
            public bool Succeeded => Result > 0;
            public int NativeYbConsumeDelta => Cost;
            public int NativeBonusBase => Cost;
            public int CreditCardValue2Delta => Cost;
        }
    }
}
