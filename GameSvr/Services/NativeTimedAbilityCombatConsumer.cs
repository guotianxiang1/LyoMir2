using System;

namespace GameSvr.Services
{
    /// <summary>
    /// DORMANT, FAIL-CLOSED reference model of the native M2Server
    /// (M2Server_unpacked_fixed.exe, image base 0x400000) combat consumers of
    /// the PAS timed abilities that are still fail-closed in the C# rewrite:
    ///
    ///   * script type 44  -> internal 76 (0x4C) -> FASTNESS Union carrier
    ///                        (actor +0x400); combat consumer is the union
    ///                        damage receiver <c>sub_741764</c> (VMT +0x198).
    ///   * script type 46  -> internal 78 (0x4E) -> job attack endpoints
    ///                        (DC/MC/SC/CC at +0x28C..+0x2A8); combat consumers
    ///                        are the job-endpoint selector <c>sub_76CD8C</c>
    ///                        /<c>sub_76CD5C</c> and the four job-3 CC attack
    ///                        resolvers 1024/260/264/268.
    ///   * script type 74  -> internal 106 (0x6A) -> magic-hit carrier
    ///                        (actor +0x272); combat consumer is the magic-hit
    ///                        contest <c>sub_7744B4</c>, gated in front of 15
    ///                        unique spell-damage owner functions.
    ///
    /// This type is a pure calculator. It is NOT wired into any live combat
    /// path and must stay that way: types 46 and 74 are absent from
    /// <c>TBaseObject.IsSupportedTimedAbilityType</c>, so their carriers are
    /// never populated in production, and nothing calls this model. It exists
    /// as the byte-accurate specification the audit checks against.
    ///
    /// Every branch and constant below was taken from the disassembly of
    /// M2Server_unpacked_fixed.exe. x87 constants were decoded from the raw
    /// 80-bit/32-bit data bytes (see staging/fp_consts.txt), not from
    /// Hex-Rays' corrupted extended-precision literal rendering.
    ///
    /// Genuinely-missing production hooks are abstracted as inputs and marked
    /// HOOK: the caller supplies the native RNG (<c>sub_403B4C</c>), the attack
    /// power roll (VMT +0xCC), the effective magic level (<c>sub_4C896C</c>),
    /// hit-eligibility (<c>sub_767498</c>), the internal-state queries/removals,
    /// and the damage/target enumeration primitives.
    /// </summary>
    public static class NativeTimedAbilityCombatConsumer
    {
        /// <summary>
        /// Machine-checkable assertion that this model is dormant. It is never
        /// invoked by live combat; types 46/74 are not admitted by
        /// <c>TBaseObject.IsSupportedTimedAbilityType</c>.
        /// </summary>
        public const bool DormantNotWiredIntoLiveCombat = true;

        // Native actor field offsets referenced by the consumers below.
        public const int SelfJobOffset = 0x72;               // btJob
        public const int RaceOffset = 0x178;                 // btRace
        public const int AntiMagicOffset = 0x270;            // target anti-magic
        public const int Type74MagicHitOffset = 0x272;       // source type-74 carrier
        public const int UnionSelectorOffset = 0x400;        // FASTNESS Union carrier
        public const int UnionManaOffset = 0x2B4;            // self mana pool
        public const int UnionFixedRecordOffset = 0x4C0;     // fixed-ability object
        public const int UnionFlatReductionOffset = 0x154;   // u16 within +0x4C0
        public const int UnionPercentReductionOffset = 0x167; // u8 within +0x4C0
        public const int JobEndpointDcLowOffset = 0x28C;
        public const int JobEndpointCcLowOffset = 0x2A4;
        public const int JobEndpointCcHighOffset = 0x2A8;
        public const int Job3ChargeOffset = 0x3E0;           // 1024 charge word

        // <c>sub_403580</c> is Delphi Round(): x87 round-half-to-even.
        private static int RoundNative(double value) =>
            (int)Math.Round(value, MidpointRounding.ToEven);

