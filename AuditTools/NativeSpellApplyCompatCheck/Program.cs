// NativeSpellApplyCompatCheck — the spell-APPLY layer (cast gates, charm
// consumption, summon levels, delayed AoE delivery) against 战神 M2Server.exe.
//
// Binary of record: D:/loym2/staging/M2Server_reunpacked_20260803.exe
// (flat image, ImageBase 0x400000). Every constant asserted below carries the
// 战神 EA it was read from. Evidence doc: staging/spellapply_fix_20260804.md.
//
// Two prior claims this audit deliberately pins AGAINST, because a discovery
// doc got them wrong and following it would have shipped a bug:
//   * "native passes nExpLevel = literal 10 to MakeSlave" — FALSE. sub_6CB070
//     @0x6CB2F9-0x6CB302 writes the EFFECTIVE MAGIC LEVEL (ecx) to BOTH
//     m_btSlaveMakeLevel (+0x483) and m_btSlaveExpLevel (+0x482); the literal 10
//     is stack slot [ebp+8] and lands in the DWORD +0x48C, a percentage field
//     (sub_71E50C @0x71E706 does `HP = HP * [+0x48C] / 100`).
//   * "the generic CheckAmulet also decrements a fixed 100" — FALSE.
//     sub_73E93C @0x73E989 is `imul eax,[ebp-4],0x64` = nCount * 100. Only the
//     inline 施毒术 path @0x6ED986 uses a bare literal 100.

using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

try
{
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    PrepareRuntimeConfig();
    InitializeRuntime();

    VerifyNoSkillZoneGate();
    VerifyAmuletCountPredicate();
    VerifySummonEffectiveLevels();
    VerifyCallMonStoneUse();
    VerifyNativeSlaveHpAfterRoyalty();
    VerifyChangeServerSlaveRestore();
    VerifyCrossServerSlaveSnapshot();
    VerifyNativeClientVersionGate();
    VerifyNativeSwitchHeroHandoff();
    VerifyHeroSlaveRecordRestore();
    VerifyHeroSummonSpawnContext();
    VerifySourceContracts();

    Console.WriteLine(
        "PASS NativeSpellApplyCompatCheck " +
        "noskillzone=cellflag-then-idlist+prefetch-position " +
        "poison=slot9-only+literal100+free-last-cast+autoremove " +
        "amulet=ncount100-le-dura50+shape1or2 " +
        "summon=effective-level-both-fields+callstone-hp0+switch-5slot-compact+switch-hero-handoff+hero-record-restore+hero-physical-context+master-owner " +
        "clientversion=1018+B75+15s+gm-sweep " +
        "range=hardcoded9 aoe=600ms-category3-queue");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        $"NativeSpellApplyCompatCheck FAIL: {exception}");
    return 1;
}

// ---------------------------------------------------------------------------
// Native sub_772A50 @0x772A5A-0x772A8E — the two-OR skill-forbid predicate,
// short-circuiting cell-flag FIRST. sub_77BE88 @0x77BE8D-0x77BEAB reads the
// byte at map[+0x38] + ((x*map[+0x40]+y)*12) + 4 and returns 0 (= ALLOWED) for
// every out-of-bounds coordinate (x<0 / y<0 / x>=Width[+0x3C] /
// y>=Height[+0x40]) — a deliberate fail-OPEN. sub_77BCF4 @0x77BD11-0x77BD19
// linear-scans the map's +0x28 int TList and returns TRUE (= DENY) on a hit.
static void VerifyNoSkillZoneGate()
{
    var envir = new Envirnoment();
    SetMapSize(envir, 8, 8);

    Equal(true, envir.IsSkillAllowedAt(3, 3, 17),
        "clean cell + empty ban list must allow (sub_772A50 bl=1 default)");

    // (a) the per-cell flag: any NON-ZERO byte denies (native tests `test al,al`
    //     @0x772A73, not a specific bit).
    SetMapCellSkillFlag(envir, 3, 3, 1);
    Equal(false, envir.IsSkillAllowedAt(3, 3, 17),
        "cell flag 1 must deny (sub_77BE88 -> jne @0x772A75)");
    SetMapCellSkillFlag(envir, 3, 3, 200);
    Equal(false, envir.IsSkillAllowedAt(3, 3, 17),
        "cell flag 200 must deny — the test is != 0, not a bit mask");
    SetMapCellSkillFlag(envir, 3, 3, 0);
    Equal(true, envir.IsSkillAllowedAt(3, 3, 17),
        "cell flag back to 0 must allow again");

    // out-of-bounds fails OPEN, matching sub_77BE88's four early `jl`/`jge`.
    Equal(true, envir.IsSkillAllowedAt(-1, 3, 17),
        "x<0 must fail OPEN (sub_77BE88 @0x77BE8F jl -> ecx stays 0)");
    Equal(true, envir.IsSkillAllowedAt(3, 999, 17),
        "y>=Height must fail OPEN (sub_77BE88 @0x77BE9A jge)");

    // (b) the per-map id list, keyed on the RAW wire skill index.
    envir.LimitSkillIds.Add(23);
    Equal(false, envir.IsSkillAllowedAt(3, 3, 23),
        "listed skill must deny (sub_77BCF4 @0x77BD14 cmp esi,[ecx+edx*4])");
    Equal(true, envir.IsSkillAllowedAt(3, 3, 24),
        "unlisted skill must still be allowed");

    // The cast path must consult the gate BEFORE resolving the skill, because
    // native calls sub_772A50 @0x6BC546 while GetMagicInfo (VMT+0xE8) is not
    // reached until @0x6BC5CB.
    string attack = ReadSource("GameSvr", "Players",
        "TPlayObject.Attack.cs");
    int gateIndex = attack.IndexOf("m_PEnvir.IsSkillAllowedAt(m_nCurrX",
        StringComparison.Ordinal);
    Assert(gateIndex >= 0,
        "ClientSpellXY must call IsSkillAllowedAt (native @0x6BC546)");
    int resolveIndex = attack.IndexOf("var UserMagic = GetMagicInfo(nKey);",
        StringComparison.Ordinal);
    Assert(resolveIndex >= 0, "ClientSpellXY must resolve the magic");
    Assert(gateIndex < resolveIndex,
        "the skill-forbid gate must run BEFORE GetMagicInfo — native order is " +
        "sub_772A50 @0x6BC546 then VMT+0xE8 @0x6BC5CB");
    int tickIndex = attack.IndexOf("HUtil32.GetTickCount() - m_dwMagicAttackTick",
        StringComparison.Ordinal);
    Assert(tickIndex < 0 || gateIndex < tickIndex,
        "the gate must also precede the interval bookkeeping " +
        "(native GetTickCount is @0x6BC597, after the gate)");
}

// ---------------------------------------------------------------------------
// Native sub_73E93C @0x73E989-0x73E999:
//   imul eax,[ebp-4],0x64   ; nCount * 100
//   mov  dx,[edi+0x26]      ; Dura
//   add  ecx,0x32           ; Dura + 50
//   cmp  eax,ecx ; jg fail  ; PASS iff nCount*100 <= Dura + 50
// The replaced C# predicate was HUtil32.Round(Dura/100.0) >= nCount, whose
// banker's rounding pushes the exact .5 case DOWN. The two forms differ at
// exactly Dura=50/nCount=1 and Dura=450/nCount=5 over Dura in [0,1200].
static void VerifyAmuletCountPredicate()
{
    // Both historically divergent points must now ALLOW the cast.
    Equal(true, CheckAmulet(dura: 50, shape: 5, count: 1, type: 1),
        "Dura=50,nCount=1 must pass: 100 <= 50+50 (native @0x73E997 cmp/jg)");
    Equal(true, CheckAmulet(dura: 450, shape: 5, count: 5, type: 1),
        "Dura=450,nCount=5 must pass: 500 <= 450+50");
    // One tick below each boundary must refuse.
    Equal(false, CheckAmulet(dura: 49, shape: 5, count: 1, type: 1),
        "Dura=49,nCount=1 must fail: 100 > 49+50");
    Equal(false, CheckAmulet(dura: 449, shape: 5, count: 5, type: 1),
        "Dura=449,nCount=5 must fail: 500 > 449+50");

    // Exhaustive equivalence against the native form, so a future refactor
    // cannot silently reintroduce a rounding-based predicate.
    for (int dura = 0; dura <= 1200; dura++)
    {
        foreach (int count in new[] { 1, 2, 5 })
        {
            bool expected = count * 100 <= dura + 50;
            Equal(expected, CheckAmulet(dura, 5, count, 1),
                $"native predicate at Dura={dura}, nCount={count}");
        }
    }

    // The type test: native `is TBujuk` (type 1) is StdMode 25 + Shape 5;
    // `is TPoisons` (type 2) is Shape in {1,2} ONLY. Shape 0 reaches a
    // DIFFERENT class at 0x74D12B in the item factory's Shape switch
    // (jumptab @0x74D07B, reached only from StdMode 25 via bytetab 0x74C374),
    // so Shape 0 must be refused by both arms.
    Equal(false, CheckAmulet(dura: 1000, shape: 0, count: 1, type: 2),
        "Shape 0 is not TPoisons (factory 0x74D07B sends it to 0x74D12B)");
    Equal(true, CheckAmulet(dura: 1000, shape: 1, count: 1, type: 2),
        "Shape 1 is TPoisons (0x74D0A8)");
    Equal(true, CheckAmulet(dura: 1000, shape: 2, count: 1, type: 2),
        "Shape 2 is TPoisons (0x74D0A8)");
    Equal(false, CheckAmulet(dura: 1000, shape: 5, count: 1, type: 2),
        "Shape 5 is TBujuk, not TPoisons");
    Equal(false, CheckAmulet(dura: 1000, shape: 1, count: 1, type: 1),
        "type 1 requires TBujuk (Shape 5)");

    // Slot discipline: native reads ONLY slot 9. Every spell-path caller passes
    // dl=9 (0x6ED949, 0x73E95F, 0x73EA50, 0x73CC53, 0x73E9D6, 0x73EBA8), so a
    // charm worn in U_ARMRINGL must NOT satisfy the gate.
    var player = NewPlayer();
    player.m_UseItems[Grobal2.U_ARMRINGL] = NewCharm(1000);
    RegisterStdItem(1, shape: 5);
    short index = -1;
    Equal(false, Magic.CheckAmulet(player, 1, 1, ref index),
        "U_ARMRINGL must never satisfy the charm gate — native only reads " +
        "slot 9 (sub_75EC20 with dl=9 at every spell-path call site)");
    Equal((short)0, index, "the out index must stay 0 when the gate refuses");
}

// ---------------------------------------------------------------------------
// Native summon producers sub_76EDFC (骷髅 17) / sub_76EE7C (神兽 30) /
// sub_76EEF4 (天使) each push `1 / 0xD2F00 / 0 / 0xA` then load
// `sub_4C896C -> cl` = the EFFECTIVE magic level, and TPlayer.MakeSlave
// (VMT+0xEC = sub_6CB070) writes that ecx byte to BOTH slave-level fields:
//   6CB2F9  mov al,byte [ebp-8]        ; ecx = effective level
//   6CB2FC  mov byte [esi+0x483],al    ; m_btSlaveMakeLevel
//   6CB302  mov byte [esi+0x482],al    ; m_btSlaveExpLevel
// Field ids come from GainSlaveExp sub_71F3D0 @0x71F427-0x71F442 and from
// TMonster.RecalcAbilitys sub_71DF70 (VMT+0x8C) reading +0x482 @0x71DFB3.
static void VerifySummonEffectiveLevels()
{
    // The helper the summon paths must use is sub_4C896C: btLevel + bonus,
    // clamped to btTrainLv (`mov dl,[eax+0xC]; add dl,[eax+0x18];
    // mov cl,[[eax]+0x1A]; cmp dl,cl; jbe`).
    var magic = MakeMagic(17, level: 2, trainLevel: 5);
    SetMagicLevelBonus(magic, 2);
    Equal(4, TPlayObject.GetNativeMagicProducerEffectiveLevel(magic),
        "effective level must add NativeLevelBonus (sub_4C896C @0x4C896F)");
    magic.MagicInfo.btTrainLv = 3;
    Equal(3, TPlayObject.GetNativeMagicProducerEffectiveLevel(magic),
        "effective level must clamp to btTrainLv (sub_4C896C @0x4C8977)");

    // Every summon site must pass the EFFECTIVE level, and must pass the SAME
    // value for makeLevel and expLevel — never the literal 10, never raw
    // btLevel. Enforced on the source because MakeSlave needs a live map.
    string manager = ReadSource("GameSvr", "Spells", "MagicManager.cs");
    foreach (string producer in new[]
             { "MagMakeSlave", "MagMakeSinSuSlave", "MagMakeAngelSlave" })
    {
        string body = MethodBody(manager, producer);
        Assert(body.Contains("GetNativeMagicProducerEffectiveLevel(UserMagic)",
                StringComparison.Ordinal),
            $"{producer} must derive the summon level from sub_4C896C " +
            "(native @0x76EE29 / @0x76EEA3 / @0x76EF21)");
        Assert(!body.Contains("= UserMagic.btLevel", StringComparison.Ordinal),
            $"{producer} must not use the RAW btLevel — native loads " +
            "sub_4C896C, so a +skill-level item would be dropped");
        Assert(!body.Contains("nExpLevel = 10", StringComparison.Ordinal),
            $"{producer} must NOT set nExpLevel = 10: the literal 0xA at " +
            "@0x76EE27 is stack slot [ebp+8] and lands in the DWORD +0x48C " +
            "(a percentage, sub_71E50C @0x71E706), not in a level field");
    }
}

