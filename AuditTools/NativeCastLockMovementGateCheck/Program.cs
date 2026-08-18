// MOVE-15 — the cast lock (native player+0x574) must refuse movement.
//
// Native contract. VMT+0x40 is the can-act predicate. TCreature/THumanKind use
// sub_76B354; TPlayer OVERRIDES the same slot with sub_6E6700, which calls the
// inherited predicate and then adds exactly one player-only term:
//   6E670D  E8 42 4C 08 00        call 0x76B354           ; inherited
//   6E6714  74 09                 je   0x6E671F           ; inherited false
//   6E6716  83 BE 74 05 00 00 00  cmp  dword [esi+0x574], 0
//   6E671D  74 04                 je   0x6E6723           ; zero  -> TRUE
//   6E671F  33 C0                 xor  eax, eax           ; non-0 -> FALSE
//
// The lock is written only in the CM_SPELL skill-27 branch
// (0x6BC9A3 `mov dword [esi+0x574],5`, 0x6BC9AF `... ,3`) and is counted down
// by the step processor sub_73F200, so +0x574 IS the forced-move counter, not a
// second field. C# calls it m_nNativeForcedMoveRemaining.
//
// Every dispatcher case that calls VMT+0x40 is therefore gated. The complete
// census of `call dword ptr [ecx+40h]` inside the CM dispatcher is:
//   0x6D9B6C  case 3010 turn   (arg dl=0)
//   0x6D9C07  case 3011 walk   (arg dl=0)
//   0x6D9C84  case 3012 pose   (arg dl=1)
//   0x6D9D23  case 3013 run    (arg dl=1)
//   0x6D9DBD  case 4108 run3   (arg dl=1)
//   0x6D9EDF  cases 3014-3016,3018,3019,3024-3026 (hit family)
//   0x6DA0A4  case 3017 spell
// This audit covers the five movement cases and STATE-50's CM_SPELL branch.
// The hit arms have their own ordering fixture in NativeHitArmGateCheck.
//
// The refusal packet differs by case, and that difference is asserted here:
// walk/run/run3 push X/Y/Dir before `mov dx,0x276` (0x6D9C4B / 0x6D9D67 /
// 0x6D9E01), while turn and pose push FOUR ZEROS (0x6D9B94-0x6D9B9E,
// 0x6D9C8B-0x6D9C95). 0x276 = 630 = SM_ACT_FAIL, 0x275 = 629 = SM_ACT_GOOD.

using System.Reflection;
using GameSvr;
using SystemModule;

PrepareRuntimeConfig();
InitializeRuntime();

CheckConstants();
CheckPredicateIsPlayerOnly();
CheckCanActPredicateMatrix();
CheckTurnAndPoseStateMatrix();
CheckTurnAndPoseHaveNoInventedInterval();
CheckTurnLandingEventPrecedesBroadcast();
CheckMapScriptEventInvokesNpcLabel();
CheckTurnEventTraversalObservesLiveChain();
CheckNonPlayerStillProcessesLandingEvent();
CheckTurnGateSuppressesBroadcastWithoutControllingSuccess();
CheckTurnClosedDoorSuppressesBroadcast();
CheckTurnAdjacentClosedDoorDoesNotBlockGate();
CheckTurnRejectedGateStillBroadcastsAfterEvents();
CheckTurnSameDirectionAndWrongCoordinatesFail();
CheckTurnDropToMapBranch();
CheckSpellCanActRoutes();
CheckHitSuccessHasNoMovementActionTick();
CheckWalkRefusedWhileLocked();
CheckRunRefusedWhileLocked();
CheckRun3RefusedWhileLocked();
CheckTurnRefusedWhileLocked();
CheckPoseRefusedWhileLocked();
CheckHorseRunRefusedWhileLocked();
CheckAllMovementAllowedWhenLockClear();
CheckGateIsAheadOfIntervalBookkeeping();
CheckLockValuesThreeAndFive();

Console.WriteLine(
    "NativeCastLockMovementGateCheck PASS " +
    "cast-lock=+0x574=m_nNativeForcedMoveRemaining(3/5) " +
    "gated=3011-walk/3013-run/4108-run3/3010-turn/3012-pose(+3035-hit-arm-no-step) " +
    "refusal=630 walk/run/run3 carry x/y/dir + turn/pose/spell four-zero " +
    "player-only=TCreature-untouched gate-precedes-interval-bookkeeping " +
    "states=29/1/26/24/62 spell-exceptions=0x72+0xD3/state26 " +
    "NOMAGIC-branch clear-lock=all-movement-allowed");

static void CheckConstants()
{
    Equal(3010, Grobal2.CM_TURN, "CM_TURN");
    Equal(3011, Grobal2.CM_WALK, "CM_WALK");
    Equal(3012, Grobal2.CM_SITDOWN, "CM_SITDOWN (native case 3012 pose)");
    Equal(3013, Grobal2.CM_RUN, "CM_RUN");
    Equal(4108, Grobal2.CM_RUN3, "CM_RUN3");
    Equal(3035, Grobal2.CM_HORSERUN, "CM_HORSERUN");
    // 0x275 / 0x276 at 0x6D9C4B, 0x6D9C69 and siblings.
    Equal(0x275, Grobal2.SM_ACT_GOOD, "SM_ACT_GOOD = 0x275");
    Equal(0x276, Grobal2.SM_ACT_FAIL, "SM_ACT_FAIL = 0x276");
}

// The extra term is a TPlayer override. Monsters keep sub_76B354 unchanged, so
// the predicate must not exist on the shared base type.
static void CheckPredicateIsPlayerOnly()
{
    const string name = "IsNativeCanActBlockedByForcedMove";
    var onPlayer = typeof(TPlayObject).GetMethod(name,
        BindingFlags.Instance | BindingFlags.NonPublic |
        BindingFlags.Public | BindingFlags.DeclaredOnly);
    Assert(onPlayer != null,
        "cast-lock predicate must be declared on TPlayObject (VMT+0x40 override)");

    var onBase = typeof(TBaseObject).GetMethod(name,
        BindingFlags.Instance | BindingFlags.NonPublic |
        BindingFlags.Public | BindingFlags.DeclaredOnly);
    Assert(onBase == null,
        "cast-lock predicate must NOT exist on TBaseObject: native adds the " +
        "+0x574 term only in the TPlayer override, so monsters are never " +
        "blocked mid-cast");

    // The predicate must actually read the counter, in the polarity native
    // uses: non-zero blocks (0x6E671F), zero allows (0x6E6723).
    var free = NewPlayer("predicate-free");
    free.m_nNativeForcedMoveRemaining = 0;
    Assert(!Blocked(free), "lock 0 must allow (0x6E671D je -> TRUE)");
    foreach (var value in new[] { 1, 3, 5 })
    {
        var held = NewPlayer("predicate-" + value);
        held.m_nNativeForcedMoveRemaining = value;
        Assert(Blocked(held), $"lock {value} must block (0x6E671F xor eax,eax)");
    }
}

