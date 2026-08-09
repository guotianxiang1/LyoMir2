using System.Buffers.Binary;
using SystemModule.Packet;

var tests = new (string Name, Action Run)[]
{
    ("preflight order", PreflightOrder),
    ("203 request", Request203),
    ("1203 response", Response1203),
    ("strict response validation", StrictResponseValidation),
    ("monthly grant gate", MonthlyGrantGate)
};

foreach (var test in tests) test.Run();
Console.WriteLine(
    $"FeeUserActivityProtocolCompatCheck PASS tests={tests.Length} " +
    "request=203/64 response=1203/32 reward=1000000 integration=dormant");
return;

static void PreflightOrder()
{
    var currentDate = new DateTime(2026, 7, 20);
    var month = YbDbFeeUserActivityProtocol.GetMonthSerial(currentDate);
    Equal((ushort)24319, month, "native year*12+month serial");

    True(YbDbFeeUserActivityProtocol.TryGetPreflightDialog(0, month,
        currentDate, out var dialog), "zero time passed preflight");
    Equal("您的剩余时间不足", dialog, "time gate precedes month gate");

    True(YbDbFeeUserActivityProtocol.TryGetPreflightDialog(1, month,
        currentDate, out dialog), "claimed month passed preflight");
    Equal("我记得您已经参与过了，难道我记错了？？", dialog,
        "claimed-month dialog");

    False(YbDbFeeUserActivityProtocol.TryGetPreflightDialog(1,
        (ushort)(month - 1), currentDate, out dialog),
        "eligible request was blocked");
    Equal(string.Empty, dialog, "eligible preflight dialog");
}

static void Request203()
{
    var identity = new YbDbLegacy77Identity
    {
        Field0 = "ptid-001",
        Field11 = "ptid-001",
        RoleName = "月奖角色",
        Field48 = "192.0.2.20"
    };
    var currentDate = new DateTime(2026, 7, 20);

    True(YbDbFeeUserActivityProtocol.TryCreateRequest(identity, currentDate,
        out var request, out var error), error);
    Equal(24319, request.QueryId, "203 Param1 month serial");
    Equal(1, request.Param, "203 Param2");
    Equal((ushort)203, request.Ident, "203 Ident");
    Equal(64, request.Payload.Length, "203 identity payload size");

    True(YbDbLegacy77Codec.TryDecodeIdentity(request.Payload,
        out var decoded, out error), error);
    Equal("月奖角色", decoded.RoleName, "203 role");

    True(YbDbLegacy77Codec.TryEncode(request, out var bytes, out error), error);
    Equal(80, bytes.Length, "203 wire size");
    Equal(24319, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4, 4)),
        "203 wire Param1");
    Equal(1, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8, 4)),
        "203 wire Param2");
    Equal((ushort)203,
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(12, 2)),
        "203 wire Ident");
    Equal((ushort)64,
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(14, 2)),
        "203 wire payload size");

    False(YbDbFeeUserActivityProtocol.TryCreateRequest(null, currentDate,
        out _, out _), "203 accepted null identity");
}

static void Response1203()
{
    var month = YbDbFeeUserActivityProtocol.GetMonthSerial(
        new DateTime(2026, 7, 20));
    var payload = NewResponsePayload("月奖角色", 101, 202, 303, 404);
    var frame = new YbDbLegacy77Frame(7, month, 1203, payload);

    True(YbDbFeeUserActivityProtocol.TryDecodeResponse(frame,
        out var response, out var error), error);
    Equal(7, response.Result, "1203 result");
    Equal((int)month, response.MonthSerial, "1203 month serial");
    Equal("月奖角色", response.RoleName, "1203 role");
    Equal(101, response.CurrentYuanbao, "1203 current yuanbao");
    Equal(202, response.TotalConsumed, "1203 total consumed");
    Equal(303, response.RemainingSeconds, "1203 remaining seconds");
    Equal(404, response.DividendConsumed, "1203 dividend consumed");

    var signed = new YbDbLegacy77Frame(int.MinValue, -1, 1203,
        NewResponsePayload("失败角色", -1, int.MinValue, int.MaxValue, -2));
    True(YbDbFeeUserActivityProtocol.TryDecodeResponse(signed,
        out response, out error), error);
    Equal(int.MinValue, response.Result, "1203 signed result");
    Equal(-1, response.MonthSerial, "1203 signed Param2");
    Equal(-1, response.CurrentYuanbao, "1203 signed yuanbao");
    Equal(int.MinValue, response.TotalConsumed, "1203 signed consumed");
    Equal(int.MaxValue, response.RemainingSeconds, "1203 signed time");
    Equal(-2, response.DividendConsumed, "1203 signed dividend");
}

