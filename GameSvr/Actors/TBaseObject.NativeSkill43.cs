namespace GameSvr
{
    public partial class TBaseObject
    {
        // Native VMT+0x19C is target-side. TCreature, players and human actors
        // return false; TAnimal supplies the accepting implementation.
        internal virtual bool IsNativeMagic43Target(TPlayObject source)
        {
            return false;
        }
    }
}
