using System;
using System.Buffers.Binary;

namespace SystemModule.Packet
{
    /// <summary>
    /// Dormant codec for the native UpFeeUserAct 203/1203 exchange.
    /// It performs no account, player, transport, experience, or script mutation.
    /// </summary>
    public static class YbDbFeeUserActivityProtocol
    {
        public const ushort RequestIdent = 203;
        public const ushort ResponseIdent = 1203;
        public const int RequestParam = 1;
        public const int ResponsePayloadSize = 32;
        public const int RoleNameOffset = 0;
        public const int RoleNameMaximumGbkBytes = 15;
        public const int CurrentYuanbaoOffset = 16;
        public const int TotalConsumedOffset = 20;
        public const int RemainingSecondsOffset = 24;
        public const int DividendConsumedOffset = 28;
        public const int SuccessExperience = 1_000_000;

        public const string AlreadyParticipatedDialog =
            "我记得您已经参与过了，难道我记错了？？";
        public const string InsufficientRemainingTimeDialog =
            "您的剩余时间不足";
        public const string SuccessScriptLabel = "@UpFeeUserAct_OK";

        public static ushort GetMonthSerial(DateTime date)
        {
            return unchecked((ushort)(date.Year * 12 + date.Month));
        }

        public static bool TryGetPreflightDialog(int remainingSeconds,
            ushort persistedClaimMonth, DateTime currentDate, out string dialog)
        {
            if (remainingSeconds <= 0)
            {
                dialog = InsufficientRemainingTimeDialog;
                return true;
            }
            if (persistedClaimMonth == GetMonthSerial(currentDate))
            {
                dialog = AlreadyParticipatedDialog;
                return true;
            }

            dialog = string.Empty;
            return false;
        }

        public static bool TryCreateRequest(YbDbLegacy77Identity identity,
            DateTime currentDate, out YbDbLegacy77Frame frame, out string error)
        {
            frame = null;
            if (!YbDbLegacy77Codec.TryEncodeNativeIdentity(identity,
                    out var payload, out error))
                return false;

            frame = new YbDbLegacy77Frame(GetMonthSerial(currentDate),
                RequestParam, RequestIdent, payload);
            return true;
        }

        public static bool TryDecodeResponse(YbDbLegacy77Frame frame,
            out Response response, out string error)
        {
            response = null;
            error = string.Empty;
            if (frame == null)
            {
                error = "fee-user activity response frame is null";
                return false;
            }
            if (frame.Ident != ResponseIdent)
            {
                error = $"fee-user activity response Ident must be {ResponseIdent}";
                return false;
            }

            var payload = frame.Payload ?? Array.Empty<byte>();
            if (payload.Length != ResponsePayloadSize)
            {
                error = $"fee-user activity response payload must be " +
                        $"{ResponsePayloadSize} bytes";
                return false;
            }
            if (!YbDbLegacy77Codec.TryDecodeShortString(payload,
                    RoleNameOffset, RoleNameMaximumGbkBytes,
                    out var roleName, out error))
                return false;

            response = new Response(frame.QueryId, frame.Param, roleName,
                BinaryPrimitives.ReadInt32LittleEndian(
                    payload.AsSpan(CurrentYuanbaoOffset, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(
                    payload.AsSpan(TotalConsumedOffset, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(
                    payload.AsSpan(RemainingSecondsOffset, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(
                    payload.AsSpan(DividendConsumedOffset, 4)));
            return true;
        }

        public static bool ShouldGrant(Response response, DateTime currentDate,
            ushort persistedClaimMonth)
        {
            if (response == null) return false;
            var currentMonth = GetMonthSerial(currentDate);
            return response.MonthSerial == currentMonth
                   && response.Result > 0
                   && persistedClaimMonth != currentMonth;
        }

        public sealed class Response
        {
            internal Response(int result, int monthSerial, string roleName,
                int currentYuanbao, int totalConsumed, int remainingSeconds,
                int dividendConsumed)
            {
                Result = result;
                MonthSerial = monthSerial;
                RoleName = roleName;
                CurrentYuanbao = currentYuanbao;
                TotalConsumed = totalConsumed;
                RemainingSeconds = remainingSeconds;
                DividendConsumed = dividendConsumed;
            }

            public int Result { get; }
            public int MonthSerial { get; }
            public string RoleName { get; }
            public int CurrentYuanbao { get; }
            public int TotalConsumed { get; }
            public int RemainingSeconds { get; }
            public int DividendConsumed { get; }
        }
    }
}