// ---------------------------------------------------------------------------
// TCallMonStone.Use sub_7887D0. The selector is the low byte of StdItem
// +0x17, the four MakeSlave calls use level 3 / max 1 / hpAfterSlave 0, and
// only a remainder of at least 1000 keeps the stone in the bag.
static void VerifyCallMonStoneUse()
{
    var names = new Dictionary<ushort, (string Name, int RoyaltySeconds)>
    {
        [1] = ("温顺的冰眼巨魔", 1_800),
        [2] = ("降伏的冰眼巨魔", 1_800),
        [3] = ("追随的冰眼巨魔", 1_800),
        [4] = ("神龙", 864_000),
        [257] = ("温顺的冰眼巨魔", 1_800)
    };

    M2Share.UserEngine.MonsterList.Clear();
    foreach (var name in names.Values.Select(value => value.Name).Distinct())
        M2Share.UserEngine.MonsterList.Add(NewSummonMonsterInfo(name));
    M2Share.UserEngine.m_MonGenList.Clear();
    M2Share.UserEngine.m_MonGenList.Add(new MonGenInfo
    {
        CertList = new List<TBaseObject>()
    });

    foreach (var pair in names)
    {
        int before = HUtil32.GetTickCount();
        var (player, item) = RunCallMonStoneUse(pair.Key, 1_000);
        var slave = player.m_SlaveList.Single();
        Equal(pair.Value.Name, slave.m_sCharName,
            $"call-stone selector {pair.Key} name");
        Equal((byte)3, slave.m_btSlaveMakeLevel,
            $"call-stone selector {pair.Key} make level");
        Equal((byte)3, slave.m_btSlaveExpLevel,
            $"call-stone selector {pair.Key} exp level");
        Assert(slave.m_boNoItem,
            $"call-stone selector {pair.Key} no-item-drop flag");
        Assert(slave is AnimalObject,
            $"call-stone selector {pair.Key} TAnimal class");
        Equal(0, ((AnimalObject)slave).m_nNativeHpAfterSlavePercent,
            $"call-stone selector {pair.Key} hpAfterSlave");
        int royalty = unchecked(slave.m_dwMasterRoyaltyTick - before);
        Assert(royalty >= pair.Value.RoyaltySeconds * 1_000 &&
               royalty <= pair.Value.RoyaltySeconds * 1_000 + 1_000,
            $"call-stone selector {pair.Key} royalty duration: {royalty}");
        Assert(!player.m_ItemList.Contains(item),
            $"call-stone selector {pair.Key} Dura=1000 whole consume");
        Equal(1, player.SentStrings.Count(entry =>
                entry.Packet.Ident == Grobal2.SM_EAT_OK),
            $"call-stone selector {pair.Key} EAT_OK count");
    }

    var (remainderPlayer, remainderItem) = RunCallMonStoneUse(1, 1_999);
    Equal((ushort)999, remainderItem.Dura,
        "call-stone Dura=1999 subtracts 1000 before wholesale consume");
    Assert(!remainderPlayer.m_ItemList.Contains(remainderItem),
        "call-stone Dura=1999 must discard the sub-1000 remainder");

    var (keptPlayer, keptItem) = RunCallMonStoneUse(1, 2_000);
    Equal((ushort)1_000, keptItem.Dura,
        "call-stone Dura=2000 remainder");
    Assert(keptPlayer.m_ItemList.Contains(keptItem),
        "call-stone Dura=2000 must keep the stone");
    var keptPackets = keptPlayer.SentStrings
        .Where(entry => entry.Packet.Ident == Grobal2.SM_BAGITEMDURACHG ||
                        entry.Packet.Ident == Grobal2.SM_EAT_FAIL)
        .Select(entry => entry.Packet.Ident).ToArray();
    Assert(keptPackets.SequenceEqual(new[]
        {
            (ushort)Grobal2.SM_BAGITEMDURACHG,
            (ushort)Grobal2.SM_EAT_FAIL
        }),
        "call-stone Dura=2000 packet order must be durability then EAT_FAIL");

    foreach (var denied in new[]
             {
                 (Label: "selector-0", Selector: (ushort)0,
                     Dura: (ushort)1_000, Scene: (byte)0, Dare: false),
                 (Label: "selector-5", Selector: (ushort)5,
                     Dura: (ushort)1_000, Scene: (byte)0, Dare: false),
                 (Label: "dura-999", Selector: (ushort)1,
                     Dura: (ushort)999, Scene: (byte)0, Dare: false),
                 (Label: "newsky", Selector: (ushort)1,
                     Dura: (ushort)1_000, Scene: (byte)2, Dare: false),
                 (Label: "dare", Selector: (ushort)1,
                     Dura: (ushort)1_000, Scene: (byte)0, Dare: true)
             })
    {
        var (player, item) = RunCallMonStoneUse(denied.Selector, denied.Dura,
            denied.Scene, denied.Dare);
        Equal(0, player.m_SlaveList.Count,
            $"call-stone denied {denied.Label} spawn count");
        Equal(denied.Dura, item.Dura,
            $"call-stone denied {denied.Label} durability");
        Assert(player.m_ItemList.Contains(item),
            $"call-stone denied {denied.Label} bag retention");
        Equal(1, player.SentStrings.Count(entry =>
                entry.Packet.Ident == Grobal2.SM_EAT_FAIL),
            $"call-stone denied {denied.Label} EAT_FAIL count");
    }

    var (fullPlayer, fullItem) = RunCallMonStoneUse(1, 1_000,
        existingSlave: true);
    Equal(1, fullPlayer.m_SlaveList.Count,
        "call-stone existing-slave refusal spawned another slave");
    Equal((ushort)1_000, fullItem.Dura,
        "call-stone existing-slave refusal changed durability");
    Assert(fullPlayer.m_ItemList.Contains(fullItem),
        "call-stone existing-slave refusal consumed the stone");
    var refusal = fullPlayer.m_MsgList.Single(message =>
        message.wIdent == Grobal2.RM_SYSMESSAGE);
    Equal("您当前已有下属，不能使用召唤石", refusal.Buff,
        "call-stone existing-slave exact refusal text");
    Equal((int)M2Share.g_Config.btGreenMsgFColor, refusal.nParam1,
        "call-stone existing-slave refusal foreground color");
    Equal((int)M2Share.g_Config.btGreenMsgBColor, refusal.nParam2,
        "call-stone existing-slave refusal background color");
}

// ---------------------------------------------------------------------------
// TAnimal +0x48C and TAnimal.Run @0x71E6FD..0x71E717. The one-operand
// signed IMUL is followed by CDQ/IDIV 100, so only the wrapped low Int32 is
// divided. TAnimal.Create initializes the field to 10, while MakeSlave always
// overwrites it with the caller's raw hpAfterSlave argument.
static void VerifyNativeSlaveHpAfterRoyalty()
{
    const string monsterName = "RoyaltyPercentOma";
    M2Share.UserEngine.MonsterList.Clear();
    M2Share.UserEngine.MonsterList.Add(NewSummonMonsterInfo(monsterName));
    M2Share.UserEngine.m_MonGenList.Clear();
    M2Share.UserEngine.m_MonGenList.Add(new MonGenInfo
    {
        CertList = new List<TBaseObject>()
    });

    var cases = new[]
    {
        (Hp: 123, Percent: 10),
        (Hp: 101, Percent: 37),
        (Hp: 500, Percent: 0),
        (Hp: int.MaxValue, Percent: 2)
    };
    for (var index = 0; index < cases.Length; index++)
    {
        var test = cases[index];
        var environment = NewSummonEnvironment(
            "RoyaltyPercent-" + index, "royalty-" + index);
        var master = NewSummonMaster("royalty-master-" + index,
            environment, 4, 4, Grobal2.DR_RIGHT);
        var slave = master.MakeNativeSlave(monsterName, magicLevel: 3,
            nMaxMob: 1, dwRoyaltySec: 60, fromHero: false,
            hpAfterSlave: test.Percent);
        Assert(slave is AnimalObject,
            "native MakeSlave did not create a TAnimal-derived slave");
        var animal = (AnimalObject)slave;
        Equal((byte)3, slave.m_btSlaveMakeLevel,
            "MakeSlave MagicLv -> make level");
        Equal((byte)3, slave.m_btSlaveExpLevel,
            "MakeSlave MagicLv -> exp level");
        Equal(test.Percent, animal.m_nNativeHpAfterSlavePercent,
            "MakeSlave hpAfterSlave raw Int32 storage");

        slave.m_WAbil.HP = test.Hp;
        slave.m_WAbil.MaxHP = 987654321;
        int expectedHp = unchecked(test.Hp * test.Percent) / 100;
        slave.ExpireNativeSlaveRoyalty();

        Equal(expectedHp, slave.m_WAbil.HP,
            $"royalty signed wrapped percentage case {index}");
        Equal(987654321, slave.m_WAbil.MaxHP,
            "royalty expiration must not alter MaxHP");
        Assert(slave.m_Master == null,
            "royalty expiration did not clear the master");
        Equal(0, master.m_SlaveList.Count,
            "royalty expiration did not remove the slave from its owner");
        Assert(animal.m_boNativeSlaveRoyaltyExpired,
            "royalty expiration did not set native TAnimal +0x450");

        var leaves = master.SentStrings.Where(entry =>
            entry.Packet.Ident == Grobal2.SM_SLAVE_LEAVE).ToArray();
        Equal(1, leaves.Length, "royalty SM_SLAVE_LEAVE count");
        Equal(monsterName, leaves[0].Body, "royalty SM_SLAVE_LEAVE body");
    }

    // The published Pascal signature accepts any signed Integer and keeps the
    // player as owner. BoFromHero changes only the physical spawn context.
    var pasMasterMap = NewSummonEnvironment("PasSlaveMaster", "pas-master");
    var pasHeroMap = NewSummonEnvironment("PasSlaveHero", "pas-hero");
    var pasMaster = NewSummonMaster("pas-slave-master", pasMasterMap,
        3, 3, Grobal2.DR_RIGHT);
    var pasHero = AttachSummonHero(pasMaster, pasHeroMap,
        8, 8, Grobal2.DR_LEFT);
    var bridge = new PasApiBridge { CurrentPlayer = pasMaster };
    var args = new List<PasValue>
    {
        PasValue.FromString(monsterName),
        PasValue.FromInt(4),
        PasValue.FromInt(1),
        PasValue.FromInt(60),
        PasValue.FromBool(true),
        PasValue.FromInt(-37)
    };
    Assert(bridge.CallPlayerFunc("MakeSlave", args, out var result),
        "Pascal MakeSlave rejected its exact six-argument signature");
    var pasSlave = result.ObjVal as AnimalObject;
    Assert(pasSlave != null,
        "Pascal MakeSlave did not return the spawned slave");
    CheckSummonResult(pasSlave, pasMaster, pasHero, pasHeroMap,
        7, 8, monsterName, "Pascal MakeSlave BoFromHero");
    Equal((byte)4, pasSlave.m_btSlaveMakeLevel,
        "Pascal MakeSlave MagicLv -> make level");
    Equal((byte)4, pasSlave.m_btSlaveExpLevel,
        "Pascal MakeSlave MagicLv -> exp level");
    Equal(-37, pasSlave.m_nNativeHpAfterSlavePercent,
        "Pascal MakeSlave signed hpAfterSlave passthrough");
}

// ---------------------------------------------------------------------------
// Player slave-record restore sub_6CB6C4. Record +0x1D is the make level,
// +0x1C is the post-create exp-level override, and HP/MP are WORDs.
static void VerifyChangeServerSlaveRestore()
{
    const string normalName = "RestoreNormalOma";
    const string holy151Name = "RestoreHoly151";
    const string holy170Name = "RestoreHoly170";
    M2Share.UserEngine.MonsterList.Clear();
    M2Share.UserEngine.MonsterList.Add(NewSummonMonsterInfo(normalName,
        (byte)M2Share.MONSTER_OMA, 3000, 3000));
    M2Share.UserEngine.MonsterList.Add(NewSummonMonsterInfo(holy151Name,
        151, 3000, 3000));
    M2Share.UserEngine.MonsterList.Add(NewSummonMonsterInfo(holy170Name,
        170, 3000, 3000));
    M2Share.UserEngine.m_MonGenList.Clear();
    M2Share.UserEngine.m_MonGenList.Add(new MonGenInfo
    {
        CertList = new List<TBaseObject>()
    });

    var normalMap = NewSummonEnvironment("RestoreNormal", "restore-normal");
    var normalMaster = NewSummonMaster("restore-normal-master", normalMap,
        4, 4, Grobal2.DR_RIGHT);
    normalMaster.m_btJob = 0;
    normalMaster.ChangeServerMakeSlave(new TSlaveInfo
    {
        sSlaveName = normalName,
        btSlaveLevel = 2,
        btSlaveExpLevel = 5,
        nKillCount = 77,
        dwRoyaltySec = 90,
        nHP = 0x12345,
        nMP = -1
    });
    Equal(1, normalMaster.m_SlaveList.Count,
        "normal slave restore count");
    var normal = normalMaster.m_SlaveList[0];
    Equal((byte)2, normal.m_btSlaveMakeLevel,
        "record +0x1D make level");
    Equal((byte)5, normal.m_btSlaveExpLevel,
        "record +0x1C exp level");
    Equal(77, normal.m_nKillMonCount, "record +0x10 kill count");
    Equal(0x2345, normal.m_WAbil.HP, "record +0x18 HP WORD");
    Equal(0xFFFF, normal.m_WAbil.MP, "record +0x1A MP WORD");
    Equal(1100, normal.m_nWalkSpeed, "restored walk-speed cap");
    Equal(1600, normal.m_nNextHitTime, "restored attack-speed cap");
    Equal(10, ((AnimalObject)normal).m_nNativeHpAfterSlavePercent,
        "restored slave hpAfterSlave literal");

    var holy151Map = NewSummonEnvironment("RestoreHoly151", "restore-holy151");
    var holy151Master = NewSummonMaster("restore-holy151-master", holy151Map,
        4, 4, Grobal2.DR_RIGHT);
    holy151Master.ChangeServerMakeSlave(new TSlaveInfo
    {
        sSlaveName = holy151Name,
        btSlaveLevel = 3,
        btSlaveExpLevel = 7,
        nKillCount = 99,
        dwRoyaltySec = 90,
        nHP = 321,
        nMP = 123
    });
    var holy151 = holy151Master.m_SlaveList.Single() as HolyMonster;
    Assert(holy151 != null,
        "race 151 restore did not create THolyMonster");
    Equal((byte)3, holy151.m_btSlaveExpLevel,
        "race 151 must skip the record exp-level override");
    Equal(0, holy151.m_nKillMonCount,
        "race 151 must skip the record kill-count override");
    Assert(ReferenceEquals(holy151Master,
            Get(holy151, "m_NativeHolyBeastSummoner")),
        "race 151 restore did not execute sub_66C630 binding");

    var holy170Map = NewSummonEnvironment("RestoreHoly170", "restore-holy170");
    var holy170Master = NewSummonMaster("restore-holy170-master", holy170Map,
        4, 4, Grobal2.DR_RIGHT);
    holy170Master.ChangeServerMakeSlave(new TSlaveInfo
    {
        sSlaveName = holy170Name,
        btSlaveLevel = 2,
        btSlaveExpLevel = 6,
        nKillCount = 88,
        dwRoyaltySec = 90,
        nHP = 222,
        nMP = 111
    });
    var holy170 = holy170Master.m_SlaveList.Single() as HolyMonster;
    Assert(holy170 != null,
        "race 170 restore did not create THolyMonster");
    Equal((byte)6, holy170.m_btSlaveExpLevel,
        "race 170 must take the ordinary exp-level override");
    Equal(88, holy170.m_nKillMonCount,
        "race 170 must take the ordinary kill-count override");
    Assert(Get(holy170, "m_NativeHolyBeastSummoner") == null,
        "race 170 incorrectly took the race-151-only binding arm");
}

