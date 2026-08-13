using SystemModule;

namespace GameSvr
{
    // ✅ 战神字节证据 (Tier-1)：race 135 = TFireDragon，VMT 0x663FE8，size 0x4D8，
    //    parent = TAnimal(0x71D51C)。工厂 sub_679F8C 索引表[135-0xB=0x7C]=0x35=53；
    //    jt[53]=0x67A859；case body 全文（0x67A859..0x67A86C）：
    //      67A859  B2 01              mov  dl,1
    //      67A85B  A1 9C 3F 66 00     mov  eax,[0x663F9C]   ; classref -> TFireDragon
    //      67A860  E8 23 0A FF FF     call 0x66B288         ; TFireDragon.Create
    //      67A865  89 45 F8           mov  [ebp-8],eax
    //      67A868  E9 D2 04 00 00     jmp  0x67AD3F         ; 汇入公共尾部，case 内无额外 RNG
    //
    // 构造器 sub_66B288 全文（call 父 TAnimal.Create 后 5 个字段写）：
    //   66B2A1  E8 82 25 0B 00           call 0x71D828              ; TAnimal.Create
    //   66B2A6  C7 46 78 01 00 00 00     mov  dword [esi+0x78],1    ; m_nViewRange = 1
    //   66B2AD  C6 86 E1 02 00 00 01     mov  byte  [esi+0x2E1],1   ; m_boSuperMan = true
    //   66B2B4  66 C7 86 6C 02 00 00 63 00
    //                                    mov  word  [esi+0x26C],0x63; m_btAntiPoison = 99
    //   66B2BD  C6 46 58 01              mov  byte  [esi+0x58],1    ; ← 见下方 fail-closed
    //   66B2C1  C6 46 75 01              mov  byte  [esi+0x75],1    ; m_boStickMode = true
    //
    // 偏移锚点（本轮独立复核，不是沿用旧注释）：
    //   +0x78  = m_nViewRange   —— TBigHeartMon.Create 0x68108A `mov dword [esi+0x78],0x10`
    //            对上 C# BigHeartMonster `m_nViewRange = 16`。
    //   +0x2E1 = m_boSuperMan   —— TSoccerBall.Create sub_6810E4 三个写全部对上 C# SoccerBall：
    //            0x681102 [+0x47E]=0 = m_boAnimal=false；0x681109 [+0x2E1]=1 = m_boSuperMan=true；
    //            0x681118 [+0x454]=-1 = m_nTargetX=-1。
    //   +0x26C = m_btAntiPoison —— TSpitSpider 吐毒 helper 0x666D2F
    //            `0F B7 86 6C 02 00 00  movzx eax,word [esi+0x26C]` / `83 C0 14 add eax,0x14`
    //            / `E8 .. call Random` / `test eax,eax / jne` 就是 C# SpitSpider.SpitAttack 的
    //            `Random(BaseObject.m_btAntiPoison + 20) == 0`，esi 是被打者。原生按 word 读，
    //            C# 声明成 byte，本类写入值 99 在 byte 范围内。
    //   +0x75  = m_boStickMode  —— 与 IceDoor.cs 既有锚点一致。
    //
    // ⛔ fail-closed：[+0x58] 这个 bool 在 C# 侧【无对应字段】，故不写。
    //    全镜像只有 5 个写入点（`C6 46 58 01`）：0x63D8D2(TMerchant.Create)、
    //    0x66B2BD(本类)、0x66D14A(TFriendAnimal)、0x684A4D(TCastleDoor)、0x684ED5(TWallStructure)；
    //    唯一读取点是 0x76B735 `80 7E 58 00  cmp byte [esi+0x58],0` + `jne 0x76B905`，
    //    位于 TCreature 虚槽 [vmt+0x10] 的实现 sub_76B6F0 里（用 [esi+0x18] 作节拍、除以 0x14）。
    //    C# 既没有这个字段也没有 sub_76B6F0 对应的 [vmt+0x10] 实现，凭空造一个 bool 只会是死代码，
    //    所以这里只记录事实、不实现。
    //
    // 唯一覆写：VMT 槽 +0x200 = AttackTarget（父 TAnimal 实现 sub_71E914，
    // 其 0x71E945/0x71E94B 的 `sub edx,[ebx+0x35C]` / `cmp edx,[ebx+0x320]` 已被
    // NativeMonsterAiSearchAttack.cs:267 锚定为 AttackTarget）。
    public class FireDragon : AnimalObject
    {
        public FireDragon() : base()
        {
            m_nViewRange = 1;
            m_boSuperMan = true;
            m_btAntiPoison = 99;
            m_boStickMode = true;
        }

        // 战神 sub_66B250 全文（eax = self，无参，返回 al）：
        //   66B255  B3 01                    mov  bl,1                 ; result := True
        //   66B257  8B 90 2C 01 00 00 / 52   push [eax+0x12C]          ; m_nCurrX
        //   66B25E  8B 90 30 01 00 00 / 52   push [eax+0x130]          ; m_nCurrY
        //   66B265  6A 00 / 6A 00 / 6A 00    push 0 / push 0 / push 0
        //   66B26B  33 C9 / 8A 88 54 01 00 00
        //                                    movzx ecx,byte [eax+0x154]; m_btDirection
        //   66B273  66 BA 14 27              mov  dx,0x2714            ; RM_HIT = 10004
        //   66B279  FF 96 D8 00 00 00        call [esi+0xD8]           ; SendRefMsg
        //   66B27F  8B C3                    mov  eax,ebx              ; -> True
        // 这个「5 push + cx=wParam + dx=wIdent + call [vmt+0xD8]」的形状与 TWhiteSkeleton.Run
        // 0x667DDD..0x667DFD 逐条同形，而那一处 C# 写的正是
        // `SendRefMsg(RM_DIGUP, m_btDirection, m_nCurrX, m_nCurrY, 0, "")`，所以本处等价于下式。
        //
        // 注：原生 TAnimal.Run sub_71E50C 全函数（0x71E50C..0x71E860）内【没有】
        // `call [vmt+0x200]`，所以对 TAnimal 直系子类而言这个覆写本身就不由 Run 派发；
        // 全镜像 13 个 [vmt+0x200] 派发点里没有一个在 TAnimal 主循环上。C# 侧同样保持不派发。
        protected virtual bool AttackTarget()
        {
            SendRefMsg(Grobal2.RM_HIT, m_btDirection, m_nCurrX, m_nCurrY, 0, "");
            return true;
        }
    }
}
