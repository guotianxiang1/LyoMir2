using System.Buffers.Binary;
using System.Collections;
using System.Runtime.CompilerServices;
using GameSvr;
using SystemModule;

PrepareRuntimeConfig();

M2Share.UserEngine = new UserEngine();
M2Share.ProcessMsgCriticalSection = new object();
M2Share.g_Config = new GameSvrConfig();
M2Share.LogMsgCriticalSection = new object();
M2Share.LogStringList = new ArrayList();
M2Share.UserEngine.StdItemList.Add(new GoodItem
{
    Name = "native-pile-150",
    StdMode = 150,
    DuraMax = 100,
    NativeReserved02 = 0x80
});
M2Share.UserEngine.StdItemList.Add(new GoodItem
{
    Name = "native-rejected-159",
    StdMode = 159,
    DuraMax = 100
});

NativeWordIsNotExtensionBind();
NativeWordMismatchStillReplies();
NativeTimestampGate();
InvalidRequestIsSilent();
InvalidBagTargetFallsBackToStorage();
HeroMissFallsBackToPlayerStorage();
NonHeroSeriesUsesPlayerBag();
ClientIdsDoNotFallBackToPersistedIds();
PartialMergeMessageAndLogSequence();
WholeMergeMessageAndLogSequence();
FullTargetStillCompletes();
SplitFallsBackPastNonPileBagItem();
HeroSplitMissFallsBackToPlayerStorage();
SplitZeroCreatesFreshTemplateItem();
HeroSplitSuccessUsesHeroContainer();
HeroSplitUsesLevelCapacity();
SplitMutationOrderIsExplicit();

Console.WriteLine("NativePileUpItemCompatCheck: PASS");

void NativeWordIsNotExtensionBind()
{
    var player = NewPlayer();
    var target = Item(1, 1001, 0, 100, 7, 0);
    var source = Item(1, 1002, 20, 20, 7, 2);
    player.m_ItemList.Add(target);
    player.m_ItemList.Add(source);

    player.ClientPileUpItem(1001, 1002, 0);

    Equal((ushort)20, target.Dura, "matching native +0x34 merged");
    Equal(false, player.m_ItemList.Contains(source),
        "zero source removed despite extension Bind mismatch");
    Result(player, 1001, 1002, 0, "native +0x34 result");
}

void NativeWordMismatchStillReplies()
{
    var player = NewPlayer();
    var target = Item(1, 1101, 10, 100, 1, 1);
    var source = Item(1, 1102, 20, 100, 2, 1);
    player.m_ItemList.Add(target);
    player.m_ItemList.Add(source);

    player.ClientPileUpItem(1101, 1102, 0);

    Equal((ushort)10, target.Dura, "different native +0x34 target unchanged");
    Equal((ushort)20, source.Dura, "different native +0x34 source unchanged");
    Message(player, "绑定物品不能与非绑定物品叠加",
        "native +0x34 mismatch message");
    Result(player, 1101, 1102, 0, "native +0x34 mismatch result");
}

void NativeTimestampGate()
{
    var player = NewPlayer();
    var target = Item(1, 1151, 10, 100, 0, 0, 45000.0);
    var source = Item(1, 1152, 20, 100, 0, 0,
        45000.0 + 61.0 / 1440.0);
    player.m_ItemList.Add(target);
    player.m_ItemList.Add(source);

    player.ClientPileUpItem(1151, 1152, 0);

    Equal((ushort)10, target.Dura, "61-minute target unchanged");
    Equal((ushort)20, source.Dura, "61-minute source unchanged");
    Message(player, "不同时效的物品不能叠加", "61-minute mismatch message");
    Result(player, 1151, 1152, 0, "61-minute mismatch result");

    SetTimestamp(source, 45000.0 + 60.0 / 1440.0);
    player.ClientPileUpItem(1151, 1152, 0);
    Equal((ushort)30, target.Dura, "60-minute boundary merged");
}

void InvalidRequestIsSilent()
{
    var player = NewPlayer();
    player.m_DefMsg = Grobal2.MakeDefaultMsg(123, 0, 0, 0, 0);
    player.ClientPileUpItem(1201, 1202, 0);
    Equal((ushort)123, player.m_DefMsg.Ident, "missing items are silent");

    player.m_ItemList.Add(Item(2, 1201, 10, 100, 0, 0));
    player.m_ItemList.Add(Item(2, 1202, 20, 100, 0, 0));
    player.ClientPileUpItem(1201, 1202, 0);
    Equal((ushort)123, player.m_DefMsg.Ident, "non-pile target is silent");
}

