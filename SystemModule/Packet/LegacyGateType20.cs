using System;
using System.Buffers.Binary;

namespace SystemModule.Packet
{
    /// <summary>
    /// Native RunGate same-link test frame (outer type 20).
    /// RunGate queues the complete incoming frame back to the originating M2
    /// connection without rewriting any header or payload byte.
    /// </summary>
    public sealed class LegacyGateType20
    {
        public const uint MagicValue = InternalPacket77.MAGIC;
        public const ushort MessageType = 20;
        public const int HeaderSize = InternalPacket77.HEADER_SIZE;
        public const int MaximumFrameLengthExclusive = 0x10000;
        public const int MaximumEchoFrameLengthExclusive = 0x8000;

        private readonly byte[] _wireBytes;

        private LegacyGateType20(byte[] wireBytes)
        {
            _wireBytes = wireBytes;
        }

        public uint ConnectionId => BinaryPrimitives.ReadUInt32LittleEndian(
            _wireBytes.AsSpan(4, 4));

        public uint Context => BinaryPrimitives.ReadUInt32LittleEndian(
            _wireBytes.AsSpan(8, 4));

        public ushort PayloadLength => BinaryPrimitives.ReadUInt16LittleEndian(
            _wireBytes.AsSpan(14, 2));

        public int TotalLength => _wireBytes.Length;

        public bool CanEcho => TotalLength < MaximumEchoFrameLengthExclusive;

        public byte[] ToBytes() => (byte[])_wireBytes.Clone();

        public static LegacyGateType20 FromBytes(byte[] buffer, int offset, int length)
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

            var wireBytes = new byte[totalLength];
            buffer.AsSpan(offset, totalLength).CopyTo(wireBytes);
            return new LegacyGateType20(wireBytes);
        }
    }
}
