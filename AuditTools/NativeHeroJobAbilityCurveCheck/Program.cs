// NativeHeroJobAbilityCurveCheck — 英雄三职业子类 VMT+0x2B8 成长曲线的运行时契约闸(2026-08-04)。
//
// 权威二进制: D:/loym2/staging/M2Server_reunpacked_20260803.exe (ImageBase 0x400000)
// 完整逐字节证据: staging/herojobs_fix_20260804.md
// 被测代码: GameSvr/Services/NativeHeroJobAbilityCurve.cs
//
// 所有断言都是对真实静态方法的【运行时】调用(不是源码 grep),咬的是数值行为。
//
// ===========================================================================
// 锁住的原版契约
// ===========================================================================
//
// (1) 职业字节映射,来自工厂 switch @0x6521FD(逐字节):
//       6521FD  mov dl, byte [eax+0xB4]   ; 记录种类
//       652203  cmp dl,1 / jne 0x65224A
//       652208  mov al, byte [eax+0x22]   ; 职业字节 = record[+0x22]
//       65220B  sub al,1 / jb  0x652217   ; job==0 -> TWarHero  (classref 0x68591C)
//       65220F           je  0x652228     ; job==1 -> TMagHero  (classref 0x685F8C)
//       652211  dec al / je  0x652239     ; job==2 -> TTaosHero (classref 0x685C54)
//       652215  jmp 0x65228F              ; job>=3 -> 不建对象
//     classref 全部解析确认: [0x68591C]=0x685968(TWarHero VMT)、
//     [0x685F8C]=0x685FD8(TMagHero)、[0x685C54]=0x685CA0(TTaosHero)。
//     ⚠ 没有任何 1/2 对调。并行的 kind==2 族 @0x65224F 用同一职业字节、同一顺序,
//     且按 RTTI 类名独立佐证: job0=TSecWarHero、job1=TSecMagHero、job2=TSecTaosHero。
//     这与 M2Share.jWarr=0 / jWizard=1 / jTaos=2 完全一致。
//
// (2) MaxMP 里的 2.2 是 80 位 x87 扩展常量(`fld xword`,DB 2D):
//       TMagHero  @0x694C8F  fld xword [0x694DD8]
//       TTaosHero @0x693715  fld xword [0x693874]
//     两处原始字节都是 `cd cc cc cc cc cc cc 8c 00 40` = 2.2。
//     按 float32 读该池会解成 -107374184.0 从而漏掉它 —— 那样 MaxMP 会差 2.2 倍。
//
// (3) DC 下限【按职业不同】: 战士 Max(lo/5-1, 1)(`mov edx,1` @0x69276B),
//     法师/道士 Max(lo/7-1, 0)(`xor edx,edx` @0x694D4B / @0x6937D1)。
//
// (4) 60 级以上修正项符号与倍率各不相同:
//       战士 @0x6926C7 lea eax,[eax+eax*2] = *3 , @0x6926CA 【SUB】
//       法师 @0x694C74 imul eax,eax,0x1E  = *30, @0x694C77 【ADD】
//       道士 @0x6936FE shl eax,5 / add eax,edx = *33(非 *32), @0x693703 【ADD】
//
// (5) lo>200 走整数线性分支,系数逐字节:
//       战士 MaxHP=e*226+23630 (imul 0xE2 / add 0x5C4E) ; MaxMP=e*4+711 (shl 2 / add 0x2C7)
//       法师 MaxHP=e*62 +7917  (imul 0x3E / add 0x1EED) ; MaxMP=e*180+18493 (imul 0xB4 / add 0x483D)
//       道士 MaxHP=e*110+13337 ; MaxMP=e*110+11013 —— 【同一个 *110 乘积复用】
//            (6936AD imul eax,eax,0x6E 只算一次, 6936B2 add 0x3419 / 6936BB add 0x2B05)
//
// (6) (hi, lo) 对由 THeroAct VMT+0x2C = sub_690300 决定。常态 hi==lo==英雄等级
//     (@0x690357/@0x69035B);仅当地图 [map+0xC0]=LIMITHEROLEVEL 生效、主人等级 >
//     [map+0xBE]=LIMITPLAYERLEVEL、英雄等级 > 该上限时,lo 变成地图上限而 hi 保持真实等级
//     (@0x690342/@0x690349)。
//     ⚠ [+0x128] 是【地图对象】不是武器记录:两个字段的写入者就是地图 token 解析器
//     (LIMITPLAYERLEVEL 串 @0x775EF4 写 @0x77580A;LIMITHEROLEVEL 串 @0x775F10 写 @0x775869)。
//
// (7) 三种负重统一 Min 钳到 0xFFDC=65500(sub_4C700C @各 curve 的 mov edx,0xFFDC)。
//
// (8) 舍入 = sub_403574 = 默认控制字下的 `fistp qword` = 银行家舍入(half-to-even)。

