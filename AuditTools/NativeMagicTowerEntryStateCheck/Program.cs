using System.Collections;
using System.Globalization;
using System.Reflection;
using GameSvr;
using GameSvr.PasEngine;
using GameSvr.Services;
using SystemModule;

PrepareRuntimeConfig();

var envirRoot = Environment.GetEnvironmentVariable("LYOMIR_PRODUCTION_ENVIR")
    ?? @"D:\lyom2Release\mud2.0\Mir200\Envir";
var mapRoot = Environment.GetEnvironmentVariable("LYOMIR_PRODUCTION_MAP")
    ?? @"D:\lyom2Release\mud2.0\Mir200\Map";
Assert(Directory.Exists(envirRoot), "production Envir missing: " + envirRoot);
Assert(Directory.Exists(mapRoot), "production Map missing: " + mapRoot);
Assert(NativeDynamicRoomDefinitionLoader.TryLoad(
        Path.Combine(envirRoot, "PsDynNpc.txt"), out var definitions,
        out var definitionErrors),
    "production definitions failed: " + string.Join(" | ", definitionErrors));
var newSky = definitions.Single(definition =>
    definition.RoomName == "NewSky");
var physicalCount = int.Parse(newSky.RawRoomCount,
    CultureInfo.InvariantCulture);
Equal(10, physicalCount, "production NewSky room count");

InitializeRuntime(envirRoot, mapRoot);
var npc = new NormNpc
{
    m_sCharName = "天关守卫",
    m_sMapName = "D5071~0"
};

CheckResetHelper();
CheckNewGuanSilent(npc);
CheckNewGuanLevelFailure(npc);
CheckNextLevelFailure(npc);
CheckNextLingFuFailure(npc);
CheckNext2Silent(npc);
CheckNext2Failure(npc);
CheckBridgeMalformedCalls(npc);
CheckBridgeNewGuan(npc);
CheckBridgeNext(npc);
CheckBridgeNext2(npc);
CheckBridgeEnterMySteryAbi(npc);
CheckNewGuanSuccess(npc);
CheckNextSuccess(npc);
FillRemainingRooms(physicalCount);
CheckRoomFullFailures(npc, physicalCount);

Console.WriteLine(
    "NativeMagicTowerEntryStateCheck PASS " +
    "room=production-NewSky@40,40 " +
    "new=phase1+level+atomic-reset next=preclear+paid-route " +
    "next2=gate/prelude archer=D2B/count/chance/slots " +
    "bridge=valid+malformed+explicit-owner+Nil-result " +
    "mystery=procedure/explicit/next2-equivalent/reject " +
    "failures=exact-message/no-debit/no-inventory-reset");

static void CheckResetHelper()
{
    var player = new TPlayObject();
    player.m_btNativeMagicTowerDefeatedMonsterCount = 9;
    player.m_sbNativeMagicTowerArcherCount = 8;
    player.m_btNativeMagicTowerEngageChance = 0;
    var slots = ReadSlots(player);
    Array.Fill(slots, (byte)1);

    player.ResetNativeMagicTowerArcherState();

    Equal((byte)0, player.m_btNativeMagicTowerDefeatedMonsterCount,
        "reset D2B");
    Equal((sbyte)0, player.m_sbNativeMagicTowerArcherCount, "reset count");
    Equal((byte)1, player.m_btNativeMagicTowerEngageChance, "reset chance");
    Assert(ReadSlots(player).All(value => value == 0), "reset slots");
}

static void CheckNewGuanSilent(NormNpc npc)
{
    var source = NewEnvironment("new-silent-source");
    var player = NewPlacedPlayer(source, "new-silent", 3, 4);
    player.m_btNativeMagicTowerPhase = 0;
    player.m_btNativeMagicTowerRoomKind = 7;
    player.m_btNativeMagicTowerDefeatedMonsterCount = 8;
    player.m_sbNativeMagicTowerArcherCount = 6;
    player.m_btNativeMagicTowerEngageChance = 0;
    Array.Fill(ReadSlots(player), (byte)1);
    AddBagFixture(player);
    var before = Snapshot(player);

    player.EnterNativeMagicTowerNewGuan(npc, M2Share.DynamicRoomService);

    Equal(before, Snapshot(player), "phase mismatch side effects");
    AssertPosition(player, source, 3, 4, "phase mismatch");
    Equal(0, player.m_MsgList.Count, "phase mismatch messages");
    Assert(player.m_NPC == null, "phase mismatch NPC binding");
}

static void CheckNewGuanLevelFailure(NormNpc npc)
{
    var source = NewEnvironment("new-level-source");
    var player = NewPlacedPlayer(source, "new-level", 5, 6);
    player.m_btNativeMagicTowerPhase = 1;
    player.m_Abil.Level = 24;
    player.m_wNativeMagicTowerEntryLevelGate = 1;
    AddBagFixture(player);
    var before = Snapshot(player);

    player.EnterNativeMagicTowerNewGuan(npc, M2Share.DynamicRoomService);

    Equal(before, Snapshot(player), "NewGuan level failure state");
    AssertPosition(player, source, 5, 6, "NewGuan level failure");
    AssertMerchantSay(player, npc,
        "你的等级不够，不能去魔王岭拦截怪物。",
        "NewGuan level failure");
}

