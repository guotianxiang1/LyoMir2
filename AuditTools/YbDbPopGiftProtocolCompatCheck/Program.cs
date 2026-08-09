using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using SystemModule.Packet;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
TestRequest();
TestResponse();
TestInvalidResponse();
TestDialogs();
TestAck();
TestDormantBoundary();

Console.WriteLine(
    "YbDbPopGiftProtocolCompatCheck PASS request=123/64 response=1123/84 " +
    "ACK=105/106 role=current-name flag=raw logs=53 runtime=closed");
return;

static void TestRequest()
{
    var identity = new YbDbLegacy77Identity
    {
        Field0 = "1234567890-TRUNCATE",
        Field11 = "1234567890-TRUNCATE",
        RoleName = "礼包勇士",
        Field48 = "127.0.0.1"
    };
    Assert(YbDbPopGiftProtocol.TryCreateRequest(identity, 17,
        out var request, out var error), error);
    Equal(0, request.QueryId, "request QueryId");
    Equal(31, request.Param, "remaining bag slots");
    Equal(YbDbPopGiftProtocol.RequestIdent, request.Ident, "request Ident");
    Equal(YbDbLegacy77Codec.IdentitySize, request.Payload.Length,
        "request payload length");
    Equal(10, request.Payload[0], "native PTID narrow truncation");
    Equal(19, request.Payload[11], "native PTID wide length");

    Assert(YbDbLegacy77Codec.TryEncode(request, out var wire, out error), error);
    Equal(80, wire.Length, "request wire length");
    EqualUInt(YbDbLegacy77Codec.FrameMagic,
        BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(0, 4)),
        "request magic");
    Equal(31, BinaryPrimitives.ReadInt32LittleEndian(wire.AsSpan(8, 4)),
        "request wire Param");
    Equal(YbDbPopGiftProtocol.RequestIdent,
        BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(12, 2)),
        "request wire Ident");
    Equal(64, BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(14, 2)),
        "request wire payload length");

    Assert(YbDbPopGiftProtocol.TryCreateRequest(identity, 49,
        out request, out error), error);
    Equal(-1, request.Param, "over-capacity bag remains negative");
}

static void TestResponse()
{
    var payload = BuildResponsePayload("礼包勇士", "疗伤药", 1, 3);
    var frame = new YbDbLegacy77Frame(93_847, unchecked((int)0x88776655),
        YbDbPopGiftProtocol.ResponseIdent, payload);
    Assert(YbDbPopGiftProtocol.TryDecodeResponse(frame,
        out var response, out var error), error);
    Equal(93_847, response.Result, "response result/token");
    Equal(unchecked((int)0x88776655), response.IgnoredHeaderParam,
        "ignored response Param preserved");
    EqualText("礼包勇士", response.RoleName, "response role");
    EqualText("疗伤药", response.ItemName, "response item");
    Equal(1, response.NativeItemFlag, "response native item flag");
    Assert(response.NativeItemFlagIsOne, "flag == 1 not exposed");
    Equal(3, response.ItemCount, "response item count");
    Assert(response.Succeeded, "positive response not successful");

    payload[79] = 2;
    Assert(YbDbPopGiftProtocol.TryDecodeResponse(frame,
        out response, out error), error);
    Assert(!response.NativeItemFlagIsOne,
        "native must only interpret item flag exactly equal to one");
}

static void TestInvalidResponse()
{
    Assert(!YbDbPopGiftProtocol.TryDecodeResponse(null,
        out _, out _), "null response accepted");
    Assert(!YbDbPopGiftProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(1, 0, 1122, new byte[84]),
        out _, out _), "wrong response Ident accepted");
    Assert(!YbDbPopGiftProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(1, 0, 1123, new byte[83]),
        out _, out _), "short response accepted");
    Assert(!YbDbPopGiftProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(1, 0, 1123, new byte[85]),
        out _, out _), "long response accepted");

    var invalidRoleLength = new byte[84];
    invalidRoleLength[32] = 16;
    Assert(!YbDbPopGiftProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(1, 0, 1123, invalidRoleLength),
        out _, out _), "16-byte role accepted");

    var invalidItemLength = new byte[84];
    invalidItemLength[64] = 15;
    Assert(!YbDbPopGiftProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(1, 0, 1123, invalidItemLength),
        out _, out _), "15-byte item accepted");

    var invalidGbk = new byte[84];
    invalidGbk[32] = 1;
    invalidGbk[33] = 0x81;
    Assert(!YbDbPopGiftProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(1, 0, 1123, invalidGbk),
        out _, out _), "invalid GBK role accepted");
}

