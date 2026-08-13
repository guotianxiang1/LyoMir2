namespace GameSvr
{
    // ✅ 战神字节证据 (Tier-1)：race 233 = TItemAttMon，classref 0x6651A0 -> VMT 0x6651EC，
    //    size 1256(0x4E8) —— 与 parent = TMonster(0x65E030) 同尺寸 => 零新增字段。
    //    工厂 sub_679F8C：索引表[233-0xB=0xDE]=0x6D=109 ; jt[109]=0x67ACA5 ; case body 全文：
    //      67ACA5  B2 01 / A1 A0 51 66 00 / E8 5B B4 FE FF / 89 45 F8 / E9 86 00 00 00
    //      classref [0x6651A0] 唯一加载点；ctor = 0x66610C = TMonster.Create（本类**无自有 ctor**）。
    //    case 内无额外 RNG/字段写。=> 构造 == Monster()（默认 race 0x50=RC_MONSTER，MonInitialize 后被 DB 覆盖）。
    //
    // VMT 差分(vs TMonster) 10 项，均 fail-closed：
    //   +0x010(4) = `xor eax,eax;ret`：TCreature 虚槽恒 0。
    //   +0x078(30) Initialize 0x66D1AC：`call 0x71D904; ret`（空转发 = inherited，C# 直接继承，无需覆写）。
    //   +0x084(33) Die 0x66D1B8：[+0x2BC]=0 / [+0x4BC]=0（父级字段，身份未定）后 base.Die。
    //   +0x0C8(50) 0x66D1D8 = `ret 4`(空,免疫某状态施加)。
    //   +0x104(65) 0x66D1E0 = `xor eax,eax; ret 0x14`(恒 0)。
    //   +0x19C/1A0/1A4(103-105) = `xor eax,eax;ret`：属性虚槽恒 0。
    //   +0x1AC(107) 0x66D1A0 = `ret 4`(空)、+0x1B0(108) 0x66D1A8 = `xor eax,eax;ret`。
    //   —— 全为属性/空虚槽（C# 侧非虚具体函数，无可覆写入口）或依赖未命名父级字段，故全保留父实现。
    //   语义：“道具攻击怪”(掉落/受击特化)，类差异主要在 Monsters.DB 数据。C# 侧行为 = Monster。
    //   原先 race 233 落 default(0x67AE5E) → nil；具名类型使其脱离 default sink。
    public class ItemAttMon : Monster
    {
        public ItemAttMon() : base()
        {
        }
    }
}
