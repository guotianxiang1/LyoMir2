using SystemModule;

namespace GameSvr
{
    // =====================================================================================
    // HERO-MAGIC-2 — 英雄魔法逐技能分派器 sub_68DD88 的证据图与可落地分支。
    //
    // 底本: D:\loym2\staging\_reunpack_work\flat_image.bin (ImageBase=0x400000), capstone 5.0.7。
    //
    // ---- 定位更正 (自行复核) ----
    // 交接给出的 "sub_68D2F8 = 英雄魔法并行分派器" 有误。0x68D2F8 是一个独立小函数
    // (0x68D2F8 prologue `55 8B EC 83 C4 F4`, 0x68D3B5 ret), 内容是取 magic 0x138(312)、
    // 用 sub_4C896C 的有效等级去查 [0x7D33C8]/[0x7D33D4]/[0x7D33E0] 三张表并经 sub_78E830
    // 下发 0x66/0x67/0x68 三项加成 —— 与魔法分派无关。
    // **真正的分派器是 sub_68DD88 (0x68DD88..0x68E705)**, 两张跳表 0x68DE7D / 0x68DEFF 与
    // 交接描述一致, 交接列出的 10 个 0x73EA20 调用点 (0x68E0BD..0x68E614) 全部落在其体内。
    // 调用链: sub_68DC50(0x68DC50, 命中率/距离/技能查找门) --0x68DD72--> sub_68DD88;
    //         sub_68DD88 另有直接调用者 0x692B48 (英雄 AI 主循环 sub_692AF4)。
    //
    // ---- 接收者身份 (VMT 枚举, 非推测) ----
    // sub_692AF4 出现在 VMT 槽 0x685C0C(+0x2A4 of TWarHero 0x685968) 与 0x5F584C(TSecWarHero)。
    // 类树 TWarHero <- THeroAct <- THumanKind <- TCreature <- TBaseObj <- TObject。
    // 佐证: sub_68DBE0 的两个嵌套函数 0x68DAE8/0x68DB64 读 [self+0x68C]+0x18AC/0x18DC/0x18DE
    // (仅 TPlayer 尺寸 6472 容得下), 且拿 self 的 +0x2AC/+0x2B0/+0x2B4/+0x2B8 (HP/MaxHP/MP/MaxMP)
    // 与之比百分比 —— 即 self=英雄、[self+0x68C]=主人的自动喝药阈值。
    //
    // ---- sub_68DD88 主干 (eax=hero, edx=UserMagic(esi), ecx=target([ebp-4])) ----
    //   0x68DD98  boSpellFire([ebp-0x16]) = 1
    //   0x68DD9C  boSpellFail([ebp-0x15]) = 0
    //   0x68DDA0  spellPoint = sub_4C8888(UserMagic)           ; == GetHeroSpellPoint
    //   0x68DDAA  result([ebp-5]) = 0
    //   0x68DDAE  if (spellPoint > [hero+0x2B4]=MP) -> 0x68E6FC 直接返回 0
    //   0x68DDBA  sub_76A894(hero, spellPoint)                 ; DamageSpell (扣蓝+刷新)
    //   0x68DDC3  target!=nil ? (tx,ty)=target(+0x12C,+0x130) : hero(+0x12C,+0x130)
    //   0x68DDF5  if (wMagicID != 0x2A/42) sub_769258(...)     ; SM_SPELL(0x11) 广播
    //             == C# MagicManager.SendNativeSpell: Param=tx, Tag=ty,
    //                Series=MakeWord(btEffect,btEffectType),
    //                body={wMagicID,effectiveLevel}
    //   0x68DE41  if (target!=nil && sub_772DA8(target)) target = nil     ; 死亡目标清空
    //   0x68DE58  分派 (见下)
    //   0x68E6A9  DEFAULT: result = 0; 直接 0x68E6FC 返回 (不写 tick, 不发 0x27E)
    //   0x68E6AF  收敛: [hero+0x360] = GetTickCount()
    //   0x68E6BA    if (boSpellFail) 返回 result(=0)
    //   0x68E6C0    if (boSpellFire) sub_76920C(...)           ; SM_MAGICFIRE(0x27E) 广播
    //             == C# MagicManager.SendNativeMagicFire: Param=tx, Tag=ty,
    //                Series=MakeWord(btEffectType,btEffect), body={targetId,effectiveLevel}
    //   0x68E6F8    result = 1
    //   与玩家 DoSpell sub_6ED62C 的差异: (a) 英雄没有 sub_78FE88 的 9 格射程门;
    //   (b) wMagicID==42 跳过 SM_SPELL; (c) 英雄多写 [hero+0x360]; (d) 英雄收敛只有
    //   0x76920C 一路, 没有玩家的 0x7692BC(失败变体)。
    //
    // ---- 分派入口 (0x68DE58) ----
    //   cmp eax,0x1D ; jg 0x68DEE1 ; je 0x68E25E(id 29) ; cmp eax,0x18 ; ja 0x68E6A9
    //   jmp [eax*4 + 0x68DE7D]                     ; TABLE1, id 0..24
    //   0x68DEE1: cmp eax,0x30 ; jg 0x68DF33 ; je 0x68E60B(id 48)
    //             add eax,-0x1E ; cmp eax,0xC ; ja 0x68E6A9 ; jmp [eax*4+0x68DEFF] ; TABLE2 30..42
    //   0x68DF33: cmp eax,0x73 ; jg 0x68DF6A ; je 0x68E647(id 115)
    //   0x68DF3E: sub eax,0x3B ; je 0x68E577(id 59)
    //   0x68DF47: sub eax,3    ; je 0x68E398(id 62)
    //   0x68DF50: add eax,-4 ; sub eax,3 ; jb 0x68E634(id 66/67/68)
    //   0x68DF5C: sub eax,0x2B ; je 0x68E313(id 112)  ; 0x68DF65 jmp DEFAULT
    //   0x68DF6A: sub eax,0x82 ; je 0x68E6AF(id 130, 空成功)
    //   0x68DF75: sub eax,0x65 ; je 0x68E666(id 231)
    //   0x68DF7E: dec eax      ; je 0x68E695(id 232)
    //   0x68DF85: sub eax,0x29 ; je 0x68E655(id 273) ; 0x68DF8E jmp DEFAULT
    //
    // ---- TABLE1 @0x68DE7D (25 项, 逐 dword 读出) ----
    //   0 ->0x68E6A9 DEFAULT          1 ->0x68DF93 [vmt+0x108](target)
    //   2 ->0x68DFA9 [vmt+0x10C](@target) 成功后 [hero+0x6C0]=GetTickCount
    //   3 ->DEFAULT   4 ->DEFAULT     5 ->0x68DF93 (同 1)
    //   6 ->0x68DFD0 施毒: sub_767498(IsProperTarget) + sub_68C8F0(hero,[hero+0x650],cl=1),
    //                然后 [hero+0x650]==1 -> [vmt+0x110]; ==2 -> [vmt+0x114]; 结果取反入 boSpellFail
    //                (注意: 英雄【不】走玩家 0x6ED945 的槽9 TPoisons 内联路径)
    //   7 ->DEFAULT   8 ->0x68E043 [vmt+0x118](ecx=0)  ; 英雄无玩家 0x6ED9FA 的 2000ms 门
    //   9 ->0x68E056 sub_76EC5C(ecx=tx, push ty)
    //  10 ->0x68E07E [vmt+0x120](ecx=tx, push ty)
    //  11 ->0x68E096 sub_76EA3C(ecx=target); 失败 target=nil
    //  12 ->DEFAULT
    //  13 ->0x68E0B4 **sub_73EA20(100,cl=1)@0x68E0BD** -> sub_76EB54(ecx=target); 否则 boSpellFail=1
    //  14 ->0x68E0E2 **sub_73EA20(100,1)@0x68E0EB** -> sub_76ECEC(ecx=tx, push ty); 否则 fail
    //  15 ->0x68E112 **sub_73EA20(100,1)@0x68E11B** -> sub_76ED74(ecx=tx, push ty); 否则 fail
    //  16 ->0x68E6AF 空成功 (玩家 id16 有 sub_76EFC0, 英雄没有)
    //  17 ->0x68E142 **sub_73EA20(100,1)@0x68E15C** -> 主人造宠 "变异骷髅" (见下)
    //  18 ->0x68E1E2 **sub_73EA20(100,1)@0x68E1EB** -> sub_76F1B8(hero,UserMagic); 否则 fail
    //  19 ->0x68E20B **sub_73EA20(100,1)@0x68E214** -> sub_76FD40(ecx=tx, push ty); 否则 fail
    //  20 ->0x68E6AF 空成功   21 ->0x68E6AF 空成功
    //  22 ->0x68E23B sub_77062C(ecx=tx, push ty)
    //  23 ->0x68E562 sub_76F21C(ecx=tx, push ty)
    //  24 ->0x68E250 sub_76F5E0(hero, UserMagic)
    //  29 ->0x68E25E sub_76F678(ecx=tx, push ty)      ; 走 0x68DE67 的 je, 不在表内
    //
    // ---- TABLE2 @0x68DEFF (13 项, 索引 = wMagicID-30) ----
    //  30 ->0x68E273 **sub_73EA20(500,1)@0x68E28D** -> 主人造宠 "神兽"
    //  31 ->0x68E53F sub_76F9E0(hero, UserMagic)
    //  32 ->0x68E6AF 空成功
    //  33 ->0x68E54D sub_76F2AC(ecx=tx, push ty)
    //  34 ->DEFAULT
    //  35 ->0x68E521 sub_76F404(ecx=target); 失败 target=nil
    //  36 ->0x68E58E [vmt+0x130](hero, UserMagic)
    //  37 ->0x68E59F [vmt+0x11C](ecx=0)
    //  38 ->DEFAULT
    //  39 ->0x68E06B sub_76FA5C(ecx=target)
    //  40 ->DEFAULT
    //  41 ->0x68E469 **sub_73EA20(500,1)@0x68E483** -> 主人造宠 "月灵" (+等级拷贝)
    //  42 ->0x68E5B2 sub_68F0C8(hero, effLvl); 成功后 [vmt+0xD8](cx=0x2734) + TrainSkill
    //
    // ---- 阶梯 (>48) ----
    //  48 ->0x68E60B **sub_73EA20(100,1)@0x68E614** -> sub_76FBBC(ecx=target); 否则 fail
    //  59 ->0x68E577 sub_76F33C(ecx=tx, push 0, push ty)
    //  62 ->0x68E398 30s 门 + **sub_73EA20(2000,1)@0x68E3CB** -> 主人造宠 "圣兽"
    //  66/67/68 ->0x68E634 sub_745744(ecx=target); boSpellFail = !结果
    // 112 ->0x68E313 主人造宠 "火灵" (【无】护身符门, 且 sub_7661E8 命中时 jne 直接收敛)
    // 115 ->0x68E647 sub_68F248(hero); boSpellFail = !结果
    // 130 ->0x68E6AF 空成功
    // 231 ->0x68E666 sub_73E93C(edx=1)【另一条护身符例程】-> sub_76F8BC(ecx=target, push 0)
    // 232 ->0x68E695 sub_76F8A8(ecx=tx, push 0, push ty)
    // 273 ->0x68E655 [vmt+0x288](ecx=target)
    //
    // 10 个 0x73EA20 调用点全部复核无误, 与交接表一致 (30=500 已按 0x68E286 `mov edx,0x1F4` 核实)。
    //
    // ---- 主人造宠内联段 (17/30/41/62/112 共用形状; 以 17 @0x68E142 为准) ----
    //   0x68E142  boSpellFail = 1
    //   0x68E146  if ([hero+0x68C] == nil) -> 收敛 (失败)
    //   0x68E153  cl=1, edx=100, call 0x73EA20            ; 失败 -> 收敛 (失败)
    //   0x68E16B  nMaxMob = sub_690B6C(hero)
    //   0x68E173  master = [hero+0x68C]
    //   0x68E17C  if (sub_7661E8(master, 0x28A1)) 跳过造宠   ; == master.CheckServerMakeSlave()
    //   0x68E18C  push nMaxMob / push 0xD2F00 / push 1 / push 0xA
    //   0x68E19B  cl = sub_4C896C(UserMagic)              ; 有效技能等级
    //   0x68E1A4  edx = "变异骷髅"@0x68E710
    //   0x68E1AE  [hero+0x6C4] = master.[vmt+0xEC](...)   ; TPlayer.MakeSlave = 0x6CB070
    //   0x68E1BA  if ([hero+0x6C4] != nil) [vmt+0x3C](UserMagic, Random(3)+1)  ; TrainSkill
    //   0x68E1D9  boSpellFail = 0
    //   名字/数量/门槛按技能: 17 "变异骷髅"@0x68E710 n=100 | 30 "神兽"@0x68E724 n=500
    //                        62 "圣兽"@0x68E744 n=2000    | 41 "月灵"@0x68E780 n=500
    //                        112 "火灵"@0x68E734 无护身符
    //   41 额外: 0x68E4EB `cmp byte[slave+0x178],0x82` 命中则 0x68E4FB
    //            `slave.[+0x278] = hero.[+0x278]` (把英雄等级拷给月灵) 再 TrainSkill;
    //            不命中则【不】TrainSkill。
    //   62 额外: 0x68E39C `if (GetTickCount() - [hero+0x50C]) <= 0x7530(30000)` ->
    //            0x68E451 发系统消息 "圣兽刚收回不到30秒，元气尚未回复"@0x68E754 ([vmt+0xD4], cx=0xFFDB)
    //            并【不】造宠 (boSpellFail 保持 1)。
    //   112 额外: 0x68E345 是 `jne 0x68E6AF` —— sub_7661E8 命中时直接收敛且 boSpellFail 仍为 1;
    //            且 0x68E379 的 TrainSkill 是【无条件】的 (不检查 slave 是否为 nil)。
    //
    // ---- sub_690B6C (0x68E16B 等处的 nMaxMob) 全函数 ----
    //   690B6C mov edx,1 ; ecx=[hero+0x68C] ; nil -> 1
    //   690B7B al=[master+0x72] (m_btJob); sub al,1
    //          jb  -> 2   (job 0 战士)
    //          je  -> 6   (job 1 法师)
    //          dec/je -> 2 (job 2 道士)
    //          dec/je -> 2 (job 3)
    //          else   -> 1
    //
    // ---- TPlayer.MakeSlave (0x6CB070) 的英雄槽与 BoFromHero 语义 ----
    //   (a) 0x6CB09D-0x6CB108 主人的英雄槽 GC: hero=[master+0xBB0]; hero 非空/未死/非 ghost 时,
    //       若 [hero+0x6C4] 指向的宠已死或 ghost 则清空该槽。
    //   (b) 0x6CB10E-0x6CB1E1 第 6 参 (英雄传 1, 玩家传 0) 为真时改用【英雄坐标】做空格搜索落地。
    //   (c) 0x6CB1E7-0x6CB1F0 若 [hero+0x6C4] 仍非空, nMaxMob 自增 1。
    //   三段现由 TBaseObject.MakeSlaveCore 统一实现：英雄槽 GC 和 nCount+1
    //   在 BoFromHero 门之前执行；BoFromHero=true 只改用英雄的物理环境/坐标，
    //   生成后的 master、m_SlaveList 与 4469 通知仍归人物。
    // =====================================================================================
    public partial class HeroObject
    {
        /// <summary>wMagicID 112 "火灵"。SpellsDef 未收录该 id, 原生只在
        /// sub_68DD88 的阶梯 0x68DF5C `sub eax,0x2B; je 0x68E313` 处出现。</summary>
        private const int NativeHeroSkillFireSpirit = 112;

