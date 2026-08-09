using System.Buffers.Binary;
using GameSvr.Services;

try
{
    VerifyFramingAndCompletion();
    VerifyMalformedFinalCompletes();
    VerifyHumanCorrections();
    VerifyHeroCorrections();
    VerifyRawOrderingAndReset();
    Console.WriteLine(
        "PASS NativeType2MagicSnapshotCheck framing=exact-72 " +
        "streams=101/102-independent completion=param2-equals-1 " +
        "records=deep-copy+ordered database-job=pre-correction " +
        "corrections=native-human+hero");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        $"NativeType2MagicSnapshotCheck FAIL: {exception}");
    return 1;
}

static void VerifyFramingAndCompletion()
{
    var state = new NativeType2MagicSnapshotState();
    Equal(NativeType2MagicSnapshotResult.Ignored,
        state.Consume(new byte[11]), "truncated packet");

    foreach (var command in new ushort[] { 103, 104, 105, 108, 200 })
    {
        Equal(NativeType2MagicSnapshotResult.Ignored,
            state.Consume(Packet(command, 1, 7)),
            $"unsupported command {command}");
    }
    Equal(0, state.CompletionFlags, "unsupported completion flags");

    var source = Packet(NativeType2MagicSnapshotState.HumanMagicCommand,
        0, NativeType2MagicSnapshotState.PacketSize);
    SetRecordUInt16(source, 0x10, 1);
    SetRecordByte(source, 0x1A, 73);
    Equal(NativeType2MagicSnapshotResult.RecordAppended,
        state.Consume(source), "human non-final result");
    source[NativeType2MagicSnapshotState.HeaderSize + 0x1A] = 1;
    Equal((byte)73, state.HumanRecords[0].CopyRecord()[0x1A],
        "accepted record is not a deep copy");
    Equal((byte)73, state.HumanRecords[0].DatabaseJob,
        "human DatabaseJob captured before source mutation");

    var humanFinal = MagicPacket(
        NativeType2MagicSnapshotState.HumanMagicCommand, 2, 1, 81);
    Equal(NativeType2MagicSnapshotResult.RecordAppendedAndCompleted,
        state.Consume(humanFinal), "human final result");
    Assert(state.HumanCompleted && !state.HeroCompleted,
        "human completion contaminated hero stream");
    Equal(NativeType2MagicSnapshotState.HumanCompleteFlag,
        state.CompletionFlags, "human completion bit");

    Equal(NativeType2MagicSnapshotResult.Ignored,
        state.Consume(MagicPacket(
            NativeType2MagicSnapshotState.HumanMagicCommand, 4, 0, 82)),
        "late human packet");
    Equal(2, state.HumanRecords.Count, "late human record count");

    Equal(NativeType2MagicSnapshotResult.RecordAppended,
        state.Consume(MagicPacket(
            NativeType2MagicSnapshotState.HeroMagicCommand, 4, 0, 83)),
        "hero remains independent");
    Equal(NativeType2MagicSnapshotResult.Ignored,
        state.Consume(Packet(
            NativeType2MagicSnapshotState.HeroMagicCommand, 2,
            NativeType2MagicSnapshotState.HeaderSize)),
        "completion marker must equal one");
    Assert(!state.HeroCompleted, "non-one marker completed hero stream");
}

static void VerifyMalformedFinalCompletes()
{
    foreach (var length in new[]
             {
                 NativeType2MagicSnapshotState.HeaderSize,
                 NativeType2MagicSnapshotState.PacketSize - 1,
                 NativeType2MagicSnapshotState.PacketSize + 1
             })
    {
        var state = new NativeType2MagicSnapshotState();
        Equal(NativeType2MagicSnapshotResult.StreamCompleted,
            state.Consume(Packet(
                NativeType2MagicSnapshotState.HumanMagicCommand, 1, length)),
            $"malformed final length {length}");
        Assert(state.HumanCompleted, $"malformed final {length} completion");
        Equal(0, state.HumanRecords.Count,
            $"malformed final {length} appended a record");
        Equal(NativeType2MagicSnapshotResult.Ignored,
            state.Consume(MagicPacket(
                NativeType2MagicSnapshotState.HumanMagicCommand,
                3, 0, 9)), $"malformed final {length} did not seal stream");
    }
}

static void VerifyHumanCorrections()
{
    var cases = new (ushort MagicId, byte Expected)[]
    {
        (62, 100), (114, 100),
        (60, 255), (61, 255),
        (128, 12),
        (116, 15), (117, 15), (118, 15),
        (125, 9), (126, 9), (127, 9),
        (234, 9), (235, 9), (236, 9),
        (115, 7),
        (3, 4), (6, 4), (11, 4), (12, 4),
        (25, 4), (31, 4), (48, 4), (59, 4),
        (160, 3), (161, 3), (162, 3),
        (291, 3), (273, 7), (286, 85),
        (287, 3), (288, 3), (289, 3), (290, 3),
        (314, 3), (315, 3), (316, 3), (317, 3)
    };
    VerifyCorrections(
        NativeType2MagicSnapshotState.HumanMagicCommand, cases, false);

    var state = new NativeType2MagicSnapshotState();
    state.Consume(MagicPacket(
        NativeType2MagicSnapshotState.HumanMagicCommand, 0, 0, 77));
    Equal((byte)77, state.HumanRecords[0].CopyRecord()[0x1A],
        "unlisted human training byte");
    Equal((byte)77, state.HumanRecords[0].DatabaseJob,
        "unlisted human DatabaseJob");
}

