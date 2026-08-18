using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using GameSvr;
using SystemModule;

PrepareRuntimeConfig();
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var gbk = Encoding.GetEncoding(936);

M2Share.UserEngine = new UserEngine();
M2Share.ObjectManager = new ObjectManager();
M2Share.UserEngine.m_MagicList.Add(new TMagic { wMagicID = 10, sMagicName = "技能十" });
M2Share.UserEngine.m_MagicList.Add(new TMagic { wMagicID = 69, sMagicName = "特殊技能" });
M2Share.UserEngine.m_HeroMagicList.Add(new TMagic { wMagicID = 10, sMagicName = "技能十" });
M2Share.UserEngine.m_HeroMagicList.Add(new TMagic { wMagicID = 69, sMagicName = "特殊技能" });
M2Share.g_Config.dwNeedExps[77] = 34567890;

var human = new THumDataInfo();
human.Data.btSecHeroPracticeRewardMode = 3;
human.Data.btSecHeroPracticeCostTier = 2;
human.Data.wSecHeroPracticeLevel = 0xC3D4;
human.Data.nLingFu = 123456;
human.Data.nUsedLingFu = 654321;
var player = new TPlayObject();
var getHumData = typeof(UserEngine).GetMethod("GetHumData",
    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
Assert(getHumData != null, "player native load method");
var loadArguments = new object[] { player, human };
getHumData!.Invoke(M2Share.UserEngine, loadArguments);
Equal((byte)3, player.m_btSecHeroPracticeRewardMode,
    "player secondary-hero practice reward mode load");
Equal((byte)2, player.m_btSecHeroPracticeCostTier,
    "player secondary-hero practice cost tier load");
Equal((ushort)0xC3D4, player.m_wSecHeroPracticeLevel,
    "player secondary-hero practice level load");
Equal(123456, player.m_nLingFu, "player native LingFu load");
Equal(654321, player.m_nUsedLingFu, "player native used LingFu load");
var playerSnapshot = new THumDataInfo();
player.MakeSaveRcd(ref playerSnapshot);
Equal(player.m_btSecHeroPracticeRewardMode,
    playerSnapshot.Data.btSecHeroPracticeRewardMode,
    "player secondary-hero practice reward mode save");
Equal(player.m_btSecHeroPracticeCostTier, playerSnapshot.Data.btSecHeroPracticeCostTier,
    "player secondary-hero practice cost tier save");
Equal(player.m_wSecHeroPracticeLevel, playerSnapshot.Data.wSecHeroPracticeLevel,
    "player secondary-hero practice level save");
Equal(player.m_nLingFu, playerSnapshot.Data.nLingFu,
    "player native LingFu save");
Equal(player.m_nUsedLingFu, playerSnapshot.Data.nUsedLingFu,
    "player native used LingFu save");

var raw = new byte[NativeHeroDbFrameCodec.HeroRecordSize];
WriteShortString(raw, NativeHeroDbFrameCodec.MasterNameOffset, 15, "主人甲");
WriteShortString(raw, NativeHeroDbFrameCodec.HeroNameOffset, 15, "英雄乙");
raw[NativeHeroDbFrameCodec.RaceOffset] = Grobal2.RC_HEROOBJECT;
raw[NativeHeroDbFrameCodec.SexOffset] = 1;
raw[NativeHeroDbFrameCodec.JobOffset] = 2;
raw[NativeHeroDbFrameCodec.HeroTypeOffset] = 2;
raw[NativeHeroDbFrameCodec.HeroRankOffset] = 7;
Equal(0xAC, NativeHeroDbFrameCodec.ForceExpOffset - 8,
    "hero force-exp record+8 relative offset");
Equal(0xB0, NativeHeroDbFrameCodec.ForceLvOffset - 8,
    "hero force-level record+8 relative offset");
BinaryPrimitives.WriteInt32LittleEndian(
    raw.AsSpan(NativeHeroDbFrameCodec.ForceExpOffset, 4), unchecked((int)0x89ABCDEF));
BinaryPrimitives.WriteInt32LittleEndian(
    raw.AsSpan(NativeHeroDbFrameCodec.ForceLvOffset, 4), 0x10203040);
// 英雄模式是持久化字段：编码 sub_689034 @0x68910A/0x689110，解码 sub_6888FC
// @0x688A9C/0x688AA5，两处 ebx 都是记录基址+8，故记录偏移 0x9C + 8 = 0xA4。
Equal(0x9C, NativeHeroDbFrameCodec.HeroModeOffset - 8,
    "hero mode record+8 relative offset");
raw[NativeHeroDbFrameCodec.HeroModeOffset] = 2; // 休息
raw[0x150] = 0xCE;
BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(NativeHeroDbFrameCodec.LevelOffset, 2), 77);
BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(NativeHeroDbFrameCodec.GoldOffset, 4), 123456);
BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(NativeHeroDbFrameCodec.ExpOffset, 4), 0x71234567);
WriteSplit(raw, NativeHeroDbFrameCodec.HpLowOffset, NativeHeroDbFrameCodec.HpHighOffset, 240573);
WriteSplit(raw, NativeHeroDbFrameCodec.MpLowOffset, NativeHeroDbFrameCodec.MpHighOffset, 131119);
BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(NativeHeroDbFrameCodec.CurrentXOffset, 4), 321);
BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(NativeHeroDbFrameCodec.CurrentYOffset, 4), 654);
raw[NativeHeroDbFrameCodec.NativeCommonInformationOption1Offset] = 1;
BinaryPrimitives.WriteInt32LittleEndian(
    raw.AsSpan(NativeHeroDbFrameCodec.NativeCommonInformationOption2Offset, 4), 87654321);
