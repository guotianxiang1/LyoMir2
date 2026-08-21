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
M2Share.LogStringList = new System.Collections.ArrayList();
M2Share.g_MonSayMsgList = new Dictionary<string, IList<TMonSayMsg>>();

// sMapName 必须非空：SPWN-56 的有效性谓词第三项对应原生
// 0x765D85 `cmp dword [eax+0x44],0`（PEnvir.MapName <> ''），空名地图上的
// actor 会在格子链扫描时被判失效摘链，于是"玩家挡路"整个失效。
var environment = new Envirnoment { sMapName = "collision" };
typeof(Envirnoment).GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic)!
    .Invoke(environment, new object[] { (short)10, (short)10 });

var mover = NewObject(environment, Grobal2.RC_PLAYOBJECT, 1, 1);
var playerBlocker = NewObject(environment, Grobal2.RC_PLAYOBJECT, 2, 1);
Place(environment, mover);
Place(environment, playerBlocker);

Assert(!environment.CanWalkEx(2, 1, false), "players did not block running by default");
Assert(!environment.CanWalk(2, 1, false), "players did not block walking");
Equal(0, environment.MoveToMovingObjectForRun(1, 1, mover, 2, 1, false),
    "blocked run commit");
Equal(1, environment.GetXYObjCount(1, 1), "mover was removed after blocked commit");

M2Share.g_Config.boRunHuman = true;
Assert(environment.CanWalkEx(2, 1, false), "global RunHuman was ignored");
Assert(!environment.CanWalk(2, 1, false), "RunHuman incorrectly changed walking collision");
M2Share.g_Config.boRunHuman = false;
environment.Flag.boRUNHUMAN = true;
Assert(environment.CanWalkEx(2, 1, false), "map RUNHUMAN was ignored");
environment.Flag.boRUNHUMAN = false;

var monsterBlocker = NewObject(environment, Grobal2.RC_MONSTER, 3, 1);
Place(environment, monsterBlocker);
Assert(!environment.CanWalkEx(3, 1, false), "monsters did not block running by default");
M2Share.g_Config.boRunMon = true;
Assert(environment.CanWalkEx(3, 1, false), "global RunMon was ignored");
M2Share.g_Config.boRunMon = false;
environment.Flag.boRUNMON = true;
Assert(environment.CanWalkEx(3, 1, false), "map RUNMON was ignored");
environment.Flag.boRUNMON = false;

monsterBlocker.m_boDeath = true;
Assert(environment.CanWalkEx(3, 1, false), "dead monster still blocked running");
monsterBlocker.m_boDeath = false;

playerBlocker.m_boDeath = true;
Equal(1, environment.MoveToMovingObjectForRun(1, 1, mover, 2, 1, false),
    "run commit through dead object");
Equal(0, environment.GetXYObjCount(1, 1), "successful commit left mover at source");
var foundDestination = false;
var destination = environment.GetMapCellInfo(2, 1, ref foundDestination);
Assert(foundDestination, "destination cell was not found");
Equal(2, destination.Count, "successful commit did not add mover at destination");

var sourceMap = NewEnvironment("MoveRollbackSource", "SourceMapFile", 0);
var blockedTargetMap = NewEnvironment("MoveRollbackTarget", "TargetMapFile", 0);
for (var x = 0; x < blockedTargetMap.wWidth; x++)
{
    for (var y = 0; y < blockedTargetMap.wHeight; y++)
        blockedTargetMap.SetMapXYFlag(x, y, false);
}

var mapManager = new MapManager();
RegisterMap(mapManager, sourceMap);
RegisterMap(mapManager, blockedTargetMap);
M2Share.MapManager = mapManager;
M2Share.nServerIndex = 0;

long playerCountTick = 20_000;
var playerCountRooms = new NativeDynamicRoomManager(() => playerCountTick);
var playerCountSource = NewEnvironment("PlayerCountLifecycle", "PlayerCountLifecycleFile", 0);
var playerCountTarget = NewEnvironment("PlayerCountTarget", "PlayerCountTargetFile", 0);
RegisterMap(mapManager, playerCountSource);
RegisterMap(mapManager, playerCountTarget);
var playerCountCleanupCount = 0;
var playerCountTargetCleanupCount = 0;
Assert(playerCountRooms.RegisterIdleRoom("PlayerCountLifecycle", 0, playerCountSource, 0,
    _ =>
    {
        playerCountCleanupCount++;
        return true;
    }), "player-count dynamic room registration failed");
Assert(playerCountRooms.RegisterIdleRoom("PlayerCountTarget", 0, playerCountTarget, 0,
    _ =>
    {
        playerCountTargetCleanupCount++;
        return true;
    }), "player-count target dynamic room registration failed");
Assert(playerCountRooms.TryReserveIdleRoom("PlayerCountLifecycle", null,
    out var playerCountIndex),
    "player-count dynamic room was not reserved");
Assert(playerCountRooms.TryReserveIdleRoom("PlayerCountTarget", null,
    out _),
    "player-count target dynamic room was not reserved");

var playerCountPlayer = new TPlayObject
{
    m_PEnvir = playerCountSource,
    m_sMapName = playerCountSource.sMapName,
    m_sMapFileName = playerCountSource.m_sMapFileName,
    m_nCurrX = 3,
    m_nCurrY = 3,
    // sub_765D64 treats nameless actors as stale cell nodes. Production
    // players always have a character name, so every inline fixture must too.
    m_sCharName = "playerCountPlayer"
};
Assert(!playerCountPlayer.m_boAddToMaped && playerCountPlayer.m_boDelFormMaped,
    "new player did not start as unpublished");
playerCountPlayer.Initialize();
Assert(!playerCountPlayer.m_boAddtoMapSuccess,
    "new player was not published during Initialize");
Equal(1, playerCountSource.HumCount,
    "new player Initialize did not increment human count");
Equal(1, playerCountSource.DynamicRoomPlayerCount,
    "new player Initialize did not increment dynamic physical occupancy");
Assert(playerCountPlayer.m_boAddToMaped && !playerCountPlayer.m_boDelFormMaped,
    "new player Initialize did not publish map registration flags");

var playerClone = new TPlayCloneObject(playerCountPlayer);
Equal(Grobal2.RC_PLAYCLONE, (int)playerClone.m_btRaceServer,
    "player clone retained the real-player race");
Equal(1, playerCountSource.HumCount,
    "player clone incremented the human count");
Equal(1, playerCountSource.DynamicRoomPlayerCount,
    "player clone incremented dynamic physical occupancy");
Equal(1, playerCountSource.MonCount,
    "player clone did not use monster-class map accounting");
playerCountSource.DeleteFromMap(playerClone.m_nCurrX, playerClone.m_nCurrY,
    CellType.OS_MOVINGOBJECT, playerClone);
Equal(1, playerCountSource.HumCount,
    "player clone removal decremented the human count");
Equal(1, playerCountSource.DynamicRoomPlayerCount,
    "player clone removal decremented dynamic physical occupancy");
Equal(0, playerCountSource.MonCount,
    "player clone removal left monster-class map accounting");
Equal(0, playerCountCleanupCount,
    "player clone removal ran dynamic room cleanup");

playerCountTick += 120_001;
playerCountRooms.Run();
Assert(playerCountRooms.TryGetActiveRoom("PlayerCountLifecycle", playerCountIndex,
           out var activePlayerCountRoom)
       && ReferenceEquals(playerCountSource, activePlayerCountRoom),
    "occupied dynamic room closed during the empty-room timer");

