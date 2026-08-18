using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using GameSvr;
using SystemModule;
using SystemModule.Packet;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
PrepareRuntimeConfig();
var root = FindRepositoryRoot();

var tests = new (string Name, Action Run)[]
{
    ("112 request codec", CheckOpenDealRequest),
    ("1112 response codec", CheckOpenDealResponse),
    ("1112 result text", CheckOpenDealDialogs),
    ("claim eligibility", CheckClaimEligibility),
    ("claim partial failure", CheckClaimPartialFailure),
    ("claim result text", CheckClaimDialogs),
    ("qualification authority closed", CheckQualificationAuthorityClosed),
    ("PAS and runtime dormant", CheckDormantBoundary)
};

foreach (var test in tests)
{
    try
    {
        test.Run();
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException($"{test.Name}: {ex.Message}", ex);
    }
}

Console.WriteLine(
    $"FirstUsedGiftCompatCheck PASS tests={tests.Length} " +
    "103/1103=credit 112/1112=open-deal claim=dormant authority=closed");
return;

void PrepareRuntimeConfig()
{
    var setupPath = Path.Combine(AppContext.BaseDirectory, "!Setup.txt");
    if (!File.Exists(setupPath) || new FileInfo(setupPath).Length == 0)
        File.WriteAllText(setupPath,
            "[Server]\r\nServerName=FirstGiftAudit\r\n", Encoding.ASCII);
    var commandPath = Path.Combine(AppContext.BaseDirectory, "Command.conf");
    if (!File.Exists(commandPath) || new FileInfo(commandPath).Length == 0)
        File.WriteAllText(commandPath,
            "[Command]\r\nAudit=Audit\r\n", Encoding.ASCII);

    var shareDirectory = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "Share"));
    Directory.CreateDirectory(shareDirectory);
    var expPath = Path.Combine(shareDirectory, "PlayerUpgradeExp.ini");
    if (!File.Exists(expPath) || new FileInfo(expPath).Length == 0)
        File.WriteAllText(expPath,
            "[PlayerLevelExp]\r\nLEVEL_1=50\r\n", Encoding.ASCII);

    M2Share.ObjectManager ??= new ObjectManager();
    M2Share.ProcessMsgCriticalSection ??= new object();
    M2Share.LogMsgCriticalSection ??= new object();
}

void CheckOpenDealRequest()
{
    var identity = new YbDbLegacy77Identity
    {
        Field0 = "ptid-112",
        Field11 = "ptid-112",
        RoleName = "交易角色",
        Field48 = "192.0.2.112"
    };
    True(YbDbOpenDealProtocol.TryCreateRequest(identity,
        out var request, out var error), error);
    Equal(0, request.QueryId, "112 QueryId");
    Equal(0, request.Param, "112 Param");
    Equal((ushort)112, request.Ident, "112 Ident");
    Equal(64, request.Payload.Length, "112 identity payload");
    True(YbDbLegacy77Codec.TryDecodeIdentity(request.Payload,
        out var decoded, out error), error);
    Equal("交易角色", decoded.RoleName, "112 role name");
}

void CheckOpenDealResponse()
{
    var payload = new byte[32];
    WriteGbkShortString(payload, 0, 15, "交易角色");
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(16, 4), 101);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(20, 4), 202);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(24, 4), 303);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(28, 4), 404);

    var response = new YbDbLegacy77Frame(1, -99, 1112, payload);
    True(YbDbOpenDealProtocol.TryDecodeResponse(response,
        out var result, out var error), error);
    Equal("交易角色", result.RoleName, "1112 role name");
    Equal(1, result.ResultCode, "1112 must use QueryId as result");
    Equal(101, result.CurrentYuanbao, "1112 current yuanbao");
    Equal(202, result.TotalConsumed, "1112 total consumed");
    Equal(303, result.RemainingSeconds, "1112 remaining seconds");
    Equal(404, result.DividendConsumed, "1112 dividend consumed");
    True(result.OpensDeal, "1112 result 1 did not open deal");

    False(YbDbOpenDealProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(1, 0, 1103, payload), out _, out _),
        "1103 decoded as 1112");
    False(YbDbOpenDealProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(1, 0, 1112, payload[..31]), out _, out _),
        "short 1112 decoded");
    False(YbDbOpenDealProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(1, 0, 1112, new byte[33]), out _, out _),
        "long 1112 decoded");
}