static void CheckNextLevelFailure(NormNpc npc)
{
    var source = NewEnvironment("next-level-source");
    var player = NewPlacedPlayer(source, "next-level", 7, 8);
    player.m_Abil.Level = 24;
    player.m_wNativeMagicTowerEntryLevelGate = 1;
    player.m_btNativeMagicTowerNextGate = 7;
    player.m_btNativeMagicTowerMysteryFlag = 3;
    player.m_nLingFu = 5;
    AddBagFixture(player);
    var before = Snapshot(player) with { NextGate = 0 };
    var route = new NativeMagicTowerRouteSequencer(_ => 0,
        10, 20, 30, 40, 50);
    var routeBefore = route.Snapshot();

    player.EnterNativeMagicTowerNext(npc, false,
        M2Share.DynamicRoomService, route);

    Equal(before, Snapshot(player), "Next level failure state");
    AssertPosition(player, source, 7, 8, "Next level failure");
    AssertMerchantSay(player, npc,
        "你的等级不够，不能去魔王岭拦截怪物。",
        "Next level failure");
    Assert(RouteEquals(routeBefore, route.Snapshot()),
        "Next level failure changed route");
}

static void CheckNextLingFuFailure(NormNpc npc)
{
    var source = NewEnvironment("next-lf-source");
    var player = NewPlacedPlayer(source, "next-lf", 9, 10);
    player.m_Abil.Level = 24;
    player.m_wNativeMagicTowerEntryLevelGate = 0;
    player.m_btNativeMagicTowerNextGate = 1;
    player.m_btNativeMagicTowerMysteryFlag = 4;
    AddBagFixture(player);
    var before = Snapshot(player) with { NextGate = 0 };

    player.EnterNativeMagicTowerNext(npc, false,
        M2Share.DynamicRoomService,
        new NativeMagicTowerRouteSequencer(_ => 0));

    Equal(before, Snapshot(player), "Next LingFu failure state");
    AssertPosition(player, source, 9, 10, "Next LingFu failure");
    AssertMerchantSay(player, npc, "你至少需要1张灵符",
        "Next LingFu failure");
    Equal(0, player.m_MsgList.Count(message =>
        message.wIdent == Grobal2.RM_SYSMESSAGE),
        "Next generic LingFu message");
}

static void CheckNext2Silent(NormNpc npc)
{
    var source = NewEnvironment("next2-silent-source");
    var player = NewPlacedPlayer(source, "next2-silent", 11, 12);
    player.m_btNativeMagicTowerNextGate = 0;
    player.m_btNativeMagicTowerMysteryFlag = 6;
    player.m_nLingFu = 4;
    AddBagFixture(player);
    var before = Snapshot(player);

    player.EnterNativeMagicTowerNext2(npc, M2Share.DynamicRoomService,
        new NativeMagicTowerRouteSequencer(_ => 0));

    Equal(before, Snapshot(player), "Next2 closed-gate state");
    AssertPosition(player, source, 11, 12, "Next2 closed gate");
    Equal(0, player.m_MsgList.Count, "Next2 closed-gate messages");
}

static void CheckNext2Failure(NormNpc npc)
{
    var source = NewEnvironment("next2-failure-source");
    var player = NewPlacedPlayer(source, "next2-failure", 13, 14);
    player.m_btNativeMagicTowerNextGate = 1;
    player.m_btNativeMagicTowerMysteryFlag = 0;
    AddBagFixture(player);
    var before = Snapshot(player) with
    {
        NextGate = 0,
        MysteryFlag = 1
    };

    player.EnterNativeMagicTowerNext2(npc, M2Share.DynamicRoomService,
        new NativeMagicTowerRouteSequencer(_ => 0));

    Equal(before, Snapshot(player), "Next2 failure prelude state");
    AssertPosition(player, source, 13, 14, "Next2 failure");
    AssertMerchantSay(player, npc, "你至少需要1张灵符",
        "Next2 failure");
}

