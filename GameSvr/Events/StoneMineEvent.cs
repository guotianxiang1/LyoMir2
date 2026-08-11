using SystemModule;

namespace GameSvr
{
    public class StoneMineEvent : Event
    {
        private readonly int _addStoneCount = 0;
        public int MineCount = 0;
        public int AddStoneMineTick = 0;
        public bool AddToMap = false;

        public StoneMineEvent(Envirnoment Envir, int nX, int nY, int nType) : base(Envir, nX, nY, nType, 0, false)
        {
            // Native ctor = sub_717658. MINE-54: there is no nType 55/56/57 arm
            // (that AddToMapItemEvent + Random(2000)+300 / Random(800)+100 branch
            // was invented). MINE-15/16: the map insertion is guarded only by
            // `test esi,esi / je 0x71769F` (Envir nil), and its return value is
            // DISCARDED - @0x717693 immediately overwrites eax with `mov
            // [ebx+0x18],esi`. So there is no null test and no failure flag; the
            // AddToMap bool and its else-arm were invented too. Both draws sit
            // after the je target and therefore run unconditionally:
            //   @0x71769F mov eax,0xC8 / Random -> [ebx+0x0C] MineCount
            //   @0x7176AC mov eax,0x50 / Random -> [ebx+0x10] _addStoneCount
            //   @0x7176B9 GetTickCount          -> [ebx+0x14] AddStoneMineTick
            AddToMap = true;
            if (m_Envir != null)
            {
                m_Envir.AddToMapMineEvent(nX, nY, CellType.OS_EVENTOBJECT, this);
            }
            m_boVisible = false;
            m_boActive = false;
            MineCount = M2Share.RandomNumber.Random(200);
            _addStoneCount = M2Share.RandomNumber.Random(80);
            AddStoneMineTick = HUtil32.GetTickCount();
        }

        public void AddStoneMine()
        {
            MineCount = _addStoneCount;
            AddStoneMineTick = HUtil32.GetTickCount();
        }
    }
}