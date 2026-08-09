using System;
using SystemModule.Common;

namespace GameSvr.Configs
{
    /// <summary>
    /// Reads 战神 Share/ServerData.ini for global integer and string arrays.
    /// This file is typically empty (0 bytes) at runtime — handled gracefully.
    /// GlobalVal and GlobalAVal arrays keep C# defaults when file is empty.
    /// </summary>
    public class GlobalConfig : IniFile
    {
        private readonly object _saveLock = new();

        public GlobalConfig(string fileName) : base(fileName)
        {
            try
            {
                Load();
            }
            catch (Exception)
            {
                // ServerData.ini is empty (0 bytes) — use C# defaults for all globals.
                // IniFile.Load() throws for empty/missing config files.
            }
        }

        public void LoadConfig()
        {
            for (var i = M2Share.g_Config.GlobalVal.GetLowerBound(0); i <= M2Share.g_Config.GlobalVal.GetUpperBound(0); i++)
            {
                var value = ReadString("Integer", "GlobalVal" + i, null);
                if (int.TryParse(value, out var parsed)) M2Share.g_Config.GlobalVal[i] = parsed;
            }

            for (var i = M2Share.g_Config.GlobalAVal.GetLowerBound(0); i <= M2Share.g_Config.GlobalAVal.GetUpperBound(0); i++)
            {
                M2Share.g_Config.GlobalAVal[i] = ReadString("String", "GlobalStrVal" + i, string.Empty);
            }
        }

        public void SaveConfig()
        {
            lock (_saveLock)
            {
                for (var i = M2Share.g_Config.GlobalVal.GetLowerBound(0); i <= M2Share.g_Config.GlobalVal.GetUpperBound(0); i++)
                    SetCachedString("Integer", "GlobalVal" + i, M2Share.g_Config.GlobalVal[i].ToString());

                for (var i = M2Share.g_Config.GlobalAVal.GetLowerBound(0); i <= M2Share.g_Config.GlobalAVal.GetUpperBound(0); i++)
                    SetCachedString("String", "GlobalStrVal" + i, M2Share.g_Config.GlobalAVal[i] ?? string.Empty);

                Save();
            }
        }
    }
}
