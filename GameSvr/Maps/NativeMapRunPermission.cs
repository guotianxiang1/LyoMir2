using SystemModule;

namespace GameSvr
{
    internal sealed class NativeMapDropItemRawState
    {
        private readonly object _syncRoot = new();
        private readonly List<string> _lines = new();
        private long _generation;

        internal long Generation
        {
            get
            {
                lock (_syncRoot)
                    return _generation;
            }
        }

        internal int Count
        {
            get
            {
                lock (_syncRoot)
                    return _lines.Count;
            }
        }

        internal IReadOnlyList<string> Snapshot()
        {
            lock (_syncRoot)
                return _lines.ToArray();
        }

        internal bool Contains(string itemName)
        {
            if (itemName == null) return false;
            lock (_syncRoot)
            {
                return _lines.Any(line => string.Equals(line, itemName,
                    StringComparison.OrdinalIgnoreCase));
            }
        }

        internal void BeginReload(string line)
        {
            lock (_syncRoot)
            {
                _lines.Clear();
                _lines.Add(line);
                _generation = unchecked(_generation + 1);
            }
        }

        internal void Append(string line)
        {
            lock (_syncRoot)
                _lines.Add(line);
        }

        internal void Clear()
        {
            lock (_syncRoot)
            {
                _lines.Clear();
                _generation = unchecked(_generation + 1);
            }
        }
    }

    internal static class NativeMapDropTrackingGeneration
    {
        private static long _switchGeneration;

        internal static long SwitchGeneration =>
            Interlocked.Read(ref _switchGeneration);

        internal static void SwitchChanged()
        {
            Interlocked.Increment(ref _switchGeneration);
        }
    }

    internal static class NativeMapRunPermission
    {
        internal static bool TryLoad(string envirDirectory,
            Envirnoment environment, bool enabled, out string error)
        {
            error = string.Empty;
            if (!enabled || environment == null ||
                string.IsNullOrEmpty(envirDirectory) ||
                string.IsNullOrEmpty(environment.sMapName))
            {
                return false;
            }

            var fileName = Path.Combine(envirDirectory, "MonItems",
                "MapDropItem_" + environment.sMapName + ".txt");
            if (!File.Exists(fileName))
                return false;

            var firstOrdinaryLine = true;
            var loadedOrdinaryLine = false;
            try
            {
                foreach (var rawLine in File.ReadLines(fileName,
                             HUtil32.GbkEncoding))
                {
                    var line = rawLine ?? string.Empty;
                    if (line.Length == 0 || line[0] is '#' or ';')
                        continue;

                    if (line[0] == '[')
                    {
                        var end = line.IndexOf(']');
                        if (end <= 1)
                            continue;

                        var tokens = line.Substring(1, end - 1).Split(
                            new[] { '\t', ',', ' ' },
                            StringSplitOptions.RemoveEmptyEntries);
                        foreach (var token in tokens)
                        {
                            if (token.Equals("NORUN",
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                environment.NativeCanRunWhileOverweight = false;
                            }
                            else if (token.Equals("CANRUN",
                                         StringComparison.OrdinalIgnoreCase))
                            {
                                environment.NativeCanRunWhileOverweight = true;
                            }
                        }
                        continue;
                    }

                    if (firstOrdinaryLine)
                    {
                        environment.NativeMapDropItems.BeginReload(line);
                        firstOrdinaryLine = false;
                    }
                    else
                    {
                        environment.NativeMapDropItems.Append(line);
                    }
                    loadedOrdinaryLine = true;
                }
                return loadedOrdinaryLine;
            }
            catch (Exception exception) when (exception is IOException ||
                                               exception is UnauthorizedAccessException ||
                                               exception is ArgumentException ||
                                               exception is NotSupportedException)
            {
                error = fileName + ": " + exception.Message;
                return false;
            }
        }

        // Compatibility parser used by the run3 audit. Production map loading
        // uses the environment overload above so raw lines remain map-owned.
        internal static bool TryLoad(string envirDirectory, string mapName,
            out bool canRunWhileOverweight, out string error)
        {
            return TryLoadCore(envirDirectory, mapName, false,
                out canRunWhileOverweight, out error);
        }

        internal static bool TryReload(string envirDirectory, string mapName,
            out bool canRunWhileOverweight, out string error)
        {
            return TryLoadCore(envirDirectory, mapName, true,
                out canRunWhileOverweight, out error);
        }

        private static bool TryLoadCore(string envirDirectory, string mapName,
            bool requireFile, out bool canRunWhileOverweight,
            out string error)
        {
            canRunWhileOverweight = true;
            error = string.Empty;
            if (string.IsNullOrEmpty(envirDirectory) ||
                string.IsNullOrEmpty(mapName))
            {
                error = "MapDropItem directory or map name is empty";
                return !requireFile;
            }

            var fileName = Path.Combine(envirDirectory, "MonItems",
                "MapDropItem_" + mapName + ".txt");
            if (!File.Exists(fileName))
            {
                if (requireFile)
                    error = fileName + ": file not found";
                return !requireFile;
            }

            var environment = new Envirnoment { sMapName = mapName };
            _ = TryLoad(envirDirectory, environment, true, out error);
            canRunWhileOverweight =
                environment.NativeCanRunWhileOverweight;
            return string.IsNullOrEmpty(error);
        }
    }
}
