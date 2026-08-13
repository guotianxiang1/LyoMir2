using System.Buffers.Binary;
using System.Reflection;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

PrepareRuntimeConfig();
InitializeRuntime();

CheckSupportedLifecycle();
CheckHeadInsertionOrder();
CheckExpiryClear();
CheckClientSerialization();
CheckHealingBaselineIsIndependent();
CheckSourceContracts();

Console.WriteLine(
    "PASS timed-type64+68 open PAS=player+hero+low-byte-alias " +
    "internal96/100=head-order+dword+3555+expiry " +
    "combat=none healing=raw0xA8/+0x4F4-only");
return;

void CheckSupportedLifecycle()
{
    var isNative = typeof(TBaseObject).GetMethod(
        "IsNativeTimedAbilityType",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("IsNativeTimedAbilityType");
    var isSupported = typeof(TBaseObject).GetMethod(
        "IsSupportedTimedAbilityType",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("IsSupportedTimedAbilityType");

    foreach (int scriptType in new[] { 64, 68 })
    {
        Assert((bool)(isNative.Invoke(null, new object[] { scriptType }) ?? false),
            $"script type{scriptType} is no longer classified as native");
        Assert((bool)(isSupported.Invoke(null, new object[] { scriptType }) ?? false),
            $"script type{scriptType} is not open");
    }

    var player = NewProbePlayer("type64-68-player", 0, 0);
    var hero = new Type64ProbeHero
    {
        m_Master = player,
        m_sCharName = "type64-68-hero"
    };
    player.m_HeroObject = hero;
    var bridge = new PasApiBridge { CurrentPlayer = player };

    var cases = new[]
    {
        (Input: 64, Script: 64, Internal: 96, Value: 40),
        (Input: 320, Script: 64, Internal: 96, Value: 41),
        (Input: 68, Script: 68, Internal: 100, Value: 50),
        (Input: 324, Script: 68, Internal: 100, Value: 51)
    };
    foreach (var entry in cases)
    {
        Assert(bridge.CallPlayerMethod("AddPlayerAbil",
                Values(entry.Input, entry.Value, 60)),
            $"player type{entry.Input} was not dispatched");
        Assert(player.HasTimedAbility(entry.Script) &&
               player.HasNativeActiveState(entry.Internal),
            $"player type{entry.Input} did not create internal{entry.Internal}");
        Equal(entry.Value, player.GetTimedAbilityValue(entry.Script),
            $"player type{entry.Input} value");
        Assert(player.RemoveTimedAbility(entry.Script),
            $"player type{entry.Input} removal");
        Assert(!player.HasNativeActiveState(entry.Internal),
            $"player internal{entry.Internal} remained active");

        Assert(bridge.CallPlayerMethod("AddHeroAbil",
                Values(entry.Input, entry.Value, 60)),
            $"hero type{entry.Input} was not dispatched");
        Assert(hero.HasTimedAbility(entry.Script) &&
               hero.HasNativeActiveState(entry.Internal),
            $"hero type{entry.Input} did not create internal{entry.Internal}");
        Equal(entry.Value, hero.GetTimedAbilityValue(entry.Script),
            $"hero type{entry.Input} value");
        Assert(hero.RemoveTimedAbility(entry.Script),
            $"hero type{entry.Input} removal");
        Assert(!hero.HasNativeActiveState(entry.Internal),
            $"hero internal{entry.Internal} remained active");
    }

    Equal(0, CountTimedNodes(player), "player lifecycle retained nodes");
    Equal(0, CountTimedNodes(hero), "hero lifecycle retained nodes");
}

void CheckHeadInsertionOrder()
{
    var type64Then68 = new TBaseObject();
    type64Then68.AddTimedAbility(64, 40, 60);
    type64Then68.AddTimedAbility(68, 50, 60);
    Equal(190, type64Then68.GetTimedHolyDefense(100),
        "type64 then type68 must traverse internal100 before internal96");

    var type68Then64 = new TBaseObject();
    type68Then64.AddTimedAbility(68, 50, 60);
    type68Then64.AddTimedAbility(64, 40, 60);
    Equal(210, type68Then64.GetTimedHolyDefense(100),
        "type68 then type64 must traverse internal96 before internal100");

    var flatWrap = new TBaseObject();
    flatWrap.AddTimedAbility(64, 1, 60);
    Equal(int.MinValue, flatWrap.GetTimedHolyDefense(int.MaxValue),
        "internal96 dword wrap");

    // internal100 recompute @0x773A45, bytes verified against flat_image.bin:
    //   773A45  8B 87 14 03 00 00  mov  eax, [edi+0x314]
    //   773A4B  89 45 C4           mov  [ebp-0x3C], eax
    //   773A4E  33 C0 / 89 45 C8   mov  [ebp-0x38], 0      ; zero-extend -> (uint)
    //   773A53  DF 6D C4           fild qword [ebp-0x3C]   ; 0xFFFFFFFF -> 4294967295
    //   773A56  DB 43 0A           fild dword [ebx+0xA]    ; 50
    //   773A59  D8 35 94 3B 77 00  fdiv dword [0x773B94]   ; 00 00 C8 42 = 100.0f -> 0.5
    //   773A5F  DE C9              fmulp st(1)             ; 2147483647.5 exactly
    //   773A61  E8 1A FB C8 FF     call 0x403580           ; @TRUNC
    //   773A66  01 87 14 03 00 00  add  [edi+0x314], eax   ; low dword only
    // @TRUNC 0x403580 forces RC=11 (`66 81 4C 24 02 00 0F  or word [esp+2],0xF00`)
    // before `DF 7C 24 04  fistp qword [esp+4]`, i.e. toward zero: 2147483647.
    // 0xFFFFFFFF + 0x7FFFFFFF = 0x17FFFFFFE, and `add` keeps the low dword
    // 0x7FFFFFFE = 2147483646. This used to expect int.MaxValue, which is the
    // value the *sibling* helper 0x403574 would give (bare fistp on the default
    // control word 0x7A2024 = 0x1372, RC=00 round-half-to-even: 2147483647.5 ->
    // 2147483648 -> low dword 0x80000000 -> -1 + -2147483648 = int.MaxValue).
    // The product was corrected off 0x403580's bytes; this expectation was not,
    // so the pin was measuring the discarded model.
    var unsignedPercent = new TBaseObject();
    unsignedPercent.AddTimedAbility(68, 50, 60);
    Equal(2147483646, unsignedPercent.GetTimedHolyDefense(-1),
        "internal100 unsigned dword percentage and x87 truncation");

    // Second, smaller discriminator so the truncation direction is not pinned
    // only by the uint-wrap case: 3 * (50/100) = 1.5. @TRUNC 0x403580 gives 1
    // (3+1=4); @ROUND 0x403574 would give 2 (3+2=5).
    var halfwayTrunc = new TBaseObject();
    halfwayTrunc.AddTimedAbility(68, 50, 60);
    Equal(4, halfwayTrunc.GetTimedHolyDefense(3),
        "internal100 must truncate 1.5 toward zero, not round half to even");
}

void CheckExpiryClear()
{
    var actor = new TBaseObject();
    int tick = HUtil32.GetTickCount();
    actor.ProcessTimedAbilities(tick);
    actor.AddTimedAbility(64, 40, 1);
    actor.AddTimedAbility(68, 50, 1);
    Equal(190, actor.GetTimedHolyDefense(100), "expiry fixture value");

    actor.ProcessTimedAbilities(unchecked(tick + 1_500));
    Assert(!actor.HasTimedAbility(64) && !actor.HasNativeActiveState(96),
        "expired internal96 remained active");
    Assert(!actor.HasTimedAbility(68) && !actor.HasNativeActiveState(100),
        "expired internal100 remained active");
    Equal(0, CountTimedNodes(actor), "expired nodes remained linked");
    Equal(100, actor.GetTimedHolyDefense(100),
        "expired type64/type68 still changed the observable value");
}

void CheckClientSerialization()
{
    foreach (byte internalType in new byte[] { 96, 100 })
    {
        var added = BuildTimedAbilityPacket(internalType, 60_000, 1234, false);
        Equal(3555, added.Header.Ident, $"internal{internalType} add ident");
        Equal(60_000, added.Header.Recog,
            $"internal{internalType} add remaining");
        Equal(internalType, added.Header.Param,
            $"internal{internalType} add param");
        Equal(10, added.Body.Length, $"internal{internalType} add body length");
        Equal(internalType, added.Body[0],
            $"internal{internalType} add body type");
        Equal(0, added.Body[1], $"internal{internalType} add body flag");
        Equal(60_000, BinaryPrimitives.ReadInt32LittleEndian(
                added.Body.AsSpan(2, 4)),
            $"internal{internalType} add body remaining");
        Equal(1234, BinaryPrimitives.ReadInt32LittleEndian(
                added.Body.AsSpan(6, 4)),
            $"internal{internalType} add body value");

        var removed = BuildTimedAbilityPacket(internalType, 60_000, 1234, true);
        Equal(3555, removed.Header.Ident,
            $"internal{internalType} remove ident");
        Equal(0, removed.Header.Recog,
            $"internal{internalType} remove remaining");
        Equal(internalType, removed.Header.Param,
            $"internal{internalType} remove param");
        Equal(0, removed.Body.Length,
            $"internal{internalType} remove body length");
    }
}

void CheckHealingBaselineIsIndependent()
{
    var control = NewProbePlayer("type64-control", 100, 101);
    var admitted = NewProbePlayer("type64-admitted", 100, 101);
    control.RecalcAbilitys();
    admitted.RecalcAbilitys();
    Equal(100, GetHealingAmount(control), "raw0xA8 control projection");
    Equal(100, GetHealingAmount(admitted), "raw0xA8 admitted projection");

    var bridge = new PasApiBridge { CurrentPlayer = admitted };
    Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(64, 500, 60)),
        "type64 request was not admitted into the baseline fixture");
    Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(68, 50, 60)),
        "type68 request was not admitted into the baseline fixture");
    admitted.RecalcAbilitys();
    Equal(100, GetHealingAmount(admitted),
        "type64/type68 contaminated raw0xA8/+0x4F4 baseline");

    control.m_WAbil.HP = 100;
    control.m_WAbil.MaxHP = 1_000;
    admitted.m_WAbil.HP = 100;
    admitted.m_WAbil.MaxHP = 1_000;

    int controlHp = InvokeHealingDeterministically(control);
    int admittedHp = InvokeHealingDeterministically(admitted);
    Equal(198, controlHp, "raw0xA8 baseline healing result");
    Equal(controlHp, admittedHp,
        "type64/type68 changed raw0xA8 baseline healing result");

    var noBaseline = NewProbePlayer("type64-no-baseline", 0, 0);
    noBaseline.RecalcAbilitys();
    noBaseline.m_WAbil.HP = 100;
    noBaseline.m_WAbil.MaxHP = 1_000;
    var noBaselineBridge = new PasApiBridge { CurrentPlayer = noBaseline };
    Assert(noBaselineBridge.CallPlayerMethod("AddPlayerAbil",
            Values(64, 500, 60)),
        "type64 request was not admitted without a fixed baseline");
    Assert(noBaselineBridge.CallPlayerMethod("AddPlayerAbil",
            Values(68, 50, 60)),
        "type68 request was not admitted without a fixed baseline");
    noBaseline.RecalcAbilitys();
    Equal(0, GetHealingAmount(noBaseline),
        "type64/type68 created a healing carrier without raw0xA8");
    Equal(100, InvokeHealingDeterministically(noBaseline),
        "type64/type68 independently triggered healing");
}