playerCountPlayer.Die();
Assert(playerCountPlayer.m_boDeath, "player death did not set the death state");
Equal(1, playerCountSource.HumCount,
    "player death must not change legacy active-player accounting (death != logout; native Die 0x766351 calls map vmt+8 0x5FD4D4, which does not touch map+0xD8 HumCount)");
Equal(1, playerCountSource.DynamicRoomPlayerCount,
    "player death decremented dynamic physical occupancy");
Assert(playerCountRooms.TryGetActiveRoom("PlayerCountLifecycle", playerCountIndex,
           out activePlayerCountRoom)
       && ReferenceEquals(playerCountSource, activePlayerCountRoom),
    "player death closed an occupied dynamic room");
Equal(0, playerCountCleanupCount,
    "player death ran dynamic room cleanup");

typeof(TBaseObject).GetMethod("ReAlive", BindingFlags.Instance | BindingFlags.NonPublic)!
    .Invoke(playerCountPlayer, null);
Assert(!playerCountPlayer.m_boDeath, "player revive did not clear the death state");
Equal(1, playerCountSource.HumCount,
    "player revive must not change legacy active-player accounting");
Equal(1, playerCountSource.DynamicRoomPlayerCount,
    "player revive changed dynamic physical occupancy");

playerCountPlayer.Die();
playerCountTick++;
playerCountPlayer.SpaceMove(playerCountTarget.sMapName, 4, 4, 0);
Equal(0, playerCountSource.HumCount,
    "dead player map change must decrement source HumCount (DeleteFromMap, same as live transfer)");
Equal(1, playerCountTarget.HumCount,
    "dead player map change must increment target HumCount (AddToMap, death did not unpublish)");
Equal(0, playerCountSource.DynamicRoomPlayerCount,
    "dead player map change did not decrement source physical occupancy");
Equal(1, playerCountTarget.DynamicRoomPlayerCount,
    "dead player map change did not increment target physical occupancy");
Equal(1, playerCountCleanupCount,
    "player map change did not clean the source dynamic room once");
playerCountTarget.DeleteFromMap(4, 4, CellType.OS_MOVINGOBJECT, playerCountPlayer);
Equal(0, playerCountTarget.DynamicRoomPlayerCount,
    "dead player removal left a dynamic physical-occupancy leak");
Equal(1, playerCountTargetCleanupCount,
    "dead player removal did not clean the target dynamic room once");

var verifyMapSource = NewEnvironment("VerifyMapLifecycle", "VerifyMapLifecycleFile", 0);
RegisterMap(mapManager, verifyMapSource);
var verifyMapCleanupCount = 0;
Assert(playerCountRooms.RegisterIdleRoom("VerifyMapLifecycle", 0, verifyMapSource, 0,
    _ =>
    {
        verifyMapCleanupCount++;
        return true;
    }), "verify-map dynamic room registration failed");
Assert(playerCountRooms.TryReserveIdleRoom("VerifyMapLifecycle", null, out _),
    "verify-map dynamic room was not reserved");
var verifyMapPlayer = new TPlayObject
{
    m_PEnvir = verifyMapSource,
    m_sMapName = verifyMapSource.sMapName,
    m_sMapFileName = verifyMapSource.m_sMapFileName,
    m_nCurrX = 2,
    m_nCurrY = 2,
    m_sCharName = "verifyMapPlayer"
};
Place(verifyMapSource, verifyMapPlayer);
typeof(Envirnoment).GetMethod("ReleaseCellObjectList",
        BindingFlags.Instance | BindingFlags.NonPublic)!
    .Invoke(verifyMapSource, new object[] { 2, 2 });
Equal(0, GetCellObjectCount(verifyMapSource, 2, 2),
    "verify-map setup did not remove the stale cell entry");
Equal(1, verifyMapSource.DynamicRoomPlayerCount,
    "stale cell cleanup removed logical dynamic occupancy");
verifyMapSource.VerifyMapTime(2, 2, verifyMapPlayer);
Equal(1, GetCellObjectCount(verifyMapSource, 2, 2),
    "VerifyMapTime did not restore the player cell");
Equal(1, verifyMapSource.DynamicRoomPlayerCount,
    "VerifyMapTime duplicated dynamic physical occupancy");
playerCountTick++;
verifyMapSource.DeleteFromMap(2, 2, CellType.OS_MOVINGOBJECT, verifyMapPlayer);
Equal(0, verifyMapSource.DynamicRoomPlayerCount,
    "verified player removal left dynamic physical occupancy");
Equal(1, verifyMapCleanupCount,
    "verified player removal did not clean the dynamic room once");

var teleporting = NewObject(sourceMap, Grobal2.RC_MONSTER, 4, 4);
teleporting.m_sMapName = sourceMap.sMapName;
teleporting.m_sMapFileName = sourceMap.m_sMapFileName;
Place(sourceMap, teleporting);
teleporting.SpaceMove(blockedTargetMap.sMapName, 4, 4, 0);

Assert(ReferenceEquals(sourceMap, teleporting.m_PEnvir),
    "failed SpaceMove changed the environment pointer");
Equal("MoveRollbackTar", teleporting.m_sMapName,
    "failed native base SpaceMove did not retain the truncated target map name");
Equal(sourceMap.m_sMapFileName, teleporting.m_sMapFileName,
    "failed SpaceMove changed the map file name");
Equal((short)4, teleporting.m_nCurrX, "failed SpaceMove changed X");
Equal((short)4, teleporting.m_nCurrY, "failed SpaceMove changed Y");
Equal(1, sourceMap.GetXYObjCount(4, 4),
    "failed SpaceMove did not restore the source-map object");
Equal(1, sourceMap.MonCount, "failed SpaceMove changed source monster count");
Equal(0, blockedTargetMap.MonCount,
    "failed SpaceMove changed target monster count");
Assert(teleporting.m_boAddToMaped && !teleporting.m_boDelFormMaped,
    "failed SpaceMove did not restore map registration flags");

var gateTraveler = NewObject(sourceMap, Grobal2.RC_MONSTER, 5, 5);
gateTraveler.m_sMapName = sourceMap.sMapName;
gateTraveler.m_sMapFileName = sourceMap.m_sMapFileName;
Place(sourceMap, gateTraveler);
var gateObserver = NewObject(sourceMap, Grobal2.RC_PLAYOBJECT, 6, 5);
Place(sourceMap, gateObserver);
var gateChangeMapCount = CountMessages(gateTraveler, Grobal2.RM_CHANGEMAP);
var gateClearObjectsCount = CountMessages(gateTraveler, Grobal2.RM_CLEAROBJECTS);
var gateDisappearCount = CountMessages(gateObserver, Grobal2.RM_DISAPPEAR);
var enterAnotherMap = typeof(TBaseObject).GetMethod("EnterAnotherMap",
    BindingFlags.Instance | BindingFlags.NonPublic)!;
var gateMoveSucceeded = (bool)enterAnotherMap.Invoke(gateTraveler,
    new object[] { blockedTargetMap, 4, 4 })!;

Assert(!gateMoveSucceeded, "blocked gate move succeeded");
Assert(ReferenceEquals(sourceMap, gateTraveler.m_PEnvir),
    "failed gate move changed the environment pointer");
Equal(sourceMap.sMapName, gateTraveler.m_sMapName,
    "failed gate move changed the map name");
Equal(sourceMap.m_sMapFileName, gateTraveler.m_sMapFileName,
    "failed gate move changed the map file name");
Equal((short)5, gateTraveler.m_nCurrX, "failed gate move changed X");
Equal((short)5, gateTraveler.m_nCurrY, "failed gate move changed Y");
Equal(1, sourceMap.GetXYObjCount(5, 5),
    "failed gate move did not restore the source-map object");
