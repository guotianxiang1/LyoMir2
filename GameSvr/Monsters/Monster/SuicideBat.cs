using SystemModule;

namespace GameSvr
{
    // 战神字节证据 (Tier-1)：race 137 = TSuicideBat，classref 0x66475C -> VMT 0x6647A8，
    //   size 0x4E8(1256)，parent = TATMonster(VMT 0x65E7E4，= C# AtMonster)。
    //   工厂 sub_679F8C：race137-0xB=0x7E -> idx 0x37=55 -> jt[55]=0x67A881，case body：
    //     67A881  B2 01              mov  dl,1
    //     67A883  A1 5C 47 66 00     mov  eax,[0x66475C]   ; classref -> TSuicideBat
    //     67A888  E8 97 41 00 00     call 0x66BA24         ; TSuicideBat.Create
    //     67A88D  89 45 F8           mov  [ebp-8],eax
    //     67A890  E9 AA 04 00 00     jmp  0x67AD3F         ; case 内无额外 RNG/字段写
    //
    // 构造器 sub_66BA24 全文（父 Create 之后）：
    //   66BA3D  E8 56 B0 FF FF          call 0x666A98               ; TATMonster.Create（= base()）
    //   66BA42  B8 DC 05 00 00 / E8..   mov eax,0x5DC / call Random ; Random(1500)
    //   66BA4C  05 C4 09 00 00          add eax,0x9C4               ; +2500
    //   66BA51  89 46 7C                mov [esi+0x7C],eax          ; m_dwSearchTime = Random(1500)+2500
    //   66BA54  33 C0 / 89 86 80 00 00 00  mov [esi+0x80],0         ; ← 见 fail-closed
    //   66BA5C  C7 46 78 0C 00 00 00    mov dword [esi+0x78],0xC    ; m_nViewRange = 12
    //   偏移锚点：+0x7C = m_dwSearchTime（race 108 case 证据：TATMonster.Create 内
    //     `mov eax,0x5DC / Random / add eax,0x5DC / mov [esi+0x7C],eax` = C# AtMonster
    //     m_dwSearchTime=Random(1500)+1500；本类在父 Create 之后再写 +0x7C，故 base() 先耗一次
    //     Random(1500)，本类再耗一次——下方按此顺序保持 RNG 一致）。+0x78 = m_nViewRange。
    //
    // ⛔ fail-closed（原生证据齐全，C# 无落点，不臆造）：
    //  (1) [+0x80]=0（介于 m_dwSearchTime +0x7C 与 m_dwSearchEnemyTick +0x88 之间）身份未确认；
    //      基类默认即 0，C# 侧不额外写。
    //  (2) VMT 槽 +0x204（slot129，父 TCreature sub_71D934）覆写 sub_66B958 = 自爆：
    //        66B977  call [vmt+0xD8]                 ; SendRefMsg(wIdent=0x2905, 5×0, cx=0) 爆炸广播
    //        66B985  mov [self+0x2AC],0              ; m_WAbil(+0x264) 内 HP 归零
    //        66B98E  dmg = Random([+0x290]-[+0x28C]+1)+[+0x28C]  ; DC(min..max) 伤害
    //        遍历 m_VisibleActors([+0x388])：0x7743E0(范围,cx=1) && !0x772DA8(死/魂) &&
    //        0x767498(可攻击) -> call [target.vmt+0xA8](0,0x3E8,ecx=self,edx=dmg) + 0x76B4F8(0x2BC,dmg)
    //      即「自杀蝙蝠」死亡自爆 AoE。依赖 slot129 的 C# 虚方法身份、[vmt+0xA8]、0x76B4F8、
    //      0x7743E0、0x767498、RM 0x2905、m_WAbil 布局，多点未定，忠实移植会臆造，故不覆写。原生起点 0x66B958。
    //  (3) VMT 槽 +0xC8（slot50，父 TCreature sub_76B3C8）覆写 sub_66BAB4：对 edx 值域做闸后转父。身份未定。
    //  (4) VMT 槽 +0x198（slot102，父 TAnimal sub_71F824）覆写 sub_66BA80：`xor eax,eax; ret 4`（恒返回 nil/0）。身份未定。
    //  (5) VMT 槽 +0x19C/+0x1A0/+0x1A4（slot103-105）、+0x1B4（slot109）、+0x1E8（slot122）
    //      覆写 sub_66BA20/66BA1C/66BA18/66BAD4/66BA8C：一组属性/状态取值虚方法，身份未定，fail-closed。
    //
    // 结论：C# 侧行为 = AtMonster（父类搜敌 AI 正确）。具名类型使 race 137 脱离 default sink，
    //   并为死亡自爆机制（slot129 +0x204）留下确定挂载点。
    public class SuicideBat : AtMonster
    {
        public SuicideBat() : base()
        {
            m_dwSearchTime = M2Share.RandomNumber.Random(1500) + 2500;
            m_nViewRange = 12;
        }
    }
}
