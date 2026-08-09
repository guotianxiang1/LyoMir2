using System.Buffers.Binary;
using SystemModule.Packet;

var tests = new (string Name, Action Run)[]
{
    ("clear-nick golden frame", ClearNickGoldenFrame),
    ("logout 104 golden frame", LogoutGoldenFrame),
    ("quest-diamond golden frame", QuestDiamondGoldenFrame),
    ("credit 103/1103 protocol", CreditRefreshProtocol),
    ("signed DWORD bit preservation", SignedDwordBitPreservation),
    ("strict frame validation", StrictFrameValidation),
    ("byte-wise split frame", ByteWiseSplitFrame),
    ("sticky and maximum frames", StickyAndMaximumFrames),
    ("noise and split magic", NoiseAndSplitMagic),
    ("oversized resynchronization", OversizedHeaderResynchronization),
    ("magic inside payload", MagicInsidePayload),
    ("zero-payload frame", ZeroPayloadFrame),
    ("strict GBK identities", StrictGbkIdentities),
    ("native CP936 byte truncation", NativeGbkByteTruncation),
    ("1303 identity response", IdentityResponse),
    ("parser reset isolation", ParserResetIsolation),
    ("large noise remains bounded", LargeNoiseRemainsBounded)
};

foreach (var test in tests) test.Run();
Console.WriteLine(
    $"YbDbLegacy77CodecCheck PASS tests={tests.Length} header=16 maxFrame=0x8000 " +
    "GBK=strict+native-byte-truncate stream=split+sticky+resync");

static void ClearNickGoldenFrame()
{
    var identity = new YbDbLegacy77Identity
    {
        Field0 = "acc001",
        Field11 = "acc001",
        RoleName = "测试角色",
        Field48 = "192.168.1.8"
    };
    var identityBytes = EncodeIdentity(identity);
    var expectedIdentity = Convert.FromHexString(
        "0661636330303100000000" +
        "066163633030310000000000000000000000000000" +
        "08B2E2CAD4BDC7C9AB00000000000000" +
        "0B3139322E3136382E312E3800000000");
    BytesEqual(expectedIdentity, identityBytes, "64-byte native identity");

    var encoded = Encode(new YbDbLegacy77Frame(5, 0, 303, identityBytes));
    var expected = Convert.FromHexString(
        "77BBAA3305000000000000002F014000" +
        "0661636330303100000000" +
        "066163633030310000000000000000000000000000" +
        "08B2E2CAD4BDC7C9AB00000000000000" +
        "0B3139322E3136382E312E3800000000");
    Equal(80, encoded.Length, "ClearNick frame length");
    BytesEqual(expected, encoded, "ClearNick golden frame");
    Equal(YbDbLegacy77Codec.FrameMagic,
        BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(0, 4)), "magic +0");
    Equal(5, BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(4, 4)),
        "query id +4");
    Equal(0, BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(8, 4)),
        "param +8");
    Equal((ushort)303,
        BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(12, 2)), "ident +12");
    Equal((ushort)64,
        BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(14, 2)), "payload +14");

    True(YbDbLegacy77Codec.TryDecode(encoded, out var decoded, out var error), error);
    Equal(5, decoded.QueryId, "decoded query id");
    Equal(0, decoded.Param, "decoded param");
    Equal((ushort)303, decoded.Ident, "decoded ident");
    BytesEqual(identityBytes, decoded.Payload, "decoded identity bytes");
}

static void LogoutGoldenFrame()
{
    var identity = new YbDbLegacy77Identity
    {
        Field0 = "acc001",
        Field11 = "acc001",
        RoleName = "测试角色",
        Field48 = "192.168.1.8"
    };
    True(YbDbLogoutProtocol.TryCreateRequest(identity,
        out var request, out var requestError), requestError);
    var encoded = Encode(request);
    var expected = Convert.FromHexString(
        "77BBAA33000000000000000068004000" +
        "0661636330303100000000" +
        "066163633030310000000000000000000000000000" +
        "08B2E2CAD4BDC7C9AB00000000000000" +
        "0B3139322E3136382E312E3800000000");

    Equal(80, encoded.Length, "logout frame length");
    BytesEqual(expected, encoded, "logout 104 golden frame");
    Equal(0, request.QueryId, "logout QueryId");
    Equal(0, request.Param, "logout Param");
    Equal((ushort)104, request.Ident, "logout Ident");
    Equal(64, request.Payload.Length, "logout identity payload length");
    True(YbDbLegacy77Codec.TryDecodeIdentity(request.Payload,
        out var decodedIdentity, out var identityError), identityError);
    Equal("acc001", decodedIdentity.Field0, "logout narrow PTID");
    Equal("acc001", decodedIdentity.Field11, "logout full PTID");
    Equal("测试角色", decodedIdentity.RoleName, "logout role");
    Equal("192.168.1.8", decodedIdentity.Field48, "logout IP");
}