raw[NativeHeroDbFrameCodec.NativeCommonInformationOption3Offset] = 0;

Equal(0x4694, NativeHeroDbFrameCodec.NativeSlaveRecordOffset,
    "embedded hero slave record offset");
Equal(0x20, NativeHeroDbFrameCodec.NativeSlaveRecordSize,
    "embedded hero slave record size");
var nativeSlave = raw.AsSpan(NativeHeroDbFrameCodec.NativeSlaveRecordOffset,
    NativeHeroDbFrameCodec.NativeSlaveRecordSize);
WriteShortString(raw, NativeHeroDbFrameCodec.NativeSlaveRecordOffset, 15,
    "记录神兽");
BinaryPrimitives.WriteInt32LittleEndian(nativeSlave.Slice(0x10, 4), 0x10203040);
BinaryPrimitives.WriteInt32LittleEndian(nativeSlave.Slice(0x14, 4),
    unchecked((int)0xFEDCBA98));
BinaryPrimitives.WriteUInt16LittleEndian(nativeSlave.Slice(0x18, 2), 0xFFFF);
BinaryPrimitives.WriteUInt16LittleEndian(nativeSlave.Slice(0x1A, 2), 0xA1B2);
nativeSlave[0x1C] = 7;
nativeSlave[0x1D] = 3;
nativeSlave[0x1E] = 0xC3;
nativeSlave[0x1F] = 0xD4;

var equip0 = raw.AsSpan(NativeHeroDbFrameCodec.EquippedItemsOffset,
    NativeHeroDbFrameCodec.ItemRecordSize);
WriteItem(equip0, 1001, 101, 500, 800, 0x81);
equip0[0xB8] = 1;
equip0[100] = 0xA5;
var emptyEquip2 = raw.AsSpan(NativeHeroDbFrameCodec.EquippedItemsOffset
                             + 2 * NativeHeroDbFrameCodec.ItemRecordSize,
    NativeHeroDbFrameCodec.ItemRecordSize);
emptyEquip2[111] = 0xB6;

var bag0 = raw.AsSpan(NativeHeroDbFrameCodec.BagItemsOffset,
    NativeHeroDbFrameCodec.ItemRecordSize);
WriteItem(bag0, 2002, 202, 10, 20, 0xC0);
bag0[120] = 0xC7;

var normal0 = raw.AsSpan(NativeHeroDbFrameCodec.NormalMagicOffset,
    NativeHeroDbFrameCodec.MagicRecordSize);
WriteMagic(normal0, 10, 2, 3, 4000);
normal0[20] = 0xD8;
var unknownNormal = raw.AsSpan(NativeHeroDbFrameCodec.NormalMagicOffset
                               + 5 * NativeHeroDbFrameCodec.MagicRecordSize,
    NativeHeroDbFrameCodec.MagicRecordSize);
WriteMagic(unknownNormal, 777, 1, 0, 7000);
unknownNormal[30] = 0xE9;
var unknownNormalCopy = unknownNormal.ToArray();
var special0 = raw.AsSpan(NativeHeroDbFrameCodec.SpecialMagicOffset,
    NativeHeroDbFrameCodec.MagicRecordSize);
WriteMagic(special0, 69, 3, 1, 9000);
special0[25] = 0xFA;

Assert(NativeHeroDbFrameCodec.TryCreateRecord(raw, out var nativeRecord, out var error), error);
var nativeType7 = new byte[4 + 16];
BinaryPrimitives.WriteUInt32LittleEndian(nativeType7, 0x0000FAFA);
for (var i = 0; i < 16; i++) nativeType7[4 + i] = (byte)(0x40 + i);
nativeType7[4 + 12] = 1;
var nativeType7Record = nativeType7.AsSpan(4, 16).ToArray();
var nativeType2 = Enumerable.Range(0, 72).Select(i => (byte)i).ToArray();
var nativeType6 = Enumerable.Range(0, 10).Select(i => (byte)(0x20 + i)).ToArray();
var dyn = new NativeHeroDynamicData(new[]
{
    new NativeHeroDynamicSection(2, nativeType2),
    new NativeHeroDynamicSection(6, nativeType6),
    new NativeHeroDynamicSection(7, nativeType7)
});
Assert(NativeHeroDbFrameCodec.TryEncodeDynamicData(dyn, out var originalDyn, out error), error);

var hero = new HeroObject();
Assert(ReferenceEquals(hero.m_HeroMagicList, hero.m_MagicList),
    "hero magic list is not the base combat list");
