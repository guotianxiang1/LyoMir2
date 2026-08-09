using System.Buffers.Binary;
using SystemModule;

const uint ExpectedMagic = 0x33AABB77;
const int ExpectedOuterHeaderSize = 0x0C;
const ushort ExpectedType1 = 1;
const ushort ExpectedType2 = 2;
const int ExpectedType2PayloadSize = 0x0C;
const int ExpectedMessagePayloadSize = 0x48;
const int ExpectedHeroRecordSize = 0x49D4;
const int ExpectedHeroDataPayloadSize = 0x48 + 0x49D4;

// Captured from the original GS1 (ServerIndex=0). Word2 is uninitialized stack
// noise in the native sender; its observed value is pinned but is not semantic.
CheckObservedType2(
    "registration",
    "77BBAA33020000000C0000003D00BDC50000000001000000",
    expectedCommand: 0x003D,
    expectedObservedWord2: 0xC5BD,
    expectedParam1: 0,
    expectedParam2: 1);

CheckObservedType2(
    "heartbeat",
    "77BBAA33020000000C0000003C0075790000000009000000",
    expectedCommand: 0x003C,
    expectedObservedWord2: 0x7975,
    expectedParam1: 0,
    expectedParam2: 9);

var fixedRecordBytes = new byte[ExpectedHeroRecordSize];
BinaryPrimitives.WriteUInt16LittleEndian(
    fixedRecordBytes.AsSpan(4, 2), (ushort)ExpectedHeroRecordSize);
WriteShortString(fixedRecordBytes, 0x08, 15, "master");
WriteShortString(fixedRecordBytes, 0x18, 15, "hero");
Assert(NativeHeroDbFrameCodec.TryCreateRecord(
    fixedRecordBytes, out var fixedRecord, out var recordError), recordError);
var emptyDynamicData = new NativeHeroDynamicData(
    Array.Empty<NativeHeroDynamicSection>());

Assert(NativeHeroDbFrameCodec.TryEncodeLoadRequest(
    new NativeHeroLoadRequest
    {
        HeroKind = 1,
        HeroSlot = 0,
        Account = "account",
        MasterName = "master"
    }, out var loadRequest, out var error), error);
CheckFrame("hero-load-request", loadRequest, ExpectedType1,
    expectedCommand: 0x0160,
    expectedPayloadLength: ExpectedMessagePayloadSize,
    expectedTotalLength: 0x54);

Assert(NativeHeroDbFrameCodec.TryEncodeSaveRequest(
    new NativeHeroSaveRequest
    {
        SaveMode = 0,
        Param1 = 0,
        Param2 = 0,
        Record = fixedRecord,
        DynamicData = emptyDynamicData
    }, out var saveRequest, out error), error);
CheckFrame("hero-save-request-empty-dyn", saveRequest, ExpectedType1,
    expectedCommand: 0x0161,
    expectedPayloadLength: ExpectedHeroDataPayloadSize,
    expectedTotalLength: 0x4A28);

Assert(NativeHeroDbFrameCodec.TryEncodeCreateRequest(
    new NativeHeroCreateRequest
    {
        HeroType = 1,
        Code = 1,
        Account = "account",
        MasterName = "master",
        HeroName = "hero"
    }, out var createRequest, out error), error);
CheckFrame("hero-create-request", createRequest, ExpectedType1,
    expectedCommand: 0x0162,
    expectedPayloadLength: ExpectedMessagePayloadSize,
    expectedTotalLength: 0x54);

Assert(NativeHeroDbFrameCodec.TryEncodeDeleteRequest(
    new NativeHeroDeleteRequest
    {
        Account = "account",
        MasterName = "master",
        HeroName = "hero"
    }, out var deleteRequest, out error), error);
CheckFrame("hero-delete-request", deleteRequest, ExpectedType1,
    expectedCommand: 0x0163,
    expectedPayloadLength: ExpectedMessagePayloadSize,
    expectedTotalLength: 0x54);

Assert(NativeHeroDbFrameCodec.TryEncodeRenameRequest(
    new NativeHeroRenameRequest
    {
        SelectionMode = 1,
        Code = 123,
        OldHeroName = "oldhero",
        MasterName = "master",
        NewHeroName = "newhero"
    }, out var renameRequest, out error), error);
CheckFrame("hero-rename-request", renameRequest, ExpectedType1,
    expectedCommand: 0x0164,
    expectedPayloadLength: ExpectedMessagePayloadSize,
    expectedTotalLength: 0x54);

Assert(NativeHeroDbFrameCodec.TryEncodeLoadResponse(
    new NativeHeroLoadResponse
    {
        Status = 0,
        MasterName = "master"
    }, out var failedLoadResponse, out error), error);
