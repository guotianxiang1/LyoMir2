using System.Reflection;
using System.Net.Sockets;
using GameSvr;
using SystemModule;

PrepareRuntimeFiles();

var failures = new List<string>();
Run("new GateUser owns M2 FrontEngine", NewGateUserOwnsFrontEngine);
Run("CloseUser uses gate index and isolates equal sockets", CloseUsesGateIndex);
Run("DeleteHuman cancels main, exchanged and active loads", CancelsEveryLoadStage);
Run("concurrent delete prevents later publication", ConcurrentDeleteAndPublish);
Run("all 24 game-time hours match the reference engine", GameTimeMapping);
Run("load retry is next-cycle with no counter delay", LoadRetryCadence);

if (failures.Count != 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine("FrontEngineLoadCancellationCheck PASS tests=6 " +
                  "stages=main/temp/active gate-key=gate+socket");
return 0;

void NewGateUserOwnsFrontEngine()
{
    using var runtime = NewRuntime();
    var gate = NewGate();
    var service = new GateService(7, gate);
    try
    {
        var open = typeof(GateService).GetMethod("OpenNewUser",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(GateService).FullName,
                "OpenNewUser");
        var index = (int)open.Invoke(service,
            new object[] { 1001, (ushort)3, "127.0.0.1", gate.UserList })!;
        Equal(0, index, "new-user slot");
        Same(runtime.FrontEngine, gate.UserList[index].FrontEngine,
            "GateUser FrontEngine");
    }
    finally
    {
        service.Stop();
    }
}

void CloseUsesGateIndex()
{
    using var runtime = NewRuntime();
    const int socket = 2201;
    runtime.FrontEngine.AddToLoadRcdList("target", "Target", "127.0.0.1",
        false, 2, 1, 0, 0, socket, 1, 7);
    runtime.FrontEngine.AddToLoadRcdList("slot", "Slot", "127.0.0.1",
        false, 2, 1, 0, 0, socket, 1, 0);
    runtime.FrontEngine.AddToLoadRcdList("other", "Other", "127.0.0.1",
        false, 2, 1, 0, 0, socket, 1, 8);

    var gate = NewGate();
    gate.UserList.Add(new TGateUserInfo
    {
        nSocket = socket,
        FrontEngine = runtime.FrontEngine
    });
    gate.nUserCount = 1;
    var service = new GateService(7, gate);
    try
    {
        service.CloseUser(socket);
        var loads = LoadQueue(runtime.FrontEngine, "m_LoadRcdList");
        Equal(false, loads.Any(load => load.nGateIdx == 7 &&
                                      load.nSocket == socket),
            "closed gate load remains");
        Equal(true, loads.Any(load => load.nGateIdx == 0 &&
                                     load.nSocket == socket),
            "same socket in slot-number gate was removed");
        Equal(true, loads.Any(load => load.nGateIdx == 8 &&
                                     load.nSocket == socket),
            "same socket in another gate was removed");
    }
    finally
    {
        service.Stop();
    }
}

void CancelsEveryLoadStage()
{
    using var runtime = NewRuntime();
    const int gate = 11;
    const int socket = 3301;
    runtime.FrontEngine.AddToLoadRcdList("main1", "Main1", "127.0.0.1",
        false, 2, 1, 0, 0, socket, 1, gate);
    runtime.FrontEngine.AddToLoadRcdList("main2", "Main2", "127.0.0.1",
        false, 2, 1, 0, 0, socket, 1, gate);
    runtime.FrontEngine.AddToLoadRcdList("other", "Other", "127.0.0.1",
        false, 2, 1, 0, 0, socket, 1, gate + 1);

    var main = LoadQueue(runtime.FrontEngine, "m_LoadRcdList");
    var mainTarget = main.First(load => load.nGateIdx == gate);
    var exchangedTarget = Load(gate, socket, "Exchanged");
    var exchangedOther = Load(gate + 1, socket, "ExchangedOther");
    var exchanged = LoadQueue(runtime.FrontEngine, "m_LoadRcdTempList");
    exchanged.Add(exchangedTarget);
    exchanged.Add(exchangedOther);
    var activeTarget = Load(gate, socket, "Active");
    SetField(runtime.FrontEngine, "_activeLoadRcd", activeTarget);

    runtime.FrontEngine.DeleteHuman(gate, socket);

    Equal(false, main.Any(load => load.nGateIdx == gate &&
                                  load.nSocket == socket),
        "main-stage target remains");
    Equal(true, main.Any(load => load.nGateIdx == gate + 1 &&
                                 load.nSocket == socket),
        "main-stage other gate was removed");
    Equal(false, exchanged.Contains(exchangedTarget),
        "exchanged-stage target remains");
    Equal(true, exchanged.Contains(exchangedOther),
        "exchanged-stage other gate was removed");
    Equal(false, Publish(runtime.FrontEngine, mainTarget),
        "removed main load published");
    Equal(false, Publish(runtime.FrontEngine, exchangedTarget),
        "removed exchanged load published");
    Equal(false, Publish(runtime.FrontEngine, activeTarget),
        "cancelled active load published");
    Equal(0, M2Share.UserEngine.LoadPlayCount,
        "TUserOpenInfo count after cancellation");
}

void ConcurrentDeleteAndPublish()
{
    using var runtime = NewRuntime();
    for (var i = 0; i < 200; i++)
    {
        var load = Load(20, 4000 + i, "Race" + i);
        SetField(runtime.FrontEngine, "_activeLoadRcd", load);
        using var start = new ManualResetEventSlim(false);
        var publishTask = Task.Run(() =>
        {
            start.Wait();
            return Publish(runtime.FrontEngine, load);
        });
        var deleteTask = Task.Run(() =>
        {
            start.Wait();
            runtime.FrontEngine.DeleteHuman(load.nGateIdx, load.nSocket);
        });
        start.Set();
        Assert(Task.WaitAll(new Task[] { publishTask, deleteTask }, 5000),
            "concurrent cancellation timed out");

        var countAfterDelete = M2Share.UserEngine.LoadPlayCount;
        Equal(false, Publish(runtime.FrontEngine, load),
            "publication succeeded after DeleteHuman returned");
        Equal(countAfterDelete, M2Share.UserEngine.LoadPlayCount,
            "TUserOpenInfo appeared after DeleteHuman returned");
    }
}

void GameTimeMapping()
{
    var method = typeof(TFrontEngine).GetMethod("GetGameTimeValue",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(TFrontEngine).FullName,
            "GetGameTimeValue");
    var expected = new[]
    {
        3, 3, 3, 3, 0, 1, 1, 1, 1, 1, 1, 2,
        3, 3, 3, 0, 1, 1, 1, 1, 1, 1, 1, 2
    };
    for (var hour = 0; hour < expected.Length; hour++)
    {
        var actual = (int)method.Invoke(null, new object[] { hour })!;
        Equal(expected[hour], actual, "game-time hour " + hour);
    }
}

void LoadRetryCadence()
{
    using var runtime = NewRuntime();
    runtime.FrontEngine.AddToSaveRcdList(new TSaveRcd
    {
        sAccount = "cadence-account",
        sChrName = "Cadence"
    });
    runtime.FrontEngine.AddToLoadRcdList("cadence-account", "Cadence",
        "127.0.0.1", false, 2, 1, 0, 0, 6101, 1, 61);
    var load = LoadQueue(runtime.FrontEngine, "m_LoadRcdList").Single();
    var process = typeof(TFrontEngine).GetMethod("ProcessGameDate",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(TFrontEngine).FullName,
            "ProcessGameDate");

    for (var cycle = 0; cycle < 64; cycle++)
    {
        process.Invoke(runtime.FrontEngine, null);
        var queued = LoadQueue(runtime.FrontEngine, "m_LoadRcdList");
        var processing = LoadQueue(runtime.FrontEngine,
            "m_LoadRcdTempList");
        Equal(1, queued.Count, "retry queue count at cycle " + cycle);
        Same(load, queued[0], "retry record at cycle " + cycle);
        Equal(0, processing.Count,
            "processing queue count at cycle " + cycle);
        Equal(0, load.nReLoadCount,
            "reference-unused retry counter at cycle " + cycle);
    }
}

void Run(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine("PASS " + name);
    }
    catch (Exception exception)
    {
        failures.Add("FAIL " + name + ": " +
                     (exception.InnerException?.Message ?? exception.Message));
    }
}