Equal(16, hero.m_UseItems.Length, "hero equipment slots before load");
Assert(NativeHeroRuntimeCodec.TryApply(hero, nativeRecord, dyn, out error), error);
var restoreMessage = hero.m_MsgList.Single(message =>
    message.wIdent == Grobal2.RM_10401);
var restoredSlaveInfo = restoreMessage.Payload as TSlaveInfo;
Assert(restoredSlaveInfo != null,
    "hero record +0x4694 did not queue a TSlaveInfo RM_10401 payload");
Equal("记录神兽", restoredSlaveInfo!.sSlaveName,
    "embedded hero slave name");
Equal(0x10203040, restoredSlaveInfo.nKillCount,
    "embedded hero slave kill count");
Equal(unchecked((int)0xFEDCBA98), restoredSlaveInfo.dwRoyaltySec,
    "embedded hero slave royalty dword");
Equal(0xFFFF, restoredSlaveInfo.nHP, "embedded hero slave HP WORD");
Equal(0xA1B2, restoredSlaveInfo.nMP, "embedded hero slave MP WORD");
Equal((byte)7, restoredSlaveInfo.btSlaveExpLevel,
    "embedded hero slave exp level");
Equal((byte)3, restoredSlaveInfo.btSlaveLevel,
    "embedded hero slave make level");
Equal("主人甲", hero.MasterName, "master name");
Equal("英雄乙", hero.m_sCharName, "hero name");
Equal((byte)2, hero.m_btJob, "native job mapping");
Equal(raw[NativeHeroDbFrameCodec.RaceOffset], hero.m_btRaceImg, "native race appearance");
Equal((byte)2, hero.HeroType, "hero type");
Equal((byte)7, hero.HeroRank, "hero rank");
// sub_6888FC @0x688A9C/0x688AA5 —— 模式来自存档，不是构造函数默认的 1=跟随。
Equal((byte)2, (byte)hero.m_btNativeHeroMode, "hero mode restored from record+0xA4");
Equal(unchecked((int)0x89ABCDEF), hero.m_nForceExp, "hero force experience");
Equal(0x10203040, hero.m_nForceLv, "hero force level");
Equal(900000, hero.m_nMaxForceExp, "hero force maximum after load");
Assert(hero.m_boNativeCommonInformationOption1,
    "hero common-information option 1 load");
Equal(87654321, hero.m_nNativeCommonInformationOption2,
    "hero common-information option 2 load");
Assert(!hero.m_boNativeCommonInformationOption3,
    "hero common-information option 3 load");
Equal(34567890, hero.m_Abil.MaxExp, "hero max exp must be level-derived");
Equal(240573, hero.m_WAbil.HP, "hero 32-bit HP");
Equal(131119, hero.m_WAbil.MP, "hero 32-bit MP");
Equal(16, hero.m_UseItems.Length, "hero equipment slots after load");
Equal((byte)1, hero.m_UseItems[0].Bind, "hero equipment bind load +0xB8");
Equal(1, hero.m_ItemList.Count, "hero bag count");
Equal(2, hero.m_MagicList.Count, "known hero magic count");
Assert(ReferenceEquals(hero.m_HeroMagicList, hero.m_MagicList),
    "hero magic list alias was lost during load");
Assert(NativeHeroRuntimeCodec.TryRename(hero, "英雄改", out error), error);
Equal("英雄改", hero.m_sCharName, "runtime rename");
Equal("英雄改", hero.NativeHeroState.Record.HeroName, "runtime fixed-record rename");
Equal((byte)0xCE, hero.NativeHeroState.Record.ToArray()[0x150],
    "runtime rename changed an unknown fixed byte");

hero.MasterName = "主人丙";
hero.m_sCharName = "英雄丁";
hero.m_nGold = 654321;
hero.m_Abil.Exp = 7654321;
hero.m_Abil.MaxExp = 9876543;
hero.m_boNativeCommonInformationOption1 = false;
hero.m_nNativeCommonInformationOption2 = -24680;
hero.m_boNativeCommonInformationOption3 = true;
hero.m_nForceExp = unchecked((int)0xFEDCBA98);
hero.m_nForceLv = 0x50607080;
hero.m_WAbil.HP = 245000;
hero.m_WAbil.MP = 135000;
hero.m_UseItems[0].Dura = 499;
hero.m_UseItems[0].NativeRecord[0xB8] = 0;
hero.m_UseItems[0].Bind = 1;
hero.m_UseItems[0].ys1 = 0x10203040;
hero.m_UseItems[0].ys2 = 2;
hero.m_UseItems[0].ys17 = 17;
hero.m_UseItems[0].jp1 = 21;
hero.m_UseItems[0].jp6 = 26;
hero.m_UseItems[0].pname = "hero-source";
hero.m_UseItems[0].desc1 = "hero-line-1";
hero.m_UseItems[0].sourceTime = "2026-07-15";
hero.m_ItemList[0].MakeIndex = 2222;
hero.m_ItemList[0].ys3 = 33;
hero.m_ItemList[0].jp4 = 44;
hero.m_ItemList[0].desc2 = "hero-bag";
hero.m_ItemList[0].killerName = "hero-killer";
hero.m_ItemList[0].mapName = "hero-map";
hero.m_btNativeHeroMode = (HeroObject.NativeHeroMode)0; // 攻击
hero.m_MagicList.Single(x => x.wMagIdx == 10).nTranPoint = 4444;
hero.m_MagicList.Single(x => x.wMagIdx == 69).nTranPoint = 9999;

