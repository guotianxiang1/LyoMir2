using System.Buffers.Binary;
using DBSvr.Core;
using GameSvr;
using GameSvr.CommandSystem;
using GameSvr.Configs;
using SystemModule;

PrepareRuntimeConfig();
M2Share.ProcessMsgCriticalSection = new object();
M2Share.UserEngine = new UserEngine();
M2Share.ObjectManager = new ObjectManager();
M2Share.UserEngine.StdItemList.Add(new GoodItem
{
    Name = "\u91d1\u5e01",
    NativeWireIndex = 0
});

Equal(923, Grobal2.SM_HERO_UNIONSTATUS, "SM_HERO_UNIONSTATUS");
Equal(10611, Grobal2.RM_HERO_UNIONSTATUS, "RM_HERO_UNIONSTATUS");
Equal(0xAD, NativeHeroDbFrameCodec.NativeUnionStateOffset,
    "union state fixed offset");
Equal(0xAE, NativeHeroDbFrameCodec.NativeUnionEnergyOffset,
    "union energy fixed offset");
Equal(0x754, NativeHeroDbFrameCodec.NativeUnionChargeTierOffset,
    "union charge tier fixed offset");

var raw = new byte[NativeHeroDbFrameCodec.HeroRecordSize];
raw[NativeHeroDbFrameCodec.HeroTypeOffset] = 1;
raw[NativeHeroDbFrameCodec.NativeUnionStateOffset] = 2;
BinaryPrimitives.WriteUInt16LittleEndian(
    raw.AsSpan(NativeHeroDbFrameCodec.NativeUnionEnergyOffset, sizeof(ushort)),
    0xCAFE);
BinaryPrimitives.WriteUInt16LittleEndian(
    raw.AsSpan(NativeHeroDbFrameCodec.NativeUnionChargeTierOffset,
        sizeof(ushort)), 0x0105);

Assert(NativeHeroDbFrameCodec.TryCreateRecord(raw, out var record, out var error),
    "native hero record create: " + error);
var hero = new HeroObject();
Assert(NativeHeroRuntimeCodec.TryApply(hero, record,
    new NativeHeroDynamicData(Array.Empty<NativeHeroDynamicSection>()),
    out error), "native hero apply: " + error);
Equal((byte)2, hero.m_btNativeUnionState, "union state runtime load");
Equal((ushort)0xCAFE, hero.m_wNativeUnionEnergy, "union energy runtime load");
Equal((ushort)0x0105, hero.m_wNativeUnionChargeTier,
    "union charge tier runtime load");

hero.m_btNativeUnionState = 1;
hero.m_wNativeUnionEnergy = 199;
hero.m_wNativeUnionChargeTier = 5;
Assert(NativeHeroRuntimeCodec.TryCreateSnapshot(hero, out var snapshot,
    out _, out error), "native hero snapshot: " + error);
var saved = snapshot.ToArray();
Equal((byte)1, saved[NativeHeroDbFrameCodec.NativeUnionStateOffset],
    "union state runtime save");
Equal((ushort)199, BinaryPrimitives.ReadUInt16LittleEndian(saved.AsSpan(
    NativeHeroDbFrameCodec.NativeUnionEnergyOffset, sizeof(ushort))),
    "union energy runtime save");
Equal((ushort)5, BinaryPrimitives.ReadUInt16LittleEndian(saved.AsSpan(
    NativeHeroDbFrameCodec.NativeUnionChargeTierOffset, sizeof(ushort))),
    "union charge tier runtime save");

var packet = HeroObject.BuildNativeUnionStatusPacket(0xCAFE, 2, 200);
Equal((ushort)923, packet.Ident, "SM923 ident");
Equal(0xCAFE, packet.Recog, "SM923 energy/recog");
Equal((ushort)2, packet.Param, "SM923 state/param");
Equal((ushort)200, packet.Tag, "SM923 maximum/tag");
Equal((ushort)0, packet.Series, "SM923 series");

foreach (var unionMagicId in new ushort[] { 50, 55, 300, 302 })
    Assert(HeroObject.IsNativeUnionMagicId(unionMagicId),
        $"native union magic {unionMagicId}");
foreach (var otherMagicId in new ushort[] { 49, 56, 69, 299, 303 })
    Assert(!HeroObject.IsNativeUnionMagicId(otherMagicId),
        $"non-union magic {otherMagicId}");

M2Share.g_Config.nHeroUnionMaxEnergy = 200;
var runtimePacket = HeroObject.BuildHeroRuntimePacket(new TProcessMessage
{
    wIdent = Grobal2.RM_HERO_UNIONSTATUS,
    wParam = 0xCAFE,
    nParam1 = 2,
    nParam2 = 12345
}, 0, 0);
Equal((ushort)923, runtimePacket.Ident, "RM10611 runtime ident");
Equal(0xCAFE, runtimePacket.Recog, "RM10611 runtime energy");
Equal((ushort)2, runtimePacket.Param, "RM10611 runtime state");
Equal((ushort)200, runtimePacket.Tag,
    "RM10611 runtime maximum comes from global config");

Equal(0, HeroObject.CalculateNativeUnionChargeAmount(42, 5, null,
    _ => throw new InvalidOperationException("random called below level 43")),
    "level 42 charge");
Equal(3, Charge(43, 0, range => range - 1), "level 43 maximum charge");
Equal(4, Charge(44, 0, range => range - 1), "level 44 maximum charge");
Equal(5, Charge(45, 0, range => range - 1), "level 45 maximum charge");
Equal(5, Charge(46, 0, range => range - 1), "level 46 maximum charge");
Equal(7, Charge(47, 0, range => range - 1), "level 47 maximum charge");
Equal(5, Charge(43, 0x0105, range => range - 1),
    "tier low-byte bonus");
Equal(3, Charge(43, 6, range => range - 1),
    "unknown tier has no invented bonus");
Equal(6, HeroObject.CalculateNativeUnionChargeAmount(43, 5,
    new Dictionary<int, int> { [0] = 6 }, _ => 0),
    "configured charge replaces random and tier result");

M2Share.g_Config.HeroUnionChargeOverrides.Clear();
M2Share.g_Config.HeroUnionChargeOverrides[0] = 6;
M2Share.UserEngine.StdItemList.Add(new GoodItem
{
    Name = "union-item",
    StdMode = 25,
    Shape = 7
});
var chargeHero = new HeroObject
{
    m_btNativeUnionState = 0,
    m_wNativeUnionEnergy = 198
};
chargeHero.m_Abil.Level = 43;
chargeHero.m_WAbil.HP = 1;
chargeHero.m_HeroMagicList.Add(new TUserMagic
{
    wMagIdx = 50,
    MagicInfo = new TMagic { wMagicID = 50 }
});
chargeHero.RestoreNativeUnionMagicCacheForLogon();
chargeHero.m_UseItems[Grobal2.U_BUJUK] = new TUserItem
{
    wIndex = 1,
    Dura = 4,
    DuraMax = 10
};
chargeHero.ProcessNativeUnionState(900);
Equal((ushort)200, chargeHero.m_wNativeUnionEnergy,
    "charge clamps to maximum");
Equal((byte)1, chargeHero.m_btNativeUnionState,
    "full charge enters ready state");
Equal((ushort)0, chargeHero.m_UseItems[Grobal2.U_BUJUK].Dura,
    "charge consumes available union-item durability");
var chargeMessages = chargeHero.m_MsgList.Where(x =>
    x.wIdent == Grobal2.RM_DURACHANGE ||
    x.wIdent == Grobal2.RM_HERO_UNIONSTATUS).ToArray();
Equal(2, chargeMessages.Length, "charge message count");
Equal(Grobal2.RM_DURACHANGE, chargeMessages[0].wIdent,
    "durability message precedes status");
Equal(Grobal2.U_BUJUK, chargeMessages[0].wParam,
    "durability slot9");
Equal(0, chargeMessages[0].nParam1, "durability current value");
Equal(10, chargeMessages[0].nParam2, "durability maximum value");
Equal(Grobal2.RM_HERO_UNIONSTATUS, chargeMessages[1].wIdent,
    "charge status message");
Equal(200, chargeMessages[1].wParam, "charged status energy");
Equal(1, chargeMessages[1].nParam1, "charged status ready state");
Equal(0, chargeMessages[1].nParam2, "charged status reserved parameter");

var drainHero = new HeroObject
{
    m_btNativeUnionState = 2,
    m_wNativeUnionEnergy = 5
};
drainHero.m_WAbil.HP = 1;
drainHero.ProcessNativeUnionState(501);
Equal((ushort)0, drainHero.m_wNativeUnionEnergy, "pending release drain");
Equal((byte)0, drainHero.m_btNativeUnionState, "pending release returns idle");
var status = drainHero.m_MsgList.Single(x =>
    x.wIdent == Grobal2.RM_HERO_UNIONSTATUS);
Equal(5, status.wParam, "status queued before drain");
Equal(2, status.nParam1, "status queued with pre-drain state");

var underflowHero = new HeroObject
{
    m_btNativeUnionState = 2,
    m_wNativeUnionEnergy = 4
};
underflowHero.m_WAbil.HP = 1;
underflowHero.ProcessNativeUnionState(501);
Equal(unchecked((ushort)(4 - 5)), underflowHero.m_wNativeUnionEnergy,
    "native ushort underflow preserved");
Equal((byte)2, underflowHero.m_btNativeUnionState,
    "underflow does not return idle");

var readyHero = new HeroObject { m_btNativeUnionState = 1 };
readyHero.ClientHeroPowerUp(new TProcessMessage());
Equal((byte)2, readyHero.m_btNativeUnionState, "CM1108 ready-to-pending");
readyHero.ClientHeroPowerUp(new TProcessMessage());
Equal((byte)2, readyHero.m_btNativeUnionState, "CM1108 state2 unchanged");

CheckCm1108Ordering();
CheckHeroLogonUnionState();
CheckNativeUnionMagicLevelBonus();
CheckHeroSkillBookLearning();
CheckScriptUnionLearnCache();
CheckNativeUnionRelease();
CheckNativeUnionDamageFormula();
CheckNativeUnionClientPackets();
CheckNativeUnionEffectPackets();
CheckNativeUnionAreaTargeting();

Assert(Maps.TryParseLimitSkill("LimitSkill", out var nakedLimit) &&
       nakedLimit.SequenceEqual(new[] { 0 }), "naked LimitSkill key0");
Assert(Maps.TryParseLimitSkill("limitskill(0|69|123)", out var listedLimit) &&
       listedLimit.SequenceEqual(new[] { 0, 69, 123 }),
    "LimitSkill integer list");
Assert(!Maps.TryParseLimitSkill("LimitSkill(0|bad)", out _),
    "invalid LimitSkill rejected");

CheckMapSkillFlags();
CheckConfigPathSemantics();

Console.WriteLine(
    "HeroUnionStateCheck PASS SM923 state/charge/action/effect/area/persistence/config/map-gate");

static int Charge(int level, ushort tier, Func<int, int> random)
{
    return HeroObject.CalculateNativeUnionChargeAmount(level, tier, null, random);
}

