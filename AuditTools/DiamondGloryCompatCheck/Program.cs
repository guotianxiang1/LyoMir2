using System.Reflection;
using System.Text.RegularExpressions;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.ObjectManager = new ObjectManager();
M2Share.LogStringList = new System.Collections.ArrayList();

var player = new TPlayObject
{
    m_nGameGold = 300,
    m_nGamePoint = 200
};
M2Share.UserEngine = new UserEngine();
M2Share.UserEngine.StdItemList.Add(new GoodItem { Name = "金刚石" });
M2Share.UserEngine.StdItemList.Add(new GoodItem { Name = "金刚石矿" });
player.m_ItemList.Add(null);
player.m_ItemList.Add(new TUserItem { wIndex = 0, Dura = ushort.MaxValue });
player.m_ItemList.Add(new TUserItem { wIndex = 99, Dura = ushort.MaxValue });
player.m_ItemList.Add(new TUserItem { wIndex = 1, Dura = 13, DuraMax = ushort.MaxValue });
player.m_ItemList.Add(new TUserItem { wIndex = 1, Dura = 20, DuraMax = 1 });
player.m_ItemList.Add(new TUserItem { wIndex = 2, Dura = 99 });
player.m_UseItems[0] = new TUserItem { wIndex = 1, Dura = 100 };
player.m_ScriptVVars[10001] = 71;
player.m_ScriptVVars[10005] = 75;
player.m_ScriptVVars[50001] = 123;
player.m_ScriptVVars[50002] = 9;
player.m_ScriptSVars[10001] = 81;
player.m_ScriptSVars[10005] = 85;

var originalV = player.m_ScriptVVars.OrderBy(entry => entry.Key).ToArray();
var originalS = player.m_ScriptSVars.OrderBy(entry => entry.Key).ToArray();
var originalGameGold = player.m_nGameGold;
var originalGamePoint = player.m_nGamePoint;
var originalItemCount = player.m_ItemList.Count;
var bridge = new PasApiBridge
{
    CurrentPlayer = player,
    CurrentNpc = new NormNpc()
};

Assert(bridge.GetPlayerProperty("MyDiamondnum", out var diamondValue),
    "MyDiamondnum read-only property was not dispatched");
Assert(diamondValue.Type == PasValueType.Integer,
    "MyDiamondnum property did not return Integer");
Equal(33, diamondValue.AsInt(),
    "MyDiamondnum did not sum canonical bag-item Dura exactly");

var emptyPlayer = new TPlayObject();
bridge.CurrentPlayer = emptyPlayer;
Assert(bridge.GetPlayerProperty("MyDiamondnum", out diamondValue),
    "empty MyDiamondnum read failed");
Assert(diamondValue.Type == PasValueType.Integer,
    "empty MyDiamondnum did not return Integer");
Equal(0, diamondValue.AsInt(), "empty MyDiamondnum value");
bridge.CurrentPlayer = player;

var enabledCreditCard = CreateCreditCardService(true);
AssertGloryPointRead(player, bridge, NativeCreditCardService.Disabled,
    loaded: false, value: 0, "disabled/unloaded zero");
AssertGloryPointRead(player, bridge, NativeCreditCardService.Disabled,
    loaded: true, value: int.MinValue, "disabled/loaded Int32.MinValue");
AssertGloryPointRead(player, bridge, enabledCreditCard,
    loaded: false, value: int.MaxValue, "enabled/unloaded Int32.MaxValue");
AssertGloryPointRead(player, bridge, enabledCreditCard,
    loaded: true, value: -123456789, "enabled/loaded negative");

var amountArgs = new List<PasValue> { PasValue.FromInt(10) };
var playerTransactionNames = new[]
{
    "TakeDiamond", "AddDiamond", "CheckDiamond", "MakeDiamondWithYB",
    "AddGloryPoint", "DecGloryPoint"
};
foreach (var name in playerTransactionNames)
    Assert(!bridge.CallPlayerMethod(name, amountArgs),
        $"{name} method exposed an incomplete native transaction");
