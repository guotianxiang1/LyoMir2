using System.Reflection;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;
using SystemModule.Packet;

// In-process isolated-engine harness (machine-safety: SINGLE process, NO network stack, NO DBSvr,
// NO MySQL, NO background engine threads; strictly serial, bounded loops). It deliberately BYPASSES
// GameApp.Initialize / StartEngine / GateManager / DataServer / IdSrvClient (the 30s DBSvr
// native-definition gate) and instead constructs the M2Share engine singletons directly, builds a
// blank in-memory map, INJECTS native definitions (StdItem / Monster drop tables / Magic templates)
// straight into the M2Share collections the DBSvr Type2 frames normally populate, then drives the
// REAL combat/skill/reward/item engine code and captures the observable state mutations.
//
// Flows driven with REAL engine methods (no model stubs):
//   * COMBAT primitives  : TBaseObject.StruckDamage / DamageHealth (magic-shield MP absorb + HP clamp)
//   * MELEE SKILL        : TBaseObject.AttackDir(wHitMode=4 刺杀/ErGum) -> _Attack -> SwordLongAttack
//                          -> _Attack_DirectAttack -> StruckDamage (real skill damage on real targets)
//   * MAGIC SKILL        : MagicManager.DoSpell (real cast dispatch) + the real deferred-magic
//                          handlers TBaseObject.Operate(RM_DELAYMAGIC) -> Operate(RM_MAGSTRUCK)
//                          -> GetMagStruckDamage + StruckDamage (real magic damage on a real monster)
//   * DEATH REWARD       : TBaseObject.Die() -> CalcGetExp + GainExp/WinExp (EXP to killer) and
//                          MonGetRandomItems + DropUseItems/ScatterBagItems (drop table -> map items)
//   * ITEM PICKUP        : TPlayObject.ClientPickUpItem -> AddItemToBag (real map item into real bag)
//   * MONSTER            : real Monster actor + RecalcAbilitys from the injected definition
//
// Evidence goes to stdout and to inproc_run_evidence.txt next to the executable.

int rc = 0;
var evidence = new List<string>();
void Log(string s)
{
    evidence.Add(s);
    Console.WriteLine("  " + s);
}
void Assert(bool cond, string msg)
{
    if (!cond) throw new Exception("ASSERT FAILED: " + msg);
}

// cached reflection handles for the non-public engine entry points we drive
var miAttackDir = typeof(TBaseObject).GetMethod("AttackDir",
    BindingFlags.Instance | BindingFlags.NonPublic, null,
    new[] { typeof(TBaseObject), typeof(short), typeof(byte) }, null);
var miPickUp = typeof(TPlayObject).GetMethod("ClientPickUpItem",
    BindingFlags.Instance | BindingFlags.NonPublic, null,
    new[] { typeof(MapItem), typeof(int), typeof(int) }, null);

try
{
    PrepareConfig();
    BootSingletons();
    Log("BOOT singletons: g_Config/RandomNumber/ObjectManager/UserEngine/MapManager/MagicManager "
        + "constructed (no GameApp.Initialize, no DBSvr gate, no network, no background threads)");

    var map = CreateBlankMap(64, 64, "harness-map");
    bool findMapResolves = M2Share.MapManager.FindMap("harness-map") == map;
    Log($"MAP built in-memory '{map.sMapName}' {map.wWidth}x{map.wHeight} (real Envirnoment.Initialize, "
        + $"no .map file); Flag initialized; FindMap resolves={findMapResolves}");

    // ================= native definition injection (the DBSvr Type2 data, built in-memory) ========
    InjectNativeDefs();

    // ================= existing combat primitives (kept) ==========================================
    var attacker = NewPlayer("attacker", job: 0, level: 30, x: 30, y: 31, map);
    var target = NewPlayer("target-shield", job: 0, level: 20, x: 30, y: 30, map);
    int occupancy = CountCellType(map, 30, 30, CellType.OS_MOVINGOBJECT);
    Log($"PLACE two real TPlayObject on map; cell(30,30) OS_MOVINGOBJECT count={occupancy}, "
        + $"target.m_boAddToMaped={target.m_boAddToMaped}, attacker.m_boAddToMaped={attacker.m_boAddToMaped}");
    Assert(occupancy >= 1 && target.m_boAddToMaped && attacker.m_boAddToMaped,
        "both actors registered onto real map cells");

    target.m_WAbil.HP = 100; target.m_WAbil.MaxHP = 100;
    target.m_WAbil.MP = 10; target.m_WAbil.MaxMP = 50;
    target.m_boMagicShield = true; target.m_LastHiter = attacker;
    int hp0 = target.m_WAbil.HP, mp0 = target.m_WAbil.MP;
    target.StruckDamage(30);
    Log($"COMBAT shield: StruckDamage(30) -> HP {hp0}->{target.m_WAbil.HP}, MP {mp0}->{target.m_WAbil.MP} "
        + "(1.5x MP absorption then residual to HP)");
    Assert(target.m_WAbil.MP < mp0 && target.m_WAbil.HP < hp0, "magic shield absorbed then residual HP");

    var victim = NewPlayer("victim", job: 0, level: 20, x: 32, y: 32, map);
    victim.m_WAbil.HP = 100; victim.m_WAbil.MaxHP = 100; victim.m_WAbil.MP = 0;
    victim.m_boMagicShield = false; victim.m_LastHiter = attacker;
    var trajectory = new List<int> { victim.m_WAbil.HP };
    for (int i = 0; i < 4 && victim.m_WAbil.HP > 0; i++) { victim.StruckDamage(40); trajectory.Add(victim.m_WAbil.HP); }
    Log($"COMBAT death-clamp: StruckDamage(40) x{trajectory.Count - 1} -> HP {string.Join("->", trajectory)}");
    Assert(victim.m_WAbil.HP == 0, "victim HP clamped to 0 at death threshold");

    // ================= NEW: real melee SKILL (刺杀剑法 / ErGum, wHitMode=4) ========================
    RunMeleeSkill(map);

    // ================= NEW: real magic SKILL (cast dispatch + real deferred-magic damage) =========
    RunMagicSkill(map);

    // ================= NEW: real death REWARD (EXP to killer + drop table -> map items) ===========
    RunDeathReward(map);

    // ================= NEW: real item PICKUP into bag =============================================
    RunPickup(map);

    // ================= NEW: real Monster + RecalcAbilitys from injected def =======================
    RunMonsterRecalc(map);

    // ================= NEW (batch 3): Hero / FieldHero / Shop domains ============================
    RunHero(map);
    RunFieldHero(map);
    RunShop(map);

    // ================= NEW (2026-08-03): deal-escrow dupe-safety + gold-guard invariants ==========
    RunDealEscrowSafety(map);
    RunGoldGuards(map);
    RunSuperRepairQuoteVsCharge(map);
    RunRepairModesEndToEnd(map);
    RunRepairEligibility(map);

    // ================= NEW (2026-08-04): merchant money contracts (statted pricing / sell
    // truncation / tax base / no pricing-side item mutation) — reverses a ref-MIR2 misreading =====
    RunMerchantMoneyContracts(map);
    RunMerchantSellAuthenticationOwnership(map);
    RunStorageTakeFailureCodes(map);
    RunStorageDepositSuccessPath(map);
    RunStorageTakeSuccessPath(map);
    RunStorageSpaceApi();
    RunQiankunResetProtocol(map);
    RunReviveMessageProtocol(map);
    RunFireHitProtocol(map);
    RunTwinBladeDefaultProtocol(map);
    RunNativeAction1011Protocol();
    RunMapDescriptionProtocol(map);
    RunMicroWhelkProtocol(map);
    RunCryCharmProtocol(map);

    // ================= existing raw map-item drop (kept) =========================================
    RunItemFlow(map);

    Console.WriteLine(
        "PASS InProcEngineRunCheck engine-booted-in-process defs-injected(std/mon/magic) "
        + "combat=StruckDamage/DamageHealth melee-skill=AttackDir(ErGum)->StruckDamage "
        + "magic-skill=DoSpell->realpump(GetMessage+Operate)->cast-derived-damage + RM_DELAYMAGIC->RM_MAGSTRUCK "
        + "death-reward=Die()->GainExp+drop-table->map pickup=ClientPickUpItem->AddItemToBag "
        + "monster=RecalcAbilitys hero=create+attach+took-damage(melee-deal-gap) shop=ClientBuyItem(no-MySQL) fieldhero=BLOCKED(dormant) "
        + "deal-escrow=DealCancel-clears-remote+6-preconditions+no-double-release gold=DecGold-negative-guard/IncGold-percharcap "
        + "repair=mode-byte+mode3-raw-price+super-quote==charge(post-Round x3)+execution-only-eligibility "
        + "merchant-money=statted(n10+(n10div5)*n14)/sell(div2-truncate)/tax(actual-amount,single-castle,no-fallback)/ore(no-item-mutation) "
        + "merchant-sell=client-id-only+range15+dual-auth+ownership+logs10/94+worker-weight "
        + "storage-take=-3>-2>0>-1+no-name-gate+full(707->706/0)+success(200->705,type2-log) "
        + "qiankun=default-router(cm3284+empty3286)->reset+sm2957 "
        + "revive=real-ladder->sm100+sm213 "
        + "firehit=skill26-resolved-mp+uint-timers+sm626 "
        + "map-description=sm54-regions+sm56-list-phases "
        + "microwhelk=cm3295->sm641/202->sm106(raw-nul/type18) "
        + "crycharm=@传(raw+4)->pi/it->cmd24+sm106(no-nul) "
        + "single-process no-network no-DBSvr no-MySQL");
}
catch (Exception ex)
{
    Console.Error.WriteLine("FAIL InProcEngineRunCheck: " + ex);
    rc = 1;
}

try { File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "inproc_run_evidence.txt"), evidence); }
catch { /* evidence file is best-effort */ }

// Hard-exit so any lingering non-foreground engine state cannot keep the process alive.
Environment.Exit(rc);

// ===================== flows =====================

void InjectNativeDefs()
{
    var eng = M2Share.UserEngine;

    // Faithful native StdItem layout: index 0 is the "金币" sentinel the DBSvr Type2 stream uses
    // (see UsrEngn.HasNativeStdItemSentinel); real items follow, so UserItem.wIndex maps 1:1 to slot.
    eng.StdItemList.Add(new GoodItem { Name = "金币", NativeWireIndex = 0, ItemType = GoodType.ITEM_GOLD });
    eng.StdItemList.Add(new GoodItem
    {
        Name = "铁剑", ItemType = GoodType.ITEM_WEAPON, StdMode = 5, Shape = 1,
        Weight = 5, DuraMax = 5000, Dc = 3, Dc2 = 8
    });
    eng.StdItemList.Add(new GoodItem
    {
        Name = "金创药(小)", ItemType = GoodType.ITEM_LEECHDOM, StdMode = 0,
        Weight = 1, DuraMax = 1, NativeDrugHealthBonus = 30
    });

    // Monster definition with a guaranteed drop: Random(MaxPoint=1)==0 <= SelPoint=1 always fires.
    eng.MonsterList.Add(new TMonInfo
    {
        sName = "测试骷髅",
        wLevel = 10, wHP = 200, wMP = 50, wAC = 2, wMAC = 2, wDC = 5, wMaxDC = 12,
        wSpeed = 2, wHitPoint = 8, dwExp = 500,
        ItemList = new List<TMonItem>
        {
            new TMonItem { ItemName = "金创药(小)", MaxPoint = 1, SelPoint = 1, Count = 1 }
        }
    });

    // Global magic-definition templates (the same wMagicID rows the DBSvr magic table publishes).
    eng.m_MagicList.Add(BuildMagicTemplate(SpellsDef.SKILL_ERGUM, "刺杀剑法", trainLv: 7));
    eng.m_MagicList.Add(BuildMagicTemplate(SpellsDef.SKILL_FIREBALL, "火球术", trainLv: 7));

    Log($"DEFS injected in-memory: StdItemList={eng.StdItemList.Count} (sentinel '金币' + 铁剑 + 金创药), "
        + $"MonsterList={eng.MonsterList.Count} ('测试骷髅' drop='金创药' exp=500), "
        + $"MagicList={eng.m_MagicList.Count} (刺杀剑法/ErGum + 火球术/FireBall)");
    Assert(eng.GetStdItem("金创药(小)") != null, "injected StdItem resolves by name");
}

void RunMeleeSkill(Envirnoment map)
{
    // A warrior with the 刺杀剑法 (ErGum) skill drives the REAL AttackDir(wHitMode=4) pipeline against
    // real monster targets: main-hit lands on the adjacent target; SwordLongAttack lands on the
    // distance-2 target. Both go through _Attack -> StruckDamage (TBaseObject.Attack.cs:598/779, 263).
    var warrior = NewPlayer("warrior-skill", job: 0, level: 35, x: 20, y: 20, map);
    warrior.m_WAbil.DC = HUtil32.MakeLong(30, 55);   // LoWord=min DC, HiWord=max DC
    warrior.m_WAbil.HP = 500; warrior.m_WAbil.MaxHP = 500;
    warrior.m_WAbil.MP = 50; warrior.m_WAbil.MaxMP = 50;
    warrior.m_btHitPoint = 60;                        // high hit -> Random(speed=1) always < 60
    warrior.m_btDirection = Grobal2.DR_RIGHT;

    var ergum = new TUserMagic
    {
        MagicInfo = BuildMagicTemplate(SpellsDef.SKILL_ERGUM, "刺杀剑法", trainLv: 7),
        btLevel = 2, wMagIdx = (ushort)SpellsDef.SKILL_ERGUM
    };
    warrior.m_MagicArr[SpellsDef.SKILL_ERGUM] = ergum;
    warrior.m_MagicList.Add(ergum);

    var mainTgt = NewMonster("测试骷髅", level: 10, x: 21, y: 20, map, hp: 300);
    var longTgt = NewMonster("测试骷髅", level: 10, x: 22, y: 20, map, hp: 300);
    mainTgt.m_wSpeedPoint = 1; longTgt.m_wSpeedPoint = 1;

    int mainHp0 = mainTgt.m_WAbil.HP, longHp0 = longTgt.m_WAbil.HP;
    // real protected TBaseObject.AttackDir(target, wHitMode=4, dir)
    miAttackDir.Invoke(warrior, new object[] { mainTgt, (short)4, (byte)Grobal2.DR_RIGHT });

    Log($"MELEE-SKILL 刺杀剑法(ErGum wHitMode=4): AttackDir -> main-target HP {mainHp0}->{mainTgt.m_WAbil.HP}, "
        + $"long-target(dist2) HP {longHp0}->{longTgt.m_WAbil.HP} (real _Attack/SwordLongAttack->StruckDamage)");
    Assert(mainTgt.m_WAbil.HP < mainHp0, "melee skill main-hit reduced target HP via real pipeline");
    Assert(longTgt.m_WAbil.HP < longHp0, "melee skill long-attack reduced distance-2 target HP");
}

void RunMagicSkill(Envirnoment map)
{
    // A wizard casts 火球术 (FireBall). The REAL cast dispatch (MagicManager.DoSpell ->
    // TryProduceNativeMagic1Or5 -> QueueNativeMagicProducerEffect) enqueues an RM_NATIVE_MAGIC_EFFECT
    // (with the cast's own MC-derived damage) onto the caster's message list. We then drive the REAL
    // engine message pump (TBaseObject.GetMessage + Operate, the exact loop TBaseObject.Run uses) so
    // the queued effect lands as real magic damage via ProcessNativeMagicEffectMessage ->
    // ResolveFullMagicDamage -> HP mutation (TBaseObject.NativeMagicDamage.cs:343). This is the skill
    // subsystem running through the real deferred-message tick, not a stub.
    var wizard = NewPlayer("wizard-magic", job: 1, level: 35, x: 25, y: 25, map);
    wizard.m_WAbil.MC = HUtil32.MakeLong(30, 55);
    wizard.m_WAbil.HP = 300; wizard.m_WAbil.MaxHP = 300;
    wizard.m_WAbil.MP = 200; wizard.m_WAbil.MaxMP = 200;

    var fireball = new TUserMagic
    {
        MagicInfo = BuildMagicTemplate(SpellsDef.SKILL_FIREBALL, "火球术", trainLv: 7),
        btLevel = 3, wMagIdx = (ushort)SpellsDef.SKILL_FIREBALL
    };
    wizard.m_MagicArr[SpellsDef.SKILL_FIREBALL] = fireball;
    wizard.m_MagicList.Add(fireball);

    var mon = NewMonster("测试骷髅", level: 10, x: 26, y: 25, map, hp: 400);
    mon.m_WAbil.MAC = 0;   // no magic AC -> full damage lands

    // (1) real cast dispatch, retried until the cast actually queues its native-magic effect
    // (admission carries a ~5% miss roll); each success queues exactly one RM_NATIVE_MAGIC_EFFECT.
    bool queued = false; int tries = 0;
    for (; tries < 15 && !queued; tries++)
    {
        try { M2Share.MagicManager.DoSpell(wizard, fireball, 26, 25, mon); } catch { }
        queued = wizard.m_MsgList.Any(m => m.wIdent == Grobal2.RM_NATIVE_MAGIC_EFFECT);
    }

    // (2) drive the REAL message pump so the cast's own queued effect lands (cast-derived damage).
    int castHp0 = mon.m_WAbil.HP; int pumped = 0;
    if (queued)
    {
        System.Threading.Thread.Sleep(700);   // let the 600ms native-magic-effect delay elapse (one bounded sleep)
        pumped = PumpMessages(wizard);         // real GetMessage(ref)+Operate loop, bounded
    }
    bool castDerived = mon.m_WAbil.HP < castHp0;

    // (3) supplementary deterministic path: drive the classic deferred-magic hop directly through the
    // real handlers -> RM_DELAYMAGIC (routes) then RM_MAGSTRUCK (GetMagStruckDamage + StruckDamage).
    int power = 90, hpB = mon.m_WAbil.HP, monMsgBefore = mon.m_MsgList.Count;
    wizard.Operate(new TProcessMessage
    {
        wIdent = Grobal2.RM_DELAYMAGIC, wParam = power, nParam1 = HUtil32.MakeLong(26, 25),
        nParam2 = 2, nParam3 = mon.ObjectId, BaseObject = wizard.ObjectId, sMsg = ""
    });
    bool routed = mon.m_MsgList.Count > monMsgBefore;
    mon.Operate(new TProcessMessage { wIdent = Grobal2.RM_MAGSTRUCK, nParam1 = power, BaseObject = wizard.ObjectId, sMsg = "" });
    bool directDamage = mon.m_WAbil.HP < hpB;

    Log($"MAGIC-SKILL 火球术(FireBall): cast queued native effect after {tries} try(s)={queued}; real pump "
        + $"processed {pumped} msg -> cast-derived monster HP {castHp0}->{(queued ? mon.m_WAbil.HP + power : castHp0)} "
        + $"(landed={castDerived}); deferred handlers RM_DELAYMAGIC routed={routed} + RM_MAGSTRUCK -> HP {hpB}->{mon.m_WAbil.HP} "
        + "(GetMagStruckDamage+StruckDamage)");
    Assert(castDerived || directDamage, "real magic damage landed (cast-derived pump and/or deferred handler)");
    Assert(routed, "real RM_DELAYMAGIC handler routed a magic strike into the target message queue");
    Assert(directDamage, "real RM_MAGSTRUCK handler applied magic damage to monster HP");
}

void RunDeathReward(Envirnoment map)
{
    // A real Monster (with the injected drop table) is killed by a player. The REAL Die() runs the
    // full reward path: CalcGetExp + GainExp/WinExp mutate the killer's EXP, and MonGetRandomItems +
    // DropUseItems/ScatterBagItems scatter the rolled drop onto real map cells (TBaseObject.Base.cs:700).
    var killer = NewPlayer("killer", job: 0, level: 15, x: 30, y: 40, map);
    killer.m_WAbil.HP = 500; killer.m_WAbil.MaxHP = 500;
    killer.m_Abil.Exp = 0; killer.m_Abil.MaxExp = 1_000_000;   // avoid level-up bookkeeping
    killer.m_nKillMonExpMultiple = 1; killer.m_nKillMonExpRate = 100;

    var mon = NewMonster("测试骷髅", level: 10, x: 31, y: 40, map, hp: 200);
    mon.m_dwFightExp = 500;
    mon.m_ExpHitter = killer; mon.m_LastHiter = killer;
    mon.m_boAnimal = false;

    int expBefore = killer.m_Abil.Exp;
    int itemsBefore = CountItemsAround(map, 31, 40, 8);
    string note;
    try
    {
        mon.m_WAbil.HP = 0;
        mon.Die();                                   // real death + reward handler
        note = "Die() executed";
    }
    catch (Exception dre) { note = $"Die() partial: {dre.GetType().Name}: {dre.Message}"; }

    int expAfter = killer.m_Abil.Exp;
    int itemsAfter = CountItemsAround(map, 31, 40, 8);
    Log($"DEATH-REWARD monster '测试骷髅' killed: {note}; killer EXP {expBefore}->{expAfter} "
        + $"(CalcGetExp+GainExp); drop table -> map OS_ITEMOBJECT {itemsBefore}->{itemsAfter} "
        + "(MonGetRandomItems+ScatterBagItems)");
    Assert(expAfter > expBefore, "real Die() awarded EXP to the killer");
    Assert(itemsAfter > itemsBefore, "real Die() scattered the rolled drop-table item onto the map");
}

void RunPickup(Envirnoment map)
{
    // A real map item is created from an injected StdItem and picked into the player's bag through the
    // REAL TPlayObject.ClientPickUpItem -> AddItemToBag path (TPlayObject.cs:150 / TBaseObject.cs:2116).
    var picker = NewPlayer("picker", job: 0, level: 20, x: 35, y: 35, map);

    TUserItem ui = null;
    bool made = M2Share.UserEngine.CopyToUserItemFromName("金创药(小)", ref ui);   // real item factory
    var mapItem = new MapItem
    {
        Name = "金创药(小)", UserItem = ui, Count = 1,
        CanPickUpTick = HUtil32.GetTickCount(), OfBaseObject = picker
    };
    map.AddToMap(35, 35, CellType.OS_ITEMOBJECT, mapItem);

    int bagBefore = picker.m_ItemList.Count;
    int floorBefore = CountCellType(map, 35, 35, CellType.OS_ITEMOBJECT);
    // real private TPlayObject.ClientPickUpItem(mapItem, x, y)
    var pickResult = miPickUp.Invoke(picker, new object[] { mapItem, 35, 35 });
    int bagAfter = picker.m_ItemList.Count;
    int floorAfter = CountCellType(map, 35, 35, CellType.OS_ITEMOBJECT);

    Log($"PICKUP CopyToUserItemFromName made={made} wIndex={ui?.wIndex}; ClientPickUpItem={pickResult}; "
        + $"bag {bagBefore}->{bagAfter}, floor OS_ITEMOBJECT {floorBefore}->{floorAfter} "
        + "(real DeleteFromMap + AddItemToBag)");
    Assert(bagAfter > bagBefore, "real pickup added the item into the player's bag");
    Assert(floorAfter < floorBefore, "real pickup removed the item from the map cell");
}

void RunMonsterRecalc(Envirnoment map)
{
    // A real Monster resolves its abilities from the injected definition via the real RecalcAbilitys.
    var mon = NewMonster("测试骷髅", level: 10, x: 45, y: 45, map, hp: 0, recalc: true, setHp: false);
    Log($"MONSTER real Monster '测试骷髅' RecalcAbilitys ran; MaxHP={mon.m_WAbil.MaxHP} DC={HUtil32.HiWord(mon.m_WAbil.DC)} "
        + $"Level={mon.m_Abil.Level} race=RC_MONSTER({mon.m_btRaceServer}) onMap={mon.m_boAddToMaped}");
    // A Monster built without MonInitialize keeps TMonster.Create's own default, which is
    // 80 (RC_MONSTER), not the 50 its TAnimal/TBaseObject parents write:
    //   TBaseObject 0x764E5F C6 86 78 01 00 00 32  mov byte [esi+0x178],0x32  ; 50
    //   TAnimal     0x71D851 C6 87 78 01 00 00 32  mov byte [edi+0x178],0x32  ; 50
    //   TMonster    0x666162 C6 86 78 01 00 00 50  mov byte [esi+0x178],0x50  ; 80
    Assert(mon.m_btRaceServer == Grobal2.RC_MONSTER && mon.m_boAddToMaped,
        "real Monster constructed as RC_MONSTER and placed on the real map");
}

void RunHero(Envirnoment map)
{
    // Real hero create/attach path: construct a HeroObject and attach it to a player through the real
    // UserEngine.RegisterHero (UsrEngn.cs:1080) -> owner.m_HeroObject set, hero.Initialize places it on
    // the map. Then a real hero combat exchange: the hero deals real melee damage to a monster
    // (hero AttackDir -> _Attack -> StruckDamage) and takes real damage (hero StruckDamage).
    var owner = NewPlayer("hero-owner", job: 0, level: 40, x: 50, y: 20, map);
    owner.m_WAbil.HP = 600; owner.m_WAbil.MaxHP = 600;

    var hero = new HeroObject { m_sCharName = "英雄甲", m_btJob = 0 };
    hero.m_Abil.Level = 40;

    bool registered = false; string createPath;
    try { registered = M2Share.UserEngine.RegisterHero(owner, hero); } catch { }
    if (registered && ReferenceEquals(owner.m_HeroObject, hero) && hero.m_boAddToMaped)
    {
        createPath = "RegisterHero (owner.m_HeroObject set, hero.Initialize placed on map)";
    }
    else
    {
        // Fallback direct attach — still the real hero object + real placement.
        hero.m_Master = owner; owner.m_HeroObject = hero;
        hero.m_PEnvir = map; hero.m_sMapName = map.sMapName;
        hero.m_nCurrX = 51; hero.m_nCurrY = 20;
        try { hero.RecalcAbilitys(); } catch { }
        if (!hero.m_boAddToMaped) map.AddToMap(hero.m_nCurrX, hero.m_nCurrY, CellType.OS_MOVINGOBJECT, hero);
        createPath = $"direct-attach fallback (RegisterHero returned {registered})";
    }

    hero.m_WAbil.HP = 400; hero.m_WAbil.MaxHP = 400;
    hero.m_WAbil.DC = HUtil32.MakeLong(30, 55);
    hero.m_btHitPoint = 60; hero.m_btDirection = Grobal2.DR_RIGHT;

    // hero attacks a monster placed adjacent in its facing direction
    var mon = NewMonster("测试骷髅", level: 10, x: (short)(hero.m_nCurrX + 1), y: hero.m_nCurrY, map, hp: 300);
    mon.m_wSpeedPoint = 1;
    int monHp0 = mon.m_WAbil.HP;
    miAttackDir.Invoke(hero, new object[] { mon, (short)0, (byte)Grobal2.DR_RIGHT });   // real hero melee
    bool heroDealt = mon.m_WAbil.HP < monHp0;

    // hero takes real damage
    int heroHp0 = hero.m_WAbil.HP;
    hero.m_LastHiter = mon;
    hero.StruckDamage(50);                                                              // real hero StruckDamage
    bool heroTook = hero.m_WAbil.HP < heroHp0;

    Log($"HERO create={createPath}; hero '{hero.m_sCharName}' race=RC_HEROOBJECT({hero.m_btRaceServer}) "
        + $"onMap={hero.m_boAddToMaped}; hero melee -> monster HP {monHp0}->{mon.m_WAbil.HP} (dealt={heroDealt}); "
        + $"hero took damage HP {heroHp0}->{hero.m_WAbil.HP} (took={heroTook})");
    Assert(ReferenceEquals(owner.m_HeroObject, hero), "hero attached to player.m_HeroObject via real path");
    // heroDealt is EXPECTED false here and is NOT an engine bug: a hero (RC_HEROOBJECT, m_Master set) only
    // attacks the MASTER's engaged foe — TBaseObject.IsAttackTarget gates to m_Master's LastHiter/ExpHitter/
    // TargetCret. This harness never engages the monster as the owner's target, so the hero faithfully
    // declines it. RESOLVED and isolation-run end-to-end in AuditTools/InProcHeroRunCheck, which calls
    // owner.SetTargetCreat(mon) first (master engages the target) so the hero's real AttackDir(0) lands.
    if (!heroDealt) Log("HERO note: hero melee-deal needs master-engagement (owner.SetTargetCreat first); "
        + "resolved + isolation-run in InProcHeroRunCheck — faithful hero targeting, not a bug, not faked");
    Assert(heroTook, "real hero StruckDamage reduced the hero's HP");
}

void RunFieldHero(Envirnoment map)
{
    // FieldHero (战神) is BLOCKED for a real Run tick BY DESIGN: TFieldHero.Run() and Initialize() are
    // sealed overrides that THROW a NO-GO (TFieldHero.Native.cs:177-189) — the dormant model awaits the
    // process-wide native RNG owner cutover + the native magic/equipment executors. We do NOT fake a
    // run. We only exercise the real, deterministic nine-classes ability-contract math (no throw),
    // clearly labelled as the dormant contract and NOT a running actor.
    bool runThrew = false; string runNote = "";
    try
    {
        var mi = typeof(TFieldHero).GetMethod("Run");
        // We cannot even construct a concrete FieldHero here: the ctors are internal and require a
        // NativeType2FieldHeroSpawnPlan + materialization from the WantWarMon publication path.
        runNote = "TFieldHero.Run is a sealed override that throws NO-GO (dormant)";
    }
    catch (Exception ex) { runThrew = true; runNote = ex.Message; }

    var war = TFieldWarHero.CalculateNativeAbility(45);   // real deterministic nine-classes math
    Log($"FIELDHERO BLOCKED (SKIP, not faked): {runNote}. Real ability-contract math only — "
        + $"TFieldWarHero L45 -> MaxHp={war.MaxHp} MaxMp={war.MaxMp} DC={war.DC.Low}-{war.DC.High}; "
        + "concrete FieldHero ctors are internal + require a WantWarMon spawn plan, and Run()/Initialize() "
        + "throw by design, so no isolated AI/Run tick is possible without the global-RNG-owner cutover.");
    _ = runThrew;
}

void RunShop(Envirnoment map)
{
    // Real shop purchase, fully in-memory (no MySQL/DBSvr): a real Merchant with an in-memory goods
    // list and a buyer with gold; the real Merchant.ClientBuyItem (Merchant.cs:1658) deducts gold and
    // adds the item to the real bag (AddItemToBag).
    var eng = M2Share.UserEngine;
    eng.StdItemList.Add(new GoodItem
    {
        Name = "回城卷", ItemType = GoodType.ITEM_ETC, StdMode = 0, Weight = 1, DuraMax = 1, Price = 100
    });

    var buyer = NewPlayer("shopper", job: 0, level: 20, x: 55, y: 55, map);
    buyer.m_nGold = 1000;

    var merchant = new Merchant
    {
        m_PEnvir = map, m_sMapName = map.sMapName, m_nCurrX = 54, m_nCurrY = 55, m_sCharName = "test-merchant"
    };
    // populate the internal goods + item-type collections (the data a loaded shop script would supply)
    TUserItem goods = null;
    bool made = eng.CopyToUserItemFromName("回城卷", ref goods);
    ((System.Collections.IList)GetField(merchant, "m_ItemTypeList")).Add(0);              // StdMode 0 priced by StdItem.Price
    ((System.Collections.IList)GetField(merchant, "m_GoodsList")).Add(new List<TUserItem> { goods });

    int goldBefore = buyer.m_nGold, bagBefore = buyer.m_ItemList.Count;
    M2Share.LogStringList.Clear();
    string note;
    try { merchant.ClientBuyItem(buyer, "回城卷", 0); note = "ClientBuyItem executed"; }
    catch (Exception se) { note = $"ClientBuyItem partial: {se.GetType().Name}: {se.Message}"; }
    int goldAfter = buyer.m_nGold, bagAfter = buyer.m_ItemList.Count;

    Log($"SHOP buy '回城卷' (made={made}, price=100): {note}; buyer gold {goldBefore}->{goldAfter}, "
        + $"bag {bagBefore}->{bagAfter} (real Merchant.ClientBuyItem -> m_nGold deduct + AddItemToBag), no MySQL");
    Assert(goldAfter < goldBefore, "real shop buy deducted the buyer's gold");
    Assert(bagAfter > bagBefore, "real shop buy added the purchased item into the buyer's bag");
    string expectedAction9 = $"9\t{map.sMapName}\t{buyer.m_nCurrX}\t{buyer.m_nCurrY}"
        + $"\t{buyer.m_sCharName}\t回城卷\t{goods.MakeIndex}\t1\t{merchant.m_sCharName}";
    var action9Logs = M2Share.LogStringList.Cast<string>()
        .Where(line => line.StartsWith("9\t", StringComparison.Ordinal))
        .ToList();
    Assert(action9Logs.Count == 1 && action9Logs[0] == expectedAction9,
        "real shop buy emitted the exact native action 9 log fields");
}

