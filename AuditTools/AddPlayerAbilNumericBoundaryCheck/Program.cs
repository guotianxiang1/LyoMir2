using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.UserEngine = new UserEngine();
M2Share.ObjectManager = new ObjectManager();
M2Share.MapManager = new MapManager();
M2Share.ProcessMsgCriticalSection = new object();
M2Share.ProcessHumanCriticalSection = new object();
M2Share.LogMsgCriticalSection = new object();
M2Share.LogStringList = new System.Collections.ArrayList();

VerifyMaxHpZero();
VerifyMaxHpReplacementAndWrap();
VerifyMaxMpWrapAndExpiry();
VerifyAntiMagicZeroMaxWrapAndExpiry();
VerifyPackedAttackRangeWrapHelpers();
VerifyTimedWeightWrap();
VerifyJobAttackUpperWrapHelper();

Console.WriteLine(
    "PASS AddPlayerAbil numeric-boundary type4=dword-wrap " +
    "type5=dword-wrap type7=word-wrap packed-range=word-wrap " +
    "type12=word-wrap type59=upper-word-wrap replace=lower+equal+higher expiry=restore");
return;

static void VerifyMaxHpZero()
{
    var (player, bridge, tick) = NewPlayer("type4-zero");
    player.m_Abil.MaxHP = 123;
    player.RecalcAbilitys();
    Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(4, 0, 1)),
        "type 4 zero value was not dispatched");
    player.ConsumePendingRecalc();
    Equal(123, player.m_WAbil.MaxHP, "type 4 zero value");
    player.ProcessTimedAbilities(unchecked(tick + 1500));
    player.ConsumePendingRecalc();
    Assert(!player.HasTimedAbility(4), "type 4 zero node did not expire");
    Equal(123, player.m_WAbil.MaxHP, "expired type 4 zero value");
}

static void VerifyMaxHpReplacementAndWrap()
{
    var (player, bridge, tick) = NewPlayer("type4-wrap");
    const int baseValue = int.MaxValue - 10;
    player.m_Abil.MaxHP = baseValue;
    player.RecalcAbilitys();

    Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(4, 10, 10)),
        "type 4 maximum-boundary value was not dispatched");
    player.ConsumePendingRecalc();
    Equal(int.MaxValue, player.m_WAbil.MaxHP, "type 4 maximum boundary");

    Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(4, 5, 20)),
        "type 4 lower value was not dispatched");
    player.ConsumePendingRecalc();
    Equal(10, player.GetTimedAbilityValue(4),
        "type 4 lower value replaced the active value");
    Equal(10000, player.GetTimedAbilityRemainingMilliseconds(4),
        "type 4 lower value replaced the active duration");
    Equal(int.MaxValue, player.m_WAbil.MaxHP,
        "type 4 lower refresh changed MaxHP");

    Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(4, 10, 20)),
        "type 4 equal value was not dispatched");
    player.ConsumePendingRecalc();
    Equal(20000, player.GetTimedAbilityRemainingMilliseconds(4),
        "type 4 equal value did not extend duration");
    Equal(int.MaxValue, player.m_WAbil.MaxHP,
        "type 4 equal refresh accumulated twice");

    Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(4, 65535, 5)),
        "type 4 higher value was not dispatched");
    player.ConsumePendingRecalc();
    Equal(65535, player.GetTimedAbilityValue(4),
        "type 4 higher value did not replace the active value");
    Equal(unchecked(baseValue + 65535), player.m_WAbil.MaxHP,
        "type 4 dword wrap");

    player.ProcessTimedAbilities(unchecked(tick + 6000));
    player.ConsumePendingRecalc();
    Assert(!player.HasTimedAbility(4), "type 4 node did not expire");
    Equal(baseValue, player.m_WAbil.MaxHP, "expired type 4 MaxHP");
}

static void VerifyMaxMpWrapAndExpiry()
{
    var (player, bridge, tick) = NewPlayer("type5-wrap");
    const int baseValue = int.MaxValue - 1;
    player.m_Abil.MaxMP = baseValue;
    player.RecalcAbilitys();
    Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(5, 65535, 1)),
        "type 5 Word-max value was not dispatched");
    player.ConsumePendingRecalc();
    Equal(unchecked(baseValue + 65535), player.m_WAbil.MaxMP,
        "type 5 dword wrap");
    player.ProcessTimedAbilities(unchecked(tick + 1500));
    player.ConsumePendingRecalc();
    Assert(!player.HasTimedAbility(5), "type 5 node did not expire");
    Equal(baseValue, player.m_WAbil.MaxMP, "expired type 5 MaxMP");
}