        // =====================================================================
        // TYPE 44 (internal 76) - FASTNESS Union damage receiver: sub_741764.
        //
        // sub_741764 is the target's VMT +0x198 handler. On both the player VMT
        // (0x006AC8C8) and the hero VMT (0x00685630) slot +0x198 resolves to
        // 0x00741764. Signature (Borland register call):
        //   int sub_741764(self@eax, other@edx, magic@ecx, damage@[arg_0])
        // where <c>self</c> is the actor taking the hit, <c>other</c> is the
        // attacker, <c>magic</c> is the skill object and <c>damage</c> is the
        // incoming damage passed and returned by reference.
        //
        // Exact order:
        //   1. if sub_4C896C(magic) >= 4  &&  other.job(+0x72) == 0:
        //        drain = Round(damage * jobCoeff[self.job]);
        //          job 0 -> 0.10 (tbyte_741854)
        //          job 1 -> 0.40 (tbyte_741860)
        //          job 2 -> 0.20 (tbyte_74186C)
        //          job 3 -> 0.20 (tbyte_74186C, shares the job-2 constant)
        //          else  -> 0
        //        self.mana(+0x2B4) = drain > mana ? 0 : mana - drain;
        //        sub_7693E8(self);                    // recompute/notify hook
        //   2. sub_791980(self.union(+0x400), &damage);  // union table reduce
        //   3. damage -= u16[(self+0x4C0)+0x154];         // flat reduce
        //   4. return damage * (100 - u8[(self+0x4C0)+0x167]) / 100; // signed
        //
        // Steps 2-4 are already reproduced by
        // TBaseObject.ApplyNativeUnionDamageReductions and step 1 by
        // HeroObject.ApplyNativeUnionTargetManaCost. This model is the
        // consolidated byte-accurate reference for all four steps together.
        // =====================================================================

        public readonly struct Type44UnionContext
        {
            public Type44UnionContext(byte selfJob, byte otherJob,
                int effectiveMagicLevel, int incomingDamage, int unionSelector,
                NativeFastnessTable unionTable, int flatReduction,
                int percentReduction, int selfMana)
            {
                SelfJob = selfJob;
                OtherJob = otherJob;
                EffectiveMagicLevel = effectiveMagicLevel;
                IncomingDamage = incomingDamage;
                UnionSelector = unionSelector;
                UnionTable = unionTable;
                FlatReduction = flatReduction;
                PercentReduction = percentReduction;
                SelfMana = selfMana;
            }

            /// <summary>self.job (byte at self+0x72); chooses the mana coefficient.</summary>
            public byte SelfJob { get; }
            /// <summary>attacker.job (byte at other+0x72); gate requires == 0.</summary>
            public byte OtherJob { get; }
            /// <summary>HOOK: sub_4C896C(magic) effective magic level; gate requires >= 4.</summary>
            public int EffectiveMagicLevel { get; }
            /// <summary>Incoming damage (arg_0).</summary>
            public int IncomingDamage { get; }
            /// <summary>self.unionSelector (int at self+0x400).</summary>
            public int UnionSelector { get; }
            /// <summary>Global FASTNESS_UNION table (off_7D6808 -&gt; sub_791980).</summary>
            public NativeFastnessTable UnionTable { get; }
            /// <summary>u16 at (self+0x4C0)+0x154 (flat reduction).</summary>
            public int FlatReduction { get; }
            /// <summary>u8 at (self+0x4C0)+0x167 (percent reduction).</summary>
            public int PercentReduction { get; }
            /// <summary>self.mana (int at self+0x2B4); modified in-place by the side effect.</summary>
            public int SelfMana { get; }
        }

        public readonly struct Type44UnionResult
        {
            public Type44UnionResult(int finalDamage, int newSelfMana,
                bool manaSideEffectApplied)
            {
                FinalDamage = finalDamage;
                NewSelfMana = newSelfMana;
                ManaSideEffectApplied = manaSideEffectApplied;
            }

            /// <summary>Return value of sub_741764 (final damage).</summary>
            public int FinalDamage { get; }
            /// <summary>self+0x2B4 after the side effect.</summary>
            public int NewSelfMana { get; }
            /// <summary>True iff the gate passed and sub_7693E8 fired.</summary>
            public bool ManaSideEffectApplied { get; }
        }

