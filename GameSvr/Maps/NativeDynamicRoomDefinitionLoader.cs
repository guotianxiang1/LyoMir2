using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;

namespace GameSvr
{
    public sealed class NativeDynamicRoomConfiguredNpcDefinition
    {
        public NativeDynamicRoomConfiguredNpcDefinition(string scriptName, int x, int y,
            string npcName, int face, int body, int sourceLine)
        {
            ScriptName = scriptName;
            X = x;
            Y = y;
            NpcName = npcName;
            Face = face;
            Body = body;
            SourceLine = sourceLine;
        }

        public string ScriptName { get; }
        public int X { get; }
        public int Y { get; }
        public string NpcName { get; }
        public int Face { get; }
        public int Body { get; }
        public int SourceLine { get; }
    }

    public sealed class NativeDynamicRoomDefinition
    {
        public NativeDynamicRoomDefinition(string roomName, string rawColumn2, int roomType,
            string description, string mapFileName, string rawRoomCount, string rawBalanceCount,
            IReadOnlyList<string> flags,
            IReadOnlyList<NativeDynamicRoomConfiguredNpcDefinition> configuredNpcs,
            int sourceLine)
        {
            RoomName = roomName;
            RawColumn2 = rawColumn2;
            RoomType = roomType;
            Description = description;
            MapFileName = mapFileName;
            RawRoomCount = rawRoomCount;
            RawBalanceCount = rawBalanceCount;
            Flags = flags;
            ConfiguredNpcs = configuredNpcs;
            SourceLine = sourceLine;
        }

        public NativeDynamicRoomDefinition(string roomName, int rawColumn2, int roomType,
            string description, string mapFileName, string rawRoomCount, string rawBalanceCount,
            IReadOnlyList<string> flags,
            IReadOnlyList<NativeDynamicRoomConfiguredNpcDefinition> configuredNpcs,
            int sourceLine)
            : this(roomName, rawColumn2.ToString(CultureInfo.InvariantCulture), roomType,
                description, mapFileName, rawRoomCount, rawBalanceCount,
                flags, configuredNpcs, sourceLine)
        {
        }

        public NativeDynamicRoomDefinition(string roomName, string rawColumn2, int roomType,
            string description, string mapFileName, int rawRoomCount, int rawBalanceCount,
            IReadOnlyList<string> flags,
            IReadOnlyList<NativeDynamicRoomConfiguredNpcDefinition> configuredNpcs,
            int sourceLine)
            : this(roomName, rawColumn2, roomType, description, mapFileName,
                rawRoomCount.ToString(CultureInfo.InvariantCulture),
                rawBalanceCount.ToString(CultureInfo.InvariantCulture),
                flags, configuredNpcs, sourceLine)
        {
        }

        public NativeDynamicRoomDefinition(string roomName, int rawColumn2, int roomType,
            string description, string mapFileName, int rawRoomCount, int rawBalanceCount,
            IReadOnlyList<string> flags,
            IReadOnlyList<NativeDynamicRoomConfiguredNpcDefinition> configuredNpcs,
            int sourceLine)
            : this(roomName, rawColumn2, roomType, description, mapFileName,
                rawRoomCount.ToString(CultureInfo.InvariantCulture),
                rawBalanceCount.ToString(CultureInfo.InvariantCulture),
                flags, configuredNpcs, sourceLine)
        {
        }

        public string RoomName { get; }
        public string RawColumn2 { get; }
        public int RoomType { get; }
        public string Description { get; }
        public string MapFileName { get; }
        public string RawRoomCount { get; }
        public string RawBalanceCount { get; }
        public IReadOnlyList<string> Flags { get; }
        public IReadOnlyList<NativeDynamicRoomConfiguredNpcDefinition> ConfiguredNpcs { get; }
        public int SourceLine { get; }
    }

    public static class NativeDynamicRoomDefinitionLoader
    {
        private static readonly Regex Whitespace = new(@"\s+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static bool TryLoad(string fileName,
            out IReadOnlyList<NativeDynamicRoomDefinition> definitions,
            out IReadOnlyList<string> errors)
        {
            definitions = Array.Empty<NativeDynamicRoomDefinition>();
            errors = Array.Empty<string>();
            if (string.IsNullOrWhiteSpace(fileName) || !File.Exists(fileName))
            {
                errors = new[] { $"PsDynNpc file not found: {fileName}" };
                return false;
            }

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var text = Encoding.GetEncoding(936).GetString(File.ReadAllBytes(fileName));
            return TryParse(text, out definitions, out errors);
        }

        public static bool TryParse(string text,
            out IReadOnlyList<NativeDynamicRoomDefinition> definitions,
            out IReadOnlyList<string> errors)
        {
            var parsed = new List<NativeDynamicRoomDefinitionBuilder>();
            var diagnostics = new List<string>();
            NativeDynamicRoomDefinitionBuilder current = null;
            var seenRooms = new HashSet<string>(StringComparer.Ordinal);

            var lines = (text ?? string.Empty).Replace("\r\n", "\n")
                .Replace('\r', '\n').Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var lineNumber = i + 1;
                var line = lines[i].Trim();
                if (line.Length == 0 || line[0] == ';') continue;

                if (line[0] == '[')
                {
                    if (current == null)
                    {
                        diagnostics.Add($"line {lineNumber}: configured NPC without room");
                        continue;
                    }
                    var configuredNpc = ParseConfiguredNpc(line, lineNumber, diagnostics);
                    if (configuredNpc != null)
                        current.ConfiguredNpcs.Add(configuredNpc);
                    continue;
                }

                current = ParseRoom(line, lineNumber, diagnostics);
                if (current == null) continue;
                if (!seenRooms.Add(current.RoomName))
                    diagnostics.Add($"line {lineNumber}: duplicate room {current.RoomName}");
                parsed.Add(current);
            }

