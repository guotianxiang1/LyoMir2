using System.Text.RegularExpressions;
using System.Reflection;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.UserEngine = new UserEngine();
M2Share.ObjectManager = new ObjectManager();
M2Share.MapManager = new MapManager();
M2Share.CastleManager = new CastleManager();
M2Share.RandomNumber = RandomNumber.GetInstance();
M2Share.ProcessMsgCriticalSection = new object();
M2Share.ProcessHumanCriticalSection = new object();
M2Share.LogMsgCriticalSection = new object();
M2Share.LogStringList = new System.Collections.ArrayList();
M2Share.UserEngine.StdItemList.Add(new GoodItem
{
    Name = "hero-agility-item",
    ItemType = GoodType.ITEM_ACCESSORY,
    StdMode = 20,
    Mac2 = 7
});

var player = new RecalcProbePlayer
{
    m_boOffLineFlag = true,
    m_sCharName = "type6-player"
};
var bridge = new PasApiBridge { CurrentPlayer = player };
var tick = HUtil32.GetTickCount();
player.ProcessTimedAbilities(tick);
player.RecalcAbilitys();
var playerRecalcBaseline = player.PendingRecalcCount;

var baseByteAgility = player.m_btSpeedPoint;
Equal(baseByteAgility, player.m_wSpeedPoint, "initial 16-bit agility mirror");

Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(6, 250, 10)),
    "AddPlayerAbil type 6 was not dispatched");
Equal(playerRecalcBaseline, player.PendingRecalcCount,
    "new type 6 recalculated before SM3555/deferred consumer");
player.ConsumePendingRecalc();
Assert(player.HasTimedAbility(6), "type 6 timed node was not created");
Equal(250, player.GetTimedAbilityValue(6), "initial type 6 value");
Equal(unchecked((ushort)(baseByteAgility + 250)), player.m_wSpeedPoint,
    "initial timed agility");
Equal(baseByteAgility, player.m_btSpeedPoint,
    "type 6 reused or mutated legacy byte agility");

Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(6, 100, 20)),
    "lower type 6 refresh was not dispatched");
player.ConsumePendingRecalc();
Equal(playerRecalcBaseline + 1, player.PendingRecalcCount,
    "lower type 6 refresh marked ability state dirty");
Equal(250, player.GetTimedAbilityValue(6),
    "lower type 6 value replaced the active value");
Equal(10000, player.GetTimedAbilityRemainingMilliseconds(6),
    "lower type 6 value replaced the active duration");
Equal(unchecked((ushort)(baseByteAgility + 250)), player.m_wSpeedPoint,
    "lower type 6 refresh changed agility");

Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(6, 250, 20)),
    "equal type 6 refresh was not dispatched");
player.ConsumePendingRecalc();
Equal(playerRecalcBaseline + 1, player.PendingRecalcCount,
    "equal type 6 refresh marked ability state dirty");
Equal(20000, player.GetTimedAbilityRemainingMilliseconds(6),
    "equal type 6 value did not extend duration");
Equal(unchecked((ushort)(baseByteAgility + 250)), player.m_wSpeedPoint,
    "equal type 6 refresh accumulated agility twice");

Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(6, 65535, 5)),
    "higher type 6 replacement was not dispatched");
Equal(playerRecalcBaseline + 1, player.PendingRecalcCount,
    "higher type 6 recalculated before SM3555/deferred consumer");
player.ConsumePendingRecalc();
Equal(65535, player.GetTimedAbilityValue(6), "replacement type 6 value");
Equal(unchecked((ushort)(baseByteAgility + ushort.MaxValue)),
    player.m_wSpeedPoint, "native UInt16 wrap");
Equal(baseByteAgility, player.m_btSpeedPoint,
    "wrapped type 6 value truncated into legacy byte agility");

player.ProcessTimedAbilities(unchecked(tick + 6000));
Equal(playerRecalcBaseline + 2, player.PendingRecalcCount,
    "expired type 6 recalculated inside the timed-state scan");
player.ConsumePendingRecalc();
Assert(!player.HasTimedAbility(6), "type 6 timed node did not expire");
Equal(baseByteAgility, player.m_wSpeedPoint,
    "expired type 6 agility was not restored");

