using SystemModule;

namespace GameSvr
{
    // ------------------------------------------------------------------------------------------
    // 死亡爆装的分母，逐字节。
    //
    // 装备 worker sub_73FC70 @0x73FCA9-0x73FD0D 把分母 K 算出来后，循环里每个够格的
    // 装备格调用一次 Random(K)：
    //
    //   73FCA9  A1 AC 5F 7D 00 / 8B 00          eax := [[0x7D5FAC]] = 200
    //   73FCB0  3B 86 60 01 00 00               cmp eax,[self+0x160]      ; 阈值 vs MyPKpoint
    //   73FCB6  7D 09                           jge 0x73FCC1              ; 阈值 >= PK -> 非红名
    //   73FCB8  C7 45 F8 15 00 00 00            K := 0x15 = 21            ; 红名（严格 PK > 200）
    //   73FCBF  EB 0C                           jmp 0x73FCCD
    //   73FCC1  8B 86 8C 01 00 00               eax := [self+0x18C]
    //   73FCC7  83 C0 5A                        eax += 0x5A = 90
    //   73FCCA  89 45 F8                        K := eax                  ; 非红名
    //   73FCEF  （LastHiter is THumanKind 时）
    //   73FD02  8A 83 79 05 00 00               al := byte [LastHiter+0x579]
    //   73FD08  2B 45 F8 …                      K -= al
    //   73FD0B  83 7D F8 00 / 7D 04 / 33 C0     K < 0 -> K := 0
    //   73FD99  E8 AE 3D CC FF                  call sub_403B4C = Random(K)
    //
    // 两个输入以前都是 BLOCKED。这一轮把它们追到底了。
    //
    // ── [self+0x18C] ──────────────────────────────────────────────────────────────────
    // 全镜像只有一个写入点和两个读取点：
    //
    //   写  0x73DAC5  89 86 8C 01 00 00   mov [esi+0x18C],eax     （在 sub_73D500 重算里）
    //   读  0x73FCC1                      非红名分母
    //   读  0x743E18  66 8B 83 8C 01 00 00 mov ax,[ebx+0x18C]     （sub_743C50 打包成
    //                                      0xB8 字节记录的 +0x8C，四个调用者
    //                                      0x689727/0x68978F 英雄、0x6B4D2A/0x6B4D71 玩家）
    //
    // 写入点的算式：
    //   73DAB8  0F B7 47 5E     movzx eax, word [edi+0x5E]
    //   73DABC  B9 0A 00 00 00  mov ecx,0xA
    //   73DAC1  33 D2 / F7 F1   xor edx,edx / div ecx        ← **无符号**除，截断
    //   73DAC5  89 86 8C 01 00 00
    //
    // 其中 edi 是栈上 432 字节的累加器（0x73D52E `lea edi,[ebp-0x1B8]`，
    // 0x73D554 FillChar 0x1B0），它是装备容器聚合块的一份拷贝：
    //   73D621  8B 86 C0 04 00 00   eax := [self+0x4C0]      ; 装备容器
    //   73D629  8D 70 48            lea esi,[eax+0x48]
    //   73D62C  B9 6C 00 00 00      mov ecx,0x6C = 108 dwords = 0x1B0 字节
    //   73D631  F3 A5               rep movsd
    // 所以 [self+0x18C] = word[装备容器 + 0x48 + 0x5E] / 10 = word[容器+0xA6] / 10。
    //
    // 容器布局（由 sub_75F3E8 0x75F40F `mov [esi+eax*4+8],0` 与上面的 lea 推出）：
    //   +0x00 头 8 字节 | +0x08..+0x47 十六个 TUserItem 指针 | +0x48 起 0x1B0 聚合块
    //   | +0x1F8 起 0x36 字节副块（0x73D63D 那次 rep movsd 的目的地是对象 +0x1B0）
    //
    // 聚合块由 sub_75EE78 重建：先 sub_75F4F8 清零，再对 16 个格子里
    // sub_7845A0(item) > 0 的调用 sub_75EE04，后者以 edx = 容器+0x48 调
    // [item.vmt+0x5C] 与 sub_75FE20。
    //
    // 聚合块 +0x5E 的唯一喂养者是装备扩展属性分发的一条臂：
    //   7620DA  8A 43 11              al := byte [std+0x11]         ; 属性类型
    //   7620DD  83 C0 C7              eax += -0x39                  ; 偏置 57
    //   7620E0  3D 97 00 00 00 / 0F 87  cmp eax,0x97 / ja           ; 只认 57..208
    //   7620EB  8A 80 F8 20 76 00     al := byte [eax+0x7620F8]     ; 类型 -> 槽号表(152 项)
    //   7620F1  FF 24 85 90 21 76 00  jmp [eax*4+0x762190]          ; 槽号 -> 臂(33 条)
    //   槽 27 的臂 = 0x7623B0，表项在 0x7621FC：
    //   7623B0  33 C0 / 8A 43 13      al := byte [std+0x13]         ; 属性值
    //   7623B5  66 01 46 5E           add word [esi+0x5E], ax
    // 反查 152 项槽号表，落到槽 27 的**只有属性类型 201 (0xC9)**（邻居 202 落到 +0x60）。
    //
    // ⇒ [self+0x18C] = (身上所有装备的扩展属性 201 低字节值之和) / 10，无符号截断。
    //    分母 = 该值 + 90，所以属性 201 只会让装备**更不容易**掉，90 是地板。
    //
    // 【订正 PKD-20 的两处措辞】这条臂所在的函数是 **[item.vmt+0x54]**（TRing 的 VMT 基址
    // 0x75D8D0 —— SelfPtr 判据 u32(vmt-0x4C)==vmt，类名在 vmt-0x2C = 'TRing'；
    // 0x75D924 - 0x75D8D0 = 0x54），不是 +0x5C。链路是
    //   sub_75EE78 -> sub_75EE04 -> [item.vmt+0x5C]=sub_75F728 -> [item.vmt+0x54]=sub_76203C
    // 而 **agg1/agg2 在 sub_75F728 里被换了位置**，这一步不看会把结论读反：
    //   75EE2A  8D 8B F8 01 00 00   lea ecx,[container+0x1F8]   ; agg2 进 ecx
    //   75EE30  8D 53 48            lea edx,[container+0x48]    ; agg1 进 edx
    //   75F731  8B F9 / 8B F2       sub_75F728: edi:=ecx(agg2) / esi:=edx(agg1)
    //   75F769  8B CE               ecx := esi = agg1           ; ← 换回来
    //   75F76B  8B D7               edx := edi = agg2
    //   75F771  FF 53 54            call [vmt+0x54]
    // 所以 sub_76203C 里 esi(=ecx)=agg1、[ebp-4](=edx)=agg2，`add word [esi+0x5E]` 确实落在
    // agg1 = 容器+0x48 上，PKD-20 的**结论**是对的。另外那条臂读的 `[ebx+0x11]/[ebx+0x13]`
    // 里的 ebx 是 [ebp+0xC]，即 sub_75F728 @0x75F74B 从 **StdItem+4 拷 0x3C 字节**的栈副本，
    // 所以属性类型/值的真实位置是 **StdItem+0x15 / StdItem+0x17**，不是某张独立属性表。
    //
    // ── [self+0x1D5]：PKD-20 说"全镜像没有写入点"，那是漏读 ─────────────────────────────
    // 它的写入点就在同一个 sub_73D500 里，在上面那次聚合拷贝的 12 条指令之后：
    //   73D542  8D 86 B0 01 00 00 / BA 36 00 00 00 / call 0x403B2C   FillChar(self+0x1B0, 0x36, 0)
    //   73D63D  8D BE B0 01 00 00   lea edi,[self+0x1B0]        ; 目的地
    //   73D643  8D B0 F8 01 00 00   lea esi,[container+0x1F8]   ; 源 = agg2
    //   73D649  B9 0D 00 00 00      mov ecx,0xD                 ; 13 dword
    //   73D64E  F3 A5 / 73D650 66 A5  rep movsd / movsw          ; 共 0x36 = 54 字节
    // 目的地区间 self+0x1B0..+0x1E6 **跨过 +0x1D5**（0x1D5-0x1B0 = 0x25），即
    //   [self+0x1D5] == agg2[0x25]
    // 而 agg2[0x25] 只有四个写入点，全是"置 1"，全在扩展属性分发的臂里：
    //   0x76231B  C6 40 25 01   mov byte [agg2+0x25],1   （sub_76203C 槽 19 = 属性类型 128）
    //   0x762372  C6 40 25 01                              （sub_76203C 槽 24 = 属性类型 138）
    //   0x762B26  C6 40 25 01   （兄弟分发器 sub_762974，形状完全一致）
    //   0x762B6A  C6 40 25 01   （同上，同时置 agg2[4]）
    // ⇒ [+0x1D5] **不是存档字段**，是"身上穿了带该扩展属性的装备"这一派生标记，
    //   每次 RecalcAbilitys 先清零再重算。它和 [+0x18C] 是同一个子系统的两个出口。
    //
    // ── [self+0x579] ──────────────────────────────────────────────────────────────────
    // 全镜像恰好三处引用，闭合：
    //   写  0x73D578  C6 86 79 05 00 00 00     mov byte [esi+0x579],0     重算开头清零
    //   写  0x73DECF  C6 86 79 05 00 00 0A     mov byte [esi+0x579],0xA
    //   读  0x73FD02  8A 83 79 05 00 00        mov al,[ebx+0x579]         分母减项
    // 写 10 的那处受一道门控，同一道门还给 [+0x2DC] 加 20：
    //   73DEBE  80 BE D5 01 00 00 00   cmp byte [self+0x1D5],0
    //   73DEC5  74 0F                  je 0x73DED6
    //   73DEC7  66 83 86 DC 02 00 00 14  add word [self+0x2DC],0x14
    //   73DECF  C6 86 79 05 00 00 0A     mov byte [self+0x579],0xA
    // [+0x2DC] 是百分比减伤（sub_73F8E0 @0x73F903 `mov cx,[edi+0x2DC]` / `jle` 跳过 /
    // `imul` / `idiv 100` / 上钳 0x4E20 / `sub esi,eax`，另一处 sub_746130 @0x746177）。
    //
    // ── C# 映射边界 ────────────────────────────────────────────────────────────────
    // 两个输入都出自装备扩展属性分发（0x7620DA 只取 StdItem+0x15 的低字节，
    // 类型 57..208 → 33 个臂 →
    // agg1/agg2）。当前 GoodItem 已保留原生六组扩展属性槽，因此本端可以精确消费
    // 已确认的三种类型：201 -> agg1+0x5E，128/138 -> agg2+0x25。
    // 未覆盖的其它扩展属性臂仍不参与本改动；这不是对整个装备扩展子系统的声明。
    // ------------------------------------------------------------------------------------------
    public partial class TBaseObject
    {
        /// <summary>
        /// [self+0x18C]。写于 0x73DAC5，读于 0x73FCC1（非红名分母）与 0x743E18（0xB8
        /// 字节记录的 +0x8C）。值 = 装备扩展属性 201 之和 / 10，无符号截断。
        /// </summary>
        public int m_nNativeDropRareBase;

