using System.Collections;
using System.Text;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

try
{
    PrepareRuntimeConfig();
    PrepareRuntime();
    PrepareChallengeConfig();
    TPlayObject.InitializeNativeMagicTowerChallengeCatalog(
        M2Share.sRootPath);

    CheckTierBoundaries();
    CheckStartupSnapshotDoesNotDrift();
    CheckConfigExceptionRecovery();
    CheckExactSelectionAndDialog();
    CheckExistingStateIsDisplayOnly();
    CheckJobAndTierTables();
    CheckFailedOrdinaryStillSelectsHidden();
    CheckInvalidJobDoesNotDraw();
    CheckBridgeContract();
    CheckSourceContract();

    Console.WriteLine(
        "PASS NativeMagicTowerCheckMonAndItemCheck " +
        "abi=procedure(player) phase=2-select/other-display " +
        "config=Share/class4+bigitem+self100/GBK " +
        "tier=21/29/34/39/default4 " +
        "rng=monster/ordinary/server/personal threshold=roll<=value " +
        "state=D10/D14/D1C/D18/phase3 dialog=exact " +
        "bridge=strict-explicit-player/procedure-Nil/function-closed " +
        "isolation=no-D2B/no-map-scan/no-crossbow-token");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        "NativeMagicTowerCheckMonAndItemCheck FAIL: " + exception);
    return 1;
}

static void CheckConfigExceptionRecovery()
{
    var rootPath = M2Share.sRootPath;
    try
    {
        TPlayObject.InitializeNativeMagicTowerChallengeCatalog("\0");
        var context = NewContext(job: 0, level: 21, phase: 2,
            route: 0, hundredth: false);
        context.Player.CheckNativeMagicTowerMonAndItem(context.Npc,
            new SequenceRandom(0, 0).Next);

        Equal((byte)2, context.Player.m_btNativeMagicTowerPhase,
            "invalid config path committed phase3");
    }
    finally
    {
        TPlayObject.InitializeNativeMagicTowerChallengeCatalog(rootPath);
    }
}

static void CheckStartupSnapshotDoesNotDrift()
{
    var rootPath = M2Share.sRootPath;
    try
    {
        M2Share.sRootPath = "\0";
        var context = NewContext(job: 0, level: 21, phase: 2,
            route: 0, hundredth: false);
        context.Player.CheckNativeMagicTowerMonAndItem(context.Npc,
            new SequenceRandom(0, 0).Next);

        Equal((byte)3, context.Player.m_btNativeMagicTowerPhase,
            "runtime root change replaced startup snapshot");
        Equal("J0T0M0", context.Player.m_sNativeMagicTowerChallengeMonsters,
            "runtime root change changed monster snapshot");
    }
    finally
    {
        M2Share.sRootPath = rootPath;
    }
}

static void CheckTierBoundaries()
{
    foreach (var sample in new (ushort Level, int Tier)[]
             {
                 (0, 0), (21, 0), (22, 1), (29, 1), (30, 2),
                 (34, 2), (35, 3), (39, 3), (40, 4),
                 (ushort.MaxValue, 4)
             })
        Equal(sample.Tier,
            TPlayObject.GetNativeMagicTowerChallengeTier(sample.Level),
            "tier " + sample.Level);
}

static void CheckExactSelectionAndDialog()
{
    var context = NewContext(job: 0, level: 21, phase: 2,
        route: 2, hundredth: true);
    context.Player.m_btNativeMagicTowerDefeatedMonsterCount = 77;
    context.Player.m_btNativeMagicTowerEngageChance = 1;
    context.Player.m_sbNativeMagicTowerArcherCount = 4;
    context.Player.m_ItemList.Add(new TUserItem { MakeIndex = 123 });

    var random = new SequenceRandom(1, 20, 50, 99);
    context.Player.CheckNativeMagicTowerMonAndItem(context.Npc,
        random.Next);

    Equal(new[] { 2, 100, 100, 100 }, random.Ranges.ToArray(),
        "random order/ranges");
    Equal((byte)3, context.Player.m_btNativeMagicTowerPhase, "phase");
    Equal("J0T0M1", context.Player.m_sNativeMagicTowerChallengeMonsters,
        "monster descriptor");
    Equal("J0T0P0", context.Player.m_sNativeMagicTowerPrimaryPrize,
        "ordinary inclusive threshold");
    Equal("S2P0", context.Player.m_sNativeMagicTowerServerPrize,
        "server inclusive threshold");
    Equal("SELF1", context.Player.m_sNativeMagicTowerPersonalPrize,
        "personal prize");

    Equal((byte)77,
        context.Player.m_btNativeMagicTowerDefeatedMonsterCount,
        "D2B changed");
    Equal((byte)1, context.Player.m_btNativeMagicTowerEngageChance,
        "engage chance changed");
    Equal((sbyte)4, context.Player.m_sbNativeMagicTowerArcherCount,
        "archer count changed");
    Equal(1, context.Player.m_ItemList.Count, "bag/token changed");

    Equal("tower-npc/" +
          "这次你能获得怪物给你带来的：<J0T0P0/c=red>" +
          "\\并且你还将获得你的隐藏宝物：<SELF1/c=red>" +
          "\\同时你还将获得服务器的隐藏宝物：<S2P0/c=red>" +
          "\\当然你必须消灭里面所有的：<J0T0M1/c=red>" +
          "\\如果您觉得本关难度太高，或对宝藏不满意，" +
          "\\给我一张灵符，我就直接送您去下一关\\ \\" +
          "|{cmd}<使用灵符进入下一关/@JinRuTong>         " +
          "|<接受挑战/@recmon> ",
        MerchantDialog(context.Player), "exact dialog");
}

