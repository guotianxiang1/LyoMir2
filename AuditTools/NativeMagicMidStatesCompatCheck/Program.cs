using System.Reflection;
using GameSvr;
using SystemModule;

PrepareRuntimeConfig();
InitializeRuntime();

VerifyState56();
VerifyEffectiveSkill115();
VerifyState16InitialTables();
VerifyState16ContestTables();
VerifyState16ContestRngGates();
VerifyState16ContestSuccessAndTrace();
VerifyState83();
VerifyTargetMidStates();
VerifyFlagTraces();
VerifyShieldBridge();
VerifySourceContract();

Console.WriteLine(
    "PASS native-magic-mid-states state56=category3-trunc-half " +
    "state16=skill115-effective+initial-full-table+level-gap-contest " +
    "state83=saturating-fixed-reducer target=state53/30-x87 " +
    "flags=04/20/10-traces " +
    "skill153=generic-shield-bridge resolver=connected");
return;

static void VerifyState56()
{
    var actor = NewActor();
    Equal(100, State56(actor, 100, 3), "state56 absent node");

    actor = NewActor();
    actor.SetNativeActiveState(56);
    Equal(100, State56(actor, 100, 3), "state56 bit without node");

    actor = ActorWithTimedAbility(56, 5);
    Equal(102, State56(actor, 100, 3), "state56 odd half");
    Equal(105, State56(actor, 100, 2), "state56 full category");

    actor = ActorWithTimedAbility(56, -5);
    Equal(98, State56(actor, 100, 3),
        "state56 negative half must truncate toward zero");
    Equal(95, State56(actor, 100, 4), "state56 negative full");

    actor = ActorWithTimedAbility(56, 1);
    Equal(int.MinValue, State56(actor, int.MaxValue, 1),
        "state56 unchecked overflow");
}

static void VerifyEffectiveSkill115()
{
    var actor = NewActor();
    Equal(1, Effective115(actor), "skill115 absent default");

    SetSkill115(actor, 1, 7);
    Equal(1, Effective115(actor), "skill115 level one");

    actor = NewActor();
    SetSkill115(actor, 5, 2);
    Equal(2, Effective115(actor), "skill115 train cap");

    actor = NewActor();
    SetSkill115(actor, byte.MaxValue, 3, 2);
    Equal(1, Effective115(actor), "skill115 unchecked byte wrap");

    actor = NewActor();
    SetSkill115(actor, 0, 7);
    Equal(0, Effective115(actor), "skill115 explicit level zero");

    actor = NewActor();
    SetSkill115(actor, 8, 8);
    Equal(8, Effective115(actor), "skill115 level above contest table");
}

static void VerifyState16InitialTables()
{
    int[] primary = { 500, 600, 600, 700, 700, 700, 700 };
    int[] secondary = { 300, 400, 400, 500, 500, 500, 500 };
    foreach (int effectiveLevel in Enumerable.Range(0, 9))
    {
        foreach (byte category in Enumerable.Range(0, 7).Select(x =>
                     (byte)x))
        {
            int expected = category switch
            {
                1 or 4 or 5 => effectiveLevel is >= 1 and <= 7
                    ? primary[effectiveLevel - 1]
                    : 300,
                2 or 3 => effectiveLevel is >= 1 and <= 7
                    ? secondary[effectiveLevel - 1]
                    : 100,
                _ => 0
            };
            Equal(expected, Initial16Bonus(1, category, effectiveLevel),
                $"state16 initial level={effectiveLevel} category={category}");
        }
    }

    foreach (int skillId in new[] { 6, 22, 127, 65536 + 6,
                 -65536 + 22 })
    {
        foreach (byte category in new byte[] { 1, 2, 3, 4, 5 })
        {
            Equal(0, Initial16Bonus(skillId, category, 4),
                $"state16 initial excluded skill={skillId}");
        }
    }

    var actor = NewActor();
    SetSkill115(actor, 1, 7);
    Equal(100, ApplyInitial16(actor, 100, 1, 1),
        "state16 inactive apply");

    actor.SetNativeActiveState(16);
    Equal(600, ApplyInitial16(actor, 100, 1, 1),
        "state16 active primary apply");
    Equal(400, ApplyInitial16(actor, 100, 1, 2),
        "state16 active secondary apply");
    Equal(100, ApplyInitial16(actor, 100, 6, 1),
        "state16 excluded apply");
    Equal(unchecked(int.MaxValue + 500),
        ApplyInitial16(actor, int.MaxValue, 1, 1),
        "state16 initial unchecked overflow");

    actor = NewActor();
    actor.SetNativeActiveState(16);
    Equal(500, ApplyInitial16(actor, 0, 1, 1),
        "state16 absent skill defaults effective one");
}

