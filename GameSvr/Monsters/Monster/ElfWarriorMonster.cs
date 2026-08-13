using SystemModule;

namespace GameSvr
{
    
    
    
    public class ElfWarriorMonster : SpitSpider
    {
        public bool boIsFirst = false;
        private int dwDigDownTick = 0;

        // ✅ 战神字节证据 (Tier-1)：AppearNow 的原生本体 = **sub_66A50C**，逐字节全文：
        //   66A513  C6 83 ED 04 00 00 00  mov byte [ebx+0x4ED],0  ; boIsFirst := false
        //   66A51A  C6 83 E3 02 00 00 00  mov byte [ebx+0x2E3],0  ; m_boFixedHideMode := false
        //   66A521  call sub_408340(GetTickCount) ; mov [ebx+0x80],esi ; call sub_765DEC
        //   66A553  mov dx,0x27D8 (=10200 RM_DIGUP) ; call dword [esi+0xD8]  ; SendRefMsg
        //   66A565  FF 92 8C 00 00 00     call dword [edx+0x8C]   ; <== 【虚派发 SearchTarget】
        //   66A56B  81 83 84 03 00 00 20 03 00 00
        //                                 add dword [ebx+0x384],0x320   ; m_dwWalkTick += 800
        //   66A575  call sub_408340 ; mov [ebx+0x4F0],eax          ; dwDigDownTick = tick
        //   66A583  ret
        // 与 TElfMonster 的差别：这里 SearchTarget 之后【只有】 tick += 800，**没有**再单独
        // 重算 m_nWalkSpeed（ElfMonster 的 sub_66A228 @0x66A248-0x66A258 有那一步）。
        // 旧 C# 写 RecalcAbilitys() 既做了原版没有的全属性重算，又漏了这次搜敌派发。
        public void AppearNow()
        {
            boIsFirst = false;
            m_boFixedHideMode = false;
            SendRefMsg(Grobal2.RM_DIGUP, m_btDirection, m_nCurrX, m_nCurrY, 0, "");
            SearchTarget();
            m_dwWalkTick = m_dwWalkTick + 800;
            dwDigDownTick = HUtil32.GetTickCount();
        }

        public ElfWarriorMonster()
            : base()
        {
            m_nViewRange = 6;
            m_boFixedHideMode = true;
            boIsFirst = true;
            m_boUsePoison = false;
        }

        // ✅ 战神字节证据 (Tier-1)：TElfWarriorMonster 覆写的是 **SearchTarget(vmt+0x08C)**，
        // 不是 RecalcAbilitys(vmt+0x0C8)。VMT 0x662DC4(size 1268, parent TSpitSpider)
        // 覆写集只有两项：Run=0x66A5D0、SearchTarget=0x66A76C。
        // sub_66A76C 逐字节：
        //   66A774  E8 F7 37 0B 00  call sub_71DF70   ; TAnimal.SearchTarget(基类)
        //   66A77B  E8 04 FE FF FF  call sub_66A584   ; = 本文件的 ResetElfMon
        // helper sub_66A584 逐字节：
        //   66A58C  mov al,byte [ebx+0x483]      ; m_btSlaveMakeLevel
        //   66A592  imul eax,eax,0x64            ; *100
        //   66A595  mov edx,0x5DC ; sub edx,eax  ; 1500 - lvl*100
        //   66A59C  mov [ebx+0x320],edx          ; m_nNextHitTime
        //   66A5A4  mov al,byte [ebx+0x483] ; imul eax,eax,0x32
        //   66A5AD  mov edx,0x1F4 ; sub edx,eax  ; 500 - lvl*50
        //   66A5B4  mov [ebx+0x324],edx          ; m_nWalkSpeed
        //   66A5BA  call sub_408340 ; add 0x7D0  ; m_dwWalkTick = tick+2000
        protected override bool SearchTarget()
        {
            var result = base.SearchTarget();
            ResetElfMon();
            return result;
        }

        private void ResetElfMon()
        {
            m_nNextHitTime = 1500 - m_btSlaveMakeLevel * 100;
            m_nWalkSpeed = 500 - m_btSlaveMakeLevel * 50;
            m_dwWalkTick = HUtil32.GetTickCount() + 2000;
        }

        public override void Run()
        {
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
                bool boChangeFace = true;
                if (m_TargetCret != null)
                {
                    boChangeFace = false;
                }
                if (m_Master != null && (m_Master.m_TargetCret != null || m_Master.m_LastHiter != null))
                {
                    boChangeFace = false;
                }
                if (boChangeFace)
                {
                    if ((HUtil32.GetTickCount() - dwDigDownTick) > (6 * 10 * 1000))
                    {
                        TBaseObject elfMon = null;
                        var ElfName = m_sCharName;
                        if (ElfName[ElfName.Length - 1] == '1')
                        {
                            ElfName = ElfName.Substring(0, ElfName.Length - 1);
                            elfMon = MakeClone(ElfName, this);
                        }
                        if (elfMon != null)
                        {
                            SendRefMsg(Grobal2.RM_DIGDOWN, m_btDirection, m_nCurrX, m_nCurrY, 0, "");
                            SendRefMsg(Grobal2.RM_CHANGEFACE, 0, ObjectId, elfMon.ObjectId, 0, "");
                            elfMon.m_boAutoChangeColor = m_boAutoChangeColor;
                            if (elfMon is ElfMonster)
                            {
                                (elfMon as ElfMonster).AppearNow();
                            }
                            m_Master = null;
                            KickException();
                        }
                    }
                }
                else
                {
                    dwDigDownTick = HUtil32.GetTickCount();
                }
            }
            base.Run();
        }
    }
}