static void CheckNativeUnionRelease()
{
    Equal(1, HeroObject.CalculateNativeUnionCollateralDamage(49, 4, 100),
        "native union 6913E8 collateral truncation");
    Equal(19, HeroObject.CalculateNativeUnionCollateralDamage(49, 4, 10),
        "native union standard collateral truncation");
    Equal((byte)Grobal2.DR_DOWNRIGHT,
        HeroObject.GetNativeUnionActionDirection(0, 0, 1, 8),
        "native union action uses raw diagonal direction");
    Equal((byte)Grobal2.DR_DOWN,
        HeroObject.GetNativeUnionActionDirection(0, 0, 0, 0),
        "native union action equal-coordinate direction");

    var success = CreateNativeUnionReleaseFixture(0, 0);
    success.Owner.m_nCurrX = -1;
    var ownerMp = success.Owner.m_WAbil.MP;
    var heroMp = success.Hero.m_WAbil.MP;
    Assert(success.Hero.TryReleaseNativeUnionMagic(),
        "native union warrior-warrior release");
    Equal((byte)0, success.Hero.m_btNativeUnionState,
        "native union success clears state");
    Equal((ushort)0, success.Hero.m_wNativeUnionEnergy,
        "native union success clears energy");
    Equal(ownerMp, success.Owner.m_WAbil.MP,
        "native union does not deduct owner MP");
    Equal(heroMp, success.Hero.m_WAbil.MP,
        "native union does not deduct hero MP");
    Assert(success.Hero.m_MsgList.Any(message =>
        message.wIdent == Grobal2.RM_SPELL),
        "native union release sends hero spell effect");
    AssertNativeUnionAction(success.Hero, success.Hero.m_TargetCret,
        Grobal2.RM_WWJATTACK, "native union warrior-warrior hero action");
    AssertNativeUnionAction(success.Owner, success.Hero.m_TargetCret,
        Grobal2.RM_WWJATTACK, "native union warrior-warrior master action");

    var assassinOwner = CreateNativeUnionReleaseFixture(0, 3);
    Assert(assassinOwner.Hero.TryReleaseNativeUnionMagic(),
        "native union warrior-assassin release");
    Assert(assassinOwner.Hero.m_TargetCret.m_MsgList.Any(message =>
        message.wIdent == Grobal2.RM_10101 &&
        message.nParam3 == assassinOwner.Owner.ObjectId),
        "native union master physical hit preserves master source");

    for (byte heroJob = 0; heroJob < 3; heroJob++)
    {
        for (byte ownerJob = 0; ownerJob < 4; ownerJob++)
        {
            var fixture = CreateNativeUnionReleaseFixture(heroJob, ownerJob);
            Assert(fixture.Hero.TryReleaseNativeUnionMagic(),
                $"native union matrix hero{heroJob}/owner{ownerJob}");
            Equal((byte)0, fixture.Hero.m_btNativeUnionState,
                $"native union matrix state hero{heroJob}/owner{ownerJob}");
            Equal((ushort)0, fixture.Hero.m_wNativeUnionEnergy,
                $"native union matrix energy hero{heroJob}/owner{ownerJob}");
            var expectedAction = ExpectedNativeUnionAction(heroJob, ownerJob);
            AssertNativeUnionAction(fixture.Hero, fixture.Hero.m_TargetCret,
                expectedAction,
                $"native union matrix hero{heroJob}/owner{ownerJob} hero action");
            AssertNativeUnionAction(fixture.Owner, fixture.Hero.m_TargetCret,
                expectedAction == Grobal2.RM_WWJATTACK
                    ? Grobal2.RM_WWJATTACK : 0,
                $"native union matrix hero{heroJob}/owner{ownerJob} master action");
        }
    }

    var mpBlocked = CreateNativeUnionReleaseFixture(1, 1);
    mpBlocked.Hero.m_WAbil.MP = 0;
    Assert(!mpBlocked.Hero.TryReleaseNativeUnionMagic(),
        "native union hero MP gate");
    Equal((byte)2, mpBlocked.Hero.m_btNativeUnionState,
        "native union hero MP gate preserves state");
    Equal((ushort)77, mpBlocked.Hero.m_wNativeUnionEnergy,
        "native union hero MP gate preserves energy");

    var ownerDead = CreateNativeUnionReleaseFixture(1, 1);
    ownerDead.Owner.m_boDeath = true;
    Assert(!ownerDead.Hero.TryReleaseNativeUnionMagic(),
        "native union owner death gate");
    Equal((byte)2, ownerDead.Hero.m_btNativeUnionState,
        "native union owner death preserves state");

    var noTarget = CreateNativeUnionReleaseFixture(1, 1);
    noTarget.Hero.m_TargetCret = null;
    Assert(!noTarget.Hero.TryReleaseNativeUnionMagic(),
        "native union target gate");
    Equal((byte)2, noTarget.Hero.m_btNativeUnionState,
        "native union target gate preserves state");

    var pulseThenRelease = CreateNativeUnionReleaseFixture(1, 1);
    pulseThenRelease.Hero.m_wNativeUnionEnergy = 10;
    pulseThenRelease.Hero.ProcessNativeUnionState(501);
    Equal((ushort)5, pulseThenRelease.Hero.m_wNativeUnionEnergy,
        "native union state2 pulse precedes release");
    Assert(pulseThenRelease.Hero.TryReleaseNativeUnionMagic(),
        "native union release after state2 pulse");
    Equal((byte)0, pulseThenRelease.Hero.m_btNativeUnionState,
        "native union release after pulse clears state");
    Equal((ushort)0, pulseThenRelease.Hero.m_wNativeUnionEnergy,
        "native union release after pulse clears energy");
    var pulseStatus = pulseThenRelease.Hero.m_MsgList.Single(message =>
        message.wIdent == Grobal2.RM_HERO_UNIONSTATUS);
    Equal(10, pulseStatus.wParam,
        "native union pulse queues pre-drain energy");
}

