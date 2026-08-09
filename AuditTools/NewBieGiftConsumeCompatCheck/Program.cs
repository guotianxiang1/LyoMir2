using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using SystemModule.Packet;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
TestInvocation();
TestRequest();
TestResponse();
TestInvalidResponse();
TestCompletionMatrix();
TestAck();
TestDormantBoundary();

Console.WriteLine(
    "NewBieGiftConsumeCompatCheck PASS ABI=Boolean/no-args " +
    "request=125/10100/5/64 response=1125/32 ACK=105/106 " +
    "callback=NPC-current snapshot=authoritative runtime=closed");
return;

static void TestInvocation()
{
    var decision = YbDbNewBieGiftConsumeProtocol.EvaluateInvocation(
        4, false, true);
    Assert(!decision.PascalReturnValue
           && !decision.InvokeCommonRequestBuilder
           && !decision.AttemptTransportSend
           && !decision.SetSharedIdent125Pending,
        "cached Yuanbao below five did not return false");

    decision = YbDbNewBieGiftConsumeProtocol.EvaluateInvocation(
        5, false, true);
    Assert(decision.PascalReturnValue
           && decision.InvokeCommonRequestBuilder
           && decision.AttemptTransportSend
           && decision.SetSharedIdent125Pending,
        "exactly five Yuanbao did not attempt the request");

    decision = YbDbNewBieGiftConsumeProtocol.EvaluateInvocation(
        5, true, true);
    Assert(decision.PascalReturnValue
           && decision.InvokeCommonRequestBuilder
           && !decision.AttemptTransportSend
           && !decision.SetSharedIdent125Pending,
        "shared Ident 125 pending changed the wrapper return value");

    decision = YbDbNewBieGiftConsumeProtocol.EvaluateInvocation(
        int.MaxValue, false, false);
    Assert(decision.PascalReturnValue
           && decision.InvokeCommonRequestBuilder
           && decision.AttemptTransportSend
           && !decision.SetSharedIdent125Pending,
        "rejected transport did not preserve the native builder attempt");
}

static void TestRequest()
{
    var identity = new YbDbLegacy77Identity
    {
        Field0 = "PTID-123456789012345",
        Field11 = "PTID-123456789012345",
        RoleName = "新手勇士",
        Field48 = "127.0.0.1"
    };
    Assert(YbDbNewBieGiftConsumeProtocol.TryCreateRequest(identity,
        out var request, out var error), error);
    Equal(YbDbNewBieGiftConsumeProtocol.Operation, request.QueryId,
        "request operation/QueryId");
    Equal(YbDbNewBieGiftConsumeProtocol.Cost, request.Param,
        "request cost/Param");
    Equal(YbDbNewBieGiftConsumeProtocol.RequestIdent, request.Ident,
        "request Ident");
    Equal(64, request.Payload.Length, "request payload length");
    Equal(10, request.Payload[0], "native narrow PTID length");
    Equal(20, request.Payload[11], "native wide PTID length");

    Assert(YbDbLegacy77Codec.TryEncode(request,
        out var wire, out error), error);
    Equal(80, wire.Length, "request wire length");
    EqualUInt(YbDbLegacy77Codec.FrameMagic,
        BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(0, 4)),
        "request magic");
    Equal(YbDbNewBieGiftConsumeProtocol.Operation,
        BinaryPrimitives.ReadInt32LittleEndian(wire.AsSpan(4, 4)),
        "request wire QueryId");
    Equal(YbDbNewBieGiftConsumeProtocol.Cost,
        BinaryPrimitives.ReadInt32LittleEndian(wire.AsSpan(8, 4)),
        "request wire Param");
    Equal(YbDbNewBieGiftConsumeProtocol.RequestIdent,
        BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(12, 2)),
        "request wire Ident");
    Equal(64, BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(14, 2)),
        "request wire payload length");
}

