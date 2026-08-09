using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using DBSvr;
using GameSvr;
using ProtoBuf;
using SystemModule;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.ObjectManager = new ObjectManager();
M2Share.ProcessMsgCriticalSection = new object();
M2Share.LogMsgCriticalSection = new object();
M2Share.g_MonSayMsgList = new Dictionary<string, IList<TMonSayMsg>>();

CheckProtoContract();
CheckDbValueCompatibility();
CheckLoginLoad();
CheckSourceContract();

Console.WriteLine(
    "PASS NativeUserId=user_index snapshot->THumDataInfo tag7->TPlayObject mail cache " +
    "legacy=zero null/overflow=zero");
return;

static void CheckProtoContract()
{
    var property = typeof(THumDataInfo).GetProperty(nameof(THumDataInfo.NativeUserId))
        ?? throw new MissingMemberException(nameof(THumDataInfo.NativeUserId));
    var member = property.GetCustomAttribute<ProtoMemberAttribute>()
        ?? throw new InvalidOperationException("NativeUserId has no ProtoMember attribute");
    Equal(7, member.Tag, "NativeUserId protobuf tag");

    const long expected = long.MaxValue - 12345;
    var current = new THumDataInfo { NativeUserId = expected };
    current.PrepareForTransport();
    using var currentStream = new MemoryStream();
    Serializer.Serialize(currentStream, current);
    currentStream.Position = 0;
    Equal(expected, Serializer.Deserialize<THumDataInfo>(currentStream).NativeUserId,
        "64-bit NativeUserId protobuf roundtrip");

    using var legacyStream = new MemoryStream();
    var legacyData = new THumDataInfo();
    legacyData.PrepareForTransport();
    Serializer.Serialize(legacyStream, new LegacyHumDataInfo
    {
        Header = legacyData.Header,
        Data = legacyData.Data
    });
    legacyStream.Position = 0;
    Equal(0L, Serializer.Deserialize<THumDataInfo>(legacyStream).NativeUserId,
        "legacy THumDataInfo defaults NativeUserId to zero");
}

static void CheckDbValueCompatibility()
{
    var normalize = typeof(MySqlPlayDataService).GetMethod("NormalizeNativeUserId",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(MySqlPlayDataService).FullName,
            "NormalizeNativeUserId");

    Equal(0L, InvokeNormalize(normalize, DBNull.Value), "NULL UserId fallback");
    Equal(0L, InvokeNormalize(normalize, ulong.MaxValue), "overflow UserId fallback");
    Equal(long.MaxValue, InvokeNormalize(normalize, long.MaxValue),
        "signed 64-bit UserId preserved");
    Equal(4294967297L, InvokeNormalize(normalize, 4294967297UL),
        "UserId is not truncated to 32 bits");
}

static void CheckLoginLoad()
{
    var getHumData = typeof(UserEngine).GetMethod("GetHumData",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(UserEngine).FullName, "GetHumData");
    var recipientField = typeof(TPlayObject).GetField("_nativeMailRecipientId",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(TPlayObject).FullName,
            "_nativeMailRecipientId");

    const long expected = 4294967297L;
    var player = new TPlayObject();
    var record = NewRecord(expected);
    InvokeGetHumData(getHumData, new UserEngine(), player, record);
    Equal(expected, (long)recipientField.GetValue(player)!,
        "login load did not seed the native mail recipient cache");

    recipientField.SetValue(player, 99L);
    InvokeGetHumData(getHumData, new UserEngine(), player, NewRecord(0));
    Equal(0L, (long)recipientField.GetValue(player)!,
        "legacy zero did not clear a recycled player cache");
}

static void CheckSourceContract()
{
    var root = FindRepositoryRoot();
    var db = File.ReadAllText(Path.Combine(root, "DBSvr", "DB", "impl",
        "MySqlPlayDataService.cs"));
    var packet = File.ReadAllText(Path.Combine(root, "SystemModule", "Packet",
        "THumDataInfo.cs"));
    var engine = File.ReadAllText(Path.Combine(root, "GameSvr", "UsrSystem",
        "UsrEngn.cs"));
    var player = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.NativeUserId.cs"));

    RequireMatches(db,
        @"SELECT\s+d\.Data,\s*d\.ScriptData,\s*i\.Job,\s*i\.Sex,\s*" +
        @"i\.CreateDate,\s*i\.UserId\s+FROM\s+mir3\.user_data",
        1, "Get-by-index does not load UserId with character data and metadata");
    RequireMatches(db,
        @"SELECT\s+d\.Idx,\s*d\.Data,\s*d\.ScriptData,\s*i\.Job,\s*i\.Sex,\s*" +
        @"i\.CreateDate,\s*i\.UserId\s+FROM\s+mir3\.user_data",
        1, "Get-by-name does not load UserId with character data and metadata");
    RequireMatches(db,
        @"humanRcd\.NativeUserId\s*=\s*NormalizeNativeUserId\(\s*" +
        @"reader\.GetValue\(reader\.GetOrdinal\(""UserId""\)\)\)",
        1, "DB metadata does not populate THumDataInfo.NativeUserId");
    RequireMatches(packet,
        @"\[ProtoMember\(7\)\]\s*public\s+long\s+NativeUserId",
        1, "NativeUserId protobuf tag 7 source contract");
    RequireMatches(engine,
        @"PlayObject\.LoadNativeMailRecipientId\(HumanRcd\.NativeUserId\)",
        1, "login load does not propagate NativeUserId to TPlayObject");
    RequireMatches(player,
        @"_nativeMailRecipientId\s*=\s*recipientId",
        1, "TPlayObject cache assignment is missing");
}

static THumDataInfo NewRecord(long userId)
{
    var record = new THumDataInfo { NativeUserId = userId };
    record.Header.dCreateDate = new DateTime(2020, 1, 2).ToOADate();
    record.Data.sCharName = "NativeUserIdAudit";
    record.Data.sCurMap = "0";
    return record;
}

static long InvokeNormalize(MethodInfo method, object value)
{
    return (long)method.Invoke(null, new[] { value })!;
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
    throw new DirectoryNotFoundException("Repository root was not found");
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

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message}: expected={expected}, actual={actual}");
}

[ProtoContract]
sealed class LegacyHumDataInfo
{
    [ProtoMember(1)] public TRecordHeader Header { get; set; }
    [ProtoMember(2)] public THumInfoData Data { get; set; }
}
