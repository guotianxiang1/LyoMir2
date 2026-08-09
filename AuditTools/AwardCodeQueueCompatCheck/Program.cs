using GameSvr;
using GameSvr.PasEngine;
using GameSvr.Services;
using SystemModule;
using SystemModule.Packet;

var tests = new (string Name, Action Run)[]
{
    ("strict age and shared FIFO", StrictAgeAndSharedFifo),
    ("tick wrap maturity", TickWrapMaturity),
    ("SQL exception callback mapping", SqlExceptionMapping),
    ("error reporter cannot interrupt FIFO", ErrorReporterFailure),
    ("callback failure cannot interrupt FIFO", CallbackFailure),
    ("inline runtime and shutdown shape", InlineRuntimeAndShutdownShape),
    ("empty DB configuration fails closed", EmptyConfiguration),
    ("offline drop and stable-id reconnect", OfflineAndReconnect),
    ("native SQL shape", NativeSqlShape),
    ("procedure ABI and source integration", ProcedureAbiAndIntegration)
};

foreach (var test in tests) test.Run();
Console.WriteLine(
    $"AwardCodeQueueCompatCheck PASS tests={tests.Length} " +
    "fifo=shared age=200ms inline=true one-per-process callbacks=0/1/2/5 " +
    "shutdown=drop-no-flush offline=drop reconnect=stable-int64 integration=open");
return;

void StrictAgeAndSharedFifo()
{
    var executed = new List<byte>();
    var callbacks = new List<int>();
    var events = new List<string>();
    var manager = CreateManager(task =>
    {
        executed.Add(task.TaskType);
        events.Add("execute:" + task.TaskType);
        return Success(task);
    }, completion =>
    {
        callbacks.Add(completion.Result);
        events.Add("callback:" + completion.Result);
    });

    manager.Enqueue(NativeAwardCodeTaskCodec.QueryTaskType,
        QueryPayload("Q-FIRST", 10, "old"), 1000);
    manager.Enqueue(NativeAwardCodeSetActiveParamTaskCodec.TaskType,
        SetPayload("S-SECOND", -2, 10, "old"), 1000);

    manager.Process(1199);
    Equal(0, executed.Count, "head executed before 200ms");
    Equal(2, manager.PendingCount, "premature pending count");

    manager.Process(1200);
    Sequence(new[] { NativeAwardCodeTaskCodec.QueryTaskType }, executed,
        "first execution");
    Sequence(new[] { NativeAwardCodeTaskCodec.QueryHit }, callbacks,
        "inline first callback");
    Sequence(new[]
    {
        "execute:" + NativeAwardCodeTaskCodec.QueryTaskType,
        "callback:" + NativeAwardCodeTaskCodec.QueryHit
    }, events, "first inline process order");
    Equal(1, manager.PendingCount, "one mature head per process");

    manager.Process(1200);
    Sequence(new byte[]
    {
        NativeAwardCodeTaskCodec.QueryTaskType,
        NativeAwardCodeSetActiveParamTaskCodec.TaskType
    }, executed, "shared FIFO order");
    Equal(0, manager.PendingCount, "second head not consumed");
    Sequence(new[]
    {
        NativeAwardCodeTaskCodec.QueryHit,
        NativeAwardCodeSetActiveParamTaskCodec.SuccessResult
    }, callbacks, "callback order");
}

void TickWrapMaturity()
{
    const int queued = int.MaxValue - 99;
    False(NativeAwardCodeManager.IsMature(
        unchecked(int.MinValue + 99), queued), "199ms wrap became mature");
    True(NativeAwardCodeManager.IsMature(
        unchecked(int.MinValue + 100), queued), "200ms wrap stayed pending");
}

void SqlExceptionMapping()
{
    var callbacks = new List<int>();
    var errors = 0;
    var manager = new NativeAwardCodeManager(
        _ => throw new InvalidOperationException("SQL failed"),
        completion => callbacks.Add(completion.Result),
        _ => errors++);

    manager.Enqueue(NativeAwardCodeTaskCodec.QueryTaskType,
        QueryPayload("Q", 11, "role"), 0);
    manager.Enqueue(NativeAwardCodeSetActiveParamTaskCodec.TaskType,
        SetPayload("S", -1, 11, "role"), 0);

    manager.Process(200);
    manager.Process(200);
    Sequence(new[]
    {
        NativeAwardCodeTaskCodec.QueryMiss,
        NativeAwardCodeSetActiveParamTaskCodec.FailureResult
    }, callbacks, "SQL failure mapping");
    Equal(2, errors, "SQL exceptions reported");
}

