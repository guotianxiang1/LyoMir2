using System;
using System.Collections.Generic;
using System.IO;
using SystemModule;

namespace GameSvr.ConfigLoaders
{
    /// <summary>
    /// 封印物品配置加载器
    /// 从 Config\ItemBuild.ini 加载封印物品和淬炼配置
    ///
    /// 逆向来源: 战神引擎 0x006A0E88
    /// 配置文件: Config\ItemBuild.ini (EA: 0x6A11E8)
    /// </summary>
    public sealed class SealedItemConfigLoader
    {
        // ============================================================
        // 常量定义 - 战神引擎字符串常量
        // ============================================================

        /// <summary>
        /// 配置文件路径 (EA: 0x6A11E8)
        /// </summary>
        private const string CONFIG_FILE_PATH = "Config\\ItemBuild.ini";

        /// <summary>
        /// 封印物品配置section名 (EA: 0x6A1208)
        /// </summary>
        private const string SECTION_SEAL_ITEMS = "封印物品";

        /// <summary>
        /// 淬炼列表配置section名 (EA: 0x6A124C)
        /// </summary>
        private const string SECTION_TEMPER_LIST = "淬炼列表";

        // ============================================================
        // 预定义基线物品 (EA: 0x6A0EB2-0x6A0F07)
        // 这5个物品在加载时首先解析索引
        // ============================================================

        private const string BASELINE_ITEM_1 = "火云石碎片";  // EA: 0x6A118C
        private const string BASELINE_ITEM_2 = "火云石";      // EA: 0x6A118C
        private const string BASELINE_ITEM_3 = "弩牌";        // EA: 0x646AC8
        private const string BASELINE_ITEM_4 = "魔龙冰晶";    // EA: 0x6A11C0
        private const string BASELINE_ITEM_5 = "火云晶石";    // EA: 0x6A11D4

        // ============================================================
        // 配置数据结构
        // ============================================================

        /// <summary>
        /// 封印物品配置数据
        /// 存储结构对应战神引擎 ebx+0x04~0x14 (基线), +0x18 (封印列表), +0x1C/+0x20 (淬炼列表)
        /// </summary>
        public class SealedItemConfig
        {
            /// <summary>
            /// 基线物品索引数组 (5个预定义物品)
            /// 对应 ebx+0x04, +0x08, +0x0C, +0x10, +0x14
            /// </summary>
            public int[] BaselineItemIndices { get; set; } = new int[5];

            /// <summary>
            /// 封印物品索引列表
            /// 对应 ebx+0x18 (TList)
            /// </summary>
            public List<int> SealItemIndices { get; set; } = new List<int>();

            /// <summary>
            /// 淬炼源物品索引列表
            /// 对应 ebx+0x1C (TList)
            /// </summary>
            public List<int> TemperSourceIndices { get; set; } = new List<int>();

            /// <summary>
            /// 淬炼目标物品索引列表
            /// 对应 ebx+0x20 (TList)
            /// 与TemperSourceIndices一一对应
            /// </summary>
            public List<int> TemperTargetIndices { get; set; } = new List<int>();

            /// <summary>
            /// 加载是否成功
            /// </summary>
            public bool LoadSucceeded { get; set; }

            /// <summary>
            /// 检查物品是否在封印列表中
            /// </summary>
            public bool IsSealItem(int itemIdx)
            {
                return SealItemIndices.Contains(itemIdx);
            }

            /// <summary>
            /// 获取淬炼目标物品索引
            /// </summary>
            /// <param name="sourceIdx">源物品索引</param>
            /// <returns>目标物品索引，未找到返回-1</returns>
            public int GetTemperTarget(int sourceIdx)
            {
                int index = TemperSourceIndices.IndexOf(sourceIdx);
                if (index >= 0 && index < TemperTargetIndices.Count)
                    return TemperTargetIndices[index];
                return -1;
            }
        }

        // ============================================================
        // 加载方法
        // ============================================================

