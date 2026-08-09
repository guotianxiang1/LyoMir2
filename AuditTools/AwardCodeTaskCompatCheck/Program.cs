using System.Buffers.Binary;
using System.Text;
using SystemModule.Packet;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var gbk = Encoding.GetEncoding(936,
    EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

var tests = new (string Name, Action Run)[]
{
    ("query layout", QueryLayout),
    ("native byte truncation", NativeByteTruncation),
    ("strict task boundary", StrictTaskBoundary),
    ("callback mapping", CallbackMapping),
    ("native constants", NativeConstants)
};

foreach (var test in tests) test.Run();
Console.WriteLine(
    $"AwardCodeTaskCompatCheck PASS tests={tests.Length} " +
    "task=3/104 queue-age=200ms callback=0/1 codec=layout");
return;

void QueryLayout()
{
    const long playerId = 0x1122334455667788;
    True(NativeAwardCodeTaskCodec.TryEncodeQuery(" 兑换ABC ", playerId,
        "测试角色", out var payload, out var error), error);
    Equal(104, payload.Length, "payload size");
    Equal(gbk.GetByteCount(" 兑换ABC "), payload[0], "code byte length");
    Sequence(gbk.GetBytes(" 兑换ABC "),
        payload.AsSpan(1, payload[0]), "code bytes/no trim");
    Equal(playerId,
        BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(80, 8)),
        "64-bit PlayerId");
    Equal(gbk.GetByteCount("测试角色"), payload[88], "role byte length");
    Sequence(gbk.GetBytes("测试角色"),
        payload.AsSpan(89, payload[88]), "role bytes");
    True(payload.AsSpan(61, 19).ToArray().All(value => value == 0),
        "deterministic helper padding was not zero");

    True(NativeAwardCodeTaskCodec.TryDecodeQuery(payload,
        out var decoded, out error), error);
    Equal(playerId, decoded.PlayerId, "decoded PlayerId");
    Sequence(gbk.GetBytes(" 兑换ABC "), decoded.CodeBytes, "decoded code");
    Sequence(gbk.GetBytes("测试角色"), decoded.RoleNameBytes, "decoded role");
}

void NativeByteTruncation()
{
    var code = new string('A', 61);
    var role = new string('B', 16);
    True(NativeAwardCodeTaskCodec.TryEncodeQuery(code, -1, role,
        out var payload, out var error), error);
    Equal(60, payload[0], "code truncation length");
    Sequence(gbk.GetBytes(new string('A', 60)),
        payload.AsSpan(1, 60), "code truncation bytes");
    Equal(15, payload[88], "role truncation length");
    Sequence(gbk.GetBytes(new string('B', 15)),
        payload.AsSpan(89, 15), "role truncation bytes");
    Equal(-1L, BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(80, 8)),
        "signed PlayerId bits");

    var splitGbk = "A" + new string('中', 30);
    True(NativeAwardCodeTaskCodec.TryEncodeQuery(splitGbk, 1, "角色",
        out payload, out error), error);
    Equal(60, payload[0], "raw GBK byte truncation length");
    Equal(gbk.GetBytes("中")[0], payload[60],
        "native truncation did not preserve the split lead byte");
}

void StrictTaskBoundary()
{
    False(NativeAwardCodeTaskCodec.TryDecodeQuery(new byte[103],
        out _, out _), "103-byte task decoded");
    False(NativeAwardCodeTaskCodec.TryDecodeQuery(new byte[105],
        out _, out _), "105-byte task decoded");

    var invalidCode = new byte[104];
    invalidCode[0] = 61;
    False(NativeAwardCodeTaskCodec.TryDecodeQuery(invalidCode,
        out _, out _), "oversized code ShortString decoded");

    var invalidRole = new byte[104];
    invalidRole[88] = 16;
    False(NativeAwardCodeTaskCodec.TryDecodeQuery(invalidRole,
        out _, out _), "oversized role ShortString decoded");
}

void CallbackMapping()
{
    var code = gbk.GetBytes("兑换码");
    var hit = NativeAwardCodeTaskCodec.CreateQueryCallback(
        1, code, 7, 9);
    Equal(1, hit.Result, "hit result");
    Equal(7, hit.AwardCodeType, "hit type");
    Equal(9, hit.ActiveParam, "hit active parameter");
    Sequence(code, hit.CodeBytes, "hit code");

    var miss = NativeAwardCodeTaskCodec.CreateQueryCallback(
        0, code, 7, 9);
    Equal(0, miss.Result, "miss result");
    Equal(0, miss.AwardCodeType, "miss type");
    Equal(0, miss.ActiveParam, "miss active parameter");

    var sqlError = NativeAwardCodeTaskCodec.CreateQueryCallback(
        -1, code, 7, 9);
    Equal(0, sqlError.Result, "SQL error mapping");
    Equal(0, sqlError.AwardCodeType, "SQL error type");
    Equal(0, sqlError.ActiveParam, "SQL error active parameter");
}

void NativeConstants()
{
    Equal((byte)3, NativeAwardCodeTaskCodec.QueryTaskType, "query task type");
    Equal(200, NativeAwardCodeTaskCodec.MinimumQueueAgeMilliseconds,
        "minimum queue age");
    Equal("@AwardCodeExecCallBack", NativeAwardCodeTaskCodec.CallbackLabel,
        "callback label");
    Equal("Select AwardCodeType,ActiveParam,ScriptParam1,ScriptParam2," +
          "OwnerPlayerID,OwnerChrName from gamedata.awardcodes " +
          "where AwardCode like '%s';",
        NativeAwardCodeTaskCodec.QuerySqlFormat, "native query SQL");
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

static void Sequence(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual,
    string name)
{
    if (!expected.SequenceEqual(actual))
        throw new InvalidOperationException(name + " bytes differ");
}
