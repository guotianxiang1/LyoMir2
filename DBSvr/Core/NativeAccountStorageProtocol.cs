using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using SystemModule.Packet;

namespace DBSvr.Core
{
    public sealed class NativeAccountStorageRequest
    {
        public byte[] Header { get; init; } = Array.Empty<byte>();
        public byte[] Account { get; init; } = Array.Empty<byte>();
        public byte[] CharacterName { get; init; } = Array.Empty<byte>();
        public byte[] Data { get; init; }
    }

    public sealed class NativeAccountStorageBlobResult
    {
        public int Result { get; init; }
        public byte[] Data { get; init; }
        public string Error { get; init; } = string.Empty;
    }

    public static class NativeAccountStorageProtocol
    {
        public const ushort LoadCommand = 0x016B;
        public const ushort SaveCommand = 0x016C;
        public const ushort LoadResponseCommand = 0x0062;
        public const ushort SaveResponseCommand = 0x0063;
        public const int HeaderSize = 0x48;
        public const int ItemSize = 0xD0;
        private const int AccountOffset = 0x10;
        private const int AccountCapacity = 20;
        private const int CharacterOffset = 0x25;
        private const int CharacterCapacity = 15;

        public static bool TryDecode(LegacyDbServerFrame frame,
            ushort command, out NativeAccountStorageRequest request,
            out string error)
        {
            request = null;
            error = string.Empty;
            if (frame == null || frame.Type != 1)
            {
                error = $"native storage 0x{command:X4} envelope is invalid";
                return false;
            }
            if (frame.Payload.Length < HeaderSize)
            {
                error = "native storage payload is shorter than 0x48 bytes";
                return false;
            }
            var payload = frame.Payload.AsSpan();
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload) != command)
            {
                error = $"native storage 0x{command:X4} command mismatch";
                return false;
            }
            if (!TryReadShortString(payload, AccountOffset, AccountCapacity,
                    out var account, out error)
                || !TryReadShortString(payload, CharacterOffset,
                    CharacterCapacity, out var characterName, out error))
                return false;

