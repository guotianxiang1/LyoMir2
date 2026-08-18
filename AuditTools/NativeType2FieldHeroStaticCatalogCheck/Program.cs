using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using GameSvr;
using GameSvr.Services;
using SystemModule;

// This audit proves the immutable wire snapshot and its atomic publication.
// Equipment resolution is deliberately deferred to actor materialization,
// matching native sub_60B154 log-and-continue behavior. These checks do not
// claim that the later factory or its nine actor classes exist.

CheckStrongParserAndAllSlots();
CheckShortStringPolicies();
CheckHeaderOnlyEmptyPublication();
CheckAllSlotReferencesAreDeferred();
CheckEquipmentBytesRemainOpaqueAtPublication();
CheckAtomicFailureAndOneShotPublication();
CheckDuplicateNamesUseHashHeadLastMatch();
CheckNativeLookupNameCanonicalization();
CheckDbServicePublicationOutcome();

Console.WriteLine("PASS NativeType2FieldHeroStaticCatalogCheck " +
                  "parser=strong slots=14 empty=ready " +
                  "equipment=deferred publication=atomic " +
                  "duplicates=hash-head-last tail=opaque");

static void CheckStrongParserAndAllSlots()
{
    var body = CreateFieldHeroBody(Bytes("TypedHero"));
    body[0x0F] = 1;
    body[0x10] = byte.MaxValue;
    body[0x11] = 0xA1;
    WriteUInt16(body, 0x12, 0x2345);
    body[0x14] = 2;
    body[0x15] = 3;
    body[0x16] = 4;
    body[0x17] = 0xA2;
    WriteInt32(body, 0x18, int.MinValue);
    WriteInt32(body, 0x1C, int.MaxValue);

    for (var index = 0;
         index < NativeType2FieldHeroDefinition.EquipmentSlotCount;
         index++)
    {
        var offset = EquipmentOffset(index);
        WriteShortString(body, offset, Bytes($"Item{index:D2}"));
        body[offset + 0x0F] = unchecked((byte)(0xB0 + index));
        WriteInt32(body, offset + 0x10,
            index == 0 ? int.MinValue : -1000 - index);
    }
    WriteUInt32(body, NativeType2FieldHeroDefinition.RuntimeSlotOffset,
        0xDEADBEEF);

    var definition = NativeType2FieldHeroDefinition.Parse(body);
    Equal("TypedHero", definition.Name, "typed name");
    Equal((byte)1, definition.Sex, "typed sex");
    Equal(byte.MaxValue, definition.Job, "all job bytes remain valid");
    Equal((byte)0xA1, definition.Reserved11, "reserved 11 preserved");
    Equal((ushort)0x2345, definition.Level, "typed level");
    Equal((byte)2, definition.BossLevel, "typed boss level");
    Equal((byte)3, definition.BodyLuck, "typed body luck");
    Equal((byte)4, definition.AddHitPoint, "typed hit point");
    Equal((byte)0xA2, definition.Reserved17, "reserved 17 preserved");
    Equal(int.MinValue, definition.DrinkDrug, "typed drink drug");
    Equal(int.MaxValue, definition.Experience, "typed experience");
    Equal(0xDEADBEEFu, definition.WireRuntimeSlot,
        "opaque runtime slot preserved");
    Equal(14, definition.Equipment.Count, "all equipment slots parsed");

    for (var index = 0; index < definition.Equipment.Count; index++)
    {
        var equipment = definition.Equipment[index];
        Equal((NativeType2FieldHeroEquipmentSlot)index, equipment.Slot,
            $"slot enum {index}");
        Equal($"Item{index:D2}", equipment.Name, $"slot name {index}");
        Equal(unchecked((byte)(0xB0 + index)), equipment.Reserved,
            $"slot reserved {index}");
        Equal(index == 0 ? int.MinValue : -1000 - index,
            equipment.Scatter, $"slot scatter {index}");
    }

    body[1] = (byte)'X';
    Check(definition.Name == "TypedHero", "definition owns its wire copy");
    var copy = definition.CopyWireBody();
    copy[1] = (byte)'Y';
    Check(definition.CopyWireBody()[1] == (byte)'T',
        "wire copy is defensive");
}

