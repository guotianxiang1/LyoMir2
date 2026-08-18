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
CheckNativeNonPlayerSameEnvironmentSpaceMove(exactMove);
CheckNativeNonPlayerCrossEnvironmentEdgeCases(exactMove);
var environmentSpaceMove = typeof(TBaseObject).GetMethod("SpaceMove",
    BindingFlags.Instance | BindingFlags.NonPublic, null,
    new[] { typeof(Envirnoment), typeof(short), typeof(short), typeof(int) },
    null)!;
Equal(typeof(void), environmentSpaceMove.ReturnType,
    "by-environment SpaceMove return contract changed");
CheckNativeServerIndexDispatch(environmentSpaceMove);
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
var slaveVisibility = SeedVisibility(slave, slaveSource);
const int slaveTick = 0x11223344;
slave.m_dwMapMoveTick = slaveTick;
slave.m_bo316 = false;
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
Equal(slaveSource.m_sMapFileName, slave.m_sMapFileName,
    "native base move changed map-file identity");
Assert(slave.m_boAddToMaped && !slave.m_boDelFormMaped,
    "exact move target registration flags");
AssertActorPosition(slave, exactEnvironment, frontX, frontY,
    "native base exact-reference cross-map move");
AssertNativeVisibilityState(slave, slaveVisibility,
    "native base exact-reference cross-map move");
AssertNativeSpaceMoveMessage(slave, slaveMessageStart,
    Grobal2.RM_SPACEMOVE_SHOW2, frontX, frontY,
    "native base exact-reference cross-map move");
Assert(slave.m_dwMapMoveTick != slaveTick && slave.m_bo316,
    "native base exact-reference cross-map tick/latch");
slaveVisibility.Event.Dispose();

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
AssertNativeSpaceMoveMessage(wrapperActor, wrapperMessageStart,
    Grobal2.RM_SPACEMOVE_SHOW, wrapperActor.m_nCurrX,
    wrapperActor.m_nCurrY, "native base string SpaceMove");
Equal(wrapperSource.m_sMapFileName, wrapperActor.m_sMapFileName,
    "native base string SpaceMove changed map-file identity");

var blockedSource = NewEnvironment("ExactMoveBlockedSource", "blocked-source", 0);
var blockedTarget = NewEnvironment("BlockedTarget", "blocked-target", 0);
BlockAllCells(blockedTarget);
var blockedMaster = NewActor(blockedSource, Grobal2.RC_PLAYOBJECT, 6, 6);
var blockedActor = NewActor(blockedSource, Grobal2.RC_MONSTER, 3, 3);
blockedActor.m_Master = blockedMaster;
blockedMaster.m_SlaveList.Add(blockedActor);
Place(blockedSource, blockedActor);
var blockedOldNode = FindActorNode(blockedSource, blockedActor, 3, 3);
var blockedVisibility = SeedVisibility(blockedActor, blockedSource);
var blockedMessageCount = blockedActor.m_MsgList.Count;
const int blockedTick = 0x55667788;
blockedActor.m_dwMapMoveTick = blockedTick;
blockedActor.m_bo316 = false;
var blockedSavedRandom = M2Share.RandomNumber;
var blockedRandom = new CountingZeroRandomNumber();
M2Share.RandomNumber = blockedRandom;
try
{
    Assert(!InvokeExact(exactMove, blockedActor, blockedTarget, 2, 2, 0),
        "all-blocked exact move succeeded");
}
finally
{
    M2Share.RandomNumber = blockedSavedRandom;
}
Equal(7, blockedRandom.Bounds.Count,
    "native base cross-map search RNG draw count");
Assert(blockedRandom.Bounds.All(bound => bound == 6),
    "native base cross-map search RNG bounds");
Assert(ReferenceEquals(blockedSource, blockedActor.m_PEnvir),
    "all-blocked exact move changed environment");
Equal((short)3, blockedActor.m_nCurrX,
    "all-blocked exact move X rollback");
Equal((short)3, blockedActor.m_nCurrY,
    "all-blocked exact move Y rollback");
Equal(blockedTarget.sMapName, blockedActor.m_sMapName,
    "all-blocked exact move restored the native-stale map name");
Equal(blockedSource.m_sMapFileName, blockedActor.m_sMapFileName,
    "all-blocked exact move changed map-file identity");
var blockedNewNode = FindActorNode(blockedSource, blockedActor, 3, 3);
Assert(blockedNewNode != null && !ReferenceEquals(blockedOldNode, blockedNewNode),
    "all-blocked exact move restored the old node instance");
Equal(1, CountActorNodes(blockedSource, blockedActor),
    "all-blocked exact move source node count");
Equal(0, CountActorNodes(blockedTarget, blockedActor),
    "all-blocked exact move target node count");
Equal(1, blockedSource.MonCount,
    "all-blocked exact move source monster count");
Equal(0, blockedTarget.MonCount,
    "all-blocked exact move target monster count");
Assert(blockedActor.m_boAddToMaped && !blockedActor.m_boDelFormMaped,
    "all-blocked exact move registration flags");
AssertNativeVisibilityState(blockedActor, blockedVisibility,
    "all-blocked native base cross-map move");
Equal(blockedMessageCount, blockedActor.m_MsgList.Count,
    "all-blocked exact move queued a movement message");
Equal(blockedTick, blockedActor.m_dwMapMoveTick,
    "all-blocked exact move changed map-move tick");
