namespace GameSvr
{
    // ✅ 战神字节证据 (Tier-1)：race 143 = TQuickKnifeIceMon，classref 0x66E394 → VMT 0x66E3E0,
    //    size 0x564（比 TSearchMon 多 2 个 dword 新字段 +0x55C/+0x560），classname "TQuickKnifeIceMon"，
    //    parent = TSearchMon（= C# SearchMon）。
    //    工厂 sub_679F8C handler 0x67A8F9 全文（20 字节，无额外 case-body 逻辑）：
    //      67A8F9  B2 01              mov  dl,1
    //      67A8FB  A1 94 E3 66 00     mov  eax,[0x66E394]   ; classref -> TQuickKnifeIceMon
    //      67A900  E8 C7 9F FF FF     call 0x6748CC         ; TQuickKnifeIceMon.Create（自有 ctor）
    //      67A905  89 45 F8           mov  [ebp-8],eax
    //      67A908  E9 32 04 00 00     jmp  0x67AD3F
    //
    // 构造器 sub_6748CC 全文（本 race 是六 Ice 怪中唯一有自有 ctor 者）：
    //   6748E5  E8 EA EC FF FF        call 0x6735D4  ; = TSearchMon.Create (= base() = SearchMon())
    //   6748EA  C7 46 78 07 00 00 00  mov dword [esi+0x78],7    ; m_nViewRange = 7（覆盖父类的 4）
    //   6748F1  C7 86 5C 05 00 00 04..  mov dword [esi+0x55C],4  ; ← 新字段（无 C# 落点）
    //   偏移锚点：+0x78=m_nViewRange（AttackIceTower.cs:26）。忠实落地：m_nViewRange=7。
    //   新字段 [+0x55C]=4 / [+0x560]（零初始化）：唯一消费者为下列 fail-closed 攻击虚槽 → 不落 C# 字段。
    //
    // ── fail-closed（VMT 差分 child 0x66E3E0 vs parent TSearchMon 0x66D320）──────────────
    //   覆写 3 个 TSearchMon 攻击 AI 新虚槽（slot137-139，不覆写 slot140）；C# 无对应虚入口：
    //     slot137 +0x224 -> 0x674B7C   slot138 +0x228 -> 0x674B80   slot139 +0x22C -> 0x674918
    //   槽族语义与依赖见 SearchMon.cs 的 (C) 段与 WolfStickIceMon.cs（同族攻击虚表，本怪为快刀变体）；
    //   由 fail-closed 的 TSearchMon.Run 驱动、引用未命名落点，忠实移植会臆造 → 不覆写。
    //
    // 综上：忠实落地【类存在 + 构造器(m_nViewRange=7)】，使 race 143 脱离工厂 default sink；
    //   3 个攻击 AI 覆写 fail-closed。原先该 race 落 default(0x67AE5E) → nil，怪不出现。
    public class QuickKnifeIceMon : SearchMon
    {
        public QuickKnifeIceMon() : base()
        {
            m_nViewRange = 7;   // 6748EA  mov dword [esi+0x78],7
        }
    }
}
