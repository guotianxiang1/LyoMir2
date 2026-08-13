using System.Collections;
using System.Reflection;
using GameSvr;
using SystemModule;

try
{
    PrepareRuntimeConfig();
    PrepareRuntime();

    CheckPropertyAndSlotGates();
    CheckInsufficientToken();
    CheckPostDebitMissingActor();
    CheckPostDebitWrongRace();
    CheckSuccessfulPileMove();
    CheckOrdinaryItemAndDuplicateName();
    CheckSignedCountClamp();
    CheckSourceContract();

    Console.WriteLine(
        "PASS NativeMagicTowerMoveChanceCheck abi=npc-function(player,index) " +
        "gate=property12/slot1..10 token=name-NuPai/count1/tail-first " +
        "atomic=insufficient-no-debit pile=StdMode7 ordinary=whole " +
        "ids=ClientItemID-packet/MakeIndex-log " +
        "postdebit=missing-or-wrong-race-no-refund " +
        "success=slot-clear/ghost/chance1/sbyte-count-clamp");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        $"NativeMagicTowerMoveChanceCheck FAIL: {exception}");
    return 1;
}

static void CheckPropertyAndSlotGates()
{
    SetDefinitions(new GoodItem
    {
        Name = "弩牌",
        StdMode = 7,
        Weight = 2
    });
    var disabled = NewContext(addProperty: false, slot: 1);
    var token = NewItem(11, 1, 2, clientItemId: 1011);
    disabled.Player.m_ItemList.Add(token);
    var before = Snapshot(disabled.Player, 1);
    Assert(!Move(disabled, 1), "property gate accepted");
    Equal(before, Snapshot(disabled.Player, 1),
        "property gate state changed");
    Equal(0, disabled.Player.m_MsgList.Count,
        "property gate was not silent");
    Equal(0, M2Share.LogStringList.Count, "property gate logged");

    foreach (var index in new[]
             {
                 int.MinValue, -1, 0, 2, 11, int.MaxValue
             })
    {
        var invalid = NewContext(slot: 1);
        invalid.Player.m_ItemList.Add(NewItem(12, 1, 1));
        Assert(!Move(invalid, index), $"invalid/empty slot {index} accepted");
        Equal("tower-npc/您选择的位置没有弓箭手，请重新选择。",
            MerchantMessage(invalid.Player), $"slot {index} dialog");
        Equal(1, invalid.Player.m_ItemList.Count,
            $"slot {index} consumed token");
        Equal(0, M2Share.LogStringList.Count, $"slot {index} logged");
    }
}

static void CheckInsufficientToken()
{
    SetDefinitions(
        new GoodItem { Name = "弩牌", StdMode = 7, Weight = 2 },
        new GoodItem { Name = "其他", StdMode = 7, Weight = 3 });
    var context = NewContext(slot: 3);
    var emptyPile = NewItem(21, 1, 0, clientItemId: 2021);
    var unrelated = NewItem(22, 2, 9, clientItemId: 2022);
    context.Player.m_ItemList.Add(emptyPile);
    context.Player.m_ItemList.Add(unrelated);
    var order = context.Player.m_ItemList.ToArray();

    Assert(!Move(context, 3), "insufficient token accepted");
    Assert(context.Player.m_ItemList.SequenceEqual(order),
        "insufficient token changed bag identity/order");
    Equal((ushort)0, emptyPile.Dura,
        "insufficient token changed empty pile");
    Equal("tower-npc/移动弓箭手需要1个弩牌，你没有足够的弩牌。",
        MerchantMessage(context.Player), "insufficient token dialog");
    Equal(0, M2Share.LogStringList.Count, "insufficient token logged");
    Equal(0, context.Player.m_MsgList.Count(message =>
        message.wIdent == Grobal2.RM_WEIGHTCHANGED),
        "insufficient token refreshed weight");
}

