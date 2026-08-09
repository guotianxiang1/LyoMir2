using System.Buffers.Binary;
using System.Reflection;
using DBSvr.Core;
using GameSvr;
using SystemModule;

PrepareRuntimeConfig();
InitializeRuntime();

VerifyPlayerBaselineAndEquipmentMerge();
VerifyExtendedCarrierBaseline();
VerifyRecoveredFixedBaseline();
VerifyHolyProjectionFailClosed();
VerifyCoreCarrierWidthAndReset();
VerifyCoreWorkingMergeAndProjection();
VerifyBaseMagicDamagePercentProducers();
VerifyHeroInitialApplyOrder();
VerifyMagicLevelBonusBaseline();
VerifyEmptyAndShortRecords();

Console.WriteLine(
    "PASS native fixed ability baseline player+hero raw-le " +
    "merge=ushort-wrap+max+or ids=21+158 separated=22+74 " +
    "fastness=76+102+103-wrap+removal-reset " +
    "extended=53+67+70+71+76+78+79+90+98-103+141 " +
    "recovered=recovery+luck+speed+drug+flags+117 " +
    "holy=fixed92+property31-local-only+packet-zero+no-damage " +
    "core=dword-width+merge+projection+overflow+removal+repeat " +
    "base-magic=mode-shape+outlook-byte+dura+wrap+reset " +
    "hero-order=state-before-recalc magic-level=0x138-low-byte+0x139-ignored " +
    "empty=null+short-reset+monster");
return;

static void VerifyExtendedCarrierBaseline()
{
    ResetItems();
    var raw = new byte[NativeHumanDataCodec.DataRecordSize];
    Write(raw, 0x118, 65535);
    Write(raw, 0x13A, 65535);
    raw[0x226] = 1;
    WriteInt(raw, 0x11C, -1);
    Write(raw, 0x14E, 65535);
    WriteInt(raw, 0x150, -1);
    raw[0x166] = 255;
    WriteInt(raw, 0x16C, 30000);
    WriteInt(raw, 0x170, -10);
    WriteInt(raw, 0x174, 100);
    WriteInt(raw, 0x178, 200);
    Write(raw, 0x148, 65535);
    Write(raw, 0x17C, 65535);
    Write(raw, 0x17E, 2);
    raw[0x1DA] = 250;

    AddStdItem((53, 2), (67, 2), (71, 2), (78, 2), (79, 2),
        (90, 2));
    AddStdItem((98, 1), (99, 2), (100, 3), (101, 4), (102, 2),
        (103, 65535));
    AddStdItem((76, 2));

    var player = NewPlayer("extended-fixed-carriers");
    player.m_NativeHumanData = raw;
    player.m_UseItems[0] = Item(1);
    player.m_UseItems[1] = Item(2);
    player.m_UseItems[2] = Item(3);
    player.RecalcAbilitys();

    Equal(1, Field<ushort>(player, "m_wNativeBreakThroughChance"),
        "fixed property53 baseline merge");
    Equal(1, Field<int>(player, "m_nNativeSteelBodyReduction"),
        "fixed property67 baseline merge");
    Assert(Field<bool>(player, "m_boNativeAwakening"),
        "fixed property70 baseline projection");
    Equal(1, Field<int>(player, "m_nNativeFlatMagicDamageIncrease"),
        "fixed property71 baseline merge");
    Equal(1, Field<int>(player, "m_nNativeGoldenBellReduction"),
        "fixed property78 baseline merge");
    Equal(1, Field<int>(player, "m_nNativeDragonBodyReduction"),
        "fixed property79 baseline merge");
    Equal(1, Field<byte>(player, "m_btNativeDamageIncreasePercent"),
        "fixed property90 baseline merge");
    Equal(30001, Field<short>(player, "m_sNativeCriticalChance"),
        "fixed property98 baseline merge");
    Equal(-8, Field<int>(player, "m_nNativeCriticalDamageIncrease"),
        "fixed property99 baseline merge");
    Equal(103, Field<short>(player, "m_sNativeAntiCriticalChance"),
        "fixed property100 baseline merge");
    Equal(204, Field<short>(player,
        "m_sNativeCriticalDamageReduction"),
        "fixed property101 baseline merge");
    Equal(1, player.m_nNativeHqFastness,
        "fixed property76 baseline merge");
    Equal(1, Field<int>(player, "m_nNativeMagicFastnessSelector"),
        "fixed property102 baseline merge");
    Equal(1, Field<int>(player, "m_nNativeSoulFastnessSelector"),
        "fixed property103 baseline merge");
    Equal(250, Field<byte>(player,
        "m_btNativeMagicDamageReductionPercent"),
        "fixed property141 baseline projection");

    player.m_UseItems[0] = null;
    player.m_UseItems[1] = null;
    player.m_UseItems[2] = null;
    player.RecalcAbilitys();
    Equal(65535, Field<ushort>(player,
        "m_wNativeBreakThroughChance"),
        "equipment removal lost fixed property53 baseline");
    Equal(65535, Field<int>(player, "m_nNativeSteelBodyReduction"),
        "equipment removal lost fixed property67 baseline");
    Equal(65535, player.m_nNativeHqFastness,
        "equipment removal lost fixed property76 baseline");
    Equal(65535, Field<int>(player, "m_nNativeMagicFastnessSelector"),
        "equipment removal lost fixed property102 baseline");
    Equal(2, Field<int>(player, "m_nNativeSoulFastnessSelector"),
        "equipment removal lost fixed property103 baseline");
}

