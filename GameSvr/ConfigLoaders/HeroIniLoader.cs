using System.Text;

namespace GameSvr.ConfigLoaders
{
    /// <summary>
    /// Simple INI loader for Hero.ini configuration
    /// Reads config/Hero.ini and provides fail-safe defaults on parse errors
    /// </summary>
    public class HeroIniLoader
    {
        private const string ConfigFileName = "Hero.ini";

        /// <summary>
        /// Hero configuration data class
        /// </summary>
        public class HeroConfig
        {
            public int MaxHeroLevel { get; set; } = 255;
            public int HeroRecallLevel { get; set; } = 40;
            public int HeroLearnSkillLevel { get; set; } = 19;
            public int HeroDieRecallTime { get; set; } = 3600;
            public int HeroLoyaltyDecRate { get; set; } = 1;
            public int HeroExpRate { get; set; } = 100;
            public bool EnableHeroAutoPickup { get; set; } = true;
            public bool EnableHeroGuard { get; set; } = true;
            public int HeroMaxBagWeight { get; set; } = 1000;
        }

        /// <summary>
        /// Loads Hero.ini configuration from config directory
        /// Returns default values if file not found or parse fails
        /// </summary>
        /// <param name="configDir">Configuration directory path</param>
        /// <returns>HeroConfig instance with loaded or default values</returns>
        public static HeroConfig LoadConfig(string configDir)
        {
            var config = new HeroConfig();
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

                    var parts = line.Split(new[] { '=' }, 2);
                    if (parts.Length != 2)
                        continue;

                    var key = parts[0].Trim();
                    var value = parts[1].Trim();

                    if (!ParseConfigEntry(config, key, value))
                    {
                        M2Share.MainOutMessage($"[配置] {ConfigFileName} 未识别的配置项: {key}");
                    }
                }

                M2Share.MainOutMessage($"[配置] {ConfigFileName} 加载完成");
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[错误] 加载 {ConfigFileName} 失败: {ex.Message}");
                M2Share.MainOutMessage("[配置] 使用默认英雄配置");
            }

            return config;
        }

        private static bool ParseConfigEntry(HeroConfig config, string key, string value)
        {
            switch (key)
            {
                case "MaxLevel":
                    if (int.TryParse(value, out var maxLevel))
                    {
                        config.MaxHeroLevel = maxLevel;
                        return true;
                    }
                    break;

                case "RecallLevel":
                    if (int.TryParse(value, out var recallLevel))
                    {
                        config.HeroRecallLevel = recallLevel;
                        return true;
                    }
                    break;

                case "LearnSkillLevel":
                    if (int.TryParse(value, out var skillLevel))
                    {
                        config.HeroLearnSkillLevel = skillLevel;
                        return true;
                    }
                    break;

                case "DieRecallTime":
                    if (int.TryParse(value, out var dieTime))
                    {
                        config.HeroDieRecallTime = dieTime;
                        return true;
                    }
                    break;

                case "LoyaltyDecRate":
                    if (int.TryParse(value, out var loyaltyRate))
                    {
                        config.HeroLoyaltyDecRate = loyaltyRate;
                        return true;
                    }
                    break;

                case "ExpRate":
                    if (int.TryParse(value, out var expRate))
                    {
                        config.HeroExpRate = expRate;
                        return true;
                    }
                    break;

                case "AutoPickup":
                    config.EnableHeroAutoPickup = value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    return true;

                case "GuardEnabled":
                    config.EnableHeroGuard = value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    return true;

                case "MaxBagWeight":
                    if (int.TryParse(value, out var maxWeight))
                    {
                        config.HeroMaxBagWeight = maxWeight;
                        return true;
                    }
                    break;

                default:
                    return false;
            }

            M2Share.MainOutMessage($"[配置] Hero.ini 配置项 {key} 解析失败: {value}");
            return false;
        }
    }
}
