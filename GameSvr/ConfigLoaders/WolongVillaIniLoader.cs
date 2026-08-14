using System.Globalization;
using System.Text;

namespace GameSvr.ConfigLoaders
{
    /// <summary>
    /// 卧龙山庄.ini configuration loader
    ///
    /// Loads Wolong Villa system configuration from config/卧龙山庄.ini
    /// Provides fail-safe defaults when config is missing or malformed
    /// </summary>
    public class WolongVillaIniLoader
    {
        private const string ConfigFileName = "卧龙山庄.ini";

        // Default configuration values
        public bool Enabled { get; set; } = true;
        public int EnterLevel { get; set; } = 40;
        public int MaxPlayers { get; set; } = 50;
        public int TimeLimit { get; set; } = 3600;
        public string MapName { get; set; } = "WolongVilla";
        public int RewardExpRate { get; set; } = 100;

        /// <summary>
        /// Loads 卧龙山庄.ini configuration from config directory
        /// </summary>
        /// <param name="configDir">Configuration directory path</param>
        /// <returns>Loaded configuration with defaults for missing/invalid values</returns>
        public static WolongVillaIniLoader LoadConfig(string configDir)
        {
            var config = new WolongVillaIniLoader();
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

        private static void ParseConfigValue(WolongVillaIniLoader config, string key, string value)
        {
            switch (key)
            {
                case "Enabled":
                    config.Enabled = ParseBool(value, config.Enabled);
                    break;
                case "EnterLevel":
                    config.EnterLevel = ParseInt(value, config.EnterLevel);
                    break;
                case "MaxPlayers":
                    config.MaxPlayers = ParseInt(value, config.MaxPlayers);
                    break;
                case "TimeLimit":
                    config.TimeLimit = ParseInt(value, config.TimeLimit);
                    break;
                case "MapName":
                    config.MapName = value;
                    break;
                case "RewardExpRate":
                    config.RewardExpRate = ParseInt(value, config.RewardExpRate);
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