static void VerifyHeroCorrections()
{
    var cases = new (ushort MagicId, byte Expected)[]
    {
        (3, 4), (6, 4), (11, 4), (12, 4), (13, 4),
        (25, 4), (26, 4), (31, 4), (35, 4), (48, 4), (59, 4),
        (129, 9), (130, 9), (131, 9),
        (50, 10), (51, 10), (52, 10),
        (53, 10), (54, 10), (55, 10),
        (60, 255), (61, 255),
        (62, 100), (112, 100), (114, 100),
        (69, 99), (115, 7), (210, 5),
        (160, 3), (161, 3), (162, 3), (291, 3),
        (164, 9), (165, 9), (166, 9), (273, 7), (286, 85)
    };
    VerifyCorrections(
        NativeType2MagicSnapshotState.HeroMagicCommand, cases, true);

    var state = new NativeType2MagicSnapshotState();
    state.Consume(MagicPacket(
        NativeType2MagicSnapshotState.HeroMagicCommand, 0, 0, 77));
    var raw = state.HeroRecords[0].CopyRecord();
    Equal((byte)77, raw[0x1A], "unlisted hero training byte");
    Equal((byte)77, state.HeroRecords[0].DatabaseJob,
        "unlisted hero DatabaseJob");
    Equal(byte.MaxValue, raw[0x1F], "hero NeedLv5 override");
    Equal(-1, BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(0x2C, 4)),
        "hero LvTrain4 override");
}

static void VerifyRawOrderingAndReset()
{
    var state = new NativeType2MagicSnapshotState();
    foreach (var id in new ushort[] { 0, 700, 700, ushort.MaxValue })
    {
        var packet = MagicPacket(
            NativeType2MagicSnapshotState.HumanMagicCommand, id, 0, 33);
        SetRecordByte(packet, 0x00, unchecked((byte)id));
        state.Consume(packet);
    }
    EqualSequence(new ushort[] { 0, 700, 700, ushort.MaxValue },
        state.HumanRecords.Select(record => record.MagicId).ToArray(),
        "raw order and duplicates");
    Equal((byte)0, state.HumanRecords[0].CopyRecord()[0x00],
        "raw record leading byte");
    Equal((byte)188, state.HumanRecords[1].CopyRecord()[0x00],
        "raw record byte preservation");
    EqualSequence(new byte[] { 33, 33, 33, 33 },
        state.HumanRecords.Select(record => record.DatabaseJob).ToArray(),
        "raw DatabaseJob order and duplicates");

    state.Consume(MagicPacket(
        NativeType2MagicSnapshotState.HeroMagicCommand, 700, 1, 33));
    state.Reset();
    Equal(0, state.HumanRecords.Count, "reset human records");
    Equal(0, state.HeroRecords.Count, "reset hero records");
    Equal((byte)0, state.CompletionFlags, "reset completion flags");
    Assert(!state.HumanCompleted && !state.HeroCompleted,
        "reset completion properties");
}

static void VerifyCorrections(ushort command,
    IReadOnlyList<(ushort MagicId, byte Expected)> cases, bool hero)
{
    foreach (var item in cases)
    {
        var state = new NativeType2MagicSnapshotState();
        state.Consume(MagicPacket(command, item.MagicId, 0, 37));
        var record = (hero ? state.HeroRecords : state.HumanRecords)[0];
        var raw = record.CopyRecord();
        Equal(item.MagicId, record.MagicId,
            $"command {command} magic id {item.MagicId}");
        Equal((byte)37, record.DatabaseJob,
            $"command {command} raw DatabaseJob {item.MagicId}");
        Equal(item.Expected, raw[0x1A],
            $"command {command} correction {item.MagicId}");
        if (!hero) continue;
        Equal(byte.MaxValue, raw[0x1F],
            $"hero NeedLv5 {item.MagicId}");
        Equal(-1, BinaryPrimitives.ReadInt32LittleEndian(
                raw.AsSpan(0x2C, 4)),
            $"hero LvTrain4 {item.MagicId}");
    }
}

static byte[] MagicPacket(ushort command, ushort magicId,
    int completionMarker, byte trainingByte)
{
    var packet = Packet(command, completionMarker,
        NativeType2MagicSnapshotState.PacketSize);
    SetRecordUInt16(packet, 0x10, magicId);
    SetRecordByte(packet, 0x1A, trainingByte);
    SetRecordByte(packet, 0x1F, 22);
    BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(
        NativeType2MagicSnapshotState.HeaderSize + 0x2C, 4), 123456789);
    return packet;
}

static byte[] Packet(ushort command, int completionMarker, int length)
{
    var packet = new byte[length];
    if (length >= 2)
        BinaryPrimitives.WriteUInt16LittleEndian(packet, command);
    if (length >= NativeType2MagicSnapshotState.HeaderSize)
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8, 4),
            completionMarker);
    return packet;
}

static void SetRecordUInt16(byte[] packet, int offset, ushort value) =>
    BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(
        NativeType2MagicSnapshotState.HeaderSize + offset, 2), value);

static void SetRecordByte(byte[] packet, int offset, byte value) =>
    packet[NativeType2MagicSnapshotState.HeaderSize + offset] = value;

static void Assert(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
}

static void Equal<T>(T expected, T actual, string description)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{description}: expected {expected}, actual {actual}");
}

static void EqualSequence<T>(IReadOnlyList<T> expected,
    IReadOnlyList<T> actual, string description)
{
    if (!expected.SequenceEqual(actual))
        throw new InvalidOperationException(
            $"{description}: expected [{string.Join(",", expected)}], " +
            $"actual [{string.Join(",", actual)}]");
}
