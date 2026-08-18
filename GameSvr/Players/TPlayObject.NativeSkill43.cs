using System;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        internal int TryProduceNativeMagic43(TUserMagic userMagic)
        {
            int effectiveLevel = GetNativeMagicProducerEffectiveLevel(userMagic);
            int threshold = unchecked(effectiveLevel * 15 + 55);
            int radius = unchecked((ushort)(effectiveLevel + 2));
            int hitCount = 0;

            for (int i = 0; i < m_VisibleActors.Count; i++)
            {
                var target = m_VisibleActors[i]?.BaseObject;
                if (target == null || !target.IsNativeMagic43Target(this))
                {
                    continue;
                }

                // Native consumes the probability draw before checking range,
                // including at effective level 3 where every roll succeeds.
                if (M2Share.RandomNumber.Random(100) > threshold)
                {
                    continue;
                }

                if (Math.Abs(target.m_nCurrX - m_nCurrX) > radius ||
                    Math.Abs(target.m_nCurrY - m_nCurrY) > radius)
                {
                    continue;
                }

                target.NativeMakePosion(0x1A, 5, 0);
                // sub_6EC4FC counts the call, even when MakePosion refuses it.
                hitCount++;
            }

            if (hitCount > 0)
            {
                TrainSkill(userMagic, M2Share.RandomNumber.Random(3) + 1);
            }

            return hitCount;
        }
    }
}
