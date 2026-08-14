using SystemModule;
using SystemModule.Common;

namespace GameSvr.ConfigLoaders
{
    /// <summary>
    /// 风云榜配置加载器
    /// 读取 config/风云榜.ini 配置文件
    /// 解析失败时记录错误日志并返回默认值
    /// </summary>
    public class FengyunBangIniLoader : IniFile
    {
        // 默认配置值
        public bool Enabled { get; private set; } = true;
        public int RefreshInterval { get; private set; } = 3600; // 秒
        public int TopPlayerCount { get; private set; } = 100;
        public int TopGuildCount { get; private set; } = 50;
        public bool ShowLevel { get; private set; } = true;
        public bool ShowPower { get; private set; } = true;
        public bool ShowWealth { get; private set; } = true;

        public FengyunBangIniLoader(string fileName) : base(fileName)
        {
            try
            {
                Load();
                LoadConfig();
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[错误] 加载风云榜配置失败: {ex.Message}");
                M2Share.MainOutMessage("[配置] 使用默认风云榜配置");
            }
        }

        /// <summary>
        /// 加载配置项
        /// 从 config/风云榜.ini 读取配置
        /// 缺失的键使用默认值
        /// </summary>
        public void LoadConfig()
        {
            try
            {
                // [基本设置] 部分
                Enabled = ReadBool("基本设置", "启用", true);
                RefreshInterval = ReadInteger("基本设置", "刷新间隔", 3600);

                // [排行榜设置] 部分
                TopPlayerCount = ReadInteger("排行榜设置", "玩家榜显示数量", 100);
                TopGuildCount = ReadInteger("排行榜设置", "行会榜显示数量", 50);

                // [显示选项] 部分
                ShowLevel = ReadBool("显示选项", "显示等级", true);
                ShowPower = ReadBool("显示选项", "显示战力", true);
                ShowWealth = ReadBool("显示选项", "显示财富", true);

                M2Share.MainOutMessage("[配置] 风云榜配置加载完成");

                // 验证配置合理性
                ValidateConfig();
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[错误] 解析风云榜配置时出错: {ex.Message}");
                M2Share.MainOutMessage("[配置] 使用默认风云榜配置");
                ResetToDefaults();
            }
        }

        /// <summary>
        /// 验证配置值的合理性
        /// </summary>
        private void ValidateConfig()
        {
            if (RefreshInterval < 60)
            {
                M2Share.MainOutMessage($"[警告] 风云榜刷新间隔 {RefreshInterval} 秒过小，重置为60秒");
                RefreshInterval = 60;
            }

            if (RefreshInterval > 86400)
            {
                M2Share.MainOutMessage($"[警告] 风云榜刷新间隔 {RefreshInterval} 秒过大，重置为86400秒(24小时)");
                RefreshInterval = 86400;
            }

            if (TopPlayerCount < 10)
            {
                M2Share.MainOutMessage($"[警告] 玩家榜显示数量 {TopPlayerCount} 过小，重置为10");
                TopPlayerCount = 10;
            }

            if (TopPlayerCount > 1000)
            {
                M2Share.MainOutMessage($"[警告] 玩家榜显示数量 {TopPlayerCount} 过大，重置为1000");
                TopPlayerCount = 1000;
            }

            if (TopGuildCount < 10)
            {
                M2Share.MainOutMessage($"[警告] 行会榜显示数量 {TopGuildCount} 过小，重置为10");
                TopGuildCount = 10;
            }

            if (TopGuildCount > 500)
            {
                M2Share.MainOutMessage($"[警告] 行会榜显示数量 {TopGuildCount} 过大，重置为500");
                TopGuildCount = 500;
            }
        }

        /// <summary>
        /// 重置为默认值
        /// </summary>
        private void ResetToDefaults()
        {
            Enabled = true;
            RefreshInterval = 3600;
            TopPlayerCount = 100;
            TopGuildCount = 50;
            ShowLevel = true;
            ShowPower = true;
            ShowWealth = true;
        }
    }
}
