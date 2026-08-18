using SystemModule;
using SystemModule.Common;

namespace GameSvr.Services
{
    /// <summary>
    /// SuperMerchant.ini read/write validation (sub_616258 @0x00616258,
    /// sub_616484 @0x00616484).
    /// </summary>
    internal static class NativeSuperMerchantIniLoader
    {
        internal const uint ReadEa = 0x00616258;
        internal const uint WriteEa = 0x00616484;
        internal const string RelativePath = @"Config\SuperMerchant.ini";
        internal const string ReadItemNameErrorMessage =
            "读取SuperMerchant.ini中物品名称错误！";
        internal const string SaveItemNameErrorMessage =
            "保存SuperMerchant.ini中物品名称错误！";

        internal static bool TryValidateAtStartup(out string error)
        {
            error = string.Empty;
            var path = ResolvePath();
            if (!File.Exists(path))
                return true;

            try
            {
                var ini = new SuperMerchantIni(path);
                for (var goodsIndex = 1; goodsIndex <= 3; goodsIndex++)
                {
                    var section = "GoodsInfo" + goodsIndex;
                    var itemName = ini.ReadString(section, "ItemName", string.Empty);
                    if (string.IsNullOrWhiteSpace(itemName))
                        continue;
                    if (!StdItemExists(itemName))
                    {
                        M2Share.ErrorMessage(ReadItemNameErrorMessage);
                        error = itemName;
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        internal static bool TryValidateItemNameForSave(string itemName, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(itemName))
                return true;
            if (StdItemExists(itemName))
                return true;
            M2Share.ErrorMessage(SaveItemNameErrorMessage);
            error = itemName;
            return false;
        }

        private static bool StdItemExists(string itemName)
        {
            var engine = M2Share.UserEngine;
            if (engine?.StdItemList == null)
                return false;
            return engine.GetStdItem(itemName) != null;
        }

        private static string ResolvePath()
        {
            return Path.GetFullPath(Path.Combine(
                M2Share.sRootPath,
                M2Share.g_Config?.sBaseDir ?? string.Empty,
                "Config",
                "SuperMerchant.ini"));
        }

        private sealed class SuperMerchantIni : IniFile
        {
            internal SuperMerchantIni(string path) : base(path)
            {
                Load();
            }
        }
    }
}
