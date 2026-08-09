namespace GameSvr
{
    public partial class NormNpc
    {
        internal bool HasNativeMagicTowerArcher(TPlayObject player, int index)
        {
            return HasNativePasProperty(12)
                   && player != null
                   && player.HasNativeMagicTowerArcher(index);
        }
    }
}
