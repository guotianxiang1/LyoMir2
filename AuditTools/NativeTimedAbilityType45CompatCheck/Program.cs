using System.Buffers.Binary;
using System.Reflection;
using System.Text.RegularExpressions;
using GameSvr;
using GameSvr.PasEngine;
using GameSvr.Services;
using SystemModule;

PrepareRuntimeConfig();
InitializeRuntime();

CheckAdmissionBoundary();
CheckFixedAndEquipmentBaseline();
CheckPlayerLifecycle();
CheckPasWordCoercionAndAlias();
CheckHeroLifecycle();
CheckNativeAdmissionGates();
CheckNearHitTableAndDamageConsumer();
CheckSourceContracts();

Console.WriteLine(
    "PASS timed-type45 PAS=player+hero+Word-coercion+low-byte-alias " +
    "internal=77 carrier=Int32-wrap refresh=lower/equal/higher " +
    "baseline=fixed@14A+item-property77+expiry-restore " +
    "packet=player+hero-near-hit@AA lifecycle=expiry-restore " +
    "hero=no-direct-3555 state16=allow state52=no-node " +
    "FASTNESS_NEARHit=parse+cap+hot-preserve " +
    "consumer=category4+not-first-classifier " +
    "fail-closed=46,74");
return;

void CheckAdmissionBoundary()
{
    var supported = typeof(TBaseObject).GetMethod(
        "IsSupportedTimedAbilityType",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("IsSupportedTimedAbilityType");

    bool IsSupported(int type) =>
        (bool)(supported.Invoke(null, new object[] { type }) ?? false);

    Assert(IsSupported(45), "script type45 admission");
    foreach (var type in new[] { 46, 74 })
        Assert(!IsSupported(type), $"script type{type} opened with type45");
}

void CheckFixedAndEquipmentBaseline()
{
    var items = M2Share.UserEngine.StdItemList;
    items.Clear();
    try
    {
        var player = NewProbePlayer("type45-baseline");
        player.m_NativeHumanData = new byte[0xEEF8];
        BinaryPrimitives.WriteUInt16LittleEndian(
            player.m_NativeHumanData.AsSpan(0x14A, sizeof(ushort)), 6);

        int tick = HUtil32.GetTickCount();
        player.ProcessTimedAbilities(tick);
        player.RecalcAbilitys();
        Equal(6, GetNearHitCarrier(player), "fixed 0x14A baseline");

        var stdItem = new GoodItem { Name = "type45-property77" };
        stdItem.NativeItemExtAbilIdents[0] = 77;
        stdItem.NativeItemExtAbilValues[0] = 5;
        items.Add(stdItem);
        player.m_UseItems[Grobal2.U_DRESS] = new TUserItem
        {
            wIndex = 1,
            Dura = 1,
            DuraMax = 1
        };
        player.RecalcAbilitys();
        Equal(11, GetNearHitCarrier(player),
            "fixed and item-property77 baseline");

        var bridge = new PasApiBridge { CurrentPlayer = player };
        Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(45, 7, 1)),
            "baseline PAS dispatch");
        player.ConsumePendingRecalc();
        Equal(18, GetNearHitCarrier(player),
            "fixed, item-property77 and timed type45 projection");
        Equal(18, BinaryPrimitives.ReadUInt16LittleEndian(
            BuildAbilityPacket(player).AsSpan(0xAA, sizeof(ushort))),
            "combined type45 ability packet field");

        int lastTick = GetTimedNodeField<int>(player, "LastTick");
        SetBaseField(player, "m_TimedAbilityProcessTick", lastTick);
        player.ProcessTimedAbilities(unchecked(lastTick + 1_500));
        player.ConsumePendingRecalc();
        Equal(11, GetNearHitCarrier(player),
            "type45 expiry did not restore fixed and item baseline");
    }
    finally
    {
        items.Clear();
    }
}

