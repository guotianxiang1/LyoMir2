namespace GameSvr
{
    // ✅ 战神字节证据 (Tier-1)：race 130 = TTaoistEngine，classref 0x71A1B8 → VMT 0x71A204,
    //    size 0x5FC，classname "TTaoistEngine"，parent = TAIMon（= C# AiMon）。
    //    工厂 sub_679F8C handler 0x67A845 全文（20 字节，无额外 case-body 逻辑）：
    //      67A845  B2 01              mov  dl,1
    //      67A847  A1 B8 A1 71 00     mov  eax,[0x71A1B8]   ; classref -> TTaoistEngine
    //      67A84C  E8 07 1D 0A 00     call 0x71C558         ; TTaoistEngine.Create
    //      67A851  89 45 F8           mov  [ebp-8],eax
    //      67A854  E9 E6 04 00 00     jmp  0x67AD3F
    //
    // 构造器 sub_71C558 全文（Delphi 序幕/收尾略）：
    //   71C571  E8 A6 E9 FF FF        call 0x71AF1C  ; = TAIMon.Create (= base() = AiMon())
    //   71C576  C6 86 F0 05 00 00 01  mov byte [esi+0x5F0],1   ; ← 新字段（无 C# 落点）
    //   71C57D  C6 86 E3 02 00 00 01  mov byte [esi+0x2E3],1   ; m_boFixedHideMode = true
    //   71C584  E8 B7 BD CE FF        call 0x408340  ; GetTickCount
    //   71C589  89 86 F4 05 00 00     mov [esi+0x5F4],eax      ; ← 新字段 = tick
    //   偏移锚点：+0x2E3=m_boFixedHideMode（TBaseObject.cs:297/637 / Monster.cs:149 印证）。
    //   忠实落地：m_boFixedHideMode = true。
    //
    // ── fail-closed（有字节、C# 无落点，不臆造）─────────────────────────────────
    //  · 新字段 [+0x5F0](byte=1) / [+0x5F4](tick)：唯一消费者是 fail-closed 的 Run（见下），
    //    故不落 C# 字段（避免死代码）。
    //  · VMT 差分（child 0x71A204 vs parent TAIMon 0x719CF4）共 4 项真实覆写，全部 fail-closed：
    //     slot34 +0x088 Run -> 0x71C60C：入口即 `cmp byte [+0x5F0],0 / je`（门控读新字段），随后
    //        写 m_btDirection=5 / 清 m_boFixedHideMode / SendRefMsg(RM 0x27D8) / GetTickCount+2000 …
    //        整段逻辑以新字段 +0x5F0 为闸 → fail-closed（退化为 AnimalObject.Run）。
    //     slot35 +0x08C RecalcAbilitys -> 0x71C5AC (parent 0x71DF70=浮点属性重算)
    //     slot45 +0x0B4 -> 0x71C8C8 (parent 0x769910=主人链解析)
    //     slot130 +0x208 Struck -> 0x71C848：`call base.Struck(0x71E208)` 后，若 Hiter!=null 且
    //        sub_71AEF0(self, Hiter.x, Hiter.y) <= 1 则 call sub_71B19C(self)。sub_71AEF0/sub_71B19C
    //        身份未定（无 C# 落点）→ fail-closed（不臆造 helper 语义）。
    //
    // 综上：本类忠实落地【类存在 + 构造器(m_boFixedHideMode=true)】；4 个覆写全部 fail-closed。
    public class TaoistEngine : AiMon
    {
        public TaoistEngine() : base()
        {
            m_boFixedHideMode = true;   // 71C57D  mov byte [esi+0x2E3],1
        }
    }
}
