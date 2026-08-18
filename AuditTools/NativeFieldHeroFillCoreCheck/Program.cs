using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using GameSvr;
using GameSvr.Services;
using SystemModule;

CheckExactOrderAndBindings();
CheckConcreteActorBytes();
CheckWireRuntimeSlotIsEvidenceOnly();
CheckActorDropBindingGate();
CheckExceptionShortCircuit();
CheckEntryContracts();
CheckStaticContracts();

Console.WriteLine("PASS NativeFieldHeroFillCoreCheck " +
                  "function=sub_60B154 scalars=exact ability-copy=0x7C " +
                  "equipment-before-drops generation=owned " +
                  "production=NO-GO");

static void CheckExactOrderAndBindings()
{
    var materialization = CreateMaterialization();
    var events = new List<string>();
    ReadOnlyMemory<byte> observedName = default;
    IReadOnlyList<NativeType2FieldHeroRuntimeEquipmentBinding>
        observedEquipment = null;
    IReadOnlyList<NativeFieldHeroRuntimeDropBinding> observedDrops = null;

    NativeFieldHeroFillCore.Fill(materialization,
        (offset, value, capacity) =>
        {
            events.Add($"name:{offset:X}:{capacity:X}");
            observedName = value;
        },
        (offset, value) => events.Add($"byte:{offset:X}:{value:X2}"),
        (offset, value) => events.Add($"word:{offset:X}:{value:X4}"),
        (offset, value) =>
            events.Add($"int:{offset:X}:{unchecked((uint)value):X8}"),
        (source, destination, length) =>
            events.Add($"copy:{source:X}->{destination:X}:{length:X}"),
        equipment =>
        {
            events.Add("equipment");
            observedEquipment = equipment;
        },
        (offset, drops) =>
        {
            events.Add($"drops:{offset:X}");
            observedDrops = drops;
        });

    Sequence(new[]
    {
        "name:106:E",
        "byte:71:7D",
        "byte:686:A3",
        "int:688:89ABCDEF",
        "word:1FC:BEEF",
        "byte:685:D4",
        "byte:684:E5",
        "int:240:76543210",
        "copy:1E8->264:7C",
        "equipment",
        "drops:474"
    }, events, "exact native Fill order");
    Sequence(new byte[] { 0x41, 0x81, 0x5A, 0x42 }, observedName.ToArray(),
        "raw definition name bytes");
    Check(ReferenceEquals(materialization.Equipment, observedEquipment),
        "equipment callback borrows the exact materialization collection");
    Check(ReferenceEquals(materialization.DropItems, observedDrops),
        "drop callback borrows the exact generation-owned collection");
    Equal(14, observedEquipment.Count, "all 14 equipment bindings retained");
    Equal(1, observedDrops.Count, "runtime drop binding retained");
}

static void CheckExceptionShortCircuit()
{
    var materialization = CreateMaterialization();
    var expected = new[]
    {
        "name", "byte:71", "byte:686", "int:688", "word:1FC",
        "byte:685", "byte:684", "int:240", "copy", "equipment",
        "drops"
    };
    var sentinel = new ApplicationException("sentinel");

    for (var targetIndex = 0; targetIndex < expected.Length; targetIndex++)
    {
        var events = new List<string>();
        void Hit(string value)
        {
            events.Add(value);
            if (events.Count - 1 == targetIndex) throw sentinel;
        }

        try
        {
            NativeFieldHeroFillCore.Fill(materialization,
                (_, _, _) => Hit("name"),
                (offset, _) => Hit($"byte:{offset:X}"),
                (offset, _) => Hit($"word:{offset:X}"),
                (offset, _) => Hit($"int:{offset:X}"),
                (_, _, _) => Hit("copy"),
                _ => Hit("equipment"),
                (_, _) => Hit("drops"));
            throw new Exception(
                "Fill exception was swallowed at " + expected[targetIndex]);
        }
        catch (ApplicationException exception)
        {
            Check(ReferenceEquals(sentinel, exception),
                "exact exception propagates at " + expected[targetIndex]);
        }

        Sequence(expected.Take(targetIndex + 1).ToArray(), events,
            "exception stops after " + expected[targetIndex]);
    }
}

