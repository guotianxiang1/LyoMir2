using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

var root = FindRepositoryRoot();
var bridgeSource = File.ReadAllText(Path.Combine(root, "GameSvr",
    "ScriptSystem", "PasEngine", "PasApiBridge.cs"));

CheckSourceContracts(bridgeSource);
PrepareRuntimeConfig();
CheckRuntimeDispatcher();

Console.WriteLine(
    "NeedKeyBoxFailClosedCheck PASS APIs=OpenNeedKeyBox/OpenNeedKeyBox2 " +
    "procedure=explicit-player function-shadow=fail-closed invalid=fail-closed");
return;

static void CheckRuntimeDispatcher()
{
    M2Share.g_Config = new GameSvrConfig();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.UserEngine = new UserEngine();

    var explicitPlayer = new TPlayObject
    {
        m_sCharName = "NeedKeyBoxTester",
        m_nGold = 123456,
        m_nGameGold = 99,
        m_nCurrX = 11,
        m_nCurrY = 22,
        m_PEnvir = new Envirnoment { sMapName = "BOXTEST" }
    };
    explicitPlayer.m_ItemList.Add(new TUserItem
        { MakeIndex = 700001, wIndex = 1 });
    explicitPlayer.m_ScriptVVars[1001] = 55;
    explicitPlayer.m_ScriptSVars[1002] = 66;
    var sentinelPlayer = new TPlayObject
    {
        m_sCharName = "NeedKeyBoxSentinel",
        m_nGold = 765432,
        m_nGameGold = 88,
        m_PEnvir = new Envirnoment { sMapName = "SENTINEL" }
    };
    sentinelPlayer.m_ScriptVVars[1001] = 77;
    sentinelPlayer.m_ScriptSVars[1002] = 88;

    var api = new PasApiBridge
    {
        CurrentPlayer = sentinelPlayer,
        CurrentNpc = new NormNpc()
    };

    var explicitBefore = Snapshot.Capture(explicitPlayer);
    var sentinelBefore = Snapshot.Capture(sentinelPlayer);
    var args = new List<PasValue>
        { PasValue.FromObject(explicitPlayer) };

    foreach (var name in new[] { "OpenNeedKeyBox", "OpenNeedKeyBox2" })
    {
        Assert(!api.CallNpcFunc(name, args, out var functionResult),
            name + " function shadow returned success");
        Assert(functionResult.Equals(PasValue.Nil),
            name + " function result is not Nil");
        explicitBefore.AssertUnchanged(explicitPlayer, name + " function");
        sentinelBefore.AssertUnchanged(sentinelPlayer, name + " function");

        Assert(api.CallNpcMethod(name, args, out var methodResult),
            name + " procedure surface was not dispatched");
        Assert(methodResult.Equals(PasValue.Nil),
            name + " procedure result is not Nil");
        explicitBefore.AssertUnchanged(explicitPlayer, name + " procedure");
        sentinelBefore.AssertUnchanged(sentinelPlayer, name + " procedure");

        foreach (var invalid in new[]
                 {
                     new List<PasValue>(),
                     new List<PasValue>
                     {
                         PasValue.FromObject(explicitPlayer),
                         PasValue.FromInt(1)
                     },
                     new List<PasValue> { PasValue.FromInt(1) },
                     new List<PasValue> { PasValue.Nil },
                     new List<PasValue>
                         { PasValue.FromObject(new NormNpc()) }
                 })
        {
            Assert(!api.CallNpcMethod(name, invalid, out var invalidResult),
                name + " accepted an invalid procedure ABI");
            Assert(invalidResult.Equals(PasValue.Nil),
                name + " invalid procedure result is not Nil");
        }
    }
}

static void CheckSourceContracts(string source)
{
    var npcMethods = Slice(source, "public bool CallNpcMethod",
        "public bool CallNpcFunc");
    var npcFunctions = Slice(source, "public bool CallNpcFunc",
        "public bool CallStandaloneFunction");

    CheckCaseCount(source, "openneedkeybox", 2);
    CheckCaseCount(source, "openneedkeybox2", 2);
    var procedureBody = ExtractGroupedCases(npcMethods, "openneedkeybox",
        "openluckbox");
    foreach (var required in new[]
             {
                 "case \"openneedkeybox2\":", "args.Count != 1",
                 "PasValueType.Object", "is not TPlayObject",
                 "TryOpenNativeNeedKeyBox(true, out _)",
                 "TrySubmitNativeNeedKeyBoxYuanbao(", "CurrentNpc"
             })
    {
        Require(procedureBody, required,
            "NeedKeyBox procedure bridge is missing: " + required);
    }
    Assert(!procedureBody.Contains("RejectUnsupportedNativeApi",
            StringComparison.Ordinal),
        "NeedKeyBox procedure bridge remained fail-closed");
    CheckGroupedFailClosed(npcFunctions, "openneedkeybox", "openneedkeybox2",
        "openluckbox", "function");
}