void InvalidBagTargetFallsBackToStorage()
{
    var player = NewPlayer();
    player.m_ItemList.Add(Item(2, 1301, 10, 100, 0, 0));
    player.m_ItemList.Add(Item(2, 1302, 20, 100, 0, 0));
    var target = Item(1, 1301, 10, 100, 0, 0);
    var source = Item(1, 1302, 20, 100, 0, 0);
    player.m_StorageItemList.Add(target);
    player.m_StorageItemList.Add(source);

    player.ClientPileUpItem(1301, 1302, 0);

    Equal((ushort)30, target.Dura, "storage fallback target");
    Equal(false, player.m_StorageItemList.Contains(source), "storage fallback source removed");
    Result(player, 1301, 1302, 0, "storage fallback result");
}

void HeroMissFallsBackToPlayerStorage()
{
    var player = NewPlayer();
    player.m_HeroObject = (HeroObject)RuntimeHelpers.GetUninitializedObject(
        typeof(HeroObject));
    player.m_HeroObject.m_ItemList = new List<TUserItem>();
    var target = Item(1, 1401, 10, 100, 0, 0);
    var source = Item(1, 1402, 20, 100, 0, 0);
    player.m_StorageItemList.Add(target);
    player.m_StorageItemList.Add(source);

    player.ClientPileUpItem(1401, 1402, 1);

    Equal((ushort)30, target.Dura, "hero miss storage fallback target");
    Equal(false, player.m_StorageItemList.Contains(source),
        "hero miss storage fallback source removed");
    Result(player, 1401, 1402, 1, "hero miss storage fallback result");
}

void NonHeroSeriesUsesPlayerBag()
{
    var player = NewPlayer();
    var target = Item(1, 1501, 10, 100, 0, 0);
    var source = Item(1, 1502, 20, 100, 0, 0);
    player.m_ItemList.Add(target);
    player.m_ItemList.Add(source);

    player.ClientPileUpItem(1501, 1502, 2);

    Equal((ushort)30, target.Dura, "non-hero series target");
    Equal(false, player.m_ItemList.Contains(source), "non-hero series source removed");
    Result(player, 1501, 1502, 2, "non-hero series result");
}

void ClientIdsDoNotFallBackToPersistedIds()
{
    var player = NewPlayer();
    var bagTarget = Item(1, 1601, 10, 100, 0, 0, makeIndex: 1701);
    var bagSource = Item(1, 1602, 20, 100, 0, 0, makeIndex: 1702);
    var storageTarget = Item(1, 1701, 10, 100, 0, 0, makeIndex: 2601);
    var storageSource = Item(1, 1702, 20, 100, 0, 0, makeIndex: 2602);
    player.m_ItemList.Add(bagTarget);
    player.m_ItemList.Add(bagSource);
    player.m_StorageItemList.Add(storageTarget);
    player.m_StorageItemList.Add(storageSource);

    player.ClientPileUpItem(1701, 1702, 0);

    Equal((ushort)10, bagTarget.Dura, "MakeIndex collision bag target unchanged");
    Equal((ushort)20, bagSource.Dura, "MakeIndex collision bag source unchanged");
    Equal((ushort)30, storageTarget.Dura, "ClientItemID storage target merged");
    Equal(false, player.m_StorageItemList.Contains(storageSource),
        "ClientItemID storage source removed");
}

void PartialMergeMessageAndLogSequence()
{
    var player = NewPlayer();
    var target = Item(1, 1801, 10, 15, 0, 0, makeIndex: 1802);
    var source = Item(1, 1802, 20, 100, 0, 0, makeIndex: 1801);
    player.m_ItemList.Add(target);
    player.m_ItemList.Add(source);
    ResetObservations(player, source);

    player.ClientPileUpItem(1801, 1802, 0);

    PacketSequence(player, "partial merge",
        (Grobal2.SM_BAGITEMDURACHG, 1801, 15, 15, 0),
        (Grobal2.SM_BAGITEMDURACHG, 1802, 15, 100, 0),
        (Grobal2.SM_ITEM_PILEUP_RESULT, 1801, HUtil32.LoWord(1802),
            HUtil32.HiWord(1802), 0));
    PileLogSequence("partial merge",
        (0x45, 1802, 15, "被减少的道具ID:1801"),
        (0x44, 1801, 15, "被增加的道具ID:1802"));
}

