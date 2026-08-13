using SystemModule;
using SystemModule.Common;

namespace GameSvr.Configs
{
    /// <summary>
    /// Reads 战神 Share/PlayerUpgradeExp.ini [PlayerLevelExp] LEVEL_N keys.
    /// Native does not read [PlayerLevelExpRate] (zero hits in the image).
    /// </summary>
    public class ExpsConfig : IniFile
    {
        public ExpsConfig(string fileName) : base(fileName)
        {
            Load();
        }

        public void LoadConfig()
        {
            // Native loader only reads [PlayerLevelExp] LEVEL_N. The section name
            // is the Delphi long string at 0x651530 (len=14, 'PlayerLevelExp').
            // [PlayerLevelExpRate] has ZERO hits in the M2Server image (ASCII /
            // GBK / UTF-16LE); production files still ship that section, but the
            // engine never consumes it. Using those 20..54 rate numbers as
            // dwNeedExps was collapsing levels 80+ (ini value 4250000000, which
            // int.TryParse rejects) onto 38 exp per level.
            //
            // Native stores the dword as-is: 4250000000 = 0xFD51DA80, the same
            // sentinel sub_6AFCC8 / sub_6884C0 return on OOB (0x6AFCF5 /
            // 0x688520 `mov …, 0xFD51DA80`). Comparisons in the level-up loop
            // are unsigned (`0x6C0581 cmp / 0x6C0587 jbe`).
            var maxLoaded = 0;
            for (var i = 1; i <= M2Share.g_Config.dwNeedExps.GetUpperBound(0); i++)
            {
                var raw = ReadString("PlayerLevelExp", "LEVEL_" + i, "");
                if (string.IsNullOrEmpty(raw))
                    continue;
                if (!uint.TryParse(raw, out var expValue))
                    continue;
                M2Share.g_Config.dwNeedExps[i] = unchecked((int)expValue);
                maxLoaded = i;
            }
            M2Share.g_Config.nNeedExpMaxLevel = maxLoaded;
        }
    }
}