void CheckOpenDealDialogs()
{
    Equal("成功开启元宝交易系统！\\ \\<返回/@main>",
        YbDbOpenDealProtocol.GetDialog(1), "1112 success dialog");
    Equal("请先进行元宝冲值！\\ \\<返回/@main>",
        YbDbOpenDealProtocol.GetDialog(-1), "1112 -1 dialog");
    Equal("您的元宝数量不足开启交易系统！\\ \\<返回/@main>",
        YbDbOpenDealProtocol.GetDialog(-2), "1112 -2 dialog");
    Equal("[失败]：您已经开启元宝交易系统！\\ \\<返回/@main>",
        YbDbOpenDealProtocol.GetDialog(-3), "1112 -3 dialog");
    Equal("开通元宝交易系统失败！ \\ \\<返回/@main>",
        YbDbOpenDealProtocol.GetDialog(0), "1112 default dialog");

    foreach (var pair in new[]
             {
                 (1, "1112 success"), (-1, "1112 -1"),
                 (-2, "1112 -2"), (-3, "1112 -3"),
                 (0, "1112 default")
             })
        AssertNativeDialogSlashBytes(
            YbDbOpenDealProtocol.GetDialog(pair.Item1), pair.Item2);
}

void AssertNativeDialogSlashBytes(string value, string name)
{
    var bytes = Encoding.GetEncoding(936).GetBytes(value);
    ReadOnlySpan<byte> marker = stackalloc byte[] { 0x5C, 0x20, 0x5C, 0x3C };
    True(bytes.AsSpan().IndexOf(marker) >= 0,
        name + " is missing native 5C 20 5C 3C dialog bytes");
    ReadOnlySpan<byte> doubled = stackalloc byte[] { 0x5C, 0x5C };
    False(bytes.AsSpan().IndexOf(doubled) >= 0,
        name + " contains a doubled runtime backslash");
}

void CheckClaimEligibility()
{
    var calls = new List<string>();
    var player = NewPlayer();
    Equal(4, RunClaim(player, name => { calls.Add(name); return 0; }),
        "unqualified result");
    Equal(0, calls.Count, "unqualified claim granted items");

    player.m_boNativeFirstUsedGiftQualified = true;
    Equal(4, RunClaim(player, _ => 0), "zero account predicate result");
    player.m_nNativeYbDividendConsumed = 1;
    player.m_btFirstUsedGiftStage = 1;
    Equal(4, RunClaim(player, _ => 0), "claimed-stage result");

    player.m_btFirstUsedGiftStage = 0;
    for (var i = 0; i < 45; i++) player.m_ItemList.Add(new TUserItem());
    Equal(3, RunClaim(player, _ => 0), "hard 44-item reserve gate");

    player.m_ItemList.Clear();
    calls.Clear();
    Equal(0, RunClaim(player, name => { calls.Add(name); return 0; }),
        "full success result");
    SequenceEqual(new[] { "聚灵珠(小)", "双倍宝典" }, calls,
        "native reward order");
    Equal((byte)2, player.m_btFirstUsedGiftStage,
        "full success stage");
}

