using SystemModule.Common;

namespace GameSvr.Configs
{
    /// <summary>
    /// Reads [Names] section from 战神 original Delphi !Setup.txt.
    /// Values may be single-quoted (Delphi INI convention). Quotes are stripped.
    /// Other string display messages use C# defaults (not in !Setup.txt).
    /// </summary>
    public class StringConfig : IniFile
    {
        public StringConfig(string fileName) : base(fileName)
        {
            Load();
        }

        /// <summary>
        /// Reads a string and strips surrounding single quotes (Delphi INI convention).
        /// </summary>
        private string ReadName(string section, string key, string defval)
        {
            string val = ReadString(section, key, defval);
            if (val != null && val.Length >= 2 && val.StartsWith("'") && val.EndsWith("'"))
            {
                val = val.Substring(1, val.Length - 2);
            }
            return val;
        }

        public void LoadString()
        {
            // [Names] section from !Setup.txt — item & monster display names
            M2Share.g_Config.sClothsMan = ReadName("Names", "ClothsMan", M2Share.g_Config.sClothsMan);
            M2Share.g_Config.sClothsWoman = ReadName("Names", "ClothsWoman", M2Share.g_Config.sClothsWoman);
            M2Share.g_Config.sWoodenSword = ReadName("Names", "WoodenSword", M2Share.g_Config.sWoodenSword);
            // BasicItem from [Names] maps to BasicDrug (starter item name)
            string basicItem = ReadName("Names", "BasicItem", "");
            if (!string.IsNullOrEmpty(basicItem))
            {
                M2Share.g_Config.sBasicDrug = basicItem;
            }
            M2Share.g_Config.sGoldStone = ReadName("Names", "GoldStone", M2Share.g_Config.sGoldStone);
            M2Share.g_Config.sSilverStone = ReadName("Names", "SilverStone", M2Share.g_Config.sSilverStone);
            M2Share.g_Config.sSteelStone = ReadName("Names", "SteelStone", M2Share.g_Config.sSteelStone);
            M2Share.g_Config.sCopperStone = ReadName("Names", "CopperStone", M2Share.g_Config.sCopperStone);
            M2Share.g_Config.sBlackStone = ReadName("Names", "BlackStone", M2Share.g_Config.sBlackStone);
            // MINE-01: Gem1Stone..Gem4Stone 已移除，它们只喂发明的 MINE2 宝石产线。
            // Zuma monster names
            M2Share.g_Config.sZuma[0] = ReadName("Names", "Zuma1", M2Share.g_Config.sZuma[0]);
            M2Share.g_Config.sZuma[1] = ReadName("Names", "Zuma2", M2Share.g_Config.sZuma[1]);
            M2Share.g_Config.sZuma[2] = ReadName("Names", "Zuma3", M2Share.g_Config.sZuma[2]);
            M2Share.g_Config.sZuma[3] = ReadName("Names", "Zuma4", M2Share.g_Config.sZuma[3]);
            // Other monster names
            M2Share.g_Config.sBee = ReadName("Names", "Bee", M2Share.g_Config.sBee);
            M2Share.g_Config.sSpider = ReadName("Names", "Spider", M2Share.g_Config.sSpider);
            M2Share.g_Config.sWomaHorn = ReadName("Names", "WomaHorn", M2Share.g_Config.sWomaHorn);
            M2Share.g_Config.sZumaPiece = ReadName("Names", "ZumaPiece", M2Share.g_Config.sZumaPiece);
            // Skeleton, Dragon, Angel
            M2Share.g_Config.sSkeleton = ReadName("Names", "Skeleton", M2Share.g_Config.sSkeleton);
            M2Share.g_Config.sDragon = ReadName("Names", "Dragon", M2Share.g_Config.sDragon);
            M2Share.g_Config.sDragon1 = ReadName("Names", "Dragon1", M2Share.g_Config.sDragon1);
            M2Share.g_Config.sAngel = ReadName("Names", "Angel", M2Share.g_Config.sAngel);

            // ============ Remaining string display messages: use C# defaults ============
            // The old String.conf had [Server], [Guild], [String] sections for display messages.
            // !Setup.txt does not have these — they stay at C# defaults.
        }
    }
}
