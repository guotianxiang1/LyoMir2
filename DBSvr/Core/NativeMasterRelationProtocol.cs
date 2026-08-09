using System;
using System.Buffers.Binary;
using SystemModule.Packet;

namespace DBSvr.Core
{
    public sealed class NativeMasterRelationResetRequest
    {
        public ushort Subcommand { get; init; }
        public byte[] Account { get; init; } = Array.Empty<byte>();
        public byte[] MasterName { get; init; } = Array.Empty<byte>();
        public byte[] StudentName { get; init; } = Array.Empty<byte>();
    }

    /// <summary>
    /// Native Type1 0x0152 relationship mutations.
    /// The original response is a zeroed 0x48-byte body with only word +2 set.
    /// </summary>
    public static class NativeMasterRelationProtocol
    {
        public const ushort RequestCommand = 0x0152;
        public const ushort MarriageClearSubcommand = 0;
        public const ushort ClearSubcommand = 3;
        public const ushort ResetSubcommand = 7;
        public const int HeaderSize = 0x48;
        public const int AccountOffset = 0x10;
        public const int MasterNameOffset = 0x25;
        public const int StudentNameOffset = 0x35;

        public static bool TryDecode(LegacyDbServerFrame frame,
            out NativeMasterRelationResetRequest request, out string error)
        {
            request = null;
            error = string.Empty;
            if (frame == null || frame.Type != 1
                || frame.Payload.Length < HeaderSize)
            {
                error = "native 0x0152 envelope is invalid";
                return false;
            }

            var payload = frame.Payload.AsSpan();
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload)
                != RequestCommand)
            {
                error = "native 0x0152 command mismatch";
                return false;
            }
            var subcommand = BinaryPrimitives.ReadUInt16LittleEndian(
                payload.Slice(2, 2));
            if (subcommand != MarriageClearSubcommand
                && subcommand != ClearSubcommand
                && subcommand != ResetSubcommand)
            {
                error = "native 0x0152 subtype is unsupported";
                return false;
            }
            if (!TryReadShortString(payload, AccountOffset, 20,
                    out var account, out error)
                || !TryReadShortString(payload, MasterNameOffset, 15,
                    out var masterName, out error)
                || !TryReadShortString(payload, StudentNameOffset, 15,
                    out var studentName, out error))
                return false;

            request = new NativeMasterRelationResetRequest
            {
                Subcommand = subcommand,
                Account = account,
                MasterName = masterName,
                StudentName = studentName
            };
            return true;
        }

        public static bool TryDecodeReset(LegacyDbServerFrame frame,
            out NativeMasterRelationResetRequest request, out string error)
        {
            if (!TryDecode(frame, out request, out error)) return false;
            if (request.Subcommand == ResetSubcommand) return true;
            request = null;
            error = "native 0x0152 subtype is not reset-master";
            return false;
        }

        public static bool EqualsAsciiIgnoreCase(ReadOnlySpan<byte> left,
            ReadOnlySpan<byte> right)
        {
            if (left.Length != right.Length) return false;
            for (var i = 0; i < left.Length; i++)
            {
                var a = FoldAscii(left[i]);
                var b = FoldAscii(right[i]);
                if (a != b) return false;
            }
            return true;
        }

        public static LegacyDbServerFrame CreateResetResponse(bool succeeded)
        {
            var payload = new byte[HeaderSize];
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2),
                succeeded ? (ushort)1 : (ushort)0);
            return new LegacyDbServerFrame(1, 0, payload);
        }

        private static bool TryReadShortString(ReadOnlySpan<byte> source,
            int offset, int capacity, out byte[] value, out string error)
        {
            value = null;
            error = string.Empty;
            var length = source[offset];
            if (length > capacity)
            {
                error = $"native 0x0152 ShortString exceeds {capacity} bytes";
                return false;
            }
            value = source.Slice(offset + 1, length).ToArray();
            return true;
        }

        private static byte FoldAscii(byte value) => value is >= (byte)'a'
            and <= (byte)'z' ? (byte)(value - ('a' - 'A')) : value;
    }
}
