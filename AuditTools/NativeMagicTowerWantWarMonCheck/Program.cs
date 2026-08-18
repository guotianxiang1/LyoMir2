using System.Collections;
using System.Reflection;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

try
{
    PrepareRuntimeConfig();
    PrepareRuntime();

    CheckExactStateParsingAndRandomOrder();
    CheckNonPhaseThreeOnlyCloses();
    CheckDefaultAndNonPositiveCounts();
    CheckNativeIntegerSyntax();
    CheckStrictBridgeAbiAndExplicitPlayer();
    CheckPhysicalEnvironmentProductionSpawn();
    CheckNativeOrdinaryPlacement();
    CheckDeferredQueueFifoAndRetryBudget();
    CheckNativeRuntimeMonsterSchedulingAndCleanup();
    CheckDeferredQueueGenerationAndStopCleanup();
    CheckSourceContract();

    Console.WriteLine(
        "PASS NativeMagicTowerWantWarMonCheck " +
        "state=phase3-to4/other-display-close " +
        "D10=first-slash+first-colon/Delphi-int/default-count1 " +
        "rng=one-Random(5)-per-mon/same-xy-offset " +
        "spawn=player-physical-environment/ordinary-exact-position " +
        "deferred=FIFO/same-original-path/5-per-round/6-failures " +
        "runtime=Archer+Challenge+War-Run/ghost-clean/slot-reuse " +
        "lifecycle=dynamic-generation/Stop-clear " +
        "message=unconditional-10127 " +
        "abi=strict-procedure(explicit-player)-Nil/function-closed");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        "NativeMagicTowerWantWarMonCheck FAIL: " + exception);
    return 1;
}

static void CheckExactStateParsingAndRandomOrder()
{
    var environment = NewEnvironment("controlled-instance");
    var player = new TPlayObject
    {
        m_PEnvir = environment,
        m_btNativeMagicTowerPhase = 3,
        m_sNativeMagicTowerChallengeMonsters =
            "FirstMonster:2/SecondMonster:not-a-number"
    };
    var npc = NewNpc(environment, 100, 200, 4321);
    var random = new SequenceRandom(0, 4, 2);
    var calls = new List<SpawnCall>();

    player.WantNativeMagicTowerWarMon(npc, random.Next,
        (actualEnvironment, name, x, y) =>
        {
            Equal((byte)4, player.m_btNativeMagicTowerPhase,
                "phase was not changed before spawn");
            calls.Add(new SpawnCall(actualEnvironment, name, x, y));
        });

    Equal((byte)4, player.m_btNativeMagicTowerPhase, "phase transition");
    Equal(new[] { 5, 5, 5 }, random.Ranges.ToArray(), "random ranges");
    Equal(3, calls.Count, "spawn call count");
    Assert(ReferenceEquals(environment, calls[0].Environment)
           && ReferenceEquals(environment, calls[1].Environment)
           && ReferenceEquals(environment, calls[2].Environment),
        "spawn did not retain the player's physical environment");
    Equal(new[] { "FirstMonster", "FirstMonster", "SecondMonster" },
        calls.Select(call => call.Name).ToArray(), "spawn names/order");
    Equal(new[] { (short)100, (short)104, (short)102 },
        calls.Select(call => call.X).ToArray(), "spawn x offsets");
    Equal(new[] { (short)200, (short)204, (short)202 },
        calls.Select(call => call.Y).ToArray(), "spawn y offsets");
    AssertCloseMessage(player, npc, 1, "phase3 close");
}

static void CheckNonPhaseThreeOnlyCloses()
{
    var environment = NewEnvironment("non-phase3");
    var player = new TPlayObject
    {
        m_PEnvir = environment,
        m_btNativeMagicTowerPhase = 2,
        m_sNativeMagicTowerChallengeMonsters = "ShouldNotSpawn:9/AlsoNo:9"
    };
    var npc = NewNpc(environment, 3, 4, 123);

    player.WantNativeMagicTowerWarMon(npc,
        _ => throw new InvalidOperationException(
            "non-phase3 consumed Random(5)"),
        (_, _, _, _) => throw new InvalidOperationException(
            "non-phase3 spawned a monster"));

    Equal((byte)2, player.m_btNativeMagicTowerPhase,
        "non-phase3 changed phase");
    AssertCloseMessage(player, npc, 1, "non-phase3 close");
}