Assert(!blockedActor.m_bo316,
    "all-blocked exact move changed map-move latch");
Assert(ReferenceEquals(blockedMaster, blockedActor.m_Master)
       && blockedMaster.m_SlaveList.Contains(blockedActor),
    "all-blocked exact move changed the owner chain");
blockedVisibility.Event.Dispose();

var exceptionSource = NewEnvironment("ExactMoveExceptionSource", "exception-source", 0);
var exceptionTarget = NewEnvironment("ExceptionTarget", "exception-target", 0);
var exceptionActor = NewActor(exceptionSource, Grobal2.RC_MONSTER, 4, 4);
var cachedVisibleActor = NewActor(exceptionSource, Grobal2.RC_PLAYOBJECT, 7, 7);
exceptionActor.m_VisibleHumanList.Add(cachedVisibleActor);
exceptionActor.m_VisibleActors.Add(new TVisibleBaseObject
    { BaseObject = cachedVisibleActor });
exceptionActor.m_boObMode = true;
Place(exceptionSource, exceptionActor);
var savedVisibleItems = exceptionActor.m_VisibleItems;
exceptionActor.m_VisibleItems = null;
var exceptionMessageStart = exceptionActor.m_MsgList.Count;
Assert(InvokeExact(exactMove, exceptionActor, exceptionTarget, 5, 5, 0),
    "native base move touched a null item-visibility list");
AssertActorPosition(exceptionActor, exceptionTarget, 5, 5,
    "native base null-item-list move");
Assert(exceptionActor.m_VisibleItems == null,
    "native base move replaced the item-visibility list");
Equal(0, exceptionActor.m_VisibleHumanList.Count,
    "native base null-item-list visible humans");
Equal(0, exceptionActor.m_VisibleActors.Count,
    "native base null-item-list visible actors");
Equal(exceptionSource.m_sMapFileName, exceptionActor.m_sMapFileName,
    "native base null-item-list map-file identity");
AssertNativeSpaceMoveMessage(exceptionActor, exceptionMessageStart,
    Grobal2.RM_SPACEMOVE_SHOW, 5, 5,
    "native base null-item-list move");
exceptionActor.m_VisibleItems = savedVisibleItems;

var committedExceptionSource = NewEnvironment("ExactMoveCommittedExceptionSource",
    "committed-exception-source", 0);
var committedExceptionTarget = NewEnvironment("CommitTarget",
    "committed-exception-target", 0);
var committedExceptionActor = NewActor(committedExceptionSource,
    Grobal2.RC_MONSTER, 4, 4);
committedExceptionActor.m_boObMode = true;
Place(committedExceptionSource, committedExceptionActor);
var savedMessageList = committedExceptionActor.m_MsgList;
committedExceptionActor.m_MsgList = null;
const int committedExceptionTick = 0x66778899;
committedExceptionActor.m_dwMapMoveTick = committedExceptionTick;
committedExceptionActor.m_bo316 = false;
var messageFaultObserved = false;
try
{
    InvokeExact(exactMove, committedExceptionActor,
        committedExceptionTarget, 6, 6, 0);
}
catch (TargetInvocationException exception)
{
    messageFaultObserved = exception.InnerException != null;
}
Assert(messageFaultObserved,
    "native base SHOW fault was swallowed by a managed transaction");
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
Equal(committedExceptionTarget.sMapName,
    committedExceptionActor.m_sMapName,
    "post-commit exception map name");
Equal(committedExceptionSource.m_sMapFileName,
    committedExceptionActor.m_sMapFileName,
    "post-commit exception map-file identity");
Equal(committedExceptionTick, committedExceptionActor.m_dwMapMoveTick,
    "post-commit exception wrote tick before SHOW completed");
Assert(!committedExceptionActor.m_bo316,
    "post-commit exception wrote latch before SHOW completed");
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
    + "server-index=base-pass/player-handoff "
    + "messages=ordered+native-10117/10118 presence=ok");
return;

