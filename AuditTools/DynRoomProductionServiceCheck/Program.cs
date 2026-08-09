using System.Globalization;
using System.Reflection;
using GameSvr;
using GameSvr.PasEngine;
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
var expectedPhysical = definitions.Sum(definition =>
    int.Parse(definition.RawRoomCount, CultureInfo.InvariantCulture));
var expectedNpcActors = definitions.Sum(definition =>
    int.Parse(definition.RawRoomCount, CultureInfo.InvariantCulture)
    * (definition.ConfiguredNpcs.Count + 1));

M2Share.g_Config = new GameSvrConfig();
M2Share.ProcessHumanCriticalSection = new object();
M2Share.ProcessMsgCriticalSection = new object();
M2Share.ObjectManager = new ObjectManager();
M2Share.MapManager = new MapManager();
M2Share.EventManager = new EventManager();
M2Share.g_MonSayMsgList = new Dictionary<string, IList<TMonSayMsg>>();
M2Share.UserEngine = new UserEngine();
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
Equal(22, definitions.Count, "production definition fixture");
Equal(72, expectedPhysical, "production physical fixture");
Equal(definitions.Count, M2Share.DynamicRoomService.DefinitionCount,
    "service definition count");
Equal(expectedPhysical, M2Share.DynamicRoomService.PhysicalRoomCount,
    "service physical count");
Equal(expectedNpcActors, ObjectManagerActorCount(M2Share.ObjectManager),
    "committed physical NPC actor count");

var bridge = new PasApiBridge { CurrentNpc = new NormNpc() };
var roomName = "Sky";
Assert(bridge.CallNpcFunc("GetAIdleDynRoomIndex",
        new List<PasValue> { PasValue.FromString(roomName) }, out var value),
    "NPC idle-room function was not dispatched");
var firstIndex = value.AsInt();
Equal(1, firstIndex, "first native activation index");
Envirnoment firstEnvironment = null;
Assert(firstIndex > 0 && M2Share.DynamicRoomManager.TryGetActiveRoom(
        roomName, firstIndex, out firstEnvironment),
    "NPC idle-room function did not activate an exact room");

var source = NewEnvironment("StaticSource", 16, 16);
var player = new TPlayObject
{
    m_PEnvir = source,
    m_nCurrX = 2,
    m_nCurrY = 2,
    m_sCharName = "DynRoomAudit"
};
Assert(ReferenceEquals(player, source.AddToMap(2, 2,
        CellType.OS_MOVINGOBJECT, player)),
    "player source publication failed");
bridge.CurrentPlayer = player;

Assert(!bridge.CallPlayerMethod("FlyToDynEnvirWithIdx", new List<PasValue>
       {
           PasValue.FromString(roomName), PasValue.FromInt(firstIndex),
           PasValue.FromInt(10), PasValue.FromInt(10)
       }), "player method shadow bypassed the function ABI");
Assert(bridge.CallPlayerFunc("FlyToDynEnvirWithIdx", new List<PasValue>
       {
           PasValue.FromString(roomName), PasValue.FromInt(firstIndex),
           PasValue.FromInt(10), PasValue.FromInt(10)
       }, out value) && value.AsBool(),
    "indexed dynamic-room movement failed");
Assert(ReferenceEquals(player.m_PEnvir, firstEnvironment),
    "indexed movement used a map-name alias instead of the exact environment");

var interpreter = CreateInterpreter(bridge);
Equal(10, interpreter.ExecuteProcedure("ProbeRoomCount").AsInt(),
    "interpreter global room-count dispatch");
Equal(1, interpreter.ExecuteProcedure("ProbeHumanCount").AsInt(),
    "interpreter global human-count dispatch");
Assert(interpreter.ExecuteProcedure("ProbeFree").AsBool()
       && interpreter.ExecuteProcedure("ProbeValid").AsBool(),
    "interpreter global dynamic-room Boolean dispatch");
Assert(interpreter.ExecuteProcedure("ProbeIndexedMove").AsBool()
       && ReferenceEquals(player.m_PEnvir, firstEnvironment),
    "interpreter TPlayer indexed-movement dispatch");
interpreter.ExecuteProcedure("ProbeZeroSpawn");
interpreter.ExecuteProcedure("ProbeGroupMove");

var routedNpc = PhysicalNpcs(M2Share.DynamicRoomService, firstEnvironment)
    .First(entry => entry.Binding.HasScript).Npc;
Equal(NativeDynamicRoomPasScriptRouteState.ExactCurrent,
    M2Share.DynamicRoomPasRoutes.ResolveCurrent(routedNpc,
        out var routedHandle, out _),
    "production activation PAS route");
