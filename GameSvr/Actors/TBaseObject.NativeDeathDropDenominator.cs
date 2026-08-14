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
    // ── [self+0x579] ──────────────────────────────────────────────────────────────────
    // 全镜像恰好三处引用，闭合：
    //   写  0x73D578  C6 86 79 05 00 00 00     mov byte [esi+0x579],0     重算开头清零
    //   写  0x73DECF  C6 86 79 05 00 00 0A     mov byte [esi+0x579],0xA
    //   读  0x73FD02  8A 83 79 05 00 00        mov al,[ebx+0x579]         分母减项
    //
    // ── 两个输入 BLOCKED ─────────────────────────────────────────────────────────────
    // 两个输入都出自装备扩展属性聚合子系统，C# 整套没有，所以恒 0/false：
    //   ① NativeEquipDropRareAggregate() ≡ 0  ⇒ 非红分母恒 90。
    //   ② NativeDropRareKillerBonusGate() ≡ false ⇒ [+0x579] 恒 0。
    // ------------------------------------------------------------------------------------------
    public partial class TBaseObject
    {
        /// <summary>
        /// [self+0x18C]。写于 0x73DAC5，读于 0x73FCC1（非红名分母）与 0x743E18。
        /// 值 = 装备扩展属性 201 之和 / 10，无符号截断。
        /// </summary>
        public int m_nNativeDropRareBase;

        /// <summary>
        /// [self+0x579]。0 或 10，由 [self+0x1D5] 决定（0x73DEBE / 0x73DECF）。
        /// 从凶手身上读走（0x73FD02 读的是 LastHiter），用来减小受害者的分母。
        /// </summary>
        public byte m_btNativeDropRareKillerBonus;

        /// <summary>
        /// word[装备容器+0x48+0x5E] —— 身上装备扩展属性类型 201 (0xC9) 的累加值。
        /// C# 还没有扩展属性子系统，所以恒 0（BLOCKED）。
        /// </summary>
        protected virtual int NativeEquipDropRareAggregate() => 0;

        /// <summary>
        /// [self+0x1D5] != 0，即 agg2[0x25]。C# 无该子系统，恒 false（BLOCKED）。
        /// </summary>
        protected virtual bool NativeDropRareKillerBonusGate() => false;

        /// <summary>
        /// sub_73D500 里与爆装分母有关的三次赋值的 C# 等价。
        /// </summary>
        internal void NativeRecalcDropRareFields()
        {
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
        /// sub_73FC70 @0x73FCA9-0x73FD0D 的分母。
        /// </summary>
        internal int NativeDeathEquipDropDenominator(bool redName, TBaseObject lastHiter)
        {
            var k = redName
                ? 21                                                 // 0x73FCB8 imm32 0x15
                : m_nNativeDropRareBase + 90;                        // 0x73FCC1 + 0x73FCC7 imm8 0x5A
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
