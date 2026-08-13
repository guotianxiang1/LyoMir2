using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Magic id 262 致残毒药. Outer arm 0x6BCA6C `E8 06 73 0B 00 call 0x773D7C`.
    /// No cooldown probe. Flat MP cost 0x14, `jle` so MP==20 fails.
    /// </summary>
    public partial class TBaseObject
    {
        private const int NativeSkill262ManaCost = 0x14;
        private const byte NativeSkill262State = 0x42;

        internal bool TryActivateNativeSkill262Poison(TUserMagic userMagic)
        {
            // 0x773D9A `83 BB B4 02 00 00 14 / 0F 8E 9B 00 00 00 jle`.
            if (userMagic?.MagicInfo == null ||
                m_WAbil.MP <= NativeSkill262ManaCost)
            {
                return false;
            }

            DamageSpell(unchecked((ushort)NativeSkill262ManaCost));
            int effectiveLevel =
                TPlayObject.GetNativeMagicProducerEffectiveLevel(userMagic);
            int durationMs = (effectiveLevel + 1) * 0x3C * 1000;
            AddTimedAbilityInternal(NativeSkill262State, effectiveLevel,
                durationMs, 0);
            (this as TPlayObject)?.TrainNativeMagicProducer(userMagic,
                M2Share.RandomNumber.Random(3) + 1);
            // 0x773E08 `B9 E8 03 00 00` / `99` / `F7 F9` — SIGNED.
            int seconds = durationMs / 1000;
            SendNativeSkill262Hint("致残毒药使用成功， 持续" + seconds + "秒");
            return true;
        }

        /// <summary>VMT+0xD4 with cx=0xFFDB at 0x773E30.</summary>
        private void SendNativeSkill262Hint(string text)
        {
            if (this is TPlayObject)
            {
                SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, 0xDB, 0xFF, 0, text);
            }
        }
    }
}
