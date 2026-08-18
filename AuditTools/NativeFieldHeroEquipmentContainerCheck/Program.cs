using System.Reflection;
using System.Runtime.CompilerServices;
using GameSvr;
using SystemModule;

var actor = (TFieldHero)RuntimeHelpers.GetUninitializedObject(
    typeof(TFieldWarHero));
var container = Activator.CreateInstance(
    typeof(NativeFieldHeroEquipmentContainer),
    BindingFlags.Instance | BindingFlags.NonPublic, null,
    new object[] { actor }, null) as NativeFieldHeroEquipmentContainer
    ?? throw new InvalidOperationException("could not construct container");
CheckActorContractSurface();
CheckOwnershipAndSlotSemantics(actor, container);
CheckFillRefreshSequence(container);
CheckPackedFeature(container);
CheckFeatureGuards(container);

Check(!TFieldHero.ProductionReady,
    "container core does not open FieldHero production");
Console.WriteLine("PASS NativeFieldHeroEquipmentContainerCheck " +
                  "slots=16 identity=direct scatter=independent " +
                  "feature=sub_75F374 production=NO-GO");

static void CheckActorContractSurface()
{
    var property = typeof(TFieldHero).GetProperty(
        nameof(TFieldHero.NativeOwnedEquipment));
    Check(property != null && property.PropertyType ==
          typeof(NativeFieldHeroEquipmentContainer),
        "FieldHero exposes the independent container type");
    Check(property.GetMethod?.IsPublic == true && property.SetMethod == null,
        "FieldHero container is publicly readable and not replaceable");
    var backingField = typeof(TFieldHero).GetField(
        "<NativeOwnedEquipment>k__BackingField",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Check(backingField != null && backingField.IsInitOnly,
        "FieldHero owns a readonly container backing field");
}

static void CheckOwnershipAndSlotSemantics(TFieldHero actor,
    NativeFieldHeroEquipmentContainer container)
{
    Check(container != null, "constructor creates actor+0x63C container");
    Check(ReferenceEquals(actor, container.Owner),
        "container retains its FieldHero owner");
    Equal(16, NativeFieldHeroEquipmentContainer.SlotCount,
        "native slot count");
    for (var slot = 0; slot < 16; slot++)
    {
        Check(container.Get(slot) == null, "initial slot " + slot);
        Equal(0, container.GetScatter(slot),
            "initial scatter " + slot);
    }

    Check(container.Get(-1) == null && container.Get(16) == null,
        "Get rejects both slot boundaries");
    Check(!container.Attach(-1, new TUserItem())
          && !container.Attach(16, new TUserItem()),
        "Attach rejects both slot boundaries");
    Check(!container.Attach(0, null), "Attach rejects null item");
    Check(container.Detach(-1) == null && container.Detach(16) == null,
        "Detach rejects both slot boundaries");
    ExpectThrows<ArgumentOutOfRangeException>(() =>
            container.GetScatter(-1),
        "scatter inspection rejects negative slot");
    ExpectThrows<ArgumentOutOfRangeException>(() =>
            container.GetScatter(16),
        "scatter inspection rejects upper boundary");

    var first = new TUserItem { MakeIndex = 11 };
    var replacement = new TUserItem { MakeIndex = 12 };
    Check(container.Attach(2, first), "first direct attach");
    Check(ReferenceEquals(first, container.Get(2)),
        "direct attach preserves object identity");
    Check(container.Attach(2, replacement),
        "occupied slot replacement has no occupancy gate");
    Check(ReferenceEquals(replacement, container.Get(2)),
        "replacement stores the new direct pointer");
    Check(ReferenceEquals(replacement, container.Detach(2)),
        "detach returns the exact old pointer");
    Check(container.Get(2) == null, "detach clears slot");

    var fillItem = new TUserItem { MakeIndex = 21 };
    Check(container.AttachFromFill(5, fillItem, 42),
        "valid Fill attach succeeds");
    Check(ReferenceEquals(fillItem, container.Get(5)),
        "Fill attach stores direct pointer");
    Equal(42, container.GetScatter(5), "Fill stores separate scatter");
    var fillReplacement = new TUserItem { MakeIndex = 22 };
    Check(container.Attach(5, fillReplacement),
        "plain attach replaces Fill item");
    Equal(42, container.GetScatter(5),
        "plain attach does not touch scatter");
    Check(ReferenceEquals(fillReplacement, container.Detach(5)),
        "detach returns Fill replacement");
    Equal(42, container.GetScatter(5),
        "detach does not clear actor scatter cell");
    var retainedAfterNullFill = new TUserItem { MakeIndex = 23 };
    Check(container.Attach(5, retainedAfterNullFill),
        "occupied null-Fill fixture");
    Check(!container.AttachFromFill(5, null, -7),
        "valid Fill null pointer fails attach");
    Check(ReferenceEquals(retainedAfterNullFill, container.Get(5)),
        "failed null Fill preserves the occupied slot pointer");
    Equal(-7, container.GetScatter(5),
        "valid Fill writes scatter after failed attach");
    Check(!container.AttachFromFill(16, fillItem, 99),
        "invalid Fill slot rejected");

    var baseItem = new TUserItem { MakeIndex = 31 };
    var nativeItem = new TUserItem { MakeIndex = 32 };
    actor.m_UseItems = new TUserItem[16];
    actor.m_UseItems[0] = baseItem;
    Check(container.Get(0) == null,
        "base m_UseItems write does not enter native container");
    Check(container.Attach(0, nativeItem),
        "native container slot zero attach");
    Check(ReferenceEquals(baseItem, actor.m_UseItems[0]),
        "native attach does not replace base m_UseItems");
    Check(ReferenceEquals(nativeItem, container.Get(0)),
        "native slot remains independent");
    container.Detach(0);
}

static void CheckPackedFeature(
    NativeFieldHeroEquipmentContainer container)
{
    var dressItem = new TUserItem { MakeIndex = 101 };
    var weaponItem = new TUserItem { MakeIndex = 102 };
    var slot4Item = new TUserItem { MakeIndex = 104 };
    var slot13Item = new TUserItem { MakeIndex = 113 };
    var dress = new GoodItem { Shape = 130 };
    var weapon = new GoodItem { Shape = 200 };
    var slot4 = new GoodItem { Outlook = 7 };
    var slot13 = new GoodItem { Outlook = 0 };
    var bindings = new Dictionary<TUserItem, GoodItem>
    {
        [dressItem] = dress,
        [weaponItem] = weapon,
        [slot4Item] = slot4,
        [slot13Item] = slot13
    };
    var resolved = new List<int>();
    GoodItem Resolve(TUserItem item)
    {
        resolved.Add(item.MakeIndex);
        return bindings[item];
    }

    Check(container.Attach(0, dressItem), "feature dress attach");
    Check(container.Attach(1, weaponItem), "feature weapon attach");
    Check(container.Attach(4, slot4Item), "feature slot4 attach");
    Equal(Pack(0xAB, 145, 15, 5),
        container.BuildPackedFeature(1, 3, 0xAB, Resolve),
        "slot4 head source and byte-overflow feature packing");
    Sequence(new[] { 101, 102, 104 }, resolved,
        "feature resolver order without slot13");

    resolved.Clear();
    Check(container.Attach(13, slot13Item), "feature slot13 attach");
    Equal(Pack(0xAB, 145, 7, 5),
        container.BuildPackedFeature(1, 3, 0xAB, Resolve),
        "slot13 Outlook zero falls back to actor hair, not slot4");
    Sequence(new[] { 101, 102, 113 }, resolved,
        "slot13 pointer suppresses slot4 resolution");

    resolved.Clear();
    slot13.Outlook = 200;
    Equal(Pack(0xAB, 145, 145, 5),
        container.BuildPackedFeature(1, 3, 0xAB, Resolve),
        "slot13 Outlook low-byte overflow");

    resolved.Clear();
    slot13.Outlook = 0x107;
    Equal(Pack(0xAB, 145, 15, 5),
        container.BuildPackedFeature(1, 3, 0xAB, Resolve),
        "slot13 Outlook discards bits above the low byte");

    resolved.Clear();
    slot13.Outlook = -1;
    dress.Shape = 200;
    Equal(Pack(0xAB, 145, 255, 145),
        container.BuildPackedFeature(1, 3, 0xAB, Resolve),
        "negative Outlook and high packed dress byte wrap unchecked");

    container.Detach(0);
    container.Detach(1);
    container.Detach(4);
    container.Detach(13);
    resolved.Clear();
    Equal(Pack(0, 1, 7, 1),
        container.BuildPackedFeature(1, 3, 0, Resolve),
        "empty equipment uses gender bytes and actor hair");
    Equal(0, resolved.Count, "empty feature resolves no standard item");
}

static void CheckFillRefreshSequence(
    NativeFieldHeroEquipmentContainer container)
{
    var oldItem = new TUserItem { MakeIndex = 601 };
    var oldStandard = new GoodItem { Shape = 2 };
    var standards = new Dictionary<TUserItem, GoodItem>
    {
        [oldItem] = oldStandard
    };
    Check(container.Attach(0, oldItem), "refresh fixture attach");
    var events = new List<string>();
    uint stored = uint.MaxValue;
    TUserItem expectedItem = oldItem;
    var expectedScatter = -33;

    GoodItem Resolve(TUserItem item)
    {
        events.Add("resolve");
        Check(ReferenceEquals(expectedItem, container.Get(0)),
            "attach is visible before feature resolve");
        Check(ReferenceEquals(expectedItem, item),
            "feature resolver receives the newly visible slot pointer");
        Equal(expectedScatter, container.GetScatter(0),
            "scatter is written before feature resolve");
        return standards[item];
    }

    Check(!container.AttachFromFillAndRefresh(0, null, -33, 1, 4,
            Resolve,
            value =>
            {
                events.Add("store");
                Check(ReferenceEquals(oldItem, container.Get(0)),
                    "failed attach keeps old pointer before feature store");
                Equal(-33, container.GetScatter(0),
                    "scatter is written before feature store");
                stored = value;
            },
            () =>
            {
                events.Add("notify");
                Check(stored != uint.MaxValue,
                    "feature is stored before VMT+0x68 notification");
            }),
        "null refresh attach returns false");
    Equal(Pack(0, 1, 9, 5), stored,
        "sub_6090E8 passes zero low byte to feature builder");
    Sequence(new[] { "resolve", "store", "notify" }, events,
        "attach-scatter-feature-store-notify tail order");

    var newItem = new TUserItem { MakeIndex = 602 };
    standards[newItem] = new GoodItem { Shape = 7 };
    expectedItem = newItem;
    expectedScatter = 44;
    events.Clear();
    stored = uint.MaxValue;
    Check(container.AttachFromFillAndRefresh(0, newItem, expectedScatter,
            1, 4, Resolve,
            value =>
            {
                events.Add("store");
                stored = value;
            },
            () =>
            {
                events.Add("notify");
                Check(stored != uint.MaxValue,
                    "successful feature store precedes notification");
            }),
        "non-null refresh attach returns true");
    Check(ReferenceEquals(newItem, container.Get(0)),
        "non-null refresh stores the new direct pointer");
    Equal(Pack(0, 1, 9, 15), stored,
        "successful refresh packs the replacement item");
    Sequence(new[] { "resolve", "store", "notify" }, events,
        "successful attach-scatter-feature-store-notify order");

    var invalidResolveCalls = 0;
    var invalidStoreCalls = 0;
    var invalidNotifyCalls = 0;
    Check(!container.AttachFromFillAndRefresh(16, oldItem, 0, 0, 0,
            _ =>
            {
                invalidResolveCalls++;
                return oldStandard;
            },
            _ => invalidStoreCalls++,
            () => invalidNotifyCalls++),
        "invalid logical slot fails before callbacks");
    Equal(0, invalidResolveCalls,
        "invalid logical slot skips feature resolver");
    Equal(0, invalidStoreCalls,
        "invalid logical slot skips feature store");
    Equal(0, invalidNotifyCalls,
        "invalid logical slot skips feature notification");
    container.Detach(0);
}

static void CheckFeatureGuards(
    NativeFieldHeroEquipmentContainer container)
{
    ExpectThrows<ArgumentNullException>(() =>
            container.BuildPackedFeature(0, 0, 0, null),
        "null standard-item resolver rejected");
    var unresolved = new TUserItem { MakeIndex = 500 };
    container.Attach(0, unresolved);
    ExpectThrows<InvalidDataException>(() =>
            container.BuildPackedFeature(0, 0, 0, _ => null),
        "attached item without GoodItem binding fails closed");
    container.Detach(0);
}

static uint Pack(byte low, byte weapon, byte head, byte dress) =>
    low | ((uint)weapon << 8) | ((uint)head << 16) | ((uint)dress << 24);

static void Sequence<T>(IReadOnlyList<T> expected,
    IReadOnlyList<T> actual, string description)
{
    Equal(expected.Count, actual.Count, description + " count");
    for (var index = 0; index < expected.Count; index++)
        Equal(expected[index], actual[index],
            description + " index " + index);
}

static void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
}

static void Equal<T>(T expected, T actual, string description)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{description}: expected={expected}, actual={actual}");
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
