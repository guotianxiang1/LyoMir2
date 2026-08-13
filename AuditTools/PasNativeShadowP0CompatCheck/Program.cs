using System.Text.RegularExpressions;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.UserEngine = new UserEngine();
M2Share.ObjectManager = new ObjectManager();
M2Share.MapManager = new MapManager();

var player = new TPlayObject();
player.m_ScriptVVars[30004] = 41;
player.m_ScriptVVars[30002] = 42;
player.m_ScriptVVars[22001] = 43;
player.m_ScriptVVars[22011] = 44;
player.m_ScriptSVars[30004] = 45;
var beforeV = player.m_ScriptVVars.OrderBy(item => item.Key).ToArray();
var beforeS = player.m_ScriptSVars.OrderBy(item => item.Key).ToArray();
var bridge = new PasApiBridge { CurrentPlayer = player };

CheckClosedPropertyRead(bridge, "PlatLv");
CheckClosedPropertyRead(bridge, "MyExpQuestValue");
Assert(!bridge.SetPlayerProperty("PlatLv", PasValue.FromInt(9)),
    "PlatLv write did not fail closed");
Assert(!bridge.SetPlayerProperty("MyExpQuestValue", PasValue.FromInt(9)),
    "read-only MyExpQuestValue acquired a write path");
CheckClosedFunction(bridge, "MyExpQuestValue", new List<PasValue>());
CheckClosedFunction(bridge, "HaveStudySSKSkill",
    new List<PasValue> { PasValue.FromString("audit-skill"), PasValue.FromBool(false) });
CheckClosedFunction(bridge, "AddSSKSkillExp",
    new List<PasValue>
    {
        PasValue.FromString("audit-skill"), PasValue.FromInt(50),
        PasValue.FromBool(false)
    });

foreach (var name in new[]
         {
             "PlatLv", "MyExpQuestValue", "HaveStudySSKSkill",
             "AddSSKSkillExp"
         })
{
    Assert(!bridge.CallPlayerMethod(name, new List<PasValue>()),
        name + " acquired a player-method dispatch");
}

Assert(beforeV.SequenceEqual(player.m_ScriptVVars.OrderBy(item => item.Key)),
    "a fail-closed P0 API changed V variables");
Assert(beforeS.SequenceEqual(player.m_ScriptSVars.OrderBy(item => item.Key)),
    "a fail-closed P0 API changed S variables");

var root = FindRepositoryRoot();
var source = File.ReadAllText(Path.Combine(root, "GameSvr", "ScriptSystem",
    "PasEngine", "PasApiBridge.cs"));
var propertyReads = Slice(source, "public bool GetPlayerProperty",
    "public bool SetPlayerProperty");
var propertyWrites = Slice(source, "public bool SetPlayerProperty",
    "public bool GetNpcProperty");
var methods = Slice(source, "public bool CallPlayerMethod",
    "public bool CallPlayerFunc");
var functions = Slice(source, "public bool CallPlayerFunc",
    "public bool CallNpcMethod");

CheckFailClosed(propertyReads, "platlv", true);
CheckFailClosed(propertyReads, "myexpquestvalue", true);
CheckFailClosed(propertyWrites, "platlv", false);
Assert(!propertyWrites.Contains("case \"myexpquestvalue\":",
        StringComparison.Ordinal),
    "read-only MyExpQuestValue acquired a property setter");

CheckFailClosed(functions, "myexpquestvalue", true);
CheckFailClosed(functions, "havestudysskskill", true);
CheckFailClosed(functions, "addsskskillexp", true);
Assert(!functions.Contains("case \"platlv\":", StringComparison.Ordinal),
    "PlatLv acquired a legacy function alias");

foreach (var name in new[]
         {
             "platlv", "myexpquestvalue", "havestudysskskill",
             "addsskskillexp"
         })
{
    Assert(!methods.Contains($"case \"{name}\":", StringComparison.Ordinal),
        name + " acquired a method ABI");
}

