using System.Reflection;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.ObjectManager = new ObjectManager();
M2Share.MapManager = new MapManager();
M2Share.UserEngine = new UserEngine();
M2Share.ProcessMsgCriticalSection = new object();
M2Share.g_MonSayMsgList = new Dictionary<string, IList<TMonSayMsg>>();

const string sharedMapName = "ExactSpawnShared";
const string monsterName = "ExactSpawnOma";
var staticEnvironment = NewEnvironment(sharedMapName, "registered-static");
var dynamicEnvironment = NewEnvironment(sharedMapName, "unregistered-instance");
RegisterMap(M2Share.MapManager, staticEnvironment);
Assert(M2Share.MapManager.Maps.Count == 1
       && ReferenceEquals(staticEnvironment, M2Share.MapManager.Maps[0]),
    "test setup registered the physical instance");

M2Share.UserEngine.MonsterList.Add(NewMonsterInfo(monsterName));
var scriptBucket = new MonGenInfo
{
    CertList = new List<TBaseObject>()
};
M2Share.UserEngine.m_MonGenList.Add(scriptBucket);

var npc = NewNpc(dynamicEnvironment, 3, 3);
var spawnPlayer = new TPlayObject
{
    m_PEnvir = dynamicEnvironment,
    m_sMapName = sharedMapName,
    m_sMapFileName = dynamicEnvironment.m_sMapFileName,
    m_nCurrX = 7,
    m_nCurrY = 7
};
var bridge = new PasApiBridge
{
    CurrentNpc = npc,
    CurrentPlayer = spawnPlayer
};
var objectsBeforeSpawn = ObjectCount(M2Share.ObjectManager);

Assert(!bridge.CallNpcMethod("WantWarMon", Values(monsterName),
        out var wantWarMonResult),
    "WantWarMon accepted a monster name instead of its TPlayer ABI");
AssertNil(wantWarMonResult, "malformed WantWarMon");
Equal(0, scriptBucket.CertList.Count,
    "malformed WantWarMon spawned a monster");
Equal(0, dynamicEnvironment.MonCount,
    "malformed WantWarMon changed the physical-instance monster count");
Equal(0, staticEnvironment.MonCount,
    "malformed WantWarMon changed the registered-static monster count");
Equal(objectsBeforeSpawn, ObjectCount(M2Share.ObjectManager),
    "malformed WantWarMon changed the object index");

Assert(bridge.CallNpcMethod("CreateMon",
        Values("", 4, 4, 0, monsterName, 1), out var blankResult),
    "blank-map CreateMon was not dispatched");
AssertNil(blankResult, "blank-map CreateMon");

Equal(1, scriptBucket.CertList.Count,
    "blank-map CreateMon script bucket count");
Equal(1, scriptBucket.CertCount,
    "blank-map CreateMon script certificate count");
var dynamicMonster = scriptBucket.CertList[0];
Assert(ReferenceEquals(dynamicEnvironment, dynamicMonster.m_PEnvir),
    "blank-map CreateMon resolved through the registered static map");
Equal(sharedMapName, dynamicMonster.m_sMapName,
    "blank-map CreateMon monster map name");
Equal(1, dynamicEnvironment.MonCount,
    "blank-map CreateMon physical-instance monster count");
Equal(0, staticEnvironment.MonCount,
    "blank-map CreateMon changed registered static monster count");
Assert(CellContains(dynamicEnvironment, dynamicMonster),
    "blank-map CreateMon did not publish to the physical-instance cell");
Assert(!CellContains(staticEnvironment, dynamicMonster),
    "blank-map CreateMon published to the registered static cell");
Assert(ReferenceEquals(dynamicMonster,
        M2Share.ObjectManager.Get(dynamicMonster.ObjectId)),
    "blank-map CreateMon object index does not reference the spawned monster");
Equal(objectsBeforeSpawn + 1, ObjectCount(M2Share.ObjectManager),
    "blank-map CreateMon object index count");

Assert(bridge.CallNpcMethod("CreateMon",
        Values(sharedMapName, 6, 6, 0, monsterName, 1), out var namedResult),
    "named-map CreateMon was not dispatched");
AssertNil(namedResult, "named-map CreateMon");
Equal(2, scriptBucket.CertList.Count,
    "named-map CreateMon script bucket count");
Equal(2, scriptBucket.CertCount,
    "named-map CreateMon script certificate count");
var staticMonster = scriptBucket.CertList[1];
Assert(ReferenceEquals(staticEnvironment, staticMonster.m_PEnvir),
    "named-map CreateMon did not retain MapManager lookup semantics");
Equal(1, staticEnvironment.MonCount,
    "named-map CreateMon registered static monster count");
Equal(1, dynamicEnvironment.MonCount,
    "named-map CreateMon changed physical-instance monster count");
Assert(CellContains(staticEnvironment, staticMonster),
    "named-map CreateMon did not publish to the registered static cell");

Assert(bridge.CallNpcFunc("CreateMon",
        Values("", 5, 5, 0, monsterName, 1), out var npcFunctionResult),
    "blank-map NPC function CreateMon was not dispatched");
AssertNil(npcFunctionResult, "blank-map NPC function CreateMon");
var npcFunctionMonster = scriptBucket.CertList[2];
Assert(ReferenceEquals(dynamicEnvironment, npcFunctionMonster.m_PEnvir),
    "NPC function CreateMon resolved through the registered static map");