static void CheckDefaultAndNonPositiveCounts()
{
    var environment = NewEnvironment("count-parser");
    var npc = NewNpc(environment, 7, 11, 77);

    var defaultPlayer = new TPlayObject
    {
        m_PEnvir = environment,
        m_btNativeMagicTowerPhase = 3,
        m_sNativeMagicTowerChallengeMonsters = "DefaultCount/"
    };
    var defaultRandom = new SequenceRandom(3);
    var defaultCalls = new List<SpawnCall>();
    defaultPlayer.WantNativeMagicTowerWarMon(npc, defaultRandom.Next,
        (actualEnvironment, name, x, y) => defaultCalls.Add(
            new SpawnCall(actualEnvironment, name, x, y)));
    Equal(1, defaultCalls.Count, "missing-colon default count");
    Equal("DefaultCount", defaultCalls[0].Name,
        "missing-colon monster name");
    Equal((short)10, defaultCalls[0].X, "missing-colon x");
    Equal((short)14, defaultCalls[0].Y, "missing-colon y");

    var nonPositivePlayer = new TPlayObject
    {
        m_PEnvir = environment,
        m_btNativeMagicTowerPhase = 3,
        m_sNativeMagicTowerChallengeMonsters =
            "Zero:0/Negative:-2"
    };
    nonPositivePlayer.WantNativeMagicTowerWarMon(npc,
        _ => throw new InvalidOperationException(
            "non-positive group consumed Random(5)"),
        (_, _, _, _) => throw new InvalidOperationException(
            "non-positive group spawned"));
    Equal((byte)4, nonPositivePlayer.m_btNativeMagicTowerPhase,
        "non-positive phase transition");
    AssertCloseMessage(nonPositivePlayer, npc, 1,
        "non-positive close");
}

static void CheckNativeIntegerSyntax()
{
    var environment = NewEnvironment("native-integer-parser");
    var player = new TPlayObject
    {
        m_PEnvir = environment,
        m_btNativeMagicTowerPhase = 3,
        m_sNativeMagicTowerChallengeMonsters = "Hex:$2/Trailing:2 "
    };
    var npc = NewNpc(environment, 20, 30, 78);
    var random = new SequenceRandom(0, 1, 2);
    var calls = new List<SpawnCall>();

    player.WantNativeMagicTowerWarMon(npc, random.Next,
        (actualEnvironment, name, x, y) => calls.Add(
            new SpawnCall(actualEnvironment, name, x, y)));

    Equal(new[] { "Hex", "Hex", "Trailing" },
        calls.Select(call => call.Name).ToArray(),
        "Delphi integer syntax/default order");
    Equal(new[] { 5, 5, 5 }, random.Ranges.ToArray(),
        "Delphi integer syntax random ranges");
    AssertCloseMessage(player, npc, 1, "Delphi integer syntax close");
}

static void CheckStrictBridgeAbiAndExplicitPlayer()
{
    var environment = NewEnvironment("bridge-instance");
    var contextPlayer = new TPlayObject
    {
        m_PEnvir = environment,
        m_btNativeMagicTowerPhase = 3,
        m_sNativeMagicTowerChallengeMonsters = "Context:0/"
    };
    var explicitPlayer = new TPlayObject
    {
        m_PEnvir = environment,
        m_btNativeMagicTowerPhase = 3,
        m_sNativeMagicTowerChallengeMonsters = "Explicit:0/"
    };
    var npc = NewNpc(environment, 5, 6, 888);
    var bridge = new PasApiBridge
    {
        CurrentPlayer = contextPlayer,
        CurrentNpc = npc
    };
    var valid = new List<PasValue> { PasValue.FromObject(explicitPlayer) };

    Assert(bridge.CallNpcMethod("WantWarMon", valid, out var result),
        "valid procedure ABI rejected");
    Equal(PasValueType.Nil, result.Type, "procedure result");
    Equal((byte)4, explicitPlayer.m_btNativeMagicTowerPhase,
        "explicit player phase");
    Equal((byte)3, contextPlayer.m_btNativeMagicTowerPhase,
        "CurrentPlayer phase changed");
    AssertCloseMessage(explicitPlayer, npc, 1, "explicit player close");
    Equal(0, contextPlayer.m_MsgList.Count,
        "CurrentPlayer received explicit player's close");

    foreach (var malformed in new[]
             {
                 new List<PasValue>(),
                 new List<PasValue> { PasValue.FromString("monster") },
                 new List<PasValue> { PasValue.FromInt(1) },
                 new List<PasValue>
                 {
                     PasValue.FromObject(explicitPlayer), PasValue.FromInt(1)
                 }
             })
        Assert(!bridge.CallNpcMethod("WantWarMon", malformed, out _),
            "malformed procedure ABI accepted");

    Assert(!bridge.CallNpcFunc("WantWarMon", valid, out var functionResult),
        "procedure exposed through function dispatcher");
    Equal(PasValueType.Nil, functionResult.Type,
        "rejected function result");
    AssertCloseMessage(explicitPlayer, npc, 1,
        "malformed/function call changed explicit player");
}

