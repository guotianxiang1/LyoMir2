using GameSvr;
using GameSvr.PasEngine;
using GameSvr.Services;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.UserEngine = new UserEngine();
M2Share.ObjectManager = new ObjectManager();
M2Share.MapManager = new MapManager();
M2Share.ProcessMsgCriticalSection = new object();
M2Share.ProcessHumanCriticalSection = new object();
M2Share.LogMsgCriticalSection = new object();
M2Share.LogStringList = new System.Collections.ArrayList();
M2Share.ServerSwitches = CreateEnabledSwitchStore();

var firstItem = new TUserItem { wIndex = 1, Dura = 97 };
var secondItem = new TUserItem { wIndex = 2, Dura = 98 };
var player = new TPlayObject { m_sCharName = "签到人物" };
player.m_ItemList.Add(firstItem);
player.m_ItemList.Add(secondItem);
var messageCount = player.m_MsgList.Count;
var bridge = new PasApiBridge { CurrentPlayer = player };

M2Share.SignActManager = null;
Assert(!bridge.CallPlayerFunc("SignIn", new List<PasValue>(), out _),
    "SignIn did not fail closed without the native manager");
Assert(!bridge.CallPlayerMethod("SignInDayAct", new List<PasValue>()),
    "SignInDayAct did not fail closed without the native manager");

var store = new FakeSignActStore();
var manager = new NativeSignActManager(store);
M2Share.SignActManager = manager;

Assert(M2Share.ServerSwitches.TrySetBit(2, 0x40, false,
        out _, out var switchError), "failed to close test bit 22: " + switchError);
Assert(bridge.CallPlayerFunc("SignIn", new List<PasValue>(),
        out var closedSignResult), "closed SignIn was not dispatched");
Assert(!closedSignResult.AsBool(), "closed bit-22 SignIn returned true");
Assert(!store.SignRows.ContainsKey("签到人物"),
    "closed bit-22 SignIn accessed the native table");
Assert(M2Share.ServerSwitches.TrySetBit(2, 0x40, true,
        out _, out switchError), "failed to open test bit 22: " + switchError);

Assert(!bridge.CallPlayerMethod("SignIn", new List<PasValue>()),
    "function-only SignIn was exposed as a method");
Assert(!bridge.CallPlayerMethod("GetSignInActPrize", new List<PasValue>()),
    "function-only GetSignInActPrize was exposed as a method");
Assert(!bridge.CallPlayerMethod("GetSignInDayActPrizer",
        new List<PasValue> { PasValue.FromInt(1) }),
    "daily winner function was exposed as a claim method");

Assert(bridge.CallPlayerFunc("SignIn", new List<PasValue>(), out var signResult),
    "SignIn function dispatch failed with the native manager");
Assert(signResult.AsBool(), "enabled SignIn returned false");
Equal(1, store.SignRows["签到人物"].SignCount,
    "SignIn did not insert native SignCnt=1");

store.SignRows["签到人物"].PrizeType = 1;
Assert(bridge.CallPlayerFunc("GetSignInActPrize", new List<PasValue>(),
        out var prizeResult), "GetSignInActPrize dispatch failed");
Equal(1, prizeResult.AsInt(), "GetSignInActPrize returned the wrong old tag");
Equal(3, store.SignRows["签到人物"].PrizeType,
    "GetSignInActPrize did not consume 1 -> 3");

Assert(bridge.CallPlayerMethod("SignInDayAct", new List<PasValue>()),
    "SignInDayAct procedure dispatch failed");
Equal("签到人物", store.LastEverydaySignIn,
    "SignInDayAct did not use the current character name");
Equal(1, store.EverydaySignInCalls,
    "direct SignInDayAct call count");

const string dispatchSource = """
    program SignActDispatchProbe;
    procedure FunctionAsProcedure;
    begin
      This_Player.SignIn;
    end;
    procedure MethodFallback;
    begin
      This_Player.SignInDayAct;
    end;
    begin
    end.
    """;
