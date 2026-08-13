using System.Reflection;
using System.Runtime.CompilerServices;
using GameSvr;
using SystemModule;

PrepareRuntimeConfig();
InitializeRuntime();

VerifyLearnedMagicLookupOnly();
VerifyEffectiveLevelBoundaries();
VerifySkill307Scaling();
VerifySkill306CooldownProcAndState();
VerifySkill308LowHealthGate();
VerifySkill190OrderDivisorsAndStats();
VerifyHumanHqReducers();
VerifyState16Caps();
VerifySourceContract();

Console.WriteLine(
    "PASS native-human-magic-hooks learned-magic-only " +
    "skills=190+306+307+308 level=byte-wrap+train-cap " +
    "rng=ordered cooldown306=120000+proc-only state102=5/8/10s " +
    "hq=player+hero state16=exact-caps resolver=connected");
return;

static void VerifyLearnedMagicLookupOnly()
{
    var source = NewActor<TestPlayer>();
    var target = NewActor<TBaseObject>();
    target.m_WAbil.HP = 1;
    target.m_WAbil.MaxHP = 100;

    foreach (int id in new[] { 190, 306, 307, 308 })
    {
        var bagItem = new TUserItem { wIndex = (ushort)id };
        source.m_ItemList.Add(bagItem);
        source.m_UseItems[id - 190 & 3] =
            new TUserItem { wIndex = (ushort)id };
    }

    var random = UseRandom();
    Equal(101, Skill307(source, 101),
        "same-id items activated skill 307");
    Equal(false, Skill306(source, target, 120_000, 120_001),
        "same-id items activated skill 306");
    Equal(101, Skill308(source, target, 101),
        "same-id items activated skill 308");
    Equal(0, Skill190(source, target, 1, 1),
        "same-id items activated skill 190");
    Equal(0, random.Calls, "item-only path consumed RNG");

    source.m_MagicList.Add(Magic(307, 1, 3));
    Equal(106, Skill307(source, 101),
        "learned skill 307 did not activate");
}

static void VerifyEffectiveLevelBoundaries()
{
    Equal(2, EffectiveLevel(Magic(307, 250, 3, 8)),
        "level bonus byte wrap");
    Equal(3, EffectiveLevel(Magic(307, 2, 3, 2)),
        "training cap at three");
    Equal(0, EffectiveLevel(Magic(307, 255, 3, 1)),
        "level bonus wrap to zero");
    Equal(1, EffectiveLevel(Magic(307, 3, 1, 0)),
        "training cap at one");
}

static void VerifySkill307Scaling()
{
    var source = NewActor<TBaseObject>();

    source.m_MagicList.Add(Magic(307, 250, 3, 8));
    Equal(111, Skill307(source, 101),
        "skill 307 wrapped effective level two");
    Equal(-20, Skill307(source, -19),
        "skill 307 did not truncate negative damage toward zero");

    source.m_MagicList.Clear();
    source.m_MagicList.Add(Magic(307, 2, 3, 2));
    Equal(116, Skill307(source, 101),
        "skill 307 training cap level three");

    source.m_MagicList.Clear();
    source.m_MagicList.Add(Magic(307, 0, 3));
    Equal(101, Skill307(source, 101),
        "skill 307 accepted level zero");

    source.m_MagicList.Clear();
    source.m_MagicList.Add(Magic(307, 4, 4));
    Equal(101, Skill307(source, 101),
        "skill 307 accepted level four");
}