using GameSvr.Services;
using SystemModule;

int checks = 0;

try
{
    VerifyJobMappingHasNoSwap();
    VerifyExtended2Point2IsPresent();
    VerifyDcFloorsDifferPerJob();
    VerifyAbove60CorrectionSigns();
    VerifyHighLevelLinearBranch();
    VerifyLevelPairAndMapCap();
    VerifyWeightCapClamp();
    VerifyBankersRounding();
    VerifyPerJobStatShape();

    Console.WriteLine(
        "PASS NativeHeroJobAbilityCurveCheck checks=" + checks +
        " job0=War/job1=Mag/job2=Taos@0x6521FD(NO-1-2-swap)" +
        " mp2.2=xword80bit@0x694DD8+0x693874" +
        " dcfloor=war1@0x69276B/mag0@0x694D4B/taos0@0x6937D1" +
        " above60=war-3*@0x6926CA/mag+30*@0x694C77/taos+33*@0x693703" +
        " gt200=war226+23630/mag62+7917/taos110-shared" +
        " pair=sub_690300(map-LIMITHEROLEVEL-cap,not-weapon)" +
        " weightcap=0xFFDC round=half-to-even@0x403574");
    return 0;
}
catch (Exception ex)
{
    Console.WriteLine("NativeHeroJobAbilityCurveCheck FAIL: " + ex);
    return 1;
}

// ===========================================================================
// (1) 职业字节映射 —— 没有 1/2 对调
// ===========================================================================
void VerifyJobMappingHasNoSwap()
{
    // 三个职业在同一 (hi,lo) 下必须给出【三种互不相同】的曲线,否则派发是错的。
    const int hi = 50, lo = 50;
    var war = NativeHeroJobAbilityCurve.Calculate(0, hi, lo);
    var mag = NativeHeroJobAbilityCurve.Calculate(1, hi, lo);
    var taos = NativeHeroJobAbilityCurve.Calculate(2, hi, lo);

    // job0 必须 == 战士曲线(而不是别的职业)
    Equal(NativeHeroJobAbilityCurve.CalculateWarrior(hi, lo).MaxHp, war.MaxHp,
        "job byte 0 must dispatch to TWarHero (classref 0x68591C @0x652219)");
    // job1 必须 == 法师曲线。若有人按"1/2 对调"的错误说法改了派发,这条会红。
    Equal(NativeHeroJobAbilityCurve.CalculateWizard(hi, lo).MaxHp, mag.MaxHp,
        "job byte 1 must dispatch to TMagHero (classref 0x685F8C @0x65222A); " +
        "the alleged 1/2 swap does NOT exist — kind==2 family confirms by RTTI name " +
        "job1=TSecMagHero");
    Equal(NativeHeroJobAbilityCurve.CalculateTaoist(hi, lo).MaxHp, taos.MaxHp,
        "job byte 2 must dispatch to TTaosHero (classref 0x685C54 @0x65223B); " +
        "kind==2 family confirms job2=TSecTaosHero");

    // 咬住"三者真的不同" —— 否则上面三条可能因为曲线雷同而假绿。
    Assert(war.MaxHp != mag.MaxHp && mag.MaxHp != taos.MaxHp && war.MaxHp != taos.MaxHp,
        "the three job curves must produce distinct MaxHP at level 50 " +
        "(sub_692618 / sub_694BF0 / sub_69367C are different functions)");
    // MP 同样必须三者不同(战士线性 *3.5,法师 (l/5+2)*2.2*l,道士 (l/8)*2.2*l)
    Assert(war.MaxMp != mag.MaxMp && mag.MaxMp != taos.MaxMp && war.MaxMp != taos.MaxMp,
        "the three job MaxMP formulas must be distinct");

    // job>=3 原版 @0x652215 直接不建对象 —— 必须抛,不能静默回落成某个职业。
    var threw = false;
    try { NativeHeroJobAbilityCurve.Calculate(3, hi, lo); }
    catch (ArgumentOutOfRangeException) { threw = true; }
    Assert(threw,
        "job >= 3 must throw: native @0x652215 jumps to 0x65228F with [ebp-0x18]==0 " +
        "so NO hero object is created — silently falling back to a job would invent behaviour");
}