var dispatchProgram = new PasParser(
    new PasLexer(dispatchSource), FindRepositoryRoot()).Parse();
var dispatchInterpreter = new PasInterpreter(dispatchProgram, bridge);
dispatchInterpreter.ExecuteProcedure("FunctionAsProcedure");
Equal(2, store.SignRows["签到人物"].SignCount,
    "PAS procedure syntax did not dispatch the function-only SignIn");
dispatchInterpreter.ExecuteProcedure("MethodFallback");
Equal("签到人物", store.LastEverydaySignIn,
    "PAS procedure syntax did not fall back to method-only SignInDayAct");
Equal(2, store.EverydaySignInCalls,
    "PAS procedure syntax did not invoke SignInDayAct exactly once");

store.YesterdayTags.Add(2);
Assert(bridge.CallPlayerFunc("GetSignInDayActTag", new List<PasValue>(),
        out var dailyTag), "GetSignInDayActTag dispatch failed");
Equal(2, dailyTag.AsInt(), "GetSignInDayActTag result");

store.EverydayWinnerRows.Add(new NativeSignActEverydayRow(0, "每日一等奖", 1));
store.EverydayWinnerRows.Add(new NativeSignActEverydayRow(0, "每日乙", 2));
store.EverydayWinnerRows.Add(new NativeSignActEverydayRow(0, "每日丙", 2));
manager.ProcessEveryday(new DateTime(2026, 7, 20, 0, 0, 0));
Assert(bridge.CallPlayerFunc("GetSignInDayActPrizer",
        new List<PasValue> { PasValue.FromInt(1) }, out var primary),
    "primary daily winner dispatch failed");
Equal("每日一等奖", primary.AsString(), "primary daily winner");
Assert(bridge.CallPlayerFunc("GetSignInDayActPrizer",
        new List<PasValue> { PasValue.FromInt(255) }, out var secondary),
    "secondary daily winner dispatch failed");
Equal("每日乙, 每日丙", secondary.AsString(), "secondary daily winners");

store.AddSignRow(2, "幸运甲", 1, 2);
store.AddSignRow(3, "幸运乙", 1, 4);
var winnerArgs = new List<PasValue>
{
    PasValue.FromString("旧值一"),
    PasValue.FromString("旧值二")
};
Assert(bridge.CallPlayerFunc("GetSignInActPrizer", winnerArgs,
        out var signActPrimary),
    "ordinary GetSignInActPrizer dispatch failed");
Equal("签到人物", signActPrimary.AsString(),
    "ordinary GetSignInActPrizer primary winner");
Equal("幸运甲", winnerArgs[0].AsString(),
    "ordinary GetSignInActPrizer first var output");
Equal("幸运乙", winnerArgs[1].AsString(),
    "ordinary GetSignInActPrizer second var output");
Assert(!bridge.CallPlayerMethod("GetSignInActPrizer",
        new List<PasValue>
        {
            PasValue.FromString(string.Empty),
            PasValue.FromString(string.Empty)
        }), "GetSignInActPrizer was exposed as a method");

const string winnerSource = """
    program SignActWinnerProbe;
    function ReadWinners: string;
    var
      Lucky1, Lucky2: string;
    begin
      Result := This_Player.GetSignInActPrizer(Lucky1, Lucky2);
      if Lucky1 <> '幸运甲' then Result := 'bad lucky1';
      if Lucky2 <> '幸运乙' then Result := 'bad lucky2';
    end;
    function RejectConstants: string;
    begin
      Result := This_Player.GetSignInActPrizer('', '');
    end;
    begin
    end.
    """;
var winnerProgram = new PasParser(
    new PasLexer(winnerSource), FindRepositoryRoot()).Parse();
var winnerInterpreter = new PasInterpreter(winnerProgram, bridge);
Equal("签到人物", winnerInterpreter.ExecuteProcedure("ReadWinners").AsString(),
    "PAS var-string winner outputs were not copied back");