static void CheckNativeUnionDamageFormula()
{
    Equal(198, HeroObject.CalculateNativeUnionDamage(100, 10, 10, 0,
        _ => 0),
        "native union level0 multiplier");
    Equal(6429, HeroObject.CalculateNativeUnionDamage(100, 10, 10, 5,
        _ => 0),
        "native union level5 bonus");
    Equal(21429, HeroObject.CalculateNativeUnionDamage(100, 10, 10, 10,
        _ => 0),
        "native union level10 bonus");
    var attributeRange = 0;
    Equal(198, HeroObject.CalculateNativeUnionDamage(100, 10, 12, 0,
        range =>
        {
            attributeRange = range;
            return 0;
        }), "native union attribute minimum damage");
    Equal(3, attributeRange,
        "native union attribute random includes maximum");
    Equal(202, HeroObject.CalculateNativeUnionDamage(100, 10, 12, 0,
        range => range - 1),
        "native union attribute maximum damage");

    var magic = new TUserMagic
    {
        btLevel = 0,
        MagicInfo = new TMagic
        {
            wPower = 100,
            wMaxPower = 101,
            btTrainLv = 0,
            btDefPower = 0,
            btDefMaxPower = 1
        }
    };
    var master = new TPlayObject { m_btJob = 0 };
    master.m_WAbil.MC = HUtil32.MakeLong(30, 30);
    master.m_WAbil.SC = HUtil32.MakeLong(40, 40);
    master.m_WAbil.DC = HUtil32.MakeLong(50, 50);
    var hero = new HeroObject { m_btJob = 1 };
    hero.m_WAbil.MC = HUtil32.MakeLong(10, 10);
    hero.m_WAbil.SC = HUtil32.MakeLong(20, 20);
    hero.m_WAbil.DC = HUtil32.MakeLong(10, 10);

    // ---------------------------------------------------------------------
    // Union base power = native sub_4C8648 -> sub_4C8658, the SAME function
    // Spells/Magic.cs GetPower delegates to. Established 2026-08-04 by anchoring on
    // this very file's data tables: the multiplier array is native f64 data at
    // 0x7D3278 and the bonus array int32 data at 0x7D32D0, whose ONLY code readers
    // are @0x68EF20 / @0x68EF32 inside sub_68EEDC (= CalculateNativeUnionDamage).
    // Each of sub_68EEDC's 10 callers builds the base power right before the call,
    // e.g. fn 0x690BAC:
    //     690C1E  mov  eax,[ebx+0x6D4]        ; the union UserMagic
    //     690C3C  push eax                    ; (abilMax - abilMin + 1) spread
    //     690C40  call 0x4C8648               ; <<== BASE POWER
    //     690C45  mov ecx,eax / add ecx,edi   ; + abilMin
    //     690C4E  call 0x68EEDC               ; final union damage
    // sub_4C8658 divides by the float32 4.0 at [0x4C86B8] (`D8 /6` = FDIV m32real
    // fixes the width at 4 bytes; decoding it as f64 gives 1.9e+96 because the
    // trailing 55 8B EC is the next function's prologue) and rounds via
    // sub_403574 = `fistp qword` = round-half-to-even. btTrainLv (+0x1A) is NEVER
    // read in the body — the only MagicInfo offsets touched are
    // {+0x15,+0x16,+0x18,+0x19}.
    //
    // These numbers were RE-BASED (2026-08-04) off the pre-fix values, which encoded
    // TWO defects at HeroObject.GetNativeUnionMagicBasePower:
    //   1. divisor `(btTrainLv + 1)` instead of the native 4.0 — and this fixture
    //      sets btTrainLv = 0, so the old divisor was 1, i.e. NO division at all;
    //   2. the EFFECTIVE level was fed into the power formula, whereas native passes
    //      the RAW btLevel there (sub_4C8648 @0x4C864B `mov dl,[eax+0x0C]`) and uses
    //      the effective level ONLY as the damage-table index (sub_4C896C @0x68EF05).
    //
    // Worked arithmetic for this fixture (wPower=100, wMaxPower=101, btTrainLv=0,
    // btDefPower=0, btLevel=0, random => 0, so MPow=100 and both rolls are 0):
    //   base power  OLD = Round(100 / (0+1) * (0+1)) + 0 = 100
    //   base power  NEW = Round(100 * (0+1) / 4.0)  + 0 = 25      <- native
    //   hero MC(10,10): inner = 25 + 10 = 35; 35 * 1.8 = 63.0 -> 63   (was 198)
    //   master MC(30,30): inner = 25 + 30 = 55; 55 * 1.8 = 99.0 -> 99
    // The old values were 3.14x the native damage on the hero-MC case.
    // See staging/heromagic_mpcost_fix_20260804.md §C-RESOLVED.
    Equal(63, hero.CalculateNativeUnionMagicDamage(master, magic, _ => 0),
        "native union hero MC contribution");
    hero.m_btJob = 0;
    master.m_btJob = 1;
    Equal(99, hero.CalculateNativeUnionMagicDamage(master, magic, _ => 0),
        "native union master MC contribution");
    hero.m_btJob = 1;
    Equal(162, hero.CalculateNativeUnionMagicDamage(master, magic, _ => 0),
        "native union MC plus MC contribution");
    hero.m_btJob = 2;
    master.m_btJob = 1;
    Equal(180, hero.CalculateNativeUnionMagicDamage(master, magic, _ => 0),
        "native union SC plus MC contribution");
    master.m_btJob = 2;
    Equal(198, hero.CalculateNativeUnionMagicDamage(master, magic, _ => 0),
        "native union SC plus SC contribution");
    // ---------------------------------------------------------------------
    // 物理（DC）路走的是 **另一张** 系数表。宿主有两条同形的合击结算例程，唯一
    // 差别是 `fmul qword [eax*8 + …]` 的表基址：
    //     0068FF6D  fmul qword [eax*8 + 0x7D33FC]   sub_68FF2C  1.5/2.0/2.4/2.6/2.8
    //     0068EF1D  fmul qword [eax*8 + 0x7D3278]   sub_68EEDC  1.8/2.5/3.3/3.6/3.9
    // 加法表 0x7D32D0 两条共用（@0x68FF7F / @0x68EF2F）。把两个函数的全部 rel32
    // 调用点反汇编、看调用前读的能力字段（DC +0x28C、MC +0x294、SC +0x29C、
    // CC +0x2A4），划分完全不交叉：
    //     sub_68FF2C : 0x68EA31 DC  0x690ECA DC  0x690D52/0x690F9C/0x69181A CC
    //     sub_68EEDC : 0x690C4E/0x69116C/0x691D29/0x691D58 SC
    //                  0x6914E0/0x691716/0x691993/0x6919C2/0x69208B/0x6920B9 MC
    // ⇒ DC/CC → 战士表，MC/SC → 法道表。此前两条 C# 路共用法道表，物理路偏高。
    // 重算（basePower 25，random => 0，level 0，战士表 [0] = 1.5）：
    //     hero   DC(10,10): inner = 25 + 10 = 35; 35 * 1.5 = 52.5 -> 52（半偶）
    //     master DC(50,50): inner = 25 + 50 = 75; 75 * 1.5 = 112.5 -> 112
    Equal(52, hero.CalculateNativeUnionPhysicalDamage(hero, magic, _ => 0),
        "native union physical uses the warrior table 0x7D33FC (sub_68FF2C) "
        + "plus hero DC");
    Equal(112, hero.CalculateNativeUnionPhysicalDamage(master, magic, _ => 0),
        "native union physical uses the warrior table 0x7D33FC (sub_68FF2C) "
        + "plus master DC");

    // The base power must divide by the literal 4.0, never by (btTrainLv + 1).
    // btTrainLv is 0 in the fixture above, so sweeping it must NOT move the damage —
    // this is what actually kills any divisor that reads +0x1A (@0x4C8658 never does).
    foreach (var sweptTrainLv in new byte[] { 0, 1, 2, 3, 7, 255 })
    {
        magic.MagicInfo.btTrainLv = sweptTrainLv;
        hero.m_btJob = 1;
        master.m_btJob = 0;
        Equal(63, hero.CalculateNativeUnionMagicDamage(master, magic, _ => 0),
            "union base power must ignore btTrainLv (+0x1A unread in sub_4C8658) "
            + "at btTrainLv=" + sweptTrainLv);
    }
    magic.MagicInfo.btTrainLv = 0;
    master.m_btJob = 2;

    // The base power must round with sub_403574 (@0x4C869C, `fistp qword` under the
    // DEFAULT control word = round-half-to-even) and must multiply by the level BEFORE
    // dividing (native does an integer `imul esi` @0x4C868E, THEN `fild` @0x4C8693, THEN
    // `fdiv` @0x4C8696). Neither truncation (sub_403580, which or's RC=11 first) nor a
    // divide-first ordering is native.
    // Separator fixture: wPower = 6, wMaxPower = 7, random => 0 (so MPow = 6),
    // btLevel = 0, btDefPower = 0, hero job 1 with MC lo = 10, table index 0 (x1.8):
    //   native round-half-even : Round(6 * 1 / 4.0) = Round(1.5) = 2
    //                            -> inner = 2 + 10 = 12 -> 12 * 1.8 = 21.6 -> 22
    //   truncation             : (int)1.5 = 1 -> inner = 11 -> 19.8 -> 20
    //   divide-first (6/4 = 1) : 1          -> inner = 11 -> 19.8 -> 20
    // so 22 distinguishes the native form from BOTH wrong forms.
    var roundingMagic = new TUserMagic
    {
        btLevel = 0,
        MagicInfo = new TMagic
        {
            wPower = 6,
            wMaxPower = 7,
            btTrainLv = 0,
            btDefPower = 0,
            btDefMaxPower = 1
        }
    };
    hero.m_btJob = 1;
    master.m_btJob = 0;
    Equal(22, hero.CalculateNativeUnionMagicDamage(master, roundingMagic, _ => 0),
        "union base power rounds half-to-even and multiplies before dividing "
        + "(sub_403574 @0x4C869C, imul @0x4C868E before fdiv @0x4C8696)");

    // The power formula takes the RAW btLevel, NOT the effective level. Native proves
    // the split inside a single call chain: sub_4C8648 @0x4C864B does
    // `mov dl,byte ptr [eax+0x0C]` (raw btLevel) to build the power, while the damage
    // TABLE index is fetched separately by sub_4C896C @0x68EF05 (btLevel +
    // NativeLevelBonus, clamped to btTrainLv). Give the hero a level bonus with real
    // headroom under the train cap so the two levels DIVERGE:
    //   btLevel = 0, NativeLevelBonus = 3, btTrainLv = 5 -> raw = 0, effective = 3.
    //   power  raw(native) : Round(6 * (0+1) / 4.0) = Round(1.5) = 2
    //   power  effective   : Round(6 * (3+1) / 4.0) = 6           <- the old C# defect
    //   table index = effective = 3 -> multiplier 3.6, bonus 0; hero MC lo = 10
    //   native : (2 + 10) * 3.6 = 43.2 -> 43
    //   defect : (6 + 10) * 3.6 = 57.6 -> 58
    var levelSplitMagic = new TUserMagic
    {
        btLevel = 0,
        MagicInfo = new TMagic
        {
            wPower = 6,
            wMaxPower = 7,
            btTrainLv = 5,
            btDefPower = 0,
            btDefMaxPower = 1
        }
    };
    var previousHeroBonus = hero.NativeMagicLevelBonus;
    hero.NativeMagicLevelBonus = 3;
    Equal(43, hero.CalculateNativeUnionMagicDamage(master, levelSplitMagic, _ => 0),
        "union power uses the RAW btLevel (sub_4C8648 @0x4C864B) while only the "
        + "damage-table index uses the effective level (sub_4C896C @0x68EF05)");
    Equal((byte)3, levelSplitMagic.NativeLevelBonus,
        "union effective-level helper still publishes NativeLevelBonus");
    hero.NativeMagicLevelBonus = previousHeroBonus;
    master.m_btJob = 2;

    var armoredPlayer = new TPlayObject();
    armoredPlayer.m_WAbil.AC = HUtil32.MakeLong(9999, 9999);
    armoredPlayer.m_WAbil.MAC = HUtil32.MakeLong(9999, 9999);
    Equal(0, armoredPlayer.GetHitStruckDamage(hero, 1000),
        "ordinary physical damage is reduced by AC");
    Equal(0, armoredPlayer.GetMagStruckDamage(hero, 1000),
        "ordinary magic damage is reduced by MAC");
    Equal(1000, HeroObject.ApplyNativeUnionTargetDamage(armoredPlayer, hero,
        1000), "native union player target bypasses ordinary AC/MAC path");

    var armoredHero = new HeroObject();
    armoredHero.m_WAbil.AC = HUtil32.MakeLong(9999, 9999);
    armoredHero.m_WAbil.MAC = HUtil32.MakeLong(9999, 9999);
    Equal(1000, HeroObject.ApplyNativeUnionTargetDamage(armoredHero, hero,
        1000), "native union hero target bypasses ordinary AC/MAC path");

    var wildMonster = new AnimalObject();
    Equal(3000, HeroObject.ApplyNativeUnionTargetDamage(wildMonster, hero,
        1000), "native union monster zero exp-hitter tick triple damage");
    wildMonster.m_ExpHitterTick = 1;
    Equal(1000, HeroObject.ApplyNativeUnionTargetDamage(wildMonster, hero,
        1000), "native union monster exp-hitter tick keeps damage");

    var warriorAttacker = new HeroObject { m_btJob = M2Share.jWarr };
    var manaTarget = new TPlayObject { m_btJob = 0 };
    manaTarget.m_WAbil.MP = 50;
    Assert(HeroObject.ApplyNativeUnionTargetManaCost(manaTarget,
        warriorAttacker, 4, 100),
        "native union warrior level4 mana cost applies");
    Equal(40, manaTarget.m_WAbil.MP,
        "native union warrior target mana cost");
    manaTarget.m_btJob = 1;
    manaTarget.m_WAbil.MP = 50;
    Assert(HeroObject.ApplyNativeUnionTargetManaCost(manaTarget,
        warriorAttacker, 4, 100),
        "native union wizard target mana cost applies");
    Equal(10, manaTarget.m_WAbil.MP,
        "native union wizard target mana cost");
    manaTarget.m_btJob = 2;
    manaTarget.m_WAbil.MP = 50;
    Assert(HeroObject.ApplyNativeUnionTargetManaCost(manaTarget,
        warriorAttacker, 4, 100),
        "native union tao target mana cost applies");
    Equal(30, manaTarget.m_WAbil.MP,
        "native union tao target mana cost");
    manaTarget.m_btJob = 3;
    manaTarget.m_WAbil.MP = 5;
    Assert(HeroObject.ApplyNativeUnionTargetManaCost(manaTarget,
        warriorAttacker, 4, 100),
        "native union assassin target mana cost applies");
    Equal(0, manaTarget.m_WAbil.MP,
        "native union mana cost clamps to zero");
    manaTarget.m_btJob = 0;
    manaTarget.m_WAbil.MP = 50;
    Assert(!HeroObject.ApplyNativeUnionTargetManaCost(manaTarget,
        warriorAttacker, 3, 100),
        "native union below level4 has no mana cost");
    Equal(50, manaTarget.m_WAbil.MP,
        "native union below level4 preserves mana");
    warriorAttacker.m_btJob = 1;
    Assert(!HeroObject.ApplyNativeUnionTargetManaCost(manaTarget,
        warriorAttacker, 4, 100),
        "native union non-warrior has no mana cost");
    Equal(50, manaTarget.m_WAbil.MP,
        "native union non-warrior preserves mana");

    var manaHeroTarget = new HeroObject { m_btJob = 3 };
    manaHeroTarget.m_WAbil.MP = 50;
    warriorAttacker.m_btJob = M2Share.jWarr;
    Assert(HeroObject.ApplyNativeUnionTargetManaCost(manaHeroTarget,
        warriorAttacker, 4, 100),
        "native union hero mana cost applies");
    Equal(30, manaHeroTarget.m_WAbil.MP,
        "native union hero mana cost");
    Equal(1000, HeroObject.ApplyNativeUnionParticipantFinalDamage(hero, 1000),
        "native union player and hero participant hook is identity");
}

