using SystemModule;

namespace GameSvr
{
    public class Monster : AnimalObject
    {
        private int m_dwThinkTick;
        protected bool bo554;
        private bool m_boDupMode;

        public Monster() : base()
        {
            m_boDupMode = false;
            bo554 = false;
            // MONAI-02 — TMonster.Create sub_66610C 的构造默认 race 是 80(RC_MONSTER)：
            //   00666162  C6 86 78 01 00 00 50   mov byte [esi+0x178],0x50
            // （父类 TAnimal.Create 0071D851 C6 87 78 01 00 00 32 = 50/RC_ANIMAL）
            // [+0x178] 是 race 而不是 Level：工厂 sub_679F8C 用 `movzx eax,byte [edi+0x14]`
            // 分派同一个字段，MonInitialize 把它写进 [mon+0x178]。
            // MonInitialize 之后会被 DB 记录覆盖（UsrEngn.cs MonInitialize），所以只有
            // 不经 MonInitialize 创建的怪物看得到这个默认值。
            m_btRaceServer = Grobal2.RC_MONSTER;
            m_dwThinkTick = HUtil32.GetTickCount();
            m_nViewRange = 5;
            m_nRunTime = 250;
            m_dwSearchTime = 3000 + M2Share.RandomNumber.Random(2000);
            m_dwSearchTick = HUtil32.GetTickCount();
        }

        protected TBaseObject MakeClone(string sMonName, TBaseObject OldMon)
        {
            TBaseObject result = null;
            var ElfMon = M2Share.UserEngine.RegenMonsterByName(m_PEnvir, m_nCurrX, m_nCurrY, sMonName);
            if (ElfMon != null)
            {
                ElfMon.m_Master = OldMon.m_Master;
                ElfMon.m_dwMasterRoyaltyTick = OldMon.m_dwMasterRoyaltyTick;
                ElfMon.m_btSlaveMakeLevel = OldMon.m_btSlaveMakeLevel;
                ElfMon.m_btSlaveExpLevel = OldMon.m_btSlaveExpLevel;
                ElfMon.RecalcAbilitys();
                ElfMon.RefNameColor();
                if (OldMon.m_Master != null)
                {
                    OldMon.m_Master.m_SlaveList.Add(ElfMon);
                }
                ElfMon.m_WAbil = new TAbility();
                ElfMon.m_WAbil.CopyFrom(OldMon.m_WAbil);
                ElfMon.m_wStatusTimeArr = (ushort[])OldMon.m_wStatusTimeArr.Clone();
                ElfMon.m_TargetCret = OldMon.m_TargetCret;
                ElfMon.m_dwTargetFocusTick = OldMon.m_dwTargetFocusTick;
                ElfMon.m_LastHiter = OldMon.m_LastHiter;
                ElfMon.m_LastHiterTick = OldMon.m_LastHiterTick;
                ElfMon.m_btDirection = OldMon.m_btDirection;
                result = ElfMon;
            }
            return result;
        }

        public override bool Operate(TProcessMessage ProcessMsg)
        {
            return base.Operate(ProcessMsg);
        }

        private bool Think()
        {
            var result = false;
            if ((HUtil32.GetTickCount() - m_dwThinkTick) > (3 * 1000))
            {
                m_dwThinkTick = HUtil32.GetTickCount();
                if (m_PEnvir.GetXYObjCount(m_nCurrX, m_nCurrY) >= 2)
                {
                    m_boDupMode = true;
                }
                if (!IsProperTarget(m_TargetCret))
                {
                    m_TargetCret = null;
                }
            }
            if (m_boDupMode)
            {
                int nOldX = m_nCurrX;
                int nOldY = m_nCurrY;
                WalkTo((byte)M2Share.RandomNumber.Random(8), false);
                if (nOldX != m_nCurrX || nOldY != m_nCurrY)
                {
                    m_boDupMode = false;
                    result = true;
                }
            }
            return result;
        }