Assert(NativeHeroRuntimeCodec.TryCreateSnapshot(hero, out var snapshot,
    out var snapshotDyn, out error), error);
var saved = snapshot.ToArray();
Assert(saved.AsSpan(NativeHeroDbFrameCodec.NativeSlaveRecordOffset,
        NativeHeroDbFrameCodec.NativeSlaveRecordSize).IndexOfAnyExcept((byte)0) < 0,
    "ordinary hero snapshot preserved a stale embedded slave record");
Equal((byte)2, saved[NativeHeroDbFrameCodec.JobOffset], "snapshot native job");
Equal(hero.m_btRaceImg, saved[NativeHeroDbFrameCodec.RaceOffset], "snapshot native race");
Equal((byte)0xCE, saved[0x150], "fixed unknown byte");
// sub_689034 @0x68910A/0x689110 —— 运行期模式必须回写记录 +0xA4。
Equal((byte)0, saved[NativeHeroDbFrameCodec.HeroModeOffset], "snapshot hero mode");
Equal(unchecked((int)0xFEDCBA98), snapshot.ForceExp,
    "snapshot hero force experience");
Equal(0x50607080, snapshot.ForceLv, "snapshot hero force level");
Equal((byte)0xA5, saved[NativeHeroDbFrameCodec.EquippedItemsOffset + 100],
    "equipment unknown tail");
Equal((byte)1, saved[NativeHeroDbFrameCodec.EquippedItemsOffset + 0xB8],
    "equipment bind save +0xB8");
Equal((byte)0xB6, saved[NativeHeroDbFrameCodec.EquippedItemsOffset
                       + 2 * NativeHeroDbFrameCodec.ItemRecordSize + 111],
    "empty equipment unknown tail");
Equal((byte)0xC7, saved[NativeHeroDbFrameCodec.BagItemsOffset + 120],
    "bag unknown tail");
Equal((byte)0xD8, saved[NativeHeroDbFrameCodec.NormalMagicOffset + 20],
    "known normal magic unknown tail");
Equal((byte)0xFA, saved[NativeHeroDbFrameCodec.SpecialMagicOffset + 25],
    "known special magic unknown tail");
Assert(saved.AsSpan(NativeHeroDbFrameCodec.NormalMagicOffset
                    + 5 * NativeHeroDbFrameCodec.MagicRecordSize,
        NativeHeroDbFrameCodec.MagicRecordSize).SequenceEqual(unknownNormalCopy),
    "unknown magic record changed");
Equal((ushort)499, BinaryPrimitives.ReadUInt16LittleEndian(saved.AsSpan(
    NativeHeroDbFrameCodec.EquippedItemsOffset + 6, 2)), "equipment durability patch");
Equal(2222, BinaryPrimitives.ReadInt32LittleEndian(saved.AsSpan(
    NativeHeroDbFrameCodec.BagItemsOffset, 4)), "bag item patch");
Equal(4444, BinaryPrimitives.ReadInt32LittleEndian(saved.AsSpan(
    NativeHeroDbFrameCodec.NormalMagicOffset + 12, 4)), "normal magic patch");
Equal(9999, BinaryPrimitives.ReadInt32LittleEndian(saved.AsSpan(
    NativeHeroDbFrameCodec.SpecialMagicOffset + 12, 4)), "special magic patch");
Equal((uint)245000, snapshot.Hp, "snapshot 32-bit HP");
Equal((uint)135000, snapshot.Mp, "snapshot 32-bit MP");
Assert(!snapshot.NativeCommonInformationOption1,
    "snapshot common-information option 1");
Equal(-24680, snapshot.NativeCommonInformationOption2,
    "snapshot common-information option 2");
Assert(snapshot.NativeCommonInformationOption3,
    "snapshot common-information option 3");
Assert(NativeHeroDbFrameCodec.TryEncodeDynamicData(snapshotDyn, out var savedDyn, out error), error);
Assert(!savedDyn.AsSpan().SequenceEqual(originalDyn),
    "eye sidecar was not added to dynamic data");
var savedType7 = snapshotDyn.Sections.Single(section => section.Type == 7).Payload;
Assert(savedType7.AsSpan(4, 16).SequenceEqual(nativeType7Record),
    "native type-7 record changed while adding eye carrier");
for (var offset = 4 + 16; offset < savedType7.Length; offset += 16)
    Equal((byte)0xFF, savedType7[offset + 12],
        "eye carrier selector must remain invisible to native jobs 0..2");
Assert(YanshenHeroDynamicCodec.TryExtract(snapshotDyn, 2,
    out var heroSidecar, out error), error);
Assert(heroSidecar.Length > 0, "hero eye sidecar payload is empty");

