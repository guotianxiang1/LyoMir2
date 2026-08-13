using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Magic id 267. Outer arm 0x6BCAB2 `E8 98 75 0B 00 call 0x774054`.
    /// Cost table dword[5] at 0x7D4D7C. MP compare is signed `jge`, so
    /// cost == MP also refuses.
    /// </summary>
    public partial class TBaseObject
    {
        private const int NativeSkill267ColdTimeKey = 0x10B;
        private const byte NativeSkill267State = 0x46;
        private const int NativeSkill267StateMilliseconds = 0x3A98;
        private const int NativeSkill267CompanionMagicId = 0x104;
        private const byte NativeSkill267CompanionState = 0x41;
        private static readonly int[] NativeSkill267ManaCosts =
            { 25, 25, 30, 35, 40 };

        internal bool TryActivateNativeSkill267(TUserMagic userMagic)
        {
            return TryActivateNativeSkill267(userMagic,
                HUtil32.GetTickCount());
        }

        internal bool TryActivateNativeSkill267(TUserMagic userMagic,
            int now)
        {
            if (userMagic == null)
            {
                return false;
            }

            if (GetNativeColdTimeRemaining(NativeSkill267ColdTimeKey) != 0)
            {
                return false;
            }

            int effectiveLevel =
                TPlayObject.GetNativeMagicProducerEffectiveLevel(userMagic);
            int costIndex = effectiveLevel < 4 ? effectiveLevel : 4;
            int cost = NativeSkill267ManaCosts[costIndex];
            // 0x77409F `0F 8D A6 00 00 00 jge` — signed >= .
            if (cost >= m_WAbil.MP)
            {
                return false;
            }

            DamageSpell((ushort)cost);
            SetNativeColdTime(NativeSkill267ColdTimeKey,
                (0x1E - effectiveLevel * 3) * 1000, now);
            AddTimedAbilityInternal(NativeSkill267State, 1,
                NativeSkill267StateMilliseconds, 0);
            if (GetMagicInfo(NativeSkill267CompanionMagicId) != null)
            {
                AddTimedAbilityInternal(NativeSkill267CompanionState, 1,
                    (effectiveLevel + 1) * 2 * 1000, 0);
            }
            (this as TPlayObject)?.TrainNativeMagicProducer(userMagic,
                M2Share.RandomNumber.Random(3) + 1);
            return true;
        }
    }
}
