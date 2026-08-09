using System;
using System.Buffers.Binary;
using SystemModule.Packet;

namespace DBSvr.Core
{
    public sealed class NativeDynamicImageRequest
    {
        public byte[] Account { get; init; } = Array.Empty<byte>();
        public byte[] CharacterName { get; init; } = Array.Empty<byte>();
    }

    public sealed class NativeDynamicImage
    {
        public byte[] Name { get; init; } = Array.Empty<byte>();
        public byte[] Metadata { get; init; } = Array.Empty<byte>();
        public byte[] Data { get; init; } = Array.Empty<byte>();
    }

    public static class NativeAuxiliaryType1Protocol
    {
        public const ushort RegisterCharacterNameCommand = 0x0157;
        public const ushort DynamicImageRequestCommand = 0x0159;
        public const ushort DynamicImageResponseCommand = 0x005F;
        public const int HeaderSize = 0x48;

        private const int AccountOffset = 0x10;
        private const int AccountCapacity = 20;
        private const int CharacterOffset = 0x25;
        private const int CharacterCapacity = 15;
        private const int DynamicNameOffset = 0x35;
        private const int DynamicNameCapacity = 15;
        private const int DynamicMetadataOffset = 0x48;
        private const int DynamicMetadataSize = 12;
        private const int DynamicDataOffset = 0x54;

        public static bool TryDecodeCharacterNameRegistration(
            LegacyDbServerFrame frame, out byte[] characterName,
            out string error)
        {
            characterName = null;
            error = string.Empty;
            if (!TryGetPayload(frame, RegisterCharacterNameCommand,
                    out var payload, out error))
                return false;
            return TryReadShortString(payload, DynamicNameOffset,
                DynamicNameCapacity, out characterName, out error);
        }

        public static bool TryDecodeDynamicImageRequest(
            LegacyDbServerFrame frame, out NativeDynamicImageRequest request,
            out string error)
        {
            request = null;
            error = string.Empty;
            if (!TryGetPayload(frame, DynamicImageRequestCommand,
                    out var payload, out error)
                || !TryReadShortString(payload, AccountOffset,
                    AccountCapacity, out var account, out error)
                || !TryReadShortString(payload, CharacterOffset,
                    CharacterCapacity, out var characterName, out error))
                return false;
            request = new NativeDynamicImageRequest
            {
                Account = account,
                CharacterName = characterName
            };
            return true;
        }

        public static LegacyDbServerFrame CreateDynamicImageResponse(
            NativeDynamicImageRequest request, NativeDynamicImage image = null)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var hasImage = image != null;
            var imageData = image?.Data ?? Array.Empty<byte>();
            var payload = new byte[checked(HeaderSize
                                           + (hasImage
                                               ? DynamicMetadataSize
                                                 + imageData.Length
                                               : 0))];
            BinaryPrimitives.WriteUInt16LittleEndian(payload,
                DynamicImageResponseCommand);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4),
                hasImage ? 1 : 0);
            WriteShortString(payload, AccountOffset, AccountCapacity,
                request.Account);
            WriteShortString(payload, CharacterOffset, CharacterCapacity,
                request.CharacterName);
            if (hasImage)
            {
                WriteShortString(payload, DynamicNameOffset,
                    DynamicNameCapacity, image.Name);
                var metadata = image.Metadata ?? Array.Empty<byte>();
                metadata.AsSpan(0, Math.Min(DynamicMetadataSize,
                        metadata.Length))
                    .CopyTo(payload.AsSpan(DynamicMetadataOffset));
                imageData.CopyTo(payload, DynamicDataOffset);
            }
            return new LegacyDbServerFrame(1, 0, payload);
        }

        private static bool TryGetPayload(LegacyDbServerFrame frame,
            ushort command, out ReadOnlySpan<byte> payload, out string error)
        {
            payload = default;
            error = string.Empty;
            if (frame == null || frame.Type != 1)
            {
                error = $"native 0x{command:X4} envelope is invalid";
                return false;
            }
            if (frame.Payload.Length < HeaderSize)
            {
                error = $"native 0x{command:X4} payload is shorter than 0x48";
                return false;
            }
            payload = frame.Payload;
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload) != command)
            {
                error = $"native 0x{command:X4} command mismatch";
                return false;
            }
            return true;
        }

        private static bool TryReadShortString(ReadOnlySpan<byte> source,
            int offset, int capacity, out byte[] value, out string error)
        {
            value = null;
            error = string.Empty;
            var length = source[offset];
            if (length > capacity)
            {
                error = $"native ShortString length exceeds {capacity}";
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