static void CheckCanActPredicateMatrix()
{
    var clear = NewPlayer("can-act-clear");
    Assert(!clear.IsNativeCanActBlocked(0), "clear arg0 allowed");
    Assert(!clear.IsNativeCanActBlocked(1), "clear arg1 allowed");

    foreach (var state in new[] { 29, 1, 26, 62 })
    {
        var player = NewPlayer("can-act-state-" + state);
        Assert(player.SetNativeActiveState(state),
            $"state {state} setup");
        Assert(player.IsNativeCanActBlocked(0),
            $"state {state} blocks arg0");
        Assert(player.IsNativeCanActBlocked(1),
            $"state {state} blocks arg1");
    }

    var state24 = NewPlayer("can-act-state-24");
    Assert(state24.SetNativeActiveState(24), "state 24 setup");
    Assert(!state24.IsNativeCanActBlocked(0),
        "state 24 must not block arg0");
    Assert(state24.IsNativeCanActBlocked(1),
        "state 24 must block arg1");

    var dead = NewPlayer("can-act-dead");
    dead.m_boDeath = true;
    Assert(dead.IsNativeCanActBlocked(0), "death blocks arg0");
    Assert(dead.IsNativeCanActBlocked(1), "death blocks arg1");

    foreach (var count in new[] { 1, 3, 5 })
    {
        var player = NewPlayer("can-act-forced-" + count);
        player.m_nNativeForcedMoveRemaining = count;
        Assert(player.IsNativeCanActBlocked(0),
            $"forced move {count} blocks arg0");
        Assert(player.IsNativeCanActBlocked(1),
            $"forced move {count} blocks arg1");
    }
}

static void CheckTurnAndPoseStateMatrix()
{
    foreach (var state in new[] { 29, 1, 26, 62 })
    {
        var turn = FreePlayer("turn-state-" + state, 5, 5,
            Grobal2.DR_LEFT);
        Assert(turn.SetNativeActiveState(state),
            $"turn state {state} setup");
        Assert(turn.Operate(Message(Grobal2.CM_TURN, 5, 5,
            Grobal2.DR_UP)), $"turn state {state} dispatch");
        Equal((byte)Grobal2.DR_LEFT, turn.m_btDirection,
            $"turn state {state} refused");
        Equal(0, CountMessages(turn, Grobal2.RM_TURN),
            $"turn state {state} no broadcast");

        var pose = FreePlayer("pose-state-" + state, 5, 5,
            Grobal2.DR_LEFT);
        Assert(pose.SetNativeActiveState(state),
            $"pose state {state} setup");
        Assert(pose.Operate(Message(Grobal2.CM_SITDOWN, 5, 5,
            Grobal2.DR_UP)), $"pose state {state} dispatch");
        Equal(0, CountMessages(pose, Grobal2.RM_SPELL2),
            $"pose state {state} no broadcast");
    }

    var turnState24 = FreePlayer("turn-state-24", 5, 5,
        Grobal2.DR_LEFT);
    Assert(turnState24.SetNativeActiveState(24), "turn state 24 setup");
    Assert(turnState24.Operate(Message(Grobal2.CM_TURN, 5, 5,
        Grobal2.DR_UP)), "turn state 24 dispatch");
    Equal((byte)Grobal2.DR_UP, turnState24.m_btDirection,
        "turn state 24 allowed with arg0");

    var poseState24 = FreePlayer("pose-state-24", 5, 5,
        Grobal2.DR_LEFT);
    Assert(poseState24.SetNativeActiveState(24), "pose state 24 setup");
    Assert(poseState24.Operate(Message(Grobal2.CM_SITDOWN, 5, 5,
        Grobal2.DR_UP)), "pose state 24 dispatch");
    Equal(0, CountMessages(poseState24, Grobal2.RM_SPELL2),
        "pose state 24 blocked with arg1");
}

static void CheckSpellCanActRoutes()
{
    const int actionTickSentinel = unchecked((int)0x23456789);
    var blockers = new (string Name, Action<ProbePlayer> Apply)[]
    {
        ("state29", player => player.SetNativeActiveState(29)),
        ("state1", player => player.SetNativeActiveState(1)),
        ("state26", player => player.SetNativeActiveState(26)),
        ("state24", player => player.SetNativeActiveState(24)),
        ("state62", player => player.SetNativeActiveState(62)),
        ("death", player => player.m_boDeath = true),
        ("forced", player => player.m_nNativeForcedMoveRemaining = 3)
    };

    foreach (var blocker in blockers)
    {
        var ordinary = SpellPlayer("ordinary-" + blocker.Name,
            0x71, false);
        blocker.Apply(ordinary);
        Cast(ordinary, 0x71);
        AssertNativeSpellRefusal(ordinary,
            $"ordinary spell blocked by {blocker.Name}");

        var allowed72 = SpellPlayer("allowed72-" + blocker.Name,
            0x72, true);
        blocker.Apply(allowed72);
        allowed72.m_dwActionTick = actionTickSentinel;
        allowed72.m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_ACT_FAIL,
            1, 2, 3, 4);
        Cast(allowed72, 0x72);
        Equal(1, CountMessages(allowed72, Grobal2.RM_SPELL),
            $"0x72 bypasses {blocker.Name} and NOMAGIC");
        Packet(allowed72.m_DefMsg, Grobal2.SM_ACT_GOOD, 0, 0, 0, 0,
            $"0x72 success ACK for {blocker.Name}");
        Equal(actionTickSentinel, allowed72.m_dwActionTick,
            $"0x72 success must not write movement action tick for {blocker.Name}");
    }

    var allowedD3 = SpellPlayer("allowed-d3", 0xD3, true);
    Assert(allowedD3.SetNativeActiveState(26), "0xD3 state26 setup");
    allowedD3.m_dwActionTick = actionTickSentinel;
    allowedD3.m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_ACT_FAIL,
        1, 2, 3, 4);
    Cast(allowedD3, 0xD3);
    Equal(1, CountMessages(allowedD3, Grobal2.RM_SPELL),
        "0xD3 with state26 bypasses can-act and NOMAGIC");
    Packet(allowedD3.m_DefMsg, Grobal2.SM_ACT_GOOD, 0, 0, 0, 0,
        "0xD3 state26 success ACK");
    Equal(actionTickSentinel, allowedD3.m_dwActionTick,
        "0xD3 state26 success must not write movement action tick");

    var refusedD3 = SpellPlayer("refused-d3", 0xD3, false);
    Assert(refusedD3.SetNativeActiveState(29), "0xD3 state29 setup");
    Cast(refusedD3, 0xD3);
    AssertNativeSpellRefusal(refusedD3,
        "0xD3 without state26 remains blocked");

    var clearNoMagic = SpellPlayer("clear-nomagic", 0x72, true);
    Cast(clearNoMagic, 0x72);
    AssertNativeSpellRefusal(clearNoMagic,
        "clear caster 0x72 remains subject to NOMAGIC");

    var spellDisabled = SpellPlayer("disabled-allowed72", 0x72, true);
    Assert(spellDisabled.SetNativeActiveState(29),
        "disabled 0x72 state29 setup");
    spellDisabled.m_boCanSpell = false;
    Cast(spellDisabled, 0x72);
    AssertNativeSpellRefusal(spellDisabled,
        "0x72 bypass does not bypass m_boCanSpell");

    var cellForbidden = SpellPlayer("cell-forbidden-allowed72", 0x72, true);
    Assert(cellForbidden.SetNativeActiveState(29),
        "cell-forbidden 0x72 state29 setup");
    cellForbidden.m_PEnvir.SetMapCellSkillFlag(5, 5, 5, 5, 1);
    Cast(cellForbidden, 0x72);
    AssertNativeSpellRefusal(cellForbidden,
        "0x72 bypass does not bypass the per-cell skill gate");

    var idForbidden = SpellPlayer("id-forbidden-allowed72", 0x72, true);
    Assert(idForbidden.SetNativeActiveState(29),
        "id-forbidden 0x72 state29 setup");
    idForbidden.m_PEnvir.LimitSkillIds.Add(0x72);
    Cast(idForbidden, 0x72);
    AssertNativeSpellRefusal(idForbidden,
        "0x72 bypass does not bypass the per-map skill-id gate");

    var lowWord = NewPlayer("spell-low-word");
    Assert(lowWord.SetNativeActiveState(29), "low-word setup");
    Assert(lowWord.CanNativeSpellBypassCanActGate(0x1_0072),
        "sub_7725FC compares the low 16-bit magic id");
}

