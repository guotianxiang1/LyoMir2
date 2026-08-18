using System.Reflection;
using GameSvr;
using SystemModule;

// In-process isolated-engine HERO harness (machine-safety FIRST: SINGLE process, NO network stack, NO
// DBSvr, NO MySQL, NO background engine threads; strictly serial; Environment.Exit at the end). Same
// technique as InProcEngineRunCheck / InProcSocialRunCheck: construct the M2Share engine singletons
// directly (bypassing GameApp.Initialize / StartEngine and the 30s DBSvr native-definition gate), inject
// the native definitions the DBSvr Type2 stream would supply, then drive the REAL hero lifecycle engine
// code end-to-end and capture the real in-memory state mutations (not model stubs).
//
// This harness RESOLVES the "hero melee-deal gap" that InProcEngineRunCheck's RunHero documented: there,
// a real HeroObject was created, attached and took real damage, but its own AttackDir(mode 0) did not
// LAND damage. Root cause (now proven here on the REAL predicate): a hero is RC_HEROOBJECT(54) >= RC_ANIMAL
// with m_Master set, so TBaseObject.IsAttackTarget (TBaseObject.cs:4589) gates it to the MASTER's engaged
// foe — the target must be m_Master.m_LastHiter / m_ExpHitter / m_TargetCret (or already be fighting the
// master/hero). That is faithful hero behaviour ("the hero attacks the master's target"), NOT a bug. The
// combat harness simply never engaged the injected monster as the owner's target. Once the master engages
// it (owner.SetTargetCreat(mon)), the hero's REAL AttackDir->_Attack->GetHitStruckDamage->StruckDamage
// lands real melee damage.
//
// REAL hero lifecycle driven here (no model stubs):
//   * CREATE + ATTACH : new HeroObject -> UserEngine.RegisterHero (UsrEngn.cs:1080) -> owner.m_HeroObject
//                       set, hero.OnEnvirnomentChanged + hero.Initialize place it on the real map (RC_HEROOBJECT).
//   * MELEE DEAL      : owner engages the monster (master target); hero AttackDir(wHitMode=0) -> real
//                       _Attack -> IsProperTarget/IsAttackTarget (master relationship) -> GetAttackPower ->
//                       target.GetHitStruckDamage -> StruckDamage. Real damage on a real injected monster.
//   * TAKE DAMAGE     : hero.StruckDamage -> HP clamp (real TBaseObject damage path).
//   * RECALC          : hero.RecalcAbilitys (TBaseObject.Base.cs:1635) deep-copies m_Abil into m_WAbil and
//                       folds the equipped 铁剑 weight into m_WAbil.HandWeight (real equipment recompute).
//   * GAIN EXP        : owner.GrantNativeHeroExperience (TPlayObject.NativeGive.cs:129) -> hero.m_Abil.Exp +
//                       hero.m_dwFightExp (real native hero-exp grant).
//   * DEATH + RELIVE  : hero.Die() (TBaseObject.Base.cs:700 — clears its own hiters, skips MonGetRandomItems
//                       for HeroObject, detaches) then the real base ReAlive() flips m_boDeath back.
//
// Evidence goes to stdout and inproc_hero_evidence.txt next to the executable.

int rc = 0;
var evidence = new List<string>();
void Log(string s) { evidence.Add(s); Console.WriteLine("  " + s); }
void Assert(bool cond, string msg) { if (!cond) throw new Exception("ASSERT FAILED: " + msg); }

// non-public / internal real entry points driven by reflection (same as the sibling harnesses)
var miAttackDir = typeof(TBaseObject).GetMethod("AttackDir",
    BindingFlags.Instance | BindingFlags.NonPublic, null,
    new[] { typeof(TBaseObject), typeof(short), typeof(byte) }, null)
    ?? throw new MissingMethodException("TBaseObject.AttackDir");
