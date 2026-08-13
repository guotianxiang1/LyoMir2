using System;
using SystemModule;

namespace GameSvr
{
    internal sealed class NativeMagicProducerPushPayload
    {
        internal NativeMagicProducerPushPayload(TBaseObject target,
            byte direction)
        {
            Target = target;
            Direction = direction;
        }

        internal TBaseObject Target { get; }
        internal byte Direction { get; }
    }

    public partial class TPlayObject
    {
        internal const int NativeMagicProducerPushIdent = 10417;

        /// <summary>Native self+0x50C: last-use tick stamp for the magic id 62
        /// producer's 30-second gate. Read at 0x6EDC7A
        /// (2B 83 0C 05 00 00 = sub eax,[ebx+0x50C]) and re-stamped on success.
        /// Same shape as skill 111's +0x510 stamp
        /// (m_dwNativeSkill111LastRecallTick), which is a plain per-object dword
        /// rather than a coldTime-table entry.</summary>
        internal int m_dwMagic62LastTick;

        internal static ushort GetNativeMagicProducerMpCost(
            TUserMagic magic)
        {
            return unchecked((ushort)(magic.MagicInfo.btDefSpell +
                HUtil32.Round((double)magic.MagicInfo.wSpell / 4.0d *
                    (magic.btLevel + 1))));
        }

        internal static int GetNativeMagicProducerEffectiveLevel(
            TUserMagic magic)
        {
            return Math.Min(
                unchecked((byte)(magic.btLevel + magic.NativeLevelBonus)),
                magic.MagicInfo.btTrainLv);
        }

        internal static int CalculateNativeMagicProducerSkillPower(
            TUserMagic magic)
        {
            int magicPower = unchecked(magic.MagicInfo.wPower +
                NextNativeMagicProducerRandom(
                    magic.MagicInfo.wMaxPower - magic.MagicInfo.wPower));
            int defaultPower = NextNativeMagicProducerRandom(
                magic.MagicInfo.btDefMaxPower - magic.MagicInfo.btDefPower);
            return unchecked(magic.MagicInfo.btDefPower +
                HUtil32.Round((double)(magic.btLevel + 1) * magicPower /
                    4.0d) + defaultPower);
        }

        // Native sub_4C870C (@0x4C870C-0x4C875C): the externally-supplied-power
        // twin of CalculateNativeMagicProducerSkillPower (sub_4C8658). It is a
        // DISTINCT native function, not an alias, and differs in exactly two ways:
        //   * the default-power roll is drawn FIRST (@0x4C8725), before the
        //     level-scaled term, so the RandSeed order is inverted vs sub_4C8658;
        //   * the level factor is the EFFECTIVE level (sub_4C896C @0x4C872D,
        //     btLevel + NativeLevelBonus clamped to btTrainLv), not raw btLevel.
        // Divisor is `fdiv dword ptr [0x4C8760]` @0x4C8740 — a 4-byte float32
        // holding raw bytes 00 00 80 40 = 4.0f. btTrainLv is NEVER read as a
        // divisor: it only ever appears as the level CAP inside sub_4C896C.
        // Rounding is sub_403574 = `fistp qword` (round-half-to-even) = HUtil32.Round.
        // Native call sites: 魔法盾 shield power @0x76FA06 and 地火 fire-cross
        // hold-time @0x77069B (both pass an externally computed nPower).
        internal static int CalculateNativeMagicProducerScaledPower(
            TUserMagic magic, int basePower)
        {
            int defaultPower = NextNativeMagicProducerRandom(
                magic.MagicInfo.btDefMaxPower - magic.MagicInfo.btDefPower);
            return unchecked(defaultPower +
                HUtil32.Round((double)(
                    GetNativeMagicProducerEffectiveLevel(magic) + 1) *
                    basePower / 4.0d) + magic.MagicInfo.btDefPower);
        }

