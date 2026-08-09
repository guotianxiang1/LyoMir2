using System.Buffers.Binary;
using SystemModule.Packet;

var tests = new (string Name, Action Run)[]
{
    ("108 request modes", RequestModes),
    ("1108 authoritative double", AuthoritativeDouble),
    ("1108 native single fallback", NativeSingleFallback),
    ("1108 validation boundary", ValidationBoundary)
};

foreach (var test in tests) test.Run();
Console.WriteLine(
    $"YbDbForgeModeProtocolCompatCheck PASS tests={tests.Length} " +
    "request=108/0 response=1108/query-mode integration=runtime");
return;

static void RequestModes()
{
    AssertRequest(false, YbDbForgeModeProtocol.SingleMode,
        "77BBAA3300000000010000006C000000");
    AssertRequest(true, YbDbForgeModeProtocol.DoubleMode,
        "77BBAA3300000000020000006C000000");
}

static void AuthoritativeDouble()
{
    var frame = new YbDbLegacy77Frame(2, int.MinValue,
        YbDbForgeModeProtocol.ResponseIdent, Array.Empty<byte>());
    True(YbDbForgeModeProtocol.TryDecodeResponse(frame,
        out var response, out var error), error);
    Equal(2, response.WireQueryId, "1108 wire QueryId");
    Equal(int.MinValue, response.IgnoredParam, "1108 ignored Param");
    Equal(0, response.IgnoredPayloadLength, "1108 empty payload length");
    Equal(YbDbForgeModeProtocol.DoubleMode, response.Mode,
        "1108 authoritative mode");
    True(response.DoubleForging, "1108 QueryId 2 did not enable double mode");
    Equal("==> 开启元宝双倍锻造", response.ConsoleMessage,
        "1108 double message");
}

static void NativeSingleFallback()
{
    foreach (var queryId in new[] { int.MinValue, -1, 0, 1, 3, int.MaxValue })
    {
        var payload = new byte[] { 1, 2, 3, 4, 5 };
        var frame = new YbDbLegacy77Frame(queryId, 987654,
            YbDbForgeModeProtocol.ResponseIdent, payload);
        True(YbDbForgeModeProtocol.TryDecodeResponse(frame,
            out var response, out var error), error);
        Equal(queryId, response.WireQueryId,
            $"1108 fallback wire QueryId {queryId}");
        Equal(YbDbForgeModeProtocol.SingleMode, response.Mode,
            $"1108 fallback mode {queryId}");
        False(response.DoubleForging,
            $"1108 non-2 QueryId enabled double mode {queryId}");
        Equal(payload.Length, response.IgnoredPayloadLength,
            $"1108 ignored payload length {queryId}");
        Equal("==> 元宝单倍锻造", response.ConsoleMessage,
            $"1108 single message {queryId}");
    }
}

static void ValidationBoundary()
{
    False(YbDbForgeModeProtocol.TryDecodeResponse(null,
        out _, out _), "null 1108 response decoded");
    False(YbDbForgeModeProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(2, 0, 1107, Array.Empty<byte>()),
        out _, out _), "wrong response Ident decoded");

    True(YbDbForgeModeProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(2, 1, 1108, null),
        out var nullPayload, out var error), error);
    Equal(0, nullPayload.IgnoredPayloadLength,
        "native ungated null payload length");
}

static void AssertRequest(bool doubleForging, int expectedMode,
    string expectedHex)
{
    var frame = YbDbForgeModeProtocol.CreateRequest(doubleForging);
    Equal(0, frame.QueryId, "108 QueryId");
    Equal(expectedMode, frame.Param, "108 Param mode");
    Equal((ushort)108, frame.Ident, "108 Ident");
    Equal(0, frame.Payload.Length, "108 payload length");

    True(YbDbLegacy77Codec.TryEncode(frame,
        out var bytes, out var error), error);
    Equal(16, bytes.Length, "108 wire length");
    BytesEqual(Convert.FromHexString(expectedHex), bytes,
        $"108 mode {expectedMode} exact wire");
    Equal(0, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4, 4)),
        "108 wire QueryId");
    Equal(expectedMode,
        BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8, 4)),
        "108 wire Param mode");
    Equal((ushort)0,
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(14, 2)),
        "108 wire payload length");
}

static void BytesEqual(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual,
    string name)
{
    if (!expected.SequenceEqual(actual))
        throw new InvalidOperationException(name + ": byte sequence differs");
}

static void True(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void False(bool condition, string message)
{
    if (condition) throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual, string name) where T : IEquatable<T>
{
    if (!expected.Equals(actual))
        throw new InvalidOperationException(
            $"{name}: expected {expected}, got {actual}");
}
