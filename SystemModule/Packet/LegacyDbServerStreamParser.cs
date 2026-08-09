using System;
using System.Buffers.Binary;

namespace SystemModule.Packet
{
    /// <summary>Stateful parser for native port 6000 type1/type2 frames.</summary>
    public sealed class LegacyDbServerStreamParser
    {
        private static readonly byte[] MagicBytes = { 0x77, 0xBB, 0xAA, 0x33 };
        public const int NativeMaximumBufferedLength = 0x20000;

        private readonly int _maximumFrameLength;
        private readonly int _maximumBufferedLength;
        private readonly bool _strict;
        private byte[] _buffer;
        private int _length;

        public LegacyDbServerStreamParser(
            int maximumFrameLength = LegacyDbServerFrameCodec.DefaultMaximumFrameLength,
            int maximumBufferedLength = NativeMaximumBufferedLength,
            bool strict = false)
        {
            if (maximumFrameLength < LegacyDbServerFrameCodec.HeaderSize)
                throw new ArgumentOutOfRangeException(nameof(maximumFrameLength));
            if (maximumBufferedLength < maximumFrameLength)
                throw new ArgumentOutOfRangeException(nameof(maximumBufferedLength));
            _maximumFrameLength = maximumFrameLength;
            _maximumBufferedLength = maximumBufferedLength;
            _strict = strict;
            _buffer = new byte[Math.Min(8192, maximumBufferedLength)];
        }

        public int BufferedLength => _length;

        public void Append(ReadOnlySpan<byte> data, Action<LegacyDbServerFrame> onFrame)
        {
            if (onFrame == null) throw new ArgumentNullException(nameof(onFrame));
            if (!_strict && data.Length > _maximumBufferedLength - _length)
            {
                // The native receiver drops the whole new receive block when its
                // connection buffer would exceed 0x20000, preserving the old tail.
                return;
            }
            while (!data.IsEmpty)
            {
                var available = _maximumBufferedLength - _length;
                if (available == 0)
                {
                    var oldLength = _length;
                    ParseAvailable(onFrame);
                    if (_length == oldLength)
                    {
                        Reset();
                        throw new InvalidOperationException(
                            $"native DBServer stream exceeds {_maximumBufferedLength} buffered bytes");
                    }
                    available = _maximumBufferedLength - _length;
                }

                var count = Math.Min(data.Length, available);
                EnsureCapacity(_length + count);
                data.Slice(0, count).CopyTo(_buffer.AsSpan(_length));
                _length += count;
                data = data.Slice(count);
                ParseAvailable(onFrame);
            }
        }

        public void Reset()
        {
            if (_length > 0) Array.Clear(_buffer, 0, _length);
            _length = 0;
        }

        private void ParseAvailable(Action<LegacyDbServerFrame> onFrame)
        {
            if (_strict)
            {
                ParseStrict(onFrame);
                return;
            }

            var scan = 0;
            while (_length - scan >= LegacyDbServerFrameCodec.HeaderSize)
            {
                if (!MagicMatches(scan))
                {
                    scan++;
                    continue;
                }

                var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(
                    _buffer.AsSpan(scan + 8, 4));
                if (payloadLength < 0
                    || payloadLength > _maximumFrameLength - LegacyDbServerFrameCodec.HeaderSize)
                {
                    // The original clears the connection buffer for a declared frame
                    // whose total size reaches 0x20000. Negative lengths are cleared by
                    // the same non-disconnecting path as a memory-safety correction.
                    Reset();
                    return;
                }
                var frameLength = LegacyDbServerFrameCodec.HeaderSize + payloadLength;
                if (_length - scan < frameLength)
                {
                    Compact(scan);
                    return;
                }
                if (!LegacyDbServerFrameCodec.TryDecode(
                        _buffer.AsSpan(scan, frameLength), out var frame, out _,
                        _maximumFrameLength))
                {
                    scan++;
                    continue;
                }

                scan += frameLength;
                try
                {
                    onFrame(frame);
                }
                catch
                {
                    Compact(scan);
                    throw;
                }
            }
            // Native parsing only runs while at least a full 12-byte header remains;
            // preserve the entire tail so a marker/header can span receive calls.
            Compact(scan);
        }

        private void ParseStrict(Action<LegacyDbServerFrame> onFrame)
        {
            while (_length >= MagicBytes.Length)
            {
                for (var i = 0; i < MagicBytes.Length; i++)
                {
                    if (_buffer[i] == MagicBytes[i]) continue;
                    Reset();
                    throw new InvalidOperationException("native DBServer stream magic mismatch");
                }
                if (_length < LegacyDbServerFrameCodec.HeaderSize) return;

                var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(
                    _buffer.AsSpan(8, 4));
                if (payloadLength < 0
                    || payloadLength > _maximumFrameLength - LegacyDbServerFrameCodec.HeaderSize)
                {
                    Reset();
                    throw new InvalidOperationException(
                        "native DBServer stream payload length is invalid");
                }
                var frameLength = LegacyDbServerFrameCodec.HeaderSize + payloadLength;
                if (_length < frameLength) return;
                if (!LegacyDbServerFrameCodec.TryDecode(
                        _buffer.AsSpan(0, frameLength), out var frame, out var error,
                        _maximumFrameLength))
                {
                    Reset();
                    throw new InvalidOperationException(error);
                }

                Compact(frameLength);
                onFrame(frame);
            }
        }

        private bool MagicMatches(int offset)
        {
            for (var i = 0; i < MagicBytes.Length; i++)
                if (_buffer[offset + i] != MagicBytes[i]) return false;
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
            Array.Resize(ref _buffer, Math.Min(_maximumBufferedLength,
                Math.Max(required, _buffer.Length * 2)));
        }
    }
}
