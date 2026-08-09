using GameSvr.Services;

namespace GameSvr
{
    public partial class TBaseObject
    {
        public int m_nNativeHqFastness;

        internal void ResetNativeHqFastness()
        {
            m_nNativeHqFastness = 0;
        }

        internal void AddNativeHqFastness(int value)
        {
            m_nNativeHqFastness = unchecked(m_nNativeHqFastness + value);
        }

        internal int ApplyNativeFastnessHqReduction(int damage)
        {
            NativeFastnessHqTable table = M2Share.NativeFastnessHqTable;
            return table == null
                ? damage
                : table.ApplyReduction(damage, m_nNativeHqFastness);
        }
    }
}
