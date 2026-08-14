using System;
using System.Collections.Generic;
using GameSvr.ConfigLoaders;
using SystemModule;

namespace GameSvr.Features.Rewards
{
    /// <summary>
    /// 风云榜排行配置 - 最小可行实现 (MVI)
    /// Fengyun Ranking Configuration - Minimal Viable Implementation
    ///
    /// 基于战神引擎逆向分析
    /// Based on Shenshen Engine reverse engineering
    ///
    /// 配置文件路径: config/风云榜.ini
    /// Configuration file path: config/FengyunBang.ini
    /// </summary>
    public class FengyunRankingConfig
    {
        #region Constants - Configuration Paths

        /// <summary>
        /// 配置文件相对路径
        /// Configuration file relative path
        /// </summary>
        public const string CONFIG_FILE_PATH = "config/风云榜.ini";

        /// <summary>
        /// 排行榜数据存储路径
        /// Ranking data storage path
        /// </summary>
        public const string RANKING_DATA_PATH = "data/rankings/";

        #endregion

        #region Configuration Properties - From FengyunBangIniLoader

        /// <summary>
        /// 排行榜系统是否启用
        /// Whether ranking system is enabled
        /// Default: true
        /// </summary>
        public bool Enabled { get; private set; }

        /// <summary>
        /// 排行榜刷新间隔（秒）
        /// Ranking refresh interval in seconds
        /// Default: 3600 (1 hour)
        /// Range: 60 ~ 86400
        /// </summary>
        public int RefreshInterval { get; private set; }

        /// <summary>
        /// 玩家排行榜显示数量
        /// Number of top players to display
        /// Default: 100
        /// Range: 10 ~ 1000
        /// </summary>
        public int TopPlayerCount { get; private set; }

        /// <summary>
        /// 行会排行榜显示数量
        /// Number of top guilds to display
        /// Default: 50
        /// Range: 10 ~ 500
        /// </summary>
        public int TopGuildCount { get; private set; }

        /// <summary>
        /// 是否显示等级排行
        /// Whether to show level ranking
        /// Default: true
        /// </summary>
        public bool ShowLevel { get; private set; }

        /// <summary>
        /// 是否显示战力排行
        /// Whether to show power ranking
        /// Default: true
        /// </summary>
        public bool ShowPower { get; private set; }

        /// <summary>
        /// 是否显示财富排行
        /// Whether to show wealth ranking
        /// Default: true
        /// </summary>
        public bool ShowWealth { get; private set; }

        #endregion

        #region Runtime State

        /// <summary>
        /// 配置加载器实例
        /// Configuration loader instance
        /// </summary>
        private FengyunBangIniLoader _loader;

        /// <summary>
        /// 最后刷新时间
        /// Last refresh timestamp
        /// </summary>
        private DateTime _lastRefreshTime;

        /// <summary>
        /// 是否已初始化
        /// Whether initialized
        /// </summary>
        private bool _initialized;

        #endregion

        #region Constructor

        /// <summary>
        /// 构造函数 - 初始化配置
        /// Constructor - Initialize configuration
        /// </summary>
        public FengyunRankingConfig()
        {
            _initialized = false;
            _lastRefreshTime = DateTime.MinValue;
        }

        #endregion

        #region Core Methods - Configuration Management

        /// <summary>
        /// 初始化配置
        /// Initialize configuration
        ///
        /// 逆向位置: 待定 (Pending reverse engineering)
        /// VA地址: TBD
        ///
        /// 加载顺序:
        /// 1. 创建配置加载器
        /// 2. 读取INI文件
        /// 3. 验证配置有效性
        /// 4. 初始化运行时状态
        /// </summary>
        /// <returns>是否初始化成功</returns>
        public bool Initialize()
        {
            try
            {
                if (_initialized)
                {
                    M2Share.MainOutMessage("[风云榜] 配置已初始化，跳过重复初始化");
                    return true;
                }

                M2Share.MainOutMessage("[风云榜] 开始初始化排行配置...");

                // 创建并加载配置文件
                _loader = new FengyunBangIniLoader(CONFIG_FILE_PATH);

                // 同步配置属性
                SyncConfigFromLoader();

                // 初始化运行时状态
                _lastRefreshTime = DateTime.Now;
                _initialized = true;

                M2Share.MainOutMessage($"[风云榜] 配置初始化完成 - 启用状态: {Enabled}");
                M2Share.MainOutMessage($"[风云榜] 刷新间隔: {RefreshInterval}秒, 玩家榜: {TopPlayerCount}名, 行会榜: {TopGuildCount}名");

                return true;
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[错误] 风云榜配置初始化失败: {ex.Message}");
                M2Share.MainOutMessage($"[错误] 堆栈追踪: {ex.StackTrace}");

                // 初始化失败时使用安全默认值
                LoadSafeDefaults();
                return false;
            }
        }

