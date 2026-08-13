using System.Reflection;
using GameSvr;
using SystemModule;

PrepareRuntimeConfig();
InitializeRuntime();

ResetRandom();
VerifyEquipmentProjection();
ResetRandom();
VerifyNegativeCarriersSkipRandom();
ResetRandom();
VerifyThresholdAndRandomOrder();
ResetRandom();
VerifyCriticalDamageFormula();
ResetRandom();
VerifyResolverOrderAndIntegration();

Console.WriteLine(
    "PASS native-magic-critical properties=98-101+dword-accumulation+signed-lowword " +
    "gate=three-signed-carriers rng=valid-path-always+zero-threshold+less-or-equal " +
    "formula=x87-ties-to-even+native-reduction-order resolver=before-superforce");
return;

static void VerifyEquipmentProjection()
{
    ResetItems();
    AddStdItem((98, 30000), (99, 65535), (100, 120), (101, 130));
    AddStdItem((98, 3000), (99, 65535), (100, 65535), (101, 65535));

    var player = NewPlayer("critical-player");
    player.m_UseItems[0] = Item(1);
    player.m_UseItems[1] = Item(2);
    player.RecalcAbilitys();

    Equal(33000, player.m_AddAbil.NativeCriticalChance,
        "property98 dword accumulation");
    Equal(131070, player.m_AddAbil.NativeCriticalDamageIncrease,
        "property99 dword accumulation");
    Equal(65655, player.m_AddAbil.NativeAntiCriticalChance,
        "property100 dword accumulation");
    Equal(65665, player.m_AddAbil.NativeCriticalDamageReduction,
        "property101 dword accumulation");
    Equal(unchecked((short)33000), GetField<short>(player,
        "m_sNativeCriticalChance"), "property98 signed low-word projection");
    Equal(131070, GetField<int>(player,
        "m_nNativeCriticalDamageIncrease"), "property99 Int32 projection");
    Equal((short)119, GetField<short>(player,
        "m_sNativeAntiCriticalChance"), "property100 signed low-word projection");
    Equal((short)129, GetField<short>(player,
        "m_sNativeCriticalDamageReduction"),
        "property101 signed low-word projection");

    var hero = new HeroObject { m_sCharName = "critical-hero" };
    hero.m_UseItems[0] = Item(1);
    hero.RecalcAbilitys();
    Equal((short)30000, GetField<short>(hero,
        "m_sNativeCriticalChance"), "hero property98 projection");
    Equal(65535, GetField<int>(hero,
        "m_nNativeCriticalDamageIncrease"), "hero property99 projection");

    player.m_UseItems[0] = null;
    player.m_UseItems[1] = null;
    player.RecalcAbilitys();
    Equal((short)0, GetField<short>(player, "m_sNativeCriticalChance"),
        "removed property98 reset");
    Equal(0, GetField<int>(player, "m_nNativeCriticalDamageIncrease"),
        "removed property99 reset");
    Equal((short)0, GetField<short>(player, "m_sNativeAntiCriticalChance"),
        "removed property100 reset");
    Equal((short)0, GetField<short>(player,
        "m_sNativeCriticalDamageReduction"), "removed property101 reset");
}

static void VerifyNegativeCarriersSkipRandom()
{
    var source = new TBaseObject();
    var target = new TBaseObject();

    var random = UseRandom(0);
    Equal(37, ApplyCritical(target, null, 37), "null source identity");
    Equal(0, random.Calls, "null source consumed RNG");

    SetCriticalFields(source, -1, 0);
    SetCriticalFields(target, 0, 0, 0);
    random = UseRandom(0);
    Equal(37, ApplyCritical(target, source, 37),
        "negative source critical chance identity");
    Equal(0, random.Calls, "negative source critical chance consumed RNG");

    SetCriticalFields(source, 0, 0);
    SetCriticalFields(target, 0, 0, antiChance: -1);
    random = UseRandom(0);
    Equal(37, ApplyCritical(target, source, 37),
        "negative target anti-critical identity");
    Equal(0, random.Calls, "negative target anti-critical consumed RNG");

    SetCriticalFields(target, 0, 0, reduction: -1);
    random = UseRandom(0);
    Equal(37, ApplyCritical(target, source, 37),
        "negative target critical reduction identity");
    Equal(0, random.Calls, "negative target critical reduction consumed RNG");
}