// ===========================================================================
// (2) 80 位扩展 2.2 必须真的在公式里
// ===========================================================================
void VerifyExtended2Point2IsPresent()
{
    // 法师 lo=100: (100/5 + 2) * 2.2 * 100 + 13 = 22*2.2*100 + 13 = 4840 + 13 = 4853
    Equal(4853, NativeHeroJobAbilityCurve.CalculateWizard(100, 100).MaxMp,
        "TMagHero MaxMP @lo=100 must be 4853 = Round((100/5+2)*2.2*100)+13 " +
        "(fld xword [0x694DD8]=2.2 @0x694C8F). Dropping the 2.2 would give 2213");

    // 道士 lo=100: (100/8) * 2.2 * 100 + 13 = 12.5*2.2*100 + 13 = 2750 + 13 = 2763
    Equal(2763, NativeHeroJobAbilityCurve.CalculateTaoist(100, 100).MaxMp,
        "TTaosHero MaxMP @lo=100 must be 2763 = Round((100/8)*2.2*100)+13 " +
        "(fld xword [0x693874]=2.2 @0x693715). Dropping the 2.2 would give 1263");

    // 反向咬: 若有人把 2.2 写成 2.0,上面两条会变 4413 / 2513 —— 都不等于断言值。
    // 再压一条低等级用例,保证不是靠某个巧合数字过关。
    // lo=50: (50/5+2)*2.2*50 = 12*2.2*50 = 1320 -> +13 = 1333
    Equal(1333, NativeHeroJobAbilityCurve.CalculateWizard(50, 50).MaxMp,
        "TMagHero MaxMP @lo=50 = Round((50/5+2)*2.2*50)+13 = 1320+13 = 1333 " +
        "(with 2.0 instead of 2.2 this would be 1213)");
}

// ===========================================================================
// (3) DC 下限按职业不同(1 vs 0)
// ===========================================================================
void VerifyDcFloorsDifferPerJob()
{
    // 战士 lo=5 -> d5 = 1 -> DcLow = Max(1-1, 1) = 1  (下限 1 生效)
    Equal(1, NativeHeroJobAbilityCurve.CalculateWarrior(5, 5).DcLow,
        "TWarHero DcLow floor is 1 (`mov edx,1` @0x69276B before call sub_4C7004)");

    // 法师 lo=7 -> d7 = 1 -> DcLow = Max(1-1, 0) = 0  (下限 0)
    Equal(0, NativeHeroJobAbilityCurve.CalculateWizard(7, 7).DcLow,
        "TMagHero DcLow floor is 0 (`xor edx,edx` @0x694D4B) — NOT 1 like the warrior");

    // 道士 lo=7 -> DcLow = 0
    Equal(0, NativeHeroJobAbilityCurve.CalculateTaoist(7, 7).DcLow,
        "TTaosHero DcLow floor is 0 (`xor edx,edx` @0x6937D1)");

    // DcHigh 三者下限都是 1
    Equal(1, NativeHeroJobAbilityCurve.CalculateWarrior(1, 1).DcHigh,
        "TWarHero DcHigh = Max(lo/5, 1) @0x692784");
    Equal(1, NativeHeroJobAbilityCurve.CalculateWizard(1, 1).DcHigh,
        "TMagHero DcHigh = Max(lo/7, 1) @0x694D5C");
    Equal(1, NativeHeroJobAbilityCurve.CalculateTaoist(1, 1).DcHigh,
        "TTaosHero DcHigh = Max(lo/7, 1) @0x6937E2");

    // 除数也不同: 战士 /5, 法/道 /7。lo=35 -> 战士 d5=7, 法/道 d7=5。
    Equal(7, NativeHeroJobAbilityCurve.CalculateWarrior(35, 35).DcHigh,
        "TWarHero uses lo/5 (div ecx=5 @0x692764)");
    Equal(5, NativeHeroJobAbilityCurve.CalculateWizard(35, 35).DcHigh,
        "TMagHero uses lo/7 (div ecx=7 @0x694D44)");
    Equal(5, NativeHeroJobAbilityCurve.CalculateTaoist(35, 35).DcHigh,
        "TTaosHero uses lo/7 (div ecx=7 @0x6937CA)");
}

