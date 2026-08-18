using System.Buffers.Binary;
using System.Text;
using GameSvr.Services;
using SystemModule;

CheckParserAndRuntimeBinding();
CheckDelphiValIntegerSyntax();
CheckEmptyTableAndGenerationOwnership();
CheckDuplicateItemNameUsesLastLoaded();
CheckFileSourcePathAndEncoding();
CheckFailureGuards();

Console.WriteLine("PASS NativeFieldHeroMonItemsCheck " +
                  "parser=0x20-semantics gold=wire0 " +
                  "path=raw-byte-fold ownership=generation-held");

static void CheckParserAndRuntimeBinding()
{
    var items = CreateStandardItems(Bytes("Blade"), Bytes("Slash/Blade"));
    var definitions = CreateFieldHeroCatalog(items,
        CreateFieldHeroBody(Bytes("DropHero")),
        CreateFieldHeroBody(Bytes("EmptyHero")));
    var source = new MemoryMonItemsSource();
    source.Set("drophero", new[]
    {
        " \t;  ",
        string.Empty,
        "1/100\t金币\t10",
        "0/0 Blade 0",
        "-5/-7 Slash/Blade -9",
        "bad/bad Blade",
        ";1/2 Blade 3",
        "1/2 blade 5",
        "1/2 \"Blade\" 3",
        "1/2 Missing 3",
        "2/3 Blade 4"
    });

    var adapter = new NativeType2FieldHeroRuntimeCatalogAdapter();
    adapter.Publish(definitions, items, source);
    var drops = Materialize(adapter, "DropHero").DropItems;

    Equal(7, drops.Count, "resolved row count and duplicate retention");
    CheckDrop(drops[0], 0, 100, 0, 10, "金币", "gold row");
    Check(drops[0].IsGold, "wire index zero identifies gold");
    CheckDrop(drops[1], -1, 0, 1, 0, "Blade", "zero row");
    CheckDrop(drops[2], -6, -7, 2, -9, "Slash/Blade",
        "negative and slash-name row");
    CheckDrop(drops[3], 0, 1, 1, 1, "Blade",
        "invalid and omitted values use defaults");
    CheckDrop(drops[4], 0, 2, 1, 3, "Blade",
        "semicolon prefix is data, not a comment");
    CheckDrop(drops[5], 0, 2, 1, 5, "blade",
        "ASCII item lookup uses native byte fold");
    CheckDrop(drops[6], 1, 3, 1, 4, "Blade",
        "duplicate later row preserves order");
    Equal(0, Materialize(adapter, "EmptyHero").DropItems.Count,
        "definition without file owns a non-null empty table");
    Equal(2, source.LoadedDefinitionNames.Count,
        "source invoked once for every definition");
    Equal("DropHero", source.LoadedDefinitionNames[0],
        "source receives original definition bytes");
}

static void CheckEmptyTableAndGenerationOwnership()
{
    var items = CreateStandardItems(Bytes("Blade"));
    var definitions = CreateFieldHeroCatalog(items,
        CreateFieldHeroBody(Bytes("ReloadHero")));
    var source = new MemoryMonItemsSource();
    source.Set("reloadhero", new[] { "1/2 Blade 3" });
    var adapter = new NativeType2FieldHeroRuntimeCatalogAdapter();
    adapter.Publish(definitions, items, source);
    var oldMaterialization = Materialize(adapter, "ReloadHero");

    source.Set("reloadhero", new[] { "1/2 金币 9" });
    adapter.Replace(definitions, items, source);
    var nextMaterialization = Materialize(adapter, "ReloadHero");

    Equal(1L, oldMaterialization.Generation, "old drop generation");
    Equal((ushort)1, oldMaterialization.DropItems[0].NativeWireIndex,
        "old generation retains manager-owned item pointer");
    Equal(2L, nextMaterialization.Generation, "new drop generation");
    Equal((ushort)0, nextMaterialization.DropItems[0].NativeWireIndex,
        "replacement publishes its own drop table");
    var empty = new NativeType2FieldHeroRuntimeCatalogAdapter();
    empty.Publish(definitions, items);
    Equal(0, Materialize(empty, "ReloadHero").DropItems.Count,
        "default source is an explicit empty table");
}

