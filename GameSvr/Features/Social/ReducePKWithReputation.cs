using System;
using SystemModule;

namespace GameSvr.Features.Social
{
    /// <summary>
    /// Provides functionality for reducing PK points by consuming reputation/credit points.
    /// This feature allows players to "wash" their red name status by spending reputation.
    /// </summary>
    public sealed class ReducePKWithReputation
    {
        private readonly object _syncRoot = new();

        /// <summary>
        /// Exchange rate: how many reputation points are required to reduce 1 PK point.
        /// Default: 10 reputation points = 1 PK point reduction.
        /// </summary>
        public int ReputationPerPKPoint { get; set; } = 10;

        /// <summary>
        /// Minimum PK points required to use this feature.
        /// Players with PK below this threshold cannot use reputation washing.
        /// </summary>
        public int MinimumPKPointsRequired { get; set; } = 1;

        /// <summary>
        /// Maximum PK points that can be reduced in a single operation.
        /// Default: 100 to prevent abuse.
        /// </summary>
        public int MaxPKPointsPerOperation { get; set; } = 100;

        /// <summary>
        /// Whether this feature is currently enabled.
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Attempts to reduce PK points for a player by consuming reputation points.
        /// </summary>
        /// <param name="player">The player object.</param>
        /// <param name="pkPointsToReduce">Number of PK points to reduce (must be positive).</param>
        /// <param name="actualReduction">Actual PK points reduced (output).</param>
        /// <param name="reputationCost">Actual reputation consumed (output).</param>
        /// <returns>Result indicating success or failure reason.</returns>
        public ReducePKResult TryReducePK(TPlayObject player, int pkPointsToReduce,
            out int actualReduction, out int reputationCost)
        {
            actualReduction = 0;
            reputationCost = 0;

            if (player == null)
                return ReducePKResult.InvalidPlayer;

            lock (_syncRoot)
            {
                // Check if feature is enabled
                if (!IsEnabled)
                    return ReducePKResult.FeatureDisabled;

                // Validate input
                if (pkPointsToReduce <= 0)
                    return ReducePKResult.InvalidAmount;

                // Check player's current PK points
                if (player.m_nPkPoint < MinimumPKPointsRequired)
                    return ReducePKResult.PKPointsTooLow;

                // Limit reduction amount
                int requestedReduction = Math.Min(pkPointsToReduce, MaxPKPointsPerOperation);
                requestedReduction = Math.Min(requestedReduction, player.m_nPkPoint);

                // Calculate reputation cost
                int requiredReputation = requestedReduction * ReputationPerPKPoint;

                // Check if player has enough reputation
                if (player.m_btCreditPoint < requiredReputation)
                {
                    // Calculate how many PK points can be reduced with available reputation
                    int affordableReduction = player.m_btCreditPoint / ReputationPerPKPoint;
                    if (affordableReduction == 0)
                        return ReducePKResult.InsufficientReputation;

                    requestedReduction = affordableReduction;
                    requiredReputation = affordableReduction * ReputationPerPKPoint;
                }

                // Perform the transaction
                player.m_btCreditPoint -= (byte)requiredReputation;
                player.m_nPkPoint -= requestedReduction;

                // Ensure PK points don't go negative
                if (player.m_nPkPoint < 0)
                    player.m_nPkPoint = 0;

                actualReduction = requestedReduction;
                reputationCost = requiredReputation;

                // Refresh player's name color based on new PK value
                RefreshPlayerNameColor(player);

                return ReducePKResult.Success;
            }
        }

        /// <summary>
        /// Calculates the reputation cost for reducing a specified amount of PK points.
        /// </summary>
        public int CalculateReputationCost(int pkPointsToReduce)
        {
            if (pkPointsToReduce <= 0)
                return 0;

            int capped = Math.Min(pkPointsToReduce, MaxPKPointsPerOperation);
            return capped * ReputationPerPKPoint;
        }

        /// <summary>
        /// Calculates how many PK points can be reduced with available reputation.
        /// </summary>
        public int CalculateAffordablePKReduction(int availableReputation)
        {
            if (availableReputation <= 0 || ReputationPerPKPoint <= 0)
                return 0;

            return Math.Min(availableReputation / ReputationPerPKPoint, MaxPKPointsPerOperation);
        }

        /// <summary>
        /// Checks if a player is eligible to use this feature.
        /// </summary>
        public bool CanPlayerUseFeature(TPlayObject player, out string reason)
        {
            reason = string.Empty;

            if (player == null)
            {
                reason = "无效的玩家对象";
                return false;
            }

            if (!IsEnabled)
            {
                reason = "该功能当前未开放";
                return false;
            }

            if (player.m_nPkPoint < MinimumPKPointsRequired)
            {
                reason = $"PK值必须大于等于 {MinimumPKPointsRequired}";
                return false;
            }

            if (player.m_btCreditPoint < ReputationPerPKPoint)
            {
                reason = $"声望点不足，至少需要 {ReputationPerPKPoint} 点声望";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Refreshes the player's name color based on current PK points.
        /// This method should trigger the native RefNameColor logic.
        /// </summary>
        private void RefreshPlayerNameColor(TPlayObject player)
        {
            // The actual RefNameColor implementation needs to be called here
            // Based on the grep results, this updates the player's visual name color
            // in relation to their PK status (white/red name)
            // Native implementation location: needs to be wired to existing game logic

            // TODO: Wire to native RefNameColor() when available
            // For MVI, we acknowledge this is a placeholder
        }
    }

    /// <summary>
    /// Result codes for PK reduction operations.
    /// </summary>
    public enum ReducePKResult
    {
        /// <summary>Operation completed successfully.</summary>
        Success = 0,

        /// <summary>Feature is currently disabled.</summary>
        FeatureDisabled = 1,

        /// <summary>Invalid player object provided.</summary>
        InvalidPlayer = 2,

        /// <summary>Invalid reduction amount (must be positive).</summary>
        InvalidAmount = 3,

        /// <summary>Player's PK points are below the minimum threshold.</summary>
        PKPointsTooLow = 4,

        /// <summary>Player doesn't have enough reputation points.</summary>
        InsufficientReputation = 5
    }
}
