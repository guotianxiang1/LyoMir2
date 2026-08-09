using System;
using System.Buffers.Binary;
using SystemModule;

namespace DBSvr.Core
{
    /// <summary>
    /// Extracts the legacy item containers embedded in native ScriptData and
    /// hero DynamicData without rebuilding their opaque section streams.
    /// </summary>
    public static class NativeDynamicItemExtractionCodec
    {
        private const uint SectionMagic = 0xABCDEFAA;
        private const byte FixedContainerType = 4;
        private const byte LegacyArrayType = 0x0C;
        private const byte HumanSidecarType = 0x79;
        private const int SectionHeaderSize = 7;
        private const int ItemSize = 0xD0;
        private const int CompactContainerSize = 0x4E1;
        private const int WideContainerSize = 0x524;

        public static bool TryExtractHuman(byte[] source, int makeIndex,
            out byte[] updated, out byte[] itemRecord)
        {
            updated = null;
            itemRecord = null;
            if (source == null || source.Length < 4) return false;

            var working = (byte[])source.Clone();
            if (TryFindSection(working, LegacyArrayType,
                    out var payloadOffset, out var payloadLength)
                && TryExtractLegacyArray(working, payloadOffset,
                    payloadLength, makeIndex, out itemRecord)
                || TryFindSection(working, FixedContainerType,
                    out payloadOffset, out payloadLength)
                && TryExtractHumanFixedContainer(working, payloadOffset,
                    payloadLength, makeIndex, out itemRecord))
            {
                updated = working;
                return true;
            }
            return false;
        }

        public static bool TryExtractHero(byte[] source, byte selector,
            int makeIndex, out byte[] updated, out byte[] itemRecord)
        {
            updated = null;
            itemRecord = null;
            if (source == null || source.Length < 4) return false;

            var working = (byte[])source.Clone();
            if (TryFindSection(working, FixedContainerType,
                    out var payloadOffset, out var payloadLength)
                && TryExtractHeroFixedContainers(working, payloadOffset,
                    payloadLength, selector, makeIndex, out itemRecord)
                || TryFindSection(working, LegacyArrayType,
                    out payloadOffset, out payloadLength)
                && TryExtractLegacyArray(working, payloadOffset,
                    payloadLength, makeIndex, out itemRecord))
            {
                updated = working;
                return true;
            }
            return false;
        }

        public static bool TryRemoveHumanSidecar(byte[] source, int makeIndex,
            ushort wIndex, out byte[] updated)
        {
            updated = null;
            if (source == null || source.Length < 4) return false;
            if (!TryFindSection(source, HumanSidecarType,
                    out var payloadOffset, out var payloadLength))
            {
                updated = (byte[])source.Clone();
                return true;
            }
            if (!YanshenItemSidecarCodec.TryRemoveEntry(
                    source.AsSpan(payloadOffset, payloadLength).ToArray(),
                    makeIndex, wIndex, out var payload, out var removed, out _))
                return false;
            if (!removed)
            {
                updated = (byte[])source.Clone();
                return true;
            }

            var removedLength = payloadLength - payload.Length;
            var sectionOffset = payloadOffset - SectionHeaderSize;
            updated = new byte[source.Length - removedLength];
            source.AsSpan(0, payloadOffset).CopyTo(updated);
            payload.CopyTo(updated, payloadOffset);
            source.AsSpan(payloadOffset + payloadLength).CopyTo(
                updated.AsSpan(payloadOffset + payload.Length));
            BinaryPrimitives.WriteUInt16LittleEndian(
                updated.AsSpan(sectionOffset + 4, 2),
                checked((ushort)payload.Length));
            var declaredLength = BinaryPrimitives.ReadInt32LittleEndian(source);
            BinaryPrimitives.WriteInt32LittleEndian(updated,
                checked(declaredLength - removedLength));
            return true;
        }

        private static bool TryFindSection(byte[] source, byte requestedType,
            out int payloadOffset, out int payloadLength)
        {
            payloadOffset = 0;
            payloadLength = 0;
            if (source.Length < 4) return false;

            var remaining = BinaryPrimitives.ReadInt32LittleEndian(source);
            if (remaining < 8 || remaining > source.Length - 4) return false;
            var offset = 4;
            while (remaining > SectionHeaderSize)
            {
                if (BinaryPrimitives.ReadUInt32LittleEndian(
                        source.AsSpan(offset, 4)) != SectionMagic)
                {
                    offset++;
                    remaining--;
                    continue;
                }

                var length = BinaryPrimitives.ReadUInt16LittleEndian(
                    source.AsSpan(offset + 4, 2));
                var sectionSize = SectionHeaderSize + length;
                if (sectionSize > remaining
                    || offset + sectionSize > source.Length)
                    return false;
                if (source[offset + 6] == requestedType)
                {
                    payloadOffset = offset + SectionHeaderSize;
                    payloadLength = length;
                    return true;
                }
                offset += sectionSize;
                remaining -= sectionSize;
            }
            return false;
        }

