using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using SystemModule.Packet;

var root = FindRepositoryRoot();
var tests = new (string Name, Action Run)[]
{
    ("112 exact request", CheckRequest),
    ("1112 exact response", CheckResponse),
    ("1112 result dialogs", CheckResultDialogs),
    ("1112 validation boundary", CheckValidationBoundary),
    ("PAS fail-closed boundary", () => CheckFailClosedBoundary(root))
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

var scanSummary = "GBK-Envir=not-scanned";
if (args.Length != 0)
    scanSummary = ScanProductionPas(args[0]);

Console.WriteLine(
    $"YbDbOpenYbProtocolCompatCheck PASS tests={tests.Length} " +
    "request=112/64/Q0/P0 response=1112/32 " +
    "results=1,-1,-2,-3,default integration=fail-closed " +
    "authority=disabled " + scanSummary);
return;

static void CheckRequest()
{
    var identity = new YbDbLegacy77Identity
    {
        Field0 = "ptid-123456789",
        Field11 = "ptid-123456789",
        RoleName = "角色一",
        Field48 = "192.0.2.1"
    };
    True(YbDbOpenDealProtocol.TryCreateRequest(identity,
        out var request, out var error), error);
    Equal(0, request.QueryId, "112 QueryId");
    Equal(0, request.Param, "112 Param");
    Equal((ushort)112, request.Ident, "112 Ident");
    Equal(64, request.Payload.Length, "112 identity length");

    True(YbDbLegacy77Codec.TryDecodeIdentity(request.Payload,
        out var decoded, out error), error);
    Equal("ptid-12345", decoded.Field0, "112 native 10-byte PTID");
    Equal("ptid-123456789", decoded.Field11, "112 20-byte PTID");
    Equal("角色一", decoded.RoleName, "112 GBK role");
    Equal("192.0.2.1", decoded.Field48, "112 IP");

    True(YbDbLegacy77Codec.TryEncode(request,
        out var bytes, out error), error);
    Equal(80, bytes.Length, "112 wire length");
    Equal("77BBAA33000000000000000070004000",
        Convert.ToHexString(bytes.AsSpan(0, 16)), "112 wire header");

    AssertNativeUnavailableBytes(
        YbDbOpenDealProtocol.RequestUnavailableDialog);

    False(YbDbOpenDealProtocol.TryCreateRequest(null,
        out _, out _), "null 112 identity encoded");
}

static void CheckResponse()
{
    var payload = MakeResponsePayload("角色一", 101, 202, 303, 404);
    foreach (var param in new[] { int.MinValue, -1, 0, 1, int.MaxValue })
    {
        var frame = new YbDbLegacy77Frame(1, param,
            YbDbOpenDealProtocol.ResponseIdent, payload);
        True(YbDbOpenDealProtocol.TryDecodeResponse(frame,
            out var response, out var error), error);
        Equal(1, response.ResultCode, $"1112 result Param={param}");
        Equal("角色一", response.RoleName, $"1112 role Param={param}");
        Equal(101, response.CurrentYuanbao, "1112 current yuanbao");
        Equal(202, response.TotalConsumed, "1112 total consumed");
        Equal(303, response.RemainingSeconds, "1112 remaining seconds");
        Equal(404, response.DividendConsumed, "1112 dividend consumed");
        True(response.OpensDeal, "1112 result 1 not successful");
        Equal(YbDbOpenDealProtocol.SuccessDialog, response.Dialog,
            "1112 success dialog");
    }
}

static void CheckResultDialogs()
{
    Equal("成功开启元宝交易系统！\\ \\<返回/@main>",
        YbDbOpenDealProtocol.GetDialog(1), "result 1 dialog");
    Equal("请先进行元宝冲值！\\ \\<返回/@main>",
        YbDbOpenDealProtocol.GetDialog(-1), "result -1 dialog");
    Equal("您的元宝数量不足开启交易系统！\\ \\<返回/@main>",
        YbDbOpenDealProtocol.GetDialog(-2), "result -2 dialog");
    Equal("[失败]：您已经开启元宝交易系统！\\ \\<返回/@main>",
        YbDbOpenDealProtocol.GetDialog(-3), "result -3 dialog");
    foreach (var code in new[] { int.MinValue, -4, 0, 2, int.MaxValue })
        Equal("开通元宝交易系统失败！ \\ \\<返回/@main>",
            YbDbOpenDealProtocol.GetDialog(code), $"result {code} dialog");

    foreach (var pair in new[]
             {
                 (1, "result 1"), (-1, "result -1"),
                 (-2, "result -2"), (-3, "result -3"),
                 (0, "result default")
             })
        AssertNativeDialogSlashBytes(
            YbDbOpenDealProtocol.GetDialog(pair.Item1), pair.Item2);

    Equal(3009, YbDbOpenDealProtocol.OpenDealClientIdent,
        "successful client UI Ident");
    False(new YbDbOpenDealResult("r", -1, 0, 0, 0, 0).OpensDeal,
        "failure result marked successful");
}

static void CheckValidationBoundary()
{
    var payload = MakeResponsePayload("角色一", 1, 2, 3, 4);
    False(YbDbOpenDealProtocol.TryDecodeResponse(null,
        out _, out _), "null 1112 decoded");
    False(YbDbOpenDealProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(1, 0, 1111, payload), out _, out _),
        "wrong response Ident decoded");
    False(YbDbOpenDealProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(1, 0, 1112, payload[..31]), out _, out _),
        "31-byte response decoded");
    False(YbDbOpenDealProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(1, 0, 1112, new byte[33]), out _, out _),
        "33-byte response decoded");

    var overlongRole = (byte[])payload.Clone();
    overlongRole[0] = 16;
    False(YbDbOpenDealProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(1, 0, 1112, overlongRole), out _, out _),
        "overlong response role decoded");

    var invalidGbk = (byte[])payload.Clone();
    invalidGbk[0] = 1;
    invalidGbk[1] = 0x81;
    False(YbDbOpenDealProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(1, 0, 1112, invalidGbk), out _, out _),
        "invalid-GBK response role decoded");
}