static void CheckExistingStateIsDisplayOnly()
{
    var context = NewContext(job: 1, level: 29, phase: 3,
        route: 0, hundredth: false);
    context.Player.m_sNativeMagicTowerChallengeMonsters = "OLD-MON";
    context.Player.m_sNativeMagicTowerPrimaryPrize = "OLD-PRIZE";
    context.Player.m_sNativeMagicTowerPersonalPrize = "OLD-SELF";
    context.Player.m_sNativeMagicTowerServerPrize = "OLD-SERVER";

    context.Player.CheckNativeMagicTowerMonAndItem(context.Npc,
        _ => throw new InvalidOperationException(
            "non-phase2 consumed random"));

    Equal((byte)3, context.Player.m_btNativeMagicTowerPhase,
        "display-only phase");
    Equal("OLD-MON", context.Player.m_sNativeMagicTowerChallengeMonsters,
        "display-only monster");
    var dialog = MerchantDialog(context.Player);
    Assert(dialog.Contains("<OLD-PRIZE/c=red>",
        StringComparison.Ordinal), "display-only primary missing");
    Assert(!dialog.Contains("OLD-SELF", StringComparison.Ordinal) &&
           !dialog.Contains("OLD-SERVER", StringComparison.Ordinal),
        "disabled hidden fields displayed");
}

static void CheckJobAndTierTables()
{
    var levels = new ushort[] { 21, 22, 30, 35, 40 };
    for (byte job = 0; job < 4; job++)
    for (var tier = 0; tier < levels.Length; tier++)
    {
        var context = NewContext(job, levels[tier], 2, 0, false);
        context.Player.CheckNativeMagicTowerMonAndItem(context.Npc,
            new SequenceRandom(0, 20).Next);
        Equal("J" + job + "T" + tier + "M0",
            context.Player.m_sNativeMagicTowerChallengeMonsters,
            "job/tier monster " + job + "/" + tier);
        Equal("J" + job + "T" + tier + "P0",
            context.Player.m_sNativeMagicTowerPrimaryPrize,
            "job/tier prize " + job + "/" + tier);
        Equal((byte)3, context.Player.m_btNativeMagicTowerPhase,
            "job/tier phase " + job + "/" + tier);
    }
}

static void CheckFailedOrdinaryStillSelectsHidden()
{
    var context = NewContext(job: 3, level: 40, phase: 2,
        route: 5, hundredth: true);
    context.Player.m_sNativeMagicTowerChallengeMonsters = "KEEP-MON";
    context.Player.m_sNativeMagicTowerPrimaryPrize = "KEEP-PRIZE";
    var random = new SequenceRandom(0, 99, 0, 0);

    context.Player.CheckNativeMagicTowerMonAndItem(context.Npc,
        random.Next);

    Equal((byte)2, context.Player.m_btNativeMagicTowerPhase,
        "failed ordinary changed phase");
    Equal("KEEP-MON", context.Player.m_sNativeMagicTowerChallengeMonsters,
        "failed ordinary changed monster");
    Equal("KEEP-PRIZE", context.Player.m_sNativeMagicTowerPrimaryPrize,
        "failed ordinary changed prize");
    Equal("S5P0", context.Player.m_sNativeMagicTowerServerPrize,
        "failed ordinary skipped server draw");
    Equal("SELF0", context.Player.m_sNativeMagicTowerPersonalPrize,
        "failed ordinary skipped personal draw");
    Equal(new[] { 2, 100, 100, 100 }, random.Ranges.ToArray(),
        "failed ordinary random order");
}