        /// <summary>
        /// [self+0x579]。0 或 10，由 [self+0x1D5] 决定（0x73DEBE / 0x73DECF）。
        /// 它从**凶手**身上读走（0x73FD02 读的是 LastHiter），用来减小受害者的分母。
        /// </summary>
        public byte m_btNativeDropRareKillerBonus;

        /// <summary>
        /// word[装备容器+0x48+0x5E] —— 身上装备扩展属性类型 201 (0xC9) 的
        /// StdItem+0x17 低字节累加值。
        /// GoodItem 已保留原生六组扩展属性槽；这里只消费已解析的装备定义，
        /// 不推断其它扩展属性的效果。
        /// </summary>
        protected virtual int NativeEquipDropRareAggregate()
        {
            if (m_UseItems == null || M2Share.UserEngine == null)
            {
                return 0;
            }

            // Native sub_75EE78 admits the same positive-durability equipped
            // records that the RecalcAbilitys worker scans.  The native arm at
            // 0x7623B0 clears EAX, loads only AL from StdItem+0x17, then adds AX
            // into agg1+0x5E. Preserve both the byte contribution and word sum.
            ushort aggregate = 0;
            var count = Math.Min(m_UseItems.Length,
                Grobal2.HUMAN_EQUIPPED_ITEM_COUNT);
            for (var slot = 0; slot < count; slot++)
            {
                var userItem = m_UseItems[slot];
                if (userItem == null || userItem.wIndex <= 0 ||
                    userItem.Dura <= 0)
                {
                    continue;
                }

                var stdItem = M2Share.UserEngine.GetStdItem(userItem.wIndex);
                if (stdItem == null || !stdItem.NativeItemExtAbilParsed)
                {
                    continue;
                }

                var idents = stdItem.NativeItemExtAbilIdents;
                var values = stdItem.NativeItemExtAbilValues;
                var pairCount = Math.Min(6, Math.Min(idents?.Length ?? 0,
                    values?.Length ?? 0));
                for (var pair = 0; pair < pairCount; pair++)
                {
                    if (unchecked((byte)idents[pair]) == 201)
                    {
                        aggregate = unchecked((ushort)(aggregate +
                            unchecked((byte)values[pair])));
                    }
                }
            }

            return aggregate;
        }

