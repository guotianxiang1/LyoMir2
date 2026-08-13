using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Buffers.Binary;
using GameSvr;
using SystemModule;
using Prize = GameSvr.NativeSecHeroPracticePrizeManager.Prize;

PrepareRuntimeConfig();

const string originalSha256 =
    "CC505716AEB2FDB09C96B805D06C1DDDCD70DB0F331EF42AE1338C71766B452F";
var originalM2 = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])
    ? args[0]
    : Environment.GetEnvironmentVariable("LYOMIR_ORIGINAL_M2") ?? ResolveOriginalM2();
if (!File.Exists(originalM2))
{
    // Absence of the golden binary says nothing about the C# port, so this is
    // exit 2 (run_audits.py EXIT_INCOMPLETE), not a failed contract. When the
    // file IS present the SHA256 below still has to match exactly.
    Console.Error.WriteLine("INCOMPLETE: original M2 baseline was not found: "
        + originalM2 + ". Pass its path as argument 1 or set LYOMIR_ORIGINAL_M2.");
    Environment.Exit(2);
}
Equal(originalSha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(originalM2))),
    "original M2 SHA256");

Equal(1216, ReadConstant(nameof(Grobal2.CM_SECHERO_PRACTICE)),
    "CM_SECHERO_PRACTICE");
Equal(1216, ReadConstant(nameof(Grobal2.SM_SECHERO_PRACTICE)),
    "SM_SECHERO_PRACTICE");
VerifyProtocolSource();

var intervalMethod = typeof(TPlayObject).GetMethod(
    "HasSecHeroPracticeIntervalElapsed",
    BindingFlags.Static | BindingFlags.NonPublic);
Assert(intervalMethod != null,
    "HasSecHeroPracticeIntervalElapsed internal method was not found");

VerifyInterval(10_000, 0, false, "positive exact boundary");
VerifyInterval(10_001, 0, true, "positive elapsed boundary");
VerifyInterval(0, 10_000, false, "negative exact boundary");
VerifyInterval(0, 10_001, true, "negative elapsed boundary");
VerifyInterval(int.MinValue, 0, false,
    "Delphi-compatible int.MinValue absolute-value wrap");
VerifyInterval(int.MinValue, int.MaxValue, false,
    "tick counter wrap with one elapsed unit");
VerifyInterval(int.MinValue + 10_000, int.MaxValue, true,
    "tick counter wrap with 10001 elapsed units");

VerifyPrizeSelection();
VerifyNativeEmptyPools();
M2Share.ObjectManager = new ObjectManager();
M2Share.ProcessMsgCriticalSection = new object();
M2Share.LogMsgCriticalSection = new object();
VerifyClientResultStates();
VerifyPracticeCycles();
VerifyNativeLingFuDebit();

Console.WriteLine(
    "PASS baseline=CC505716... CM/SM=1216 dispatch=p3,p2 response=empty " +
    "ticks=10000/10001/negative/int.MinValue tiers=1..3 roll=0..999 " +
    "threshold=first-inclusive native-pools=3x1000-empty states=0/-1 " +
    "cycles=gold/base/empty-pool lingfu=Value2,Value,native " +
    "logout=918 colors=FCFF,38FF logs=555550,555551,30010");
return;

void VerifyInterval(int nowTick, int lastTick, bool expected, string scenario)
{
    var actual = (bool)intervalMethod.Invoke(null, new object[] { nowTick, lastTick })!;
    Equal(expected, actual, scenario);
}

/// <summary>
/// The operator Desktop tree this used to name is not in the repo and is not on
/// this machine, so the check always took the exit-2 INCOMPLETE branch and proved
/// nothing. staging/ys207_original_capture/Mir200/GS1/M2Server.exe is the same
/// 7774208-byte binary and hashes to the CC505716... the check demands, so it is
/// searched first. The SHA256 gate below is unchanged and still decides.
/// </summary>
static string ResolveOriginalM2()
{
    foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
    {
        for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
        {
            var captured = Path.Combine(dir.FullName, "staging",
                "ys207_original_capture", "Mir200", "GS1", "M2Server.exe");
            if (File.Exists(captured))
                return captured;
        }
    }

    return "C:\\Users\\Administrator\\Desktop\\\u4E09\u9F99\u4F4D\\"
        + "\u4ED9\u7F18\u590D\u5DE50.3\u5929\u9F99\\mud2.0\\Mir200\\Gs1\\M2Server.exe";
}

