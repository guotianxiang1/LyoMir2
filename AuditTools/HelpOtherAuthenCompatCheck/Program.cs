using System.Reflection;
using System.Collections.Concurrent;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.ObjectManager = new ObjectManager();
M2Share.AuthenticationManager = new NativeAuthenticationManager();
M2Share.LogMsgCriticalSection = new object();
M2Share.LogStringList = new System.Collections.ArrayList();

var player = new TPlayObject
{
    m_sMapName = "3",
    m_sCharName = "帮助认证测试",
    m_nCurrX = 11,
    m_nCurrY = 22
};
var setStatus = GetMethod("SetNativeAuthenticationStatus",
    typeof(byte), typeof(byte), typeof(byte));
var core = GetMethod("HelpOtherNativeAuthentication",
    typeof(Func<int>), typeof(Action));
var writeLog = GetMethod("WriteNativeHelpOtherLog");
var clearIdentity = GetMethod("ClearNativeAuthenticationIdentity");
var setIdentity = GetMethod("SetNativeAuthenticationIdentity",
    typeof(long), typeof(byte[]));
var tryGetIdentity = GetMethod("TryGetNativeAuthenticationIdentity",
    typeof(long).MakeByRefType(), typeof(byte[]).MakeByRefType());
var markHelpOther = typeof(NativeAuthenticationManager).GetMethod(
    "MarkHelpOther", BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new MissingMethodException("MarkHelpOther");
var status1Field = GetField("_nativeAuthStatus1");
var status2Field = GetField("_nativeAuthStatus2");
var status3Field = GetField("_nativeAuthStatus3");

clearIdentity.Invoke(player, null);
var missingIdentity = new object[] { 0L, null! };
Equal(false, (bool)tryGetIdentity.Invoke(player, missingIdentity)!,
    "cleared identity must remain unavailable");
Equal(0, (int)markHelpOther.Invoke(M2Share.AuthenticationManager,
        new object[] { player })!,
    "unloaded identity result");
setIdentity.Invoke(player, new object[] { long.MinValue, Array.Empty<byte>() });
var loadedIdentity = new object[] { 0L, null! };
Equal(true, (bool)tryGetIdentity.Invoke(player, loadedIdentity)!,
    "loaded identity must not validate contents");
Equal(long.MinValue, (long)loadedIdentity[0], "loaded Int64 PlayerId");

SetStatus(0xA5, 0x5A, 0x01);
player.m_nGoldMax = 123;
player.m_nStorageSpaceCount = 77;
foreach (var code in new[] { 0, 2, -2 })
{
    var events = new List<string>();
    Equal(code, InvokeCore(
        () => { events.Add("sql"); return code; },
        () => events.Add("unexpected-log")), $"result {code}");
    Sequence(new[] { "sql" }, events, $"result {code} events");
    AssertStateUnchanged($"result {code}");
}

M2Share.g_Config.boAuthOpen = false;
var successEvents = new List<string>();
Equal(1, InvokeCore(
    () => { successEvents.Add("sql"); return 1; },
    () =>
    {
        AssertStateUnchanged("success before log");
        successEvents.Add("log");
    }), "success result while authentication disabled");
Sequence(new[] { "sql", "log" }, successEvents, "success event order");
AssertStateUnchanged("success");

writeLog.Invoke(player, null);
Equal(1, M2Share.LogStringList.Count, "type 94 log count");
Equal("94\t3\t11\t22\t帮助认证测试\t帮助认证测试\t1\t0\t申请验证小号成功",
    (string)M2Share.LogStringList[0]!, "type 94 exact columns");
M2Share.LogStringList.Clear();

using (var markEntered = new ManualResetEventSlim())
using (var releaseMark = new ManualResetEventSlim())
using (var secondStarted = new ManualResetEventSlim())
{
    var helpOtherState = 0;
    var markCalls = 0;
    var concurrentEvents = new ConcurrentQueue<string>();
    int ConcurrentMark()
    {
        if (Volatile.Read(ref helpOtherState) == 1)
        {
            concurrentEvents.Enqueue("sql:2");
            Interlocked.Increment(ref markCalls);
            return 2;
        }

        var call = Interlocked.Increment(ref markCalls);
        concurrentEvents.Enqueue($"sql:{call}");
        if (call == 1)
        {
            markEntered.Set();
            if (!releaseMark.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("concurrent HelpOtherAuthen release timeout");
        }
        Volatile.Write(ref helpOtherState, 1);
        return 1;
    }

    var first = Task.Run(() => InvokeCore(ConcurrentMark,
        () => concurrentEvents.Enqueue("log")));
    Assert(markEntered.Wait(TimeSpan.FromSeconds(5)),
        "first HelpOtherAuthen did not enter persistence");
    var second = Task.Run(() =>
    {
        secondStarted.Set();
        return InvokeCore(ConcurrentMark,
            () => concurrentEvents.Enqueue("unexpected-log"));
    });
    Assert(secondStarted.Wait(TimeSpan.FromSeconds(5)),
        "second HelpOtherAuthen did not start");
    try
    {
        Assert(!second.Wait(150),
            "second HelpOtherAuthen escaped the shared authentication lock");
    }
    finally
    {
        releaseMark.Set();
    }

    Equal(1, first.GetAwaiter().GetResult(), "first concurrent HelpOtherAuthen result");
    Equal(2, second.GetAwaiter().GetResult(), "second concurrent HelpOtherAuthen result");
    Equal(2, markCalls, "concurrent HelpOtherAuthen query count");
    Sequence(new[] { "sql:1", "log", "sql:2" }, concurrentEvents,
        "concurrent HelpOtherAuthen event order");
}

clearIdentity.Invoke(player, null);
var bridge = new PasApiBridge { CurrentPlayer = player };
Assert(bridge.CallPlayerFunc("HelpOtherAuthen", Args(), out var exactResult),
    "zero-argument HelpOtherAuthen dispatch");
Equal(0, exactResult.AsInt(), "unloaded exact dispatch result");
foreach (var invalidArgs in new[] { Args(0), Args(1, 100) })
{
    Assert(!bridge.CallPlayerFunc("HelpOtherAuthen", invalidArgs, out var rejected),
        "HelpOtherAuthen accepted arguments");
    Equal(PasValueType.Nil, rejected.Type,
        "rejected HelpOtherAuthen result must remain Nil");
}
Assert(!bridge.CallPlayerMethod("HelpOtherAuthen", Args()),
    "HelpOtherAuthen procedure form must remain fail-closed");

VerifySourceContract();
Console.WriteLine("HelpOtherAuthenCompatCheck PASS");
return;

int InvokeCore(Func<int> mark, Action log) =>
    (int)core.Invoke(player, new object[] { mark, log })!;

void SetStatus(byte status1, byte status2, byte status3) =>
    setStatus.Invoke(player, new object[] { status1, status2, status3 });

void AssertStateUnchanged(string scenario)
{
    Equal((byte)0xA5, (byte)status1Field.GetValue(player)!,
        scenario + " Status1");
    Equal((byte)0x5A, (byte)status2Field.GetValue(player)!,
        scenario + " Status2");
    Equal((byte)0x01, (byte)status3Field.GetValue(player)!,
        scenario + " HelpOther status");
    Equal(123, player.m_nGoldMax, scenario + " gold limit");
    Equal(77, player.m_nStorageSpaceCount, scenario + " storage limit");
    Equal(0, player.m_MsgList.Count, scenario + " client messages");
}

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

    var managerStart = manager.IndexOf("internal int MarkHelpOther(",
        StringComparison.Ordinal);
    var managerEnd = manager.IndexOf("private void EnsureSchema()",
        managerStart, StringComparison.Ordinal);
    Assert(managerStart >= 0 && managerEnd > managerStart,
        "MarkHelpOther manager block missing");
    var managerBlock = manager.Substring(managerStart, managerEnd - managerStart);
    Require(manager, "Select Idx, PlayerId, HelpOther from gamedata.AuthenticateUser",
        "native HelpOther SELECT missing");
    Require(manager, "where PlayerId = @playerId;",
        "native HelpOther PlayerId predicate missing");
    Require(manager, "Update gamedata.AuthenticateUser set HelpOther = 1 where Idx = @idx;",
        "native HelpOther UPDATE missing");
    Require(managerBlock, "MySqlDbType.Int64",
        "HelpOther PlayerId must remain 64-bit");
    Require(managerBlock, "if (!reader.Read())",
        "HelpOther no-row gate missing");
    Require(managerBlock, "if (helpOther == 1)",
        "HelpOther exact duplicate gate missing");
    Require(managerBlock, "return 2;", "HelpOther duplicate result missing");
    Require(managerBlock, "update.ExecuteNonQuery();",
        "HelpOther synchronous UPDATE missing");
    Require(managerBlock, "return -2;", "HelpOther update error missing");
    Reject(managerBlock, "BeginTransaction", "transaction added");
    Reject(managerBlock, "FOR UPDATE", "row lock added");
    Reject(managerBlock, "ON DUPLICATE KEY", "upsert added");
    Reject(managerBlock, "Retry", "retry added");
    Reject(managerBlock, "AffectedRows", "affected-row gate added");
    Reject(managerBlock, "ExecuteNonQuery() ==", "affected-row equality gate added");
    Reject(managerBlock, "ExecuteNonQuery() !=", "affected-row inequality gate added");
    Reject(managerBlock, "HelpOther = 0", "compensation update added");
    Reject(managerBlock, "playerId <= 0", "PlayerId content gate added");

    var playerStart = player.IndexOf("internal int HelpOtherNativeAuthentication()",
        StringComparison.Ordinal);
    Assert(playerStart >= 0, "HelpOther player entry missing");
    var playerBlock = player[playerStart..];
    Require(playerBlock, "if (result == 1)",
        "HelpOther success-only log gate missing");
    Require(player, "private readonly object _nativeAuthenticationSync = new();",
        "shared authentication lock missing");
    Equal(3, CountOccurrences(player, "lock (_nativeAuthenticationSync)"),
        "ActiveAuthen, ActiveDelAuthen and HelpOtherAuthen must share one player lock");
    Require(playerBlock, "M2Share.AddGameDataLog(\"94\\t\"",
        "HelpOther type 94 game log missing");
    Require(playerBlock, "\"\\t1\\t0\\t\" + NativeHelpOtherSuccess",
        "HelpOther type 94 fixed fields missing");
    Reject(playerBlock, "SysMsg(", "HelpOther must not send SysMsg");
    Reject(playerBlock, "ApplyNativeAuthenticationLimits",
        "HelpOther must not apply limits");
    Reject(playerBlock, "SendNativeAuthenticationStatus",
        "HelpOther must not send 4636");
    Reject(playerBlock, "_nativeAuthStatus",
        "HelpOther must not mutate authentication state");

    var methodAt = bridge.IndexOf("case \"helpotherauthen\":",
        StringComparison.OrdinalIgnoreCase);
    var authByHelpedAt = bridge.IndexOf("case \"authbyhelped\":",
        methodAt, StringComparison.OrdinalIgnoreCase);
    Assert(methodAt >= 0 && authByHelpedAt > methodAt,
        "HelpOther procedure fail-closed gate missing");
    Require(bridge.Substring(methodAt, authByHelpedAt - methodAt),
        "RejectUnsupportedNativeApi()",
        "HelpOther procedure form must remain fail-closed");

    var functionAt = bridge.LastIndexOf("case \"helpotherauthen\":",
        StringComparison.OrdinalIgnoreCase);
    var checkAt = bridge.IndexOf("case \"checkauthen\":",
        functionAt, StringComparison.OrdinalIgnoreCase);
    Assert(functionAt > methodAt && checkAt > functionAt,
        "HelpOther function gate missing");
    var functionGate = bridge.Substring(functionAt, checkAt - functionAt);
    Require(functionGate, "args.Count != 0",
        "HelpOther exact zero-argument gate missing");
    Require(functionGate, "RejectUnsupportedNativeApi(out result)",
        "HelpOther arguments must remain fail-closed");
    Require(functionGate, "PasValue.FromInt(",
        "HelpOther integer result mapping missing");
    Require(functionGate, "CurrentPlayer.HelpOtherNativeAuthentication()",
        "HelpOther native entry missing");
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
