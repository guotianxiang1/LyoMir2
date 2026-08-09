using System;
using System.Buffers.Binary;

namespace SystemModule.Packet
{
    /// <summary>
    /// Raw 12-byte message payload carried by the original DBServer's 5100
    /// Ident=4/14 frames.
    /// </summary>
    public static class LegacyGateDataCodec
    {
        public const ushort RequestIdent = 4;
        public const ushort ResponseIdent = 14;
        public const int MessageHeaderSize = 12;

        public static bool TryDecodeRequest(YbDbLegacy77Frame frame,
            out LegacyGateDataMessage message, out string error)
        {
            message = null;
            error = string.Empty;
            if (frame == null)
            {
                error = "legacy gate frame is null";
                return false;
            }
            if (frame.Ident != RequestIdent)
            {
                error = $"legacy gate data request ident must be {RequestIdent}";
                return false;
            }
            if (frame.Payload.Length < MessageHeaderSize)
            {
                error = $"legacy gate data payload is shorter than {MessageHeaderSize} bytes";
                return false;
            }

            var payload = frame.Payload.AsSpan();
            message = new LegacyGateDataMessage(
                BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(0, 4)),
                BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2)),
                BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(6, 2)),
                BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(8, 2)),
                BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(10, 2)),
                payload.Length == MessageHeaderSize
                    ? Array.Empty<byte>()
                    : payload.Slice(MessageHeaderSize).ToArray());
            return true;
        }

        public static YbDbLegacy77Frame CreateResponse(int queryId, int recog,
            ushort ident, ushort param, ushort tag, ushort series, byte[] body)
        {
            body ??= Array.Empty<byte>();
            var payload = new byte[MessageHeaderSize + body.Length];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), recog);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), ident);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(6, 2), param);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(8, 2), tag);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(10, 2), series);
            body.CopyTo(payload, MessageHeaderSize);
            return new YbDbLegacy77Frame(queryId, 0, ResponseIdent, payload);
        }
    }

    public sealed class LegacyGateDataMessage
    {
        public LegacyGateDataMessage(int recog, ushort ident, ushort param,
            ushort tag, ushort series, byte[] body)
        {
            Recog = recog;
            Ident = ident;
            Param = param;
            Tag = tag;
            Series = series;
            Body = body ?? Array.Empty<byte>();
        }

        public int Recog { get; }
        public ushort Ident { get; }
        public ushort Param { get; }
        public ushort Tag { get; }
        public ushort Series { get; }
        public byte[] Body { get; }
    }
}