static void VerifyState16ContestTables()
{
    int[] low = { 15, 25, 35, 45, 55, 65, 75, 75, 85, 85, 95, 95 };
    int[] middle = { 10, 18, 25, 33, 40, 48, 55, 55, 60, 60, 65, 65 };
    int[] high = { 3, 5, 7, 9, 11, 13, 15, 15, 17, 17, 19, 19 };

    for (int effectiveLevel = 0; effectiveLevel <= 7; effectiveLevel++)
    {
        for (int bin = -1; bin <= 12; bin++)
        {
            int expected = bin is < 0 or >= 12
                ? 0
                : effectiveLevel switch
                {
                    1 or 2 => low[bin],
                    3 or 4 => middle[bin],
                    5 or 6 => high[bin],
                    _ => 0
                };
            Equal(expected, ContestDenominator(effectiveLevel, bin),
                $"state16 contest level={effectiveLevel} bin={bin}");
        }
    }
}

static void VerifyState16ContestRngGates()
{
    var target = NewActor();
    var random = new RecordingRandom();
    Equal(9, Contest(target, null, 1, 9, random),
        "state16 null source gate");
    Equal(0, random.Calls, "state16 null source consumed RNG");

    var source = NewActor();
    random = new RecordingRandom();
    Equal(9, Contest(target, source, 1, 9, random),
        "state16 inactive source gate");
    Equal(0, random.Calls, "state16 inactive source consumed RNG");

    source.SetNativeActiveState(16);
    foreach (int skillId in new[] { 22, 127, 65536 + 22,
                 -65536 + 127 })
    {
        random = new RecordingRandom();
        Equal(9, Contest(target, source, skillId, 9, random),
            $"state16 contest excluded skill={skillId}");
        Equal(0, random.Calls,
            $"state16 excluded skill={skillId} consumed RNG");
    }

    source.m_Abil.Level = 601;
    target.m_Abil.Level = 1;
    random = new RecordingRandom();
    Equal(9, Contest(target, source, 1, 9, random),
        "state16 gap600 gate");
    Equal(0, random.Calls, "state16 gap600 consumed RNG");

    foreach (byte invalidLevel in new byte[] { 0, 7, 8 })
    {
        source = NewActor();
        target = NewActor();
        source.SetNativeActiveState(16);
        SetSkill115(source, invalidLevel, invalidLevel == 0
            ? (byte)7
            : invalidLevel);
        random = new RecordingRandom();
        Equal(9, Contest(target, source, 1, 9, random),
            $"state16 effective{invalidLevel} gate");
        Equal(0, random.Calls,
            $"state16 effective{invalidLevel} consumed RNG");
    }

    foreach (var sample in new[]
             {
                 (Gap: 49, Level: (byte)1, Expected: 15),
                 (Gap: 50, Level: (byte)3, Expected: 18),
                 (Gap: 599, Level: (byte)5, Expected: 19)
             })
    {
        source = NewActor();
        target = NewActor();
        source.SetNativeActiveState(16);
        SetSkill115(source, sample.Level, 7);
        source.m_Abil.Level = unchecked((ushort)(sample.Gap + 1));
        target.m_Abil.Level = 1;
        random = new RecordingRandom(1);
        Equal(9, Contest(target, source, 1, 9, random),
            $"state16 failed roll gap={sample.Gap}");
        EqualSequence(new[] { sample.Expected }, random.MaxValues,
            $"state16 denominator gap={sample.Gap}");
    }

    source = NewActor();
    target = NewActor();
    source.SetNativeActiveState(16);
    random = new RecordingRandom(0);
    Equal(8009, Contest(target, source, 6, 9, random),
        "state16 skill6 must remain contest eligible");
    EqualSequence(new[] { 15 }, random.MaxValues,
        "state16 skill6 RNG sequence");
}

