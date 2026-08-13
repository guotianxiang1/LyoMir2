namespace GameSvr
{
    // ✅ 战神字节证据 (Tier-1)：race 179 = TBaiHu（白虎），classref 0x6656B8 -> VMT 0x665704，
    //    size 1256(0x4E8) —— 与 parent = TATMonster(0x65E7E4) 同尺寸 => 零新增字段。
    //    工厂 sub_679F8C：索引表[179-0xB=0xA8]=0x5F=95 ; jt[95]=0x67ABA1 ; case body 全文：
    //      67ABA1  B2 01 / A1 B8 56 66 00 / E8 EB BE FE FF(call 0x666A98) / 89 45 F8 / E9 8A 01 00 00
    //      classref [0x6656B8] 唯一加载点；ctor = 0x666A98 = TATMonster.Create（本类**无自有 ctor**）。
    //    case 内无额外 RNG/字段写。=> 构造 == AtMonster()。
    //
    // VMT 差分(vs TATMonster) 4 项，均 fail-closed：
    //   +0x19C/1A0/1A4(103-105) = `xor eax,eax;ret`：属性虚槽恒 0（C# 侧非虚，无可覆写入口）。
    //   +0x204(129) Attack 0x66CEBC：白虎扑击——Random(100)>=0x4B(75) 时在目标格 [+0x12C]/[+0x130]
    //     call [vmt+0xE0](dx=0x10 特效) + [tgt.vmt+0x104]，按 DC([+0x28C]/[+0x290]) 掷伤。
    //     依赖 [+0x28C]/[+0x290] 与多点 helper/坐标虚槽，C# 侧 Attack 语义未收敛，故不覆写。
    //   —— 全部保留父实现。C# 侧行为 = AtMonster。原先 race 179 落 default(0x67AE5E) → nil。
    //   具名类型使白虎脱离 default sink，并为扑击攻击留确定挂载点。
    public class BaiHu : AtMonster
    {
        public BaiHu() : base()
        {
        }
    }
}
