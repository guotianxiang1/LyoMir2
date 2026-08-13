using System.Collections;
using System.Reflection;
using System.Text;
using GameSvr;
using SystemModule;

try
{
    PrepareRuntimeConfig();
    PrepareRuntime();
    PrepareTianMon();
    NormNpc.InitializeNativeMagicTowerMonsterCatalog(M2Share.sRootPath,
        M2Share.g_Config.sBaseDir);

    CheckCoordinates();
    CheckPropertyAndCloseGates();
    CheckCountAndOccupiedGates();
    CheckFactoryAndExactPositionFailures();
    CheckSuccessAndRace99();
    CheckPhase2Controller();
    CheckStartupSnapshotDoesNotDrift();
    CheckConfigPathFailureDoesNotCommit();
    CheckConcurrentSingleCommit();

    Console.WriteLine(
        "PASS NativeMagicTowerEngageArcherCheck " +
        "gate=property12/index1..10/pending/sbyte-count/slot " +
        "spawn=Race99/exact-environment/exact-coordinate/no-104 " +
        "atomic=failure-no-state/concurrent-single-commit " +
        "phase2=TianMon-split/50-sequence/tick-2s/mission35/finish50 " +
        "order=owner/chance/slot/count/controller/10127/FCFF");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        "NativeMagicTowerEngageArcherCheck FAIL: " + exception);
    return 1;
}

static void CheckConfigPathFailureDoesNotCommit()
{
    var rootPath = M2Share.sRootPath;
    var baseDir = M2Share.g_Config.sBaseDir;
    try
    {
        NormNpc.InitializeNativeMagicTowerMonsterCatalog("\0", baseDir);
        var context = NewContext(phase: 2);
        context.Npc.StartNativeMagicTowerChallenge(context.Player);

        Equal((byte)2, Phase(context.Player),
            "invalid TianMon path committed phase3");
        Assert(!context.Npc.NativeMagicTowerChallengeActive,
            "invalid TianMon path activated controller");
    }
    finally
    {
        NormNpc.InitializeNativeMagicTowerMonsterCatalog(rootPath,
            baseDir);
    }
}

static void CheckStartupSnapshotDoesNotDrift()
{
    var rootPath = M2Share.sRootPath;
    try
    {
        M2Share.sRootPath = "\0";
        var context = NewContext(phase: 2);
        context.Npc.StartNativeMagicTowerChallenge(context.Player);

        Equal((byte)3, Phase(context.Player),
            "runtime root change replaced TianMon snapshot");
        Assert(context.Npc.NativeMagicTowerChallengeActive,
            "runtime root change disabled TianMon snapshot");
        Equal(19, context.Npc.NativeMagicTowerChallenge.Monsters.Count,
            "runtime root change changed TianMon snapshot");
    }
    finally
    {
        M2Share.sRootPath = rootPath;
    }
}

static void CheckCoordinates()
{
    var expected = new (short X, short Y)[]
    {
        (30, 30), (27, 33), (29, 37), (31, 41), (34, 44),
        (38, 46), (41, 49), (45, 51), (48, 47), (51, 43)
    };
    for (var index = 1; index <= expected.Length; index++)
    {
        Assert(TPlayObject.TryGetNativeMagicTowerArcherCoordinates(index,
            out var x, out var y), "coordinate rejected " + index);
        Equal(expected[index - 1], (x, y), "coordinate " + index);
    }
    foreach (var index in new[] { int.MinValue, -1, 0, 11, int.MaxValue })
        Assert(!TPlayObject.TryGetNativeMagicTowerArcherCoordinates(index,
            out _, out _), "invalid coordinate accepted " + index);
}

