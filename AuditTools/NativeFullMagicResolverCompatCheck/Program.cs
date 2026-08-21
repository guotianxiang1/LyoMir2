using System.Reflection;
using GameSvr;
using SystemModule;

PrepareRuntimeConfig();
InitializeRuntime();

VerifySourceOrder();
VerifyEarlyImmunity();
VerifyDefenceRandomOrder();
VerifyResolverRandomOrder();
VerifySkill152Then153();
VerifySkill152Flags();
VerifySkill154BurstBeforeState16Cap();
VerifySpecialDirectLandingIsolation();

Console.WriteLine(
    "PASS native-full-magic-resolver order=closed-chain " +
    "rng=breakthrough/fixed/awakening/state16/306/190/critical " +
    "immunity=52/55-zero-side-effects skill152/151/154-before-cap-before-skill153 " +
    "skill154=kind1024-uncapped-caller-owned-count " +
    "special=282-285+287-290-direct-landing-shell");
return;

static void VerifySourceOrder()
{
    string path = Path.Combine(FindRepoRoot(), "GameSvr", "Actors",
        "TBaseObject.NativeMagicDamage.cs");
    string source = File.ReadAllText(path);
    int end = source.IndexOf(
        "private static bool IsNativeUnclosedSpecialMagicSkill",
        StringComparison.Ordinal);
    Assert(end > 0, "resolver source boundary");
    string resolver = source[..end];

    Ordered(resolver,
        "HasNativeActiveState(52)",
        "ResetNativeMagicTrace();",
        "IsNativeUnclosedSpecialMagicSkill(skillId)",
        "IsNativeMagicFirstClassifier(skillId)",
        "source.ApplyNativeMagicBreakthrough(flags)",
        "source.ApplyNativeState56MagicBonus",
        "source.ApplyNativeState16InitialMagicBonus",
        "ResolveNativeMagicDefence(",
        "ApplyNativeFixedMagicReductions(",
        "ApplyNativeState83MagicReduction(",
        "source.ApplyNativeMagicAwakening(",
        "ApplyNativeTargetMidMagicStates(",
        "ApplyNativeHumanHqReduction(",
        "ApplyNativeHumanMagicPercentReduction(",
        "ApplyNativeHumanMagicBreakContest(",
        "damage = unchecked(damage + breakExtra);",
        "damage = unchecked(damage + breakBonus);",
        "source.ApplyNativeSkill152OneShotBonus(",
        "source.ApplyNativeSkill151BurstDamage(",
        "source.ApplyNativeSkill154BurstDamage(",
        "ApplyNativeState16MagicDamageCap(",
        "ApplyNativeState16LevelContest(",
        "source.ApplyNativeSkill307Damage(",
        "RecordNativeBreakthroughFlagTrace(",
        "if (damage < 0)",
        "ApplyNativeSkill153ShieldToMagicDamage(",
        "ApplyNativeFastnessHqReduction(",
        "ApplyNativeGeneralFastnessReduction(",
        "RecordNativePostTableFlagTrace(",
        "if (damage <= 0)",
        "source.TryApplyNativeSkill306(",
        "source.ApplyNativeSkill308LowHealthDamage(",
        "source.GetNativeSkill190DamageBonus(",
        "ApplyNativeMagicCritical(",
        "ApplyStandardEarthFireSuperForce(",
        "ApplyStandardEarthFireLanding(");

    Contains(source,
        "return id is >= 282 and <= 285 or >= 287 and <= 290;",
        "legacy special range");
}

