using System.Buffers.Binary;
using System.Text;
using SystemModule;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var gbk = Encoding.GetEncoding(936,
    EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

CheckRequest();
CheckResponse();
CheckInitialRecord();
CheckDeleteFrames();
CheckInvalidFrames();

Console.WriteLine(
    "PASS hero-create/delete native request=162/163 response=53/59 soft-delete=index-only initial=49D4");
return;

void CheckRequest()
{
    var source = new NativeHeroCreateRequest
    {
        HeroType = 2,
        Code = 6,
        Account = "account01",
        MasterName = "主人甲",
        HeroName = "英雄乙"
    };
    Assert(NativeHeroDbFrameCodec.TryEncodeCreateRequest(
        source, out var frame, out var error), error);
    Equal(0x54, frame.Length, "create request frame size");
    Equal(NativeHeroDbFrameCodec.FrameMagic,
        BinaryPrimitives.ReadUInt32LittleEndian(frame), "create request magic");
    Equal((ushort)0x162,
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(12, 2)),
        "create request opcode");
    Equal(source.HeroType,
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(14, 2)),
        "create request HeroType");
    Equal(source.Code,
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(16, 4)),
        "create request code");
    Equal(source.Account, ReadShortString(frame, 12 + 16, 20),
        "create request account wire field");
    Equal(source.MasterName, ReadShortString(frame, 12 + 37, 15),
        "create request +37 must be MasterName");
    Equal(source.HeroName, ReadShortString(frame, 12 + 53, 15),
        "create request +53 must be HeroName");

    Assert(NativeHeroDbFrameCodec.TryDecodeCreateRequest(
        frame, out var decoded, out error), error);
    Equal(source.HeroType, decoded.HeroType, "create request type round trip");
    Equal(source.Code, decoded.Code, "create request code round trip");
    Equal(string.Empty, decoded.Account, "create request ignored account decode");
    Equal(source.MasterName, decoded.MasterName, "create request master round trip");
    Equal(source.HeroName, decoded.HeroName, "create request hero round trip");

    // Delphi constructs the 0x48-byte message on its stack and leaves unnamed bytes unspecified.
    var stackGarbage = (byte[])frame.Clone();
    stackGarbage[12 + 8] = 0xA5;
    stackGarbage[12 + 15] = 0x5A;
    stackGarbage[12 + 69] = 0x11;
    stackGarbage[12 + 71] = 0x33;
    Assert(NativeHeroDbFrameCodec.TryDecodeCreateRequest(
        stackGarbage, out decoded, out error), error);
    Equal(source.MasterName, decoded.MasterName,
        "create request with stack garbage changed master");
}

void CheckResponse()
{
    foreach (var result in new[] { -6, -5, -4, -3, -2, -1, 1, 2, 3, 4, 5, 6 })
    {
        var source = new NativeHeroCreateResponse
        {
            HeroType = 1,
            Result = result,
            MasterName = "主人甲",
            HeroName = "英雄乙"
        };
        Assert(NativeHeroDbFrameCodec.TryEncodeCreateResponse(
            source, out var frame, out var error), error);
        Equal(0x54, frame.Length, "create response frame size");
        Equal((ushort)0x53,
            BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(12, 2)),
            "create response opcode");
        Equal(source.HeroType,
            BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(14, 2)),
            "create response HeroType");
        Equal(result, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(16, 4)),
            "create response result");
        Equal(source.MasterName, ReadShortString(frame, 12 + 37, 15),
            "create response +37 master");
        Equal(source.HeroName, ReadShortString(frame, 12 + 53, 15),
            "create response +53 hero");
        Assert(NativeHeroDbFrameCodec.TryDecodeCreateResponse(
            frame, out var decoded, out error), error);
        Equal(result, decoded.Result, "create response result round trip");
        Equal(source.MasterName, decoded.MasterName,
            "create response master round trip");
        Equal(source.HeroName, decoded.HeroName,
            "create response hero round trip");

        frame[12 + 8] = 0x7A;
        frame[12 + 36] = 0x4C;
        frame[12 + 71] = 0x2D;
        Assert(NativeHeroDbFrameCodec.TryDecodeCreateResponse(
            frame, out _, out error), error);
    }
}

void CheckInitialRecord()
{
    for (var code = 1; code <= 6; code++)
    {
        var source = new NativeHeroCreateRequest
        {
            HeroType = code <= 3 ? (ushort)1 : (ushort)2,
            Code = code,
            Account = "account01",
            MasterName = "主人甲",
            HeroName = "英雄乙"
        };
        Assert(NativeHeroDbFrameCodec.TryCreateInitialRecord(
            source, out var record, out var error), error);
        var raw = record.ToArray();
        Equal(NativeHeroDbFrameCodec.HeroRecordSize, raw.Length,
            "initial fixed record size");
        Equal((ushort)NativeHeroDbFrameCodec.HeroRecordSize,
            BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(4, 2)),
            "initial fixed record embedded length");
        Equal(source.MasterName,
            ReadShortString(raw, NativeHeroDbFrameCodec.MasterNameOffset, 15),
            "initial fixed record master");
        Equal(source.HeroName,
            ReadShortString(raw, NativeHeroDbFrameCodec.HeroNameOffset, 15),
            "initial fixed record hero");
        Equal((byte)1, raw[NativeHeroDbFrameCodec.RaceOffset],
            "initial fixed record race");
        Equal((byte)((code - 1) / 3), raw[NativeHeroDbFrameCodec.SexOffset],
            "initial fixed record sex");
        Equal((byte)((code - 1) % 3), raw[NativeHeroDbFrameCodec.JobOffset],
            "initial fixed record job");
        Equal((byte)source.HeroType, raw[NativeHeroDbFrameCodec.HeroTypeOffset],
            "initial fixed record HeroType");
        Equal((ushort)0,
            BinaryPrimitives.ReadUInt16LittleEndian(
                raw.AsSpan(NativeHeroDbFrameCodec.LevelOffset, 2)),
            "initial fixed record level must remain zero");
    }
}