// ===========================================================================
// (4) 60 级以上修正:符号与倍率
// ===========================================================================
void VerifyAbove60CorrectionSigns()
{
    // 战士: SUB 3*(lo-60)。lo=61 与 lo=60 相比,基式增量减去 3。
    var w60 = NativeHeroJobAbilityCurve.CalculateWarrior(60, 60).MaxHp;
    var w61 = NativeHeroJobAbilityCurve.CalculateWarrior(61, 61).MaxHp;
    var w61NoCorr = HUtil32.Round((61 / 2.0 + 10.0 + 61 / 20.0) * 61) + 50;
    Equal(w61NoCorr - 3, w61,
        "TWarHero above-60 correction SUBTRACTS 3*(lo-60) " +
        "(lea eax,[eax+eax*2]=*3 @0x6926C7 then `sub` @0x6926CA)");
    Assert(w61 > w60,
        "the -3 correction must not overpower base growth at lo=61 (sanity)");

    // 法师: ADD 30*(lo-60)
    var m61NoCorr = HUtil32.Round((61 / 15.0 + 5.0) * 61) + 50;
    Equal(m61NoCorr + 30, NativeHeroJobAbilityCurve.CalculateWizard(61, 61).MaxHp,
        "TMagHero above-60 correction ADDS 30*(lo-60) " +
        "(imul eax,eax,0x1E @0x694C74 then `add` @0x694C77)");

    // 道士: ADD 33*(lo-60) —— 是 33 不是 32(shl 5 之后又把原值加回)
    var t61NoCorr = HUtil32.Round((61 / 6.0 + 10.0) * 61) + 50;
    Equal(t61NoCorr + 33, NativeHeroJobAbilityCurve.CalculateTaoist(61, 61).MaxHp,
        "TTaosHero above-60 correction ADDS 33*(lo-60): " +
        "`mov edx,eax / shl eax,5 / add eax,edx` @0x6936FC-0x693701 = *33, NOT *32");

    // 恰好 60 时不得触发(原版 `cmp di,0x3C / jbe` = 只有 >60 才修正)
    Equal(HUtil32.Round((60 / 6.0 + 10.0) * 60) + 50,
        NativeHeroJobAbilityCurve.CalculateTaoist(60, 60).MaxHp,
        "at exactly lo=60 no correction applies (`jbe` @0x6936F4 skips it)");
}

