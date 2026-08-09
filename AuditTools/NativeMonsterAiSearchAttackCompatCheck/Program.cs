using GameSvr;
using SystemModule;
using M = GameSvr.NativeMonsterAiSearchAttack;

// Contract check for the dormant native monster-AI target-search + attack ladder
// (Monster.Run tick sub_666AE4, SearchTarget sub_71DA70/sub_71DCD8, GetAttackDir
// 3x3 adjacency, AttackTarget swing gate, Wondering sub_71E8C4). Pure model, no
// engine, no RandomNumber draw. See staging/monster_ai_search_attack_20260731.md.

var failures = 0;

// ---- constants ----
Equal(0x1F40, M.SearchRefreshHardMs, "hard search refresh 8000ms");
Equal(0x3E8, M.SearchRefreshIdleMs, "idle search refresh 1000ms");
Equal(5, M.StickyTargetRange, "sticky-target range 5");
Equal(999, M.NearestSentinel, "nearest sentinel 999");
Equal(5, M.DefaultViewRange, "default view range 5");

// ---- 1. Run think-tick search gate (sub_666AE4) ----
Equal(M.NativeSearchTickDecision.Skip,
    M.DecideSearchTick(new M.NativeSearchTickContext(false, 100000, 0, false)),
    "tick: guard false -> skip");
Equal(M.NativeSearchTickDecision.RefreshSearch,
    M.DecideSearchTick(new M.NativeSearchTickContext(true, 100000, 100000 - 9000, true)),
    "tick: >8000 with target -> refresh");
Equal(M.NativeSearchTickDecision.RefreshSearch,
    M.DecideSearchTick(new M.NativeSearchTickContext(true, 100000, 100000 - 1500, false)),
    "tick: >1000 no target -> refresh");
Equal(M.NativeSearchTickDecision.Skip,
    M.DecideSearchTick(new M.NativeSearchTickContext(true, 100000, 100000 - 1500, true)),
    "tick: >1000 with target -> skip");
Equal(M.NativeSearchTickDecision.Skip,
    M.DecideSearchTick(new M.NativeSearchTickContext(true, 100000, 100000 - 500, false)),
    "tick: <1000 -> skip");

// ---- 2. SearchTarget selection (sub_71DA70 full / sub_71DCD8 simple) ----
// self at (10,10), viewRange 5.
//   id1 (12,10) proper, in box, dist2
//   id2 (11,10) proper, HIDDEN, dist1
//   id3 (18,10) proper, dist8 (outside the +/-5 box)
//   id4 (10,12) DEATH
var visible = new List<M.NativeVisibleActor>
{
    new(1, 12, 10, death: false, hideMode: false, properTarget: true),
    new(2, 11, 10, death: false, hideMode: true,  properTarget: true),
    new(3, 18, 10, death: false, hideMode: false, properTarget: true),
    new(4, 10, 12, death: true,  hideMode: false, properTarget: true),
};
Equal(1L, M.SelectTarget(new M.NativeSearchTargetContext(10, 10, 5, false, visible, true)),
    "full: hidden+far+dead excluded -> nearest visible id1");
Equal(2L, M.SelectTarget(new M.NativeSearchTargetContext(10, 10, 5, true, visible, true)),
    "full+cool-eye: hidden id2 (dist1) now nearest");
Equal(2L, M.SelectTarget(new M.NativeSearchTargetContext(10, 10, 5, false, visible, false)),
    "simple: no box/hide gate -> nearest id2");
Equal(-1L, M.SelectTarget(new M.NativeSearchTargetContext(10, 10, 5, false,
        new List<M.NativeVisibleActor>
        {
            new(7, 11, 10, death: true, hideMode: false, properTarget: true),
            new(8, 11, 10, death: false, hideMode: false, properTarget: false),
        }, true)),
    "none proper/alive -> no target (-1)");
Equal(1L, M.SelectTarget(new M.NativeSearchTargetContext(10, 10, 5, false,
        new List<M.NativeVisibleActor>
        {
            new(1, 12, 10, death: false, hideMode: false, properTarget: true),
            new(9, 12, 10, death: false, hideMode: false, properTarget: true),
        }, true)),
    "tie distance -> earliest actor (strictly-less) id1");

// sticky designated-target pre-check (sub_71DA70, slot +1124, NOT m_TargetCret +836)
// range is Chebyshev max(|dx|,|dy|) from sub_76B4A4, kept iff < 5 strict
Assert(M.KeepsStickyTarget(cacheAlive: true, cacheBlocked: false, cacheRange: 4),
    "sticky: alive+unblocked+cheb4(<5) -> keep designated target");
Assert(!M.KeepsStickyTarget(cacheAlive: true, cacheBlocked: false, cacheRange: 5),
    "sticky: cheb==5 not < 5 -> re-scan");
Assert(!M.KeepsStickyTarget(cacheAlive: false, cacheBlocked: false, cacheRange: 1),
    "sticky: dead cache -> re-scan");
