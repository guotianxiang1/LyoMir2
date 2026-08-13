using System.Text;
using SystemModule.Packet;

var root = FindRepositoryRoot();
var tests = new (string Name, Action Run)[]
{
    ("124 exact variable request", CheckExactRequest),
    ("124 request boundaries", CheckRequestBoundaries),
    ("1124 exact response", CheckExactResponse),
    ("1124 response validation", CheckResponseValidation),
    ("1124 result and route matrix", CheckResultAndRouteMatrix),
    ("117 PAS distinction and runtime fail-closed",
        () => CheckRuntimeFailClosed(root))
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
    $"YbDbCancelDealProtocolCompatCheck PASS tests={tests.Length} " +
    "request=124/Q0/P0/65+N response=1124/64B runtime=fail-closed");
return;

static void CheckExactRequest()
{
    var identity = Identity();
    True(YbDbCancelDealProtocol.TryCreateRequest(identity, "目标甲",
        out var request, out var error), error);
    Equal(0, request.QueryId, "124 QueryId");
    Equal(0, request.Param, "124 Param");
    Equal((ushort)124, request.Ident, "124 Ident");
    Equal(71, request.Payload.Length, "124 payload 65+N length");

    True(YbDbLegacy77Codec.TryDecodeIdentity(
        request.Payload.AsSpan(0, 64), out var decoded, out error), error);
    Equal("ptid-12345", decoded.Field0, "124 narrow PTID");
    Equal("ptid-123456789", decoded.Field11, "124 full PTID");
    Equal("管理员", decoded.RoleName, "124 actor role");
    Equal("192.0.2.124", decoded.Field48, "124 actor IP");
    Equal("C4BFB1EABCD700",
        Convert.ToHexString(request.Payload.AsSpan(64)),
        "124 raw target plus NUL");

    True(YbDbLegacy77Codec.TryEncode(request, out var wire, out error),
        error);
    Equal(87, wire.Length, "124 wire length");
    Equal("77BBAA3300000000000000007C004700",
        Convert.ToHexString(wire.AsSpan(0, 16)), "124 wire header");
}

static void CheckRequestBoundaries()
{
    var identity = Identity();
    False(YbDbCancelDealProtocol.TryCreateRequest(identity, null,
        out _, out _), "null target encoded");
    False(YbDbCancelDealProtocol.TryCreateRequest(identity, string.Empty,
        out _, out _), "empty target encoded");
    False(YbDbCancelDealProtocol.TryCreateRequest(null, "目标",
        out _, out _), "null identity encoded");
    False(YbDbCancelDealProtocol.TryCreateRequest(identity, "目标😀",
        out _, out _), "non-GBK target encoded");

    var maximum = new string('A',
        YbDbCancelDealProtocol.MaximumTargetByteLength);
    True(YbDbCancelDealProtocol.TryCreateRequest(identity, maximum,
        out var request, out var error), error);
    Equal(YbDbLegacy77Codec.MaximumPayloadLength, request.Payload.Length,
        "124 maximum payload");
    True(YbDbLegacy77Codec.TryEncode(request, out var wire, out error),
        error);
    Equal(YbDbLegacy77Codec.MaximumFrameLength, wire.Length,
        "124 maximum wire");
    Equal((byte)0, request.Payload[^1], "124 final NUL");

    False(YbDbCancelDealProtocol.TryCreateRequest(identity, maximum + "A",
        out _, out _), "oversized target encoded");
}

static void CheckExactResponse()
{
    var payload = ResponsePayload("管理员", "目标甲");
    // The native handler ignores bytes 0..31 entirely.
    payload[0] = 1;
    payload[1] = 0x81;
    var frame = new YbDbLegacy77Frame(1, unchecked((int)0x87654321),
        1124, payload);
    True(YbDbCancelDealProtocol.TryDecodeResponse(frame,
        out var result, out var error), error);
    Equal("管理员", result.RoleName, "1124 actor role");
    Equal("目标甲", result.TargetRoleName, "1124 target role");
    Equal(1, result.ResultCode, "1124 result code");
    Equal(unchecked((int)0x87654321), result.IgnoredHeaderParam,
        "1124 ignored Param preserved");
    True(result.IsSuccess, "1124 QueryId 1 not successful");
    Equal("取消 目标甲 的元宝交易成功", result.Message,
        "1124 success text");
}

