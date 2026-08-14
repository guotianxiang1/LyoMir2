using System;

namespace GameSvr.Features.Merchant
{
    /// <summary>
    /// Tracks proximity state between players and Fortune Beast NPCs.
    /// Manages interaction eligibility based on spatial relationships.
    /// </summary>
    public sealed class FortuneBeastProximity
    {
        /// <summary>
        /// Proximity state for interaction eligibility.
        /// </summary>
        public enum ProximityState
        {
            /// <summary>Player is out of range.</summary>
            OutOfRange,

            /// <summary>Player is in proximity range for interaction.</summary>
            InRange,

            /// <summary>Player is within optimal interaction distance.</summary>
            Optimal
        }

        private readonly int _maxInteractionDistance;
        private readonly int _optimalDistance;

        /// <summary>
        /// Creates a new proximity tracker with specified distance thresholds.
        /// </summary>
        /// <param name="maxInteractionDistance">Maximum distance for any interaction (default: 10)</param>
        /// <param name="optimalDistance">Optimal distance for best interaction (default: 3)</param>
        public FortuneBeastProximity(int maxInteractionDistance = 10, int optimalDistance = 3)
        {
            if (maxInteractionDistance < 1)
                throw new ArgumentOutOfRangeException(nameof(maxInteractionDistance),
                    "Maximum interaction distance must be at least 1");

            if (optimalDistance < 1)
                throw new ArgumentOutOfRangeException(nameof(optimalDistance),
                    "Optimal distance must be at least 1");

            if (optimalDistance > maxInteractionDistance)
                throw new ArgumentException(
                    "Optimal distance cannot exceed maximum interaction distance");

            _maxInteractionDistance = maxInteractionDistance;
            _optimalDistance = optimalDistance;
        }

        /// <summary>
        /// Evaluates proximity state between player and Fortune Beast.
        /// </summary>
        /// <param name="playerX">Player X coordinate</param>
        /// <param name="playerY">Player Y coordinate</param>
        /// <param name="npcX">Fortune Beast NPC X coordinate</param>
        /// <param name="npcY">NPC Y coordinate</param>
        /// <returns>Current proximity state</returns>
        public ProximityState EvaluateProximity(int playerX, int playerY, int npcX, int npcY)
        {
            var distance = CalculateDistance(playerX, playerY, npcX, npcY);

            if (distance <= _optimalDistance)
                return ProximityState.Optimal;

            if (distance <= _maxInteractionDistance)
                return ProximityState.InRange;

            return ProximityState.OutOfRange;
        }

        /// <summary>
        /// Checks if player is within interaction range of the Fortune Beast.
        /// </summary>
        public bool IsInInteractionRange(int playerX, int playerY, int npcX, int npcY)
        {
            var state = EvaluateProximity(playerX, playerY, npcX, npcY);
            return state == ProximityState.InRange || state == ProximityState.Optimal;
        }

        /// <summary>
        /// Checks if player is at optimal interaction distance.
        /// </summary>
        public bool IsAtOptimalDistance(int playerX, int playerY, int npcX, int npcY)
        {
            var state = EvaluateProximity(playerX, playerY, npcX, npcY);
            return state == ProximityState.Optimal;
        }

        /// <summary>
        /// Gets the actual distance between player and NPC.
        /// </summary>
        public int GetDistance(int playerX, int playerY, int npcX, int npcY)
        {
            return CalculateDistance(playerX, playerY, npcX, npcY);
        }

        /// <summary>
        /// Gets the maximum interaction distance threshold.
        /// </summary>
        public int MaxInteractionDistance => _maxInteractionDistance;

        /// <summary>
        /// Gets the optimal interaction distance threshold.
        /// </summary>
        public int OptimalDistance => _optimalDistance;

        /// <summary>
        /// Calculates Chebyshev distance (chess king distance) between two points.
        /// Uses max(|dx|, |dy|) which matches typical MIR2 proximity logic.
        /// </summary>
        private static int CalculateDistance(int x1, int y1, int x2, int y2)
        {
            var dx = Math.Abs(x1 - x2);
            var dy = Math.Abs(y1 - y2);
            return Math.Max(dx, dy);
        }
    }
}
