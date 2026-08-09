using System;
using System.Buffers.Binary;
using System.Text;

namespace SystemModule.Packet
{
    /// <summary>
    /// Codec for the meaningful fields in the native M2 award-code worker
    /// task. This is an internal FIFO payload, not a YBDB wire frame.
    /// </summary>
    public static class NativeAwardCodeTaskCodec
    {
        public const byte QueryTaskType = 3;
        public const int PayloadSize = 104;
        public const int CodeOffset = 0;
        public const int CodeMaximumGbkBytes = 60;
        public const int PlayerIdOffset = 80;
        public const int RoleNameOffset = 88;
        public const int RoleNameMaximumGbkBytes = 15;
        public const int MinimumQueueAgeMilliseconds = 200;

        public const int QueryMiss = 0;
        public const int QueryHit = 1;
        public const string CallbackLabel = "@AwardCodeExecCallBack";
        public const string QuerySqlFormat =
            "Select AwardCodeType,ActiveParam,ScriptParam1,ScriptParam2," +
            "OwnerPlayerID,OwnerChrName from gamedata.awardcodes " +
            "where AwardCode like '%s';";

        private static readonly Encoding Gbk;

        static NativeAwardCodeTaskCodec()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Gbk = Encoding.GetEncoding(936,
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback);
        }

        public static bool TryEncodeQuery(string code, long playerId,
            string roleName, out byte[] payload, out string error)
        {
            payload = new byte[PayloadSize];
            error = string.Empty;
            if (!TryWriteNativeShortString(payload, CodeOffset,
                    CodeMaximumGbkBytes, code, out error)
                || !TryWriteNativeShortString(payload, RoleNameOffset,
                    RoleNameMaximumGbkBytes, roleName, out error))
            {
                payload = null;
                return false;
            }

            BinaryPrimitives.WriteInt64LittleEndian(
                payload.AsSpan(PlayerIdOffset, sizeof(long)), playerId);
            return true;
        }

        public static bool TryDecodeQuery(ReadOnlySpan<byte> payload,
            out QueryTask task, out string error)
        {
            task = null;
            error = string.Empty;
            if (payload.Length != PayloadSize)
            {
                error = $"award-code task payload must be {PayloadSize} bytes";
                return false;
            }
            if (!TryReadShortStringBytes(payload, CodeOffset,
                    CodeMaximumGbkBytes, out var codeBytes, out error)
                || !TryReadShortStringBytes(payload, RoleNameOffset,
                    RoleNameMaximumGbkBytes, out var roleNameBytes, out error))
                return false;

            task = new QueryTask(codeBytes,
                BinaryPrimitives.ReadInt64LittleEndian(
                    payload.Slice(PlayerIdOffset, sizeof(long))),
                roleNameBytes);
            return true;
        }

        public static QueryCallback CreateQueryCallback(int queryRowCount,
            byte[] codeBytes, int awardCodeType, int activeParam)
        {
            var hit = queryRowCount > 0;
            return new QueryCallback(hit ? QueryHit : QueryMiss,
                codeBytes ?? Array.Empty<byte>(),
                hit ? awardCodeType : 0,
                hit ? activeParam : 0);
        }

        private static bool TryWriteNativeShortString(Span<byte> destination,
            int offset, int maximumLength, string value, out string error)
        {
            error = string.Empty;
            byte[] bytes;
            try
            {
                bytes = Gbk.GetBytes(value ?? string.Empty);
            }
            catch (EncoderFallbackException ex)
            {
                error = "award-code task string is not GBK: " + ex.Message;
                return false;
            }

            var length = Math.Min(bytes.Length, maximumLength);
            destination[offset] = (byte)length;
            bytes.AsSpan(0, length).CopyTo(destination.Slice(offset + 1));
            return true;
        }

        private static bool TryReadShortStringBytes(ReadOnlySpan<byte> source,
            int offset, int maximumLength, out byte[] value, out string error)
        {
            value = null;
            error = string.Empty;
            var length = source[offset];
            if (length > maximumLength)
            {
                error = $"award-code task ShortString length {length} " +
                        $"exceeds {maximumLength} at 0x{offset:X}";
                return false;
            }

            value = source.Slice(offset + 1, length).ToArray();
            return true;
        }

        public sealed class QueryTask
        {
            internal QueryTask(byte[] codeBytes, long playerId,
                byte[] roleNameBytes)
            {
                CodeBytes = codeBytes;
                PlayerId = playerId;
                RoleNameBytes = roleNameBytes;
            }

            public byte[] CodeBytes { get; }
            public long PlayerId { get; }
            public byte[] RoleNameBytes { get; }
        }

        public sealed class QueryCallback
        {
            internal QueryCallback(int result, byte[] codeBytes,
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
