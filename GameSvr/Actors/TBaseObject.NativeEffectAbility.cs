using SystemModule;

namespace GameSvr
{
    public partial class TBaseObject
    {
        internal bool m_boNativeUserMove;
        internal bool m_boNativeState26DirectStrong;
        internal bool m_boNativeState26DirectWeak;
        internal bool m_boNativeState26SingleStrong;
        internal bool m_boNativeState26SingleWeak;
        internal ushort m_wNativeState26DeadlineBonus;
        internal ushort m_wNativeType74MagicHit;
        internal ushort m_wNativeBaseMagicDamagePercent;
        internal short m_sNativeCriticalChance;
        internal int m_nNativeCriticalDamageIncrease;
        internal short m_sNativeAntiCriticalChance;
        internal short m_sNativeCriticalDamageReduction;
        internal int m_nNativeFlatMagicDamageIncrease;
        internal byte m_btNativeDamageIncreasePercent;
        internal byte m_btNativeDragonPossessionLevel;

        private void ApplyNativeEffectItemParameters(TUserItem userItem,
            GoodItem stdItem, ref TAddAbility addAbility)
        {
            MergeNativeCoreItem(stdItem);

            if ((stdItem.StdMode is 10 or 11 && stdItem.Shape == 43 &&
                    unchecked((byte)stdItem.Outlook) == 2) ||
                (stdItem.StdMode == 7 && stdItem.Shape == 7 &&
                    userItem.Dura > 0))
            {
                addAbility.NativeBaseMagicDamagePercent = unchecked((ushort)(
                    addAbility.NativeBaseMagicDamagePercent +
                    stdItem.WordParam1));
            }

            ApplyNativeShieldShapeParameters(stdItem, ref addAbility);

            for (var index = 0; index < 6; index++)
            {
                var propertyId = stdItem.NativeItemExtAbilIdents[index];
                if (!IsNativeEffectPropertyId(propertyId))
                    break;

                ApplyNativeEffectProperty(propertyId,
                    stdItem.NativeItemExtAbilValues[index], ref addAbility);
                MergeNativeCoreEffectProperty(propertyId,
                    stdItem.NativeItemExtAbilValues[index]);
            }

            if ((stdItem.StdMode == 5 || stdItem.StdMode == 6) &&
                userItem.btValue[7] != 61 &&
                userItem.btValue[7] != 62 &&
                userItem.btValue[7] != 63)
            {
                AddEffectStrength(ref addAbility, stdItem.WordParam1);
            }

            if (stdItem.StdMode == 29 && stdItem.Shape == 1)
            {
                AddEffectStrength(ref addAbility,
                    unchecked((ushort)stdItem.IntParam1));
            }
        }

        private static void ApplyNativeShieldShapeParameters(GoodItem stdItem,
            ref TAddAbility addAbility)
        {
            bool necklace = stdItem.StdMode is 19 or 20 or 21;
            bool ring = stdItem.StdMode is 22 or 23;
            bool armRing = stdItem.StdMode is 24 or 26;

            if (necklace && stdItem.Shape == 121)
            {
                addAbility.NativeSearchHuman = true;
            }

            if ((ring || armRing) && stdItem.Shape is 118 or 206 ||
                stdItem.StdMode == 30 && stdItem.Shape == 201)
            {
                addAbility.NativeStandardMagicShield = true;
            }

            if (ring && stdItem.Shape is 125 or 208 ||
                armRing && stdItem.Shape is 125 or 208)
            {
                addAbility.NativeFullMagicShield = true;
            }

            if (ring && stdItem.Shape is 121 or 207 ||
                armRing && stdItem.Shape is 207 or 209)
            {
                addAbility.NativeHalfMagicShield = true;
            }

            if (ring && stdItem.Shape == 112)
            {
                addAbility.NativeUserMove = true;
            }
        }

        private static bool IsNativeEffectPropertyId(ushort propertyId) =>
            (propertyId >= 1 && propertyId <= 158) || propertyId == 254;

