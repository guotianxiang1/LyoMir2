using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Magic id 273 升龙破. Outer arm 0x6BCA46 calls TPlayer VMT+0x288 =
    /// 0x6EF834 and does NOT store AL into [ebp-5], so the CM_SPELL
    /// result stays the 0 written at 0x6BC59F even when the callee
    /// runs its success path. The callee itself also ends with
    /// `33 C0` at 0x6EF91A.
    /// </summary>
    public partial class TBaseObject
    {
        private const int NativeSkill273ColdTimeKey = 0x111;
        private const int NativeSkill273CooldownMilliseconds = 0x2BF20;

        internal bool TryActivateNativeSkill273DragonBreak(TUserMagic userMagic,
            TBaseObject target)
        {
            return TryActivateNativeSkill273DragonBreak(userMagic, target,
                HUtil32.GetTickCount());
        }

        internal bool TryActivateNativeSkill273DragonBreak(TUserMagic userMagic,
            TBaseObject target, int now)
        {
            // Always returns false: outer never stores AL, and the callee
            // zeroes EAX on every path. Side effects below still run.
            if (userMagic == null)
            {
                return false;
            }
            if (!IsProperTarget(target))
            {
                return false;
            }

            int effectiveLevel =
                TPlayObject.GetNativeMagicProducerEffectiveLevel(userMagic);
            // 0x6EF876 `48` / `2C 07` / `0F 83 9B 00 00 00 jae` on AL
            // after a 32-bit `dec eax`. Level 0 underflows al to 0xFF and
            // is rejected; 1..7 pass; >=8 is rejected.
            if (unchecked((byte)(effectiveLevel - 1)) >= 7)
            {
                return false;
            }

            int remaining = GetNativeColdTimeRemaining(
                NativeSkill273ColdTimeKey);
            if (remaining != 0)
            {
                // 0x6EF8E4 `B9 E8 03 00 00` / `99` / `F7 F9` — SIGNED.
                int seconds = remaining / 1000;
                SendNativeSkill273Hint("还需要" + seconds + "秒才能释放升龙破");
                return false;
            }

            // 0x6EF897 `8B 15 E8 BB 73 00` / `E8 86 4F D1 FF call 0x404828`
            // is `target is THumanKind`. On a hit it calls VMT+0x1E4 with
            // word[effLevel*4+0x7D3DA0], but TPlayer/THumanKind VMT+0x1E4
            // is 0x746124 `E8 B0 E2 02 00 call 0x7743DC` and 0x7743DC is
            // `C3`. The call is a no-op; cooldown still arms for any
            // proper target, human or not.
            SetNativeColdTime(NativeSkill273ColdTimeKey,
                NativeSkill273CooldownMilliseconds, now);
            return false;
        }

        /// <summary>VMT+0xD4 with cx=0xFFDB at 0x6EF90C.</summary>
        private void SendNativeSkill273Hint(string text)
        {
            if (this is TPlayObject)
            {
                SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, 0xDB, 0xFF, 0, text);
            }
        }
    }
}