static void TestResponse()
{
    var frame = BuildResponse(83_761,
        YbDbNewBieGiftConsumeProtocol.Operation, "新手勇士",
        95, 2_005, 3_600, 17);
    Assert(YbDbNewBieGiftConsumeProtocol.TryDecodeResponse(frame,
        out var response, out var error), error);
    Equal(83_761, response.Result, "response result/transaction token");
    Equal(YbDbNewBieGiftConsumeProtocol.Operation, response.Operation,
        "response operation");
    EqualText("新手勇士", response.RoleName, "response role");
    Equal(95, response.CurrentYuanbao, "authoritative current Yuanbao");
    Equal(2_005, response.TotalConsumed, "authoritative total consumed");
    Equal(3_600, response.RemainingSeconds,
        "authoritative remaining seconds");
    Equal(17, response.DividendConsumed,
        "authoritative dividend consumed");
    Equal(5, response.NativeYbConsumeDelta, "native YBConsume delta");
    Equal(5, response.NativeBonusBase, "native bonus base");
    Equal(5, response.CreditCardValue2Delta, "CreditCard Value2 delta");

    Assert(YbDbLegacy77Codec.TryEncode(frame, out var wire, out error), error);
    Equal(48, wire.Length, "response wire length");
    Equal(83_761,
        BinaryPrimitives.ReadInt32LittleEndian(wire.AsSpan(4, 4)),
        "response wire result/token");
    Equal(YbDbNewBieGiftConsumeProtocol.Operation,
        BinaryPrimitives.ReadInt32LittleEndian(wire.AsSpan(8, 4)),
        "response wire operation");
    Equal(YbDbNewBieGiftConsumeProtocol.ResponseIdent,
        BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(12, 2)),
        "response wire Ident");
    Equal(32, BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(14, 2)),
        "response wire payload length");
}

static void TestInvalidResponse()
{
    Assert(!YbDbNewBieGiftConsumeProtocol.TryDecodeResponse(null,
        out _, out _), "null response accepted");
    Assert(!YbDbNewBieGiftConsumeProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(1, 10100, 1124, new byte[32]),
        out _, out _), "wrong response Ident accepted");
    Assert(!YbDbNewBieGiftConsumeProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(1, 10100, 1125, new byte[31]),
        out _, out _), "short response accepted");
    Assert(!YbDbNewBieGiftConsumeProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(1, 10100, 1125, new byte[33]),
        out _, out _), "long response accepted");

    var longRole = new byte[32];
    longRole[0] = 16;
    Assert(!YbDbNewBieGiftConsumeProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(1, 10100, 1125, longRole),
        out _, out _), "16-byte role accepted");

    var invalidGbk = new byte[32];
    invalidGbk[0] = 1;
    invalidGbk[1] = 0x81;
    Assert(!YbDbNewBieGiftConsumeProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(1, 10100, 1125, invalidGbk),
        out _, out _), "invalid GBK role accepted");
}

