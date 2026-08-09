// NativeHeroBehaviourCheck — 英雄行为族(2026-08-04)的运行时契约闸。
//
// 权威二进制: D:/loym2/staging/M2Server_reunpacked_20260803.exe (ImageBase 0x400000)
// 完整证据 + 逐字节反汇编: staging/herobehaviour_fix_20260804.md
//
// 所有断言都是对 GameSvr 真实对象/真实方法的【运行时】断言(不是源码文本 grep),
// 所以它们咬的是行为而不是措辞。
//
// ===========================================================================
// 本闸锁住的原版契约
// ===========================================================================
//
// (1) 距离度量 = 切比雪夫,不是曼哈顿。sub_76B4A4 @0x76B4A4..0x76B4CE:
//       76B4AA  mov eax,[ebx+0x12C]; sub eax,esi; cdq; xor eax,edx; sub eax,edx  ; abs dx
//       76B4B9  mov eax,[ebx+0x130]; sub eax,ecx; cdq; xor eax,edx; sub eax,edx  ; abs dy
//       76B4C6  cmp eax,esi; jge +2; mov eax,esi                                 ; 取大者
//     英雄 Run 的每处距离判定都调它: 0x68A16E(>=15 丢目标) / 0x68A31E(<12 才重取)
//     / 0x68A4A0(>2 才走) / 0x68A660(>=15 丢主目标) / 0x68A6BC(>12 丢副目标)
//     / 0x68BB35(跟随, >=12 算太远) / 0x6930DD(战士近战 <=2) / 0x692466(<=1)。
//
// (2) 视野 = 11,搜索间隔 = 1000ms。THeroAct ctor sub_6864C4:
//       68659F  mov dword [edi+0x78], 0xB      ; 视野 11(C# 原为 10,差一)
//       68650E  mov dword [edi+0x7C], 0x3E8    ; 搜索间隔 1000
//     [+0x78] 即视野半径: sub_765DEC @0x765E3E `mov eax,[ebx+0x78]; push eax` 作范围入参。
//     [+0x7C] 即搜索间隔: 英雄 Run @0x68A08B `cmp edx,[eax+0x7C]` 对 tick-[hero+0x80]。
//
// (3) 攻击间隔 = [hero+0x320](m_nNextHitTime),播种 1000ms。ctor:
//       686515  mov dword [edi+0x320], 0x3E8   ; 攻击间隔 1000
//       6864FD  mov [edi+0x324], eax (=0x258)  ; 步行间隔 600
//     ⚠ 发现文档把这两个偏移【标反了】。判据: 三个职业子类都拿 [+0x320] 和命中戳
//     [+0x35C] 相比 —— TWarHero sub_693090 @0x6930B0/@0x6930B6、
//     TTaosHero sub_69422C @0x6942B8/@0x6942BE、TMagHero sub_694FE8 @0x695037/@0x69503D;
//     而 [+0x324] 是在通用怪物【走路】门里对 [+0x384] 比 (@0x6669EB/@0x6669F1)。
//     原 C# 公式 `_MAX(300, 800 - m_Abil.Level*5)` 是发明: 原版此处从不读等级。
//
// (4) 模式字节 [hero+0x6A1],三值循环,默认 1=跟随。
//       6865A6  mov byte [edi+0x6A1], 1        ; ctor 默认"跟随"
//       6886AE-6886BE  mov al,[ebx+0x6A1]; inc eax; mov ecx,3; div ecx; mov [ebx+0x6A1],dl
//       688673-68867E  dl!=0 且模式!=0 -> 模式:=0(强制攻击)
//       68A1F5  cmp byte [eax+0x6A1],0 / jne 0x68A4CF   ; 只有模式 0 跑战斗分支
//     名表 0x7D32FC (GBK,NUL 结尾): [0]=0x6862CC 攻击 [1]=0x6862DC 跟随 [2]=0x6862EC 休息。
//
// (5) 英雄回收门 sub_689FDC @0x68A018-0x68A071 —— 与发现文档相反的两点:
//       68A01B  call sub_772DA8        ; al = self.m_boDEATH ([obj+0x74]),不是 ghost
//       68A022  test al,al / je 68A039
//       68A02C  sub eax,[edx+0x330]    ; tick - self.m_dwDeathTick
//       68A032  cmp eax,0xEA60 / jae   ; 60000 —— 是"自己的尸体"超时,不是"主人死后宽限"
//       68A042  cmp byte [eax+0x73],0  ; master.m_boGHOST,且【无任何延时】
//       68A06C  call sub_768060        ; TCreature.MarkDelete = 变 ghost,不是 Die
//     m_boDeath/[+0x74] 与 m_boGhost/[+0x73] 的归属: VMT +0x84 = Die
//     (THumanKind override sub_741368 带串 "[Exception]: THumanKind.Die -1: " @0x741548),
//     它调 TCreature.Die sub_76631C,而后者首条指令就是 @0x766323 `mov byte [ebx+0x74],1`
//     紧跟 @0x76632C `mov [ebx+0x330],eax`。sub_768060 自带的两条串则是
//     "TCreature.MarkDelete ..." @0x768138/@0x768174,它写 [+0x73] 与 [+0x14C]。
//     全镜像扫描: [+0x73] 只有 1 处写(@0x7680EF)且【从不清零】,故不可能是死亡标志。
//
// (6) 通用"怪物奴隶"主人死亡/叛变块对英雄不成立。THeroAct/TWarHero/TTaosHero/TMagHero
//     四张 VMT 的 +0x154(sub_690B08) 与 +0x158(sub_690B1C) 字面都是
//       690B0C  xor ebx,ebx
//       690B0E  mov [eax+0x38C],ebx   ; 通用 m_Master := NULL
//     所以以 [+0x38C]!=0 为门的块(原版对应物在 TAnimal.Run sub_71E50C @0x71E594)
//     对英雄永不成立。
//
// (7) CM_HERO_CHGSTATE(1107) 原版是静默 no-op。派发 sub_6D7D68:
//       6D81A5  add eax,0xFFFFFBED    ; ident-0x413
//       6D81AA  cmp eax,0x48 / ja default
//       6D81B3  jmp dword [eax*4 + 0x6D81BA]
//     表项 1107(idx 0x40) -> 0x6DBC2C = 共享 default 落地标签
//     (`xor eax,eax; pop×3; mov fs:[eax],edx; jmp 0x6DBD0E`,全表 36 项指向它)。
//     邻居都是真 handler: 1100->0x6D9743 1105->0x6D97B0 1106->0x6D97D9
//     1108->0x6D98B1 1109->0x6D993C 1110->0x6D9963。