        /// <summary>
        /// 原版英雄切服标志 [hero+0x4BA]。人物切服设置器 sub_6BD044
        /// @0x6BD096..0x6BD0A0 在人物存在英雄时同步置 1；英雄记录编码器
        /// sub_689034 @0x68934A 只在此标志置位时写入专属召唤记录。
        /// </summary>
        internal bool m_boNativeSwitchData;

        /// <summary>[hero+0x6C4] —— 分派器为 17/30/41/62/112 记录的召唤物。</summary>
        private TBaseObject m_NativeHeroSummonSlave;

        internal bool IsNativeHeroSummonSlave(TBaseObject candidate) =>
            candidate != null && ReferenceEquals(m_NativeHeroSummonSlave, candidate);

        internal TBaseObject GetNativeSwitchSlaveForSave()
        {
            var slave = m_boNativeSwitchData ? m_NativeHeroSummonSlave : null;
            return slave != null && !slave.m_boGhost && !slave.m_boDeath
                ? slave
                : null;
        }

        // TPlayer.MakeSlave sub_6CB070 performs this maintenance before it
        // inspects BoFromHero, so ordinary player summons must see it too.
        internal bool PrepareNativeHeroSummonSlotForMakeSlave()
        {
            if (m_NativeHeroSummonSlave != null &&
                (m_NativeHeroSummonSlave.m_boDeath ||
                 m_NativeHeroSummonSlave.m_boGhost))
            {
                m_NativeHeroSummonSlave = null;
            }
            return m_NativeHeroSummonSlave != null;
        }

