using System.Reflection;
using System.Runtime.CompilerServices;
using GameSvr;
using SystemModule;

PrepareRuntimeConfig();
InitializeRuntime();

VerifyEquipmentProjection();
VerifyBreakthroughProc();
VerifyFixedReductionEntryAndOrder();
VerifyFixedReductionThresholds();
VerifyNativeClassifierBoundaries();
VerifyAwakeningGatesAndClassifiers();
VerifyAwakeningThresholdsAndTruncation();
VerifyCombinedRandomSequence();
VerifySourceContract();

Console.WriteLine(
    "PASS native-general-magic-carriers properties=53+67+70+71+78+79+90 " +
    "breakthrough=always-rng+cap600+flags5 " +
    "fixed=entry-positive+30/40/50-rng+continued-zero+signed-golden-bell " +
    "awakening=classifier-gates+ten-tiers+truncate-toward-zero resolver=connected");
return;

static void VerifyEquipmentProjection()
{
    ResetItems();
    AddStdItem((53, 65535), (67, 65535), (70, 0), (71, 65535),
        (78, 65535), (90, 255));
    AddStdItem((53, 2), (67, 2), (71, 2), (78, 2), (79, 65535),
        (90, 2));
    AddStdItem((79, 2));

    var player = NewPlayer("general-carrier-player");
    player.m_UseItems[0] = Item(1);
    player.m_UseItems[1] = Item(2);
    player.m_UseItems[2] = Item(3);
    player.RecalcAbilitys();

    Equal((ushort)1, player.m_AddAbil.NativeBreakThroughChance,
        "property53 ushort accumulation");
    Equal((ushort)1, player.m_AddAbil.NativeSteelBodyReduction,
        "property67 ushort accumulation");
    Equal(true, player.m_AddAbil.NativeAwakening,
        "property70 ignored its zero value");
    Equal(65537, player.m_AddAbil.NativeFlatMagicDamageIncrease,
        "property71 Int32 accumulation");
    Equal((ushort)1, player.m_AddAbil.NativeGoldenBellReduction,
        "property78 ushort accumulation");
    Equal(65537, player.m_AddAbil.NativeDragonBodyReduction,
        "property79 Int32 accumulation");
    Equal((byte)1, player.m_AddAbil.NativeDamageIncreasePercent,
        "property90 byte accumulation");

    Equal((ushort)1, GetField<ushort>(player,
        "m_wNativeBreakThroughChance"), "property53 actor projection");
    Equal(1, GetField<int>(player, "m_nNativeSteelBodyReduction"),
        "property67 actor projection");
    Equal(true, GetField<bool>(player, "m_boNativeAwakening"),
        "property70 actor projection");
    Equal(65537, GetField<int>(player,
        "m_nNativeFlatMagicDamageIncrease"), "property71 actor projection");
    Equal(1, GetField<int>(player, "m_nNativeGoldenBellReduction"),
        "property78 actor projection");
    Equal(65537, GetField<int>(player, "m_nNativeDragonBodyReduction"),
        "property79 actor projection");
    Equal((byte)1, GetField<byte>(player,
        "m_btNativeDamageIncreasePercent"), "property90 actor projection");

    player.m_UseItems[0] = null;
    player.m_UseItems[1] = null;
    player.m_UseItems[2] = null;
    player.RecalcAbilitys();
    Equal((ushort)0, GetField<ushort>(player,
        "m_wNativeBreakThroughChance"), "property53 removal reset");
    Equal(0, GetField<int>(player, "m_nNativeSteelBodyReduction"),
        "property67 removal reset");
    Equal(false, GetField<bool>(player, "m_boNativeAwakening"),
        "property70 removal reset");
    Equal(0, GetField<int>(player, "m_nNativeFlatMagicDamageIncrease"),
        "property71 removal reset");
    Equal(0, GetField<int>(player, "m_nNativeGoldenBellReduction"),
        "property78 removal reset");
    Equal(0, GetField<int>(player, "m_nNativeDragonBodyReduction"),
        "property79 removal reset");
    Equal((byte)0, GetField<byte>(player,
        "m_btNativeDamageIncreasePercent"), "property90 removal reset");
}