static void CheckHitSuccessHasNoMovementActionTick()
{
    const int actionTickSentinel = unchecked((int)0x3456789A);
    var player = FreePlayer("hit-no-movement-tick", 5, 5,
        Grobal2.DR_UP);
    player.m_boCanHit = true;
    player.m_dwActionTick = actionTickSentinel;
    player.m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_ACT_FAIL,
        1, 2, 3, 4);

    var hit = Message(Grobal2.CM_TWINHIT, 5, 5, Grobal2.DR_RIGHT);
    hit.boLateDelivery = true;
    Assert(player.Operate(hit), "3028 hit success dispatch");
    Packet(player.m_DefMsg, Grobal2.SM_ACT_GOOD, 0, 0, 0, 0,
        "3028 hit success replaces a stale packet with four-zero 0x275");
    Equal(actionTickSentinel, player.m_dwActionTick,
        "hit success must not write movement action tick");
}

static void CheckTurnLandingEventPrecedesBroadcast()
{
    const int actionTickSentinel = unchecked((int)0x456789AB);
    var map = NewMap();
    var player = Place(map, NewPlayer("turn-event-order"), 5, 5);
    player.m_btDirection = Grobal2.DR_LEFT;
    player.m_nNativeForcedMoveRemaining = 0;
    player.m_dwActionTick = actionTickSentinel;
    player.m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_ACT_FAIL,
        1, 2, 3, 4);

    var plainEvent = new ProbePlainTurnEvent(map, 5, 5);
    var landingEvent = new ProbeTurnLandingEvent(map, 5, 5);
    var beforeTurns = CountMessages(player, Grobal2.RM_TURN);

    Assert(player.Operate(Message(Grobal2.CM_TURN, 5, 5,
        Grobal2.DR_UP)), "turn event-order dispatch");

    Equal(0, plainEvent.ApplyCount,
        "plain TMapEvent must not receive the +0x34 landing callback");
    Equal(1, landingEvent.ApplyCount,
        "native +0x34 landing event applied once");
    Assert(ReferenceEquals(player, landingEvent.AppliedTarget),
        "turn landing event target");
    Equal((byte)Grobal2.DR_UP, landingEvent.DirectionAtApply,
        "direction committed before landing event");
    Equal(beforeTurns, landingEvent.TurnMessagesAtApply,
        "landing event must run before RM_TURN broadcast");
    Equal(beforeTurns + 1, CountMessages(player, Grobal2.RM_TURN),
        "event-only turn broadcasts exactly once after landing");
    Equal(actionTickSentinel, player.m_dwActionTick,
        "event turn must not write movement action tick");
    Packet(player.m_DefMsg, Grobal2.SM_ACT_GOOD, 0, 0, 0, 0,
        "event turn replaces stale packet with four-zero 0x275");
}

static void CheckMapScriptEventInvokesNpcLabel()
{
    var map = NewMap();
    var player = Place(map, NewPlayer("turn-map-script-event"), 5, 5);
    player.m_btDirection = Grobal2.DR_LEFT;
    player.m_nNativeForcedMoveRemaining = 0;
    var scriptNpc = new ProbeScriptNpc();
    _ = new MapScriptEvt(scriptNpc, map, 5, 5, 60_000, true);

    Assert(player.Operate(Message(Grobal2.CM_TURN, 5, 5,
        Grobal2.DR_UP)), "map-script-event turn dispatch");

    Equal(1, scriptNpc.CallCount,
        "MapScriptEvt invokes NPC VMT+0x44 once");
    Assert(ReferenceEquals(player, scriptNpc.LastPlayer),
        "MapScriptEvt passes the landing player");
    Equal(MapScriptEvt.ScriptLabel, scriptNpc.LastLabel,
        "MapScriptEvt exact label");
    Assert(!scriptNpc.LastExtendedJump,
        "MapScriptEvt passes the native false jump mode");
}

static void CheckTurnEventTraversalObservesLiveChain()
{
    var map = NewMap();
    var player = Place(map, NewPlayer("turn-live-event-chain"), 5, 5);
    player.m_btDirection = Grobal2.DR_LEFT;
    player.m_nNativeForcedMoveRemaining = 0;

    var removedBeforeVisit = new ProbeTurnLandingEvent(map, 5, 5);
    var remover = new ProbeRemovingTurnEvent(map, 5, 5,
        removedBeforeVisit);

    Assert(player.Operate(Message(Grobal2.CM_TURN, 5, 5,
        Grobal2.DR_UP)), "live-event-chain turn dispatch");

    Equal(1, remover.ApplyCount,
        "head landing event runs once");
    Equal(0, removedBeforeVisit.ApplyCount,
        "event removed by the current callback is not visited later");
}

static void CheckNonPlayerStillProcessesLandingEvent()
{
    var map = NewMap();
    var actor = new ProbeNonPlayer
    {
        m_PEnvir = map,
        m_nCurrX = 5,
        m_nCurrY = 5,
        m_btRaceServer = Grobal2.RC_ANIMAL,
        m_sCharName = "turn-non-player-event"
    };
    Assert(ReferenceEquals(actor, map.AddToMap(5, 5,
        CellType.OS_MOVINGOBJECT, actor)),
        "non-player fixture placement");
    var landingEvent = new ProbeTurnLandingEvent(map, 5, 5);

    Assert(!actor.ProcessLanding(),
        "non-player returns before gate handling");
    Equal(1, landingEvent.ApplyCount,
        "non-player still receives the first event pass");
    Assert(ReferenceEquals(actor, landingEvent.AppliedTarget),
        "non-player event target");
}

