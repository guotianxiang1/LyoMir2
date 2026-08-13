using System.Collections;
using System.Reflection;
using GameSvr;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.CastleManager = new CastleManager();
M2Share.ObjectManager = new ObjectManager();
M2Share.LogSystem = new MirLog();
M2Share.ProcessMsgCriticalSection = new object();
M2Share.LogMsgCriticalSection = new object();
M2Share.LogStringList = new ArrayList();
M2Share.g_MonSayMsgList = new Dictionary<string, IList<TMonSayMsg>>();
M2Share.MapManager = new MapManager();
M2Share.nServerIndex = 0;

// MOVE-52 - both space-move arms load the internal idents as immediates, so a
// teleport that takes the default has to queue them too, not the legacy 8097/8098:
//   006BD3AA  66 B9 85 27  mov cx, 0x2785   ; 10117 -> 006BD3B2 call 0x765E68
//   006BD3D3  66 B9 86 27  mov cx, 0x2786   ; 10118 -> 006BD3DB call 0x765F6C
// and the cross-map arm repeats them at 0x6BD51B / 0x6BD544.
Equal(10117, Grobal2.RM_NATIVE_CLEAROBJECTS, "0x6BD3AA mov cx,0x2785");
Equal(10118, Grobal2.RM_NATIVE_CHANGEMAP, "0x6BD3D3 mov cx,0x2786");

var exactMove = typeof(TBaseObject).GetMethod("TrySpaceMoveToEnvironment",
    BindingFlags.Instance | BindingFlags.NonPublic)!;
var publicSpaceMove = typeof(TBaseObject).GetMethod("SpaceMove",
    BindingFlags.Instance | BindingFlags.Public, null,
    new[] { typeof(string), typeof(short), typeof(short), typeof(int) }, null)!;
Equal(typeof(void), publicSpaceMove.ReturnType,
    "public SpaceMove return contract changed");

const string sharedMapName = "ExactMoveShared";
var registeredEnvironment = NewEnvironment(sharedMapName, "registered-instance", 0);
var exactEnvironment = NewEnvironment(sharedMapName, "unregistered-instance", 0);
RegisterMap(M2Share.MapManager, registeredEnvironment);
Assert(!M2Share.MapManager.Maps.Any(environment =>
        ReferenceEquals(environment, exactEnvironment)),
    "exact target was unexpectedly registered");

var master = NewActor(exactEnvironment, Grobal2.RC_PLAYOBJECT, 5, 5);
Place(exactEnvironment, master);
var slaveSource = NewEnvironment("ExactMoveSlaveSource", "slave-source", 0);
var slave = NewActor(slaveSource, Grobal2.RC_MONSTER, 4, 4);
slave.m_Master = master;
master.m_SlaveList.Add(slave);
slave.m_boObMode = true;
Place(slaveSource, slave);
short frontX = master.m_nCurrX;
short frontY = master.m_nCurrY;
Assert(master.GetFrontPosition(ref frontX, ref frontY),
    "master front position was not available");

var slaveMessageStart = slave.m_MsgList.Count;
Assert(InvokeExact(exactMove, slave, exactEnvironment, frontX, frontY, 1),
    "exact same-name environment move failed");
Assert(ReferenceEquals(exactEnvironment, slave.m_PEnvir),
    "exact move was redirected to the registered same-name environment");
Assert(!CellContains(registeredEnvironment, slave),
    "registered same-name environment received the slave");
Assert(CellContains(exactEnvironment, slave),
    "exact environment did not receive the slave");
Equal(0, slaveSource.MonCount, "exact move source monster count");
Equal(1, exactEnvironment.MonCount, "exact move target monster count");
Equal(0, registeredEnvironment.MonCount,
    "exact move registered-instance monster count");
Assert(ReferenceEquals(master, slave.m_Master)
       && master.m_SlaveList.Count(actor => ReferenceEquals(actor, slave)) == 1,
    "exact move changed the owner chain");
Equal(exactEnvironment.m_sMapFileName, slave.m_sMapFileName,
    "exact move map-file identity");
Assert(slave.m_boAddToMaped && !slave.m_boDelFormMaped,
    "exact move target registration flags");