void CheckDeleteFrames()
{
    var request = new NativeHeroDeleteRequest
    {
        Account = "account01",
        MasterName = "主人甲",
        HeroName = string.Empty
    };
    Assert(NativeHeroDbFrameCodec.TryEncodeDeleteRequest(
        request, out var frame, out var error), error);
    Equal(0x54, frame.Length, "delete request frame size");
    Equal((ushort)0x163,
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(12, 2)),
        "delete request opcode");
    Equal((ushort)0,
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(14, 2)),
        "delete request +2");
    Equal(0, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(16, 4)),
        "delete request +4");
    Assert(NativeHeroDbFrameCodec.TryDecodeDeleteRequest(
        frame, out var decodedRequest, out error), error);
    Equal(request.Account, decodedRequest.Account, "delete request account");
    Equal(request.MasterName, decodedRequest.MasterName, "delete request master");
    Equal(string.Empty, decodedRequest.HeroName, "delete request empty hero");

    foreach (var result in new[] { 0, 1, 2, 3 })
    {
        var response = new NativeHeroDeleteResponse
        {
            Result = result,
            Account = request.Account,
            MasterName = request.MasterName,
            HeroName = request.HeroName
        };
        Assert(NativeHeroDbFrameCodec.TryEncodeDeleteResponse(
            response, out frame, out error), error);
        Equal((ushort)0x59,
            BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(12, 2)),
            "delete response opcode");
        Equal(result, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(16, 4)),
            "delete response result");
        frame[14] = 0xA5;
        frame[15] = 0x5A;
        Assert(NativeHeroDbFrameCodec.TryDecodeDeleteResponse(
            frame, out var decodedResponse, out error), error);
        Equal(result, decodedResponse.Result, "delete response result round trip");
        Equal(request.Account, decodedResponse.Account, "delete response account");
        Equal(request.MasterName, decodedResponse.MasterName, "delete response master");
    }

    frame = (byte[])frame.Clone();
    BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(12, 2), 0x53);
    Assert(!NativeHeroDbFrameCodec.TryDecodeDeleteResponse(
        frame, out _, out _), "create response accepted as delete response");
    Assert(!NativeHeroDbFrameCodec.TryEncodeDeleteResponse(
        new NativeHeroDeleteResponse { Result = 4 }, out _, out _),
        "invalid delete result accepted");
}

void CheckInvalidFrames()
{
    foreach (var heroType in new ushort[] { 0, 3 })
    {
        Assert(!NativeHeroDbFrameCodec.TryEncodeCreateRequest(
            new NativeHeroCreateRequest
            {
                HeroType = heroType, Code = 1, MasterName = "主人甲", HeroName = "英雄乙"
            }, out _, out _), $"invalid HeroType {heroType} accepted");
    }
    foreach (var code in new[] { 0, 7 })
    {
        Assert(!NativeHeroDbFrameCodec.TryEncodeCreateRequest(
            new NativeHeroCreateRequest
            {
                HeroType = 1, Code = code, MasterName = "主人甲", HeroName = "英雄乙"
            }, out _, out _), $"invalid create code {code} accepted");
    }
    foreach (var result in new[] { -7, 0, 7 })
    {
        Assert(!NativeHeroDbFrameCodec.TryEncodeCreateResponse(
            new NativeHeroCreateResponse
            {
                HeroType = 1, Result = result, MasterName = "主人甲", HeroName = "英雄乙"
            }, out _, out _), $"invalid create result {result} accepted");
    }

    Assert(NativeHeroDbFrameCodec.TryEncodeCreateRequest(
        new NativeHeroCreateRequest
        {
            HeroType = 1, Code = 1, Account = "account01",
            MasterName = "主人甲", HeroName = "英雄乙"
        }, out var valid, out var error), error);
    var wrongOpcode = (byte[])valid.Clone();
    BinaryPrimitives.WriteUInt16LittleEndian(wrongOpcode.AsSpan(12, 2), 0x161);
    Assert(!NativeHeroDbFrameCodec.TryDecodeCreateRequest(
        wrongOpcode, out _, out _), "save opcode accepted as create request");
    Assert(!NativeHeroDbFrameCodec.TryDecodeCreateRequest(
        valid[..^1], out _, out _), "truncated create request accepted");

    var oversized = new string('A', 16);
    Assert(!NativeHeroDbFrameCodec.TryEncodeCreateRequest(
        new NativeHeroCreateRequest
        {
            HeroType = 1, Code = 1, MasterName = oversized, HeroName = "英雄乙"
        }, out _, out _), "oversized create master name accepted");
}

string ReadShortString(byte[] data, int offset, int maximumLength)
{
    var length = data[offset];
    Assert(length <= maximumLength, "test short string length overflow");
    return gbk.GetString(data, offset + 1, length);
}

static void Equal<T>(T expected, T actual, string message) where T : IEquatable<T>
{
    if (!expected.Equals(actual))
        throw new InvalidOperationException($"{message}: expected={expected} actual={actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