static void VerifyRecoveredFixedBaseline()
{
    ResetItems();
    var raw = new byte[NativeHumanDataCodec.DataRecordSize];
    Write(raw, 0x86, 65534);
    Write(raw, 0x88, 1234);
    Write(raw, 0x8A, 2345);
    Write(raw, 0x8C, 65535);
    raw[0x8E] = 19;
    raw[0x8F] = 7;
    Write(raw, 0x92, 65535);
    Write(raw, 0x94, 54321);
    Write(raw, 0x140, 65535);
    Write(raw, 0x142, 10);
    Write(raw, 0x144, 20);
    raw[0x202] = 1;
    raw[0x203] = 1;
    raw[0x204] = 1;
    raw[0x20B] = 1;
    raw[0x218] = 4;
    raw[0x21B] = 1;
    raw[0x226] = 1;

    var equipment = AddStdItem((117, 3), (117, 7), (31, 2));
    equipment.NativeDrugHealthBonus = 1;
    equipment.NativeDrugSpellBonus = 2;
    equipment.NativeDrugJobBonus = 3;

    var player = NewPlayer("recovered-fixed-baseline");
    player.m_NativeHumanData = raw;
    player.m_UseItems[0] = Item(1);
    player.RecalcAbilitys();

    Equal(65534, player.m_nPoisonRecover, "fixed poison recovery");
    Equal(1234, player.m_nHealthRecover, "fixed health recovery");
    Equal(2345, player.m_nSpellRecover, "fixed spell recovery");
    Equal(0, player.m_nAntiMagic, "fixed anti-magic UInt16 wrap");
    Equal(12, player.m_nLuck, "fixed luck minus curse");
    Equal(0, player.m_AddAbil.btUndead,
        "fixed raw 0x92/property31 leaked into live holy carrier");
    Equal(54321, Field<ushort>(player, "m_nHitSpeed"),
        "fixed attack speed");
    Equal(0, player.m_wNativeDrugHealthBonus,
        "fixed plus equipment health drug wrap");
    Equal(12, player.m_wNativeDrugSpellBonus,
        "fixed plus equipment spell drug");
    Equal(23, player.m_wNativeDrugJobBonus,
        "fixed plus equipment job drug");
    Assert(Field<bool>(player, "m_boNativeAwakening"),
        "fixed awakening raw 0x226");
    Assert(player.m_boMagicShield, "fixed standard shield");
    Assert(Field<bool>(player, "m_boNativeHalfMagicShield"),
        "fixed half shield");
    Assert(Field<bool>(player, "m_boNativeFullMagicShield"),
        "fixed full shield");
    Assert(Field<bool>(player, "m_boNativeUserMove"),
        "fixed user move");
    Assert(player.m_boProbeNecklace, "fixed search-human flag");
    Equal(7, Field<byte>(player, "m_btNativeDragonPossessionLevel"),
        "property117 max projection");
    Assert(player.HasNativeActiveState(5),
        "property117 did not publish active state 5");

    player.m_UseItems[0] = null;
    player.RecalcAbilitys();
    Equal(65535, player.m_wNativeDrugHealthBonus,
        "equipment removal lost fixed health drug baseline");
    Equal(0, player.m_AddAbil.btUndead,
        "fixed raw 0x92 leaked after equipment removal");
    Equal(4, Field<byte>(player, "m_btNativeDragonPossessionLevel"),
        "equipment removal lost fixed property117 baseline");

    raw[0x76] = 1;
    raw[0x202] = 0;
    raw[0x203] = 0;
    raw[0x204] = 0;
    raw[0x20B] = 0;
    raw[0x218] = 0;
    raw[0x21B] = 0;
    raw[0x226] = 0;
    player.RecalcAbilitys();
    Assert(!Field<bool>(player, "m_boNativeAwakening"),
        "legacy wrong raw 0x76 still triggers awakening");
    Assert(!player.m_boMagicShield &&
           !Field<bool>(player, "m_boNativeHalfMagicShield") &&
           !Field<bool>(player, "m_boNativeFullMagicShield") &&
           !Field<bool>(player, "m_boNativeUserMove") &&
           !player.m_boProbeNecklace,
        "fixed flag removal did not reset projections");
    Equal(0, Field<byte>(player, "m_btNativeDragonPossessionLevel"),
        "fixed property117 removal did not reset level");
    Assert(!player.HasNativeActiveState(5),
        "property117 removal did not clear active state 5");
}

