using System;
using System.Collections.Generic;

namespace SystemModule
{
    /// <summary>
    /// Stateful parser for the port 6000 RequestServerPacket wire format.
    /// A frame starts with '#' followed by its little-endian total length.
    /// </summary>
    public sealed class RequestServerFrameParser
    {
        public const int HeaderSize = 5;
        public const int MinimumFrameLength = 22;
        public const int DefaultMaximumFrameLength = 16 * 1024 * 1024;

        private readonly int _maximumFrameLength;
        private byte[] _frameBuffer = new byte[HeaderSize];
        private int _bufferedLength;
        private int _expectedFrameLength;

        public RequestServerFrameParser()
            : this(DefaultMaximumFrameLength)
        {
        }

        public RequestServerFrameParser(int maximumFrameLength)
        {
            if (maximumFrameLength < MinimumFrameLength)
                throw new ArgumentOutOfRangeException(nameof(maximumFrameLength));

            _maximumFrameLength = maximumFrameLength;
        }

        public bool TryAppend(byte[] data, int offset, int count, out List<byte[]> frames, out string error)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (offset < 0 || count < 0 || offset > data.Length - count)
                throw new ArgumentOutOfRangeException(nameof(offset));

            frames = new List<byte[]>();
            error = string.Empty;

            var end = offset + count;
            while (offset < end)
            {
                if (_bufferedLength == 0 && data[offset] != (byte)'#')
                    return Fail($"invalid frame marker 0x{data[offset]:X2}", out error);

                if (_expectedFrameLength == 0)
                {
                    var headerBytes = Math.Min(HeaderSize - _bufferedLength, end - offset);
                    Buffer.BlockCopy(data, offset, _frameBuffer, _bufferedLength, headerBytes);
                    _bufferedLength += headerBytes;
                    offset += headerBytes;

                    if (_bufferedLength < HeaderSize)
                        continue;

                    var frameLength = BitConverter.ToInt32(_frameBuffer, 1);
                    if (frameLength < MinimumFrameLength || frameLength > _maximumFrameLength)
                        return Fail($"invalid frame length {frameLength}", out error);

                    _expectedFrameLength = frameLength;
                }

                var bodyBytes = Math.Min(_expectedFrameLength - _bufferedLength, end - offset);
                EnsureCapacity(_bufferedLength + bodyBytes);
                Buffer.BlockCopy(data, offset, _frameBuffer, _bufferedLength, bodyBytes);
                _bufferedLength += bodyBytes;
                offset += bodyBytes;

                if (_bufferedLength == _expectedFrameLength)
                {
                    if (_frameBuffer[_expectedFrameLength - 1] != (byte)'!')
                        return Fail($"invalid frame terminator 0x{_frameBuffer[_expectedFrameLength - 1]:X2}", out error);

                    frames.Add(_frameBuffer);
                    StartNextFrame();
                }
            }

            return true;
        }

        public void Reset()
        {
            StartNextFrame();
        }

        private bool Fail(string message, out string error)
        {
            error = message;
            Reset();
            return false;
        }

        private void EnsureCapacity(int requiredLength)
        {
            if (_frameBuffer.Length >= requiredLength) return;

            var newLength = Math.Max(_frameBuffer.Length * 2, requiredLength);
            newLength = Math.Min(newLength, _expectedFrameLength);
            Array.Resize(ref _frameBuffer, newLength);
        }

        private void StartNextFrame()
        {
            _frameBuffer = new byte[HeaderSize];
            _bufferedLength = 0;
            _expectedFrameLength = 0;
        }
    }
}
