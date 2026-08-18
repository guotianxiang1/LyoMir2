using System.Globalization;
using System.Xml.Linq;
using SystemModule.Common;

namespace GameSvr
{
    internal sealed class NativeLimitBagItemDropRule
    {
        internal NativeLimitBagItemDropRule(string itemName, int rnd,
            int ranger)
        {
            ItemName = itemName ?? string.Empty;
            Rnd = rnd;
            Ranger = ranger;
        }

        internal string ItemName { get; }
        internal int Rnd { get; }
        internal int Ranger { get; }
    }

    internal sealed class NativeLimitBagItemDropState
    {
        private readonly Dictionary<string, NativeLimitBagItemDropRule> _rules =
            new(StringComparer.Ordinal);

        internal int Count => _rules.Count;

        internal bool TryAdd(string itemName, int rnd, int ranger)
        {
            itemName ??= string.Empty;
            return _rules.TryAdd(itemName,
                new NativeLimitBagItemDropRule(itemName, rnd, ranger));
        }

        internal bool TryGet(string itemName,
            out NativeLimitBagItemDropRule rule)
        {
            return _rules.TryGetValue(itemName ?? string.Empty, out rule);
        }
    }

    internal static class NativeLimitBagItemDropLoader
    {
        internal const uint OriginalLoader = 0x00697FD4;
        internal const uint OriginalAddRule = 0x0077BF50;
        internal const uint OriginalFindRule = 0x0077C028;
        internal const string ConfigFileName = "MapDropLimitBagItems.xml";
        internal const string MainConfigFileName = "main.ini";
        internal const string ConfigDirectory =
            "\u9759\u6001\u5730\u56fe\u7ba1\u7406";
        internal const string ConfigSection =
            "\u90e8\u5206\u5730\u56fe\u80cc\u5305\u7269\u54c1\u6389\u843d\u9650\u5236";

        internal static string GetDefaultPath(string rootPath)
        {
            return Path.Combine(rootPath ?? string.Empty, "Share",
                "EngineConfig", ConfigDirectory, ConfigFileName);
        }

        internal static bool TryResolveAutoLoadFile(string rootPath,
            out bool autoLoad, out string fileName, out string error)
        {
            autoLoad = false;
            fileName = string.Empty;
            error = string.Empty;
            var moduleDirectory = Path.Combine(rootPath ?? string.Empty,
                "Share", "EngineConfig", ConfigDirectory);
            var mainFileName = Path.Combine(moduleDirectory,
                MainConfigFileName);
            if (!File.Exists(mainFileName))
            {
                error = "LIMITBAGITEMDROP main.ini was not found: "
                        + mainFileName;
                return false;
            }

            try
            {
                var config = new ReadOnlyModuleConfig(mainFileName);
                if (!config.GetAutoStart())
                    return true;

                if (!config.HasSection(ConfigSection))
                {
                    error = "LIMITBAGITEMDROP main.ini is missing section: "
                            + ConfigSection;
                    return false;
                }

                autoLoad = config.GetAutoLoad(ConfigSection);
                if (!autoLoad)
                    return true;

                var configuredName = config.GetFileName(ConfigSection);
                if (string.IsNullOrWhiteSpace(configuredName))
                {
                    error = "LIMITBAGITEMDROP main.ini FileName is empty";
                    return false;
                }

                fileName = Path.GetFullPath(Path.IsPathRooted(configuredName)
                    ? configuredName
                    : Path.Combine(moduleDirectory, configuredName));
                return true;
            }
            catch (Exception exception) when (exception is not
                                               OutOfMemoryException)
            {
                error = exception.Message;
                return false;
            }
        }

        internal static bool TryApply(string fileName,
            Func<string, Envirnoment> findMap, out string error,
            Action<string> diagnostic = null)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                error = "MapDropLimitBagItems.xml path is empty";
                return false;
            }
            if (findMap == null)
            {
                error = "map resolver is null";
                return false;
            }
            if (!File.Exists(fileName))
            {
                error = "MapDropLimitBagItems.xml was not found: " + fileName;
                return false;
            }

            try
            {
                var document = XDocument.Load(fileName, LoadOptions.None);
                var mapsElement = document.Root?.Element("Maps");
                if (mapsElement == null)
                {
                    error = "MapDropLimitBagItems.xml is missing Maps";
                    return false;
                }

                foreach (var mapElement in mapsElement.Elements())
                {
                    var mapName = NormalizeMapName(
                        mapElement.Element("Name")?.Value);
                    var environment = findMap(mapName);
                    if (environment == null)
                    {
                        diagnostic?.Invoke("地图不存在: " + mapName);
                        continue;
                    }
                    if (environment.Flag?.boLIMITBAGITEMDROP != true)
                    {
                        diagnostic?.Invoke("地图配置在MapInfo.txt 中不是LIMITBAGITEMDROP属性: "
                                           + mapName);
                        continue;
                    }

                    var itemsElement = mapElement.Element("Items");
                    if (itemsElement == null)
                        continue;

                    // sub_77BF50 inserts only when the exact, case-sensitive
                    // item-name key is absent, so the first duplicate wins.
                    foreach (var itemElement in itemsElement.Elements())
                    {
                        var itemName = itemElement.Attribute("Name")?.Value
                                       ?? string.Empty;
                        var rnd = ReadInt(itemElement.Attribute("Rnd")?.Value);
                        var ranger = ReadInt(
                            itemElement.Attribute("Ranger")?.Value);
                        environment.NativeLimitBagItemDrops.TryAdd(itemName,
                            rnd, ranger);
                    }
                }

                return true;
            }
            catch (Exception exception) when (exception is not
                                               OutOfMemoryException)
            {
                error = exception.Message;
                return false;
            }
        }

        private static int ReadInt(string value)
        {
            return int.TryParse(value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0;
        }

        private static string NormalizeMapName(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var start = 0;
            var end = value.Length - 1;
            while (start <= end && value[start] <= ' ')
                start++;
            while (end >= start && value[end] <= ' ')
                end--;
            if (start > end)
                return string.Empty;

            var result = value.Substring(start, end - start + 1).ToCharArray();
            for (var index = 0; index < result.Length; index++)
            {
                if (result[index] >= 'a' && result[index] <= 'z')
                    result[index] = (char)(result[index] - ('a' - 'A'));
            }
            return new string(result);
        }

        private sealed class ReadOnlyModuleConfig : IniFile
        {
            internal ReadOnlyModuleConfig(string fileName) : base(fileName)
            {
                Load();
            }

            internal bool HasSection(string section)
            {
                return ContainSectionName(section);
            }

            internal bool GetAutoStart()
            {
                return ReadBool("Set", "AutoStart", false);
            }

            internal bool GetAutoLoad(string section)
            {
                return ReadBool(section, "AutoLoad", true);
            }

            internal string GetFileName(string section)
            {
                return ReadString(section, "FileName", string.Empty);
            }
        }
    }
}
