using System;
using System.Collections.Generic;

namespace GameSvr.Features.Rewards
{
    /// <summary>
    /// Platinum Level Prizes System (白金等级/热血勇士奖励系统)
    ///
    /// Native Implementation References:
    /// - GM Command: @SetGoldActLv (idx496, perm5) @0x006292A1
    /// - Activation: UseGoldActCredential (GoldActCredUse) @0x007869EE, @0x00786A0A
    /// - Reward Claim: ReqItemByGoldAct @0x006457F3, @0x0064584B
    /// - Persistence: Load @0x006B0627, Save @0x006B13F1
    ///
    /// Player Fields:
    /// - m_btPlatLv: obj+0xB85, rec+0x016E (Byte, RW) - PAS script-accessible tier
    /// - m_btGoldActNextLevel: obj+0x181D (Byte) - reward progression state
    ///
    /// Logic:
    /// - Level range: 46-55 (10 reward tiers)
    /// - Credential activation sets m_btGoldActNextLevel=1 if ==0
    /// - Reward claiming: iterates from m_btGoldActNextLevel to current level
    /// - Each successful grant increments m_btGoldActNextLevel
    /// - All state persisted in character save data
    /// </summary>
    public static class PlatinumLevelPrizes
    {
        #region Configuration Constants

        /// <summary>
        /// Configuration file name for platinum level prizes.
        /// Native config location: typically in game data directory.
        /// </summary>
        public const string ConfigFileName = "PlatinumPrizes.ini";

        /// <summary>
        /// Default config directory path (relative to server root).
        /// </summary>
        public const string ConfigDirectory = "config";

        #endregion

        #region Native Constants

        /// <summary>
        /// Minimum level required to claim platinum prizes.
        /// Native gate: @0x00645801 (cmp ecx, 0x2E / jl)
        /// </summary>
        public const int MinimumClaimLevel = 46;

        /// <summary>
        /// Maximum level for platinum prize eligibility.
        /// Native clamp: @0x0064580B (cmp ecx, 0x37 / cmovg)
        /// </summary>
        public const int MaximumClaimLevel = 55;

        /// <summary>
        /// Number of prize tiers (MaximumClaimLevel - MinimumClaimLevel + 1).
        /// </summary>
        public const int PrizeTierCount = 10;

        /// <summary>
        /// Activation marker value (m_btGoldActNextLevel after credential use).
        /// Native: @0x00786A0A (mov byte ptr [esi+0x181D], 1)
        /// </summary>
        public const byte ActivatedMarker = 1;

        #endregion

        #region Native Messages

        /// <summary>
        /// Message when player is not activated as a platinum member.
        /// Native: TPlayObject.NativeGoldGift.cs - GoldActRewardNotActivatedMessage
        /// </summary>
        public const string NotActivatedMessage = "您还没有成为热血勇士，不能领取奖励物品";

        /// <summary>
        /// Message when player level is below minimum threshold.
        /// Native: TPlayObject.NativeGoldGift.cs - GoldActRewardLevelTooLowMessage
        /// </summary>
        public const string LevelTooLowMessage = "您的等级尚未达到46级，还不能领取热血勇士的奖励";

        /// <summary>
        /// Message when all eligible rewards have been claimed.
        /// Native: TPlayObject.NativeGoldGift.cs - GoldActRewardAlreadyClaimedMessage
        /// </summary>
        public const string AlreadyClaimedMessage = "您已经领取过该等级的奖励";

        /// <summary>
        /// Message when rewards are successfully granted.
        /// Native: TPlayObject.NativeGoldGift.cs - GoldActRewardCompleteMessage
        /// </summary>
        public const string RewardCompleteMessage = "恭喜您，已经领取了热血勇士的奖励";

        /// <summary>
        /// Message when credential is used but already activated.
        /// Native: @0x00786A1A message send
        /// </summary>
        public const string AlreadyActivatedMessage = "你已经是热血勇士";

        /// <summary>
        /// Message when credential successfully activates platinum status.
        /// Native: @0x00786A2E message send
        /// </summary>
        public const string ActivationSuccessMessage = "本角色成功升级为热血勇士！";

        #endregion

        #region Core State Machine

