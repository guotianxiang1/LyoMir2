using GameSvr;
using SystemModule;

// Pins the monster-death drop RNG sequence against the native call order in
// sub_71FA20 and the per-class VMT+0x28 / Shape-130 VMT+0x08 bodies.
// Bounds (Random arguments) and draw counts are the contract; a later edit
// that inserts or drops a draw turns this red.

M2Share.RandomNumber = RandomNumber.GetInstance();
NativeJewelStoneTable.Reset();

PinClassBounds();
PinJewelTableWrite();
PinSeededDropChain();
PinSourceOrder();

Console.WriteLine(
    "NativeDropRngSequenceCheck PASS " +
    "plus28-bounds jewel-table seeded-chain source-order");

static void PinClassBounds()
{
    // Gate miss (Random never returns 0 except Random(1)): extra-attr body skipped.
    EqualSeq(Draws(Fill: 1, Std(5, 0)), new[] { 80, 10 }, "weapon gate-miss");
    EqualSeq(Draws(Fill: 1, Std(10, 0)), new[] { 80, 10 }, "clothes gate-miss");
    EqualSeq(Draws(Fill: 1, Std(15, 0)), new[] { 80, 10 }, "helmet gate-miss");
    EqualSeq(Draws(Fill: 1, Std(19, 0)), new[] { 80, 10 }, "necklace gate-miss");
    EqualSeq(Draws(Fill: 1, Std(22, 0)), new[] { 80, 9 }, "ring gate-miss Random(9)");
    EqualSeq(Draws(Fill: 1, Std(24, 0)), new[] { 80, 10 }, "armring gate-miss");
    EqualSeq(Draws(Fill: 1, Std(79, 0)), new[] { 80, 12 }, "jewel Random(80)+Random(12)");
    EqualSeq(Draws(Fill: 1, Std(1, 0)), new[] { 80 }, "base +0x28");
    EqualSeq(Draws(Fill: 1, Std(154, 0)), Array.Empty<int>(), "pile +0x28 bare ret");

    // Shape 130 skips Random(80) and runs +0x08 with no extra-attr gate.
    EqualSeq(Draws(Fill: 1, Std(15, 130)), HelmetUnknown08(), "helmet shape130 +0x08");
    EqualSeq(Draws(Fill: 1, Std(22, 130)), RingUnknown08(), "ring shape130 +0x08");
    EqualSeq(Draws(Fill: 1, Std(24, 130)), ArmRingUnknown08(), "armring shape130 +0x08 after dura80");

    Equal(HelmetUnknown08().Length, 49, "helmet +0x08 draw count");
    Equal(RingUnknown08().Length, 43, "ring +0x08 draw count");
    Equal(ArmRingUnknown08().Length, 48, "armring +0x08 draw count (80 + 47)");
}

static void PinJewelTableWrite()
{
    NativeJewelStoneTable.Reset();
    var row = new byte[] { 2, 7, 0x11, 0x22, 3, 9, 40, 50, 60 };
    NativeJewelStoneTable.SetRow(2, 1, row);

    var item = Item(1000);
    var std = Std(79, 0);
    std.WordParam1 = 2;
    UseRandom(Fill: 0); // Random(12)=0 → index 1
    NativeItemPlus28.ApplyOnDrop(item, std);

    Equal(row[0], item.btValue[12], "jewel type byte item+0x36");
    Equal(row[1], item.btValue[13], "jewel attr index item+0x37");
    Equal(row[2], item.NativeRecord[0x18], "jewel word lo item+0x38");
    Equal(row[3], item.NativeRecord[0x19], "jewel word hi item+0x39");
    Equal(row[4], item.NativeRecord[0x1A], "jewel min item+0x3A");
    Equal(row[5], item.NativeRecord[0x1B], "jewel max item+0x3B");
    Equal(row[6], item.NativeRecord[0xE0], "jewel compose item+0x100");
    Equal(row[7], item.NativeRecord[0xE1], "jewel normal-up item+0x101");
    Equal(row[8], item.NativeRecord[0xE2], "jewel shop-up item+0x102");
    NativeJewelStoneTable.Reset();

    var skipped = Item(1000);
    var zeroStd = Std(79, 0);
    zeroStd.WordParam1 = 0;
    UseRandom(Fill: 0);
    NativeItemPlus28.ApplyOnDrop(skipped, zeroStd);
    Equal((byte)0, skipped.btValue[12], "type 0 skips table write");
    Equal(skipped.NativeRecord == null, true, "type 0 does not allocate NativeRecord");
}