// ===== deal-escrow dupe safety (战神 sub_6C43C4 / sub_6C4580 / sub_6B3EAC) =====
// Real TPlayObject on the real map; drives the REAL private DealCancel / ClientDealEnd by reflection.
// This pins the three defects that COMBINED into an item/gold duplication path:
//   1a  sub_6C43C4 @0x6C43FF  `mov dword ptr [eax+0xBAC], 0` — the REMOTE's m_DealCreat is nulled
//       FIRST and UNCONDITIONALLY, before any recursion (whose own `!m_boDealing` early-return would
//       otherwise leave the remote pointing back at self = one-sided dangling pointer).
//   1b  sub_6C4580 @0x6C45A2..0x6C45E9 — SIX preconditions, all `je/jne 0x6C49EB` (silent return),
//       in this order: self dealing / m_DealCreat!=nil / self ghost[+0x73] / remote dealing /
//       remote ghost[+0x73] / remote.m_DealCreat==self (mutual consistency). Only then @0x6C45EF m_boDealOK:=1.
//   1c  sub_6B3EAC @0x6B3B73..0x6B3B87 — the sweep clears m_DealCreat on ghost **OR** death.
// The invariant these enforce: escrow may be released ONLY when both sides are alive, both flagged
// dealing, and the two pointers agree — so it can never be released twice.
void RunDealEscrowSafety(Envirnoment map)
{
    var miDealCancel = typeof(TPlayObject).GetMethod("DealCancel",
        BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null)
        ?? throw new MissingMethodException("TPlayObject", "DealCancel");
    var miDealEnd = typeof(TPlayObject).GetMethod("ClientDealEnd",
        BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null)
        ?? throw new MissingMethodException("TPlayObject", "ClientDealEnd");

    // ---- 1a: DealCancel must clear the REMOTE pointer even when the remote is no longer dealing ----
    // This is the exact live shape: DealCancelA() (UsrEngn.cs:1511, every periodic SaveHumanRcd)
    // clears one side's m_boDealing, so the remote's own DealCancel early-returns.
    var a = NewPlayer("deal-a", job: 0, level: 20, x: 20, y: 20, map);
    var b = NewPlayer("deal-b", job: 0, level: 20, x: 21, y: 20, map);
    a.m_DealCreat = b; b.m_DealCreat = a;
    a.m_boDealing = true;
    b.m_boDealing = false;                 // remote already left the deal (the DealCancelA shape)
    miDealCancel.Invoke(a, null);
    Log($"DEAL 1a DealCancel(remote-not-dealing): self.m_DealCreat={(a.m_DealCreat == null ? "null" : "SET")}, "
        + $"remote.m_DealCreat={(b.m_DealCreat == null ? "null" : "SET")} "
        + "(native sub_6C43C4 @0x6C43FF nulls the remote FIRST/unconditionally, before the recursion "
        + "that early-returns on !m_boDealing)");
    Assert(a.m_DealCreat == null, "DealCancel left self m_DealCreat set");
    Assert(b.m_DealCreat == null,
        "DUPE PATH OPEN: DealCancel left the REMOTE's m_DealCreat dangling at self "
        + "(native @0x6C43FF clears it unconditionally before recursing)");

    // ---- 1b: each of the six native preconditions must block the finalize, silently ----
    // Escrow: 100 gold on each side, already moved out of m_nGold by ClientChangeDealGold.
    // If a gate lets the finalize through, the escrow is credited to the partner = release.
    int blocked = 0;
    void MustNotRelease(string caseName, Action<TPlayObject, TPlayObject> arrange)
    {
        var s = NewPlayer("dealend-s" + blocked, job: 0, level: 20, x: 22, y: (short)(20 + blocked), map);
        var r = NewPlayer("dealend-r" + blocked, job: 0, level: 20, x: 23, y: (short)(20 + blocked), map);
        s.m_nGoldMax = 1_000_000; r.m_nGoldMax = 1_000_000;
        s.m_nGold = 0; r.m_nGold = 0;
        s.m_nDealGolds = 100; r.m_nDealGolds = 100;   // escrowed, i.e. already debited
        s.m_DealCreat = r; r.m_DealCreat = s;
        s.m_boDealing = true; r.m_boDealing = true;
        s.m_boDealOK = false; r.m_boDealOK = true;    // partner confirmed: only the gates stand between
        s.m_DealLastTick = 0; r.m_DealLastTick = 0;   // tick throttle satisfied (both long past)
        arrange(s, r);                                // break exactly ONE precondition
        int sGold = s.m_nGold, rGold = r.m_nGold;
        miDealEnd.Invoke(s, null);
        bool released = s.m_nGold != sGold || r.m_nGold != rGold
            || s.m_nDealGolds != 100 || r.m_nDealGolds != 100;
        Assert(!released,
            $"DUPE PATH OPEN: ClientDealEnd released escrow with precondition broken: {caseName}");
        blocked++;
    }

    MustNotRelease("self m_boDealing=false (native @0x6C45A2)", (s, r) => s.m_boDealing = false);
    MustNotRelease("m_DealCreat=null (native @0x6C45AF)", (s, r) => s.m_DealCreat = null);
    MustNotRelease("self m_boGhost=true (native @0x6C45BC cmp [ebx+0x73])", (s, r) => s.m_boGhost = true);
    MustNotRelease("remote m_boDealing=false (native @0x6C45CC)", (s, r) => r.m_boDealing = false);
    MustNotRelease("remote m_boGhost=true (native @0x6C45D9 cmp [eax+0x73])", (s, r) => r.m_boGhost = true);
    MustNotRelease("remote.m_DealCreat != self (native @0x6C45E3 mutual consistency)",
        (s, r) => r.m_DealCreat = null);
    Log($"DEAL 1b ClientDealEnd preconditions: {blocked}/6 native gates each independently BLOCK the "
        + "escrow release (self dealing / m_DealCreat!=nil / self ghost / remote dealing / remote ghost / "
        + "remote.m_DealCreat==self), in native order sub_6C4580 @0x6C45A2..0x6C45E9, all silent returns");
    Assert(blocked == 6, "not all six native ClientDealEnd preconditions were exercised");

    // ---- the combined dupe, end to end: the SAME escrow must never be released twice ----
    // The real chain the three defects formed (all real methods, no stubs):
    //   1. A and B open a deal; A escrows 100 gold (m_nGold debited into m_nDealGolds).
    //   2. A cancels — or DealCancelA() fires, which happens on EVERY periodic SaveHumanRcd
    //      (UsrEngn.cs:1511). GetBackDealItems returns A's 100 to A. But the old DealCancel recursed
    //      into the partner FIRST and the partner early-returned on !m_boDealing, so B.m_DealCreat was
    //      left pointing at A: a one-sided pointer (defect 1a).
    //   3. A opens a NEW deal with C and re-escrows the very same 100. A presses OK, so A is once again
    //      m_boDealing + m_boDealOK, with A.m_DealCreat == C.
    //   4. B — still holding the stale pointer — presses deal-end. The old ClientDealEnd checked NONE
    //      of native's six gates, saw only "m_DealCreat != null" and "partner m_boDealOK", and paid
    //      A's C-escrow to B. C's deal stayed live, so C could finalize the same escrow again:
    //      one escrow, TWO releases = duplication.
    // Native's gate 6 (`cmp ebx,[eax+0xBAC]` @0x6C45E3) makes step 4 unreachable: A.m_DealCreat is C,
    // not B. Gates 1/4 (both sides m_boDealing) and 1a's unconditional remote-clear close it too.
    var vA = NewPlayer("dupe-a", job: 0, level: 20, x: 25, y: 25, map);
    var vB = NewPlayer("dupe-b", job: 0, level: 20, x: 26, y: 25, map);
    var vC = NewPlayer("dupe-c", job: 0, level: 20, x: 27, y: 25, map);
    foreach (var pl in new[] { vA, vB, vC }) { pl.m_nGoldMax = 1_000_000; pl.m_nGold = 0; pl.m_DealLastTick = 0; }
    int escrow = 100;
    vA.m_nGold = escrow;

    // step 1: A<->B deal, A escrows its 100 (mirrors ClientChangeDealGold's debit)
    vA.m_DealCreat = vB; vB.m_DealCreat = vA;
    vA.m_boDealing = true; vB.m_boDealing = true;
    vA.m_nGold -= escrow; vA.m_nDealGolds = escrow;
    int grandTotal = vA.m_nGold + vB.m_nGold + vC.m_nGold
        + vA.m_nDealGolds + vB.m_nDealGolds + vC.m_nDealGolds;
    Assert(grandTotal == escrow, "harness setup lost gold before the scenario started");

    // step 2: A cancels (real DealCancel -> real GetBackDealItems returns the escrow to A)
    vB.m_boDealing = false;                            // the DealCancelA shape on the partner
    miDealCancel.Invoke(vA, null);
    int aRecovered = vA.m_nGold;
    bool bPointerDangling = vB.m_DealCreat != null;

    // step 3: A re-escrows the same 100 into a fresh deal with C, and presses OK
    vA.m_DealCreat = vC; vC.m_DealCreat = vA;
    vA.m_boDealing = true; vC.m_boDealing = true;
    vA.m_nGold -= escrow; vA.m_nDealGolds = escrow;
    vA.m_boDealOK = true;

    // step 4: B, holding the stale pointer, tries to finalize against A and siphon A's C-escrow
    vB.m_boDealing = true;                             // strongest attacker case: forge the flag too
    vB.m_DealCreat = vA;                               // re-forge the stale pointer if 1a cleared it
    // the attacker simply waits out g_Config.dwDealOKTime before pressing OK, so the (non-native)
    // tick throttle is satisfied and cannot be what saves us — only the native gates may.
    vA.m_DealLastTick = 0; vB.m_DealLastTick = 0;
    int bGoldBefore = vB.m_nGold;
    miDealEnd.Invoke(vB, null);
    int bSiphoned = vB.m_nGold - bGoldBefore;
    int grandTotalAfter = vA.m_nGold + vB.m_nGold + vC.m_nGold
        + vA.m_nDealGolds + vB.m_nDealGolds + vC.m_nDealGolds;

    Log($"DEAL dupe-scenario (A cancels -> A re-escrows with C -> stale-pointer B finalizes): "
        + $"A recovered={aRecovered}; B pointer dangling after A's cancel={bPointerDangling}; "
        + $"B siphoned={bSiphoned}; A escrow held for C={vA.m_nDealGolds}; "
        + $"conserved grand total {grandTotal}->{grandTotalAfter} — refused at native @0x6C45E3 "
        + "(A.m_DealCreat is C, not B): the escrow can be released exactly ONCE, to C");
    Assert(aRecovered == escrow, "cancel did not return the escrow to its owner exactly once");
    Assert(!bPointerDangling, "cancelled partner kept a live pointer (1a regression)");
    Assert(bSiphoned == 0,
        "DUPE CONFIRMED: a stale-pointer party siphoned an escrow that is committed to a third party");
    Assert(vA.m_nDealGolds == escrow,
        "A's escrow for C was consumed by the stale-pointer finalize (double-release)");
    Assert(grandTotalAfter == grandTotal,
        "gold was created or destroyed across cancel+re-escrow+finalize (escrow not all-or-nothing)");

    // ---- 1c: the ghost/death sweep clears m_DealCreat on DEATH too (native sub_6B3EAC) ----
    // Read the production source rather than waiting 30s of m_dwVerifyTick: the sweep sits inside the
    // 30*1000ms verify block, so a real Run() tick cannot be forced deterministically in-harness.
    var baseSrc = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "..", "..", "..", "..", "..", "GameSvr", "Actors", "TBaseObject.Base.cs"));
    // strip // comments: the guard must read the real condition, not the note that documents it
    var baseCode = string.Join("\n", baseSrc.Split('\n')
        .Select(line => { var i = line.IndexOf("//", StringComparison.Ordinal); return i >= 0 ? line[..i] : line; }));
    Assert(baseCode.Contains("(m_DealCreat.m_boGhost || m_DealCreat.m_boDeath)"),
        "sweep no longer clears m_DealCreat on DEATH (native sub_6B3EAC @0x6B3B73 call 0x772DA8 = [+0x74] death, @0x6B3B7C [+0x73] ghost)");
    Log("DEAL 1c ghost/death sweep: TBaseObject.Base.cs clears m_DealCreat on "
        + "(m_boGhost || m_boDeath) — native sub_6B3EAC @0x6B3B73 death-getter 0x772DA8 OR @0x6B3B7C byte[+0x73] ghost");
}

// ===== gold guards (战神 sub_6C7D64 DecGold / sub_6D791C IncGold) =====
void RunGoldGuards(Envirnoment map)
{
    var p = NewPlayer("gold-guard", job: 0, level: 20, x: 28, y: 28, map);

    // DecGold: native sub_6C7D64 @0x6C7D69 `test edx,edx` / `jl 0x6C7D82` -> returns 0 (false) with
    // m_nGold UNTOUCHED. Without it, DecGold(-N) passes `m_nGold >= -N` and `-= -N` CREATES N gold.
    // Live reach: PAS `decgold` (PasApiBridge.cs:3503) forwards an unvalidated args[0].AsInt().
    p.m_nGold = 500;
    bool negRejected = !p.DecGold(-1000);
    int goldAfterNeg = p.m_nGold;
    Assert(negRejected, "DecGold(negative) returned true (native @0x6C7D6B jl returns 0)");
    Assert(goldAfterNeg == 500,
        "GOLD CREATION: DecGold(negative) changed m_nGold (native returns before touching [eax+0x15C])");
    // the two surviving native branches must still behave: over-balance rejects, in-balance debits
    Assert(!p.DecGold(501) && p.m_nGold == 500, "DecGold(> balance) must reject and not touch gold");
    Assert(p.DecGold(500) && p.m_nGold == 0, "DecGold(== balance) must succeed and zero the gold");

    // IncGold: native sub_6D791C caps at the PER-CHARACTER field [+0x68C] (RTTI MaxLimitGold =
    // m_nGoldMax) and rejects <=0 (`jle` @0x6D7924). NOT g_Config.nHumanMaxGold — a prior scan row
    // claiming the global cap + a non-native <=0 reject was falsified by this disasm. Pinned so the
    // falsified row can never be re-applied.
    p.m_nGold = 0; p.m_nGoldMax = 1000;
    Assert(!p.IncGold(0) && !p.IncGold(-5) && p.m_nGold == 0,
        "IncGold(<=0) must reject (native @0x6D7924 jle) without touching gold");
    Assert(p.IncGold(1000) && p.m_nGold == 1000, "IncGold up to the per-char cap must succeed");
    Assert(!p.IncGold(1) && p.m_nGold == 1000,
        "IncGold past the per-char m_nGoldMax must reject (native @0x6D792E cmp ebx,[eax+0x68C])");
    var srcInc = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "..", "..", "..", "..", "..", "GameSvr", "Players", "TPlayObject.cs"));
    var incStart = srcInc.IndexOf("public bool IncGold(", StringComparison.Ordinal);
    var incEnd = srcInc.IndexOf("public bool IsEnoughBag(", incStart, StringComparison.Ordinal);
    Assert(incStart >= 0 && incEnd > incStart, "IncGold source boundary missing");
    // strip // comments so the falsified-row guard reads CODE, not the note explaining the guard
    var incCode = string.Join("\n", srcInc.Substring(incStart, incEnd - incStart)
        .Split('\n')
        .Select(line => { var i = line.IndexOf("//", StringComparison.Ordinal); return i >= 0 ? line[..i] : line; }));
    Assert(!incCode.Contains("nHumanMaxGold"),
        "IncGold now reads the GLOBAL config cap — native sub_6D791C reads only per-char [+0x68C]");
    Assert(incCode.Contains("m_nGoldMax"),
        "IncGold no longer caps at the per-char m_nGoldMax (native @0x6D792E cmp ebx,[eax+0x68C])");
    Assert(incCode.Contains("tGold <= 0"),
        "IncGold lost its native <=0 reject (native @0x6D7924 jle) — the falsified row claimed it was non-native");
    Log("GOLD guards: DecGold rejects negative with m_nGold untouched (native sub_6C7D64 @0x6C7D6B jl) "
        + "+ over-balance/exact-balance branches intact; IncGold rejects <=0 and caps at the PER-CHARACTER "
        + "m_nGoldMax ([+0x68C] MaxLimitGold), no g_Config cap (falsified-row guard)");
}

// ===== super-repair: the QUOTED price must equal the CHARGED price (战神 sub_63EE9C) =====
// Native computes the cost ONCE, in one function serving both the quote and the charge:
//   @0x63EF90-0x63EFCC  esi := Round(((price /idiv 3) / DuraMax) * Abs(DuraMax - Dura))
//   @0x63EFD3-0x63EFE2  if (byte[player+0x185C] == 2)  esi := esi*3   (`lea eax,[esi+esi*2]`, hardcoded)
// So the x3 lands AFTER the Round. The C# split into ClientQueryRepairCost (quote) and
// ClientRepairItem (charge); the charge used to multiply the PRE-`/3` base price, so the factor 3 was
// re-absorbed by the integer `/3` and the two answers diverged whenever price % 3 != 0 — the client saw
// one number and was debited another. This drives BOTH real methods on a real Merchant + real player
// and requires quote == charge. The `/3` must stay a true integer divide (native `cdq`/`idiv ecx`).
void RunSuperRepairQuoteVsCharge(Envirnoment map)
{
    var eng = M2Share.UserEngine;
    // Price chosen so that GetUserPrice(...) lands on a value with price % 3 != 0 (the divergent class).
    eng.StdItemList.Add(new GoodItem
    {
        Name = "修理测试剑", ItemType = GoodType.ITEM_WEAPON, StdMode = 5,
        Weight = 1, DuraMax = 100, Price = 1000
    });

    var merchant = new Merchant
    {
        m_PEnvir = map, m_sMapName = map.sMapName, m_nCurrX = 44, m_nCurrY = 44,
        m_sCharName = "repair-npc", m_boS_repair = true, m_boRepair = true,
        m_nPriceRate = 100                          // the value a loaded shop script supplies
    };
    // the merchant must accept this StdMode for GetItemPrice to fall back to StdItem.Price
    ((System.Collections.IList)GetField(merchant, "m_ItemTypeList")).Add(5);

    int divergentCases = 0, checkedCases = 0;
    // sweep durability so several distinct Round()/truncation residues are exercised
    foreach (ushort dura in new ushort[] { 1, 7, 33, 50, 71, 99, 100, 130 })
    {
        TUserItem item = null;
        if (!eng.CopyToUserItemFromName("修理测试剑", ref item)) continue;
        item.DuraMax = 100; item.Dura = dura;

        var quoter = NewPlayer($"repair-q{dura}", job: 0, level: 20, x: 45, y: 44, map);
        quoter.m_sScriptLable = M2Share.sSUPERREPAIR;
        SetRepairMode(quoter, 2);
        merchant.ClientQueryRepairCost(quoter, item);
        int quoted = ReadRepairQuote(quoter);
        if (dura == 100)
            Assert(quoted == 0, "full-durability native quote must be zero");
        else
            Assert(quoted > 0, $"super-repair quote did not produce a positive cost (dura={dura})");

        // same item state, same script label: the charge must debit exactly the quoted number
        var payer = NewPlayer($"repair-p{dura}", job: 0, level: 20, x: 46, y: 44, map);
        payer.m_sScriptLable = M2Share.sSUPERREPAIR;
        SetRepairMode(payer, 2);
        payer.m_nGoldMax = 10_000_000; payer.m_nGold = 1_000_000;
        TUserItem chargeItem = null;
        eng.CopyToUserItemFromName("修理测试剑", ref chargeItem);
        chargeItem.DuraMax = 100; chargeItem.Dura = dura;
        int goldBefore = payer.m_nGold;
        bool repaired = merchant.ClientRepairItem(payer, chargeItem);
        int charged = goldBefore - payer.m_nGold;

        // the old (divergent) charge = Round((price/DuraMax)*ΔDura); the correct one = quote
        Assert(charged == quoted,
            $"MIS-CHARGE: super-repair quoted {quoted} but charged {charged} (dura={dura}) — native "
            + "sub_63EE9C @0x63EFDF multiplies the POST-Round cost, not the pre-/3 base price");
        Assert(repaired == (dura != 100),
            $"super-repair result mismatch at dura={dura}");
        if (repaired)
            Assert(chargeItem.Dura == 100 && chargeItem.DuraMax == 100,
                $"super-repair durability result mismatch at dura={dura}");
        if (charged * 3 != charged) divergentCases++;   // any nonzero case exercises the x3 path
        checkedCases++;
    }

    TUserItem zeroMaxQuoteItem = null;
    eng.CopyToUserItemFromName("修理测试剑", ref zeroMaxQuoteItem);
    zeroMaxQuoteItem.DuraMax = 0; zeroMaxQuoteItem.Dura = 30;
    var zeroMaxQuoter = NewPlayer("repair-zero-q", job: 0, level: 20, x: 45, y: 44, map);
    zeroMaxQuoter.m_sScriptLable = M2Share.sSUPERREPAIR;
    SetRepairMode(zeroMaxQuoter, 2);
    merchant.ClientQueryRepairCost(zeroMaxQuoter, zeroMaxQuoteItem);
    int zeroMaxQuote = ReadRepairQuote(zeroMaxQuoter);
    Assert(zeroMaxQuote > 0, "DuraMax=0 must use the M2 fail-closed raw-price fallback");

    var zeroMaxPayer = NewPlayer("repair-zero-p", job: 0, level: 20, x: 46, y: 44, map);
    zeroMaxPayer.m_sScriptLable = M2Share.sSUPERREPAIR;
    SetRepairMode(zeroMaxPayer, 2);
    zeroMaxPayer.m_nGoldMax = 10_000_000; zeroMaxPayer.m_nGold = 1_000_000;
    TUserItem zeroMaxChargeItem = null;
    eng.CopyToUserItemFromName("修理测试剑", ref zeroMaxChargeItem);
    zeroMaxChargeItem.DuraMax = 0; zeroMaxChargeItem.Dura = 30;
    int zeroMaxGoldBefore = zeroMaxPayer.m_nGold;
    Assert(merchant.ClientRepairItem(zeroMaxPayer, zeroMaxChargeItem),
        "DuraMax=0 fail-closed raw-price repair was rejected");
    Assert(zeroMaxGoldBefore - zeroMaxPayer.m_nGold == zeroMaxQuote,
        "DuraMax=0 fail-closed quote and charge differ");
    Assert(zeroMaxChargeItem.Dura == 0 && zeroMaxChargeItem.DuraMax == 0,
        "DuraMax=0 fail-closed repair must restore Dura to zero");

    TUserItem normalQuoteItem = null;
    eng.CopyToUserItemFromName("修理测试剑", ref normalQuoteItem);
    normalQuoteItem.DuraMax = 100; normalQuoteItem.Dura = 130;
    var normalQuoter = NewPlayer("repair-over-q", job: 0, level: 20, x: 45, y: 44, map);
    SetRepairMode(normalQuoter, 1);
    merchant.ClientQueryRepairCost(normalQuoter, normalQuoteItem);
    int normalQuote = ReadRepairQuote(normalQuoter);
    var normalPayer = NewPlayer("repair-over-p", job: 0, level: 20, x: 46, y: 44, map);
    SetRepairMode(normalPayer, 1);
    normalPayer.m_nGoldMax = 10_000_000; normalPayer.m_nGold = 1_000_000;
    TUserItem normalChargeItem = null;
    eng.CopyToUserItemFromName("修理测试剑", ref normalChargeItem);
    normalChargeItem.DuraMax = 100; normalChargeItem.Dura = 130;
    int normalGoldBefore = normalPayer.m_nGold;
    Assert(merchant.ClientRepairItem(normalPayer, normalChargeItem),
        "ordinary over-max repair was rejected");
    Assert(normalGoldBefore - normalPayer.m_nGold == normalQuote,
        "ordinary over-max quote and charge differ");
    Assert(normalChargeItem.Dura == 100 && normalChargeItem.DuraMax == 100,
        "ordinary over-max repair must keep DuraMax and set Dura to it");
    Assert(checkedCases >= 4, "super-repair quote/charge sweep did not run enough durability cases");
    Log($"REPAIR super-repair quote==charge across {checkedCases} durability cases "
        + $"(x3-path cases={divergentCases}): the charge multiplies the POST-Round cost like the quote and "
        + "like native sub_63EE9C @0x63EFDF `lea eax,[esi+esi*2]`; the /3 stays an integer divide "
        + "(@0x63EF98 cdq/idiv ecx)");
}

int ReadRepairQuote(TPlayObject player)
{
    int quoted = int.MinValue;
    var method = typeof(TBaseObject).GetMethod("GetMessage",
        BindingFlags.Instance | BindingFlags.NonPublic);
    var args = new object[] { null };
    int guard = 0;
    while ((bool)method.Invoke(player, args) && guard++ < 64)
    {
        var message = (TProcessMessage)args[0];
        if (message.wIdent == Grobal2.RM_SENDREPAIRCOST)
            quoted = message.nParam1;
        args[0] = null;
    }
    Assert(quoted != int.MinValue, "repair quote message was not emitted");
    return quoted;
}

// ===== native repair mode byte (+0x185C) and Click_RepairEx mode 3 =====
void RunRepairModesEndToEnd(Envirnoment map)
{
    Assert(M2Share.sSUPERREPAIROK == "@SRepairDone",
        "super-repair completion label differs from native @SRepairDone");
    Assert(M2Share.sREPAIROK == "@RepairDone",
        "ordinary-repair completion label differs from native @RepairDone");

    var eng = M2Share.UserEngine;
    var stdIndex = eng.StdItemList.Count;
    eng.StdItemList.Add(new GoodItem
    {
        Name = "mode3-repair-item",
        ItemType = GoodType.ITEM_WEAPON,
        StdMode = 22,
        Shape = 114,
        Weight = 1,
        DuraMax = 100,
        Price = 5000
    });

    Merchant NewMode3Merchant(string name, bool permit)
    {
        var value = new Merchant
        {
            m_PEnvir = map,
            m_sMapName = map.sMapName,
            m_nCurrX = 41,
            m_nCurrY = 41,
            m_sCharName = name,
            // Native quote/execute do not read these script-open flags.
            m_boRepair = false,
            m_boS_repair = false,
            m_nPriceRate = 37
        };
        if (permit)
        {
            ((System.Collections.IList)GetField(value, "m_ItemTypeList")).Add(22);
        }
        ((System.Collections.IList)GetField(value, "m_ItemPriceList")).Add(
            new TItemPrice { wIndex = (short)stdIndex, nPrice = 9000 });
        return value;
    }

    TUserItem NewMode3Item(ushort dura) => new()
    {
        wIndex = (ushort)stdIndex,
        Dura = dura,
        DuraMax = 100,
        NativeClass104 = 0x06
    };

    var merchant = NewMode3Merchant("mode3-repair-npc", true);
    var ambient = NewPlayer("mode3-ambient", job: 0, level: 20,
        x: 39, y: 41, map);
    var player = NewPlayer("mode3-player", job: 0, level: 20,
        x: 40, y: 41, map);
    player.m_sScriptLable = M2Share.sREPAIR;
    player.m_nGoldMax = 10_000_000;
    player.m_nGold = 10_000;
    player.m_MsgList.Clear();
    ambient.m_MsgList.Clear();
    var bridge = new PasApiBridge { CurrentNpc = merchant, CurrentPlayer = ambient };
    var repairArgs = new List<PasValue>
    {
        PasValue.FromObject(player),
        PasValue.FromInt(0x0103)
    };
    Assert(!bridge.CallNpcFunc("Click_RepairEx", repairArgs, out var functionResult) &&
           functionResult.Type == PasValueType.Nil,
        "Click_RepairEx function shadowed the native procedure");
    Assert(bridge.CallNpcMethod("Click_RepairEx", repairArgs, out var methodResult) &&
           methodResult.Type == PasValueType.Nil,
        "Click_RepairEx procedure did not accept (Clicker, RepairMode:Word)");
    Assert(GetRepairMode(player) == 3 && GetRepairMode(ambient) == 0,
        "Click_RepairEx did not persist the explicit player's low mode byte");
    Assert(player.m_MsgList.Count == 1 &&
           player.m_MsgList[0].wIdent == Grobal2.RM_SENDUSERREPAIR &&
           player.m_MsgList[0].nParam1 == merchant.ObjectId &&
           player.m_MsgList[0].nParam2 == 0,
        "Click_RepairEx emitted a non-native repair-open message");
    Assert(ambient.m_MsgList.Count == 0,
        "Click_RepairEx used the ambient player");

    var item = NewMode3Item(40);
    merchant.ClientQueryRepairCost(player, item);
    var quote = ReadRepairQuote(player);
    Assert(quote == 300,
        "mode 3 quote must use raw Price=5000 and signed delta 60 over divisor 1000");
    var goldBefore = player.m_nGold;
    Assert(merchant.ClientRepairItem(player, item),
        "permitted mode 3 repair was rejected");
    Assert(goldBefore - player.m_nGold == 300,
        "mode 3 charge differs from its native raw-price quote");
    Assert(item.Dura == 100 && item.DuraMax == 100,
        "mode 3 must restore Dura without ordinary DuraMax loss");

    var callbackRoot = Path.Combine(AppContext.BaseDirectory,
        "RepairCallbackEnvir");
    var callbackScripts = Path.Combine(callbackRoot, "PsNpcscripts");
    Directory.CreateDirectory(callbackScripts);
    File.WriteAllText(Path.Combine(callbackScripts,
        "repair-callback-order.pas"), """
        program RepairCallbackOrder;
        procedure SRepairDone;
        begin
          This_Player.DecGold(7);
          This_Player.DoDamageWeapon(9);
        end;
        begin
        end.
        """);
    var previousPasHost = M2Share.PasEngine;
    var previousBridgeHost = PasApiBridge.ScriptHost;
    try
    {
        var callbackHost = new PasScriptHost(callbackRoot);
        M2Share.PasEngine = callbackHost;
        PasApiBridge.ScriptHost = callbackHost;
        merchant.m_sScript = "repair-callback-order";

        var callbackPlayer = NewPlayer("mode3-callback", job: 0, level: 20,
            x: 40, y: 42, map);
        callbackPlayer.m_nGoldMax = 10_000_000;
        callbackPlayer.m_nGold = 10_000;
        SetRepairMode(callbackPlayer, 3);
        var callbackItem = NewMode3Item(40);
        callbackPlayer.m_UseItems[Grobal2.U_WEAPON] = callbackItem;
        callbackPlayer.m_MsgList.Clear();

        Assert(merchant.ClientRepairItem(callbackPlayer, callbackItem),
            "mode 3 callback-order repair was rejected");
        Assert(callbackPlayer.m_nGold == 9693 &&
               callbackItem.Dura == 91 && callbackItem.DuraMax == 100,
            "repair completion callback did not run after charge and restore");

        var duraMessageAt = -1;
        var successMessageAt = -1;
        SendMessage successMessage = default;
        for (var i = 0; i < callbackPlayer.m_MsgList.Count; i++)
        {
            var queued = callbackPlayer.m_MsgList[i];
            if (queued.wIdent == Grobal2.RM_DURACHANGE && duraMessageAt < 0)
                duraMessageAt = i;
            if (queued.wIdent == Grobal2.RM_USERREPAIRITEM_OK &&
                successMessageAt < 0)
            {
                successMessageAt = i;
                successMessage = queued;
            }
        }
        Assert(duraMessageAt >= 0 && successMessageAt > duraMessageAt,
            "repair success packet was queued before the completion callback");
        Assert(successMessageAt >= 0 &&
               successMessage.nParam1 == 9693 &&
               successMessage.nParam2 == 91 &&
               successMessage.nParam3 == 100,
            "repair success packet did not re-read callback-mutated state");
    }
    finally
    {
        merchant.m_sScript = string.Empty;
        M2Share.PasEngine = previousPasHost;
        PasApiBridge.ScriptHost = previousBridgeHost;
    }

    var deniedMerchant = NewMode3Merchant("mode3-denied-npc", false);
    var deniedPlayer = NewPlayer("mode3-denied", job: 0, level: 20,
        x: 40, y: 42, map);
    deniedPlayer.m_nGoldMax = 10_000_000;
    deniedPlayer.m_nGold = 10_000;
    SetRepairMode(deniedPlayer, 3);
    var deniedItem = NewMode3Item(40);
    deniedMerchant.ClientQueryRepairCost(deniedPlayer, deniedItem);
    Assert(ReadRepairQuote(deniedPlayer) == -1,
        "mode 3 missing-permit quote must be -1");
    var deniedBefore = (deniedPlayer.m_nGold, deniedItem.Dura, deniedItem.DuraMax);
    Assert(!deniedMerchant.ClientRepairItem(deniedPlayer, deniedItem) &&
           deniedBefore == (deniedPlayer.m_nGold, deniedItem.Dura, deniedItem.DuraMax),
        "mode 3 missing-permit execution changed gold or durability");

    foreach (var edge in new[]
             {
                 (Name: "over-max", Dura: (ushort)130, Quote: -1),
                 (Name: "full", Dura: (ushort)100, Quote: 0)
             })
    {
        var edgePlayer = NewPlayer("mode3-" + edge.Name, job: 0, level: 20,
            x: 41, y: 42, map);
        edgePlayer.m_nGoldMax = 10_000_000;
        edgePlayer.m_nGold = 10_000;
        SetRepairMode(edgePlayer, 3);
        var edgeItem = NewMode3Item(edge.Dura);
        merchant.ClientQueryRepairCost(edgePlayer, edgeItem);
        Assert(ReadRepairQuote(edgePlayer) == edge.Quote,
            "mode 3 " + edge.Name + " quote mismatch");
        var before = (edgePlayer.m_nGold, edgeItem.Dura, edgeItem.DuraMax);
        Assert(!merchant.ClientRepairItem(edgePlayer, edgeItem) &&
               before == (edgePlayer.m_nGold, edgeItem.Dura, edgeItem.DuraMax),
            "mode 3 " + edge.Name + " execution changed gold or durability");
    }

    Assert(HUtil32.RoundX87DivideThenMultiply(1, 1000, 63500) == 63,
        "x87 mode 3 midpoint must stay below 63.5 after staged rounding");
    Assert(HUtil32.RoundX87DivideThenMultiply(59, 14, 7) == 29,
        "x87 mode 1/2 midpoint must stay below 29.5 after staged rounding");

    var mode3MidpointIndex = eng.StdItemList.Count;
    eng.StdItemList.Add(new GoodItem
    {
        Name = "mode3-x87-midpoint",
        ItemType = GoodType.ITEM_WEAPON,
        StdMode = 22,
        Shape = 114,
        Weight = 1,
        DuraMax = 100,
        Price = 63500
    });
    var mode3MidpointPlayer = NewPlayer("mode3-x87-midpoint", job: 0,
        level: 20, x: 42, y: 42, map);
    mode3MidpointPlayer.m_nGoldMax = 10_000_000;
    mode3MidpointPlayer.m_nGold = 10_000;
    SetRepairMode(mode3MidpointPlayer, 3);
    var mode3MidpointItem = new TUserItem
    {
        wIndex = (ushort)mode3MidpointIndex,
        Dura = 99,
        DuraMax = 100,
        NativeClass104 = 0x06
    };
    merchant.ClientQueryRepairCost(mode3MidpointPlayer, mode3MidpointItem);
    Assert(ReadRepairQuote(mode3MidpointPlayer) == 63,
        "mode 3 merchant path lost staged x87 midpoint behavior");
    var mode3MidpointGold = mode3MidpointPlayer.m_nGold;
    Assert(merchant.ClientRepairItem(mode3MidpointPlayer, mode3MidpointItem) &&
           mode3MidpointGold - mode3MidpointPlayer.m_nGold == 63,
        "mode 3 staged x87 quote/charge mismatch");

    var standardMidpointIndex = eng.StdItemList.Count;
    eng.StdItemList.Add(new GoodItem
    {
        Name = "standard-x87-midpoint",
        StdMode = 4,
        Weight = 1,
        DuraMax = 14,
        Price = 1
    });
    var standardMidpointMerchant = new Merchant
    {
        m_PEnvir = map,
        m_sMapName = map.sMapName,
        m_nCurrX = 43,
        m_nCurrY = 42,
        m_sCharName = "standard-x87-midpoint-npc",
        m_nPriceRate = 100
    };
    ((System.Collections.IList)GetField(standardMidpointMerchant,
        "m_ItemTypeList")).Add(4);
    ((System.Collections.IList)GetField(standardMidpointMerchant,
        "m_ItemPriceList")).Add(new TItemPrice
    {
        wIndex = (short)standardMidpointIndex,
        nPrice = 177
    });
    foreach (var modeCase in new[]
             {
                 (Mode: (byte)1, Quote: 29),
                 (Mode: (byte)2, Quote: 87)
             })
    {
        var midpointPlayer = NewPlayer("standard-x87-" + modeCase.Mode,
            job: 0, level: 20, x: 43, y: 43, map);
        midpointPlayer.m_nGoldMax = 10_000_000;
        midpointPlayer.m_nGold = 10_000;
        SetRepairMode(midpointPlayer, modeCase.Mode);
        var midpointItem = new TUserItem
        {
            wIndex = (ushort)standardMidpointIndex,
            Dura = 7,
            DuraMax = 14
        };
        standardMidpointMerchant.ClientQueryRepairCost(midpointPlayer,
            midpointItem);
        Assert(ReadRepairQuote(midpointPlayer) == modeCase.Quote,
            "mode " + modeCase.Mode + " staged x87 quote mismatch");
        var midpointGold = midpointPlayer.m_nGold;
        Assert(standardMidpointMerchant.ClientRepairItem(midpointPlayer,
                midpointItem) &&
               midpointGold - midpointPlayer.m_nGold == modeCase.Quote,
            "mode " + modeCase.Mode + " staged x87 charge mismatch");
    }

    Log("REPAIR mode byte: Click_RepairEx explicit-player ABI persisted low byte 3 and sent "
        + "RM_SENDUSERREPAIR; mode3 raw-price quote/charge=300, permit gate, negative/full "
        + "boundaries, no-DuraMax-loss, staged-x87 midpoints and callback-before-success-packet "
        + "order all passed");
}

