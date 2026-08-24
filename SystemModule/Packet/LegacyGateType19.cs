using System;
using System.Buffers.Binary;

namespace SystemModule.Packet
{
    /// <summary>
    /// Native M2-to-GameGate type 19 envelope.
    ///
    /// The dword at +8 is the number of target session ids.  Those ids are
    /// packed as little-endian words at the beginning of the body; the rest
    /// of the body is forwarded byte-for-byte to each selected client.
    /// </summary>
    public sealed class LegacyGateType19
    {
        public const uint MagicValue = InternalPacket77.MAGIC;
        public const ushort MessageType = 19;
        public const int HeaderSize = 16;
        public const int ClientPacketSize = 12;
        public const int ClientRelayHeaderSize = ClientPacketSize;
        public const int MaximumClientRelayLengthExclusive = 0x8000;
        public const int MaximumFrameLengthExclusive = 0x10000;

        public uint Magic;
        public uint IgnoredConnectionId;
        public ushort Type;
        public ushort PayloadLength;
        public ushort[] SessionIds = Array.Empty<ushort>();
        public byte[] ClientPayload = Array.Empty<byte>();

        public uint SessionCount => (uint)(SessionIds?.Length ?? 0);

        /// <summary>Returns the client body without the target-id prefix.</summary>
        public byte[] ToClientPayload() =>
            ClientPayload == null ? Array.Empty<byte>() : (byte[])ClientPayload.Clone();

        public byte[] ToBytes()
        {
            var ids = SessionIds ?? Array.Empty<ushort>();
            var clientPayload = ClientPayload ?? Array.Empty<byte>();
            var idBytes = checked(ids.Length * sizeof(ushort));
            var bodyLength = checked(idBytes + clientPayload.Length);
            var totalLength = checked(HeaderSize + bodyLength);
            if (bodyLength > ushort.MaxValue
                || totalLength >= MaximumFrameLengthExclusive)
                throw new InvalidOperationException(
                    "Legacy GameGate type 19 frame length is outside the native range");

            var result = new byte[totalLength];
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0, 4), MagicValue);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4, 4),
                IgnoredConnectionId);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8, 4),
                checked((uint)ids.Length));
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(12, 2), MessageType);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(14, 2),
                checked((ushort)bodyLength));

            var cursor = HeaderSize;
            foreach (var id in ids)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(cursor, 2), id);
                cursor += 2;
            }
            clientPayload.AsSpan().CopyTo(result.AsSpan(cursor));
            return result;
        }

        public static LegacyGateType19 FromBytes(byte[] buffer, int offset, int length)
        {
            if (buffer == null || offset < 0 || length < HeaderSize
                || offset > buffer.Length - length)
                return null;

            var magic = BinaryPrimitives.ReadUInt32LittleEndian(
                buffer.AsSpan(offset, 4));
            var type = BinaryPrimitives.ReadUInt16LittleEndian(
                buffer.AsSpan(offset + 12, 2));
            var payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(
                buffer.AsSpan(offset + 14, 2));
            var totalLength = HeaderSize + payloadLength;
            if (magic != MagicValue || type != MessageType
                || totalLength >= MaximumFrameLengthExclusive
                || length < totalLength)
                return null;

            var count = BinaryPrimitives.ReadUInt32LittleEndian(
                buffer.AsSpan(offset + 8, 4));
            // The native loop consumes exactly two bytes per target.  Reject
            // an impossible count before multiplying it, so malformed input
            // cannot wrap an index or make the parser wait forever.
            if (count > (uint)(payloadLength / sizeof(ushort)))
                return null;

            var idBytes = checked((int)count * sizeof(ushort));
            var clientLength = payloadLength - idBytes;
            var packet = new LegacyGateType19
            {
                Magic = MagicValue,
                IgnoredConnectionId = BinaryPrimitives.ReadUInt32LittleEndian(
                    buffer.AsSpan(offset + 4, 4)),
                Type = MessageType,
                PayloadLength = payloadLength,
                SessionIds = new ushort[count],
                ClientPayload = new byte[clientLength]
            };

            var cursor = offset + HeaderSize;
            for (var i = 0; i < packet.SessionIds.Length; i++)
            {
                packet.SessionIds[i] = BinaryPrimitives.ReadUInt16LittleEndian(
                    buffer.AsSpan(cursor, 2));
                cursor += 2;
            }
            buffer.AsSpan(cursor, clientLength).CopyTo(packet.ClientPayload);
            return packet;
        }
    }
}