static void VerifyState16ContestSuccessAndTrace()
{
    var source = NewActor();
    var target = NewActor();
    source.SetNativeActiveState(16);
    SetSkill115(source, 1, 7);
    var random = new RecordingRandom(0);
    Equal(8100, Contest(target, source, 1, 100, random),
        "state16 effective1 success bonus");
    Equal(-8100, TraceDamage(target), "state16 success trace damage");
    Equal("致命一击 -", TracePrefix(target),
        "state16 success trace prefix");

    source = NewActor();
    target = NewActor();
    source.SetNativeActiveState(16);
    target.SetNativeActiveState(16);
    SetSkill115(source, 2, 7);
    SetSkill115(target, 1, 7);
    random = new RecordingRandom(0);
    Equal(100, Contest(target, source, 1, 100, random),
        "state16 target cancellation must use source effective level");
    Equal(-10100, TraceDamage(target),
        "state16 trace must precede target cancellation");

    source = NewActor();
    target = NewActor();
    source.SetNativeActiveState(16);
    SetSkill115(source, 1, 7);
    random = new RecordingRandom(0);
    Equal(1000, Contest(target, source, 1, -7000, random),
        "state16 contest must accept nonpositive incoming damage");
    Equal(-1000, TraceDamage(target),
        "state16 negative incoming success trace");

    target.RecordNativeBreakthroughFlagTraceForAudit(4, 321);
    random = new RecordingRandom(1);
    Equal(50, Contest(target, source, 1, 50, random),
        "state16 failed roll changed damage");
    Equal(321, TraceDamage(target),
        "state16 failed roll changed existing trace");
    Equal("击破 -", TracePrefix(target),
        "state16 failed roll changed existing prefix");

    target = NewActor();
    random = new RecordingRandom(0);
    int expected = unchecked(int.MaxValue + 8000);
    Equal(expected, Contest(target, source, 1, int.MaxValue, random),
        "state16 success unchecked overflow");
    Equal(unchecked(-expected), TraceDamage(target),
        "state16 trace unchecked negation");
}

static void VerifyState83()
{
    var actor = NewActor();
    Equal(100, State83(actor, 100), "state83 absent node");
    actor.SetNativeActiveState(83);
    Equal(100, State83(actor, 100), "state83 bit without node");

    foreach (int value in new[] { -1, 0 })
    {
        actor = ActorWithTimedAbility(83, value);
        Equal(100, State83(actor, 100),
            $"state83 nonpositive value={value}");
    }

    actor = ActorWithTimedAbility(83, 40);
    Equal(60, State83(actor, 100), "state83 partial reduction");
    Equal(0, State83(actor, 0), "state83 zero entry");
    Equal(-7, State83(actor, -7), "state83 negative entry");

    actor = ActorWithTimedAbility(83, 100);
    Equal(0, State83(actor, 100), "state83 equal saturation");
    actor = ActorWithTimedAbility(83, 101);
    Equal(0, State83(actor, 100), "state83 greater saturation");
}

static void VerifyTargetMidStates()
{
    var actor = NewActor();
    Equal(7, TargetMid(actor, 7), "target mid absent states");

    actor.SetNativeActiveState(30);
    Equal(8, TargetMid(actor, 7), "state30 active-bit default value");

    actor = ActorWithTimedAbility(30, 4);
    Equal(9, TargetMid(actor, 7), "state30 value4 x87 rounding");

    actor = ActorWithTimedAbility(30, 3);
    Equal(6, TargetMid(actor, 5), "state30 non4 multiplier");

    actor.SetNativeActiveState(53);
    Equal(6, TargetMid(actor, 5),
        "state53 precedence and ties-to-even rounding");
    Equal(-6, TargetMid(actor, -5),
        "state53 signed ties-to-even rounding");
}