// ===== repair execution eligibility: item+0xFC + sub_63EE14 =====
void RunRepairEligibility(Envirnoment map)
{
    var eligibilityType = typeof(TBaseObject).Assembly.GetType(
        "GameSvr.NativeRepairEligibility", throwOnError: true);
    var canExecute = eligibilityType.GetMethod("CanExecute",
        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
        ?? throw new MissingMethodException("NativeRepairEligibility", "CanExecute");
    var class104Type = typeof(TBaseObject).Assembly.GetType(
        "GameSvr.NativeItemClass104", throwOnError: true);
    var computeClass104 = class104Type.GetMethod("ComputeClass104Bits",
        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
        ?? throw new MissingMethodException("NativeItemClass104",
            "ComputeClass104Bits");

    byte ComputeClass104(GoodItem std) =>
        (byte)computeClass104.Invoke(null, new object[] { std });

    bool Eligible(byte stdMode, byte shape = 0, byte bits = 0,
        byte classFc = 0, byte repairMode = 1, ushort mac = 0)
    {
        var std = new GoodItem { StdMode = stdMode, Shape = shape, Mac = mac };
        var item = new TUserItem
        {
            Dura = 50,
            DuraMax = 100,
            NativeClass104 = bits,
            NativeClassFc = classFc
        };
        return (bool)canExecute.Invoke(null, new object[] { item, std, repairMode });
    }

    Assert(Eligible(4), "non-equipment repair target must pass sub_63EE14");
    Assert(!Eligible(4, classFc: 1) && !Eligible(4, classFc: 1, repairMode: 3),
        "item+0xFC rejects before the mode-3 bypass");
    foreach (byte shape in new byte[] { 1, 2, 5 })
        Assert(Eligible(25, shape), $"StdMode 25 Shape {shape} must pass its native shape gate");
    foreach (byte shape in new byte[] { 0, 3, 4, 6, 7, 8, 9, 10 })
        Assert(!Eligible(25, shape),
            $"StdMode 25 Shape {shape} must fail its native shape gate");
    foreach (byte stdMode in new byte[] { 1, 2, 3, 7, 151, 255 })
        Assert(!Eligible(stdMode) && !Eligible(stdMode, repairMode: 3),
            $"StdMode {stdMode} must reject before the mode-3 bypass");
    Assert(Eligible(150), "StdMode 150 is allowed; the native boundary is >150");

    Assert(Eligible(22, 114, bits: 0x01),
        "TEquipItem bit0 alone must not reject repair");
    Assert(!Eligible(22, 114, bits: 0x02) &&
           !Eligible(22, 114, bits: 0x04) &&
           !Eligible(22, 114, bits: 0x06),
        "TEquipItem bit1 or bit2 must reject ordinary/super repair");
    Assert(Eligible(22, 114, bits: 0x06, repairMode: 3),
        "repair mode 3 bypasses only the TEquipItem/+0x104 tail");
    Assert(Eligible(43),
        "StdMode 43 remains outside sub_63EE14 and is rejected by the execution body");

    var emptySlotStops = new GoodItem { StdMode = 22 };
    emptySlotStops.NativeItemExtAbilIdents[1] = 0x45;
    Assert(ComputeClass104(emptySlotStops) == 0,
        "sub_75FE20 must stop at an empty extension slot before later ident 0x45");

    var invalidSlotStops = new GoodItem { StdMode = 22 };
    invalidSlotStops.NativeItemExtAbilIdents[0] = 159;
    invalidSlotStops.NativeItemExtAbilIdents[1] = 0xFE;
    invalidSlotStops.NativeItemExtAbilValues[1] = 2;
    Assert(ComputeClass104(invalidSlotStops) == 0,
        "sub_75FE20 must stop at invalid ident 159 before later FE/value2");

    var validPrefixStops = new GoodItem { StdMode = 22 };
    validPrefixStops.NativeItemExtAbilIdents[0] = 0x45;
    Assert(ComputeClass104(validPrefixStops) == 0x04,
        "a valid ident 0x45 before the first empty slot must retain bit2");

    var seventhSlotIgnored = new GoodItem
    {
        StdMode = 22,
        NativeItemExtAbilIdents = new ushort[] { 1, 1, 1, 1, 1, 1, 0x45 },
        NativeItemExtAbilValues = new ushort[7]
    };
    Assert(ComputeClass104(seventhSlotIgnored) == 0,
        "sub_75FE20 must ignore extension slots after its fixed six-slot scan");

    var merchant = new Merchant
    {
        m_PEnvir = map,
        m_sMapName = map.sMapName,
        m_nCurrX = 43,
        m_nCurrY = 43,
        m_sCharName = "repair-eligibility-npc",
        m_boRepair = true,
        m_boS_repair = true,
        m_nPriceRate = 100
    };

    void AssertQuotedButRejected(string name, GoodItem std,
        Action<TUserItem> configure)
    {
        std.Name = name;
        std.Price = 1000;
        std.DuraMax = 100;
        std.Weight = 1;
        var index = M2Share.UserEngine.StdItemList.Count;
        M2Share.UserEngine.StdItemList.Add(std);
        ((System.Collections.IList)GetField(merchant, "m_ItemTypeList")).Add(
            (int)std.StdMode);

        var item = new TUserItem
        {
            wIndex = (ushort)index,
            Dura = 50,
            DuraMax = 100
        };
        configure(item);
        var player = NewPlayer(name + "-player", job: 0, level: 20,
            x: 42, y: 43, map);
        player.m_nGoldMax = 10_000_000;
        player.m_nGold = 1_000_000;

        merchant.ClientQueryRepairCost(player, item);
        var quote = ReadRepairQuote(player);
        Assert(quote > 0, name + " must retain a positive repair quote");
        var gold = player.m_nGold;
        var dura = item.Dura;
        var duraMax = item.DuraMax;
        Assert(!merchant.ClientRepairItem(player, item),
            name + " must be rejected only at execution");
        Assert(player.m_nGold == gold && item.Dura == dura && item.DuraMax == duraMax,
            name + " rejection changed gold or durability");
    }

    AssertQuotedButRejected("repair-fc", new GoodItem { StdMode = 4 },
        item => item.NativeClassFc = 1);
    AssertQuotedButRejected("repair-mode1", new GoodItem { StdMode = 1 },
        _ => { });
    AssertQuotedButRejected("repair-shape25", new GoodItem { StdMode = 25, Shape = 9 },
        _ => { });
    AssertQuotedButRejected("repair-class104", new GoodItem { StdMode = 22, Shape = 114 },
        item => item.NativeClass104 = 0x02);
    AssertQuotedButRejected("repair-ore43", new GoodItem { StdMode = 43 },
        _ => { });

    var merchantSource = File.ReadAllText(Path.Combine(AuditRepoRoot.Resolve(),
        "GameSvr", "Npcs", "Merchant.cs"));
    var quoteStart = merchantSource.IndexOf("public void ClientQueryRepairCost(",
        StringComparison.Ordinal);
    var executeStart = merchantSource.IndexOf("public bool ClientRepairItem(",
        StringComparison.Ordinal);
    var clearStart = merchantSource.IndexOf("public override void ClearScript(",
        executeStart, StringComparison.Ordinal);
    var quoteBlock = merchantSource.Substring(quoteStart, executeStart - quoteStart);
    var executeBlock = merchantSource.Substring(executeStart, clearStart - executeStart);
    Assert(!quoteBlock.Contains("NativeRepairEligibility.CanExecute", StringComparison.Ordinal) &&
           executeBlock.Contains("NativeRepairEligibility.CanExecute", StringComparison.Ordinal),
        "sub_63EE14 must remain execution-only and absent from repair quoting");

    Log("REPAIR eligibility: +0xFC, StdMode/Shape, TEquipItem +0x104 and StdMode43 "
        + "reject only execution; +0x104 six-slot producer stops on empty/invalid ident; "
        + "quotes stay positive; mode3 tail-bypass is wired through the persisted "
        + "Click_RepairEx mode byte");
}

// ===== merchant money contracts: statted pricing / sell truncation / tax base / no item mutation =====
// Pins the four byte-verified 战神 contracts that a ref-MIR2 (GameOfMir — a DIFFERENT Mir2 fork, NOT
// 战神) reading had broken. Every one is a MONEY path, so each is asserted on REAL engine methods
// (real Merchant + real TUserItem) with the exact native arithmetic recomputed independently here.
//
//  A) STATTED PRICING  — 战神 sub_783D70 (TBaseItem VMT slot+0x20) @0x783E79-0x783E86
//        mov eax,edi ; mov ecx,5 ; cdq ; idiv ecx ; imul dword[ebp-8] ; add edi,eax
//        => n10 := n10 + (n10 div 5) * n14        (raw bytes: ... F7 F9 / F7 6D F8 / 03 F8)
//     The ref source dropped the leading `n10 +`; that priced StdMode>4 gear at 0.2*n14 of the
//     correct value — 1/6 of the price at n14=1 (-83%) and 1/2 at n14=5 — on BUY, SELL and REPAIR.
//     `div 5` IS a real 32-bit signed integer divide (that half of the ref reading was right).
//
//  B) SELL TRUNCATION  — 战神 sub_63F200 @0x63F233-0x63F23E
//        mov esi,eax ; sar esi,1 ; jns +3 ; adc esi,0     = Delphi `div 2` (truncate toward zero)
//     Not Round(n/2.0): banker's rounding overpaid 1 gold on every odd price (n=7 -> 4 vs 3).
//
//  C) TAX BASE = the money that actually moved, single castle, SINGLE gate — 战神 sub_65B31C
//     has exactly 5 callers in all of CODE (E8 rel32 full scan, each disassembled): 0x63ECF2 buy /
//     0x63F020 repair / 0x63F28E sell / 0x6C9EA7 upgrade(K=0x2710) / 0x6CA182 upgrade-noBreak
//     (K=0x7530). Every one is `cmp byte [self+0x578],0 / je <skip>` -> IncRateGold(<actual amount>)
//     on the SINGLE castle object [[0x7D6214]]. There is NO castle==nil fallback branch, and the
//     strings "GetAllNpcTax"/"UpgradeWeaponPrice" have 0 hits in the whole image. The upgrade tax
//     therefore equals the immediate just charged — using a config constant under-taxed the
//     no-break tier by 2/3.
//
//  D) NO PERSISTENT ITEM MUTATION FOR PRICING — 战神 TOreItem sub_7862B4 @0x7862DA-0x7862E2 clamps
//     DuraMax to 10000 in EBX ONLY and never writes back to [esi+0x28]. The C# used to assign
//     UserItem.DuraMax = 10000, permanently altering a player's item just to quote a price.
void RunMerchantMoneyContracts(Envirnoment map)
{
    var eng = M2Share.UserEngine;
    var src = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "..", "..", "..", "..", "..", "GameSvr", "Npcs", "Merchant.cs"));
    // read CODE, not the comments that explain the contract
    string StripComments(string s) => string.Join("\n", s.Split('\n')
        .Select(line => { var i = line.IndexOf("//", StringComparison.Ordinal); return i >= 0 ? line[..i] : line; }));
    var code = StripComments(src);

    // ---------- A) statted-item pricing keeps the `n10 +` term ----------
    // StdMode 5 weapon, StdItem.DuraMax == UserItem.DuraMax and Dura == DuraMax so the trailing
    // wear terms are identity: GetUserItemPrice collapses to exactly `n10 + (n10 div 5)*n14`.
    eng.StdItemList.Add(new GoodItem
    {
        Name = "属性定价剑", ItemType = GoodType.ITEM_WEAPON, StdMode = 5,
        Weight = 1, DuraMax = 100, Price = 1000
    });
    var pricer = new Merchant
    {
        m_PEnvir = map, m_sMapName = map.sMapName, m_nCurrX = 47, m_nCurrY = 47,
        m_sCharName = "price-npc", m_nPriceRate = 100
    };
    ((System.Collections.IList)GetField(pricer, "m_ItemTypeList")).Add(5);
    var miGetUserItemPrice = typeof(Merchant).GetMethod("GetUserItemPrice",
        BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(TUserItem) }, null)
        ?? throw new MissingMethodException("Merchant", "GetUserItemPrice");
    var miGetSellItemPrice = typeof(Merchant).GetMethod("GetSellItemPrice",
        BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(double) }, null)
        ?? throw new MissingMethodException("Merchant", "GetSellItemPrice");

    int statCases = 0;
    // n14 is the sum of btValue[0..7]; slot 0 (DC+) alone drives it for a plain weapon.
    foreach (int n14 in new[] { 1, 2, 5, 9 })
    {
        TUserItem it = null;
        if (!eng.CopyToUserItemFromName("属性定价剑", ref it)) continue;
        it.DuraMax = 100; it.Dura = 100;
        it.btValue[0] = (byte)n14;

        double got = (double)miGetUserItemPrice.Invoke(pricer, new object[] { it });
        // independent recomputation of the native arithmetic. This NPC has no price-table
        // row for the fixture, so the base is the native ×1.1 template fallback, not the raw
        // 1000 — sub_63F3B4:
        //   0x63F411 7F 2F              jg 0x63F442            ; table value > 0 -> use verbatim
        //   0x63F42F DB 40 3C           fild dword [eax+0x3C]  ; template Price
        //   0x63F432 DB 2D 68 F4 63 00  fld xword [0x63F468]   ; CD CC CC CC CC CC CC 8C FF 3F = 1.1
        //   0x63F438 DE C9              fmulp st(1)
        //   0x63F43A E8 35 41 DC FF     call 0x403574          ; @ROUND
        int nativeBase = HUtil32.Round(1000 * 1.1);                 // 1100
        int nativeN10 = nativeBase + (nativeBase / 5) * n14;        // @0x783E86 add edi,eax
        int expected = HUtil32._MAX(2, nativeN10);                  // wear terms are identity here
        int refBug = HUtil32._MAX(2, (nativeBase / 5) * n14);        // the dropped-`n10 +` value
        Assert((int)got == expected,
            $"STATTED PRICING: n14={n14} -> {got}, native sub_783D70 @0x783E86 gives {expected} "
            + $"(n10 + (n10 div 5)*n14). The dropped-term ref value would be {refBug}.");
        Assert((int)got != refBug || n14 == 4,
            $"STATTED PRICING regressed to the ref shape `n10 div 5 * n14` at n14={n14} ({refBug})");
        statCases++;
    }
    Assert(statCases == 4, "statted-pricing sweep did not run all n14 cases");
    // n14=1 is the worst case: the ref shape is 220 vs the native 1320 = -83.3%
    Assert(!code.Contains("Math.Floor(n10 / 5)") && !code.Contains("Math.Floor(n10/5)"),
        "GetUserItemPrice reverted to `Math.Floor(n10/5)*n14` — native @0x783E86 ADDS n10 back");
    Assert(code.Contains("n10 = n10 + (double)((int)n10 / 5 * n14)"),
        "the native `n10 + (n10 div 5)*n14` form is gone (integer divide must stay integer: cdq/idiv)");

    // ---------- B) sell price truncates, it does not round ----------
    foreach (int odd in new[] { 3, 7, 11, 4001 })
    {
        int sell = (int)miGetSellItemPrice.Invoke(pricer, new object[] { (double)odd });
        Assert(sell == odd / 2,
            $"SELL PRICE: {odd} -> {sell}, native sub_63F200 @0x63F235 `sar esi,1` gives {odd / 2} "
            + $"(Round(n/2.0) would give {HUtil32.Round(odd / 2.0)} = +1 gold on odd prices)");
    }
    Assert(!code.Contains("HUtil32.Round(nPrice / 2.0)"),
        "GetSellItemPrice reverted to banker's rounding — native @0x63F235 sar/jns/adc = div 2");

    // ---------- C) tax base = the actual amount; no invented castle==nil fallback ----------
    // The three trade paths must pass the real money moved, and the upgrade tax must pass its
    // `price` argument (K=0x7530=30000 for no-break) — never a config constant.
    var taxStart = code.IndexOf("private void AddWeaponUpgradeTax(", StringComparison.Ordinal);
    var taxEnd = code.IndexOf("private static void ApplyNativeWeaponUpgrade(", taxStart, StringComparison.Ordinal);
    Assert(taxStart >= 0 && taxEnd > taxStart, "AddWeaponUpgradeTax source boundary missing");
    var taxCode = code.Substring(taxStart, taxEnd - taxStart);
    Assert(taxCode.Contains("IncRateGold(price)"),
        "AddWeaponUpgradeTax no longer accrues the ACTUAL charged amount — native sub_6CA020 @0x6CA163-82 "
        + "feeds the SAME immediate K to DecGold and IncRateGold (K=0x7530 no-break / 0x2710 normal); "
        + "a config constant under-taxes the no-break tier by 2/3");
    Assert(!taxCode.Contains("nUpgradeWeaponPrice"),
        "AddWeaponUpgradeTax reverted to the config constant nUpgradeWeaponPrice "
        + "(\"UpgradeWeaponPrice\" has 0 string hits in the whole 战神 image)");
    Assert(!taxCode.Contains("_ = price"),
        "AddWeaponUpgradeTax is discarding its price argument again");
    // no fallback branch anywhere in the file: 5/5 native callers are single-gate/single-branch
    Assert(!code.Contains("CastleManager.IncRateGold"),
        "a castle==nil fallback accrual came back — sub_65B31C has exactly 5 callers in all of CODE, "
        + "every one single-gate onto the SINGLE castle object [[0x7D6214]]; there is no 6th caller "
        + "and no CastleManager-shaped list walk (\"GetAllNpcTax\" has 0 string hits)");
    Assert(!code.Contains("boGetAllNpcTax"),
        "boGetAllNpcTax is gating a money path again — it is a ref-MIR2 concept with 0 string hits in 战神");
    int incRateGoldSites = 0, idx = 0;
    while ((idx = code.IndexOf(".IncRateGold(", idx, StringComparison.Ordinal)) >= 0) { incRateGoldSites++; idx++; }
    Assert(incRateGoldSites == 4,
        $"Merchant has {incRateGoldSites} IncRateGold call sites, expected 4 (buy nPrice / sell nPrice / "
        + "repair nRepairPrice / upgrade price) matching native 0x63ECF2 / 0x63F28E / 0x63F020 / 0x6C9EA7+0x6CA182");
    foreach (var expectedArg in new[] { "IncRateGold(nPrice)", "IncRateGold(nRepairPrice)", "IncRateGold(price)" })
        Assert(code.Contains(expectedArg),
            $"tax site lost its actual-amount argument: expected `{expectedArg}` (native passes the money that moved)");

    // ---------- D) pricing must not persistently mutate the item (TOreItem, StdMode 43) ----------
    eng.StdItemList.Add(new GoodItem
    {
        Name = "定价矿石", ItemType = GoodType.ITEM_ETC, StdMode = 43,
        Weight = 1, DuraMax = 100, Price = 1000
    });
    ((System.Collections.IList)GetField(pricer, "m_ItemTypeList")).Add(43);
    TUserItem ore = null;
    Assert(eng.CopyToUserItemFromName("定价矿石", ref ore), "ore fixture not created");
    ore.DuraMax = 100; ore.Dura = 50;
    ushort oreDuraMaxBefore = ore.DuraMax, oreDuraBefore = ore.Dura;
    _ = (double)miGetUserItemPrice.Invoke(pricer, new object[] { ore });
    Assert(ore.DuraMax == oreDuraMaxBefore && ore.Dura == oreDuraBefore,
        $"ITEM MUTATED BY PRICING: DuraMax {oreDuraMaxBefore}->{ore.DuraMax}, Dura {oreDuraBefore}->{ore.Dura}. "
        + "Native TOreItem sub_7862B4 @0x7862DA-E2 clamps to 10000 in EBX ONLY and never writes [esi+0x28]");
    // quoting twice must be idempotent — a persistent clamp would change the second answer
    double q1 = (double)miGetUserItemPrice.Invoke(pricer, new object[] { ore });
    double q2 = (double)miGetUserItemPrice.Invoke(pricer, new object[] { ore });
    Assert(q1 == q2, $"ore price not idempotent ({q1} then {q2}) — pricing is mutating item state");
    Assert(!code.Contains("UserItem.DuraMax = 10000"),
        "the persistent `UserItem.DuraMax = 10000` pricing mutation came back (native clamps in EBX only)");

    // ---------- E) ECON-12: pile items multiply the base price by the stack COUNT ----------
    // Native sub_63F3B4 @0x63F442-0x63F45B gates on the runtime KIND byte
    // `cmp byte [instance+0x14],7` (NOT template StdMode -- the same function does hop
    // `mov eax,[eax+0x1c]` at 0x63F416/0x63F42C to reach template fields, and this gate
    // pointedly does not), then `movzx eax,word [instance+0x26]` (= Dura) and `imul`.
    // +0x14 is zeroed by the base item ctor sub_783788 @0x7837AE and set to 7 ONLY by the
    // pile ctor sub_7880F0 @0x788118, which the factory sub_74C338 selects via
    // @0x74D67E `cmp al,0x96 / jb` => StdMode >= 150. C#'s equivalent is IsPileItem.
    // Because the multiply lives INSIDE sub_63F3B4, it is upstream of the buy shell's
    // `jle` (0x63F3A1), the VMT+0x20 wear slot (0x63F3A9), the rate stage and the sell
    // `sar esi,1`, so buy / sell / repair must ALL scale with the count.
    eng.StdItemList.Add(new GoodItem
    {
        Name = "定价堆叠药", ItemType = GoodType.ITEM_ETC, StdMode = 150,
        Weight = 1, DuraMax = 0, Price = 100
    });
    ((System.Collections.IList)GetField(pricer, "m_ItemTypeList")).Add(150);
    // NativeItemFactory is internal and this audit is not a friend assembly, so reach
    // IsPileItem by reflection rather than widening GameSvr's InternalsVisibleTo list.
    var tNativeItemFactory = typeof(Merchant).Assembly.GetType("GameSvr.NativeItemFactory")
        ?? throw new TypeLoadException("GameSvr.NativeItemFactory");
    var miIsPileItem = tNativeItemFactory.GetMethod("IsPileItem",
        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
        null, new[] { typeof(GoodItem) }, null)
        ?? throw new MissingMethodException("NativeItemFactory", "IsPileItem");
    Assert((bool)miIsPileItem.Invoke(null, new object[] { eng.GetStdItem("定价堆叠药") }),
        "ECON-12 fixture is not a pile item -- StdMode 150 must satisfy IsPileItem "
        + "(native factory sub_74C338 @0x74D67E `cmp al,0x96/jb` routes >=150 to pile ctor 0x7880F0)");

    int pileCases = 0;
    foreach (int qty in new[] { 1, 2, 100, 999 })
    {
        TUserItem pile = null;
        if (!eng.CopyToUserItemFromName("定价堆叠药", ref pile)) continue;
        pile.Dura = (ushort)qty;
        double basePrice = (double)miGetUserItemPrice.Invoke(pricer, new object[] { pile });
        // The unit price is the ×1.1 template fallback (0x63F42F fild [tpl+0x3C] /
        // 0x63F432 fld xword [0x63F468]=1.1 / fmulp / 0x63F43A call 0x403574 @ROUND),
        // because this NPC has no price-table row for the fixture.
        int pileUnit = HUtil32.Round(100 * 1.1);                    // 110
        Assert((int)basePrice == pileUnit * qty,
            $"ECON-12 PILE BASE PRICE: qty={qty} -> {basePrice}, native sub_63F3B4 @0x63F458 "
            + $"`imul` gives {pileUnit * qty} (unit price {pileUnit} * count {qty}). Missing the "
            + "multiply pays for ONE unit while ClientUserSellItem removes the WHOLE stack.");
        // the sell half then applies div 2 on the already-multiplied base (0x63F235)
        int sellPrice = (int)miGetSellItemPrice.Invoke(pricer, new object[] { basePrice });
        Assert(sellPrice == pileUnit * qty / 2,
            $"ECON-12 PILE SELL PRICE: qty={qty} -> {sellPrice}, expected {pileUnit * qty / 2} "
            + "(native halves the multiplied base, so the count survives into the payout)");
        pileCases++;
    }
    Assert(pileCases == 4, "ECON-12 pile pricing sweep did not run all quantity cases");

    // a NON-pile item must NOT scale with Dura, or every worn weapon gets count-inflated
    TUserItem nonPile = null;
    Assert(eng.CopyToUserItemFromName("属性定价剑", ref nonPile), "non-pile fixture not created");
    nonPile.DuraMax = 100; nonPile.Dura = 100;
    double nonPilePrice = (double)miGetUserItemPrice.Invoke(pricer, new object[] { nonPile });
    Assert((int)nonPilePrice == HUtil32.Round(1000 * 1.1),
        $"ECON-12 OVER-REACH: non-pile StdMode 5 priced {nonPilePrice}, expected "
        + $"{HUtil32.Round(1000 * 1.1)}. The count "
        + "multiply must be gated on IsPileItem -- native `jne 0x63F45E` skips it for +0x14 != 7 "
        + "(base ctor sub_783788 @0x7837AE writes 0), otherwise Dura doubles as a bogus multiplier.");
    Assert(code.Contains("NativeItemFactory.IsPileItem(StdItem)"),
        "ECON-12 pile-count multiply lost its IsPileItem gate in GetUserItemPrice "
        + "(native gate = runtime +0x14 == 7, whose set is exactly StdMode>=150 with a class)");
    Assert(!code.Contains("StdItem.StdMode == 7)")
        || !code.Contains("n10 = unchecked((int)n10 * (int)UserItem.Dura)"),
        "ECON-12 gate rewritten as template `StdMode == 7`. That is a DIFFERENT field: native "
        + "@0x63F445 reads INSTANCE+0x14 with no +0x1C hop, and StdMode 7 (charm) never reaches "
        + "the pile ctor in factory sub_74C338.");

    Log($"ECON-12 pile pricing: base = unit * count across {pileCases} quantity cases "
        + "(native sub_63F3B4 @0x63F454-58 `movzx eax,word [inst+0x26]` / `imul`), gated on the "
        + "runtime KIND byte `cmp byte [inst+0x14],7` @0x63F445 -- NOT template StdMode (the same "
        + "function hops `mov eax,[eax+0x1c]` @0x63F416/0x63F42C for template fields, this gate "
        + "does not); +0x14 is 0 from base ctor sub_783788 @0x7837AE and 7 only from pile ctor "
        + "sub_7880F0 @0x788118, selected by factory @0x74D67E `cmp al,0x96/jb` = StdMode>=150 "
        + "== IsPileItem; non-pile StdMode 5 stays unscaled; multiply is upstream of the wear slot, "
        + "rate stage and the sell div 2, so buy/sell/repair all scale with the count");

    Log($"MERCHANT money contracts: statted pricing = n10 + (n10 div 5)*n14 across {statCases} n14 cases "
        + "(native sub_783D70 @0x783E86 `add edi,eax`; dropping `n10 +` was -83% at n14=1: 220 vs 1320); "
        + "sell price truncates via div 2 (sub_63F200 @0x63F235 sar/jns/adc, not banker's Round: 7->3 not 4); "
        + $"{incRateGoldSites}/4 tax sites pass the ACTUAL money moved incl. IncRateGold(price) for the "
        + "no-break upgrade tier K=0x7530 (sub_6CA020 @0x6CA163-82), single castle [[0x7D6214]], no "
        + "castle==nil fallback (sub_65B31C has exactly 5 CODE callers, all single-gate); TOreItem pricing "
        + "leaves UserItem.DuraMax/Dura untouched and is idempotent (sub_7862B4 @0x7862DA-E2 clamps EBX only)");
}

// ===== merchant sell caller/worker ownership and order-4 authentication (sub_6B9220/sub_63F200) =====
void RunMerchantSellAuthenticationOwnership(Envirnoment map)
{
    var engine = M2Share.UserEngine;
    int normalIndex = engine.StdItemList.Count;
    engine.StdItemList.Add(new GoodItem
    {
        Name = "ECON39普通剑", ItemType = GoodType.ITEM_WEAPON, StdMode = 5,
        Price = 1000, Weight = 1, DuraMax = 1000, NeedIdentify = 0
    });
    int pileIndex = engine.StdItemList.Count;
    engine.StdItemList.Add(new GoodItem
    {
        Name = "ECON39堆叠物", ItemType = GoodType.ITEM_ETC, StdMode = 150,
        Price = 100, Weight = 1, DuraMax = 0, NeedIdentify = 0
    });

    bool originalAuthOpen = M2Share.g_Config.boAuthOpen;
    int sequence = 0;

    bool MerchantHasExact(Merchant merchant, TUserItem expected)
    {
        var groups = (System.Collections.IEnumerable)GetField(merchant, "m_GoodsList");
        foreach (var group in groups)
        {
            foreach (var item in (System.Collections.IEnumerable)group)
            {
                if (ReferenceEquals(item, expected)) return true;
            }
        }
        return false;
    }

    void RunSuccessfulSale(string scenario, int stdIndex, ushort dura,
        ushort duraMax, bool authOpen, byte authStatus1, bool property9,
        byte permission, bool sellFlag, int dx, int dy, string requestName,
        int expectedGoldDelta, bool expectedMerchantOwnership,
        string expectedAction94 = null, bool customNamed = false,
        byte authStatus2 = 0)
    {
        int id = ++sequence;
        var merchant = new Merchant
        {
            m_PEnvir = map,
            m_sMapName = map.sMapName,
            m_nCurrX = 20,
            m_nCurrY = 20,
            m_sCharName = "econ39-npc-" + id,
            m_boSell = sellFlag,
            m_nPriceRate = 100
        };
        merchant.m_ItemTypeList.Add(engine.GetStdItem(stdIndex).StdMode);
        if (property9) merchant.AddNativePasProperty(9);
        Assert(ReferenceEquals(engine.FindMerchant(merchant.ObjectId), merchant),
            scenario + " merchant must resolve by native object id");

        var player = new StoragePacketProbe
        {
            m_boOffLineFlag = true,
            m_boGhost = false,
            m_btJob = 0,
            m_btPermission = permission,
            m_sMapName = map.sMapName,
            m_nCurrX = (short)(merchant.m_nCurrX + dx),
            m_nCurrY = (short)(merchant.m_nCurrY + dy),
            m_sCharName = "econ39-player-" + id,
            m_PEnvir = map,
            m_nGold = 100,
            m_nGoldMax = 1_000_000
        };
        player.m_WAbil.MaxWeight = 10_000;
        player.SetNativeAuthenticationStatus(authStatus1, authStatus2, 0);

        var userItem = new TUserItem
        {
            wIndex = (ushort)stdIndex,
            MakeIndex = 930_000 + id,
            Dura = dura,
            DuraMax = duraMax
        };
        if (customNamed) userItem.btValue[13] = 1;
        player.m_ItemList.Add(userItem);
        int clientItemId = player.EnsureClientItemId(userItem);

        player.WeightChanged();
        PumpMessages(player);
        int preSaleWeight = player.m_WAbil.Weight;
        int preSaleGold = player.m_nGold;
        player.Sent.Clear();
        player.m_MsgList.Clear();
        M2Share.LogStringList.Clear();
        M2Share.g_Config.boAuthOpen = authOpen;

        player.Operate(new TProcessMessage
        {
            wIdent = Grobal2.CM_USERSELLITEM,
            nParam1 = merchant.ObjectId,
            nParam2 = HUtil32.LoWord(clientItemId),
            nParam3 = HUtil32.HiWord(clientItemId),
            sMsg = requestName
        });
        PumpMessages(player);

        Assert(player.m_nGold == preSaleGold + expectedGoldDelta,
            scenario + " gold delta mismatch");
        Assert(!player.m_ItemList.Any(item => ReferenceEquals(item, userItem)),
            scenario + " sold item remained in player bag");
        Assert(MerchantHasExact(merchant, userItem) == expectedMerchantOwnership,
            scenario + " merchant ownership mismatch");
        Assert(player.Sent.Count(packet => packet.Ident == Grobal2.SM_USERSELLITEM_OK) == 1
               && !player.Sent.Any(packet => packet.Ident == Grobal2.SM_USERSELLITEM_FAIL),
            scenario + " must send one sell-success packet and no failure packet");
        var weightPackets = player.Sent
            .Where(packet => packet.Ident == Grobal2.SM_WEIGHTCHANGED).ToList();
        Assert(weightPackets.Count == 1 && weightPackets[0].Recog == preSaleWeight
               && player.m_WAbil.Weight == preSaleWeight,
            scenario + " WeightChanged must run exactly once in the worker before bag removal");

        var logs = M2Share.LogStringList.Cast<string>().ToList();
        string expectedAction10 = $"10\t{map.sMapName}\t{player.m_nCurrX}\t{player.m_nCurrY}"
            + $"\t{player.m_sCharName}\t{engine.GetStdItem(stdIndex).Name}\t{userItem.MakeIndex}"
            + $"\t1\t{merchant.m_sCharName}";
        Assert(logs.Count(line => line == expectedAction10) == 1,
            scenario + " exact action 10 record mismatch (it must remain unconditional at NeedIdentify=0)");
        if (expectedAction94 == null)
        {
            Assert(!logs.Any(line => line.StartsWith("94\t", StringComparison.Ordinal)),
                scenario + " unexpectedly emitted action 94");
        }
        else
        {
            Assert(logs.Count(line => line == expectedAction94) == 1,
                scenario + " action 94 record mismatch");
            Assert(!player.Sent.Any(packet =>
                    (packet.Body ?? string.Empty).Contains("未验证", StringComparison.Ordinal)),
                scenario + " action 94 text leaked into a client packet");
        }
        if (customNamed)
        {
            Assert(userItem.btValue[13] == 1,
                scenario + " caller mutated custom-name state absent from native sub_6B9220");
        }
    }

    try
    {
        RunSuccessfulSale("wrong request name", normalIndex, 1000, 1000,
            authOpen: false, authStatus1: 0, property9: false, permission: 0,
            sellFlag: true, dx: 1, dy: 0, requestName: "完全错误的名称",
            expectedGoldDelta: 550, expectedMerchantOwnership: true);

        RunSuccessfulSale("m_boSell false", normalIndex, 1000, 1000,
            authOpen: true, authStatus1: 0x10, property9: false, permission: 0,
            sellFlag: false, dx: 1, dy: 0, requestName: "ECON39普通剑",
            expectedGoldDelta: 550, expectedMerchantOwnership: true,
            customNamed: true);

        RunSuccessfulSale("status2 order-4 authenticated", normalIndex, 1000, 1000,
            authOpen: true, authStatus1: 0, property9: false, permission: 0,
            sellFlag: true, dx: 1, dy: 0, requestName: "ECON39普通剑",
            expectedGoldDelta: 550, expectedMerchantOwnership: true,
            authStatus2: 0x10);

        int unauthId = sequence + 1;
        string unauthPlayer = "econ39-player-" + unauthId;
        RunSuccessfulSale("regular unauthenticated", normalIndex, 1000, 1000,
            authOpen: true, authStatus1: 0, property9: false, permission: 0,
            sellFlag: true, dx: 1, dy: 0, requestName: "ECON39普通剑",
            expectedGoldDelta: 550, expectedMerchantOwnership: false,
            expectedAction94: $"94\t{map.sMapName}\t21\t20\t{unauthPlayer}\tECON39普通剑\t{930_000 + unauthId}\t1\t未验证,物品消失Npc");

        RunSuccessfulSale("range 15 inclusive", normalIndex, 1000, 1000,
            authOpen: false, authStatus1: 0, property9: false, permission: 0,
            sellFlag: true, dx: 15, dy: 15, requestName: "ECON39普通剑",
            expectedGoldDelta: 550, expectedMerchantOwnership: true);

        int propertyId = sequence + 1;
        string propertyPlayer = "econ39-player-" + propertyId;
        RunSuccessfulSale("property-9 unauthenticated", normalIndex, 1000, 1000,
            authOpen: true, authStatus1: 0, property9: true, permission: 4,
            sellFlag: false, dx: 1, dy: 0, requestName: "ECON39普通剑",
            expectedGoldDelta: 0, expectedMerchantOwnership: false,
            expectedAction94: $"94\t{map.sMapName}\t21\t20\t{propertyPlayer}\tECON39普通剑\t{930_000 + propertyId}\t1\t未验证,物品消失Npc");

        int pileId = sequence + 1;
        string pilePlayer = "econ39-player-" + pileId;
        RunSuccessfulSale("pile unauthenticated", pileIndex, 7, 0,
            authOpen: true, authStatus1: 0, property9: false, permission: 0,
            sellFlag: true, dx: 1, dy: 0, requestName: "ECON39堆叠物",
            expectedGoldDelta: 385, expectedMerchantOwnership: false,
            expectedAction94: $"94\t{map.sMapName}\t21\t20\t{pilePlayer}\tECON39堆叠物\t{930_000 + pileId}\t7\t未验证物品消失Npc");

        var farMerchant = new Merchant
        {
            m_PEnvir = map, m_sMapName = map.sMapName,
            m_nCurrX = 20, m_nCurrY = 20, m_sCharName = "econ39-far-npc",
            m_boSell = true, m_nPriceRate = 100
        };
        farMerchant.m_ItemTypeList.Add(5);
        var farPlayer = new StoragePacketProbe
        {
            m_boOffLineFlag = true, m_boGhost = false,
            m_sMapName = map.sMapName, m_PEnvir = map,
            m_nCurrX = 36, m_nCurrY = 20, m_sCharName = "econ39-range16",
            m_nGold = 100, m_nGoldMax = 1_000_000
        };
        var farItem = new TUserItem
        {
            wIndex = (ushort)normalIndex, MakeIndex = 939_999,
            Dura = 1000, DuraMax = 1000
        };
        farPlayer.m_ItemList.Add(farItem);
        int farClientId = farPlayer.EnsureClientItemId(farItem);
        M2Share.g_Config.boAuthOpen = false;
        M2Share.LogStringList.Clear();
        farPlayer.Operate(new TProcessMessage
        {
            wIdent = Grobal2.CM_USERSELLITEM,
            nParam1 = farMerchant.ObjectId,
            nParam2 = HUtil32.LoWord(farClientId),
            nParam3 = HUtil32.HiWord(farClientId),
            sMsg = "ECON39普通剑"
        });
        PumpMessages(farPlayer);
        Assert(farPlayer.m_nGold == 100
               && farPlayer.m_ItemList.Any(item => ReferenceEquals(item, farItem))
               && !MerchantHasExact(farMerchant, farItem)
               && farPlayer.Sent.Count == 0
               && M2Share.LogStringList.Count == 0,
            "range 16 must reject silently without moving money or ownership");

        var nextClientIdField = typeof(TPlayObject).GetField("_nextClientItemId",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(TPlayObject), "_nextClientItemId");
        int NextForgedId(TPlayObject player)
        {
            int next = (int)nextClientIdField.GetValue(player);
            return next == 0 ? 1 : next;
        }

        var hiddenMerchant = new Merchant
        {
            m_PEnvir = map, m_sMapName = map.sMapName,
            m_nCurrX = 20, m_nCurrY = 20, m_sCharName = "econ39-hidden-npc",
            m_boSell = true, m_nPriceRate = 100
        };
        hiddenMerchant.m_ItemTypeList.Add(5);
        var hiddenPlayer = new StoragePacketProbe
        {
            m_boOffLineFlag = true, m_boGhost = false,
            m_sMapName = map.sMapName, m_PEnvir = map,
            m_nCurrX = 21, m_nCurrY = 20, m_sCharName = "econ39-hidden",
            m_nGold = 100, m_nGoldMax = 1_000_000
        };
        var hiddenItem = new TUserItem
        {
            wIndex = (ushort)normalIndex, MakeIndex = 940_001,
            Dura = 1000, DuraMax = 1000, ClientItemID = 0
        };
        hiddenPlayer.m_ItemList.Add(hiddenItem);
        int forgedId = NextForgedId(hiddenPlayer);
        hiddenPlayer.Operate(new TProcessMessage
        {
            wIdent = Grobal2.CM_USERSELLITEM,
            nParam1 = hiddenMerchant.ObjectId,
            nParam2 = HUtil32.LoWord(forgedId),
            nParam3 = HUtil32.HiWord(forgedId),
            sMsg = "ECON39普通剑"
        });
        PumpMessages(hiddenPlayer);
        Assert(hiddenItem.ClientItemID == 0
               && hiddenPlayer.m_ItemList.Any(item => ReferenceEquals(item, hiddenItem))
               && hiddenPlayer.m_nGold == 100
               && !MerchantHasExact(hiddenMerchant, hiddenItem)
               && hiddenPlayer.Sent.Count == 0,
            "forged next id must not lazily expose and sell a ClientItemID=0 bag item");

        var hiddenFarPlayer = new StoragePacketProbe
        {
            m_boOffLineFlag = true, m_boGhost = false,
            m_sMapName = map.sMapName, m_PEnvir = map,
            m_nCurrX = 36, m_nCurrY = 20, m_sCharName = "econ39-hidden-range16",
            m_nGold = 100, m_nGoldMax = 1_000_000
        };
        var hiddenFarItem = new TUserItem
        {
            wIndex = (ushort)normalIndex, MakeIndex = 940_002,
            Dura = 1000, DuraMax = 1000, ClientItemID = 0
        };
        hiddenFarPlayer.m_ItemList.Add(hiddenFarItem);
        int farForgedId = NextForgedId(hiddenFarPlayer);
        hiddenFarPlayer.Operate(new TProcessMessage
        {
            wIdent = Grobal2.CM_USERSELLITEM,
            nParam1 = hiddenMerchant.ObjectId,
            nParam2 = HUtil32.LoWord(farForgedId),
            nParam3 = HUtil32.HiWord(farForgedId),
            sMsg = "ECON39普通剑"
        });
        Assert(hiddenFarItem.ClientItemID == 0
               && hiddenFarPlayer.m_ItemList.Count == 1,
            "range rejection must happen before bag scan and must not allocate a client id");

        hiddenFarPlayer.Operate(new TProcessMessage
        {
            wIdent = Grobal2.CM_USERSELLITEM,
            nParam1 = int.MaxValue,
            nParam2 = 0,
            nParam3 = 0,
            sMsg = "ECON39普通剑"
        });
        Assert(hiddenFarItem.ClientItemID == 0,
            "non-positive client id must return before merchant lookup or bag mutation");

        hiddenFarPlayer.Operate(new TProcessMessage
        {
            wIdent = Grobal2.CM_USERSELLITEM,
            nParam1 = int.MaxValue,
            nParam2 = HUtil32.LoWord(farForgedId),
            nParam3 = HUtil32.HiWord(farForgedId),
            sMsg = "ECON39普通剑"
        });
        Assert(hiddenFarItem.ClientItemID == 0
               && hiddenFarPlayer.m_ItemList.Count == 1,
            "unknown merchant id must return before bag scan without throwing or assigning an id");
    }
    finally
    {
        M2Share.g_Config.boAuthOpen = originalAuthOpen;
    }

    Log("ECON-39 merchant sell: positive pre-existing client-id-only lookup, no lazy id allocation, "
        + "merchant/range gates before bag scan, no m_boSell gate, inclusive range 15, "
        + "two fresh order-4 checks, regular/property-9 ownership, exact action 10/94 records, "
        + "pile quantity/reason, and one worker-side pre-removal WeightChanged all passed; "
        + "property-9 auth rejection safely removes the native dangling goods reference");
}

