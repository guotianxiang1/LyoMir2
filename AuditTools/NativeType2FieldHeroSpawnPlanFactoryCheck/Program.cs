using System.Buffers.Binary;
using System.Text;
using GameSvr.Services;

CheckAllSelectorMappings();
CheckCapturedAndPersistentSelector();
CheckDeferredMaterialization();
CheckReplacementGenerationLifetime();
CheckNullSelection();

Console.WriteLine("PASS NativeType2FieldHeroSpawnPlanFactoryCheck " +
                  "selectors=0..255 plan=dormant " +
                  "materialization=deferred generation=strong-held");

static void CheckAllSelectorMappings()
{
    var items = CreateStandardItems();
    var adapter = new NativeType2FieldHeroRuntimeCatalogAdapter();
    adapter.Publish(CreateFieldHeroCatalog(items,
        CreateFieldHeroBody(Bytes("SelectorHero"), 0, 10)), items);
    var definition = adapter.Ready
        ? CreatePlan(adapter, "SelectorHero", null).Definition
        : throw new InvalidOperationException("runtime catalog not ready");

    for (var value = 0; value <= byte.MaxValue; value++)
    {
        var selector = checked((byte)value);
        var plan = CreatePlan(adapter, "SelectorHero", selector);
        Equal(ExpectedActorKind(selector), plan.ActorKind,
            $"selector {value} actor kind");
        Equal(selector, plan.EffectiveJob,
            $"selector {value} captured value");
        Equal(1L, plan.Generation,
            $"selector {value} generation");
        Check(ReferenceEquals(definition, plan.Definition),
            $"selector {value} definition identity");
    }
}

static void CheckCapturedAndPersistentSelector()
{
    var items = CreateStandardItems();
    var adapter = new NativeType2FieldHeroRuntimeCatalogAdapter();
    adapter.Publish(CreateFieldHeroCatalog(items,
        CreateFieldHeroBody(Bytes("FameHero"), 0, 20)), items);

    var famePlan = CreatePlan(adapter, "FameHero", 6);
    Equal(NativeType2FieldHeroActorKind.MirDotaMatchHumMonTaos,
        famePlan.ActorKind, "fame selector chooses Dota Taos");

    // Abandoning a later spawn stage must not roll the shared selector back.
    var afterFailure = CreatePlan(adapter, "FameHero", null);
    Equal((byte)6, afterFailure.EffectiveJob,
        "abandoned spawn retains shared selector");
    Equal(NativeType2FieldHeroActorKind.MirDotaMatchHumMonTaos,
        afterFailure.ActorKind,
        "rank-zero spawn observes persistent selector");

    var captured = CreatePlan(adapter, "FameHero", 2);
    var latest = CreatePlan(adapter, "FameHero", 7);
    Equal((byte)2, captured.EffectiveJob,
        "existing plan retains captured selector");
    Equal(NativeType2FieldHeroActorKind.FieldTaosHero,
        captured.ActorKind, "existing plan retains captured class");
    Equal((byte)7, latest.EffectiveJob,
        "new plan observes latest selector");
    Equal(NativeType2FieldHeroActorKind.MirDotaMatchHumMonAss,
        latest.ActorKind, "new plan observes latest class");
}

static void CheckDeferredMaterialization()
{
    var items = CreateStandardItems(Bytes("Known"));
    var body = CreateFieldHeroBody(Bytes("EquipmentHero"), 0, 30);
    WriteShortString(body, EquipmentOffset(0), Bytes("Missing"));
    WriteShortString(body, EquipmentOffset(1), Bytes("Known"));
    var adapter = new NativeType2FieldHeroRuntimeCatalogAdapter();
    adapter.Publish(CreateFieldHeroCatalog(items, body), items);

    var plan = CreatePlan(adapter, "EquipmentHero", null);

    var materialization = plan.MaterializeEquipment();
    Equal(NativeType2FieldHeroDefinition.EquipmentSlotCount,
        materialization.Equipment.Count, "all equipment slots retained");
    Check(materialization.Equipment[0].IsMissing,
        "missing first slot retained");
    Check(materialization.Equipment[1].IsResolved,
        "known later slot resolves after missing slot");
    Check(ReferenceEquals(items.Items[1],
            materialization.Equipment[1].Item),
        "resolved item remains manager-owned");
}

