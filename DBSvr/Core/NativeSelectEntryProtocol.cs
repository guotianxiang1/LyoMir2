using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DBSvr.Core
{
    public enum NativeSelectEntryStatus
    {
        Normal = 1,
        MustRename = 2,
        NameNotAllowed = 3,
        Denied = 4
    }

    /// <summary>
    /// Native 2.08 character-entry name gates and rename continuation state.
    /// </summary>
    public sealed class NativeSelectEntryProtocol
    {
        private const uint DeniedReloadIntervalMilliseconds = 30000;
        private const uint EntryConfigReloadIntervalMilliseconds = 30000;
        private readonly string _baseDirectory;
        private readonly object _sync = new();
        private HashSet<string> _deniedNames = new(StringComparer.Ordinal);
        private HashSet<string> _renameNames = new(StringComparer.Ordinal);
        // The native AdminList TStringList comparer is not linked to its
        // constructor in the recovered image. Keep this table ordinal until
        // that field is proven; do not silently invent case folding here.
        private HashSet<string> _adminNames = new(StringComparer.Ordinal);
        private string _newZoneTemplate = string.Empty;
        private long _deniedFileTimestamp = long.MinValue;
        private long _ipAddressFileTimestamp = long.MinValue;
        private long _adminFileTimestamp = long.MinValue;
        private uint _deniedReloadTick;
        private uint _entryConfigReloadTick;

        public NativeSelectEntryProtocol()
            : this(AppContext.BaseDirectory)
        {
        }

        public NativeSelectEntryProtocol(string baseDirectory)
        {
            _baseDirectory = string.IsNullOrEmpty(baseDirectory)
                ? AppContext.BaseDirectory
                : Path.GetFullPath(baseDirectory);
            Load();
        }

        public void Load()
        {
            ReloadDeniedNamesCore();
            _deniedReloadTick = unchecked((uint)Environment.TickCount);

            ReloadEntryConfigCore();
            _entryConfigReloadTick = unchecked((uint)Environment.TickCount);

            lock (_sync)
                _renameNames = new HashSet<string>(StringComparer.Ordinal);
            var rename = LoadRenameNames(Path.Combine(_baseDirectory,
                "Rename", "HumName.txt"));
            lock (_sync)
                _renameNames = rename;
        }

        public NativeSelectEntryStatus Classify(string characterName)
        {
            var now = unchecked((uint)Environment.TickCount);
            ReloadDeniedNamesIfDue(now);
            ReloadEntryConfigIfDue(now);
            if (string.IsNullOrEmpty(characterName))
                return NativeSelectEntryStatus.Normal;

            var key = Normalize(characterName);
            lock (_sync)
            {
                // fn_5C8D04 checks TBaseSQLDB+0x8C before +0x88.
                if (_deniedNames.Contains(key))
                    return NativeSelectEntryStatus.Denied;
                if (_renameNames.Contains(key))
                    return NativeSelectEntryStatus.MustRename;
                // fn_5C8D04 checks the non-empty NewZone template before
                // querying TStringList(Self+0x3C). A missing/empty template
                // therefore leaves this gate inactive.
                if (!string.IsNullOrEmpty(_newZoneTemplate)
                    && !_adminNames.Contains(characterName))
                    return NativeSelectEntryStatus.NameNotAllowed;
            }
            return NativeSelectEntryStatus.Normal;
        }

        /// <summary>
        /// Formats the native IpAddress.txt NewZone template. The recovered
        /// handler passes exactly one AnsiString argument to SysUtils.Format;
        /// this method implements the proven %s case and leaves malformed
        /// templates fail-closed for the caller.
        /// </summary>
        public bool TryFormatNewZoneNotice(string characterName,
            out string notice)
        {
            notice = string.Empty;
            if (characterName == null) characterName = string.Empty;

            string template;
            lock (_sync) template = _newZoneTemplate;
            if (string.IsNullOrEmpty(template)) return false;

            var marker = template.IndexOf("%s",
                StringComparison.OrdinalIgnoreCase);
            if (marker < 0)
            {
                // Delphi Format accepts a format string with no conversion;
                // retain the configured text rather than inventing a body.
                notice = template;
                return true;
            }

            notice = template.Substring(0, marker) + characterName
                + template.Substring(marker + 2);
            return true;
        }

        public static void BeginRenamePrompt(TUserInfo userInfo,
            string characterName)
        {
            if (userInfo == null) return;
            userInfo.NativePendingRenameName = characterName;
        }

        public static string BeginRenameContinuation(TUserInfo userInfo)
        {
            if (userInfo == null) return null;
            userInfo.NativeRenameLatch = 1;
            return userInfo.NativePendingRenameName;
        }

        public static void CompleteRename(TUserInfo userInfo)
        {
            if (userInfo == null) return;
            userInfo.NativeRenameLatch = 1;
            userInfo.NativePendingRenameName = null;
        }

        public static void CompleteSelection(TUserInfo userInfo,
            string characterName)
        {
            if (userInfo == null) return;
            userInfo.NativeCurrentCharName = characterName;
        }

        internal void ReloadDeniedNamesIfDue(uint now)
        {
            lock (_sync)
            {
                if (!IsDeniedReloadDue(now, _deniedReloadTick)) return;
                _deniedReloadTick = now;
            }
            ReloadDeniedNamesCore();
        }

        internal void ReloadEntryConfigIfDue(uint now)
        {
            lock (_sync)
            {
                if (unchecked(now - _entryConfigReloadTick)
                    < EntryConfigReloadIntervalMilliseconds)
                    return;
                _entryConfigReloadTick = now;
            }
            ReloadEntryConfigCore();
        }

        internal static bool IsDeniedReloadDue(uint now, uint previous) =>
            unchecked(now - previous) >= DeniedReloadIntervalMilliseconds;

        private void ReloadDeniedNamesCore()
        {
            var fileName = Path.Combine(_baseDirectory, "!DenyLogon.txt");
            if (!File.Exists(fileName)) return;

            long timestamp;
            try
            {
                timestamp = File.GetLastWriteTimeUtc(fileName).Ticks;
            }
            catch
            {
                return;
            }

            lock (_sync)
            {
                if (timestamp == _deniedFileTimestamp) return;

                // fn_5C2560 clears TBaseSQLDB+0x8C before LoadFromFile.
                _deniedNames = new HashSet<string>(StringComparer.Ordinal);
                try
                {
                    var loaded = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var rawLine in File.ReadAllLines(fileName,
                                 Encoding.GetEncoding(936)))
                    {
                        var line = TrimNative(rawLine);
                        if (string.IsNullOrEmpty(line) || line[0] == ';')
                            continue;
                        loaded.Add(Normalize(line));
                    }
                    _deniedNames = loaded;
                    _deniedFileTimestamp = timestamp;
                }
                catch
                {
                    // fn_5C2560 catches read conflicts, leaves the table empty,
                    // and retains the old timestamp so the next pass retries.
                }
            }
        }

        private void ReloadEntryConfigCore()
        {
            var ipFile = Path.Combine(_baseDirectory, "IpAddress.txt");
            var adminFile = ResolveAdminListFile();

            var ipStamp = GetFileTimestamp(ipFile);
            var adminStamp = GetFileTimestamp(adminFile);
            lock (_sync)
            {
                if (ipStamp == _ipAddressFileTimestamp
                    && adminStamp == _adminFileTimestamp)
                    return;
            }

            var template = LoadNewZoneTemplate(ipFile);
            var admins = LoadAdminNames(adminFile);
            lock (_sync)
            {
                _newZoneTemplate = template;
                _adminNames = admins;
                _ipAddressFileTimestamp = ipStamp;
                _adminFileTimestamp = adminStamp;
            }
        }

        private string ResolveAdminListFile()
        {
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(DBShare.sMapFile))
            {
                var mapDirectory = Path.GetDirectoryName(DBShare.sMapFile);
                if (!string.IsNullOrEmpty(mapDirectory))
                    candidates.Add(Path.Combine(mapDirectory,
                        "AdminList.txt"));
            }

            candidates.Add(Path.Combine(_baseDirectory,
                "Mir200", "Envir", "AdminList.txt"));
            candidates.Add(Path.GetFullPath(Path.Combine(_baseDirectory,
                "..", "Mir200", "Envir", "AdminList.txt")));
            candidates.Add(Path.Combine(_baseDirectory,
                "Envir", "AdminList.txt"));
            candidates.Add(Path.Combine(_baseDirectory, "AdminList.txt"));

            foreach (var candidate in candidates)
                if (File.Exists(candidate)) return candidate;
            return candidates[^1];
        }

        private static long GetFileTimestamp(string fileName)
        {
            try
            {
                return File.Exists(fileName)
                    ? File.GetLastWriteTimeUtc(fileName).Ticks
                    : long.MinValue;
            }
            catch
            {
                return long.MinValue;
            }
        }

        private static string LoadNewZoneTemplate(string fileName)
        {
            if (!File.Exists(fileName)) return string.Empty;
            try
            {
                var lines = File.ReadAllLines(fileName,
                    Encoding.GetEncoding(936));
                // Native fn_5C5178 walks from the last line to the first and
                // overwrites the field on every match; consequently the first
                // duplicate key in file order wins.
                var result = string.Empty;
                for (var i = lines.Length - 1; i >= 0; i--)
                {
                    var line = TrimNative(lines[i]);
                    if (string.IsNullOrEmpty(line)
                        || line[0] is ';' or '#')
                        continue;

                    var equals = line.IndexOf('=');
                    if (equals <= 0) continue;
                    var key = TrimNative(line.Substring(0, equals));
                    if (!string.Equals(key, "NewZone",
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    var value = TrimNative(line[(equals + 1)..]);
                    if (value.EndsWith("*", StringComparison.Ordinal))
                        value = TrimNative(value[..^1]);
                    result = value;
                }
                return result;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static HashSet<string> LoadAdminNames(string fileName)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (!File.Exists(fileName)) return result;
            try
            {
                foreach (var rawLine in File.ReadAllLines(fileName,
                             Encoding.GetEncoding(936)))
                {
                    var line = TrimNative(rawLine);
                    if (string.IsNullOrEmpty(line)
                        || line[0] is '#' or '/' or ';' or '\\')
                        continue;

                    var tokens = line.Split(new[] { ',', ' ', '\t' },
                        StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length < 2) continue;

                    // fn_5C5554 accepts a first token whose first byte is
                    // '*', '1', or '2', then adds the second token to the
                    // TStringList at Self+0x3C.
                    var marker = tokens[0];
                    if (marker.Length == 0
                        || (marker[0] != '*' && marker[0] != '1'
                            && marker[0] != '2'))
                        continue;
                    result.Add(TrimNative(tokens[1]));
                }
            }
            catch
            {
                // Native startup catches file/decoder errors and leaves the
                // previous table untouched. On initial load an empty table is
                // the only safe equivalent.
            }
            return result;
        }

        private static HashSet<string> LoadRenameNames(string fileName)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (!File.Exists(fileName)) return result;

            var lines = File.ReadAllLines(fileName, Encoding.GetEncoding(936));
            for (var i = lines.Length - 1; i >= 0; i--)
            {
                var line = lines[i];
                if (string.IsNullOrEmpty(line) || line[0] == ';')
                    continue;
                result.Add(Normalize(line));
            }
            return result;
        }

        private static string TrimNative(string value)
        {
            var start = 0;
            var end = value.Length;
            while (start < end && value[start] <= ' ') start++;
            while (end > start && value[end - 1] <= ' ') end--;
            return value.Substring(start, end - start);
        }

        private static string Normalize(string value)
        {
            var chars = value.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
                if (chars[i] is >= 'a' and <= 'z') chars[i] -= (char)32;
            return new string(chars);
        }
    }
}
