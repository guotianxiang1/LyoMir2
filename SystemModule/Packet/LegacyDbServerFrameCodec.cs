using System;
using System.Buffers.Binary;

namespace SystemModule.Packet
{
    /// <summary>
    /// Native port 6000 DBServer frame: magic, type, reserved, payload length, payload.
    /// </summary>
    public static class LegacyDbServerFrameCodec
    {
        public const uint FrameMagic = 0x33AABB77;
        public const int HeaderSize = 12;
        public const int DefaultMaximumFrameLength = 0x1FFFF;

        public static bool TryEncode(LegacyDbServerFrame frame, out byte[] data,
            out string error, int maximumFrameLength = DefaultMaximumFrameLength)
        {
            data = null;
            error = string.Empty;
            if (frame == null)
            {
                error = "native DBServer frame is null";
                return false;
            }
            var payload = frame.Payload ?? Array.Empty<byte>();
            if (payload.Length > maximumFrameLength - HeaderSize)
            {
                error = $"native DBServer frame exceeds {maximumFrameLength} bytes";
                return false;
            }

            data = new byte[HeaderSize + payload.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), FrameMagic);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4, 2), frame.Type);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6, 2), frame.Reserved);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8, 4), payload.Length);
            payload.CopyTo(data, HeaderSize);
            return true;
        }

        public static bool TryDecode(ReadOnlySpan<byte> data,
            out LegacyDbServerFrame frame, out string error,
            int maximumFrameLength = DefaultMaximumFrameLength)
        {
            frame = null;
            error = string.Empty;
            if (data.Length < HeaderSize)
            {
                error = "native DBServer frame is truncated";
                return false;
            }
            if (BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(0, 4)) != FrameMagic)
            {
                error = "native DBServer frame magic mismatch";
                return false;
            }

            var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(8, 4));
            if (payloadLength < 0 || payloadLength > maximumFrameLength - HeaderSize)
            {
                error = "native DBServer payload length is invalid";
                return false;
            }
            if (data.Length != HeaderSize + payloadLength)
            {
                error = "native DBServer payload length mismatch";
                return false;
            }

            frame = new LegacyDbServerFrame(
                BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(4, 2)),
                BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(6, 2)),
                payloadLength == 0
                    ? Array.Empty<byte>()
                    : data.Slice(HeaderSize, payloadLength).ToArray());
            return true;
        }
    }

    public sealed class LegacyDbServerFrame
    {
        public LegacyDbServerFrame(ushort type, ushort reserved, byte[] payload)
        {
            Type = type;
            Reserved = reserved;
            Payload = payload ?? Array.Empty<byte>();
        }

        public ushort Type { get; }
        public ushort Reserved { get; }
        public byte[] Payload { get; }
    }
}