try
{
    winnerInterpreter.ExecuteProcedure("RejectConstants");
    throw new InvalidOperationException(
        "GetSignInActPrizer accepted non-variable output arguments");
}
catch (PasRuntimeException exception)
{
    Assert(exception.Message.Contains("argument 1 must be a variable",
            StringComparison.Ordinal),
        "GetSignInActPrizer rejected constants for the wrong reason");
}

Equal(2, player.m_ItemList.Count, "native SignAct changed the bag count");
Assert(ReferenceEquals(firstItem, player.m_ItemList[0]) &&
       ReferenceEquals(secondItem, player.m_ItemList[1]),
    "native SignAct replaced or reordered bag items");
Equal(messageCount, player.m_MsgList.Count,
    "native SignAct emitted a fabricated client message");

TestNativeCommand();
TestSourceContracts();
    Console.WriteLine(
        "PASS SignAct=runtime/native-tables PAS=5-open var-winner=open/exact " +
    "GM=exact/no-bit22-write daily=owner-loop/no-V30/no-message " +
    "reward=PAS-owned");
return;

static void TestNativeCommand()
{
    var player = new TPlayObject { m_sCharName = "命令人物" };
    var command = new SignInActCommand();

    Assert(M2Share.ServerSwitches.TrySetBit(2, 0x40, false,
            out _, out var switchError),
        "failed to close command-test bit 22: " + switchError);

    var openStore = new FakeSignActStore();
    openStore.AddSignRow(1, "旧签到", 7, 2);
    M2Share.SignActManager = new NativeSignActManager(openStore);
    command.SignInAct(new[] { "开启活动" }, player);
    Equal(0, openStore.SignRows["旧签到"].SignCount,
        "SignInAct open did not reset SignCnt");
    Equal(0, openStore.SignRows["旧签到"].PrizeType,
        "SignInAct open did not reset PrizeType");
    AssertCommandMessage(player, "清除数据成功", 0xDB, 0xFF,
        "SignInAct open success");
    Assert(!M2Share.ServerSwitches.IsBitSet(2, 0x40),
        "SignInAct open changed bit 22");

    var failedOpenStore = new FakeSignActStore { ResetResult = false };
    M2Share.SignActManager = new NativeSignActManager(failedOpenStore);
    command.SignInAct(new[] { "开启活动" }, player);
    AssertCommandMessage(player, "清除数据失败", 0xDB, 0xFF,
        "SignInAct open failure");

    var alreadyStore = new FakeSignActStore();
    alreadyStore.AddSignRow(1, "已开奖", 5, 1);
    M2Share.SignActManager = new NativeSignActManager(alreadyStore);
    command.SignInAct(new[] { "关闭活动" }, player);
    AssertCommandMessage(player, "已经开过奖了", 0xDB, 0xFF,
        "SignInAct already-drawn close");

    var existingQueryFailed = new FakeSignActStore
    {
        ExistingPrizeQueryCountOverride = -1
    };
    M2Share.SignActManager = new NativeSignActManager(existingQueryFailed);
    command.SignInAct(new[] { "关闭活动" }, player);
    AssertCommandMessage(player, "已经开过奖了", 0xDB, 0xFF,
        "SignInAct existing-prize SQL failure");

    var emptyStore = new FakeSignActStore();
    M2Share.SignActManager = new NativeSignActManager(emptyStore);
    command.SignInAct(new[] { "关闭活动" }, player);
    AssertCommandMessage(player, "没有人中奖", 0xDB, 0xFF,
        "SignInAct empty close");

    var candidateQueryFailed = new FakeSignActStore
    {
        DrawQueryCountOverride = -1
    };
    M2Share.SignActManager = new NativeSignActManager(candidateQueryFailed);
    command.SignInAct(new[] { "关闭活动" }, player);
    AssertCommandMessage(player, "成功开奖", 0xDB, 0xFF,
        "SignInAct candidate SQL failure");

    var successStore = CreateDrawStore();
    M2Share.SignActManager = new NativeSignActManager(successStore);
    command.SignInAct(new[] { "关闭活动" }, player);
    AssertCommandMessage(player, "成功开奖", 0xDB, 0xFF,
        "SignInAct successful close");

    var failedDrawStore = CreateDrawStore();
    failedDrawStore.FailPrizeUpdateCall = 2;
    M2Share.SignActManager = new NativeSignActManager(failedDrawStore);
    command.SignInAct(new[] { "关闭活动" }, player);
    AssertCommandMessage(player, "开奖更新sql失败", 0xDB, 0xFF,
        "SignInAct failed close");

    command.SignInAct(Array.Empty<string>(), player);
    AssertCommandMessage(player, "格式：SignInAct [开启活动,关闭活动]",
        0xFF, 0x38, "SignInAct usage");

    Assert(!M2Share.ServerSwitches.IsBitSet(2, 0x40),
        "SignInAct close changed bit 22");
    Assert(M2Share.ServerSwitches.TrySetBit(2, 0x40, true,
            out _, out switchError),
        "failed to restore command-test bit 22: " + switchError);
}

