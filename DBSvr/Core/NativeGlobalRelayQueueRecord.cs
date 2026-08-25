using System;
using System.Buffers.Binary;

namespace DBSvr.Core
{
    /// <summary>
    /// The exact 0x41-byte record queued under node command 0x2750 by the
    /// native type1 0x0173 handler.
    ///
    /// This is deliberately a record codec only.  The native queue node keeps
    /// the 0x2750 command and the preceding 0x33AABB77/0x1F43 frame header
    /// outside this byte slice; neither belongs in <see cref="ToBytes"/>.
    /// Fields whose business meaning is not proven retain neutral names and
    /// the eight untouched bytes at +0x04..+0x0B remain opaque.
    /// </summary>
    public sealed class NativeGlobalRelayQueueRecord
    {
        public const int Size = 0x41;
        public const int ResultOffset = 0x00;
        public const int OpaqueOffset = 0x04;
        public const int OpaqueLength = 0x08;
        public const int ContextWord0Offset = 0x0C;
        public const int ContextWord1Offset = 0x0E;
        public const int SelectorOffset = 0x10;
        public const int TagOffset = 0x12;
        public const int ResponseValue70Offset = 0x14;
        public const int ResponseValue74Offset = 0x18;
        public const int CharacterNameOffset = 0x1C;
        public const int CharacterNameCapacity = 0x0F;
        public const int AccountOffset = 0x2C;
        public const int AccountCapacity = 0x14;

        private readonly byte[] _raw;

        private NativeGlobalRelayQueueRecord(byte[] raw)
        {
            _raw = raw;
            ResultCode = BinaryPrimitives.ReadInt32LittleEndian(
                raw.AsSpan(ResultOffset, 4));
            Opaque = Copy(raw, OpaqueOffset, OpaqueLength);
            ContextWord0 = ReadUInt16(raw, ContextWord0Offset);
            ContextWord1 = ReadUInt16(raw, ContextWord1Offset);
            Selector = ReadUInt16(raw, SelectorOffset);
            Tag = ReadUInt16(raw, TagOffset);
            ResponseValue70 = ReadInt32(raw, ResponseValue70Offset);
            ResponseValue74 = ReadInt32(raw, ResponseValue74Offset);
            CharacterName = ReadShortString(raw, CharacterNameOffset,
                CharacterNameCapacity);
            Account = ReadShortString(raw, AccountOffset, AccountCapacity);
        }

        public int ResultCode { get; set; }
        public byte[] Opaque { get; set; }
        public ushort ContextWord0 { get; set; }
        public ushort ContextWord1 { get; set; }
        public ushort Selector { get; set; }
        public ushort Tag { get; set; }
        public int ResponseValue70 { get; set; }
        public int ResponseValue74 { get; set; }
        public byte[] CharacterName { get; set; }
        public byte[] Account { get; set; }

        public static bool TryDecode(byte[] source,
            out NativeGlobalRelayQueueRecord record, out string error)
        {
            record = null;
            error = string.Empty;
            if (source == null || source.Length != Size)
            {
                error = $"native 0x2750 queue record must be exactly {Size} bytes";
                return false;
            }
            if (!TryValidateShortString(source, CharacterNameOffset,
                    CharacterNameCapacity, out error)
                || !TryValidateShortString(source, AccountOffset,
                    AccountCapacity, out error))
                return false;
            record = new NativeGlobalRelayQueueRecord((byte[])source.Clone());
            return true;
        }

        public static bool TryEncode(NativeGlobalRelayQueueRecord record,
            out byte[] bytes, out string error)
        {
            bytes = Array.Empty<byte>();
            error = string.Empty;
            if (record == null)
            {
                error = "native 0x2750 queue record is null";
                return false;
            }
            if (!TryValidateBytes(record.Opaque, OpaqueLength,
                    "opaque bytes", out error)
                || !TryValidateBytes(record.CharacterName,
                    CharacterNameCapacity, "character name", out error)
                || !TryValidateBytes(record.Account, AccountCapacity,
                    "account", out error))
                return false;

            bytes = (byte[])record._raw.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(ResultOffset, 4), record.ResultCode);
            record.Opaque.AsSpan().CopyTo(bytes.AsSpan(
                OpaqueOffset, OpaqueLength));
            WriteUInt16(bytes, ContextWord0Offset, record.ContextWord0);
            WriteUInt16(bytes, ContextWord1Offset, record.ContextWord1);
            WriteUInt16(bytes, SelectorOffset, record.Selector);
            WriteUInt16(bytes, TagOffset, record.Tag);
            WriteInt32(bytes, ResponseValue70Offset, record.ResponseValue70);
            WriteInt32(bytes, ResponseValue74Offset, record.ResponseValue74);
            WriteShortString(bytes, CharacterNameOffset, record.CharacterName);
            WriteShortString(bytes, AccountOffset, record.Account);
            return true;
        }

        public byte[] ToBytes()
        {
            if (!TryEncode(this, out var bytes, out var error))
                throw new InvalidOperationException(error);
            return bytes;
        }

        private static bool TryValidateShortString(byte[] source, int offset,
            int capacity, out string error)
        {
            error = string.Empty;
            var length = source[offset];
            if (length > capacity)
            {
                error = $"native 0x2750 {offset:X2} ShortString exceeds {capacity}";
                return false;
            }
            if (offset + 1 + length > Size)
            {
                error = $"native 0x2750 {offset:X2} ShortString is truncated";
                return false;
            }
            return true;
        }

        private static bool TryValidateBytes(byte[] value, int capacity,
            string name, out string error)
        {
            error = string.Empty;
            if (value == null || value.Length > capacity)
            {
                error = $"native 0x2750 {name} exceeds {capacity} bytes";
                return false;
            }
            return true;
        }

        private static byte[] ReadShortString(byte[] source, int offset,
            int capacity) => source.AsSpan(offset + 1, source[offset])
                .ToArray();

        private static void WriteShortString(byte[] destination, int offset,
            byte[] value)
        {
            destination[offset] = (byte)value.Length;
            value.AsSpan().CopyTo(destination.AsSpan(offset + 1, value.Length));
        }

        private static byte[] Copy(byte[] source, int offset, int length) =>
            source.AsSpan(offset, length).ToArray();

        private static ushort ReadUInt16(byte[] source, int offset) =>
            BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(offset, 2));

        private static int ReadInt32(byte[] source, int offset) =>
            BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(offset, 4));

        private static void WriteUInt16(byte[] destination, int offset,
            ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(
                destination.AsSpan(offset, 2), value);

        private static void WriteInt32(byte[] destination, int offset,
            int value) => BinaryPrimitives.WriteInt32LittleEndian(
                destination.AsSpan(offset, 4), value);
    }
}