static void VerifyProtocolSource()
{
    var root = FindRepositoryRoot();
    var constantsSource = File.ReadAllText(
        Path.Combine(root, "SystemModule", "Grobal2.cs"));
    var dispatchSource = File.ReadAllText(
        Path.Combine(root, "GameSvr", "Players", "TPlayObject.Message.cs"));
    var practiceSource = File.ReadAllText(
        Path.Combine(root, "GameSvr", "Players", "TPlayObject.SecHeroPractice.cs"));
    var gameAppSource = File.ReadAllText(
        Path.Combine(root, "GameSvr", "GameApp.cs"));
    var managerSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Services",
        "NativeSecHeroPracticePrizeManager.cs"));
    var playerBaseSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.Base.cs"));
    var userEngineSource = File.ReadAllText(Path.Combine(root, "GameSvr", "UsrSystem",
        "UsrEngn.cs"));
    var nativeLingFuSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.NativeLingFu.cs"));

    RequireMatches(constantsSource,
        @"public\s+const\s+int\s+CM_SECHERO_PRACTICE\s*=\s*1216\s*;",
        1, "CM_SECHERO_PRACTICE source declaration");
    RequireMatches(constantsSource,
        @"public\s+const\s+int\s+SM_SECHERO_PRACTICE\s*=\s*1216\s*;",
        1, "SM_SECHERO_PRACTICE source declaration");
    RequireMatches(dispatchSource,
        @"case\s+Grobal2\.CM_SECHERO_PRACTICE\s*:\s*" +
        @"ClientSecHeroPractice\s*\(\s*\(byte\)\s*ProcessMsg\.nParam3\s*,\s*" +
        @"\(byte\)\s*ProcessMsg\.nParam2\s*\)\s*;\s*break\s*;",
        1, "CM 1216 low-byte reward/cost dispatch order");
    RequireMatches(practiceSource,
        @"SendDefMessage\s*\(\s*Grobal2\.SM_SECHERO_PRACTICE\s*,\s*result\s*,\s*" +
        @"0\s*,\s*0\s*,\s*0\s*,\s*string\.Empty\s*\)\s*;",
        1, "SM 1216 empty-body response structure");
    Assert(!gameAppSource.Contains("zdxiulian.ini",
            StringComparison.OrdinalIgnoreCase) &&
        !managerSource.Contains("zdxiulian.ini",
            StringComparison.OrdinalIgnoreCase),
        "unused zdxiulian.ini was added to the native startup path");
    RequireMatches(practiceSource,
        @"RemoveHero\s*\(\s*this\s*\)\s*==\s*true\s*\)\s*\{\s*" +
        @"result\s*=\s*1\s*;\s*" +
        @"SendDefMessage\s*\(\s*Grobal2\.SM_HERO_LOGOUT\s*,\s*0\s*,\s*0\s*,\s*" +
        @"0\s*,\s*0\s*,\s*string\.Empty\s*\)",
        1, "successful practice hero removal must send empty SM_HERO_LOGOUT");
    RequireMatches(practiceSource,
        @"StopSecHeroPractice\s*\(\s*\)\s*\{\s*" +
        @"ClearSecHeroPractice\s*\(\s*\)\s*;[\s\S]{0,180}?" +
        @"您的副将英雄放养已结束！",
        1, "native practice normal stop message");
    RequireMatches(practiceSource,
        @"ClearSecHeroPractice\s*\(\s*\)\s*\{\s*" +
        @"m_btSecHeroPracticeCostTier\s*=\s*0\s*;\s*" +
        @"m_btSecHeroPracticeRewardMode\s*=\s*0\s*;",
        1, "native practice clear field order");
    RequireMatches(playerBaseSource,
        @"public\s+TPlayObject\s*\(\s*\)[\s\S]{0,800}?" +
        @"m_dwSecHeroPracticeTick\s*=\s*HUtil32\.GetTickCount\s*\(\s*\)\s*;",
        1, "practice tick constructor initialization");
    RequireMatches(practiceSource,
        @"ResumeSecHeroPracticeAfterLogon\s*\(\s*\)\s*\{\s*" +
        @"if\s*\(\s*\(uint\)\s*\(m_btSecHeroPracticeCostTier\s*-\s*1\)",
        1, "logon resume must not reset the constructor practice tick");
    // The save entry is now two thin public overloads over one private core, so
    // the old anchor matched the 3-line forwarder and the flush fell outside the
    // 2200-character window. Pin the forwarding instead, then check the ordering
    // inside the core — "shared entry" is still exactly what is enforced.
    RequireMatches(userEngineSource,
        @"public\s+void\s+SaveHumanRcd\s*\(\s*TPlayObject\s+PlayObject\s*\)\s*\{\s*" +
        @"SaveHumanRcdCore\s*\(\s*PlayObject\s*,\s*0\s*\)\s*;\s*\}",
        1, "SaveHumanRcd(1-arg) must forward to the shared core");
    RequireMatches(userEngineSource,
        @"public\s+void\s+SaveHumanRcd\s*\(\s*TPlayObject\s+PlayObject\s*,\s*" +
        @"ushort\s+saveMode\s*\)\s*\{\s*" +
        @"SaveHumanRcdCore\s*\(\s*PlayObject\s*,\s*saveMode\s*\)\s*;\s*\}",
        1, "SaveHumanRcd(2-arg) must forward to the shared core");
    RequireMatches(userEngineSource,
        @"SaveHumanRcdCore\s*\(\s*TPlayObject\s+PlayObject\s*,\s*ushort\s+saveMode\s*\)" +
        @"[\s\S]*?PlayObject\.FlushSecHeroPracticeLingFuLog\s*\(\s*\)\s*;" +
        @"[\s\S]{0,2000}?PlayObject\.MakeSaveRcd",
        1, "practice LingFu summary at shared save entry");
    Assert(!practiceSource.Contains("StringComparison.OrdinalIgnoreCase",
            StringComparison.Ordinal) &&
        practiceSource.Contains("StringComparison.Ordinal)", StringComparison.Ordinal),
        "practice reward kind comparison is not native strict equality");
    Assert(!practiceSource.Contains("SysMsg(", StringComparison.Ordinal),
        "practice messages still use configurable colors instead of native fixed bytes");
    RequireMatches(nativeLingFuSource,
        @"reason\s+is\s+not\s+\(\s*30_003\s+or\s+30_006\s*\)\s*\)\s*" +
        @"AddNativeLingFuDebitLog\s*\(\s*reason\s*,\s*amount\s*\)\s*;\s*" +
        @"m_nUsedLingFu\s*=",
        1, "native immediate LingFu debit log order and exclusions");
}