        public static Type44UnionResult ApplyType44UnionReceiver(
            in Type44UnionContext ctx)
        {
            int damage = ctx.IncomingDamage;
            int selfMana = ctx.SelfMana;
            bool manaApplied = false;

            // Step 1: job/level mana side effect (gate then unconditional
            // sub_7693E8). The gate is sub_4C896C(magic) >= 4 AND other.job == 0.
            if (ctx.EffectiveMagicLevel >= 4 && ctx.OtherJob == 0)
            {
                double coefficient = ctx.SelfJob switch
                {
                    0 => 0.10,      // tbyte_741854
                    1 => 0.40,      // tbyte_741860
                    2 or 3 => 0.20, // tbyte_74186C (job 2 and 3 share it)
                    _ => 0.0        // native leaves v6 = 0 for other jobs
                };
                int drain = RoundNative(damage * coefficient);
                selfMana = drain > selfMana ? 0 : selfMana - drain;
                manaApplied = true; // sub_7693E8 runs whenever the gate passes
            }

            // Step 2: union table reduction (sub_791980), selector = self+0x400.
            if (ctx.UnionTable != null)
            {
                damage = ctx.UnionTable.ApplyReduction(damage, ctx.UnionSelector);
            }

            // Step 3: flat reduction (u16 at (self+0x4C0)+0x154).
            damage = unchecked(damage - ctx.FlatReduction);

            // Step 4: percent reduction, signed imul/idiv by 100.
            damage = unchecked(damage * (100 - ctx.PercentReduction)) / 100;

            return new Type44UnionResult(damage, selfMana, manaApplied);
        }

        // =====================================================================
        // TYPE 46 (internal 78) - job attack endpoint selection.
        //
        // sub_76CD8C(self@eax, mode@dl) picks the job-specific endpoint pair by
        // the job byte at self+0x72, then defers to sub_76CD5C for the mode
        // resolution:
        //   job 0 -> DC (+0x28C low, +0x290 high)
        //   job 1 -> MC (+0x294 low, +0x298 high)
        //   job 2 -> SC (+0x29C low, +0x2A0 high)
        //   job 3 -> CC (+0x2A4 low, +0x2A8 high)
        //   other -> returns 0 with no sub_76CD5C call (no RNG consumed).
        //
        // sub_76CD5C(high@eax, low@edx, &mode):
        //   mode != 0            -> high endpoint
        //   mode == 0, low >= high -> low endpoint
        //   mode == 0, low <  high -> low + Random(high - low)  (high exclusive)
        //
        // The type-46 timed node adds its value to BOTH CC endpoints
        // (+0x2A4 and +0x2A8) via sub_7733C0.
        // =====================================================================

        public readonly struct Type46EndpointContext
        {
            public Type46EndpointContext(byte job, int dcLow, int dcHigh,
                int mcLow, int mcHigh, int scLow, int scHigh, int ccLow,
                int ccHigh, byte modeByte)
            {
                Job = job;
                DcLow = dcLow; DcHigh = dcHigh;
                McLow = mcLow; McHigh = mcHigh;
                ScLow = scLow; ScHigh = scHigh;
                CcLow = ccLow; CcHigh = ccHigh;
                ModeByte = modeByte;
            }

            public byte Job { get; }
            public int DcLow { get; } public int DcHigh { get; }
            public int McLow { get; } public int McHigh { get; }
            public int ScLow { get; } public int ScHigh { get; }
            public int CcLow { get; } public int CcHigh { get; }
            /// <summary>dl passed to sub_76CD8C; nonzero selects the high endpoint.</summary>
            public byte ModeByte { get; }
        }

        /// <summary>
        /// sub_76CD8C + sub_76CD5C. <paramref name="random"/> is HOOK
        /// sub_403B4C: Random(n) uniform in [0, n). It is only consumed on the
        /// mode==0, low&lt;high branch, matching the native RNG order.
        /// </summary>
        public static int SelectType46JobEndpoint(
            in Type46EndpointContext ctx, Func<int, int> random)
        {
            // sub_76CD8C: jobs outside {0,1,2,3} return 0 with no RNG use.
            if (ctx.Job > 3)
            {
                return 0;
            }

            (int low, int high) = ctx.Job switch
            {
                0 => (ctx.DcLow, ctx.DcHigh),
                1 => (ctx.McLow, ctx.McHigh),
                2 => (ctx.ScLow, ctx.ScHigh),
                _ => (ctx.CcLow, ctx.CcHigh), // job 3
            };

            // sub_76CD5C.
            if (ctx.ModeByte != 0)
            {
                return high;
            }
            if (low >= high)
            {
                return low;
            }
            return low + random(high - low);
        }