// ---------------------------------------------------------------------------
// Fixed cross-server slave area, sub_6B1764/sub_6B188C: five compact output
// slots, excluding null/dead/ghost and hero+0x6C4, followed by a full sparse
// five-slot read with the native 1500 ms delivery delay.
static void VerifyCrossServerSlaveSnapshot()
{
    var blank = new TSwitchDataInfo();
    Assert(blank.BlockWhisperArr != null, "switch block-whisper container");
    Equal(TSwitchDataInfo.NativeSlaveSlotCount, blank.SlaveArr.Length,
        "switch native slave slot count");
    Assert(blank.SlaveArr.All(item => item != null),
        "switch native slave elements");
    Equal(TSwitchDataInfo.NativeStatusSlotCount, blank.StatusValue.Length,
        "switch native status-value count");
    Equal(TSwitchDataInfo.NativeStatusSlotCount, blank.StatusTimeOut.Length,
        "switch native status-timeout count");

    var player = new TPlayObject { m_sCharName = "switch-owner" };
    var hero = new HeroObject { m_Master = player };
    player.m_HeroObject = hero;
    var dead = new TBaseObject { m_sCharName = "dead", m_boDeath = true };
    var ghost = new TBaseObject { m_sCharName = "ghost", m_boGhost = true };
    var heroSlave = new TBaseObject { m_sCharName = "hero-slot" };
    Set(hero, "m_NativeHeroSummonSlave", heroSlave);
    var a = new TBaseObject
    {
        m_sCharName = "A",
        m_nKillMonCount = 0x10203040,
        m_btSlaveMakeLevel = 3,
        m_btSlaveExpLevel = 7,
        m_dwMasterRoyaltyTick = HUtil32.GetTickCount() + 120_000
    };
    a.m_WAbil.HP = -1;
    a.m_WAbil.MP = 0x12345;
    var b = new TBaseObject { m_sCharName = "B" };
    var c = new TBaseObject { m_sCharName = "C" };
    var d = new TBaseObject { m_sCharName = "D" };
    var e = new TBaseObject { m_sCharName = "E" };
    var sixth = new TBaseObject { m_sCharName = "F" };
    player.m_SlaveList.Clear();
    foreach (var candidate in new TBaseObject[]
             { null, dead, a, heroSlave, ghost, b, c, d, e, sixth })
    {
        player.m_SlaveList.Add(candidate);
    }

    var make = typeof(UserEngine).GetMethod("MakeSwitchData",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("UserEngine.MakeSwitchData");
    object[] makeArgs = { player, null };
    make.Invoke(M2Share.UserEngine, makeArgs);
    var saved = (TSwitchDataInfo)makeArgs[1];
    Equal("A", saved.SlaveArr[0].sSlaveName,
        "first compact cross-server slave");
    Equal("B", saved.SlaveArr[1].sSlaveName,
        "second compact cross-server slave");
    Equal("E", saved.SlaveArr[4].sSlaveName,
        "fifth compact cross-server slave");
    Assert(saved.SlaveArr.All(item => item.sSlaveName != "hero-slot"),
        "hero+0x6C4 leaked into ordinary cross-server slots");
    Assert(saved.SlaveArr.All(item => item.sSlaveName != "F"),
        "sixth eligible cross-server slave exceeded fixed capacity");
    Equal(0x10203040, saved.SlaveArr[0].nKillCount,
        "cross-server slave kill count");
    Equal((byte)3, saved.SlaveArr[0].btSlaveLevel,
        "cross-server slave make level");
    Equal((byte)7, saved.SlaveArr[0].btSlaveExpLevel,
        "cross-server slave exp level");
    Equal(0xFFFF, saved.SlaveArr[0].nHP,
        "cross-server slave HP low WORD");
    Equal(0x2345, saved.SlaveArr[0].nMP,
        "cross-server slave MP low WORD");
    Assert(saved.SlaveArr[0].dwRoyaltySec is >= 119 and <= 120,
        "cross-server slave unsigned remaining royalty seconds");

    var sparse = new TSwitchDataInfo();
    sparse.SlaveArr[1].sSlaveName = "SparseA";
    sparse.SlaveArr[3].sSlaveName = "SparseB";
    var receiver = new TPlayObject();
    var load = typeof(UserEngine).GetMethod("LoadSwitchData",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("UserEngine.LoadSwitchData");
    var beforeLoad = HUtil32.GetTickCount();
    object[] loadArgs = { sparse, receiver };
    load.Invoke(M2Share.UserEngine, loadArgs);
    var afterLoad = HUtil32.GetTickCount();
    var restored = receiver.m_MsgList
        .Where(message => message.wIdent == Grobal2.RM_10401)
        .ToArray();
    Equal(2, restored.Length, "sparse cross-server slave restore count");
    Equal("SparseA", ((TSlaveInfo)restored[0].Payload).sSlaveName,
        "sparse cross-server slot 1");
    Equal("SparseB", ((TSlaveInfo)restored[1].Payload).sSlaveName,
        "sparse cross-server slot 3");
    foreach (var message in restored)
    {
        var fromBefore = unchecked(message.dwDeliveryTime - beforeLoad);
        var fromAfter = unchecked(message.dwDeliveryTime - afterLoad);
        Assert(fromBefore >= 1500 && fromAfter <= 1500 && fromAfter >= 1400,
            $"cross-server slave restore delay must be native 1500 ms, " +
            $"fromBefore={fromBefore} fromAfter={fromAfter}");
    }

    var malformed = new TSwitchDataInfo
    {
        BlockWhisperArr = null,
        SlaveArr = null,
        StatusValue = null,
        StatusTimeOut = null
    };
    object[] malformedArgs = { malformed, new TPlayObject() };
    load.Invoke(M2Share.UserEngine, malformedArgs);

    const int switchTick = 0x10203040;
    player.m_nNativeSwitchSerial = 0x01020304;
    player.m_boNativeSwitchOffsetB75 = true;
    player.m_boNativeSwitchHeroHandoffPending = true;
    player.m_wNativeSwitchOffsetD38 = 0;
    player.m_nNativeSwitchOffsetD3C = unchecked((int)0x88776655);
    player.m_nNativeSwitchOffsetD40 = 0x11223344;
    player.m_btNativeHeroRequestKind = 2;
    player.m_btNativeHeroRequestSlot = 1;
    player.m_boObMode = false;
    player.SetNativeActiveState(0x3C);
    a.m_dwMasterRoyaltyTick = switchTick + 120_000;
    Assert(NativeSwitchDataCodec.TryEncode(player, switchTick,
        out var extension, out var extensionError), extensionError);
    Equal(NativeSwitchDataCodec.ExtensionSize, extension.Length,
        "native switch extension size");
    Equal(0x01020305, BinaryPrimitives.ReadInt32LittleEndian(
        extension.AsSpan(4, 4)), "switch serial increment");
    Equal((ushort)0x38, BinaryPrimitives.ReadUInt16LittleEndian(
        extension.AsSpan(8, 2)), "switch flag word");
    Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(
        extension.AsSpan(0x0A, 2)), "switch D38 word");
    Equal(unchecked((int)0x88776655), BinaryPrimitives.ReadInt32LittleEndian(
        extension.AsSpan(0x0C, 4)), "switch D3C value");
    Equal(0x11223344, BinaryPrimitives.ReadInt32LittleEndian(
        extension.AsSpan(0x10, 4)), "switch D40 value");
    Equal((byte)2, extension[0x14], "switch hero kind byte");
    Equal((byte)1, extension[0x15], "switch hero slot byte");
    Assert(extension.AsSpan(0x16,
            NativeSwitchDataCodec.SlaveOffset - 0x16).ToArray()
        .All(value => value == 0), "switch reserved header bytes");
    Equal((byte)1, extension[NativeSwitchDataCodec.SlaveOffset],
        "switch slave slot0 name length");
    Equal(0x10203040, BinaryPrimitives.ReadInt32LittleEndian(
        extension.AsSpan(NativeSwitchDataCodec.SlaveOffset + 0x10, 4)),
        "switch slave slot0 kill count bytes");
    Equal(120u, BinaryPrimitives.ReadUInt32LittleEndian(
        extension.AsSpan(NativeSwitchDataCodec.SlaveOffset + 0x14, 4)),
        "switch slave slot0 royalty bytes");
    Equal((ushort)0xFFFF, BinaryPrimitives.ReadUInt16LittleEndian(
        extension.AsSpan(NativeSwitchDataCodec.SlaveOffset + 0x18, 2)),
        "switch slave slot0 HP bytes");
    Equal((ushort)0x2345, BinaryPrimitives.ReadUInt16LittleEndian(
        extension.AsSpan(NativeSwitchDataCodec.SlaveOffset + 0x1A, 2)),
        "switch slave slot0 MP bytes");
    Equal((byte)7, extension[NativeSwitchDataCodec.SlaveOffset + 0x1C],
        "switch slave slot0 exp level byte");
    Equal((byte)3, extension[NativeSwitchDataCodec.SlaveOffset + 0x1D],
        "switch slave slot0 make level byte");
    Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(
        extension.AsSpan(NativeSwitchDataCodec.SlaveOffset + 0x1E, 2)),
        "switch slave slot0 reserved tail");

    var rawReceiver = new TPlayObject();
    Assert(NativeSwitchDataCodec.TryRestore(rawReceiver, extension,
        switchTick + 50, out var rawRestored, out extensionError), extensionError);
    Assert(rawRestored, "mode2 marker did not restore");
    Equal(0x01020305, rawReceiver.m_nNativeSwitchSerial,
        "restored switch serial");
    Assert(rawReceiver.m_boNativeSwitchOffsetB75,
        "restored switch B75 flag");
    Assert(rawReceiver.m_boObMode, "restored switch pass-through -> ob mode");
    Assert(rawReceiver.m_boNativeSwitchHeroHandoffPending,
        "restored switch hero handoff flag");
    Equal(unchecked((int)0x88776655), rawReceiver.m_nNativeSwitchOffsetD3C,
        "restored switch D3C");
    Equal(0x11223344, rawReceiver.m_nNativeSwitchOffsetD40,
        "restored switch D40");
    Equal(switchTick + 50, rawReceiver.m_dwNativeSwitchOffsetD44,
        "restored switch D44 baseline");
    Equal(switchTick + 50, rawReceiver.m_dwNativeSwitchHeroKind0Tick,
        "switch handoff restore records the old kind before replacing it");
    Equal((byte)2, rawReceiver.m_btNativeHeroRequestKind,
        "restored switch hero kind");
    Equal((byte)1, rawReceiver.m_btNativeHeroRequestSlot,
        "restored switch hero slot");
    Equal(5, rawReceiver.m_MsgList.Count(message =>
        message.wIdent == Grobal2.RM_10401),
        "raw switch five-slot restore count");

    var longName = new TBaseObject
    {
        m_sCharName = "1234567890ABCDEF",
        m_dwMasterRoyaltyTick = switchTick
    };
    var slaveRecord = new byte[NativeSlaveInfoCodec.RecordSize];
    Assert(NativeSlaveInfoCodec.TryEncode(slaveRecord, longName,
        switchTick, out extensionError), extensionError);
    Equal((byte)15, slaveRecord[0],
        "native ShortString15 must truncate raw bytes");
    Assert(NativeSlaveInfoCodec.TryDecode(slaveRecord,
        out var truncated, out extensionError), extensionError);
    Equal("1234567890ABCDE", truncated.sSlaveName,
        "native ShortString15 truncated value");

    var splitGbkName = new TBaseObject
    {
        m_sCharName = "12345678901234\u4E2D",
        m_dwMasterRoyaltyTick = switchTick
    };
    Assert(NativeSlaveInfoCodec.TryEncode(slaveRecord, splitGbkName,
        switchTick, out extensionError), extensionError);
    Equal((byte)15, slaveRecord[0],
        "native ShortString15 split-GBK length");
    Assert(NativeSlaveInfoCodec.TryDecode(slaveRecord,
        out var splitGbkDecoded, out extensionError), extensionError);
    Assert(splitGbkDecoded.sSlaveName.StartsWith(
            "12345678901234", StringComparison.Ordinal),
        "split GBK tail rejected or corrupted the intact prefix");

    var malformedExtension = (byte[])extension.Clone();
    malformedExtension[NativeSwitchDataCodec.SlaveOffset
        + (NativeSwitchDataCodec.SlaveSlotCount - 1)
        * NativeSlaveInfoCodec.RecordSize] = 16;
    var atomicReceiver = new TPlayObject
    {
        m_nNativeSwitchSerial = unchecked((int)0x55667788),
        m_boNativeSwitchOffsetB75 = false
    };
    Assert(!NativeSwitchDataCodec.TryRestore(atomicReceiver,
            malformedExtension, switchTick, out rawRestored,
            out extensionError),
        "malformed fifth slave slot was accepted");
    Equal(unchecked((int)0x55667788), atomicReceiver.m_nNativeSwitchSerial,
        "failed switch restore partially changed player fields");
    Equal(0, atomicReceiver.m_MsgList.Count,
        "failed switch restore partially queued slave messages");

    player.m_wNativeSwitchOffsetD38 = 7;
    player.m_nNativeSwitchOffsetD3C = 99;
    player.m_dwNativeSwitchOffsetD44 = switchTick - 456;
    Assert(NativeSwitchDataCodec.TryEncode(player, switchTick,
        out var elapsedExtension, out extensionError), extensionError);
    Equal(456, BinaryPrimitives.ReadInt32LittleEndian(
        elapsedExtension.AsSpan(0x0C, 4)), "switch active elapsed value");
    Equal(0, BinaryPrimitives.ReadInt32LittleEndian(
        elapsedExtension.AsSpan(0x10, 4)), "switch active D40 stays zero");
    var elapsedReceiver = new TPlayObject
    {
        m_nNativeSwitchOffsetD3C = 77
    };
    Assert(NativeSwitchDataCodec.TryRestore(elapsedReceiver,
        elapsedExtension, switchTick + 1000, out rawRestored,
        out extensionError), extensionError);
    Equal((ushort)7, elapsedReceiver.m_wNativeSwitchOffsetD38,
        "active switch restore preserves D38");
    Equal(switchTick + 1000 - 77,
        elapsedReceiver.m_dwNativeSwitchOffsetD44,
        "active switch restore uses existing D3C, not extension +0x0C");
    Assert(NativeSwitchDataCodec.TryEncode(elapsedReceiver,
        switchTick + 1500, out var secondActiveExtension,
        out extensionError), extensionError);
    Equal((ushort)7, BinaryPrimitives.ReadUInt16LittleEndian(
        secondActiveExtension.AsSpan(0x0A, 2)),
        "second mode-2 hop preserves active D38");
    Equal(0, BinaryPrimitives.ReadInt32LittleEndian(
        secondActiveExtension.AsSpan(0x10, 4)),
        "second active mode-2 hop keeps D40 suppressed");
    var secondActiveReceiver = new TPlayObject
    {
        m_nNativeSwitchOffsetD3C = 123
    };
    Assert(NativeSwitchDataCodec.TryRestore(secondActiveReceiver,
        secondActiveExtension, switchTick + 2000, out rawRestored,
        out extensionError) && rawRestored, extensionError);
    Equal((ushort)7, secondActiveReceiver.m_wNativeSwitchOffsetD38,
        "second mode-2 restore preserves active D38");
}