static void VerifySkill306CooldownProcAndState()
{
    var source = NewActor<TBaseObject>();
    var target = NewActor<TBaseObject>();
    source.m_MagicList.Add(Magic(306, 2, 3, 2));
    source.m_btJob = 0;

    var random = UseRandom();
    Equal(true, Skill306(source, target, 119_999, 999),
        "valid skill 306 cooldown path returned false");
    Equal(0, random.Calls, "skill 306 cooldown consumed RNG");
    Equal(0, GetField<int>(source, "m_dwNativeSkill306ProcTick"),
        "skill 306 cooldown changed proc tick");

    random = UseRandom(12);
    Equal(true, Skill306(source, target, 120_000, 999),
        "valid skill 306 failed-roll path returned false");
    EqualSequence(new[] { 100 }, random.MaxValues,
        "skill 306 failed-roll RNG sequence");
    Equal(0, GetField<int>(source, "m_dwNativeSkill306ProcTick"),
        "skill 306 failed roll changed proc tick");
    Equal(false, target.HasNativeActiveState(102),
        "skill 306 failed roll added state 102");

    random = UseRandom(11);
    Equal(true, Skill306(source, target, 120_001, 120_009),
        "skill 306 successful proc returned false");
    Equal(true, target.HasNativeActiveState(102),
        "skill 306 did not add state 102");
    Equal(10_000, TimedRemaining(target, 102),
        "skill 306 level-three duration");
    Equal(120_009, GetField<int>(source, "m_dwNativeSkill306ProcTick"),
        "skill 306 did not store successful-proc tick");

    random = UseRandom();
    Equal(true, Skill306(source, target, 240_008, 999),
        "skill 306 second cooldown returned false");
    Equal(0, random.Calls, "skill 306 second cooldown consumed RNG");

    SetField(source, "m_dwNativeSkill306ProcTick", int.MaxValue - 10);
    random = UseRandom(99);
    Equal(true, Skill306(source, target,
        unchecked(int.MinValue + 120_000), 17),
        "skill 306 unsigned-wrap cooldown returned false");
    EqualSequence(new[] { 100 }, random.MaxValues,
        "skill 306 unsigned-wrap cooldown did not expire");

    source.m_MagicList.Clear();
    source.m_MagicList.Add(Magic(306, 0, 3));
    random = UseRandom();
    Equal(false, Skill306(source, target, 1_000_000, 1_000_001),
        "skill 306 accepted level zero");
    Equal(0, random.Calls, "invalid skill 306 consumed RNG");
}

static void VerifySkill308LowHealthGate()
{
    var source = NewActor<TestPlayer>();
    var target = NewActor<TBaseObject>();
    source.m_MagicList.Add(Magic(308, 2, 3, 2));

    target.m_WAbil.HP = 29;
    target.m_WAbil.MaxHP = 100;
    source.Proper = true;
    Equal(116, Skill308(source, target, 101),
        "skill 308 low-health level-three scaling");

    target.m_WAbil.HP = 30;
    Equal(101, Skill308(source, target, 101),
        "skill 308 accepted exact thirty percent");

    target.m_WAbil.HP = 29;
    source.Proper = false;
    Equal(101, Skill308(source, target, 101),
        "skill 308 ignored proper-target predicate");
    Equal(101, Skill308(source, null, 101),
        "skill 308 null target changed damage");
}

static void VerifySkill190OrderDivisorsAndStats()
{
    var target = NewActor<TBaseObject>();
    var player = NewActor<TestPlayer>();
    player.m_MagicList.Add(Magic(190, 0, 0));
    player.m_WAbil.DC = HUtil32.MakeLong(1, 7);
    player.m_btJob = 0;

    var random = UseRandom(0);
    Equal(0, Skill190(player, target, 0, 1),
        "skill 190 arg0-zero gate changed bonus");
    EqualSequence(new[] { 8 }, random.MaxValues,
        "skill 190 RNG did not precede arg0 gate");

    random = UseRandom(0);
    Equal(0, Skill190(player, null, 1, 1),
        "skill 190 null-target gate changed bonus");
    EqualSequence(new[] { 8 }, random.MaxValues,
        "skill 190 RNG did not precede target gate");

    var animal = NewActor<TBaseObject>();
    animal.m_MagicList.Add(Magic(190, 0, 0));
    animal.m_btJob = 0;
    random = UseRandom(0);
    Equal(0, Skill190(animal, target, 1, 1),
        "skill 190 non-human source changed bonus");
    EqualSequence(new[] { 8 }, random.MaxValues,
        "skill 190 RNG did not precede human RTTI gate");

    random = UseRandom();
    Equal(0, Skill190(player, target, 1, 127),
        "skill 190 excluded resolver skill changed bonus");
    Equal(0, random.Calls, "skill 190 exclusion consumed RNG");

    random = UseRandom(0);
    Equal(10, Skill190(player, target, 1, 1),
        "skill 190 job-zero MaxDC bonus");
    EqualSequence(new[] { 8 }, random.MaxValues,
        "skill 190 job-zero divisor");

    player.m_btJob = 1;
    player.m_WAbil.MC = HUtil32.MakeLong(1, 9);
    random = UseRandom(0);
    Equal(13, Skill190(player, target, 1, 235),
        "skill 190 job-one MaxMC bonus");
    EqualSequence(new[] { 9 }, random.MaxValues,
        "skill 190 resolver-235 divisor");

    player.m_btJob = 2;
    player.m_WAbil.SC = HUtil32.MakeLong(1, 11);
    random = UseRandom(0);
    Equal(16, Skill190(player, target, 3, 234),
        "skill 190 job-two MaxSC bonus or bitwise arg0 gate");
    EqualSequence(new[] { 26 }, random.MaxValues,
        "skill 190 resolver-234 divisor");

    player.m_btJob = 3;
    SetField(player, "m_nNativeJob3BaseAbilityMax", 13);
    random = UseRandom(0);
    Equal(19, Skill190(player, target, 1, 1),
        "skill 190 independent job-three base carrier");
    EqualSequence(new[] { 8 }, random.MaxValues,
        "skill 190 job-three divisor");

    random = UseRandom(0);
    Equal(0, Skill190(player, target, 2, 1),
        "skill 190 accepted arg0 without low bit");
    EqualSequence(new[] { 8 }, random.MaxValues,
        "skill 190 bitwise arg0 gate RNG order");
}