static void CheckPhysicalEnvironmentProductionSpawn()
{
    PrepareRuntime();
    const string sharedMapName = "same-logical-map";
    var registered = NewEnvironment(sharedMapName);
    var physical = NewEnvironment(sharedMapName);
    RegisterMap(registered);
    AddMonsterDefinition("ProductionA");
    AddMonsterDefinition("ProductionB");

    var player = new TPlayObject
    {
        m_PEnvir = physical,
        m_sMapName = sharedMapName,
        m_btNativeMagicTowerPhase = 3,
        m_sNativeMagicTowerChallengeMonsters =
            "ProductionA:2/ProductionB:1"
    };
    var npc = NewNpc(physical, 10, 10, 900);
    player.WantNativeMagicTowerWarMon(npc);

    var monsters = ObjectActors().Where(actor =>
        actor.m_sCharName is "ProductionA" or "ProductionB").ToArray();
    Equal(3, monsters.Length, "production monster count");
    Equal(2, monsters.Count(actor => actor.m_sCharName == "ProductionA"),
        "production first group count");
    Equal(1, monsters.Count(actor => actor.m_sCharName == "ProductionB"),
        "production second group count");
    foreach (var monster in monsters)
    {
        Assert(ReferenceEquals(physical, monster.m_PEnvir),
            "production spawn resolved through the registered map");
        var offsetX = monster.m_nCurrX - npc.m_nCurrX;
        var offsetY = monster.m_nCurrY - npc.m_nCurrY;
        Assert(offsetX == offsetY && offsetX is >= 0 and < 5,
            "production spawn did not reuse one Random(5) for x/y");
        Assert(CellContains(physical, monster),
            "production monster missing from physical cell");
        Assert(!CellContains(registered, monster),
            "production monster leaked into registered same-name map");
    }
    Equal(3, physical.MonCount, "physical environment monster count");
    Equal(0, registered.MonCount, "registered environment monster count");
    AssertCloseMessage(player, npc, 1, "production close");
}

static void CheckNativeOrdinaryPlacement()
{
    PrepareRuntime();
    var occupiedEnvironment = NewEnvironment("occupied-instance");
    AddMonsterDefinition("PlacementOccupant");
    AddMonsterDefinition("PlacementWarMonster");
    var occupant = M2Share.UserEngine.RegenNativeMagicTowerChallengeMonster(
        occupiedEnvironment, "PlacementOccupant", 10, 10);
    Assert(occupant != null, "placement occupant was not created");

    var stacked = M2Share.UserEngine.RegenNativeMagicTowerWarMonster(
        occupiedEnvironment, "PlacementWarMonster", 10, 10);
    Assert(stacked != null,
        "ordinary construction rejected the occupied original cell");
    Equal((short)10, stacked.m_nCurrX, "occupied-cell spawn x");
    Equal((short)10, stacked.m_nCurrY, "occupied-cell spawn y");
    Assert(CellContains(occupiedEnvironment, occupant)
           && CellContains(occupiedEnvironment, stacked),
        "occupied-cell actors were not both published");
    Equal(0, M2Share.UserEngine.NativeMagicTowerDeferredSpawnCount,
        "occupied-cell spawn was incorrectly deferred");

    PrepareRuntime();
    var fallbackEnvironment = NewEnvironment("point-fallback-instance");
    AddMonsterDefinition("PointFallbackMonster");
    BlockAllCells(fallbackEnvironment);
    fallbackEnvironment.SetMapXYFlag(1, 1, true);
    fallbackEnvironment.m_PointList.Add(new PointInfo(1, 1));

    var relocated = M2Share.UserEngine.RegenNativeMagicTowerWarMonster(
        fallbackEnvironment, "PointFallbackMonster", 10, 10);
    Assert(relocated == null,
        "ordinary monster moved away from its blocked original position");
    Equal(1, M2Share.UserEngine.NativeMagicTowerDeferredSpawnCount,
        "blocked original position was not deferred");
    Assert(!ObjectActors().Any(actor =>
            actor.m_sCharName == "PointFallbackMonster"),
        "ordinary monster used the special-object point fallback");
}

