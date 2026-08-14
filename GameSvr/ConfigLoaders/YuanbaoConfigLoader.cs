using System;
using System.IO;
using SystemModule.Common;

namespace GameSvr.ConfigLoaders
{
    /// <summary>
    /// Simple INI loader for 元宝系统 (Yuanbao System) configuration
    /// Reads config/元宝系统.ini and returns default values on parse failure
    /// </summary>
    public class YuanbaoConfigLoader : IniFile
    {
        private const string ConfigFileName = "config/元宝系统.ini";

        public YuanbaoConfigLoader(string filePath) : base(filePath)
        {
        }

        /// <summary>
        /// Loads Yuanbao System configuration from config/元宝系统.ini
        /// Returns default configuration if file is missing or parsing fails
        /// </summary>
        public static YuanbaoConfig LoadConfig(string baseDir)
        {
            var config = new YuanbaoConfig();
            var configPath = Path.Combine(baseDir, ConfigFileName);

            if (!File.Exists(configPath))
            {
                M2Share.MainOutMessage($"[配置] {ConfigFileName} 不存在，使用默认值");
                return config;
            }

            try
            {
                var loader = new YuanbaoConfigLoader(configPath);
                loader.Load();

                // Read configuration values with default fallbacks
                config.Enabled = loader.ReadBool("System", "Enabled", true);
                config.ExchangeRate = loader.ReadInteger("System", "ExchangeRate", 100);
                config.MinExchangeAmount = loader.ReadInteger("System", "MinExchangeAmount", 1);
                config.MaxExchangeAmount = loader.ReadInteger("System", "MaxExchangeAmount", 10000);
                config.DailyExchangeLimit = loader.ReadInteger("System", "DailyExchangeLimit", 100000);

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
    /// Yuanbao System configuration data class
    /// </summary>
    public class YuanbaoConfig
    {
        public bool Enabled { get; set; } = true;
        public int ExchangeRate { get; set; } = 100;
        public int MinExchangeAmount { get; set; } = 1;
        public int MaxExchangeAmount { get; set; } = 10000;
        public int DailyExchangeLimit { get; set; } = 100000;
    }
}