var switchHero = new HeroObject();
Assert(NativeHeroRuntimeCodec.TryApply(switchHero, nativeRecord, dyn, out error), error);
switchHero.m_boNativeSwitchData = true;
var switchSlave = new NativeHeroSlaveDeathProbe
{
    m_sCharName = "切服神兽",
    m_nKillMonCount = unchecked((int)0x89ABCDEF),
    m_btSlaveExpLevel = 9,
    m_btSlaveMakeLevel = 4,
    m_dwMasterRoyaltyTick = HUtil32.GetTickCount() + 120_000
};
switchSlave.m_WAbil.HP = -1;
switchSlave.m_WAbil.MP = 0x12345;
SetHeroSlave(switchHero, switchSlave);
Assert(NativeHeroRuntimeCodec.TryCreateSnapshot(switchHero,
    out var switchSnapshot, out _, out error), error);
var switchSlot = switchSnapshot.ToArray().AsSpan(
    NativeHeroDbFrameCodec.NativeSlaveRecordOffset,
    NativeHeroDbFrameCodec.NativeSlaveRecordSize);
Equal("切服神兽", ReadShortString(switchSlot, 15),
    "switch hero slave name");
Equal(unchecked((int)0x89ABCDEF),
    BinaryPrimitives.ReadInt32LittleEndian(switchSlot.Slice(0x10, 4)),
    "switch hero slave kill count");
var switchRoyalty = BinaryPrimitives.ReadUInt32LittleEndian(
    switchSlot.Slice(0x14, 4));
Assert(switchRoyalty is >= 119 and <= 120,
    "switch hero slave remaining royalty seconds");
Equal((ushort)0xFFFF, BinaryPrimitives.ReadUInt16LittleEndian(
    switchSlot.Slice(0x18, 2)), "switch hero slave HP low WORD");
Equal((ushort)0x2345, BinaryPrimitives.ReadUInt16LittleEndian(
    switchSlot.Slice(0x1A, 2)), "switch hero slave MP low WORD");
Equal((byte)9, switchSlot[0x1C], "switch hero slave exp level");
Equal((byte)4, switchSlot[0x1D], "switch hero slave make level");
Equal((byte)0, switchSlot[0x1E], "switch hero slave reserved +1E");
Equal((byte)0, switchSlot[0x1F], "switch hero slave reserved +1F");
Equal(0, switchSlave.DieCalls,
    "pure switch hero snapshot executed the deferred VMT+0x84 side effect");

foreach (var invalidKind in new[] { "death", "ghost" })
{
    var invalidHero = new HeroObject();
    Assert(NativeHeroRuntimeCodec.TryApply(invalidHero, nativeRecord, dyn,
        out error), error);
    invalidHero.m_boNativeSwitchData = true;
    var invalidSlave = new NativeHeroSlaveDeathProbe
    {
        m_sCharName = "invalid-" + invalidKind,
        m_boDeath = invalidKind == "death",
        m_boGhost = invalidKind == "ghost"
    };
    SetHeroSlave(invalidHero, invalidSlave);
    Assert(NativeHeroRuntimeCodec.TryCreateSnapshot(invalidHero,
        out var invalidSnapshot, out _, out error), error);
    Assert(invalidSnapshot.ToArray().AsSpan(
            NativeHeroDbFrameCodec.NativeSlaveRecordOffset,
            NativeHeroDbFrameCodec.NativeSlaveRecordSize)
        .IndexOfAnyExcept((byte)0) < 0,
        invalidKind + " switch hero slave record was not empty");
    Equal(0, invalidSlave.DieCalls,
        invalidKind + " switch hero slave was killed again");
}

var rejectedSaveHero = new HeroObject();
Assert(NativeHeroRuntimeCodec.TryApply(rejectedSaveHero, nativeRecord, dyn,
    out error), error);
rejectedSaveHero.m_sCharName = new string('A', 16);
rejectedSaveHero.m_boNativeSwitchData = true;
var rejectedSaveSlave = new NativeHeroSlaveDeathProbe
{
    m_sCharName = "编码失败神兽"
};
SetHeroSlave(rejectedSaveHero, rejectedSaveSlave);
Assert(!HeroDataService.QueueSave(rejectedSaveHero),
    "oversized hero name unexpectedly produced a save frame");
Equal(0, rejectedSaveSlave.DieCalls,
    "failed hero save encoding consumed the switch slave");

// The native writer calls Die after the fixed slot is built, but before the
// outbound save request is encoded or transported. DBService accepts
// a valid frame into its offline FIFO without opening a socket, so this probes
// the ordering without network or background threads.
M2Share.DataServer = null;
Assert(HeroDataService.QueueSave(switchHero),
    "hero switch save queue rejected a valid frame");
Equal(1, switchSlave.DieCalls,
    "hero switch slave Die must run even while DataServer is absent");
Assert(HeroDataService.QueueSave(switchHero),
    "ordinary save after switch frame was rejected");
Equal(1, switchSlave.DieCalls,
    "ordinary save repeated the switch slave Die side effect");
var saveDb = new DBService();
M2Share.DataServer = saveDb;
Equal(0, saveDb.PendingNativeSendCount,
    "hero switch save unexpectedly sent before FlushPendingSaves");
HeroDataService.FlushPendingSaves();
Equal(2, saveDb.PendingNativeSendCount,
    "same-hero switch and ordinary saves were not both retained in FIFO");
