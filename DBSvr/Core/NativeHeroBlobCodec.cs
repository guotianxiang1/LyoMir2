using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using SystemModule;

namespace DBSvr.Core
{
    /// <summary>
    /// SQL Blob envelope used by the native Delphi DBServer for hero_data.Data/dynData.
    /// Data stores its uncompressed total length at +4; dynData stores its payload length.
    /// </summary>
    public static class NativeHeroBlobCodec
    {
        public const int HeaderSize = 8;
        public const int MaximumMySqlBlobSize = ushort.MaxValue;
        public const int MaximumSqlBlobBufferSize = 0x20000;
        public const int ThreeHeroRecordSize = NativeHeroDbFrameCodec.HeroRecordSize * 3;

        private const int DataCompressionThreshold = 1024;
        private const int DynamicCompressionThreshold = 0x408;

        public static bool TryDecodeDataBlob(byte[] blob, out byte[] data, out string error)
        {
            data = null;
            if (!TryReadDataEnvelope(blob, out data, out error)) return false;
            return ValidateDataPayload(data, out error);
        }

        public static bool TryEncodeDataBlob(byte[] data, out byte[] blob, out string error)
        {
            blob = null;
            if (!ValidateDataPayload(data, out error)) return false;

            // Data's compression envelope is the first eight bytes of the fixed record.
            // The Delphi server compresses record[8..] and keeps the total record length at +4.
            var uncompressed = (byte[])data.Clone();
            BinaryPrimitives.WriteUInt32LittleEndian(uncompressed.AsSpan(0, 4), 0);
            BinaryPrimitives.WriteUInt16LittleEndian(uncompressed.AsSpan(4, 2),
                checked((ushort)uncompressed.Length));
            BinaryPrimitives.WriteUInt16LittleEndian(uncompressed.AsSpan(6, 2), 0);
            return TryCompressEnvelope(uncompressed, uncompressed.AsSpan(HeaderSize).ToArray(),
                checked((ushort)uncompressed.Length), DataCompressionThreshold,
                out blob, out error);
        }

        public static bool TryApplyIndexForceLevel(byte[] data,
            ushort forceLevel, out byte[] updated, out string error)
        {
            updated = null;
            error = string.Empty;
            if (data == null
                || data.Length != NativeHeroDbFrameCodec.HeroRecordSize
                && data.Length != ThreeHeroRecordSize)
            {
                error = "native hero ForceLv Data has an invalid record count";
                return false;
            }
            updated = (byte[])data.Clone();
            for (var offset = 0; offset < updated.Length;
                 offset += NativeHeroDbFrameCodec.HeroRecordSize)
                BinaryPrimitives.WriteUInt16LittleEndian(
                    updated.AsSpan(offset + NativeHeroDbFrameCodec.IndexForceLvOffset,
                        sizeof(ushort)), forceLevel);
            return true;
        }

        public static bool TryMergeDataRecord(byte[] existingData, byte[] record,
            out byte[] mergedData, out string error)
            => TryMergeDataRecord(existingData, record, false, out mergedData, out error);

        public static bool TryMergeDataRecord(byte[] existingData, byte[] record,
            bool requireThreeRecords, out byte[] mergedData, out string error)
        {
            mergedData = null;
            if (record == null || record.Length != NativeHeroDbFrameCodec.HeroRecordSize)
            {
                error = $"native hero save record length must be {NativeHeroDbFrameCodec.HeroRecordSize}";
                return false;
            }
            if (!NativeHeroDbFrameCodec.TryCreateRecord(record, out var nativeRecord, out error))
                return false;

            existingData ??= Array.Empty<byte>();
            if (requireThreeRecords && existingData.Length != ThreeHeroRecordSize)
            {
                error = $"special hero Data payload length must be {ThreeHeroRecordSize}";
                return false;
            }
            if (existingData.Length == 0
                || existingData.Length == NativeHeroDbFrameCodec.HeroRecordSize)
            {
                mergedData = (byte[])record.Clone();
                return true;
            }
            if (!ValidateDataPayload(existingData, out error)) return false;

            // The native three-record format uses the fixed record's Job byte (+0x2A)
            // as its zero-based slice selector.
            var slot = nativeRecord.Job;
            if (slot >= 3)
            {
                error = $"native hero three-record slot {slot} is outside 0..2";
                return false;
            }
            mergedData = (byte[])existingData.Clone();
            record.CopyTo(mergedData, slot * NativeHeroDbFrameCodec.HeroRecordSize);
            return true;
        }