static void CheckDeferredQueueFifoAndRetryBudget()
{
    PrepareRuntime();
    AddMonsterDefinition("DeferredOccupant");
    AddMonsterDefinition("DeferredHead");
    AddMonsterDefinition("DeferredTail");
    var headEnvironment = NewEnvironment("deferred-head-instance");
    var tailEnvironment = NewEnvironment("deferred-tail-instance");
    BlockAllCells(headEnvironment);
    BlockAllCells(tailEnvironment);

    Assert(M2Share.UserEngine.RegenNativeMagicTowerWarMonster(
               headEnvironment, "DeferredHead", 10, 10) == null,
        "blocked head unexpectedly spawned");
    Assert(M2Share.UserEngine.RegenNativeMagicTowerWarMonster(
               tailEnvironment, "DeferredTail", 10, 10) == null,
        "blocked tail unexpectedly spawned");
    Equal(2, M2Share.UserEngine.NativeMagicTowerDeferredSpawnCount,
        "initial deferred record count");

    headEnvironment.SetMapXYFlag(10, 10, true);
    var occupant = M2Share.UserEngine.RegenNativeMagicTowerChallengeMonster(
        headEnvironment, "DeferredOccupant", 10, 10);
    Assert(occupant != null, "deferred head occupant was not created");
    tailEnvironment.SetMapXYFlag(12, 10, true);
    M2Share.UserEngine.ProcessNativeMagicTowerDeferredSpawns();
    Equal(1, M2Share.UserEngine.NativeMagicTowerDeferredSpawnCount,
        "first-round FIFO budget did not retain only the tail");
    var head = ObjectActors().Single(actor =>
        actor.m_sCharName == "DeferredHead");
    Equal((short)10, head.m_nCurrX, "occupied-head original x");
    Equal((short)10, head.m_nCurrY, "occupied-head original y");
    Assert(CellContains(headEnvironment, occupant) &&
           CellContains(headEnvironment, head),
        "deferred head did not retry the original position");
    Assert(!ObjectActors().Any(actor => actor.m_sCharName == "DeferredTail"),
        "FIFO tail bypassed the first-round head budget");

    M2Share.UserEngine.ProcessNativeMagicTowerDeferredSpawns();
    Equal(0, M2Share.UserEngine.NativeMagicTowerDeferredSpawnCount,
        "six failed original-position retries did not drain the queue");
    Assert(!ObjectActors().Any(actor =>
            actor.m_sCharName == "DeferredTail"),
        "deferred retry moved to an alternate valid position");

    PrepareRuntime();
    AddMonsterDefinition("DeferredDrop");
    var dropEnvironment = NewEnvironment("deferred-drop-instance");
    BlockAllCells(dropEnvironment);
    Assert(M2Share.UserEngine.RegenNativeMagicTowerWarMonster(
               dropEnvironment, "DeferredDrop", 10, 10) == null,
        "six-failure fixture unexpectedly spawned");
    M2Share.UserEngine.ProcessNativeMagicTowerDeferredSpawns();
    Equal(1, M2Share.UserEngine.NativeMagicTowerDeferredSpawnCount,
        "first five failed attempts dropped the record");
    M2Share.UserEngine.ProcessNativeMagicTowerDeferredSpawns();
    Equal(0, M2Share.UserEngine.NativeMagicTowerDeferredSpawnCount,
        "sixth failed attempt did not drop the record");
    Assert(!ObjectActors().Any(actor =>
            actor.m_sCharName == "DeferredDrop"),
        "six-failure record unexpectedly published an actor");
}