static RuntimeScope NewRuntime()
{
    var frontEngine = new TFrontEngine();
    M2Share.FrontEngine = frontEngine;
    M2Share.UserEngine = new UserEngine();
    return new RuntimeScope(frontEngine);
}

static TGateInfo NewGate() => new()
{
    Socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream,
        ProtocolType.Tcp),
    UserList = new List<TGateUserInfo>()
};

static TLoadDBInfo Load(int gate, int socket, string name) => new()
{
    nGateIdx = gate,
    nSocket = socket,
    sAccount = name,
    sCharName = name,
    sIPaddr = "127.0.0.1",
    nSessionID = 2
};

static IList<TLoadDBInfo> LoadQueue(TFrontEngine engine, string fieldName)
{
    var field = typeof(TFrontEngine).GetField(fieldName,
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(TFrontEngine).FullName,
            fieldName);
    return (IList<TLoadDBInfo>)field.GetValue(engine)!;
}

static void SetField(TFrontEngine engine, string fieldName, object value)
{
    var field = typeof(TFrontEngine).GetField(fieldName,
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(TFrontEngine).FullName,
            fieldName);
    field.SetValue(engine, value);
}

static bool Publish(TFrontEngine engine, TLoadDBInfo load)
{
    var method = typeof(TFrontEngine).GetMethod("TryPublishLoadedHuman",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(TFrontEngine).FullName,
            "TryPublishLoadedHuman");
    return (bool)method.Invoke(engine,
        new object[] { load, new THumDataInfo(), null })!;
}

static void PrepareRuntimeFiles()
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

static void Same(object expected, object actual, string label)
{
    Assert(ReferenceEquals(expected, actual), label + " reference changed");
}

static void Equal<T>(T expected, T actual, string label)
{
    Assert(EqualityComparer<T>.Default.Equals(expected, actual),
        $"{label}: expected={expected}, actual={actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed class RuntimeScope : IDisposable
{
    public RuntimeScope(TFrontEngine frontEngine)
    {
        FrontEngine = frontEngine;
    }

    public TFrontEngine FrontEngine { get; }

    public void Dispose()
    {
        M2Share.UserEngine = null;
        M2Share.FrontEngine = null;
    }
}