var nestedRoomIndex = -1;
bridge.CurrentNpc = routedNpc;
Assert(M2Share.DynamicRoomRuntime.TryExecuteExpectedPas(routedNpc,
        routedHandle, _ =>
        {
            if (!bridge.CallNpcFunc("GetAIdleDynRoomIndex",
                    new List<PasValue> { PasValue.FromString("NewSky") },
                    out var nestedValue))
                return false;
            nestedRoomIndex = nestedValue.AsInt();
            return nestedRoomIndex > 0;
        }), "dynamic PAS could not activate a different production room");
Envirnoment nestedEnvironment = null;
Assert(M2Share.DynamicRoomManager.TryGetActiveRoom("NewSky",
        nestedRoomIndex, out nestedEnvironment),
    "nested dynamic PAS activation did not commit its exact room");

var groupMember = new TPlayObject
{
    m_PEnvir = firstEnvironment,
    m_sCharName = "DynRoomGroupMember"
};
var outsideMember = new TPlayObject
{
    m_PEnvir = source,
    m_sCharName = "DynRoomOutsideMember"
};
Assert(TryPublishPlayer(firstEnvironment, groupMember)
       && TryPublishPlayer(source, outsideMember),
    "group movement fixtures were not published");
player.m_GroupMembers.Clear();
player.m_GroupMembers.Add(player);
player.m_GroupMembers.Add(groupMember);
player.m_GroupMembers.Add(outsideMember);
groupMember.m_GroupOwner = player;
outsideMember.m_GroupOwner = player;
Assert(bridge.CallPlayerMethod("GroupFlyToDynRoom", new List<PasValue>
       {
           PasValue.FromString("NewSky"), PasValue.FromInt(nestedRoomIndex)
       }), "production group movement was not dispatched");
Assert(ReferenceEquals(player.m_PEnvir, nestedEnvironment)
       && ReferenceEquals(groupMember.m_PEnvir, nestedEnvironment)
       && ReferenceEquals(outsideMember.m_PEnvir, source),
    "group movement did not preserve exact source-environment filtering");

const string auditMonsterName = "DynRoomAuditMonster";
M2Share.UserEngine.MonsterList.Add(NewMonsterInfo(auditMonsterName));
var spawnBucket = new MonGenInfo { CertList = new List<TBaseObject>() };
M2Share.UserEngine.m_MonGenList.Add(spawnBucket);
Assert(bridge.CallNpcMethod("CreateDynRoomMon", new List<PasValue>
       {
           PasValue.FromString(roomName), PasValue.FromInt(firstIndex),
           PasValue.FromInt(10), PasValue.FromInt(10), PasValue.FromInt(0),
           PasValue.FromString(auditMonsterName), PasValue.FromInt(2)
       }, out _), "dynamic monster procedure was not dispatched");
Equal(2, spawnBucket.CertCount, "dynamic monster attempt count");
Equal(2, spawnBucket.CertList.Count, "dynamic monster publication count");
Assert(spawnBucket.CertList.All(monster =>
        ReferenceEquals(monster.m_PEnvir, firstEnvironment)),
    "dynamic monsters were not published to the exact active environment");

Assert(bridge.CallStandaloneFunction("GetDynRoomHumNum", new List<PasValue>
       {
           PasValue.FromString("NewSky"), PasValue.FromInt(nestedRoomIndex)
       }, out value) && value.AsInt() == 2,
    "dynamic-room player count did not observe exact environment presence");
Assert(bridge.CallStandaloneFunction("GetDynRoomCnt",
        new List<PasValue> { PasValue.FromString(roomName) }, out value)
       && value.AsInt() == 10,
    "dynamic-room physical count API mismatch");
Assert(bridge.CallStandaloneFunction("PsHaveFreeDynRoom",
        new List<PasValue> { PasValue.FromString(roomName) }, out value)
       && value.AsBool(), "free-room API mismatch");
Assert(bridge.CallStandaloneFunction("PsIsDynRoomValid", new List<PasValue>
       {
           PasValue.FromString(roomName), PasValue.FromInt(firstIndex)
       }, out value) && value.AsBool(), "active-room validity API mismatch");
Assert(!bridge.CallPlayerFunc("GetDynRoomCnt",
        new List<PasValue> { PasValue.FromString(roomName) }, out _),
    "global dynamic-room query leaked through a TPlayer method shadow");

Assert(bridge.CallPlayerFunc("FlyToDynRoom", new List<PasValue>
       {
           PasValue.FromString(roomName), PasValue.FromInt(11),
           PasValue.FromInt(11)
       }, out value), "dynamic-room allocation movement was not dispatched");
var secondIndex = value.AsInt();
Envirnoment secondEnvironment = null;
Assert(secondIndex > firstIndex
       && M2Share.DynamicRoomManager.TryGetActiveRoom(roomName, secondIndex,
           out secondEnvironment)
       && ReferenceEquals(player.m_PEnvir, secondEnvironment),
    "allocation movement did not activate and enter a fresh exact room");

