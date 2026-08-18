using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using GameSvr;
using GameSvr.Services;
using SystemModule;

CheckOrderedInitializationAndAttach();
CheckFailuresLogAndContinue();
CheckEntryContracts();

Check(!TFieldHero.ProductionReady,
    "Fill equipment core must not open FieldHero production");
Console.WriteLine("PASS NativeFieldHeroFillEquipmentCoreCheck " +
                  "slots=14 order=create-type-init-attach " +
                  "failure=log-and-continue production=NO-GO");

static void CheckOrderedInitializationAndAttach()
{
    var standards = Enumerable.Range(0,
            NativeType2FieldHeroDefinition.EquipmentSlotCount)
        .Select(index => Equip($"Equip{index:D2}"))
        .ToArray();
    var slots = standards.Select((standardItem, index) =>
            new Slot(index, standardItem.Name, ScatterFor(index),
                standardItem))
        .ToArray();
    var bindings = CreateBindings(slots);
    var container = CreateContainer();
    var replaced = new TUserItem { MakeIndex = -1 };
    Check(container.AttachFromFill(2, replaced, 81),
        "pre-existing slot fixture");

    var events = new List<string>();
    var itemStandards = new Dictionary<TUserItem, GoodItem>();
    var expectedStandards = standards.ToDictionary(item => item.Name);
    var expectedSlots = standards.Select((item, index) =>
            new { item.Name, Index = index })
        .ToDictionary(pair => pair.Name, pair => pair.Index);
    var created = new Dictionary<string, TUserItem>();
    var nextMakeIndex = 100;
    var pendingRefreshSlot = -1;
    TUserItem pendingRefreshItem = null;

    TUserItem Create(GoodItem standardItem)
    {
        Check(ReferenceEquals(expectedStandards[standardItem.Name],
                standardItem),
            "creator receives the direct manager-owned GoodItem");
        events.Add("create:" + standardItem.Name);
        var item = new TUserItem { MakeIndex = nextMakeIndex++ };
        itemStandards[item] = standardItem;
        created[standardItem.Name] = item;
        return item;
    }

    GoodItem Resolve(TUserItem item)
    {
        if (pendingRefreshSlot >= 0)
        {
            Check(ReferenceEquals(pendingRefreshItem,
                    container.Get(pendingRefreshSlot)),
                $"slot {pendingRefreshSlot} is attached before feature resolve");
            Equal(ScatterFor(pendingRefreshSlot),
                container.GetScatter(pendingRefreshSlot),
                $"slot {pendingRefreshSlot} scatter is written before feature resolve");
            pendingRefreshSlot = -1;
            pendingRefreshItem = null;
        }
        var standardItem = itemStandards[item];
        var slot = expectedSlots[standardItem.Name];
        if (ReferenceEquals(item, container.Get(slot)))
        {
            Equal(ScatterFor(slot), container.GetScatter(slot),
                $"slot {slot} scatter is written before feature resolve");
        }
        events.Add("resolve:" + standardItem.Name);
        return standardItem;
    }

    bool IsEquipment(TUserItem item, GoodItem standardItem)
    {
        Check(ReferenceEquals(itemStandards[item], standardItem),
            "type gate receives the created item's GoodItem binding");
        events.Add("type:" + standardItem.Name);
        return standardItem.StdMode == 5;
    }

    void Initialize(TUserItem item, GoodItem standardItem)
    {
        events.Add("init:" + standardItem.Name);
        var slot = expectedSlots[standardItem.Name];
        Check(!ReferenceEquals(item, container.Get(slot)),
            "DL=1 initializer runs before attach");
        Equal(-1, pendingRefreshSlot,
            "the preceding success reached feature resolve");
        pendingRefreshSlot = slot;
        pendingRefreshItem = item;
        item.Dura = 77;
    }

    var logs = new List<string>();
    var storedFeatures = new List<uint>();
    var notifications = 0;
    void StoreFeature(uint feature)
    {
        events.Add("store");
        storedFeatures.Add(feature);
    }
    void NotifyFeature()
    {
        events.Add("notify");
        notifications++;
    }
    NativeFieldHeroFillEquipmentCore.Fill(bindings, container, 1, 3,
        Create, Resolve, IsEquipment, Initialize, StoreFeature,
        NotifyFeature, logs.Add);

    var expectedEvents = new List<string>();
    for (var slot = 0; slot < standards.Length; slot++)
    {
        var item = standards[slot];
        expectedEvents.Add("create:" + item.Name);
        expectedEvents.Add("resolve:" + item.Name);
        expectedEvents.Add("type:" + item.Name);
        expectedEvents.Add("init:" + item.Name);
        expectedEvents.Add("resolve:Equip00");
        if (slot >= 1) expectedEvents.Add("resolve:Equip01");
        if (slot >= 13) expectedEvents.Add("resolve:Equip13");
        else if (slot >= 4) expectedEvents.Add("resolve:Equip04");
        expectedEvents.Add("store");
        expectedEvents.Add("notify");
    }
    Sequence(expectedEvents, events, "native 0..13 slot-order call sequence");
    Equal(-1, pendingRefreshSlot,
        "the final success reached feature resolve");
    Equal(0, logs.Count, "successful Fill emits no failure log");
    Equal(14, storedFeatures.Count,
        "each successful slot rebuilds and stores feature once");
    Equal(14, notifications,
        "each successful slot dispatches VMT+0x68 once");
    for (var slot = 0; slot < standards.Length; slot++)
    {
        var name = standards[slot].Name;
        Check(ReferenceEquals(created[name], container.Get(slot)),
            $"slot {slot} stores its exact created item");
        Equal(ScatterFor(slot), container.GetScatter(slot),
            $"slot {slot} stores its exact scatter");
    }
    Check(!ReferenceEquals(replaced, container.Get(2)),
        "occupied slot is replaced without rollback");
}