static void TestDialogs()
{
    var response = Decode(-2, "裁决之杖", 2);
    Assert(YbDbPopGiftProtocol.TryGetFailureDialog(response, out var dialog),
        "result -2 has no dialog");
    EqualText("您背包不足，无法领取道具：裁决之杖 数量:2" +
              " \\ \\ <离开/@exit>", dialog, "result -2 dialog");

    response = Decode(-1, string.Empty, 0);
    Assert(YbDbPopGiftProtocol.TryGetFailureDialog(response, out dialog),
        "result -1 has no dialog");
    EqualText(YbDbPopGiftProtocol.NoAwardDialog, dialog, "result -1 dialog");

    response = Decode(0, string.Empty, 0);
    Assert(YbDbPopGiftProtocol.TryGetFailureDialog(response, out dialog),
        "result 0 has no dialog");
    EqualText(YbDbPopGiftProtocol.GenericFailureDialog, dialog,
        "result 0 dialog");

    Assert(!YbDbPopGiftProtocol.TryGetFailureDialog(
        Decode(1, "疗伤药", 1), out _), "positive result has failure dialog");
    EqualText("成功领取了3张灵符",
        YbDbPopGiftProtocol.BuildLingFuSuccessDialog(3),
        "LingFu success dialog");
    EqualText("成功领取了 疗伤药 3",
        YbDbPopGiftProtocol.BuildItemSuccessDialog("疗伤药", 3),
        "item success dialog");
}

static void TestAck()
{
    Assert(YbDbPopGiftProtocol.TryCreateAck(93_847, true,
        out var ack, out var error), error);
    AssertAck(ack, YbDbPopGiftProtocol.SuccessAckIdent, 93_847);
    Assert(YbDbPopGiftProtocol.TryCreateAck(93_847, false,
        out ack, out error), error);
    AssertAck(ack, YbDbPopGiftProtocol.FailureAckIdent, 93_847);
    Assert(!YbDbPopGiftProtocol.TryCreateAck(0, true,
        out _, out _), "zero transaction ACK accepted");
    Assert(!YbDbPopGiftProtocol.TryCreateAck(-1, false,
        out _, out _), "negative transaction ACK accepted");
}

static void TestDormantBoundary()
{
    var root = FindRepositoryRoot();
    var protocol = File.ReadAllText(Path.Combine(root, "SystemModule", "Packet",
        "YbDbPopGiftProtocol.cs"));
    var client = File.ReadAllText(Path.Combine(root, "GameSvr", "Services",
        "YbDbClient.cs"));
    var bridge = File.ReadAllText(Path.Combine(root, "GameSvr", "ScriptSystem",
        "PasEngine", "PasApiBridge.cs"));
    var methods = Slice(bridge, "public bool CallNpcMethod",
        "public bool CallNpcFunc");
    var body = ExtractThroughFirstReturn(methods, "reqpopgift");

    Require(body, "return RejectUnsupportedNativeApi(out result);",
        "ReqPopGift PAS entry is not fail-closed");
    Equal(1, Regex.Matches(bridge, "case \\\"reqpopgift\\\":",
        RegexOptions.CultureInvariant).Count,
        "ReqPopGift PAS case count");
    Reject(client, "RequestPopGift(",
        "ReqPopGift runtime sender was opened without the central service");
    Reject(client, "YbDbPopGiftProtocol.ResponseIdent",
        "1123 runtime dispatch was opened through the completion constant");
    Assert(!Regex.IsMatch(client,
            @"(?:frame|queued\.Frame)\.Ident\s*==?\s*1123",
            RegexOptions.CultureInvariant),
        "1123 literal runtime dispatch was opened");
    Reject(protocol, "TPlayObject",
        "dormant pop-gift codec gained a player dependency");
    Reject(protocol, "Bind",
        "unproven native item flag was mapped to C# Bind");
    Reject(protocol, "SendMsg(",
        "dormant pop-gift codec gained player messaging");
}

static YbDbPopGiftProtocol.Response Decode(int result, string itemName,
    int count)
{
    var frame = new YbDbLegacy77Frame(result, 0,
        YbDbPopGiftProtocol.ResponseIdent,
        BuildResponsePayload("礼包勇士", itemName, 0, count));
    Assert(YbDbPopGiftProtocol.TryDecodeResponse(frame,
        out var response, out var error), error);
    return response;
}

static byte[] BuildResponsePayload(string roleName, string itemName,
    byte nativeFlag, int count)
{
    var payload = new byte[YbDbPopGiftProtocol.ResponsePayloadSize];
    WriteShortString(payload, 32, 15, roleName);
    WriteShortString(payload, 64, 14, itemName);
    payload[79] = nativeFlag;
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(80, 4), count);
    return payload;
}

static void WriteShortString(byte[] payload, int offset, int capacity,
    string value)
{
    var bytes = Encoding.GetEncoding(936).GetBytes(value ?? string.Empty);
    Assert(bytes.Length <= capacity, "test short string exceeds native slot");
    payload[offset] = (byte)bytes.Length;
    bytes.CopyTo(payload, offset + 1);
}

static void AssertAck(YbDbLegacy77Frame ack, ushort ident,
    int transactionToken)
{
    Equal(YbDbPopGiftProtocol.ResponseIdent, ack.QueryId, "ACK QueryId");
    Equal(transactionToken, ack.Param, "ACK transaction token");
    Equal(ident, ack.Ident, "ACK Ident");
    Equal(0, ack.Payload.Length, "ACK payload length");
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