static void CheckNativeServerIndexDispatch(MethodInfo environmentSpaceMove)
{
    var source = NewEnvironment("Move55IndexSource", "move55-index-source", 0);
    var target = NewEnvironment("Move55IndexTarget", "move55-index-target", 1);
    var mover = NewActor(source, Grobal2.RC_MONSTER, 3, 3);
    mover.m_sCharName = "move55-index-mover";
    mover.m_boObMode = true;
    Place(source, mover);
    var visibility = SeedVisibility(mover, source);
    var messageStart = mover.m_MsgList.Count;
    const int tick = 0x10203040;
    mover.m_dwMapMoveTick = tick;
    mover.m_bo316 = false;

    InvokeEnvironmentSpaceMove(environmentSpaceMove, mover, target, 5, 5, 0);

    AssertActorPosition(mover, target, 5, 5,
        "MOVE-55 nonplayer cross-index dispatch");
    Equal(0, source.MonCount,
        "MOVE-55 nonplayer cross-index source count");
    Equal(1, target.MonCount,
        "MOVE-55 nonplayer cross-index target count");
    Assert(!mover.m_boDeath && !mover.m_boGhost,
        "MOVE-55 nonplayer cross-index dispatch invoked KickException");
    AssertNativeVisibilityState(mover, visibility,
        "MOVE-55 nonplayer cross-index dispatch");
    AssertNativeSpaceMoveMessage(mover, messageStart,
        Grobal2.RM_SPACEMOVE_SHOW, 5, 5,
        "MOVE-55 nonplayer cross-index dispatch");
    Assert(mover.m_dwMapMoveTick != tick && mover.m_bo316,
        "MOVE-55 nonplayer cross-index tick/latch");
    visibility.Event.Dispose();

    var playerSource = NewEnvironment("Move60IndexSource",
        "move60-index-source", 0);
    var playerTarget = NewEnvironment("Move60IndexTarget",
        "move60-index-target", 2);
    var player = new TPlayObject
    {
        m_PEnvir = playerSource,
        m_sMapName = playerSource.sMapName,
        m_sMapFileName = playerSource.m_sMapFileName,
        m_sCharName = "move60-index-player",
        m_nCurrX = 3,
        m_nCurrY = 3,
        m_boObMode = true
    };
    Place(playerSource, player);

    InvokeEnvironmentSpaceMove(environmentSpaceMove, player, playerTarget,
        5, 5, 1);

    Assert(ReferenceEquals(playerSource, player.m_PEnvir),
        "MOVE-60 player cross-index was locally moved");
    Equal(0, CountActorNodes(playerSource, player),
        "MOVE-60 player cross-index source node count");
    Equal(0, CountActorNodes(playerTarget, player),
        "MOVE-60 player cross-index target node count");
    Assert(player.m_boSwitchData && player.m_boEmergencyClose
           && player.m_boReconnection && player.m_bo316,
        "MOVE-60 player cross-index handoff flags");
    Equal(playerTarget.sMapName, player.m_sSwitchMapName,
        "MOVE-60 player cross-index target map");
    Equal((short)5, player.m_nSwitchMapX,
        "MOVE-60 player cross-index target X");
    Equal((short)5, player.m_nSwitchMapY,
        "MOVE-60 player cross-index target Y");
    Equal(playerTarget.nServerIndex, player.m_nServerIndex,
        "MOVE-60 player cross-index target server");
    Assert(!player.m_boDeath && !player.m_boGhost,
        "MOVE-60 player cross-index handoff killed the player");
}