static void QuestDiamondGoldenFrame()
{
    var identity = new YbDbLegacy77Identity
    {
        Field0 = "acc001",
        Field11 = "acc001",
        RoleName = "测试角色",
        Field48 = "192.168.1.8"
    };
    var identityBytes = EncodeIdentity(identity);
    var encoded = Encode(new YbDbLegacy77Frame(
        0, 0x12345678, 122, identityBytes));
    var expected = Convert.FromHexString(
        "77BBAA3300000000785634127A004000" +
        "0661636330303100000000" +
        "066163633030310000000000000000000000000000" +
        "08B2E2CAD4BDC7C9AB00000000000000" +
        "0B3139322E3136382E312E3800000000");

    Equal(80, encoded.Length, "ClientQuestGetDiam frame length");
    BytesEqual(expected, encoded, "ClientQuestGetDiam golden frame");
    Equal(0, BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(4, 4)),
        "ClientQuestGetDiam query +4");
    Equal(0x12345678,
        BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(8, 4)),
        "ClientQuestGetDiam amount +8");
    Equal((ushort)122,
        BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(12, 2)),
        "ClientQuestGetDiam ident +12");
    Equal((ushort)64,
        BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(14, 2)),
        "ClientQuestGetDiam payload +14");
}

static void CreditRefreshProtocol()
{
    var identity = new YbDbLegacy77Identity
    {
        Field0 = "ptid-001",
        Field11 = "ptid-001",
        RoleName = "credit-role",
        Field48 = "192.0.2.10"
    };
    True(YbDbCreditProtocol.TryCreateRefreshRequest(identity, 0xBEEF, true,
        out var request, out var requestError), requestError);
    Equal(1, request.QueryId, "103 QueryId");
    Equal(0x0001BEEF, request.Param, "103 payment and qualification Param");
    Equal((ushort)103, request.Ident, "103 request Ident");
    Equal(64, request.Payload.Length, "103 identity payload length");
    True(YbDbLegacy77Codec.TryDecodeIdentity(request.Payload,
        out var decodedIdentity, out var identityError), identityError);
    Equal("ptid-001", decodedIdentity.Field0, "103 narrow PTID");
    Equal("ptid-001", decodedIdentity.Field11, "103 full PTID");
    Equal("credit-role", decodedIdentity.RoleName, "103 role");
    Equal("192.0.2.10", decodedIdentity.Field48, "103 IP");

    True(YbDbCreditProtocol.TryCreateRefreshRequest(identity, 0xBEEF, false,
        out var unqualified, out requestError), requestError);
    Equal(0xBEEF, unqualified.Param, "103 unqualified Param");

    var payload = new byte[32];
    payload[0] = 11;
    "credit-role"u8.CopyTo(payload.AsSpan(1));
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(16, 4), -1);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(20, 4), 2);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(24, 4), int.MinValue);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(28, 4), int.MaxValue);
    var response = new YbDbLegacy77Frame(9876, 1, 1103, payload);
    True(YbDbCreditProtocol.TryDecodeResponse(response,
        out var snapshot, out var responseError), responseError);
    Equal("credit-role", snapshot.RoleName, "1103 role");
    Equal(-1, snapshot.CurrentYuanbao, "1103 current yuanbao");
    Equal(2, snapshot.TotalConsumed, "1103 total consumed");
    Equal(int.MinValue, snapshot.RemainingSeconds, "1103 remaining seconds");
    Equal(int.MaxValue, snapshot.DividendConsumed, "1103 dividend consumed");
    True(snapshot.ResponseParamIsOne, "1103 Param==1 gate");

    False(YbDbCreditProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(0, 1, 1102, payload), out _, out _),
        "wrong credit response Ident");
    False(YbDbCreditProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(0, 1, 1103, payload[..31]), out _, out _),
        "short credit response payload");
    False(YbDbCreditProtocol.TryDecodeResponse(
        new YbDbLegacy77Frame(0, 1, 1103, new byte[33]), out _, out _),
        "long credit response payload");
    payload[0] = 16;
    False(YbDbCreditProtocol.TryDecodeResponse(response, out _, out _),
        "oversized credit response role");
    payload[0] = 1;
    payload[1] = 0x81;
    False(YbDbCreditProtocol.TryDecodeResponse(response, out _, out _),
        "invalid GBK credit response role");
}

