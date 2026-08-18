using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.CompilerServices;
using GameSvr;
using GameSvr.Services;
using SystemModule;

PrepareRuntimeConfig();
InitializeRuntime();

VerifyEntryAndExternalLevels();
VerifyPoolGainAndRngOrder();
VerifyFinalContestFormula();
VerifyStrictPoolExpiry();
VerifyPoolSerialization();
VerifyNearestEvenRounding();
VerifyAbilityProducerChain();
VerifyHumanPercentReduction();
VerifyResolverWrapper();
VerifySourceContract();

Console.WriteLine(
    "PASS native-break-contest input=explicit-map+global " +
    "pool=ushort-cap5000+refresh-first expiry=unsigned-strict-gt60000 " +
    "rng=fallback+pool-factor+chance result=nearest-even " +
    "serialization=remaining-then-pool producer=fixed+effect+projection " +
    "human-percent=signed-word+cap20000 resolver=connected");
return;

static void VerifyEntryAndExternalLevels()
{
    var target = Actor(100, 0, 100);
    var source = Actor(100, 0, 100);

    var random = UseRandom();
    ContestResult result = Contest(target, null, 100, 100,
        1, 1, 0, 0, 0, 0, 0, 123);
    Equal(0, result.Bonus, "null source bonus");
    Equal((ushort)0, result.Combined, "null source combined output");
    Equal(0, result.Extra, "null source extra output");
    Equal(0, random.Calls, "null source consumed RNG");

    source.m_btRaceServer = Grobal2.RC_ANIMAL;
    result = Contest(target, source, 100, 100,
        1, 1, 0, 0, 0, 0, 0, 124);
    Equal((ushort)0, result.Combined, "invalid race combined output");
    Equal(0, random.Calls, "invalid race consumed RNG");

    source.m_btRaceServer = Grobal2.RC_PLAYOBJECT;
    result = Contest(target, source, 100, 100,
        0, 0, 0, 0, 7, -1, -1, 125);
    Equal((ushort)0, result.Combined,
        "negative global levels made contest eligible");
    Equal(0, Field<int>(target, "m_dwNativeBreakContestPoolTick"),
        "ineligible contest refreshed pool tick");

    random = UseRandom(0);
    result = Contest(target, source, 0, 0,
        1, 1, 2, 3, 7, 4, 5, 126);
    Equal((ushort)16, result.Combined,
        "raw/map/global level accumulation");
    Equal(0, result.Extra, "extra output was not cleared");
    Equal(126, Field<int>(target, "m_dwNativeBreakContestPoolTick"),
        "eligible contest did not refresh pool tick before gain");
    EqualSequence(new[] { 100 }, random.MaxValues,
        "nonpositive current damage RNG sequence");

    random = UseRandom(0);
    result = Contest(target, source, 0, 0,
        65_530, 65_530, 10, 10, 0, 0, 0, 127);
    Equal((ushort)8, result.Combined,
        "combined break level did not wrap as UInt16");
}

