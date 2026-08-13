using System.Collections;
using System.Reflection;
using GameSvr;
using GameSvr.PasEngine;
using GameSvr.Services;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.ObjectManager = new ObjectManager();
M2Share.MapManager = new MapManager();
M2Share.nServerIndex = 0;
M2Share.RandomNumber = RandomNumber.GetInstance();
M2Share.MagicTowerRouteSequencer = new NativeMagicTowerRouteSequencer(
    M2Share.RandomNumber.Random);
M2Share.ProcessMsgCriticalSection = new object();
M2Share.LogMsgCriticalSection = new object();
M2Share.LogStringList = new ArrayList();
M2Share.CreditCardService = NativeCreditCardService.Disabled;

var player = new TPlayObject();
var npc = new NormNpc();
var bridge = new PasApiBridge
{
    CurrentPlayer = player,
    CurrentNpc = npc
};

CheckAddNpcPropExactDispatch();

var propertyArgs = new List<PasValue> { PasValue.FromInt(12) };
Assert(bridge.CallNpcMethod("AddNpcProp", propertyArgs, out _),
    "AddNpcProp procedure was not acknowledged");
Assert(npc.HasNativePasProperty(12), "AddNpcProp(12) did not set the native bit");
Assert(!npc.HasNativePasProperty(11), "AddNpcProp(12) changed an adjacent bit");
bridge.CallNpcMethod("AddNpcProp", new List<PasValue> { PasValue.FromInt(16) }, out _);
Assert(!npc.HasNativePasProperty(16), "out-of-range native NPC property was accepted");

var playerArg = PasValue.FromObject(player);
var slotArgs = new List<PasValue> { playerArg, PasValue.FromInt(1) };
var playerOnlyArgs = new List<PasValue> { playerArg };
var routeWayArgs = new List<PasValue> { playerArg, PasValue.FromBool(true) };
var routeWayExArgs = new List<PasValue>
{
    playerArg, PasValue.FromBool(true), PasValue.FromBool(true)
};

var routeOrigin = new Envirnoment { sMapName = "0122~1" };
player.m_PEnvir = routeOrigin;
player.m_sMapName = routeOrigin.sMapName;
player.m_sMapFileName = routeOrigin.sMapName;
player.m_nCurrX = 23;
player.m_nCurrY = 31;
player.m_nLingFu = 3;
player.m_nUsedLingFu = 7;

Assert(bridge.CallNpcMethod("EngageArcher", slotArgs, out var engageResult),
    "EngageArcher valid NPC procedure ABI was rejected");
Assert(engageResult.Type == PasValueType.Nil,
    "EngageArcher procedure result was not Nil");
AssertNpcFuncClosed(bridge, player, "EngageArcher", slotArgs,
    "EngageArcher procedure was exposed through the function dispatcher");
Assert(bridge.CallNpcFunc("GetMoveChance", slotArgs, out var moveResult),
    "GetMoveChance valid NPC function ABI was rejected");
Assert(moveResult.Type == PasValueType.Boolean && !moveResult.AsBool(),
    "GetMoveChance fresh NPC slot was not false");
AssertNpcMethodClosed(bridge, player, "GetMoveChance", slotArgs,
    "GetMoveChance function was exposed through the procedure dispatcher");
var noLingFuPlayer = new TPlayObject();
Assert(bridge.CallNpcFunc("GetEngageChance",
        new List<PasValue> { PasValue.FromObject(noLingFuPlayer) },
        out var engageChanceResult),
    "GetEngageChance valid NPC function ABI was rejected");
Assert(engageChanceResult.Type == PasValueType.Boolean
       && !engageChanceResult.AsBool(),
    "GetEngageChance insufficient-balance result mismatch");
AssertNpcMethodClosed(bridge, player, "GetEngageChance", playerOnlyArgs,
    "GetEngageChance function was exposed through the procedure dispatcher");
Assert(bridge.CallNpcFunc("IsExistArcher", slotArgs, out var archerResult),
    "IsExistArcher valid NPC function ABI was rejected");
Assert(archerResult.Type == PasValueType.Boolean && !archerResult.AsBool(),
    "IsExistArcher fresh NPC slot was not false");
AssertNpcMethodClosed(bridge, player, "IsExistArcher", slotArgs,
    "IsExistArcher function was exposed through the procedure dispatcher");

AssertNpcMethodClosed(bridge, player, "EnterRouteWayByLF", playerOnlyArgs,
    "malformed EnterRouteWayByLF call was acknowledged");
AssertNpcMethodClosed(bridge, player, "EnterRouteWayByLF",
    new List<PasValue> { playerArg, PasValue.FromInt(1) },
    "EnterRouteWayByLF accepted a non-Boolean client-click argument");
AssertNpcMethodClosed(bridge, player, "EnterRouteWayByLF",
    new List<PasValue>
    {
        playerArg, PasValue.FromBool(true), PasValue.FromBool(false)
    },
    "EnterRouteWayByLF accepted an extra argument");
AssertNpcMethodClosed(bridge, player, "EnterRouteWayByLF",
    new List<PasValue> { PasValue.FromInt(1), PasValue.FromBool(true) },
    "EnterRouteWayByLF accepted a non-Player first argument");
AssertNpcFuncClosed(bridge, player, "EnterRouteWayByLF", routeWayArgs,
    "EnterRouteWayByLF procedure was exposed through the function dispatcher");
AssertNpcMethodClosed(bridge, player, "EnterRouteWayByLFEx", routeWayArgs,
    "EnterRouteWayByLFEx accepted a missing free-entry argument");
AssertNpcMethodClosed(bridge, player, "EnterRouteWayByLFEx",
    new List<PasValue>
    {
        playerArg, PasValue.FromInt(1), PasValue.FromBool(true)
    }, "EnterRouteWayByLFEx accepted a non-Boolean client-click argument");
AssertNpcMethodClosed(bridge, player, "EnterRouteWayByLFEx",
    new List<PasValue>
    {
        playerArg, PasValue.FromBool(true), PasValue.FromInt(1)
    }, "EnterRouteWayByLFEx accepted a non-Boolean free-entry argument");
