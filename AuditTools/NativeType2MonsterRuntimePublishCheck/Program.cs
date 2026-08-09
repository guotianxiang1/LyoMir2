using System.Buffers.Binary;
using System.Text;
using GameSvr.Services;

var catalog = new NativeType2MonsterRuntimeCatalog();
var incomplete = new NativeType2MonsterSnapshotState();
incomplete.Consume(Packet(Body("Alpha", 1), completed: false));
Throws<InvalidDataException>(() => catalog.Publish(incomplete),
    "incomplete snapshot rejected");
Check(!catalog.Ready, "failed publication leaves empty catalog");

var state = new NativeType2MonsterSnapshotState();
state.Consume(Packet(Body("Alpha", 1), completed: false));
state.Consume(Packet(Body("Beta", 2), completed: false));
state.Consume(Packet(Body("Alpha", 3), completed: false));
state.Consume(Packet(Body("alpha", 4), completed: true));
catalog.Publish(state);

Check(catalog.Ready, "terminal snapshot published");
Equal(3, catalog.Definitions.Count, "exact duplicate updates in place");
Equal("Alpha", catalog.Definitions[0].Name, "first arrival remains first");
Equal(3, catalog.Definitions[0].Experience, "duplicate replaced fields");
Equal("Beta", catalog.Definitions[1].Name, "second arrival order");
Equal("alpha", catalog.Definitions[2].Name, "case variant remains distinct");
Check(ReferenceEquals(catalog.FindByName("Alpha"), catalog.Definitions[0]),
    "exact string lookup");
Check(ReferenceEquals(catalog.FindByName("alpha"), catalog.Definitions[2]),
    "case variant lookup");
Check(catalog.FindByName("ALPHA") == null, "lookup is case sensitive");
Check(catalog.FindByNameBytes(Encoding.ASCII.GetBytes("Alpha")) ==
      catalog.Definitions[0], "raw byte lookup");

var definition = catalog.Definitions[0];
Equal(0x12345678, definition.HitPoints, "32-bit HP preserved");
Equal(unchecked((int)0x89ABCDEF), definition.ManaPoints,
    "32-bit MP preserved");
Equal(17, definition.Race, "race byte");
Equal(18, definition.RaceImage, "race image byte");
Equal(19, definition.LifeAttribute, "life attribute byte");
Equal(20, definition.CoolEye, "cool-eye byte");
Equal(0x2122, definition.Appearance, "appearance word");
Equal(0x2324, definition.Level, "level word");
Equal(0x2526, definition.ArmorClass, "AC word");
Equal(0x2728, definition.MagicArmorClass, "MAC word");
Equal(0x292A, definition.DamageClass, "DC word");
Equal(0x2B2C, definition.MaximumDamageClass, "max DC word");
Equal(0x2D2E, definition.MagicClass, "MC word");
Equal(0x2F30, definition.SoulClass, "SC word");
Equal(0x3132, definition.Speed, "speed word");
Equal(0x3334, definition.Hit, "hit word");
Equal(99, definition.WalkSpeed, "walk speed has no 200ms floor");
Equal(0xABCD, definition.WalkStepWire, "walk-step wire word preserved");
Equal(0xCD, definition.WalkStep, "actor consumes walk-step low byte");
Equal(0x3738, definition.WalkWait, "walk wait word");
Equal(77, definition.AttackSpeed, "attack speed has no 200ms floor");
Equal(unchecked((int)0xA1A2A3A4), definition.ForceValue,
    "force value remap");
Equal(unchecked((int)0xB1B2B3B4), definition.SuperForceExperience,
    "super-force exp remap");
Equal(0x0000C1C2, definition.SuperForceLevel,
    "super-force level zero-extended source word");
Equal(unchecked((int)0xD1D2D3D4), definition.JobFastness,
    "job fastness remap");
Equal(0, definition.RuntimeReset, "runtime reset field cleared");
Equal(0, definition.ScriptMarker, "ignored speciality does not become script marker");

var monster = definition.CreateTMonInfo();
Equal(definition.HitPoints, monster.wHP, "TMonInfo HP is not truncated");
Equal(definition.ManaPoints, monster.wMP, "TMonInfo MP is not truncated");
Equal(99, monster.wWalkSpeed, "TMonInfo walk speed is not corrected");
Equal(77, monster.wAttackSpeed, "TMonInfo attack speed is not corrected");
Equal(0xCD, monster.wWalkStep, "TMonInfo actor walk-step width");

