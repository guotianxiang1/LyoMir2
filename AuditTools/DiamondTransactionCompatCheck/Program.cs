using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.ObjectManager = new ObjectManager();
M2Share.UserEngine = new UserEngine();
M2Share.ProcessMsgCriticalSection = new object();
M2Share.LogMsgCriticalSection = new object();
M2Share.LogStringList = new ArrayList();

var takeDiamond = typeof(TPlayObject).GetMethod("TakeNativeDiamond",
    BindingFlags.Instance | BindingFlags.NonPublic);
Assert(takeDiamond != null, "TakeNativeDiamond helper is missing");

SetDefinitions(new GoodItem { Name = "金刚石", StdMode = 152, DuraMax = 100 });
var invalidPlayer = NewPlayer();
invalidPlayer.m_ItemList.Add(NewItem(10, 1, 9));
Assert(!Take(invalidPlayer, 0), "zero TakeDiamond returned True");
Assert(!Take(invalidPlayer, -1), "negative TakeDiamond returned True");
Equal(1, invalidPlayer.m_ItemList.Count, "invalid TakeDiamond bag count");
Equal(9, invalidPlayer.m_ItemList[0].Dura, "invalid TakeDiamond quantity");
Equal(0, invalidPlayer.m_MsgList.Count, "invalid TakeDiamond message count");
Equal(0, M2Share.LogStringList.Count, "invalid TakeDiamond log count");

SetDefinitions(new GoodItem { Name = "钻石", StdMode = 7, DuraMax = 100 });
var missingPlayer = NewPlayer();
missingPlayer.m_ItemList.Add(NewItem(20, 1, 50));
Assert(!Take(missingPlayer, 1), "missing definition TakeDiamond returned True");
Equal(1, missingPlayer.m_ItemList.Count, "missing definition changed bag");
AssertRefreshOnly(missingPlayer, "missing definition");
Equal(0, M2Share.LogStringList.Count, "missing definition log count");

SetDefinitions(new GoodItem { Name = "金刚石", StdMode = 152, DuraMax = 100 });
var insufficientPlayer = NewPlayer();
var insufficientFront = NewItem(30, 1, 20);
var insufficientTail = NewItem(31, 1, 10);
insufficientPlayer.m_ItemList.Add(insufficientFront);
insufficientPlayer.m_ItemList.Add(insufficientTail);
Assert(!Take(insufficientPlayer, 31), "insufficient pile TakeDiamond returned True");
Assert(insufficientPlayer.m_ItemList.SequenceEqual(
        new[] { insufficientFront, insufficientTail }),
    "insufficient pile changed bag order or identity");
Equal(20, insufficientFront.Dura, "insufficient front quantity");
Equal(10, insufficientTail.Dura, "insufficient tail quantity");
AssertRefreshOnly(insufficientPlayer, "insufficient pile");
Equal(0, M2Share.LogStringList.Count, "insufficient pile log count");

SetDefinitions(
    new GoodItem { Name = "金刚石", StdMode = 0, Weight = 3 },
    new GoodItem { Name = "其他", StdMode = 0, Weight = 5 });
var ordinaryPlayer = NewPlayer();
var ordinaryKeep = NewItem(101, 1, 600);
var unrelated = NewItem(150, 2, 600);
var unsignedItem = NewItem(-1, 1, 600);
var ordinaryTail = NewItem(103, 1, 600);
ordinaryPlayer.m_ItemList.Add(ordinaryKeep);
ordinaryPlayer.m_ItemList.Add(unrelated);
ordinaryPlayer.m_ItemList.Add(unsignedItem);
ordinaryPlayer.m_ItemList.Add(ordinaryTail);
Assert(Take(ordinaryPlayer, 2), "ordinary TakeDiamond returned False");
Assert(ordinaryPlayer.m_ItemList.SequenceEqual(new[] { ordinaryKeep, unrelated }),
    "ordinary TakeDiamond did not remove matching items from the tail");