            byte[] data = null;
            if (command == SaveCommand)
            {
                data = payload.Slice(HeaderSize).ToArray();
                if (data.Length < 4)
                {
                    error = "native storage save data is shorter than 4 bytes";
                    return false;
                }
                var count = BinaryPrimitives.ReadUInt16LittleEndian(
                    data.AsSpan(2, 2));
                if (data.Length != 4 + count * ItemSize)
                {
                    error = "native storage save item count does not match its length";
                    return false;
                }
            }
            request = new NativeAccountStorageRequest
            {
                Header = payload.Slice(0, HeaderSize).ToArray(),
                Account = account,
                CharacterName = characterName,
                Data = data
            };
            return true;
        }

        public static LegacyDbServerFrame CreateLoadResponse(
            NativeAccountStorageRequest request, int result,
            byte[] data = null)
        {
            if (request?.Header == null
                || request.Header.Length != HeaderSize)
                throw new ArgumentException(
                    "native storage request header must be 0x48 bytes",
                    nameof(request));
            var success = result == 1 && data != null;
            var payload = new byte[HeaderSize + (success ? data.Length : 0)];
            request.Header.CopyTo(payload, 0);
            BinaryPrimitives.WriteUInt16LittleEndian(payload,
                LoadResponseCommand);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2),
                unchecked((ushort)result));
            if (success) data.CopyTo(payload, HeaderSize);
            return new LegacyDbServerFrame(1, 0, payload);
        }

        public static LegacyDbServerFrame CreateSaveResponse(
            NativeAccountStorageRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var payload = new byte[HeaderSize];
            BinaryPrimitives.WriteUInt16LittleEndian(payload,
                SaveResponseCommand);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2), 1);
            WriteShortString(payload, AccountOffset, AccountCapacity,
                request.Account);
            WriteShortString(payload, CharacterOffset, CharacterCapacity,
                request.CharacterName);
            return new LegacyDbServerFrame(1, 0, payload);
        }

        private static bool TryReadShortString(ReadOnlySpan<byte> source,
            int offset, int capacity, out byte[] value, out string error)
        {
            value = null;
            error = string.Empty;
            var length = source[offset];
            if (length > capacity)
            {
                error = $"native storage ShortString exceeds {capacity} bytes";
                return false;
            }
            value = source.Slice(offset + 1, length).ToArray();
            return true;
        }

        private static void WriteShortString(Span<byte> destination,
            int offset, int capacity, byte[] value)
        {
            value ??= Array.Empty<byte>();
            var length = Math.Min(capacity, value.Length);
            destination[offset] = (byte)length;
            value.AsSpan(0, length).CopyTo(destination.Slice(offset + 1));
        }
    }

    public static class NativeAccountStorageBlobCodec
    {
        private const int HeaderSize = 8;
        private const int MaximumDataSize = 0x20000;

        public static NativeAccountStorageBlobResult Decode(byte[] blob)
        {
            if (blob == null || blob.Length <= HeaderSize)
                return Failure(-3,
                    "native storage Blob data length is invalid");

            var crc = BinaryPrimitives.ReadUInt32LittleEndian(blob);
            var expectedLength = BinaryPrimitives.ReadUInt16LittleEndian(
                blob.AsSpan(4, 2));
            var compressedLength = BinaryPrimitives.ReadUInt16LittleEndian(
                blob.AsSpan(6, 2));
            if (compressedLength == 0)
            {
                if (blob.Length < 12)
                    return Failure(-4,
                        "native storage Blob header is invalid");
                var rawLength = BinaryPrimitives.ReadInt32LittleEndian(
                    blob.AsSpan(4, 4));
                if (rawLength < 0 || rawLength > MaximumDataSize
                    || HeaderSize + rawLength > blob.Length)
                    return Failure(-3,
                        "native storage Blob data length is invalid");
                return new NativeAccountStorageBlobResult
                {
                    Result = 1,
                    Data = blob.AsSpan(HeaderSize, rawLength).ToArray()
                };
            }

            if (HeaderSize + compressedLength > blob.Length)
                return Failure(-3,
                    "native storage Blob data length is invalid");
            var compressed = blob.AsSpan(HeaderSize, compressedLength);
            if (crc != 0 && ComputeNativeCrc(compressed) != crc)
                return Failure(-5, "native storage Blob CRC mismatch");
            try
            {
                using var input = new MemoryStream(compressed.ToArray(), false);
                using var zlib = new ZLibStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream(expectedLength);
                var buffer = new byte[8192];
                int read;
                while ((read = zlib.Read(buffer, 0, buffer.Length)) != 0)
                {
                    if (output.Length + read > MaximumDataSize)
                        return Failure(-6,
                            "native storage Blob expands beyond 0x20000 bytes");
                    output.Write(buffer, 0, read);
                }
                var data = output.ToArray();
                if (data.Length != expectedLength)
                    return Failure(-6,
                        "native storage Blob decompressed length is invalid");
                return new NativeAccountStorageBlobResult
                {
                    Result = 1,
                    Data = data
                };
            }
            catch (Exception ex) when (ex is IOException
                                       || ex is InvalidDataException)
            {
                return Failure(-6,
                    "native storage Blob decompression failed: " + ex.Message);
            }
        }

        public static byte[] EncodeUncompressed(byte[] data)
        {
            data ??= Array.Empty<byte>();
            if (data.Length > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(data));
            var blob = new byte[HeaderSize + data.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(4, 2),
                (ushort)data.Length);
            data.CopyTo(blob, HeaderSize);
            return blob;
        }

        public static byte[] Encode(byte[] data)
        {
            data ??= Array.Empty<byte>();
            if (data.Length > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(data));
            var uncompressed = EncodeUncompressed(data);
            if (uncompressed.Length < 0x400) return uncompressed;

            byte[] compressed;
            using (var output = new MemoryStream())
            {
                using (var zlib = new ZLibStream(output,
                           CompressionLevel.SmallestSize, true))
                    zlib.Write(data, 0, data.Length);
                compressed = output.ToArray();
            }
            if (compressed.Length == 0
                || compressed.Length > ushort.MaxValue)
                return uncompressed;
            var compressedStorageLength = checked(
                (HeaderSize + compressed.Length + 0xFF) & ~0xFF);
            if (compressedStorageLength >= uncompressed.Length)
                return uncompressed;

            var blob = new byte[compressedStorageLength];
            BinaryPrimitives.WriteUInt32LittleEndian(blob,
                ComputeNativeCrc(compressed));
            BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(4, 2),
                (ushort)data.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(6, 2),
                (ushort)compressed.Length);
            compressed.CopyTo(blob, HeaderSize);
            return blob;
        }

        public static uint ComputeNativeCrc(ReadOnlySpan<byte> data)
        {
            var crc = uint.MaxValue;
            foreach (var value in data)
            {
                crc ^= value;
                for (var bit = 0; bit < 8; bit++)
                    crc = (crc & 1) != 0
                        ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
            return crc;
        }

        private static NativeAccountStorageBlobResult Failure(int result,
            string error) => new() { Result = result, Error = error };
    }
}
