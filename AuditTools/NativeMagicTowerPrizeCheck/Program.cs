using System.Collections;
using System.Reflection;
using System.Text;
using GameSvr;
using SystemModule;

try
{
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    PrepareRuntimeConfig();
    PrepareRuntime();
    PrepareTowerConfigs();

    CheckStartupConfigSnapshot();
    CheckConfigExceptionRecovery();
    CheckRandomInvocationContract();
    CheckClientPropertyAndPhaseGates();
    CheckClientPrizeCommitAndRepeat();
    CheckClientTierBoundaries();
    CheckClientBagFailureDoesNotRollback();
    CheckThresholdSelection();
    CheckSkyClearPredicateAndCommit();
    CheckSkyHeroFullLevelMessages();
    CheckSkyVitalityResidual();
    CheckSkyDiamondWriter();
    CheckSkyBagFailureDoesNotRollback();
    CheckSourceContract();

    Console.WriteLine(
        "PASS NativeMagicTowerPrizeCheck ClientGetPrize=property12/phase3->4/" +
        "D2B-clear/tier+xp+hidden+server+personal/no-rollback " +
        "GetSkyPrize=phase4/clear-room/phase0/D14+D18+D1C/diamond+100 " +
        "predicate=alive-unowned-race>=50 config=startup-GBK/roll<=threshold " +
        "rng=span-call-including-zero-negative hero200=DBFF+generic " +
        "vitality=session-cattle+FB-notice");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"NativeMagicTowerPrizeCheck FAIL: {exception}");
    return 1;
}

static void CheckConfigExceptionRecovery()
{
    TPlayObject.InitializeNativeMagicTowerPrizeCatalog("\0");
    try
    {
        var context = NewContext();
        context.Player.m_btNativeMagicTowerPhase = 3;
        context.Player.m_btNativeMagicTowerDefeatedMonsterCount = 50;
        context.Player.m_Abil.MaxExp = 0;

        Assert(context.Player.ClientGetNativeMagicTowerPrize(
            context.Npc, _ => 0), "invalid config path escaped startup");
        Equal(0, context.Player.m_Abil.Exp,
            "invalid config path retained partial experience config");
    }
    finally
    {
        TPlayObject.InitializeNativeMagicTowerPrizeCatalog(
            M2Share.sRootPath);
    }
}

static void CheckStartupConfigSnapshot()
{
    SetDefinitions("金天赐", "服务器奖", "个人奖", "篡改服务器奖",
        "篡改个人奖");
    WriteTowerConfigs(9_000_000, 9_500_000,
        "篡改服务器奖", "篡改个人奖");
    try
    {
        var context = NewContext();
        context.Player.m_btNativeMagicTowerPhase = 3;
        context.Player.m_btNativeMagicTowerDefeatedMonsterCount = 50;
        context.Player.m_btNativeMagicTowerSpecialRoute = 1;
        context.Player.m_boNativeMagicTowerHundredth = true;
        context.Player.m_Abil.MaxExp = 0;
        var rolls = new Queue<int>(new[] { 0, 0, 0 });

        Assert(context.Player.ClientGetNativeMagicTowerPrize(context.Npc,
            _ => rolls.Dequeue()), "startup snapshot claim rejected");
        Equal(1_800_000, context.Player.m_Abil.Exp,
            "claim reread mutated NewExp.ini");
        var dialog = MerchantMessage(context.Player);
        Assert(dialog.Contains("服务器大奖：服务器奖", StringComparison.Ordinal),
            "claim reread mutated NewServPrize.ini");
        Assert(dialog.Contains("个人大奖：个人奖", StringComparison.Ordinal),
            "claim reread mutated NewSelfPrize.ini");
    }
    finally
    {
        WriteTowerConfigs();
    }
}