static void TestCompletionMatrix()
{
    var negative = Decode(BuildResponse(-1, 10100,
        "新手勇士", 4, 100, 200, 3));
    var decision = YbDbNewBieGiftConsumeProtocol.EvaluateCompletion(
        negative, true, true);
    Equal(YbDbNewBieGiftConsumeProtocol.AckDisposition.None,
        decision.Ack, "negative ACK");
    Assert(decision.ClearSharedPending,
        "negative response did not clear shared pending");
    Assert(decision.ShowInsufficientYuanbaoDialog,
        "result -1 did not show the native insufficient dialog");
    Equal(YbDbNewBieGiftConsumeProtocol.FailureOutputDisposition.CurrentNpcDialog,
        decision.FailureOutput, "negative current-NPC output route");
    EqualText("对不起，您没有那么多的元宝",
        YbDbNewBieGiftConsumeProtocol.InsufficientYuanbaoDialog,
        "result -1 dialog");

    decision = YbDbNewBieGiftConsumeProtocol.EvaluateCompletion(
        negative, true, false);
    Equal(YbDbNewBieGiftConsumeProtocol.FailureOutputDisposition.MerchantSayNpcPrefix,
        decision.FailureOutput, "negative no-NPC output route");
    Equal(643, YbDbNewBieGiftConsumeProtocol.FallbackMerchantMessageIdent,
        "negative fallback merchant Ident");
    EqualText("NPC/", YbDbNewBieGiftConsumeProtocol.FallbackMerchantPrefix,
        "negative fallback merchant prefix");

    var otherNegative = Decode(BuildResponse(-2, 10100,
        "新手勇士", 4, 100, 200, 3));
    decision = YbDbNewBieGiftConsumeProtocol.EvaluateCompletion(
        otherNegative, true, true);
    Assert(!decision.ShowInsufficientYuanbaoDialog,
        "non-minus-one result gained a dialog");
    Equal(YbDbNewBieGiftConsumeProtocol.FailureOutputDisposition.None,
        decision.FailureOutput, "other negative output route");
    Equal(YbDbNewBieGiftConsumeProtocol.AckDisposition.None,
        decision.Ack, "other negative ACK");

    var positive = Decode(BuildResponse(83_761, 10100,
        "新手勇士", 95, 2_005, 3_600, 17));
    decision = YbDbNewBieGiftConsumeProtocol.EvaluateCompletion(
        positive, false, false);
    Equal(YbDbNewBieGiftConsumeProtocol.AckDisposition.Failure,
        decision.Ack, "offline positive ACK");
    Assert(!decision.ClearSharedPending,
        "offline positive response cleared an absent player's pending state");

    decision = YbDbNewBieGiftConsumeProtocol.EvaluateCompletion(
        positive, true, false);
    Equal(YbDbNewBieGiftConsumeProtocol.AckDisposition.Failure,
        decision.Ack, "no-current-NPC positive ACK");
    Assert(decision.ClearSharedPending && !decision.InvokeCallback,
        "no-current-NPC state matrix mismatch");

    decision = YbDbNewBieGiftConsumeProtocol.EvaluateCompletion(
        positive, true, true);
    Equal(YbDbNewBieGiftConsumeProtocol.AckDisposition.Success,
        decision.Ack, "positive ACK");
    Assert(decision.ClearSharedPending && decision.InvokeCallback,
        "positive callback state mismatch");
    EqualText(
        "ClearSharedIdent125Pending>InvokeNpcCallback>" +
        "ApplyNativeConsumeLingFuBonus>RecordNativeYbConsume>" +
        "ApplyAuthoritativeAccountSnapshot>TryAccumulateCreditCardValue2>" +
        "SendSuccessAck",
        string.Join('>', decision.SuccessSteps),
        "native success side-effect order");

    var otherOperation = Decode(BuildResponse(83_761, 10099,
        "新手勇士", 95, 2_005, 3_600, 17));
    decision = YbDbNewBieGiftConsumeProtocol.EvaluateCompletion(
        otherOperation, true, true);
    Equal(YbDbNewBieGiftConsumeProtocol.AckDisposition.None,
        decision.Ack, "other Ident-125 operation entered NewBie handler");
}

static void TestAck()
{
    Assert(YbDbNewBieGiftConsumeProtocol.TryCreateAck(83_761, true,
        out var ack, out var error), error);
    AssertAck(ack, YbDbNewBieGiftConsumeProtocol.SuccessAckIdent, 83_761);
    Assert(YbDbNewBieGiftConsumeProtocol.TryCreateAck(83_761, false,
        out ack, out error), error);
    AssertAck(ack, YbDbNewBieGiftConsumeProtocol.FailureAckIdent, 83_761);
    Assert(!YbDbNewBieGiftConsumeProtocol.TryCreateAck(0, true,
        out _, out _), "zero transaction ACK accepted");
    Assert(!YbDbNewBieGiftConsumeProtocol.TryCreateAck(-1, false,
        out _, out _), "negative transaction ACK accepted");
}

