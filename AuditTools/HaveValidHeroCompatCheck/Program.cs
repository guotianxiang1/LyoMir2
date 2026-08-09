using System.Text.RegularExpressions;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.ObjectManager = new ObjectManager();

var player = new TPlayObject();
var bridge = new PasApiBridge { CurrentPlayer = player };
var interpreter = CreateInterpreter(bridge);

player.m_ScriptVVars[15001] = 1;
Verify(false, 0, null, "no native hero");

player.m_ScriptVVars[15001] = 0;
Verify(true, 1, null, "primary hero owned but not summoned");
Verify(true, 2, null, "secondary hero owned but not summoned");
Verify(false, 0, new HeroObject(), "runtime object without native presence bits");

player.m_btNativeHeroState = 1;
player.m_HeroObject = null;
player.m_ScriptVVars[15001] = 1;
Assert(ReadProperty().AsBool(), "V[15,1]=1 changed true native state");
player.m_ScriptVVars[15001] = 0;
Assert(ReadProperty().AsBool(), "V[15,1]=0 changed true native state");
player.m_btNativeHeroState = 0;
player.m_ScriptVVars[15001] = 1;
Assert(!ReadProperty().AsBool(), "V[15,1]=1 fabricated a native hero");
Assert(!bridge.CallPlayerFunc("HaveValidHero", new List<PasValue>(), out var fakeFunction),
    "HaveValidHero function dispatch still shadows the native property");
Equal(PasValueType.Nil, fakeFunction.Type,
    "rejected HaveValidHero function did not return Nil");

VerifySourceContract();

Console.WriteLine(
    "PASS HaveValidHero=NativeHeroState(bit0|bit1) member-read=function-fallback-to-property " +
    "runtime-object/V[15,1]=ignored");
return;

void Verify(bool expected, byte state, HeroObject hero, string scenario)
{
    player.m_btNativeHeroState = state;
    player.m_HeroObject = hero;

    var property = ReadProperty();
    Equal(PasValueType.Boolean, property.Type, scenario + " property type");
    Equal(expected, property.AsBool(), scenario + " direct property");

    var interpreted = interpreter.ExecuteProcedure("ProbeValue");
    Equal(expected, interpreted.AsBool(), scenario + " interpreter member read");
}

PasValue ReadProperty()
{
    Assert(bridge.GetPlayerProperty("HaveValidHero", out var value),
        "HaveValidHero property was not dispatched");
    return value;
}

static PasInterpreter CreateInterpreter(PasApiBridge bridge)
{
    const string source = """
        program HaveValidHeroProbe;
        function ProbeValue: Boolean;
        begin
          Result := This_Player.HaveValidHero;
        end;
        begin
        end.
        """;
    var program = new PasParser(new PasLexer(source), FindRepositoryRoot()).Parse();
    return new PasInterpreter(program, bridge);
}

static void VerifySourceContract()
{
    var root = FindRepositoryRoot();
    var path = Path.Combine(root, "GameSvr", "ScriptSystem", "PasEngine",
        "PasApiBridge.cs");
    var source = File.ReadAllText(path);
    var properties = Slice(source, "public bool GetPlayerProperty",
        "public bool SetPlayerProperty");
    var functions = Slice(source, "public bool CallPlayerFunc",
        "public bool CallNpcMethod");

    Equal(1, Count(properties, "case \"havevalidhero\":"),
        "HaveValidHero property dispatch count");
    Equal(0, Count(functions, "case \"havevalidhero\":"),
        "HaveValidHero function dispatch count");
    Require(properties,
        @"case\s+""havevalidhero""\s*:\s*result\s*=\s*PasValue\.FromBool\(\s*" +
        @"\(CurrentPlayer\.m_btNativeHeroState\s*&\s*(?:3|0x0?3)\)\s*!=\s*0\s*\)",
        "HaveValidHero native state-bit property");

    var marker = properties.IndexOf("case \"havevalidhero\":",
        StringComparison.Ordinal);
    var terminator = properties.IndexOf("break;", marker, StringComparison.Ordinal);
    Assert(marker >= 0 && terminator > marker,
        "HaveValidHero property case body was not found");
    var body = properties.Substring(marker, terminator - marker);
    foreach (var forbidden in new[]
             {
                 "m_HeroObject", "GetPlayerVar", "SetPlayerVar", "m_ScriptVVars",
                 "m_ScriptSVars"
             })
        Assert(!body.Contains(forbidden, StringComparison.Ordinal),
            "HaveValidHero property retains a non-native substitute: " + forbidden);
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

static int Count(string source, string value)
{
    var count = 0;
    for (var offset = 0;;)
    {
        var index = source.IndexOf(value, offset, StringComparison.Ordinal);
        if (index < 0) return count;
        count++;
        offset = index + value.Length;
    }
}

static void Require(string source, string pattern, string message) =>
    Assert(Regex.IsMatch(source, pattern,
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase), message + " missing");

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
        "repository root containing GameSvr/GameSvr.csproj was not found");
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