        // Native sub_4C8764 (@0x4C8764-0x4C87BB) — the "type 13" power helper.
        // Exact body order:
        //   @0x4C877D  Random(btDefMaxPower - btDefPower)   <- drawn FIRST
        //   @0x4C8787  += btDefPower                        (pushed as one term)
        //   @0x4C878C  sub_4C896C  = EFFECTIVE level (btLevel + NativeLevelBonus,
        //                            clamped to btTrainLv)
        //   @0x4C8796  add eax,eax / add eax,3 / add eax,3  = 2*effLevel + 6
        //   @0x4C879E  imul edi                             = * nInt
        //   @0x4C87A6  fdiv dword ptr [0x4C87BC]            = float32 12.0f
        //                                                     (raw 00 00 40 41)
        //   @0x4C87AC  sub_403574 = fistp qword (round-half-to-even)
        //   @0x4C87B4  + the default-power term
        // There is NO btTrainLv divisor and no nInt/3 split anywhere in the body.
        // Governs 降魔/地狱雷光/神圣战甲/隐身/大隐身 power AND the 隐身 duration.
        internal static int CalculateNativeMagicProducer13Power(
            TUserMagic magic, int nInt)
        {
            int defaultPower = unchecked(NextNativeMagicProducerRandom(
                magic.MagicInfo.btDefMaxPower - magic.MagicInfo.btDefPower) +
                magic.MagicInfo.btDefPower);
            return unchecked(defaultPower +
                HUtil32.Round((double)(2 *
                    GetNativeMagicProducerEffectiveLevel(magic) + 6) *
                    nInt / 12.0d));
        }

        internal int NativeLuckOnlyRoll(int basePower, int spread)
        {
            spread = Math.Max(spread, 0);
            if (m_nLuck > 0)
            {
                if (NextNativeMagicProducerRandom(
                        10 - Math.Min(9, m_nLuck)) == 0)
                    return unchecked(basePower + spread);

                return unchecked(basePower +
                    NextNativeMagicProducerRandom(spread + 1));
            }

            int result = unchecked(basePower +
                NextNativeMagicProducerRandom(spread + 1));
            if (m_nLuck < 0 &&
                NextNativeMagicProducerRandom(
                    10 - Math.Max(0, -m_nLuck)) == 0)
                result = basePower;
            return result;
        }

        internal bool TryProduceNativeMagic1Or5(TUserMagic magic,
            TBaseObject target)
        {
            if (!TryAdmitNativeMagicProducerTarget(target, true))
                return false;

            int rawDamage = CalculateNativeMagicProducerRawDamage(magic);
            QueueNativeMagicProducerEffect(magic, target, rawDamage);
            if (target.m_btRaceServer >= Grobal2.RC_ANIMAL)
            {
                TrainNativeMagicProducer(magic,
                    NextNativeMagicProducerRandom(3) + 1);
            }
            return true;
        }

        internal bool TryProduceNativeMagic11(TUserMagic magic,
            TBaseObject target)
        {
            if (!TryAdmitNativeMagicProducerTarget(target, false))
                return false;

            int effectiveLevel = GetNativeMagicProducerEffectiveLevel(magic);
            int rawDamage = CalculateNativeMagicProducerRawDamage(magic);
            if (effectiveLevel == 4)
            {
                if (!IsNativeMagicProducerHumanKind(target))
                    rawDamage = unchecked(rawDamage * 2);
            }
            else if (target.m_btLifeAttrib == Grobal2.LA_UNDEAD)
            {
                rawDamage = HUtil32.Round(rawDamage * 1.5d);
            }

            QueueNativeMagicProducerEffect(magic, target, rawDamage);
            if (target.m_btRaceServer > Grobal2.RC_ANIMAL)
            {
                TrainNativeMagicProducer(magic,
                    NextNativeMagicProducerRandom(3) + 1);
            }
            return true;
        }