static void CheckTurnGateSuppressesBroadcastWithoutControllingSuccess()
{
    const int actionTickSentinel = unchecked((int)0x56789ABC);
    var source = NewMap();
    source.sMapName = "TURN-GATE-SOURCE";
    source.m_sMapFileName = source.sMapName;
    source.nServerIndex = M2Share.nServerIndex;

    var target = NewMap();
    target.sMapName = "TURN-GATE-TARGET";
    target.m_sMapFileName = target.sMapName;
    target.nServerIndex = M2Share.nServerIndex;
    target.SetMapXYFlag(8, 8, false);

    var targetFound = false;
    var targetCell = target.GetMapCellInfo(8, 8, ref targetFound);
    Assert(targetFound && !targetCell.Valid,
        "gate target must exist but reject AddToMap");

    var player = Place(source, NewPlayer("turn-gate-failed-land"), 5, 5);
    player.m_sMapName = source.sMapName;
    player.m_sMapFileName = source.m_sMapFileName;
    player.m_btDirection = Grobal2.DR_LEFT;
    player.m_nNativeForcedMoveRemaining = 0;
    player.m_dwActionTick = actionTickSentinel;
    player.m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_ACT_FAIL,
        1, 2, 3, 4);

    var gate = new TGateObj
    {
        DEnvir = target,
        nDMapX = 8,
        nDMapY = 8
    };
    Assert(ReferenceEquals(gate, source.AddToMap(5, 5,
        CellType.OS_GATEOBJECT, gate)), "turn gate placement");
    Assert(source.ArroundDoorOpened(5, 5),
        "turn gate door admission");
    Assert(!target.Flag.boNEEDHOLE, "turn gate hole admission");

    var beforeTurns = CountMessages(player, Grobal2.RM_TURN);
    Assert(player.Operate(Message(Grobal2.CM_TURN, 5, 5,
        Grobal2.DR_UP)), "turn gate dispatch");

    Equal((byte)Grobal2.DR_UP, player.m_btDirection,
        "accepted gate keeps changed direction");
    Assert(ReferenceEquals(source, player.m_PEnvir),
        "failed landing rolls back to source");
    Position(player, 5, 5, "failed landing rollback");
    Equal(beforeTurns, CountMessages(player, Grobal2.RM_TURN),
        "accepted gate suppresses source turn broadcast");
    Equal(actionTickSentinel, player.m_dwActionTick,
        "gate turn must not write movement action tick");
    Packet(player.m_DefMsg, Grobal2.SM_ACT_GOOD, 0, 0, 0, 0,
        "accepted gate replaces stale packet with four-zero 0x275");
}

static void CheckTurnClosedDoorSuppressesBroadcast()
{
    var source = NewMap();
    var target = NewMap();
    var player = Place(source, NewPlayer("turn-gate-closed-door"), 5, 5);
    player.m_btDirection = Grobal2.DR_LEFT;
    player.m_nNativeForcedMoveRemaining = 0;

    var gate = new TGateObj
    {
        DEnvir = target,
        nDMapX = 8,
        nDMapY = 8
    };
    Assert(ReferenceEquals(gate, source.AddToMap(5, 5,
        CellType.OS_GATEOBJECT, gate)), "closed-door gate placement");
    source.m_DoorList.Add(new TDoorInfo
    {
        nX = 5,
        nY = 5,
        Status = new TDoorStatus { boOpened = false }
    });
    Assert(!source.ArroundDoorOpened(5, 5),
        "closed-door fixture");

    var beforeTurns = CountMessages(player, Grobal2.RM_TURN);
    Assert(player.Operate(Message(Grobal2.CM_TURN, 5, 5,
        Grobal2.DR_UP)), "closed-door turn dispatch");

    Equal((byte)Grobal2.DR_UP, player.m_btDirection,
        "closed-door turn still commits direction");
    Assert(ReferenceEquals(source, player.m_PEnvir),
        "closed door does not transfer the player");
    Equal(beforeTurns, CountMessages(player, Grobal2.RM_TURN),
        "gate found plus closed door suppresses RM_TURN");
    Packet(player.m_DefMsg, Grobal2.SM_ACT_GOOD, 0, 0, 0, 0,
        "closed-door turn remains four-zero 0x275 success");
}

static void CheckTurnAdjacentClosedDoorDoesNotBlockGate()
{
    var source = NewMap();
    source.nServerIndex = M2Share.nServerIndex;
    var target = NewMap();
    target.nServerIndex = M2Share.nServerIndex;
    var player = Place(source,
        NewPlayer("turn-gate-adjacent-closed-door"), 5, 5);
    player.m_btDirection = Grobal2.DR_LEFT;
    player.m_nNativeForcedMoveRemaining = 0;

    var gate = new TGateObj
    {
        DEnvir = target,
        nDMapX = 8,
        nDMapY = 8
    };
    Assert(ReferenceEquals(gate, source.AddToMap(5, 5,
        CellType.OS_GATEOBJECT, gate)),
        "adjacent-door gate placement");
    source.m_DoorList.Add(new TDoorInfo
    {
        nX = 6,
        nY = 5,
        Status = new TDoorStatus { boOpened = false }
    });
    Assert(!source.ArroundDoorOpened(5, 5),
        "legacy adjacent-door scan sees the fixture");
    Assert(source.GetDoor(5, 5) == null,
        "native current cell has no door");

    var beforeTurns = CountMessages(player, Grobal2.RM_TURN);
    Assert(player.Operate(Message(Grobal2.CM_TURN, 5, 5,
        Grobal2.DR_UP)), "adjacent-door turn dispatch");

    Assert(ReferenceEquals(target, player.m_PEnvir),
        "adjacent closed door must not block the current-cell gate");
    Position(player, 8, 8, "adjacent-door gate landing");
    Equal(beforeTurns + 1, CountMessages(player, Grobal2.RM_TURN),
        "accepted adjacent-door gate emits only target-map appearance RM_TURN");
    Packet(player.m_DefMsg, Grobal2.SM_ACT_GOOD, 0, 0, 0, 0,
        "adjacent-door turn remains four-zero 0x275 success");
}

static void CheckTurnRejectedGateStillBroadcastsAfterEvents()
{
    var source = NewMap();
    var target = NewMap();
    target.Flag.boNEEDHOLE = true;
    var player = Place(source, NewPlayer("turn-gate-needhole"), 5, 5);
    player.m_btDirection = Grobal2.DR_LEFT;
    player.m_nNativeForcedMoveRemaining = 0;

    var landingEvent = new ProbeTurnLandingEvent(source, 5, 5);
    var gate = new TGateObj
    {
        DEnvir = target,
        nDMapX = 8,
        nDMapY = 8
    };
    // Gate is inserted after the event and is therefore the managed list head.
    Assert(ReferenceEquals(gate, source.AddToMap(5, 5,
        CellType.OS_GATEOBJECT, gate)), "NEEDHOLE gate placement");

    var beforeTurns = CountMessages(player, Grobal2.RM_TURN);
    Assert(player.Operate(Message(Grobal2.CM_TURN, 5, 5,
        Grobal2.DR_UP)), "NEEDHOLE turn dispatch");

    Equal(1, landingEvent.ApplyCount,
        "event pass must precede a gate that is at the list head");
    Equal(beforeTurns, landingEvent.TurnMessagesAtApply,
        "head gate must not move RM_TURN ahead of the event pass");
    Equal(beforeTurns + 1, CountMessages(player, Grobal2.RM_TURN),
        "rejected NEEDHOLE admission restores RM_TURN broadcast");
    Assert(ReferenceEquals(source, player.m_PEnvir),
        "rejected NEEDHOLE gate does not transfer the player");
    Packet(player.m_DefMsg, Grobal2.SM_ACT_GOOD, 0, 0, 0, 0,
        "rejected NEEDHOLE gate does not fail a valid turn");

    var oldAuthOpen = M2Share.g_Config.boAuthOpen;
    try
    {
        M2Share.g_Config.boAuthOpen = true;
        var activeSource = NewMap();
        var activeTarget = NewMap();
        activeTarget.NativeMapActivePointRequired = 1;
        var activePlayer = Place(activeSource,
            NewPlayer("turn-gate-active-point"), 5, 5);
        activePlayer.m_btDirection = Grobal2.DR_LEFT;
        activePlayer.m_nNativeForcedMoveRemaining = 0;
        var activeGate = new TGateObj
        {
            DEnvir = activeTarget,
            nDMapX = 8,
            nDMapY = 8
        };
        Assert(ReferenceEquals(activeGate, activeSource.AddToMap(5, 5,
            CellType.OS_GATEOBJECT, activeGate)),
            "active-point gate placement");

        var activeBeforeTurns = CountMessages(activePlayer,
            Grobal2.RM_TURN);
        Assert(activePlayer.Operate(Message(Grobal2.CM_TURN, 5, 5,
            Grobal2.DR_UP)), "active-point turn dispatch");
        Equal(activeBeforeTurns + 1,
            CountMessages(activePlayer, Grobal2.RM_TURN),
            "rejected active-point admission restores RM_TURN broadcast");
        Assert(ReferenceEquals(activeSource, activePlayer.m_PEnvir),
            "active-point rejection does not transfer the player");
        Packet(activePlayer.m_DefMsg, Grobal2.SM_ACT_GOOD, 0, 0, 0, 0,
            "active-point rejection does not fail a valid turn");
    }
    finally
    {
        M2Share.g_Config.boAuthOpen = oldAuthOpen;
    }
}