// ---------------------------------------------------------------------------
// B75 client-version lifecycle: 1018 pre-dispatch producer, permission bypass,
// mode-2 roundtrip, @ClientVersion mismatch sweep, and the 15-second Run arm.
static void VerifyNativeClientVersionGate()
{
    var originalServerSwitches = M2Share.ServerSwitches;
    NativeClientVersionPolicy.SetRequiredVersion(string.Empty);
    try
    {
        M2Share.ServerSwitches = NativeServerSwitchStore.FromSnapshot(
            "b75-disabled-switches.bin", new byte[5]);
        var preHandshake = new TPlayObject();
        Assert(!preHandshake.ShouldDispatchNativeClientMessage(
                new ClientPacket { Ident = Grobal2.CM_QUERYBAGITEMS }),
            "pre-handshake non-1018 message must be dropped");
        Assert(!preHandshake.m_boNativeClientVersionHandshakeDone,
            "dropped pre-handshake message changed handshake state");
        Assert(!preHandshake.ShouldDispatchNativeClientMessage(
                new ClientPacket { Ident = Grobal2.CM_3340 }),
            "disabled client-info switch admitted pre-handshake 3340");
        M2Share.ServerSwitches = NativeServerSwitchStore.FromSnapshot(
            "b75-enabled-switches.bin", new byte[] { 0, 0, 0, 0x20, 0 });
        Assert(preHandshake.ShouldDispatchNativeClientMessage(
                new ClientPacket { Ident = Grobal2.CM_3340 }),
            "enabled client-info switch rejected pre-handshake 3340");
        Assert(!preHandshake.m_boNativeClientVersionHandshakeDone,
            "3340 exception completed the version handshake");

        NativeClientVersionPolicy.SetRequiredVersion("1.2.3.4");
        var exact = new TPlayObject();
        Assert(!exact.ShouldDispatchNativeClientMessage(new ClientPacket
            {
                Ident = Grobal2.CM_LOGINNOTICEOK,
                Recog = 1,
                Param = 2,
                Tag = 3,
                Series = 4
            }), "first 1018 must be consumed by the pre-dispatch arm");
        Assert(exact.m_boNativeClientVersionHandshakeDone,
            "1018 did not complete the native handshake");
        Equal("1.2.3.4", exact.m_sNativeClientVersion,
            "1018 native version format");
        Assert(exact.m_boNativeSwitchOffsetB75,
            "exact required version did not set B75");
        Assert(exact.ShouldDispatchNativeClientMessage(
                new ClientPacket { Ident = Grobal2.CM_QUERYBAGITEMS }),
            "post-handshake message was not dispatched");
        Assert(exact.ShouldDispatchNativeClientMessage(
                new ClientPacket { Ident = Grobal2.CM_LOGINNOTICEOK }),
            "repeated 1018 must reach the final no-op arm");

        var mismatch = new TPlayObject();
        mismatch.ShouldDispatchNativeClientMessage(new ClientPacket
        {
            Ident = Grobal2.CM_LOGINNOTICEOK,
            Recog = 1,
            Param = 2,
            Tag = 3,
            Series = 5
        });
        Equal("1.2.3.5", mismatch.m_sNativeClientVersion,
            "mismatch version format");
        Assert(!mismatch.m_boNativeSwitchOffsetB75,
            "mismatched version set B75");

        NativeClientVersionPolicy.SetRequiredVersion(string.Empty);
        var failOpen = new TPlayObject();
        failOpen.ShouldDispatchNativeClientMessage(new ClientPacket
        {
            Ident = Grobal2.CM_LOGINNOTICEOK,
            Recog = -1,
            Param = ushort.MaxValue,
            Tag = ushort.MaxValue,
            Series = ushort.MaxValue
        });
        Assert(failOpen.m_boNativeSwitchOffsetB75,
            "empty required version must allow any reported version");

        NativeClientVersionPolicy.SetRequiredVersion("never-match");
        var truncated = new TPlayObject();
        truncated.ShouldDispatchNativeClientMessage(new ClientPacket
        {
            Ident = Grobal2.CM_LOGINNOTICEOK,
            Recog = int.MaxValue,
            Param = ushort.MaxValue,
            Tag = ushort.MaxValue,
            Series = ushort.MaxValue
        });
        Equal("2147483647.6553", truncated.m_sNativeClientVersion,
            "native ShortString[15] truncation");

        var gm = new TPlayObject
        {
            m_btPermission = 3,
            m_boNativeSwitchOffsetB75 = false
        };
        gm.InitializeNativeClientVersionRunGate(12_345);
        Assert(gm.m_boNativeSwitchOffsetB75,
            "permission >=3 did not set native B75 bypass");
        Equal(12_345, gm.m_dwNativeClientVersionCheckTick,
            "native +0x738 login baseline");
        var ordinaryRestored = new TPlayObject
        {
            m_btPermission = 2,
            m_boNativeSwitchOffsetB75 = true
        };
        ordinaryRestored.InitializeNativeClientVersionRunGate(54_321);
        Assert(ordinaryRestored.m_boNativeSwitchOffsetB75,
            "ordinary login cleared a restored mode-2 B75 value");

        var readyMismatch = new TPlayObject
        {
            m_boReadyRun = true,
            m_btPermission = 0,
            m_sNativeClientVersion = "1.2.3.5",
            m_boNativeSwitchOffsetB75 = true
        };
        var readyMatch = new TPlayObject
        {
            m_boReadyRun = true,
            m_btPermission = 0,
            m_sNativeClientVersion = "1.2.3.4",
            m_boNativeSwitchOffsetB75 = false
        };
        var readyGm = new TPlayObject
        {
            m_boReadyRun = true,
            m_btPermission = 3,
            m_sNativeClientVersion = "1.2.3.5",
            m_boNativeSwitchOffsetB75 = true
        };
        var notReady = new TPlayObject
        {
            m_boReadyRun = false,
            m_sNativeClientVersion = "1.2.3.5",
            m_boNativeSwitchOffsetB75 = true
        };
        var noVersion = new TPlayObject
        {
            m_boReadyRun = true,
            m_sNativeClientVersion = string.Empty,
            m_boNativeSwitchOffsetB75 = true
        };
        NativeClientVersionPolicy.SetRequiredVersion("1.2.3.4");
        Equal(1, NativeClientVersionPolicy.RevalidatePlayers(new[]
            {
                readyMismatch, readyMatch, readyGm, notReady, noVersion
            }), "@ClientVersion native mismatch count");
        Assert(!readyMismatch.m_boNativeSwitchOffsetB75,
            "@ClientVersion did not clear mismatched B75");
        Assert(!readyMatch.m_boNativeSwitchOffsetB75,
            "@ClientVersion incorrectly promoted a matching B75");
        Assert(readyGm.m_boNativeSwitchOffsetB75 &&
               notReady.m_boNativeSwitchOffsetB75 &&
               noVersion.m_boNativeSwitchOffsetB75,
            "@ClientVersion changed a player excluded by the native sweep");
        NativeClientVersionPolicy.SetRequiredVersion(string.Empty);
        readyMismatch.m_boNativeSwitchOffsetB75 = true;
        Equal(0, NativeClientVersionPolicy.RevalidatePlayers(
            new[] { readyMismatch }),
            "empty @ClientVersion policy must skip the sweep");
        Assert(readyMismatch.m_boNativeSwitchOffsetB75,
            "empty policy changed B75");

        var falseCodec = new TPlayObject
        {
            m_boNativeSwitchOffsetB75 = false
        };
        Assert(NativeSwitchDataCodec.TryEncode(falseCodec, 1_000,
            out var extension, out var codecError), codecError);
        Assert((BinaryPrimitives.ReadUInt16LittleEndian(
                    extension.AsSpan(8, 2)) & 0x20) == 0,
            "mode-2 sender retained B75 bit for false");
        var codecReceiver = new TPlayObject
        {
            m_boNativeSwitchOffsetB75 = true
        };
        Assert(NativeSwitchDataCodec.TryRestore(codecReceiver, extension,
            1_100, out var codecRestored, out codecError) && codecRestored,
            codecError);
        Assert(!codecReceiver.m_boNativeSwitchOffsetB75,
            "mode-2 restore failed to overwrite a preset true B75 with false");
        Assert(!codecReceiver.m_boNativeClientVersionHandshakeDone,
            "raw mode-2 codec restore completed the outer login handshake");
        codecReceiver.ApplyNativeClientVersionReconnectBypass(codecRestored);
        Assert(codecReceiver.m_boNativeClientVersionHandshakeDone,
            "mode-2 reconnect did not bypass the 1018 handshake");
        var freshLogin = new TPlayObject();
        freshLogin.ApplyNativeClientVersionReconnectBypass(false);
        Assert(!freshLogin.m_boNativeClientVersionHandshakeDone,
            "fresh login incorrectly bypassed the 1018 handshake");

        var penalty = NewClientVersionPenaltyPlayer(10_000);
        penalty.RunNativeClientVersionGate(24_999);
        Assert(!penalty.HasNativeActiveState(25),
            "B75 penalty ran before 15000 ms");
        Equal(10_000, penalty.m_dwNativeClientVersionCheckTick,
            "early B75 check advanced the cadence clock");
        penalty.RunNativeClientVersionGate(25_000);
        Assert(penalty.HasNativeActiveState(25),
            "B75 penalty missing at exact 15000 ms");
        Equal(0, penalty.GetNativeTimedAbilityValue(25),
            "B75 penalty state value");
        Equal(600_000,
            penalty.GetNativeTimedAbilityRemainingMilliseconds(25),
            "B75 penalty state duration");
        Assert(!penalty.m_boNativeSwitchOffsetB75,
            "state 25 acquisition wrote B75");
        Assert(penalty.m_MsgList.Any(message =>
                message.wIdent == 10_000 && message.boLateDelivery),
            "B75 penalty did not queue delayed self-message 10000");

        Assert(penalty.ReduceNativeTimedAbilityRemaining(25, 100_000),
            "B75 penalty duration reduction setup");
        penalty.RunNativeClientVersionGate(39_999);
        Equal(500_000,
            penalty.GetNativeTimedAbilityRemainingMilliseconds(25),
            "B75 penalty refreshed before the next 15-second boundary");
        penalty.RunNativeClientVersionGate(40_000);
        Equal(600_000,
            penalty.GetNativeTimedAbilityRemainingMilliseconds(25),
            "B75 penalty did not refresh at the next 15-second boundary");

        var allowed = NewClientVersionPenaltyPlayer(1_000);
        allowed.m_boNativeSwitchOffsetB75 = true;
        allowed.RunNativeClientVersionGate(16_000);
        Assert(!allowed.HasNativeActiveState(25),
            "B75=true incorrectly applied state 25");
        Equal(16_000, allowed.m_dwNativeClientVersionCheckTick,
            "B75=true path did not advance the cadence clock");
        allowed.m_boNativeSwitchOffsetB75 = false;
        allowed.RunNativeClientVersionGate(30_999);
        Assert(!allowed.HasNativeActiveState(25),
            "B75 false transition bypassed the next full interval");
        allowed.RunNativeClientVersionGate(31_000);
        Assert(allowed.HasNativeActiveState(25),
            "B75 false transition missing at the next full interval");

        var blocked = NewClientVersionPenaltyPlayer(5_000);
        blocked.SetNativeActiveState(52);
        blocked.RunNativeClientVersionGate(20_000);
        Assert(!blocked.HasNativeActiveState(25),
            "state 52 failed to block B75 state 25");
        Equal(20_000, blocked.m_dwNativeClientVersionCheckTick,
            "blocked B75 call did not advance cadence");
        blocked.ClearNativeActiveState(52);
        blocked.RunNativeClientVersionGate(34_999);
        Assert(!blocked.HasNativeActiveState(25),
            "blocked B75 call retried before a full interval");
        blocked.RunNativeClientVersionGate(35_000);
        Assert(blocked.HasNativeActiveState(25),
            "blocked B75 call did not retry at the next interval");

        var wrap = NewClientVersionPenaltyPlayer(
            unchecked((int)0xFFFFFF00u));
        wrap.RunNativeClientVersionGate(unchecked((int)0x00003997u));
        Assert(!wrap.HasNativeActiveState(25),
            "B75 unsigned wrap 14999 ms boundary");
        wrap.RunNativeClientVersionGate(unchecked((int)0x00003998u));
        Assert(wrap.HasNativeActiveState(25),
            "B75 unsigned wrap 15000 ms boundary");

        var lowLevel = NewClientVersionPenaltyPlayer(0);
        lowLevel.m_sCharName = "b75-low-level";
        lowLevel.m_Abil.Level = 7;
        M2Share.g_DenySayMsgList.TryRemove(lowLevel.m_sCharName, out _);
        var muteStart = HUtil32.GetTickCount();
        lowLevel.RunNativeClientVersionGate(15_000);
        Assert(M2Share.g_DenySayMsgList.TryGetValue(
                lowLevel.m_sCharName, out var muteUntil),
            "B75 level<8 did not enter the native deny-say list");
        var muteDuration = muteUntil - muteStart;
        Assert(muteDuration is >= 3_600_000 and <= 3_601_000,
            $"B75 low-level mute duration: {muteDuration}");

        var softClose = new TPlayObject();
        Assert(softClose.Operate(new TProcessMessage { wIdent = 10_000 }),
            "internal self-message 10000 was not handled");
        Assert(softClose.m_boSoftClose,
            "internal self-message 10000 did not set m_boSoftClose");
    }
    finally
    {
        M2Share.ServerSwitches = originalServerSwitches;
        NativeClientVersionPolicy.SetRequiredVersion(string.Empty);
    }
}