static void VerifyFlagTraces()
{
    var actor = NewActor();
    InvokeVoid(actor, "ResetNativeMagicTrace", Type.EmptyTypes);
    Equal(0, TraceDamage(actor), "trace reset damage");
    Equal(string.Empty, TracePrefix(actor), "trace reset prefix");

    BreakthroughTrace(actor, 0, 10);
    Equal(string.Empty, TracePrefix(actor), "flags0 created trace");
    BreakthroughTrace(actor, 4, -11);
    Equal(-11, TraceDamage(actor), "flags4 trace damage");
    Equal("击破 -", TracePrefix(actor), "flags4 trace prefix");

    BreakthroughTrace(actor, 2 | 8, 99);
    Equal(-11, TraceDamage(actor), "flags2/8 changed trace");
    PostTableTrace(actor, 0x10, 12);
    Equal(12, TraceDamage(actor), "flags10 trace damage");
    Equal("暴袭 -", TracePrefix(actor), "flags10 trace prefix");
    PostTableTrace(actor, 0x20, 13);
    Equal("狂击 -", TracePrefix(actor), "flags20 trace prefix");
    PostTableTrace(actor, 0x30, 14);
    Equal(14, TraceDamage(actor), "flags30 trace damage");
    Equal("狂击 -", TracePrefix(actor),
        "flags20 must win over flags10");

    PostTableTrace(actor, 2 | 8, 100);
    Equal(14, TraceDamage(actor), "post flags2/8 changed trace");
}

static void VerifyShieldBridge()
{
    var actor = NewActor();
    actor.m_btJob = M2Share.jWarr;
    actor.m_WAbil.DC = HUtil32.MakeLong(0, 10);
    SetField(actor, "m_wNativeSkill153ShieldCharges", (ushort)2);
    Equal(-25, ShieldBridge(actor, 0),
        "shield bridge must consume zero damage");
    Equal((ushort)1, GetField<ushort>(actor,
        "m_wNativeSkill153ShieldCharges"),
        "shield bridge zero damage charge");

    actor = NewActor();
    actor.m_btJob = 3;
    SetField(actor, "m_wNativeSkill153ShieldCharges", (ushort)1);
    Equal(7, ShieldBridge(actor, 7), "shield bridge job3 zero gap");
    Equal((ushort)0, GetField<ushort>(actor,
        "m_wNativeSkill153ShieldCharges"),
        "shield bridge job3 must still consume charge");

    actor = NewActor();
    actor.m_btJob = M2Share.jWarr;
    actor.m_WAbil.DC = HUtil32.MakeLong(0, 2);
    SetField(actor, "m_wNativeSkill153ShieldCharges", (ushort)1);
    Equal(-10, ShieldBridge(actor, -5),
        "shield bridge must not clamp negative damage");
}

