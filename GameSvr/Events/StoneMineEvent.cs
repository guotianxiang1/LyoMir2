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
            AddToMap = true;
            // MINE-54: the native StoneMine constructor (sub_717xxx) has NO
            // nType 55/56/57 branch - that arm (AddToMapItemEvent +
            // Random(2000)+300 / Random(800)+100) was invented. Native always
            // takes the single path below: AddToMapMineEvent, MineCount =
            // Random(200) and _addStoneCount = Random(80), both stored raw
            // (@0x7176A4 / @0x7176B1, verified).
            if (m_Envir.AddToMapMineEvent(nX, nY, CellType.OS_EVENTOBJECT, this) == null)
            {
                AddToMap = false;
            }
            else
            {
                m_boVisible = false;
                MineCount = M2Share.RandomNumber.Random(200);
                AddStoneMineTick = HUtil32.GetTickCount();
                m_boActive = false;
                _addStoneCount = M2Share.RandomNumber.Random(80);
            }
        }

        public void AddStoneMine()
        {
            MineCount = _addStoneCount;
            AddStoneMineTick = HUtil32.GetTickCount();
        }
    }
}