        public static bool TrySelectDataRecord(byte[] data, int requestedSlot,
            bool requireThreeRecords, out byte[] record, out string error)
        {
            record = null;
            if (!ValidateDataPayload(data, out error)) return false;
            if (requireThreeRecords && data.Length != ThreeHeroRecordSize)
            {
                error = $"special hero Data payload length must be {ThreeHeroRecordSize}";
                return false;
            }
            if (data.Length == NativeHeroDbFrameCodec.HeroRecordSize)
            {
                record = (byte[])data.Clone();
                return true;
            }

            // The Delphi request uses only the low byte of the slot field.
            var slot = unchecked((byte)requestedSlot);
            if (slot >= 3)
            {
                error = $"native hero three-record slot {slot} is outside 0..2";
                return false;
            }
            record = data.AsSpan(slot * NativeHeroDbFrameCodec.HeroRecordSize,
                NativeHeroDbFrameCodec.HeroRecordSize).ToArray();
            return true;
        }

        /// <summary>
        /// Applies the original 0x0167 fixed-record conversion. The lower-level hero
        /// becomes the three-job record; the other hero receives rank marker 1.
        /// </summary>
        public static bool TryBuildThreeSlotData(byte[] lowerLevelData,
            byte[] higherLevelData, out byte[] threeSlotData,
            out byte[] rankedHigherLevelData, out string error)
        {
            threeSlotData = null;
            rankedHigherLevelData = null;
            if (!ValidateDataPayload(lowerLevelData, out error)
                || !ValidateDataPayload(higherLevelData, out error))
                return false;
            if (lowerLevelData.Length != NativeHeroDbFrameCodec.HeroRecordSize)
            {
                error = "native hero 0x0167 source must contain one fixed record";
                return false;
            }

            var source = (byte[])lowerLevelData.Clone();
            source[NativeHeroDbFrameCodec.HeroRankOffset] = 2;
            threeSlotData = new byte[ThreeHeroRecordSize];
            var sourceJob = source[NativeHeroDbFrameCodec.JobOffset];
            if (sourceJob < 3)
            {
                for (var slot = 0; slot < 3; slot++)
                {
                    var destination = threeSlotData.AsSpan(
                        slot * NativeHeroDbFrameCodec.HeroRecordSize,
                        NativeHeroDbFrameCodec.HeroRecordSize);
                    if (slot == sourceJob)
                    {
                        source.CopyTo(destination);
                    }
                    else
                    {
                        CopyRange(source, destination, 0x0000, 0x0008);
                        CopyShortString(source, destination, 0x0008, 15);
                        CopyShortString(source, destination, 0x0018, 15);
                        CopyRange(source, destination, 0x0028, 0x0002);
                        CopyRange(source, destination, 0x002C, 0x0002);
                        CopyRange(source, destination, 0x0030, 0x0008);
                        CopyRange(source, destination, 0x003C, 0x0070);
                        CopyRange(source, destination, 0x00AD, 0x0010);
                        destination[0x00BE] = source[0x00BE];
                        CopyRange(source, destination, 0x00C0, 0x002A);
                        destination[NativeHeroDbFrameCodec.HeroRankOffset] = 2;
                        CopyRange(source, destination, 0x00EB, 0x0011);
                        CopyRange(source, destination, 0x0100, 0x000C);
                        CopyRange(source, destination, 0x012C, 0x0040);
                        CopyRange(source, destination, 0x4644, 0x007C);
                        CopyRange(source, destination, 0x4810, 0x0078);
                        CopyRange(source, destination, 0x48DE, 0x00F6);
                    }
                }
            }
            else
            {
                // 57FAA0 writes this marker before rejecting an invalid source job;
                // its caller ignores that result and still publishes the sparse record.
                BinaryPrimitives.WriteUInt16LittleEndian(
                    threeSlotData.AsSpan(4, 2),
                    checked((ushort)ThreeHeroRecordSize));
            }
            for (var slot = 0; slot < 3; slot++)
            {
                threeSlotData[slot * NativeHeroDbFrameCodec.HeroRecordSize
                              + NativeHeroDbFrameCodec.JobOffset] = (byte)slot;
            }

            rankedHigherLevelData = (byte[])higherLevelData.Clone();
            if (rankedHigherLevelData.Length == NativeHeroDbFrameCodec.HeroRecordSize)
                rankedHigherLevelData[NativeHeroDbFrameCodec.HeroRankOffset] = 1;

            if (ValidateDataPayload(threeSlotData, out error)
                && ValidateDataPayload(rankedHigherLevelData, out error))
                return true;
            threeSlotData = null;
            rankedHigherLevelData = null;
            return false;
        }