static void VerifySourceContract()
{
    string root = FindRepositoryRoot();
    string path = Path.Combine(root, "GameSvr", "Actors",
        "TBaseObject.NativeMagicMidStates.cs");
    string source = File.ReadAllText(path);

    Contains(source, "value / 2", "state56 signed truncation missing");
    Contains(source, "unchecked((byte)(magic.btLevel + magic.NativeLevelBonus))",
        "skill115 byte wrap missing");
    Contains(source, "nativeSkillId is 6 or 22 or 127",
        "state16 initial exclusions missing");
    Contains(source, "nativeSkillId is 22 or 127",
        "state16 contest exclusions missing");
    Contains(source, "levelGap >= 600", "state16 gap bound missing");
    Contains(source, "levelGap / 50", "state16 gap bin missing");
    Contains(source, "random(denominator) != 0",
        "state16 exact RNG gate missing");
    Contains(source, "RecordNativeState16CriticalTrace(damage);",
        "state16 pre-cancellation trace missing");
    Contains(source, "ConsumeNativeSkill153ShieldCharge(damage)",
        "skill153 bridge missing");
    Contains(source, "damage * 1.3d", "state53 multiplier missing");
    Contains(source, "value == 4 ? 1.25d : 1.2d",
        "state30 multiplier split missing");
    Equal(1, Count(source, "M2Share.RandomNumber.Random"),
        "production RNG call-site count");
    Assert(!source.Contains("DelphiRandom", StringComparison.Ordinal),
        "dormant Delphi RNG connected");

    string resolver = File.ReadAllText(Path.Combine(root, "GameSvr",
        "Actors", "TBaseObject.NativeMagicDamage.cs"));
    int state56 = resolver.IndexOf("ApplyNativeState56MagicBonus",
        StringComparison.Ordinal);
    int state16Initial = resolver.IndexOf(
        "ApplyNativeState16InitialMagicBonus", StringComparison.Ordinal);
    int state83 = resolver.IndexOf("ApplyNativeState83MagicReduction",
        StringComparison.Ordinal);
    int targetMid = resolver.IndexOf("ApplyNativeTargetMidMagicStates",
        StringComparison.Ordinal);
    int state16Contest = resolver.IndexOf(
        "ApplyNativeState16LevelContest", StringComparison.Ordinal);
    int skill153 = resolver.IndexOf(
        "ApplyNativeSkill153ShieldToMagicDamage", StringComparison.Ordinal);
    Assert(state56 >= 0 && state16Initial > state56 &&
        state83 > state16Initial && targetMid > state83 &&
        state16Contest > targetMid && skill153 > state16Contest,
        "mid-state resolver order differs from sub_76CFC4");
}

static TBaseObject NewActor()
{
    var actor = new TBaseObject
    {
        m_PEnvir = new Envirnoment(),
        m_boObMode = true
    };
    actor.m_MsgList.Clear();
    return actor;
}

static TBaseObject ActorWithTimedAbility(byte internalType, int value)
{
    var actor = NewActor();
    bool added = (bool)Invoke(actor, "AddTimedAbilityInternal",
        new[] { typeof(byte), typeof(int), typeof(int), typeof(byte) },
        internalType, value, -1, (byte)0);
    Assert(added, $"timed ability {internalType} was rejected");
    actor.m_MsgList.Clear();
    return actor;
}

