namespace GameSvr
{
    // ✅ 战神字节证据 (Tier-1)：race 167 = TSuperSkeleton，classref 0x6610C8 -> VMT 0x661114，
    //    size 1292(0x50C)，parent = TWhiteSkeleton(0x660E80, C# WhiteSkeleton)。
    //    工厂 sub_679F8C：索引表[167-0xB=0x9C]=0x53=83 ; jt[83]=0x67AAC5 ; case body 全文：
    //      67AAC5  B2 01 / A1 C8 10 66 00 / E8 17 D2 FE FF / 89 45 F8 / E9 66 02 00 00
    //      classref [0x6610C8] 唯一加载点；ctor = 0x667CE8 = **TWhiteSkeleton.Create（父子共享，
    //      本类无自有 ctor）**——故 C# `: base()` 即忠实复刻 ctor（m_boFixedHideMode=true、
    //      m_nViewRange=6、m_boIsFirst 等，见 WhiteSkeleton.cs）。case 内无额外 RNG/字段写。
    //
    // VMT 差分(vs TWhiteSkeleton) 仅 1 项，fail-closed：
    //   +0x204(129) Attack 0x66CA80：白骨投掷强化——按 byte[+0x483] 等级、DC([+0x28C]/[+0x290])
    //     在 [+0x4F0] 缓冲上 call 0x76E268(骨矛飞射, 0x3E8/1) 等施放远程攻击。
    //     父 WhiteSkeleton 覆写的是 SearchTarget(+0x8C) 与 Run(+0x88)，**并未**为 +0x204 造 C# 虚方法入口；
    //     本槽依赖 [+0x483]/[+0x4F0]/[+0x28C]/[+0x290] 与多点 helper(0x76E268/0x772578)，忠实移植会臆造，
    //     故保留父(WhiteSkeleton)实现。
    //   —— C# 侧行为 = WhiteSkeleton（钻地/搜敌 AI 正确）。原先 race 167 落 default(0x67AE5E) → nil；
    //   具名类型使“超级白骨”脱离 default sink，并为骨矛远程攻击(slot129)留确定挂载点。
    public class SuperSkeleton : WhiteSkeleton
    {
        public SuperSkeleton() : base()
        {
        }
    }
}