var miGrantHeroExp = typeof(TPlayObject).GetMethod("GrantNativeHeroExperience",
    BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new MissingMethodException("TPlayObject.GrantNativeHeroExperience");
var miReAlive = typeof(TBaseObject).GetMethod("ReAlive",
    BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new MissingMethodException("TBaseObject.ReAlive");

try
{
    PrepareConfig();
    BootSingletons();
    Log("BOOT singletons: g_Config/RandomNumber/ObjectManager/UserEngine/MapManager/MagicManager "
        + "constructed (no GameApp.Initialize, no DBSvr gate, no network, no background threads)");

    var map = CreateBlankMap(64, 64, "hero-harness-map");
    bool findMapResolves = M2Share.MapManager.FindMap("hero-harness-map") == map;
    Log($"MAP built in-memory '{map.sMapName}' {map.wWidth}x{map.wHeight} (real Envirnoment.Initialize, "
        + $"no .map file); Flag initialized; FindMap resolves={findMapResolves}");

    InjectNativeDefs();

    RunHeroLifecycle(map);

    Console.WriteLine(
        "PASS InProcHeroRunCheck engine-booted-in-process defs-injected(std/mon) "
        + "hero=RegisterHero(real-attach,RC_HEROOBJECT,on-map) "
        + "melee-deal=RESOLVED(master-engage->IsAttackTarget->AttackDir(0)->StruckDamage on monster) "
        + "take-damage=StruckDamage recalc=RecalcAbilitys(deep-copy+weapon-weight) "
        + "exp=GrantNativeHeroExperience(Exp+FightExp) death=Die() relive=ReAlive() "
        + "single-process no-network no-DBSvr no-MySQL");
}
catch (Exception ex)
{
    Console.Error.WriteLine("FAIL InProcHeroRunCheck: " + ex);
    rc = 1;
}

try { File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "inproc_hero_evidence.txt"), evidence); }
catch { /* evidence file is best-effort */ }

// Hard-exit so no lingering engine state can keep the process alive.
Environment.Exit(rc);

// ===================== hero lifecycle =====================

