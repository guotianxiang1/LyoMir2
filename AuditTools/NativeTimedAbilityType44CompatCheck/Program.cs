using System.Buffers.Binary;
using System.Reflection;
using System.Text.RegularExpressions;
using DBSvr.Core;
using GameSvr;
using GameSvr.PasEngine;
using GameSvr.Services;
using SystemModule;

PrepareRuntimeConfig();
InitializeRuntime();

CheckAdmissionAndPasAbi();
CheckFixedAndEquipmentBaseline();
CheckPlayerLifecycleAndProjection();
CheckHeroLifecycle();
CheckTwoPhaseExpiryOrder();
CheckUnionTableAndDamageConsumer();
CheckRuntimeOnlyState();
CheckSourceContracts();

Console.WriteLine(
    "PASS timed-type44 PAS=player+hero+Word-coercion+low-byte-alias " +
    "internal=76 carrier=Int32-wrap baseline=fixed@146+item-property75 " +
    "packet=player+hero@A8 lifecycle=500ms+oldest-first+runtime-only " +
    "player=SM3555 hero=no-direct-SM3555 " +
    "consumer=FASTNESS_UNION+raw154-flat+raw167-percent");
return;

static void CheckAdmissionAndPasAbi()
{
    var supported = typeof(TBaseObject).GetMethod(
        "IsSupportedTimedAbilityType",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("IsSupportedTimedAbilityType");

    bool IsSupported(int type) =>
        (bool)(supported.Invoke(null, new object[] { type }) ?? false);

    Assert(IsSupported(44), "script type44 admission");
    Assert(!IsSupported(46), "script type46 opened with type44");

    var player = NewPlayer("type44-alias");
    var bridge = new PasApiBridge { CurrentPlayer = player };
    Assert(bridge.CallPlayerMethod("AddPlayerAbil",
        Values(300, 65_540, 65_537)), "type300 alias dispatch");
    Assert(player.HasTimedAbility(44), "type300 did not alias type44");
    Assert(player.HasNativeActiveState(76), "type44 did not set internal76");
    Equal(4, player.GetTimedAbilityValue(44), "PAS Word value coercion");
    Equal(1_000, player.GetTimedAbilityRemainingMilliseconds(44),
        "PAS Word duration coercion");
    Equal(1, player.TimedStates.Count, "player initial SM3555 callback");
    Equal(76, player.TimedStates[0].InternalType,
        "player initial SM3555 internal type");
}

static void CheckFixedAndEquipmentBaseline()
{
    var items = M2Share.UserEngine.StdItemList;
    items.Clear();
    try
    {
        var player = NewPlayer("type44-baseline");
        player.m_NativeHumanData = new byte[NativeHumanDataCodec.DataRecordSize];
        BinaryPrimitives.WriteUInt16LittleEndian(
            player.m_NativeHumanData.AsSpan(0x146, sizeof(ushort)), 5);
        player.RecalcAbilitys();
        Equal(5, GetUnionCarrier(player), "fixed 0x146 baseline");

        var stdItem = new GoodItem { Name = "type44-property75" };
        stdItem.NativeItemExtAbilIdents[0] = 75;
        stdItem.NativeItemExtAbilValues[0] = 6;
        items.Add(stdItem);
        player.m_UseItems[Grobal2.U_DRESS] = new TUserItem
        {
            wIndex = 1,
            Dura = 1,
            DuraMax = 1
        };
        player.RecalcAbilitys();
        Equal(11, GetUnionCarrier(player),
            "fixed and item-property75 baseline");
    }
    finally
    {
        items.Clear();
    }
}

static void CheckPlayerLifecycleAndProjection()
{
    var player = NewPlayer("type44-player");
    player.m_NativeHumanData = new byte[NativeHumanDataCodec.DataRecordSize];
    BinaryPrimitives.WriteUInt16LittleEndian(
        player.m_NativeHumanData.AsSpan(0x146, sizeof(ushort)), 5);
    byte[] persistedBaseline = (byte[])player.m_NativeHumanData.Clone();
    player.RecalcAbilitys();
    Equal(5, GetUnionCarrier(player), "fixed 0x146 baseline");

    var bridge = new PasApiBridge { CurrentPlayer = player };
    int recalcBaseline = player.RecalcCount;
    Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(44, 7, 1)),
        "player type44 dispatch");
    Equal(recalcBaseline, player.RecalcCount,
        "type44 recalculated before deferred consumer");
    player.ConsumePendingRecalc();
    Equal(recalcBaseline + 1, player.RecalcCount,
        "type44 deferred recalc count");
    Equal(12, GetUnionCarrier(player), "fixed plus timed carrier");
    Equal(12, BinaryPrimitives.ReadUInt16LittleEndian(
        BuildAbilityPacket(player).AsSpan(0xA8, sizeof(ushort))),
        "player type44 packet projection");

    Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(44, 6, 2)),
        "lower refresh dispatch");
    player.ConsumePendingRecalc();
    Equal(7, player.GetTimedAbilityValue(44),
        "lower refresh replaced active value");
    Equal(1_000, player.GetTimedAbilityRemainingMilliseconds(44),
        "lower refresh replaced active duration");
    Equal(recalcBaseline + 1, player.RecalcCount,
        "lower refresh marked recalc dirty");

    Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(44, 7, 2)),
        "equal refresh dispatch");
    player.ConsumePendingRecalc();
    Equal(2_000, player.GetTimedAbilityRemainingMilliseconds(44),
        "equal refresh did not extend duration");
    Equal(recalcBaseline + 1, player.RecalcCount,
        "equal refresh marked recalc dirty");

    Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(44, 8, 1)),
        "higher refresh dispatch");
    player.ConsumePendingRecalc();
    Equal(13, GetUnionCarrier(player), "higher refresh carrier");
    Equal(recalcBaseline + 2, player.RecalcCount,
        "higher refresh deferred recalc");

    SetUnionCarrier(player, int.MaxValue);
    ApplyTimedBonuses(player);
    Equal(unchecked(int.MaxValue + 8), GetUnionCarrier(player),
        "type44 Int32 carrier wrap");

    int lastTick = GetTimedNodeField<int>(player, "LastTick");
    SetBaseField(player, "m_TimedAbilityProcessTick", lastTick);
    player.ProcessTimedAbilities(unchecked(lastTick + 1_500));
    player.ConsumePendingRecalc();
    Assert(!player.HasTimedAbility(44) &&
           !player.HasNativeActiveState(76),
        "expired type44/internal76 state");
    Equal(5, GetUnionCarrier(player),
        "expiry did not restore fixed baseline");
    Assert(player.TimedStates[^1].Removed,
        "player expiry did not emit removal SM3555 callback");
    Bytes(persistedBaseline, player.m_NativeHumanData,
        "timed type44 changed persisted human record");
}

