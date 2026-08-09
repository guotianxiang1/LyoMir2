using System.Buffers.Binary;
using System.Reflection;
using System.Text.RegularExpressions;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

PrepareRuntimeConfig();
InitializeRuntime();

CheckPlayerPasDispatchAndLifecycle();
CheckPasWordCoercionAndAlias();
CheckHeroPasDispatch();
CheckNativeAdmissionGate();
CheckConsumerWiring();

Console.WriteLine(
    "PASS timed-type61 PAS=player+hero+Word-coercion+low-byte-alias " +
    "internal=93 carrier=UInt16-wrap item-baseline=additive " +
    "refresh=lower/equal/higher lifecycle=expiry-restore+zero-second " +
    "packet=player+hero-strength@80-dword hero=no-direct-3555 " +
    "contest=single+area+direct state52=no-node");
return;

void CheckPlayerPasDispatchAndLifecycle()
{
    ResetStrengthItem(65_534);
    var player = NewProbePlayer("type61-player");
    EquipStrengthItem(player);
    var bridge = new PasApiBridge { CurrentPlayer = player };
    int tick = HUtil32.GetTickCount();
    player.ProcessTimedAbilities(tick);
    player.RecalcAbilitys();
    int recalcBaseline = player.RecalcCount;
    Equal(65_534, player.m_wEffectStrength, "player item strength baseline");

    Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(61, 5, 10)),
        "player PAS dispatch");
    Assert(player.HasTimedAbility(61), "player type61 node");
    Assert(player.HasNativeActiveState(93), "player internal state93");
    Equal(5, player.GetTimedAbilityValue(61), "player type61 value");
    Equal(10_000, player.GetTimedAbilityRemainingMilliseconds(61),
        "player initial duration");
    Equal(recalcBaseline, player.RecalcCount,
        "player recalculated before deferred consumer");
    Equal(1, player.TimedClientStateCount, "player initial SM3555 count");
    Equal(93, player.LastTimedInternalType, "player initial SM3555 type");

    player.ConsumePendingRecalc();
    Equal(recalcBaseline + 1, player.RecalcCount,
        "player deferred recalc count");
    Equal(3, player.m_wEffectStrength,
        "player item baseline plus timed UInt16 wrap");
    var packet = BuildAbilityPacket(player);
    Equal(184, packet.Length, "player ability packet length");
    EqualUInt(3, BinaryPrimitives.ReadUInt32LittleEndian(
        packet.AsSpan(0x80, sizeof(uint))), "player packet strength dword");

    Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(61, 4, 20)),
        "player lower refresh dispatch");
    player.ConsumePendingRecalc();
    Equal(5, player.GetTimedAbilityValue(61),
        "lower refresh replaced active value");
    Equal(10_000, player.GetTimedAbilityRemainingMilliseconds(61),
        "lower refresh replaced active duration");
    Equal(recalcBaseline + 1, player.RecalcCount,
        "lower refresh marked ability dirty");

    Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(61, 5, 20)),
        "player equal refresh dispatch");
    player.ConsumePendingRecalc();
    Equal(20_000, player.GetTimedAbilityRemainingMilliseconds(61),
        "equal refresh did not extend duration");
    Equal(recalcBaseline + 1, player.RecalcCount,
        "equal refresh marked ability dirty");

    Assert(bridge.CallPlayerMethod("AddPlayerAbil",
        Values(61, ushort.MaxValue, 5)), "player higher refresh dispatch");
    Equal(ushort.MaxValue, player.GetTimedAbilityValue(61),
        "higher refresh value");
    Equal(5_000, player.GetTimedAbilityRemainingMilliseconds(61),
        "higher refresh duration");
    player.ConsumePendingRecalc();
    Equal(recalcBaseline + 2, player.RecalcCount,
        "higher refresh deferred recalc");
    Equal(65_533, player.m_wEffectStrength,
        "higher refresh item baseline addition");

    player.m_wEffectStrength = 2;
    ApplyTimedBonuses(player);
    Equal(1, player.m_wEffectStrength, "type61 UInt16 wrap");

    int lastTick = GetTimedNodeField<int>(player, "LastTick");
    SetField(player, "m_TimedAbilityProcessTick", lastTick);
    player.ProcessTimedAbilities(unchecked(lastTick + 5_500));
    Assert(!player.HasNativeActiveState(93),
        "expired type61 retained state93");
    player.ConsumePendingRecalc();
    Assert(!player.HasTimedAbility(61), "player type61 did not expire");
    Equal(65_534, player.m_wEffectStrength,
        "expired type61 did not restore item baseline");
    Equal(5, player.TimedClientStateCount,
        "player refresh/removal SM3555 count");
    Assert(player.LastTimedRemoved, "player removal SM3555 flag");
    Equal(93, player.LastTimedInternalType, "player removal SM3555 type");
}

