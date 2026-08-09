using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        // ==================================================================
        // 杀怪经验发放链 —— sub_6F7A18 (WinExp) / sub_6C0318 / sub_6C037C
        //
        // 战神的击杀发经验是一条四段链，C# 之前只有首尾两段，中间两段整段缺失：
        //
        //   sub_6C0148  击杀回调
        //     0x6C0193  call sub_6C02A4   CalcGetExp（等级差衰减）
        //     0x6C019C  call sub_6F79E8   组队分配适配器
        //     0x6C01A5  call sub_6F7A18   WinExp            <-- 本文件 §1
        //       0x6F7A74  call sub_6C0318 award             <-- 本文件 §3
        //         0x6C0344  call sub_6C037C 主宠比例分割     <-- 本文件 §2
        //         0x6C0358  call sub_687714 英雄得经验
        //         0x6C036B  call sub_6C03F8 落账/升级
        //                   == C# GrantNativePlayerExperience（已忠实移植）
        //
        // 缺的两段导致两个后果，第二个比第一个更隐蔽：
        //   (a) 英雄打怪永远不涨经验（C# 里英雄只能靠 GM/脚本/魔塔奖励涨）；
        //   (b) 有活英雄时主人本该只拿 ML/(ML+HL)，C# 却拿满 100% —— 缺口
        //       同时**放大了玩家经验**，不只是少给英雄。
        //
        // 同时，C# 旧 WinExp 里的五个缩放全部在战神中不存在（见 §4）。
        // ==================================================================

        // ------------------------------------------------------------------
        // §1  WinExp = sub_6F7A18
        //
        // 6F7A1B  add esp,-0x14                ; 5 个 dword 桶
        // 6F7A2F  call sub_403B2C              ; FillChar(桶,20,0)
        // 6F7A34  mov [ebp-0x10],ebx           ; 桶[1] := dwExp
        // 6F7A3B  call sub_6F7A8C              ; 桶[2] := Nx 倍经验加成
        // 6F7A47  call sub_6F7AA4              ; 桶[2] += MultiTempExpRate 加成
        // 6F7A4C  add [ebp-0xc],eax
        // 6F7A4F  cmp ebx,[ebp-0xc] / 7F 05 jg ; dwExp > 桶[2] ?
        // 6F7A54  sub [ebp-0xc],ebx            ;   否：桶[2] -= dwExp（去掉重复的基数）
        // 6F7A59  xor eax,eax / mov [ebp-0xc],eax ; 是：桶[2] := 0
        // 6F7A5E  xor ebx,ebx                  ; 源序号 i 从 0 起
        // 6F7A63  mov eax,[esi] / test / jle   ; <=0 的桶跳过
        // 6F7A69  push 0x3F800000              ; 1.0f，第 4 个（栈）参数
        // 6F7A74  call sub_6C0318              ; award(值, i, 1.0f)
        // 6F7A7D  cmp bl,5 / jne               ; i = 0..4
        //
        // 桶[0]/[3]/[4] 全程无写者 => 恒 0 => 只有 [1](基数) 和 [2](加成) 会发出，
        // 源序号因此恒为 1 和 2。栈上的 1.0f 在被调用方未被读取（sub_6C0318
        // 只用 eax/edx/cl 和三个字节栈参），是死参数，故此处不带。
        // ------------------------------------------------------------------

        /// <summary>桶的个数，0x6F7A7D <c>cmp bl,5</c>。</summary>
        private const int NativeWinExpBucketCount = 5;

        /// <summary>基数桶下标（0x6F7A34 写 [ebp-0x10]，即桶 1）。</summary>
        private const int NativeWinExpBaseBucket = 1;

        /// <summary>加成桶下标（0x6F7A40 写 [ebp-0xc]，即桶 2）。</summary>
        private const int NativeWinExpBonusBucket = 2;

        private void WinExp(int dwExp)
        {
            var buckets = new int[NativeWinExpBucketCount];   // FillChar 清零
            buckets[NativeWinExpBaseBucket] = dwExp;

            // 两个加成相加后再减去一份基数：加成助手各自返回的都是「含基数的
            // 总额」，桶[1] 已经带了一份，故 0x6F7A54 把重复的那份减掉。
            var bonus = unchecked(NativeExpBuffBonus(dwExp) + NativeMultiTempExpRateBonus(dwExp));
            // 0x6F7A4F 比较方向：dwExp > bonus 时整桶作废（而不是夹到 0）。
            bonus = dwExp > bonus ? 0 : unchecked(bonus - dwExp);
            buckets[NativeWinExpBonusBucket] = bonus;

            for (var i = 0; i < NativeWinExpBucketCount; i++)
            {
                var value = buckets[i];
                if (value <= 0) continue;                    // 0x6F7A65 test/jle
                NativeAwardExperience(value, i);             // 0x6F7A74
            }
        }

        // ------------------------------------------------------------------
        // sub_6F7A8C —— Nx 倍经验加成。eax=Self, edx=dwExp -> eax
        //   6F7A8C  or  ecx,0xFFFFFFFF          ; 默认 -1，不是 0
        //   6F7A8F  cmp dword [eax+0xBB8],0     ; 剩余秒数
        //   6F7A96  jle 0x6F7AA1                ; 未生效 -> 返回 -1
        //   6F7A98  mov ecx,dword [eax+0xBBC]   ; 倍数 N
        //   6F7A9E  imul ecx,edx                ; 32 位整数乘，会环绕
        //
        // 默认值 -1 是哨兵而非差一错误：未生效时 bonus = -1 + 0*dwExp = -1，
        // 于是 0x6F7A4F 的 `dwExp > -1` 成立，加成桶被整桶作废。若这里返回 0，
        // dwExp == bonus 时会走 `bonus -= dwExp` 分支得 0 —— 结果同为不发，
        // 但 dwExp == 0 时行为会分叉，故仍按 -1 忠实实现。
        // ------------------------------------------------------------------
        private int NativeExpBuffBonus(int dwExp)
            => m_nNativeExpBuffSeconds > 0
                ? unchecked(m_nNativeExpBuffMultiplier * dwExp)
                : -1;

        // ------------------------------------------------------------------
        // sub_6F7AA4 —— MultiTempExpRate 加成。eax=Self, edx=dwExp -> eax
        //   6F7AA4  mov eax,dword [eax+0xBC0]   ; RTTI 'MultiTempExpRate'(Integer)
        //   6F7AAA  imul edx                    ; 单操作数：EDX:EAX，取低 32 位
        // 无除法、无 /100、无下限 1 —— 构造函数把它初始化为 0（0x6ADA18），
        // 所以「0」是合法的原生默认值。
        // ------------------------------------------------------------------
        private int NativeMultiTempExpRateBonus(int dwExp)
            => unchecked(m_nNativeMultiTempExpRate * dwExp);

        /// <summary>
        /// obj+0xBC0，RTTI 已发布属性 <c>MultiTempExpRate</c>（Integer，
        /// propinfo @0x6AD5EC，Get=Set=<c>FF000BC0</c>）。全镜像只有两处引用：
        /// 构造函数 0x6ADA18 写 0，sub_6F7AA4 @0x6F7AA4 读。
        /// </summary>
        public int m_nNativeMultiTempExpRate;

        // ------------------------------------------------------------------
        // §2  sub_6C037C —— 主人/英雄按等级比例分割
        //
        // eax=Self(主人), edx=dwExp, ecx=out 指针（英雄那份）
        //   6C038D  mov eax,[ebx+0xBB0] / movzx eax,word [eax+0x278]  ; HL
        //   6C039C  add esi,0xA                                       ; HL+10
        //   6C039F  movzx edx,word [ebx+0x278] ; ML
        //   6C03A6  cmp esi,edx / 7E 02 jle / mov esi,edx             ; esi=MIN(HL+10,ML)
        //   6C03AC  movzx edi,word [ebx+0x278] / add edi,eax          ; edi=ML+HL
        //   6C03B8  fild esi / fild edi / fdivp st(1)                 ; esi/edi
        //   6C03C3  fild dwExp / fmulp st(1) / call sub_403574        ; Round(*dwExp)
        //   6C03D0  mov [edx],eax                                     ; -> 英雄那份
        //   6C03D2  movzx eax,word [ebx+0x278] ...                    ; 同式换成 ML
        //   6C03EC  call sub_403574 / ret                             ; -> 主人那份
        //
        // 两式共用分母 ML+HL。先做除法再乘 dwExp（x87 的顺序），不能改写成
        // 先乘后除 —— 那会改变舍入结果。sub_403574 = Delphi Round = 银行家舍入。
        //
        // 分母为 0 只在 ML==HL==0 时出现，而活英雄的等级至少 1，故不可达；
        // 战神在此也没有守卫（x87 会抛 EInvalidOp 交给 sub_6C0148 的 SEH 帧），
        // 这里同样不加守卫以保持一致。
        // ------------------------------------------------------------------
        private int NativeSplitExperienceWithHero(int dwExp, HeroObject hero,
            out int heroShare)
        {
            int heroLevel = hero.m_Abil.Level;
            int masterLevel = m_Abil.Level;

            var heroTerm = heroLevel + 10;                       // 0x6C039C
            if (heroTerm > masterLevel) heroTerm = masterLevel;  // 0x6C03A6
            var sumLevels = masterLevel + heroLevel;             // 0x6C03AC

            heroShare = HUtil32.Round((double)heroTerm / sumLevels * dwExp);
            return HUtil32.Round((double)masterLevel / sumLevels * dwExp);
        }

        // ------------------------------------------------------------------
        // §3  sub_6C0318 —— award(值, 源序号)
        //
        //   6C0321  mov [ebp-1],cl              ; 源序号（字节）
        //   6C0328  mov edi,[ebx+0xBB0]         ; 英雄
        //   6C032E  test edi,edi / je 0x6C035D  ; 无英雄 -> 不分割
        //   6C0334  call sub_772DA8             ; = byte [hero+0x74] = m_boDeath
        //   6C033B  test al,al / jne 0x6C035D   ; 英雄已死 -> 不分割
        //   6C0344  call sub_6C037C             ; 分割
        //   6C0349  mov esi,eax                 ; 主人那份**覆盖**原额
        //   6C034B  push 0 / mov cl,1           ; a4=0(directMode), cl=1(算战斗经验)
        //   6C034F  mov edx,[ebp-8]             ; 英雄那份
        //   6C0358  call sub_687714             ; 英雄落账
        //   6C035D  push 1 / push 1 / push eax  ; [ebp+0x10]=1, [ebp+0xC]=1, [ebp+8]=源序号
        //   6C0365  xor ecx,ecx                 ; cl=0 -> **关掉** 8~12% 英雄红利
        //   6C036B  call sub_6C03F8             ; 落账/升级
        //
        // 关键点（决定了不能把两条英雄给经验的路混为一谈）：击杀路 cl=0，
        // 所以 sub_6C03F8 里 0x6C0482 的 8~12% 随机红利在**这条路上不执行**；
        // 英雄拿到的是 §2 的比例分割。两者互斥。
        //
        // 参数映射到已有的 C# 落账器：
        //   cl            -> shareWithHero          (击杀路 = false)
        //   [ebp+0x10]    -> countAsFightExperience (= true)
        //   [ebp+8]       -> experienceMode          (= 源序号)
        //   [ebp+0xC]     -> 死参数，函数体内无引用
        // ------------------------------------------------------------------
        private void NativeAwardExperience(int value, int sourceIndex)
        {
            var hero = m_HeroObject;                             // 0x6C0328
            if (hero != null && !hero.m_boDeath)                 // 0x6C032E / 0x6C0334
            {
                value = NativeSplitExperienceWithHero(value, hero, out var heroShare);
                GrantNativeHeroExperience(hero, heroShare,
                    countAsFightExperience: true, directMode: false);   // 0x6C0358
            }

            GrantNativePlayerExperience(value, shareWithHero: false,
                countAsFightExperience: true, experienceMode: sourceIndex);  // 0x6C036B
        }

        // ------------------------------------------------------------------
        // §4  从 WinExp 中删除的五个非原生缩放
        //
        // 每一项都做过「阳性对照」式的整镜像字符串搜索：对照串
        // PKPunishPoint / SafeZoneHPRecover / LFMultiple / MonExpRate 均命中
        // 并能定位到读取该键的函数，而下列键**零命中**，故属证实性缺席，
        // 不是搜索方法失灵。
        //
        //   1) g_Config.nLimitExpLevel / nLimitExpValue 提前返回
        //      —— 'LimitExpLevel'、'LimitExpValue' 零命中；战神没有任何
        //      「高等级只给固定经验」的机制。原 C# 默认值 1000/1 还会在
        //      等级 >1000 时把经验砍成 1。
        //   2) g_Config.dwKillMonExpMultiple 全局倍数
        //      —— 'KillMonExpMultiple'、'ExpMultiple'、'KillMonExp' 零命中。
        //      唯一存在的全局经验倍率控制是 GM 命令 MonExpRate（记录
        //      @0x7D0614），而它的 body 是一句作废通知
        //      （@0x62DC6C『由配置文件调整，此指令暂时作废』），不改任何字段。
        //   3) m_nKillMonExpMultiple 每角色整数倍数 —— 无对应字段。真正的
        //      每角色倍数是 +0xBBC，且必须配 +0xBB8 计时器（见 §1 第一个助手）。
        //   4) m_nKillMonExpRate / 100 —— 战神没有任何 /100 形态的每角色经验
        //      倍率。形态最近的 +0xBC0 是**纯整数乘、无除法**（§1 第二个助手）。
        //   5) 地图旗标 EXPRATE —— 'EXPRATE' 零命中；且地图对象只能经
        //      [actor+0x128] 抵达，而经验链九个函数
        //      （sub_6C02A4/728124/6C0318/6C037C/6C03F8/6F79E8/6F7A18/6F7A8C/
        //      6F7AA4）对 [+0x128] 的读取次数全部为 0。对照：地图旗标确实抵达
        //      死亡/掉落/PK 路（FIGHT [env+0x5D] @0x6C07F4、FREEPK [env+0x5F]
        //      @0x6C0830、掉落策略 sub_741368），所以经验链里的「零」是有意义
        //      的结论而非扫描假象。
        //   6) m_boExpItem / m_rExpItem 浮点道具加成 —— +0xBC4(int)/+0xBC8(float)
        //      确实存在，但整镜像只被 sub_788CA8（讨伐令 TTaoFaLingAddExpItem.Use）
        //      引用（各 3/4 处，全在该函数内），经验链根本不读它们。
        // ------------------------------------------------------------------
    }
}
