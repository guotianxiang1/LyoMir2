using System.Buffers.Binary;
using GameSvr.Services;

var state = new NativeType2MonsterSnapshotState();

Check(state.Consume(CreatePacket(new byte[0x30], completed: false))
      == NativeType2MonsterSnapshotResult.Ignored,
    "short non-terminal monster packet");
Check(!state.Completed && state.Records.Count == 0,
    "short non-terminal monster state");

var first = CreateBody("MonsterA", 100, 0x11111111, 0x22222222,
    0x33333333, 0x44444444, 0x55555555, 0x66666666);
Check(state.Consume(CreatePacket(first, completed: false))
      == NativeType2MonsterSnapshotResult.RecordCreated,
    "first monster append");
Check(state.Records.Count == 1, "first monster count");
var before = state.Records[0].CopyNativeFields();
Equal(0x11111111, ReadInt32(before, 0x20), "native 32-bit HP");
Equal(0x22222222, ReadInt32(before, 0x24), "native 32-bit MP");
Equal(0x44444444, ReadInt32(before, 0x50), "native force value");
Equal(0x55555555, ReadInt32(before, 0x5C), "native super force exp");
Equal(0x66666666, ReadInt32(before, 0x60), "native super force level dword");
Equal(0, ReadInt32(before, 0x48), "native target reset field");

var update = CreateBody("MonsterA", 200, 0x77777777, unchecked((int)0x88888888),
    unchecked((int)0x99999999), unchecked((int)0xAAAAAAAA), unchecked((int)0xBBBBBBBB),
    unchecked((int)0xCCCCCCCC));
BinaryPrimitives.WriteInt32LittleEndian(update.AsSpan(0x40, 4), 0x12345678);
BinaryPrimitives.WriteInt32LittleEndian(update.AsSpan(0x48, 4), 0x23456789);
Check(state.Consume(CreatePacket(update, completed: false))
      == NativeType2MonsterSnapshotResult.RecordUpdated,
    "same-name monster update");
Check(state.Records.Count == 1, "same-name update does not append");
var after = state.Records[0].CopyNativeFields();
Equal(200, ReadInt32(after, 0x1C), "updated exp");
Equal(0x77777777, ReadInt32(after, 0x20), "updated HP");
Equal(unchecked((int)0x88888888), ReadInt32(after, 0x24), "updated MP");
Equal(unchecked((int)0xAAAAAAAA), ReadInt32(after, 0x50), "updated force value");
Equal(unchecked((int)0xBBBBBBBB), ReadInt32(after, 0x5C), "updated super force exp");
Equal(unchecked((int)0xCCCCCCCC), ReadInt32(after, 0x60), "updated super force level");
Equal(0, ReadInt32(after, 0x40), "source ignored field remains untouched");
Equal(0, ReadInt32(after, 0x48), "source ignored field does not defeat reset");

var invalid = CreateBody("Bad", 1, 1, 2, 3, 4, 5, 6);
invalid[0x04] = 16;
Check(state.Consume(CreatePacket(invalid, completed: false))
      == NativeType2MonsterSnapshotResult.InvalidRecord,
    "invalid short string body");
Check(state.Records.Count == 1, "invalid body did not mutate records");
Check(state.HasInvalidRecord, "invalid record flag persists");

Check(state.Consume(CreatePacket(new byte[0], completed: true))
      == NativeType2MonsterSnapshotResult.StreamCompleted,
    "short terminal monster packet");
Check(state.Completed, "monster terminal state");
Check(state.Consume(CreatePacket(CreateBody("MonsterB", 1, 1, 2, 3, 4, 5, 6),
        completed: false)) == NativeType2MonsterSnapshotResult.Ignored,
    "post-terminal monster packet");
Check(state.Records.Count == 1, "post-terminal monster mutation");

state.Reset();
Check(!state.Completed && !state.HasInvalidRecord && state.Records.Count == 0,
    "monster reset");

Console.WriteLine("PASS NativeType2MonsterSnapshotCheck command=0067 min=5C " +
                  "update=by-name raw=dword-preserved terminal=param2-equals-1");

static byte[] CreatePacket(byte[] body, bool completed)
{
    var payload = new byte[NativeType2MonsterSnapshotState.HeaderSize + body.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(payload, NativeType2MonsterSnapshotState.Command);
    if (completed)
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), 1);
    body.CopyTo(payload, NativeType2MonsterSnapshotState.HeaderSize);
    return payload;
}

static byte[] CreateBody(string name, int exp, int hp, int mp, int ignored,
    int forceValue, int superForceExp, int superForceLevel)
{
    var body = new byte[NativeType2MonsterSnapshotState.MinimumBodySize];
    var nameBytes = System.Text.Encoding.ASCII.GetBytes(name);
    body[0x04] = (byte)nameBytes.Length;
    nameBytes.CopyTo(body, 0x05);
    BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(0x1C, 4), exp);
    BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(0x20, 4), hp);
    BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(0x24, 4), mp);
    BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(0x40, 4), ignored);
    BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(0x44, 4), forceValue);
    BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(0x50, 4), superForceExp);
    BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(0x54, 4), superForceLevel);
    BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(0x58, 4), 0x0D0E0F10);
    return body;
}

static int ReadInt32(byte[] value, int offset) =>
    BinaryPrimitives.ReadInt32LittleEndian(value.AsSpan(offset, 4));

static void Equal(int expected, int actual, string description)
{
    if (expected != actual)
        throw new InvalidOperationException(
            $"{description}: expected 0x{expected:X8}, actual 0x{actual:X8}");
}

static void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
}
