namespace GameSvr
{
    // ============================================================================
    // race-base 批：TAIMon / TSearchMon 两个缺失怪物基类【已在本批移植】(见
    //   GameSvr/Monsters/AiMon.cs、GameSvr/Monsters/SearchMon.cs) 之后，补齐它们
    //   8 个子类的 race 子类工厂映射。这些 race 原先因"父类未移植"被 RaceFactory_RaceA.cs:88
    //   登记为 fail-closed、落工厂 default sink(0x67AE5E `xor eax,eax` → nil)，怪根本不出现。
    //
    // 依据：AddCreature 工厂 sub_679F8C（VA 0x679F8C）——
    //   movzx eax,byte[monRec+0x14]      ; race
    //   add   eax,-0x0B                   ; race - 11
    //   cmp   eax,0xEE / ja 0x67AE5E      ; 越界 -> default sink
    //   mov   al,byte[eax+0x67A026]       ; 索引表 -> group
    //   jmp   dword[eax*4+0x67A115]       ; 跳表 -> handler
    // 每个 handler 形如 `mov dl,1 / mov eax,[classref] / call ctor / mov [ebp-8],eax
    //   / jmp 0x67AD3F`。本批 8 个 handler 均已逐字反汇编（20 字节整），确认【无】任何
    //   额外 case-body 逻辑（无 RNG / 无字段写），故每个 case 只需 `new 对应类()`。
    //   三件套 race→handler→classref→ctor 见各子类 .cs 文件头与下方注释。
    //
    // 防冲突：本文件是 race-base 独占的【新文件】；不改动 UsrEngn.cs 的热点 switch。
    //   现状 UsrEngn.cs AddBaseObject @2800 已接线 race-a 批：
    //       TryCreateRaceA(nMonRace, out Cert);
    //       if (Cert == null)
    //       switch (nMonRace) { ... }
    //   合并者把本批接入工厂，只需在 TryCreateRaceA 那一行之后【并列加一行】(务必带
    //   `Cert == null` 守卫，避免覆盖 race-a 已认领的结果)：
    //       if (Cert == null) TryCreateRaceBase(nMonRace, out Cert);
    //   命中即认领，未命中 Cert 保持 null 落回既有 switch，对既有 race 行为零影响。
    //
    // 说明：与 RaceFactory_RaceA.cs 一致，刻意写成普通方法（不加 partial 关键字）。UserEngine
    //   本就是 partial class，普通方法放入分部文件可独立编译。行为与工厂内联等价。
    // ============================================================================
    public partial class UserEngine
    {
        // 返回 true 表示本批认领并已构造该 race 的实例（本批 8 个 case body 均无 ctor 后逻辑）。
        private bool TryCreateRaceBase(int nMonRace, out TBaseObject cert)
        {
            cert = null;
            switch (nMonRace)
            {
                // race 129 handler 0x67A831 classref 0x719F2C ctor 0x71B4D0 TShadowHero
                //   parent TAIMon(=AiMon)。case body 无 ctor 后逻辑。ctor + Die 已核验（见 ShadowHero.cs）。
                case 129:
                    cert = new ShadowHero();
                    break;

                // race 130 handler 0x67A845 classref 0x71A1B8 ctor 0x71C558 TTaoistEngine
                //   parent TAIMon(=AiMon)。case body 无 ctor 后逻辑。ctor 已核验（见 TaoistEngine.cs）。
                case 130:
                    cert = new TaoistEngine();
                    break;

                // race 138 handler 0x67A895 classref 0x66D594 ctor 0x6735D4 TWolfStickIceMon
                //   parent TSearchMon(=SearchMon)，共享 ctor。case body 无 ctor 后逻辑（见 WolfStickIceMon.cs）。
                case 138:
                    cert = new WolfStickIceMon();
                    break;

                // race 139 handler 0x67A8A9 classref 0x66D860 ctor 0x6735D4 TLongKnifeIceMon
                //   parent TSearchMon，共享 ctor。case body 无 ctor 后逻辑（见 LongKnifeIceMon.cs）。
                case 139:
                    cert = new LongKnifeIceMon();
                    break;

                // race 140 handler 0x67A8BD classref 0x66DB2C ctor 0x6735D4 TGreenPoisonIceMon
                //   parent TSearchMon，共享 ctor。case body 无 ctor 后逻辑（见 GreenPoisonIceMon.cs）。
                case 140:
                    cert = new GreenPoisonIceMon();
                    break;

                // race 141 handler 0x67A8D1 classref 0x66DDFC ctor 0x6735D4 TRedPoisonIceMon
                //   parent TSearchMon，共享 ctor。case body 无 ctor 后逻辑（见 RedPoisonIceMon.cs）。
                case 141:
                    cert = new RedPoisonIceMon();
                    break;

                // race 142 handler 0x67A8E5 classref 0x66E0C8 ctor 0x6735D4 TBluePoisonIceMon
                //   parent TSearchMon，共享 ctor。case body 无 ctor 后逻辑（见 BluePoisonIceMon.cs）。
                case 142:
                    cert = new BluePoisonIceMon();
                    break;

                // race 143 handler 0x67A8F9 classref 0x66E394 ctor 0x6748CC TQuickKnifeIceMon
                //   parent TSearchMon，自有 ctor(m_nViewRange=7)。case body 无 ctor 后逻辑（见 QuickKnifeIceMon.cs）。
                case 143:
                    cert = new QuickKnifeIceMon();
                    break;

                default:
                    return false;
            }
            return true;
        }
    }
}
