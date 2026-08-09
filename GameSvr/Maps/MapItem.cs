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

        /// <summary>
        /// 战神 map-item <c>+0x0D</c> — the "expirable" byte tested at the head of every
        /// branch of the ground-item cleanup <c>sub_77A178</c> (@0x77A4A2 for StdMode 37,
        /// @0x77A54B for StdMode 41, @0x77A5D4 for everything else), each doing
        /// <c>cmp byte [item+0x0D],0; je &lt;skip&gt;</c>.  ZERO MEANS NEVER EXPIRES: the
        /// permanent ground-item class (quest/event props placed on the map).  C# had no
        /// counterpart, so it aged those out after its flat timeout and destroyed them.
        /// Defaults to 1 (expirable) so ordinary drops behave exactly as before.
        /// </summary>
        public byte NativeExpirable = 1;

        /// <summary>
        /// 战神 map-item <c>+0x20</c> — the per-item lifetime dword used ONLY by the
        /// StdMode-41 branch (@0x77A560 <c>cmp edx,dword [eax+0x20]</c>), where the age is
        /// compared against this stored value instead of the 15-minute constant.  Zero
        /// means "no per-item lifetime recorded"; the resolver then falls back to
        /// <see cref="NativeMapItemExpiry.DefaultLifetimeMs"/>.
        /// </summary>
        public int NativeLifetimeMs;

        public MapItem()
        {
            Id = HUtil32.Sequence();
        }
    }
}