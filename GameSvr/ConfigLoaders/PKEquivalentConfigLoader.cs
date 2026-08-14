using System;
using System.IO;
using SystemModule.Common;

namespace GameSvr.ConfigLoaders
{
    /// <summary>
    /// Loads PK equivalent configuration from config/PKEquivalent.ini
    /// Provides default values when file is missing or parse fails
    /// </summary>
    public class PKEquivalentConfigLoader : IniFile
    {
        private const string DefaultConfigPath = "config/PKEquivalent.ini";

        public PKEquivalentConfigLoader(string fileName) : base(fileName)
        {
        }

        /// <summary>
        /// Loads configuration from PKEquivalent.ini
        /// Returns true on success, false on failure (with defaults applied)
        /// </summary>
        public bool LoadConfig()
        {
            try
            {
                if (!File.Exists(FileName))
                {
                    M2Share.MainOutMessage($"[配置] PKEquivalent.ini 不存在，使用默认值");
                    return false;
                }

                Load();
                M2Share.MainOutMessage("[配置] PKEquivalent.ini 加载成功");
                return true;
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[配置] PKEquivalent.ini 解析失败: {ex.Message}，使用默认值");
                return false;
            }
        }

        /// <summary>
        /// Creates a loader instance with default config path
        /// </summary>
        public static PKEquivalentConfigLoader CreateDefault()
        {
            return new PKEquivalentConfigLoader(DefaultConfigPath);
        }

        // Add config property readers here as needed, for example:
        // public int GetSomeValue() => ReadInteger("Section", "Key", defaultValue);
    }
}
