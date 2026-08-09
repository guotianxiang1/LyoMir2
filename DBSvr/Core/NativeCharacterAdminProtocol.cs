using System;
using System.Buffers.Binary;
using SystemModule.Packet;

namespace DBSvr.Core
{
    public sealed class NativeCharacterRestoreRequest
    {
        public byte[] OperatorAccount { get; init; } = Array.Empty<byte>();
        public byte[] OperatorCharacter { get; init; } = Array.Empty<byte>();
        public byte[] TargetCharacter { get; init; } = Array.Empty<byte>();
    }

    public sealed class NativeCharacterLookupRequest
    {
        public ushort Mode { get; init; }
        public byte[] CharacterName { get; init; } = Array.Empty<byte>();
        public byte[] Tail { get; init; } = Array.Empty<byte>();
    }

    public static class NativeCharacterAdminProtocol
    {
        public const ushort RestoreRequestCommand = 0x019A;
        public const ushort RestoreResponseCommand = 0x0138;
        public const ushort LookupRequestCommand = 0x019B;
        public const ushort LookupResponseCommand = 0x0139;
        public const int HeaderSize = 0x48;
        public const int LookupMinimumTailSize = 0x30;

        public static bool TryDecodeRestore(LegacyDbServerFrame frame,
            out NativeCharacterRestoreRequest request, out string error)
        {
            request = null;
            error = string.Empty;
            if (!HasHeader(frame, RestoreRequestCommand))
            {
                error = "native 0x019A envelope is invalid";
                return false;
            }
            var payload = frame.Payload.AsSpan();
            if (!TryReadShortString(payload, 0x10, 20,
                    out var account, out error)
                || !TryReadShortString(payload, 0x25, 15,
                    out var character, out error)
                || !TryReadShortString(payload, 0x35, 15,
                    out var target, out error))
                return false;
            request = new NativeCharacterRestoreRequest
            {
                OperatorAccount = account,
                OperatorCharacter = character,
                TargetCharacter = target
            };
            return true;
        }

        public static LegacyDbServerFrame CreateRestoreResponse(
            NativeCharacterRestoreRequest request, bool success)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var payload = new byte[HeaderSize];
            BinaryPrimitives.WriteUInt16LittleEndian(payload,
                RestoreResponseCommand);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2),
                success ? (ushort)1 : (ushort)0);
            WriteShortString(payload, 0x10, 20, request.OperatorAccount);
            WriteShortString(payload, 0x25, 15, request.OperatorCharacter);
            WriteShortString(payload, 0x35, 15, request.TargetCharacter);
            return new LegacyDbServerFrame(1, 0, payload);
        }

        public static bool TryDecodeLookup(LegacyDbServerFrame frame,
            out NativeCharacterLookupRequest request, out string error)
        {
            request = null;
            error = string.Empty;
            if (!HasHeader(frame, LookupRequestCommand))
            {
                error = "native 0x019B envelope is invalid";
                return false;
            }
            var payload = frame.Payload.AsSpan();
            var tail = payload.Slice(HeaderSize);
            if (tail.Length < LookupMinimumTailSize)
            {
                error = "native 0x019B tail is shorter than 0x30";
                return false;
            }
            if (!TryReadShortString(payload, 0x25, 15,
                    out var character, out error))
                return false;
            request = new NativeCharacterLookupRequest
            {
                Mode = BinaryPrimitives.ReadUInt16LittleEndian(
                    payload.Slice(2, 2)),
                CharacterName = character,
                Tail = tail.ToArray()
            };
            return true;
        }

        public static long ReadLookupUserId(
            NativeCharacterLookupRequest request)
        {
            if (request?.Tail == null
                || request.Tail.Length < sizeof(long))
                throw new ArgumentException("native 0x019B tail has no UserId",
                    nameof(request));
            return BinaryPrimitives.ReadInt64LittleEndian(request.Tail);
        }

        public static LegacyDbServerFrame CreateLookupResponse(
            NativeCharacterLookupRequest request, ChrIndexInfo character)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var payload = new byte[HeaderSize + request.Tail.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(payload,
                LookupResponseCommand);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2),
                request.Mode);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4),
                character == null ? 0 : 1);
            request.Tail.CopyTo(payload, HeaderSize);
            if (character != null)
            {
                var tail = payload.AsSpan(HeaderSize);
                BinaryPrimitives.WriteInt64LittleEndian(tail,
                    character.UserId);
                WriteShortString(tail, 8, 15,
                    character.ChrNameBytes ?? Array.Empty<byte>());
                WriteShortString(tail, 24, 20,
                    character.PTIDBytes ?? Array.Empty<byte>());
            }
            return new LegacyDbServerFrame(1, 0, payload);
        }

        private static bool HasHeader(LegacyDbServerFrame frame,
            ushort command) => frame != null && frame.Type == 1
                               && frame.Payload.Length >= HeaderSize
                               && BinaryPrimitives.ReadUInt16LittleEndian(
                                   frame.Payload) == command;

        private static bool TryReadShortString(ReadOnlySpan<byte> source,
            int offset, int capacity, out byte[] value, out string error)
        {
            value = null;
            error = string.Empty;
            var length = source[offset];
            if (length > capacity)
            {
                error = $"native character ShortString exceeds {capacity} bytes";
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