void CheckPlayerLifecycle()
{
    var player = NewProbePlayer("type45-player");
    var bridge = new PasApiBridge { CurrentPlayer = player };
    int tick = HUtil32.GetTickCount();
    player.ProcessTimedAbilities(tick);
    player.RecalcAbilitys();
    int recalcBaseline = player.RecalcCount;

    Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(45, 7, 10)),
        "player PAS dispatch");
    Assert(player.HasTimedAbility(45), "player type45 node");
    Assert(player.HasNativeActiveState(77), "player internal state77");
    Equal(7, player.GetTimedAbilityValue(45), "player type45 value");
    Equal(10_000, player.GetTimedAbilityRemainingMilliseconds(45),
        "player initial duration");
    Equal(recalcBaseline, player.RecalcCount,
        "player recalculated before deferred consumer");
    Equal(1, player.TimedClientStateCount, "player initial SM3555 count");
    Equal(77, player.LastTimedInternalType, "player initial SM3555 type");
    Assert(!player.LastTimedRemoved, "player initial SM3555 removal flag");

    player.ConsumePendingRecalc();
    Equal(recalcBaseline + 1, player.RecalcCount,
        "player deferred recalc count");
    Equal(7, GetNearHitCarrier(player), "player production recalc");
    Equal(7, BinaryPrimitives.ReadUInt16LittleEndian(
        BuildAbilityPacket(player).AsSpan(0xAA, sizeof(ushort))),
        "player ability packet near-hit field");

    Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(45, 6, 20)),
        "player lower refresh dispatch");
    player.ConsumePendingRecalc();
    Equal(7, player.GetTimedAbilityValue(45),
        "lower refresh replaced active value");
    Equal(10_000, player.GetTimedAbilityRemainingMilliseconds(45),
        "lower refresh replaced active duration");
    Equal(recalcBaseline + 1, player.RecalcCount,
        "lower refresh marked ability dirty");

    Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(45, 7, 20)),
        "player equal refresh dispatch");
    player.ConsumePendingRecalc();
    Equal(20_000, player.GetTimedAbilityRemainingMilliseconds(45),
        "equal refresh did not extend duration");
    Equal(recalcBaseline + 1, player.RecalcCount,
        "equal refresh marked ability dirty");

    Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(45, 8, 5)),
        "player higher refresh dispatch");
    Equal(8, player.GetTimedAbilityValue(45), "higher refresh value");
    Equal(5_000, player.GetTimedAbilityRemainingMilliseconds(45),
        "higher refresh duration");
    player.ConsumePendingRecalc();
    Equal(recalcBaseline + 2, player.RecalcCount,
        "higher refresh deferred recalc");
    Equal(8, GetNearHitCarrier(player), "higher refresh carrier");

    SetNearHitCarrier(player, int.MaxValue);
    ApplyTimedBonuses(player);
    Equal(unchecked(int.MaxValue + 8), GetNearHitCarrier(player),
        "type45 Int32 wrap");

    int lastTick = GetTimedNodeField<int>(player, "LastTick");
    SetBaseField(player, "m_TimedAbilityProcessTick", lastTick);
    player.ProcessTimedAbilities(unchecked(lastTick + 5_500));
    Assert(!player.HasNativeActiveState(77),
        "expired type45 retained state77");
    player.ConsumePendingRecalc();
    Assert(!player.HasTimedAbility(45), "player type45 did not expire");
    Equal(0, GetNearHitCarrier(player),
        "expired type45 did not restore near-hit carrier");
    Equal(5, player.TimedClientStateCount,
        "player refresh/removal SM3555 count");
    Assert(player.LastTimedRemoved, "player removal SM3555 flag");
    Equal(77, player.LastTimedInternalType, "player removal SM3555 type");
}

void CheckPasWordCoercionAndAlias()
{
    var alias = NewProbePlayer("type45-alias");
    var bridge = new PasApiBridge { CurrentPlayer = alias };
    Assert(bridge.CallPlayerMethod("AddPlayerAbil",
        Values(301, 65_540, 65_537)), "player low-byte alias dispatch");
    Assert(alias.HasTimedAbility(45), "alias301 did not map to type45");
    Assert(alias.HasNativeActiveState(77),
        "alias301 did not map to internal state77");
    Equal(4, alias.GetTimedAbilityValue(45), "alias Word value coercion");
    Equal(1_000, alias.GetTimedAbilityRemainingMilliseconds(45),
        "alias Word duration coercion");

    var maximum = NewProbePlayer("type45-word-max");
    var maximumBridge = new PasApiBridge { CurrentPlayer = maximum };
    Assert(maximumBridge.CallPlayerMethod("AddPlayerAbil",
        Values(45, ushort.MaxValue, ushort.MaxValue)),
        "player Word-max PAS dispatch");
    Equal(ushort.MaxValue, maximum.GetTimedAbilityValue(45),
        "player Word-max value");
    Equal(65_535_000, maximum.GetTimedAbilityRemainingMilliseconds(45),
        "player Word-max duration");
}

