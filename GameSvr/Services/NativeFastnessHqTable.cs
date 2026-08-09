using System.Globalization;

namespace GameSvr.Services
{
    public sealed class NativeFastnessHqTable
    {
        private readonly Dictionary<int, Entry> _entries = new();

        private readonly struct Entry
        {
            public Entry(double ratio, int limit)
            {
                Ratio = ratio;
                Limit = limit;
            }

            public double Ratio { get; }
            public int Limit { get; }
        }

        public int Count => _entries.Count;
        public int MaximumPositiveKey { get; private set; }

        public bool Load(string fileName)
        {
            if (string.IsNullOrEmpty(fileName) || !File.Exists(fileName))
                return false;

            // The native loader preserves the hot table when the file cannot
            // be opened, and clears it only after LoadFromFile succeeds.
            var lines = File.ReadAllLines(fileName);
            _entries.Clear();
            MaximumPositiveKey = 0;

            foreach (var line in lines)
            {
                if (line.Length == 0 || line[0] == '#' || line[0] == ';')
                    continue;

                var position = 0;
                var keyToken = NextToken(line, ref position);
                var ratioToken = NextToken(line, ref position);
                var limitToken = NextToken(line, ref position);

                var key = ParseInt32OrZero(keyToken);
                if (key == 0)
                    continue;

                var ratio = ParseDoubleOrZero(ratioToken);
                var limit = ParseInt32OrZero(limitToken);
                _entries[key] = new Entry(ratio, limit);
                if (key > MaximumPositiveKey)
                    MaximumPositiveKey = key;
            }

            return true;
        }

        public bool TryResolve(int selector, out double ratio, out int limit)
        {
            if (MaximumPositiveKey > 0 && selector > MaximumPositiveKey)
                selector = MaximumPositiveKey;

            if (_entries.TryGetValue(selector, out var entry))
            {
                ratio = entry.Ratio;
                limit = entry.Limit;
                return true;
            }

            ratio = 0;
            limit = 0;
            return false;
        }

        public int CalculateReduction(int damage, int selector)
        {
            if (!TryResolve(selector, out var ratio, out var limit))
                return 0;

            var truncated = unchecked((long)Math.Truncate(
                (double)damage * ratio));
            var candidate = unchecked((int)truncated);
            return Math.Min(candidate, limit);
        }

        public int ApplyReduction(int damage, int selector)
        {
            return unchecked(damage - CalculateReduction(damage, selector));
        }

        private static ReadOnlySpan<char> NextToken(string line,
            ref int position)
        {
            while (position < line.Length && IsDelimiter(line[position]))
                position++;

            var start = position;
            while (position < line.Length && !IsDelimiter(line[position]))
                position++;
            return line.AsSpan(start, position - start);
        }

        private static bool IsDelimiter(char value) =>
            value == ' ' || value == '\t';

        private static int ParseInt32OrZero(ReadOnlySpan<char> value)
        {
            return int.TryParse(value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0;
        }

        private static double ParseDoubleOrZero(ReadOnlySpan<char> value)
        {
            return double.TryParse(value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0;
        }
    }
}
