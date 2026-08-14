using System;
using System.IO;
using SystemModule.Common;

namespace GameSvr.ConfigLoaders
{
    /// <summary>
    /// Simple INI loader for 荣耀系统 (Honor System) configuration
    /// Reads config/荣耀系统.ini and returns default values on parse failure
    /// </summary>
    public class HonorSystemIniLoader : IniFile
    {
        private const string ConfigFileName = "config/荣耀系统.ini";

        public HonorSystemIniLoader(string filePath) : base(filePath)
        {
        }

        /// <summary>
        /// Loads Honor System configuration from config/荣耀系统.ini
        /// Returns default configuration if file is missing or parsing fails
        /// </summary>
        public static HonorSystemConfig LoadConfig(string baseDir)
        {
            var config = new HonorSystemConfig();
            var configPath = Path.Combine(baseDir, ConfigFileName);

            if (!File.Exists(configPath))
            {
                M2Share.MainOutMessage($"[配置] {ConfigFileName} 不存在，使用默认值");
                return config;
            }

            try
            {
                var loader = new HonorSystemIniLoader(configPath);
                loader.Load();

                // Read configuration values with default fallbacks
                config.Enabled = loader.ReadBool("System", "Enabled", true);
                config.BaseHonorPoints = loader.ReadInteger("System", "BaseHonorPoints", 100);
                config.MaxHonorPoints = loader.ReadInteger("System", "MaxHonorPoints", 10000);
                config.HonorDecayRate = loader.ReadInteger("System", "HonorDecayRate", 1);
                config.KillHonorGain = loader.ReadInteger("Rewards", "KillHonorGain", 10);
                config.DeathHonorLoss = loader.ReadInteger("Penalties", "DeathHonorLoss", 5);

                M2Share.MainOutMessage($"[配置] 成功加载 {ConfigFileName}");
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[配置] 加载 {ConfigFileName} 失败: {ex.Message}，使用默认值");
            }

            return config;
        }
    }

    /// <summary>
    /// Honor System configuration data class
    /// </summary>
    public class HonorSystemConfig
    {
        public bool Enabled { get; set; } = true;
        public int BaseHonorPoints { get; set; } = 100;
        public int MaxHonorPoints { get; set; } = 10000;
        public int HonorDecayRate { get; set; } = 1;
        public int KillHonorGain { get; set; } = 10;
        public int DeathHonorLoss { get; set; } = 5;
    }
}
