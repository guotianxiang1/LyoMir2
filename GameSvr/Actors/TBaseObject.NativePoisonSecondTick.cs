namespace GameSvr
{
    public partial class TBaseObject
    {
        // ============================================================================
        // POIS-38 — 战神 TCreature.Run (sub_76B6F0 = TCreature VMT+0x10) 的【中段】
        // 1000ms(1 秒)结算块,权威字节区间 0x76B905..0x76BD33(flat_image.bin,
        // ImageBase=0x400000)。等价账本 POIS-38 原判 BLOCKED/未勘,本文件按二进制补齐。
        //
        // 与尾段 2500ms 块(POIS-05..09,见 TBaseObject.NativePoisonTick.cs)的关键区别:
        //  * 2500ms 块是 if/else-if 链,一个 tick 只服务优先级最高的一档,伤害=Value+1;
        //  * 本 1000ms 块是【平铺的独立 if 段】,每个命中的状态都在同一 tick 各自结算,
        //    伤害=Value(不 +1)。
        //
        // 入口锁存 (0x76B905):
        //   76B905  8B45FC          mov eax,[ebp-4]          ; now = 本次 Run 的 tick 快照
        //   76B908  2B462C          sub eax,[esi+0x2C]       ; elapsed = now - 上次锁存
        //   76B90B  3DE8030000      cmp eax,0x3E8            ; 1000ms(硬编码常量,非配置)
        //   76B910  0F82.. jb 0x76BD33                       ; 反极性: elapsed(无符号) < 1000 跳过
        //   76B916  8B45FC/89462C   mov [esi+0x2C],now       ; 硬置位(丢弃余数)
        //
        // 本 tick 依次处理 11 个状态(全部经 HasState=sub_772960 门控;取节点=sub_773B98
        // FindNode,它内部先查 obj+0x168 位集再遍历 obj+0xDC 链表,节点 Value 在 rec+0x0A):
        //
        //  id    地址      动作                                                    广播
        //  0x39  76B91C   GetStateValue(0x39) -> [vmt+0x1AC]=sub_73F8E0 落地     无(管线内,param3=0 不播)
        //  0x47  76B942   HP>14000 ? [vmt+0x1AC]=sub_73F8E0(4000) : RemoveState  无
        //  0x49  76B97D   Value>0 ? DamageHealth(Value)                         color 0x38FF " -N"
        //  0x4A  76B9D8   同上                                                   color 0x38FF " -N"
        //  0x4F  76BA33   同上                                                   color 0x38FF "灼烧 -N"
        //  0x50  76BA8E   同上                                                   color 0x38FF "裂魂 -N"
        //  0x51  76BAE9   同上                                                   color 0x38FF "流血 -N"
        //  0x52  76BB44   同上                                                   color 0x38FF "惊魂 -N"
        //  0x5F  76BB9F   Value>0 ? DrainMP((MP*Value)/100)                     color 0x00FC "封魔 -N"
        //  0x56  76BC0B   0x54 宽限计数;耗尽后 DamageHealth(Value)              color 0x38FF "水元 -N"
        //  0x57  76BC9F   同 0x56(共用同一个 0x54 计数)                         color 0x38FF "火元 -N"
        //
        // 广播路径 sub_76CB44 -> [vmt+0xD8] = SendRefMsg。SendRefMsg 契约(sub_6DC590):
        //   76CB44 收 (eax=self, edx=value, ecx=color, [ebp+8]=已拼好的字符串);
        //   76CB66  test ebx,ebx / je       ; value==0 不发
        //   正值(76CB6E): push value/0/0/str/0 ; ecx=color ; dx=0x3002 ; call [vmt+0xD8]
        //   负值(76CB8B): neg value; push value/1/0/str/0 ; ...(本毒系块 value 恒>0)
        //   [vmt+0xD8]=sub_6DC590: dx->wIdent, cx->wParam, 栈参左到右 =
        //     nParam1=value, nParam2=符号位, nParam3=0, sMsg=str([ebp+0xC])。
        //   => C# SendRefMsg(wIdent=0x3002, wParam=color, nParam1=value, nParam2=sign, nParam3=0, sMsg)
        // 文本 = fmt + IntToStr(value):fmt 是 Delphi AnsiString 常量(GBK,已含尾部" -"),
        //   0x76BE54=" -" 0x76BE60="灼烧 -" 0x76BE70="裂魂 -" 0x76BE80="流血 -"
        //   0x76BE90="惊魂 -" 0x76BEA0="封魔 -" 0x76BEB0="水元 -" 0x76BEC0="火元 -";
        //   0x40C89C=IntToStr(有符号十进制),0x40581C=LStrCat3(dest,fmt,valueStr)。
        //
        // ✅ 0x39 与 0x47 的伤害分支走 [vmt+0x1AC]=sub_73F8E0——一条独立的物理落地管线,现已
        //   移植到 TBaseObject.NativeArmorLanding.cs 的 ApplyNativePhysicalLandingDamage(int)。
        //   两个调用点均传 param3=0/param4=0,由此可证:护甲 [vmt+0x50]=sub_744894 在
        //   0x7448B8 `test ebx,ebx / je 0x744C3F` 早退(retval/掷点全 0)、广播恒不触发、
        //   0x73F9C1 sub_767BA8 在 0x767BB7 `test edx,edx / je` 原样返回。管线实际生效的是
        //   ① self+0x2DC 百分比减伤(其源装备扩展属性聚合子系统 C# 未移植 → self+0x2DC 恒 0,
        //   即当前不减伤,与 C# 其它伤害路径一致)与 ③ HasState(8) 随机缩放,末尾 DamageHealth。
        //   0x47 的 RemoveState(HP<=14000) 分支证据充分,保留。
        // ============================================================================

        /// <summary>native TCreature 对象 +0x2C —— 本 1000ms 毒系结算块的锁存 tick。</summary>
        private int m_dwNativePoisonSecondTick;

        /// <summary>0x76CB44 -> [vmt+0xD8] 广播的固定 wIdent: <c>mov dx,0x3002</c>。</summary>
        private const int NativePoisonSecondBroadcastIdent = 0x3002;

        /// <summary>伤害档广播颜色码 <c>mov ecx,0x38FF</c>(0x49/0x4A/0x4F/0x50/0x51/0x52/0x56/0x57)。</summary>
        private const int NativePoisonSecondDamageColor = 0x38FF;

        /// <summary>0x5F 封魔(扣蓝)档广播颜色码 <c>mov ecx,0xFC</c> @0x76BBFD。</summary>
        private const int NativePoisonSecondSealColor = 0xFC;

        /// <summary>0x56/0x57 共用的 0x54 宽限计数状态(FindNode(0x54) @0x76BC32 / 0x76BCC6)。</summary>
        private const byte NativePoisonSecondGraceState = 0x54;

        /// <summary>
        /// POIS-38 中段 1000ms 毒系结算块(0x76B905..0x76BD33)。须在尾段 2500ms 块
        /// (TBaseObject.NativePoisonTick.cs 的 TryResolveNativePoisonTickDamage)之前调用,
        /// 与原生同用一份 Run tick 快照。接线由主代理并入 Run。
        /// </summary>
        public void ProcessNativePoisonSecondTick(int dwCurrentTick)
        {
            // 0x76B90B cmp 0x3E8 / 0x76B910 jb: 无符号 elapsed < 1000 直接跳过整块。
            if (unchecked((uint)(dwCurrentTick - m_dwNativePoisonSecondTick)) < 0x3E8u)
            {
                return;
            }
            // 0x76B919 mov [esi+0x2C],eax: 硬置位锁存(丢弃余数)。
            m_dwNativePoisonSecondTick = dwCurrentTick;

            ApplyNativePoisonSecondArmorLandingState(0x39);       // 0x76B91C
            ApplyNativePoisonSecondHighHpState(0x47);             // 0x76B942
            ApplyNativePoisonSecondDamageState(0x49, " -");       // 0x76B97D  fmt 0x76BE54
            ApplyNativePoisonSecondDamageState(0x4A, " -");       // 0x76B9D8  fmt 0x76BE54
            ApplyNativePoisonSecondDamageState(0x4F, "灼烧 -");   // 0x76BA33  fmt 0x76BE60
            ApplyNativePoisonSecondDamageState(0x50, "裂魂 -");   // 0x76BA8E  fmt 0x76BE70
            ApplyNativePoisonSecondDamageState(0x51, "流血 -");   // 0x76BAE9  fmt 0x76BE80
            ApplyNativePoisonSecondDamageState(0x52, "惊魂 -");   // 0x76BB44  fmt 0x76BE90
            ApplyNativePoisonSecondSealState(0x5F, "封魔 -");     // 0x76BB9F  fmt 0x76BEA0
            ApplyNativePoisonSecondGraceDamageState(0x56, "水元 -"); // 0x76BC0B  fmt 0x76BEB0
            ApplyNativePoisonSecondGraceDamageState(0x57, "火元 -"); // 0x76BC9F  fmt 0x76BEC0
        }

        /// <summary>
        /// 0x39 (0x76B91C): HasState 命中即 <c>GetStateValue(0x39)</c> 经 [vmt+0x1AC]=sub_73F8E0
        /// 物理落地管线结算(param3=0/param4=0)。管线已移植,见
        /// <c>TBaseObject.NativeArmorLanding.cs</c> 的 ApplyNativePhysicalLandingDamage。
        /// </summary>
        private void ApplyNativePoisonSecondArmorLandingState(byte stateId)
        {
            if (!HasNativeActiveState(stateId))
            {
                return;
            }
            // 0x76B929 push 0(param4) / 0x76B92F call sub_773BEC(GetStateValue) -> edx
            // 0x76B936 xor ecx,ecx(param3) / 0x76B93C call [vmt+0x1AC](self, value, 0, 0)
            var value = GetNativeTimedAbilityValue(stateId); // sub_773BEC(0x39)
            ApplyNativePhysicalLandingDamage(value);
        }

        /// <summary>
        /// 0x47 (0x76B942): edi=0xFA0(4000);阈值=edi+0x2710=14000。
        ///   0x76B95A cmp 14000,HP / 0x76B960 jge: HP&lt;=14000 -&gt; RemoveState(0x47);
        ///   否则 [vmt+0x1AC](self,4000) 经 sub_73F8E0 物理落地管线结算(param3=0/param4=0)。无广播。
        /// </summary>
        private void ApplyNativePoisonSecondHighHpState(byte stateId)
        {
            if (!HasNativeActiveState(stateId))
            {
                return;
            }
            // 0x76B95A cmp eax(14000),[esi+0x2AC](HP) / 0x76B960 jge -> 移除档
            if (m_WAbil.HP <= 14000)
            {
                // 0x76B974 mov dl,0x47 / call sub_7731C0 RemoveState(0x47)
                RemoveTimedAbilityInternal(stateId);
                return;
            }
            // 0x76B962 push 0(param4) / 0x76B964 xor ecx,ecx(param3) / 0x76B966 edx=edi=0xFA0(4000)
            // 0x76B96C call [vmt+0x1AC](self, 4000, 0, 0) —— 见 TBaseObject.NativeArmorLanding.cs
            ApplyNativePhysicalLandingDamage(0xFA0);
        }

        /// <summary>
        /// 0x49/0x4A/0x4F/0x50/0x51/0x52 共形(如 0x49 @0x76B97D):
        ///   HasState + FindNode; edi=rec.Value; test/jle 跳过非正;
        ///   0x76B9A6 call [vmt+0x1B0] = DamageHealth(Value); 随后广播 color 0x38FF。
        /// </summary>
        private void ApplyNativePoisonSecondDamageState(byte stateId, string label)
        {
            // sub_773B98(FindNode) 内部先 HasState 再遍历 = C# FindTimedAbilityInternal。
            var node = FindTimedAbilityInternal(stateId);
            if (node == null)
            {
                return;
            }
            var value = node.Value;   // mov edi,[rec+0x0A]
            if (value <= 0)           // test edi,edi / jle
            {
                return;
            }
            DamageHealth(value);      // call [vmt+0x1B0]
            BroadcastNativePoisonSecond(NativePoisonSecondDamageColor, label, value);
        }

        /// <summary>
        /// 0x5F 封魔 (0x76BB9F): HasState + FindNode; edi=rec.Value; test/jle 跳过非正;
        ///   0x76BBC2 amount=(MP*Value)/100(imul 后 cdq 丢弃高位 -&gt; 低 32 位截断 / idiv 100);
        ///   0x76BBD4 call sub_769E48(edx=0 不扣血, ecx=amount 扣蓝); 广播 color 0xFC。
        /// </summary>
        private void ApplyNativePoisonSecondSealState(byte stateId, string label)
        {
            var node = FindTimedAbilityInternal(stateId);
            if (node == null)
            {
                return;
            }
            var value = node.Value;   // 0x76BBBB mov edi,[rec+0x0A]
            if (value <= 0)           // 0x76BBBE test edi,edi / jle
            {
                return;
            }
            // 0x76BBC2 mov eax,[esi+0x2B4](MP) / imul edi / mov ecx,0x64 / cdq / idiv ecx
            var amount = unchecked(m_WAbil.MP * value) / 100;
            // 0x76BBD4 mov ecx,edi(amount) / xor edx,edx / call sub_769E48
            DrainNativeHealthMp(0, amount);
            // 0x76BBDF..0x76BC06 IntToStr(amount) + fmt + SendRefMsg(color 0xFC)
            BroadcastNativePoisonSecond(NativePoisonSecondSealColor, label, amount);
        }

        /// <summary>
        /// 0x56 水元 (0x76BC0B) / 0x57 火元 (0x76BC9F) 共形,共用 0x54 宽限计数:
        ///   HasState + FindNode(id); edi=rec.Value; test/jle 跳过非正;
        ///   FindNode(0x54)=g; g!=nil && g.Value&gt;0 时: g.Value--; 仍&gt;0 则本 tick 吸收(不打),
        ///     减到 0 则 RemoveState(0x54) 且本 tick 仍不打;
        ///   g 为 nil 或 g.Value&lt;=0 时才 DamageHealth(Value) 并广播 color 0x38FF。
        /// 注:0x56 先于 0x57 处理,两者各自对 0x54 递减一次(与原生逐段 dec 一致)。
        /// </summary>
        private void ApplyNativePoisonSecondGraceDamageState(byte stateId, string label)
        {
            var node = FindTimedAbilityInternal(stateId);
            if (node == null)
            {
                return;
            }
            var dmg = node.Value;     // mov edi,[rec+0x0A]
            if (dmg <= 0)             // test edi,edi / jle
            {
                return;
            }
            // 0x76BC32 mov dl,0x54 / call sub_773B98 FindNode(0x54)
            var grace = FindTimedAbilityInternal(NativePoisonSecondGraceState);
            // 0x76BC3E cmp [ebp-0xC],0 / je 打伤害 ; 0x76BC47 cmp [g+0x0A],0 / jle 打伤害
            if (grace != null && grace.Value > 0)
            {
                grace.Value--;        // 0x76BC50 dec [g+0x0A]
                if (grace.Value > 0)  // 0x76BC56 cmp [g+0x0A],0 / jg 跳过
                {
                    return;
                }
                // 0x76BC5C mov dl,0x54 / call sub_7731C0 RemoveState(0x54); 0x76BC65 jmp 跳过
                RemoveTimedAbilityInternal(NativePoisonSecondGraceState);
                return;
            }
            DamageHealth(dmg);        // 0x76BC6D call [vmt+0x1B0]
            BroadcastNativePoisonSecond(NativePoisonSecondDamageColor, label, dmg);
        }

        /// <summary>
        /// 战神 sub_769E48 @0x769E48 —— 扣血(edx)/扣蓝(ecx),各自下限 0,不派发死亡。
        ///   769E4C test edx,edx / jl ret     ; hpLoss&lt;0 直接返回
        ///   769E50 test ecx,ecx / jl ret     ; mpLoss&lt;0 直接返回
        ///   769E54 ebx=HP-edx; 769E5C test/jle: &gt;0 则 HP-=edx 否则 HP=0
        ///   769E70 edx=MP-ecx; 769E78 test/jle: &gt;0 则 MP-=ecx 否则 MP=0
        ///   769E8C call sub_7693E8          ; mov byte [self+0x99],1 脏位(= m_boNativeHealthSpellDirty)
        /// </summary>
        private void DrainNativeHealthMp(int hpLoss, int mpLoss)
        {
            if (hpLoss < 0 || mpLoss < 0)
            {
                return;
            }
            m_WAbil.HP = unchecked(m_WAbil.HP - hpLoss) > 0
                ? unchecked(m_WAbil.HP - hpLoss)
                : 0;
            m_WAbil.MP = unchecked(m_WAbil.MP - mpLoss) > 0
                ? unchecked(m_WAbil.MP - mpLoss)
                : 0;
            // sub_7693E8: 仅置 +0x99 脏位,由 500ms 玩家循环回刷(不立即 HealthSpellChanged)。
            m_boNativeHealthSpellDirty = true;
        }

        /// <summary>
        /// sub_76CB44 -&gt; [vmt+0xD8]=SendRefMsg 的浮字广播。见文件头广播契约。
        /// value==0 不发;正值 nParam1=value/nParam2=0,负值 nParam1=-value/nParam2=1。
        /// sMsg = fmt(含尾部" -") + IntToStr(value)。
        /// </summary>
        private void BroadcastNativePoisonSecond(int color, string label, int value)
        {
            // 0x76CB66 test ebx,ebx / je: value==0 不发。
            if (value == 0)
            {
                return;
            }
            var magnitude = value > 0 ? value : unchecked(-value);
            var signFlag = value > 0 ? 0 : 1;
            SendRefMsg(NativePoisonSecondBroadcastIdent, color, magnitude, signFlag, 0,
                label + value);
        }
    }
}