static void VerifyHolyProjectionFailClosed()
{
    ResetItems();
    var raw = new byte[NativeHumanDataCodec.DataRecordSize];
    Write(raw, 0x92, ushort.MaxValue);
    AddStdItem((31, ushort.MaxValue));

    var player = NewPlayer("holy-projection-player");
    player.m_NativeHumanData = raw;
    player.m_UseItems[0] = Item(1);
    player.RecalcAbilitys();
    Equal(0, player.m_AddAbil.btUndead,
        "player fixed raw/property31 live projection");

    var hero = new HeroObject
    {
        m_sCharName = "holy-projection-hero"
    };
    hero.m_UseItems[0] = Item(1);
    hero.RecalcAbilitys();
    Equal(0, hero.m_AddAbil.btUndead,
        "hero property31 live projection");

    player.m_AddAbil.btUndead = ushort.MaxValue;
    hero.m_AddAbil.btUndead = ushort.MaxValue;
    Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(
        BuildAbilityPacket(player).AsSpan(0x7C, sizeof(ushort))),
        "player packet +0x7C");
    Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(
        BuildHeroAbilityPacket(hero).AsSpan(0x7C, sizeof(ushort))),
        "hero packet +0x7C");

    var defender = NewPlayer("holy-projection-defender");
    defender.m_btLifeAttrib = Grobal2.LA_UNDEAD;
    defender.m_WAbil.AC = 0;
    defender.m_WAbil.MAC = 0;
    defender.m_AddAbil.btUndead = ushort.MaxValue;
    Equal(100, defender.GetHitStruckDamage(player, 100),
        "physical damage consumed holy carrier");
    Equal(100, defender.GetMagStruckDamage(player, 100),
        "magic damage consumed holy carrier");
}

static void VerifyCoreCarrierWidthAndReset()
{
    ResetItems();
    var raw = new byte[NativeHumanDataCodec.DataRecordSize];
    WriteInt(raw, 0x48, int.MaxValue);
    WriteInt(raw, 0x4C, int.MinValue);
    Write(raw, 0x50, ushort.MaxValue);
    Write(raw, 0x52, 0x8000);
    WriteInt(raw, 0x54, -1);
    WriteInt(raw, 0x58, 0x01020304);
    WriteInt(raw, 0x5C, int.MinValue + 1);
    WriteInt(raw, 0x60, int.MaxValue - 1);
    WriteInt(raw, 0x64, unchecked((int)0x89ABCDEF));
    WriteInt(raw, 0x68, 0x12345678);
    WriteInt(raw, 0x6C, -2);
    WriteInt(raw, 0x70, 2);
    WriteInt(raw, 0x74, -3);
    WriteInt(raw, 0x78, 3);
    WriteInt(raw, 0x7C, -4);
    WriteInt(raw, 0x80, 4);

    var player = NewPlayer("fixed-core-width");
    player.m_NativeHumanData = raw;
    player.RecalcAbilitys();

    Equal(int.MaxValue, CoreField<int>(player, "MaxHP"), "core MaxHP dword");
    Equal(int.MinValue, CoreField<int>(player, "MaxMP"), "core MaxMP dword");
    Equal(ushort.MaxValue, CoreField<int>(player, "HitPoint"),
        "core hit dword working carrier");
    Equal(0x8000, CoreField<int>(player, "SpeedPoint"),
        "core agility dword working carrier");
    Equal(-1, CoreField<int>(player, "ACLow"), "core AC low dword");
    Equal(0x01020304, CoreField<int>(player, "ACHigh"),
        "core AC high dword");
    Equal(int.MinValue + 1, CoreField<int>(player, "MACLow"),
        "core MAC low dword");
    Equal(int.MaxValue - 1, CoreField<int>(player, "MACHigh"),
        "core MAC high dword");
    Equal(unchecked((int)0x89ABCDEF), CoreField<int>(player, "DCLow"),
        "core DC low dword");
    Equal(0x12345678, CoreField<int>(player, "DCHigh"),
        "core DC high dword");
    Equal(-2, CoreField<int>(player, "MCLow"), "core MC low dword");
    Equal(2, CoreField<int>(player, "MCHigh"), "core MC high dword");
    Equal(-3, CoreField<int>(player, "SCLow"), "core SC low dword");
    Equal(3, CoreField<int>(player, "SCHigh"), "core SC high dword");
    Equal(-4, CoreField<int>(player, "CCLow"), "core CC low dword");
    Equal(4, CoreField<int>(player, "CCHigh"), "core CC high dword");

    player.m_NativeHumanData = Array.Empty<byte>();
    player.RecalcAbilitys();
    Equal(0, CoreField<int>(player, "MaxHP"),
        "core carrier did not reset without an exact record");
}