static void VerifyThresholdAndRandomOrder()
{
    var source = new TBaseObject();
    var target = new TBaseObject();
    SetCriticalFields(target, 0, 0, 0);

    SetCriticalFields(source, 0, 0);
    var random = UseRandom(0);
    Equal(12, ApplyCritical(target, source, 8),
        "zero threshold roll zero must critically hit");
    Equal(1, random.Calls, "zero threshold did not consume exactly one RNG call");

    random = UseRandom(1);
    Equal(8, ApplyCritical(target, source, 8),
        "zero threshold roll one critically hit");
    Equal(1, random.Calls, "zero threshold miss RNG count");

    SetCriticalFields(source, 50, 0);
    SetCriticalFields(target, 0, 0, antiChance: 100);
    random = UseRandom(50);
    Equal(12, ApplyCritical(target, source, 8),
        "49.5 threshold did not round to even 50 or rejected equality");
    Equal(1, random.Calls, "49.5 threshold RNG count");

    SetCriticalFields(target, 0, 0, antiChance: 300);
    random = UseRandom(49);
    Equal(8, ApplyCritical(target, source, 8),
        "48.5 threshold did not round to even 48");
    Equal(1, random.Calls, "48.5 threshold miss RNG count");

    random = UseRandom(48);
    Equal(12, ApplyCritical(target, source, 8),
        "rounded threshold equality did not critically hit");

    SetCriticalFields(source, 12000, 0);
    SetCriticalFields(target, 0, 0, 0);
    random = UseRandom(9999);
    Equal(12, ApplyCritical(target, source, 8),
        "critical chance upper clamp did not yield guaranteed hit");

    SetCriticalFields(target, 0, 0, antiChance: 12000);
    random = UseRandom(1);
    Equal(8, ApplyCritical(target, source, 8),
        "anti-critical upper clamp did not reduce threshold to zero");
}

static void VerifyCriticalDamageFormula()
{
    var source = new TBaseObject();
    var target = new TBaseObject();
    SetCriticalFields(source, 10000, 0);
    SetCriticalFields(target, 0, 0, 0);

    UseRandom(9999);
    Equal(4, ApplyCritical(target, source, 3),
        "critical damage 4.5 ties-to-even");
    UseRandom(9999);
    Equal(8, ApplyCritical(target, source, 5),
        "critical damage 7.5 ties-to-even");

    SetCriticalFields(source, 10000, 5000);
    SetCriticalFields(target, 0, 0, reduction: 2000);
    UseRandom(9999);
    Equal(45, ApplyCritical(target, source, 25),
        "critical increase/reduction native order");

    SetCriticalFields(target, 0, 0, reduction: 10000);
    UseRandom(9999);
    Equal(25, ApplyCritical(target, source, 25),
        "critical reduction 10000 formula");

    SetCriticalFields(target, 0, 0, reduction: 12000);
    UseRandom(9999);
    Equal(25, ApplyCritical(target, source, 25),
        "critical reduction upper clamp");

    SetCriticalFields(source, 10000, -20000);
    SetCriticalFields(target, 0, 0, 0);
    UseRandom(9999);
    Equal(-5, ApplyCritical(target, source, 10),
        "critical damage increase signed Int32 or final clamp");
}

