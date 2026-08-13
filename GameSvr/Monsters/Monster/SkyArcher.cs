namespace GameSvr
{
    // 战神字节证据 (Tier-1)：race 99 = TSkyArcher，classref 0x67F21C -> VMT 0x67F268，
    //   size 0x4DC(1244)，parent = TAnimal(VMT 0x71D51C，= C# AnimalObject)。
    //   工厂 sub_679F8C：race99-0xB=0x58 -> idx 0x1C=28 -> jt[28]=0x67A63F，case body：
    //     67A63F  B2 01              mov  dl,1
    //     67A641  A1 1C F2 67 00     mov  eax,[0x67F21C]   ; classref -> TSkyArcher
    //     67A646  E8 0D 13 00 00     call 0x681958         ; TSkyArcher.Create
    //     67A64B  89 45 F8           mov  [ebp-8],eax
    //     67A64E  E9 EC 06 00 00     jmp  0x67AD3F         ; case 内无额外 RNG/字段写
    //
    // 构造器 sub_681958 全文（父 Create 之后 4 个写）：
    //   681971  E8 B2 BE 09 00              call 0x71D828          ; TAnimal.Create（= base()）
    //   681976  C6 86 78 01 00 00 63        mov  byte [esi+0x178],0x63 ; m_btRaceServer = 99
    //   68197D  C7 46 78 07 00 00 00        mov  dword[esi+0x78],7     ; m_nViewRange = 7
    //   681984  C6 86 AC 03 00 00 01        mov  byte [esi+0x3AC],1    ; ← 见 fail-closed
    //   68198B  33 C0 / 89 86 D8 04 00 00   mov  dword[esi+0x4D8],0    ; ← 新字段，见 fail-closed
    //   偏移锚点：+0x178 = m_btRaceServer（NativeTimedAbilityCombatConsumer.cs:51 `RaceOffset=0x178`
    //     及 [player+0x178] 多处印证）；+0x78 = m_nViewRange（同 BigHeartMonster/ArmLightGuard）。
    //
    // VMT 槽 +0x020（slot8）覆写 sub_6819B0 = IsAttackTarget（父 TCreature sub_7671F0；
    //   槽身份见 TBaseObject.NativeProperTargetGate.cs:46 `call [self.vmt+0x20]=IsAttackTarget`）。
    //   sub_6819B0 全文（eax=self，edx=target，返回 al）：
    //     6819B0  B1 01              mov  cl,1
    //     6819B2  8A 82 78 01 00 00  mov  al,[edx+0x178]   ; target.m_btRaceServer
    //     6819B8  3C 32              cmp  al,0x32
    //     6819BA  72 04              jb   0x6819C0          ; race<50 -> False
    //     6819BC  3C 63              cmp  al,0x63
    //     6819BE  75 05              jne  0x6819C5          ; race!=99 -> 保持 cl=1 -> True
    //     6819C0  33 C9              xor  ecx,ecx           ; race==99 -> False
    //     6819C5  8B C1 / C3         mov eax,ecx / ret
    //   即：只攻击 m_btRaceServer 在 [50,255] 且 != 99 的目标（不打玩家 race=0、不打同族 99）。下方已 1:1 覆写。
    //
    // ⛔ fail-closed（有原生证据，C# 无落点，不臆造）：
    //  (1) [+0x3AC] 这个 bool（ctor 置 1）在 C# 侧无对应字段。仅记录，不补字段。
    //  (2) 新字段 [+0x4D8]（父 size 0x4D8 之后唯一新槽，object 指针，初值 0）无 C# 对应。
    //  (3) VMT 槽 +0x088（slot34 = Run，父 TAnimal sub_71E50C）覆写 sub_6819DC 是本类的
    //      远程搜敌-攻击 AI，通篇读写新字段 [+0x4D8] 并调 sub_768060 / sub_76719C /
    //      sub_681B28 / [vmt+0x80] 等未定身份的 helper。缺 (2) 的字段与这些 helper 的 C# 落点，
    //      忠实移植会臆造，故 Run 不覆写（暂用 AnimalObject.Run；本类 IsAttackTarget 仍生效）。
    //      原生 Run 全文起点 0x6819DC，留待补齐 [+0x4D8] 字段与 helper 后再移植。
    public class SkyArcher : AnimalObject
    {
        public SkyArcher() : base()
        {
            m_btRaceServer = 99;
            m_nViewRange = 7;
        }

        public override bool IsAttackTarget(TBaseObject BaseObject)
        {
            var bt = BaseObject.m_btRaceServer;
            return bt >= 0x32 && bt != 0x63;
        }
    }
}