        /// <summary>
        /// Evaluate platinum reward eligibility and state.
        /// Native VA: @0x006457F3 - @0x0064585F (reward grant state machine)
        ///
        /// Gates (in order):
        /// 1. m_btGoldActNextLevel == 0 → not activated
        /// 2. player.Level &lt; 46 → level too low
        /// 3. player.Level &lt; m_btGoldActNextLevel → already claimed up to current level
        /// 4. Iteration: [nextLevel..currentLevel] → grant rewards, advance nextLevel
        /// </summary>
        /// <param name="isActivated">True if m_btGoldActNextLevel != 0</param>
        /// <param name="playerLevel">Current player level (1-based)</param>
        /// <param name="nextRewardLevel">m_btGoldActNextLevel value</param>
        /// <returns>Eligibility result with message</returns>
        public static EligibilityResult EvaluateEligibility(
            bool isActivated,
            int playerLevel,
            byte nextRewardLevel)
        {
            // Gate 1: Not activated (native: @0x006457F3 test al,al / jz)
            if (!isActivated)
            {
                return new EligibilityResult
                {
                    IsEligible = false,
                    Message = NotActivatedMessage,
                    Gate = EligibilityGate.NotActivated
                };
            }

            // Native clamps player level to [1, 55] before comparison
            var clampedLevel = Math.Clamp(playerLevel, 1, MaximumClaimLevel);

            // Gate 2: Level too low (native: @0x00645801 cmp ecx,0x2E / jl)
            if (clampedLevel < MinimumClaimLevel)
            {
                return new EligibilityResult
                {
                    IsEligible = false,
                    Message = LevelTooLowMessage,
                    Gate = EligibilityGate.LevelTooLow
                };
            }

            // Native: @0x0064580B (mov eax,[esi+0x181D] / cmp eax,0x2E / cmovl eax,0x2E)
            var effectiveNextLevel = Math.Max((int)nextRewardLevel, MinimumClaimLevel);

            // Gate 3: Already claimed (native: @0x00645817 cmp ecx,eax / jl)
            if (clampedLevel < effectiveNextLevel)
            {
                return new EligibilityResult
                {
                    IsEligible = false,
                    Message = AlreadyClaimedMessage,
                    Gate = EligibilityGate.AlreadyClaimed
                };
            }

            // Eligible: can claim levels [effectiveNextLevel..clampedLevel]
            return new EligibilityResult
            {
                IsEligible = true,
                Message = RewardCompleteMessage,
                Gate = EligibilityGate.Eligible,
                StartLevel = effectiveNextLevel,
                EndLevel = clampedLevel
            };
        }

        /// <summary>
        /// Check if credential can activate platinum status.
        /// Native VA: @0x007869EE - @0x00786A0A
        /// </summary>
        /// <param name="currentNextLevel">Current m_btGoldActNextLevel value</param>
        /// <returns>True if activation should proceed (currentNextLevel == 0)</returns>
        public static bool CanActivate(byte currentNextLevel)
        {
            // Native: @0x007869EE (cmp byte ptr [esi+0x181D], 0 / jnz refuse_branch)
            return currentNextLevel == 0;
        }

        #endregion

        #region Configuration Loading

        /// <summary>
        /// Load platinum prize configuration from INI file.
        /// Native: Configuration is typically loaded at server startup.
        /// Prize definitions map level tiers to item grants.
        /// </summary>
        /// <param name="configPath">Full path to configuration file</param>
        /// <param name="config">Loaded configuration object</param>
        /// <param name="error">Error message if loading fails</param>
        /// <returns>True if successful</returns>
        public static bool TryLoadConfiguration(
            string configPath,
            out PlatinumPrizeConfig config,
            out string error)
        {
            config = null;
            error = string.Empty;

            // TODO: Implement INI/XML parser for prize tier definitions
            // Expected format:
            // [Level46]
            // ItemName=金创药(大)
            // ItemCount=10
            // [Level47]
            // ...

            error = "Configuration loading not yet implemented.";
            return false;
        }

        #endregion

        #region Data Structures

        /// <summary>
        /// Result of eligibility evaluation.
        /// </summary>
        public class EligibilityResult
        {
            /// <summary>True if player can claim rewards.</summary>
            public bool IsEligible { get; set; }

            /// <summary>Message to display to player.</summary>
            public string Message { get; set; }

            /// <summary>Gate that determined eligibility.</summary>
            public EligibilityGate Gate { get; set; }

            /// <summary>Starting level for reward iteration (if eligible).</summary>
            public int StartLevel { get; set; }

            /// <summary>Ending level for reward iteration (if eligible).</summary>
            public int EndLevel { get; set; }
        }

        /// <summary>
        /// Eligibility gate enumeration.
        /// Corresponds to native state machine branches.
        /// </summary>
        public enum EligibilityGate
        {
            /// <summary>m_btGoldActNextLevel == 0</summary>
            NotActivated,

            /// <summary>playerLevel &lt; 46</summary>
            LevelTooLow,

            /// <summary>playerLevel &lt; effectiveNextLevel</summary>
            AlreadyClaimed,

            /// <summary>Can claim rewards</summary>
            Eligible
        }

        /// <summary>
        /// Configuration container for platinum prize system.
        /// </summary>
        public class PlatinumPrizeConfig
        {
            /// <summary>Prize definitions keyed by level (46-55).</summary>
            public Dictionary<int, PrizeTier> Tiers { get; set; }

            /// <summary>Whether the system is enabled globally.</summary>
            public bool Enabled { get; set; }

            public PlatinumPrizeConfig()
            {
                Tiers = new Dictionary<int, PrizeTier>();
                Enabled = true;
            }
        }

        /// <summary>
        /// Prize tier definition for a specific level.
        /// </summary>
        public class PrizeTier
        {
            /// <summary>Level threshold (46-55).</summary>
            public int Level { get; set; }

            /// <summary>Items to grant at this tier.</summary>
            public List<PrizeItem> Items { get; set; }

            /// <summary>Optional gold/currency reward.</summary>
            public int GoldReward { get; set; }

            public PrizeTier()
            {
                Items = new List<PrizeItem>();
            }
        }

        /// <summary>
        /// Single prize item grant descriptor.
        /// </summary>
        public class PrizeItem
        {
            /// <summary>Item name or identifier.</summary>
            public string ItemName { get; set; }

            /// <summary>Quantity to grant.</summary>
            public int Count { get; set; }

            /// <summary>Optional: item quality/durability.</summary>
            public int Quality { get; set; }
        }

        #endregion
    }
}
