using System;
using System.Collections.Generic;

namespace SystemModule.Packet
{
    /// <summary>
    /// Stateful parser for the DBServer gateway stream: %...$.
    /// Delimiters are ASCII, so parsing bytes also preserves split GBK characters.
    /// </summary>
    public sealed class PercentDollarFrameParser
    {
        public const int DefaultMaximumBufferedLength = 1024 * 1024;
        public const int DefaultMaximumFrameLength = 64 * 1024;

        private readonly int _maximumBufferedLength;
        private readonly int _maximumFrameLength;
        private byte[] _buffer;
        private int _length;

        public PercentDollarFrameParser(int maximumFrameLength = DefaultMaximumFrameLength)
            : this(Math.Max(DefaultMaximumBufferedLength, maximumFrameLength),
                maximumFrameLength)
        {
        }

        public PercentDollarFrameParser(int maximumBufferedLength, int maximumFrameLength)
        {
            if (maximumFrameLength < 3)
                throw new ArgumentOutOfRangeException(nameof(maximumFrameLength));
            if (maximumBufferedLength < maximumFrameLength)
                throw new ArgumentOutOfRangeException(nameof(maximumBufferedLength));

            _maximumBufferedLength = maximumBufferedLength;
            _maximumFrameLength = maximumFrameLength;
            _buffer = new byte[Math.Min(1024, maximumFrameLength)];
        }

        public int BufferedLength => _length;

        public bool TryAppend(byte[] data, int offset, int count,
            out List<byte[]> frames, out string error)
        {
            frames = new List<byte[]>();
            error = string.Empty;

            if (data == null || offset < 0 || count < 0 || offset > data.Length - count)
            {
                error = "invalid input range";
                Reset();
                return false;
            }

            if (count == 0) return true;
            if (count > _maximumBufferedLength - _length)
            {
                error = $"gateway stream exceeds {_maximumBufferedLength} buffered bytes";
                Reset();
                return false;
            }

            EnsureCapacity(_length + count);
            Buffer.BlockCopy(data, offset, _buffer, _length, count);
            _length += count;

            var scan = 0;
            while (scan < _length)
            {
                var start = IndexOf((byte)'%', scan, _length);
                if (start < 0)
                {
                    _length = 0;
                    return true;
                }

                var end = IndexOf((byte)'$', start + 1, _length);
                if (end < 0)
                {
                    if (start > 0)
                    {
                        Buffer.BlockCopy(_buffer, start, _buffer, 0, _length - start);
                        _length -= start;
                    }

                    if (_length >= _maximumFrameLength)
                    {
                        error = $"unterminated gateway frame exceeds {_maximumFrameLength} bytes";
                        Reset();
                        return false;
                    }
                    return true;
                }

                var frameLength = end - start + 1;
                if (frameLength > _maximumFrameLength)
                {
                    error = $"gateway frame exceeds {_maximumFrameLength} bytes";
                    Reset();
                    return false;
                }
                var frame = new byte[frameLength];
                Buffer.BlockCopy(_buffer, start, frame, 0, frameLength);
                frames.Add(frame);
                scan = end + 1;
            }

            _length = 0;
            return true;
        }

        public void Reset()
        {
            _length = 0;
        }

        private int IndexOf(byte value, int start, int end)
        {
            for (var i = start; i < end; i++)
                if (_buffer[i] == value) return i;
            return -1;
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