foreach (var name in new[]
         {
             "AddDiamond", "CheckDiamond", "MakeDiamondWithYB"
         })
{
    Assert(!bridge.CallPlayerFunc(name, amountArgs, out var value),
        $"{name} function exposed an incomplete native transaction");
    Assert(value.Type == PasValueType.Nil, $"{name} function failure did not return Nil");
}
Assert(!bridge.CallPlayerFunc("DecGloryPoint", amountArgs,
        out var wrongArityDecResult),
    "DecGloryPoint function accepted a non-exact argument count");
Assert(wrongArityDecResult.Type == PasValueType.Nil,
    "wrong-arity DecGloryPoint did not return Nil");
foreach (var name in new[] { "ClientQuestGetDiam" })
{
    Assert(!bridge.CallNpcMethod(name, amountArgs, out var methodValue),
        $"{name} NPC method exposed an incomplete native transaction");
    Assert(methodValue.Type == PasValueType.Nil,
        $"{name} NPC method failure did not return Nil");
    Assert(!bridge.CallNpcFunc(name, amountArgs, out var functionValue),
        $"{name} NPC function exposed an incomplete native transaction");
    Assert(functionValue.Type == PasValueType.Nil,
        $"{name} NPC function failure did not return Nil");
}
// MakeItemUseDiam is now LIVE (procedure-only, verified faithful vs sub_64DF3C):
// its method path dispatches the forge and its function path rejects (procedure
// not exposed as a function). Runtime behavior is covered exhaustively by
// NativeMakeItemUseDiamTransactionCheck; here we only assert the source wiring below.

Assert(originalV.SequenceEqual(player.m_ScriptVVars.OrderBy(entry => entry.Key)),
    "Diamond/Glory operation changed V variables");
Assert(originalS.SequenceEqual(player.m_ScriptSVars.OrderBy(entry => entry.Key)),
    "Diamond/Glory operation changed S variables");
Equal(originalGameGold, player.m_nGameGold, "Diamond operation changed GameGold");
Equal(originalGamePoint, player.m_nGamePoint, "Diamond/Glory operation changed GamePoint");
Equal(originalItemCount, player.m_ItemList.Count, "Diamond operation changed the inventory");

var root = FindRepositoryRoot();
var bridgeSource = File.ReadAllText(Path.Combine(root, "GameSvr", "ScriptSystem", "PasEngine",
    "PasApiBridge.cs"));
var integrationSource = File.ReadAllText(Path.Combine(root, "GameSvr", "ScriptSystem", "PasEngine",
    "PasIntegration.cs"));

RequireMatches(bridgeSource,
    "case \\\"mydiamondnum\\\":\\s*result = PasValue\\.FromInt\\(" +
    "CurrentPlayer\\.GetNativeDiamondCount\\(\\)\\);\\s*break;",
    1, "MyDiamondnum property must use the native bag-Dura count");
RequireMatches(bridgeSource,
    "case \\\"glorypoint\\\":\\s*result = PasValue\\.FromInt\\(" +
    "CurrentPlayer\\.m_CreditCard\\.GloryPointValue\\);\\s*break;",
    1, "GloryPoint property must read the native account value directly");
RequireMatches(bridgeSource, "case \\\"glorypoint\\\":", 1,
    "GloryPoint must have exactly one read-only dispatcher entry");
var gloryGetterStart = bridgeSource.IndexOf("case \"glorypoint\":",
    StringComparison.Ordinal);
var gloryGetterEnd = bridgeSource.IndexOf("case \"guildpoint\":",
    gloryGetterStart, StringComparison.Ordinal);
Assert(gloryGetterStart >= 0 && gloryGetterEnd > gloryGetterStart,
    "GloryPoint getter source boundary is missing");
var gloryGetter = bridgeSource.Substring(gloryGetterStart,
    gloryGetterEnd - gloryGetterStart);
foreach (var forbidden in new[]
         {
             "CreditCardService", "Enabled", "Loaded", "Dirty", "TryLoad",
             "CalculateGloryPointPeriod", "RefreshNativeLingFu",
             "RejectUnsupportedNativeApi"
         })
    Reject(gloryGetter, forbidden,
        $"GloryPoint getter must not depend on or mutate {forbidden}");
RequireMatches(bridgeSource,
    "case \\\"takediamond\\\":\\s*case \\\"adddiamond\\\":\\s*" +
    "return RejectUnsupportedNativeApi\\(\\);",
    1, "Diamond method dispatch must fail closed");