        /// <summary>
        /// THeroAct slave-record restore, sub_68FAB8. This is the RM_10401
        /// consumer for the embedded TSlaveInfo at hero record +0x4694.
        /// </summary>
        internal void RestoreNativeHeroSummon(TSlaveInfo slaveInfo)
        {
            var master = m_Master as TPlayObject;
            if (master == null || m_NativeHeroSummonSlave != null || m_boGhost)
            {
                // 0x68FB2E..0x68FB30 explicitly clears hero+0x6C4 on every
                // rejected arm, including an already occupied slot.
                m_NativeHeroSummonSlave = null;
                return;
            }

            var slave = master.MakeNativeSlave(slaveInfo.sSlaveName,
                slaveInfo.btSlaveLevel, NativeHeroSummonMaxMob(master),
                slaveInfo.dwRoyaltySec, fromHero: true, hpAfterSlave: 10);
            m_NativeHeroSummonSlave = slave;
            if (slave == null)
                return;

            // 0x68FB44..0x68FBCB: unlike the player cross-server restore,
            // every race restores these fields. Only race 0x82 has a special
            // level-copy arm immediately before the final RecalcAbilitys.
            slave.m_nKillMonCount = slaveInfo.nKillCount;
            slave.m_btSlaveExpLevel = slaveInfo.btSlaveExpLevel;
            slave.m_WAbil.HP = unchecked((ushort)slaveInfo.nHP);
            slave.m_WAbil.MP = unchecked((ushort)slaveInfo.nMP);

            var walkSpeed = 1500 - slaveInfo.btSlaveLevel * 200;
            if (walkSpeed < slave.m_nWalkSpeed)
                slave.m_nWalkSpeed = walkSpeed;
            var nextHitTime = 2000 - slaveInfo.btSlaveLevel * 200;
            if (nextHitTime < slave.m_nNextHitTime)
                slave.m_nNextHitTime = nextHitTime;

            if (slave.m_btRaceServer == 0x82)
                slave.m_Abil.Level = m_Abil.Level;
            slave.RecalcAbilitys();
        }