void CheckPasWordCoercionAndAlias()
{
    ResetStrengthItem(0);
    var alias = NewProbePlayer("type61-alias");
    var bridge = new PasApiBridge { CurrentPlayer = alias };
    Assert(bridge.CallPlayerMethod("AddPlayerAbil",
        Values(317, 65_540, 65_537)), "player low-byte alias dispatch");
    Assert(alias.HasTimedAbility(61) && alias.HasNativeActiveState(93),
        "alias317 did not map to type61/internal93");
    Equal(4, alias.GetTimedAbilityValue(61), "alias Word value coercion");
    Equal(1_000, alias.GetTimedAbilityRemainingMilliseconds(61),
        "alias Word duration coercion");

    var zero = NewProbePlayer("type61-zero-second");
    var zeroBridge = new PasApiBridge { CurrentPlayer = zero };
    Assert(zeroBridge.CallPlayerMethod("AddPlayerAbil", Values(61, 1, 0)),
        "zero-second PAS dispatch");
    Assert(zero.HasTimedAbility(61),
        "zero-second state expired before timed scan");
    int tick = HUtil32.GetTickCount();
    SetField(zero, "m_TimedAbilityProcessTick", tick);
    zero.ProcessTimedAbilities(unchecked(tick + 500));
    Assert(!zero.HasTimedAbility(61) && !zero.HasNativeActiveState(93),
        "zero-second state survived eligible scan");
}

void CheckHeroPasDispatch()
{
    ResetStrengthItem(10);
    var player = NewProbePlayer("type61-master");
    var hero = new Type61ProbeHero
    {
        m_Master = player,
        m_sCharName = "type61-hero"
    };
    EquipStrengthItem(hero);
    player.m_HeroObject = hero;
    var bridge = new PasApiBridge { CurrentPlayer = player };
    hero.RecalcAbilitys();
    int recalcBaseline = hero.RecalcCount;
    Equal(10, hero.m_wEffectStrength, "hero item strength baseline");
    player.m_DefMsg = null;

    Assert(bridge.CallPlayerMethod("AddHeroAbil",
        Values(61, ushort.MaxValue, 1)), "hero PAS dispatch");
    Assert(hero.HasTimedAbility(61) && hero.HasNativeActiveState(93),
        "hero type61/internal93 state");
    Assert(!player.HasTimedAbility(61), "hero type61 leaked to owner");
    Assert(player.m_DefMsg == null, "hero type61 sent direct SM3555 to owner");
    var heroTimedHook = typeof(HeroObject).GetMethod(
        "SendTimedAbilityClientState", BindingFlags.Instance |
        BindingFlags.NonPublic);
    Assert(heroTimedHook?.DeclaringType == typeof(TBaseObject),
        "hero overrides the player-only SM3555 hook");

    hero.ConsumePendingRecalc();
    Equal(recalcBaseline + 1, hero.RecalcCount,
        "hero deferred recalc count");
    Equal(9, hero.m_wEffectStrength, "hero type61 UInt16 wrap");
    var packet = BuildHeroAbilityPacket(hero);
    Equal(184, packet.Length, "hero ability packet length");
    EqualUInt(9, BinaryPrimitives.ReadUInt32LittleEndian(
        packet.AsSpan(0x80, sizeof(uint))), "hero packet strength dword");

    var noHero = NewProbePlayer("type61-no-hero");
    Assert(new PasApiBridge { CurrentPlayer = noHero }.CallPlayerMethod(
        "AddHeroAbil", Values(61, 1, 1)), "missing hero was not silent no-op");
}

