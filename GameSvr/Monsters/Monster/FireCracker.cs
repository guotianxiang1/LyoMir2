namespace GameSvr
{
    // ✅ 战神字节证据 (Tier-1)：race 149 = TFireCracker，classref 0x664C7C -> VMT 0x664CC8，
    //    size 1268(0x4F4)，parent = TAnimal(0x71D51C, size 1240=0x4D8)。
    //    工厂 sub_679F8C：索引表[149-0xB=0x8A]=0x43=67 ; jt[67]=0x67A971 ; case body 全文：
    //      67A971  B2 01              mov  dl,1
    //      67A973  A1 7C 4C 66 00     mov  eax,[0x664C7C]   ; classref -> TFireCracker
    //      67A978  E8 4B 12 FF FF     call 0x66BBC8         ; TFireCracker.Create
    //      67A97D  89 45 F8           mov  [ebp-8],eax
    //      67A980  E9 BA 03 00 00     jmp  0x67AD3F         ; case 内无额外 RNG/字段写
    //    归属唯一：classref [0x664C7C] 全 CODE 段 1 个加载点(0x67A973)；ctor 0x66BBC8 唯一 E8 调用者。
    //
    // 构造器 sub_66BBC8 全文（父 Create 之后）：
    //   66BBE1  E8 42 1C 0B 00        call 0x71D828        ; = TAnimal.Create（= C# AnimalObject()）
    //   66BBE6  33 C0 / 89 46 78      xor eax,eax / mov [esi+0x78],eax ; m_nViewRange = 0
    //   66BBEB  C6 86 58 01 00 00 01  mov byte [esi+0x158],1           ; ← 见 fail-closed(ctor)
    //   +0x78 = m_nViewRange（IceDoor/KingFireDragon 同源）。
    //
    // ── fail-closed ────────────────────────────────────────────────────────────
    // (ctor) byte[+0x158]=1：介于 m_btDirection(+0x154) 之后的 TAnimal 字段，C# 侧无已确证
    //   的命名落点；基类默认 0，本类置 1。宁缺毋滥，不臆造字段名/语义，不写。
    // (新字段) 本类自有 [+0x4D8..0x4F3]（父 size 0x4D8 之后 28 字节，被下方覆写读写）无 C# 落点。
    // (VMT 覆写 9 项，逐槽 diff vs TAnimal，全部依赖上述新字段/未定 helper/无入口槽，故全保留父实现)：
    //   +0x018(6)  Operate 0x66BFEC：处理消息 0x2724/0x283C，命中则 [tgt+0x2AC]=0 后 call 0x767504。
    //   +0x078(30) Initialize 0x66BFC4：[+0x4F0]=[+0x240] / [+0x240]=0 / [+0x2BC]=0 / base.Initialize。
    //   +0x084(33) Die 0x66BCAC：取名串([+0x4DC]?/[+0x34C]/[+0x354]) + call [tgt.vmt+0xB4]，鞭炮死亡广播。
    //   +0x088(34) Run 0x66C034：读 [+0x4D8]，(tick-[+0x4EC])>0x1D4C0(120000) 时 call 0x7685E0(MakeGhost)。
    //   +0x090(36) 0x66BF20：字符串/喊话构造(call 0x4C6F40)，槽身份未定。
    //   +0x19C/1A0/1A4(103-105) = `xor eax,eax;ret`：属性虚槽恒 0，C# 非虚，无入口。
    //   +0x1B4(109) 0x66C0B4：受击伤害选择器(kind 0xC8/0x7F 位表)，C# 拆成非虚 GetHit/MagStruckDamage。
    //   —— 依赖未命名新字段 [+0x4D8]/[+0x4DC]/[+0x4EC]/[+0x4F0] 与未定 helper，忠实移植会臆造，故不覆写。
    //
    // 语义：一枚“鞭炮”——viewRange=0（永不索敌），到时(120s)自灭并播死亡特效。
    //   原先 race 149 落工厂 default(0x67AE5E) → nil，鞭炮根本不出现。C# 侧行为退化为纯 AnimalObject
    //   + viewRange=0；具名类型使 race 149 脱离 default sink，并为上述机制留确定挂载点。
    public class FireCracker : AnimalObject
    {
        public FireCracker() : base()
        {
            m_nViewRange = 0;
        }
    }
}
