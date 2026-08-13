using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace SystemModule
{
    /// <summary>
    /// Stores an eye-item sidecar in native hero type-7 records.
    ///
    /// Native sub_68AE28 walks the 0xFAFA array in 16-byte steps (0x68AEDF add ebx,0x10) and
    /// compares the whole DWORD at record+12 (0x68AE73 mov eax,[eax+0x0C]) against the
    /// zero-extended Job byte at obj+0x72 (0x68AE7C movzx edx,byte[edx+0x72]). Records that
    /// differ are copied out verbatim (0x68AEC8 mov ecx,0x10) and re-appended on save
    /// (0x68AC7A/0x68AC8A), which is what lets a carrier survive a native round trip.
    ///
    /// The comparison is a DWORD, not a byte: bytes 13..15 must therefore stay nonzero so the
    /// selector can never fall into the 0..255 Job range. The 'HDR'/'D'/'END' tags carry that
    /// invariant — do not zero them.
    /// </summary>
    public static class YanshenHeroDynamicCodec
    {
        private const byte Type7 = 7;
        private const uint NativeType7Magic = 0x0000FAFA;
        private const uint CarrierMagic = 0x37325359; // "YS27"
        private const byte CarrierVersion = 1;
        private const byte PreserveSelector = 0xFF;
        private const int RecordSize = 16;
        private const int ChunkSize = 12;

        public static bool TryExtract(NativeHeroDynamicData source, byte heroSlot,
            out byte[] payload, out string error)
        {
            payload = Array.Empty<byte>();
            if (!ValidateSlot(heroSlot, out error)
                || !TryGetType7(source, out _, out var type7, out error))
                return false;
            if (type7 == null) return true;
            if (!TryParseType7(type7.Payload, out _, out var streams, out error))
                return false;
            if (streams.TryGetValue(heroSlot, out var stream))
                payload = (byte[])stream.Payload.Clone();
            return true;
        }

        public static bool TryMerge(NativeHeroDynamicData source, byte heroSlot,
            byte[] payload, out NativeHeroDynamicData result, out string error)
        {
            result = null;
            payload ??= Array.Empty<byte>();
            if (!ValidateSlot(heroSlot, out error)
                || !TryGetType7(source, out var sections, out var type7, out error))
                return false;

            var records = new List<byte[]>();
            var streams = new Dictionary<byte, CarrierStream>();
            if (type7 != null
                && !TryParseType7(type7.Payload, out records, out streams, out error))
                return false;

            var mergedRecords = new List<byte[]>(records.Count);
            streams.TryGetValue(heroSlot, out var replaced);
            for (var i = 0; i < records.Count; i++)
            {
                if (replaced != null && i >= replaced.StartRecord
                    && i < replaced.StartRecord + replaced.RecordCount)
                    continue;
                mergedRecords.Add((byte[])records[i].Clone());
            }
            if (payload.Length > 0)
            {
                if (!TryBuildCarrier(heroSlot, payload, out var carrier, out error))
                    return false;
                mergedRecords.AddRange(carrier);
            }

            var outputSections = new List<NativeHeroDynamicSection>(sections.Count + 1);
            var replacedType7 = false;
            foreach (var section in sections)
            {
                if (section.Type != Type7)
                {
                    outputSections.Add(new NativeHeroDynamicSection(section.Type, section.Payload));
                    continue;
                }
                replacedType7 = true;
                if (mergedRecords.Count > 0)
                    outputSections.Add(new NativeHeroDynamicSection(Type7,
                        BuildType7Payload(mergedRecords)));
            }
            if (!replacedType7 && mergedRecords.Count > 0)
                outputSections.Add(new NativeHeroDynamicSection(Type7,
                    BuildType7Payload(mergedRecords)));

            result = new NativeHeroDynamicData(outputSections);
            if (!NativeHeroDbFrameCodec.TryEncodeDynamicData(result,
                    out var encoded, out error))
            {
                result = null;
                return false;
            }
            if (encoded.Length > ushort.MaxValue)
            {
                error = "hero dynamic data with eye sidecar exceeds the native SQL Blob limit";
                result = null;
                return false;
            }
            return true;
        }

        private static bool TryGetType7(NativeHeroDynamicData source,
            out List<NativeHeroDynamicSection> sections, out NativeHeroDynamicSection type7,
            out string error)
        {
            sections = new List<NativeHeroDynamicSection>();
            type7 = null;
            source ??= new NativeHeroDynamicData(Array.Empty<NativeHeroDynamicSection>());
            if (!NativeHeroDbFrameCodec.TryEncodeDynamicData(source, out _, out error))
                return false;
            foreach (var section in source.Sections)
            {
                sections.Add(section);
                if (section.Type != Type7) continue;
                if (type7 != null)
                {
                    error = "duplicate native hero type-7 section";
                    return false;
                }
                type7 = section;
            }
            return true;
        }

        private static bool TryParseType7(byte[] payload, out List<byte[]> records,
            out Dictionary<byte, CarrierStream> streams, out string error)
        {
            records = new List<byte[]>();
            streams = new Dictionary<byte, CarrierStream>();
            error = string.Empty;
            if (payload == null || payload.Length < 4
                || BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(0, 4))
                != NativeType7Magic || (payload.Length - 4) % RecordSize != 0)
            {
                error = "invalid native hero type-7 record array";
                return false;
            }
            for (var offset = 4; offset < payload.Length; offset += RecordSize)
                records.Add(payload.AsSpan(offset, RecordSize).ToArray());

            for (var i = 0; i < records.Count; i++)
            {
                var header = records[i];
                if (!IsHeader(header)) continue;
                var version = header[4];
                var slot = header[5];
                var chunkCount = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(6, 2));
                var payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8, 4));
                if (version != CarrierVersion)
                {
                    error = $"unsupported hero eye carrier version {version}";
                    return false;
                }
                if (!ValidateSlot(slot, out error)) return false;
                var expectedChunks = checked((payloadLength + ChunkSize - 1) / ChunkSize);
                if (payloadLength == 0 || expectedChunks != chunkCount
                    || i + chunkCount + 1 >= records.Count)
                {
                    error = $"invalid hero eye carrier length for slot {slot}";
                    return false;
                }
                if (streams.ContainsKey(slot))
                {
                    error = $"duplicate hero eye carrier for slot {slot}";
                    return false;
                }

                var decoded = new byte[payloadLength];
                var copied = 0;
                for (var chunk = 0; chunk < chunkCount; chunk++)
                {
                    var record = records[i + 1 + chunk];
                    if (record[12] != PreserveSelector || record[13] != (byte)'D'
                        || BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(14, 2)) != chunk)
                    {
                        error = $"invalid hero eye carrier chunk {chunk} for slot {slot}";
                        return false;
                    }
                    var length = Math.Min(ChunkSize, decoded.Length - copied);
                    record.AsSpan(0, length).CopyTo(decoded.AsSpan(copied));
                    copied += length;
                }

                var trailer = records[i + 1 + chunkCount];
                if (!IsTrailer(trailer)
                    || BinaryPrimitives.ReadUInt32LittleEndian(trailer.AsSpan(8, 4))
                    != payloadLength
                    || BinaryPrimitives.ReadUInt32LittleEndian(trailer.AsSpan(4, 4))
                    != ComputeCrc32(decoded))
                {
                    error = $"invalid hero eye carrier trailer for slot {slot}";
                    return false;
                }
                var recordCount = chunkCount + 2;
                streams.Add(slot, new CarrierStream(i, recordCount, decoded));
                i += recordCount - 1;
            }
            return true;
        }

        private static bool TryBuildCarrier(byte heroSlot, byte[] payload,
            out List<byte[]> records, out string error)
        {
            records = null;
            error = string.Empty;
            var chunkCount = checked((payload.Length + ChunkSize - 1) / ChunkSize);
            if (chunkCount == 0 || chunkCount > ushort.MaxValue)
            {
                error = "hero eye carrier payload length is invalid";
                return false;
            }
            var projectedType7Length = checked(4 + (chunkCount + 2) * RecordSize);
            if (projectedType7Length > ushort.MaxValue)
            {
                error = "hero eye carrier exceeds one native dynamic section";
                return false;
            }

            records = new List<byte[]>(chunkCount + 2);
            var header = NewCarrierRecord();
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), CarrierMagic);
            header[4] = CarrierVersion;
            header[5] = heroSlot;
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6, 2), (ushort)chunkCount);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8, 4), (uint)payload.Length);
            header[13] = (byte)'H'; header[14] = (byte)'D'; header[15] = (byte)'R';
            records.Add(header);

            for (var chunk = 0; chunk < chunkCount; chunk++)
            {
                var record = NewCarrierRecord();
                var sourceOffset = chunk * ChunkSize;
                var length = Math.Min(ChunkSize, payload.Length - sourceOffset);
                payload.AsSpan(sourceOffset, length).CopyTo(record);
                record[13] = (byte)'D';
                BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(14, 2), (ushort)chunk);
                records.Add(record);
            }

            var trailer = NewCarrierRecord();
            BinaryPrimitives.WriteUInt32LittleEndian(trailer.AsSpan(0, 4), CarrierMagic);
            BinaryPrimitives.WriteUInt32LittleEndian(trailer.AsSpan(4, 4), ComputeCrc32(payload));
            BinaryPrimitives.WriteUInt32LittleEndian(trailer.AsSpan(8, 4), (uint)payload.Length);
            trailer[13] = (byte)'E'; trailer[14] = (byte)'N'; trailer[15] = (byte)'D';
            records.Add(trailer);
            return true;
        }

        private static byte[] BuildType7Payload(List<byte[]> records)
        {
            var payload = new byte[checked(4 + records.Count * RecordSize)];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), NativeType7Magic);
            for (var i = 0; i < records.Count; i++)
                records[i].CopyTo(payload, 4 + i * RecordSize);
            return payload;
        }

        private static byte[] NewCarrierRecord()
        {
            var result = new byte[RecordSize];
            result[12] = PreserveSelector;
            return result;
        }

        private static bool IsHeader(byte[] record) =>
            record[12] == PreserveSelector
            && BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(0, 4)) == CarrierMagic
            && record[13] == (byte)'H' && record[14] == (byte)'D' && record[15] == (byte)'R';

        private static bool IsTrailer(byte[] record) =>
            record[12] == PreserveSelector
            && BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(0, 4)) == CarrierMagic
            && record[13] == (byte)'E' && record[14] == (byte)'N' && record[15] == (byte)'D';

        private static bool ValidateSlot(byte slot, out string error)
        {
            if (slot <= 2)
            {
                error = string.Empty;
                return true;
            }
            error = $"hero eye carrier slot {slot} is outside 0..2";
            return false;
        }

        private static uint ComputeCrc32(ReadOnlySpan<byte> data)
        {
            var crc = uint.MaxValue;
            foreach (var value in data)
            {
                crc ^= value;
                for (var bit = 0; bit < 8; bit++)
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
            return ~crc;
        }

        private sealed class CarrierStream
        {
            public CarrierStream(int startRecord, int recordCount, byte[] payload)
            {
                StartRecord = startRecord;
                RecordCount = recordCount;
                Payload = payload;
            }

            public int StartRecord { get; }
            public int RecordCount { get; }
            public byte[] Payload { get; }
        }
    }
}
