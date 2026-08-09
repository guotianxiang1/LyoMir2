using System;
using System.Buffers.Binary;

namespace SystemModule.Packet
{
    public static class YbDbCreditProtocol
    {
        public const ushort RequestIdent = 103;
        public const ushort ResponseIdent = 1103;
        public const int InitialQueryId = 0;
        public const int RefreshQueryId = 1;
        public const int ResponsePayloadSize = 32;
        public const int ResponseRoleCapacity = 15;

        public static bool TryCreateInitialRequest(YbDbLegacy77Identity identity,
            ushort payment, bool firstUsedGiftQualified,
            out YbDbLegacy77Frame frame, out string error)
        {
            return TryCreateRequest(identity, payment, firstUsedGiftQualified,
                InitialQueryId, out frame, out error);
        }

        public static bool TryCreateRefreshRequest(YbDbLegacy77Identity identity,
            ushort payment, bool firstUsedGiftQualified,
            out YbDbLegacy77Frame frame, out string error)
        {
            return TryCreateRequest(identity, payment, firstUsedGiftQualified,
                RefreshQueryId, out frame, out error);
        }

        private static bool TryCreateRequest(YbDbLegacy77Identity identity,
            ushort payment, bool firstUsedGiftQualified, int queryId,
            out YbDbLegacy77Frame frame, out string error)
        {
            frame = null;
            if (!YbDbLegacy77Codec.TryEncodeNativeIdentity(identity,
                    out var payload, out error))
                return false;

            var param = (int)payment;
            if (firstUsedGiftQualified) param |= 1 << 16;
            frame = new YbDbLegacy77Frame(queryId, param,
                RequestIdent, payload);
            return true;
        }

        public static bool TryDecodeResponse(YbDbLegacy77Frame frame,
            out YbDbCreditSnapshot snapshot, out string error)
        {
            snapshot = null;
            error = string.Empty;
            if (frame == null || frame.Ident != ResponseIdent)
            {
                error = "legacy YBDB credit response Ident must be 1103";
                return false;
            }
            if (frame.Payload.Length != ResponsePayloadSize)
            {
                error = "legacy YBDB credit response payload must be 32 bytes";
                return false;
            }
            if (!YbDbLegacy77Codec.TryDecodeShortString(frame.Payload, 0,
                    ResponseRoleCapacity, out var roleName, out error))
                return false;

            snapshot = new YbDbCreditSnapshot(roleName,
                BinaryPrimitives.ReadInt32LittleEndian(frame.Payload.AsSpan(16, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(frame.Payload.AsSpan(20, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(frame.Payload.AsSpan(24, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(frame.Payload.AsSpan(28, 4)),
                frame.Param == 1);
            return true;
        }
    }

    public sealed class YbDbCreditSnapshot
    {
        public YbDbCreditSnapshot(string roleName, int currentYuanbao,
            int totalConsumed, int remainingSeconds, int dividendConsumed,
            bool responseParamIsOne)
        {
            RoleName = roleName;
            CurrentYuanbao = currentYuanbao;
            TotalConsumed = totalConsumed;
            RemainingSeconds = remainingSeconds;
            DividendConsumed = dividendConsumed;
            ResponseParamIsOne = responseParamIsOne;
        }

        public string RoleName { get; }
        public int CurrentYuanbao { get; }
        public int TotalConsumed { get; }
        public int RemainingSeconds { get; }
        public int DividendConsumed { get; }
        public bool ResponseParamIsOne { get; }
    }
}
