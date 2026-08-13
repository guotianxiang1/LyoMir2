namespace GameSvr
{
    
    
    
    public class VisibleMapItem
    {
        public int nX;
        public int nY;
        public MapItem MapItem;
        public string sName;
        public ushort wLooks;
        public int nVisibleFlag;
    }

    
    
    
    public class CellObject
    {
        public CellType CellType;
        public object CellObj;
        public int dwAddTime;
        public bool boObjectDisPose;
    }

    /// <summary>
    /// WARNING: these values are the stock-Mir2 numbering and do NOT match 战神's cell
    /// tag.  战神 tags cells MOVING=1 / ITEM=2 / EVENT=3 — proven by the two constructors
    /// that write the tag the linker later copies into the node
    /// (<c>0x777DCF mov al,byte [esi+4]</c> / <c>0x777DD2 mov byte [ebx],al</c>):
    /// <c>sub_783788</c> ground item @0x7837AA <c>C6 43 04 02  mov byte [ebx+4],2</c> and
    /// <c>sub_717300</c> event object @0x717322 <c>C6 43 04 03  mov byte [ebx+4],3</c>.
    /// Never use one of these enum values to index a native branch: doing exactly that
    /// once moved the event-object expiry ladder (@0x77A480) onto ground items, which run
    /// the much shorter @0x77A3D9 branch instead.
    /// </summary>
    public enum CellType : byte
    {
        OS_EVENTOBJECT = 1,
        OS_MOVINGOBJECT = 2,
        OS_ITEMOBJECT = 3,
        OS_GATEOBJECT = 4,
        OS_MAPEVENT = 5,
        OS_DOOR = 6,
        OS_ROON = 7
    }

    public enum CellAttribute : byte
    {
        
        
        
        Walk = 0,
        HighWall = 1,
        LowWall = 2
    }

    public struct MapCellinfo
    {
        public static MapCellinfo LowWall => new() { Attribute = CellAttribute.LowWall };
        public static MapCellinfo HighWall => new() { Attribute = CellAttribute.HighWall };

        public bool Valid => Attribute == CellAttribute.Walk;

        public CellAttribute Attribute;
        public byte SkillFlag;
        public bool SkillBlocked => SkillFlag != 0;

        
        
        
        public int Count => ObjList?.Count ?? 0;

        public IList<CellObject> ObjList;

        public void Remove(int idx)
        {
            ObjList?.RemoveAt(idx);
        }
    }

    public class PointInfo
    {
        public short nX;
        public short nY;

        public PointInfo(short x, short y)
        {
            nX = x;
            nY = y;
        }
    }

    public class TRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public TRect(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }
    }
}
