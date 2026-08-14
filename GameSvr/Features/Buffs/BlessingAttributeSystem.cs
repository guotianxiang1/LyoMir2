using System;

namespace GameSvr.Features.Buffs
{
    /// <summary>
    /// Blessing Attribute System (祝福属性/幸运值系统)
    ///
    /// Native Implementation References:
    /// - Core Handler: sub_7698BC (AddBodyLuck) @0x7698BC
    /// - GM Command: @ChgBodyLuck (idx359, perm5) @0x628008
    /// - PAS API: AddPlayerBodyLuck @PasApiBridge
    /// - Death Penalty: +500 raw units @0x6C07A0 (sub_6C07A0)
    /// - PK Kill: -500 raw units @0x6C0FE4 (sub_6C0FE4)
    /// - State Refresh: 0 units (re-clamp) @0x7650D8 (sub_7650D8)
    ///
    /// Player Fields:
    /// - m_nBodyLuckLevel: obj+0x164, rec+0xCC (Integer, RW) - authoritative level [-10, +5]
    /// - Native stores raw units (multiples of 500), then applies Round(raw/500) with clamp
    ///
    /// Logic:
    /// - Native: [obj+0x164] += Round(rawUnits / 500.0), then clamp to [-10, +5]
    /// - All native callers pass multiples of 500 (GM=luck*500, PAS=luck*500, death=±500)
    /// - C# port: direct integer addition to m_nBodyLuckLevel, clamp [-10, +5]
    /// - Range: minimum -10 (corrected from old -5), maximum +5
    /// - Affects: critical hit chance, magic damage, merchant buy/sell prices
    ///
    /// Evidence:
    /// - staging/update_clothes_4637_ida_work/luck_hide_out.txt (sub_7698BC disassembly)
    /// - staging/gm_player_attr_commands_20260801.md (GM command analysis)
    /// - TBaseObject.cs:1479 (current implementation)
    /// </summary>
    public static class BlessingAttributeSystem
    {
        #region Configuration Constants

        /// <summary>
        /// Configuration file name for blessing attribute system.
        /// Native: luck values may be defined in server configuration or scripts.
        /// </summary>
        public const string ConfigFileName = "BlessingAttribute.ini";

        /// <summary>
        /// Default config directory path (relative to server root).
        /// </summary>
        public const string ConfigDirectory = "config";

        #endregion

        #region Native Constants

        /// <summary>
        /// Minimum blessing/luck level.
        /// Native clamp: @0x7698BC (cmp with -10, cmovl to clamp)
        /// Evidence: TBaseObject.cs:1486-1488 corrected from old -5 to native -10
        /// </summary>
        public const int MinimumLuckLevel = -10;

        /// <summary>
        /// Maximum blessing/luck level.
        /// Native clamp: @0x7698BC (cmp with 5, cmovg to clamp)
        /// </summary>
        public const int MaximumLuckLevel = 5;

        /// <summary>
        /// Native scaling factor for raw units to level conversion.
        /// Native: Round(rawUnits / 500.0) → level adjustment
        /// All native callers use multiples of 500.
        /// </summary>
        public const int RawUnitsPerLevel = 500;

        /// <summary>
        /// Death penalty: +1 luck level (+500 raw units).
        /// Native: @0x6C07A0 (sub_6C07A0 death handler)
        /// </summary>
        public const int DeathPenaltyLevels = 1;

        /// <summary>
        /// PK kill penalty: -1 luck level (-500 raw units).
        /// Native: @0x6C0FE4 (sub_6C0FE4 PK kill handler)
        /// </summary>
        public const int PkKillPenaltyLevels = -1;

        #endregion

        #region Native Messages

        /// <summary>
        /// Message when GM sets body luck successfully.
        /// Native: @0x628008 (GM @ChgBodyLuck handler)
        /// </summary>
        public const string GmSetLuckSuccessMessage = "已经成功修改幸运值为：";

