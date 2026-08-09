using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using SystemModule.Packet;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
TestRequest();
TestResponseAbi();
TestInvalidResponse();
TestDialogMatrix();
TestStateMatrix();
TestDormantBoundary();

Console.WriteLine(
    "YbDbForgeProgressProtocolCompatCheck PASS request=121/64 " +
    "response=1121/32 query=dialog-only ACK=none pending=none runtime=closed");
return;

static void TestRequest()
{
    var identity = new YbDbLegacy77Identity
    {
        Field0 = "1234567890-TRUNCATE",
        Field11 = "12345678901234567890-TRUNCATE",
        RoleName = "锻造勇士",
        Field48 = "127.0.0.1"
    };
    Assert(YbDbForgeProgressProtocol.TryCreateRequest(identity,
        out var request, out var error), error);
    Equal(0, request.QueryId, "request QueryId");
    Equal(0, request.Param, "request Param");
    Equal(YbDbForgeProgressProtocol.RequestIdent, request.Ident,
        "request Ident");
    Equal(YbDbLegacy77Codec.IdentitySize, request.Payload.Length,
        "request payload length");
    Equal(10, request.Payload[0], "native PTID narrow length");
    Equal(20, request.Payload[11], "native PTID wide length");

    Assert(YbDbLegacy77Codec.TryEncode(request, out var wire, out error), error);
    Equal(80, wire.Length, "request wire length");
    EqualUInt(YbDbLegacy77Codec.FrameMagic,
        BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(0, 4)),
        "request magic");
    Equal(0, BinaryPrimitives.ReadInt32LittleEndian(wire.AsSpan(4, 4)),
        "request wire QueryId");
    Equal(0, BinaryPrimitives.ReadInt32LittleEndian(wire.AsSpan(8, 4)),
        "request wire Param");
    Equal(YbDbForgeProgressProtocol.RequestIdent,
        BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(12, 2)),
        "request wire Ident");
    Equal(64, BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(14, 2)),
        "request wire payload length");
}

static void TestResponseAbi()
{
    var frame = BuildResponse(60, unchecked((int)0x88776655),
        "锻造勇士", 30, 12, 8, unchecked((int)0x44332211));
    Assert(YbDbForgeProgressProtocol.TryDecodeResponse(frame,
        out var response, out var error), error);
    Equal(60, response.RequestedTotal, "response QueryId/requested total");
    Equal(unchecked((int)0x88776655), response.IgnoredHeaderParam,
        "ignored response Param");
    EqualText("锻造勇士", response.RoleName, "response role");
    Equal(30, response.CompletedCount, "completed count");
    Equal(12, response.ClaimedCount, "claimed count");
    Equal(8, response.DoubleCompletedCount, "double completed count");
    Equal(unchecked((int)0x44332211), response.IgnoredTail,
        "ignored payload tail");
}

static void TestInvalidResponse()
{
    Assert(!YbDbForgeProgressProtocol.TryDecodeResponse(null,
        out _, out _), "null response accepted");
    Assert(!YbDbForgeProgressProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(0, 0, 1120, new byte[32]),
        out _, out _), "wrong response Ident accepted");
    Assert(!YbDbForgeProgressProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(0, 0, 1121, new byte[31]),
        out _, out _), "short response accepted");
    Assert(!YbDbForgeProgressProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(0, 0, 1121, new byte[33]),
        out _, out _), "long response accepted");

    var invalidLength = new byte[32];
    invalidLength[0] = 16;
    Assert(!YbDbForgeProgressProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(0, 0, 1121, invalidLength),
        out _, out _), "16-byte role accepted");

    var invalidGbk = new byte[32];
    invalidGbk[0] = 1;
    invalidGbk[1] = 0x81;
    Assert(!YbDbForgeProgressProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(0, 0, 1121, invalidGbk),
        out _, out _), "invalid GBK role accepted");
}

static void TestDialogMatrix()
{
    var notApplied = Decode(-1, "锻造勇士", 999, 888, 777, 666);
    EqualText(YbDbForgeProgressProtocol.NotAppliedDialog,
        YbDbForgeProgressProtocol.BuildDialog(notApplied),
        "QueryId -1 dialog");
    AssertContainsGbkBytes(YbDbForgeProgressProtocol.NotAppliedDialog,
        0x5C, 0x20, 0x5C, 0x3C, 0xB7, 0xB5);

    var claimable = Decode(60, "锻造勇士", 30, 12, 8, 123456);
    var claimableDialog = YbDbForgeProgressProtocol.BuildDialog(claimable);
    EqualText("锻造勇士 您的元宝锻造金刚石信息如下：\\" +
              "申请总数：60 颗\\已锻造完成数：30 颗  " +
              "其中双倍锻造完成数：8 颗  \\已领取数：12 颗\\" +
              "本次可领取数：18 颗\\尚未完成数：30 颗" +
              "\\ \\您要领取吗？  <全部领取/@ybdzlq>  " +
              "<只领取12颗/@ybdzlq_12>    <返回/@main>",
        claimableDialog, "claimable dialog");
    Assert(claimableDialog.Contains("\\ \\您要领取吗？",
        StringComparison.Ordinal), "claim prompt slash bytes drifted");

    var complete = Decode(12, "锻造勇士", 12, 12, 0, -999);
    EqualText("锻造勇士 您的元宝锻造金刚石信息如下：\\" +
              "申请总数：12 颗\\已锻造完成数：12 颗  \\" +
              "已领取数：12 颗\\本次可领取数：0 颗\\" +
              "尚未完成数：0 颗\\ \\<返回/@main>",
        YbDbForgeProgressProtocol.BuildDialog(complete),
        "no-double/no-claim dialog");

    var raw = Decode(int.MinValue, "A", int.MaxValue, -1, 0, 0);
    var rawDialog = YbDbForgeProgressProtocol.BuildDialog(raw);
    Assert(rawDialog.Contains("本次可领取数：-2147483648 颗",
        StringComparison.Ordinal), "native unchecked completed-claimed drifted");
    Assert(rawDialog.Contains("尚未完成数：1 颗",
        StringComparison.Ordinal), "native unchecked requested-completed drifted");
}

