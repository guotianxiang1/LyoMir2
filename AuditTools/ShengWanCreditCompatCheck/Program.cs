extern alias dbsvr;

using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;
using NativeHumanDataCodec = global::DBSvr.Core.NativeHumanDataCodec;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
TestNativeRecordRoundTrip();
TestProtobufRoundTrip();
TestPasProperties();
TestSourceContracts();

Console.WriteLine(
    "PASS MyShengwan=native record=0xEC physical=0xF4 protobuf=60 " +
    "PAS=get/set RM_ABILITY mall=no-shengwan-debit CreditPoint=closed substitutes=0");
return;

static void TestNativeRecordRoundTrip()
{
    const int dataOffset = 0xEC;
    const int physicalOffset = dataOffset + 8;
    const int unrelatedOffset = 0xF4;
    const int unknownOffset = 0x104;
    const int originalValue = -123456789;
    const int updatedValue = 7654321;
    const int unrelatedSentinel = unchecked((int)0x55667788);

    var blob = new byte[NativeHumanDataCodec.DataRecordSize + 8];
    BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(4, 4),
        NativeHumanDataCodec.DataRecordSize);
    var raw = blob.AsSpan(8);
    raw[0x3E] = 1;
    BinaryPrimitives.WriteInt32LittleEndian(raw.Slice(dataOffset, 4), originalValue);
    BinaryPrimitives.WriteInt32LittleEndian(raw.Slice(unrelatedOffset, 4), unrelatedSentinel);
    raw[unknownOffset] = 0xA5;

    Equal(originalValue,
        BinaryPrimitives.ReadInt32LittleEndian(blob.AsSpan(physicalOffset, 4)),
        "physical MyShengwan offset");
    Assert(NativeHumanDataCodec.TryDecode(blob, null, out var decoded, out var error),
        "native decode failed: " + error);
    Equal(originalValue, decoded.Data.nShengWan, "native MyShengwan decode");
    Equal(unrelatedSentinel,
        BinaryPrimitives.ReadInt32LittleEndian(decoded.NativeData.AsSpan(unrelatedOffset, 4)),
        "native adjacent field before encode");
    Equal(0xA5, decoded.NativeData[unknownOffset], "native unknown byte before encode");

    decoded.Data.nShengWan = updatedValue;
    Assert(NativeHumanDataCodec.TryEncode(decoded, out var encoded, out var script, out error),
        "native encode failed: " + error);
    Assert(NativeHumanDataCodec.TryDecode(encoded, script, out var roundTrip, out error),
        "native round-trip decode failed: " + error);
    Equal(updatedValue, roundTrip.Data.nShengWan, "native MyShengwan round trip");
    Equal(updatedValue,
        BinaryPrimitives.ReadInt32LittleEndian(roundTrip.NativeData.AsSpan(dataOffset, 4)),
        "native MyShengwan raw write");
    Equal(unrelatedSentinel,
        BinaryPrimitives.ReadInt32LittleEndian(roundTrip.NativeData.AsSpan(unrelatedOffset, 4)),
        "native adjacent field preservation");
    Equal(0xA5, roundTrip.NativeData[unknownOffset], "native unknown byte preservation");
}

static void TestProtobufRoundTrip()
{
    var source = new THumDataInfo();
    source.Data.nShengWan = -20260715;
    source.PrepareForTransport();
    var payload = ProtoBufDecoder.Serialize(source);
    var decoded = ProtoBufDecoder.DeSerialize<THumDataInfo>(payload);
    Assert(decoded?.Data != null, "protobuf THumDataInfo decode failed");
    Equal(source.Data.nShengWan, decoded.Data.nShengWan,
        "protobuf MyShengwan round trip");
}

static void TestPasProperties()
{
    PrepareRuntimeConfig();
    M2Share.g_Config = new GameSvrConfig();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.ProcessMsgCriticalSection = new object();

    var player = new TPlayObject { m_nShengWan = 321, m_btCreditPoint = 88 };
    player.m_ScriptVVars[10009] = 909;
    player.m_ScriptVVars[10004] = 404;
    var bridge = new PasApiBridge { CurrentPlayer = player };

    Assert(bridge.GetPlayerProperty("MyShengwan", out var balance),
        "MyShengwan getter rejected native property");
    Equal(321, balance.AsInt(), "MyShengwan getter");
    Assert(bridge.SetPlayerProperty("MyShengWan", PasValue.FromInt(-77)),
        "MyShengwan setter rejected native property");
    Equal(-77, player.m_nShengWan, "MyShengwan setter");
    Equal(1, player.m_MsgList.Count, "MyShengwan setter refresh count");
    Equal(Grobal2.RM_ABILITY, player.m_MsgList[0].wIdent,
        "MyShengwan setter refresh message");
    Equal(909, player.m_ScriptVVars[10009], "MyShengwan changed V[10,9]");

    Assert(!bridge.GetPlayerProperty("CreditPoint", out var credit),
        "CreditPoint exposed a PAS property absent from the native TPlayer API");
    Assert(credit.Type == PasValueType.Nil, "CreditPoint failure did not return Nil");
    Equal(404, player.m_ScriptVVars[10004], "CreditPoint changed V[10,4]");
}

