using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Magic id 265. Outer ladder arm 0x6BCAF1; the body is TPlayObject
    /// VMT+0x15C = sub_774158 (`FF 93 5C 01 00 00` at 0x6BCB28), which returns
    /// the number of actors it managed to shove rather than a Boolean.
    /// </summary>
    public partial class TBaseObject
    {
        /// <summary>0x6BCB02 `3D D0 07 00 00` / `76 74 jbe` — the ladder needs
        /// strictly more than 2000 ms since the last use of the shared tick.
        /// </summary>
        private const int NativeSkill265GateMilliseconds = 0x7D0;

        /// <summary>dword[5] at 0x7D4D90, read at 0x774181 after
        /// `0x4C700C` clamps the effective level to 4.</summary>
        private static readonly int[] NativeSkill265ManaCosts =
            { 15, 15, 20, 25, 30 };

        /// <summary>0x774240 `83 C1 02` — the shove distance is
        /// effectiveLevel + 2, uncapped.</summary>
        private const int NativeSkill265PushBonus = 2;

        internal bool TryActivateNativeSkill265(TUserMagic userMagic,
            int direction)
        {
            return TryActivateNativeSkill265(userMagic, direction,
                HUtil32.GetTickCount());
        }

        /// <summary>
        /// The ladder arm itself. 0x6BCAFC reads the SAME dword the 野蛮冲撞
        /// arm uses (`2B 86 70 03 00 00` here, `2B 86 70 03 00 00` at 0x6BC938
        /// with a 0x1194 bound), so the two skills share one throttle.
        /// </summary>
        internal bool TryActivateNativeSkill265(TUserMagic userMagic,
            int direction, int now)
        {
            if (unchecked(now - m_dwDoMotaeboTick) <=
                NativeSkill265GateMilliseconds)
            {
                return false;
            }

            // 0x6BCB09 `8A 45 0C` — the low byte of nTargetX is the heading,
            // written before the body runs and kept even if nothing is hit.
            m_btDirection = (byte)direction;
            m_dwDoMotaeboTick = now;
            // 0x6BCB1B `C6 45 FB 01` precedes the body call, so the CM_SPELL
            // answer is TRUE regardless of what the body returns.
            RunNativeSkill265Push(userMagic);
            if (userMagic?.MagicInfo != null)
            {
                // 0x6BCB73 `E8 E0 C6 0A 00 call 0x769258`, the same emitter
                // magic 237 uses, with X/Y taken from self.
                SendRefMsg(Grobal2.RM_SPELL, userMagic.MagicInfo.btEffect,
                    m_nCurrX, m_nCurrY, userMagic.MagicInfo.wMagicID, "");
            }
            return true;
        }

        /// <summary>sub_774158. Returns the number of actors shoved.</summary>
        private int RunNativeSkill265Push(TUserMagic userMagic)
        {
            var envir = m_PEnvir;
            if (envir == null || userMagic?.MagicInfo == null)
            {
                return 0;
            }

            int effectiveLevel =
                TPlayObject.GetNativeMagicProducerEffectiveLevel(userMagic);
            int costIndex = effectiveLevel < 4 ? effectiveLevel : 4;
            int cost = NativeSkill265ManaCosts[costIndex];
            // 0x774188 `3B 83 B4 02 00 00` / `0F 8F jg` — signed, cost > MP.
            if (cost > m_WAbil.MP)
            {
                return 0;
            }
            DamageSpell(unchecked((ushort)cost));

            int pushed = 0;
            for (int offset = -1; offset <= 1; offset++)
            {
                int dir = m_btDirection + offset;
                if (dir > 7)
                    dir = 0;
                if (dir < 0)
                    dir = 7;
                short cellX = 0;
                short cellY = 0;
                envir.GetNextPosition(m_nCurrX, m_nCurrY, dir, 1, ref cellX,
                    ref cellY);
                // 0x7741ED sub_7784A8 with `6A 01` as the last stack slot.
                if (!(envir.GetMovingObject(cellX, cellY, true) is TBaseObject
                        target))
                {
                    continue;
                }
                if (!CanNativeSkill265Shove(target))
                {
                    continue;
                }
                // 0x77422D sub_764A90, the sign helper, aimed from self at the
                // victim; then VMT+0xA4 = CharPushed with effectiveLevel + 2.
                byte pushDir = M2Share.GetNextDirection(m_nCurrX, m_nCurrY,
                    target.m_nCurrX, target.m_nCurrY);
                target.CharPushed(pushDir,
                    effectiveLevel + NativeSkill265PushBonus);
                pushed++;
            }

            // 0x774260 `83 7D F8 00 / 7E 17` — training only when something moved.
            if (pushed > 0)
            {
                (this as TPlayObject)?.TrainNativeMagicProducer(userMagic,
                    M2Share.RandomNumber.Random(3) + 1);
            }
            return pushed;
        }

        /// <summary>
        /// Target VMT+0xB8 = sub_768F50, invoked at 0x774204 with edx = the
        /// caster and ecx = 0. Two of its three terms are portable:
        ///   0x768F67/0x768F6E  casterLevel - targetLevel, `test edi,edi /
        ///                      jle` so the caster must be strictly higher
        ///                      (word[obj+0x278] is m_WAbil.Level)
        ///   0x768FB9           sub_767498 IsProperTarget(caster, target)
        ///
        /// BLOCKED: the middle term `0x768FAE 80 7E 75 00 cmp byte [esi+0x75],0
        /// / 75 12 jne` has no C# field. That byte is set to 1 by about a dozen
        /// monster constructors (0x63D8CE, 0x668921, 0x674C0E, 0x68074C, ...)
        /// and never cleared, and it also gates the CharPushed at 0x6747FD, but
        /// nothing in the C# actor carries it. Its absence only makes 265 more
        /// permissive against those specific monsters.
        ///
        /// The `0x768F89..0x768FA8` middle block compares two names against
        /// [0x73BBE8] and then throws its own result away (`84 C0` at 0x768FA8
        /// is immediately overwritten by `84 DB` at 0x768FAA), so it has no
        /// effect and is not ported.
        /// </summary>
        private bool CanNativeSkill265Shove(TBaseObject target)
        {
            return m_WAbil.Level > target.m_WAbil.Level &&
                   IsProperTarget(target);
        }
    }
}