// ===========================================================================
// (5) lo>200 的整数线性分支
// ===========================================================================
void VerifyHighLevelLinearBranch()
{
    // lo=201 -> e=1
    var w = NativeHeroJobAbilityCurve.CalculateWarrior(201, 201);
    Equal(1 * 226 + 23630, w.MaxHp,
        "TWarHero lo>200: MaxHP = (lo-200)*226 + 23630 (imul 0xE2 @0x692649 / add 0x5C4E)");
    Equal(1 * 4 + 711, w.MaxMp,
        "TWarHero lo>200: MaxMP = (lo-200)*4 + 711 (shl eax,2 @0x692658 / add 0x2C7)");

    var m = NativeHeroJobAbilityCurve.CalculateWizard(201, 201);
    Equal(1 * 62 + 7917, m.MaxHp,
        "TMagHero lo>200: MaxHP = (lo-200)*62 + 7917 (imul 0x3E @0x694C21 / add 0x1EED)");
    Equal(1 * 180 + 18493, m.MaxMp,
        "TMagHero lo>200: MaxMP = (lo-200)*180 + 18493 (imul 0xB4 @0x694C2D / add 0x483D)");

    var t = NativeHeroJobAbilityCurve.CalculateTaoist(201, 201);
    Equal(1 * 110 + 13337, t.MaxHp,
        "TTaosHero lo>200: MaxHP = (lo-200)*110 + 13337 (imul 0x6E @0x6936AD / add 0x3419)");
    Equal(1 * 110 + 11013, t.MaxMp,
        "TTaosHero lo>200: MaxMP reuses the SAME *110 product, + 11013 (add 0x2B05 @0x6936BB) " +
        "— the native code computes imul 0x6E once and adds two different constants");

    // 道士两式之差必须恒为常量 (13337-11013)=2324,与 e 无关 —— 咬住"复用同一乘积"。
    var t300 = NativeHeroJobAbilityCurve.CalculateTaoist(300, 300);
    Equal(2324, t.MaxHp - t.MaxMp,
        "taoist MaxHP-MaxMP == 2324 at lo=201 (shared *110 product)");
    Equal(2324, t300.MaxHp - t300.MaxMp,
        "taoist MaxHP-MaxMP stays 2324 at lo=300 — proves one shared multiply, " +
        "not two independent slopes");

    // 恰好 200 必须走【浮点】分支(`cmp si,0xC8 / jbe`),201 起才走线性分支。
    // 关键佐证: 六个公式(三职业 × HP/MP)在 lo=200 处【全部精确连续】——
    // 浮点分支在 lo=200 的值恰好等于线性分支在 e=0 的截距。
    // 这不可能是巧合(原作者是按曲线在 200 处的值与斜率拟合出线性延长段的),
    // 所以它同时验证了两个分支的全部 12 个常量,以及 60 级以上修正项的符号。
    // 任何一个常量抄错,下面 6 条里至少一条会红。
    Equal(NativeHeroJobAbilityCurve.CalculateWarrior(200, 200).MaxHp,
        0 * 226 + 23630,
        "warrior MaxHP is CONTINUOUS at lo=200: float branch value == linear intercept 23630");
    Equal(NativeHeroJobAbilityCurve.CalculateWarrior(200, 200).MaxMp,
        0 * 4 + 711,
        "warrior MaxMP is continuous at lo=200 (== 711)");
    Equal(NativeHeroJobAbilityCurve.CalculateWizard(200, 200).MaxHp,
        0 * 62 + 7917,
        "wizard MaxHP is continuous at lo=200 (== 7917); this also validates the " +
        "+30*(lo-60) correction, without which the float side would not land on 7917");
    Equal(NativeHeroJobAbilityCurve.CalculateWizard(200, 200).MaxMp,
        0 * 180 + 18493,
        "wizard MaxMP is continuous at lo=200 (== 18493); this independently confirms " +
        "the 80-bit 2.2 — with 2.0 the float side gives 16813, breaking continuity");
    Equal(NativeHeroJobAbilityCurve.CalculateTaoist(200, 200).MaxHp,
        0 * 110 + 13337,
        "taoist MaxHP is continuous at lo=200 (== 13337); this validates the *33 " +
        "(not *32) above-60 correction — *32 would give 13197");
    Equal(NativeHeroJobAbilityCurve.CalculateTaoist(200, 200).MaxMp,
        0 * 110 + 11013,
        "taoist MaxMP is continuous at lo=200 (== 11013); also confirms the 2.2");
}

