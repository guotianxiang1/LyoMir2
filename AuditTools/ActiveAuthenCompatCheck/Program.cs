using System.Reflection;
using System.Collections.Concurrent;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.ObjectManager = new ObjectManager();
M2Share.LogMsgCriticalSection = new object();
M2Share.ProcessHumanCriticalSection = new object();
M2Share.LogStringList = new System.Collections.ArrayList();

var player = new TPlayObject
{
    m_sMapName = "3",
    m_sCharName = "认证测试",
    m_nCurrX = 10,
    m_nCurrY = 20
};
var setStatus = GetMethod("SetNativeAuthenticationStatus",
    typeof(byte), typeof(byte), typeof(byte));
var core = GetMethod("ActiveNativeAuthentication100",
    typeof(Func<byte, int>), typeof(Action), typeof(Action<int, string>));
var buildStatus = GetMethod("BuildNativeAuthenticationStatusMessage");
var clearIdentity = GetMethod("ClearNativeAuthenticationIdentity");
var setIdentity = GetMethod("SetNativeAuthenticationIdentity",
    typeof(long), typeof(byte[]));
var tryGetIdentity = GetMethod("TryGetNativeAuthenticationIdentity",
    typeof(long).MakeByRefType(), typeof(byte[]).MakeByRefType());
var writeAuthenticationLog = GetMethod("WriteNativeAuthenticationLog",
    typeof(int), typeof(string));
var status1Field = GetField("_nativeAuthStatus1");
var status2Field = GetField("_nativeAuthStatus2");

clearIdentity.Invoke(player, null);
var missingIdentity = new object[] { 0L, null! };
Equal(false, (bool)tryGetIdentity.Invoke(player, missingIdentity)!,
    "cleared identity must remain unavailable");
setIdentity.Invoke(player, new object[] { 0L, Array.Empty<byte>() });
var loadedEmptyIdentity = new object[] { long.MinValue, null! };
Equal(true, (bool)tryGetIdentity.Invoke(player, loadedEmptyIdentity)!,
    "loaded native identity must not validate its contents");
Equal(0L, (long)loadedEmptyIdentity[0], "loaded zero PlayerId");
Equal(0, ((byte[])loadedEmptyIdentity[1]).Length, "loaded empty PTID");

writeAuthenticationLog.Invoke(player,
    new object[] { 1, "信用分验证成功" });
Equal(1, M2Share.LogStringList.Count, "type 95 log count");
Equal("95\t3\t10\t20\t认证测试\t认证测试\t100\t1\t信用分验证成功",
    (string)M2Share.LogStringList[0]!, "type 95 exact columns");
M2Share.LogStringList.Clear();

M2Share.g_Config.boAuthOpen = false;
SetStatus(0x80, 0x40, 0);
player.m_nGoldMax = 123;
player.m_nStorageSpaceCount = 77;
var disabledCalls = 0;
Equal(0, InvokeCore(
    _ => { disabledCalls++; return 1; },
    () => disabledCalls++,
    (_, _) => disabledCalls++), "disabled result");
Equal(0, disabledCalls, "disabled side effects");
Equal((byte)0x80, Status1(), "disabled Status1");
Equal((byte)0x40, Status2(), "disabled Status2");
Equal(123, player.m_nGoldMax, "disabled gold limit unchanged");
Equal(77, player.m_nStorageSpaceCount, "disabled storage limit unchanged");

M2Share.g_Config.boAuthOpen = true;
SetStatus(0xFF, 0x40, 0);
var duplicateEvents = new List<string>();
Equal(2, InvokeCore(
    _ => throw new InvalidOperationException("duplicate state persisted"),
    () => throw new InvalidOperationException("duplicate state sent 4636"),
    (code, description) => duplicateEvents.Add($"log:{code}:{description}")),
    "duplicate result");
Sequence(new[] { "log:2:信用分验证失败" }, duplicateEvents,
    "duplicate event order");
Equal((byte)0xFF, Status1(), "duplicate Status1");
Equal((byte)0x40, Status2(), "duplicate Status2");

