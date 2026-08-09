using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using DBSvr.Core;
using GameSvr;
using GameSvr.Services;
using SystemModule.Packet;

try
{
    var builtPackets = VerifyLoaderOrderAndExactPackets();
    VerifyM2IgnoredFramesAreNoOp(builtPackets);
    Console.WriteLine(
        "PASS NativeType2StaticTailNoOpCheck " +
        "73=ignored 75=ignored 76=ignored 6D=ignored " +
        "static-and-generic-paths");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        $"NativeType2StaticTailNoOpCheck FAIL: {exception}");
    return 1;
}

static byte[][] VerifyLoaderOrderAndExactPackets()
{
    var tablesField = typeof(MySqlNativeType2StaticLoader).GetField("Tables",
        BindingFlags.Static | BindingFlags.NonPublic);
    var tables = tablesField?.GetValue(null) as Array
        ?? throw new InvalidOperationException(
            "missing native static loader table array");
    var commands = new List<ushort>();
    foreach (var table in tables)
    {
        var command = table.GetType().GetProperty("Command",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert(command != null, "missing static table command property");
        commands.Add((ushort)command.GetValue(table));
    }
    Assert(commands.SequenceEqual(new ushort[]
        {
            0x65, 0x66, 0x67, 0x68, 0x73, 0x6C, 0x75, 0x76, 0x6D
        }), "native static loader order");

    var antiqueBytes = new Dictionary<string, byte[]>
    {
        ["AntiqueName"] = Encoding.ASCII.GetBytes("AntiqueAlpha"),
        ["baseItemName"] = Encoding.ASCII.GetBytes("BaseBlade")
    };
    var antiqueValues = new Dictionary<string, int>
    {
        ["antiqueLv"] = 0xA0,
        ["maxAntiqueLv"] = 0xA1,
        ["mysteryCnt"] = 0xA2,
        ["maxMysteryCnt"] = 0xA3,
        ["steelLv"] = 0xA8,
        ["veinslv"] = 0xA9
    };
    for (var i = 1; i <= 4; i++)
    {
        antiqueBytes[$"abilName{i}"] =
            Encoding.ASCII.GetBytes($"Ability{i}");
        antiqueBytes[$"specAbil{i}"] =
            Encoding.ASCII.GetBytes($"Special{i}");
        antiqueValues[$"abilVal{i}"] = 0xA3 + i;
    }
    var antique = NativeType2StaticRecordBuilder.Build(0x73,
        new NativeType2StaticRow(antiqueBytes, antiqueValues), true);
    var expectedAntique = Packet(0x73, 0xB6, true);
    WriteShortString(expectedAntique, 0x00, antiqueBytes["AntiqueName"]);
    WriteShortString(expectedAntique, 0x10, antiqueBytes["baseItemName"]);
    for (var i = 1; i <= 4; i++)
    {
        WriteShortString(expectedAntique, 0x10 + i * 0x10,
            antiqueBytes[$"abilName{i}"]);
        WriteShortString(expectedAntique, 0x50 + i * 0x10,
            antiqueBytes[$"specAbil{i}"]);
        WriteByte(expectedAntique, 0xA3 + i,
            antiqueValues[$"abilVal{i}"]);
    }
    WriteByte(expectedAntique, 0xA0, antiqueValues["antiqueLv"]);
    WriteByte(expectedAntique, 0xA1, antiqueValues["maxAntiqueLv"]);
    WriteByte(expectedAntique, 0xA2, antiqueValues["mysteryCnt"]);
    WriteByte(expectedAntique, 0xA3, antiqueValues["maxMysteryCnt"]);
    WriteByte(expectedAntique, 0xA8, antiqueValues["steelLv"]);
    WriteByte(expectedAntique, 0xA9, antiqueValues["veinslv"]);
    AssertPacket(expectedAntique, antique, "AntiqueItems 0x73");

    var forceValues = new Dictionary<string, int>
    {
        ["Level"] = 11,
        ["NeedExp"] = 0x10203040
    };
    var families = new[]
    {
        "AC", "MaxAC", "MAC", "MaxMAC", "MainPower", "MaxMainPower"
    };
    for (var family = 0; family < families.Length; family++)
        for (var level = 1; level <= 5; level++)
            forceValues[families[family] + level] =
                1000 + family * 100 + level;
    var superForce = NativeType2StaticRecordBuilder.Build(0x75,
        new NativeType2StaticRow(int32Values: forceValues), true);
    var expectedForce = Packet(0x75, 0x8C, true);
    WriteInt32(expectedForce, 0x00, forceValues["Level"]);
    WriteInt32(expectedForce, 0x04, forceValues["NeedExp"]);
    for (var family = 0; family < families.Length; family++)
        for (var level = 1; level <= 5; level++)
            WriteInt32(expectedForce,
                0x08 + family * 0x14 + (level - 1) * 4,
                forceValues[families[family] + level]);
    AssertPacket(expectedForce, superForce, "SuperForce 0x75");

    var skillValues = new Dictionary<string, int>
    {
        ["SkillId"] = 0x11223344,
        ["BaseParam"] = 0x21314151,
        ["LevelParam"] = 0x22324252,
        ["upItemParam"] = 0x61,
        ["EffectType"] = 0x62
    };
    for (var i = 1; i <= 9; i++) skillValues[$"NeedLv{i}"] = 0x100 + i;
    for (var i = 1; i <= 4; i++) skillValues[$"Effect{i}"] = 0x70 + i;
    var skillName = Encoding.ASCII.GetBytes("SkillTail");
    var superSkill = NativeType2StaticRecordBuilder.Build(0x76,
        new NativeType2StaticRow(
            new Dictionary<string, byte[]> { ["SkillName"] = skillName },
            skillValues), true);
    var expectedSkill = Packet(0x76, 0x48, true);
    WriteInt32(expectedSkill, 0x00, skillValues["SkillId"]);
    WriteShortString(expectedSkill, 0x04, skillName);
    WriteInt32(expectedSkill, 0x1C, skillValues["BaseParam"]);
    WriteInt32(expectedSkill, 0x20, skillValues["LevelParam"]);
    for (var i = 1; i <= 9; i++)
        WriteUInt16(expectedSkill, 0x22 + i * 2, skillValues[$"NeedLv{i}"]);
    WriteByte(expectedSkill, 0x36, skillValues["upItemParam"]);
    WriteByte(expectedSkill, 0x37, skillValues["EffectType"]);
    for (var i = 1; i <= 4; i++)
        WriteByte(expectedSkill, 0x37 + i, skillValues[$"Effect{i}"]);
    AssertPacket(expectedSkill, superSkill, "SuperSkill 0x76");

    var magicValues = new Dictionary<string, int>
    {
        ["ForceId"] = 0x1234,
        ["MagicIdx"] = 0x2345,
        ["MagKind"] = 0x31,
        ["Effect"] = 0x32,
        ["Spell"] = 0x33,
        ["DefSpell"] = 0x34,
        ["Power"] = 0x35,
        ["DefPower"] = 0x36,
        ["PowerParam"] = 0x37,
        ["LastLv"] = 0x38,
        ["Job"] = 0x39
    };
    for (var i = 1; i <= 5; i++)
    {
        magicValues[$"NeedL{i}"] = 0x200 + i;
        magicValues[$"L{i}Train"] = 0x30405060 + i;
        magicValues[$"L{i}NeedStone"] = 0x40 + i;
    }
    var magicName = Encoding.ASCII.GetBytes("ForceTail");
    var forceMagic = NativeType2StaticRecordBuilder.Build(0x6D,
        new NativeType2StaticRow(
            new Dictionary<string, byte[]> { ["Name"] = magicName },
            magicValues), true);
    var expectedMagic = Packet(0x6D, 0x50, true);
    WriteUInt16(expectedMagic, 0x00, magicValues["ForceId"]);
    WriteUInt16(expectedMagic, 0x02, magicValues["MagicIdx"]);
    WriteShortString(expectedMagic, 0x04, magicName);
    WriteByte(expectedMagic, 0x13, magicValues["MagKind"]);
    WriteByte(expectedMagic, 0x14, magicValues["Effect"]);
    WriteByte(expectedMagic, 0x15, magicValues["Spell"]);
    WriteByte(expectedMagic, 0x16, magicValues["DefSpell"]);
    WriteByte(expectedMagic, 0x17, magicValues["Power"]);
    WriteByte(expectedMagic, 0x18, magicValues["DefPower"]);
    WriteByte(expectedMagic, 0x19, magicValues["PowerParam"]);
    WriteByte(expectedMagic, 0x1A, magicValues["LastLv"]);
    WriteByte(expectedMagic, 0x1B, magicValues["Job"]);
    for (var i = 1; i <= 5; i++)
    {
        WriteUInt16(expectedMagic, 0x1A + i * 2, magicValues[$"NeedL{i}"]);
        WriteInt32(expectedMagic, 0x24 + i * 4, magicValues[$"L{i}Train"]);
        WriteByte(expectedMagic, 0x3B + i, magicValues[$"L{i}NeedStone"]);
    }
    AssertPacket(expectedMagic, forceMagic, "ForceMagic 0x6D");

    return new[] { antique, superForce, superSkill, forceMagic };
}

static void VerifyM2IgnoredFramesAreNoOp(byte[][] builtPackets)
{
    var consumeStatic = RequiredPrivateMethod("ConsumeStaticInitializationFrame");
    var process = RequiredPrivateMethod("ProcessNativeFrame");
    using var service = new DBService();
    var beforeStatic = SnapshotService(service);

    foreach (var payload in builtPackets)
    {
        var copy = (byte[])payload.Clone();
        Assert(InvokeBool(consumeStatic, service,
            new LegacyDbServerFrame(2, 0, payload)),
            "tail packet escaped the original static callback path");
        Assert(payload.AsSpan().SequenceEqual(copy),
            "static callback mutated tail input");
    }
    AssertService(beforeStatic, SnapshotService(service),
        "static callback tail packets");
    Assert(!service.StaticInitializationCompleted,
        "tail completion marker opened static initialization gate");

    var fieldHeroDone = Packet(0x6C, 12, true);
    Assert(InvokeBool(consumeStatic, service,
        new LegacyDbServerFrame(2, 0, fieldHeroDone)),
        "108 completion was not consumed by static callback");
    Assert(service.StaticInitializationCompleted,
        "108 completion did not open static initialization gate");

    var rankings = ReadPrivate<NativeType2SecondaryRankingState>(service,
        "_secondaryRankings");
    var seed = Packet(0x69, 14, false);
    BinaryPrimitives.WriteInt32LittleEndian(seed.AsSpan(4, 4), 2);
    seed[12] = 0xAA;
    seed[13] = 0xBB;
    Assert(rankings.Consume(seed) ==
           NativeType2SecondaryRankingResult.RecordAppended,
        "ranking seed failed");
    var rankingBaseline = SnapshotRanking(rankings);
    var serviceBaseline = SnapshotService(service);

    var inputs = new List<byte[]>();
    inputs.AddRange(builtPackets.Select(packet => (byte[])packet.Clone()));
    foreach (var command in new ushort[] { 0x73, 0x75, 0x76, 0x6D })
    {
        var headerOnly = Packet(command, 12, false);
        BinaryPrimitives.WriteInt32LittleEndian(headerOnly.AsSpan(8, 4),
            unchecked((int)0xA5A5A5A5));
        inputs.Add(headerOnly);
        var oddBody = Packet(command, 13, true);
        oddBody[12] = 0xCC;
        inputs.Add(oddBody);
    }
    var truncated = new byte[11];
    BinaryPrimitives.WriteUInt16LittleEndian(truncated, 0x73);
    inputs.Add(truncated);

    foreach (var payload in inputs)
    {
        var copy = (byte[])payload.Clone();
        InvokeVoid(process, service, new LegacyDbServerFrame(2, 0, payload));
        Assert(payload.AsSpan().SequenceEqual(copy),
            "generic Type2 route mutated tail input");
        AssertRanking(rankingBaseline, SnapshotRanking(rankings));
        AssertService(serviceBaseline, SnapshotService(service),
            "generic Type2 tail packet");
    }
}

static ServiceSnapshot SnapshotService(DBService service)
{
    var magic = ReadPrivate<NativeType2MagicSnapshotState>(service,
        "_magicSnapshot");
    var monster = ReadPrivate<NativeType2MonsterSnapshotState>(service,
        "_monsterSnapshot");
    var items = ReadPrivate<NativeType2StdItemSnapshotState>(service,
        "_stdItemSnapshot");
    var heroes = ReadPrivate<NativeType2FieldHeroSnapshotState>(service,
        "_fieldHeroSnapshot");
    var endpoints = ReadPrivate<NativeType2EndpointSlotState>(service,
        "_endpointSlots");
    return new ServiceSnapshot(
        magic.HumanRecords.Count, magic.HeroRecords.Count,
        magic.HumanCompleted, magic.HeroCompleted,
        monster.Records.Count, monster.Completed,
        items.Records.Count, items.Completed, items.ExpectedWireIndex,
        heroes.Records.Count, heroes.Completed,
        service.NativeMagicDefinitionsPublished,
        service.NativeMonsterDefinitionsPublished,
        service.MagicRuntimeCatalog.HumanDefinitions.Count,
        service.MagicRuntimeCatalog.HeroDefinitions.Count,
        service.MonsterRuntimeCatalog.Definitions.Count,
        Enumerable.Range(0, NativeType2EndpointSlotState.SlotCount + 1)
            .Select(endpoints.CopySlot).ToArray());
}

static RankingSnapshot SnapshotRanking(NativeType2SecondaryRankingState state) =>
    new(state.TotalRecordCount, state.LastFinalizeValue,
        state.Level999OrHigherCount,
        Enumerable.Range(0, NativeType2SecondaryRankingState.BucketCount)
            .Select(bucket => state.GetBucket(bucket)
                .Select(record => record.CopyBody()).ToArray()).ToArray());

static void AssertService(ServiceSnapshot expected, ServiceSnapshot actual,
    string description)
{
    Assert(expected with { EndpointSlots = actual.EndpointSlots } == actual,
        description + " changed snapshot/catalog state");
    Assert(expected.EndpointSlots.Length == actual.EndpointSlots.Length
           && expected.EndpointSlots.Zip(actual.EndpointSlots)
               .All(pair => pair.First.AsSpan().SequenceEqual(pair.Second)),
        description + " changed endpoint slots");
}

static void AssertRanking(RankingSnapshot expected, RankingSnapshot actual)
{
    Assert(expected.Total == actual.Total
           && expected.Finalize == actual.Finalize
           && expected.Level999 == actual.Level999
           && expected.Buckets.Length == actual.Buckets.Length,
        "tail packet changed ranking counters");
    for (var bucket = 0; bucket < expected.Buckets.Length; bucket++)
    {
        Assert(expected.Buckets[bucket].Length == actual.Buckets[bucket].Length,
            "tail packet changed ranking bucket count");
        for (var row = 0; row < expected.Buckets[bucket].Length; row++)
            Assert(expected.Buckets[bucket][row].AsSpan().SequenceEqual(
                    actual.Buckets[bucket][row]),
                "tail packet changed ranking body");
    }
}

static MethodInfo RequiredPrivateMethod(string name) =>
    typeof(DBService).GetMethod(name,
        BindingFlags.Instance | BindingFlags.NonPublic, null,
        new[] { typeof(LegacyDbServerFrame) }, null)
    ?? throw new InvalidOperationException("missing DBService method " + name);

static T ReadPrivate<T>(object instance, string name)
{
    var field = instance.GetType().GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(field != null, "missing private field " + name);
    return (T)field.GetValue(instance);
}

static bool InvokeBool(MethodInfo method, object instance,
    LegacyDbServerFrame frame)
{
    try
    {
        return (bool)method.Invoke(instance, new object[] { frame });
    }
    catch (TargetInvocationException exception)
        when (exception.InnerException != null)
    {
        throw exception.InnerException;
    }
}

static void InvokeVoid(MethodInfo method, object instance,
    LegacyDbServerFrame frame)
{
    try
    {
        method.Invoke(instance, new object[] { frame });
    }
    catch (TargetInvocationException exception)
        when (exception.InnerException != null)
    {
        throw exception.InnerException;
    }
}

static byte[] Packet(ushort command, int length, bool completed)
{
    var payload = new byte[length];
    if (length >= 2) BinaryPrimitives.WriteUInt16LittleEndian(payload, command);
    if (completed && length >= 12)
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), 1);
    return payload;
}

