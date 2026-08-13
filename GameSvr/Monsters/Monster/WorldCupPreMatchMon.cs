namespace GameSvr
{
    // ✅ 战神字节证据 (Tier-1)：race 236 = TWorldCupPreMatchMon，classref 0x661DD8 -> VMT 0x661E24，
    //    size 1244(0x4DC)，parent = TAnimal(0x71D51C, size 0x4D8) —— 自有字段 = dword[+0x4D8]。
    //    工厂 sub_679F8C：索引表[236-0xB=0xE1]=0x6E=110 ; jt[110]=0x67ACB9 ; case body 全文：
    //      67ACB9  B2 01 / A1 D8 1D 66 00 / E8 37 DC FE FF / 89 45 F8 / EB 75(jmp 0x67AD3F)
    //      classref [0x661DD8] 唯一加载点；ctor 0x6688FC 唯一 E8 调用者。case 内无额外 RNG/字段写。
    //
    // 构造器 sub_6688FC 全文（父 Create 之后）：
    //   00668915  E8 0E 4F 0B 00        call 0x71D828        ; = TAnimal.Create（= C# AnimalObject()）
    //   0066891A  C6 86 E1 02 00 00 01  mov byte [esi+0x2E1],1 ; m_boSuperMan = true
    //   00668921  C6 46 75 01           mov byte [esi+0x75],1   ; m_boStickMode = true
    //   00668925  33 C0 / 89 86 D8 04 00 00  mov dword [esi+0x4D8],0 ; 新字段(积伤计数),清零=默认
    //   +0x2E1 = m_boSuperMan(StoneFoxBossMon.cs:77)、+0x75 = m_boStickMode(IceDoor.cs:36)。
    //
    // VMT 差分(vs TAnimal) 8 项，均 fail-closed（依赖新字段 dword[+0x4D8] 或无 C# 入口槽）：
    //   +0x0A8(42) 0x668948 = `xor eax,eax;ret 8`：恒返回 0/nil。
    //   +0x104(65) 0x668960：伤害累加进 [+0x4D8]（超大上限 0x7D2B7500 前不累）——世界杯预选“积分靶”。
    //   +0x198(102) 0x668954 = `mov eax,[ebp+8];ret 4`：属性虚槽，原样透传。
    //   +0x19C/1A0/1A4(103-105) = `xor eax,eax;ret`：属性虚槽恒 0。
    //   +0x1AC(107) 0x6689A0 = `ret 4`(空)、+0x1B0(108) 0x6689A8 = `mov eax,edx;ret`。
    //   —— 皆属性/计数虚槽，C# 侧为非虚具体函数，无可覆写入口；[+0x4D8] 无命名落点。
    //
    // 语义：不可推动(m_boStickMode)、无敌(m_boSuperMan)的“射门靶”，被击时把伤害累加进 [+0x4D8]。
    //   C# 忠实 = 无敌 + 不可推 + 具名类型；积分累计 fail-closed。原先 race 236 落 default → nil。
    public class WorldCupPreMatchMon : AnimalObject
    {
        public WorldCupPreMatchMon() : base()
        {
            m_boSuperMan = true;
            m_boStickMode = true;
        }
    }
}