static void VerifyEarlyImmunity()
{
    foreach (int state in new[] { 52, 55 })
    {
        var source = NewActor();
        var target = NewActor();
        target.m_WAbil.MaxHP = 1000;
        target.m_WAbil.HP = 1000;
        target.SetNativeActiveState(state);
        SetField(source, "m_nNativeOneShotMagicDamage", 250);
        SetField(target, "m_wNativeSkill153ShieldCharges", (ushort)1);
        SetField(target, "m_nNativeMagicTraceDamage", 321);
        SetField(target, "m_sNativeMagicTracePrefix", "existing");

        WithRandom(Array.Empty<int>(), random =>
        {
            Equal(0, Resolve(target, source, 1, false, 1, 0, 100),
                $"state{state} result");
            EqualSequence(Array.Empty<int>(), random.MaxValues,
                $"state{state} RNG");
        });

        Equal(1000, target.m_WAbil.HP, $"state{state} HP");
        Equal(250, GetField<int>(source, "m_nNativeOneShotMagicDamage"),
            $"state{state} consumed skill152");
        Equal((ushort)1,
            GetField<ushort>(target, "m_wNativeSkill153ShieldCharges"),
            $"state{state} consumed skill153");
        Equal(321, GetField<int>(target, "m_nNativeMagicTraceDamage"),
            $"state{state} reset trace");
        Equal("existing", GetField<string>(target,
            "m_sNativeMagicTracePrefix"), $"state{state} reset prefix");
    }
}

static void VerifyResolverRandomOrder()
{
    var source = NewPlayer();
    var target = NewActor();
    PrepareLanding(target, 2000);
    target.SetNativeActiveState(17);
    source.SetNativeActiveState(16);
    source.m_Abil.Level = 1;
    target.m_Abil.Level = 1;
    source.m_MagicList.Add(Magic(115, 1, 7));
    source.m_MagicList.Add(Magic(306, 1, 3));
    source.m_MagicList.Add(Magic(190, 0, 0));

    SetField(source, "m_boNativeAwakening", true);
    SetField(target, "m_nNativeSteelBodyReduction", 1);
    SetField(target, "m_nNativeGoldenBellReduction", 1);
    SetField(source, "m_dwNativeSkill306ProcTick",
        unchecked(HUtil32.GetTickCount() - 120_001));
    SetCritical(source, 0, 0);
    SetCriticalTarget(target, 0, 0);

    int[] values = { 999, 99, 99, 9999, 14, 99, 7, 9999 };
    WithRandom(values, random =>
    {
        Equal(900, Resolve(target, source, 1, true, 1, 1, 100),
            "full resolver damage");
        EqualSequence(new[] { 1000, 100, 100, 10000, 15, 100, 8, 10000 },
            random.MaxValues, "full resolver RNG order");
        random.AssertExhausted("full resolver RNG values");
    });

    Equal(1100, target.m_WAbil.HP, "full resolver landing HP");
    Assert(GetField<bool>(target, "m_boNativeHealthSpellDirty"),
        "full resolver health dirty flag");
}

static void VerifyDefenceRandomOrder()
{
    var source = NewActor();
    var target = NewActor();
    PrepareLanding(target, 1000);
    target.m_btRaceServer = Grobal2.RC_PLAYOBJECT;
    target.m_nBodyLuckLevel = 1;
    target.m_WAbil.MAC = HUtil32.MakeLong(3, 9);
    SetCritical(source, -1, 0);

    WithRandom(new[] { 999, 4, 0, 99 }, random =>
    {
        Equal(97, Resolve(target, source, 1, false, 1, 0, 100),
            "defence RNG damage");
        EqualSequence(new[] { 1000, 5, 7, 100 }, random.MaxValues,
            "defence RNG order");
        random.AssertExhausted("defence RNG values");
    });

    Equal(903, target.m_WAbil.HP, "defence RNG landing HP");
}

