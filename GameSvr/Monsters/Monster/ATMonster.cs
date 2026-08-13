using SystemModule;

namespace GameSvr
{
    public class AtMonster : Monster
    {
        public AtMonster() : base()
        {
            m_dwSearchTime = M2Share.RandomNumber.Random(1500) + 1500;
        }

        public override void Run()
        {
            // MONAI-10 — 原生 TATMonster.Run sub_666AE4 的搜敌闸只有 can-act 谓词一项：
            //   00666AEA  B2 01        mov  dl,1
            //   00666AEE  8B 08        mov  ecx,[eax]
            //   00666AF0  FF 51 40     call [ecx+0x40]   ; sub_76B354
            //   00666AF5  74 3B        je   0x666B32     ; 跳过搜敌，直接 TMonster.Run
            //   00666AF7  E8 44 18 DA FF        call 0x408340        ; GetTickCount
            //   00666AFE  2B 93 88 00 00 00     sub  edx,[ebx+0x88]  ; m_dwSearchEnemyTick
            //   00666B04  81 FA 40 1F 00 00     cmp  edx,0x1F40      ; 8000
            //   00666B0A  77 19                 ja   搜敌
            //   00666B14  81 FA E8 03 00 00     cmp  edx,0x3E8       ; 1000
            //   00666B1A  76 16                 jbe  跳过
            //   00666B1C  83 BB 44 03 00 00 00  cmp  dword [ebx+0x344],0  ; m_TargetCret
            //   00666B23  75 0D                 jne  跳过
            //   00666B25  89 83 88 00 00 00     mov  [ebx+0x88],eax
            //   00666B2D  E8 3E 6F 0B 00        call 0x71DA70        ; SearchTarget
            // 两次时限都是无符号比较(ja/jbe)。bo554 / m_boGhost / POISON_STONE 见
            // Monster.Run 的同名说明。
            if (!IsNativeCanActBlocked(1) && !bo554 && !m_boGhost && m_wStatusTimeArr[Grobal2.POISON_STONE] == 0)
            {
                if ((HUtil32.GetTickCount() - m_dwSearchEnemyTick) > 8000 || (HUtil32.GetTickCount() - m_dwSearchEnemyTick) > 1000 && m_TargetCret == null)
                {
                    m_dwSearchEnemyTick = HUtil32.GetTickCount();
                    SearchTarget();
                }
            }
            base.Run();
        }
    }
}