        // =====================================================================
        // TYPE 46 - job-3 CC attack resolvers 1024/260/264/268.
        //
        // All four read the CC endpoints directly (+0x2A4 low, +0x2A8 high) and
        // roll base power with VMT +0xCC: base = GetAttackPower(CCLow,
        // CCHigh - CCLow). effLevel is sub_4C896C(magic) low byte.
        //
        //   1024 sub_77136C: power = base. Gate job==3, hit-eligible
        //        (sub_767498), not blocked (!sub_772578). Delivers, repeats the
        //        hit if sub_7712B0 (extra-hit chain), and if base > 0 consumes
        //        one charge at self+0x3E0.
        //   260  sub_771570: needs internal state 65 (0x41, sub_773B98); if
        //        absent falls back to 1024. Else power =
        //        Round((0.2*effLevel + 1.8) * base), removes state 65, one hit.
        //   264  sub_77176C: needs internal state 68 (0x44, sub_772960). power =
        //        Round((0.2*effLevel + 2.4) * base). Removes state 68 BEFORE the
        //        hit checks, hits the main target then up to two direction-
        //        derived adjacent targets (n = 1,2).
        //   268  sub_7718E4: needs a non-null target and internal state 69
        //        (0x45, sub_772960). power = Round((0.2*effLevel + 3.0) * base).
        //        Removes state 69 first, builds a direction/range target list
        //        and delivers to every entry, flagging the primary target.
        //
        // Multiplier constants decoded from raw bytes (staging/fp_consts.txt):
        //   0.2 = tbyte_771658 / tbyte_7718CC / tbyte_771A4C
        //   1.8 = tbyte_771664 ; 2.4 = tbyte_7718D8 ; 3.0 = flt_771A58
        // =====================================================================

        public enum Job3ResolverOutcome
        {
            /// <summary>Self job was not 3 (1024 only): returns 0.</summary>
            NotJob3,
            /// <summary>Required internal state was absent (264/268).</summary>
            StateAbsent,
            /// <summary>State 65 absent: resolver 260 delegates to 1024.</summary>
            FallbackTo1024,
            /// <summary>Gate passed but hit-eligibility failed for the main target.</summary>
            NotHitEligible,
            /// <summary>Gate passed and the power was computed/delivered.</summary>
            Delivered,
        }

        public readonly struct Job3ResolverContext
        {
            public Job3ResolverContext(byte selfJob, int ccLow, int ccHigh,
                int effectiveMagicLevel, int attackPower, bool hasRequiredState,
                bool mainTargetHitEligible, bool blocked, bool targetIsNull)
            {
                SelfJob = selfJob;
                CcLow = ccLow;
                CcHigh = ccHigh;
                EffectiveMagicLevel = effectiveMagicLevel;
                AttackPower = attackPower;
                HasRequiredState = hasRequiredState;
                MainTargetHitEligible = mainTargetHitEligible;
                Blocked = blocked;
                TargetIsNull = targetIsNull;
            }

            /// <summary>self.job (self+0x72). 1024 requires 3.</summary>
            public byte SelfJob { get; }
            public int CcLow { get; }
            public int CcHigh { get; }
            /// <summary>HOOK sub_4C896C(magic) low byte.</summary>
            public int EffectiveMagicLevel { get; }
            /// <summary>HOOK VMT +0xCC: GetAttackPower(CcLow, CcHigh - CcLow).</summary>
            public int AttackPower { get; }
            /// <summary>HOOK sub_773B98/sub_772960: state 65/68/69 present.</summary>
            public bool HasRequiredState { get; }
            /// <summary>HOOK sub_767498(self, target) for the main target.</summary>
            public bool MainTargetHitEligible { get; }
            /// <summary>HOOK sub_772578: 1024 proceeds only when this is false.</summary>
            public bool Blocked { get; }
            /// <summary>268 requires a non-null target before the state gate.</summary>
            public bool TargetIsNull { get; }
        }

        public readonly struct Job3ResolverResult
        {
            public Job3ResolverResult(Job3ResolverOutcome outcome, int power,
                bool consumesCharge)
            {
                Outcome = outcome;
                Power = power;
                ConsumesCharge = consumesCharge;
            }

            public Job3ResolverOutcome Outcome { get; }
            /// <summary>Computed hit power (0 when not delivered).</summary>
            public int Power { get; }
            /// <summary>1024 only: decrement self+0x3E0 when power &gt; 0 and a charge exists.</summary>
            public bool ConsumesCharge { get; }
        }