static void VerifySkill152Then153()
{
    var source = NewActor();
    var target = NewPlayer();
    PrepareLanding(target, 1000);
    target.SetNativeActiveState(17);
    target.m_btJob = M2Share.jWarr;
    target.m_WAbil.DC = HUtil32.MakeLong(0, 100);
    Assert(AddTimed(target, 16, 1, -1), "skill152/cap state16 setup");
    SetField(source, "m_nNativeOneShotMagicDamage", 250);
    SetField(target, "m_wNativeSkill153ShieldCharges", (ushort)1);
    SetCritical(source, -1, 0);

    WithRandom(new[] { 999, 99 }, random =>
    {
        Equal(550, Resolve(target, source, 1, false, 1, 1, 700),
            "skill152 then cap then skill153 damage");
        EqualSequence(new[] { 1000, 100 }, random.MaxValues,
            "skill152/153 RNG order");
        random.AssertExhausted("skill152/153 RNG values");
    });

    Equal(450, target.m_WAbil.HP, "skill152/cap/153 landing HP");
    Equal(250, GetField<int>(source, "m_nNativeOneShotMagicDamage"),
        "resolver consumed skill152 carrier");
    Equal((ushort)0,
        GetField<ushort>(target, "m_wNativeSkill153ShieldCharges"),
        "resolver did not consume skill153 charge");
}

static void VerifySkill152Flags()
{
    foreach (int flags in new[] { 0x00, 0x04, 0x08, 0x0C })
    {
        var source = NewActor();
        var target = NewPlayer();
        PrepareLanding(target, 1000);
        target.SetNativeActiveState(17);
        SetField(source, "m_nNativeOneShotMagicDamage", 250);
        SetCritical(source, -1, 0);

        WithRandom(new[] { 999, 99 }, random =>
        {
            Equal(350, Resolve(target, source, 1, false, 1, flags, 100),
                $"skill152 flags 0x{flags:X2} result");
            EqualSequence(new[] { 1000, 100 }, random.MaxValues,
                $"skill152 flags 0x{flags:X2} RNG");
            random.AssertExhausted($"skill152 flags 0x{flags:X2} RNG");
        });

        Equal(650, target.m_WAbil.HP,
            $"skill152 flags 0x{flags:X2} landing HP");
        Equal(250, GetField<int>(source, "m_nNativeOneShotMagicDamage"),
            $"skill152 flags 0x{flags:X2} preserved carrier");
    }
}

static void VerifySkill154BurstBeforeState16Cap()
{
    var source = NewActor();
    source.m_btJob = 3;
    SetNativeCoreCcHigh(source, 6000);
    SetField(source, "m_nNativeSkill154StrikeCount", (ushort)1);
    SetCritical(source, -1, 0);

    var wrongKindTarget = NewPlayer();
    PrepareLanding(wrongKindTarget, 50_000);
    WithRandom(new[] { 999, 99 }, random =>
    {
        Equal(100, Resolve(wrongKindTarget, source, 0x401, false, 1, 1,
            100), "skill154 wrong attack kind");
        EqualSequence(new[] { 1000, 100 }, random.MaxValues,
            "skill154 wrong-kind RNG");
        random.AssertExhausted("skill154 wrong-kind RNG");
    });
    Equal((ushort)1, GetField<ushort>(source,
        "m_nNativeSkill154StrikeCount"),
        "skill154 wrong kind consumed count");

    var uncappedTarget = NewPlayer();
    PrepareLanding(uncappedTarget, 50_000);
    WithRandom(new[] { 999, 99 }, random =>
    {
        Equal(30_100, Resolve(uncappedTarget, source, 0x400, false, 1, 1,
            100), "skill154 uncapped max-attack burst");
        EqualSequence(new[] { 1000, 100 }, random.MaxValues,
            "skill154 uncapped RNG");
        random.AssertExhausted("skill154 uncapped RNG");
    });
    Equal(19_900, uncappedTarget.m_WAbil.HP,
        "skill154 uncapped landing HP");
    Equal((ushort)1, GetField<ushort>(source,
        "m_nNativeSkill154StrikeCount"),
        "skill154 resolver must not consume count");

    var cappedTarget = NewPlayer();
    PrepareLanding(cappedTarget, 10_000);
    Assert(AddTimed(cappedTarget, 16, 1, -1),
        "skill154 state16 setup");
    WithRandom(new[] { 999, 99 }, random =>
    {
        Equal(800, Resolve(cappedTarget, source, 0x400, false, 1, 1,
            100), "skill154 before state16 cap");
        EqualSequence(new[] { 1000, 100 }, random.MaxValues,
            "skill154 state16-cap RNG");
        random.AssertExhausted("skill154 state16-cap RNG");
    });
    Equal(9_200, cappedTarget.m_WAbil.HP,
        "skill154 state16-cap landing HP");

    ConsumeSkill154(source, 0);
    Equal((ushort)1, GetField<ushort>(source,
        "m_nNativeSkill154StrikeCount"),
        "skill154 zero attack power consumed count");
    ConsumeSkill154(source, 1);
    ConsumeSkill154(source, 1);
    Equal((ushort)0, GetField<ushort>(source,
        "m_nNativeSkill154StrikeCount"),
        "skill154 positive attack power count/underflow");
}

