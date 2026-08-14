namespace GameSvr
{
    /// <summary>
    /// Native Hero.ini configuration loader (EA 0x00621710)
    ///
    /// Evidence: sub_621710 in M2Server unpacked image
    /// - Loads hero system configuration from Hero.ini
    /// - Validates hero-related parameters (level caps, costs, abilities)
    /// - Provides fail-safe defaults when config missing
    ///
    /// Status: Placeholder implementation (core body deferred)
    /// Config structure needs reverse engineering from binary
    /// </summary>
    public class NativeHeroConfig
    {
        public const uint LoaderFunctionEa = 0x00621710;
        private const string ConfigFileName = "Hero.ini";

        // Placeholder default values (need verification from sub_621710)
        public int MaxHeroLevel { get; set; } = 255;
        public int HeroRecallLevel { get; set; } = 40;
        public int HeroLearnSkillLevel { get; set; } = 19;
        public int HeroDieRecallTime { get; set; } = 3600; // seconds
        public int HeroLoyaltyDecRate { get; set; } = 1;
        public int HeroExpRate { get; set; } = 100; // percentage
        public bool EnableHeroAutoPickup { get; set; } = true;
        public bool EnableHeroGuard { get; set; } = true;
        public int HeroMaxBagWeight { get; set; } = 1000;

        /// <summary>
        /// Loads Hero.ini configuration from EnvirDir
        /// </summary>
        public static NativeHeroConfig Load(string envirDir)
        {
            var config = new NativeHeroConfig();
            var configPath = Path.Combine(envirDir, ConfigFileName);

            if (!File.Exists(configPath))
            {
                M2Share.MainOutMessage($"[配置] {ConfigFileName} 不存在，使用默认值");
                return config;
            }

            try
            {
                // TODO: Parse Hero.ini format (INI file structure)
                // Native loader sub_621710 reads specific sections/keys
                // Need to reverse engineer exact field names and sections

                // Placeholder: Read ini sections
                // [Hero]
                // MaxLevel=255
                // RecallLevel=40
                // LearnSkillLevel=19
                // DieRecallTime=3600
                // LoyaltyDecRate=1
                // ExpRate=100
                // AutoPickup=1
                // GuardEnabled=1
                // MaxBagWeight=1000

                var lines = File.ReadAllLines(configPath, System.Text.Encoding.GetEncoding("GBK"));
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";") || line.StartsWith("#"))
                        continue;

                    var parts = line.Split('=');
                    if (parts.Length != 2)
                        continue;

                    var key = parts[0].Trim();
                    var value = parts[1].Trim();

                    // Placeholder parsing (actual keys need verification)
                    switch (key)
                    {
                        case "MaxLevel":
                            if (int.TryParse(value, out var maxLevel))
                                config.MaxHeroLevel = maxLevel;
                            break;
                        case "RecallLevel":
                            if (int.TryParse(value, out var recallLevel))
                                config.HeroRecallLevel = recallLevel;
                            break;
                        case "LearnSkillLevel":
                            if (int.TryParse(value, out var skillLevel))
                                config.HeroLearnSkillLevel = skillLevel;
                            break;
                        case "DieRecallTime":
                            if (int.TryParse(value, out var dieTime))
                                config.HeroDieRecallTime = dieTime;
                            break;
                        case "LoyaltyDecRate":
                            if (int.TryParse(value, out var loyaltyRate))
                                config.HeroLoyaltyDecRate = loyaltyRate;
                            break;
                        case "ExpRate":
                            if (int.TryParse(value, out var expRate))
                                config.HeroExpRate = expRate;
                            break;
                        case "AutoPickup":
                            config.EnableHeroAutoPickup = value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
                            break;
                        case "GuardEnabled":
                            config.EnableHeroGuard = value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
                            break;
                        case "MaxBagWeight":
                            if (int.TryParse(value, out var maxWeight))
                                config.HeroMaxBagWeight = maxWeight;
                            break;
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

        /// <summary>
        /// Validates loaded configuration values
        /// </summary>
        public bool Validate()
        {
            // TODO: Apply native validation rules from sub_621710
            // - MaxHeroLevel: 1-255
            // - RecallLevel: must be <= MaxHeroLevel
            // - DieRecallTime: reasonable range (60-7200 seconds)
            // - ExpRate: 1-1000 percentage

            if (MaxHeroLevel < 1 || MaxHeroLevel > 255)
            {
                M2Share.MainOutMessage($"[警告] Hero.ini MaxLevel={MaxHeroLevel} 超出范围，重置为255");
                MaxHeroLevel = 255;
            }

            if (HeroRecallLevel > MaxHeroLevel)
            {
                M2Share.MainOutMessage($"[警告] Hero.ini RecallLevel={HeroRecallLevel} 超过MaxLevel，已调整");
                HeroRecallLevel = MaxHeroLevel;
            }

            return true;
        }
    }
}
