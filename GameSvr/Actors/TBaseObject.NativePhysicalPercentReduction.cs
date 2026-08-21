using SystemModule;

namespace GameSvr
{
    // ============================================================================
    // 战神 self+0x2DC —— 「百分比物理减伤总量」(signed word)，逐字节。
    // (flat_image.bin, ImageBase=0x400000, file_offset = VA-0x400000)
    //
    // ── 消费者（两处，公式完全一致，证实 self+0x2DC 是百分比减伤）──────────────────
    //  ① sub_73F8E0(VMT+0x1AC 物理落地管线，C# = ApplyNativePhysicalLandingDamage)
    //       0x73F903  66 8B 8F DC 02 00 00   mov cx,[edi+0x2DC]     ; pct (signed word)
    //       0x73F90A  test cx,cx / jle        ; pct<=0 整段跳过
    //       0x73F90F  movsx eax,cx / imul esi ; pct*damage
    //       0x73F919  cdq / idiv 0x64         ; /100 (向零)
    //       0x73F920  cmp eax,0x4E20 / 上钳 20000
    //       0x73F92C  sub esi,eax             ; damage -= reduction
    //  ② sub_746130 @0x746177（另一伤害路径，同式）
    //       0x746177  66 8B 88 DC 02 00 00   mov cx,[eax+0x2DC]
    //       0x746186  imul ebx / idiv 0x64 / cmp 0x4E20 上钳 / sub ebx,eax
    //  => reduction = min(damage * self+0x2DC / 100, 20000)，self+0x2DC 有符号 word。
    //
    // ── 填充：全镜像里 self+0x2DC 作为 word 的唯一写者就是 RecalcAbilitys(sub_73D500) ──
    //  的三条 add（disp32 全镜像扫描：写点仅 0x73DEAB/0x73DEBA/0x73DECA，余皆读）。
    //
    //  【种子 = 0，逐字节证明】RecalcAbilitys 开头 0x73D564 call sub_73D3E4：
    //       0x73D418  8D B0 E8 01 00 00   lea esi,[eax+0x1E8]   ; base 扩展块
    //       0x73D41E  8D B8 64 02 00 00   lea edi,[eax+0x264]   ; work 扩展块
    //       0x73D424  B9 1F 00 00 00      mov ecx,0x1F          ; 31 dword = 0x7C 字节
    //       0x73D429  F3 A5               rep movsd             ; base→work
    //     偏移差 0x264-0x1E8 = 0x7C，故 work[+0x2DC] := base[+0x2DC-0x7C] = base[+0x260]。
    //     base[+0x260] 在 THumanKind 无任何写者（disp32 扫描 +0x260 的命中几乎全是
    //     `FF 93/97/96 60 02 00 00` = call [reg+0x260] 的 VMT 槽调用，非本对象数据写），
    //     且 sub_73D3E4 前段只把 work 的 +0x278/+0x2AC/+0x2B4/+0x2BC 回存到 base 的
    //     +0x1FC/+0x230/+0x238/+0x240（不含 +0x260）。⇒ 每次重算 self+0x2DC 复位为 0，
    //     再叠加下面三条 add。C# 以 total=0 起算即精确复刻该复位。
    //
    //  【write#1】0x73DEA4 66 8B 47 58 mov ax,[edi+0x58] / 0x73DEA8 66 01 86 DC 02 00 00
    //     add word[esi+0x2DC],ax。edi = 栈上 0x1B0 字节缓冲(0x73D52E lea edi,[ebp-0x1B8]，
    //     0x73D631 rep movsd 从「装备容器 self+0x4C0」的 +0x48 拷 0x6C dword)，即
    //     edi+0x58 = 容器聚合块 agg1(容器+0x48) 的 +0x58。agg1+0x58 是 **DWORD** 累加器
    //     (0x7623AB 01 46 58 add dword[esi+0x58],eax)，由装备扩展属性分发器
    //     (sub_76203C / 兄弟 sub_762974) 的 slot 26 臂(0x76238E→0x7623A6)喂养：
    //       0x7623A6  33 C0      xor eax,eax
    //       0x7623A8  8A 43 13   mov al,[ebx+0x13]      ; ebx = StdItem+4 栈副本 ⇒ StdItem+0x17 属性值低字节
    //       0x7623AB  01 46 58   add dword[agg1+0x58],eax
    //     0x7620DA 用 AL 读取 StdItem+0x15，因此属性编号同样只取低字节。
    //     反查 152 项槽表(0x7620F8)：落到 slot 26 的属性类型 = 0xAA..0xAE(170..174)。
    //     ⇒ write#1 = Σ(身上装备中扩展属性类型∈{0xAA..0xAE}的 byte(StdItem+0x17))，
    //     先按 DWORD 累加，再读其低 word。
    //     GoodItem 已保留 2.08 原生六组扩展属性槽，本实现直接消费这些已解析定义，
    //     精确恢复类型 0xAA..0xAE 对 agg1+0x58 的贡献；其它分发臂不在本文件扩展。
    //
    //  【write#2】0x73DEAF 33 C0 xor eax,eax / 0x73DEB1 8A 86 78 05 00 00 mov al,[esi+0x578]
    //     / 0x73DEB7 66 01 86 DC 02 00 00 add word[esi+0x2DC],ax。零扩展一个 byte。
    //     self+0x578 = m_btNativeDamageShare(伤害分担)，GM 359 @ChgDmgShare 0x628036 设置、
    //     持久化于 rec+0x537(见 TPlayObject.NativeUnmappedScalars.cs)。**活字段**，忠实累加。
    //
    //  【write#3】0x73DEBE 80 BE D5 01 00 00 00 cmp byte[esi+0x1D5],0 / 0x73DEC5 74 0F je /
    //     0x73DEC7 66 83 86 DC 02 00 00 14 add word[esi+0x2DC],0x14。即门开则 +20。
    //     self+0x1D5 = agg2[0x25]（装备容器+0x1F8+0x25，经 0x73D64E rep movsd 落到 self+0x1B0+0x25），
    //     四个写者全是扩展属性臂里的 C6 40 25 01(0x76231B/0x762372/0x762B26/0x762B6A)。
    //     同一道门在 0x73DECF 还把 self+0x579 置 10 —— 即已建模的
    //     NativeDropRareKillerBonusGate()（见 TBaseObject.NativeDeathDropDenominator.cs）。
    //     同一道门已由 NativeDropRareKillerBonusGate() 从扩展属性 128/138 恢复，
    //     因此 write#3 与爆装凶手减项共享同一个已验证谓词。
    //
    // ── 顺序说明（与 NativeRecalcDropRareFields 相同的既定约定）─────────────────────
    //  原生 write#1..#3 在装备扫描/容器聚合之后(0x73DExx)。本实现直接扫描同一批
    //  正耐久装备定义，而不是依赖一个临时 agg1/agg2 缓冲，因此在重算重置段求值仍
    //  得到相同的三个输入；write#2(m_btNativeDamageShare)与装备扫描无关。
    // ============================================================================
    public partial class TBaseObject
    {
        /// <summary>
        /// self+0x2DC（signed word）= 百分比物理减伤总量。仅由
        /// <see cref="NativeRecalcPhysicalReductionPercent"/> 在 RecalcAbilitys 内累加，
        /// 由 <c>NativePhysicalPercentDamageReduction()</c>（见
        /// TBaseObject.NativeArmorLanding.cs）与 sub_746130 端口读取。
        /// </summary>
        public short m_wNativePhysicalDamageReductionPercent;