static void CheckNativeNonPlayerSameEnvironmentSpaceMove(MethodInfo exactMove)
{
    var fastMap = NewEnvironment("Move54Fast", "move54-fast", 0);
    var fastMover = NewActor(fastMap, Grobal2.RC_MONSTER, 3, 3);
    fastMover.m_sCharName = "move54-fast-mover";
    fastMover.m_boObMode = true;
    Place(fastMap, fastMover);
    var fastNode = FindActorNode(fastMap, fastMover, 3, 3);
    Assert(fastNode != null, "MOVE-54 fast source node missing");
    fastNode.dwAddTime = 0x12345678;
    var fastVisibility = SeedVisibility(fastMover, fastMap);
    var fastMessageCount = fastMover.m_MsgList.Count;
    const int fastTick = 0x13572468;
    fastMover.m_dwMapMoveTick = fastTick;
    fastMover.m_bo316 = false;
    var fastMonCount = fastMap.MonCount;

    Assert(InvokeExact(exactMove, fastMover, fastMap, 4, 3, 0),
        "MOVE-54 fast same-environment move failed");
    AssertActorPosition(fastMover, fastMap, 4, 3,
        "MOVE-54 fast path");
    var relocatedFastNode = FindActorNode(fastMap, fastMover, 4, 3);
    Assert(ReferenceEquals(fastNode, relocatedFastNode),
        "MOVE-54 fast path replaced the native map node");
    Equal(CellType.OS_MOVINGOBJECT, relocatedFastNode.CellType,
        "MOVE-54 fast path changed the node type");
    Equal(0x12345678, relocatedFastNode.dwAddTime,
        "MOVE-54 fast path changed the node timestamp");
    Assert(FindActorNode(fastMap, fastMover, 3, 3) == null,
        "MOVE-54 fast path left the source node linked");
    Equal(1, CountActorNodes(fastMap, fastMover),
        "MOVE-54 fast path actor node count");
    Equal(fastMessageCount, fastMover.m_MsgList.Count,
        "MOVE-54 fast path emitted a message");
    AssertNativeVisibilityState(fastMover, fastVisibility,
        "MOVE-54 fast path");
    Equal(fastTick, fastMover.m_dwMapMoveTick,
        "MOVE-54 fast path changed map-move tick");
    Assert(!fastMover.m_bo316,
        "MOVE-54 fast path changed map-move latch");
    Equal(fastMonCount, fastMap.MonCount,
        "MOVE-54 fast path changed monster count");
    fastVisibility.Event.Dispose();

    var resolvedMap = NewEnvironment("Move54Resolved", "move54-resolved", 0);
    resolvedMap.SetMapXYFlag(4, 3, false);
    var resolvedMover = NewActor(resolvedMap, Grobal2.RC_MONSTER, 3, 3);
    resolvedMover.m_sCharName = "move54-resolved-mover";
    resolvedMover.m_boObMode = true;
    Place(resolvedMap, resolvedMover);
    var resolvedOldNode = FindActorNode(resolvedMap, resolvedMover, 3, 3);
    var resolvedMessageCount = resolvedMover.m_MsgList.Count;
    Assert(InvokeExact(exactMove, resolvedMover, resolvedMap, 4, 3, 0,
            coordinatesAlreadyResolved: true),
        "MOVE-54 nonplayer resolved-flag move failed");
    AssertActorPosition(resolvedMover, resolvedMap, 6, 3,
        "MOVE-54 nonplayer resolved-flag path");
    Assert(ReferenceEquals(resolvedOldNode,
            FindActorNode(resolvedMap, resolvedMover, 6, 3)),
        "MOVE-54 nonplayer resolved flag skipped the mandatory first search");
    Equal(resolvedMessageCount, resolvedMover.m_MsgList.Count,
        "MOVE-54 nonplayer resolved flag forced the robust message path");

    var occupiedMap = NewEnvironment("Move54Occupied", "move54-occupied", 0);
    var occupiedMover = NewActor(occupiedMap, Grobal2.RC_MONSTER, 3, 3);
    occupiedMover.m_sCharName = "move54-occupied-mover";
    occupiedMover.m_boObMode = true;
    var blocker = NewActor(occupiedMap, Grobal2.RC_MONSTER, 4, 3);
    blocker.m_sCharName = "move54-blocker";
    Place(occupiedMap, occupiedMover);
    Place(occupiedMap, blocker);
    var occupiedOldNode = FindActorNode(occupiedMap, occupiedMover, 3, 3);
    var occupiedVisibility = SeedVisibility(occupiedMover, occupiedMap);
    var occupiedMessageStart = occupiedMover.m_MsgList.Count;
    const int occupiedTick = 0x24681357;
    occupiedMover.m_dwMapMoveTick = occupiedTick;
    occupiedMover.m_bo316 = false;

    Assert(InvokeExact(exactMove, occupiedMover, occupiedMap, 4, 3, 1),
        "MOVE-54 occupied-target robust move failed");
    AssertActorPosition(occupiedMover, occupiedMap, 4, 3,
        "MOVE-54 occupied-target robust path");
    var occupiedNewNode = FindActorNode(occupiedMap, occupiedMover, 4, 3);
    Assert(occupiedNewNode != null
           && !ReferenceEquals(occupiedOldNode, occupiedNewNode),
        "MOVE-54 occupied-target robust path reused the old node");
    Equal(1, CountActorNodes(occupiedMap, occupiedMover),
        "MOVE-54 occupied-target mover node count");
    Assert(FindActorNode(occupiedMap, blocker, 4, 3) != null,
        "MOVE-54 occupied-target move displaced the blocker");
    Assert(IsCellHead(occupiedMap, occupiedNewNode, 4, 3),
        "MOVE-54 occupied-target actor node was not head-inserted");
    AssertNativeSpaceMoveMessage(occupiedMover, occupiedMessageStart,
        Grobal2.RM_SPACEMOVE_SHOW2, 4, 3,
        "MOVE-54 occupied-target robust path");
    AssertNativeVisibilityState(occupiedMover, occupiedVisibility,
        "MOVE-54 occupied-target robust path");
    Assert(occupiedMover.m_dwMapMoveTick != occupiedTick,
        "MOVE-54 occupied-target path did not update map-move tick");
    Assert(occupiedMover.m_bo316,
        "MOVE-54 occupied-target path did not set map-move latch");
    Equal(2, occupiedMap.MonCount,
        "MOVE-54 occupied-target monster count");
    occupiedVisibility.Event.Dispose();

    var linkMap = NewEnvironment("Move54LinkPoint", "move54-link", 0);
    var linkMover = NewActor(linkMap, Grobal2.RC_MONSTER, 3, 3);
    linkMover.m_sCharName = "move54-link-mover";
    linkMover.m_boObMode = true;
    Place(linkMap, linkMover);
    var linkOldNode = FindActorNode(linkMap, linkMover, 3, 3);
    var gateMarker = new object();
    Assert(ReferenceEquals(gateMarker, linkMap.AddToMap(4, 3,
            CellType.OS_GATEOBJECT, gateMarker)),
        "MOVE-54 LinkPoint marker placement failed");
    var linkMessageStart = linkMover.m_MsgList.Count;

    Assert(InvokeExact(exactMove, linkMover, linkMap, 4, 3, 2),
        "MOVE-54 LinkPoint robust move failed");
    AssertActorPosition(linkMover, linkMap, 4, 3,
        "MOVE-54 LinkPoint robust path");
    var linkNewNode = FindActorNode(linkMap, linkMover, 4, 3);
    Assert(linkNewNode != null && !ReferenceEquals(linkOldNode, linkNewNode),
        "MOVE-54 LinkPoint did not force the robust path");
    Assert(IsCellHead(linkMap, linkNewNode, 4, 3),
        "MOVE-54 LinkPoint actor node was not head-inserted");
    Assert(FindCellNode(linkMap, gateMarker, CellType.OS_GATEOBJECT, 4, 3)
           != null,
        "MOVE-54 LinkPoint robust path removed the gate marker");
    AssertNativeSpaceMoveMessage(linkMover, linkMessageStart,
        Grobal2.RM_SPACEMOVE_SHOW, 4, 3,
        "MOVE-54 LinkPoint showMode=2 path");

    var missingMap = NewEnvironment("Move54MissingSource", "move54-missing", 0);
    var missingMover = NewActor(missingMap, Grobal2.RC_MONSTER, 3, 3);
    missingMover.m_sCharName = "move54-missing-mover";
    missingMover.m_boObMode = true;
    Place(missingMap, missingMover);
    Assert(UnlinkActorNodeWithoutRegistration(missingMap, missingMover, 3, 3)
           != null, "MOVE-54 missing-source setup failed");
    var missingMessageStart = missingMover.m_MsgList.Count;
    const int missingTick = 0x31415926;
    missingMover.m_dwMapMoveTick = missingTick;
    missingMover.m_bo316 = false;
    Assert(InvokeExact(exactMove, missingMover, missingMap, 4, 3, 0),
        "MOVE-54 missing-source robust move failed");
    AssertActorPosition(missingMover, missingMap, 4, 3,
        "MOVE-54 missing-source robust path");
    Assert(FindActorNode(missingMap, missingMover, 4, 3) != null,
        "MOVE-54 missing-source path did not create the target node");
    Equal(1, CountActorNodes(missingMap, missingMover),
        "MOVE-54 missing-source actor node count");
    Equal(2, missingMap.MonCount,
        "MOVE-54 missing-source native duplicate registration count");
    Assert(missingMover.m_boAddToMaped && !missingMover.m_boDelFormMaped,
        "MOVE-54 missing-source registration flags");
    Assert(missingMover.m_dwMapMoveTick != missingTick && missingMover.m_bo316,
        "MOVE-54 missing-source tick/latch state");
    AssertNativeSpaceMoveMessage(missingMover, missingMessageStart,
        Grobal2.RM_SPACEMOVE_SHOW, 4, 3,
        "MOVE-54 missing-source robust path");

    var failureMap = NewEnvironment("Move54Failure", "move54-failure", 0);
    BlockAllCells(failureMap);
    failureMap.SetMapXYFlag(3, 3, true);
    var failureMover = NewActor(failureMap, Grobal2.RC_MONSTER, 3, 3);
    failureMover.m_sCharName = "move54-failure-mover";
    failureMover.m_boObMode = true;
    Place(failureMap, failureMover);
    var failureOldNode = FindActorNode(failureMap, failureMover, 3, 3);
    var failureVisibility = SeedVisibility(failureMover, failureMap);
    var failureMessageCount = failureMover.m_MsgList.Count;
    const int failureTick = 0x10293847;
    failureMover.m_dwMapMoveTick = failureTick;
    failureMover.m_bo316 = false;
    var savedRandom = M2Share.RandomNumber;
    var countingRandom = new CountingZeroRandomNumber();
    M2Share.RandomNumber = countingRandom;
    try
    {
        Assert(!InvokeExact(exactMove, failureMover, failureMap, 2, 2, 0),
            "MOVE-54 two-search failure reported success");
    }
    finally
    {
        M2Share.RandomNumber = savedRandom;
    }
    Equal(14, countingRandom.Bounds.Count,
        "MOVE-54 two-search RNG draw count");
    Assert(countingRandom.Bounds.All(bound => bound == 6),
        "MOVE-54 two-search RNG bound sequence");
    Assert(ReferenceEquals(failureMap, failureMover.m_PEnvir),
        "MOVE-54 failed robust path changed environment");
    Equal((short)3, failureMover.m_nCurrX,
        "MOVE-54 failed robust path X rollback");
    Equal((short)3, failureMover.m_nCurrY,
        "MOVE-54 failed robust path Y rollback");
    var failureNewNode = FindActorNode(failureMap, failureMover, 3, 3);
    Assert(failureNewNode != null
           && !ReferenceEquals(failureOldNode, failureNewNode),
        "MOVE-54 failed robust path restored the old node instance");
    Equal(1, CountActorNodes(failureMap, failureMover),
        "MOVE-54 failed robust path actor node count");
    Equal(1, failureMap.MonCount,
        "MOVE-54 failed robust path monster count");
    Assert(failureMover.m_boAddToMaped && !failureMover.m_boDelFormMaped,
        "MOVE-54 failed robust path registration flags");
    Equal(failureMessageCount, failureMover.m_MsgList.Count,
        "MOVE-54 failed robust path emitted a message");
    AssertNativeVisibilityState(failureMover, failureVisibility,
        "MOVE-54 failed robust path");
    Equal(failureTick, failureMover.m_dwMapMoveTick,
        "MOVE-54 failed robust path changed map-move tick");
    Assert(!failureMover.m_bo316,
        "MOVE-54 failed robust path changed map-move latch");
    failureVisibility.Event.Dispose();

    var playerMap = NewEnvironment("Move54Player", "move54-player", 0);
    var player = new TPlayObject
    {
        m_PEnvir = playerMap,
        m_sMapName = playerMap.sMapName,
        m_sMapFileName = playerMap.m_sMapFileName,
        m_sCharName = "move54-player",
        m_nCurrX = 3,
        m_nCurrY = 3,
        m_boObMode = true
    };
    var playerBlocker = NewActor(playerMap, Grobal2.RC_MONSTER, 4, 3);
    playerBlocker.m_sCharName = "move54-player-blocker";
    Place(playerMap, player);
    Place(playerMap, playerBlocker);
    var playerOldNode = FindActorNode(playerMap, player, 3, 3);
    var playerMessageStart = player.m_MsgList.Count;
    Assert(InvokeExact(exactMove, player, playerMap, 4, 3, 1),
        "MOVE-54 player isolation move failed");
    AssertActorPosition(player, playerMap, 4, 3,
        "MOVE-54 player isolation path");
    var playerNewNode = FindActorNode(playerMap, player, 4, 3);
    Assert(playerNewNode != null && !ReferenceEquals(playerOldNode, playerNewNode),
        "MOVE-54 player incorrectly used the nonplayer fast path");
    AssertMessageSequence(player, playerMessageStart,
        Grobal2.RM_NATIVE_CLEAROBJECTS, Grobal2.RM_NATIVE_CHANGEMAP,
        Grobal2.RM_SPACEMOVE_SHOW2);
    AssertNativeSpaceMovePayload(player, playerMessageStart + 2,
        Grobal2.RM_SPACEMOVE_SHOW2, 4, 3,
        "MOVE-54 player isolation path");
}

