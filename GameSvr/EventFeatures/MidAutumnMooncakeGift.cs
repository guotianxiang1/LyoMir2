using System;
using SystemModule;

namespace GameSvr.EventFeatures
{
    /// <summary>
    /// Mid-Autumn Festival mooncake gift system (TCoupleFeastBox).
    /// Native VA: sub_6DE234 @0x006DE234
    /// StdMode 31, Shape 7..9 correspond to use-mode 1..3.
    /// </summary>
    public static class MidAutumnMooncakeGift
    {
        #region Native Address Constants

        /// <summary>
        /// TCoupleFeastBox.Use entry point
        /// </summary>
        private const int NativeMooncakeGiftAddress = 0x006DE234;

        /// <summary>
        /// Shape-to-mode calculation: add edx,6
        /// </summary>
        private const int NativeShapeToModeAddress = 0x006DE325;

        /// <summary>
        /// Reward table lookup entry
        /// </summary>
        private const int NativeRewardTableAddress = 0x007D3C1C;

        /// <summary>
        /// Reward delivery function (sub_74DE54)
        /// </summary>
        private const int NativeRewardDeliveryAddress = 0x006DE341;

        #endregion

        #region Configuration Constants

        // Config file path
        private const string ConfigFilePath = "config/MidAutumnEvent.ini";

        // StdMode and Shape constraints
        private const int ExpectedStdMode = 31;
        private const int MinShape = 7;
        private const int MaxShape = 9;
        private const int ShapeOffset = 6; // Shape - 6 = mode

        #endregion

        #region Message Constants

        private const string NoSpouseMessage =
            "您还没有配偶，此月饼盒只能送给自己的爱人！";

        private const string SpouseNotFoundMessage =
            "找不到{0}";

        private const string SpouseNotNearbyMessage =
            " 不在附近，你的中秋礼品无法赠送！";

        private const string SpouseBusyMessage =
            "您的爱人目前不能接受中秋礼品";

        private const string BagEmptyMessage =
            "您的包裹中没有中秋礼品";

        private const string BroadcastPrefix = "";

        private const string BroadcastSuffix =
            " 月饼盒，期望与爱人共度中秋良宵！！！ 祝大家中秋快乐！";

        #endregion

        #region Core Methods

        /// <summary>
        /// Validate if item can be used as mooncake gift.
        /// Checks StdMode 31 and Shape range 7..9.
        /// </summary>
        /// <param name="stdItem">Item template</param>
        /// <returns>True if valid mooncake gift item</returns>
        public static bool IsValidMooncakeGift(GoodItem stdItem)
        {
            if (stdItem == null)
                return false;

            if (stdItem.StdMode != ExpectedStdMode)
                return false;

            return stdItem.Shape >= MinShape && stdItem.Shape <= MaxShape;
        }

        /// <summary>
        /// Calculate use-mode from item shape.
        /// Native formula: mode = Shape - 6 (@0x006DE325 add edx,6)
        /// Valid modes: 1..3
        /// </summary>
        /// <param name="shape">Item shape value</param>
        /// <returns>Use-mode (1-3) or 0 if invalid</returns>
        public static int GetModeFromShape(int shape)
        {
            var mode = shape >= MinShape ? shape - ShapeOffset : 1;
            return mode >= 1 && mode <= 3 ? mode : 0;
        }

        /// <summary>
        /// Try to use mooncake gift item.
        /// Native implementation: sub_6DE234 @0x006DE234
        /// </summary>
        /// <param name="player">Player using the item</param>
        /// <param name="stdItem">Item template</param>
        /// <param name="item">Item instance</param>
        /// <returns>True if successfully used</returns>
        public static bool TryUseMooncakeGift(TPlayObject player, GoodItem stdItem, TUserItem item)
        {
            if (player == null || stdItem == null || item == null)
                return false;

            // Validate marriage status
            if (!ValidateMarriageStatus(player))
                return false;

            // Find and validate spouse
            var spouse = FindValidSpouse(player);
            if (spouse == null)
                return false;

            // Calculate mode
            var mode = GetModeFromShape(stdItem.Shape);
            if (mode == 0)
                return false;

            // TODO: Deliver rewards to spouse based on mode
            // Reward delivery uses native table @0x007D3C1C indexed by mode
            // (sub_74DE54 @0x006DE341); table contents require further reverse engineering

            // Send broadcast messages
            SendMooncakeBroadcast(player, spouse);

            return true;
        }