static void SignedDwordBitPreservation()
{
    var queryId = unchecked((int)0x89ABCDEFu);
    var param = unchecked((int)0xFEDCBA98u);
    var encoded = Encode(new YbDbLegacy77Frame(queryId, param, 0xA55A,
        new byte[] { 0xFE, 0xDC }));
    Equal(0x89ABCDEFu,
        BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(4, 4)),
        "high-bit query wire value");
    Equal(0xFEDCBA98u,
        BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(8, 4)),
        "high-bit param wire value");
    True(YbDbLegacy77Codec.TryDecode(encoded, out var decoded, out var error), error);
    Equal(queryId, decoded.QueryId, "high-bit query roundtrip");
    Equal(param, decoded.Param, "high-bit param roundtrip");
}

static void StrictFrameValidation()
{
    False(YbDbLegacy77Codec.TryEncode(null, out _, out _), "null frame encoded");
    False(YbDbLegacy77Codec.TryEncode(
        new YbDbLegacy77Frame(1, 2, 3,
            new byte[YbDbLegacy77Codec.MaximumPayloadLength + 1]), out _, out _),
        "0x8001 frame encoded");

    False(YbDbLegacy77Codec.TryDecode(new byte[15], out _, out _),
        "truncated header decoded");
    var badMagic = new byte[YbDbLegacy77Codec.HeaderSize];
    False(YbDbLegacy77Codec.TryDecode(badMagic, out _, out _),
        "bad magic decoded");

    var declaredOne = Encode(new YbDbLegacy77Frame(1, 2, 3, new byte[] { 4 }));
    False(YbDbLegacy77Codec.TryDecode(declaredOne.AsSpan(0, 16), out _, out _),
        "truncated payload decoded");
    var trailing = Join(declaredOne, new byte[] { 5 });
    False(YbDbLegacy77Codec.TryDecode(trailing, out _, out _),
        "trailing bytes decoded as one frame");

    var oversized = NewHeader(YbDbLegacy77Codec.MaximumPayloadLength + 1);
    False(YbDbLegacy77Codec.TryDecode(oversized, out _, out _),
        "oversized declared frame decoded");
}

static void ByteWiseSplitFrame()
{
    var encoded = Encode(new YbDbLegacy77Frame(101, -7, 303,
        Enumerable.Range(0, 91).Select(i => (byte)i).ToArray()));
    var frames = new List<YbDbLegacy77Frame>();
    var parser = new YbDbLegacy77StreamParser();
    for (var i = 0; i < encoded.Length; i++)
    {
        parser.Append(encoded.AsSpan(i, 1), frames.Add);
        Equal(i == encoded.Length - 1 ? 1 : 0, frames.Count,
            $"byte-wise frame count at {i}");
    }
    Equal(0, parser.BufferedLength, "byte-wise buffered length");
    Equal(101, frames[0].QueryId, "byte-wise query id");
}

