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
            // 0x773D0A `66 BA 42 30` through VMT+0xD8 = sub_6DC590, which
            // enqueues an internal message on every nearby actor (0x765E68 /
            // 0x76533C), exactly what C# SendRefMsg does. All five stack
            // slots are `6A 00` at 0x773CFE..0x773D06, and cx is 0.
            //
            // Note the ordering: the broadcast at 0x773D12 runs BEFORE the
            // state is applied at 0x773D38, so nobody is filtered out of it;
            // by the time a receiver processes the message the state is up and
            // its own sub_774288 test decides.
            SendRefMsg(Grobal2.RM_NATIVE_STEALTH_VANISH, 0, 0, 0, 0, "");
            AddTimedAbilityInternal(NativeSkill261State, 1,
                (effectiveLevel + 1) * 5 * 1000, 0);
            SetNativeColdTime(key, NativeSkill261CooldownMilliseconds, now);
            (this as TPlayObject)?.TrainNativeMagicProducer(userMagic,
                M2Share.RandomNumber.Random(3) + 1);
            return true;
        }

        /// <summary>
        /// sub_774288(eax = the actor being looked at, edx = the viewer). Two
        /// terms, both required:
        ///   0x774291  `B2 40` + sub_772960 -> the actor must hold state 0x40
        ///   0x7742AC  sub_76B4A4 Chebyshev to the viewer
        ///   0x7742B1  `83 F8 02 / 77 04 ja` -> strictly more than 2 cells
        /// It is the gate on the 0x3042 arm at 0x6B606B and the per-viewer
        /// exclusion inside both broadcast slots (0x6DC247 in VMT+0xE0,
        /// 0x6DC6F1 in VMT+0xD8), i.e. state 0x40 is a stealth flag.
        /// </summary>
        internal bool IsNativeStealthedFrom(TBaseObject viewer)
        {
            if (viewer == null || !HasNativeActiveState(NativeSkill261State))
            {
                return false;
            }
            int dx = m_nCurrX - viewer.m_nCurrX;
            if (dx < 0)
                dx = -dx;
            int dy = m_nCurrY - viewer.m_nCurrY;
            if (dy < 0)
                dy = -dy;
            return (dx >= dy ? dx : dy) > 2;
        }
    }
}