static TPlayObject NewClientVersionPenaltyPlayer(int baselineTick)
{
    var player = new TPlayObject
    {
        m_sCharName = "b75-penalty-" + Guid.NewGuid().ToString("N"),
        // Keep packet writes inside the in-process probe; the state engine and
        // internal delayed-message queue remain live while SendSocket returns.
        m_boOffLineFlag = true,
        m_boNativeSwitchOffsetB75 = false,
        m_dwNativeClientVersionCheckTick = baselineTick
    };
    player.m_Abil.Level = 8;
    return player;
}

// ---------------------------------------------------------------------------
// Mode2 flag 0x10, sub_6B188C/sub_6BF5FC and TPlayer.Run sub_6B2D38.
// Restore records the old kind's clock before replacing kind/slot. Run tests
// the new kind's clock, clears pending before the one-shot load attempt, and
// preserves unsigned GetTickCount wraparound.
static void VerifyNativeSwitchHeroHandoff()
{
    const int restoreTick = 10_000;
    var delayed = NewNativeSwitchHeroPlayer(0);
    Assert(NativeSwitchDataCodec.TryRestore(delayed,
        NewNativeSwitchHeroExtension(0, 3), restoreTick,
        out var restored, out var error) && restored, error);
    Equal(restoreTick, delayed.m_dwNativeSwitchHeroKind0Tick,
        "same-kind handoff old-kind clock");
    Assert(!delayed.TryConsumeNativeSwitchHeroHandoff(
            restoreTick + 4_999, out var requestedKind,
            out var requestedSlot),
        "switch hero loaded before 5000 ms");
    Assert(delayed.m_boNativeSwitchHeroHandoffPending,
        "early switch hero check cleared pending");
    Assert(delayed.TryConsumeNativeSwitchHeroHandoff(
            restoreTick + 5_000, out requestedKind, out requestedSlot),
        "switch hero did not load at exact 5000 ms");
    Equal((byte)0, requestedKind, "switch hero requested kind");
    Equal((byte)3, requestedSlot, "switch hero requested slot");
    Assert(!delayed.m_boNativeSwitchHeroHandoffPending,
        "switch hero due check did not clear pending");
    Equal(restoreTick + 5_000, delayed.m_dwNativeSwitchHeroKind0Tick,
        "switch hero due check did not refresh current-kind clock");

    var wrapped = NewNativeSwitchHeroPlayer(0);
    var wrapStart = unchecked((int)0xFFFFFF00u);
    Assert(NativeSwitchDataCodec.TryRestore(wrapped,
        NewNativeSwitchHeroExtension(0, 1), wrapStart,
        out restored, out error) && restored, error);
    Assert(!wrapped.TryConsumeNativeSwitchHeroHandoff(
            unchecked((int)0x00001287u), out _, out _),
        "switch hero wraparound 4999 ms boundary");
    Assert(wrapped.TryConsumeNativeSwitchHeroHandoff(
            unchecked((int)0x00001288u), out _, out _),
        "switch hero wraparound 5000 ms boundary");

    var kindOne = NewNativeSwitchHeroPlayer(1);
    Assert(NativeSwitchDataCodec.TryRestore(kindOne,
        NewNativeSwitchHeroExtension(1, 4), restoreTick,
        out restored, out error) && restored, error);
    Equal(restoreTick, kindOne.m_dwNativeSwitchHeroKind1Tick,
        "same-kind handoff old-kind 1 clock");
    Assert(!kindOne.TryConsumeNativeSwitchHeroHandoff(
            restoreTick + 4_999, out _, out _),
        "kind 1 switch hero loaded before 5000 ms");
    Assert(kindOne.TryConsumeNativeSwitchHeroHandoff(
            restoreTick + 5_000, out requestedKind, out requestedSlot),
        "kind 1 switch hero did not load at exact 5000 ms");
    Equal((byte)1, requestedKind, "kind 1 switch hero requested kind");
    Equal((byte)4, requestedSlot, "kind 1 switch hero requested slot");

    var crossKind = NewNativeSwitchHeroPlayer(0);
    Assert(NativeSwitchDataCodec.TryRestore(crossKind,
        NewNativeSwitchHeroExtension(1, 2), restoreTick,
        out restored, out error) && restored, error);
    Equal(restoreTick, crossKind.m_dwNativeSwitchHeroKind0Tick,
        "cross-kind restore must stamp old kind 0");
    Equal(0, crossKind.m_dwNativeSwitchHeroKind1Tick,
        "cross-kind restore must leave incoming kind 1 clock untouched");
    Assert(crossKind.TryConsumeNativeSwitchHeroHandoff(
            restoreTick + 1, out requestedKind, out requestedSlot),
        "old kind 0 -> incoming kind 1 must preserve native immediate quirk");
    Equal((byte)1, requestedKind, "cross-kind requested kind");
    Equal((byte)2, requestedSlot, "cross-kind requested slot");

    var reverseCrossKind = NewNativeSwitchHeroPlayer(1);
    Assert(NativeSwitchDataCodec.TryRestore(reverseCrossKind,
        NewNativeSwitchHeroExtension(0, 5), restoreTick,
        out restored, out error) && restored, error);
    Equal(restoreTick, reverseCrossKind.m_dwNativeSwitchHeroKind1Tick,
        "cross-kind restore must stamp old kind 1");
    Equal(0, reverseCrossKind.m_dwNativeSwitchHeroKind0Tick,
        "cross-kind restore must leave incoming kind 0 clock untouched");
    Assert(reverseCrossKind.TryConsumeNativeSwitchHeroHandoff(
            restoreTick + 1, out requestedKind, out requestedSlot),
        "old kind 1 -> incoming kind 0 must preserve native immediate quirk");
    Equal((byte)0, requestedKind, "reverse cross-kind requested kind");
    Equal((byte)5, requestedSlot, "reverse cross-kind requested slot");

    var unsupported = NewNativeSwitchHeroPlayer(0);
    Assert(NativeSwitchDataCodec.TryRestore(unsupported,
        NewNativeSwitchHeroExtension(2, 4), restoreTick,
        out restored, out error) && restored, error);
    Assert(!unsupported.TryConsumeNativeSwitchHeroHandoff(
            int.MaxValue, out _, out _),
        "unsupported switch hero kind requested a load");
    Assert(unsupported.m_boNativeSwitchHeroHandoffPending,
        "unsupported switch hero kind must leave pending armed");

    foreach (var gate in new[] { "DARE", "NOHERO", "HERO", "EQUIP" })
    {
        var gated = NewNativeSwitchHeroPlayer(0);
        Assert(NativeSwitchDataCodec.TryRestore(gated,
            NewNativeSwitchHeroExtension(0, 1), restoreTick,
            out restored, out error) && restored, error);
        switch (gate)
        {
            case "DARE": gated.m_PEnvir.Flag.boDARE = true; break;
            case "NOHERO": gated.m_PEnvir.Flag.boNOHERO = true; break;
            case "HERO": gated.m_HeroObject = new HeroObject(); break;
            case "EQUIP": Set(gated, "_nativeEquipLockActive", true); break;
        }
        Assert(!gated.TryConsumeNativeSwitchHeroHandoff(
                restoreTick + 5_000, out _, out _),
            $"switch hero {gate} gate requested a load");
        Assert(!gated.m_boNativeSwitchHeroHandoffPending,
            $"switch hero {gate} gate must clear pending before callback");
    }

    var ghostPlayer = NewNativeSwitchHeroPlayer(0);
    Assert(NativeSwitchDataCodec.TryRestore(ghostPlayer,
        NewNativeSwitchHeroExtension(0, 1), restoreTick,
        out restored, out error) && restored, error);
    ghostPlayer.m_boGhost = true;
    Assert(!ghostPlayer.TryConsumeNativeSwitchHeroHandoff(
            restoreTick + 5_000, out _, out _),
        "ghost player requested a switch hero load");
    Assert(ghostPlayer.m_boNativeSwitchHeroHandoffPending,
        "ghost player must leave switch hero pending armed");

    var ghostHero = NewNativeSwitchHeroPlayer(0);
    Assert(NativeSwitchDataCodec.TryRestore(ghostHero,
        NewNativeSwitchHeroExtension(0, 1), restoreTick,
        out restored, out error) && restored, error);
    ghostHero.m_HeroObject = new HeroObject { m_boGhost = true };
    Assert(ghostHero.TryConsumeNativeSwitchHeroHandoff(
            restoreTick + 5_000, out _, out _),
        "cleared ghost hero blocked the due switch hero load");
    Assert(ghostHero.m_HeroObject == null,
        "ghost hero pointer was not cleared before handoff gates");

    var runPlayer = NewNativeSwitchHeroPlayer(0);
    runPlayer.m_boNativeSwitchOffsetB75 = true;
    runPlayer.InitializeNativeClientVersionRunGate(HUtil32.GetTickCount());
    runPlayer.m_nNativeSwitchOffsetD3C = 123;
    runPlayer.Run();
    Equal(0, runPlayer.m_nNativeSwitchOffsetD3C,
        "TPlayer.Run must clear native switch D3C every tick");
}