static FakeSignActStore CreateDrawStore()
{
    var store = new FakeSignActStore();
    store.AddSignRow(1, "候选甲", 5, 0);
    store.AddSignRow(2, "候选乙", 5, 0);
    store.AddSignRow(3, "候选丙", 5, 0);
    store.SignDrawCandidates.AddRange(store.SignRows.Values
        .Select(row => row.ToRow()));
    return store;
}

static void AssertCommandMessage(TPlayObject player, string body,
    int foreground, int background, string scenario)
{
    var messages = player.m_MsgList.Where(message =>
        message.wIdent == Grobal2.RM_SYSMESSAGE &&
        string.Equals(message.Buff, body, StringComparison.Ordinal)).ToArray();
    Equal(1, messages.Length, scenario + " message count");
    Equal(0, messages[0].wParam, scenario + " wParam");
    Equal(foreground, messages[0].nParam1, scenario + " foreground");
    Equal(background, messages[0].nParam2, scenario + " background");
    Equal(0, messages[0].nParam3, scenario + " nParam3");
    player.m_MsgList.Clear();
}

static void TestSourceContracts()
{
    var root = FindRepositoryRoot();
    var source = File.ReadAllText(Path.Combine(root, "GameSvr", "ScriptSystem",
        "PasEngine", "PasApiBridge.cs"));
    var interpreter = File.ReadAllText(Path.Combine(root, "GameSvr", "ScriptSystem",
        "PasEngine", "PasInterpreter.cs"));
    var startup = File.ReadAllText(Path.Combine(root, "GameSvr", "GameApp.cs"));
    var server = File.ReadAllText(Path.Combine(root, "GameSvr", "GameServer.cs"));
    var globals = File.ReadAllText(Path.Combine(root, "GameSvr", "M2Share.cs"));
    var command = File.ReadAllText(Path.Combine(root, "GameSvr", "Command",
        "Commands", "SignInActCommand.cs"));

    var methods = Slice(source, "public bool CallPlayerMethod",
        "public bool CallPlayerFunc");
    Require(ExtractCase(methods, "signin"),
        "return RejectUnsupportedNativeApi();",
        "function-only SignIn method dispatch");
    Require(ExtractCase(methods, "getsigninactprize"),
        "return RejectUnsupportedNativeApi();",
        "function-only GetSignInActPrize method dispatch");
    var daySign = ExtractCase(methods, "signindayact");
    Require(daySign, "M2Share.SignActManager.SignInEveryday(",
        "SignInDayAct manager dispatch");
    Reject(daySign, "DateTime.Now", "SignInDayAct local-date substitute");
    Reject(daySign, "ExecuteSqlNonQuery", "SignInDayAct bridge SQL");
    Reject(daySign, "SysMsg(", "SignInDayAct fabricated message");

    var functions = Slice(source, "public bool CallPlayerFunc",
        "public bool CallNpcMethod");
    Reject(functions, "case \"signindayact\"",
        "procedure-only SignInDayAct function dispatch");
    Require(ExtractCase(functions, "signin"),
        "M2Share.ServerSwitches.IsBitSet(2, 0x40)",
        "SignIn bit-22 gate");
    Require(ExtractCase(functions, "getsigninactprize"),
        "M2Share.SignActManager.Claim(", "SignIn claim manager dispatch");
    Require(ExtractCase(functions, "getsignindayactprizer"),
        "M2Share.SignActManager.GetEverydayWinners(",
        "daily winner manager dispatch");
    Require(ExtractCase(functions, "getsignindayacttag"),
        "M2Share.SignActManager.GetYesterdayPrizeTag(",
        "daily tag manager dispatch");
    foreach (var name in new[]
             {
                 "signin", "getsigninactprize", "getsignindayactprizer",
                 "getsignindayacttag"
             })
    {
        var nativeCase = ExtractCase(functions, name);
        Reject(nativeCase, "GetPlayerVar(", name + " V substitute");
        Reject(nativeCase, "SetPlayerVar(", name + " V mutation");
        Reject(nativeCase, "SysMsg(", name + " fabricated message");
    }

    var nativeWinner = ExtractCase(functions, "getsigninactprizer");
    Require(nativeWinner, "IsYanshenSignInTunnelCall(args)",
        "eye tunnel discriminator");
    Require(nativeWinner, "TryCallYanshenSignInTunnel(args, out result)",
        "explicit eye tunnel");
    Require(nativeWinner, "M2Share.SignActManager.GetWinners()",
        "ordinary native winner manager dispatch");
    Require(nativeWinner, "args[0] = PasValue.FromString(signActWinners.Lucky1)",
        "first native winner var output");
    Require(nativeWinner, "args[1] = PasValue.FromString(signActWinners.Lucky2)",
        "second native winner var output");
    Require(nativeWinner, "result = PasValue.FromString(signActWinners.Primary)",
        "native winner function result");
    var copyBack = Slice(interpreter,
        "private static void CopyBackBridgeArguments",
        "private static void WriteBackArgument");
    var winnerCopyBack = ExtractCase(copyBack, "getsigninactprizer");
    Require(winnerCopyBack, "WriteBackArgument(name, 0, references, args)",
        "first native winner interpreter writeback");
    Require(winnerCopyBack, "WriteBackArgument(name, 1, references, args)",
        "second native winner interpreter writeback");

    Require(startup, "var signActSchemasReady = signActStore.EnsureSchemas(",
        "startup schema lifecycle");
    Require(startup, "M2Share.SignActManager = new NativeSignActManager(signActStore);",
        "startup manager publication");
    var schemaCheck = RequiredIndex(startup,
        "var signActSchemasReady = signActStore.EnsureSchemas(",
        "startup schema check");
    var managerPublish = RequiredIndex(startup,
        "M2Share.SignActManager = new NativeSignActManager(signActStore);",
        "startup manager publication order");
    var schemaBranch = RequiredIndex(startup, "if (signActSchemasReady)",
        "startup schema branch");
    Assert(schemaCheck < managerPublish && managerPublish < schemaBranch,
        "schema failure incorrectly suppresses native manager construction");
    var process = RequiredIndex(server,
        "M2Share.SignActManager?.ProcessEveryday(DateTime.Now);",
        "daily owner-loop process");
    var startService = RequiredIndex(server, "public void StartService()",
        "owner-loop service startup");
    var startServiceEnd = RequiredIndex(server, "public void Stop()",
        "owner-loop service startup boundary");
    var startupPhaseTick = server.LastIndexOf(
        "_runTimeTick = HUtil32.GetTickCount();", startServiceEnd,
        StringComparison.Ordinal);
    Assert(startupPhaseTick > startService && startupPhaseTick < startServiceEnd,
        "daily second-phase tick was not reset at owner-loop startup");
    var phaseGate = RequiredIndex(server,
        "unchecked((uint)(currentTick - _runTimeTick)) >= 1000U",
        "native one-second second-phase gate");
    var phaseAdvance = RequiredIndex(server, "_runTimeTick = currentTick;",
        "native second-phase tick advance");
    Assert(phaseGate < phaseAdvance && phaseAdvance < process,
        "daily processing escaped the native one-second second phase");
    var enter = server.LastIndexOf(
        "HUtil32.EnterCriticalSection(M2Share.ProcessHumanCriticalSection);",
        process, StringComparison.Ordinal);
    var leave = server.IndexOf(
        "HUtil32.LeaveCriticalSection(M2Share.ProcessHumanCriticalSection);",
        process, StringComparison.Ordinal);
    Assert(enter < process && process < leave,
        "daily process escaped the GameState execution domain");
    var userStop = RequiredIndex(server, "M2Share.UserEngine?.Stop();",
        "shutdown UserEngine stop");
    var shutdownEnter = server.IndexOf(
        "HUtil32.EnterCriticalSection(M2Share.ProcessHumanCriticalSection);",
        userStop, StringComparison.Ordinal);
    Assert(shutdownEnter > userStop,
        "shutdown GameState enter must follow UserEngine stop/join");
    var managerRelease = server.IndexOf("M2Share.SignActManager = null;",
        shutdownEnter, StringComparison.Ordinal);
    var shutdownLeave = server.IndexOf(
        "HUtil32.LeaveCriticalSection(M2Share.ProcessHumanCriticalSection);",
        shutdownEnter, StringComparison.Ordinal);
    Assert(managerRelease > shutdownEnter && shutdownLeave > managerRelease,
        "shutdown manager release escaped the GameState execution domain");
    Require(interpreter,
        "_api.CallPlayerFunc(method, args, out result) || _api.CallPlayerMethod(method, args)",
        "PAS player function-first procedure fallback");
    Require(globals, "public static NativeSignActManager SignActManager = null;",
        "shared manager owner");

    Require(command, "[GameCommand(\"SignInAct\"", "native GM command");
    Require(command, "[开启活动|关闭活动]", "native GM help");
    foreach (var text in new[]
             {
                 "清除数据成功", "清除数据失败", "已经开过奖了",
                 "没有人中奖", "成功开奖", "开奖更新sql失败"
             })
        Require(command, text, "native GM result " + text);
    Reject(command, "TrySetBit", "SignInAct command must not change bit 22");
    Reject(command, "Give(", "SignInAct command fabricated reward");
}

