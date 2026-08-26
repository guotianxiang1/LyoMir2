using System.Reflection;
using GameSvr;
using SystemModule;

PrepareRuntimeFiles();
InitializeRuntime();

var walkMethod = typeof(TPlayObject).GetMethod("ClientWalkXY",
    BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new MissingMethodException("TPlayObject.ClientWalkXY");
var runMethod = typeof(TPlayObject).GetMethod("ClientRunXY",
    BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new MissingMethodException("TPlayObject.ClientRunXY");

CheckWalkIgnoresManagedInterval();
CheckRunIgnoresManagedInterval();
CheckWalkFailurePreservesMoveTick();
CheckRunFailurePreservesMoveTick();
CheckRunFallbackTickBoundary();
CheckState2DLeavesTimingUntouched();

Console.WriteLine(
    "NativeWalkRunTimingParityCheck PASS WALK/RUN immediate "
    + "state2D-before-can-act success-only-move-tick");

void CheckWalkIgnoresManagedInterval()
{
    M2Share.g_Config.boSpeedHackCheck = false;
    M2Share.g_Config.dwWalkIntervalTime = int.MaxValue;
    var player = FreePlayer("walk-interval", 5, 5);
    player.m_dwMoveTick = HUtil32.GetTickCount();

    var (moved, delay) = Walk(player, 6, 5, false);
    Assert(moved, "walk must not be rejected by managed interval");
    Equal(0, delay, "walk delay");
    Position(player, 6, 5, "walk destination");
}

void CheckRunIgnoresManagedInterval()
{
    M2Share.g_Config.boSpeedHackCheck = false;
    M2Share.g_Config.dwRunIntervalTime = int.MaxValue;
    var player = FreePlayer("run-interval", 5, 5);
    player.m_dwMoveTick = HUtil32.GetTickCount();

    var (moved, delay) = Run(player, 7, 5);
    Assert(moved, "run must not be rejected by managed interval");
    Equal(0, delay, "run delay");
    Position(player, 7, 5, "run destination");
}

void CheckWalkFailurePreservesMoveTick()
{
    M2Share.g_Config.boSpeedHackCheck = true;
    var map = NewMap("walk-failure");
    var player = Place(map, NewPlayer("walk-failure"), 5, 5);
    map.SetMapXYFlag(6, 5, false);
    player.m_dwMoveTick = 123;

    var (moved, delay) = Walk(player, 6, 5, false);
    Assert(!moved, "blocked walk result");
    Equal(0, delay, "blocked walk delay");
    Equal(123, player.m_dwMoveTick, "blocked walk move tick");
    Position(player, 5, 5, "blocked walk position");
}

void CheckRunFailurePreservesMoveTick()
{
    M2Share.g_Config.boSpeedHackCheck = true;
    var map = NewMap("run-failure");
    var player = Place(map, NewPlayer("run-failure"), 5, 5);
    map.SetMapXYFlag(6, 5, false);
    map.SetMapXYFlag(7, 5, false);
    player.m_dwMoveTick = 456;

    var (moved, delay) = Run(player, 7, 5);
    Assert(!moved, "blocked run result");
    Equal(0, delay, "blocked run delay");
    Equal(456, player.m_dwMoveTick, "blocked run move tick");
    Position(player, 5, 5, "blocked run position");
}

void CheckRunFallbackTickBoundary()
{
    M2Share.g_Config.boSpeedHackCheck = true;
    var success = FreePlayer("run-fallback-success", 5, 5);
    Assert(success.SetNativeActiveState(67), "fallback success state");
    success.m_dwMoveTick = -123;

    var successResult = Run(success, 7, 5);
    Assert(successResult.Moved, "fallback run result");
    Equal(0, successResult.Delay, "fallback run delay");
    Assert(success.m_dwMoveTick != -123, "fallback run move tick");
    Position(success, 6, 6, "fallback run destination");

    var map = NewMap("run-fallback-failure");
    var failure = Place(map, NewPlayer("run-fallback-failure"), 5, 5);
    Assert(failure.SetNativeActiveState(67), "fallback failure state");
    map.SetMapXYFlag(6, 6, false);
    failure.m_dwMoveTick = 654;

    var failureResult = Run(failure, 7, 5);
    Assert(!failureResult.Moved, "blocked fallback run result");
    Equal(0, failureResult.Delay, "blocked fallback run delay");
    Equal(654, failure.m_dwMoveTick, "blocked fallback run move tick");
    Position(failure, 5, 5, "blocked fallback run position");
}

void CheckState2DLeavesTimingUntouched()
{
    M2Share.g_Config.boSpeedHackCheck = false;
    var walk = FreePlayer("walk-state2d", 5, 5);
    walk.AddTimedAbility(13, 1, 1);
    walk.m_dwMoveTick = 789;
    var walkResult = Walk(walk, 6, 5, false);
    Assert(!walkResult.Moved, "state 0x2D walk result");
    Equal(0, walkResult.Delay, "state 0x2D walk delay");
    Equal(789, walk.m_dwMoveTick, "state 0x2D walk tick");

    var run = FreePlayer("run-state2d", 5, 5);
    run.AddTimedAbility(13, 1, 1);
    run.m_dwMoveTick = 987;
    var runResult = Run(run, 7, 5);
    Assert(!runResult.Moved, "state 0x2D run result");
    Equal(0, runResult.Delay, "state 0x2D run delay");
    Equal(987, run.m_dwMoveTick, "state 0x2D run tick");
}

(bool Moved, int Delay) Walk(TPlayObject player, int x, int y,
    bool lateDelivery)
{
    object[] args = { Grobal2.CM_WALK, x, y, lateDelivery, 0 };
    var moved = (bool)walkMethod.Invoke(player, args);
    return (moved, (int)args[4]);
}

(bool Moved, int Delay) Run(TPlayObject player, int x, int y)
{
    object[] args = { Grobal2.CM_RUN, x, y, 0, 0 };
    var moved = (bool)runMethod.Invoke(player, args);
    return (moved, (int)args[4]);
}

static ProbePlayer FreePlayer(string name, short x, short y) =>
    Place(NewMap(name), NewPlayer(name), x, y);

static ProbePlayer NewPlayer(string name) => new()
{
    m_boOffLineFlag = true,
    m_sCharName = name,
    m_btRaceServer = Grobal2.RC_PLAYOBJECT,
    m_boCanWalk = true,
    m_boCanRun = true,
    m_boClientFlag = true
};

static Envirnoment NewMap(string name, short width = 16,
    short height = 16)
{
    var map = new Envirnoment { sMapName = name };
    var initialize = typeof(Envirnoment).GetMethod("Initialize",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("Envirnoment.Initialize");
    initialize.Invoke(map, new object[] { width, height });
    return map;
}

static ProbePlayer Place(Envirnoment map, ProbePlayer player, short x,
    short y)
{
    player.m_PEnvir = map;
    player.m_nCurrX = x;
    player.m_nCurrY = y;
    player.m_boGhost = false;
    player.m_boAddToMaped = false;
    player.m_boDelFormMaped = false;
    Assert(ReferenceEquals(player, map.AddToMap(x, y,
        CellType.OS_MOVINGOBJECT, player)), "place " + player.m_sCharName);
    return player;
}

static void Position(TBaseObject actor, int x, int y, string label)
{
    Equal((short)x, actor.m_nCurrX, label + " x");
    Equal((short)y, actor.m_nCurrY, label + " y");
}

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

static void PrepareRuntimeFiles()
{
    var root = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(root, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(root, "String.ini"),
        "[String]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(root, "Command.conf"),
        "[Command]" + Environment.NewLine);
    var share = Path.Combine(Path.GetFullPath(Path.Combine(root, "..")), "Share");
    Directory.CreateDirectory(share);
    File.WriteAllText(Path.Combine(share, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(share, "ServerData.ini"),
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
}
