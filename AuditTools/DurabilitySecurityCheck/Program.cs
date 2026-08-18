using System.Reflection;
using DBSvr;
using GameSvr;
using SystemModule;

var root = args.Length > 0 ? Path.GetFullPath(args[0]) : FindRepositoryRoot();
if (root == null)
{
    Console.Error.WriteLine("INCOMPLETE: repository root was not supplied and could "
        + "not be located from the working directory. "
        + "Usage: DurabilitySecurityCheck [repository root]");
    return 2;
}

var failures = new List<string>();

Run("debug builds do not grant GM permission", CheckCommandPermission);
Run("robot creation GM verb stays unregistered", CheckRobotCommand);
Run("save queue keeps only the newest immutable snapshot", CheckSaveCoalescing);
Run("old save attempts cannot remove a newer snapshot", CheckSaveAttemptCompletionGuard);
Run("native exit save-mode selector", CheckNativeExitSaveMode);
Run("offline gold requests remain queued and ordered", CheckGoldQueue);
Run("DB internal record bypass accepts only the native sentinel", CheckInternalRequest);

if (failures.Count != 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine("Durability/security regression checks passed.");
return 0;

void Run(string name, Action check)
{
    try
    {
        check();
        Console.WriteLine("PASS " + name);
    }
    catch (Exception ex)
    {
        failures.Add("FAIL " + name + ": " + ex.Message);
    }
}

void CheckCommandPermission()
{
    var source = Read("GameSvr", "Command", "BaseCommond.cs");
    Check(!source.Contains("#if DEBUG", StringComparison.Ordinal),
        "BaseCommond contains a DEBUG permission branch");
    Check(!source.Contains("m_btPermission = 10", StringComparison.Ordinal),
        "BaseCommond still promotes ordinary players");
}

void CheckRobotCommand()
{
    // @AddRebotsPlay 不在原生 430 行 GM 注册表里（0x007B4654 起，stride 0x120，全镜像
    // GBK/长度前缀/NUL/UTF-16LE 四种形式 0 命中），原版根本没有这个刷机器人的 GM 动词。
    // 与其校验「上限够不够严」，不如钉死它不存在——上限再严也是自造的攻击面。
    foreach (var path in Directory.GetFiles(Path.Combine(root, "GameSvr"), "*.cs",
                 SearchOption.AllDirectories))
    {
        Check(!File.ReadAllText(path).Contains("[GameCommand(\"AddRebotsPlay\"",
                  StringComparison.OrdinalIgnoreCase),
            "AddRebotsPlay is absent from the native registry and must not be registered: " + path);
    }
}

void CheckSaveCoalescing()
{
    var engine = new TFrontEngine();
    var first = Save("Account", "Role", retry: 3, nextRetry: 1234, lastLog: 5678);
    var latest = Save("account", "role", retry: 0, nextRetry: 0, lastLog: 0);
    engine.AddToSaveRcdList(first);
    engine.AddToSaveRcdList(latest);

    var queue = SaveQueue(engine);
    Equal(1, queue.Count, "same account/role was not coalesced");
    Same(latest, queue[0], "latest snapshot did not replace the old snapshot");
    Equal(3, latest.nReTryCount, "retry count was not inherited");
    Equal(1234, latest.NextRetryTick, "retry deadline was not inherited");
    Equal(5678, latest.LastErrorLogTick, "log throttle was not inherited");
    Check(latest.Generation > first.Generation, "snapshot generation did not advance");

    engine.AddToSaveRcdList(Save("OtherAccount", "Role", 0, 0, 0));
    Equal(2, SaveQueue(engine).Count, "different accounts were incorrectly merged");

    var lanes = new TFrontEngine();
    var ordinary = Save("Account", "Role", 0, 0, 0, 0);
    var switching = Save("account", "role", 0, 0, 0, 2);
    lanes.AddToSaveRcdList(ordinary);
    lanes.AddToSaveRcdList(switching);
    Equal(2, SaveQueue(lanes).Count,
        "mode2 switch snapshot was coalesced with an ordinary save");
    var latestOrdinary = Save("ACCOUNT", "ROLE", 0, 0, 0, 3);
    lanes.AddToSaveRcdList(latestOrdinary);
    Equal(2, SaveQueue(lanes).Count,
        "ordinary lane no longer coalesces independently of mode2");
    Check(SaveQueue(lanes).Contains(switching),
        "ordinary save replaced the pending mode2 snapshot");
}

void CheckSaveAttemptCompletionGuard()
{
    var source = Read("GameSvr", "Services", "FrnEngn.cs");
    Check(source.Contains("if (m_SaveRcdList[j] == SaveRcd)", StringComparison.Ordinal),
        "save-attempt removal is not tied to the attempted snapshot instance");
    Check(source.Contains("ReferenceEquals(current, SaveRcd)", StringComparison.Ordinal),
        "in-flight failure does not distinguish a replacement snapshot");
    Check(source.Contains("SameSaveKey(existing, SaveRcd)", StringComparison.Ordinal),
        "save coalescing no longer uses account and character");
    Check(source.Contains("SaveRcd.NativeSaveMode == 2", StringComparison.Ordinal),
        "switch completion is not gated on the mode2 save attempt");
}

void CheckNativeExitSaveMode()
{
    var selector = typeof(UserEngine).GetMethod("SelectNativeExitSaveMode",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException(typeof(UserEngine).FullName,
            "SelectNativeExitSaveMode");

    Equal((ushort)3, Select(false, false), "ordinary exit mode");
    Equal((ushort)1, Select(false, true), "reconnection exit mode");
    Equal((ushort)2, Select(true, false), "switch exit mode");
    Equal((ushort)2, Select(true, true),
        "switch must take priority over reconnection");

    ushort Select(bool switching, bool reconnecting)
    {
        var player = (TPlayObject)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(TPlayObject));
        player.m_boSwitchData = switching;
        player.m_boReconnection = reconnecting;
        return (ushort)(selector.Invoke(null, new object[] { player })
            ?? throw new InvalidOperationException("save-mode selector returned null"));
    }
}

void CheckGoldQueue()
{
    var source = Read("GameSvr", "Services", "FrnEngn.cs");
    Check(!source.Contains("m_ChangeGoldList.Clear()", StringComparison.Ordinal),
        "offline gold queue is cleared before DB confirmation");
    Check(source.Contains("changeResult != GoldChangeResult.Retry", StringComparison.Ordinal),
        "offline gold failures are not retained");
    Check(source.Contains("ReferenceEquals(m_ChangeGoldList[j], GoldChangeInfo)",
        StringComparison.Ordinal), "gold completion can remove another request");
    Check(source.Contains("attemptedUsers.Add", StringComparison.Ordinal),
        "same-character gold requests can overtake a failed predecessor");
}

void CheckInternalRequest()
{
    var method = typeof(GameSocService).GetMethod("IsInternalRecordRequest",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException(typeof(GameSocService).FullName,
            "IsInternalRecordRequest");

    Check(Invoke(new LoadHumDataPacket
    {
        sAccount = "1", sChrName = "Role", sUserAddr = "1", nSessionID = 1
    }), "native internal sentinel was rejected");
    Check(!Invoke(new LoadHumDataPacket
    {
        sAccount = "1", sChrName = "Role", sUserAddr = "127.0.0.1", nSessionID = 1
    }), "non-sentinel IP bypassed session validation");
    Check(!Invoke(new LoadHumDataPacket
    {
        sAccount = "player", sChrName = "Role", sUserAddr = "1", nSessionID = 1
    }), "ordinary account bypassed session validation");
    Check(!Invoke(new LoadHumDataPacket
    {
        sAccount = "1", sChrName = "", sUserAddr = "1", nSessionID = 1
    }), "empty character bypassed session validation");

    bool Invoke(LoadHumDataPacket packet) => (bool)method.Invoke(null, new object[] { packet })!;
}

string Read(params string[] parts) => File.ReadAllText(
    Path.Combine(new[] { root }.Concat(parts).ToArray()));

static TSaveRcd Save(string account, string role, int retry, int nextRetry, int lastLog,
    ushort mode = 0) =>
    new()
    {
        sAccount = account,
        sChrName = role,
        NativeSaveMode = mode,
        nReTryCount = retry,
        NextRetryTick = nextRetry,
        LastErrorLogTick = lastLog
    };

static IList<TSaveRcd> SaveQueue(TFrontEngine engine)
{
    var field = typeof(TFrontEngine).GetField("m_SaveRcdList",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new MissingFieldException(typeof(TFrontEngine).FullName, "m_SaveRcdList");
    return (IList<TSaveRcd>)(field.GetValue(engine)
        ?? throw new InvalidOperationException("save queue is null"));
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual, string message) where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
}

static void Same(object expected, object actual, string message)
{
    if (!ReferenceEquals(expected, actual))
        throw new InvalidOperationException(message);
}

// run_audits.py invokes every audit with no arguments, so a tool that hard-requires
// its repository root reported FAIL without evaluating a single assertion. Falling
// back to the enclosing checkout keeps the assertions exactly as they were and only
// removes the "never ran" outcome.
static string FindRepositoryRoot()
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        var current = new DirectoryInfo(start);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "GameSvr", "GameSvr.csproj")))
                return current.FullName;
            current = current.Parent;
        }
    }
    return null;
}
