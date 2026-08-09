using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace GameSvr.PasEngine
{
    public partial class PasApiBridge
    {
        private const string NativeStrengthenEquipDirectory =
            "\u88c5\u5907\u5408\u6210\u7ba1\u7406";
        private static readonly object NativeStrengthenEquipSync = new();
        private static string _nativeStrengthenEquipPath;
        private static int[] _nativeStrengthenEquipLimits;

        private static bool TryGetNativeMaxStrengthenEquipLevel(
            TPlayObject player, out int result)
        {
            result = 0;
            if (player?.m_Abil == null
                || !TryGetNativeStrengthenEquipLimits(out var limits))
                return false;

            var playerLevel = player.m_Abil.Level;
            for (var i = 0; i < limits.Length; i++)
            {
                if (limits[i] <= playerLevel)
                    result = i + 1;
            }
            return true;
        }

        private static bool TryGetNativeStrengthenEquipLimits(
            out int[] limits)
        {
            limits = null;
            if (string.IsNullOrWhiteSpace(M2Share.sRootPath)) return false;

            var fileName = Path.Combine(M2Share.sRootPath, "Share",
                "EngineConfig", NativeStrengthenEquipDirectory,
                "StrengthenEquip.xml");
            lock (NativeStrengthenEquipSync)
            {
                if (_nativeStrengthenEquipLimits != null
                    && string.Equals(_nativeStrengthenEquipPath, fileName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    limits = _nativeStrengthenEquipLimits;
                    return true;
                }

                if (!TryLoadNativeStrengthenEquipLimits(fileName,
                        out var loaded))
                    return false;
                _nativeStrengthenEquipPath = fileName;
                _nativeStrengthenEquipLimits = loaded;
                limits = loaded;
                return true;
            }
        }

        private static bool TryLoadNativeStrengthenEquipLimits(
            string fileName, out int[] limits)
        {
            limits = null;
            try
            {
                if (!File.Exists(fileName)) return false;
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                };
                using var reader = XmlReader.Create(fileName, settings);
                var document = XDocument.Load(reader, LoadOptions.None);
                if (document.Root?.Name != "Describle") return false;
                var entries = document.Root.Element("Info")?
                    .Elements("EquipLevel").ToArray();
                if (entries == null || entries.Length == 0) return false;

                var loaded = new int[entries.Length];
                for (var i = 0; i < entries.Length; i++)
                {
                    if (!int.TryParse((string)entries[i].Attribute("Level"),
                            NumberStyles.Integer, CultureInfo.InvariantCulture,
                            out var level)
                        || level != i + 1
                        || !int.TryParse(
                            (string)entries[i].Attribute("LimitLv"),
                            NumberStyles.Integer, CultureInfo.InvariantCulture,
                            out loaded[i]))
                        return false;
                }

                limits = loaded;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
