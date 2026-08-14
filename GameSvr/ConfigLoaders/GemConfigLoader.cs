using System;
using System.IO;
using SystemModule;

namespace GameSvr.ConfigLoaders
{
    /// <summary>
    /// 宝石配置加载器
    /// 从 config/GemConfig.ini 读取宝石系统配置
    /// </summary>
    public class GemConfigLoader
    {
        private const string ConfigFilePath = "config/GemConfig.ini";

        // 默认配置值
        public int MaxGemLevel { get; private set; } = 10;
        public int UpgradeSuccessRate { get; private set; } = 50;
        public int UpgradeProtectionLevel { get; private set; } = 3;
        public bool EnableGemSystem { get; private set; } = true;
        public int GemSlotCount { get; private set; } = 4;

        /// <summary>
        /// 加载宝石配置
        /// </summary>
        /// <returns>加载成功返回 true，失败返回 false（使用默认值）</returns>
        public bool LoadConfig()
        {
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFilePath);

            if (!File.Exists(configPath))
            {
                M2Share.MainOutMessage($"[配置] {ConfigFilePath} 不存在，使用默认值");
                return false;
            }

            try
            {
                var lines = File.ReadAllLines(configPath, HUtil32.GbkEncoding);
                string currentSection = string.Empty;

                foreach (var line in lines)
                {
                    string trimmed = line.Trim();

                    // 跳过空行和注释
                    if (string.IsNullOrWhiteSpace(trimmed) ||
                        trimmed.StartsWith(";") ||
                        trimmed.StartsWith("#") ||
                        trimmed.StartsWith("//"))
                    {
                        continue;
                    }

                    // 解析节名
                    if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                    {
                        currentSection = trimmed.Substring(1, trimmed.Length - 2).Trim();
                        continue;
                    }

                    // 解析键值对
                    var parts = trimmed.Split(new[] { '=' }, 2);
                    if (parts.Length != 2)
                    {
                        continue;
                    }

                    string key = parts[0].Trim();
                    string value = parts[1].Trim();

                    // 解析配置项
                    ParseConfigValue(key, value);
                }

                M2Share.MainOutMessage($"[配置] {ConfigFilePath} 加载完成");
                return true;
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[错误] 加载 {ConfigFilePath} 失败: {ex.Message}");
                M2Share.MainOutMessage("[配置] 使用默认宝石配置");
                return false;
            }
        }

        /// <summary>
        /// 解析配置值
        /// </summary>
        private void ParseConfigValue(string key, string value)
        {
            try
            {
                switch (key)
                {
                    case "MaxGemLevel":
                        if (int.TryParse(value, out var maxLevel))
                        {
                            MaxGemLevel = maxLevel;
                        }
                        break;

                    case "UpgradeSuccessRate":
                        if (int.TryParse(value, out var successRate))
                        {
                            UpgradeSuccessRate = successRate;
                        }
                        break;

                    case "UpgradeProtectionLevel":
                        if (int.TryParse(value, out var protectionLevel))
                        {
                            UpgradeProtectionLevel = protectionLevel;
                        }
                        break;

                    case "EnableGemSystem":
                        EnableGemSystem = value == "1" ||
                                        value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                                        value.Equals("是", StringComparison.OrdinalIgnoreCase);
                        break;

                    case "GemSlotCount":
                        if (int.TryParse(value, out var slotCount))
                        {
                            GemSlotCount = slotCount;
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[警告] 解析配置项 {key}={value} 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 验证配置值有效性
        /// </summary>
        public bool Validate()
        {
            bool isValid = true;

            if (MaxGemLevel < 1 || MaxGemLevel > 20)
            {
                M2Share.MainOutMessage($"[警告] MaxGemLevel={MaxGemLevel} 超出范围 [1-20]，重置为10");
                MaxGemLevel = 10;
                isValid = false;
            }

            if (UpgradeSuccessRate < 0 || UpgradeSuccessRate > 100)
            {
                M2Share.MainOutMessage($"[警告] UpgradeSuccessRate={UpgradeSuccessRate} 超出范围 [0-100]，重置为50");
                UpgradeSuccessRate = 50;
                isValid = false;
            }

            if (UpgradeProtectionLevel < 0 || UpgradeProtectionLevel > MaxGemLevel)
            {
                M2Share.MainOutMessage($"[警告] UpgradeProtectionLevel={UpgradeProtectionLevel} 超出范围，重置为3");
                UpgradeProtectionLevel = 3;
                isValid = false;
            }

            if (GemSlotCount < 1 || GemSlotCount > 10)
            {
                M2Share.MainOutMessage($"[警告] GemSlotCount={GemSlotCount} 超出范围 [1-10]，重置为4");
                GemSlotCount = 4;
                isValid = false;
            }

            return isValid;
        }
    }
}