static void CheckRandomInvocationContract()
{
    SetDefinitions("金天赐");
    WriteTowerConfigs(1_800_000, 1_800_000);
    TPlayObject.InitializeNativeMagicTowerPrizeCatalog(M2Share.sRootPath);
    var zero = NewContext();
    zero.Player.m_btNativeMagicTowerPhase = 3;
    zero.Player.m_btNativeMagicTowerDefeatedMonsterCount = 50;
    zero.Player.m_Abil.MaxExp = 0;
    var zeroRanges = new List<int>();
    Assert(zero.Player.ClientGetNativeMagicTowerPrize(zero.Npc, range =>
    {
        zeroRanges.Add(range);
        return 0;
    }), "zero-span claim rejected");
    Equal(1, zeroRanges.Count, "Random(0) call count");
    Equal(0, zeroRanges[0], "Random(0) range");

    var defaultZero = NewContext();
    defaultZero.Player.m_btNativeMagicTowerPhase = 3;
    defaultZero.Player.m_btNativeMagicTowerDefeatedMonsterCount = 50;
    defaultZero.Player.m_Abil.MaxExp = 0;
    Assert(defaultZero.Player.ClientGetNativeMagicTowerPrize(defaultZero.Npc),
        "legacy owner Random(0) path rejected");

    WriteTowerConfigs(2_300_000, 1_800_000);
    TPlayObject.InitializeNativeMagicTowerPrizeCatalog(M2Share.sRootPath);
    var negative = NewContext();
    negative.Player.m_btNativeMagicTowerPhase = 3;
    negative.Player.m_btNativeMagicTowerDefeatedMonsterCount = 50;
    negative.Player.m_Abil.MaxExp = 0;
    var negativeRanges = new List<int>();
    Assert(negative.Player.ClientGetNativeMagicTowerPrize(negative.Npc,
        range =>
        {
            negativeRanges.Add(range);
            return 0;
        }), "negative-span claim rejected");
    Equal(1, negativeRanges.Count, "Random(negative) call count");
    Equal(-500_000, negativeRanges[0], "Random(negative) range");

    WriteTowerConfigs();
    TPlayObject.InitializeNativeMagicTowerPrizeCatalog(M2Share.sRootPath);
}

static void CheckClientPropertyAndPhaseGates()
{
    SetDefinitions("金天赐");
    var disabled = NewContext(addProperty: false);
    disabled.Player.m_btNativeMagicTowerPhase = 3;
    disabled.Player.m_btNativeMagicTowerDefeatedMonsterCount = 50;
    Assert(!disabled.Player.ClientGetNativeMagicTowerPrize(
        disabled.Npc, _ => 0), "property-disabled client prize accepted");
    Equal((byte)3, disabled.Player.m_btNativeMagicTowerPhase,
        "property-disabled phase");
    Equal((byte)50, disabled.Player.m_btNativeMagicTowerDefeatedMonsterCount,
        "property-disabled D2B");
    Equal(0, disabled.Player.m_MsgList.Count,
        "property-disabled was not silent");

    var early = NewContext();
    early.Player.m_btNativeMagicTowerPhase = 2;
    early.Player.m_btNativeMagicTowerDefeatedMonsterCount = 50;
    Assert(!early.Player.ClientGetNativeMagicTowerPrize(early.Npc, _ => 0),
        "phase2 client prize accepted");
    Equal((byte)2, early.Player.m_btNativeMagicTowerPhase, "phase2 phase");
    Equal((byte)50, early.Player.m_btNativeMagicTowerDefeatedMonsterCount,
        "phase2 D2B");
    Assert(MerchantMessage(early.Player).Contains("您消灭的怪物太少了吧！",
        StringComparison.Ordinal), "phase2 dialog");
}