var hero = new RecalcProbeHero
{
    m_Master = player,
    m_sCharName = "type6-hero"
};
player.m_HeroObject = hero;
hero.m_UseItems[Grobal2.U_NECKLACE] = new TUserItem
{
    wIndex = 1,
    Dura = 100,
    DuraMax = 100
};
hero.RecalcAbilitys();
var heroTick = HUtil32.GetTickCount();
hero.ProcessTimedAbilities(heroTick);
var heroBaseAgility = hero.m_wSpeedPoint;
hero.RecalcAbilitys();
var heroRecalcBaseline = hero.PendingRecalcCount;
Equal(heroBaseAgility, hero.m_wSpeedPoint,
    "hero equipment agility accumulated across recalculation");
Assert(bridge.CallPlayerMethod("AddHeroAbil", Values(6, 65535, 5)),
    "AddHeroAbil type 6 was not dispatched");
Equal(heroRecalcBaseline, hero.PendingRecalcCount,
    "hero type 6 recalculated before its shared deferred consumer");
hero.ConsumePendingRecalc();
Assert(hero.HasTimedAbility(6), "hero type 6 timed node was not created");
Assert(!player.HasTimedAbility(6),
    "hero type 6 timed node was created on the owner");
Equal(unchecked((ushort)(heroBaseAgility + ushort.MaxValue)),
    hero.m_wSpeedPoint, "hero native UInt16 wrap");
hero.ProcessTimedAbilities(unchecked(heroTick + 6000));
Equal(heroRecalcBaseline + 1, hero.PendingRecalcCount,
    "expired hero type 6 recalculated inside the timed-state scan");
hero.ConsumePendingRecalc();
Assert(!hero.HasTimedAbility(6), "hero type 6 timed node did not expire");
Equal(heroBaseAgility, hero.m_wSpeedPoint,
    "expired hero type 6 agility was not restored");

var stateOnlyHero = new RecalcCountingHero();
var stateOnlyTick = HUtil32.GetTickCount();
stateOnlyHero.ProcessTimedAbilities(stateOnlyTick);
stateOnlyHero.AddTimedAbility(13, 1, 1);
stateOnlyHero.AddTimedAbility(13, 2, 1);
stateOnlyHero.AddTimedAbility(17, 1, 1);
stateOnlyHero.ConsumePendingRecalc();
Equal(0, stateOnlyHero.RecalcCount,
    "state-only type 13/17 addition triggered ability recalculation");
stateOnlyHero.ProcessTimedAbilities(unchecked(stateOnlyTick + 1500));
stateOnlyHero.ConsumePendingRecalc();
Equal(0, stateOnlyHero.RecalcCount,
    "state-only type 13/17 expiry triggered ability recalculation");

var coalescingHero = new RecalcCountingHero();
coalescingHero.AddTimedAbility(0, 1, 10);
coalescingHero.AddTimedAbility(1, 1, 10);
Equal(0, coalescingHero.RecalcCount,
    "pending ability changes recalculated before the shared consumer");
coalescingHero.Run();
Equal(1, coalescingHero.RecalcCount,
    "Run-tail consumer did not coalesce pending ability changes");
Equal(1, coalescingHero.AbilityQueueCount,
    "coalesced recalculation did not queue exactly one ability snapshot");
Equal(0, coalescingHero.AbilityDispatchCount,
    "coalesced ability snapshot was consumed in the producing Run");
coalescingHero.Run();
Equal(1, coalescingHero.RecalcCount,
    "successful ability recalculation did not clear the pending flag");
Equal(0, coalescingHero.AbilityQueueCount,
    "queued coalesced ability snapshot survived the next Run");
Equal(1, coalescingHero.AbilityDispatchCount,
    "queued coalesced ability snapshot was not consumed exactly once");
coalescingHero.Run();
Equal(1, coalescingHero.AbilityDispatchCount,
    "coalesced ability snapshot was dispatched more than once");

var retryHero = new ThrowOnceRecalcHero();
retryHero.AddTimedAbility(0, 1, 10);
retryHero.Run();
Equal(1, retryHero.RecalcCount,
    "Run-tail deferred ability recalculation was not attempted");
Equal(0, retryHero.AbilityQueueCount,
    "failed ability recalculation queued a stale snapshot");
Assert(IsAbilityRecalcPending(retryHero),
    "failed ability recalculation cleared the pending flag");
retryHero.Run();
Equal(2, retryHero.RecalcCount,
    "failed Run-tail ability recalculation did not remain pending");
Equal(1, retryHero.AbilityQueueCount,
    "successful retry did not queue exactly one ability snapshot");
