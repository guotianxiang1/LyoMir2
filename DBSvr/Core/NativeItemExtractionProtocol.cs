using System;
using System.Buffers.Binary;
using SystemModule.Packet;

namespace DBSvr.Core
{
    public sealed class NativeItemExtractionRequest
    {
        public int MakeIndex { get; init; }
        public byte[] Account { get; init; } = Array.Empty<byte>();
        public byte[] RequesterName { get; init; } = Array.Empty<byte>();
        public byte[] TargetName { get; init; } = Array.Empty<byte>();
    }

    public sealed class NativeItemExtractionResponse
    {
        public int MakeIndex { get; init; }
        public ushort Status { get; init; }
        public byte[] Account { get; init; } = Array.Empty<byte>();
        public byte[] RequesterName { get; init; } = Array.Empty<byte>();
        public byte[] TargetName { get; init; } = Array.Empty<byte>();
        public byte[] ItemRecord { get; init; } = Array.Empty<byte>();
    }

    /// <summary>Native Type1 0x0153 GetUserItem request and 0x0055 reply.</summary>
    public static class NativeItemExtractionProtocol
    {
        public const ushort RequestCommand = 0x0153;
        public const ushort ResponseCommand = 0x0055;
        public const int HeaderSize = 0x48;
        public const int ItemSize = NativeHumanDataCodec.ItemRecordSize;

        public const ushort Success = 1;
        public const ushort ItemNotFound = 2;
        public const ushort CharacterBusy = 3;
        public const ushort CharacterDeleted = 4;

        public static LegacyDbServerFrame CreateRequest(int makeIndex,
            byte[] account, byte[] requesterName, byte[] targetName)
        {
            var payload = new byte[HeaderSize];
            BinaryPrimitives.WriteUInt16LittleEndian(payload,
                RequestCommand);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4),
                makeIndex);
            WriteShortString(payload, 0x10, 20, account);
            WriteShortString(payload, 0x25, 15, requesterName);
            WriteShortString(payload, 0x35, 15, targetName);
            return new LegacyDbServerFrame(1, 0, payload);
        }

        public static bool TryDecode(LegacyDbServerFrame frame,
            out NativeItemExtractionRequest request, out string error)
        {
            request = null;
            error = string.Empty;
            if (frame == null || frame.Type != 1
                || frame.Payload.Length < HeaderSize)
            {
                error = "native 0x0153 envelope is invalid";
                return false;
            }

            var payload = frame.Payload.AsSpan();
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload)
                != RequestCommand)
            {
                error = "native 0x0153 command mismatch";
                return false;
            }
            if (!TryReadShortString(payload, 0x10, 20,
                    out var account, out error)
                || !TryReadShortString(payload, 0x25, 15,
                    out var requesterName, out error)
                || !TryReadShortString(payload, 0x35, 15,
                    out var targetName, out error))
                return false;

            request = new NativeItemExtractionRequest
            {
                MakeIndex = BinaryPrimitives.ReadInt32LittleEndian(
                    payload.Slice(4, 4)),
                Account = account,
                RequesterName = requesterName,
                TargetName = targetName
            };
            return true;
        }

        public static bool TryDecodeResponse(LegacyDbServerFrame frame,
            out NativeItemExtractionResponse response, out string error)
        {
            response = null;
            error = string.Empty;
            if (frame == null || frame.Type != 1
                || frame.Payload.Length < HeaderSize)
            {
                error = "native 0x0055 envelope is invalid";
                return false;
            }

            var payload = frame.Payload.AsSpan();
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload)
                != ResponseCommand)
            {
                error = "native 0x0055 command mismatch";
                return false;
            }
            if (!TryReadShortString(payload, 0x10, 20,
                    out var account, out error)
                || !TryReadShortString(payload, 0x25, 15,
                    out var requesterName, out error)
                || !TryReadShortString(payload, 0x35, 15,
                    out var targetName, out error))
                return false;

            response = new NativeItemExtractionResponse
            {
                MakeIndex = BinaryPrimitives.ReadInt32LittleEndian(
                    payload.Slice(4, 4)),
                Status = BinaryPrimitives.ReadUInt16LittleEndian(
                    payload.Slice(2, 2)),
                Account = account,
                RequesterName = requesterName,
                TargetName = targetName,
                ItemRecord = payload.Slice(HeaderSize).ToArray()
            };
            return true;
        }

        public static LegacyDbServerFrame CreateResponse(
            NativeItemExtractionRequest request, ushort status,
            byte[] itemRecord = null)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (status == Success
                && itemRecord?.Length != ItemSize)
                throw new ArgumentException(
                    "native 0x0055 success requires one 0xD0 item record",
                    nameof(itemRecord));

            var payload = new byte[HeaderSize
                                   + (status == Success ? ItemSize : 0)];
            BinaryPrimitives.WriteUInt16LittleEndian(payload,
                ResponseCommand);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2),
                status);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4),
                request.MakeIndex);
            WriteShortString(payload, 0x10, 20, request.Account);
            WriteShortString(payload, 0x25, 15, request.RequesterName);
            WriteShortString(payload, 0x35, 15, request.TargetName);
            if (status == Success)
                itemRecord.CopyTo(payload, HeaderSize);
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
                error = $"native GetUserItem ShortString exceeds {capacity} bytes";
                return false;
            }
            value = source.Slice(offset + 1, length).ToArray();
            return true;
        }

        private static void WriteShortString(Span<byte> destination,
            int offset, int capacity, byte[] value)
        {
            value ??= Array.Empty<byte>();
            var length = Math.Min(capacity, value.Length);
            destination[offset] = (byte)length;
            value.AsSpan(0, length).CopyTo(destination.Slice(offset + 1));
        }
    }
}