AssertMessageSequence(slave, slaveMessageStart,
    Grobal2.RM_NATIVE_CLEAROBJECTS, Grobal2.RM_NATIVE_CHANGEMAP,
    Grobal2.RM_SPACEMOVE_SHOW2);
Equal(exactEnvironment.m_sMapFileName,
    slave.m_MsgList[slaveMessageStart + 1].Buff,
    "exact move map-change payload");

registeredEnvironment.SetMapXYFlag(2, 2, false);
var wrapperSource = NewEnvironment("ExactMoveWrapperSource", "wrapper-source", 0);
var wrapperActor = NewActor(wrapperSource, Grobal2.RC_MONSTER, 3, 3);
wrapperActor.m_boObMode = true;
Place(wrapperSource, wrapperActor);
var wrapperMessageStart = wrapperActor.m_MsgList.Count;
wrapperActor.SpaceMove(sharedMapName, 2, 2, 0);
Assert(ReferenceEquals(registeredEnvironment, wrapperActor.m_PEnvir),
    "string SpaceMove did not retain MapManager lookup semantics");
Assert(wrapperActor.m_nCurrX != 2 || wrapperActor.m_nCurrY != 2,
    "string SpaceMove ignored the blocked requested cell");
Assert(registeredEnvironment.CanWalk(wrapperActor.m_nCurrX,
        wrapperActor.m_nCurrY, true),
    "string SpaceMove did not use the existing random-position resolver");
AssertMessageSequence(wrapperActor, wrapperMessageStart,
    Grobal2.RM_NATIVE_CLEAROBJECTS, Grobal2.RM_NATIVE_CHANGEMAP,
    Grobal2.RM_SPACEMOVE_SHOW);

var blockedSource = NewEnvironment("ExactMoveBlockedSource", "blocked-source", 0);
var blockedTarget = NewEnvironment("ExactMoveBlockedTarget", "blocked-target", 0);
BlockAllCells(blockedTarget);
var blockedMaster = NewActor(blockedSource, Grobal2.RC_PLAYOBJECT, 6, 6);
var blockedActor = NewActor(blockedSource, Grobal2.RC_MONSTER, 3, 3);
blockedActor.m_Master = blockedMaster;
blockedMaster.m_SlaveList.Add(blockedActor);
Place(blockedSource, blockedActor);
var blockedMessageCount = blockedActor.m_MsgList.Count;
Assert(!InvokeExact(exactMove, blockedActor, blockedTarget, 2, 2, 0),
    "all-blocked exact move succeeded");
AssertSpatialRollback(blockedActor, blockedSource, blockedTarget, 3, 3,
    "all-blocked exact move");
Equal(blockedMessageCount, blockedActor.m_MsgList.Count,
    "all-blocked exact move queued a movement message");
Assert(ReferenceEquals(blockedMaster, blockedActor.m_Master)
       && blockedMaster.m_SlaveList.Contains(blockedActor),
    "all-blocked exact move changed the owner chain");

var exceptionSource = NewEnvironment("ExactMoveExceptionSource", "exception-source", 0);
var exceptionTarget = NewEnvironment("ExactMoveExceptionTarget", "exception-target", 0);
var exceptionActor = NewActor(exceptionSource, Grobal2.RC_MONSTER, 4, 4);
var cachedVisibleActor = NewActor(exceptionSource, Grobal2.RC_PLAYOBJECT, 7, 7);
exceptionActor.m_VisibleHumanList.Add(cachedVisibleActor);
Place(exceptionSource, exceptionActor);
var savedVisibleItems = exceptionActor.m_VisibleItems;
exceptionActor.m_VisibleItems = null;
var exceptionMessageCount = exceptionActor.m_MsgList.Count;
Assert(!InvokeExact(exactMove, exceptionActor, exceptionTarget, 5, 5, 0),
    "pre-commit exception reported success");
AssertSpatialRollback(exceptionActor, exceptionSource, exceptionTarget, 4, 4,
    "pre-commit exception");
Assert(exceptionActor.m_VisibleHumanList.Count == 1
       && ReferenceEquals(cachedVisibleActor,
           exceptionActor.m_VisibleHumanList[0]),
    "pre-commit exception did not restore visible-object state");
Equal(exceptionMessageCount, exceptionActor.m_MsgList.Count,
    "pre-commit exception queued a movement message");