AssertNpcMethodClosed(bridge, player, "EnterRouteWayByLFEx",
    new List<PasValue>
    {
        playerArg, PasValue.FromBool(true), PasValue.FromBool(true),
        PasValue.FromBool(false)
    }, "EnterRouteWayByLFEx accepted an extra argument");
AssertNpcMethodClosed(bridge, player, "EnterRouteWayByLFEx",
    new List<PasValue>
    {
        PasValue.FromInt(1), PasValue.FromBool(true), PasValue.FromBool(true)
    }, "EnterRouteWayByLFEx accepted a non-Player first argument");
AssertNpcFuncClosed(bridge, player, "EnterRouteWayByLFEx", routeWayExArgs,
    "EnterRouteWayByLFEx procedure was exposed through the function dispatcher");
AssertNpcMethodClosed(bridge, player, "EnterGuan", new List<PasValue>(),
    "EnterGuan accepted a missing player argument");
AssertNpcMethodClosed(bridge, player, "EnterGuan",
    new List<PasValue> { PasValue.FromInt(1) },
    "EnterGuan accepted a non-Player argument");
AssertNpcMethodClosed(bridge, player, "EnterGuan",
    new List<PasValue> { playerArg, PasValue.FromInt(1) },
    "EnterGuan accepted an extra argument");
AssertNpcFuncClosed(bridge, player, "EnterGuan", playerOnlyArgs,
    "EnterGuan procedure was exposed through the function dispatcher");
AssertNpcMethodClosed(bridge, player, "EnterNewGuan", playerOnlyArgs,
    "EnterNewGuan fabricated a static-map move without NewSky state");
AssertNpcMethodClosed(bridge, player, "EnterNext",
    new List<PasValue> { playerArg, PasValue.FromBool(false) },
    "EnterNext advanced without the native ten-slot tower state");
AssertNpcMethodClosed(bridge, player, "EnterNext2", playerOnlyArgs,
    "EnterNext2 advanced without the native tower state");

CheckPrizeProcedureDispatch();
Assert(bridge.CallNpcMethod("ChkMonAndItem", playerOnlyArgs,
        out var checkMonAndItemResult),
    "ChkMonAndItem valid NPC procedure ABI was rejected");
Assert(checkMonAndItemResult.Type == PasValueType.Nil,
    "ChkMonAndItem procedure result was not Nil");
AssertNpcFuncClosed(bridge, player, "ChkMonAndItem", playerOnlyArgs,
    "ChkMonAndItem procedure shadow returned a fabricated value");

Assert(bridge.CallNpcMethod("EngageArcher",
        new List<PasValue> { playerArg, PasValue.FromInt(0) }, out _),
    "EngageArcher business-invalid slot ABI was rejected");
AssertNpcMethodClosed(bridge, player, "EngageArcher",
    new List<PasValue> { playerArg, PasValue.FromBool(true) },
    "EngageArcher accepted a non-Integer slot");
Assert(bridge.CallNpcFunc("GetMoveChance",
        new List<PasValue> { playerArg, PasValue.FromInt(11) },
        out var invalidMoveResult)
       && invalidMoveResult.Type == PasValueType.Boolean
       && !invalidMoveResult.AsBool(),
    "invalid Magic Tower GetMoveChance result mismatch");
AssertNpcFuncClosed(bridge, player, "GetMoveChance",
    new List<PasValue> { playerArg },
    "GetMoveChance accepted a missing slot argument");
AssertNpcFuncClosed(bridge, player, "GetMoveChance",
    new List<PasValue> { PasValue.FromInt(1), PasValue.FromInt(1) },
    "GetMoveChance accepted a non-player first argument");
AssertNpcFuncClosed(bridge, player, "GetMoveChance",
    new List<PasValue> { playerArg, PasValue.FromInt(1), PasValue.FromInt(2) },
    "GetMoveChance accepted an extra argument");
AssertNpcFuncClosed(bridge, player, "GetMoveChance",
    new List<PasValue> { playerArg, PasValue.FromBool(true) },
    "GetMoveChance accepted a non-Integer slot");

CheckRouteSuccessAndExplicitPlayerOwnership();
CheckRouteExPaid();
CheckRouteExFree();
CheckRouteInsufficientLingFu();
CheckRouteMissingMapDoesNotRollback();
CheckRouteHundredthUsage();
CheckRouteSequencerBoundaries();
CheckRouteInterpreterDispatch();

Console.WriteLine("MagicTowerApiCheck PASS");

