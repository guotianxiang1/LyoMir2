namespace GameSvr
{
    // ✅ 战神字节证据 (Tier-1)：race 138 = TWolfStickIceMon，classref 0x66D594 → VMT 0x66D5E0,
    //    size 0x55C（= TSearchMon size，自身无新字段），classname "TWolfStickIceMon"，
    //    parent = TSearchMon（= C# SearchMon）。
    //    工厂 sub_679F8C handler 0x67A895 全文（20 字节，无额外 case-body 逻辑）：
    //      67A895  B2 01              mov  dl,1
    //      67A897  A1 94 D5 66 00     mov  eax,[0x66D594]   ; classref -> TWolfStickIceMon
    //      67A89C  E8 33 8D FF FF     call 0x6735D4         ; = TSearchMon.Create（共享 ctor）
    //      67A8A1  89 45 F8           mov  [ebp-8],eax
    //      67A8A4  E9 96 04 00 00     jmp  0x67AD3F
    //    构造器：无自有 ctor —— 直接以 TSearchMon.Create(sub_6735D4) 构造（工厂传本类 classref，
    //    Delphi 据 VMT 分配 size 0x55C、跑基类 ctor 体）。C# 侧 ctor = base()（= SearchMon()）。
    //
    // ── fail-closed（VMT 差分 child 0x66D5E0 vs parent TSearchMon 0x66D320）──────────────
    //   本类覆写 4 个 TSearchMon【攻击 AI 新虚槽】（父类中 slot137-140 为 0x4035A4 抽象桩，本类给出
    //   具体实现）；这 12 个新虚槽（slot131-142）在 C# 扁平化层级中无对应虚方法入口 → 全部 fail-closed：
    //     slot137 +0x224 -> 0x673EF4：读 m_TargetCret(+0x344)，call sub_7682D8 求朝向，查表
    //        [0x7D6BE0 + dir*8] 做伤害计算（出手方向/伤害）。
    //     slot138 +0x228 -> 0x673F84：读 m_TargetCret，算 |dx|/|dy| 与 1 比较（贴身射程谓词）。
    //     slot139 +0x22C -> 0x673E8C：写 m_btDirection(+0x154=dl)，call [vmt+0xCC] 出手、call sub_6733D0。
    //     slot140 +0x230 -> 0x673DBC：写 m_btDirection，call [vmt+0xCC]，伤害 ×15/10（伤害变体）。
    //   槽族语义见 SearchMon.cs 的 (C) 段登记；这些槽由 TSearchMon.Run（本身 fail-closed）驱动，
    //   且引用 +0x28C/+0x290 属性、[vmt+0xCC]、sub_6733D0/sub_7682D8、表 0x7D6BE0 等未命名落点，
    //   忠实移植会臆造 → 宁缺毋滥，不覆写。
    //
    // 综上：本类忠实落地【类存在 + 构造器(经 SearchMon)】，使 race 138 脱离工厂 default sink；
    //   4 个攻击 AI 覆写 fail-closed（附 VA+原因）。原先该 race 落 default(0x67AE5E) → nil，怪不出现。
    public class WolfStickIceMon : SearchMon
    {
        public WolfStickIceMon() : base()
        {
        }
    }
}
