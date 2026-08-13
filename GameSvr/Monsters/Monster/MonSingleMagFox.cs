namespace GameSvr
{
    // ✅ 战神字节证据 (Tier-1)：race 157 = TMonSingleMagFox，classref 0x66135C -> VMT 0x6613A8，
    //    size 1256(0x4E8) —— 与 parent = TATMonster(0x65E7E4) 同尺寸 => 零新增字段。
    //    工厂 sub_679F8C：索引表[157-0xB=0x92]=0x4B=75 ; jt[75]=0x67AA11 ; case body 全文：
    //      67AA11  B2 01 / A1 5C 13 66 00 / E8 7B C0 FE FF / 89 45 F8 / E9 1A 03 00 00
    //      classref [0x66135C] 唯一加载点；ctor = 0x666A98 = TATMonster.Create（本类**无自有 ctor**）。
    //    case 内无额外 RNG/字段写。=> 构造 == AtMonster()（m_dwSearchTime=Random(1500)+1500）。
    //
    // VMT 差分(vs TATMonster) 2 项，均 fail-closed：
    //   +0x024(9) 0x66CA70 = `cmp edx,6 / jl ret0? ; cmp ecx,6 / jl ; xor eax,eax; ret`：
    //     一个坐标/距离谓词(dx,cx 两个坐标分量同 >=6 才返回值)，槽身份未定、C# 无对应虚方法。
    //   +0x204(129) Attack 0x66C9C4：IsProperTarget(0x767498) 命中后按 DC([+0x28C]/[+0x290]) 掷伤，
    //     在目标格 [+0x12C]/[+0x130] call 0x769258(cx=?)/0x76C01C(狐火特效, skill 0xDC=220, 0x258=600)/
    //     0x76920C —— 单只“魔法狐”的火球攻击。依赖 [+0x28C]/[+0x290]/[+0x2B8] 与多点 helper，
    //     C# 侧 Attack 语义未收敛，忠实移植会臆造，故不覆写。
    //   —— 全部保留父实现。C# 侧行为 = AtMonster（父类搜敌 AI 正确）。
    //   原先 race 157 落 default(0x67AE5E) → nil；具名类型使其脱离 default sink，并为狐火攻击留挂载点。
    public class MonSingleMagFox : AtMonster
    {
        public MonSingleMagFox() : base()
        {
        }
    }
}