static void CheckAddNpcPropExactDispatch()
{
    var functionNpc = new NormNpc();
    var functionBridge = new PasApiBridge
    {
        CurrentPlayer = new TPlayObject(),
        CurrentNpc = functionNpc
    };
    var functionArgs = new List<PasValue> { PasValue.FromInt(10) };
    Assert(!functionBridge.CallNpcFunc("AddNpcProp", functionArgs, out _),
        "AddNpcProp procedure was exposed through function dispatch");
    Assert(!functionNpc.HasNativePasProperty(10),
        "AddNpcProp function shadow changed the NPC property bit");

    var methodNpc = new NormNpc();
    var methodBridge = new PasApiBridge
    {
        CurrentPlayer = new TPlayObject(),
        CurrentNpc = methodNpc
    };
    Assert(!methodBridge.CallNpcMethod("AddNpcProp", new List<PasValue>(), out _),
        "AddNpcProp accepted a missing property argument");
    Assert(!methodNpc.HasNativePasProperty(12),
        "zero-argument AddNpcProp changed the NPC property bit");
    Assert(!methodBridge.CallNpcMethod("AddNpcProp", new List<PasValue>
        {
            PasValue.FromInt(12),
            PasValue.FromInt(13)
        }, out _),
        "AddNpcProp accepted an extra property argument");
    Assert(!methodNpc.HasNativePasProperty(12)
           && !methodNpc.HasNativePasProperty(13),
        "extra-argument AddNpcProp changed an NPC property bit");
    foreach (var invalidArgument in new[]
             {
                 PasValue.FromString("12"),
                 PasValue.FromBool(true),
                 PasValue.FromDouble(12),
                 PasValue.FromObject(new object()),
                 PasValue.Nil
             })
    {
        Assert(!methodBridge.CallNpcMethod("AddNpcProp",
                new List<PasValue> { invalidArgument }, out _),
            "AddNpcProp accepted a non-integer property argument");
        Assert(!methodNpc.HasNativePasProperty(0)
               && !methodNpc.HasNativePasProperty(1)
               && !methodNpc.HasNativePasProperty(12),
            "non-integer AddNpcProp changed an NPC property bit");
    }
    Assert(methodBridge.CallNpcMethod("AddNpcProp",
            new List<PasValue> { PasValue.FromInt(12) }, out _),
        "AddNpcProp rejected its exact procedure ABI");
    Assert(methodNpc.HasNativePasProperty(12),
        "AddNpcProp exact procedure ABI did not set the NPC property bit");

    var interpreterNpc = new NormNpc();
    var interpreterBridge = new PasApiBridge
    {
        CurrentPlayer = new TPlayObject(),
        CurrentNpc = interpreterNpc
    };
    const string sourceCode = """
        program AddNpcPropDispatchProbe;
        procedure Probe;
        begin
          This_Npc.AddNpcProp(11);
        end;
        begin
        end.
        """;
    var interpreter = new PasInterpreter(
        new PasParser(new PasLexer(sourceCode)).Parse(), interpreterBridge);

    var result = interpreter.ExecuteProcedure("Probe");
    Assert(result.Type == PasValueType.Nil,
        "interpreter AddNpcProp procedure result was not Nil");
    Assert(interpreterNpc.HasNativePasProperty(11),
        "interpreter did not fall back from NPC function to method dispatch");
}

static void CheckPrizeProcedureDispatch()
{
    var contextPlayer = new TPlayObject { m_sCharName = "prize-context" };
    var explicitPlayer = new TPlayObject { m_sCharName = "prize-explicit" };
    var npc = new NormNpc { m_sCharName = "prize-npc" };
    var bridge = new PasApiBridge
    {
        CurrentPlayer = contextPlayer,
        CurrentNpc = npc
    };
    Assert(bridge.CallNpcMethod("AddNpcProp",
            new List<PasValue> { PasValue.FromInt(12) }, out _),
        "prize NPC property setup failed");

    SetPlayerField(contextPlayer, "m_btNativeMagicTowerPhase", (byte)2);
    SetPlayerField(contextPlayer,
        "m_btNativeMagicTowerDefeatedMonsterCount", (byte)73);
    SetPlayerField(explicitPlayer, "m_btNativeMagicTowerPhase", (byte)3);
    SetPlayerField(explicitPlayer,
        "m_btNativeMagicTowerDefeatedMonsterCount", (byte)40);
    var contextBefore = CapturePrizeDispatchPlayer(contextPlayer);

    Assert(bridge.CallNpcMethod("ClientGetPrize",
            new List<PasValue> { PasValue.FromObject(explicitPlayer) },
            out var clientResult),
        "ClientGetPrize exact procedure ABI was rejected");
    Assert(clientResult.Type == PasValueType.Nil,
        "ClientGetPrize procedure result was not Nil");
    Assert(ReadPlayerField<byte>(explicitPlayer,
               "m_btNativeMagicTowerPhase") == 4
           && ReadPlayerField<byte>(explicitPlayer,
               "m_btNativeMagicTowerDefeatedMonsterCount") == 0,
        "ClientGetPrize did not invoke the explicit player's core transaction");
    AssertPrizeDispatchPlayerUnchanged(contextPlayer, contextBefore,
        "ClientGetPrize explicit-player ownership");

    Assert(bridge.CallNpcMethod("GetSkyPrize",
            new List<PasValue> { PasValue.FromObject(explicitPlayer) },
            out var skyResult),
        "GetSkyPrize exact procedure ABI was rejected");
    Assert(skyResult.Type == PasValueType.Nil,
        "GetSkyPrize procedure result was not Nil");
    Assert(ReadPlayerField<byte>(explicitPlayer,
               "m_btNativeMagicTowerPhase") == 0,
        "GetSkyPrize did not invoke the explicit player's core transaction");
    AssertPrizeDispatchPlayerUnchanged(contextPlayer, contextBefore,
        "GetSkyPrize explicit-player ownership");

    var businessRejected = new TPlayObject
    {
        m_sCharName = "prize-business-rejected"
    };
    Assert(bridge.CallNpcMethod("GetSkyPrize",
            new List<PasValue> { PasValue.FromObject(businessRejected) },
            out var rejectedSkyResult)
           && rejectedSkyResult.Type == PasValueType.Nil,
        "GetSkyPrize leaked its core business result into procedure dispatch");
    Assert(bridge.CallNpcMethod("ClientGetPrize",
            new List<PasValue> { PasValue.FromObject(businessRejected) },
            out var rejectedClientResult)
           && rejectedClientResult.Type == PasValueType.Nil,
        "ClientGetPrize leaked its core business result into procedure dispatch");

    foreach (var name in new[] { "GetSkyPrize", "ClientGetPrize" })
    {
        AssertPrizeMethodMalformed(bridge, contextPlayer, explicitPlayer, name,
            new List<PasValue>(), "missing player argument");
        AssertPrizeMethodMalformed(bridge, contextPlayer, explicitPlayer, name,
            new List<PasValue> { PasValue.FromInt(1) },
            "non-object player argument");
        AssertPrizeMethodMalformed(bridge, contextPlayer, explicitPlayer, name,
            new List<PasValue> { PasValue.FromObject(npc) },
            "non-player object argument");
        AssertPrizeMethodMalformed(bridge, contextPlayer, explicitPlayer, name,
            new List<PasValue>
            {
                PasValue.FromObject(explicitPlayer), PasValue.FromInt(1)
            }, "extra argument");
        AssertPrizeFunctionClosed(bridge, contextPlayer, explicitPlayer, name);
    }
}

