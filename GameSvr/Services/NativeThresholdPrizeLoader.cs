using System.Collections.ObjectModel;
using System.Globalization;
using SystemModule;
using SystemModule.Common;

namespace GameSvr
{
    /// <summary>
    /// Shared threshold-weighted prize pool loader used by Fengyun, FireDragon,
    /// Temple Sky Gate, Hot Blood threshold tables, and similar native INI files.
    /// Native pattern: section = prefix + poolNumber, key = keyPrefix + entryNumber,
    /// value = descriptor/threshold (Random(100) or Random(1000) select).
    /// </summary>
    public sealed class NativeThresholdPrizeCatalog
    {
        public const int DefaultRandomRange = 100;
        public const int DefaultMaxEntries = 100;

        private readonly List<Entry>[] _pools;
        private readonly ReadOnlyCollection<Entry>[] _views;
        private readonly Func<int, int> _random;
        private readonly int _randomRange;

        private NativeThresholdPrizeCatalog(int poolCount, int randomRange,
            Func<int, int> random)
        {
            _randomRange = randomRange;
            _random = random;
            _pools = new List<Entry>[poolCount];
            _views = new ReadOnlyCollection<Entry>[poolCount];
            for (var i = 0; i < poolCount; i++)
            {
                _pools[i] = new List<Entry>(DefaultMaxEntries);
                _views[i] = _pools[i].AsReadOnly();
            }
        }

        public int PoolCount => _pools.Length;

        public IReadOnlyList<Entry> GetPool(int poolIndex)
        {
            if ((uint)(poolIndex - 1) >= (uint)_pools.Length)
                throw new ArgumentOutOfRangeException(nameof(poolIndex));
            return _views[poolIndex - 1];
        }

        public bool TrySelect(int poolIndex, out string descriptor)
        {
            descriptor = null;
            if ((uint)(poolIndex - 1) >= (uint)_pools.Length)
                return false;

            var pool = _pools[poolIndex - 1];
            if (pool.Count == 0)
                return false;

            var roll = _random(_randomRange);
            foreach (var entry in pool)
            {
                if (roll <= entry.Threshold)
                {
                    descriptor = entry.Descriptor;
                    return true;
                }
            }

            return false;
        }

        public static bool TryLoad(string fileName, int poolCount,
            string sectionPrefix, string keyPrefix, string configErrorTag,
            out NativeThresholdPrizeCatalog catalog, out string error,
            int randomRange = DefaultRandomRange,
            Func<int, int> random = null)
        {
            catalog = null;
            error = string.Empty;
            random ??= maximum => M2Share.RandomNumber.Random(maximum);

            if (string.IsNullOrWhiteSpace(fileName))
            {
                error = configErrorTag + " path is empty";
                return false;
            }

            catalog = new NativeThresholdPrizeCatalog(poolCount, randomRange,
                random);
            if (!File.Exists(fileName))
                return true;

            try
            {
                var ini = new ThresholdIni(fileName);
                for (var pool = 1; pool <= poolCount; pool++)
                {
                    var section = sectionPrefix + pool;
                    ReadPool(ini, section, keyPrefix, catalog._pools[pool - 1],
                        configErrorTag, out var valid);
                    if (!valid)
                    {
                        error = $"{configErrorTag}: section {section}";
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex) when (ex is IOException ||
                                       ex is UnauthorizedAccessException)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Loads simple item-name pools (no threshold) like Share/Platina.ini and
        /// Share/GoldID.ini: sectionPrefix + N, keys 奖励N.
        /// </summary>
        public static bool TryLoadSimplePools(string fileName, int poolCount,
            string sectionPrefix, string keyPrefix,
            out Dictionary<int, List<string>> pools, out string error)
        {
            pools = new Dictionary<int, List<string>>();
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                error = "simple prize path is empty";
                return false;
            }

            if (!File.Exists(fileName))
                return true;

            try
            {
                var ini = new ThresholdIni(fileName);
                for (var pool = 1; pool <= poolCount; pool++)
                {
                    var items = new List<string>();
                    var section = sectionPrefix + pool;
                    for (var entry = 1; entry <= DefaultMaxEntries; entry++)
                    {
                        var value = ini.ReadString(section, keyPrefix + entry, null);
                        if (string.IsNullOrWhiteSpace(value))
                            break;
                        items.Add(value);
                    }

                    pools[pool] = items;
                }

                return true;
            }
            catch (Exception ex) when (ex is IOException ||
                                       ex is UnauthorizedAccessException)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void ReadPool(ThresholdIni ini, string section,
            string keyPrefix, List<Entry> target, string configErrorTag,
            out bool valid)
        {
            valid = true;
            var lastThreshold = 0;
            for (var entryNumber = 1; entryNumber <= DefaultMaxEntries;
                 entryNumber++)
            {
                var value = ini.ReadString(section, keyPrefix + entryNumber,
                    null);
                if (string.IsNullOrWhiteSpace(value))
                    break;

                var separator = value.IndexOf('/');
                if (separator <= 0)
                {
                    LogConfigError(configErrorTag, value);
                    continue;
                }

                var descriptor = value[..separator];
                var thresholdText = value[(separator + 1)..].Trim();
                if (!int.TryParse(thresholdText, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out var threshold))
                {
                    LogConfigError(configErrorTag, value);
                    continue;
                }

                if (target.Count > 0 && threshold <= lastThreshold)
                {
                    valid = false;
                    LogConfigError(configErrorTag, section);
                    return;
                }

                target.Add(new Entry(descriptor, threshold));
                lastThreshold = threshold;
                if (lastThreshold >= DefaultRandomRange - 1)
                    break;
            }
        }

        private static void LogConfigError(string prefix, string suffix)
        {
            if (M2Share.LogSystem != null)
                M2Share.ErrorMessage("[Error]: " + prefix + "配置错误:[" +
                                     suffix + "]");
        }

        private sealed class ThresholdIni : IniFile
        {
            internal ThresholdIni(string fileName) : base(fileName)
            {
                try
                {
                    Load();
                }
                catch (Exception ex) when (ex.GetType() == typeof(Exception) &&
                    ConfigCount == 0 && string.Equals(ex.Message,
                        $"配置文件[{fileName}]不存在或配置文件内容为空。",
                        StringComparison.Ordinal))
                {
                }
            }
        }

        public sealed class Entry
        {
            internal Entry(string descriptor, int threshold)
            {
                Descriptor = descriptor;
                Threshold = threshold;
            }

            public string Descriptor { get; }
            public int Threshold { get; }
        }
    }
}
