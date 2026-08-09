extern alias dbsvr;

using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;
using NativeHumanDataCodec = global::DBSvr.Core.NativeHumanDataCodec;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
PrepareRuntimeConfig();
TestNativeRecordRoundTrip();
TestProtobufRoundTrip();
TestLoginAndSaveMappings();
TestPasSemantics();
TestSourceContracts();

Console.WriteLine(
    "PASS ActivePoint raw=0x0608 protobuf=71 login/save=native " +
    "PAS=Get/GetTmp/Inc/Dec Int32=exact V10:8=unused");
return;

static void TestNativeRecordRoundTrip()
{
    const int dataOffset = 0x0608;
    const int physicalOffset = dataOffset + 8;
    const int originalValue = -123456789;
    const int updatedValue = 7654321;
    const int beforeSentinel = unchecked((int)0x11223344);
    const int afterSentinel = unchecked((int)0x55667788);

    var blob = new byte[NativeHumanDataCodec.DataRecordSize + 8];
    BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(4, 4),
        NativeHumanDataCodec.DataRecordSize);
    var raw = blob.AsSpan(8);
    raw[0x3E] = 1;
    BinaryPrimitives.WriteInt32LittleEndian(raw.Slice(dataOffset - 4, 4), beforeSentinel);
    BinaryPrimitives.WriteInt32LittleEndian(raw.Slice(dataOffset, 4), originalValue);
    BinaryPrimitives.WriteInt32LittleEndian(raw.Slice(dataOffset + 4, 4), afterSentinel);

    Equal(originalValue,
        BinaryPrimitives.ReadInt32LittleEndian(blob.AsSpan(physicalOffset, 4)),
        "physical ActivePoint offset");
    Assert(NativeHumanDataCodec.TryDecode(blob, null, out var decoded, out var error),
        "native decode failed: " + error);
    Equal(originalValue, decoded.Data.nActivePoint, "native ActivePoint decode");

    decoded.Data.nActivePoint = updatedValue;
    Assert(NativeHumanDataCodec.TryEncode(decoded, out var encoded, out var script, out error),
        "native encode failed: " + error);
    Assert(NativeHumanDataCodec.TryDecode(encoded, script, out var roundTrip, out error),
        "native round-trip decode failed: " + error);
    Equal(updatedValue, roundTrip.Data.nActivePoint, "native ActivePoint round trip");
    Equal(updatedValue,
        BinaryPrimitives.ReadInt32LittleEndian(roundTrip.NativeData.AsSpan(dataOffset, 4)),
        "native ActivePoint raw write");
    Equal(beforeSentinel,
        BinaryPrimitives.ReadInt32LittleEndian(roundTrip.NativeData.AsSpan(dataOffset - 4, 4)),
        "native preceding bytes preservation");
    Equal(afterSentinel,
        BinaryPrimitives.ReadInt32LittleEndian(roundTrip.NativeData.AsSpan(dataOffset + 4, 4)),
        "native following bytes preservation");
}

static void TestProtobufRoundTrip()
{
    var source = new THumDataInfo();
    source.Data.nActivePoint = -20260718;
    source.PrepareForTransport();
    var payload = ProtoBufDecoder.Serialize(source);
    var decoded = ProtoBufDecoder.DeSerialize<THumDataInfo>(payload);
    Assert(decoded?.Data != null, "protobuf THumDataInfo decode failed");
    Equal(source.Data.nActivePoint, decoded.Data.nActivePoint,
        "protobuf ActivePoint round trip");
}