static void CheckNativeUnionClientPackets()
{
    Equal(60, Grobal2.SM_WWJATTACK, "SM_WWJATTACK value");
    Equal(61, Grobal2.SM_WSJATTACK, "SM_WSJATTACK value");
    Equal(62, Grobal2.SM_WTJATTACK, "SM_WTJATTACK value");

    var recipient = new TPlayObject { m_boOffLineFlag = true };
    var actor = new TBaseObject();
    Assert(actor.ObjectId != recipient.ObjectId,
        "native union client packet sender differs from recipient");

    var cases = new (int RequestIdent, int ResponseIdent, int X, int Y,
        int Direction)[]
    {
        (Grobal2.RM_WWJATTACK, Grobal2.SM_WWJATTACK, 101, 202, 3),
        (Grobal2.RM_WSJATTACK, Grobal2.SM_WSJATTACK, 303, 404, 5),
        (Grobal2.RM_WTJATTACK, Grobal2.SM_WTJATTACK, 505, 606, 7)
    };

    foreach (var action in cases)
    {
        recipient.m_DefMsg = null;
        recipient.Operate(new TProcessMessage
        {
            wIdent = action.RequestIdent,
            BaseObject = actor.ObjectId,
            nParam1 = action.X,
            nParam2 = action.Y,
            wParam = action.Direction
        });

        var packet = recipient.m_DefMsg;
        Assert(packet != null,
            $"native union client packet {action.RequestIdent} emitted");
        Equal((ushort)action.ResponseIdent, packet.Ident,
            $"native union client packet {action.RequestIdent} ident");
        Equal(actor.ObjectId, packet.Recog,
            $"native union client packet {action.RequestIdent} recog");
        Equal((ushort)action.X, packet.Param,
            $"native union client packet {action.RequestIdent} param");
        Equal((ushort)action.Y, packet.Tag,
            $"native union client packet {action.RequestIdent} tag");
        Equal((ushort)action.Direction, packet.Series,
            $"native union client packet {action.RequestIdent} series");
    }
}

static void CheckNativeUnionEffectPackets()
{
    Equal(1230, Grobal2.SM_NATIVE_UNION_EFFECT,
        "SM native union effect value");
    Equal(10612, Grobal2.RM_NATIVE_UNION_EFFECT,
        "RM native union effect value");

    var body = HeroObject.BuildNativeUnionEffectBody(1032, 3, 6,
        0x1234, 0x5678);
    Equal(12, body.Length, "native union effect body length");
    Equal((ushort)1032, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(0, 2)),
        "native union effect body action");
    Equal((ushort)3, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(2, 2)),
        "native union effect body level");
    Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(4, 2)),
        "native union effect body reserved");
    Equal((ushort)6, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(6, 2)),
        "native union effect body direction");
    Equal((ushort)0x1234,
        BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(8, 2)),
        "native union effect body x");
    Equal((ushort)0x5678,
        BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(10, 2)),
        "native union effect body y");

    var recipient = new TPlayObject { m_boOffLineFlag = true };
    var actor = new TBaseObject();
    recipient.Operate(new TProcessMessage
    {
        wIdent = Grobal2.RM_NATIVE_UNION_EFFECT,
        BaseObject = actor.ObjectId,
        wParam = 1032,
        nParam1 = 0x1234,
        nParam2 = 0x5678,
        Payload = body
    });
    var packet = recipient.m_DefMsg;
    Assert(packet != null, "native union effect client packet emitted");
    Equal((ushort)1230, packet.Ident, "native union effect client ident");
    Equal(actor.ObjectId, packet.Recog, "native union effect client recog");
    Equal((ushort)1032, packet.Param, "native union effect client param");
    Equal((ushort)0x1234, packet.Tag, "native union effect client tag");
    Equal((ushort)0x5678, packet.Series, "native union effect client series");

    var warriorAssassin = CreateNativeUnionReleaseFixture(0, 3);
    Assert(warriorAssassin.Hero.TryReleaseNativeUnionMagic(),
        "warrior-assassin native union effect release");
    Assert(!warriorAssassin.Hero.m_MsgList.Any(message =>
        message.wIdent == Grobal2.RM_SPELL),
        "warrior-assassin native union has no generic spell effect");
    AssertNativeUnionEffect(warriorAssassin.Hero, 1032,
        "warrior-assassin warrior effect");
    AssertNativeUnionEffect(warriorAssassin.Owner, 1029,
        "warrior-assassin assassin effect");

    var effectOrderEnvironment = CreateNativeUnionCombatEnvironment();
    var orderedWarriorAssassin = CreateNativeUnionReleaseFixture(0, 3,
        effectOrderEnvironment);
    orderedWarriorAssassin.Hero.m_boFixedHideMode = false;
    orderedWarriorAssassin.Owner.m_boFixedHideMode = false;
    var effectObserver = new TPlayObject
    {
        m_sCharName = "UnionEffectObserver",
        m_PEnvir = effectOrderEnvironment,
        m_nCurrX = 5,
        m_nCurrY = 5
    };
    Assert(ReferenceEquals(effectObserver, effectOrderEnvironment.AddToMap(5, 5,
        CellType.OS_MOVINGOBJECT, effectObserver)),
        "native union effect observer map add");
    Assert(orderedWarriorAssassin.Hero.TryReleaseNativeUnionMagic(),
        "warrior-assassin native union effect order release");
    var effectActions = effectObserver.m_MsgList.Where(message =>
        message.wIdent == Grobal2.RM_NATIVE_UNION_EFFECT)
        .Select(message => message.wParam).ToArray();
    Assert(effectActions.SequenceEqual(new[] { 1032, 1029 }),
        "warrior-assassin native union effect order");

    AssertNativeUnionMagicPhysicalRelease(1, 1030,
        "wizard-assassin native union");
    AssertNativeUnionMagicPhysicalRelease(2, 1031,
        "tao-assassin native union");
}

static void AssertNativeUnionMagicPhysicalRelease(byte heroJob,
    int expectedPhysicalEffect, string label)
{
    var environment = CreateNativeUnionCombatEnvironment();
    var fixture = CreateNativeUnionReleaseFixture(heroJob, 3, environment);
    fixture.Hero.m_boFixedHideMode = false;
    fixture.Owner.m_boFixedHideMode = false;
    var observer = new TPlayObject
    {
        m_sCharName = $"UnionPhysicalObserver{heroJob}",
        m_PEnvir = environment,
        m_nCurrX = 5,
        m_nCurrY = 5
    };
    Assert(ReferenceEquals(observer, environment.AddToMap(5, 5,
        CellType.OS_MOVINGOBJECT, observer)), label + " observer map add");

    Assert(fixture.Hero.TryReleaseNativeUnionMagic(), label + " release");

    var visuals = observer.m_MsgList.Where(message =>
        message.wIdent == Grobal2.RM_SPELL ||
        message.wIdent == Grobal2.RM_NATIVE_UNION_EFFECT).ToArray();
    Equal(2, visuals.Length, label + " visual count");
    Equal(Grobal2.RM_SPELL, visuals[0].wIdent,
        label + " hero spell precedes physical effect");
    Assert(ReferenceEquals(fixture.Hero, visuals[0].BaseObject),
        label + " hero spell source");
    Equal(Grobal2.RM_NATIVE_UNION_EFFECT, visuals[1].wIdent,
        label + " physical effect follows hero spell");
    Equal(expectedPhysicalEffect, visuals[1].wParam,
        label + " physical effect action");
    Assert(ReferenceEquals(fixture.Owner, visuals[1].BaseObject),
        label + " physical effect source");

    var strikes = fixture.Hero.m_TargetCret.m_MsgList.Where(message =>
        message.wIdent == Grobal2.RM_10101).ToArray();
    Equal(2, strikes.Length, label + " hit count");
    Equal(fixture.Hero.ObjectId, strikes[0].nParam3,
        label + " magic hit source");
    Equal(fixture.Owner.ObjectId, strikes[1].nParam3,
        label + " physical hit source");
}

static void AssertNativeUnionEffect(TBaseObject actor, int action,
    string label)
{
    var effect = actor.m_MsgList.Single(message =>
        message.wIdent == Grobal2.RM_NATIVE_UNION_EFFECT);
    Equal(action, effect.wParam, label + " action");
    Equal(actor.m_nCurrX, effect.nParam1, label + " x");
    Equal(actor.m_nCurrY, effect.nParam2, label + " y");
    Assert(ReferenceEquals(actor, effect.BaseObject), label + " source");
    Assert(effect.Payload is byte[], label + " body type");
    var body = (byte[])effect.Payload;
    Equal(12, body.Length, label + " body length");
    Equal((ushort)action,
        BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(0, 2)),
        label + " body action");
    Equal((ushort)0,
        BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(2, 2)),
        label + " body effective level");
    Equal((ushort)0,
        BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(4, 2)),
        label + " body reserved");
    Equal((ushort)actor.m_btDirection,
        BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(6, 2)),
        label + " body direction");
    Equal(unchecked((ushort)actor.m_nCurrX),
        BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(8, 2)),
        label + " body x");
    Equal(unchecked((ushort)actor.m_nCurrY),
        BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(10, 2)),
        label + " body y");
}