static void StickyAndMaximumFrames()
{
    var first = Encode(new YbDbLegacy77Frame(1, 2, 3, new byte[] { 4, 5 }));
    var second = Encode(new YbDbLegacy77Frame(6, 7, 8, Array.Empty<byte>()));
    var frames = ParseOnce(Join(first, second));
    Equal(2, frames.Count, "two sticky frames");
    Equal(1, frames[0].QueryId, "first sticky frame");
    Equal(6, frames[1].QueryId, "second sticky frame");

    var maximumPayload = new byte[YbDbLegacy77Codec.MaximumPayloadLength];
    for (var i = 0; i < maximumPayload.Length; i++) maximumPayload[i] = (byte)(i * 31);
    var maximum = Encode(new YbDbLegacy77Frame(9, 10, 11, maximumPayload));
    Equal(YbDbLegacy77Codec.MaximumFrameLength, maximum.Length, "maximum frame length");
    var tail = Encode(new YbDbLegacy77Frame(12, 13, 14, new byte[] { 15 }));
    frames = ParseOnce(Join(maximum, tail));
    Equal(2, frames.Count, "maximum frame plus tail");
    Equal(maximumPayload.Length, frames[0].Payload.Length, "maximum payload length");
    BytesEqual(maximumPayload, frames[0].Payload, "maximum payload bytes");
    Equal(12, frames[1].QueryId, "maximum tail frame");
}

static void NoiseAndSplitMagic()
{
    var encoded = Encode(new YbDbLegacy77Frame(22, 23, 24, new byte[] { 25 }));
    for (var split = 1; split <= 3; split++)
    {
        var parser = new YbDbLegacy77StreamParser();
        var frames = new List<YbDbLegacy77Frame>();
        parser.Append(Join(new byte[] { 0x10, 0x20, 0x30 },
            encoded.AsSpan(0, split).ToArray()), frames.Add);
        Equal(split, parser.BufferedLength, $"split magic suffix {split}");
        parser.Append(encoded.AsSpan(split), frames.Add);
        Equal(1, frames.Count, $"split magic frame count {split}");
        Equal(22, frames[0].QueryId, $"split magic query {split}");
    }

    var falseSuffix = new YbDbLegacy77StreamParser();
    falseSuffix.Append(new byte[] { 0x77, 0x00, 0xBB, 0xAA }, _ => { });
    Equal(0, falseSuffix.BufferedLength, "non-prefix noise suffix");
}

static void OversizedHeaderResynchronization()
{
    var malformed = NewHeader(YbDbLegacy77Codec.MaximumPayloadLength + 1);
    BinaryPrimitives.WriteInt32LittleEndian(malformed.AsSpan(4, 4), 900);
    var valid = Encode(new YbDbLegacy77Frame(901, 902, 903, new byte[] { 1, 2, 3 }));
    var parser = new YbDbLegacy77StreamParser();
    var frames = new List<YbDbLegacy77Frame>();
    parser.Append(Join(new byte[] { 0x55, 0x66 }, malformed, valid), frames.Add);
    Equal(1, frames.Count, "oversized-header recovery count");
    Equal(901, frames[0].QueryId, "oversized-header recovery query");
    Equal(0, parser.BufferedLength, "oversized-header recovery buffer");
}

static void MagicInsidePayload()
{
    var payload = new byte[48];
    payload[0] = 0x11;
    BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4),
        YbDbLegacy77Codec.FrameMagic);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(18, 2), 0);
    payload[47] = 0x22;
    var outer = Encode(new YbDbLegacy77Frame(1001, 1002, 1003, payload));
    var tail = Encode(new YbDbLegacy77Frame(1004, 1005, 1006, Array.Empty<byte>()));
    var frames = ParseOnce(Join(outer, tail));
    Equal(2, frames.Count, "payload magic frame count");
    BytesEqual(payload, frames[0].Payload, "payload containing magic");
    Equal(1004, frames[1].QueryId, "payload magic tail query");
}

static void ZeroPayloadFrame()
{
    var encoded = Encode(new YbDbLegacy77Frame(0, 0, 0, Array.Empty<byte>()));
    Equal(YbDbLegacy77Codec.HeaderSize, encoded.Length, "zero-payload frame length");
    var frames = ParseOnce(encoded);
    Equal(1, frames.Count, "zero-payload parser count");
    Equal(0, frames[0].Payload.Length, "zero-payload decoded length");
}

