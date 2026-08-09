using System.Text.RegularExpressions;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.ObjectManager = new ObjectManager();

var first = new TPlayObject();
var second = new TPlayObject();
var bridge = new PasApiBridge { CurrentPlayer = first };

Equal(string.Empty, first.m_sStrParam, "new player StrParam is not empty");
Assert(bridge.GetPlayerProperty("StrParam", out var value),
    "PAS StrParam getter was rejected");
Equal(string.Empty, value.AsString(), "PAS StrParam default is not empty");

first.m_nVal[0] = 11;
first.m_nInteger[0] = 22;
first.m_nSval[0] = "S-slot";
first.m_sString[0] = "I-slot";
first.m_ScriptVVars[1] = 33;
first.m_ScriptSVars[1] = 44;

const string firstValue = "1|warrior:-1";
Assert(bridge.SetPlayerProperty("strparam", PasValue.FromString(firstValue)),
    "PAS StrParam setter was rejected");
Equal(firstValue, first.m_sStrParam, "PAS StrParam setter did not write the player field");
Assert(bridge.GetPlayerProperty("STRPARAM", out value),
    "case-insensitive PAS StrParam getter was rejected");
Equal(firstValue, value.AsString(), "PAS StrParam did not round-trip");
Assert(first.m_nVal[0] == 11 && first.m_nInteger[0] == 22 &&
       first.m_nSval[0] == "S-slot" && first.m_sString[0] == "I-slot" &&
       first.m_ScriptVVars[1] == 33 && first.m_ScriptSVars[1] == 44,
    "StrParam was parsed into a generic V/S/I command slot");

bridge.CurrentPlayer = second;
Assert(bridge.GetPlayerProperty("strparam", out value),
    "second player StrParam getter was rejected");
Equal(string.Empty, value.AsString(), "StrParam leaked between player objects");
Assert(bridge.SetPlayerProperty("strparam", PasValue.FromString("2|skill")),
    "second player StrParam setter was rejected");
Equal(firstValue, first.m_sStrParam, "second player changed the first player's StrParam");
Equal("2|skill", second.m_sStrParam, "second player did not retain its own StrParam");

Assert(bridge.SetPlayerProperty("strparam", PasValue.FromString(string.Empty)),
    "empty StrParam setter was rejected");
Assert(bridge.GetPlayerProperty("strparam", out value),
    "empty StrParam getter was rejected");
Equal(string.Empty, value.AsString(), "empty StrParam did not round-trip");

TestSourceContracts();
Console.WriteLine("StrParamCompatCheck PASS default=empty scope=player-object PAS=direct no-store=V/S/I+DB");

static void TestSourceContracts()
{
    var root = FindRepositoryRoot();
    var playersDirectory = Path.Combine(root, "GameSvr", "Players");
    var fieldPath = Path.Combine(playersDirectory, "TPlayObject.NativeStrParam.cs");
    var bridgePath = Path.Combine(root, "GameSvr", "ScriptSystem", "PasEngine",
        "PasApiBridge.cs");
    var fieldSource = File.ReadAllText(fieldPath);
    var bridgeSource = File.ReadAllText(bridgePath);

    RequireMatches(fieldSource,
        @"public\s+string\s+m_sStrParam\s*=\s*string\.Empty\s*;", 1,
        "player StrParam field is not explicitly initialized to empty");
    RequireMatches(bridgeSource,
        @"case\s+""strparam""\s*:\s*result\s*=\s*PasValue\.FromString\(CurrentPlayer\.m_sStrParam\)\s*;",
        1, "PAS StrParam getter is not a direct player-field read");
    RequireMatches(bridgeSource,
        @"case\s+""strparam""\s*:\s*CurrentPlayer\.m_sStrParam\s*=\s*value\.AsString\(\)\s*;\s*return\s+true\s*;",
        1, "PAS StrParam setter is not a direct player-field write");

    var persistenceSources = new[]
    {
        Path.Combine(root, "GameSvr", "UsrSystem", "UsrEngn.cs"),
        Path.Combine(root, "GameSvr", "Players", "TPlayObject.cs"),
        Path.Combine(root, "DBSvr", "DB", "impl", "MySqlPlayDataService.cs")
    };
    foreach (var path in persistenceSources)
    {
        Assert(!File.ReadAllText(path).Contains("m_sStrParam", StringComparison.Ordinal),
            "StrParam was added to character/database persistence: " + Path.GetFileName(path));
    }
}

static string FindRepositoryRoot()
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        var directory = new DirectoryInfo(start);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameSvr", "GameSvr.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
    }
    throw new DirectoryNotFoundException(
        "Repository root containing GameSvr/GameSvr.csproj was not found.");
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

static void RequireMatches(string source, string pattern, int expected, string message)
{
    var actual = Regex.Matches(source, pattern,
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase).Count;
    if (actual != expected)
        throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
}

static void Equal(string expected, string actual, string message)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
        throw new InvalidOperationException($"{message}: expected '{expected}', actual '{actual}'");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
