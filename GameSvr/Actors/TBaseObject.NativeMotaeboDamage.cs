using SystemModule;

namespace GameSvr
{
    public partial class TBaseObject
    {
        private const int NativeMotaeboUnmappedDamageBonus = 0;

        internal int ResolveNativeMotaeboDamage(int rawDamage)
        {
            int damage = rawDamage;
            if (!HasNativeActiveState(17))
            {
                int lowArmor = HUtil32.LoWord(m_WAbil.AC);
                int highArmor = HUtil32.HiWord(m_WAbil.AC);
                int armorSpan = Math.Max(0, highArmor - lowArmor) + 1;
                int armor = lowArmor +
                    M2Share.RandomNumber.Random(armorSpan);
                damage = Math.Max(0, unchecked(damage - armor));
            }

            // Original +0x2EE conditionally adds +0x17A. Neither carrier has
            // a proven C# mapping, so this branch remains fail-closed at zero.
            damage = unchecked(damage + NativeMotaeboUnmappedDamageBonus);

            if (HasNativeActiveState(7))
                return unchecked(damage * 3) / 10;

            if (damage > 0 &&
                TryGetNativeTimedAbilityValue(20, out int bubbleLevel))
            {
                damage = bubbleLevel == 4
                    ? unchecked(damage * 3) / 10
                    : unchecked(unchecked(damage *
                        (bubbleLevel + 2)) << 3) / 100;
                ReduceNativeTimedAbilityRemaining(20, 3000);
            }
            return damage;
        }
    }
}