static void AssertPrizeMethodMalformed(PasApiBridge bridge,
    TPlayObject contextPlayer, TPlayObject explicitPlayer, string name,
    List<PasValue> args, string scenario)
{
    var contextBefore = CapturePrizeDispatchPlayer(contextPlayer);
    var explicitBefore = CapturePrizeDispatchPlayer(explicitPlayer);
    Assert(!bridge.CallNpcMethod(name, args, out var result),
        $"{name} accepted {scenario}");
    Assert(result.Type == PasValueType.Nil,
        $"{name} malformed procedure result was not Nil: {scenario}");
    AssertPrizeDispatchPlayerUnchanged(contextPlayer, contextBefore,
        $"{name} {scenario} context player");
    AssertPrizeDispatchPlayerUnchanged(explicitPlayer, explicitBefore,
        $"{name} {scenario} explicit player");
}

static void AssertPrizeFunctionClosed(PasApiBridge bridge,
    TPlayObject contextPlayer, TPlayObject explicitPlayer, string name)
{
    var contextBefore = CapturePrizeDispatchPlayer(contextPlayer);
    var explicitBefore = CapturePrizeDispatchPlayer(explicitPlayer);
    Assert(!bridge.CallNpcFunc(name,
            new List<PasValue> { PasValue.FromObject(explicitPlayer) }, out _),
        $"{name} procedure was exposed through the function dispatcher");
    AssertPrizeDispatchPlayerUnchanged(contextPlayer, contextBefore,
        $"{name} function context player");
    AssertPrizeDispatchPlayerUnchanged(explicitPlayer, explicitBefore,
        $"{name} function explicit player");
}

static void CheckRouteSuccessAndExplicitPlayerOwnership()
{
    ResetMapManager();
    var source = NewEnvironment("RouteSuccessSource");
    var destination = NewEnvironment("D5071~0");
    RegisterMap(source);
    RegisterMap(destination);

    var explicitPlayer = NewPlacedPlayer(source, "route-explicit", 3, 3);
    explicitPlayer.m_nLingFu = 3;
    explicitPlayer.m_nUsedLingFu = 7;
    var contextPlayer = new TPlayObject
    {
        m_sCharName = "route-context",
        m_nLingFu = 9,
        m_nUsedLingFu = 11
    };
    var contextBefore = CapturePlayer(contextPlayer);
    var npc = new NormNpc
    {
        m_sCharName = "route-npc",
        m_sMapName = "route-npc-map"
    };
    var routeSequencer = new NativeMagicTowerRouteSequencer(
        _ => 0, 0, 2_499, 9_000, 0, 0);
    M2Share.MagicTowerRouteSequencer = routeSequencer;
    M2Share.LogStringList.Clear();
    var bridge = new PasApiBridge
    {
        CurrentPlayer = contextPlayer,
        CurrentNpc = npc
    };

    Assert(bridge.CallNpcMethod("EnterRouteWayByLF", new List<PasValue>
    {
        PasValue.FromObject(explicitPlayer), PasValue.FromBool(true)
    }, out var result), "EnterRouteWayByLF exact ABI was rejected");
    Assert(result.Type == PasValueType.Nil,
        "EnterRouteWayByLF procedure result was not Nil");
    AssertPlayerUnchanged(contextPlayer, contextBefore,
        "EnterRouteWayByLF explicit-player ownership");
    Assert(explicitPlayer.m_nLingFu == 2,
        "EnterRouteWayByLF did not debit exactly one permanent LingFu");
    Assert(explicitPlayer.m_nUsedLingFu == 8,
        "EnterRouteWayByLF used-LingFu accounting mismatch");
    Assert(ReferenceEquals(explicitPlayer.m_PEnvir, destination),
        "EnterRouteWayByLF did not move the supplied player to D5071~0");
    Assert(explicitPlayer.m_sMapName == "D5071~0"
           && explicitPlayer.m_sMapFileName == "D5071~0",
        "EnterRouteWayByLF destination map identity mismatch");
    Assert(explicitPlayer.m_nCurrX == 11 && explicitPlayer.m_nCurrY == 13,
        "EnterRouteWayByLF destination coordinates mismatch");
    Assert(ReadPlayerField<byte>(explicitPlayer,
               "m_btNativeMagicTowerPhase") == 1,
        "EnterRouteWayByLF did not set native B88 phase state");
    Assert(!ReadPlayerField<bool>(explicitPlayer,
            "m_boNativeMagicTowerHundredth"),
        "EnterRouteWayByLF set native B89 outside a hundredth use");
    Assert(ReadPlayerField<byte>(explicitPlayer,
               "m_btNativeMagicTowerSpecialRoute") == 2,
        "EnterRouteWayByLF did not persist the forced 2500 route");

    var snapshot = routeSequencer.Snapshot();
    Assert(snapshot.TotalEntries == 1 && snapshot.Sequence == 2_500
           && snapshot.PaidEntries == 1 && snapshot.FreeEntries == 0,
        "EnterRouteWayByLF global route counters mismatch");
    Assert(explicitPlayer.m_MsgList.Count(message =>
            message.wIdent == Grobal2.RM_LINGFU_CHANGED) == 1,
        "EnterRouteWayByLF LingFu refresh count mismatch");
    // MOVE-52: SpaceMove takes the default ident pair, and both native space-move arms
    // load it as immediates - 0x6BD3AA `mov cx,0x2785` (10117) and 0x6BD3D3
    // `mov cx,0x2786` (10118), repeated at 0x6BD51B / 0x6BD544 on the cross-map arm.
    Assert(explicitPlayer.m_MsgList.Count(message =>
            message.wIdent == Grobal2.RM_NATIVE_CLEAROBJECTS) == 1,
        "EnterRouteWayByLF clear-objects message count mismatch");
    Assert(explicitPlayer.m_MsgList.Count(message =>
            message.wIdent == Grobal2.RM_NATIVE_CHANGEMAP
            && message.Buff == "D5071~0") == 1,
        "EnterRouteWayByLF change-map message mismatch");
    Assert(explicitPlayer.m_MsgList.All(message =>
            message.wIdent != Grobal2.RM_CLEAROBJECTS
            && message.wIdent != Grobal2.RM_CHANGEMAP),
        "EnterRouteWayByLF fell back to the legacy 8097/8098 idents");
    Assert(M2Share.LogStringList.Count == 1,
        "EnterRouteWayByLF LingFu debit log count mismatch");
    Assert((string)M2Share.LogStringList[0] ==
           "101\tRouteSuccessSource\t3\t3\troute-explicit\t" +
           "闯天关消耗灵符\t0\t1\troute-npc-route-npc-map",
        "EnterRouteWayByLF LingFu debit log payload mismatch");
}