Equal(2, M2Share.LogStringList.Count, "ordinary TakeDiamond log count");
EqualText("10\taudit-map\t12\t34\taudit-role\t金刚石\t103\t1\tNPC收取",
    LogAt(0), "ordinary tail log");
EqualText("10\taudit-map\t12\t34\taudit-role\t金刚石\t4294967295\t1\tNPC收取",
    LogAt(1), "ordinary Cardinal MakeIndex log");
AssertSuccessMessages(ordinaryPlayer, "ordinary");
Equal(8, ordinaryPlayer.m_WAbil.Weight, "ordinary WeightChanged value");

SetDefinitions(new GoodItem
    { Name = "金刚石", StdMode = 152, DuraMax = 100, Weight = 2 });
var pilePlayer = NewPlayer();
var pileFront = NewItem(400, 1, 40, 100);
var pileTail = NewItem(425, 1, 25, 100);
pilePlayer.m_ItemList.Add(pileFront);
pilePlayer.m_ItemList.Add(pileTail);
Assert(Take(pilePlayer, 50), "StdMode=152 TakeDiamond returned False");
Equal(1, pilePlayer.m_ItemList.Count, "StdMode=152 remaining bag count");
Assert(ReferenceEquals(pileFront, pilePlayer.m_ItemList[0]),
    "StdMode=152 removed the wrong pile");
Equal(15, pileFront.Dura, "StdMode=152 partial pile quantity");
Equal(2, M2Share.LogStringList.Count, "StdMode=152 log count");
EqualText("10\taudit-map\t12\t34\taudit-role\t金刚石\t425\t50\tNPC收取50个",
    LogAt(0), "StdMode=152 tail log");
EqualText("10\taudit-map\t12\t34\taudit-role\t金刚石\t400\t25\tNPC收取25个",
    LogAt(1), "StdMode=152 partial log");
Equal(Grobal2.SM_BAGITEMDURACHG, pilePlayer.m_DefMsg.Ident,
    "StdMode=152 partial packet ident");
Equal(pileFront.ClientItemID, pilePlayer.m_DefMsg.Recog,
    "StdMode=152 partial packet client item id");
Equal(15, pilePlayer.m_DefMsg.Param, "StdMode=152 partial packet quantity");
Equal(100, pilePlayer.m_DefMsg.Tag, "StdMode=152 partial packet DuraMax");
AssertSuccessMessages(pilePlayer, "StdMode=152");
Equal(30, pilePlayer.m_WAbil.Weight, "StdMode=152 WeightChanged value");

// StdMode 7 is the charm family, not a pile. Keep this explicit boundary so a
// future broad StdMode shortcut cannot silently reintroduce the old bug.
SetDefinitions(new GoodItem
    { Name = "金刚石", StdMode = 7, DuraMax = 100, Weight = 2 });
var charmPlayer = NewPlayer();
var charmKeep = NewItem(450, 1, 40, 100);
var charmTail = NewItem(451, 1, 25, 100);
charmPlayer.m_ItemList.Add(charmKeep);
charmPlayer.m_ItemList.Add(charmTail);
Assert(Take(charmPlayer, 1), "StdMode=7 ordinary TakeDiamond returned False");
Equal(1, charmPlayer.m_ItemList.Count, "StdMode=7 ordinary remaining bag count");
Equal(40, charmKeep.Dura, "StdMode=7 changed ordinary item Dura");
EqualText("10\taudit-map\t12\t34\taudit-role\t金刚石\t451\t1\tNPC收取",
    LogAt(0), "StdMode=7 ordinary log");
AssertSuccessMessages(charmPlayer, "StdMode=7 ordinary");

SetDefinitions(new GoodItem
    { Name = "金刚石", StdMode = 150, Shape = 0, DuraMax = 100 });
