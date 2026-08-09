using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.ObjectManager = new ObjectManager();
M2Share.ProcessMsgCriticalSection = new object();
M2Share.LogMsgCriticalSection = new object();
M2Share.g_MonSayMsgList = new Dictionary<string, IList<TMonSayMsg>>();

var engine = new UserEngine();
M2Share.UserEngine = engine;
var getHumData = typeof(UserEngine).GetMethod("GetHumData",
    BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new MissingMethodException(typeof(UserEngine).FullName, "GetHumData");

var nativeCreateDate = new DateTime(2020, 7, 15, 13, 14, 15,
    DateTimeKind.Unspecified).ToOADate();
const uint forceLvBits = 0xA5A500C8u;
const uint forceExpBits = 0x7FFFFFFEu;
const uint fightPointsBits = 0xFEDCBA98u;
const uint sfLevelBits = 0x80000001u;
var player = new TPlayObject { m_dCreateDate = 123.25 };
var loadedRecord = CreateRecord(nativeCreateDate);
loadedRecord.Data.ForceLv = unchecked((int)forceLvBits);
loadedRecord.Data.ForceExp = unchecked((int)forceExpBits);
loadedRecord.Data.FightPoints = unchecked((int)fightPointsBits);
loadedRecord.Data.sfLevel = unchecked((int)sfLevelBits);
InvokeGetHumData(getHumData, engine, player, loadedRecord);
Equal(nativeCreateDate, player.m_dCreateDate,
    "GetHumData did not load Header.dCreateDate into the player");
EqualBits(forceLvBits, player.m_nForceLv,
    "GetHumData did not load THumInfoData.ForceLv bit-for-bit");
EqualBits(forceExpBits, player.m_nForceExp,
    "GetHumData did not load THumInfoData.ForceExp bit-for-bit");
EqualBits(fightPointsBits, player.m_nFightPoints,
    "GetHumData did not load THumInfoData.FightPoints bit-for-bit");
EqualBits(sfLevelBits, player.m_nSfLevel,
    "GetHumData did not load THumInfoData.sfLevel bit-for-bit");

var bridge = new PasApiBridge { CurrentPlayer = player };
Assert(bridge.GetPlayerProperty("GetCreateTime", out var pasValue),
    "PAS GetCreateTime property was rejected");
EqualPasType(PasValueType.Double, pasValue.Type,
    "PAS GetCreateTime did not preserve Delphi TDateTime as Double");
Equal(nativeCreateDate, pasValue.AsDouble(),
    "PAS GetCreateTime changed the native OA date");

var saveRecord = new THumDataInfo();
player.MakeSaveRcd(ref saveRecord);
Equal(nativeCreateDate, saveRecord.Header.dCreateDate,
    "MakeSaveRcd did not write the player's native create date to the record header");
EqualBits(forceLvBits, saveRecord.Data.ForceLv,
    "MakeSaveRcd did not preserve ForceLv high/low-word bits");
EqualBits(forceExpBits, saveRecord.Data.ForceExp,
    "MakeSaveRcd did not preserve ForceExp bits");
EqualBits(fightPointsBits, saveRecord.Data.FightPoints,
    "MakeSaveRcd did not preserve negative FightPoints bits");
EqualBits(sfLevelBits, saveRecord.Data.sfLevel,
    "MakeSaveRcd did not preserve negative sfLevel bits");

foreach (var invalidDate in new[]
         {
             double.NaN,
             double.PositiveInfinity,
             double.NegativeInfinity,
             double.MaxValue,
             double.MinValue
         })
{
    var rejectedPlayer = new TPlayObject { m_dCreateDate = 456.75 };
    var invalidRecord = CreateRecord(invalidDate);
    AssertThrowsInvalidData(
        () => InvokeGetHumData(getHumData, engine, rejectedPlayer, invalidRecord),
        $"GetHumData accepted invalid OA date {invalidDate:R}");
    Equal(456.75, rejectedPlayer.m_dCreateDate,
        $"invalid OA date {invalidDate:R} mutated the player before rejection");
}

TestSourceContracts();

Console.WriteLine(
    "PASS GetCreateTime=db user_index.CreateDate->OA Double->Header->player->PAS->save " +
    "force-fields=THumInfoData->runtime->THumInfoData bit-exact " +
    "invalid=NaN+Infinity+OA-range fail-closed update=preserves-create-date");
return;

static THumDataInfo CreateRecord(double createDate)
{
    var record = new THumDataInfo();
    record.Header.dCreateDate = createDate;
    record.Data.sCharName = "CreateTimeAudit";
    record.Data.sCurMap = "0";
    return record;
}

static void InvokeGetHumData(MethodInfo method, UserEngine engine,
    TPlayObject player, THumDataInfo record)
{
    try
    {
        method.Invoke(engine, new object[] { player, record });
    }
    catch (TargetInvocationException ex) when (ex.InnerException != null)
    {
        throw ex.InnerException;
    }
}

static void TestSourceContracts()
{
    var root = FindRepositoryRoot();
    var dbSource = File.ReadAllText(Path.Combine(root, "DBSvr", "DB", "impl",
        "MySqlPlayDataService.cs"));
    var engineSource = File.ReadAllText(Path.Combine(root, "GameSvr", "UsrSystem",
        "UsrEngn.cs"));
    var playerSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.cs"));
    var bridgeSource = File.ReadAllText(Path.Combine(root, "GameSvr", "ScriptSystem",
        "PasEngine", "PasApiBridge.cs"));

    RequireMatches(dbSource,
        @"SELECT\s+d\.Data,\s*d\.ScriptData,\s*i\.Job,\s*i\.Sex,\s*i\.CreateDate",
        1, "Get-by-index SELECT does not load user_index.CreateDate");
    RequireMatches(dbSource,
        @"SELECT\s+d\.Idx,\s*d\.Data,\s*d\.ScriptData,\s*i\.Job,\s*i\.Sex,\s*i\.CreateDate",
        1, "Get-by-name SELECT does not load user_index.CreateDate");
    RequireMatches(dbSource,
        @"reader\.GetDateTime\(createDateOrdinal\)[\s\S]{0,160}?\.ToOADate\(\)" +
        @"[\s\S]{0,600}?humanRcd\.Header\.dCreateDate\s*=\s*nativeCreateDate",
        1, "DB metadata does not convert CreateDate to OA date and store it in Header");
    RequireMatches(dbSource,
        @"reader\.IsDBNull\(createDateOrdinal\)[\s\S]{0,180}?return false;",
        1, "NULL user_index.CreateDate is not fail-closed");

    var updateBody = ExtractMethodBody(dbSource, "public bool Update(int nIndex");
    Reject(updateBody, "CreateDate", "character UPDATE mutates user_index.CreateDate");
    var deleteFlagBody = ExtractMethodBody(dbSource,
        "public bool ResetDeletedFlagByChrName");
    Reject(deleteFlagBody, "CreateDate",
        "deleted-flag UPDATE mutates user_index.CreateDate");
    var saveBlobBody = ExtractMethodBody(dbSource, "public bool SaveBlob(int idx");
    Reject(saveBlobBody, "user_index", "blob save writes user_index metadata");
    RejectUpdateOfCreateDate(dbSource);

    RequireMatches(engineSource,
        @"DateTime\.FromOADate\(HumanRcd\.Header\.dCreateDate\)[\s\S]{0,300}?" +
        @"PlayObject\.m_dCreateDate\s*=\s*HumanRcd\.Header\.dCreateDate",
        1, "GetHumData native create-date validation/load contract");
    RequireMatches(playerSource,
        @"MakeSaveRcd\(ref THumDataInfo HumanRcd\)[\s\S]{0,180}?" +
        @"HumanRcd\.Header\.dCreateDate\s*=\s*m_dCreateDate",
        1, "MakeSaveRcd native create-date writeback contract");
    RequireMatches(bridgeSource,
        @"case\s+""getcreatetime"":\s*result\s*=\s*" +
        @"PasValue\.FromDouble\(CurrentPlayer\.m_dCreateDate\)",
        1, "PAS GetCreateTime native Double contract");
}

static string ExtractMethodBody(string source, string signature)
{
    var start = source.IndexOf(signature, StringComparison.Ordinal);
    if (start < 0) Fail("source method was not found: " + signature);
    var open = source.IndexOf('{', start);
    if (open < 0) Fail("source method has no body: " + signature);

    var depth = 0;
    for (var index = open; index < source.Length; index++)
    {
        if (source[index] == '{') depth++;
        else if (source[index] == '}' && --depth == 0)
            return source.Substring(open, index - open + 1);
    }
    Fail("source method body is incomplete: " + signature);
    return string.Empty;
}

static void RejectUpdateOfCreateDate(string source)
{
    var sqlStrings = Regex.Matches(source, "@\"(?:\"\"|[^\"])*\"|\"(?:\\\\.|[^\"\\\\])*\"",
            RegexOptions.CultureInvariant)
        .Select(match => match.Value)
        .Where(value => Regex.IsMatch(value, @"UPDATE\s+mir3\.user_index",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        .ToArray();
    Assert(sqlStrings.Length >= 2,
        "source contract did not find the expected user_index UPDATE statements");
    foreach (var sql in sqlStrings)
    {
        Assert(!Regex.IsMatch(sql, @"(?<![A-Za-z0-9_])CreateDate\s*=",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "user_index UPDATE writes native CreateDate");
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
        Fail($"{message}: expected {expected} match(es), actual {actual}");
}

static void Reject(string source, string value, string message)
{
    if (source.Contains(value, StringComparison.OrdinalIgnoreCase)) Fail(message);
}

static void AssertThrowsInvalidData(Action action, string message)
{
    try
    {
        action();
    }
    catch (InvalidDataException)
    {
        return;
    }
    catch (Exception ex)
    {
        Fail($"{message}: wrong exception {ex.GetType().Name}: {ex.Message}");
    }
    Fail(message);
}

static void Equal(double expected, double actual, string message)
{
    if (BitConverter.DoubleToInt64Bits(expected) != BitConverter.DoubleToInt64Bits(actual))
        Fail($"{message}: expected {expected:R}, actual {actual:R}");
}

static void EqualBits(uint expected, int actual, string message)
{
    if (expected != unchecked((uint)actual))
        Fail($"{message}: expected 0x{expected:X8}, actual 0x{unchecked((uint)actual):X8}");
}

static void EqualPasType(PasValueType expected, PasValueType actual, string message)
{
    if (expected != actual) Fail($"{message}: expected {expected}, actual {actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) Fail(message);
}

static void Fail(string message) => throw new InvalidOperationException(message);
