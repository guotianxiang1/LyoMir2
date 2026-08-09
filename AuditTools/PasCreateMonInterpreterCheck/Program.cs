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

const string sharedMapName = "PasInterpreterShared";
const string monsterName = "PasInterpreterOma";
var staticEnvironment = NewEnvironment(sharedMapName, "registered-static");
var exactEnvironment = NewEnvironment(sharedMapName, "unregistered-exact");
RegisterMap(M2Share.MapManager, staticEnvironment);

M2Share.UserEngine.MonsterList.Add(NewMonsterInfo(monsterName));
var scriptBucket = new MonGenInfo { CertList = new List<TBaseObject>() };
M2Share.UserEngine.m_MonGenList.Add(scriptBucket);

var player = new TPlayObject
{
    m_PEnvir = exactEnvironment,
    m_sMapName = sharedMapName,
    m_sMapFileName = exactEnvironment.m_sMapFileName,
    m_nCurrX = 3,
    m_nCurrY = 3
};
var npc = new NormNpc
{
    m_PEnvir = exactEnvironment,
    m_sMapName = sharedMapName,
    m_sMapFileName = exactEnvironment.m_sMapFileName,
    m_nCurrX = 4,
    m_nCurrY = 4
};
var bridge = new PasApiBridge { CurrentPlayer = player, CurrentNpc = npc };
var interpreter = CreateInterpreter(bridge);

VerifyFuncFirstDispatcher();

var playerBlank = ExecuteSpawn(interpreter, "PlayerBlank", scriptBucket);
AssertSpawn(playerBlank, exactEnvironment, staticEnvironment,
    "This_Player blank map");

var playerNamed = ExecuteSpawn(interpreter, "PlayerNamed", scriptBucket);
AssertSpawn(playerNamed, staticEnvironment, exactEnvironment,
    "This_Player explicit map");

var npcBlank = ExecuteSpawn(interpreter, "NpcBlank", scriptBucket);
AssertSpawn(npcBlank, exactEnvironment, staticEnvironment,
    "This_Npc blank map");

var npcNamed = ExecuteSpawn(interpreter, "NpcNamed", scriptBucket);
AssertSpawn(npcNamed, staticEnvironment, exactEnvironment,
    "This_Npc explicit map");

Equal(2, exactEnvironment.MonCount,
    "blank-map exact physical-environment monster count");
Equal(2, staticEnvironment.MonCount,
    "explicit-map registered environment monster count");
Equal(4, scriptBucket.CertList.Count,
    "successful interpreter CreateMon certificate-list count");
Equal(4, scriptBucket.CertCount,
    "successful interpreter CreateMon certificate count");

var beforePlayerNull = CaptureState(scriptBucket,
    exactEnvironment, staticEnvironment);
bridge.CurrentPlayer = null;
ExpectRuntimeFailure(interpreter, "PlayerBlank", "This_Player null host");
AssertUnchanged(beforePlayerNull, scriptBucket, exactEnvironment,
    staticEnvironment, "This_Player null host");
bridge.CurrentPlayer = player;

var beforeNpcNull = CaptureState(scriptBucket,
    exactEnvironment, staticEnvironment);
bridge.CurrentNpc = null;
ExpectRuntimeFailure(interpreter, "NpcBlank", "This_Npc null host");
AssertUnchanged(beforeNpcNull, scriptBucket, exactEnvironment,
    staticEnvironment, "This_Npc null host");
bridge.CurrentNpc = npc;

var beforePlayerUnknown = CaptureState(scriptBucket,
    exactEnvironment, staticEnvironment);
AssertNil(interpreter.ExecuteProcedure("PlayerUnknown"),
    "This_Player unknown map");
AssertUnchanged(beforePlayerUnknown, scriptBucket, exactEnvironment,
    staticEnvironment, "This_Player unknown map");

var beforeNpcUnknown = CaptureState(scriptBucket,
    exactEnvironment, staticEnvironment);
AssertNil(interpreter.ExecuteProcedure("NpcUnknown"),
    "This_Npc unknown map");