static void CheckPropertyAndCloseGates()
{
    SetArcherDefinition();
    var disabled = NewContext(addProperty: false, chance: 1);
    var before = Snapshot(disabled.Player);
    Engage(disabled, 1);
    Equal(before, Snapshot(disabled.Player), "property state");
    Equal(0, disabled.Player.m_MsgList.Count, "property message");
    Equal(0, disabled.Map.MonCount, "property spawn");

    foreach (var index in new[] { int.MinValue, -1, 0, 11, int.MaxValue })
    {
        var invalid = NewContext(chance: 1);
        Engage(invalid, index);
        AssertCloseOnly(invalid.Player, "invalid index " + index);
        Equal((byte)1, Chance(invalid.Player), "invalid chance");
        Equal(0, invalid.Map.MonCount, "invalid spawn");
    }

    var noChance = NewContext(chance: 0);
    Engage(noChance, 1);
    AssertCloseOnly(noChance.Player, "no chance");
    Equal(0, noChance.Map.MonCount, "no chance spawn");
}

static void CheckCountAndOccupiedGates()
{
    SetArcherDefinition();
    var limit = NewContext(chance: 1, count: 10);
    Engage(limit, 1);
    AssertMerchantOnly(limit.Player,
        "tower-npc/你已经拥有了10个弓箭手，不能继续。", "limit");
    Equal((byte)1, Chance(limit.Player), "limit chance");

    var occupied = NewContext(chance: 1);
    Slots(occupied.Player)[2] = 1;
    Engage(occupied, 3);
    AssertMerchantOnly(occupied.Player,
        "tower-npc/该位置已有弓箭手，请重新选择。", "occupied");
    Equal((byte)1, Chance(occupied.Player), "occupied chance");

    var signed = NewContext(chance: 1, count: -1);
    Engage(signed, 2);
    Equal((sbyte)0, Count(signed.Player), "signed increment");
    Equal(1, signed.Map.MonCount, "signed spawn");
}

static void CheckFactoryAndExactPositionFailures()
{
    M2Share.UserEngine.MonsterList.Clear();
    var missingDefinition = NewContext(chance: 1);
    var before = Snapshot(missingDefinition.Player);
    Engage(missingDefinition, 1);
    Equal(before, Snapshot(missingDefinition.Player),
        "missing definition state");
    Equal(0, missingDefinition.Player.m_MsgList.Count,
        "missing definition message");
    Equal(0, missingDefinition.Map.MonCount,
        "missing definition spawn");

    SetArcherDefinition();
    var blocked = NewContext(chance: 1);
    blocked.Map.SetMapXYFlag(30, 30, false);
    before = Snapshot(blocked.Player);
    Engage(blocked, 1);
    Equal(before, Snapshot(blocked.Player), "blocked state");
    Equal(0, blocked.Player.m_MsgList.Count, "blocked message");
    Equal(0, blocked.Map.MonCount, "blocked actor count");
    Assert(blocked.Map.GetMovingObject(30, 30, true) == null,
        "blocked coordinate received an actor");
}

static void CheckSuccessAndRace99()
{
    SetArcherDefinition();
    var context = NewContext(chance: 1, phase: 1);
    Engage(context, 10);

    var actor = context.Map.GetMovingObject(51, 43, true) as TBaseObject;
    Assert(actor != null, "Race99 actor missing");
    // Race 99 is TSkyArcher: factory sub_679F8C jt[28] = 0x67A63F loads classref
    // 0x67F21C and calls ctor 0x681958, which writes
    //   681976  C6 86 78 01 00 00 63   mov byte [esi+0x178],0x63  ; race = 99
    //   68197D  C7 46 78 07 00 00 00   mov dword [esi+0x78],7     ; view range = 7
    // AddBaseObject reaches it through TryCreateRaceA, which runs before the big
    // switch, so the unevidenced MagicTowerArcherMonster arm never executes.
    Assert(actor is SkyArcher,
        "Race99 did not use the byte-verified TSkyArcher (ctor 0x681958)");
    Assert(actor is not ArcherMonster, "Race99 reused Race104 class");
    Equal((byte)99, actor.m_btRaceServer, "Race99 value");
    Equal(7, actor.m_nViewRange, "Race99 view range");
    // VMT slot +0x20 override sub_6819B0: `cmp al,0x32 / jb -> false`,
    // `cmp al,0x63 / jne -> true`, i.e. race in [50,255] and != 99.
    // (The ctor's `mov byte [esi+0x3AC],1` has no proven C# field — SkyArcher.cs
    // and ShadowHero.cs both register +0x3AC as unmapped — so it is not asserted.)
    Assert(!actor.IsAttackTarget(NewRaceProbe(0x31)), "0x6819BA jb race<50");
    Assert(actor.IsAttackTarget(NewRaceProbe(0x32)), "0x6819B8 race>=50");
    Assert(!actor.IsAttackTarget(NewRaceProbe(0x63)), "0x6819BE race==99 excluded");
    Assert(ReferenceEquals(context.Player, actor.m_Master), "owner");
    Assert(ReferenceEquals(context.Map, actor.m_PEnvir), "environment");
    Equal((short)51, actor.m_nCurrX, "success X");
    Equal((short)43, actor.m_nCurrY, "success Y");
    Equal((byte)0, Chance(context.Player), "success chance");
    Equal((byte)1, Slots(context.Player)[9], "success slot");
    Equal((sbyte)1, Count(context.Player), "success count");
    Equal((byte)1, Phase(context.Player), "non-phase2 changed phase");
    Assert(!context.Npc.NativeMagicTowerChallengeActive,
        "non-phase2 started controller");
    AssertSuccessMessages(context.Player);
}