static void CheckTurnSameDirectionAndWrongCoordinatesFail()
{
    var sameDirectionMap = NewMap();
    var sameDirectionPlayer = Place(sameDirectionMap,
        NewPlayer("turn-same-direction"), 5, 5);
    sameDirectionPlayer.m_btDirection = Grobal2.DR_LEFT;
    sameDirectionPlayer.m_nNativeForcedMoveRemaining = 0;
    var sameDirectionEvent = new ProbeTurnLandingEvent(
        sameDirectionMap, 5, 5);

    Assert(sameDirectionPlayer.Operate(Message(Grobal2.CM_TURN, 5, 5,
        Grobal2.DR_LEFT)), "same-direction turn dispatch");
    Equal(0, sameDirectionEvent.ApplyCount,
        "same-direction refusal must not process landing events");
    Equal(0, CountMessages(sameDirectionPlayer, Grobal2.RM_TURN),
        "same-direction refusal has no RM_TURN");
    Packet(sameDirectionPlayer.m_DefMsg, Grobal2.SM_ACT_FAIL,
        0, 0, 0, 0, "same-direction refusal is four-zero 0x276");

    var wrongCoordinatesMap = NewMap();
    var wrongCoordinatesPlayer = Place(wrongCoordinatesMap,
        NewPlayer("turn-wrong-coordinates"), 5, 5);
    wrongCoordinatesPlayer.m_btDirection = Grobal2.DR_LEFT;
    wrongCoordinatesPlayer.m_nNativeForcedMoveRemaining = 0;
    var wrongCoordinatesEvent = new ProbeTurnLandingEvent(
        wrongCoordinatesMap, 5, 5);

    Assert(wrongCoordinatesPlayer.Operate(Message(Grobal2.CM_TURN, 6, 5,
        Grobal2.DR_UP)), "wrong-coordinate turn dispatch");
    Equal((byte)Grobal2.DR_LEFT, wrongCoordinatesPlayer.m_btDirection,
        "wrong-coordinate refusal keeps direction");
    Equal(0, wrongCoordinatesEvent.ApplyCount,
        "wrong-coordinate refusal must not process landing events");
    Equal(0, CountMessages(wrongCoordinatesPlayer, Grobal2.RM_TURN),
        "wrong-coordinate refusal has no RM_TURN");
    Packet(wrongCoordinatesPlayer.m_DefMsg, Grobal2.SM_ACT_FAIL,
        0, 0, 0, 0, "wrong-coordinate refusal is four-zero 0x276");

    var wrongYMap = NewMap();
    var wrongYPlayer = Place(wrongYMap,
        NewPlayer("turn-wrong-y"), 5, 5);
    wrongYPlayer.m_btDirection = Grobal2.DR_LEFT;
    wrongYPlayer.m_nNativeForcedMoveRemaining = 0;
    var wrongYEvent = new ProbeTurnLandingEvent(wrongYMap, 5, 5);

    Assert(wrongYPlayer.Operate(Message(Grobal2.CM_TURN, 5, 6,
        Grobal2.DR_UP)), "wrong-Y turn dispatch");
    Equal((byte)Grobal2.DR_LEFT, wrongYPlayer.m_btDirection,
        "wrong-Y refusal keeps direction");
    Equal(0, wrongYEvent.ApplyCount,
        "wrong-Y refusal must not process landing events");
    Equal(0, CountMessages(wrongYPlayer, Grobal2.RM_TURN),
        "wrong-Y refusal has no RM_TURN");
    Packet(wrongYPlayer.m_DefMsg, Grobal2.SM_ACT_FAIL,
        0, 0, 0, 0, "wrong-Y refusal is four-zero 0x276");
}

static void CheckTurnDropToMapBranch()
{
    var source = NewMap();
    source.sMapName = "TURN-DROP-SOURCE";
    source.m_sMapFileName = source.sMapName;
    source.Flag.boDROPTOMAP = true;

    var target = NewMap(13, 9);
    target.sMapName = "TURN-DROP-TARGET";
    target.m_sMapFileName = target.sMapName;
    target.nServerIndex = M2Share.nServerIndex + 1;
    RegisterMap(target);
    source.Flag.sDropToMap = target.sMapName;

    var player = Place(source, NewPlayer("turn-drop-to-map"), 5, 5);
    player.m_sMapName = source.sMapName;
    player.m_sMapFileName = source.m_sMapFileName;
    player.m_btDirection = Grobal2.DR_LEFT;
    player.m_nNativeForcedMoveRemaining = 0;
    player.m_nServerIndex = M2Share.nServerIndex;
    _ = new Event(source, 5, 5, Grobal2.ET_DIGOUTZOMBI,
        60_000, true);

    var oldRandom = M2Share.RandomNumber;
    var probeRandom = new ProbeBoundedRandom(4, 7);
    try
    {
        M2Share.RandomNumber = probeRandom;
        Assert(player.Operate(Message(Grobal2.CM_TURN, 5, 5,
            Grobal2.DR_UP)), "DROPTOMAP turn dispatch");

        Assert(ReferenceEquals(target, player.m_PEnvir),
            "DROPTOMAP type-1 event moves to configured map");
        Assert(probeRandom.Bounds.Count >= 2,
            "DROPTOMAP performed both coordinate draws");
        Equal((int)target.wHeight, probeRandom.Bounds[0],
            "sub_768C7C draws Y from height first");
        Equal((int)target.wWidth, probeRandom.Bounds[1],
            "sub_768C7C draws X from width second");
        Position(player, 7, 4,
            "DROPTOMAP preserves distinct X/Y draw order");
        Assert(!player.m_boSwitchData && !player.m_boReconnection
               && !player.m_boEmergencyClose,
            "DROPTOMAP VMT+0x1C0 must not start cross-server transfer");
        Equal(string.Empty, player.m_sSwitchMapName,
            "DROPTOMAP leaves switch-map name empty");
        Equal(M2Share.nServerIndex, player.m_nServerIndex,
            "DROPTOMAP leaves player server index unchanged");
        Equal(1, CountMessages(player, Grobal2.RM_TURN),
            "DROPTOMAP returns zero and broadcasts turn on the target map");
        Packet(player.m_DefMsg, Grobal2.SM_ACT_GOOD, 0, 0, 0, 0,
            "DROPTOMAP turn remains four-zero 0x275 success");
    }
    finally
    {
        M2Share.RandomNumber = oldRandom;
    }
}