static void CheckPostDebitMissingActor()
{
    SetDefinitions(new GoodItem
    {
        Name = "弩牌",
        StdMode = 7,
        Weight = 2
    });
    var context = NewContext(slot: 1, withMap: true);
    var token = NewItem(-31, 1, 2, 100, 3031);
    context.Player.m_ItemList.Add(token);
    SetField(context.Player, "m_sbNativeMagicTowerArcherCount", (sbyte)1);

    Assert(!Move(context, 1), "missing actor accepted");
    Equal((ushort)1, token.Dura, "missing actor token was refunded");
    Equal((byte)1, Slot(context.Player, 1), "missing actor cleared slot");
    Equal((byte)0, ReadField<byte>(context.Player,
        "m_btNativeMagicTowerEngageChance"),
        "missing actor restored chance");
    Equal((sbyte)1, ReadField<sbyte>(context.Player,
        "m_sbNativeMagicTowerArcherCount"),
        "missing actor changed count");
    Equal("10\tplayer-map\t10\t20\tplayer\t弩牌\t4294967265\t1\t" +
          "tower-npc收取1个", LogAt(0), "missing actor log");
    Equal(Grobal2.SM_BAGITEMDURACHG, context.Player.m_DefMsg.Ident,
        "partial token packet ident");
    Equal(3031, context.Player.m_DefMsg.Recog,
        "partial token ClientItemID");
    Equal((ushort)1, context.Player.m_DefMsg.Param,
        "partial token Dura");
    Equal((ushort)100, context.Player.m_DefMsg.Tag,
        "partial token DuraMax");
    Equal(1, context.Player.m_MsgList.Count(message =>
        message.wIdent == Grobal2.RM_WEIGHTCHANGED),
        "missing actor weight refresh");
}

static void CheckPostDebitWrongRace()
{
    SetDefinitions(new GoodItem
    {
        Name = "弩牌",
        StdMode = 7,
        Weight = 2
    });
    var context = NewContext(slot: 2, withMap: true);
    var token = NewItem(41, 1, 1, 100, 4041);
    context.Player.m_ItemList.Add(token);
    var actor = PlaceActor(context.Map, 27, 33, 100);
    SetField(context.Player, "m_sbNativeMagicTowerArcherCount", (sbyte)1);

    Assert(!Move(context, 2), "wrong Race actor accepted");
    Equal(0, context.Player.m_ItemList.Count,
        "wrong Race actor token was refunded");
    Assert(!actor.m_boGhost, "wrong Race actor was ghosted");
    Equal((byte)1, Slot(context.Player, 2), "wrong Race actor cleared slot");
    Equal((byte)0, ReadField<byte>(context.Player,
        "m_btNativeMagicTowerEngageChance"),
        "wrong Race actor restored chance");
    Equal(Grobal2.SM_DELITEM, context.Player.m_DefMsg.Ident,
        "whole token packet ident");
    Equal(4041, context.Player.m_DefMsg.Recog,
        "whole token ClientItemID");
}

static void CheckSuccessfulPileMove()
{
    SetDefinitions(new GoodItem
    {
        Name = "弩牌",
        StdMode = 7,
        Weight = 2
    });
    var context = NewContext(slot: 10, withMap: true);
    var token = NewItem(51, 1, 3, 100, 5051);
    context.Player.m_ItemList.Add(token);
    var actor = PlaceActor(context.Map, 51, 43, 99);
    SetField(context.Player, "m_sbNativeMagicTowerArcherCount", (sbyte)1);

    Assert(Move(context, 10), "valid tower archer move rejected");
    Equal((ushort)2, token.Dura, "successful move token quantity");
    Equal((byte)0, Slot(context.Player, 10), "successful move slot");
    Equal((byte)1, ReadField<byte>(context.Player,
        "m_btNativeMagicTowerEngageChance"),
        "successful move chance");
    Equal((sbyte)0, ReadField<sbyte>(context.Player,
        "m_sbNativeMagicTowerArcherCount"),
        "successful move count");
    Assert(actor.m_boGhost, "successful move did not ghost actor");
    Assert(context.Map.GetMovingObject(51, 43, true) == null,
        "successful move retained map actor");
    Equal(5051, context.Player.m_DefMsg.Recog,
        "successful partial ClientItemID");
}