static void SetSkill115(TBaseObject actor, byte level, byte trainLevel,
    byte bonus = 0)
{
    var magic = new TUserMagic
    {
        btLevel = level,
        wMagIdx = 115,
        MagicInfo = new TMagic
        {
            wMagicID = 115,
            btTrainLv = trainLevel
        }
    };
    typeof(TUserMagic).GetField("NativeLevelBonus",
        BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(magic,
        bonus);
    actor.m_MagicList.Add(magic);
}

static int State56(TBaseObject actor, int damage, byte category) =>
    (int)Invoke(actor, "ApplyNativeState56MagicBonus",
        new[] { typeof(int), typeof(byte) }, damage, category);

static int Effective115(TBaseObject actor) =>
    (int)Invoke(actor, "GetNativeState16EffectiveLevel", Type.EmptyTypes);

static int Initial16Bonus(int skillId, byte category, int effectiveLevel) =>
    (int)InvokeStatic("GetNativeState16InitialMagicBonus",
        new[] { typeof(int), typeof(byte), typeof(int) }, skillId, category,
        effectiveLevel);

static int ApplyInitial16(TBaseObject actor, int damage, int skillId,
    byte category) =>
    (int)Invoke(actor, "ApplyNativeState16InitialMagicBonus",
        new[] { typeof(int), typeof(int), typeof(byte) }, damage, skillId,
        category);

static int ContestDenominator(int effectiveLevel, int levelGapBin) =>
    (int)InvokeStatic("GetNativeState16ContestDenominator",
        new[] { typeof(int), typeof(int) }, effectiveLevel, levelGapBin);

static int Contest(TBaseObject target, TBaseObject source, int skillId,
    int damage, RecordingRandom random) =>
    (int)Invoke(target, "ApplyNativeState16LevelContest",
        new[]
        {
            typeof(TBaseObject), typeof(int), typeof(int),
            typeof(Func<int, int>)
        }, source, skillId, damage, new Func<int, int>(random.Next));

static int State83(TBaseObject actor, int damage) =>
    (int)Invoke(actor, "ApplyNativeState83MagicReduction",
        new[] { typeof(int) }, damage);

static int TargetMid(TBaseObject actor, int damage) =>
    (int)Invoke(actor, "ApplyNativeTargetMidMagicStates",
        new[] { typeof(int) }, damage);

static int ShieldBridge(TBaseObject actor, int damage) =>
    (int)Invoke(actor, "ApplyNativeSkill153ShieldToMagicDamage",
        new[] { typeof(int) }, damage);

static void BreakthroughTrace(TBaseObject actor, int flags, int damage) =>
    InvokeVoid(actor, "RecordNativeBreakthroughFlagTrace",
        new[] { typeof(int), typeof(int) }, flags, damage);

static void PostTableTrace(TBaseObject actor, int flags, int damage) =>
    InvokeVoid(actor, "RecordNativePostTableFlagTrace",
        new[] { typeof(int), typeof(int) }, flags, damage);

static int TraceDamage(TBaseObject actor) =>
    GetField<int>(actor, "m_nNativeMagicTraceDamage");

static string TracePrefix(TBaseObject actor) =>
    GetField<string>(actor, "m_sNativeMagicTracePrefix");

static object Invoke(TBaseObject actor, string name, Type[] parameterTypes,
    params object[] arguments)
{
    MethodInfo method = typeof(TBaseObject).GetMethod(name,
        BindingFlags.Instance | BindingFlags.NonPublic, null,
        parameterTypes, null) ?? throw new MissingMethodException(name);
    return method.Invoke(actor, arguments)!;
}

static object InvokeStatic(string name, Type[] parameterTypes,
    params object[] arguments)
{
    MethodInfo method = typeof(TBaseObject).GetMethod(name,
        BindingFlags.Static | BindingFlags.NonPublic, null,
        parameterTypes, null) ?? throw new MissingMethodException(name);
    return method.Invoke(null, arguments)!;
}

static void InvokeVoid(TBaseObject actor, string name,
    Type[] parameterTypes, params object[] arguments) =>
    Invoke(actor, name, parameterTypes, arguments);

static void SetField(TBaseObject actor, string name, object value) =>
    (typeof(TBaseObject).GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic) ??
        throw new MissingFieldException(name)).SetValue(actor, value);

static T GetField<T>(TBaseObject actor, string name) =>
    (T)((typeof(TBaseObject).GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic) ??
        throw new MissingFieldException(name)).GetValue(actor) ??
        throw new InvalidOperationException($"{name} was null"));

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
    File.WriteAllText(Path.Combine(directory, "Command.conf"),
        "[Command]");
    string share = Path.GetFullPath(Path.Combine(directory, "..", "Share"));
    Directory.CreateDirectory(share);
    File.WriteAllText(Path.Combine(share, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]");
    File.WriteAllText(Path.Combine(share, "ServerData.ini"), "[Integer]");
}

static string FindRepositoryRoot()
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
    {
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
    }
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
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed class RecordingRandom
{
    private readonly Queue<int> _values;

    internal RecordingRandom(params int[] values)
    {
        _values = new Queue<int>(values);
    }

    internal List<int> MaxValues { get; } = new();
    internal int Calls => MaxValues.Count;

    internal int Next(int maximum)
    {
        MaxValues.Add(maximum);
        if (_values.Count == 0)
            throw new InvalidOperationException("unexpected RNG call");
        int value = _values.Dequeue();
        if ((uint)value >= (uint)maximum)
            throw new ArgumentOutOfRangeException(nameof(value));
        return value;
    }
}

static class TraceAuditExtensions
{
    internal static void RecordNativeBreakthroughFlagTraceForAudit(
        this TBaseObject actor, int flags, int damage)
    {
        MethodInfo method = typeof(TBaseObject).GetMethod(
            "RecordNativeBreakthroughFlagTrace",
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            new[] { typeof(int), typeof(int) }, null) ??
            throw new MissingMethodException(
                "RecordNativeBreakthroughFlagTrace");
        method.Invoke(actor, new object[] { flags, damage });
    }
}