var pendingSendField = typeof(DBService).GetField("_pendingSends",
    BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new MissingFieldException(typeof(DBService).FullName,
        "_pendingSends");
var pendingFrames = ((System.Collections.Concurrent.ConcurrentQueue<byte[]>)
    pendingSendField.GetValue(saveDb)!).ToArray();
Assert(NativeHeroDbFrameCodec.TryDecodeSaveRequest(pendingFrames[0],
    out var queuedSwitchSave, out error), error);
Assert(NativeHeroDbFrameCodec.TryDecodeSaveRequest(pendingFrames[1],
    out var queuedOrdinarySave, out error), error);
Assert(queuedSwitchSave.Record.ToArray().AsSpan(
        NativeHeroDbFrameCodec.NativeSlaveRecordOffset,
        NativeHeroDbFrameCodec.NativeSlaveRecordSize)
    .IndexOfAnyExcept((byte)0) >= 0,
    "first FIFO save lost its embedded switch slave record");
Assert(queuedOrdinarySave.Record.ToArray().AsSpan(
        NativeHeroDbFrameCodec.NativeSlaveRecordOffset,
        NativeHeroDbFrameCodec.NativeSlaveRecordSize)
    .IndexOfAnyExcept((byte)0) < 0,
    "ordinary save after Die retained the embedded switch slave record");
M2Share.DataServer = null;
saveDb.Dispose();

var second = new HeroObject();
Assert(NativeHeroRuntimeCodec.TryApply(second, snapshot, snapshotDyn, out error), error);
Equal("英雄丁", second.m_sCharName, "round-trip hero name");
Equal(unchecked((int)0xFEDCBA98), second.m_nForceExp,
    "round-trip hero force experience");
Equal(0x50607080, second.m_nForceLv, "round-trip hero force level");
Equal(900000, second.m_nMaxForceExp,
    "round-trip hero force maximum after load");
Assert(!second.m_boNativeCommonInformationOption1,
    "round-trip common-information option 1");
Equal(-24680, second.m_nNativeCommonInformationOption2,
    "round-trip common-information option 2");
Assert(second.m_boNativeCommonInformationOption3,
    "round-trip common-information option 3");
Equal(34567890, second.m_Abil.MaxExp,
    "round-trip max exp must remain level-derived");
Equal(245000, second.m_WAbil.HP, "round-trip 32-bit HP");
Equal((byte)1, second.m_UseItems[0].Bind, "round-trip equipment bind");
Equal(2, second.m_MagicList.Count, "round-trip known magic count");
Equal(0x10203040, second.m_UseItems[0].ys1, "round-trip hero equipment ys1");
Equal((byte)17, second.m_UseItems[0].ys17, "round-trip hero equipment ys17");
Equal((byte)26, second.m_UseItems[0].jp6, "round-trip hero equipment jp6");
Equal("hero-source", second.m_UseItems[0].pname, "round-trip hero equipment pname");
Equal("hero-line-1", second.m_UseItems[0].desc1, "round-trip hero equipment desc1");
Equal("2026-07-15", second.m_UseItems[0].sourceTime,
    "round-trip hero equipment sourceTime");
Equal((byte)33, second.m_ItemList[0].ys3, "round-trip hero bag ys3");
Equal((byte)44, second.m_ItemList[0].jp4, "round-trip hero bag jp4");
Equal("hero-bag", second.m_ItemList[0].desc2, "round-trip hero bag desc2");
Equal("hero-killer", second.m_ItemList[0].killerName,
    "round-trip hero bag killerName");
Equal("hero-map", second.m_ItemList[0].mapName, "round-trip hero bag mapName");

var emptySlaveRaw = (byte[])raw.Clone();
emptySlaveRaw.AsSpan(NativeHeroDbFrameCodec.NativeSlaveRecordOffset,
    NativeHeroDbFrameCodec.NativeSlaveRecordSize).Clear();
Assert(NativeHeroDbFrameCodec.TryCreateRecord(emptySlaveRaw,
    out var emptySlaveRecord, out error), error);
var emptySlaveHero = new HeroObject();
Assert(NativeHeroRuntimeCodec.TryApply(emptySlaveHero, emptySlaveRecord,
    dyn, out error), error);
Assert(emptySlaveHero.m_MsgList.All(message =>
        message.wIdent != Grobal2.RM_10401),
    "zero-length embedded slave record queued RM_10401");

var corruptSlaveRaw = (byte[])raw.Clone();
corruptSlaveRaw[NativeHeroDbFrameCodec.NativeSlaveRecordOffset] = 16;
Assert(NativeHeroDbFrameCodec.TryCreateRecord(corruptSlaveRaw,
    out var corruptSlaveRecord, out error), error);
Assert(!NativeHeroRuntimeCodec.TryApply(new HeroObject(), corruptSlaveRecord,
        dyn, out error)
       && error.Contains("slave name", StringComparison.Ordinal),
    "oversized embedded hero slave ShortString was accepted");

Assert(YanshenHeroDynamicCodec.TryMerge(snapshotDyn, 0, heroSidecar,
    out var twoSlotDynamic, out error), error);
Assert(YanshenHeroDynamicCodec.TryExtract(twoSlotDynamic, 0,
        out var slot0Sidecar, out error) && slot0Sidecar.SequenceEqual(heroSidecar),
    "hero eye slot 0 was not preserved");
Assert(YanshenHeroDynamicCodec.TryExtract(twoSlotDynamic, 2,
        out var slot2Sidecar, out error) && slot2Sidecar.SequenceEqual(heroSidecar),
    "hero eye slot 2 was overwritten");
Assert(YanshenHeroDynamicCodec.TryMerge(twoSlotDynamic, 2, Array.Empty<byte>(),
    out var slot2Cleared, out error), error);
Assert(YanshenHeroDynamicCodec.TryExtract(slot2Cleared, 2,
        out slot2Sidecar, out error) && slot2Sidecar.Length == 0,
    "hero eye slot 2 carrier was not removed");
Assert(YanshenHeroDynamicCodec.TryExtract(slot2Cleared, 0,
        out slot0Sidecar, out error) && slot0Sidecar.SequenceEqual(heroSidecar),
    "clearing hero eye slot 2 damaged slot 0");

var corruptSections = snapshotDyn.Sections.Select(section =>
{
    var payload = (byte[])section.Payload.Clone();
    if (section.Type == 7) payload[4 + 16 + 16] ^= 1;
    return new NativeHeroDynamicSection(section.Type, payload);
}).ToArray();
Assert(!YanshenHeroDynamicCodec.TryExtract(new NativeHeroDynamicData(corruptSections),
        2, out _, out error) && error.Contains("trailer", StringComparison.Ordinal),
    "corrupt hero eye carrier CRC was accepted");

Assert(YanshenItemSidecarCodec.TryApply(Array.Empty<byte>(), second.m_UseItems,
        second.m_ItemList.ToArray(), Array.Empty<TUserItem>(), out error), error);
Assert(NativeHeroRuntimeCodec.TryCreateSnapshot(second, out var clearedSnapshot,
    out var clearedDynamic, out error), error);
var clearedHero = new HeroObject();
Assert(NativeHeroRuntimeCodec.TryApply(clearedHero, clearedSnapshot,
    clearedDynamic, out error), error);
Equal(0, clearedHero.m_UseItems[0].ys1, "cleared hero equipment eye data returned");
Equal((byte)0, clearedHero.m_ItemList[0].ys3, "cleared hero bag eye data returned");
Assert(clearedDynamic.Sections.Single(section => section.Type == 7).Payload
        .AsSpan(4, 16).SequenceEqual(nativeType7Record),
    "clearing hero eye carrier damaged native type-7 record");

var wideRaw = (byte[])raw.Clone();
WriteSplit(wideRaw, NativeHeroDbFrameCodec.HpLowOffset,
    NativeHeroDbFrameCodec.HpHighOffset, 65536);
Assert(NativeHeroDbFrameCodec.TryCreateRecord(wideRaw, out var wideRecord, out error), error);
var wideHero = new HeroObject();
Assert(NativeHeroRuntimeCodec.TryApply(wideHero, wideRecord, dyn, out error), error);
Equal(65536, wideHero.m_WAbil.HP, "native hero HP at first 32-bit value");

var overflowRaw = (byte[])raw.Clone();
WriteSplit(overflowRaw, NativeHeroDbFrameCodec.HpLowOffset,
    NativeHeroDbFrameCodec.HpHighOffset, 0x80000000);
Assert(NativeHeroDbFrameCodec.TryCreateRecord(overflowRaw, out var overflowRecord, out error), error);
Assert(!NativeHeroRuntimeCodec.TryApply(new HeroObject(), overflowRecord, dyn, out error)
       && error.Contains("HP/MP", StringComparison.Ordinal),
    "native HP above signed 32-bit range was accepted");

while (hero.m_ItemList.Count <= NativeHeroDbFrameCodec.BagItemCount)
    hero.m_ItemList.Add(new TUserItem { wIndex = 1 });
Assert(!NativeHeroRuntimeCodec.TryCreateSnapshot(hero, out _, out _, out error)
       && error.Contains("bag capacity", StringComparison.Ordinal),
    "oversized hero bag was truncated");

var equipmentOverflow = new HeroObject();
Assert(NativeHeroRuntimeCodec.TryApply(equipmentOverflow, snapshot, snapshotDyn, out error), error);
equipmentOverflow.m_UseItems = new TUserItem[17];
equipmentOverflow.m_UseItems[16] = new TUserItem { wIndex = 1 };
Assert(!NativeHeroRuntimeCodec.TryCreateSnapshot(equipmentOverflow, out _, out _, out error)
       && error.Contains("exactly 16", StringComparison.Ordinal),
    "oversized hero equipment was truncated");

var magicOverflow = new HeroObject();
Assert(NativeHeroRuntimeCodec.TryApply(magicOverflow, snapshot, snapshotDyn, out error), error);
for (var i = 0; i < 54; i++)
    magicOverflow.m_MagicList.Add(new TUserMagic { wMagIdx = (ushort)(1000 + i) });
Assert(!NativeHeroRuntimeCodec.TryCreateSnapshot(magicOverflow, out _, out _, out error)
       && error.Contains("magic capacity exceeded", StringComparison.Ordinal),
    "hero magic overflow ignored the reserved unknown record");

// EXP-06: the exp threshold tracks the level, it is not pinned at 100. The 100 written by
// 0x652479 (A block, ctor) and 0x6B1A3E (B block) is a fresh-object default -- 0x6B1988 guards
// it with `cmp word [obj+0x278],0 / jne`, so it only lands while the level is still 0. Both
// copies are then rewritten from the level table: 0x68720E stores GetLevelExp(A.Level) into
// A.MaxExp (+0x244), and the level-up loop calls [vtbl+0x240] at 0x687930 -- implemented at
// 0x6BDBD3 as B.MaxExp (+0x2C0) = table[B.Level] -- before 0x687936 re-reads it.
// A GM-forced level change therefore has to re-derive the threshold from the config table.
var setLevelHero = new HeroObject();
Assert(NativeHeroRuntimeCodec.TryApply(setLevelHero, snapshot, snapshotDyn, out error), error);
Equal(34567890, setLevelHero.m_Abil.MaxExp,
    "set-level precondition: load derives MaxExp from the level-77 table entry");
M2Share.g_Config.dwNeedExps[50] = 12345678;
Assert(setLevelHero.TrySetNativeLevel(50, out error), error);
Equal(12345678, setLevelHero.m_Abil.MaxExp,
    "EXP-06: TrySetNativeLevel must re-derive MaxExp from the config table for the new level");
Equal((ushort)50, setLevelHero.m_Abil.Level, "TrySetNativeLevel applied the level");

Console.WriteLine(
    "PASS hero-runtime fixed=49D4 slave=+4694/RM10401 equip=16 bag=40 bind=+B8 eye=type7-preserved magic=55+3 unknown=fixed,item,magic,dyn overflow=closed EXP-06=setlevel-rederives");

void WriteShortString(byte[] destination, int offset, int maximumLength, string value)
{
    var bytes = gbk.GetBytes(value);
    Assert(bytes.Length <= maximumLength, "test short string is oversized");
    destination.AsSpan(offset, maximumLength + 1).Clear();
    destination[offset] = (byte)bytes.Length;
    bytes.CopyTo(destination, offset + 1);
}

string ReadShortString(ReadOnlySpan<byte> source, int maximumLength)
{
    var length = source[0];
    Assert(length <= maximumLength, "test short string length");
    return gbk.GetString(source.Slice(1, length));
}

static void SetHeroSlave(HeroObject hero, TBaseObject slave)
{
    var field = typeof(HeroObject).GetField("m_NativeHeroSummonSlave",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(HeroObject).FullName,
            "m_NativeHeroSummonSlave");
    field.SetValue(hero, slave);
}

static void WriteSplit(byte[] destination, int lowOffset, int highOffset, uint value)
{
    BinaryPrimitives.WriteUInt16LittleEndian(destination.AsSpan(lowOffset, 2), (ushort)value);
    BinaryPrimitives.WriteUInt16LittleEndian(destination.AsSpan(highOffset, 2), (ushort)(value >> 16));
}

static void WriteItem(Span<byte> destination, int makeIndex, ushort itemIndex,
    ushort dura, ushort duraMax, byte flags)
{
    BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(0, 4), makeIndex);
    BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(4, 2), itemIndex);
    BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(6, 2), dura);
    BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(8, 2), duraMax);
    for (var i = 0; i < 14; i++) destination[10 + i] = (byte)(i + 1);
    destination[0x27] = flags;
}

