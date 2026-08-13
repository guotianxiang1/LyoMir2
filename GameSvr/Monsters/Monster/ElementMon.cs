namespace GameSvr
{
    // ✅ 战神字节证据 (Tier-1)：race 242 = TElementMon，classref 0x662308 -> VMT 0x662354，
    //    size 1268(0x4F4)，parent = TMonster(0x65E030, size 0x4E8)。
    //    工厂 sub_679F8C：索引表[242-0xB=0xE7]=0x6F=111 ; jt[111]=0x67ACCA ; case body 全文：
    //      67ACCA  B2 01 / A1 08 23 66 00 / E8 A2 E2 FE FF / 89 45 F8 / EB 64(jmp 0x67AD3F)
    //      classref [0x662308] 唯一加载点；ctor 0x668F78 唯一 E8 调用者。case 内无额外 RNG/字段写。
    //
    // 构造器 sub_668F78 全文（父 Create 之后）：
    //   00668F91  E8 76 D1 FF FF        call 0x66610C        ; = TMonster.Create（= C# Monster()）
    //   00668F96  C7 46 78 0C 00 00 00  mov dword [esi+0x78],0xC ; m_nViewRange = 12
    //   00668F9D  C6 86 C8 04 00 00 00  mov byte [esi+0x4C8],0   ; TMonster 字段,清零=默认
    //   00668FA4  33 C0 / 89 86 E8 04 00 00  mov dword [esi+0x4E8],0 ; 新字段(元素类型),清零=默认
    //   +0x78 = m_nViewRange。
    //
    // VMT 差分(vs TMonster) 4 项，均 fail-closed（依赖新字段 [+0x4E8]/[+0x4EC]/[+0x4F0]）：
    //   +0x024(9) 0x668D10：按 [+0x4E8] 元素类型分流的坐标/距离谓词。
    //   +0x078(30) Initialize 0x668EFC：base.Initialize → [+0x4E8]=[+0x294] / [+0x4F0]=1 /
    //              据 [+0x4E8]∈{1,2,5..7} 算 [+0x4EC]=(七倍[+0x2B0])/10 —— 元素属性初始化。
    //   +0x088(34) Run 0x6692E4：元素怪 AI（依赖 [+0x4F0]/[+0x4EC]/[+0x4E8]/[+0x2AC] 状态机）。
    //   +0x204(129) Attack 0x668D80：按 [+0x4E8] 元素类型走不同攻击(冰/火/…特效与伤害)。
    //   —— 依赖未命名新字段 [+0x4E8]/[+0x4EC]/[+0x4F0]，忠实移植会臆造，故全保留父实现。
    //   语义：“元素怪”，出生时按 [+0x4E8] 定元素属性并据此 AI/攻击。C# 忠实 = Monster + m_nViewRange=12；
    //   元素分支 fail-closed。原先 race 242 落 default(0x67AE5E) → nil。
    public class ElementMon : Monster
    {
        public ElementMon() : base()
        {
            m_nViewRange = 12;
        }
    }
}
