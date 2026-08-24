using System;
using System.Collections.Generic;

namespace SystemModule.Packet
{
    public sealed class GameGateServerFrame
    {
        public InternalPacket77 Internal77 { get; private set; }
        public LegacyGateType17 LegacyType17 { get; private set; }
        public LegacyGateType18 LegacyType18 { get; private set; }
        public LegacyGateType19 LegacyType19 { get; private set; }
        public LegacyGateType20 LegacyType20 { get; private set; }

        public static GameGateServerFrame FromInternal77(InternalPacket77 packet) =>
            new GameGateServerFrame { Internal77 = packet };

        public static GameGateServerFrame FromLegacyType17(LegacyGateType17 packet) =>
            new GameGateServerFrame { LegacyType17 = packet };

        public static GameGateServerFrame FromLegacyType18(LegacyGateType18 packet) =>
            new GameGateServerFrame { LegacyType18 = packet };

        public static GameGateServerFrame FromLegacyType19(LegacyGateType19 packet) =>
            new GameGateServerFrame { LegacyType19 = packet };

        public static GameGateServerFrame FromLegacyType20(LegacyGateType20 packet) =>
            new GameGateServerFrame { LegacyType20 = packet };
    }

    /// <summary>
    /// Parses the shared M2-to-GameGate stream without changing the existing
    /// InternalPacket77 parser. Native outer types 17 through 20 at offset 12
    /// have dedicated routing semantics and are returned as typed envelopes.
    /// </summary>
    public sealed class GameGateServerFrameParser
    {
        // The native GameGate outer parser accepts a frame only while
        // payload+0x20 < 0x10000, i.e. total frame length < 0xFFF0.
        public const int NativeMaximumFrameLengthExclusive = 0xFFF0;
        public const int NativeMaximumFrameLength =
            NativeMaximumFrameLengthExclusive - 1;
        // Unlike the M2 receive parser, this side must be able to hold the
        // native outer frame boundary (just under 0x10000) while a frame is
        // split across socket reads.  No smaller 0x8000 cap is proven for the
        // GameGate stream.
        public const int DefaultMaximumBufferedLength = 1024 * 1024;
        private readonly int _maximumBufferedLength;
        private readonly int _maximumInternalFrameLength;
        private byte[] _buffer = new byte[8192];
        private int _length;

        public GameGateServerFrameParser(
            int maximumBufferedLength = DefaultMaximumBufferedLength,
            int maximumInternalFrameLength = NativeMaximumFrameLength)
        {
            if (maximumBufferedLength < InternalPacket77.HEADER_SIZE)
                throw new ArgumentOutOfRangeException(nameof(maximumBufferedLength));
            if (maximumInternalFrameLength < InternalPacket77.HEADER_SIZE
                || maximumInternalFrameLength > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(maximumInternalFrameLength));
            _maximumBufferedLength = maximumBufferedLength;
            _maximumInternalFrameLength = maximumInternalFrameLength;
        }

        public int BufferedLength => _length;