using System.Reflection;
using GameSvr;
using SystemModule;

int checks = 0;

try
{
    PrepareRuntimeConfig();
    InitializeRuntime();

    VerifyCtorSeeds();
    VerifyChebyshevNotManhattan();
    VerifyAttackIntervalIsFieldNotLevelFormula();
    VerifyModeCycleAndDefault();
    VerifyChgStateIsNoOp();
    VerifyMasterGoneReapContract();
    VerifyGenericSlaveBlocksExcludeHero();

    Console.WriteLine(
        "PASS NativeHeroBehaviourCheck checks=" + checks +
        " view=11@0x68659F search=1000@0x68650E hit=1000@0x686515([+0x320]not[+0x324])" +
        " dist=chebyshev(sub_76B4A4) mode=[+0x6A1]cycle3-default-follow@0x6865A6" +
        " chgstate1107=no-op(0x6DBC2C-sink) reap=self-corpse-60s@0x68A032+master-ghost-no-delay" +
        " slave-blocks=hero-excluded([+0x38C]pinned-null@0x690B0E)");
    return 0;
}
catch (Exception ex)
{
    Console.WriteLine("NativeHeroBehaviourCheck FAIL: " + ex);
    return 1;
}

// ===========================================================================
// (2)(3) ctor seeds
// ===========================================================================
void VerifyCtorSeeds()
{
    var hero = new HeroObject();

    // 68659F  mov dword [edi+0x78], 0xB
    Equal(11, hero.m_nViewRange,
        "hero view range must be 11 (ctor sub_6864C4 @0x68659F mov dword [edi+0x78],0xB)");

    // 68650E  mov dword [edi+0x7C], 0x3E8
    Equal(1000, hero.m_dwSearchTime,
        "hero search interval must be 1000 (ctor @0x68650E mov dword [edi+0x7C],0x3E8)");

    // 686515  mov dword [edi+0x320], 0x3E8  —— 攻击间隔,不是 0x324 的 600
    Equal(1000, hero.m_nNextHitTime,
        "hero attack interval must be seeded 1000 from [+0x320] " +
        "(ctor @0x686515); 600 would mean [+0x324] (walk) was used by mistake");

    // 6864C4 ctor @0x6864E3 `mov byte [edi+0x178],0x36` = 54
    Equal(Grobal2.RC_HEROOBJECT, hero.m_btRaceServer,
        "hero race must be RC_HEROOBJECT=54 (ctor @0x6864E3 mov byte [edi+0x178],0x36)");
}