        private static bool TryExtractHumanFixedContainer(byte[] data,
            int payloadOffset, int payloadLength, int makeIndex,
            out byte[] itemRecord)
        {
            itemRecord = null;
            if (payloadLength != CompactContainerSize
                && payloadLength != WideContainerSize
                || data[payloadOffset] == 0)
                return false;

            var payloadEnd = payloadOffset + payloadLength;
            for (var i = 0; i < 6; i++)
            {
                var offset = payloadOffset + 1 + i * ItemSize;
                if (offset + ItemSize > payloadEnd) return false;
                if (BinaryPrimitives.ReadInt32LittleEndian(
                        data.AsSpan(offset, 4)) != makeIndex)
                    continue;
                itemRecord = data.AsSpan(offset, ItemSize).ToArray();
                data.AsSpan(offset, ItemSize).Clear();
                return true;
            }
            return false;
        }

        private static bool TryExtractHeroFixedContainers(byte[] data,
            int payloadOffset, int payloadLength, byte selector, int makeIndex,
            out byte[] itemRecord)
        {
            itemRecord = null;
            if (payloadLength != 3 * WideContainerSize
                && payloadLength != 3 * CompactContainerSize)
                return false;

            var payloadEnd = payloadOffset + payloadLength;
            for (var group = 0; group < 3; group++)
            {
                if (selector != 0 && group + 1 == selector) continue;
                var groupOffset = payloadOffset + group * WideContainerSize;
                if (groupOffset >= payloadEnd) continue;
                if (data[groupOffset] == 0) continue;
                for (var i = 0; i < 6; i++)
                {
                    var offset = groupOffset + 1 + i * ItemSize;
                    // The native compact-size branch still uses the wide stride.
                    // Bound it here instead of reproducing its out-of-range access.
                    if (offset + ItemSize > payloadEnd) break;
                    if (BinaryPrimitives.ReadInt32LittleEndian(
                            data.AsSpan(offset, 4)) != makeIndex)
                        continue;
                    itemRecord = data.AsSpan(offset, ItemSize).ToArray();
                    data.AsSpan(offset, ItemSize).Clear();
                    return true;
                }
            }
            return false;
        }

        private static bool TryExtractLegacyArray(byte[] data,
            int payloadOffset, int payloadLength, int makeIndex,
            out byte[] itemRecord)
        {
            itemRecord = null;
            if (payloadLength < 12) return false;
            var payload = data.AsSpan(payloadOffset, payloadLength);
            var headerSize = BinaryPrimitives.ReadInt32LittleEndian(
                payload.Slice(4, 4));
            var elementSize = BinaryPrimitives.ReadInt32LittleEndian(
                payload.Slice(8, 4));
            if (headerSize < 0 || headerSize > payloadLength
                || elementSize < 0)
                return false;

            var count = payload[0] + payload[1] + payload[2] + payload[3];
            if ((long)payloadLength - headerSize
                != (long)count * elementSize)
                return false;

            var offset = payloadOffset + headerSize;
            var payloadEnd = payloadOffset + payloadLength;
            for (var group = 0; group < 4; group++)
            {
                for (var i = 0; i < payload[group]; i++)
                {
                    if ((long)offset + elementSize > payloadEnd) return false;
                    var sourceItem = new byte[ItemSize];
                    var copied = Math.Min(elementSize, ItemSize);
                    if (copied > 0)
                        data.AsSpan(offset, copied).CopyTo(sourceItem);
                    if (BinaryPrimitives.ReadInt32LittleEndian(sourceItem)
                        == makeIndex)
                    {
                        itemRecord = ConvertLegacyItem(sourceItem);
                        if (copied > 0) data.AsSpan(offset, copied).Clear();
                        return true;
                    }
                    offset += elementSize;
                }
            }
            return false;
        }

        private static byte[] ConvertLegacyItem(byte[] source)
        {
            var result = new byte[ItemSize];
            Copy(source, 0x00, result, 0x00, 4);
            Copy(source, 0x04, result, 0x04, 2);
            Copy(source, 0x06, result, 0x06, 2);
            Copy(source, 0x08, result, 0x08, 2);
            Copy(source, 0x0A, result, 0x14, 2);
            Copy(source, 0x0C, result, 0x1B, 1);
            Copy(source, 0x10, result, 0x1C, 1);
            Copy(source, 0x11, result, 0x1D, 0x28);
            Copy(source, 0xA1, result, 0x45, 3);
            Copy(source, 0x39, result, 0x48, 1);
            Copy(source, 0x3B, result, 0x49, 1);
            Copy(source, 0x3D, result, 0x4A, 4);
            Copy(source, 0x45, result, 0x4E, 4);
            Copy(source, 0x49, result, 0x52, 0x14);
            Copy(source, 0x5D, result, 0x66, 4);
            Copy(source, 0x61, result, 0x6A, 4);
            Copy(source, 0x65, result, 0x6E, 4);
            return result;
        }

        private static void Copy(byte[] source, int sourceOffset,
            byte[] destination, int destinationOffset, int length) =>
            source.AsSpan(sourceOffset, length).CopyTo(
                destination.AsSpan(destinationOffset, length));
    }
}
