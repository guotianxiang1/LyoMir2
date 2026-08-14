using System;
using System.IO;
using SystemModule.Common;

namespace GameSvr.ConfigLoaders
{
    /// <summary>
    /// SuperMerchant.ini configuration loader
    /// Loads configuration for super merchant NPC behavior
    /// </summary>
    public class SuperMerchantIniLoader : IniFile
    {
        private const string ConfigFileName = "SuperMerchant.ini";

        // Default configuration values
        public bool Enabled { get; private set; } = true;
        public int RefreshIntervalMinutes { get; private set; } = 60;
        public int MaxItemCount { get; private set; } = 50;
        public int MinPricePercent { get; private set; } = 80;
        public int MaxPricePercent { get; private set; } = 120;
        public bool AllowRareItems { get; private set; } = true;
        public int QualityThreshold { get; private set; } = 0;

        public SuperMerchantIniLoader(string fileName) : base(fileName)
        {
        }

        /// <summary>
        /// Loads SuperMerchant.ini configuration
        /// Returns a configured instance, or default values if file missing/error
        /// </summary>
        public static SuperMerchantIniLoader LoadConfig(string configDir)
        {
            var configPath = Path.Combine(configDir, ConfigFileName);

            if (!File.Exists(configPath))
            {
                LogError($"配置文件不存在: {configPath}，使用默认值");
                return CreateDefault();
            }

            try
            {
                var loader = new SuperMerchantIniLoader(configPath);
                loader.Load();
                loader.ParseConfiguration();

                LogInfo($"成功加载配置: {ConfigFileName}");
                return loader;
            }
            catch (Exception ex)
            {
                LogError($"加载配置文件失败: {ConfigFileName}, 错误: {ex.Message}");
                LogError($"使用默认配置值");
                return CreateDefault();
            }
        }

        /// <summary>
        /// Creates a loader with default values
        /// </summary>
        private static SuperMerchantIniLoader CreateDefault()
        {
            // Return a dummy instance with default values already set
            var loader = new SuperMerchantIniLoader(string.Empty);
            return loader;
        }

        /// <summary>
        /// Parses configuration from loaded INI file
        /// Expected structure:
        /// [SuperMerchant]
        /// Enabled=1
        /// RefreshIntervalMinutes=60
        /// MaxItemCount=50
        /// MinPricePercent=80
        /// MaxPricePercent=120
        /// AllowRareItems=1
        /// QualityThreshold=0
        /// </summary>
        private void ParseConfiguration()
        {
            const string section = "SuperMerchant";

            try
            {
                Enabled = ReadBool(section, "Enabled", true);
                RefreshIntervalMinutes = ReadInteger(section, "RefreshIntervalMinutes", 60);
                MaxItemCount = ReadInteger(section, "MaxItemCount", 50);
                MinPricePercent = ReadInteger(section, "MinPricePercent", 80);
                MaxPricePercent = ReadInteger(section, "MaxPricePercent", 120);
                AllowRareItems = ReadBool(section, "AllowRareItems", true);
                QualityThreshold = ReadInteger(section, "QualityThreshold", 0);

                ValidateConfiguration();
            }
            catch (Exception ex)
            {
                LogError($"解析配置时出错: {ex.Message}，部分配置可能使用默认值");
            }
        }

        /// <summary>
        /// Validates loaded configuration values
        /// </summary>
        private void ValidateConfiguration()
        {
            if (RefreshIntervalMinutes < 1)
            {
                LogError($"RefreshIntervalMinutes 值无效 ({RefreshIntervalMinutes})，重置为默认值 60");
                RefreshIntervalMinutes = 60;
            }

            if (MaxItemCount < 1 || MaxItemCount > 1000)
            {
                LogError($"MaxItemCount 值超出范围 ({MaxItemCount})，重置为默认值 50");
                MaxItemCount = 50;
            }

            if (MinPricePercent < 1 || MinPricePercent > 1000)
            {
                LogError($"MinPricePercent 值超出范围 ({MinPricePercent})，重置为默认值 80");
                MinPricePercent = 80;
            }

            if (MaxPricePercent < 1 || MaxPricePercent > 10000)
            {
                LogError($"MaxPricePercent 值超出范围 ({MaxPricePercent})，重置为默认值 120");
                MaxPricePercent = 120;
            }

            if (MinPricePercent > MaxPricePercent)
            {
                LogError($"MinPricePercent ({MinPricePercent}) > MaxPricePercent ({MaxPricePercent})，交换值");
                (MinPricePercent, MaxPricePercent) = (MaxPricePercent, MinPricePercent);
            }

            if (QualityThreshold < 0)
            {
                LogError($"QualityThreshold 值无效 ({QualityThreshold})，重置为默认值 0");
                QualityThreshold = 0;
            }
        }

        /// <summary>
        /// Logs informational message
        /// </summary>
        private static void LogInfo(string message)
        {
            M2Share.MainOutMessage($"[SuperMerchant] {message}");
        }

        /// <summary>
        /// Logs error message
        /// </summary>
        private static void LogError(string message)
        {
            M2Share.MainOutMessage($"[SuperMerchant][错误] {message}");
        }
    }
}
