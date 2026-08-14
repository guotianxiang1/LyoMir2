using System;
using System.IO;
using SystemModule.Common;

namespace GameSvr.ConfigLoaders
{
    /// <summary>
    /// Simple INI loader for Buff System configuration
    /// Reads config/BuffSystem.ini and returns default values on parse failure
    /// </summary>
    public class BuffSystemConfigLoader : IniFile
    {
        private const string ConfigFileName = "config/BuffSystem.ini";

        public BuffSystemConfigLoader(string filePath) : base(filePath)
        {
        }

        /// <summary>
        /// Loads Buff System configuration from config/BuffSystem.ini
        /// Returns default configuration if file is missing or parsing fails
        /// </summary>
        public static BuffSystemConfig LoadConfig(string baseDir)
        {
            var config = new BuffSystemConfig();
            var configPath = Path.Combine(baseDir, ConfigFileName);

            if (!File.Exists(configPath))
            {
                M2Share.MainOutMessage($"[配置] {ConfigFileName} 不存在，使用默认值");
                return config;
            }

            try
            {
                var loader = new BuffSystemConfigLoader(configPath);
                loader.Load();

                // Read configuration values with default fallbacks
                config.Enabled = loader.ReadBool("System", "Enabled", true);
                config.MaxBuffSlots = loader.ReadInteger("System", "MaxBuffSlots", 32);
                config.TickIntervalMs = loader.ReadInteger("System", "TickIntervalMs", 500);
                config.AllowBuffStacking = loader.ReadBool("System", "AllowBuffStacking", false);
                config.PersistBuffsOnLogout = loader.ReadBool("System", "PersistBuffsOnLogout", true);

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
    /// Buff System configuration data class
    /// </summary>
    public class BuffSystemConfig
    {
        public bool Enabled { get; set; } = true;
        public int MaxBuffSlots { get; set; } = 32;
        public int TickIntervalMs { get; set; } = 500;
        public bool AllowBuffStacking { get; set; } = false;
        public bool PersistBuffsOnLogout { get; set; } = true;
    }
}