var runtimePilePlayer = NewPlayer();
var runtimePile = NewItem(500, 1, 5, 100);
runtimePilePlayer.m_ItemList.Add(runtimePile);
Assert(Take(runtimePilePlayer, 3), "runtime pile TakeDiamond returned False");
Equal(2, runtimePile.Dura, "runtime pile quantity");
EqualText("10\taudit-map\t12\t34\taudit-role\t金刚石\t500\t3\tNPC收取3个",
    LogAt(0), "runtime pile log");
Equal(Grobal2.SM_BAGITEMDURACHG, runtimePilePlayer.m_DefMsg.Ident,
    "runtime pile packet ident");
AssertSuccessMessages(runtimePilePlayer, "runtime pile");

SetDefinitions(new GoodItem
    { Name = "金刚石", StdMode = 149, DuraMax = 100 });
var belowRuntimePilePlayer = NewPlayer();
var belowKeep = NewItem(600, 1, 40, 100);
var belowTail = NewItem(601, 1, 25, 100);
belowRuntimePilePlayer.m_ItemList.Add(belowKeep);
belowRuntimePilePlayer.m_ItemList.Add(belowTail);
Assert(Take(belowRuntimePilePlayer, 1), "StdMode=149 TakeDiamond returned False");
Equal(1, belowRuntimePilePlayer.m_ItemList.Count,
    "StdMode=149 was incorrectly treated as a pile");
Equal(40, belowKeep.Dura, "StdMode=149 changed ordinary item Dura");
EqualText("10\taudit-map\t12\t34\taudit-role\t金刚石\t601\t1\tNPC收取",
    LogAt(0), "StdMode=149 ordinary log");
AssertSuccessMessages(belowRuntimePilePlayer, "StdMode=149");

SetDefinitions(new GoodItem { Name = "金刚石", StdMode = 152, DuraMax = 100 });
var zeroPilePlayer = NewPlayer();
var nonzeroPile = NewItem(700, 1, 5, 100);
var zeroPile = NewItem(701, 1, 0, 100);
zeroPilePlayer.m_ItemList.Add(nonzeroPile);
zeroPilePlayer.m_ItemList.Add(zeroPile);
Assert(Take(zeroPilePlayer, 1), "zero-Dura leading pile TakeDiamond returned False");
Equal(1, zeroPilePlayer.m_ItemList.Count, "zero-Dura pile was not removed");
Equal(4, nonzeroPile.Dura, "zero-Dura traversal partial quantity");
Equal(2, M2Share.LogStringList.Count, "zero-Dura traversal log count");
EqualText("10\taudit-map\t12\t34\taudit-role\t金刚石\t701\t1\tNPC收取1个",
    LogAt(0), "zero-Dura pile log");
EqualText("10\taudit-map\t12\t34\taudit-role\t金刚石\t700\t1\tNPC收取1个",
    LogAt(1), "post-zero partial pile log");
AssertSuccessMessages(zeroPilePlayer, "zero-Dura traversal");

SetDefinitions(
    new GoodItem { Name = "金刚石", StdMode = 152, DuraMax = 100 },
    new GoodItem { Name = "金刚石", StdMode = 152, DuraMax = 100 });
var duplicateNamePlayer = NewPlayer();
duplicateNamePlayer.m_ItemList.Add(NewItem(800, 2, 99, 100));
Assert(!Take(duplicateNamePlayer, 1),
    "TakeDiamond did not use the first resolved wIndex");
Equal(1, duplicateNamePlayer.m_ItemList.Count,
    "duplicate-name unresolved item was consumed");
AssertRefreshOnly(duplicateNamePlayer, "duplicate-name wIndex");
Equal(0, M2Share.LogStringList.Count, "duplicate-name log count");

SetDefinitions(new GoodItem
    { Name = "金刚石", StdMode = 152, DuraMax = 100 });
