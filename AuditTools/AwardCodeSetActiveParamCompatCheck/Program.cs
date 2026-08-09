using System.Buffers.Binary;
using System.Text;
using SystemModule.Packet;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var gbk = Encoding.GetEncoding(936,
    EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

var tests = new (string Name, Action Run)[]
{
    ("type-2 task layout", TaskLayout),
    ("signed active parameter", SignedActiveParameter),
    ("owner gate", OwnerGate),
    ("callback mapping", CallbackMapping),
    ("native SQL and queue constants", NativeConstants)
};

foreach (var test in tests) test.Run();
Console.WriteLine(
    $"AwardCodeSetActiveParamCompatCheck PASS tests={tests.Length} " +
    "task=2/104 active=+68 owner=0|self callback=2|5 codec=layout");
return;

void TaskLayout()
{
    const long playerId = 0x1122334455667788;
    True(NativeAwardCodeSetActiveParamTaskCodec.TryEncode(
        " CODE%_ ", -2, playerId, "测试角色",
        out var payload, out var error), error);
    Equal(104, payload.Length, "payload size");
    Sequence(gbk.GetBytes(" CODE%_ "),
        payload.AsSpan(1, payload[0]), "code bytes/no trim");
    Equal(-2, BinaryPrimitives.ReadInt32LittleEndian(
        payload.AsSpan(68, 4)), "ActiveParam offset");
    Equal(playerId, BinaryPrimitives.ReadInt64LittleEndian(
        payload.AsSpan(80, 8)), "PlayerId offset");
    Sequence(gbk.GetBytes("测试角色"),
        payload.AsSpan(89, payload[88]), "role bytes");

    True(NativeAwardCodeSetActiveParamTaskCodec.TryDecode(payload,
        out var decoded, out error), error);
    Sequence(gbk.GetBytes(" CODE%_ "), decoded.CodeBytes, "decoded code");
    Equal(-2, decoded.ActiveParam, "decoded ActiveParam");
    Equal(playerId, decoded.PlayerId, "decoded PlayerId");
    Sequence(gbk.GetBytes("测试角色"), decoded.RoleNameBytes,
        "decoded role");
}

void SignedActiveParameter()
{
    foreach (var value in new[] { int.MinValue, -2, -1, 0, 1, int.MaxValue })
    {
        True(NativeAwardCodeSetActiveParamTaskCodec.TryEncode(
            "CODE", value, 1, "角色", out var payload, out var error), error);
        True(NativeAwardCodeSetActiveParamTaskCodec.TryDecode(payload,
            out var decoded, out error), error);
        Equal(value, decoded.ActiveParam, "signed ActiveParam round trip");
    }
}

void OwnerGate()
{
    False(NativeAwardCodeSetActiveParamTaskCodec.CanUpdate(-1, 0, 10),
        "SQL query error passed owner gate");
    False(NativeAwardCodeSetActiveParamTaskCodec.CanUpdate(0, 0, 10),
        "missing row passed owner gate");
    True(NativeAwardCodeSetActiveParamTaskCodec.CanUpdate(1, 0, 10),
        "unowned code failed owner gate");
    True(NativeAwardCodeSetActiveParamTaskCodec.CanUpdate(1, 10, 10),
        "same owner failed owner gate");
    False(NativeAwardCodeSetActiveParamTaskCodec.CanUpdate(1, 11, 10),
        "different owner passed owner gate");
}

void CallbackMapping()
{
    var code = gbk.GetBytes("兑换码");
    var success = NativeAwardCodeSetActiveParamTaskCodec.CreateCallback(
        true, code, 700, -2);
    Equal(2, success.Result, "success result");
    Equal(700, success.AwardCodeType, "success selected AwardCodeType");
    Equal(-2, success.ActiveParam, "success requested ActiveParam");
    Sequence(code, success.CodeBytes, "success code");

    var failure = NativeAwardCodeSetActiveParamTaskCodec.CreateCallback(
        false, code, 700, -2);
    Equal(5, failure.Result, "failure result");
    Equal(0, failure.AwardCodeType, "failure AwardCodeType");
    Equal(0, failure.ActiveParam, "failure ActiveParam");
    Sequence(code, failure.CodeBytes, "failure code");
}

void NativeConstants()
{
    Equal((byte)2, NativeAwardCodeSetActiveParamTaskCodec.TaskType,
        "task type");
    Equal(68, NativeAwardCodeSetActiveParamTaskCodec.ActiveParamOffset,
        "ActiveParam offset constant");
    Equal(200,
        NativeAwardCodeSetActiveParamTaskCodec.MinimumQueueAgeMilliseconds,
        "minimum queue age");
    Equal("@AwardCodeExecCallBack",
        NativeAwardCodeSetActiveParamTaskCodec.CallbackLabel,
        "callback label");
    Equal("Select AwardCodeType,ActiveParam,ScriptParam1,ScriptParam2," +
          "OwnerPlayerID,OwnerChrName from gamedata.awardcodes " +
          "where AwardCode like '%s';",
        NativeAwardCodeSetActiveParamTaskCodec.SelectSqlFormat,
        "select SQL");
    Equal("Update gamedata.awardcodes  set ActiveParam = %d, " +
          "OwnerPlayerID = %d, OwnerChrName = '%s', " +
          "ModifyDate = Now() where AwardCode like '%s';",
        NativeAwardCodeSetActiveParamTaskCodec.UpdateSqlFormat,
        "update SQL");
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