        /// <summary>
        /// Message when PAS script adjusts body luck.
        /// Native: PasApiBridge AddPlayerBodyLuck
        /// </summary>
        public const string ScriptAdjustLuckMessage = "您的幸运值已调整";

        #endregion

        #region Core Logic

        /// <summary>
        /// Apply blessing/luck adjustment with native clamping.
        /// Native VA: sub_7698BC @0x7698BC
        ///
        /// Algorithm:
        /// 1. Add level adjustment to current level
        /// 2. Clamp to [MinimumLuckLevel, MaximumLuckLevel]
        ///
        /// Native signature: sub_7698BC(this: TBaseObject, rawUnits: Integer)
        /// - rawUnits are multiples of 500 in all native call sites
        /// - Result = Round(rawUnits / 500.0) added to obj+0x164
        /// - C# port receives level directly (rawUnits / 500 already computed)
        /// </summary>
        /// <param name="currentLevel">Current m_nBodyLuckLevel value</param>
        /// <param name="adjustment">Level adjustment (±n levels)</param>
        /// <returns>New clamped luck level</returns>
        public static int ApplyLuckAdjustment(int currentLevel, int adjustment)
        {
            // Native: obj+0x164 += Round(rawUnits / 500.0)
            // C# receives level directly, so just add
            int newLevel = currentLevel + adjustment;

            // Native clamp: [-10, +5]
            // @0x7698BC: cmp result with bounds, cmov to clamp
            return Math.Clamp(newLevel, MinimumLuckLevel, MaximumLuckLevel);
        }

        /// <summary>
        /// Convert raw units (native scale) to level adjustment.
        /// Native: Round(rawUnits / 500.0)
        ///
        /// All native call sites:
        /// - GM @ChgBodyLuck: rawUnits = inputLevel * 500 @0x628008
        /// - PAS AddPlayerBodyLuck: rawUnits = inputLevel * 500
        /// - Death: rawUnits = 500 @0x6C07A0
        /// - PK kill: rawUnits = -500 @0x6C0FE4
        /// - State refresh: rawUnits = 0 @0x7650D8
        /// </summary>
        /// <param name="rawUnits">Raw units (multiples of 500 in native)</param>
        /// <returns>Level adjustment after rounding</returns>
        public static int RawUnitsToLevelAdjustment(int rawUnits)
        {
            // Native uses Round (banker's rounding / half-to-even)
            // For multiples of 500, rounding is exact
            return (int)Math.Round(rawUnits / (double)RawUnitsPerLevel, MidpointRounding.ToEven);
        }

        /// <summary>
        /// Validate luck level is within native bounds.
        /// </summary>
        /// <param name="level">Level to validate</param>
        /// <returns>True if within [-10, +5]</returns>
        public static bool IsValidLuckLevel(int level)
        {
            return level >= MinimumLuckLevel && level <= MaximumLuckLevel;
        }

        /// <summary>
        /// Get luck display string for UI/messages.
        /// </summary>
        /// <param name="level">Current luck level</param>
        /// <returns>Formatted string with sign prefix</returns>
        public static string GetLuckDisplayString(int level)
        {
            if (level > 0)
                return $"+{level}";
            else if (level < 0)
                return level.ToString();
            else
                return "0";
        }

        #endregion

        #region Native Integration Points

        /// <summary>
        /// Handle death penalty (native: sub_6C07A0 @0x6C07A0).
        /// Native applies +500 raw units → +1 level.
        /// </summary>
        /// <param name="currentLevel">Current luck level</param>
        /// <returns>New luck level after death penalty</returns>
        public static int ApplyDeathPenalty(int currentLevel)
        {
            // Native: @0x6C07A0 calls AddBodyLuck(500)
            // Net effect: +1 level (less negative/more positive)
            return ApplyLuckAdjustment(currentLevel, DeathPenaltyLevels);
        }

