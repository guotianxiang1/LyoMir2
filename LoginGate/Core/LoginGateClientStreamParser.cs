using System.Buffers.Binary;

namespace LoginGate.Core;

internal sealed class LoginGateClientStreamParser
{
    private static readonly byte[] MagicBytes = [0x44, 0xFF, 0x44, 0xFF];
    private const int MaximumBufferedLength = 4096;
    private byte[] _buffer = new byte[512];
    private int _length;

    public int BufferedLength => _length;

    public void Append(ReadOnlySpan<byte> data, Action<LoginGateClientFrame> onFrame)
    {
        ArgumentNullException.ThrowIfNull(onFrame);
        if (data.Length > MaximumBufferedLength - _length)
            throw new InvalidDataException("LoginGate client stream buffer limit exceeded");

        EnsureCapacity(_length + data.Length);
        data.CopyTo(_buffer.AsSpan(_length));
        _length += data.Length;
        ParseAvailable(onFrame);
    }

    public void Reset()
    {
        if (_length > 0) Array.Clear(_buffer, 0, _length);
        _length = 0;
    }

    private void ParseAvailable(Action<LoginGateClientFrame> onFrame)
    {
        var scan = 0;
        while (_length - scan >= MagicBytes.Length)
        {
            var marker = FindMagic(scan);
            if (marker < 0)
            {
                KeepMagicSuffix(scan);
                return;
            }
            if (_length - marker < LoginGateWireProtocol.ClientHeaderSize)
            {
                Compact(marker);
                return;
            }

            var payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(
                _buffer.AsSpan(marker + 6, 2));
            if (payloadLength > LoginGateWireProtocol.ClientInboundMaximumPayloadSize)
            {
                Reset();
                throw new InvalidDataException(
                    $"LoginGate client payload exceeds {LoginGateWireProtocol.ClientInboundMaximumPayloadSize} bytes");
            }

            var frameLength = LoginGateWireProtocol.ClientHeaderSize + payloadLength;
            if (_length - marker < frameLength)
            {
                Compact(marker);
                return;
            }
            if (!LoginGateWireProtocol.TryDecodeClientFrame(
                    _buffer.AsSpan(marker, frameLength), out var frame, out var error))
            {
                Reset();
                throw new InvalidDataException(error);
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

        KeepMagicSuffix(scan);
    }

    private int FindMagic(int start)
    {
        for (var index = start; index + MagicBytes.Length <= _length; index++)
        {
            if (_buffer[index] == MagicBytes[0]
                && _buffer[index + 1] == MagicBytes[1]
                && _buffer[index + 2] == MagicBytes[2]
                && _buffer[index + 3] == MagicBytes[3])
                return index;
        }
        return -1;
    }

    private void KeepMagicSuffix(int start)
    {
        var available = _length - start;
        var maximum = Math.Min(MagicBytes.Length - 1, available);
        var keep = 0;
        for (var count = maximum; count > 0; count--)
        {
            if (_buffer.AsSpan(_length - count, count)
                .SequenceEqual(MagicBytes.AsSpan(0, count)))
            {
                keep = count;
                break;
            }
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
        Array.Resize(ref _buffer, Math.Min(MaximumBufferedLength,
            Math.Max(required, _buffer.Length * 2)));
    }
}