static void CheckRouteExPaid()
{
    ResetMapManager();
    var source = NewEnvironment("RouteExPaidSource");
    var destination = NewEnvironment("D5071~0");
    RegisterMap(source);
    RegisterMap(destination);

    var explicitPlayer = NewPlacedPlayer(source, "route-ex-paid", 6, 7);
    explicitPlayer.m_nLingFu = 2;
    explicitPlayer.m_nUsedLingFu = 17;
    var contextPlayer = new TPlayObject
    {
        m_sCharName = "route-ex-paid-context",
        m_nLingFu = 8,
        m_nUsedLingFu = 12
    };
    var contextBefore = CapturePlayer(contextPlayer);
    var npc = new NormNpc
    {
        m_sCharName = "route-ex-paid-npc",
        m_sMapName = "route-ex-paid-npc-map"
    };
    var sequencer = new NativeMagicTowerRouteSequencer(
        _ => 0, 3, 9, 100, 2, 1);
    M2Share.MagicTowerRouteSequencer = sequencer;
    M2Share.LogStringList.Clear();
    var bridge = new PasApiBridge
    {
        CurrentPlayer = contextPlayer,
        CurrentNpc = npc
    };

    Assert(bridge.CallNpcMethod("EnterRouteWayByLFEx", new List<PasValue>
    {
        PasValue.FromObject(explicitPlayer), PasValue.FromBool(false),
        PasValue.FromBool(false)
    }, out var result), "paid EnterRouteWayByLFEx exact ABI was rejected");
    Assert(result.Type == PasValueType.Nil,
        "paid EnterRouteWayByLFEx procedure result was not Nil");
    AssertPlayerUnchanged(contextPlayer, contextBefore,
        "paid EnterRouteWayByLFEx explicit-player ownership");
    Assert(explicitPlayer.m_nLingFu == 1
           && explicitPlayer.m_nUsedLingFu == 18,
        "paid EnterRouteWayByLFEx LingFu accounting mismatch");
    Assert(ReferenceEquals(explicitPlayer.m_PEnvir, destination)
           && explicitPlayer.m_nCurrX == 11
           && explicitPlayer.m_nCurrY == 13,
        "paid EnterRouteWayByLFEx movement mismatch");
    Assert(ReadPlayerField<byte>(explicitPlayer,
               "m_btNativeMagicTowerPhase") == 1,
        "paid EnterRouteWayByLFEx did not set native B88 state");
    var snapshot = sequencer.Snapshot();
    Assert(snapshot.TotalEntries == 4 && snapshot.Sequence == 10
           && snapshot.Threshold == 100 && snapshot.PaidEntries == 3
           && snapshot.FreeEntries == 1,
        "paid EnterRouteWayByLFEx global counters mismatch");
    Assert(explicitPlayer.m_MsgList.Count(message =>
            message.wIdent == Grobal2.RM_LINGFU_CHANGED) == 1,
        "paid EnterRouteWayByLFEx LingFu refresh mismatch");
    Assert(M2Share.LogStringList.Count == 1
           && (string)M2Share.LogStringList[0] ==
           "101\tRouteExPaidSource\t6\t7\troute-ex-paid\t" +
           "闯天关消耗灵符\t0\t1\troute-ex-paid-npc-route-ex-paid-npc-map",
        "paid EnterRouteWayByLFEx native log mismatch");
}

static void CheckRouteExFree()
{
    ResetMapManager();
    var source = NewEnvironment("RouteExFreeSource");
    var destination = NewEnvironment("D5071~0");
    RegisterMap(source);
    RegisterMap(destination);

    var explicitPlayer = NewPlacedPlayer(source, "route-ex-free", 8, 9);
    var contextPlayer = new TPlayObject
    {
        m_sCharName = "route-ex-free-context",
        m_nLingFu = 6,
        m_nUsedLingFu = 14
    };
    var contextBefore = CapturePlayer(contextPlayer);
    var npc = new NormNpc
    {
        m_sCharName = "route-ex-free-npc",
        m_sMapName = "route-ex-free-npc-map"
    };
    var randomCalls = 0;
    var sequencer = new NativeMagicTowerRouteSequencer(_ =>
    {
        randomCalls++;
        return 17;
    }, 7, 2_500, 2_500, 4, 5);
    M2Share.MagicTowerRouteSequencer = sequencer;
    M2Share.LogStringList.Clear();
    var bridge = new PasApiBridge
    {
        CurrentPlayer = contextPlayer,
        CurrentNpc = npc
    };

    Assert(bridge.CallNpcMethod("EnterRouteWayByLFEx", new List<PasValue>
    {
        PasValue.FromObject(explicitPlayer), PasValue.FromBool(false),
        PasValue.FromBool(true)
    }, out var result), "free EnterRouteWayByLFEx exact ABI was rejected");
    Assert(result.Type == PasValueType.Nil,
        "free EnterRouteWayByLFEx procedure result was not Nil");
    AssertPlayerUnchanged(contextPlayer, contextBefore,
        "free EnterRouteWayByLFEx explicit-player ownership");
    Assert(explicitPlayer.m_nLingFu == 0
           && explicitPlayer.m_nUsedLingFu == 1,
        "free EnterRouteWayByLFEx changed debit state or lost hundredth state");
    Assert(ReferenceEquals(explicitPlayer.m_PEnvir, destination)
           && explicitPlayer.m_nCurrX == 11
           && explicitPlayer.m_nCurrY == 13,
        "free EnterRouteWayByLFEx movement mismatch");
    Assert(ReadPlayerField<byte>(explicitPlayer,
               "m_btNativeMagicTowerPhase") == 1
           && ReadPlayerField<bool>(explicitPlayer,
               "m_boNativeMagicTowerHundredth")
           && ReadPlayerField<byte>(explicitPlayer,
               "m_btNativeMagicTowerSpecialRoute") == 2,
        "free EnterRouteWayByLFEx B88/B89/B8A state mismatch");
    var snapshot = sequencer.Snapshot();
    Assert(snapshot.TotalEntries == 7 && snapshot.Sequence == 2_500
           && snapshot.Threshold == 2_717 && snapshot.PaidEntries == 4
           && snapshot.FreeEntries == 5 && randomCalls == 1,
        "free EnterRouteWayByLFEx changed counters or lost threshold RNG");
    Assert(explicitPlayer.m_MsgList.All(message =>
            message.wIdent != Grobal2.RM_LINGFU_CHANGED),
        "free EnterRouteWayByLFEx emitted a LingFu refresh");
    Assert(M2Share.LogStringList.Count == 0,
        "free EnterRouteWayByLFEx wrote a debit log");
    Assert(explicitPlayer.m_MsgList.Count(message =>
            message.wIdent == Grobal2.RM_NATIVE_CLEAROBJECTS) == 1
           && explicitPlayer.m_MsgList.Count(message =>
               message.wIdent == Grobal2.RM_NATIVE_CHANGEMAP
               && message.Buff == "D5071~0") == 1,
        "free EnterRouteWayByLFEx movement protocol mismatch");
    Assert(explicitPlayer.m_MsgList.All(message =>
            message.wIdent != Grobal2.RM_CLEAROBJECTS
            && message.wIdent != Grobal2.RM_CHANGEMAP),
        "free EnterRouteWayByLFEx fell back to the legacy 8097/8098 idents");
}

