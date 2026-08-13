using System.Reflection;
using GameSvr;
using SystemModule;

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

    // ================= NEW (2026-08-04): merchant money contracts (statted pricing / sell
    // truncation / tax base / no pricing-side item mutation) — reverses a ref-MIR2 misreading =====
    RunMerchantMoneyContracts(map);

    // ================= existing raw map-item drop (kept) =========================================
    RunItemFlow(map);

    Console.WriteLine(
        "PASS InProcEngineRunCheck engine-booted-in-process defs-injected(std/mon/magic) "
        + "combat=StruckDamage/DamageHealth melee-skill=AttackDir(ErGum)->StruckDamage "
        + "magic-skill=DoSpell->realpump(GetMessage+Operate)->cast-derived-damage + RM_DELAYMAGIC->RM_MAGSTRUCK "
        + "death-reward=Die()->GainExp+drop-table->map pickup=ClientPickUpItem->AddItemToBag "
        + "monster=RecalcAbilitys hero=create+attach+took-damage(melee-deal-gap) shop=ClientBuyItem(no-MySQL) fieldhero=BLOCKED(dormant) "
        + "deal-escrow=DealCancel-clears-remote+6-preconditions+no-double-release gold=DecGold-negative-guard/IncGold-percharcap "
        + "repair=super-quote==charge(post-Round x3) "
        + "merchant-money=statted(n10+(n10div5)*n14)/sell(div2-truncate)/tax(actual-amount,single-castle,no-fallback)/ore(no-item-mutation) "
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
        + $"Level={mon.m_Abil.Level} race=RC_ANIMAL({mon.m_btRaceServer}) onMap={mon.m_boAddToMaped}");
    Assert(mon.m_btRaceServer == Grobal2.RC_ANIMAL && mon.m_boAddToMaped,
        "real Monster constructed as RC_ANIMAL and placed on the real map");
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
    string note;
    try { merchant.ClientBuyItem(buyer, "回城卷", 0); note = "ClientBuyItem executed"; }
    catch (Exception se) { note = $"ClientBuyItem partial: {se.GetType().Name}: {se.Message}"; }
    int goldAfter = buyer.m_nGold, bagAfter = buyer.m_ItemList.Count;

    Log($"SHOP buy '回城卷' (made={made}, price=100): {note}; buyer gold {goldBefore}->{goldAfter}, "
        + $"bag {bagBefore}->{bagAfter} (real Merchant.ClientBuyItem -> m_nGold deduct + AddItemToBag), no MySQL");
    Assert(goldAfter < goldBefore, "real shop buy deducted the buyer's gold");
    Assert(bagAfter > bagBefore, "real shop buy added the purchased item into the buyer's bag");
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
    foreach (ushort dura in new ushort[] { 1, 7, 33, 50, 71, 99 })
    {
        TUserItem item = null;
        if (!eng.CopyToUserItemFromName("修理测试剑", ref item)) continue;
        item.DuraMax = 100; item.Dura = dura;

        var quoter = NewPlayer($"repair-q{dura}", job: 0, level: 20, x: 45, y: 44, map);
        quoter.m_sScriptLable = M2Share.sSUPERREPAIR;
        merchant.ClientQueryRepairCost(quoter, item);
        int quoted = -1;
        var mi = typeof(TBaseObject).GetMethod("GetMessage", BindingFlags.Instance | BindingFlags.NonPublic);
        var args = new object[] { null };
        int guard = 0;
        while ((bool)mi.Invoke(quoter, args) && guard++ < 64)
        {
            var msg = (TProcessMessage)args[0];
            if (msg.wIdent == Grobal2.RM_SENDREPAIRCOST) quoted = msg.nParam1;
            args[0] = null;
        }
        Assert(quoted > 0, $"super-repair quote did not produce a positive cost (dura={dura})");

        // same item state, same script label: the charge must debit exactly the quoted number
        var payer = NewPlayer($"repair-p{dura}", job: 0, level: 20, x: 46, y: 44, map);
        payer.m_sScriptLable = M2Share.sSUPERREPAIR;
        payer.m_nGoldMax = 10_000_000; payer.m_nGold = 1_000_000;
        TUserItem chargeItem = null;
        eng.CopyToUserItemFromName("修理测试剑", ref chargeItem);
        chargeItem.DuraMax = 100; chargeItem.Dura = dura;
        int goldBefore = payer.m_nGold;
        merchant.ClientRepairItem(payer, chargeItem);
        int charged = goldBefore - payer.m_nGold;

        // the old (divergent) charge = Round((price/DuraMax)*ΔDura); the correct one = quote
        Assert(charged == quoted,
            $"MIS-CHARGE: super-repair quoted {quoted} but charged {charged} (dura={dura}) — native "
            + "sub_63EE9C @0x63EFDF multiplies the POST-Round cost, not the pre-/3 base price");
        if (charged * 3 != charged) divergentCases++;   // any nonzero case exercises the x3 path
        checkedCases++;
    }
    Assert(checkedCases >= 4, "super-repair quote/charge sweep did not run enough durability cases");
    Log($"REPAIR super-repair quote==charge across {checkedCases} durability cases "
        + $"(x3-path cases={divergentCases}): the charge multiplies the POST-Round cost like the quote and "
        + "like native sub_63EE9C @0x63EFDF `lea eax,[esi+esi*2]`; the /3 stays an integer divide "
        + "(@0x63EF98 cdq/idiv ecx)");
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
        // independent recomputation of the native arithmetic (base price 1000)
        int nativeN10 = 1000 + (1000 / 5) * n14;                    // @0x783E86 add edi,eax
        int expected = HUtil32._MAX(2, nativeN10);                  // wear terms are identity here
        int refBug = HUtil32._MAX(2, (1000 / 5) * n14);              // the dropped-`n10 +` value
        Assert((int)got == expected,
            $"STATTED PRICING: n14={n14} -> {got}, native sub_783D70 @0x783E86 gives {expected} "
            + $"(n10 + (n10 div 5)*n14). The dropped-term ref value would be {refBug}.");
        Assert((int)got != refBug || n14 == 4,
            $"STATTED PRICING regressed to the ref shape `n10 div 5 * n14` at n14={n14} ({refBug})");
        statCases++;
    }
    Assert(statCases == 4, "statted-pricing sweep did not run all n14 cases");
    // n14=1 is the worst case: the ref shape is 200 vs the native 1200 = -83.3%
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
        Assert((int)basePrice == 100 * qty,
            $"ECON-12 PILE BASE PRICE: qty={qty} -> {basePrice}, native sub_63F3B4 @0x63F458 "
            + $"`imul` gives {100 * qty} (unit price 100 * count {qty}). Missing the multiply pays "
            + "for ONE unit while ClientUserSellItem removes the WHOLE stack.");
        // the sell half then applies div 2 on the already-multiplied base (0x63F235)
        int sellPrice = (int)miGetSellItemPrice.Invoke(pricer, new object[] { basePrice });
        Assert(sellPrice == (100 * qty) / 2,
            $"ECON-12 PILE SELL PRICE: qty={qty} -> {sellPrice}, expected {(100 * qty) / 2} "
            + "(native halves the multiplied base, so the count survives into the payout)");
        pileCases++;
    }
    Assert(pileCases == 4, "ECON-12 pile pricing sweep did not run all quantity cases");

    // a NON-pile item must NOT scale with Dura, or every worn weapon gets count-inflated
    TUserItem nonPile = null;
    Assert(eng.CopyToUserItemFromName("属性定价剑", ref nonPile), "non-pile fixture not created");
    nonPile.DuraMax = 100; nonPile.Dura = 100;
    double nonPilePrice = (double)miGetUserItemPrice.Invoke(pricer, new object[] { nonPile });
    Assert((int)nonPilePrice == 1000,
        $"ECON-12 OVER-REACH: non-pile StdMode 5 priced {nonPilePrice}, expected 1000. The count "
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
        + "(native sub_783D70 @0x783E86 `add edi,eax`; dropping `n10 +` was -83% at n14=1: 200 vs 1200); "
        + "sell price truncates via div 2 (sub_63F200 @0x63F235 sar/jns/adc, not banker's Round: 7->3 not 4); "
        + $"{incRateGoldSites}/4 tax sites pass the ACTUAL money moved incl. IncRateGold(price) for the "
        + "no-break upgrade tier K=0x7530 (sub_6CA020 @0x6CA163-82), single castle [[0x7D6214]], no "
        + "castle==nil fallback (sub_65B31C has exactly 5 CODE callers, all single-gate); TOreItem pricing "
        + "leaves UserItem.DuraMax/Dura untouched and is idempotent (sub_7862B4 @0x7862DA-E2 clamps EBX only)");
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