static void StrictGbkIdentities()
{
    var boundary = new YbDbLegacy77Identity
    {
        Field0 = "甲乙丙丁戊",
        Field11 = "甲乙丙丁戊己庚辛壬癸",
        RoleName = "甲乙丙丁戊己庚A",
        Field48 = "一二三四五六七B"
    };
    var encoded = EncodeIdentity(boundary);
    Equal((byte)10, encoded[YbDbLegacy77Codec.IdentityField0Offset],
        "field 0 GBK boundary");
    Equal((byte)20, encoded[YbDbLegacy77Codec.IdentityField11Offset],
        "field 11 GBK boundary");
    Equal((byte)15, encoded[YbDbLegacy77Codec.IdentityRoleNameOffset],
        "role name GBK boundary");
    Equal((byte)15, encoded[YbDbLegacy77Codec.IdentityField48Offset],
        "field 48 GBK boundary");
    True(YbDbLegacy77Codec.TryDecodeIdentity(encoded, out var decoded, out var error), error);
    Equal(boundary.Field0, decoded.Field0, "field 0 boundary roundtrip");
    Equal(boundary.Field11, decoded.Field11, "field 11 boundary roundtrip");
    Equal(boundary.RoleName, decoded.RoleName, "role boundary roundtrip");
    Equal(boundary.Field48, decoded.Field48, "field 48 boundary roundtrip");

    RejectIdentity(new YbDbLegacy77Identity { Field0 = "甲乙丙丁戊己" },
        "field 0 overrun");
    RejectIdentity(new YbDbLegacy77Identity { Field11 = new string('A', 21) },
        "field 11 overrun");
    RejectIdentity(new YbDbLegacy77Identity { RoleName = new string('A', 16) },
        "role overrun");
    RejectIdentity(new YbDbLegacy77Identity { Field48 = new string('A', 16) },
        "field 48 overrun");
    RejectIdentity(new YbDbLegacy77Identity { RoleName = "甲乙丙丁戊己庚辛" },
        "half-character truncation prevention");
    RejectIdentity(new YbDbLegacy77Identity { Field0 = "emoji:\U0001F642" },
        "unmappable GBK character");

    var invalidLength = new byte[YbDbLegacy77Codec.IdentitySize];
    invalidLength[0] = YbDbLegacy77Codec.IdentityField0Capacity + 1;
    False(YbDbLegacy77Codec.TryDecodeIdentity(invalidLength, out _, out _),
        "oversized ShortString length decoded");
    var invalidGbk = new byte[YbDbLegacy77Codec.IdentitySize];
    invalidGbk[0] = 1;
    invalidGbk[1] = 0x81;
    False(YbDbLegacy77Codec.TryDecodeIdentity(invalidGbk, out _, out _),
        "invalid GBK decoded");

    var nonzeroTail = EncodeIdentity(new YbDbLegacy77Identity
    {
        Field0 = "A",
        Field11 = "B",
        RoleName = "C",
        Field48 = "D"
    });
    nonzeroTail[2] = 0xFF;
    nonzeroTail[13] = 0xFF;
    nonzeroTail[34] = 0xFF;
    nonzeroTail[50] = 0xFF;
    True(YbDbLegacy77Codec.TryDecodeIdentity(nonzeroTail,
        out var tailDecoded, out error), error);
    Equal("A", tailDecoded.Field0, "nonzero unused field 0 tail");
    Equal("C", tailDecoded.RoleName, "nonzero unused role tail");

    False(YbDbLegacy77Codec.TryDecodeIdentity(new byte[63], out _, out _),
        "short identity decoded");
    False(YbDbLegacy77Codec.TryDecodeIdentity(new byte[65], out _, out _),
        "long identity decoded");
}

static void NativeGbkByteTruncation()
{
    var identity = new YbDbLegacy77Identity
    {
        Field0 = "123456789中",
        Field11 = "PT",
        RoleName = "角色",
        Field48 = "IP"
    };
    True(YbDbLegacy77Codec.TryEncodeNativeIdentity(identity,
        out var encoded, out var error), error);

    Equal((byte)10, encoded[YbDbLegacy77Codec.IdentityField0Offset],
        "native field 0 truncated byte length");
    BytesEqual(Convert.FromHexString("313233343536373839D6"),
        encoded.AsSpan(YbDbLegacy77Codec.IdentityField0Offset + 1, 10),
        "nine ASCII plus half CP936 character");
    Equal((byte)2, encoded[YbDbLegacy77Codec.IdentityField11Offset],
        "native field 11 length");
    True(encoded.AsSpan(YbDbLegacy77Codec.IdentityField11Offset + 3,
            YbDbLegacy77Codec.IdentityField11Capacity - 2).ToArray().All(value => value == 0),
        "native field 11 unused tail was not cleared");
    False(YbDbLegacy77Codec.TryDecodeIdentity(encoded, out _, out _),
        "half CP936 byte unexpectedly decoded as strict identity");
}