void WholeMergeMessageAndLogSequence()
{
    var player = NewPlayer();
    var target = Item(1, 1901, 10, 100, 0, 0, makeIndex: 2901);
    var source = Item(1, 1902, 20, 20, 0, 0, makeIndex: 2902);
    player.m_ItemList.Add(target);
    player.m_ItemList.Add(source);
    ResetObservations(player, source);

    player.ClientPileUpItem(1901, 1902, 0);

    PacketSequence(player, "whole merge",
        (Grobal2.SM_BAGITEMDURACHG, 1901, 30, 100, 0),
        (Grobal2.SM_DELITEM, 1902, 0, 0, 1),
        (Grobal2.SM_ITEM_PILEUP_RESULT, 1901, HUtil32.LoWord(1902),
            HUtil32.HiWord(1902), 0));
    Equal(false, player.SentPackets[1].SourceWasPresent,
        "whole merge removes source before SM_DELITEM");
    PileLogSequence("whole merge",
        (0x45, 2901, 30, "被减少的道具ID:2902"),
        (0x44, 2902, 0, "被增加的道具ID:2901"));
}

void FullTargetStillCompletes()
{
    var player = NewPlayer();
    var target = Item(1, 2001, 100, 100, 0, 0, makeIndex: 3001);
    var source = Item(1, 2002, 20, 100, 0, 0, makeIndex: 3002);
    player.m_ItemList.Add(target);
    player.m_ItemList.Add(source);
    ResetObservations(player, source);

    player.ClientPileUpItem(2001, 2002, 0);

    PacketSequence(player, "full target",
        (Grobal2.SM_BAGITEMDURACHG, 2002, 20, 100, 0),
        (Grobal2.SM_ITEM_PILEUP_RESULT, 2001, HUtil32.LoWord(2002),
            HUtil32.HiWord(2002), 0));
    PileLogSequence("full target",
        (0x45, 3001, 100, "被减少的道具ID:3002"),
        (0x44, 3002, 20, "被增加的道具ID:3001"));
}

void SplitFallsBackPastNonPileBagItem()
{
    var player = NewPlayer();
    var bagDecoy = Item(2, 2101, 9, 100, 0, 0, makeIndex: 2101);
    var storageSource = Item(1, 2101, 20, 100, 7, 4, makeIndex: 3101);
    player.m_ItemList.Add(bagDecoy);
    player.m_StorageItemList.Add(storageSource);
    ResetObservations(player, storageSource);

    InvokeSplit(player, 2101, 5, 0);

    Equal((ushort)9, bagDecoy.Dura, "non-pile bag item unchanged");
    Equal((ushort)15, storageSource.Dura, "split storage fallback source");
    Equal(2, player.m_StorageItemList.Count, "split storage fallback added item");
    Equal((ushort)5, player.m_StorageItemList[1].Dura,
        "split storage fallback new item dura");
    PacketPrefix(player, "split storage source status",
        Grobal2.SM_STORAGEITEMDURACHG, 2101, 15, 100, 0);
    var split = player.m_StorageItemList[1];
    PileLogSequence("split storage fallback",
        (0x46, 3101, 15, "拆分生成的道具ID：" + split.MakeIndex),
        (0x47, split.MakeIndex, 5, "源道具ID：3101"));
}

void HeroSplitMissFallsBackToPlayerStorage()
{
    var player = NewPlayer();
    player.m_HeroObject = NewHero();
    var source = Item(1, 2201, 20, 100, 0, 0, makeIndex: 3201);
    player.m_StorageItemList.Add(source);
    ResetObservations(player, source);

    InvokeSplit(player, 2201, 5, 1);

    Equal((ushort)15, source.Dura, "hero split miss storage source");
    Equal(2, player.m_StorageItemList.Count, "hero split miss storage add");
    PacketPrefix(player, "hero split miss storage source status",
        Grobal2.SM_STORAGEITEMDURACHG, 2201, 15, 100, 0);
    var split = player.m_StorageItemList[1];
    PileLogSequence("hero split miss storage",
        (0x46, 3201, 15, "拆分生成的道具ID：" + split.MakeIndex),
        (0x47, split.MakeIndex, 5, "源道具ID：3201"));
}