Equal(2, sourceMap.MonCount, "failed gate move changed source monster count");
Equal(0, blockedTargetMap.MonCount,
    "failed gate move changed target monster count");
Assert(gateTraveler.m_boAddToMaped && !gateTraveler.m_boDelFormMaped,
    "failed gate move did not restore map registration flags");
Equal(gateChangeMapCount, CountMessages(gateTraveler, Grobal2.RM_CHANGEMAP),
    "failed gate move queued a map-change message");
Equal(gateClearObjectsCount, CountMessages(gateTraveler, Grobal2.RM_CLEAROBJECTS),
    "failed gate move queued a clear-objects message");
Equal(gateDisappearCount, CountMessages(gateObserver, Grobal2.RM_DISAPPEAR),
    "failed gate move sent a disappear message to a source observer");

var committedGateTarget = NewEnvironment("CommittedGateTarget",
    "CommittedGateTargetFile", 0);
var observerGateMoveSucceeded = (bool)enterAnotherMap.Invoke(gateTraveler,
    new object[] { committedGateTarget, 4, 4 })!;
Assert(observerGateMoveSucceeded, "valid gate move failed");
Assert(ReferenceEquals(committedGateTarget, gateTraveler.m_PEnvir),
    "valid gate move did not commit the target environment");
Equal(gateChangeMapCount + 1,
    CountMessages(gateTraveler, Grobal2.RM_CHANGEMAP),
    "committed gate move did not queue one map-change message");
Equal(gateClearObjectsCount + 1,
    CountMessages(gateTraveler, Grobal2.RM_CLEAROBJECTS),
    "committed gate move did not queue one clear-objects message");
Equal(gateDisappearCount + 1,
    CountMessages(gateObserver, Grobal2.RM_DISAPPEAR),
    "committed gate move did not notify the source observer once");

long dynamicTick = 10_000;
var dynamicRooms = new NativeDynamicRoomManager(() => dynamicTick);
var dynamicSource = NewEnvironment("MoveLifecycle", "MoveLifecycleFile", 0);
RegisterMap(mapManager, dynamicSource);
var dynamicPrepareCount = 0;
Assert(dynamicRooms.RegisterIdleRoom("MoveLifecycle", 0, dynamicSource, 0, _ =>
{
    dynamicPrepareCount++;
    return true;
}), "dynamic source registration failed");
Assert(dynamicRooms.TryReserveIdleRoom("MoveLifecycle", null, out _),
    "dynamic source was not reserved");

var dynamicPlayer = new TPlayObject
{
    m_PEnvir = dynamicSource,
    m_sMapName = dynamicSource.sMapName,
    m_sMapFileName = dynamicSource.m_sMapFileName,
    m_nCurrX = 3,
    m_nCurrY = 3,
    m_btRaceServer = Grobal2.RC_PLAYOBJECT,
    m_sCharName = "dynamicPlayer"
};
Place(dynamicSource, dynamicPlayer);
dynamicTick++;

dynamicPlayer.SpaceMove(blockedTargetMap.sMapName, 4, 4, 0);
Assert(dynamicRooms.TryGetActiveRoom("MoveLifecycle", dynamicSource.DynamicRoomIndex,
           out var activeDynamic)
       && ReferenceEquals(dynamicSource, activeDynamic),
    "failed dynamic-room SpaceMove closed the occupied room");
Equal(1, dynamicSource.HumCount,
    "failed dynamic-room SpaceMove changed source human count");
Equal(0, dynamicPrepareCount,
    "failed dynamic-room SpaceMove ran room cleanup");

dynamicPlayer.SpaceMove(dynamicSource.sMapName, 6, 6, 0);
Assert(dynamicRooms.TryGetActiveRoom("MoveLifecycle", dynamicSource.DynamicRoomIndex,
           out activeDynamic)
       && ReferenceEquals(dynamicSource, activeDynamic),
    "same-room SpaceMove closed the occupied room");
Equal(1, dynamicSource.HumCount,
    "same-room SpaceMove changed source human count");
Equal(0, dynamicPrepareCount,
    "same-room SpaceMove ran room cleanup");

var dynamicGateMoveSucceeded = (bool)enterAnotherMap.Invoke(dynamicPlayer,
    new object[] { blockedTargetMap, 4, 4 })!;
Assert(!dynamicGateMoveSucceeded, "blocked dynamic-room gate move succeeded");
Assert(dynamicRooms.TryGetActiveRoom("MoveLifecycle", dynamicSource.DynamicRoomIndex,
           out activeDynamic)
       && ReferenceEquals(dynamicSource, activeDynamic),
    "failed dynamic-room gate move closed the occupied room");
Equal(1, dynamicSource.HumCount,
    "failed dynamic-room gate move changed source human count");
Equal(0, dynamicPrepareCount,
    "failed dynamic-room gate move ran room cleanup");

var exceptionSource = NewEnvironment("ExceptionMoveLifecycle", "ExceptionMoveFile", 0);
RegisterMap(mapManager, exceptionSource);
var exceptionPrepareCount = 0;
Assert(dynamicRooms.RegisterIdleRoom("ExceptionMoveLifecycle", 0, exceptionSource, 0,
    _ =>
    {
        exceptionPrepareCount++;
        return true;
    }), "exception SpaceMove source registration failed");
Assert(dynamicRooms.TryReserveIdleRoom("ExceptionMoveLifecycle", null, out _),
    "exception SpaceMove source was not reserved");
var exceptionPlayer = new TPlayObject
{
    m_PEnvir = exceptionSource,
    m_sMapName = exceptionSource.sMapName,
    m_sMapFileName = exceptionSource.m_sMapFileName,
    m_nCurrX = 3,
    m_nCurrY = 3,
    m_sCharName = "exceptionPlayer"
};
Place(exceptionSource, exceptionPlayer);
var savedExceptionVisibleHumans = exceptionPlayer.m_VisibleHumanList;
exceptionPlayer.m_VisibleHumanList = null;
exceptionPlayer.SpaceMove(sourceMap.sMapName, 8, 8, 0);
exceptionPlayer.m_VisibleHumanList = savedExceptionVisibleHumans;
Assert(ReferenceEquals(exceptionSource, exceptionPlayer.m_PEnvir),
    "SpaceMove exception did not restore the source environment");
Equal((short)3, exceptionPlayer.m_nCurrX, "SpaceMove exception did not restore X");
Equal((short)3, exceptionPlayer.m_nCurrY, "SpaceMove exception did not restore Y");
Equal(1, GetCellObjectCount(exceptionSource, 3, 3),
    "SpaceMove exception did not restore the source cell");
Equal(1, exceptionSource.HumCount,
    "SpaceMove exception changed source human count");
Equal(1, exceptionSource.DynamicRoomPlayerCount,
    "SpaceMove exception changed source physical occupancy");
Assert(exceptionPlayer.m_boAddToMaped && !exceptionPlayer.m_boDelFormMaped,
    "SpaceMove exception did not restore map registration flags");
Assert(dynamicRooms.TryGetActiveRoom("ExceptionMoveLifecycle",
        exceptionSource.DynamicRoomIndex, out _),
    "SpaceMove exception closed the occupied dynamic room");
Equal(0, exceptionPrepareCount,
    "SpaceMove exception ran dynamic room cleanup");

var exceptionGateSource = NewEnvironment("ExceptionGateLifecycle", "ExceptionGateFile", 0);
RegisterMap(mapManager, exceptionGateSource);
var exceptionGatePrepareCount = 0;
Assert(dynamicRooms.RegisterIdleRoom("ExceptionGateLifecycle", 0, exceptionGateSource, 0,
    _ =>
    {
        exceptionGatePrepareCount++;
        return true;
    }), "exception gate source registration failed");
