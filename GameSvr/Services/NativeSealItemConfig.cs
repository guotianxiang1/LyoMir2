using System;
using System.Collections.Generic;
using System.IO;
using SystemModule;
using SystemModule.Common;

namespace GameSvr
{
    /// <summary>
    /// Config\ItemBuild.ini loader — native sub_6A0E88 @0x6A0E88.
    /// Pre-resolves five baseline item names from UserEngine (0x74C1E0 calls
    /// at 0x6A0EB2..0x6A0F07), then reads:
    ///   section "封印物品" — each entry must exist in std-item db
    ///   section "淬炼列表" — same validation
    /// Missing items log "[Error]: 配置错误-不存在的封印物品：" + name.
    /// </summary>
    public sealed class NativeSealItemConfig
    {
        public const string ConfigRelativePath = @"Share\config\ItemBuild.ini";
        private const string SealSection = "封印物品";
        private const string RefineSection = "淬炼列表";

        private static readonly string[] NativeBaselineItemNames =
        {
            "火云石碎片", "火云石", "弩牌", "魔龙冰晶", "火云晶石"
        };

        private static readonly NativeSealItemConfig _shared = new NativeSealItemConfig();
        public static NativeSealItemConfig Shared => _shared;

        private readonly List<string> _sealedItems = new List<string>();
        private readonly List<string> _refineItems = new List<string>();
        private readonly int[] _baselineItemIds = new int[5];

        public IReadOnlyList<string> SealedItems => _sealedItems;
        public IReadOnlyList<string> RefineItems => _refineItems;

        public static string ResolveDefaultPath(string rootPath, string baseDir)
        {
            return Path.Combine(rootPath ?? string.Empty, baseDir ?? string.Empty,
                "config", "ItemBuild.ini");
        }

        public bool IsSealedItem(string itemName)
        {
            if (string.IsNullOrEmpty(itemName))
                return false;
            for (var i = 0; i < _sealedItems.Count; i++)
            {
                if (string.Equals(_sealedItems[i], itemName,
                        StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public bool Reload(string fileName, out string error)
        {
            error = string.Empty;
            _sealedItems.Clear();
            _refineItems.Clear();
            Array.Clear(_baselineItemIds, 0, _baselineItemIds.Length);

            var engine = M2Share.UserEngine;
            if (engine == null)
            {
                error = "UserEngine unavailable";
                return false;
            }

            for (var i = 0; i < NativeBaselineItemNames.Length; i++)
            {
                if (engine.GetStdItemIdx(NativeBaselineItemNames[i]) < 0)
                    _baselineItemIds[i] = -1;
                else
                    _baselineItemIds[i] = engine.GetStdItemIdx(NativeBaselineItemNames[i]);
            }

            if (string.IsNullOrWhiteSpace(fileName) || !File.Exists(fileName))
            {
                error = "Config\\ItemBuild.ini missing";
                return false;
            }

            var ini = new ItemBuildIni(fileName);
            var hadError = false;

            LoadSection(ini, SealSection, _sealedItems, engine, ref hadError);
            LoadSection(ini, RefineSection, _refineItems, engine, ref hadError);

            if (hadError)
            {
                error = "seal/refine list validation failed";
                return false;
            }

            return true;
        }

        private static void LoadSection(ItemBuildIni ini, string section,
            List<string> target, UserEngine engine, ref bool hadError)
        {
            foreach (var name in ini.ReadSectionValues(section))
            {
                if (engine.GetStdItemIdx(name) < 0)
                {
                    M2Share.ErrorMessage(
                        "[Error]: 配置错误-不存在的封印物品：" + name);
                    hadError = true;
                    continue;
                }

                target.Add(name);
            }
        }

        private sealed class ItemBuildIni : IniFile
        {
            internal ItemBuildIni(string path) : base(path) { }

            internal IEnumerable<string> ReadSectionValues(string section)
            {
                foreach (var value in GetAllValues(section))
                {
                    if (!string.IsNullOrWhiteSpace(value))
                        yield return value.Trim();
                }
            }
        }
    }
}
