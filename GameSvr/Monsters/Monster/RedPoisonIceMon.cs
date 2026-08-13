namespace GameSvr
{
    // ✅ 战神字节证据 (Tier-1)：race 141 = TRedPoisonIceMon，classref 0x66DDFC → VMT 0x66DE48,
    //    size 0x55C（自身无新字段），classname "TRedPoisonIceMon"，parent = TSearchMon（= C# SearchMon）。
    //    工厂 sub_679F8C handler 0x67A8D1 全文（20 字节，无额外 case-body 逻辑）：
    //      67A8D1  B2 01              mov  dl,1
    //      67A8D3  A1 FC DD 66 00     mov  eax,[0x66DDFC]   ; classref -> TRedPoisonIceMon
    //      67A8D8  E8 F7 8C FF FF     call 0x6735D4         ; = TSearchMon.Create（共享 ctor）
    //      67A8DD  89 45 F8           mov  [ebp-8],eax
    //      67A8E0  E9 5A 04 00 00     jmp  0x67AD3F
    //    构造器：无自有 ctor —— 直接以 TSearchMon.Create(sub_6735D4) 构造。C# ctor = base()（SearchMon()）。
    //
    // ── fail-closed（VMT 差分 child 0x66DE48 vs parent TSearchMon 0x66D320）──────────────
    //   覆写 5 个 TSearchMon 攻击 AI 新虚槽（slot136-140）；C# 无对应虚入口：
    //     slot136 +0x220 -> 0x6745D4   slot137 +0x224 -> 0x674600   slot138 +0x228 -> 0x674658
    //     slot139 +0x22C -> 0x6745D0   slot140 +0x230 -> 0x6744C0
    //   槽族语义与依赖见 SearchMon.cs 的 (C) 段与 WolfStickIceMon.cs（同族攻击虚表，本怪为红毒变体）；
    //   由 fail-closed 的 TSearchMon.Run 驱动、引用未命名落点，忠实移植会臆造 → 不覆写。
    //
    // 综上：忠实落地【类存在 + 构造器(经 SearchMon)】，使 race 141 脱离工厂 default sink；
    //   5 个攻击 AI 覆写 fail-closed。原先该 race 落 default(0x67AE5E) → nil，怪不出现。
    public class RedPoisonIceMon : SearchMon
    {
        public RedPoisonIceMon() : base()
        {
        }
    }
}
