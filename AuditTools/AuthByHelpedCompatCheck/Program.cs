using System.Reflection;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.ObjectManager = new ObjectManager();
M2Share.LogMsgCriticalSection = new object();
M2Share.LogStringList = new System.Collections.ArrayList();

var player = new TPlayObject
{
    m_sMapName = "3",
    m_sCharName = "AuthByHelpedIsolationTest",
    m_nCurrX = 11,
    m_nCurrY = 22,
    m_nGoldMax = 1_234_567,
    m_nStorageSpaceCount = 77
};
var bridge = new PasApiBridge { CurrentPlayer = player };
var setStatus = GetMethod("SetNativeAuthenticationStatus",
    typeof(byte), typeof(byte), typeof(byte));
var status1Field = GetField("_nativeAuthStatus1");
var status2Field = GetField("_nativeAuthStatus2");
var status3Field = GetField("_nativeAuthStatus3");
setStatus.Invoke(player, new object[] { (byte)0xA5, (byte)0x5A, (byte)0x01 });

var panguLevels = Enumerable.Range(6212, 24)
    .Concat(new[] { 1400, 396, 397, 1401 })
    .ToArray();
Equal(28, panguLevels.Length, "Pangu physical call level count");
Equal(28, panguLevels.Distinct().Count(), "Pangu physical call levels must be unique");

foreach (var level in panguLevels)
{
    foreach (var value in new[] { 0, 1, 255 })
        AssertRejected(level, value, $"Pangu tuple ({level},{value})");
}

foreach (var level in new[] { 1, 2, 3 })
{
    foreach (var order in Enumerable.Range(0, 8).Append(100))
        AssertRejected(level, order, $"native tuple ({level},{order})");
}

VerifySourceContract();
Console.WriteLine("AuthByHelpedCompatCheck PASS");
return;

void AssertRejected(int level, int order, string scenario)
{
    var args = Args(level, order);
    Assert(!bridge.CallPlayerFunc("AuthByHelped", args, out var functionResult),
        scenario + " function surface escaped fail-closed");
    Equal(PasValueType.Nil, functionResult.Type,
        scenario + " rejected function result must remain Nil");
    Assert(!bridge.CallPlayerMethod("AuthByHelped", args),
        scenario + " method surface escaped fail-closed");
    AssertStateUnchanged(scenario);
}

void AssertStateUnchanged(string scenario)
{
    Equal((byte)0xA5, (byte)status1Field.GetValue(player)!, scenario + " Status1");
    Equal((byte)0x5A, (byte)status2Field.GetValue(player)!, scenario + " Status2");
    Equal((byte)0x01, (byte)status3Field.GetValue(player)!, scenario + " HelpOther status");
    Equal(1_234_567, player.m_nGoldMax, scenario + " gold limit");
    Equal(77, player.m_nStorageSpaceCount, scenario + " storage limit");
    Equal(0, player.m_MsgList.Count, scenario + " client message count");
    Equal(0, M2Share.LogStringList.Count, scenario + " game log count");
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
    var bridge = File.ReadAllText(Path.Combine(root,
        "GameSvr", "ScriptSystem", "PasEngine", "PasApiBridge.cs"));
    var manager = File.ReadAllText(Path.Combine(root,
        "GameSvr", "Services", "NativeAuthenticationManager.cs"));
    var player = File.ReadAllText(Path.Combine(root,
        "GameSvr", "Players", "TPlayObject.NativeAuthentication.cs"));

    var methodStart = bridge.IndexOf("public bool CallPlayerMethod(",
        StringComparison.Ordinal);
    var functionStart = bridge.IndexOf("public bool CallPlayerFunc(",
        StringComparison.Ordinal);
    Assert(methodStart >= 0 && functionStart > methodStart,
        "player method/function dispatch boundaries missing");

    var methodBlock = bridge.Substring(methodStart, functionStart - methodStart);
    Require(methodBlock, "case \"authbyhelped\":",
        "AuthByHelped method rejection case missing");
    var authMethodAt = methodBlock.IndexOf("case \"authbyhelped\":",
        StringComparison.OrdinalIgnoreCase);
    var nextCaseAt = methodBlock.IndexOf("case \"", authMethodAt + 6,
        StringComparison.OrdinalIgnoreCase);
    Assert(nextCaseAt > authMethodAt, "AuthByHelped method case boundary missing");
    Require(methodBlock.Substring(authMethodAt, nextCaseAt - authMethodAt),
        "RejectUnsupportedNativeApi()",
        "AuthByHelped method surface must remain fail-closed");

    var functionBlock = bridge[functionStart..];
    Reject(functionBlock, "case \"authbyhelped\":",
        "AuthByHelped function surface was opened without authoritative closure");
    Equal(1, CountOccurrences(bridge, "case \"authbyhelped\":"),
        "AuthByHelped must have only the method rejection case");
    Reject(manager, "ConsumeHelpOther",
        "help-row consume path was added without AuthByHelped closure");
    Reject(player, "AuthByHelpedNativeAuthentication",
        "player AuthByHelped entry was added without authoritative closure");
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

static void Require(string source, string value, string message) =>
    Assert(source.Contains(value, StringComparison.OrdinalIgnoreCase), message);

static void Reject(string source, string value, string message) =>
    Assert(!source.Contains(value, StringComparison.OrdinalIgnoreCase), message);

static int CountOccurrences(string source, string value)
{
    var count = 0;
    for (var index = 0; (index = source.IndexOf(value, index,
             StringComparison.OrdinalIgnoreCase)) >= 0; index += value.Length)
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
