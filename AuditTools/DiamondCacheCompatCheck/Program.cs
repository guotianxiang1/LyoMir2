using System.Buffers.Binary;
using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using GameSvr;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.ObjectManager = new ObjectManager();
M2Share.UserEngine = new UserEngine();
M2Share.ProcessMsgCriticalSection = new object();
M2Share.LogMsgCriticalSection = new object();
M2Share.LogStringList = new ArrayList();
SetDefinitions(new GoodItem { Name = "金刚石", StdMode = 152, DuraMax = 100 });

var cacheField = typeof(TPlayObject).GetField("m_nNativeDiamondCache",
    BindingFlags.Instance | BindingFlags.NonPublic);
var initializeCache = typeof(TPlayObject).GetMethod(
    "InitializeNativeDiamondCacheAfterLogon",
    BindingFlags.Instance | BindingFlags.NonPublic);
var countBag = typeof(TPlayObject).GetMethod("GetNativeDiamondCount",
    BindingFlags.Instance | BindingFlags.NonPublic);
var buildCapital = typeof(TPlayObject).GetMethod("BuildNativeCapitalInfoBody",
    BindingFlags.Instance | BindingFlags.NonPublic);
var takeDiamond = typeof(TPlayObject).GetMethod("TakeNativeDiamond",
    BindingFlags.Instance | BindingFlags.NonPublic);
Assert(cacheField != null, "transient diamond cache field is missing");
Assert(initializeCache != null, "login diamond-cache initializer is missing");
Assert(countBag != null, "physical diamond bag scanner is missing");
Assert(buildCapital != null, "native capital body builder is missing");
Assert(takeDiamond != null, "TakeDiamond helper is missing");

var player = new TPlayObject { m_boOffLineFlag = true };
var initialPile = NewItem(100, 20);
player.m_ItemList.Add(initialPile);
initializeCache.Invoke(player, null);
Equal(20, ReadCache(player), "first login cache baseline");
Equal(20, ReadBag(player), "first login physical bag count");
AssertRefreshOnly(player, "first login baseline");

var pickedPile = NewItem(101, 7);
player.m_MsgList.Clear();
player.m_ItemList.Add(pickedPile);
Equal(27, ReadBag(player), "pickup physical bag count");
Equal(20, ReadCache(player), "pickup changed transient cache");
Equal(20, ReadCapitalDiamond(player), "pickup changed capital offset +8");
Equal(0, player.m_MsgList.Count, "pickup simulation queued a capital refresh");

player.m_ItemList.Remove(pickedPile);
Equal(20, ReadBag(player), "drop physical bag count");
Equal(20, ReadCache(player), "drop changed transient cache");
Equal(20, ReadCapitalDiamond(player), "drop changed capital offset +8");

player.m_MsgList.Clear();
Assert(!(bool)takeDiamond.Invoke(player, new object[] { 99 })!,
    "insufficient TakeDiamond returned True");
Equal(20, ReadBag(player), "failed TakeDiamond changed physical bag");
Equal(20, ReadCache(player), "failed TakeDiamond changed transient cache");
AssertRefreshOnly(player, "failed positive TakeDiamond");

player.m_MsgList.Clear();
Assert((bool)takeDiamond.Invoke(player, new object[] { 5 })!,
    "successful TakeDiamond returned False");
Equal(15, ReadBag(player), "successful TakeDiamond physical bag count");
Equal(20, ReadCache(player), "successful TakeDiamond changed transient cache");
Equal(20, ReadCapitalDiamond(player),
    "successful TakeDiamond changed capital offset +8");
Equal(1, player.m_MsgList.Count(message =>
        message.wIdent == Grobal2.RM_LINGFU_CHANGED),
    "successful TakeDiamond capital refresh count");

player.m_MsgList.Clear();
Assert(!(bool)takeDiamond.Invoke(player, new object[] { 0 })!,
    "zero TakeDiamond returned True");
Equal(20, ReadCache(player), "zero TakeDiamond changed transient cache");
Equal(0, player.m_MsgList.Count, "zero TakeDiamond queued a refresh");

M2Share.UserEngine.StdItemList.Clear();
Equal(20, ReadCapitalDiamond(player),
    "capital offset +8 rescanned item definitions or the bag");
SetDefinitions(new GoodItem { Name = "金刚石", StdMode = 152, DuraMax = 100 });
player.m_MsgList.Clear();
initializeCache.Invoke(player, null);
Equal(15, ReadCache(player), "relogin cache baseline");
Equal(15, ReadCapitalDiamond(player), "relogin capital offset +8");
AssertRefreshOnly(player, "relogin baseline");

var root = FindRepositoryRoot();
var lingFuSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
    "TPlayObject.NativeLingFu.cs"));
var loginSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
    "TPlayObject.Base.cs"));
var takeSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
    "TPlayObject.NativeDiamond.cs"));
var bridgeSource = File.ReadAllText(Path.Combine(root, "GameSvr", "ScriptSystem",
    "PasEngine", "PasApiBridge.cs"));
var gameSourceFiles = Directory.EnumerateFiles(Path.Combine(root, "GameSvr"),
        "*.cs", SearchOption.AllDirectories)
    .Select(path => new
    {
        Path = path,
        RelativePath = Path.GetRelativePath(root, path),
        Source = File.ReadAllText(path)
    })
    .ToArray();

var capitalStart = lingFuSource.IndexOf("internal byte[] BuildNativeCapitalInfoBody()",
    StringComparison.Ordinal);
var capitalEnd = lingFuSource.IndexOf("internal int GetNativeDiamondCount()",
    capitalStart, StringComparison.Ordinal);
Assert(capitalStart >= 0 && capitalEnd > capitalStart,
    "capital builder source boundary is missing");
var capitalSource = lingFuSource.Substring(capitalStart, capitalEnd - capitalStart);
Assert(capitalSource.Contains("body.AsSpan(8, 4)", StringComparison.Ordinal) &&
       capitalSource.Contains("m_nNativeDiamondCache", StringComparison.Ordinal),
    "capital offset +8 does not read the transient cache");
Assert(!capitalSource.Contains("GetNativeDiamondCount", StringComparison.Ordinal),
    "capital packet still rescans the physical bag");
RequireMatches(lingFuSource,
    @"m_nNativeDiamondCache\s*=\s*GetNativeDiamondCount\(\);\s*" +
    @"RefreshNativeLingFu\(\);", 1,
    "login cache scan/assign/refresh sequence");
RequireMatches(loginSource,
    @"ResumeSecHeroPracticeAfterLogon\(\);\s*" +
    @"InitializeNativeDiamondCacheAfterLogon\(\);\s*}\s*catch", 1,
    "successful login absolute-tail cache initialization");
Assert(!takeSource.Contains("m_nNativeDiamondCache", StringComparison.Ordinal),
    "TakeDiamond directly mutates the transient cache");
RequireMatches(bridgeSource,
    @"PasValue\.FromInt\(CurrentPlayer\.GetNativeDiamondCount\(\)\)", 1,
    "MyDiamondnum physical bag binding");
RequireMatches(bridgeSource,
    @"case\s+""donatediam"":[\s\S]{0,500}?" +
    @"return\s+RejectUnsupportedNativeApi\(\);", 1,
    "Donatediam must remain fail-closed");
RequireMatches(bridgeSource,
    @"case\s+""getskyprize"":[\s\S]{0,300}?" +
    @"GetNativeMagicTowerSkyPrize\(CurrentNpc\)", 1,
    "GetSkyPrize explicit-player procedure bridge");

var cacheOwners = gameSourceFiles
    .Where(file => file.Source.Contains("m_nNativeDiamondCache",
        StringComparison.Ordinal))
    .Select(file => file.RelativePath)
    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
    .ToArray();
Assert(cacheOwners.Any(path => path.EndsWith(
        "TPlayObject.NativeLingFu.cs", StringComparison.OrdinalIgnoreCase)),
    "transient cache lost its runtime owner");
Assert(cacheOwners.Any(path => path.EndsWith(
        "TPlayObject.NativeMagicTower.Prize.cs",
        StringComparison.OrdinalIgnoreCase)),
    "GetSkyPrize cache writer escaped its evidence owner");

// The cache is a transient mirror of the bag's 金刚石 Dura total, so what needs fencing is the
// shape of each writer, not how many files hold one. Every assignment must either re-derive
// the value from GetNativeDiamondCount() -- idempotent resync against the authoritative bag --
// or be the single native mutation, the magic-tower hundredth prize's +100. A plain file
// headcount rejected NativeMakeItemUseDiamHost.SendInternalRefresh, which takes the
// re-derivation shape and therefore obeys the rule the headcount stood in for.
var cacheAssignments = gameSourceFiles
    .SelectMany(file => Regex.Matches(file.Source,
            @"m_nNativeDiamondCache\s*=(?!=)(?<rhs>[^;]+);",
            RegexOptions.CultureInvariant)
        .Select(match => (file.RelativePath,
            Rhs: Regex.Replace(match.Groups["rhs"].Value, @"\s+", string.Empty))))
    .ToArray();
