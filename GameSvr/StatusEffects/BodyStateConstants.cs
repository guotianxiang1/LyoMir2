namespace GameSvr.StatusEffects
{
    /// <summary>
    /// Native body-state constants from 战神 M2Server
    ///
    /// Source: D:/loym2/staging/status_effects_spec_20260810.md §2
    /// These are the 53 states named by native GBK strings in notify handlers
    ///
    /// IMPORTANT: OLD C# CONSTANTS (POISON_DECHEALTH=0, POISON_STONE=5) ARE WRONG
    /// - State 0x00 is burrow/dig-up mode (monster), not poison
    /// - State 0x1E/0x1F/0x35 are the poison states (not 0)
    /// - RM_POISON (8037) does not exist in this binary (zero sites)
    /// </summary>
    public static class BodyStateConstants
    {
        // Valid state id range: 0x00..0x6F (112 states)
        public const byte STATE_MIN = 0x00;
        public const byte STATE_MAX = 0x6F;

        // §2.A Low band - proven by native strings and behavior

        /// <summary>
        /// Burrow / hidden (dig-up) - monster only
        /// Clearing broadcasts RM_DIGUP (10200) and clears obj+0x2E5
        /// </summary>
        public const byte STATE_BURROW = 0x00;

        /// <summary>
        /// Graded state; level >= 5 confers partial immunity
        /// Used by gate (§6)
        /// </summary>
        public const byte STATE_GRADED_IMMUNITY = 0x10;

        /// <summary>
        /// Suppresses 0x1A (paralyze/stone)
        /// Gained handler removes state 0x1A
        /// </summary>
        public const byte STATE_SUPPRESS_1A = 0x12;

        /// <summary>
        /// Derived sub-state of 0x14
        /// Applied only from 0x14's gained handler (permanent)
        /// Removed only from 0x14's lost handler
        /// </summary>
        public const byte STATE_DERIVED_OF_14 = 0x13;

        /// <summary>
        /// Parent state, threshold > 3 on record value
        /// If value > 3: applies 0x13 permanent
        /// Lost handler removes 0x13
        /// </summary>
        public const byte STATE_PARENT_14 = 0x14;

        /// <summary>
        /// 蛛网 (spider web)
        /// From native notify strings
        /// </summary>
        public const byte STATE_SPIDER_WEB = 0x18;

        /// <summary>
        /// 麻痹 or 石化 (paralyze or stone) - CONFLICT in native strings
        /// Has cooldown deadline at obj+0x3A4
        /// Gained handler: calculates duration from word[obj+0x26C]
        /// </summary>
        public const byte STATE_PARALYZE_STONE = 0x1A;

        /// <summary>
        /// 中毒 - Green poison (one of three poison states)
        /// Native notify: "你中毒了！"
        /// DoT tick in vmt+0x10 (2500ms @ obj+0x28)
        /// </summary>
        public const byte STATE_POISON_GREEN = 0x1E;

        /// <summary>
        /// 中毒 - Yellow/Blue poison (shares notify with 0x1E/0x35)
        /// DoT tick in vmt+0x10 (2500ms @ obj+0x28)
        /// </summary>
        public const byte STATE_POISON_YELLOW = 0x1F;

        /// <summary>
        /// 定身 (immobilize)
        /// From native notify strings
        /// </summary>
        public const byte STATE_IMMOBILIZE = 0x2D;

        /// <summary>
        /// Dispatches to state 7 or 8 by obj+0x72 (job)
        /// job==1 -> apply state 7 perm
        /// job==2 -> apply state 8 perm
        /// </summary>
        public const byte STATE_JOB_DISPATCH = 0x32;

        /// <summary>
        /// 单人坐骑 (single-rider mount) - rider/driver
        /// From native notify strings and 5 independent subsystems
        /// </summary>
        public const byte STATE_MOUNT_SINGLE = 0x33;

        /// <summary>
        /// 双人坐骑 (two-rider mount) - passenger
        /// Global apply-block (see §6 gate)
        /// Most-consulted state (41 HasState sites)
        /// </summary>
        public const byte STATE_MOUNT_DOUBLE = 0x34;

        /// <summary>
        /// 中毒 - Third poison state (shares notify with 0x1E/0x1F)
        /// DoT tick in vmt+0x10 (2500ms @ obj+0x28)
        /// </summary>
        public const byte STATE_POISON_THIRD = 0x35;

        /// <summary>
        /// 凝冰 (frozen)
        /// From native notify strings
        /// </summary>
        public const byte STATE_FROZEN = 0x3E;

        // §2.B Band 0x15..0x6A - bonus-ability contributions
        // These states contribute to TNakedAbility accumulator
        // Full table in spec §2.B (86 states, 29 unique handlers)

        /// <summary>
        /// Ability contribution: [+0x24] += (word[edi+0x278] / 7) + 2
        /// </summary>
        public const byte STATE_ABILITY_15 = 0x15;

        /// <summary>
        /// Ability contribution: [+0x1C] += (word[edi+0x278] / 7) + 2
        /// </summary>
        public const byte STATE_ABILITY_16 = 0x16;

        // ... (86 total ability states, defining key ones)

        /// <summary>
        /// Ability contribution: doubles [+0x64], [+0x6C], [+0x74] in place
        /// Ignores value field
        /// </summary>
        public const byte STATE_ABILITY_DOUBLE = 0x2C;

        /// <summary>
        /// Ability contribution: SUBTRACT, floored at 0
        /// For each of +0x18,+0x1C,+0x20,+0x24: result = max(0, current - v)
        /// </summary>
        public const byte STATE_ABILITY_SUBTRACT = 0x36;

        /// <summary>
        /// Ability contribution: multiplier state (value must == 1)
        /// +0x28..+0x3C x 1.2 (x87 extended precision)
        /// +0x18..+0x54 x 1.5 (float32)
        /// Results via @TRUNC (toward zero)
        /// </summary>
        public const byte STATE_ABILITY_MULTIPLIER = 0x2A;

        // §2.C Recompute-trigger bitmap
        // 37 states trigger ability cache recalc when gained/lost
        // Bitmap @0x77326C, bias +8
    }
}