static void CheckFailuresLogAndContinue()
{
    var nullCreate = Equip("NullCreate");
    var nullStandard = Equip("NullStd");
    var nonEquipment = new GoodItem
    {
        Name = "NonEquip",
        StdMode = 0,
        DuraMax = 100
    };
    var good = Equip("LaterGood");
    var bindings = CreateBindings(
        new Slot(0, "Missing", 10, null),
        new Slot(1, "NullCreate", 11, nullCreate),
        new Slot(2, "NullStd", 12, nullStandard),
        new Slot(3, "NonEquip", 13, nonEquipment),
        new Slot(4, "LaterGood", 14, good));
    var container = CreateContainer();
    var events = new List<string>();
    var itemStandards = new Dictionary<TUserItem, GoodItem>();
    TUserItem laterItem = null;

    TUserItem Create(GoodItem standardItem)
    {
        events.Add("create:" + standardItem.Name);
        if (ReferenceEquals(standardItem, nullCreate)) return null;
        var item = new TUserItem();
        itemStandards[item] = standardItem;
        if (ReferenceEquals(standardItem, good)) laterItem = item;
        return item;
    }

    GoodItem Resolve(TUserItem item)
    {
        var standardItem = itemStandards[item];
        events.Add("resolve:" + standardItem.Name);
        return ReferenceEquals(standardItem, nullStandard)
            ? null
            : standardItem;
    }

    bool IsEquipment(TUserItem item, GoodItem standardItem)
    {
        events.Add("type:" + standardItem.Name);
        return standardItem.StdMode == 5;
    }

    void Initialize(TUserItem item, GoodItem standardItem)
    {
        events.Add("init:" + standardItem.Name);
    }

    var logs = new List<string>();
    var storedFeatures = 0;
    var notifications = 0;
    void StoreFeature(uint _)
    {
        events.Add("store");
        storedFeatures++;
    }
    void NotifyFeature()
    {
        events.Add("notify");
        notifications++;
    }
    NativeFieldHeroFillEquipmentCore.Fill(bindings, container, 1, 3,
        Create, Resolve, IsEquipment, Initialize, StoreFeature,
        NotifyFeature, logs.Add);

    Sequence(new[]
    {
        "create:NullCreate",
        "create:NullStd", "resolve:NullStd",
        "create:NonEquip", "resolve:NonEquip", "type:NonEquip",
        "create:LaterGood", "resolve:LaterGood", "type:LaterGood",
        "init:LaterGood", "resolve:LaterGood", "store", "notify"
    }, events, "each failed slot stops locally and later slots continue");
    Sequence(new[]
    {
        MissingLog("Missing"),
        MissingLog("NullCreate"),
        MissingLog("NullStd"),
        MissingLog("NonEquip")
    }, logs, "each non-empty failed slot logs exactly once");
    for (var slot = 0; slot < 4; slot++)
    {
        Check(container.Get(slot) == null,
            "failed slot is not attached: " + slot);
        Equal(0, container.GetScatter(slot),
            "failed slot does not write scatter: " + slot);
    }
    Check(ReferenceEquals(laterItem, container.Get(4)),
        "success after four failures still attaches");
    Equal(14, container.GetScatter(4),
        "later success stores its scatter");
    Equal(1, storedFeatures,
        "only the later successful slot stores feature");
    Equal(1, notifications,
        "only the later successful slot notifies feature change");
}

