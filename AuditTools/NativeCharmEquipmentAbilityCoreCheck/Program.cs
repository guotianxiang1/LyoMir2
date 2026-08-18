using GameSvr;
using System.Buffers.Binary;
using SystemModule;

CheckConstants();
CheckClassMatrix();
CheckOverwriteAndIsolation();
CheckWriteOrder();
CheckMarkStoneZeroDurability();
CheckMarkStoneScaledEndpoints();
CheckMarkStoneShapeBranches();
CheckMarkStoneWrappingAndRouting();
CheckArgumentGates();

Console.WriteLine("PASS NativeCharmEquipmentAbilityCoreCheck " +
                  "classes=6 flags=19,1A,1B overwrite=exact " +
                  "markstone=76443C scale=75F780 production=NO-GO");
return 0;

static void CheckConstants()
{
    Equal(0x0076332Cu, NativeCharmEquipmentAbilityCore.CharmFunction,
        "TCharm function");
    Equal(0x00763424u, NativeCharmEquipmentAbilityCore.CryCharmFunction,
        "TCryCharm function");
    Equal(0x00763440u, NativeCharmEquipmentAbilityCore.HpCharmFunction,
        "THPCharm function");
    Equal(0x007634C0u, NativeCharmEquipmentAbilityCore.MpCharmFunction,
        "TMPCharm function");
    Equal(0x0076353Cu, NativeCharmEquipmentAbilityCore.HpMpCharmFunction,
        "THPMPCharm function");
    Equal(0x0076443Cu, NativeCharmEquipmentAbilityCore.MarkStoneFunction,
        "TMarkStoneCharm function");
    Equal(0x0075F780u,
        NativeCharmEquipmentAbilityCore.MarkStoneScaleFunction,
        "TMarkStoneCharm scale function");
    Equal(0x1B0, NativeCharmEquipmentAbilityCore.PrimarySize,
        "primary size");
    Equal(0x36, NativeCharmEquipmentAbilityCore.SecondarySize,
        "secondary size");
    Equal(0x19, NativeCharmEquipmentAbilityCore.CryFlagOffset,
        "cry offset");
    Equal(0x1A, NativeCharmEquipmentAbilityCore.HpFlagOffset,
        "HP offset");
    Equal(0x1B, NativeCharmEquipmentAbilityCore.MpFlagOffset,
        "MP offset");
}

static void CheckClassMatrix()
{
    CheckCase(0, true, new[] { 0x19 }, "TCryCharm");
    CheckCase(1, true, new[] { 0x1A }, "THPCharm");
    CheckCase(2, true, new[] { 0x1B }, "TMPCharm");
    CheckCase(3, true, new[] { 0x1A, 0x1B }, "THPMPCharm");
    CheckCase(4, true, Array.Empty<int>(), "TCharm");
    CheckCase(5, false, Array.Empty<int>(), "TMarkStoneCharm shape 5");
    CheckCase(9, false, Array.Empty<int>(), "TMarkStoneCharm shape 9");
    CheckCase(0, false, Array.Empty<int>(), "non-charm", stdMode: 15);
}

static void CheckOverwriteAndIsolation()
{
    foreach (var fill in new byte[] { 0x00, 0x02, 0xFF })
    foreach (var shape in new byte[] { 0, 1, 2, 3, 4 })
    {
        var secondary = Enumerable.Repeat(fill,
            NativeCharmEquipmentAbilityCore.SecondarySize).ToArray();
        var expected = (byte[])secondary.Clone();
        foreach (var offset in ExpectedOffsets(shape)) expected[offset] = 1;

        Check(NativeCharmEquipmentAbilityCore.TryApply(Charm(shape), secondary),
            $"shape {shape} handled with fill {fill:X2}");
        SequenceEqual(expected, secondary,
            $"shape {shape} exact writes with fill {fill:X2}");

        Check(NativeCharmEquipmentAbilityCore.TryApply(Charm(shape), secondary),
            $"shape {shape} second application handled");
        SequenceEqual(expected, secondary,
            $"shape {shape} idempotent");
    }
}