static void VerifyPoolGainAndRngOrder()
{
    var target = Actor(100, 0, 100);
    var source = Actor(100, 0, 100);

    SetField(target, "m_wNativeBreakContestPool", (ushort)0);
    var random = UseRandom(0, 99);
    Contest(target, source, 100, 100,
        1, 0, 0, 0, -100, 0, 0, 1_000);
    Equal((ushort)12, Field<ushort>(target,
        "m_wNativeBreakContestPool"), "positive damage pool gain");
    EqualSequence(new[] { 2, 100 }, random.MaxValues,
        "positive damage RNG order");

    SetField(target, "m_wNativeBreakContestPool", (ushort)4_990);
    random = UseRandom(0, 99);
    Contest(target, source, 100, 100,
        1, 0, 0, 0, -100, 0, 0, 1_001);
    Equal((ushort)5_000, Field<ushort>(target,
        "m_wNativeBreakContestPool"), "pool cap at 5000");

    SetField(target, "m_wNativeBreakContestPool", (ushort)0);
    random = UseRandom(20);
    Contest(target, source, 50, 0,
        1, 0, 0, 0, 0, 0, 0, 2_000);
    Equal((ushort)10, Field<ushort>(target,
        "m_wNativeBreakContestPool"),
        "fallback roll equal to threshold did not gain pool");
    EqualSequence(new[] { 100 }, random.MaxValues,
        "fallback gain RNG sequence");

    SetField(target, "m_wNativeBreakContestPool", (ushort)0);
    random = UseRandom(21);
    Contest(target, source, 50, 0,
        1, 0, 0, 0, 0, 0, 0, 2_001);
    Equal((ushort)0, Field<ushort>(target,
        "m_wNativeBreakContestPool"),
        "fallback accepted roll above threshold");

    random = UseRandom(99);
    Contest(target, source, 201, 0,
        1, 0, 0, 0, 0, 0, 0, 2_002);
    Equal((ushort)10, Field<ushort>(target,
        "m_wNativeBreakContestPool"),
        "fallback >200 threshold was not unconditional");

    SetField(target, "m_wNativeBreakContestPool", (ushort)988);
    random = UseRandom(0);
    ContestResult crazyOnly = Contest(target, source, 100, 100,
        0, 1, 0, 0, 0, 0, 0, 3_000);
    Equal(0, crazyOnly.Bonus, "crazy-only contest produced bonus");
    Equal((ushort)1, crazyOnly.Combined,
        "crazy-only combined output");
    EqualSequence(new[] { 2 }, random.MaxValues,
        "crazy-only path did not consume pool-factor RNG before exit");

    SetField(target, "m_wNativeBreakContestPool", (ushort)0);
    random = UseRandom(0, 99);
    Contest(target, source, 100, int.MaxValue,
        1, 0, 0, 0, -100, 0, 0, 3_001);
    Equal((ushort)10, Field<ushort>(target,
        "m_wNativeBreakContestPool"),
        "gain multiplication did not wrap as Int32");
}

static void VerifyFinalContestFormula()
{
    var target = Actor(100, 0, 100);
    var source = Actor(100, 0, 100);

    SetField(target, "m_wNativeBreakContestPool", (ushort)988);
    var random = UseRandom(0, 50);
    ContestResult result = Contest(target, source, 100, 100,
        10, 0, 0, 0, 0, 0, 0, 4_000);
    Equal(47, result.Bonus, "job-zero final contest formula");
    Equal((ushort)10, result.Combined, "final combined output");
    Equal(0, result.Extra, "final extra output");
    EqualSequence(new[] { 2, 100 }, random.MaxValues,
        "final contest RNG sequence");

    SetField(target, "m_wNativeBreakContestPool", (ushort)988);
    random = UseRandom(0, 51);
    result = Contest(target, source, 100, 100,
        10, 0, 0, 0, 0, 0, 0, 4_001);
    Equal(0, result.Bonus,
        "final chance accepted roll above threshold");

    source.m_btJob = 1;
    SetField(target, "m_wNativeBreakContestPool", (ushort)989);
    random = UseRandom(0, 50);
    result = Contest(target, source, 100, 100,
        10, 0, 0, 0, 0, 0, 0, 4_002);
    Equal(19, result.Bonus, "job-one 0.4 final scale");

    source.m_btJob = 2;
    SetField(target, "m_wNativeBreakContestPool", (ushort)989);
    random = UseRandom(0, 50);
    result = Contest(target, source, 100, 100,
        10, 0, 0, 0, 0, 0, 0, 4_003);
    Equal(38, result.Bonus, "job-two 0.8 final scale");

    source.m_btJob = 3;
    SetField(target, "m_wNativeBreakContestPool", (ushort)988);
    random = UseRandom(0, 50);
    result = Contest(target, source, 100, 100,
        10, 0, 0, 0, 0, 0, 0, 4_004);
    Equal(47, result.Bonus, "job-three final scale");

    target.m_btJob = 3;
    SetNativeCoreWorkingAbility(target, "CCHigh", 100);
    SetField(target, "m_wNativeBreakContestPool", (ushort)988);
    random = UseRandom(0, 50);
    result = Contest(target, source, 100, 100,
        10, 0, 0, 0, 0, 0, 0, 4_005);
    Equal(47, result.Bonus,
        "target job-three working maximum carrier");
}