static void TestLoginAndSaveMappings()
{
    M2Share.g_Config = new GameSvrConfig();
    M2Share.ObjectManager = new ObjectManager();
    var userEngine = new UserEngine();
    M2Share.UserEngine = userEngine;

    var record = new THumDataInfo();
    record.Header.dCreateDate = DateTime.Today.ToOADate();
    record.Data.nActivePoint = -77;
    var player = new TPlayObject();
    var load = typeof(UserEngine).GetMethod("GetHumData",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(load != null, "UserEngine.GetHumData was not found");
    var arguments = new object[] { player, record };
    load!.Invoke(userEngine, arguments);
    Equal(-77, player.m_nActivePoint, "login ActivePoint mapping");

    player.m_nActivePoint = 808;
    var save = new THumDataInfo();
    player.MakeSaveRcd(ref save);
    Equal(808, save.Data.nActivePoint, "save ActivePoint mapping");
}

static void TestPasSemantics()
{
    var tempDirectory = Path.Combine(Path.GetTempPath(),
        "active-point-check-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempDirectory);
    try
    {
        var activityFile = Path.Combine(tempDirectory, "PlayerActivePoint.xml");
        File.WriteAllText(activityFile, """
            <Describle>
              <Jobs>
                <Job Id="0">
                  <Lucks><Luck LuckValue="0" Value="2"/></Lucks>
                </Job>
              </Jobs>
            </Describle>
            """);
        Assert(NativeActivityPointManager.TryLoad(activityFile,
            out var activity, out var error), "activity configuration failed: " + error);
        M2Share.ActivityPointManager = activity;

        var player = new TPlayObject { m_btJob = 0 };
        player.m_ScriptVVars[10008] = 908;
        var bridge = new PasApiBridge { CurrentPlayer = player };
        var one = Values(1);

        player.m_nActivePoint = int.MaxValue - 1;
        Assert(bridge.CallPlayerFunc("IncActivePoint", one, out var result),
            "IncActivePoint function was not dispatched");
        Equal(1, result.AsInt(), "IncActivePoint cap return value");
        Equal(int.MaxValue, player.m_nActivePoint, "IncActivePoint exact cap");

        Assert(bridge.CallPlayerFunc("IncActivePoint", one, out result),
            "IncActivePoint overflow function was not dispatched");
        Equal(1, result.AsInt(), "IncActivePoint overflow return value");
        Equal(int.MinValue, player.m_nActivePoint, "IncActivePoint overflow wrap");

        player.m_nActivePoint = 10;
        Assert(bridge.CallPlayerFunc("IncActivePoint", Values(-3), out result),
            "negative IncActivePoint was not dispatched");
        Equal(-3, result.AsInt(), "negative IncActivePoint return value");
        Equal(7, player.m_nActivePoint, "negative IncActivePoint balance");

        foreach (var value in new[] { 0, -4 })
        {
            Assert(bridge.CallPlayerFunc("DecActivePoint", Values(value), out result),
                "non-positive DecActivePoint was not dispatched");
            Equal(7, result.AsInt(), "non-positive DecActivePoint return value");
            Equal(7, player.m_nActivePoint, "non-positive DecActivePoint balance");
        }

        Assert(bridge.CallPlayerFunc("DecActivePoint", Values(10), out result),
            "over-balance DecActivePoint was not dispatched");
        Equal(-3, result.AsInt(), "over-balance DecActivePoint return value");
        Equal(-3, player.m_nActivePoint, "over-balance DecActivePoint balance");

        player.m_nActivePoint = int.MaxValue;
        Assert(bridge.CallPlayerFunc("GetTmpActivePoint", Values(), out result),
            "GetTmpActivePoint was not dispatched");
        Equal(2, result.AsInt(), "GetTmpActivePoint value");
        Assert(bridge.CallPlayerFunc("GetActivePoint", Values(), out result),
            "GetActivePoint was not dispatched");
        Equal(unchecked(int.MaxValue + 2), result.AsInt(),
            "GetActivePoint Int32 wrap");

        M2Share.ActivityPointManager = null;
        player.m_nActivePoint = -20260719;
        Assert(bridge.CallPlayerFunc("GetActivePoint", Values(), out result),
            "GetActivePoint without activity configuration was not dispatched");
        Equal(player.m_nActivePoint, result.AsInt(),
            "GetActivePoint without activity configuration permanent value");
        Assert(bridge.CallPlayerFunc("GetTmpActivePoint", Values(), out result),
            "GetTmpActivePoint without activity configuration was not dispatched");
        Equal(0, result.AsInt(),
            "GetTmpActivePoint without activity configuration value");
        player.m_nActivePoint = int.MaxValue;

        Assert(bridge.GetPlayerProperty("ActiveValue", out result),
            "ActiveValue property was not dispatched");
        Equal(int.MaxValue, result.AsInt(), "ActiveValue permanent value");
        Assert(bridge.CallPlayerMethod("IncActivePoint", Values(-1)),
            "IncActivePoint method was not dispatched");
        Equal(int.MaxValue - 1, player.m_nActivePoint,
            "IncActivePoint method permanent value");
        Equal(908, player.m_ScriptVVars[10008], "ActivePoint changed V[10,8]");
    }
    finally
    {
        M2Share.ActivityPointManager = null;
        Directory.Delete(tempDirectory, true);
    }
}

static void TestSourceContracts()
{
    var root = FindRepositoryRoot();
    var packet = Read(root, "SystemModule", "Packet", "THumDataInfo.cs");
    var codec = Read(root, "DBSvr", "Core", "NativeHumanDataCodec.cs");
    var player = Read(root, "GameSvr", "Players", "TPlayObject.cs");
    var loader = Read(root, "GameSvr", "UsrSystem", "UsrEngn.cs");
    var bridge = Read(root, "GameSvr", "ScriptSystem", "PasEngine", "PasApiBridge.cs");
    var integration = Read(root, "GameSvr", "ScriptSystem", "PasEngine", "PasIntegration.cs");

    Require(packet, "[ProtoMember(71)]", "ActivePoint protobuf tag");
    Require(packet, "public int nActivePoint", "ActivePoint transport field");
    Require(codec, "ActivePointOffset = 0x0608", "ActivePoint native offset");
    Require(player, "HumData.nActivePoint = m_nActivePoint", "ActivePoint save mapping");
    Require(loader, "PlayObject.m_nActivePoint = HumData.nActivePoint",
        "ActivePoint login mapping");
    Reject(bridge, "GetPlayerVar('V', 10, 8)", "ActivePoint V[10,8] read substitute");
    Reject(bridge, "SetPlayerVar('V', 10, 8", "ActivePoint V[10,8] write substitute");
    Reject(integration, "V[10, 8]", "ActivePoint V-variable documentation substitute");
    Reject(CaseBody(bridge, "getactivepoint", "incactivepoint"),
        "RejectUnsupportedNativeApi", "GetActivePoint fail-closed dispatch");
    Reject(CaseBody(bridge, "gettmpactivepoint", "getdynroomhumcnt"),
        "RejectUnsupportedNativeApi", "GetTmpActivePoint fail-closed dispatch");
}

static List<PasValue> Values(params int[] values) =>
    values.Select(PasValue.FromInt).ToList();

static string Read(string root, params string[] parts) =>
    File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));

static string CaseBody(string source, string caseName, string nextCaseName)
{
    var startToken = $"case \"{caseName}\":";
    var endToken = $"case \"{nextCaseName}\":";
    var start = source.IndexOf(startToken, StringComparison.OrdinalIgnoreCase);
    if (start < 0)
        throw new InvalidOperationException($"PAS case is missing: {caseName}");
    var end = source.IndexOf(endToken, start + startToken.Length,
        StringComparison.OrdinalIgnoreCase);
    if (end < 0)
        throw new InvalidOperationException($"PAS next case is missing: {nextCaseName}");
    return source[start..end];
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
    throw new DirectoryNotFoundException("Repository root was not found.");
}

static void Require(string source, string value, string message)
{
    if (!source.Contains(value, StringComparison.Ordinal))
        throw new InvalidOperationException(message + " is missing");
}

static void Reject(string source, string value, string message)
{
    if (source.Contains(value, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException(message + " is present");
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