void ErrorReporterFailure()
{
    var callbacks = new List<int>();
    var reports = 0;
    var manager = new NativeAwardCodeManager(
        _ => throw new InvalidOperationException("SQL failed"),
        completion => callbacks.Add(completion.Result),
        _ =>
        {
            reports++;
            throw new InvalidOperationException("logger failed");
        });

    manager.Enqueue(NativeAwardCodeTaskCodec.QueryTaskType,
        QueryPayload("Q", 12, "role"), 0);
    manager.Enqueue(NativeAwardCodeSetActiveParamTaskCodec.TaskType,
        SetPayload("S", -2, 12, "role"), 0);
    manager.Process(200);
    manager.Process(200);

    Sequence(new[]
    {
        NativeAwardCodeTaskCodec.QueryMiss,
        NativeAwardCodeSetActiveParamTaskCodec.FailureResult
    }, callbacks,
        "error reporter failure callback");
    Equal(2, reports, "execute failure report count");
    Equal(0, manager.PendingCount, "error reporter interrupted FIFO");
}

void CallbackFailure()
{
    var executed = 0;
    var reports = 0;
    var manager = new NativeAwardCodeManager(task =>
    {
        executed++;
        return Success(task);
    }, _ => throw new InvalidOperationException("callback failed"),
        _ => reports++);

    manager.Enqueue(NativeAwardCodeTaskCodec.QueryTaskType,
        QueryPayload("Q", 13, "role"), 0);
    manager.Enqueue(NativeAwardCodeSetActiveParamTaskCodec.TaskType,
        SetPayload("S", -2, 13, "role"), 0);
    manager.Process(200);
    manager.Process(200);

    Equal(2, executed, "callback failure execution count");
    Equal(2, reports, "callback failure report count");
    Equal(0, manager.PendingCount, "callback failure interrupted FIFO");
}

void InlineRuntimeAndShutdownShape()
{
    var root = FindRepositoryRoot();
    var service = File.ReadAllText(Path.Combine(root,
        "GameSvr", "Services", "NativeAwardCodeManager.cs"));
    var gameServer = File.ReadAllText(Path.Combine(root,
        "GameSvr", "GameServer.cs"));

    False(service.Contains("Task.Run", StringComparison.Ordinal),
        "award-code SQL escaped UserEngine through Task.Run");
    False(service.Contains("ConcurrentQueue", StringComparison.Ordinal),
        "award-code completion queue returned");
    False(service.Contains("_workerRunning", StringComparison.Ordinal),
        "award-code in-flight worker state returned");
    False(service.Contains("_schedule", StringComparison.Ordinal),
        "award-code async scheduler returned");
    False(service.Contains("NativeAwardCodeService.Stop",
            StringComparison.Ordinal),
        "non-native award-code stop/drain method added");
    False(service.Contains("static void Stop(", StringComparison.Ordinal),
        "non-native award-code stop method declared");
    False(service.Contains("NativeAwardCodeService.Flush",
            StringComparison.Ordinal),
        "non-native award-code flush method added");
    False(service.Contains("static void Flush(", StringComparison.Ordinal),
        "non-native award-code flush method declared");
    False(gameServer.Contains("NativeAwardCodeService.Stop",
            StringComparison.Ordinal),
        "shutdown waits for award-code worker");
    False(gameServer.Contains("NativeAwardCodeService.Flush",
            StringComparison.Ordinal),
        "shutdown flushes award-code queue");
}