        private static void CopyRange(byte[] source, Span<byte> destination,
            int offset, int length)
            => source.AsSpan(offset, length).CopyTo(destination.Slice(offset, length));

        private static void CopyShortString(byte[] source, Span<byte> destination,
            int offset, int capacity)
        {
            var length = Math.Min(source[offset], capacity);
            destination[offset] = (byte)length;
            source.AsSpan(offset + 1, length)
                .CopyTo(destination.Slice(offset + 1, length));
        }

        public static bool TryDecodeDynamicBlob(byte[] blob, out byte[] dynamicData, out string error)
        {
            dynamicData = null;
            error = string.Empty;
            if (blob == null || blob.Length == 0)
            {
                dynamicData = Array.Empty<byte>();
                return true;
            }
            if (!TryReadDynamicEnvelope(blob, out dynamicData, out error)) return false;
            return true;
        }

        public static bool TryEncodeDynamicBlob(byte[] dynamicData, out byte[] blob, out string error)
        {
            blob = null;
            error = string.Empty;
            dynamicData ??= Array.Empty<byte>();
            if (dynamicData.Length == 0)
            {
                blob = Array.Empty<byte>();
                return true;
            }

            int uncompressedLength;
            try { uncompressedLength = RoundUp256(checked(HeaderSize + dynamicData.Length)); }
            catch (OverflowException)
            {
                error = "native hero dynData Blob payload is too large";
                return false;
            }
            var uncompressed = new byte[uncompressedLength];
            var lengthMarker = unchecked((ushort)dynamicData.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(uncompressed.AsSpan(4, 2),
                lengthMarker);
            dynamicData.CopyTo(uncompressed, HeaderSize);
            blob = uncompressed;
            if (uncompressedLength <= DynamicCompressionThreshold) return true;

            byte[] compressed;
            try
            {
                using var output = new MemoryStream();
                using (var zlib = new ZLibStream(
                           output, CompressionLevel.SmallestSize, true))
                    zlib.Write(dynamicData, 0, lengthMarker);
                compressed = output.ToArray();
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException)
            {
                error = "native hero dynData zlib compression failed: " + ex.Message;
                blob = null;
                return false;
            }

            if (compressed.Length > ushort.MaxValue) return true;
            var compressedLength = (ushort)compressed.Length;
            var alignedCompressedLength = RoundUp256(HeaderSize + compressedLength);
            if (alignedCompressedLength >= uncompressedLength) return true;

            blob = new byte[alignedCompressedLength];
            var storedCompressed = compressed.AsSpan(0, compressedLength);
            BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(0, 4),
                ComputeNativeCrc(storedCompressed));
            BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(4, 2), lengthMarker);
            BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(6, 2),
                compressedLength);
            storedCompressed.CopyTo(blob.AsSpan(HeaderSize));
            return true;
        }

        public static uint ComputeNativeCrc(ReadOnlySpan<byte> data)
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

        private static bool ValidateDataPayload(byte[] data, out string error)
        {
            error = string.Empty;
            if (data == null || data.Length != NativeHeroDbFrameCodec.HeroRecordSize
                && data.Length != ThreeHeroRecordSize)
            {
                error = $"native hero Data payload length must be "
                        + $"{NativeHeroDbFrameCodec.HeroRecordSize} or {ThreeHeroRecordSize}";
                return false;
            }

            for (var offset = 0; offset < data.Length;
                 offset += NativeHeroDbFrameCodec.HeroRecordSize)
            {
                var record = data.AsSpan(offset, NativeHeroDbFrameCodec.HeroRecordSize).ToArray();
                if (!NativeHeroDbFrameCodec.TryCreateRecord(record, out _, out error))
                {
                    error = $"invalid native hero Data record at 0x{offset:X}: {error}";
                    return false;
                }
            }
            return true;
        }

        private static bool TryReadDataEnvelope(byte[] blob, out byte[] data, out string error)
        {
            data = null;
            error = string.Empty;
            if (blob == null || blob.Length < HeaderSize)
            {
                error = "native hero Data Blob is shorter than its embedded 8-byte header";
                return false;
            }

            var crc = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(0, 4));
            var lengthMarker = BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(4, 2));
            var compressedLength = BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(6, 2));
            if (lengthMarker != NativeHeroDbFrameCodec.HeroRecordSize
                && lengthMarker != ThreeHeroRecordSize)
            {
                error = "native hero Data Blob total-length marker is invalid";
                return false;
            }

            if (compressedLength == 0)
            {
                if (blob.Length != lengthMarker)
                {
                    error = "uncompressed native hero Data Blob length does not match its marker";
                    return false;
                }
                data = (byte[])blob.Clone();
                return true;
            }

            var storedLength = HeaderSize + compressedLength;
            var alignedLength = RoundUp256(storedLength);
            if (blob.Length != alignedLength)
            {
                error = "compressed native hero Data Blob is not stored at its native 256-byte length";
                return false;
            }
            if (!PaddingIsZero(blob, storedLength))
            {
                error = "compressed native hero Data Blob has nonzero padding";
                return false;
            }

            var compressed = blob.AsSpan(HeaderSize, compressedLength);
            if (ComputeNativeCrc(compressed) != crc)
            {
                error = "native hero Data Blob CRC mismatch";
                return false;
            }
            if (!TryDecompress(compressed, lengthMarker - HeaderSize,
                    out var body, out error)) return false;
            if (body.Length != lengthMarker - HeaderSize)
            {
                error = $"native hero Data Blob decompressed length mismatch: "
                        + $"{body.Length} != {lengthMarker - HeaderSize}";
                return false;
            }

            data = new byte[lengthMarker];
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4, 2), lengthMarker);
            body.CopyTo(data, HeaderSize);
            return true;
        }

        private static bool TryReadDynamicEnvelope(byte[] blob,
            out byte[] payload, out string error)
        {
            payload = null;
            error = string.Empty;
            if (blob == null || blob.Length < HeaderSize)
            {
                error = "native hero dynData Blob is shorter than its 8-byte header";
                return false;
            }

            var crc = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(0, 4));
            var expectedPayloadLength =
                BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(4, 2));
            var compressedLength = BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(6, 2));
            if (compressedLength == 0)
            {
                if (blob.Length < HeaderSize + expectedPayloadLength)
                {
                    error = "uncompressed native hero dynData Blob is shorter than its marker";
                    return false;
                }
                payload = blob.AsSpan(HeaderSize, expectedPayloadLength).ToArray();
                return true;
            }

            var storedLength = HeaderSize + compressedLength;
            if (blob.Length < storedLength)
            {
                error = "compressed native hero dynData Blob is shorter than its marker";
                return false;
            }

            var compressed = blob.AsSpan(HeaderSize, compressedLength);
            if (ComputeNativeCrc(compressed) != crc)
            {
                error = "native hero dynData Blob CRC mismatch";
                return false;
            }
            if (!TryDecompress(compressed, expectedPayloadLength, out payload, out error))
                return false;
            if (payload.Length == 0 || payload.Length != expectedPayloadLength)
            {
                error = $"native hero Blob decompressed length mismatch: "
                        + $"{payload.Length} != {expectedPayloadLength}";
                return false;
            }
            return true;
        }

        private static bool TryCompressEnvelope(byte[] uncompressed, byte[] payload,
            ushort lengthMarker, int compressionThreshold, out byte[] blob, out string error)
        {
            error = string.Empty;
            blob = uncompressed;
            var canStoreUncompressed = uncompressed.Length <= MaximumMySqlBlobSize;
            if (uncompressed.Length < compressionThreshold)
            {
                if (canStoreUncompressed) return true;
                error = "native hero Blob exceeds the MySQL BLOB limit";
                blob = null;
                return false;
            }

            byte[] compressed;
            try
            {
                using var output = new MemoryStream();
                using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, true))
                    zlib.Write(payload, 0, payload.Length);
                compressed = output.ToArray();
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException)
            {
                error = "native hero Blob zlib compression failed: " + ex.Message;
                blob = null;
                return false;
            }

            if (compressed.Length == 0 || compressed.Length > ushort.MaxValue)
            {
                if (canStoreUncompressed) return true;
                error = "native hero Blob cannot fit in a MySQL BLOB";
                blob = null;
                return false;
            }
            var alignedLength = RoundUp256(HeaderSize + compressed.Length);
            if (alignedLength >= uncompressed.Length || alignedLength > MaximumMySqlBlobSize)
            {
                if (canStoreUncompressed) return true;
                error = "native hero Blob cannot fit in a MySQL BLOB";
                blob = null;
                return false;
            }

            blob = new byte[alignedLength];
            BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(0, 4),
                ComputeNativeCrc(compressed));
            BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(4, 2), lengthMarker);
            BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(6, 2),
                (ushort)compressed.Length);
            compressed.CopyTo(blob, HeaderSize);
            return true;
        }

        private static bool TryDecompress(ReadOnlySpan<byte> compressed, int expectedLength,
            out byte[] payload, out string error)
        {
            payload = null;
            error = string.Empty;
            if (expectedLength < 0 || expectedLength > MaximumSqlBlobBufferSize)
            {
                error = "native hero Blob decompressed length exceeds 0x20000 bytes";
                return false;
            }
            try
            {
                using var input = new MemoryStream(compressed.ToArray(), false);
                using var zlib = new ZLibStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream(expectedLength);
                var buffer = new byte[8192];
                int read;
                while ((read = zlib.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (output.Length + read > MaximumSqlBlobBufferSize)
                    {
                        error = "native hero Blob expands beyond 0x20000 bytes";
                        return false;
                    }
                    output.Write(buffer, 0, read);
                }
                payload = output.ToArray();
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException)
            {
                error = "invalid native hero Blob zlib stream: " + ex.Message;
                return false;
            }
        }

        private static bool PaddingIsZero(byte[] blob, int offset)
        {
            for (var i = offset; i < blob.Length; i++)
                if (blob[i] != 0) return false;
            return true;
        }

        private static int RoundUp256(int value) => checked((value + 0xFF) & ~0xFF);
    }
}