SetStatus(0xA4, 0x55, 0);
player.m_nGoldMax = 2_000_000;
player.m_nStorageSpaceCount = 24;
var successEvents = new List<string>();
byte persistedStatus = 0;
Equal(1, InvokeCore(
    status =>
    {
        persistedStatus = status;
        successEvents.Add("sql");
        return 1;
    },
    () =>
    {
        Equal(50_000_000, player.m_nGoldMax,
            "limits must apply before 4636 gold");
        Equal(192, player.m_nStorageSpaceCount,
            "limits must apply before 4636 storage");
        var message = (ClientPacket)buildStatus.Invoke(player, null)!;
        Equal(Grobal2.SM_PLAYER_AUTHEN, (int)message.Ident, "success ident");
        Equal(0, message.Recog, "success recog");
        Equal((ushort)0, message.Param, "success param");
        Equal((ushort)0, message.Tag, "success tag");
        Equal((ushort)0, message.Series, "success series");
        successEvents.Add("4636");
    },
    (code, description) =>
        successEvents.Add($"log:{code}:{description}")), "success result");
Equal((byte)0xBF, persistedStatus, "persisted aggregate status");
Equal((byte)0xBF, Status1(), "success Status1 preserves upper bits");
Equal((byte)0x55, Status2(), "success Status2 unchanged");
Sequence(new[] { "sql", "4636", "log:1:信用分验证成功" },
    successEvents, "success event order");

SetStatus(0xA0, 0x5A, 0);
player.m_nGoldMax = 321;
player.m_nStorageSpaceCount = 78;
var failureEvents = new List<string>();
Equal(-1, InvokeCore(
    status =>
    {
        Equal((byte)0xBF, status, "failure attempted aggregate status");
        failureEvents.Add("sql");
        return -1;
    },
    () => failureEvents.Add("unexpected-4636"),
    (code, description) =>
        failureEvents.Add($"log:{code}:{description}")), "SQL failure result");
Equal((byte)0xA0, Status1(), "SQL failure Status1 rollback");
Equal((byte)0x5A, Status2(), "SQL failure Status2 rollback");
Equal(321, player.m_nGoldMax, "SQL failure gold limit unchanged");
Equal(78, player.m_nStorageSpaceCount, "SQL failure storage limit unchanged");
Sequence(new[] { "sql", "log:-1:信用分验证失败" }, failureEvents,
    "SQL failure event order");

