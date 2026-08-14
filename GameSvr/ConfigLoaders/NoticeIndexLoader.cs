using System;
using System.IO;
using System.Text;

namespace GameSvr.ConfigLoaders
{
    /// <summary>
    /// Notice配置加载器
    ///
    /// 从 Notice/Notice.txt 加载公告索引配置
    /// 解析失败时记录错误日志并返回默认值
    ///
    /// Status: Initial implementation
    /// </summary>
    public class NoticeIndexLoader
    {
        private const string ConfigFileName = "Notice/Notice.txt";

        public int NoticeIndex { get; set; } = 0;

        /// <summary>
        /// 加载公告索引配置
        /// </summary>
        public static NoticeIndexLoader LoadConfig(string baseDir)
        {
            var config = new NoticeIndexLoader();
            var configPath = Path.Combine(baseDir, ConfigFileName);

            if (!File.Exists(configPath))
            {
                M2Share.MainOutMessage($"[配置] {ConfigFileName} 不存在，使用默认值");
                return config;
            }

            try
            {
                // 使用GBK编码读取配置文件
                var lines = File.ReadAllLines(configPath, Encoding.GetEncoding(936));

                foreach (var line in lines)
                {
                    // 跳过空行和注释行
                    if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith(";") || line.TrimStart().StartsWith("#"))
                        continue;

                    // 解析键值对 (格式: key=value)
                    var parts = line.Split('=');
                    if (parts.Length != 2)
                        continue;

                    var key = parts[0].Trim();
                    var value = parts[1].Trim();

                    if (key.Equals("NoticeIndex", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(value, out var parsedValue))
                        {
                            config.NoticeIndex = parsedValue;
                        }
                        else
                        {
                            M2Share.MainOutMessage($"[警告] {ConfigFileName} NoticeIndex 解析失败: {value}");
                        }
                    }
                }

                M2Share.MainOutMessage($"[配置] {ConfigFileName} 加载完成");
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[错误] 加载 {ConfigFileName} 失败: {ex.Message}");
                M2Share.MainOutMessage("[配置] 使用默认公告索引配置");
            }

            return config;
        }
    }
}
