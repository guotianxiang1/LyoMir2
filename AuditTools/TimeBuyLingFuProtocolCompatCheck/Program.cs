using System.Buffers.Binary;
using SystemModule.Packet;

var tests = new (string Name, Action Run)[]
{
    ("201 request", Request201),
    ("1201 response", Response1201),
    ("strict response validation", StrictResponseValidation),
    ("failure dialogs", FailureDialogs),
    ("105/106 ACK", AckFrames)
};

foreach (var test in tests) test.Run();
Console.WriteLine(
    $"TimeBuyLingFuProtocolCompatCheck PASS tests={tests.Length} " +
    "request=201/64 response=1201/152 ACK=105|106 integration=dormant");
return;

static void Request201()
{
    var identity = new YbDbLegacy77Identity
    {
        Field0 = "ptid-001",
        Field11 = "ptid-001",
        RoleName = "时间角色",
        Field48 = "192.0.2.10"
    };

    True(YbDbTimeBuyLingFuProtocol.TryCreateRequest(identity, 1000,
        out var request, out var error), error);
    Equal(0, request.QueryId, "201 Param1");
    Equal(1000, request.Param, "201 Param2 Num");
    Equal(YbDbTimeBuyLingFuProtocol.RequestIdent, request.Ident, "201 Ident");
    Equal(YbDbLegacy77Codec.IdentitySize, request.Payload.Length,
        "201 identity payload length");

    True(YbDbLegacy77Codec.TryDecodeIdentity(request.Payload,
        out var decodedIdentity, out error), error);
    Equal("ptid-001", decodedIdentity.Field0, "201 narrow PTID");
    Equal("ptid-001", decodedIdentity.Field11, "201 full PTID");
    Equal("时间角色", decodedIdentity.RoleName, "201 role");
    Equal("192.0.2.10", decodedIdentity.Field48, "201 IP");

    True(YbDbLegacy77Codec.TryEncode(request, out var bytes, out error), error);
    Equal(80, bytes.Length, "201 wire length");
    Equal(0, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4, 4)),
        "201 wire Param1");
    Equal(1000, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8, 4)),
        "201 wire Param2");
    Equal((ushort)201,
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(12, 2)),
        "201 wire Ident");
    Equal((ushort)64,
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(14, 2)),
        "201 wire payload size");

    False(YbDbTimeBuyLingFuProtocol.TryCreateRequest(identity, 0,
        out _, out _), "201 accepted zero Num");
    False(YbDbTimeBuyLingFuProtocol.TryCreateRequest(identity, -1,
        out _, out _), "201 accepted negative Num");
    False(YbDbTimeBuyLingFuProtocol.TryCreateRequest(null, 1,
        out _, out _), "201 accepted null identity");

    var nativeBoundary = new YbDbLegacy77Identity
    {
        Field0 = "123456789中",
        Field11 = "123456789中",
        RoleName = "role",
        Field48 = "ip"
    };
    True(YbDbTimeBuyLingFuProtocol.TryCreateRequest(nativeBoundary, int.MaxValue,
        out var truncated, out error), error);
    Equal(int.MaxValue, truncated.Param, "201 maximum positive Num");
    BytesEqual(Convert.FromHexString("313233343536373839D6"),
        truncated.Payload.AsSpan(1, 10), "201 native CP936 byte truncation");
}

static void Response1201()
{
    var payload = NewResponsePayload("时间角色", "灵符:1000/金创药:2");
    var frame = new YbDbLegacy77Frame(77, 123456, 1201, payload);
    True(YbDbTimeBuyLingFuProtocol.TryDecodeResponse(frame,
        out var response, out var error), error);
    Equal(77, response.Result, "1201 result");
    Equal(123456, response.AuthoritativeRemainingSeconds,
        "1201 authoritative HaveTimeNum");
    Equal("时间角色", response.RoleName, "1201 role");
    Equal("灵符:1000/金创药:2", response.Descriptor, "1201 descriptor");
    True(response.Succeeded, "1201 positive result state");

    var failureFrame = new YbDbLegacy77Frame(-3, int.MinValue, 1201,
        NewResponsePayload("失败角色", string.Empty));
    True(YbDbTimeBuyLingFuProtocol.TryDecodeResponse(failureFrame,
        out response, out error), error);
    Equal(-3, response.Result, "1201 negative result");
    Equal(int.MinValue, response.AuthoritativeRemainingSeconds,
        "1201 signed HaveTimeNum bits");
    False(response.Succeeded, "1201 negative result state");
}