void SplitZeroCreatesFreshTemplateItem()
{
    var player = NewPlayer();
    var source = Item(1, 2301, 20, 77, 0x5678, 9, makeIndex: 3301);
    source.ys1 = 11;
    source.jp1 = 12;
    source.pname = "copied-name";
    source.NativeRecord = new byte[] { 1, 2, 3 };
    source.btValue[8] = 0x44;
    player.m_ItemList.Add(source);
    ResetObservations(player, source);

    InvokeSplit(player, 2301, 0, 2);

    Equal((ushort)20, source.Dura, "zero split source unchanged");
    Equal(2, player.m_ItemList.Count, "zero split adds item");
    var split = player.m_ItemList[1];
    Equal((ushort)0, split.Dura, "zero split new dura");
    Equal((ushort)100, split.DuraMax, "split uses standard template DuraMax");
    Equal((ushort)0x5678, ReadCompatibility(split),
        "split copies only native +0x34 word");
    Equal((byte)0, split.btValue[8], "split does not clone other btValue");
    Equal((byte)0, split.Bind, "split does not clone Bind");
    Equal(0, split.ys1, "split does not clone yanshen values");
    Equal((byte)0, split.jp1, "split does not clone extreme values");
    Equal(string.Empty, split.pname, "split does not clone description");
    Equal<byte[]>(null, split.NativeRecord, "split does not clone native record");
    Equal(false, split.MakeIndex == source.MakeIndex, "split gets fresh MakeIndex");
    Equal(false, split.ClientItemID == source.ClientItemID,
        "split gets fresh ClientItemID");
    PacketPrefix(player, "zero split source status",
        Grobal2.SM_BAGITEMDURACHG, 2301, 20, 77, 0);
    Equal(0, player.SentPackets[0].LogCount,
        "zero split source status precedes logs");
    Equal(1, player.SentPackets[0].ItemCount,
        "zero split source status precedes insertion");
    Equal((ushort)Grobal2.SM_ADDITEM, player.m_DefMsg.Ident,
        "zero split add packet");
    PileLogSequence("zero split",
        (0x46, 3301, 20, "拆分生成的道具ID：" + split.MakeIndex),
        (0x47, split.MakeIndex, 0, "源道具ID：3301"));
}

void SplitMutationOrderIsExplicit()
{
    var root = FindRepositoryRoot();
    var source = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.PileItems.cs"));
    var start = source.IndexOf("private void ClientSplitItem",
        StringComparison.Ordinal);
    var end = source.IndexOf("private PileItemContainer FindPileItemContainer",
        start, StringComparison.Ordinal);
    Equal(false, start < 0 || end <= start, "split source body located");
    var body = source.Substring(start, end - start);

    AssertOrdered(body, "split mutation order",
        "source.Dura -= (ushort)count;",
        "SendPileItemDuraChange(container, source);",
        "WritePileItemLog(0x46",
        "WritePileItemLog(0x47",
        "ReassignClientItemId(splitItem);",
        "items.Add(splitItem);",
        "SendAddItem(splitItem);",
        "SendHeroAddItem(splitItem);",
        "SendStorageSplitItem(splitItem);");
}

void HeroSplitUsesLevelCapacity()
{
    var player = NewPlayer();
    var hero = NewHero();
    player.m_HeroObject = hero;
    var source = Item(1, 2401, 20, 100, 0, 0, makeIndex: 3401);
    hero.m_ItemList.Add(source);
    for (var i = 1; i < 10; i++)
        hero.m_ItemList.Add(Item(1, 2401 + i, 1, 100, 0, 0));
    player.m_DefMsg = Grobal2.MakeDefaultMsg(123, 0, 0, 0, 0);
    ResetObservations(player, source);

    InvokeSplit(player, 2401, 1, 1);

    Equal((ushort)20, source.Dura, "level-zero full hero bag source unchanged");
    Equal(10, hero.m_ItemList.Count, "level-zero hero bag capacity is 10");
    Equal(0, player.SentPackets.Count, "full hero bag split is silent");
    Equal((ushort)123, player.m_DefMsg.Ident, "full hero bag has no add packet");
}