static void CheckDelphiValIntegerSyntax()
{
    var items = CreateStandardItems(Bytes("Blade"));
    var item = items.Items[1];
    var drops = NativeFieldHeroMonItemsParser.Parse(new[]
    {
        "$10/$20 Blade $30",
        "x10/X20 Blade 0x30",
        "0X10/-$20 Blade -0x30",
        "+0x10/+x20 Blade +$30",
        "12tail/2147483648 Blade 2147483648",
        "-2147483648/-0x80000000 Blade -2147483648",
        "0x/0X Blade $",
        "$80000000/$FFFFFFFF Blade -$FFFFFFFF",
        "$FFFFFFFF/$100000000 Blade $100000000",
        "-$FFFFFFFF/-$80000000 Blade 1"
    }, name => name == "Blade" ? item : null);

    Equal(10, drops.Length, "Delphi Val row count");
    CheckDrop(drops[0], 15, 32, 1, 48, "Blade", "$ hex");
    CheckDrop(drops[1], 15, 32, 1, 48, "Blade", "x/X hex");
    CheckDrop(drops[2], 15, -32, 1, -48, "Blade",
        "0X and negative hex");
    CheckDrop(drops[3], 15, 32, 1, 48, "Blade",
        "signed positive hex");
    CheckDrop(drops[4], 0, 1, 1, 1, "Blade",
        "invalid tail and positive overflow default");
    CheckDrop(drops[5], int.MaxValue, int.MinValue, 1, int.MinValue,
        "Blade", "signed minimum boundary");
    CheckDrop(drops[6], 0, 1, 1, 1, "Blade",
        "prefix without digits defaults");
    CheckDrop(drops[7], int.MaxValue, -1, 1, 1, "Blade",
        "unsigned high hex and signed wrap");
    CheckDrop(drops[8], -2, 1, 1, 1, "Blade",
        "maximum hex and 33-bit overflow default");
    CheckDrop(drops[9], 0, int.MinValue, 1, 1, "Blade",
        "negative high hex wraps after sign application");
}

static void CheckDuplicateItemNameUsesLastLoaded()
{
    var items = CreateStandardItems(Bytes("Blade"), Bytes("blade"));
    var definitions = CreateFieldHeroCatalog(items,
        CreateFieldHeroBody(Bytes("Duplicate")));
    var source = new MemoryMonItemsSource();
    source.Set("duplicate", new[] { "1/2 BLADE 1" });
    var adapter = new NativeType2FieldHeroRuntimeCatalogAdapter();
    adapter.Publish(definitions, items, source);

    var drop = Materialize(adapter, "Duplicate").DropItems.Single();
    Equal((ushort)2, drop.NativeWireIndex,
        "hash-bucket head selects last loaded duplicate item");
}

static void CheckFileSourcePathAndEncoding()
{
    var root = Path.Combine(Path.GetTempPath(),
        "fieldhero-monitems-" + Guid.NewGuid().ToString("N"));
    var monItems = Path.Combine(root, "MonItems");
    Directory.CreateDirectory(monItems);
    try
    {
        var source = new NativeFieldHeroFileMonItemsSource(root);
        var rawName = Bytes("MiXeD");
        var expectedPath = Path.Combine(monItems, "mixed.txt");
        Equal(expectedPath, source.ResolvePath(rawName),
            "ASCII definition bytes fold in the file name");
        File.WriteAllText(expectedPath, "1/2 金币 3\r\n",
            HUtil32.GbkEncoding);
        var lines = source.LoadLines(rawName);
        Equal(1, lines.Count, "GBK file line count");
        Equal("1/2 金币 3", lines[0], "GBK file content");

        var rawTrail = new byte[] { 0x81, (byte)'A', (byte)'X' };
        var foldedTrail = new byte[] { 0x81, (byte)'a', (byte)'x' };
        var expectedTrailPath = Path.Combine(monItems,
            HUtil32.GbkEncoding.GetString(foldedTrail) + ".txt");
        Equal(expectedTrailPath, source.ResolvePath(rawTrail),
            "GBK trail byte in A..Z is folded before decoding");
        Equal(monItems + Path.DirectorySeparatorChar + "\\outside.txt",
            source.ResolvePath(Bytes("\\outside")),
            "root marker does not reset the MonItems directory");
        Equal(monItems + Path.DirectorySeparatorChar + "c:\\outside.txt",
            source.ResolvePath(Bytes("C:\\outside")),
            "drive marker remains a directly appended native name");

        var bomPath = Path.Combine(monItems, "bom.txt");
        var rawBomLine = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(HUtil32.GbkEncoding.GetBytes("1/2 Blade 3"))
            .ToArray();
        File.WriteAllBytes(bomPath, rawBomLine
            .Concat(new byte[] { 0x0D, 0x0A }).ToArray());
        var bomLines = source.LoadLines(Bytes("BOM"));
        Equal(1, bomLines.Count, "raw BOM line count");
        Equal(HUtil32.GbkEncoding.GetString(rawBomLine), bomLines[0],
            "ANSI reader does not detect and strip a UTF-8 BOM");
        Equal(0, source.LoadLines(Bytes("Missing")).Count,
            "missing file returns a non-null empty list");
    }
    finally
    {
        Directory.Delete(root, true);
    }
}