static void VerifyBreakthroughProc()
{
    var source = NewActor();

    SetField(source, "m_wNativeBreakThroughChance", (ushort)0);
    var random = UseRandom(0);
    Equal(2, ApplyBreakthrough(source, 2),
        "zero breakthrough chance changed flags");
    EqualSequence(new[] { 1000 }, random.MaxValues,
        "zero breakthrough chance RNG sequence");

    SetField(source, "m_wNativeBreakThroughChance", (ushort)600);
    random = UseRandom(599);
    Equal(7, ApplyBreakthrough(source, 2),
        "breakthrough success did not OR flags with five");
    EqualSequence(new[] { 1000 }, random.MaxValues,
        "breakthrough success RNG sequence");

    random = UseRandom(600);
    Equal(2, ApplyBreakthrough(source, 2),
        "breakthrough accepted roll equal to chance");

    SetField(source, "m_wNativeBreakThroughChance", (ushort)65535);
    random = UseRandom(599);
    Equal(5, ApplyBreakthrough(source, 0),
        "breakthrough cap rejected roll 599");
    random = UseRandom(600);
    Equal(0, ApplyBreakthrough(source, 0),
        "breakthrough cap exceeded 600");
}

static void VerifyFixedReductionEntryAndOrder()
{
    var target = NewActor();
    SetReductionFields(target, 10, -4, 2);

    var random = UseRandom();
    Equal(0, ApplyFixedReductions(target, 0),
        "zero damage entry changed result");
    Equal(-7, ApplyFixedReductions(target, -7),
        "negative damage entry changed result");
    Equal(0, random.Calls, "nonpositive entry consumed RNG");

    // Iron bone first zeros the hit; negative golden bell must still raise it
    // to four, then dragon protection reduces that result to two.
    random = UseRandom(0, 0);
    Equal(2, ApplyFixedReductions(target, 10),
        "fixed reducer continuation/order mismatch");
    EqualSequence(new[] { 100, 100 }, random.MaxValues,
        "fixed reducer continuation RNG sequence");

    SetReductionFields(target, -100, -3, 0);
    random = UseRandom(39);
    Equal(13, ApplyFixedReductions(target, 10),
        "negative golden bell did not increase damage");
    EqualSequence(new[] { 100 }, random.MaxValues,
        "nonpositive iron bone consumed RNG");

    SetReductionFields(target, 0, 0, 4);
    random = UseRandom(99);
    Equal(6, ApplyFixedReductions(target, 10),
        "dragon protection fixed reduction mismatch");
    EqualSequence(new[] { 100 }, random.MaxValues,
        "dragon protection added RNG or golden bell omitted RNG");

    SetReductionFields(target, 0, 0, 10);
    random = UseRandom(99);
    Equal(0, ApplyFixedReductions(target, 10),
        "dragon protection saturation mismatch");
}

static void VerifyFixedReductionThresholds()
{
    var target = NewActor();

    SetReductionFields(target, 3, 0, 0);
    var random = UseRandom(29, 99);
    Equal(7, ApplyFixedReductions(target, 10),
        "iron bone roll 29 did not hit");
    random = UseRandom(30, 99);
    Equal(10, ApplyFixedReductions(target, 10),
        "iron bone roll 30 hit");

    SetReductionFields(target, 0, 2999, 0);
    random = UseRandom(39);
    Equal(2001, ApplyFixedReductions(target, 5000),
        "golden bell value 2999 roll 39 did not hit");
    random = UseRandom(40);
    Equal(5000, ApplyFixedReductions(target, 5000),
        "golden bell value 2999 roll 40 hit");

    SetReductionFields(target, 0, 3000, 0);
    random = UseRandom(49);
    Equal(1000, ApplyFixedReductions(target, 4000),
        "golden bell value 3000 roll 49 did not hit");
    random = UseRandom(50);
    Equal(4000, ApplyFixedReductions(target, 4000),
        "golden bell value 3000 roll 50 hit");
}