Assert(dynamicRooms.TryReserveIdleRoom("ExceptionGateLifecycle", null, out _),
    "exception gate source was not reserved");
var exceptionGatePlayer = new TPlayObject
{
    m_PEnvir = exceptionGateSource,
    m_sMapName = exceptionGateSource.sMapName,
    m_sMapFileName = exceptionGateSource.m_sMapFileName,
    m_nCurrX = 3,
    m_nCurrY = 3,
    m_sCharName = "exceptionGatePlayer"
};
Place(exceptionGateSource, exceptionGatePlayer);
var savedExceptionGateVisibleHumans = exceptionGatePlayer.m_VisibleHumanList;
exceptionGatePlayer.m_VisibleHumanList = null;
var exceptionGateMoveSucceeded = (bool)enterAnotherMap.Invoke(exceptionGatePlayer,
    new object[] { sourceMap, 7, 8 })!;
exceptionGatePlayer.m_VisibleHumanList = savedExceptionGateVisibleHumans;
Assert(!exceptionGateMoveSucceeded, "gate exception unexpectedly committed the move");
Assert(ReferenceEquals(exceptionGateSource, exceptionGatePlayer.m_PEnvir),
    "gate exception did not restore the source environment");
Equal((short)3, exceptionGatePlayer.m_nCurrX, "gate exception did not restore X");
Equal((short)3, exceptionGatePlayer.m_nCurrY, "gate exception did not restore Y");
Equal(1, GetCellObjectCount(exceptionGateSource, 3, 3),
    "gate exception did not restore the source cell");
Equal(1, exceptionGateSource.HumCount,
    "gate exception changed source human count");
Equal(1, exceptionGateSource.DynamicRoomPlayerCount,
    "gate exception changed source physical occupancy");
Assert(exceptionGatePlayer.m_boAddToMaped && !exceptionGatePlayer.m_boDelFormMaped,
    "gate exception did not restore map registration flags");
Assert(dynamicRooms.TryGetActiveRoom("ExceptionGateLifecycle",
        exceptionGateSource.DynamicRoomIndex, out _),
    "gate exception closed the occupied dynamic room");
Equal(0, exceptionGatePrepareCount,
    "gate exception ran dynamic room cleanup");

var committedTarget = NewEnvironment("CommittedTarget", "CommittedTargetFile", 0);
RegisterMap(mapManager, committedTarget);
dynamicPlayer.SpaceMove(committedTarget.sMapName, 4, 4, 0);
Assert(!dynamicRooms.TryGetActiveRoom("MoveLifecycle",
        dynamicSource.DynamicRoomIndex, out _),
    "successful dynamic-room SpaceMove left the source room active");
Equal(0, dynamicSource.HumCount,
    "successful dynamic-room SpaceMove did not remove the source player");
Equal(1, committedTarget.HumCount,
    "successful dynamic-room SpaceMove did not add the target player");
Equal(1, dynamicPrepareCount,
    "successful dynamic-room SpaceMove did not run source cleanup once");

var gateSource = NewEnvironment("GateLifecycle", "GateLifecycleFile", 0);
RegisterMap(mapManager, gateSource);
var gatePrepareCount = 0;
Assert(dynamicRooms.RegisterIdleRoom("GateLifecycle", 0, gateSource, 0, _ =>
{
    gatePrepareCount++;
    return true;
}), "dynamic gate source registration failed");
Assert(dynamicRooms.TryReserveIdleRoom("GateLifecycle", null, out _),
    "dynamic gate source was not reserved");

var dynamicGatePlayer = new TPlayObject
{
    m_PEnvir = gateSource,
    m_sMapName = gateSource.sMapName,
    m_sMapFileName = gateSource.m_sMapFileName,
    m_nCurrX = 3,
    m_nCurrY = 3,
    m_btRaceServer = Grobal2.RC_PLAYOBJECT,
    m_sCharName = "dynamicGatePlayer"
};
Place(gateSource, dynamicGatePlayer);
dynamicTick++;
var committedGateMoveSucceeded = (bool)enterAnotherMap.Invoke(dynamicGatePlayer,
    new object[] { committedTarget, 5, 5 })!;
Assert(committedGateMoveSucceeded, "valid dynamic-room gate move failed");
Assert(!dynamicRooms.TryGetActiveRoom("GateLifecycle",
        gateSource.DynamicRoomIndex, out _),
    "successful dynamic-room gate move left the source room active");
Equal(0, gateSource.HumCount,
    "successful dynamic-room gate move did not remove the source player");
Equal(2, committedTarget.HumCount,
    "successful dynamic-room gate move did not add the target player");
Equal(1, gatePrepareCount,
    "successful dynamic-room gate move did not run source cleanup once");

var crossServerTarget = NewEnvironment("CrossServerTarget", "CrossServerTargetFile", 1);
RegisterMap(mapManager, crossServerTarget);
var crossServerSource = NewEnvironment("CrossServerLifecycle", "CrossServerSourceFile", 0);
RegisterMap(mapManager, crossServerSource);
var crossServerCleanupCount = 0;
Assert(dynamicRooms.RegisterIdleRoom("CrossServerLifecycle", 0, crossServerSource, 0,
    _ =>
    {
        crossServerCleanupCount++;
        return true;
    }), "cross-server source registration failed");
Assert(dynamicRooms.TryReserveIdleRoom("CrossServerLifecycle", null, out _),
    "cross-server source was not reserved");
var crossServerPlayer = new TPlayObject
{
    m_PEnvir = crossServerSource,
    m_sMapName = crossServerSource.sMapName,
    m_sMapFileName = crossServerSource.m_sMapFileName,
    m_nCurrX = 3,
    m_nCurrY = 3,
    m_sCharName = "crossServerPlayer"
};
Place(crossServerSource, crossServerPlayer);
dynamicTick++;
crossServerPlayer.SpaceMove(crossServerTarget.sMapName, 4, 4, 0);
Equal(0, crossServerSource.HumCount,
    "cross-server move did not remove source human count");
Equal(0, crossServerSource.DynamicRoomPlayerCount,
    "cross-server move did not remove source physical occupancy");
Equal(0, GetCellObjectCount(crossServerSource, 3, 3),
    "cross-server move left the player in the source cell");
Assert(crossServerPlayer.m_boSwitchData && crossServerPlayer.m_boReconnection,
    "cross-server move did not set transfer state");
Equal(crossServerTarget.nServerIndex, crossServerPlayer.m_nServerIndex,
    "cross-server move did not select the target server");
Equal(1, crossServerCleanupCount,
    "cross-server move did not clean the source dynamic room once");

var ghostSource = NewEnvironment("GhostLifecycle", "GhostLifecycleFile", 0);
RegisterMap(mapManager, ghostSource);
var ghostCleanupCount = 0;
Assert(dynamicRooms.RegisterIdleRoom("GhostLifecycle", 0, ghostSource, 0,
    _ =>
    {
        ghostCleanupCount++;
        return true;
    }), "ghost source registration failed");
Assert(dynamicRooms.TryReserveIdleRoom("GhostLifecycle", null, out _),
    "ghost source was not reserved");
