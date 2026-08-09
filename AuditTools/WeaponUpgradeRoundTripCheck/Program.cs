using System.Reflection;
using System.Runtime.Loader;

if (args.Length != 2)
{
    throw new ArgumentException("Usage: WeaponUpgradeRoundTripCheck <GameSvr build> <connection string>");
}

var gameDirectory = Path.GetFullPath(args[0]);
var connectionString = args[1];
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var dependency = Path.Combine(gameDirectory, $"{name.Name}.dll");
    return File.Exists(dependency)
        ? AssemblyLoadContext.Default.LoadFromAssemblyPath(dependency)
        : null;
};

var systemModule = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameDirectory, "SystemModule.dll"));
var gameSvr = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameDirectory, "GameSvr.dll"));
var itemType = systemModule.GetType("SystemModule.TUserItem", throwOnError: true)!;
var codecType = gameSvr.GetType("GameSvr.LegacyUserItem208Codec", throwOnError: true)!;
var repositoryType = gameSvr.GetType("GameSvr.WeaponUpgradeRepository", throwOnError: true)!;
var recordType = gameSvr.GetType("GameSvr.WeaponUpgradeRecord", throwOnError: true)!;
var repository = Activator.CreateInstance(repositoryType,
    BindingFlags.Instance | BindingFlags.NonPublic, null, new object?[] { connectionString }, null)!;
var hasPending = repositoryType.GetMethod("HasPending", BindingFlags.Instance | BindingFlags.NonPublic)!;
var insert = repositoryType.GetMethod("Insert", BindingFlags.Instance | BindingFlags.NonPublic)!;
var get = repositoryType.GetMethod("GetByCharacter", BindingFlags.Instance | BindingFlags.NonPublic)!;
var delete = repositoryType.GetMethod("Delete", BindingFlags.Instance | BindingFlags.NonPublic)!;
var tryEncode = codecType.GetMethod("TryEncode", BindingFlags.Static | BindingFlags.NonPublic)!;
var tryDecode = codecType.GetMethod("TryDecode", BindingFlags.Static | BindingFlags.NonPublic)!;

var marker = "P5T" + DateTime.Now.ToString("yyMMddHHmmss");
var insertedIdx = 0;
try
{
    Assert(marker.Length == 15, "probe character name is not exactly 15 ASCII bytes");
    Assert(!(bool)hasPending.Invoke(repository, new object[] { marker })!, "probe marker already exists");

    var item = Activator.CreateInstance(itemType)!;
    SetField(itemType, item, "MakeIndex", unchecked((int)0xF1234567u));
    SetField(itemType, item, "wIndex", (ushort)5);
    SetField(itemType, item, "Dura", (ushort)5000);
    SetField(itemType, item, "DuraMax", (ushort)10000);
    SetField(itemType, item, "UpgradeFlags", (byte)0xC0);
    SetField(itemType, item, "Bind", (byte)1);
    ((byte[])GetField(itemType, item, "btValue"))[9] = 1;

    var unknownTailItem = Activator.CreateInstance(itemType)!;
    var unknownTail = new byte[208];
    unknownTail[0x40] = 1;
    SetField(itemType, unknownTailItem, "NativeRecord", unknownTail);
    var unknownTailArgs = new object?[] { unknownTailItem, null, null };
    Assert(!(bool)tryEncode.Invoke(null, unknownTailArgs)! &&
           unknownTailArgs[2]!.ToString()!.Contains("0x40", StringComparison.Ordinal),
        "unmapped native item tail was silently discarded");

    var encodeArgs = new object?[] { item, null, null };
    Assert((bool)tryEncode.Invoke(null, encodeArgs)!, $"encode failed: {encodeArgs[2]}");
    var weaponData = (string)encodeArgs[1]!;
    Assert(weaponData.Length == 416 && weaponData.All(ch => char.IsDigit(ch) || ch is >= 'A' and <= 'F'),
        "WeaponData is not 416-character uppercase HEX");
    Assert(Convert.FromHexString(weaponData)[0xB8] == 1,
        "WeaponData bind byte +0xB8 was not encoded");

    insertedIdx = (int)insert.Invoke(repository,
        new object[] { "P5PROBE", marker, item, (byte)1, (byte)2, (byte)3, (byte)4, (byte)5, weaponData })!;
    Assert(insertedIdx > 0, "native insert returned no idx");
    Assert((bool)hasPending.Invoke(repository, new object[] { marker })!, "inserted row is not queryable");

    var record = get.Invoke(repository, new object[] { marker })
        ?? throw new InvalidOperationException("inserted row was not returned");
    Assert((int)GetField(recordType, record, "Idx") == insertedIdx &&
           (int)GetField(recordType, record, "ItemIdx") == 5 &&
           (uint)GetField(recordType, record, "ItemId") == 0xF1234567u &&
           (byte)GetField(recordType, record, "UpDc") == 1 &&
           (byte)GetField(recordType, record, "UpSc") == 2 &&
           (byte)GetField(recordType, record, "UpMc") == 3 &&
           (byte)GetField(recordType, record, "UpCc") == 4 &&
           (byte)GetField(recordType, record, "UpDura") == 5 &&
           !(bool)GetField(recordType, record, "Built") &&
           (string)GetField(recordType, record, "WeaponData") == weaponData,
        "native row fields did not round-trip");

    var decodeArgs = new object?[] { weaponData, null, null };
    Assert((bool)tryDecode.Invoke(null, decodeArgs)!, $"decode failed: {decodeArgs[2]}");
    var decoded = decodeArgs[1]!;
    Assert(unchecked((uint)(int)GetField(itemType, decoded, "MakeIndex")) == 0xF1234567u &&
           (ushort)GetField(itemType, decoded, "wIndex") == 5 &&
           (byte)GetField(itemType, decoded, "UpgradeFlags") == 0xC0 &&
           (byte)GetField(itemType, decoded, "Bind") == 1 &&
           ((byte[])GetField(itemType, decoded, "btValue"))[9] == 1 &&
           ((byte[])GetField(itemType, decoded, "NativeRecord")).Length == 208,
        "decoded item fields did not round-trip");

    Assert((bool)delete.Invoke(repository, new object[] { insertedIdx })!, "native delete failed");
    insertedIdx = 0;
    Assert(get.Invoke(repository, new object[] { marker }) == null, "probe row remains after delete");
    Console.WriteLine($"PASS marker={marker} itemId=0xF1234567 hex={weaponData.Length} row=insert/get/decode/delete");
}
finally
{
    for (var attempt = 0; attempt < 3; attempt++)
    {
        var record = get.Invoke(repository, new object[] { marker });
        if (record == null) break;
        var idx = (int)GetField(recordType, record, "Idx");
        delete.Invoke(repository, new object[] { idx });
    }
    if (insertedIdx > 0)
    {
        delete.Invoke(repository, new object[] { insertedIdx });
    }
    Assert(get.Invoke(repository, new object[] { marker }) == null, "probe cleanup failed");
}

static object GetField(Type type, object instance, string name) =>
    type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(instance)!;

static void SetField(Type type, object instance, string name, object value) =>
    type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(instance, value);

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