static void VerifyCoreWorkingMergeAndProjection()
{
    ResetItems();
    var raw = new byte[NativeHumanDataCodec.DataRecordSize];
    WriteInt(raw, 0x48, 1000);
    WriteInt(raw, 0x4C, 2000);
    Write(raw, 0x50, 300);
    Write(raw, 0x52, 400);
    WriteInt(raw, 0x54, 10);
    WriteInt(raw, 0x58, 20);
    WriteInt(raw, 0x5C, 30);
    WriteInt(raw, 0x60, 40);
    WriteInt(raw, 0x64, 50);
    WriteInt(raw, 0x68, 60);
    WriteInt(raw, 0x6C, 70);
    WriteInt(raw, 0x70, 80);
    WriteInt(raw, 0x74, 90);
    WriteInt(raw, 0x78, 100);
    WriteInt(raw, 0x7C, 110);
    WriteInt(raw, 0x80, 120);

    AddCoreItem(new GoodItem
    {
        ItemType = GoodType.ITEM_ARMOR,
        Ac = 1,
        Ac2 = 2,
        Mac = 3,
        Mac2 = 4,
        Dc = 5,
        Dc2 = 6,
        Mc = 7,
        Mc2 = 8,
        Sc = 9,
        Sc2 = 10,
        Cc = 11,
        Cc2 = 12
    }, (1, 100), (2, 200), (7, 300), (8, 400), (9, 500),
        (10, 600));
    AddCoreItem(new GoodItem
    {
        ItemType = GoodType.ITEM_WEAPON,
        Ac2 = 13
    }, (3, 30), (4, 40), (5, 50), (6, 60), (13, 70), (14, 80));
    AddCoreItem(new GoodItem
    {
        ItemType = GoodType.ITEM_ACCESSORY,
        StdMode = 20,
        Ac2 = 14,
        Mac2 = 15
    }, (11, 700), (12, 800), (111, 90), (112, 100));
    AddCoreItem(new GoodItem
    {
        ItemType = GoodType.ITEM_ACCESSORY,
        StdMode = 63,
        Ac = 16,
        Ac2 = 17
    });

    var control = new CoreRecordActor(Array.Empty<byte>());
    control.RecalcAbilitys();
    ushort baseHit = control.m_btHitPoint;
    ushort baseSpeed = control.m_wSpeedPoint;
    byte baseSpeedByte = control.m_btSpeedPoint;

    var player = new CoreRecordActor(raw);
    player.m_Abil.MaxHP = 7;
    player.m_Abil.MaxMP = 8;
    player.m_Abil.AC = PackEndpoints(1, 2);
    player.m_Abil.MAC = PackEndpoints(3, 4);
    player.m_Abil.DC = PackEndpoints(5, 6);
    player.m_Abil.MC = PackEndpoints(7, 8);
    player.m_Abil.SC = PackEndpoints(9, 10);
    for (ushort index = 1; index <= 4; index++)
        player.m_UseItems[index - 1] = Item(index);

    player.RecalcAbilitys();
    AssertCoreWorking(player, 1716, 2817, 397, 495,
        311, 422, 533, 644, 155, 266, 107, 128, 149, 170,
        211, 232, "merged");
    Equal(PackEndpoints(1, 2), player.m_Abil.AC,
        "base AC changed during recalc");
    Equal(1723, player.m_WAbil.MaxHP, "projected MaxHP");
    Equal(2825, player.m_WAbil.MaxMP, "projected MaxMP");
    Equal(PackEndpoints(312, 424), player.m_WAbil.AC, "projected AC");
    Equal(PackEndpoints(536, 648), player.m_WAbil.MAC, "projected MAC");
    Equal(PackEndpoints(160, 272), player.m_WAbil.DC, "projected DC");
    Equal(PackEndpoints(114, 136), player.m_WAbil.MC, "projected MC");
    Equal(PackEndpoints(158, 180), player.m_WAbil.SC, "projected SC");
    Equal(unchecked((ushort)(baseHit + 397)), player.m_btHitPoint,
        "projected hit");
    Equal(unchecked((ushort)(baseSpeed + 495)), player.m_wSpeedPoint,
        "projected agility word");
    Equal(unchecked((byte)(baseSpeedByte + 495)), player.m_btSpeedPoint,
        "projected agility byte view");

    player.RecalcAbilitys();
    AssertCoreWorking(player, 1716, 2817, 397, 495,
        311, 422, 533, 644, 155, 266, 107, 128, 149, 170,
        211, 232, "repeat");
    Equal(1723, player.m_WAbil.MaxHP, "repeat projected MaxHP");
    Equal(PackEndpoints(160, 272), player.m_WAbil.DC,
        "repeat projected DC");
    Equal(unchecked((ushort)(baseHit + 397)), player.m_btHitPoint,
        "repeat projected hit");

    Array.Clear(player.m_UseItems, 0, player.m_UseItems.Length);
    player.RecalcAbilitys();
    AssertCoreWorking(player, 1000, 2000, 300, 400,
        10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120,
        "removed");
    Equal(1007, player.m_WAbil.MaxHP, "removed projected MaxHP");
    Equal(PackEndpoints(55, 66), player.m_WAbil.DC,
        "removed projected DC");
    Equal(unchecked((ushort)(baseHit + 300)), player.m_btHitPoint,
        "removed projected hit");

    VerifyCoreWorkingOverflow();
}

static void VerifyCoreWorkingOverflow()
{
    ResetItems();
    var raw = new byte[NativeHumanDataCodec.DataRecordSize];
    WriteInt(raw, 0x48, int.MaxValue);
    WriteInt(raw, 0x64, int.MaxValue);
    Write(raw, 0x50, ushort.MaxValue);
    AddCoreItem(new GoodItem
    {
        ItemType = GoodType.ITEM_ETC,
        Dc = 1
    }, (11, 1), (13, 1));

    var control = new CoreRecordActor(Array.Empty<byte>());
    control.RecalcAbilitys();
    ushort baseHit = control.m_btHitPoint;

    var player = new CoreRecordActor(raw);
    player.m_Abil.MaxHP = 0;
    player.m_Abil.DC = 0;
    player.m_UseItems[0] = Item(1);
    player.RecalcAbilitys();
    Equal(int.MinValue, CoreField<int>(player, "MaxHP"),
        "working MaxHP int overflow");
    Equal(int.MinValue, CoreField<int>(player, "DCLow"),
        "working DC int overflow");
    Equal(65536, CoreField<int>(player, "HitPoint"),
        "working hit retained above UInt16");
    Equal(int.MinValue, player.m_WAbil.MaxHP,
        "projected MaxHP int overflow");
    Equal(0, HUtil32.LoWord(player.m_WAbil.DC),
        "projected DC truncates once at protocol boundary");
    Equal(baseHit, player.m_btHitPoint, "projected hit single wrap");

    player.RecalcAbilitys();
    Equal(int.MinValue, CoreField<int>(player, "MaxHP"),
        "repeat working MaxHP overflow");
    Equal(baseHit, player.m_btHitPoint, "repeat projected hit overflow");

    player.m_UseItems[0] = null;
    player.RecalcAbilitys();
    Equal(int.MaxValue, CoreField<int>(player, "MaxHP"),
        "removed working MaxHP baseline");
    Equal(int.MaxValue, CoreField<int>(player, "DCLow"),
        "removed working DC baseline");
    Equal(ushort.MaxValue, CoreField<int>(player, "HitPoint"),
        "removed working hit baseline");
    Equal(int.MaxValue, player.m_WAbil.MaxHP,
        "removed projected MaxHP baseline");
    Equal(M2Share.MAXHUMPOWER, HUtil32.LoWord(player.m_WAbil.DC),
        "removed projected DC post-cap baseline");
    Equal(unchecked((ushort)(baseHit + ushort.MaxValue)),
        player.m_btHitPoint, "removed projected hit baseline");
}