static void CheckNativeUnionAreaTargeting()
{
    var warriorMage = CreateNativeUnionAreaFixture(1, 0);
    var warriorMageInside = AddNativeUnionProbe(warriorMage.Environment, 12, 11);
    Assert(warriorMage.Hero.TryReleaseNativeUnionMagic(),
        "warrior-mage rectangle release");
    Assert(warriorMageInside.m_WAbil.HP < 100000,
        "warrior-mage rectangle reaches non-diagonal cell");

    var warriorTao = CreateNativeUnionAreaFixture(2, 0);
    var warriorTaoDiagonal = AddNativeUnionProbe(warriorTao.Environment, 12, 12);
    var warriorTaoRectangleOnly = AddNativeUnionProbe(warriorTao.Environment,
        12, 11);
    Assert(warriorTao.Hero.TryReleaseNativeUnionMagic(),
        "warrior-tao diagonal release");
    Assert(warriorTaoDiagonal.m_WAbil.HP < 100000,
        "warrior-tao diagonal reaches diagonal cell");
    Equal(100000, warriorTaoRectangleOnly.m_WAbil.HP,
        "warrior-tao diagonal excludes rectangle-only cell");

    var mageTao = CreateNativeUnionAreaFixture(1, 2);
    var mageTaoTop = AddNativeUnionProbe(mageTao.Environment, 10, 8);
    var mageTaoBottom = AddNativeUnionProbe(mageTao.Environment, 10, 12);
    Assert(mageTao.Hero.TryReleaseNativeUnionMagic(),
        "mage-tao rectangle release");
    Equal(100000, mageTaoTop.m_WAbil.HP,
        "mage-tao 5x4 excludes y-minus-two");
    Assert(mageTaoBottom.m_WAbil.HP < 100000,
        "mage-tao 5x4 includes y-plus-two");

    var taoTao = CreateNativeUnionAreaFixture(2, 2);
    var taoTaoTop = AddNativeUnionProbe(taoTao.Environment, 10, 8);
    Assert(taoTao.Hero.TryReleaseNativeUnionMagic(),
        "tao-tao rectangle release");
    Assert(taoTaoTop.m_WAbil.HP < 100000,
        "tao-tao 5x5 includes y-minus-two");

    var firstTarget = CreateNativeUnionAreaFixture(1, 2);
    var firstCandidate = AddNativeUnionProbe(firstTarget.Environment, 12, 11);
    var secondCandidate = AddNativeUnionProbe(firstTarget.Environment, 12, 11);
    Assert(firstTarget.Hero.TryReleaseNativeUnionMagic(),
        "native union first-target release");
    Equal(100000, firstCandidate.m_WAbil.HP,
        "native union older target in cell is skipped");
    Assert(secondCandidate.m_WAbil.HP < 100000,
        "native union head target in cell is hit");

    var mageMage = CreateNativeUnionAreaFixture(1, 1);
    var firstMageMageTarget = AddNativeUnionAnimalProbe(mageMage.Environment,
        8, 9);
    var finalMageMageTarget = AddNativeUnionProbe(mageMage.Environment, 12,
        12);
    Assert(mageMage.Hero.TryReleaseNativeUnionMagic(),
        "mage-mage rolling rectangle release");
    var firstMageMageDamage = 100000 - firstMageMageTarget.m_WAbil.HP;
    var primaryMageMageDamage = 100000 -
        mageMage.Hero.m_TargetCret.m_WAbil.HP;
    var finalMageMageDamage = 100000 - finalMageMageTarget.m_WAbil.HP;
    Equal(firstMageMageDamage, primaryMageMageDamage,
        "mage-mage first collateral hook return rolls into primary");
    Equal(HeroObject.CalculateNativeUnionCollateralDamage(
            primaryMageMageDamage, 4, 10), finalMageMageDamage,
        "mage-mage primary hook return rolls into later collateral");

    var diagonalControl = CreateNativeUnionAreaFixture(2, 0);
    Assert(diagonalControl.Hero.TryReleaseNativeUnionMagic(),
        "native union diagonal control release");
    var diagonalControlDamage = 100000 -
        diagonalControl.Hero.m_TargetCret.m_WAbil.HP;

    var diagonalPrimary = CreateNativeUnionAreaFixture(2, 0);
    var repeatedPrimary = diagonalPrimary.Hero.m_TargetCret;
    Assert(ReferenceEquals(repeatedPrimary,
        diagonalPrimary.Environment.AddToMap(7, 7,
            CellType.OS_MOVINGOBJECT, repeatedPrimary)),
        "native union diagonal primary first-arm map add");
    Assert(ReferenceEquals(repeatedPrimary,
        diagonalPrimary.Environment.AddToMap(13, 13,
            CellType.OS_MOVINGOBJECT, repeatedPrimary)),
        "native union diagonal primary second-arm map add");
    Assert(diagonalPrimary.Hero.TryReleaseNativeUnionMagic(),
        "native union diagonal primary exclusion release");
    Equal(diagonalControlDamage, 100000 - repeatedPrimary.m_WAbil.HP,
        "native union diagonal arms exclude primary before final hit");
}

static (TPlayObject Owner, HeroObject Hero, Envirnoment Environment)
    CreateNativeUnionAreaFixture(byte heroJob, byte ownerJob)
{
    var environment = CreateNativeUnionCombatEnvironment();
    var fixture = CreateNativeUnionReleaseFixture(heroJob, ownerJob,
        environment);
    fixture.Hero.m_nCurrX = 9;
    fixture.Hero.m_nCurrY = 10;
    fixture.Owner.m_nCurrX = 8;
    fixture.Owner.m_nCurrY = 10;
    fixture.Hero.m_TargetCret.m_nCurrX = 10;
    fixture.Hero.m_TargetCret.m_nCurrY = 10;
    fixture.Hero.m_TargetCret.m_WAbil.HP = 100000;
    fixture.Hero.m_TargetCret.m_WAbil.MaxHP = 100000;
    fixture.Hero.m_HeroMagicList[0].MagicInfo.wPower = 1000;
    fixture.Hero.m_HeroMagicList[0].MagicInfo.wMaxPower = 1001;
    fixture.Hero.m_HeroMagicList[0].MagicInfo.btDefPower = 0;
    fixture.Hero.m_HeroMagicList[0].MagicInfo.btDefMaxPower = 1;
    Assert(ReferenceEquals(fixture.Hero.m_TargetCret,
        environment.AddToMap(10, 10, CellType.OS_MOVINGOBJECT,
            fixture.Hero.m_TargetCret)), "native union primary target map add");
    return (fixture.Owner, fixture.Hero, environment);
}

static TBaseObject AddNativeUnionProbe(Envirnoment environment, int x, int y)
{
    // 名字同样是 sub_765D64 的一项（0x765D6D cmp byte [esi+0x106],0）：无名探针
    // 会在下一次同格 AddToMap 时被摘链，「同格第二个目标被跳过」那条断言就失去被测对象。
    var probe = new TBaseObject
    {
        m_PEnvir = environment,
        m_sCharName = $"UnionProbe{x}_{y}",
        m_nCurrX = (short)x,
        m_nCurrY = (short)y,
        m_btRaceServer = Grobal2.RC_MONSTER
    };
    probe.m_WAbil.HP = 100000;
    probe.m_WAbil.MaxHP = 100000;
    Assert(ReferenceEquals(probe, environment.AddToMap(x, y,
        CellType.OS_MOVINGOBJECT, probe)), "native union probe map add");
    return probe;
}

static AnimalObject AddNativeUnionAnimalProbe(Envirnoment environment, int x,
    int y)
{
    var probe = new AnimalObject
    {
        m_PEnvir = environment,
        m_sCharName = $"UnionAnimal{x}_{y}",
        m_nCurrX = (short)x,
        m_nCurrY = (short)y,
        m_btRaceServer = Grobal2.RC_MONSTER
    };
    probe.m_WAbil.HP = 100000;
    probe.m_WAbil.MaxHP = 100000;
    Assert(ReferenceEquals(probe, environment.AddToMap(x, y,
        CellType.OS_MOVINGOBJECT, probe)), "native union animal probe map add");
    return probe;
}

static Envirnoment CreateNativeUnionCombatEnvironment()
{
    const short width = 32;
    const short height = 32;
    var mapPath = Path.Combine(AppContext.BaseDirectory,
        "hero-union-combat.map");
    var mapBytes = new byte[52 + width * height * 12];
    BinaryPrimitives.WriteInt16LittleEndian(mapBytes.AsSpan(0, sizeof(short)),
        width);
    BinaryPrimitives.WriteInt16LittleEndian(mapBytes.AsSpan(2, sizeof(short)),
        height);
    File.WriteAllBytes(mapPath, mapBytes);
    var environment = new Envirnoment();
    Assert(environment.LoadMapData(mapPath),
        "native union combat map load");
    // AddToMap 的节点循环带战神 sub_765D64 有效性谓词
    // (0x7777EA call 0x765D64 / 0x7777F1 jne 0x777898)，其三项之一是
    // PEnvir.MapName <> ''（0x765D85 83 78 44 00 cmp dword [eax+0x44],0）。
    // 生产里地图对象一律带图名，夹具也必须给，否则格内已有的对象会被当悬挂项摘链。
    environment.sMapName = "HeroUnionCombat";
    return environment;
}

static int ExpectedNativeUnionAction(byte heroJob, byte ownerJob)
{
    if (heroJob == 0 && ownerJob == 0)
        return Grobal2.RM_WWJATTACK;
    if ((heroJob == 0 && ownerJob == 1) ||
        (heroJob == 1 && ownerJob == 0))
        return Grobal2.RM_WSJATTACK;
    if ((heroJob == 0 && ownerJob == 2) ||
        (heroJob == 2 && ownerJob == 0))
        return Grobal2.RM_WTJATTACK;
    return 0;
}

static void AssertNativeUnionAction(TBaseObject actor, TBaseObject target,
    int expectedIdent, string label)
{
    var messages = actor.m_MsgList.Where(message =>
        message.wIdent == Grobal2.RM_WWJATTACK ||
        message.wIdent == Grobal2.RM_WSJATTACK ||
        message.wIdent == Grobal2.RM_WTJATTACK).ToArray();
    if (expectedIdent == 0)
    {
        Equal(0, messages.Length, label + " absent");
        return;
    }

    var message = messages.Single();
    Equal(expectedIdent, message.wIdent, label + " ident");
    Equal((int)HeroObject.GetNativeUnionActionDirection(actor.m_nCurrX,
        actor.m_nCurrY, target.m_nCurrX, target.m_nCurrY), message.wParam,
        label + " direction");
    Equal(actor.m_nCurrX, message.nParam1, label + " x");
    Equal(actor.m_nCurrY, message.nParam2, label + " y");
    Equal(0, message.nParam3, label + " trailing parameter");
    Assert(ReferenceEquals(actor, message.BaseObject), label + " source");
}

static (TPlayObject Owner, HeroObject Hero) CreateNativeUnionReleaseFixture(
    byte heroJob, byte ownerJob, Envirnoment environment = null)
{
    environment ??= new Envirnoment();
    var owner = new TPlayObject
    {
        m_sCharName = $"UnionOwner{heroJob}_{ownerJob}",
        m_btJob = ownerJob,
        m_PEnvir = environment,
        m_nCurrX = 0,
        m_nCurrY = 0,
        m_boFixedHideMode = true
    };
    owner.m_WAbil.MP = 100;
    owner.m_WAbil.HP = 100;
    owner.m_WAbil.DC = HUtil32.MakeLong(10, 10);

    var hero = new NativeUnionTestHeroObject
    {
        m_sCharName = $"UnionHero{heroJob}_{ownerJob}",
        m_Master = owner,
        m_btJob = heroJob,
        m_PEnvir = environment,
        m_nCurrX = 0,
        m_nCurrY = 0,
        m_boFixedHideMode = true,
        m_btNativeUnionState = 2,
        m_wNativeUnionEnergy = 77
    };
    hero.m_WAbil.MP = 100;
    hero.m_WAbil.HP = 100;
    hero.m_WAbil.DC = HUtil32.MakeLong(10, 10);
    hero.m_TargetCret = new TBaseObject
    {
        m_sCharName = $"UnionTarget{heroJob}_{ownerJob}",
        m_nCurrX = 1,
        m_nCurrY = 0,
        m_btRaceServer = Grobal2.RC_MONSTER,
        m_PEnvir = environment
    };
    hero.m_TargetCret.m_WAbil.HP = 1000;
    hero.m_TargetCret.m_WAbil.MaxHP = 1000;
    hero.m_HeroMagicList.Add(new TUserMagic
    {
        wMagIdx = 50,
        btLevel = 0,
        MagicInfo = new TMagic
        {
            wMagicID = 50,
            btEffect = 1,
            wSpell = 4,
            wPower = 1,
            wMaxPower = 2,
            btTrainLv = 3,
            btDefSpell = 1,
            btDefPower = 1,
            btDefMaxPower = 2
        }
    });
    hero.RestoreNativeUnionMagicCacheForLogon();
    return (owner, hero);
}