static void CheckPhase2Controller()
{
    SetChallengeDefinitions();
    var context = NewContext(chance: 1, phase: 2);
    Engage(context, 4);

    Equal((byte)3, Phase(context.Player), "phase2 phase");
    Assert(context.Npc.NativeMagicTowerChallengeActive,
        "phase2 controller inactive");
    var state = context.Npc.NativeMagicTowerChallenge;
    Assert(ReferenceEquals(context.Player, state.Player),
        "controller player");
    Assert(ReferenceEquals(context.Map, state.Environment),
        "controller environment");
    Equal(0, state.Position, "controller position");
    Equal(19, state.Monsters.Count, "TianMon count");
    Equal(15, state.Split, "TianMon split");
    Equal(50, state.Sequence.Length, "sequence length");
    for (var index = 0; index <= 9; index++)
        Equal((byte)index, state.Sequence[index], "fixed sequence " + index);
    for (var index = 10; index <= 34; index++)
        Assert(state.Sequence[index] is >= 10 and <= 14,
            "first pool range " + index + "=" + state.Sequence[index]);
    for (var index = 35; index <= 49; index++)
        Assert(state.Sequence[index] is >= 16 and <= 18,
            "second pool range " + index + "=" + state.Sequence[index]);
    AssertSuccessMessages(context.Player);

    state.Tick = unchecked(HUtil32.GetTickCount() - 2000);
    var gatedTick = state.Tick;
    context.Player.m_boGhost = true;
    context.Npc.Run();
    Equal(0, state.Position, "ghost gate position");
    Equal(gatedTick, state.Tick, "ghost gate tick");
    context.Player.m_boGhost = false;

    var capturedMap = context.Player.m_PEnvir;
    context.Player.m_PEnvir = NewMap();
    context.Npc.Run();
    Equal(0, state.Position, "environment gate position");
    Equal(gatedTick, state.Tick, "environment gate tick");
    context.Player.m_PEnvir = capturedMap;

    for (var position = 0; position < 50; position++)
    {
        state.Tick = unchecked(HUtil32.GetTickCount() - 2000);
        context.Npc.Run();
        Equal(position + 1, state.Position,
            "tick position " + position);
    }

    var supplied = MovingActorsAt(context.Map, 29, 19);
    Equal(50, supplied.Count, "supplied monster count");
    Equal(35, supplied.Count(monster => monster.m_boMission),
        "mission monster count");
    Equal(15, supplied.Count(monster => !monster.m_boMission),
        "ordinary monster count");
    Assert(!context.Npc.NativeMagicTowerChallengeActive,
        "controller remained active at 50");
    Equal((byte)3, Phase(context.Player), "tick changed phase");
    Assert(ReferenceEquals(state, context.Npc.NativeMagicTowerChallenge),
        "tick released controller state");

    var countAtEnd = context.Map.MonCount;
    state.Tick = unchecked(HUtil32.GetTickCount() - 2000);
    context.Npc.Run();
    Equal(countAtEnd, context.Map.MonCount, "inactive controller spawned");
}