static void CheckTurnAndPoseHaveNoInventedInterval()
{
    const int actionTickSentinel = unchecked((int)0x12345678);
    bool oldSpeedHackCheck = M2Share.g_Config.boSpeedHackCheck;
    int oldTurnInterval = M2Share.g_Config.dwTurnIntervalTime;
    try
    {
        M2Share.g_Config.boSpeedHackCheck = false;
        M2Share.g_Config.dwTurnIntervalTime = int.MaxValue;

        var turn = FreePlayer("turn-no-interval", 5, 5, Grobal2.DR_LEFT);
        turn.m_dwTurnTick = HUtil32.GetTickCount();
        turn.m_dwActionTick = actionTickSentinel;
        turn.m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_ACT_FAIL,
            1, 2, 3, 4);
        Assert(turn.Operate(Message(Grobal2.CM_TURN, 5, 5,
            Grobal2.DR_UP)), "turn no-interval dispatch");
        Equal((byte)Grobal2.DR_UP, turn.m_btDirection,
            "turn has no invented interval after native can-act");
        Equal(actionTickSentinel, turn.m_dwActionTick,
            "turn success must not write the shared action tick");
        Packet(turn.m_DefMsg, Grobal2.SM_ACT_GOOD, 0, 0, 0, 0,
            "turn success replaces a stale packet with four-zero 0x275");

        var pose = FreePlayer("pose-no-interval", 5, 5, Grobal2.DR_LEFT);
        pose.m_dwTurnTick = HUtil32.GetTickCount();
        pose.m_dwActionTick = actionTickSentinel;
        pose.m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_ACT_FAIL,
            1, 2, 3, 4);
        Assert(pose.Operate(Message(Grobal2.CM_SITDOWN, 5, 5,
            Grobal2.DR_UP)), "pose no-interval dispatch");
        Equal(1, CountMessages(pose, Grobal2.RM_SPELL2),
            "pose has no invented interval after native can-act");
        Equal(actionTickSentinel, pose.m_dwActionTick,
            "pose success must not write the shared action tick");
        Packet(pose.m_DefMsg, Grobal2.SM_ACT_GOOD, 0, 0, 0, 0,
            "pose success replaces a stale packet with four-zero 0x275");
    }
    finally
    {
        M2Share.g_Config.boSpeedHackCheck = oldSpeedHackCheck;
        M2Share.g_Config.dwTurnIntervalTime = oldTurnInterval;
    }
}

static ProbePlayer SpellPlayer(string name, int magicId, bool noMagic)
{
    var map = NewMap();
    map.Flag = new TMapFlag { boNOMAGIC = noMagic };
    var player = Place(map, NewPlayer(name), 5, 5);
    player.m_boCanSpell = true;
    player.m_nSoftVersionDateEx = 1;
    player.m_dwClientTick = 1;
    player.m_Abil.Level = 50;
    player.m_WAbil.MP = 1000;
    player.m_WAbil.MaxMP = 1000;
    player.m_MagicList.Add(new TUserMagic
    {
        wMagIdx = unchecked((ushort)magicId),
        btLevel = 3,
        MagicInfo = new TMagic
        {
            wMagicID = unchecked((ushort)magicId),
            sMagicName = "state50-" + magicId,
            btTrainLv = 3,
            btEffect = 1,
            btEffectType = 0
        }
    });
    return player;
}

static void Cast(ProbePlayer player, int magicId)
{
    Assert(player.Operate(new TProcessMessage
    {
        wIdent = Grobal2.CM_SPELL,
        wParam = magicId,
        nParam1 = player.m_nCurrX,
        nParam2 = player.m_nCurrY
    }), $"spell {magicId:X} dispatch");
}

static void AssertNativeSpellRefusal(ProbePlayer player, string label)
{
    Equal(0, CountMessages(player, Grobal2.RM_SPELL),
        label + " no spell broadcast");
    Equal(0, CountMessages(player, Grobal2.RM_MOVEFAIL),
        label + " no extra RM_MOVEFAIL broadcast");
    Packet(player.m_DefMsg, Grobal2.SM_ACT_FAIL, 0, 0, 0, 0,
        label + " single four-zero 0x276");
}

static void CheckWalkRefusedWhileLocked()
{
    var player = LockedPlayer("walk-locked", 5, 5, Grobal2.DR_LEFT);
    Assert(player.Operate(Message(Grobal2.CM_WALK, 5, 4, 0)),
        "3011 dispatch while locked");
    // Native 0x6D9C26-0x6D9C4B: X, Y, Dir then dx=0x276.
    Packet(player.m_DefMsg, Grobal2.SM_ACT_FAIL, 0, 5, 5, Grobal2.DR_LEFT,
        "3011 refusal carries x/y/dir");
    Position(player, 5, 5, "3011 refused: no step");
    Equal(0, CountMessages(player, Grobal2.RM_WALK), "3011 refused: no broadcast");
}

static void CheckRunRefusedWhileLocked()
{
    var player = LockedPlayer("run-locked", 5, 5, Grobal2.DR_LEFT);
    Assert(player.Operate(Message(Grobal2.CM_RUN, 5, 3, 0)),
        "3013 dispatch while locked");
    // Native 0x6D9D42-0x6D9D67.
    Packet(player.m_DefMsg, Grobal2.SM_ACT_FAIL, 0, 5, 5, Grobal2.DR_LEFT,
        "3013 refusal carries x/y/dir");
    Position(player, 5, 5, "3013 refused: no step");
    Equal(0, CountMessages(player, Grobal2.RM_RUN), "3013 refused: no broadcast");
}

static void CheckRun3RefusedWhileLocked()
{
    var player = LockedPlayer("run3-locked", 5, 5, Grobal2.DR_LEFT);
    Assert(player.SetNativeActiveState(51), "run3 mount state");
    Assert(player.Operate(Message(Grobal2.CM_RUN3, 8, 5, Grobal2.DR_UP)),
        "4108 dispatch while locked");
    // Native 0x6D9DDC-0x6D9E01.
    Packet(player.m_DefMsg, Grobal2.SM_ACT_FAIL, 0, 5, 5, Grobal2.DR_LEFT,
        "4108 refusal carries x/y/dir");
    Position(player, 5, 5, "4108 refused: no step");
    Equal(0, CountMessages(player, Grobal2.RM_RUN3), "4108 refused: no broadcast");
}