Assert(bridge.CallPlayerFunc("CreateMon",
        Values("", 7, 7, 0, monsterName, 1), out var playerResult),
    "blank-map player CreateMon was not dispatched");
AssertNil(playerResult, "blank-map player CreateMon");
var playerMonster = scriptBucket.CertList[3];
Assert(ReferenceEquals(dynamicEnvironment, playerMonster.m_PEnvir),
    "player CreateMon resolved through the registered static map");
Equal(4, scriptBucket.CertList.Count,
    "all CreateMon routes script bucket count");
Equal(4, scriptBucket.CertCount,
    "all CreateMon routes certificate count");
Equal(3, dynamicEnvironment.MonCount,
    "all blank-map CreateMon routes physical-instance monster count");
Equal(1, staticEnvironment.MonCount,
    "blank-map CreateMon routes changed registered static monster count");
Equal(objectsBeforeSpawn + 4, ObjectCount(M2Share.ObjectManager),
    "all CreateMon routes object index count");

foreach (var monster in new[]
         {
             dynamicMonster, npcFunctionMonster, playerMonster
         })
    monster.MakeGhost();
Assert(dynamicMonster.m_boGhost && npcFunctionMonster.m_boGhost
       && playerMonster.m_boGhost,
    "physical-instance monsters were not marked as ghosts");
Equal(0, dynamicEnvironment.MonCount,
    "physical-instance monster count did not return to zero");
Assert(!CellContains(dynamicEnvironment, dynamicMonster)
       && !CellContains(dynamicEnvironment, npcFunctionMonster)
       && !CellContains(dynamicEnvironment, playerMonster),
    "physical-instance monster remained in a map cell after MakeGhost");

var blockedEnvironment = NewEnvironment(sharedMapName, "blocked-instance", 8, 8);
BlockAllCells(blockedEnvironment);
bridge.CurrentNpc = NewNpc(blockedEnvironment, 2, 2);
var objectsBeforeFailure = ObjectCount(M2Share.ObjectManager);
var bucketCountBeforeFailure = scriptBucket.CertList.Count;
var certificateCountBeforeFailure = scriptBucket.CertCount;
var cellsBeforeFailure = CellObjectCount(blockedEnvironment);

Assert(bridge.CallNpcMethod("CreateMon",
        Values("", 2, 2, 0, monsterName, 1), out var blockedResult),
    "blocked blank-map CreateMon was not dispatched");
AssertNil(blockedResult, "blocked blank-map CreateMon");
Equal(0, blockedEnvironment.MonCount,
    "blocked CreateMon changed the environment monster count");
Equal(bucketCountBeforeFailure, scriptBucket.CertList.Count,
    "blocked CreateMon leaked into the script bucket");
Equal(certificateCountBeforeFailure, scriptBucket.CertCount,
    "blocked CreateMon changed the script certificate count");
Equal(cellsBeforeFailure, CellObjectCount(blockedEnvironment),
    "blocked CreateMon leaked a map-cell object");
Equal(objectsBeforeFailure, ObjectCount(M2Share.ObjectManager),
    "blocked CreateMon leaked an ObjectManager entry");

Console.WriteLine(
    "ExactEnvironmentMonsterSpawnCheck PASS routes=npc-method+npc-function+player-function blank=exact-environment named=map-manager want-war-mon=strict-player-abi failure=transactional");
return;

static Envirnoment NewEnvironment(string mapName, string mapFileName,
    short width = 12, short height = 12)
{
    var environment = new Envirnoment
    {
        sMapName = mapName,
        m_sMapFileName = mapFileName
    };
    typeof(Envirnoment).GetMethod("Initialize",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(environment, new object[] { width, height });
    return environment;
}

static NormNpc NewNpc(Envirnoment environment, short x, short y) => new()
{
    m_PEnvir = environment,
    m_sMapName = environment.sMapName,
    m_sMapFileName = environment.m_sMapFileName,
    m_nCurrX = x,
    m_nCurrY = y
};

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

static void RegisterMap(MapManager manager, Envirnoment environment)
{
    var field = typeof(MapManager).GetField("m_MapList",
        BindingFlags.Instance | BindingFlags.NonPublic)!;
    var maps = (IDictionary<string, Envirnoment>)field.GetValue(manager)!;
    maps.Add(environment.sMapName, environment);
}

static void BlockAllCells(Envirnoment environment)
{
    for (var x = 0; x < environment.wWidth; x++)
    for (var y = 0; y < environment.wHeight; y++)
        environment.SetMapXYFlag(x, y, false);
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

static int CellObjectCount(Envirnoment environment)
{
    var count = 0;
    for (var x = 0; x < environment.wWidth; x++)
    for (var y = 0; y < environment.wHeight; y++)
    {
        var found = false;
        count += environment.GetMapCellInfo(x, y, ref found).Count;
    }
    return count;
}

static int ObjectCount(ObjectManager manager)
{
    var actors = typeof(ObjectManager).GetField("_actors",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(manager)!;
    return (int)actors.GetType().GetProperty("Count")!.GetValue(actors)!;
}

static List<PasValue> Values(params object[] values) => values.Select(value =>
    value switch
    {
        int number => PasValue.FromInt(number),
        string text => PasValue.FromString(text),
        _ => throw new InvalidOperationException("unsupported test PAS value")
    }).ToList();

static void AssertNil(PasValue value, string message) =>
    Assert(value.Type == PasValueType.Nil, message + " did not return Nil");

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
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