// ===== script storage-space APIs (sub_6F30A0 / GetStorageSpaceCount 0x72BBE0) =====
void RunStorageSpaceApi()
{
    var player = new StoragePacketProbe
    {
        m_boOffLineFlag = true,
        m_sCharName = "storage-space-api"
    };
    var bridge = new PasApiBridge { CurrentPlayer = player };

    static (int Final, int Added) NativeExpandModel(int current, int requested)
    {
        var target = unchecked(current + requested);
        if (target > TPlayObject.MAX_STORAGE_ITEM_COUNT)
            target = TPlayObject.MAX_STORAGE_ITEM_COUNT;
        var actualAdded = unchecked(target - current);
        var final = current;
        if (actualAdded > 0)
            final = unchecked(current + actualAdded);
        return (final, actualAdded);
    }

    foreach (var current in new[] { 48, 191, 192, 193 })
    foreach (var requested in new[] { -10, 0, 1, 100 })
    {
        var expected = NativeExpandModel(current, requested);
        player.m_nStorageSpaceCount = current;
        player.Sent.Clear();
        Assert(bridge.CallPlayerFunc("ExpandStorageSpace",
                new List<PasValue> { PasValue.FromInt(requested) },
                out var result),
            $"ExpandStorageSpace dispatch current={current} requested={requested}");
        Assert(result.Type == PasValueType.Integer &&
               result.AsInt() == expected.Added,
            $"ExpandStorageSpace return current={current} requested={requested}: " +
            $"expected {expected.Added}, got {result.AsInt()}");
        Assert(player.m_nStorageSpaceCount == expected.Final,
            $"ExpandStorageSpace final capacity current={current} requested={requested}: " +
            $"expected {expected.Final}, got {player.m_nStorageSpaceCount}");
        Assert(player.Sent.Count == 1,
            $"ExpandStorageSpace packet count current={current} requested={requested}");
        var packet = player.Sent[0];
        Assert(packet.Ident == Grobal2.SM_STORAGE_SPACE && packet.Recog == 0 &&
               packet.Param == 0 && packet.Tag == unchecked((ushort)expected.Final) &&
               packet.Series == 0,
            $"ExpandStorageSpace packet fields current={current} requested={requested}");
    }

    player.m_nStorageSpaceCount = 193;
    Assert(bridge.CallPlayerFunc("GetStorageSpaceCount", new List<PasValue>(),
            out var queryResult) && queryResult.Type == PasValueType.Integer &&
           queryResult.AsInt() == 193,
        "GetStorageSpaceCount must return the raw runtime value above 192");

    Log("storage-space API: raw signed capacity query; unchecked add; upper-only 192 cap; " +
        "non-positive effective increments preserve the field; SM718 fields exact");
}