static void CheckShortStringPolicies()
{
    ExpectThrows<InvalidDataException>(() =>
            NativeType2FieldHeroDefinition.Parse(
                new byte[NativeType2FieldHeroSnapshotState.BodySize - 1]),
        "short body rejected");
    ExpectThrows<InvalidDataException>(() =>
            NativeType2FieldHeroDefinition.Parse(
                new byte[NativeType2FieldHeroSnapshotState.BodySize + 1]),
        "long body rejected");

    ExpectThrows<InvalidDataException>(() =>
            NativeType2FieldHeroDefinition.Parse(
                new byte[NativeType2FieldHeroSnapshotState.BodySize]),
        "empty field-hero name rejected");
    var longHeroName = CreateFieldHeroBody(Bytes("Valid"));
    longHeroName[0] = 15;
    ExpectThrows<InvalidDataException>(() =>
            NativeType2FieldHeroDefinition.Parse(longHeroName),
        "15-byte field-hero name rejected");

    for (var index = 0;
         index < NativeType2FieldHeroDefinition.EquipmentSlotCount;
         index++)
    {
        var body = CreateFieldHeroBody(Bytes("LengthHero"));
        body[EquipmentOffset(index)] = 15;
        ExpectThrows<InvalidDataException>(() =>
                NativeType2FieldHeroDefinition.Parse(body),
            $"slot {index} length 15 rejected");
    }
}

static void CheckHeaderOnlyEmptyPublication()
{
    var standardItems = CreateStandardItemCatalog();
    var snapshot = CreateFieldHeroSnapshot();
    Check(snapshot.Completed && snapshot.Records.Count == 0,
        "header-only terminal preserves raw snapshot contract");

    var catalog = new NativeType2FieldHeroStaticCatalog();
    catalog.Publish(snapshot, standardItems);
    Check(catalog.Ready, "zero-record publication is ready");
    Equal(0, catalog.Count, "zero-record publication count");
    Check(catalog.FindByName("anything") == null,
        "empty catalog string lookup");
    Check(catalog.FindByNameBytes(Bytes("anything")) == null,
        "empty catalog byte lookup");

    var independent = new NativeType2FieldHeroStaticCatalog();
    independent.Publish(snapshot, new NativeType2StdItemStaticCatalog());
    Check(independent.Ready && independent.Count == 0,
        "wire publication does not depend on standard-item completion");

    var incomplete = new NativeType2FieldHeroStaticCatalog();
    ExpectThrows<InvalidDataException>(() => incomplete.Publish(
            new NativeType2FieldHeroSnapshotState(), standardItems),
        "incomplete snapshot rejected");
    Check(!incomplete.Ready && incomplete.Count == 0,
        "incomplete publication remains empty");
}

static void CheckAllSlotReferencesAreDeferred()
{
    var names = Enumerable.Range(0,
            NativeType2FieldHeroDefinition.EquipmentSlotCount)
        .Select(index => Bytes($"Item{index:D2}"))
        .ToArray();
    var standardItems = CreateStandardItemCatalog(names);
    var body = CreateFieldHeroBody(Bytes("KnownHero"));
    for (var index = 0; index < names.Length; index++)
        WriteShortString(body, EquipmentOffset(index), names[index]);

    var catalog = new NativeType2FieldHeroStaticCatalog();
    catalog.Publish(CreateFieldHeroSnapshot(body), standardItems);
    Check(catalog.Ready && catalog.Count == 1,
        "all 14 known references publish");

    var emptyEquipment = new NativeType2FieldHeroStaticCatalog();
    emptyEquipment.Publish(CreateFieldHeroSnapshot(
        CreateFieldHeroBody(Bytes("EmptyEquipment"))), standardItems);
    Check(emptyEquipment.Ready && emptyEquipment.Count == 1,
        "all empty equipment slots publish");

    for (var slot = 0;
         slot < NativeType2FieldHeroDefinition.EquipmentSlotCount;
         slot++)
    {
        var unknownBody = CreateFieldHeroBody(Bytes($"Unknown{slot:D2}"));
        WriteShortString(unknownBody, EquipmentOffset(slot),
            Bytes($"Missing{slot:D2}"));
        var deferred = new NativeType2FieldHeroStaticCatalog();
        deferred.Publish(CreateFieldHeroSnapshot(unknownBody),
            standardItems);
        Check(deferred.Ready && deferred.Count == 1,
            $"unknown reference in slot {slot} remains publishable");
        Equal($"Missing{slot:D2}",
            deferred.Definitions[0].Equipment[slot].Name,
            $"unknown reference in slot {slot} remains available");
    }
}