SetStatus(0, 0, 0);
using (var persistEntered = new ManualResetEventSlim())
using (var releasePersist = new ManualResetEventSlim())
using (var secondStarted = new ManualResetEventSlim())
{
    var concurrentEvents = new ConcurrentQueue<string>();
    var persistCalls = 0;
    int ConcurrentPersist(byte _)
    {
        var call = Interlocked.Increment(ref persistCalls);
        concurrentEvents.Enqueue($"sql:{call}");
        if (call == 1)
        {
            persistEntered.Set();
            if (!releasePersist.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("concurrent ActiveAuthen release timeout");
        }
        return 1;
    }

    var first = Task.Run(() => InvokeCore(ConcurrentPersist,
        () => concurrentEvents.Enqueue("4636"),
        (code, _) => concurrentEvents.Enqueue($"log:{code}")));
    Assert(persistEntered.Wait(TimeSpan.FromSeconds(5)),
        "first ActiveAuthen did not enter persistence");
    var second = Task.Run(() =>
    {
        secondStarted.Set();
        return InvokeCore(ConcurrentPersist,
            () => concurrentEvents.Enqueue("unexpected-4636"),
            (code, _) => concurrentEvents.Enqueue($"log:{code}"));
    });
    Assert(secondStarted.Wait(TimeSpan.FromSeconds(5)),
        "second ActiveAuthen did not start");
    try
    {
        Assert(!second.Wait(150),
            "second ActiveAuthen escaped the shared authentication lock");
    }
    finally
    {
        releasePersist.Set();
    }

    Equal(1, first.GetAwaiter().GetResult(), "first concurrent ActiveAuthen result");
    Equal(2, second.GetAwaiter().GetResult(), "second concurrent ActiveAuthen result");
    Equal(1, persistCalls, "concurrent ActiveAuthen persistence count");
    Sequence(new[] { "sql:1", "4636", "log:1", "log:2" },
        concurrentEvents, "concurrent ActiveAuthen event order");
}

SetStatus(0, 0, 0);
var lifecycleEngine = new UserEngine();
var playObjectListField = typeof(UserEngine).GetField("m_PlayObjectList",
    BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new MissingFieldException("m_PlayObjectList");
var playObjects = (IList<TPlayObject>)playObjectListField.GetValue(lifecycleEngine)!;
var oldPlayer = player;
oldPlayer.m_sCharName = "慢认证同名角色";
oldPlayer.m_boGhost = false;
oldPlayer.m_boSoftClose = false;
oldPlayer.m_boEmergencyClose = false;
var replacementPlayer = new TPlayObject
{
    m_sCharName = oldPlayer.m_sCharName,
    m_boGhost = false
};
playObjects.Add(oldPlayer);
M2Share.UserEngine = lifecycleEngine;
using (var slowSqlEntered = new ManualResetEventSlim())
using (var releaseSlowSql = new ManualResetEventSlim())
using (var reloginStarted = new ManualResetEventSlim())
{
    var lifecycleEvents = new ConcurrentQueue<string>();
    var authentication = Task.Run(() =>
    {
        lock (M2Share.ProcessHumanCriticalSection)
        {
            return InvokeCore(_ =>
                {
                    lifecycleEvents.Enqueue("sql:start");
                    slowSqlEntered.Set();
                    if (!releaseSlowSql.Wait(TimeSpan.FromSeconds(5)))
                        throw new TimeoutException("slow authentication release timeout");
                    return 1;
                },
                () => lifecycleEvents.Enqueue(
                    $"4636:old:{oldPlayer.m_boGhost}:{ReferenceEquals(lifecycleEngine.GetPlayObjectEx(oldPlayer.m_sCharName), oldPlayer)}"),
                (code, _) => lifecycleEvents.Enqueue($"log:{code}"));
        }
    });
    Assert(slowSqlEntered.Wait(TimeSpan.FromSeconds(5)),
        "slow authentication did not enter SQL");

    var gateSignal = Task.Run(() =>
    {
        oldPlayer.m_boSoftClose = true;
        lifecycleEvents.Enqueue("gate:soft-close");
        lifecycleEngine.AddUserOpenInfo(new TUserOpenInfo
        {
            sChrName = replacementPlayer.m_sCharName
        });
        lifecycleEvents.Enqueue("gate:relogin-queued");
    });
    Assert(gateSignal.Wait(1000),
        "gate signal was incorrectly blocked by the GameState domain");

    var disconnectAndRelogin = Task.Run(() =>
    {
        reloginStarted.Set();
        lock (M2Share.ProcessHumanCriticalSection)
        {
            oldPlayer.m_boGhost = true;
            playObjects.Remove(oldPlayer);
            lifecycleEvents.Enqueue("disconnect:old");
            playObjects.Add(replacementPlayer);
            lifecycleEvents.Enqueue("relogin:new");
        }
    });
    Assert(reloginStarted.Wait(TimeSpan.FromSeconds(5)),
        "disconnect/relogin task did not start");
    try
    {
        Assert(!disconnectAndRelogin.Wait(150),
            "disconnect/relogin escaped the GameState execution domain");
    }
    finally
    {
        releaseSlowSql.Set();
    }

    Equal(1, authentication.GetAwaiter().GetResult(),
        "slow authentication result");
    disconnectAndRelogin.GetAwaiter().GetResult();
    Sequence(new[]
        {
            "sql:start", "gate:soft-close", "gate:relogin-queued",
            "4636:old:False:True", "log:1",
            "disconnect:old", "relogin:new"
        }, lifecycleEvents, "slow authentication lifecycle order");
    Assert(oldPlayer.m_boGhost && oldPlayer.m_boSoftClose &&
           !oldPlayer.m_boEmergencyClose,
        "old role did not complete normal disconnect flags");
    Assert(ReferenceEquals(lifecycleEngine.GetPlayObjectEx(oldPlayer.m_sCharName),
            replacementPlayer),
        "same-name relogin replacement missing");
    Assert(!replacementPlayer.m_boGhost && !replacementPlayer.m_boSoftClose &&
           !replacementPlayer.m_boEmergencyClose,
        "old authentication mutated replacement role");
}

var bridge = new PasApiBridge { CurrentPlayer = player };
M2Share.g_Config.boAuthOpen = false;
Assert(bridge.CallPlayerFunc("ActiveAuthen", Args(1, 100), out var disabledResult),
    "exact ActiveAuthen tuple dispatch");
Equal(0, disabledResult.AsInt(), "exact ActiveAuthen disabled return");

M2Share.g_Config.boAuthOpen = true;
foreach (var tupleArgs in new[]
         {
             Args(), Args(1), Args(1, 99), Args(2, 100),
             Args(1, 100, 0), Args(257, 100), Args(1, 356)
         })
{
    Assert(!bridge.CallPlayerFunc("ActiveAuthen", tupleArgs, out var rejected),
        "unsupported ActiveAuthen tuple accepted");
    Equal(PasValueType.Nil, rejected.Type,
        "unsupported ActiveAuthen result must remain Nil");
}

VerifySourceContract();
Console.WriteLine("ActiveAuthenCompatCheck PASS");
return;

int InvokeCore(Func<byte, int> persist, Action send,
    Action<int, string> log) =>
    (int)core.Invoke(player, new object[] { persist, send, log })!;

void SetStatus(byte status1, byte status2, byte status3) =>
    setStatus.Invoke(player, new object[] { status1, status2, status3 });

byte Status1() => (byte)status1Field.GetValue(player)!;
byte Status2() => (byte)status2Field.GetValue(player)!;

static List<PasValue> Args(params int[] values) =>
    values.Select(PasValue.FromInt).ToList();

static MethodInfo GetMethod(string name, params Type[] parameterTypes) =>
    typeof(TPlayObject).GetMethod(name,
        BindingFlags.Instance | BindingFlags.NonPublic, null,
        parameterTypes, null) ?? throw new MissingMethodException(name);

static FieldInfo GetField(string name) =>
    typeof(TPlayObject).GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new MissingFieldException(name);

static void VerifySourceContract()
{
    var root = FindRepositoryRoot();
    var manager = File.ReadAllText(Path.Combine(root,
        "GameSvr", "Services", "NativeAuthenticationManager.cs"));
    var player = File.ReadAllText(Path.Combine(root,
        "GameSvr", "Players", "TPlayObject.NativeAuthentication.cs"));
    var bridge = File.ReadAllText(Path.Combine(root,
        "GameSvr", "ScriptSystem", "PasEngine", "PasApiBridge.cs"));
    var userEngine = File.ReadAllText(Path.Combine(root,
        "GameSvr", "UsrSystem", "UsrEngn.cs"));
    var gameServer = File.ReadAllText(Path.Combine(root,
        "GameSvr", "GameServer.cs"));

    Require(manager,
        "Select PlayerId from gamedata.AuthenticateUser where PlayerId = @playerId;",
        "native existence SELECT missing");
    Require(manager,
        "Insert Into gamedata.AuthenticateUser(PlayerId, Status1, AuthenDate, PTID)",
        "native Status1 INSERT missing");
    Require(manager,
        "update gamedata.AuthenticateUser set Status1 = @status,",
        "native Status1 UPDATE missing");
    Require(manager, "AuthenDate = Now()",
        "native authentication timestamp missing");
    Require(manager, "MySqlDbType.Int64",
        "PlayerId must remain 64-bit");
    Require(manager, "MySqlDbType.VarBinary, 20",
        "PTID must match native Char(20) binary storage");
    Require(manager, "i.UserId, Cast(i.PTID as Binary)",
        "PTID login source must be a binary result");
    var ptidReadAt = manager.IndexOf("private static byte[] ReadPtidBytes(",
        StringComparison.Ordinal);
    var openConnectionAt = manager.IndexOf("private static MySqlConnection OpenConnection()",
        ptidReadAt, StringComparison.Ordinal);
    Assert(ptidReadAt >= 0 && openConnectionAt > ptidReadAt,
        "raw PTID reader block missing");
    var ptidReader = manager.Substring(ptidReadAt, openConnectionAt - ptidReadAt);
    Require(ptidReader, "value is not byte[] binary",
        "PTID reader must reject non-binary provider values");
    Require(ptidReader, "throw new InvalidDataException",
        "PTID provider mismatch must fail closed");
    Require(ptidReader, "return binary.ToArray();",
        "PTID reader must preserve the provider bytes");
    Reject(ptidReader, "Encoding.Latin1",
        "PTID reader must not guess Latin1 from character values");
    Reject(ptidReader, "GbkEncoding.GetBytes",
        "PTID reader must not round-trip provider text through GBK");
    Reject(ptidReader, "Convert.ToString",
        "PTID reader must not accept provider text");
    Require(manager, "rowCount = -1;",
        "SELECT failure must enter the native INSERT branch");
    Reject(manager, "BeginTransaction", "transaction added");
    Reject(manager, "FOR UPDATE", "row lock added");
    Reject(manager, "ON DUPLICATE KEY", "upsert added");
    Reject(manager, "ExecuteNonQuery() !=", "affected-row gate added");
    var clearAt = manager.IndexOf("player.ClearNativeAuthenticationIdentity();",
        StringComparison.Ordinal);
    var readerAt = manager.IndexOf("if (!reader.Read())",
        clearAt, StringComparison.Ordinal);
    var setAt = manager.IndexOf("player.SetNativeAuthenticationIdentity(",
        readerAt, StringComparison.Ordinal);
    Assert(clearAt >= 0 && readerAt > clearAt && setAt > readerAt,
        "identity must remain unavailable until login query returns a row");
    Reject(manager, "playerId <= 0", "PlayerId content gate added");
    Reject(manager, "ptid.Length == 0", "PTID content gate added");

    Require(player, "_nativeAuthStatus1 |= 0x1F;",
        "aggregate Status1 mutation missing");
    Require(player, "_nativeAuthStatus1 = savedStatus1;",
        "Status1 rollback missing");
    Require(player, "_nativeAuthStatus2 = savedStatus2;",
        "Status2 rollback missing");
    Require(player, "ApplyNativeAuthenticationLimits();",
        "derived limit update missing");
    Require(player, "sendStatus();",
        "4636 callback missing");
    Require(player, "writeLog(result, result == 1",
        "type 95 result log missing");
    Require(player, "private readonly object _nativeAuthenticationSync = new();",
        "shared authentication lock missing");
    Equal(3, CountOccurrences(player, "lock (_nativeAuthenticationSync)"),
        "ActiveAuthen, ActiveDelAuthen and HelpOtherAuthen must share one player lock");
    Require(player, "M2Share.AddGameDataLog(\"95\\t\"",
        "type 95 native game log missing");
    Reject(player, "SysMsg(", "ActiveAuthen must not send SysMsg");

    var activeAt = bridge.LastIndexOf("case \"activeauthen\":",
        StringComparison.OrdinalIgnoreCase);
    var checkAt = bridge.IndexOf("case \"checkauthen\":",
        activeAt, StringComparison.OrdinalIgnoreCase);
    Assert(activeAt >= 0 && checkAt > activeAt,
        "ActiveAuthen function gate missing");
    var gate = bridge.Substring(activeAt, checkAt - activeAt);
    Require(gate, "args.Count != 2", "exact argument count gate missing");
    Require(gate, "args[0].AsInt() != 1", "exact level gate missing");
    Require(gate, "args[1].AsInt() != 100", "exact order gate missing");
    Require(gate, "return false;",
        "unsupported tuples must remain fail-closed");
    Require(gate, "PasValue.FromInt(", "integer return mapping missing");

    var processDataAt = userEngine.IndexOf("private void PrcocessData()",
        StringComparison.Ordinal);
    var getHomeInfoAt = userEngine.IndexOf("public string GetHomeInfo(",
        processDataAt, StringComparison.Ordinal);
    Assert(processDataAt >= 0 && getHomeInfoAt > processDataAt,
        "UserEngine process loop missing");
    var processData = userEngine.Substring(processDataAt,
        getHomeInfoAt - processDataAt);
    AssertOrdered(processData,
        "HUtil32.EnterCriticalSection(M2Share.ProcessHumanCriticalSection);",
        "ProcessUiActions();",
        "ProcessHumans();",
        "ProcessNpcs();",
        "HUtil32.LeaveCriticalSection(M2Share.ProcessHumanCriticalSection);",
        "Thread.Sleep(20);");

    var aiAt = userEngine.IndexOf("private void ProcessAiPlayObjectData()",
        StringComparison.Ordinal);
    var playerAt = userEngine.IndexOf("private void ProcessPlayObjectData()",
        aiAt, StringComparison.Ordinal);
    Assert(aiAt >= 0 && playerAt > aiAt, "AI player process loop missing");
    var aiProcess = userEngine.Substring(aiAt, playerAt - aiAt);
    // Audited contract is the ORDER: Enter -> Run() -> (save) -> Leave -> Sleep(30).
    // The AI-thread ghost-cleanup site now calls the 2-arg overload
    // SaveHumanRcd(PlayObject, 3). The saveMode arg (3 = reason/mode code) semantic
    // is pending Tier-1 native confirmation — see staging/idat_batch_queue_20260803.md.
    // Only the token text is refreshed; the ordering assertion is unchanged.
    AssertOrdered(aiProcess,
        "HUtil32.EnterCriticalSection(M2Share.ProcessHumanCriticalSection);",
        "PlayObject.Run();",
        "SaveHumanRcd(PlayObject, 3);",
        "HUtil32.LeaveCriticalSection(M2Share.ProcessHumanCriticalSection);",
        "Thread.Sleep(30);");

    var runAt = gameServer.IndexOf("public void Run()", StringComparison.Ordinal);
    var noticeAt = gameServer.IndexOf("private void ProcessGameNotice()",
        runAt, StringComparison.Ordinal);
    Assert(runAt >= 0 && noticeAt > runAt, "GameServer Run loop missing");
    var serverRun = gameServer.Substring(runAt, noticeAt - runAt);
    AssertOrdered(serverRun,
        "M2Share.GateManager.Run();",
        "HUtil32.EnterCriticalSection(M2Share.ProcessHumanCriticalSection);",
        "M2Share.PasEngine?.ProcessDeferredCalls();",
        "M2Share.PasEngine?.ProcessAutoScripts();",
        "HUtil32.LeaveCriticalSection(M2Share.ProcessHumanCriticalSection);",
        "Mall.MallManager.Instance.ProcessScheduledRefresh(DateTime.Now);");

    var stopAt = userEngine.IndexOf("public void Stop()", StringComparison.Ordinal);
    var initializeAt = userEngine.IndexOf("public void Initialize()",
        stopAt, StringComparison.Ordinal);
    Assert(stopAt >= 0 && initializeAt > stopAt, "UserEngine Stop block missing");
    var stop = userEngine.Substring(stopAt, initializeAt - stopAt);
    Require(stop, "JoinThread(_userEngineThread);",
        "UserEngine thread join missing");
    Reject(stop, "ProcessHumanCriticalSection",
        "Stop/Join must remain outside the GameState execution domain");
}

static void AssertOrdered(string source, params string[] values)
{
    var offset = 0;
    foreach (var value in values)
    {
        var index = source.IndexOf(value, offset, StringComparison.Ordinal);
        Assert(index >= 0, $"source order token missing: {value}");
        offset = index + value.Length;
    }
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

static string FindRepositoryRoot()
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
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
    throw new DirectoryNotFoundException("GameSvr repository root not found");
}

static void Sequence(IEnumerable<string> expected, IEnumerable<string> actual,
    string message) => Equal(string.Join("|", expected), string.Join("|", actual), message);

static void Require(string source, string value, string message) =>
    Assert(source.Contains(value, StringComparison.OrdinalIgnoreCase), message);

static void Reject(string source, string value, string message) =>
    Assert(!source.Contains(value, StringComparison.OrdinalIgnoreCase), message);

static int CountOccurrences(string source, string value)
{
    var count = 0;
    for (var index = 0; (index = source.IndexOf(value, index,
             StringComparison.Ordinal)) >= 0; index += value.Length)
        count++;
    return count;
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