static void CheckClientPrizeCommitAndRepeat()
{
    SetDefinitions("金天赐", "神秘天赐", "服务器奖", "个人奖");
    var context = NewContext();
    var player = context.Player;
    player.m_btNativeMagicTowerPhase = 3;
    player.m_btNativeMagicTowerDefeatedMonsterCount = 50;
    player.m_btNativeMagicTowerMysteryFlag = 1;
    player.m_btNativeMagicTowerSpecialRoute = 1;
    player.m_boNativeMagicTowerHundredth = true;
    player.m_Abil.Exp = 10;
    player.m_Abil.MaxExp = 0;
    var values = new Queue<int>(new[] { 234_567, 0, 0 });

    Assert(player.ClientGetNativeMagicTowerPrize(context.Npc,
        _ => values.Dequeue()), "phase3 client prize rejected");
    Equal((byte)4, player.m_btNativeMagicTowerPhase, "client commit phase");
    Equal((byte)0, player.m_btNativeMagicTowerDefeatedMonsterCount,
        "client commit D2B");
    Equal((byte)0, player.m_btNativeMagicTowerMysteryFlag,
        "client hidden flag");
    Equal((byte)0, player.m_btNativeMagicTowerSpecialRoute,
        "client route flag");
    Assert(!player.m_boNativeMagicTowerHundredth,
        "client hundredth flag");
    Equal(4, player.m_ItemList.Count, "client item count");
    Equal(2_030_010, player.m_Abil.Exp, "client rounded experience");
    Equal(1, player.m_nNativeMagicTowerAllKilledCount,
        "all-killed counter");
    var dialog = MerchantMessage(player);
    Assert(dialog.Contains("您本次总共阻击了 50 个怪物", StringComparison.Ordinal),
        "client count dialog");
    Assert(dialog.Contains("服务器大奖：服务器奖", StringComparison.Ordinal),
        "client server prize dialog");
    Assert(dialog.Contains("个人大奖：个人奖", StringComparison.Ordinal),
        "client personal prize dialog");
    Assert(player.m_MsgList.Any(message =>
            message.wIdent == Grobal2.RM_SYSMESSAGE &&
            message.nParam2 == 0x38 &&
            message.Buff.Contains("全部怪物", StringComparison.Ordinal)),
        "client all-killed system message");

    var bagCount = player.m_ItemList.Count;
    var experience = player.m_Abil.Exp;
    player.m_MsgList.Clear();
    Assert(!player.ClientGetNativeMagicTowerPrize(context.Npc, _ => 0),
        "repeat client prize accepted");
    Equal(bagCount, player.m_ItemList.Count, "repeat client bag");
    Equal(experience, player.m_Abil.Exp, "repeat client experience");
    Equal(TPlayObject.NativeMagicTowerNextPrizeDialog,
        MerchantMessage(player)[("tower-npc/").Length..],
        "repeat client dialog");
}

static void CheckClientTierBoundaries()
{
    foreach (var (count, expected) in new[]
             {
                 (40, "木天赐"), (41, "铜天赐"), (46, "铜天赐"),
                 (47, "银天赐"), (49, "银天赐"), (50, "金天赐"),
                 (80, "金天赐"), (81, "木天赐")
             })
    {
        SetDefinitions("木天赐", "铜天赐", "银天赐", "金天赐");
        var context = NewContext();
        context.Player.m_btNativeMagicTowerPhase = 3;
        context.Player.m_btNativeMagicTowerDefeatedMonsterCount =
            unchecked((byte)count);
        context.Player.m_Abil.MaxExp = 0;
        Assert(context.Player.ClientGetNativeMagicTowerPrize(
            context.Npc, _ => 0), $"tier {count} rejected");
        Assert(MerchantMessage(context.Player).Contains(
            "您获得了：" + expected + "\\", StringComparison.Ordinal),
            $"tier {count} classification");
        if (expected == "木天赐")
            Equal(0, context.Player.m_Abil.Exp, $"tier {count} xp");
        else
            Equal(1_800_000, context.Player.m_Abil.Exp, $"tier {count} xp");
    }
}