static void CheckWriteOrder()
{
    var writes = new List<(int Offset, byte Value)>();
    Check(NativeCharmEquipmentAbilityCore.TryApplyWithWriter(Charm(3),
        (offset, value) => writes.Add((offset, value))),
        "THPMPCharm writer handled");
    SequenceEqual(new[] { (0x1A, (byte)1), (0x1B, (byte)1) }, writes,
        "THPMPCharm HP-before-MP write order");

    writes.Clear();
    Check(NativeCharmEquipmentAbilityCore.TryApplyWithWriter(Charm(4),
        (offset, value) => writes.Add((offset, value))),
        "base TCharm writer handled");
    Equal(0, writes.Count, "base TCharm is a no-op");

    writes.Clear();
    Check(!NativeCharmEquipmentAbilityCore.TryApplyWithWriter(Charm(5),
        (offset, value) => writes.Add((offset, value))),
        "TMarkStoneCharm writer excluded");
    Equal(0, writes.Count, "excluded class has no writes");
}

static void CheckMarkStoneZeroDurability()
{
    var primary = Pattern(NativeCharmEquipmentAbilityCore.PrimarySize, 0x31);
    var secondary = Pattern(NativeCharmEquipmentAbilityCore.SecondarySize, 0x91);
    var expectedPrimary = (byte[])primary.Clone();
    var expectedSecondary = (byte[])secondary.Clone();
    expectedSecondary[0x1A] = 0;
    expectedSecondary[0x1B] = 0;
    var item = new TUserItem
    {
        Dura = 0,
        NativeItemPlus102 = 0xFF,
        NativeItemPlus103 = 0xFF
    };

    Check(NativeCharmEquipmentAbilityCore.TryApply(item,
        MarkStone(9), primary, secondary),
        "zero-durability mark stone handled");
    SequenceEqual(expectedPrimary, primary,
        "zero durability leaves primary untouched");
    SequenceEqual(expectedSecondary, secondary,
        "zero durability clears only HP and MP flags");
}

static void CheckMarkStoneScaledEndpoints()
{
    var primary = new byte[NativeCharmEquipmentAbilityCore.PrimarySize];
    var secondary = Pattern(NativeCharmEquipmentAbilityCore.SecondarySize, 0xA0);
    var item = new TUserItem
    {
        Dura = 1,
        NativeItemPlus102 = 25,
        NativeItemPlus103 = 0
    };
    var stdItem = MarkStone(5);
    stdItem.Dc = 4;
    stdItem.Dc2 = 8;
    stdItem.Mc = 12;
    stdItem.Mc2 = 16;
    stdItem.Sc = 20;
    stdItem.Sc2 = 24;
    stdItem.Cc = 28;
    stdItem.Cc2 = 32;
    stdItem.Source = -1;
    stdItem.AniCount = 0x1234;
    stdItem.WordParam2 = 0x5678;

    Check(NativeCharmEquipmentAbilityCore.TryApply(item, stdItem,
        primary, secondary), "positive-durability mark stone handled");
    var offsets = new[] { 0x1C, 0x20, 0x24, 0x28, 0x2C, 0x30, 0x34, 0x38 };
    var expected = new uint[] { 5, 10, 15, 20, 25, 30, 35, 40 };
    for (var index = 0; index < offsets.Length; index++)
        Equal(expected[index], ReadUInt32(primary, offsets[index]),
            "scaled endpoint " + index);
    Equal((ushort)0x00FF, ReadUInt16(primary, 0x3C),
        "Source is zero-extended from native byte");
    Equal((ushort)0x1234, ReadUInt16(primary, 0x4C),
        "AniCount word add");
    Equal((ushort)0x5678, ReadUInt16(primary, 0x0A),
        "WordParam2 speed add");
    Equal((byte)1, secondary[0x1A], "positive durability HP flag");
    Equal((byte)1, secondary[0x1B], "positive durability MP flag");
    for (var offset = 0; offset < secondary.Length; offset++)
        if (offset is not 0x1A and not 0x1B)
            Equal(unchecked((byte)(0xA0 + offset)), secondary[offset],
                "positive durability secondary isolation " + offset);

    Array.Clear(primary);
    item.NativeItemPlus102 = 0;
    item.NativeItemPlus103 = 1;
    stdItem.Dc = 100;
    stdItem.Dc2 = stdItem.Mc = stdItem.Mc2 = 0;
    stdItem.Sc = stdItem.Sc2 = stdItem.Cc = stdItem.Cc2 = 0;
    stdItem.Source = 0;
    stdItem.AniCount = stdItem.WordParam2 = 0;
    NativeCharmEquipmentAbilityCore.TryApply(item, stdItem,
        primary, secondary);
    Equal(356u, ReadUInt32(primary, 0x1C),
        "item+0x102 high byte participates in percentage");
}