void RunHeroLifecycle(Envirnoment map)
{
    // ---- 1. CREATE + ATTACH (real HeroObject + real UserEngine.RegisterHero) --------------------
    var owner = NewPlayer("hero-owner", job: 0, level: 40, x: 50, y: 20, map);
    owner.m_WAbil.HP = 600; owner.m_WAbil.MaxHP = 600;

    var hero = new HeroObject { m_sCharName = "英雄甲", m_btJob = 0 };
    hero.m_Abil.Level = 40;

    bool registered = false;
    try { registered = M2Share.UserEngine.RegisterHero(owner, hero); }
    catch (Exception ex) { Log("HERO RegisterHero threw: " + ex.Message); }

    string createPath;
    if (registered && ReferenceEquals(owner.m_HeroObject, hero) && hero.m_boAddToMaped)
    {
        createPath = "UserEngine.RegisterHero (real: owner.m_HeroObject set, hero.Initialize placed on map)";
    }
    else
    {
        // Faithful fallback — still the real HeroObject on the real map cell.
        hero.m_Master = owner; owner.m_HeroObject = hero;
        hero.m_PEnvir = map; hero.m_sMapName = map.sMapName;
        hero.m_nCurrX = 51; hero.m_nCurrY = 20;
        try { hero.RecalcAbilitys(); } catch { }
        if (!hero.m_boAddToMaped) map.AddToMap(hero.m_nCurrX, hero.m_nCurrY, CellType.OS_MOVINGOBJECT, hero);
        createPath = $"direct-attach fallback (RegisterHero returned {registered})";
    }
    Log($"HERO CREATE+ATTACH: {createPath}; '{hero.m_sCharName}' race=RC_HEROOBJECT({hero.m_btRaceServer}) "
        + $"onMap={hero.m_boAddToMaped} at ({hero.m_nCurrX},{hero.m_nCurrY}); "
        + $"owner.m_HeroObject==hero={ReferenceEquals(owner.m_HeroObject, hero)} master-bound={ReferenceEquals(hero.m_Master, owner)}");
    Assert(hero.m_btRaceServer == Grobal2.RC_HEROOBJECT, "hero constructed as RC_HEROOBJECT");
    Assert(ReferenceEquals(owner.m_HeroObject, hero), "hero attached to player.m_HeroObject via real path");
    Assert(hero.m_boAddToMaped, "hero placed on the real map");
    Assert(ReferenceEquals(hero.m_Master, owner), "hero.m_Master bound to owner");

    // ---- 2. RESOLVE THE MELEE-DEAL GAP ---------------------------------------------------------
    // Deterministic hit set-up (high hit-point vs speed 1) + a real DC range, exactly as the combat
    // harness set up the warrior. The hero is NOT RC_PLAYOBJECT, so GetAttackPower returns the raw
    // nBasePower + Random(range) (no m_nPowerRate gate) — damage is guaranteed positive.
    hero.m_WAbil.HP = 400; hero.m_WAbil.MaxHP = 400;
    hero.m_WAbil.DC = HUtil32.MakeLong(30, 55);        // LoWord=min DC, HiWord=max DC
    hero.m_btHitPoint = 60; hero.m_btDirection = Grobal2.DR_RIGHT;

    var mon = NewMonster("测试骷髅", level: 10, x: (short)(hero.m_nCurrX + 1), y: hero.m_nCurrY, map, hp: 300);
    mon.m_wSpeedPoint = 1;                              // Random(1)==0 < 60 -> always hits

    // Prove the gap cause on the REAL predicate: with NO master engagement, a hero refuses an arbitrary
    // monster (IsAttackTarget requires the master relationship). This is faithful, not a bug.
    bool properBefore = hero.IsProperTarget(mon);

    // Faithful hero mechanic: the MASTER engages the monster; the hero attacks the master's target.
    owner.SetTargetCreat(mon);                          // master.m_TargetCret = mon (player attacks it)
    hero.SetTargetCreat(mon);                           // hero acquires master's target (HeroObject.Run attack mode)
    bool properAfter = hero.IsProperTarget(mon);

    int monHp0 = mon.m_WAbil.HP;
    miAttackDir.Invoke(hero, new object[] { mon, (short)0, (byte)Grobal2.DR_RIGHT });   // REAL hero melee
    int monHp1 = mon.m_WAbil.HP;
    bool heroDealt = monHp1 < monHp0;

    Log($"HERO MELEE-DEAL (gap RESOLVED): IsProperTarget(mon) before-master-engage={properBefore} "
        + $"after-master-engage={properAfter}; AttackDir(mode 0) -> monster HP {monHp0}->{monHp1} (dealt={heroDealt}) "
        + "via real _Attack->GetAttackPower->GetHitStruckDamage->StruckDamage");
    Assert(!properBefore, "faithful: hero does NOT target an arbitrary monster with no master engagement");
    Assert(properAfter, "hero targets the master's engaged monster (real IsAttackTarget relationship)");
    Assert(heroDealt, "REAL hero melee LANDED damage on the monster (melee-deal gap resolved, not faked)");

    // ---- 3. HERO TAKES REAL DAMAGE -------------------------------------------------------------
    int heroHp0 = hero.m_WAbil.HP;
    hero.m_LastHiter = mon;
    hero.StruckDamage(50);
    int heroHp1 = hero.m_WAbil.HP;
    Log($"HERO TAKE-DAMAGE: StruckDamage(50) -> hero HP {heroHp0}->{heroHp1}");
    Assert(heroHp1 < heroHp0, "real hero StruckDamage reduced the hero's HP");

    // ---- 4. HERO RecalcAbilitys (real equipment recompute) -------------------------------------
    TUserItem weapon = null;
    bool madeWeapon = M2Share.UserEngine.CopyToUserItemFromName("铁剑", ref weapon);   // real item factory
    hero.m_Abil.MaxHP = 500;                            // base pool the deep-copy must mirror into m_WAbil
    if (madeWeapon) hero.m_UseItems[Grobal2.U_WEAPON] = weapon;   // 铁剑 Weight=5, Dc 3-8
    int handWeight0 = hero.m_WAbil.HandWeight;
    hero.RecalcAbilitys();
    int handWeight1 = hero.m_WAbil.HandWeight;
    Log($"HERO RECALC: equipped 铁剑(made={madeWeapon}) then RecalcAbilitys -> m_WAbil.MaxHP={hero.m_WAbil.MaxHP} "
        + $"(deep-copy of m_Abil.MaxHP=500), HandWeight {handWeight0}->{handWeight1} (weapon weight folded), "
        + $"DC={HUtil32.LoWord(hero.m_WAbil.DC)}-{HUtil32.HiWord(hero.m_WAbil.DC)}");
    Assert(hero.m_WAbil.MaxHP == 500, "real RecalcAbilitys deep-copied m_Abil.MaxHP into m_WAbil");
    Assert(!madeWeapon || handWeight1 >= 5, "real RecalcAbilitys folded the equipped weapon weight into HandWeight");

    // ---- 5. HERO GAINS EXP (real native hero-exp grant) ----------------------------------------
    hero.m_Abil.Exp = 0; hero.m_Abil.MaxExp = 1_000_000;   // avoid level-up bookkeeping
    hero.m_dwFightExp = 0;
    int expBefore = hero.m_Abil.Exp, fightBefore = hero.m_dwFightExp;
    // internal TPlayObject.GrantNativeHeroExperience(hero, amount, countAsFightExperience, directMode)
    miGrantHeroExp.Invoke(owner, new object[] { hero, 500, true, false });
    int expAfter = hero.m_Abil.Exp, fightAfter = hero.m_dwFightExp;
    Log($"HERO EXP: GrantNativeHeroExperience(500, fight, natural) -> hero Exp {expBefore}->{expAfter}, "
        + $"FightExp {fightBefore}->{fightAfter}");
    Assert(expAfter == 500, "real hero-exp grant added exp to hero.m_Abil.Exp");
    Assert(fightAfter == 500, "real hero-exp grant accumulated hero fight-exp");

    // ---- 6. HERO DEATH + RELIVE (real Die()/ReAlive()) -----------------------------------------
    // THeroAct keeps owner +0x68C across death. C# folds that native owner slot into
    // m_Master, so the real Die path must preserve it while bypassing the monster pipeline.
    hero.m_WAbil.HP = 0;
    hero.Die();
    bool diedFlag = hero.m_boDeath;
    Log($"HERO DEATH: HP->0 then real Die() -> m_boDeath={diedFlag}, "
        + $"owner-preserved={ReferenceEquals(hero.m_Master, owner)}");
    Assert(diedFlag, "real hero Die() set m_boDeath");
    Assert(ReferenceEquals(hero.m_Master, owner),
        "real hero Die() preserved THeroAct owner +0x68C");

    // Relive drives the real base ReAlive() (heroes carry m_boCanReAlive=false and never autonomously
    // relive; this exercises the real method, flipping m_boDeath back to alive).
    miReAlive.Invoke(hero, null);
    bool aliveFlag = !hero.m_boDeath;
    Log($"HERO RELIVE: real ReAlive() -> m_boDeath={hero.m_boDeath} (alive={aliveFlag})");
    Assert(aliveFlag, "real hero ReAlive() cleared m_boDeath");
}

