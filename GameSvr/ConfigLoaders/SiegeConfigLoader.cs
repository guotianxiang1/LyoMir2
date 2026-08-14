using System;
using System.IO;
using SystemModule.Common;

namespace GameSvr.ConfigLoaders
{
    /// <summary>
    /// Simple INI loader for 攻城 (Siege) configuration
    /// Reads config/攻城.ini and returns default values on parse failure
    /// </summary>
    public class SiegeConfigLoader : IniFile
    {
        private const string ConfigFileName = "config/攻城.ini";

        public SiegeConfigLoader(string filePath) : base(filePath)
        {
        }

        /// <summary>
        /// Loads Siege configuration from config/攻城.ini
        /// Returns default configuration if file is missing or parsing fails
        /// </summary>
        public static SiegeConfig LoadConfig(string baseDir)
        {
            var config = new SiegeConfig();
            var configPath = Path.Combine(baseDir, ConfigFileName);

            if (!File.Exists(configPath))
            {
                M2Share.MainOutMessage($"[配置] {ConfigFileName} 不存在，使用默认值");
                return config;
            }

            try
            {
                var loader = new SiegeConfigLoader(configPath);
                loader.Load();

                // Read configuration values with default fallbacks
                config.Enabled = loader.ReadBool("System", "Enabled", true);
                config.SiegeDurationMinutes = loader.ReadInteger("System", "SiegeDurationMinutes", 120);
                config.MinGuildMembers = loader.ReadInteger("System", "MinGuildMembers", 10);
                config.RegistrationFee = loader.ReadInteger("System", "RegistrationFee", 1000000);
                config.WinnerReward = loader.ReadInteger("Rewards", "WinnerReward", 5000000);
                config.DefenderAdvantage = loader.ReadInteger("Balance", "DefenderAdvantage", 10);

                M2Share.MainOutMessage($"[配置] 成功加载 {ConfigFileName}");
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[错误] 加载 {ConfigFileName} 失败: {ex.Message}，使用默认值");
            }

            return config;
        }
    }

    /// <summary>
    /// Siege configuration data class
    /// </summary>
    public class SiegeConfig
    {
        public bool Enabled { get; set; } = true;
        public int SiegeDurationMinutes { get; set; } = 120;
        public int MinGuildMembers { get; set; } = 10;
        public int RegistrationFee { get; set; } = 1000000;
        public int WinnerReward { get; set; } = 5000000;
        public int DefenderAdvantage { get; set; } = 10;
    }
}