Assert(!IsAbilityRecalcPending(retryHero),
    "successful retry did not clear the pending flag");
retryHero.Run();
Equal(2, retryHero.RecalcCount,
    "retried deferred ability recalculation did not clear after success");
Equal(1, retryHero.AbilityDispatchCount,
    "retried ability snapshot was not consumed exactly once");
retryHero.Run();
Equal(1, retryHero.AbilityDispatchCount,
    "retried ability snapshot was dispatched more than once");

var snapshotPlayer = new AbilityCyclePlayer
{
    m_boOffLineFlag = true,
    m_dwKickOffLineTick = int.MaxValue,
    m_PEnvir = new Envirnoment(),
    m_sCharName = "snapshot-player"
};
var snapshotTick = HUtil32.GetTickCount();
snapshotPlayer.ProcessTimedAbilities(snapshotTick);
snapshotPlayer.AddTimedAbility(4, 10, 30);
Equal(0, snapshotPlayer.RecalcCount,
    "player timed add recalculated before the Run-tail consumer");
Equal(0, snapshotPlayer.AbilityQueueCount,
    "player timed add queued RM_ABILITY before recalculation");
snapshotPlayer.Run();
Equal(1, snapshotPlayer.RecalcCount,
    "player first Run cycle did not recalculate timed abilities");
Equal(1, snapshotPlayer.AbilityQueueCount,
    "player first Run cycle did not queue exactly one RM_ABILITY");
Equal(0, snapshotPlayer.AbilityDispatchCount,
    "player first Run cycle consumed its own RM_ABILITY");
snapshotPlayer.Run();
Equal(0, snapshotPlayer.AbilityQueueCount,
    "player second Run cycle did not consume RM_ABILITY");
Equal(1, snapshotPlayer.AbilityDispatchCount,
    "player second Run cycle did not dispatch one ability snapshot");
Equal(Grobal2.SM_ABILITY, snapshotPlayer.m_DefMsg.Ident,
    "player RM_ABILITY did not produce SM_ABILITY");

snapshotPlayer.AddTimedAbility(4, 5, 60);
snapshotPlayer.RunAbilityCycle(snapshotTick + 3);
Equal(10, snapshotPlayer.GetTimedAbilityValue(4),
    "lower refresh replaced the player timed value");
Equal(30000, snapshotPlayer.GetTimedAbilityRemainingMilliseconds(4),
    "lower refresh replaced the player timed duration");
Equal(1, snapshotPlayer.RecalcCount,
    "lower refresh marked the player ability dirty");
Equal(0, snapshotPlayer.AbilityQueueCount,
    "lower refresh queued a player ability snapshot");

snapshotPlayer.AddTimedAbility(4, 10, 60);
snapshotPlayer.RunAbilityCycle(snapshotTick + 4);
Equal(60000, snapshotPlayer.GetTimedAbilityRemainingMilliseconds(4),
    "equal refresh did not extend the player timed duration");
Equal(1, snapshotPlayer.RecalcCount,
    "equal refresh marked the player ability dirty");
Equal(0, snapshotPlayer.AbilityQueueCount,
    "equal refresh queued a player ability snapshot");

snapshotPlayer.AddTimedAbility(4, 11, 5);
snapshotPlayer.RunAbilityCycle(snapshotTick + 5);
Equal(11, snapshotPlayer.GetTimedAbilityValue(4),
    "higher refresh did not replace the player timed value");
Equal(5000, snapshotPlayer.GetTimedAbilityRemainingMilliseconds(4),
    "higher refresh did not replace the player timed duration");
Equal(2, snapshotPlayer.RecalcCount,
    "higher refresh did not trigger one deferred player recalculation");
Equal(1, snapshotPlayer.AbilityQueueCount,
    "higher refresh did not queue one player ability snapshot");
snapshotPlayer.RunAbilityCycle(snapshotTick + 6);
Equal(2, snapshotPlayer.AbilityDispatchCount,
    "higher refresh ability snapshot was not consumed on the next cycle");

var snapshotHero = new AbilityCycleHero
{
    m_sCharName = "snapshot-hero"
};
snapshotHero.AddTimedAbility(4, 10, 30);
snapshotHero.Run();
Equal(1, snapshotHero.RecalcCount,
    "hero first Run did not recalculate timed abilities");
Equal(1, snapshotHero.AbilityQueueCount,
    "hero first Run did not queue exactly one RM_ABILITY");
