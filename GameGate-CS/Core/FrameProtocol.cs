using System.Runtime.InteropServices;

namespace GameGate.Models;

/// <summary>
/// 0xFF44FF44 Frame Protocol — matches Delphi GG original 1:1.
/// 12-byte header: Sign(4B) + Flags(1B) + Cmd(1B) + DataLen(2B) + Ident(4B).
/// No CRC32 footer (Delphi does not use CRC32).
/// </summary>
public static class FrameProtocol
{
    public const uint FRAME_HEADER_MAGIC = 0xFF44FF44;
    public const int HEADER_SIZE = 12;  // Sign(4) + Flags(1) + Cmd(1) + DataLen(2) + Ident(4)
    public const int MAX_PAYLOAD = 0x8000; // 32KB

    /// <summary>Build a frame with the complete 12-byte Delphi header. No CRC32.</summary>
    public static byte[] BuildFrame(byte flags, byte cmd, byte[] payload, uint ident = 0)
    {
        int total = HEADER_SIZE + payload.Length;
        var buf = new byte[total];
        var span = buf.AsSpan();

        // Header: Sign(4) + Flags(1) + Cmd(1) + DataLen(2) + Ident(4)
        BitConverter.TryWriteBytes(span[0..4], FRAME_HEADER_MAGIC);
        span[4] = flags;
        span[5] = cmd;
        BitConverter.TryWriteBytes(span[6..8], (ushort)payload.Length);
        BitConverter.TryWriteBytes(span[8..12], ident);

        // Payload
        payload.CopyTo(span[HEADER_SIZE..]);

        return buf;
    }

    /// <summary>Build a heartbeat frame (CMD 0x19, no payload).</summary>
    public static byte[] BuildHeartbeat() => BuildFrame(0, 0x19, [], 0);
}

/// <summary>Frame parse state machine — 12-byte header, sticky-packet handling.</summary>
public class FrameParser
{
    private enum State { WaitHeader, WaitPayload }

    private State _state = State.WaitHeader;
    private byte[] _buffer = new byte[131072]; // 128KB
    private int _bufLen;
    private int _processed;
    private byte _currentFlags;
    private byte _currentCmd;
    private ushort _currentDataLen;
    private uint _currentIdent;

    public List<(byte flags, byte cmd, uint ident, byte[] payload)> Feed(byte[] data, int offset, int length)
    {
        var frames = new List<(byte, byte, uint, byte[])>();

        // Compact if needed
        if (_processed > 65536)
        {
            Buffer.BlockCopy(_buffer, _processed, _buffer, 0, _bufLen - _processed);
            _bufLen -= _processed;
            _processed = 0;
        }

        // Append
        if (_bufLen + length > _buffer.Length)
        {
            var newBuf = new byte[Math.Max(_buffer.Length * 2, _bufLen + length + 4096)];
            Buffer.BlockCopy(_buffer, _processed, newBuf, 0, _bufLen - _processed);
            _bufLen -= _processed;
            _processed = 0;
            _buffer = newBuf;
        }
        Buffer.BlockCopy(data, offset, _buffer, _bufLen, length);
        _bufLen += length;

        while (true)
        {
            int avail = _bufLen - _processed;

            if (_state == State.WaitHeader)
            {
                // Need at least 12 bytes for full header
                if (avail < FrameProtocol.HEADER_SIZE) break;
                bool found = false;
                int scanEnd = avail - 11; // need 12 bytes to read full header
                for (int i = 0; i < scanEnd; i++)
                {
                    uint val = BitConverter.ToUInt32(_buffer, _processed + i);
                    if (val == FrameProtocol.FRAME_HEADER_MAGIC)
                    {
                        _processed += i;
                        // Read full 12-byte header: Sign(4) Flags(1) Cmd(1) DataLen(2) Ident(4)
                        _currentFlags = _buffer[_processed + 4];
                        _currentCmd = _buffer[_processed + 5];
                        _currentDataLen = BitConverter.ToUInt16(_buffer, _processed + 6);
                        _currentIdent = BitConverter.ToUInt32(_buffer, _processed + 8);
                        if (_currentDataLen > FrameProtocol.MAX_PAYLOAD)
                        { _processed += FrameProtocol.HEADER_SIZE; continue; }
                        _state = State.WaitPayload;
                        found = true;
                        break;
                    }
                }
                if (!found) { _processed += Math.Max(0, scanEnd); break; }
            }
            else // WaitPayload
            {
                int total = FrameProtocol.HEADER_SIZE + _currentDataLen;
                if (avail < total) break;
                var payload = new byte[_currentDataLen];
                Buffer.BlockCopy(_buffer, _processed + FrameProtocol.HEADER_SIZE, payload, 0, _currentDataLen);
                frames.Add((_currentFlags, _currentCmd, _currentIdent, payload));
                _processed += total;
                _state = State.WaitHeader;
            }
        }
        return frames;
    }

    public void Reset() { _state = State.WaitHeader; _bufLen = 0; _processed = 0; }
}
