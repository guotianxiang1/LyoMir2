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
// This audit covers the five MOVEMENT cases. The hit/spell cases share the
// slot but are combat, not movement, and are deliberately left out of scope.
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
    "gated=3011-walk/3013-run/4108-run3/3010-turn/3012-pose(+3035-horserun-extension) " +
    "refusal=630 walk/run/run3 carry x/y/dir + turn/pose ident-only(MOVE-25 open) " +
    "player-only=TCreature-untouched gate-precedes-interval-bookkeeping " +
    "clear-lock=all-movement-allowed");

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
    // Refusal ident only. Native pushes FOUR ZEROS at 0x6D9B94 before
    // dx=0x276 at 0x6D9B9E, so the turn correction should carry no
    // coordinates — but C# routes this refusal through the shared
    // coordinate-carrying SendMoveActionFail(), which leaks X/Y/Dir. That is
    // MOVE-25, a separate open divergence about the refusal PAYLOAD, not
    // about this gate. Asserting the current leaky shape would freeze the bug
    // and asserting the native shape would fail red on an unrelated row, so
    // this audit deliberately constrains only the ident and the state.
    Assert(player.m_DefMsg != null,
        "3010 refusal must answer with a correction packet, not silence " +
        "(only the bodyState 0x34 gate is silent, and this is not it)");
    Equal(unchecked((ushort)Grobal2.SM_ACT_FAIL), player.m_DefMsg.Ident,
        "3010 refusal ident 630");
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
    Equal(0, CountMessages(player, Grobal2.RM_POWERHIT),
        "3012 refused: no pose broadcast");
}

// C#-only extension: no native 3035 movement handler exists (the dispatcher
// jumptable at 0x6D858B stops at 3017; the only native 3035 is a broadcast
// ident inside sub_6EC078 at 0x6EC29C). Gated anyway so the cast window cannot
// be walked through via the one opcode native does not have.
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

static Envirnoment NewMap()
{
    var map = new Envirnoment();
    var initialize = typeof(Envirnoment).GetMethod("Initialize",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("Envirnoment.Initialize");
    initialize.Invoke(map, new object[] { (short)16, (short)16 });
    return map;
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
