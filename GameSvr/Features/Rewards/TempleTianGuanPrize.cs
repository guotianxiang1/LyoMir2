using System.Globalization;
using SystemModule;

namespace GameSvr.Features.Rewards
{
    /// <summary>
    /// 天关奖励系统 (Temple TianGuan Prize System)
    /// 闯天关 - Battle through Heaven's Gates dungeon reward distribution
    ///
    /// Reverse-engineered from 战神引擎 M2Server
    /// Configuration-driven prize selection with threshold-based probability
    /// </summary>
    public sealed class TempleTianGuanPrize
    {
        // Configuration file paths (relative to Share/config/)
        private const string ConfigExperienceFile = "NewExp.ini";
        private const string ConfigServerPrizeFile = "NewServPrize.ini";
        private const string ConfigPersonalPrizeFile = "NewSelfPrize.ini";

        // Prize tier item names
        private const string PrizeTierWood = "木天赐";
        private const string PrizeTierBronze = "铜天赐";
        private const string PrizeTierSilver = "银天赐";
        private const string PrizeTierGold = "金天赐";
        private const string PrizeHidden = "神秘天赐";

        // Diamond prize special handling
        private const string DiamondHundredPrize = "金刚石:100";
        private const string DiamondLogName = "金刚宝石";
        private const string DiamondLogReason = "闯天关大奖";

        // Tier thresholds (defeated monster count)
        private const int TierWoodMax = 40;
        private const int TierBronzeMin = 41;
        private const int TierBronzeMax = 46;
        private const int TierSilverMin = 47;
        private const int TierSilverMax = 49;
        private const int TierGoldMin = 50;
        private const int TierGoldMax = 80;
        private const int HiddenPrizeThreshold = 47;

        private static readonly object SyncRoot = new();
        private static TempleTianGuanPrizeCatalog s_Catalog;

        private TempleTianGuanPrize()
        {
        }

        /// <summary>
        /// Initialize prize catalog from configuration files at startup
        /// Must be called once during server initialization
        /// Native: Loaded at startup into static catalog
        /// </summary>
        public static void Initialize(string rootPath)
        {
            lock (SyncRoot)
            {
                s_Catalog = LoadCatalog(rootPath);
            }
        }

        /// <summary>
        /// Calculate experience reward based on configured range
        /// Native: Experience span calculation with 10000-unit rounding
        /// Formula: minimum + (random(span) / 10000 * 10000)
        /// </summary>
        public static int CalculateExperienceReward(Func<int, int> randomFunc)
        {
            var catalog = CaptureCatalog();
            var minimum = catalog.MinimumExperience;
            var maximum = catalog.MaximumExperience;
            var span = unchecked(maximum - minimum);
            var addition = randomFunc(span);
            return unchecked(minimum + addition / 10_000 * 10_000);
        }

        /// <summary>
        /// Determine prize tier based on defeated monster count
        /// Native: Tier classification ranges [41-46], [47-49], [50-80]
        /// Defeated count bands map to: 木/铜/银/金天赐
        /// </summary>
        public static string DeterminePrizeTier(int defeatedCount)
        {
            if (defeatedCount >= TierGoldMin && defeatedCount <= TierGoldMax)
                return PrizeTierGold;
            if (defeatedCount >= TierSilverMin && defeatedCount <= TierSilverMax)
                return PrizeTierSilver;
            if (defeatedCount >= TierBronzeMin && defeatedCount <= TierBronzeMax)
                return PrizeTierBronze;
            return PrizeTierWood;
        }

        /// <summary>
        /// Check if player qualifies for all-killed bonus
        /// Native: [50, 80] range awards gold tier and increments counter
        /// </summary>
        public static bool IsAllKilledRange(int defeatedCount)
        {
            return defeatedCount >= TierGoldMin && defeatedCount <= TierGoldMax;
        }

        /// <summary>
        /// Check if player qualifies for hidden prize
        /// Native: Mystery flag active and defeated >= 47
        /// </summary>
        public static bool QualifiesForHiddenPrize(int defeatedCount)
        {
            return defeatedCount >= HiddenPrizeThreshold;
        }

        /// <summary>
        /// Select prize from threshold entries using roll value
        /// Native: Inclusive threshold comparison (roll &lt;= threshold)
        /// Returns first matching entry or empty string if no match
        /// </summary>
        public static string SelectThresholdPrize(
            IReadOnlyList<ThresholdEntry> entries,
            int roll)
        {
            for (var index = 0; index < entries.Count; index++)
            {
                if (roll <= entries[index].Threshold)
                    return entries[index].Descriptor;
            }
            return string.Empty;
        }

        /// <summary>
        /// Select server prize for specific route
        /// Native: 5 routes with separate prize tables (配置1-配置5)
        /// Route range: [1, 5]
        /// </summary>
        public static string SelectServerPrize(int route, int roll)
        {
            var catalog = CaptureCatalog();
            if (route < 1 || route > 5) return string.Empty;
            return SelectThresholdPrize(catalog.ServerPrizes[route - 1], roll);
        }

        /// <summary>
        /// Select personal prize (hundredth entry bonus)
        /// Native: Single personal prize table (配置)
        /// </summary>
        public static string SelectPersonalPrize(int roll)
        {
            var catalog = CaptureCatalog();
            return SelectThresholdPrize(catalog.PersonalPrizes, roll);
        }