static void VerifyAwakeningGatesAndClassifiers()
{
    var source = NewActor();

    SetField(source, "m_boNativeAwakening", false);
    var random = UseRandom();
    Equal(9, ApplyAwakening(source, 1, true, 9),
        "inactive awakening changed damage");
    Equal(0, random.Calls, "inactive awakening consumed RNG");

    SetField(source, "m_boNativeAwakening", true);
    random = UseRandom();
    Equal(9, ApplyAwakening(source, 1, false, 9),
        "arg0 false awakening changed damage");
    Equal(0, random.Calls, "arg0 false awakening consumed RNG");

    foreach (int skillId in new[] { 50, 55, 70, 99, 300, 302, 3071, 3118 })
    {
        random = UseRandom();
        Equal(9, ApplyAwakening(source, skillId, true, 9),
            $"excluded skill {skillId} changed damage");
        Equal(0, random.Calls,
            $"excluded skill {skillId} consumed RNG");
    }

    foreach (int skillId in new[] { 49, 56, 69, 100, 299, 303, 3070, 3119 })
    {
        random = UseRandom(9999);
        Equal(3, ApplyAwakening(source, skillId, true, 2),
            $"adjacent skill {skillId} was incorrectly excluded");
        EqualSequence(new[] { 10000 }, random.MaxValues,
            $"adjacent skill {skillId} RNG sequence");
    }
}

static void VerifyNativeClassifierBoundaries()
{
    for (int id = 0; id <= ushort.MaxValue; id++)
    {
        bool expectedFirst = id is >= 70 and <= 99 or
            >= 3071 and <= 3118;
        bool expectedSecond = id is >= 50 and <= 55 or
            >= 300 and <= 302;
        bool expectedThird = id is >= 116 and <= 118 or
            >= 125 and <= 127 or >= 129 and <= 131 or 270;
        Equal(expectedFirst, InvokeClassifier(
            "IsNativeMagicFirstClassifier", id),
            $"first classifier id {id}");
        Equal(expectedSecond, InvokeClassifier(
            "IsNativeMagicSecondClassifier", id),
            $"second classifier id {id}");
        Equal(expectedThird, InvokeClassifier(
            "IsNativeMagicThirdClassifier", id),
            $"third classifier id {id}");
    }

    Equal(true, InvokeClassifier("IsNativeMagicFirstClassifier",
        65536 + 70), "first classifier ushort input coercion");
    Equal(true, InvokeClassifier("IsNativeMagicSecondClassifier",
        -65536 + 50), "second classifier ushort input coercion");
    Equal(true, InvokeClassifier("IsNativeMagicThirdClassifier",
        65536 + 270), "third classifier ushort input coercion");
}

static void VerifyAwakeningThresholdsAndTruncation()
{
    var source = NewActor();
    SetField(source, "m_boNativeAwakening", true);

    var cases = new (int Roll, int Expected)[]
    {
        (49, 21), (50, 19), (109, 19), (110, 17),
        (189, 17), (190, 15), (289, 15), (290, 13),
        (439, 13), (440, 11), (639, 11), (640, 9),
        (939, 9), (940, 7), (1439, 7), (1440, 5),
        (5939, 5), (5940, 3), (9999, 3)
    };
    foreach (var item in cases)
    {
        var random = UseRandom(item.Roll);
        Equal(item.Expected, ApplyAwakening(source, 1, true, 2),
            $"awakening tier roll {item.Roll}");
        EqualSequence(new[] { 10000 }, random.MaxValues,
            $"awakening tier roll {item.Roll} RNG sequence");
    }

    UseRandom(9999);
    Equal(4, ApplyAwakening(source, 1, true, 3),
        "positive awakening did not truncate toward zero");
    UseRandom(9999);
    Equal(-4, ApplyAwakening(source, 1, true, -3),
        "negative awakening did not truncate toward zero");
    UseRandom(9999);
    Equal(25165825, ApplyAwakening(source, 1, true, 16777217),
        "awakening prematurely rounded damage through Single");
}

