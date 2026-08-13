using SystemModule;

namespace GameSvr
{
    // Dormant, evidence-backed, side-effect-free model of the native M2Server
    // monster-AI target-search + attack-decision ladder (the #1 documented combat
    // residual, staging/inproc_engine_harness_20260731.md §7.3). Hex-Rays verified
    // against m2full.i64 (image base 0x00400000). NOT wired: it performs no map
    // scan, no packet, no RandomNumber draw and no state mutation. It is a pure
    // classifier that, given a monster tick's observable inputs, returns the exact
    // native decision, so the live Monster.Run / AnimalObject.SearchTarget /
    // TBaseObject.GetAttackDir re-implementation and the AuditTools check can be
    // pinned to the reversed ladders. Where the native routing is subclass-virtual
    // (the concrete swing into _Attack / HitMagic) or belongs to the timed-ability
    // scheduler, the model fails closed rather than fabricating an outcome.
    //
    // ---- native call graph (standard aggressive monster) ----
    //   Run tick (vtbl+0xA8)  sub_666AE4 @0x00666AE4:
    //       if ( (*(vtbl+0x40))() )                       // CanRun guard sub_7671F0
    //         v = GetTickCount();                          // sub_408340
    //         if ( (v - self[+136]) > 0x1F40               // 8000ms hard refresh
    //           || (v - self[+136]) > 0x3E8 && !self[+836] ) // 1000ms if no target
    //             { self[+136] = v; SearchTarget(); }      // sub_71DA70
    //       return BaseRun();                              // sub_66622C (movement)
    //     sibling  sub_66715C -> SearchTarget2 sub_71DCD8 -> sub_666AE4
    //     special  sub_667420 (dual-timer 0x7530=30000ms boss) -> sub_666AE4
    //   SearchTarget (full)   sub_71DA70 @0x0071DA70   (view-range + hide/cool-eye gated)
    //   SearchTarget (simple) sub_71DCD8 @0x0071DCD8   (no view-range / hide gate)
    //   BaseRun               sub_66622C @0x0066622C -> Wondering sub_71E8C4 /
    //                         GotoTargetXY sub_71DDD0 / TCreature run sub_71E50C
    //   IsProperTarget sub_767498 ; SetTargetCreat sub_76719C ; hide-check sub_76B438
    //   nearest-range helper sub_76B4A4 ; global block gate sub_772DA8
    //
    // native field offsets on the monster object (bytes):
    //   +116 hold/"can-walk" byte   +120 m_nViewRange   +136 m_dwSearchEnemyTick
    //   +300 CurrX  +304 CurrY      +713 m_boCoolEye     +836 m_TargetCret
    //   +904 visible-actor list head (node +12 = actor, +16 = next)
    //   +908 m_Master   +1108/+1112 m_nTargetX/Y   +1124 sticky-target cache
    public static class NativeMonsterAiSearchAttack
    {
        // sub_666AE4 search-timer thresholds (milliseconds).
        public const int SearchRefreshHardMs = 0x1F40;   // 8000: re-scan even with a target
        public const int SearchRefreshIdleMs = 0x3E8;    // 1000: re-scan only when target-less
        // sub_71DA70 sticky-keep: prefer the *designated* target cached at native
        // self[+1124] (a slot distinct from m_TargetCret@+836; SetTargetCreat
        // sub_76719C writes +836/+840, NOT +1124) while it stays alive, un-blocked
        // and within this range. IMPORTANT: the range test uses sub_76B4A4 =
        // CHEBYSHEV max(|dx|,|dy|) < 5 (NOT the Manhattan metric the nearest-scan
        // uses). +1124 is set only by special paths (character-load sub_6AFD7C /
        // sub_6521BC) and is cleared by SearchTarget itself — for a normal
        // search-acquired target (+1124==0) the sticky branch never fires and the
        // scan runs every tick, so this is a designated/priority-target mechanism,
        // not a general "don't re-scan my current target" optimization.
        public const int StickyTargetRange = 5;          // Chebyshev < 5 (sub_76B4A4)
        public const int NearestSentinel = 999;          // n999 initial "no nearest yet"
        public const int DefaultViewRange = 5;           // m_nViewRange (+120) default
        public const int DefaultNextHitTime = 2000;      // m_nNextHitTime default

        // ---- 1. Run think-tick search gate (sub_666AE4) ----

        public enum NativeSearchTickDecision
        {
            Skip,           // guard false, or neither timer elapsed
            RefreshSearch   // self[+136]=now; SearchTarget() runs this tick
        }

        public readonly struct NativeSearchTickContext
        {
            public NativeSearchTickContext(bool canRun, int tickNow,
                int searchEnemyTick, bool hasTarget)
            {
                CanRun = canRun;
                TickNow = tickNow;
                SearchEnemyTick = searchEnemyTick;
                HasTarget = hasTarget;
            }

            public bool CanRun { get; }               // (*(vtbl+0x40))() sub_7671F0
            public int TickNow { get; }               // GetTickCount sub_408340
            public int SearchEnemyTick { get; }       // self[+136]
            public bool HasTarget { get; }            // self[+836] != 0
        }

