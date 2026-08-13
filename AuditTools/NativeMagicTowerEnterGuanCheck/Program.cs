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
Assert(Directory.Exists(envirRoot), $"production Envir missing: {envirRoot}");
Assert(Directory.Exists(mapRoot), $"production Map missing: {mapRoot}");

Assert(NativeDynamicRoomDefinitionLoader.TryLoad(
        Path.Combine(envirRoot, "PsDynNpc.txt"), out var definitions,
        out var definitionErrors),
    "production definitions failed: " + string.Join(" | ", definitionErrors));
var skyDefinition = definitions.Single(definition =>
    definition.RoomName == "Sky");
var skyPhysicalCount = int.Parse(skyDefinition.RawRoomCount,
    CultureInfo.InvariantCulture);
Equal(10, skyPhysicalCount, "production Sky physical-room fixture");

M2Share.g_Config = new GameSvrConfig();
M2Share.ProcessHumanCriticalSection = new object();
M2Share.ProcessMsgCriticalSection = new object();
M2Share.LogMsgCriticalSection = new object();
M2Share.LogStringList = new ArrayList();
M2Share.ObjectManager = new ObjectManager();
M2Share.MapManager = new MapManager();
M2Share.EventManager = new EventManager();
M2Share.g_MonSayMsgList = new Dictionary<string, IList<TMonSayMsg>>();
M2Share.UserEngine = new UserEngine();
M2Share.RandomNumber = RandomNumber.GetInstance();
M2Share.nServerIndex = 0;
M2Share.CreditCardService = NativeCreditCardService.Disabled;
M2Share.MagicTowerRouteSequencer = new NativeMagicTowerRouteSequencer(
    _ => 0, 12, 34, 56, 7, 8);
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

Assert(M2Share.DynamicRoomService.TryInitializeFromFiles(envirRoot, mapRoot,
        0, out var startupErrors),
    "production service startup failed: " + string.Join(" | ", startupErrors));

var guardian = new NormNpc
{
    m_sCharName = "天关守卫",
    m_sMapName = "D5071~0"
};
var bridge = new PasApiBridge
{
    CurrentNpc = guardian,
    CurrentPlayer = new TPlayObject { m_sCharName = "context-player" }
};

CheckSilentPhase(bridge, guardian, 0, 6, "phase-zero");
CheckSilentPhase(bridge, guardian, 2, 7, "phase-two");
CheckSuccess(bridge, guardian);
CheckIneligibleFailure(bridge, guardian, ghost: true,
    invalidRace: false, expectMerchantSay: false, "ghost-player");
CheckIneligibleFailure(bridge, guardian, ghost: false,
    invalidRace: true, expectMerchantSay: true, "non-player-race");
FillRemainingSkyRooms(M2Share.DynamicRoomService, skyPhysicalCount);
CheckRoomFullFailure(bridge, guardian, skyPhysicalCount);
CheckAbiShadows(bridge, guardian);

var freshPlayer = new TPlayObject();
Equal((byte)0, ReadPlayerField<byte>(freshPlayer,
    "m_btNativeMagicTowerPhase"), "fresh B88 phase");
Equal((byte)0, ReadPlayerField<byte>(freshPlayer,
    "m_btNativeMagicTowerRoomKind"), "fresh B8D room kind");

Console.WriteLine("NativeMagicTowerEnterGuanCheck PASS " +
    "abi=player-procedure phase=1-only room=Sky@28,20 " +
    "state=B88:2/B8D:1 full=single-merchant-say " +
    "ineligible=ghost-filtered+race-message " +
    "silent=phase0+phase2 transient=zero-default");

