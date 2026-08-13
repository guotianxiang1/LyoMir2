namespace GameSvr
{
    // ✅ 战神字节证据 (Tier-1)：race 178 = TQingLong（青龙），classref 0x66542C -> VMT 0x665478，
    //    size 1256(0x4E8) —— 与 parent = TATMonster(0x65E7E4) 同尺寸 => 零新增字段。
    //    工厂 sub_679F8C：索引表[178-0xB=0xA7]=0x5E=94 ; jt[94]=0x67AB8D ; case body 全文：
    //      67AB8D  B2 01 / A1 2C 54 66 00 / E8 06 BF FE FF(call 0x666A98) / 89 45 F8 / jmp 0x67AD3F
    //      classref [0x66542C] 唯一加载点；ctor = 0x666A98 = TATMonster.Create（本类**无自有 ctor**）。
    //    case 内无额外 RNG/字段写。=> 构造 == AtMonster()。
    //
    // VMT 差分(vs TATMonster) 4 项，均 fail-closed：
    //   +0x19C/1A0/1A4(103-105) = `xor eax,eax;ret`：属性虚槽恒 0（C# 侧非虚，无可覆写入口）。
    //   +0x200(128) 0x66CCB8：AttackTarget 变体（青龙的范围/元素攻击，依赖父级 helper 与坐标虚槽）。
    //   —— 全部保留父实现。C# 侧行为 = AtMonster。原先 race 178 落 default(0x67AE5E) → nil。
    //   具名类型使青龙脱离 default sink，并为其攻击/属性虚槽留确定挂载点。
    public class QingLong : AtMonster
    {
        public QingLong() : base()
        {
        }
    }
}