void CheckSourceContracts()
{
    string root = FindRepositoryRoot();
    string timed = File.ReadAllText(Path.Combine(root, "GameSvr", "Actors",
        "TBaseObject.TimedAbility.cs"));
    string fixedAbility = File.ReadAllText(Path.Combine(root, "GameSvr",
        "Actors", "TBaseObject.NativeFixedAbility.cs"));
    string baseline = File.ReadAllText(Path.Combine(root, "GameSvr", "Actors",
        "TBaseObject.Base.cs"));
    string effects = File.ReadAllText(Path.Combine(root, "GameSvr", "Actors",
        "TBaseObject.NativeState26Effects.cs"));

    Assert(!timed.Contains("m_nNativeMagicHitHealAmount",
            StringComparison.Ordinal),
        "timed abilities still mutate the +0x4F4 healing carrier");
    Assert(timed.Contains("or 64 or 68 => true", StringComparison.Ordinal),
        "type64/type68 support gate was closed");
    Assert(timed.Contains("case 96:", StringComparison.Ordinal) &&
           timed.Contains("case 100:", StringComparison.Ordinal),
        "type64/type68 ordered dword projection was removed");
    // The old form of this contract required the literal "MidpointRounding.ToEven"
    // to appear in the file. That models 0x403574, the helper this path does NOT
    // call, and after the product moved to @TRUNC the string survived only inside
    // a prose comment - so the assertion kept passing while asserting nothing
    // (REPLICATION_RULES 4.17: grep-shaped contracts). Invert it: the round-half
    // API must be absent and the truncating helper must be the one cited.
    Assert(!timed.Contains("MidpointRounding", StringComparison.Ordinal),
        "internal100 reverted to a round-half model; 0x773A61 calls @TRUNC 0x403580");
    Assert(timed.Contains("0x403580", StringComparison.Ordinal),
        "internal100 no longer cites the truncating helper @0x403580");
    Assert(fixedAbility.Contains("record.Slice(0xA8",
            StringComparison.Ordinal),
        "raw0xA8 healing baseline projection was removed");
    Assert(baseline.Contains(
            "m_AddAbil.NativeMagicHitHealAmount",
            StringComparison.Ordinal),
        "recalc no longer projects raw0xA8 into the healing carrier");
    Assert(effects.Contains("ApplyNativeMagicHitHealing()",
            StringComparison.Ordinal) &&
           effects.Contains("m_nNativeMagicHitHealAmount",
            StringComparison.Ordinal),
        "the independent +0x4F4 healing consumer was removed");
}