static void VerifyStrictPoolExpiry()
{
    var actor = Actor(1, 0, 0);
    SetField(actor, "m_wNativeBreakContestPool", (ushort)123);
    SetField(actor, "m_dwNativeBreakContestPoolTick", 100);

    ProcessPool(actor, 60_100);
    Equal((ushort)123, Field<ushort>(actor,
        "m_wNativeBreakContestPool"),
        "pool cleared at exactly 60000 milliseconds");
    Equal(100, Field<int>(actor, "m_dwNativeBreakContestPoolTick"),
        "exact-boundary cleanup changed tick");

    ProcessPool(actor, 60_101);
    Equal((ushort)0, Field<ushort>(actor,
        "m_wNativeBreakContestPool"),
        "pool did not clear above 60000 milliseconds");
    Equal(60_101, Field<int>(actor, "m_dwNativeBreakContestPoolTick"),
        "pool cleanup did not refresh tick");

    SetField(actor, "m_wNativeBreakContestPool", (ushort)77);
    SetField(actor, "m_dwNativeBreakContestPoolTick", int.MaxValue);
    ProcessPool(actor, int.MinValue);
    Equal((ushort)77, Field<ushort>(actor,
        "m_wNativeBreakContestPool"),
        "unsigned tick wrap expired a one-millisecond pool");
}

static void VerifyPoolSerialization()
{
    var source = Actor(1, 0, 0);
    SetField(source, "m_dwNativeBreakContestPoolTick", 1_001);
    SetField(source, "m_wNativeBreakContestPool", (ushort)0x1234);

    using var stream = new MemoryStream();
    using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8,
        true))
    {
        WritePool(source, writer, 61_000);
    }
    EqualSequence(new byte[] { 1, 0, 0x34, 0x12 }, stream.ToArray(),
        "remaining/pool serialization order or width");

    stream.Position = 0;
    var restored = Actor(1, 0, 0);
    using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8,
        true))
    {
        ReadPool(restored, reader, 100_000);
    }
    Equal(40_001, Field<int>(restored,
        "m_dwNativeBreakContestPoolTick"),
        "remaining window reconstruction");
    Equal((ushort)0x1234, Field<ushort>(restored,
        "m_wNativeBreakContestPool"), "pool reconstruction");

    RestorePool(restored, 60_000, 65_535, 200_000);
    Equal(200_000, Field<int>(restored,
        "m_dwNativeBreakContestPoolTick"),
        "remaining 60000 reconstruction");
    Equal((ushort)65_535, Field<ushort>(restored,
        "m_wNativeBreakContestPool"),
        "serialized pool was incorrectly clamped");

    RestorePool(restored, 60_001, 7, 200_000);
    Equal(140_000, Field<int>(restored,
        "m_dwNativeBreakContestPoolTick"),
        "invalid remaining value did not normalize to zero");

    SetField(restored, "m_dwNativeBreakContestPoolTick", int.MaxValue);
    Equal((ushort)59_999, Remaining(restored, int.MinValue),
        "remaining calculation lost signed tick wrap");
}

static void VerifyNearestEvenRounding()
{
    Equal(2, NativeRound(2.5d), "nearest-even round 2.5");
    Equal(4, NativeRound(3.5d), "nearest-even round 3.5");
    Equal(-2, NativeRound(-2.5d), "nearest-even round -2.5");
    Equal(-4, NativeRound(-3.5d), "nearest-even round -3.5");
}