        /// <summary>[hero+0x50C] —— id 62 "圣兽" 的收回时间戳; 原生门为
        /// `GetTickCount() - [hero+0x50C] > 30000`。写入方 (收回圣兽) 不在本子系统内,
        /// 保持 0 即 "从未收回过", 与原生首次施放时的判定一致。</summary>
        private int m_dwNativeHeroSinSuBackTick;

        /// <summary>原生 sub_690B6C: 主人职业决定的召唤上限。</summary>
        private int NativeHeroSummonMaxMob(TPlayObject master)
        {
            if (master == null)
            {
                return 1;                       // 0x690B79 je -> edx=1
            }
            switch (master.m_btJob)
            {
                case 0: return 2;               // 0x690B9C
                case 1: return 6;               // 0x690B8E
                case 2: return 2;               // 0x690B95
                case 3: return 2;               // 0x690BA3
                default: return 1;              // 0x690B8C
            }
        }

        /// <summary>分派器里【不】自带造宠内联段的那 6 个护身符门 (13/14/15/18/19/48)。
        /// 命中时给出原生 edx 立即数; 其余 4 个 (17/30/41/62) 的门在
        /// <see cref="TryReleaseNativeHeroSummonMagic"/> 内部按原生顺序执行。</summary>
        internal static bool TryGetNativeHeroAmuletCost(ushort wMagicID, out int nCount)
        {
            switch (wMagicID)
            {
                case SpellsDef.SKILL_FIRECHARM:      nCount = 100; return true;  // 0x68E0BD
                case SpellsDef.SKILL_HANGMAJINBUB:   nCount = 100; return true;  // 0x68E0EB
                case SpellsDef.SKILL_DEJIWONHO:      nCount = 100; return true;  // 0x68E11B
                case SpellsDef.SKILL_CLOAK:          nCount = 100; return true;  // 0x68E1EB
                case SpellsDef.SKILL_BIGCLOAK:       nCount = 100; return true;  // 0x68E214
                case SpellsDef.SKILL_GROUPAMYOUNSUL: nCount = 100; return true;  // 0x68E614
                default:                             nCount = 0;   return false;
            }
        }

