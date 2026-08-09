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