        private static void ApplyNativeEffectProperty(ushort propertyId,
            ushort value, ref TAddAbility addAbility)
        {
            switch (propertyId)
            {
                case 21:
                    addAbility.NativeMagicHitHealAmount = unchecked((ushort)(
                        addAbility.NativeMagicHitHealAmount + value));
                    break;
                case 27:
                    addAbility.NativeBreakPower = unchecked((ushort)(
                        addAbility.NativeBreakPower + value));
                    break;
                case 30:
                    addAbility.wAntiPoison = unchecked((ushort)(
                        addAbility.wAntiPoison + value));
                    break;
                case 31:
                    // Native keeps property 31 in the local fixed record only.
                    break;
                case 39:
                    addAbility.NativeCrazyPower = unchecked((ushort)(
                        addAbility.NativeCrazyPower + value));
                    break;
                case 53:
                    addAbility.NativeBreakThroughChance = unchecked((ushort)(
                        addAbility.NativeBreakThroughChance + value));
                    break;
                case 54:
                    AddEffectStrength(ref addAbility, value);
                    break;
                case 64:
                    addAbility.NativeHumanMagicPercentReductionRaw = unchecked(
                        addAbility.NativeHumanMagicPercentReductionRaw + value);
                    break;
                case 67:
                    addAbility.NativeSteelBodyReduction = unchecked((ushort)(
                        addAbility.NativeSteelBodyReduction + value));
                    break;
                case 70:
                    addAbility.NativeAwakening = true;
                    break;
                case 71:
                    addAbility.NativeFlatMagicDamageIncrease = unchecked(
                        addAbility.NativeFlatMagicDamageIncrease + value);
                    break;
                case 72:
                    addAbility.NativeStandardMagicShield = true;
                    break;
                case 73:
                    addAbility.NativeFullMagicShield = true;
                    break;
                case 75:
                    addAbility.NativeUnionFastnessSelector = unchecked((ushort)(
                        addAbility.NativeUnionFastnessSelector + value));
                    break;
                case 76:
                    addAbility.NativeHqFastnessSelector = unchecked((ushort)(
                        addAbility.NativeHqFastnessSelector + value));
                    break;
                case 77:
                    addAbility.NativeNearHitFastnessSelector = unchecked((ushort)(
                        addAbility.NativeNearHitFastnessSelector + value));
                    break;
                case 78:
                    addAbility.NativeGoldenBellReduction = unchecked((ushort)(
                        addAbility.NativeGoldenBellReduction + value));
                    break;
                case 79:
                    addAbility.NativeDragonBodyReduction = unchecked(
                        addAbility.NativeDragonBodyReduction + value);
                    break;
                case 86:
                    if (value > addAbility.NativeState26DeadlineBonus)
                    {
                        addAbility.NativeState26DeadlineBonus = value;
                    }
                    break;
                case 88:
                case 89:
                    addAbility.NativeState26SingleWeak = true;
                    break;
                case 90:
                    addAbility.NativeDamageIncreasePercent = unchecked((byte)(
                        addAbility.NativeDamageIncreasePercent + (byte)value));
                    break;
                case 98:
                    addAbility.NativeCriticalChance = unchecked(
                        addAbility.NativeCriticalChance + value);
                    break;
                case 99:
                    addAbility.NativeCriticalDamageIncrease = unchecked(
                        addAbility.NativeCriticalDamageIncrease + value);
                    break;
                case 100:
                    addAbility.NativeAntiCriticalChance = unchecked(
                        addAbility.NativeAntiCriticalChance + value);
                    break;
                case 101:
                    addAbility.NativeCriticalDamageReduction = unchecked(
                        addAbility.NativeCriticalDamageReduction + value);
                    break;
                case 102:
                    addAbility.NativeMagicFastnessSelector = unchecked((ushort)(
                        addAbility.NativeMagicFastnessSelector + value));
                    break;
                case 103:
                    addAbility.NativeSoulFastnessSelector = unchecked((ushort)(
                        addAbility.NativeSoulFastnessSelector + value));
                    break;
                case 117:
                    addAbility.NativeDragonPossessionLevel = unchecked((byte)
                        Math.Max(addAbility.NativeDragonPossessionLevel, value));
                    break;
                case 141:
                    addAbility.NativeMagicDamageReductionPercent = unchecked(
                        (byte)(addAbility.NativeMagicDamageReductionPercent +
                            (byte)value));
                    break;
                case 158:
                    addAbility.NativeType74MagicHit = unchecked((ushort)(
                        addAbility.NativeType74MagicHit + value));
                    break;
                case 254:
                    switch (value & 0x7F)
                    {
                        case 0:
                            addAbility.NativeHalfMagicShield = true;
                            break;
                        case 3:
                            addAbility.NativeSearchHuman = true;
                            break;
                        case 4:
                            addAbility.NativeUserMove = true;
                            break;
                        case 5:
                            addAbility.NativeState26DirectStrong = true;
                            break;
                        case 1:
                            addAbility.NativeState26DirectWeak = true;
                            break;
                        case 6:
                            addAbility.NativeState26SingleStrong = true;
                            break;
                    }
                    break;
            }
        }

        private static void AddEffectStrength(ref TAddAbility addAbility, ushort value)
        {
            addAbility.wEffectStrength = unchecked((ushort)(
                addAbility.wEffectStrength + value));
        }
    }
}