static void TestStateMatrix()
{
    EqualText("元宝系统暂时关闭中...\\ \\ \\ <返回/@main>",
        YbDbForgeProgressProtocol.RequestUnavailableDialog,
        "transport unavailable dialog");

    var rejected = YbDbForgeProgressProtocol.EvaluateInvocation(false);
    Assert(!rejected.TransportAccepted,
        "transport rejection reported native success");
    Assert(rejected.ShowUnavailableDialog,
        "transport rejection lost immediate dialog");
    Assert(!rejected.CreatesPendingRequest,
        "transport rejection created a pending request");

    var accepted = YbDbForgeProgressProtocol.EvaluateInvocation(true);
    Assert(accepted.TransportAccepted,
        "accepted request did not report native success");
    Assert(!accepted.ShowUnavailableDialog,
        "accepted request showed unavailable dialog");
    Assert(!accepted.CreatesPendingRequest,
        "accepted request created an unproven pending ledger");

    var response = Decode(12, "锻造勇士", 6, 2, 0, 0);
    var offline = YbDbForgeProgressProtocol.EvaluateCompletion(
        response, false, true);
    Assert(!offline.Display, "offline role received a response dialog");
    EqualValue(YbDbForgeProgressProtocol.OutputDisposition.None, offline.Output,
        "offline output disposition");

    var currentNpc = YbDbForgeProgressProtocol.EvaluateCompletion(
        response, true, true);
    Assert(currentNpc.Display, "online role lost response dialog");
    EqualValue(YbDbForgeProgressProtocol.OutputDisposition.CurrentNpcDialog,
        currentNpc.Output, "current NPC output disposition");
    Assert(!currentNpc.SendsAck, "query-only response produced an ACK");
    Assert(!currentNpc.MutatesPlayerOrAccount,
        "query-only response mutated state");

    var fallback = YbDbForgeProgressProtocol.EvaluateCompletion(
        response, true, false);
    EqualValue(YbDbForgeProgressProtocol.OutputDisposition.MerchantSayNpcPrefix,
        fallback.Output, "NPC/ fallback output disposition");
    EqualText(YbDbForgeProgressProtocol.FallbackMerchantPrefix +
              fallback.Dialog,
        "NPC/" + YbDbForgeProgressProtocol.BuildDialog(response),
        "NPC/ fallback payload");
}

static void TestDormantBoundary()
{
    var root = FindRepositoryRoot();
    var protocol = File.ReadAllText(Path.Combine(root, "SystemModule", "Packet",
        "YbDbForgeProgressProtocol.cs"));
    var client = File.ReadAllText(Path.Combine(root, "GameSvr", "Services",
        "YbDbClient.cs"));
    var bridge = File.ReadAllText(Path.Combine(root, "GameSvr", "ScriptSystem",
        "PasEngine", "PasApiBridge.cs"));
    var methods = Slice(bridge, "public bool CallNpcMethod",
        "public bool CallNpcFunc");
    var functions = Slice(bridge, "public bool CallNpcFunc",
        "public bool CallStandaloneFunction");

    CheckFailClosed(methods, "clientaskybduanzao");
    CheckFailClosed(functions, "clientaskybduanzao");
    Equal(2, Regex.Matches(bridge, "case \\\"clientaskybduanzao\\\":" ,
        RegexOptions.CultureInvariant).Count,
        "ClientAskYBDuanZao PAS case count");
    Reject(client, "RequestForgeProgress(",
        "121 runtime sender was opened without the central service authority");
    Reject(client, "YbDbForgeProgressProtocol.ResponseIdent",
        "1121 runtime dispatch was opened through the completion constant");
    Assert(!Regex.IsMatch(client,
            @"(?:frame|queued\.Frame)\.Ident\s*==?\s*1121",
            RegexOptions.CultureInvariant),
        "1121 literal runtime dispatch was opened");
    Reject(protocol, "TPlayObject",
        "dormant forge-progress codec gained a player dependency");
    Reject(protocol, "SendMsg(",
        "dormant forge-progress codec gained player messaging");
    Reject(protocol, "SuccessAckIdent",
        "query-only forge-progress codec gained an ACK");
}