static void VerifySpecialDirectLandingIsolation()
{
    foreach (int skillId in Enumerable.Range(282, 4)
                 .Concat(Enumerable.Range(287, 4)))
    {
        Assert(IsLegacySpecial(skillId), $"legacy skill {skillId} gate");
        var source = NewActor();
        var target = NewActor();
        PrepareLanding(target, 1000);
        target.m_WAbil.MAC = HUtil32.MakeLong(50, 50);
        SetField(source, "m_wNativeBreakThroughChance", (ushort)600);
        SetField(source, "m_boNativeAwakening", true);
        SetField(source, "m_nNativeOneShotMagicDamage", 250);
        SetField(target, "m_nNativeSteelBodyReduction", 50);
        SetField(target, "m_nNativeGoldenBellReduction", 50);
        SetField(target, "m_wNativeSkill153ShieldCharges", (ushort)1);
        SetField(target, "m_nNativeMagicTraceDamage", 77);
        SetField(target, "m_sNativeMagicTracePrefix", "legacy");
        SetCritical(source, 10000, 10000);
        SetCriticalTarget(target, 0, 0);

        WithRandom(Array.Empty<int>(), random =>
        {
            Equal(10, Resolve(target, source, skillId, true, 1, 0, 10),
                $"legacy skill {skillId} result");
            EqualSequence(Array.Empty<int>(), random.MaxValues,
                $"legacy skill {skillId} RNG");
        });

        Equal(990, target.m_WAbil.HP, $"legacy skill {skillId} HP");
        Equal((ushort)1,
            GetField<ushort>(target, "m_wNativeSkill153ShieldCharges"),
            $"legacy skill {skillId} consumed skill153");
        Equal(0, GetField<int>(target, "m_nNativeMagicTraceDamage"),
            $"legacy skill {skillId} trace reset");
        Equal(string.Empty, GetField<string>(target,
            "m_sNativeMagicTracePrefix"),
            $"legacy skill {skillId} prefix reset");
    }

    Assert(!IsLegacySpecial(281), "legacy lower neighbor");
    Assert(!IsLegacySpecial(286), "legacy gap skill286");
    Assert(!IsLegacySpecial(291), "legacy upper neighbor");
    Assert(IsLegacySpecial(65536 + 282), "legacy ushort coercion");
}

static int Resolve(TBaseObject target, TBaseObject source, int skillId,
    bool arg0, byte category, int flags, int damage)
{
    MethodInfo method = typeof(TBaseObject).GetMethod(
        "ResolveFullMagicDamage", BindingFlags.Instance |
        BindingFlags.NonPublic) ??
        throw new MissingMethodException("ResolveFullMagicDamage");
    ParameterInfo[] parameters = method.GetParameters();
    Equal(7, parameters.Length, "resolver parameter count");
    Equal(typeof(byte), parameters[4].ParameterType,
        "resolver category ABI");
    Equal(typeof(int), parameters[5].ParameterType,
        "resolver flags ABI");
    object context = parameters[3].ParameterType.GetProperty("Empty",
        BindingFlags.Static | BindingFlags.Public |
        BindingFlags.NonPublic)?.GetValue(null) ??
        throw new MissingMemberException("MagicDamageContext.Empty");
    object result = method.Invoke(target, new object[]
    {
        source, skillId, arg0, context, category, flags, damage
    });
    return (int)(result ?? throw new InvalidOperationException(
        "resolver returned null"));
}