CheckCaseCount(source, "platlv", 2);
CheckCaseCount(source, "myexpquestvalue", 2);
CheckCaseCount(source, "havestudysskskill", 1);
CheckCaseCount(source, "addsskskillexp", 1);

CheckGenericVariableRoutes(methods, functions);

Console.WriteLine(
    "PASS P0 native shadows=closed PlatLv=property-RW " +
    "MyExpQuestValue=property-RO SSK=functions V/S=unchanged generic-V/S=present");
return;

static void CheckClosedPropertyRead(PasApiBridge bridge, string name)
{
    Assert(!bridge.GetPlayerProperty(name, out var result),
        name + " property read did not fail closed");
    AssertNil(result, name + " property read");
}

static void CheckClosedFunction(PasApiBridge bridge, string name,
    List<PasValue> args)
{
    Assert(!bridge.CallPlayerFunc(name, args, out var result),
        name + " function did not fail closed");
    AssertNil(result, name + " function");
}

static void CheckFailClosed(string region, string name, bool hasResult)
{
    var body = ExtractCase(region, name);
    var expected = hasResult
        ? "return RejectUnsupportedNativeApi(out result);"
        : "return RejectUnsupportedNativeApi();";
    Require(body, expected, name + " is not explicitly fail closed");

    foreach (var forbidden in new[]
             {
                 "GetPlayerVar(", "SetPlayerVar(", "m_ScriptVVars",
                 "m_ScriptSVars"
             })
    {
        Assert(!body.Contains(forbidden, StringComparison.Ordinal),
            name + " still touches V/S storage: " + forbidden);
    }
}

static void CheckGenericVariableRoutes(string methods, string functions)
{
    foreach (var name in new[] { "setv", "sets" })
    {
        var body = ExtractCase(methods, name);
        Require(body, "SetPlayerVar(", name + " method route was removed");
    }

    foreach (var name in new[] { "getv", "setv", "gets", "sets" })
    {
        var body = ExtractCase(functions, name);
        var helper = name.StartsWith("get", StringComparison.Ordinal)
            ? "GetPlayerVar(" : "SetPlayerVar(";
        Require(body, helper, name + " function route was removed");
    }
}

static void CheckCaseCount(string source, string name, int expected)
{
    var count = Regex.Matches(source, $"case \\\"{Regex.Escape(name)}\\\":",
        RegexOptions.CultureInvariant).Count;
    Assert(count == expected,
        $"{name} case count mismatch: expected {expected}, actual {count}");
}

static string ExtractCase(string region, string name)
{
    var marker = $"case \"{name}\":";
    var start = region.IndexOf(marker, StringComparison.Ordinal);
    Assert(start >= 0, "missing case: " + name);
    var next = region.IndexOf("case \"", start + marker.Length,
        StringComparison.Ordinal);
    return next < 0 ? region[start..] : region[start..next];
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

static void Require(string source, string value, string message) =>
    Assert(source.Contains(value, StringComparison.Ordinal), message);

static void AssertNil(PasValue value, string message) =>
    Assert(value.Type == PasValueType.Nil, message + " did not return Nil");

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static string FindRepositoryRoot()
{
    // args[0], reached through GetCommandLineArgs because this is a static local
    // function. It must outrank the working directory: the default tree below is
    // not always sitting on the branch under test.
    var cli = Environment.GetCommandLineArgs();
    foreach (var start in new[]
             {
                 cli.Length > 1 ? cli[1] : null,
                 Environment.CurrentDirectory,
                 AppContext.BaseDirectory,
                 @"D:\loym2\LyoMir2-master"
             })
    {
        if (string.IsNullOrWhiteSpace(start))
            continue;
        var current = new DirectoryInfo(start);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName,
                    "GameSvr", "GameSvr.csproj")))
                return current.FullName;
            current = current.Parent;
        }
    }

    throw new DirectoryNotFoundException("GameSvr repository root not found");
}

static void PrepareRuntimeConfig()
{
    var runtimeDirectory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(runtimeDirectory, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
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