static void CheckBridgeMalformedCalls(NormNpc npc)
{
    var source = NewEnvironment("bridge-malformed-source");
    var player = NewPlacedPlayer(source, "bridge-malformed", 23, 24);
    player.m_Abil.Level = 25;
    player.m_btNativeMagicTowerPhase = 1;
    player.m_btNativeMagicTowerNextGate = 7;
    player.m_btNativeMagicTowerMysteryFlag = 6;
    player.m_nLingFu = 5;
    player.m_nUsedLingFu = 31;
    AddBagFixture(player);

    var contextSource = NewEnvironment("bridge-malformed-context-source");
    var contextPlayer = NewPlacedPlayer(contextSource,
        "bridge-malformed-context", 25, 26);
    contextPlayer.m_nLingFu = 9;
    var bridge = new PasApiBridge
    {
        CurrentPlayer = contextPlayer,
        CurrentNpc = npc
    };
    var playerArg = PasValue.FromObject(player);
    var playerBefore = Snapshot(player);
    var contextBefore = Snapshot(contextPlayer);
    var activeRoomsBefore = ActiveRoomCount(M2Share.DynamicRoomService,
        "NewSky");

    void RejectMethod(string name, List<PasValue> args, string caseName)
    {
        Assert(!bridge.CallNpcMethod(name, args, out var result),
            caseName + " was acknowledged");
        Equal(PasValueType.Nil, result.Type, caseName + " result");
        Equal(playerBefore, Snapshot(player), caseName + " explicit state");
        AssertPosition(player, source, 23, 24,
            caseName + " explicit position");
        Equal(contextBefore, Snapshot(contextPlayer),
            caseName + " context state");
        AssertPosition(contextPlayer, contextSource, 25, 26,
            caseName + " context position");
        Equal(activeRoomsBefore,
            ActiveRoomCount(M2Share.DynamicRoomService, "NewSky"),
            caseName + " room activation");
    }

    RejectMethod("EnterNewGuan", new List<PasValue>(),
        "NewGuan missing player");
    RejectMethod("EnterNewGuan", new List<PasValue>
    {
        PasValue.FromInt(1)
    }, "NewGuan non-player");
    RejectMethod("EnterNewGuan", new List<PasValue>
    {
        playerArg, PasValue.FromInt(1)
    }, "NewGuan extra argument");

    RejectMethod("EnterNext", new List<PasValue> { playerArg },
        "Next missing Boolean");
    RejectMethod("EnterNext", new List<PasValue>
    {
        PasValue.FromInt(1), PasValue.FromBool(false)
    }, "Next non-player");
    RejectMethod("EnterNext", new List<PasValue>
    {
        playerArg, PasValue.FromInt(0)
    }, "Next Integer Boolean");
    RejectMethod("EnterNext", new List<PasValue>
    {
        playerArg, PasValue.FromString("FALSE")
    }, "Next String Boolean");
    RejectMethod("EnterNext", new List<PasValue>
    {
        playerArg, PasValue.FromBool(false), PasValue.FromBool(false)
    }, "Next extra argument");

    RejectMethod("EnterNext2", new List<PasValue>(),
        "Next2 missing player");
    RejectMethod("EnterNext2", new List<PasValue>
    {
        PasValue.FromInt(1)
    }, "Next2 non-player");
    RejectMethod("EnterNext2", new List<PasValue>
    {
        playerArg, PasValue.FromBool(false)
    }, "Next2 extra argument");

    Assert(!bridge.CallNpcFunc("EnterNewGuan",
            new List<PasValue> { playerArg }, out var newFunctionResult)
           && newFunctionResult.Type == PasValueType.Nil,
        "NewGuan was exposed through function dispatcher");
    Assert(!bridge.CallNpcFunc("EnterNext", new List<PasValue>
        {
            playerArg, PasValue.FromBool(false)
        }, out var nextFunctionResult)
           && nextFunctionResult.Type == PasValueType.Nil,
        "Next was exposed through function dispatcher");
    Assert(!bridge.CallNpcFunc("EnterNext2",
            new List<PasValue> { playerArg }, out var next2FunctionResult)
           && next2FunctionResult.Type == PasValueType.Nil,
        "Next2 was exposed through function dispatcher");
    Equal(playerBefore, Snapshot(player),
        "wrong dispatcher explicit state");
    AssertPosition(player, source, 23, 24,
        "wrong dispatcher explicit position");
    Equal(contextBefore, Snapshot(contextPlayer),
        "wrong dispatcher context state");
    AssertPosition(contextPlayer, contextSource, 25, 26,
        "wrong dispatcher context position");
    Equal(activeRoomsBefore,
        ActiveRoomCount(M2Share.DynamicRoomService, "NewSky"),
        "wrong dispatcher room activation");
}

static void CheckBridgeNewGuan(NormNpc npc)
{
    var source = NewEnvironment("bridge-new-source");
    var player = NewPlacedPlayer(source, "bridge-new-explicit", 27, 28);
    player.m_Abil.Level = 25;
    player.m_btNativeMagicTowerPhase = 1;
    player.m_btNativeMagicTowerRoomKind = 8;
    player.m_btNativeMagicTowerDefeatedMonsterCount = 7;
    player.m_sbNativeMagicTowerArcherCount = 6;
    Array.Fill(ReadSlots(player), (byte)1);
    AddBagFixture(player);

    var contextSource = NewEnvironment("bridge-new-context-source");
    var contextPlayer = NewPlacedPlayer(contextSource,
        "bridge-new-context", 29, 30);
    contextPlayer.m_btNativeMagicTowerPhase = 9;
    contextPlayer.m_nLingFu = 11;
    var contextBefore = Snapshot(contextPlayer);
    var activeRoomsBefore = ActiveRoomCount(M2Share.DynamicRoomService,
        "NewSky");
    var bridge = new PasApiBridge
    {
        CurrentPlayer = contextPlayer,
        CurrentNpc = npc
    };

    Assert(bridge.CallNpcMethod("EnterNewGuan", new List<PasValue>
        {
            PasValue.FromObject(player)
        }, out var result), "bridge NewGuan valid ABI rejected");

    Equal(PasValueType.Nil, result.Type, "bridge NewGuan procedure result");
    AssertDynamicPosition(player, "bridge NewGuan explicit player");
    AssertInitializedEntry(player, "bridge NewGuan explicit player");
    AssertBagCleared(player, "bridge NewGuan explicit player");
    Equal(contextBefore, Snapshot(contextPlayer),
        "bridge NewGuan context state");
    AssertPosition(contextPlayer, contextSource, 29, 30,
        "bridge NewGuan context position");
    Equal(activeRoomsBefore + 1,
        ActiveRoomCount(M2Share.DynamicRoomService, "NewSky"),
        "bridge NewGuan room activation");
}

