using System.Buffers.Binary;
using GameSvr;
using SystemModule;

var root = Path.Combine(Path.GetTempPath(),
    "lyom2-dropcontrol-parser-" + Guid.NewGuid().ToString("N"));
var directory = Path.Combine(root, "DropControl");
Directory.CreateDirectory(directory);

try
{
    var mapState = new NativeDropControlState(
        NativeDropControlBucketField.ItemName);
    var tick = 1000;
    WriteGbk(Path.Combine(directory, "map-a.txt"),
        "TYPE=1\r\n" +
        "2:5 金币 白野猪\r\n" +
        "3 7 金币 祖玛教主\r\n" +
        "4 8 金币 赤月恶魔\r\n" +
        "bad:bad 默认物品 默认怪物\r\n" +
        "1 2 Z-item Z怪\r\n" +
        "1 2 a-item A怪\r\n" +
        "9 9 跳过物品 怪物;整行注释\r\n" +
        "type=2\r\n" +
        "$10:0x20 天外飞仙超级无敌特别特别特别长的测试物品名称 牛魔王\r\n");

    Assert(NativeDropControlLoader.TryLoadMap(root, "map-a", mapState,
        ResolveItem, range => range - 1, () => tick++, out var error),
        "map load: " + error);
    Equal(7, mapState.RecordCount, "map record count");
    Equal(4, mapState.BucketCount(NativeDropControlType.Timed),
        "map timed bucket count");
    Equal(1, mapState.BucketCount(NativeDropControlType.Counted),
        "map counted bucket count");

    var gold = new List<NativeDropControlRecord>();
    mapState.VisitBucket(NativeDropControlType.Timed, "金币", gold.Add);
    Equal(3, gold.Count, "same item bucket count");
    Equal("白野猪", gold[0].MonsterName, "native chain head");
    Equal("赤月恶魔", gold[1].MonsterName, "native chain inserted second");
    Equal("祖玛教主", gold[2].MonsterName, "native chain old second");

    Equal((ushort)2, gold[0].Quantity, "quantity");
    Equal(5, gold[0].PeriodOrRange, "period");
    Equal(115, gold[0].ItemIndex, "item resolver");
    Equal((ushort)5, gold[0].RandomThreshold, "random threshold +1");
    Equal(1000, gold[0].Tick, "tick");

    var defaulted = mapState.Snapshot(NativeDropControlType.Timed)
        .Single(record => record.ItemName == "默认物品");
    Equal((ushort)0, defaulted.Quantity, "invalid quantity default");
    Equal(1, defaulted.PeriodOrRange, "invalid range default");
    Equal((ushort)1, defaulted.RandomThreshold,
        "default range random threshold");

    var orderedItems = new List<string>();
    mapState.VisitAll(NativeDropControlType.Timed,
        record => orderedItems.Add(record.ItemName));
    Assert(orderedItems.IndexOf("a-item") < orderedItems.IndexOf("Z-item"),
        "Sorted TStringList cross-bucket order: " +
        string.Join(" | ", orderedItems));

    var counted = mapState.Snapshot(NativeDropControlType.Counted).Single();
    Equal((ushort)0x10, counted.Quantity, "Delphi $ hex quantity");
    Equal(0x20, counted.PeriodOrRange, "Delphi 0x hex range");
    Assert(HUtil32.GbkEncoding.GetByteCount(counted.ItemName) <= 40,
        "ShortString[40] GBK truncation");
    Equal(9001, counted.ItemIndex,
        "resolver receives untruncated item name");

    var layout = gold[0].ToNativeLayout();
    Equal(104, layout.Length, "native record size");
    Equal((byte)HUtil32.GbkEncoding.GetByteCount("白野猪"), layout[0],
        "monster ShortString length");
    Equal((byte)HUtil32.GbkEncoding.GetByteCount("金币"), layout[0x29],
        "item ShortString length");
    Equal((ushort)2,
        BinaryPrimitives.ReadUInt16LittleEndian(layout.AsSpan(0x52, 2)),
        "layout quantity");
    Equal(5, BinaryPrimitives.ReadInt32LittleEndian(layout.AsSpan(0x54, 4)),
        "layout period");
    Equal(115,
        BinaryPrimitives.ReadInt32LittleEndian(layout.AsSpan(0x58, 4)),
        "layout item index");
    Equal((ushort)0,
        BinaryPrimitives.ReadUInt16LittleEndian(layout.AsSpan(0x5C, 2)),
        "layout counter");
    Equal((ushort)5,
        BinaryPrimitives.ReadUInt16LittleEndian(layout.AsSpan(0x5E, 2)),
        "layout threshold");
    Equal(1000,
        BinaryPrimitives.ReadInt32LittleEndian(layout.AsSpan(0x60, 4)),
        "layout tick");
    Equal(0,
        BinaryPrimitives.ReadInt32LittleEndian(layout.AsSpan(0x64, 4)),
        "managed next pointer");

    mapState.VisitBucket(NativeDropControlType.Timed, "金币", record =>
    {
        record.Counter++;
        record.Tick += 10;
    });
    var mutated = mapState.Snapshot(NativeDropControlType.Timed)
        .Where(record => record.ItemName == "金币").ToArray();
    Assert(mutated.All(record => record.Counter == 1),
        "locked bucket mutation counter");
    Assert(mutated.All(record => record.Tick >= 1010),
        "locked bucket mutation tick");

    var beforeMissing = mapState.RecordCount;
    Assert(!NativeDropControlLoader.TryLoadMap(root, "missing", mapState,
        ResolveItem, range => 0, () => 0, out _),
        "missing file returns false");
    Equal(beforeMissing, mapState.RecordCount,
        "missing file preserves old table");

    WriteGbk(Path.Combine(directory, "empty.txt"), string.Empty);
    Assert(NativeDropControlLoader.TryLoadMap(root, "empty", mapState,
        ResolveItem, range => 0, () => 0, out error),
        "empty existing file: " + error);
    Equal(0, mapState.RecordCount, "existing file clears old table");

    WriteGbk(Path.Combine(directory, "partial.txt"),
        "type=1\r\n1 2 前缀物品 前缀怪物\r\n" +
        "1 2 抛错物品 后缀怪物\r\n");
    Assert(!NativeDropControlLoader.TryLoadMap(root, "partial", mapState,
        name => name == "抛错物品"
            ? throw new InvalidOperationException("resolver failure")
            : ResolveItem(name), range => 0, () => 77, out error),
        "partial parse returns false");
    Equal("resolver failure", error, "partial parse error");
    Equal(1, mapState.RecordCount,
        "parse failure retains inserted prefix only");

    WriteGbk(Path.Combine(directory, "edge-random.txt"),
        "type=1\r\n1 0 零范围物品 零范围怪物\r\n" +
        "1 -1 负范围物品 负范围怪物\r\n");
    var randomInputs = new List<int>();
    Assert(NativeDropControlLoader.TryLoadMap(root, "edge-random", mapState,
        _ => 0, range =>
        {
            randomInputs.Add(range);
            return range == 0 ? 0 : 0x1234;
        }, () => 99, out error), "edge random load: " + error);
    Equal(2, mapState.RecordCount, "zero item ids remain inserted");
    Assert(mapState.Snapshot(NativeDropControlType.Timed)
        .All(record => record.ItemIndex == 0),
        "item resolver zero is retained");
    Assert(randomInputs.SequenceEqual(new[] { 0, -1 }),
        "zero/negative range passed unchanged to native random delegate");
    var edgeRecords = mapState.Snapshot(NativeDropControlType.Timed);
    Equal((ushort)1, edgeRecords.Single(record =>
            record.ItemName == "零范围物品").RandomThreshold,
        "Random(0)+1 threshold");
    Equal(unchecked((ushort)0x1235), edgeRecords.Single(record =>
            record.ItemName == "负范围物品").RandomThreshold,
        "negative-range random result wraps to WORD");

    var worldState = new NativeDropControlState(
        NativeDropControlBucketField.MonsterName);
    WriteGbk(Path.Combine(directory, "WorldDrop.txt"),
        "type=2\r\n1 3 金币 白野猪\r\n2 4 裁决之杖 白野猪\r\n");
    Assert(NativeDropControlLoader.TryLoadWorld(root, worldState,
        ResolveItem, range => 0, () => 88, out error),
        "world load: " + error);
    var worldBucket = new List<NativeDropControlRecord>();
    worldState.VisitBucket(NativeDropControlType.Counted, "白野猪",
        worldBucket.Add);
    Equal(2, worldBucket.Count, "world monster bucket");
    Equal("金币", worldBucket[0].ItemName, "world chain head");
    Equal("裁决之杖", worldBucket[1].ItemName, "world chain second");
    var beforeWrongState = worldState.RecordCount;
    Assert(!NativeDropControlLoader.TryLoadMap(root, "map-a", worldState,
        ResolveItem, range => 0, () => 0, out _),
        "wrong bucket field rejected");
    Equal(beforeWrongState, worldState.RecordCount,
        "wrong state rejected before clear");

    Console.WriteLine(
        "NativeDropControlParserCheck PASS GBK/type1/type2/104B/defaults/" +
        "random/tick/map-item-bucket/world-monster-bucket/" +
        "missing-preserve/existing-clear/partial-prefix/locked-consume");
}
finally
{
    Directory.Delete(root, true);
}

static int ResolveItem(string name)
{
    if (name == "金币") return 115;
    if (name.StartsWith("天外飞仙", StringComparison.Ordinal)) return 9001;
    return 500;
}

static void WriteGbk(string fileName, string value)
{
    File.WriteAllBytes(fileName, HUtil32.GbkEncoding.GetBytes(value));
}

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{label}: expected={expected}, actual={actual}");
}

static void Assert(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
}