static void AssertCoreWorking(TBaseObject actor, int maxHp, int maxMp,
    int hit, int speed, int acLow, int acHigh, int macLow, int macHigh,
    int dcLow, int dcHigh, int mcLow, int mcHigh, int scLow, int scHigh,
    int ccLow, int ccHigh, string context)
{
    Equal(maxHp, CoreField<int>(actor, "MaxHP"), context + " MaxHP");
    Equal(maxMp, CoreField<int>(actor, "MaxMP"), context + " MaxMP");
    Equal(hit, CoreField<int>(actor, "HitPoint"), context + " hit");
    Equal(speed, CoreField<int>(actor, "SpeedPoint"), context + " speed");
    Equal(acLow, CoreField<int>(actor, "ACLow"), context + " AC low");
    Equal(acHigh, CoreField<int>(actor, "ACHigh"), context + " AC high");
    Equal(macLow, CoreField<int>(actor, "MACLow"), context + " MAC low");
    Equal(macHigh, CoreField<int>(actor, "MACHigh"), context + " MAC high");
    Equal(dcLow, CoreField<int>(actor, "DCLow"), context + " DC low");
    Equal(dcHigh, CoreField<int>(actor, "DCHigh"), context + " DC high");
    Equal(mcLow, CoreField<int>(actor, "MCLow"), context + " MC low");
    Equal(mcHigh, CoreField<int>(actor, "MCHigh"), context + " MC high");
    Equal(scLow, CoreField<int>(actor, "SCLow"), context + " SC low");
    Equal(scHigh, CoreField<int>(actor, "SCHigh"), context + " SC high");
    Equal(ccLow, CoreField<int>(actor, "CCLow"), context + " CC low");
    Equal(ccHigh, CoreField<int>(actor, "CCHigh"), context + " CC high");
}

static void VerifyPlayerBaselineAndEquipmentMerge()
{
    ResetItems();
    var raw = new byte[NativeHumanDataCodec.DataRecordSize];
    Write(raw, 0x84, 65535);
    Write(raw, 0xA8, 5);
    Write(raw, 0xAA, 79);
    Write(raw, 0xFE, 65534);
    Write(raw, 0x100, 123);
    Write(raw, 0x160, 10);
    Write(raw, 0x1F6, 65535);
    raw[0x1FC] = 1;
    raw[0x1FF] = 2;

    var player = NewPlayer("fixed-baseline-player");
    player.m_NativeHumanData = raw;
    player.RecalcAbilitys();

    Equal(65535, player.m_wEffectResistance, "player raw anti-poison");
    Equal(255, player.m_btAntiPoison, "player raw anti-poison byte projection");
    Equal(65534, player.m_wEffectStrength, "player raw effect strength");
    Equal(123, Field<ushort>(player, "m_wNativeBaseMagicDamagePercent"),
        "player raw base magic percent or 0xFE/0x100 slot isolation");
    Equal(5, Field<int>(player, "m_nNativeMagicHitHealAmount"),
        "player raw magic-hit heal amount");
    Equal(79, Field<int>(player, "m_nNativeMagicHitHealChance"),
        "player raw magic-hit heal chance");
    Equal(10, Field<ushort>(player, "m_wNativeState26DeadlineBonus"),
        "player raw state26 deadline");
    Equal(65535, Field<ushort>(player, "m_wNativeType74MagicHit"),
        "player raw magic-hit numeric");
    Assert(Field<bool>(player, "m_boNativeState26DirectStrong"),
        "player raw direct-strong flag");
    Assert(!Field<bool>(player, "m_boNativeState26DirectWeak"),
        "player raw direct-weak flag");
    Assert(!Field<bool>(player, "m_boNativeState26SingleStrong"),
        "player raw single-strong flag");
    Assert(Field<bool>(player, "m_boNativeState26SingleWeak"),
        "player raw single-weak flag");

    AddStdItem((30, 2), (54, 3), (86, 7), (86, 12),
        (158, 2), (74, 400));
    AddStdItem((21, 3), (21, 65535), (22, 65000));
    AddStdItem((254, 1), (254, 6));
    AddStdItem((102, 65535), (102, 2), (103, 65534), (103, 3));
    player.m_UseItems[0] = Item(1);
    player.m_UseItems[1] = Item(2);
    player.m_UseItems[2] = Item(3);
    player.m_UseItems[3] = Item(4);
    player.RecalcAbilitys();

    Equal(1, player.m_wEffectResistance, "anti-poison UInt16 wrap");
    Equal(1, player.m_btAntiPoison, "anti-poison wrapped byte projection");
    Equal(1, player.m_wEffectStrength, "effect-strength UInt16 wrap");
    Equal(123, Field<ushort>(player, "m_wNativeBaseMagicDamagePercent"),
        "equipment merge changed raw base magic percent");
    Equal(7, Field<int>(player, "m_nNativeMagicHitHealAmount"),
        "raw A8 plus ID21 UInt16 wrap");
    Equal(79, Field<int>(player, "m_nNativeMagicHitHealChance"),
        "ID22 changed raw AA chance");
    Equal(12, Field<ushort>(player, "m_wNativeState26DeadlineBonus"),
        "state26 deadline max merge");
    Equal(1, Field<ushort>(player, "m_wNativeType74MagicHit"),
        "ID158 wrap or ID74 numeric separation");
    Assert(Field<bool>(player, "m_boNativeState26DirectStrong"),
        "raw direct-strong lost during merge");
    Assert(Field<bool>(player, "m_boNativeState26DirectWeak"),
        "equipment direct-weak OR merge");
    Assert(Field<bool>(player, "m_boNativeState26SingleStrong"),
        "equipment single-strong OR merge");
    Assert(Field<bool>(player, "m_boNativeState26SingleWeak"),
        "raw single-weak lost during merge");
    Equal(1, Field<int>(player, "m_nNativeMagicFastnessSelector"),
        "property102 UInt16 wrap");
    Equal(1, Field<int>(player, "m_nNativeSoulFastnessSelector"),
        "property103 UInt16 wrap");

    player.m_UseItems[3] = null;
    player.RecalcAbilitys();
    Equal(0, Field<int>(player, "m_nNativeMagicFastnessSelector"),
        "property102 removal did not reset selector");
    Equal(0, Field<int>(player, "m_nNativeSoulFastnessSelector"),
        "property103 removal did not reset selector");
}