var nativeCopy = definition.CopyNativeRecord();
nativeCopy[0x14] = 0;
Equal(17, definition.Race, "definition is immutable");
state.Reset();
Equal(3, catalog.Definitions.Count, "snapshot reset cannot mutate publication");

var invalid = new NativeType2MonsterSnapshotState();
var invalidBody = Body("Bad", 9);
invalidBody[0x04] = 16;
invalid.Consume(Packet(invalidBody, completed: true));
Throws<InvalidDataException>(() => catalog.Publish(invalid),
    "invalid terminal snapshot rejected");
Equal(3, catalog.Definitions.Count,
    "failed replacement leaves prior publication intact");

var empty = new NativeType2MonsterSnapshotState();
empty.Consume(Packet(Array.Empty<byte>(), completed: true));
Throws<InvalidDataException>(() => catalog.Publish(empty),
    "empty synthetic terminal rejected");

Console.WriteLine("PASS NativeType2MonsterRuntimePublishCheck " +
                  "command=0067 order=exact-name hpmp=int32 " +
                  "corrections=none publication=terminal-valid-atomic");

static byte[] Body(string name, int experience)
{
    var body = new byte[NativeType2MonsterSnapshotState.NativeRecordSize];
    var nameBytes = Encoding.ASCII.GetBytes(name);
    body[0x04] = (byte)nameBytes.Length;
    nameBytes.CopyTo(body, 0x05);
    body[0x14] = 17;
    body[0x15] = 18;
    body[0x16] = 19;
    body[0x17] = 20;
    WriteUInt16(body, 0x18, 0x2122);
    WriteUInt16(body, 0x1A, 0x2324);
    WriteInt32(body, 0x1C, experience);
    WriteInt32(body, 0x20, 0x12345678);
    WriteInt32(body, 0x24, unchecked((int)0x89ABCDEF));
    WriteUInt16(body, 0x28, 0x2526);
    WriteUInt16(body, 0x2A, 0x2728);
    WriteUInt16(body, 0x2C, 0x292A);
    WriteUInt16(body, 0x2E, 0x2B2C);
    WriteUInt16(body, 0x30, 0x2D2E);
    WriteUInt16(body, 0x32, 0x2F30);
    WriteUInt16(body, 0x34, 0x3132);
    WriteUInt16(body, 0x36, 0x3334);
    WriteUInt16(body, 0x38, 99);
    WriteUInt16(body, 0x3A, 0xABCD);
    WriteUInt16(body, 0x3C, 0x3738);
    WriteUInt16(body, 0x3E, 77);
    WriteInt32(body, 0x40, unchecked((int)0x81828384));
    WriteInt32(body, 0x44, unchecked((int)0xA1A2A3A4));
    WriteInt32(body, 0x48, unchecked((int)0x91929394));
    WriteInt32(body, 0x4C, unchecked((int)0xA5A6A7A8));
    WriteInt32(body, 0x50, unchecked((int)0xB1B2B3B4));
    WriteUInt16(body, 0x54, 0xC1C2);
    WriteUInt16(body, 0x56, 0);
    WriteInt32(body, 0x58, unchecked((int)0xD1D2D3D4));
    WriteInt32(body, 0x5C, unchecked((int)0xE1E2E3E4));
    WriteInt32(body, 0x60, unchecked((int)0xF1F2F3F4));
    WriteInt32(body, 0x64, unchecked((int)0x01020304));
    return body;
}

static byte[] Packet(byte[] body, bool completed)
{
    var packet = new byte[NativeType2MonsterSnapshotState.HeaderSize + body.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(packet,
        NativeType2MonsterSnapshotState.Command);
    if (completed)
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8, 4), 1);
    body.CopyTo(packet, NativeType2MonsterSnapshotState.HeaderSize);
    return packet;
}

static void WriteUInt16(byte[] target, int offset, ushort value) =>
    BinaryPrimitives.WriteUInt16LittleEndian(target.AsSpan(offset, 2), value);

static void WriteInt32(byte[] target, int offset, int value) =>
    BinaryPrimitives.WriteInt32LittleEndian(target.AsSpan(offset, 4), value);

static void Equal<T>(T expected, T actual, string description)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{description}: expected {expected}, actual {actual}");
}

static void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
}

static void Throws<T>(Action action, string description) where T : Exception
{
    try
    {
        action();
    }
    catch (T)
    {
        return;
    }
    throw new InvalidOperationException(description);
}