        /// <summary>Resolver 1024 (sub_77136C).</summary>
        public static Job3ResolverResult ResolveJob3Attack1024(
            in Job3ResolverContext ctx)
        {
            if (ctx.SelfJob != 3)
            {
                return new Job3ResolverResult(Job3ResolverOutcome.NotJob3, 0, false);
            }
            if (!ctx.MainTargetHitEligible || ctx.Blocked)
            {
                return new Job3ResolverResult(
                    Job3ResolverOutcome.NotHitEligible, 0, false);
            }
            int power = ctx.AttackPower; // base, no multiplier
            bool consumesCharge = power > 0;
            return new Job3ResolverResult(
                Job3ResolverOutcome.Delivered, power, consumesCharge);
        }

        /// <summary>Resolver 260 (sub_771570). Requires internal state 65.</summary>
        public static Job3ResolverResult ResolveJob3Attack260(
            in Job3ResolverContext ctx)
        {
            if (!ctx.HasRequiredState)
            {
                // Delegates to resolver 1024.
                return new Job3ResolverResult(
                    Job3ResolverOutcome.FallbackTo1024, 0, false);
            }
            if (!ctx.MainTargetHitEligible)
            {
                return new Job3ResolverResult(
                    Job3ResolverOutcome.NotHitEligible, 0, false);
            }
            int power = RoundNative(
                (0.2 * ctx.EffectiveMagicLevel + 1.8) * ctx.AttackPower);
            return new Job3ResolverResult(
                Job3ResolverOutcome.Delivered, power, false);
        }

        /// <summary>Resolver 264 (sub_77176C). Requires internal state 68.</summary>
        public static Job3ResolverResult ResolveJob3Attack264(
            in Job3ResolverContext ctx)
        {
            if (!ctx.HasRequiredState)
            {
                return new Job3ResolverResult(
                    Job3ResolverOutcome.StateAbsent, 0, false);
            }
            // Power is computed and state 68 removed before the hit checks, so
            // an ineligible main target still yields the computed power for the
            // adjacent enumeration; Delivered reports the computed value.
            int power = RoundNative(
                (0.2 * ctx.EffectiveMagicLevel + 2.4) * ctx.AttackPower);
            return new Job3ResolverResult(
                Job3ResolverOutcome.Delivered, power, false);
        }

        /// <summary>Resolver 268 (sub_7718E4). Requires non-null target and state 69.</summary>
        public static Job3ResolverResult ResolveJob3Attack268(
            in Job3ResolverContext ctx)
        {
            if (ctx.TargetIsNull || !ctx.HasRequiredState)
            {
                return new Job3ResolverResult(
                    Job3ResolverOutcome.StateAbsent, 0, false);
            }
            int power = RoundNative(
                (0.2 * ctx.EffectiveMagicLevel + 3.0) * ctx.AttackPower);
            return new Job3ResolverResult(
                Job3ResolverOutcome.Delivered, power, false);
        }

        // =====================================================================
        // TYPE 74 (internal 106) - magic-hit contest: sub_7744B4.
        //
        //   bool sub_7744B4(target@eax, source@edx):
        //     if target.race(+0x178) == 11: return false;   // undying race
        //     if source == null:            return false;
        //     chance = (100 * (source.type74(+0x272) + 10))
        //              / (target.antiMagic(+0x270) + 10);    // unsigned div
        //     chance = clamp(chance, 30, 95);                // Max then Min
        //     return chance > Random(100);                   // Random(100) < chance
        //
        // The contest is a uniform gate placed in front of the damage/effect of
        // 15 unique owner functions (19 call sites). See
        // <see cref="Type74ContestOwners"/>.
        // =====================================================================

        public readonly struct Type74ContestContext
        {
            public Type74ContestContext(byte targetRace, bool sourceIsNull,
                int sourceType74MagicHit, int targetAntiMagic)
            {
                TargetRace = targetRace;
                SourceIsNull = sourceIsNull;
                SourceType74MagicHit = sourceType74MagicHit;
                TargetAntiMagic = targetAntiMagic;
            }

            /// <summary>target.race (byte at target+0x178); race 11 always misses.</summary>
            public byte TargetRace { get; }
            /// <summary>source actor is null (edx == 0) -&gt; always misses.</summary>
            public bool SourceIsNull { get; }
            /// <summary>source.type74 carrier (u16 at source+0x272).</summary>
            public int SourceType74MagicHit { get; }
            /// <summary>target.antiMagic (u16 at target+0x270).</summary>
            public int TargetAntiMagic { get; }
        }