static void TestSourceContracts()
{
    var root = FindRepositoryRoot();
    var bridge = File.ReadAllText(Path.Combine(root, "GameSvr", "ScriptSystem", "PasEngine",
        "PasApiBridge.cs"));
    var integration = File.ReadAllText(Path.Combine(root, "GameSvr", "ScriptSystem", "PasEngine",
        "PasIntegration.cs"));
    var player = File.ReadAllText(Path.Combine(root, "GameSvr", "Players", "TPlayObject.cs"));
    var codec = File.ReadAllText(Path.Combine(root, "DBSvr", "Core", "NativeHumanDataCodec.cs"));
    var mall = File.ReadAllText(Path.Combine(root, "GameSvr", "Mall", "MallManager.cs"));

    Reject(bridge, "GetPlayerVar('V', 10, 9)", "PAS V[10,9] read substitute");
    Reject(bridge, "SetPlayerVar('V', 10, 9", "PAS V[10,9] write substitute");
    Reject(bridge, "GetPlayerVar('V', 10, 4)", "PAS V[10,4] read substitute");
    Reject(bridge, "SetPlayerVar('V', 10, 4", "PAS V[10,4] write substitute");
    Reject(integration, "V[10, 4] = CreditPoint", "CreditPoint V-variable documentation");
    Reject(mall, "GetPlayerVariable(player.m_ScriptVVars, 10, 9)",
        "mall MyShengwan V[10,9] balance substitute");
    Reject(mall, "SetPlayerVariable(player.m_ScriptVVars, 10, 9",
        "mall MyShengwan V[10,9] deduction substitute");

    RequireMatches(bridge,
        "case \\\"myshengwan\\\":\\s*result = PasValue\\.FromInt\\(CurrentPlayer\\.m_nShengWan\\)",
        1, "MyShengwan native getter");
    RequireMatches(bridge,
        "case \\\"myshengwan\\\":\\s*CurrentPlayer\\.SetShengWan\\(value\\.AsInt\\(\\)\\)",
        1, "MyShengwan native setter");
    RequireMatches(bridge,
        "case \\\"creditpoint\\\":[\\s\\S]{0,220}?RejectUnsupportedNativeApi\\(out result\\)",
        1, "CreditPoint PAS property fail-closed");
    RequireMatches(player,
        "void SetShengWan\\(int value\\)[\\s\\S]{0,180}?m_nShengWan = value;" +
        "[\\s\\S]{0,180}?SendMsg\\(this, Grobal2\\.RM_ABILITY, 0, 0, 0, 0, \\\"\\\"\\)",
        1, "MyShengwan setter RM_ABILITY notification");
    RequireMatches(codec, "ShengWanOffset = 0x00EC", 1,
        "native MyShengwan decoded-data offset");
    RequireMatches(codec,
        "ReadInt32LittleEndian[\\s\\S]{0,120}?ShengWanOffset", 1,
        "native MyShengwan decode mapping");
    RequireMatches(codec,
        "WriteInt32LittleEndian[\\s\\S]{0,120}?ShengWanOffset", 1,
        "native MyShengwan encode mapping");
    // 原来这里钉的是"商城货币类型 3 = 声望，扣 m_nShengWan"。字节推翻了它：
    // 原生商品表根本没有货币类型字段（加载器 sub_636D68 的 '$' 分隔 10 字段，
    // 跳表 0x636FD6，上限 0x636FC6 cmp eax,0xA），CM_DOSHOP 处理器 sub_6CB7E4 只有
    // 确认闸 + PAS @ClientBuy 调用，发货核心 sub_6CC420 里唯一一条余额加减是
    // 0x6CC504 add [esi+0xBD8],eax（发灵符）。商城从不扣声望。
    // 断言改成钉这一条：商城不许碰声望，付款只能走那个 fail-closed 的元宝结算闸。
    Reject(mall, "m_nShengWan", "mall still reads or debits ShengWan");
    Reject(mall, "SetShengWan", "mall still writes ShengWan");
    Reject(mall, "DeductCurrency", "mall still has a local currency deduction path");
    Assert(mall.Contains("if (!TrySettleYuanbaoPayment", StringComparison.Ordinal),
        "mall purchase no longer goes through the single settlement gate");
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
    throw new DirectoryNotFoundException(
        "Repository root containing GameSvr/GameSvr.csproj was not found.");
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
        Fail(message + " is present");
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
