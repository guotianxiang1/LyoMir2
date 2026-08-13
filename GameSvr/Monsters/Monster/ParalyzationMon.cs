namespace GameSvr
{
    // ✅ 战神字节证据 (Tier-1)：race 247 = TParalyzationMon，VMT 0x665C18，size 0x4E8，
    //    parent = TGasMothMonster(0x65F754)。工厂跳表 sub_679F8C：
    //      索引表[247-0xB=0xEC]=0x73=115 ; jt[115]=0x67AD0E ; case body 全文：
    //        67AD0E  B2 01              mov  dl,1
    //        67AD10  A1 CC 5B 66 00     mov  eax,[0x665BCC]   ; classref -> TParalyzationMon
    //        67AD15  E8 DE 24 00 00     call 0x66D1F8         ; TParalyzationMon.Create
    //        67AD1A  89 45 F8           mov  [ebp-8],eax
    //        67AD1D  E9 1D 06 00 00     jmp  0x67AD3F
    //    归属唯一性：classref [0x665BCC] 全 CODE 段仅 1 处加载(0x67AD10)；
    //    ctor sub_66D1F8 的 E8 调用者全扫 = 1 处(0x67AD15)。
    //
    // 构造器 sub_66D1F8 全文（无自定义字段，纯转调父类）：
    //   66D1F8  55 8B EC 53 56 ...     push ebp / prologue
    //   66D211  E8 CE 9E FF FF         call 0x6670E4         ; = TGasMothMonster.Create
    //   66D216..66D230                 epilogue / ret
    //   sub_6670E4(GasMoth ctor) 唯一自定义写是 [+0x78]=7 (m_nViewRange=7)，
    //   与 C# GasMothMonster 构造函数逐字一致。故本类构造 = GasMothMonster 构造。
    //
    // 唯一 VMT 覆写是 Attack(+0x204)=0x66D1EC，全文：
    //   66D1EC  55 8B EC              push ebp / mov ebp,esp
    //   66D1EF  E8 30 9F FF FF        call 0x667124         ; = TGasMothMonster 的 Attack(+0x204)
    //   66D1F4  5D C3                 pop ebp / ret
    //   —— 这是「空覆写，只调 inherited」：TParalyzationMon.Attack 与 TGasMothMonster.Attack
    //   在网线上完全等价。GasMoth 的 Attack 体在 C# 里被内联为 sub_4A9C78（经 AttackTarget
    //   调用，见 GasAttackMonster.cs 的 SHIELD 说明），本类原样继承，无需另写。
    //   因此「麻痹怪」与「毒蛾」在类层面行为一致，二者差异只来自 Monsters.DB 数据（魔法/毒设定），
    //   不在类里。fail-closed：不臆造额外的麻痹逻辑。
    public class ParalyzationMon : GasMothMonster
    {
        public ParalyzationMon() : base()
        {
        }
    }
}
