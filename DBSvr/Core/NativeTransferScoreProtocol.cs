using System;
using System.Buffers.Binary;
using SystemModule.Packet;

namespace DBSvr.Core
{
    public sealed class NativeTransferScoreRequest
    {
        public ushort ScoreType { get; init; }
        public ushort Amount { get; init; }
        public byte[] CharacterName { get; init; } = Array.Empty<byte>();
    }

    public static class NativeTransferScoreProtocol
    {
        public const ushort RequestCommand = 0x0176;
        public const ushort ResponseCommand = 0x012F;
        public const int HeaderSize = 0x48;

        public static bool TryDecode(LegacyDbServerFrame frame,
            out NativeTransferScoreRequest request, out string error)
        {
            request = null;
            error = string.Empty;
            if (frame == null || frame.Type != 1
                || frame.Payload.Length < HeaderSize)
            {
                error = "native 0x0176 envelope is invalid";
                return false;
            }
            var payload = frame.Payload.AsSpan();
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload)
                != RequestCommand)
            {
                error = "native 0x0176 command mismatch";
                return false;
            }
            var packed = BinaryPrimitives.ReadUInt32LittleEndian(
                payload.Slice(4, 4));
            var nameLength = payload[0x35];
            if (nameLength > 15)
            {
                error = "native 0x0176 character name exceeds 15 bytes";
                return false;
            }
            request = new NativeTransferScoreRequest
            {
                ScoreType = unchecked((ushort)packed),
                Amount = unchecked((ushort)(packed >> 16)),
                CharacterName = payload.Slice(0x36, nameLength).ToArray()
            };
            return true;
        }

        public static LegacyDbServerFrame CreateResponse(
            NativeTransferScoreRequest request, bool success)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var payload = new byte[HeaderSize];
            BinaryPrimitives.WriteUInt16LittleEndian(payload, ResponseCommand);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2),
                request.ScoreType);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4),
                success ? request.Amount : 0);
            var nameLength = Math.Min(15, request.CharacterName.Length);
            payload[0x25] = (byte)nameLength;
            request.CharacterName.AsSpan(0, nameLength)
                .CopyTo(payload.AsSpan(0x26));
            return new LegacyDbServerFrame(1, 0, payload);
        }
    }
}