void EmptyConfiguration()
{
    PrepareRuntimeConfig();
    M2Share.g_Config = new GameSvrConfig { sConnctionString = string.Empty };

    var query = NativeAwardCodeStore.Execute(new NativeAwardCodeTask(
        NativeAwardCodeTaskCodec.QueryTaskType,
        QueryPayload("Q", 14, "role"), 0));
    Equal(NativeAwardCodeTaskCodec.QueryMiss, query.Result,
        "empty configuration query result");
    Equal(14L, query.PlayerId, "empty configuration query identity");

    var set = NativeAwardCodeStore.Execute(new NativeAwardCodeTask(
        NativeAwardCodeSetActiveParamTaskCodec.TaskType,
        SetPayload("S", -2, 14, "role"), 0));
    Equal(NativeAwardCodeSetActiveParamTaskCodec.FailureResult, set.Result,
        "empty configuration set result");
    Equal(14L, set.PlayerId, "empty configuration set identity");
}

void OfflineAndReconnect()
{
    var onlineSessions = new Dictionary<long, string> { [77] = "old" };
    var delivered = new List<string>();
    var manager = CreateManager(Success, completion =>
    {
        if (onlineSessions.TryGetValue(completion.PlayerId, out var session))
            delivered.Add(session + ":" + completion.Result);
    });

    manager.Enqueue(NativeAwardCodeTaskCodec.QueryTaskType,
        QueryPayload("Q", 77, "old-role"), 0);
    manager.Enqueue(NativeAwardCodeSetActiveParamTaskCodec.TaskType,
        SetPayload("S", -2, 77, "old-role"), 0);

    onlineSessions.Clear();
    manager.Process(200);
    Equal(0, delivered.Count, "offline callback was not dropped");

    onlineSessions[77] = "new";
    manager.Process(200);
    Sequence(new[]
    {
        "new:" + NativeAwardCodeSetActiveParamTaskCodec.SuccessResult
    }, delivered, "stable PlayerId reconnect callback");
}

void NativeSqlShape()
{
    var code = HUtil32.GbkEncoding.GetBytes(" A%_CODE ");
    Equal("Select AwardCodeType,ActiveParam,ScriptParam1,ScriptParam2," +
          "OwnerPlayerID,OwnerChrName from gamedata.awardcodes " +
          "where AwardCode like ' A%_CODE ';",
        NativeAwardCodeStore.BuildSelectSql(code), "raw LIKE select");

    var update = NativeAwardCodeStore.BuildUpdateSql(code, -2,
        9223372036854770000L, HUtil32.GbkEncoding.GetBytes("角色"));
    Equal("Update gamedata.awardcodes  set ActiveParam = -2, " +
          "OwnerPlayerID = 9223372036854770000, OwnerChrName = '角色', " +
          "ModifyDate = Now() where AwardCode like ' A%_CODE ';",
        update, "raw LIKE update");
    var where = update.Substring(update.IndexOf(" where ",
        StringComparison.Ordinal));
    False(where.Contains("OwnerPlayerID", StringComparison.Ordinal),
        "non-native owner predicate added to UPDATE");
    False(update.Contains("limit", StringComparison.OrdinalIgnoreCase),
        "non-native UPDATE limit added");
}