static void PinSeededDropChain()
{
    // Fixed seed. Extra-attr flag is off, so every class has a seed-independent
    // bound list. Order matches sub_71FA20: exclusive gold → exclusive item
    // +0x28 → table gate Random(MaxPoint*penalty) → table gold Random(Count)
    // or table item +0x28.
    const uint seed = 0xA5A5A5A5u;
    DelphiRandom.Seed = seed;
    M2Share.RandomNumber = RandomNumber.GetInstance();
    RngTraceSink.Reset();
    RngTraceSink.Enabled = true;
    RngTraceSink.CurrentOwner = "drop-chain";

    // 0x71FB76 Random(N) exclusive-chain gold, N = RepeatCount
    _ = M2Share.RandomNumber.Random(30);
    // 0x71FBD5 exclusive item +0x28 (base)
    NativeItemPlus28.ApplyOnDrop(Item(500), Std(1, 0));
    // 0x71FD3D Random(MaxPoint * penalty)
    _ = M2Share.RandomNumber.Random(100);
    // 0x71FD6B table gold Random(Count)
    _ = M2Share.RandomNumber.Random(40);
    // 0x71FDA2 table item +0x28 pile (0 draws)
    NativeItemPlus28.ApplyOnDrop(Item(1), Std(154, 0));
    // another table item: jewel
    NativeItemPlus28.ApplyOnDrop(Item(1000), Std(79, 0));
    // helmet shape 130
    NativeItemPlus28.ApplyOnDrop(Item(800), Std(15, 130));

    RngTraceSink.Enabled = false;
    var bounds = RngTraceSink.Log.Select(d => (int)d.Arg0).ToArray();
    var expected = Concat(
        new[] { 30, 80, 100, 40 },
        new[] { 80, 12 },
        HelmetUnknown08);
    EqualSeq(bounds, expected, "seeded drop-chain bounds");
    Equal(bounds.Length, expected.Length, "seeded drop-chain count");
    Equal(seed != DelphiRandom.Seed, true, "seed advanced");
}

static void PinSourceOrder()
{
    var root = FindRepoRoot();
    var usr = File.ReadAllText(Path.Combine(root, "GameSvr", "UsrSystem", "UsrEngn.cs"));
    var tree = File.ReadAllText(Path.Combine(root, "GameSvr", "UsrSystem",
        "UserEngine.MonItemsTree.cs"));
    var magic = File.ReadAllText(Path.Combine(root, "GameSvr", "Spells", "Magic.cs"));
    var plus = File.ReadAllText(Path.Combine(root, "GameSvr", "Items",
        "NativeItemPlus28.cs"));

    var gate = usr.IndexOf("Random(MonItem.MaxPoint * penalty)", StringComparison.Ordinal);
    var drop = usr.IndexOf("NativeItemPlus28.ApplyOnDrop", StringComparison.Ordinal);
    Assert(gate >= 0 && drop > gate, "MonGetRandomItems gate before +0x28");

    var gold = tree.IndexOf("Random(node.RepeatCount)", StringComparison.Ordinal);
    var chainDrop = tree.IndexOf("NativeItemPlus28.ApplyOnDrop", StringComparison.Ordinal);
    Assert(gold >= 0 && chainDrop > gold, "exclusive gold Random before item +0x28");

    Assert(!magic.Contains("Math.Max(1, UserMagic.MagicInfo.wMaxPower",
        StringComparison.Ordinal),
        "Magic.MPow must not clamp a negative native bound");
    Assert(plus.Contains("ApplyUnknownHelmet08", StringComparison.Ordinal),
        "helmet shape 130 must call +0x08");
    Assert(plus.Contains("NativeJewelStoneTable.Apply", StringComparison.Ordinal),
        "jewel +0x28 must write the 9-byte row");
}