static void VerifyAbilityProducerChain()
{
    byte[] record = new byte[0x22E];
    BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(0xA0),
        int.MaxValue - 2);
    BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0xBA), 65_530);
    BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0xCE), 65_520);

    var actor = FixedRecordActor.Create(record);
    var addAbility = new TAddAbility();
    object[] seedArguments = { addAbility };
    Invoke<object>(actor, "SeedNativeFixedAbility",
        new[] { typeof(TAddAbility).MakeByRefType() }, seedArguments);
    addAbility = (TAddAbility)seedArguments[0];

    Equal(int.MaxValue - 2, Field<int>(addAbility,
        "NativeHumanMagicPercentReductionRaw"),
        "fixed A0 human magic percent seed");
    Equal((ushort)65_530, Field<ushort>(addAbility,
        "NativeBreakPower"), "fixed BA break seed");
    Equal((ushort)65_520, Field<ushort>(addAbility,
        "NativeCrazyPower"), "fixed CE crazy seed");

    ApplyEffectProperty(27, 10, addAbility);
    ApplyEffectProperty(39, 20, addAbility);
    ApplyEffectProperty(64, 5, addAbility);
    Equal((ushort)4, Field<ushort>(addAbility, "NativeBreakPower"),
        "property 27 UInt16 wrap/fixed addition");
    Equal((ushort)4, Field<ushort>(addAbility, "NativeCrazyPower"),
        "property 39 UInt16 wrap/fixed addition");
    Equal(int.MinValue + 2, Field<int>(addAbility,
        "NativeHumanMagicPercentReductionRaw"),
        "property 64 Int32 wrap/fixed addition");

    actor.m_AddAbil = addAbility;
    Invoke<object>(actor, "ProjectNativeBreakContestAbilities",
        Type.EmptyTypes);
    Equal((ushort)4, Field<ushort>(actor, "m_wNativeBreakPower"),
        "break actor projection");
    Equal((ushort)4, Field<ushort>(actor, "m_wNativeCrazyPower"),
        "crazy actor projection");
    Equal((ushort)2, Field<ushort>(actor,
        "m_wNativeHumanMagicPercentReduction"),
        "human percent LOWORD actor projection");
}

static void VerifyHumanPercentReduction()
{
    var target = Actor(100, 0, 100);

    SetField(target, "m_wNativeHumanMagicPercentReduction", (ushort)25);
    Equal(750, HumanPercentReduction(target, 1_000),
        "positive human magic percent reduction");

    SetField(target, "m_wNativeHumanMagicPercentReduction", (ushort)1_000);
    Equal(-15_000, HumanPercentReduction(target, 5_000),
        "human magic percent reduction cap at 20000");

    SetField(target, "m_wNativeHumanMagicPercentReduction", (ushort)0xFFFF);
    Equal(1_000, HumanPercentReduction(target, 1_000),
        "negative signed-word human percent was applied");

    SetField(target, "m_wNativeHumanMagicPercentReduction", (ushort)0x7FFF);
    Equal(21_540_047, HumanPercentReduction(target, 65_539),
        "human percent multiplication lost Int32 wrap");
}

static void VerifyResolverWrapper()
{
    NativeGlobalBreakSettings.Reset();
    try
    {
        var target = Actor(100, 0, 100);
        var source = Actor(100, 0, 100);
        SetField(source, "m_wNativeBreakPower", (ushort)0);
        SetField(source, "m_wNativeCrazyPower", (ushort)0);
        NativeGlobalBreakSettings.SetSlot(
            NativeGlobalBreakSettings.BreakLevelIndex, 10);

        SetField(target, "m_wNativeBreakContestPool", (ushort)988);
        var random = UseRandom(0, 50);
        ContestResult normal = ResolverContest(target, source, 100, 100, 1);
        Equal(47, normal.Bonus, "normal resolver wrapper bonus");
        Equal((ushort)10, normal.Combined,
            "resolver wrapper global level input");
        Equal(0, normal.Extra, "resolver wrapper extra output");
        EqualSequence(new[] { 2, 100 }, random.MaxValues,
            "normal resolver wrapper RNG sequence");

        foreach (int skillId in new[] { 22, 127 })
        {
            SetField(target, "m_wNativeBreakContestPool", (ushort)988);
            random = UseRandom(0, 50);
            ContestResult filtered = ResolverContest(target, source,
                100, 100, skillId);
            Equal(0, filtered.Bonus,
                $"skill {skillId} retained resolver wrapper bonus");
            Equal((ushort)10, filtered.Combined,
                $"skill {skillId} skipped resolver wrapper call");
            Equal(0, filtered.Extra,
                $"skill {skillId} retained resolver wrapper extra");
            Equal((ushort)1_000, Field<ushort>(target,
                "m_wNativeBreakContestPool"),
                $"skill {skillId} skipped pool side effect");
            EqualSequence(new[] { 2, 100 }, random.MaxValues,
                $"skill {skillId} skipped RNG side effects");
        }
    }
    finally
    {
        NativeGlobalBreakSettings.Reset();
    }
}

