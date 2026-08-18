using System.Buffers.Binary;
using SystemModule;

namespace GameSvr
{
    internal static class NativeMerchantGoodsCodec
    {
        internal const int RecordSize = 208;
        private const int UpgradeFlagsOffset = 0x27;
        private const int BindOffset = 0xB8;

        internal static TUserItem Decode(ReadOnlySpan<byte> source)
        {
            if (source.Length != RecordSize)
                throw new ArgumentException($"Native merchant item record must be {RecordSize} bytes.",
                    nameof(source));

            var record = source.ToArray();
            var item = new TUserItem
            {
                MakeIndex = BinaryPrimitives.ReadInt32LittleEndian(source),
                wIndex = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(4, 2)),
                Dura = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(6, 2)),
                DuraMax = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(8, 2)),
                UpgradeFlags = source[UpgradeFlagsOffset],
                Bind = source[BindOffset],
                NativeRecord = record
            };
            source.Slice(10, 14).CopyTo(item.btValue);
            NativeSpecialDropItemRollCore.HydrateConstructorState(item);
            return item;
        }

        internal static byte[] Encode(TUserItem item)
        {
            if (!TryEncode(item, out var record, out var error))
                throw new InvalidDataException(error);
            return record;
        }

        internal static bool TryEncode(TUserItem item, out byte[] record,
            out string error)
        {
            record = Array.Empty<byte>();
            error = string.Empty;
            if (item == null)
            {
                error = "Native merchant item is null.";
                return false;
            }
            if (item.btValue == null || item.btValue.Length != 14)
            {
                error = "Native merchant item has an invalid core record.";
                return false;
            }
            if (item.NativeRecord != null && item.NativeRecord.Length != RecordSize)
            {
                error = $"Native merchant item record must be {RecordSize} bytes.";
                return false;
            }
            if (HasUnmappedExtensionData(item))
            {
                error = "Native merchant item has unmapped extended attributes.";
                return false;
            }

            record = item.NativeRecord == null
                ? new byte[RecordSize]
                : (byte[])item.NativeRecord.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(record, item.MakeIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(4, 2), item.wIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(6, 2), item.Dura);
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(8, 2), item.DuraMax);
            item.btValue.CopyTo(record, 10);
            record[UpgradeFlagsOffset] = item.UpgradeFlags;
            record[BindOffset] = item.Bind;
            return true;
        }

        private static bool HasUnmappedExtensionData(TUserItem item)
        {
            return item.ys1 != 0 || item.ys2 != 0 || item.ys3 != 0 ||
                   item.ys4 != 0 || item.ys5 != 0 || item.ys6 != 0 ||
                   item.ys7 != 0 || item.ys8 != 0 || item.ys9 != 0 ||
                   item.ys10 != 0 || item.ys11 != 0 || item.ys12 != 0 ||
                   item.ys13 != 0 || item.ys14 != 0 || item.ys15 != 0 ||
                   item.ys16 != 0 || item.ys17 != 0 || item.jp1 != 0 ||
                   item.jp2 != 0 || item.jp3 != 0 || item.jp4 != 0 ||
                   item.jp5 != 0 || item.jp6 != 0 ||
                   !string.IsNullOrEmpty(item.pname) ||
                   !string.IsNullOrEmpty(item.desc1) ||
                   !string.IsNullOrEmpty(item.desc2) ||
                   !string.IsNullOrEmpty(item.sourceTime) ||
                   !string.IsNullOrEmpty(item.killerName) ||
                   !string.IsNullOrEmpty(item.mapName);
        }
    }
}