static NativeServerSwitchStore CreateEnabledSwitchStore()
{
    var root = Path.Combine(AppContext.BaseDirectory, "SignActSwitch");
    var config = Path.Combine(root, "Config");
    Directory.CreateDirectory(config);
    File.WriteAllBytes(Path.Combine(config, "ServerSwitch.Bin"),
        new byte[] { 0, 0, 0x40, 0, 0 });
    Assert(NativeServerSwitchStore.TryLoad(root, out var store, out var error),
        "test switch load failed: " + error);
    return store;
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

static int RequiredIndex(string source, string value, string message)
{
    var index = source.IndexOf(value, StringComparison.Ordinal);
    Assert(index >= 0, message);
    return index;
}

static void Require(string source, string value, string message) =>
    Assert(source.Contains(value, StringComparison.Ordinal), message);

static void Reject(string source, string value, string message) =>
    Assert(!source.Contains(value, StringComparison.OrdinalIgnoreCase), message);

static void Equal<T>(T expected, T actual, string message) =>
    Assert(EqualityComparer<T>.Default.Equals(expected, actual),
        $"{message}: expected={expected}, actual={actual}");

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static string FindRepositoryRoot()
{
    return AuditRepoRoot.Resolve();
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

sealed class FakeSignActStore : INativeSignActStore
{
    public Dictionary<string, MutableSignRow> SignRows { get; } =
        new(StringComparer.Ordinal);
    public List<int> YesterdayTags { get; } = new();
    public List<NativeSignActRow> SignDrawCandidates { get; } = new();
    public List<NativeSignActEverydayRow> EverydayWinnerRows { get; } = new();
    public string LastEverydaySignIn { get; private set; } = string.Empty;
    public int EverydaySignInCalls { get; private set; }
    public bool ResetResult { get; set; } = true;
    public int FailPrizeUpdateCall { get; set; }
    public int? ExistingPrizeQueryCountOverride { get; set; }
    public int? DrawQueryCountOverride { get; set; }
    private int _prizeUpdateCalls;

    public bool EnsureSchemas(out string error)
    {
        error = string.Empty;
        return true;
    }

    public bool TryGetSignCountRow(string characterName,
        out NativeSignActRow row)
    {
        row = null;
        if (!SignRows.TryGetValue(characterName, out var found)) return false;
        row = found.ToRow();
        return true;
    }

    public bool TryGetSignPrizeRow(string characterName,
        out NativeSignActRow row)
    {
        row = null;
        if (!SignRows.TryGetValue(characterName, out var found)) return false;
        row = found.ToRow();
        return true;
    }

    public bool InsertSignAct(string characterName)
    {
        if (SignRows.ContainsKey(characterName)) return false;
        SignRows[characterName] = new MutableSignRow(
            SignRows.Count + 1, characterName, 1, 0);
        return true;
    }

    public bool UpdateSignCount(int index, int signCount)
    {
        SignRows.Values.Single(row => row.Index == index).SignCount = signCount;
        return true;
    }

    public bool ResetSignAct()
    {
        if (!ResetResult) return false;
        foreach (var row in SignRows.Values)
        {
            row.SignCount = 0;
            row.PrizeType = 0;
        }
        return true;
    }

    public int QueryExistingSignActPrizeCount() =>
        ExistingPrizeQueryCountOverride ??
        (SignRows.Values.Any(row => row.PrizeType > 0) ? 1 : 0);

    public IReadOnlyList<NativeSignActRow> SelectSignActDrawCandidates(
        out int queryCount)
    {
        queryCount = DrawQueryCountOverride ?? SignDrawCandidates.Count;
        return SignDrawCandidates;
    }

    public bool UpdateSignActPrizeType(int index, int prizeType)
    {
        _prizeUpdateCalls++;
        if (FailPrizeUpdateCall == _prizeUpdateCalls) return false;
        var row = SignRows.Values.SingleOrDefault(row => row.Index == index);
        if (row != null) row.PrizeType = prizeType;
        return true;
    }

    public IReadOnlyList<NativeSignActRow> SelectSignActWinners() =>
        SignRows.Values.Where(row => row.PrizeType > 0)
            .Select(row => row.ToRow()).ToArray();

    public bool ReplaceEverydaySignIn(string characterName)
    {
        LastEverydaySignIn = characterName;
        EverydaySignInCalls++;
        return true;
    }

    public IReadOnlyList<int> SelectYesterdayPrizeTags(string characterName) =>
        YesterdayTags;

    public IReadOnlyList<NativeSignActEverydayRow>
        SelectYesterdayEverydayWinners(out int queryCount)
    {
        queryCount = EverydayWinnerRows.Count;
        return EverydayWinnerRows;
    }

    public IReadOnlyList<NativeSignActEverydayRow>
        SelectYesterdayEverydayDrawCandidates() =>
        Array.Empty<NativeSignActEverydayRow>();

    public bool UpdateEverydayPrizeTag(int index, int prizeTag) => true;

    public void AddSignRow(int index, string name, int signCount, int prizeType)
    {
        SignRows[name] = new MutableSignRow(index, name, signCount, prizeType);
    }
}

sealed class MutableSignRow
{
    public MutableSignRow(int index, string name, int signCount, int prizeType)
    {
        Index = index;
        Name = name;
        SignCount = signCount;
        PrizeType = prizeType;
    }

    public int Index { get; }
    public string Name { get; }
    public int SignCount { get; set; }
    public int PrizeType { get; set; }
    public NativeSignActRow ToRow() =>
        new(Index, Name, SignCount, PrizeType);
}
