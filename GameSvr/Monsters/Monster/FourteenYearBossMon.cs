namespace GameSvr
{
    // ✅ 战神字节证据 (Tier-1)：race 243 = TFourteenYearBossMon，classref 0x661B38 -> VMT 0x661B84，
    //    size 1248(0x4E0)，parent = TAnimal(0x71D51C)。
    //    工厂 sub_679F8C：索引表[243-0xB=0xE8]=0x70=112 ; jt[112]=0x67ACDB ; case body 全文：
    //      67ACDB  B2 01 / A1 38 1B 66 00 / E8 05 D4 FE FF / 89 45 F8 / EB 53(jmp 0x67AD3F)
    //      classref [0x661B38] 唯一加载点；ctor 0x6680EC 唯一 E8 调用者。case 内无额外 RNG/字段写。
    //
    // 构造器 sub_6680EC 全文（父 Create 之后）：
    //   00668105  E8 1E 57 0B 00        call 0x71D828        ; = TAnimal.Create（= C# AnimalObject()）
    //   0066810A  C6 86 C8 04 00 00 00  mov byte [esi+0x4C8],0 ; TAnimal 字段,清零=默认
    //   00668111  C7 46 78 0C 00 00 00  mov dword [esi+0x78],0xC ; m_nViewRange = 12
    //   +0x78 = m_nViewRange。
    //
    // VMT 差分(vs TAnimal) 9 项，均 fail-closed：
    //   +0x00C(3)/+0x010(4) = `xor eax,eax;ret`：低位 TCreature 虚槽恒 0，C# 无入口。
    //   +0x078(30) Initialize 0x668228：base.Initialize → [+0x4D8]=[+0x2B0] / [+0x4DC]=[+0x2B0]
    //              （新字段初值取自 [+0x2B0]，无 C# 命名落点）。
    //   +0x088(34) Run 0x668160：周年 Boss AI（依赖 [+0x4D8]/[+0x4DC] 倒计时）。
    //   +0x0C8(50) 0x668890：状态施加路径。
    //   +0x19C/1A0/1A4(103-105) = `xor eax,eax;ret`：属性虚槽恒 0。
    //   +0x1E8(122) CanAddNativeTimedAbility 0x6688C0：base && !sub_668334(t) —— 拒绝表在
    //              **未移植 helper sub_668334** 里(非内联)，无法忠实展开，故 fail-closed（不像 159/160
    //              是内联 t!=26&&t!=29）。
    //   —— 全部保留父实现。
    //
    // 语义：十四周年 Boss；C# 忠实 = 纯 AnimalObject + m_nViewRange=12；AI/倒计时/状态免疫 fail-closed。
    //   原先 race 243 落 default(0x67AE5E) → nil。
    public class FourteenYearBossMon : AnimalObject
    {
        public FourteenYearBossMon() : base()
        {
            m_nViewRange = 12;
        }
    }
}