static void CheckClientBagFailureDoesNotRollback()
{
    SetDefinitions("金天赐", "填充");
    var context = NewContext();
    FillBag(context.Player);
    context.Player.m_btNativeMagicTowerPhase = 3;
    context.Player.m_btNativeMagicTowerDefeatedMonsterCount = 50;
    context.Player.m_Abil.MaxExp = 0;

    Assert(context.Player.ClientGetNativeMagicTowerPrize(
        context.Npc, _ => 0), "bag-full client transaction rejected");
    Equal((byte)4, context.Player.m_btNativeMagicTowerPhase,
        "bag-full client phase rollback");
    Equal((byte)0, context.Player.m_btNativeMagicTowerDefeatedMonsterCount,
        "bag-full client D2B rollback");
    Equal(Grobal2.MAXBAGITEM, context.Player.m_ItemList.Count,
        "bag-full client added item");
    Equal(1_800_000, context.Player.m_Abil.Exp,
        "bag-full client experience");
}

static void CheckThresholdSelection()
{
    var serverConfig = Path.Combine(M2Share.sRootPath, "Share", "config",
        "NewServPrize.ini");
    Equal("服务器奖", TPlayObject.SelectNativeMagicTowerThresholdPrize(
        serverConfig, "配置1", 10), "threshold inclusive low");
    Equal("服务器奖2", TPlayObject.SelectNativeMagicTowerThresholdPrize(
        serverConfig, "配置1", 11), "threshold next");
    Equal(string.Empty, TPlayObject.SelectNativeMagicTowerThresholdPrize(
        serverConfig, "配置1", 100), "threshold miss");
}

static void CheckSkyClearPredicateAndCommit()
{
    SetDefinitions();
    var blocked = NewContext(withMap: true);
    blocked.Player.m_btNativeMagicTowerPhase = 4;
    blocked.Player.m_sNativeMagicTowerPrimaryPrize = "经验:100";
    var monster = PlaceActor(blocked.Map, 8, 9, Grobal2.RC_ANIMAL);

    Assert(!blocked.Player.GetNativeMagicTowerSkyPrize(blocked.Npc),
        "alive unowned monster did not block");
    Equal((byte)4, blocked.Player.m_btNativeMagicTowerPhase,
        "blocked sky phase changed");
    Equal("经验:100", blocked.Player.m_sNativeMagicTowerPrimaryPrize,
        "blocked sky descriptor changed");
    Equal("tower-npc/" + TPlayObject.NativeMagicTowerSkyPrizeFailureMessage,
        MerchantMessage(blocked.Player), "blocked sky dialog");

    monster.m_Master = blocked.Player;
    blocked.Player.m_MsgList.Clear();
    blocked.Player.m_Abil.MaxExp = 0;
    Assert(blocked.Player.GetNativeMagicTowerSkyPrize(blocked.Npc),
        "owned monster incorrectly blocked");
    Equal((byte)0, blocked.Player.m_btNativeMagicTowerPhase,
        "sky phase commit");
    Equal(100, blocked.Player.m_Abil.Exp, "sky primary experience");
    Equal(string.Empty, blocked.Player.m_sNativeMagicTowerPrimaryPrize,
        "sky primary not cleared");
    Assert(blocked.Player.m_MsgList.Any(message =>
            message.wIdent == Grobal2.RM_SYSMESSAGE &&
            message.nParam2 == 0xFC &&
            message.Buff.Contains("[经验:100]", StringComparison.Ordinal)),
        "sky result sysmessage");
    var result = "经过你的奋斗，你终于获得了[经验:100]" +
                 "\\可能有更好的宝藏在下一关等着您哦！";
    Equal("tower-npc/" + result +
          TPlayObject.NativeMagicTowerSkyNextPrizeDialog,
        MerchantMessage(blocked.Player), "sky exact next-level question");
}

