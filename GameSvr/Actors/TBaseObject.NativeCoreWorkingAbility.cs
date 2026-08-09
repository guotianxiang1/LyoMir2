using SystemModule;

namespace GameSvr
{
    public partial class TBaseObject
    {
        private void MergeNativeCoreItem(GoodItem item)
        {
            unchecked
            {
                ref var ability = ref m_NativeCoreWorkingAbility;
                ability.DCLow += item.Dc;
                ability.DCHigh += item.Dc2;
                ability.MCLow += item.Mc;
                ability.MCHigh += item.Mc2;
                ability.SCLow += item.Sc;
                ability.SCHigh += item.Sc2;
                ability.CCLow += item.Cc;
                ability.CCHigh += item.Cc2;

                switch (item.ItemType)
                {
                    case GoodType.ITEM_WEAPON:
                        ability.HitPoint += item.Ac2;
                        break;
                    case GoodType.ITEM_ARMOR:
                        AddNativeArmorCore(item, ref ability);
                        break;
                    case GoodType.ITEM_ACCESSORY:
                        MergeNativeAccessoryCore(item, ref ability);
                        break;
                }
            }
        }

        private static void MergeNativeAccessoryCore(GoodItem item,
            ref NativeCoreWorkingAbility ability)
        {
            unchecked
            {
                switch (item.StdMode)
                {
                    case 20:
                    case 24:
                        ability.HitPoint += item.Ac2;
                        ability.SpeedPoint += item.Mac2;
                        break;
                    case 52:
                        if (M2Share.g_Config.boAddUserItemNewValue)
                        {
                            ability.HitPoint += item.Ac2;
                            ability.SpeedPoint += item.Mac2;
                        }
                        else
                        {
                            AddNativeArmorCore(item, ref ability);
                        }
                        break;
                    case 53:
                    case 54:
                        if (!M2Share.g_Config.boAddUserItemNewValue)
                            AddNativeArmorCore(item, ref ability);
                        break;
                    case 63:
                        ability.MaxHP += item.Ac;
                        ability.MaxMP += item.Ac2;
                        break;
                }
            }
        }

        private static void AddNativeArmorCore(GoodItem item,
            ref NativeCoreWorkingAbility ability)
        {
            unchecked
            {
                ability.ACLow += item.Ac;
                ability.ACHigh += item.Ac2;
                ability.MACLow += item.Mac;
                ability.MACHigh += item.Mac2;
            }
        }

        private void MergeNativeCoreEffectProperty(ushort propertyId,
            ushort value)
        {
            unchecked
            {
                ref var ability = ref m_NativeCoreWorkingAbility;
                switch (propertyId)
                {
                    case 1: ability.DCLow += value; break;
                    case 2: ability.DCHigh += value; break;
                    case 3: ability.MCLow += value; break;
                    case 4: ability.MCHigh += value; break;
                    case 5: ability.SCLow += value; break;
                    case 6: ability.SCHigh += value; break;
                    case 7: ability.ACLow += value; break;
                    case 8: ability.ACHigh += value; break;
                    case 9: ability.MACLow += value; break;
                    case 10: ability.MACHigh += value; break;
                    case 11: ability.MaxHP += value; break;
                    case 12: ability.MaxMP += value; break;
                    case 13: ability.HitPoint += value; break;
                    case 14: ability.SpeedPoint += value; break;
                    case 111: ability.CCLow += value; break;
                    case 112: ability.CCHigh += value; break;
                }
            }
        }

        private void ProjectNativeCoreHitAndAgility()
        {
            int speedBase = m_NativeMonsterDefinition == null
                ? m_wSpeedPoint
                : m_wNativeMonsterSpeedPoint;
            m_wSpeedPoint = unchecked((ushort)(speedBase +
                m_NativeCoreWorkingAbility.SpeedPoint));
            m_btSpeedPoint = unchecked((byte)(m_btSpeedPoint +
                m_NativeCoreWorkingAbility.SpeedPoint));
            m_btHitPoint = unchecked((ushort)(m_btHitPoint +
                m_NativeCoreWorkingAbility.HitPoint));
        }

        private void ProjectNativeCoreCombatAbility()
        {
            ref var ability = ref m_NativeCoreWorkingAbility;
            m_WAbil.MaxHP = unchecked(m_Abil.MaxHP + ability.MaxHP);
            m_WAbil.MaxMP = unchecked(m_Abil.MaxMP + ability.MaxMP);
            m_WAbil.AC = PackNativeCoreEndpoints(m_Abil.AC,
                ability.ACLow, ability.ACHigh);
            m_WAbil.MAC = PackNativeCoreEndpoints(m_Abil.MAC,
                ability.MACLow, ability.MACHigh);
            m_WAbil.DC = PackNativeCoreEndpoints(m_Abil.DC,
                ability.DCLow, ability.DCHigh);
            m_WAbil.MC = PackNativeCoreEndpoints(m_Abil.MC,
                ability.MCLow, ability.MCHigh);
            m_WAbil.SC = PackNativeCoreEndpoints(m_Abil.SC,
                ability.SCLow, ability.SCHigh);
        }

        private static int PackNativeCoreEndpoints(int packedBase,
            int low, int high)
        {
            int projectedLow = unchecked(low + HUtil32.LoWord(packedBase));
            int projectedHigh = unchecked(high + HUtil32.HiWord(packedBase));
            return unchecked((ushort)projectedLow |
                ((int)(ushort)projectedHigh << 16));
        }
    }
}