        public static NativeSearchTickDecision DecideSearchTick(
            NativeSearchTickContext c)
        {
            if (!c.CanRun) return NativeSearchTickDecision.Skip;
            var elapsed = unchecked((uint)(c.TickNow - c.SearchEnemyTick));
            if (elapsed > SearchRefreshHardMs
                || (elapsed > SearchRefreshIdleMs && !c.HasTarget))
                return NativeSearchTickDecision.RefreshSearch;
            return NativeSearchTickDecision.Skip;
        }

        // ---- 2. SearchTarget target selection (sub_71DA70 full / sub_71DCD8 simple) ----

        public readonly struct NativeVisibleActor
        {
            public NativeVisibleActor(long id, int x, int y, bool death,
                bool hideMode, bool properTarget, bool blocked = false)
            {
                Id = id;
                X = x;
                Y = y;
                Death = death;
                HideMode = hideMode;
                ProperTarget = properTarget;
                Blocked = blocked;
            }

            public long Id { get; }
            public int X { get; }
            public int Y { get; }
            public bool Death { get; }               // actor[+115]/m_boDeath
            public bool HideMode { get; }            // sub_76B438 hide check
            public bool ProperTarget { get; }        // IsProperTarget sub_767498
            public bool Blocked { get; }             // global gate sub_772DA8
        }

        public readonly struct NativeSearchTargetContext
        {
            public NativeSearchTargetContext(int selfX, int selfY, int viewRange,
                bool coolEye, IReadOnlyList<NativeVisibleActor> visible,
                bool applyViewRangeAndHideGate = true)
            {
                SelfX = selfX;
                SelfY = selfY;
                ViewRange = viewRange;
                CoolEye = coolEye;
                Visible = visible ?? Array.Empty<NativeVisibleActor>();
                ApplyViewRangeAndHideGate = applyViewRangeAndHideGate;
            }

            public int SelfX { get; }
            public int SelfY { get; }
            public int ViewRange { get; }            // self[+120]
            public bool CoolEye { get; }             // self[+713]
            public IReadOnlyList<NativeVisibleActor> Visible { get; }
            // true  = sub_71DA70 (view-range box + hide/cool-eye gate)
            // false = sub_71DCD8 (proper-target only)
            public bool ApplyViewRangeAndHideGate { get; }
        }

        /// <summary>
        /// Nearest proper target the native SearchTarget scan would pick, or a
        /// negative sentinel when it would set no target. Manhattan distance,
        /// strictly-less comparison so the earliest of equal-distance actors wins
        /// (matches n999 update order).
        /// </summary>
        public static long SelectTarget(NativeSearchTargetContext c)
        {
            var nearest = NearestSentinel;
            long chosen = -1;
            foreach (var actor in c.Visible)
            {
                if (actor.Death || actor.Blocked) continue;
                if (!actor.ProperTarget) continue;
                if (c.ApplyViewRangeAndHideGate)
                {
                    if (actor.HideMode && !c.CoolEye) continue;
                    if (Math.Abs(c.SelfX - actor.X) > c.ViewRange
                        || Math.Abs(c.SelfY - actor.Y) > c.ViewRange)
                        continue;
                }
                var dist = Math.Abs(c.SelfX - actor.X) + Math.Abs(c.SelfY - actor.Y);
                if (dist < nearest)
                {
                    nearest = dist;
                    chosen = actor.Id;
                }
            }
            return chosen;
        }

        /// <summary>
        /// sub_71DA70 sticky pre-check for the designated-target slot (native
        /// self[+1124], NOT m_TargetCret): return true when the monster re-affirms
        /// that cached target and skips the visible-actor scan. <paramref
        /// name="cacheRange"/> MUST be the CHEBYSHEV distance max(|dx|,|dy|) from
        /// sub_76B4A4 (kept iff &lt; <see cref="StickyTargetRange"/>); <paramref
        /// name="cacheBlocked"/> is the target's native +116 hold/block byte
        /// (sub_772DA8, the same field the vtbl+512 "busy" getter returns).
        /// </summary>
        public static bool KeepsStickyTarget(bool cacheAlive, bool cacheBlocked,
            int cacheRange) =>
            cacheAlive && !cacheBlocked && cacheRange < StickyTargetRange;

        /// <summary>Chebyshev distance sub_76B4A4 = max(|selfX-x|, |selfY-y|).
        /// Used only by the sticky designated-target pre-check (the nearest-target
        /// scan uses Manhattan). </summary>
        public static int StickyChebyshev(int selfX, int selfY, int x, int y) =>
            Math.Max(Math.Abs(selfX - x), Math.Abs(selfY - y));

        // ---- 3. GetAttackDir: 3x3 adjacency -> facing (TBaseObject GetAttackDir) ----

