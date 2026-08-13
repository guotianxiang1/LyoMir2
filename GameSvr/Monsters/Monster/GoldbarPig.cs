namespace GameSvr
{
    // ✅ 战神字节证据 (Tier-1)：race 249 = TGoldbarPig，VMT 0x665EB4，size 0x4E8(1256)，
    //    parent = TATMonster(0x65E7E4，同为 0x4E8) —— 子类【零新增字段】。
    //    工厂跳表 sub_679F8C：索引表[249-0xB=0xEE]=0x75=117 ; jt[117]=0x67AD30 ; case 全文：
    //      67AD30  B2 01              mov  dl,1
    //      67AD32  A1 68 5E 66 00     mov  eax,[0x665E68]   ; classref(=VMT-0x4C) -> TGoldbarPig
    //      67AD37  E8 F8 24 00 00     call 0x66D234         ; TGoldbarPig.Create
    //      67AD3C  89 45 F8           mov  [ebp-8],eax
    //      67AD3F  ...                汇入公共尾部
    //    case 内无额外 RNG、无额外字段写。
    //
    // 构造器 sub_66D234 全文（纯转调父类，无自定义字段）：
    //   66D249  33 D2              xor  edx,edx
    //   66D24B  8B C6              mov  eax,esi
    //   66D24D  E8 46 98 FF FF     call 0x666A98        ; = TATMonster.Create
    //   66D252..66D26C             epilogue / ret
    //   => 构造 == AtMonster()（父 ctor 内 m_dwSearchTime = Random(1500)+1500）。
    //
    // 唯一 VMT 覆写：slot[127] +0x1FC = 0x66D270（父 TAnimal 处为 0x71F46C）。
    // +0x1FC 是散金入口的薄转发器（TBaseObject.cs 5145 行已考证：123 个怪物 VMT 都放
    // sub_71F46C，它把 [ebp+0xC] 与 [ebp+8] 原样再压一遍后尾调 sub_71FA20=ScatterGolds）。
    // 覆写体 sub_66D270 逐字节：
    //   66D270  55 8B EC           push ebp / mov ebp,esp
    //   66D273  53                 push ebx                 ; 保存 ebx
    //   66D274  8A 5D 0C           mov  bl,[ebp+0xC]        ; 取【左】栈参(second-arm 开关)
    //   66D277  53                 push ebx                 ; 左参原样透传
    //   66D278  6A 00              push 0                   ; 【右】栈参强制 0
    //   66D27A  E8 ED 21 0B 00     call 0x71F46C            ; inherited
    //   66D27F  5B 5D C2 08 00     pop ebx / pop ebp / ret 8
    //   Delphi register 约定下多余实参【自左向右】入栈，所以先压的是左参、后压的是右参；
    //   右参正是 sub_71FA20 的 [ebp+8]。
    //
    // 语义：sub_71FA20 里 [ebp+8] 只管 0x71FFBD 那道 3000 上钳
    //   （0x71FFB7 cmp byte [ebp+8],0 / je 0x71FFCD 直接跳过 cmp 0xBB8 / mov 0xBB8；
    //    0x71FFD1 的 idiv 落在 0x71FFCD 汇合点，钳不钳都要除）。
    //   怪物 Die 的两个调用点(0x71E3C4 / 0x71E3DA)恒 push 1，本类把它改写成 0，
    //   => 【金条猪死亡时不吃 3000 金上限】，按全额金币分堆撒落；疲劳档位除法照常。
    // fail-closed：VMT 差异集仅此一槽，构造器无自定义写，故不臆造任何其它行为。
    public class GoldbarPig : AtMonster
    {
        public GoldbarPig() : base()
        {
        }

        protected override bool NativeScatterGoldCapped => false;
    }
}