static void CheckCm1108Ordering()
{
    foreach (var blockedState in new[] { 0x33, 0x34 })
    {
        var player = CreateCm1108Player(1, false);
        SeedCm1108Cancels(player);
        Assert(player.SetNativeActiveState(blockedState),
            $"CM1108 state {blockedState:X2} setup");
        var messageCount = player.m_MsgList.Count;

        player.Operate(new TProcessMessage
        {
            wIdent = Grobal2.CM_HERO_POWERUP
        });

        AssertCm1108CancelsRetained(player,
            $"CM1108 state {blockedState:X2} gate");
        Equal(unchecked((int)0x55667788),
            player.m_nNativeUnionActivationCarrier,
            $"CM1108 state {blockedState:X2} carrier");
        Equal((byte)1, player.m_HeroObject.m_btNativeUnionState,
            $"CM1108 state {blockedState:X2} hero state");
        Equal(messageCount, player.m_MsgList.Count,
            $"CM1108 state {blockedState:X2} messages");
    }

    var success = CreateCm1108Player(1, false);
    SeedCm1108Cancels(success);
    var successMessages = success.m_MsgList.Count;
    success.Operate(new TProcessMessage
    {
        wIdent = Grobal2.CM_HERO_POWERUP
    });
    AssertCm1108CancelsCleared(success, "CM1108 success");
    Equal(0, success.m_nNativeUnionActivationCarrier,
        "CM1108 success carrier clear");
    Equal((byte)2, success.m_HeroObject.m_btNativeUnionState,
        "CM1108 success state 1 to 2");
    AssertCm1108CancelMessages(success, successMessages, "CM1108 success");

    var mapBlocked = CreateCm1108Player(1, true);
    SeedCm1108Cancels(mapBlocked);
    var mapBlockedMessages = mapBlocked.m_MsgList.Count;
    mapBlocked.Operate(new TProcessMessage
    {
        wIdent = Grobal2.CM_HERO_POWERUP
    });
    AssertCm1108CancelsCleared(mapBlocked, "CM1108 map gate");
    Equal(unchecked((int)0x55667788),
        mapBlocked.m_nNativeUnionActivationCarrier,
        "CM1108 map gate carrier retained");
    Equal((byte)1, mapBlocked.m_HeroObject.m_btNativeUnionState,
        "CM1108 map gate hero state");
    AssertCm1108CancelMessages(mapBlocked, mapBlockedMessages,
        "CM1108 map gate");

    var heroNotReady = CreateCm1108Player(0, false);
    SeedCm1108Cancels(heroNotReady);
    var heroNotReadyMessages = heroNotReady.m_MsgList.Count;
    heroNotReady.Operate(new TProcessMessage
    {
        wIdent = Grobal2.CM_HERO_POWERUP
    });
    AssertCm1108CancelsCleared(heroNotReady, "CM1108 hero-state gate");
    Equal(unchecked((int)0x55667788),
        heroNotReady.m_nNativeUnionActivationCarrier,
        "CM1108 hero-state gate carrier retained");
    Equal((byte)0, heroNotReady.m_HeroObject.m_btNativeUnionState,
        "CM1108 hero-state gate state retained");
    AssertCm1108CancelMessages(heroNotReady, heroNotReadyMessages,
        "CM1108 hero-state gate");
}

static TPlayObject CreateCm1108Player(byte heroState, bool mapBlocked)
{
    var environment = new Envirnoment();
    if (mapBlocked)
        environment.LimitSkillIds.Add(0);
    return new TPlayObject
    {
        m_PEnvir = environment,
        m_nCurrX = 0,
        m_nCurrY = 0,
        m_boFixedHideMode = true,
        m_HeroObject = new HeroObject
        {
            m_btNativeUnionState = heroState
        }
    };
}

static void SeedCm1108Cancels(TPlayObject player)
{
    player.m_dwNativeChannelMagicTick = 0x11223344;
    player.m_wNativeChannelMagicId = 0x72;
    player.m_wNativeChannelMagicParam = 0x3344;

    player.m_boNativeLocationChannelActive = true;
    player.m_dwNativeLocationChannelStartTick = 0x01020304;
    player.m_dwNativeLocationChannelPulseTick = 0x11121314;
    player.m_nNativeLocationChannelContext0 = 0x21222324;
    player.m_dwNativeLocationChannelDuration = 0x31323334;
    player.m_nNativeLocationChannelContext1 = 0x41424344;
    player.m_nNativeLocationChannelContext2 = 0x51525354;
    player.m_nNativeLocationChannelX = 0x1111;
    player.m_nNativeLocationChannelY = 0x2222;
    player.m_nNativeLocationChannelMapToken = 0x61626364;
    player.m_wNativeLocationChannelMagicId = 0x3456;

    player.m_boNativeHorseCallPending = true;
    player.m_dwNativeHorseCallTick = 0x71727374;
    player.m_wNativeHorseCallDelay = 0x4546;
    player.m_nNativeUnionActivationCarrier = unchecked((int)0x55667788);
}

static void AssertCm1108CancelsRetained(TPlayObject player, string label)
{
    Equal(0x11223344u, player.m_dwNativeChannelMagicTick,
        label + " channel tick");
    Equal((ushort)0x72, player.m_wNativeChannelMagicId,
        label + " channel magic");
    Equal((ushort)0x3344, player.m_wNativeChannelMagicParam,
        label + " channel param");

    Assert(player.m_boNativeLocationChannelActive,
        label + " location active");
    Equal(0x01020304u, player.m_dwNativeLocationChannelStartTick,
        label + " location start");
    Equal(0x11121314u, player.m_dwNativeLocationChannelPulseTick,
        label + " location pulse");
    Equal(0x21222324, player.m_nNativeLocationChannelContext0,
        label + " location context0");
    Equal(0x31323334u, player.m_dwNativeLocationChannelDuration,
        label + " location duration");
    Equal(0x41424344, player.m_nNativeLocationChannelContext1,
        label + " location context1");
    Equal(0x51525354, player.m_nNativeLocationChannelContext2,
        label + " location context2");
    Equal(0x1111, player.m_nNativeLocationChannelX, label + " location x");
    Equal(0x2222, player.m_nNativeLocationChannelY, label + " location y");
    Equal(0x61626364, player.m_nNativeLocationChannelMapToken,
        label + " location map token");
    Equal((ushort)0x3456, player.m_wNativeLocationChannelMagicId,
        label + " location magic");

    Assert(player.m_boNativeHorseCallPending, label + " horse pending");
    Equal(0x71727374u, player.m_dwNativeHorseCallTick,
        label + " horse tick");
    Equal((ushort)0x4546, player.m_wNativeHorseCallDelay,
        label + " horse delay");
}

static void AssertCm1108CancelsCleared(TPlayObject player, string label)
{
    Equal(0u, player.m_dwNativeChannelMagicTick, label + " channel tick");
    Equal((ushort)0, player.m_wNativeChannelMagicId,
        label + " channel magic");
    Equal((ushort)0, player.m_wNativeChannelMagicParam,
        label + " channel param");

    Assert(!player.m_boNativeLocationChannelActive,
        label + " location active");
    Equal(0u, player.m_dwNativeLocationChannelStartTick,
        label + " location start");
    Equal(0u, player.m_dwNativeLocationChannelPulseTick,
        label + " location pulse");
    Equal(0, player.m_nNativeLocationChannelContext0,
        label + " location context0");
    Equal(0u, player.m_dwNativeLocationChannelDuration,
        label + " location duration");
    Equal(0, player.m_nNativeLocationChannelContext1,
        label + " location context1");
    Equal(0, player.m_nNativeLocationChannelContext2,
        label + " location context2");
    Equal(0, player.m_nNativeLocationChannelX, label + " location x");
    Equal(0, player.m_nNativeLocationChannelY, label + " location y");
    Equal(0x61626364, player.m_nNativeLocationChannelMapToken,
        label + " location map token retained");
    Equal((ushort)0, player.m_wNativeLocationChannelMagicId,
        label + " location magic");

    Assert(!player.m_boNativeHorseCallPending, label + " horse pending");
    Equal(0u, player.m_dwNativeHorseCallTick, label + " horse tick");
    Equal((ushort)0, player.m_wNativeHorseCallDelay,
        label + " horse delay");
}

static void AssertCm1108CancelMessages(TPlayObject player, int start,
    string label)
{
    var messages = player.m_MsgList.Skip(start).ToArray();
    Equal(3, messages.Length, label + " cancel message count");
    AssertCm1108CancelMessage(messages[0], player, 1232, 0x72,
        label + " channel cancel");
    AssertCm1108CancelMessage(messages[1], player, 1234, 0x3456,
        label + " location cancel");
    AssertCm1108CancelMessage(messages[2], player,
        Grobal2.RM_NATIVE_HORSE_CALL_STOP, 0, label + " horse cancel");
}

static void AssertCm1108CancelMessage(SendMessage message,
    TPlayObject player, int ident, int oldMagicId, string label)
{
    Equal(ident, message.wIdent, label + " ident");
    Equal(oldMagicId, message.wParam, label + " old magic");
    Equal(0, message.nParam1, label + " nParam1");
    Equal(0, message.nParam2, label + " nParam2");
    Equal(0, message.nParam3, label + " nParam3");
    Assert(ReferenceEquals(player, message.BaseObject), label + " source");
}

static void CheckHeroLogonUnionState()
{
    var noMagicHero = CreateOfflineHero(2, 123);
    noMagicHero.SendHeroLogon();
    Equal((byte)0, noMagicHero.m_btNativeUnionState,
        "logon resets active union state without magic");
    Equal((ushort)0, noMagicHero.m_wNativeUnionEnergy,
        "logon resets active union energy without magic");
    Equal(0, UnionStatusMessages(noMagicHero).Length,
        "logon without union magic does not queue status");

    var skill69Hero = CreateOfflineHero(1, 77);
    skill69Hero.m_HeroMagicList.Add(new TUserMagic
    {
        wMagIdx = 69,
        MagicInfo = new TMagic { wMagicID = 69 }
    });
    skill69Hero.SendHeroLogon();
    Equal((byte)1, skill69Hero.m_btNativeUnionState,
        "skill69 does not alter union state on logon");
    Equal((ushort)77, skill69Hero.m_wNativeUnionEnergy,
        "skill69 does not alter union energy on logon");
    Equal(0, UnionStatusMessages(skill69Hero).Length,
        "skill69 does not queue union status");

    var unionHero = CreateOfflineHero(2, 177);
    unionHero.m_HeroMagicList.Add(new TUserMagic
    {
        wMagIdx = 300,
        MagicInfo = new TMagic
        {
            wMagicID = 300,
            btTrainLv = 3,
            TrainLevel = new byte[] { 10, 20, 30, 30 },
            MaxTrain = new[] { 300, 500, 700, 700 }
        }
    });
    unionHero.SendHeroLogon();
    Equal((byte)0, unionHero.m_btNativeUnionState,
        "logon resets active union state with union magic");
    Equal((ushort)0, unionHero.m_wNativeUnionEnergy,
        "logon resets active union energy with union magic");
    var statusMessages = UnionStatusMessages(unionHero);
    Equal(1, statusMessages.Length,
        "logon with union magic queues one status");
    Equal(0, statusMessages[0].wParam, "logon union status energy");
    Equal(0, statusMessages[0].nParam1, "logon union status state");
    Equal(0, statusMessages[0].nParam2,
        "logon union status reserved parameter");
    Equal(0, statusMessages[0].nParam3,
        "logon union status trailing parameter");
    Assert(ReferenceEquals(unionHero, statusMessages[0].BaseObject),
        "logon union status source hero");
    var tailMessages = unionHero.m_MsgList.Where(message =>
        message.wIdent == Grobal2.RM_HERO_UNIONSTATUS ||
        message.wIdent == Grobal2.RM_MAGIC_LVEXP ||
        message.wIdent == Grobal2.RM_ABILITY).ToArray();
    Assert(tailMessages.Select(message => message.wIdent).SequenceEqual(
        new[]
        {
            Grobal2.RM_HERO_UNIONSTATUS,
            Grobal2.RM_MAGIC_LVEXP,
            Grobal2.RM_ABILITY
        }), "logon native union tail ordering");
    Equal(300, tailMessages[1].nParam1,
        "logon union progress magic id");
    Equal(0, tailMessages[1].nParam2,
        "logon union progress level");
    Equal(0, tailMessages[1].nParam3,
        "logon union progress experience");
    Equal(300, BitConverter.ToInt32((byte[])tailMessages[1].Payload, 0),
        "logon union progress required train");
}