void CheckHeroLifecycle()
{
    var player = NewProbePlayer("type45-master");
    var hero = new Type45ProbeHero
    {
        m_Master = player,
        m_sCharName = "type45-hero"
    };
    player.m_HeroObject = hero;
    var bridge = new PasApiBridge { CurrentPlayer = player };
    int tick = HUtil32.GetTickCount();
    hero.ProcessTimedAbilities(tick);
    hero.RecalcAbilitys();
    int recalcBaseline = hero.RecalcCount;
    player.m_DefMsg = null;

    Assert(bridge.CallPlayerMethod("AddHeroAbil",
        Values(45, ushort.MaxValue, 1)), "hero PAS dispatch");
    Assert(hero.HasTimedAbility(45) && hero.HasNativeActiveState(77),
        "hero type45/internal77 state");
    Assert(!player.HasTimedAbility(45) && !player.HasNativeActiveState(77),
        "hero type45 state leaked to owner");
    Assert(player.m_DefMsg == null, "hero type45 sent direct SM3555 to owner");
    var heroTimedHook = typeof(HeroObject).GetMethod(
        "SendTimedAbilityClientState",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(heroTimedHook?.DeclaringType == typeof(TBaseObject),
        "hero overrides the player-only SM3555 hook");

    hero.ConsumePendingRecalc();
    Equal(recalcBaseline + 1, hero.RecalcCount,
        "hero deferred recalc count");
    Equal(ushort.MaxValue, GetNearHitCarrier(hero),
        "hero type45 carrier");
    Equal(ushort.MaxValue, BinaryPrimitives.ReadUInt16LittleEndian(
        BuildHeroAbilityPacket(hero).AsSpan(0xAA, sizeof(ushort))),
        "hero ability packet near-hit field");

    int lastTick = GetTimedNodeField<int>(hero, "LastTick");
    SetBaseField(hero, "m_TimedAbilityProcessTick", lastTick);
    hero.ProcessTimedAbilities(unchecked(lastTick + 1_500));
    hero.ConsumePendingRecalc();
    Assert(!hero.HasTimedAbility(45) && !hero.HasNativeActiveState(77),
        "hero removal retained type45/internal77");
    Equal(0, GetNearHitCarrier(hero),
        "expired hero type45 did not restore carrier");
    Assert(player.m_DefMsg == null,
        "hero type45 expiry sent direct SM3555 to owner");

    player.m_HeroObject = null;
    Assert(bridge.CallPlayerMethod("AddHeroAbil", Values(45, 1, 1)),
        "missing hero was not a successful no-op");
}

void CheckNativeAdmissionGates()
{
    var state16 = NewProbePlayer("type45-state16");
    Assert(state16.SetNativeActiveState(16), "state16 setup");
    var state16Bridge = new PasApiBridge { CurrentPlayer = state16 };
    Assert(state16Bridge.CallPlayerMethod("AddPlayerAbil",
        Values(45, 10, 60)), "state16 PAS call was not handled");
    Assert(state16.HasTimedAbility(45) && state16.HasNativeActiveState(77),
        "state16 incorrectly blocked internal77");

    var state52 = NewProbePlayer("type45-state52");
    Assert(state52.SetNativeActiveState(52), "state52 setup");
    var state52Bridge = new PasApiBridge { CurrentPlayer = state52 };
    Assert(state52Bridge.CallPlayerMethod("AddPlayerAbil",
        Values(45, 10, 60)), "state52 PAS call was not handled");
    Assert(!state52.HasTimedAbility(45) && !state52.HasNativeActiveState(77),
        "state52 admitted internal77");
}

void CheckNearHitTableAndDamageConsumer()
{
    var table = LoadNearHitTable("""
        # ignored
        ; ignored
        1 0.25 100
        2 0.50 30
        2 0.75 40
        4 0.10 999
        """);
    Equal(3, table.Count, "near-hit table duplicate/skip count");
    Equal(4, table.MaximumPositiveKey, "near-hit maximum key");
    Assert(table.TryResolve(5, out double cappedRatio, out int cappedLimit),
        "near-hit selector cap lookup");
    EqualDouble(0.10d, cappedRatio, "near-hit capped ratio");
    Equal(999, cappedLimit, "near-hit capped limit");
    Assert(!table.TryResolve(3, out _, out _),
        "near-hit missing selector resolved");
    Equal(25, table.CalculateReduction(102, 1),
        "near-hit x87 toward-zero half boundary");
    Equal(60, table.ApplyReduction(100, 2),
        "near-hit duplicate-last/cap reduction");

    string missing = Path.Combine(Path.GetTempPath(),
        $"m2-near-missing-{Guid.NewGuid():N}.txt");
    Assert(!table.Load(missing), "missing near-hit file unexpectedly loaded");
    Equal(3, table.Count, "missing near-hit file cleared hot table");

    M2Share.NativeFastnessNearHitTable = table;
    var target = NewDamageTarget();
    SetNearHitCarrier(target, 1);
    Equal(75, ResolveFullMagicDamage(target, 1, 4, 100),
        "category4 near-hit reduction");

    target = NewDamageTarget();
    SetNearHitCarrier(target, 1);
    Equal(100, ResolveFullMagicDamage(target, 70, 4, 100),
        "first-classifier near-hit bypass");

    target = NewDamageTarget();
    SetNearHitCarrier(target, 1);
    Equal(75, ResolveFullMagicDamage(target, 50, 4, 100),
        "second-classifier must not bypass near-hit table");

    target = NewDamageTarget();
    SetNearHitCarrier(target, 1);
    Equal(100, ResolveFullMagicDamage(target, 1, 3, 100),
        "non-category4 consumed near-hit table");
}

void CheckSourceContracts()
{
    string root = FindRepositoryRoot();
    string timed = File.ReadAllText(Path.Combine(root, "GameSvr", "Actors",
        "TBaseObject.TimedAbility.cs"));
    string damage = File.ReadAllText(Path.Combine(root, "GameSvr", "Actors",
        "TBaseObject.NativeMagicDamage.cs"));
    string app = File.ReadAllText(Path.Combine(root, "GameSvr", "GameApp.cs"));
    string packet = File.ReadAllText(Path.Combine(root, "GameSvr", "Actors",
        "TBaseObject.NativeAbilityPacket.cs"));
    string fixedAbility = File.ReadAllText(Path.Combine(root, "GameSvr", "Actors",
        "TBaseObject.NativeFixedAbility.cs"));
    string effectAbility = File.ReadAllText(Path.Combine(root, "GameSvr", "Actors",
        "TBaseObject.NativeEffectAbility.cs"));
    string baseAbility = File.ReadAllText(Path.Combine(root, "GameSvr", "Actors",
        "TBaseObject.Base.cs"));

    Require(timed, @"case\s+45\s*:\s*AddNativeNearHitFastness\(value\)",
        "type45 does not add the Int32 near-hit carrier");
    Require(damage,
        @"if\s*\(\s*!firstClassifier\s*&&\s*category\s*==\s*4\s*\)[\s\S]{0,180}?ApplyNativeFastnessNearHitReduction\(damage\)",
        "general damage resolver is missing the native type45 gate");
    Require(app, @"FASTNESS_NEARHit\.txt", "near-hit table path casing");
    Require(app,
        @"if\s*\(fastnessNearHitTable\.Load\(fastnessNearHitPath\)\)[\s\S]{0,180}?Volatile\.Write\(ref\s+M2Share\.NativeFastnessNearHitTable",
        "near-hit startup loader does not preserve the hot table on failure");
    Require(packet,
        @"Position\s*=\s*0xAA[\s\S]{0,100}?\(ushort\)m_nNativeNearHitFastness",
        "ability packet does not write the type45 low Word at 0xAA");
    Require(fixedAbility,
        @"NativeNearHitFastnessSelector\s*=\s*ReadNativeFixedUInt16\(record,\s*0x14A\)",
        "fixed record 0x14A is not seeded into type45 baseline");
    Require(effectAbility,
        @"case\s+77\s*:[\s\S]{0,180}?NativeNearHitFastnessSelector[\s\S]{0,100}?\+\s*value",
        "item extended property77 is not merged into type45 baseline");
    Require(baseAbility,
        @"m_nNativeNearHitFastness\s*=\s*m_AddAbil\.NativeNearHitFastnessSelector",
        "type45 baseline is not projected before timed bonuses");
}

static Type45ProbePlayer NewProbePlayer(string name) => new()
{
    m_boOffLineFlag = true,
    m_sCharName = name
};

static TBaseObject NewDamageTarget()
{
    var target = new TBaseObject();
    target.m_WAbil.HP = 1_000;
    target.m_WAbil.MaxHP = 1_000;
    target.m_WAbil.MP = 0;
    target.m_WAbil.MaxMP = 0;
    target.m_WAbil.AC = HUtil32.MakeLong(0, 0);
    target.m_WAbil.MAC = HUtil32.MakeLong(0, 0);
    return target;
}

static List<PasValue> Values(params int[] values) =>
    values.Select(PasValue.FromInt).ToList();

static byte[] BuildAbilityPacket(TBaseObject actor)
{
    var method = typeof(TBaseObject).GetMethod("BuildNativeAbilityPacket",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("BuildNativeAbilityPacket");
    return (byte[])(method.Invoke(actor, null)
        ?? throw new InvalidOperationException("native ability packet"));
}

static byte[] BuildHeroAbilityPacket(HeroObject hero)
{
    var method = typeof(HeroObject).GetMethod("BuildHeroAbility",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("BuildHeroAbility");
    return (byte[])(method.Invoke(hero, null)
        ?? throw new InvalidOperationException("hero ability packet"));
}

static int ResolveFullMagicDamage(TBaseObject target, int skillId,
    int category, int rawDamage)
{
    var method = typeof(TBaseObject).GetMethod("ResolveFullMagicDamage",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("ResolveFullMagicDamage");
    var parameters = method.GetParameters();
    var contextType = parameters[3].ParameterType;
    var empty = contextType.GetProperty("Empty", BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null)
                ?? throw new MissingMemberException(contextType.FullName, "Empty");
    return (int)(method.Invoke(target, new object[]
    {
        null,
        skillId,
        false,
        empty,
        unchecked((byte)category),
        1,
        rawDamage
    }) ?? 0);
}

static NativeFastnessTable LoadNearHitTable(string contents)
{
    string path = Path.Combine(Path.GetTempPath(),
        $"m2-near-{Guid.NewGuid():N}.txt");
    try
    {
        File.WriteAllText(path, contents);
        var table = new NativeFastnessTable();
        Assert(table.Load(path), "near-hit fixture load");
        return table;
    }
    finally
    {
        File.Delete(path);
    }
}

static int GetNearHitCarrier(TBaseObject actor) =>
    (int)(typeof(TBaseObject).GetField("m_nNativeNearHitFastness",
        BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(actor)
        ?? throw new MissingFieldException("m_nNativeNearHitFastness"));

static void SetNearHitCarrier(TBaseObject actor, int value)
{
    var field = typeof(TBaseObject).GetField("m_nNativeNearHitFastness",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException("m_nNativeNearHitFastness");
    field.SetValue(actor, value);
}

static void ApplyTimedBonuses(TBaseObject actor)
{
    var method = typeof(TBaseObject).GetMethod("ApplyTimedAbilityBonuses",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("ApplyTimedAbilityBonuses");
    method.Invoke(actor, null);
}

static T GetTimedNodeField<T>(TBaseObject actor, string name)
{
    var head = typeof(TBaseObject).GetField("m_TimedAbilityHead",
        BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(actor)
        ?? throw new MissingMemberException("m_TimedAbilityHead");
    var field = head.GetType().GetField(name)
        ?? throw new MissingFieldException(name);
    return (T)(field.GetValue(head)
        ?? throw new InvalidOperationException(name));
}

static void SetBaseField(TBaseObject actor, string name, object value)
{
    var field = typeof(TBaseObject).GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(name);
    field.SetValue(actor, value);
}

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

static void Require(string source, string pattern, string message) =>
    Assert(Regex.IsMatch(source, pattern, RegexOptions.Singleline), message);

static void InitializeRuntime()
{
    M2Share.g_Config = new GameSvrConfig();
    M2Share.UserEngine = new UserEngine();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.MapManager = new MapManager();
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

static void EqualDouble(double expected, double actual, string message)
{
    if (expected != actual)
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class Type45ProbePlayer : TPlayObject
{
    public int RecalcCount { get; private set; }
    public int TimedClientStateCount { get; private set; }
    public byte LastTimedInternalType { get; private set; }
    public bool LastTimedRemoved { get; private set; }

    public void ConsumePendingRecalc() => ConsumeAbilityRecalcPending();

    public override void RecalcAbilitys()
    {
        RecalcCount++;
        base.RecalcAbilitys();
    }

    protected override void SendTimedAbilityClientState(byte internalType,
        int remainingMilliseconds, int value, bool removed)
    {
        TimedClientStateCount++;
        LastTimedInternalType = internalType;
        LastTimedRemoved = removed;
        base.SendTimedAbilityClientState(internalType, remainingMilliseconds,
            value, removed);
    }
}

sealed class Type45ProbeHero : HeroObject
{
    public int RecalcCount { get; private set; }

    public void ConsumePendingRecalc() => ConsumeAbilityRecalcPending();

    public override void RecalcAbilitys()
    {
        RecalcCount++;
        base.RecalcAbilitys();
    }
}