static void CheckBridgeNext(NormNpc npc)
{
    M2Share.LogStringList.Clear();
    var source = NewEnvironment("bridge-next-source");
    var player = NewPlacedPlayer(source, "bridge-next-explicit", 31, 32);
    player.m_Abil.Level = 25;
    player.m_btNativeMagicTowerNextGate = 8;
    player.m_btNativeMagicTowerMysteryFlag = 4;
    player.m_nLingFu = 2;
    player.m_nUsedLingFu = 40;
    AddBagFixture(player);

    var contextSource = NewEnvironment("bridge-next-context-source");
    var contextPlayer = NewPlacedPlayer(contextSource,
        "bridge-next-context", 33, 34);
    contextPlayer.m_btNativeMagicTowerNextGate = 5;
    contextPlayer.m_nLingFu = 13;
    var contextBefore = Snapshot(contextPlayer);
    var activeRoomsBefore = ActiveRoomCount(M2Share.DynamicRoomService,
        "NewSky");
    var route = new NativeMagicTowerRouteSequencer(_ => 0,
        3, 10, 500, 2, 1);
    M2Share.MagicTowerRouteSequencer = route;
    var bridge = new PasApiBridge
    {
        CurrentPlayer = contextPlayer,
        CurrentNpc = npc
    };

    Assert(bridge.CallNpcMethod("EnterNext", new List<PasValue>
        {
            PasValue.FromObject(player), PasValue.FromBool(false)
        }, out var result), "bridge Next valid ABI rejected");

    Equal(PasValueType.Nil, result.Type, "bridge Next procedure result");
    AssertDynamicPosition(player, "bridge Next explicit player");
    AssertInitializedEntry(player, "bridge Next explicit player");
    AssertBagCleared(player, "bridge Next explicit player");
    Equal((byte)0, player.m_btNativeMagicTowerNextGate,
        "bridge Next Boolean false preclear");
    Equal((byte)4, player.m_btNativeMagicTowerMysteryFlag,
        "bridge Next mystery state");
    Equal(1, player.m_nLingFu, "bridge Next LingFu debit");
    Equal(41, player.m_nUsedLingFu, "bridge Next used LingFu");
    Equal(4, route.Snapshot().TotalEntries,
        "bridge Next route sequence");
    Equal(contextBefore, Snapshot(contextPlayer),
        "bridge Next context state");
    AssertPosition(contextPlayer, contextSource, 33, 34,
        "bridge Next context position");
    Equal(activeRoomsBefore + 1,
        ActiveRoomCount(M2Share.DynamicRoomService, "NewSky"),
        "bridge Next room activation");
}

static void CheckBridgeNext2(NormNpc npc)
{
    M2Share.LogStringList.Clear();
    var source = NewEnvironment("bridge-next2-source");
    var player = NewPlacedPlayer(source, "bridge-next2-explicit", 35, 36);
    player.m_Abil.Level = 25;
    player.m_btNativeMagicTowerNextGate = 1;
    player.m_btNativeMagicTowerMysteryFlag = 7;
    player.m_nLingFu = 2;
    player.m_nUsedLingFu = 50;
    AddBagFixture(player);

    var contextSource = NewEnvironment("bridge-next2-context-source");
    var contextPlayer = NewPlacedPlayer(contextSource,
        "bridge-next2-context", 37, 38);
    contextPlayer.m_btNativeMagicTowerNextGate = 9;
    contextPlayer.m_btNativeMagicTowerMysteryFlag = 8;
    contextPlayer.m_nLingFu = 15;
    var contextBefore = Snapshot(contextPlayer);
    var activeRoomsBefore = ActiveRoomCount(M2Share.DynamicRoomService,
        "NewSky");
    var route = new NativeMagicTowerRouteSequencer(_ => 0,
        6, 20, 700, 5, 4);
    M2Share.MagicTowerRouteSequencer = route;
    var bridge = new PasApiBridge
    {
        CurrentPlayer = contextPlayer,
        CurrentNpc = npc
    };

    Assert(bridge.CallNpcMethod("EnterNext2", new List<PasValue>
        {
            PasValue.FromObject(player)
        }, out var result), "bridge Next2 valid ABI rejected");

    Equal(PasValueType.Nil, result.Type, "bridge Next2 procedure result");
    AssertDynamicPosition(player, "bridge Next2 explicit player");
    AssertInitializedEntry(player, "bridge Next2 explicit player");
    AssertBagCleared(player, "bridge Next2 explicit player");
    Equal((byte)0, player.m_btNativeMagicTowerNextGate,
        "bridge Next2 gate");
    Equal((byte)1, player.m_btNativeMagicTowerMysteryFlag,
        "bridge Next2 prelude");
    Equal(1, player.m_nLingFu, "bridge Next2 LingFu debit");
    Equal(51, player.m_nUsedLingFu, "bridge Next2 used LingFu");
    Equal(7, route.Snapshot().TotalEntries,
        "bridge Next2 route sequence");
    Equal(contextBefore, Snapshot(contextPlayer),
        "bridge Next2 context state");
    AssertPosition(contextPlayer, contextSource, 37, 38,
        "bridge Next2 context position");
    Equal(activeRoomsBefore + 1,
        ActiveRoomCount(M2Share.DynamicRoomService, "NewSky"),
        "bridge Next2 room activation");
}

