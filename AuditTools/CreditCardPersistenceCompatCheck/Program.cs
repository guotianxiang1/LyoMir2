using System.Reflection;
using System.Text.RegularExpressions;
using GameSvr;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig { sConnctionString = string.Empty };
M2Share.ObjectManager = new ObjectManager();
M2Share.LogStringList = new System.Collections.ArrayList();
M2Share.ProcessMsgCriticalSection = new object();

var service = CreateCreditCardService();
var combined = NewPlayer();
combined.m_CreditCard.Loaded = true;
combined.m_CreditCard.Dirty = true;
combined.m_CreditCard.Value = 11;
combined.m_CreditCard.GloryPointDirty = true;
combined.m_CreditCard.GloryPointValue = 22;
combined.m_CreditCard.GloryPointPeriod = 33;
Assert(!service.TrySaveDue(combined, 44, true),
    "combined save unexpectedly succeeded without a database connection");
Assert(combined.m_CreditCard.Dirty,
    "CreditCard dirty state was cleared after a connection failure");
Assert(!combined.m_CreditCard.GloryPointDirty,
    "GloryPoint dirty state was restored after a connection failure");
Equal(2, combined.m_MsgList.Count(entry =>
        entry.wIdent == Grobal2.RM_LINGFU_CHANGED),
    "independent CreditCard/GloryPoint pre-save refresh count");

var unloadedGlory = NewPlayer();
unloadedGlory.m_CreditCard.Loaded = false;
unloadedGlory.m_CreditCard.GloryPointDirty = true;
unloadedGlory.m_CreditCard.GloryPointValue = 55;
unloadedGlory.m_CreditCard.GloryPointPeriod = 66;
Assert(!service.TrySaveDue(unloadedGlory, 77, true),
    "unloaded GloryPoint save unexpectedly succeeded");
Assert(!unloadedGlory.m_CreditCard.GloryPointDirty,
    "Loaded=false blocked or restored GloryPoint dirty consumption");
Equal(1, unloadedGlory.m_MsgList.Count(entry =>
        entry.wIdent == Grobal2.RM_LINGFU_CHANGED),
    "unloaded GloryPoint pre-save refresh count");

var unloadedCredit = NewPlayer();
unloadedCredit.m_CreditCard.Loaded = false;
unloadedCredit.m_CreditCard.Dirty = true;
unloadedCredit.m_CreditCard.Value = 88;
Assert(service.TrySaveDue(unloadedCredit, 99, true),
    "unloaded clean-Glory account should have no persistence work");
Assert(unloadedCredit.m_CreditCard.Dirty,
    "Loaded=false ordinary CreditCard dirty guard was removed");
Equal(0, unloadedCredit.m_MsgList.Count,
    "unloaded ordinary CreditCard queued a refresh");

var throttled = NewPlayer();
throttled.m_CreditCard.LastSaveTick = 100;
throttled.m_CreditCard.GloryPointPeriod = CurrentGloryPointPeriod();
Assert(service.TrySaveDue(throttled, 10_099, false),
    "sub-10-second clean save unexpectedly failed");
Equal(100, throttled.m_CreditCard.LastSaveTick,
    "sub-10-second save advanced the timer");
Assert(service.TrySaveDue(throttled, 10_100, false),
    "10-second clean save unexpectedly failed");
Equal(10_100, throttled.m_CreditCard.LastSaveTick,
    "10-second save did not advance the timer");

var forced = NewPlayer();
forced.m_CreditCard.LastSaveTick = 123;
forced.m_CreditCard.GloryPointPeriod = -1;
forced.m_CreditCard.GloryPointValue = 77;
Assert(service.TrySaveDue(forced, 99_999, true),
    "forced clean save unexpectedly failed");
Equal(123, forced.m_CreditCard.LastSaveTick,
    "forced save advanced the periodic timer");
Equal(-1, forced.m_CreditCard.GloryPointPeriod,
    "forced save performed a GloryPoint phase check");
Equal(77, forced.m_CreditCard.GloryPointValue,
    "forced save cleared GloryPoint during an out-of-band phase check");
Equal(0, forced.m_MsgList.Count,
    "forced clean save queued a phase refresh");