// ===========================================================================
// (1) distance metric
// ===========================================================================
void VerifyChebyshevNotManhattan()
{
    // 私有静态 helper: 反射调用,断言的是真实被 Run 使用的那一个函数。
    var m = typeof(HeroObject).GetMethod("NativeGridDistance",
        BindingFlags.NonPublic | BindingFlags.Static);
    Assert(m != null,
        "HeroObject.NativeGridDistance must exist (the single Chebyshev helper " +
        "standing in for sub_76B4A4)");

    int Dist(int x1, int y1, int x2, int y2) =>
        (int)m.Invoke(null, new object[] { x1, y1, x2, y2 });

    // 纯对角: 切比雪夫 = 3, 曼哈顿 = 6。这一条就把两种度量分开。
    Equal(3, Dist(0, 0, 3, 3),
        "diagonal (0,0)->(3,3): Chebyshev=3 (sub_76B4A4 @0x76B4C6 keep-max); " +
        "Manhattan would give 6");

    // 非对称: dx=5, dy=2 -> max=5, sum=7
    Equal(5, Dist(10, 10, 15, 12),
        "dx=5 dy=2: Chebyshev=5, Manhattan would give 7");

    // 轴向: 两种度量恰好一致(所以只用轴向用例是测不出区别的 —— 记录在此免得
    // 后来者把断言弱化成只有轴向)。
    Equal(4, Dist(0, 0, 4, 0), "axis-only dx=4 dy=0: both metrics give 4");

    // 对称性 + 绝对值(原版 cdq/xor/sub 三连就是 abs)
    Equal(3, Dist(3, 3, 0, 0), "abs/symmetry: negative deltas give the same distance");
    Equal(7, Dist(0, 0, -7, -2), "abs on both axes (cdq/xor edx/sub edx)");

    // ⚠ 这条是"咬"断言: 若有人把 helper 改回曼哈顿,上面 3 条会红。
    // 再额外压一条大差距用例,防止有人用 (max+min/2) 之类的折中式发明。
    Equal(100, Dist(0, 0, 100, 100),
        "large diagonal stays max(|dx|,|dy|)=100 (Manhattan=200, average-ish=150)");
}