static void TestDormantBoundary()
{
    var root = FindRepositoryRoot();
    var protocol = File.ReadAllText(Path.Combine(root, "SystemModule", "Packet",
        "YbDbNewBieGiftConsumeProtocol.cs"));
    var client = File.ReadAllText(Path.Combine(root, "GameSvr", "Services",
        "YbDbClient.cs"));
    var bridge = File.ReadAllText(Path.Combine(root, "GameSvr", "ScriptSystem",
        "PasEngine", "PasApiBridge.cs"));
    var functions = Slice(bridge, "public bool CallPlayerFunc",
        "public bool CallNpcMethod");
    var body = ExtractThroughFirstReturn(functions, "newbiegiftconsume");

    Require(body, "return RejectUnsupportedNativeApi(out result);",
        "NewBieGiftConsume PAS entry is not fail-closed");
    Equal(1, Regex.Matches(bridge,
        "case \\\"newbiegiftconsume\\\":",
        RegexOptions.CultureInvariant).Count,
        "NewBieGiftConsume PAS case count");
    Reject(client, "RequestNewBieGiftConsume(",
        "NewBieGiftConsume sender opened without the central service");
    Reject(client, "YbDbNewBieGiftConsumeProtocol",
        "1125 runtime dispatch opened through the dormant protocol");
    Assert(!Regex.IsMatch(client,
            @"\bcase\s+1125\s*:|(?:frame|queued\.Frame)\.Ident\s*(?:==|is)\s*1125",
            RegexOptions.CultureInvariant),
        "1125 literal runtime dispatch was opened");

    foreach (var forbidden in new[]
             {
                 "TPlayObject", "MySql", "YBConsume(",
                 "SetPlayerVar", "Give(", "SendMsg(", "ProcessCompletions"
             })
        Reject(protocol, forbidden,
            "dormant protocol gained a runtime dependency: " + forbidden);
}

static YbDbLegacy77Frame BuildResponse(int result, int operation,
    string roleName, int currentYuanbao, int totalConsumed,
    int remainingSeconds, int dividendConsumed)
{
    var payload = new byte[32];
    var roleBytes = Encoding.GetEncoding(936).GetBytes(roleName);
    Assert(roleBytes.Length <= 15, "test role exceeds native slot");
    payload[0] = (byte)roleBytes.Length;
    roleBytes.CopyTo(payload, 1);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(16, 4),
        currentYuanbao);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(20, 4),
        totalConsumed);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(24, 4),
        remainingSeconds);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(28, 4),
        dividendConsumed);
    return new YbDbLegacy77Frame(result, operation,
        YbDbNewBieGiftConsumeProtocol.ResponseIdent, payload);
}

static YbDbNewBieGiftConsumeProtocol.Response Decode(
    YbDbLegacy77Frame frame)
{
    Assert(YbDbNewBieGiftConsumeProtocol.TryDecodeResponse(frame,
        out var response, out var error), error);
    return response;
}

static void AssertAck(YbDbLegacy77Frame ack, ushort ident,
    int transactionToken)
{
    Equal(YbDbNewBieGiftConsumeProtocol.ResponseIdent,
        ack.QueryId, "ACK QueryId");
    Equal(transactionToken, ack.Param, "ACK transaction token");
    Equal(ident, ack.Ident, "ACK Ident");
    Equal(0, ack.Payload.Length, "ACK payload length");
    Assert(YbDbLegacy77Codec.TryEncode(ack, out var wire, out var error),
        error);
    Equal(16, wire.Length, "ACK wire length");
    EqualUInt(YbDbLegacy77Codec.FrameMagic,
        BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(0, 4)),
        "ACK magic");
    Equal(YbDbNewBieGiftConsumeProtocol.ResponseIdent,
        BinaryPrimitives.ReadInt32LittleEndian(wire.AsSpan(4, 4)),
        "ACK wire QueryId");
    Equal(transactionToken,
        BinaryPrimitives.ReadInt32LittleEndian(wire.AsSpan(8, 4)),
        "ACK wire transaction token");
    Equal(ident,
        BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(12, 2)),
        "ACK wire Ident");
    Equal(0,
        BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(14, 2)),
        "ACK wire payload length");
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

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
}

static void EqualText(string expected, string actual, string message)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
        throw new InvalidOperationException(
            $"{message}: expected [{expected}], actual [{actual}]");
}

static void EqualUInt(uint expected, uint actual, string message)
{
    if (expected != actual)
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
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