static void VerifyHumanHqReducers()
{
    var hero = NewActor<HeroObject>();
    SetField(hero, "m_btNativeHumanHqEnabled", (byte)1);
    hero.m_Abil.Level = 33;
    var random = UseRandom();
    Equal(101, HumanHq(hero, 101), "hero HQ accepted level 33");
    Equal(0, random.Calls, "hero HQ level gate consumed RNG");

    hero.m_Abil.Level = 34;
    random = UseRandom(9);
    Equal(71, HumanHq(hero, 101), "hero HQ level-34 reduction");
    EqualSequence(new[] { 100 }, random.MaxValues,
        "hero HQ RNG sequence");
    random = UseRandom(10);
    Equal(101, HumanHq(hero, 101),
        "hero HQ accepted roll equal to chance");

    hero.m_Abil.Level = 42;
    random = UseRandom(14);
    Equal(31, HumanHq(hero, 101), "hero HQ seventy-percent cap");

    var player = NewActor<TestPlayer>();
    SetField(player, "m_btNativeHumanHqEnabled", (byte)1);
    player.m_Abil.Level = 46;
    random = UseRandom(9);
    Equal(71, HumanHq(player, 101), "player HQ pre-47 reduction");

    player.m_Abil.Level = 47;
    random = UseRandom(9);
    Equal(61, HumanHq(player, 101), "player HQ level-47 reduction");

    player.m_Abil.Level = 59;
    random = UseRandom(9);
    Equal(31, HumanHq(player, 101), "player HQ seventy-percent cap");

    SetField(player, "m_btNativeHumanHqEnabled", (byte)0);
    random = UseRandom();
    Equal(101, HumanHq(player, 101), "disabled player HQ changed damage");
    Equal(0, random.Calls, "disabled player HQ consumed RNG");
}

static void VerifyState16Caps()
{
    int[] caps = { 800, 800, 600, 400, 200, 200, 515 };
    for (int value = 1; value <= 7; value++)
    {
        Equal(caps[value - 1], State16Cap(1, 0, 900, true, value),
            $"state16 value {value} cap");
    }

    Equal(900, State16Cap(1, 0x10, 900, true, 1),
        "state16 flags bit 0x10 did not bypass cap");
    Equal(900, State16Cap(1, 0, 900, false, 1),
        "inactive state16 capped damage");
    Equal(900, State16Cap(1, 0, 900, true, 0),
        "state16 value zero capped damage");
    Equal(900, State16Cap(1, 0, 900, true, 8),
        "state16 value eight capped damage");
    Equal(900, State16Cap(127, 0, 900, true, 4),
        "skill 127 capped below state16 value five");
    Equal(200, State16Cap(127, 0, 900, true, 5),
        "skill 127 did not cap at state16 value five");
    Equal(199, State16Cap(1, 0, 199, true, 5),
        "state16 raised damage below cap");

    var actor = NewActor<TBaseObject>();
    Equal(true, AddTimed(actor, 16, 5, 1_000),
        "could not construct active state16 audit node");
    Equal(200, ApplyState16(actor, 1, 0, 900),
        "state16 actor wrapper did not read timed value");
}