static void CheckNativeNonPlayerCrossEnvironmentEdgeCases(MethodInfo exactMove)
{
    const string longMapName =
        "\u76ee\u6807\u5730\u56feABCDEFGH";
    const string truncatedMapName =
        "\u76ee\u6807\u5730\u56feABCDEFG";
    var resolvedSource = NewEnvironment("Move55ResolvedSource",
        "move55-resolved-source", 0);
    var resolvedTarget = NewEnvironment(longMapName,
        "move55-resolved-target", 0);
    resolvedTarget.SetMapXYFlag(4, 3, false);
    var resolvedMover = NewActor(resolvedSource, Grobal2.RC_MONSTER, 3, 3);
    resolvedMover.m_sCharName = "move55-resolved-mover";
    resolvedMover.m_boObMode = true;
    Place(resolvedSource, resolvedMover);
    var resolvedOldNode = FindActorNode(resolvedSource, resolvedMover, 3, 3);
    var resolvedVisibility = SeedVisibility(resolvedMover, resolvedSource);
    var resolvedMessageStart = resolvedMover.m_MsgList.Count;
    const int resolvedTick = 0x12344321;
    resolvedMover.m_dwMapMoveTick = resolvedTick;
    resolvedMover.m_bo316 = false;
    Assert(InvokeExact(exactMove, resolvedMover, resolvedTarget, 4, 3, 2,
            coordinatesAlreadyResolved: true),
        "MOVE-55 resolved-flag cross-map move failed");
    AssertActorPosition(resolvedMover, resolvedTarget, 6, 3,
        "MOVE-55 resolved-flag cross-map move");
    var resolvedNewNode = FindActorNode(resolvedTarget, resolvedMover, 6, 3);
    Assert(resolvedNewNode != null
           && !ReferenceEquals(resolvedOldNode, resolvedNewNode),
        "MOVE-55 cross-map robust path reused the source node");
    Equal(truncatedMapName, resolvedMover.m_sMapName,
        "MOVE-55 GBK ShortString[15] map name");
    Equal(resolvedSource.m_sMapFileName, resolvedMover.m_sMapFileName,
        "MOVE-55 cross-map move changed map-file identity");
    Equal(0, resolvedSource.MonCount,
        "MOVE-55 resolved source monster count");
    Equal(1, resolvedTarget.MonCount,
        "MOVE-55 resolved target monster count");
    AssertNativeVisibilityState(resolvedMover, resolvedVisibility,
        "MOVE-55 resolved-flag cross-map move");
    AssertNativeSpaceMoveMessage(resolvedMover, resolvedMessageStart,
        Grobal2.RM_SPACEMOVE_SHOW, 6, 3,
        "MOVE-55 resolved-flag showMode=2 move");
    Assert(resolvedMover.m_dwMapMoveTick != resolvedTick
           && resolvedMover.m_bo316,
        "MOVE-55 resolved-flag tick/latch");
    resolvedVisibility.Event.Dispose();

    var missingSource = NewEnvironment("Move55MissingSource",
        "move55-missing-source", 0);
    var missingTarget = NewEnvironment("Move55MissingTarget",
        "move55-missing-target", 0);
    var missingMover = NewActor(missingSource, Grobal2.RC_MONSTER, 3, 3);
    missingMover.m_sCharName = "move55-missing-mover";
    missingMover.m_boObMode = true;
    Place(missingSource, missingMover);
    Assert(UnlinkActorNodeWithoutRegistration(missingSource, missingMover, 3, 3)
           != null, "MOVE-55 missing-source setup failed");
    var missingMessageStart = missingMover.m_MsgList.Count;
    Assert(InvokeExact(exactMove, missingMover, missingTarget, 5, 5, 1),
        "MOVE-55 missing-source move failed");
    AssertActorPosition(missingMover, missingTarget, 5, 5,
        "MOVE-55 missing-source move");
    Equal(1, missingSource.MonCount,
        "MOVE-55 missing-source stale source count");
    Equal(1, missingTarget.MonCount,
        "MOVE-55 missing-source target count");
    Assert(missingMover.m_boAddToMaped && !missingMover.m_boDelFormMaped,
        "MOVE-55 missing-source registration flags");
    AssertNativeSpaceMoveMessage(missingMover, missingMessageStart,
        Grobal2.RM_SPACEMOVE_SHOW2, 5, 5,
        "MOVE-55 missing-source move");

    var duplicateSource = NewEnvironment("Move55DuplicateSource",
        "move55-duplicate-source", 0);
    var duplicateTarget = NewEnvironment("Move55DuplicateTarget",
        "move55-duplicate-target", 0);
    var duplicateMover = NewActor(duplicateSource, Grobal2.RC_MONSTER, 3, 3);
    duplicateMover.m_sCharName = "move55-duplicate-mover";
    duplicateMover.m_boObMode = true;
    Place(duplicateSource, duplicateMover);
    InsertRawCellNode(duplicateTarget, duplicateMover,
        CellType.OS_MOVINGOBJECT, 5, 5);
    var duplicateMessageStart = duplicateMover.m_MsgList.Count;
    const int duplicateTick = 0x23455432;
    duplicateMover.m_dwMapMoveTick = duplicateTick;
    duplicateMover.m_bo316 = false;
    Assert(InvokeExact(exactMove, duplicateMover, duplicateTarget, 5, 5, 0),
        "MOVE-55 duplicate-target move reported Add failure");
    AssertActorPosition(duplicateMover, duplicateTarget, 5, 5,
        "MOVE-55 duplicate-target move");
    Equal(0, CountActorNodes(duplicateSource, duplicateMover),
        "MOVE-55 duplicate-target source node count");
    Equal(1, CountActorNodes(duplicateTarget, duplicateMover),
        "MOVE-55 duplicate-target node count");
    AssertNativeSpaceMoveMessage(duplicateMover, duplicateMessageStart,
        Grobal2.RM_SPACEMOVE_SHOW, 5, 5,
        "MOVE-55 duplicate-target move");
    Assert(duplicateMover.m_dwMapMoveTick != duplicateTick
           && duplicateMover.m_bo316,
        "MOVE-55 duplicate-target tick/latch");

    var rollbackSource = NewEnvironment("Move55RollbackSource",
        "move55-rollback-source", 0);
    var rollbackTarget = NewEnvironment("RollbackTarget",
        "move55-rollback-target", 0);
    BlockAllCells(rollbackTarget);
    var rollbackMover = NewActor(rollbackSource, Grobal2.RC_MONSTER, 3, 3);
    rollbackMover.m_sCharName = "move55-rollback-mover";
    rollbackMover.m_boObMode = true;
    Place(rollbackSource, rollbackMover);
    rollbackSource.SetMapXYFlag(3, 3, false);
    var rollbackVisibility = SeedVisibility(rollbackMover, rollbackSource);
    var rollbackMessageCount = rollbackMover.m_MsgList.Count;
    const int rollbackTick = 0x34566543;
    rollbackMover.m_dwMapMoveTick = rollbackTick;
    rollbackMover.m_bo316 = false;
    var savedRandom = M2Share.RandomNumber;
    var rollbackRandom = new CountingZeroRandomNumber();
    M2Share.RandomNumber = rollbackRandom;
    try
    {
        Assert(!InvokeExact(exactMove, rollbackMover, rollbackTarget, 2, 2, 0),
            "MOVE-55 blocked rollback-source move succeeded");
    }
    finally
    {
        M2Share.RandomNumber = savedRandom;
    }
    Equal(7, rollbackRandom.Bounds.Count,
        "MOVE-55 blocked rollback-source RNG draw count");
    AssertActorFields(rollbackMover, rollbackSource, 3, 3,
        "MOVE-55 blocked rollback-source move");
    Equal(rollbackTarget.sMapName, rollbackMover.m_sMapName,
        "MOVE-55 blocked rollback-source stale map name");
    Equal(0, CountActorNodes(rollbackSource, rollbackMover),
        "MOVE-55 blocked rollback-source source node count");
    Equal(0, CountActorNodes(rollbackTarget, rollbackMover),
        "MOVE-55 blocked rollback-source target node count");
    Equal(0, rollbackSource.MonCount,
        "MOVE-55 blocked rollback-source monster count");
    Assert(!rollbackMover.m_boAddToMaped && rollbackMover.m_boDelFormMaped,
        "MOVE-55 blocked rollback-source registration flags");
    Equal(rollbackMessageCount, rollbackMover.m_MsgList.Count,
        "MOVE-55 blocked rollback-source emitted a message");
    Equal(rollbackTick, rollbackMover.m_dwMapMoveTick,
        "MOVE-55 blocked rollback-source changed tick");
    Assert(!rollbackMover.m_bo316,
        "MOVE-55 blocked rollback-source changed latch");
    AssertNativeVisibilityState(rollbackMover, rollbackVisibility,
        "MOVE-55 blocked rollback-source move");
    rollbackVisibility.Event.Dispose();
}