// ---------------------------------------------------------------------------
// THeroAct RM_10401 -> sub_68FAB8. This path is deliberately separate from
// TPlayer.ChangeServerMakeSlave: it uses the hero as the physical anchor,
// keeps the player as owner, restores every race's fields, and special-cases
// only race 0x82 by copying the hero level before the final RecalcAbilitys.
static void VerifyHeroSlaveRecordRestore()
{
    const string normalName = "HeroRecordNormal";
    const string race130Name = "HeroRecordRace130";
    const string race151Name = "HeroRecordRace151";
    M2Share.UserEngine.MonsterList.Clear();
    M2Share.UserEngine.MonsterList.Add(NewSummonMonsterInfo(normalName,
        (byte)M2Share.MONSTER_OMA, 3000, 3000));
    M2Share.UserEngine.MonsterList.Add(NewSummonMonsterInfo(race130Name,
        0x82, 3000, 3000));
    M2Share.UserEngine.MonsterList.Add(NewSummonMonsterInfo(race151Name,
        0x97, 3000, 3000));
    M2Share.UserEngine.m_MonGenList.Clear();
    M2Share.UserEngine.m_MonGenList.Add(new MonGenInfo
    {
        CertList = new List<TBaseObject>()
    });

    var normalMasterMap = NewSummonEnvironment("HeroRecordNormalMaster",
        "hero-record-normal-master");
    var normalHeroMap = NewSummonEnvironment("HeroRecordNormalHero",
        "hero-record-normal-hero");
    var normalMaster = NewSummonMaster("hero-record-normal-master",
        normalMasterMap, 3, 3, Grobal2.DR_RIGHT);
    normalMaster.m_btJob = 1;
    var normalHero = AttachSummonHero(normalMaster, normalHeroMap,
        8, 8, Grobal2.DR_LEFT);
    Assert(normalHero.Operate(new TProcessMessage
    {
        wIdent = Grobal2.RM_10401,
        Payload = new TSlaveInfo
        {
            sSlaveName = normalName,
            btSlaveLevel = 2,
            btSlaveExpLevel = 5,
            nKillCount = 77,
            dwRoyaltySec = 90,
            nHP = -1,
            nMP = 0x12345
        }
    }), "hero RM_10401 was not handled");
    var normal = normalMaster.m_SlaveList.Single();
    CheckSummonResult(normal, normalMaster, normalHero, normalHeroMap,
        7, 8, normalName, "hero record normal restore");
    Assert(ReferenceEquals(normal,
            Get(normalHero, "m_NativeHeroSummonSlave")),
        "hero record restore did not publish hero+0x6C4");
    Equal((byte)2, normal.m_btSlaveMakeLevel,
        "hero record +0x1D make level");
    Equal((byte)5, normal.m_btSlaveExpLevel,
        "hero record +0x1C exp level");
    Equal(77, normal.m_nKillMonCount,
        "hero record +0x10 kill count");
    Equal(0xFFFF, normal.m_WAbil.HP,
        "hero record +0x18 HP WORD");
    Equal(0x2345, normal.m_WAbil.MP,
        "hero record +0x1A MP WORD");
    Equal(1100, normal.m_nWalkSpeed,
        "hero record walk-speed cap");
    Equal(1600, normal.m_nNextHitTime,
        "hero record attack-speed cap");
    Equal(10, ((AnimalObject)normal).m_nNativeHpAfterSlavePercent,
        "hero record hpAfterSlave literal");

    var race130MasterMap = NewSummonEnvironment("HeroRecord130Master",
        "hero-record-130-master");
    var race130HeroMap = NewSummonEnvironment("HeroRecord130Hero",
        "hero-record-130-hero");
    var race130Master = NewSummonMaster("hero-record-130-master",
        race130MasterMap, 3, 3, Grobal2.DR_RIGHT);
    var race130Hero = AttachSummonHero(race130Master, race130HeroMap,
        8, 8, Grobal2.DR_LEFT);
    race130Hero.m_Abil.Level = 55;
    race130Hero.Operate(new TProcessMessage
    {
        wIdent = Grobal2.RM_10401,
        Payload = new TSlaveInfo
        {
            sSlaveName = race130Name,
            btSlaveLevel = 1,
            btSlaveExpLevel = 4,
            nKillCount = 22,
            dwRoyaltySec = 60,
            nHP = 333,
            nMP = 222
        }
    });
    var race130 = race130Master.m_SlaveList.Single();
    Equal((ushort)55, race130.m_Abil.Level,
        "hero record race 0x82 level copy");
    Equal(22, race130.m_nKillMonCount,
        "hero record race 0x82 kill restore");
    Equal((byte)4, race130.m_btSlaveExpLevel,
        "hero record race 0x82 exp restore");

    var race151MasterMap = NewSummonEnvironment("HeroRecord151Master",
        "hero-record-151-master");
    var race151HeroMap = NewSummonEnvironment("HeroRecord151Hero",
        "hero-record-151-hero");
    var race151Master = NewSummonMaster("hero-record-151-master",
        race151MasterMap, 3, 3, Grobal2.DR_RIGHT);
    var race151Hero = AttachSummonHero(race151Master, race151HeroMap,
        8, 8, Grobal2.DR_LEFT);
    race151Hero.Operate(new TProcessMessage
    {
        wIdent = Grobal2.RM_10401,
        Payload = new TSlaveInfo
        {
            sSlaveName = race151Name,
            btSlaveLevel = 3,
            btSlaveExpLevel = 7,
            nKillCount = 99,
            dwRoyaltySec = 60,
            nHP = 444,
            nMP = 111
        }
    });
    var race151 = race151Master.m_SlaveList.Single() as HolyMonster;
    Assert(race151 != null,
        "hero record race 151 did not create THolyMonster");
    Equal(99, race151!.m_nKillMonCount,
        "hero record race 151 must restore kill count");
    Equal((byte)7, race151.m_btSlaveExpLevel,
        "hero record race 151 must restore exp level");
    Assert(Get(race151, "m_NativeHolyBeastSummoner") == null,
        "hero record race 151 incorrectly took player restore binding");

    var occupiedMap = NewSummonEnvironment("HeroRecordOccupied",
        "hero-record-occupied");
    var occupiedMaster = NewSummonMaster("hero-record-occupied-master",
        occupiedMap, 3, 3, Grobal2.DR_RIGHT);
    var occupiedHero = AttachSummonHero(occupiedMaster, occupiedMap,
        8, 8, Grobal2.DR_LEFT);
    Set(occupiedHero, "m_NativeHeroSummonSlave", new Monster());
    occupiedHero.Operate(new TProcessMessage
    {
        wIdent = Grobal2.RM_10401,
        Payload = new TSlaveInfo { sSlaveName = normalName }
    });
    Assert(Get(occupiedHero, "m_NativeHeroSummonSlave") == null,
        "occupied hero restore gate did not clear hero+0x6C4");
    Equal(0, occupiedMaster.m_SlaveList.Count,
        "occupied hero restore gate spawned a slave");
}

// ---------------------------------------------------------------------------
// TPlayer.MakeSlave sub_6CB070: [ebp+0x0C] is BoFromHero. It changes the
// physical environment and position anchor only; +0x38C, +0x4FC and the 4469
// sender remain the master player (0x6CB2D4 / 0x6CB348 / 0x6CB355).
static void VerifyHeroSummonSpawnContext()
{
    const string monsterName = "HeroSpawnContextOma";
    const string sharedMapName = "HeroSpawnShared";

    M2Share.UserEngine.MonsterList.Clear();
    M2Share.UserEngine.MonsterList.Add(NewSummonMonsterInfo(monsterName));
    M2Share.UserEngine.m_MonGenList.Clear();
    M2Share.UserEngine.m_MonGenList.Add(new MonGenInfo
    {
        CertList = new List<TBaseObject>()
    });

    var registeredDecoy = NewSummonEnvironment(sharedMapName,
        "registered-decoy");
    RegisterSummonMap(registeredDecoy);

    // A same-name registered map must not steal a hero summon from the hero's
    // unregistered physical instance.
    var masterMap = NewSummonEnvironment("HeroSpawnMaster", "master");
    var heroMap = NewSummonEnvironment(sharedMapName, "hero-physical");
    var master = NewSummonMaster("hero-context-master", masterMap, 3, 3,
        Grobal2.DR_RIGHT);
    var hero = AttachSummonHero(master, heroMap, 8, 8, Grobal2.DR_LEFT);
    var slave = master.MakeNativeSlave(monsterName, 2, 8, 60,
        fromHero: true, hpAfterSlave: 10);

    CheckSummonResult(slave, master, hero, heroMap, 7, 8, monsterName,
        "valid hero physical context");
    Equal(0, masterMap.MonCount,
        "hero summon changed the master's environment count");
    Equal(0, registeredDecoy.MonCount,
        "hero summon resolved through the same-name registered map");

    // The public five-argument ABI remains the ordinary-player path even when
    // the player owns a valid hero in another environment.
    var ordinaryMap = NewSummonEnvironment("OrdinarySummonMaster", "ordinary");
    var ordinaryHeroMap = NewSummonEnvironment("OrdinarySummonHero", "ordinary-hero");
    var ordinaryMaster = NewSummonMaster("ordinary-master", ordinaryMap,
        4, 4, Grobal2.DR_RIGHT);
    var ordinaryHero = AttachSummonHero(ordinaryMaster, ordinaryHeroMap,
        9, 9, Grobal2.DR_LEFT);
    var ordinarySlave = ordinaryMaster.MakeSlave(monsterName, 2, 2, 8, 60);
    CheckSummonResult(ordinarySlave, ordinaryMaster, ordinaryHero,
        ordinaryMap, 5, 4, monsterName, "ordinary five-argument context");
    Equal(0, ordinaryHeroMap.MonCount,
        "ordinary summon incorrectly used the hero environment");

    // Hero +0x6C4 maintenance and nCount+1 precede the BoFromHero test.
    // With one existing slave and nMaxMob=1, a live recorded hero summon must
    // admit one more ordinary summon; a dead record must be cleared and must
    // not increase the limit.
    var liveSlotMap = NewSummonEnvironment("LiveHeroSlotMaster", "live-slot");
    var liveSlotMaster = NewSummonMaster("live-slot-master", liveSlotMap,
        4, 4, Grobal2.DR_RIGHT);
    var liveSlotHero = AttachSummonHero(liveSlotMaster,
        NewSummonEnvironment("LiveHeroSlotHero", "live-slot-hero"),
        9, 9, Grobal2.DR_LEFT);
    var liveRecordedSlave = new TBaseObject { m_sCharName = "live-recorded-slave" };
    liveSlotMaster.m_SlaveList.Add(liveRecordedSlave);
    Set(liveSlotHero, "m_NativeHeroSummonSlave", liveRecordedSlave);
    var liveSlotSpawn = liveSlotMaster.MakeSlave(monsterName, 2, 2, 1, 60);
    Assert(liveSlotSpawn != null &&
           ReferenceEquals(liveSlotMap, liveSlotSpawn.m_PEnvir),
        "live hero summon slot did not increase ordinary nMaxMob");
    Equal(2, liveSlotMaster.m_SlaveList.Count,
        "live hero summon slot ordinary slave count");

    var deadSlotMap = NewSummonEnvironment("DeadHeroSlotMaster", "dead-slot");
    var deadSlotMaster = NewSummonMaster("dead-slot-master", deadSlotMap,
        4, 4, Grobal2.DR_RIGHT);
    var deadSlotHero = AttachSummonHero(deadSlotMaster,
        NewSummonEnvironment("DeadHeroSlotHero", "dead-slot-hero"),
        9, 9, Grobal2.DR_LEFT);
    var deadRecordedSlave = new TBaseObject
    {
        m_sCharName = "dead-recorded-slave",
        m_boDeath = true
    };
    deadSlotMaster.m_SlaveList.Add(deadRecordedSlave);
    Set(deadSlotHero, "m_NativeHeroSummonSlave", deadRecordedSlave);
    Assert(deadSlotMaster.MakeSlave(monsterName, 2, 2, 1, 60) == null,
        "dead hero summon slot incorrectly increased ordinary nMaxMob");
    Assert(Get(deadSlotHero, "m_NativeHeroSummonSlave") == null,
        "dead hero summon slot was not cleared before BoFromHero");

    foreach (var invalidCase in new[] { "null", "death", "ghost", "map-null" })
    {
        var fallbackMap = NewSummonEnvironment(
            "HeroFallback-" + invalidCase, "fallback-" + invalidCase);
        var fallbackMaster = NewSummonMaster(
            "fallback-master-" + invalidCase, fallbackMap,
            6, 6, Grobal2.DR_RIGHT);
        HeroObject invalidHero = null;
        if (invalidCase != "null")
        {
            var invalidHeroMap = invalidCase == "map-null"
                ? null
                : NewSummonEnvironment("InvalidHero-" + invalidCase,
                    "invalid-hero-" + invalidCase);
            invalidHero = AttachSummonHero(fallbackMaster, invalidHeroMap,
                10, 10, Grobal2.DR_LEFT);
            invalidHero.m_boDeath = invalidCase == "death";
            invalidHero.m_boGhost = invalidCase == "ghost";
        }

        var fallbackSlave = fallbackMaster.MakeNativeSlave(monsterName, 2,
            8, 60, fromHero: true, hpAfterSlave: 10);
        CheckSummonResult(fallbackSlave, fallbackMaster, invalidHero,
            fallbackMap, 7, 6, monsterName,
            "invalid hero fallback " + invalidCase);
    }

    // Both (7,8) and (8,7) are open. X-outer/Y-inner reaches (7,8) first;
    // Y-outer/X-inner would reach (8,7). The hero's down-facing front (8,9)
    // stays blocked so the 3x3 branch is mandatory.
    var scanMasterMap = NewSummonEnvironment("HeroScanMaster", "scan-master");
    var scanHeroMap = NewSummonEnvironment("HeroScanPhysical", "scan-hero");
    BlockAllSummonCells(scanHeroMap);
    scanHeroMap.SetMapXYFlag(7, 8, true);
    scanHeroMap.SetMapXYFlag(8, 7, true);
    var scanMaster = NewSummonMaster("scan-master", scanMasterMap,
        3, 3, Grobal2.DR_RIGHT);
    var scanHero = AttachSummonHero(scanMaster, scanHeroMap,
        8, 8, Grobal2.DR_DOWN);
    var scanSlave = scanMaster.MakeNativeSlave(monsterName, 2, 8, 60,
        fromHero: true, hpAfterSlave: 10);
    CheckSummonResult(scanSlave, scanMaster, scanHero, scanHeroMap,
        7, 8, monsterName, "hero 3x3 X-outer Y-inner order");
}