        /// <summary>
        /// Handle PK kill penalty (native: sub_6C0FE4 @0x6C0FE4).
        /// Native applies -500 raw units → -1 level.
        /// </summary>
        /// <param name="currentLevel">Current luck level</param>
        /// <returns>New luck level after PK penalty</returns>
        public static int ApplyPkKillPenalty(int currentLevel)
        {
            // Native: @0x6C0FE4 calls AddBodyLuck(-500)
            // Net effect: -1 level (more negative/less positive)
            return ApplyLuckAdjustment(currentLevel, PkKillPenaltyLevels);
        }

        /// <summary>
        /// Re-clamp luck level (native: sub_7650D8 @0x7650D8).
        /// Native calls AddBodyLuck(0) to force re-clamp.
        /// </summary>
        /// <param name="currentLevel">Current luck level (possibly out of bounds)</param>
        /// <returns>Clamped luck level</returns>
        public static int ReClampLuckLevel(int currentLevel)
        {
            // Native: @0x7650D8 calls AddBodyLuck(0)
            // Forces re-evaluation of clamp bounds
            return ApplyLuckAdjustment(currentLevel, 0);
        }

        #endregion

        #region Configuration Loading

        /// <summary>
        /// Load blessing attribute configuration.
        /// Native: configuration may be embedded in server config or script files.
        /// </summary>
        /// <param name="configPath">Full path to configuration file</param>
        /// <param name="config">Loaded configuration object</param>
        /// <param name="error">Error message if loading fails</param>
        /// <returns>True if successful</returns>
        public static bool TryLoadConfiguration(
            string configPath,
            out BlessingAttributeConfig config,
            out string error)
        {
            config = null;
            error = string.Empty;

            // TODO: Implement configuration parser
            // Expected configuration items:
            // - EnableSystem: bool (default true)
            // - DeathPenaltyEnabled: bool (default true)
            // - PkPenaltyEnabled: bool (default true)
            // - CustomAdjustments: dictionary of event → level change

            error = "Configuration loading not yet implemented.";
            return false;
        }

        #endregion

        #region Data Structures

        /// <summary>
        /// Configuration container for blessing attribute system.
        /// </summary>
        public class BlessingAttributeConfig
        {
            /// <summary>Whether the system is globally enabled.</summary>
            public bool Enabled { get; set; }

            /// <summary>Whether death penalty is active.</summary>
            public bool DeathPenaltyEnabled { get; set; }

            /// <summary>Whether PK kill penalty is active.</summary>
            public bool PkPenaltyEnabled { get; set; }

            /// <summary>Custom level adjustment overrides (event name → adjustment).</summary>
            public System.Collections.Generic.Dictionary<string, int> CustomAdjustments { get; set; }

            public BlessingAttributeConfig()
            {
                Enabled = true;
                DeathPenaltyEnabled = true;
                PkPenaltyEnabled = true;
                CustomAdjustments = new System.Collections.Generic.Dictionary<string, int>();
            }
        }

        /// <summary>
        /// Result of luck adjustment operation.
        /// </summary>
        public class LuckAdjustmentResult
        {
            /// <summary>Previous luck level.</summary>
            public int OldLevel { get; set; }

            /// <summary>New luck level after adjustment and clamp.</summary>
            public int NewLevel { get; set; }

            /// <summary>Requested adjustment amount.</summary>
            public int RequestedAdjustment { get; set; }

            /// <summary>Actual adjustment applied (may differ due to clamping).</summary>
            public int ActualAdjustment => NewLevel - OldLevel;

            /// <summary>Whether adjustment was clamped.</summary>
            public bool WasClamped => Math.Abs(ActualAdjustment) < Math.Abs(RequestedAdjustment);

            /// <summary>Which boundary triggered clamp (if any).</summary>
            public ClampBoundary? ClampedAt { get; set; }
        }

        /// <summary>
        /// Clamp boundary enumeration.
        /// </summary>
        public enum ClampBoundary
        {
            /// <summary>Clamped at minimum (-10).</summary>
            Minimum,

            /// <summary>Clamped at maximum (+5).</summary>
            Maximum
        }

        #endregion
    }
}
