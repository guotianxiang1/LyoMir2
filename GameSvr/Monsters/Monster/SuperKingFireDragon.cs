namespace GameSvr
{
    // ✅ 战神字节证据 (Tier-1)：race 155 = TSuperKingFireDragon，classref 0x6644B8 -> VMT 0x664504，
    //    size 1260(0x4EC) —— 与 parent = TKingFireDragon(0x664270, C# KingFireDragon) 同尺寸 => 零新增字段。
    //    工厂 sub_679F8C：索引表[155-0xB=0x90]=0x49=73 ; jt[73]=0x67A9E9 ; case body 全文：
    //      67A9E9  B2 01 / A1 B8 44 66 00 / E8 63 0C FF FF / 89 45 F8 / E9 42 03 00 00
    //      classref [0x6644B8] 唯一加载点；ctor = 0x66B658 = **TKingFireDragon.Create（父子共享，
    //      本类无自有 ctor）**——故 C# `: base()` 即忠实复刻 ctor（m_nViewRange=12 + 4 个新字段清零，
    //      见 KingFireDragon.cs）。case 内无额外 RNG/字段写。
    //
    // VMT 差分(vs TKingFireDragon) 仅 1 项，fail-closed：
    //   +0x20C(131,TKingFireDragon 定义的新增虚槽) 0x66B8D4：Random(100) 分档(10/60/95)后
    //     选 (skill, dur, val) 三元组，若有目标 call 0x766060(cx=0x283C=10300, 0x258=600) 施放技能/特效。
    //     该槽是 TKingFireDragon 的**新增虚槽**，KingFireDragon.cs 已注明其身份未定并 fail-closed
    //     （C# 未为其造虚方法入口），故本类的覆写同样无处落地，保留父(TKingFireDragon)实现。
    //   —— C# 侧行为 = KingFireDragon。原先 race 155 落 default(0x67AE5E) → nil；具名类型使
    //   “超级火龙王”脱离 default sink，并为其分档技能(slot131)留确定挂载点。
    public class SuperKingFireDragon : KingFireDragon
    {
        public SuperKingFireDragon() : base()
        {
        }
    }
}