        /// <summary>
        /// True and a facing 0..7 when the target occupies one of the eight cells
        /// bordering the monster; false (btDir untouched by the ladder) when the
        /// target is the same cell or farther than one tile. Direction encoding is
        /// the native Grobal2 order (DR_UP=0 … DR_UPLEFT=7), confirmed by the
        /// GotoTargetXY step cascade sub_71DDD0.
        /// </summary>
        public static bool TryGetAttackDir(int selfX, int selfY, int targetX,
            int targetY, out byte dir)
        {
            dir = 0;
            var inBox = selfX - 1 <= targetX && selfX + 1 >= targetX
                        && selfY - 1 <= targetY && selfY + 1 >= targetY
                        && (selfX != targetX || selfY != targetY);
            if (!inBox) return false;
            var dx = targetX - selfX;
            var dy = targetY - selfY;
            dir = (dx, dy) switch
            {
                (-1, 0) => (byte)Grobal2.DR_LEFT,       // 6
                (1, 0) => (byte)Grobal2.DR_RIGHT,       // 2
                (0, -1) => (byte)Grobal2.DR_UP,         // 0
                (0, 1) => (byte)Grobal2.DR_DOWN,        // 4
                (-1, -1) => (byte)Grobal2.DR_UPLEFT,    // 7
                (1, -1) => (byte)Grobal2.DR_UPRIGHT,    // 1
                (-1, 1) => (byte)Grobal2.DR_DOWNLEFT,   // 5
                (1, 1) => (byte)Grobal2.DR_DOWNRIGHT,   // 3
                _ => (byte)0
            };
            return true;
        }

        // ---- 4. AttackTarget swing gate (Monster.AttackTarget over GetAttackDir) ----

        public enum NativeMonsterAttackAction
        {
            NoTarget,       // m_TargetCret == null
            Swing,          // adjacent AND hit-cooldown elapsed -> Attack(dir)
            HoldForCooldown,// adjacent but still within m_nNextHitTime (handled, no swing)
            Chase,          // not adjacent, same map -> SetTargetXY(target)
            DropTarget      // not adjacent, different map -> DelTargetCreat
        }

        public readonly struct NativeAttackDecisionContext
        {
            public NativeAttackDecisionContext(bool hasTarget, bool adjacent,
                int tickNow, int hitTick, int nextHitTime, bool sameMap)
            {
                HasTarget = hasTarget;
                Adjacent = adjacent;
                TickNow = tickNow;
                HitTick = hitTick;
                NextHitTime = nextHitTime;
                SameMap = sameMap;
            }

            public bool HasTarget { get; }
            public bool Adjacent { get; }            // TryGetAttackDir == true
            public int TickNow { get; }
            public int HitTick { get; }              // m_dwHitTick
            public int NextHitTime { get; }          // m_nNextHitTime
            public bool SameMap { get; }             // target.m_PEnvir == self.m_PEnvir
        }

        public static NativeMonsterAttackAction DecideAttack(
            NativeAttackDecisionContext c)
        {
            if (!c.HasTarget) return NativeMonsterAttackAction.NoTarget;
            if (c.Adjacent)
            {
                // MONAI-16 — AttackTarget sub_71E914 @0x71E94B is SIGNED `jle`:
                //   0071E945  2B 93 5C 03 00 00  sub edx,[ebx+0x35C]
                //   0071E94B  3B 93 20 03 00 00  cmp edx,[ebx+0x320]
                //   0071E951  7E 26              jle 0x71E979
                // Live Monster.AttackTarget uses the same signed `>`. An unsigned
                // wrap compare would swing on a future hit-tick (ctor + Random(3000)).
                var elapsed = c.TickNow - c.HitTick;
                return elapsed > c.NextHitTime
                    ? NativeMonsterAttackAction.Swing
                    : NativeMonsterAttackAction.HoldForCooldown;
            }
            return c.SameMap
                ? NativeMonsterAttackAction.Chase
                : NativeMonsterAttackAction.DropTarget;
        }

        // ---- 5. Wondering roam decision (sub_71E8C4) ----

        public enum NativeWanderAction { Stay, Turn, Walk }

        /// <summary>
        /// Native roam draw order: Random(20); if it is 0, Random(4) selects Turn
        /// (==1, then Random(8) picks the new facing) vs Walk. Any non-zero
        /// Random(20) is a no-op tick. Caller supplies the raw draws so the model
        /// stays deterministic and consumes no RandomNumber itself.
        /// </summary>
        public static NativeWanderAction DecideWander(int random20, int random4)
        {
            if (random20 != 0) return NativeWanderAction.Stay;
            return random4 == 1 ? NativeWanderAction.Turn : NativeWanderAction.Walk;
        }

        // ---- 6. Out-of-scope boundary (fail closed) ----

        /// <summary>
        /// The timed-ability scheduler tick (skills 27/44/46/74 etc.) and the
        /// subclass-virtual swing routing into the concrete _Attack / HitMagic /
        /// SpitMap paths are not modelled here — they are actor-executor entangled
        /// and are the remaining bounded half of the combat domain. This fails
        /// closed so nothing drives those paths through this model.
        /// </summary>
        public static bool NoGoTimedAbilityAndConcreteSwing() => false;
    }
}