var ghostPlayer = new TPlayObject
{
    m_PEnvir = ghostSource,
    m_sMapName = ghostSource.sMapName,
    m_sMapFileName = ghostSource.m_sMapFileName,
    m_nCurrX = 3,
    m_nCurrY = 3,
    m_sCharName = "ghostPlayer"
};
Place(ghostSource, ghostPlayer);
dynamicTick++;
ghostPlayer.MakeGhost();
Assert(ghostPlayer.m_boGhost, "MakeGhost did not set the ghost state");
Equal(0, ghostSource.HumCount, "MakeGhost did not remove source human count");
Equal(0, ghostSource.DynamicRoomPlayerCount,
    "MakeGhost did not remove source physical occupancy");
Equal(0, GetCellObjectCount(ghostSource, 3, 3),
    "MakeGhost left the player in the source cell");
Equal(1, ghostCleanupCount,
    "MakeGhost did not clean the source dynamic room once");
ghostPlayer.MakeGhost();
Equal(1, ghostCleanupCount,
    "repeated MakeGhost cleaned the dynamic room more than once");

var failedCrossServerSource = NewEnvironment("CrossServerDeleteFailure",
    "CrossServerDeleteFailureFile", 0);
RegisterMap(mapManager, failedCrossServerSource);
var failedCrossServerCleanupCount = 0;
Assert(dynamicRooms.RegisterIdleRoom("CrossServerDeleteFailure", 0,
    failedCrossServerSource, 0, _ =>
    {
        failedCrossServerCleanupCount++;
        return true;
    }), "cross-server delete-failure source registration failed");
Assert(dynamicRooms.TryReserveIdleRoom("CrossServerDeleteFailure", null,
           out _),
    "cross-server delete-failure source was not reserved");
var failedCrossServerPlayer = new TPlayObject
{
    m_PEnvir = failedCrossServerSource,
    m_sMapName = failedCrossServerSource.sMapName,
    m_sMapFileName = failedCrossServerSource.m_sMapFileName,
    m_nCurrX = 3,
    m_nCurrY = 3,
    m_sCharName = "failedCrossServerPlayer"
};
Place(failedCrossServerSource, failedCrossServerPlayer);
dynamicTick++;
RemoveCellObjectWithoutAccounting(failedCrossServerSource, 3, 3,
    CellType.OS_MOVINGOBJECT, failedCrossServerPlayer);
var failedCrossServerDisappearCount = CountMessages(failedCrossServerPlayer,
    Grobal2.RM_DISAPPEAR);
var failedCrossServerTargetHumCount = crossServerTarget.HumCount;
var failedCrossServerInitialServerIndex = failedCrossServerPlayer.m_nServerIndex;
failedCrossServerPlayer.SpaceMove(crossServerTarget.sMapName, 4, 4, 0);
Assert(ReferenceEquals(failedCrossServerSource,
        failedCrossServerPlayer.m_PEnvir),
    "cross-server delete failure changed the environment pointer");
Equal(failedCrossServerSource.sMapName, failedCrossServerPlayer.m_sMapName,
    "cross-server delete failure changed the map name");
Equal(failedCrossServerSource.m_sMapFileName,
    failedCrossServerPlayer.m_sMapFileName,
    "cross-server delete failure changed the map file name");
Equal((short)3, failedCrossServerPlayer.m_nCurrX,
    "cross-server delete failure changed X");
Equal((short)3, failedCrossServerPlayer.m_nCurrY,
    "cross-server delete failure changed Y");
Assert(!failedCrossServerPlayer.m_bo316
       && !failedCrossServerPlayer.m_boSwitchData
       && !failedCrossServerPlayer.m_boEmergencyClose
       && !failedCrossServerPlayer.m_boReconnection,
    "cross-server delete failure changed transfer flags");
Equal(string.Empty, failedCrossServerPlayer.m_sSwitchMapName,
    "cross-server delete failure changed the switch map");
Equal((short)0, failedCrossServerPlayer.m_nSwitchMapX,
    "cross-server delete failure changed switch X");
Equal((short)0, failedCrossServerPlayer.m_nSwitchMapY,
    "cross-server delete failure changed switch Y");
Equal(failedCrossServerInitialServerIndex,
    failedCrossServerPlayer.m_nServerIndex,
    "cross-server delete failure changed the target server");
Equal(failedCrossServerDisappearCount,
    CountMessages(failedCrossServerPlayer, Grobal2.RM_DISAPPEAR),
    "cross-server delete failure queued a disappear message");
Equal(1, failedCrossServerSource.HumCount,
    "cross-server delete failure changed source human count");
Equal(1, failedCrossServerSource.DynamicRoomPlayerCount,
    "cross-server delete failure changed source physical occupancy");
Equal(failedCrossServerTargetHumCount, crossServerTarget.HumCount,
    "cross-server delete failure changed target human count");
Assert(failedCrossServerPlayer.m_boAddToMaped
       && !failedCrossServerPlayer.m_boDelFormMaped,
    "cross-server delete failure changed map registration flags");
Assert(dynamicRooms.TryGetActiveRoom("CrossServerDeleteFailure",
        failedCrossServerSource.DynamicRoomIndex, out _),
    "cross-server delete failure closed the occupied dynamic room");
Equal(0, failedCrossServerCleanupCount,
    "cross-server delete failure ran dynamic room cleanup");

var walk = typeof(TBaseObject).GetMethod("Walk",
    BindingFlags.Instance | BindingFlags.NonPublic)!;
var crossServerGateSuccessSource = NewEnvironment("CrossServerGateSuccess",
    "CrossServerGateSuccessFile", 0);
RegisterMap(mapManager, crossServerGateSuccessSource);
var crossServerGateSuccessObserver = NewObject(crossServerGateSuccessSource,
    Grobal2.RC_PLAYOBJECT, 3, 3);
Place(crossServerGateSuccessSource, crossServerGateSuccessObserver);
var crossServerGateSuccessPlayer = new TPlayObject
{
    m_PEnvir = crossServerGateSuccessSource,
    m_sMapName = crossServerGateSuccessSource.sMapName,
    m_sMapFileName = crossServerGateSuccessSource.m_sMapFileName,
    m_nCurrX = 3,
    m_nCurrY = 3,
    m_boFixedHideMode = false,
    m_sCharName = "crossServerGateSuccessPlayer"
};
Place(crossServerGateSuccessSource, crossServerGateSuccessPlayer);
var crossServerGateSuccess = new TGateObj
{
    DEnvir = crossServerTarget,
    // sub_78FE80 is `mov al,1; ret` in this image. A gate therefore always
    // calls EnterAnotherMap even when the target carries a remote server
    // index. Use an out-of-bounds landing to make that local call fail before
    // it changes either map, and verify that the mover ignores its return.
    nDMapX = 99,
    nDMapY = 99
};
Assert(ReferenceEquals(crossServerGateSuccess,
        crossServerGateSuccessSource.AddToMap(3, 3,
            CellType.OS_GATEOBJECT, crossServerGateSuccess)),
    "cross-server success gate placement failed");
var crossServerGateSuccessDisappearCount = CountMessages(
    crossServerGateSuccessObserver, Grobal2.RM_DISAPPEAR);
var crossServerGateSuccessWalkCount = CountMessages(
    crossServerGateSuccessObserver, Grobal2.RM_WALK);
var crossServerGateSuccessTargetHumCount = crossServerTarget.HumCount;
var crossServerGateSuccessServerIndex =
    crossServerGateSuccessPlayer.m_nServerIndex;
var crossServerGateWalkSucceeded = (bool)walk.Invoke(
    crossServerGateSuccessPlayer, new object[] { Grobal2.RM_WALK })!;
Assert(crossServerGateWalkSucceeded,
    "remote-index gate with failed local landing rejected the completed walk");
