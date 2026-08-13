using System;
using System.Collections.Generic;

namespace SystemModule.Packet
{
    /// <summary>
    /// Stateful parser for the shared GameGate-to-M2 77BBAA33 byte stream.
    /// It preserves a split frame and discards only bytes before the next valid marker.
    /// </summary>
    public sealed class InternalPacket77FrameParser
    {
        public const int DefaultMaximumBufferedLength = 1024 * 1024;

        private readonly int _maximumBufferedLength;
        private readonly int _maximumFrameLength;
        private byte[] _buffer = new byte[8192];
        private int _length;

        public InternalPacket77FrameParser(int maximumBufferedLength = DefaultMaximumBufferedLength,
            int maximumFrameLength = ushort.MaxValue)
        {
            if (maximumBufferedLength < InternalPacket77.HEADER_SIZE)
                throw new ArgumentOutOfRangeException(nameof(maximumBufferedLength));
            if (maximumFrameLength < InternalPacket77.HEADER_SIZE || maximumFrameLength > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(maximumFrameLength));
            _maximumBufferedLength = maximumBufferedLength;
            _maximumFrameLength = maximumFrameLength;
        }

        public int BufferedLength => _length;

        public bool TryAppend(byte[] data, int offset, int count,
            out List<InternalPacket77> frames, out string error)
        {
            frames = new List<InternalPacket77>();
            error = string.Empty;
            if (data == null || offset < 0 || count < 0 || offset > data.Length - count)
            {
                error = "invalid input range";
                Reset();
                return false;
            }
            if (count == 0) return true;
            if (_length + count > _maximumBufferedLength)
            {
                error = $"77BBAA33 stream exceeds {_maximumBufferedLength} buffered bytes";
                Reset();
                return false;
            }

            EnsureCapacity(_length + count);
            Buffer.BlockCopy(data, offset, _buffer, _length, count);
            _length += count;

            var scan = 0;
            while (_length - scan >= 4)
            {
                var marker = FindMarker(scan);
                if (marker < 0)
                {
                    KeepPossibleMarkerSuffix(scan);
                    return true;
                }
                if (_length - marker < InternalPacket77.HEADER_SIZE)
                {
                    Compact(marker);
                    return true;
                }

                // 原版 16 字节头: total = 0x10 + word[+0x0E](BodyLen)。+0x0C 是 Cmd 而非长度。
                // 证据: 解析器 0x5F666A/0x63A66C 均 `lea eax,[pos+0x10]; add eax,word[+0x0E]`;
                //       接收器 0x63B258 `movsd`x4 拷 16 字节头, body 自 frame+0x10 (M2 flat_image.bin)。
                var bodyLength = BitConverter.ToUInt16(_buffer, marker + 14);
                var frameLength = InternalPacket77.HEADER_SIZE + bodyLength;
                if (frameLength > _maximumFrameLength)
                {
                    scan = marker + 1;
                    continue;
                }
                if (_length - marker < frameLength)
                {
                    Compact(marker);
                    return true;
                }

                var packet = InternalPacket77.FromBytes(_buffer, marker, frameLength);
                if (packet == null || packet.Magic != InternalPacket77.MAGIC)
                {
                    scan = marker + 1;
                    continue;
                }
                frames.Add(packet);
                scan = marker + frameLength;
            }

            Compact(scan);
            return true;
        }

        public void Reset() => _length = 0;

        private int FindMarker(int start)
        {
            for (var i = start; i + 4 <= _length; i++)
                if (_buffer[i] == 0x77 && _buffer[i + 1] == 0xBB
                    && _buffer[i + 2] == 0xAA && _buffer[i + 3] == 0x33)
                    return i;
            return -1;
        }

        private void KeepPossibleMarkerSuffix(int start)
        {
            var keep = Math.Min(3, _length - start);
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
                Math.Max(required, Math.Max(_buffer.Length * 2, 8192)));
            Array.Resize(ref _buffer, size);
        }
    }
}