static void CheckSkyHeroFullLevelMessages()
{
    SetDefinitions();
    var context = NewContext();
    var hero = new HeroObject { m_boOffLineFlag = true };
    hero.m_Abil.Level = 200;
    context.Player.m_HeroObject = hero;
    context.Player.m_btNativeMagicTowerPhase = 4;
    context.Player.m_sNativeMagicTowerPrimaryPrize = "英雄经验:30";

    Assert(context.Player.GetNativeMagicTowerSkyPrize(context.Npc),
        "hero level-200 sky claim rejected");
    var heroMessages = hero.m_MsgList.Where(message =>
        message.wIdent == Grobal2.RM_SYSMESSAGE).ToArray();
    Equal(1, heroMessages.Length, "hero level-200 hero message count");
    Equal("你的英雄级数已满", heroMessages[0].Buff,
        "hero level-200 first message");
    Equal(0xDB, heroMessages[0].nParam1,
        "hero level-200 foreground");
    Equal(0xFF, heroMessages[0].nParam2,
        "hero level-200 background");

    var playerMessages = context.Player.m_MsgList.Where(message =>
        message.wIdent == Grobal2.RM_SYSMESSAGE).ToArray();
    Equal(2, playerMessages.Length, "hero level-200 player message count");
    Equal("恭喜：你获得了：英雄经验:30", playerMessages[0].Buff,
        "hero level-200 generic success");
    Equal(0xFF, playerMessages[0].nParam1,
        "hero generic foreground");
    Equal(0xFC, playerMessages[0].nParam2,
        "hero generic background");
    Assert(playerMessages[1].Buff.Contains("[英雄经验:30]",
        StringComparison.Ordinal), "hero sky result message order");
}

static void CheckSkyVitalityResidual()
{
    SetDefinitions();
    var context = NewContext();
    context.Player.m_btNativeMagicTowerPhase = 4;
    context.Player.m_sNativeMagicTowerPrimaryPrize = "牛气值:25";

    Assert(context.Player.GetNativeMagicTowerSkyPrize(context.Npc),
        "vitality residual changed tower state commit");
    Equal((byte)0, context.Player.m_btNativeMagicTowerPhase,
        "vitality residual phase");
    Equal(string.Empty, context.Player.m_sNativeMagicTowerPrimaryPrize,
        "vitality residual descriptor clear");
    Equal(25, context.Player.m_NativeCattle.Value,
        "vitality residual cattle value");
    Assert(context.Player.m_MsgList.Any(message =>
            message.wIdent == Grobal2.RM_CATTLE_SYSMESSAGE &&
            message.wParam == 0xFB &&
            message.Buff == "25 点牛气值增加"),
        "vitality residual cattle notice");
    Assert(context.Player.m_MsgList.Any(message =>
            message.wIdent == Grobal2.RM_SYSMESSAGE &&
            message.Buff == "恭喜：你获得了：牛气值:25"),
        "vitality residual generic success");
}

static void CheckSkyDiamondWriter()
{
    SetDefinitions();
    M2Share.LogStringList.Clear();
    var context = NewContext();
    context.Player.m_btNativeMagicTowerPhase = 4;
    context.Player.m_btNativeMagicTowerSpecialRoute = 5;
    context.Player.m_sNativeMagicTowerServerPrize = "金刚石:100";
    context.Player.m_nNativeDiamondCache = 7;

    Assert(context.Player.GetNativeMagicTowerSkyPrize(context.Npc),
        "diamond sky prize rejected");
    Equal(107, context.Player.m_nNativeDiamondCache,
        "diamond cache delta");
    Equal((byte)0, context.Player.m_btNativeMagicTowerSpecialRoute,
        "diamond route clear");
    Equal(string.Empty, context.Player.m_sNativeMagicTowerServerPrize,
        "diamond descriptor clear");
    Equal("50\tplayer-map\t10\t20\tplayer\t金刚宝石\t100\t1\t闯天关大奖",
        (string)M2Share.LogStringList[0]!, "diamond exact log");
    Equal(0, context.Player.m_MsgList.Count(message =>
        message.wIdent == Grobal2.RM_LINGFU_CHANGED),
        "diamond writer refreshed capital");
}