static void CheckConcurrentSingleCommit()
{
    SetArcherDefinition();
    var context = NewContext(chance: 1);
    Parallel.For(0, 20, attempt => Engage(context, attempt % 10 + 1,
        clearMessages: false));

    Equal((byte)0, Chance(context.Player), "concurrent chance");
    Equal((sbyte)1, Count(context.Player), "concurrent count");
    Equal(1, Slots(context.Player).Count(slot => slot != 0),
        "concurrent slots");
    Equal(1, context.Map.MonCount, "concurrent actors");
    Equal(1, context.Player.m_MsgList.Count(message =>
        message.wIdent == Grobal2.RM_SYSMESSAGE),
        "concurrent success messages");
    Equal(20, context.Player.m_MsgList.Count(message =>
        message.wIdent == Grobal2.RM_MERCHANTDLGCLOSE),
        "concurrent close messages");
}

static Context NewContext(bool addProperty = true, byte chance = 0,
    sbyte count = 0, byte phase = 0)
{
    var map = NewMap();
    var player = new TPlayObject
    {
        m_boOffLineFlag = true,
        m_sCharName = "player",
        m_sMapName = map.sMapName,
        m_PEnvir = map,
        m_nCurrX = 10,
        m_nCurrY = 20
    };
    var npc = new NormNpc
    {
        m_sCharName = "tower-npc",
        m_sMapName = "npc-map"
    };
    if (addProperty) npc.AddNativePasProperty(12);
    SetField(player, "m_btNativeMagicTowerEngageChance", chance);
    SetField(player, "m_sbNativeMagicTowerArcherCount", count);
    SetField(player, "m_btNativeMagicTowerPhase", phase);
    return new Context(player, npc, map);
}

static void Engage(Context context, int index, bool clearMessages = true)
{
    if (clearMessages) context.Player.m_MsgList.Clear();
    context.Player.EngageNativeMagicTowerArcher(context.Npc, index);
}

