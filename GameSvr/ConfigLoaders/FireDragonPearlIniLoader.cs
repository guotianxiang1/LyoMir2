using System;
using System.IO;
using SystemModule;

namespace GameSvr.ConfigLoaders
{
    /// <summary>
    /// 火龙珠配置加载器
    /// 从 config/火龙珠.ini 加载配置
    /// </summary>
    public sealed class FireDragonPearlIniLoader
    {
        private const string CONFIG_FILE_PATH = "config\\火龙珠.ini";

        /// <summary>
        /// 火龙珠配置数据
        /// </summary>
        public class FireDragonPearlConfig
        {
            // 默认值
            public int DropRate { get; set; } = 100;              // 掉落率 (1-10000)
            public int ExperienceBonus { get; set; } = 0;         // 经验加成 (百分比)
            public int DurationMinutes { get; set; } = 60;        // 持续时间 (分钟)
            public bool Enabled { get; set; } = true;             // 是否启用
            public int MinPlayerLevel { get; set; } = 1;          // 最低玩家等级
            public string RequiredMap { get; set; } = string.Empty; // 限定地图 (空=所有地图)
        }

        /// <summary>
        /// 加载配置
        /// </summary>
        /// <returns>配置对象，失败时返回默认值</returns>
        public static FireDragonPearlConfig LoadConfig()
        {
            var config = new FireDragonPearlConfig();

            try
            {
                // 构建完整配置文件路径
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CONFIG_FILE_PATH);

                // 文件不存在时使用默认值
                if (!File.Exists(configPath))
                {
                    M2Share.MainOutMessage($"[配置] {CONFIG_FILE_PATH} 不存在，使用默认值");
                    return config;
                }

                // 读取INI文件
                var lines = File.ReadAllLines(configPath, HUtil32.GbkEncoding);
                bool inSection = false;

                foreach (var line in lines)
                {
                    string trimmed = line.Trim();

                    // 跳过空行和注释
                    if (string.IsNullOrWhiteSpace(trimmed) ||
                        trimmed.StartsWith(";") ||
                        trimmed.StartsWith("//") ||
                        trimmed.StartsWith("#"))
                    {
                        continue;
                    }

                    // 检查section标记
                    if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                    {
                        string sectionName = trimmed.Substring(1, trimmed.Length - 2);
                        inSection = string.Equals(sectionName, "火龙珠", StringComparison.Ordinal) ||
                                   string.Equals(sectionName, "FireDragonPearl", StringComparison.OrdinalIgnoreCase);
                        continue;
                    }

                    // 在目标section内解析键值对
                    if (inSection)
                    {
                        var parts = trimmed.Split('=');
                        if (parts.Length != 2)
                            continue;

                        string key = parts[0].Trim();
                        string value = parts[1].Trim();

                        // 解析配置项
                        ParseConfigValue(config, key, value);
                    }
                }

                M2Share.MainOutMessage($"[配置] 成功加载 {CONFIG_FILE_PATH}");
                return config;
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[Exception] 加载{CONFIG_FILE_PATH}失败: {ex.Message}");
                M2Share.MainOutMessage($"[配置] 使用默认值");
                return config;
            }
        }

        /// <summary>
        /// 解析单个配置值
        /// </summary>
        private static void ParseConfigValue(FireDragonPearlConfig config, string key, string value)
        {
            try
            {
                switch (key)
                {
                    case "DropRate":
                    case "掉落率":
                        if (int.TryParse(value, out int dropRate))
                        {
                            config.DropRate = Math.Max(1, Math.Min(10000, dropRate));
                        }
                        break;

                    case "ExperienceBonus":
                    case "经验加成":
                        if (int.TryParse(value, out int expBonus))
                        {
                            config.ExperienceBonus = Math.Max(0, expBonus);
                        }
                        break;

                    case "DurationMinutes":
                    case "持续时间":
                        if (int.TryParse(value, out int duration))
                        {
                            config.DurationMinutes = Math.Max(1, duration);
                        }
                        break;

                    case "Enabled":
                    case "启用":
                        config.Enabled = ParseBoolValue(value);
                        break;

                    case "MinPlayerLevel":
                    case "最低等级":
                        if (int.TryParse(value, out int minLevel))
                        {
                            config.MinPlayerLevel = Math.Max(1, minLevel);
                        }
                        break;

                    case "RequiredMap":
                    case "限定地图":
                        config.RequiredMap = value;
                        break;

                    default:
                        M2Share.MainOutMessage($"[警告] 未知的配置项: {key}");
                        break;
                }
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[错误] 解析配置项 {key}={value} 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 解析布尔值 (支持多种格式)
        /// </summary>
        private static bool ParseBoolValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            value = value.ToLower();
            return value == "1" ||
                   value == "true" ||
                   value == "yes" ||
                   value == "是" ||
                   value == "启用";
        }
    }
}