static void CheckRouteInsufficientLingFu()
{
    ResetMapManager();
    var source = NewEnvironment("RouteInsufficientSource");
    var destination = NewEnvironment("D5071~0");
    RegisterMap(source);
    RegisterMap(destination);
    var player = NewPlacedPlayer(source, "route-insufficient", 4, 4);
    player.m_nUsedLingFu = 23;
    var npc = new NormNpc
    {
        m_sCharName = "天关统领",
        m_sMapName = "RouteInsufficientNpc"
    };
    var sequencer = new NativeMagicTowerRouteSequencer(_ => 0);
    M2Share.MagicTowerRouteSequencer = sequencer;
    M2Share.LogStringList.Clear();
    var bridge = new PasApiBridge { CurrentPlayer = player, CurrentNpc = npc };

    Assert(bridge.CallNpcMethod("EnterRouteWayByLF", new List<PasValue>
    {
        PasValue.FromObject(player), PasValue.FromBool(true)
    }, out var result),
        "insufficient EnterRouteWayByLF was not acknowledged");
    Assert(result.Type == PasValueType.Nil,
        "insufficient EnterRouteWayByLF result was not Nil");
    Assert(player.m_nLingFu == 0 && player.m_nUsedLingFu == 23,
        "insufficient EnterRouteWayByLF changed LingFu accounting");
    Assert(ReferenceEquals(player.m_PEnvir, source)
           && player.m_nCurrX == 4 && player.m_nCurrY == 4,
        "insufficient EnterRouteWayByLF moved the player");
    Assert(ReferenceEquals(player.m_NPC, npc),
        "insufficient EnterRouteWayByLF did not bind the NPC owner");
    Assert(ReadPlayerField<byte>(player, "m_btNativeMagicTowerPhase") == 0
           && !ReadPlayerField<bool>(player,
               "m_boNativeMagicTowerHundredth")
           && ReadPlayerField<byte>(player,
               "m_btNativeMagicTowerSpecialRoute") == 0,
        "insufficient EnterRouteWayByLF changed native route state");
    var snapshot = sequencer.Snapshot();
    Assert(snapshot.TotalEntries == 0 && snapshot.Sequence == 0
           && snapshot.PaidEntries == 0 && snapshot.FreeEntries == 0,
        "insufficient EnterRouteWayByLF changed global route counters");
    Assert(M2Share.LogStringList.Count == 0,
        "insufficient EnterRouteWayByLF wrote a debit log");

    var merchantSay = player.m_MsgList.Single(message =>
        message.wIdent == Grobal2.RM_MERCHANTSAY);
    Assert(merchantSay.wParam == 0 && merchantSay.nParam1 == 0
           && merchantSay.nParam2 == 0 && merchantSay.nParam3 == 0,
        "insufficient EnterRouteWayByLF merchant message parameters mismatch");
    Assert(ReferenceEquals(merchantSay.BaseObject, npc),
        "insufficient EnterRouteWayByLF merchant message owner mismatch");
    Assert(merchantSay.Buff ==
           "天关统领/你给我的灵符在哪呢？要不你先去兑换一些？",
        "insufficient EnterRouteWayByLF merchant message payload mismatch");
}