static void CheckScriptUnionLearnCache()
{
    const string magicName = "script-union-cache";
    M2Share.UserEngine.m_HeroMagicList.Add(new TMagic
    {
        wMagicID = 50,
        sMagicName = magicName,
        btTrainLv = 3,
        TrainLevel = new byte[] { 10, 20, 30, 30 },
        MaxTrain = new[] { 300, 500, 700, 700 }
    });

    var hero = CreateOfflineHero(1, 87);
    hero.SendHeroLogon();
    hero.m_MsgList.Clear();

    Assert(hero.LearnHeroMagic(magicName),
        "script union magic learn");
    Equal((byte)1, hero.m_btNativeUnionState,
        "script learn preserves union state");
    Equal((ushort)87, hero.m_wNativeUnionEnergy,
        "script learn preserves union energy");
    Equal(0, UnionStatusMessages(hero).Length,
        "script learn does not queue union status");
    Equal(-2, hero.CheckIfCanAddUSExp(),
        "script learn does not populate native union cache");

    hero.SendHeroLogon();
    Equal(-3, hero.CheckIfCanAddUSExp(),
        "logon scan restores native union cache");
    Equal(1, UnionStatusMessages(hero).Length,
        "logon after script learn queues union status");
}

static void CheckNativeUnionMagicLevelBonus()
{
    var ownerRaw = new byte[NativeHumanDataCodec.DataRecordSize];
    ownerRaw[0x138] = 250;
    ownerRaw[0x139] = 0x7F;
    var owner = new TPlayObject
    {
        m_boOffLineFlag = true,
        m_NativeHumanData = ownerRaw
    };
    owner.RecalcAbilitys();

    var heroRaw = new byte[NativeHeroDbFrameCodec.HeroRecordSize];
    heroRaw[NativeHeroDbFrameCodec.HeroTypeOffset] = 1;
    heroRaw[0x138] = 10;
    heroRaw[0x139] = 0xA5;
    Assert(NativeHeroDbFrameCodec.TryCreateRecord(heroRaw,
        out var heroRecord, out var error), error);
    var hero = new HeroObject();
    Assert(NativeHeroRuntimeCodec.TryApply(hero, heroRecord,
        new NativeHeroDynamicData(Array.Empty<NativeHeroDynamicSection>()),
        out error), error);
    hero.m_Master = owner;

    Equal((byte)250, owner.NativeMagicLevelBonus,
        "owner magic level bonus uses raw 0x138 low byte");
    Equal((byte)10, hero.NativeMagicLevelBonus,
        "hero magic level bonus uses raw 0x138 low byte");

    var magic = new TUserMagic
    {
        wMagIdx = 50,
        btLevel = 2,
        MagicInfo = new TMagic
        {
            wMagicID = 50,
            btTrainLv = 4,
            TrainLevel = new byte[] { 10, 20, 30, 30 },
            MaxTrain = new[] { 300, 500, 700, 700 }
        }
    };
    hero.m_HeroMagicList.Add(magic);

    hero.SendHeroLogon();

    var progress = hero.m_MsgList.Single(message =>
        message.wIdent == Grobal2.RM_MAGIC_LVEXP);
    Equal((byte)4, magic.NativeLevelBonus,
        "union magic master plus hero bonus preserves byte wrap");
    Equal(4, progress.nParam2,
        "union magic effective level respects train cap");

    hero.m_MsgList.Clear();
    magic.btLevel = 254;
    hero.SendHeroLogon();

    progress = hero.m_MsgList.Single(message =>
        message.wIdent == Grobal2.RM_MAGIC_LVEXP);
    Equal(2, progress.nParam2,
        "union magic effective level preserves byte wrap");
}

static void CheckHeroSkillBookLearning()
{
    const string rewardName = "\u706b\u9f99\u4e4b\u5fc3";
    const string unionBookName = "native-hero-union-book";
    const string ordinaryUnionBookName = "native-hero-ordinary-union-book";
    const string initialLevelBookName = "native-hero-initial-level-book";

    AddStdItem(new GoodItem
    {
        Name = rewardName,
        StdMode = 25,
        Shape = 9,
        Weight = 2,
        DuraMax = 100
    });
    var unionBookIndex = AddStdItem(new GoodItem
    {
        Name = unionBookName,
        StdMode = 4,
        Weight = 0
    });
    M2Share.UserEngine.m_HeroMagicList.Add(new TMagic
    {
        wMagicID = 50,
        sMagicName = unionBookName,
        btJob = 99,
        btTrainLv = 3,
        TrainLevel = new byte[] { 10, 20, 30, 30 },
        MaxTrain = new[] { 300, 500, 700, 700 }
    });

    var success = CreateHeroBookFixture(unionBookIndex, 0, 0, 1, 177);
    success.Hero.ClientHeroUseItem(new TProcessMessage
    {
        nParam1 = success.Owner.EnsureClientItemId(success.Book)
    });
    Assert(!success.Hero.m_ItemList.Contains(success.Book),
        "union skill book consumed");
    var learnedUnion = success.Hero.m_HeroMagicList.Single(magic =>
        magic.MagicInfo.wMagicID == 50);
    Equal((byte)0, learnedUnion.btLevel,
        "union skill book initial level");
    Assert(success.Hero.IsCachedNativeUnionMagic(learnedUnion),
        "union skill book populates native cache");
    Equal((byte)0, success.Hero.m_btNativeUnionState,
        "union skill book clears state");
    Equal((ushort)0, success.Hero.m_wNativeUnionEnergy,
        "union skill book clears energy");
    var unionStatus = UnionStatusMessages(success.Hero).Single();
    Equal(0, unionStatus.wParam, "union skill book status energy");
    Equal(0, unionStatus.nParam1, "union skill book status state");
    Equal(-3, success.Hero.CheckIfCanAddUSExp(),
        "union skill book cache active without union item");
    var reward = success.Hero.m_ItemList.Single();
    Equal(rewardName, M2Share.UserEngine.GetStdItem(reward.wIndex).Name,
        "union skill book reward");
    Equal((ushort)2, success.Hero.m_WAbil.Weight,
        "union skill book reward weight");

    var jobMismatch = CreateHeroBookFixture(unionBookIndex, 1, 0, 1, 66);
    jobMismatch.Hero.ClientHeroUseItem(new TProcessMessage
    {
        nParam1 = jobMismatch.Owner.EnsureClientItemId(jobMismatch.Book)
    });
    Assert(jobMismatch.Hero.m_ItemList.Contains(jobMismatch.Book),
        "union skill book wrong master job retained");
    Equal(0, jobMismatch.Hero.m_HeroMagicList.Count,
        "union skill book wrong master job not learned");
    Equal((byte)1, jobMismatch.Hero.m_btNativeUnionState,
        "union skill book failure preserves state");
    Equal((ushort)66, jobMismatch.Hero.m_wNativeUnionEnergy,
        "union skill book failure preserves energy");

    var ordinaryUnionBookIndex = AddStdItem(new GoodItem
    {
        Name = ordinaryUnionBookName,
        StdMode = 4,
        Weight = 0
    });
    M2Share.UserEngine.m_HeroMagicList.Add(new TMagic
    {
        wMagicID = 55,
        sMagicName = ordinaryUnionBookName,
        btJob = 0,
        btTrainLv = 3,
        TrainLevel = new byte[] { 0, 20, 30, 30 },
        MaxTrain = new[] { 300, 500, 700, 700 }
    });
    var ordinaryUnion = CreateHeroBookFixture(
        ordinaryUnionBookIndex, 2, 0, 1, 88);
    ordinaryUnion.Hero.ClientHeroUseItem(new TProcessMessage
    {
        nParam1 = ordinaryUnion.Owner.EnsureClientItemId(ordinaryUnion.Book)
    });
    Equal(1, ordinaryUnion.Hero.m_HeroMagicList.Count,
        "union id outside hero row uses ordinary job rule");
    Equal(-2, ordinaryUnion.Hero.CheckIfCanAddUSExp(),
        "ordinary-path union id does not populate cache");
    Equal((byte)1, ordinaryUnion.Hero.m_btNativeUnionState,
        "ordinary-path union id preserves state");
    Equal((ushort)88, ordinaryUnion.Hero.m_wNativeUnionEnergy,
        "ordinary-path union id preserves energy");
    Equal(0, UnionStatusMessages(ordinaryUnion.Hero).Length,
        "ordinary-path union id has no union status");

    var initialLevelBookIndex = AddStdItem(new GoodItem
    {
        Name = initialLevelBookName,
        StdMode = 4,
        Weight = 0
    });
    M2Share.UserEngine.m_HeroMagicList.Add(new TMagic
    {
        wMagicID = 151,
        sMagicName = initialLevelBookName,
        btJob = 0,
        btTrainLv = 3,
        TrainLevel = new byte[] { 0, 20, 30, 30 },
        MaxTrain = new[] { 300, 500, 700, 700 }
    });
    var initialLevel = CreateHeroBookFixture(
        initialLevelBookIndex, 0, 0, 0, 0);
    initialLevel.Hero.ClientHeroUseItem(new TProcessMessage
    {
        nParam1 = initialLevel.Owner.EnsureClientItemId(initialLevel.Book)
    });
    Equal((byte)1, initialLevel.Hero.m_HeroMagicList.Single().btLevel,
        "native special hero book starts at level one");
}

static ushort AddStdItem(GoodItem stdItem)
{
    var index = checked((ushort)M2Share.UserEngine.StdItemList.Count);
    stdItem.NativeWireIndex = index;
    M2Share.UserEngine.StdItemList.Add(stdItem);
    return index;
}