exceptionActor.m_VisibleItems = savedVisibleItems;

var committedExceptionSource = NewEnvironment("ExactMoveCommittedExceptionSource",
    "committed-exception-source", 0);
var committedExceptionTarget = NewEnvironment("ExactMoveCommittedExceptionTarget",
    "committed-exception-target", 0);
var committedExceptionActor = NewActor(committedExceptionSource,
    Grobal2.RC_MONSTER, 4, 4);
Place(committedExceptionSource, committedExceptionActor);
var savedMessageList = committedExceptionActor.m_MsgList;
committedExceptionActor.m_MsgList = null;
Assert(InvokeExact(exactMove, committedExceptionActor,
        committedExceptionTarget, 6, 6, 0),
    "post-commit message exception reported failure");
Assert(ReferenceEquals(committedExceptionTarget,
        committedExceptionActor.m_PEnvir)
       && CellContains(committedExceptionTarget, committedExceptionActor)
       && !CellContains(committedExceptionSource, committedExceptionActor),
    "post-commit message exception rolled back the committed move");
Equal(0, committedExceptionSource.MonCount,
    "post-commit exception source monster count");
Equal(1, committedExceptionTarget.MonCount,
    "post-commit exception target monster count");
Assert(committedExceptionActor.m_boAddToMaped
       && !committedExceptionActor.m_boDelFormMaped,
    "post-commit exception target registration flags");
committedExceptionActor.m_MsgList = savedMessageList;

var rejectedSource = NewEnvironment("ExactMoveRejectedSource", "rejected-source", 0);
var remoteTarget = NewEnvironment("ExactMoveRemoteTarget", "remote-target", 1);
var rejectedActor = NewActor(rejectedSource, Grobal2.RC_MONSTER, 3, 3);
Place(rejectedSource, rejectedActor);
var rejectedMessages = rejectedActor.m_MsgList.Count;
Assert(!InvokeExact(exactMove, rejectedActor, null, 5, 5, 0),
    "null exact target succeeded");
Assert(!InvokeExact(exactMove, rejectedActor, remoteTarget, 5, 5, 0),
    "remote exact target succeeded");
AssertSpatialRollback(rejectedActor, rejectedSource, remoteTarget, 3, 3,
    "null/remote rejection");
Equal(rejectedMessages, rejectedActor.m_MsgList.Count,
    "null/remote rejection queued a movement message");
Assert(!rejectedActor.m_boDeath && !rejectedActor.m_boGhost,
    "null/remote rejection invoked monster cross-server kick semantics");

var playerSource = NewEnvironment("ExactMovePlayerSource", "player-source", 0);
var playerTarget = NewEnvironment("ExactMovePlayerTarget", "player-target", 0);
ConfigureDormantDynamicRoom(playerSource, "ExactMovePlayerSource");
ConfigureDormantDynamicRoom(playerTarget, "ExactMovePlayerTarget");
var player = new TPlayObject
{
    m_PEnvir = playerSource,
    m_sMapName = playerSource.sMapName,
    m_sMapFileName = playerSource.m_sMapFileName,
    m_nCurrX = 3,
    m_nCurrY = 3
};
Place(playerSource, player);
Equal(1, playerSource.HumCount, "player source human count before move");
Equal(1, playerSource.DynamicRoomPlayerCount,
    "player source physical presence before move");
Assert(InvokeExact(exactMove, player, playerTarget, 5, 5, 0),
    "exact player presence move failed");
Equal(0, playerSource.HumCount, "player source human count after move");
Equal(1, playerTarget.HumCount, "player target human count after move");
Equal(0, playerSource.DynamicRoomPlayerCount,
    "player source physical presence after move");
Equal(1, playerTarget.DynamicRoomPlayerCount,
    "player target physical presence after move");
Assert(player.m_boAddToMaped && !player.m_boDelFormMaped,
    "player target registration flags");

Console.WriteLine(
    "ExactEnvironmentMoveCheck PASS exact-reference=ok transaction=ok "
    + "messages=ordered+native-10117/10118 presence=ok");
return;