static void CheckGroupedFailClosed(string region, string firstName,
    string secondName, string nextName, string surface)
{
    var body = ExtractGroupedCases(region, firstName, nextName);
    Require(body, $"case \"{secondName}\":",
        $"{secondName} {surface} surface is not grouped with {firstName}");
    Require(body, "return RejectUnsupportedNativeApi(out result);",
        $"{firstName}/{secondName} {surface} surface is not fail-closed");

    foreach (var forbidden in new[]
             {
                 "SendMsg(", "SendDefMessage(", "m_nGameGold", "m_nGold",
                 "AddItemToBag", "DelBagItem", "GotoLable_GiveItem",
                 "NativeYuanbao", "YBConsume", "SetPlayerVar(",
                 "m_ScriptVVars", "m_ScriptSVars"
             })
    {
        Assert(!body.Contains(forbidden, StringComparison.Ordinal),
            $"{firstName}/{secondName} {surface} surface acquired a local substitute: {forbidden}");
    }
}

static void CheckCaseCount(string source, string name, int expected)
{
    var count = Count(source, $"case \"{name}\":");
    Assert(count == expected,
        $"{name} case count mismatch: expected {expected}, actual {count}");
}

static string ExtractGroupedCases(string region, string firstName, string nextName)
{
    var marker = $"case \"{firstName}\":";
    var start = region.IndexOf(marker, StringComparison.Ordinal);
    Assert(start >= 0, "missing case: " + firstName);
    var next = region.IndexOf($"case \"{nextName}\":", start + marker.Length,
        StringComparison.Ordinal);
    Assert(next > start, "missing following case: " + nextName);
    return region[start..next];
}

static string Slice(string source, string startMarker, string endMarker)
{
    var start = source.IndexOf(startMarker, StringComparison.Ordinal);
    Assert(start >= 0, "missing marker: " + startMarker);
    var end = source.IndexOf(endMarker, start + startMarker.Length,
        StringComparison.Ordinal);
    Assert(end > start, "missing marker: " + endMarker);
    return source[start..end];
}

static int Count(string source, string value)
{
    var count = 0;
    for (var offset = 0; (offset = source.IndexOf(value, offset,
             StringComparison.Ordinal)) >= 0; offset += value.Length)
    {
        count++;
    }
    return count;
}

static void Require(string source, string value, string message)
{
    Assert(source.Contains(value, StringComparison.Ordinal), message);
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
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
    foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
    {
        for (var directory = new DirectoryInfo(start); directory != null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameSvr",
                    "GameSvr.csproj")))
            {
                return directory.FullName;
            }
        }
    }

    throw new DirectoryNotFoundException("repository root not found");
}

file sealed class Snapshot
{
    private readonly int _gold;
    private readonly int _gameGold;
    private readonly short _x;
    private readonly short _y;
    private readonly Envirnoment _environment;
    private readonly int _bagCount;
    private readonly int _v1001;
    private readonly int _s1002;

    private Snapshot(TPlayObject player)
    {
        _gold = player.m_nGold;
        _gameGold = player.m_nGameGold;
        _x = player.m_nCurrX;
        _y = player.m_nCurrY;
        _environment = player.m_PEnvir;
        _bagCount = player.m_ItemList.Count;
        _v1001 = player.m_ScriptVVars.TryGetValue(1001, out var v) ? v : 0;
        _s1002 = player.m_ScriptSVars.TryGetValue(1002, out var s) ? s : 0;
    }

    public static Snapshot Capture(TPlayObject player) => new Snapshot(player);

    public void AssertUnchanged(TPlayObject player, string context)
    {
        ProgramAssert(player.m_nGold == _gold, context + " changed gold");
        ProgramAssert(player.m_nGameGold == _gameGold, context + " changed yuanbao");
        ProgramAssert(player.m_nCurrX == _x && player.m_nCurrY == _y,
            context + " changed coordinates");
        ProgramAssert(ReferenceEquals(player.m_PEnvir, _environment),
            context + " changed environment");
        ProgramAssert(player.m_ItemList.Count == _bagCount,
            context + " changed bag item count");
        ProgramAssert(player.m_ScriptVVars.TryGetValue(1001, out var v) && v == _v1001,
            context + " changed V vars");
        ProgramAssert(player.m_ScriptSVars.TryGetValue(1002, out var s) && s == _s1002,
            context + " changed S vars");
    }

    private static void ProgramAssert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
