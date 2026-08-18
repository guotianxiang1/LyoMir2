using System.Buffers.Binary;
using SystemModule;

namespace GameSvr.Services
{
    internal static class NativeMailAttachmentCodec
    {
        internal const int RecordSize = 208;
        private const int UpgradeFlagsOffset = 0x27;
        private const int BindOffset = 0xB8;
        private const byte KnownUpgradeFlags = 0xC0;

        internal static byte[] NormalizeRecord(byte[] data)
        {
            var record = new byte[RecordSize];
            if (data != null)
                data.AsSpan(0, Math.Min(data.Length, RecordSize)).CopyTo(record);
            return record;
        }

        internal static bool TryDecode(byte[] record, out TUserItem item, out string error)
        {
            item = null;
            error = string.Empty;
            if (record == null || record.Length != RecordSize)
            {
                error = $"native mail attachment must be {RecordSize} bytes";
                return false;
            }

            var itemIndex = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(4, 2));
            if (itemIndex == 0)
            {
                error = "native mail attachment has no item index";
                return false;
            }

            item = new TUserItem
            {
                MakeIndex = BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(0, 4)),
                wIndex = itemIndex,
                Dura = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(6, 2)),
                DuraMax = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(8, 2)),
                UpgradeFlags = record[UpgradeFlagsOffset],
                Bind = record[BindOffset],
                NativeRecord = (byte[])record.Clone()
            };
            record.AsSpan(10, 14).CopyTo(item.btValue);
            NativeSpecialDropItemRollCore.HydrateConstructorState(item);
            return true;
        }

        internal static bool TryEncode(TUserItem item, out byte[] record, out string error)
        {
            record = null;
            error = string.Empty;
            if (item == null || item.wIndex == 0 || item.btValue == null || item.btValue.Length != 14)
            {
                error = "invalid native mail attachment item";
                return false;
            }
            if (item.NativeRecord != null && item.NativeRecord.Length != RecordSize)
            {
                error = $"native mail attachment must be {RecordSize} bytes";
                return false;
            }

            record = item.NativeRecord == null
                ? new byte[RecordSize]
                : (byte[])item.NativeRecord.Clone();
            var originalUnknownFlags = record[UpgradeFlagsOffset] & ~KnownUpgradeFlags;
            if ((item.UpgradeFlags & ~KnownUpgradeFlags) != originalUnknownFlags)
            {
                record = null;
                error = "unknown native mail attachment refine flags changed";
                return false;
            }

            BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(0, 4), item.MakeIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(4, 2), item.wIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(6, 2), item.Dura);
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(8, 2), item.DuraMax);
            item.btValue.AsSpan().CopyTo(record.AsSpan(10, 14));
            record[UpgradeFlagsOffset] = item.UpgradeFlags;
            record[BindOffset] = item.Bind;
            return true;
        }
    }
}
