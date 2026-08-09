using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.ObjectManager = new ObjectManager();

var player = new TPlayObject
{
    m_nGold = 101,
    m_nGameGold = 202,
    m_nGamePoint = 303,
    m_nPayMentPoint = 404,
    m_nShengWan = 505
};
player.m_ScriptVVars[91001] = 611;
player.m_ScriptSVars[91001] = 722;
var bagItem = new TUserItem { MakeIndex = 811, wIndex = 812, Dura = 813, DuraMax = 814 };
var bodyItem = new TUserItem { MakeIndex = 821, wIndex = 822, Dura = 823, DuraMax = 824 };
player.m_ItemList.Add(bagItem);
player.m_UseItems[0] = bodyItem;

var npc = new NormNpc();
var bridge = new PasApiBridge { CurrentPlayer = player, CurrentNpc = npc };
var before = Snapshot.Capture(player);

Assert(!bridge.GetPlayerProperty("HaveTimeNum", out var haveTime),
    "HaveTimeNum exposed a synthetic account-time balance");
AssertNil(haveTime, "HaveTimeNum");
Assert(!bridge.CallPlayerFunc("GetVitalityValue", Values(1), out var vitality),
    "GetVitalityValue exposed a synthetic vitality pool");
AssertNil(vitality, "GetVitalityValue");
Assert(!bridge.CallNpcFunc("GivePositiveVValue", Values(1, 1, player),
        out var positiveVitality),
    "GivePositiveVValue exposed an incomplete vitality mutation");
AssertNil(positiveVitality, "GivePositiveVValue");
Assert(!bridge.CallPlayerFunc("NewBieGiftConsume", Values(), out var newbie),
    "NewBieGiftConsume bypassed its asynchronous account request");
AssertNil(newbie, "NewBieGiftConsume");
Assert(!bridge.CallNpcMethod("ReqUseTimeBuyLF", Values(player, 10), out var timeBuy),
    "ReqUseTimeBuyLF credited a local substitute balance");
AssertNil(timeBuy, "ReqUseTimeBuyLF");
Assert(!bridge.CallPlayerFunc("QueryAwardCode", Values(""), out var fakeQuery),
    "QueryAwardCode fake function still shadows its native procedure");
AssertNil(fakeQuery, "QueryAwardCode function surface");
Assert(bridge.CallPlayerMethod("QueryAwardCode", Values("CODE-001")),
    "QueryAwardCode native asynchronous procedure is not open");
Assert(bridge.CallPlayerMethod("SetAwardCodeActiveParam",
        Values("CODE%_001", -2)),
    "SetAwardCodeActiveParam native asynchronous procedure is not open");
before.AssertUnchanged(player, "timed/account and async award-code dispatch");

var repositoryRoot = FindRepositoryRoot();
var bridgeSource = File.ReadAllText(Path.Combine(repositoryRoot,
    "GameSvr", "ScriptSystem", "PasEngine", "PasApiBridge.cs"));
var playerProperties = Slice(bridgeSource, "public bool GetPlayerProperty", "public bool SetPlayerProperty");
var playerMethods = Slice(bridgeSource, "public bool CallPlayerMethod", "public bool CallPlayerFunc");
var playerFunctions = Slice(bridgeSource, "public bool CallPlayerFunc", "public bool CallNpcMethod");
var npcMethods = Slice(bridgeSource, "public bool CallNpcMethod", "public bool CallNpcFunc");
var npcFunctions = Slice(bridgeSource, "public bool CallNpcFunc",
    "public bool CallStandaloneFunction");

RequireClosed(playerProperties, "case \"havetimenum\":", "HaveTimeNum");
RequireClosed(playerFunctions, "case \"getvitalityvalue\":", "GetVitalityValue");
RequireClosed(playerFunctions, "case \"newbiegiftconsume\":", "NewBieGiftConsume");
RequireClosed(npcMethods, "case \"requsetimebuylf\":", "ReqUseTimeBuyLF");
Equal(0, Count(playerMethods, "case \"givepositivevvalue\":"),
    "GivePositiveVValue player-method dispatch count");
Equal(0, Count(npcMethods, "case \"givepositivevvalue\":"),
    "GivePositiveVValue NPC-method dispatch count");
RequireClosed(npcFunctions, "case \"givepositivevvalue\":",
    "GivePositiveVValue NPC function");
Equal(1, Count(playerMethods, "case \"queryawardcode\":"),
    "QueryAwardCode player-method dispatch count");
Equal(0, Count(playerFunctions, "case \"queryawardcode\":"),
    "QueryAwardCode player-function dispatch count");
RequireOpen(playerMethods, "case \"queryawardcode\":",
    "CurrentPlayer.QueryNativeAwardCode", "QueryAwardCode");
RequireOpen(playerMethods, "case \"setawardcodeactiveparam\":",
    "CurrentPlayer.SetNativeAwardCodeActiveParam", "SetAwardCodeActiveParam");
Equal(0, Count(bridgeSource, "private void SetAwardCodeActiveParam("),
    "SetAwardCodeActiveParam synchronous substitute method count");