void HeroSplitSuccessUsesHeroContainer()
{
    var player = NewPlayer();
    var hero = NewHero();
    hero.m_Master = player;
    player.m_HeroObject = hero;
    var source = Item(1, 2501, 20, 100, 0x1234, 0, makeIndex: 3501);
    hero.m_ItemList.Add(source);
    ResetObservations(player, source);

    InvokeSplit(player, 2501, 5, 1);

    Equal((ushort)15, source.Dura, "hero split source dura");
    Equal(2, hero.m_ItemList.Count, "hero split inserts into hero bag");
    var split = hero.m_ItemList[1];
    Equal((ushort)5, split.Dura, "hero split new dura");
    Equal((ushort)0x1234, ReadCompatibility(split),
        "hero split copies native compatibility");
    Equal(false, split.ClientItemID == 0, "hero split assigns ClientItemID");
    PacketPrefix(player, "hero split source status",
        Grobal2.SM_HERO_BAGITEMDURACHG, 2501, 15, 100, 0);
    Equal(0, player.SentPackets[0].LogCount,
        "hero split source status precedes logs");
    Equal(1, player.SentPackets[0].ItemCount,
        "hero split source status precedes insertion");
    PileLogSequence("hero split",
        (0x46, 3501, 15, "拆分生成的道具ID：" + split.MakeIndex),
        (0x47, split.MakeIndex, 5, "源道具ID：3501"));
}

static RecordingPlayer NewPlayer()
{
    var player = (RecordingPlayer)RuntimeHelpers.GetUninitializedObject(
        typeof(RecordingPlayer));
    player.m_ItemList = new List<TUserItem>();
    player.m_StorageItemList = new List<TUserItem>();
    player.m_MsgList = new List<SendMessage>();
    player.SentPackets = new List<PacketSnapshot>();
    player.m_sMapName = "pile-map";
    player.m_nCurrX = 11;
    player.m_nCurrY = 22;
    player.m_sCharName = "pile-player";
    player.m_boOffLineFlag = true;
    return player;
}

static HeroObject NewHero()
{
    var hero = (HeroObject)RuntimeHelpers.GetUninitializedObject(typeof(HeroObject));
    hero.m_ItemList = new List<TUserItem>();
    hero.m_Abil = new TAbility();
    return hero;
}

static TUserItem Item(ushort index, int clientId, ushort dura, ushort duraMax,
    ushort nativeCompatibility, byte bind, double timestamp = 0.0,
    int makeIndex = 0)
{
    var item = new TUserItem
    {
        wIndex = index,
        MakeIndex = makeIndex,
        ClientItemID = clientId,
        Dura = dura,
        DuraMax = duraMax,
        Bind = bind
    };
    BinaryPrimitives.WriteUInt16LittleEndian(item.btValue.AsSpan(10, 2),
        nativeCompatibility);
    SetTimestamp(item, timestamp);
    return item;
}

static ushort ReadCompatibility(TUserItem item)
{
    return BinaryPrimitives.ReadUInt16LittleEndian(item.btValue.AsSpan(10, 2));
}