var dispatchPlayer = NewPlayer();
var dispatchPile = NewItem(900, 1, 5, 100);
dispatchPlayer.m_ItemList.Add(dispatchPile);
var bridge = new PasApiBridge { CurrentPlayer = dispatchPlayer };
foreach (var invalidArgs in new[]
         {
             new List<PasValue>(),
             new List<PasValue> { PasValue.FromInt(2) },
             new List<PasValue>
             {
                 PasValue.FromInt(2), PasValue.Nil, PasValue.FromInt(0)
             }
         })
{
    Assert(!bridge.CallPlayerFunc("TakeDiamond", invalidArgs, out var invalidResult),
        $"TakeDiamond accepted arity {invalidArgs.Count}");
    Assert(invalidResult.Type == PasValueType.Nil,
        $"TakeDiamond arity {invalidArgs.Count} did not return Nil");
}
Equal(5, dispatchPile.Dura, "invalid TakeDiamond dispatch changed the pile");
Equal(0, dispatchPlayer.m_MsgList.Count,
    "invalid TakeDiamond dispatch emitted a message");
Equal(0, M2Share.LogStringList.Count,
    "invalid TakeDiamond dispatch emitted a log");
Assert(!bridge.CallPlayerMethod("TakeDiamond", new List<PasValue>
    { PasValue.FromInt(2), PasValue.Nil }),
    "TakeDiamond method dispatcher was opened");
Assert(bridge.CallPlayerFunc("TakeDiamond", new List<PasValue>
    { PasValue.FromInt(2), PasValue.Nil }, out var dispatchResult),
    "TakeDiamond exact-two function was not dispatched");
Assert(dispatchResult.Type == PasValueType.Boolean && dispatchResult.AsBool(),
    "TakeDiamond exact-two function did not return True");
Equal(3, dispatchPile.Dura,
    "TakeDiamond exact-two function did not debit the requested quantity");
AssertSuccessMessages(dispatchPlayer, "function dispatch");

var root = FindRepositoryRoot();
var source = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
    "TPlayObject.NativeDiamond.cs"));
var bridgeSource = File.ReadAllText(Path.Combine(root, "GameSvr", "ScriptSystem",
    "PasEngine", "PasApiBridge.cs"));
// Native sub_74054C has exactly one pass, and it is the consuming one: 0x740596
// `mov esi,[eax+8]` takes the bag count, 0x740599 `dec esi` starts at Count-1, and the tail
// at 0x7406E8 is `dec esi / cmp esi,-1 / jne 0x7405A3`. Atomicity is achieved by staging the
// removed items in a temp list (0x424B30 delete + 0x424AB8 add) and restoring them at
// 0x7406F5 `cmp eax,[ebp-0xc] / jl 0x740804` when the total came up short.
//
// So "the commit walks tail to head" is a byte fact and stays pinned. There is no native
// preflight pass at all, which means its direction is not a native fact and must not be
// pinned -- the C# preflight only sums and breaks early, so its verdict is order-independent
// either way. What has to hold is the all-or-nothing outcome native reaches by rollback.
RequireMatches(source,
    "for \\(var index = m_ItemList\\.Count - 1;[\\s\\S]{0,260}?consumed",
    1, "TakeDiamond commit tail-to-head pass");
RequireMatches(source, "if \\(available >= amount\\) break;", 1,
    "TakeDiamond preflight stops once the requested quantity is reachable");
RequireMatches(source, "if \\(available < amount\\) return false;", 1,
    "TakeDiamond preflight is not all-or-nothing (native rolls back at 0x7406F5)");
RequireMatches(source,
    "NativeItemFactory\\.IsPileItem\\(stdItem\\)",
    1, "TakeDiamond runtime pile classification");
Assert(!source.Contains("stdItem.StdMode == 7", StringComparison.Ordinal),
    "TakeDiamond still treats the StdMode=7 charm family as a pile");
RequireMatches(source, "RefreshNativeLingFu\\(\\);", 1,
    "TakeDiamond positive capital refresh");
RequireMatches(source, "WeightChanged\\(\\);", 1,
    "TakeDiamond successful weight refresh");
Assert(!source.Contains("m_nNativeDiamondCache", StringComparison.Ordinal),
    "TakeDiamond directly mutates the transient diamond cache");