static void CheckEquipmentBytesRemainOpaqueAtPublication()
{
    var standardItems = CreateStandardItemCatalog(Bytes("Blade"));
    var differentCase = CreateFieldHeroBody(Bytes("CaseHero"));
    WriteShortString(differentCase, EquipmentOffset(0), Bytes("blade"));
    var caseCatalog = new NativeType2FieldHeroStaticCatalog();
    caseCatalog.Publish(CreateFieldHeroSnapshot(differentCase),
        standardItems);
    Equal("blade", caseCatalog.Definitions[0].Equipment[0].Name,
        "equipment case remains opaque until materialization");

    var goldEquipment = CreateFieldHeroBody(Bytes("GoldHero"));
    WriteShortString(goldEquipment, EquipmentOffset(0),
        HUtil32.GbkEncoding.GetBytes("金币"));
    var goldCatalog = new NativeType2FieldHeroStaticCatalog();
    goldCatalog.Publish(CreateFieldHeroSnapshot(goldEquipment),
        CreateStandardItemCatalog());
    Equal("金币", goldCatalog.Definitions[0].Equipment[0].Name,
        "index-zero sentinel name remains opaque until materialization");
}

static void CheckAtomicFailureAndOneShotPublication()
{
    var standardItems = CreateStandardItemCatalog(Bytes("Known"));
    var validBody = CreateFieldHeroBody(Bytes("Original"));
    WriteShortString(validBody, EquipmentOffset(0), Bytes("Known"));
    var invalidBody = CreateFieldHeroBody(Bytes("Rejected"));
    invalidBody[EquipmentOffset(13)] = 15;
    var catalog = new NativeType2FieldHeroStaticCatalog();

    var emptyDefinitions = catalog.Definitions;
    ExpectThrows<InvalidDataException>(() => catalog.Publish(
            CreateFieldHeroSnapshot(validBody, invalidBody), standardItems),
        "mixed structurally valid/invalid initial publication rejected");
    Check(!catalog.Ready && catalog.Count == 0
          && ReferenceEquals(emptyDefinitions, catalog.Definitions),
        "failed initial publication commits no partial prefix");

    catalog.Publish(CreateFieldHeroSnapshot(validBody), standardItems);

    var originalDefinitions = catalog.Definitions;
    var originalDefinition = catalog.Definitions[0];
    var replacementBody = CreateFieldHeroBody(Bytes("Replacement"));
    ExpectThrows<InvalidOperationException>(() => catalog.Publish(
            CreateFieldHeroSnapshot(replacementBody), standardItems),
        "second startup publication rejected");

    Check(catalog.Ready && catalog.Count == 1,
        "second publication retains ready catalog");
    Check(ReferenceEquals(originalDefinitions, catalog.Definitions)
          && ReferenceEquals(originalDefinition, catalog.Definitions[0]),
        "second publication preserves publication identity");
    Check(ReferenceEquals(originalDefinition, catalog.FindByName("Original")),
        "second publication preserves query result");
}

static void CheckDuplicateNamesUseHashHeadLastMatch()
{
    var firstBody = CreateFieldHeroBody(Bytes("Duplicate"));
    var secondBody = CreateFieldHeroBody(Bytes("Duplicate"));
    WriteUInt16(firstBody, 0x12, 11);
    WriteUInt16(secondBody, 0x12, 22);

    var catalog = new NativeType2FieldHeroStaticCatalog();
    catalog.Publish(CreateFieldHeroSnapshot(firstBody, secondBody),
        CreateStandardItemCatalog());
    Equal(2, catalog.Count, "duplicate names retained");
    Equal((ushort)11, catalog.Definitions[0].Level,
        "first duplicate retained");
    Equal((ushort)22, catalog.Definitions[1].Level,
        "second duplicate retained");
    Check(ReferenceEquals(catalog.Definitions[1],
            catalog.FindByName("Duplicate"))
          && ReferenceEquals(catalog.Definitions[1],
              catalog.FindByNameBytes(Bytes("Duplicate"))),
        "duplicate lookup observes the last bucket-head insertion");
}