RequireMatches(bridgeSource,
    "case \\\"takediamond\\\":\\s*if \\(args\\.Count != 2\\) return false;\\s*" +
    "result = PasValue\\.FromBool\\(\\s*" +
    "CurrentPlayer\\.TakeNativeDiamond\\(args\\[0\\]\\.AsInt\\(\\)\\)\\);\\s*" +
    "return true;",
    1, "TakeDiamond function dispatch must require exactly two arguments");
var takeDiamondFunctionStart = bridgeSource.IndexOf("case \"takediamond\":",
    bridgeSource.IndexOf("public bool CallPlayerFunc", StringComparison.Ordinal),
    StringComparison.Ordinal);
var takeDiamondFunctionEnd = bridgeSource.IndexOf("case \"checkdiamond\":",
    takeDiamondFunctionStart, StringComparison.Ordinal);
Assert(takeDiamondFunctionStart >= 0 && takeDiamondFunctionEnd > takeDiamondFunctionStart,
    "TakeDiamond function source boundary is missing");
var takeDiamondFunction = bridgeSource.Substring(takeDiamondFunctionStart,
    takeDiamondFunctionEnd - takeDiamondFunctionStart);
Reject(takeDiamondFunction, "args[1]",
    "TakeDiamond must not read its required Npc argument");
RequireMatches(bridgeSource,
    "case \\\"makediamondwithyb\\\":[\\s\\S]{0,240}?" +
    "return RejectUnsupportedNativeApi\\(out result\\);",
    1, "MakeDiamondWithYB must fail closed");
RequireMatches(bridgeSource,
    "case \\\"addglorypoint\\\":\\s*case \\\"decglorypoint\\\":\\s*" +
    "return RejectUnsupportedNativeApi\\(\\);",
    1, "GloryPoint method dispatch must fail closed");
RequireMatches(bridgeSource,
    "case \\\"decglorypoint\\\":\\s*" +
    "if \\(args\\.Count != 5\\) return false;[\\s\\S]{0,320}?" +
    "CurrentPlayer\\.DecNativeGloryPoint\\(",
    1, "DecGloryPoint function must dispatch exact five parameters");
RequireMatches(bridgeSource,
    "case \\\"clientquestgetdiam\\\":\\s*return RejectUnsupportedNativeApi\\(out result\\);",
    2, "Every ClientQuestGetDiam dispatch must fail closed");
RequireMatches(bridgeSource,
    "ExecuteNativeDiamondForge\\(CurrentNpc,",
    1, "MakeItemUseDiam method dispatches the live forge (verified faithful vs sub_64DF3C)");
RequireMatches(bridgeSource,
    "case \\\"makeitemusediam\\\":[\\s\\S]{0,320}?" +
    "is a procedure and is not exposed as a function[\\s\\S]{0,200}?" +
    "return RejectUnsupportedNativeApi\\(out result\\);",
    1, "MakeItemUseDiam function ABI stays rejected (procedure-only)");
RequireMatches(bridgeSource,
    "(?:Get|Set)PlayerVar\\(\\s*'[VSI]'\\s*,\\s*10\\s*,\\s*(?:1|5)\\b",
    0, "V/S/I currency substitute remains in PasApiBridge");
Reject(integrationSource, "V[10, 1]", "Diamond V-variable documentation substitute");
Reject(integrationSource, "V[10, 5]", "GloryPoint V-variable documentation substitute");
AssertDispatchesHaveNoSubstitute(bridgeSource);

Console.WriteLine(
    "PASS MyDiamondnum=bag-Dura/Integer/zero/null-safe TakeDiamond=func/exact2/Npc-unread " +
    "GloryPoint=read-only/direct Loaded=ignored Enabled=ignored " +
    "Int32=min/max AddMethod=closed DecFunction=exact5 DecMethod=closed side-effects=0");
return;