static void CheckHeroLifecycle()
{
    var master = NewPlayer("type44-master");
    var hero = new Type44ProbeHero
    {
        m_Master = master,
        m_sCharName = "type44-hero"
    };
    master.m_HeroObject = hero;
    master.m_DefMsg = null;
    hero.RecalcAbilitys();
    int recalcBaseline = hero.RecalcCount;

    var bridge = new PasApiBridge { CurrentPlayer = master };
    Assert(bridge.CallPlayerMethod("AddHeroAbil",
        Values(44, ushort.MaxValue, 1)), "hero type44 dispatch");
    Assert(hero.HasTimedAbility(44) && hero.HasNativeActiveState(76),
        "hero type44/internal76 state");
    Assert(!master.HasTimedAbility(44), "hero state leaked to owner");
    Assert(master.m_DefMsg == null, "hero sent direct SM3555 to owner");
    var hook = typeof(HeroObject).GetMethod("SendTimedAbilityClientState",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(hook?.DeclaringType == typeof(TBaseObject),
        "hero overrides player-only SM3555 hook");

    hero.ConsumePendingRecalc();
    Equal(recalcBaseline + 1, hero.RecalcCount,
        "hero deferred recalc count");
    Equal(ushort.MaxValue, GetUnionCarrier(hero), "hero type44 carrier");
    Equal(ushort.MaxValue, BinaryPrimitives.ReadUInt16LittleEndian(
        BuildHeroAbilityPacket(hero).AsSpan(0xA8, sizeof(ushort))),
        "hero type44 packet projection");

    int lastTick = GetTimedNodeField<int>(hero, "LastTick");
    SetBaseField(hero, "m_TimedAbilityProcessTick", lastTick);
    hero.ProcessTimedAbilities(unchecked(lastTick + 1_500));
    hero.ConsumePendingRecalc();
    Assert(!hero.HasTimedAbility(44) && !hero.HasNativeActiveState(76),
        "hero expiry retained type44/internal76");
    Equal(0, GetUnionCarrier(hero), "hero expiry carrier");
    Assert(master.m_DefMsg == null,
        "hero expiry sent direct SM3555 to owner");
}

static void CheckTwoPhaseExpiryOrder()
{
    var player = NewPlayer("type44-order");
    player.AddTimedAbility(44, 1, 0);
    player.AddTimedAbility(0, 1, 0);
    player.ClearTimedStates();

    const int tick = 20_000;
    SetBaseField(player, "m_TimedAbilityProcessTick", tick);
    SetAllTimedNodeTicks(player, tick);
    player.ProcessTimedAbilities(tick + 500);

    Equal(2, player.TimedStates.Count, "batched expiry callback count");
    Equal(76, player.TimedStates[0].InternalType,
        "oldest type44 callback order");
    Equal(32, player.TimedStates[1].InternalType,
        "newest type0 callback order");
    Assert(player.TimedStates.All(state => state.Removed &&
           !state.Type44Present && !state.Type0Present),
        "callbacks observed a partially linked expiry batch");
}

static void CheckUnionTableAndDamageConsumer()
{
    NativeFastnessTable previous = M2Share.NativeFastnessUnionTable;
    try
    {
        var table = LoadUnionTable(
            "1 0.25 300" + Environment.NewLine +
            "3 0.50 100" + Environment.NewLine +
            "-2 0.10 200");
        Equal(3, table.Count, "union table row count");
        Equal(3, table.MaximumPositiveKey, "union table maximum key");
        Assert(table.TryResolve(99, out double ratio, out int limit),
            "union table high selector resolution");
        EqualDouble(0.50, ratio, "union table capped ratio");
        Equal(100, limit, "union table capped limit");
        Equal(900, table.ApplyReduction(1_000, 99),
            "union table high selector reduction");
        Equal(900, table.ApplyReduction(1_000, -2),
            "union table signed selector reduction");
        M2Share.NativeFastnessUnionTable = table;

        var target = NewPlayer("type44-target");
        target.m_NativeHumanData = new byte[NativeHumanDataCodec.DataRecordSize];
        BinaryPrimitives.WriteUInt16LittleEndian(
            target.m_NativeHumanData.AsSpan(0x146, sizeof(ushort)), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(
            target.m_NativeHumanData.AsSpan(0x154, sizeof(ushort)), 50);
        target.m_NativeHumanData[0x167] = 20;
        target.m_WAbil.AC = HUtil32.MakeLong(9999, 9999);
        target.m_WAbil.MAC = HUtil32.MakeLong(9999, 9999);
        target.RecalcAbilitys();

        var attacker = new HeroObject();
        Equal(560, ApplyUnionTargetDamage(target, attacker, 1_000),
            "union order table then flat then percent");
        SetUnionCarrier(target, 99);
        Equal(680, ApplyUnionTargetDamage(target, attacker, 1_000),
            "union capped selector with raw post reductions");

        var monster = new AnimalObject();
        Equal(3_000, ApplyUnionTargetDamage(monster, attacker, 1_000),
            "monster zero exp-hitter triple damage regression");
        monster.m_ExpHitterTick = 1;
        Equal(1_000, ApplyUnionTargetDamage(monster, attacker, 1_000),
            "monster nonzero exp-hitter damage regression");
    }
    finally
    {
        M2Share.NativeFastnessUnionTable = previous;
    }
}

static void CheckRuntimeOnlyState()
{
    var player = NewPlayer("type44-runtime-only");
    player.m_NativeHumanData = new byte[NativeHumanDataCodec.DataRecordSize];
    BinaryPrimitives.WriteUInt16LittleEndian(
        player.m_NativeHumanData.AsSpan(0x146, sizeof(ushort)), 9);
    byte[] before = (byte[])player.m_NativeHumanData.Clone();
    player.AddTimedAbility(44, 11, 60);
    player.ConsumePendingRecalc();
    Equal(20, GetUnionCarrier(player), "runtime-only active carrier");
    player.ClearTimedForExit();
    Assert(!player.HasTimedAbility(44) && !player.HasNativeActiveState(76),
        "exit retained type44/internal76 node");
    Bytes(before, player.m_NativeHumanData,
        "exit lifecycle persisted timed type44 state");
}

static void CheckSourceContracts()
{
    string root = FindRepositoryRoot();
    string timed = File.ReadAllText(Path.Combine(root, "GameSvr", "Actors",
        "TBaseObject.TimedAbility.cs"));
    string union = File.ReadAllText(Path.Combine(root, "GameSvr", "Actors",
        "TBaseObject.NativeFastnessUnion.cs"));
    string packet = File.ReadAllText(Path.Combine(root, "GameSvr", "Actors",
        "TBaseObject.NativeAbilityPacket.cs"));
    string baseAbility = File.ReadAllText(Path.Combine(root, "GameSvr", "Actors",
        "TBaseObject.Base.cs"));
    string fixedAbility = File.ReadAllText(Path.Combine(root, "GameSvr", "Actors",
        "TBaseObject.NativeFixedAbility.cs"));
    string effectAbility = File.ReadAllText(Path.Combine(root, "GameSvr", "Actors",
        "TBaseObject.NativeEffectAbility.cs"));
    string hero = File.ReadAllText(Path.Combine(root, "GameSvr", "Actors",
        "HeroObject.cs"));
    string app = File.ReadAllText(Path.Combine(root, "GameSvr", "GameApp.cs"));

    Require(timed, @"case\s+44\s*:\s*AddNativeUnionFastness\(value\)",
        "type44 timed value is not projected into union carrier");
    Require(baseAbility,
        @"m_nNativeUnionFastness\s*=\s*m_AddAbil\.NativeUnionFastnessSelector",
        "union baseline is not projected before timed bonuses");
    Require(fixedAbility,
        @"NativeUnionFastnessSelector\s*=\s*ReadNativeFixedUInt16\(record,\s*0x146\)",
        "fixed record 0x146 is not seeded into the union baseline");
    Require(effectAbility,
        @"case\s+75\s*:[\s\S]{0,180}?NativeUnionFastnessSelector[\s\S]{0,100}?\+\s*value",
        "item extended property75 is not merged into the union baseline");
    Require(packet,
        @"Position\s*=\s*0xA8[\s\S]{0,100}?m_nNativeUnionFastness",
        "ability packet does not write type44 low Word at 0xA8");
    Require(union, @"NativeUnionFlatReductionOffset\s*=\s*0x154",
        "union flat reduction raw offset");
    Require(union, @"NativeUnionPercentReductionOffset\s*=\s*0x167",
        "union percent reduction raw offset");
    Require(union,
        @"ApplyReduction\(damage,[\s\S]{0,120}?m_nNativeUnionFastness[\s\S]{0,500}?damage\s*=\s*unchecked\(damage\s*-\s*flatReduction\)[\s\S]{0,220}?damage\s*\*\s*multiplier",
        "union consumer order is not table then flat then percent");
    Require(hero,
        @"target\s+is\s+TPlayObject\s*\|\|\s*target\s+is\s+HeroObject[\s\S]{0,120}?ApplyNativeUnionDamageReductions",
        "player/hero union VMT consumer is not connected");
    Require(hero,
        @"ApplyNativeUnionTargetManaCost\(target,\s*attacker,[\s\S]{0,180}?return\s+ApplyNativeUnionTargetDamage\(target,\s*attacker",
        "union target mana side effect no longer precedes damage reductions");
    Require(app,
        @"FASTNESS_UNION\.txt[\s\S]{0,260}?if\s*\(fastnessUnionTable\.Load\(fastnessUnionPath\)\)[\s\S]{0,180}?Volatile\.Write\(ref\s+M2Share\.NativeFastnessUnionTable",
        "union table startup load does not preserve hot state on failure");
}

static Type44ProbePlayer NewPlayer(string name) => new()
{
    m_boOffLineFlag = true,
    m_sCharName = name
};

static List<PasValue> Values(params int[] values) =>
    values.Select(PasValue.FromInt).ToList();

static int GetUnionCarrier(TBaseObject actor) =>
    (int)(typeof(TBaseObject).GetField("m_nNativeUnionFastness",
        BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(actor)
        ?? throw new MissingFieldException("m_nNativeUnionFastness"));

static void SetUnionCarrier(TBaseObject actor, int value)
{
    var field = typeof(TBaseObject).GetField("m_nNativeUnionFastness",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException("m_nNativeUnionFastness");
    field.SetValue(actor, value);
}

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

static int ApplyUnionTargetDamage(TBaseObject target, TBaseObject attacker,
    int damage)
{
    var method = typeof(HeroObject).GetMethod(
        "ApplyNativeUnionTargetDamage",
        BindingFlags.Static | BindingFlags.NonPublic, null,
        new[] { typeof(TBaseObject), typeof(TBaseObject), typeof(int) }, null)
        ?? throw new MissingMethodException("ApplyNativeUnionTargetDamage");
    return (int)(method.Invoke(null, new object[] { target, attacker, damage })
        ?? throw new InvalidOperationException("union target damage"));
}

static NativeFastnessTable LoadUnionTable(string contents)
{
    string path = Path.Combine(Path.GetTempPath(),
        $"m2-union-{Guid.NewGuid():N}.txt");
    try
    {
        File.WriteAllText(path, contents);
        var table = new NativeFastnessTable();
        Assert(table.Load(path), "union table fixture load");
        return table;
    }
    finally
    {
        File.Delete(path);
    }
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
    object head = typeof(TBaseObject).GetField("m_TimedAbilityHead",
        BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(actor)
        ?? throw new MissingMemberException("m_TimedAbilityHead");
    var field = head.GetType().GetField(name)
        ?? throw new MissingFieldException(name);
    return (T)(field.GetValue(head)
        ?? throw new InvalidOperationException(name));
}

static void SetAllTimedNodeTicks(TBaseObject actor, int tick)
{
    object node = typeof(TBaseObject).GetField("m_TimedAbilityHead",
        BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(actor);
    while (node != null)
    {
        var type = node.GetType();
        (type.GetField("LastTick")
            ?? throw new MissingFieldException("LastTick")).SetValue(node, tick);
        node = (type.GetField("Next")
            ?? throw new MissingFieldException("Next")).GetValue(node);
    }
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
    foreach (string start in new[]
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

static void Bytes(byte[] expected, byte[] actual, string message)
{
    if (!expected.AsSpan().SequenceEqual(actual))
        throw new InvalidOperationException(message);
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
    if (!condition)
        throw new InvalidOperationException(message);
}

readonly record struct TimedState(byte InternalType, bool Removed,
    bool Type44Present, bool Type0Present);

sealed class Type44ProbePlayer : TPlayObject
{
    public int RecalcCount { get; private set; }
    public List<TimedState> TimedStates { get; } = new();

    public void ConsumePendingRecalc() => ConsumeAbilityRecalcPending();
    public void ClearTimedForExit() => ClearTimedAbilitiesOnExit();
    public void ClearTimedStates() => TimedStates.Clear();

    public override void RecalcAbilitys()
    {
        RecalcCount++;
        base.RecalcAbilitys();
    }

    protected override void SendTimedAbilityClientState(byte internalType,
        int remainingMilliseconds, int value, bool removed)
    {
        TimedStates.Add(new TimedState(internalType, removed,
            HasTimedAbility(44), HasTimedAbility(0)));
        base.SendTimedAbilityClientState(internalType, remainingMilliseconds,
            value, removed);
    }
}

sealed class Type44ProbeHero : HeroObject
{
    public int RecalcCount { get; private set; }

    public void ConsumePendingRecalc() => ConsumeAbilityRecalcPending();

    public override void RecalcAbilitys()
    {
        RecalcCount++;
        base.RecalcAbilitys();
    }
}
