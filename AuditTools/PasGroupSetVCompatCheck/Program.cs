using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.UserEngine = new UserEngine();
M2Share.ObjectManager = new ObjectManager();
M2Share.MapManager = new MapManager();

var leader = NewPlayer("leader");
var member = NewPlayer("member");
var outsider = NewPlayer("outsider");
leader.m_GroupOwner = leader;
leader.m_GroupMembers.Add(leader);
leader.m_GroupMembers.Add(member);
member.m_GroupOwner = leader;

var bridge = new PasApiBridge { CurrentPlayer = member };

Assert(bridge.CallPlayerMethod("GroupSetV", Values(22, 3, 50)),
    "GroupSetV player-method route was not dispatched");
AssertValue(leader, 22003, 50, "player-method leader");
AssertValue(member, 22003, 50, "player-method member");
AssertMissing(outsider, 22003, "player-method outsider");

Assert(bridge.CallPlayerFunc("GroupSetV", Values(22, 3, 0), out var functionResult),
    "GroupSetV player-function route was not dispatched");
Assert(functionResult.AsBool(), "GroupSetV player-function returned False");
AssertMissing(leader, 22003, "player-function leader zero removal");
AssertMissing(member, 22003, "player-function member zero removal");

bridge.CurrentPlayer = leader;
Assert(bridge.CallStandaloneFunction("GroupSetV", Values(26, 4, 1),
        out var standaloneResult),
    "GroupSetV standalone route was not dispatched");
Assert(standaloneResult.AsBool(), "GroupSetV standalone returned False");
AssertValue(leader, 26004, 1, "standalone leader");
AssertValue(member, 26004, 1, "standalone member");
AssertMissing(outsider, 26004, "standalone outsider");

var solo = NewPlayer("solo");
bridge.CurrentPlayer = solo;
Assert(bridge.CallPlayerFunc("GroupSetV", Values(10, 2, 7), out var soloResult),
    "solo GroupSetV player-function route was not dispatched");
Assert(soloResult.AsBool(), "solo GroupSetV player-function returned False");
AssertValue(solo, 10002, 7, "solo compatibility fallback");

const string interpreterSource = """
    program GroupSetVProbe;
    procedure GroupWrite;
    begin
      GroupSetV(31, 6, 77);
    end;
    procedure GroupClear;
    begin
      GroupSetV(31, 6, 0);
    end;
    procedure SoloWrite;
    begin
      GroupSetV(32, 7, 9);
    end;
    begin
    end.
    """;
var interpreterProgram = new PasParser(new PasLexer(interpreterSource)).Parse();
var interpreter = new PasInterpreter(interpreterProgram, bridge);

bridge.CurrentPlayer = member;
interpreter.ExecuteProcedure("GroupWrite");
AssertValue(leader, 31006, 77, "interpreter group leader");
AssertValue(member, 31006, 77, "interpreter group member");
AssertMissing(outsider, 31006, "interpreter group outsider");
interpreter.ExecuteProcedure("GroupClear");
AssertMissing(leader, 31006, "interpreter group leader zero removal");
AssertMissing(member, 31006, "interpreter group member zero removal");

bridge.CurrentPlayer = solo;
interpreter.ExecuteProcedure("SoloWrite");
AssertValue(solo, 32007, 9, "interpreter solo fallback");
AssertMissing(outsider, 32007, "interpreter solo outsider");

Console.WriteLine("PASS GroupSetV routes=method/function/bridge-standalone/interpreter group=leader+member solo=fallback");
return;

static TPlayObject NewPlayer(string name) => new()
{
    m_boOffLineFlag = true,
    m_sCharName = name,
    m_sMapName = "audit-map",
    m_nCurrX = 12,
    m_nCurrY = 34
};

static List<PasValue> Values(params int[] values) => values
    .Select(PasValue.FromInt)
    .ToList();

static void AssertValue(TPlayObject player, int key, int expected, string operation)
{
    Assert(player.m_ScriptVVars.TryGetValue(key, out var actual),
        operation + " did not write the V variable");
    Assert(actual == expected,
        $"{operation} wrote {actual}, expected {expected}");
}

static void AssertMissing(TPlayObject player, int key, string operation) =>
    Assert(!player.m_ScriptVVars.ContainsKey(key),
        operation + " unexpectedly changed the V variable");

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
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