static void AssertGloryPointRead(TPlayObject player, PasApiBridge bridge,
    NativeCreditCardService service, bool loaded, int value, string scenario)
{
    M2Share.CreditCardService = service;
    var account = player.m_CreditCard;
    account.Loaded = loaded;
    account.Dirty = true;
    account.Value = unchecked(value + 101);
    account.Value2 = unchecked(value - 202);
    account.UsedValue = unchecked(value ^ 0x13579BDF);
    account.Index = 0xF1234567;
    account.LastSaveTick = unchecked(value + 303);
    account.GloryPointDirty = true;
    account.GloryPointValue = value;
    account.GloryPointPeriod = unchecked(value - 404);

    var before = SnapshotAccount(account);
    var messageCount = player.m_MsgList.Count;
    var logCount = M2Share.LogStringList.Count;
    var gameGold = player.m_nGameGold;
    var gamePoint = player.m_nGamePoint;
    var inventoryCount = player.m_ItemList.Count;

    Assert(bridge.GetPlayerProperty("GloryPoint", out var result),
        $"GloryPoint getter failed for {scenario}");
    Assert(result.Type == PasValueType.Integer,
        $"GloryPoint getter returned a non-integer for {scenario}");
    Equal(value, result.AsInt(), $"GloryPoint getter value for {scenario}");
    Assert(SnapshotAccount(account).Equals(before),
        $"GloryPoint getter changed the native account for {scenario}");

    var replacement = value == int.MaxValue ? int.MinValue : value + 1;
    Assert(!bridge.SetPlayerProperty("GloryPoint", PasValue.FromInt(replacement)),
        $"GloryPoint setter was exposed for {scenario}");
    Assert(SnapshotAccount(account).Equals(before),
        $"rejected GloryPoint setter changed the native account for {scenario}");
    Equal(messageCount, player.m_MsgList.Count,
        $"GloryPoint access emitted a message for {scenario}");
    Equal(logCount, M2Share.LogStringList.Count,
        $"GloryPoint access emitted a log for {scenario}");
    Equal(gameGold, player.m_nGameGold,
        $"GloryPoint access changed GameGold for {scenario}");
    Equal(gamePoint, player.m_nGamePoint,
        $"GloryPoint access changed GamePoint for {scenario}");
    Equal(inventoryCount, player.m_ItemList.Count,
        $"GloryPoint access changed inventory for {scenario}");
}

static object SnapshotAccount(NativeCreditCardAccount account) =>
    (account.Loaded, account.Dirty, account.Value, account.Value2,
        account.UsedValue, account.Index, account.LastSaveTick,
        account.GloryPointDirty, account.GloryPointValue,
        account.GloryPointPeriod);

static NativeCreditCardService CreateCreditCardService(bool enabled)
{
    var constructor = typeof(NativeCreditCardService).GetConstructor(
        BindingFlags.Instance | BindingFlags.NonPublic, null,
        new[] { typeof(bool), typeof(bool), typeof(string), typeof(byte[]) },
        null);
    Assert(constructor != null,
        "NativeCreditCardService constructor reflection target is missing");
    var switches = new byte[5];
    if (enabled) switches[1] = 0x10;
    return (NativeCreditCardService)constructor.Invoke(
        new object[] { enabled, false, string.Empty, switches });
}

static void AssertDispatchesHaveNoSubstitute(string source)
{
    foreach (var marker in new[]
             {
                 "case \"adddiamond\":", "case \"checkdiamond\":",
                 "case \"makediamondwithyb\":", "case \"clientquestgetdiam\":"
             })
    {
        var offset = 0;
        while ((offset = source.IndexOf(marker, offset, StringComparison.Ordinal)) >= 0)
        {
            var end = source.IndexOf("RejectUnsupportedNativeApi", offset + marker.Length,
                StringComparison.Ordinal);
            if (end < 0 || end - offset > 800)
                Fail($"missing nearby fail-closed return after {marker}");
            var region = source.Substring(offset, end - offset);
            foreach (var forbidden in new[]
                     {
                         "GetPlayerVar", "SetPlayerVar", "m_ScriptVVars", "m_ScriptSVars",
                         "m_nGameGold", "m_nGamePoint", "m_nPayMentPoint"
                     })
            {
                if (region.Contains(forbidden, StringComparison.Ordinal))
                    Fail($"{marker} uses non-native substitute: {forbidden}");
            }
            offset = end + 1;
        }
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
    Equal(expected, actual, message);
}

static void Reject(string source, string value, string message)
{
    if (source.Contains(value, StringComparison.OrdinalIgnoreCase))
        Fail($"{message} is present");
}

static void Equal(int expected, int actual, string message)
{
    if (expected != actual) Fail($"{message}: expected {expected}, actual {actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) Fail(message);
}

static void Fail(string message) => throw new InvalidOperationException(message);