static Envirnoment NewMap()
{
    var map = new Envirnoment { sMapName = "tower-map" };
    typeof(Envirnoment).GetMethod("Initialize",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(map, new object[] { (short)64, (short)64 });
    return map;
}

static void SetArcherDefinition()
{
    M2Share.UserEngine.MonsterList.Clear();
    M2Share.UserEngine.MonsterList.Add(new TMonInfo
    {
        ItemList = new List<TMonItem>(),
        sName = TPlayObject.NativeMagicTowerArcherName,
        btRace = TPlayObject.NativeMagicTowerArcherRace,
        wLevel = 1,
        wHP = 100,
        wWalkSpeed = 1000,
        wWalkStep = 1,
        wWalkWait = 1000,
        wAttackSpeed = 1000
    });
}

static void SetChallengeDefinitions()
{
    SetArcherDefinition();
    foreach (var prefixAndCount in new[]
             {
                 (Prefix: "low", Count: 12),
                 (Prefix: "middle", Count: 3),
                 (Prefix: "high", Count: 4)
             })
    {
        for (var number = 1; number <= prefixAndCount.Count; number++)
        {
            M2Share.UserEngine.MonsterList.Add(new TMonInfo
            {
                ItemList = new List<TMonItem>(),
                sName = prefixAndCount.Prefix + number,
                btRace = (byte)M2Share.MONSTER_OMA,
                wLevel = 1,
                wHP = 100,
                wWalkSpeed = 1000,
                wWalkStep = 1,
                wWalkWait = 1000,
                wAttackSpeed = 1000
            });
        }
    }
}

static List<TBaseObject> MovingActorsAt(Envirnoment map, int x, int y)
{
    var found = false;
    var cell = map.GetMapCellInfo(x, y, ref found);
    if (!found || cell.ObjList == null) return new List<TBaseObject>();
    return cell.ObjList.Where(item =>
            item.CellType == CellType.OS_MOVINGOBJECT)
        .Select(item => item.CellObj as TBaseObject)
        .Where(actor => actor != null)
        .ToList();
}

static TBaseObject NewRaceProbe(byte race) =>
    new Monster { m_btRaceServer = race };

static void AssertCloseOnly(TPlayObject player, string name)
{
    Equal(1, player.m_MsgList.Count, name + " message count");
    Equal(Grobal2.RM_MERCHANTDLGCLOSE, player.m_MsgList[0].wIdent,
        name + " ident");
}

static void AssertMerchantOnly(TPlayObject player, string text, string name)
{
    Equal(1, player.m_MsgList.Count, name + " message count");
    Equal(Grobal2.RM_MERCHANTSAY, player.m_MsgList[0].wIdent,
        name + " ident");
    Equal(text, player.m_MsgList[0].Buff, name + " text");
}

static void AssertSuccessMessages(TPlayObject player)
{
    Equal(2, player.m_MsgList.Count, "success message count");
    Equal(Grobal2.RM_MERCHANTDLGCLOSE, player.m_MsgList[0].wIdent,
        "success close order");
    Equal(Grobal2.RM_SYSMESSAGE, player.m_MsgList[1].wIdent,
        "success system order");
    Equal(0xFF, player.m_MsgList[1].nParam1,
        "success foreground");
    Equal(0xFC, player.m_MsgList[1].nParam2,
        "success background");
    Equal(TPlayObject.NativeMagicTowerArcherReadyMessage,
        player.m_MsgList[1].Buff, "success text");
}

static byte[] Slots(TPlayObject player) =>
    ReadField<byte[]>(player, "m_btNativeMagicTowerArcherSlots");
static byte Chance(TPlayObject player) =>
    ReadField<byte>(player, "m_btNativeMagicTowerEngageChance");
static sbyte Count(TPlayObject player) =>
    ReadField<sbyte>(player, "m_sbNativeMagicTowerArcherCount");
static byte Phase(TPlayObject player) =>
    ReadField<byte>(player, "m_btNativeMagicTowerPhase");

static PlayerSnapshot Snapshot(TPlayObject player) => new(
    Chance(player), Count(player), Slots(player).ToArray());

static T ReadField<T>(object target, string name)
{
    var field = target.GetType().GetField(name,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    Assert(field != null, "missing field " + name);
    return (T)field.GetValue(target)!;
}

static void SetField<T>(object target, string name, T value)
{
    var field = target.GetType().GetField(name,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    Assert(field != null, "missing field " + name);
    field.SetValue(target, value);
}

static void PrepareTianMon()
{
    var directory = Path.Combine(M2Share.sRootPath,
        M2Share.g_Config.sBaseDir, "Config");
    Directory.CreateDirectory(directory);
    var text = new StringBuilder();
    AppendSection(text, "低级怪物", 12, "low");
    AppendSection(text, "中级怪物", 3, "middle");
    AppendSection(text, "高级怪物", 4, "high");
    File.WriteAllText(Path.Combine(directory, "TianMon.ini"),
        text.ToString(), HUtil32.GbkEncoding);
}

static void AppendSection(StringBuilder text, string section, int count,
    string prefix)
{
    text.Append('[').Append(section).AppendLine("]");
    for (var number = 1; number <= count; number++)
        text.Append("怪物").Append(number).Append('=').Append(prefix)
            .Append(number).AppendLine();
}

static void PrepareRuntime()
{
    M2Share.g_Config = new GameSvrConfig
    {
        nSendRefMsgRange = 12,
        sBaseDir = "Share"
    };
    M2Share.sRootPath = AppContext.BaseDirectory;
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

readonly record struct PlayerSnapshot(byte Chance, sbyte Count, byte[] Slots)
{
    public bool Equals(PlayerSnapshot other) => Chance == other.Chance &&
        Count == other.Count && Slots.SequenceEqual(other.Slots);
    public override int GetHashCode() => HashCode.Combine(Chance, Count);
}