static void StrictResponseValidation()
{
    var payload = NewResponsePayload("角色", 1, 2, 3, 4);
    False(YbDbFeeUserActivityProtocol.TryDecodeResponse(null,
        out _, out _), "null 1203 decoded");
    False(YbDbFeeUserActivityProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(1, 2, 1202, payload), out _, out _),
        "wrong response Ident decoded");
    False(YbDbFeeUserActivityProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(1, 2, 1203, payload[..31]), out _, out _),
        "31-byte response decoded");
    False(YbDbFeeUserActivityProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(1, 2, 1203, new byte[33]), out _, out _),
        "33-byte response decoded");

    var oversizedRole = (byte[])payload.Clone();
    oversizedRole[0] = 16;
    False(YbDbFeeUserActivityProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(1, 2, 1203, oversizedRole), out _, out _),
        "oversized role decoded");

    var invalidRole = (byte[])payload.Clone();
    invalidRole[0] = 1;
    invalidRole[1] = 0x81;
    False(YbDbFeeUserActivityProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(1, 2, 1203, invalidRole), out _, out _),
        "invalid GBK role decoded");
}

static void MonthlyGrantGate()
{
    var currentDate = new DateTime(2026, 7, 20);
    var currentMonth = YbDbFeeUserActivityProtocol.GetMonthSerial(currentDate);
    True(YbDbFeeUserActivityProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(1, currentMonth, 1203,
            NewResponsePayload("角色", 1, 2, 3, 4)),
        out var eligible, out var error), error);

    True(YbDbFeeUserActivityProtocol.ShouldGrant(eligible, currentDate,
        (ushort)(currentMonth - 1)), "eligible response did not grant");
    False(YbDbFeeUserActivityProtocol.ShouldGrant(eligible, currentDate,
        currentMonth), "same-month replay granted");

    True(YbDbFeeUserActivityProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(0, currentMonth, 1203,
            NewResponsePayload("角色", 9, 8, 7, 6)),
        out var failed, out error), error);
    Equal(9, failed.CurrentYuanbao,
        "failed response did not preserve authoritative snapshot");
    False(YbDbFeeUserActivityProtocol.ShouldGrant(failed, currentDate,
        (ushort)(currentMonth - 1)), "zero result granted");

    True(YbDbFeeUserActivityProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(1, currentMonth - 1, 1203,
            NewResponsePayload("角色", 1, 2, 3, 4)),
        out var stale, out error), error);
    False(YbDbFeeUserActivityProtocol.ShouldGrant(stale, currentDate,
        (ushort)(currentMonth - 2)), "stale month response granted");

    Equal(1_000_000, YbDbFeeUserActivityProtocol.SuccessExperience,
        "native success experience");
    Equal("@UpFeeUserAct_OK",
        YbDbFeeUserActivityProtocol.SuccessScriptLabel,
        "native success script label");
}

static byte[] NewResponsePayload(string roleName, int currentYuanbao,
    int totalConsumed, int remainingSeconds, int dividendConsumed)
{
    var payload = new byte[YbDbFeeUserActivityProtocol.ResponsePayloadSize];
    WriteStrictGbkShortString(payload, 0,
        YbDbFeeUserActivityProtocol.RoleNameMaximumGbkBytes, roleName);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(16, 4),
        currentYuanbao);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(20, 4),
        totalConsumed);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(24, 4),
        remainingSeconds);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(28, 4),
        dividendConsumed);
    return payload;
}

static void WriteStrictGbkShortString(byte[] destination, int offset,
    int maximumLength, string value)
{
    System.Text.Encoding.RegisterProvider(
        System.Text.CodePagesEncodingProvider.Instance);
    var encoding = System.Text.Encoding.GetEncoding(936,
        System.Text.EncoderFallback.ExceptionFallback,
        System.Text.DecoderFallback.ExceptionFallback);
    var bytes = encoding.GetBytes(value);
    if (bytes.Length > maximumLength)
        throw new InvalidOperationException("test ShortString exceeds its slot");
    destination[offset] = (byte)bytes.Length;
    bytes.CopyTo(destination, offset + 1);
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
