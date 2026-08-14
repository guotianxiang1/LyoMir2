using System;
using SystemModule;
using SystemModule.Common;

namespace GameSvr.ConfigLoaders
{
    /// <summary>
    /// Simple ini loader for Festival.ini configuration file.
    /// Reads festival event settings from config/Festival.ini.
    /// Returns default values when parsing fails or file is missing.
    /// </summary>
    public class FestivalConfigLoader : IniFile
    {
        private const string DefaultConfigPath = "config/Festival.ini";

        public FestivalConfigLoader(string fileName) : base(fileName)
        {
        }

        /// <summary>
        /// Creates loader with default config path
        /// </summary>
        public static FestivalConfigLoader Create()
        {
            return new FestivalConfigLoader(DefaultConfigPath);
        }

        /// <summary>
        /// Loads Festival.ini configuration.
        /// Returns default values if file is missing or parsing fails.
        /// </summary>
        public FestivalSettings LoadConfig()
        {
            var settings = new FestivalSettings();

            try
            {
                // Load the ini file into cache
                Load();

                // Read [Festival] section
                settings.Enabled = ReadBool("Festival", "Enabled", false);
                settings.GlobalExpMultiplier = ReadFloat("Festival", "GlobalExpMultiplier", 1.0);
                settings.GlobalDropMultiplier = ReadFloat("Festival", "GlobalDropMultiplier", 1.0);
                settings.AnnouncementEnabled = ReadBool("Festival", "Announcement", true);
                settings.AnnouncementInterval = ReadInteger("Festival", "AnnouncementInterval", 3600);

                M2Share.MainOutMessage($"[配置] Festival.ini 加载成功");
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[错误] 加载 Festival.ini 失败: {ex.Message}");
                M2Share.MainOutMessage("[配置] 使用默认配置");
            }

            return settings;
        }

        /// <summary>
        /// Holds festival configuration settings with default values
        /// </summary>
        public class FestivalSettings
        {
            public bool Enabled { get; set; } = false;
            public double GlobalExpMultiplier { get; set; } = 1.0;
            public double GlobalDropMultiplier { get; set; } = 1.0;
            public bool AnnouncementEnabled { get; set; } = true;
            public int AnnouncementInterval { get; set; } = 3600;

            /// <summary>
            /// Validates loaded configuration values and applies safe limits
            /// </summary>
            public void Validate()
            {
                if (GlobalExpMultiplier < 0.1 || GlobalExpMultiplier > 100.0)
                {
                    M2Share.MainOutMessage($"[警告] GlobalExpMultiplier={GlobalExpMultiplier} 超出范围 [0.1-100.0]，重置为 1.0");
                    GlobalExpMultiplier = 1.0;
                }

                if (GlobalDropMultiplier < 0.1 || GlobalDropMultiplier > 100.0)
                {
                    M2Share.MainOutMessage($"[警告] GlobalDropMultiplier={GlobalDropMultiplier} 超出范围 [0.1-100.0]，重置为 1.0");
                    GlobalDropMultiplier = 1.0;
                }

                if (AnnouncementInterval < 60)
                {
                    M2Share.MainOutMessage($"[警告] AnnouncementInterval={AnnouncementInterval} 小于最小值 60秒，重置为 3600");
                    AnnouncementInterval = 3600;
                }
            }
        }
    }
}