static void InvokeSplit(TPlayObject player, int clientItemId, int count, int series)
{
    var method = typeof(TPlayObject).GetMethod("ClientSplitItem",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    method!.Invoke(player, new object[] { clientItemId, count, series });
}

static void ResetObservations(RecordingPlayer player, TUserItem source)
{
    player.SentPackets.Clear();
    player.ObservedItems = player.m_ItemList.Contains(source)
        ? player.m_ItemList
        : player.m_HeroObject?.m_ItemList.Contains(source) == true
            ? player.m_HeroObject.m_ItemList
            : player.m_StorageItemList;
    player.ObservedSource = source;
    M2Share.LogStringList.Clear();
}

static void PacketSequence(RecordingPlayer player, string area,
    params (short Ident, int Recog, int Param, int Tag, int Series)[] expected)
{
    Equal(expected.Length, player.SentPackets.Count, area + " packet count");
    for (var i = 0; i < expected.Length; i++)
    {
        var packet = player.SentPackets[i];
        Equal((ushort)expected[i].Ident, packet.Ident, area + $" packet {i} ident");
        Equal(expected[i].Recog, packet.Recog, area + $" packet {i} recog");
        Equal((ushort)expected[i].Param, packet.Param, area + $" packet {i} param");
        Equal((ushort)expected[i].Tag, packet.Tag, area + $" packet {i} tag");
        Equal((ushort)expected[i].Series, packet.Series, area + $" packet {i} series");
    }
}

static void PacketPrefix(RecordingPlayer player, string area, short ident,
    int recog, int param, int tag, int series)
{
    Equal(false, player.SentPackets.Count == 0, area + " exists");
    PacketSequencePrefix(player.SentPackets[0], area, ident, recog, param, tag,
        series);
}

static void PacketSequencePrefix(PacketSnapshot packet, string area, short ident,
    int recog, int param, int tag, int series)
{
    Equal((ushort)ident, packet.Ident, area + " ident");
    Equal(recog, packet.Recog, area + " recog");
    Equal((ushort)param, packet.Param, area + " param");
    Equal((ushort)tag, packet.Tag, area + " tag");
    Equal((ushort)series, packet.Series, area + " series");
}

static void PileLogSequence(string area,
    params (int Action, int MakeIndex, int Dura, string RelatedId)[] expected)
{
    Equal(expected.Length, M2Share.LogStringList.Count, area + " log count");
    for (var i = 0; i < expected.Length; i++)
    {
        var fields = ((string)M2Share.LogStringList[i]!).Split('\t');
        Equal(9, fields.Length, area + $" log {i} field count");
        Equal(expected[i].Action.ToString(), fields[0], area + $" log {i} action");
        Equal("pile-map", fields[1], area + $" log {i} map");
        Equal("11", fields[2], area + $" log {i} x");
        Equal("22", fields[3], area + $" log {i} y");
        Equal("pile-player", fields[4], area + $" log {i} player");
        Equal("native-pile-150", fields[5], area + $" log {i} item");
        Equal(expected[i].MakeIndex.ToString(), fields[6],
            area + $" log {i} MakeIndex");
        Equal(expected[i].Dura.ToString(), fields[7], area + $" log {i} Dura");
        Equal(expected[i].RelatedId, fields[8], area + $" log {i} related ID");
    }
}

static void AssertOrdered(string source, string area, params string[] markers)
{
    var offset = 0;
    foreach (var marker in markers)
    {
        var index = source.IndexOf(marker, offset, StringComparison.Ordinal);
        Equal(false, index < 0, area + " marker " + marker);
        offset = index + marker.Length;
    }
}

static string FindRepositoryRoot()
{
    foreach (var start in new[] { Environment.CurrentDirectory,
                 AppContext.BaseDirectory })
    {
        var directory = new DirectoryInfo(start);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameSvr", "Players",
                    "TPlayObject.PileItems.cs")))
                return directory.FullName;
            directory = directory.Parent;
        }
    }
    throw new DirectoryNotFoundException("LyoMir2 repository root not found");
}

static void SetTimestamp(TUserItem item, double timestamp)
{
    BinaryPrimitives.WriteInt64LittleEndian(item.btValue.AsSpan(0, 8),
        BitConverter.DoubleToInt64Bits(timestamp));
}

static void Result(TPlayObject player, int targetId, int sourceId, int series,
    string area)
{
    Equal((ushort)Grobal2.SM_ITEM_PILEUP_RESULT, player.m_DefMsg.Ident,
        area + " ident");
    Equal(targetId, player.m_DefMsg.Recog, area + " target");
    Equal(HUtil32.LoWord(sourceId), player.m_DefMsg.Param, area + " source low");
    Equal(HUtil32.HiWord(sourceId), player.m_DefMsg.Tag, area + " source high");
    Equal((ushort)series, player.m_DefMsg.Series, area + " series");
}

static void Message(TPlayObject player, string expected, string area)
{
    var messages = player.m_MsgList
        .Where(message => message.wIdent == Grobal2.RM_SYSMESSAGE).ToArray();
    Equal(1, messages.Length, area + " count");
    Equal(expected, messages[0].Buff, area + " text");
    Equal(0xDB, messages[0].nParam1, area + " foreground");
    Equal(0xFF, messages[0].nParam2, area + " background");
}

static void Equal<T>(T expected, T actual, string area)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{area}: expected={expected}, actual={actual}");
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

sealed class RecordingPlayer : TPlayObject
{
    internal List<PacketSnapshot> SentPackets;
    internal IList<TUserItem> ObservedItems;
    internal TUserItem ObservedSource;

    internal override void SendSocket(ClientPacket defMsg, string sMsg)
    {
        SentPackets.Add(new PacketSnapshot(defMsg.Ident, defMsg.Recog,
            defMsg.Param, defMsg.Tag, defMsg.Series,
            ObservedItems?.Contains(ObservedSource) == true,
            M2Share.LogStringList?.Count ?? -1, ObservedItems?.Count ?? -1));
    }
}

readonly record struct PacketSnapshot(ushort Ident, int Recog, ushort Param,
    ushort Tag, ushort Series, bool SourceWasPresent, int LogCount,
    int ItemCount);