// ---------------------------------------------------------------------------
static void VerifySourceContracts()
{
    string manager = ReadSource("GameSvr", "Spells", "MagicManager.cs");
    string magic = ReadSource("GameSvr", "Spells", "Magic.cs");
    string baseObject = ReadSource("GameSvr", "Actors", "TBaseObject.cs");
    string heroData = ReadSource("GameSvr", "Services", "HeroDataService.cs");
    string operate = ReadSource("GameSvr", "Players", "TPlayObject.Operate.cs");
    string switchCodec = ReadSource("GameSvr", "Services",
        "NativeSwitchDataCodec.cs");
    string switchHero = ReadSource("GameSvr", "Players",
        "TPlayObject.NativeSwitchHeroHandoff.cs");
    string playerRun = ReadSource("GameSvr", "Players",
        "TPlayObject.Message.cs");
    string clientVersionGate = ReadSource("GameSvr", "Players",
        "TPlayObject.NativeClientVersionGate.cs");
    string userEngine = ReadSource("GameSvr", "UsrSystem", "UsrEngn.cs");
    string playerBase = ReadSource("GameSvr", "Players",
        "TPlayObject.Base.cs");
    string timedAbility = ReadSource("GameSvr", "Actors",
        "TBaseObject.TimedAbility.cs");
    string clientVersionCommand = ReadSource("GameSvr", "Command",
        "Commands", "ClientVersionCommand.cs");
    Require(playerRun, "RunNativeClientVersionGate(currentTick);",
        "TPlayer.Run must poll the native B75 client-version gate");
    Require(userEngine,
        "PlayObject.ShouldDispatchNativeClientMessage(DefMsg)",
        "the native client-version handshake must run before CM dispatch");
    Require(userEngine, "ApplyNativeClientVersionReconnectBypass(",
        "mode-2 login must invoke the reconnect bypass");
    Require(userEngine, "nativeSwitchRestored);",
        "mode-2 login must bypass 1018 only after a successful restore");
    Require(playerBase,
        "InitializeNativeClientVersionRunGate(HUtil32.GetTickCount());",
        "UserLogon must seed +0x738 and apply the permission bypass");
    Require(clientVersionGate,
        "NativeMakePosion(25, NativeClientVersionPenaltySeconds, 0);",
        "B75 false must request native state 25 with (600,0)");
    Require(clientVersionGate,
        "IsClientInfoCollectionEnabled();",
        "pre-handshake 3340 must use ServerSwitch byte 3 bit 5");
    Require(clientVersionGate,
        "SendDelayMsg(this, NativeClientVersionDisconnectIdent,",
        "B75 false must queue the 500 ms self-disconnect");
    Require(clientVersionCommand,
        "本服务器共有\" + mismatchCount +",
        "@ClientVersion must report the native mismatch count");
    Reject(timedAbility, "m_boNativeSwitchOffsetB75",
        "state gained/lost callbacks must not rewrite B75");
    int oldKindTickIndex = switchCodec.IndexOf(
        "player.RecordNativeSwitchHeroRequestTick(currentTick);",
        StringComparison.Ordinal);
    int incomingKindIndex = switchCodec.IndexOf(
        "player.m_btNativeHeroRequestKind = extension[HeroKindOffset];",
        StringComparison.Ordinal);
    Assert(oldKindTickIndex >= 0 && incomingKindIndex > oldKindTickIndex,
        "mode2 restore must record the old hero kind clock before replacing kind");
    Require(playerRun, "RunNativeSwitchHeroHandoff(currentTick);",
        "TPlayer.Run must poll the mode2 hero handoff");
    Require(MethodBody(switchHero, "RunNativeSwitchHeroHandoff"),
        "HeroDataService.RequestLoad(this, heroKind, heroSlot);",
        "the TPlayer.Run handoff wrapper must submit the consumed kind/slot to HeroDataService");
    string handoffRun = MethodBody(switchHero,
        "TryConsumeNativeSwitchHeroHandoff");
    int refreshIndex = handoffRun.IndexOf(
        "RecordNativeSwitchHeroRequestTick(currentTick);",
        StringComparison.Ordinal);
    int clearPendingIndex = handoffRun.IndexOf(
        "m_boNativeSwitchHeroHandoffPending = false;",
        StringComparison.Ordinal);
    int environmentGateIndex = handoffRun.IndexOf(
        "var environment = m_PEnvir;", StringComparison.Ordinal);
    Assert(refreshIndex >= 0 && clearPendingIndex > refreshIndex &&
           environmentGateIndex > clearPendingIndex,
        "switch hero due path must refresh clock and clear pending before load gates");
    Require(operate, "case \"TCallMonStone\":",
        "StdMode 2 Shape 25 must dispatch TCallMonStone.Use");
    Require(MethodBody(operate, "UseNativeCallMonStone"),
        "hpAfterSlave: 0",
        "TCallMonStone MakeSlave calls must write TAnimal +0x48C as zero");
    Require(baseObject, "MonObj.m_boNoItem = true;",
        "TPlayer.MakeSlave must set native slave+0x47D no-item-drop flag");
    Require(baseObject, "playObject.m_HeroObject.m_boNativeSwitchData = true;",
        "sub_6BD044 must mirror player+0x4BA into hero+0x4BA (@0x6BD096..0x6BD0A0)");
    Require(heroData, "nativeSwitchSlave?.Die();",
        "sub_689034 VMT+0x84 must run after the fixed snapshot and before request encoding/transport");
    string queueSave = MethodBody(heroData, "QueueSave");
    int snapshotSaveIndex = queueSave.IndexOf(
        "NativeHeroRuntimeCodec.TryCreateSnapshot", StringComparison.Ordinal);
    int switchDieIndex = queueSave.IndexOf(
        "nativeSwitchSlave?.Die();", StringComparison.Ordinal);
    int encodeSaveIndex = queueSave.IndexOf(
        "NativeHeroDbFrameCodec.TryEncodeSaveRequest", StringComparison.Ordinal);
    Assert(snapshotSaveIndex >= 0 && switchDieIndex > snapshotSaveIndex &&
           encodeSaveIndex > switchDieIndex,
        "hero switch slave Die must be between fixed snapshot creation and outbound save encoding");
    Require(heroData, "ConcurrentQueue<PendingSave>",
        "hero save frames must remain FIFO so an ordinary save cannot overwrite a consumed switch frame");
    Reject(heroData, "pending.NativeSwitchSlave?.Die();",
        "hero switch slave death must not be delayed until DB transport flush");
    Reject(ReadSource("GameSvr", "DataStores", "NativeHeroRuntimeCodec.cs"),
        "nativeSwitchSlave?.Die();",
        "rollback snapshot codec must remain side-effect free");

    // The no-skill-zone refusal MESSAGE CHANNEL. Native @0x6BC54F sends
    // `mov cx,0xFFDB` through vtable+0xD4 with the literal at 0x6BCD18. cx
    // unpacks as FColor = cx & 0xFF and BColor = cx >> 8 (see the playernotice
    // bridge in PasApiBridge), so 0xFFDB is the 0xDB/0xFF pair == MsgColor.Green
    // in GameSvrConfig. It was ported as MsgColor.Red (the 0x38FF pair), i.e.
    // the wrong channel; fixed 2026-08-08. Assert the derivation as well as the
    // call so the two cannot drift apart silently.
    var config = new GameSvrConfig();
    Equal(0xFFDB & 0xFF, (int)config.btGreenMsgFColor,
        "0xFFDB low byte must be btGreenMsgFColor (0xFFDB is the Green channel)");
    Equal((0xFFDB >> 8) & 0xFF, (int)config.btGreenMsgBColor,
        "0xFFDB high byte must be btGreenMsgBColor");
    string attack = ReadSource("GameSvr", "Players", "TPlayObject.Attack.cs");
    Require(attack,
        "SysMsg(\"当前区域不可使用该技能\", MsgColor.Green, MsgType.Hint);",
        "no-skill-zone refusal must use the 0xFFDB Green channel (@0x6BC54F), not Red");

    // Spell range: native sub_6ED62C @0x6ED67B is `cmp eax,9` for EVERY spell,
    // with no config read anywhere in the body.
    Require(manager, "const int magicAttackRange = 9;",
        "spell range must be the native hardcoded 9 (@0x6ED67B)");
    Reject(manager, "M2Share.g_Config.nMagicAttackRage",
        "spell range must not read config — native reads none");

    // 施毒术: literal 100, slot 9, and the free last cast. Native @0x6ED97C
    // `cmp word [eax+0x26],0x64; jb 0x6ED9B0` skips the decrement yet still
    // reaches the poison applier, so `Dura >= 100` must guard ONLY the
    // decrement — never the cast.
    string poison = SwitchCase(manager, "SpellsDef.SKILL_AMYOUNSUL");
    Require(poison, "poisonCharm.Dura -= 100;",
        "poison must decrement the native literal 100 (@0x6ED986)");
    Require(poison, "if (poisonCharm.Dura >= 100)",
        "the 100 decrement must be guarded by Dura >= 100 (@0x6ED97C jb)");
    Require(poison, "m_UseItems[Grobal2.U_BUJUK]",
        "poison must read slot 9 only (mov dl,9 @0x6ED949)");
    Reject(poison, "Magic.UseAmulet",
        "poison must NOT route through UseAmulet: that subtracts nCount*100 " +
        "(@0x73E989 imul ...,0x64) and drained charms 2x too fast");
    Reject(poison, "Magic.CheckAmulet",
        "poison must NOT route through CheckAmulet: native @0x6ED945 inlines " +
        "its own slot-9 fetch and TPoisons test, and CheckAmulet also " +
        "admitted U_ARMRINGL");
    Require(poison, "ConsumeSpentPoisonCharm(PlayObject, poisonCharm);",
        "the unconditional post-cast charm hook sub_73CC18 (@0x6ED9F0)");
    // The hook is unconditional in native, so it must sit AFTER the applier and
    // outside the AntiPoison roll.
    int applierIndex = poison.IndexOf("Grobal2.RM_POISON",
        StringComparison.Ordinal);
    int hookIndex = poison.IndexOf("ConsumeSpentPoisonCharm",
        StringComparison.Ordinal);
    Assert(applierIndex >= 0 && hookIndex > applierIndex,
        "sub_73CC18 runs after the poison applier (@0x6ED9EB follows the " +
        "VMT+0x110/+0x114 calls)");
    string hook = MethodBody(manager, "ConsumeSpentPoisonCharm");
    Require(hook, "charm.Dura >= 100",
        "sub_73CC18 only removes the charm once Dura < 100 (@0x73CC33 jae)");
    Require(hook, "PlayObject.m_UseItems[Grobal2.U_BUJUK] = null;",
        "sub_75F27C nulls the slot pointer (@0x75F2BB)");
    Require(hook, "PlayObject.RecalcAbilitys();",
        "sub_75F27C recalcs via VMT+0x8C (@0x75F2D9)");
    Reject(hook, "SysMsg",
        "\"持久耗尽\" is the reason column of the sub_768BE0 game-data log " +
        "(@0x75F328), NOT a player-visible message — do not invent one");

    // Generic UseAmulet keeps nCount*100 and must remove, not zero-and-keep.
    string useAmulet = MethodBody(magic, "UseAmulet");
    Require(useAmulet, "nCount * 100",
        "generic consume is nCount*100 (@0x73E989 imul eax,[ebp-4],0x64)");
    Require(useAmulet, "PlayObject.m_UseItems[Idx] = null;",
        "shortfall must remove via the sub_75F27C shape (@0x73E9DE)");
    Reject(useAmulet, "wIndex = 0",
        "native never zeroes wIndex in place — sub_75F27C nulls the slot");
    string checkAmulet = MethodBody(magic, "CheckAmulet");
    Require(checkAmulet, "nCount * 100 <= charm.Dura + 50",
        "native count predicate (@0x73E989-0x73E999)");
    Reject(checkAmulet, "HUtil32.Round",
        "the banker's-rounding predicate must stay deleted");
    Reject(checkAmulet, "U_ARMRINGL",
        "native reads only slot 9 (mov dl,9 @0x73E95F)");

    // Delayed AoE: 爆裂火焰 23 / 冰咆哮 33 queue a 600 ms category-3 effect and
    // apply nothing at cast time (sub_76F21C @0x76F26B push 0x258, @0x76F272
    // push 3, @0x76F270 push 1 = range).
    string blast = MethodBody(manager, "QueueNativeAreaBlast");
    Require(blast, "QueueNativeMagicEffect(3, null, rawDamage,",
        "AoE must queue dispatchCategory 3 with a nil target (@0x76F272 " +
        "push 3, @0x76F27E xor edx,edx)");
    // The range slot now goes through the 眼神 skill-range trampoline, which
    // returns its nativeDefault when the toggle is off — so the native 1 has to be
    // that default (@0x76F270 `6A 01 push 1`), and arg0 stays true (@0x76F27A
    // `6A 01 push 1`).
    Require(blast, "RangeByte(PlayObject, magicId, 1)",
        "AoE range slot defaults to the native 1 (@0x76F270)");
    Require(blast, "nTargetX, nTargetY, range, true, 0,",
        "AoE passes that range through and arg0 is true (@0x76F27A)");
    Require(blast, "600);",
        "AoE delay is 600 ms (@0x76F26B push 0x258)");
    foreach (string skill in new[]
             { "SpellsDef.SKILL_FIREBOOM", "SpellsDef.SKILL_SNOWWIND" })
    {
        string body = SwitchCase(manager, skill);
        Require(body, "QueueNativeAreaBlast(PlayObject, UserMagic,",
            $"{skill} must use the native 600 ms queue");
        Reject(body, "MagBigExplosion",
            $"{skill} must not resolve damage at cast time — native applies " +
            "nothing until the 10177 receiver fires 600 ms later");
        Reject(body, "RM_MAGSTRUCK",
            $"{skill} must not take the legacy RM_MAGSTRUCK route, which " +
            "bypasses the category-3 branch of ResolveFullMagicDamage");
    }
}

// ---------------------------------------------------------------------------
// helpers
// ---------------------------------------------------------------------------

static TPlayObject NewNativeSwitchHeroPlayer(byte oldKind)
{
    var suffix = Guid.NewGuid().ToString("N");
    return new TPlayObject
    {
        m_btNativeHeroRequestKind = oldKind,
        m_PEnvir = NewSummonEnvironment("SwitchHero-" + suffix,
            "switch-hero-" + suffix)
    };
}

