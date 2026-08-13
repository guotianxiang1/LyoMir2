namespace GameSvr
{
    // 战神字节证据 (Tier-1)：race 136 = TKingFireDragon，classref 0x664224 -> VMT 0x664270，
    //   size 0x4EC(1260)，parent = TAnimal(VMT 0x71D51C，= C# AnimalObject)。
    //   注意：父类是 TAnimal，并非 TFireDragon —— 它与 race 135 TFireDragon 是兄弟而非父子。
    //   工厂 sub_679F8C：race136-0xB=0x7D -> idx 0x36=54 -> jt[54]=0x67A86D，case body：
    //     67A86D  B2 01              mov  dl,1
    //     67A86F  A1 24 42 66 00     mov  eax,[0x664224]   ; classref -> TKingFireDragon
    //     67A874  E8 DF 2D 00 00     call 0x66B658         ; TKingFireDragon.Create
    //     67A879  89 45 F8           mov  [ebp-8],eax
    //     67A87C  E9 BE 04 00 00     jmp  0x67AD3F         ; case 内无额外 RNG/字段写
    //
    // 构造器 sub_66B658 全文（父 Create 之后）：
    //   66B671  E8 B2 21 0B 00          call 0x71D828             ; TAnimal.Create（= base()）
    //   66B676  C7 46 78 0C 00 00 00    mov  dword [esi+0x78],0xC ; m_nViewRange = 12
    //   66B67D  C6 86 E8 04 00 00 00    mov  byte [esi+0x4E8],0   ; 新字段（清零）
    //   66B684  C6 86 E9 04 00 00 00    mov  byte [esi+0x4E9],0   ; 新字段（清零）
    //   66B68B  33 C0 / 89 86 E0 04 00 00  mov dword [esi+0x4E0],0 ; 新字段（清零）
    //   66B695  33 C0 / 89 86 E4 04 00 00  mov dword [esi+0x4E4],0 ; 新字段（清零）
    //   偏移锚点：+0x78 = m_nViewRange（同 FireDragon/ArmLightGuard）。
    //
    // ⛔ fail-closed（原生证据齐全，C# 无落点，不臆造）：
    //  (1) 新字段 [+0x4E0]/[+0x4E4]/[+0x4E8]/[+0x4E9]（父 size 0x4D8 之后）清零初始化；
    //      C# AnimalObject 无这些字段，清零 = 默认，C# 侧不额外写。
    //  (2) 本类共 13 个 VMT 覆写（含 1 个新增虚槽），均身份未定或依赖复杂 helper，全部保留父实现：
    //        slot5  +0x014 sub_66B4E4 (父 0x76B42C)   slot33 +0x084 sub_66B81C (父 0x71E2BC)
    //        slot34 +0x088 sub_66B6F8 (父 TAnimal.Run 0x71E50C)
    //        slot42 +0x0A8 sub_66B894 (父 0x767A18)   slot50 +0x0C8 sub_66B6E0 (父 0x76B3C8)
    //        slot65 +0x104 sub_66B8AC (父 0x76CFC4)   slot103 +0x19C sub_66B654 (父 0x71F840)
    //        slot104 +0x1A0 sub_66B650 (父 0x71F884)  slot105 +0x1A4 sub_66B64C (父 0x71F8A8)
    //        slot122 +0x1E8 sub_66B6B8 (父 0x772F84)
    //        slot128 +0x200 sub_66B304 (父 TAnimal.AttackTarget 0x71E914) —— 喷火 AoE：
    //          遍历目标周围 ±2 矩形，逐格 GetMapObject 0x7784A8 + 可攻击 0x767498，播火焰特效
    //          (call 0x769258 cx=0x66)、按 DC([+0x28C]/[+0x290]) 掷伤，含中毒判定 0x772960 /
    //          [target.vmt+0x1B0] / [target.vmt+0x20C] / 0x76A894 / 0x76C01C。helper 多点未定，
    //          与 FireDragon 的简单 RM_HIT 形状不同，故不移植。原生起点 0x66B304。
    //        slot130 +0x208 sub_66B878 (父 0x71E208)
    //        slot131 +0x20C sub_66B2E0（新增虚槽，父无）：call 0x766060(0x1E,5,self,3,0,0x258,cx=0x283C)
    //          —— 某技能/特效广播，身份未定。
    //
    // 结论：C# 侧行为 = AnimalObject。具名类型使 race 136 脱离 default sink，并为喷火 AoE(slot128)、
    //   新增虚槽(slot131) 及新字段留下确定挂载点。
    public class KingFireDragon : AnimalObject
    {
        public KingFireDragon() : base()
        {
            m_nViewRange = 12;
        }
    }
}
