using System.Buffers.Binary;
using SystemModule;

namespace GameSvr
{
    internal enum NativeDropControlType : byte
    {
        Timed = 1,
        Counted = 2
    }

    internal enum NativeDropControlBucketField : byte
    {
        ItemName,
        MonsterName
    }

    internal sealed class NativeDropControlRecord
    {
        internal const int NativeSize = 104;
        internal const int TextCapacity = 40;

        private readonly byte[] _monsterNameBytes;
        private readonly byte[] _itemNameBytes;

        internal NativeDropControlRecord(byte[] monsterNameBytes,
            byte[] itemNameBytes, ushort quantity, int periodOrRange,
            int itemIndex, ushort randomThreshold, int tick)
        {
            _monsterNameBytes = monsterNameBytes;
            _itemNameBytes = itemNameBytes;
            Quantity = quantity;
            PeriodOrRange = periodOrRange;
            ItemIndex = itemIndex;
            RandomThreshold = randomThreshold;
            Tick = tick;
        }

        internal string MonsterName =>
            HUtil32.GbkEncoding.GetString(_monsterNameBytes);
        internal string ItemName => HUtil32.GbkEncoding.GetString(_itemNameBytes);
        internal ushort Quantity { get; }
        internal int PeriodOrRange { get; }
        internal int ItemIndex { get; }
        internal ushort Counter { get; set; }
        internal ushort RandomThreshold { get; set; }
        internal int Tick { get; set; }