static void CheckFailClosedBoundary(string root)
{
    var bridge = File.ReadAllText(Path.Combine(root, "GameSvr",
        "ScriptSystem", "PasEngine", "PasApiBridge.cs"));
    Equal(2, Regex.Matches(bridge, "case \"clientaskopenyb\":",
        RegexOptions.CultureInvariant).Count, "ClientAskOpenYB case count");
    var methodRegion = Slice(bridge, "public bool CallNpcMethod",
        "public bool CallNpcFunc");
    var functionRegion = Slice(bridge, "public bool CallNpcFunc",
        "public bool CallStandaloneFunction");
    foreach (var region in new[] { methodRegion, functionRegion })
    {
        var body = ExtractCase(region, "clientaskopenyb");
        Require(body, "return RejectUnsupportedNativeApi(out result);",
            "ClientAskOpenYB is not fail-closed");
        Reject(body, "SendMsg(", "ClientAskOpenYB sends a local UI message");
        Reject(body, "@openyb", "ClientAskOpenYB restored the fake label");
        Reject(body, "YbDbClient", "ClientAskOpenYB was wired without authority");
    }
    Reject(bridge, "@openyb", "live PAS bridge contains fake @openyb UI");

    var client = File.ReadAllText(Path.Combine(root, "GameSvr", "Services",
        "YbDbClient.cs"));
    Require(client,
        "private static readonly bool NativeOpenDealAuthorityEnabled = false;",
        "OpenYB authority gate is not explicitly disabled");
    Require(client, "internal bool TryRequestOpenDeal",
        "dormant OpenYB request adapter is not assembly-internal");
    Reject(client, "public bool TryRequestOpenDeal",
        "OpenYB request adapter is publicly exposed");
    Require(client,
        "if (!NativeOpenDealAuthorityEnabled || player == null) return false;",
        "OpenYB request can run before its authority is enabled");
    Require(client, "TryTakeOpenDealRequest",
        "OpenYB response does not require a pending request");
    Require(client, "player.ObjectId != request.ObjectId",
        "OpenYB response does not verify the player object id");
    Require(client, "ReferenceEquals(player, requestedPlayer)",
        "OpenYB response does not verify the exact player instance");
    Require(client, "player.m_sUserID, request.Ptid",
        "OpenYB response does not verify PTID");
    Equal(4, Regex.Matches(client, "_openDealRequests\\.Clear\\(\\);",
        RegexOptions.CultureInvariant).Count,
        "OpenYB pending identities are not cleared at every session boundary");
}