static void CheckBridgeEnterMySteryAbi(NormNpc npc)
{
    M2Share.LogStringList.Clear();
    var source = NewEnvironment("bridge-mystery-source");
    var player = NewPlacedPlayer(source, "bridge-mystery-explicit", 41, 42);
    player.m_Abil.Level = 25;
    player.m_btNativeMagicTowerNextGate = 1;
    player.m_btNativeMagicTowerMysteryFlag = 7;
    player.m_nLingFu = 2;
    player.m_nUsedLingFu = 60;
    AddBagFixture(player);

    var referenceSource = NewEnvironment("bridge-mystery-reference-source");
    var referencePlayer = NewPlacedPlayer(referenceSource,
        "bridge-mystery-reference", 43, 44);
    referencePlayer.m_Abil.Level = 25;
    referencePlayer.m_btNativeMagicTowerNextGate = 1;
    referencePlayer.m_btNativeMagicTowerMysteryFlag = 7;
    referencePlayer.m_nLingFu = 2;
    referencePlayer.m_nUsedLingFu = 60;
    AddBagFixture(referencePlayer);

    var contextSource = NewEnvironment("bridge-mystery-context-source");
    var contextPlayer = NewPlacedPlayer(contextSource,
        "bridge-mystery-context", 45, 46);
    contextPlayer.m_Abil.Level = 25;
    contextPlayer.m_btNativeMagicTowerNextGate = 1;
    contextPlayer.m_btNativeMagicTowerMysteryFlag = 9;
    contextPlayer.m_nLingFu = 20;
    contextPlayer.m_nUsedLingFu = 70;
    AddBagFixture(contextPlayer);
    var contextBefore = Snapshot(contextPlayer);
    var activeRoomsBefore = ActiveRoomCount(M2Share.DynamicRoomService,
        "NewSky");
    var route = new NativeMagicTowerRouteSequencer(_ => 0,
        6, 20, 700, 5, 4);
    M2Share.MagicTowerRouteSequencer = route;
    var bridge = new PasApiBridge
    {
        CurrentPlayer = contextPlayer,
        CurrentNpc = npc
    };
    var playerArg = PasValue.FromObject(player);

    // Native EnterMyStery is a one-player procedure wrapper around EnterNext2.
    Assert(bridge.CallNpcMethod("EnterMyStery",
            new List<PasValue> { playerArg }, out var result),
        "bridge EnterMyStery valid ABI rejected");
    Equal(PasValueType.Nil, result.Type,
        "bridge EnterMyStery procedure result");

    var referenceRoute = new NativeMagicTowerRouteSequencer(_ => 0,
        6, 20, 700, 5, 4);
    referencePlayer.EnterNativeMagicTowerNext2(npc,
        M2Share.DynamicRoomService, referenceRoute);

    Equal(Snapshot(referencePlayer), Snapshot(player),
        "bridge EnterMyStery EnterNext2 state equivalence");
    AssertDynamicPosition(player,
        "bridge EnterMyStery explicit player");
    AssertDynamicPosition(referencePlayer,
        "bridge EnterMyStery EnterNext2 reference");
    Assert(RouteEquals(referenceRoute.Snapshot(), route.Snapshot()),
        "bridge EnterMyStery EnterNext2 route equivalence");
    Equal(contextBefore, Snapshot(contextPlayer),
        "bridge EnterMyStery context state");
    AssertPosition(contextPlayer, contextSource, 45, 46,
        "bridge EnterMyStery context position");
    Equal(activeRoomsBefore + 2,
        ActiveRoomCount(M2Share.DynamicRoomService, "NewSky"),
        "bridge EnterMyStery room activation");

    var playerAfter = Snapshot(player);
    var playerEnvironment = player.m_PEnvir;
    var roomsAfter = ActiveRoomCount(M2Share.DynamicRoomService, "NewSky");

    void RejectProcedure(List<PasValue> args, string caseName)
    {
        Assert(!bridge.CallNpcMethod("EnterMyStery", args,
                out var rejectedResult),
            caseName + " was acknowledged");
        Equal(PasValueType.Nil, rejectedResult.Type, caseName + " result");
        Equal(playerAfter, Snapshot(player), caseName + " explicit state");
        AssertPosition(player, playerEnvironment, 40, 40,
            caseName + " explicit position");
        Equal(contextBefore, Snapshot(contextPlayer),
            caseName + " context state");
        AssertPosition(contextPlayer, contextSource, 45, 46,
            caseName + " context position");
        Equal(roomsAfter,
            ActiveRoomCount(M2Share.DynamicRoomService, "NewSky"),
            caseName + " room activation");
    }

    RejectProcedure(new List<PasValue>(),
        "EnterMyStery missing player");
    RejectProcedure(new List<PasValue> { PasValue.FromInt(1) },
        "EnterMyStery non-player");
    RejectProcedure(new List<PasValue>
    {
        playerArg, PasValue.FromBool(false)
    }, "EnterMyStery extra argument");

    Assert(!bridge.CallNpcFunc("EnterMyStery",
            new List<PasValue> { playerArg }, out var functionResult)
           && functionResult.Type == PasValueType.Nil,
        "EnterMyStery was exposed through function dispatcher");
    Equal(playerAfter, Snapshot(player),
        "EnterMyStery function explicit state");
    AssertPosition(player, playerEnvironment, 40, 40,
        "EnterMyStery function explicit position");
    Equal(contextBefore, Snapshot(contextPlayer),
        "EnterMyStery function context state");
    AssertPosition(contextPlayer, contextSource, 45, 46,
        "EnterMyStery function context position");
    Equal(roomsAfter,
        ActiveRoomCount(M2Share.DynamicRoomService, "NewSky"),
        "EnterMyStery function room activation");
}

