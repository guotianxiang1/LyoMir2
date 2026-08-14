using System;
using System.IO;
using SystemModule.Common;

namespace GameSvr.ConfigLoaders
{
    /// <summary>
    /// Simple INI loader for 副本 (Instance/Dungeon) configuration
    /// Reads config/副本.ini and returns default values on parse failure
    /// </summary>
    public class InstanceConfigLoader : IniFile
    {
        private const string ConfigFileName = "config/副本.ini";

        public InstanceConfigLoader(string filePath) : base(filePath)
        {
        }

        /// <summary>
        /// Loads Instance configuration from config/副本.ini
        /// Returns default configuration if file is missing or parsing fails
        /// </summary>
        public static InstanceConfig LoadConfig(string baseDir)
        {
            var config = new InstanceConfig();
            var configPath = Path.Combine(baseDir, ConfigFileName);

            if (!File.Exists(configPath))
            {
                M2Share.MainOutMessage($"[配置] {ConfigFileName} 不存在，使用默认值");
                return config;
            }

            try
            {
                var loader = new InstanceConfigLoader(configPath);
                loader.Load();

                // Read configuration values with default fallbacks
                config.Enabled = loader.ReadBool("System", "Enabled", true);
                config.MaxInstanceCount = loader.ReadInteger("System", "MaxInstanceCount", 100);
                config.DefaultTimeLimit = loader.ReadInteger("System", "DefaultTimeLimit", 3600);
                config.MinPlayerLevel = loader.ReadInteger("Restrictions", "MinPlayerLevel", 1);
                config.MaxPlayerLevel = loader.ReadInteger("Restrictions", "MaxPlayerLevel", 999);
                config.MinPartySize = loader.ReadInteger("Restrictions", "MinPartySize", 1);
                config.MaxPartySize = loader.ReadInteger("Restrictions", "MaxPartySize", 8);
                config.ResetInterval = loader.ReadInteger("Reset", "ResetInterval", 86400);

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
    /// Instance configuration data class
    /// </summary>
    public class InstanceConfig
    {
        public bool Enabled { get; set; } = true;
        public int MaxInstanceCount { get; set; } = 100;
        public int DefaultTimeLimit { get; set; } = 3600;
        public int MinPlayerLevel { get; set; } = 1;
        public int MaxPlayerLevel { get; set; } = 999;
        public int MinPartySize { get; set; } = 1;
        public int MaxPartySize { get; set; } = 8;
        public int ResetInterval { get; set; } = 86400;
    }
}