static void CheckInvalidJobDoesNotDraw()
{
    var context = NewContext(job: 4, level: 40, phase: 2,
        route: 1, hundredth: true);
    context.Player.CheckNativeMagicTowerMonAndItem(context.Npc,
        _ => throw new InvalidOperationException("invalid job drew random"));
    Equal((byte)2, context.Player.m_btNativeMagicTowerPhase,
        "invalid job phase");
    Equal(string.Empty,
        context.Player.m_sNativeMagicTowerChallengeMonsters,
        "invalid job monster");
}

static void CheckBridgeContract()
{
    var contextPlayer = NewContext(job: 0, level: 21, phase: 3,
        route: 0, hundredth: false).Player;
    contextPlayer.m_sCharName = "context-player";
    var explicitPlayer = NewContext(job: 1, level: 29, phase: 3,
        route: 0, hundredth: false).Player;
    explicitPlayer.m_sCharName = "explicit-player";
    explicitPlayer.m_sNativeMagicTowerChallengeMonsters = "EXPLICIT-MON";
    explicitPlayer.m_sNativeMagicTowerPrimaryPrize = "EXPLICIT-PRIZE";
    var npc = new NormNpc
    {
        m_sCharName = "bridge-npc",
        m_sMapName = "tower-map"
    };
    var bridge = new PasApiBridge
    {
        CurrentPlayer = contextPlayer,
        CurrentNpc = npc
    };
    var validArgs = new List<PasValue>
    {
        PasValue.FromObject(explicitPlayer)
    };

    Assert(bridge.CallNpcMethod("ChkMonAndItem", validArgs,
        out var procedureResult), "valid procedure ABI rejected");
    Equal(PasValueType.Nil, procedureResult.Type,
        "procedure result type");
    Equal(0, contextPlayer.m_MsgList.Count,
        "CurrentPlayer received explicit player's dialog");
    var dialog = MerchantDialog(explicitPlayer);
    Assert(dialog.StartsWith("bridge-npc/", StringComparison.Ordinal) &&
           dialog.Contains("<EXPLICIT-MON/c=red>",
               StringComparison.Ordinal),
        "explicit player did not receive its own state");

    var explicitMessageCount = explicitPlayer.m_MsgList.Count;
    foreach (var malformed in new[]
             {
                 new List<PasValue>(),
                 new List<PasValue>
                 {
                     PasValue.FromObject(explicitPlayer),
                     PasValue.FromInt(1)
                 },
                 new List<PasValue> { PasValue.FromInt(1) },
                 new List<PasValue> { PasValue.Nil }
             })
    {
        Assert(!bridge.CallNpcMethod("ChkMonAndItem", malformed, out _),
            "malformed procedure ABI accepted");
        Equal(explicitMessageCount, explicitPlayer.m_MsgList.Count,
            "malformed call changed explicit player");
        Equal(0, contextPlayer.m_MsgList.Count,
            "malformed call changed CurrentPlayer");
    }

    Assert(!bridge.CallNpcFunc("ChkMonAndItem", validArgs,
            out var functionResult),
        "procedure was exposed through function dispatcher");
    Equal(PasValueType.Nil, functionResult.Type,
        "rejected function result type");
    Equal(explicitMessageCount, explicitPlayer.m_MsgList.Count,
        "function dispatcher changed explicit player");
    Equal(0, contextPlayer.m_MsgList.Count,
        "function dispatcher changed CurrentPlayer");
}

static void CheckSourceContract()
{
    var source = File.ReadAllText(FindSource());
    foreach (var forbidden in new[]
             {
                 "m_btNativeMagicTowerDefeatedMonsterCount", "弩牌",
                 "m_PEnvir", "GetMapMonster", "MonCount"
             })
        Assert(!source.Contains(forbidden, StringComparison.Ordinal),
            "source contains forbidden dependency " + forbidden);

    foreach (var required in new[]
             {
                 "m_btNativeMagicTowerPhase == 2", "warr.ini",
                 "fashi.ini", "taos.ini", "assassin.ini", "bigitem.ini",
                 "self100.ini", "怪物", "爆物", "roll >",
                 "m_btNativeMagicTowerPhase = 3"
             })
        Assert(source.Contains(required, StringComparison.Ordinal),
            "source missing contract " + required);
}

