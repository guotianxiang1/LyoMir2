using System.Buffers.Binary;
using System.Reflection;
using System.Text.RegularExpressions;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

PrepareRuntimeConfig();
InitializeRuntime();

var supported = typeof(TBaseObject).GetMethod("IsSupportedTimedAbilityType",
    BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new MissingMethodException("IsSupportedTimedAbilityType");
Assert((bool)supported.Invoke(null, new object[] { 62 })!,
    "script type62 admission");
Assert((bool)supported.Invoke(null, new object[] { 61 })!,
    "script type61 admission");

CheckPlayerPasDispatchAndRefresh();
CheckPasWordCoercionAndAlias();
CheckFindPlayerTargetContext();
CheckHeroPasDispatch();
CheckNativeAdmissionGate();
CheckContestFormula();
CheckConsumerWiring();

Console.WriteLine(
    "PASS timed-type62 PAS=player+hero+Word-coercion+low-byte-alias " +
    "target-context=player+hero+restore " +
    "internal=94 carrier=UInt16-wrap refresh=lower/equal/higher " +
    "packet=player+hero-resistance@0A lifecycle=expiry-restore+zero-second " +
    "hero=no-direct-3555 " +
    "contest=single+area+direct+physical state52=no-node type61=independent-open");
return;

void CheckPlayerPasDispatchAndRefresh()
{
    var player = NewProbePlayer("type62-player");
    var bridge = new PasApiBridge { CurrentPlayer = player };
    int tick = HUtil32.GetTickCount();
    player.ProcessTimedAbilities(tick);
    player.RecalcAbilitys();
    int recalcBaseline = player.RecalcCount;
    ushort resistanceBaseline = player.m_wEffectResistance;

    Assert(bridge.CallPlayerMethod("AddPlayerAbil",
        Values(62, 4, 10)), "player PAS dispatch");
    Assert(player.HasTimedAbility(62), "player type62 node");
    Assert(player.HasNativeActiveState(94), "player internal state94");
    Equal(4, player.GetTimedAbilityValue(62), "player type62 value");
    Equal(10_000, player.GetTimedAbilityRemainingMilliseconds(62),
        "player initial duration");
    Equal(recalcBaseline, player.RecalcCount,
        "player recalculated before deferred consumer");
    Equal(1, player.TimedClientStateCount, "player initial SM3555 count");
    Equal(94, player.LastTimedInternalType, "player initial SM3555 type");
    Assert(!player.LastTimedRemoved, "player initial SM3555 removal flag");
    Equal(10_000, player.LastTimedRemainingMilliseconds,
        "player initial SM3555 duration");
    Equal(4, player.LastTimedValue, "player initial SM3555 value");

    player.ConsumePendingRecalc();
    Equal(recalcBaseline + 1, player.RecalcCount,
        "player deferred recalc count");
    Equal(unchecked((ushort)(resistanceBaseline + 4)),
        player.m_wEffectResistance, "player production recalc");
    var packet = BuildAbilityPacket(player);
    Equal(unchecked((ushort)(resistanceBaseline + 4)),
        BinaryPrimitives.ReadUInt16LittleEndian(
        packet.AsSpan(0x0A, sizeof(ushort))), "ability packet resistance");

    Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(62, 3, 20)),
        "player lower refresh dispatch");
    player.ConsumePendingRecalc();
    Equal(4, player.GetTimedAbilityValue(62),
        "lower refresh replaced active value");
    Equal(10_000, player.GetTimedAbilityRemainingMilliseconds(62),
        "lower refresh replaced active duration");
    Equal(recalcBaseline + 1, player.RecalcCount,
        "lower refresh marked ability dirty");

    Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(62, 4, 20)),
        "player equal refresh dispatch");
    player.ConsumePendingRecalc();
    Equal(20_000, player.GetTimedAbilityRemainingMilliseconds(62),
        "equal refresh did not extend duration");
    Equal(recalcBaseline + 1, player.RecalcCount,
        "equal refresh marked ability dirty");

    Assert(bridge.CallPlayerMethod("AddPlayerAbil",
        Values(62, ushort.MaxValue, 5)), "player higher refresh dispatch");
    Equal(ushort.MaxValue, player.GetTimedAbilityValue(62),
        "higher refresh value");
    Equal(5_000, player.GetTimedAbilityRemainingMilliseconds(62),
        "higher refresh duration");
    player.ConsumePendingRecalc();
    Equal(recalcBaseline + 2, player.RecalcCount,
        "higher refresh deferred recalc");
    Equal(unchecked((ushort)(resistanceBaseline + ushort.MaxValue)),
        player.m_wEffectResistance, "higher refresh resistance");

    player.m_wEffectResistance = 2;
    ApplyTimedBonuses(player);
    Equal(1, player.m_wEffectResistance, "type62 UInt16 wrap");

    int lastTick = GetTimedNodeField<int>(player, "LastTick");
    SetField(player, "m_TimedAbilityProcessTick", lastTick);
    player.ProcessTimedAbilities(unchecked(lastTick + 5_500));
    Assert(player.HasNativeActiveState(94) == false,
        "expired type62 retained state94");
    player.ConsumePendingRecalc();
    Assert(!player.HasTimedAbility(62) && !player.HasNativeActiveState(94),
        "player type62 did not expire");
    Equal(resistanceBaseline, player.m_wEffectResistance,
        "expired type62 did not restore resistance");
    Equal(5, player.TimedClientStateCount,
        "player refresh/removal SM3555 count");
    Assert(player.LastTimedRemoved, "player removal SM3555 flag");
    Equal(94, player.LastTimedInternalType, "player removal SM3555 type");
    Equal(0, player.LastTimedRemainingMilliseconds,
        "player removal SM3555 duration");
}