static void CheckNewGuanSuccess(NormNpc npc)
{
    var source = NewEnvironment("new-success-source");
    var player = NewPlacedPlayer(source, "new-success", 15, 16);
    player.m_btNativeMagicTowerPhase = 1;
    player.m_btNativeMagicTowerRoomKind = 9;
    player.m_btNativeMagicTowerDefeatedMonsterCount = 7;
    player.m_sbNativeMagicTowerArcherCount = 5;
    player.m_btNativeMagicTowerEngageChance = 0;
    Array.Fill(ReadSlots(player), (byte)1);
    player.m_nLingFu = 6;
    player.m_nUsedLingFu = 12;
    AddBagFixture(player);

    player.EnterNativeMagicTowerNewGuan(npc,
        M2Share.DynamicRoomService);

    AssertDynamicPosition(player, "NewGuan success");
    AssertInitializedEntry(player, "NewGuan success");
    AssertBagCleared(player, "NewGuan success");
    Equal(6, player.m_nLingFu, "NewGuan success LingFu");
    Equal(12, player.m_nUsedLingFu, "NewGuan success used LingFu");
    Equal(0, player.m_MsgList.Count(message =>
        message.wIdent == Grobal2.RM_MERCHANTSAY),
        "NewGuan success merchant message");
}

static void CheckNextSuccess(NormNpc npc)
{
    M2Share.LogStringList.Clear();
    var source = NewEnvironment("next-success-source");
    var player = NewPlacedPlayer(source, "next-success", 17, 18);
    player.m_btNativeMagicTowerPhase = 8;
    player.m_btNativeMagicTowerRoomKind = 9;
    player.m_btNativeMagicTowerNextGate = 5;
    player.m_btNativeMagicTowerMysteryFlag = 4;
    player.m_btNativeMagicTowerSpecialRoute = 9;
    player.m_boNativeMagicTowerHundredth = false;
    player.m_btNativeMagicTowerDefeatedMonsterCount = 7;
    player.m_sbNativeMagicTowerArcherCount = 5;
    player.m_btNativeMagicTowerEngageChance = 0;
    Array.Fill(ReadSlots(player), (byte)1);
    player.m_nLingFu = 3;
    player.m_nUsedLingFu = 99;
    AddBagFixture(player);
    var route = new NativeMagicTowerRouteSequencer(_ => 0,
        10, 2_499, 8_000, 7, 8);

    player.EnterNativeMagicTowerNext(npc, false,
        M2Share.DynamicRoomService, route);

    AssertDynamicPosition(player, "Next success");
    AssertInitializedEntry(player, "Next success");
    AssertBagCleared(player, "Next success");
    Equal((byte)0, player.m_btNativeMagicTowerNextGate,
        "Next success next gate");
    Equal((byte)4, player.m_btNativeMagicTowerMysteryFlag,
        "Next success mystery flag");
    Equal((byte)2, player.m_btNativeMagicTowerSpecialRoute,
        "Next success 2500 route");
    Assert(player.m_boNativeMagicTowerHundredth,
        "Next success hundredth flag");
    Equal(2, player.m_nLingFu, "Next success LingFu debit");
    Equal(101, player.m_nUsedLingFu, "Next success used LingFu");
    var snapshot = route.Snapshot();
    Equal(11, snapshot.TotalEntries, "Next success total entries");
    Equal(2_500, snapshot.Sequence, "Next success sequence");
    Equal(8, snapshot.PaidEntries, "Next success paid entries");
    Equal(8, snapshot.FreeEntries, "Next success free entries");
    Equal(2, player.m_MsgList.Count(message =>
        message.wIdent == Grobal2.RM_LINGFU_CHANGED),
        "Next success LingFu refreshes");
    Equal(1, M2Share.LogStringList.Count,
        "Next success selector 101 log count");
    // Re-based 2026-08-04: reason is 战神 sub_646F40 @0x646F89 `mov dl,1`, not 0 (see the
    // sibling pin in NativeMagicTowerGetEngageChanceCheck). This audit was the one that went
    // RED against production's hardcoded 0 — and it was red for the wrong reason: its own
    // expectation was also 0, so it agreed with the bug; the failure came from elsewhere.
    Equal("101\tNewSky\t40\t40\tnext-success\t魔王岭消耗灵符\t1\t1\t天关守卫-D5071~0",
        (string)M2Share.LogStringList[0],
        "Next success selector 101 payload");
}