static void WriteMagic(Span<byte> destination, ushort magicId, byte level, byte key, int train)
{
    BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(0, 2), magicId);
    destination[2] = level;
    destination[3] = key;
    BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(4, 4), train);
}

static void Equal<T>(T expected, T actual, string message) where T : IEquatable<T>
{
    if (!expected.Equals(actual))
        throw new InvalidOperationException($"{message}: expected={expected} actual={actual}");
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
    File.WriteAllText(Path.Combine(runtimeDirectory, "Command.conf"),
        "[Command]" + Environment.NewLine);
    var shareDirectory = Path.Combine(Path.GetFullPath(Path.Combine(runtimeDirectory, "..")), "Share");
    Directory.CreateDirectory(shareDirectory);
    File.WriteAllText(Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"),
        "[Integer]" + Environment.NewLine);

    // HeroObject.TrySetNativeLevel reaches TBaseObject.SendMsg, which locks
    // M2Share.ProcessMsgCriticalSection (TBaseObject.cs:3532). Only GameApp assigns it in
    // a real boot, so without it Monitor.Enter(null) threw and the level/exp assertions
    // never ran. Nothing is queued out of the process.
    M2Share.ProcessMsgCriticalSection ??= new object();
    M2Share.LogMsgCriticalSection ??= new object();
}

sealed class NativeHeroSlaveDeathProbe : TBaseObject
{
    public int DieCalls { get; private set; }

    public override void Die()
    {
        DieCalls++;
        m_boDeath = true;
    }
}
