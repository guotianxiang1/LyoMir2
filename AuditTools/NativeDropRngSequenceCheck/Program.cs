using GameSvr;
using SystemModule;

// Pins the monster-death drop RNG sequence against the native call order in
// sub_71FA20 and the per-class VMT+0x28 / Shape-130 VMT+0x08 bodies.
// Bounds (Random arguments) and draw counts are the contract; a later edit
// that inserts or drops a draw turns this red.

PrepareRuntimeConfig();
M2Share.RandomNumber = RandomNumber.GetInstance();
NativeJewelStoneTable.Reset();

PinClassBounds();
PinFieldHeroDl1Bounds();
PinJewelTableWrite();
PinSeededDropChain();
PinSourceOrder();

Console.WriteLine(
    "NativeDropRngSequenceCheck PASS " +
    "plus28-bounds fieldhero-dl1 jewel-table seeded-chain source-order");

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

static void PinFieldHeroDl1Bounds()
{
    var ordinaryEquipment = new[]
    {
        (Item: Std(30, 0), ClassName: "TRWeapon"),
        (Item: Std(5, 0), ClassName: "TLWeapon"),
        (Item: Std(5, 6, 100), ClassName: "TBrokenWeapon"),
        (Item: Std(5, 61), ClassName: "TSpade"),
        (Item: Std(16, 0), ClassName: "THeadMask"),
        (Item: Std(29, 0), ClassName: "TWarDrum"),
        (Item: Std(15, 0), ClassName: "THelmet"),
        (Item: Std(19, 0), ClassName: "TNecklace"),
        (Item: Std(22, 0), ClassName: "TRing"),
        (Item: Std(24, 0), ClassName: "TArmRing"),
        (Item: Std(27, 0), ClassName: "TBelt"),
        (Item: Std(28, 0), ClassName: "TBoots"),
        (Item: Std(34, 0), ClassName: "TMaPai"),
        (Item: Std(7, 4), ClassName: "TCharm"),
        (Item: Std(7, 0), ClassName: "TCryCharm"),
        (Item: Std(7, 1), ClassName: "THPCharm"),
        (Item: Std(7, 2), ClassName: "TMPCharm"),
        (Item: Std(7, 3), ClassName: "THPMPCharm"),
        (Item: Std(7, 5), ClassName: "TMarkStoneCharm"),
        (Item: Std(10, 0), ClassName: "TManClothes"),
        (Item: Std(11, 0), ClassName: "TWomanClothes"),
        (Item: Std(10, 28), ClassName: "TTemporaryManClothes"),
        (Item: Std(11, 28), ClassName: "TTemporaryWomanClothes"),
        (Item: Std(25, 1), ClassName: "TPoisons"),
        (Item: Std(25, 5), ClassName: "TBujuk"),
        (Item: Std(25, 7), ClassName: "TUnionItem"),
        (Item: Std(25, 8), ClassName: "TVessel"),
        (Item: Std(25, 9), ClassName: "TDragonHeart"),
        (Item: Std(25, 10), ClassName: "TSuperDragonHeart")
    };
    foreach (var entry in ordinaryEquipment)
    {
        Equal(NativeItemFactory.GetClassName(entry.Item), entry.ClassName,
            "FieldHero DL1 factory class " + entry.ClassName);
        EqualSeq(FieldHeroDraws(Fill: 1, entry.Item), new[] { 80 },
            "FieldHero DL1 ordinary " + entry.ClassName);
    }

    foreach (var shape in new byte[] { 130, 131, 132 })
    {
        EqualSeq(FieldHeroDraws(Fill: 1, Std(15, shape)),
            HelmetUnknown08(), $"FieldHero helmet shape={shape} +0x08");
        EqualSeq(FieldHeroDraws(Fill: 1, Std(22, shape)),
            RingUnknown08(), $"FieldHero ring shape={shape} +0x08");
        EqualSeq(FieldHeroDraws(Fill: 1, Std(24, shape)),
            ArmRingUnknown08(),
            $"FieldHero arm-ring shape={shape} Dura80 +0x08");
    }

    foreach (var shape in new byte[] { 129, 133 })
    {
        EqualSeq(FieldHeroDraws(Fill: 1, Std(15, shape)), new[] { 80 },
            $"FieldHero helmet adjacent shape={shape} stays ordinary");
        EqualSeq(FieldHeroDraws(Fill: 1, Std(22, shape)), new[] { 80 },
            $"FieldHero ring adjacent shape={shape} stays ordinary");
        EqualSeq(FieldHeroDraws(Fill: 1, Std(24, shape)), new[] { 80 },
            $"FieldHero arm-ring adjacent shape={shape} stays ordinary");
    }
    Equal(NativeItemFactory.GetClassName(Std(19, 130)), "TNecklace",
        "FieldHero shape 130 ordinary control class");
    EqualSeq(FieldHeroDraws(Fill: 1, Std(19, 130)), new[] { 80 },
        "FieldHero shape 130 is not globally special");

    var normal = Item(1000);
    UseRandom(Fill: 0);
    NativeItemPlus28.ApplyFromFieldHeroFill(normal, Std(5, 0));
    Equal((ushort)200, normal.Dura,
        "FieldHero DL1 base durability uses factor 20 when Random(80)=0");

    var rejectedItem = Item(1000);
    rejectedItem.Dura = 777;
    var rejectedRng = UseRandom(Fill: 0);
    ExpectThrows<InvalidDataException>(() =>
            NativeItemPlus28.ApplyFromFieldHeroFill(rejectedItem, Std(1, 0)),
        "FieldHero DL1 rejects non-equipment classes");
    EqualSeq(rejectedRng.Bounds.ToArray(), Array.Empty<int>(),
        "FieldHero DL1 non-equipment rejection consumes no RNG");
    Equal(rejectedItem.Dura, (ushort)777,
        "FieldHero DL1 non-equipment rejection preserves durability");

    rejectedItem = Item(1000);
    rejectedItem.Dura = 779;
    rejectedRng = UseRandom(Fill: 0);
    ExpectThrows<InvalidDataException>(() =>
            NativeItemPlus28.ApplyFromFieldHeroFill(rejectedItem, Std(154, 0)),
        "FieldHero DL1 rejects pile classes before their bare +0x28");
    EqualSeq(rejectedRng.Bounds.ToArray(), Array.Empty<int>(),
        "FieldHero DL1 pile rejection consumes no RNG");
    Equal(rejectedItem.Dura, (ushort)779,
        "FieldHero DL1 pile rejection preserves durability");
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
    // item+0x100..0x102 are runtime bytes, NOT record bytes: the persisted window is
    // item+0x20..item+0xEF (0x74DB3A lea edi,[ebx+0x20] / 0x74DB3D mov ecx,0x34 /
    // 0x74DB42 rep movsd), so record offset 0xE0 is past the end of the 208-byte
    // array and the old spelling threw IndexOutOfRange on every jewel drop.
    Equal(row[6], item.NativeItemPlus100, "jewel compose item+0x100");
    Equal(row[7], item.NativeItemPlus101, "jewel normal-up item+0x101");
    Equal(row[8], item.NativeItemPlus102, "jewel shop-up item+0x102");
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
        HelmetUnknown08());
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

    // The bound is now wrapped by the 眼神 equip-drop-boost trampoline
    // (YanshenEquipDropBoost.Denominator), so match the operand, not the old
    // Random(...) spelling; the ordering against the +0x28 hook is the contract.
    var gate = usr.IndexOf("MonItem.MaxPoint * penalty", StringComparison.Ordinal);
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

