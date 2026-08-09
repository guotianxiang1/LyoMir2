using GameSvr.Services;

namespace GameSvr
{
    public partial class TBaseObject
    {
        internal int m_nNativeNearHitFastness;

        internal void ResetNativeNearHitFastness()
        {
            m_nNativeNearHitFastness = 0;
        }

        internal void AddNativeNearHitFastness(int value)
        {
            m_nNativeNearHitFastness = unchecked(
                m_nNativeNearHitFastness + value);
        }

        internal int ApplyNativeFastnessNearHitReduction(int damage)
        {
            NativeFastnessTable table = M2Share.NativeFastnessNearHitTable;
            return table == null
                ? damage
                : table.ApplyReduction(damage, m_nNativeNearHitFastness);
        }
    }
}