static string ScanProductionPas(string envirPath)
{
    if (!Directory.Exists(envirPath))
        throw new DirectoryNotFoundException(envirPath);

    var files = Directory.EnumerateFiles(envirPath, "*.pas",
        SearchOption.AllDirectories).ToArray();
    var hits = new List<string>();
    foreach (var file in files)
    {
        var raw = Encoding.Latin1.GetString(File.ReadAllBytes(file));
        if (raw.Contains("ClientAskOpenYB", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("@openyb", StringComparison.OrdinalIgnoreCase))
            hits.Add(Path.GetRelativePath(envirPath, file));
    }
    if (hits.Count != 0)
        throw new InvalidOperationException(
            "production OpenYB calls require authority: " + string.Join(", ", hits));
    return $"GBK-Envir={files.Length}/calls=0";
}

static byte[] MakeResponsePayload(string roleName, int currentYuanbao,
    int totalConsumed, int remainingSeconds, int dividendConsumed)
{
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    var gbk = Encoding.GetEncoding(936,
        EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    var roleBytes = gbk.GetBytes(roleName);
    if (roleBytes.Length > 15) throw new ArgumentOutOfRangeException(nameof(roleName));
    var payload = new byte[32];
    payload[0] = (byte)roleBytes.Length;
    roleBytes.CopyTo(payload, 1);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(16, 4), currentYuanbao);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(20, 4), totalConsumed);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(24, 4), remainingSeconds);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(28, 4), dividendConsumed);
    return payload;
}

static void AssertNativeDialogSlashBytes(string value, string name)
{
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    var bytes = Encoding.GetEncoding(936).GetBytes(value);
    ReadOnlySpan<byte> marker = stackalloc byte[] { 0x5C, 0x20, 0x5C, 0x3C };
    True(bytes.AsSpan().IndexOf(marker) >= 0,
        name + " is missing native 5C 20 5C 3C dialog bytes");
    ReadOnlySpan<byte> doubled = stackalloc byte[] { 0x5C, 0x5C };
    False(bytes.AsSpan().IndexOf(doubled) >= 0,
        name + " contains a doubled runtime backslash");
}

static void AssertNativeUnavailableBytes(string value)
{
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    var bytes = Encoding.GetEncoding(936).GetBytes(value);
    ReadOnlySpan<byte> marker = stackalloc byte[]
        { 0x5C, 0x20, 0x5C, 0x20, 0x5C, 0x20, 0x3C };
    True(bytes.AsSpan().IndexOf(marker) >= 0,
        "request failure is missing native dialog slash bytes");
    ReadOnlySpan<byte> doubled = stackalloc byte[] { 0x5C, 0x5C };
    False(bytes.AsSpan().IndexOf(doubled) >= 0,
        "request failure contains a doubled runtime backslash");
}

static string ExtractCase(string region, string name)
{
    var marker = $"case \"{name}\":";
    var start = region.IndexOf(marker, StringComparison.Ordinal);
    True(start >= 0, "missing case " + name);
    var next = region.IndexOf("case \"", start + marker.Length,
        StringComparison.Ordinal);
    return next < 0 ? region[start..] : region[start..next];
}

static string Slice(string source, string startMarker, string endMarker)
{
    var start = source.IndexOf(startMarker, StringComparison.Ordinal);
    True(start >= 0, "missing marker " + startMarker);
    var end = source.IndexOf(endMarker, start + startMarker.Length,
        StringComparison.Ordinal);
    True(end > start, "missing marker " + endMarker);
    return source[start..end];
}

static string FindRepositoryRoot()
{
    for (var current = new DirectoryInfo(AppContext.BaseDirectory);
         current != null; current = current.Parent)
    {
        if (File.Exists(Path.Combine(current.FullName, "GameSvr",
                "GameSvr.csproj")))
            return current.FullName;
    }
    for (var current = new DirectoryInfo(Directory.GetCurrentDirectory());
         current != null; current = current.Parent)
    {
        if (File.Exists(Path.Combine(current.FullName, "GameSvr",
                "GameSvr.csproj")))
            return current.FullName;
    }
    throw new DirectoryNotFoundException("repository root not found");
}

static void Require(string source, string value, string message) =>
    True(source.Contains(value, StringComparison.Ordinal), message);

static void Reject(string source, string value, string message) =>
    False(source.Contains(value, StringComparison.Ordinal), message);

static void True(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void False(bool condition, string message)
{
    if (condition) throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{name}: expected={expected}, actual={actual}");
}