static void CheckReplacementGenerationLifetime()
{
    var items = CreateStandardItems(Bytes("OldBlade"), Bytes("NewBlade"));
    var oldBody = CreateFieldHeroBody(Bytes("ReloadHero"), 1, 11);
    WriteShortString(oldBody, EquipmentOffset(0), Bytes("OldBlade"));
    var nextBody = CreateFieldHeroBody(Bytes("ReloadHero"), 4, 22);
    WriteShortString(nextBody, EquipmentOffset(0), Bytes("NewBlade"));

    var adapter = new NativeType2FieldHeroRuntimeCatalogAdapter();
    adapter.Publish(CreateFieldHeroCatalog(items, oldBody), items);
    var oldPlan = CreatePlan(adapter, "ReloadHero", null);

    adapter.Replace(CreateFieldHeroCatalog(items, nextBody), items);
    var nextPlan = CreatePlan(adapter, "ReloadHero", null);

    Equal(1L, oldPlan.Generation, "old plan generation");
    Equal((ushort)11, oldPlan.Definition.Level,
        "old plan definition remains reachable");
    Equal(NativeType2FieldHeroActorKind.FieldWizHero,
        oldPlan.ActorKind, "old plan class remains captured");
    Equal(2L, nextPlan.Generation, "replacement plan generation");
    Equal((ushort)22, nextPlan.Definition.Level,
        "replacement plan definition");
    Equal(NativeType2FieldHeroActorKind.MirDotaMatchHumMonWar,
        nextPlan.ActorKind, "replacement plan class");

    var oldMaterialization = oldPlan.MaterializeEquipment();
    var nextMaterialization = nextPlan.MaterializeEquipment();
    Check(ReferenceEquals(items.Items[1],
            oldMaterialization.Equipment[0].Item),
        "old plan retains old manager-owned item binding");
    Check(ReferenceEquals(items.Items[2],
            nextMaterialization.Equipment[0].Item),
        "replacement plan exposes new item binding");
}

static void CheckNullSelection()
{
    ExpectThrows<ArgumentNullException>(() =>
            NativeType2FieldHeroSpawnPlanFactory.Create(null),
        "null selection rejected");
}

static NativeType2FieldHeroSpawnPlan CreatePlan(
    NativeType2FieldHeroRuntimeCatalogAdapter adapter, string name,
    byte? fameJob)
{
    if (!adapter.TryResolveTemplate(name, out var template))
        throw new InvalidOperationException("missing runtime selection " + name);
    var selection = template.CaptureSelectionAfterPlacement(fameJob);
    return NativeType2FieldHeroSpawnPlanFactory.Create(selection);
}

static NativeType2FieldHeroActorKind ExpectedActorKind(byte selector) =>
    selector switch
    {
        0 => NativeType2FieldHeroActorKind.FieldWarHero,
        1 => NativeType2FieldHeroActorKind.FieldWizHero,
        2 => NativeType2FieldHeroActorKind.FieldTaosHero,
        3 => NativeType2FieldHeroActorKind.FieldAssHero,
        4 => NativeType2FieldHeroActorKind.MirDotaMatchHumMonWar,
        5 => NativeType2FieldHeroActorKind.MirDotaMatchHumMonWiz,
        6 => NativeType2FieldHeroActorKind.MirDotaMatchHumMonTaos,
        7 => NativeType2FieldHeroActorKind.MirDotaMatchHumMonAss,
        _ => NativeType2FieldHeroActorKind.ModelHero
    };

static NativeType2FieldHeroStaticCatalog CreateFieldHeroCatalog(
    NativeType2StdItemStaticCatalog items, params byte[][] bodies)
{
    var snapshot = new NativeType2FieldHeroSnapshotState();
    for (var index = 0; index < bodies.Length; index++)
    {
        snapshot.Consume(CreatePacket(
            NativeType2FieldHeroSnapshotState.Command,
            NativeType2FieldHeroSnapshotState.HeaderSize, bodies[index],
            index == bodies.Length - 1));
    }

    var catalog = new NativeType2FieldHeroStaticCatalog();
    catalog.Publish(snapshot, items);
    return catalog;
}

static NativeType2StdItemStaticCatalog CreateStandardItems(
    params byte[][] names)
{
    var snapshot = NativeType2StdItemSnapshotState
        .CreateForVerifiedOriginalStartup();
    if (names.Length == 0)
    {
        snapshot.Consume(CreatePacket(
            NativeType2StdItemSnapshotState.Command,
            NativeType2StdItemSnapshotState.HeaderSize,
            Array.Empty<byte>(), true));
    }
    else
    {
        for (var index = 0; index < names.Length; index++)
        {
            var body = new byte[NativeType2StdItemSnapshotState.BodySize];
            WriteUInt16(body, 0x00, checked((ushort)(index + 1)));
            WriteShortString(body, 0x04, names[index]);
            snapshot.Consume(CreatePacket(
                NativeType2StdItemSnapshotState.Command,
                NativeType2StdItemSnapshotState.HeaderSize, body,
                index == names.Length - 1));
        }
    }

    var catalog = new NativeType2StdItemStaticCatalog();
    catalog.Publish(snapshot);
    return catalog;
}

static byte[] CreateFieldHeroBody(byte[] name, byte job, ushort level)
{
    var body = new byte[NativeType2FieldHeroSnapshotState.BodySize];
    WriteShortString(body, 0x00, name);
    body[0x10] = job;
    WriteUInt16(body, 0x12, level);
    return body;
}

static byte[] CreatePacket(ushort command, int headerSize, byte[] body,
    bool completed)
{
    var packet = new byte[headerSize + body.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(packet, command);
    if (completed) WriteInt32(packet, 0x08, 1);
    body.CopyTo(packet, headerSize);
    return packet;
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