static void VerifySourceContract()
{
    string root = FindRepoRoot();
    string path = Path.Combine(root, "GameSvr", "Actors",
        "TBaseObject.NativeBreakContest.cs");
    string source = File.ReadAllText(path);

    Contains(source, "NativeBreakContestGainByJob",
        "job gain table missing");
    Contains(source, "{ 100, 50, 80, 100 }",
        "job gain values changed");
    Contains(source, "{ 1.0f, 0.4f, 0.8f, 1.0f }",
        "job final scales changed");
    Contains(source, "Random(100) <= threshold",
        "fallback inclusive RNG condition missing");
    Contains(source, "Random(100) > chance",
        "final inclusive RNG condition missing");
    Contains(source, "MidpointRounding.ToEven",
        "nearest-even rounding missing");
    // Files are CRLF; pin the operator, not a particular line break.
    var expiryCollapsed = string.Join(' ', source.Split((char[])null,
        StringSplitOptions.RemoveEmptyEntries));
    Contains(expiryCollapsed, ") <= NativeBreakContestWindowMilliseconds",
        "unsigned elapsed <= window keep is missing");
    string resolverPath = Path.Combine(root, "GameSvr", "Actors",
        "TBaseObject.NativeMagicDamage.cs");
    string resolver = File.ReadAllText(resolverPath);
    int hq = resolver.IndexOf("ApplyNativeHumanHqReduction(",
        StringComparison.Ordinal);
    int percent = resolver.IndexOf(
        "ApplyNativeHumanMagicPercentReduction(", StringComparison.Ordinal);
    int contest = resolver.IndexOf("ApplyNativeHumanMagicBreakContest(",
        StringComparison.Ordinal);
    int middleHook = resolver.IndexOf("ApplyNativeSkill152OneShotBonus(",
        StringComparison.Ordinal);
    int breakExtra = resolver.IndexOf(
        "damage = unchecked(damage + breakExtra);", StringComparison.Ordinal);
    int breakBonus = resolver.IndexOf(
        "damage = unchecked(damage + breakBonus);", StringComparison.Ordinal);
    int state16Cap = resolver.IndexOf("ApplyNativeState16MagicDamageCap(",
        StringComparison.Ordinal);
    Assert(hq >= 0 && percent >= 0 && contest >= 0 && breakExtra >= 0 &&
        middleHook >= 0 && breakBonus >= 0 && state16Cap >= 0,
        "human resolver connected hook missing");
    int flags04Gate = resolver.LastIndexOf(
        "if ((effectiveFlags & 0x04) == 0)", hq,
        StringComparison.Ordinal);
    Assert(flags04Gate >= 0, "human resolver flags 0x04 gate missing");
    int flags04Open = resolver.IndexOf('{', flags04Gate);
    int flags04Close = FindMatchingBrace(resolver, flags04Open);
    Assert(flags04Gate >= 0 && flags04Open < hq && hq < percent &&
        percent < flags04Close && flags04Close < contest,
        "human resolver contest moved inside flags 0x04 HQ/percent gate");
    Assert(contest < breakExtra && breakExtra < breakBonus &&
        breakBonus < middleHook && middleHook < state16Cap,
        "human resolver extra/skill152/break-bonus/cap order changed");
    Assert(!resolver.Contains("(effectiveFlags & 0x0C) == 0",
        StringComparison.Ordinal),
        "flags 4/8 incorrectly gate the native Skill152 consumer");
}

