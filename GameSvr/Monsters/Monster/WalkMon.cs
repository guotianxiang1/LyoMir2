namespace GameSvr
{
    // 战神字节证据 (Tier-1)：race 98 = TWalkMon，classref 0x67EF94 -> VMT 0x67EFE0，
    //   size 0x508(1288)，parent = TAnimal(VMT 0x71D51C，= C# AnimalObject)。
    //   工厂 sub_679F8C：race98-0xB=0x57 -> idx 0x1B=27 -> jt[27]=0x67A62B，case body：
    //     67A62B  B2 01              mov  dl,1
    //     67A62D  A1 94 EF 67 00     mov  eax,[0x67EF94]   ; classref -> TWalkMon
    //     67A632  E8 D5 0C 00 00     call 0x68130C         ; TWalkMon.Create
    //     67A637  89 45 F8           mov  [ebp-8],eax
    //     67A63A  E9 00 07 00 00     jmp  0x67AD3F         ; case 内无额外 RNG/字段写
    //
    // 构造器 sub_68130C 全文（父 Create 之后）：
    //   681325  E8 FE C4 09 00              call 0x71D828             ; TAnimal.Create（= base()）
    //   68132A  C6 86 78 01 00 00 62        mov  byte [esi+0x178],0x62 ; m_btRaceServer = 98
    //   681331  66 C7 86 6C 02 00 00 63 00  mov  word [esi+0x26C],0x63 ; m_btAntiPoison = 99
    //   68133A  C7 86 54 04 00 00 38 00 00 00  mov dword [esi+0x454],0x38 ; m_nTargetX = 56
    //   681344  C7 86 58 04 00 00 2A 00 00 00  mov dword [esi+0x458],0x2A ; m_nTargetY = 42
    //   68134E  lea eax,[esi+0x4D8] / ecx=0 / edx=0x20 / call 0x403B2C ; FillChar([+0x4D8],32,0)
    //   681360  lea eax,[esi+0x4F8] / ecx=0 / edx=8   / call 0x403B2C ; FillChar([+0x4F8],8,0)
    //   偏移锚点：+0x178=m_btRaceServer；+0x26C=m_btAntiPoison（FireDragon/SpitSpider 印证）；
    //     +0x454=m_nTargetX、+0x458=m_nTargetY（FireDragon/SoccerBall 印证：SoccerBall 写 +0x454=-1）。
    //
    // ⛔ fail-closed（原生证据齐全，C# 无落点，不臆造）：
    //  (1) 新字段区 [+0x4D8..+0x4F8)（32B 数组）与 [+0x4F8..+0x500)（8B）由 FillChar 清零；
    //      C# AnimalObject 无这些字段，清零 = 默认，C# 侧不额外写。
    //  (2) 本类共 10 个 VMT 覆写，均读写 (1) 的新字段或身份未定，忠实移植会臆造，故全部保留父实现：
    //        slot6  +0x018 sub_68172C (父 0x71DEE8)   slot12 +0x030 sub_681858 (父 0x71F0F4)
    //        slot29 +0x074 sub_681854 (父 0x767494)   slot33 +0x084 sub_681688 (父 0x71E2BC)
    //        slot34 +0x088 sub_6817C0 (父 TAnimal.Run 0x71E50C) —— WalkMon 主 AI
    //        slot102 +0x198 sub_68194C (父 0x71F824)  slot103 +0x19C sub_681924 (父 0x71F840)
    //        slot104 +0x1A0 sub_681920 (父 0x71F884)  slot105 +0x1A4 sub_68191C (父 0x71F8A8)
    //        slot109 +0x1B4 sub_681928 (父 0x76C35C)
    //
    // 结论：C# 侧行为 = AnimalObject。具名类型使 race 98 脱离 default sink，并为上述 10 个虚槽
    //   与新字段区留下确定挂载点。
    public class WalkMon : AnimalObject
    {
        public WalkMon() : base()
        {
            m_btRaceServer = 98;
            m_btAntiPoison = 99;
            m_nTargetX = 56;
            m_nTargetY = 42;
        }
    }
}