static void CheckNativeLookupNameCanonicalization()
{
    var asciiBody = CreateFieldHeroBody(Bytes("CaseHero"));
    var trailBody = CreateFieldHeroBody(
        new byte[] { 0x81, (byte)'A', (byte)'X' });
    var catalog = new NativeType2FieldHeroStaticCatalog();
    catalog.Publish(CreateFieldHeroSnapshot(asciiBody, trailBody),
        CreateStandardItemCatalog());

    Check(ReferenceEquals(catalog.Definitions[0],
            catalog.FindByNameBytes(Bytes("casehero"))),
        "ASCII query uses native bytewise lowercase key");
    Check(ReferenceEquals(catalog.Definitions[1],
            catalog.FindByNameBytes(
                new byte[] { 0x81, (byte)'a', (byte)'x' })),
        "GBK trail bytes in A..Z are folded like sub_40BCBC");
    Check(catalog.FindByNameBytes(
              new byte[] { 0x81, (byte)'a', (byte)'[' }) == null,
        "bytes outside A..Z remain distinct");
}

static void CheckDbServicePublicationOutcome()
{
    using (var service = new DBService())
    {
        PublishStandardItems(service.StdItemRuntimeCatalog);
        InvokeFieldHeroCompletion(service, CreateFieldHeroSnapshot());
        Check(service.StaticInitializationCompleted,
            "zero-record callback signals completion");
        Check(service.NativeFieldHeroDefinitionsPublished
              && service.FieldHeroRuntimeCatalog.Ready
              && service.FieldHeroRuntimeCatalog.Count == 0
              && service.FieldHeroSpawnRuntimeCatalog.Ready
              && service.FieldHeroSpawnRuntimeCatalog.Count == 0,
            "zero-record callback publishes both ready empty catalogs");
    }

    using (var service = new DBService())
    {
        PublishStandardItems(service.StdItemRuntimeCatalog, Bytes("Known"));
        var body = CreateFieldHeroBody(Bytes("Deferred"));
        WriteShortString(body, EquipmentOffset(0), Bytes("Missing"));
        InvokeFieldHeroCompletion(service, CreateFieldHeroSnapshot(body));
        Check(service.StaticInitializationCompleted,
            "unknown-equipment callback signals wait completion");
        Check(service.NativeFieldHeroDefinitionsPublished
              && service.FieldHeroRuntimeCatalog.Ready
              && service.FieldHeroRuntimeCatalog.Count == 1
              && service.FieldHeroSpawnRuntimeCatalog.Ready
              && service.FieldHeroSpawnRuntimeCatalog.Count == 1,
            "unknown equipment does not reject either DBService catalog");
        Check(service.FieldHeroSpawnRuntimeCatalog.TryResolveTemplate(
                "deferred", out var runtimeTemplate)
              && runtimeTemplate.Definition.Level == 0,
            "production runtime adapter exposes native normalized lookup");
        Check(ReadPrivate<string>(service,
                  "_fieldHeroPublicationFailure") == null,
            "unknown equipment does not record publication failure");
    }

    using (var service = new DBService())
    {
        PublishStandardItems(service.StdItemRuntimeCatalog, Bytes("Known"));
        var body = CreateFieldHeroBody(Bytes("Malformed"));
        body[EquipmentOffset(0)] = 15;
        InvokeFieldHeroCompletion(service, CreateFieldHeroSnapshot(body));
        Check(service.StaticInitializationCompleted,
            "malformed callback still signals wait completion");
        Check(!service.NativeFieldHeroDefinitionsPublished
              && !service.FieldHeroRuntimeCatalog.Ready
              && !service.FieldHeroSpawnRuntimeCatalog.Ready,
            "malformed callback does not commit either catalog");
        var failure = ReadPrivate<string>(service,
            "_fieldHeroPublicationFailure");
        Check(failure.Contains("exceeds 14 bytes",
                StringComparison.Ordinal),
            "malformed callback records independent publication failure");
    }
}

static NativeType2StdItemStaticCatalog CreateStandardItemCatalog(
    params byte[][] names)
{
    var catalog = new NativeType2StdItemStaticCatalog();
    PublishStandardItems(catalog, names);
    return catalog;
}