var phaseChange = NewPlayer();
phaseChange.m_CreditCard.LastSaveTick = 0;
phaseChange.m_CreditCard.GloryPointPeriod = -1;
phaseChange.m_CreditCard.GloryPointValue = 88;
Assert(service.TrySaveDue(phaseChange, 10_000, false),
    "periodic phase-only save unexpectedly failed");
Equal(CurrentGloryPointPeriod(), phaseChange.m_CreditCard.GloryPointPeriod,
    "periodic save did not advance the GloryPoint phase");
Equal(0, phaseChange.m_CreditCard.GloryPointValue,
    "periodic phase change did not clear GloryPoint");
Assert(!phaseChange.m_CreditCard.GloryPointDirty,
    "periodic phase change introduced a GloryPoint save");
Equal(1, phaseChange.m_MsgList.Count(entry =>
        entry.wIdent == Grobal2.RM_LINGFU_CHANGED),
    "periodic phase change refresh count");

var root = FindRepositoryRoot();
var source = File.ReadAllText(Path.Combine(root, "GameSvr", "Services",
    "NativeCreditCardService.cs"));
var load = ExtractMethod(source, "public bool TryLoad(",
    "public bool TrySaveDue(", "TryLoad");
var save = ExtractMethod(source, "public bool TrySaveDue(",
    "private void EnsureSchema()", "TrySaveDue");
var glorySave = ExtractMethod(source, "private static bool SaveGloryPoint(",
    "private static MySqlConnection OpenConnection()", "SaveGloryPoint");

Reject(load, "EnsureSchema();", "per-player load executes schema DDL");
Reject(save, "EnsureSchema();", "per-player save executes schema DDL");
Assert(source.IndexOf("service.EnsureSchema();", StringComparison.Ordinal) >= 0,
    "global CreditCard initialization no longer prepares the schema");

var creditSelect = RequiredIndex(load, "command.CommandText = SelectSql;",
    "ordinary CreditCard SELECT");
var creditCatch = RequiredIndex(load, "LogError(\"CreditCard登录加载\", ex);",
    "ordinary CreditCard SELECT error continuation");
var glorySelect = RequiredIndex(load, "command.CommandText = SelectGloryPointSql;",
    "GloryPoint SELECT");
Assert(creditSelect < creditCatch && creditCatch < glorySelect,
    "ordinary CreditCard SELECT failure does not continue to GloryPoint SELECT");
Assert(RequiredIndex(load, "account.Loaded = true;", "ordinary Loaded assignment") <
       creditCatch, "ordinary CreditCard Loaded flag is not scoped to SELECT success");

Reject(save, "if (!account.Loaded) return true;",
    "Loaded guard blocks independent GloryPoint persistence");
RequireMatches(save,
    "creditCardDirty\\s*=\\s*account\\.Loaded\\s*&&\\s*account\\.Dirty;",
    1, "ordinary CreditCard dirty state lost its Loaded guard");
var creditBlock = ExtractControlBlock(save, "if (creditCardDirty)",
    "CreditCard save branch");
var gloryBlock = ExtractControlBlock(save, "if (gloryPointDirty)",
    "GloryPoint save branch");
Assert(save.IndexOf("if (gloryPointDirty)", StringComparison.Ordinal) >=
       save.IndexOf("if (creditCardDirty)", StringComparison.Ordinal) + creditBlock.Length,
    "GloryPoint save is nested in the ordinary CreditCard branch");
var clearDirty = RequiredIndex(gloryBlock, "account.GloryPointDirty = false;",
    "GloryPoint pre-save dirty clear");
var refresh = RequiredIndex(gloryBlock, "player.RefreshNativeLingFu();",
    "GloryPoint pre-save 10054 refresh");
var open = RequiredIndex(gloryBlock, "using var connection = OpenConnection();",
    "GloryPoint database connection");
var execute = RequiredIndex(gloryBlock, "SaveGloryPoint(connection",
    "GloryPoint database write");
Assert(clearDirty < refresh && refresh < open && open < execute,
    "GloryPoint dirty/10054/connection/SQL order differs from the original");
Reject(gloryBlock.Substring(open), "GloryPointDirty = true",
    "GloryPoint dirty state is restored after a database failure");
