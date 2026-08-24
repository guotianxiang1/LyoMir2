using System;
using System.Buffers.Binary;

namespace SystemModule.Packet
{
    /// <summary>
    /// Native RunGate cross-gate frame (outer type 17).
    /// The dword at +8 selects a gate: zero broadcasts to every gate except
    /// the sender; a positive value selects every gate with that assigned byte.
    /// Before forwarding, RunGate rewrites the outer type to 7 in place.
    /// </summary>
    public sealed class LegacyGateType17
    {
        public const uint MagicValue = InternalPacket77.MAGIC;
        public const ushort MessageType = 17;
        public const ushort ForwardedMessageType = 7;
        public const int HeaderSize = 16;
        public const int MaximumFrameLengthExclusive = 0x10000;
        public const int MaximumForwardedFrameLengthExclusive = 0x8000;

        public uint ConnectionId;
        public uint TargetGate;
        public byte[] Payload = Array.Empty<byte>();

        public int TotalLength => checked(HeaderSize + (Payload?.Length ?? 0));
        public bool CanForward => TotalLength < MaximumForwardedFrameLengthExclusive;

        public bool ShouldForwardTo(byte candidateGateIndex, bool isSender)
        {
            var signedTarget = unchecked((int)TargetGate);
            if (signedTarget == 0) return !isSender;
            return signedTarget > 0 && signedTarget == candidateGateIndex;
        }

        public byte[] ToBytes() => BuildWire(MessageType);

        public byte[] ToForwardedBytes() => BuildWire(ForwardedMessageType);

        private byte[] BuildWire(ushort type)
        {
            var payload = Payload ?? Array.Empty<byte>();
            var totalLength = checked(HeaderSize + payload.Length);
            if (payload.Length > ushort.MaxValue
                || totalLength >= MaximumFrameLengthExclusive)
                throw new InvalidOperationException(
                    "Legacy GameGate type 17 frame length is outside the native range");

            var result = new byte[totalLength];
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0, 4), MagicValue);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4, 4), ConnectionId);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8, 4), TargetGate);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(12, 2), type);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(14, 2),
                checked((ushort)payload.Length));
            payload.AsSpan().CopyTo(result.AsSpan(HeaderSize));
            return result;
        }

        public static LegacyGateType17 FromBytes(byte[] buffer, int offset, int length)
        {
            if (buffer == null || offset < 0 || length < HeaderSize
                || offset > buffer.Length - length)
                return null;

            var payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(
                buffer.AsSpan(offset + 14, 2));
            var totalLength = HeaderSize + payloadLength;
            if (BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset, 4))
                    != MagicValue
                || BinaryPrimitives.ReadUInt16LittleEndian(
                    buffer.AsSpan(offset + 12, 2)) != MessageType
                || totalLength >= MaximumFrameLengthExclusive
                || length < totalLength)
                return null;

            var payload = new byte[payloadLength];
            if (payloadLength != 0)
                buffer.AsSpan(offset + HeaderSize, payloadLength).CopyTo(payload);
            return new LegacyGateType17
            {
                ConnectionId = BinaryPrimitives.ReadUInt32LittleEndian(
                    buffer.AsSpan(offset + 4, 4)),
                TargetGate = BinaryPrimitives.ReadUInt32LittleEndian(
                    buffer.AsSpan(offset + 8, 4)),
                Payload = payload
            };
        }
    }
}