// ===========================================================================
// (6) (hi, lo) 对 = sub_690300,以及地图等级封顶(不是武器)
// ===========================================================================
void VerifyLevelPairAndMapCap()
{
    // 常态: 无地图上限 -> hi == lo == 英雄等级 (0x690357/0x69035B)
    var (hi, lo) = NativeHeroJobAbilityCurve.ResolveLevelPair(80, 90);
    Equal(80, hi, "no map cap: hi = hero level (@0x690357 mov dx,[esi+0x14])");
    Equal(80, lo, "no map cap: lo = hi (@0x69035B mov ecx,edx)");

    // 地图设了 LIMITHEROLEVEL=50、LIMITPLAYERLEVEL=40,主人 90>40,英雄 80>50 -> lo 被封到 50
    var capped = NativeHeroJobAbilityCurve.ResolveLevelPair(80, 90, 50, 40);
    Equal(80, capped.Hi,
        "map cap active: hi stays the hero's REAL level (@0x690349 mov dx,[esi+0x14])");
    Equal(50, capped.Lo,
        "map cap active: lo becomes [map+0xC0]=LIMITHEROLEVEL " +
        "(@0x690342 mov cx,[edx+0xC0]) — this is a MAP level cap, not a weapon clamp");

    // 主人等级不够高(<=LIMITPLAYERLEVEL) -> 不封顶 (@0x69032C jbe)
    Equal(80, NativeHeroJobAbilityCurve.ResolveLevelPair(80, 40, 50, 40).Lo,
        "master level must be STRICTLY above LIMITPLAYERLEVEL (@0x69032C `jbe` skips)");

    // 英雄等级不高于上限 -> 不封顶 (@0x690339 jbe)
    Equal(50, NativeHeroJobAbilityCurve.ResolveLevelPair(50, 90, 50, 40).Lo,
        "hero level must be STRICTLY above LIMITHEROLEVEL (@0x690339 `jbe` skips)");

    // LIMITHEROLEVEL==0 -> 整个封顶不生效 (@0x690315 jbe)
    Equal(80, NativeHeroJobAbilityCurve.ResolveLevelPair(80, 90, 0, 40).Lo,
        "LIMITHEROLEVEL==0 disables the cap entirely (@0x690315 `jbe 0x690357`)");

    // 封顶只咬 HP/MP 轴,负重轴走 hi —— 这正是 hi/lo 分离的意义。
    var uncapped = NativeHeroJobAbilityCurve.CalculateWarrior(80, 80);
    var withCap = NativeHeroJobAbilityCurve.CalculateWarrior(80, 50);
    Assert(withCap.MaxHp < uncapped.MaxHp,
        "the capped lo axis must lower MaxHP (warrior takes HP/MP from lo)");
    Equal(uncapped.MaxWeight, withCap.MaxWeight,
        "the weight band takes hi, so it is UNAFFECTED by the lo cap " +
        "(warrior @0x6926D6 reads di=hi while HP/MP read si=lo)");
}

// ===========================================================================
// (7) 负重 Min 钳位 0xFFDC
// ===========================================================================
void VerifyWeightCapClamp()
{
    Equal(0xFFDC, NativeHeroJobAbilityCurve.WeightCap, "weight cap constant is 0xFFDC");

    // hi 足够大时三种负重都必须被钳住(战士 /3.0 增长最快)
    var big = NativeHeroJobAbilityCurve.CalculateWarrior(60000, 10);
    Equal(0xFFDC, big.MaxWeight,
        "MaxWeight clamps to 0xFFDC (mov edx,0xFFDC @0x6926EF -> Min sub_4C700C)");
    Equal(0xFFDC, big.MaxWearWeight,
        "MaxWearWeight clamps to 0xFFDC (@0x69271E)");
    Equal(0xFFDC, big.MaxHandWeight,
        "MaxHandWeight clamps to 0xFFDC (@0x69274D)");

    // 小等级时不得被钳(否则钳位方向写反了也会假绿)
    Assert(NativeHeroJobAbilityCurve.CalculateWarrior(10, 10).MaxWeight < 0xFFDC,
        "at low level the weights must be BELOW the cap (Min not Max)");
}

// ===========================================================================
// (8) 银行家舍入
// ===========================================================================
void VerifyBankersRounding()
{
    // sub_403574 是 fistp(默认控制字)= half-to-even。HUtil32.Round 已一致。
    Equal(2, HUtil32.Round(2.5),
        "sub_403574 = fistp under default control word = banker's rounding: " +
        "Round(2.5)=2, not 3");
    Equal(4, HUtil32.Round(3.5),
        "Round(3.5)=4 (half-to-even rounds up here)");
    Equal(2, HUtil32.Round(2.4), "Round(2.4)=2 (no +0.5 bias)");

    // 战士 MaxMP = Round(lo*3.5)+11。lo=1 -> Round(3.5)=4 -> 15。
    // 若舍入变成 half-away-from-zero,lo=1 仍是 4;换 lo=3 -> Round(10.5): even=10, away=11。
    Equal(10 + 11, NativeHeroJobAbilityCurve.CalculateWarrior(3, 3).MaxMp,
        "TWarHero MaxMP @lo=3 = Round(10.5)+11 = 10+11 = 21 under half-to-even; " +
        "half-away-from-zero would give 22 (this case separates the two)");
}