static void VerifySourceContract()
{
    string root = FindRepoRoot();
    string path = Path.Combine(root, "GameSvr", "Actors",
        "TBaseObject.NativeHumanMagicHooks.cs");
    string source = File.ReadAllText(path);

    Contains(source, "GetMagicInfo(NativeSkill190Id)",
        "skill 190 learned-magic lookup missing");
    Contains(source, "GetMagicInfo(NativeSkill306Id)",
        "skill 306 learned-magic lookup missing");
    Contains(source, "GetMagicInfo(NativeSkill307Id)",
        "skill 307 learned-magic lookup missing");
    Contains(source, "GetMagicInfo(NativeSkill308Id)",
        "skill 308 learned-magic lookup missing");
    Assert(!source.Contains("m_ItemList", StringComparison.Ordinal) &&
        !source.Contains("m_UseItems", StringComparison.Ordinal),
        "native human magic hooks scan items/equipment");
    Contains(source,
        "unchecked((byte)(magic.btLevel + magic.NativeLevelBonus))",
        "effective-level byte wrap missing");
    Contains(source, "magic.MagicInfo.btTrainLv",
        "effective-level training cap missing");
    Contains(source, "Math.Truncate", "native truncation missing");
    Assert(!source.Contains("Math.Round", StringComparison.Ordinal),
        "native hooks introduced rounding-to-nearest");

    string resolver = File.ReadAllText(Path.Combine(root, "GameSvr",
        "Actors", "TBaseObject.NativeMagicDamage.cs"));
    int skill307 = resolver.IndexOf("ApplyNativeSkill307Damage",
        StringComparison.Ordinal);
    int hq = resolver.IndexOf("ApplyNativeHumanHqReduction",
        StringComparison.Ordinal);
    int cap = resolver.IndexOf("ApplyNativeState16MagicDamageCap",
        StringComparison.Ordinal);
    int skill306 = resolver.IndexOf("TryApplyNativeSkill306",
        StringComparison.Ordinal);
    int skill308 = resolver.IndexOf("ApplyNativeSkill308LowHealthDamage",
        StringComparison.Ordinal);
    int skill190 = resolver.IndexOf("GetNativeSkill190DamageBonus",
        StringComparison.Ordinal);
    Assert(skill307 >= 0 && hq >= 0 && cap > hq && skill306 > skill307 &&
        skill308 > skill306 && skill190 > skill308,
        "human hook resolver order differs from native VMT dispatch");

    string manager = File.ReadAllText(Path.Combine(root, "GameSvr",
        "Spells", "MagicManager.cs"));
    Assert(!manager.Contains("ApplyNativeSkill307Damage",
            StringComparison.Ordinal) &&
        !manager.Contains("TryApplyNativeSkill306", StringComparison.Ordinal) &&
        !manager.Contains("ApplyNativeSkill308LowHealthDamage",
            StringComparison.Ordinal) &&
        !manager.Contains("GetNativeSkill190DamageBonus",
            StringComparison.Ordinal),
        "human hooks were also connected in MagicManager");
}

static T NewActor<T>() where T : TBaseObject
{
    var actor = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    actor.m_sCharName = "native-human-magic-audit";
    actor.m_MagicList = new List<TUserMagic>();
    actor.m_ItemList = new List<TUserItem>();
    actor.m_UseItems = new TUserItem[Grobal2.HUMAN_EQUIPPED_ITEM_COUNT];
    actor.m_Abil = new TAbility();
    actor.m_WAbil = new TAbility();
    return actor;
}

static TUserMagic Magic(int id, int level, int train, int bonus = 0)
{
    var magic = new TUserMagic
    {
        btLevel = unchecked((byte)level),
        MagicInfo = new TMagic
        {
            wMagicID = unchecked((ushort)id),
            btTrainLv = unchecked((byte)train)
        }
    };
    SetField(magic, "NativeLevelBonus", unchecked((byte)bonus));
    return magic;
}

static int EffectiveLevel(TUserMagic magic) => InvokeStatic<int>(
    "GetNativeHumanMagicEffectiveLevel", new[] { typeof(TUserMagic) }, magic);

static int Skill307(TBaseObject source, int damage) => Invoke<int>(source,
    "ApplyNativeSkill307Damage", new[] { typeof(int) }, damage);

static bool Skill306(TBaseObject source, TBaseObject target, int currentTick,
    int procTick) => Invoke<bool>(source, "TryApplyNativeSkill306",
    new[] { typeof(TBaseObject), typeof(int), typeof(int) }, target,
    currentTick, procTick);