static void CheckNativeRuntimeMonsterSchedulingAndCleanup()
{
    PrepareRuntime();
    AddMonsterDefinitionWithRace(TPlayObject.NativeMagicTowerArcherName,
        TPlayObject.NativeMagicTowerArcherRace);
    AddMonsterDefinition("RuntimeChallenge");
    AddMonsterDefinition("RuntimeWar");
    var environment = NewEnvironment("runtime-monster-instance");

    var archer = M2Share.UserEngine.RegenNativeMagicTowerArcher(
        environment, 4, 4);
    var challenge =
        M2Share.UserEngine.RegenNativeMagicTowerChallengeMonster(environment,
            "RuntimeChallenge", 8, 8);
    var war = M2Share.UserEngine.RegenNativeMagicTowerWarMonster(
        environment, "RuntimeWar", 12, 12);
    var monsters = new[] { archer, challenge, war };
    Assert(monsters.All(monster => monster != null),
        "runtime fixture failed to publish all tower monster kinds");
    Equal(3, M2Share.UserEngine.NativeMagicTowerRuntimeMonsterCount,
        "tower monsters were not registered for runtime scheduling");

    var currentTick = HUtil32.GetTickCount();
    foreach (var monster in monsters)
    {
        monster.m_boIsVisibleActive = true;
        monster.m_nRunTime = 0;
        monster.m_dwRunTick = currentTick - 1000;
        monster.m_dwSearchTick = currentTick;
        monster.m_dwSearchTime = int.MaxValue;
        monster.m_dwHPMPTick = 0;
    }
    M2Share.UserEngine.ProcessNativeMagicTowerRuntimeMonsters();
    Assert(monsters.All(monster => monster.m_dwHPMPTick != 0),
        "tower runtime scheduler did not reach every monster Run method");

    foreach (var monster in monsters)
        monster.MakeGhost();
    M2Share.UserEngine.ProcessNativeMagicTowerRuntimeMonsters();
    Equal(0, M2Share.UserEngine.NativeMagicTowerRuntimeMonsterCount,
        "ghost tower monsters remained in the runtime list");
    Equal(3, M2Share.UserEngine.NativeMagicTowerRuntimeSlotCount,
        "ghost cleanup compacted native runtime null holes");
    Assert(monsters.All(monster =>
            !ReferenceEquals(M2Share.ObjectManager.Get(monster.ObjectId),
                monster) && !CellContains(environment, monster)),
        "ghost tower monster cleanup leaked map/object publication");

    var replacement = M2Share.UserEngine.RegenNativeMagicTowerWarMonster(
        environment, "RuntimeWar", 16, 16);
    Assert(replacement != null,
        "runtime slot-reuse replacement was not published");
    Equal(1, M2Share.UserEngine.NativeMagicTowerRuntimeMonsterCount,
        "runtime slot-reuse active count");
    Equal(3, M2Share.UserEngine.NativeMagicTowerRuntimeSlotCount,
        "runtime registration did not reuse the first null slot");
}

