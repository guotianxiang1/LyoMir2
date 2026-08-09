using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using SystemModule.Common;

namespace LoginGate.Core;

public sealed class LoginGateConfig
{
    public const int DefaultLoginGateListen = 7000;
    public const int DefaultDbServerListen = 5600;
    public const int DefaultPigServerListen = 7766;
    public const int MaxAreaIndex = ushort.MaxValue;
    public const int MaxAreaSlots = 300;
    public const int MaxGroupSlots = 32;
    public const int MaxGroupIndex = byte.MaxValue;

    private static readonly Encoding Gbk = CreateGbkEncoding();
    private readonly LoginGateArea _primaryArea = new(1);
    private PreservingIniDocument _document = new();

    public LoginGateConfig()
    {
        Areas.Add(_primaryArea);
        DbServerAddresses.Add(new LoginGateDbServerAddress(1, "127.0.0.1"));
    }

    public string? FileName { get; private set; }
    public int LoginGateListen { get; set; } = DefaultLoginGateListen;
    public int DBServerListen { get; set; } = DefaultDbServerListen;
    public int PIGServerListen { get; set; } = DefaultPigServerListen;
    public string PIGServerIP { get; set; } = "0.0.0.0";
    public int Project { get; set; }
    public bool SecondZone { get; set; }
    public bool DenySpreader { get; set; }
    public bool PKWarning { get; set; }
    public bool DebugMode { get; set; }
    public string CompressMode { get; set; } = string.Empty;

    /// <summary>
    /// [Login]/TicketDb — MySQL connection string for the operator's own account
    /// database, used to verify login tickets against account.ticket (the native
    /// LoginCenter /verify equivalent). Empty means ticket authentication is
    /// disabled (fail-closed). Read-only from the ini and never auto-written, so a
    /// credential placed here is preserved but not injected into fresh configs.
    /// Server-side secret — do not commit real credentials.
    /// </summary>
    public string TicketDb { get; set; } = string.Empty;
    public int AreaIdx
    {
        get => _primaryArea.AreaIdx;
        set => _primaryArea.AreaIdx = value;
    }
    public string Suffix
    {
        get => _primaryArea.Suffix;
        set => _primaryArea.Suffix = value;
    }
    public List<LoginGateDbServerAddress> DbServerAddresses { get; } = [];
    public List<LoginGateGroup> Groups => _primaryArea.Groups;
    public List<LoginGateArea> Areas { get; } = [];

    public IReadOnlyList<LoginGateArea> GetConfiguredAreas() =>
        EnumerateAreas().OrderBy(area => area.Slot).ToArray();

    public LoginGateArea? FindArea(uint areaIndex)
    {
        if (areaIndex == 0) return _primaryArea;
        return EnumerateAreas().FirstOrDefault(area =>
            area.AreaIdx == areaIndex && area.Groups.Count != 0);
    }

    public static LoginGateConfig Load(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("A LoginGate.ini file name is required.", nameof(fileName));
        }

        var fullPath = Path.GetFullPath(fileName);
        var config = new LoginGateConfig { FileName = fullPath };
        if (!File.Exists(fullPath))
        {
            return config;
        }