static TBaseObject Actor(int level, byte job, int workingMaximum)
{
    var actor = (TBaseObject)RuntimeHelpers.GetUninitializedObject(
        typeof(TBaseObject));
    actor.m_Abil = new TAbility { Level = unchecked((ushort)level) };
    actor.m_WAbil = new TAbility();
    actor.m_btJob = job;
    actor.m_btRaceServer = Grobal2.RC_PLAYOBJECT;
    switch (job)
    {
        case 0:
            actor.m_WAbil.DC = HUtil32.MakeLong(0, workingMaximum);
            break;
        case 1:
            actor.m_WAbil.MC = HUtil32.MakeLong(0, workingMaximum);
            break;
        case 2:
            actor.m_WAbil.SC = HUtil32.MakeLong(0, workingMaximum);
            break;
        case 3:
            SetNativeCoreWorkingAbility(actor, "CCHigh", workingMaximum);
            break;
    }
    return actor;
}

static ContestResult Contest(TBaseObject target, TBaseObject source,
    int originalDamage, int currentDamage, int breakLevel, int crazyLevel,
    int mapBreak, int mapCrazy, int globalBaseChance, int globalBreak,
    int globalCrazy, int currentTick)
{
    Type byRefWord = typeof(ushort).MakeByRefType();
    Type byRefInt = typeof(int).MakeByRefType();
    Type[] parameterTypes =
    {
        typeof(TBaseObject), typeof(int), byRefWord, byRefInt, typeof(int),
        typeof(ushort), typeof(ushort), typeof(byte), typeof(ushort),
        typeof(int), typeof(int), typeof(int), typeof(int)
    };
    object[] arguments =
    {
        source, originalDamage, (ushort)0xFFFF, int.MaxValue, currentDamage,
        unchecked((ushort)breakLevel), unchecked((ushort)crazyLevel),
        unchecked((byte)mapBreak), unchecked((ushort)mapCrazy),
        globalBaseChance, globalBreak, globalCrazy, currentTick
    };
    int bonus = Invoke<int>(target, "ApplyNativeBreakContest",
        parameterTypes, arguments);
    return new ContestResult(bonus, (ushort)arguments[2],
        (int)arguments[3]);
}

static ContestResult ResolverContest(TBaseObject target, TBaseObject source,
    int originalDamage, int currentDamage, int skillId)
{
    Type byRefWord = typeof(ushort).MakeByRefType();
    Type byRefInt = typeof(int).MakeByRefType();
    object[] arguments =
    {
        source, originalDamage, currentDamage, skillId,
        (ushort)0xFFFF, int.MaxValue
    };
    int bonus = Invoke<int>(target, "ApplyNativeHumanMagicBreakContest",
        new[]
        {
            typeof(TBaseObject), typeof(int), typeof(int), typeof(int),
            byRefWord, byRefInt
        }, arguments);
    return new ContestResult(bonus, (ushort)arguments[4],
        (int)arguments[5]);
}

static int HumanPercentReduction(TBaseObject target, int damage) =>
    Invoke<int>(target, "ApplyNativeHumanMagicPercentReduction",
        new[] { typeof(int) }, damage);

static void ApplyEffectProperty(ushort propertyId, ushort value,
    TAddAbility addAbility)
{
    object[] arguments = { propertyId, value, addAbility };
    InvokeStatic<object>("ApplyNativeEffectProperty",
        new[]
        {
            typeof(ushort), typeof(ushort),
            typeof(TAddAbility).MakeByRefType()
        }, arguments);
}

static void ProcessPool(TBaseObject actor, int tick) => Invoke<object>(actor,
    "ProcessNativeBreakContestPool", new[] { typeof(int) }, tick);

static ushort Remaining(TBaseObject actor, int tick) => Invoke<ushort>(actor,
    "GetNativeBreakContestRemainingMilliseconds", new[] { typeof(int) },
    tick);

static void WritePool(TBaseObject actor, BinaryWriter writer, int tick) =>
    Invoke<object>(actor, "WriteNativeBreakContestPool",
        new[] { typeof(BinaryWriter), typeof(int) }, writer, tick);

