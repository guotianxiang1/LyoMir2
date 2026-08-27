using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Native <c>TBeeQueen</c>, VMT 0x0067E5A8, instance size 1244 = its
    /// parent <c>TWithChildMon</c>'s size, so it declares no fields of its own; the
    /// spawned-child list lives on TWithChildMon (m_ChildList), shared with the
    /// sibling TSpiderHouseMon. The port had it as a direct AnimalObject child with
    /// a private BBList, which skipped the native intermediate layer.
    /// </summary>
    public class BeeQueen : TWithChildMon
    {

        public BeeQueen() : base()
        {
            m_nViewRange = 9;
            m_nRunTime = 250;
            m_dwSearchTime = M2Share.RandomNumber.Random(1500) + 2500;
            m_dwSearchTick = HUtil32.GetTickCount();
            m_boStickMode = true;
        }

        private void MakeChildBee()
        {
            if (m_ChildList.Count >= 15)
            {
                return;
            }
            SendRefMsg(Grobal2.RM_HIT, m_btDirection, m_nCurrX, m_nCurrY, 0, "");
            SendDelayMsg(this, Grobal2.RM_ZEN_BEE, 0, 0, 0, 0, "", 500);
        }

        public override bool Operate(TProcessMessage ProcessMsg)
        {
            if (ProcessMsg.wIdent == Grobal2.RM_ZEN_BEE)
            {
                var BB = M2Share.UserEngine.RegenMonsterByName(m_PEnvir, m_nCurrX, m_nCurrY, M2Share.g_Config.sBee);
                if (BB != null)
                {
                    BB.SetTargetCreat(m_TargetCret);
                    m_ChildList.Add(BB);
                }
            }
            return base.Operate(ProcessMsg);
        }

        public override void Run()
        {
            if (!m_boGhost && !m_boDeath && m_wStatusTimeArr[Grobal2.POISON_STONE] == 0)
            {
                if ((HUtil32.GetTickCount() - m_dwWalkTick) >= m_nWalkSpeed)
                {
                    m_dwWalkTick = HUtil32.GetTickCount();
                    if ((HUtil32.GetTickCount() - m_dwHitTick) >= m_nNextHitTime)
                    {
                        m_dwHitTick = HUtil32.GetTickCount();
                        SearchTarget();
                        if (m_TargetCret != null)
                        {
                            MakeChildBee();
                        }
                    }
                    for (var i = m_ChildList.Count - 1; i >= 0; i--)
                    {
                        var BB = m_ChildList[i];
                        if (BB.m_boDeath || BB.m_boGhost)
                        {
                            m_ChildList.RemoveAt(i);
                        }
                    }
                }
            }
            base.Run();
        }
    }
}