static void CheckMarkStoneShapeBranches()
{
    foreach (var shape in new byte[] { 5, 6, 7, 8, 9 })
    {
        var primary = new byte[NativeCharmEquipmentAbilityCore.PrimarySize];
        var secondary = new byte[NativeCharmEquipmentAbilityCore.SecondarySize];
        var stdItem = MarkStone(shape);
        stdItem.WordParam1 = 0x1234;
        stdItem.WordParam2 = 3;
        NativeCharmEquipmentAbilityCore.TryApply(
            new TUserItem { Dura = 1 }, stdItem, primary, secondary);

        Equal(shape == 6 ? (ushort)0x1234 : (ushort)0,
            ReadUInt16(primary, 0x08), "shape 6 hit-point branch");
        Equal(shape == 9 ? (ushort)0x1237 : (ushort)3,
            ReadUInt16(primary, 0x0A), "shape 9 speed branch order");
        Equal(shape == 7 ? (ushort)0x1234 : (ushort)0,
            ReadUInt16(primary, 0xB8), "shape 7 +0xB8 branch");
        Equal(shape == 8 ? 0x1234u : 0u,
            ReadUInt32(primary, 0x6C), "shape 8 +0x6C branch");
    }
}

static void CheckMarkStoneWrappingAndRouting()
{
    var primary = new byte[NativeCharmEquipmentAbilityCore.PrimarySize];
    var secondary = new byte[NativeCharmEquipmentAbilityCore.SecondarySize];
    WriteUInt32(primary, 0x1C, uint.MaxValue - 2);
    WriteUInt16(primary, 0x0A, 0xFFFE);
    WriteUInt16(primary, 0x3C, 0xFFFE);
    var stdItem = MarkStone(9);
    stdItem.Dc = 2;
    stdItem.WordParam1 = 2;
    stdItem.WordParam2 = 3;
    stdItem.Source = 5;
    NativeCharmEquipmentAbilityCore.TryApply(
        new TUserItem { Dura = 1 }, stdItem, primary, secondary);
    Equal(uint.MaxValue, ReadUInt32(primary, 0x1C),
        "dword add preserves native unchecked arithmetic");
    Equal((ushort)3, ReadUInt16(primary, 0x0A),
        "two ordered speed word adds wrap");
    Equal((ushort)3, ReadUInt16(primary, 0x3C),
        "Source word add wraps");

    primary = Pattern(NativeCharmEquipmentAbilityCore.PrimarySize, 0x11);
    secondary = Pattern(NativeCharmEquipmentAbilityCore.SecondarySize, 0x51);
    var beforePrimary = (byte[])primary.Clone();
    var beforeSecondary = (byte[])secondary.Clone();
    Check(!NativeCharmEquipmentAbilityCore.TryApply(
        new TUserItem { Dura = 1 },
        new GoodItem { StdMode = 15, Shape = 5 }, primary, secondary),
        "non-charm full overload is unhandled");
    SequenceEqual(beforePrimary, primary,
        "unhandled full overload preserves primary");
    SequenceEqual(beforeSecondary, secondary,
        "unhandled full overload preserves secondary");
}

