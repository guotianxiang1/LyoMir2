using System.Text;

namespace GameSvr
{
    public sealed class NativeDynamicRoomEventCoordinate
    {
        public NativeDynamicRoomEventCoordinate(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; }
        public int Y { get; }
    }

    public sealed class NativeDynamicRoomEventDescriptor
    {
        public NativeDynamicRoomEventDescriptor(ushort rawEventType,
            int durationSeconds,
            IReadOnlyList<NativeDynamicRoomEventCoordinate> coordinates,
            int sourceLine)
        {
            RawEventType = rawEventType;
            DurationSeconds = durationSeconds;
            Coordinates = coordinates;
            SourceLine = sourceLine;
        }

        public ushort RawEventType { get; }
        public byte EffectiveEventType => unchecked((byte)RawEventType);
        public int DurationSeconds { get; }
        public IReadOnlyList<NativeDynamicRoomEventCoordinate> Coordinates { get; }
        public int SourceLine { get; }
    }

    public static class NativeDynamicRoomEventDescriptorLoader
    {
        public static string BuildFileName(string roomName)
        {
            return string.IsNullOrEmpty(roomName)
                ? string.Empty
                : $"Envent_{roomName}.txt";
        }

        public static bool TryLoad(string scriptDirectory, string roomName,
            out IReadOnlyList<NativeDynamicRoomEventDescriptor> descriptors,
            out IReadOnlyList<string> diagnostics)
        {
            descriptors = Array.Empty<NativeDynamicRoomEventDescriptor>();
            diagnostics = Array.Empty<string>();

            var fileName = BuildFileName(roomName);
            if (string.IsNullOrWhiteSpace(scriptDirectory)
                || !IsSafeFileName(fileName))
            {
                diagnostics = new[] { "invalid dynamic room event descriptor path" };
                return false;
            }

            var path = Path.Combine(scriptDirectory, fileName);
            if (!File.Exists(path)) return true;

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (IOException ex)
            {
                diagnostics = new[]
                {
                    $"dynamic room event descriptor could not be read: {fileName} ({ex.Message})"
                };
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                diagnostics = new[]
                {
                    $"dynamic room event descriptor could not be read: {fileName} ({ex.Message})"
                };
                return false;
            }

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var text = Encoding.GetEncoding(936).GetString(bytes);
            return TryParse(text, out descriptors, out diagnostics);
        }

        public static bool TryParse(string text,
            out IReadOnlyList<NativeDynamicRoomEventDescriptor> descriptors,
            out IReadOnlyList<string> diagnostics)
        {
            var parsed = new List<NativeDynamicRoomEventDescriptor>();
            var auditDiagnostics = new List<string>();
            var lines = (text ?? string.Empty).Replace("\r\n", "\n")
                .Replace('\r', '\n').Split('\n');

            for (var i = 0; i < lines.Length; i++)
            {
                var rawLine = lines[i];
                var sourceLine = i + 1;
                if (rawLine.Length > 0
                    && (rawLine[0] == '#' || rawLine[0] == ';'))
                {
                    continue;
                }

                var position = 0;
                var eventTypeText = ReadField(rawLine, ref position);
                var durationText = ReadField(rawLine, ref position);
                var coordinateText = ReadField(rawLine, ref position);
                if (eventTypeText.Length == 0 || durationText.Length == 0
                    || coordinateText.Length == 0)
                {
                    if (rawLine.Length > 0)
                        auditDiagnostics.Add($"line {sourceLine}: fewer than 3 fields");
                    continue;
                }

                var eventType = ParseOrDefault(eventTypeText, 0);
                var durationSeconds = ParseOrDefault(durationText, 1);
                var coordinates = ParseCoordinates(coordinateText, sourceLine,
                    auditDiagnostics);
                parsed.Add(new NativeDynamicRoomEventDescriptor(
                    unchecked((ushort)eventType), durationSeconds,
                    coordinates.AsReadOnly(), sourceLine));
            }

            descriptors = parsed.AsReadOnly();
            diagnostics = auditDiagnostics.AsReadOnly();
            return true;
        }