Equal(0, snapshotHero.AbilityDispatchCount,
    "hero first Run consumed its own RM_ABILITY");
snapshotHero.Run();
Equal(0, snapshotHero.AbilityQueueCount,
    "hero second Run did not consume RM_ABILITY");
Equal(1, snapshotHero.AbilityDispatchCount,
    "hero second Run did not route one ability snapshot");
var heroAbilityHeaderMethod = typeof(HeroObject).GetMethod(
    "BuildHeroAbilityHeader", BindingFlags.Static | BindingFlags.NonPublic);
Assert(heroAbilityHeaderMethod != null,
    "hero ability header builder reflection target missing");
var heroAbilityHeader = (ClientPacket)heroAbilityHeaderMethod.Invoke(null,
    new object[] { snapshotHero.m_Abil.Exp, snapshotHero.m_btJob });
Equal(Grobal2.SM_HERO_ABILITY, heroAbilityHeader.Ident,
    "hero RM_ABILITY target packet was not SM_HERO_ABILITY");

var flagOwner = new AbilityCyclePlayer { m_boOffLineFlag = true };
var flagBridge = new PasApiBridge { CurrentPlayer = flagOwner };
foreach (var flagCase in new[]
         {
             (Label: "dead", Death: true, Ghost: false, ExpectedQueue: 1),
             (Label: "ghost", Death: false, Ghost: true, ExpectedQueue: 0),
             (Label: "dead+ghost", Death: true, Ghost: true, ExpectedQueue: 0)
         })
{
    var flaggedHero = new AbilityCycleHero
    {
        m_Master = flagOwner,
        m_boDeath = flagCase.Death,
        m_boGhost = flagCase.Ghost,
        m_dwDeathTick = HUtil32.GetTickCount(),
        m_sCharName = flagCase.Label
    };
    flagOwner.m_HeroObject = flaggedHero;
    Assert(flagBridge.CallPlayerMethod("AddHeroAbil", Values(4, 1, 30)),
        flagCase.Label + " hero AddHeroAbil was rejected");
    Assert(flaggedHero.HasTimedAbility(4),
        flagCase.Label + " hero timed node was not created");
    flaggedHero.Run();
    Equal(1, flaggedHero.RecalcCount,
        flagCase.Label + " hero timed ability was not recalculated");
    Equal(flagCase.ExpectedQueue, flaggedHero.AbilityQueueCount,
        flagCase.Label + " hero message gate mismatch");
}

foreach (var flagCase in new[]
         {
             (Label: "dead", Death: true, Ghost: false, ExpectedQueue: 1),
             (Label: "ghost", Death: false, Ghost: true, ExpectedQueue: 0),
             (Label: "dead+ghost", Death: true, Ghost: true, ExpectedQueue: 0)
         })
{
    var flaggedPlayer = new AbilityCyclePlayer
    {
        m_boOffLineFlag = true,
        m_boDeath = flagCase.Death,
        m_boGhost = flagCase.Ghost,
        m_sCharName = flagCase.Label
    };
    var flaggedBridge = new PasApiBridge { CurrentPlayer = flaggedPlayer };
    Assert(flaggedBridge.CallPlayerMethod("AddPlayerAbil", Values(4, 1, 30)),
        flagCase.Label + " player AddPlayerAbil was rejected");
    Assert(flaggedPlayer.HasTimedAbility(4),
        flagCase.Label + " player timed node was not created");
    flaggedPlayer.RunAbilityCycle(HUtil32.GetTickCount());
    Equal(1, flaggedPlayer.RecalcCount,
        flagCase.Label + " player timed ability was not recalculated");
    Equal(flagCase.ExpectedQueue, flaggedPlayer.AbilityQueueCount,
        flagCase.Label + " player message gate mismatch");
}

var root = FindRepositoryRoot();
var timedSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Actors",
    "TBaseObject.TimedAbility.cs"));
var playerTimedSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
    "TPlayObject.TimedAbility.cs"));
var recalcSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Actors",
    "TBaseObject.Base.cs"));
var nativeCoreSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Actors",
    "TBaseObject.NativeCoreWorkingAbility.cs"));
Require(timedSource,
    @"case\s+6\s*:\s*m_wSpeedPoint\s*=\s*unchecked\s*\(\s*\(ushort\)\s*\(\s*m_wSpeedPoint\s*\+\s*\(ushort\)value\s*\)\s*\)",
    "type 6 is not an unchecked UInt16 addition");