static void CheckArgumentGates()
{
    Throws<ArgumentNullException>(() =>
        NativeCharmEquipmentAbilityCore.TryApply(null,
            new byte[NativeCharmEquipmentAbilityCore.SecondarySize]),
        "null std item");
    Throws<ArgumentNullException>(() =>
        NativeCharmEquipmentAbilityCore.TryApply(Charm(0), null),
        "null secondary");
    Throws<ArgumentException>(() =>
        NativeCharmEquipmentAbilityCore.TryApply(Charm(0),
            new byte[NativeCharmEquipmentAbilityCore.SecondarySize - 1]),
        "short secondary");
    Throws<ArgumentNullException>(() =>
        NativeCharmEquipmentAbilityCore.TryApplyWithWriter(null, (_, _) => { }),
        "null writer std item");
    Throws<ArgumentNullException>(() =>
        NativeCharmEquipmentAbilityCore.TryApplyWithWriter(Charm(0), null),
        "null writer");
    Throws<ArgumentNullException>(() =>
        NativeCharmEquipmentAbilityCore.TryApply(null, MarkStone(5),
            new byte[NativeCharmEquipmentAbilityCore.PrimarySize],
            new byte[NativeCharmEquipmentAbilityCore.SecondarySize]),
        "full overload null item");
    Throws<ArgumentNullException>(() =>
        NativeCharmEquipmentAbilityCore.TryApply(new TUserItem(), null,
            new byte[NativeCharmEquipmentAbilityCore.PrimarySize],
            new byte[NativeCharmEquipmentAbilityCore.SecondarySize]),
        "full overload null std item");
    Throws<ArgumentNullException>(() =>
        NativeCharmEquipmentAbilityCore.TryApply(new TUserItem(),
            MarkStone(5), null,
            new byte[NativeCharmEquipmentAbilityCore.SecondarySize]),
        "full overload null primary");
    Throws<ArgumentException>(() =>
        NativeCharmEquipmentAbilityCore.TryApply(new TUserItem(),
            MarkStone(5),
            new byte[NativeCharmEquipmentAbilityCore.PrimarySize - 1],
            new byte[NativeCharmEquipmentAbilityCore.SecondarySize]),
        "full overload short primary");
    Throws<ArgumentNullException>(() =>
        NativeCharmEquipmentAbilityCore.TryApply(new TUserItem(),
            MarkStone(5),
            new byte[NativeCharmEquipmentAbilityCore.PrimarySize], null),
        "full overload null secondary");
    Throws<ArgumentException>(() =>
        NativeCharmEquipmentAbilityCore.TryApply(new TUserItem(),
            MarkStone(5),
            new byte[NativeCharmEquipmentAbilityCore.PrimarySize],
            new byte[NativeCharmEquipmentAbilityCore.SecondarySize - 1]),
        "full overload short secondary");
}

static void CheckCase(byte shape, bool expectedHandled,
    IReadOnlyCollection<int> expectedOffsets, string label, byte stdMode = 7)
{
    var secondary = new byte[NativeCharmEquipmentAbilityCore.SecondarySize];
    var handled = NativeCharmEquipmentAbilityCore.TryApply(
        new GoodItem { StdMode = stdMode, Shape = shape }, secondary);
    Equal(expectedHandled, handled, label + " handled");
    var actualOffsets = secondary.Select((value, offset) => (value, offset))
        .Where(pair => pair.value != 0)
        .Select(pair => pair.offset)
        .ToArray();
    SequenceEqual(expectedOffsets.ToArray(), actualOffsets,
        label + " write set");
}

static GoodItem Charm(byte shape) => new() { StdMode = 7, Shape = shape };

static GoodItem MarkStone(byte shape) => new() { StdMode = 7, Shape = shape };

static byte[] Pattern(int length, int seed) => Enumerable.Range(0, length)
    .Select(offset => unchecked((byte)(seed + offset))).ToArray();

static ushort ReadUInt16(byte[] block, int offset) =>
    BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(offset, 2));

static uint ReadUInt32(byte[] block, int offset) =>
    BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(offset, 4));

static void WriteUInt16(byte[] block, int offset, ushort value) =>
    BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(offset, 2), value);

static void WriteUInt32(byte[] block, int offset, uint value) =>
    BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(offset, 4), value);

static int[] ExpectedOffsets(byte shape) => shape switch
{
    0 => new[] { 0x19 },
    1 => new[] { 0x1A },
    2 => new[] { 0x1B },
    3 => new[] { 0x1A, 0x1B },
    _ => Array.Empty<int>()
};

static void Throws<T>(Action action, string label) where T : Exception
{
    try
    {
        action();
        throw new Exception(label + ": expected " + typeof(T).Name);
    }
    catch (T)
    {
    }
}

static void SequenceEqual<T>(IReadOnlyList<T> expected,
    IReadOnlyList<T> actual, string label)
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