AssertUnchanged(beforeNpcUnknown, scriptBucket, exactEnvironment,
    staticEnvironment, "This_Npc unknown map");

Console.WriteLine(
    "PASS PasCreateMonInterpreterCheck entry=PasParser+PasInterpreter.ExecuteProcedure+This_Player/This_Npc.CreateMon native-abi=MapName,X,Y,Ranger,MonName,MonNum dispatcher=func-first blank=exact-environment explicit=MapManager null-host/unknown-map=fail-closed-no-leak");
return;

static PasInterpreter CreateInterpreter(PasApiBridge bridge)
{
    const string source = """
        program PasCreateMonInterpreterProbe;
        procedure PlayerBlank;
        begin
          This_Player.CreateMon('', 5, 5, 0, 'PasInterpreterOma', 1);
        end;
        procedure PlayerNamed;
        begin
          This_Player.CreateMon('PasInterpreterShared', 6, 6, 0, 'PasInterpreterOma', 1);
        end;
        procedure NpcBlank;
        begin
          This_Npc.CreateMon('', 7, 7, 0, 'PasInterpreterOma', 1);
        end;
        procedure NpcNamed;
        begin
          This_Npc.CreateMon('PasInterpreterShared', 8, 8, 0, 'PasInterpreterOma', 1);
        end;
        procedure PlayerUnknown;
        begin
          This_Player.CreateMon('PasInterpreterMissing', 5, 5, 0, 'PasInterpreterOma', 1);
        end;
        procedure NpcUnknown;
        begin
          This_Npc.CreateMon('PasInterpreterMissing', 7, 7, 0, 'PasInterpreterOma', 1);
        end;
        begin
        end.
        """;
    var program = new PasParser(new PasLexer(source)).Parse();
    return new PasInterpreter(program, bridge);
}

static TBaseObject ExecuteSpawn(PasInterpreter interpreter, string procedure,
    MonGenInfo bucket)
{
    var objectCount = ObjectCount(M2Share.ObjectManager);
    var certificateListCount = bucket.CertList.Count;
    var certificateCount = bucket.CertCount;

    AssertNil(interpreter.ExecuteProcedure(procedure), procedure);
    Equal(objectCount + 1, ObjectCount(M2Share.ObjectManager),
        procedure + " ObjectManager count");
    Equal(certificateListCount + 1, bucket.CertList.Count,
        procedure + " certificate-list count");
    Equal(certificateCount + 1, bucket.CertCount,
        procedure + " certificate count");
    return bucket.CertList[^1];
}

static void AssertSpawn(TBaseObject monster, Envirnoment expected,
    Envirnoment unexpected, string operation)
{
    Assert(ReferenceEquals(expected, monster.m_PEnvir),
        operation + " selected the wrong physical environment");
    Equal(expected.sMapName, monster.m_sMapName,
        operation + " map-name metadata");
    Assert(CellContains(expected, monster),
        operation + " did not publish into the expected map cell");
    Assert(!CellContains(unexpected, monster),
        operation + " published into the other same-name environment");
    Assert(ReferenceEquals(monster,
            M2Share.ObjectManager.Get(monster.ObjectId)),
        operation + " ObjectManager entry");
}

static void ExpectRuntimeFailure(PasInterpreter interpreter, string procedure,
    string operation)
{
    try
    {
        interpreter.ExecuteProcedure(procedure);
    }
    catch (PasRuntimeException exception)
    {
        Assert(exception.Message.Contains("CreateMon",
                StringComparison.OrdinalIgnoreCase),
            operation + " failed for an unrelated reason: " + exception.Message);
        return;
    }
    throw new InvalidOperationException(operation + " did not fail closed");
}

static SpawnState CaptureState(MonGenInfo bucket, Envirnoment exact,
    Envirnoment registered) => new(
    ObjectCount(M2Share.ObjectManager),
    bucket.CertList.Count,
    bucket.CertCount,
    exact.MonCount,
    registered.MonCount,
    CellObjectCount(exact),
    CellObjectCount(registered));

