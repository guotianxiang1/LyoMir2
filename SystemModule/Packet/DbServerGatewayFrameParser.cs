using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace SystemModule.Packet
{
    public enum DbServerGatewayFrameKind
    {
        PercentDollar,
        NativeControl
    }

    public sealed class DbServerGatewayFrame
    {
        private DbServerGatewayFrame(DbServerGatewayFrameKind kind,
            byte[] data, uint connectionId, int parameter, ushort command,
            byte[] payload)
        {
            Kind = kind;
            Data = data;
            ConnectionId = connectionId;
            Parameter = parameter;
            Command = command;
            Payload = payload;
        }

        public DbServerGatewayFrameKind Kind { get; }
        public byte[] Data { get; }
        public uint ConnectionId { get; }
        public int Parameter { get; }
        public ushort Command { get; }
        public byte[] Payload { get; }

        internal static DbServerGatewayFrame CreatePercentDollar(byte[] data) =>
            new(DbServerGatewayFrameKind.PercentDollar, data, 0, 0, 0,
                Array.Empty<byte>());

        internal static DbServerGatewayFrame CreateNativeControl(byte[] data,
            uint connectionId, int parameter, ushort command, byte[] payload) =>
            new(DbServerGatewayFrameKind.NativeControl, data, connectionId,
                parameter, command, payload);
    }

    /// <summary>
    /// Parses the native DBServer-to-GameGate stream. Ordinary responses use
    /// %...$ framing, while native SelGate controls use a 0x33AABB77 header
    /// and a 16-bit payload length on the same ordered TCP stream.
    /// </summary>
    public sealed class DbServerGatewayFrameParser
    {
        public const int DefaultMaximumBufferedLength = 1024 * 1024;
        public const int DefaultMaximumTextFrameLength = 64 * 1024;
        public const int MaximumNativeFrameLength = 0xFFEF;

        private readonly int _maximumBufferedLength;
        private readonly int _maximumTextFrameLength;
        private byte[] _buffer;
        private int _length;

        public DbServerGatewayFrameParser(
            int maximumTextFrameLength = DefaultMaximumTextFrameLength)
            : this(Math.Max(DefaultMaximumBufferedLength, maximumTextFrameLength),
                maximumTextFrameLength)
        {
        }

        public DbServerGatewayFrameParser(int maximumBufferedLength,
            int maximumTextFrameLength)
        {
            if (maximumTextFrameLength < 3)
                throw new ArgumentOutOfRangeException(nameof(maximumTextFrameLength));
            if (maximumBufferedLength < maximumTextFrameLength)
                throw new ArgumentOutOfRangeException(nameof(maximumBufferedLength));

            _maximumBufferedLength = maximumBufferedLength;
            _maximumTextFrameLength = maximumTextFrameLength;
            _buffer = new byte[Math.Min(1024, maximumTextFrameLength)];
        }

        public int BufferedLength => _length;

        public bool TryAppend(byte[] data, int offset, int count,
            out List<DbServerGatewayFrame> frames, out string error)
        {
            frames = new List<DbServerGatewayFrame>();
            error = string.Empty;

            if (data == null || offset < 0 || count < 0
                || offset > data.Length - count)
                return Fail("invalid input range", out error);
            if (count == 0) return true;
            if (count > _maximumBufferedLength - _length)
                return Fail($"gateway stream exceeds {_maximumBufferedLength} buffered bytes",
                    out error);

            EnsureCapacity(_length + count);
            Buffer.BlockCopy(data, offset, _buffer, _length, count);
            _length += count;

            var consumed = 0;
            while (consumed < _length)
            {
                if (_buffer[consumed] == (byte)'%')
                {
                    var end = IndexOf((byte)'$', consumed + 1, _length);
                    if (end < 0)
                    {
                        if (_length - consumed >= _maximumTextFrameLength)
                            return Fail($"unterminated gateway frame exceeds "
                                + $"{_maximumTextFrameLength} bytes", out error);
                        break;
                    }

                    var frameLength = end - consumed + 1;
                    if (frameLength > _maximumTextFrameLength)
                        return Fail($"gateway frame exceeds {_maximumTextFrameLength} bytes",
                            out error);
                    var frame = new byte[frameLength];
                    Buffer.BlockCopy(_buffer, consumed, frame, 0, frameLength);
                    frames.Add(DbServerGatewayFrame.CreatePercentDollar(frame));
                    consumed = end + 1;
                    continue;
                }

                var available = _length - consumed;
                if (available < sizeof(uint))
                {
                    if (IsNativeMagicPrefix(consumed, available)) break;
                    consumed++;
                    continue;
                }
                if (BinaryPrimitives.ReadUInt32LittleEndian(
                        _buffer.AsSpan(consumed, sizeof(uint))) !=
                    YbDbLegacy77Codec.FrameMagic)
                {
                    // Native SelGate advances one byte after a bad magic and
                    // continues looking for the next complete envelope.
                    consumed++;
                    continue;
                }
                if (available < YbDbLegacy77Codec.HeaderSize) break;

                var header = _buffer.AsSpan(consumed,
                    YbDbLegacy77Codec.HeaderSize);
                var payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(
                    header.Slice(14, 2));
                var nativeFrameLength = YbDbLegacy77Codec.HeaderSize
                    + payloadLength;
                if (nativeFrameLength > MaximumNativeFrameLength)
                {
                    // The native SelGate drops its complete receive buffer on
                    // this size gate and returns to the socket loop.
                    consumed = _length;
                    break;
                }
                if (available < nativeFrameLength) break;

                var control = _buffer.AsSpan(consumed,
                    nativeFrameLength).ToArray();
                frames.Add(DbServerGatewayFrame.CreateNativeControl(control,
                    BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(4, 4)),
                    BinaryPrimitives.ReadInt32LittleEndian(header.Slice(8, 4)),
                    BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(12, 2)),
                    payloadLength == 0
                        ? Array.Empty<byte>()
                        : control.AsSpan(YbDbLegacy77Codec.HeaderSize,
                            payloadLength).ToArray()));
                consumed += nativeFrameLength;
            }

            Compact(consumed);
            return true;
        }

        public void Reset()
        {
            _length = 0;
        }

        private bool Fail(string message, out string error)
        {
            error = message;
            Reset();
            return false;
        }

        private int IndexOf(byte value, int start, int end)
        {
            for (var i = start; i < end; i++)
                if (_buffer[i] == value) return i;
            return -1;
        }

        private bool IsNativeMagicPrefix(int offset, int available)
        {
            Span<byte> magic = stackalloc byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(magic,
                YbDbLegacy77Codec.FrameMagic);
            for (var i = 0; i < available; i++)
                if (_buffer[offset + i] != magic[i]) return false;
            return true;
        }

        private void Compact(int consumed)
        {
            if (consumed <= 0) return;
            if (consumed < _length)
                Buffer.BlockCopy(_buffer, consumed, _buffer, 0, _length - consumed);
            _length -= consumed;
        }

        private void EnsureCapacity(int required)
        {
            if (_buffer.Length >= required) return;
            var size = Math.Min(_maximumBufferedLength,
                Math.Max(required, Math.Max(_buffer.Length * 2, 1024)));
            Array.Resize(ref _buffer, size);
        }
    }
}