// ===========================================================================
// (3) attack interval must come from the field, never from m_Abil.Level
// ===========================================================================
void VerifyAttackIntervalIsFieldNotLevelFormula()
{
    // 旧公式 `_MAX(300, 800 - m_Abil.Level*5)` 会随等级变化;原版只读 [+0x320]。
    // 造两只等级悬殊的英雄,间隔必须一模一样。
    var low = new HeroObject();
    low.m_Abil.Level = 1;
    var high = new HeroObject();
    high.m_Abil.Level = 200;

    Equal(low.m_nNextHitTime, high.m_nNextHitTime,
        "hero attack interval must NOT depend on m_Abil.Level " +
        "(native sub_693090 @0x6930B6 compares against [+0x320] only; the old " +
        "800-Level*5 formula moved with level)");

    // 并且它必须真的等于原版播种值,而不是"两边都错成同一个数"。
    Equal(1000, low.m_nNextHitTime,
        "hero attack interval == 1000 seeded at ctor @0x686515");

    // 装备/记录只会【调低】(sub_68FAB8 @0x68FBA0 `cmp edx,[eax+0x320]; jge skip`)。
    // 这里断言的是"C# 用的是可被调低的字段",而不是硬编码常量:改字段就该生效。
    low.m_nNextHitTime = 400;
    Equal(400, low.m_nNextHitTime,
        "attack interval is a mutable field so sub_68FAB8's lower-only narrowing " +
        "(@0x68FB8E-0x68FBAE, 2000 - rec[0x1D]*200) can apply");

    // ---------------------------------------------------------------------
    // 上面三条只检查【字段】,咬不到"消费点是否真的用这个字段"。
    // 2026-08-04 变异测试实测: 把 AttackTarget 里的
    //   int attackInterval = m_nNextHitTime;
    // 改回 `_MAX(300, 800 - m_Abil.Level*5)` 时上面三条【全绿】—— 假绿。
    // 所以这里直接读 AttackTarget 的 IL,断言:
    //   (a) 它确实加载了 m_nNextHitTime 字段;
    //   (b) 它没有加载 m_Abil(等级公式的必经之路),也没有 _MAX 调用。
    // 这样把消费点本身钉住。
    // ---------------------------------------------------------------------
    var attack = typeof(HeroObject).GetMethod("AttackTarget",
        BindingFlags.NonPublic | BindingFlags.Instance);
    Assert(attack != null, "HeroObject.AttackTarget must exist");

    var il = attack.GetMethodBody().GetILAsByteArray();
    var module = typeof(HeroObject).Module;

    var nextHitField = typeof(TBaseObject).GetField("m_nNextHitTime",
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
    Assert(nextHitField != null, "TBaseObject.m_nNextHitTime must exist");

    bool loadsNextHitTime = false;
    bool loadsAbil = false;
    bool callsMax = false;

    // ldfld = 0x7B, ldflda = 0x7C, call = 0x28, callvirt = 0x6F —— 都带 4 字节 token。
    for (int i = 0; i + 4 < il.Length; i++)
    {
        int token = BitConverter.ToInt32(il, i + 1);
        byte op = il[i];
        try
        {
            if (op == 0x7B || op == 0x7C)
            {
                var f = module.ResolveField(token);
                if (f != null && f.Name == "m_nNextHitTime") loadsNextHitTime = true;
                if (f != null && f.Name == "m_Abil") loadsAbil = true;
            }
            else if (op == 0x28 || op == 0x6F)
            {
                var mb = module.ResolveMethod(token);
                if (mb != null && mb.Name == "_MAX") callsMax = true;
            }
        }
        catch (Exception)
        {
            // token 不是字段/方法(operand 落在其它指令的立即数上)—— 忽略。
        }
    }

    Assert(loadsNextHitTime,
        "AttackTarget's IL must load m_nNextHitTime: the attack gate is " +
        "`tick - m_dwHitTick > [+0x320]` (native sub_693090 @0x6930B0-@0x6930B6). " +
        "MUTATION-VERIFIED 2026-08-04: reverting the consumer to " +
        "_MAX(300, 800 - m_Abil.Level*5) left every field-only assertion green.");
    Assert(!loadsAbil,
        "AttackTarget's IL must NOT read m_Abil: native never consults the hero's " +
        "level for the hit interval (the 800-Level*5 formula was invented)");
    Assert(!callsMax,
        "AttackTarget's IL must NOT call HUtil32._MAX: native has no floor-clamp " +
        "here, only sub_68FAB8's lower-only narrowing at load time");
}

// ===========================================================================
// (4) mode model
// ===========================================================================
void VerifyModeCycleAndDefault()
{
    var hero = new HeroObject();

    // 6865A6  mov byte [edi+0x6A1], 1
    Equal("Follow", hero.m_btNativeHeroMode.ToString(),
        "hero mode default must be 1=Follow/跟随 (ctor @0x6865A6 " +
        "mov byte [edi+0x6A1],1); Idle/Attack would be wrong");
    Equal((byte)1, (byte)hero.m_btNativeHeroMode,
        "the mode enum's numeric value must match the native byte (1=跟随)");

    // 名表 0x7D32FC 的三个可达项
    Equal("\u6538\u51fb".Replace("\u6538", "\u653b"),
        HeroObject.GetNativeHeroModeName((HeroObject.NativeHeroMode)0),
        "mode name [0] = 攻击 (table 0x7D32FC -> 0x6862CC)");
    Equal("\u8ddf\u968f",
        HeroObject.GetNativeHeroModeName((HeroObject.NativeHeroMode)1),
        "mode name [1] = 跟随 (0x6862DC)");
    Equal("\u4f11\u606f",
        HeroObject.GetNativeHeroModeName((HeroObject.NativeHeroMode)2),
        "mode name [2] = 休息 (0x6862EC)");

    // 6886AE-6886BE: (mode+1) mod 3 —— 循环必须是 1->2->0->1,且【绝不】出现 3
    // (表里 [3]=守护 [4]=决斗 不在 div 3 的可达范围)。
    hero.ChangeNativeHeroMode(false);
    Equal((byte)2, (byte)hero.m_btNativeHeroMode,
        "cycle 1 -> 2 (@0x6886B4 inc / @0x6886BC div 3)");
    hero.ChangeNativeHeroMode(false);
    Equal((byte)0, (byte)hero.m_btNativeHeroMode,
        "cycle 2 -> 0 (mod 3 wraps; a 4-state enum would give 3 here)");
    hero.ChangeNativeHeroMode(false);
    Equal((byte)1, (byte)hero.m_btNativeHeroMode, "cycle 0 -> 1");

    // 整圈扫一遍,确认永不越界到 3/4
    for (int i = 0; i < 30; i++)
    {
        hero.ChangeNativeHeroMode(false);
        Assert((byte)hero.m_btNativeHeroMode <= 2,
            "mode must stay in [0,2] forever (div 3 at @0x6886BC); got " +
            (byte)hero.m_btNativeHeroMode + " at step " + i);
    }

    // 688673-68867E: dl!=0 强制攻击模式
    hero.m_btNativeHeroMode = (HeroObject.NativeHeroMode)2;
    hero.ChangeNativeHeroMode(true);
    Equal((byte)0, (byte)hero.m_btNativeHeroMode,
        "force-attack sets mode 0 (@0x68867C xor eax,eax / mov [ebx+0x6A1],al)");

    // 688673: 已是攻击模式时原版只清出参、不改不报名 -> 返回空串
    Equal(string.Empty, hero.ChangeNativeHeroMode(true),
        "force-attack when already 0 reports nothing (@0x688673 je 0x68869C)");

    // 6886DA-68870B: 模式!=0 时清主人的目标;模式==0 时【不】清
    var owner = new TPlayObject();
    var bystander = new TPlayObject();
    hero.m_Master = owner;

    owner.m_TargetCret = bystander;
    hero.m_btNativeHeroMode = (HeroObject.NativeHeroMode)0;
    hero.ChangeNativeHeroMode(false);          // 0 -> 1, 非攻击 -> 应清
    Assert(owner.m_TargetCret == null,
        "leaving attack mode clears the master's target " +
        "(@0x6886F5-0x688705 mov dword [master+0x344],0)");

    owner.m_TargetCret = bystander;
    hero.m_btNativeHeroMode = (HeroObject.NativeHeroMode)2;
    hero.ChangeNativeHeroMode(true);           // -> 0, 攻击 -> 不应清
    Assert(ReferenceEquals(owner.m_TargetCret, bystander),
        "entering attack mode must NOT clear the master's target " +
        "(@0x6886DA cmp byte [ebx+0x6A1],0 / je 0x68870D skips the clear)");
}

// ===========================================================================
// (7) CM_HERO_CHGSTATE = native no-op
// ===========================================================================
void VerifyChgStateIsNoOp()
{
    Equal(1107, Grobal2.CM_HERO_CHGSTATE, "CM_HERO_CHGSTATE opcode");

    var hero = new HeroObject();
    var owner = new TPlayObject();
    var bystander = new TPlayObject();
    hero.m_Master = owner;
    hero.m_TargetCret = bystander;

    // 原版表项 1107 -> 0x6DBC2C(共享 default 落地),所以任何 nParam1 都不得改状态。
    foreach (int state in new[] { 0, 1, 2, 3, 99, -1 })
    {
        var before = hero.m_btNativeHeroMode;
        hero.ClientHeroChgState(new TProcessMessage { nParam1 = state });
        Equal((byte)before, (byte)hero.m_btNativeHeroMode,
            "CM_HERO_CHGSTATE(1107) must not change the hero mode for nParam1=" +
            state + " (native jump-table entry idx 0x40 -> 0x6DBC2C default sink)");
        Assert(ReferenceEquals(hero.m_TargetCret, bystander),
            "CM_HERO_CHGSTATE(1107) must not touch the target either, nParam1=" + state);
    }
}

// ===========================================================================
// (5) reap contract
// ===========================================================================
void VerifyMasterGoneReapContract()
{
    var reap = typeof(HeroObject).GetMethod("RunNativeMasterGoneReap",
        BindingFlags.NonPublic | BindingFlags.Instance);
    Assert(reap != null,
        "HeroObject.RunNativeMasterGoneReap must exist (sub_689FDC @0x68A018-0x68A071)");

    // --- 案例 A: 主人 ghost -> 立即回收,【无 1000ms/60s 延时】(@0x68A042/@0x68A046)
    {
        var hero = new HeroObject();
        var owner = new TPlayObject();
        hero.m_Master = owner;
        owner.m_boGhost = true;
        int tick = HUtil32.GetTickCount();
        reap.Invoke(hero, new object[] { tick });
        Assert(hero.m_boGhost,
            "master ghost => hero reaped with NO delay " +
            "(@0x68A042 cmp byte [master+0x73],0 / je -> normal AI; else reap)");
    }

    // --- 案例 B: 主人只是【死了】但还在场 -> 不回收(原版判的是 ghost 不是 death)
    {
        var hero = new HeroObject();
        var owner = new TPlayObject();
        hero.m_Master = owner;
        owner.m_boDeath = true;
        owner.m_dwDeathTick = HUtil32.GetTickCount() - 600000;   // 死了 10 分钟
        int tick = HUtil32.GetTickCount();
        reap.Invoke(hero, new object[] { tick });
        Assert(!hero.m_boGhost,
            "master merely DEAD (not ghost) must NOT reap the hero, however long ago " +
            "(@0x68A042 tests [master+0x73]=m_boGhost, not [master+0x74]=m_boDeath). " +
            "The generic slave block's 1000ms HP=0 rule does not apply to heroes.");
    }

    // --- 案例 C: 英雄自己是尸体但未满 60s -> 不回收 (@0x68A032 cmp 0xEA60)
    {
        var hero = new HeroObject();
        var owner = new TPlayObject();
        hero.m_Master = owner;
        hero.m_boDeath = true;
        int tick = HUtil32.GetTickCount();
        hero.m_dwDeathTick = tick - 59000;
        reap.Invoke(hero, new object[] { tick });
        Assert(!hero.m_boGhost,
            "own corpse at 59s must NOT be reaped (@0x68A032 cmp eax,0xEA60 / jae)");
    }

    // --- 案例 D: 英雄自己是尸体且满 60s -> 回收
    {
        var hero = new HeroObject();
        var owner = new TPlayObject();
        hero.m_Master = owner;
        hero.m_boDeath = true;
        int tick = HUtil32.GetTickCount();
        hero.m_dwDeathTick = tick - 60000;
        reap.Invoke(hero, new object[] { tick });
        Assert(hero.m_boGhost,
            "own corpse at exactly 60000ms IS reaped (`jae` is >=, not >) " +
            "(@0x68A032-@0x68A037)");
    }

    // --- 案例 E: 边界另一侧,60001ms 也回收
    {
        var hero = new HeroObject();
        var owner = new TPlayObject();
        hero.m_Master = owner;
        hero.m_boDeath = true;
        int tick = HUtil32.GetTickCount();
        hero.m_dwDeathTick = tick - 60001;
        reap.Invoke(hero, new object[] { tick });
        Assert(hero.m_boGhost, "own corpse past 60s is reaped");
    }

    // --- 案例 F: 活着 + 主人在场 -> 什么都不做
    {
        var hero = new HeroObject();
        var owner = new TPlayObject();
        hero.m_Master = owner;
        int tick = HUtil32.GetTickCount();
        reap.Invoke(hero, new object[] { tick });
        Assert(!hero.m_boGhost && !hero.m_boDeath,
            "alive hero with a present master is untouched (@0x68A046 je 0x68A076)");
    }

    // --- 案例 G: 回收走的是 MarkDelete(变 ghost),【不是】Die。
    //     若有人把 MakeGhost() 换成 Die(),m_boDeath 会被置位 —— 这条会红。
    {
        var hero = new HeroObject();
        var owner = new TPlayObject();
        hero.m_Master = owner;
        owner.m_boGhost = true;
        int tick = HUtil32.GetTickCount();
        reap.Invoke(hero, new object[] { tick });
        Assert(hero.m_boGhost,
            "reap goes through MarkDelete/MakeGhost (@0x68A06C call sub_768060)");
        Assert(!hero.m_boDeath,
            "reap must NOT set m_boDeath: sub_768060 is TCreature.MarkDelete " +
            "(strings @0x768138/@0x768174) writing [+0x73]; Die is a DIFFERENT routine " +
            "(VMT +0x84 = sub_76631C writing [+0x74] @0x766323)");
    }

    // =====================================================================
    // 案例 H/I —— END-TO-END: 必须经【真实 Run()】触发,不只是反射直调。
    //
    // 2026-08-04 自查抓到的真 bug: 最初的 Run() 把回收门放在 FindMaster() 之后,
    // 而 FindMaster() 会把 ghost/死亡的主人过滤成 null 并提前 return —— 于是
    // "主人 ghost -> 回收" 这条最重要的路径【经 Run() 永不触发】,可上面 A..G
    // (反射直调 RunNativeMasterGoneReap) 却全绿。原版门序是回收门在取主人之前
    // (0x68A00B 判的是原始绑定指针 [hero+0x68C],0x68A018 才是回收门)。
    // 这两条断言就是为了让那个 bug 不能再躲。
    // =====================================================================

    // --- 案例 H: 主人 ghost,走真实 Run() -> 必须被回收
    {
        var hero = new HeroObject();
        var owner = new TPlayObject();
        hero.m_Master = owner;
        hero.m_WAbil.HP = 400;
        hero.m_WAbil.MaxHP = 400;
        hero.m_Abil.HP = 400;
        hero.m_Abil.MaxHP = 400;
        owner.m_boGhost = true;

        hero.Run();

        Assert(hero.m_boGhost,
            "END-TO-END: a real Run() tick must reap the hero when the master is ghost " +
            "(native gate order: 0x68A00B pointer-null check THEN 0x68A018 reap gate, " +
            "BEFORE the normal-AI path at 0x68A076). MUTATION-VERIFIED: moving the reap " +
            "after FindMaster() makes this path dead because FindMaster() filters ghost " +
            "masters to null and returns early.");
    }

    // --- 案例 I: 自己是尸体满 60s,走真实 Run() -> 必须被回收
    {
        var hero = new HeroObject();
        var owner = new TPlayObject();
        hero.m_Master = owner;
        hero.m_boDeath = true;
        hero.m_dwDeathTick = HUtil32.GetTickCount() - 61000;

        hero.Run();

        Assert(hero.m_boGhost,
            "END-TO-END: a real Run() tick must reap a hero corpse older than 60s " +
            "(@0x68A032); an early `if (m_boDeath) return;` before the reap gate would " +
            "leave corpses idling forever");
    }

    // --- 案例 J: 活着 + 主人在场,走真实 Run() -> 不得被回收(防止 H/I 靠"总是回收"作弊)
    {
        var hero = new HeroObject();
        var owner = new TPlayObject();
        hero.m_Master = owner;
        hero.m_WAbil.HP = 400;
        hero.m_WAbil.MaxHP = 400;
        hero.m_Abil.HP = 400;
        hero.m_Abil.MaxHP = 400;

        hero.Run();

        Assert(!hero.m_boGhost,
            "END-TO-END: a healthy hero with a present master must survive a Run() tick " +
            "(@0x68A046 je 0x68A076 -> normal AI)");
    }
}

// ===========================================================================
// (6) generic slave blocks must exclude heroes
// ===========================================================================
void VerifyGenericSlaveBlocksExcludeHero()
{
    // 原版把英雄的通用 master 槽 [+0x38C] 钉死为 NULL(@0x690B0E),所以
    // "主人死了 1 秒 -> HP=0" 与 "忠诚度到期 -> 摘主人 + HP/10" 都对英雄不成立。
    //
    // 这里跑【真实的 TBaseObject.Run】: 一只英雄 + 一只普通怪物奴隶,同样的主人死亡
    // 情形,英雄必须毫发无伤,怪物必须按通用块被处理。这样断言咬的是分支本身,
    // 而不是某个字段的名字。
    var owner = new TPlayObject();
    owner.m_boDeath = true;
    owner.m_dwDeathTick = HUtil32.GetTickCount() - 30000;   // 死了 30 秒,远超 1000ms

    var hero = new HeroObject();
    hero.m_Master = owner;
    hero.m_WAbil.HP = 400;
    hero.m_WAbil.MaxHP = 400;
    hero.m_Abil.HP = 400;
    hero.m_Abil.MaxHP = 400;

    int hpBefore = hero.m_WAbil.HP;
    hero.Run();

    Assert(hero.m_WAbil.HP == hpBefore || hero.m_WAbil.HP > 0,
        "hero must not be zeroed by the generic master-die slave block " +
        "(native pins [hero+0x38C]=NULL via VMT +0x154/+0x158 @0x690B0E, so " +
        "TAnimal.Run's @0x71E594 [+0x38C]!=0 gate never opens for a hero)");
    Assert(ReferenceEquals(hero.m_Master, owner),
        "hero must keep its master binding: the royalty block would null it " +
        "(m_dwMasterRoyaltyTick defaults to 0 so `GetTickCount() > 0` is always true)");

    // 主人 ghost 的情形: 通用块会 MakeGhost() 且带 1000ms 延时;英雄侧应由
    // RunNativeMasterGoneReap 无延时接管 —— 两条路都会让它 ghost,所以这里
    // 单独断言"HP 没被通用块砍成 1/10"(那是忠诚度块的指纹)。
    var hero2 = new HeroObject();
    var owner2 = new TPlayObject();
    hero2.m_Master = owner2;
    hero2.m_WAbil.HP = 400;
    hero2.m_WAbil.MaxHP = 400;
    hero2.m_Abil.HP = 400;
    hero2.m_Abil.MaxHP = 400;
    hero2.Run();
    Assert(hero2.m_WAbil.HP != 40,
        "hero HP must never be decimated by the royalty block (HP/=10 -> 40 would be " +
        "its fingerprint); native has no [+0x488] royalty reader in any hero function");
}

// ===========================================================================
void Assert(bool condition, string label)
{
    checks++;
    if (!condition)
        throw new InvalidOperationException(label);
}

void Equal<T>(T expected, T actual, string label)
{
    checks++;
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{label}: expected={expected}, actual={actual}");
}

void InitializeRuntime()
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
}

void PrepareRuntimeConfig()
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