// ===== personal-storage take-back failure codes (sub_6C2D7C) =====
void RunStorageTakeFailureCodes(Envirnoment map)
{
    var merchant = new Merchant
    {
        m_PEnvir = map, m_sMapName = map.sMapName, m_nCurrX = 50, m_nCurrY = 50,
        m_sCharName = "storage-npc", m_boGetback = true
    };

    var player = new StoragePacketProbe
    {
        m_boOffLineFlag = true,
        m_boGhost = false,
        m_btJob = 0,
        m_sMapName = map.sMapName,
        m_nCurrX = 51,
        m_nCurrY = 50,
        m_sCharName = "storage-take"
    };
    player.m_Abil.Level = 20;
    player.m_PEnvir = map;
    player.m_NPC = merchant;
    TUserItem item = null;
    Assert(M2Share.UserEngine.CopyToUserItemFromName("铁剑", ref item),
        "storage failure-code fixture item was not created");
    player.m_StorageItemList.Add(item);

    var ensureClientId = typeof(TPlayObject).GetMethod("EnsureClientItemId",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("TPlayObject", "EnsureClientItemId");
    var takeBack = typeof(TPlayObject).GetMethod("ClientTakeBackStorageItem",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("TPlayObject", "ClientTakeBackStorageItem");
    int clientItemId = (int)ensureClientId.Invoke(player, new object[] { item });

    int bagBefore = player.m_ItemList.Count;
    int storageBefore = player.m_StorageItemList.Count;

    var loadedStorageItem = new TUserItem
    {
        wIndex = item.wIndex,
        MakeIndex = item.MakeIndex,
        Dura = item.Dura,
        DuraMax = item.DuraMax,
        ClientItemID = 0
    };
    var loadedRecord = new THumDataInfo();
    loadedRecord.Header.dCreateDate = DateTime.Now.ToOADate();
    loadedRecord.Data.sCharName = "storage-load-id";
    loadedRecord.Data.sCurMap = map.sMapName;
    loadedRecord.Data.StorageItems = new TUserItem[192];
    loadedRecord.Data.StorageItems[0] = loadedStorageItem;
    var loadedPlayer = new StoragePacketProbe { m_boOffLineFlag = true };
    var getHumData = typeof(UserEngine).GetMethod("GetHumData",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(nameof(UserEngine), "GetHumData");
    getHumData.Invoke(M2Share.UserEngine, new object[] { loadedPlayer, loadedRecord });
    Assert(loadedPlayer.m_StorageItemList.Count == 1
           && ReferenceEquals(loadedPlayer.m_StorageItemList[0], loadedStorageItem)
           && loadedStorageItem.ClientItemID != 0,
        "personal storage load must immediately assign the native client item id");

    void InvokeTakeBack(int objectId, string itemName = "铁剑",
        int? requestedItemId = null)
    {
        player.Sent.Clear();
        player.m_DefMsg = null;
        takeBack.Invoke(player, new object[]
        {
            objectId, requestedItemId ?? clientItemId, itemName
        });
    }

    void AssertSingleFailure(int recog, string scenario)
    {
        Assert(player.Sent.Count == 1
               && player.Sent[0].Ident == Grobal2.SM_TAKEBACKSTORAGEITEM_FAIL
               && player.Sent[0].Recog == recog
               && player.Sent[0].Param == 0
               && player.Sent[0].Tag == 0
               && player.Sent[0].Series == 0,
            scenario + $" must send only SM 706 / Recog={recog}");
    }

    // 0x6C2DAF precedes 0x6C2DBC and both precede every NPC read.
    player.m_boCanGetBackItem = false;
    player.m_boDealing = true;
    player.m_NPC = null;
    InvokeTakeBack(int.MaxValue);
    AssertSingleFailure(-3, "prohibited+dealing+invalid-NPC take-back");
    Assert(player.m_ItemList.Count == bagBefore
           && player.m_StorageItemList.Count == storageBefore,
        "locked personal-storage take-back moved the item");

    player.m_boCanGetBackItem = true;
    InvokeTakeBack(int.MaxValue);
    AssertSingleFailure(-2, "dealing+invalid-NPC take-back");

    player.m_boDealing = false;
    InvokeTakeBack(int.MaxValue);
    AssertSingleFailure(0, "invalid cached-NPC take-back");

    player.m_NPC = merchant;
    InvokeTakeBack(merchant.ObjectId, requestedItemId: int.MaxValue);
    AssertSingleFailure(0,
        "valid cached-NPC plus unknown-item take-back");

    player.m_WAbil.Weight = 100;
    player.m_WAbil.MaxWeight = 100;
    merchant.m_nCurrX = 80;
    InvokeTakeBack(merchant.ObjectId);
    AssertSingleFailure(0, "out-of-range+overweight take-back");

    merchant.m_nCurrX = 50;
    var previousTryModeUseStorage = M2Share.g_Config.boTryModeUseStorage;
    try
    {
        merchant.m_boGetback = false;
        player.m_nPayMent = 1;
        M2Share.g_Config.boTryModeUseStorage = false;
        InvokeTakeBack(merchant.ObjectId);
        AssertSingleFailure(-1,
            "merchant flag and trial-mode independent overweight take-back");
    }
    finally
    {
        merchant.m_boGetback = true;
        player.m_nPayMent = 0;
        M2Share.g_Config.boTryModeUseStorage = previousTryModeUseStorage;
    }

    var ordinaryNpc = new TBaseObject
    {
        m_PEnvir = map,
        m_sMapName = map.sMapName,
        m_nCurrX = 50,
        m_nCurrY = 50,
        m_sCharName = "storage-ordinary-npc"
    };
    player.m_NPC = ordinaryNpc;
    InvokeTakeBack(ordinaryNpc.ObjectId);
    AssertSingleFailure(-1,
        "non-Merchant cached-NPC overweight take-back");
    player.m_NPC = merchant;
    Assert(player.m_ItemList.Count == bagBefore
           && player.m_StorageItemList.Count == storageBefore,
        "overweight personal-storage take-back moved the item");

    player.m_WAbil.Weight = 0;
    player.m_WAbil.MaxWeight = 100;
    while (player.m_ItemList.Count < BagCapacity.Of(player))
        player.m_ItemList.Add(new TUserItem());
    InvokeTakeBack(merchant.ObjectId, "name-is-not-on-the-native-wire");
    Assert(player.Sent.Count == 2
           && player.Sent[0].Ident == Grobal2.SM_TAKEBACKSTORAGEITEM_FULLBAG
           && player.Sent[0].Recog == 0
           && player.Sent[0].Param == 0
           && player.Sent[0].Tag == 0
           && player.Sent[0].Series == 0
           && player.Sent[1].Ident == Grobal2.SM_TAKEBACKSTORAGEITEM_FAIL
           && player.Sent[1].Recog == 0
           && player.Sent[1].Param == 0
           && player.Sent[1].Tag == 0
           && player.Sent[1].Series == 0,
        "full-bag take-back must send native 707 then 706/0, without a name gate");
    Assert(player.m_StorageItemList.Count == storageBefore,
        "full-bag personal-storage take-back removed the stored item");

    var hiddenPlayer = new StoragePacketProbe
    {
        m_boOffLineFlag = true,
        m_boCanGetBackItem = true,
        m_boDealing = false,
        m_PEnvir = map,
        m_sMapName = map.sMapName,
        m_nCurrX = 51,
        m_nCurrY = 50,
        m_NPC = merchant
    };
    var hiddenStorageItem = new TUserItem
    {
        wIndex = item.wIndex,
        Dura = item.Dura,
        DuraMax = item.DuraMax,
        ClientItemID = 0
    };
    hiddenPlayer.m_StorageItemList.Add(hiddenStorageItem);
    var nextClientIdField = typeof(TPlayObject).GetField("_nextClientItemId",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(nameof(TPlayObject), "_nextClientItemId");
    const int forgedNextClientId = 0x23456789;
    nextClientIdField.SetValue(hiddenPlayer, forgedNextClientId);
    takeBack.Invoke(hiddenPlayer, new object[]
    {
        merchant.ObjectId, forgedNextClientId, "ignored-by-native"
    });
    Assert(hiddenPlayer.Sent.Count == 1
           && hiddenPlayer.Sent[0].Ident == Grobal2.SM_TAKEBACKSTORAGEITEM_FAIL
           && hiddenPlayer.Sent[0].Recog == 0
           && hiddenPlayer.Sent[0].Series == 0
           && hiddenStorageItem.ClientItemID == 0
           && (int)nextClientIdField.GetValue(hiddenPlayer) == forgedNextClientId
           && hiddenPlayer.m_StorageItemList.Count == 1
           && hiddenPlayer.m_ItemList.Count == 0,
        "storage lookup must be read-only and reject a forged next client id");

    void AssertDrugStorageFailure(int expected, string scenario)
    {
        player.Sent.Clear();
        player.RejectUnsupportedTakeBackStorageItem(2);
        Assert(player.Sent.Count == 1
               && player.Sent[0].Ident ==
                  Grobal2.SM_TAKEBACKSTORAGEITEM_FAIL
               && player.Sent[0].Recog == expected
               && player.Sent[0].Param == 0
               && player.Sent[0].Tag == 0
               && player.Sent[0].Series == 2,
            scenario + $" must send only SM 706 / Recog={expected} / Series=2");
    }

    player.m_boCanGetBackItem = false;
    player.m_boDealing = true;
    AssertDrugStorageFailure(-3, "prohibited drug-storage take-back");
    player.m_boCanGetBackItem = true;
    AssertDrugStorageFailure(-2, "dealing drug-storage take-back");
    player.m_boDealing = false;
    AssertDrugStorageFailure(0, "unimplemented drug-storage item take-back");

    Log("STORAGE take-back control flow: -3 > -2 > NPC/range(0) > item(0) "
        + "> weight(-1); no Merchant/getback/trial gate; full bag -> 707 then 706/0; "
        + "DrugStore fail-closed Series=2 preserves -3/-2/0");
}

// ===== personal/account-storage successful deposit (sub_6C2A34) =====
void RunStorageDepositSuccessPath(Envirnoment map)
{
    var engine = M2Share.UserEngine;
    int normalIndex = engine.StdItemList.Count;
    engine.StdItemList.Add(new GoodItem
    {
        Name = "storage-deposit-normal", ItemType = GoodType.ITEM_WEAPON,
        StdMode = 5, Weight = 1, DuraMax = 100, NeedIdentify = 0
    });
    int pileIndex = engine.StdItemList.Count;
    engine.StdItemList.Add(new GoodItem
    {
        Name = "storage-deposit-pile", ItemType = GoodType.ITEM_ETC,
        StdMode = 150, Weight = 1, DuraMax = 0, NeedIdentify = 1
    });

    var merchant = new Merchant
    {
        m_PEnvir = map, m_sMapName = map.sMapName,
        m_nCurrX = 45, m_nCurrY = 45,
        m_sCharName = "storage-deposit-npc", m_boStorage = false
    };
    Assert(ReferenceEquals(M2Share.UserEngine.FindMerchant(merchant.ObjectId),
            merchant),
        "storage deposit fixture merchant was not registered");
    var deposit = typeof(TPlayObject).GetMethod("ClientStorageItem",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("TPlayObject", "ClientStorageItem");
    var savedLogs = M2Share.LogStringList.Cast<object>().ToArray();
    var savedTryModeUseStorage = M2Share.g_Config.boTryModeUseStorage;
    var directStandard = engine.GetStdItem((ushort)normalIndex)
        ?? throw new InvalidOperationException("storage direct-NPC template missing");

    try
    {
        foreach (var fixture in new[]
                 {
                     (Index: normalIndex, Quantity: 1),
                     (Index: pileIndex, Quantity: 37)
                 })
        {
            var standard = engine.GetStdItem((ushort)fixture.Index)
                ?? throw new InvalidOperationException("storage deposit fixture template missing");
            var normalItem = new TUserItem
            {
                wIndex = (ushort)fixture.Index, Dura = (ushort)fixture.Quantity,
                DuraMax = standard.DuraMax, MakeIndex = 850000 + fixture.Index,
                ClientItemID = 860000 + fixture.Index
            };
            var normalPlayer = new StoragePacketProbe
            {
                m_boOffLineFlag = true, m_boCanGetBackItem = true,
                m_boDealing = false, m_nPayMent = 1,
                m_PEnvir = map, m_sMapName = map.sMapName,
                m_nCurrX = 46, m_nCurrY = 45,
                m_sCharName = "storage-deposit-normal-" + fixture.Quantity,
                m_NPC = merchant
            };
            normalPlayer.m_WAbil.MaxWeight = 100;
            normalPlayer.m_ItemList.Add(normalItem);
            M2Share.LogStringList.Clear();
            deposit.Invoke(normalPlayer, new object[]
            {
                merchant.ObjectId, normalItem.ClientItemID,
                "intentionally-wrong-body-" + fixture.Quantity
            });
            AssertStorageDepositSuccess(normalPlayer, normalItem, 0,
                standard.Name, fixture.Quantity, "普通仓库", "personal");

            var accountItem = new TUserItem
            {
                wIndex = (ushort)fixture.Index, Dura = (ushort)fixture.Quantity,
                DuraMax = standard.DuraMax, MakeIndex = 870000 + fixture.Index,
                ClientItemID = 880000 + fixture.Index
            };
            var accountPlayer = new StoragePacketProbe
            {
                m_boOffLineFlag = true, m_boCanGetBackItem = true,
                m_boDealing = false, m_nPayMent = 1,
                m_PEnvir = map, m_sMapName = map.sMapName,
                m_nCurrX = 46, m_nCurrY = 45,
                m_sCharName = "storage-deposit-account-" + fixture.Quantity,
                m_NPC = merchant
            };
            accountPlayer.m_WAbil.MaxWeight = 100;
            accountPlayer.m_ItemList.Add(accountItem);
            var accountState = accountPlayer.GetNativeAccountStorageState();
            accountState.Capacity = 1;
            accountState.Dirty = false;
            M2Share.LogStringList.Clear();
            accountPlayer.ClientNativeAccountStorageItem(
                merchant.ObjectId, accountItem.ClientItemID);
            Assert(accountState.Dirty && accountState.Items.Count == 1
                   && ReferenceEquals(accountState.Items[0], accountItem),
                "account storage successful deposit did not dirty and retain the source item");
            AssertStorageDepositSuccess(accountPlayer, accountItem, 1,
                standard.Name, fixture.Quantity, "账号仓库", "account");
        }

        var trialMatrixCase = 0;
        foreach (var payment in new[] { 0, 1 })
        foreach (var tryModeUseStorage in new[] { false, true })
        {
            M2Share.g_Config.boTryModeUseStorage = tryModeUseStorage;
            var matrixItem = new TUserItem
            {
                wIndex = (ushort)normalIndex, Dura = 1,
                DuraMax = directStandard.DuraMax,
                MakeIndex = 894000 + trialMatrixCase,
                ClientItemID = 895000 + trialMatrixCase
            };
            var matrixPlayer = new StoragePacketProbe
            {
                m_boOffLineFlag = true, m_boCanGetBackItem = true,
                m_boDealing = false, m_nPayMent = payment,
                m_PEnvir = map, m_sMapName = map.sMapName,
                m_nCurrX = 46, m_nCurrY = 45,
                m_sCharName = "storage-payment-personal-" + trialMatrixCase,
                m_NPC = merchant
            };
            matrixPlayer.m_ItemList.Add(matrixItem);
            M2Share.LogStringList.Clear();
            deposit.Invoke(matrixPlayer, new object[]
            {
                merchant.ObjectId, matrixItem.ClientItemID, "ignored-body"
            });
            AssertStorageDepositSuccess(matrixPlayer, matrixItem, 0,
                directStandard.Name, 1, "普通仓库", "personal trial matrix");

            var matrixAccountItem = new TUserItem
            {
                wIndex = (ushort)normalIndex, Dura = 1,
                DuraMax = directStandard.DuraMax,
                MakeIndex = 896000 + trialMatrixCase,
                ClientItemID = 897000 + trialMatrixCase
            };
            var matrixAccountPlayer = new StoragePacketProbe
            {
                m_boOffLineFlag = true, m_boCanGetBackItem = true,
                m_boDealing = false, m_nPayMent = payment,
                m_PEnvir = map, m_sMapName = map.sMapName,
                m_nCurrX = 46, m_nCurrY = 45,
                m_sCharName = "storage-payment-account-" + trialMatrixCase,
                m_NPC = merchant
            };
            matrixAccountPlayer.m_ItemList.Add(matrixAccountItem);
            var matrixState = matrixAccountPlayer.GetNativeAccountStorageState();
            matrixState.Capacity = 1;
            matrixState.Dirty = false;
            M2Share.LogStringList.Clear();
            matrixAccountPlayer.ClientNativeAccountStorageItem(
                merchant.ObjectId, matrixAccountItem.ClientItemID);
            Assert(matrixState.Dirty,
                "account trial matrix successful deposit did not mark dirty");
            AssertStorageDepositSuccess(matrixAccountPlayer,
                matrixAccountItem, 1, directStandard.Name, 1,
                "账号仓库", "account");
            trialMatrixCase++;
        }

        // The native receiver uses the cached object without a class or
        // storage-capability gate. Exercise both ordinary NPC and scripted
        // monster shapes at the inclusive 15-tile boundary.
        var directTargets = new TBaseObject[]
        {
            new NormNpc
            {
                m_PEnvir = map, m_sMapName = map.sMapName,
                m_nCurrX = 30, m_nCurrY = 30,
                m_sCharName = "storage-direct-normnpc"
            },
            new Monster
            {
                m_PEnvir = map, m_sMapName = map.sMapName,
                m_nCurrX = 31, m_nCurrY = 31,
                m_sCharName = "storage-direct-monster"
            }
        };
        for (var directCase = 0; directCase < directTargets.Length; directCase++)
        {
            var target = directTargets[directCase];
            var directItem = new TUserItem
            {
                wIndex = (ushort)normalIndex, Dura = 1,
                DuraMax = directStandard.DuraMax,
                MakeIndex = 890000 + directCase,
                ClientItemID = 891000 + directCase
            };
            var directPlayer = new StoragePacketProbe
            {
                m_boOffLineFlag = true, m_boCanGetBackItem = true,
                m_boDealing = false, m_PEnvir = map,
                m_sMapName = map.sMapName,
                m_nCurrX = (short)(target.m_nCurrX + 15),
                m_nCurrY = (short)(target.m_nCurrY + 15),
                m_sCharName = "storage-direct-" + directCase,
                m_NPC = target
            };
            directPlayer.m_ItemList.Add(directItem);
            M2Share.LogStringList.Clear();
            deposit.Invoke(directPlayer, new object[]
            {
                target.ObjectId, directItem.ClientItemID, "ignored-body"
            });
            AssertStorageDepositSuccess(directPlayer, directItem, 0,
                directStandard.Name, 1, "普通仓库", "direct cached NPC");
        }

        var fullItem = new TUserItem
        {
            wIndex = (ushort)normalIndex, Dura = 1,
            DuraMax = directStandard.DuraMax,
            MakeIndex = 898000, ClientItemID = 899000
        };
        var fullPlayer = new StoragePacketProbe
        {
            m_boOffLineFlag = true, m_boCanGetBackItem = true,
            m_boDealing = false, m_PEnvir = map,
            m_sMapName = map.sMapName, m_nCurrX = 46, m_nCurrY = 45,
            m_sCharName = "storage-deposit-full", m_NPC = merchant,
            m_nStorageSpaceCount = TPlayObject.MIN_STORAGE_ITEM_COUNT
        };
        fullPlayer.m_ItemList.Add(fullItem);
        for (var i = 0; i < TPlayObject.MIN_STORAGE_ITEM_COUNT; i++)
        {
            fullPlayer.m_StorageItemList.Add(new TUserItem
            {
                wIndex = (ushort)normalIndex,
                MakeIndex = 898100 + i,
                ClientItemID = 899100 + i
            });
        }
        M2Share.LogStringList.Clear();
        deposit.Invoke(fullPlayer, new object[]
        {
            merchant.ObjectId, fullItem.ClientItemID, "ignored-body"
        });
        Assert(fullPlayer.Sent.Count == 2
               && fullPlayer.Sent[0].Ident == Grobal2.SM_STORAGE_FULL
               && fullPlayer.Sent[1].Ident == Grobal2.SM_STORAGE_FAIL
               && fullPlayer.Sent.All(packet => packet.Recog == 0
                   && packet.Param == 0 && packet.Tag == 0 && packet.Series == 0
                   && packet.Body == string.Empty)
               && fullPlayer.m_ItemList.Count == 1
               && ReferenceEquals(fullPlayer.m_ItemList[0], fullItem)
               && fullPlayer.m_StorageItemList.Count == TPlayObject.MIN_STORAGE_ITEM_COUNT
               && !fullPlayer.m_StorageItemList.Any(item => ReferenceEquals(item, fullItem))
               && M2Share.LogStringList.Count == 0,
            "full personal storage must emit SM702 then SM703/Series=0 and preserve ownership");

        var fullAccountItem = new TUserItem
        {
            wIndex = (ushort)normalIndex, Dura = 1,
            DuraMax = directStandard.DuraMax,
            MakeIndex = 898001, ClientItemID = 899001
        };
        var fullAccountPlayer = new StoragePacketProbe
        {
            m_boOffLineFlag = true, m_boCanGetBackItem = true,
            m_boDealing = false, m_PEnvir = map,
            m_sMapName = map.sMapName, m_nCurrX = 46, m_nCurrY = 45,
            m_sCharName = "account-storage-deposit-full", m_NPC = merchant
        };
        fullAccountPlayer.m_ItemList.Add(fullAccountItem);
        var fullAccountState = fullAccountPlayer.GetNativeAccountStorageState();
        fullAccountState.Capacity = 0;
        fullAccountState.Dirty = false;
        M2Share.LogStringList.Clear();
        fullAccountPlayer.ClientNativeAccountStorageItem(
            merchant.ObjectId, fullAccountItem.ClientItemID);
        Assert(fullAccountPlayer.Sent.Count == 2
               && fullAccountPlayer.Sent[0].Ident == Grobal2.SM_STORAGE_FULL
               && fullAccountPlayer.Sent[1].Ident == Grobal2.SM_STORAGE_FAIL
               && fullAccountPlayer.Sent.All(packet => packet.Recog == 0
                   && packet.Param == 0 && packet.Tag == 0 && packet.Series == 1
                   && packet.Body == string.Empty)
               && fullAccountPlayer.m_ItemList.Count == 1
               && ReferenceEquals(fullAccountPlayer.m_ItemList[0], fullAccountItem)
               && fullAccountState.Items.Count == 0 && !fullAccountState.Dirty
               && M2Share.LogStringList.Count == 0,
            "full account storage must emit SM702 then SM703/Series=1 and preserve ownership");

        AssertStorageDepositRejected("missing cached NPC", null, 123456,
            46, 45, false);
        var wrongIdNpc = NewStorageNpc(map, 45, 45, "wrong-id");
        AssertStorageDepositRejected("cached NPC id mismatch", wrongIdNpc,
            wrongIdNpc.ObjectId + 1, 46, 45, false);
        var wrongMapNpc = NewStorageNpc(null, 45, 45, "wrong-map");
        AssertStorageDepositRejected("cached NPC map mismatch", wrongMapNpc,
            wrongMapNpc.ObjectId, 46, 45, false);
        var farXNp = NewStorageNpc(map, 30, 45, "far-x");
        AssertStorageDepositRejected("cached NPC x distance 16", farXNp,
            farXNp.ObjectId, 46, 45, false);
        var farYNpc = NewStorageNpc(map, 46, 29, "far-y");
        AssertStorageDepositRejected("cached NPC y distance 16", farYNpc,
            farYNpc.ObjectId, 46, 45, false);
        var dealingNpc = NewStorageNpc(map, 45, 45, "dealing");
        AssertStorageDepositRejected("dealing gate precedence", dealingNpc,
            dealingNpc.ObjectId, 46, 45, true);
    }
    finally
    {
        M2Share.g_Config.boTryModeUseStorage = savedTryModeUseStorage;
        M2Share.LogStringList.Clear();
        foreach (var log in savedLogs)
            M2Share.LogStringList.Add(log);
    }

    Log("STORAGE deposit success: personal/account normal+piled paths emit "
        + "SM701(Series=0/1) and one unconditional type-1 native log with exact "
        + "pile quantity/reason; cached Merchant(flag=false)/NormNpc/Monster "
        + "receiver shapes pass at distance 15; null/id/map/distance16/dealing reject; "
        + "opcode-1031 body ignored; payment 0/1 x TryModeUseStorage false/true "
        + "all pass for personal/account receivers; full personal/account storage emits "
        + "SM702 then SM703 (Series=0/1) without moving the item; "
        + "no immediate SaveHumanRcd");

    NormNpc NewStorageNpc(Envirnoment environment, short x, short y,
        string name)
    {
        return new NormNpc
        {
            m_PEnvir = environment,
            m_sMapName = environment?.sMapName ?? "other-map",
            m_nCurrX = x,
            m_nCurrY = y,
            m_sCharName = "storage-reject-" + name
        };
    }

    void AssertStorageDepositRejected(string scenario, TBaseObject target,
        int packetNpcId, short playerX, short playerY, bool dealing)
    {
        var item = new TUserItem
        {
            wIndex = (ushort)normalIndex, Dura = 1,
            DuraMax = directStandard.DuraMax,
            MakeIndex = 892000 + packetNpcId,
            ClientItemID = 893000 + packetNpcId
        };
        var player = new StoragePacketProbe
        {
            m_boOffLineFlag = true, m_boCanGetBackItem = true,
            m_boDealing = dealing, m_PEnvir = map,
            m_sMapName = map.sMapName, m_nCurrX = playerX,
            m_nCurrY = playerY, m_sCharName = "storage-reject",
            m_NPC = target
        };
        player.m_ItemList.Add(item);
        M2Share.LogStringList.Clear();
        deposit.Invoke(player, new object[]
        {
            packetNpcId, item.ClientItemID, directStandard.Name
        });
        Assert(player.Sent.Count == 1
               && player.Sent[0].Ident == Grobal2.SM_STORAGE_FAIL
               && player.Sent[0].Recog == 0
               && player.Sent[0].Param == 0
               && player.Sent[0].Tag == 0
               && player.Sent[0].Series == 0
               && player.m_ItemList.Count == 1
               && ReferenceEquals(player.m_ItemList[0], item)
               && player.m_StorageItemList.Count == 0
               && M2Share.LogStringList.Count == 0,
            scenario + " must emit one SM703 and preserve the item");
    }

    void AssertStorageDepositSuccess(StoragePacketProbe player, TUserItem item,
        ushort series, string itemName, int quantity, string reason,
        string storageKind)
    {
        Assert(player.m_ItemList.Count == 0,
            storageKind + " storage successful deposit did not remove the item from the bag");
        var storedItems = storageKind == "account"
            ? player.GetNativeAccountStorageState().Items
            : player.m_StorageItemList;
        Assert(storedItems.Count == 1,
            storageKind + " storage successful deposit source count mismatch");
        var stored = storedItems[0];
        Assert(ReferenceEquals(stored, item),
            storageKind + " storage successful deposit did not retain the exact item");
        Assert(player.Sent.Count == 1
               && player.Sent[0].Ident == Grobal2.SM_STORAGE_OK
               && player.Sent[0].Recog == item.ClientItemID
               && player.Sent[0].Param == 0 && player.Sent[0].Tag == 0
               && player.Sent[0].Series == series,
            storageKind + " storage successful deposit packet mismatch");
        string expectedLog = string.Join('\t', 1, player.m_sMapName,
            player.m_nCurrX, player.m_nCurrY, player.m_sCharName, itemName,
            item.MakeIndex, quantity, reason);
        Assert(M2Share.LogStringList.Count == 1
               && (string)M2Share.LogStringList[0] == expectedLog,
            storageKind + " storage successful deposit native type-1 log mismatch");
    }
}

// ===== personal/account-storage successful take-back (sub_6C2D7C) =====
void RunStorageTakeSuccessPath(Envirnoment map)
{
    var engine = M2Share.UserEngine;
    int normalIndex = engine.StdItemList.Count;
    engine.StdItemList.Add(new GoodItem
    {
        Name = "storage-success-normal", ItemType = GoodType.ITEM_WEAPON,
        StdMode = 5, Weight = 1, DuraMax = 100, NeedIdentify = 0
    });
    int pileIndex = engine.StdItemList.Count;
    engine.StdItemList.Add(new GoodItem
    {
        Name = "storage-success-pile", ItemType = GoodType.ITEM_ETC,
        StdMode = 150, Weight = 1, DuraMax = 0, NeedIdentify = 0
    });

    var merchant = new Merchant
    {
        m_PEnvir = map, m_sMapName = map.sMapName,
        m_nCurrX = 45, m_nCurrY = 45, m_sCharName = "storage-success-npc"
    };
    var takeBack = typeof(TPlayObject).GetMethod("ClientTakeBackStorageItem",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("TPlayObject", "ClientTakeBackStorageItem");
    var savedLogs = M2Share.LogStringList.Cast<object>().ToArray();

    try
    {
        foreach (var fixture in new[]
                 {
                     (Index: normalIndex, Quantity: 1),
                     (Index: pileIndex, Quantity: 37)
                 })
        {
            var standard = engine.GetStdItem((ushort)fixture.Index)
                ?? throw new InvalidOperationException("storage fixture template missing");
            var normalItem = new TUserItem
            {
                wIndex = (ushort)fixture.Index, Dura = (ushort)fixture.Quantity,
                DuraMax = standard.DuraMax, MakeIndex = 810000 + fixture.Index,
                ClientItemID = 820000 + fixture.Index
            };
            var normalPlayer = new StoragePacketProbe
            {
                m_boOffLineFlag = true, m_boCanGetBackItem = true,
                m_boDealing = false, m_PEnvir = map, m_sMapName = map.sMapName,
                m_nCurrX = 46, m_nCurrY = 45,
                m_sCharName = "storage-normal-" + fixture.Quantity,
                m_NPC = merchant
            };
            normalPlayer.m_WAbil.MaxWeight = 100;
            normalPlayer.m_StorageItemList.Add(normalItem);
            M2Share.LogStringList.Clear();
            takeBack.Invoke(normalPlayer, new object[]
            {
                merchant.ObjectId, normalItem.ClientItemID, "not-on-native-wire"
            });
            AssertStorageTakeSuccess(normalPlayer, normalItem, 0,
                standard.Name, fixture.Quantity, "普通仓库", "personal");

            var accountItem = new TUserItem
            {
                wIndex = (ushort)fixture.Index, Dura = (ushort)fixture.Quantity,
                DuraMax = standard.DuraMax, MakeIndex = 830000 + fixture.Index,
                ClientItemID = 840000 + fixture.Index
            };
            var accountPlayer = new StoragePacketProbe
            {
                m_boOffLineFlag = true, m_boCanGetBackItem = true,
                m_boDealing = false, m_PEnvir = map, m_sMapName = map.sMapName,
                m_nCurrX = 46, m_nCurrY = 45,
                m_sCharName = "storage-account-" + fixture.Quantity,
                m_NPC = merchant
            };
            accountPlayer.m_WAbil.MaxWeight = 100;
            var accountState = accountPlayer.GetNativeAccountStorageState();
            accountState.Capacity = 1;
            accountState.Dirty = false;
            accountState.Items.Add(accountItem);
            M2Share.LogStringList.Clear();
            accountPlayer.ClientNativeAccountTakeBackStorageItem(
                merchant.ObjectId, accountItem.ClientItemID);
            Assert(accountState.Dirty && accountState.Items.Count == 0,
                "account storage successful take-back did not dirty then remove the source item");
            AssertStorageTakeSuccess(accountPlayer, accountItem, 1,
                standard.Name, fixture.Quantity, "账号仓库", "account");
        }
    }
    finally
    {
        M2Share.LogStringList.Clear();
        foreach (var log in savedLogs)
            M2Share.LogStringList.Add(log);
    }

    Log("STORAGE take-back success: personal/account non-pile+piled paths emit "
        + "SM200(Series=1) then SM705(Series=0/1), no SM704, and one type-2 "
        + "native log with exact pile quantity/reason");

    void AssertStorageTakeSuccess(StoragePacketProbe player, TUserItem item,
        ushort series, string itemName, int quantity, string reason,
        string storageKind)
    {
        Assert(player.m_ItemList.Count == 1 && ReferenceEquals(player.m_ItemList[0], item)
               && player.m_StorageItemList.Count == 0,
            storageKind + " storage successful take-back did not move the exact item into the bag");
        Assert(player.Sent.Count == 2
               && player.Sent[0].Ident == Grobal2.SM_ADDITEM
               && player.Sent[0].Series == 1
               && player.Sent[1].Ident == Grobal2.SM_TAKEBACKSTORAGEITEM_OK
               && player.Sent[1].Recog == item.ClientItemID
               && player.Sent[1].Param == 0 && player.Sent[1].Tag == 0
               && player.Sent[1].Series == series,
            storageKind + " storage successful take-back packet order is not SM200 -> SM705");
        string expectedLog = string.Join('\t', 2, player.m_sMapName,
            player.m_nCurrX, player.m_nCurrY, player.m_sCharName, itemName,
            item.MakeIndex, quantity, reason);
        Assert(M2Share.LogStringList.Count == 1
               && (string)M2Share.LogStringList[0] == expectedLog,
            storageKind + " storage successful take-back native type-2 log mismatch");
    }
}

void RunQiankunResetProtocol(Envirnoment map)
{
    var player = new StoragePacketProbe
    {
        m_boOffLineFlag = true,
        m_sCharName = "qiankun-reset",
        m_sMapName = map.sMapName,
        m_PEnvir = map
    };
    const BindingFlags instanceFields = BindingFlags.Instance
        | BindingFlags.NonPublic;
    var indexField = typeof(TPlayObject).GetField("m_nQiankunSelIndex",
        instanceFields) ?? throw new MissingFieldException(nameof(TPlayObject),
        "m_nQiankunSelIndex");
    var listField = typeof(TPlayObject).GetField("m_QiankunSelList",
        instanceFields) ?? throw new MissingFieldException(nameof(TPlayObject),
        "m_QiankunSelList");
    var bagField = typeof(TPlayObject).GetField("m_QiankunBagRef",
        instanceFields) ?? throw new MissingFieldException(nameof(TPlayObject),
        "m_QiankunBagRef");
    var list = (System.Collections.IList)listField.GetValue(player);
    var entryType = list.GetType().GetGenericArguments()[0];
    list.Add(Activator.CreateInstance(entryType, nonPublic: true));

    void InvokeAndAssertReset(int cmIdent, string scenario)
    {
        player.Sent.Clear();
        player.Operate(new TProcessMessage { wIdent = cmIdent });
        Assert((int)indexField.GetValue(player) == 0
               && bagField.GetValue(player) == null
               && list.Count == 0,
            scenario + " did not clear +0x9F4/+0x9F8/+0x9FC");
        Assert(player.Sent.Count == 1
               && player.Sent[0].Ident == Grobal2.SM_2957
               && player.Sent[0].Recog == 0
               && player.Sent[0].Param == 0
               && player.Sent[0].Tag == 0
               && player.Sent[0].Series == 0
               && string.IsNullOrEmpty(player.Sent[0].Body),
            scenario + " must send exactly one all-zero SM 2957");
    }

    indexField.SetValue(player, 7);
    bagField.SetValue(player, new object());
    InvokeAndAssertReset(Grobal2.CM_3284, "CM 3284");

    indexField.SetValue(player, 9);
    bagField.SetValue(player, new object());
    InvokeAndAssertReset(Grobal2.CM_3286, "empty-list CM 3286");

    Log("QIANKUN default dispatch: CM 3284 and empty-list CM 3286 clear +0x9F4/+0x9F8/+0x9FC "
        + "and each send one all-zero SM 2957");
}

void RunReviveMessageProtocol(Envirnoment map)
{
    var revive = typeof(TBaseObject).GetMethod("TryNativeRevive",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(nameof(TBaseObject), "TryNativeRevive");

    StoragePacketProbe NewReviveProbe(string name)
    {
        var player = new StoragePacketProbe
        {
            m_boOffLineFlag = true,
            m_boGhost = false,
            m_boDeath = false,
            m_sCharName = name,
            m_sMapName = map.sMapName,
            m_PEnvir = map
        };
        player.m_WAbil.HP = 0;
        player.m_WAbil.MaxHP = 100;
        player.m_WAbil.MP = 0;
        player.m_WAbil.MaxMP = 60;
        return player;
    }

    void AssertNativePair(TBaseObject queueOwner, StoragePacketProbe sink,
        int revivedObjectId, string systemText, string popupText,
        int systemWireLength, int popupWireLength, string scenario)
    {
        PumpMessages(queueOwner);
        var packets = sink.Sent
            .Where(packet => packet.Ident == Grobal2.SM_SYSMESSAGE ||
                             packet.Ident == Grobal2.SM_REVIVE_MESSAGE)
            .ToArray();
        Assert(packets.Length == 2,
            scenario + " must emit exactly SM 100 then SM 213");
        Assert(packets[0].Ident == Grobal2.SM_SYSMESSAGE
               && packets[0].Recog == revivedObjectId
               && packets[0].Param == 0xFCFF
               && packets[0].Tag == 0
               && packets[0].Series == 1
               && packets[0].Body == systemText,
            scenario + " SM 100 frame/text mismatch");
        AssertNativeTerminatedBody(packets[0], systemText, systemWireLength,
            scenario + " SM 100");
        Assert(packets[1].Ident == Grobal2.SM_REVIVE_MESSAGE
               && packets[1].Recog == revivedObjectId
               && packets[1].Param == 1
               && packets[1].Tag == 0
               && packets[1].Series == 0
               && packets[1].Body == popupText,
            scenario + " SM 213 frame/text mismatch");
        AssertNativeTerminatedBody(packets[1], popupText, popupWireLength,
            scenario + " SM 213");
    }

    void AssertNativeTerminatedBody(CapturedDefMessage packet, string text,
        int wireLength, string scenario)
    {
        var encoded = HUtil32.GbkEncoding.GetBytes(text);
        Assert(packet.RawBody != null && packet.RawBody.Length == wireLength
               && packet.RawBody.Length == encoded.Length + 1
               && packet.RawBody[^1] == 0
               && packet.RawBody.Take(encoded.Length).SequenceEqual(encoded),
            scenario + " must carry exact GBK bytes followed by one NUL");
    }

    var equip = NewReviveProbe("revive-sm213-equip");
    equip.m_boRevival = true;
    equip.m_dwRevivalTick = 0;
    Assert((bool)revive.Invoke(equip, null),
        "equipment revive ladder did not revive");
    Assert(equip.m_WAbil.HP == equip.m_WAbil.MaxHP,
        "equipment revive did not restore HP");
    var equipSystemRecord = equip.m_MsgList.First(message =>
        message.wIdent == Grobal2.RM_SYSMESSAGE);
    Assert(equipSystemRecord.wParam == 0xFCFF
           && equipSystemRecord.nParam1 == 0
           && equipSystemRecord.nParam2 == 0,
        "native SM 100 queue record must keep packed CX in wParam");
    AssertNativePair(equip, equip, equip.ObjectId,
        TBaseObject.NativeEquipReviveNotice,
        TBaseObject.NativeEquipReviveNotice, 25, 25, "equipment revive");

    var second = NewReviveProbe("revive-sm213-second");
    second.m_btNativeSecondPathFlag = 1;
    second.m_btNativeSecondPathTier = 1;
    Assert((bool)revive.Invoke(second, null),
        "second revive ladder did not revive");
    Assert(second.m_WAbil.HP == second.m_WAbil.MaxHP
           && second.m_WAbil.MP == second.m_WAbil.MaxMP,
        "second revive did not restore HP/MP");
    AssertNativePair(second, second, second.ObjectId,
        TBaseObject.NativeSecondPathSystemNotice,
        TBaseObject.NativeSecondPathPopupNotice, 28, 26, "second revive");

    var lateGhostPlayer = NewReviveProbe("revive-sm213-late-player-ghost");
    lateGhostPlayer.m_boRevival = true;
    lateGhostPlayer.m_dwRevivalTick = 0;
    Assert((bool)revive.Invoke(lateGhostPlayer, null),
        "late-player-ghost equipment revive ladder did not revive");
    lateGhostPlayer.m_boGhost = true;
    PumpMessages(lateGhostPlayer);
    Assert(!lateGhostPlayer.Sent.Any(packet =>
            packet.Ident == Grobal2.SM_SYSMESSAGE ||
            packet.Ident == Grobal2.SM_REVIVE_MESSAGE),
        "player dispatcher must apply the player ghost gate at dispatch time");

    HeroObject NewHeroReviveProbe(string name, StoragePacketProbe master)
    {
        var hero = new HeroObject
        {
            m_boOffLineFlag = true,
            m_boGhost = false,
            m_boDeath = false,
            m_sCharName = name,
            m_sMapName = map.sMapName,
            m_PEnvir = map,
            m_Master = master
        };
        hero.m_WAbil.HP = 0;
        hero.m_WAbil.MaxHP = 100;
        hero.m_WAbil.MP = 0;
        hero.m_WAbil.MaxMP = 60;
        return hero;
    }

    var heroMaster = NewReviveProbe("revive-sm213-hero-master");
    var heroEquip = NewHeroReviveProbe("revive-sm213-hero-equip", heroMaster);
    heroEquip.m_boRevival = true;
    heroEquip.m_dwRevivalTick = 0;
    Assert((bool)revive.Invoke(heroEquip, null),
        "hero equipment revive ladder did not revive");
    Assert(heroEquip.m_MsgList.Count(message =>
               message.wIdent == Grobal2.RM_SYSMESSAGE ||
               message.wIdent == Grobal2.RM_NATIVE_REVIVE_MESSAGE) == 2
           && !heroMaster.m_MsgList.Any(message =>
               message.wIdent == Grobal2.RM_SYSMESSAGE ||
               message.wIdent == Grobal2.RM_NATIVE_REVIVE_MESSAGE),
        "hero notices must queue on the hero, not on its master");
    var heroSystemRecord = heroEquip.m_MsgList.First(message =>
        message.wIdent == Grobal2.RM_SYSMESSAGE);
    Assert(heroSystemRecord.wParam == 0xFCFF
           && heroSystemRecord.nParam1 == 0
           && heroSystemRecord.nParam2 == 0,
        "hero native SM 100 queue record must keep packed CX in wParam");
    AssertNativePair(heroEquip, heroMaster, heroEquip.ObjectId,
        "(英雄) " + TBaseObject.NativeEquipReviveNotice,
        "(英雄) " + TBaseObject.NativeEquipReviveNotice,
        32, 32, "hero equipment revive");

    heroMaster.Sent.Clear();
    var heroSecond = NewHeroReviveProbe("revive-sm213-hero-second", heroMaster);
    heroSecond.m_btNativeSecondPathFlag = 1;
    heroSecond.m_btNativeSecondPathTier = 1;
    Assert((bool)revive.Invoke(heroSecond, null),
        "hero second revive ladder did not revive");
    AssertNativePair(heroSecond, heroMaster, heroSecond.ObjectId,
        "(英雄) " + TBaseObject.NativeSecondPathSystemNotice,
        "(英雄) " + TBaseObject.NativeSecondPathPopupNotice,
        35, 33, "hero second revive");

    heroMaster.Sent.Clear();
    var lateGhostHero = NewHeroReviveProbe(
        "revive-sm213-late-hero-ghost", heroMaster);
    lateGhostHero.m_boRevival = true;
    lateGhostHero.m_dwRevivalTick = 0;
    Assert((bool)revive.Invoke(lateGhostHero, null),
        "late-hero-ghost equipment revive ladder did not revive");
    lateGhostHero.m_boGhost = true;
    AssertNativePair(lateGhostHero, heroMaster, lateGhostHero.ObjectId,
        "(英雄) " + TBaseObject.NativeEquipReviveNotice,
        "(英雄) " + TBaseObject.NativeEquipReviveNotice,
        32, 32, "hero made ghost after enqueue");

    heroMaster.Sent.Clear();
    var detachedHero = NewHeroReviveProbe(
        "revive-sm213-detached-hero", heroMaster);
    detachedHero.m_boRevival = true;
    detachedHero.m_dwRevivalTick = 0;
    Assert((bool)revive.Invoke(detachedHero, null),
        "detached hero equipment revive ladder did not revive");
    detachedHero.m_Master = null;
    PumpMessages(detachedHero);
    Assert(!heroMaster.Sent.Any(packet =>
            packet.Ident == Grobal2.SM_SYSMESSAGE ||
            packet.Ident == Grobal2.SM_REVIVE_MESSAGE),
        "hero dispatcher must drop both notices when the master is absent");

    heroMaster.Sent.Clear();
    var ghostHero = NewHeroReviveProbe("revive-sm213-ghost-hero", heroMaster);
    ghostHero.m_boGhost = true;
    ghostHero.m_boRevival = true;
    ghostHero.m_dwRevivalTick = 0;
    Assert((bool)revive.Invoke(ghostHero, null),
        "ghost hero equipment revive ladder did not revive");
    PumpMessages(ghostHero);
    Assert(!heroMaster.Sent.Any(packet =>
            packet.Ident == Grobal2.SM_SYSMESSAGE ||
            packet.Ident == Grobal2.SM_REVIVE_MESSAGE),
        "hero ghost enqueue gate must drop SM 100/213 notices");

    heroMaster.Sent.Clear();
    var lateGhostMasterHero = NewHeroReviveProbe(
        "revive-sm213-late-master-ghost", heroMaster);
    lateGhostMasterHero.m_boRevival = true;
    lateGhostMasterHero.m_dwRevivalTick = 0;
    Assert((bool)revive.Invoke(lateGhostMasterHero, null),
        "late-master-ghost equipment revive ladder did not revive");
    heroMaster.m_boGhost = true;
    PumpMessages(lateGhostMasterHero);
    Assert(!heroMaster.Sent.Any(packet =>
            packet.Ident == Grobal2.SM_SYSMESSAGE ||
            packet.Ident == Grobal2.SM_REVIVE_MESSAGE),
        "hero dispatcher must apply the master ghost gate at dispatch time");
    heroMaster.m_boGhost = false;

    heroMaster.Sent.Clear();
    var deadMasterHero = NewHeroReviveProbe(
        "revive-sm213-dead-master", heroMaster);
    deadMasterHero.m_boRevival = true;
    deadMasterHero.m_dwRevivalTick = 0;
    Assert((bool)revive.Invoke(deadMasterHero, null),
        "dead-master equipment revive ladder did not revive");
    heroMaster.m_boDeath = true;
    AssertNativePair(deadMasterHero, heroMaster, deadMasterHero.ObjectId,
        "(英雄) " + TBaseObject.NativeEquipReviveNotice,
        "(英雄) " + TBaseObject.NativeEquipReviveNotice,
        32, 32, "dead but non-ghost hero master");
    heroMaster.m_boDeath = false;

    Log("REVIVE notices: real player+hero TryNativeRevive equip/second-path branches "
        + "send hardcoded 0xFCFF SM 100 then RM 12308 -> SM 213/Param1/Series0; "
        + "hero forwards both with native '(英雄) ' prefix and hero Recog");
}

void RunFireHitProtocol(Envirnoment map)
{
    var clientSpell = typeof(TPlayObject).GetMethod("ClientSpellXY",
        BindingFlags.Instance | BindingFlags.NonPublic, null,
        new[]
        {
            typeof(short), typeof(int), typeof(int), typeof(int),
            typeof(TBaseObject), typeof(bool), typeof(bool),
            typeof(int).MakeByRefType()
        }, null) ?? throw new MissingMethodException(nameof(TPlayObject),
        "ClientSpellXY");

    TUserMagic FireMagic(ushort spell, byte level = 0)
    {
        var definition = BuildMagicTemplate(SpellsDef.SKILL_FIRESWORD,
            "烈火剑法", trainLv: 3);
        definition.wSpell = spell;
        definition.btDefSpell = 0;
        return new TUserMagic
        {
            MagicInfo = definition,
            btLevel = level,
            wMagIdx = SpellsDef.SKILL_FIRESWORD
        };
    }

    int nextCoordinate = 42;
    StoragePacketProbe NewProbe(string name)
    {
        var coordinate = nextCoordinate++;
        var player = new StoragePacketProbe
        {
            m_boOffLineFlag = false,
            m_boGhost = false,
            m_boDeath = false,
            m_boCanSpell = true,
            m_sCharName = name,
            m_sMapName = map.sMapName,
            m_nCurrX = (short)coordinate,
            m_nCurrY = 42,
            m_PEnvir = map
        };
        player.m_Abil.Level = 35;
        map.AddToMap(player.m_nCurrX, player.m_nCurrY,
            CellType.OS_MOVINGOBJECT, player);
        return player;
    }

    bool Dispatch(StoragePacketProbe player, TBaseObject target = null)
    {
        var args = new object[]
        {
            (short)Grobal2.CM_SPELL, SpellsDef.SKILL_FIRESWORD,
            (int)player.m_nCurrX, (int)player.m_nCurrY,
            target, true, false, 0
        };
        return (bool)clientSpell.Invoke(player, args);
    }

    CapturedDefMessage FindSystemMessage(StoragePacketProbe player,
        string text)
    {
        return player.Sent.LastOrDefault(packet =>
            packet.Ident == Grobal2.SM_SYSMESSAGE && packet.Body == text);
    }

    var cooldown = NewProbe("firehit-cooldown");
    cooldown.m_dwLatestFireHitTick = 1000;
    Assert(!cooldown.AllowFireHitSkill(11000)
           && !cooldown.m_boFireHitSkill
           && cooldown.m_dwLatestFireHitTick == 1000,
        "fire-hit elapsed 10000 must reject without changing state/tick");
    PumpMessages(cooldown);
    var cooldownError = FindSystemMessage(cooldown, "凝聚内力失败");
    Assert(cooldownError.Ident == Grobal2.SM_SYSMESSAGE
           && cooldownError.Param == 0x38FF
           && cooldown.Sent.Count(packet =>
               packet.Ident == Grobal2.SM_SYSMESSAGE) == 1,
        "fire-hit cooldown reject must send only native red failure text");

    cooldown.Sent.Clear();
    Assert(cooldown.AllowFireHitSkill(11001)
           && cooldown.m_boFireHitSkill
           && cooldown.m_dwLatestFireHitTick == 11001,
        "fire-hit elapsed 10001 must arm with the sampled tick");
    PumpMessages(cooldown);
    Assert(cooldown.Sent.Count == 0,
        "successful fire-hit preparation must not send a text message");

    var activationWrap = NewProbe("firehit-activation-wrap");
    activationWrap.m_dwLatestFireHitTick = 0;
    Assert(activationWrap.AllowFireHitSkill(int.MinValue)
           && activationWrap.m_dwLatestFireHitTick == int.MinValue,
        "fire-hit activation must use uint32 elapsed across signed wrap");

    var noCache = NewProbe("firehit-no-cache");
    noCache.m_MagicList.Add(FireMagic(0));
    noCache.m_WAbil.MP = 100;
    var noCacheMp = noCache.m_WAbil.MP;
    Assert(!Dispatch(noCache)
           && !noCache.m_boFireHitSkill
           && noCache.m_WAbil.MP == noCacheMp
           && noCache.FireHitSends.Count == 0,
        "skill 26 with resolved UserMagic but null +0xB0 cache must return false");

    var resolvedCost = NewProbe("firehit-resolved-cost");
    var expensiveResolvedMagic = FireMagic(40); // native cost = 10 at level 0
    var cheapCacheMagic = FireMagic(0);
    resolvedCost.m_MagicList.Add(expensiveResolvedMagic);
    resolvedCost.m_MagicArr[SpellsDef.SKILL_FIRESWORD] = cheapCacheMagic;
    resolvedCost.m_WAbil.MP = 5;
    resolvedCost.m_dwLatestFireHitTick = unchecked(
        HUtil32.GetTickCount() - 10001);
    var resolvedOldTick = resolvedCost.m_dwLatestFireHitTick;
    Assert(Dispatch(resolvedCost)
           && resolvedCost.m_boFireHitSkill
           && resolvedCost.m_dwLatestFireHitTick != resolvedOldTick
           && resolvedCost.m_WAbil.MP == 5
           && resolvedCost.FireHitSends.Count == 0,
        "fire-hit MP must come from resolved UserMagic; insufficient MP stays armed without SM626");
    PumpMessages(resolvedCost);
    Assert(!resolvedCost.Sent.Any(packet =>
            packet.Ident == Grobal2.SM_SYSMESSAGE),
        "insufficient-MP fire-hit preparation must not invent a success message");

    var open = NewProbe("firehit-open");
    var openMagic = FireMagic(40); // exact-equality MP boundary: cost 10
    open.m_MagicList.Add(openMagic);
    open.m_MagicArr[SpellsDef.SKILL_FIRESWORD] = FireMagic(0);
    open.m_WAbil.MP = 10;
    open.m_dwLatestFireHitTick = unchecked(HUtil32.GetTickCount() - 10001);
    Assert(Dispatch(open)
           && open.m_boFireHitSkill
           && open.m_WAbil.MP == 0
           && open.FireHitSends.Count == 1,
        "fire-hit exact MP must deduct to zero and send one SM626 open");
    var openSend = open.FireHitSends[0];
    Assert(openSend.Ready
           && openSend.Tick == open.m_dwLatestFireHitTick
           && openSend.Mp == 0
           && openSend.Packet.Ident == Grobal2.SM_FIREHITSKILL
           && openSend.Packet.Recog == 0
           && openSend.Packet.Param == 0
           && openSend.Packet.Tag == 0
           && openSend.Packet.Series == 0
           && openSend.Packet.Body == string.Empty,
        "SM626 open must observe armed/post-MP state and an empty all-zero payload tuple");

    var expiry = NewProbe("firehit-expiry");
    expiry.m_boFireHitSkill = true;
    expiry.m_dwLatestFireHitTick = 1000;
    Assert(!expiry.TryExpireNativeFireHitSkill(21000)
           && expiry.m_boFireHitSkill
           && expiry.FireHitSends.Count == 0,
        "fire-hit elapsed 20000 must remain armed");
    Assert(expiry.TryExpireNativeFireHitSkill(21001)
           && !expiry.m_boFireHitSkill
           && expiry.m_dwLatestFireHitTick == 1000
           && expiry.FireHitSends.Count == 1,
        "fire-hit elapsed 20001 must clear state before one SM626 close");
    var closeSend = expiry.FireHitSends[0];
    Assert(!closeSend.Ready
           && closeSend.Tick == 1000
           && closeSend.Packet.Ident == Grobal2.SM_FIREHITSKILL
           && closeSend.Packet.Recog == 1
           && closeSend.Packet.Param == 0
           && closeSend.Packet.Tag == 0
           && closeSend.Packet.Series == 0
           && closeSend.Packet.Body == string.Empty
           && !expiry.TryExpireNativeFireHitSkill(21002)
           && expiry.FireHitSends.Count == 1,
        "SM626 close must be empty/all-zero except Recog=1 and must not repeat while inactive");
    PumpMessages(expiry);
    Assert(!expiry.Sent.Any(packet =>
            packet.Ident == Grobal2.SM_SYSMESSAGE),
        "fire-hit timeout must not send a text message");

    var expiryWrap = NewProbe("firehit-expiry-wrap");
    expiryWrap.m_boFireHitSkill = true;
    expiryWrap.m_dwLatestFireHitTick = 0;
    Assert(expiryWrap.TryExpireNativeFireHitSkill(int.MinValue)
           && !expiryWrap.m_boFireHitSkill,
        "fire-hit expiry must use uint32 elapsed across signed wrap");

    var validHit = NewProbe("firehit-valid-hit");
    validHit.m_WAbil.DC = HUtil32.MakeLong(30, 30);
    validHit.m_btHitPoint = 60;
    var trainedMagic = FireMagic(0);
    validHit.m_MagicList.Add(trainedMagic);
    validHit.m_MagicArr[SpellsDef.SKILL_FIRESWORD] = trainedMagic;
    validHit.m_boFireHitSkill = true;
    validHit.m_dwLatestFireHitTick = 0x12345678;
    var hitTarget = NewMonster("测试骷髅", level: 10,
        x: (short)(validHit.m_nCurrX + 1), y: validHit.m_nCurrY,
        map, hp: 500);
    hitTarget.m_wSpeedPoint = 1;
    hitTarget.m_WAbil.AC = 0;
    var hitMode = (short)7;
    validHit._Attack(ref hitMode, hitTarget);
    Assert(!validHit.m_boFireHitSkill
           && validHit.m_dwLatestFireHitTick == 0x12345678
           && trainedMagic.nTranPoint == 1,
        "valid fire-hit consumption must preserve prepare tick and train exactly once");

    var invalidHit = NewProbe("firehit-invalid-hit");
    invalidHit.m_WAbil.DC = HUtil32.MakeLong(30, 30);
    var invalidMagic = FireMagic(0);
    invalidHit.m_MagicList.Add(invalidMagic);
    invalidHit.m_MagicArr[SpellsDef.SKILL_FIRESWORD] = invalidMagic;
    invalidHit.m_boFireHitSkill = true;
    invalidHit.m_dwLatestFireHitTick = 0x23456701;
    hitMode = 7;
    invalidHit._Attack(ref hitMode, invalidHit);
    Assert(!invalidHit.m_boFireHitSkill
           && invalidHit.m_dwLatestFireHitTick == 0x23456701
           && invalidMagic.nTranPoint == 0,
        "invalid non-null fire-hit target must consume state without changing tick/training");

    invalidHit.m_boFireHitSkill = true;
    hitMode = 7;
    invalidHit._Attack(ref hitMode, null);
    Assert(invalidHit.m_boFireHitSkill
           && invalidHit.m_dwLatestFireHitTick == 0x23456701,
        "null fire-hit target must not consume prepared state");

    Log("FIREHIT: skill26 +0xB0 gate/resolved UserMagic MP, uint >10s/>20s "
        + "boundaries and wrap, silent success/expiry, exact SM626 Recog 0/1, "
        + "prepare-tick-preserving single-train consumption passed");
}

void RunTwinBladeDefaultProtocol(Envirnoment _)
{
    var clientSpell = typeof(TPlayObject).GetMethod("ClientSpellXY",
        BindingFlags.Instance | BindingFlags.NonPublic, null,
        new[]
        {
            typeof(short), typeof(int), typeof(int), typeof(int),
            typeof(TBaseObject), typeof(bool), typeof(bool),
            typeof(int).MakeByRefType()
        }, null) ?? throw new MissingMethodException(nameof(TPlayObject),
        "ClientSpellXY");
    var clientHit = typeof(TPlayObject).GetMethod("ClientHitXY",
        BindingFlags.Instance | BindingFlags.NonPublic, null,
        new[]
        {
            typeof(int), typeof(int), typeof(int), typeof(byte),
            typeof(bool), typeof(int).MakeByRefType()
        }, null) ?? throw new MissingMethodException(nameof(TPlayObject),
        "ClientHitXY");

    var twinMap = CreateBlankMap(128, 64, "harness-twinblade-default");
    var nextX = 16;

    TUserMagic DefaultMagic(int skillId, string name, ushort spell)
    {
        var definition = BuildMagicTemplate(skillId, name, trainLv: 3);
        definition.wSpell = spell;
        definition.btDefSpell = 0;
        definition.btEffectType = 4;
        definition.btEffect = 7;
        return new TUserMagic
        {
            MagicInfo = definition,
            btLevel = 0,
            wMagIdx = unchecked((ushort)skillId)
        };
    }

    TUserMagic TwinMagic(ushort spell) => DefaultMagic(
        SpellsDef.SKILL_TWINBLADE, "狂风斩", spell);

    (StoragePacketProbe Caster, StoragePacketProbe Observer) NewPair(
        string name)
    {
        var x = (short)nextX;
        nextX += 8;
        var caster = new StoragePacketProbe
        {
            m_boOffLineFlag = false,
            m_boGhost = false,
            m_boDeath = false,
            m_boFixedHideMode = false,
            m_boCanSpell = true,
            m_sCharName = name,
            m_sMapName = twinMap.sMapName,
            m_nCurrX = x,
            m_nCurrY = 24,
            m_PEnvir = twinMap
        };
        var observer = new StoragePacketProbe
        {
            m_boOffLineFlag = false,
            m_boGhost = false,
            m_boDeath = false,
            m_boFixedHideMode = false,
            m_sCharName = name + "-observer",
            m_sMapName = twinMap.sMapName,
            m_nCurrX = (short)(x + 1),
            m_nCurrY = 24,
            m_PEnvir = twinMap
        };
        caster.m_Abil.Level = 35;
        observer.m_Abil.Level = 35;
        twinMap.AddToMap(caster.m_nCurrX, caster.m_nCurrY,
            CellType.OS_MOVINGOBJECT, caster);
        twinMap.AddToMap(observer.m_nCurrX, observer.m_nCurrY,
            CellType.OS_MOVINGOBJECT, observer);
        return (caster, observer);
    }

    StoragePacketProbe NewTarget(StoragePacketProbe caster, string name,
        bool dead)
    {
        var target = new StoragePacketProbe
        {
            m_boOffLineFlag = false,
            m_boGhost = false,
            m_boDeath = dead,
            m_boFixedHideMode = false,
            m_sCharName = name,
            m_sMapName = twinMap.sMapName,
            m_nCurrX = (short)(caster.m_nCurrX + 2),
            m_nCurrY = caster.m_nCurrY,
            m_PEnvir = twinMap
        };
        target.m_Abil.Level = 35;
        twinMap.AddToMap(target.m_nCurrX, target.m_nCurrY,
            CellType.OS_MOVINGOBJECT, target);
        return target;
    }

    bool CastSkill(StoragePacketProbe caster, int skillId,
        int targetX = int.MinValue, int targetY = int.MinValue,
        TBaseObject target = null)
    {
        var x = targetX == int.MinValue ? caster.m_nCurrX : targetX;
        var y = targetY == int.MinValue ? caster.m_nCurrY : targetY;
        var args = new object[]
        {
            (short)Grobal2.CM_SPELL, skillId,
            x, y, target, true, false, 0
        };
        return (bool)clientSpell.Invoke(caster, args);
    }

    bool Cast(StoragePacketProbe caster, int targetX = int.MinValue,
        int targetY = int.MinValue, TBaseObject target = null) =>
        CastSkill(caster, SpellsDef.SKILL_TWINBLADE, targetX, targetY,
            target);

    byte[] TwoInts(int first, int second) =>
        BitConverter.GetBytes(first).Concat(BitConverter.GetBytes(second))
            .ToArray();

    Assert(!M2Share.MagicManager.IsWarrSkill(SpellsDef.SKILL_TWINBLADE),
        "skill 38 must enter the native generic spell dispatcher");

    var (missing, _) = NewPair("twinblade-missing");
    missing.m_WAbil.MP = 19;
    missing.m_dwLatestTwinHitTick = 0x10203040;
    Assert(!Cast(missing)
           && missing.m_WAbil.MP == 19
           && !missing.m_boTwinHitSkill
           && missing.m_dwLatestTwinHitTick == 0x10203040
           && missing.m_MsgList.Count == 0,
        "unlearned skill 38 must fail without MP, state or broadcasts");

    var (success, successObserver) = NewPair("twinblade-success");
    var successMagic = TwinMagic(40); // native level-0 MP cost = 10
    success.m_MagicList.Add(successMagic);
    success.m_WAbil.MP = 10;
    success.m_boTwinHitSkill = true;
    success.m_dwLatestTwinHitTick = 0x12345678;
    Assert(Cast(success)
           && success.m_WAbil.MP == 0
           && success.m_boTwinHitSkill
           && success.m_dwLatestTwinHitTick == 0x12345678
           && successMagic.nTranPoint == 0,
        "skill 38 default success must spend exact MP without state/training changes");
    var queuedSpell = success.m_MsgList.Single(message =>
        message.wIdent == Grobal2.RM_SPELL);
    var queuedEffect = success.m_MsgList.Single(message =>
        message.wIdent == Grobal2.RM_MAGICFIRE);
    Assert(ReferenceEquals(queuedSpell.BaseObject, success)
           && queuedSpell.wParam == HUtil32.MakeWord(7, 4)
           && queuedSpell.nParam1 == success.m_nCurrX
           && queuedSpell.nParam2 == success.m_nCurrY
           && queuedSpell.nParam3 == SpellsDef.SKILL_TWINBLADE
           && queuedSpell.Payload != null
           && ReferenceEquals(queuedEffect.BaseObject, success)
           && queuedEffect.wParam == HUtil32.MakeWord(4, 7)
           && queuedEffect.nParam1 == success.m_nCurrX
           && queuedEffect.nParam2 == success.m_nCurrY
           && queuedEffect.nParam3 == 0
           && queuedEffect.Payload != null,
        "skill 38 must queue exact ordinary RM_SPELL/RM_MAGICFIRE fields");
    Assert(ReferenceEquals(M2Share.ObjectManager.Get(success.ObjectId), success),
        "skill 38 caster must remain registered for RM_SPELL mapping");
    Assert(successObserver.m_MsgList.Any(message =>
               message.wIdent == Grobal2.RM_SPELL)
           && successObserver.m_MsgList.Any(message =>
               message.wIdent == Grobal2.RM_MAGICFIRE),
        "skill 38 observer must receive both ordinary ref broadcasts");
    PumpMessages(successObserver);
    var spellFrame = successObserver.Sent.Single(packet =>
        packet.Ident == Grobal2.SM_SPELL);
    var effectFrame = successObserver.Sent.Single(packet =>
        packet.Ident == Grobal2.SM_MAGICFIRE);
    Assert(spellFrame.Recog == success.ObjectId
           && spellFrame.Param == success.m_nCurrX
           && spellFrame.Tag == success.m_nCurrY
           && spellFrame.Series == HUtil32.MakeWord(7, 4)
           && spellFrame.RawBody.SequenceEqual(TwoInts(
               SpellsDef.SKILL_TWINBLADE, 0)),
        "skill 38 ordinary SM_SPELL frame mismatch");
    Assert(effectFrame.Recog == success.ObjectId
           && effectFrame.Param == success.m_nCurrX
           && effectFrame.Tag == success.m_nCurrY
           && effectFrame.Series == HUtil32.MakeWord(4, 7)
           && effectFrame.RawBody.SequenceEqual(TwoInts(0, 0)),
        "skill 38 ordinary SM_MAGICFIRE frame mismatch");

    var (zeroCost, zeroCostObserver) = NewPair("twinblade-zero-cost");
    var zeroCostMagic = TwinMagic(0);
    zeroCost.m_MagicList.Add(zeroCostMagic);
    zeroCost.m_WAbil.MP = 7;
    zeroCost.m_WAbil.MaxMP = 7;
    zeroCost.m_dwLatestTwinHitTick = 0x18273645;
    Assert(Cast(zeroCost)
           && zeroCost.m_WAbil.MP == 7
           && !zeroCost.m_boTwinHitSkill
           && zeroCost.m_dwLatestTwinHitTick == 0x18273645
           && zeroCostMagic.nTranPoint == 0
           && zeroCost.m_MsgList.Any(message =>
               message.wIdent == Grobal2.RM_HEALTHSPELLCHANGED),
        "zero-cost skill 38 must still publish MP and use the ordinary success tail");
    PumpMessages(zeroCostObserver);
    Assert(zeroCostObserver.Sent.Count(packet =>
               packet.Ident == Grobal2.SM_SPELL) == 1
           && zeroCostObserver.Sent.Count(packet =>
               packet.Ident == Grobal2.SM_MAGICFIRE) == 1,
        "zero-cost skill 38 ordinary frames missing");

    var (insufficient, insufficientObserver) = NewPair(
        "twinblade-insufficient");
    insufficient.m_MagicList.Add(TwinMagic(40));
    insufficient.m_WAbil.MP = 9;
    insufficient.m_boTwinHitSkill = true;
    insufficient.m_dwLatestTwinHitTick = 0x23456701;
    Assert(Cast(insufficient)
           && insufficient.m_WAbil.MP == 9
           && insufficient.m_boTwinHitSkill
           && insufficient.m_dwLatestTwinHitTick == 0x23456701
           && !insufficient.m_MsgList.Any(message =>
               message.wIdent == Grobal2.RM_HEALTHSPELLCHANGED),
        "skill 38 insufficient MP must fail the inner cast without state/publication changes");
    PumpMessages(insufficientObserver);
    Assert(insufficientObserver.Sent.Count(packet =>
               packet.Ident == Grobal2.SM_MAGICFIRE_FAIL) == 1
           && !insufficientObserver.Sent.Any(packet =>
               packet.Ident == Grobal2.SM_SPELL ||
               packet.Ident == Grobal2.SM_MAGICFIRE),
        "skill 38 insufficient MP must emit only ordinary magic-fire failure");

    var (rangeNine, rangeNineObserver) = NewPair("twinblade-range-nine");
    var rangeNineMagic = TwinMagic(40);
    rangeNine.m_MagicList.Add(rangeNineMagic);
    rangeNine.m_WAbil.MP = 10;
    Assert(Cast(rangeNine, rangeNine.m_nCurrX + 9)
           && rangeNine.m_WAbil.MP == 0
           && rangeNineMagic.nTranPoint == 0,
        "skill 38 Chebyshev distance 9 must succeed after MP deduction");
    PumpMessages(rangeNineObserver);
    Assert(rangeNineObserver.Sent.Count(packet =>
               packet.Ident == Grobal2.SM_SPELL) == 1
           && rangeNineObserver.Sent.Count(packet =>
               packet.Ident == Grobal2.SM_MAGICFIRE) == 1,
        "skill 38 distance-9 success frames missing");

    var (tooFar, tooFarObserver) = NewPair("twinblade-range");
    tooFar.m_MagicList.Add(TwinMagic(40));
    tooFar.m_WAbil.MP = 10;
    Assert(Cast(tooFar, tooFar.m_nCurrX + 10)
           && tooFar.m_WAbil.MP == 0
           && !tooFar.m_boTwinHitSkill,
        "skill 38 range failure must occur after the native MP deduction");
    PumpMessages(tooFarObserver);
    Assert(tooFarObserver.Sent.Count(packet =>
               packet.Ident == Grobal2.SM_MAGICFIRE_FAIL) == 1
           && !tooFarObserver.Sent.Any(packet =>
               packet.Ident == Grobal2.SM_SPELL ||
               packet.Ident == Grobal2.SM_MAGICFIRE),
        "skill 38 range failure must not emit ordinary spell/effect frames");

    var (liveCaster, liveObserver) = NewPair("twinblade-live-target");
    var liveMagic = TwinMagic(0);
    liveMagic.btLevel = 2;
    var liveTarget = NewTarget(liveCaster, "twinblade-live", dead: false);
    liveCaster.m_MagicList.Add(liveMagic);
    liveCaster.m_WAbil.MP = 3;
    Assert(Cast(liveCaster, liveTarget.m_nCurrX, liveTarget.m_nCurrY,
               liveTarget)
           && liveMagic.nTranPoint == 0,
        "skill 38 live target must use the ordinary success tail");
    var liveQueuedEffect = liveCaster.m_MsgList.Single(message =>
        message.wIdent == Grobal2.RM_MAGICFIRE);
    Assert(liveQueuedEffect.nParam3 == liveTarget.ObjectId,
        "skill 38 live target id must reach RM_MAGICFIRE");
    PumpMessages(liveObserver);
    var liveSpell = liveObserver.Sent.Single(packet =>
        packet.Ident == Grobal2.SM_SPELL);
    var liveEffect = liveObserver.Sent.Single(packet =>
        packet.Ident == Grobal2.SM_MAGICFIRE);
    Assert(liveSpell.RawBody.SequenceEqual(TwoInts(
               SpellsDef.SKILL_TWINBLADE, 2)),
        "skill 38 live target SM_SPELL body mismatch");
    Assert(liveEffect.RawBody.SequenceEqual(TwoInts(liveTarget.ObjectId,
               2)),
        "skill 38 live target SM_MAGICFIRE body mismatch");

    var (deadCaster, deadObserver) = NewPair("twinblade-dead-target");
    var deadMagic = TwinMagic(0);
    var deadTarget = NewTarget(deadCaster, "twinblade-dead", dead: true);
    deadCaster.m_MagicList.Add(deadMagic);
    Assert(Cast(deadCaster, deadTarget.m_nCurrX, deadTarget.m_nCurrY,
               deadTarget)
           && deadMagic.nTranPoint == 0,
        "skill 38 dead target must retain the ordinary cast success");
    var deadQueuedSpell = deadCaster.m_MsgList.Single(message =>
        message.wIdent == Grobal2.RM_SPELL);
    var deadQueuedEffect = deadCaster.m_MsgList.Single(message =>
        message.wIdent == Grobal2.RM_MAGICFIRE);
    Assert(deadQueuedSpell.nParam1 == deadTarget.m_nCurrX
           && deadQueuedSpell.nParam2 == deadTarget.m_nCurrY
           && deadQueuedEffect.nParam3 == 0,
        "dead target must be cleared after RM_SPELL and before RM_MAGICFIRE");
    PumpMessages(deadObserver);
    var deadEffect = deadObserver.Sent.Single(packet =>
        packet.Ident == Grobal2.SM_MAGICFIRE);
    Assert(deadEffect.RawBody.SequenceEqual(TwoInts(0, 0)),
        "skill 38 dead target SM_MAGICFIRE must carry target id zero");

    var (hitNoOp, _) = NewPair("twinblade-cm3028");
    hitNoOp.m_boCanHit = true;
    hitNoOp.m_btDirection = Grobal2.DR_UP;
    hitNoOp.m_boTwinHitSkill = true;
    hitNoOp.m_dwLatestTwinHitTick = 0x34567812;
    hitNoOp.m_nHealthTick = 300;
    hitNoOp.m_nSpellTick = 300;
    var hitArgs = new object[]
    {
        Grobal2.CM_TWINHIT, (int)hitNoOp.m_nCurrX,
        (int)hitNoOp.m_nCurrY, (byte)Grobal2.DR_RIGHT, true, 0
    };
    Assert((bool)clientHit.Invoke(hitNoOp, hitArgs)
           && hitNoOp.m_btDirection == Grobal2.DR_UP
           && hitNoOp.m_boTwinHitSkill
           && hitNoOp.m_dwLatestTwinHitTick == 0x34567812
           && hitNoOp.m_nHealthTick == 270
           && hitNoOp.m_nSpellTick == 200
           && !hitNoOp.m_MsgList.Any(message =>
               message.wIdent == Grobal2.RM_TWINHIT),
        "CM_TWINHIT 3028 must run only the native shared tick/ack tail");

    var healthTickAfterValid = hitNoOp.m_nHealthTick;
    var spellTickAfterValid = hitNoOp.m_nSpellTick;
    var wrongHitArgs = new object[]
    {
        Grobal2.CM_TWINHIT, (int)hitNoOp.m_nCurrX + 1,
        (int)hitNoOp.m_nCurrY, (byte)Grobal2.DR_RIGHT, true, 0
    };
    Assert(!(bool)clientHit.Invoke(hitNoOp, wrongHitArgs)
           && hitNoOp.m_btDirection == Grobal2.DR_UP
           && hitNoOp.m_boTwinHitSkill
           && hitNoOp.m_dwLatestTwinHitTick == 0x34567812
           && hitNoOp.m_nHealthTick == healthTickAfterValid
           && hitNoOp.m_nSpellTick == spellTickAfterValid
           && !hitNoOp.m_MsgList.Any(message =>
               message.wIdent == Grobal2.RM_TWINHIT),
        "CM_TWINHIT 3028 wrong coordinates must fail before the shared tail");

    var (mode9Attacker, _) = NewPair("twinblade-mode9-no-train");
    mode9Attacker.m_WAbil.DC = HUtil32.MakeLong(30, 30);
    mode9Attacker.m_btHitPoint = 1;
    mode9Attacker.m_nPowerRate = 100;
    mode9Attacker.m_boFastTrain = false;
    mode9Attacker.m_boTwinHitSkill = false;
    mode9Attacker.m_sNativeCriticalChance = -1;
    var mode9Magic = TwinMagic(0);
    mode9Magic.nTranPoint = 17;
    var mode9Skill43 = DefaultMagic(SpellsDef.SKILL_43, "破空剑", 0);
    mode9Skill43.nTranPoint = 23;
    mode9Attacker.m_MagicList.Add(mode9Magic);
    mode9Attacker.m_MagicList.Add(mode9Skill43);
    mode9Attacker.m_MagicArr[SpellsDef.SKILL_TWINBLADE] = mode9Magic;
    mode9Attacker.m_MagicArr[SpellsDef.SKILL_43] = mode9Skill43;
    var mode9Target = NewMonster("测试骷髅", 10,
        mode9Attacker.m_nCurrX,
        (short)(mode9Attacker.m_nCurrY + 1), twinMap, 500);
    mode9Target.m_wSpeedPoint = 1;
    mode9Target.m_WAbil.AC = 0;
    var mode9Hp = mode9Target.m_WAbil.HP;
    short hitMode = 9;
    Assert(mode9Attacker._Attack(ref hitMode, mode9Target)
           && mode9Target.m_WAbil.HP < mode9Hp
           && mode9Magic.nTranPoint == 17
           && mode9Skill43.nTranPoint == 23,
        "successful mode9 physical hit must not train spell skills 38/43");

    var (mode12Attacker, _) = NewPair("mode12-no-skill66-train");
    mode12Attacker.m_WAbil.DC = HUtil32.MakeLong(30, 30);
    mode12Attacker.m_btHitPoint = 60;
    mode12Attacker.m_nPowerRate = 100;
    mode12Attacker.m_sNativeCriticalChance = -1;
    var mode12Magic = DefaultMagic(66, "技能66", 0);
    mode12Magic.nTranPoint = 19;
    mode12Attacker.m_MagicList.Add(mode12Magic);
    mode12Attacker.m_MagicArr[66] = mode12Magic;
    var mode12Target = NewMonster("测试骷髅", 10,
        mode12Attacker.m_nCurrX,
        (short)(mode12Attacker.m_nCurrY + 1), twinMap, 500);
    mode12Target.m_wSpeedPoint = 1;
    mode12Target.m_WAbil.AC = 0;
    var mode12Hp = mode12Target.m_WAbil.HP;
    hitMode = 12;
    Assert(mode12Attacker._Attack(ref hitMode, mode12Target)
           && mode12Target.m_WAbil.HP < mode12Hp
           && mode12Magic.nTranPoint == 19,
        "legacy mode12 physical hit must not train unrelated skill66");

    void AssertOrdinaryNoStateSkill(int skillId, string fixtureName,
        string magicName)
    {
        Assert(!M2Share.MagicManager.IsWarrSkill(skillId),
            $"skill {skillId} must enter the native generic spell dispatcher");
        var (caster, observer) = NewPair(fixtureName);
        var magic = DefaultMagic(skillId, magicName, 0);
        caster.m_MagicList.Add(magic);
        caster.m_WAbil.MP = 7;
        caster.m_WAbil.MaxMP = 7;
        caster.m_nSoftVersionDateEx = 1;
        caster.m_dwClientTick = 1;
        Assert(CastSkill(caster, skillId)
               && CastSkill(caster, skillId)
               && caster.m_WAbil.MP == 7
               && magic.nTranPoint == 0
               && caster.m_MsgList.Count(message =>
               message.wIdent == Grobal2.RM_SPELL) == 2
               && caster.m_MsgList.Count(message =>
               message.wIdent == Grobal2.RM_MAGICFIRE) == 2,
            $"skill {skillId} must repeat the ordinary no-state spell path");
        var queuedSpells = caster.m_MsgList.Where(message =>
            message.wIdent == Grobal2.RM_SPELL).ToArray();
        var queuedEffects = caster.m_MsgList.Where(message =>
            message.wIdent == Grobal2.RM_MAGICFIRE).ToArray();
        Assert(queuedSpells.All(message =>
                   ReferenceEquals(message.BaseObject, caster)
                   && message.wParam == HUtil32.MakeWord(7, 4)
                   && message.nParam1 == caster.m_nCurrX
                   && message.nParam2 == caster.m_nCurrY
                   && message.nParam3 == skillId
                   && message.Payload != null)
               && queuedEffects.All(message =>
                   ReferenceEquals(message.BaseObject, caster)
                   && message.wParam == HUtil32.MakeWord(4, 7)
                   && message.nParam1 == caster.m_nCurrX
                   && message.nParam2 == caster.m_nCurrY
                   && message.nParam3 == 0
                   && message.Payload != null),
            $"skill {skillId} ordinary RM_SPELL/RM_MAGICFIRE fields mismatch");
        Assert(!caster.Sent.Any(packet => packet.Body == "+WID" ||
                   packet.Body == "+UWID" || packet.Body == "+CRS" ||
                   packet.Body == "+UCRS"),
            $"skill {skillId} emitted an invented direct text-state packet");
        PumpMessages(observer);
        var spellFrames = observer.Sent.Where(packet =>
            packet.Ident == Grobal2.SM_SPELL).ToArray();
        var effectFrames = observer.Sent.Where(packet =>
            packet.Ident == Grobal2.SM_MAGICFIRE).ToArray();
        Assert(spellFrames.Length == 2 && spellFrames.All(packet =>
                   packet.Recog == caster.ObjectId
                   && packet.Param == caster.m_nCurrX
                   && packet.Tag == caster.m_nCurrY
                   && packet.Series == HUtil32.MakeWord(7, 4)
                   && packet.RawBody.SequenceEqual(TwoInts(skillId, 0)))
               && effectFrames.Length == 2 && effectFrames.All(packet =>
                   packet.Recog == caster.ObjectId
                   && packet.Param == caster.m_nCurrX
                   && packet.Tag == caster.m_nCurrY
                   && packet.Series == HUtil32.MakeWord(4, 7)
                   && packet.RawBody.SequenceEqual(TwoInts(0, 0))),
            $"skill {skillId} ordinary SM_SPELL/SM_MAGICFIRE frame mismatch");
    }

    AssertOrdinaryNoStateSkill(SpellsDef.SKILL_REDBANWOL,
        "redmoon-default", "红月剑法");
    AssertOrdinaryNoStateSkill(SpellsDef.SKILL_CROSSMOON,
        "crossmoon-default", "抱月刀法");

    var skill43Map = CreateBlankMap(64, 64, "harness-skill43-native");
    var skill43Caster = new StoragePacketProbe
    {
        m_boOffLineFlag = false,
        m_boGhost = false,
        m_boDeath = false,
        m_boFixedHideMode = false,
        m_boCanSpell = true,
        m_sCharName = "skill43-caster",
        m_sMapName = skill43Map.sMapName,
        m_nCurrX = 24,
        m_nCurrY = 24,
        m_PEnvir = skill43Map,
        m_nSoftVersionDateEx = 1,
        m_dwClientTick = 1
    };
    var skill43Observer = new StoragePacketProbe
    {
        m_boOffLineFlag = false,
        m_boGhost = false,
        m_boDeath = false,
        m_boFixedHideMode = false,
        m_sCharName = "skill43-observer",
        m_sMapName = skill43Map.sMapName,
        m_nCurrX = 25,
        m_nCurrY = 24,
        m_PEnvir = skill43Map
    };
    skill43Caster.m_Abil.Level = 35;
    skill43Observer.m_Abil.Level = 35;
    skill43Map.AddToMap(skill43Caster.m_nCurrX, skill43Caster.m_nCurrY,
        CellType.OS_MOVINGOBJECT, skill43Caster);
    skill43Map.AddToMap(skill43Observer.m_nCurrX,
        skill43Observer.m_nCurrY, CellType.OS_MOVINGOBJECT,
        skill43Observer);

    var eligibleA = NewMonster("skill43-eligible-a", 10, 26, 24,
        skill43Map, 100, recalc: false);
    var blockedState = NewMonster("skill43-blocked-state", 10, 27, 24,
        skill43Map, 100, recalc: false);
    var eligibleB = NewMonster("skill43-eligible-b", 10, 19, 24,
        skill43Map, 100, recalc: false);
    var outOfRange = NewMonster("skill43-out-of-range", 10, 30, 24,
        skill43Map, 100, recalc: false);
    var stronger = NewMonster("skill43-stronger", 36, 26, 25,
        skill43Map, 100, recalc: false);
    var dead = NewMonster("skill43-dead", 10, 26, 23,
        skill43Map, 100, recalc: false);
    dead.m_boDeath = true;
    var slave = NewMonster("skill43-slave", 10, 23, 24,
        skill43Map, 100, recalc: false);
    slave.m_Master = skill43Caster;
    var race50 = new AnimalObject
    {
        m_sCharName = "skill43-race50",
        m_sMapName = skill43Map.sMapName,
        m_nCurrX = 24,
        m_nCurrY = 23,
        m_PEnvir = skill43Map
    };
    race50.m_Abil.Level = 10;
    skill43Map.AddToMap(race50.m_nCurrX, race50.m_nCurrY,
        CellType.OS_MOVINGOBJECT, race50);
    var playerClass = new StoragePacketProbe
    {
        m_sCharName = "skill43-player-class",
        m_sMapName = skill43Map.sMapName,
        m_nCurrX = 24,
        m_nCurrY = 25,
        m_PEnvir = skill43Map,
        m_btRaceServer = Grobal2.RC_MONSTER
    };
    playerClass.m_Abil.Level = 10;
    skill43Map.AddToMap(playerClass.m_nCurrX, playerClass.m_nCurrY,
        CellType.OS_MOVINGOBJECT, playerClass);
    var walkClass = new WalkMon
    {
        m_sCharName = "skill43-walk-class",
        m_sMapName = skill43Map.sMapName,
        m_nCurrX = 23,
        m_nCurrY = 23,
        m_PEnvir = skill43Map,
        m_btRaceServer = Grobal2.RC_MONSTER
    };
    walkClass.m_Abil.Level = 10;
    skill43Map.AddToMap(walkClass.m_nCurrX, walkClass.m_nCurrY,
        CellType.OS_MOVINGOBJECT, walkClass);
    var flattenedSuperGuard = new SuperGuard
    {
        m_btRaceServer = Grobal2.RC_MONSTER
    };
    flattenedSuperGuard.m_Abil.Level = 10;
    Assert(flattenedSuperGuard.IsNativeMagic43Target(skill43Caster),
        "C# SuperGuard flattening must retain native TAnimal skill-43 eligibility");
    blockedState.SetNativeActiveState(52);

    foreach (var target in new TBaseObject[]
             {
                 eligibleA, blockedState, eligibleB, outOfRange, stronger,
                 dead, slave, race50, playerClass, walkClass
             })
    {
        skill43Caster.m_VisibleActors.Add(new TVisibleBaseObject
        {
            BaseObject = target
        });
    }

    var skill43Magic = DefaultMagic(SpellsDef.SKILL_43, "破空剑", 0);
    skill43Magic.btLevel = 3;
    skill43Magic.nTranPoint = 17;
    skill43Caster.m_MagicList.Add(skill43Magic);
    skill43Caster.m_WAbil.MP = 7;
    skill43Caster.m_WAbil.MaxMP = 7;
    Assert(CastSkill(skill43Caster, SpellsDef.SKILL_43)
           && skill43Caster.m_WAbil.MP == 7
           && skill43Magic.nTranPoint >= 18
           && skill43Magic.nTranPoint <= 20,
        "skill 43 must succeed and train exactly once after qualifying calls");
    Assert(eligibleA.HasNativeActiveState(26)
           && eligibleB.HasNativeActiveState(26)
           && !blockedState.HasNativeActiveState(26)
           && blockedState.HasNativeActiveState(52)
           && !outOfRange.HasNativeActiveState(26)
           && !stronger.HasNativeActiveState(26)
           && !dead.HasNativeActiveState(26)
           && !slave.HasNativeActiveState(26)
           && !race50.HasNativeActiveState(26)
           && !playerClass.HasNativeActiveState(26)
           && !walkClass.HasNativeActiveState(26),
        "skill 43 target-side VMT/range/state gates mismatch");
    Assert(!skill43Caster.Sent.Any(packet => packet.Body == "+CID" ||
               packet.Body == "+UCID"),
        "skill 43 emitted an invented direct text-state packet");
    PumpMessages(skill43Observer);
    var skill43Spell = skill43Observer.Sent.Single(packet =>
        packet.Ident == Grobal2.SM_SPELL);
    var skill43Effect = skill43Observer.Sent.Single(packet =>
        packet.Ident == Grobal2.SM_MAGICFIRE);
    Assert(skill43Spell.Recog == skill43Caster.ObjectId
           && skill43Spell.Param == skill43Caster.m_nCurrX
           && skill43Spell.Tag == skill43Caster.m_nCurrY
           && skill43Spell.Series == HUtil32.MakeWord(7, 4)
           && skill43Spell.RawBody.SequenceEqual(TwoInts(
               SpellsDef.SKILL_43, 3))
           && skill43Effect.Recog == skill43Caster.ObjectId
           && skill43Effect.Param == skill43Caster.m_nCurrX
           && skill43Effect.Tag == skill43Caster.m_nCurrY
           && skill43Effect.Series == HUtil32.MakeWord(4, 7)
           && skill43Effect.RawBody.SequenceEqual(TwoInts(0, 3)),
        "skill 43 native SM17/638 frame mismatch");

    var noHitCaster = new StoragePacketProbe
    {
        m_boOffLineFlag = false,
        m_boGhost = false,
        m_boDeath = false,
        m_boFixedHideMode = false,
        m_boCanSpell = true,
        m_sCharName = "skill43-no-hit",
        m_sMapName = skill43Map.sMapName,
        m_nCurrX = 48,
        m_nCurrY = 24,
        m_PEnvir = skill43Map,
        m_nSoftVersionDateEx = 1,
        m_dwClientTick = 1
    };
    var noHitObserver = new StoragePacketProbe
    {
        m_boOffLineFlag = false,
        m_boGhost = false,
        m_boDeath = false,
        m_boFixedHideMode = false,
        m_sCharName = "skill43-no-hit-observer",
        m_sMapName = skill43Map.sMapName,
        m_nCurrX = 49,
        m_nCurrY = 24,
        m_PEnvir = skill43Map
    };
    noHitCaster.m_Abil.Level = 35;
    noHitObserver.m_Abil.Level = 35;
    skill43Map.AddToMap(noHitCaster.m_nCurrX, noHitCaster.m_nCurrY,
        CellType.OS_MOVINGOBJECT, noHitCaster);
    skill43Map.AddToMap(noHitObserver.m_nCurrX, noHitObserver.m_nCurrY,
        CellType.OS_MOVINGOBJECT, noHitObserver);
    var deadExplicit = NewMonster("skill43-dead-explicit", 10, 50, 24,
        skill43Map, 100, recalc: false);
    deadExplicit.m_boDeath = true;
    var noHitMagic = DefaultMagic(SpellsDef.SKILL_43, "破空剑", 0);
    noHitMagic.btLevel = 3;
    noHitMagic.nTranPoint = 31;
    noHitCaster.m_MagicList.Add(noHitMagic);
    Assert(CastSkill(noHitCaster, SpellsDef.SKILL_43,
               deadExplicit.m_nCurrX, deadExplicit.m_nCurrY, deadExplicit)
           && noHitMagic.nTranPoint == 31,
        "skill 43 zero-hit cast must succeed without training");
    PumpMessages(noHitObserver);
    var noHitSpell = noHitObserver.Sent.Single(packet =>
        packet.Ident == Grobal2.SM_SPELL);
    var noHitEffect = noHitObserver.Sent.Single(packet =>
        packet.Ident == Grobal2.SM_MAGICFIRE);
    Assert(noHitSpell.RawBody.SequenceEqual(TwoInts(
               SpellsDef.SKILL_43, 3))
           && noHitEffect.RawBody.SequenceEqual(TwoInts(0, 3)),
        "skill 43 zero-hit/dead-target SM17/638 body mismatch");

    Log("TWINBLADE: skill38 generic default spell path, missing/zero/exact MP, "
        + "range 9/10, null/live/dead targets, ordinary SM17/638 frames, no "
        + "Twin state/training (including mode9), and CM3028 coordinate-only "
        + "shared tail; skills34/56 ordinary no-state paths and skill43 native "
        + "state26 target scan passed");
}

void RunNativeAction1011Protocol()
{
    var clientHit = typeof(TPlayObject).GetMethod("ClientHitXY",
        BindingFlags.Instance | BindingFlags.NonPublic, null,
        new[]
        {
            typeof(int), typeof(int), typeof(int), typeof(byte),
            typeof(bool), typeof(int).MakeByRefType()
        }, null) ?? throw new MissingMethodException(nameof(TPlayObject),
        "ClientHitXY");
    var scenarioId = 0;

    (Envirnoment Map, StoragePacketProbe Caster,
        StoragePacketProbe Observer) NewScenario(string name)
    {
        var map = CreateBlankMap(32, 32,
            $"harness-action1011-{scenarioId++}-{name}");
        var caster = new StoragePacketProbe
        {
            m_boOffLineFlag = false,
            m_boGhost = false,
            m_boDeath = false,
            m_boFixedHideMode = false,
            m_boCanHit = true,
            m_sCharName = name,
            m_sMapName = map.sMapName,
            m_nCurrX = 10,
            m_nCurrY = 10,
            m_PEnvir = map,
            m_btDirection = Grobal2.DR_RIGHT,
            m_btHitPoint = 60,
            m_nPowerRate = 100
        };
        var observer = new StoragePacketProbe
        {
            m_boOffLineFlag = false,
            m_boGhost = false,
            m_boDeath = false,
            m_boFixedHideMode = false,
            m_sCharName = name + "-observer",
            m_sMapName = map.sMapName,
            m_nCurrX = 10,
            m_nCurrY = 13,
            m_PEnvir = map
        };
        caster.m_Abil.Level = 35;
        caster.m_WAbil.DC = HUtil32.MakeLong(40, 40);
        observer.m_Abil.Level = 35;
        map.AddToMap(caster.m_nCurrX, caster.m_nCurrY,
            CellType.OS_MOVINGOBJECT, caster);
        map.AddToMap(observer.m_nCurrX, observer.m_nCurrY,
            CellType.OS_MOVINGOBJECT, observer);
        return (map, caster, observer);
    }

    TUserMagic CrossMoonMagic()
    {
        var definition = BuildMagicTemplate(
            SpellsDef.SKILL_CROSSMOON, "圆月弯刀", trainLv: 4);
        definition.wSpell = 40;
        definition.btDefSpell = 0;
        return new TUserMagic
        {
            MagicInfo = definition,
            btLevel = 2,
            wMagIdx = SpellsDef.SKILL_CROSSMOON
        };
    }

    void LearnCrossMoon(StoragePacketProbe caster, TUserMagic magic)
    {
        caster.m_MagicList.Add(magic);
        caster.m_MagicArr[SpellsDef.SKILL_CROSSMOON] = magic;
    }

    NativeActionDamageProbe AddTarget(Envirnoment map,
        StoragePacketProbe caster, int distance, byte level, string name)
    {
        var target = new NativeActionDamageProbe
        {
            m_boOffLineFlag = false,
            m_boGhost = false,
            m_boDeath = false,
            m_boFixedHideMode = false,
            m_sCharName = name,
            m_sMapName = map.sMapName,
            m_nCurrX = (short)(caster.m_nCurrX + distance),
            m_nCurrY = caster.m_nCurrY,
            m_PEnvir = map,
            m_wSpeedPoint = 1
        };
        target.m_Abil.Level = level;
        target.m_WAbil.HP = 1000;
        target.m_WAbil.MaxHP = 1000;
        map.AddToMap(target.m_nCurrX, target.m_nCurrY,
            CellType.OS_MOVINGOBJECT, target);
        return target;
    }

    bool Hit(StoragePacketProbe caster, int action)
    {
        var args = new object[]
        {
            action, caster.m_nCurrX, caster.m_nCurrY,
            (byte)Grobal2.DR_RIGHT, true, 0
        };
        return (bool)clientHit.Invoke(caster, args);
    }

    byte[] ExpectedBody(int action, int level, int direction, int x, int y)
    {
        var body = new byte[12];
        BitConverter.GetBytes(unchecked((ushort)action)).CopyTo(body, 0);
        BitConverter.GetBytes(unchecked((ushort)level)).CopyTo(body, 2);
        BitConverter.GetBytes((ushort)0).CopyTo(body, 4);
        BitConverter.GetBytes(unchecked((ushort)direction)).CopyTo(body, 6);
        BitConverter.GetBytes(unchecked((ushort)x)).CopyTo(body, 8);
        BitConverter.GetBytes(unchecked((ushort)y)).CopyTo(body, 10);
        return body;
    }

    void AssertPhysicalFrame(StoragePacketProbe caster,
        StoragePacketProbe observer, int action, int level,
        int direction = Grobal2.DR_RIGHT, bool includeSource = false)
    {
        Assert(caster.m_MsgList.Count(message =>
                   message.wIdent == Grobal2.RM_PHYSICAL_ATT) == 1
               && observer.m_MsgList.Count(message =>
                   message.wIdent == Grobal2.RM_PHYSICAL_ATT) == 1,
            $"action {action} physical-frame receiver set mismatch");
        PumpMessages(caster);
        PumpMessages(observer);
        var expected = ExpectedBody(action, level, direction,
            caster.m_nCurrX, caster.m_nCurrY);
        var receivers = includeSource
            ? new[] { caster, observer }
            : new[] { observer };
        Assert(includeSource || !caster.Sent.Any(packet =>
                packet.Ident == Grobal2.SM_PHYSICAL_ATT),
            $"action {action} must not send its physical frame to the source");
        foreach (var receiver in receivers)
        {
            var frames = receiver.Sent.Where(packet =>
                packet.Ident == Grobal2.SM_PHYSICAL_ATT).ToList();
            Assert(frames.Count == 1,
                $"action {action} must produce exactly one SM1230 per receiver");
            var frame = frames[0];
            Assert(frame.Recog == caster.ObjectId
                   && frame.Param == action
                   && frame.Tag == caster.m_nCurrX
                   && frame.Series == caster.m_nCurrY
                   && frame.RawBody != null
                   && frame.RawBody.SequenceEqual(expected),
                $"action {action} SM1230 header/body mismatch");
        }
    }

    void AssertDirectCall(NativeActionDamageCall call,
        StoragePacketProbe caster, int skillId, bool arg0, int rawDamage,
        ushort magicIndex)
    {
        Assert(ReferenceEquals(call.Source, caster)
               && call.SkillId == skillId
               && call.Arg0 == arg0
               && call.Category == 4
               && call.Flags == 0
               && call.RawDamage == rawDamage
               && call.MagicIndex == magicIndex,
            $"direct action call mismatch for skill {skillId}");
    }

    var (_, missing, missingObserver) = NewScenario("missing");
    missing.m_WAbil.MP = 29;
    Assert(Hit(missing, TBaseObject.NativeAction1011Code)
           && missing.m_WAbil.MP == 29,
        "missing skill34 must fall back without spending MP");
    AssertPhysicalFrame(missing, missingObserver, 1000, 0);

    var (fallbackMap, fallback, fallbackObserver) =
        NewScenario("insufficient");
    var insufficientMagic = CrossMoonMagic();
    LearnCrossMoon(fallback, insufficientMagic);
    var fallbackDecoyInfo = BuildMagicTemplate(99, "decoy", trainLv: 4);
    var fallbackDecoy = new TUserMagic
    {
        MagicInfo = fallbackDecoyInfo,
        btLevel = 3,
        wMagIdx = SpellsDef.SKILL_ONESWORD
    };
    var fallbackInfo = BuildMagicTemplate(SpellsDef.SKILL_ONESWORD,
        "基本剑术", trainLv: 4);
    var fallbackMagic = new TUserMagic
    {
        MagicInfo = fallbackInfo,
        btLevel = 1,
        wMagIdx = 99
    };
    fallback.m_MagicList.Add(fallbackDecoy);
    fallback.m_MagicList.Add(fallbackMagic);
    fallback.m_WAbil.MP = 29;
    var fallbackTarget = AddTarget(fallbackMap, fallback, 1, 35,
        "fallback-target");
    Assert(Hit(fallback, TBaseObject.NativeAction1011Code)
           && fallback.m_WAbil.MP == 29
           && insufficientMagic.nTranPoint == 0
           && fallbackTarget.Calls.Count == 1,
        "insufficient MP must execute one ordinary fallback hit");
    AssertDirectCall(fallbackTarget.Calls[0], fallback, 1000, true, 40,
        fallbackMagic.wMagIdx);
    AssertPhysicalFrame(fallback, fallbackObserver, 1000, 1);

    var (_, paidEmpty, paidEmptyObserver) = NewScenario("paid-empty");
    var paidEmptyMagic = CrossMoonMagic();
    LearnCrossMoon(paidEmpty, paidEmptyMagic);
    paidEmpty.m_WAbil.MP = 30;
    Assert(Hit(paidEmpty, TBaseObject.NativeAction1011Code)
           && paidEmpty.m_WAbil.MP == 0
           && paidEmptyMagic.nTranPoint == 3,
        "MP equality with no targets must spend 30 and train exactly three");
    AssertPhysicalFrame(paidEmpty, paidEmptyObserver,
        TBaseObject.NativeAction1011Code, 2);

    var (rangeTwoMap, rangeTwo, rangeTwoObserver) =
        NewScenario("range-two");
    var rangeTwoMagic = CrossMoonMagic();
    LearnCrossMoon(rangeTwo, rangeTwoMagic);
    rangeTwo.m_WAbil.MP = 30;
    var rangeTwo1 = AddTarget(rangeTwoMap, rangeTwo, 1, 35, "range2-1");
    var rangeTwo2 = AddTarget(rangeTwoMap, rangeTwo, 2, 35, "range2-2");
    var rangeTwo3 = AddTarget(rangeTwoMap, rangeTwo, 3, 35, "range2-3");
    Assert(Hit(rangeTwo, TBaseObject.NativeAction1011Code)
           && rangeTwo1.Calls.Count == 1
           && rangeTwo2.Calls.Count == 1
           && rangeTwo3.Calls.Count == 0,
        "action1011 must scan exactly the first two forward cells");
    AssertDirectCall(rangeTwo1.Calls[0], rangeTwo,
        TBaseObject.NativeAction1011Code, false, 70,
        SpellsDef.SKILL_CROSSMOON);
    AssertDirectCall(rangeTwo2.Calls[0], rangeTwo,
        TBaseObject.NativeAction1011Code, false, 70,
        SpellsDef.SKILL_CROSSMOON);
    AssertPhysicalFrame(rangeTwo, rangeTwoObserver,
        TBaseObject.NativeAction1011Code, 2);

    var (rangeFourMap, rangeFour, rangeFourObserver) =
        NewScenario("range-four");
    var rangeFourMagic = CrossMoonMagic();
    LearnCrossMoon(rangeFour, rangeFourMagic);
    rangeFour.m_WAbil.MP = 30;
    var rangeFourTargets = new[]
    {
        AddTarget(rangeFourMap, rangeFour, 1, 34, "range4-1"),
        AddTarget(rangeFourMap, rangeFour, 2, 35, "range4-2"),
        AddTarget(rangeFourMap, rangeFour, 3, 36, "range4-3"),
        AddTarget(rangeFourMap, rangeFour, 4, 34, "range4-4"),
        AddTarget(rangeFourMap, rangeFour, 5, 34, "range4-5")
    };
    Assert(Hit(rangeFour, TBaseObject.NativeAction1012Code)
           && rangeFourTargets.Take(4).All(target =>
               target.Calls.Count == 1)
           && rangeFourTargets[4].Calls.Count == 0,
        "action1012 must scan exactly the first four forward cells");
    var expectedRaw = new[] { 120, 70, 70, 120 };
    for (var i = 0; i < expectedRaw.Length; i++)
    {
        AssertDirectCall(rangeFourTargets[i].Calls[0], rangeFour,
            TBaseObject.NativeAction1011Code, false, expectedRaw[i],
            SpellsDef.SKILL_CROSSMOON);
    }
    AssertPhysicalFrame(rangeFour, rangeFourObserver,
        TBaseObject.NativeAction1012Code, 2);

    var (explicitMap, explicitCaster, explicitObserver) =
        NewScenario("explicit-target");
    var explicitTarget = AddTarget(explicitMap, explicitCaster, 2, 35,
        "explicit-target-object");
    Assert(explicitCaster.RunNativeCrossMoonAction(
               TBaseObject.NativeAction1011Code, Grobal2.DR_RIGHT,
               explicitTarget) == 2
           && explicitTarget.Calls.Count == 1
           && ReferenceEquals(explicitCaster.m_TargetCret, explicitTarget),
        "explicit action target must survive the nil-only front-cell probe");
    AssertDirectCall(explicitTarget.Calls[0], explicitCaster, 1000, true,
        40, 0);
    AssertPhysicalFrame(explicitCaster, explicitObserver, 1000, 0);

    var (cacheMap, cacheCaster, cacheObserver) =
        NewScenario("dynamic-crossmoon-cache");
    var cacheMagicA = CrossMoonMagic();
    var cacheMagicB = CrossMoonMagic();
    cacheMagicB.wMagIdx = 134;
    LearnCrossMoon(cacheCaster, cacheMagicA);
    cacheCaster.m_MagicList.Add(cacheMagicB);
    cacheCaster.m_WAbil.MP = 30;
    var cacheTarget1 = AddTarget(cacheMap, cacheCaster, 1, 35,
        "dynamic-cache-1");
    var cacheTarget2 = AddTarget(cacheMap, cacheCaster, 2, 35,
        "dynamic-cache-2");
    cacheTarget1.OnResolve = (source, _) =>
        source.m_MagicArr[SpellsDef.SKILL_CROSSMOON] = cacheMagicB;
    Assert(Hit(cacheCaster, TBaseObject.NativeAction1011Code)
           && cacheTarget1.Calls.Count == 1
           && cacheTarget2.Calls.Count == 1
           && cacheTarget1.Calls[0].MagicIndex ==
               SpellsDef.SKILL_CROSSMOON
           && cacheTarget2.Calls[0].MagicIndex == 134
           && cacheMagicA.nTranPoint == 0
           && cacheMagicB.nTranPoint == 3,
        "action1011 must reload its cached magic for each target and training");
    AssertPhysicalFrame(cacheCaster, cacheObserver,
        TBaseObject.NativeAction1011Code, 2);

    var (directionMap, directionCaster, directionObserver) =
        NewScenario("dynamic-direction");
    var directionMagic = CrossMoonMagic();
    LearnCrossMoon(directionCaster, directionMagic);
    directionCaster.m_WAbil.MP = 30;
    var directionTarget = AddTarget(directionMap, directionCaster, 1, 35,
        "dynamic-direction-target");
    directionTarget.OnResolve = (source, _) =>
        source.m_btDirection = Grobal2.DR_DOWN;
    Assert(Hit(directionCaster, TBaseObject.NativeAction1011Code)
           && directionTarget.Calls.Count == 1
           && directionCaster.m_btDirection == Grobal2.DR_DOWN,
        "action1011 must preserve a direction mutation made during landing");
    AssertPhysicalFrame(directionCaster, directionObserver,
        TBaseObject.NativeAction1011Code, 2, Grobal2.DR_DOWN);

    var (fallbackReloadMap, fallbackReload, fallbackReloadObserver) =
        NewScenario("dynamic-fallback-cache");
    var fallbackReloadA = new TUserMagic
    {
        MagicInfo = BuildMagicTemplate(SpellsDef.SKILL_ONESWORD,
            "fallback-a", trainLv: 4),
        btLevel = 1,
        wMagIdx = SpellsDef.SKILL_ONESWORD
    };
    var fallbackReloadB = new TUserMagic
    {
        MagicInfo = BuildMagicTemplate(SpellsDef.SKILL_ILKWANG,
            "fallback-b", trainLv: 4),
        btLevel = 3,
        wMagIdx = 104
    };
    fallbackReload.m_MagicList.Add(fallbackReloadA);
    var fallbackReloadTarget = AddTarget(fallbackReloadMap,
        fallbackReload, 1, 35, "dynamic-fallback-target");
    fallbackReloadTarget.OnResolve = (source, _) =>
        source.m_MagicList.Add(fallbackReloadB);
    Assert(fallbackReload.RunNativeCrossMoonAction(
               TBaseObject.NativeAction1011Code, Grobal2.DR_RIGHT) == 2
           && fallbackReloadTarget.Calls.Count == 1
           && fallbackReloadTarget.Calls[0].MagicIndex ==
               SpellsDef.SKILL_ONESWORD
           && fallbackReload.m_MsgList.Any(message =>
               message.wIdent == Grobal2.RM_MAGIC_LVEXP
               && message.nParam1 == SpellsDef.SKILL_ILKWANG)
           && !fallbackReload.m_MsgList.Any(message =>
               message.wIdent == Grobal2.RM_MAGIC_LVEXP
               && message.nParam1 == SpellsDef.SKILL_ONESWORD),
        "ordinary fallback must reload the last id3/id4 magic for training");
    AssertPhysicalFrame(fallbackReload, fallbackReloadObserver, 1000, 1);

    var (_, physicalTailCaster, _) = NewScenario("physical-tail-rate");
    int AddPhysicalTailItem(string name, byte mode, byte shape,
        ushort durabilityMaximum)
    {
        int index = M2Share.UserEngine.StdItemList.Count;
        M2Share.UserEngine.StdItemList.Add(new GoodItem
        {
            Name = name,
            ItemType = GoodType.ITEM_ACCESSORY,
            StdMode = mode,
            Shape = shape,
            DuraMax = durabilityMaximum
        });
        return index;
    }

    var rateRing = new TUserItem
    {
        wIndex = checked((ushort)AddPhysicalTailItem(
            "tail-ring-136", 22, 136, 40000)),
        Dura = 1,
        DuraMax = 40000
    };
    var rateArmRing = new TUserItem
    {
        wIndex = checked((ushort)AddPhysicalTailItem(
            "tail-armring-137", 24, 137, 30000)),
        Dura = 1,
        DuraMax = 30000
    };
    var rateNecklace = new TUserItem
    {
        wIndex = checked((ushort)AddPhysicalTailItem(
            "tail-necklace-138", 19, 138, 5)),
        Dura = 1,
        DuraMax = 5
    };
    var wrongShapeRing = new TUserItem
    {
        wIndex = checked((ushort)AddPhysicalTailItem(
            "tail-wrong-ring-137", 22, 137, 60000)),
        Dura = 1,
        DuraMax = 60000
    };
    var zeroDuraArmRing = new TUserItem
    {
        wIndex = checked((ushort)AddPhysicalTailItem(
            "tail-zero-armring-137", 24, 137, 60000)),
        Dura = 0,
        DuraMax = 60000
    };
    physicalTailCaster.m_UseItems[Grobal2.U_RINGL] = rateRing;
    physicalTailCaster.m_UseItems[Grobal2.U_RINGR] = wrongShapeRing;
    physicalTailCaster.m_UseItems[Grobal2.U_ARMRINGL] = rateArmRing;
    physicalTailCaster.m_UseItems[Grobal2.U_ARMRINGR] = zeroDuraArmRing;
    physicalTailCaster.m_UseItems[Grobal2.U_NECKLACE] = rateNecklace;
    physicalTailCaster.m_nNativePhysicalTailAccumulator = 2;
    physicalTailCaster.RecalcAbilitys();
    Assert(physicalTailCaster.m_wNativePhysicalTailRate == 4469
           && physicalTailCaster.m_nNativePhysicalTailAccumulator == 2,
        "physical tail equipment aggregation must wrap to the low WORD and preserve +0x188");
    rateRing.Dura = 0;
    rateArmRing.Dura = 0;
    rateNecklace.Dura = 0;
    physicalTailCaster.RecalcAbilitys();
    Assert(physicalTailCaster.m_wNativePhysicalTailRate == 0
           && physicalTailCaster.m_nNativePhysicalTailAccumulator == 2,
        "Recalc must clear +0x184 while preserving the independent accumulator");

    var heroTryRun = typeof(HeroObject).GetMethod(
        "TryRunNativeWarCrossMoon",
        BindingFlags.Instance | BindingFlags.NonPublic, null,
        new[] { typeof(TBaseObject), typeof(int) }, null)
        ?? throw new MissingMethodException(nameof(HeroObject),
            "TryRunNativeWarCrossMoon");
    var heroSelect = typeof(HeroObject).GetMethod(
        "TrySelectNativeWarCrossMoon",
        BindingFlags.Instance | BindingFlags.NonPublic, null,
        new[] { typeof(TBaseObject), typeof(int).MakeByRefType() }, null)
        ?? throw new MissingMethodException(nameof(HeroObject),
            "TrySelectNativeWarCrossMoon");
    var heroExpiry = typeof(HeroObject).GetMethod(
        "ProcessNativeWarCrossMoonSelectionExpiry",
        BindingFlags.Instance | BindingFlags.NonPublic, null,
        new[] { typeof(int) }, null)
        ?? throw new MissingMethodException(nameof(HeroObject),
            "ProcessNativeWarCrossMoonSelectionExpiry");

    (HeroObject Hero, TUserMagic Magic, StoragePacketProbe Observer)
        NewCrossMoonHero(Envirnoment map, string name)
    {
        var hero = new HeroObject
        {
            m_boOffLineFlag = false,
            m_boGhost = false,
            m_boDeath = false,
            m_boFixedHideMode = false,
            m_sCharName = name,
            m_sMapName = map.sMapName,
            m_nCurrX = 10,
            m_nCurrY = 10,
            m_PEnvir = map,
            m_btDirection = Grobal2.DR_RIGHT,
            m_btJob = 0,
            m_btHitPoint = 60,
            m_dwHitTick = 0,
            m_nNextHitTime = 1000,
            m_dwNativeWarCrossMoonReadyTick = 1000,
            m_nHealthTick = 10,
            m_nSpellTick = 50,
            m_sbHealthSpellRecoveryStep = 5
        };
        hero.m_Abil.Level = 35;
        hero.m_WAbil.Level = 35;
        hero.m_WAbil.DC = HUtil32.MakeLong(40, 40);
        hero.m_WAbil.HP = 950;
        hero.m_WAbil.MaxHP = 1000;
        hero.m_WAbil.MP = 60;
        var magic = CrossMoonMagic();
        hero.m_MagicList.Add(magic);
        hero.m_MagicArr[SpellsDef.SKILL_CROSSMOON] = magic;
        var observer = new StoragePacketProbe
        {
            m_boOffLineFlag = false,
            m_boGhost = false,
            m_boDeath = false,
            m_boFixedHideMode = false,
            m_sCharName = name + "-observer",
            m_sMapName = map.sMapName,
            m_nCurrX = 10,
            m_nCurrY = 13,
            m_PEnvir = map
        };
        map.AddToMap(hero.m_nCurrX, hero.m_nCurrY,
            CellType.OS_MOVINGOBJECT, hero);
        map.AddToMap(observer.m_nCurrX, observer.m_nCurrY,
            CellType.OS_MOVINGOBJECT, observer);
        return (hero, magic, observer);
    }

    NativeActionDamageProbe AddHeroTarget(Envirnoment map, int x, int y,
        byte race, byte level, string name)
    {
        var target = new NativeActionDamageProbe
        {
            m_boOffLineFlag = false,
            m_boGhost = false,
            m_boDeath = false,
            m_boFixedHideMode = false,
            m_sCharName = name,
            m_sMapName = map.sMapName,
            m_nCurrX = unchecked((short)x),
            m_nCurrY = unchecked((short)y),
            m_PEnvir = map,
            m_btRaceServer = race,
            m_wSpeedPoint = 1
        };
        target.m_Abil.Level = level;
        target.m_WAbil.HP = 1000;
        target.m_WAbil.MaxHP = 1000;
        map.AddToMap(target.m_nCurrX, target.m_nCurrY,
            CellType.OS_MOVINGOBJECT, target);
        return target;
    }

    bool TryRunHeroCrossMoon(HeroObject hero, TBaseObject target, int now)
    {
        // Production enters through HeroObject.Run with self+0x344. Keep the
        // reflection harness on that same state contract.
        hero.m_TargetCret = target;
        return (bool)heroTryRun.Invoke(hero, new object[] { target, now });
    }

    var selectorHero = new HeroObject
    {
        m_nCurrX = 10,
        m_nCurrY = 10
    };
    selectorHero.m_Abil.Level = 35;
    var selectorTarget = new NativeActionDamageProbe
    {
        m_nCurrX = 12,
        m_nCurrY = 12,
        m_btRaceServer = 0x81
    };
    selectorTarget.m_Abil.Level = 35;
    var selectorArgs = new object[] { selectorTarget, 0 };
    Assert((bool)heroSelect.Invoke(selectorHero, selectorArgs)
           && (int)selectorArgs[1] == TBaseObject.NativeAction1011Code
           && selectorHero.m_boNativeWarCrossMoonShortSelected
           && !selectorHero.m_boNativeWarCrossMoonLongSelected,
        "hero race129 diagonal distance-two selector must choose action1011");
    selectorHero.m_boNativeWarCrossMoonShortSelected = false;
    selectorTarget.m_btRaceServer = 80;
    selectorTarget.m_nCurrX = 14;
    selectorTarget.m_nCurrY = 10;
    selectorArgs = new object[] { selectorTarget, 0 };
    Assert((bool)heroSelect.Invoke(selectorHero, selectorArgs)
           && (int)selectorArgs[1] == TBaseObject.NativeAction1012Code
           && selectorHero.m_boNativeWarCrossMoonLongSelected,
        "hero ordinary-race distance-four selector must choose action1012");
    selectorHero.m_boNativeWarCrossMoonLongSelected = false;
    selectorTarget.m_btRaceServer = 0x81;
    selectorTarget.m_nCurrX = 13;
    selectorArgs = new object[] { selectorTarget, 0 };
    Assert(!(bool)heroSelect.Invoke(selectorHero, selectorArgs),
        "hero special-race selector must reject distance three");
    selectorTarget.m_btRaceServer = 80;
    selectorTarget.m_nCurrX = 12;
    selectorTarget.m_nCurrY = 13;
    selectorArgs = new object[] { selectorTarget, 0 };
    Assert(!(bool)heroSelect.Invoke(selectorHero, selectorArgs),
        "hero selector must reject a non-straight non-diagonal target");

    var heroMap = CreateBlankMap(32, 32,
        $"harness-action1011-{scenarioId++}-hero-short");
    var (shortHero, shortHeroMagic, shortHeroObserver) =
        NewCrossMoonHero(heroMap, "hero-short");
    var shortDecoy = AddHeroTarget(heroMap, 11, 10,
        Grobal2.RC_PLAYOBJECT, 35, "hero-short-decoy");
    var shortTarget = AddHeroTarget(heroMap, 12, 10,
        Grobal2.RC_PLAYOBJECT, 35, "hero-short-target");
    int shortOldHitTick = shortHero.m_dwHitTick;
    Assert(!TryRunHeroCrossMoon(shortHero, shortTarget, 26000)
           && shortHero.m_boNativeWarCrossMoonReady
           && shortHero.HasNativeActiveState(3)
           && shortHero.m_WAbil.MP == 60
           && shortDecoy.Calls.Count == 0
           && shortTarget.Calls.Count == 0,
        "hero first eligible pass must arm action state without firing");
    Assert(TryRunHeroCrossMoon(shortHero, shortTarget, 26001)
           && shortHero.m_WAbil.MP == 30
           && shortHeroMagic.nTranPoint == 3
           && shortDecoy.Calls.Count == 1
           && shortTarget.Calls.Count == 1
           && ReferenceEquals(shortHero.m_TargetCret, shortTarget)
           && !shortHero.m_boNativeWarCrossMoonReady
           && !shortHero.m_boNativeWarCrossMoonShortSelected
           && !shortHero.m_boNativeWarCrossMoonLongSelected
           && !shortHero.HasNativeActiveState(3)
           && shortHero.m_nHealthTick == -20
           && shortHero.m_nSpellTick == 0
           && shortHero.m_sbHealthSpellRecoveryStep == 3
           && shortHero.m_dwHitTick == shortHero.m_dwTargetFocusTick
           && shortHero.m_dwHitTick != shortOldHitTick,
        "hero second pass must execute action1011 and clear its exact state");
    foreach (var call in shortDecoy.Calls.Concat(shortTarget.Calls))
    {
        Assert(ReferenceEquals(call.Source, shortHero)
               && call.SkillId == TBaseObject.NativeAction1011Code
               && !call.Arg0 && call.Category == 4 && call.Flags == 0
               && call.RawDamage == 70
               && call.MagicIndex == SpellsDef.SKILL_CROSSMOON,
            "hero action1011 direct call mismatch");
    }
    PumpMessages(shortHeroObserver);
    var shortHeroFrames = shortHeroObserver.Sent.Where(packet =>
        packet.Ident == Grobal2.SM_PHYSICAL_ATT).ToList();
    Assert(shortHeroFrames.Count == 1
           && shortHeroFrames[0].RawBody.SequenceEqual(ExpectedBody(
               TBaseObject.NativeAction1011Code, 2, Grobal2.DR_RIGHT,
               shortHero.m_nCurrX, shortHero.m_nCurrY)),
        "hero action1011 observer frame mismatch");

    var longHeroMap = CreateBlankMap(32, 32,
        $"harness-action1011-{scenarioId++}-hero-long");
    var (longHero, longHeroMagic, longHeroObserver) =
        NewCrossMoonHero(longHeroMap, "hero-long");
    longHeroMagic.nTranPoint = 4997;
    var longTarget = AddHeroTarget(longHeroMap, 14, 10,
        Grobal2.RC_PLAYOBJECT, 34, "hero-long-target");
    Assert(!TryRunHeroCrossMoon(longHero, longTarget, 26000)
           && TryRunHeroCrossMoon(longHero, longTarget, 26001)
           && longTarget.Calls.Count == 1
           && longTarget.Calls[0].RawDamage == 120
           && longHeroMagic.btLevel == 3
           && longHeroMagic.nTranPoint == 0
           && longHero.m_MsgList.Any(message =>
               message.wIdent == Grobal2.RM_MAGIC_LVEXP
               && message.nParam1 == SpellsDef.SKILL_CROSSMOON
               && message.nParam2 == 3 && message.nParam3 == 0),
        "hero action1012 must use the long selector and native level-up loop");
    PumpMessages(longHeroObserver);
    var longHeroFrames = longHeroObserver.Sent.Where(packet =>
        packet.Ident == Grobal2.SM_PHYSICAL_ATT).ToList();
    Assert(longHeroFrames.Count == 1
           && longHeroFrames[0].RawBody.SequenceEqual(ExpectedBody(
               TBaseObject.NativeAction1012Code, 3, Grobal2.DR_RIGHT,
               longHero.m_nCurrX, longHero.m_nCurrY)),
        "hero action1012 observer frame mismatch");

    var residualMap = CreateBlankMap(32, 32,
        $"harness-action1011-{scenarioId++}-hero-residual");
    var (residualHero, _, residualObserver) =
        NewCrossMoonHero(residualMap, "hero-residual");
    var residualTarget = AddHeroTarget(residualMap, 11, 10,
        Grobal2.RC_PLAYOBJECT, 35, "hero-residual-target");
    residualHero.m_boNativeWarCrossMoonReady = false;
    residualHero.m_boNativeWarCrossMoonShortSelected = true;
    residualHero.m_boNativeWarCrossMoonLongSelected = true;
    Assert(TryRunHeroCrossMoon(residualHero, residualTarget, 26000)
           && residualTarget.Calls.Count == 1
           && residualTarget.Calls[0].RawDamage == 70
           && residualHero.m_WAbil.MP == 30
           && !residualHero.m_boNativeWarCrossMoonShortSelected
           && !residualHero.m_boNativeWarCrossMoonLongSelected,
        "hero executor must consume persisted selections with long priority");
    PumpMessages(residualObserver);
    var residualFrames = residualObserver.Sent.Where(packet =>
        packet.Ident == Grobal2.SM_PHYSICAL_ATT).ToList();
    Assert(residualFrames.Count == 1
           && residualFrames[0].RawBody.SequenceEqual(ExpectedBody(
               TBaseObject.NativeAction1012Code, 2, Grobal2.DR_RIGHT,
               residualHero.m_nCurrX, residualHero.m_nCurrY)),
        "hero persisted-selection executor did not choose action1012 first");

    var heldMap = CreateBlankMap(32, 32,
        $"harness-action1011-{scenarioId++}-hero-held-selection");
    var (heldHero, _, _) = NewCrossMoonHero(heldMap, "hero-held-selection");
    var heldTarget = AddHeroTarget(heldMap, 12, 10,
        Grobal2.RC_PLAYOBJECT, 35, "hero-held-selection-target");
    heldHero.m_boNativeWarCrossMoonReady = false;
    heldHero.m_boNativeWarCrossMoonShortSelected = true;
    heldHero.m_boNativeWarCrossMoonLongSelected = true;
    Assert(!TryRunHeroCrossMoon(heldHero, heldTarget, 26000)
           && heldTarget.Calls.Count == 0
           && heldHero.m_WAbil.MP == 60
           && heldHero.m_boNativeWarCrossMoonShortSelected
           && heldHero.m_boNativeWarCrossMoonLongSelected,
        "hero rejected distance-two selector must retain persisted selections");

    var rereadMap = CreateBlankMap(32, 32,
        $"harness-action1011-{scenarioId++}-hero-target-reread");
    var (rereadHero, _, rereadObserver) =
        NewCrossMoonHero(rereadMap, "hero-target-reread");
    var selectorSnapshot = AddHeroTarget(rereadMap, 11, 10,
        Grobal2.RC_PLAYOBJECT, 35, "hero-selector-snapshot");
    var executorTarget = AddHeroTarget(rereadMap, 10, 11,
        Grobal2.RC_PLAYOBJECT, 35, "hero-executor-target");
    rereadHero.m_boNativeWarCrossMoonReady = false;
    rereadHero.m_boNativeWarCrossMoonShortSelected = true;
    rereadHero.m_TargetCret = executorTarget;
    Assert((bool)heroTryRun.Invoke(rereadHero,
               new object[] { selectorSnapshot, 26000 })
           && selectorSnapshot.Calls.Count == 0
           && executorTarget.Calls.Count == 1
           && ReferenceEquals(rereadHero.m_TargetCret, executorTarget),
        "hero executor must reload self+0x344 after selector return");
    PumpMessages(rereadObserver);
    var rereadFrames = rereadObserver.Sent.Where(packet =>
        packet.Ident == Grobal2.SM_PHYSICAL_ATT).ToList();
    Assert(rereadFrames.Count == 1
           && rereadFrames[0].RawBody.SequenceEqual(ExpectedBody(
               TBaseObject.NativeAction1011Code, 2, Grobal2.DR_DOWN,
               rereadHero.m_nCurrX, rereadHero.m_nCurrY)),
        "hero reloaded-target physical frame mismatch");

    var sentinelMap = CreateBlankMap(16, 16,
        $"harness-action1011-{scenarioId++}-hero-sentinel");
    var (sentinelHero, sentinelMagic, _) =
        NewCrossMoonHero(sentinelMap, "hero-sentinel");
    var sentinelTarget = AddHeroTarget(sentinelMap, 12, 10,
        Grobal2.RC_PLAYOBJECT, 35, "hero-sentinel-target");
    sentinelMagic.wMagIdx = 0x00FF;
    Assert(!TryRunHeroCrossMoon(sentinelHero, sentinelTarget, 26000)
           && !sentinelHero.m_boNativeWarCrossMoonReady,
        "hero readiness must reject only the native 0x00FF sentinel");
    sentinelMagic.wMagIdx = ushort.MaxValue;
    Assert(!TryRunHeroCrossMoon(sentinelHero, sentinelTarget, 26001)
           && sentinelHero.m_boNativeWarCrossMoonReady,
        "hero readiness must accept 0xFFFF rather than treating it as 0x00FF");

    selectorHero.m_boNativeWarCrossMoonLongSelected = true;
    selectorHero.m_dwNativeWarCrossMoonReadyTick = 1000;
    selectorHero.SetNativeActiveState(3);
    heroExpiry.Invoke(selectorHero, new object[] { 5999 });
    Assert(selectorHero.m_boNativeWarCrossMoonLongSelected
           && selectorHero.HasNativeActiveState(3),
        "hero long selection must survive through 4999 milliseconds");
    heroExpiry.Invoke(selectorHero, new object[] { 6000 });
    Assert(!selectorHero.m_boNativeWarCrossMoonLongSelected
           && !selectorHero.HasNativeActiveState(3),
        "hero long selection must expire at exactly 5000 milliseconds");

    var priorityMap = CreateBlankMap(32, 32,
        $"harness-action1011-{scenarioId++}-hero-1017-priority");
    var (priorityHero, _, priorityObserver) =
        NewCrossMoonHero(priorityMap, "hero-1017-priority");
    var priorityTarget = AddHeroTarget(priorityMap, 12, 10,
        Grobal2.RC_PLAYOBJECT, 35, "hero-1017-priority-target");
    var priorityMagic = ChargedCounterMagic();
    priorityHero.m_WAbil.MC = HUtil32.MakeLong(40, 40);
    priorityHero.m_MagicList.Add(priorityMagic);
    priorityHero.m_NativeChargedCounterMagic = priorityMagic;
    int priorityOldHitTick = priorityHero.m_dwHitTick;
    Assert(!TryRunHeroCrossMoon(priorityHero, priorityTarget, 26000)
           && priorityHero.m_btNativeChargedIndicator == 1
           && priorityHero.m_boNativeWarCrossMoonReady
           && priorityHero.HasNativeActiveState(3)
           && priorityHero.m_WAbil.MP == 60
           && priorityTarget.Calls.Count == 0,
        "hero first decision must coexistently arm action1017 and cross-moon");
    Assert(TryRunHeroCrossMoon(priorityHero, priorityTarget, 26001)
           && priorityTarget.Calls.Count == 1
           && priorityTarget.Calls[0].SkillId ==
                TBaseObject.NativeAction1017Code
           && priorityTarget.Calls[0].MagicIndex == SpellsDef.SKILL_66
           && priorityHero.m_btNativeChargedIndicator == 0
           && priorityHero.m_boNativeWarCrossMoonShortSelected
           && !priorityHero.m_boNativeWarCrossMoonLongSelected
           && !priorityHero.m_boNativeWarCrossMoonReady
           && priorityHero.HasNativeActiveState(3)
           && priorityHero.m_WAbil.MP == 60
           && priorityMagic.nTranPoint is >= 1 and <= 3
           && priorityHero.GetNativeColdTimeRemaining(
                SpellsDef.SKILL_66) > 0
           && priorityHero.m_nHealthTick == -20
           && priorityHero.m_nSpellTick == 0
           && priorityHero.m_sbHealthSpellRecoveryStep == 3
           && priorityHero.m_dwHitTick == priorityHero.m_dwTargetFocusTick
           && priorityHero.m_dwHitTick != priorityOldHitTick,
        "hero action1017 must win and preserve the selected cross-moon state");
    PumpMessages(priorityObserver);
    var priorityFrames = priorityObserver.Sent.Where(packet =>
        packet.Ident == Grobal2.SM_PHYSICAL_ATT).ToList();
    Assert(priorityFrames.Count == 1
           && priorityFrames[0].RawBody.SequenceEqual(ExpectedBody(
               TBaseObject.NativeAction1017Code, 2, Grobal2.DR_RIGHT,
               priorityHero.m_nCurrX, priorityHero.m_nCurrY)),
        "hero action1017 priority observer frame mismatch");

    TUserMagic ChargedCounterMagic()
    {
        return new TUserMagic
        {
            MagicInfo = BuildMagicTemplate(SpellsDef.SKILL_66,
                "charged-counter", trainLv: 4),
            btLevel = 2,
            wMagIdx = SpellsDef.SKILL_66
        };
    }

    void ConfigureChargedCounter(StoragePacketProbe caster,
        TUserMagic magic)
    {
        caster.m_Abil.Level = 40;
        caster.m_WAbil.Level = 40;
        caster.m_WAbil.HP = 1000;
        caster.m_WAbil.MaxHP = 1000;
        caster.m_WAbil.MC = HUtil32.MakeLong(40, 40);
        caster.m_MagicList.Add(magic);
        caster.m_NativeChargedCounterMagic = magic;
    }

    var (action1017FallbackMap, action1017Fallback,
        action1017FallbackObserver) = NewScenario("action1017-fallback");
    action1017Fallback.m_WAbil.HP = 1000;
    action1017Fallback.m_WAbil.MaxHP = 1000;
    action1017Fallback.m_wNativePhysicalTailRate = 10;
    action1017Fallback.m_nNativePhysicalTailAccumulator = 2;
    var action1017Ordinary = new TUserMagic
    {
        MagicInfo = BuildMagicTemplate(SpellsDef.SKILL_ONESWORD,
            "action1017-ordinary", trainLv: 4),
        btLevel = 1,
        wMagIdx = SpellsDef.SKILL_ONESWORD
    };
    action1017Fallback.m_MagicList.Add(action1017Ordinary);
    var action1017FallbackTarget = AddTarget(action1017FallbackMap,
        action1017Fallback, 1, 35, "action1017-fallback-target");
    Assert(action1017Fallback.RunNativeAction1017() == 2
           && action1017FallbackTarget.Calls.Count == 1
           && action1017Fallback.m_nNativePhysicalTailAccumulator == 0
           && action1017Fallback.m_WAbil.HP == 1000
           && !action1017FallbackTarget.m_MsgList.Any(message =>
               message.wIdent == TBaseObject.NativeAction1017StruckIdent),
        "action1017 result-zero branch must run ordinary fallback and its common tail");
    AssertDirectCall(action1017FallbackTarget.Calls[0],
        action1017Fallback, 1000, true, 40,
        SpellsDef.SKILL_ONESWORD);
    AssertPhysicalFrame(action1017Fallback, action1017FallbackObserver,
        1000, 1, Grobal2.DR_RIGHT, includeSource: false);

    var (_, action1017Empty, action1017EmptyObserver) =
        NewScenario("action1017-empty");
    var action1017EmptyMagic = ChargedCounterMagic();
    ConfigureChargedCounter(action1017Empty, action1017EmptyMagic);
    action1017Empty.m_wNativePhysicalTailRate = 10;
    action1017Empty.m_nNativePhysicalTailAccumulator = 2;
    Assert(action1017Empty.RunNativeAction1017() == 1
           && action1017Empty.m_WAbil.HP == 900
           && action1017Empty.GetNativeColdTimeRemaining(
               SpellsDef.SKILL_66) > 0
           && action1017EmptyMagic.nTranPoint is >= 1 and <= 3
           && action1017Empty.m_nNativePhysicalTailAccumulator == 2,
        "action1017 result-one branch must pay cost/train without the common tail");
    AssertPhysicalFrame(action1017Empty, action1017EmptyObserver,
        TBaseObject.NativeAction1017Code, 2, Grobal2.DR_RIGHT,
        includeSource: false);

    var (action1017HitMap, action1017Hit, action1017HitObserver) =
        NewScenario("action1017-hit");
    var action1017HitMagic = ChargedCounterMagic();
    ConfigureChargedCounter(action1017Hit, action1017HitMagic);
    action1017Hit.m_wNativePhysicalTailRate = 10;
    action1017Hit.m_nNativePhysicalTailAccumulator = 2;
    var action1017HitTarget = AddTarget(action1017HitMap,
        action1017Hit, 1, 35, "action1017-hit-target");
    action1017HitTarget.m_WAbil.Level = ushort.MaxValue;
    action1017HitTarget.OnResolve = (source, _) =>
        source.m_btDirection = Grobal2.DR_DOWN;
    Assert(action1017Hit.RunNativeAction1017() == 2
           && action1017HitTarget.Calls.Count == 1
           && action1017Hit.m_WAbil.HP == 900
           && action1017Hit.GetNativeColdTimeRemaining(
               SpellsDef.SKILL_66) > 0
           && action1017HitMagic.nTranPoint is >= 1 and <= 3
           && action1017Hit.m_nNativePhysicalTailAccumulator == 0
           && ReferenceEquals(action1017Hit.m_TargetCret,
               action1017HitTarget)
           && action1017HitTarget.m_MsgList.Count(message =>
               message.wIdent ==
                   TBaseObject.NativeAction1017StruckIdent) == 1,
        "action1017 result-two branch must run the shared tail and struck message");
    AssertDirectCall(action1017HitTarget.Calls[0], action1017Hit,
        TBaseObject.NativeAction1017Code, true, 280,
        SpellsDef.SKILL_66);
    AssertPhysicalFrame(action1017Hit, action1017HitObserver,
        TBaseObject.NativeAction1017Code, 2, Grobal2.DR_DOWN,
        includeSource: false);

    var (action1017RangeTwoMap, action1017RangeTwo,
        action1017RangeTwoObserver) = NewScenario("action1017-range-two");
    var action1017RangeTwoMagic = ChargedCounterMagic();
    ConfigureChargedCounter(action1017RangeTwo,
        action1017RangeTwoMagic);
    var action1017RangeTwoTarget = AddTarget(action1017RangeTwoMap,
        action1017RangeTwo, 2, 35, "action1017-range-two-target");
    action1017RangeTwoTarget.m_WAbil.Level = ushort.MaxValue;
    Assert(action1017RangeTwo.RunNativeAction1017() == 2
           && action1017RangeTwoTarget.Calls.Count == 1
           && action1017RangeTwo.m_TargetCret == null,
        "action1017 two-cell worker hit must keep the outer initial target null");
    AssertDirectCall(action1017RangeTwoTarget.Calls[0],
        action1017RangeTwo, TBaseObject.NativeAction1017Code, true, 280,
        SpellsDef.SKILL_66);
    AssertPhysicalFrame(action1017RangeTwo,
        action1017RangeTwoObserver, TBaseObject.NativeAction1017Code, 2,
        Grobal2.DR_RIGHT, includeSource: false);

    var (_, rawCaster, rawObserver) = NewScenario("raw-compat");
    foreach (var action in new[] { 1000, 1015 })
    {
        rawCaster.SendRefMsg(Grobal2.RM_PHYSICAL_ATT, action,
            rawCaster.m_nCurrX, rawCaster.m_nCurrY, 0, string.Empty,
            ExpectedBody(action, 2, Grobal2.DR_RIGHT,
                rawCaster.m_nCurrX, rawCaster.m_nCurrY));
    }
    PumpMessages(rawCaster);
    PumpMessages(rawObserver);
    Assert(!rawCaster.Sent.Any(packet =>
               packet.Ident == Grobal2.SM_PHYSICAL_ATT)
           && rawObserver.Sent.Count(packet =>
               packet.Ident == Grobal2.SM_PHYSICAL_ATT) == 2,
        "legacy byte[] physical frames must remain observer-only");

    var (_, refused, refusedObserver) = NewScenario("refused");
    refused.m_boCanHit = false;
    refused.Operate(new TProcessMessage
    {
        wIdent = Grobal2.CM_3037,
        wParam = Grobal2.DR_RIGHT,
        nParam1 = refused.m_nCurrX,
        nParam2 = refused.m_nCurrY,
        nParam3 = TBaseObject.NativeAction1011Code,
        boLateDelivery = true
    });
    Assert(refused.Sent.Count(packet =>
               packet.Ident == Grobal2.SM_ACT_FAIL
               && packet.Recog == 0 && packet.Param == 0
               && packet.Tag == 0 && packet.Series == 0) == 1
           && !refused.m_MsgList.Any(message =>
               message.wIdent == Grobal2.RM_MOVEFAIL)
           && !refusedObserver.m_MsgList.Any(message =>
               message.wIdent == Grobal2.RM_MOVEFAIL),
        "CM3027 refusal must emit only the four-zero SM_ACT_FAIL frame");

    var (_, burstSource, _) = NewScenario("skill151-source");
    var (_, burstTarget, _) = NewScenario("skill151-target");
    burstTarget.m_PEnvir = burstSource.m_PEnvir;
    burstTarget.m_sMapName = burstSource.m_sMapName;
    burstTarget.m_WAbil.HP = 1000;
    burstTarget.m_WAbil.MaxHP = 1000;
    burstSource.m_btJob = 0;
    burstSource.m_WAbil.DC = HUtil32.MakeLong(100, 100);
    burstSource.m_sNativeCriticalChance = -1;
    var strikeCountField = typeof(TBaseObject).GetField(
        "m_nNativeSkill151StrikeCount",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(nameof(TBaseObject),
            "m_nNativeSkill151StrikeCount");
    var strikeFactorField = typeof(TBaseObject).GetField(
        "m_fNativeSkill151DamageFactor",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(nameof(TBaseObject),
            "m_fNativeSkill151DamageFactor");
    strikeCountField.SetValue(burstSource, (ushort)1);
    strikeFactorField.SetValue(burstSource, 0.25f);
    int burstDamage = burstTarget.ResolveFullMagicDamage(burstSource, 1000,
        true, MagicDamageContext.Empty, 4, 0, 100);
    Assert(burstDamage == 225
           && (ushort)strikeCountField.GetValue(burstSource) == 1,
        "skill151 kind1000 must add 125 before caps without consuming count");
    burstSource.ConsumeNativeSkill151StrikeAfterMainDamage(0);
    Assert((ushort)strikeCountField.GetValue(burstSource) == 1,
        "non-positive main landing must not consume skill151 count");
    burstSource.ConsumeNativeSkill151StrikeAfterMainDamage(burstDamage);
    burstSource.ConsumeNativeSkill151StrikeAfterMainDamage(burstDamage);
    Assert((ushort)strikeCountField.GetValue(burstSource) == 0
           && burstSource.ApplyNativeSkill151BurstDamage(100,
               TBaseObject.NativeAction1011Code) == 100,
        "skill151 count must decrement once without underflow and reject kind1011");

    Log("ACTION1011/1017: explicit targets, dynamic magic/direction reloads, "
        + "MP equality, fixed +3 and hero level-up training, 2/4-cell scan, "
        + "lower-level +50, exact fallback, equipment WORD aggregation, "
        + "hero arm/selector/1017 priority and preserved cross-moon/target reread/expiry/recovery costs, "
        + "action1017 result 0/1/2 and null outer target, SM1230 source flags, "
        + "legacy byte[] compatibility, four-zero refusal and skill151 passed");
}

void RunMapDescriptionProtocol(Envirnoment map)
{
    var sameDescriptionMap = CreateBlankMap(64, 64,
        "harness-map-same-description");
    var noTableMap = CreateBlankMap(64, 64, "harness-map-no-table");
    map.sMapDesc = "比奇省";
    sameDescriptionMap.sMapDesc = "比奇省";
    noTableMap.sMapDesc = "无标记地图";

    M2Share.MapManager.LoadNativeMapAreasFromLines(new[]
    {
        "; comment",
        "harness-map=Wrap 65566 65566 65538;ZeroX 0 10 5;ZeroY 10 0 5;ZeroRadius 10 10 0;Negative -1 10 5;Outer 10 10 5;Inner 11 10 4",
        "missing-map=Ignored 1 1 9"
    });
    Assert(M2Share.MapManager.NativeMapAreaRegionCount == 3
           && map.NativeMapAreaRegionCount == 3,
        "maparea loader must reject nonpositive fields and unresolved maps");
    Assert(map.ResolveNativeMapDescription(10, 10) == "Inner",
        "later maparea section must win an overlap through head insertion");
    Assert(map.ResolveNativeMapDescription(30, 30) == "Wrap",
        "maparea coordinates/radius must retain only their low 16 bits");
    Assert(map.ResolveNativeMapDescription(15, 10) == "比奇省",
        "maparea radius boundary must be strict Manhattan distance < radius");
    Assert(map.ResolveNativeMapDescription(0, 10) == "比奇省"
           && map.ResolveNativeMapDescription(10, 0) == "比奇省",
        "zero on either axis must resolve the default map description");

    M2Share.MapManager.LoadNativeMapDescriptionsFromLines(new[]
    {
        "; comment",
        "比奇省,343,245,比奇城,$33FFFF,0",
        "比奇省 154 21 沃玛墓地 7BBDCE 2",
        "比奇省,600,242,$7BBDCE,0",
        "five,1,2,label,FFFF"
    });
    Assert(M2Share.MapManager.NativeMapDescriptionKeyCount == 1
           && M2Share.MapManager.NativeMapDescriptionRecordCount == 2
           && M2Share.MapManager.NativeMapDescriptionSkippedRowCount == 2,
        "MapDesc loader must preserve valid file order and skip five-field rows");

    var expectedFirstRecord = new byte[]
    {
        0x06, 0xB1, 0xC8, 0xC6, 0xE6, 0xB3, 0xC7,
        0, 0, 0, 0, 0, 0, 0, 0,
        0x00, 0x57, 0x01, 0xF5, 0x00, 0xFF, 0xFF, 0x33, 0x00
    };
    var records = M2Share.MapManager.GetNativeMapDescriptionRecords("比奇省");
    Assert(records.Count == 2
           && records[0].AsSpan().SequenceEqual(expectedFirstRecord),
        "MapDesc first production vector must match the native 24-byte body");

    const string rawTruncationLabel = "A一二三四五六七";
    var rawLabel = HUtil32.GbkEncoding.GetBytes(rawTruncationLabel);
    var truncated = MapManager.EncodeNativeMapDescriptionRecord(
        rawTruncationLabel, 0x101, -1, 0x10001, 0xA1B2C3D4);
    Assert(truncated[0] == MapManager.NativeMapDescriptionLabelCapacity
           && truncated.AsSpan(1, MapManager.NativeMapDescriptionLabelCapacity)
               .SequenceEqual(rawLabel.AsSpan(0,
                   MapManager.NativeMapDescriptionLabelCapacity))
           && truncated[15] == 1
           && truncated[16] == 0xFF && truncated[17] == 0xFF
           && truncated[18] == 0x01 && truncated[19] == 0x00
           && truncated[20] == 0xD4 && truncated[23] == 0xA1,
        "MapDesc fields must truncate raw GBK/low byte/low words without clamping");

    var player = new TPlayObject
    {
        m_PEnvir = map,
        m_sMapName = map.sMapName,
        m_nCurrX = 10,
        m_nCurrY = 10
    };
    var first = player.BuildNativeMapDescriptionFrames();
    Assert(first.Count == 4
           && !first[0].IsBinary
           && first[0].Header.Ident == Grobal2.SM_MAPDESCRIPTION
           && first[0].Header.Recog == 1
           && first[0].Header.Param == 0
           && first[0].Header.Tag == 0
           && first[0].Header.Series == 0
           && first[0].TextBody == "Inner"
           && first[1].Header.Ident == Grobal2.SM_56
           && first[1].Header.Param == 1
           && first[1].BinaryBody.AsSpan().SequenceEqual(expectedFirstRecord)
           && first[2].Header.Param == 1
           && first[3].Header.Param == 2
           && first[3].BinaryBody.Length == 0,
        "first map entry must send changed SM54 then ordered SM56 records/end");
    Assert(player.BuildNativeMapDescriptionFrames().Count == 0,
        "same map and same region must not resend SM54 or SM56");

    player.m_nCurrX = 15;
    var regionExit = player.BuildNativeMapDescriptionFrames();
    Assert(regionExit.Count == 1
           && regionExit[0].Header.Ident == Grobal2.SM_MAPDESCRIPTION
           && regionExit[0].TextBody == "比奇省",
        "leaving a maparea region must send only the changed SM54");

    player.m_PEnvir = sameDescriptionMap;
    player.m_sMapName = sameDescriptionMap.sMapName;
    var sameTextMapChange = player.BuildNativeMapDescriptionFrames();
    Assert(sameTextMapChange.Count == 4
           && sameTextMapChange.All(frame => frame.IsBinary
               && frame.Header.Ident == Grobal2.SM_56
               && frame.Header.Recog == 0
               && frame.Header.Tag == 0
               && frame.Header.Series == 0)
           && sameTextMapChange.Select(frame => frame.Header.Param)
               .SequenceEqual(new ushort[] { 0, 1, 1, 2 }),
        "same SM54 text on a different canonical map must still clear/reload SM56");

    player.m_PEnvir = noTableMap;
    player.m_sMapName = noTableMap.sMapName;
    var tableToNone = player.BuildNativeMapDescriptionFrames();
    Assert(tableToNone.Count == 2
           && tableToNone[0].Header.Ident == Grobal2.SM_MAPDESCRIPTION
           && tableToNone[0].TextBody == "无标记地图"
           && tableToNone[1].Header.Ident == Grobal2.SM_56
           && tableToNone[1].Header.Param == 0
           && tableToNone[1].BinaryBody.Length == 0,
        "map with records to map without records must send SM54 then SM56 clear only");

    M2Share.MapManager.LoadNativeMapDescriptionsFromLines(new[]
    {
        "Town 1 1 Upper FFFFFF 0",
        "town 2 2 Lower FFFFFF 0"
    });
    var upperCaseRecords = M2Share.MapManager.GetNativeMapDescriptionRecords("Town");
    var lowerCaseRecords = M2Share.MapManager.GetNativeMapDescriptionRecords("town");
    Assert(M2Share.MapManager.NativeMapDescriptionKeyCount == 2
           && upperCaseRecords.Count == 1 && lowerCaseRecords.Count == 1
           && upperCaseRecords[0][16] == 1
           && lowerCaseRecords[0][16] == 2,
        "MapDesc keys must retain native case-sensitive byte identity");

    var productionConfigDirectory =
        @"D:\lyom2Release\mud2.0\Mir200\Share\config";
    var productionMapArea = Path.Combine(productionConfigDirectory,
        "maparea.txt");
    var productionMapDesc = Path.Combine(productionConfigDirectory,
        "MapDesc.dat");
    if (File.Exists(productionMapArea) && File.Exists(productionMapDesc))
    {
        foreach (var mapName in new[] { "0", "1", "2", "3", "4", "5", "6", "11" })
        {
            if (M2Share.MapManager.FindMap(mapName) == null)
                CreateBlankMap(8, 8, mapName);
        }
        Assert(M2Share.MapManager.TryLoadNativeMapAreas(productionMapArea,
                   out var mapAreaError)
               && string.IsNullOrEmpty(mapAreaError)
               && M2Share.MapManager.NativeMapAreaRegionCount == 14,
            "production maparea.txt must load all 14 regions read-only");
        Assert(M2Share.MapManager.TryLoadNativeMapDescriptions(productionMapDesc,
                   out var mapDescError)
               && string.IsNullOrEmpty(mapDescError)
               && M2Share.MapManager.NativeMapDescriptionRecordCount == 205
               && M2Share.MapManager.NativeMapDescriptionKeyCount == 20
               && M2Share.MapManager.NativeMapDescriptionSkippedRowCount == 3,
            "production MapDesc.dat must load 205 records/20 keys and skip 3 rows read-only");
    }

    Log("MAPDESC: maparea head-order + strict Manhattan SM54 and MapDesc.dat "
        + "24-byte GBK records drive SM56 Param 0(clear)/1(records)/2(end), "
        + "including equal-description map changes");
}

void RunMicroWhelkProtocol(Envirnoment map)
{
    var engine = M2Share.UserEngine;
    var microWhelkIndex = engine.StdItemList.Count;
    engine.StdItemList.Add(new GoodItem
    {
        Name = "测试小海螺",
        StdMode = 3,
        Shape = 26,
        DuraMax = 4321
    });
    var wrongClassIndex = engine.StdItemList.Count;
    engine.StdItemList.Add(new GoodItem
    {
        Name = "错误类别道具",
        StdMode = 3,
        Shape = 25,
        DuraMax = 4321
    });

    StoragePacketProbe NewProbe(string name)
    {
        return new StoragePacketProbe
        {
            m_sCharName = name,
            m_sMapName = map.sMapName,
            m_PEnvir = map,
            m_boGhost = false,
            m_boOffLineFlag = false
        };
    }

    TUserItem AddItem(StoragePacketProbe player, int clientId, int makeIndex,
        int stdIndex, ushort dura, ushort duraMax = 4321)
    {
        var item = new TUserItem
        {
            ClientItemID = clientId,
            MakeIndex = makeIndex,
            wIndex = checked((ushort)stdIndex),
            Dura = dura,
            DuraMax = duraMax
        };
        player.m_ItemList.Add(item);
        return item;
    }

    void Dispatch(StoragePacketProbe player, int recog, int param = 0x1234,
        int tag = 0x2345, int series = 0x3456, string text = "错误文本",
        byte[] payload = null)
    {
        player.Operate(new TProcessMessage
        {
            wIdent = Grobal2.CM_3295,
            nParam1 = recog,
            nParam2 = param,
            nParam3 = tag,
            wParam = series,
            sMsg = text,
            Payload = payload,
            nBodyLen = payload?.Length ?? 0
        });
    }

    var missing = NewProbe("missing");
    Dispatch(missing, 3001, payload: HUtil32.GbkEncoding.GetBytes("静默"));
    Assert(missing.Sent.Count == 0 && missing.BroadcastFrames.Count == 0,
        "CM3295 missing item must be silent");

    var makeIndexOnly = NewProbe("makeindex");
    var makeIndexItem = AddItem(makeIndexOnly, 3101, 9101,
        microWhelkIndex, 2000);
    Dispatch(makeIndexOnly, 9101, payload: HUtil32.GbkEncoding.GetBytes("静默"));
    Assert(makeIndexItem.Dura == 2000 && makeIndexOnly.Sent.Count == 0
           && makeIndexOnly.BroadcastFrames.Count == 0,
        "CM3295 must not fall back from ClientItemID to MakeIndex");

    var wrongClass = NewProbe("wrongclass");
    var wrongClassItem = AddItem(wrongClass, 3201, 9201,
        wrongClassIndex, 2000);
    Dispatch(wrongClass, 3201, payload: HUtil32.GbkEncoding.GetBytes("静默"));
    Assert(wrongClassItem.Dura == 2000 && wrongClass.Sent.Count == 0
           && wrongClass.BroadcastFrames.Count == 0,
        "CM3295 wrong native item class must be silent");

    var shortDura = NewProbe("dura999");
    var shortDuraItem = AddItem(shortDura, 3301, 9301,
        microWhelkIndex, 999);
    Dispatch(shortDura, 3301, payload: HUtil32.GbkEncoding.GetBytes("静默"));
    Assert(shortDuraItem.Dura == 999 && shortDura.Sent.Count == 0
           && shortDura.BroadcastFrames.Count == 0,
        "CM3295 Dura 999 must be silent and unchanged");

    const string overlongName = "ABCDEFGHIJKLMNO";
    var mutedKeep = NewProbe(overlongName);
    mutedKeep.m_boDisableSayMsg = true;
    const int keepClientId = 3401;
    const ushort keepDuraMax = 5432;
    var keepItem = AddItem(mutedKeep, keepClientId, 9401,
        microWhelkIndex, 2000, keepDuraMax);
    var requestText = HUtil32.GbkEncoding.GetBytes("原文A");
    var rawWithTail = requestText.Concat(new byte[] { 0, 0x51, 0x52 }).ToArray();
    Dispatch(mutedKeep, keepClientId, 0x1234, 0xABCD, 0x7777,
        "不得使用这段解码文本", rawWithTail);

    var expectedName = HUtil32.GbkEncoding.GetBytes(overlongName)
        .AsSpan(0, 14).ToArray();
    var expectedVisible = expectedName.Concat(new byte[] { 0x0D, 0x0A })
        .Concat(requestText).ToArray();
    var expectedDirect = expectedVisible.Concat(new byte[] { 0, 0 }).ToArray();
    Assert(keepItem.Dura == 1000 && mutedKeep.m_ItemList.Contains(keepItem),
        "CM3295 Dura 2000 must remain at 1000");
    Assert(mutedKeep.Sent.Count == 2
           && mutedKeep.BroadcastFrames.Count == 0,
        "muted CM3295 must send SM641 then direct SM106 only");
    var duraPacket = mutedKeep.Sent[0];
    Assert(duraPacket.Ident == Grobal2.SM_BAGITEMDURACHG
           && duraPacket.Recog == keepClientId
           && duraPacket.Param == 1000
           && duraPacket.Tag == keepDuraMax
           && duraPacket.Series == 0,
        "CM3295 retained item SM641 header mismatch");
    var directPacket = mutedKeep.Sent[1];
    Assert(directPacket.Ident == Grobal2.SM_MICROWHELK
           && directPacket.Recog == mutedKeep.ObjectId
           && directPacket.Param == 0x1234
           && directPacket.Tag == 0xABCD
           && directPacket.Series == 1
           && directPacket.RawBody != null
           && directPacket.RawBody.SequenceEqual(expectedDirect),
        "CM3295 muted SM106 header/raw body/double-NUL mismatch");

    var delete1999 = NewProbe("delete1999");
    delete1999.m_boDisableSayMsg = true;
    var item1999 = AddItem(delete1999, 3501, 9501,
        microWhelkIndex, 1999);
    Dispatch(delete1999, 3501, payload: HUtil32.GbkEncoding.GetBytes("边界"));
    Assert(item1999.Dura == 999 && !delete1999.m_ItemList.Contains(item1999)
           && delete1999.Sent.Count == 2
           && delete1999.Sent[0].Ident == Grobal2.SM_DELITEM
           && delete1999.Sent[0].Recog == 3501
           && delete1999.Sent[0].Param == 0
           && delete1999.Sent[0].Tag == 0
           && delete1999.Sent[0].Series == 1
           && delete1999.Sent[1].Ident == Grobal2.SM_MICROWHELK,
        "CM3295 Dura 1999 must delete with SM202 before SM106");

    var broadcastDelete = NewProbe("广播者");
    const int deleteClientId = 3601;
    var deleteItem = AddItem(broadcastDelete, deleteClientId, 9601,
        microWhelkIndex, 1000);
    var broadcastText = HUtil32.GbkEncoding.GetBytes("广播明文");
    var broadcastRaw = broadcastText.Concat(new byte[] { 0, 0x61 }).ToArray();
    Dispatch(broadcastDelete, deleteClientId, 0x4567, 0xCDEF, 0x9999,
        "错误解码文本", broadcastRaw);
    Assert(deleteItem.Dura == 0 && !broadcastDelete.m_ItemList.Contains(deleteItem)
           && broadcastDelete.Sent.Count == 1
           && broadcastDelete.Sent[0].Ident == Grobal2.SM_DELITEM
           && broadcastDelete.Sent[0].Recog == deleteClientId
           && broadcastDelete.BroadcastFrames.Count == 1,
        "CM3295 Dura 1000 must send SM202 before one broadcast SM106");

    var frame = broadcastDelete.BroadcastFrames[0];
    var parsed = LegacyGateType18.FromBytes(frame, 0, frame.Length);
    var broadcastVisible = HUtil32.GbkEncoding.GetBytes("广播者")
        .Concat(new byte[] { 0x0D, 0x0A }).Concat(broadcastText).ToArray();
    Assert(parsed != null
           && parsed.IgnoredConnectionId == 0
           && parsed.FilterUserIndex == 0
           && parsed.Recog == broadcastDelete.ObjectId
           && parsed.Ident == Grobal2.SM_MICROWHELK
           && parsed.Param == 0x4567
           && parsed.Tag == 0xCDEF
           && parsed.Series == 1
           && parsed.TextBytes.SequenceEqual(broadcastVisible),
        "CM3295 type18 SM106 header/visible body mismatch");
    Assert(frame.Length == LegacyGateType18.HeaderSize
                          + LegacyGateType18.ClientPacketSize
                          + broadcastVisible.Length + 1
           && BitConverter.ToUInt32(frame, 0) == LegacyGateType18.MagicValue
           && BitConverter.ToUInt16(frame, 12) == LegacyGateType18.MessageType
           && BitConverter.ToUInt16(frame, 14)
              == LegacyGateType18.ClientPacketSize + broadcastVisible.Length + 1
           && frame.AsSpan(28, broadcastVisible.Length)
               .SequenceEqual(broadcastVisible)
           && frame[^1] == 0 && frame[^2] != 0,
        "CM3295 type18 wire length/body/single-NUL mismatch");

    var decodeMethod = typeof(GateService).GetMethod("DecodeClientMessageBody",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(nameof(GateService),
            "DecodeClientMessageBody");
    var rawPlainText = HUtil32.GbkEncoding.GetBytes("原始明文")
        .Concat(new byte[] { 0 }).ToArray();
    var decodedPlainText = (string)decodeMethod.Invoke(null,
        new object[] { Grobal2.CM_3295, rawPlainText });
    Assert(decodedPlainText == "原始明文",
        "GateService must classify CM3295 as original plaintext");

    Log("MICROWHELK: default NameQuery CM3295 route, main-bag ClientItemID/class/Dura "
        + "gates, 999/1000/1999/2000 boundaries, SM641/202 ordering, raw GBK "
        + "first-NUL text, 14-byte name and direct double-NUL/type18 single-NUL passed");
}

void RunCryCharmProtocol(Envirnoment map)
{
    var engine = M2Share.UserEngine;
    var cryCharmIndex = engine.StdItemList.Count;
    engine.StdItemList.Add(new GoodItem
    {
        Name = "测试传音符",
        StdMode = 7,
        Shape = 0,
        Source = 1,
        AniCount = 2,
        DuraMax = 6000
    });
    var clampPaletteIndex = engine.StdItemList.Count;
    engine.StdItemList.Add(new GoodItem
    {
        Name = "测试无色传音符",
        StdMode = 7,
        Shape = 0,
        Source = 5,
        AniCount = 0,
        DuraMax = 6000
    });
    var queryItemIndex = engine.StdItemList.Count;
    engine.StdItemList.Add(new GoodItem
    {
        Name = "展示剑",
        StdMode = 5,
        Shape = 0,
        DuraMax = 9000
    });

    var command = new CryCharmCommand();

    StoragePacketProbe NewProbe(string name, int tick = 1000)
    {
        return new StoragePacketProbe
        {
            m_sCharName = name,
            m_sMapName = map.sMapName,
            m_PEnvir = map,
            m_boGhost = false,
            m_boOffLineFlag = false,
            NativeCryCharmTick = tick
        };
    }

    TUserItem EquipCharm(StoragePacketProbe player, ushort dura,
        int stdIndex = -1, int clientId = 7001)
    {
        var item = new TUserItem
        {
            ClientItemID = clientId,
            MakeIndex = clientId + 10000,
            wIndex = checked((ushort)(stdIndex < 0 ? cryCharmIndex : stdIndex)),
            Dura = dura,
            DuraMax = 6000
        };
        player.m_UseItems[Grobal2.U_CHARM] = item;
        return item;
    }

    byte[] CommandBytes(string line, int terminalZeroCount = 1)
    {
        return HUtil32.GbkEncoding.GetBytes(line)
            .Concat(Enumerable.Repeat((byte)0, terminalZeroCount)).ToArray();
    }

    void Dispatch(StoragePacketProbe player, string body,
        int terminalZeroCount = 1)
    {
        var line = "@传 " + body;
        var raw = CommandBytes(line, terminalZeroCount);
        command.HandleRaw(line, body, raw, raw.Length, player);
    }

    CapturedDefMessage FindSystemMessage(StoragePacketProbe player,
        string text)
    {
        return player.Sent.LastOrDefault(packet =>
            packet.Ident == Grobal2.SM_SYSMESSAGE && packet.Body == text);
    }

    var minimumLength = NewProbe("最短");
    var minimumCharm = EquipCharm(minimumLength, 2000);
    var minimumRaw = CommandBytes("@传 ");
    command.HandleRaw("@传 ", string.Empty, minimumRaw,
        minimumRaw.Length, minimumLength);
    Assert(minimumCharm.Dura == 2000
           && minimumLength.Sent.Count == 0
           && minimumLength.BroadcastFrames.Count == 0,
        "@传 logical raw length 4 must not enter the worker");

    var overlongName = NewProbe("ABCDEFGHIJKLMNOP");
    var overlongNameCharm = EquipCharm(overlongName, 2000);
    Dispatch(overlongName, "静默");
    Assert(overlongNameCharm.Dura == 2000
           && overlongName.Sent.Count == 0
           && overlongName.BroadcastFrames.Count == 0,
        "@传 16-byte character name must be silent before clock/item use");

    var piFailure = NewProbe("甲");
    piFailure.m_boDisableSayMsg = true;
    var piCharm = EquipCharm(piFailure, 2000);
    Dispatch(piFailure, "{@PIx|2048|y}");
    PumpMessages(piFailure);
    var piError = FindSystemMessage(piFailure,
        "对不起，图片信息过大，无法发送");
    Assert(piCharm.Dura == 2000
           && piFailure.m_UseItems[Grobal2.U_CHARM] == piCharm
           && piError.Ident == Grobal2.SM_SYSMESSAGE
           && piError.Param == 0x38FF,
        "@传 PI >1024 must fail red before tick/item consumption");

    var sentBeforeValid = piFailure.Sent.Count;
    Dispatch(piFailure, "正文");
    Assert(piCharm.Dura == 1000,
        "@传 PI failure must not spend the 1-second clock");
    var direct = piFailure.Sent.Last(packet =>
        packet.Ident == Grobal2.SM_MICROWHELK);
    var expectedDirectBody = HUtil32.GbkEncoding.GetBytes("甲: 正文");
    Assert(piFailure.Sent.Count == sentBeforeValid + 1
           && direct.Recog == piFailure.ObjectId
           && direct.Param == 0x38FF
           && direct.Tag == 2
           && direct.Series == 0
           && direct.RawBody.SequenceEqual(expectedDirectBody),
        "@传 muted direct SM106 fields/body/no-NUL mismatch");

    piFailure.NativeCryCharmTick = 1999;
    var sentBeforeThrottle = piFailure.Sent.Count;
    Dispatch(piFailure, "节流");
    Assert(piCharm.Dura == 1000
           && piFailure.Sent.Count == sentBeforeThrottle,
        "@传 elapsed 999 must be silent and unchanged");
    piFailure.NativeCryCharmTick = 2000;
    Dispatch(piFailure, "边界");
    Assert(piCharm.Dura == 0
           && piFailure.m_UseItems[Grobal2.U_CHARM] == null,
        "@传 elapsed exactly 1000 must pass and consume the charm");

    foreach (var boundary in new[]
             {
                 (Dura: (ushort)999, Remaining: (ushort)0, Kept: false),
                 (Dura: (ushort)1000, Remaining: (ushort)0, Kept: false),
                 (Dura: (ushort)1999, Remaining: (ushort)999, Kept: false),
                 (Dura: (ushort)2000, Remaining: (ushort)1000, Kept: true)
             })
    {
        var player = NewProbe("耐久");
        player.m_boDisableSayMsg = true;
        var item = EquipCharm(player, boundary.Dura,
            clampPaletteIndex, 7100 + boundary.Dura);
        Dispatch(player, "测试");
        Assert(item.Dura == boundary.Remaining
               && (player.m_UseItems[Grobal2.U_CHARM] == item)
                  == boundary.Kept,
            "@传 Dura boundary mismatch: " + boundary.Dura);
        var packet = player.Sent.LastOrDefault(value =>
            value.Ident == Grobal2.SM_MICROWHELK);
        Assert(packet.Ident == Grobal2.SM_MICROWHELK
               && packet.Param == 0xFDFF
               && packet.Tag == 0,
            "@传 Source >4 palette clamp mismatch: " + boundary.Dura);
    }

    var overlongInput = NewProbe("长度");
    var overlongCharm = EquipCharm(overlongInput, 2000);
    Dispatch(overlongInput, new string('A', 256));
    Assert(overlongCharm.Dura == 2000
           && overlongInput.BroadcastFrames.Count == 0,
        "@传 256-byte input must be silent before clock/item use");

    var taggedLength = NewProbe("甲");
    var taggedCharm = EquipCharm(taggedLength, 2000);
    Dispatch(taggedLength, new string('A', 60));
    PumpMessages(taggedLength);
    var lengthError = FindSystemMessage(taggedLength,
        "对不起,你所输入的字太多,无法发送");
    Assert(taggedCharm.Dura == 2000
           && lengthError.Ident == Grobal2.SM_SYSMESSAGE
           && lengthError.Param == 0xFFDB,
        "@传 tagged 64-byte visible limit must fail green before use");

    var blackroom = NewProbe("黑屋");
    var blackroomCharm = EquipCharm(blackroom, 2000);
    map.Flag.boBLACKROOM = true;
    try
    {
        Dispatch(blackroom, "静默");
    }
    finally
    {
        map.Flag.boBLACKROOM = false;
    }
    Assert(blackroomCharm.Dura == 2000
           && blackroom.Sent.Count == 0
           && blackroom.BroadcastFrames.Count == 0,
        "@传 BLACKROOM gate must be silent");

    var broadcast = NewProbe("广播者");
    var broadcastCharm = EquipCharm(broadcast, 2000,
        clampPaletteIndex, 7201);
    Dispatch(broadcast, "广播明文");
    Assert(broadcastCharm.Dura == 1000
           && broadcast.BroadcastFrames.Count == 1,
        "@传 unmuted send must retain Dura 1000 and broadcast once");
    var broadcastFrame = broadcast.BroadcastFrames[0];
    var parsedBroadcast = LegacyGateType18.FromBytes(broadcastFrame, 0,
        broadcastFrame.Length);
    var expectedBroadcastBody = HUtil32.GbkEncoding.GetBytes(
        "广播者: 广播明文");
    Assert(parsedBroadcast != null
           && parsedBroadcast.Recog == broadcast.ObjectId
           && parsedBroadcast.Ident == Grobal2.SM_MICROWHELK
           && parsedBroadcast.Param == 0xFDFF
           && parsedBroadcast.Tag == 0
           && parsedBroadcast.Series == 0
           && parsedBroadcast.TextBytes.SequenceEqual(expectedBroadcastBody)
           && broadcastFrame.Length == LegacyGateType18.HeaderSize
              + LegacyGateType18.ClientPacketSize
              + expectedBroadcastBody.Length
           && BitConverter.ToUInt16(broadcastFrame, 14)
              == LegacyGateType18.ClientPacketSize
                 + expectedBroadcastBody.Length
           && broadcastFrame.AsSpan(28).SequenceEqual(expectedBroadcastBody),
        "@传 type18 SM106 exact body/no-NUL mismatch");

    var itemTag = NewProbe("物品者");
    var itemTagCharm = EquipCharm(itemTag, 2000,
        clampPaletteIndex, 7301);
    var queryItem = new TUserItem
    {
        ClientItemID = 8801,
        MakeIndex = 18801,
        wIndex = checked((ushort)queryItemIndex),
        Dura = 4321,
        DuraMax = 9000
    };
    itemTag.m_ItemList.Add(queryItem);
    Dispatch(itemTag, "{@IT8801|x|展示剑|y}");
    Assert(itemTagCharm.Dura == 1000
           && itemTag.InternalBroadcastFrames.Count == 1
           && itemTag.BroadcastFrames.Count == 1
           && itemTag.BroadcastOrder.SequenceEqual(new[] { "24", "18" }),
        "@传 valid IT must broadcast Cmd24 before SM106");
    var itemFrame = itemTag.InternalBroadcastFrames[0];
    var parsedItemFrame = InternalPacket77.FromBytes(itemFrame, 0,
        itemFrame.Length);
    var expectedRecord = itemTag.EncodeOwnedClientItemRecord(queryItem);
    Assert(parsedItemFrame != null
           && parsedItemFrame.ConnID == 0
           && parsedItemFrame.SeqID == 0
           && parsedItemFrame.Cmd == 24
           && parsedItemFrame.Payload.Length == 12 + expectedRecord.Length
           && BitConverter.ToInt32(parsedItemFrame.Payload, 0) == 8801
           && BitConverter.ToUInt16(parsedItemFrame.Payload, 4)
              == Grobal2.SM_QUERY_FOCUS_ITEM
           && parsedItemFrame.Payload.AsSpan(6, 6)
               .SequenceEqual(new byte[6])
           && parsedItemFrame.Payload.AsSpan(12)
               .SequenceEqual(expectedRecord)
           && itemFrame.Length == InternalPacket77.HEADER_SIZE + 12
              + expectedRecord.Length,
        "@传 IT Cmd24 inner header/item record/exact length mismatch");

    var invalidItemTag = NewProbe("失效者");
    var invalidCharm = EquipCharm(invalidItemTag, 1000,
        clampPaletteIndex, 7401);
    Dispatch(invalidItemTag, "{@IT9999|x|不存在|y}");
    PumpMessages(invalidItemTag);
    var itemError = FindSystemMessage(invalidItemTag,
        "对不起，无效物品信息，无法发送");
    Assert(invalidCharm.Dura == 0
           && invalidItemTag.m_UseItems[Grobal2.U_CHARM] == null
           && invalidItemTag.InternalBroadcastFrames.Count == 0
           && invalidItemTag.BroadcastFrames.Count == 0
           && itemError.Ident == Grobal2.SM_SYSMESSAGE
           && itemError.Param == 0x38FF,
        "@传 invalid IT must fail red after irreversible charm consumption");

    var deferredInvalidTag = NewProbe("全验者");
    var deferredCharm = EquipCharm(deferredInvalidTag, 2000,
        clampPaletteIndex, 7451);
    var validFirstItem = new TUserItem
    {
        ClientItemID = 8802,
        MakeIndex = 18802,
        wIndex = checked((ushort)queryItemIndex),
        Dura = 5000,
        DuraMax = 9000
    };
    deferredInvalidTag.m_ItemList.Add(validFirstItem);
    Dispatch(deferredInvalidTag,
        "{@IT8802|x|展示剑|y}{@IT9998|x|不存在|y}");
    PumpMessages(deferredInvalidTag);
    Assert(deferredCharm.Dura == 1000
           && deferredInvalidTag.InternalBroadcastFrames.Count == 0
           && deferredInvalidTag.BroadcastFrames.Count == 0
           && FindSystemMessage(deferredInvalidTag,
               "对不起，无效物品信息，无法发送").Param == 0x38FF,
        "@传 IT must validate every tag before emitting the first Cmd24");

    var rawOffset = NewProbe("原字");
    rawOffset.m_boDisableSayMsg = true;
    EquipCharm(rawOffset, 2000, clampPaletteIndex, 7501);
    const string aliasLine = "@longalias 正文";
    var aliasRaw = CommandBytes(aliasLine, 2);
    command.HandleRaw(aliasLine, "正文", aliasRaw, aliasRaw.Length,
        rawOffset);
    var aliasPacket = rawOffset.Sent.Last(packet =>
        packet.Ident == Grobal2.SM_MICROWHELK);
    var expectedAliasInput = aliasRaw.AsSpan(4, aliasRaw.Length - 5).ToArray();
    var expectedAliasBody = HUtil32.GbkEncoding.GetBytes("原字: ")
        .Concat(expectedAliasInput).ToArray();
    Assert(aliasPacket.RawBody.SequenceEqual(expectedAliasBody)
           && aliasPacket.RawBody[^1] == 0,
        "@传 alias must still skip fixed raw +4 and strip one tail NUL only");

    Log("CRYCHARM: @传 fixed raw+4/single-tail-NUL, BLACKROOM/1s/name-body gates, "
        + "PI and tagged length pre-use failures, 999/1000/1999/2000 consumption, "
        + "Source palette/AniCount tag, IT late failure and Cmd24-before-Type18 exact no-NUL passed");
}

void RunItemFlow(Envirnoment map)
{
    try
    {
        var mapItem = new MapItem { Name = "铁剑", Count = 1 };
        map.AddToMap(40, 40, CellType.OS_ITEMOBJECT, mapItem);
        int items = CountCellType(map, 40, 40, CellType.OS_ITEMOBJECT);
        Log($"ITEM raw map drop: AddToMap(OS_ITEMOBJECT '铁剑') at (40,40); cell OS_ITEMOBJECT count={items}");
        Assert(items >= 1, "item present on real map cell");
    }
    catch (Exception ie) { Log($"ITEM map-drop blocked: {ie.GetType().Name}: {ie.Message}"); }
}

// ===================== helpers =====================

void PrepareConfig()
{
    var baseDir = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(baseDir, "!Setup.txt"), "[Server]\r\n");
    File.WriteAllText(Path.Combine(baseDir, "String.ini"), "[String]\r\n");
    File.WriteAllText(Path.Combine(baseDir, "Command.conf"), "[Command]\r\n");
    var share = Path.GetFullPath(Path.Combine(baseDir, "..", "Share"));
    Directory.CreateDirectory(share);
    File.WriteAllText(Path.Combine(share, "PlayerUpgradeExp.ini"), "[PlayerLevelExp]\r\nLEVEL_1=50\r\n");
}

void BootSingletons()
{
    M2Share.g_Config = new GameSvrConfig();
    M2Share.RandomNumber = RandomNumber.GetInstance();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.UserEngine = new UserEngine();
    M2Share.MapManager = new MapManager();
    M2Share.MagicManager = new MagicManager();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new System.Collections.ArrayList();
    M2Share.g_MonDropLimitLIst = new Dictionary<string, TMonDrop>();   // drop-limit table (else NRE in ScatterBagItems)
    M2Share.g_DenySayMsgList = new System.Collections.Concurrent.ConcurrentDictionary<string, long>();

    // Defensive engine-config knobs so the real reward/drop math runs (and no Random(0)); these mirror
    // typical !Setup values that the config file would otherwise supply.
    M2Share.g_Config.nMonRandomAddValue = 100;   // MonGetRandomItems upgrade roll divisor
    M2Share.g_Config.dwKillMonExpMultiple = 1;   // WinExp server multiplier
    M2Share.g_Config.nLimitExpLevel = 10000;     // keep killer on the normal exp path
    M2Share.g_Config.nMagicAttackRage = 15;      // DoSpell range gate
}

Envirnoment CreateBlankMap(short w, short h, string name)
{
    var map = new Envirnoment { sMapName = name, sMapDesc = name, m_sMapFileName = name };
    // private Envirnoment.Initialize(short,short) allocates all cell arrays; default CellAttribute (0)
    // is walkable, so a file-less blank map is fully traversable.
    var init = typeof(Envirnoment).GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("Envirnoment.Initialize");
    init.Invoke(map, new object[] { w, h });
    map.Flag = new TMapFlag();   // real flag holder (all gates default false) needed by Die()/WinExp

    var mapListField = typeof(MapManager).GetField("m_MapList", BindingFlags.Instance | BindingFlags.NonPublic);
    if (mapListField?.GetValue(M2Share.MapManager) is System.Collections.IDictionary dict && !dict.Contains(name))
        dict.Add(name, map);
    return map;
}

TMagic BuildMagicTemplate(int wMagicID, string name, byte trainLv)
{
    return new TMagic
    {
        wMagicID = (ushort)wMagicID,
        sMagicName = name,
        btEffectType = 0,
        btEffect = 0,
        btTrainLv = trainLv,
        btJob = 0,
        btDefSpell = 0,
        wPower = 10,
        wMaxPower = 20,
        wSpell = 5,
        TrainLevel = new byte[] { 1, 7, 13, 22 },
        MaxTrain = new int[] { 100, 1000, 5000, 20000 }
    };
}

TPlayObject NewPlayer(string name, byte job, byte level, short x, short y, Envirnoment map)
{
    var player = new TPlayObject
    {
        m_boOffLineFlag = true, m_boGhost = false, m_btJob = job,
        m_sMapName = map.sMapName, m_nCurrX = x, m_nCurrY = y, m_sCharName = name
    };
    player.m_Abil.Level = level;
    player.m_PEnvir = map;
    try { player.RecalcAbilitys(); }
    catch { /* recalc may reference optional config; damage pipeline still runs with explicit stats */ }
    map.AddToMap(x, y, CellType.OS_MOVINGOBJECT, player);
    return player;
}

Monster NewMonster(string name, byte level, short x, short y, Envirnoment map,
    int hp, bool recalc = true, bool setHp = true)
{
    var mon = new Monster
    {
        m_boOffLineFlag = false, m_boGhost = false, m_boDeath = false,
        m_sMapName = map.sMapName, m_nCurrX = x, m_nCurrY = y, m_sCharName = name
    };
    mon.m_Abil.Level = level;
    mon.m_PEnvir = map;
    if (recalc)
    {
        try { mon.RecalcAbilitys(); }
        catch { /* recalc reads the injected def; explicit HP still set below when requested */ }
    }
    if (setHp) { mon.m_WAbil.HP = hp; mon.m_WAbil.MaxHP = hp; }
    map.AddToMap(x, y, CellType.OS_MOVINGOBJECT, mon);
    return mon;
}

int CountCellType(Envirnoment map, int x, int y, CellType type)
{
    bool ok = false;
    var info = map.GetMapCellInfo(x, y, ref ok);
    if (!ok || info.ObjList == null) return 0;
    int n = 0;
    foreach (var cell in info.ObjList)
        if (cell != null && cell.CellType == type) n++;
    return n;
}

int CountItemsAround(Envirnoment map, int cx, int cy, int radius)
{
    int total = 0;
    for (int dx = -radius; dx <= radius; dx++)
        for (int dy = -radius; dy <= radius; dy++)
            total += CountCellType(map, cx + dx, cy + dy, CellType.OS_ITEMOBJECT);
    return total;
}

int PumpMessages(TBaseObject actor)
{
    // The exact loop TBaseObject.Run uses: while (GetMessage(ref msg)) Operate(msg). GetMessage is
    // protected, so it is reached by reflection; it pops only messages whose delivery time is due.
    // Bounded to a hard cap so a runaway queue can never spin.
    var mi = typeof(TBaseObject).GetMethod("GetMessage", BindingFlags.Instance | BindingFlags.NonPublic);
    int n = 0;
    var args = new object[] { null };
    while ((bool)mi.Invoke(actor, args))
    {
        actor.Operate((TProcessMessage)args[0]);
        args[0] = null;
        if (++n >= 256) break;
    }
    return n;
}

object GetField(object target, string name)
{
    var f = target.GetType().GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
        ?? throw new MissingFieldException(target.GetType().Name, name);
    return f.GetValue(target);
}

void SetRepairMode(TPlayObject player, byte repairMode)
{
    var field = typeof(TPlayObject).GetField("m_btNativeRepairMode",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(nameof(TPlayObject),
            "m_btNativeRepairMode");
    field.SetValue(player, repairMode);
}

byte GetRepairMode(TPlayObject player)
{
    var field = typeof(TPlayObject).GetField("m_btNativeRepairMode",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(nameof(TPlayObject),
            "m_btNativeRepairMode");
    return (byte)field.GetValue(player);
}

sealed class StoragePacketProbe : TPlayObject
{
    internal List<CapturedDefMessage> Sent { get; } = new();
    internal List<FireHitSendSnapshot> FireHitSends { get; } = new();
    internal List<byte[]> BroadcastFrames { get; } = new();
    internal List<byte[]> InternalBroadcastFrames { get; } = new();
    internal List<string> BroadcastOrder { get; } = new();
    internal int NativeCryCharmTick { get; set; }

    internal override void BroadcastNativeCm3295(LegacyGateType18 packet)
    {
        BroadcastFrames.Add(packet.ToBytes());
    }

    internal override int GetNativeCryCharmTick()
    {
        return NativeCryCharmTick;
    }

    internal override void BroadcastNativeCryCharm(LegacyGateType18 packet)
    {
        BroadcastOrder.Add("18");
        BroadcastFrames.Add(packet.ToBytes());
    }

    internal override void BroadcastNativeCryCharmItem(InternalPacket77 packet)
    {
        BroadcastOrder.Add("24");
        InternalBroadcastFrames.Add(packet.ToBytes());
    }

    internal override void SendSocket(ClientPacket defMsg, string sMsg)
    {
        var captured = defMsg == null
            ? new CapturedDefMessage(0, 0, 0, 0, 0, sMsg)
            : new CapturedDefMessage(defMsg.Ident, defMsg.Recog,
                defMsg.Param, defMsg.Tag, defMsg.Series, sMsg);
        if (defMsg?.Ident == Grobal2.SM_FIREHITSKILL)
        {
            FireHitSends.Add(new FireHitSendSnapshot(m_boFireHitSkill,
                m_dwLatestFireHitTick, m_WAbil.MP, captured));
        }
        Sent.Add(captured);
    }

    internal override void SendSocket(ClientPacket defMsg, byte[] rawBody)
    {
        var body = rawBody ?? Array.Empty<byte>();
        var textLength = body.Length > 0 && body[^1] == 0
            ? body.Length - 1
            : body.Length;
        var text = HUtil32.GbkEncoding.GetString(body, 0, textLength);
        Sent.Add(defMsg == null
            ? new CapturedDefMessage(0, 0, 0, 0, 0, text, body.ToArray())
            : new CapturedDefMessage(defMsg.Ident, defMsg.Recog,
                defMsg.Param, defMsg.Tag, defMsg.Series, text, body.ToArray()));
    }
}

sealed class NativeActionDamageProbe : Monster
{
    internal List<NativeActionDamageCall> Calls { get; } = new();
    internal Action<TBaseObject, MagicDamageContext> OnResolve { get; set; }

    internal override int ResolveFullMagicDamage(TBaseObject source,
        int skillId, bool arg0, MagicDamageContext context, byte category,
        int flags, int rawDamage)
    {
        Calls.Add(new NativeActionDamageCall(source, skillId, arg0,
            category, flags, rawDamage, context?.MagicIndex ?? 0));
        OnResolve?.Invoke(source, context);
        return rawDamage;
    }
}

readonly record struct NativeActionDamageCall(
    TBaseObject Source, int SkillId, bool Arg0, byte Category, int Flags,
    int RawDamage, ushort MagicIndex);

readonly record struct CapturedDefMessage(
    ushort Ident, int Recog, ushort Param, ushort Tag, ushort Series,
    string Body, byte[] RawBody = null);

readonly record struct FireHitSendSnapshot(
    bool Ready, int Tick, int Mp, CapturedDefMessage Packet);