static void CheckTurnRefusedWhileLocked()
{
    var player = LockedPlayer("turn-locked", 5, 5, Grobal2.DR_LEFT);
    Assert(player.Operate(Message(Grobal2.CM_TURN, 5, 5, Grobal2.DR_UP)),
        "3010 dispatch while locked");
    // Native pushes FOUR ZEROS at 0x6D9B94 before dx=0x276 at 0x6D9B9E.
    Packet(player.m_DefMsg, Grobal2.SM_ACT_FAIL, 0, 0, 0, 0,
        "3010 refusal is one four-zero 0x276");
    Equal((byte)Grobal2.DR_LEFT, player.m_btDirection,
        "3010 refused: direction unchanged");
    Equal(0, CountMessages(player, Grobal2.RM_TURN), "3010 refused: no broadcast");
}

static void CheckPoseRefusedWhileLocked()
{
    var player = LockedPlayer("pose-locked", 5, 5, Grobal2.DR_LEFT);
    Assert(player.Operate(Message(Grobal2.CM_SITDOWN, 5, 5, Grobal2.DR_UP)),
        "3012 dispatch while locked");
    Position(player, 5, 5, "3012 refused: no position change");
    Equal(0, CountMessages(player, Grobal2.RM_SPELL2),
        "3012 refused: no pose broadcast");
    Equal(0, CountMessages(player, Grobal2.RM_MOVEFAIL),
        "3012 refused: no extra RM_MOVEFAIL broadcast");
    Packet(player.m_DefMsg, Grobal2.SM_ACT_FAIL, 0, 0, 0, 0,
        "3012 refusal is one four-zero 0x276");
}

// ID3035 订正：早前这里写「原生没有 3035 的 handler，跳表 0x6D858B 到 3017 就停了」。
// 前半句不成立 —— 3035 不走那张跳表，而是经累减链 0x6D85F0 `sub eax,0xBD4` /
// 0x6D85FB / 0x6D8604 / 0x6D860D 在 0x6D8610 `je 0x6D9EAF` 落进 HIT CASE1，
// 是攻击动作（sub_6EC078 字节表 0x6EC178[33]=0x09 → 0x6EC29C `mov cx,0x3F9`
// = 动作码 1017，worker sub_772388 是活的）。原生的骑乘跑是 CM_RUN3(4108)。
// 本用例的断言依然成立且仍有价值：3035 现在走 HIT 臂，原生对它只更新朝向
// （0x7707E3 对 1000..1033 全窗口的无条件副作用），位移量为 0 —— 所以
// 「锁定期间发 3035 不产生位移」这条不变，只是理由从「移动被闸挡住」
// 变成了「它本来就不是移动」。
static void CheckHorseRunRefusedWhileLocked()
{
    var player = LockedPlayer("horserun-locked", 5, 5, Grobal2.DR_LEFT);
    Assert(player.Operate(Message(Grobal2.CM_HORSERUN, 5, 3, 0)),
        "3035 dispatch while locked");
    Position(player, 5, 5, "3035 refused: no step");
}

// Fail-closed guard against the gate being wired but inert: with the lock
// clear, each opcode must reach its primitive and move or turn the player.
static void CheckAllMovementAllowedWhenLockClear()
{
    var walk = FreePlayer("walk-free", 5, 5, Grobal2.DR_LEFT);
    Assert(walk.Operate(Message(Grobal2.CM_WALK, 5, 4, 0)),
        "3011 dispatch with clear lock");
    Position(walk, 5, 4, "3011 allowed: stepped");
    Packet(walk.m_DefMsg, Grobal2.SM_ACT_GOOD, 0, 0, 0, 0,
        "3011 allowed: 629");

    var turn = FreePlayer("turn-free", 5, 5, Grobal2.DR_LEFT);
    Assert(turn.Operate(Message(Grobal2.CM_TURN, 5, 5, Grobal2.DR_UP)),
        "3010 dispatch with clear lock");
    Equal((byte)Grobal2.DR_UP, turn.m_btDirection, "3010 allowed: turned");

    var run3 = FreePlayer("run3-free", 5, 5, Grobal2.DR_LEFT);
    Assert(run3.SetNativeActiveState(51), "run3 free mount state");
    Assert(run3.Operate(Message(Grobal2.CM_RUN3, 8, 5, Grobal2.DR_UP)),
        "4108 dispatch with clear lock");
    Position(run3, 8, 5, "4108 allowed: ran three cells");
}

// Native tests the lock at gate 4, BEFORE the primitive at gate 5 and before
// any tick bookkeeping. A refusal must therefore not stamp the move tick or
// bump the busy counters, otherwise the lock would perturb speed control.
static void CheckGateIsAheadOfIntervalBookkeeping()
{
    var player = LockedPlayer("order-locked", 5, 5, Grobal2.DR_LEFT);
    player.m_dwMoveTick = 0;
    player.m_dwMoveCount = 0;
    player.m_nHealthTick = 100;
    player.m_nSpellTick = 50;

    Assert(player.Operate(Message(Grobal2.CM_WALK, 5, 4, 0)),
        "3011 ordering dispatch");
    Equal(0, player.m_dwMoveTick,
        "refusal must not stamp m_dwMoveTick (gate 4 precedes the primitive)");
    Equal(0, player.m_dwMoveCount,
        "refusal must not bump the busy counter");
    Equal(100, player.m_nHealthTick, "refusal must not spend health tick");
    Equal(50, player.m_nSpellTick, "refusal must not spend spell tick");
}

// The two native writes are 5 and 3, chosen by `cmp al,3` at 0x6BC99F, and
// they are counts rather than a bool. Assert both survive as blocking values
// and that the step processor's decrement-to-zero releases the gate.
static void CheckLockValuesThreeAndFive()
{
    foreach (var count in new[] { 3, 5 })
    {
        var player = FreePlayer("countdown-" + count, 5, 5, Grobal2.DR_LEFT);
        player.m_nNativeForcedMoveRemaining = count;
        Assert(Blocked(player), $"lock {count} blocks");

        // Walk down to zero the way sub_73F200 does (0x73F224 dec).
        for (var remaining = count; remaining > 0; remaining--)
        {
            Assert(Blocked(player),
                $"lock still held at {remaining} of {count}");
            player.m_nNativeForcedMoveRemaining--;
        }
        Equal(0, player.m_nNativeForcedMoveRemaining,
            $"lock {count} reaches zero");
        Assert(!Blocked(player),
            $"lock {count} released at zero, movement allowed again");

        Assert(player.Operate(Message(Grobal2.CM_WALK, 5, 4, 0)),
            $"lock {count} post-release dispatch");
        Position(player, 5, 4, $"lock {count} post-release step");
    }
}

static bool Blocked(TPlayObject player)
{
    var method = typeof(TPlayObject).GetMethod(
        "IsNativeCanActBlockedByForcedMove",
        BindingFlags.Instance | BindingFlags.NonPublic |
        BindingFlags.Public | BindingFlags.DeclaredOnly)
        ?? throw new MissingMethodException(
            "TPlayObject.IsNativeCanActBlockedByForcedMove");
    return (bool)method.Invoke(player, null);
}

