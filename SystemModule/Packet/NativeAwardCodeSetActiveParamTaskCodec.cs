using System;
using System.Buffers.Binary;

namespace SystemModule.Packet
{
    /// <summary>
    /// Meaningful-field codec for the native SetAwardCodeActiveParam task. It
    /// does not itself enqueue work, access SQL, or invoke script callbacks.
    /// </summary>
    public static class NativeAwardCodeSetActiveParamTaskCodec
    {
        public const byte TaskType = 2;
        public const int PayloadSize = NativeAwardCodeTaskCodec.PayloadSize;
        public const int ActiveParamOffset = 68;
        public const int MinimumQueueAgeMilliseconds =
            NativeAwardCodeTaskCodec.MinimumQueueAgeMilliseconds;

        public const int SuccessResult = 2;
        public const int FailureResult = 5;
        public const string CallbackLabel = NativeAwardCodeTaskCodec.CallbackLabel;
        public const string SelectSqlFormat = NativeAwardCodeTaskCodec.QuerySqlFormat;
        public const string UpdateSqlFormat =
            "Update gamedata.awardcodes  set ActiveParam = %d, " +
            "OwnerPlayerID = %d, OwnerChrName = '%s', " +
            "ModifyDate = Now() where AwardCode like '%s';";

        public static bool TryEncode(string code, int activeParam,
            long playerId, string roleName, out byte[] payload, out string error)
        {
            if (!NativeAwardCodeTaskCodec.TryEncodeQuery(code, playerId,
                    roleName, out payload, out error))
                return false;

            BinaryPrimitives.WriteInt32LittleEndian(
                payload.AsSpan(ActiveParamOffset, sizeof(int)), activeParam);
            return true;
        }

        public static bool TryDecode(ReadOnlySpan<byte> payload,
            out SetActiveParamTask task, out string error)
        {
            task = null;
            if (!NativeAwardCodeTaskCodec.TryDecodeQuery(payload,
                    out var common, out error))
                return false;

            task = new SetActiveParamTask(common.CodeBytes,
                BinaryPrimitives.ReadInt32LittleEndian(
                    payload.Slice(ActiveParamOffset, sizeof(int))),
                common.PlayerId, common.RoleNameBytes);
            return true;
        }

        public static bool CanUpdate(int selectedRowCount,
            long selectedOwnerPlayerId, long requestingPlayerId)
        {
            return selectedRowCount > 0
                   && (selectedOwnerPlayerId == 0
                       || selectedOwnerPlayerId == requestingPlayerId);
        }

        public static UpdateCallback CreateCallback(bool updateExecuted,
            byte[] codeBytes, int selectedAwardCodeType,
            int requestedActiveParam)
        {
            return new UpdateCallback(
                updateExecuted ? SuccessResult : FailureResult,
                codeBytes ?? Array.Empty<byte>(),
                updateExecuted ? selectedAwardCodeType : 0,
                updateExecuted ? requestedActiveParam : 0);
        }

        public sealed class SetActiveParamTask
        {
            internal SetActiveParamTask(byte[] codeBytes, int activeParam,
                long playerId, byte[] roleNameBytes)
            {
                CodeBytes = codeBytes;
                ActiveParam = activeParam;
                PlayerId = playerId;
                RoleNameBytes = roleNameBytes;
            }

            public byte[] CodeBytes { get; }
            public int ActiveParam { get; }
            public long PlayerId { get; }
            public byte[] RoleNameBytes { get; }
        }

        public sealed class UpdateCallback
        {
            internal UpdateCallback(int result, byte[] codeBytes,
                int awardCodeType, int activeParam)
            {
                Result = result;
                CodeBytes = codeBytes;
                AwardCodeType = awardCodeType;
                ActiveParam = activeParam;
            }

            public int Result { get; }
            public byte[] CodeBytes { get; }
            public int AwardCodeType { get; }
            public int ActiveParam { get; }
        }
    }
}