static void VerifyNativeEmptyPools()
{
    var manager = M2Share.SecHeroPracticePrizeManager;
    Assert(manager != null, "native secondary-hero practice manager was not constructed");

    var field = typeof(NativeSecHeroPracticePrizeManager).GetField("_pools",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(field != null, "native secondary-hero practice pools were not found");
    var pools = (IReadOnlyList<Prize>[])field!.GetValue(manager)!;
    Equal(4, pools.Length, "native secondary-hero practice pool slots");
    for (var tier = 1; tier <= 3; tier++)
    {
        Assert(pools[tier] is List<Prize>, $"tier {tier} native pool type");
        var pool = (List<Prize>)pools[tier];
        Equal(0, pool.Count, $"tier {tier} native pool count");
        Equal(1000, pool.Capacity, $"tier {tier} native pool capacity");
        Assert(!manager.TrySelect(tier, out _),
            $"tier {tier} native empty pool did not fail closed");
    }
}

static void VerifyClientResultStates()
{
    var method = typeof(TPlayObject).GetMethod("ClientSecHeroPractice",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(method != null, "ClientSecHeroPractice internal method was not found");

    var player = NewOfflinePlayer();
    method!.Invoke(player, new object[] { (byte)2, (byte)1 });
    VerifyPracticeResponse(player, 0, "no hero response");

    var hero = new HeroObject();
    typeof(HeroObject).GetProperty(nameof(HeroObject.HeroType))!
        .SetValue(hero, (byte)2);
    hero.m_Abil.Level = 77;
    player.m_HeroObject = hero;
    method.Invoke(player, new object[] { (byte)3, (byte)2 });
    VerifyPracticeResponse(player, -1, "missing callback response");
    Assert(ReferenceEquals(hero, player.m_HeroObject),
        "failed callback removed the secondary hero");
    Equal((byte)0, player.m_btSecHeroPracticeRewardMode,
        "failed callback reward mode");
    Equal((byte)0, player.m_btSecHeroPracticeCostTier,
        "failed callback cost tier");
    Equal((ushort)0, player.m_wSecHeroPracticeLevel,
        "failed callback level snapshot");
}

static void VerifyPracticeCycles()
{
    var method = typeof(TPlayObject).GetMethod("ProcessSecHeroPractice",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(method != null, "ProcessSecHeroPractice private method was not found");
    var previousService = M2Share.CreditCardService;
    var previousManager = M2Share.SecHeroPracticePrizeManager;
    try
    {
        M2Share.CreditCardService = CreateCreditCardService(enabled: true);

        var tier1 = NewOfflinePlayer();
        tier1.m_nGold = 100;
        tier1.m_btSecHeroPracticeRewardMode = 1;
        tier1.m_btSecHeroPracticeCostTier = 1;
        tier1.m_wSecHeroPracticeLevel = 10;
        method!.Invoke(tier1, null);
        Equal(50, tier1.m_nGold, "tier 1 gold debit");
        Equal((uint)880, ReadAccumulator(tier1, 0), "tier 1 main experience");
        Equal((uint)113, ReadAccumulator(tier1, 1), "tier 1 inner experience");

        var emptyTier2 = NewOfflinePlayer();
        emptyTier2.m_nGold = 100;
        emptyTier2.m_btSecHeroPracticeRewardMode = 2;
        emptyTier2.m_btSecHeroPracticeCostTier = 2;
        emptyTier2.m_wSecHeroPracticeLevel = 10;
        emptyTier2.m_CreditCard.Loaded = true;
        emptyTier2.m_CreditCard.Value2 = 1;
        method.Invoke(emptyTier2, null);
        Equal(50, emptyTier2.m_nGold, "tier 2 gold debit");
        Equal((uint)1750, ReadAccumulator(emptyTier2, 0),
            "tier 2 base experience before empty pool");
        Equal(1, emptyTier2.m_CreditCard.Value2,
            "empty tier 2 pool deducted CreditCard Value2");
        Equal(0, emptyTier2.m_nUsedLingFu,
            "empty tier 2 pool changed native used LingFu");
        Equal(0, ReadInternalInt(emptyTier2, "m_nSecHeroPracticeLingFuUsed"),
            "empty tier 2 pool changed practice LingFu usage");

        var insufficientGold = NewOfflinePlayer();
        insufficientGold.m_nGold = 49;
        insufficientGold.m_btSecHeroPracticeRewardMode = 1;
        insufficientGold.m_btSecHeroPracticeCostTier = 1;
        insufficientGold.m_wSecHeroPracticeLevel = 10;
        method.Invoke(insufficientGold, null);
        Equal(49, insufficientGold.m_nGold, "insufficient gold balance");
        Equal(1, CountPracticeMessage(insufficientGold,
            "您的金币不足，副将英雄的自动修炼终止"),
            "insufficient gold stop message count");
        Equal(0, CountPracticeMessage(insufficientGold,
            "您的副将英雄放养已结束！"),
            "insufficient gold normal stop message count");
        Equal((uint)0, ReadAccumulator(insufficientGold, 0),
            "insufficient gold granted base experience");
        Equal((byte)0, insufficientGold.m_btSecHeroPracticeRewardMode,
            "insufficient gold reward mode stop");
        Equal((byte)0, insufficientGold.m_btSecHeroPracticeCostTier,
            "insufficient gold cost tier stop");
        Equal((ushort)10, insufficientGold.m_wSecHeroPracticeLevel,
            "insufficient gold cleared level snapshot");

        var insufficientLingFu = NewOfflinePlayer();
        insufficientLingFu.m_nGold = 100;
        insufficientLingFu.m_nLingFu = 9;
        insufficientLingFu.m_btSecHeroPracticeRewardMode = 3;
        insufficientLingFu.m_btSecHeroPracticeCostTier = 3;
        insufficientLingFu.m_wSecHeroPracticeLevel = 10;
        insufficientLingFu.m_CreditCard.Loaded = true;
        method.Invoke(insufficientLingFu, null);
        Equal(50, insufficientLingFu.m_nGold, "insufficient LingFu gold debit");
        Equal(1, CountPracticeMessage(insufficientLingFu,
            "您的灵符不足，副将英雄的自动修炼终止"),
            "insufficient LingFu stop message count");
        Equal(0, CountPracticeMessage(insufficientLingFu,
            "您的副将英雄放养已结束！"),
            "insufficient LingFu normal stop message count");
        Equal((uint)225, ReadAccumulator(insufficientLingFu, 1),
            "insufficient LingFu base experience");
        Equal(9, insufficientLingFu.m_nLingFu,
            "insufficient LingFu changed native balance");
        Equal((byte)0, insufficientLingFu.m_btSecHeroPracticeRewardMode,
            "insufficient LingFu reward mode stop");
        Equal((byte)0, insufficientLingFu.m_btSecHeroPracticeCostTier,
            "insufficient LingFu cost tier stop");

        var pools = new IReadOnlyList<Prize>[4];
        pools[1] = Array.Empty<Prize>();
        pools[2] = new[] { new Prize("经验", 7, 999) };
        pools[3] = new[] { new Prize("内功经验", 9, 999) };
        M2Share.SecHeroPracticePrizeManager = CreateManager(pools, _ => 0);
        M2Share.LogStringList ??= new System.Collections.ArrayList();
        M2Share.LogStringList.Clear();

        var bonus = NewOfflinePlayer();
        bonus.m_sMapName = "practice-map";
        bonus.m_nCurrX = 12;
        bonus.m_nCurrY = 34;
        bonus.m_sCharName = "practice-player";
        bonus.m_nGold = 100;
        bonus.m_btSecHeroPracticeRewardMode = 2;
        bonus.m_btSecHeroPracticeCostTier = 2;
        bonus.m_wSecHeroPracticeLevel = 10;
        bonus.m_CreditCard.Loaded = true;
        bonus.m_CreditCard.Value2 = 1;
        method.Invoke(bonus, null);
        Equal((uint)1757, ReadAccumulator(bonus, 0),
            "tier 2 selected bonus experience");
        Equal(1, ReadInternalInt(bonus, "m_nSecHeroPracticeLingFuUsed"),
            "tier 2 practice LingFu usage accumulation");
        Equal(2, M2Share.LogStringList.Count,
            "selected bonus and immediate LingFu log count");
        Equal("9\tpractice-map\t12\t34\tpractice-player\t副将累计经验\t555550\t7\t副将放养给予",
            (string)M2Share.LogStringList[0]!, "selected bonus native log columns");
        Equal("10\tpractice-map\t12\t34\tpractice-player\t灵符\t30010\t1\t",
            (string)M2Share.LogStringList[1]!,
            "practice immediate LingFu native log columns");

        var flushMethod = typeof(TPlayObject).GetMethod(
            "FlushSecHeroPracticeLingFuLog",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert(flushMethod != null,
            "FlushSecHeroPracticeLingFuLog internal method was not found");
        flushMethod!.Invoke(bonus, null);
        Equal(3, M2Share.LogStringList.Count, "practice LingFu summary log count");
        Equal("10\tpractice-map\t12\t34\tpractice-player\t灵符\t30010\t1\t副将英雄放养消耗",
            (string)M2Share.LogStringList[2]!, "practice LingFu native summary columns");
        Equal(0, ReadInternalInt(bonus, "m_nSecHeroPracticeLingFuUsed"),
            "practice LingFu summary reset");

        M2Share.LogStringList.Clear();
        var innerBonus = NewOfflinePlayer();
        innerBonus.m_sMapName = "inner-map";
        innerBonus.m_nCurrX = 56;
        innerBonus.m_nCurrY = 78;
        innerBonus.m_sCharName = "inner-player";
        innerBonus.m_nGold = 100;
        innerBonus.m_btSecHeroPracticeRewardMode = 3;
        innerBonus.m_btSecHeroPracticeCostTier = 3;
        innerBonus.m_wSecHeroPracticeLevel = 10;
        innerBonus.m_CreditCard.Loaded = true;
        innerBonus.m_CreditCard.Value2 = 10;
        method.Invoke(innerBonus, null);
        Equal((uint)234, ReadAccumulator(innerBonus, 1),
            "tier 3 selected inner experience");
        Equal("9\tinner-map\t56\t78\tinner-player\t副将累计内功经验\t555551\t9\t副将放养给予",
            (string)M2Share.LogStringList[0]!,
            "selected inner bonus native log columns");
        Equal("10\tinner-map\t56\t78\tinner-player\t灵符\t30010\t10\t",
            (string)M2Share.LogStringList[1]!,
            "tier 3 immediate LingFu native log columns");
    }
    finally
    {
        M2Share.CreditCardService = previousService;
        M2Share.SecHeroPracticePrizeManager = previousManager;
    }
}

static void VerifyNativeLingFuDebit()
{
    var previousService = M2Share.CreditCardService;
    try
    {
        M2Share.CreditCardService = CreateCreditCardService(enabled: true);
        var player = NewOfflinePlayer();
        player.m_nLingFu = 10;
        player.m_nUsedLingFu = 2;
        player.m_CreditCard.Loaded = true;
        player.m_CreditCard.Value2 = 3;
        player.m_CreditCard.Value = 4;
        player.m_CreditCard.UsedValue = 5;
        player.m_sMapName = "debit-map";
        player.m_nCurrX = 90;
        player.m_nCurrY = 91;
        player.m_sCharName = "debit-player";
        player.m_NPC = new NormNpc
        {
            m_sCharName = "shop-npc",
            m_sMapName = "npc-map"
        };
        M2Share.LogStringList ??= new System.Collections.ArrayList();
        M2Share.LogStringList.Clear();

        Assert(player.DecNativeLingFu(30_010, 8),
            "native LingFu debit failed");
        Equal(0, player.m_CreditCard.Value2, "Value2 debit order");
        Equal(0, player.m_CreditCard.Value, "Value debit order");
        Equal(9, player.m_CreditCard.UsedValue, "UsedValue accounting");
        Equal(9, player.m_nLingFu, "native LingFu remainder");
        Equal(10, player.m_nUsedLingFu, "native used LingFu accounting");
        Assert(player.m_CreditCard.Dirty, "CreditCard debit did not mark dirty");
        Equal(1, player.m_MsgList.Count,
            "successful LingFu debit must enqueue one capital refresh");
        Equal(Grobal2.RM_LINGFU_CHANGED, player.m_MsgList[0].wIdent,
            "successful LingFu internal capital refresh ident");
        Assert(ReferenceEquals(player, player.m_MsgList[0].BaseObject),
            "successful LingFu internal capital refresh target");
        Assert(player.m_DefMsg == null ||
               player.m_DefMsg.Ident != (ushort)Grobal2.RM_LINGFU_CHANGED,
            "internal capital refresh 10054 leaked to the client header");
        Equal("10\tdebit-map\t90\t91\tdebit-player\t灵符\t30010\t8\tnpc扣除shop-npc-npc-map",
            (string)M2Share.LogStringList[0]!,
            "native LingFu NPC description log columns");

        var before = new[]
        {
            player.m_CreditCard.Value2, player.m_CreditCard.Value,
            player.m_CreditCard.UsedValue, player.m_nLingFu, player.m_nUsedLingFu
        };
        player.m_MsgList.Clear();
        player.m_DefMsg = Grobal2.MakeDefaultMsg(42, 0, 0, 0, 0);
        Assert(!player.DecNativeLingFu(30_010, 10),
            "insufficient native LingFu debit succeeded");
        var after = new[]
        {
            player.m_CreditCard.Value2, player.m_CreditCard.Value,
            player.m_CreditCard.UsedValue, player.m_nLingFu, player.m_nUsedLingFu
        };
        Assert(before.SequenceEqual(after),
            "insufficient native LingFu debit changed balances");
        Equal((ushort)42, player.m_DefMsg.Ident,
            "insufficient native LingFu debit sent a refresh");
        Assert(!player.m_MsgList.Any(message =>
                message.wIdent == Grobal2.RM_LINGFU_CHANGED),
            "insufficient native LingFu debit queued a capital refresh");
    }
    finally
    {
        M2Share.CreditCardService = previousService;
    }
}

static TPlayObject NewOfflinePlayer()
{
    return new TPlayObject { m_boOffLineFlag = true };
}

static int CountPracticeMessage(TPlayObject player, string message)
{
    return player.m_MsgList.Count(entry => entry.wIdent == Grobal2.RM_SYSMESSAGE
        && entry.nParam1 == 0xFF
        && entry.Buff == message);
}

static void VerifyPracticeResponse(TPlayObject player, int recog, string scenario)
{
    Equal((ushort)Grobal2.SM_SECHERO_PRACTICE, player.m_DefMsg.Ident,
        scenario + " ident");
    Equal(recog, player.m_DefMsg.Recog, scenario + " recog");
    Equal((ushort)0, player.m_DefMsg.Param, scenario + " param");
    Equal((ushort)0, player.m_DefMsg.Tag, scenario + " tag");
    Equal((ushort)0, player.m_DefMsg.Series, scenario + " series");
}

static uint ReadAccumulator(TPlayObject player, int slot)
{
    var field = typeof(TPlayObject).GetField("m_NativeHeroExperienceAccumulator",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(field != null, "native hero experience accumulator was not found");
    var bytes = (byte[])field!.GetValue(player)!;
    return BinaryPrimitives.ReadUInt32LittleEndian(
        bytes.AsSpan(8 + slot * sizeof(uint), sizeof(uint)));
}

static int ReadInternalInt(TPlayObject player, string name)
{
    var field = typeof(TPlayObject).GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(field != null, $"internal player field was not found: {name}");
    return (int)field!.GetValue(player)!;
}

static NativeCreditCardService CreateCreditCardService(bool enabled)
{
    var constructor = typeof(NativeCreditCardService).GetConstructor(
        BindingFlags.Instance | BindingFlags.NonPublic,
        binder: null,
        new[] { typeof(bool), typeof(bool), typeof(string), typeof(byte[]) },
        modifiers: null);
    Assert(constructor != null, "NativeCreditCardService private constructor was not found");
    return (NativeCreditCardService)constructor!.Invoke(
        new object[] { enabled, false, string.Empty, new byte[5] });
}

static void VerifyPrizeSelection()
{
    var pools = new IReadOnlyList<Prize>[4];
    pools[1] = new[]
    {
        new Prize("tier1-first", 11, 500),
        new Prize("tier1-second", 12, 999)
    };
    pools[2] = new[] { new Prize("tier2", 21, 999) };
    pools[3] = new[] { new Prize("tier3", 31, 999) };

    VerifySelected(pools, 1, 0, "tier1-first", 11, "tier 1 and roll 0");
    VerifySelected(pools, 2, 0, "tier2", 21, "tier 2");
    VerifySelected(pools, 3, 999, "tier3", 31, "tier 3 and roll 999");
    VerifySelected(pools, 1, 500, "tier1-first", 11,
        "first inclusive cumulative threshold");
    VerifySelected(pools, 1, 501, "tier1-second", 12,
        "next cumulative threshold");

    var randomCalls = 0;
    var manager = CreateManager(pools, _ =>
    {
        randomCalls++;
        return 0;
    });
    foreach (var invalidTier in new[] { int.MinValue, -1, 0, 4, int.MaxValue })
        Assert(!manager.TrySelect(invalidTier, out _),
            $"invalid tier {invalidTier} was accepted");
    Equal(0, randomCalls, "invalid tiers must not consume random values");

    var emptyPools = new IReadOnlyList<Prize>[4];
    emptyPools[1] = Array.Empty<Prize>();
    emptyPools[2] = null;
    emptyPools[3] = new[] { new Prize("unused", 1, 999) };
    manager = CreateManager(emptyPools, _ =>
    {
        randomCalls++;
        return 0;
    });
    Assert(!manager.TrySelect(1, out _), "empty tier pool did not fail closed");
    Assert(!manager.TrySelect(2, out _), "null tier pool did not fail closed");
    Equal(0, randomCalls, "empty pools must not consume random values");
}

static void VerifySelected(
    IReadOnlyList<NativeSecHeroPracticePrizeManager.Prize>[] pools,
    int tier, int roll, string expectedKind, int expectedAmount, string scenario)
{
    var randomCalls = 0;
    var randomBound = -1;
    var manager = CreateManager(pools, bound =>
    {
        randomCalls++;
        randomBound = bound;
        return roll;
    });

    Assert(manager.TrySelect(tier, out var prize), scenario + " was not selected");
    Equal(1, randomCalls, scenario + " random call count");
    Equal(1000, randomBound, scenario + " random exclusive upper bound");
    Equal(expectedKind, prize.Kind, scenario + " kind");
    Equal(expectedAmount, prize.Amount, scenario + " amount");
}

static NativeSecHeroPracticePrizeManager CreateManager(
    IReadOnlyList<NativeSecHeroPracticePrizeManager.Prize>[] pools,
    Func<int, int> random)
{
    var constructor = typeof(NativeSecHeroPracticePrizeManager).GetConstructor(
        BindingFlags.Instance | BindingFlags.NonPublic,
        binder: null,
        new[]
        {
            typeof(IReadOnlyList<NativeSecHeroPracticePrizeManager.Prize>[]),
            typeof(Func<int, int>)
        },
        modifiers: null);
    Assert(constructor != null,
        "NativeSecHeroPracticePrizeManager internal constructor was not found");
    return (NativeSecHeroPracticePrizeManager)constructor.Invoke(
        new object[] { pools, random });
}

static int ReadConstant(string name)
{
    var field = typeof(Grobal2).GetField(name,
        BindingFlags.Public | BindingFlags.Static);
    Assert(field != null && field.IsLiteral,
        $"Grobal2.{name} public constant was not found");
    return (int)field.GetRawConstantValue()!;
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

static string FindRepositoryRoot() => AuditRepoRoot.Resolve();

static void RequireMatches(string source, string pattern, int expected, string message)
{
    var actual = Regex.Matches(source, pattern,
        RegexOptions.CultureInvariant | RegexOptions.Singleline).Count;
    Equal(expected, actual, message);
}

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{message}: expected={expected} actual={actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