        /// <summary>
        /// sub_7744B4. <paramref name="random"/> is HOOK sub_403B4C:
        /// Random(100) uniform in [0, 100). RNG is consumed only after both
        /// early-out checks pass, matching the native order.
        /// </summary>
        public static bool Type74MagicHitContest(
            in Type74ContestContext ctx, Func<int, int> random)
        {
            if (ctx.TargetRace == 11)
            {
                return false;
            }
            if (ctx.SourceIsNull)
            {
                return false;
            }

            // Unsigned integer division; operands are non-negative (u16 + 10).
            int chance = (100 * (ctx.SourceType74MagicHit + 10))
                / (ctx.TargetAntiMagic + 10);
            chance = Math.Min(95, Math.Max(30, chance));
            return chance > random(100);
        }

        /// <summary>
        /// The 15 unique native owner functions that gate their spell damage on
        /// <c>sub_7744B4</c>, with each call site. Documentation only: the
        /// contest itself is <see cref="Type74MagicHitContest"/>; the owners are
        /// production spell dispatchers that are abstracted, not modeled here.
        /// </summary>
        public static readonly (uint Owner, uint[] CallSites, string Role)[]
            Type74ContestOwners =
        {
            (0x00609678u, new[] { 0x00609889u, 0x00609938u, 0x006099DBu },
                "player normal-cast dispatcher (magic ids 0x0B/0x0D/0x23)"),
            (0x00667284u, new[] { 0x00667302u },
                "object/spell damage entry"),
            (0x00676770u, new[] { 0x006767B6u },
                "object/spell damage entry"),
            (0x006769CCu, new[] { 0x00676A12u },
                "object/spell damage entry"),
            (0x00676E00u, new[] { 0x00676E4Fu },
                "object/spell damage entry"),
            (0x00676F90u, new[] { 0x00676FDFu },
                "object/spell damage entry"),
            (0x0071BB8Cu, new[] { 0x0071BD0Cu, 0x0071BEEEu, 0x0071BFAFu },
                "hero cast dispatcher (magic ids 1/5, 0x0B, 0x23)"),
            (0x00767F1Cu, new[] { 0x00767F75u },
                "per-cell straight-line target scan (contest per target)"),
            (0x0076DF5Cu, new[] { 0x0076E009u },
                "area/path target loop (contest per target)"),
            (0x0076E37Cu, new[] { 0x0076E3A5u },
                "human spell damage entry"),
            (0x0076EA3Cu, new[] { 0x0076EA65u },
                "human spell damage entry"),
            (0x0076EB54u, new[] { 0x0076EB8Fu },
                "human spell damage entry"),
            (0x0076F404u, new[] { 0x0076F437u },
                "human spell damage entry"),
            (0x0076FA5Cu, new[] { 0x0076FA8Du },
                "human spell damage entry"),
            (0x0076FBBCu, new[] { 0x0076FBF8u },
                "human spell damage entry"),
        };

        /// <summary>
        /// The eight <c>sub_76CD8C</c> consumer call sites for the job-selected
        /// endpoint (type 46), across six owner functions. Documentation only:
        /// the selector itself is <see cref="SelectType46JobEndpoint"/>. Two of
        /// these owners (sub_744894, sub_745BD8) are entangled with domains not
        /// represented in the C# runtime and are deliberately not modeled; the
        /// state-59 shield's <c>high * 2.5</c> constant is cited from the prior
        /// type-46 closeout and was not re-decoded in this pass.
        /// </summary>
        public static readonly (uint Owner, uint[] CallSites, string Behavior)[]
            Type46EndpointConsumers =
        {
            (0x0072035Cu, new[] { 0x007203C1u, 0x007203FCu },
                "power resolver: high-endpoint bonus for ids 1018/1000 and 1024"),
            (0x0073F9FCu, new[] { 0x0073FB6Du },
                "state-59 physical shield subtracts Trunc(high * 2.5), consumes a charge"),
            (0x00744894u, new[] { 0x00744BEEu },
                "endpoint in a separate probability/value formula (domain absent in C#)"),
            (0x00745BD8u, new[] { 0x00745C30u },
                "skill/state-152 activation derives a value from endpoint + effective level"),
            (0x00746130u, new[] { 0x00746267u, 0x007462A3u },
                "second power resolver: repeats the 1018/1000 and 1024 bonuses"),
            (0x0076CFC4u, new[] { 0x0076D7DCu },
                "general damage repeats the state-59 Trunc(high * 2.5) shield"),
        };
    }
}