static void CheckRouteMissingMapDoesNotRollback()
{
    ResetMapManager();
    var source = NewEnvironment("RouteMissingMapSource");
    RegisterMap(source);
    var player = NewPlacedPlayer(source, "route-missing-map", 5, 6);
    player.m_nLingFu = 2;
    player.m_nUsedLingFu = 20;
    var npc = new NormNpc
    {
        m_sCharName = "missing-map-npc",
        m_sMapName = "missing-map-npc-map"
    };
    var sequencer = new NativeMagicTowerRouteSequencer(
        _ => 0, 0, 0, 1, 0, 0);
    M2Share.MagicTowerRouteSequencer = sequencer;
    M2Share.LogStringList.Clear();
    var bridge = new PasApiBridge { CurrentPlayer = player, CurrentNpc = npc };

    Assert(bridge.CallNpcMethod("EnterRouteWayByLF", new List<PasValue>
    {
        PasValue.FromObject(player), PasValue.FromBool(false)
    }, out _), "missing-map EnterRouteWayByLF was not acknowledged");
    Assert(player.m_nLingFu == 1 && player.m_nUsedLingFu == 21,
        "missing-map EnterRouteWayByLF rolled back the LingFu debit");
    Assert(ReferenceEquals(player.m_PEnvir, source)
           && player.m_sMapName == source.sMapName
           && player.m_nCurrX == 5 && player.m_nCurrY == 6,
        "missing-map EnterRouteWayByLF changed the source location");
    Assert(ReadPlayerField<byte>(player,
               "m_btNativeMagicTowerPhase") == 1
           && ReadPlayerField<byte>(player,
               "m_btNativeMagicTowerSpecialRoute") == 1,
        "missing-map EnterRouteWayByLF rolled back native route state");
    var snapshot = sequencer.Snapshot();
    Assert(snapshot.TotalEntries == 1 && snapshot.Sequence == 1
           && snapshot.Threshold == 201 && snapshot.PaidEntries == 1,
        "missing-map EnterRouteWayByLF rolled back global route counters");
    Assert(M2Share.LogStringList.Count == 1,
        "missing-map EnterRouteWayByLF rolled back the debit log");
    Assert(player.m_MsgList.Count(message =>
            message.wIdent == Grobal2.RM_LINGFU_CHANGED) == 1
           && player.m_MsgList.All(message =>
               message.wIdent != Grobal2.RM_NATIVE_CHANGEMAP
               && message.wIdent != Grobal2.RM_CHANGEMAP),
        "missing-map EnterRouteWayByLF movement/refresh messages mismatch");
}

static void CheckRouteHundredthUsage()
{
    ResetMapManager();
    var source = NewEnvironment("RouteHundredthSource");
    var destination = NewEnvironment("D5071~0");
    RegisterMap(source);
    RegisterMap(destination);
    var player = NewPlacedPlayer(source, "route-hundredth", 7, 8);
    player.m_nLingFu = 1;
    player.m_nUsedLingFu = 99;
    var npc = new NormNpc
    {
        m_sCharName = "hundredth-npc",
        m_sMapName = "hundredth-npc-map"
    };
    M2Share.MagicTowerRouteSequencer =
        new NativeMagicTowerRouteSequencer(_ => 0);
    M2Share.LogStringList.Clear();
    var bridge = new PasApiBridge { CurrentPlayer = player, CurrentNpc = npc };

    Assert(bridge.CallNpcMethod("EnterRouteWayByLF", new List<PasValue>
    {
        PasValue.FromObject(player), PasValue.FromBool(true)
    }, out _), "hundredth EnterRouteWayByLF was rejected");
    Assert(player.m_nLingFu == 0 && player.m_nUsedLingFu == 101,
        "hundredth EnterRouteWayByLF did not apply native 100-to-101 usage");
    Assert(ReadPlayerField<bool>(player,
            "m_boNativeMagicTowerHundredth"),
        "hundredth EnterRouteWayByLF did not set native B89 state");
    Assert(ReferenceEquals(player.m_PEnvir, destination)
           && player.m_nCurrX == 11 && player.m_nCurrY == 13,
        "hundredth EnterRouteWayByLF did not complete the route move");
}

static void CheckRouteSequencerBoundaries()
{
    var randomCalls = 0;
    var threshold = new NativeMagicTowerRouteSequencer(_ =>
    {
        randomCalls++;
        return 0;
    }, 0, 9, 10, 0, 0);
    var thresholdEntry = threshold.Enter(false);
    var thresholdSnapshot = threshold.Snapshot();
    Assert(thresholdEntry.Sequence == 10 && thresholdEntry.SpecialRoute == 1,
        "route sequencer threshold entry mismatch");
    Assert(thresholdSnapshot.TotalEntries == 1
           && thresholdSnapshot.Threshold == 210
           && thresholdSnapshot.PaidEntries == 1
           && thresholdSnapshot.FreeEntries == 0 && randomCalls == 1,
        "route sequencer threshold update mismatch");

    var forced = new NativeMagicTowerRouteSequencer(
        _ => 17, 7, 2_499, 2_500, 4, 5);
    var forcedEntry = forced.Enter(false);
    var forcedSnapshot = forced.Snapshot();
    Assert(forcedEntry.Sequence == 2_500 && forcedEntry.SpecialRoute == 2,
        "route sequencer 2500 forced route did not override threshold route");
    Assert(forcedSnapshot.TotalEntries == 8
           && forcedSnapshot.Threshold == 2_717
           && forcedSnapshot.PaidEntries == 5
           && forcedSnapshot.FreeEntries == 5,
        "route sequencer 2500 counters/threshold mismatch");

    var wrap = new NativeMagicTowerRouteSequencer(
        _ => 0, 41, 9_999, 10_000, 7, 8);
    var tenThousand = wrap.Enter(true);
    var wrapped = wrap.Enter(true);
    var wrapSnapshot = wrap.Snapshot();
    Assert(tenThousand.Sequence == 10_000 && tenThousand.SpecialRoute == 5,
        "route sequencer 10000 forced route mismatch");
    Assert(wrapped.Sequence == 1 && wrapped.SpecialRoute == 0,
        "route sequencer did not wrap 10000 to 1");
    Assert(wrapSnapshot.TotalEntries == 43 && wrapSnapshot.Sequence == 1
           && wrapSnapshot.Threshold == 200
           && wrapSnapshot.PaidEntries == 7
           && wrapSnapshot.FreeEntries == 10,
        "route sequencer wrap counters/threshold mismatch");
}

