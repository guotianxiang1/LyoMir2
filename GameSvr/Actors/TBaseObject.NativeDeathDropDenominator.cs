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
    //   7620DA  8A 43 11              al := byte [attr+0x11]        ; 属性类型
    //   7620DD  83 C0 C7              eax += -0x39                  ; 偏置 57
    //   7620E0  3D 97 00 00 00 / 0F 87  cmp eax,0x97 / ja           ; 只认 57..208
    //   7620EB  8A 80 F8 20 76 00     al := byte [eax+0x7620F8]     ; 类型 -> 槽号表(152 项)
    //   7620F1  FF 24 85 90 21 76 00  jmp [eax*4+0x762190]          ; 槽号 -> 臂(33 条)
    //   槽 27 的臂 = 0x7623B0，表项在 0x7621FC：
    //   7623B0  33 C0 / 8A 43 13      al := byte [attr+0x13]        ; 属性值
    //   7623B5  66 01 46 5E           add word [esi+0x5E], ax
    // 反查 152 项槽号表，落到槽 27 的**只有属性类型 201 (0xC9)**（邻居 202 落到 +0x60）。
    //
    // ⇒ [self+0x18C] = (身上所有装备的扩展属性 201 之和) / 10，无符号截断。
    //    分母 = 该值 + 90，所以属性 201 只会让装备**更不容易**掉，90 是地板。
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
    // ── 仍然 BLOCKED 的两件事，别当成 0 是"对的" ─────────────────────────────────────
    // ① 装备扩展属性子系统（类型 57..208 → 33 个聚合槽）C# 整套没有，所以
    //    NativeEquipDropRareAggregate() 现在恒返回 0，分母恒为 90。对**没有属性 201
    //    装备**的玩家这就是原生值；对有的玩家 C# 会比原生**更容易**掉装备。
    //    方向是单边的、有界的，不会比原生掉得少。
    // ② [self+0x1D5] 在**全镜像只有一处引用**，就是 0x73DEBE 那次读，一个写入点都没有。
    //    它只能来自人物存档记录的整块装载。在定位到那条装载路径之前
    //    NativeDropRareKillerBonusGate() 恒 false，等于 [+0x579] 恒 0。
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
        /// word[装备容器+0x48+0x5E] —— 身上装备扩展属性类型 201 (0xC9) 的累加值。
        /// C# 还没有扩展属性子系统，所以恒 0（BLOCKED，见文件头 ①）。
        /// </summary>
        protected virtual int NativeEquipDropRareAggregate() => 0;

        /// <summary>
        /// [self+0x1D5] != 0。全镜像唯一引用是 0x73DEBE 的读，没有写入点，
        /// 只能来自存档整块装载（BLOCKED，见文件头 ②）。
        /// </summary>
        protected virtual bool NativeDropRareKillerBonusGate() => false;

        /// <summary>
        /// sub_73D500 里与爆装分母有关的三次赋值：0x73D578 清 [+0x579]、
        /// 0x73DAC5 写 [+0x18C]、0x73DECF 在 [+0x1D5] 门下把 [+0x579] 置 10。
        /// 由 RecalcAbilitys 调用。
        /// <para>
        /// 顺序注意：原生的 0x73D578 在重置段，0x73DAC5 在**装备聚合块拷贝之后**
        /// （0x73D631 的 rep movsd）。这里三步并在重置段执行，只有在
        /// NativeEquipDropRareAggregate() 恒 0 的当下才等价。谁实现了属性 201 的
        /// 聚合，必须把这一步挪到装备扫描之后。
        /// </para>
        /// </summary>
        internal void NativeRecalcDropRareFields()
        {
            m_btNativeDropRareKillerBonus = 0;                       // 0x73D578
            var aggregate = NativeEquipDropRareAggregate();
            if (aggregate < 0) aggregate = 0;
            m_nNativeDropRareBase = (int)((uint)aggregate / 10u);    // 0x73DAC1 div ecx，无符号
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