            if (diagnostics.Count > 0)
            {
                definitions = Array.Empty<NativeDynamicRoomDefinition>();
                errors = diagnostics;
                return false;
            }

            definitions = parsed.Select(room => room.ToDefinition()).ToList();
            errors = Array.Empty<string>();
            return true;
        }

        public static IReadOnlyList<string> ValidateMapFiles(
            IEnumerable<NativeDynamicRoomDefinition> definitions, string mapDirectory)
        {
            var diagnostics = new List<string>();
            if (string.IsNullOrWhiteSpace(mapDirectory) || !Directory.Exists(mapDirectory))
            {
                diagnostics.Add($"map directory not found: {mapDirectory}");
                return diagnostics;
            }

            var available = new HashSet<string>(
                Directory.EnumerateFiles(mapDirectory, "*.map")
                    .Select(file => Path.GetFileNameWithoutExtension(file)),
                StringComparer.OrdinalIgnoreCase);
            foreach (var room in definitions)
            {
                if (!available.Contains(room.MapFileName))
                    diagnostics.Add($"room {room.RoomName}: map file not found: {room.MapFileName}.map");
            }
            return diagnostics;
        }

        private static NativeDynamicRoomDefinitionBuilder ParseRoom(string line,
            int lineNumber, List<string> diagnostics)
        {
            var flagStart = line.IndexOf('[');
            var main = flagStart >= 0 ? line[..flagStart].Trim() : line;
            var flagText = string.Empty;
            if (flagStart >= 0)
            {
                var flagEnd = line.LastIndexOf(']');
                if (flagEnd <= flagStart)
                {
                    diagnostics.Add($"line {lineNumber}: malformed flag block");
                    return null;
                }
                flagText = line[(flagStart + 1)..flagEnd].Trim();
            }

            var parts = SplitFields(main);
            if (parts.Length < 7)
            {
                diagnostics.Add($"line {lineNumber}: room definition needs 7 fields");
                return null;
            }

            if (!int.TryParse(parts[2], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var roomType)
                || roomType == -1)
            {
                diagnostics.Add($"line {lineNumber}: room type field is invalid");
                return null;
            }

            return new NativeDynamicRoomDefinitionBuilder
            {
                RoomName = parts[0],
                RawColumn2 = parts[1],
                RoomType = roomType,
                Description = parts[3],
                MapFileName = parts[4],
                RawRoomCount = parts[5],
                RawBalanceCount = parts[6],
                Flags = SplitFields(flagText).ToList(),
                SourceLine = lineNumber
            };
        }

        private static NativeDynamicRoomConfiguredNpcDefinition ParseConfiguredNpc(string line,
            int lineNumber, List<string> diagnostics)
        {
            var close = line.LastIndexOf(']');
            if (close <= 0)
            {
                diagnostics.Add($"line {lineNumber}: malformed configured NPC block");
                return null;
            }

            var parts = SplitFields(line[1..close]);
            if (parts.Length < 6)
            {
                diagnostics.Add($"line {lineNumber}: configured NPC needs 6 fields");
                return null;
            }

            if (!TryParseNonNegative(parts[1], out var x)
                || !TryParseNonNegative(parts[2], out var y)
                || !TryParseNonNegative(parts[4], out var face)
                || !TryParseNonNegative(parts[5], out var body))
            {
                diagnostics.Add($"line {lineNumber}: configured NPC numeric field is invalid");
                return null;
            }

            return new NativeDynamicRoomConfiguredNpcDefinition(parts[0], x, y,
                parts[3], face, body, lineNumber);
        }

        private static string[] SplitFields(string value)
        {
            value = value?.Trim() ?? string.Empty;
            return value.Length == 0
                ? Array.Empty<string>()
                : Whitespace.Split(value);
        }

        private static bool TryParseNonNegative(string value, out int number)
        {
            return int.TryParse(value, out number) && number >= 0;
        }

        private sealed class NativeDynamicRoomDefinitionBuilder
        {
            public string RoomName { get; init; }
            public string RawColumn2 { get; init; }
            public int RoomType { get; init; }
            public string Description { get; init; }
            public string MapFileName { get; init; }
            public string RawRoomCount { get; init; }
            public string RawBalanceCount { get; init; }
            public List<string> Flags { get; init; }
            public List<NativeDynamicRoomConfiguredNpcDefinition> ConfiguredNpcs { get; } = new();
            public int SourceLine { get; init; }

            public NativeDynamicRoomDefinition ToDefinition()
            {
                return new NativeDynamicRoomDefinition(RoomName, RawColumn2, RoomType,
                    Description, MapFileName, RawRoomCount, RawBalanceCount,
                    Flags.AsReadOnly(), ConfiguredNpcs.AsReadOnly(), SourceLine);
            }
        }
    }
}