void CheckClaimPartialFailure()
{
    var firstBagFail = NewQualifiedPlayer();
    Equal(1, RunClaim(firstBagFail,
        name => name == "聚灵珠(小)" ? 1 : 0),
        "first bag failure result");
    Equal((byte)2, firstBagFail.m_btFirstUsedGiftStage,
        "second success must advance directly to stage 2");

    var firstSystemFail = NewQualifiedPlayer();
    Equal(2, RunClaim(firstSystemFail,
        name => name == "聚灵珠(小)" ? 2 : 0),
        "first system failure result");
    Equal((byte)2, firstSystemFail.m_btFirstUsedGiftStage,
        "first missing item plus second success stage");

    var secondBagFail = NewQualifiedPlayer();
    Equal(1, RunClaim(secondBagFail,
        name => name == "双倍宝典" ? 1 : 0),
        "second bag failure result");
    Equal((byte)1, secondBagFail.m_btFirstUsedGiftStage,
        "first success plus second bag failure stage");

    var overwrite = NewQualifiedPlayer();
    Equal(2, RunClaim(overwrite,
        name => name == "聚灵珠(小)" ? 1 : 99),
        "second failure must overwrite first result");
    Equal((byte)0, overwrite.m_btFirstUsedGiftStage,
        "two failures changed stage");
}

void CheckClaimDialogs()
{
    Equal("领奖成功", ClaimDialog(0), "claim result 0");
    Equal("[错误]：你的包裹空位不足", ClaimDialog(1), "claim result 1");
    Equal("[错误]：系统错误", ClaimDialog(2), "claim result 2");
    Equal("[错误]：请至少预留2个以上包裹位置", ClaimDialog(3),
        "claim result 3");
    Equal("[错误]：您不符合领奖条件", ClaimDialog(4), "claim result 4");
}

void CheckQualificationAuthorityClosed()
{
    var runtimeFiles = EnumerateSources("GameSvr")
        .Concat(EnumerateSources("DBSvr"))
        .Concat(EnumerateSources("SystemModule"))
        .ToArray();
    var runtime = string.Join("\n", runtimeFiles.Select(File.ReadAllText));
    Equal(0, Regex.Matches(runtime,
            @"m_boNativeFirstUsedGiftQualified\s*=(?!=)",
            RegexOptions.CultureInvariant).Count,
        "runtime writes FirstUsedGift qualification without LoginCenter authority");

    var state = Read("GameSvr", "Players", "TPlayObject.NativeFirstUsedGift.cs");
    foreach (var forbidden in new[]
             {
                 "awardplayers", "NetIPList", "m_nGameGold", "m_CreditCard",
                 "GetPlayerVar", "SetPlayerVar", "m_ScriptVVars", "m_ScriptSVars"
             })
        False(state.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
            "claim state uses forbidden substitute: " + forbidden);

    var creditPlayer = Read("GameSvr", "Players", "TPlayObject.NativeYbCredit.cs");
    var snapshotStart = creditPlayer.IndexOf("ApplyNativeYb1103Snapshot",
        StringComparison.Ordinal);
    var snapshotEnd = creditPlayer.IndexOf("TakeNativeYbDealPackets",
        snapshotStart, StringComparison.Ordinal);
    var snapshotRegion = creditPlayer[snapshotStart..snapshotEnd];
    False(snapshotRegion.Contains("FirstUsedGift", StringComparison.Ordinal),
        "1103 callback writes FirstUsedGift state");
}