static void CheckSkyBagFailureDoesNotRollback()
{
    SetDefinitions("奖励物", "个人物", "填充");
    var context = NewContext();
    FillBag(context.Player);
    context.Player.m_btNativeMagicTowerPhase = 4;
    context.Player.m_sNativeMagicTowerPrimaryPrize = "奖励物:1";
    context.Player.m_sNativeMagicTowerServerPrize = "奖励物";
    context.Player.m_sNativeMagicTowerPersonalPrize = "个人物";
    context.Player.m_btNativeMagicTowerSpecialRoute = 1;
    context.Player.m_boNativeMagicTowerHundredth = true;

    Assert(context.Player.GetNativeMagicTowerSkyPrize(context.Npc),
        "bag-full sky transaction rejected");
    Equal((byte)0, context.Player.m_btNativeMagicTowerPhase,
        "bag-full sky phase rollback");
    Equal(string.Empty, context.Player.m_sNativeMagicTowerPrimaryPrize,
        "bag-full sky primary rollback");
    Equal(string.Empty, context.Player.m_sNativeMagicTowerServerPrize,
        "bag-full sky server rollback");
    Equal(string.Empty, context.Player.m_sNativeMagicTowerPersonalPrize,
        "bag-full sky personal rollback");
    Equal((byte)0, context.Player.m_btNativeMagicTowerSpecialRoute,
        "bag-full sky route rollback");
    Assert(!context.Player.m_boNativeMagicTowerHundredth,
        "bag-full sky personal flag rollback");
    Equal(Grobal2.MAXBAGITEM, context.Player.m_ItemList.Count,
        "bag-full sky added item");
}

static void CheckSourceContract()
{
    var root = FindRepositoryRoot();
    var source = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.NativeMagicTower.Prize.cs"));
    Assert(source.Contains("m_btNativeMagicTowerDefeatedMonsterCount = 0;",
        StringComparison.Ordinal), "D2B commit missing");
    Assert(source.Contains("m_btNativeMagicTowerPhase = 4;",
        StringComparison.Ordinal), "client phase commit missing");
    Assert(source.Contains("m_btNativeMagicTowerPhase = 0;",
        StringComparison.Ordinal), "sky phase commit missing");
    Assert(source.Contains("File.ReadLines(path, HUtil32.GbkEncoding)",
        StringComparison.Ordinal), "GBK config reader missing");
    Assert(source.Contains("if (roll <= entries[index].Threshold)",
        StringComparison.Ordinal), "inclusive threshold missing");
    Assert(!source.Contains("DropItemDown", StringComparison.Ordinal),
        "prize failure drops to ground");
    Assert(!source.Contains("PasApiBridge", StringComparison.Ordinal),
        "prize implementation coupled to PAS bridge");
    Assert(source.Contains("var addition = random(span);",
        StringComparison.Ordinal), "Random(span) call was gated");
    Assert(!source.Contains("DelphiRandom", StringComparison.Ordinal),
        "tower was wired into the dormant process-global Delphi owner");
    Assert(source.Contains("success = AddNativeCattle(amount);",
        StringComparison.Ordinal), "vitality descriptor bypasses cattle state");

    var m2Share = File.ReadAllText(Path.Combine(root, "GameSvr", "M2Share.cs"));
    Assert(m2Share.Contains(
            "TPlayObject.InitializeNativeMagicTowerPrizeCatalog(sRootPath);",
            StringComparison.Ordinal),
        "tower prize catalog is not captured at startup");
}

static Context NewContext(bool addProperty = true, bool withMap = false)
{
    M2Share.LogStringList.Clear();
    var map = withMap ? NewMap() : null;
    var player = new TPlayObject
    {
        m_boOffLineFlag = true,
        m_sCharName = "player",
        m_sMapName = "player-map",
        m_nCurrX = 10,
        m_nCurrY = 20,
        m_PEnvir = map
    };
    var npc = new NormNpc
    {
        m_sCharName = "tower-npc",
        m_sMapName = "npc-map"
    };
    if (addProperty) npc.AddNativePasProperty(12);
    return new Context(player, npc, map);
}