// ===========================================================================
// (9) 每职业属性形状(哪些槽写、哪些留零)
// ===========================================================================
void VerifyPerJobStatShape()
{
    const int L = 70;
    var w = NativeHeroJobAbilityCurve.CalculateWarrior(L, L);
    var m = NativeHeroJobAbilityCurve.CalculateWizard(L, L);
    var t = NativeHeroJobAbilityCurve.CalculateTaoist(L, L);

    // 战士: 只有 DC 与 AcHigh 非零;MC/SC/MAC/AcLow 全零。
    Assert(w.DcHigh > 0, "warrior writes DC");
    Equal(L / 7, w.AcHigh, "warrior AcHigh = lo/7 (div ecx=7 @0x6927B4, stored @0x6927B6)");
    Equal(0, w.AcLow, "warrior AcLow = 0 (@0x6927A7)");
    Equal(0, w.McLow + w.McHigh, "warrior MC = 0 (@0x692789/@0x69278E)");
    Equal(0, w.ScLow + w.ScHigh, "warrior SC = 0 (@0x692793/@0x692798)");
    Equal(0, w.MacLow + w.MacHigh, "warrior MAC = 0 (@0x6927BB/@0x6927C0)");

    // 法师: DC 与 MC 同式非零;SC/AC/MAC 全零。
    Assert(m.DcHigh > 0 && m.McHigh > 0, "wizard writes both DC and MC");
    Equal(m.DcLow, m.McLow, "wizard MC uses the same formula as DC (@0x694D69 vs @0x694D4D)");
    Equal(m.DcHigh, m.McHigh, "wizard McHigh == DcHigh (@0x694D78 vs @0x694D5C)");
    Equal(0, m.ScLow + m.ScHigh, "wizard SC = 0 (@0x694D82/@0x694D87)");
    Equal(0, m.AcLow + m.AcHigh, "wizard AC = 0 (@0x694D96/@0x694D9B)");
    Equal(0, m.MacLow + m.MacHigh, "wizard MAC = 0 (@0x694DA0/@0x694DA5)");

    // 道士: DC 与 SC 同式;MC/AC 零;MAC = (lo/6)>>1 与 (lo/6)+1。
    Assert(t.DcHigh > 0 && t.ScHigh > 0, "taoist writes both DC and SC");
    Equal(t.DcLow, t.ScLow, "taoist SC uses the same formula as DC (@0x6937F9 vs @0x6937D3)");
    Equal(t.DcHigh, t.ScHigh, "taoist ScHigh == DcHigh (@0x693808 vs @0x6937E2)");
    Equal(0, t.McLow + t.McHigh, "taoist MC = 0 (@0x6937EC/@0x6937F1)");
    Equal(0, t.AcLow + t.AcHigh, "taoist AC = 0 (@0x693812/@0x69381C)");
    Equal((L / 6) >> 1, t.MacLow,
        "taoist MacLow = (lo/6)>>1 (sar eax,1 + adc @0x693834, stored @0x69383B)");
    Equal(L / 6 + 1, t.MacHigh,
        "taoist MacHigh = (lo/6)+1 (inc esi @0x69383E, stored @0x69383F)");

    // 咬住"三个职业的属性形状真的不同"(否则可能全部退化成同一份)
    Assert(w.AcHigh > 0 && m.AcHigh == 0 && t.AcHigh == 0,
        "only the warrior writes AcHigh — this asymmetry is the shape fingerprint");
    Assert(t.MacHigh > 0 && w.MacHigh == 0 && m.MacHigh == 0,
        "only the taoist writes MAC");
}

// ===========================================================================
void Assert(bool cond, string what)
{
    checks++;
    if (!cond) throw new Exception("ASSERT FAILED: " + what);
}

void Equal(int expected, int actual, string what)
{
    checks++;
    if (expected != actual)
        throw new Exception($"ASSERT FAILED: {what} (expected {expected}, got {actual})");
}
