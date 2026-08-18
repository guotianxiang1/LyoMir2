using System.Buffers.Binary;
using System.Text;

namespace GameSvr
{
    internal static class NativeSlaveInfoCodec
    {
        internal const int RecordSize = 0x20;
        private const int MaximumNameBytes = 15;
        private static readonly Encoding Gbk;

        static NativeSlaveInfoCodec()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Gbk = Encoding.GetEncoding(936,
                EncoderFallback.ExceptionFallback, DecoderFallback.ReplacementFallback);
        }

        internal static bool TryDecode(ReadOnlySpan<byte> record,
            out TSlaveInfo slaveInfo, out string error)
        {
            slaveInfo = null;
            error = string.Empty;
            if (record.Length != RecordSize)
            {
                error = $"native slave record length must be {RecordSize}";
                return false;
            }

            var nameLength = record[0];
            if (nameLength == 0)
                return true;
            if (nameLength > MaximumNameBytes)
            {
                error = "native slave name exceeds 15 bytes";
                return false;
            }

            // Native ShortString truncation is byte-based and may leave a GBK
            // lead byte at position 15. Delphi keeps that record readable, so
            // a malformed tail must not reject the complete switch payload.
            var name = Gbk.GetString(record.Slice(1, nameLength));

            slaveInfo = new TSlaveInfo
            {
                sSlaveName = name,
                nKillCount = BinaryPrimitives.ReadInt32LittleEndian(
                    record.Slice(0x10, 4)),
                dwRoyaltySec = BinaryPrimitives.ReadInt32LittleEndian(
                    record.Slice(0x14, 4)),
                nHP = BinaryPrimitives.ReadUInt16LittleEndian(
                    record.Slice(0x18, 2)),
                nMP = BinaryPrimitives.ReadUInt16LittleEndian(
                    record.Slice(0x1A, 2)),
                btSlaveExpLevel = record[0x1C],
                btSlaveLevel = record[0x1D]
            };
            return true;
        }

        internal static bool TryEncode(Span<byte> destination,
            TBaseObject slave, int currentTick, out string error)
        {
            error = string.Empty;
            if (destination.Length != RecordSize)
            {
                error = $"native slave record length must be {RecordSize}";
                return false;
            }
            if (slave == null)
            {
                error = "native slave is null";
                return false;
            }
            if (!TryWriteName(destination, slave.m_sCharName, out error))
                return false;

            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(0x10, 4),
                slave.m_nKillMonCount);
            var remainingMilliseconds = unchecked((uint)(
                slave.m_dwMasterRoyaltyTick - currentTick));
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(0x14, 4),
                remainingMilliseconds / 1000u);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(0x18, 2),
                unchecked((ushort)slave.m_WAbil.HP));
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(0x1A, 2),
                unchecked((ushort)slave.m_WAbil.MP));
            destination[0x1C] = slave.m_btSlaveExpLevel;
            destination[0x1D] = slave.m_btSlaveMakeLevel;
            return true;
        }

        private static bool TryWriteName(Span<byte> destination,
            string value, out string error)
        {
            error = string.Empty;
            byte[] bytes;
            try
            {
                bytes = Gbk.GetBytes(value ?? string.Empty);
            }
            catch (EncoderFallbackException ex)
            {
                error = "native slave name is not GBK: " + ex.Message;
                return false;
            }

            destination.Clear();
            var length = Math.Min(bytes.Length, MaximumNameBytes);
            destination[0] = (byte)length;
            bytes.AsSpan(0, length).CopyTo(destination.Slice(1, length));
            return true;
        }
    }
}