        internal bool TryProduceNativeMagic35(TUserMagic magic,
            TBaseObject target)
        {
            if (!TryAdmitNativeMagicProducerTarget(target, false))
                return false;

            int effectiveLevel = GetNativeMagicProducerEffectiveLevel(magic);
            int rawDamage = CalculateNativeMagicProducerRawDamage(magic);
            int lowMagic = HUtil32.LoWord(m_WAbil.MC);
            int highMagic = HUtil32.HiWord(m_WAbil.MC);
            if (effectiveLevel == 4)
            {
                rawDamage = unchecked(rawDamage +
                    NativeLuckOnlyRoll(lowMagic, highMagic - lowMagic) / 4);
            }
            if (target.m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                rawDamage = HUtil32.Round(rawDamage * 1.25d);
            if (effectiveLevel == 5)
                rawDamage = HUtil32.Round(rawDamage * 1.1d);
            if (effectiveLevel == 6)
                rawDamage = HUtil32.Round(rawDamage * 1.2d);

            int spellDamage = rawDamage / 2;
            if (effectiveLevel == 5)
                spellDamage = HUtil32.Round(spellDamage * 1.1d);
            if (effectiveLevel == 6)
                spellDamage = HUtil32.Round(spellDamage * 1.2d);
            DamageNativeMagicProducerSpell(target, spellDamage);

            QueueNativeMagicProducerEffect(magic, target, rawDamage);
            if (target.m_btRaceServer > Grobal2.RC_ANIMAL)
            {
                TrainNativeMagicProducer(magic,
                    NextNativeMagicProducerRandom(3) + 1);
            }
            return true;
        }

        internal bool TryProduceNativeMagic39(TUserMagic magic,
            TBaseObject target)
        {
            if (!TryAdmitNativeMagicProducerTarget(target, false))
                return false;

            int rawDamage = CalculateNativeMagicProducerRawDamage(magic);
            if (target.m_btLifeAttrib == Grobal2.LA_UNDEAD)
                rawDamage = HUtil32.Round(rawDamage * 1.2d);

            QueueNativeMagicProducerEffect(magic, target, rawDamage);
            TrainNativeMagicProducer(magic,
                NextNativeMagicProducerRandom(3) + 1);
            int effectiveLevel = GetNativeMagicProducerEffectiveLevel(magic);
            if (effectiveLevel >= 3 && m_Abil.Level > target.m_Abil.Level &&
                NextNativeMagicProducerRandom(100) <= 30)
            {
                byte direction = M2Share.GetNextDirection(m_nCurrX,
                    m_nCurrY, target.m_nCurrX, target.m_nCurrY);
                SendDelayMsg(this, NativeMagicProducerPushIdent, direction,
                    1, 0, 0, string.Empty, 700,
                    new NativeMagicProducerPushPayload(target, direction));
            }
            return true;
        }

        internal bool TrainNativeMagicProducer(TUserMagic magic,
            int trainingPoints)
        {
            if (magic?.MagicInfo == null ||
                !TryGetNativeMagicProducerTrainingValue(
                    magic.MagicInfo.TrainLevel, magic.btLevel,
                    out int requiredActorLevel) ||
                m_Abil.Level < requiredActorLevel)
                return false;

            int awardedPoints = m_boFastTrain
                ? unchecked(trainingPoints * 3)
                : trainingPoints;
            magic.nTranPoint = unchecked(magic.nTranPoint + awardedPoints);

            bool crossedThreshold = false;
            bool leveled = false;
            int requiredTraining = GetNativeMagicProducerRequiredTraining(magic);
            while (requiredTraining >= 0 &&
                   magic.nTranPoint >= requiredTraining)
            {
                magic.nTranPoint = unchecked(
                    magic.nTranPoint - requiredTraining);
                crossedThreshold = true;
                if (magic.btLevel >= magic.MagicInfo.btTrainLv)
                    break;

                magic.btLevel = unchecked((byte)(magic.btLevel + 1));
                leveled = true;
                requiredTraining =
                    GetNativeMagicProducerRequiredTraining(magic);
            }

            if (crossedThreshold)
            {
                RecalcAbilitys();
                SendMsg(this, Grobal2.RM_ABILITY, 0, 0, 0, 0,
                    string.Empty);
            }
            QueueNativeMagicProducerTrainingSnapshot(magic,
                leveled ? 800 : 3000);
            return true;
        }

        internal bool TryHandleNativeMagicProducerMessage(
            TProcessMessage processMessage)
        {
            if (processMessage?.wIdent != NativeMagicProducerPushIdent)
                return false;

            var payload = processMessage.Payload as
                NativeMagicProducerPushPayload;
            if (!m_boDeath && payload?.Target != null &&
                !payload.Target.m_boGhost)
                payload.Target.CharPushed(payload.Direction, 1);
            return true;
        }

        internal static bool IsNativeMagicProducerHumanKind(
            TBaseObject target)
        {
            return target is TPlayObject || target is HeroObject;
        }

        private bool TryAdmitNativeMagicProducerTarget(TBaseObject target,
            bool requireLineOfSight)
        {
            if (target == null || !IsProperTarget(target))
                return false;
            if (target.m_btRaceServer == Grobal2.RC_GUARD)
                return false;

            int chance = GetNativeMagicHitChance(m_wNativeType74MagicHit,
                target.m_nAntiMagic);
            if (NextNativeMagicProducerRandom(100) >= chance)
                return false;
            return !requireLineOfSight ||
                MagCanHitTarget(m_nCurrX, m_nCurrY, target);
        }

        private int CalculateNativeMagicProducerRawDamage(TUserMagic magic)
        {
            int skillPower = CalculateNativeMagicProducerSkillPower(magic);
            int lowMagic = HUtil32.LoWord(m_WAbil.MC);
            int highMagic = HUtil32.HiWord(m_WAbil.MC);
            return NativeLuckOnlyRoll(unchecked(lowMagic + skillPower),
                highMagic - lowMagic + 1);
        }

        private void QueueNativeMagicProducerEffect(TUserMagic magic,
            TBaseObject target, int rawDamage)
        {
            QueueNativeMagicEffect(1, target, rawDamage,
                magic.MagicInfo.wMagicID, target.m_nCurrX,
                target.m_nCurrY, 2, true, 0,
                MagicDamageContext.Capture(magic), 600);
        }

        private static void DamageNativeMagicProducerSpell(
            TBaseObject target, int amount)
        {
            int remaining = unchecked(target.m_WAbil.MP - amount);
            if (amount > 0)
            {
                target.m_WAbil.MP = remaining > 0 ? remaining : 0;
            }
            else
            {
                target.m_WAbil.MP = remaining < target.m_WAbil.MaxMP
                    ? remaining
                    : target.m_WAbil.MaxMP;
            }
        }

        private static int GetNativeMagicProducerRequiredTraining(
            TUserMagic magic)
        {
            return TryGetNativeMagicProducerTrainingValue(
                magic.MagicInfo.MaxTrain, magic.btLevel, out int value)
                    ? value
                    : -1;
        }

        private void QueueNativeMagicProducerTrainingSnapshot(
            TUserMagic magic, int delayMilliseconds)
        {
            HUtil32.EnterCriticalSection(M2Share.ProcessMsgCriticalSection);
            try
            {
                int index = 0;
                while (index < m_MsgList.Count)
                {
                    SendMessage message = m_MsgList[index];
                    if (message.wIdent != Grobal2.RM_MAGIC_LVEXP)
                    {
                        index++;
                        continue;
                    }
                    if (message.nParam1 == magic.MagicInfo.wMagicID)
                    {
                        if (!message.boLateDelivery)
                        {
                            index++;
                            continue;
                        }
                        m_MsgList.RemoveAt(index);
                        Dispose(message);
                        continue;
                    }
                    if (message.boLateDelivery)
                    {
                        message.dwDeliveryTime = 0;
                        message.boLateDelivery = false;
                        m_MsgList[index] = message;
                    }
                    index++;
                }
            }
            finally
            {
                HUtil32.LeaveCriticalSection(
                    M2Share.ProcessMsgCriticalSection);
            }

            SendDelayMsg(this, Grobal2.RM_MAGIC_LVEXP, 0,
                magic.MagicInfo.wMagicID, magic.btLevel,
                magic.nTranPoint, string.Empty, delayMilliseconds);
        }

        private static bool TryGetNativeMagicProducerTrainingValue<T>(
            T[] values, byte level, out int value) where T : struct
        {
            if (values == null || level >= values.Length)
            {
                value = -1;
                return false;
            }

            value = Convert.ToInt32(values[level]);
            return true;
        }

        private static int NextNativeMagicProducerRandom(int range)
        {
            if (range > 0)
                return M2Share.RandomNumber.Random(range);
            if (range == 0)
            {
                // Delphi Random(0) advances RandSeed and returns zero.
                _ = M2Share.RandomNumber.Random();
                return 0;
            }

            // Native sub_4C866E / 0x4C8683 pass wMax-wPower / defMax-defPower
            // with no test. A negative bound is the UInt32 bit pattern of
            // Random(n); the fused body then `add esi` / `imul (level+1)` /
            // `fild` (signed) / `fdiv 4.0` / ROUND — no subsequent clamp.
            return M2Share.RandomNumber.Random(range);
        }
    }
}