Assert(!crossServerGateSuccessPlayer.m_bo316
       && !crossServerGateSuccessPlayer.m_boSwitchData
       && !crossServerGateSuccessPlayer.m_boEmergencyClose
       && !crossServerGateSuccessPlayer.m_boReconnection,
    "remote-index gate incorrectly entered cross-server transfer state");
Equal(string.Empty, crossServerGateSuccessPlayer.m_sSwitchMapName,
    "remote-index gate changed the switch map");
Equal((short)0, crossServerGateSuccessPlayer.m_nSwitchMapX,
    "remote-index gate changed switch X");
Equal((short)0, crossServerGateSuccessPlayer.m_nSwitchMapY,
    "remote-index gate changed switch Y");
Equal(crossServerGateSuccessServerIndex,
    crossServerGateSuccessPlayer.m_nServerIndex,
    "remote-index gate changed the target server");
Assert(ReferenceEquals(crossServerGateSuccessSource,
        crossServerGateSuccessPlayer.m_PEnvir),
    "failed local gate landing changed the source environment");
Equal((short)3, crossServerGateSuccessPlayer.m_nCurrX,
    "failed local gate landing changed source X");
Equal((short)3, crossServerGateSuccessPlayer.m_nCurrY,
    "failed local gate landing changed source Y");
Equal(2, crossServerGateSuccessSource.HumCount,
    "failed local gate landing changed source human count");
Equal(crossServerGateSuccessTargetHumCount, crossServerTarget.HumCount,
    "failed local gate landing changed target human count");
Equal(crossServerGateSuccessDisappearCount,
    CountMessages(crossServerGateSuccessObserver, Grobal2.RM_DISAPPEAR),
    "failed local gate landing queued a disappear message");
Equal(crossServerGateSuccessWalkCount + 1,
    CountMessages(crossServerGateSuccessObserver, Grobal2.RM_WALK),
    "remote-index gate did not preserve native broadcast-before-gate order");

var crossServerGateFailureSource = NewEnvironment("CrossServerGateFailure",
    "CrossServerGateFailureFile", 0);
RegisterMap(mapManager, crossServerGateFailureSource);
var crossServerGateFailureObserver = NewObject(crossServerGateFailureSource,
    Grobal2.RC_PLAYOBJECT, 3, 3);
Place(crossServerGateFailureSource, crossServerGateFailureObserver);
var crossServerGateFailurePlayer = new TPlayObject
{
    m_PEnvir = crossServerGateFailureSource,
    m_sMapName = crossServerGateFailureSource.sMapName,
    m_sMapFileName = crossServerGateFailureSource.m_sMapFileName,
    m_nCurrX = 3,
    m_nCurrY = 3,
    m_boFixedHideMode = false,
    m_sCharName = "crossServerGateFailurePlayer"
};
Place(crossServerGateFailureSource, crossServerGateFailurePlayer);
var crossServerGateFailure = new TGateObj
{
    DEnvir = crossServerTarget,
    nDMapX = 4,
    nDMapY = 4
};
Assert(ReferenceEquals(crossServerGateFailure,
        crossServerGateFailureSource.AddToMap(3, 3,
            CellType.OS_GATEOBJECT, crossServerGateFailure)),
    "cross-server failure gate placement failed");
RemoveCellObjectWithoutAccounting(crossServerGateFailureSource, 3, 3,
    CellType.OS_MOVINGOBJECT, crossServerGateFailurePlayer);
var crossServerGateFailureDisappearCount = CountMessages(
    crossServerGateFailureObserver, Grobal2.RM_DISAPPEAR);
var crossServerGateFailureWalkCount = CountMessages(
    crossServerGateFailureObserver, Grobal2.RM_WALK);
var crossServerGateFailureServerIndex =
    crossServerGateFailurePlayer.m_nServerIndex;
var crossServerGateWalkContinued = (bool)walk.Invoke(
    crossServerGateFailurePlayer, new object[] { Grobal2.RM_WALK })!;
Assert(crossServerGateWalkContinued,
    "cross-server gate delete failure rejected the completed walk step");
Assert(!crossServerGateFailurePlayer.m_bo316
       && !crossServerGateFailurePlayer.m_boSwitchData
       && !crossServerGateFailurePlayer.m_boEmergencyClose
       && !crossServerGateFailurePlayer.m_boReconnection,
    "cross-server gate delete failure changed transfer flags");
Equal(string.Empty, crossServerGateFailurePlayer.m_sSwitchMapName,
    "cross-server gate delete failure changed the switch map");
Equal((short)0, crossServerGateFailurePlayer.m_nSwitchMapX,
    "cross-server gate delete failure changed switch X");
Equal((short)0, crossServerGateFailurePlayer.m_nSwitchMapY,
    "cross-server gate delete failure changed switch Y");
Equal(crossServerGateFailureServerIndex,
    crossServerGateFailurePlayer.m_nServerIndex,
    "cross-server gate delete failure changed the target server");
Equal(crossServerGateFailureDisappearCount,
    CountMessages(crossServerGateFailureObserver, Grobal2.RM_DISAPPEAR),
    "cross-server gate delete failure queued a disappear message");
Equal(crossServerGateFailureWalkCount + 1,
    CountMessages(crossServerGateFailureObserver, Grobal2.RM_WALK),
    "cross-server gate delete failure did not retain one walk message");
Equal(2, crossServerGateFailureSource.HumCount,
    "cross-server gate delete failure changed source human count");
Assert(crossServerGateFailurePlayer.m_boAddToMaped
       && !crossServerGateFailurePlayer.m_boDelFormMaped,
    "cross-server gate delete failure changed map registration flags");

var noHorseBlockedSource = NewEnvironment("NoHorseBlockedSource",
    "NoHorseBlockedSourceFile", 0);
var noHorseBlockedPlayer = new TPlayObject
{
    m_PEnvir = noHorseBlockedSource,
    m_sMapName = noHorseBlockedSource.sMapName,
    m_sMapFileName = noHorseBlockedSource.m_sMapFileName,
    m_nCurrX = 3,
    m_nCurrY = 3,
    m_boOnHorse = true,
    m_sCharName = "noHorseBlockedPlayer"
};
Place(noHorseBlockedSource, noHorseBlockedPlayer);
blockedTargetMap.Flag.boNOHORSE = true;
var noHorseBlockedMoveSucceeded = (bool)enterAnotherMap.Invoke(
    noHorseBlockedPlayer, new object[] { blockedTargetMap, 4, 4 })!;
blockedTargetMap.Flag.boNOHORSE = false;
Assert(!noHorseBlockedMoveSucceeded,
    "blocked NOHORSE move unexpectedly committed");
Assert(noHorseBlockedPlayer.m_boOnHorse,
    "blocked NOHORSE move dismounted the player");
Assert(ReferenceEquals(noHorseBlockedSource, noHorseBlockedPlayer.m_PEnvir)
       && CellContains(noHorseBlockedSource, 3, 3, noHorseBlockedPlayer),
    "blocked NOHORSE move did not restore the source placement");

var noHorseExceptionSource = NewEnvironment("NoHorseExceptionSource",
    "NoHorseExceptionSourceFile", 0);
var noHorseExceptionTarget = NewEnvironment("NoHorseExceptionTarget",
    "NoHorseExceptionTargetFile", 0);
noHorseExceptionTarget.Flag.boNOHORSE = true;
var noHorseExceptionPlayer = new TPlayObject
{
    m_PEnvir = noHorseExceptionSource,
    m_sMapName = noHorseExceptionSource.sMapName,
    m_sMapFileName = noHorseExceptionSource.m_sMapFileName,
    m_nCurrX = 3,
    m_nCurrY = 3,
    m_boOnHorse = true,
    m_sCharName = "noHorseExceptionPlayer"
};
Place(noHorseExceptionSource, noHorseExceptionPlayer);
var noHorseVisibleSentinel = NewObject(noHorseExceptionSource,
    Grobal2.RC_MONSTER, 4, 3);