static Context NewContext(byte job, ushort level, byte phase, byte route,
    bool hundredth)
{
    var player = new TPlayObject
    {
        m_boOffLineFlag = true,
        m_sCharName = "player",
        m_btJob = job,
        m_btNativeMagicTowerPhase = phase,
        m_btNativeMagicTowerSpecialRoute = route,
        m_boNativeMagicTowerHundredth = hundredth
    };
    player.m_Abil.Level = level;
    var npc = new NormNpc
    {
        m_sCharName = "tower-npc",
        m_sMapName = "tower-map"
    };
    return new Context(player, npc);
}

static string MerchantDialog(TPlayObject player)
{
    Equal(1, player.m_MsgList.Count, "merchant message count");
    Equal(Grobal2.RM_MERCHANTSAY, player.m_MsgList[0].wIdent,
        "merchant ident");
    return player.m_MsgList[0].Buff;
}

static void PrepareChallengeConfig()
{
    var share = Path.Combine(M2Share.sRootPath, "Share");
    Directory.CreateDirectory(share);
    var files = new[] { "warr.ini", "fashi.ini", "taos.ini", "assassin.ini" };
    for (var job = 0; job < files.Length; job++)
    {
        var text = new StringBuilder();
        for (var tier = 0; tier < 5; tier++)
        {
            text.Append("[配置").Append(tier + 1).AppendLine("]");
            text.Append("怪物0=J").Append(job).Append('T').Append(tier)
                .AppendLine("M0");
            text.Append("怪物2=J").Append(job).Append('T').Append(tier)
                .AppendLine("M1");
            text.Append("爆物1=J").Append(job).Append('T').Append(tier)
                .AppendLine("P0/20");
            var finalThreshold = job == 3 && tier == 4 ? 40 : 99;
            text.Append("爆物3=J").Append(job).Append('T').Append(tier)
                .Append("P1/").Append(finalThreshold).AppendLine();
        }
        File.WriteAllText(Path.Combine(share, files[job]), text.ToString(),
            HUtil32.GbkEncoding);
    }

    var big = new StringBuilder();
    for (var route = 1; route <= 5; route++)
    {
        big.Append("[配置").Append(route).AppendLine("]");
        big.Append("爆物1=S").Append(route).AppendLine("P0/50");
        big.Append("爆物2=S").Append(route).AppendLine("P1/99");
    }
    File.WriteAllText(Path.Combine(share, "bigitem.ini"), big.ToString(),
        HUtil32.GbkEncoding);
    File.WriteAllText(Path.Combine(share, "self100.ini"),
        "[配置]\r\n爆物1=SELF0/50\r\n爆物2=SELF1/99\r\n",
        HUtil32.GbkEncoding);
}

static string FindSource()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory != null)
    {
        var path = Path.Combine(directory.FullName, "GameSvr", "Players",
            "TPlayObject.NativeMagicTower.Check.cs");
        if (File.Exists(path)) return path;
        directory = directory.Parent;
    }
    throw new FileNotFoundException("could not locate Check.cs");
}

static void PrepareRuntime()
{
    M2Share.g_Config = new GameSvrConfig { sBaseDir = "Share" };
    M2Share.sRootPath = Path.Combine(AppContext.BaseDirectory,
        "NativeMagicTowerCheckRoot");
    M2Share.UserEngine = new UserEngine();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.MapManager = new MapManager();
    M2Share.RandomNumber = RandomNumber.GetInstance();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new ArrayList();
    M2Share.g_MonSayMsgList =
        new Dictionary<string, IList<TMonSayMsg>>();
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

static void Equal<T>(T expected, T actual, string message)
{
    if (expected is IEnumerable<int> expectedSequence &&
        actual is IEnumerable<int> actualSequence)
    {
        if (expectedSequence.SequenceEqual(actualSequence)) return;
    }
    else if (EqualityComparer<T>.Default.Equals(expected, actual))
    {
        return;
    }
    throw new InvalidOperationException(
        $"{message}: expected={Format(expected)} actual={Format(actual)}");
}

static string Format<T>(T value) => value is IEnumerable<int> sequence
    ? string.Join(',', sequence)
    : value?.ToString() ?? "<null>";

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

readonly record struct Context(TPlayObject Player, NormNpc Npc);

sealed class SequenceRandom
{
    private readonly Queue<int> _values;
    internal SequenceRandom(params int[] values) =>
        _values = new Queue<int>(values);
    internal List<int> Ranges { get; } = new();
    internal int Next(int range)
    {
        Ranges.Add(range);
        if (_values.Count == 0)
            throw new InvalidOperationException("random sequence exhausted");
        var value = _values.Dequeue();
        if (range > 0 && (value < 0 || value >= range))
            throw new InvalidOperationException(
                $"random value {value} outside range {range}");
        return value;
    }
}