static bool InvokeExact(MethodInfo method, TBaseObject actor,
    Envirnoment target, short x, short y, int showMode,
    bool coordinatesAlreadyResolved = false)
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
    if (parameters.Length > 4)
        arguments[4] = coordinatesAlreadyResolved;
    for (var index = 5; index < parameters.Length; index++)
        arguments[index] = parameters[index].DefaultValue;
    return (bool)method.Invoke(actor, arguments)!;
}

static void InvokeEnvironmentSpaceMove(MethodInfo method, TBaseObject actor,
    Envirnoment target, short x, short y, int showMode)
{
    method.Invoke(actor, new object[] { target, x, y, showMode });
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

static (VisibleMapItem Item, GameSvr.Event Event) SeedVisibility(
    TBaseObject actor, Envirnoment environment)
{
    var visibleActor = NewActor(environment, Grobal2.RC_MONSTER, 8, 8);
    visibleActor.m_sCharName = "move54-visible-actor";
    actor.m_VisibleHumanList.Add(visibleActor);
    actor.m_VisibleActors.Add(new TVisibleBaseObject
    {
        BaseObject = visibleActor,
        nVisibleFlag = 7
    });
    var item = new VisibleMapItem { nX = 8, nY = 8, nVisibleFlag = 9 };
    var mapEvent = new GameSvr.Event(null, 0, 0, 0, 1000, false);
    actor.m_VisibleItems.Add(item);
    actor.m_VisibleEvents.Add(mapEvent);
    return (item, mapEvent);
}

static void AssertNativeVisibilityState(TBaseObject actor,
    (VisibleMapItem Item, GameSvr.Event Event) sentinels, string label)
{
    Equal(0, actor.m_VisibleHumanList.Count,
        label + " did not clear visible humans");
    Equal(0, actor.m_VisibleActors.Count,
        label + " did not clear visible actors");
    Assert(actor.m_VisibleItems.Count == 1
           && ReferenceEquals(sentinels.Item, actor.m_VisibleItems[0]),
        label + " changed visible items");
    Assert(actor.m_VisibleEvents.Count == 1
           && ReferenceEquals(sentinels.Event, actor.m_VisibleEvents[0]),
        label + " changed visible events");
}

static void AssertActorPosition(TBaseObject actor, Envirnoment environment,
    short x, short y, string label)
{
    AssertActorFields(actor, environment, x, y, label);
    Assert(FindActorNode(environment, actor, x, y) != null,
        label + " has no node at the actor coordinates");
}

static void AssertActorFields(TBaseObject actor, Envirnoment environment,
    short x, short y, string label)
{
    Assert(ReferenceEquals(environment, actor.m_PEnvir),
        label + " changed environment identity");
    Equal(x, actor.m_nCurrX, label + " X");
    Equal(y, actor.m_nCurrY, label + " Y");
}

static void AssertNativeSpaceMoveMessage(TBaseObject actor, int start,
    int ident, short x, short y, string label)
{
    Equal(start + 1, actor.m_MsgList.Count,
        label + " message count");
    AssertNativeSpaceMovePayload(actor, start, ident, x, y, label);
}

static void AssertNativeSpaceMovePayload(TBaseObject actor, int index,
    int ident, short x, short y, string label)
{
    var message = actor.m_MsgList[index];
    Equal(ident, message.wIdent, label + " ident");
    Equal((int)actor.m_btDirection, message.wParam,
        label + " direction");
    Equal((int)x, message.nParam1, label + " X payload");
    Equal((int)y, message.nParam2, label + " Y payload");
    Equal(0, message.nParam3, label + " Param3");
    Assert(string.IsNullOrEmpty(message.Buff), label + " body");
}

static CellObject FindActorNode(Envirnoment environment, TBaseObject actor,
    int x, int y)
{
    return FindCellNode(environment, actor, CellType.OS_MOVINGOBJECT, x, y);
}

static CellObject FindCellNode(Envirnoment environment, object value,
    CellType type, int x, int y)
{
    var found = false;
    var cell = environment.GetMapCellInfo(x, y, ref found);
    if (!found || cell.ObjList == null)
        return null;
    return cell.ObjList.FirstOrDefault(node =>
        node.CellType == type && ReferenceEquals(node.CellObj, value));
}

static bool IsCellHead(Envirnoment environment, CellObject node, int x, int y)
{
    var found = false;
    var cell = environment.GetMapCellInfo(x, y, ref found);
    return found && cell.ObjList != null && cell.ObjList.Count > 0
           && ReferenceEquals(node, cell.ObjList[0]);
}

static CellObject UnlinkActorNodeWithoutRegistration(Envirnoment environment,
    TBaseObject actor, int x, int y)
{
    var found = false;
    var cell = environment.GetMapCellInfo(x, y, ref found);
    if (!found || cell.ObjList == null)
        return null;
    var node = cell.ObjList.FirstOrDefault(candidate =>
        candidate.CellType == CellType.OS_MOVINGOBJECT
        && ReferenceEquals(candidate.CellObj, actor));
    if (node != null)
        cell.ObjList.Remove(node);
    return node;
}

static CellObject InsertRawCellNode(Envirnoment environment, object value,
    CellType type, int x, int y)
{
    var marker = new object();
    Assert(ReferenceEquals(marker,
            environment.AddToMap(x, y, CellType.OS_GATEOBJECT, marker)),
        "raw-cell marker placement failed");
    var found = false;
    var cell = environment.GetMapCellInfo(x, y, ref found);
    Assert(found && cell.ObjList != null,
        "raw-cell target list was not initialized");
    var node = new CellObject
    {
        CellType = type,
        CellObj = value,
        dwAddTime = 0x45677654
    };
    cell.ObjList.Insert(0, node);
    return node;
}

static int CountActorNodes(Envirnoment environment, TBaseObject actor)
{
    var count = 0;
    for (var x = 0; x < environment.wWidth; x++)
    for (var y = 0; y < environment.wHeight; y++)
    {
        var found = false;
        var cell = environment.GetMapCellInfo(x, y, ref found);
        if (found && cell.ObjList != null)
        {
            count += cell.ObjList.Count(node =>
                node.CellType == CellType.OS_MOVINGOBJECT
                && ReferenceEquals(node.CellObj, actor));
        }
    }
    return count;
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

sealed class CountingZeroRandomNumber : RandomNumber
{
    public List<int> Bounds { get; } = new();

    public override int Random(int value)
    {
        Bounds.Add(value);
        return 0;
    }
}