static void VerifyBaseMagicDamagePercentProducers()
{
    ResetItems();
    AddBaseMagicStdItem(10, 43, 2, 65535);
    AddBaseMagicStdItem(11, 43, 0x102, 2);
    AddBaseMagicStdItem(7, 7, 0, 3);
    AddBaseMagicStdItem(10, 43, 3, 100);
    AddBaseMagicStdItem(9, 43, 2, 100);
    AddBaseMagicStdItem(10, 42, 2, 100);
    AddBaseMagicStdItem(7, 7, 0, 100);

    var player = NewPlayer("base-magic-percent-player");
    for (ushort index = 1; index <= 6; index++)
        player.m_UseItems[index - 1] = Item(index);
    player.m_UseItems[6] = Item(7, 0);
    player.RecalcAbilitys();

    Equal(4, Field<ushort>(player, "m_wNativeBaseMagicDamagePercent"),
        "base magic percent producer conditions or UInt16 wrap");

    for (var index = 0; index < 7; index++)
        player.m_UseItems[index] = null;
    player.RecalcAbilitys();
    Equal(0, Field<ushort>(player, "m_wNativeBaseMagicDamagePercent"),
        "base magic percent removal did not reset");
}

static void VerifyHeroInitialApplyOrder()
{
    ResetItems();
    var raw = new byte[NativeHeroDbFrameCodec.HeroRecordSize];
    raw[NativeHeroDbFrameCodec.MasterNameOffset] = 1;
    raw[NativeHeroDbFrameCodec.MasterNameOffset + 1] = (byte)'M';
    raw[NativeHeroDbFrameCodec.HeroNameOffset] = 1;
    raw[NativeHeroDbFrameCodec.HeroNameOffset + 1] = (byte)'H';
    raw[NativeHeroDbFrameCodec.RaceOffset] = Grobal2.RC_HEROOBJECT;
    raw[NativeHeroDbFrameCodec.HeroTypeOffset] = 1;
    Write(raw, 0x84, 321);
    Write(raw, 0xA8, 11);
    Write(raw, 0xAA, 22);
    Write(raw, 0xFE, 654);
    Write(raw, 0x100, 432);
    Write(raw, 0x160, 777);
    Write(raw, 0x1F6, 888);
    raw[0x138] = 0xFE;
    raw[0x139] = 0xA5;
    raw[0x1FC] = 1;
    raw[0x1FD] = 2;
    raw[0x1FE] = 0;
    raw[0x1FF] = 3;

    Assert(NativeHeroDbFrameCodec.TryCreateRecord(raw,
        out var record, out var error), error);
    var hero = new HeroObject();
    var dynamicData = new NativeHeroDynamicData(
        Array.Empty<NativeHeroDynamicSection>());
    Assert(NativeHeroRuntimeCodec.TryApply(hero, record, dynamicData,
        out error), error);

    Equal(321, hero.m_wEffectResistance,
        "hero first RecalcAbilitys missed fixed state");
    Equal(654, hero.m_wEffectStrength,
        "hero first RecalcAbilitys missed effect strength");
    Equal(432, Field<ushort>(hero, "m_wNativeBaseMagicDamagePercent"),
        "hero raw base magic percent or 0xFE/0x100 slot isolation");
    Equal(11, Field<int>(hero, "m_nNativeMagicHitHealAmount"),
        "hero raw magic-hit heal amount");
    Equal(22, Field<int>(hero, "m_nNativeMagicHitHealChance"),
        "hero raw magic-hit heal chance");
    Equal(777, Field<ushort>(hero, "m_wNativeState26DeadlineBonus"),
        "hero raw state26 deadline");
    Equal(888, Field<ushort>(hero, "m_wNativeType74MagicHit"),
        "hero raw magic-hit numeric");
    Equal(0xFE, hero.NativeMagicLevelBonus,
        "hero raw magic level bonus uses fixed 0x138 low byte");
    Assert(Field<bool>(hero, "m_boNativeState26DirectStrong"),
        "hero raw direct-strong flag");
    Assert(Field<bool>(hero, "m_boNativeState26DirectWeak"),
        "hero raw direct-weak flag");
    Assert(!Field<bool>(hero, "m_boNativeState26SingleStrong"),
        "hero zero single-strong flag");
    Assert(Field<bool>(hero, "m_boNativeState26SingleWeak"),
        "hero raw single-weak flag");
}