void CheckPasWordCoercionAndAlias()
{
    var alias = NewProbePlayer("type62-alias");
    var bridge = new PasApiBridge { CurrentPlayer = alias };
    Assert(bridge.CallPlayerMethod("AddPlayerAbil",
        Values(318, 65_540, 65_537)), "player low-byte alias dispatch");
    Assert(alias.HasTimedAbility(62), "alias318 did not map to type62");
    Equal(4, alias.GetTimedAbilityValue(62), "alias Word value coercion");
    Equal(1_000, alias.GetTimedAbilityRemainingMilliseconds(62),
        "alias Word duration coercion");

    var maximum = NewProbePlayer("type62-word-max");
    var maximumBridge = new PasApiBridge { CurrentPlayer = maximum };
    Assert(maximumBridge.CallPlayerMethod("AddPlayerAbil",
        Values(62, ushort.MaxValue, ushort.MaxValue)),
        "player Word-max PAS dispatch");
    Equal(ushort.MaxValue, maximum.GetTimedAbilityValue(62),
        "player Word-max value");
    Equal(65_535_000, maximum.GetTimedAbilityRemainingMilliseconds(62),
        "player Word-max duration");
}

void CheckFindPlayerTargetContext()
{
    var admin = NewProbePlayer("type62-admin");
    var target = NewProbePlayer("type62-target");
    var hero = new Type62ProbeHero
    {
        m_Master = target,
        m_sCharName = "type62-target-hero"
    };
    target.m_HeroObject = hero;
    PublishPlayer(target);

    var bridge = new PasApiBridge { CurrentPlayer = admin };
    const string source = """
        program Type62TargetContextProbe;
        procedure Apply;
        begin
          This_Player.FindPlayerByName('type62-target').AddPlayerAbil(62,254,65535);
          This_Player.FindPlayerByName('type62-target').AddHeroAbil(62,254,65535);
        end;
        procedure Clear;
        begin
          This_Player.FindPlayerByName('type62-target').AddPlayerAbil(62,255,0);
          This_Player.FindPlayerByName('type62-target').AddHeroAbil(62,255,0);
        end;
        begin
        end.
        """;
    var program = new PasParser(new PasLexer(source), FindRepositoryRoot()).Parse();
    var interpreter = new PasInterpreter(program, bridge);

    interpreter.ExecuteProcedure("Apply");
    Assert(ReferenceEquals(bridge.CurrentPlayer, admin),
        "FindPlayerByName apply did not restore caller context");
    Assert(!admin.HasTimedAbility(62),
        "FindPlayerByName type62 leaked to executing admin");
    Assert(target.HasTimedAbility(62) && target.HasNativeActiveState(94),
        "FindPlayerByName AddPlayerAbil missed target player");
    Assert(hero.HasTimedAbility(62) && hero.HasNativeActiveState(94),
        "FindPlayerByName AddHeroAbil missed target hero");
    Equal(254, target.GetTimedAbilityValue(62),
        "target player apply value");
    Equal(254, hero.GetTimedAbilityValue(62), "target hero apply value");
    Equal(65_535_000, target.GetTimedAbilityRemainingMilliseconds(62),
        "target player maximum duration");
    Equal(65_535_000, hero.GetTimedAbilityRemainingMilliseconds(62),
        "target hero maximum duration");

    interpreter.ExecuteProcedure("Clear");
    Assert(ReferenceEquals(bridge.CurrentPlayer, admin),
        "FindPlayerByName clear did not restore caller context");
    Equal(255, target.GetTimedAbilityValue(62),
        "target player zero-second refresh value");
    Equal(255, hero.GetTimedAbilityValue(62),
        "target hero zero-second refresh value");
    Equal(0, target.GetTimedAbilityRemainingMilliseconds(62),
        "target player zero-second duration");
    Equal(0, hero.GetTimedAbilityRemainingMilliseconds(62),
        "target hero zero-second duration");

    int tick = HUtil32.GetTickCount();
    SetField(target, "m_TimedAbilityProcessTick", tick);
    SetField(hero, "m_TimedAbilityProcessTick", tick);
    target.ProcessTimedAbilities(unchecked(tick + 500));
    hero.ProcessTimedAbilities(unchecked(tick + 500));
    Assert(!target.HasTimedAbility(62) && !target.HasNativeActiveState(94),
        "target player zero-second node survived first scan");
    Assert(!hero.HasTimedAbility(62) && !hero.HasNativeActiveState(94),
        "target hero zero-second node survived first scan");
}