static void CheckResponseValidation()
{
    var payload = ResponsePayload("管理员", "目标甲");
    False(YbDbCancelDealProtocol.TryDecodeResponse(null,
        out _, out _), "null response decoded");
    False(YbDbCancelDealProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(1, 0, 1123, payload),
        out _, out _), "wrong response Ident decoded");
    False(YbDbCancelDealProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(1, 0, 1124, new byte[63]),
        out _, out _), "short response decoded");
    False(YbDbCancelDealProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(1, 0, 1124, new byte[65]),
        out _, out _), "long response decoded");

    var invalidRole = (byte[])payload.Clone();
    invalidRole[32] = 1;
    invalidRole[33] = 0x81;
    False(YbDbCancelDealProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(1, 0, 1124, invalidRole),
        out _, out _), "invalid role GBK decoded");

    var invalidTarget = (byte[])payload.Clone();
    invalidTarget[48] = 1;
    invalidTarget[49] = 0x81;
    False(YbDbCancelDealProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(1, 0, 1124, invalidTarget),
        out _, out _), "invalid target GBK decoded");
}

static void CheckResultAndRouteMatrix()
{
    var success = Decode(1, 7, "管理员", "目标甲");
    var failure = Decode(-2, -9, "管理员", "目标甲");
    var zero = Decode(0, 0, "管理员", "目标甲");
    Equal("取消 目标甲 的元宝交易失败(-2)", failure.Message,
        "1124 negative failure text");
    Equal("取消 目标甲 的元宝交易失败(0)", zero.Message,
        "1124 zero failure text");
    False(failure.IsSuccess, "negative QueryId succeeded");
    False(zero.IsSuccess, "zero QueryId succeeded");

    var online = YbDbCancelDealProtocol.EvaluateResponse(success,
        roleEntryFound: true, roleGhostFlag: false,
        roleReadyFlagAtD2C: true);
    True(online.EmitMessage, "eligible online role was silent");
    Equal(success.Message, online.Message, "online response text");
    Equal((ushort)0xFFDB, online.MessageKind, "native message kind");
    True(online.MatchesByRoleNameOnly, "route invents another lookup key");
    False(online.ValidatesObjectIdAccountPtidOrSession,
        "route invents session correlation");
    False(online.RegistersPendingRequest, "route registers pending state");
    False(online.SendsAcknowledgement, "route sends ACK");
    False(online.RetriesRequest, "route retries request");
    False(online.MutatesPlayerDealOrAccountState,
        "route mutates deal/account state");
    False(online.MutatesInventoryOrDatabase,
        "route mutates inventory/database");
    False(online.WritesBusinessOrGameLog,
        "route writes local business/game log");

    foreach (var route in new[]
             {
                 YbDbCancelDealProtocol.EvaluateResponse(success,
                     false, false, true),
                 YbDbCancelDealProtocol.EvaluateResponse(success,
                     true, true, true),
                 YbDbCancelDealProtocol.EvaluateResponse(success,
                     true, false, false),
                 YbDbCancelDealProtocol.EvaluateResponse(
                     Decode(1, 0, "", "目标甲"), true, false, true)
             })
    {
        False(route.EmitMessage, "ineligible/offline role emitted message");
        Equal(string.Empty, route.Message, "silent route carries message");
    }
}