        internal byte[] ToNativeLayout()
        {
            var result = new byte[NativeSize];
            WriteShortString(result.AsSpan(0, TextCapacity + 1),
                _monsterNameBytes);
            WriteShortString(result.AsSpan(0x29, TextCapacity + 1),
                _itemNameBytes);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(0x52, 2),
                Quantity);
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(0x54, 4),
                PeriodOrRange);
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(0x58, 4),
                ItemIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(0x5C, 2),
                Counter);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(0x5E, 2),
                RandomThreshold);
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(0x60, 4),
                Tick);
            return result;
        }

        internal byte[] GetBucketBytes(NativeDropControlBucketField field)
        {
            var source = field == NativeDropControlBucketField.ItemName
                ? _itemNameBytes
                : _monsterNameBytes;
            return (byte[])source.Clone();
        }

        internal NativeDropControlRecord Clone()
        {
            return new NativeDropControlRecord(
                (byte[])_monsterNameBytes.Clone(),
                (byte[])_itemNameBytes.Clone(), Quantity, PeriodOrRange,
                ItemIndex, RandomThreshold, Tick)
            {
                Counter = Counter
            };
        }

        private static void WriteShortString(Span<byte> destination,
            ReadOnlySpan<byte> value)
        {
            destination.Clear();
            var length = Math.Min(TextCapacity, value.Length);
            destination[0] = (byte)length;
            value.Slice(0, length).CopyTo(destination.Slice(1));
        }
    }

    internal sealed class NativeDropControlState
    {
        private readonly object _syncRoot = new();
        private readonly SortedDictionary<string, List<NativeDropControlRecord>>
            _timed = new(StringComparer.Ordinal);
        private readonly SortedDictionary<string, List<NativeDropControlRecord>>
            _counted = new(StringComparer.Ordinal);

        internal NativeDropControlState(NativeDropControlBucketField bucketField)
        {
            BucketField = bucketField;
        }

        internal NativeDropControlBucketField BucketField { get; }

        internal int RecordCount
        {
            get
            {
                lock (_syncRoot)
                    return CountRecords(_timed) + CountRecords(_counted);
            }
        }

        internal int BucketCount(NativeDropControlType type)
        {
            lock (_syncRoot)
                return GetTable(type).Count;
        }

        internal void Clear()
        {
            lock (_syncRoot)
                ClearUnsafe();
        }

        internal void VisitAll(NativeDropControlType type,
            Action<NativeDropControlRecord> visitor)
        {
            ArgumentNullException.ThrowIfNull(visitor);
            lock (_syncRoot)
            {
                foreach (var bucket in GetTable(type).Values)
                {
                    foreach (var record in bucket)
                        visitor(record);
                }
            }
        }

        internal void VisitBucket(NativeDropControlType type, string bucketName,
            Action<NativeDropControlRecord> visitor)
        {
            ArgumentNullException.ThrowIfNull(visitor);
            var bucketBytes = NativeDropControlLoader.ToShortStringBytes(
                bucketName ?? string.Empty);
            var key = MakeKey(bucketBytes);
            lock (_syncRoot)
            {
                if (!GetTable(type).TryGetValue(key, out var bucket))
                    return;
                foreach (var record in bucket)
                    visitor(record);
            }
        }

        internal IReadOnlyList<NativeDropControlRecord> Snapshot(
            NativeDropControlType type)
        {
            lock (_syncRoot)
            {
                var result = new List<NativeDropControlRecord>();
                foreach (var bucket in GetTable(type).Values)
                {
                    foreach (var record in bucket)
                        result.Add(record.Clone());
                }
                return result;
            }
        }

        internal bool ReloadExistingFile(Func<byte[]> readFile,
            Action<byte[], NativeDropControlState> parse, out string error)
        {
            lock (_syncRoot)
            {
                ClearUnsafe();
                try
                {
                    parse(readFile(), this);
                    error = string.Empty;
                    return true;
                }
                catch (Exception exception) when (exception is not
                                                   OutOfMemoryException)
                {
                    // The native loader clears before LoadFromFile and does not
                    // roll back records already inserted when parsing aborts.
                    error = exception.Message;
                    return false;
                }
            }
        }

        internal void AddUnsafe(NativeDropControlType type,
            NativeDropControlRecord record)
        {
            var key = MakeKey(record.GetBucketBytes(BucketField));
            var table = GetTable(type);
            if (!table.TryGetValue(key, out var bucket))
            {
                table.Add(key, new List<NativeDropControlRecord> { record });
                return;
            }

            // Native +0x64 insertion keeps the first record as the head and
            // inserts every later same-key record directly behind it.
            bucket.Insert(1, record);
        }

        private SortedDictionary<string, List<NativeDropControlRecord>> GetTable(
            NativeDropControlType type)
        {
            return type == NativeDropControlType.Timed ? _timed : _counted;
        }

        private void ClearUnsafe()
        {
            _timed.Clear();
            _counted.Clear();
        }

        private static int CountRecords(
            SortedDictionary<string, List<NativeDropControlRecord>> table)
        {
            var result = 0;
            foreach (var bucket in table.Values)
                result += bucket.Count;
            return result;
        }

        private static string MakeKey(ReadOnlySpan<byte> value)
        {
            Span<byte> normalized = stackalloc byte[value.Length];
            for (var i = 0; i < value.Length; i++)
            {
                var current = value[i];
                normalized[i] = current is >= (byte)'a' and <= (byte)'z'
                    ? (byte)(current - 0x20)
                    : current;
            }
            return Convert.ToHexString(normalized);
        }
    }

    internal static class NativeDropControlLoader
    {
        private static readonly byte[] FirstSeparators =
            { (byte)'\t', (byte)':', (byte)' ' };
        private static readonly byte[] TextSeparators =
            { (byte)'\t', (byte)' ' };

        internal static bool TryLoad(string rootDirectory, string configName,
            NativeDropControlState destination, out string error)
        {
            return TryLoadMap(rootDirectory, configName, destination,
                ResolveItem, NextRandom, HUtil32.GetTickCount, out error);
        }

        internal static bool TryLoadMap(string rootDirectory, string configName,
            NativeDropControlState destination, out string error)
        {
            return TryLoad(rootDirectory, configName, destination,
                NativeDropControlBucketField.ItemName, ResolveItem, NextRandom,
                HUtil32.GetTickCount, out error);
        }

        internal static bool TryLoadWorld(string rootDirectory,
            NativeDropControlState destination, out string error)
        {
            return TryLoad(rootDirectory, "WorldDrop", destination,
                NativeDropControlBucketField.MonsterName, ResolveItem,
                NextRandom, HUtil32.GetTickCount, out error);
        }

        internal static bool TryLoadMap(string rootDirectory, string configName,
            NativeDropControlState destination, Func<string, int> itemResolver,
            Func<int, int> random, Func<int> tick, out string error)
        {
            return TryLoad(rootDirectory, configName, destination,
                NativeDropControlBucketField.ItemName, itemResolver, random,
                tick, out error);
        }

        internal static bool TryLoadWorld(string rootDirectory,
            NativeDropControlState destination, Func<string, int> itemResolver,
            Func<int, int> random, Func<int> tick, out string error)
        {
            return TryLoad(rootDirectory, "WorldDrop", destination,
                NativeDropControlBucketField.MonsterName, itemResolver, random,
                tick, out error);
        }

        internal static byte[] ToShortStringBytes(string value)
        {
            var encoded = HUtil32.GbkEncoding.GetBytes(value ?? string.Empty);
            if (encoded.Length <= NativeDropControlRecord.TextCapacity)
                return encoded;
            return encoded.AsSpan(0,
                NativeDropControlRecord.TextCapacity).ToArray();
        }

        private static bool TryLoad(string rootDirectory, string configName,
            NativeDropControlState destination,
            NativeDropControlBucketField requiredBucketField,
            Func<string, int> itemResolver, Func<int, int> random,
            Func<int> tick, out string error)
        {
            if (destination == null || itemResolver == null || random == null ||
                tick == null || string.IsNullOrEmpty(rootDirectory) ||
                string.IsNullOrEmpty(configName))
            {
                error = "DropControl loader arguments are incomplete";
                return false;
            }
            if (destination.BucketField != requiredBucketField)
            {
                error = "DropControl state uses the wrong bucket field";
                return false;
            }

            var fileName = Path.Combine(rootDirectory, "DropControl",
                configName + ".txt");
            if (!File.Exists(fileName))
            {
                error = fileName + ": file not found";
                return false;
            }

            return destination.ReloadExistingFile(
                () => File.ReadAllBytes(fileName),
                (bytes, state) => Parse(bytes, state, itemResolver, random,
                    tick), out error);
        }

        private static void Parse(byte[] fileBytes,
            NativeDropControlState destination, Func<string, int> itemResolver,
            Func<int, int> random, Func<int> tick)
        {
            var type = (NativeDropControlType)0;
            foreach (var rawLine in SplitLines(fileBytes))
            {
                var line = Trim(rawLine);
                if (line.Length == 0 || line.AsSpan().IndexOf((byte)';') >= 0)
                    continue;
                if (EqualsAsciiIgnoreCase(line, "type=1"))
                {
                    type = NativeDropControlType.Timed;
                    continue;
                }
                if (EqualsAsciiIgnoreCase(line, "type=2"))
                {
                    type = NativeDropControlType.Counted;
                    continue;
                }
                if (type is not (NativeDropControlType.Timed or
                    NativeDropControlType.Counted))
                {
                    continue;
                }

                var remaining = line;
                SplitFirst(ref remaining, FirstSeparators, out var quantityText);
                SplitFirst(ref remaining, FirstSeparators, out var rangeText);
                SplitFirst(ref remaining, TextSeparators, out var itemNameText);
                SplitFirst(ref remaining, TextSeparators,
                    out var monsterNameText);

                var quantity = unchecked((ushort)ParseIntOrDefault(
                    quantityText, 0));
                var periodOrRange = ParseIntOrDefault(rangeText, 1);
                var fullItemName = HUtil32.GbkEncoding.GetString(itemNameText);
                var record = new NativeDropControlRecord(
                    Truncate(monsterNameText), Truncate(itemNameText), quantity,
                    periodOrRange, itemResolver(fullItemName),
                    unchecked((ushort)(random(periodOrRange) + 1)), tick());
                destination.AddUnsafe(type, record);
            }
        }

        private static IEnumerable<byte[]> SplitLines(byte[] source)
        {
            var start = 0;
            for (var index = 0; index < source.Length; index++)
            {
                if (source[index] is not ((byte)'\r' or (byte)'\n'))
                    continue;
                yield return source.AsSpan(start, index - start).ToArray();
                if (source[index] == (byte)'\r' && index + 1 < source.Length &&
                    source[index + 1] == (byte)'\n')
                {
                    index++;
                }
                start = index + 1;
            }
            if (start < source.Length)
                yield return source.AsSpan(start).ToArray();
        }

        private static byte[] Trim(byte[] source)
        {
            var start = 0;
            while (start < source.Length && source[start] <= 0x20)
                start++;
            var end = source.Length;
            while (end > start && source[end - 1] <= 0x20)
                end--;
            return source.AsSpan(start, end - start).ToArray();
        }

        private static void SplitFirst(ref byte[] source,
            ReadOnlySpan<byte> separators, out byte[] token)
        {
            var first = 0;
            while (first < source.Length &&
                   separators.IndexOf(source[first]) >= 0)
            {
                first++;
            }
            if (first == source.Length)
            {
                token = Array.Empty<byte>();
                source = Array.Empty<byte>();
                return;
            }

            var separator = first + 1;
            while (separator < source.Length &&
                   separators.IndexOf(source[separator]) < 0)
            {
                separator++;
            }
            token = source.AsSpan(first, separator - first).ToArray();
            source = separator < source.Length
                ? source.AsSpan(separator + 1).ToArray()
                : Array.Empty<byte>();
        }

        private static bool EqualsAsciiIgnoreCase(ReadOnlySpan<byte> source,
            string expected)
        {
            if (source.Length != expected.Length)
                return false;
            for (var i = 0; i < source.Length; i++)
            {
                var value = source[i];
                if (value is >= (byte)'a' and <= (byte)'z')
                    value = (byte)(value - 0x20);
                var expectedValue = (byte)expected[i];
                if (expectedValue is >= (byte)'a' and <= (byte)'z')
                    expectedValue = (byte)(expectedValue - 0x20);
                if (value != expectedValue)
                    return false;
            }
            return true;
        }

        private static int ParseIntOrDefault(ReadOnlySpan<byte> value,
            int defaultValue)
        {
            var cursor = 0;
            while (cursor < value.Length && value[cursor] == (byte)' ')
                cursor++;
            if (cursor == value.Length)
                return defaultValue;

            var negative = false;
            if (value[cursor] is (byte)'-' or (byte)'+')
            {
                negative = value[cursor] == (byte)'-';
                cursor++;
            }
            if (cursor == value.Length)
                return defaultValue;

            var radix = 10;
            if (value[cursor] is (byte)'$' or (byte)'x' or (byte)'X')
            {
                radix = 16;
                cursor++;
            }
            else if (cursor + 1 < value.Length && value[cursor] == (byte)'0' &&
                     value[cursor + 1] is (byte)'x' or (byte)'X')
            {
                radix = 16;
                cursor += 2;
            }
            if (cursor == value.Length)
                return defaultValue;

            ulong parsed = 0;
            var limit = negative ? 0x80000000UL : 0x7FFFFFFFUL;
            for (; cursor < value.Length; cursor++)
            {
                var digit = GetDigit(value[cursor]);
                if (digit < 0 || digit >= radix ||
                    parsed > (limit - (uint)digit) / (uint)radix)
                {
                    return defaultValue;
                }
                parsed = parsed * (uint)radix + (uint)digit;
            }
            if (!negative)
                return (int)parsed;
            return parsed == 0x80000000UL ? int.MinValue : -(int)parsed;
        }

        private static int GetDigit(byte value)
        {
            if (value is >= (byte)'0' and <= (byte)'9')
                return value - (byte)'0';
            if (value is >= (byte)'a' and <= (byte)'f')
                return value - (byte)'a' + 10;
            if (value is >= (byte)'A' and <= (byte)'F')
                return value - (byte)'A' + 10;
            return -1;
        }

        private static byte[] Truncate(ReadOnlySpan<byte> value)
        {
            return value.Slice(0, Math.Min(value.Length,
                NativeDropControlRecord.TextCapacity)).ToArray();
        }

        private static int ResolveItem(string itemName)
        {
            return M2Share.UserEngine?.GetStdItemIdx(itemName) ?? 0;
        }

        private static int NextRandom(int range)
        {
            return M2Share.RandomNumber?.Random(range) ?? 0;
        }
    }
}
