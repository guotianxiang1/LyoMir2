using System;
using System.IO;
using SystemModule.Common;

namespace GameSvr.ConfigLoaders
{
    /// <summary>
    /// 衣服升级配置加载器
    /// Clothes Upgrade System Configuration Loader
    ///
    /// Native Function: TPlayObject.UpgradeClothes
    /// VA: 0x00??????  [PLACEHOLDER - needs reverse engineering]
    /// Binary: M2Server_reunpacked_20260803.exe (MD5: 2ad31a8a)
    /// </summary>
    public class ClothesUpgradeConfig : IniFile
    {
        private const string ConfigFileName = "config/ClothesUpgrade.ini";

        public ClothesUpgradeConfig(string filePath) : base(filePath)
        {
        }

        /// <summary>
        /// 加载衣服升级配置
        /// Loads clothes upgrade configuration from config/ClothesUpgrade.ini
        /// Returns configuration object with defaults on failure
        /// </summary>
        public static ClothesUpgradeSettings LoadConfig(string baseDir)
        {
            var config = new ClothesUpgradeSettings();
            var configPath = Path.Combine(baseDir, ConfigFileName);

            if (!File.Exists(configPath))
            {
                M2Share.MainOutMessage($"[配置] {ConfigFileName} 不存在，使用默认值");
                return config;
            }

            try
            {
                var loader = new ClothesUpgradeConfig(configPath);
                loader.Load();

                // System settings
                config.Enabled = loader.ReadBool("System", "Enabled", true);
                config.MaxUpgradeLevel = loader.ReadInteger("System", "MaxUpgradeLevel", 10);
                config.MinUpgradeLevel = loader.ReadInteger("System", "MinUpgradeLevel", 0);

                // Material settings
                // Native: 黑铁矿石 collection loop at VA 0x00??????
                config.MaxMaterialCount = loader.ReadInteger("Material", "MaxMaterialCount", 5);
                config.MaterialItemName = loader.ReadString("Material", "MaterialItemName", "黑铁矿石");
                config.RequireMaterialCheck = loader.ReadBool("Material", "RequireMaterialCheck", true);

                // Success rate calculation
                // Native: Durability divisor constant at VA 0x00??????
                config.DurabilityDivisor = loader.ReadInteger("Rate", "DurabilityDivisor", 5000);
                config.BaseSuccessRate = loader.ReadInteger("Rate", "BaseSuccessRate", 50);
                config.MaxSuccessRate = loader.ReadInteger("Rate", "MaxSuccessRate", 100);
                config.MinSuccessRate = loader.ReadInteger("Rate", "MinSuccessRate", 0);

                // Upgrade result settings
                // Native: Success handler at VA 0x00??????
                // Native: Failure handler at VA 0x00??????
                config.DamageOnFailure = loader.ReadInteger("Result", "DamageOnFailure", 1);
                config.DestroyOnFailure = loader.ReadBool("Result", "DestroyOnFailure", false);
                config.SendSuccessMessage = loader.ReadBool("Result", "SendSuccessMessage", true);
                config.SendFailureMessage = loader.ReadBool("Result", "SendFailureMessage", true);

                // Notification settings
                config.SuccessMessageText = loader.ReadString("Message", "SuccessText", "你的衣服升级成功");
                config.FailureMessageText = loader.ReadString("Message", "FailureText", "你的衣服升级失败");
                config.MaterialCollectedText = loader.ReadString("Message", "MaterialCollectedText", "衣服升级收取");

                M2Share.MainOutMessage($"[配置] 成功加载 {ConfigFileName}");
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[配置] 加载 {ConfigFileName} 失败: {ex.Message}，使用默认值");
            }

            return config;
        }