static void VerifyCombinedRandomSequence()
{
    var source = NewActor();
    var target = NewActor();
    SetField(source, "m_wNativeBreakThroughChance", (ushort)1);
    SetField(source, "m_boNativeAwakening", true);
    SetReductionFields(target, 1, 1, 0);

    var random = UseRandom(999, 99, 99, 9999);
    Equal(0, ApplyBreakthrough(source, 0),
        "combined breakthrough mismatch");
    Equal(10, ApplyFixedReductions(target, 10),
        "combined fixed reduction mismatch");
    Equal(15, ApplyAwakening(source, 1, true, 10),
        "combined awakening mismatch");
    EqualSequence(new[] { 1000, 100, 100, 10000 }, random.MaxValues,
        "combined RNG order");
}

static void VerifySourceContract()
{
    string root = FindRepoRoot();
    string carrierPath = Path.Combine(root, "GameSvr", "Actors",
        "TBaseObject.NativeGeneralMagicCarriers.cs");
    string source = File.ReadAllText(carrierPath);

    Contains(source, "Math.Min(", "breakthrough cap missing");
    Contains(source, "m_wNativeBreakThroughChance, 600",
        "breakthrough cap value missing");
    Contains(source, "flags |= 5", "breakthrough flag mask missing");
    Contains(source, "goldenBell < 3000 ? 40 : 50",
        "golden bell split missing");
    Contains(source, "Math.Truncate", "awakening truncation missing");
    Contains(source, "IsNativeMagicFirstClassifier(skillId)",
        "first awakening classifier missing");
    Contains(source, "IsNativeMagicSecondClassifier(skillId)",
        "second awakening classifier missing");
    Assert(Count(source, "M2Share.RandomNumber.Random(") == 4,
        "carrier production RNG call-site count changed");
    Assert(!source.Contains("DelphiRandom", StringComparison.Ordinal),
        "carrier connected dormant Delphi RNG");

    string resolver = File.ReadAllText(Path.Combine(root, "GameSvr", "Actors",
        "TBaseObject.NativeMagicDamage.cs"));
    int breakthrough = resolver.IndexOf("ApplyNativeMagicBreakthrough",
        StringComparison.Ordinal);
    int fixedReductions = resolver.IndexOf("ApplyNativeFixedMagicReductions",
        StringComparison.Ordinal);
    int awakening = resolver.IndexOf("ApplyNativeMagicAwakening",
        StringComparison.Ordinal);
    Assert(breakthrough >= 0 && fixedReductions > breakthrough &&
        awakening > fixedReductions,
        "general carrier resolver order differs from sub_76CFC4");
}

static int ApplyBreakthrough(TBaseObject actor, int flags) =>
    Invoke<int>(actor, "ApplyNativeMagicBreakthrough", flags);

static int ApplyFixedReductions(TBaseObject actor, int damage) =>
    Invoke<int>(actor, "ApplyNativeFixedMagicReductions", damage);

static int ApplyAwakening(TBaseObject actor, int skillId, bool arg0,
    int damage) => Invoke<int>(actor, "ApplyNativeMagicAwakening",
        skillId, arg0, damage);

static bool InvokeClassifier(string name, int skillId)
{
    MethodInfo method = typeof(TBaseObject).GetMethod(name,
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(TBaseObject).FullName,
            name);
    return (bool)(method.Invoke(null, new object[] { skillId })
        ?? throw new InvalidOperationException($"{name} returned null"));
}