Equal(0, Count(bridgeSource, "SELECT idx FROM mir3.user_index"),
    "award-code synthetic owner lookup count");

Console.WriteLine(
    "PASS timed/account APIs=4 AwardCode query/set=async-open " +
    "inventory/equipment/currency/messages/V/S=unchanged");
return;

static List<PasValue> Values(params object[] values) => values.Select(value => value switch
{
    int number => PasValue.FromInt(number),
    string text => PasValue.FromString(text),
    _ => PasValue.FromObject(value)
}).ToList();

static void RequireClosed(string source, string marker, string name)
{
    Equal(1, Count(source, marker), name + " dispatch count");
    var start = source.IndexOf(marker, StringComparison.Ordinal);
    var end = source.IndexOf("RejectUnsupportedNativeApi", start + marker.Length,
        StringComparison.Ordinal);
    Assert(end >= 0 && end - start <= 320, name + " is not fail-closed near its dispatch");
    var region = source.Substring(start, end - start);
    foreach (var forbidden in new[]
             {
                 "GetPlayerVar", "SetPlayerVar", "m_ScriptVVars", "m_ScriptSVars",
                 "m_ItemList", "m_UseItems", "m_nGold", "m_nGameGold", "m_nGamePoint",
                 "m_nPayMentPoint", "m_nShengWan", "SendMsg", "SendDefMessage", "SysMsg"
             })
        Assert(!region.Contains(forbidden, StringComparison.Ordinal),
            name + " retains non-native substitute: " + forbidden);
}

static void RequireOpen(string source, string marker, string required,
    string name)
{
    Equal(1, Count(source, marker), name + " dispatch count");
    var start = source.IndexOf(marker, StringComparison.Ordinal);
    var end = source.IndexOf("case \"", start + marker.Length,
        StringComparison.Ordinal);
    if (end < 0) end = source.Length;
    var region = source.Substring(start, end - start);
    Assert(region.Contains(required, StringComparison.Ordinal),
        name + " does not enqueue the native async task");
    Assert(!region.Contains("RejectUnsupportedNativeApi",
        StringComparison.Ordinal), name + " remains fail-closed");
}

static string Slice(string source, string startMarker, string endMarker)
{
    var start = source.IndexOf(startMarker, StringComparison.Ordinal);
    var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
    if (start < 0 || end < 0) throw new InvalidOperationException(
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

static void AssertNil(PasValue value, string message) =>
    Assert(value.Type == PasValueType.Nil, message + " failure did not return Nil");

static void Equal(int expected, int actual, string message)
{
    if (expected != actual) throw new InvalidOperationException(
        $"{message}: expected {expected}, actual {actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed record Snapshot(
    KeyValuePair<int, int>[] V,
    KeyValuePair<int, int>[] S,
    int Gold,
    int GameGold,
    int GamePoint,
    int PaymentPoint,
    int ShengWan,
    int MessageCount,
    TUserItem BagItem,
    TUserItem BodyItem)
{
    public static Snapshot Capture(TPlayObject player) => new(
        player.m_ScriptVVars.OrderBy(item => item.Key).ToArray(),
        player.m_ScriptSVars.OrderBy(item => item.Key).ToArray(),
        player.m_nGold, player.m_nGameGold, player.m_nGamePoint,
        player.m_nPayMentPoint, player.m_nShengWan, player.m_MsgList.Count,
        player.m_ItemList.Single(), player.m_UseItems[0]);

    public void AssertUnchanged(TPlayObject player, string operation)
    {
        Ensure(V.SequenceEqual(player.m_ScriptVVars.OrderBy(item => item.Key)),
            operation + " changed V variables");
        Ensure(S.SequenceEqual(player.m_ScriptSVars.OrderBy(item => item.Key)),
            operation + " changed S variables");
        EnsureEqual(Gold, player.m_nGold, operation + " changed Gold");
        EnsureEqual(GameGold, player.m_nGameGold, operation + " changed GameGold");
        EnsureEqual(GamePoint, player.m_nGamePoint, operation + " changed GamePoint");
        EnsureEqual(PaymentPoint, player.m_nPayMentPoint, operation + " changed PaymentPoint");
        EnsureEqual(ShengWan, player.m_nShengWan, operation + " changed ShengWan");
        EnsureEqual(MessageCount, player.m_MsgList.Count, operation + " emitted a message");
        Ensure(player.m_ItemList.Count == 1 && ReferenceEquals(BagItem, player.m_ItemList[0]),
            operation + " changed the bag");
        Ensure(ReferenceEquals(BodyItem, player.m_UseItems[0]),
            operation + " changed equipment");
        Ensure(BagItem.wIndex == 812 && BagItem.Dura == 813 && BagItem.DuraMax == 814,
            operation + " mutated the bag item");
        Ensure(BodyItem.wIndex == 822 && BodyItem.Dura == 823 && BodyItem.DuraMax == 824,
            operation + " mutated the equipped item");
    }

    private static void EnsureEqual(int expected, int actual, string message)
    {
        if (expected != actual) throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