static void CheckRuntimeFailClosed(string root)
{
    var service = Read(root, "GameSvr", "Services", "YbDbClient.cs");
    Reject(service, "YbDbCancelDealProtocol",
        "dormant 124/1124 codec is wired into YbDbClient");
    Reject(service, "RequestCancelYbDeal",
        "live 124 sender exists without 6108 authority");

    // "CancelYBDeal" occurs once under GameSvr/Command, in the auto-generated
    // NativeGmCommandRegistry, and that occurrence is a transcribed native fact rather
    // than an exposure. The image carries the ShortString `0C 'CancelYBDeal'` at
    // 0x7BBB54, exactly one 0x120 stride after DelSelfSkill (index 95, 0x7BBA34) and one
    // before ChgmanKind (index 97, 0x7BBC74), matching the registry's indices; the case
    // body sits at 0x00624FF8 and forwards to sub_6D731C. The registry is consulted only
    // to map a FormGMCommand.ini index onto an ALREADY REGISTERED command -- see
    // CommandManager.ApplyNativeFormGmCommandIni, which does
    // `OriginalCommandMaps.TryGetValue(defaultName, out var cmd)` and `continue`s when
    // the name is not registered -- so a table entry for a command nobody implemented
    // reaches no runtime path. Rejecting the bare string could only be satisfied by
    // deleting a fact read off the image, which is the wrong direction entirely.
    //
    // Exposure means a registered handler under that name, or the command surface
    // touching the 124 codec. Both are still rejected, per file so the report names the
    // offender.
    var commandFiles = Directory.EnumerateFiles(
        Path.Combine(root, "GameSvr", "Command"), "*.cs",
        SearchOption.AllDirectories).ToArray();
    var commandSources = string.Join("\n", commandFiles.Select(File.ReadAllText));

    foreach (var path in commandFiles)
    {
        var text = File.ReadAllText(path);
        if (!text.Contains("CancelYBDeal", StringComparison.Ordinal)) continue;
        if (Path.GetFileName(path) != "NativeGmCommandRegistry.cs")
            throw new InvalidOperationException(
                "admin command exposes dormant 124 at runtime: " +
                Path.GetFileName(path));

        Require(text, "[96] = \"CancelYBDeal\", // perm 4 IMPL @00624FF8",
            "the CancelYBDeal name-table entry no longer matches the image " +
            "(0x7BBB54 ShortString, case body 0x00624FF8)");
        Reject(text, "[GameCommand(",
            "the GM name table started registering command handlers");
    }

    Reject(commandSources, "YbDbCancelDealProtocol",
        "command surface references the dormant 124/1124 codec");
    Reject(commandSources, "RequestCancelYbDeal",
        "command surface has a live 124 sender");

    var bridge = Read(root, "GameSvr", "ScriptSystem", "PasEngine",
        "PasApiBridge.cs");
    var pasCase = ExtractCase(bridge, "clientsellercancelybdeal");
    Require(pasCase, "RejectUnsupportedNativeApi()",
        "ClientSellerCancelYBDeal is not fail-closed");
    Reject(pasCase, "RM_MERCHANTDLGCLOSE",
        "request 117 PAS wrapper is replaced by a local dialog close");
    Reject(pasCase, "YbDbCancelDealProtocol",
        "request 117 PAS wrapper is conflated with request 124");
}

static YbDbCancelDealProtocol.CancelDealResult Decode(int queryId,
    int param, string roleName, string targetName)
{
    var frame = new YbDbLegacy77Frame(queryId, param, 1124,
        ResponsePayload(roleName, targetName));
    True(YbDbCancelDealProtocol.TryDecodeResponse(frame,
        out var result, out var error), error);
    return result;
}

static byte[] ResponsePayload(string roleName, string targetName)
{
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    var gbk = Encoding.GetEncoding(936,
        EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    var payload = new byte[64];
    WriteShortString(payload, 32, 15, roleName, gbk);
    WriteShortString(payload, 48, 15, targetName, gbk);
    return payload;
}

static void WriteShortString(byte[] data, int offset, int capacity,
    string value, Encoding encoding)
{
    var bytes = encoding.GetBytes(value ?? string.Empty);
    if (bytes.Length > capacity)
        throw new InvalidOperationException("test short string is oversized");
    data[offset] = (byte)bytes.Length;
    bytes.CopyTo(data, offset + 1);
}

static YbDbLegacy77Identity Identity() => new()
{
    Field0 = "ptid-123456789",
    Field11 = "ptid-123456789",
    RoleName = "管理员",
    Field48 = "192.0.2.124"
};

static string ExtractCase(string source, string caseName)
{
    var marker = $"case \"{caseName}\":";
    var start = source.IndexOf(marker, StringComparison.Ordinal);
    if (start < 0) throw new InvalidOperationException(marker + " is absent");
    var next = source.IndexOf("case \"", start + marker.Length,
        StringComparison.Ordinal);
    return next < 0 ? source[start..] : source[start..next];
}

static string Read(string root, params string[] parts) =>
    File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));

static string FindRepositoryRoot()
{
    foreach (var start in new[]
             {
                 Environment.CurrentDirectory,
                 AppContext.BaseDirectory
             })
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
    throw new DirectoryNotFoundException("LyoMir2 repository root not found");
}
static void Require(string text, string value, string message)
{
    if (!text.Contains(value, StringComparison.Ordinal))
        throw new InvalidOperationException(message);
}

static void Reject(string text, string value, string message)
{
    if (text.Contains(value, StringComparison.Ordinal))
        throw new InvalidOperationException(message);
}

static void True(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
}

static void False(bool value, string message)
{
    if (value) throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{name}: expected={expected}, actual={actual}");
}
