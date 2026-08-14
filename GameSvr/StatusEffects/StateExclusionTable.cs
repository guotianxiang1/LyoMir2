namespace GameSvr.StatusEffects
{
    /// <summary>
    /// Native state mutual exclusion table
    ///
    /// Source: D:/loym2/staging/status_effects_spec_20260810.md §4
    ///
    /// When a state is applied, certain existing states are automatically removed.
    /// This is implemented in StateGained handlers (vmt+0x60 = 0x77327C)
    ///
    /// PROVEN EXCLUSIONS from native binary:
    /// - State 0x12 gained -> removes state 0x1A (EA: 773316)
    /// - State 0x14 lost -> removes state 0x13 (EA: 7733B4)
    /// - State 0x32 gained -> dispatches to state 7 or 8 by job (EA: 77332E)
    ///
    /// GATE-BASED EXCLUSIONS (implicit):
    /// - State 0x34 blocks ALL incoming states (EA: 772F92)
    /// - State 0x10 (level>=5) blocks states 0x2D and 0x35 (EA: 772FAA)
    /// - State 0x12 affects state 0x1A cooldown (EA: 773C74)
    /// </summary>
    public static class StateExclusionTable
    {
        /// <summary>
        /// Get states that should be removed when a given state is gained
        /// Returns null if no exclusions
        /// </summary>
        public static byte[] GetExcludedOnGained(byte stateId)
        {
            switch (stateId)
            {
                case BodyStateConstants.STATE_SUPPRESS_1A: // 0x12
                    // EA: 773316 - removes 0x1A (paralyze/stone)
                    return new byte[] { BodyStateConstants.STATE_PARALYZE_STONE };

                default:
                    return null;
            }
        }

        /// <summary>
        /// Get states that should be removed when a given state is lost
        /// Returns null if no exclusions
        /// </summary>
        public static byte[] GetExcludedOnLost(byte stateId)
        {
            switch (stateId)
            {
                case BodyStateConstants.STATE_PARENT_14: // 0x14
                    // EA: 7733B4 - removes 0x13 (derived sub-state)
                    return new byte[] { BodyStateConstants.STATE_DERIVED_OF_14 };

                default:
                    return null;
            }
        }

        /// <summary>
        /// Check if two states are mutually exclusive
        /// (for defensive validation, not actively used in native)
        /// </summary>
        public static bool AreMutuallyExclusive(byte state1, byte state2)
        {
            // 0x12 removes 0x1A
            if (state1 == BodyStateConstants.STATE_SUPPRESS_1A &&
                state2 == BodyStateConstants.STATE_PARALYZE_STONE)
                return true;

            if (state2 == BodyStateConstants.STATE_SUPPRESS_1A &&
                state1 == BodyStateConstants.STATE_PARALYZE_STONE)
                return true;

            // 0x14 and 0x13 are parent-child, not mutually exclusive
            // (0x13 only exists when 0x14's value > 3)

            return false;
        }
    }
}