static T Invoke<T>(TBaseObject actor, string name, params object[] args)
{
    MethodInfo method = typeof(TBaseObject).GetMethod(name,
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(TBaseObject).FullName, name);
    return (T)(method.Invoke(actor, args)
        ?? throw new InvalidOperationException($"{name} returned null"));
}

static void SetReductionFields(TBaseObject actor, int ironBone,
    int goldenBell, int dragonProtection)
{
    SetField(actor, "m_nNativeSteelBodyReduction", ironBone);
    SetField(actor, "m_nNativeGoldenBellReduction", goldenBell);
    SetField(actor, "m_nNativeDragonBodyReduction", dragonProtection);
}

static void SetField(TBaseObject actor, string name, object value) =>
    (typeof(TBaseObject).GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(TBaseObject).FullName, name))
    .SetValue(actor, value);

static T GetField<T>(TBaseObject actor, string name) =>
    (T)(typeof(TBaseObject).GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(actor)
        ?? throw new MissingFieldException(typeof(TBaseObject).FullName,
            name));

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
        Name = "general-magic-carrier-effect",
        ItemType = GoodType.ITEM_ETC
    };
    for (int index = 0; index < properties.Length; index++)
    {
        item.NativeItemExtAbilIdents[index] = properties[index].Id;
        item.NativeItemExtAbilValues[index] = properties[index].Value;
    }
    M2Share.UserEngine.StdItemList.Add(item);
}

static void ResetItems() => M2Share.UserEngine.StdItemList.Clear();

static TBaseObject NewActor() =>
    (TBaseObject)RuntimeHelpers.GetUninitializedObject(typeof(TBaseObject));

// The recorder is installed on M2Share.RandomNumber, the field the server
// assigns at startup. It used to be installed on RandomNumber's private
// `random` field, which POIS-26 removed when the facade moved onto the Delphi
// LCG sub_403B4C (@0x403B4C imul [0x7A2008],0x08088405 / inc / mul / take EDX);
// GetField then returned null and every carrier draw assertion below stopped
// running. The expected values, bounds and call counts are unchanged.
static FixedRandom UseRandom(params int[] values)
{
    var random = new FixedRandom(values);
    M2Share.RandomNumber = random;
    return random;
}

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

static int Count(string text, string value)
{
    int count = 0;
    int index = 0;
    while ((index = text.IndexOf(value, index,
        StringComparison.Ordinal)) >= 0)
    {
        count++;
        index += value.Length;
    }
    return count;
}

static void Contains(string text, string value, string message) =>
    Assert(text.Contains(value, StringComparison.Ordinal), message);

static void Equal<T>(T expected, T actual, string message)
    where T : IEquatable<T>
{
    if (!expected.Equals(actual))
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
}

static void EqualSequence(IReadOnlyList<int> expected,
    IReadOnlyList<int> actual, string message)
{
    if (!expected.SequenceEqual(actual))
    {
        throw new InvalidOperationException(
            $"{message}: expected [{string.Join(',', expected)}], " +
            $"actual [{string.Join(',', actual)}]");
    }
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

    internal List<int> MaxValues { get; } = new();
    internal int Calls => MaxValues.Count;

    public override int Random(int Value)
    {
        MaxValues.Add(Value);
        if (_values.Count == 0)
            throw new InvalidOperationException("unexpected RNG call");
        int value = _values.Dequeue();
        if (value < 0 || value >= Value)
            throw new ArgumentOutOfRangeException(nameof(value));
        return value;
    }

    // The three carriers draw only through the bounded entry. A parameterless
    // advance or a min/max draw would be a new call the Calls/MaxValues
    // assertions cannot see, so refuse instead of absorbing it.
    public override int Random() => throw new InvalidOperationException(
        "unexpected parameterless RandSeed advance");

    public override int Random(int minValue, int maxValue) =>
        throw new InvalidOperationException("unexpected Random(min,max) draw");

    public override int GetRandomNumber(int minValue, int maxValue) =>
        throw new InvalidOperationException("unexpected GetRandomNumber draw");
}
