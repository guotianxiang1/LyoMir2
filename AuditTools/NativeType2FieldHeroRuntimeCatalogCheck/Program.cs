using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text;
using GameSvr.Services;
using SystemModule;

CheckPersistentFameSelector();
CheckEquipmentBindingsWithoutSideEffects();
CheckReplacementLifetimeAndReset();
CheckConcurrentPublicationCapture();
CheckNativeLookupNameCanonicalization();
CheckDuplicateNormalizedNamesUseLastLoadedEntry();

Console.WriteLine("PASS NativeType2FieldHeroRuntimeCatalogCheck " +
                  "selector=persistent equipment=side-effect-free " +
                  "publication=atomic generation=borrower-held");

static void CheckPersistentFameSelector()
{
    var items = CreateStandardItems();
    var body = CreateFieldHeroBody(Bytes("SelectorHero"), 3, 10);
    var definitions = CreateFieldHeroCatalog(items, body);
    var adapter = new NativeType2FieldHeroRuntimeCatalogAdapter();
    adapter.Publish(definitions, items);

    Check(adapter.TryResolveTemplate("SelectorHero", out var template),
        "initial selector lookup");
    Equal((byte)3, template.CurrentEffectiveJob,
        "wire selector initial value");
    Equal(1L, template.Generation, "initial generation");

    // The factory has not completed placement yet. A placement failure must
    // leave template+0x10 untouched and skip fame selection entirely.
    Equal((byte)3, template.CurrentEffectiveJob,
        "unconsumed template has no fame side effect");

    var fame = template.CaptureSelectionAfterPlacement(byte.MaxValue);
    Equal(byte.MaxValue, fame.EffectiveJob,
        "selector remains full byte and is not clamped to 0..7");

    // Simulate a later constructor/publication failure by abandoning fame.
    // Native does not roll the template mutation back.
    Check(adapter.TryResolveTemplate("SelectorHero", out var afterTemplate),
        "selector lookup after abandoned spawn");
    var afterFailure = afterTemplate.CaptureSelectionAfterPlacement(null);
    Equal(byte.MaxValue, afterFailure.EffectiveJob,
        "abandoned spawn did not roll back shared selector");

    Check(!adapter.TryResolveTemplate("Missing", out _),
        "template miss does not create or mutate a sidecar");
    ExpectThrows<InvalidOperationException>(() =>
            adapter.Publish(definitions, items),
        "normal publication remains one-shot");
}

static void CheckEquipmentBindingsWithoutSideEffects()
{
    var items = CreateStandardItems(Bytes("Blade"), Bytes("Shield"));
    var body = CreateFieldHeroBody(Bytes("EquipmentHero"), 0, 20);
    WriteShortString(body, EquipmentOffset(0), Bytes("MissingFirst"));
    WriteShortString(body, EquipmentOffset(1), Bytes("Shield"));
    WriteShortString(body, EquipmentOffset(2), Bytes("blade"));
    WriteShortString(body, EquipmentOffset(3),
        HUtil32.GbkEncoding.GetBytes("金币"));

    var adapter = new NativeType2FieldHeroRuntimeCatalogAdapter();
    adapter.Publish(CreateFieldHeroCatalog(items, body), items);
    Check(TryCaptureAfterPlacement(adapter, "EquipmentHero", null,
            out var selection),
        "equipment hero lookup");

    var materialization = selection.MaterializeEquipment();
    Equal(14, materialization.Equipment.Count, "all slots retained");
    Check(materialization.Equipment[0].IsMissing,
        "unknown first slot retained as missing");
    Check(materialization.Equipment[1].IsResolved,
        "known later slot resolves after an earlier miss");
    Check(ReferenceEquals(items.Items[2],
            materialization.Equipment[1].Item),
        "resolved slot borrows manager-owned GoodItem");
    Check(materialization.Equipment[2].IsMissing,
        "lookup remains wire-byte exact instead of case-insensitive");
    Check(materialization.Equipment[3].IsMissing,
        "local index-zero gold sentinel is excluded from wire definitions");
    Check(materialization.Equipment[4].IsEmpty,
        "empty slot remains empty");
    Check(ReferenceEquals(materialization.Equipment,
            selection.MaterializeEquipment().Equipment),
        "repeated materialization borrows the same immutable bindings");
}