static void VerifyResolverOrderAndIntegration()
{
    var sourceText = File.ReadAllText(Path.Combine(FindRepoRoot(), "GameSvr",
        "Actors", "TBaseObject.NativeMagicDamage.cs"));
    int criticalIndex = sourceText.IndexOf(
        "damage = ApplyNativeMagicCritical(source, damage);",
        StringComparison.Ordinal);
    int superForceIndex = sourceText.IndexOf(
        "damage = ApplyStandardEarthFireSuperForce(source, damage);",
        StringComparison.Ordinal);
    Assert(criticalIndex >= 0 && superForceIndex > criticalIndex,
        "critical resolver order differs from sub_76CFC4");

    var source = new TBaseObject();
    SetCriticalFields(source, 10000, 0);
    var target = NewPlayer("critical-resolver-target");
    target.m_WAbil.MaxHP = 100;
    target.m_WAbil.HP = 100;
    target.m_WAbil.MP = 0;
    target.m_WAbil.MAC = 0;
    Assert(target.SetNativeActiveState(17), "state17 setup failed");
    SetCriticalFields(target, 0, 0, 0);

    var contextType = typeof(TBaseObject).Assembly.GetType(
        "GameSvr.MagicDamageContext")
        ?? throw new TypeLoadException("MagicDamageContext");
    var emptyContext = contextType.GetProperty("Empty",
        BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null)
        ?? throw new MissingMemberException("MagicDamageContext.Empty");
    var resolver = typeof(TBaseObject).GetMethod("ResolveFullMagicDamage",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("ResolveFullMagicDamage");
    var random = UseRandom(999, 99, 9999);
    var result = (int)(resolver.Invoke(target,
        new object[] { source, 22, false, emptyContext, (byte)1, 0, 10 })
        ?? throw new InvalidOperationException("resolver result"));
    Equal(15, result, "resolver critical result");
    Equal(85, target.m_WAbil.HP, "resolver critical landing");
    Equal(3, random.Calls, "resolver critical RNG count");
}

static int ApplyCritical(TBaseObject target, TBaseObject source, int damage)
{
    var method = typeof(TBaseObject).GetMethod("ApplyNativeMagicCritical",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("ApplyNativeMagicCritical");
    return (int)(method.Invoke(target, new object[] { source, damage })
        ?? throw new InvalidOperationException("critical result"));
}

static void SetCriticalFields(TBaseObject actor, short chance, int increase,
    short antiChance = 0, short reduction = 0)
{
    SetField(actor, "m_sNativeCriticalChance", chance);
    SetField(actor, "m_nNativeCriticalDamageIncrease", increase);
    SetField(actor, "m_sNativeAntiCriticalChance", antiChance);
    SetField(actor, "m_sNativeCriticalDamageReduction", reduction);
}

// The recorder rides M2Share.RandomNumber, the field the server assigns at
// startup. It used to ride RandomNumber's private `random` field, which
// POIS-26 removed when the facade moved onto the Delphi LCG sub_403B4C
// (@0x403B4C imul [0x7A2008],0x08088405 / inc / mul / take EDX); GetField then
// returned null and every threshold, formula and resolver-order assertion
// below stopped running. The expectations themselves are unchanged.
static FixedRandom UseRandom(params int[] values)
{
    var random = new FixedRandom(values);
    SetRandom(random);
    return random;
}

static void ResetRandom() => SetRandom(RandomNumber.GetInstance());

static void SetRandom(RandomNumber random) => M2Share.RandomNumber = random;

static T GetField<T>(TBaseObject actor, string name) =>
    (T)(typeof(TBaseObject).GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(actor)
        ?? throw new MissingFieldException(name));

static void SetField(TBaseObject actor, string name, object value) =>
    (typeof(TBaseObject).GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(name)).SetValue(actor, value);

static TPlayObject NewPlayer(string name) => new()
{
    m_boOffLineFlag = true,
    m_sCharName = name
};

static TUserItem Item(ushort index) => new()
{
    wIndex = index,
    Dura = 100,
    DuraMax = 100,
    NativeRecord = new byte[208]
};

static void AddStdItem(params (ushort Id, ushort Value)[] properties)
{
    var item = new GoodItem
    {
        Name = "critical-effect",
        ItemType = GoodType.ITEM_ETC
    };
    for (var index = 0; index < properties.Length; index++)
    {
        item.NativeItemExtAbilIdents[index] = properties[index].Id;
        item.NativeItemExtAbilValues[index] = properties[index].Value;
    }
    M2Share.UserEngine.StdItemList.Add(item);
}

static void ResetItems() => M2Share.UserEngine.StdItemList.Clear();

static void InitializeRuntime()
{
    M2Share.g_Config = new GameSvrConfig();
    M2Share.UserEngine = new UserEngine();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.MapManager = new MapManager();
    M2Share.RandomNumber = RandomNumber.GetInstance();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new System.Collections.ArrayList();
}

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
    foreach (string startPath in new[]
    {
        Directory.GetCurrentDirectory(),
        AppContext.BaseDirectory
    })
    {
        var directory = new DirectoryInfo(startPath);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "GameSvr")))
                return directory.FullName;
            directory = directory.Parent;
        }
    }
    throw new DirectoryNotFoundException("repository root");
}

static void Equal<T>(T expected, T actual, string message)
    where T : IEquatable<T>
{
    if (!expected.Equals(actual))
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class FixedRandom : RandomNumber
{
    private readonly Queue<int> _values;

    internal FixedRandom(IEnumerable<int> values)
    {
        _values = new Queue<int>(values);
    }

    internal int Calls { get; private set; }

    public override int Random(int Value)
    {
        Calls++;
        if (_values.Count == 0)
            throw new InvalidOperationException("unexpected RNG call");
        int value = _values.Dequeue();
        if (value < 0 || value >= Value)
            throw new ArgumentOutOfRangeException(nameof(value));
        return value;
    }

    // The critical roll and the resolver steps in front of it are all bounded
    // draws. A parameterless advance or a min/max draw would slip past the
    // Calls counter, so refuse it instead of counting nothing.
    public override int Random() => throw new InvalidOperationException(
        "unexpected parameterless RandSeed advance");

    public override int Random(int minValue, int maxValue) =>
        throw new InvalidOperationException("unexpected Random(min,max) draw");

    public override int GetRandomNumber(int minValue, int maxValue) =>
        throw new InvalidOperationException("unexpected GetRandomNumber draw");
}
