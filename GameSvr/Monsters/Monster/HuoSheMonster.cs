namespace GameSvr
{
    // ✅ 战神字节证据 (Tier-1)：race 245 = THuoSheMonster，classref 0x662078 -> VMT 0x6620C4，
    //    size 1240(0x4D8) —— 与 parent = TAnimal(0x71D51C) 尺寸【完全相同】=> 自身零新增字段。
    //    工厂 sub_679F8C：索引表[245-0xB=0xEA]=0x72=114 ; jt[114]=0x67ACFD ; case body 全文：
    //      67ACFD  B2 01 / A1 78 20 66 00 / E8 AA DC FE FF / 89 45 F8 / (落公共尾部 0x67AD3F)
    //      classref [0x662078] 唯一加载点；ctor 0x6689AC 唯一 E8 调用者。case 内无额外 RNG/字段写。
    //
    // 构造器 sub_6689AC 全文（父 Create 之后）：
    //   006689C5  E8 5E 4E 0B 00        call 0x71D828        ; = TAnimal.Create（= C# AnimalObject()）
    //   006689CA  C7 46 78 05 00 00 00  mov dword [esi+0x78],5 ; m_nViewRange = 5
    //   +0x78 = m_nViewRange。
    //
    // VMT 差分(vs TAnimal) 14 项，均 fail-closed（依赖未定 helper/无 C# 入口槽；自身无新字段）：
    //   +0x024(9) 0x668AA4、+0x078(30 Init) 0x668A18、+0x084(33 Die) 0x668A54、+0x088(34 Run) 0x668AE0、
    //   +0x0A8(42)、+0x0C8(50)、+0x0F4(61)、+0x104(65)、+0x19C/1A0/1A4(103-105 属性虚槽,恒0)、
    //   +0x1AC/+0x1B0(107/108)、+0x204(129 Attack) 0x668ABC。
    //   —— Run/Attack/搜敌等依赖父级 helper 与属性虚槽，C# 侧多为非虚具体函数，无可覆写入口；
    //   忠实移植会臆造，故全保留父实现。
    //   语义：“火蛇怪”；C# 忠实 = 纯 AnimalObject + m_nViewRange=5。原先 race 245 落 default → nil。
    public class HuoSheMonster : AnimalObject
    {
        public HuoSheMonster() : base()
        {
            m_nViewRange = 5;
        }
    }
}
