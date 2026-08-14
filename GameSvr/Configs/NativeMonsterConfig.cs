namespace GameSvr
{
    /// <summary>
    /// Native Monster.ini configuration loader (EA 0x00610A94)
    ///
    /// Evidence: sub_610A94 in M2Server unpacked image
    /// - Loads monster system configuration from Monster.ini
    /// - Configures monster spawn rates, AI behaviors, drop modifiers
    /// - Provides fail-safe defaults when config missing
    ///
    /// Status: Placeholder implementation (core body deferred)
    /// Config structure needs reverse engineering from binary
    /// </summary>
    public class NativeMonsterConfig
    {
        public const uint LoaderFunctionEa = 0x00610A94;
        private const string ConfigFileName = "Monster.ini";

        // Placeholder default values (need verification from sub_610A94)
        public int MonsterRegenInterval { get; set; } = 1000; // ms
        public int MonsterSearchRange { get; set; } = 12;     // cells
        public int MonsterAttackSpeed { get; set; } = 1000;   // ms
        public double MonsterExpRate { get; set; } = 1.0;     // multiplier
        public double MonsterDropRate { get; set; } = 1.0;    // multiplier
        public bool EnableMonsterRevive { get; set; } = false;
        public int MonsterMaxReviveTimes { get; set; } = 0;
        public int MonsterAiTickInterval { get; set; } = 500; // ms
        public bool EnableBossNotification { get; set; } = true;
        public int MonsterMaxCount { get; set; } = 5000;

        /// <summary>
        /// Loads Monster.ini configuration from EnvirDir
        /// </summary>
        public static NativeMonsterConfig Load(string envirDir)
        {
            var config = new NativeMonsterConfig();
            var configPath = Path.Combine(envirDir, ConfigFileName);

            if (!File.Exists(configPath))
            {
                M2Share.MainOutMessage($"[配置] {ConfigFileName} 不存在，使用默认值");
                return config;
            }

            try
            {
                // TODO: Parse Monster.ini format (INI file structure)
                // Native loader sub_610A94 reads specific sections/keys
                // Need to reverse engineer exact field names and sections

                // Placeholder: Expected ini structure
                // [Monster]
                // RegenInterval=1000
                // SearchRange=12
                // AttackSpeed=1000
                // ExpRate=1.0
                // DropRate=1.0
                // EnableRevive=0
                // MaxReviveTimes=0
                // AiTickInterval=500
                // BossNotification=1
                // MaxCount=5000

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
                        case "RegenInterval":
                            if (int.TryParse(value, out var regenInterval))
                                config.MonsterRegenInterval = regenInterval;
                            break;
                        case "SearchRange":
                            if (int.TryParse(value, out var searchRange))
                                config.MonsterSearchRange = searchRange;
                            break;
                        case "AttackSpeed":
                            if (int.TryParse(value, out var attackSpeed))
                                config.MonsterAttackSpeed = attackSpeed;
                            break;
                        case "ExpRate":
                            if (double.TryParse(value, out var expRate))
                                config.MonsterExpRate = expRate;
                            break;
                        case "DropRate":
                            if (double.TryParse(value, out var dropRate))
                                config.MonsterDropRate = dropRate;
                            break;
                        case "EnableRevive":
                            config.EnableMonsterRevive = value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
                            break;
                        case "MaxReviveTimes":
                            if (int.TryParse(value, out var maxRevive))
                                config.MonsterMaxReviveTimes = maxRevive;
                            break;
                        case "AiTickInterval":
                            if (int.TryParse(value, out var aiTick))
                                config.MonsterAiTickInterval = aiTick;
                            break;
                        case "BossNotification":
                            config.EnableBossNotification = value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
                            break;
                        case "MaxCount":
                            if (int.TryParse(value, out var maxCount))
                                config.MonsterMaxCount = maxCount;
                            break;
                    }
                }

                M2Share.MainOutMessage($"[配置] {ConfigFileName} 加载完成");
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[错误] 加载 {ConfigFileName} 失败: {ex.Message}");
                M2Share.MainOutMessage("[配置] 使用默认怪物配置");
            }

            return config;
        }

        /// <summary>
        /// Validates loaded configuration values
        /// </summary>
        public bool Validate()
        {
            // TODO: Apply native validation rules from sub_610A94
            // - RegenInterval: 100-60000 ms
            // - SearchRange: 1-50 cells
            // - ExpRate/DropRate: 0.0-10.0 multipliers
            // - MaxCount: reasonable limits

            if (MonsterRegenInterval < 100 || MonsterRegenInterval > 60000)
            {
                M2Share.MainOutMessage($"[警告] Monster.ini RegenInterval={MonsterRegenInterval} 超出范围，重置为1000");
                MonsterRegenInterval = 1000;
            }

            if (MonsterSearchRange < 1 || MonsterSearchRange > 50)
            {
                M2Share.MainOutMessage($"[警告] Monster.ini SearchRange={MonsterSearchRange} 超出范围，重置为12");
                MonsterSearchRange = 12;
            }

            if (MonsterExpRate < 0.0 || MonsterExpRate > 10.0)
            {
                M2Share.MainOutMessage($"[警告] Monster.ini ExpRate={MonsterExpRate} 超出范围，重置为1.0");
                MonsterExpRate = 1.0;
            }

            if (MonsterDropRate < 0.0 || MonsterDropRate > 10.0)
            {
                M2Share.MainOutMessage($"[警告] Monster.ini DropRate={MonsterDropRate} 超出范围，重置为1.0");
                MonsterDropRate = 1.0;
            }

            return true;
        }
    }
}