static bool InvokeExact(MethodInfo method, TBaseObject actor,
    Envirnoment target, short x, short y, int showMode)
{
    // TrySpaceMoveToEnvironment gained two optional parameters and Invoke does not
    // apply C# default values, so they are read off the signature. That keeps this
    // identical to a four-argument call site such as TBaseObject.SpaceMove even if
    // a default later changes.
    var parameters = method.GetParameters();
    var arguments = new object[parameters.Length];
    arguments[0] = target;
    arguments[1] = x;
    arguments[2] = y;
    arguments[3] = showMode;
    for (var index = 4; index < parameters.Length; index++)
        arguments[index] = parameters[index].DefaultValue;
    return (bool)method.Invoke(actor, arguments)!;
}

static TBaseObject NewActor(Envirnoment environment, byte race, short x, short y) =>
    new()
    {
        m_PEnvir = environment,
        m_sMapName = environment.sMapName,
        m_sMapFileName = environment.m_sMapFileName,
        m_btRaceServer = race,
        m_nCurrX = x,
        m_nCurrY = y
    };

static Envirnoment NewEnvironment(string mapName, string mapFileName,
    int serverIndex)
{
    var environment = new Envirnoment
    {
        sMapName = mapName,
        m_sMapFileName = mapFileName,
        nServerIndex = serverIndex
    };
    typeof(Envirnoment).GetMethod("Initialize",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(environment, new object[] { (short)12, (short)12 });
    return environment;
}

static void Place(Envirnoment environment, TBaseObject actor)
{
    actor.m_boAddToMaped = false;
    actor.m_boDelFormMaped = false;
    Assert(ReferenceEquals(actor, environment.AddToMap(actor.m_nCurrX,
        actor.m_nCurrY, CellType.OS_MOVINGOBJECT, actor)), "place actor");
}

static void RegisterMap(MapManager manager, Envirnoment environment)
{
    var maps = (IDictionary<string, Envirnoment>)typeof(MapManager)
        .GetField("m_MapList", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(manager)!;
    maps.Add(environment.sMapName, environment);
}

static void ConfigureDormantDynamicRoom(Envirnoment environment, string roomName)
{
    typeof(Envirnoment).GetMethod("ConfigureDormantDynamicRoom",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(environment, new object[] { roomName });
}

static void BlockAllCells(Envirnoment environment)
{
    for (var x = 0; x < environment.wWidth; x++)
    for (var y = 0; y < environment.wHeight; y++)
        environment.SetMapXYFlag(x, y, false);
}

static bool CellContains(Envirnoment environment, TBaseObject actor)
{
    for (var x = 0; x < environment.wWidth; x++)
    for (var y = 0; y < environment.wHeight; y++)
    {
        var found = false;
        var cell = environment.GetMapCellInfo(x, y, ref found);
        if (found && cell.ObjList != null && cell.ObjList.Any(item =>
                item.CellType == CellType.OS_MOVINGOBJECT
                && ReferenceEquals(item.CellObj, actor)))
            return true;
    }
    return false;
}

static void AssertSpatialRollback(TBaseObject actor, Envirnoment source,
    Envirnoment rejectedTarget, short sourceX, short sourceY, string label)
{
    Assert(ReferenceEquals(source, actor.m_PEnvir),
        label + " changed environment identity");
    Equal(source.sMapName, actor.m_sMapName, label + " changed map name");
    Equal(source.m_sMapFileName, actor.m_sMapFileName,
        label + " changed map-file name");
    Equal(sourceX, actor.m_nCurrX, label + " changed X");
    Equal(sourceY, actor.m_nCurrY, label + " changed Y");
    Assert(CellContains(source, actor), label + " did not restore source cell");
    Assert(!CellContains(rejectedTarget, actor),
        label + " left an object in the rejected target");
    Equal(1, source.MonCount, label + " source monster count");
    Equal(0, rejectedTarget.MonCount, label + " target monster count");
    Assert(actor.m_boAddToMaped && !actor.m_boDelFormMaped,
        label + " registration flags");
}

static void AssertMessageSequence(TBaseObject actor, int start,
    params int[] expected)
{
    var actual = actor.m_MsgList.Skip(start).Select(message => message.wIdent).ToArray();
    if (!actual.SequenceEqual(expected))
    {
        throw new InvalidOperationException(
            $"message order: expected {string.Join(",", expected)}, actual {string.Join(",", actual)}");
    }
}

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{label}: expected {expected}, actual {actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
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