        var text = Gbk.GetString(File.ReadAllBytes(fullPath));
        config._document = PreservingIniDocument.Parse(text);
        config.ReadKnownValues();
        config.ThrowIfInvalid();
        return config;
    }

    public void Save(string? fileName = null)
    {
        var target = fileName ?? FileName;
        if (string.IsNullOrWhiteSpace(target))
        {
            throw new InvalidOperationException("No LoginGate.ini target file has been selected.");
        }

        ThrowIfInvalid();
        WriteKnownValues();

        var fullPath = Path.GetFullPath(target);
        AtomicFile.WriteAllText(fullPath, _document.Render(), Gbk);
        FileName = fullPath;
    }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        ValidatePort(LoginGateListen, "Setup/LoginGateListen", errors);
        ValidatePort(DBServerListen, "Setup/DBServerListen", errors);
        ValidatePort(PIGServerListen, "Setup/PIGServerListen", errors);
        if (!TryParseIpv4(PIGServerIP, out _))
        {
            errors.Add("Setup/PIGServerIP is not a dotted-decimal IPv4 address.");
        }

        if (Project is < 0 or > 1)
        {
            errors.Add("Setup/Project must be 0 or 1.");
        }

        if (ContainsLineBreak(CompressMode))
        {
            errors.Add("Setup/CompressMode cannot contain a line break.");
        }
        else if (!CanEncodeGbk(CompressMode))
        {
            errors.Add("Setup/CompressMode contains text that cannot be encoded as GBK.");
        }
        var addressSlots = new HashSet<int>();
        foreach (var address in DbServerAddresses)
        {
            if (address.Slot < 1)
            {
                errors.Add($"DBServerIP address slot {address.Slot} must be positive.");
            }
            else if (!addressSlots.Add(address.Slot))
            {
                errors.Add($"DBServerIP address slot {address.Slot} is duplicated.");
            }

            if (!TryParseIpv4(address.Address, out _))
            {
                errors.Add($"DBServerIP/IPAddress{address.Slot} is not a dotted-decimal IPv4 address.");
            }
        }

        var areaSlots = new HashSet<int>();
        var areaIndexes = new HashSet<int>();
        foreach (var area in EnumerateAreas())
        {
            var section = $"Area{area.Slot}";
            if (area.Slot is < 1 or > MaxAreaSlots)
            {
                errors.Add($"{section} slot must be between 1 and {MaxAreaSlots}.");
            }
            else if (!areaSlots.Add(area.Slot))
            {
                errors.Add($"{section} slot is duplicated.");
            }

            if (area.AreaIdx is < 0 or > MaxAreaIndex)
            {
                errors.Add($"{section}/AreaIdx must be between 0 and {MaxAreaIndex}.");
            }
            else if (area.AreaIdx != 0 && !areaIndexes.Add(area.AreaIdx))
            {
                errors.Add($"{section}/AreaIdx {area.AreaIdx} is duplicated.");
            }

            if (ContainsLineBreak(area.Suffix))
            {
                errors.Add($"{section}/Suffix cannot contain a line break.");
            }
            else if (!CanEncodeGbk(area.Suffix))
            {
                errors.Add($"{section}/Suffix contains text that cannot be encoded as GBK.");
            }
            else if (Gbk.GetByteCount(area.Suffix) > 7)
            {
                errors.Add($"{section}/Suffix exceeds the 7-byte GBK field.");
            }

            var groupSlots = new HashSet<int>();
            foreach (var group in area.Groups)
            {
                ValidateGroup(section, group, groupSlots, errors);
            }
        }

        return errors;
    }

    public void ThrowIfInvalid()
    {
        var errors = Validate();
        if (errors.Count != 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        }
    }

    public static bool TryParseIpv4(string? value, out IPAddress address)
    {
        address = IPAddress.None;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('.');
        if (parts.Length != 4)
        {
            return false;
        }

        foreach (var part in parts)
        {
            if (part.Length == 0 || part.Any(character => character is < '0' or > '9') ||
                !byte.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                return false;
            }
        }

        if (!IPAddress.TryParse(value, out var parsed) ||
            parsed.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        address = parsed;
        return true;
    }

    private void ReadKnownValues()
    {
        LoginGateListen = ReadInt("Setup", "LoginGateListen", LoginGateListen);
        DBServerListen = ReadInt("Setup", "DBServerListen", DBServerListen);
        PIGServerListen = ReadInt("Setup", "PIGServerListen", PIGServerListen);
        PIGServerIP = _document.GetValue("Setup", "PIGServerIP") ?? PIGServerIP;
        Project = ReadInt("Setup", "Project", Project);
        SecondZone = ReadBool("Setup", "SecondZone", SecondZone);
        DenySpreader = ReadBool("Setup", "DenySpreader", DenySpreader);
        PKWarning = ReadBool("Setup", "PK_Warning", PKWarning);
        DebugMode = ReadBool("Setup", "DebugMode", DebugMode);
        CompressMode = _document.GetValue("Setup", "CompressMode") ?? CompressMode;
        // Read-only: operator-supplied account DB connection for ticket verification.
        // Not echoed by WriteKnownValues so credentials are never auto-persisted;
        // PreservingIniDocument keeps any existing line across a load/save cycle.
        TicketDb = _document.GetValue("Login", "TicketDb") ?? TicketDb;
        ReadDbServerAddresses();
        ReadAreas();
    }

    private void ReadDbServerAddresses()
    {
        var addresses = new Dictionary<int, string>();
        foreach (var entry in _document.GetEntries("DBServerIP"))
        {
            if (TryReadIndexedKey(entry.Key, "IPAddress", string.Empty, out var slot))
            {
                addresses[slot] = entry.Value;
            }
        }

        if (addresses.Count == 0)
        {
            return;
        }

        DbServerAddresses.Clear();
        foreach (var address in addresses.OrderBy(item => item.Key))
        {
            DbServerAddresses.Add(new LoginGateDbServerAddress(address.Key, address.Value));
        }
    }

    private void ReadAreas()
    {
        Areas.Clear();
        Areas.Add(_primaryArea);
        ReadArea(_primaryArea, allowMissingIndex: true);
        for (var slot = 2; slot <= MaxAreaSlots; slot++)
        {
            var section = $"Area{slot}";
            var rawIndex = _document.GetValue(section, "AreaIdx");
            if (string.IsNullOrWhiteSpace(rawIndex)) break;
            var areaIndex = ParseInt(rawIndex, $"{section}/AreaIdx");
            if (areaIndex == 0) break;
            var area = new LoginGateArea(slot) { AreaIdx = areaIndex };
            ReadArea(area, allowMissingIndex: false);
            Areas.Add(area);
        }
    }

    private void ReadArea(LoginGateArea area, bool allowMissingIndex)
    {
        var section = $"Area{area.Slot}";
        if (allowMissingIndex)
            area.AreaIdx = ReadInt(section, "AreaIdx", area.AreaIdx);
        area.Suffix = _document.GetValue(section, "Suffix") ?? area.Suffix;
        var groups = new Dictionary<int, LoginGateGroup>();
        foreach (var entry in _document.GetEntries(section))
        {
            if (TryReadIndexedKey(entry.Key, "group", "name", out var nameSlot))
            {
                GetOrCreateGroup(groups, nameSlot).Name = entry.Value;
            }
            else if (TryReadIndexedKey(entry.Key, "group", "Desc", out var descriptionSlot))
            {
                GetOrCreateGroup(groups, descriptionSlot).Description = entry.Value;
            }
            else if (TryReadIndexedKey(entry.Key, "group", "idx", out var indexSlot))
            {
                GetOrCreateGroup(groups, indexSlot).Index = ParseInt(
                    entry.Value, $"{section}/{entry.Key}");
            }
            else if (TryReadIndexedKey(entry.Key, "group", "DBS", out var dbServerSlot))
            {
                GetOrCreateGroup(groups, dbServerSlot).DbServerName = entry.Value;
            }
        }

        area.Groups.Clear();
        area.Groups.AddRange(groups.OrderBy(item => item.Key).Select(item => item.Value));
    }

    private void WriteKnownValues()
    {
        _document.SetValue("Setup", "LoginGateListen", FormatInt(LoginGateListen));
        _document.SetValue("Setup", "DBServerListen", FormatInt(DBServerListen));
        _document.SetValue("Setup", "PIGServerListen", FormatInt(PIGServerListen));
        _document.SetValue("Setup", "PIGServerIP", PIGServerIP);
        _document.SetValue("Setup", "Project", FormatInt(Project));
        _document.SetValue("Setup", "SecondZone", FormatBool(SecondZone));
        _document.SetValue("Setup", "DenySpreader", FormatBool(DenySpreader));
        _document.SetValue("Setup", "PK_Warning", FormatBool(PKWarning));
        _document.SetValue("Setup", "DebugMode", FormatBool(DebugMode));
        _document.SetValue("Setup", "CompressMode", CompressMode ?? string.Empty);

        var addressSlots = DbServerAddresses.Select(address => address.Slot).ToHashSet();
        _document.RemoveEntries("DBServerIP", key =>
            TryReadIndexedKey(key, "IPAddress", string.Empty, out var slot) &&
            !addressSlots.Contains(slot));
        foreach (var address in DbServerAddresses.OrderBy(address => address.Slot))
        {
            _document.SetValue("DBServerIP", $"IPAddress{address.Slot}", address.Address);
        }

        foreach (var area in EnumerateAreas().OrderBy(area => area.Slot))
        {
            var section = $"Area{area.Slot}";
            _document.SetValue(section, "AreaIdx", FormatInt(area.AreaIdx));
            _document.SetValue(section, "Suffix", area.Suffix ?? string.Empty);
            var groupSlots = area.Groups.Select(group => group.Slot).ToHashSet();
            _document.RemoveEntries(section, key =>
                TryReadGroupKey(key, out var slot) && !groupSlots.Contains(slot));
            foreach (var group in area.Groups.OrderBy(group => group.Slot))
            {
                _document.SetValue(section, $"group{group.Slot}DBS", group.DbServerName);
                _document.SetValue(section, $"group{group.Slot}name", group.Name);
                _document.SetValue(section, $"group{group.Slot}Desc", group.Description);
                _document.SetValue(section, $"group{group.Slot}idx", FormatInt(group.Index));
            }
        }
    }

    private IEnumerable<LoginGateArea> EnumerateAreas()
    {
        yield return _primaryArea;
        foreach (var area in Areas)
        {
            if (!ReferenceEquals(area, _primaryArea)) yield return area;
        }
    }

    private static void ValidateGroup(string section, LoginGateGroup group,
        ISet<int> groupSlots, ICollection<string> errors)
    {
        if (group.Slot is < 1 or > MaxGroupSlots)
            errors.Add($"{section} group slot {group.Slot} must be between 1 and {MaxGroupSlots}.");
        else if (!groupSlots.Add(group.Slot))
            errors.Add($"{section} group slot {group.Slot} is duplicated.");

        if (group.Index is < 0 or > MaxGroupIndex)
            errors.Add($"{section}/group{group.Slot}idx must be between 0 and {MaxGroupIndex}.");
        ValidateGbkField(group.DbServerName, 15, false,
            $"{section}/group{group.Slot}DBS", errors);
        ValidateGbkField(group.Name, 15, true,
            $"{section}/group{group.Slot}name", errors);
        ValidateGbkField(group.Description, 15, true,
            $"{section}/group{group.Slot}Desc", errors);
    }

    private static void ValidateGbkField(string? value, int maximumBytes,
        bool required, string field, ICollection<string> errors)
    {
        if (required && string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{field} must be non-empty.");
            return;
        }
        if (ContainsLineBreak(value))
            errors.Add($"{field} cannot contain a line break.");
        else if (!CanEncodeGbk(value))
            errors.Add($"{field} contains text that cannot be encoded as GBK.");
        // Aligned to native: LoginGate LoadConfig NEVER rejects an over-long server
        // name/desc/DBS. It truncates via StrPLCopy(...,15) (and DebugOut-warns for
        // DBS only: uServerInfo.pas:266-270), then continues. The wire layer
        // (LoginGateWireProtocol.TryWriteGbkCString) now reproduces that byte-level
        // truncation, so a misconfigured >maximumBytes field must not break gate
        // startup (ThrowIfInvalid runs in the LoginGateServer ctor). The prior hard
        // reject on length was a native divergence and a crash risk; removed.
        // maximumBytes is retained to document the native StrPLCopy limit per field.
        _ = maximumBytes;
    }

    private int ReadInt(string section, string key, int defaultValue)
    {
        var value = _document.GetValue(section, key);
        return string.IsNullOrWhiteSpace(value) ? defaultValue : ParseInt(value, $"{section}/{key}");
    }

    private bool ReadBool(string section, string key, bool defaultValue)
    {
        var value = _document.GetValue(section, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            "0" or "false" or "no" or "off" => false,
            _ => throw new InvalidDataException($"{section}/{key} must be a boolean value (0 or 1).")
        };
    }

    private static int ParseInt(string value, string field)
    {
        if (!int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            throw new InvalidDataException($"{field} must be an integer.");
        }
        return result;
    }

    private static void ValidatePort(int value, string field, ICollection<string> errors)
    {
        if (value is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
        {
            errors.Add($"{field} must be between {IPEndPoint.MinPort} and {IPEndPoint.MaxPort}.");
        }
    }

    private static LoginGateGroup GetOrCreateGroup(IDictionary<int, LoginGateGroup> groups, int slot)
    {
        if (!groups.TryGetValue(slot, out var group))
        {
            group = new LoginGateGroup(slot, 0, string.Empty, string.Empty, string.Empty);
            groups.Add(slot, group);
        }
        return group;
    }

    private static bool TryReadGroupKey(string key, out int slot) =>
        TryReadIndexedKey(key, "group", "name", out slot) ||
        TryReadIndexedKey(key, "group", "Desc", out slot) ||
        TryReadIndexedKey(key, "group", "idx", out slot) ||
        TryReadIndexedKey(key, "group", "DBS", out slot);

    private static bool TryReadIndexedKey(string key, string prefix, string suffix, out int slot)
    {
        slot = 0;
        if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var digitsLength = key.Length - prefix.Length - suffix.Length;
        if (digitsLength <= 0)
        {
            return false;
        }

        return int.TryParse(key.AsSpan(prefix.Length, digitsLength), NumberStyles.None,
            CultureInfo.InvariantCulture, out slot);
    }

    private static bool ContainsLineBreak(string? value) =>
        value?.IndexOfAny(['\r', '\n']) >= 0;

    private static bool CanEncodeGbk(string? value)
    {
        try
        {
            Gbk.GetByteCount(value ?? string.Empty);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    private static string FormatBool(bool value) => value ? "1" : "0";

    private static string FormatInt(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static Encoding CreateGbkEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(936, EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);
    }

    private sealed class PreservingIniDocument
    {
        private readonly List<string> _lines;

        public PreservingIniDocument()
            : this([])
        {
        }

        private PreservingIniDocument(List<string> lines)
        {
            _lines = lines;
        }

        public static PreservingIniDocument Parse(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return new PreservingIniDocument();
            }

            var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n');
            return new PreservingIniDocument(normalized.Split('\n').ToList());
        }

        public string? GetValue(string section, string key)
        {
            string? result = null;
            foreach (var entry in GetEntries(section))
            {
                if (entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    result = entry.Value;
                }
            }
            return result;
        }

        public IEnumerable<IniEntry> GetEntries(string section)
        {
            var currentSection = string.Empty;
            var inBlockComment = false;
            foreach (var line in _lines)
            {
                if (ConsumeBlockComment(line, ref inBlockComment))
                {
                    continue;
                }
                if (TryReadSection(line, out var parsedSection))
                {
                    currentSection = parsedSection;
                }
                else if (currentSection.Equals(section, StringComparison.OrdinalIgnoreCase) &&
                         TryReadEntry(line, out var key, out var value, out _, out _))
                {
                    yield return new IniEntry(key, value);
                }
            }
        }

        public void SetValue(string section, string key, string value)
        {
            var currentSection = string.Empty;
            var matchingLine = -1;
            var inBlockComment = false;
            for (var index = 0; index < _lines.Count; index++)
            {
                if (ConsumeBlockComment(_lines[index], ref inBlockComment))
                {
                    continue;
                }
                if (TryReadSection(_lines[index], out var parsedSection))
                {
                    currentSection = parsedSection;
                }
                else if (currentSection.Equals(section, StringComparison.OrdinalIgnoreCase) &&
                         TryReadEntry(_lines[index], out var parsedKey, out _,
                             out var valueStart, out var valueEnd) &&
                         parsedKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    matchingLine = index;
                }
            }

            if (matchingLine >= 0 && TryReadEntry(_lines[matchingLine], out _, out _,
                    out var start, out var end))
            {
                var line = _lines[matchingLine];
                _lines[matchingLine] = line[..start] + value + line[end..];
                return;
            }

            InsertEntry(section, key, value);
        }

        public void RemoveEntries(string section, Func<string, bool> predicate)
        {
            var matchingLines = new List<int>();
            var currentSection = string.Empty;
            var inBlockComment = false;
            for (var index = 0; index < _lines.Count; index++)
            {
                if (ConsumeBlockComment(_lines[index], ref inBlockComment))
                {
                    continue;
                }
                if (TryReadSection(_lines[index], out var parsedSection))
                {
                    currentSection = parsedSection;
                }
                else if (currentSection.Equals(section, StringComparison.OrdinalIgnoreCase) &&
                         TryReadEntry(_lines[index], out var key, out _, out _, out _) &&
                         predicate(key))
                {
                    matchingLines.Add(index);
                }
            }

            for (var index = matchingLines.Count - 1; index >= 0; index--)
            {
                _lines.RemoveAt(matchingLines[index]);
            }
        }

        public string Render()
        {
            if (_lines.Count == 0)
            {
                return string.Empty;
            }

            var result = string.Join("\r\n", _lines);
            return result.EndsWith("\r\n", StringComparison.Ordinal) ? result : result + "\r\n";
        }

        private void InsertEntry(string section, string key, string value)
        {
            var sectionStart = -1;
            var sectionEnd = -1;
            var inBlockComment = false;
            for (var index = 0; index < _lines.Count; index++)
            {
                if (ConsumeBlockComment(_lines[index], ref inBlockComment))
                {
                    continue;
                }
                if (!TryReadSection(_lines[index], out var parsedSection))
                {
                    continue;
                }

                if (parsedSection.Equals(section, StringComparison.OrdinalIgnoreCase))
                {
                    sectionStart = index;
                    sectionEnd = _lines.Count;
                }
                else if (sectionStart >= 0 && sectionEnd == _lines.Count)
                {
                    sectionEnd = index;
                }
            }

            if (sectionStart < 0)
            {
                if (_lines.Count == 1 && _lines[0].Length == 0)
                {
                    _lines.Clear();
                }
                if (_lines.Count > 0 && _lines[^1].Length != 0)
                {
                    _lines.Add(string.Empty);
                }
                _lines.Add($"[{section}]");
                _lines.Add($"{key}={value}");
                return;
            }

            while (sectionEnd > sectionStart + 1 && string.IsNullOrWhiteSpace(_lines[sectionEnd - 1]))
            {
                sectionEnd--;
            }
            _lines.Insert(sectionEnd, $"{key}={value}");
        }

        private static bool ConsumeBlockComment(string line, ref bool inBlockComment)
        {
            var trimmed = line.TrimStart();
            if (inBlockComment)
            {
                if (trimmed.StartsWith("*/", StringComparison.Ordinal))
                {
                    inBlockComment = false;
                }
                return true;
            }

            if (!trimmed.StartsWith("/*", StringComparison.Ordinal))
            {
                return false;
            }

            inBlockComment = !trimmed.Contains("*/", StringComparison.Ordinal);
            return true;
        }

        private static bool TryReadSection(string line, out string section)
        {
            section = string.Empty;
            var trimmed = line.Trim();
            if (trimmed.Length < 3 || IsComment(trimmed) || trimmed[0] != '[')
            {
                return false;
            }

            var close = trimmed.IndexOf(']');
            if (close <= 1)
            {
                return false;
            }

            section = trimmed[1..close].Trim();
            return section.Length != 0;
        }

        private static bool TryReadEntry(string line, out string key, out string value,
            out int valueStart, out int valueEnd)
        {
            key = string.Empty;
            value = string.Empty;
            valueStart = 0;
            valueEnd = 0;

            var trimmedStart = line.AsSpan().TrimStart();
            if (trimmedStart.Length == 0 || IsComment(trimmedStart))
            {
                return false;
            }

            var equals = line.IndexOf('=');
            if (equals <= 0)
            {
                return false;
            }

            key = line[..equals].Trim();
            if (key.Length == 0)
            {
                return false;
            }

            valueStart = equals + 1;
            while (valueStart < line.Length && line[valueStart] is ' ' or '\t')
            {
                valueStart++;
            }

            valueEnd = FindInlineComment(line, valueStart);
            while (valueEnd > valueStart && line[valueEnd - 1] is ' ' or '\t')
            {
                valueEnd--;
            }
            value = line[valueStart..valueEnd];
            return true;
        }

        private static int FindInlineComment(string line, int start)
        {
            var quoted = false;
            for (var index = start; index < line.Length; index++)
            {
                if (line[index] == '"')
                {
                    quoted = !quoted;
                    continue;
                }
                if (quoted)
                {
                    continue;
                }

                var marker = line[index] is ';' or '#';
                var doubledSemicolon = line[index] == ';' && index + 1 < line.Length &&
                                       line[index + 1] == ';';
                if (marker && (doubledSemicolon || index == start || char.IsWhiteSpace(line[index - 1])))
                {
                    return index;
                }
            }
            return line.Length;
        }

        private static bool IsComment(ReadOnlySpan<char> line) =>
            line[0] is ';' or '#' or '!';

        public readonly record struct IniEntry(string Key, string Value);
    }
}

public sealed class LoginGateDbServerAddress
{
    public LoginGateDbServerAddress(int slot, string address)
    {
        Slot = slot;
        Address = address;
    }

    public int Slot { get; set; }
    public string Address { get; set; }
}

public sealed class LoginGateGroup
{
    public LoginGateGroup(int slot, int index, string name, string description,
        string dbServerName = "")
    {
        Slot = slot;
        Index = index;
        Name = name;
        Description = description;
        DbServerName = dbServerName;
    }

    public int Slot { get; set; }
    public int Index { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string DbServerName { get; set; }
}

public sealed class LoginGateArea
{
    public LoginGateArea(int slot)
    {
        Slot = slot;
    }

    public int Slot { get; set; }
    public int AreaIdx { get; set; }
    public string Suffix { get; set; } = string.Empty;
    public List<LoginGateGroup> Groups { get; } = [];
}