static void VerifyMagicLevelBonusBaseline()
{
    ResetItems();
    var raw = new byte[NativeHumanDataCodec.DataRecordSize];
    raw[0x138] = 0xFE;
    raw[0x139] = 0xA5;

    var player = NewPlayer("magic-level-bonus");
    player.m_NativeHumanData = raw;
    player.RecalcAbilitys();
    Equal(0xFE, player.NativeMagicLevelBonus,
        "fixed 0x138 low-byte magic level bonus");

    raw[0x138] = 0;
    raw[0x139] = 0x7F;
    player.RecalcAbilitys();
    Equal(0, player.NativeMagicLevelBonus,
        "fixed 0x139 high byte does not project");
}

static void VerifyEmptyAndShortRecords()
{
    ResetItems();
    var noRecord = NewPlayer("null-fixed-record");
    noRecord.RecalcAbilitys();
    AssertBaselineEmpty(noRecord, "null player");

    var shortPlayer = NewPlayer("short-fixed-record");
    shortPlayer.m_NativeHumanData = new byte[
        NativeHumanDataCodec.DataRecordSize - 1];
    Write(shortPlayer.m_NativeHumanData, 0x84, 1234);
    shortPlayer.NativeMagicLevelBonus = 0x7F;
    shortPlayer.RecalcAbilitys();
    AssertBaselineEmpty(shortPlayer, "short player");

    var shortActor = new ShortRecordActor();
    shortActor.NativeMagicLevelBonus = 0x7F;
    shortActor.RecalcAbilitys();
    AssertBaselineEmpty(shortActor, "short base actor");

    var monster = new TBaseObject();
    monster.RecalcAbilitys();
    AssertBaselineEmpty(monster, "monster/base actor");
}

static void AssertBaselineEmpty(TBaseObject actor, string context)
{
    Equal(0, actor.m_wEffectResistance, context + " anti-poison");
    Equal(0, actor.m_wEffectStrength, context + " effect strength");
    Equal(0, Field<int>(actor, "m_nNativeMagicHitHealAmount"),
        context + " magic-hit heal amount");
    Equal(0, Field<int>(actor, "m_nNativeMagicHitHealChance"),
        context + " magic-hit heal chance");
    Equal(0, Field<ushort>(actor, "m_wNativeState26DeadlineBonus"),
        context + " state26 deadline");
    Equal(0, Field<ushort>(actor, "m_wNativeType74MagicHit"),
        context + " magic-hit numeric");
    Equal(0, Field<ushort>(actor, "m_wNativeBaseMagicDamagePercent"),
        context + " base magic damage percent");
    Equal(0, actor.NativeMagicLevelBonus,
        context + " magic level bonus");
    Equal(0, Field<int>(actor, "m_nNativeMagicFastnessSelector"),
        context + " magic fastness selector");
    Equal(0, Field<int>(actor, "m_nNativeSoulFastnessSelector"),
        context + " soul fastness selector");
    Equal(0, actor.m_nPoisonRecover, context + " poison recovery");
    Equal(0, actor.m_nHealthRecover, context + " health recovery");
    Equal(0, actor.m_nSpellRecover, context + " spell recovery");
    Equal(1, actor.m_nAntiMagic, context + " anti-magic default");
    Equal(0, actor.m_nLuck, context + " luck");
    Equal(0, actor.m_AddAbil.btUndead, context + " holy");
    Equal(0, Field<ushort>(actor, "m_nHitSpeed"),
        context + " attack speed");
    Equal(0, actor.m_wNativeDrugHealthBonus, context + " health drug");
    Equal(0, actor.m_wNativeDrugSpellBonus, context + " spell drug");
    Equal(0, actor.m_wNativeDrugJobBonus, context + " job drug");
    Assert(!actor.m_boMagicShield, context + " standard shield");
    Assert(!Field<bool>(actor, "m_boNativeHalfMagicShield"),
        context + " half shield");
    Assert(!Field<bool>(actor, "m_boNativeFullMagicShield"),
        context + " full shield");
    Assert(!Field<bool>(actor, "m_boNativeUserMove"),
        context + " user move");
    Assert(!actor.m_boProbeNecklace, context + " search human");
    Equal(0, Field<byte>(actor, "m_btNativeDragonPossessionLevel"),
        context + " property117");
    Assert(!actor.HasNativeActiveState(5), context + " active state 5");
    Assert(!Field<bool>(actor, "m_boNativeAwakening"),
        context + " awakening");
    Equal(0, CoreField<int>(actor, "MaxHP"), context + " core carrier");
    Equal(0, CoreField<int>(actor, "CCLow"), context + " core CC carrier");
    Assert(!Field<bool>(actor, "m_boNativeState26DirectStrong"),
        context + " direct-strong flag");
    Assert(!Field<bool>(actor, "m_boNativeState26DirectWeak"),
        context + " direct-weak flag");
    Assert(!Field<bool>(actor, "m_boNativeState26SingleStrong"),
        context + " single-strong flag");
    Assert(!Field<bool>(actor, "m_boNativeState26SingleWeak"),
        context + " single-weak flag");
}