static void CheckSilentPhase(PasApiBridge bridge, NormNpc npc, byte phase,
    byte roomKind, string name)
{
    var source = NewEnvironment(name + "-source", 64, 64);
    var player = NewPlacedPlayer(source, name, 3, 4);
    SetPlayerField(player, "m_btNativeMagicTowerPhase", phase);
    SetPlayerField(player, "m_btNativeMagicTowerRoomKind", roomKind);
    SetPlayerField(player, "m_boNativeMagicTowerHundredth", true);
    SetPlayerField(player, "m_btNativeMagicTowerSpecialRoute", (byte)5);
    var activeBefore = ActiveRoomCount(M2Share.DynamicRoomService, "Sky");
    var context = new TPlayObject { m_sCharName = name + "-context" };
    bridge.CurrentNpc = npc;
    bridge.CurrentPlayer = context;

    Assert(bridge.CallNpcMethod("EnterGuan", new List<PasValue>
    {
        PasValue.FromObject(player)
    }, out var result), name + " valid ABI was rejected");
    Equal(PasValueType.Nil, result.Type, name + " procedure result");
    Equal(activeBefore, ActiveRoomCount(M2Share.DynamicRoomService, "Sky"),
        name + " activated a room");
    Assert(ReferenceEquals(player.m_PEnvir, source)
           && player.m_nCurrX == 3 && player.m_nCurrY == 4,
        name + " moved the player");
    Equal(phase, ReadPlayerField<byte>(player,
        "m_btNativeMagicTowerPhase"), name + " phase");
    Equal(roomKind, ReadPlayerField<byte>(player,
        "m_btNativeMagicTowerRoomKind"), name + " room kind");
    Assert(ReadPlayerField<bool>(player,
            "m_boNativeMagicTowerHundredth")
           && ReadPlayerField<byte>(player,
               "m_btNativeMagicTowerSpecialRoute") == 5,
        name + " changed adjacent tower state");
    Equal(0, player.m_MsgList.Count, name + " emitted a message");
    Assert(player.m_NPC == null, name + " rebound the current NPC");
    Equal(0, context.m_MsgList.Count, name + " changed context player");
}

static void CheckSuccess(PasApiBridge bridge, NormNpc npc)
{
    var source = NewEnvironment("guan-success-source", 64, 64);
    var player = NewPlacedPlayer(source, "guan-success", 5, 6);
    player.m_nGold = 1234;
    player.m_nLingFu = 9;
    player.m_nUsedLingFu = 17;
    SetPlayerField(player, "m_btNativeMagicTowerPhase", (byte)1);
    SetPlayerField(player, "m_btNativeMagicTowerRoomKind", (byte)7);
    SetPlayerField(player, "m_boNativeMagicTowerHundredth", true);
    SetPlayerField(player, "m_btNativeMagicTowerSpecialRoute", (byte)4);
    var activeBefore = ActiveRoomCount(M2Share.DynamicRoomService, "Sky");
    var routeBefore = M2Share.MagicTowerRouteSequencer.Snapshot();
    var logBefore = M2Share.LogStringList.Count;
    var context = new TPlayObject { m_sCharName = "guan-success-context" };
    bridge.CurrentNpc = npc;
    bridge.CurrentPlayer = context;

    Assert(bridge.CallNpcMethod("EnterGuan", new List<PasValue>
    {
        PasValue.FromObject(player)
    }, out var result), "successful EnterGuan ABI was rejected");
    Equal(PasValueType.Nil, result.Type, "successful procedure result");
    Equal(activeBefore + 1,
        ActiveRoomCount(M2Share.DynamicRoomService, "Sky"),
        "successful EnterGuan activation count");
    Assert(player.m_PEnvir?.DynamicRoomName == "Sky"
           && player.m_PEnvir.DynamicRoomIndex > 0,
        "successful EnterGuan did not use an exact active Sky environment");
    Assert(player.m_nCurrX == 28 && player.m_nCurrY == 20,
        "successful EnterGuan coordinates mismatch");
    Equal((byte)2, ReadPlayerField<byte>(player,
        "m_btNativeMagicTowerPhase"), "successful B88 phase");
    Equal((byte)1, ReadPlayerField<byte>(player,
        "m_btNativeMagicTowerRoomKind"), "successful B8D room kind");
    Assert(ReadPlayerField<bool>(player,
            "m_boNativeMagicTowerHundredth")
           && ReadPlayerField<byte>(player,
               "m_btNativeMagicTowerSpecialRoute") == 4,
        "successful EnterGuan changed adjacent tower state");
    Assert(player.m_NPC == null,
        "successful EnterGuan rebound the current NPC");
    Equal(1234, player.m_nGold, "successful EnterGuan gold");
    Equal(9, player.m_nLingFu, "successful EnterGuan LingFu");
    Equal(17, player.m_nUsedLingFu, "successful EnterGuan used LingFu");
    Equal(0, player.m_ItemList.Count, "successful EnterGuan bag");
    Equal(0, player.m_MsgList.Count(message =>
        message.wIdent == Grobal2.RM_MERCHANTSAY),
        "successful EnterGuan merchant messages");
    // MOVE-52: FlyToDynamicRoom takes the default ident pair, and both native
    // space-move arms load it as immediates - 0x6BD3AA `mov cx,0x2785` (10117) and
    // 0x6BD3D3 `mov cx,0x2786` (10118).
    Equal(1, player.m_MsgList.Count(message =>
        message.wIdent == Grobal2.RM_NATIVE_CLEAROBJECTS),
        "successful EnterGuan clear-object messages");
    Equal(1, player.m_MsgList.Count(message =>
        message.wIdent == Grobal2.RM_NATIVE_CHANGEMAP),
        "successful EnterGuan change-map messages");
    Equal(0, player.m_MsgList.Count(message =>
        message.wIdent == Grobal2.RM_CLEAROBJECTS
        || message.wIdent == Grobal2.RM_CHANGEMAP),
        "successful EnterGuan fell back to the legacy 8097/8098 idents");
    Assert(RouteSnapshotEquals(routeBefore,
            M2Share.MagicTowerRouteSequencer.Snapshot()),
        "successful EnterGuan changed route counters");
    Equal(logBefore, M2Share.LogStringList.Count,
        "successful EnterGuan wrote a log");
    Equal(0, context.m_MsgList.Count,
        "successful EnterGuan changed context player");
}

