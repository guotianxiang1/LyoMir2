using System;
using System.Buffers.Binary;

namespace SystemModule.Packet
{
    /// <summary>
    /// Dormant codec for the native ClientAskOpenYB 112/1112 exchange.
    /// Runtime dispatch remains closed until the external 6108 authority exists.
    /// </summary>
    public static class YbDbOpenDealProtocol
    {
        public const ushort RequestIdent = 112;
        public const ushort ResponseIdent = 1112;
        public const int RequestQueryId = 0;
        public const int RequestParam = 0;
        public const int ResponsePayloadSize = 32;
        public const int ResponseRoleNameCapacity = 15;
        public const int OpenDealClientIdent = 3009;

        public const int SuccessResult = 1;
        public const int NoRechargeResult = -1;
        public const int InsufficientYuanbaoResult = -2;
        public const int AlreadyOpenedResult = -3;

        public const string SuccessDialog =
            "成功开启元宝交易系统！\\ \\<返回/@main>";
        public const string NoRechargeDialog =
            "请先进行元宝冲值！\\ \\<返回/@main>";
        public const string InsufficientYuanbaoDialog =
            "您的元宝数量不足开启交易系统！\\ \\<返回/@main>";
        public const string AlreadyOpenedDialog =
            "[失败]：您已经开启元宝交易系统！\\ \\<返回/@main>";
        public const string FailureDialog =
            "开通元宝交易系统失败！ \\ \\<返回/@main>";
        public const string RequestUnavailableDialog =
            "元宝系统暂时关闭中...\\ \\ \\ <返回/@main>";

        public static bool TryCreateRequest(YbDbLegacy77Identity identity,
            out YbDbLegacy77Frame frame, out string error)
        {
            frame = null;
            if (!YbDbLegacy77Codec.TryEncodeNativeIdentity(identity,
                    out var payload, out error))
                return false;

            frame = new YbDbLegacy77Frame(RequestQueryId, RequestParam,
                RequestIdent, payload);
            return true;
        }

        public static bool TryDecodeResponse(YbDbLegacy77Frame frame,
            out YbDbOpenDealResult result, out string error)
        {
            result = null;
            error = string.Empty;
            if (frame == null || frame.Ident != ResponseIdent)
            {
                error = "legacy YBDB open-deal response Ident must be 1112";
                return false;
            }
            if (frame.Payload.Length != ResponsePayloadSize)
            {
                error = "legacy YBDB open-deal response payload must be 32 bytes";
                return false;
            }
            if (!YbDbLegacy77Codec.TryDecodeShortString(frame.Payload, 0,
                    ResponseRoleNameCapacity, out var roleName, out error))
                return false;

            result = new YbDbOpenDealResult(
                roleName,
                frame.QueryId,
                BinaryPrimitives.ReadInt32LittleEndian(frame.Payload.AsSpan(16, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(frame.Payload.AsSpan(20, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(frame.Payload.AsSpan(24, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(frame.Payload.AsSpan(28, 4)));
            return true;
        }

        public static string GetDialog(int resultCode)
        {
            return resultCode switch
            {
                SuccessResult => SuccessDialog,
                NoRechargeResult => NoRechargeDialog,
                InsufficientYuanbaoResult => InsufficientYuanbaoDialog,
                AlreadyOpenedResult => AlreadyOpenedDialog,
                _ => FailureDialog
            };
        }
    }

    public sealed class YbDbOpenDealResult
    {
        public YbDbOpenDealResult(string roleName, int resultCode,
            int currentYuanbao, int totalConsumed, int remainingSeconds,
            int dividendConsumed)
        {
            RoleName = roleName;
            ResultCode = resultCode;
            CurrentYuanbao = currentYuanbao;
            TotalConsumed = totalConsumed;
            RemainingSeconds = remainingSeconds;
            DividendConsumed = dividendConsumed;
        }

        public string RoleName { get; }
        public int ResultCode { get; }
        public int CurrentYuanbao { get; }
        public int TotalConsumed { get; }
        public int RemainingSeconds { get; }
        public int DividendConsumed { get; }
        public bool OpensDeal =>
            ResultCode == YbDbOpenDealProtocol.SuccessResult;
        public string Dialog => YbDbOpenDealProtocol.GetDialog(ResultCode);
    }
}
