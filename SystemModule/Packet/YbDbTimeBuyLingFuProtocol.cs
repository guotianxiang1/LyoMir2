using System;

namespace SystemModule.Packet
{
    /// <summary>
    /// Dormant codec for the native ReqUseTimeBuyLF 201/1201 exchange.
    /// It performs no account, player, transport, or reward mutation.
    /// </summary>
    public static class YbDbTimeBuyLingFuProtocol
    {
        public const ushort RequestIdent = 201;
        public const ushort ResponseIdent = 1201;
        public const ushort SuccessAckIdent = 105;
        public const ushort FailureAckIdent = 106;
        public const int ResponsePayloadSize = 152;
        public const int RoleNameOffset = 32;
        public const int RoleNameMaximumGbkBytes = 15;
        public const int DescriptorOffset = 66;

        // Only the 152-byte frame boundary is proven. The external service's
        // declared ShortString capacity is unavailable.
        public const int MaximumReadableDescriptorGbkBytes =
            ResponsePayloadSize - DescriptorOffset - 1;

        public const string InsufficientGameTimeDialog =
            "[失败]：你没有那么多的游戏时间";
        public const string PurchaseFailedDialog =
            "[失败]: 你无法购买";

        public static bool TryCreateRequest(YbDbLegacy77Identity identity,
            int number, out YbDbLegacy77Frame frame, out string error)
        {
            frame = null;
            error = string.Empty;
            if (number <= 0)
            {
                error = "time-buy LingFu number must be positive";
                return false;
            }
            if (!YbDbLegacy77Codec.TryEncodeNativeIdentity(identity,
                    out var payload, out error))
                return false;

            frame = new YbDbLegacy77Frame(0, number, RequestIdent, payload);
            return true;
        }

        public static bool TryDecodeResponse(YbDbLegacy77Frame frame,
            out Response response, out string error)
        {
            response = null;
            error = string.Empty;
            if (frame == null)
            {
                error = "time-buy LingFu response frame is null";
                return false;
            }
            if (frame.Ident != ResponseIdent)
            {
                error = $"time-buy LingFu response Ident must be {ResponseIdent}";
                return false;
            }

            var payload = frame.Payload ?? Array.Empty<byte>();
            if (payload.Length != ResponsePayloadSize)
            {
                error = $"time-buy LingFu response payload must be " +
                        $"{ResponsePayloadSize} bytes";
                return false;
            }
            if (!YbDbLegacy77Codec.TryDecodeShortString(payload,
                    RoleNameOffset, RoleNameMaximumGbkBytes,
                    out var roleName, out error))
                return false;
            if (!YbDbLegacy77Codec.TryDecodeShortString(payload,
                    DescriptorOffset, MaximumReadableDescriptorGbkBytes,
                    out var descriptor, out error))
                return false;

            response = new Response(frame.QueryId, frame.Param,
                roleName, descriptor);
            return true;
        }

        public static bool TryGetFailureDialog(int result, out string dialog)
        {
            dialog = string.Empty;
            if (result > 0)
                return false;

            dialog = result is -3 or -2
                ? InsufficientGameTimeDialog
                : PurchaseFailedDialog;
            return true;
        }

        public static bool TryCreateAck(int transactionResult, bool succeeded,
            out YbDbLegacy77Frame frame, out string error)
        {
            frame = null;
            error = string.Empty;
            if (transactionResult <= 0)
            {
                error = "time-buy LingFu ACK requires a positive transaction result";
                return false;
            }

            frame = new YbDbLegacy77Frame(ResponseIdent, transactionResult,
                succeeded ? SuccessAckIdent : FailureAckIdent,
                Array.Empty<byte>());
            return true;
        }

        public sealed class Response
        {
            internal Response(int result, int authoritativeRemainingSeconds,
                string roleName, string descriptor)
            {
                Result = result;
                AuthoritativeRemainingSeconds = authoritativeRemainingSeconds;
                RoleName = roleName;
                Descriptor = descriptor;
            }

            public int Result { get; }
            public int AuthoritativeRemainingSeconds { get; }
            public string RoleName { get; }
            public string Descriptor { get; }
            public bool Succeeded => Result > 0;
        }
    }
}