static void IdentityResponse()
{
    var identityBytes = EncodeIdentity(new YbDbLegacy77Identity
    {
        Field0 = "account",
        Field11 = "10.0.0.1",
        RoleName = "响应角色",
        Field48 = "server"
    });
    var encoded = Encode(new YbDbLegacy77Frame(5, -1, 1303, identityBytes));
    True(YbDbLegacy77Codec.TryDecode(encoded, out var frame, out var error), error);
    Equal(5, frame.QueryId, "response query id");
    Equal((ushort)1303, frame.Ident, "response ident");
    True(YbDbLegacy77Codec.TryDecodeIdentity(frame.Payload,
        out var identity, out error), error);
    Equal("响应角色", identity.RoleName, "response role at +32");
}

static void ParserResetIsolation()
{
    var stale = Encode(new YbDbLegacy77Frame(2001, 0, 1, new byte[32]));
    var fresh = Encode(new YbDbLegacy77Frame(2002, 0, 2, new byte[] { 3 }));
    var parser = new YbDbLegacy77StreamParser();
    var frames = new List<YbDbLegacy77Frame>();
    parser.Append(stale.AsSpan(0, 20), frames.Add);
    Equal(20, parser.BufferedLength, "stale partial frame buffer");
    parser.Reset();
    Equal(0, parser.BufferedLength, "reset buffer length");
    parser.Append(fresh, frames.Add);
    Equal(1, frames.Count, "post-reset frame count");
    Equal(2002, frames[0].QueryId, "post-reset query id");
}

static void LargeNoiseRemainsBounded()
{
    var noise = Enumerable.Repeat((byte)0x42,
        YbDbLegacy77StreamParser.DefaultMaximumBufferedLength * 3).ToArray();
    noise[^3] = 0x77;
    noise[^2] = 0xBB;
    noise[^1] = 0xAA;
    var parser = new YbDbLegacy77StreamParser();
    var frames = new List<YbDbLegacy77Frame>();
    parser.Append(noise, frames.Add);
    Equal(0, frames.Count, "large noise frame count");
    Equal(3, parser.BufferedLength, "large noise candidate suffix");

    var valid = Encode(new YbDbLegacy77Frame(3001, 0, 3, new byte[] { 4 }));
    parser.Append(valid.AsSpan(3), frames.Add);
    Equal(1, frames.Count, "large noise recovery count");
    Equal(3001, frames[0].QueryId, "large noise recovery query");
}

static byte[] NewHeader(int payloadLength)
{
    var data = new byte[YbDbLegacy77Codec.HeaderSize];
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4),
        YbDbLegacy77Codec.FrameMagic);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(14, 2),
        checked((ushort)payloadLength));
    return data;
}

static List<YbDbLegacy77Frame> ParseOnce(byte[] data)
{
    var frames = new List<YbDbLegacy77Frame>();
    var parser = new YbDbLegacy77StreamParser();
    parser.Append(data, frames.Add);
    Equal(0, parser.BufferedLength, "one-shot parser buffer");
    return frames;
}

static byte[] Encode(YbDbLegacy77Frame frame)
{
    True(YbDbLegacy77Codec.TryEncode(frame, out var data, out var error), error);
    return data;
}

static byte[] EncodeIdentity(YbDbLegacy77Identity identity)
{
    True(YbDbLegacy77Codec.TryEncodeIdentity(identity,
        out var data, out var error), error);
    return data;
}

static void RejectIdentity(YbDbLegacy77Identity identity, string name)
{
    False(YbDbLegacy77Codec.TryEncodeIdentity(identity, out _, out _), name);
}

static byte[] Join(params byte[][] arrays)
{
    var length = arrays.Sum(array => array.Length);
    var result = new byte[length];
    var offset = 0;
    foreach (var array in arrays)
    {
        array.CopyTo(result, offset);
        offset += array.Length;
    }
    return result;
}

static void BytesEqual(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual,
    string name)
{
    if (!expected.SequenceEqual(actual))
        throw new InvalidOperationException($"{name}: byte sequence differs");
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