noHorseExceptionPlayer.m_VisibleHumanList.Add(noHorseVisibleSentinel);
var savedNoHorseVisibleItems = noHorseExceptionPlayer.m_VisibleItems;
noHorseExceptionPlayer.m_VisibleItems = null;
var noHorseExceptionMoveSucceeded = (bool)enterAnotherMap.Invoke(
    noHorseExceptionPlayer, new object[] { noHorseExceptionTarget, 4, 4 })!;
noHorseExceptionPlayer.m_VisibleItems = savedNoHorseVisibleItems;
Assert(!noHorseExceptionMoveSucceeded,
    "exceptional NOHORSE move unexpectedly committed");
Assert(noHorseExceptionPlayer.m_boOnHorse,
    "exceptional NOHORSE move dismounted the player");
Assert(ReferenceEquals(noHorseExceptionSource,
        noHorseExceptionPlayer.m_PEnvir)
       && CellContains(noHorseExceptionSource, 3, 3,
           noHorseExceptionPlayer),
    "exceptional NOHORSE move did not restore the source placement");
Assert(noHorseExceptionPlayer.m_VisibleHumanList.Contains(
        noHorseVisibleSentinel),
    "exceptional NOHORSE move did not restore visible humans");

var noHorseSuccessSource = NewEnvironment("NoHorseSuccessSource",
    "NoHorseSuccessSourceFile", 0);
var noHorseSuccessTarget = NewEnvironment("NoHorseSuccessTarget",
    "NoHorseSuccessTargetFile", 0);
noHorseSuccessTarget.Flag.boNOHORSE = true;
var noHorseSuccessPlayer = new TPlayObject
{
    m_PEnvir = noHorseSuccessSource,
    m_sMapName = noHorseSuccessSource.sMapName,
    m_sMapFileName = noHorseSuccessSource.m_sMapFileName,
    m_nCurrX = 3,
    m_nCurrY = 3,
    m_boOnHorse = true,
    m_sCharName = "noHorseSuccessPlayer"
};
Place(noHorseSuccessSource, noHorseSuccessPlayer);
var noHorseSuccessMoveSucceeded = (bool)enterAnotherMap.Invoke(
    noHorseSuccessPlayer, new object[] { noHorseSuccessTarget, 4, 4 })!;
Assert(noHorseSuccessMoveSucceeded,
    "valid NOHORSE move did not commit");
Assert(!noHorseSuccessPlayer.m_boOnHorse,
    "committed NOHORSE move left the player mounted");
Assert(ReferenceEquals(noHorseSuccessTarget, noHorseSuccessPlayer.m_PEnvir)
       && CellContains(noHorseSuccessTarget, 4, 4, noHorseSuccessPlayer),
    "committed NOHORSE move did not attach to the target");

var horseAllowedSource = NewEnvironment("HorseAllowedSource",
    "HorseAllowedSourceFile", 0);
var horseAllowedTarget = NewEnvironment("HorseAllowedTarget",
    "HorseAllowedTargetFile", 0);
var horseAllowedPlayer = new TPlayObject
{
    m_PEnvir = horseAllowedSource,
    m_sMapName = horseAllowedSource.sMapName,
    m_sMapFileName = horseAllowedSource.m_sMapFileName,
    m_nCurrX = 3,
    m_nCurrY = 3,
    m_boOnHorse = true,
    m_sCharName = "horseAllowedPlayer"
};
Place(horseAllowedSource, horseAllowedPlayer);
var horseAllowedMoveSucceeded = (bool)enterAnotherMap.Invoke(
    horseAllowedPlayer, new object[] { horseAllowedTarget, 4, 4 })!;
Assert(horseAllowedMoveSucceeded,
    "valid horse-allowed move did not commit");
Assert(horseAllowedPlayer.m_boOnHorse,
    "horse-allowed move dismounted the player");
Assert(ReferenceEquals(horseAllowedTarget, horseAllowedPlayer.m_PEnvir)
       && CellContains(horseAllowedTarget, 4, 4, horseAllowedPlayer),
    "horse-allowed move did not attach to the target");

var staleGhostSource = NewEnvironment("StaleGhostLifecycle",
    "StaleGhostLifecycleFile", 0);
RegisterMap(mapManager, staleGhostSource);
var staleGhostCleanupCount = 0;
Assert(dynamicRooms.RegisterIdleRoom("StaleGhostLifecycle", 0,
    staleGhostSource, 0, _ =>
    {
        staleGhostCleanupCount++;
        return true;
    }), "stale ghost source registration failed");
Assert(dynamicRooms.TryReserveIdleRoom("StaleGhostLifecycle", null,
           out _),
    "stale ghost source was not reserved");
var staleGhostPlayer = new TPlayObject
{
    m_PEnvir = staleGhostSource,
    m_sMapName = staleGhostSource.sMapName,
    m_sMapFileName = staleGhostSource.m_sMapFileName,
    m_nCurrX = 3,
    m_nCurrY = 3,
    m_sCharName = "staleGhostPlayer"
};
Place(staleGhostSource, staleGhostPlayer);
dynamicTick++;
RemoveCellObjectWithoutAccounting(staleGhostSource, 3, 3,
    CellType.OS_MOVINGOBJECT, staleGhostPlayer);
Equal(1, staleGhostSource.HumCount,
    "stale ghost setup lost source human registration");
Equal(1, staleGhostSource.DynamicRoomPlayerCount,
    "stale ghost setup lost physical occupancy registration");
staleGhostPlayer.MakeGhost();
Assert(staleGhostPlayer.m_boGhost,
    "stale-cell MakeGhost did not set the ghost state");
Equal(0, staleGhostSource.HumCount,
    "stale-cell MakeGhost did not remove source human registration");
Equal(0, staleGhostSource.DynamicRoomPlayerCount,
    "stale-cell MakeGhost did not remove physical occupancy registration");
Assert(!staleGhostPlayer.m_boAddToMaped
       && staleGhostPlayer.m_boDelFormMaped,
    "stale-cell MakeGhost did not update map registration flags");
Equal(1, staleGhostCleanupCount,
    "stale-cell MakeGhost did not clean the dynamic room once");
staleGhostPlayer.MakeGhost();
Equal(0, staleGhostSource.HumCount,
    "repeated stale-cell MakeGhost underflowed source human count");
Equal(0, staleGhostSource.DynamicRoomPlayerCount,
    "repeated stale-cell MakeGhost changed physical occupancy");
Equal(1, staleGhostCleanupCount,
    "repeated stale-cell MakeGhost cleaned the dynamic room again");

// MOVE-34 — the native cell+2 (LinkPoint) marker. cell+2 is written 1:1 with the
// OS_GATEOBJECT node by the loader sub_779328 (@0x7793D4 `mov byte [cell+2],1`),
// so the C# reader scans a cell's object list for a gate. Three readers:
//   * drop/placement avoidance   — sub_778DB0 @0x778DEF  (GetItemEx / GetItem)
//   * player gate-step teleport  — sub_778E48 @0x778F93  (TBaseObject.Walk)
//   * walk-mover creature block  — sub_7797CC @0x7799D5/@0x7799DE (this check)
// The walk-mover gate rejects a creature (Cert+0x178 != 0, =0x32 for TCreature
// @0x764E5F, =0 for TPlayer @0x6AD76F) and fires even with boFlag set because it
// sits after the boFlag short-circuit @0x779874. Players teleport instead.
var linkPointGate = new TGateObj { DEnvir = environment, nDMapX = 7, nDMapY = 7 };
Assert(ReferenceEquals(linkPointGate,
        environment.AddToMap(7, 7, CellType.OS_GATEOBJECT, linkPointGate)),
    "MOVE-34 LinkPoint gate placement failed");

