namespace GameSvr
{
    public class MonGenInfo
    {
        public string sMapName;
        public int nX;
        public int nY;
        public string sMonName;
        public int nRange;
        public int nCount;
        public int nActiveCount;
        public int dwZenTime;
        public int nMissionGenRate;
        public IList<TBaseObject> CertList;
        public int CertCount;
        public object Envir;
        public int nRace;
        public int dwStartTick;
        public ushort nSpawnTag;
        // ✅ SPWN-14: Native spawn broadcast array at [gen+0x40] (EA: 0x67CA5D).
        // Tier-1 evidence: ProcessMon sub_67C150 Phase-2 regen worker sub_67C9E0
        // accesses [esi+0x40] when broadcasting monster spawn events to online
        // players in the same map. Array stores player references for notification.
        public IList<TPlayObject> BroadcastList;
    }
}