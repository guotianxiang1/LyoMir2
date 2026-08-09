using System;
using System.Collections.Generic;

namespace SystemModule.Packet
{
    /// <summary>Bounded stream parser for split/coalesced mobile client frames.</summary>
    public sealed class Frame44FF44StreamParser
    {
        public const int DefaultMaximumBufferedLength = 64 * 1024;
        private const int InitialBufferLength = 2048;

        private readonly int _maximumBufferedLength;
        private byte[] _buffer;
        private int _length;

        public Frame44FF44StreamParser(
            int maximumBufferedLength = DefaultMaximumBufferedLength)
        {
            if (maximumBufferedLength < Frame44FF44.HEADER_SIZE)
                throw new ArgumentOutOfRangeException(nameof(maximumBufferedLength));
            _maximumBufferedLength = maximumBufferedLength;
            _buffer = new byte[Math.Min(InitialBufferLength, maximumBufferedLength)];
        }

        public int BufferedLength => _length;
        public int BufferCapacity => _buffer.Length;

        public bool TryAppend(byte[] data, int offset, int count,
            out List<Frame44FF44> frames, out string error)
        {
            frames = new List<Frame44FF44>();
            error = string.Empty;
            if (data == null || offset < 0 || count < 0 || offset > data.Length - count)
                return Fail("invalid input range", out error);
            if (count == 0) return true;
            if (_length + count > _maximumBufferedLength)
                return Fail($"44FF44FF stream exceeds {_maximumBufferedLength} buffered bytes",
                    out error);

            EnsureCapacity(_length + count);
            Buffer.BlockCopy(data, offset, _buffer, _length, count);
            _length += count;

            frames = Frame44FF44.ScanAll(_buffer, 0, _length, out var consumed);
            if (consumed > 0)
            {
                if (consumed < _length)
                    Buffer.BlockCopy(_buffer, consumed, _buffer, 0, _length - consumed);
                _length -= consumed;
            }
            if (_length == 0 && _buffer.Length > InitialBufferLength)
                Array.Resize(ref _buffer,
                    Math.Min(InitialBufferLength, _maximumBufferedLength));
            return true;
        }

        public void Reset()
        {
            _length = 0;
            if (_buffer.Length > InitialBufferLength)
                Array.Resize(ref _buffer,
                    Math.Min(InitialBufferLength, _maximumBufferedLength));
        }

        private bool Fail(string message, out string error)
        {
            error = message;
            Reset();
            return false;
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