static void CheckConcreteActorBytes()
{
    foreach (var nameBytes in new[]
             {
                 new byte[] { 0x81 },
                 Enumerable.Range(0, 14)
                     .Select(index => unchecked((byte)(0x80 + index)))
                     .ToArray()
             })
    {
        var materialization = CreateMaterialization(nameBytes: nameBytes);
        var actor = Enumerable.Repeat((byte)0xCC,
                TFieldHero.OriginalInstanceSize)
            .ToArray();
        for (var index = 0;
             index < NativeFieldHeroFillCore.AbilityBlockLength;
             index++)
        {
            actor[NativeFieldHeroFillCore.BaseAbilityOffset + index] =
                unchecked((byte)(index * 29 + 7));
        }

        NativeFieldHeroFillCore.Fill(materialization,
            (offset, value, capacity) =>
            {
                var length = Math.Min(value.Length, capacity);
                actor[offset] = checked((byte)length);
                value.Span[..length].CopyTo(
                    actor.AsSpan(offset + 1, length));
            },
            (offset, value) => actor[offset] = value,
            (offset, value) => BinaryPrimitives.WriteUInt16LittleEndian(
                actor.AsSpan(offset, 2), value),
            (offset, value) => BinaryPrimitives.WriteInt32LittleEndian(
                actor.AsSpan(offset, 4), value),
            (source, destination, length) =>
                actor.AsSpan(source, length).CopyTo(
                    actor.AsSpan(destination, length)),
            _ =>
            {
                Check(actor.AsSpan(NativeFieldHeroFillCore.BaseAbilityOffset,
                        NativeFieldHeroFillCore.AbilityBlockLength)
                    .SequenceEqual(actor.AsSpan(
                        NativeFieldHeroFillCore.WorkingAbilityOffset,
                        NativeFieldHeroFillCore.AbilityBlockLength)),
                    "ability copy completes before equipment Fill");
            },
            (_, _) => { });

        Equal((byte)nameBytes.Length,
            actor[NativeFieldHeroFillCore.NameOffset],
            $"{nameBytes.Length}-byte ShortString length");
        Sequence(nameBytes,
            actor.AsSpan(NativeFieldHeroFillCore.NameOffset + 1,
                nameBytes.Length).ToArray(),
            $"{nameBytes.Length}-byte raw ShortString payload");
        Equal((byte)0xCC,
            actor[NativeFieldHeroFillCore.NameOffset + 1 +
                  nameBytes.Length],
            $"{nameBytes.Length}-byte ShortString does not clear tail");
        Equal((ushort)0xBEEF,
            BinaryPrimitives.ReadUInt16LittleEndian(actor.AsSpan(0x278, 2)),
            "Level write is mirrored by the later ability copy");
        Equal(0x76543210,
            BinaryPrimitives.ReadInt32LittleEndian(actor.AsSpan(0x2BC, 4)),
            "Experience write is mirrored by the later ability copy");
    }
}

static void CheckWireRuntimeSlotIsEvidenceOnly()
{
    foreach (var wireRuntimeSlot in new[] { 0u, 0xDEADBEEFu })
    {
        foreach (var includeDrop in new[] { false, true })
        {
            var materialization = CreateMaterialization(wireRuntimeSlot,
                includeDrop: includeDrop);
            IReadOnlyList<NativeFieldHeroRuntimeDropBinding> observed = null;
            NativeFieldHeroFillCore.Fill(materialization,
                (_, _, _) => { }, (_, _) => { }, (_, _) => { },
                (_, _) => { }, (_, _, _) => { }, _ => { },
                (_, drops) => observed = drops);
            Check(ReferenceEquals(materialization.DropItems, observed),
                $"wire runtime slot {wireRuntimeSlot:X8} uses managed binding");
            Equal(includeDrop ? 1 : 0, observed.Count,
                $"wire runtime slot {wireRuntimeSlot:X8} drop count");
        }
    }
}

