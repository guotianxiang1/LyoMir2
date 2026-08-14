using System;
using System.Collections.Generic;
using System.IO;
using SystemModule.Common;

namespace GameSvr.ConfigLoaders
{
    /// <summary>
    /// 新手礼包配置加载器 (Newbie Gift Pack Config Loader)
    /// Reads config/新手礼包.ini and returns default values on parse failure
    ///
    /// Reverse Engineering Status: MINIMAL VIABLE IMPLEMENTATION
    /// TODO: Add VA addresses once native implementation is fully reversed
    /// TODO: Verify field offsets and native behavior patterns
    /// TODO: Cross-reference with FreshmanTaskCommand handlers (CM 4496)
    /// </summary>
    public class NewbieGiftPackConfigLoader : IniFile
    {
        private const string ConfigFileName = "config/新手礼包.ini";

        public NewbieGiftPackConfigLoader(string filePath) : base(filePath)
        {
        }

        /// <summary>
        /// Loads Newbie Gift Pack configuration from config/新手礼包.ini
        /// Returns default configuration if file is missing or parsing fails
        ///
        /// Native Implementation Notes:
        /// - Related to CM 4496 (FreshmanTaskCommand) at 0x6DBBDC -> sub_6FAC8C
        /// - Task-board admin object reference at [0x7D5D20]
        /// - Script execution via @Main proc "FreshmanTaskCommand"
        /// - Reply packet SM 0x1190 with integer result
        /// TODO: Map exact native behavior once script subsystem is modeled
        /// </summary>
        public static NewbieGiftPackConfig LoadConfig(string baseDir)
        {
            var config = new NewbieGiftPackConfig();
            var configPath = Path.Combine(baseDir, ConfigFileName);

            if (!File.Exists(configPath))
            {
                M2Share.MainOutMessage($"[配置] {ConfigFileName} 不存在，使用默认值");
                return config;
            }

            try
            {
                var loader = new NewbieGiftPackConfigLoader(configPath);
                loader.Load();

                // System settings
                config.Enabled = loader.ReadBool("System", "Enabled", true);
                config.AutoGrantOnFirstLogin = loader.ReadBool("System", "AutoGrantOnFirstLogin", true);
                config.RequireManualClaim = loader.ReadBool("System", "RequireManualClaim", false);

                // Level gates
                config.MinLevel = loader.ReadInteger("LevelGates", "MinLevel", 1);
                config.MaxLevel = loader.ReadInteger("LevelGates", "MaxLevel", 10);

                // Time constraints
                config.ExpireAfterDays = loader.ReadInteger("TimeConstraints", "ExpireAfterDays", 7);
                config.OncePerAccount = loader.ReadBool("TimeConstraints", "OncePerAccount", true);
                config.OncePerCharacter = loader.ReadBool("TimeConstraints", "OncePerCharacter", false);

                // Notification
                config.ShowWelcomeMessage = loader.ReadBool("Notification", "ShowWelcomeMessage", true);
                config.WelcomeMessageColor = loader.ReadInteger("Notification", "WelcomeMessageColor", 0xFFDB);

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
    /// 新手礼包配置数据类 (Newbie Gift Pack Configuration Data)
    ///
    /// Field Mapping Notes (TODO: Verify against native binary):
    /// - Gift pack state likely stored in player object or task subsystem
    /// - May use quest key-value store (player+0x804/0x808) for tracking
    /// - Cross-reference with task-board @Main script execution context
    /// - Verify persistence layer (HumDataDB record or separate table)
    ///
    /// Related Native Structures:
    /// - TTaskAdmin at [0x7D5D20] (task-board admin object)
    /// - Player quest storage: Self+0x804 (keys), Self+0x808 (values)
    /// - Quest key format: nTaskNo * 1000 + nFieldNo (0x6E42CC)
    /// - Binary search reader at 0x6E4270 (returns -1 on miss)
    /// </summary>
    public class NewbieGiftPackConfig
    {
        // System Control
        /// <summary>
        /// Master switch for the newbie gift pack system
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Automatically grant gift pack on first character login
        /// Native behavior: TODO verify if auto-grant exists or requires script trigger
        /// </summary>
        public bool AutoGrantOnFirstLogin { get; set; } = true;

        /// <summary>
        /// Require player to manually claim (e.g., via NPC or UI)
        /// Related to CM 4496 FreshmanTaskCommand script execution
        /// </summary>
        public bool RequireManualClaim { get; set; } = false;

        // Level Gates
        /// <summary>
        /// Minimum character level to receive gift pack
        /// TODO: Verify native level check VA and field offset
        /// </summary>
        public int MinLevel { get; set; } = 1;

        /// <summary>
        /// Maximum character level eligible for gift pack
        /// TODO: Cross-reference with level progression system
        /// </summary>
        public int MaxLevel { get; set; } = 10;

        // Time Constraints
        /// <summary>
        /// Number of days before unclaimed gift pack expires
        /// TODO: Map to native timer field (possibly qword deadline format)
        /// See: qword timing pattern fdiv 86400.0 + fadd for absolute deadline
        /// </summary>
        public int ExpireAfterDays { get; set; } = 7;

        /// <summary>
        /// Only one gift pack per account (cross-character restriction)
        /// TODO: Verify if account-level tracking exists in native
        /// </summary>
        public bool OncePerAccount { get; set; } = true;

        /// <summary>
        /// Only one gift pack per character
        /// TODO: Map to persistence (HumDataDB record field or quest store)
        /// </summary>
        public bool OncePerCharacter { get; set; } = false;

        // Notification
        /// <summary>
        /// Display welcome message when player logs in
        /// </summary>
        public bool ShowWelcomeMessage { get; set; } = true;

        /// <summary>
        /// Message color (native SysMsg color format)
        /// 0xFFDB = Green, 0x38FF = Red, 0xFCFF = Blue
        /// See: SysMsg颜色cx拆包 (sysmsg-cx-color-packing.md)
        /// </summary>
        public int WelcomeMessageColor { get; set; } = 0xFFDB; // Green

        // TODO: Add gift pack item list structure once item granting subsystem is reversed
        // TODO: Add reward tiers based on character class or achievements
        // TODO: Map native packet format for gift pack claim (SM 0x1190 response)
    }
}