Assert(!M.KeepsStickyTarget(cacheAlive: true, cacheBlocked: true, cacheRange: 1),
    "sticky: blocked (+116 set) -> re-scan");
// Chebyshev metric sub_76B4A4 = max(|dx|,|dy|)  NOT Manhattan
Equal(3, M.StickyChebyshev(10, 10, 13, 12), "chebyshev max(3,2)=3");
Equal(2, M.StickyChebyshev(10, 10, 12, 10), "chebyshev max(2,0)=2");
Equal(2, M.StickyChebyshev(10, 10, 10, 12), "chebyshev max(0,2)=2");
Equal(2, M.StickyChebyshev(10, 10, 12, 12), "chebyshev max(2,2)=2 (diagonal)");
Assert(M.StickyChebyshev(10, 10, 15, 10) >= 5, "chebyshev(15,10)=5 >= 5 -> no sticky");

// ---- 3. GetAttackDir 3x3 adjacency (native DR_ encoding) ----
CheckDir(9, 10, Grobal2.DR_LEFT, "left");
CheckDir(11, 10, Grobal2.DR_RIGHT, "right");
CheckDir(10, 9, Grobal2.DR_UP, "up");
CheckDir(10, 11, Grobal2.DR_DOWN, "down");
CheckDir(9, 9, Grobal2.DR_UPLEFT, "up-left");
CheckDir(11, 9, Grobal2.DR_UPRIGHT, "up-right");
CheckDir(9, 11, Grobal2.DR_DOWNLEFT, "down-left");
CheckDir(11, 11, Grobal2.DR_DOWNRIGHT, "down-right");
Assert(!M.TryGetAttackDir(10, 10, 10, 10, out _), "same cell -> not adjacent");
Assert(!M.TryGetAttackDir(10, 10, 12, 10, out _), "two tiles away -> not adjacent");

// ---- 4. AttackTarget swing gate (Monster.AttackTarget) ----
Equal(M.NativeMonsterAttackAction.NoTarget,
    M.DecideAttack(new M.NativeAttackDecisionContext(false, false, 0, 0, 2000, true)),
    "attack: no target");
Equal(M.NativeMonsterAttackAction.Swing,
    M.DecideAttack(new M.NativeAttackDecisionContext(true, true, 10000, 0, 2000, true)),
    "attack: adjacent + cooldown elapsed -> swing");
Equal(M.NativeMonsterAttackAction.HoldForCooldown,
    M.DecideAttack(new M.NativeAttackDecisionContext(true, true, 1000, 0, 2000, true)),
    "attack: adjacent but within nNextHitTime -> hold");
Equal(M.NativeMonsterAttackAction.Chase,
    M.DecideAttack(new M.NativeAttackDecisionContext(true, false, 10000, 0, 2000, true)),
    "attack: not adjacent, same map -> chase");
Equal(M.NativeMonsterAttackAction.DropTarget,
    M.DecideAttack(new M.NativeAttackDecisionContext(true, false, 10000, 0, 2000, false)),
    "attack: not adjacent, other map -> drop target");

// ---- 5. Wondering roam (sub_71E8C4) ----
Equal(M.NativeWanderAction.Stay, M.DecideWander(5, 0), "wander: Random(20)!=0 -> stay");
Equal(M.NativeWanderAction.Turn, M.DecideWander(0, 1), "wander: 0 then Random(4)==1 -> turn");
Equal(M.NativeWanderAction.Walk, M.DecideWander(0, 2), "wander: 0 then Random(4)!=1 -> walk");

// ---- 6. fail-closed boundary ----
Assert(!M.NoGoTimedAbilityAndConcreteSwing(),
    "timed-ability + concrete swing routing stays NO-GO (fail closed)");

if (failures == 0)
{
    Console.WriteLine(
        "PASS NativeMonsterAiSearchAttackCompatCheck tick=sub_666AE4(8000/1000,+136) "
        + "search=sub_71DA70/sub_71DCD8(n999,view+120,IsProperTarget sub_767498) "
        + "attackdir=3x3(DR_ native) swing=hit-timer wander=sub_71E8C4(20/4/8) "
        + "timed-ability=NO-GO dormant=true");
    return 0;
}
Console.Error.WriteLine($"NativeMonsterAiSearchAttackCompatCheck FAIL ({failures})");
return 1;

void CheckDir(int tx, int ty, int expected, string label)
{
    var ok = M.TryGetAttackDir(10, 10, tx, ty, out var d);
    Assert(ok, "attackdir " + label + " adjacency");
    Equal((byte)expected, d, "attackdir " + label + " facing");
}

void Equal<T>(T expected, T actual, string msg)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        failures++;
        Console.Error.WriteLine($"FAIL {msg}: expected {expected}, got {actual}");
    }
}

void Assert(bool condition, string msg)
{
    if (!condition)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {msg}");
    }
}
