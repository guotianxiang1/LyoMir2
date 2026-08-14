using System;
using System.IO;
using SystemModule;
using SystemModule.Common;

namespace GameSvr.ConfigLoaders
{
    /// <summary>
    /// 红字奖励配置数据
    /// </summary>
    public class RedWordPrizeConfig
    {
        /// <summary>奖励档位（天）</summary>
        public int RewardTierDays { get; set; } = 1;

        /// <summary>是否启用</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>奖励物品名称</summary>
        public string RewardItemName { get; set; } = string.Empty;

        /// <summary>奖励数量</summary>
        public int RewardCount { get; set; } = 1;
    }

    /// <summary>
    /// 红字奖励配置加载器
    /// Loads config/红字奖.ini
    /// </summary>
    public class RedWordPrizeIniLoader : IniFile
    {
        private const string ConfigFileName = "config/红字奖.ini";
        private const string SectionName = "Setup";

        public RedWordPrizeConfig Config { get; private set; }

        private RedWordPrizeIniLoader() : base(ConfigFileName)
        {
            Config = new RedWordPrizeConfig();
        }

        /// <summary>
        /// 加载配置文件
        /// </summary>
        /// <returns>配置对象，失败时返回默认值</returns>
        public static RedWordPrizeConfig LoadConfig()
        {
            var loader = new RedWordPrizeIniLoader();

            try
            {
                if (!File.Exists(ConfigFileName))
                {
                    M2Share.MainOutMessage($"[红字奖] 配置文件不存在: {ConfigFileName}，使用默认值");
                    return loader.Config;
                }

                loader.Load();
                loader.ParseConfig();

                M2Share.MainOutMessage($"[红字奖] 配置加载成功: 启用={loader.Config.Enabled}, 档位={loader.Config.RewardTierDays}天");
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[红字奖] 配置加载失败: {ex.Message}，使用默认值");
            }

            return loader.Config;
        }

        private void ParseConfig()
        {
            try
            {
                Config.Enabled = ReadBool(SectionName, "Enabled", true);
                Config.RewardTierDays = ReadInteger(SectionName, "RewardTierDays", 1);
                Config.RewardItemName = ReadString(SectionName, "RewardItemName", string.Empty);
                Config.RewardCount = ReadInteger(SectionName, "RewardCount", 1);

                // 验证配置合法性
                if (Config.RewardTierDays <= 0)
                {
                    M2Share.MainOutMessage($"[红字奖] 警告: RewardTierDays={Config.RewardTierDays} 无效，使用默认值 1");
                    Config.RewardTierDays = 1;
                }

                if (Config.RewardCount < 0)
                {
                    M2Share.MainOutMessage($"[红字奖] 警告: RewardCount={Config.RewardCount} 无效，使用默认值 1");
                    Config.RewardCount = 1;
                }
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[红字奖] 解析配置失败: {ex.Message}");
                throw;
            }
        }
    }
}