static Type64ProbePlayer NewProbePlayer(string name, ushort amount,
    ushort chance)
{
    var record = new byte[0x22E];
    BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0xA8, 2), amount);
    BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0xAA, 2), chance);
    return new Type64ProbePlayer(record)
    {
        m_boOffLineFlag = true,
        m_sCharName = name
    };
}

// The deterministic source is installed on M2Share.RandomNumber, the field the
// server assigns at startup. It used to be installed by reflecting
// RandomNumber's private `random` field, which POIS-26 removed when the facade
// moved onto the Delphi LCG sub_403B4C; GetField then returned null and this
// threw MissingFieldException instead of running the healing assertions.
static int InvokeHealingDeterministically(TBaseObject actor)
{
    RandomNumber originalRandom = M2Share.RandomNumber;
    M2Share.RandomNumber = new Type64DeterministicRandom();
    try
    {
        var method = typeof(TBaseObject).GetMethod(
            "ApplyNativeMagicHitHealing",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("ApplyNativeMagicHitHealing");
        method.Invoke(actor, null);
        return actor.m_WAbil.HP;
    }
    finally
    {
        M2Share.RandomNumber = originalRandom;
    }
}

static int GetHealingAmount(TBaseObject actor) =>
    (int)(typeof(TBaseObject).GetField("m_nNativeMagicHitHealAmount",
        BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(actor)
        ?? throw new MissingFieldException("m_nNativeMagicHitHealAmount"));

static (ClientPacket Header, byte[] Body) BuildTimedAbilityPacket(byte type,
    int remaining, int value, bool removed)
{
    var method = typeof(TBaseObject).GetMethod(
        "BuildTimedAbilityClientState",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("BuildTimedAbilityClientState");
    var tuple = method.Invoke(null,
            new object[] { type, remaining, value, removed })
        ?? throw new InvalidOperationException("3555 builder returned null");
    var tupleType = tuple.GetType();
    var header = (ClientPacket)(tupleType.GetField("Item1")?.GetValue(tuple)
        ?? throw new MissingFieldException(tupleType.FullName, "Item1"));
    var body = (byte[])(tupleType.GetField("Item2")?.GetValue(tuple)
        ?? throw new MissingFieldException(tupleType.FullName, "Item2"));
    return (header, body);
}

static int CountTimedNodes(TBaseObject actor)
{
    var headField = typeof(TBaseObject).GetField("m_TimedAbilityHead",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException("m_TimedAbilityHead");
    var node = headField.GetValue(actor);
    if (node == null) return 0;
    var nextField = node.GetType().GetField("Next",
        BindingFlags.Instance | BindingFlags.Public)
        ?? throw new MissingFieldException("TimedAbilityNode.Next");
    var count = 0;
    while (node != null)
    {
        count++;
        node = nextField.GetValue(node);
    }
    return count;
}

static List<PasValue> Values(params int[] values) =>
    values.Select(PasValue.FromInt).ToList();

static string FindRepositoryRoot()
{
    foreach (var start in new[]
             { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        var directory = new DirectoryInfo(start);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameSvr",
                    "GameSvr.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
    }
    throw new DirectoryNotFoundException("repository root was not found");
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

static void Equal(int expected, int actual, string message)
{
    if (expected != actual)
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class Type64ProbePlayer : TPlayObject
{
    private readonly byte[] _fixedRecord;

    internal Type64ProbePlayer(byte[] fixedRecord)
    {
        _fixedRecord = fixedRecord;
    }

    protected override ReadOnlySpan<byte> GetNativeFixedAbilityRecord() =>
        _fixedRecord;
}

sealed class Type64ProbeHero : HeroObject
{
}

sealed class Type64DeterministicRandom : RandomNumber
{
    public override int Random() => 0;

    public override int Random(int Value)
    {
        if (Value <= 0 || Value == 100)
            return 0;
        if (Value == 2)
            return 1;
        return Value - 1;
    }
}