Require(timedSource,
    @"protected\s+virtual\s+void\s+SendTimedAbilityClientState\s*\([\s\S]*?\)\s*\{\s*\}",
    "base timed state client hook is not a no-op for non-player actors");
Require(playerTimedSource,
    @"protected\s+override\s+void\s+SendTimedAbilityClientState\s*\([\s\S]*?\)\s*\{[\s\S]*?BuildTimedAbilityClientState\([\s\S]*?SendSocket\(state\.Header,\s*state\.Body\);[\s\S]*?\}",
    "player timed state no longer owns direct SM3555 transport");
Require(timedSource,
    @"if\s*\(abilityChanged\s*&&\s*RequiresTimedAbilityRecalc\(node\.InternalType\)\)\s*\{\s*MarkAbilityRecalcPending\(\);\s*\}\s*SendTimedAbilityState\(node,\s*false\)",
    "new/higher timed state is not marked before shared notification");
// The state-lost virtual runs between the broadcast and the companion removal: native
// sub_77337C pushes the LOST flag as 0 at 0x773386 with `xor ecx,ecx` at 0x773388 (so the
// seconds argument is always 0 on this side) and the per-type table is dispatched at
// 0x742692 (`xor eax,eax / mov al,bl / add eax,-0xE / cmp eax,0x5C / ja 0x742C42 /
// jmp [eax*4+0x7426A9]`). Pin that call site too, not just the Send/Mark ordering.
Require(timedSource,
    @"SendTimedAbilityState\(node,\s*true\);\s*OnNativeTimedStateLost\(node\.InternalType\);\s*RemoveTimedAbilityCompanion\(node\.InternalType\);\s*if\s*\(RequiresTimedAbilityRecalc\(node\.InternalType\)\)\s*\{\s*MarkAbilityRecalcPending\(\);\s*\}",
    "expired timed state is not notified before it is marked dirty");
Require(timedSource,
    @"RecalcAbilitys\(\);\s*QueueTimedAbilitySnapshotAfterRecalc\(\);\s*" +
    @"m_boAbilityRecalcPending\s*=\s*false",
    "snapshot is not queued after successful recalculation and before pending clear");
Require(recalcSource,
    @"m_btRaceServer\s*==\s*Grobal2\.RC_PLAYOBJECT\s*\|\|\s*m_btRaceServer\s*==\s*Grobal2\.RC_HEROOBJECT",
    "hero does not rebuild naked hit/agility before equipment bonuses");
Require(recalcSource,
    @"catch\s*\(Exception\s+ex\)\s*\{\s*M2Share\.ErrorMessage\(sExceptionMsg6[\s\S]*?\}\s*try\s*\{\s*(?:ProcessNativeSkill153Shield\(dwRunTick\);\s*)?ProcessTimedAbilities\(\);\s*ConsumeAbilityRecalcPending\(\);",
    "fresh-tick timed scan and shared ability consumer are not at the Run tail");
Equal(1, Regex.Matches(recalcSource,
        @"ProcessTimedAbilities\(\)").Count,
    "TBaseObject.Run invokes the timed scan more than once");
Require(nativeCoreSource,
    @"m_wSpeedPoint\s*=\s*unchecked\s*\(\s*\(ushort\)\s*\(\s*speedBase\s*\+\s*m_NativeCoreWorkingAbility\.SpeedPoint\s*\)\s*\)",
    "16-bit equipment agility was truncated through the legacy byte field");

var hitSources = new[]
{
    Path.Combine(root, "GameSvr", "Actors", "TBaseObject.Attack.cs"),
    Path.Combine(root, "GameSvr", "Spells", "MagicManager.cs"),
    Path.Combine(root, "GameSvr", "Monsters", "Monster",
        "DoubleCriticalMonster.cs"),
    Path.Combine(root, "GameSvr", "Monsters", "Monster", "GasAttackMonster.cs"),
    Path.Combine(root, "GameSvr", "Monsters", "Monster", "SpitSpider.cs")
};
var hitSource = string.Join('\n', hitSources.Select(File.ReadAllText));
Assert(!Regex.IsMatch(hitSource,
        @"Random\s*\(\s*[^\)]*\.m_btSpeedPoint\s*\)"),
    "a physical hit consumer still reads byte agility");
Equal(7, Regex.Matches(hitSource,
        @"Random\s*\(\s*[^\)]*\.m_wSpeedPoint\s*\)").Count,
    "16-bit physical hit consumer count");

