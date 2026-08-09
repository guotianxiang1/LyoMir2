using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using SystemModule.Packet;

namespace DBSvr.Core
{
    public sealed class NativeDominatorPetRequest
    {
        public byte[] MasterName { get; init; } = Array.Empty<byte>();
        public byte[] Data { get; init; }
    }

    public static class NativeDominatorPetProtocol
    {
        public const ushort CreateCommand = 0x0181;
        public const ushort LoadCommand = 0x0182;
        public const ushort SaveCommand = 0x0183;
        public const ushort CreateResponseCommand = 0x0136;
        public const ushort LoadResponseCommand = 0x0137;
        public const int HeaderSize = 0x48;
        public const int DataSize = 0xA034;
        public const int SavePayloadSize = HeaderSize + DataSize;
        public const int MasterNameOffset = 0x25;
        public const int MasterNameCapacity = 15;
        public const int DataMasterNameOffset = 0x18;
        public const int DataLevelOffset = 0x28;
        public const int DataExperienceOffset = 0x29;

        public static bool TryDecodeRequest(LegacyDbServerFrame frame,
            ushort command, out NativeDominatorPetRequest request,
            out string error)
        {
            request = null;
            error = string.Empty;
            if (frame == null || frame.Type != 1)
            {
                error = $"native pet 0x{command:X4} envelope is invalid";
                return false;
            }
            var expectedLength = command == SaveCommand
                ? SavePayloadSize : HeaderSize;
            if (command == SaveCommand
                ? frame.Payload.Length != expectedLength
                : frame.Payload.Length < expectedLength)
            {
                error = command == SaveCommand
                    ? $"native pet save payload must be 0x{expectedLength:X} bytes"
                    : "native pet payload is shorter than 0x48 bytes";
                return false;
            }
            var payload = frame.Payload.AsSpan();
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload) != command)
            {
                error = $"native pet 0x{command:X4} command mismatch";
                return false;
            }
            var nameLength = payload[MasterNameOffset];
            if (nameLength > MasterNameCapacity)
            {
                error = "native pet master name exceeds 15 bytes";
                return false;
            }
            request = new NativeDominatorPetRequest
            {
                MasterName = payload.Slice(MasterNameOffset + 1,
                    nameLength).ToArray(),
                Data = command == SaveCommand
                    ? payload.Slice(HeaderSize, DataSize).ToArray()
                    : null
            };
            return true;
        }

        public static LegacyDbServerFrame CreateCreateResponse(
            byte[] masterName, int result)
        {
            var payload = new byte[HeaderSize];
            BinaryPrimitives.WriteUInt16LittleEndian(payload,
                CreateResponseCommand);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4),
                result);
            WriteShortString(payload, MasterNameOffset, MasterNameCapacity,
                masterName);
            return new LegacyDbServerFrame(1, 0, payload);
        }

        public static LegacyDbServerFrame CreateLoadResponse(byte[] masterName,
            int result, byte[] data = null)
        {
            var success = result == 1 && data?.Length == DataSize;
            var payload = new byte[HeaderSize + (success ? DataSize : 0)];
            BinaryPrimitives.WriteUInt16LittleEndian(payload,
                LoadResponseCommand);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2),
                unchecked((ushort)result));
            WriteShortString(payload, 0x10, 20, masterName);
            if (success) data.CopyTo(payload, HeaderSize);
            return new LegacyDbServerFrame(1, 0, payload);
        }

        public static byte[] CreateDefaultData(byte[] masterName)
        {
            var data = new byte[DataSize];
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4, 2),
                DataSize);
            WriteShortString(data, DataMasterNameOffset,
                MasterNameCapacity, masterName);
            return data;
        }

        public static byte[] PrepareData(byte[] source, byte[] masterName)
        {
            if (source == null || source.Length != DataSize)
                throw new ArgumentException(
                    $"native pet data must be 0x{DataSize:X} bytes",
                    nameof(source));
            var data = (byte[])source.Clone();
            WriteShortString(data, DataMasterNameOffset,
                MasterNameCapacity, masterName);
            return data;
        }

        private static void WriteShortString(Span<byte> destination,
            int offset, int capacity, byte[] value)
        {
            destination.Slice(offset, capacity + 1).Clear();
            value ??= Array.Empty<byte>();
            var length = Math.Min(capacity, value.Length);
            destination[offset] = (byte)length;
            value.AsSpan(0, length).CopyTo(destination.Slice(offset + 1));
        }
    }

    public static class NativeDominatorPetBlobCodec
    {
        private const int HeaderSize = 8;

        public static bool TryDecode(byte[] blob, out byte[] data,
            out string error)
        {
            data = null;
            error = string.Empty;
            if (blob == null || blob.Length <= HeaderSize)
            {
                error = "native pet Blob is shorter than its header";
                return false;
            }
            var marker = BinaryPrimitives.ReadUInt16LittleEndian(
                blob.AsSpan(4, 2));
            var compressedLength = BinaryPrimitives.ReadUInt16LittleEndian(
                blob.AsSpan(6, 2));
            if (marker != NativeDominatorPetProtocol.DataSize)
            {
                error = "native pet Blob size marker is invalid";
                return false;
            }
            if (compressedLength == 0)
            {
                if (blob.Length != NativeDominatorPetProtocol.DataSize)
                {
                    error = "uncompressed native pet Blob length is invalid";
                    return false;
                }
                data = (byte[])blob.Clone();
                return true;
            }

            var storedLength = HeaderSize + compressedLength;
            if (blob.Length != RoundUp256(storedLength)
                || !PaddingIsZero(blob, storedLength))
            {
                error = "compressed native pet Blob storage length is invalid";
                return false;
            }
            var compressed = blob.AsSpan(HeaderSize, compressedLength);
            if (ComputeNativeCrc(compressed)
                != BinaryPrimitives.ReadUInt32LittleEndian(blob))
            {
                error = "native pet Blob CRC mismatch";
                return false;
            }
            try
            {
                using var input = new MemoryStream(compressed.ToArray(), false);
                using var zlib = new ZLibStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream(
                    NativeDominatorPetProtocol.DataSize - HeaderSize);
                zlib.CopyTo(output);
                var body = output.ToArray();
                if (body.Length != NativeDominatorPetProtocol.DataSize - HeaderSize)
                {
                    error = "native pet Blob decompressed length is invalid";
                    return false;
                }
                data = new byte[NativeDominatorPetProtocol.DataSize];
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4, 2),
                    NativeDominatorPetProtocol.DataSize);
                body.CopyTo(data, HeaderSize);
                return true;
            }
            catch (Exception ex) when (ex is IOException
                                       || ex is InvalidDataException)
            {
                error = "native pet Blob decompression failed: " + ex.Message;
                return false;
            }
        }

        public static bool TryEncode(byte[] data, out byte[] blob,
            out string error)
        {
            blob = null;
            error = string.Empty;
            if (data == null
                || data.Length != NativeDominatorPetProtocol.DataSize)
            {
                error = "native pet data has an invalid length";
                return false;
            }
            byte[] compressed;
            try
            {
                using var output = new MemoryStream();
                using (var zlib = new ZLibStream(output,
                           CompressionLevel.SmallestSize, true))
                    zlib.Write(data, HeaderSize, data.Length - HeaderSize);
                compressed = output.ToArray();
            }
            catch (Exception ex) when (ex is IOException
                                       || ex is InvalidDataException)
            {
                error = "native pet Blob compression failed: " + ex.Message;
                return false;
            }

            var storedLength = RoundUp256(HeaderSize + compressed.Length);
            if (compressed.Length > ushort.MaxValue
                || storedLength >= data.Length)
            {
                blob = (byte[])data.Clone();
                return true;
            }
            blob = new byte[storedLength];
            BinaryPrimitives.WriteUInt32LittleEndian(blob,
                ComputeNativeCrc(compressed));
            BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(4, 2),
                NativeDominatorPetProtocol.DataSize);
            BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(6, 2),
                (ushort)compressed.Length);
            compressed.CopyTo(blob, HeaderSize);
            return true;
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

        private static int RoundUp256(int length) =>
            checked((length + 0xFF) & ~0xFF);

        private static bool PaddingIsZero(byte[] blob, int offset)
        {
            for (var i = offset; i < blob.Length; i++)
                if (blob[i] != 0) return false;
            return true;
        }
    }
}
