using System.Reflection;
using System.Runtime.CompilerServices;
using GameSvr;
using GameSvr.Services;

PrepareRuntimeConfig();
VerifyTableParsingAndMath();
VerifyMissingFileBehavior();
VerifyDetachedActorReducers();

Console.WriteLine(
    "PASS native-fastness-magic-soul tables=space+tab+last-wins+exact-key+positive-cap " +
    "math=truncate+signed-min+unchecked reducers=classifier-gates+magic-categories1-3+skill22-excluded+soul-category5 missing-files=identity");
return;

static void PrepareRuntimeConfig()
{
    string runtimeDirectory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(runtimeDirectory, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "String.ini"),
        "[String]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "Command.conf"),
        "[Command]" + Environment.NewLine);
    string shareDirectory = Path.Combine(Path.GetFullPath(
        Path.Combine(runtimeDirectory, "..")), "Share");
    Directory.CreateDirectory(shareDirectory);
    File.WriteAllText(Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"),
        "[Integer]" + Environment.NewLine);
}

static void VerifyTableParsingAndMath()
{
    using var directory = TempDirectory.Create();
    var file = Path.Combine(directory.Path, "FASTNESS_MAGIC.txt");
    File.WriteAllText(file,
        "# comment\n" +
        "; comment\n" +
        "-2\t0.25\t-25\n" +
        "1 0.25 100\n" +
        "2\t0.3333333333333333\t100\n" +
        "4 0.50 100\n" +
        "4 0.07 100\n" +
        "6 -0.50 100\n" +
        "0 9 9\n" +
        "bad 9 9\n");

    var table = new NativeFastnessTable();
    Assert(table.Load(file), "table load");
    Equal(5, table.Count, "parsed entry count");
    Equal(6, table.MaximumPositiveKey, "maximum positive key");
    Equal(76, table.ApplyReduction(101, 1),
        "positive truncation");
    Equal(-76, table.ApplyReduction(-101, 1),
        "negative truncation toward zero");
    Equal(68, table.ApplyReduction(101, 2),
        "repeating ratio truncation");
    Equal(93, table.ApplyReduction(100, 4),
        "duplicate key last wins");
    Equal(151, table.ApplyReduction(101, 99),
        "selector above positive maximum caps to maximum key");
    Equal(125, table.ApplyReduction(100, -2),
        "signed minimum reduction");
    Equal(100, table.ApplyReduction(100, 3),
        "missing exact key remains identity");

    Assert(table.TryResolve(99, out var ratio, out var limit),
        "capped selector resolution");
    Equal(-0.5, ratio, "capped selector ratio");
    Equal(100, limit, "capped selector limit");
}

static void VerifyMissingFileBehavior()
{
    using var directory = TempDirectory.Create();
    var table = new NativeFastnessTable();
    var existing = Path.Combine(directory.Path, "FASTNESS_SOUL.txt");
    File.WriteAllText(existing, "1 0.25 100");
    Assert(table.Load(existing), "existing table load");

    var missing = Path.Combine(directory.Path, "missing.txt");
    Assert(!table.Load(missing), "missing table result");
    Equal(1, table.Count, "missing table preserves prior entries");
    Equal(75, table.ApplyReduction(100, 1),
        "missing table preserves prior behavior");

    var empty = new NativeFastnessTable();
    Equal(100, empty.ApplyReduction(100, 1),
        "unloaded table identity");
}

static void VerifyDetachedActorReducers()
{
    var magic = LoadTable("1 0.25 100");
    var soul = LoadTable("2 0.50 100");
    M2Share.NativeFastnessMagicTable = magic;
    M2Share.NativeFastnessSoulTable = soul;

    var actor = (TBaseObject)RuntimeHelpers.GetUninitializedObject(
        typeof(TBaseObject));
    SetField(actor, "m_nNativeMagicFastnessSelector", 1);
    SetField(actor, "m_nNativeSoulFastnessSelector", 2);

    Equal(75, Apply(actor, 7, 1, false, false, false, 100),
        "magic category1");
    Equal(75, Apply(actor, 7, 2, false, false, false, 100),
        "magic category2");
    Equal(75, Apply(actor, 7, 3, false, false, false, 100),
        "magic category3");
    Equal(75, Apply(actor, 7, 257, false, false, false, 100),
        "magic category low-byte coercion");
    Equal(100, Apply(actor, 22, 1, false, false, false, 100),
        "magic skill22 excluded");
    Equal(100, Apply(actor, 7, 1, true, false, false, 100),
        "first classifier excluded");
    Equal(100, Apply(actor, 7, 1, false, true, false, 100),
        "second classifier excluded");
    Equal(100, Apply(actor, 7, 1, false, false, true, 100),
        "third classifier excluded");
    Equal(50, Apply(actor, 7, 5, false, false, false, 100),
        "soul category5");
    Equal(50, Apply(actor, 22, 5, false, false, false, 100),
        "soul category5 has no skill22 exclusion");
    Equal(100, Apply(actor, 7, 6, false, false, false, 100),
        "unhandled category identity");

    M2Share.NativeFastnessMagicTable = null;
    M2Share.NativeFastnessSoulTable = null;
    Equal(100, Apply(actor, 7, 1, false, false, false, 100),
        "null magic table identity");
    Equal(100, Apply(actor, 7, 5, false, false, false, 100),
        "null soul table identity");
}

static NativeFastnessTable LoadTable(string contents)
{
    using var directory = TempDirectory.Create();
    var path = Path.Combine(directory.Path, "table.txt");
    File.WriteAllText(path, contents);
    var table = new NativeFastnessTable();
    Assert(table.Load(path), "fixture table load");
    return table;
}

static int Apply(TBaseObject actor, int skillId, int category,
    bool firstClassifier, bool secondClassifier, bool thirdClassifier,
    int damage)
{
    var method = typeof(TBaseObject).GetMethod(
        "ApplyNativeGeneralFastnessReduction",
        BindingFlags.Instance | BindingFlags.NonPublic) ??
        throw new InvalidOperationException("reducer method missing");
    return (int)method.Invoke(actor, new object[]
    {
        skillId, category, firstClassifier, secondClassifier,
        thirdClassifier, damage
    });
}

static void SetField<T>(TBaseObject actor, string name, T value)
{
    var field = typeof(TBaseObject).GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic) ??
        throw new InvalidOperationException($"field missing: {name}");
    field.SetValue(actor, value);
}

static void Equal<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            $"{name}: expected={expected}, actual={actual}");
    }
}

static void Assert(bool condition, string name)
{
    if (!condition)
        throw new InvalidOperationException(name);
}

sealed class TempDirectory : IDisposable
{
    private TempDirectory(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public static TempDirectory Create()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "NativeFastnessMagicSoulCompatCheck",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return new TempDirectory(path);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, true);
    }
}