static void FillRemainingRooms(int expectedCount)
{
    var attempt = 0;
    while (M2Share.DynamicRoomService.HasFreeDynamicRoom("NewSky"))
    {
        attempt++;
        Assert(attempt <= expectedCount, "NewSky fill loop overflow");
        Assert(M2Share.DynamicRoomService.TryReserveActivatedRoom(
                "NewSky", new TPlayObject
                {
                    m_sCharName = "NewSky-fill-" + attempt
                }, out _),
            "could not reserve NewSky room");
    }
    Equal(expectedCount,
        ActiveRoomCount(M2Share.DynamicRoomService, "NewSky"),
        "full NewSky activation count");
}

static void CheckRoomFullFailures(NormNpc npc, int expectedCount)
{
    var nextSource = NewEnvironment("next-full-source");
    var next = NewPlacedPlayer(nextSource, "next-full", 19, 20);
    next.m_btNativeMagicTowerNextGate = 3;
    next.m_btNativeMagicTowerPhase = 7;
    next.m_btNativeMagicTowerRoomKind = 8;
    next.m_btNativeMagicTowerDefeatedMonsterCount = 6;
    next.m_sbNativeMagicTowerArcherCount = 5;
    next.m_btNativeMagicTowerEngageChance = 0;
    Array.Fill(ReadSlots(next), (byte)1);
    next.m_nLingFu = 5;
    next.m_nUsedLingFu = 6;
    AddBagFixture(next);
    var nextBefore = Snapshot(next) with { NextGate = 0 };
    var route = new NativeMagicTowerRouteSequencer(_ => 0,
        1, 2, 3, 4, 5);
    var routeBefore = route.Snapshot();

    next.EnterNativeMagicTowerNext(npc, false,
        M2Share.DynamicRoomService, route);

    Equal(nextBefore, Snapshot(next), "Next room-full state");
    AssertPosition(next, nextSource, 19, 20, "Next room full");
    AssertMerchantSay(next, npc, "天关房间满员,请稍候再试...",
        "Next room full");
    Assert(RouteEquals(routeBefore, route.Snapshot()),
        "Next room full changed route");

    var newSource = NewEnvironment("new-full-source");
    var newPlayer = NewPlacedPlayer(newSource, "new-full", 21, 22);
    newPlayer.m_btNativeMagicTowerPhase = 1;
    newPlayer.m_btNativeMagicTowerRoomKind = 7;
    newPlayer.m_nLingFu = 4;
    AddBagFixture(newPlayer);
    var newBefore = Snapshot(newPlayer);

    newPlayer.EnterNativeMagicTowerNewGuan(npc,
        M2Share.DynamicRoomService);

    Equal(newBefore, Snapshot(newPlayer), "NewGuan room-full state");
    AssertPosition(newPlayer, newSource, 21, 22, "NewGuan room full");
    AssertMerchantSay(newPlayer, npc, "天关房间满员,请稍候再试...",
        "NewGuan room full");
    Equal(expectedCount,
        ActiveRoomCount(M2Share.DynamicRoomService, "NewSky"),
        "room-full activation count");
}

static void InitializeRuntime(string envirRoot, string mapRoot)
{
    M2Share.g_Config = new GameSvrConfig();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new ArrayList();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.MapManager = new MapManager();
    M2Share.EventManager = new EventManager();
    M2Share.g_MonSayMsgList =
        new Dictionary<string, IList<TMonSayMsg>>();
    M2Share.UserEngine = new UserEngine();
    M2Share.UserEngine.StdItemList.Clear();
    M2Share.UserEngine.StdItemList.Add(new GoodItem { Name = "弩牌" });
    M2Share.UserEngine.StdItemList.Add(new GoodItem { Name = "保留物品" });
    M2Share.RandomNumber = RandomNumber.GetInstance();
    M2Share.nServerIndex = 0;
    M2Share.CreditCardService = NativeCreditCardService.Disabled;
    M2Share.DynamicRoomManager = new NativeDynamicRoomManager();
    M2Share.DynamicRoomPasRoutes = new NativeDynamicRoomPasScriptRouteTable(
        Path.Combine(envirRoot, "DynRoomScripts"));
    M2Share.DynamicRoomNpcOwner = new NativeDynamicRoomNpcOwner(
        M2Share.DynamicRoomPasRoutes);
    M2Share.DynamicRoomRuntime = new NativeDynamicRoomRuntime(
        M2Share.DynamicRoomManager, M2Share.DynamicRoomPasRoutes, envirRoot);
    M2Share.DynamicRoomNpcMaterializer = new NativeDynamicRoomNpcMaterializer(
        M2Share.ObjectManager, M2Share.UserEngine);
    M2Share.DynamicRoomService = new NativeDynamicRoomService(
        M2Share.DynamicRoomManager, M2Share.DynamicRoomRuntime,
        M2Share.DynamicRoomNpcOwner, M2Share.DynamicRoomNpcMaterializer,
        M2Share.EventManager, M2Share.ObjectManager, M2Share.UserEngine);
    Assert(M2Share.DynamicRoomService.TryInitializeFromFiles(envirRoot,
            mapRoot, 0, out var errors),
        "production dynamic-room startup failed: " +
        string.Join(" | ", errors));
}