static void CheckFailureGuards()
{
    var items = CreateStandardItems();
    var definitions = CreateFieldHeroCatalog(items,
        CreateFieldHeroBody(Bytes("GuardHero")));
    var adapter = new NativeType2FieldHeroRuntimeCatalogAdapter();
    ExpectThrows<ArgumentNullException>(() =>
            adapter.Publish(definitions, items, null),
        "null MonItems source rejected");
    Check(!adapter.Ready, "null source does not publish");

    var nullSource = new NullMonItemsSource();
    ExpectThrows<InvalidDataException>(() =>
            adapter.Publish(definitions, items, nullSource),
        "null line collection rejected");
    Check(!adapter.Ready && adapter.Generation == 0,
        "failed source leaves publication untouched");

    ExpectThrows<ArgumentNullException>(() =>
            NativeFieldHeroMonItemsParser.Parse(null, _ => null),
        "null parser lines rejected");
    ExpectThrows<ArgumentNullException>(() =>
            NativeFieldHeroMonItemsParser.Parse(Array.Empty<string>(), null),
        "null item resolver rejected");
}

static NativeType2FieldHeroMaterialization Materialize(
    NativeType2FieldHeroRuntimeCatalogAdapter adapter, string name)
{
    if (!adapter.TryResolveTemplate(name, out var template))
        throw new InvalidOperationException("missing template " + name);
    return template.CaptureSelectionAfterPlacement(null)
        .MaterializeEquipment();
}

static void CheckDrop(NativeFieldHeroRuntimeDropBinding drop,
    int selectionPoint, int maximumPoint, ushort wireIndex, int count,
    string recordName, string description)
{
    Equal(selectionPoint, drop.SelectionPoint,
        description + " selection point");
    Equal(maximumPoint, drop.MaximumPoint,
        description + " maximum point");
    Equal(wireIndex, drop.NativeWireIndex,
        description + " native wire index");
    Equal(count, drop.Count, description + " count");
    Equal(recordName, drop.RecordName, description + " record name");
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

static byte[] CreateFieldHeroBody(byte[] name)
{
    var body = new byte[NativeType2FieldHeroSnapshotState.BodySize];
    WriteShortString(body, 0x00, name);
    return body;
}

static byte[] CreatePacket(ushort command, int headerSize, byte[] body,
    bool completed)
{
    var packet = new byte[headerSize + body.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(packet, command);
    if (completed)
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(0x08, 4), 1);
    body.CopyTo(packet, headerSize);
    return packet;
}

static void WriteShortString(byte[] destination, int offset, byte[] value)
{
    destination[offset] = checked((byte)value.Length);
    value.CopyTo(destination, offset + 1);
}

static void WriteUInt16(byte[] destination, int offset, ushort value) =>
    BinaryPrimitives.WriteUInt16LittleEndian(
        destination.AsSpan(offset, sizeof(ushort)), value);

static byte[] Bytes(string value) => Encoding.ASCII.GetBytes(value);

static void Equal<T>(T expected, T actual, string description)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            $"{description}: expected={expected}, actual={actual}");
    }
}

static void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
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

sealed class MemoryMonItemsSource : INativeFieldHeroMonItemsSource
{
    private readonly Dictionary<string, IReadOnlyList<string>> _lines =
        new(StringComparer.Ordinal);

    public List<string> LoadedDefinitionNames { get; } = new();

    public void Set(string foldedName, IReadOnlyList<string> lines) =>
        _lines[foldedName] = lines;

    public IReadOnlyList<string> LoadLines(byte[] definitionNameBytes)
    {
        LoadedDefinitionNames.Add(
            HUtil32.GbkEncoding.GetString(definitionNameBytes));
        var folded = NativeFieldHeroFactoryPreflight.CanonicalizeLookupName(
            definitionNameBytes);
        var key = HUtil32.GbkEncoding.GetString(folded);
        return _lines.TryGetValue(key, out var lines)
            ? lines
            : Array.Empty<string>();
    }
}

sealed class NullMonItemsSource : INativeFieldHeroMonItemsSource
{
    public IReadOnlyList<string> LoadLines(byte[] definitionNameBytes) => null;
}