        /// <summary>
        /// 重新加载配置
        /// Reload configuration
        ///
        /// 逆向位置: 待定
        /// VA地址: TBD
        /// </summary>
        /// <returns>是否重载成功</returns>
        public bool Reload()
        {
            try
            {
                M2Share.MainOutMessage("[风云榜] 重新加载配置...");

                if (_loader != null)
                {
                    _loader.LoadConfig();
                    SyncConfigFromLoader();
                    M2Share.MainOutMessage("[风云榜] 配置重载完成");
                    return true;
                }
                else
                {
                    M2Share.MainOutMessage("[警告] 配置加载器未初始化，执行完整初始化");
                    return Initialize();
                }
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[错误] 风云榜配置重载失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 从加载器同步配置属性
        /// Sync configuration properties from loader
        /// </summary>
        private void SyncConfigFromLoader()
        {
            if (_loader == null)
            {
                throw new InvalidOperationException("配置加载器未初始化");
            }

            Enabled = _loader.Enabled;
            RefreshInterval = _loader.RefreshInterval;
            TopPlayerCount = _loader.TopPlayerCount;
            TopGuildCount = _loader.TopGuildCount;
            ShowLevel = _loader.ShowLevel;
            ShowPower = _loader.ShowPower;
            ShowWealth = _loader.ShowWealth;
        }

        /// <summary>
        /// 加载安全默认值
        /// Load safe default values
        /// </summary>
        private void LoadSafeDefaults()
        {
            Enabled = false; // 初始化失败时禁用功能
            RefreshInterval = 3600;
            TopPlayerCount = 100;
            TopGuildCount = 50;
            ShowLevel = true;
            ShowPower = true;
            ShowWealth = true;
            _initialized = false;

            M2Share.MainOutMessage("[风云榜] 已加载安全默认配置（功能禁用）");
        }

        #endregion

        #region Query Methods - Configuration Access

        /// <summary>
        /// 检查是否需要刷新排行榜
        /// Check if ranking needs refresh
        ///
        /// 逆向位置: 待定
        /// VA地址: TBD
        ///
        /// 判定逻辑:
        /// 1. 检查功能是否启用
        /// 2. 计算距离上次刷新的时间差
        /// 3. 与配置的刷新间隔比较
        /// </summary>
        /// <returns>是否需要刷新</returns>
        public bool ShouldRefresh()
        {
            if (!Enabled || !_initialized)
            {
                return false;
            }

            var elapsed = (DateTime.Now - _lastRefreshTime).TotalSeconds;
            return elapsed >= RefreshInterval;
        }

        /// <summary>
        /// 标记刷新完成
        /// Mark refresh completed
        /// </summary>
        public void MarkRefreshed()
        {
            _lastRefreshTime = DateTime.Now;
        }

        /// <summary>
        /// 获取下次刷新剩余时间（秒）
        /// Get remaining seconds until next refresh
        /// </summary>
        /// <returns>剩余秒数</returns>
        public int GetSecondsUntilNextRefresh()
        {
            if (!Enabled || !_initialized)
            {
                return -1;
            }

            var elapsed = (DateTime.Now - _lastRefreshTime).TotalSeconds;
            var remaining = RefreshInterval - elapsed;
            return remaining > 0 ? (int)remaining : 0;
        }

        /// <summary>
        /// 验证配置有效性
        /// Validate configuration
        /// </summary>
        /// <returns>配置是否有效</returns>
        public bool IsValid()
        {
            return _initialized && _loader != null;
        }

        #endregion

        #region Ranking Type Management

        /// <summary>
        /// 排行榜类型枚举
        /// Ranking type enumeration
        ///
        /// 逆向位置: 待定
        /// VA地址: TBD
        ///
        /// 可能的原版类型值:
        /// - 0x01: 等级排行
        /// - 0x02: 战力排行
        /// - 0x03: 财富排行
        /// - 0x10: 行会排行
        /// </summary>
        public enum RankingType
        {
            /// <summary>等级排行</summary>
            Level = 1,

            /// <summary>战力排行</summary>
            Power = 2,

            /// <summary>财富排行</summary>
            Wealth = 3,

            /// <summary>行会排行</summary>
            Guild = 0x10,

            /// <summary>PK排行</summary>
            PK = 4,

            /// <summary>声望排行</summary>
            Reputation = 5
        }

        /// <summary>
        /// 检查指定排行榜类型是否启用
        /// Check if specific ranking type is enabled
        /// </summary>
        /// <param name="rankingType">排行榜类型</param>
        /// <returns>是否启用</returns>
        public bool IsRankingTypeEnabled(RankingType rankingType)
        {
            if (!Enabled)
            {
                return false;
            }

            switch (rankingType)
            {
                case RankingType.Level:
                    return ShowLevel;
                case RankingType.Power:
                    return ShowPower;
                case RankingType.Wealth:
                    return ShowWealth;
                case RankingType.Guild:
                    return true; // 行会排行总是启用（如果总开关开启）
                default:
                    return false; // 其他类型默认禁用
            }
        }

        /// <summary>
        /// 获取指定排行榜类型的显示数量
        /// Get display count for specific ranking type
        /// </summary>
        /// <param name="rankingType">排行榜类型</param>
        /// <returns>显示数量</returns>
        public int GetDisplayCount(RankingType rankingType)
        {
            switch (rankingType)
            {
                case RankingType.Guild:
                    return TopGuildCount;
                default:
                    return TopPlayerCount; // 玩家相关排行使用统一配置
            }
        }

        #endregion

        #region Debug and Diagnostics

        /// <summary>
        /// 获取配置摘要信息
        /// Get configuration summary
        /// </summary>
        /// <returns>配置摘要字符串</returns>
        public string GetConfigSummary()
        {
            return $"FengyunRankingConfig [" +
                   $"Enabled={Enabled}, " +
                   $"RefreshInterval={RefreshInterval}s, " +
                   $"TopPlayers={TopPlayerCount}, " +
                   $"TopGuilds={TopGuildCount}, " +
                   $"ShowLevel={ShowLevel}, " +
                   $"ShowPower={ShowPower}, " +
                   $"ShowWealth={ShowWealth}, " +
                   $"Initialized={_initialized}" +
                   $"]";
        }

        /// <summary>
        /// 输出配置详情到日志
        /// Output configuration details to log
        /// </summary>
        public void DumpConfig()
        {
            M2Share.MainOutMessage("========== 风云榜配置详情 ==========");
            M2Share.MainOutMessage($"配置文件路径: {CONFIG_FILE_PATH}");
            M2Share.MainOutMessage($"初始化状态: {(_initialized ? "已初始化" : "未初始化")}");
            M2Share.MainOutMessage($"功能启用: {Enabled}");
            M2Share.MainOutMessage($"刷新间隔: {RefreshInterval} 秒");
            M2Share.MainOutMessage($"玩家榜显示数量: {TopPlayerCount}");
            M2Share.MainOutMessage($"行会榜显示数量: {TopGuildCount}");
            M2Share.MainOutMessage($"显示等级排行: {ShowLevel}");
            M2Share.MainOutMessage($"显示战力排行: {ShowPower}");
            M2Share.MainOutMessage($"显示财富排行: {ShowWealth}");
            M2Share.MainOutMessage($"最后刷新时间: {_lastRefreshTime}");
            M2Share.MainOutMessage($"距下次刷新: {GetSecondsUntilNextRefresh()} 秒");
            M2Share.MainOutMessage("===================================");
        }

        #endregion
    }
}