        /// <summary>
        /// Load prize configuration from INI files
        /// Native: GBK-encoded INI files in Share/config/
        /// Config sections: [配置] for exp/personal, [配置1-5] for server
        /// Entry format: 爆物N=ItemName/Threshold
        /// </summary>
        private static TempleTianGuanPrizeCatalog LoadCatalog(string rootPath)
        {
            var result = new TempleTianGuanPrizeCatalog();
            try
            {
                if (string.IsNullOrEmpty(rootPath)) return result;

                var configPath = Path.Combine(Path.GetFullPath(rootPath),
                    "Share", "config");

                // Load experience configuration
                var expValues = ReadIniSection(
                    Path.Combine(configPath, ConfigExperienceFile),
                    "配置");
                result.MinimumExperience = ParseInteger(
                    expValues.TryGetValue("最小经验", out var minimum)
                        ? minimum : null);
                result.MaximumExperience = ParseInteger(
                    expValues.TryGetValue("最大经验", out var maximum)
                        ? maximum : null);

                // Load server prize tables (5 routes)
                var serverPath = Path.Combine(configPath, ConfigServerPrizeFile);
                for (var route = 0; route < result.ServerPrizes.Length; route++)
                {
                    result.ServerPrizes[route] = ReadThresholdEntries(
                        serverPath,
                        "配置" + (route + 1));
                }

                // Load personal prize table
                result.PersonalPrizes = ReadThresholdEntries(
                    Path.Combine(configPath, ConfigPersonalPrizeFile),
                    "配置");

                return result;
            }
            catch (Exception e)
            {
                try
                {
                    M2Share.ErrorMessage(
                        "[Exception]:TempleTianGuanPrize.LoadCatalog: " +
                        e.Message);
                }
                catch
                {
                    // Suppress nested exception
                }
                return new TempleTianGuanPrizeCatalog();
            }
        }

        /// <summary>
        /// Read INI section with GBK encoding
        /// Native: Manual INI parser, GBK-encoded text files
        /// Supports ; and # comments, [Section] headers, Key=Value format
        /// </summary>
        private static Dictionary<string, string> ReadIniSection(
            string path,
            string wantedSection)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return result;

            string currentSection = string.Empty;
            foreach (var rawLine in File.ReadLines(path, HUtil32.GbkEncoding))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line[0] is ';' or '#') continue;

                if (line[0] == '[' && line[^1] == ']')
                {
                    currentSection = line[1..^1].Trim();
                    continue;
                }

                if (!string.Equals(currentSection, wantedSection,
                        StringComparison.Ordinal)) continue;

                var equals = line.IndexOf('=');
                if (equals <= 0) continue;

                result[line[..equals].Trim()] = line[(equals + 1)..].Trim();
            }
            return result;
        }

        /// <summary>
        /// Parse threshold entries from INI section
        /// Native: Entry key format "爆物N", value format "ItemName/Threshold"
        /// Sorted by index N, filtered for valid descriptors
        /// </summary>
        private static ThresholdEntry[] ReadThresholdEntries(
            string path,
            string section)
        {
            var values = ReadIniSection(path, section);
            return values
                .Where(pair => pair.Key.StartsWith("爆物",
                    StringComparison.Ordinal))
                .Select(pair => new
                {
                    Pair = pair,
                    Index = ParseInteger(pair.Key.AsSpan(2).ToString())
                })
                .OrderBy(entry => entry.Index)
                .Select(entry =>
                {
                    var separator = entry.Pair.Value.LastIndexOf('/');
                    if (separator <= 0)
                        return new ThresholdEntry(string.Empty, int.MinValue);
                    return new ThresholdEntry(
                        entry.Pair.Value[..separator].Trim(),
                        ParseInteger(entry.Pair.Value[(separator + 1)..]));
                })
                .Where(entry => entry.Descriptor.Length != 0)
                .ToArray();
        }

        /// <summary>
        /// Parse integer with invariant culture
        /// Native: Integer parsing, returns 0 on failure
        /// </summary>
        private static int ParseInteger(string value)
        {
            return int.TryParse(value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var result) ? result : 0;
        }

        /// <summary>
        /// Capture current catalog snapshot (thread-safe)
        /// Native: Catalog loaded at startup, cached for runtime access
        /// </summary>
        private static TempleTianGuanPrizeCatalog CaptureCatalog()
        {
            lock (SyncRoot)
            {
                s_Catalog ??= LoadCatalog(M2Share.sRootPath);
                return s_Catalog;
            }
        }

        /// <summary>
        /// Prize catalog container
        /// Native: Static catalog structure with experience range and prize tables
        /// </summary>
        private sealed class TempleTianGuanPrizeCatalog
        {
            internal TempleTianGuanPrizeCatalog()
            {
                for (var route = 0; route < ServerPrizes.Length; route++)
                    ServerPrizes[route] = Array.Empty<ThresholdEntry>();
            }

            internal int MinimumExperience { get; set; }
            internal int MaximumExperience { get; set; }
            internal ThresholdEntry[][] ServerPrizes { get; } =
                new ThresholdEntry[5][];
            internal ThresholdEntry[] PersonalPrizes { get; set; } =
                Array.Empty<ThresholdEntry>();
        }

        /// <summary>
        /// Threshold prize entry
        /// Native: Item descriptor with inclusive threshold value
        /// Roll &lt;= Threshold triggers selection
        /// </summary>
        public readonly record struct ThresholdEntry(
            string Descriptor,
            int Threshold);
    }
}