void CheckNativeAdmissionGate()
{
    var player = NewProbePlayer("type61-state52");
    var bridge = new PasApiBridge { CurrentPlayer = player };
    Assert(player.SetNativeActiveState(52), "state52 setup");
    Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(61, 10, 60)),
        "state52 PAS call was not handled");
    Assert(!player.HasTimedAbility(61) && !player.HasNativeActiveState(93),
        "state52 admitted type61");
}

void CheckConsumerWiring()
{
    var method = typeof(TBaseObject).GetMethod(
        "GetNativeState26ContestRange", BindingFlags.Static |
        BindingFlags.NonPublic)
        ?? throw new MissingMethodException("GetNativeState26ContestRange");
    int Range(ushort strength, ushort resistance, int baseRange) =>
        (int)(method.Invoke(null,
            new object[] { strength, resistance, baseRange }) ?? int.MinValue);
    Equal(7, Range(11, 10, 7), "strength greater than resistance");
    Equal(7, Range(10, 10, 7), "equal strength and resistance");
    Equal(8, Range(9, 10, 7), "resistance excess extends range");

    string root = FindRepositoryRoot();
    string effectSource = File.ReadAllText(Path.Combine(root, "GameSvr",
        "Actors", "TBaseObject.NativeState26Effects.cs"));
    string timedSource = File.ReadAllText(Path.Combine(root, "GameSvr",
        "Actors", "TBaseObject.TimedAbility.cs"));
    string packetSource = File.ReadAllText(Path.Combine(root, "GameSvr",
        "Actors", "TBaseObject.NativeAbilityPacket.cs"));
    Require(timedSource,
        @"case\s+61\s*:\s*m_wEffectStrength\s*=\s*unchecked\s*\(\s*\(ushort\)\s*\(\s*m_wEffectStrength\s*\+\s*\(ushort\)value\s*\)\s*\)",
        "type61 is not an unchecked UInt16 strength addition");
    Require(effectSource,
        @"ApplyNativeDirectMagicEffect[\s\S]{0,1400}?TryApplyNativeState26Direct\(target\)",
        "direct effect path is missing the strength contest");
    Require(effectSource,
        @"ApplyNativeSingleMagicEffect[\s\S]{0,1800}?TryApplyNativeState26Single\(target\)",
        "single effect path is missing the strength contest");
    Require(effectSource,
        @"ApplyNativeAreaMagicEffect[\s\S]{0,2600}?TryApplyNativeState26Single\(target\)",
        "area effect path is missing the strength contest");
    Require(effectSource,
        @"GetNativeState26ContestRange\(m_wEffectStrength,\s*target\.m_wEffectResistance,\s*baseRange\)",
        "effect contest does not consume the strength carrier");
    Require(packetSource,
        @"Position\s*=\s*0x80\s*;[\s\S]{0,80}?Write\(\(uint\)m_wEffectStrength\)",
        "184-byte packet does not write zero-extended strength at 0x80");
}

static Type61ProbePlayer NewProbePlayer(string name) => new()
{
    m_boOffLineFlag = true,
    m_sCharName = name
};

static List<PasValue> Values(params int[] values) =>
    values.Select(PasValue.FromInt).ToList();

static void ResetStrengthItem(ushort strength)
{
    M2Share.UserEngine.StdItemList.Clear();
    var item = new GoodItem
    {
        Name = "type61-strength-item",
        StdMode = 0,
        ItemType = GoodType.ITEM_ETC
    };
    item.NativeItemExtAbilIdents[0] = 54;
    item.NativeItemExtAbilValues[0] = strength;
    M2Share.UserEngine.StdItemList.Add(item);
}

static void EquipStrengthItem(TBaseObject actor)
{
    actor.m_UseItems[0] = new TUserItem
    {
        wIndex = 1,
        Dura = 100,
        DuraMax = 100,
        NativeRecord = new byte[208]
    };
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
    foreach (var start in new[]
             {
                 Environment.CurrentDirectory, AppContext.BaseDirectory
             })
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

static void EqualUInt(uint expected, uint actual, string message)
{
    if (expected != actual)
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class Type61ProbePlayer : TPlayObject
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

sealed class Type61ProbeHero : HeroObject
{
    public int RecalcCount { get; private set; }

    public void ConsumePendingRecalc() => ConsumeAbilityRecalcPending();

    public override void RecalcAbilitys()
    {
        RecalcCount++;
        base.RecalcAbilitys();
    }
}