        /// <summary>本分派器把哪些 wMagicID 当作 "主人造宠" 内联段处理。</summary>
        internal static bool IsNativeHeroSummonMagic(ushort wMagicID)
        {
            return wMagicID == SpellsDef.SKILL_SKELLETON   // 17  @0x68E142
                || wMagicID == SpellsDef.SKILL_SINSU       // 30  @0x68E273
                || wMagicID == SpellsDef.SKILL_ANGEL       // 41  @0x68E469
                || wMagicID == SpellsDef.SKILL_62          // 62  @0x68E398
                || wMagicID == NativeHeroSkillFireSpirit;  // 112 @0x68E313
        }

        /// <summary>原生 17/30/41/62/112 的主人造宠内联段。
        /// 返回值 = 原生 boSpellFail 的补 (true = 收敛后会发 SM_MAGICFIRE 并返回成功)。</summary>
        private bool TryReleaseNativeHeroSummonMagic(TUserMagic userMagic, ushort wMagicID)
        {
            string sMonName;
            int nAmuletCount;
            switch (wMagicID)
            {
                case SpellsDef.SKILL_SKELLETON: sMonName = "变异骷髅"; nAmuletCount = 100; break;
                case SpellsDef.SKILL_SINSU:     sMonName = "神兽";     nAmuletCount = 500; break;
                case SpellsDef.SKILL_ANGEL:     sMonName = "月灵";     nAmuletCount = 500; break;
                case SpellsDef.SKILL_62:        sMonName = "圣兽";     nAmuletCount = 2000; break;
                case NativeHeroSkillFireSpirit: sMonName = "火灵";     nAmuletCount = 0; break;
                default: return false;
            }

            // id 62 独有: 0x68E39C-0x68E464 的 30 秒门, 未过门发系统消息且不造宠。
            if (wMagicID == SpellsDef.SKILL_62)
            {
                if (HUtil32.GetTickCount() - m_dwNativeHeroSinSuBackTick <= 30000)
                {
                    // 0x68E451 `mov cx,0xFFDB` -> [vmt+0xD4] = THumanKind SysMsg 0x73C8F4,
                    // 其体为 SendMsg(self, 0x2774, 0, sMsg, 0,0,0) —— 消息投到英雄自己的
                    // 队列, 再由英雄的消息处理转发。这里用同一条基类 SysMsg, 不另造投递路径。
                    SysMsg("圣兽刚收回不到30秒，元气尚未回复", MsgColor.Red, MsgType.Hint);
                    return false;                          // boSpellFail 保持 1
                }
            }

            // 0x68E146 / 0x68E277 / 0x68E317 / 0x68E3B5 / 0x68E46D: master 为空直接失败。
            var master = FindMaster();
            if (master == null)
            {
                return false;
            }

            // 0x68E15C / 0x68E28D / 0x68E3CB / 0x68E483 —— 112 没有这道门。
            if (nAmuletCount > 0 && !NativeConsumeBujukCharm(nAmuletCount, true))
            {
                return false;
            }

            var nMaxMob = NativeHeroSummonMaxMob(master);          // sub_690B6C
            var nMakeLevel = TPlayObject.GetNativeMagicProducerEffectiveLevel(userMagic); // sub_4C896C
            const int dwRoyaltySec = 0xD2F00;                      // 864000 秒 = 10 天

            if (master.CheckServerMakeSlave())                     // sub_7661E8(master, 0x28A1)
            {
                // 112 在这里是 `jne 0x68E6AF` (0x68E345): 直接收敛且 boSpellFail 仍为 1。
                // 其余四个是跳到 boSpellFail=0 (0x68E1D9/0x68E30A/0x68E448/0x68E518)。
                return wMagicID != NativeHeroSkillFireSpirit;
            }

            // sub_6CB070: ECX feeds both level bytes; [ebp+8]=10 is the
            // signed HP percentage applied when royalty expires.
            var slave = master.MakeNativeSlave(sMonName, nMakeLevel,
                nMaxMob, dwRoyaltySec, fromHero: true, hpAfterSlave: 10);
            m_NativeHeroSummonSlave = slave;                       // [hero+0x6C4]

            if (wMagicID == SpellsDef.SKILL_ANGEL)
            {
                // 0x68E4E1-0x68E515: 仅当 slave 非空且 byte[slave+0x178]==0x82 时,
                // 先把英雄等级 word[hero+0x278] 拷到 slave, 再 TrainSkill。
                // [slave+0x178] = m_btRaceServer, [+0x278] = m_Abil.Level (工程内已证的映射)。
                if (slave != null && slave.m_btRaceServer == 0x82 && slave.m_Abil != null)
                {
                    slave.m_Abil.Level = m_Abil.Level;
                    TrainSkill(userMagic, M2Share.RandomNumber.Random(3) + 1);
                }
            }
            else if (wMagicID == NativeHeroSkillFireSpirit)
            {
                // 0x68E379: 112 的 TrainSkill 无 slave!=nil 前置。
                TrainSkill(userMagic, M2Share.RandomNumber.Random(3) + 1);
            }
            else if (slave != null)
            {
                // 0x68E1BA / 0x68E2EB / 0x68E429
                TrainSkill(userMagic, M2Share.RandomNumber.Random(3) + 1);
            }

            return true;                                           // boSpellFail = 0
        }
    }
}
