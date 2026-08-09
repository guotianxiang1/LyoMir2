using System.Buffers.Binary;
using System.IO.Compression;

namespace MakeIndexOccupancyAudit;

internal static class NativeBlobReaders
{
    internal const int ItemSize = 208;
    private const int HumanRecordSize = 0xEEF8;
    private const ushort HumanSizeMarker = 0xEF00;
    private const int HeroRecordSize = 0x49D4;

    internal static byte[] DecodeHuman(byte[] input)
    {
        if (input.Length == HumanRecordSize) return input;
        if (input.Length < 8 || input.Length % 256 != 0)
            throw new InvalidDataException("native human Blob must be raw 0xEEF8 bytes or a 256-byte-aligned envelope");

        var crc = BinaryPrimitives.ReadUInt32LittleEndian(input);
        if (crc == 0)
        {
            var length = BinaryPrimitives.ReadInt32LittleEndian(input.AsSpan(4, 4));
            if (length == HumanSizeMarker) length = HumanRecordSize;
            if (length != HumanRecordSize || 8L + length > input.Length)
                throw new InvalidDataException("native human uncompressed length is invalid");
            EnsureZeroPadding(input, 8 + length);
            return input.AsSpan(8, length).ToArray();
        }

        var marker = BinaryPrimitives.ReadUInt16LittleEndian(input.AsSpan(4, 2));
        var compressedLength = BinaryPrimitives.ReadUInt16LittleEndian(input.AsSpan(6, 2));
        if (marker != HumanSizeMarker || compressedLength < 6 || 8 + compressedLength > input.Length)
            throw new InvalidDataException("native human compressed header is invalid");
        var compressed = input.AsSpan(8, compressedLength);
        EnsureCrc(compressed, crc);
        EnsureZeroPadding(input, 8 + compressedLength);
        return InflateExact(compressed, HumanRecordSize, "native human");
    }

    internal static byte[] DecodeHero(byte[] input)
    {
        if (input.Length == HeroRecordSize || input.Length == HeroRecordSize * 3)
            return input;
        if (input.Length < 8)
            throw new InvalidDataException("native hero Blob is shorter than 8 bytes");

        var marker = BinaryPrimitives.ReadUInt16LittleEndian(input.AsSpan(4, 2));
        if (marker != HeroRecordSize && marker != HeroRecordSize * 3)
            throw new InvalidDataException("native hero total-length marker is invalid");
        var compressedLength = BinaryPrimitives.ReadUInt16LittleEndian(input.AsSpan(6, 2));
        if (compressedLength == 0)
        {
            if (input.Length != marker)
                throw new InvalidDataException("native hero uncompressed length does not match marker");
            return input;
        }

        var storedLength = 8 + compressedLength;
        if (input.Length != RoundUp256(storedLength))
            throw new InvalidDataException("native hero compressed Blob length is invalid");
        EnsureZeroPadding(input, storedLength);
        var compressed = input.AsSpan(8, compressedLength);
        EnsureCrc(compressed, BinaryPrimitives.ReadUInt32LittleEndian(input));
        var body = InflateExact(compressed, marker - 8, "native hero");
        var data = new byte[marker];
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4, 2), marker);
        body.CopyTo(data, 8);
        return data;
    }

    internal static byte[] DecodeStorage(byte[] input)
    {
        if (LooksLikeRawStorage(input)) return input;
        if (input.Length <= 8)
            throw new InvalidDataException("native storage Blob is too short");

        var crc = BinaryPrimitives.ReadUInt32LittleEndian(input);
        var expectedLength = BinaryPrimitives.ReadUInt16LittleEndian(input.AsSpan(4, 2));
        var compressedLength = BinaryPrimitives.ReadUInt16LittleEndian(input.AsSpan(6, 2));
        byte[] data;
        if (compressedLength == 0)
        {
            var rawLength = BinaryPrimitives.ReadInt32LittleEndian(input.AsSpan(4, 4));
            if (rawLength < 0 || 8L + rawLength > input.Length)
                throw new InvalidDataException("native storage uncompressed length is invalid");
            data = input.AsSpan(8, rawLength).ToArray();
        }
        else
        {
            if (8 + compressedLength > input.Length)
                throw new InvalidDataException("native storage compressed length is invalid");
            var compressed = input.AsSpan(8, compressedLength);
            if (crc != 0) EnsureCrc(compressed, crc);
            data = InflateExact(compressed, expectedLength, "native storage");
        }
        if (!LooksLikeRawStorage(data))
            throw new InvalidDataException("native storage item count does not match payload length");
        return data;
    }

    internal static bool LooksLikeRawStorage(byte[] data)
    {
        if (data.Length < 4) return false;
        var count = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2, 2));
        return data.Length == 4L + count * ItemSize;
    }

    internal static uint ComputeCrc(ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return crc;
    }

    private static byte[] InflateExact(ReadOnlySpan<byte> compressed, int expectedLength, string label)
    {
        try
        {
            using var input = new MemoryStream(compressed.ToArray(), false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream(expectedLength);
            zlib.CopyTo(output);
            var data = output.ToArray();
            if (data.Length != expectedLength)
                throw new InvalidDataException($"{label} decompressed length {data.Length} != {expectedLength}");
            return data;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException)
        {
            throw new InvalidDataException($"{label} zlib decompression failed", ex);
        }
    }

    private static void EnsureCrc(ReadOnlySpan<byte> data, uint expected)
    {
        var actual = ComputeCrc(data);
        if (actual != expected)
            throw new InvalidDataException($"native Blob CRC mismatch: 0x{actual:X8} != 0x{expected:X8}");
    }

    private static void EnsureZeroPadding(byte[] data, int offset)
    {
        for (var i = offset; i < data.Length; i++)
            if (data[i] != 0)
                throw new InvalidDataException($"native Blob has nonzero padding at 0x{i:X}");
    }

    private static int RoundUp256(int value) => checked((value + 0xFF) & ~0xFF);
}
