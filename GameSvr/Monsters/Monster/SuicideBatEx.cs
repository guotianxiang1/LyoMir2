namespace GameSvr
{
    // ✅ 战神字节证据 (Tier-1)：race 153 = TSuicideBatEx，classref 0x6649E8 -> VMT 0x664A34，
    //    size 1256(0x4E8) —— 与 parent = TSuicideBat(0x6647A8, C# SuicideBat) 同尺寸 => 零新增字段。
    //    工厂 sub_679F8C：索引表[153-0xB=0x8E]=0x47=71 ; jt[71]=0x67A9C1 ; case body 全文：
    //      67A9C1  B2 01 / A1 E8 49 66 00 / E8 1B 11 FF FF / 89 45 F8 / E9 6A 03 00 00
    //      classref [0x6649E8] 唯一加载点；ctor 0x66BAE8 唯一 E8 调用者。case 内无额外 RNG/字段写。
    //
    // 构造器 sub_66BAE8 全文（纯转调父类，无自定义字段）：
    //   0066BB01  E8 1E FF FF FF        call 0x66BA24        ; = TSuicideBat.Create（= C# SuicideBat()）
    //   （即 m_dwSearchTime=Random(1500)+2500、m_nViewRange=12，见 SuicideBat.cs）
    //
    // VMT 差分(vs TSuicideBat) 3 项，均 fail-closed（父 TSuicideBat 亦已把这些槽 fail-closed，无 C# 入口）：
    //   +0x1A4(105) 0x66BBB8 = `xor eax,eax;ret`：属性虚槽恒 0。
    //   +0x1B4(109) 0x66BB24 = `xor eax,eax;ret 8`：受击伤害选择器恒 0（C# 侧非虚）。
    //   +0x204(129) 0x66BB30：死亡自爆 AoE 增强版——SendRefMsg(0x2905 爆炸广播) + [+0x264+0x48]=0，
    //     遍历 m_VisibleActors([+0x388])：0x7743E0(范围) && !0x772DA8(死/魂) && 0x767498(可攻击)
    //     -> [tgt+0x2AC]=0（目标 HP 归零）。依赖 slot129 的 Attack 语义、[+0x2AC]/[+0x388] 布局与
    //     多点 helper，与 SuicideBat.cs 的自爆(同为 fail-closed)同因，忠实移植会臆造，故不覆写。
    //   —— 全部保留父实现。C# 侧行为 = SuicideBat（父类搜敌 AI 正确）。
    //   原先 race 153 落 default(0x67AE5E) → nil；具名类型使其脱离 default sink，并为增强自爆留挂载点。
    public class SuicideBatEx : SuicideBat
    {
        public SuicideBatEx() : base()
        {
        }
    }
}