static void CheckOrdinaryItemAndDuplicateName()
{
    SetDefinitions(
        new GoodItem { Name = "弩牌", StdMode = 7, Weight = 2 },
        new GoodItem { Name = "弩牌", StdMode = 0, Weight = 3 });
    var context = NewContext(slot: 4, withMap: true);
    var keep = NewItem(61, 1, 5, clientItemId: 6061);
    var ordinaryTail = NewItem(62, 2, 999, clientItemId: 6062);
    context.Player.m_ItemList.Add(keep);
    context.Player.m_ItemList.Add(ordinaryTail);
    PlaceActor(context.Map, 31, 41, 99);
    SetField(context.Player, "m_sbNativeMagicTowerArcherCount", (sbyte)1);

    Assert(Move(context, 4), "duplicate-name ordinary token rejected");
    Assert(context.Player.m_ItemList.SequenceEqual(new[] { keep }),
        "ordinary token did not consume tail instance whole");
    Equal((ushort)5, keep.Dura, "ordinary token changed pile");
    Equal("10\tplayer-map\t10\t20\tplayer\t弩牌\t62\t1\ttower-npc",
        LogAt(0), "ordinary token log");
    Equal(Grobal2.SM_DELITEM, context.Player.m_DefMsg.Ident,
        "ordinary token packet ident");
    Equal(6062, context.Player.m_DefMsg.Recog,
        "ordinary token ClientItemID");
}

static void CheckSignedCountClamp()
{
    SetDefinitions(new GoodItem { Name = "弩牌", StdMode = 7 });
    var zero = NewContext(slot: 5, withMap: true);
    zero.Player.m_ItemList.Add(NewItem(71, 1, 1));
    PlaceActor(zero.Map, 34, 44, 99);
    SetField(zero.Player, "m_sbNativeMagicTowerArcherCount", (sbyte)0);
    Assert(Move(zero, 5), "zero-count success rejected");
    Equal((sbyte)0, ReadField<sbyte>(zero.Player,
        "m_sbNativeMagicTowerArcherCount"), "zero count clamp");

    var wrap = NewContext(slot: 6, withMap: true);
    wrap.Player.m_ItemList.Add(NewItem(72, 1, 1));
    PlaceActor(wrap.Map, 38, 46, 99);
    SetField(wrap.Player, "m_sbNativeMagicTowerArcherCount", sbyte.MinValue);
    Assert(Move(wrap, 6), "sbyte wrap success rejected");
    Equal(sbyte.MaxValue, ReadField<sbyte>(wrap.Player,
        "m_sbNativeMagicTowerArcherCount"), "sbyte DEC wrap");
}

static void CheckSourceContract()
{
    var root = FindRepositoryRoot();
    var source = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.NativeMagicTower.MoveChance.cs"));
    Assert(source.Contains(
        "internal bool GetNativeMagicTowerMoveChance(NormNpc npc, int index)",
        StringComparison.Ordinal), "move chance method signature");
    Assert(source.Contains(
        "TryGetNativeMagicTowerArcherCoordinates(index, out var x,",
        StringComparison.Ordinal), "shared coordinate table not used");
    Assert(source.Contains(
        "archer.m_btRaceServer != NativeMagicTowerArcherRace",
        StringComparison.Ordinal), "Race 99 gate not used");
    Assert(source.Contains("standardItem.StdMode == 7",
        StringComparison.Ordinal), "StdMode 7 pile gate missing");
    Assert(source.Contains("EnsureClientItemId(take.Item)",
        StringComparison.Ordinal), "partial ClientItemID packet missing");
    Assert(source.Contains("unchecked((uint)makeIndex)",
        StringComparison.Ordinal), "Cardinal MakeIndex log missing");
    Assert(!source.Contains("ClientItemID ==", StringComparison.Ordinal),
        "ClientItemID was used as a transaction selector");
}

