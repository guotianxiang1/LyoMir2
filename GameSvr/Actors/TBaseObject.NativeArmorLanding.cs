namespace GameSvr
{
    public partial class TBaseObject
    {
        // ============================================================================
        // 战神 sub_73F8E0 (VMT+0x1AC) —— 物理落地伤害管线,权威字节 0x73F8E0..0x73F9E9
        // (flat_image.bin, ImageBase=0x400000, file_offset = VA-0x400000)。全部 10 个把
        // sub_73F8E0 放在 +0x1AC 的 VMT(0x5F55A8/0x5F58E4/0x5F5C24/0x62EF8C/0x685630/
        // 0x685968/0x685CA0/0x685FD8/0x6AC8C8/0x73BC34)都统一使用 [vmt+0x50]=sub_744894、
        // [vmt+0x1B0]=sub_767D14(=DamageHealth=ApplyStandardEarthFireLanding),所以护甲/
        // 落地入口不因对象类型(玩家/怪物/NPC)分叉 —— 无需按 VMT 取证,单实现即可。
        //
        // 【入参 / Delphi 寄存器约定】sub_73F8E0(eax=self, edx=damage, ecx=param3, [ebp+8]=param4)
        //   0x73F8EE mov [ebp-4],ecx   ; param3
        //   0x73F8F1 mov esi,edx        ; damage
        //   0x73F8F3 mov edi,eax        ; self
        //   [ebp-0x10]=0(0x73F8EB,末尾 LStrClr 用的临时串)
        //
        // 【本任务的两个调用点都传 param3=0, param4=0】(TCreature 1000ms 毒系块,见
        //   TBaseObject.NativePoisonSecondTick.cs):
        //   0x39: 0x76B929 push 0(param4) / GetStateValue(0x39)->edx / 0x76B936 xor ecx,ecx(param3)
        //   0x47: 0x76B962 push 0(param4) / edx=0xFA0=4000 / 0x76B964 xor ecx,ecx(param3)
        // param3=0/param4=0 使管线中「护甲、护甲加成、广播、sub_767BA8 变换」四段全部
        // 可证塌缩(下逐条注明),故本方法忠实建模 sub_73F8E0(self, rawDamage, 0, 0)。
        //
        // 【逐阶段公式与取整】
        //  ① 百分比减伤 0x73F903..0x73F92C:
        //       0x73F903 mov cx,[edi+0x2DC]      ; pct = self+0x2DC (有符号 word)
        //       0x73F90A test cx,cx / 0x73F90D jle 0x73F92E   ; pct<=0 整段跳过
        //       0x73F90F movsx eax,cx / 0x73F912 imul esi     ; edx:eax = pct*damage
        //       0x73F914 mov ecx,0x64 / 0x73F919 cdq          ; ★cdq 覆盖 edx=符号扩展(eax)
        //       0x73F91A idiv ecx                             ;   => 低 32 位截断积 / 100(向零取整)
        //       0x73F91C test eax,eax / 0x73F91E jle 0x73F92E ; reduction<=0 跳过 sub
        //       0x73F920 cmp eax,0x4E20 / 0x73F925 jle / 0x73F927 mov eax,0x4E20 ; 上钳 20000
        //       0x73F92C sub esi,eax                          ; damage -= reduction
        //     self+0x2DC = 「百分比物理减伤总量」。它由 RecalcAbilitys(sub_73D500)在装备
        //     扩展属性聚合子系统(sub_75EE78,容器+0x48/+0x1F8)重算时累加而成,来源为
        //     0x73DEA8 的装备扩展属性 0xAA..0xAE 低字节之和、0x73DEB1 的
        //     m_btNativeDamageShare 零扩展，以及 0x73DEC7 的扩展属性 128/138 门控 +20。
        //     三个已证实输入现由 NativeRecalcPhysicalReductionPercent()重建。
        //
        //  ② 护甲掷点 0x73F92E..0x73F94C -> [vmt+0x50]=sub_744894:
        //       0x73F92E push esi(damage) / push &[ebp-8](dwordOut) / push &[ebp-0xA](wordOut)
        //       0x73F937 ecx=param4 / 0x73F93A edx=param3 / 0x73F93D eax=self / 0x73F941 call [vmt+0x50]
        //       0x73F944 mov ebx,eax(retval)
        //       0x73F946 cmp [ebp-8],0 / 0x73F94A jle / 0x73F94C add esi,[ebp-8]  ; dwordOut>0 才加
        //     sub_744894(eax=self,edx=param3,ecx=param4,[ebp+8]=&wordOut,[ebp+0xC]=&dwordOut,
        //     [ebp+0x10]=damage):开头 0x7448A6 [ebp-8]=0(默认返回)、0x7448AE *dwordOut=0、
        //     0x7448B3 *wordOut=0;随即 0x7448B8 `test ebx,ebx`(ebx=param3) / 0x7448BA
        //     `je 0x744C3F`。★param3=0 时直接跳出口 0x744C3F `mov eax,[ebp-8]`(=0),
        //     `ret 0xC`。=> retval=0、wordOut=0、dwordOut=0。故本管线里护甲对 damage 无加成、
        //     无掷点输出。(param3≠0 的完整护甲路径依赖 self+0x540/+0x544、全局 0x7D6830、
        //     表 0x7D3ED8/0x7D3EE8、sub_408340/sub_76CD8C/sub_4C700C 与 param3 的 +0x72/+0x128/
        //     +0x178/+0x278/+0x3A0/+0x3A2 字段,本 0x39/0x47 调用点不可达,未移植。)
        //
        //  ③ HasState(8) 随机缩放 0x73F94F..0x73F970:
        //       0x73F94F mov dl,8 / 0x73F951 eax=self / 0x73F953 call sub_772960(HasState)
        //       0x73F958 test al,al / 0x73F95A je 0x73F972
        //       0x73F95C mov eax,0x46(70) / 0x73F961 call sub_403B4C(Random) -> [0,69]
        //       0x73F966 imul esi / 0x73F968 mov ecx,0x64 / 0x73F96D cdq / 0x73F96E idiv ecx
        //       0x73F970 mov esi,eax          ; damage = (Random(70)*damage)/100  ★替换,非累加
        //     Random(70)∈[0,69] => damage 变为原值的 0%..69%(观测行为为缩小;task 提示语
        //     「随机放大」与字节不符,以字节为准)。sub_772960 = `bt [self+0x168],id`(id≤0x6F)
        //     = C# HasNativeActiveState。sub_403B4C = Delphi 有界 LCG
        //     (seed=seed*0x08088405+1; result=high32(bound*seed)),C# = M2Share.RandomNumber.Random。
        //
        //  ④ 下限钳 0x73F972..0x73F976:test esi,esi / jge / xor esi,esi => if(damage<0)damage=0
        //     (无论 ③ 是否命中都执行,两路在 0x73F972 汇合)。
        //
        //  ⑤ 护甲加成回加 0x73F978..0x73F97C:test ebx,ebx / jle / add esi,ebx
        //     ebx=retval=0(见②)=> 不加。
        //
        //  ⑥ 广播 0x73F97E..0x73F9B5:
        //       0x73F97E eax=[ebp-8]+ebx(=dwordOut+retval) / 0x73F983 test/jle 0x73F9BA  ; <=0 不播
        //       0x73F987 cmp word[ebp-0xA],5 / 0x73F98C jbe 0x73F9BA                       ; wordOut<=5 不播
        //     本管线 dwordOut+retval=0 且 wordOut=0 => 广播恒不触发(与毒系块表「0x39/0x47 无广播」一致)。
        //     (真播时值=-(param4+dwordOut+retval),色 0x38FF,文本=IntToStr(同值),经 sub_76CB44->[vmt+0xD8]。)
        //
        //  ⑦ 落地前变换 0x73F9BA..0x73F9C6 -> sub_767BA8:
        //       0x73F9BA ecx=damage / 0x73F9BC edx=param3 / 0x73F9BF eax=self / 0x73F9C1 call sub_767BA8
        //       0x73F9C6 mov esi,eax
        //     sub_767BA8(eax=self,edx=param3,ecx=damage):0x767BB7 `test edx,edx` / 0x767BB9
        //     `je 0x767C9A`。★param3=0 时原样返回 damage(0x767BB4 [ebp-4]=damage 默认返回)。
        //     (param3≠0 的浮点变换读 param3+0x194/+0x198、self+0x19C/+0x1A0,含 Random(10000) 概率
        //     门与常数 100/10000/1.5/5e-05,本调用点不可达,未移植。)
        //
        //  ⑧ 落地 0x73F9CC..0x73F9CE:mov ecx,[eax] / call [ecx+0x1B0] = DamageHealth(self,damage)。
        //     [vmt+0x1B0]=sub_767D14=C# DamageHealth(int)=ApplyStandardEarthFireLanding。
        //     函数尾 0x73F9D4 xor eax,eax:sub_73F8E0 自身返回 0(调用点均忽略返回值)。
        // ============================================================================

        /// <summary>0x73F920 `cmp eax,0x4E20` —— ① 百分比减伤额的上钳(20000)。</summary>
        private const int NativePhysicalLandingReductionCap = 0x4E20;

        /// <summary>0x73F95C `mov eax,0x46` —— ③ HasState(8) 命中时 Random 的上界(70,取值 [0,69])。</summary>
        private const int NativePhysicalLandingRandomBound = 0x46;

        /// <summary>0x73F94F `mov dl,8` —— ③ 触发随机缩放的状态 id(HasState(8))。</summary>
        private const int NativePhysicalLandingRandomState = 8;

        /// <summary>
        /// self+0x2DC(有符号 word)= 百分比物理减伤总量,管线阶段①的输入
        /// (0x73F903 `mov cx,[edi+0x2DC]`)。
        /// <para>
        /// 其填充在 RecalcAbilitys(sub_73D500)内,由 self+0x2DC 三条 add 累加而成:
        /// write#1 0x73DEA8 `add word[self+0x2DC],word[agg1+0x58]`(装备扩展属性聚合,
        /// 类型 0xAA..0xAE 的值低字节)、write#2 0x73DEB7 `add word[self+0x2DC],ax`
        /// (ax=m_btNativeDamageShare 零扩展,活字段)、write#3 0x73DEC7
        /// `add word[self+0x2DC],0x14`([self+0x1D5]=NativeDropRareKillerBonusGate 门控,
        /// 由装备扩展属性 128/138 置位)。种子经 sub_73D3E4(base[+0x260]=0)复位为 0。完整逐字节见
        /// <c>TBaseObject.NativePhysicalPercentReduction.cs</c>。
        /// </para>
        /// <para>
        /// 现已作为真实字段 <see cref="m_wNativePhysicalDamageReductionPercent"/> 落地:
        /// write#1 消费已解析的装备扩展属性 170..174，write#2 消费
        /// m_btNativeDamageShare(GM 359 @ChgDmgShare / 持久化)，write#3 复用
        /// 已验证的装备扩展属性 128/138 门。
        /// </para>
        /// </summary>
        private int NativePhysicalPercentDamageReduction() => m_wNativePhysicalDamageReductionPercent;

        /// <summary>
        /// 战神 sub_73F8E0(VMT+0x1AC)物理落地伤害管线,建模为 param3=0/param4=0 形态
        /// (0x39/0x47 两个调用点的唯一入参形态,见文件头逐阶段字节)。忠实复刻:
        /// 阶段① 百分比减伤(self+0x2DC)→ 阶段② 护甲(param3=0 证明塌缩,
        /// 无加成/无广播)→ 阶段③ HasState(8) 随机缩放 → 下限钳 → 阶段⑦ sub_767BA8(param3=0
        /// 原样返回)→ 阶段⑧ DamageHealth。返回喂给 DamageHealth 的最终落地伤害
        /// (native 自身返回 0/被忽略,此处返回值仅便于核对)。
        /// </summary>
        /// <param name="rawDamage">edx 入参:落地伤害基值(0x39=GetStateValue(0x39);0x47=4000)。</param>
        public int ApplyNativePhysicalLandingDamage(int rawDamage)
        {
            var damage = rawDamage;

            // ① 0x73F903..0x73F92C 百分比减伤(self+0x2DC)。
            // 0x73F903 mov cx,[edi+0x2DC] / 0x73F90F movsx eax,cx:字段是 word,以有符号 16 位读入。
            var pct = (short)NativePhysicalPercentDamageReduction();
            if (pct > 0) // 0x73F90A test cx,cx / 0x73F90D jle(有符号 16 位比较)
            {
                // 0x73F912 imul esi / 0x73F919 cdq / 0x73F91A idiv 100:低 32 位截断积 / 100(向零)。
                var reduction = unchecked(pct * damage) / 100;
                if (reduction > 0) // 0x73F91C test eax,eax / 0x73F91E jle
                {
                    if (reduction > NativePhysicalLandingReductionCap) // 0x73F920 cmp 0x4E20 / jle
                    {
                        reduction = NativePhysicalLandingReductionCap; // 0x73F927 mov eax,0x4E20
                    }
                    damage -= reduction; // 0x73F92C sub esi,eax
                }
            }

            // ② 0x73F92E..0x73F94C 护甲 [vmt+0x50]=sub_744894。param3=0 => 0x7448B8 test ebx/je 0x744C3F
            //    早退:retval=0、dwordOut=0、wordOut=0。故 dwordOut 回加(0x73F94C)为 0,跳过。

            // ③ 0x73F94F..0x73F970 HasState(8) 随机缩放(替换,非累加)。
            if (HasNativeActiveState(NativePhysicalLandingRandomState)) // 0x73F953 sub_772960
            {
                // 0x73F961 Random(70)∈[0,69] / 0x73F966 imul / cdq / idiv 100:(rand*damage)/100(向零)。
                damage = unchecked(M2Share.RandomNumber.Random(NativePhysicalLandingRandomBound) * damage) / 100;
            }

            // ④ 0x73F972..0x73F976 下限钳(两路汇合,恒执行)。
            if (damage < 0)
            {
                damage = 0;
            }

            // ⑤ 0x73F978..0x73F97C 护甲加成回加:ebx=retval=0 => 跳过。
            // ⑥ 0x73F97E..0x73F9B5 广播:dwordOut+retval=0 且 wordOut=0 => 恒不触发。
            // ⑦ 0x73F9BA..0x73F9C6 sub_767BA8:param3=0 => 0x767BB7 test edx/je 原样返回 damage。

            // ⑧ 0x73F9CE call [vmt+0x1B0] = DamageHealth = sub_767D14 = ApplyStandardEarthFireLanding。
            return DamageHealth(damage);
        }
    }
}