        /// <summary>
        /// 加载封印物品配置
        ///
        /// 逆向来源: 战神引擎 0x006A0E88
        /// 加载流程:
        /// 1. 解析5个基线物品索引 (EA: 0x6A0EB2-0x6A0F0C)
        /// 2. 验证配置文件存在 (EA: 0x6A0F24-0x6A0F2E)
        /// 3. 清空列表 (EA: 0x6A0F38-0x6A0F4D)
        /// 4. 读取"封印物品"section (EA: 0x6A0F86-0x6A1029)
        /// 5. 读取"淬炼列表"section (EA: 0x6A1033-0x6A111B)
        /// </summary>
        /// <param name="userEngine">UserEngine实例，用于查找物品索引 (调用0x74C1E0)</param>
        /// <returns>配置对象，失败时LoadSucceeded=false</returns>
        public static SealedItemConfig LoadConfig(UserEngine userEngine)
        {
            var config = new SealedItemConfig();

            if (userEngine == null)
            {
                M2Share.MainOutMessage("[Error] SealedItemConfigLoader: UserEngine为null");
                return config;
            }

            try
            {
                // Phase 1: 解析5个基线物品 (EA: 0x6A0EB2-0x6A0F0C)
                // 调用 0x74C1E0 (UserEngine.GetStdItemIdx) 5次
                config.BaselineItemIndices[0] = userEngine.GetStdItemIdx(BASELINE_ITEM_1);
                config.BaselineItemIndices[1] = userEngine.GetStdItemIdx(BASELINE_ITEM_2);
                config.BaselineItemIndices[2] = userEngine.GetStdItemIdx(BASELINE_ITEM_3);
                config.BaselineItemIndices[3] = userEngine.GetStdItemIdx(BASELINE_ITEM_4);
                config.BaselineItemIndices[4] = userEngine.GetStdItemIdx(BASELINE_ITEM_5);

                // Phase 2: 验证配置文件存在 (EA: 0x6A0F24-0x6A0F2E)
                // 调用 0x40CF2C (FileExists)
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CONFIG_FILE_PATH);
                if (!File.Exists(configPath))
                {
                    M2Share.MainOutMessage($"[Error] 配置文件不存在: {CONFIG_FILE_PATH}");
                    return config;
                }

                // Phase 3: 清空列表 (EA: 0x6A0F38-0x6A0F4D)
                // 调用 VMT+0x08 三次 (TList.Clear)
                config.SealItemIndices.Clear();
                config.TemperSourceIndices.Clear();
                config.TemperTargetIndices.Clear();

                // Phase 4: 初始化失败标志 (EA: 0x6A0F34)
                bool errorFlag = false;

                // Phase 5: 读取"封印物品"section (EA: 0x6A0F86-0x6A1029)
                var sealItems = ReadIniSection(configPath, SECTION_SEAL_ITEMS);
                foreach (var itemName in sealItems)
                {
                    if (string.IsNullOrWhiteSpace(itemName))
                        continue;

                    // 调用 0x74C1E0 查找物品索引 (EA: 0x6A0FE6)
                    int itemIdx = userEngine.GetStdItemIdx(itemName.Trim());

                    if (itemIdx == -1)
                    {
                        // 设置错误标志并记录日志 (EA: 0x6A0FF2-0x6A1012)
                        errorFlag = true;
                        string errorMsg = $"[Error]: 配置错误-不存在的封印物品：{itemName}";
                        M2Share.MainOutMessage(errorMsg);
                    }
                    else
                    {
                        // 添加到列表 (EA: 0x6A101E, call 0x424AB8 = TList.Add)
                        config.SealItemIndices.Add(itemIdx);
                    }
                }

                // Phase 6: 如果封印物品有错误，跳过淬炼列表 (EA: 0x6A1029)
                if (errorFlag)
                {
                    config.LoadSucceeded = false;
                    return config;
                }

                // Phase 7: 读取"淬炼列表"section (EA: 0x6A1033-0x6A111B)
                var temperItems = ReadIniSection(configPath, SECTION_TEMPER_LIST);
                foreach (var line in temperItems)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    // 每行格式: "物品名1=物品名2"
                    // 调用 VMT+0x0C 和 VMT+0x?? 获取键值对 (EA: 0x6A106F, 0x6A107D)
                    var parts = line.Split('=');
                    if (parts.Length != 2)
                        continue;

                    string item1 = parts[0].Trim();
                    string item2 = parts[1].Trim();

                    // 查找两个物品索引 (EA: 0x6A108C, 0x6A109D)
                    int idx1 = userEngine.GetStdItemIdx(item1);
                    int idx2 = userEngine.GetStdItemIdx(item2);

                    if (idx1 == -1 || idx2 == -1)
                    {
                        // 记录错误 (EA: 0x6A10B0-0x6A10DE)
                        string errorMsg = $"[Error]: 配置错误-不存在的淬炼物品：{item1}={item2}";
                        M2Share.MainOutMessage(errorMsg);
                        continue;
                    }

                    // 添加到两个列表 (EA: 0x6A10E5, 0x6A10F0)
                    config.TemperSourceIndices.Add(idx1);
                    config.TemperTargetIndices.Add(idx2);
                }

                config.LoadSucceeded = true;
                M2Share.MainOutMessage($"[配置] 成功加载 {CONFIG_FILE_PATH}: 封印物品{config.SealItemIndices.Count}个, 淬炼配置{config.TemperSourceIndices.Count}组");
                return config;
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[Exception] 加载{CONFIG_FILE_PATH}失败: {ex.Message}");
                config.LoadSucceeded = false;
                return config;
            }
        }

        // ============================================================
        // 辅助方法
        // ============================================================

        /// <summary>
        /// 读取INI文件的section内容
        /// 战神引擎使用TStringList读取，按行解析
        /// </summary>
        /// <param name="filePath">配置文件路径</param>
        /// <param name="sectionName">section名称</param>
        /// <returns>section内容行列表</returns>
        private static List<string> ReadIniSection(string filePath, string sectionName)
        {
            var result = new List<string>();
            bool inSection = false;

            try
            {
                foreach (var line in File.ReadLines(filePath, HUtil32.GbkEncoding))
                {
                    string trimmed = line.Trim();

                    // 检查section标记
                    if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                    {
                        string currentSection = trimmed.Substring(1, trimmed.Length - 2);
                        inSection = string.Equals(currentSection, sectionName, StringComparison.Ordinal);
                        continue;
                    }

                    // 在目标section内，收集非空行和非注释行
                    if (inSection && !string.IsNullOrWhiteSpace(trimmed) &&
                        !trimmed.StartsWith(";") &&
                        !trimmed.StartsWith("//") &&
                        !trimmed.StartsWith("#"))
                    {
                        result.Add(trimmed);
                    }
                }
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[Exception] 读取INI section {sectionName} 失败: {ex.Message}");
            }

            return result;
        }
    }
}