RequireMatches(bridgeSource,
    "case \\\"takediamond\\\":\\s*if \\(args\\.Count != 2\\) return false;\\s*" +
    "result = PasValue\\.FromBool\\(\\s*" +
    "CurrentPlayer\\.TakeNativeDiamond\\(args\\[0\\]\\.AsInt\\(\\)\\)\\);\\s*" +
    "return true;",
    1, "TakeDiamond CallPlayerFunc exact-two dispatch");
var functionStart = bridgeSource.IndexOf("case \"takediamond\":",
    bridgeSource.IndexOf("public bool CallPlayerFunc", StringComparison.Ordinal),
    StringComparison.Ordinal);
var functionEnd = bridgeSource.IndexOf("case \"checkdiamond\":",
    functionStart, StringComparison.Ordinal);
Assert(functionStart >= 0 && functionEnd > functionStart,
    "TakeDiamond CallPlayerFunc source boundary is missing");
var functionSource = bridgeSource.Substring(functionStart,
    functionEnd - functionStart);
Assert(!functionSource.Contains("args[1]", StringComparison.Ordinal),
    "TakeDiamond CallPlayerFunc reads its required Npc argument");

Console.WriteLine(
    "PASS TakeDiamond invalid=silent positive=10054 preflight=atomic commit=tail-to-head " +
    "ordinary=count pile=StdMode152/runtime logs=type10/Cardinal/NPC WeightChanged=once " +
    "dispatch=CallPlayerFunc/exact2/Npc-unread method=closed");
return;

bool Take(TPlayObject player, int amount)
{
    M2Share.LogStringList.Clear();
    player.m_MsgList.Clear();
    return (bool)takeDiamond!.Invoke(player, new object[] { amount })!;
}

static TPlayObject NewPlayer()
{
    return new TPlayObject
    {
        m_boOffLineFlag = true,
        m_sMapName = "audit-map",
        m_nCurrX = 12,
        m_nCurrY = 34,
        m_sCharName = "audit-role"
    };
}

static TUserItem NewItem(int makeIndex, ushort itemIndex, ushort dura,
    ushort duraMax = 1000)
{
    return new TUserItem
    {
        MakeIndex = makeIndex,
        wIndex = itemIndex,
        Dura = dura,
        DuraMax = duraMax,
        btValue = new byte[14]
    };
}

static void SetDefinitions(params GoodItem[] definitions)
{
    M2Share.UserEngine.StdItemList.Clear();
    foreach (var definition in definitions)
        M2Share.UserEngine.StdItemList.Add(definition);
}

static void AssertRefreshOnly(TPlayObject player, string scenario)
{
    Equal(1, player.m_MsgList.Count, $"{scenario} internal message count");
    Equal(Grobal2.RM_LINGFU_CHANGED, player.m_MsgList[0].wIdent,
        $"{scenario} internal capital refresh ident");
}

static void AssertSuccessMessages(TPlayObject player, string scenario)
{
    Equal(2, player.m_MsgList.Count, $"{scenario} internal message count");
    Equal(Grobal2.RM_WEIGHTCHANGED, player.m_MsgList[0].wIdent,
        $"{scenario} WeightChanged order");
    Equal(Grobal2.RM_LINGFU_CHANGED, player.m_MsgList[1].wIdent,
        $"{scenario} capital refresh order");
}

static string LogAt(int index) => (string)M2Share.LogStringList[index]!;

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory != null)
    {
        if (Directory.Exists(Path.Combine(directory.FullName, "GameSvr")) &&
            Directory.Exists(Path.Combine(directory.FullName, "AuditTools")))
            return directory.FullName;
        directory = directory.Parent;
    }
    throw new InvalidOperationException("repository root not found");
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

static void RequireMatches(string source, string pattern, int expected,
    string message)
{
    var actual = Regex.Matches(source, pattern,
        RegexOptions.CultureInvariant).Count;
    Equal(expected, actual, message);
}

static void Equal(int expected, int actual, string message)
{
    if (expected != actual)
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
}

static void EqualText(string expected, string actual, string message)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