static (TPlayObject Owner, HeroObject Hero, TUserItem Book)
    CreateHeroBookFixture(ushort bookIndex, byte ownerJob, byte heroJob,
        byte state, ushort energy)
{
    var owner = new TPlayObject
    {
        m_boOffLineFlag = true,
        m_boCanUseItem = true,
        m_btJob = ownerJob
    };
    var hero = new HeroObject
    {
        m_Master = owner,
        m_btJob = heroJob,
        m_btNativeUnionState = state,
        m_wNativeUnionEnergy = energy
    };
    hero.m_Abil.Level = 60;
    owner.m_HeroObject = hero;
    var book = new TUserItem
    {
        wIndex = bookIndex,
        Dura = 1,
        DuraMax = 1
    };
    hero.m_ItemList.Add(book);
    return (owner, hero, book);
}

static HeroObject CreateOfflineHero(byte state, ushort energy)
{
    var hero = new HeroObject
    {
        m_btNativeUnionState = state,
        m_wNativeUnionEnergy = energy,
        m_Master = new TPlayObject { m_boOffLineFlag = true }
    };
    return hero;
}

static SendMessage[] UnionStatusMessages(HeroObject hero)
{
    return hero.m_MsgList.Where(message =>
        message.wIdent == Grobal2.RM_HERO_UNIONSTATUS).ToArray();
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

static void CheckConfigPathSemantics()
{
    var root = Path.Combine(AppContext.BaseDirectory, "hero-union-config");
    Directory.CreateDirectory(root);
    var setConfig = Path.Combine(root, "SetConfig");
    Directory.CreateDirectory(setConfig);
    var setup = Path.Combine(root, "!Setup.txt");
    File.WriteAllText(setup, "[Server]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(root, "FormGMSet.ini"),
        "SETKEY_MAXLQ=321" + Environment.NewLine +
        "SETF_HERO_LV_NQ=SetConfig\\hero_lv_nq.txt" + Environment.NewLine);
    File.WriteAllText(Path.Combine(setConfig, "hero_lv_nq.txt"),
        "0=6" + Environment.NewLine + "27=9" + Environment.NewLine);

    M2Share.g_Config = new GameSvrConfig();
    new ServerConfig(setup).LoadConfig();
    Equal(321, M2Share.g_Config.nHeroUnionMaxEnergy, "SETKEY_MAXLQ");
    Equal(6, M2Share.g_Config.HeroUnionChargeOverrides[0],
        "relative charge table key0");
    Equal(9, M2Share.g_Config.HeroUnionChargeOverrides[27],
        "relative charge table key27");

    File.WriteAllText(Path.Combine(root, "FormGMSet.ini"),
        "SETKEY_MAXLQ=200" + Environment.NewLine +
        "SETF_HERO_LV_NQ=hero_lv_nq.txt" + Environment.NewLine);
    File.WriteAllText(Path.Combine(root, "hero_lv_nq.txt"), string.Empty);
    new ServerConfig(setup).LoadConfig();
    Equal(0, M2Share.g_Config.HeroUnionChargeOverrides.Count,
        "configured empty table does not fall back to SetConfig");
}

static void CheckMapSkillFlags()
{
    const short width = 4;
    const short height = 3;
    var root = Path.Combine(AppContext.BaseDirectory, "hero-union-map");
    Directory.CreateDirectory(root);
    var mapPath = Path.Combine(root, "union-gate.map");
    var mapBytes = new byte[52 + width * height * 12];
    BinaryPrimitives.WriteInt16LittleEndian(mapBytes.AsSpan(0, sizeof(short)), width);
    BinaryPrimitives.WriteInt16LittleEndian(mapBytes.AsSpan(2, sizeof(short)), height);
    for (var i = 0; i < width * height; i++)
        mapBytes[52 + i * 12 + 4] = 0xA5;
    File.WriteAllBytes(mapPath, mapBytes);

    var environment = new Envirnoment();
    Assert(environment.LoadMapData(mapPath), "map skill flag test map load");

    var commandAttribute = (GameCommandAttribute)Attribute.GetCustomAttribute(
        typeof(SetNoSkillZoneCommand), typeof(GameCommandAttribute));
    Assert(commandAttribute != null, "SetNoSkillZone command registration");
    Equal((byte)5, commandAttribute.nPermissionMin,
        "SetNoSkillZone native permission");
    Equal("left right top bot on/off", commandAttribute.Help,
        "SetNoSkillZone native help arguments");
    Equal("设置地图点能否使用技能", commandAttribute.Desc,
        "SetNoSkillZone native description");
    Assert(CommandManager.ExtractCommandAndParameters(
               "@SetNoSkillZone:0:1:0:1:on", out var commandName,
               out var commandParameters),
        "native colon command extraction");
    Equal("SetNoSkillZone", commandName, "native colon command name");
    Equal("0:1:0:1:on", commandParameters,
        "native colon command parameters");
    Equal((byte)0, environment.GetMapCellSkillFlag(0, 0),
        "raw map record byte4 is not the runtime skill byte");
    Assert(environment.IsSkillAllowedAt(1, 1, 0),
        "empty runtime cell allows skill key0");

    environment.SetMapCellSkillFlag(1, 2, 1, 2, 0x80);
    foreach (var (x, y) in new[] { (1, 1), (1, 2), (2, 1), (2, 2) })
        Equal((byte)0x80, environment.GetMapCellSkillFlag(x, y),
            "native rectangle uses inclusive endpoints");
    Assert(!environment.IsSkillAllowedAt(1, 1, 0),
        "any nonzero runtime cell byte blocks skill");
    Assert(environment.IsSkillAllowedAt(0, 0, 0),
        "rectangle leaves outside cell unchanged");

    var player = new TPlayObject { m_PEnvir = environment };
    var command = new SetNoSkillZoneCommand();
    command.Register(commandAttribute,
        typeof(SetNoSkillZoneCommand).GetMethod(
            nameof(SetNoSkillZoneCommand.SetNoSkillZone)));

    player.m_btPermission = 4;
    M2Share.g_Config.boTestServer = false;
    // "权限不够!!!" is 0 hits in the M2 baseline. The native reply is concatenated from two
    // adjacent constants:
    //   0x62B760 FF FF FF FF 0A 00 00 00  B8 C3 C3 FC C1 EE D0 E8 D2 AA  = "该命令需要" (10)
    //   0x62B774 FF FF FF FF 0C 00 00 00  BC B6 47 4D B2 C5 C4 DC CA B9 D3 C3
    //                                                                    = "级GM才能使用" (12)
    Equal("该命令需要" + commandAttribute.nPermissionMin + "级GM才能使用",
        command.Handle("0 0 0 0 on", player),
        "non-test permission4 remains below command permission5");
    Equal((byte)0, environment.GetMapCellSkillFlag(0, 0),
        "rejected permission does not mutate map");
    M2Share.g_Config.boTestServer = true;
    command.Handle("0::0,0  0:off", player);
    Assert(player.m_MsgList.Count > 0,
        "test-server permission4 executes permission5 command");
    M2Share.g_Config.boTestServer = false;
    player.m_btPermission = 5;

    command.SetNoSkillZone(new[] { "0", "1", "0", "1", "ON" }, player);
    Equal((byte)1, environment.GetMapCellSkillFlag(0, 0),
        "SetNoSkillZone on writes byte one");
    Equal((byte)1, environment.GetMapCellSkillFlag(1, 1),
        "SetNoSkillZone on includes right/bottom endpoint");
    command.SetNoSkillZone(new[] { "0", "2", "0", "2", "off" }, player);
    Equal((byte)0, environment.GetMapCellSkillFlag(1, 1),
        "SetNoSkillZone off clears rectangle");
    Equal((byte)0, environment.GetMapCellSkillFlag(2, 2),
        "SetNoSkillZone off includes right/bottom endpoint");
    var successMessage = player.m_MsgList[^1];
    Equal(Grobal2.RM_SYSMESSAGE, successMessage.wIdent,
        "SetNoSkillZone success message ident");
    Equal((int)M2Share.g_Config.btGreenMsgFColor, successMessage.nParam1,
        "SetNoSkillZone success foreground");
    Equal((int)M2Share.g_Config.btGreenMsgBColor, successMessage.nParam2,
        "SetNoSkillZone success background");
    Equal("该区域可以继续使用技能", successMessage.Buff,
        "SetNoSkillZone off success text");

    player.m_MsgList.Clear();
    command.SetNoSkillZone(new[] { "bad", "0", "0", "0", "on" }, player);
    Equal((byte)0, environment.GetMapCellSkillFlag(0, 0),
        "invalid coordinate does not become zero");
    var usageMessage = player.m_MsgList.Single();
    Equal((int)M2Share.g_Config.btGreenMsgFColor, usageMessage.nParam1,
        "SetNoSkillZone usage foreground");
    Equal((int)M2Share.g_Config.btGreenMsgBColor, usageMessage.nParam2,
        "SetNoSkillZone usage background");
    Equal("格式：SetNoSkillZone: left right top bot on/off (on表示禁止攻击)",
        usageMessage.Buff, "SetNoSkillZone native usage text");

    environment.SetMapXYFlag(0, 0, false);
    Assert(environment.IsSkillAllowedAt(0, 0, 0),
        "wall attribute does not participate in native skill gate");
    Assert(environment.IsSkillAllowedAt(-1, -1, 0),
        "out-of-bounds native cell byte reads as zero");

    environment.LimitSkillIds.Add(0);
    Assert(!environment.IsSkillAllowedAt(0, 0, 0),
        "LimitSkill key0 blocks independently of cell byte");
    Assert(!environment.IsSkillAllowedAt(-1, -1, 0),
        "LimitSkill key0 still blocks out-of-bounds coordinate");
    environment.LimitSkillIds.Clear();

    var gateHero = new HeroObject { m_btNativeUnionState = 1 };
    player.m_HeroObject = gateHero;
    player.m_nCurrX = 0;
    player.m_nCurrY = 0;
    environment.LimitSkillIds.Add(69);
    player.Operate(new TProcessMessage { wIdent = Grobal2.CM_HERO_POWERUP });
    Equal((byte)2, gateHero.m_btNativeUnionState,
        "CM1108 tests LimitSkill key0 rather than skill69");

    gateHero.m_btNativeUnionState = 1;
    environment.LimitSkillIds.Clear();
    environment.LimitSkillIds.Add(0);
    player.Operate(new TProcessMessage { wIdent = Grobal2.CM_HERO_POWERUP });
    Equal((byte)1, gateHero.m_btNativeUnionState,
        "CM1108 LimitSkill key0 gate");

    environment.LimitSkillIds.Clear();
    environment.SetMapCellSkillFlag(0, 0, 0, 0, 1);
    player.Operate(new TProcessMessage { wIdent = Grobal2.CM_HERO_POWERUP });
    Equal((byte)1, gateHero.m_btNativeUnionState,
        "CM1108 runtime cell-byte gate");
    environment.SetMapCellSkillFlag(0, 0, 0, 0, 0);

    environment.SetMapCellSkillFlag(1, 1, height, height, 1);
    Equal((byte)1, environment.GetMapCellSkillFlag(2, 0),
        "native bottom-equals-height aliases next column");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual, string message)
    where T : IEquatable<T>
{
    if (!expected.Equals(actual))
        throw new InvalidOperationException(
            $"{message}: expected={expected}, actual={actual}");
}

sealed class NativeUnionTestHeroObject : HeroObject
{
    public override bool IsProperTarget(TBaseObject baseObject)
    {
        return baseObject != null;
    }
}