static void AssertUnchanged(SpawnState state, MonGenInfo bucket,
    Envirnoment exact, Envirnoment registered, string operation)
{
    Equal(state.ObjectCount, ObjectCount(M2Share.ObjectManager),
        operation + " ObjectManager leak");
    Equal(state.CertificateListCount, bucket.CertList.Count,
        operation + " certificate-list leak");
    Equal(state.CertificateCount, bucket.CertCount,
        operation + " certificate-count leak");
    Equal(state.ExactMonsterCount, exact.MonCount,
        operation + " exact-environment monster-count leak");
    Equal(state.StaticMonsterCount, registered.MonCount,
        operation + " registered-environment monster-count leak");
    Equal(state.ExactCellObjectCount, CellObjectCount(exact),
        operation + " exact-environment map-cell leak");
    Equal(state.StaticCellObjectCount, CellObjectCount(registered),
        operation + " registered-environment map-cell leak");
}

static void VerifyFuncFirstDispatcher()
{
    var path = Path.Combine(FindRepositoryRoot(), "GameSvr", "ScriptSystem",
        "PasEngine", "PasInterpreter.cs");
    var source = File.ReadAllText(path);
    var playerDispatch = Slice(source, "private bool TryInvokePlayerMethod",
        "private bool TryInvokeNpcMethod");
    var npcDispatch = Slice(source, "private bool TryInvokeNpcMethod",
        "private bool TryGetObjectValue");

    AssertBefore(playerDispatch, "_api.CallPlayerFunc(",
        "_api.CallPlayerMethod(", "This_Player Func-first dispatcher");
    AssertBefore(npcDispatch, "_api.CallNpcFunc(",
        "_api.CallNpcMethod(", "This_Npc Func-first dispatcher");
}

static void AssertBefore(string source, string first, string second,
    string operation)
{
    var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
    var secondIndex = source.IndexOf(second, StringComparison.Ordinal);
    Assert(firstIndex >= 0 && secondIndex > firstIndex,
        operation + " changed");
}

static string Slice(string source, string startMarker, string endMarker)
{
    var start = source.IndexOf(startMarker, StringComparison.Ordinal);
    var end = source.IndexOf(endMarker, start + startMarker.Length,
        StringComparison.Ordinal);
    if (start < 0 || end < 0)
        throw new InvalidOperationException(
            $"source slice not found: {startMarker} -> {endMarker}");
    return source.Substring(start, end - start);
}

static Envirnoment NewEnvironment(string mapName, string mapFileName)
{
    var environment = new Envirnoment
    {
        sMapName = mapName,
        m_sMapFileName = mapFileName
    };
    typeof(Envirnoment).GetMethod("Initialize",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(environment, new object[] { (short)12, (short)12 });
    return environment;
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

static void RegisterMap(MapManager manager, Envirnoment environment)
{
    var field = typeof(MapManager).GetField("m_MapList",
        BindingFlags.Instance | BindingFlags.NonPublic)!;
    var maps = (IDictionary<string, Envirnoment>)field.GetValue(manager)!;
    maps.Add(environment.sMapName, environment);
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

static string FindRepositoryRoot()
{
    foreach (var start in new[]
             {
                 Environment.CurrentDirectory, AppContext.BaseDirectory
             })
    {
        var directory = new DirectoryInfo(start);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName,
                    "GameSvr", "GameSvr.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
    }
    throw new DirectoryNotFoundException(
        "repository root containing GameSvr/GameSvr.csproj was not found");
}

static void AssertNil(PasValue value, string operation) =>
    Assert(value.Type == PasValueType.Nil,
        operation + " did not return Nil");

static void Equal<T>(T expected, T actual, string operation)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{operation}: expected {expected}, actual {actual}");
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

sealed record SpawnState(
    int ObjectCount,
    int CertificateListCount,
    int CertificateCount,
    int ExactMonsterCount,
    int StaticMonsterCount,
    int ExactCellObjectCount,
    int StaticCellObjectCount);
