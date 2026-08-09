using System;
using System.Buffers.Binary;
using SystemModule.Packet;

namespace DBSvr.Core
{
    public sealed class NativeItemInjectionRequest
    {
        public ushort Command { get; init; }
        public int Correlation { get; init; }
        public byte[] Account { get; init; } = Array.Empty<byte>();
        public byte[] CharacterName { get; init; } = Array.Empty<byte>();
        public byte[] TargetName { get; init; } = Array.Empty<byte>();
        public byte[] Attachment { get; init; } = Array.Empty<byte>();
        public bool OuterLengthValid { get; init; }
    }

    public sealed class NativeItemInjectionResponse
    {
        public ushort Status { get; init; }
        public int MakeIndex { get; init; }
        public byte[] Account { get; init; } = Array.Empty<byte>();
        public byte[] CharacterName { get; init; } = Array.Empty<byte>();
        public byte[] TargetName { get; init; } = Array.Empty<byte>();
    }

    public static class NativeItemInjectionProtocol
    {
        public const ushort MailRequestCommand = 0x0154;
        public const ushort MailResponseCommand = 0x0056;
        public const ushort BagRequestCommand = 0x015A;
        public const ushort BagResponseCommand = 0x0060;
        public const int HeaderSize = 0x48;
        public const int ItemSize = NativeHumanDataCodec.ItemRecordSize;
        public const ushort Success = 1;

        public static bool TryDecodeMail(LegacyDbServerFrame frame,
            out NativeItemInjectionRequest request, out string error)
        {
            if (!TryDecodeHeader(frame, MailRequestCommand, out request,
                    out error))
                return false;
            if (request.Attachment.Length < ItemSize)
            {
                request = null;
                error = "native 0x0154 attachment is shorter than 0xD0";
                return false;
            }
            return true;
        }

        public static bool TryDecodeBag(LegacyDbServerFrame frame,
            out NativeItemInjectionRequest request, out string error) =>
            TryDecodeHeader(frame, BagRequestCommand, out request, out error);

        public static bool TryDecodeMailResponse(LegacyDbServerFrame frame,
            out NativeItemInjectionResponse response, out string error)
        {
            response = null;
            error = string.Empty;
            if (frame == null || frame.Type != 1
                || frame.Payload.Length < HeaderSize)
            {
                error = "native 0x0056 envelope is invalid";
                return false;
            }

            var payload = frame.Payload.AsSpan();
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload)
                != MailResponseCommand)
            {
                error = "native 0x0056 command mismatch";
                return false;
            }
            if (!TryReadShortString(payload, 0x10, 20,
                    out var account, out error)
                || !TryReadShortString(payload, 0x25, 15,
                    out var characterName, out error)
                || !TryReadShortString(payload, 0x35, 15,
                    out var targetName, out error))
                return false;

            response = new NativeItemInjectionResponse
            {
                Status = BinaryPrimitives.ReadUInt16LittleEndian(
                    payload.Slice(2, 2)),
                MakeIndex = BinaryPrimitives.ReadInt32LittleEndian(
                    payload.Slice(4, 4)),
                Account = account,
                CharacterName = characterName,
                TargetName = targetName
            };
            return true;
        }

        public static LegacyDbServerFrame CreateMailResponse(
            NativeItemInjectionRequest request, ushort result)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var payload = CreateResponseHeader(request, MailResponseCommand,
                result);
            var makeIndex = request.Attachment.Length >= 4
                ? BinaryPrimitives.ReadInt32LittleEndian(request.Attachment)
                : 0;
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4),
                makeIndex);
            return new LegacyDbServerFrame(1, 0, payload);
        }

        public static LegacyDbServerFrame CreateBagResponse(
            NativeItemInjectionRequest request, ushort result)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var payload = CreateResponseHeader(request, BagResponseCommand,
                result);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4),
                request.Correlation);
            return new LegacyDbServerFrame(1, 0, payload);
        }

        private static bool TryDecodeHeader(LegacyDbServerFrame frame,
            ushort command, out NativeItemInjectionRequest request,
            out string error)
        {
            request = null;
            error = string.Empty;
            if (frame == null || frame.Type != 1
                || frame.Payload.Length < HeaderSize)
            {
                error = $"native 0x{command:X4} envelope is invalid";
                return false;
            }
            var payload = frame.Payload.AsSpan();
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload) != command)
            {
                error = $"native 0x{command:X4} command mismatch";
                return false;
            }
            if (!TryReadShortString(payload, 0x10, 20,
                    out var account, out error)
                || !TryReadShortString(payload, 0x25, 15,
                    out var characterName, out error)
                || !TryReadShortString(payload, 0x35, 15,
                    out var targetName, out error))
                return false;
            var attachment = payload.Slice(HeaderSize).ToArray();
            request = new NativeItemInjectionRequest
            {
                Command = command,
                Correlation = BinaryPrimitives.ReadInt32LittleEndian(
                    payload.Slice(4, 4)),
                Account = account,
                CharacterName = characterName,
                TargetName = targetName,
                Attachment = attachment,
                OuterLengthValid = attachment.Length > 0
                                   && attachment.Length % ItemSize == 0
            };
            return true;
        }

        private static byte[] CreateResponseHeader(
            NativeItemInjectionRequest request, ushort command, ushort result)
        {
            var payload = new byte[HeaderSize];
            BinaryPrimitives.WriteUInt16LittleEndian(payload, command);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2),
                result);
            WriteShortString(payload, 0x10, 20, request.Account);
            WriteShortString(payload, 0x25, 15, request.CharacterName);
            WriteShortString(payload, 0x35, 15, request.TargetName);
            return payload;
        }

        private static bool TryReadShortString(ReadOnlySpan<byte> source,
            int offset, int capacity, out byte[] value, out string error)
        {
            value = null;
            error = string.Empty;
            var length = source[offset];
            if (length > capacity)
            {
                error = $"native item ShortString exceeds {capacity} bytes";
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
