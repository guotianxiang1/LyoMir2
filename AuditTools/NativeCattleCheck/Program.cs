using GameSvr;
using SystemModule;
using System.Buffers.Binary;
using System.Reflection;
using System.Text;

try
{
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    PrepareRuntimeConfig();
    M2Share.g_Config = new GameSvrConfig();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.UserEngine = new UserEngine();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new System.Collections.ArrayList();
    M2Share.RandomNumber = RandomNumber.GetInstance();

    CheckConstructorAndFirstAdd();
    CheckMapFlagsAndRunPath();
    CheckSceneAndThresholds();
    CheckPrizeProtocolAndClaimState();
    CheckCattleDispatcherWiring();
    CheckFireKingWiringAndBehavior();

    Console.WriteLine(
        "PASS NativeCattleCheck session=+08/+0C/+10/+11/+12 " +
        "notice=10105/FB bar=2844/2845/2846 " +
        "scene=OLDSKY/NEWSKY/MULSKY " +
        "thresholds=5000/15000/30000/50000 full=10/20/70 " +
        "cattleprize=GBK/10000/950-952-953/216 " +
        "event=配置1-4/calm-furious/1750/creditcard " +
        "fireking=race150/skill103/state27/self-heal/local-force");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"NativeCattleCheck FAIL: {exception}");
    return 1;
}

static void CheckConstructorAndFirstAdd()
{
    var player = new ProbePlayer();
    var cattle = player.m_NativeCattle;

    Equal(0, cattle.Progress, "constructor +08");
    Equal(0, cattle.Value, "constructor +0C");
    Equal((byte)0, cattle.Tier, "constructor +10");
    Assert(!cattle.NearFullNotified, "constructor +11");
    Assert(!cattle.BarVisible, "constructor +12");

    cattle.Add(-1, _ => 99);
    Equal(-1, cattle.Value, "unchecked first Add value");
    Equal((byte)1, cattle.Tier, "first Add native tier transition");
    var notice = player.m_MsgList.Single(message =>
        message.wIdent == Grobal2.RM_CATTLE_SYSMESSAGE);
    Equal(0xFB, notice.wParam, "first Add notice wParam");
    Equal("-1 点牛气值增加", notice.Buff, "first Add notice text");
    Assert(player.Packets.Any(packet =>
            packet.Ident == Grobal2.SM_CATTLE_BAR_CHANGE),
        "hidden first Add did not send unconditional 2846");
}

static void CheckSceneAndThresholds()
{
    var player = new ProbePlayer();
    var cattle = player.m_NativeCattle;
    cattle.Progress = 17;
    cattle.NearFullNotified = true;

    cattle.ProcessSceneType(3);
    Assert(cattle.BarVisible, "type 3 did not show bar");
    Equal(0, cattle.Value, "type 3 value reset");
    Equal((byte)1, cattle.Tier, "type 3 tier reset");
    Equal((ushort)Grobal2.SM_CATTLE_BAR_SHOW, player.Packets[^1].Ident,
        "type 3 show packet");

    cattle.NearFullNotified = false;
    cattle.Add(4501, _ => 99);
    Equal((byte)1, cattle.Tier, "near threshold tier");
    Assert(cattle.NearFullNotified, "strict under-500 notice");
    Assert(player.m_MsgList.Any(message => message.Buff?.Contains(
            "请预留6格包裹空位", StringComparison.Ordinal) == true),
        "near threshold notice text");

    cattle.Add(499, _ => 99);
    Equal(5000, cattle.Value, "tier-2 value");
    Equal((byte)2, cattle.Tier, "tier-2 threshold");
    Assert(!cattle.NearFullNotified, "tier raise did not clear +11");
    var change = player.Packets[^1];
    Equal((ushort)Grobal2.SM_CATTLE_BAR_CHANGE, change.Ident, "2846 ident");
    Equal(2, change.Recog, "2846 normalized tier");
    Equal((ushort)2, change.Param, "2846 raw tier");
    Equal((ushort)15000, change.Tag, "2846 next threshold");
    Equal((ushort)5000, change.Series, "2846 current value");

    cattle.ProcessSceneType(0);
    Assert(!cattle.BarVisible, "leaving type 3 did not hide bar");
    Equal((ushort)Grobal2.SM_CATTLE_BAR_HIDE, player.Packets[^1].Ident,
        "type 3 hide packet");
    cattle.ProcessSceneType(3);
    Equal(17, cattle.Progress, "type 3 changed +08");
}