static void CheckEntryContracts()
{
    var bindings = CreateBindings();
    var container = CreateContainer();
    var calls = 0;
    TUserItem Create(GoodItem _) { calls++; return new TUserItem(); }
    GoodItem Resolve(TUserItem _) { calls++; return new GoodItem(); }
    bool Type(TUserItem _, GoodItem __) { calls++; return true; }
    void Initialize(TUserItem _, GoodItem __) { calls++; }
    void Store(uint _) { calls++; }
    void Notify() { calls++; }
    void Log(string _) { calls++; }

    ExpectThrows<InvalidDataException>(() =>
        NativeFieldHeroFillEquipmentCore.Fill(bindings.Take(13).ToArray(),
            container, 0, 0, Create, Resolve, Type, Initialize, Store,
            Notify, Log),
        "non-14-slot input fails closed");
    ExpectThrows<ArgumentNullException>(() =>
        NativeFieldHeroFillEquipmentCore.Fill(null, container, 0, 0,
            Create, Resolve, Type, Initialize, Store, Notify, Log),
        "null equipment input fails closed");
    Equal(0, calls, "entry guards run before any callback");
}

static IReadOnlyList<NativeType2FieldHeroRuntimeEquipmentBinding>
    CreateBindings(params Slot[] slots)
{
    var body = new byte[NativeType2FieldHeroSnapshotState.BodySize];
    WriteShortString(body, 0, "FillAuditHero");
    var bySlot = slots.ToDictionary(x => x.Index);
    foreach (var slot in slots)
    {
        var offset = NativeType2FieldHeroDefinition.EquipmentOffset
                     + slot.Index *
                     NativeType2FieldHeroDefinition.EquipmentStride;
        WriteShortString(body, offset, slot.Name);
        BinaryPrimitives.WriteInt32LittleEndian(
            body.AsSpan(offset + 0x10, sizeof(int)), slot.Scatter);
    }

    var definition = NativeType2FieldHeroDefinition.Parse(body);
    var constructor = typeof(NativeType2FieldHeroRuntimeEquipmentBinding)
        .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
            new[]
            {
                typeof(NativeType2FieldHeroEquipmentDefinition),
                typeof(GoodItem)
            }, null)
        ?? throw new MissingMethodException("runtime equipment binding ctor");
    var result = new NativeType2FieldHeroRuntimeEquipmentBinding[
        NativeType2FieldHeroDefinition.EquipmentSlotCount];
    for (var index = 0; index < result.Length; index++)
    {
        bySlot.TryGetValue(index, out var slot);
        result[index] =
            (NativeType2FieldHeroRuntimeEquipmentBinding)constructor.Invoke(
                new object[] { definition.Equipment[index], slot?.Item });
    }
    return result;
}

static NativeFieldHeroEquipmentContainer CreateContainer()
{
    var actor = (TFieldHero)RuntimeHelpers.GetUninitializedObject(
        typeof(TFieldWarHero));
    return Activator.CreateInstance(
               typeof(NativeFieldHeroEquipmentContainer),
               BindingFlags.Instance | BindingFlags.NonPublic, null,
               new object[] { actor }, null)
           as NativeFieldHeroEquipmentContainer
           ?? throw new InvalidOperationException("container construction");
}

static GoodItem Equip(string name) => new()
{
    Name = name,
    StdMode = 5,
    DuraMax = 1000
};

static string MissingLog(string name) =>
    " [Error]: TFieldHero.FillDBData: "
    + name
    + "\u4E0D\u5B58\u5728\uFF01";

static int ScatterFor(int slot) => slot switch
{
    0 => int.MinValue,
    13 => int.MaxValue,
    _ => slot * 101 - 500
};

static void WriteShortString(byte[] destination, int offset, string value)
{
    var bytes = Encoding.ASCII.GetBytes(value);
    destination[offset] = checked((byte)bytes.Length);
    bytes.CopyTo(destination, offset + 1);
}

static void Check(bool value, string description)
{
    if (!value) throw new InvalidOperationException(description);
}

static void Equal<T>(T expected, T actual, string description)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            $"{description}: expected={expected}, actual={actual}");
    }
}

static void Sequence<T>(IReadOnlyList<T> expected,
    IReadOnlyList<T> actual, string description)
{
    if (!expected.SequenceEqual(actual))
    {
        throw new InvalidOperationException(
            $"{description}: expected=[{string.Join(",", expected)}], " +
            $"actual=[{string.Join(",", actual)}]");
    }
}

static void ExpectThrows<TException>(Action action, string description)
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
    throw new InvalidOperationException(description);
}

sealed class Slot
{
    public Slot(int index, string name, int scatter, GoodItem item)
    {
        Index = index;
        Name = name;
        Scatter = scatter;
        Item = item;
    }

    public int Index { get; }
    public string Name { get; }
    public int Scatter { get; }
    public GoodItem Item { get; }
}
