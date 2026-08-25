using System;
using System.Buffers.Binary;

namespace DBSvr.Core
{
    /// <summary>
    /// The exact 0x20-byte record queued under node command 0x274D by the
    /// native type1 0x0156 handler when the global socket is down.
    ///
    /// The queue node and its command word are outside this slice. The native
    /// drain reads only result+0, requester id+4, and the ShortString at +8;
    /// all other bytes are retained as opaque data so a decoded record can be
    /// round-tripped without inventing stack-buffer semantics.
    /// </summary>
    public sealed class NativeGlobalRelayRegistrationQueueRecord
    {
        public const int Size = 0x20;
        public const int ResultOffset = 0x00;
        public const int RequestingServerIdOffset = 0x04;
        public const int OpaquePrefixOffset = 0x05;
        public const int OpaquePrefixLength = 0x03;
        public const int NameOffset = 0x08;
        public const int NameCapacity = 0x14;
        public const int OpaqueSuffixOffset = 0x1D;
        public const int OpaqueSuffixLength = 0x03;

        private readonly byte[] _raw;

        private NativeGlobalRelayRegistrationQueueRecord(byte[] raw)
        {
            _raw = raw;
            ResultCode = BinaryPrimitives.ReadInt32LittleEndian(
                raw.AsSpan(ResultOffset, 4));
            RequestingServerId = raw[RequestingServerIdOffset];
            OpaquePrefix = Copy(raw, OpaquePrefixOffset, OpaquePrefixLength);
            Name = ReadShortString(raw, NameOffset);
            OpaqueSuffix = Copy(raw, OpaqueSuffixOffset, OpaqueSuffixLength);
        }

        public int ResultCode { get; set; }
        public byte RequestingServerId { get; set; }
        public byte[] OpaquePrefix { get; set; }
        public byte[] Name { get; set; }
        public byte[] OpaqueSuffix { get; set; }

        /// <summary>
        /// Creates the link-down record emitted by native 0x0156.  The native
        /// producer initializes a 0x20-byte stack buffer and only the result,
        /// requester id, and name are consumed by the drain; the untouched
        /// bytes therefore remain zero in this managed representation.
        /// </summary>
        public static NativeGlobalRelayRegistrationQueueRecord Create(
            int resultCode, byte requestingServerId, byte[] name)
        {
            name ??= Array.Empty<byte>();
            if (name.Length > NameCapacity)
                throw new ArgumentOutOfRangeException(nameof(name),
                    $"native 0x274D name exceeds {NameCapacity} bytes");

            var raw = new byte[Size];
            BinaryPrimitives.WriteInt32LittleEndian(
                raw.AsSpan(ResultOffset, sizeof(int)), resultCode);
            raw[RequestingServerIdOffset] = requestingServerId;
            raw[NameOffset] = (byte)name.Length;
            name.AsSpan().CopyTo(raw.AsSpan(NameOffset + 1));
            return new NativeGlobalRelayRegistrationQueueRecord(raw);
        }

        public static bool TryDecode(byte[] source,
            out NativeGlobalRelayRegistrationQueueRecord record,
            out string error)
        {
            record = null;
            error = string.Empty;
            if (source == null || source.Length != Size)
            {
                error = $"native 0x274D queue record must be exactly {Size} bytes";
                return false;
            }
            if (!TryValidateShortString(source, out error))
                return false;
            record = new NativeGlobalRelayRegistrationQueueRecord(
                (byte[])source.Clone());
            return true;
        }

        public static bool TryEncode(
            NativeGlobalRelayRegistrationQueueRecord record,
            out byte[] bytes, out string error)
        {
            bytes = Array.Empty<byte>();
            error = string.Empty;
            if (record == null)
            {
                error = "native 0x274D queue record is null";
                return false;
            }
            if (!TryValidateBytes(record.OpaquePrefix, OpaquePrefixLength,
                    "opaque prefix", out error)
                || !TryValidateBytes(record.Name, NameCapacity,
                    "name", out error)
                || !TryValidateBytes(record.OpaqueSuffix, OpaqueSuffixLength,
                    "opaque suffix", out error))
                return false;

            bytes = (byte[])record._raw.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(ResultOffset, 4), record.ResultCode);
            bytes[RequestingServerIdOffset] = record.RequestingServerId;
            record.OpaquePrefix.AsSpan().CopyTo(bytes.AsSpan(
                OpaquePrefixOffset, OpaquePrefixLength));
            WriteShortString(bytes, record.Name);
            record.OpaqueSuffix.AsSpan().CopyTo(bytes.AsSpan(
                OpaqueSuffixOffset, OpaqueSuffixLength));
            return true;
        }

        public byte[] ToBytes()
        {
            if (!TryEncode(this, out var bytes, out var error))
                throw new InvalidOperationException(error);
            return bytes;
        }

        private static bool TryValidateShortString(byte[] source,
            out string error)
        {
            error = string.Empty;
            var length = source[NameOffset];
            if (length > NameCapacity)
            {
                error = $"native 0x274D name ShortString exceeds {NameCapacity}";
                return false;
            }
            if (NameOffset + 1 + length > Size)
            {
                error = "native 0x274D name ShortString is truncated";
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
                error = $"native 0x274D {name} exceeds {capacity} bytes";
                return false;
            }
            return true;
        }

        private static byte[] ReadShortString(byte[] source, int offset) =>
            source.AsSpan(offset + 1, source[offset]).ToArray();

        private static void WriteShortString(byte[] destination, byte[] value)
        {
            destination[NameOffset] = (byte)value.Length;
            value.AsSpan().CopyTo(destination.AsSpan(NameOffset + 1,
                value.Length));
        }

        private static byte[] Copy(byte[] source, int offset, int length) =>
            source.AsSpan(offset, length).ToArray();
    }
}