static void CheckMapFlagsAndRunPath()
{
    var oldSky = new TMapFlag();
    Assert(Maps.TryApplySceneFlag(oldSky, "oLdSkY"),
        "OLDSKY map flag was not parsed");
    Equal((byte)1, oldSky.SceneType, "OLDSKY scene type");

    var newSky = new TMapFlag();
    Assert(Maps.TryApplySceneFlag(newSky, "NEWSKY"),
        "NEWSKY map flag was not parsed");
    Equal((byte)2, newSky.SceneType, "NEWSKY scene type");

    var fight3Player = new ProbePlayer
    {
        m_PEnvir = new Envirnoment()
    };
    Assert(Maps.TryApplySceneFlag(fight3Player.m_PEnvir.Flag, "FIGHT3"),
        "FIGHT3 map flag was not parsed");
    Assert(fight3Player.m_PEnvir.Flag.boFight3Zone,
        "FIGHT3 boolean flag was not preserved");
    Equal((byte)0, fight3Player.m_PEnvir.Flag.SceneType,
        "FIGHT3 mapped to cattle scene type");
    fight3Player.m_NativeCattle.Value = 123;
    fight3Player.m_NativeCattle.Tier = 2;
    fight3Player.RunNativeCattle();
    Assert(!fight3Player.m_NativeCattle.BarVisible,
        "FIGHT3 showed cattle bar");
    Equal(123, fight3Player.m_NativeCattle.Value,
        "FIGHT3 reset cattle value");
    Assert(!fight3Player.Packets.Any(packet =>
            packet.Ident == Grobal2.SM_CATTLE_BAR_SHOW),
        "FIGHT3 sent cattle bar show packet");

    var mulSkyPlayer = new ProbePlayer
    {
        m_PEnvir = new Envirnoment()
    };
    Assert(Maps.TryApplySceneFlag(mulSkyPlayer.m_PEnvir.Flag, "MULSKY"),
        "MULSKY map flag was not parsed");
    Equal((byte)3, mulSkyPlayer.m_PEnvir.Flag.SceneType,
        "MULSKY scene type");
    mulSkyPlayer.m_NativeCattle.Value = 456;
    mulSkyPlayer.m_NativeCattle.Tier = 4;
    mulSkyPlayer.RunNativeCattle();
    Assert(mulSkyPlayer.m_NativeCattle.BarVisible,
        "MULSKY did not show cattle bar");
    Equal(0, mulSkyPlayer.m_NativeCattle.Value,
        "MULSKY did not reset cattle value");
    Equal((byte)1, mulSkyPlayer.m_NativeCattle.Tier,
        "MULSKY did not reset cattle tier");
    Equal((ushort)Grobal2.SM_CATTLE_BAR_SHOW,
        mulSkyPlayer.Packets[^1].Ident, "MULSKY show packet");
}

static void CheckFullTierDraws()
{
    CheckDraw(0, 30000, 4);
    CheckDraw(10, 15000, 3);
    CheckDraw(30, 5000, 2);
}

