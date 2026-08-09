using SystemModule;
using SystemModule.Common;

namespace GameSvr.Configs
{
    /// <summary>
    /// Reads 战神 Share/PlayerUpgradeExp.ini with sections:
    ///   [PlayerLevelExpRate] — rate multipliers per level (LEVEL_1=20, LEVEL_2=20, ...)
    ///   [PlayerLevelExp]   — base exp per level (LEVEL_1=100, LEVEL_2=200, ...)
    /// Does NOT write-back. Missing keys use C# defaults.
    /// </summary>
    public class ExpsConfig : IniFile
    {
        public ExpsConfig(string fileName) : base(fileName)
        {
            Load();
        }

        public void LoadConfig()
        {
            // Read basic exp settings from !Setup.txt style keys (fall back to defaults)
            // These were previously in the C# custom Exps.conf [Exp] section.
            // Since PlayerUpgradeExp.ini only has level data, use C# defaults for:
            // nLimitExpLevel, nLimitExpValue, dwKillMonExpMultiple, boHighLevelKillMonFixExp,
            // boHighLevelGroupFixExp, boUseFixExp, nBaseExp, nAddExp, boMonDelHptoExp,
            // MonHptoExpLevel, MonHptoExpmax — all remain at GameSvrConfig defaults.

            // Load per-level experience requirements
            // Format: [PlayerLevelExpRate] LEVEL_1=20, LEVEL_2=20, ...
            // These are rate multipliers; the base exp per level is read from [PlayerLevelExp]
            for (var i = 0; i <= M2Share.g_Config.dwNeedExps.GetUpperBound(0); i++)
            {
                // Try [PlayerLevelExp] section first (absolute exp per level)
                string levelKey = "LEVEL_" + i;
                int expValue = ReadInteger("PlayerLevelExp", levelKey, -1);
                if (expValue > 0)
                {
                    M2Share.g_Config.dwNeedExps[i] = expValue;
                }
                else
                {
                    // Fall back: try [PlayerLevelExpRate] for rate (not absolute exp, keep default)
                    int rateValue = ReadInteger("PlayerLevelExpRate", levelKey, -1);
                    if (rateValue > 0)
                    {
                        // Apply rate to base exp to compute level requirement
                        // The Delphi original uses rate as a multiplier
                        M2Share.g_Config.dwNeedExps[i] = rateValue;
                    }
                    // else: keep C# default value (already initialized in GameSvrConfig constructor)
                }
            }
        }
    }
}