static void WriteShortString(byte[] packet, int bodyOffset, byte[] value)
{
    packet[12 + bodyOffset] = checked((byte)value.Length);
    value.CopyTo(packet, 12 + bodyOffset + 1);
}

static void WriteByte(byte[] packet, int bodyOffset, int value) =>
    packet[12 + bodyOffset] = unchecked((byte)value);

static void WriteUInt16(byte[] packet, int bodyOffset, int value) =>
    BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(12 + bodyOffset, 2),
        unchecked((ushort)value));

static void WriteInt32(byte[] packet, int bodyOffset, int value) =>
    BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(12 + bodyOffset, 4),
        value);

static void AssertPacket(byte[] expected, byte[] actual, string description) =>
    Assert(expected.AsSpan().SequenceEqual(actual),
        description + " exact wire layout");

static void Assert(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
}

internal sealed record ServiceSnapshot(
    int HumanMagicCount,
    int HeroMagicCount,
    bool HumanMagicCompleted,
    bool HeroMagicCompleted,
    int MonsterCount,
    bool MonsterCompleted,
    int ItemCount,
    bool ItemCompleted,
    int ItemExpectedWireIndex,
    int FieldHeroCount,
    bool FieldHeroCompleted,
    bool MagicPublished,
    bool MonsterPublished,
    int PublishedHumanCount,
    int PublishedHeroCount,
    int PublishedMonsterCount,
    byte[][] EndpointSlots);

internal sealed record RankingSnapshot(
    int Total,
    ushort Finalize,
    int Level999,
    byte[][][] Buckets);