void CheckHeroPasDispatch()
{
    var player = NewProbePlayer("type62-master");
    var hero = new Type62ProbeHero
    {
        m_Master = player,
        m_sCharName = "type62-hero"
    };
    player.m_HeroObject = hero;
    var bridge = new PasApiBridge { CurrentPlayer = player };
    int tick = HUtil32.GetTickCount();
    hero.ProcessTimedAbilities(tick);
    hero.RecalcAbilitys();
    int recalcBaseline = hero.RecalcCount;
    ushort resistanceBaseline = hero.m_wEffectResistance;
    player.m_DefMsg = null;

    Assert(bridge.CallPlayerMethod("AddHeroAbil",
        Values(62, ushort.MaxValue, 1)), "hero PAS dispatch");
    Assert(hero.HasTimedAbility(62) && hero.HasNativeActiveState(94),
        "hero type62/internal94 state");
    Assert(!player.HasTimedAbility(62) && !player.HasNativeActiveState(94),
        "hero type62 state leaked to owner");
    Equal(recalcBaseline, hero.RecalcCount,
        "hero recalculated before deferred consumer");
    Assert(player.m_DefMsg == null, "hero type62 sent direct SM3555 to owner");
    var heroTimedHook = typeof(HeroObject).GetMethod(
        "SendTimedAbilityClientState", BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(heroTimedHook?.DeclaringType == typeof(TBaseObject),
        "hero overrides the player-only SM3555 hook");

    hero.ConsumePendingRecalc();
    Equal(recalcBaseline + 1, hero.RecalcCount,
        "hero deferred recalc count");
    Equal(unchecked((ushort)(resistanceBaseline + ushort.MaxValue)),
        hero.m_wEffectResistance, "hero type62 UInt16 wrap");
    var heroPacket = BuildHeroAbilityPacket(hero);
    Equal(184, heroPacket.Length, "hero ability packet length");
    Equal(unchecked((ushort)(resistanceBaseline + ushort.MaxValue)),
        BinaryPrimitives.ReadUInt16LittleEndian(
            heroPacket.AsSpan(0x0A, sizeof(ushort))),
        "hero ability packet resistance");

    int lastTick = GetTimedNodeField<int>(hero, "LastTick");
    SetField(hero, "m_TimedAbilityProcessTick", lastTick);
    hero.ProcessTimedAbilities(unchecked(lastTick + 1_500));
    hero.ConsumePendingRecalc();
    Assert(!hero.HasTimedAbility(62) && !hero.HasNativeActiveState(94),
        "hero removal retained type62/internal94");
    Equal(resistanceBaseline, hero.m_wEffectResistance,
        "expired hero type62 did not restore resistance");
    Assert(player.m_DefMsg == null,
        "hero type62 expiry sent direct SM3555 to owner");

    player.m_HeroObject = null;
    Assert(bridge.CallPlayerMethod("AddHeroAbil", Values(62, 1, 1)),
        "missing hero was not a successful no-op");
}

void CheckNativeAdmissionGate()
{
    var player = NewProbePlayer("type62-state52");
    var bridge = new PasApiBridge { CurrentPlayer = player };
    Assert(player.SetNativeActiveState(52), "state52 setup");
    Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(62, 10, 60)),
        "state52 PAS call was not handled");
    Assert(!player.HasTimedAbility(62) && !player.HasNativeActiveState(94),
        "state52 admitted type62");
}

