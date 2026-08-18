using System;
using System.Buffers.Binary;

namespace SystemModule.Packet
{
    /// <summary>
    /// Legacy M2-to-GameGate broadcast envelope used by message type 18.
    /// The connection field is present on the wire but is not a routing key.
    /// </summary>
    public sealed class LegacyGateType18
    {
        public const uint MagicValue = InternalPacket77.MAGIC;
        public const ushort MessageType = 18;
        public const int HeaderSize = 16;
        public const int ClientPacketSize = 12;
        public const int ClientRelayHeaderSize = 12;
        public const int MaximumClientRelayLengthExclusive = 0x8000;
        public const int MaximumFrameLengthExclusive = 0xFFF0;

        private byte[] _wireClientPayload;

        public uint Magic;
        public uint IgnoredConnectionId;
        public uint FilterUserIndex;
        public ushort Type;
        public ushort PayloadLength;
        public int Recog;
        public ushort Ident;
        public ushort Param;
        public ushort Tag;
        public ushort Series;
        public byte[] TextBytes = Array.Empty<byte>();
        public bool AppendTextTerminator = true;

        public byte[] ToClientPayload()
        {
            if (_wireClientPayload != null)
                return (byte[])_wireClientPayload.Clone();

            var textLength = TextBytes?.Length ?? 0;
            var terminatorLength = textLength != 0 && AppendTextTerminator ? 1 : 0;
            var result = new byte[ClientPacketSize + textLength + terminatorLength];
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(0, 4), Recog);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4, 2), Ident);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(6, 2), Param);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(8, 2), Tag);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(10, 2), Series);
            if (textLength != 0)
                TextBytes.AsSpan().CopyTo(result.AsSpan(ClientPacketSize, textLength));
            return result;
        }

        public byte[] ToBytes()
        {
            var clientPayload = ToClientPayload();
            var totalLength = HeaderSize + clientPayload.Length;
            if (clientPayload.Length < ClientPacketSize
                || totalLength >= MaximumFrameLengthExclusive)
                throw new InvalidOperationException(
                    "Legacy GameGate type 18 frame length is outside the native range");

            var result = new byte[totalLength];
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0, 4),
                MagicValue);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4, 4),
                IgnoredConnectionId);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8, 4),
                FilterUserIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(12, 2),
                MessageType);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(14, 2),
                checked((ushort)clientPayload.Length));
            clientPayload.CopyTo(result, HeaderSize);
            return result;
        }

        public static LegacyGateType18 FromBytes(byte[] buffer, int offset, int length)
        {
            if (buffer == null || offset < 0 || length < HeaderSize + ClientPacketSize
                || offset > buffer.Length - length)
                return null;

            var payloadLength = BitConverter.ToUInt16(buffer, offset + 14);
            var totalLength = HeaderSize + payloadLength;
            if (BitConverter.ToUInt32(buffer, offset) != MagicValue
                || BitConverter.ToUInt16(buffer, offset + 12) != MessageType
                || payloadLength < ClientPacketSize
                || totalLength >= MaximumFrameLengthExclusive
                || length < totalLength)
                return null;

            var packet = new LegacyGateType18
            {
                Magic = MagicValue,
                IgnoredConnectionId = BitConverter.ToUInt32(buffer, offset + 4),
                FilterUserIndex = BitConverter.ToUInt32(buffer, offset + 8),
                Type = MessageType,
                PayloadLength = payloadLength,
                Recog = BitConverter.ToInt32(buffer, offset + HeaderSize),
                Ident = BitConverter.ToUInt16(buffer, offset + HeaderSize + 4),
                Param = BitConverter.ToUInt16(buffer, offset + HeaderSize + 6),
                Tag = BitConverter.ToUInt16(buffer, offset + HeaderSize + 8),
                Series = BitConverter.ToUInt16(buffer, offset + HeaderSize + 10)
            };
            packet._wireClientPayload = new byte[payloadLength];
            Buffer.BlockCopy(buffer, offset + HeaderSize,
                packet._wireClientPayload, 0, payloadLength);

            var textLength = payloadLength - ClientPacketSize;
            if (textLength > 0)
            {
                var logicalTextLength = buffer[offset + totalLength - 1] == 0
                    ? textLength - 1
                    : textLength;
                packet.TextBytes = new byte[logicalTextLength];
                Buffer.BlockCopy(buffer, offset + HeaderSize + ClientPacketSize,
                    packet.TextBytes, 0, packet.TextBytes.Length);
            }
            return packet;
        }
    }
}