static bool IsLegacySpecial(int skillId)
{
    MethodInfo method = typeof(TBaseObject).GetMethod(
        "IsNativeUnclosedSpecialMagicSkill", BindingFlags.Static |
        BindingFlags.NonPublic) ??
        throw new MissingMethodException("IsNativeUnclosedSpecialMagicSkill");
    return (bool)(method.Invoke(null, new object[] { skillId }) ?? false);
}

static TBaseObject NewActor()
{
    var actor = new TBaseObject
    {
        m_PEnvir = new Envirnoment(),
        m_boObMode = true,
        m_sCharName = "full-resolver-audit"
    };
    actor.m_MsgList.Clear();
    return actor;
}

static TPlayObject NewPlayer()
{
    var player = new TPlayObject
    {
        m_PEnvir = new Envirnoment(),
        m_boObMode = true,
        m_boOffLineFlag = true,
        m_sCharName = "full-resolver-player"
    };
    player.m_MsgList.Clear();
    return player;
}

static void PrepareLanding(TBaseObject actor, int health)
{
    actor.m_WAbil.MaxHP = health;
    actor.m_WAbil.HP = health;
    actor.m_WAbil.MP = 0;
    actor.m_WAbil.MAC = 0;
}

static TUserMagic Magic(int id, byte level, byte trainLevel) => new()
{
    btLevel = level,
    wMagIdx = unchecked((ushort)id),
    MagicInfo = new TMagic
    {
        wMagicID = unchecked((ushort)id),
        btTrainLv = trainLevel
    }
};

static bool AddTimed(TBaseObject actor, byte type, int value, int duration)
{
    MethodInfo method = typeof(TBaseObject).GetMethod(
        "AddTimedAbilityInternal", BindingFlags.Instance |
        BindingFlags.NonPublic, null,
        new[] { typeof(byte), typeof(int), typeof(int), typeof(byte) },
        null) ?? throw new MissingMethodException("AddTimedAbilityInternal");
    return (bool)(method.Invoke(actor,
        new object[] { type, value, duration, (byte)0 }) ?? false);
}

static void SetNativeCoreCcHigh(TBaseObject actor, int value)
{
    object ability = GetField<object>(actor, "m_NativeCoreWorkingAbility");
    FieldInfo field = ability.GetType().GetField("CCHigh",
        BindingFlags.Instance | BindingFlags.Public |
        BindingFlags.NonPublic) ??
        throw new MissingFieldException(ability.GetType().FullName,
            "CCHigh");
    field.SetValue(ability, value);
    SetField(actor, "m_NativeCoreWorkingAbility", ability);
}

static void ConsumeSkill154(TBaseObject actor, int attackPower)
{
    MethodInfo method = typeof(TBaseObject).GetMethod(
        "ConsumeNativeSkill154StrikeAfterPositiveAttackPower",
        BindingFlags.Instance | BindingFlags.NonPublic) ??
        throw new MissingMethodException(
            "ConsumeNativeSkill154StrikeAfterPositiveAttackPower");
    method.Invoke(actor, new object[] { attackPower });
}

static void SetCritical(TBaseObject actor, short chance, int increase)
{
    SetField(actor, "m_sNativeCriticalChance", chance);
    SetField(actor, "m_nNativeCriticalDamageIncrease", increase);
}

static void SetCriticalTarget(TBaseObject actor, short antiChance,
    short reduction)
{
    SetField(actor, "m_sNativeAntiCriticalChance", antiChance);
    SetField(actor, "m_sNativeCriticalDamageReduction", reduction);
}

// The draw probe hangs off M2Share.RandomNumber, the field the server itself
// assigns. It used to hang off RandomNumber's private `random` field, which
// POIS-26 deleted when the facade moved onto the Delphi LCG sub_403B4C
// (@0x403B4C: imul [0x7A2008],0x08088405 / inc / mul / take EDX). That deletion
// silently turned this probe into a MissingFieldException, so none of the draw
// assertions below were being evaluated at all. Every expected value, bound and
// ordinal below is unchanged; only where the recorder is installed moved.
static void WithRandom(IEnumerable<int> values,
    Action<RecordingRandom> action)
{
    RandomNumber original = M2Share.RandomNumber;
    var random = new RecordingRandom(values);
    M2Share.RandomNumber = random;
    try
    {
        action(random);
    }
    finally
    {
        M2Share.RandomNumber = original ?? RandomNumber.GetInstance();
    }
}