void CheckContestFormula()
{
    var method = typeof(TBaseObject).GetMethod(
        "GetNativeState26ContestRange",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("GetNativeState26ContestRange");

    int Range(ushort strength, ushort resistance, int baseRange) =>
        (int)(method.Invoke(null,
            new object[] { strength, resistance, baseRange }) ?? int.MinValue);

    Equal(7, Range(11, 10, 7), "strength greater than resistance");
    Equal(7, Range(10, 10, 7), "equal strength and resistance");
    Equal(8, Range(9, 10, 7), "resistance excess extends range");
    Equal(65_541, Range(0, ushort.MaxValue, 6),
        "maximum resistance integer range");
}

void CheckConsumerWiring()
{
    string root = FindRepositoryRoot();
    string effectSource = File.ReadAllText(Path.Combine(root, "GameSvr",
        "Actors", "TBaseObject.NativeState26Effects.cs"));
    string timedSource = File.ReadAllText(Path.Combine(root, "GameSvr",
        "Actors", "TBaseObject.TimedAbility.cs"));
    string bridgeSource = File.ReadAllText(Path.Combine(root, "GameSvr",
        "ScriptSystem", "PasEngine", "PasApiBridge.cs"));

    Require(timedSource,
        @"case\s+62\s*:\s*m_wEffectResistance\s*=\s*unchecked\s*\(\s*\(ushort\)\s*\(\s*m_wEffectResistance\s*\+\s*\(ushort\)value\s*\)\s*\)",
        "type62 is not an unchecked UInt16 resistance addition");
    Require(effectSource,
        @"ApplyNativeDirectMagicEffect[\s\S]{0,1400}?TryApplyNativeState26Direct\(target\)",
        "direct effect path is missing the resistance contest");
    Require(effectSource,
        @"ApplyNativeSingleMagicEffect[\s\S]{0,1800}?TryApplyNativeState26Single\(target\)",
        "single effect path is missing the resistance contest");
    Require(effectSource,
        @"ApplyNativeAreaMagicEffect[\s\S]{0,2600}?TryApplyNativeState26Single\(target\)",
        "area effect path is missing the resistance contest");
    Require(effectSource,
        @"GetNativeState26ContestRange\(m_wEffectStrength,\s*target\.m_wEffectResistance,\s*baseRange\)",
        "effect contest does not consume strength and resistance carriers");
    Require(effectSource,
        @"TryApplyNativeState26AfterPhysicalDamage[\s\S]{0,900}?target\.m_wEffectResistance\s*\+\s*5[\s\S]{0,500}?target\.m_wEffectResistance\s*\+\s*15",
        "physical effect path does not consume resistance");
    Require(bridgeSource,
        @"case\s+""addplayerabil""[\s\S]{0,900}?unchecked\s*\(\s*\(ushort\)args\[1\]\.AsInt\(\)\s*\)[\s\S]{0,180}?unchecked\s*\(\s*\(ushort\)args\[2\]\.AsInt\(\)\s*\)",
        "AddPlayerAbil does not coerce value/duration as Word");
    Require(bridgeSource,
        @"case\s+""addheroabil""[\s\S]{0,1100}?unchecked\s*\(\s*\(ushort\)args\[1\]\.AsInt\(\)\s*\)[\s\S]{0,180}?unchecked\s*\(\s*\(ushort\)args\[2\]\.AsInt\(\)\s*\)",
        "AddHeroAbil does not coerce value/duration as Word");
}

static Type62ProbePlayer NewProbePlayer(string name) => new()
{
    m_boOffLineFlag = true,
    m_sCharName = name
};

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

static void PublishPlayer(TPlayObject player)
{
    var field = typeof(UserEngine).GetField("m_PlayObjectList",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException("m_PlayObjectList");
    var players = (IList<TPlayObject>)(field.GetValue(M2Share.UserEngine)
        ?? throw new InvalidOperationException("m_PlayObjectList"));
    players.Add(player);
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

static void SetField(object target, string name, object value)
{
    var field = target.GetType().GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? typeof(TBaseObject).GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(name);
    field.SetValue(target, value);
}

static string FindRepositoryRoot()
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
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

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class Type62ProbePlayer : TPlayObject
{
    public int RecalcCount { get; private set; }
    public int TimedClientStateCount { get; private set; }
    public byte LastTimedInternalType { get; private set; }
    public int LastTimedRemainingMilliseconds { get; private set; }
    public int LastTimedValue { get; private set; }
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
        LastTimedRemainingMilliseconds = removed ? 0 : remainingMilliseconds;
        LastTimedValue = value;
        LastTimedRemoved = removed;
        base.SendTimedAbilityClientState(internalType, remainingMilliseconds,
            value, removed);
    }
}

sealed class Type62ProbeHero : HeroObject
{
    public int RecalcCount { get; private set; }

    public void ConsumePendingRecalc() => ConsumeAbilityRecalcPending();

    public override void RecalcAbilitys()
    {
        RecalcCount++;
        base.RecalcAbilitys();
    }
}
