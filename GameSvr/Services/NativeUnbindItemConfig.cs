using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Threading;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Immutable records parsed from the native UnBindItem.txt file.
    ///
    /// The native loader keeps a four-token row: item name, quantity,
    /// probability and binding flag.  The names below deliberately expose
    /// both the positional values and the meanings used by the source file.
    /// </summary>
    public sealed class NativeUnbindItemEntry
    {
        internal NativeUnbindItemEntry(string name, int value1, int value2,
            int value3)
        {
            Name = name;
            Value1 = value1;
            Value2 = value2;
            Value3 = value3;
        }

        public string Name { get; }
        public int Value1 { get; }
        public int Value2 { get; }
        public int Value3 { get; }

        public int Quantity => Value1;
        public int Count => Value1;
        public int Probability => Value2;
        public int Rate => Value2;
        public int BindFlag => Value3;
        public int Binding => Value3;
    }

    /// <summary>A bracketed section from UnBindItem.txt.</summary>
    public sealed class NativeUnbindItemSection
    {
        internal NativeUnbindItemSection(string name,
            IList<NativeUnbindItemEntry> items)
        {
            Name = name;
            Items = new ReadOnlyCollection<NativeUnbindItemEntry>(
                items.ToArray());
        }

        public string Name { get; }
        public string SectionName => Name;
        public IReadOnlyList<NativeUnbindItemEntry> Items { get; }
        public int ItemCount => Items.Count;
    }

    /// <summary>
    /// A publication-safe view of all UnBindItem.txt sections.
    /// </summary>
    public sealed class NativeUnbindItemSnapshot
    {
        internal NativeUnbindItemSnapshot(
            IList<NativeUnbindItemSection> sections)
        {
            Sections = new ReadOnlyCollection<NativeUnbindItemSection>(
                sections.ToArray());
            SectionCount = Sections.Count;

            var itemCount = 0;
            for (var index = 0; index < Sections.Count; index++)
                itemCount += Sections[index].Items.Count;
            ItemCount = itemCount;
        }

        public static NativeUnbindItemSnapshot Empty { get; } =
            new NativeUnbindItemSnapshot(
                Array.Empty<NativeUnbindItemSection>());

        public IReadOnlyList<NativeUnbindItemSection> Sections { get; }
        public int SectionCount { get; }
        public int ItemCount { get; }
        public bool IsEmpty => SectionCount == 0;
    }

    /// <summary>
    /// Strict GBK parser for the native UnBindItem.txt format.
    ///
    /// Parsing is performed into a new snapshot.  Callers can therefore
    /// reject a malformed file without disturbing the currently published
    /// configuration.
    /// </summary>
    public static class NativeUnbindItemParser
    {
        public static bool TryParse(string fileName,
            out NativeUnbindItemSnapshot snapshot, out string error)
        {
            snapshot = NativeUnbindItemSnapshot.Empty;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(fileName))
            {
                error = "UnBindItem.txt path is empty";
                return false;
            }

            string[] lines;
            try
            {
                // The original 2.08 files are code page 936, not UTF-8.
                lines = File.ReadAllLines(fileName, HUtil32.GbkEncoding);
            }
            catch (Exception ex)
            {
                error = "UnBindItem.txt read failed: " + ex.Message;
                return false;
            }

            return TryParseLines(lines, out snapshot, out error);
        }

        public static bool TryParseLines(IEnumerable<string> lines,
            out NativeUnbindItemSnapshot snapshot, out string error)
        {
            snapshot = NativeUnbindItemSnapshot.Empty;
            error = string.Empty;
            if (lines == null)
            {
                error = "UnBindItem.txt lines are null";
                return false;
            }

            var sections = new List<NativeUnbindItemSection>();
            List<NativeUnbindItemEntry> currentItems = null;
            string currentName = null;
            var lineNumber = 0;

            foreach (var sourceLine in lines)
            {
                lineNumber++;
                var line = (sourceLine ?? string.Empty).Trim();
                if (lineNumber == 1)
                    line = line.TrimStart('\uFEFF');

                if (line.Length == 0 || IsComment(line))
                    continue;

                if (line[0] == '[')
                {
                    if (!TryReadSectionHeader(line, out var sectionName))
                    {
                        error = $"UnBindItem.txt line {lineNumber}: invalid section header";
                        return false;
                    }

                    if (currentName != null)
                    {
                        sections.Add(new NativeUnbindItemSection(currentName,
                            currentItems));
                    }

                    currentName = sectionName;
                    currentItems = new List<NativeUnbindItemEntry>();
                    continue;
                }

                if (currentName == null)
                {
                    error = $"UnBindItem.txt line {lineNumber}: item appears before a section";
                    return false;
                }

                if (!TryReadEntry(line, out var entry, out var entryError))
                {
                    error = $"UnBindItem.txt line {lineNumber}: {entryError}";
                    return false;
                }

                currentItems.Add(entry);
            }

            if (currentName != null)
            {
                sections.Add(new NativeUnbindItemSection(currentName,
                    currentItems));
            }

            if (sections.Count == 0)
            {
                error = "UnBindItem.txt contains no sections";
                return false;
            }

            snapshot = new NativeUnbindItemSnapshot(sections);
            return true;
        }

        private static bool IsComment(string line)
        {
            return line[0] == ';' || line[0] == '#'
                || (line.Length >= 2 && line[0] == '/' && line[1] == '/');
        }

        private static bool TryReadSectionHeader(string line,
            out string sectionName)
        {
            sectionName = null;
            if (line.Length < 3 || line[line.Length - 1] != ']')
                return false;

            var name = line.Substring(1, line.Length - 2).Trim();
            if (name.Length == 0)
                return false;

            sectionName = name;
            return true;
        }

        private static bool TryReadEntry(string line,
            out NativeUnbindItemEntry entry, out string error)
        {
            entry = null;
            error = string.Empty;
            if (!TryTokenize(line, out var tokens, out error))
                return false;
            if (tokens.Count != 4)
            {
                error = "expected four fields (name and three integers)";
                return false;
            }
            if (tokens[0].Length == 0)
            {
                error = "item name is empty";
                return false;
            }

            if (!TryParseInteger(tokens[1], out var value1)
                || !TryParseInteger(tokens[2], out var value2)
                || !TryParseInteger(tokens[3], out var value3))
            {
                error = "item values must be signed 32-bit integers";
                return false;
            }

            entry = new NativeUnbindItemEntry(tokens[0], value1, value2,
                value3);
            return true;
        }

        private static bool TryTokenize(string line,
            out List<string> tokens, out string error)
        {
            tokens = new List<string>(4);
            error = string.Empty;
            var position = 0;
            while (position < line.Length)
            {
                while (position < line.Length
                    && char.IsWhiteSpace(line[position]))
                    position++;
                if (position >= line.Length)
                    break;

                if (line[position] == '"')
                {
                    position++;
                    var start = position;
                    while (position < line.Length && line[position] != '"')
                        position++;
                    if (position >= line.Length)
                    {
                        error = "unterminated quoted item name";
                        return false;
                    }

                    tokens.Add(line.Substring(start, position - start));
                    position++;
                    if (position < line.Length
                        && !char.IsWhiteSpace(line[position]))
                    {
                        error = "quoted item name must be followed by whitespace";
                        return false;
                    }
                    continue;
                }

                var tokenStart = position;
                while (position < line.Length
                    && !char.IsWhiteSpace(line[position]))
                    position++;
                tokens.Add(line.Substring(tokenStart,
                    position - tokenStart));
            }

            return true;
        }

        private static bool TryParseInteger(string text, out int value)
        {
            return int.TryParse(text, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out value);
        }
    }

    /// <summary>
    /// Atomic process-wide store used by the ReloadunBindItem command.
    /// </summary>
    public sealed class NativeUnbindItemConfig
    {
        public const string FileName = "UnBindItem.txt";

        private static readonly NativeUnbindItemConfig _shared =
            new NativeUnbindItemConfig();
        private readonly object _reloadGate = new object();
        private NativeUnbindItemSnapshot _snapshot =
            NativeUnbindItemSnapshot.Empty;

        public static NativeUnbindItemConfig Shared => _shared;

        public NativeUnbindItemSnapshot Snapshot =>
            Volatile.Read(ref _snapshot);
        public NativeUnbindItemSnapshot CurrentSnapshot => Snapshot;
        public IReadOnlyList<NativeUnbindItemSection> Sections =>
            Snapshot.Sections;
        public int SectionCount => Snapshot.SectionCount;

        public static string ResolveDefaultPath(string rootPath,
            string envirDir)
        {
            return Path.Combine(rootPath ?? string.Empty,
                envirDir ?? string.Empty, FileName);
        }

        public static string ResolveDefaultPath()
        {
            return ResolveDefaultPath(M2Share.sConfigPath,
                M2Share.g_Config?.sEnvirDir);
        }

        public bool TryReload(out int sectionCount, out string error)
        {
            return TryReload(ResolveDefaultPath(), out sectionCount,
                out error);
        }

        public bool TryReload(string fileName, out int sectionCount)
        {
            return TryReload(fileName, out sectionCount, out _);
        }

        public bool TryReload(string fileName, out int sectionCount,
            out string error)
        {
            sectionCount = 0;
            error = string.Empty;

            // Keep parse and publication under one gate.  A failed parse
            // exits before the volatile pointer is touched, preserving the
            // last known-good configuration for readers.
            lock (_reloadGate)
            {
                if (!NativeUnbindItemParser.TryParse(fileName,
                        out var candidate, out error))
                    return false;

                Volatile.Write(ref _snapshot, candidate);
                sectionCount = candidate.SectionCount;
                return true;
            }
        }

        public int Reload(string fileName, out string error)
        {
            return TryReload(fileName, out var sectionCount, out error)
                ? sectionCount
                : 0;
        }

        public int Reload(string fileName)
        {
            return Reload(fileName, out _);
        }
    }
}