Console.WriteLine(
    "PASS AddPlayerAbil type6=UInt16-wrap+replace+extend+expiry " +
    "hero=stable-equipment-base+same-UInt16-effect+no-direct-SM3555 " +
    "deferred=next-Run-snapshot+coalesced+retry-on-failure " +
    "flags=dead-accepted+ghost-message-gate " +
    "state-only=13+17-no-recalc " +
    "hit=target-word-agility");
return;

static List<PasValue> Values(params object[] values) => values.Select(value =>
    value switch
    {
        int number => PasValue.FromInt(number),
        _ => throw new ArgumentException("unsupported PAS test value")
    }).ToList();

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

static void Require(string source, string pattern, string message) =>
    Assert(Regex.IsMatch(source, pattern, RegexOptions.Singleline), message);

static bool IsAbilityRecalcPending(TBaseObject actor)
{
    var field = typeof(TBaseObject).GetField("m_boAbilityRecalcPending",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(field != null, "timed ability pending field reflection target missing");
    return (bool)field.GetValue(actor);
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
    public int PendingRecalcCount { get; private set; }

    public void ConsumePendingRecalc() => ConsumeAbilityRecalcPending();

    public override void RecalcAbilitys()
    {
        PendingRecalcCount++;
        base.RecalcAbilitys();
    }
}

sealed class RecalcCountingHero : HeroObject
{
    public int RecalcCount { get; private set; }
    public int AbilityDispatchCount { get; private set; }
    public int AbilityQueueCount => m_MsgList.Count(message =>
        message.wIdent == Grobal2.RM_ABILITY);

    public void ConsumePendingRecalc() => ConsumeAbilityRecalcPending();

    public override void RecalcAbilitys()
    {
        RecalcCount++;
    }

    public override bool Operate(TProcessMessage message)
    {
        if (message.wIdent == Grobal2.RM_ABILITY)
            AbilityDispatchCount++;
        return base.Operate(message);
    }
}

sealed class RecalcProbeHero : HeroObject
{
    public int PendingRecalcCount { get; private set; }

    public void ConsumePendingRecalc() => ConsumeAbilityRecalcPending();

    public override void RecalcAbilitys()
    {
        PendingRecalcCount++;
        base.RecalcAbilitys();
    }
}

sealed class ThrowOnceRecalcHero : HeroObject
{
    public int RecalcCount { get; private set; }
    public int AbilityDispatchCount { get; private set; }
    public int AbilityQueueCount => m_MsgList.Count(message =>
        message.wIdent == Grobal2.RM_ABILITY);

    public override void RecalcAbilitys()
    {
        RecalcCount++;
        if (RecalcCount == 1)
        {
            throw new InvalidOperationException("expected deferred recalc failure");
        }
    }

    public override bool Operate(TProcessMessage message)
    {
        if (message.wIdent == Grobal2.RM_ABILITY)
            AbilityDispatchCount++;
        return base.Operate(message);
    }
}

sealed class AbilityCyclePlayer : TPlayObject
{
    public int RecalcCount { get; private set; }
    public int AbilityDispatchCount { get; private set; }
    public int AbilityQueueCount => m_MsgList.Count(message =>
        message.wIdent == Grobal2.RM_ABILITY);

    public void RunAbilityCycle(int now)
    {
        TProcessMessage message = null;
        while (GetMessage(ref message))
            Operate(message);
        ProcessTimedAbilities(now);
        ConsumeAbilityRecalcPending();
    }

    public override void RecalcAbilitys()
    {
        RecalcCount++;
        base.RecalcAbilitys();
    }

    public override bool Operate(TProcessMessage message)
    {
        if (message.wIdent == Grobal2.RM_ABILITY)
            AbilityDispatchCount++;
        return base.Operate(message);
    }
}

sealed class AbilityCycleHero : HeroObject
{
    public int RecalcCount { get; private set; }
    public int AbilityDispatchCount { get; private set; }
    public int AbilityQueueCount => m_MsgList.Count(message =>
        message.wIdent == Grobal2.RM_ABILITY);

    public override void RecalcAbilitys()
    {
        RecalcCount++;
    }

    public override bool Operate(TProcessMessage message)
    {
        if (message.wIdent == Grobal2.RM_ABILITY)
            AbilityDispatchCount++;
        return base.Operate(message);
    }
}