static void StrictResponseValidation()
{
    var valid = NewResponsePayload("角色", "灵符:1");
    False(YbDbTimeBuyLingFuProtocol.TryDecodeResponse(null,
        out _, out _), "null 1201 response decoded");
    False(YbDbTimeBuyLingFuProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(1, 2, 1200, valid), out _, out _),
        "wrong response Ident decoded");
    False(YbDbTimeBuyLingFuProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(1, 2, 1201, valid[..151]), out _, out _),
        "151-byte response decoded");
    False(YbDbTimeBuyLingFuProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(1, 2, 1201, new byte[153]), out _, out _),
        "153-byte response decoded");

    var longRole = (byte[])valid.Clone();
    longRole[YbDbTimeBuyLingFuProtocol.RoleNameOffset] = 16;
    False(YbDbTimeBuyLingFuProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(1, 2, 1201, longRole), out _, out _),
        "oversized role decoded");

    var invalidRole = (byte[])valid.Clone();
    invalidRole[YbDbTimeBuyLingFuProtocol.RoleNameOffset] = 1;
    invalidRole[YbDbTimeBuyLingFuProtocol.RoleNameOffset + 1] = 0x81;
    False(YbDbTimeBuyLingFuProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(1, 2, 1201, invalidRole), out _, out _),
        "invalid GBK role decoded");

    var oversizedDescriptor = (byte[])valid.Clone();
    oversizedDescriptor[YbDbTimeBuyLingFuProtocol.DescriptorOffset] =
        (byte)(YbDbTimeBuyLingFuProtocol.MaximumReadableDescriptorGbkBytes + 1);
    False(YbDbTimeBuyLingFuProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(1, 2, 1201, oversizedDescriptor), out _, out _),
        "descriptor beyond frame boundary decoded");

    var invalidDescriptor = (byte[])valid.Clone();
    invalidDescriptor[YbDbTimeBuyLingFuProtocol.DescriptorOffset] = 1;
    invalidDescriptor[YbDbTimeBuyLingFuProtocol.DescriptorOffset + 1] = 0x81;
    False(YbDbTimeBuyLingFuProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(1, 2, 1201, invalidDescriptor), out _, out _),
        "invalid GBK descriptor decoded");

    var maximumDescriptor = new string('A',
        YbDbTimeBuyLingFuProtocol.MaximumReadableDescriptorGbkBytes);
    True(YbDbTimeBuyLingFuProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(1, 2, 1201,
            NewResponsePayload("R", maximumDescriptor)),
        out var response, out var error), error);
    Equal(maximumDescriptor, response.Descriptor,
        "descriptor at proven frame boundary");
}

static void FailureDialogs()
{
    foreach (var result in new[] { -3, -2 })
    {
        True(YbDbTimeBuyLingFuProtocol.TryGetFailureDialog(result,
            out var dialog), "missing insufficient-time dialog");
        Equal("[失败]：你没有那么多的游戏时间", dialog,
            $"insufficient-time dialog {result}");
    }

    foreach (var result in new[] { int.MinValue, -4, -1, 0 })
    {
        True(YbDbTimeBuyLingFuProtocol.TryGetFailureDialog(result,
            out var dialog), "missing generic failure dialog");
        Equal("[失败]: 你无法购买", dialog,
            $"generic failure dialog {result}");
    }

    False(YbDbTimeBuyLingFuProtocol.TryGetFailureDialog(1,
        out var positiveDialog), "positive result produced failure dialog");
    Equal(string.Empty, positiveDialog, "positive failure dialog output");
}

static void AckFrames()
{
    foreach (var succeeded in new[] { true, false })
    {
        True(YbDbTimeBuyLingFuProtocol.TryCreateAck(77, succeeded,
            out var ack, out var error), error);
        Equal(1201, ack.QueryId, "ACK QueryId");
        Equal(77, ack.Param, "ACK original transaction result");
        Equal(succeeded ? (ushort)105 : (ushort)106,
            ack.Ident, "ACK Ident");
        Equal(0, ack.Payload.Length, "ACK payload length");

        True(YbDbLegacy77Codec.TryEncode(ack, out var bytes, out error), error);
        Equal(16, bytes.Length, "ACK wire length");
        Equal(1201, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4, 4)),
            "ACK wire QueryId");
        Equal(77, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8, 4)),
            "ACK wire Param");
        Equal(succeeded ? (ushort)105 : (ushort)106,
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(12, 2)),
            "ACK wire Ident");
        Equal((ushort)0,
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(14, 2)),
            "ACK wire payload length");
    }

    False(YbDbTimeBuyLingFuProtocol.TryCreateAck(0, true,
        out _, out _), "zero result ACK created");
    False(YbDbTimeBuyLingFuProtocol.TryCreateAck(-1, false,
        out _, out _), "negative result ACK created");
}

static byte[] NewResponsePayload(string roleName, string descriptor)
{
    var payload = new byte[YbDbTimeBuyLingFuProtocol.ResponsePayloadSize];
    WriteStrictGbkShortString(payload,
        YbDbTimeBuyLingFuProtocol.RoleNameOffset,
        YbDbTimeBuyLingFuProtocol.RoleNameMaximumGbkBytes, roleName);
    WriteStrictGbkShortString(payload,
        YbDbTimeBuyLingFuProtocol.DescriptorOffset,
        YbDbTimeBuyLingFuProtocol.MaximumReadableDescriptorGbkBytes, descriptor);
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