static void CheckPrizeProtocolAndClaimState()
{
    M2Share.UserEngine = new UserEngine();
    SetCattleDefinitions();
    var directory = Path.Combine(Path.GetTempPath(),
        "NativeCattleCheck-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var configPath = Path.Combine(directory, "CattlePrize.ini");

    try
    {
        File.WriteAllText(configPath, BuildCattlePrizeConfig(),
            HUtil32.GbkEncoding);
        Assert(TPlayObject.InitializeNativeCattlePrizeConfigFromPath(configPath),
            "CattlePrize.ini GBK configuration did not load");

        CheckFullTierDraws();
        CheckEventAddPath();
        CheckGlobalPrizeAccount();

        var player = new ProbePlayer();
        var cattle = player.m_NativeCattle;
        var randomCalls = new Queue<(int Range, int Value)>(new[]
        {
            (10000, 5000), // threshold equality -> 金牛装备
            (10000, 0), // [金牛装备] -> 金牛靴
            (8, 7),     // exclude 宝箱1.奖品8
            (10000, 0), // 宝箱1.奖品7 金牛装备 -> 金牛靴
            (8, 0), (7, 0), (6, 0), (5, 0),
            (4, 0), (3, 0), (2, 0), (1, 0)
        });
        int NextRandom(int range)
        {
            Assert(randomCalls.Count != 0, "unexpected cattle random call");
            var expected = randomCalls.Dequeue();
            Equal(expected.Range, range, "cattle random range");
            return expected.Value;
        }

        Assert(cattle.TryCreatePrizeState(1, NextRandom, out var body),
            "tier-one prize state did not build");
        Equal(0, randomCalls.Count, "cattle random call count");
        Equal(216, body.Length, "SM950 body length");
        Equal(6, body[0], "SM950 ShortString length");
        Equal("金牛靴", ReadShortString(body.AsSpan(0, 16)),
            "SM950 actual display name");
        Equal(777, BinaryPrimitives.ReadInt32LittleEndian(
            body.AsSpan(16, 4)), "SM950 actual Looks");
        Equal(1, BinaryPrimitives.ReadInt32LittleEndian(
            body.AsSpan(20, 4)), "SM950 actual amount");
        Equal(1186, BinaryPrimitives.ReadInt32LittleEndian(
            body.AsSpan(40, 4)), "SM950 decoy special Looks");
        Assert(body.AsSpan(8 * 24, 24).ToArray().All(value => value == 0),
            "SM950 ninth record was not zero-filled");
        Equal((byte)3, cattle.PrizeMode, "native cattle +0D94 mode");
        Equal((byte)3, player.NativeCattleNeedKeyBoxMode,
            "cattle/NeedKey shared +0D94 mode");
        Equal((byte)1, cattle.SelectedPrizeSlot,
            "native cattle +0D72 one-based selection");
        Equal("金牛靴:1", cattle.RevealPendingDescriptor,
            "native cattle +0D48 descriptor");
        Equal("金牛靴:1", cattle.ClaimPendingDescriptor,
            "native cattle +0D5D descriptor");

        Assert(cattle.ClientRevealPrize(),
            "CM1081 did not consume the cattle reveal state");
        var reveal = player.Packets[^1];
        Equal((ushort)Grobal2.SM_CATTLE_PRIZE_REVEAL, reveal.Ident,
            "CM1081 SM952 ident");
        Equal(1, reveal.Recog, "CM1081 selected one-based slot");
        Assert(!cattle.HasRevealPending && cattle.HasClaimPending,
            "CM1081 did not clear only +0D48");
        Assert(!cattle.ClientRevealPrize(),
            "repeated CM1081 consumed a nonexistent reveal state");

        for (var index = 0; index < Grobal2.MAXBAGITEM - 5; index++)
            player.m_ItemList.Add(new TUserItem { btValue = new byte[14] });
        cattle.ClientClaimPrize();
        var bagFull = player.Packets[^1];
        Equal((ushort)Grobal2.SM_CATTLE_PRIZE_CLAIM, bagFull.Ident,
            "CM1082 bag-full SM953 ident");
        Equal(0, bagFull.Recog, "CM1082 bag-full result");
        Assert(cattle.HasClaimPending,
            "CM1082 bag-full cleared pending reward");

        player.m_ItemList.Clear();
        cattle.ClientClaimPrize();
        var claimed = player.Packets[^1];
        Equal(1, claimed.Recog, "CM1082 normal result");
        Assert(!cattle.HasRevealPending && !cattle.HasClaimPending,
            "CM1082 normal claim retained pending reward");
        Assert(player.m_ItemList.Count == 1,
            "CM1082 did not issue resolved gold equipment");

        cattle.ClientClaimPrize();
        Equal(2, player.Packets[^1].Recog,
            "CM1082 repeat claim result");

        SetCattleDefinitions(clearOnly: true);
        var missingDefinitionBody = CreateTierOnePrize(cattle);
        Equal(-1, BinaryPrimitives.ReadInt32LittleEndian(
            missingDefinitionBody.AsSpan(16, 4)),
            "missing gold-equipment definition retained Looks=-1");
        cattle.ClientClaimPrize();
        Equal(1, player.Packets[^1].Recog,
            "CM1082 ignored failed Give result");
        Assert(!cattle.HasRevealPending && !cattle.HasClaimPending,
            "failed Give did not clear native pending descriptors");

        SetCattleDefinitions();
        CreateTierOnePrize(cattle);
        cattle.ClientClaimPrize();
        Equal(1, player.Packets[^1].Recog,
            "CM1082 direct claim before reveal result");
        Assert(!cattle.HasRevealPending && !cattle.HasClaimPending,
            "direct CM1082 did not clear both descriptors");

        var finalBoundaryBody = CreateTierOnePrize(cattle, 9999, false);
        Equal("经验", ReadShortString(finalBoundaryBody.AsSpan(0, 16)),
            "last cumulative threshold reward");
        Equal(1186, BinaryPrimitives.ReadInt32LittleEndian(
            finalBoundaryBody.AsSpan(16, 4)),
            "last cumulative threshold Looks");
        cattle.ClientClaimPrize();
    }
    finally
    {
        TPlayObject.InitializeNativeCattlePrizeConfigFromPath(
            Path.Combine(directory, "missing.ini"));
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}

static void CheckEventAddPath()
{
    var mappings = new[]
    {
        (Name: "calm-one", Request: 1, Furious: false,
            Descriptor: "经验:11", Progress: 1, Value: 100, Result: 1),
        (Name: "furious-one", Request: 1, Furious: true,
            Descriptor: "经验:22", Progress: 1, Value: 5, Result: 1),
        (Name: "calm-many", Request: 2, Furious: false,
            Descriptor: "经验:33", Progress: 10, Value: 1000, Result: 10),
        (Name: "furious-many", Request: 2, Furious: true,
            Descriptor: "经验:44", Progress: 10, Value: 50, Result: 10)
    };

    foreach (var mapping in mappings)
    {
        var player = CreateEventPlayer("测试者");
        var cattle = player.m_NativeCattle;
        TBodyCattleU.ResetEventStateForCheck(0, 0, 100);
        var random = new ExpectedRandom(mapping.Name,
            (10000, 0), (5000, 4999));
        var activityRewards = new List<string>();
        var awards = new List<(byte[] Prefix, int Amount)>();
        var broadcasts = new List<string>();
        var furious = mapping.Furious;

        var result = cattle.AddEvent(10000, mapping.Request, ref furious,
            random.Next, activityRewards.Add,
            (prefix, amount) => awards.Add((prefix, amount)),
            broadcasts.Add);

        random.AssertComplete();
        Equal(mapping.Result, result, mapping.Name + " return");
        Equal(mapping.Progress, cattle.Progress,
            mapping.Name + " normalized progress");
        Equal(mapping.Value, cattle.Value, mapping.Name + " value gain");
        Equal(mapping.Furious, furious, mapping.Name + " phase");
        Equal(mapping.Descriptor, activityRewards.Single(),
            mapping.Name + " activity pool");
        Equal(0, awards.Count, mapping.Name + " global award count");
        Equal(0, broadcasts.Count, mapping.Name + " broadcast count");
        var notice = player.m_MsgList.Last(message =>
            message.wIdent == Grobal2.RM_CATTLE_SYSMESSAGE);
        Equal(mapping.Value + " 点牛气值增加", notice.Buff,
            mapping.Name + " notice");
    }

    {
        var player = CreateEventPlayer("一二三四五六七八");
        var cattle = player.m_NativeCattle;
        TBodyCattleU.ResetEventStateForCheck(0, 0, 100);
        var random = new ExpectedRandom("calm kill",
            (10000, 0), (5000, 9));
        var awards = new List<(byte[] Prefix, int Amount, int Pool)>();
        var furious = false;
        var result = cattle.AddEvent(100, 10, ref furious, random.Next,
            _ => { },
            (prefix, amount) => awards.Add((prefix, amount,
                TBodyCattleU.EventGlobalPool)),
            _ => { });

        random.AssertComplete();
        Equal(10000, result, "calm kill return");
        var award = awards.Single();
        Equal("恭喜一二三四五六七在富贵兽平静的时候把富贵兽消灭了，获得了",
            HUtil32.GbkEncoding.GetString(award.Prefix),
            "calm kill award/GBK boundary");
        Equal(2, award.Amount, "calm kill award amount");
        Equal(10, award.Pool, "calm kill award ordering");
        Equal(0, TBodyCattleU.EventGlobalPool, "calm kill pool reset");
        Assert(!furious, "calm kill changed furious phase");
    }

    {
        var player = CreateEventPlayer("测试者");
        var cattle = player.m_NativeCattle;
        TBodyCattleU.ResetEventStateForCheck(999, 0, 100);
        var random = new ExpectedRandom("become furious",
            (10000, 0), (10000, 4));
        var awards = new List<(byte[] Prefix, int Amount, int Pool)>();
        var furious = false;
        var result = cattle.AddEvent(1000, 1, ref furious, random.Next,
            _ => { },
            (prefix, amount) => awards.Add((prefix, amount,
                TBodyCattleU.EventGlobalPool)),
            _ => { });

        random.AssertComplete();
        Equal(1, result, "become furious return");
        var award = awards.Single();
        Equal("恭喜测试者激怒了富贵兽。获得了",
            HUtil32.GbkEncoding.GetString(award.Prefix),
            "become furious award prefix");
        Equal(200, award.Amount, "become furious award amount");
        Equal(1000, award.Pool, "become furious award ordering");
        Equal(1000, TBodyCattleU.EventGlobalPool,
            "become furious retained pool");
        Assert(furious, "threshold did not set furious phase");
    }

    {
        var player = CreateEventPlayer("测试者");
        var cattle = player.m_NativeCattle;
        TBodyCattleU.ResetEventStateForCheck(1000, 4, 5);
        var random = new ExpectedRandom("furious kill",
            (10000, 0), (1750, 123));
        var awards = new List<(byte[] Prefix, int Amount, int Pool)>();
        var furious = true;
        var result = cattle.AddEvent(1000, 1, ref furious, random.Next,
            _ => { },
            (prefix, amount) => awards.Add((prefix, amount,
                TBodyCattleU.EventGlobalPool)),
            _ => { });

        random.AssertComplete();
        Equal(10000, result, "furious kill return");
        var award = awards.Single();
        Equal("恭喜测试者在富贵兽狂暴的时候把富贵兽消灭了，获得了",
            HUtil32.GbkEncoding.GetString(award.Prefix),
            "furious kill award prefix");
        Equal(400, award.Amount, "furious kill award amount");
        Equal(1001, award.Pool, "furious kill award ordering");
        Equal(0, TBodyCattleU.EventGlobalPool, "furious kill pool reset");
        Equal(0, TBodyCattleU.EventKillCounterCurrent,
            "furious kill counter current");
        Equal(124, TBodyCattleU.EventKillCounterTarget,
            "furious kill counter target");
    }

    {
        var player = CreateEventPlayer("测试者");
        var cattle = player.m_NativeCattle;
        TBodyCattleU.ResetEventStateForCheck(1000, 0, 100);
        var random = new ExpectedRandom("furious bounty",
            (10000, 0), (20, 0));
        var awards = new List<(byte[] Prefix, int Amount)>();
        var broadcasts = new List<string>();
        var furious = true;
        var result = cattle.AddEvent(1000, 1, ref furious, random.Next,
            _ => { },
            (prefix, amount) => awards.Add((prefix, amount)),
            broadcasts.Add);

        random.AssertComplete();
        Equal(1, result, "furious bounty return");
        Equal(0, awards.Count, "furious bounty award count");
        Equal("悬赏捕杀富贵兽，目前赏金额度已经提高到400张灵符，请勇士们速速前往猎杀",
            broadcasts.Single(), "furious bounty text");
        Equal(1001, TBodyCattleU.EventGlobalPool,
            "furious bounty retained pool");
        Equal(1, TBodyCattleU.EventKillCounterCurrent,
            "furious bounty counter current");
    }

    {
        var player = CreateEventPlayer("ABCDEFGHIJKLM中");
        var cattle = player.m_NativeCattle;
        TBodyCattleU.ResetEventStateForCheck(0, 0, 100);
        var random = new ExpectedRandom("raw split name",
            (10000, 0), (5000, 9));
        byte[] awardPrefix = null;
        var furious = false;
        cattle.AddEvent(100, 10, ref furious, random.Next, _ => { },
            (prefix, _) => awardPrefix = prefix, _ => { });

        random.AssertComplete();
        var expectedPrefix = HUtil32.GbkEncoding.GetBytes(
            "恭喜ABCDEFGHIJKLM")
            .Concat(new byte[] { 0xD6 })
            .Concat(HUtil32.GbkEncoding.GetBytes(
                "在富贵兽平静的时候把富贵兽消灭了，获得了"))
            .Take(0x3A)
            .ToArray();
        Assert(awardPrefix.SequenceEqual(expectedPrefix),
            "15-byte legal name did not preserve native half-GBK byte");
        var packet = TPlayObject.BuildNativeCattleGlobalPrizeBroadcast(
            awardPrefix, 2);
        var expectedWire = expectedPrefix.Concat(
            HUtil32.GbkEncoding.GetBytes("2张灵符")).ToArray();
        Assert(packet.TextBytes.SequenceEqual(expectedWire),
            "Type18 global-prize body changed raw prefix bytes");
    }

    {
        var player = CreateEventPlayer("测试者");
        var cattle = player.m_NativeCattle;
        TBodyCattleU.ResetEventStateForCheck(1000, 1749, 1750);
        var random = new ExpectedRandom("counter boundary",
            (10000, 0), (1750, 100), (1750, 200));
        var furious = true;
        var result = cattle.AddEvent(1000, 1, ref furious, random.Next,
            _ => { }, (_, _) => { }, _ => { });

        random.AssertComplete();
        Equal(10000, result, "counter boundary return");
        Equal(0, TBodyCattleU.EventKillCounterCurrent,
            "counter boundary current");
        Equal(201, TBodyCattleU.EventKillCounterTarget,
            "counter boundary double reset");
    }
}

static ProbePlayer CreateEventPlayer(string name)
{
    var player = new ProbePlayer { m_sCharName = name };
    player.m_NativeCattle.Tier = 1;
    return player;
}

static void CheckGlobalPrizeAccount()
{
    M2Share.LogStringList.Clear();
    var player = new ProbePlayer { m_sCharName = "账户测试" };
    player.m_nLingFu = 7;
    player.m_CreditCard.Value = 3;

    player.GrantNativeCattleGlobalPrize("测试奖励", 5);

    Equal(7, player.m_nLingFu,
        "cattle global prize changed base LingFu");
    Equal(8, player.m_CreditCard.Value,
        "cattle global prize CreditCard.Value");
    Assert(player.m_CreditCard.Dirty,
        "cattle global prize did not dirty CreditCard");
    Assert(player.TryGetNativeLingFuReasonBuckets(out var buckets),
        "cattle global prize reason buckets missing");
    Equal(5, buckets[9], "cattle global prize reason bucket 9");
    Assert(player.m_MsgList.Any(message =>
            message.wIdent == Grobal2.RM_LINGFU_CHANGED),
        "cattle global prize omitted RM_LINGFU_CHANGED");
    var fields = M2Share.LogStringList[
        M2Share.LogStringList.Count - 1].ToString().Split('\t');
    Equal(9, fields.Length, "cattle global prize log field count");
    Equal("9", fields[0], "cattle global prize log type");
    Equal("灵符2", fields[5], "cattle global prize log item");
    Equal("222222", fields[6], "cattle global prize log reason");
    Equal("5", fields[7], "cattle global prize log amount");
    Equal("牛气服务器大奖", fields[8],
        "cattle global prize log description");
}

static byte[] CreateTierOnePrize(TBodyCattleU cattle,
    int personalRoll = 0, bool personalGold = true)
{
    var calls = new List<(int Range, int Value)>
    {
        (10000, personalRoll)
    };
    if (personalGold) calls.Add((10000, 0));
    calls.AddRange(new[]
    {
        (8, 7), (10000, 0),
        (8, 0), (7, 0), (6, 0), (5, 0),
        (4, 0), (3, 0), (2, 0), (1, 0)
    });
    var randomCalls = new Queue<(int Range, int Value)>(calls);
    int NextRandom(int range)
    {
        var expected = randomCalls.Dequeue();
        Equal(expected.Range, range, "reopened cattle random range");
        return expected.Value;
    }
    Assert(cattle.TryCreatePrizeState(1, NextRandom, out var body),
        "reopened cattle prize state");
    Equal(0, randomCalls.Count, "reopened cattle random count");
    return body;
}

static void SetCattleDefinitions(bool clearOnly = false)
{
    M2Share.UserEngine.StdItemList.Clear();
    if (clearOnly) return;
    M2Share.UserEngine.StdItemList.Add(new GoodItem
    {
        Name = "金牛靴",
        Looks = 777,
        DuraMax = 100
    });
}

static string BuildCattlePrizeConfig()
{
    var lines = new List<string>();
    for (var tier = 1; tier <= 4; tier++)
    {
        lines.Add("[配置" + tier + "]");
        lines.Add("奖品1=经验:" + tier * 11 + "/9999");
        lines.Add(string.Empty);

        lines.Add("[个人奖" + tier + "]");
        if (tier == 1)
        {
            lines.Add("奖品1=金牛装备:1/5000");
            lines.Add("奖品2=经验:7/9999");
        }
        else
        {
            lines.Add("奖品1=经验:" + tier + "/9999");
        }
        lines.Add(string.Empty);

        lines.Add("[宝箱" + tier + "]");
        lines.Add("奖品1=经验:1/ignored");
        lines.Add("奖品2=牛气值:2/2000");
        lines.Add("奖品3=金刚石:3/3000");
        lines.Add("奖品4=灵符:4/4000");
        lines.Add("奖品5=声望:5/5000");
        lines.Add("奖品6=金币:6/6000");
        lines.Add("奖品7=金牛装备:1/7000");
        lines.Add("奖品8=经验:8/9999");
        lines.Add(string.Empty);
    }
    lines.Add("[金牛装备]");
    lines.Add("奖品1=金牛靴:1/9999");
    return string.Join(Environment.NewLine, lines);
}

static string ReadShortString(ReadOnlySpan<byte> source)
{
    var length = source[0];
    Assert(length <= 15 && length < source.Length,
        "invalid native ShortString length");
    return HUtil32.GbkEncoding.GetString(source.Slice(1, length));
}

static void CheckCattleDispatcherWiring()
{
    var root = FindRepositoryRoot();
    var source = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.Message.cs"));
    Assert(source.Contains("case Grobal2.CM_CATTLE_REVEAL_PRIZE:",
            StringComparison.Ordinal), "CM1081 dispatcher case is missing");
    Assert(source.Contains("case Grobal2.CM_CATTLE_CLAIM_PRIZE:",
            StringComparison.Ordinal), "CM1082 dispatcher case is missing");
    Assert(source.Contains("!ClientNativeCattleRevealPrize() &&",
            StringComparison.Ordinal),
        "CM1081 cattle-first branch is missing");
    Assert(source.Contains("TrySelectNativeNeedKeyBox(out var cattleBoxSlot)",
            StringComparison.Ordinal),
        "CM1081 NeedKey fallback is missing");
    Assert(source.Contains("_nativeNeedKeyBoxSelectedReward?.Length",
            StringComparison.Ordinal),
        "CM1082 NeedKey-first branch is missing");

    var cattleSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.NativeCattle.cs"));
    Assert(cattleSource.Contains(
            "MakeDefaultMsg(NativeCattlePrizeOpenMessage",
            StringComparison.Ordinal), "SM950 header construction is missing");
    Assert(cattleSource.Contains("SendSocket(m_DefMsg, body)",
            StringComparison.Ordinal), "SM950 216-byte body send is missing");
    Assert(cattleSource.Contains(
            "TryCreatePrizeState(newTier - 1, random, out var body)",
            StringComparison.Ordinal),
        "tier transition is not wired to sub_716174 state construction");
    Assert(cattleSource.Contains(
            "AddNativeCattleEvent(int threshold, int amount",
            StringComparison.Ordinal),
        "native event-103 cattle entry point is missing");
    Assert(cattleSource.Contains("m_CreditCard.Value = value < 0 ? 0 : value",
            StringComparison.Ordinal),
        "global cattle prize is not wired to CreditCard.Value");
}

static void CheckFireKingWiringAndBehavior()
{
    Assert(typeof(FireKingMonster).BaseType == typeof(AnimalObject),
        "FireKing must inherit AnimalObject directly (native TFireKingMonster's "
        + "parent is TAnimal: VMT 0x67FF80, size 1256)");
    // Re-based 2026-08-04 from 216 to 150 on 战神 bytes, NOT relaxed.
    // Native index table[150-0x0B=0x8B] = 0x44 = 68 ; jt[68] = 0x67A985, whose body is
    //   67A987  A1 34 FF 67 00  mov eax,[0x67FF34]   ; classref -> TFireKingMonster
    //   67A98C  E8 67 78 00 00  call sub_6821F8      ; ctor
    // Uniqueness of that ownership is exhaustive, not xref-guessed:
    //   * classref global [0x67FF34] has exactly ONE load site image-wide (0x67A987)
    //   * ctor sub_6821F8 has exactly ONE E8 rel32 caller (0x67A98C)
    //   * `mov/cmp byte [reg+0x178], 0xD8` (216 used as a race) => ZERO sites
    //   * none of sub_679F8C's four callers (0x67BD77/0x67BE2F/0x67BFE2/0x67CA3B)
    //     has an immediate 0xD8 within 0x40 bytes before the call
    // Race 216's index byte is 0x00 -> jt[0] = 0x67AE5E = the default sink
    // (`xor eax,eax` -> nil), i.e. 216 spawns NOTHING natively.
    Equal(150, FireKingMonster.NativeRace,
        "FireKing native race = 150 (jt[68]=0x67A985); 216 has index byte 0x00 -> "
        + "default sink 0x67AE5E");
    Equal(103, FireKingMonster.NativeCattleDamageSkill,
        "FireKing cattle damage skill");
    Equal(10161, FireKingMonster.NativeInitializeMessage,
        "FireKing initialize message");
    Equal(10000, FireKingMonster.NativeInitializeDelay,
        "FireKing initialize delay");
    Equal(257, Grobal2.ISM_MAKE_CATTLE_CRAZY,
        "FireKing mirror ident");

    var resolver = typeof(TBaseObject).GetMethod(
        "ResolveFullMagicDamage",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(resolver?.IsVirtual == true,
        "full magic resolver is not virtual");
    var fireKingResolver = typeof(FireKingMonster).GetMethod(
        "ResolveFullMagicDamage",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(fireKingResolver?.DeclaringType == typeof(FireKingMonster),
        "FireKing full magic resolver override is missing");

    var fireKing = new FireKingMonster();
    Assert(fireKing.NativeFuriousThreshold is >= 2000 and < 2200,
        "FireKing threshold range");
    Equal(0, fireKing.ResolveFullMagicDamage(null, 102, false,
        MagicDamageContext.Empty, 0, 0, 123),
        "FireKing non-103 immunity");
    Equal(1, fireKing.NativeAllCallbackCount,
        "FireKing all-callback counter");
    Equal(0, fireKing.NativeSkill103Count,
        "FireKing non-103 special counter");

    Equal(0, fireKing.ResolveFullMagicDamage(null, 103, false,
        MagicDamageContext.Empty, 0, 0, 123),
        "FireKing non-player 103 fail-closed result");
    Equal(2, fireKing.NativeAllCallbackCount,
        "FireKing 103 all-callback counter");
    Equal(1, fireKing.NativeSkill103Count,
        "FireKing 103 special counter");

    Assert(fireKing.SetBodyState(27, true),
        "FireKing state 27 activation");
    Equal((byte)0x08, fireKing.GetBodyStateBuffer()[3],
        "FireKing state 27 wire bit");
    Assert(fireKing.SetBodyState(27, false),
        "FireKing state 27 clear");
    Equal((byte)0, fireKing.GetBodyStateBuffer()[3],
        "FireKing calm wire bit");

    NativeFireKingEventState.ResetForCheck();
    Assert(!NativeFireKingEventState.IsForced,
        "FireKing local force reset");
    NativeFireKingEventState.ObserveThreshold(2100);
    NativeFireKingEventState.ObserveThreshold(2050);
    NativeFireKingEventState.ObserveThreshold(2199);
    Equal(2199, NativeFireKingEventState.MaxThreshold,
        "FireKing forced threshold maximum");
    NativeFireKingEventState.ForceLocally();
    Assert(NativeFireKingEventState.IsForced,
        "FireKing local force flag");
    NativeFireKingEventState.ResetForCheck();
    new MirrorMessage().ProcessData(Grobal2.ISM_MAKE_CATTLE_CRAZY,
        3, string.Empty);
    Assert(NativeFireKingEventState.IsForced,
        "FireKing mirror force flag");
    NativeFireKingEventState.ResetForCheck();

    var root = FindRepositoryRoot();
    var fireKingSource = File.ReadAllText(Path.Combine(root, "GameSvr",
        "Monsters", "Monster", "FireKingMonster.cs"));
    Assert(fireKingSource.Contains(
            "class FireKingMonster : AnimalObject",
            StringComparison.Ordinal),
        "FireKing direct AnimalObject inheritance source");
    Assert(fireKingSource.Contains(
            "m_WAbil.HP = m_WAbil.MaxHP;",
            StringComparison.Ordinal),
        "FireKing self-heal source");
    Assert(fireKingSource.Contains(
            "SendDelayMsg(this, NativeInitializeMessage, 1, m_WAbil.MaxHP",
            StringComparison.Ordinal),
        "FireKing 10161 initialize scheduling source");
    Assert(fireKingSource.Contains("SetBodyState(27, true);",
            StringComparison.Ordinal) &&
        fireKingSource.Contains("StatusChanged();",
            StringComparison.Ordinal),
        "FireKing furious state publication source");
    Assert(!fireKingSource.Contains("GetMagStruckDamage",
            StringComparison.Ordinal) &&
        !fireKingSource.Contains("base.ResolveFullMagicDamage",
            StringComparison.Ordinal),
        "FireKing must not use the lossy or parent damage path");

    var factorySource = File.ReadAllText(Path.Combine(root, "GameSvr",
        "UsrSystem", "UsrEngn.cs"));
    // Re-based 2026-08-04 from `case 216:` to `case 150:` on 战神 bytes (see the
    // Equal(150, ...) note above for the exhaustive ownership proof).  Kept STRICT:
    // it still demands the factory arm exist AND that the stale 216 arm is gone, so
    // neither a missing wiring nor a silent revert to 216 can pass.
    Assert(factorySource.Contains("case 150:", StringComparison.Ordinal) &&
        factorySource.Contains("Cert = new FireKingMonster();",
            StringComparison.Ordinal),
        "race 150 factory wiring (native jt[68]=0x67A985 -> classref [0x67FF34] -> "
        + "ctor sub_6821F8)");
    Assert(!System.Text.RegularExpressions.Regex.IsMatch(factorySource,
            @"case\s+216\s*:"),
        "race 216 must NOT be wired: its index byte at 0x67A026+(216-0x0B) is 0x00, "
        + "so it routes to jt[0] = 0x67AE5E, the default sink (`xor eax,eax` -> nil)");

    var pasSource = File.ReadAllText(Path.Combine(root, "GameSvr",
        "ScriptSystem", "PasEngine", "PasApiBridge.cs"));
    int forceStart = pasSource.IndexOf("case \"makecattlecrazy\":",
        StringComparison.Ordinal);
    int forceEnd = forceStart < 0 ? -1 : pasSource.IndexOf("case ",
        forceStart + 1, StringComparison.Ordinal);
    Assert(forceStart >= 0 && forceEnd > forceStart,
        "MakeCattleCrazy PAS branch");
    string forceBranch = pasSource[forceStart..forceEnd];
    Assert(forceBranch.Contains("NativeFireKingEventState.ForceLocally();",
            StringComparison.Ordinal) &&
        forceBranch.Contains(
            "Grobal2.ISM_MAKE_CATTLE_CRAZY, 3, string.Empty",
            StringComparison.Ordinal) &&
        forceBranch.Contains("PasValue.FromBool(true)",
            StringComparison.Ordinal),
        "MakeCattleCrazy local and mirror force behavior");

    var mirrorSource = File.ReadAllText(Path.Combine(root, "GameSvr",
        "Snaps", "MirrorMessage.cs"));
    Assert(mirrorSource.Contains(
            "case Grobal2.ISM_MAKE_CATTLE_CRAZY:",
            StringComparison.Ordinal) &&
        mirrorSource.Contains("NativeFireKingEventState.ForceLocally();",
            StringComparison.Ordinal),
        "MakeCattleCrazy mirror receiver");
}

static string FindRepositoryRoot()
{
    foreach (var origin in new[]
             {
                 Directory.GetCurrentDirectory(), AppContext.BaseDirectory
             })
    {
        var directory = new DirectoryInfo(origin);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameSvr",
                    "GameSvr.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
    }
    throw new InvalidOperationException("repository root not found");
}

static void CheckDraw(int roll, int expectedValue, byte expectedTier)
{
    var player = new ProbePlayer();
    var cattle = player.m_NativeCattle;
    cattle.ProcessSceneType(3);
    var random = new ExpectedRandom("full-tier roll " + roll,
        (10000, 0),
        (8, 6),
        (8, 0), (7, 0), (6, 0), (5, 0),
        (4, 0), (3, 0), (2, 0), (1, 0),
        (100, roll));
    cattle.Add(50000, random.Next);
    random.AssertComplete();
    Assert(cattle.HasRevealPending && cattle.HasClaimPending,
        $"full-tier roll {roll} omitted tier-4 prize state");
    Equal(expectedValue, cattle.Value, $"full-tier roll {roll} value");
    Equal(expectedTier, cattle.Tier, $"full-tier roll {roll} tier");
}

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{message}: expected={expected}, actual={actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
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

sealed class ExpectedRandom
{
    private readonly string _scenario;
    private readonly Queue<(int Range, int Value)> _calls;

    internal ExpectedRandom(string scenario,
        params (int Range, int Value)[] calls)
    {
        _scenario = scenario;
        _calls = new Queue<(int Range, int Value)>(calls);
    }

    internal int Next(int range)
    {
        if (_calls.Count == 0)
            throw new InvalidOperationException(
                _scenario + " made an unexpected random call");
        var expected = _calls.Dequeue();
        if (expected.Range != range)
            throw new InvalidOperationException(
                $"{_scenario} random range: expected={expected.Range}, " +
                $"actual={range}");
        return expected.Value;
    }

    internal void AssertComplete()
    {
        if (_calls.Count != 0)
            throw new InvalidOperationException(
                $"{_scenario} random call count: expected=0, " +
                $"actual={_calls.Count}");
    }
}

sealed class ProbePlayer : TPlayObject
{
    internal List<ClientPacket> Packets { get; } = new();

    internal ProbePlayer()
    {
        m_boOffLineFlag = true;
    }

    internal override void SendSocket(ClientPacket defMsg, string message)
    {
        Packets.Add(defMsg);
    }
}