        public bool TryAppend(byte[] data, int offset, int count,
            out List<GameGateServerFrame> frames, out string error)
        {
            frames = new List<GameGateServerFrame>();
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
                error = $"GameGate server stream exceeds {_maximumBufferedLength} buffered bytes";
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
                if (_length - marker < InternalPacket77.ACK_FRAME_LEN)
                {
                    Compact(marker);
                    return true;
                }

                var discriminator = BitConverter.ToUInt16(_buffer, marker + 12);
                var declaredBodyLength = BitConverter.ToUInt16(_buffer, marker + 14);
                var declaredFrameLength =
                    InternalPacket77.HEADER_SIZE + declaredBodyLength;
                if (declaredFrameLength > _maximumInternalFrameLength)
                {
                    // RunGate logs an oversized outer frame and drops the whole
                    // current receive buffer instead of scanning for a nested marker.
                    // The default maximum is the native < 0xFFF0 boundary;
                    // callers that intentionally use a wider synthetic fixture
                    // may opt into that limit through the constructor.
                    Reset();
                    return true;
                }
                if (discriminator == LegacyGateType17.MessageType)
                {
                    if (_length - marker < LegacyGateType17.HeaderSize)
                    {
                        Compact(marker);
                        return true;
                    }

                    var payloadLength = BitConverter.ToUInt16(_buffer, marker + 14);
                    var totalLength = LegacyGateType17.HeaderSize + payloadLength;
                    if (totalLength > _maximumInternalFrameLength)
                    {
                        scan = marker + 1;
                        continue;
                    }
                    if (_length - marker < totalLength)
                    {
                        Compact(marker);
                        return true;
                    }

                    var legacy = LegacyGateType17.FromBytes(_buffer, marker, totalLength);
                    if (legacy == null)
                    {
                        scan = marker + 1;
                        continue;
                    }
                    frames.Add(GameGateServerFrame.FromLegacyType17(legacy));
                    scan = marker + totalLength;
                    continue;
                }

                if (discriminator == LegacyGateType18.MessageType)
                {
                    if (_length - marker < LegacyGateType18.HeaderSize)
                    {
                        Compact(marker);
                        return true;
                    }

                    var payloadLength = BitConverter.ToUInt16(_buffer, marker + 14);
                    var totalLength = LegacyGateType18.HeaderSize + payloadLength;
                    if (totalLength > _maximumInternalFrameLength)
                    {
                        scan = marker + 1;
                        continue;
                    }
                    if (_length - marker < totalLength)
                    {
                        Compact(marker);
                        return true;
                    }

                    var legacy = LegacyGateType18.FromBytes(_buffer, marker, totalLength);
                    if (legacy == null)
                    {
                        scan = marker + 1;
                        continue;
                    }
                    frames.Add(GameGateServerFrame.FromLegacyType18(legacy));
                    scan = marker + totalLength;
                    continue;
                }

                if (discriminator == LegacyGateType19.MessageType)
                {
                    if (_length - marker < LegacyGateType19.HeaderSize)
                    {
                        Compact(marker);
                        return true;
                    }

                    var payloadLength = BitConverter.ToUInt16(_buffer, marker + 14);
                    var totalLength = LegacyGateType19.HeaderSize + payloadLength;
                    if (totalLength > _maximumInternalFrameLength)
                    {
                        scan = marker + 1;
                        continue;
                    }

                    var sessionCount = BitConverter.ToUInt32(_buffer, marker + 8);
                    if (sessionCount > (uint)(payloadLength / sizeof(ushort)))
                    {
                        // Every target consumes one little-endian WORD at the
                        // start of the body. The remaining client payload may be
                        // shorter than 12 bytes; the relay routine then consumes
                        // it without producing a client transmission.
                        scan = marker + 1;
                        continue;
                    }
                    if (_length - marker < totalLength)
                    {
                        Compact(marker);
                        return true;
                    }

                    var legacy = LegacyGateType19.FromBytes(_buffer, marker, totalLength);
                    if (legacy == null)
                    {
                        scan = marker + 1;
                        continue;
                    }
                    frames.Add(GameGateServerFrame.FromLegacyType19(legacy));
                    scan = marker + totalLength;
                    continue;
                }

                if (discriminator == LegacyGateType20.MessageType)
                {
                    if (_length - marker < LegacyGateType20.HeaderSize)
                    {
                        Compact(marker);
                        return true;
                    }

                    var payloadLength = BitConverter.ToUInt16(_buffer, marker + 14);
                    var totalLength = LegacyGateType20.HeaderSize + payloadLength;
                    if (totalLength > _maximumInternalFrameLength)
                    {
                        scan = marker + 1;
                        continue;
                    }
                    if (_length - marker < totalLength)
                    {
                        Compact(marker);
                        return true;
                    }

                    var legacy = LegacyGateType20.FromBytes(_buffer, marker, totalLength);
                    if (legacy == null)
                    {
                        scan = marker + 1;
                        continue;
                    }
                    frames.Add(GameGateServerFrame.FromLegacyType20(legacy));
                    scan = marker + totalLength;
                    continue;
                }

                // 通用 16 字节头 InternalPacket77: total = 0x10 + word[+0x0E](BodyLen)。
                // discriminator(=word[+0x0C]) 是 Cmd, 已在上方读取; 帧长取 +0x0E 的 BodyLen。
                // 证据: 0x5F666A/0x63A66C (total=pos+0x10+word[+0x0E]), 接收器 0x63B258。
                var bodyLength = BitConverter.ToUInt16(_buffer, marker + 14);
                var frameLength = InternalPacket77.HEADER_SIZE + bodyLength;
                if (frameLength > _maximumInternalFrameLength)
                {
                    scan = marker + 1;
                    continue;
                }

                if (_length - marker < frameLength)
                {
                    Compact(marker);
                    return true;
                }

                var internalPacket = InternalPacket77.FromBytes(_buffer, marker, frameLength);
                if (internalPacket == null || internalPacket.Magic != InternalPacket77.MAGIC)
                {
                    scan = marker + 1;
                    continue;
                }
                frames.Add(GameGateServerFrame.FromInternal77(internalPacket));
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
            var keep = 0;
            var available = _length - start;
            if (available >= 3
                && _buffer[_length - 3] == 0x77
                && _buffer[_length - 2] == 0xBB
                && _buffer[_length - 1] == 0xAA)
                keep = 3;
            else if (available >= 2
                && _buffer[_length - 2] == 0x77
                && _buffer[_length - 1] == 0xBB)
                keep = 2;
            else if (available >= 1 && _buffer[_length - 1] == 0x77)
                keep = 1;
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