CheckFrame("hero-load-response-failure", failedLoadResponse, ExpectedType1,
    expectedCommand: 0x0051,
    expectedPayloadLength: ExpectedMessagePayloadSize,
    expectedTotalLength: 0x54);

Assert(NativeHeroDbFrameCodec.TryEncodeLoadResponse(
    new NativeHeroLoadResponse
    {
        Status = 1,
        Record = fixedRecord,
        DynamicData = emptyDynamicData
    }, out var successfulLoadResponse, out error), error);
CheckFrame("hero-load-response-success-empty-dyn", successfulLoadResponse,
    ExpectedType1,
    expectedCommand: 0x0051,
    expectedPayloadLength: ExpectedHeroDataPayloadSize,
    expectedTotalLength: 0x4A28);

Assert(NativeHeroDbFrameCodec.TryEncodeCreateResponse(
    new NativeHeroCreateResponse
    {
        HeroType = 1,
        Result = 1,
        MasterName = "master",
        HeroName = "hero"
    }, out var createResponse, out error), error);
CheckFrame("hero-create-response", createResponse, ExpectedType1,
    expectedCommand: 0x0053,
    expectedPayloadLength: ExpectedMessagePayloadSize,
    expectedTotalLength: 0x54);

Assert(NativeHeroDbFrameCodec.TryEncodeDeleteResponse(
    new NativeHeroDeleteResponse
    {
        Result = 1,
        Account = "account",
        MasterName = "master",
        HeroName = "hero"
    }, out var deleteResponse, out error), error);
CheckFrame("hero-delete-response", deleteResponse, ExpectedType1,
    expectedCommand: 0x0059,
    expectedPayloadLength: ExpectedMessagePayloadSize,
    expectedTotalLength: 0x54);

Assert(NativeHeroDbFrameCodec.TryEncodeRenameResponse(
    new NativeHeroRenameResponse
    {
        Result = 1,
        Code = 123,
        MasterName = "master",
        NewHeroName = "newhero"
    }, out var renameResponse, out error), error);
CheckFrame("hero-rename-response", renameResponse, ExpectedType1,
    expectedCommand: 0x005A,
    expectedPayloadLength: ExpectedMessagePayloadSize,
    expectedTotalLength: 0x54);

Console.WriteLine(
    "PASS m2-native-db-golden outer=0x0C type2=003D/003C " +
    "hero=0160..0164/0051/0053/0059/005A " +
    "registration-param2=server-index+1 word2=observed-stack-noise");

static void CheckObservedType2(string name, string capturedHex,
    ushort expectedCommand, ushort expectedObservedWord2,
    int expectedParam1, int expectedParam2)
{
    var frame = Convert.FromHexString(capturedHex);
    CheckFrame(name, frame, ExpectedType2, expectedCommand,
        ExpectedType2PayloadSize,
        ExpectedOuterHeaderSize + ExpectedType2PayloadSize);

    Equal(expectedObservedWord2,
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(14, 2)),
        $"{name} observed Word2");
    Equal(expectedParam1,
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(16, 4)),
        $"{name} Param1");
    Equal(expectedParam2,
        BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(20, 4)),
        $"{name} Param2");
}

static void CheckFrame(string name, byte[] frame, ushort expectedType,
    ushort expectedCommand, int expectedPayloadLength, int expectedTotalLength)
{
    Assert(frame != null, $"{name}: frame is null");
    Equal(expectedTotalLength, frame.Length, $"{name} total length");
    Assert(frame.Length >= ExpectedOuterHeaderSize + 2,
        $"{name}: frame is shorter than outer header plus command");

    Equal(ExpectedMagic,
        BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(0, 4)),
        $"{name} magic");
    Equal(expectedType,
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2)),
        $"{name} outer type");
    Equal((ushort)0,
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(6, 2)),
        $"{name} outer reserved");

    var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(
        frame.AsSpan(8, 4));
    Equal(expectedPayloadLength, payloadLength, $"{name} payload length");
    Equal(ExpectedOuterHeaderSize + payloadLength, frame.Length,
        $"{name} outer length equation");
    Equal(expectedCommand,
        BinaryPrimitives.ReadUInt16LittleEndian(
            frame.AsSpan(ExpectedOuterHeaderSize, 2)),
        $"{name} command");
}

static void WriteShortString(byte[] destination, int offset,
    int maximumLength, string value)
{
    var bytes = System.Text.Encoding.ASCII.GetBytes(value);
    Assert(bytes.Length <= maximumLength,
        $"fixture short string exceeds {maximumLength} bytes");
    destination[offset] = (byte)bytes.Length;
    bytes.CopyTo(destination, offset + 1);
}

static void Equal<T>(T expected, T actual, string label)
    where T : IEquatable<T>
{
    if (!expected.Equals(actual))
        throw new InvalidOperationException(
            $"{label}: expected {expected}, actual {actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