        #endregion

        #region Validation Methods

        /// <summary>
        /// Validate player has spouse.
        /// Native check: m_boMarried && !IsNullOrEmpty(m_sDearName)
        /// </summary>
        private static bool ValidateMarriageStatus(TPlayObject player)
        {
            if (!player.m_boMarried || string.IsNullOrEmpty(player.m_sDearName))
            {
                player.SysMsg(NoSpouseMessage, MsgColor.Red, MsgType.Hint);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Find spouse and validate they can receive gift.
        /// Native checks: exists, not ghost, alive, same map, bag space
        /// </summary>
        private static TPlayObject FindValidSpouse(TPlayObject player)
        {
            var spouse = M2Share.UserEngine?.GetPlayObject(player.m_sDearName);

            // Check exists and not ghost
            if (spouse == null || spouse.m_boGhost)
            {
                player.SysMsg(string.Format(SpouseNotFoundMessage, player.m_sDearName),
                    MsgColor.Red, MsgType.Hint);
                return null;
            }

            // Check alive and same map
            if (spouse.m_boDeath || spouse.m_PEnvir != player.m_PEnvir)
            {
                player.SysMsg(SpouseNotNearbyMessage.TrimStart(),
                    MsgColor.Red, MsgType.Hint);
                return null;
            }

            // Check can receive items (bag space)
            if (!CanTakeBagItem(spouse))
            {
                player.SysMsg(SpouseBusyMessage, MsgColor.Red, MsgType.Hint);
                return null;
            }

            return spouse;
        }

        /// <summary>
        /// Check if player has bag space (< 48 items).
        /// Native check: (m_ItemList?.Count ?? int.MaxValue) < 48
        /// </summary>
        private static bool CanTakeBagItem(TPlayObject player)
        {
            return (player.m_ItemList?.Count ?? int.MaxValue) < 48;
        }

        #endregion

        #region Broadcast Methods

        /// <summary>
        /// Send mooncake gift broadcast to world and spouse.
        /// Native VA: broadcast logic in sub_6DE234
        /// </summary>
        private static void SendMooncakeBroadcast(TPlayObject sender, TPlayObject spouse)
        {
            var broadcastMsg = BroadcastPrefix + sender.m_sCharName + BroadcastSuffix;

            // World broadcast
            M2Share.UserEngine?.SendBroadCastMsg(broadcastMsg, MsgType.Notice);

            // Direct message to spouse
            spouse.SysMsg(broadcastMsg, MsgColor.Red, MsgType.Hint);
        }

        #endregion

        #region Configuration (Placeholder)

        /// <summary>
        /// Load mooncake gift configuration from ini file.
        /// TODO: Implement when reward table structure is reverse engineered.
        /// </summary>
        public static void LoadConfiguration()
        {
            // Placeholder for future configuration loading
            // Will load reward table mappings from config/MidAutumnEvent.ini
            // Reward table @0x007D3C1C structure requires further reverse engineering
        }

        #endregion

        #region Reward Delivery (Dormant)

        /// <summary>
        /// Deliver rewards to spouse based on mode.
        /// Native implementation: sub_74DE54 @0x006DE341
        /// Reward table: @0x007D3C1C indexed by mode (1-3)
        /// BLOCKED: Table contents not statically recovered from binary.
        /// </summary>
        /// <param name="spouse">Recipient player</param>
        /// <param name="mode">Gift mode (1-3)</param>
        private static void DeliverRewards(TPlayObject spouse, int mode)
        {
            // TODO: Implement reward delivery
            // Requires reverse engineering of reward table @0x007D3C1C
            // Table structure: mode -> item list mapping
            // Delivery function: sub_74DE54 @0x006DE341

            throw new NotImplementedException(
                "Reward delivery blocked: native table @0x007D3C1C contents not recovered. " +
                "Requires IDA analysis of reward table structure and item mappings.");
        }

        #endregion
    }
}