static void CheckIneligibleFailure(PasApiBridge bridge, NormNpc npc,
    bool ghost, bool invalidRace, bool expectMerchantSay, string name)
{
    var source = NewEnvironment(name + "-source", 64, 64);
    var player = NewPlacedPlayer(source, name, 7, 8);
    player.m_boGhost = ghost;
    if (invalidRace) player.m_btRaceServer = Grobal2.RC_ANIMAL;
    SetPlayerField(player, "m_btNativeMagicTowerPhase", (byte)1);
    SetPlayerField(player, "m_btNativeMagicTowerRoomKind", (byte)9);
    var activeBefore = ActiveRoomCount(M2Share.DynamicRoomService, "Sky");
    bridge.CurrentNpc = npc;
    bridge.CurrentPlayer = new TPlayObject
    {
        m_sCharName = name + "-context"
    };

    Assert(bridge.CallNpcMethod("EnterGuan", new List<PasValue>
    {
        PasValue.FromObject(player)
    }, out _), name + " failure was not handled");
    Equal(activeBefore, ActiveRoomCount(M2Share.DynamicRoomService, "Sky"),
        name + " reserved a room");
    Assert(ReferenceEquals(player.m_PEnvir, source)
           && player.m_nCurrX == 7 && player.m_nCurrY == 8,
        name + " changed position");
    Equal((byte)1, ReadPlayerField<byte>(player,
        "m_btNativeMagicTowerPhase"), name + " phase");
    Equal((byte)9, ReadPlayerField<byte>(player,
        "m_btNativeMagicTowerRoomKind"), name + " room kind");
    Assert(ReferenceEquals(player.m_NPC, npc), name + " NPC binding");
    if (expectMerchantSay)
    {
        AssertMerchantSay(player, npc, name);
    }
    else
    {
        Equal(0, player.m_MsgList.Count, name + " ghost message filter");
    }
}

static void FillRemainingSkyRooms(NativeDynamicRoomService service,
    int expectedCount)
{
    var attempt = 0;
    while (service.HasFreeDynamicRoom("Sky"))
    {
        attempt++;
        Assert(attempt <= expectedCount,
            "Sky fill loop exceeded the physical-room count");
        var owner = new TPlayObject
        {
            m_sCharName = "sky-fill-" + attempt
        };
        Assert(service.TryReserveActivatedRoom("Sky", owner, out _),
            "could not reserve a free Sky room");
    }
    Equal(expectedCount, ActiveRoomCount(service, "Sky"),
        "full Sky activation count");
}

static void CheckRoomFullFailure(PasApiBridge bridge, NormNpc npc,
    int expectedCount)
{
    var source = NewEnvironment("guan-full-source", 64, 64);
    var player = NewPlacedPlayer(source, "guan-full", 9, 10);
    player.m_nGold = 4321;
    player.m_nLingFu = 11;
    SetPlayerField(player, "m_btNativeMagicTowerPhase", (byte)1);
    SetPlayerField(player, "m_btNativeMagicTowerRoomKind", (byte)8);
    var routeBefore = M2Share.MagicTowerRouteSequencer.Snapshot();
    var logBefore = M2Share.LogStringList.Count;
    bridge.CurrentNpc = npc;
    bridge.CurrentPlayer = new TPlayObject
    {
        m_sCharName = "guan-full-context"
    };

    Assert(bridge.CallNpcMethod("EnterGuan", new List<PasValue>
    {
        PasValue.FromObject(player)
    }, out _), "room-full EnterGuan was not handled");
    Equal(expectedCount, ActiveRoomCount(M2Share.DynamicRoomService, "Sky"),
        "room-full EnterGuan changed activation count");
    Assert(ReferenceEquals(player.m_PEnvir, source)
           && player.m_nCurrX == 9 && player.m_nCurrY == 10,
        "room-full EnterGuan changed position");
    Equal((byte)1, ReadPlayerField<byte>(player,
        "m_btNativeMagicTowerPhase"), "room-full B88 phase");
    Equal((byte)8, ReadPlayerField<byte>(player,
        "m_btNativeMagicTowerRoomKind"), "room-full B8D room kind");
    Equal(4321, player.m_nGold, "room-full EnterGuan gold");
    Equal(11, player.m_nLingFu, "room-full EnterGuan LingFu");
    Equal(0, player.m_ItemList.Count, "room-full EnterGuan bag");
    Assert(ReferenceEquals(player.m_NPC, npc),
        "room-full EnterGuan NPC binding");
    AssertMerchantSay(player, npc, "room-full");
    Assert(RouteSnapshotEquals(routeBefore,
            M2Share.MagicTowerRouteSequencer.Snapshot()),
        "room-full EnterGuan changed route counters");
    Equal(logBefore, M2Share.LogStringList.Count,
        "room-full EnterGuan wrote a log");
}