static ProbePlayer LockedPlayer(string name, short x, short y, int direction)
{
    var player = FreePlayer(name, x, y, direction);
    // The value native writes at 0x6BC9A3 for skill level >= 3.
    player.m_nNativeForcedMoveRemaining = 5;
    return player;
}

static ProbePlayer FreePlayer(string name, short x, short y, int direction)
{
    var player = Place(NewMap(), NewPlayer(name), x, y);
    player.m_boCanWalk = true;
    player.m_boCanRun = true;
    player.m_btDirection = (byte)direction;
    player.m_nNativeForcedMoveRemaining = 0;
    return player;
}

static TProcessMessage Message(int ident, int x, int y, int direction) => new()
{
    wIdent = ident,
    wParam = direction,
    nParam1 = x,
    nParam2 = y
};

static int CountMessages(TBaseObject actor, int ident) =>
    actor.m_MsgList.Count(message => message.wIdent == ident);

static void Packet(ClientPacket packet, int ident, int recog, int param,
    int tag, int series, string label)
{
    Assert(packet != null, label + " packet");
    Equal(unchecked((ushort)ident), packet.Ident, label + " ident");
    Equal(recog, packet.Recog, label + " recog");
    Equal(unchecked((ushort)param), packet.Param, label + " param");
    Equal(unchecked((ushort)tag), packet.Tag, label + " tag");
    Equal(unchecked((ushort)series), packet.Series, label + " series");
}

static void Position(TBaseObject actor, int x, int y, string label)
{
    Equal((short)x, actor.m_nCurrX, label + " x");
    Equal((short)y, actor.m_nCurrY, label + " y");
}

static Envirnoment NewMap(short width = 16, short height = 16)
{
    var map = new Envirnoment();
    var initialize = typeof(Envirnoment).GetMethod("Initialize",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("Envirnoment.Initialize");
    initialize.Invoke(map, new object[] { width, height });
    map.sMapName = "STATE50";
    return map;
}

static void RegisterMap(Envirnoment map)
{
    var field = typeof(MapManager).GetField("m_MapList",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException("MapManager.m_MapList");
    var maps = (Dictionary<string, Envirnoment>)field.GetValue(
        M2Share.MapManager);
    maps[map.sMapName] = map;
}

static ProbePlayer Place(Envirnoment map, ProbePlayer player, short x, short y)
{
    player.m_PEnvir = map;
    player.m_nCurrX = x;
    player.m_nCurrY = y;
    player.m_boFixedHideMode = false;
    player.m_boObMode = false;
    player.m_boGhost = false;
    player.m_boAddToMaped = false;
    player.m_boDelFormMaped = false;
    Assert(ReferenceEquals(player, map.AddToMap(x, y,
        CellType.OS_MOVINGOBJECT, player)), "place " + player.m_sCharName);
    return player;
}

static ProbePlayer NewPlayer(string name) => new()
{
    m_boOffLineFlag = true,
    m_sCharName = name,
    m_btRaceServer = Grobal2.RC_PLAYOBJECT
};

static void InitializeRuntime()
{
    M2Share.g_Config = new GameSvrConfig { nSendRefMsgRange = 12 };
    M2Share.UserEngine = new UserEngine();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.MapManager = new MapManager();
    M2Share.CastleManager = new CastleManager();
    M2Share.MagicManager = new MagicManager();
    M2Share.RandomNumber = RandomNumber.GetInstance();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new System.Collections.ArrayList();
    M2Share.LogonCostLogList = new System.Collections.ArrayList();
    M2Share.g_MonSayMsgList = new Dictionary<string, IList<TMonSayMsg>>();
}

static void PrepareRuntimeConfig()
{
    var runtimeDirectory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(runtimeDirectory, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "String.ini"),
        "[String]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "Command.conf"),
        "[Command]" + Environment.NewLine);
    var shareDirectory = Path.Combine(Path.GetFullPath(
        Path.Combine(runtimeDirectory, "..")), "Share");
    Directory.CreateDirectory(shareDirectory);
    File.WriteAllText(Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"),
        "[Integer]" + Environment.NewLine);
}

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{label}: expected={expected}, actual={actual}");
}

static void Assert(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
}

sealed class ProbePlayer : TPlayObject
{
    internal List<(ClientPacket Packet, string Body)> SocketMessages { get; } =
        new();

    internal override void SendSocket(ClientPacket defMsg, string message)
    {
        SocketMessages.Add((defMsg, message));
    }
}

sealed class ProbePlainTurnEvent : Event
{
    internal int ApplyCount { get; private set; }

    internal ProbePlainTurnEvent(Envirnoment map, int x, int y)
        : base(map, x, y, 0x7F4F, 60_000, true)
    {
    }

    public override bool ApplyTo(TBaseObject target)
    {
        ApplyCount++;
        return false;
    }
}

sealed class ProbeTurnLandingEvent : DebuffTrapEvent
{
    internal int ApplyCount { get; private set; }
    internal int TurnMessagesAtApply { get; private set; } = -1;
    internal byte DirectionAtApply { get; private set; }
    internal TBaseObject AppliedTarget { get; private set; }

    internal ProbeTurnLandingEvent(Envirnoment map, int x, int y)
        : base(map, x, y, 60_000, null)
    {
    }

    public override bool ApplyTo(TBaseObject target)
    {
        ApplyCount++;
        AppliedTarget = target;
        DirectionAtApply = target.m_btDirection;
        TurnMessagesAtApply = target.m_MsgList.Count(
            message => message.wIdent == Grobal2.RM_TURN);
        return false;
    }
}

sealed class ProbeRemovingTurnEvent : DebuffTrapEvent
{
    private readonly Event _eventToRemove;

    internal int ApplyCount { get; private set; }

    internal ProbeRemovingTurnEvent(Envirnoment map, int x, int y,
        Event eventToRemove)
        : base(map, x, y, 60_000, null)
    {
        _eventToRemove = eventToRemove;
    }

    public override bool ApplyTo(TBaseObject target)
    {
        ApplyCount++;
        _eventToRemove.Close();
        return false;
    }
}

sealed class ProbeNonPlayer : TBaseObject
{
    internal bool ProcessLanding() =>
        ProcessNativeMoveActionWithoutBroadcast();
}

sealed class ProbeScriptNpc : NormNpc
{
    internal int CallCount { get; private set; }
    internal TPlayObject LastPlayer { get; private set; }
    internal string LastLabel { get; private set; }
    internal bool LastExtendedJump { get; private set; }

    public override void GotoLable(TPlayObject playObject, string label,
        bool extendedJump)
    {
        CallCount++;
        LastPlayer = playObject;
        LastLabel = label;
        LastExtendedJump = extendedJump;
    }
}

sealed class ProbeBoundedRandom : RandomNumber
{
    private readonly Queue<int> _values;

    internal List<int> Bounds { get; } = new();

    internal ProbeBoundedRandom(params int[] values)
    {
        _values = new Queue<int>(values);
    }

    public override int Random(int value)
    {
        Bounds.Add(value);
        if (value <= 0)
        {
            return 0;
        }
        var next = _values.Count > 0 ? _values.Dequeue() : 0;
        if (next < 0 || next >= value)
        {
            throw new InvalidOperationException(
                $"probe random value {next} outside bound {value}");
        }
        return next;
    }
}
