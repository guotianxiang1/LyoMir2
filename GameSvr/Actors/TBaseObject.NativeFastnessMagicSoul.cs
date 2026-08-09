using GameSvr.Services;

namespace GameSvr
{
    public partial class TBaseObject
    {
        internal int m_nNativeMagicFastnessSelector;
        internal int m_nNativeSoulFastnessSelector;

        internal int ApplyNativeFastnessMagicReduction(int damage)
        {
            NativeFastnessTable table = M2Share.NativeFastnessMagicTable;
            return table == null
                ? damage
                : table.ApplyReduction(damage,
                    m_nNativeMagicFastnessSelector);
        }

        internal int ApplyNativeFastnessSoulReduction(int damage)
        {
            NativeFastnessTable table = M2Share.NativeFastnessSoulTable;
            return table == null
                ? damage
                : table.ApplyReduction(damage,
                    m_nNativeSoulFastnessSelector);
        }

        internal int ApplyNativeGeneralFastnessReduction(int skillId,
            int category, bool firstClassifier, bool secondClassifier,
            bool thirdClassifier, int damage)
        {
            if (firstClassifier || secondClassifier || thirdClassifier)
                return damage;

            if (skillId != 22 &&
                unchecked((byte)(category - 1)) < 3)
            {
                return ApplyNativeFastnessMagicReduction(damage);
            }

            return category == 5
                ? ApplyNativeFastnessSoulReduction(damage)
                : damage;
        }
    }
}
