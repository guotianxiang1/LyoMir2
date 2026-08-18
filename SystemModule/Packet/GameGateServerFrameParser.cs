using System;
using System.Collections.Generic;

namespace SystemModule.Packet
{
    public sealed class GameGateServerFrame
    {
        public InternalPacket77 Internal77 { get; private set; }
        public LegacyGateType18 LegacyType18 { get; private set; }

        public static GameGateServerFrame FromInternal77(InternalPacket77 packet) =>
            new GameGateServerFrame { Internal77 = packet };

        public static GameGateServerFrame FromLegacyType18(LegacyGateType18 packet) =>
            new GameGateServerFrame { LegacyType18 = packet };
    }

    /// <summary>
    /// Parses the shared M2-to-GameGate stream without changing the existing
    /// InternalPacket77 parser. Native outer types 17 and 18 at offset 12 use
    /// the legacy 16-byte envelope. Type 17 is consumed without dispatch, as in
    /// the Delphi GameGate; type 18 is returned for native broadcast routing.
    /// </summary>
    public sealed class GameGateServerFrameParser
    {
        private const ushort IgnoredLegacyMessageType = 17;
        private readonly int _maximumBufferedLength;
        private readonly int _maximumInternalFrameLength;
        private byte[] _buffer = new byte[8192];
        private int _length;

        public GameGateServerFrameParser(
            int maximumBufferedLength = InternalPacket77FrameParser.DefaultMaximumBufferedLength,
            int maximumInternalFrameLength = ushort.MaxValue)
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
                if (discriminator == IgnoredLegacyMessageType)
                {
                    if (_length - marker < LegacyGateType18.HeaderSize)
                    {
                        Compact(marker);
                        return true;
                    }

                    var payloadLength = BitConverter.ToUInt16(_buffer, marker + 14);
                    var totalLength = LegacyGateType18.HeaderSize + payloadLength;
                    if (totalLength >= LegacyGateType18.MaximumFrameLengthExclusive)
                    {
                        scan = marker + 1;
                        continue;
                    }
                    if (_length - marker < totalLength)
                    {
                        Compact(marker);
                        return true;
                    }

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
                    // Command 18 is ambiguous on this stream: the legacy broadcast
                    // envelope carries a 12-byte client sub-header, while an ordinary
                    // InternalPacket77 command 18 may have a shorter body.  Only take
                    // the legacy branch when its complete minimum shape is present.
                    // Short command-18 frames must fall through to the generic parser;
                    // scanning past the marker here can mistake a marker inside their
                    // payload for a new frame and leave a partial tail buffered.
                    if (payloadLength >= LegacyGateType18.ClientPacketSize
                        && totalLength < LegacyGateType18.MaximumFrameLengthExclusive)
                    {
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

                // A malformed legacy type-18 header can advertise a body that
                // extends across the next real frame.  Prefer a complete marker
                // inside that declared span as the resynchronization point.  A
                // marker merely embedded in a short command-18 payload is not
                // enough: without a complete following frame it remains payload.
                var nestedMarker = FindMarker(marker + InternalPacket77.HEADER_SIZE);
                if (discriminator == LegacyGateType18.MessageType
                    && (bodyLength < LegacyGateType18.ClientPacketSize
                        || frameLength >= LegacyGateType18.MaximumFrameLengthExclusive)
                    && nestedMarker > marker
                    && nestedMarker < marker + frameLength
                    && IsCompleteInternalFrame(nestedMarker))
                {
                    scan = nestedMarker;
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

        private bool IsCompleteInternalFrame(int marker)
        {
            if (_length - marker < InternalPacket77.HEADER_SIZE)
                return false;
            var bodyLength = BitConverter.ToUInt16(_buffer, marker + 14);
            var frameLength = InternalPacket77.HEADER_SIZE + bodyLength;
            return frameLength <= _maximumInternalFrameLength
                && _length - marker >= frameLength;
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