void CheckDormantBoundary()
{
    var bridge = Read("GameSvr", "ScriptSystem", "PasEngine", "PasApiBridge.cs");
    Equal(2, Regex.Matches(bridge,
        "case \\\"reqgetfirstusedgift\\\":[\\s\\S]{0,900}?" +
        "return RejectUnsupportedNativeApi\\(out result\\);",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Count,
        "ReqGetFirstUsedGift PAS dispatch is not fail closed");
    False(bridge.Contains("RunNativeFirstUsedGiftStateMachine",
        StringComparison.Ordinal), "PAS invokes dormant claim state machine");

    var state = Read("GameSvr", "Players", "TPlayObject.NativeFirstUsedGift.cs");
    Equal(1, Regex.Matches(state,
        @"TryGrantNativeFirstUsedGiftItem\s*\(",
        RegexOptions.CultureInvariant).Count,
        "dormant real grant helper became reachable");

    var ybClient = Read("GameSvr", "Services", "YbDbClient.cs");
    True(ybClient.Contains(
            "private static readonly bool NativeOpenDealAuthorityEnabled = false;",
            StringComparison.Ordinal),
        "OpenYB authority gate is not explicitly disabled");
    True(ybClient.Contains("internal bool TryRequestOpenDeal",
            StringComparison.Ordinal),
        "dormant OpenYB request adapter is not assembly-internal");
    False(ybClient.Contains("public bool TryRequestOpenDeal",
            StringComparison.Ordinal),
        "OpenYB request adapter is publicly exposed");
    True(ybClient.Contains(
            "if (!NativeOpenDealAuthorityEnabled || player == null) return false;",
            StringComparison.Ordinal),
        "OpenYB request can run before its authority is enabled");
    True(ybClient.Contains("TryTakeOpenDealRequest",
            StringComparison.Ordinal),
        "OpenYB response does not require a pending request");
    True(ybClient.Contains("player.ObjectId != request.ObjectId",
            StringComparison.Ordinal),
        "OpenYB response does not verify the player object id");
    True(ybClient.Contains("ReferenceEquals(player, requestedPlayer)",
            StringComparison.Ordinal),
        "OpenYB response does not verify the exact player instance");
    True(ybClient.Contains("player.m_sUserID, request.Ptid",
            StringComparison.Ordinal),
        "OpenYB response does not verify PTID");
    Equal(4, Regex.Matches(ybClient, "_openDealRequests\\.Clear\\(\\);",
        RegexOptions.CultureInvariant).Count,
        "OpenYB pending identities are not cleared at every session boundary");
}

TPlayObject NewPlayer() => new();

TPlayObject NewQualifiedPlayer() => new()
{
    m_boNativeFirstUsedGiftQualified = true,
    m_nNativeYbDividendConsumed = 1
};

int RunClaim(TPlayObject player, Func<string, int> grant)
{
    var method = typeof(TPlayObject).GetMethod(
        "RunNativeFirstUsedGiftStateMachine",
        BindingFlags.Instance | BindingFlags.NonPublic);
    True(method != null, "dormant claim state machine is missing");
    return (int)method!.Invoke(player, new object[] { grant })!;
}

string ClaimDialog(int result)
{
    var method = typeof(TPlayObject).GetMethod(
        "GetNativeFirstUsedGiftResultMessage",
        BindingFlags.Static | BindingFlags.NonPublic);
    True(method != null, "claim result mapping is missing");
    return (string)method!.Invoke(null, new object[] { result })!;
}

IEnumerable<string> EnumerateSources(string directory)
{
    return Directory.EnumerateFiles(Path.Combine(root, directory), "*.cs",
            SearchOption.AllDirectories)
        .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                           StringComparison.OrdinalIgnoreCase)
                       && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                           StringComparison.OrdinalIgnoreCase));
}

string Read(params string[] parts) =>
    File.ReadAllText(parts.Aggregate(root, Path.Combine));

void WriteGbkShortString(byte[] destination, int offset, int capacity,
    string value)
{
    var gbk = Encoding.GetEncoding(936,
        EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    var bytes = gbk.GetBytes(value);
    True(bytes.Length <= capacity, "test short string is too long");
    destination[offset] = (byte)bytes.Length;
    bytes.CopyTo(destination, offset + 1);
}

string FindRepositoryRoot()
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        var directory = new DirectoryInfo(start);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName,
                    "GameSvr", "GameSvr.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
    }
    throw new DirectoryNotFoundException("repository root not found");
}

static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual,
    string message)
{
    if (!expected.SequenceEqual(actual))
        throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{message}: expected={expected}, actual={actual}");
}

static void True(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void False(bool condition, string message) => True(!condition, message);