static void CheckFailClosed(string region, string name)
{
    var body = ExtractThroughFirstReturn(region, name);
    Require(body, "return RejectUnsupportedNativeApi(out result);",
        name + " is not fail-closed");
    foreach (var forbidden in new[]
             {
                 "SendMsg(", "@ybduanzao", "RequestForgeProgress("
             })
        Reject(body, forbidden, name + " retains runtime substitute: " + forbidden);
}

static YbDbForgeProgressProtocol.Response Decode(int requestedTotal,
    string roleName, int completed, int claimed, int doubleCompleted,
    int ignoredTail)
{
    var frame = BuildResponse(requestedTotal, 987654321, roleName,
        completed, claimed, doubleCompleted, ignoredTail);
    Assert(YbDbForgeProgressProtocol.TryDecodeResponse(frame,
        out var response, out var error), error);
    return response;
}

static YbDbLegacy77Frame BuildResponse(int requestedTotal, int param,
    string roleName, int completed, int claimed, int doubleCompleted,
    int ignoredTail)
{
    var payload = new byte[YbDbForgeProgressProtocol.ResponsePayloadSize];
    WriteShortString(payload, YbDbForgeProgressProtocol.RoleNameOffset,
        YbDbForgeProgressProtocol.RoleNameMaximumGbkBytes, roleName);
    BinaryPrimitives.WriteInt32LittleEndian(
        payload.AsSpan(YbDbForgeProgressProtocol.CompletedCountOffset, 4),
        completed);
    BinaryPrimitives.WriteInt32LittleEndian(
        payload.AsSpan(YbDbForgeProgressProtocol.ClaimedCountOffset, 4),
        claimed);
    BinaryPrimitives.WriteInt32LittleEndian(
        payload.AsSpan(YbDbForgeProgressProtocol.DoubleCompletedCountOffset, 4),
        doubleCompleted);
    BinaryPrimitives.WriteInt32LittleEndian(
        payload.AsSpan(YbDbForgeProgressProtocol.IgnoredTailOffset, 4),
        ignoredTail);
    return new YbDbLegacy77Frame(requestedTotal, param,
        YbDbForgeProgressProtocol.ResponseIdent, payload);
}

static void WriteShortString(byte[] payload, int offset, int capacity,
    string value)
{
    var bytes = Encoding.GetEncoding(936).GetBytes(value ?? string.Empty);
    Assert(bytes.Length <= capacity, "test ShortString exceeds native slot");
    payload[offset] = (byte)bytes.Length;
    bytes.CopyTo(payload, offset + 1);
}

static void AssertContainsGbkBytes(string value, params byte[] expected)
{
    var bytes = Encoding.GetEncoding(936).GetBytes(value);
    Assert(bytes.AsSpan().IndexOf(expected) >= 0,
        "GBK dialogue byte sequence drifted: " + Convert.ToHexString(bytes));
}

static string ExtractThroughFirstReturn(string region, string name)
{
    var marker = $"case \"{name}\":";
    var start = region.IndexOf(marker, StringComparison.Ordinal);
    Assert(start >= 0, "missing PAS case: " + name);
    var returnStart = region.IndexOf("return ", start + marker.Length,
        StringComparison.Ordinal);
    Assert(returnStart >= 0, "PAS case has no return: " + name);
    var returnEnd = region.IndexOf(';', returnStart);
    Assert(returnEnd > returnStart, "PAS case return has no terminator: " + name);
    return region.Substring(start, returnEnd - start + 1);
}

static string Slice(string source, string startMarker, string endMarker)
{
    var start = source.IndexOf(startMarker, StringComparison.Ordinal);
    Assert(start >= 0, "source start marker missing: " + startMarker);
    var end = source.IndexOf(endMarker, start + startMarker.Length,
        StringComparison.Ordinal);
    Assert(end > start, "source end marker missing: " + endMarker);
    return source.Substring(start, end - start);
}

static void Require(string source, string value, string message) =>
    Assert(source.Contains(value, StringComparison.Ordinal), message);

static void Reject(string source, string value, string message) =>
    Assert(!source.Contains(value, StringComparison.Ordinal), message);

static void Equal(int expected, int actual, string message)
{
    if (expected != actual)
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
}

static void EqualValue<T>(T expected, T actual, string message) where T : struct
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
}

static void EqualUInt(uint expected, uint actual, string message)
{
    if (expected != actual)
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
}

static void EqualText(string expected, string actual, string message)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
        throw new InvalidOperationException(
            $"{message}: expected [{expected}], actual [{actual}]");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static string FindRepositoryRoot()
{
    foreach (var start in new[]
             {
                 Environment.CurrentDirectory,
                 AppContext.BaseDirectory
             })
    {
        for (var directory = new DirectoryInfo(start);
             directory != null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName,
                    "GameSvr", "GameSvr.csproj")))
                return directory.FullName;
        }
    }

    throw new DirectoryNotFoundException(
        "repository root containing GameSvr/GameSvr.csproj was not found");
}