        protected virtual bool AttackTarget()
        {
            var result = false;
            byte btDir = 0;
            if (m_TargetCret != null)
            {
                if (GetAttackDir(m_TargetCret, ref btDir))
                {
                    if (HUtil32.GetTickCount() - m_dwHitTick > m_nNextHitTime)
                    {
                        m_dwHitTick = HUtil32.GetTickCount();
                        m_dwTargetFocusTick = HUtil32.GetTickCount();
                        Attack(m_TargetCret, btDir);
                        BreakHolySeizeMode();
                    }
                    result = true;
                }
                else
                {
                    if (m_TargetCret.m_PEnvir == m_PEnvir)
                    {
                        SetTargetXY(m_TargetCret.m_nCurrX, m_TargetCret.m_nCurrY);
                    }
                    else
                    {
                        DelTargetCreat();
                    }
                }
            }
            return result;
        }

        public override void Run()
        {
            if (!m_boGhost && !m_boDeath && !m_boFixedHideMode && !m_boStoneMode && m_wStatusTimeArr[Grobal2.POISON_STONE] == 0)
            {
                if (Think())
                {
                    base.Run();
                    return;
                }
                if (m_boWalkWaitLocked)
                {
                    if ((HUtil32.GetTickCount() - m_dwWalkWaitTick) > m_dwWalkWait)
                    {
                        m_boWalkWaitLocked = false;
                    }
                }
                if (!m_boWalkWaitLocked && (HUtil32.GetTickCount() - m_dwWalkTick) > m_nWalkSpeed)
                {
                    m_dwWalkTick = HUtil32.GetTickCount();
                    m_nWalkCount++;
                    if (m_nWalkCount > m_nWalkStep)
                    {
                        m_nWalkCount = 0;
                        m_boWalkWaitLocked = true;
                        m_dwWalkWaitTick = HUtil32.GetTickCount();
                    }
                    if (!m_boRunAwayMode)
                    {
                        if (!m_boNoAttackMode)
                        {
                            if (m_TargetCret != null)
                            {
                                if (AttackTarget())
                                {
                                    base.Run();
                                    return;
                                }
                            }
                            else
                            {
                                m_nTargetX = -1;
                                if (m_boMission)
                                {
                                    m_nTargetX = m_nMissionX;
                                    m_nTargetY = m_nMissionY;
                                }
                            }
                        }
                        if (m_Master != null)
                        {
                            short nX = 0;
                            short nY = 0;
                            if (m_TargetCret == null)
                            {
                                m_Master.GetBackPosition(ref nX, ref nY);
                                if (Math.Abs(m_nTargetX - nX) > 1 || Math.Abs(m_nTargetY - nY) > 1)
                                {
                                    m_nTargetX = nX;
                                    m_nTargetY = nY;
                                    if (Math.Abs(m_nCurrX - nX) <= 2 && Math.Abs(m_nCurrY - nY) <= 2)
                                    {
                                        if (m_PEnvir.GetMovingObject(nX, nY, true) != null)
                                        {
                                            m_nTargetX = m_nCurrX;
                                            m_nTargetY = m_nCurrY;
                                        }
                                    }
                                }
                            }
                            if (!m_Master.m_boSlaveRelax && (m_PEnvir != m_Master.m_PEnvir || Math.Abs(m_nCurrX - m_Master.m_nCurrX) > 20 || Math.Abs(m_nCurrY - m_Master.m_nCurrY) > 20))
                            {
                                SpaceMove(m_Master.m_PEnvir.sMapName, m_nTargetX, m_nTargetY, 1);
                            }
                        }
                    }
                    else
                    {
                        if (m_dwRunAwayTime > 0 && (HUtil32.GetTickCount() - m_dwRunAwayStart) > m_dwRunAwayTime)
                        {
                            m_boRunAwayMode = false;
                            m_dwRunAwayTime = 0;
                        }
                    }
                    if (m_Master != null && m_Master.m_boSlaveRelax)
                    {
                        base.Run();
                        return;
                    }
                    if (m_nTargetX != -1)
                    {
                        GotoTargetXY();
                    }
                    else
                    {
                        if (m_TargetCret == null)
                        {
                            Wondering();
                        }
                    }
                }
            }
            base.Run();
        }
    }
}