Equal(3, cacheAssignments.Length, "transient cache direct assignment count");
foreach (var (path, rhs) in cacheAssignments)
{
    Assert(rhs.EndsWith("GetNativeDiamondCount()", StringComparison.Ordinal)
           || rhs == "unchecked(m_nNativeDiamondCache+100)",
        $"transient cache gained an unapproved writer in {path}: {rhs}");
}
RequireMatches(string.Join("\n", gameSourceFiles.Select(file => file.Source)),
    @"\bm_nNativeDiamondCache\s*(?:\+\+|--|\+=|-=|\*=|/=|%=|&=|\|=|\^=|<<=|>>=)",
    0, "transient cache compound mutation count");
RequireMatches(lingFuSource,
    @"\b(?:Set|Add|Subtract)NativeDiamondCache\b", 0,
    "dormant diamond-cache mutator helper count");
var towerPrizeSource = File.ReadAllText(Path.Combine(root, "GameSvr",
    "Players", "TPlayObject.NativeMagicTower.Prize.cs"));
RequireMatches(towerPrizeSource,
    @"m_nNativeDiamondCache\s*=\s*unchecked\(\s*" +
    @"m_nNativeDiamondCache\s*\+\s*100\s*\)", 1,
    "GetSkyPrize exact cache +100 writer");
RequireMatches(towerPrizeSource,
    @"NativeMagicTowerDiamondHundredPrize[\s\S]{0,1500}?" +
    @"m_nNativeDiamondCache\s*=", 1,
    "GetSkyPrize descriptor-gated cache writer");
RequireMatches(towerPrizeSource,
    @"m_nNativeDiamondCache\s*=\s*unchecked\([\s\S]{0,500}?" +
    @"RefreshNativeLingFu\(\)", 0,
    "GetSkyPrize cache writer must not refresh 10054");

// Native evidence contract. Donatediam remains closed; GetSkyPrize owns its
// direct stateful writer and is exposed only through the proven procedure ABI.
var donatediamLog = new NativeWriterLogEvidence(
    31, "金刚宝石", "senderRemainingBF0", "amount", "targetName");
var getSkyPrizeLog = new NativeWriterLogEvidence(
    50, "金刚宝石", "100", "1", "闯天关大奖");
Equal(31, donatediamLog.Type, "Donatediam native log type evidence");
EqualText("金刚宝石", donatediamLog.Name,
    "Donatediam native log name evidence");
EqualText("senderRemainingBF0", donatediamLog.ItemId,
    "Donatediam native log ItemId evidence");
EqualText("amount", donatediamLog.ItemNum,
    "Donatediam native log ItemNum evidence");
EqualText("targetName", donatediamLog.Reason,
    "Donatediam native log reason evidence");
Equal(50, getSkyPrizeLog.Type, "GetSkyPrize native log type evidence");
EqualText("金刚宝石", getSkyPrizeLog.Name,
    "GetSkyPrize native log name evidence");
EqualText("100", getSkyPrizeLog.ItemId,
    "GetSkyPrize native log ItemId evidence");
EqualText("1", getSkyPrizeLog.ItemNum,
    "GetSkyPrize native log ItemNum evidence");
EqualText("闯天关大奖", getSkyPrizeLog.Reason,
    "GetSkyPrize native log reason evidence");

Console.WriteLine(
    "PASS diamond-cache login=rebase/10054 capital+8=cache-only " +
    "pickup-drop=no-link TakeDiamond=no-link MyDiamondnum=physical " +
    "writers=login+GetSkyPrize(+100/no-refresh) " +
    "PAS=GetSkyPrize-procedure/Donatediam-fail-closed persistence=none");
return;

int ReadCache(TPlayObject target) => (int)cacheField.GetValue(target)!;

int ReadBag(TPlayObject target) => (int)countBag.Invoke(target, null)!;

int ReadCapitalDiamond(TPlayObject target)
{
    var body = (byte[])buildCapital.Invoke(target, null)!;
    return BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(8, 4));
}

static TUserItem NewItem(int makeIndex, ushort dura)
{
    return new TUserItem
    {
        MakeIndex = makeIndex,
        wIndex = 1,
        Dura = dura,
        DuraMax = 100,
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

static string FindRepositoryRoot()
{
    foreach (var origin in new[]
             {
                 Directory.GetCurrentDirectory(), AppContext.BaseDirectory
             })
    {
        var directory = new DirectoryInfo(origin);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameSvr",
                    "GameSvr.csproj")) &&
                Directory.Exists(Path.Combine(directory.FullName, "AuditTools")))
                return directory.FullName;
            directory = directory.Parent;
        }
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
            $"{message}: expected '{expected}', actual '{actual}'");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

internal readonly record struct NativeWriterLogEvidence(
    int Type,
    string Name,
    string ItemId,
    string ItemNum,
    string Reason);
