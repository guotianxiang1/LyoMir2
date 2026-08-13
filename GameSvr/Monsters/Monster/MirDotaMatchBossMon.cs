namespace GameSvr
{
    // ✅ 战神字节证据 (Tier-1)：race 244 = TMirDotaMatchBossMon，classref 0x6625B8 -> VMT 0x662604，
    //    size 1252(0x4E4)，parent = TAnimal(0x71D51C)。
    //    工厂 sub_679F8C：索引表[244-0xB=0xE9]=0x71=113 ; jt[113]=0x67ACEC ; case body 全文：
    //      67ACEC  B2 01 / A1 B8 25 66 00 / E8 30 E7 FE FF / 89 45 F8 / EB 42(jmp 0x67AD3F)
    //      classref [0x6625B8] 唯一加载点；ctor 0x669428 唯一 E8 调用者。case 内无额外 RNG/字段写。
    //
    // 构造器 sub_669428 全文（父 Create 之后）：
    //   00669441  E8 E2 43 0B 00        call 0x71D828         ; = TAnimal.Create（= C# AnimalObject()）
    //   00669446  C6 86 C8 04 00 00 00  mov byte [esi+0x4C8],0  ; TAnimal 字段,清零=默认
    //   0066944D  C7 46 78 0C 00 00 00  mov dword [esi+0x78],0xC ; m_nViewRange = 12
    //   00669454  33 C0 / 89 86 D8 04 00 00  mov dword [esi+0x4D8],0 ; 新字段,清零=默认
    //   0066945E  A1 8C 1E 42 00 / E8..  mov eax,[0x421E8C] / call 0x404660 ; 新建一个对象(TList/TStringList)
    //   00669468  89 86 E0 04 00 00     mov [esi+0x4E0],eax     ; 新字段 = 该对象（见 fail-closed）
    //   +0x78 = m_nViewRange。
    //
    // ── fail-closed ────────────────────────────────────────────────────────────
    //   (新字段) dword[+0x4E0] = ctor 内 new 出来的一个容器对象（0x404660 = TObject/TList.Create 家族），
    //     C# 无对应命名字段与类型信息，忠实构造会臆造，故不建该字段（[+0x4D8] 清零=默认，不写）。
    //   (VMT 覆写 16 项，vs TAnimal)：+0x00C/+0x010(3/4)、+0x078(30 Init)、+0x084(33 Die)、
    //     +0x088(34 Run)、+0x094(37)、+0x0A8(42)、+0x0C8(50)、+0x104(65)、+0x19C/1A0/1A4(103-105)、
    //     +0x1AC/+0x1B0(107/108)、+0x1B4(109 受击伤害选择器)、+0x1E8(122 CanAdd：base && !sub_669B40，
    //     拒绝表在未移植 helper 里，非内联)。均依赖新字段 [+0x4D8]/[+0x4E0] 或未定 helper/无 C# 入口，
    //     全部保留父实现。
    //   语义：Mir Dota 赛事 Boss；C# 忠实 = 纯 AnimalObject + m_nViewRange=12。原先 race 244 落 default → nil。
    public class MirDotaMatchBossMon : AnimalObject
    {
        public MirDotaMatchBossMon() : base()
        {
            m_nViewRange = 12;
        }
    }
}
