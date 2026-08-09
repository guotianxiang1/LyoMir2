using System;
using System.Buffers.Binary;

namespace SystemModule.Packet
{
    /// <summary>Stateful parser for the legacy 16-byte 77 frame stream.</summary>
    public sealed class YbDbLegacy77StreamParser
    {
        public const int DefaultMaximumBufferedLength = 64 * 1024;

        private static readonly byte[] MagicBytes = { 0x77, 0xBB, 0xAA, 0x33 };

        private readonly int _maximumBufferedLength;
        private readonly int _maximumFrameLength;
        private byte[] _buffer;
        private int _length;

        public YbDbLegacy77StreamParser(
            int maximumBufferedLength = DefaultMaximumBufferedLength,
            int maximumFrameLength = YbDbLegacy77Codec.MaximumFrameLength)
        {
            if (maximumFrameLength < YbDbLegacy77Codec.HeaderSize
                || maximumFrameLength > YbDbLegacy77Codec.MaximumFrameLength)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumFrameLength));
            }
            if (maximumBufferedLength < maximumFrameLength)
                throw new ArgumentOutOfRangeException(nameof(maximumBufferedLength));

            _maximumBufferedLength = maximumBufferedLength;
            _maximumFrameLength = maximumFrameLength;
            _buffer = new byte[Math.Min(8192, maximumBufferedLength)];
        }

        public int BufferedLength => _length;

        public void Append(ReadOnlySpan<byte> data,
            Action<YbDbLegacy77Frame> onFrame)
        {
            if (onFrame == null) throw new ArgumentNullException(nameof(onFrame));

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
                            $"legacy YBDB stream exceeds {_maximumBufferedLength} buffered bytes");
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

        private void ParseAvailable(Action<YbDbLegacy77Frame> onFrame)
        {
            var scan = 0;
            while (_length - scan >= MagicBytes.Length)
            {
                var marker = FindMagic(scan);
                if (marker < 0)
                {
                    KeepPossibleMagicSuffix(scan);
                    return;
                }
                if (_length - marker < YbDbLegacy77Codec.HeaderSize)
                {
                    Compact(marker);
                    return;
                }

                var payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(
                    _buffer.AsSpan(marker + 14, 2));
                var frameLength = YbDbLegacy77Codec.HeaderSize + payloadLength;
                if (frameLength > _maximumFrameLength)
                {
                    scan = marker + 1;
                    continue;
                }
                if (_length - marker < frameLength)
                {
                    Compact(marker);
                    return;
                }

                if (!YbDbLegacy77Codec.TryDecode(
                        _buffer.AsSpan(marker, frameLength), out var frame, out _))
                {
                    scan = marker + 1;
                    continue;
                }

                scan = marker + frameLength;
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

            KeepPossibleMagicSuffix(scan);
        }

        private int FindMagic(int start)
        {
            for (var i = start; i + MagicBytes.Length <= _length; i++)
            {
                if (_buffer[i] == MagicBytes[0]
                    && _buffer[i + 1] == MagicBytes[1]
                    && _buffer[i + 2] == MagicBytes[2]
                    && _buffer[i + 3] == MagicBytes[3])
                {
                    return i;
                }
            }
            return -1;
        }

        private void KeepPossibleMagicSuffix(int start)
        {
            var available = _length - start;
            var maximum = Math.Min(MagicBytes.Length - 1, available);
            var keep = 0;
            for (var count = maximum; count > 0; count--)
            {
                var matches = true;
                for (var i = 0; i < count; i++)
                {
                    if (_buffer[_length - count + i] == MagicBytes[i]) continue;
                    matches = false;
                    break;
                }
                if (!matches) continue;
                keep = count;
                break;
            }

            if (keep > 0)
                Buffer.BlockCopy(_buffer, _length - keep, _buffer, 0, keep);
            _length = keep;
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
                Math.Max(required, _buffer.Length * 2));
            Array.Resize(ref _buffer, size);
        }
    }
}