static int[] Draws(int Fill, GoodItem std)
{
    var rng = UseRandom(Fill);
    NativeItemPlus28.ApplyOnDrop(Item(1000), std);
    return rng.Bounds.ToArray();
}

static BoundRandom UseRandom(int Fill)
{
    var rng = new BoundRandom(Fill);
    M2Share.RandomNumber = rng;
    return rng;
}

static TUserItem Item(ushort duraMax) => new() { DuraMax = duraMax, Dura = duraMax };

static GoodItem Std(byte mode, byte shape) => new()
{
    StdMode = mode,
    Shape = shape,
    DuraMax = 1000
};

static int[] Repeat(int bound, int n)
{
    var a = new int[n];
    Array.Fill(a, bound);
    return a;
}

static int[] Concat(params int[][] parts) => parts.SelectMany(x => x).ToArray();

static int[] HelmetUnknown08() => Concat(
    Repeat(3, 4), Repeat(8, 4), Repeat(20, 4),
    Repeat(3, 4), Repeat(8, 4), Repeat(20, 4),
    Repeat(15, 3), Repeat(30, 3),
    Repeat(15, 3), Repeat(30, 3),
    Repeat(15, 3), Repeat(30, 3),
    Repeat(30, 6),
    new[] { 30 });

static int[] RingUnknown08() => Concat(
    Repeat(4, 3), Repeat(8, 3), Repeat(20, 6),
    Repeat(4, 3), Repeat(8, 3), Repeat(20, 6),
    Repeat(4, 3), Repeat(8, 3), Repeat(20, 6),
    Repeat(30, 6),
    new[] { 30 });

static int[] ArmRingUnknown08() => Concat(
    new[] { 80 },
    Repeat(5, 3), Repeat(20, 5),
    Repeat(5, 3), Repeat(20, 5),
    Repeat(15, 3), Repeat(30, 5),
    Repeat(15, 3), Repeat(30, 5),
    Repeat(15, 3), Repeat(30, 5),
    Repeat(30, 6),
    new[] { 30 });

static void EqualSeq(int[] actual, int[] expected, string label)
{
    if (actual.Length != expected.Length || !actual.SequenceEqual(expected))
        throw new InvalidOperationException(
            $"{label}: expected [{string.Join(",", expected)}] " +
            $"actual [{string.Join(",", actual)}]");
}

static void Equal<T>(T actual, T expected, string label)
{
    if (!EqualityComparer<T>.Default.Equals(actual, expected))
        throw new InvalidOperationException($"{label}: expected {expected} actual {actual}");
}

static void Assert(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
}

static string FindRepoRoot()
{
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        var dir = new DirectoryInfo(start);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "GameSvr")))
                return dir.FullName;
            dir = dir.Parent;
        }
    }
    throw new DirectoryNotFoundException("repository root");
}

sealed class BoundRandom : RandomNumber
{
    public BoundRandom(int fill) => Fill = fill;
    public int Fill { get; }
    public List<int> Bounds { get; } = new();

    public override int Random(int Value)
    {
        Bounds.Add(Value);
        if (Value <= 1) return 0;
        return Fill >= Value ? Value - 1 : Fill;
    }

    public override int Random()
    {
        Bounds.Add(0);
        return 0;
    }

    public override int Random(int minValue, int maxValue) =>
        throw new InvalidOperationException("unexpected Random(min,max)");

    public override int GetRandomNumber(int minValue, int maxValue) =>
        throw new InvalidOperationException("unexpected GetRandomNumber");
}
