using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SystemModule
{
    /// <summary>
    /// Dual-write overlay for eye-plugin fields that also live in the native 208-byte item
    /// (see <see cref="YanshenNativeItemLayout"/>). Bind remains in the proven native byte
    /// at +0xB8 and is intentionally excluded here. Do not delete this sidecar until every
    /// HumanRcd has been packed into the 208-byte blob at least once — native ScriptData
    /// type 0x79 is skipped on load and dropped on save
    /// (<c>0x6E4510 cmp eax,8 / 0x6E4DD3</c> rebuilds only six sections).
    /// </summary>
    public static class YanshenItemSidecarCodec
    {
        private const uint Magic = 0x37325359; // "YS27" in little-endian byte order.
        private const ushort Version = 1;
        private const byte EquipmentContainer = 0;
        private const byte BagContainer = 1;
        private const byte StorageContainer = 2;
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

        public const int MaximumPayloadLength = ushort.MaxValue;

        public static bool HasExtensionData(TUserItem item)
        {
            return item != null
                   && (item.ys1 != 0 || item.ys2 != 0 || item.ys3 != 0 || item.ys4 != 0
                       || item.ys5 != 0 || item.ys6 != 0 || item.ys7 != 0 || item.ys8 != 0
                       || item.ys9 != 0 || item.ys10 != 0 || item.ys11 != 0 || item.ys12 != 0
                       || item.ys13 != 0 || item.ys14 != 0 || item.ys15 != 0 || item.ys16 != 0
                       || item.ys17 != 0 || item.jp1 != 0 || item.jp2 != 0 || item.jp3 != 0
                       || item.jp4 != 0 || item.jp5 != 0 || item.jp6 != 0
                       || !string.IsNullOrEmpty(item.pname) || !string.IsNullOrEmpty(item.desc1)
                       || !string.IsNullOrEmpty(item.desc2) || !string.IsNullOrEmpty(item.sourceTime)
                       || !string.IsNullOrEmpty(item.killerName) || !string.IsNullOrEmpty(item.mapName));
        }

        public static bool TryEncode(TUserItem[] equipment, TUserItem[] bag,
            TUserItem[] storage, out byte[] payload, out string error)
        {
            payload = Array.Empty<byte>();
            error = string.Empty;
            var entries = new List<Entry>();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            if (!Collect(equipment, EquipmentContainer, "equipment", entries, identities, out error)
                || !Collect(bag, BagContainer, "bag", entries, identities, out error)
                || !Collect(storage, StorageContainer, "storage", entries, identities, out error))
                return false;
            if (entries.Count == 0) return true;
            if (entries.Count > ushort.MaxValue)
            {
                error = "eye sidecar has too many item entries";
                return false;
            }

            try
            {
                using var output = new MemoryStream();
                using var writer = new BinaryWriter(output, Utf8, true);
                writer.Write(Magic);
                writer.Write(Version);
                writer.Write((ushort)entries.Count);
                foreach (var entry in entries)
                {
                    writer.Write(entry.Container);
                    writer.Write(entry.Slot);
                    writer.Write(entry.Item.MakeIndex);
                    writer.Write(entry.Item.wIndex);
                    writer.Write(entry.Item.ys1);
                    WriteYsBytes(writer, entry.Item);
                    WriteJpBytes(writer, entry.Item);
                    if (!TryWriteString(writer, entry.Item.pname, out error)
                        || !TryWriteString(writer, entry.Item.desc1, out error)
                        || !TryWriteString(writer, entry.Item.desc2, out error)
                        || !TryWriteString(writer, entry.Item.sourceTime, out error)
                        || !TryWriteString(writer, entry.Item.killerName, out error)
                        || !TryWriteString(writer, entry.Item.mapName, out error))
                        return false;
                }
                if (output.Length > MaximumPayloadLength)
                {
                    error = $"eye sidecar exceeds {MaximumPayloadLength} bytes";
                    return false;
                }
                payload = output.ToArray();
                return true;
            }
            catch (EncoderFallbackException ex)
            {
                error = "eye sidecar contains invalid text: " + ex.Message;
                return false;
            }
        }

        public static bool TryApply(byte[] payload, TUserItem[] equipment, TUserItem[] bag,
            TUserItem[] storage, out string error)
        {
            return TryApply(payload, equipment, bag, storage, clearUnlisted: true, out error);
        }

        /// <param name="clearUnlisted">
        /// Native load must pass false: fields already unpacked from the 208-byte blob
        /// have to survive when this sidecar has no entry for that item. Passing true
        /// keeps the old "sidecar is the only authority" tests working.
        /// </param>
        public static bool TryApply(byte[] payload, TUserItem[] equipment, TUserItem[] bag,
            TUserItem[] storage, bool clearUnlisted, out string error)
        {
            error = string.Empty;
            if (payload == null || payload.Length == 0)
            {
                if (clearUnlisted) ClearAll(equipment, bag, storage);
                return true;
            }
            if (payload.Length > MaximumPayloadLength)
            {
                error = $"eye sidecar exceeds {MaximumPayloadLength} bytes";
                return false;
            }

            var entries = new List<Entry>();
            var locations = new HashSet<string>(StringComparer.Ordinal);
            var identities = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                using var input = new MemoryStream(payload, false);
                using var reader = new BinaryReader(input, Utf8, true);
                if (input.Length < 8 || reader.ReadUInt32() != Magic)
                {
                    error = "invalid eye sidecar magic";
                    return false;
                }
                var version = reader.ReadUInt16();
                if (version != Version)
                {
                    error = $"unsupported eye sidecar version {version}";
                    return false;
                }
                var count = reader.ReadUInt16();
                for (var i = 0; i < count; i++)
                {
                    if (input.Length - input.Position < 35)
                    {
                        error = $"truncated eye sidecar entry {i}";
                        return false;
                    }
                    var entry = new Entry
                    {
                        Container = reader.ReadByte(),
                        Slot = reader.ReadUInt16(),
                        MakeIndex = reader.ReadInt32(),
                        WIndex = reader.ReadUInt16(),
                        Ys1 = reader.ReadInt32()
                    };
                    if (!IsKnownContainer(entry.Container)
                        || entry.Slot >= GetLength(entry.Container, equipment, bag, storage))
                    {
                        error = $"invalid eye sidecar location {entry.Container}:{entry.Slot}";
                        return false;
                    }
                    if (entry.WIndex == 0)
                    {
                        error = $"eye sidecar entry {i} has an empty item index";
                        return false;
                    }
                    entry.Ys = reader.ReadBytes(16);
                    entry.Jp = reader.ReadBytes(6);
                    if (entry.Ys.Length != 16 || entry.Jp.Length != 6
                        || !TryReadString(reader, input, out entry.PName, out error)
                        || !TryReadString(reader, input, out entry.Desc1, out error)
                        || !TryReadString(reader, input, out entry.Desc2, out error)
                        || !TryReadString(reader, input, out entry.SourceTime, out error)
                        || !TryReadString(reader, input, out entry.KillerName, out error)
                        || !TryReadString(reader, input, out entry.MapName, out error))
                    {
                        if (string.IsNullOrEmpty(error)) error = $"truncated eye sidecar entry {i}";
                        return false;
                    }
                    var location = LocationKey(entry.Container, entry.Slot);
                    var identity = IdentityKey(entry.MakeIndex, entry.WIndex);
                    if (!locations.Add(location))
                    {
                        error = $"duplicate eye sidecar location {entry.Container}:{entry.Slot}";
                        return false;
                    }
                    if (!identities.Add(identity))
                    {
                        error = $"duplicate eye sidecar item identity {entry.MakeIndex}:{entry.WIndex}";
                        return false;
                    }
                    entries.Add(entry);
                }
                if (input.Position != input.Length)
                {
                    error = "eye sidecar has trailing bytes";
                    return false;
                }
            }
            catch (EndOfStreamException)
            {
                error = "truncated eye sidecar";
                return false;
            }
            catch (DecoderFallbackException ex)
            {
                error = "eye sidecar contains invalid UTF-8: " + ex.Message;
                return false;
            }

            var targetLocations = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                var array = GetContainer(entry.Container, equipment, bag, storage);
                var item = array[entry.Slot];
                var targetContainer = entry.Container;
                var targetSlot = (int)entry.Slot;
                if (!Matches(item, entry.MakeIndex, entry.WIndex))
                {
                    item = null;
                    var matches = 0;
                    FindMatches(equipment, EquipmentContainer, entry, ref item,
                        ref targetContainer, ref targetSlot, ref matches);
                    FindMatches(bag, BagContainer, entry, ref item,
                        ref targetContainer, ref targetSlot, ref matches);
                    FindMatches(storage, StorageContainer, entry, ref item,
                        ref targetContainer, ref targetSlot, ref matches);
                    if (matches != 1)
                    {
                        error = $"eye sidecar item {entry.MakeIndex}:{entry.WIndex} matched {matches} records";
                        return false;
                    }
                }
                var targetLocation = LocationKey(targetContainer, targetSlot);
                if (!targetLocations.Add(targetLocation))
                {
                    error = $"multiple eye sidecar entries resolve to {targetContainer}:{targetSlot}";
                    return false;
                }
                entry.Target = item;
            }

            if (clearUnlisted) ClearAll(equipment, bag, storage);
            foreach (var entry in entries)
                Apply(entry.Target, entry, overlay: !clearUnlisted);
            return true;
        }

        public static bool TryRemoveEntry(byte[] payload, int makeIndex,
            ushort wIndex, out byte[] result, out bool removed,
            out string error)
        {
            result = null;
            removed = false;
            error = string.Empty;
            if (payload == null || payload.Length == 0 || wIndex == 0)
            {
                result = payload == null ? Array.Empty<byte>()
                    : (byte[])payload.Clone();
                return true;
            }
            if (payload.Length < 8 || payload.Length > MaximumPayloadLength
                || BinaryPrimitives.ReadUInt32LittleEndian(payload) != Magic
                || BinaryPrimitives.ReadUInt16LittleEndian(
                    payload.AsSpan(4, 2)) != Version)
            {
                error = "invalid eye sidecar header";
                return false;
            }

            var count = BinaryPrimitives.ReadUInt16LittleEndian(
                payload.AsSpan(6, 2));
            var offset = 8;
            var matchStart = -1;
            var matchEnd = -1;
            for (var i = 0; i < count; i++)
            {
                var start = offset;
                if (payload.Length - offset < 35)
                {
                    error = $"truncated eye sidecar entry {i}";
                    return false;
                }
                var entryMakeIndex = BinaryPrimitives.ReadInt32LittleEndian(
                    payload.AsSpan(offset + 3, 4));
                var entryWIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                    payload.AsSpan(offset + 7, 2));
                offset += 35;
                for (var field = 0; field < 6; field++)
                {
                    if (payload.Length - offset < 2)
                    {
                        error = $"truncated eye sidecar entry {i}";
                        return false;
                    }
                    var length = BinaryPrimitives.ReadUInt16LittleEndian(
                        payload.AsSpan(offset, 2));
                    offset += 2;
                    if (payload.Length - offset < length)
                    {
                        error = $"truncated eye sidecar entry {i}";
                        return false;
                    }
                    offset += length;
                }
                if (entryMakeIndex != makeIndex || entryWIndex != wIndex)
                    continue;
                if (matchStart >= 0)
                {
                    error = "duplicate eye sidecar item identity";
                    return false;
                }
                matchStart = start;
                matchEnd = offset;
            }
            if (offset != payload.Length)
            {
                error = "eye sidecar has trailing bytes";
                return false;
            }
            if (matchStart < 0)
            {
                result = (byte[])payload.Clone();
                return true;
            }

            result = new byte[payload.Length - (matchEnd - matchStart)];
            payload.AsSpan(0, matchStart).CopyTo(result);
            payload.AsSpan(matchEnd).CopyTo(result.AsSpan(matchStart));
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(6, 2),
                unchecked((ushort)(count - 1)));
            removed = true;
            return true;
        }

        private static bool Collect(TUserItem[] items, byte container, string name,
            List<Entry> entries, HashSet<string> identities, out string error)
        {
            error = string.Empty;
            if (items == null) return true;
            if (items.Length > ushort.MaxValue)
            {
                error = $"eye sidecar {name} capacity exceeds {ushort.MaxValue}";
                return false;
            }
            for (var i = 0; i < items.Length; i++)
            {
                var item = items[i];
                if (!HasExtensionData(item)) continue;
                if (item.wIndex == 0)
                {
                    error = $"eye sidecar {name}[{i}] has extension data but no item index";
                    return false;
                }
                if (!identities.Add(IdentityKey(item.MakeIndex, item.wIndex)))
                {
                    error = $"duplicate eye sidecar item identity {item.MakeIndex}:{item.wIndex}";
                    return false;
                }
                entries.Add(new Entry { Container = container, Slot = (ushort)i, Item = item });
            }
            return true;
        }

        private static bool TryWriteString(BinaryWriter writer, string value, out string error)
        {
            error = string.Empty;
            var bytes = Utf8.GetBytes(value ?? string.Empty);
            if (bytes.Length > ushort.MaxValue)
            {
                error = "eye sidecar text field exceeds 65535 bytes";
                return false;
            }
            writer.Write((ushort)bytes.Length);
            writer.Write(bytes);
            return true;
        }

        private static bool TryReadString(BinaryReader reader, MemoryStream input,
            out string value, out string error)
        {
            value = string.Empty;
            error = string.Empty;
            if (input.Length - input.Position < 2)
            {
                error = "truncated eye sidecar text length";
                return false;
            }
            var length = reader.ReadUInt16();
            if (input.Length - input.Position < length)
            {
                error = "truncated eye sidecar text";
                return false;
            }
            value = Utf8.GetString(reader.ReadBytes(length));
            return true;
        }

        private static void WriteYsBytes(BinaryWriter writer, TUserItem item)
        {
            writer.Write(item.ys2); writer.Write(item.ys3); writer.Write(item.ys4);
            writer.Write(item.ys5); writer.Write(item.ys6); writer.Write(item.ys7);
            writer.Write(item.ys8); writer.Write(item.ys9); writer.Write(item.ys10);
            writer.Write(item.ys11); writer.Write(item.ys12); writer.Write(item.ys13);
            writer.Write(item.ys14); writer.Write(item.ys15); writer.Write(item.ys16);
            writer.Write(item.ys17);
        }

        private static void WriteJpBytes(BinaryWriter writer, TUserItem item)
        {
            writer.Write(item.jp1); writer.Write(item.jp2); writer.Write(item.jp3);
            writer.Write(item.jp4); writer.Write(item.jp5); writer.Write(item.jp6);
        }

        private static void Apply(TUserItem item, Entry entry, bool overlay)
        {
            var hasYs = entry.Ys1 != 0;
            for (var i = 0; i < entry.Ys.Length && !hasYs; i++)
                if (entry.Ys[i] != 0) hasYs = true;
            var hasJp = false;
            for (var i = 0; i < entry.Jp.Length && !hasJp; i++)
                if (entry.Jp[i] != 0) hasJp = true;
            var hasOrigin = !string.IsNullOrEmpty(entry.PName)
                            || !string.IsNullOrEmpty(entry.Desc1)
                            || !string.IsNullOrEmpty(entry.Desc2)
                            || !string.IsNullOrEmpty(entry.SourceTime)
                            || !string.IsNullOrEmpty(entry.KillerName)
                            || !string.IsNullOrEmpty(entry.MapName);

            // Overlay keeps blob-unpacked jp/ys when the sidecar stored zeros for
            // that group — otherwise a ys-only sidecar would wipe native 极品 in
            // btValue[0..5] (plugin jp slots at item+0x2A..0x2F).
            if (!overlay || hasYs)
            {
                item.ys1 = entry.Ys1;
                item.ys2 = entry.Ys[0]; item.ys3 = entry.Ys[1]; item.ys4 = entry.Ys[2];
                item.ys5 = entry.Ys[3]; item.ys6 = entry.Ys[4]; item.ys7 = entry.Ys[5];
                item.ys8 = entry.Ys[6]; item.ys9 = entry.Ys[7]; item.ys10 = entry.Ys[8];
                item.ys11 = entry.Ys[9]; item.ys12 = entry.Ys[10]; item.ys13 = entry.Ys[11];
                item.ys14 = entry.Ys[12]; item.ys15 = entry.Ys[13]; item.ys16 = entry.Ys[14];
                item.ys17 = entry.Ys[15];
            }
            if (!overlay || hasJp)
            {
                item.jp1 = entry.Jp[0]; item.jp2 = entry.Jp[1]; item.jp3 = entry.Jp[2];
                item.jp4 = entry.Jp[3]; item.jp5 = entry.Jp[4]; item.jp6 = entry.Jp[5];
            }
            if (!overlay || hasOrigin)
            {
                item.pname = entry.PName;
                item.desc1 = entry.Desc1;
                item.desc2 = entry.Desc2;
                item.sourceTime = entry.SourceTime;
                item.killerName = entry.KillerName;
                item.mapName = entry.MapName;
            }
        }

        private static void ClearAll(params TUserItem[][] containers)
        {
            foreach (var container in containers)
            {
                if (container == null) continue;
                foreach (var item in container)
                    if (item != null) Clear(item);
            }
        }

        private static void Clear(TUserItem item)
        {
            item.ys1 = 0;
            item.ys2 = item.ys3 = item.ys4 = item.ys5 = item.ys6 = item.ys7 = 0;
            item.ys8 = item.ys9 = item.ys10 = item.ys11 = item.ys12 = item.ys13 = 0;
            item.ys14 = item.ys15 = item.ys16 = item.ys17 = 0;
            item.jp1 = item.jp2 = item.jp3 = item.jp4 = item.jp5 = item.jp6 = 0;
            item.pname = item.desc1 = item.desc2 = string.Empty;
            item.sourceTime = item.killerName = item.mapName = string.Empty;
        }

        private static bool Matches(TUserItem item, int makeIndex, ushort wIndex) =>
            item != null && item.MakeIndex == makeIndex && item.wIndex == wIndex;

        private static void FindMatches(TUserItem[] items, byte container, Entry entry,
            ref TUserItem match, ref byte matchContainer, ref int matchSlot, ref int count)
        {
            if (items == null) return;
            for (var i = 0; i < items.Length; i++)
            {
                if (!Matches(items[i], entry.MakeIndex, entry.WIndex)) continue;
                match = items[i];
                matchContainer = container;
                matchSlot = i;
                count++;
            }
        }

        private static TUserItem[] GetContainer(byte container, TUserItem[] equipment,
            TUserItem[] bag, TUserItem[] storage)
        {
            return container == EquipmentContainer ? equipment
                : container == BagContainer ? bag : storage;
        }

        private static int GetLength(byte container, TUserItem[] equipment,
            TUserItem[] bag, TUserItem[] storage)
        {
            var values = GetContainer(container, equipment, bag, storage);
            return values?.Length ?? 0;
        }

        private static bool IsKnownContainer(byte container) =>
            container == EquipmentContainer || container == BagContainer
            || container == StorageContainer;

        private static string IdentityKey(int makeIndex, ushort wIndex) =>
            makeIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":"
            + wIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);

        private static string LocationKey(byte container, int slot) =>
            container.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":"
            + slot.ToString(System.Globalization.CultureInfo.InvariantCulture);

        private sealed class Entry
        {
            public byte Container;
            public ushort Slot;
            public TUserItem Item;
            public int MakeIndex;
            public ushort WIndex;
            public int Ys1;
            public byte[] Ys;
            public byte[] Jp;
            public string PName;
            public string Desc1;
            public string Desc2;
            public string SourceTime;
            public string KillerName;
            public string MapName;
            public TUserItem Target;
        }
    }
}