RequireMatches(gloryBlock,
    "account\\.GloryPointDirtyVersion\\s*==\\s*gloryPointDirtyVersion\\s*" +
    "&&\\s*account\\.GloryPointPeriod\\s*==\\s*gloryPointPeriod",
    1, "GloryPoint pre-clear does not protect a newer mutation/version");
Assert(creditBlock.Contains("using var connection = OpenConnection();",
        StringComparison.Ordinal),
    "ordinary CreditCard branch lost its independent connection attempt");

RequireMatches(glorySave,
    "exactlyOneRow\\s*=\\s*!reader\\.Read\\(\\);", 1,
    "GloryPoint SELECT does not distinguish exactly one row");
RequireMatches(glorySave,
    "command\\.CommandText\\s*=\\s*exactlyOneRow\\s*" +
    "\\?\\s*UpdateGloryPointSql\\s*:\\s*InsertGloryPointSql;", 1,
    "GloryPoint does not update exactly one row and insert otherwise");

Console.WriteLine(
    "PASS CreditCard ordinary=Loaded-guard/retry GloryPoint=Loaded-independent/" +
    "clear+10054-before-connect/no-restore load=credit-error-continues-glory " +
    "upsert=one-row-update-otherwise-insert schema=global-only " +
    "timer=unsigned-10s force=no-timer/no-phase phase=clear+10054/no-dirty " +
    "version=newer-mutation-preserved");
return;

static int CurrentGloryPointPeriod()
{
    var now = DateTime.Now;
    var closingDay = now.Day <= 15
        ? 15
        : DateTime.DaysInMonth(now.Year, now.Month);
    return unchecked((int)new DateTime(now.Year, now.Month, closingDay).ToOADate());
}

static TPlayObject NewPlayer() => new()
{
    m_sUserID = "credit-test",
    m_sCharName = "credit-role"
};

static NativeCreditCardService CreateCreditCardService()
{
    var constructor = typeof(NativeCreditCardService).GetConstructor(
        BindingFlags.Instance | BindingFlags.NonPublic, null,
        new[] { typeof(bool), typeof(bool), typeof(string), typeof(byte[]) }, null);
    Assert(constructor != null, "NativeCreditCardService constructor is missing");
    return (NativeCreditCardService)constructor.Invoke(
        new object[] { true, true, string.Empty, new byte[5] });
}

static string ExtractMethod(string source, string startMarker, string endMarker,
    string description)
{
    var start = RequiredIndex(source, startMarker, description + " start");
    var end = RequiredIndex(source, endMarker, description + " end");
    Assert(end > start, description + " source boundary is invalid");
    return source.Substring(start, end - start);
}

static string ExtractControlBlock(string source, string marker, string description)
{
    var markerIndex = RequiredIndex(source, marker, description);
    var open = source.IndexOf('{', markerIndex);
    Assert(open >= 0, description + " opening brace is missing");
    var depth = 0;
    for (var i = open; i < source.Length; i++)
    {
        if (source[i] == '{') depth++;
        else if (source[i] == '}' && --depth == 0)
            return source.Substring(markerIndex, i - markerIndex + 1);
    }
    throw new InvalidOperationException(description + " closing brace is missing");
}

static int RequiredIndex(string source, string value, string description)
{
    var index = source.IndexOf(value, StringComparison.Ordinal);
    if (index < 0) throw new InvalidOperationException(description + " is missing");
    return index;
}

static void RequireMatches(string source, string pattern, int expected, string message)
{
    var actual = Regex.Matches(source, pattern,
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase).Count;
    if (actual != expected)
        throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
}

static void Reject(string source, string value, string message)
{
    if (source.Contains(value, StringComparison.Ordinal))
        throw new InvalidOperationException(message);
}

static void Equal(int expected, int actual, string message)
{
    if (expected != actual)
        throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
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
    throw new DirectoryNotFoundException("GameSvr/GameSvr.csproj was not found");
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
    var share = Path.Combine(Path.GetFullPath(Path.Combine(runtimeDirectory, "..")), "Share");
    Directory.CreateDirectory(share);
    File.WriteAllText(Path.Combine(share, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(share, "ServerData.ini"),
        "[Integer]" + Environment.NewLine);
}
