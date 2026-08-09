namespace GameSvr
{
    public partial class TBaseObject
    {
        // Native actor offsets +966, +972, +976, +980 and +478.
        internal ushort m_wNativeBreakThroughChance;
        internal int m_nNativeSteelBodyReduction;
        internal int m_nNativeGoldenBellReduction;
        internal int m_nNativeDragonBodyReduction;
        internal bool m_boNativeAwakening;

        internal int ApplyNativeMagicBreakthrough(int flags)
        {
            int chance = Math.Min(
                (int)m_wNativeBreakThroughChance, 600);
            if (M2Share.RandomNumber.Random(1000) < chance)
                flags |= 5;
            return flags;
        }

        internal int ApplyNativeFixedMagicReductions(int damage)
        {
            if (damage <= 0)
                return damage;

            int ironBone = m_nNativeSteelBodyReduction;
            if (ironBone > 0 && M2Share.RandomNumber.Random(100) < 30)
            {
                damage = ironBone >= damage
                    ? 0
                    : unchecked(damage - ironBone);
            }

            int goldenBell = m_nNativeGoldenBellReduction;
            int goldenBellChance = goldenBell < 3000 ? 40 : 50;
            if (M2Share.RandomNumber.Random(100) < goldenBellChance)
            {
                damage = goldenBell >= damage
                    ? 0
                    : unchecked(damage - goldenBell);
            }

            int dragonProtection = m_nNativeDragonBodyReduction;
            if (dragonProtection > 0)
            {
                damage = dragonProtection >= damage
                    ? 0
                    : unchecked(damage - dragonProtection);
            }

            return damage;
        }

        internal int ApplyNativeMagicAwakening(int skillId, bool arg0,
            int damage)
        {
            if (!m_boNativeAwakening || !arg0 ||
                IsNativeMagicAwakeningExcludedSkill(skillId))
            {
                return damage;
            }

            int roll = M2Share.RandomNumber.Random(10000);
            float multiplier = roll switch
            {
                < 50 => 10.5f,
                < 110 => 9.5f,
                < 190 => 8.5f,
                < 290 => 7.5f,
                < 440 => 6.5f,
                < 640 => 5.5f,
                < 940 => 4.5f,
                < 1440 => 3.5f,
                < 5940 => 2.5f,
                _ => 1.5f
            };
            return unchecked((int)Math.Truncate(damage *
                (double)multiplier));
        }

        internal static bool IsNativeMagicAwakeningExcludedSkill(
            int skillId)
        {
            return IsNativeMagicFirstClassifier(skillId) ||
                IsNativeMagicSecondClassifier(skillId);
        }
    }
}