        /// <summary>
        /// 验证配置值有效性
        /// Validates configuration ranges and constraints
        /// </summary>
        public static bool ValidateConfig(ClothesUpgradeSettings config)
        {
            bool isValid = true;

            if (config.MaxUpgradeLevel < 1 || config.MaxUpgradeLevel > 20)
            {
                M2Share.MainOutMessage($"[警告] MaxUpgradeLevel={config.MaxUpgradeLevel} 超出范围 [1-20]，重置为10");
                config.MaxUpgradeLevel = 10;
                isValid = false;
            }

            if (config.MinUpgradeLevel < 0 || config.MinUpgradeLevel >= config.MaxUpgradeLevel)
            {
                M2Share.MainOutMessage($"[警告] MinUpgradeLevel={config.MinUpgradeLevel} 无效，重置为0");
                config.MinUpgradeLevel = 0;
                isValid = false;
            }

            if (config.MaxMaterialCount < 1 || config.MaxMaterialCount > 10)
            {
                M2Share.MainOutMessage($"[警告] MaxMaterialCount={config.MaxMaterialCount} 超出范围 [1-10]，重置为5");
                config.MaxMaterialCount = 5;
                isValid = false;
            }

            if (config.DurabilityDivisor <= 0)
            {
                M2Share.MainOutMessage($"[警告] DurabilityDivisor={config.DurabilityDivisor} 无效，重置为5000");
                config.DurabilityDivisor = 5000;
                isValid = false;
            }

            if (config.BaseSuccessRate < 0 || config.BaseSuccessRate > 100)
            {
                M2Share.MainOutMessage($"[警告] BaseSuccessRate={config.BaseSuccessRate} 超出范围 [0-100]，重置为50");
                config.BaseSuccessRate = 50;
                isValid = false;
            }

            return isValid;
        }
    }

    /// <summary>
    /// 衣服升级配置数据类
    /// Clothes upgrade system configuration data
    /// </summary>
    public class ClothesUpgradeSettings
    {
        // System settings
        public bool Enabled { get; set; } = true;
        public int MaxUpgradeLevel { get; set; } = 10;
        public int MinUpgradeLevel { get; set; } = 0;

        // Material settings
        /// <summary>
        /// 最大材料数量 (Native: MAX_BLACK_IRON_COUNT = 5)
        /// Maximum material count that can contribute to upgrade
        /// </summary>
        public int MaxMaterialCount { get; set; } = 5;

        /// <summary>
        /// 材料物品名称 (Native: "黑铁矿石" at VA 0x00??????)
        /// Material item name for upgrade
        /// </summary>
        public string MaterialItemName { get; set; } = "黑铁矿石";

        public bool RequireMaterialCheck { get; set; } = true;

        // Success rate calculation
        /// <summary>
        /// 耐久度除数 (Native: DURABILITY_DIVISOR = 5000 at VA 0x00??????)
        /// Divisor for converting raw durability to rate calculation
        /// </summary>
        public int DurabilityDivisor { get; set; } = 5000;

        public int BaseSuccessRate { get; set; } = 50;
        public int MaxSuccessRate { get; set; } = 100;
        public int MinSuccessRate { get; set; } = 0;

        // Upgrade result settings
        /// <summary>
        /// 失败时扣除耐久 (Native: DamageItem call at VA 0x00??????)
        /// Durability damage on upgrade failure
        /// </summary>
        public int DamageOnFailure { get; set; } = 1;

        public bool DestroyOnFailure { get; set; } = false;
        public bool SendSuccessMessage { get; set; } = true;
        public bool SendFailureMessage { get; set; } = true;

        // Notification messages
        /// <summary>
        /// 成功消息 (Native: "你的首饰升级成功" at VA 0x00??????)
        /// Success notification message
        /// </summary>
        public string SuccessMessageText { get; set; } = "你的衣服升级成功";

        /// <summary>
        /// 失败消息
        /// Failure notification message
        /// </summary>
        public string FailureMessageText { get; set; } = "你的衣服升级失败";

        /// <summary>
        /// 材料收取消息 (Native: "首饰升级收取" at VA 0x00??????)
        /// Material collection notification message
        /// </summary>
        public string MaterialCollectedText { get; set; } = "衣服升级收取";
    }
}
