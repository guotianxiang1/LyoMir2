using SystemModule;

namespace GameSvr
{
    public class ChickenDeer : Monster
    {
        public ChickenDeer() : base()
        {
            m_nViewRange = 5;
        }

        public override void Run()
        {
            // MONAI-14 — TChickenDeer.Run sub_6669C0（parent 是 TMonster 不是 TATMonster）：
            //   006669C8  B2 01 / FF 51 40          CanAct(+0x40) 假则只跑 inherited
            //   006669D9  80 BB 51 04 00 00 00      cmp [ebx+0x451],0  ; 已在 runaway 则跳过搜敌
            //   006669F1  3B 83 24 03 00 00 / 0F 8E jle  ; elapsed <= m_nWalkSpeed 则 inherited
            //   006669FF  E8 6C 70 0B 00            call 0x71DA70      ; SearchTarget
            //   00666A04  88 83 51 04 00 00         mov [ebx+0x451],al ; runaway = 本次搜到
            //   有目标且 |dx|<=6 且 |dy|<=6 才 GetNextDirection+GetNextPosition(...,5)
            //   00666A84  33 C0 / 89 83 44 03 00 00 xor/mov [ebx+0x344],0  ; 只清目标指针
            //     不清 TargetXY（DelTargetCreat 会把逃跑格抹成 -1）
            // 旧 C# 自己扫可见表、两轴都比 X、留下 m_TargetCret，鹿会回头打人而不是逃。
            if (!IsNativeCanActBlocked(1) && !m_boRunAwayMode)
            {
                if ((HUtil32.GetTickCount() - m_dwWalkTick) > m_nWalkSpeed)
                {
                    m_boRunAwayMode = SearchTarget();
                    if (m_boRunAwayMode && m_TargetCret != null)
                    {
                        var tx = m_TargetCret.m_nCurrX;
                        var ty = m_TargetCret.m_nCurrY;
                        if (Math.Abs(m_nCurrX - tx) <= 6 && Math.Abs(m_nCurrY - ty) <= 6)
                        {
                            var dir = M2Share.GetNextDirection(m_nCurrX, m_nCurrY, tx, ty);
                            m_PEnvir.GetNextPosition(tx, ty, dir, 5, ref m_nTargetX, ref m_nTargetY);
                        }
                        m_TargetCret = null;
                    }
                }
            }
            base.Run();
        }
    }
}