        private static List<NativeDynamicRoomEventCoordinate> ParseCoordinates(
            string value, int sourceLine, List<string> diagnostics)
        {
            var coordinates = new List<NativeDynamicRoomEventCoordinate>();
            foreach (var coordinateText in value.Split('|'))
            {
                var separator = coordinateText.IndexOf(',');
                if (separator < 0)
                {
                    diagnostics.Add($"line {sourceLine}: invalid coordinate {coordinateText}");
                    continue;
                }

                var x = ParseOrDefault(coordinateText[..separator], 0);
                var y = ParseOrDefault(coordinateText[(separator + 1)..], 0);
                if (x <= 0 || y <= 0)
                {
                    diagnostics.Add($"line {sourceLine}: invalid coordinate {coordinateText}");
                    continue;
                }

                coordinates.Add(new NativeDynamicRoomEventCoordinate(x, y));
            }
            return coordinates;
        }

        private static string ReadField(string value, ref int position)
        {
            while (position < value.Length
                   && (value[position] == ' ' || value[position] == '\t'))
            {
                position++;
            }

            var start = position;
            while (position < value.Length
                   && value[position] != ' ' && value[position] != '\t')
            {
                position++;
            }
            return value[start..position];
        }

        private static int ParseOrDefault(string value, int defaultValue)
        {
            if (string.IsNullOrEmpty(value)) return defaultValue;

            var position = 0;
            while (position < value.Length && value[position] == ' ') position++;

            var negative = false;
            if (position < value.Length
                && (value[position] == '+' || value[position] == '-'))
            {
                negative = value[position] == '-';
                position++;
            }

            var hexadecimal = false;
            if (position < value.Length && value[position] == '$')
            {
                hexadecimal = true;
                position++;
            }
            else if (position < value.Length
                     && (value[position] == 'x' || value[position] == 'X'))
            {
                hexadecimal = true;
                position++;
            }
            else if (position + 1 < value.Length && value[position] == '0'
                     && (value[position + 1] == 'x'
                         || value[position + 1] == 'X'))
            {
                hexadecimal = true;
                position += 2;
            }

            if (position >= value.Length) return defaultValue;
            if (hexadecimal)
            {
                uint bits = 0;
                for (; position < value.Length; position++)
                {
                    var digit = HexDigit(value[position]);
                    if (digit < 0
                        || bits > (uint.MaxValue - (uint)digit) / 16)
                    {
                        return defaultValue;
                    }
                    bits = bits * 16 + (uint)digit;
                }

                var signed = unchecked((int)bits);
                return negative ? unchecked(-signed) : signed;
            }

            var limit = negative ? 2147483648u : int.MaxValue;
            uint magnitude = 0;
            for (; position < value.Length; position++)
            {
                var character = value[position];
                if (character < '0' || character > '9') return defaultValue;
                var digit = (uint)(character - '0');
                if (magnitude > (limit - digit) / 10) return defaultValue;
                magnitude = magnitude * 10 + digit;
            }

            if (!negative) return (int)magnitude;
            return magnitude == 2147483648u
                ? int.MinValue
                : -(int)magnitude;
        }

        private static int HexDigit(char value)
        {
            if (value is >= '0' and <= '9') return value - '0';
            if (value is >= 'a' and <= 'f') return value - 'a' + 10;
            if (value is >= 'A' and <= 'F') return value - 'A' + 10;
            return -1;
        }

        private static bool IsSafeFileName(string fileName)
        {
            return !string.IsNullOrWhiteSpace(fileName)
                && fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
                && fileName.IndexOfAny(new[] { '/', '\\' }) < 0
                && !Path.IsPathRooted(fileName);
        }
    }
}