static Envirnoment NewMap()
{
    var map = new Envirnoment { sMapName = "player-map" };
    typeof(Envirnoment).GetMethod("Initialize",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(map, new object[] { (short)32, (short)32 });
    return map;
}

static TBaseObject PlaceActor(Envirnoment map, short x, short y, int race)
{
    var actor = new TBaseObject
    {
        m_boOffLineFlag = true,
        m_PEnvir = map,
        m_sMapName = map.sMapName,
        m_nCurrX = x,
        m_nCurrY = y,
        m_btRaceServer = unchecked((byte)race),
        bo2B9 = true
    };
    Assert(ReferenceEquals(actor, map.AddToMap(x, y,
        CellType.OS_MOVINGOBJECT, actor)), "actor placement");
    return actor;
}

static void FillBag(TPlayObject player)
{
    while (player.m_ItemList.Count < Grobal2.MAXBAGITEM)
    {
        player.m_ItemList.Add(new TUserItem
        {
            MakeIndex = player.m_ItemList.Count + 1,
            wIndex = 1,
            Dura = 1,
            DuraMax = 1,
            btValue = new byte[14]
        });
    }
}

static void SetDefinitions(params string[] names)
{
    M2Share.UserEngine.StdItemList.Clear();
    foreach (var name in names)
    {
        M2Share.UserEngine.StdItemList.Add(new GoodItem
        {
            Name = name,
            StdMode = 0,
            DuraMax = 1,
            Weight = 1
        });
    }
}

static string MerchantMessage(TPlayObject player)
{
    var messages = player.m_MsgList.Where(message =>
        message.wIdent == Grobal2.RM_MERCHANTSAY).ToArray();
    Equal(1, messages.Length, "merchant dialog count");
    return messages[0].Buff;
}

static void PrepareTowerConfigs()
{
    WriteTowerConfigs();
    TPlayObject.InitializeNativeMagicTowerPrizeCatalog(M2Share.sRootPath);
}

static void WriteTowerConfigs(int minimumExperience = 1_800_000,
    int maximumExperience = 2_300_000,
    string serverPrize = "服务器奖", string personalPrize = "个人奖")
{
    var config = Path.Combine(M2Share.sRootPath, "Share", "config");
    Directory.CreateDirectory(config);
    File.WriteAllText(Path.Combine(config, "NewExp.ini"),
        "[配置]\r\n最小经验=" + minimumExperience +
        "\r\n最大经验=" + maximumExperience + "\r\n",
        HUtil32.GbkEncoding);
    File.WriteAllText(Path.Combine(config, "NewServPrize.ini"),
        "[配置1]\r\n爆物1=" + serverPrize +
        "/10\r\n爆物2=服务器奖2/99\r\n",
        HUtil32.GbkEncoding);
    File.WriteAllText(Path.Combine(config, "NewSelfPrize.ini"),
        "[配置]\r\n爆物1=" + personalPrize + "/99\r\n",
        HUtil32.GbkEncoding);
}

static string FindRepositoryRoot()
{
    return AuditRepoRoot.Resolve();
}

static void PrepareRuntime()
{
    M2Share.g_Config = new GameSvrConfig { nSendRefMsgRange = 12 };
    M2Share.UserEngine = new UserEngine();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.MapManager = new MapManager();
    M2Share.RandomNumber = RandomNumber.GetInstance();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new ArrayList();
    M2Share.g_MonSayMsgList = new Dictionary<string, IList<TMonSayMsg>>();
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
    var share = Path.Combine(Path.GetFullPath(
        Path.Combine(runtimeDirectory, "..")), "Share");
    Directory.CreateDirectory(share);
    File.WriteAllText(Path.Combine(share, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(share, "ServerData.ini"),
        "[Integer]" + Environment.NewLine);
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

readonly record struct Context(TPlayObject Player, NormNpc Npc,
    Envirnoment Map);