static void CheckDeferredQueueGenerationAndStopCleanup()
{
    PrepareRuntime();
    AddMonsterDefinition("StaleGeneration");
    var staleEnvironment = NewEnvironment("stale-generation-instance");
    staleEnvironment.ConfigureDynamicRoom("tower", 41, null);
    staleEnvironment.SetDynamicRoomLeaseIndex(700);
    staleEnvironment.DynamicRoomState = 2;
    BlockAllCells(staleEnvironment);

    Assert(M2Share.UserEngine.RegenNativeMagicTowerWarMonster(
               staleEnvironment, "StaleGeneration", 10, 10) == null,
        "stale-generation fixture did not defer");
    Equal(1, M2Share.UserEngine.NativeMagicTowerDeferredSpawnCount,
        "stale-generation deferred count");
    staleEnvironment.SetDynamicRoomLeaseIndex(701);
    staleEnvironment.SetMapXYFlag(10, 10, true);
    M2Share.UserEngine.ProcessNativeMagicTowerDeferredSpawns();
    Equal(0, M2Share.UserEngine.NativeMagicTowerDeferredSpawnCount,
        "stale dynamic-room generation was retained");
    Assert(!ObjectActors().Any(actor =>
            actor.m_sCharName == "StaleGeneration"),
        "stale dynamic-room generation spawned into reused room");

    AddMonsterDefinition("StopRuntime");
    var stopRuntimeEnvironment = NewEnvironment(
        "stop-runtime-instance");
    var stopRuntime =
        M2Share.UserEngine.RegenNativeMagicTowerChallengeMonster(
            stopRuntimeEnvironment, "StopRuntime", 6, 6);
    Assert(stopRuntime != null &&
           M2Share.UserEngine.NativeMagicTowerRuntimeMonsterCount == 1,
        "Stop runtime cleanup fixture was not registered");

    AddMonsterDefinition("StopCleanup");
    var stopEnvironment = NewEnvironment("stop-cleanup-instance");
    BlockAllCells(stopEnvironment);
    Assert(M2Share.UserEngine.RegenNativeMagicTowerWarMonster(
               stopEnvironment, "StopCleanup", 10, 10) == null,
        "Stop cleanup fixture did not defer");
    Equal(1, M2Share.UserEngine.NativeMagicTowerDeferredSpawnCount,
        "Stop cleanup deferred count");
    M2Share.UserEngine.Stop();
    Equal(0, M2Share.UserEngine.NativeMagicTowerDeferredSpawnCount,
        "Stop did not clear deferred WantWarMon records");
    Equal(0, M2Share.UserEngine.NativeMagicTowerRuntimeMonsterCount,
        "Stop did not clear tower runtime records");
    Equal(0, M2Share.UserEngine.NativeMagicTowerRuntimeSlotCount,
        "Stop did not release tower runtime slots");
    Assert(!ReferenceEquals(M2Share.ObjectManager.Get(stopRuntime.ObjectId),
               stopRuntime) &&
           !CellContains(stopRuntimeEnvironment, stopRuntime),
        "Stop leaked a live tower monster publication");
}