static void PublishStandardItems(NativeType2StdItemStaticCatalog catalog,
    params byte[][] names)
{
    var snapshot = NativeType2StdItemSnapshotState
        .CreateForVerifiedOriginalStartup();
    if (names.Length == 0)
    {
        snapshot.Consume(CreateStdItemPacket(Array.Empty<byte>(), true));
    }
    else
    {
        for (var index = 0; index < names.Length; index++)
        {
            var body = new byte[NativeType2StdItemSnapshotState.BodySize];
            WriteUInt16(body, 0x00, checked((ushort)(index + 1)));
            WriteShortString(body, 0x04, names[index]);
            snapshot.Consume(CreateStdItemPacket(body,
                index == names.Length - 1));
        }
    }
    catalog.Publish(snapshot);
}

static NativeType2FieldHeroSnapshotState CreateFieldHeroSnapshot(
    params byte[][] bodies)
{
    var snapshot = new NativeType2FieldHeroSnapshotState();
    if (bodies.Length == 0)
    {
        snapshot.Consume(CreateFieldHeroPacket(Array.Empty<byte>(), true));
    }
    else
    {
        for (var index = 0; index < bodies.Length; index++)
        {
            snapshot.Consume(CreateFieldHeroPacket(bodies[index],
                index == bodies.Length - 1));
        }
    }
    return snapshot;
}

static byte[] CreateFieldHeroBody(byte[] name)
{
    var body = new byte[NativeType2FieldHeroSnapshotState.BodySize];
    WriteShortString(body, 0x00, name);
    return body;
}

static byte[] CreateFieldHeroPacket(byte[] body, bool completed)
{
    var packet = new byte[NativeType2FieldHeroSnapshotState.HeaderSize
                          + body.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(packet,
        NativeType2FieldHeroSnapshotState.Command);
    if (completed)
        WriteInt32(packet, 0x08, 1);
    body.CopyTo(packet, NativeType2FieldHeroSnapshotState.HeaderSize);
    return packet;
}

static byte[] CreateStdItemPacket(byte[] body, bool completed)
{
    var packet = new byte[NativeType2StdItemSnapshotState.HeaderSize
                          + body.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(packet,
        NativeType2StdItemSnapshotState.Command);
    if (completed)
        WriteInt32(packet, 0x08, 1);
    body.CopyTo(packet, NativeType2StdItemSnapshotState.HeaderSize);
    return packet;
}

static void InvokeFieldHeroCompletion(DBService service,
    NativeType2FieldHeroSnapshotState snapshot)
{
    var method = typeof(DBService).GetMethod(
        "PublishFieldHeroDefinitionsWhenCompleted",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(
            "missing DBService FieldHero publication callback");
    method.Invoke(service, new object[] { snapshot });
}

static T ReadPrivate<T>(object instance, string fieldName)
{
    var field = instance.GetType().GetField(fieldName,
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("missing field " + fieldName);
    return (T)field.GetValue(instance);
}

static int EquipmentOffset(int index) =>
    NativeType2FieldHeroDefinition.EquipmentOffset
    + index * NativeType2FieldHeroDefinition.EquipmentStride;

static byte[] Bytes(string value) => Encoding.ASCII.GetBytes(value);

static void WriteShortString(byte[] destination, int offset, byte[] value)
{
    destination[offset] = checked((byte)value.Length);
    value.CopyTo(destination, offset + 1);
}

static void WriteUInt16(byte[] destination, int offset, ushort value) =>
    BinaryPrimitives.WriteUInt16LittleEndian(
        destination.AsSpan(offset, sizeof(ushort)), value);

static void WriteInt32(byte[] destination, int offset, int value) =>
    BinaryPrimitives.WriteInt32LittleEndian(
        destination.AsSpan(offset, sizeof(int)), value);

static void WriteUInt32(byte[] destination, int offset, uint value) =>
    BinaryPrimitives.WriteUInt32LittleEndian(
        destination.AsSpan(offset, sizeof(uint)), value);

static void Equal<T>(T expected, T actual, string description)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            $"{description}: expected {expected}, actual {actual}");
    }
}

static void ExpectThrows<T>(Action action, string description)
    where T : Exception
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

static void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
}
