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
            // MINE-16: 原版 sub_7176E8 直接复用构造期存下的补矿量，不重新抽样：
            //   0x7176EE  8B 43 10   mov eax, [ebx+0x10]
            //   0x7176F1  89 43 0C   mov [ebx+0x0C], eax
            //   0x7176F4  E8 47 0C CF FF  call 0x408340
            //   0x7176F9  89 43 14   mov [ebx+0x14], eax
            // 因此补矿量为 0 的矿点会永久枯竭，这是原版行为，不得「修正」。
            MineCount = _addStoneCount;
            AddStoneMineTick = HUtil32.GetTickCount();
        }
    }
}