void ProcedureAbiAndIntegration()
{
    PrepareRuntimeConfig();
    M2Share.g_Config = new GameSvrConfig();
    M2Share.ObjectManager = new ObjectManager();
    var player = new TPlayObject();
    var bridge = new PasApiBridge { CurrentPlayer = player };

    True(bridge.CallPlayerMethod("QueryAwardCode", Values(" CODE ")),
        "QueryAwardCode procedure is not open");
    False(bridge.CallPlayerMethod("QueryAwardCode", Values()),
        "QueryAwardCode accepted zero arguments");
    True(bridge.CallPlayerMethod("SetAwardCodeActiveParam",
        Values("CODE", -2)), "SetAwardCodeActiveParam procedure is not open");
    False(bridge.CallPlayerMethod("SetAwardCodeActiveParam", Values("CODE")),
        "SetAwardCodeActiveParam accepted one argument");
    False(bridge.CallPlayerFunc("QueryAwardCode", Values("CODE"), out _),
        "QueryAwardCode function shadow reopened");

    var root = FindRepositoryRoot();
    var service = File.ReadAllText(Path.Combine(root,
        "GameSvr", "Services", "NativeAwardCodeManager.cs"));
    var bridgeSource = File.ReadAllText(Path.Combine(root,
        "GameSvr", "ScriptSystem", "PasEngine", "PasApiBridge.cs"));
    var userEngine = File.ReadAllText(Path.Combine(root,
        "GameSvr", "UsrSystem", "UsrEngn.cs"));

    Contains(bridgeSource, "CurrentPlayer.QueryNativeAwardCode(",
        "Query procedure dispatch");
    Contains(bridgeSource, "CurrentPlayer.SetNativeAwardCodeActiveParam(",
        "Set procedure dispatch");
    Contains(userEngine, "NativeAwardCodeService.Process(HUtil32.GetTickCount())",
        "UserEngine completion pump");
    Contains(service, "GetCachedNativeUserId() == playerId",
        "stable Int64 online lookup");
    False(service.Contains("m_sCharName ==", StringComparison.Ordinal),
        "callback added role-name revalidation");
    False(service.Contains("BeginTransaction", StringComparison.Ordinal),
        "transaction added to native non-transactional worker");
    False(service.Contains("CREATE TABLE", StringComparison.OrdinalIgnoreCase),
        "award-code substitute table added");
    False(service.Contains("mir3.user_index", StringComparison.OrdinalIgnoreCase),
        "synthetic idx lookup returned");
    False(service.Contains("retry", StringComparison.OrdinalIgnoreCase),
        "non-native SQL retry added");
}

static NativeAwardCodeManager CreateManager(
    Func<NativeAwardCodeTask, NativeAwardCodeCompletion> execute,
    Action<NativeAwardCodeCompletion> complete)
{
    return new NativeAwardCodeManager(execute, complete,
        ex => throw ex);
}

static NativeAwardCodeCompletion Success(NativeAwardCodeTask task)
{
    if (task.TaskType == NativeAwardCodeTaskCodec.QueryTaskType
        && NativeAwardCodeTaskCodec.TryDecodeQuery(task.Payload,
            out var query, out var queryError))
    {
        return new NativeAwardCodeCompletion(task.TaskType,
            query.PlayerId, query.CodeBytes,
            NativeAwardCodeTaskCodec.QueryHit, 101, 202);
    }
    if (task.TaskType == NativeAwardCodeSetActiveParamTaskCodec.TaskType
        && NativeAwardCodeSetActiveParamTaskCodec.TryDecode(task.Payload,
            out var set, out var setError))
    {
        return new NativeAwardCodeCompletion(task.TaskType,
            set.PlayerId, set.CodeBytes,
            NativeAwardCodeSetActiveParamTaskCodec.SuccessResult,
            303, set.ActiveParam);
    }
    throw new InvalidOperationException("bad award-code test task");
}

static byte[] QueryPayload(string code, long playerId, string role)
{
    True(NativeAwardCodeTaskCodec.TryEncodeQuery(code, playerId, role,
        out var payload, out var error), error);
    return payload;
}

static byte[] SetPayload(string code, int activeParam, long playerId,
    string role)
{
    True(NativeAwardCodeSetActiveParamTaskCodec.TryEncode(code, activeParam,
        playerId, role, out var payload, out var error), error);
    return payload;
}

static List<PasValue> Values(params object[] values) => values.Select(value =>
    value is int number ? PasValue.FromInt(number) :
    PasValue.FromString(value?.ToString() ?? string.Empty)).ToList();

static string FindRepositoryRoot()
{
    foreach (var seed in new[]
             {
                 Directory.GetCurrentDirectory(), AppContext.BaseDirectory
             })
    {
        var directory = new DirectoryInfo(seed);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName,
                    "GameSvr", "GameSvr.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
    }
    throw new DirectoryNotFoundException("repository root not found");
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

static void Contains(string source, string expected, string name)
{
    True(source.Contains(expected, StringComparison.Ordinal),
        name + " missing");
}

static void True(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void False(bool condition, string message)
{
    if (condition) throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual, string name)
    where T : IEquatable<T>
{
    if (!expected.Equals(actual))
        throw new InvalidOperationException(
            $"{name}: expected {expected}, got {actual}");
}

static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual,
    string name)
{
    if (!expected.SequenceEqual(actual))
        throw new InvalidOperationException(name + " sequence differs");
}
