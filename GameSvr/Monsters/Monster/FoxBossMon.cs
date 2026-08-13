namespace GameSvr
{
    // ✅ 战神字节证据 (Tier-1)：race 174 = TFoxBossMon，classref 0x5F9348 -> VMT 0x5F9394，
    //    size 1272(0x4F8)，parent = TAnimal(0x71D51C)。它与 race 175 TStoneFoxBossMon 是兄弟
    //    （StoneFoxBossMon.cs 已合入，同 parent TAnimal）。
    //    工厂 sub_679F8C：索引表[174-0xB=0xA3]=0x5A=90 ; jt[90]=0x67AB3D ; case body 全文：
    //      67AB3D  B2 01 / A1 48 93 5F 00 / E8 DF 2C 0A 00 / 89 45 F8 / E9 EE 01 00 00
    //      classref [0x5F9348] 唯一加载点；ctor = 0x71D828 = TAnimal.Create（本类**无自有 ctor**）。
    //    case 内无额外 RNG/字段写。
    //
    // VMT 差分(vs TAnimal) 共 17 项，含 2 个新增虚槽(+0x20C/+0x210)；均依赖本类新字段
    //   [+0x4D8]/[+0x4DC]/[+0x4E0]/[+0x4E8]（父 size 0x4D8 之后）或未定 helper/无 C# 入口槽：
    //   +0x078(30) Initialize 0x5FA438：[+0x4E8]=tick / [+0x4D8]=0x1F4 / [+0x4DC]=0 / m_nViewRange=0xA
    //              / call 0x5FA660([+0x294]) / [+0x4E0]==4 时 AddState(0xE,dur 0x1770,1)。
    //   +0x084(33) Die 0x5FA8C8：[+0x4E0]∈{4,5}→m_btDirection=0xFF；base.Die。
    //   +0x088(34) Run 0x5FA830：can-act 且 !m_boStoneMode 且 (tick-[+0x4DC])>10000 → call [vmt+0x20C]。
    //   +0x0B8(46) 0x5FA4D4 = `xor eax,eax;ret`：被推挤谓词恒 false（C# 侧为调用方私有谓词，非虚）。
    //   +0x0C8(50) 0x5FA540：状态号(0x11/0x0A/0x03/1/2)分流后转父，状态施加路径。
    //   +0x0F4(61) 0x5FA954：(tick-[+0x4E8])>0x1388(5000) 的周期喊话/广播。
    //   +0x104(65) / +0x19C/1A0/1A4(103-105 属性虚槽,恒0) / +0x1A8(106) / +0x1B4(109 受击伤害选择器)
    //              / +0x1E8(122) / +0x1EC(123 AddState 变体) / +0x1FC(127 散金：两参原样透传，非 cap-flip)。
    //   +0x20C(131,新槽) 0x5FA5D0：HP<最大/2(word[+0x2B0]>>1 vs [+0x2AC]) 时 call [vmt+0x1D0](edx=8)。
    //   +0x210(132,新槽) 0x5FA5F4：清失效 target([+0x344]) + 搜敌节流(0x1F40/0x3E8)。
    //
    // ── fail-closed：以上 17 槽全部依赖未命名新字段/未定 helper/新增虚槽/无 C# 入口，忠实移植会臆造，
    //   故全保留父实现。构造器即 TAnimal.Create，无自有字段写。
    //   语义：一尊“狐王 Boss”——出生随机相位([+0x4E0])决定初始 AddState、周期广播、半血阶段技能、
    //   受击伤害免疫表。C# 侧行为退化为纯 AnimalObject；具名类型使 race 174 脱离 default sink
    //   (0x67AE5E→nil)，并为上述机制留确定挂载点。
    public class FoxBossMon : AnimalObject
    {
        public FoxBossMon() : base()
        {
        }
    }
}