var createArgs = new List<PasValue>
{
    PasValue.FromString(roomName), PasValue.FromInt(secondIndex),
    PasValue.FromInt(11), PasValue.FromInt(11), PasValue.FromInt(3),
    PasValue.FromString("MissingAuditMonster"), PasValue.FromInt(0)
};
Assert(bridge.CallNpcMethod("CreateDynRoomMon", createArgs, out _),
    "zero-count dynamic monster procedure was not a handled no-op");
Assert(!bridge.CallNpcFunc("CreateDynRoomMon", createArgs, out _),
    "dynamic monster function shadow bypassed the procedure ABI");
Assert(bridge.CallPlayerMethod("GroupFlyToDynRoom", new List<PasValue>
       {
           PasValue.FromString(roomName), PasValue.FromInt(secondIndex)
       }), "group dynamic-room procedure was not dispatched");
Assert(!bridge.CallPlayerFunc("GroupFlyToDynRoom", new List<PasValue>
       {
           PasValue.FromString(roomName), PasValue.FromInt(secondIndex)
       }, out _), "group dynamic-room function shadow bypassed the procedure ABI");

Assert(bridge.CallNpcFunc("GetAIdleDynRoomIndexEx", new List<PasValue>
       {
           PasValue.FromString(roomName), PasValue.FromObject(player)
       }, out value) && value.AsInt() > secondIndex,
    "owned idle-room function did not activate a fresh room");

Console.WriteLine("DynRoomProductionServiceCheck PASS "
    + $"definitions={definitions.Count} physical={expectedPhysical} "
    + $"npcs={expectedNpcActors} spawn=loop-exact "
    + "api=interpreter+exact-environment+abi-shadowed "
    + "nested-PAS=cross-room-activation");

static int ObjectManagerActorCount(ObjectManager manager)
{
    var actors = typeof(ObjectManager).GetField("_actors",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(manager)!;
    return (int)actors.GetType().GetProperty("Count")!.GetValue(actors)!;
}

static NativeDynamicRoomMaterializedNpc[] PhysicalNpcs(
    NativeDynamicRoomService service, Envirnoment environment)
{
    var rooms = (System.Collections.IDictionary)typeof(
            NativeDynamicRoomService)
        .GetField("_physicalRooms",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(service)!;
    var physical = rooms[environment]
        ?? throw new InvalidOperationException(
            "production physical-room entry is missing");
    return (NativeDynamicRoomMaterializedNpc[])physical.GetType()
        .GetProperty("Npcs")!.GetValue(physical)!;
}

static PasInterpreter CreateInterpreter(PasApiBridge bridge)
{
    const string source = """
        program DynRoomProductionProbe;
        function ProbeRoomCount: Integer;
        begin
          Result := GetDynRoomCnt('Sky');
        end;
        function ProbeHumanCount: Integer;
        begin
          Result := GetDynRoomHumNum('Sky', 1);
        end;
        function ProbeFree: Boolean;
        begin
          Result := PsHaveFreeDynRoom('Sky');
        end;
        function ProbeValid: Boolean;
        begin
          Result := PsIsDynRoomValid('Sky', 1);
        end;
        function ProbeIndexedMove: Boolean;
        begin
          Result := This_Player.FlyToDynEnvirWithIdx('Sky', 1, 10, 10);
        end;
        procedure ProbeZeroSpawn;
        begin
          This_Npc.CreateDynRoomMon('Sky', 1, 10, 10, 3,
            'MissingAuditMonster', 0);
        end;
        procedure ProbeGroupMove;
        begin
          This_Player.GroupFlyToDynRoom('Sky', 1);
        end;
        begin
        end.
        """;
    return new PasInterpreter(new PasParser(new PasLexer(source)).Parse(),
        bridge);
}

static TMonInfo NewMonsterInfo(string name) => new()
{
    ItemList = new List<TMonItem>(),
    sName = name,
    btRace = (byte)M2Share.MONSTER_OMA,
    wLevel = 1,
    wHP = 100,
    wWalkSpeed = 1000,
    wWalkStep = 1,
    wWalkWait = 1000,
    wAttackSpeed = 1000
};

static Envirnoment NewEnvironment(string name, short width, short height)
{
    var environment = new Envirnoment { sMapName = name };
    typeof(Envirnoment).GetMethod("Initialize",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(environment, new object[] { width, height });
    return environment;
}

static bool TryPublishPlayer(Envirnoment environment, TPlayObject player)
{
    for (short x = 0; x < environment.wWidth; x++)
    for (short y = 0; y < environment.wHeight; y++)
    {
        if (!environment.CanWalk(x, y, false)) continue;
        player.m_PEnvir = environment;
        player.m_nCurrX = x;
        player.m_nCurrY = y;
        if (ReferenceEquals(player, environment.AddToMap(x, y,
                CellType.OS_MOVINGOBJECT, player)))
            return true;
    }
    return false;
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
