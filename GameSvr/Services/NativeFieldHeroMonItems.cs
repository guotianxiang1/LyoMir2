using System.Collections.ObjectModel;
using SystemModule;

namespace GameSvr.Services
{
    public interface INativeFieldHeroMonItemsSource
    {
        IReadOnlyList<string> LoadLines(byte[] definitionNameBytes);
    }

    public sealed class NativeFieldHeroEmptyMonItemsSource
        : INativeFieldHeroMonItemsSource
    {
        public static readonly NativeFieldHeroEmptyMonItemsSource Instance =
            new();

        private NativeFieldHeroEmptyMonItemsSource()
        {
        }

        public IReadOnlyList<string> LoadLines(byte[] definitionNameBytes)
        {
            if (definitionNameBytes == null)
                throw new ArgumentNullException(nameof(definitionNameBytes));
            return Array.Empty<string>();
        }
    }

    public sealed class NativeFieldHeroFileMonItemsSource
        : INativeFieldHeroMonItemsSource
    {
        private readonly string _envirDirectory;

        public NativeFieldHeroFileMonItemsSource(string envirDirectory)
        {
            if (string.IsNullOrWhiteSpace(envirDirectory))
                throw new ArgumentException(
                    "FieldHero Envir directory is required.",
                    nameof(envirDirectory));
            _envirDirectory = Path.GetFullPath(envirDirectory);
        }

        public string ResolvePath(byte[] definitionNameBytes)
        {
            if (definitionNameBytes == null)
                throw new ArgumentNullException(nameof(definitionNameBytes));
            var lookupName = NativeFieldHeroFactoryPreflight
                .CanonicalizeLookupName(definitionNameBytes);
            var fileName = HUtil32.GbkEncoding.GetString(lookupName) + ".txt";
            return Path.Combine(_envirDirectory, "MonItems")
                   + Path.DirectorySeparatorChar + fileName;
        }

        public IReadOnlyList<string> LoadLines(byte[] definitionNameBytes)
        {
            var path = ResolvePath(definitionNameBytes);
            if (!File.Exists(path)) return Array.Empty<string>();

            var lines = new List<string>();
            using var reader = new StreamReader(path, HUtil32.GbkEncoding,
                detectEncodingFromByteOrderMarks: false);
            while (reader.Peek() >= 0) lines.Add(reader.ReadLine());
            return new ReadOnlyCollection<string>(lines);
        }
    }

    public sealed class NativeFieldHeroRuntimeDropBinding
    {
        private readonly byte[] _recordNameBytes;

        internal NativeFieldHeroRuntimeDropBinding(byte[] recordNameBytes,
            int selectionPoint, int maximumPoint, GoodItem item, int count)
        {
            _recordNameBytes = recordNameBytes;
            SelectionPoint = selectionPoint;
            MaximumPoint = maximumPoint;
            Item = item;
            Count = count;
        }

        public string RecordName =>
            HUtil32.GbkEncoding.GetString(_recordNameBytes);
        public int SelectionPoint { get; }
        public int MaximumPoint { get; }
        public GoodItem Item { get; }
        public ushort NativeWireIndex => Item.NativeWireIndex;
        public int Count { get; }
        public bool IsGold => NativeWireIndex == 0;

        public byte[] CopyRecordNameBytes() =>
            (byte[])_recordNameBytes.Clone();
    }

    public static class NativeFieldHeroMonItemsParser
    {
        public const int RecordNameCapacity = 15;

        public static NativeFieldHeroRuntimeDropBinding[] Parse(
            IReadOnlyList<string> lines, Func<string, GoodItem> resolveItem)
        {
            if (lines == null) throw new ArgumentNullException(nameof(lines));
            if (resolveItem == null)
                throw new ArgumentNullException(nameof(resolveItem));

            var result = new List<NativeFieldHeroRuntimeDropBinding>();
            for (var index = 0; index < lines.Count; index++)
            {
                var line = TrimNativeLine(lines[index] ?? string.Empty);
                if (line.Length == 0 || line == ";") continue;

                var remainder = line;
                var token = string.Empty;
                remainder = HUtil32.GetValidStr3(remainder, ref token,
                    new[] { " ", "/", "\t" });
                var selectionPoint = unchecked(
                    StrToIntDef(token, 1) - 1);
                remainder = HUtil32.GetValidStr3(remainder, ref token,
                    new[] { " ", "/", "\t" });
                var maximumPoint = StrToIntDef(token, 1);
                remainder = HUtil32.GetValidStr3(remainder, ref token,
                    new[] { " ", "\t" });
                var itemName = token;
                _ = HUtil32.GetValidStr3(remainder, ref token,
                    new[] { " ", "\t" });
                var count = StrToIntDef(token, 1);

                var item = resolveItem(itemName);
                if (item == null) continue;

                var nameBytes = HUtil32.GbkEncoding.GetBytes(itemName);
                if (nameBytes.Length > RecordNameCapacity)
                    Array.Resize(ref nameBytes, RecordNameCapacity);
                result.Add(new NativeFieldHeroRuntimeDropBinding(nameBytes,
                    selectionPoint, maximumPoint, item, count));
            }
            return result.ToArray();
        }

        private static string TrimNativeLine(string value)
        {
            var first = 0;
            while (first < value.Length && value[first] <= 0x20) first++;
            var last = value.Length - 1;
            while (last >= first && value[last] <= 0x20) last--;
            return first > last
                ? string.Empty
                : value.Substring(first, last - first + 1);
        }

        private static int StrToIntDef(string value, int defaultValue)
        {
            value ??= string.Empty;
            var cursor = 0;
            while (cursor < value.Length && value[cursor] == ' ') cursor++;
            if (cursor == value.Length) return defaultValue;

            var negative = false;
            if (value[cursor] is '-' or '+')
            {
                negative = value[cursor] == '-';
                cursor++;
            }
            if (cursor == value.Length) return defaultValue;

            var radix = 10;
            if (value[cursor] is '$' or 'x' or 'X')
            {
                radix = 16;
                cursor++;
            }
            else if (cursor + 1 < value.Length && value[cursor] == '0'
                     && value[cursor + 1] is 'x' or 'X')
            {
                radix = 16;
                cursor += 2;
            }
            if (cursor == value.Length) return defaultValue;

            ulong parsed = 0;
            var limit = radix == 16
                ? uint.MaxValue
                : negative ? 0x80000000UL : 0x7FFFFFFFUL;
            for (; cursor < value.Length; cursor++)
            {
                var digit = GetDigit(value[cursor]);
                if (digit < 0 || digit >= radix
                    || parsed > (limit - (uint)digit) / (uint)radix)
                    return defaultValue;
                parsed = parsed * (uint)radix + (uint)digit;
            }

            if (radix == 16)
            {
                var signed = unchecked((int)(uint)parsed);
                return negative ? unchecked(-signed) : signed;
            }
            if (!negative) return (int)parsed;
            return parsed == 0x80000000UL
                ? int.MinValue
                : -(int)parsed;
        }

        private static int GetDigit(char value)
        {
            if (value is >= '0' and <= '9') return value - '0';
            if (value is >= 'a' and <= 'f') return value - 'a' + 10;
            if (value is >= 'A' and <= 'F') return value - 'A' + 10;
            return -1;
        }
    }
}