static void CheckRouteInterpreterDispatch()
{
    ResetMapManager();
    var source = NewEnvironment("RouteInterpreterSource");
    var destination = NewEnvironment("D5071~0");
    RegisterMap(source);
    RegisterMap(destination);
    var player = NewPlacedPlayer(source, "route-interpreter", 9, 9);
    player.m_nLingFu = 1;
    var npc = new NormNpc { m_sCharName = "route-interpreter-npc" };
    M2Share.MagicTowerRouteSequencer =
        new NativeMagicTowerRouteSequencer(_ => 0);
    M2Share.LogStringList.Clear();
    var bridge = new PasApiBridge
    {
        CurrentPlayer = player,
        CurrentNpc = npc
    };
    const string sourceCode = """
        program RouteInterpreterProbe;
        procedure Probe;
        begin
          This_Npc.EnterRouteWayByLF(This_Player, True);
        end;
        begin
        end.
        """;
    var interpreter = new PasInterpreter(
        new PasParser(new PasLexer(sourceCode)).Parse(), bridge);

    var result = interpreter.ExecuteProcedure("Probe");
    Assert(result.Type == PasValueType.Nil,
        "interpreter route procedure result was not Nil");
    Assert(player.m_nLingFu == 0 && player.m_nUsedLingFu == 1,
        "interpreter route did not execute the native debit");
    Assert(ReferenceEquals(player.m_PEnvir, destination)
           && player.m_nCurrX == 11 && player.m_nCurrY == 13,
        "interpreter route did not execute the fixed movement");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void AssertNpcMethodClosed(PasApiBridge bridge, TPlayObject player,
    string name, List<PasValue> args, string message)
{
    var before = CapturePlayer(player);
    Assert(!bridge.CallNpcMethod(name, args, out _), message);
    AssertPlayerUnchanged(player, before, name);
}

static void AssertNpcFuncClosed(PasApiBridge bridge, TPlayObject player,
    string name, List<PasValue> args, string message)
{
    var before = CapturePlayer(player);
    Assert(!bridge.CallNpcFunc(name, args, out _), message);
    AssertPlayerUnchanged(player, before, name);
}

static (
    Envirnoment Envir,
    string MapName,
    string MapFileName,
    short CurrX,
    short CurrY,
    int LingFu,
    int UsedLingFu,
    int GameGold,
    int BagCount) CapturePlayer(TPlayObject player) => (
        player.m_PEnvir,
        player.m_sMapName,
        player.m_sMapFileName,
        player.m_nCurrX,
        player.m_nCurrY,
        player.m_nLingFu,
        player.m_nUsedLingFu,
        player.m_nGameGold,
        player.m_ItemList.Count);

static void AssertPlayerUnchanged(TPlayObject player,
    (Envirnoment Envir, string MapName, string MapFileName, short CurrX,
        short CurrY, int LingFu, int UsedLingFu, int GameGold, int BagCount) before,
    string api)
{
    Assert(ReferenceEquals(player.m_PEnvir, before.Envir), $"{api} changed player environment");
    Assert(player.m_sMapName == before.MapName, $"{api} changed player map name");
    Assert(player.m_sMapFileName == before.MapFileName, $"{api} changed player map file name");
    Assert(player.m_nCurrX == before.CurrX && player.m_nCurrY == before.CurrY,
        $"{api} changed player coordinates");
    Assert(player.m_nLingFu == before.LingFu && player.m_nUsedLingFu == before.UsedLingFu,
        $"{api} changed native LingFu accounting");
    Assert(player.m_nGameGold == before.GameGold, $"{api} changed yuanbao balance");
    Assert(player.m_ItemList.Count == before.BagCount, $"{api} changed bag item count");
}

static void SetPlayerField<T>(TPlayObject player, string fieldName, T value)
{
    var field = typeof(TPlayObject).GetField(fieldName,
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(field != null, $"native prize field is missing: {fieldName}");
    field!.SetValue(player, value);
}

static (
    byte Phase,
    byte Defeated,
    int Experience,
    int LingFu,
    int Gold,
    int BagCount,
    int MessageCount,
    TBaseObject Npc) CapturePrizeDispatchPlayer(TPlayObject player) => (
        ReadPlayerField<byte>(player, "m_btNativeMagicTowerPhase"),
        ReadPlayerField<byte>(player,
            "m_btNativeMagicTowerDefeatedMonsterCount"),
        player.m_Abil.Exp,
        player.m_nLingFu,
        player.m_nGold,
        player.m_ItemList.Count,
        player.m_MsgList.Count,
        player.m_NPC);

static void AssertPrizeDispatchPlayerUnchanged(TPlayObject player,
    (byte Phase, byte Defeated, int Experience, int LingFu, int Gold,
        int BagCount, int MessageCount, TBaseObject Npc) before,
    string scenario)
{
    var after = CapturePrizeDispatchPlayer(player);
    Assert(after.Equals(before), $"{scenario} changed player state");
}

static void ResetMapManager()
{
    M2Share.MapManager = new MapManager();
}

static Envirnoment NewEnvironment(string mapName)
{
    var environment = new Envirnoment
    {
        sMapName = mapName,
        m_sMapFileName = mapName,
        nServerIndex = 0
    };
    typeof(Envirnoment).GetMethod("Initialize",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(environment, new object[] { (short)32, (short)32 });
    return environment;
}

static void RegisterMap(Envirnoment environment)
{
    var maps = (IDictionary<string, Envirnoment>)typeof(MapManager)
        .GetField("m_MapList", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(M2Share.MapManager)!;
    maps.Add(environment.sMapName, environment);
}

static TPlayObject NewPlacedPlayer(Envirnoment environment, string name,
    short x, short y)
{
    var player = new TPlayObject
    {
        m_sCharName = name,
        m_PEnvir = environment,
        m_sMapName = environment.sMapName,
        m_sMapFileName = environment.m_sMapFileName,
        m_nCurrX = x,
        m_nCurrY = y
    };
    player.m_boAddToMaped = false;
    player.m_boDelFormMaped = false;
    Assert(ReferenceEquals(player, environment.AddToMap(x, y,
            CellType.OS_MOVINGOBJECT, player)),
        "could not place route player on the source map");
    return player;
}

static T ReadPlayerField<T>(TPlayObject player, string fieldName)
{
    var field = typeof(TPlayObject).GetField(fieldName,
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(field != null, $"native route field is missing: {fieldName}");
    return (T)field!.GetValue(player)!;
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
