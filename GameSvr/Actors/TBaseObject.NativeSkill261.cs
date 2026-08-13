using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Magic id 261. Outer arm 0x6BCA5A `E8 44 72 0B 00 call 0x773CA8`.
    /// Cost table dword[5] at 0x7D4D54, indexed by Min(effLevel, 4)
    /// (`0x4C700C cmp edx,eax / jg keep-eax / mov eax,edx`).
    /// </summary>
    public partial class TBaseObject
    {
        private const byte NativeSkill261State = 0x40;
        private const int NativeSkill261CooldownMilliseconds = 0x7530;
        private static readonly int[] NativeSkill261ManaCosts =
            { 25, 25, 30, 35, 40 };

        internal bool TryActivateNativeSkill261(TUserMagic userMagic)
        {
            return TryActivateNativeSkill261(userMagic,
                HUtil32.GetTickCount());
        }

        internal bool TryActivateNativeSkill261(TUserMagic userMagic,
            int now)
        {
            if (userMagic?.MagicInfo == null)
            {
                return false;
            }

            int key = userMagic.MagicInfo.wMagicID;
            int remaining = GetNativeColdTimeRemaining(key);
            int effectiveLevel =
                TPlayObject.GetNativeMagicProducerEffectiveLevel(userMagic);
            int costIndex = effectiveLevel < 4 ? effectiveLevel : 4;
            int cost = NativeSkill261ManaCosts[costIndex];
            if (remaining != 0 || cost > m_WAbil.MP)
            {
                return false;
            }

            DamageSpell((ushort)cost);
            // 0x773D0A `66 BA 42 30 mov dx,0x3042` then VMT+0xD8. That
            // slot is not the wire send at +0x250/+0x254, and 0x6DC590
            // was not mapped onto a C# SendMsg/SendRefMsg ident. The
            // packet is omitted rather than invented; state and cooldown
            // below still match the rest of the arm.
            AddTimedAbilityInternal(NativeSkill261State, 1,
                (effectiveLevel + 1) * 5 * 1000, 0);
            SetNativeColdTime(key, NativeSkill261CooldownMilliseconds, now);
            (this as TPlayObject)?.TrainNativeMagicProducer(userMagic,
                M2Share.RandomNumber.Random(3) + 1);
            return true;
        }
    }
}
