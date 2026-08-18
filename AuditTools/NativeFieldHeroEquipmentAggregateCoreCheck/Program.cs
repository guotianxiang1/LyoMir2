using System.Reflection;
using System.Runtime.CompilerServices;
using GameSvr;
using SystemModule;

CheckFullSixteenSlotOrder();
CheckNullAndZeroDurabilityGates();
CheckTailEqualityGates();
CheckEveryCallbackExceptionShortCircuits();
CheckStaticContracts();

Console.WriteLine("PASS NativeFieldHeroEquipmentAggregateCoreCheck " +
                  "slots=16 dura=nonzero order=exact tail=byte7 " +
                  "production=NO-GO");

static void CheckFullSixteenSlotOrder()
{
    var container = CreateContainer();
    var items = Enumerable.Range(0,
            NativeFieldHeroEquipmentContainer.SlotCount)
        .Select(slot => new TUserItem { MakeIndex = 1000 + slot })
        .ToArray();
    for (var slot = 0; slot < items.Length; slot++)
    {
        Check(container.Attach(slot, items[slot]), "attach slot " + slot);
    }

    var byItem = items.Select((item, slot) => new { item, slot })
        .ToDictionary(pair => pair.item, pair => pair.slot);
    var events = new List<string>();
    var tailDword = int.MaxValue - 20;
    ushort tailWord = ushort.MaxValue;

    NativeFieldHeroEquipmentAggregateCore.Recalculate(
        container,
        () => events.Add("reset"),
        item =>
        {
            var slot = byItem[item];
            events.Add("dura:" + slot);
            if (slot == 0) return 0;
            return slot == 15 ? ushort.MaxValue : (ushort)(slot + 1);
        },
        (slot, item) =>
        {
            Check(ReferenceEquals(items[slot], item),
                "direct item identity at slot " + slot);
            events.Add("apply:" + slot);
        },
        () => events.Add("sets"),
        () => events.Add("secondary"),
        offset =>
        {
            events.Add("read:" + offset.ToString("X"));
            return 7;
        },
        (offset, delta) =>
        {
            events.Add($"add32:{offset:X}:{delta}");
            Equal(0x98, offset, "tail dword offset");
            tailDword = unchecked(tailDword + delta);
        },
        (offset, delta) =>
        {
            events.Add($"add16:{offset:X}:{delta}");
            Equal(0x50, offset, "tail word offset");
            tailWord = unchecked((ushort)(tailWord + delta));
        });

    var expected = new List<string> { "reset" };
    for (var slot = 0; slot < items.Length; slot++)
    {
        expected.Add("dura:" + slot);
        if (slot != 0) expected.Add("apply:" + slot);
    }
    expected.AddRange(new[]
    {
        "sets", "secondary", "read:1F9", "add32:98:50",
        "read:1FA", "add16:50:2"
    });
    SequenceEqual(expected, events, "full aggregate order");
    Equal(unchecked(int.MaxValue - 20 + 50), tailDword,
        "tail dword callback preserves unchecked add boundary");
    Equal((ushort)1, tailWord,
        "tail word callback preserves unchecked add boundary");
}

static void CheckNullAndZeroDurabilityGates()
{
    var container = CreateContainer();
    var zero = new TUserItem { MakeIndex = 3 };
    var live = new TUserItem { MakeIndex = 9 };
    Check(container.Attach(3, zero), "attach zero-dura fixture");
    Check(container.Attach(9, live), "attach live fixture");
    var events = new List<string>();

    NativeFieldHeroEquipmentAggregateCore.Recalculate(
        container,
        () => events.Add("reset"),
        item =>
        {
            if (ReferenceEquals(item, zero))
            {
                events.Add("dura:3");
                return 0;
            }
            Check(ReferenceEquals(item, live), "only attached items read dura");
            events.Add("dura:9");
            return 1;
        },
        (slot, item) =>
        {
            Equal(9, slot, "only positive-dura slot applied");
            Check(ReferenceEquals(live, item), "positive-dura direct identity");
            events.Add("apply:9");
        },
        () => events.Add("sets"),
        () => events.Add("secondary"),
        offset =>
        {
            events.Add("read:" + offset.ToString("X"));
            return 0;
        },
        (_, _) => throw new Exception("unexpected dword tail add"),
        (_, _) => throw new Exception("unexpected word tail add"));

    SequenceEqual(new[]
    {
        "reset", "dura:3", "dura:9", "apply:9", "sets", "secondary",
        "read:1F9", "read:1FA"
    }, events, "null and zero-durability gates");
}

