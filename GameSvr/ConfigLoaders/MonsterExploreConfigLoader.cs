using System.Globalization;
using System.Text;

namespace GameSvr.ConfigLoaders
{
    /// <summary>
    /// MonsterExplore.ini configuration loader
    ///
    /// Loads monster exploration system configuration from config/MonsterExplore.ini
    /// Provides fail-safe defaults when config is missing or malformed
    /// </summary>
    public class MonsterExploreConfigLoader
    {
        private const string ConfigFileName = "MonsterExplore.ini";

        // Default configuration values
        public bool Enabled { get; set; } = true;
        public int ExploreRadius { get; set; } = 15;
        public int ExploreInterval { get; set; } = 3000;
        public int MaxExploreTargets { get; set; } = 10;
        public bool EnablePathfinding { get; set; } = true;
        public int PathfindingMaxSteps { get; set; } = 50;
        public bool LogExploreActivity { get; set; } = false;

        /// <summary>
        /// Loads MonsterExplore.ini configuration from config directory
        /// </summary>
        /// <param name="configDir">Configuration directory path</param>
        /// <returns>Loaded configuration with defaults for missing/invalid values</returns>
        public static MonsterExploreConfigLoader LoadConfig(string configDir)
        {
            var config = new MonsterExploreConfigLoader();
            var configPath = Path.Combine(configDir, ConfigFileName);

            if (!File.Exists(configPath))
            {
                M2Share.MainOutMessage($"[配置] {ConfigFileName} 不存在，使用默认值");
                return config;
            }

            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                var lines = File.ReadAllLines(configPath, Encoding.GetEncoding("GBK"));

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith(";") || line.TrimStart().StartsWith("#"))
                        continue;

                    var trimmedLine = line.Trim();
                    var equalIndex = trimmedLine.IndexOf('=');
                    if (equalIndex <= 0)
                        continue;

                    var key = trimmedLine[..equalIndex].Trim();
                    var value = trimmedLine[(equalIndex + 1)..].Trim();

                    if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                        continue;

                    try
                    {
                        ParseConfigValue(config, key, value);
                    }
                    catch (Exception ex)
                    {
                        M2Share.MainOutMessage($"[配置] {ConfigFileName} 解析失败 [{key}={value}]: {ex.Message}");
                    }
                }

                M2Share.MainOutMessage($"[配置] {ConfigFileName} 加载完成");
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[配置] {ConfigFileName} 读取失败: {ex.Message}，使用默认值");
            }

            return config;
        }

        private static void ParseConfigValue(MonsterExploreConfigLoader config, string key, string value)
        {
            switch (key)
            {
                case "Enabled":
                    config.Enabled = ParseBool(value, config.Enabled);
                    break;
                case "ExploreRadius":
                    config.ExploreRadius = ParseInt(value, config.ExploreRadius);
                    break;
                case "ExploreInterval":
                    config.ExploreInterval = ParseInt(value, config.ExploreInterval);
                    break;
                case "MaxExploreTargets":
                    config.MaxExploreTargets = ParseInt(value, config.MaxExploreTargets);
                    break;
                case "EnablePathfinding":
                    config.EnablePathfinding = ParseBool(value, config.EnablePathfinding);
                    break;
                case "PathfindingMaxSteps":
                    config.PathfindingMaxSteps = ParseInt(value, config.PathfindingMaxSteps);
                    break;
                case "LogExploreActivity":
                    config.LogExploreActivity = ParseBool(value, config.LogExploreActivity);
                    break;
                default:
                    // Ignore unknown keys silently
                    break;
            }
        }

        private static int ParseInt(string value, int defaultValue)
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
                return result;
            return defaultValue;
        }

        private static bool ParseBool(string value, bool defaultValue)
        {
            if (bool.TryParse(value, out var boolResult))
                return boolResult;

            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intResult))
                return intResult != 0;

            return defaultValue;
        }
    }
}