static int Skill308(TBaseObject source, TBaseObject target, int damage) =>
    Invoke<int>(source, "ApplyNativeSkill308LowHealthDamage",
        new[] { typeof(TBaseObject), typeof(int) }, target, damage);

static int Skill190(TBaseObject source, TBaseObject target, byte arg0,
    int skillId) => Invoke<int>(source, "GetNativeSkill190DamageBonus",
    new[] { typeof(TBaseObject), typeof(byte), typeof(int) }, target, arg0,
    skillId);

static int HumanHq(TBaseObject actor, int damage) => Invoke<int>(actor,
    "ApplyNativeHumanHqReduction", new[] { typeof(int) }, damage);

static int State16Cap(int skillId, int flags, int damage, bool active,
    int value) => InvokeStatic<int>("CalculateNativeState16MagicDamageCap",
    new[] { typeof(int), typeof(int), typeof(int), typeof(bool), typeof(int) },
    skillId, flags, damage, active, value);

static int ApplyState16(TBaseObject actor, int skillId, int flags,
    int damage) => Invoke<int>(actor, "ApplyNativeState16MagicDamageCap",
    new[] { typeof(int), typeof(int), typeof(int) }, skillId, flags, damage);

static bool AddTimed(TBaseObject actor, byte type, int value, int duration) =>
    Invoke<bool>(actor, "AddTimedAbilityInternal",
        new[] { typeof(byte), typeof(int), typeof(int), typeof(byte) }, type,
        value, duration, (byte)0);

static int TimedRemaining(TBaseObject actor, byte type) => Invoke<int>(actor,
    "GetNativeTimedAbilityRemainingMilliseconds", new[] { typeof(byte) },
    type);

static T Invoke<T>(object instance, string name, Type[] parameterTypes,
    params object[] arguments)
{
    MethodInfo method = typeof(TBaseObject).GetMethod(name,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        null, parameterTypes, null) ??
        throw new MissingMethodException(typeof(TBaseObject).FullName, name);
    return (T)method.Invoke(instance, arguments);
}

static T InvokeStatic<T>(string name, Type[] parameterTypes,
    params object[] arguments)
{
    MethodInfo method = typeof(TBaseObject).GetMethod(name,
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
        null, parameterTypes, null) ??
        throw new MissingMethodException(typeof(TBaseObject).FullName, name);
    return (T)method.Invoke(null, arguments);
}

static T GetField<T>(object instance, string name)
{
    FieldInfo field = FindField(instance.GetType(), name);
    return (T)field.GetValue(instance);
}

static void SetField(object instance, string name, object value)
{
    FindField(instance.GetType(), name).SetValue(instance, value);
}

static FieldInfo FindField(Type type, string name)
{
    for (Type current = type; current != null; current = current.BaseType)
    {
        FieldInfo field = current.GetField(name,
            BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        if (field != null) return field;
    }
    throw new MissingFieldException(type.FullName, name);
}

// The recorder rides M2Share.RandomNumber, the field the server assigns at
// startup. It used to ride RandomNumber's private `random` field, which
// POIS-26 removed when the facade moved onto the Delphi LCG sub_403B4C
// (@0x403B4C imul [0x7A2008],0x08088405 / inc / mul / take EDX). GetField then
// returned null, so the 190/306/307/308 and HQ draw assertions below were
// never reached. Their expected values, bounds and counts are unchanged.
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
        Directory.GetCurrentDirectory(), AppContext.BaseDirectory
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

sealed class TestPlayer : TPlayObject
{
    internal bool Proper = true;

    public override bool IsProperTarget(TBaseObject target) => Proper;
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

    // Every hook here draws bounded (skill 306's Random(100), skill 190's
    // Random(divisor), the two HQ Random(100) ladders). A parameterless
    // advance or a min/max draw would escape the Calls/MaxValues ledger, so
    // refuse it rather than let it pass unrecorded.
    public override int Random() => throw new InvalidOperationException(
        "unexpected parameterless RandSeed advance");

    public override int Random(int minValue, int maxValue) =>
        throw new InvalidOperationException("unexpected Random(min,max) draw");

    public override int GetRandomNumber(int minValue, int maxValue) =>
        throw new InvalidOperationException("unexpected GetRandomNumber draw");
}