static void VerifyAntiMagicZeroMaxWrapAndExpiry()
{
    var (player, bridge, tick) = NewPlayer("type7-wrap");
    var baseValue = player.m_nAntiMagic;
    Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(7, 0, 10)),
        "type 7 zero value was not dispatched");
    player.ConsumePendingRecalc();
    Equal(baseValue, player.m_nAntiMagic, "type 7 zero value");
    Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(7, 65535, 1)),
        "type 7 Word-max replacement was not dispatched");
    player.ConsumePendingRecalc();
    Equal(65535, player.GetTimedAbilityValue(7),
        "type 7 Word-max value did not replace zero");
    Equal(unchecked((ushort)(baseValue + ushort.MaxValue)),
        player.m_nAntiMagic, "type 7 word wrap");
    player.ProcessTimedAbilities(unchecked(tick + 1500));
    player.ConsumePendingRecalc();
    Assert(!player.HasTimedAbility(7), "type 7 node did not expire");
    Equal(baseValue, player.m_nAntiMagic, "expired type 7 anti-magic");
}

static void VerifyPackedAttackRangeWrapHelpers()
{
    Equal(HUtil32.MakeLong(0, 1),
        InvokePrivateInt("AddTimedRange", HUtil32.MakeLong(65534, 65535), 2),
        "packed low/high word wrap helper");
    Equal(HUtil32.MakeLong(1, 0),
        InvokePrivateInt("AddTimedRange", HUtil32.MakeLong(65535, 65534), 2),
        "packed alternate low/high word wrap helper");
}

static void VerifyTimedWeightWrap()
{
    var (player, bridge, tick) = NewPlayer("type12-weight-wrap");
    player.m_Abil.MaxWeight = 40000;
    player.m_Abil.MaxWearWeight = 50000;
    player.m_Abil.MaxHandWeight = 65535;
    player.RecalcAbilitys();

    Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(12, 1, 1)),
        "type 12 weight value was not dispatched");
    player.ConsumePendingRecalc();
    Equal(unchecked((ushort)(40000 + 40000)), player.m_WAbil.MaxWeight,
        "type 12 max weight wrap");
    Equal(unchecked((ushort)(50000 + 50000)), player.m_WAbil.MaxWearWeight,
        "type 12 max wear weight wrap");
    Equal(unchecked((ushort)(65535 + 65535)), player.m_WAbil.MaxHandWeight,
        "type 12 max hand weight wrap");

    player.ProcessTimedAbilities(unchecked(tick + 1500));
    player.ConsumePendingRecalc();
    Equal(40000, player.m_WAbil.MaxWeight, "expired type 12 max weight");
    Equal(50000, player.m_WAbil.MaxWearWeight, "expired type 12 max wear weight");
    Equal(65535, player.m_WAbil.MaxHandWeight, "expired type 12 max hand weight");
}

static void VerifyJobAttackUpperWrapHelper()
{
    Equal(HUtil32.MakeLong(222, 1),
        InvokePrivateInt("AddTimedUpper", HUtil32.MakeLong(222, 65535), 2),
        "type 59 upper word wrap helper");
}

static int InvokePrivateInt(string methodName, params object[] args)
{
    var method = typeof(TBaseObject).GetMethod(methodName,
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
        ?? throw new MissingMethodException(methodName);
    return (int)(method.Invoke(null, args)
        ?? throw new InvalidOperationException(methodName + " returned null"));
}

static (RecalcProbePlayer Player, PasApiBridge Bridge, int Tick) NewPlayer(string name)
{
    var player = new RecalcProbePlayer
    {
        m_boOffLineFlag = true,
        m_sCharName = name
    };
    var tick = HUtil32.GetTickCount();
    player.ProcessTimedAbilities(tick);
    player.RecalcAbilitys();
    return (player, new PasApiBridge { CurrentPlayer = player }, tick);
}

static List<PasValue> Values(params int[] values) =>
    values.Select(PasValue.FromInt).ToList();

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

sealed class RecalcProbePlayer : TPlayObject
{
    public void ConsumePendingRecalc() => ConsumeAbilityRecalcPending();
}
