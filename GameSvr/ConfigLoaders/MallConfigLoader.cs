using System;
using System.IO;
using SystemModule.Common;

namespace GameSvr.ConfigLoaders
{
    /// <summary>
    /// Simple INI loader for 商城 (Mall) configuration
    /// Reads config/商城.ini and returns default values on parse failure
    /// </summary>
    public class MallConfigLoader : IniFile
    {
        private const string ConfigFileName = "config/商城.ini";

        public MallConfigLoader(string filePath) : base(filePath)
        {
        }

        /// <summary>
        /// Loads Mall configuration from config/商城.ini
        /// Returns default configuration if file is missing or parsing fails
        /// </summary>
        public static MallConfig LoadConfig(string baseDir)
        {
            var config = new MallConfig();
            var configPath = Path.Combine(baseDir, ConfigFileName);

            if (!File.Exists(configPath))
            {
                M2Share.MainOutMessage($"[配置] {ConfigFileName} 不存在，使用默认值");
                return config;
            }

            try
            {
                var loader = new MallConfigLoader(configPath);
                loader.Load();

                // Read configuration values with default fallbacks
                config.Enabled = loader.ReadBool("System", "Enabled", true);
                config.RefreshInterval = loader.ReadInteger("System", "RefreshInterval", 3600);
                config.MaxItemsPerPage = loader.ReadInteger("System", "MaxItemsPerPage", 20);
                config.PriceMultiplier = loader.ReadFloat("System", "PriceMultiplier", 1.0);
                config.DiscountEnabled = loader.ReadBool("Discount", "Enabled", false);
                config.DiscountRate = loader.ReadFloat("Discount", "Rate", 0.9);

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
    /// Mall configuration data class
    /// </summary>
    public class MallConfig
    {
        public bool Enabled { get; set; } = true;
        public int RefreshInterval { get; set; } = 3600;
        public int MaxItemsPerPage { get; set; } = 20;
        public double PriceMultiplier { get; set; } = 1.0;
        public bool DiscountEnabled { get; set; } = false;
        public double DiscountRate { get; set; } = 0.9;
    }
}