static Context NewContext(bool addProperty = true, int slot = 0,
    bool withMap = false)
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
    if (slot is >= 1 and <= 10) ArcherSlots(player)[slot - 1] = 1;
    return new Context(player, npc, map);
}

static bool Move(Context context, int index)
{
    M2Share.LogStringList.Clear();
    context.Player.m_MsgList.Clear();
    var method = typeof(TPlayObject).GetMethod(
        "GetNativeMagicTowerMoveChance",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(method != null, "move chance method missing");
    return (bool)method.Invoke(context.Player,
        new object[] { context.Npc, index })!;
}

static Envirnoment NewMap()
{
    var map = new Envirnoment { sMapName = "player-map" };
    typeof(Envirnoment).GetMethod("Initialize",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(map, new object[] { (short)64, (short)64 });
    return map;
}

static TBaseObject PlaceActor(Envirnoment map, short x, short y, byte race)
{
    var actor = new TBaseObject
    {
        m_boOffLineFlag = true,
        m_PEnvir = map,
        m_sMapName = map.sMapName,
        m_nCurrX = x,
        m_nCurrY = y,
        m_btRaceServer = race,
        bo2B9 = true
    };
    Assert(ReferenceEquals(actor, map.AddToMap(x, y,
        CellType.OS_MOVINGOBJECT, actor)), "could not place actor");
    return actor;
}

static TUserItem NewItem(int makeIndex, ushort itemIndex, ushort dura,
    ushort duraMax = 100, int clientItemId = 0) => new()
{
    MakeIndex = makeIndex,
    ClientItemID = clientItemId,
    wIndex = itemIndex,
    Dura = dura,
    DuraMax = duraMax,
    btValue = new byte[14]
};

static void SetDefinitions(params GoodItem[] definitions)
{
    M2Share.UserEngine.StdItemList.Clear();
    foreach (var definition in definitions)
        M2Share.UserEngine.StdItemList.Add(definition);
}

static byte[] ArcherSlots(TPlayObject player) =>
    ReadField<byte[]>(player, "m_btNativeMagicTowerArcherSlots");

static byte Slot(TPlayObject player, int index) =>
    ArcherSlots(player)[index - 1];

static T ReadField<T>(object target, string fieldName)
{
    var field = target.GetType().GetField(fieldName,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    Assert(field != null, "missing field " + fieldName);
    return (T)field.GetValue(target)!;
}

static void SetField<T>(object target, string fieldName, T value)
{
    var field = target.GetType().GetField(fieldName,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    Assert(field != null, "missing field " + fieldName);
    field.SetValue(target, value);
}

static string MerchantMessage(TPlayObject player)
{
    var messages = player.m_MsgList.Where(message =>
        message.wIdent == Grobal2.RM_MERCHANTSAY).ToArray();
    Equal(1, messages.Length, "merchant dialog count");
    return messages[0].Buff;
}

static string LogAt(int index) => (string)M2Share.LogStringList[index]!;

static PlayerSnapshot Snapshot(TPlayObject player, int slot) => new(
    player.m_ItemList.Count,
    player.m_ItemList.Count == 0 ? (ushort)0 : player.m_ItemList[0].Dura,
    Slot(player, slot),
    ReadField<byte>(player, "m_btNativeMagicTowerEngageChance"),
    ReadField<sbyte>(player, "m_sbNativeMagicTowerArcherCount"));

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

readonly record struct PlayerSnapshot(int BagCount, ushort Dura, byte Slot,
    byte Chance, sbyte Count);
