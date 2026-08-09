namespace GameSvr.Services
{
    public readonly struct NativeType2MonsterManagerFields
    {
        public NativeType2MonsterManagerFields(int managerLookupValue,
            byte classification, byte classificationValue)
        {
            ManagerLookupValue = managerLookupValue;
            ManagerId = unchecked((ushort)managerLookupValue);
            Classification = classification;
            ClassificationValue = classificationValue;
        }

        public int ManagerLookupValue { get; }
        public ushort ManagerId { get; }
        public byte Classification { get; }
        public byte ClassificationValue { get; }
    }

    /// <summary>
    /// Exact local metadata used by the original M2 0x0067 receiver. Names are
    /// compared as case-sensitive raw bytes so GBK data is never normalized.
    /// </summary>
    public sealed class NativeType2MonsterManagerTables
    {
        private delegate void LineParser(ReadOnlySpan<byte> line);

        private sealed class ButchEntry
        {
            public ButchEntry(byte[] nameBytes, byte classification,
                byte classificationValue)
            {
                NameBytes = nameBytes;
                Classification = classification;
                ClassificationValue = classificationValue;
            }

            public byte[] NameBytes { get; }
            public byte Classification { get; }
            public byte ClassificationValue { get; }
        }

        private readonly Dictionary<string, int> _managerIds =
            new(StringComparer.Ordinal);
        private readonly List<ButchEntry> _butchEntries = new();

        private NativeType2MonsterManagerTables(byte[] monBasePkContent,
            byte[] butchTypeContent)
        {
            MonBasePkLoaded = monBasePkContent != null;
            ButchTypeLoaded = butchTypeContent != null;

            if (monBasePkContent != null)
                ParseLines(monBasePkContent, ParseMonBasePkLine);
            if (butchTypeContent != null)
                ParseLines(butchTypeContent, ParseButchTypeLine);
        }

        public bool MonBasePkLoaded { get; }
        public bool ButchTypeLoaded { get; }

        public static NativeType2MonsterManagerTables FromContents(
            byte[] monBasePkContent, byte[] butchTypeContent) =>
            new(monBasePkContent, butchTypeContent);

        public static NativeType2MonsterManagerTables LoadFromDirectory(
            string configDirectory)
        {
            if (configDirectory == null)
                throw new ArgumentNullException(nameof(configDirectory));

            return new NativeType2MonsterManagerTables(
                TryRead(Path.Combine(configDirectory, "MonBasePk.txt")),
                TryRead(Path.Combine(configDirectory, "ButchType.txt")));
        }

        public NativeType2MonsterManagerFields Resolve(
            NativeType2MonsterDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            return Resolve(definition.CopyNameBytes());
        }

        public NativeType2MonsterManagerFields Resolve(
            ReadOnlySpan<byte> nameBytes)
        {
            var managerLookupValue = 0;
            if (_managerIds.TryGetValue(Key(nameBytes), out var value)
                && value >= 0)
            {
                managerLookupValue = value;
            }

            for (var index = 0; index < _butchEntries.Count; index++)
            {
                var entry = _butchEntries[index];
                if (!nameBytes.SequenceEqual(entry.NameBytes)) continue;

                return entry.Classification <= 8
                    ? new NativeType2MonsterManagerFields(managerLookupValue,
                        entry.Classification, entry.ClassificationValue)
                    : new NativeType2MonsterManagerFields(
                        managerLookupValue, 0, 0);
            }

            return new NativeType2MonsterManagerFields(
                managerLookupValue, 0, 0);
        }

        private static byte[] TryRead(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                return File.ReadAllBytes(path);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        private void ParseMonBasePkLine(ReadOnlySpan<byte> line)
        {
            var tokens = Tokens(line, 2);
            if (tokens[0].Length == 0) return;

            // The native hash table prepends each entry, so the last duplicate
            // in the file is the value returned by lookup.
            _managerIds[Key(tokens[0])] = ParseNativeInt32OrZero(tokens[1]);
        }

        private void ParseButchTypeLine(ReadOnlySpan<byte> line)
        {
            var tokens = Tokens(line, 3);
            if (tokens[0].Length == 0) return;

            var nameLength = Math.Min(
                NativeType2MonsterDefinition.NameCapacity, tokens[0].Length);
            var name = tokens[0].AsSpan(0, nameLength).ToArray();
            _butchEntries.Add(new ButchEntry(name,
                unchecked((byte)ParseNativeInt32OrZero(tokens[1])),
                unchecked((byte)ParseNativeInt32OrZero(tokens[2]))));
        }

        private static void ParseLines(byte[] content, LineParser parseLine)
        {
            var start = 0;
            while (start <= content.Length)
            {
                var end = start;
                while (end < content.Length
                       && content[end] != (byte)'\r'
                       && content[end] != (byte)'\n')
                {
                    end++;
                }

                var line = Trim(content.AsSpan(start, end - start));
                if (line.Length != 0 && line[0] != (byte)';')
                    parseLine(line);

                if (end == content.Length) break;
                var firstSeparator = content[end++];
                if (end < content.Length
                    && ((firstSeparator == (byte)'\r'
                         && content[end] == (byte)'\n')
                        || (firstSeparator == (byte)'\n'
                            && content[end] == (byte)'\r')))
                {
                    end++;
                }
                start = end;
            }
        }

        private static ReadOnlySpan<byte> Trim(ReadOnlySpan<byte> value)
        {
            var start = 0;
            while (start < value.Length && value[start] <= 0x20) start++;
            var end = value.Length;
            while (end > start && value[end - 1] <= 0x20) end--;
            return value.Slice(start, end - start);
        }

        private static byte[][] Tokens(ReadOnlySpan<byte> line, int count)
        {
            var result = new byte[count][];
            var position = 0;
            for (var index = 0; index < count; index++)
            {
                while (position < line.Length && IsDelimiter(line[position]))
                    position++;
                var start = position;
                while (position < line.Length && !IsDelimiter(line[position]))
                    position++;
                result[index] = line.Slice(start, position - start).ToArray();
            }
            return result;
        }

        private static bool IsDelimiter(byte value) =>
            value == (byte)' ' || value == (byte)'\t';

        private static string Key(ReadOnlySpan<byte> value) =>
            Convert.ToBase64String(value);

        private static int ParseNativeInt32OrZero(ReadOnlySpan<byte> value) =>
            TryParseNativeInt32(value, out var parsed) ? parsed : 0;

        private static bool TryParseNativeInt32(ReadOnlySpan<byte> value,
            out int result)
        {
            result = 0;
            var position = 0;
            while (position < value.Length && value[position] == (byte)' ')
                position++;
            if (position == value.Length) return false;

            var negative = false;
            if (value[position] == (byte)'-'
                || value[position] == (byte)'+')
            {
                negative = value[position] == (byte)'-';
                position++;
                if (position == value.Length) return false;
            }

            var hexadecimal = false;
            if (value[position] == (byte)'$'
                || value[position] == (byte)'x'
                || value[position] == (byte)'X')
            {
                hexadecimal = true;
                position++;
            }
            else if (value[position] == (byte)'0')
            {
                position++;
                if (position < value.Length
                    && (value[position] == (byte)'x'
                        || value[position] == (byte)'X'))
                {
                    hexadecimal = true;
                    position++;
                }
                else if (position == value.Length)
                {
                    result = 0;
                    return true;
                }
            }

            if (position == value.Length) return false;

            uint accumulator = 0;
            if (hexadecimal)
            {
                for (; position < value.Length; position++)
                {
                    if (!TryHexDigit(value[position], out var digit)
                        || accumulator > 0x0FFFFFFF)
                        return false;
                    accumulator = unchecked(accumulator * 16 + digit);
                }

                var signed = unchecked((int)accumulator);
                result = negative ? unchecked(-signed) : signed;
                return true;
            }

            for (; position < value.Length; position++)
            {
                var current = value[position];
                if (current < (byte)'0' || current > (byte)'9'
                    || accumulator > 0x0CCCCCCC)
                    return false;
                accumulator = unchecked(
                    accumulator * 10 + (uint)(current - (byte)'0'));
            }

            var decimalValue = unchecked((int)accumulator);
            if (negative)
            {
                result = unchecked(-decimalValue);
                return result <= 0;
            }

            result = decimalValue;
            return result >= 0;
        }

        private static bool TryHexDigit(byte value, out uint digit)
        {
            if (value >= (byte)'0' && value <= (byte)'9')
            {
                digit = (uint)(value - (byte)'0');
                return true;
            }

            if (value >= (byte)'a') value = unchecked((byte)(value - 0x20));
            if (value >= (byte)'A' && value <= (byte)'F')
            {
                digit = (uint)(value - (byte)'A' + 10);
                return true;
            }

            digit = 0;
            return false;
        }
    }
}