static void CheckReplacementLifetimeAndReset()
{
    var items = CreateStandardItems(Bytes("OldBlade"), Bytes("NewBlade"));
    var oldBody = CreateFieldHeroBody(Bytes("ReloadHero"), 1, 11);
    WriteShortString(oldBody, EquipmentOffset(0), Bytes("OldBlade"));
    var nextBody = CreateFieldHeroBody(Bytes("ReloadHero"), 2, 22);
    WriteShortString(nextBody, EquipmentOffset(0), Bytes("NewBlade"));

    var adapter = new NativeType2FieldHeroRuntimeCatalogAdapter();
    adapter.Publish(CreateFieldHeroCatalog(items, oldBody), items);
    Check(TryCaptureAfterPlacement(adapter, "ReloadHero", 7,
            out var oldSelection),
        "old generation fame mutation");
    var oldMaterialization = oldSelection.MaterializeEquipment();

    var failedGeneration = adapter.Generation;
    ExpectThrows<InvalidOperationException>(() => adapter.Replace(
            CreateFieldHeroCatalog(items, nextBody),
            new NativeType2StdItemStaticCatalog()),
        "failed replacement rejects an unpublished item snapshot");
    Equal(failedGeneration, adapter.Generation,
        "failed replacement leaves the current generation intact");

    adapter.Replace(CreateFieldHeroCatalog(items, nextBody), items);
    Equal(2L, adapter.Generation, "replacement generation");
    Check(TryCaptureAfterPlacement(adapter, "ReloadHero", null,
            out var nextSelection),
        "new generation lookup");
    Equal((byte)2, nextSelection.EffectiveJob,
        "new generation starts from its wire selector");
    Equal((ushort)22, nextSelection.Definition.Level,
        "new generation exposes only its complete definition");

    Equal(1L, oldSelection.Generation,
        "old in-flight selection retains its generation");
    Equal((byte)7, oldSelection.EffectiveJob,
        "old selection retains its captured fame selector");
    Equal("OldBlade", oldMaterialization.Equipment[0].Item.Name,
        "old actor borrow remains valid after replacement");
}

static void CheckConcurrentPublicationCapture()
{
    var items = CreateStandardItems();
    var first = CreateFieldHeroCatalog(items,
        CreateFieldHeroBody(Bytes("ConcurrentHero"), 1, 101));
    var second = CreateFieldHeroCatalog(items,
        CreateFieldHeroBody(Bytes("ConcurrentHero"), 2, 202));
    var adapter = new NativeType2FieldHeroRuntimeCatalogAdapter();
    adapter.Publish(first, items);

    var failures = new ConcurrentQueue<string>();
    using var start = new ManualResetEventSlim(false);
    var writer = Task.Run(() =>
    {
        start.Wait();
        for (var index = 0; index < 64; index++)
            adapter.Replace((index & 1) == 0 ? second : first, items);
    });
    var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
    {
        start.Wait();
        for (var index = 0; index < 2000; index++)
        {
            if (!TryCaptureAfterPlacement(adapter, "ConcurrentHero", null,
                    out var selection))
            {
                failures.Enqueue("lookup observed an empty publication");
                continue;
            }
            var coherent = selection.Definition.Level == 101
                           && selection.EffectiveJob == 1
                           || selection.Definition.Level == 202
                           && selection.EffectiveJob == 2;
            if (!coherent)
                failures.Enqueue("lookup mixed two publication generations");
        }
    })).ToArray();

    start.Set();
    Task.WaitAll(readers.Append(writer).ToArray());
    Check(failures.IsEmpty,
        failures.TryPeek(out var failure) ? failure : "concurrent failure");
    Equal(65L, adapter.Generation,
        "all replacements committed a complete generation");
}

static void CheckNativeLookupNameCanonicalization()
{
    var items = CreateStandardItems();
    var body = CreateFieldHeroBody(Bytes("MixedCaseHero"), 2, 33);
    var adapter = new NativeType2FieldHeroRuntimeCatalogAdapter();
    adapter.Publish(CreateFieldHeroCatalog(items, body), items);

    Check(TryCaptureAfterPlacement(adapter, "mixedcasehero", null,
            out var lower),
        "runtime lowercase query resolves mixed-case native key");
    Check(TryCaptureAfterPlacement(adapter, "MIXEDCASEHERO", null,
            out var upper),
        "runtime uppercase query resolves mixed-case native key");
    Check(ReferenceEquals(lower.Definition, upper.Definition),
        "runtime case variants resolve the same definition");
}

static void CheckDuplicateNormalizedNamesUseLastLoadedEntry()
{
    var items = CreateStandardItems();
    var first = CreateFieldHeroBody(Bytes("DuplicateHero"), 1, 11);
    var last = CreateFieldHeroBody(Bytes("duplicatehero"), 2, 22);
    var adapter = new NativeType2FieldHeroRuntimeCatalogAdapter();
    adapter.Publish(CreateFieldHeroCatalog(items, first, last), items);

    Check(adapter.TryResolveTemplate("DUPLICATEHERO", out var template),
        "normalized duplicate resolves");
    Equal((ushort)22, template.Definition.Level,
        "hash-bucket head favors the last loaded normalized key");
    Equal((byte)2, template.CurrentEffectiveJob,
        "last loaded definition supplies the initial selector");
}

static bool TryCaptureAfterPlacement(
    NativeType2FieldHeroRuntimeCatalogAdapter adapter, string name,
    byte? fameJob, out NativeType2FieldHeroSpawnSelection selection)
{
    selection = null;
    if (!adapter.TryResolveTemplate(name, out var template)) return false;
    selection = template.CaptureSelectionAfterPlacement(fameJob);
    return true;
}

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
    if (bodies.Length == 0)
    {
        snapshot.Consume(CreatePacket(
            NativeType2FieldHeroSnapshotState.Command,
            NativeType2FieldHeroSnapshotState.HeaderSize,
            Array.Empty<byte>(), true));
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