static byte[] NewNativeSwitchHeroExtension(byte kind, byte slot)
{
    var extension = new byte[NativeSwitchDataCodec.ExtensionSize];
    BinaryPrimitives.WriteInt32LittleEndian(extension.AsSpan(4, 4), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(extension.AsSpan(8, 2), 0x10);
    extension[0x14] = kind;
    extension[0x15] = slot;
    return extension;
}

static (SummonProbePlayer Player, TUserItem Item) RunCallMonStoneUse(
    ushort selector, ushort dura, byte sceneType = 0, bool dare = false,
    bool existingSlave = false)
{
    var suffix = Guid.NewGuid().ToString("N");
    var environment = NewSummonEnvironment("CallStone-" + suffix,
        "call-stone-" + suffix);
    environment.Flag.SceneType = sceneType;
    environment.Flag.boDARE = dare;
    var player = NewSummonMaster("call-stone-player-" + suffix,
        environment, 4, 4, Grobal2.DR_RIGHT);
    if (existingSlave)
    {
        player.m_SlaveList.Add(new TBaseObject
        {
            m_sCharName = "existing-call-stone-slave",
            m_Master = player
        });
    }

    M2Share.UserEngine.StdItemList.Clear();
    M2Share.UserEngine.StdItemList.Add(new GoodItem
    {
        Name = "召唤石",
        StdMode = 2,
        Shape = 25,
        AniCount = selector,
        DuraMax = ushort.MaxValue
    });
    var item = new TUserItem
    {
        MakeIndex = 1,
        wIndex = 1,
        Dura = dura,
        DuraMax = ushort.MaxValue
    };
    player.m_ItemList.Add(item);
    int itemId = player.EnsureClientItemId(item);
    var use = typeof(TPlayObject).GetMethod("ClientUseItems",
        BindingFlags.Instance | BindingFlags.NonPublic, null,
        new[] { typeof(int), typeof(int) }, null)
        ?? throw new MissingMethodException(typeof(TPlayObject).FullName,
            "ClientUseItems");
    use.Invoke(player, new object[] { itemId, 0 });
    return (player, item);
}

static Envirnoment NewSummonEnvironment(string mapName, string mapFileName,
    short width = 16, short height = 16)
{
    var environment = new Envirnoment
    {
        sMapName = mapName,
        m_sMapFileName = mapFileName
    };
    typeof(Envirnoment).GetMethod("Initialize",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(environment, new object[] { width, height });
    return environment;
}

static TMonInfo NewSummonMonsterInfo(string name,
    byte race = (byte)M2Share.MONSTER_OMA,
    ushort walkSpeed = 1000, ushort attackSpeed = 1000) => new()
{
    ItemList = new List<TMonItem>(),
    sName = name,
    btRace = race,
    wLevel = 1,
    wHP = 100,
    wWalkSpeed = walkSpeed,
    wWalkStep = 1,
    wWalkWait = 1000,
    wAttackSpeed = attackSpeed
};

static SummonProbePlayer NewSummonMaster(string name,
    Envirnoment environment, short x, short y, byte direction) => new()
{
    m_sCharName = name,
    m_PEnvir = environment,
    m_sMapName = environment.sMapName,
    m_sMapFileName = environment.m_sMapFileName,
    m_nCurrX = x,
    m_nCurrY = y,
    m_btDirection = direction
};

static HeroObject AttachSummonHero(TPlayObject master,
    Envirnoment environment, short x, short y, byte direction)
{
    var hero = new HeroObject
    {
        m_sCharName = "summon-hero-" + master.m_sCharName,
        m_Master = master,
        m_PEnvir = environment,
        m_sMapName = environment?.sMapName ?? string.Empty,
        m_sMapFileName = environment?.m_sMapFileName ?? string.Empty,
        m_nCurrX = x,
        m_nCurrY = y,
        m_btDirection = direction
    };
    master.m_HeroObject = hero;
    return hero;
}

static void CheckSummonResult(TBaseObject slave, SummonProbePlayer master,
    HeroObject hero, Envirnoment expectedEnvironment, short expectedX,
    short expectedY, string monsterName, string label)
{
    Assert(slave != null, label + " returned null");
    Assert(ReferenceEquals(expectedEnvironment, slave.m_PEnvir),
        label + " physical environment");
    Equal(expectedX, slave.m_nCurrX, label + " x");
    Equal(expectedY, slave.m_nCurrY, label + " y");
    Assert(ReferenceEquals(master, slave.m_Master), label + " master owner");
    Assert(master.m_SlaveList.Count == 1 &&
           ReferenceEquals(slave, master.m_SlaveList[0]),
        label + " master slave-list publication");
    Equal(0, hero?.m_SlaveList.Count ?? 0,
        label + " hero slave-list must remain unchanged");
    Assert(CellContainsSummon(expectedEnvironment, slave),
        label + " map-cell publication");
    Equal(1, expectedEnvironment.MonCount,
        label + " environment monster count");
    Assert(ReferenceEquals(slave, M2Share.ObjectManager.Get(slave.ObjectId)),
        label + " ObjectManager publication");

    var joins = master.SentStrings
        .Where(entry => entry.Packet.Ident == Grobal2.SM_SLAVE_JOIN)
        .ToArray();
    Equal(1, joins.Length, label + " SM_SLAVE_JOIN count");
    Equal(0, joins[0].Packet.Recog, label + " SM_SLAVE_JOIN Recog");
    Equal((ushort)0, joins[0].Packet.Param,
        label + " SM_SLAVE_JOIN Param");
    Equal((ushort)0, joins[0].Packet.Tag,
        label + " SM_SLAVE_JOIN Tag");
    Equal((ushort)0, joins[0].Packet.Series,
        label + " SM_SLAVE_JOIN Series");
    Equal(monsterName, joins[0].Body, label + " SM_SLAVE_JOIN body");
}

static bool CellContainsSummon(Envirnoment environment, TBaseObject actor)
{
    var found = false;
    var cell = environment.GetMapCellInfo(actor.m_nCurrX, actor.m_nCurrY,
        ref found);
    return found && cell.ObjList != null && cell.ObjList.Any(item =>
        item.CellType == CellType.OS_MOVINGOBJECT &&
        ReferenceEquals(item.CellObj, actor));
}

static void RegisterSummonMap(Envirnoment environment)
{
    var field = typeof(MapManager).GetField("m_MapList",
        BindingFlags.Instance | BindingFlags.NonPublic)!;
    var maps = (IDictionary<string, Envirnoment>)field.GetValue(
        M2Share.MapManager)!;
    maps.Add(environment.sMapName, environment);
}

static void BlockAllSummonCells(Envirnoment environment)
{
    for (var x = 0; x < environment.wWidth; x++)
    for (var y = 0; y < environment.wHeight; y++)
        environment.SetMapXYFlag(x, y, false);
}

static bool CheckAmulet(int dura, byte shape, int count, int type)
{
    var player = NewPlayer();
    player.m_UseItems[Grobal2.U_BUJUK] = NewCharm(dura);
    RegisterStdItem(1, shape);
    short index = -1;
    return Magic.CheckAmulet(player, count, type, ref index);
}

static TUserItem NewCharm(int dura) => new TUserItem
{
    wIndex = 1,
    Dura = (ushort)dura,
    DuraMax = 20000,
    MakeIndex = 1,
};

static void RegisterStdItem(int index, byte shape)
{
    var items = M2Share.UserEngine.StdItemList;
    items.Clear();
    // GetStdItem(int) subtracts one unless the list carries the native
    // sentinel, so seed slot 0 as index 1.
    items.Add(new GoodItem
    {
        Name = "毒药",
        StdMode = 25,
        Shape = shape,
    });
}

static TPlayObject NewPlayer()
{
    var player = new TPlayObject();
    player.m_UseItems = new TUserItem[Grobal2.HUMAN_EQUIPPED_ITEM_COUNT];
    return player;
}

static void SetMapSize(Envirnoment envir, int width, int height)
{
    // Native sub_77BE88 indexes as (x * map[+0x40] + y) * 12 + 4 with the
    // bounds x<map[+0x3C], y<map[+0x40]; C# TryGetMapCellIndex is
    // `nX * wHeight + nY` guarded by wWidth/wHeight — the same shape.
    envir.wWidth = (short)width;
    envir.wHeight = (short)height;
    Set(envir, "MapCellSkillFlags", new byte[width * height]);
}

static void SetMapCellSkillFlag(Envirnoment envir, int x, int y, byte value)
{
    var flags = (byte[])Get(envir, "MapCellSkillFlags");
    var method = typeof(Envirnoment).GetMethod("TryGetMapCellIndex",
        BindingFlags.Instance | BindingFlags.Public |
        BindingFlags.NonPublic)
        ?? throw new MissingMethodException("TryGetMapCellIndex");
    object[] arguments = { x, y, 0 };
    if (!(bool)method.Invoke(envir, arguments))
        throw new InvalidOperationException($"cell ({x},{y}) out of range");
    flags[(int)arguments[2]] = value;
}

static void Set(object target, string name, object value)
{
    var type = target.GetType();
    const BindingFlags all = BindingFlags.Instance | BindingFlags.Public |
        BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
    for (var t = type; t != null; t = t.BaseType)
    {
        var field = t.GetField(name, all);
        if (field != null) { field.SetValue(target, value); return; }
        var property = t.GetProperty(name, all);
        if (property != null && property.CanWrite)
        {
            property.SetValue(target, value);
            return;
        }
        var backing = t.GetField($"<{name}>k__BackingField", all);
        if (backing != null) { backing.SetValue(target, value); return; }
    }
    throw new MissingMemberException(type.FullName, name);
}

static object Get(object target, string name)
{
    const BindingFlags all = BindingFlags.Instance | BindingFlags.Public |
        BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
    for (var t = target.GetType(); t != null; t = t.BaseType)
    {
        var field = t.GetField(name, all);
        if (field != null) return field.GetValue(target);
        var property = t.GetProperty(name, all);
        if (property != null) return property.GetValue(target);
    }
    throw new MissingMemberException(target.GetType().FullName, name);
}

static TUserMagic MakeMagic(ushort id, byte level, byte trainLevel = 3)
{
    return new TUserMagic
    {
        wMagIdx = id,
        btLevel = level,
        MagicInfo = new TMagic
        {
            wMagicID = id,
            btTrainLv = trainLevel,
            TrainLevel = new byte[] { 0, 0, 0, 0 },
            MaxTrain = new[] { 1000, 1000, 1000, 1000 },
        }
    };
}

static void SetMagicLevelBonus(TUserMagic magic, byte bonus)
{
    var field = typeof(TUserMagic).GetField("NativeLevelBonus",
        BindingFlags.Instance | BindingFlags.Public |
        BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(TUserMagic).FullName,
            "NativeLevelBonus");
    field.SetValue(magic, bonus);
}

// Extract a `case <label>:` arm so a Require/Reject is scoped to one spell
// instead of the whole 900-line switch. The arm ends at the first `break;`
// that is at the arm's own brace depth — a naive "first break;" would stop
// inside a nested switch (the 施毒术 arm contains one) and silently check only
// a prefix, which is how a Require can fail against code that is actually
// present.
static string SwitchCase(string source, string label)
{
    int start = source.IndexOf($"case {label}:", StringComparison.Ordinal);
    if (start < 0)
        throw new InvalidOperationException($"case {label} not found");
    int depth = 0;
    for (int i = start; i < source.Length; i++)
    {
        char c = source[i];
        if (c == '{') depth++;
        else if (c == '}')
        {
            depth--;
            if (depth < 0) return source[start..i];
        }
        else if (depth == 0 && c == 'b' &&
                 string.CompareOrdinal(source, i, "break;", 0, 6) == 0)
        {
            return source[start..(i + 6)];
        }
    }
    throw new InvalidOperationException($"case {label} has no break");
}

// Extract a method body by brace matching from its DECLARATION. Anchoring on
// the bare name would match a call site first (QueueNativeAreaBlast is invoked
// twice above its declaration) and then brace-match the enclosing method,
// producing a body that silently contains the wrong code.
static string MethodBody(string source, string name)
{
    int signature = -1;
    for (int probe = 0; ; )
    {
        int hit = source.IndexOf($" {name}(", probe, StringComparison.Ordinal);
        if (hit < 0) break;
        int lineStart = source.LastIndexOf('\n', hit) + 1;
        string prefix = source[lineStart..hit];
        if (prefix.Contains("void", StringComparison.Ordinal) ||
            prefix.Contains("bool", StringComparison.Ordinal) ||
            prefix.Contains("int", StringComparison.Ordinal) ||
            prefix.Contains("static", StringComparison.Ordinal))
        {
            signature = hit;
            break;
        }
        probe = hit + 1;
    }
    if (signature < 0)
        throw new InvalidOperationException(
            $"method {name} declaration not found");
    int open = source.IndexOf('{', signature);
    if (open < 0)
        throw new InvalidOperationException($"method {name} has no body");
    int depth = 0;
    for (int i = open; i < source.Length; i++)
    {
        if (source[i] == '{') depth++;
        else if (source[i] == '}')
        {
            depth--;
            if (depth == 0) return source[open..(i + 1)];
        }
    }
    throw new InvalidOperationException($"method {name} body unterminated");
}

static string ReadSource(params string[] parts)
{
    string path = Path.Combine(RepositoryRoot(), Path.Combine(parts));
    if (!File.Exists(path))
        throw new FileNotFoundException(path);
    return File.ReadAllText(path);
}

static string RepositoryRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "GameSvr",
                "GameSvr.csproj")))
            return dir.FullName;
        dir = dir.Parent;
    }
    throw new DirectoryNotFoundException("GameSvr/GameSvr.csproj");
}

static void PrepareRuntimeConfig()
{
    string runtime = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(runtime, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtime, "String.ini"),
        "[String]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtime, "Command.conf"),
        "[Command]" + Environment.NewLine);
    string share = Path.Combine(Path.GetFullPath(
        Path.Combine(runtime, "..")), "Share");
    Directory.CreateDirectory(share);
    File.WriteAllText(Path.Combine(share, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(share, "ServerData.ini"),
        "[Integer]" + Environment.NewLine);
}

static void InitializeRuntime()
{
    M2Share.g_Config = new GameSvrConfig();
    M2Share.UserEngine = new UserEngine();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.MapManager = new MapManager();
    M2Share.RandomNumber = RandomNumber.GetInstance();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new System.Collections.ArrayList();
    M2Share.g_MonSayMsgList = new Dictionary<string, IList<TMonSayMsg>>();
    M2Share.g_DenySayMsgList =
        new System.Collections.Concurrent.ConcurrentDictionary<string, long>();
}

static void Require(string source, string value, string label) =>
    Assert(source.Contains(value, StringComparison.Ordinal), label);

static void Reject(string source, string value, string label) =>
    Assert(!source.Contains(value, StringComparison.Ordinal), label);

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            $"{label}: expected={expected}, actual={actual}");
    }
}

static void Assert(bool condition, string label)
{
    if (!condition)
        throw new InvalidOperationException(label);
}

sealed class SummonProbePlayer : TPlayObject
{
    internal readonly List<(ClientPacket Packet, string Body)> SentStrings = new();

    internal override void SendSocket(ClientPacket defMsg, string message)
    {
        SentStrings.Add((defMsg, message));
    }
}
