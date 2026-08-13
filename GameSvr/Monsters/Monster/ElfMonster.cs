using SystemModule;

namespace GameSvr
{
    
    
    
    public class ElfMonster : Monster
    {
        public bool boIsFirst = false;

        public ElfMonster() : base()
        {
            m_nViewRange = 6;
            m_boFixedHideMode = true;
            m_boNoAttackMode = true;
            boIsFirst = true;
        }

        // ✅ 战神字节证据 (Tier-1)：AppearNow 的原生本体 = **sub_66A228**，逐字节全文：
        //   66A22E  C6 83 E8 04 00 00 00  mov byte [ebx+0x4E8],0   ; 清"待出现"标记
        //   66A235  C6 83 E3 02 00 00 00  mov byte [ebx+0x2E3],0   ; m_boFixedHideMode := false
        //   66A23E  8B 10 / FF 92 8C 00 00 00
        //                                 call dword [edx+0x8C]    ; <== 【虚派发 SearchTarget】
        //   66A248  mov al,byte [ebx+0x483] ; imul eax,eax,0x32
        //   66A251  mov edx,0x1F4 ; sub edx,eax                     ; 500 - lvl*50
        //   66A258  mov [ebx+0x324],edx                             ; m_nWalkSpeed
        //   66A25E  81 83 84 03 00 00 20 03 00 00
        //                                 add dword [ebx+0x384],0x320  ; m_dwWalkTick += 800
        //   66A26A  ret
        // 关键点：这里是【虚派发 +0x8C】而不是直调 helper，所以走的是 TElfMonster.SearchTarget
        // (sub_66A2C4) = 基类 SearchTarget(sub_71DF70) + helper(sub_66A2DC)。
        // 即出现时【会真的搜一次敌】，然后再单独把 m_nWalkSpeed 重算一遍、m_dwWalkTick 加 800
        // （注意是 `add`，叠在 helper 刚写下的 tick+2000 之上 = tick+2800，不是 `=`）。
        // 旧 C# 这里写 RecalcAbilitys() 会触发原版此处【没有】的全属性重算，且不搜敌。
        public void AppearNow()
        {
            // 0x66A22E `mov byte [ebx+0x4E8],0` is AppearNow's FIRST statement,
            // and +0x4E8 IS boIsFirst -- the ctor sets it (0x66A29F `mov byte
            // [esi+0x4E8],1`) and Run tests-then-clears it (0x66A318 `cmp byte
            // [esi+0x4E8],0 / je` then 0x66A321 clears). Without this line an elf
            // that was forced to appear early still runs its first-tick DIGUP
            // block on the next Run, re-showing and re-resetting a monster that
            // has already surfaced. (Note TElfWarrior genuinely has two flags:
            // +0x4EC tested first, then its own +0x4ED.)
            boIsFirst = false;
            m_boFixedHideMode = false;
            SearchTarget();
            m_nWalkSpeed = 500 - m_btSlaveMakeLevel * 50;
            m_dwWalkTick = m_dwWalkTick + 800;
        }

        // ✅ 战神字节证据 (Tier-1)：TElfMonster 覆写的是 **SearchTarget(vmt+0x08C)**，
        // 不是 RecalcAbilitys(vmt+0x0C8)。VMT 0x662B38(size 1260, parent TMonster) 的
        // 覆写集只有两项：Run=0x66A310、SearchTarget=0x66A2C4。
        // sub_66A2C4 逐字节 = 先调基类再调私有 helper：
        //   66A2CC  E8 9F 3C 0B 00  call sub_71DF70      ; TAnimal.SearchTarget(基类)
        //   66A2D3  E8 04 00 00 00  call sub_66A2DC      ; = 本文件的 ResetElfMon
        // helper sub_66A2DC 逐字节：
        //   66A2E4  mov al,byte [ebx+0x483]      ; m_btSlaveMakeLevel
        //   66A2EA  imul eax,eax,0x32            ; *50
        //   66A2ED  mov edx,0x1F4 ; sub edx,eax  ; 500 - lvl*50
        //   66A2F4  mov [ebx+0x324],edx          ; m_nWalkSpeed
        //   66A2FA  call sub_408340(GetTickCount) ; add eax,0x7D0 (+2000)
        //   66A304  mov [ebx+0x384],eax          ; m_dwWalkTick
        // => 挂在 RecalcAbilitys 上会在【装备/状态重算】时机触发，原版是在【搜敌】时机触发，
        //    两者调用频率与时点都不同（RecalcAbilitys 由 TBaseObject.Base.cs:2825 那条路径驱动）。
        protected override bool SearchTarget()
        {
            var result = base.SearchTarget();
            ResetElfMon();
            return result;
        }

        private void ResetElfMon()
        {
            m_nWalkSpeed = 500 - m_btSlaveMakeLevel * 50;
            m_dwWalkTick = HUtil32.GetTickCount() + 2000;
        }

        public override void Run()
        {
            bool boChangeFace = false;
            if (boIsFirst)
            {
                boIsFirst = false;
                m_boFixedHideMode = false;
                SendRefMsg(Grobal2.RM_DIGUP, m_btDirection, m_nCurrX, m_nCurrY, 0, "");
                ResetElfMon();
            }
            if (m_boDeath)
            {
                if ((HUtil32.GetTickCount() - m_dwDeathTick) > (2 * 1000))
                {
                    MakeGhost();
                }
            }
            else
            {
                if (m_TargetCret != null)
                {
                    boChangeFace = true;
                }
                if (m_Master != null && (m_Master.m_TargetCret != null || m_Master.m_LastHiter != null))
                {
                    boChangeFace = true;
                }
                if (boChangeFace)
                {
                    var ElfMon = MakeClone(M2Share.g_Config.sDragon1, this);
                    if (ElfMon != null)
                    {
                        ElfMon.m_boAutoChangeColor = m_boAutoChangeColor;
                        if (ElfMon is ElfWarriorMonster)
                        {
                            (ElfMon as ElfWarriorMonster).AppearNow();
                        }
                        m_Master = null;
                        KickException();
                    }
                }
            }
            base.Run();
        }
    }
}