static void Ordered(string source, params string[] values)
{
    int position = -1;
    foreach (string value in values)
    {
        int next = source.IndexOf(value, position + 1,
            StringComparison.Ordinal);
        Assert(next > position, $"resolver order missing/out-of-order: {value}");
        position = next;
    }
}

static void Contains(string source, string value, string label) =>
    Assert(source.Contains(value, StringComparison.Ordinal), label);

static void SetField(object instance, string name, object value) =>
    FindField(instance.GetType(), name).SetValue(instance, value);

static T GetField<T>(object instance, string name) =>
    (T)(FindField(instance.GetType(), name).GetValue(instance) ??
        throw new InvalidOperationException($"{name} was null"));

static FieldInfo FindField(Type type, string name)
{
    for (Type current = type; current != null; current = current.BaseType)
    {
        FieldInfo field = current.GetField(name,
            BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        if (field != null)
            return field;
    }
    throw new MissingFieldException(type.FullName, name);
}

static string FindRepoRoot()
{
    foreach (string start in new[]
             { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        for (var directory = new DirectoryInfo(start); directory != null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName,
                    "GameSvr", "GameSvr.csproj")))
            {
                return directory.FullName;
            }
        }
    }
    throw new DirectoryNotFoundException("GameSvr/GameSvr.csproj");
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
    string directory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(directory, "!Setup.txt"), "[Server]");
    File.WriteAllText(Path.Combine(directory, "String.ini"), "[String]");
    File.WriteAllText(Path.Combine(directory, "Command.conf"), "[Command]");
    string share = Path.GetFullPath(Path.Combine(directory, "..", "Share"));
    Directory.CreateDirectory(share);
    File.WriteAllText(Path.Combine(share, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]");
    File.WriteAllText(Path.Combine(share, "ServerData.ini"), "[Integer]");
}

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            $"{label}: expected={expected}, actual={actual}");
    }
}

static void EqualSequence(IEnumerable<int> expected,
    IEnumerable<int> actual, string label)
{
    int[] expectedValues = expected.ToArray();
    int[] actualValues = actual.ToArray();
    if (!expectedValues.SequenceEqual(actualValues))
    {
        throw new InvalidOperationException(
            $"{label}: expected=[{string.Join(',', expectedValues)}], " +
            $"actual=[{string.Join(',', actualValues)}]");
    }
}

static void Assert(bool condition, string label)
{
    if (!condition)
        throw new InvalidOperationException(label);
}

sealed class RecordingRandom : RandomNumber
{
    private readonly Queue<int> _values;

    internal RecordingRandom(IEnumerable<int> values)
    {
        _values = new Queue<int>(values);
    }

    internal List<int> MaxValues { get; } = new();

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

    // No routine the resolver reaches advances RandSeed without a bound, and
    // none draws through the min/max entries: the only three parameterless
    // advances in GameSvr are the magic-tower prize, the magic producers and
    // the state-26 effects. Any of these arriving here is a new draw the
    // ordinal assertions could not see, so refuse rather than absorb it.
    public override int Random() => throw new InvalidOperationException(
        "unexpected parameterless RandSeed advance");

    public override int Random(int minValue, int maxValue) =>
        throw new InvalidOperationException("unexpected Random(min,max) draw");

    public override int GetRandomNumber(int minValue, int maxValue) =>
        throw new InvalidOperationException("unexpected GetRandomNumber draw");

    internal void AssertExhausted(string label)
    {
        if (_values.Count != 0)
            throw new InvalidOperationException(
                $"{label}: {_values.Count} unused value(s)");
    }
}