static void CheckSourceContract()
{
    var root = FindRepositoryRoot();
    var playerSource = File.ReadAllText(Path.Combine(root, "GameSvr",
        "Players", "TPlayObject.NativeMagicTower.WarMon.cs"));
    var engineSource = File.ReadAllText(Path.Combine(root, "GameSvr",
        "UsrSystem", "UsrEngn.cs"));
    var bridgeSource = File.ReadAllText(Path.Combine(root, "GameSvr",
        "ScriptSystem", "PasEngine", "PasApiBridge.cs"));

    Assert(playerSource.Contains("m_btNativeMagicTowerPhase == 3",
            StringComparison.Ordinal)
           && playerSource.Contains("m_btNativeMagicTowerPhase = 4",
               StringComparison.Ordinal)
           && playerSource.Contains("random(5)", StringComparison.Ordinal)
           && playerSource.Contains("TryParseNativeDelphiInteger",
               StringComparison.Ordinal)
           && playerSource.Contains("spawn(m_PEnvir", StringComparison.Ordinal)
           && playerSource.Contains("RM_MERCHANTDLGCLOSE",
               StringComparison.Ordinal),
        "player source lost native state/random/environment/close contract");
    Assert(!playerSource.Contains("FindMap", StringComparison.Ordinal)
           && !playerSource.Contains("RegenMonsterByName",
               StringComparison.Ordinal),
        "player source regressed to map-name or ordinary spawn lookup");
    Assert(engineSource.Contains(
               "NativeMagicTowerDeferredSpawnBudget = 5",
               StringComparison.Ordinal)
           && engineSource.Contains(
               "ProcessNativeMagicTowerDeferredSpawns();",
               StringComparison.Ordinal)
           && !engineSource.Contains("pending.RetryCounter < 2",
               StringComparison.Ordinal)
           && engineSource.Contains(
               "ProcessNativeMagicTowerRuntimeMonsters();",
               StringComparison.Ordinal)
           && engineSource.Contains("monster.Run();",
               StringComparison.Ordinal)
           && engineSource.Contains(
               "RegisterNativeMagicTowerRuntimeMonster",
               StringComparison.Ordinal)
           && engineSource.Contains(
               "_nativeMagicTowerRuntimeMonsters.IndexOf(null)",
               StringComparison.Ordinal)
           && engineSource.Contains(
               "NativeMagicTowerRuntimeBudgetCheckInterval = 20",
               StringComparison.Ordinal)
           && engineSource.Contains(
               "NativeMagicTowerRuntimeTimeBudget = 25",
               StringComparison.Ordinal)
           && engineSource.Contains("DynamicRoomPhysicalInstanceId",
               StringComparison.Ordinal)
           && engineSource.Contains("DynamicRoomIndex ==",
               StringComparison.Ordinal),
        "engine source lost exact deferred/generation/slot-reuse contract");

    var createStart = engineSource.IndexOf(
        "private TBaseObject CreateNativeMagicTowerWarMonster(",
        StringComparison.Ordinal);
    var createEnd = engineSource.IndexOf("public void Run()", createStart,
        StringComparison.Ordinal);
    Assert(createStart >= 0 && createEnd > createStart,
        "native War monster creation helper missing");
    var createCase = engineSource[createStart..createEnd];
    var addIndex = createCase.IndexOf("AddBaseObject", StringComparison.Ordinal);
    Assert(addIndex >= 0 &&
           createCase.Contains("false, true);", StringComparison.Ordinal)
           && !createCase.Contains("TryResolveNativeMagicTowerPlacement",
               StringComparison.Ordinal)
           && !createCase.Contains("nativePermissivePlacement",
               StringComparison.Ordinal)
           && !createCase.Contains("CanWalk", StringComparison.Ordinal)
           && !createCase.Contains("m_PointList", StringComparison.Ordinal),
        "ordinary War monster creation did not preserve the exact position");

    var runtimeStart = engineSource.IndexOf(
        "internal void ProcessNativeMagicTowerRuntimeMonsters()",
        StringComparison.Ordinal);
    var runtimeEnd = engineSource.IndexOf(
        "private void ClearNativeMagicTowerRuntimeMonsters()", runtimeStart,
        StringComparison.Ordinal);
    Assert(runtimeStart >= 0 && runtimeEnd > runtimeStart,
        "native standalone runtime scheduler missing");
    var runtimeCase = engineSource[runtimeStart..runtimeEnd];
    Assert(runtimeCase.Contains("_nativeMagicTowerRuntimeCursor++",
               StringComparison.Ordinal)
           && runtimeCase.Contains(
               "_nativeMagicTowerRuntimeMonsters[slot] = null",
               StringComparison.Ordinal)
           && runtimeCase.Contains("monster.Run();",
               StringComparison.Ordinal)
           && !runtimeCase.Contains("m_boIsVisibleActive",
               StringComparison.Ordinal)
           && !runtimeCase.Contains("SearchViewRange",
               StringComparison.Ordinal),
        "runtime scheduler lost native cursor/hole/direct-Run contract");

    var start = bridgeSource.IndexOf("case \"wantwarmon\":",
        StringComparison.Ordinal);
    var end = bridgeSource.IndexOf("case \"getskyprize\":", start,
        StringComparison.Ordinal);
    Assert(start >= 0 && end > start, "WantWarMon bridge case missing");
    var bridgeCase = bridgeSource[start..end];
    Assert(bridgeCase.Contains("args.Count != 1", StringComparison.Ordinal)
           && bridgeCase.Contains("PasValueType.Object",
               StringComparison.Ordinal)
           && bridgeCase.Contains("WantNativeMagicTowerWarMon(CurrentNpc)",
               StringComparison.Ordinal)
           && !bridgeCase.Contains("CurrentPlayer.", StringComparison.Ordinal),
        "bridge source lost strict explicit-player ABI");
}

static void PrepareRuntime()
{
    M2Share.g_Config = new GameSvrConfig();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.MapManager = new MapManager();
    M2Share.UserEngine = new UserEngine();
    M2Share.RandomNumber = RandomNumber.GetInstance();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new ArrayList();
    M2Share.g_MonSayMsgList =
        new Dictionary<string, IList<TMonSayMsg>>();
}

static void AddMonsterDefinition(string name) =>
    AddMonsterDefinitionWithRace(name, (byte)M2Share.MONSTER_OMA);

static void AddMonsterDefinitionWithRace(string name, byte race)
{
    M2Share.UserEngine.MonsterList.Add(new TMonInfo
    {
        ItemList = new List<TMonItem>(),
        sName = name,
        btRace = race,
        wLevel = 1,
        wHP = 100,
        wWalkSpeed = 1000,
        wWalkStep = 1,
        wWalkWait = 1000,
        wAttackSpeed = 1000
    });
}

