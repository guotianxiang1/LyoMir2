using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// 封印物品和淬炼配置加载器 - 战神引擎 0x006A0E88
    /// 从 Config\ItemBuild.ini 加载配置
    /// </summary>
    internal sealed class NativeSealItemConfig
    {
        // 战神引擎字符串常量 (EA地址标注)
        private const string SECTION_SEAL_ITEMS = "封印物品";    // EA: 0x6A1208
        private const string SECTION_TEMPER_LIST = "淬炼列表";   // EA: 0x6A124C

        // 预定义基线物品 (EA: 0x6A0EB2-0x6A0F07)
        private const string BASELINE_ITEM_1 = "火云石碎片";     // EA: 0x6A118C
        private const string BASELINE_ITEM_2 = "火云石";         // EA: 0x6A118C
        private const string BASELINE_ITEM_3 = "弩牌";           // EA: 0x646AC8
        private const string BASELINE_ITEM_4 = "魔龙冰晶";       // EA: 0x6A11C0
        private const string BASELINE_ITEM_5 = "火云晶石";       // EA: 0x6A11D4

        private const string CONFIG_FILE_PATH = "Config\\ItemBuild.ini"; // EA: 0x6A11E8

        // 存储结构 (对应 ebx+0x04~0x14, +0x18, +0x1C, +0x20)
        private readonly int[] baselineItemIndices = new int[5];
        private readonly List<int> sealItemIndices = new List<int>();
        private readonly List<int> temperList1 = new List<int>();
        private readonly List<int> temperList2 = new List<int>();

        internal bool LoadSucceeded { get; private set; }

        /// <summary>
        /// 加载配置 - 战神引擎 0x006A0E88 完整复刻
        /// </summary>
        internal bool LoadConfig(UserEngine userEngine)
        {
            if (userEngine == null)
                return false;

            try
            {
                // Phase 1: 解析5个基线物品 (EA: 0x6A0EB2-0x6A0F0C)
                // 调用 0x74C1E0 (UserEngine.GetStdItemIdx) 5次
                baselineItemIndices[0] = userEngine.GetStdItemIdx(BASELINE_ITEM_1);
                baselineItemIndices[1] = userEngine.GetStdItemIdx(BASELINE_ITEM_2);
                baselineItemIndices[2] = userEngine.GetStdItemIdx(BASELINE_ITEM_3);
                baselineItemIndices[3] = userEngine.GetStdItemIdx(BASELINE_ITEM_4);
                baselineItemIndices[4] = userEngine.GetStdItemIdx(BASELINE_ITEM_5);

                // Phase 2: 验证配置文件存在 (EA: 0x6A0F24-0x6A0F2E)
                // 调用 0x40CF2C (FileExists)
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CONFIG_FILE_PATH);
                if (!File.Exists(configPath))
                {
                    M2Share.MainOutMessage($"[Error] 配置文件不存在: {CONFIG_FILE_PATH}");
                    return false;
                }

                // Phase 3: 清空列表 (EA: 0x6A0F38-0x6A0F4D)
                // 调用 VMT+0x08 三次 (List.Clear)
                sealItemIndices.Clear();
                temperList1.Clear();
                temperList2.Clear();

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
                        sealItemIndices.Add(itemIdx);
                    }
                }

                // Phase 6: 如果封印物品有错误，跳过淬炼列表 (EA: 0x6A1029)
                if (errorFlag)
                {
                    LoadSucceeded = false;
                    return false;
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
                    temperList1.Add(idx1);
                    temperList2.Add(idx2);
                }

                LoadSucceeded = true;
                return true;
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[Exception] 加载ItemBuild.ini失败: {ex.Message}");
                LoadSucceeded = false;
                return false;
            }
        }

        /// <summary>
        /// 读取INI文件的section内容
        /// </summary>
        private List<string> ReadIniSection(string filePath, string sectionName)
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
                    if (inSection && !string.IsNullOrWhiteSpace(trimmed) && !trimmed.StartsWith(";") && !trimmed.StartsWith("//"))
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

        /// <summary>
        /// 检查物品是否在封印列表中
        /// </summary>
        internal bool IsSealItem(int itemIdx)
        {
            return sealItemIndices.Contains(itemIdx);
        }

        /// <summary>
        /// 获取淬炼目标物品索引
        /// </summary>
        internal int GetTemperTarget(int sourceIdx)
        {
            int index = temperList1.IndexOf(sourceIdx);
            if (index >= 0 && index < temperList2.Count)
                return temperList2[index];
            return -1;
        }

        /// <summary>
        /// 获取基线物品索引数组（用于调试）
        /// </summary>
        internal int[] GetBaselineIndices() => baselineItemIndices;

        /// <summary>
        /// 获取封印物品数量
        /// </summary>
        internal int SealItemCount => sealItemIndices.Count;

        /// <summary>
        /// 获取淬炼配置数量
        /// </summary>
        internal int TemperConfigCount => temperList1.Count;
    }
}