var linkPointDropCount = 0;
environment.GetItemEx(7, 7, ref linkPointDropCount);
Assert(!environment.bo2C,
    "MOVE-34 drop/placement scan treated a LinkPoint cell as usable");

var linkPointMonster = NewObject(environment, Grobal2.RC_MONSTER, 7, 6);
Place(environment, linkPointMonster);
Equal(0, environment.MoveToMovingObject(7, 6, linkPointMonster, 7, 7, false),
    "MOVE-34 monster was allowed onto a LinkPoint cell");
Equal(0, environment.MoveToMovingObject(7, 6, linkPointMonster, 7, 7, true),
    "MOVE-34 monster crossed a LinkPoint cell when boFlag was set");
Equal(1, GetCellObjectCount(environment, 7, 6),
    "MOVE-34 blocked monster was unlinked from its source cell");
Assert(!CellContains(environment, 7, 7, linkPointMonster),
    "MOVE-34 blocked monster was committed onto the LinkPoint cell");

var linkPointPlayer = NewObject(environment, Grobal2.RC_PLAYOBJECT, 7, 8);
Place(environment, linkPointPlayer);
Equal(1, environment.MoveToMovingObject(7, 8, linkPointPlayer, 7, 7, false),
    "MOVE-34 player was blocked by a LinkPoint cell");
Assert(CellContains(environment, 7, 7, linkPointPlayer),
    "MOVE-34 player did not move onto the LinkPoint cell");

AssertSwitchReentryUsesNativePlacement();

Console.WriteLine("MovementCollisionCheck PASS");

static TBaseObject NewObject(Envirnoment environment, byte race, short x, short y)
{
    return new TBaseObject
    {
        m_PEnvir = environment,
        m_btRaceServer = race,
        m_nCurrX = x,
        m_nCurrY = y,
        // SPWN-56 的有效性谓词（原生 sub_765D64）要求 Length(CName)>0，否则
        // 该 actor 会在格子链扫描时被判失效并摘链。原生 actor 一律带名字
        // （怪物取自 mongen、玩家取自角色记录），无名 actor 是夹具特有的失真态。
        m_sCharName = "probe-" + race + "-" + x + "-" + y
    };
}

static void Place(Envirnoment environment, TBaseObject actor)
{
    actor.m_boAddToMaped = false;
    actor.m_boDelFormMaped = false;
    Assert(ReferenceEquals(actor, environment.AddToMap(actor.m_nCurrX,
        actor.m_nCurrY, CellType.OS_MOVINGOBJECT, actor)), "place actor");
}

static int GetCellObjectCount(Envirnoment environment, int x, int y)
{
    var found = false;
    return environment.GetMapCellInfo(x, y, ref found).Count;
}

static bool CellContains(Envirnoment environment, int x, int y,
    object target)
{
    var found = false;
    var cell = environment.GetMapCellInfo(x, y, ref found);
    return found && cell.ObjList != null && cell.ObjList.Any(entry =>
        ReferenceEquals(entry.CellObj, target));
}

static void RemoveCellObjectWithoutAccounting(Envirnoment environment,
    int x, int y, CellType cellType, object target)
{
    var found = false;
    var cell = environment.GetMapCellInfo(x, y, ref found);
    Assert(found && cell.ObjList != null,
        "stale-cell setup could not find the source cell");
    var index = -1;
    for (var i = 0; i < cell.ObjList.Count; i++)
    {
        var entry = cell.ObjList[i];
        if (entry.CellType == cellType && ReferenceEquals(entry.CellObj, target))
        {
            index = i;
            break;
        }
    }
    Assert(index >= 0, "stale-cell setup could not find the target object");
    cell.ObjList.RemoveAt(index);
}

static int CountMessages(TBaseObject actor, int ident) =>
    actor.m_MsgList.Count(message => message.wIdent == ident);

static Envirnoment NewEnvironment(string mapName, string mapFileName,
    int serverIndex)
{
    var environment = new Envirnoment
    {
        sMapName = mapName,
        m_sMapFileName = mapFileName,
        nServerIndex = serverIndex
    };
    typeof(Envirnoment).GetMethod("Initialize", BindingFlags.Instance |
        BindingFlags.NonPublic)!.Invoke(environment, new object[] { (short)10, (short)10 });
    return environment;
}

static void RegisterMap(MapManager manager, Envirnoment environment)
{
    var field = typeof(MapManager).GetField("m_MapList",
        BindingFlags.Instance | BindingFlags.NonPublic)!;
    var maps = (IDictionary<string, Envirnoment>)field.GetValue(manager)!;
    maps.Add(environment.sMapName, environment);
}

static void AssertSwitchReentryUsesNativePlacement()
{
    var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "GameSvr",
        "UsrSystem", "UsrEngn.cs"));
    const string startMarker = "GetHumData(PlayObject, ref UserOpenInfo.HumanRcd);";
    const string endMarker = "PlayObject.AbilCopyToWAbil();";
    var start = source.LastIndexOf(startMarker, StringComparison.Ordinal);
    var end = start < 0 ? -1 : source.IndexOf(endMarker, start,
        StringComparison.Ordinal);
    Assert(start >= 0 && end > start,
        "MOVE-57 switch/re-entry branch could not be located");

    var block = source[start..end].Replace("\r", string.Empty,
        StringComparison.Ordinal);
    var nativeLookup = block.IndexOf(
        "TBaseObject.NativeGetRandomXY(Envir, ref nSwitchX, ref nSwitchY)",
        StringComparison.Ordinal);
    var writeX = block.IndexOf(
        "PlayObject.m_nCurrX = unchecked((short)nSwitchX);",
        StringComparison.Ordinal);
    var writeY = block.IndexOf(
        "PlayObject.m_nCurrY = unchecked((short)nSwitchY);",
        StringComparison.Ordinal);
    var failureGate = block.IndexOf("}\n                    else\n                    {",
        StringComparison.Ordinal);
    var fallback = block.IndexOf("sChangeServerFail4", StringComparison.Ordinal);
    Assert(nativeLookup >= 0,
        "MOVE-57 switch/re-entry path does not call NativeGetRandomXY");
    Assert(writeX > nativeLookup && writeY > nativeLookup,
        "MOVE-57 switch/re-entry path does not retain the native coordinates");
    Assert(failureGate > writeX && failureGate > writeY
        && fallback > failureGate,
        "MOVE-57 switch/re-entry fallback is not gated by native lookup failure");
    Assert(!block.Contains("if (!Envir.CanWalk(", StringComparison.Ordinal),
        "MOVE-57 switch/re-entry path still uses the non-native CanWalk shortcut");
}

static string FindRepositoryRoot()
{
    foreach (var startPath in new[]
             {
                 Directory.GetCurrentDirectory(),
                 AppContext.BaseDirectory
             })
    {
        for (var directory = new DirectoryInfo(startPath);
             directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameSvr",
                    "UsrSystem", "UsrEngn.cs"))
                && Directory.Exists(Path.Combine(directory.FullName,
                    "SystemModule")))
                return directory.FullName;
        }
    }

    throw new DirectoryNotFoundException("repository root not found");
}

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
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
