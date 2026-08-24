using System;

namespace DBSvr.Core
{
    /// <summary>
    /// Encodes the native 20-byte TMirCharInfo row used by 4010/4014 replies.
    /// </summary>
    public static class NativeCharacterListCodec
    {
        public const int RowSize = 20;
        public const int MaxRows = 200;
        public const int NameCapacity = 15;

        public static void WriteRow(Span<byte> destination,
            ReadOnlySpan<byte> name, int job, int sex, int level)
        {
            if (destination.Length < RowSize)
                throw new ArgumentException(
                    $"character row requires {RowSize} bytes", nameof(destination));

            var row = destination.Slice(0, RowSize);
            row.Clear();

            var nameLength = Math.Min(name.Length, NameCapacity);
            row[0] = (byte)nameLength;
            name.Slice(0, nameLength).CopyTo(row.Slice(1, NameCapacity));

            var nativeLevel = unchecked((ushort)level);
            row[16] = unchecked((byte)((nativeLevel >> 8) + 1));
            row[17] = unchecked((byte)job);
            row[18] = unchecked((byte)sex);
            row[19] = (byte)nativeLevel;
        }
    }
}