        /// <summary>
        /// write#1 源：agg1+0x58（DWORD 累加器）的低 word。= 身上装备扩展属性类型
        /// ∈{0xAA..0xAE} 的 StdItem+0x17 低字节之和（分发器 slot 26 臂
        /// 0x76238E/0x7623A6）。
        /// GoodItem 已映射原生六组扩展属性槽；这里只消费已证实的 170..174。
        /// </summary>
        protected virtual int NativeEquipPhysicalReductionAggregate()
        {
            if (m_UseItems == null || M2Share.UserEngine == null)
            {
                return 0;
            }

            // 0x7623AB is a DWORD add, then 0x73DEA4 reads only AX.  Preserve
            // both widths: accumulate modulo 2^32 and return the low word.
            uint aggregate = 0;
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
                    var ident = unchecked((byte)idents[pair]);
                    if (ident >= 0xAA && ident <= 0xAE)
                    {
                        aggregate = unchecked(aggregate +
                            unchecked((byte)values[pair]));
                    }
                }
            }

            return unchecked((ushort)aggregate);
        }

        /// <summary>
        /// write#2 源：byte[self+0x578] 零扩展（0x73DEB1）。TBaseObject 基类无此字段，
        /// 返回 0；<see cref="TPlayObject"/> 覆盖为返回 m_btNativeDamageShare（活字段）。
        /// </summary>
        protected virtual int NativePhysicalReductionDamageShare() => 0;

        /// <summary>
        /// sub_73D500 的 self+0x2DC 累加（write#1/#2/#3），由 RecalcAbilitys 调用。
        /// 以 total=0 起算 = 原生 sub_73D3E4 把 self+0x2DC 复位为 base[+0x260]=0（见文件头）。
        /// </summary>
        internal void NativeRecalcPhysicalReductionPercent()
        {
            // total 用 int 聚合、末尾截 16 位，与原生对 word 逐条 add 的最终低 16 位一致
            // （模 2^16 加法可结合）。self+0x2DC 读端为有符号 word，故存 short。
            var total = 0;
            total += NativeEquipPhysicalReductionAggregate(); // 0x73DEA8 write#1
            total += NativePhysicalReductionDamageShare();    // 0x73DEB7 write#2（m_btNativeDamageShare）
            if (NativeDropRareKillerBonusGate())              // 0x73DEBE 门 self+0x1D5
            {
                total += 0x14;                                // 0x73DEC7 write#3（+20）
            }
            m_wNativePhysicalDamageReductionPercent = unchecked((short)total);
        }
    }
}