static Envirnoment NewEnvironment(string mapName)
{
    var environment = new Envirnoment
    {
        sMapName = mapName,
        m_sMapFileName = mapName
    };
    typeof(Envirnoment).GetMethod("Initialize",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(environment, new object[] { (short)32, (short)32 });
    return environment;
}

static NormNpc NewNpc(Envirnoment environment, short x, short y,
    int _) => new()
{
    m_PEnvir = environment,
    m_sMapName = environment.sMapName,
    m_sMapFileName = environment.m_sMapFileName,
    m_nCurrX = x,
    m_nCurrY = y
};

static void RegisterMap(Envirnoment environment)
{
    var maps = (IDictionary<string, Envirnoment>)typeof(MapManager)
        .GetField("m_MapList", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(M2Share.MapManager)!;
    maps.Add(environment.sMapName, environment);
}

static IReadOnlyList<TBaseObject> ObjectActors()
{
    var actors = typeof(ObjectManager).GetField("_actors",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(M2Share.ObjectManager)!;
    return ((IEnumerable)actors).Cast<object>()
        .Select(entry => (TBaseObject)entry.GetType()
            .GetProperty("Value")!.GetValue(entry)!)
        .ToArray();
}

static bool CellContains(Envirnoment environment, TBaseObject actor)
{
    var found = false;
    var cell = environment.GetMapCellInfo(actor.m_nCurrX, actor.m_nCurrY,
        ref found);
    return found && cell.ObjList != null && cell.ObjList.Any(item =>
        item.CellType == CellType.OS_MOVINGOBJECT
        && ReferenceEquals(item.CellObj, actor));
}

static void BlockAllCells(Envirnoment environment)
{
    for (var x = 0; x < environment.wWidth; x++)
    for (var y = 0; y < environment.wHeight; y++)
        environment.SetMapXYFlag(x, y, false);
}

static void AssertCloseMessage(TPlayObject player, NormNpc npc,
    int expectedCount, string scenario)
{
    var messages = player.m_MsgList.Where(message =>
        message.wIdent == Grobal2.RM_MERCHANTDLGCLOSE).ToArray();
    Equal(expectedCount, messages.Length, scenario + " count");
    var message = messages[^1];
    Assert(ReferenceEquals(npc, message.BaseObject),
        scenario + " owner");
    Equal(0, message.wParam, scenario + " wParam");
    Equal(npc.ObjectId, message.nParam1, scenario + " npc id");
    Equal(0, message.nParam2, scenario + " nParam2");
    Equal(0, message.nParam3, scenario + " nParam3");
    Assert(string.IsNullOrEmpty(message.Buff), scenario + " payload");
}

static string FindRepositoryRoot()
    => AuditRepoRoot.Resolve();

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

static void Equal<T>(T expected, T actual, string message)
{
    if (expected is IEnumerable<int> expectedInts
        && actual is IEnumerable<int> actualInts
        && expectedInts.SequenceEqual(actualInts))
        return;
    if (expected is IEnumerable<short> expectedShorts
        && actual is IEnumerable<short> actualShorts
        && expectedShorts.SequenceEqual(actualShorts))
        return;
    if (expected is IEnumerable<string> expectedStrings
        && actual is IEnumerable<string> actualStrings
        && expectedStrings.SequenceEqual(actualStrings))
        return;
    if (EqualityComparer<T>.Default.Equals(expected, actual)) return;
    throw new InvalidOperationException(
        $"{message}: expected={Format(expected)} actual={Format(actual)}");
}

static string Format<T>(T value) => value is IEnumerable values
    && value is not string
        ? string.Join(',', values.Cast<object>())
        : value?.ToString() ?? "<null>";

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

readonly record struct SpawnCall(Envirnoment Environment, string Name,
    short X, short Y);

sealed class SequenceRandom
{
    private readonly Queue<int> _values;

    internal SequenceRandom(params int[] values) =>
        _values = new Queue<int>(values);

    internal List<int> Ranges { get; } = new();

    internal int Next(int range)
    {
        Ranges.Add(range);
        if (_values.Count == 0)
            throw new InvalidOperationException("random sequence exhausted");
        var value = _values.Dequeue();
        if (value < 0 || value >= range)
            throw new InvalidOperationException(
                $"random value {value} outside range {range}");
        return value;
    }
}
