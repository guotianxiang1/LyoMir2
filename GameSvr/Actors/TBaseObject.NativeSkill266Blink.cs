using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Magic id 266, a short-range blink. Outer ladder arm 0x6BCA7E
    /// `66 8B 45 08 / 50 / 66 8B 4D 0C / 8B 55 F0 / 8B C6 / E8 0B 74 0B 00`
    /// = sub_773E9C(eax=Self, edx=UserMagic, cx=nTargetX, [ebp+8]=nTargetY),
    /// AL stored into [ebp-5] at 0x6BCA91.
    ///
    /// Mana is spent at 0x773EF5, BEFORE the reachability tests at
    /// 0x773F03/0x773F1E/0x773F38/0x773F62, so a blocked destination costs the
    /// caster the mana and answers FALSE.
    /// </summary>
    public partial class TBaseObject
    {
        /// <summary>0x773EB0 `BA 0A 01 00 00` — cold-time key 266.</summary>
        private const int NativeSkill266ColdTimeKey = 0x10A;

        /// <summary>dword[5] at 0x7D4D68, read at 0x773EDE.</summary>
        private static readonly int[] NativeSkill266ManaCosts =
            { 25, 25, 30, 35, 40 };

        /// <summary>0x773F08 `83 F8 06 / 0F 87 ja` — unsigned, so 0..6.</summary>
        private const int NativeSkill266MaxChebyshev = 6;

        /// <summary>0x6B750B `3D 58 1B 00 00` — the pickup lockout window.
        /// </summary>
        internal const int NativeSkill266PickupLockMilliseconds = 0x1B58;

        /// <summary>self+0x3E4 / +0x3E8 / +0x3EC, written at
        /// 0x773FF0 / 0x773FF9 / 0x774003 and read only by the pickup handler
        /// sub_6B74D8 at 0x6B7505 / 0x6B7512 / 0x6B751A.</summary>
        internal int m_dwNativeBlinkLandTick;
        internal int m_nNativeBlinkLandX;
        internal int m_nNativeBlinkLandY;

        internal bool TryActivateNativeSkill266Blink(TUserMagic userMagic,
            int targetX, int targetY)
        {
            return TryActivateNativeSkill266Blink(userMagic, targetX, targetY,
                HUtil32.GetTickCount());
        }

        internal bool TryActivateNativeSkill266Blink(TUserMagic userMagic,
            int targetX, int targetY, int now)
        {
            var envir = m_PEnvir;
            if (envir == null || userMagic?.MagicInfo == null)
            {
                return false;
            }

            // 0x773EBF `85 C0 / 0F 85 57 01 00 00` — silent refusal.
            if (GetNativeColdTimeRemaining(NativeSkill266ColdTimeKey) != 0)
            {
                return false;
            }

            int effectiveLevel =
                TPlayObject.GetNativeMagicProducerEffectiveLevel(userMagic);
            int costIndex = effectiveLevel < 4 ? effectiveLevel : 4;
            int cost = NativeSkill266ManaCosts[costIndex];
            // 0x773EE5 `3B 83 B4 02 00 00 / 0F 8F jg` — signed, cost > MP.
            if (cost > m_WAbil.MP)
            {
                return false;
            }
            DamageSpell(unchecked((ushort)cost));

            int dx = m_nCurrX - targetX;
            if (dx < 0)
                dx = -dx;
            int dy = m_nCurrY - targetY;
            if (dy < 0)
                dy = -dy;
            int chebyshev = dx >= dy ? dx : dy;
            if (unchecked((uint)chebyshev) > NativeSkill266MaxChebyshev)
            {
                SendNativeSkill266UnreachableHint();
                return false;
            }

            // 0x773F1E sub_777EBC, the terrain-only probe (GetMapCellInfo then
            // `80 38 00`), which is CanWalk with the object scan skipped.
            if (!envir.CanWalk(targetX, targetY, true))
            {
                SendNativeSkill266UnreachableHint();
                return false;
            }

            // 0x773F38 sub_778858 GetMovObjCount, `85 C0 / 0F 8F jg`.
            if (envir.GetNativeMovObjCount(targetX, targetY) > 0)
            {
                SendNativeSkill266UnreachableHint();
                return false;
            }

            // 0x773F62 sub_7797CC with `6A 01`, so the mover itself does not
            // re-test occupancy — 0x778858 above is the occupancy gate.
            if (envir.MoveToMovingObject(m_nCurrX, m_nCurrY, this, targetX,
                    targetY, true) <= 0)
            {
                SendNativeSkill266UnreachableHint();
                return false;
            }

            // 0x773F82..0x773F9A: (23 - 2*effectiveLevel) * 1000 ms.
            SetNativeColdTime(NativeSkill266ColdTimeKey,
                (0x17 - 2 * effectiveLevel) * 0x3E8, now);
            m_nCurrX = (short)targetX;
            m_nCurrY = (short)targetY;
            // 0x773FC6 `66 BA E5 0D` through VMT+0xE0, boSendSelf = 1, Series
            // carrying the magic id 0x10A.
            SendRefMsg(Grobal2.RM_NATIVE_BLINK_MOVE, NativeSkill266ColdTimeKey,
                targetX, targetY, 0, "");
            (this as TPlayObject)?.TrainNativeMagicProducer(userMagic,
                M2Share.RandomNumber.Random(3) + 1);
            m_dwNativeBlinkLandTick = now;
            m_nNativeBlinkLandX = targetX;
            m_nNativeBlinkLandY = targetY;
            return true;
        }

        /// <summary>
        /// 0x6B7500..0x6B752F. The pickup handler sub_6B74D8 refuses when the
        /// caster is still inside the 7000 ms window AND is reaching for the
        /// exact cell it blinked onto; all three tests must hold
        /// (`77 28 ja` then two `75 20 / 75 18 jne` skips).
        /// </summary>
        internal bool IsNativeBlinkPickupLocked(int nX, int nY, int now)
        {
            return unchecked((uint)(now - m_dwNativeBlinkLandTick)) <=
                       NativeSkill266PickupLockMilliseconds &&
                   nX == m_nNativeBlinkLandX &&
                   nY == m_nNativeBlinkLandY;
        }

        /// <summary>0x77400B `66 B9 FF 38` + string 0x774034, GBK length
        /// prefix 28 = 14 characters. Same text and colour as magic 168.
        /// </summary>
        private void SendNativeSkill266UnreachableHint()
        {
            if (this is TPlayObject)
            {
                SysMsg("目标位置不可达，技能使用失败", MsgColor.Red, MsgType.Hint);
            }
        }
    }
}
