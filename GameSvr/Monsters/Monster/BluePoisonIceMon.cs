namespace GameSvr
{
    // ✅ 战神字节证据 (Tier-1)：race 142 = TBluePoisonIceMon，classref 0x66E0C8 → VMT 0x66E114,
    //    size 0x560（比 TSearchMon 多 1 个 dword 新字段 +0x55C，共享 ctor 不写它 → 零初始化），
    //    classname "TBluePoisonIceMon"，parent = TSearchMon（= C# SearchMon）。
    //    工厂 sub_679F8C handler 0x67A8E5 全文（20 字节，无额外 case-body 逻辑）：
    //      67A8E5  B2 01              mov  dl,1
    //      67A8E7  A1 C8 E0 66 00     mov  eax,[0x66E0C8]   ; classref -> TBluePoisonIceMon
    //      67A8EC  E8 E3 8C FF FF     call 0x6735D4         ; = TSearchMon.Create（共享 ctor）
    //      67A8F1  89 45 F8           mov  [ebp-8],eax
    //      67A8F4  E9 46 04 00 00     jmp  0x67AD3F
    //    构造器：无自有 ctor —— 直接以 TSearchMon.Create(sub_6735D4) 构造。C# ctor = base()（SearchMon()）。
    //    新字段 [+0x55C]：零初始化、唯一消费者为下列 fail-closed 覆写 → 不落 C# 字段（避免死代码）。
    //
    // ── fail-closed（VMT 差分 child 0x66E114 vs parent TSearchMon 0x66D320）──────────────
    //  (1) slot6 +0x018 Operate -> 0x6747BC：`call base.Operate(0x71DEE8)` 后，若 msg.wIdent==0x28B1
    //      (=10417=NativeMagicProducerPushIdent) 且 msg[+0xC] 非空且目标未死且 self.m_boStickMode(+0x75)==0，
    //      则 `call [target.vmt+0xA4]`(edx=msg[+2], ecx=msg[+8]) 把消息转发给目标的虚槽 +0xA4。
    //      虽然 Operate 是 C# 虚方法，但 (a) 目标虚槽 +0xA4(idx41) 在 C# 无映射；(b) 该覆写返回值
    //      不明确（base.Operate 的 al 被覆盖、末路径未显式置 eax）。忠实移植会臆造 → fail-closed
    //      （不覆写；退化为 SearchMon→AnimalObject.Operate，仅丢失 RM 10417 转发一条支路）。
    //  (2) 覆写 5 个 TSearchMon 攻击 AI 新虚槽（slot136-140），C# 无对应虚入口：
    //      slot136 +0x220 -> 0x674844   slot137 +0x224 -> 0x674870   slot138 +0x228 -> 0x6748C8
    //      slot139 +0x22C -> 0x6747B8   slot140 +0x230 -> 0x67465C
    //      槽族语义与依赖见 SearchMon.cs 的 (C) 段与 WolfStickIceMon.cs（同族攻击虚表，本怪为蓝毒变体）。
    //
    // 综上：忠实落地【类存在 + 构造器(经 SearchMon)】，使 race 142 脱离工厂 default sink；
    //   Operate 与 5 个攻击 AI 覆写 fail-closed。原先该 race 落 default(0x67AE5E) → nil，怪不出现。
    public class BluePoisonIceMon : SearchMon
    {
        public BluePoisonIceMon() : base()
        {
        }
    }
}