static int[] FieldHeroDraws(int Fill, GoodItem std)
{
    var rng = UseRandom(Fill);
    NativeItemPlus28.ApplyFromFieldHeroFill(Item(1000), std);
    return rng.Bounds.ToArray();
}

static BoundRandom UseRandom(int Fill)
{
    var rng = new BoundRandom(Fill);
    M2Share.RandomNumber = rng;
    return rng;
}

static TUserItem Item(ushort duraMax) => new() { DuraMax = duraMax, Dura = duraMax };

static GoodItem Std(byte mode, byte shape, ushort duraMax = 1000) => new()
{
    StdMode = mode,
    Shape = shape,
    DuraMax = duraMax
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
    throw new InvalidOperationException(label);
}

// M2Share's static constructor reads !Setup.txt / ../Share/*.ini out of the
// runtime directory; without them the very first M2Share touch throws before a
// single assertion runs.  Same bootstrap the other in-process audits use.
static void PrepareRuntimeConfig()
{
    var runtimeDirectory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(runtimeDirectory, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "String.ini"),
        "[String]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "Command.conf"),
        "[Command]" + Environment.NewLine);
    var shareDirectory = Path.Combine(Path.GetFullPath(
        Path.Combine(runtimeDirectory, "..")), "Share");
    Directory.CreateDirectory(shareDirectory);
    File.WriteAllText(Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"),
        "[Integer]" + Environment.NewLine);
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