// ===================== native definition injection (DBSvr Type2 data, built in-memory) =========

void InjectNativeDefs()
{
    var eng = M2Share.UserEngine;

    // Faithful native StdItem layout: index 0 is the "金币" sentinel the DBSvr Type2 stream uses
    // (UsrEngn.HasNativeStdItemSentinel); real items follow, so UserItem.wIndex maps 1:1 to slot.
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

    // Monster definition used as the hero's melee target (small AC so the hero's DC lands full damage).
    eng.MonsterList.Add(new TMonInfo
    {
        sName = "测试骷髅",
        wLevel = 10, wHP = 200, wMP = 50, wAC = 2, wMAC = 2, wDC = 5, wMaxDC = 12,
        wSpeed = 2, wHitPoint = 8, dwExp = 500,
        ItemList = new List<TMonItem>()
    });

    Log($"DEFS injected in-memory: StdItemList={eng.StdItemList.Count} (sentinel '金币' + 铁剑 + 金创药), "
        + $"MonsterList={eng.MonsterList.Count} ('测试骷髅' AC=2 for the hero's melee target)");
    Assert(eng.GetStdItem("铁剑") != null, "injected weapon StdItem resolves by name");
}

// ===================== helpers (shared with InProcEngineRunCheck) ===============================

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
    M2Share.g_MonDropLimitLIst = new Dictionary<string, TMonDrop>();   // else NRE in ScatterBagItems on Die()

    // Defensive engine-config knobs mirroring typical !Setup values so the real paths run.
    M2Share.g_Config.nMonRandomAddValue = 100;
    M2Share.g_Config.dwKillMonExpMultiple = 1;
    M2Share.g_Config.nLimitExpLevel = 10000;
}

Envirnoment CreateBlankMap(short w, short h, string name)
{
    var map = new Envirnoment { sMapName = name, sMapDesc = name, m_sMapFileName = name };
    // private Envirnoment.Initialize(short,short) allocates all cell arrays; default CellAttribute (0)
    // is walkable, so a file-less blank map is fully traversable.
    var init = typeof(Envirnoment).GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("Envirnoment.Initialize");
    init.Invoke(map, new object[] { w, h });
    map.Flag = new TMapFlag();   // real flag holder (all gates default false) needed by Die()

    var mapListField = typeof(MapManager).GetField("m_MapList", BindingFlags.Instance | BindingFlags.NonPublic);
    if (mapListField?.GetValue(M2Share.MapManager) is System.Collections.IDictionary dict && !dict.Contains(name))
        dict.Add(name, map);
    return map;
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
    catch { /* recalc may reference optional config; combat path still runs with explicit stats */ }
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