        /// <summary>
        /// [self+0x1D5] != 0，即 agg2[0x25]（装备容器 +0x1F8+0x25，经 0x73D64E 的
        /// rep movsd 落到 self+0x1B0+0x25）。四个写入点全是扩展属性臂里的
        /// `C6 40 25 01`（0x76231B / 0x762372 / 0x762B26 / 0x762B6A）。
        /// The native dispatcher sets this marker when an equipped extension
        /// ident is 128 or 138.  GoodItem carries those idents, so the gate can
        /// be reconstructed without inventing a broader extension subsystem.
        /// </summary>
        protected virtual bool NativeDropRareKillerBonusGate()
        {
            if (m_UseItems == null || M2Share.UserEngine == null)
            {
                return false;
            }

            var count = Math.Min(m_UseItems.Length,
                Grobal2.HUMAN_EQUIPPED_ITEM_COUNT);
            for (var slot = 0; slot < count; slot++)
            {
                var userItem = m_UseItems[slot];
                if (userItem == null || userItem.wIndex <= 0 ||
                    userItem.Dura <= 0)
                {
                    continue;
                }

                var stdItem = M2Share.UserEngine.GetStdItem(userItem.wIndex);
                if (stdItem == null || !stdItem.NativeItemExtAbilParsed)
                {
                    continue;
                }

                var idents = stdItem.NativeItemExtAbilIdents;
                if (idents == null)
                {
                    continue;
                }

                var pairCount = Math.Min(6, idents.Length);
                for (var pair = 0; pair < pairCount; pair++)
                {
                    var ident = unchecked((byte)idents[pair]);
                    if (ident == 128 || ident == 138)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// sub_73D500 里与爆装分母有关的三次赋值：0x73D578 清 [+0x579]、
        /// 0x73DAC5 写 [+0x18C]、0x73DECF 在 [+0x1D5] 门下把 [+0x579] 置 10。
        /// 由 RecalcAbilitys 调用。
        /// <para>
        /// 原生的 0x73D578 在重置段，0x73DAC5 在装备聚合块拷贝之后
        /// （0x73D631 的 rep movsd）。本实现的属性 201 聚合直接读取同一批
        /// 正耐久装备记录，因此不依赖临时聚合块的写入顺序；其它扩展属性仍由
        /// 各自已验证的重算分支负责。
        /// </para>
        /// </summary>
        internal void NativeRecalcDropRareFields()
        {
            // 眼神「脚本控制人物爆率」把 0x73D578（7 字节）与 0x73DAC5（6 字节）整条
            // NOP 掉，重算不再复位这两个字段，改由脚本 SetV(g>0,2/3,·) 设定。
            // 0x73DECF 那次「[+0x1D5] 门下置 10」不在补丁范围，照常执行。
            var suppressed = Plugins.YanshenScriptDropRate.RecalcResetSuppressed();
            if (!suppressed)
            {
                m_btNativeDropRareKillerBonus = 0;                   // 0x73D578
                var aggregate = NativeEquipDropRareAggregate();
                if (aggregate < 0) aggregate = 0;
                m_nNativeDropRareBase = (int)((uint)aggregate / 10u); // 0x73DAC1 div ecx，无符号
            }
            if (NativeDropRareKillerBonusGate())                     // 0x73DEBE
            {
                m_btNativeDropRareKillerBonus = 10;                  // 0x73DECF
            }
        }

        /// <summary>
        /// sub_73FC70 @0x73FCA9-0x73FD0D 的分母。<paramref name="redName"/> 必须由调用方
        /// 按 0x73FCB6 的 `jge` 算成**严格** MyPKpoint &gt; nPKPunishPoint —— 背包 worker
        /// 的 0x7400BE `setle` 是 &gt;= ，原生这两处就不一致，不要统一。
        /// </summary>
        internal int NativeDeathEquipDropDenominator(bool redName, TBaseObject lastHiter)
        {
            var k = redName
                ? 21                                                 // 0x73FCB8 imm32 0x15
                : m_nNativeDropRareBase + 90;                        // 0x73FCC1 + 0x73FCC7 imm8 0x5A
            // 0x73FCEF: 只有 LastHiter 是 THumanKind([0x73BBE8]) 才减，怪物凶手不减。
            if (lastHiter != null && lastHiter.IsNativeHumanKind())
            {
                k -= lastHiter.m_btNativeDropRareKillerBonus;        // 0x73FD02 / 0x73FD08
            }
            if (k < 0) k = 0;                                        // 0x73FD0B
            return k;
        }

        /// <summary>
        /// 0x73FCEF 的 `is THumanKind` 测试。原生的 THumanKind 派生自玩家与英雄两支，
        /// 类指针常量 [0x73BBE8]。
        /// </summary>
        internal virtual bool IsNativeHumanKind() => false;
    }
}