static void AddBagFixture(TPlayObject player)
{
    player.m_ItemList.Add(new TUserItem
    {
        MakeIndex = FixtureIds.Next(),
        wIndex = 1
    });
    player.m_ItemList.Add(new TUserItem
    {
        MakeIndex = FixtureIds.Next(),
        wIndex = 2
    });
    player.m_ItemList.Add(new TUserItem
    {
        MakeIndex = FixtureIds.Next(),
        wIndex = 1
    });
}

static void AssertBagCleared(TPlayObject player, string name)
{
    Equal(1, player.m_ItemList.Count, name + " bag count");
    Equal("保留物品",
        M2Share.UserEngine.GetStdItemName(player.m_ItemList[0].wIndex),
        name + " retained item");
}

static void AssertInitializedEntry(TPlayObject player, string name)
{
    Equal((byte)2, player.m_btNativeMagicTowerPhase, name + " phase");
    Equal((byte)2, player.m_btNativeMagicTowerRoomKind,
        name + " room kind");
    Equal((byte)0, player.m_btNativeMagicTowerDefeatedMonsterCount,
        name + " D2B");
    Equal((sbyte)0, player.m_sbNativeMagicTowerArcherCount,
        name + " archer count");
    Equal((byte)1, player.m_btNativeMagicTowerEngageChance,
        name + " engage chance");
    Assert(ReadSlots(player).All(value => value == 0), name + " slots");
}

static void AssertDynamicPosition(TPlayObject player, string name)
{
    Assert(player.m_PEnvir?.DynamicRoomName == "NewSky"
           && player.m_PEnvir.DynamicRoomIndex >= 0,
        name + " exact dynamic room");
    Assert(player.m_nCurrX == 40 && player.m_nCurrY == 40,
        name + " coordinates");
}

static void AssertPosition(TPlayObject player, Envirnoment environment,
    short x, short y, string name)
{
    Assert(ReferenceEquals(player.m_PEnvir, environment)
           && player.m_nCurrX == x && player.m_nCurrY == y,
        name + " changed position");
}

static void AssertMerchantSay(TPlayObject player, NormNpc npc,
    string message, string name)
{
    var messages = player.m_MsgList.Where(value =>
        value.wIdent == Grobal2.RM_MERCHANTSAY).ToArray();
    Equal(1, messages.Length, name + " merchant count");
    Equal((npc.m_sCharName ?? string.Empty) + "/" + message,
        messages[0].Buff, name + " merchant payload");
    Assert(ReferenceEquals(messages[0].BaseObject, npc),
        name + " merchant owner");
}

static byte[] ReadSlots(TPlayObject player)
{
    var field = typeof(TPlayObject).GetField(
        "m_btNativeMagicTowerArcherSlots",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(field != null, "archer slots field missing");
    return (byte[])field!.GetValue(player)!;
}

static PlayerSnapshot Snapshot(TPlayObject player) => new(
    player.m_btNativeMagicTowerPhase,
    player.m_btNativeMagicTowerRoomKind,
    player.m_btNativeMagicTowerNextGate,
    player.m_btNativeMagicTowerMysteryFlag,
    player.m_btNativeMagicTowerDefeatedMonsterCount,
    player.m_sbNativeMagicTowerArcherCount,
    player.m_btNativeMagicTowerEngageChance,
    player.m_btNativeMagicTowerSpecialRoute,
    player.m_boNativeMagicTowerHundredth,
    player.m_nLingFu,
    player.m_nUsedLingFu,
    player.m_ItemList.Count,
    string.Join(',', ReadSlots(player)));

static bool RouteEquals(NativeMagicTowerRouteSnapshot left,
    NativeMagicTowerRouteSnapshot right) =>
    left.TotalEntries == right.TotalEntries
    && left.Sequence == right.Sequence
    && left.Threshold == right.Threshold
    && left.PaidEntries == right.PaidEntries
    && left.FreeEntries == right.FreeEntries;

static int ActiveRoomCount(NativeDynamicRoomService service,
    string roomName)
{
    var rooms = (IDictionary)typeof(NativeDynamicRoomService)
        .GetField("_physicalRooms",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(service)!;
    var count = 0;
    foreach (DictionaryEntry entry in rooms)
    {
        if (entry.Key is Envirnoment environment
            && environment.DynamicRoomName == roomName
            && environment.DynamicRoomIndex >= 0)
            count++;
    }
    return count;
}

static Envirnoment NewEnvironment(string name)
{
    var environment = new Envirnoment
    {
        sMapName = name,
        m_sMapFileName = name,
        nServerIndex = 0
    };
    typeof(Envirnoment).GetMethod("Initialize",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(environment, new object[] { (short)64, (short)64 });
    return environment;
}

static TPlayObject NewPlacedPlayer(Envirnoment environment, string name,
    short x, short y)
{
    var player = new TPlayObject
    {
        m_boOffLineFlag = true,
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
        "could not place player on " + environment.sMapName);
    return player;
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

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{message}: expected={expected} actual={actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

readonly record struct PlayerSnapshot(byte Phase, byte RoomKind,
    byte NextGate, byte MysteryFlag, byte DefeatedCount, sbyte ArcherCount,
    byte EngageChance, byte SpecialRoute, bool Hundredth, int LingFu,
    int UsedLingFu, int BagCount, string Slots);

static class FixtureIds
{
    private static int _makeIndex = 10_000;

    internal static int Next() => Interlocked.Increment(ref _makeIndex);
}