static void CheckAbiShadows(PasApiBridge bridge, NormNpc npc)
{
    var player = new TPlayObject { m_sCharName = "abi-player" };
    SetPlayerField(player, "m_btNativeMagicTowerPhase", (byte)0);
    bridge.CurrentNpc = npc;
    bridge.CurrentPlayer = new TPlayObject
    {
        m_sCharName = "abi-context"
    };
    Assert(!bridge.CallNpcMethod("EnterGuan", new List<PasValue>(), out _),
        "EnterGuan accepted a missing argument");
    Assert(!bridge.CallNpcMethod("EnterGuan", new List<PasValue>
        { PasValue.FromInt(1) }, out _),
        "EnterGuan accepted a non-player argument");
    Assert(!bridge.CallNpcMethod("EnterGuan", new List<PasValue>
    {
        PasValue.FromObject(player), PasValue.FromInt(1)
    }, out _), "EnterGuan accepted an extra argument");
    Assert(!bridge.CallNpcFunc("EnterGuan", new List<PasValue>
        { PasValue.FromObject(player) }, out _),
        "EnterGuan procedure leaked through the function dispatcher");
    Assert(bridge.CallNpcMethod("EnterGuan", new List<PasValue>
        { PasValue.FromObject(player) }, out var result)
           && result.Type == PasValueType.Nil,
        "valid phase-zero EnterGuan ABI was not handled");
}

static void AssertMerchantSay(TPlayObject player, NormNpc npc, string name)
{
    var messages = player.m_MsgList.Where(message =>
        message.wIdent == Grobal2.RM_MERCHANTSAY).ToArray();
    Equal(1, messages.Length, name + " merchant message count");
    var message = messages[0];
    Assert(message.wParam == 0 && message.nParam1 == 0
           && message.nParam2 == 0 && message.nParam3 == 0,
        name + " merchant message parameters");
    Assert(ReferenceEquals(message.BaseObject, npc),
        name + " merchant message owner");
    Equal("天关守卫/天关房间满员,请稍候再试...", message.Buff,
        name + " merchant message payload");
}

static int ActiveRoomCount(NativeDynamicRoomService service, string roomName)
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

static Envirnoment NewEnvironment(string name, short width, short height)
{
    var environment = new Envirnoment
    {
        sMapName = name,
        m_sMapFileName = name,
        nServerIndex = 0
    };
    typeof(Envirnoment).GetMethod("Initialize",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(environment, new object[] { width, height });
    return environment;
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
        "could not place player on " + environment.sMapName);
    return player;
}

static T ReadPlayerField<T>(TPlayObject player, string fieldName)
{
    var field = typeof(TPlayObject).GetField(fieldName,
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(field != null, "native tower field is missing: " + fieldName);
    return (T)field!.GetValue(player)!;
}

static void SetPlayerField<T>(TPlayObject player, string fieldName, T value)
{
    var field = typeof(TPlayObject).GetField(fieldName,
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(field != null, "native tower field is missing: " + fieldName);
    field!.SetValue(player, value);
}

static bool RouteSnapshotEquals(NativeMagicTowerRouteSnapshot left,
    NativeMagicTowerRouteSnapshot right)
{
    return left.TotalEntries == right.TotalEntries
           && left.Sequence == right.Sequence
           && left.Threshold == right.Threshold
           && left.PaidEntries == right.PaidEntries
           && left.FreeEntries == right.FreeEntries;
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
