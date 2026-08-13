using SystemModule;

namespace GameSvr
{
    public class MapItem
    {
        
        
        
        public int Id;
        
        
        
        public string Name;
        
        
        
        public ushort Looks;
        public byte AniCount;
        public int Reserved;
        
        
        
        public int Count;
        public object DropBaseObject;
        public object OfBaseObject;
        
        
        
        public int CanPickUpTick;
        public TUserItem UserItem;

        // NativeExpirable (+0x0D) and NativeLifetimeMs (+0x20) used to live here.  They
        // are fields of 战神's EVENT object (cell tag 3, constructed by sub_717300 which
        // writes `mov byte [ebx+4],3` @0x717322), not of a ground item (cell tag 2,
        // sub_783788 `mov byte [ebx+4],2` @0x7837AA).  The ground-item branch @0x77A3D9
        // reads neither.  They were only ever read — nothing in the tree assigned them —
        // so keeping them here just invited the tag-2/tag-3 confusion to recur.  See
        // NativeMapItemExpiry for the ladder they really belong to.

        public MapItem()
        {
            Id = HUtil32.Sequence();
        }
    }
}