static void CheckTailEqualityGates()
{
    foreach (var first in new byte[] { 6, 7, 8, byte.MaxValue })
    foreach (var second in new byte[] { 6, 7, 8, byte.MaxValue })
    {
        var events = new List<string>();
        NativeFieldHeroEquipmentAggregateCore.Recalculate(
            CreateContainer(),
            () => events.Add("reset"),
            _ => throw new Exception("empty container must not read dura"),
            (_, _) => throw new Exception("empty container must not apply"),
            () => events.Add("sets"),
            () => events.Add("secondary"),
            offset =>
            {
                events.Add("read:" + offset.ToString("X"));
                return offset == 0x1F9 ? first : second;
            },
            (offset, delta) => events.Add($"add32:{offset:X}:{delta}"),
            (offset, delta) => events.Add($"add16:{offset:X}:{delta}"));

        var expected = new List<string>
        {
            "reset", "sets", "secondary", "read:1F9"
        };
        if (first == 7) expected.Add("add32:98:50");
        expected.Add("read:1FA");
        if (second == 7) expected.Add("add16:50:2");
        SequenceEqual(expected, events,
            $"tail equality first={first} second={second}");
    }
}

static void CheckEveryCallbackExceptionShortCircuits()
{
    var allEvents = new[]
    {
        "reset", "dura", "apply", "sets", "secondary", "read:1F9",
        "add32:98:50", "read:1FA", "add16:50:2"
    };
    var sentinel = new ApplicationException("sentinel");
    foreach (var targetIndex in Enumerable.Range(0, allEvents.Length))
    {
        var events = new List<string>();
        void Hit(string current)
        {
            events.Add(current);
            if (current == allEvents[targetIndex]) throw sentinel;
        }

        var container = CreateContainer();
        Check(container.Attach(0, new TUserItem { MakeIndex = 1 }),
            "exception fixture attach");
        try
        {
            NativeFieldHeroEquipmentAggregateCore.Recalculate(
                container,
                () => Hit("reset"),
                _ =>
                {
                    Hit("dura");
                    return 1;
                },
                (_, _) => Hit("apply"),
                () => Hit("sets"),
                () => Hit("secondary"),
                offset =>
                {
                    Hit("read:" + offset.ToString("X"));
                    return 7;
                },
                (offset, delta) => Hit($"add32:{offset:X}:{delta}"),
                (offset, delta) => Hit($"add16:{offset:X}:{delta}"));
            throw new Exception("callback exception swallowed at " +
                                allEvents[targetIndex]);
        }
        catch (ApplicationException ex)
        {
            Check(ReferenceEquals(sentinel, ex),
                "exact callback exception at " + allEvents[targetIndex]);
        }

        SequenceEqual(allEvents.Take(targetIndex + 1).ToArray(), events,
            "exception prefix " + allEvents[targetIndex]);
    }
}

static void CheckStaticContracts()
{
    Equal(0x0075F4F8u, NativeFieldHeroEquipmentAggregateCore.ResetFunction,
        "reset function");
    Equal(0x007845A0u,
        NativeFieldHeroEquipmentAggregateCore.ItemDurabilityFunction,
        "item durability function");
    Equal(0x0075EE04u,
        NativeFieldHeroEquipmentAggregateCore.ApplyItemFunction,
        "apply-item function");
    Equal(0x0075F548u,
        NativeFieldHeroEquipmentAggregateCore.SetBonusFunction,
        "set-bonus function");
    Equal(0x00758AC0u,
        NativeFieldHeroEquipmentAggregateCore.SecondaryRebuildFunction,
        "secondary rebuild function");
    Equal(0x48, NativeFieldHeroEquipmentAggregateCore.AggregateBlockOffset,
        "aggregate block offset");
    Equal(0x50, NativeFieldHeroEquipmentAggregateCore.TailWordOffset,
        "tail word offset");
    Equal(0x98, NativeFieldHeroEquipmentAggregateCore.TailDwordOffset,
        "tail dword offset");
    Equal(0x1F8, NativeFieldHeroEquipmentAggregateCore.SecondaryBlockOffset,
        "secondary block offset");
    Equal(0x1F9,
        NativeFieldHeroEquipmentAggregateCore.SecondaryFirstGateOffset,
        "secondary first gate offset");
    Equal(0x1FA,
        NativeFieldHeroEquipmentAggregateCore.SecondarySecondGateOffset,
        "secondary second gate offset");
    Check(!TFieldHero.ProductionReady,
        "aggregate core must not open FieldHero production");
}

static NativeFieldHeroEquipmentContainer CreateContainer()
{
    var actor = (TFieldHero)RuntimeHelpers.GetUninitializedObject(
        typeof(TFieldWarHero));
    return Activator.CreateInstance(
        typeof(NativeFieldHeroEquipmentContainer),
        BindingFlags.Instance | BindingFlags.NonPublic, null,
        new object[] { actor }, null) as NativeFieldHeroEquipmentContainer
        ?? throw new InvalidOperationException("could not construct container");
}

static void SequenceEqual(IReadOnlyList<string> expected,
    IReadOnlyList<string> actual, string label)
{
    Equal(expected.Count, actual.Count, label + " count");
    for (var i = 0; i < expected.Count; i++)
    {
        Equal(expected[i], actual[i], label + $"[{i}]");
    }
}

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new Exception($"{label}: expected {expected}, got {actual}");
    }
}

static void Check(bool condition, string label)
{
    if (!condition) throw new Exception(label);
}
