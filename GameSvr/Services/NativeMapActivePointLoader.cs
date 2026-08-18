using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Loads <c>MapActivePoint.xml</c> and writes each map's required active-point
    /// threshold into <see cref="Envirnoment.NativeMapActivePointRequired"/>
    /// (native <c>TEnvironment+0x30</c>).
    ///
    /// Native producers:
    ///   <c>sub_618FB8</c> @0x00618FB8 — boot + @ReloadMapActivePoint (0x0062ADE9)
    ///   <c>sub_61927C</c> @0x0061927C — same XML walk with TActivePointMgr error sink
    ///
    /// Consumer: <c>sub_619848</c> @0x00619848 (<c>CanEnterActiveMap</c> PAS API).
    /// </summary>
    public static class NativeMapActivePointLoader
    {
        internal const uint NativeLoadVa = 0x00618FB8;
        internal const uint NativeReloadCmdVa = 0x0061927C;

        public static string DefaultFilePath =>
            Path.Combine(M2Share.sRootPath, "Share", "EngineConfig",
                "\u4fe1\u7528\u5206\u7ba1\u7406", "MapActivePoint.xml");

        /// <summary>
        /// Unconfigured maps keep a non-zero sentinel so auth-on comparisons fail closed
        /// (native ctor places a TList pointer at +0x30 — see PasApiBridge CanEnterActiveMap note).
        /// </summary>
        internal const int UnconfiguredRequiredSentinel = int.MaxValue;

        public static int GetTotalActivePoint(TPlayObject player)
        {
            if (player == null)
                return 0;
            return unchecked(player.m_nActivePoint
                + (M2Share.ActivityPointManager?.Calculate(player) ?? 0));
        }

        /// <summary>Faithful port of <c>sub_619848</c> @0x00619848.</summary>
        public static bool CanEnterActiveMap(TPlayObject player, Envirnoment environment)
        {
            if (player == null || environment == null)
                return false;

            var required = environment.NativeMapActivePointRequired;
            if (!M2Share.g_Config.boAuthOpen)
                return true;

            if (required <= 0)
                return true;

            if (GetTotalActivePoint(player) >= required)
                return true;

            M2Share.g_FunctionNPC?.GotoLable(player, "@PlayerActiveWithMap", false);
            return false;
        }

        public static bool CanEnterActiveMap(TPlayObject player, string mapName)
        {
            if (string.IsNullOrEmpty(mapName))
                return false;
            var environment = M2Share.MapManager?.FindMap(mapName);
            return environment != null && CanEnterActiveMap(player, environment);
        }

        public static bool TryApply(string fileName, out string error)
        {
            error = string.Empty;
            try
            {
                if (string.IsNullOrWhiteSpace(fileName) || !File.Exists(fileName))
                {
                    error = " 地图信用分配置文件不存在!";
                    return false;
                }

                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                };
                using var reader = XmlReader.Create(fileName, settings);
                var document = XDocument.Load(reader, LoadOptions.None);
                if (document.Root == null)
                {
                    error = "缺少根节点";
                    return false;
                }

                var mapsElement = document.Root.Element("Maps");
                if (mapsElement == null)
                {
                    error = "缺少Maps节点";
                    return false;
                }

                var manager = M2Share.MapManager;
                if (manager == null)
                {
                    error = "MapManager unavailable";
                    return false;
                }

                foreach (var mapElement in mapsElement.Elements("Map"))
                {
                    var mapName = mapElement.Attribute("Name")?.Value;
                    if (string.IsNullOrWhiteSpace(mapName))
                        continue;

                    var valueText = mapElement.Attribute("Value")?.Value ?? "0";
                    if (!int.TryParse(valueText, NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out var required))
                    {
                        error = $"地图信用分配置文件未知错误 (bad Value for {mapName})";
                        return false;
                    }

                    var environment = manager.FindMap(mapName);
                    if (environment != null)
                        environment.NativeMapActivePointRequired = required;
                }

                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or XmlException or FormatException)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