static void ReadPool(TBaseObject actor, BinaryReader reader, int tick) =>
    Invoke<object>(actor, "ReadNativeBreakContestPool",
        new[] { typeof(BinaryReader), typeof(int) }, reader, tick);

static void RestorePool(TBaseObject actor, int remaining, int pool,
    int tick) => Invoke<object>(actor, "RestoreNativeBreakContestPool",
    new[] { typeof(ushort), typeof(ushort), typeof(int) },
    unchecked((ushort)remaining), unchecked((ushort)pool), tick);

static int NativeRound(double value) => InvokeStatic<int>(
    "RoundNativeBreakContest", new[] { typeof(double) }, value);

static T Invoke<T>(object instance, string name, Type[] parameterTypes,
    params object[] arguments)
{
    MethodInfo method = typeof(TBaseObject).GetMethod(name,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        null, parameterTypes, null) ??
        throw new MissingMethodException(typeof(TBaseObject).FullName, name);
    object result = method.Invoke(instance, arguments);
    return result == null ? default : (T)result;
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

static T Field<T>(object instance, string name) =>
    (T)FindField(instance.GetType(), name).GetValue(instance);

static void SetField(object instance, string name, object value) =>
    FindField(instance.GetType(), name).SetValue(instance, value);

static void SetNativeCoreWorkingAbility(TBaseObject actor, string name,
    int value)
{
    FieldInfo carrier = FindField(actor.GetType(),
        "m_NativeCoreWorkingAbility");
    object workingAbility = carrier.GetValue(actor);
    FindField(workingAbility.GetType(), name).SetValue(workingAbility, value);
    carrier.SetValue(actor, workingAbility);
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
// startup. It used to ride RandomNumber's private `random` field, which POIS-26
// removed when the facade moved onto the Delphi LCG sub_403B4C (@0x403B4C
// imul [0x7A2008],0x08088405 / inc / mul / take EDX); GetField then returned
// null and this threw MissingFieldException out of the very first assertion
// group, so none of the contest assertions below were running at all.
static FixedRandom UseRandom(params int[] values)
{
    var random = new FixedRandom(values);
    M2Share.RandomNumber = random;
    return random;
}

static void InitializeRuntime()
{
    M2Share.g_Config = new GameSvrConfig();
    M2Share.RandomNumber = RandomNumber.GetInstance();
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

static int FindMatchingBrace(string text, int openingBrace)
{
    Assert(openingBrace >= 0 && openingBrace < text.Length &&
        text[openingBrace] == '{', "resolver gate opening brace missing");
    int depth = 0;
    for (int index = openingBrace; index < text.Length; index++)
    {
        if (text[index] == '{') depth++;
        else if (text[index] == '}' && --depth == 0) return index;
    }
    throw new InvalidOperationException("resolver gate closing brace missing");
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

static void EqualSequence<T>(IReadOnlyList<T> expected,
    IReadOnlyList<T> actual, string message)
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

readonly record struct ContestResult(int Bonus, ushort Combined, int Extra);

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

    // The contest path draws only through the bounded entry. A parameterless
    // advance or a min/max draw would never reach MaxValues, so the ordinal
    // assertions would silently under-count instead of failing.
    public override int Random() => throw new InvalidOperationException(
        "unexpected parameterless RandSeed advance");

    public override int Random(int minValue, int maxValue) =>
        throw new InvalidOperationException("unexpected Random(min,max) draw");

    public override int GetRandomNumber(int minValue, int maxValue) =>
        throw new InvalidOperationException("unexpected GetRandomNumber draw");
}

sealed class FixedRecordActor : TBaseObject
{
    private byte[] _record;

    internal static FixedRecordActor Create(byte[] record)
    {
        var actor = (FixedRecordActor)RuntimeHelpers.GetUninitializedObject(
            typeof(FixedRecordActor));
        actor._record = record;
        return actor;
    }

    protected override ReadOnlySpan<byte> GetNativeFixedAbilityRecord() =>
        _record;
}
