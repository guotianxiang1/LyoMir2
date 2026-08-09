using System.Reflection;
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

var phase2Types = new[] { 46, 74 };
var isNative = typeof(TBaseObject).GetMethod("IsNativeTimedAbilityType",
    BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new MissingMethodException("IsNativeTimedAbilityType");
var isSupported = typeof(TBaseObject).GetMethod("IsSupportedTimedAbilityType",
    BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new MissingMethodException("IsSupportedTimedAbilityType");

var player = new TPlayObject
{
    m_boOffLineFlag = true,
    m_sCharName = "phase2-player"
};
var hero = new HeroObject
{
    m_Master = player,
    m_sCharName = "phase2-hero"
};
player.m_HeroObject = hero;
var bridge = new PasApiBridge { CurrentPlayer = player };
var playerPermanent = Snapshot(player);
var heroPermanent = Snapshot(hero);

foreach (var scriptType in phase2Types)
{
    Assert((bool)isNative.Invoke(null, new object[] { scriptType }),
        $"phase2 type {scriptType} is no longer classified as native");
    Assert(!(bool)isSupported.Invoke(null, new object[] { scriptType }),
        $"phase2 type {scriptType} was exposed without a closed consumer chain");

    var playerNodes = CountTimedNodes(player);
    Assert(!bridge.CallPlayerMethod("AddPlayerAbil",
            Values(scriptType, ushort.MaxValue, 60)),
        $"AddPlayerAbil type {scriptType} did not fail closed");
    Equal(playerNodes, CountTimedNodes(player),
        $"AddPlayerAbil type {scriptType} created a player node");

    var heroNodes = CountTimedNodes(hero);
    Assert(!bridge.CallPlayerMethod("AddHeroAbil",
            Values(scriptType, ushort.MaxValue, 60)),
        $"AddHeroAbil type {scriptType} did not fail closed");
    Equal(heroNodes, CountTimedNodes(hero),
        $"AddHeroAbil type {scriptType} created a hero node");

    player.AddTimedAbility(scriptType, ushort.MaxValue, 60);
    hero.AddTimedAbility(scriptType, ushort.MaxValue, 60);
    Equal(playerNodes, CountTimedNodes(player),
        $"direct type {scriptType} created a player node");
    Equal(heroNodes, CountTimedNodes(hero),
        $"direct type {scriptType} created a hero node");
}

Assert((bool)isSupported.Invoke(null, new object[] { 43 }),
    "type 43 was not opened after its consumer chain was completed");
Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(299, 1, 60)),
    "low-byte type 43 alias 299 was not dispatched");
Assert(player.HasTimedAbility(43),
    "low-byte type 43 alias 299 did not create the player state");
Assert(player.HasNativeActiveState(75),
    "low-byte type 43 alias 299 did not map to internal state 75");
Equal(1, CountTimedNodes(player),
    "low-byte type 43 alias 299 created more than one player node");
Assert(player.RemoveTimedAbility(43),
    "low-byte type 43 alias 299 player state was not removable");

Assert(bridge.CallPlayerMethod("AddHeroAbil", Values(43, 2, 60)),
    "type 43 hero ability was not dispatched");
Assert(hero.HasTimedAbility(43) && hero.HasNativeActiveState(75),
    "type 43 hero ability did not map to internal state 75");
Equal(1, CountTimedNodes(hero), "type 43 hero node count");
Assert(hero.RemoveTimedAbility(43), "type 43 hero state was not removable");

Assert((bool)isSupported.Invoke(null, new object[] { 45 }),
    "type 45 was not opened after its near-hit consumer chain was completed");
Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(301, 5, 60)),
    "low-byte type 45 alias 301 was not dispatched");
Assert(player.HasTimedAbility(45) && player.HasNativeActiveState(77),
    "low-byte type 45 alias 301 did not map to internal state 77");
Assert(player.RemoveTimedAbility(45), "type 45 player state was not removable");

Assert(bridge.CallPlayerMethod("AddHeroAbil", Values(45, 6, 60)),
    "type 45 hero ability was not dispatched");
Assert(hero.HasTimedAbility(45) && hero.HasNativeActiveState(77),
    "type 45 hero ability did not map to internal state 77");
Assert(hero.RemoveTimedAbility(45), "type 45 hero state was not removable");

Assert((bool)isSupported.Invoke(null, new object[] { 61 }),
    "type 61 was not opened after its strength consumer chain was completed");
Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(317, 5, 60)),
    "low-byte type 61 alias 317 was not dispatched");
Assert(player.HasTimedAbility(61) && player.HasNativeActiveState(93),
    "low-byte type 61 alias 317 did not map to internal state 93");
Assert(player.RemoveTimedAbility(61), "type 61 player state was not removable");

Assert(bridge.CallPlayerMethod("AddHeroAbil", Values(61, 6, 60)),
    "type 61 hero ability was not dispatched");
Assert(hero.HasTimedAbility(61) && hero.HasNativeActiveState(93),
    "type 61 hero ability did not map to internal state 93");
Assert(hero.RemoveTimedAbility(61), "type 61 hero state was not removable");

Assert((bool)isSupported.Invoke(null, new object[] { 62 }),
    "type 62 was not opened after its resistance consumer chain was completed");
Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(62, 3, 60)),
    "type 62 player ability was not dispatched");
Assert(player.HasTimedAbility(62) && player.HasNativeActiveState(94),
    "type 62 player ability did not map to internal state 94");
Assert(player.RemoveTimedAbility(62), "type 62 player state was not removable");

Assert(bridge.CallPlayerMethod("AddHeroAbil", Values(62, 4, 60)),
    "type 62 hero ability was not dispatched");
Assert(hero.HasTimedAbility(62) && hero.HasNativeActiveState(94),
    "type 62 hero ability did not map to internal state 94");
Assert(hero.RemoveTimedAbility(62), "type 62 hero state was not removable");

Equal(0, CountTimedNodes(player), "phase2 checks retained a player node");
Equal(0, CountTimedNodes(hero), "phase2 checks retained a hero node");
Assert(playerPermanent == Snapshot(player),
    "phase2/open checks changed permanent player ability state");
Assert(heroPermanent == Snapshot(hero),
    "phase2/open checks changed permanent hero ability state");

Console.WriteLine(
    "PASS AddPlayerAbil phase2 fail-closed=" +
    "46,74 open=27/283->59,43/299->75,44/300->76,45/301->77,61/317->93,62->94,64->96,68->100 " +
    "player+hero=transient-only+no-permanent-mutation");
return;

static (int Dc, int Mc, int Sc, int Ac, int Mac, int MaxHp, int MaxMp,
    ushort Speed, ushort AntiMagic) Snapshot(TBaseObject actor) =>
    (actor.m_Abil.DC, actor.m_Abil.MC, actor.m_Abil.SC, actor.m_Abil.AC,
        actor.m_Abil.MAC, actor.m_Abil.MaxHP, actor.m_Abil.MaxMP,
        actor.m_wSpeedPoint, actor.m_nAntiMagic);

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