static T Field<T>(TBaseObject actor, string name)
{
    var field = typeof(TBaseObject).GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(TBaseObject).FullName, name);
    return (T)field.GetValue(actor)!;
}

static T CoreField<T>(TBaseObject actor, string name)
{
    object core = Field<object>(actor, "m_NativeCoreWorkingAbility");
    var field = core.GetType().GetField(name,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(core.GetType().FullName, name);
    return (T)field.GetValue(core)!;
}

static void Write(byte[] record, int offset, ushort value)
{
    BinaryPrimitives.WriteUInt16LittleEndian(
        record.AsSpan(offset, sizeof(ushort)), value);
}

static void WriteInt(byte[] record, int offset, int value)
{
    BinaryPrimitives.WriteInt32LittleEndian(
        record.AsSpan(offset, sizeof(int)), value);
}

static TPlayObject NewPlayer(string name) => new()
{
    m_boOffLineFlag = true,
    m_sCharName = name
};

static byte[] BuildAbilityPacket(TBaseObject actor)
{
    var method = typeof(TBaseObject).GetMethod("BuildNativeAbilityPacket",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("BuildNativeAbilityPacket");
    return (byte[])(method.Invoke(actor, null)
        ?? throw new InvalidOperationException("native ability packet"));
}

static byte[] BuildHeroAbilityPacket(HeroObject hero)
{
    var method = typeof(HeroObject).GetMethod("BuildHeroAbility",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("BuildHeroAbility");
    return (byte[])(method.Invoke(hero, null)
        ?? throw new InvalidOperationException("hero ability packet"));
}

static TUserItem Item(ushort index, ushort dura = 100) => new()
{
    wIndex = index,
    Dura = dura,
    DuraMax = 100,
    NativeRecord = new byte[208]
};

static GoodItem AddStdItem(params (ushort Id, ushort Value)[] properties)
{
    var item = new GoodItem
    {
        Name = "fixed-baseline-effect-" + M2Share.UserEngine.StdItemList.Count,
        ItemType = GoodType.ITEM_ETC
    };
    for (var index = 0; index < properties.Length; index++)
    {
        item.NativeItemExtAbilIdents[index] = properties[index].Id;
        item.NativeItemExtAbilValues[index] = properties[index].Value;
    }
    M2Share.UserEngine.StdItemList.Add(item);
    return item;
}

static GoodItem AddCoreItem(GoodItem item,
    params (ushort Id, ushort Value)[] properties)
{
    if (properties.Length > item.NativeItemExtAbilIdents.Length)
        throw new ArgumentOutOfRangeException(nameof(properties));
    item.Name = "core-working-item-" +
        M2Share.UserEngine.StdItemList.Count;
    for (var index = 0; index < properties.Length; index++)
    {
        item.NativeItemExtAbilIdents[index] = properties[index].Id;
        item.NativeItemExtAbilValues[index] = properties[index].Value;
    }
    M2Share.UserEngine.StdItemList.Add(item);
    return item;
}

static int PackEndpoints(int low, int high) => unchecked(
    (ushort)low | ((int)(ushort)high << 16));

static void AddBaseMagicStdItem(byte stdMode, byte shape, int outlook,
    ushort wordParam1)
{
    M2Share.UserEngine.StdItemList.Add(new GoodItem
    {
        Name = "base-magic-effect-" + M2Share.UserEngine.StdItemList.Count,
        StdMode = stdMode,
        Shape = shape,
        Outlook = outlook,
        WordParam1 = wordParam1,
        ItemType = GoodType.ITEM_ETC
    });
}

static void ResetItems()
{
    M2Share.UserEngine.StdItemList.Clear();
}

static void InitializeRuntime()
{
    M2Share.g_Config = new GameSvrConfig();
    M2Share.UserEngine = new UserEngine();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.MapManager = new MapManager();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new System.Collections.ArrayList();
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

static void Equal(int expected, int actual, string message)
{
    if (expected != actual)
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed class ShortRecordActor : TBaseObject
{
    private readonly byte[] _record = new byte[0x22D];

    protected override ReadOnlySpan<byte> GetNativeFixedAbilityRecord() =>
        _record;
}

sealed class CoreRecordActor : TBaseObject
{
    private readonly byte[] _record;

    internal CoreRecordActor(byte[] record)
    {
        _record = record;
        m_btRaceServer = Grobal2.RC_HEROOBJECT;
    }

    protected override ReadOnlySpan<byte> GetNativeFixedAbilityRecord() =>
        _record;
}