static void CheckActorDropBindingGate()
{
    var materialization = CreateMaterialization();
    var foreign = CreateMaterialization();
    var actor = (TFieldHero)RuntimeHelpers.GetUninitializedObject(
        typeof(TFieldWarHero));
    var materializationField = typeof(TFieldHero).GetField(
        "_materialization", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException("_materialization");
    var boundField = typeof(TFieldHero).GetField(
        "_nativeBoundDropItems",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException("_nativeBoundDropItems");
    materializationField.SetValue(actor, materialization);
    boundField.SetValue(actor,
        Array.Empty<NativeFieldHeroRuntimeDropBinding>());

    Equal(0, actor.NativeDropItems.Count,
        "actor drop table remains unbound before Fill tail");
    var bindMethod = typeof(TFieldHero).GetMethod(
        "BindNativeDropItemsFromFill",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("BindNativeDropItemsFromFill");
    try
    {
        bindMethod.Invoke(actor, new object[] { foreign.DropItems });
        throw new Exception("foreign drop generation was accepted");
    }
    catch (TargetInvocationException exception)
        when (exception.InnerException is InvalidOperationException)
    {
    }
    Equal(0, actor.NativeDropItems.Count,
        "foreign generation rejection preserves unbound state");

    bindMethod.Invoke(actor, new object[] { materialization.DropItems });
    Check(ReferenceEquals(materialization.DropItems, actor.NativeDropItems),
        "Fill tail binds the exact captured-generation drop collection");
}

static void CheckEntryContracts()
{
    var materialization = CreateMaterialization();
    var calls = 0;
    Action<int, ReadOnlyMemory<byte>, int> name = (_, _, _) => calls++;
    Action<int, byte> writeByte = (_, _) => calls++;
    Action<int, ushort> writeWord = (_, _) => calls++;
    Action<int, int> writeInt = (_, _) => calls++;
    Action<int, int, int> copy = (_, _, _) => calls++;
    Action<IReadOnlyList<NativeType2FieldHeroRuntimeEquipmentBinding>>
        equipment = _ => calls++;
    Action<int, IReadOnlyList<NativeFieldHeroRuntimeDropBinding>> drops =
        (_, _) => calls++;

    ExpectThrows<ArgumentNullException>(() => NativeFieldHeroFillCore.Fill(
        null, name, writeByte, writeWord, writeInt, copy, equipment, drops),
        "null materialization");
    ExpectThrows<ArgumentNullException>(() => NativeFieldHeroFillCore.Fill(
        materialization, null, writeByte, writeWord, writeInt, copy,
        equipment, drops), "null name writer");
    ExpectThrows<ArgumentNullException>(() => NativeFieldHeroFillCore.Fill(
        materialization, name, null, writeWord, writeInt, copy, equipment,
        drops), "null byte writer");
    ExpectThrows<ArgumentNullException>(() => NativeFieldHeroFillCore.Fill(
        materialization, name, writeByte, null, writeInt, copy, equipment,
        drops), "null word writer");
    ExpectThrows<ArgumentNullException>(() => NativeFieldHeroFillCore.Fill(
        materialization, name, writeByte, writeWord, null, copy, equipment,
        drops), "null int writer");
    ExpectThrows<ArgumentNullException>(() => NativeFieldHeroFillCore.Fill(
        materialization, name, writeByte, writeWord, writeInt, null,
        equipment, drops), "null copy callback");
    ExpectThrows<ArgumentNullException>(() => NativeFieldHeroFillCore.Fill(
        materialization, name, writeByte, writeWord, writeInt, copy, null,
        drops), "null equipment callback");
    ExpectThrows<ArgumentNullException>(() => NativeFieldHeroFillCore.Fill(
        materialization, name, writeByte, writeWord, writeInt, copy,
        equipment, null), "null drop callback");
    Equal(0, calls, "all entry guards run before actor mutation");
}

static void CheckStaticContracts()
{
    Equal(0x0060B154u, NativeFieldHeroFillCore.OriginalFunction,
        "native function address");
    Equal(0x106, NativeFieldHeroFillCore.NameOffset, "name offset");
    Equal(0x0E, NativeFieldHeroFillCore.NameCapacity, "name capacity");
    Equal(0x71, NativeFieldHeroFillCore.SexOffset, "sex offset");
    Equal(0x686, NativeFieldHeroFillCore.BossLevelOffset,
        "boss-level offset");
    Equal(0x688, NativeFieldHeroFillCore.DrinkDrugOffset,
        "drink-drug offset");
    Equal(0x1FC, NativeFieldHeroFillCore.LevelOffset, "level offset");
    Equal(0x685, NativeFieldHeroFillCore.BodyLuckOffset,
        "body-luck offset");
    Equal(0x684, NativeFieldHeroFillCore.AddHitPointOffset,
        "add-hit-point offset");
    Equal(0x240, NativeFieldHeroFillCore.ExperienceOffset,
        "experience offset");
    Equal(0x1E8, NativeFieldHeroFillCore.BaseAbilityOffset,
        "base ability offset");
    Equal(0x264, NativeFieldHeroFillCore.WorkingAbilityOffset,
        "working ability offset");
    Equal(0x7C, NativeFieldHeroFillCore.AbilityBlockLength,
        "ability block length");
    Equal(0x474, NativeFieldHeroFillCore.RuntimeDropBindingOffset,
        "runtime drop-binding offset");
    Check(!TFieldHero.ProductionReady,
        "Fill core must not open FieldHero production");
}

static NativeType2FieldHeroMaterialization CreateMaterialization(
    uint wireRuntimeSlot = 0xDEADBEEF, byte[] nameBytes = null,
    bool includeDrop = true)
{
    var body = new byte[NativeType2FieldHeroSnapshotState.BodySize];
    var name = nameBytes ?? new byte[] { 0x41, 0x81, 0x5A, 0x42 };
    body[0] = checked((byte)name.Length);
    name.CopyTo(body, 1);
    body[0x0F] = 0x7D;
    body[0x10] = 0x6C;
    body[0x11] = 0x5B;
    BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(0x12, 2), 0xBEEF);
    body[0x14] = 0xA3;
    body[0x15] = 0xD4;
    body[0x16] = 0xE5;
    body[0x17] = 0xF6;
    BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(0x18, 4),
        0x89ABCDEF);
    BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(0x1C, 4),
        0x76543210);
    BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(0x138, 4),
        wireRuntimeSlot);
    var definition = NativeType2FieldHeroDefinition.Parse(body);

    var equipmentConstructor = typeof(
            NativeType2FieldHeroRuntimeEquipmentBinding)
        .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[]
            {
                typeof(NativeType2FieldHeroEquipmentDefinition),
                typeof(GoodItem)
            }, null)
        ?? throw new MissingMethodException("equipment binding ctor");
    var equipment = definition.Equipment.Select(slot =>
            (NativeType2FieldHeroRuntimeEquipmentBinding)
            equipmentConstructor.Invoke(new object[] { slot, null }))
        .ToArray();

    var item = new GoodItem { Name = "DropItem", NativeWireIndex = 17 };
    var dropConstructor = typeof(NativeFieldHeroRuntimeDropBinding)
        .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[]
            {
                typeof(byte[]), typeof(int), typeof(int), typeof(GoodItem),
                typeof(int)
            }, null)
        ?? throw new MissingMethodException("drop binding ctor");
    var drop = (NativeFieldHeroRuntimeDropBinding)dropConstructor.Invoke(
        new object[] { new byte[] { 0x44 }, 2, 7, item, 3 });

    var runtimeConstructor = typeof(NativeType2FieldHeroRuntimeDefinition)
        .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[]
            {
                typeof(object), typeof(long),
                typeof(NativeType2FieldHeroDefinition),
                typeof(NativeType2FieldHeroRuntimeEquipmentBinding[]),
                typeof(NativeFieldHeroRuntimeDropBinding[])
            }, null)
        ?? throw new MissingMethodException("runtime definition ctor");
    var drops = includeDrop
        ? new[] { drop }
        : Array.Empty<NativeFieldHeroRuntimeDropBinding>();
    var runtime = (NativeType2FieldHeroRuntimeDefinition)
        runtimeConstructor.Invoke(new object[]
        {
            new object(), 91L, definition, equipment, drops
        });

    var materializationConstructor = typeof(
            NativeType2FieldHeroMaterialization)
        .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[]
            {
                typeof(NativeType2FieldHeroRuntimeDefinition),
                typeof(ReadOnlyCollection<
                    NativeType2FieldHeroRuntimeEquipmentBinding>),
                typeof(ReadOnlyCollection<NativeFieldHeroRuntimeDropBinding>)
            }, null)
        ?? throw new MissingMethodException("materialization ctor");
    return (NativeType2FieldHeroMaterialization)
        materializationConstructor.Invoke(new object[]
        {
            runtime, Array.AsReadOnly(equipment),
            Array.AsReadOnly(drops)
        });
}

static void Sequence<T>(IReadOnlyList<T> expected,
    IReadOnlyList<T> actual, string label)
{
    Equal(expected.Count, actual.Count, label + " count");
    for (var index = 0; index < expected.Count; index++)
    {
        Equal(expected[index], actual[index], label + $"[{index}]");
    }
}

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"{label}: expected={expected}, actual={actual}");
}

static void Check(bool condition, string label)
{
    if (!condition) throw new Exception(label);
}

static void ExpectThrows<TException>(Action action, string label)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }
    throw new Exception(label);
}
