using System.Buffers.Binary;
using SystemModule;

namespace GameSvr
{
    internal static class LegacyUserItem208Codec
    {
        internal const int RecordSize = 208;
        internal const int HexLength = RecordSize * 2;
        internal const int CoreSize = 24;
        internal const int UpgradeFlagsOffset = 0x27;
        internal const int BindOffset = 0xB8;
        internal const byte KnownUpgradeFlags = 0xC0;

        internal static bool TryEncode(TUserItem item, out string weaponData, out string error)
        {
            weaponData = string.Empty;
            error = string.Empty;
            if (item == null || item.btValue == null || item.btValue.Length != 14)
            {
                error = "invalid core item record";
                return false;
            }
            if (HasUnmappedExtensionData(item))
            {
                error = "item contains unmapped extended attributes";
                return false;
            }
            if ((item.UpgradeFlags & ~KnownUpgradeFlags) != 0)
            {
                error = "item contains unknown native refine flags";
                return false;
            }

            byte[] record;
            if (item.NativeRecord == null)
            {
                record = new byte[RecordSize];
            }
            else
            {
                if (item.NativeRecord.Length != RecordSize)
                {
                    error = $"native item record must be {RecordSize} bytes";
                    return false;
                }
                record = (byte[])item.NativeRecord.Clone();
                if (!TryValidateNativeTail(record, out error))
                {
                    return false;
                }
            }

            BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(0, 4), item.MakeIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(4, 2), item.wIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(6, 2), item.Dura);
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(8, 2), item.DuraMax);
            item.btValue.CopyTo(record, 10);
            record[UpgradeFlagsOffset] = item.UpgradeFlags;
            record[BindOffset] = item.Bind;
            weaponData = Convert.ToHexString(record);
            return true;
        }

        internal static bool TryDecode(string weaponData, out TUserItem item, out string error)
        {
            item = null;
            error = string.Empty;
            if (!IsNativeHex(weaponData))
            {
                error = $"WeaponData must be {HexLength} uppercase hex characters";
                return false;
            }

            var record = Convert.FromHexString(weaponData);
            if (!TryValidateNativeTail(record, out error))
            {
                return false;
            }

            item = new TUserItem
            {
                MakeIndex = BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(0, 4)),
                wIndex = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(4, 2)),
                Dura = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(6, 2)),
                DuraMax = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(8, 2)),
                UpgradeFlags = record[UpgradeFlagsOffset],
                Bind = record[BindOffset],
                NativeRecord = (byte[])record.Clone()
            };
            record.AsSpan(10, 14).CopyTo(item.btValue);
            return true;
        }

        private static bool IsNativeHex(string value)
        {
            if (value == null || value.Length != HexLength) return false;
            foreach (var ch in value)
            {
                if (!((ch >= '0' && ch <= '9') || (ch >= 'A' && ch <= 'F'))) return false;
            }
            return true;
        }

        private static bool TryValidateNativeTail(byte[] record, out string error)
        {
            error = string.Empty;
            for (var i = CoreSize; i < record.Length; i++)
            {
                if (i == UpgradeFlagsOffset)
                {
                    if ((record[i] & ~KnownUpgradeFlags) != 0)
                    {
                        error = $"unknown native refine flags: 0x{record[i]:X2}";
                        return false;
                    }
                    continue;
                }
                if (i == BindOffset) continue;
                if (record[i] != 0)
                {
                    error = $"unmapped native item data at offset 0x{i:X2}";
                    return false;
                }
            }
            return true;
        }

        private static bool HasUnmappedExtensionData(TUserItem item)
        {
            return item.ys1 != 0 || item.ys2 != 0 || item.ys3 != 0 || item.ys4 != 0 ||
                   item.ys5 != 0 || item.ys6 != 0 || item.ys7 != 0 || item.ys8 != 0 ||
                   item.ys9 != 0 || item.ys10 != 0 || item.ys11 != 0 || item.ys12 != 0 ||
                   item.ys13 != 0 || item.ys14 != 0 || item.ys15 != 0 || item.ys16 != 0 ||
                   item.ys17 != 0 || item.jp1 != 0 || item.jp2 != 0 || item.jp3 != 0 ||
                   item.jp4 != 0 || item.jp5 != 0 || item.jp6 != 0 ||
                   !string.IsNullOrEmpty(item.pname) || !string.IsNullOrEmpty(item.desc1) ||
                   !string.IsNullOrEmpty(item.desc2) || !string.IsNullOrEmpty(item.sourceTime) ||
                   !string.IsNullOrEmpty(item.killerName) || !string.IsNullOrEmpty(item.mapName);
        }
    }
}
