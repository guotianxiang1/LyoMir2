using SystemModule;

namespace GameSvr
{
    internal sealed class NativePhysicalAttackFramePayload
    {
        internal NativePhysicalAttackFramePayload(byte[] body,
            bool includeSource)
        {
            Body = body ?? Array.Empty<byte>();
            IncludeSource = includeSource;
        }

        internal byte[] Body { get; }
        internal bool IncludeSource { get; }
    }

    public partial class TBaseObject
    {
        internal const int NativeAction1011Code = 1011;
        internal const int NativeAction1012Code = 1012;
        private const int NativeBasicAttackAction = 1000;

        // Native self+0x184/+0x188. RecalcAbilitys rebuilds the first from the
        // three image-proven equipment writers; the second persists between
        // recalculations until the common physical tail exceeds two.
        internal ushort m_wNativePhysicalTailRate;
        internal int m_nNativePhysicalTailAccumulator;

        internal int RunNativeCrossMoonAction(int action, byte direction,
            TBaseObject target = null)
        {
            m_btDirection = direction;
            // sub_7707A8 preserves an explicit target and only probes the
            // facing cell when its target argument is nil.
            TBaseObject initialTarget = target ?? GetPoseCreate();
            TUserMagic frameMagic = m_MagicArr[SpellsDef.SKILL_CROSSMOON];

            int tailPower = GetAttackPower(HUtil32.LoWord(m_WAbil.DC),
                HUtil32.HiWord(m_WAbil.DC) - HUtil32.LoWord(m_WAbil.DC));
            int result = RunNativeCrossMoonWorker(action, tailPower);

            int frameAction = action;
            if (result == 0)
            {
                // 0x770CE1 overwrites the frame-magic local with self+0x9C
                // before both the fallback and the SM1230 body are built.
                frameMagic = GetSunSwordFallbackMagic();
                tailPower = GetAttackPower(HUtil32.LoWord(m_WAbil.DC),
                    HUtil32.HiWord(m_WAbil.DC) -
                    HUtil32.LoWord(m_WAbil.DC));
                result = RunNativeBasicAttackFallback(initialTarget,
                    tailPower);
                frameAction = NativeBasicAttackAction;
            }

            if (result == 2)
            {
                RunNativePhysicalAttackCommonTail(initialTarget, tailPower);
            }

            // 0x770DC2..0x770DD3 is outside the result==2 block.
            if (m_btRaceServer == Grobal2.RC_PLAYOBJECT &&
                initialTarget != null)
            {
                CheckWeaponUpgrade();
            }

            int effectiveLevel = frameMagic == null
                ? 0
                : NativeEffectiveMagicLevel(frameMagic);
            byte[] body = BuildSunSwordPhysicalAttackBody(frameAction,
                effectiveLevel, m_btDirection, m_nCurrX, m_nCurrY);
            SendRefMsg(Grobal2.RM_PHYSICAL_ATT, frameAction, m_nCurrX,
                m_nCurrY, 0, string.Empty,
                new NativePhysicalAttackFramePayload(body, false));
            return result;
        }

        private int RunNativeCrossMoonWorker(int action, int basePower)
        {
            // Native intentionally reloads self+0xBC at every consumer below;
            // hooks can replace the cached record during target processing.
            TUserMagic magic = m_MagicArr[SpellsDef.SKILL_CROSSMOON];
            if (magic == null)
                return 0;

            ushort spellPoint = TPlayObject.GetNativeMagicProducerMpCost(
                magic);
            if (m_WAbil.MP < spellPoint)
                return 0;

            // Native publishes the ability change even when spellPoint is 0.
            DamageSpell(spellPoint);
            HealthSpellChanged();

            TUserMagic levelMagic =
                m_MagicArr[SpellsDef.SKILL_CROSSMOON];
            int effectiveLevel = levelMagic == null ? 0 :
                TPlayObject.GetNativeMagicProducerEffectiveLevel(levelMagic);
            int rawDamage = unchecked(basePower + HUtil32.Round(
                (double)(effectiveLevel + 1) * basePower / 4.0d));
            int range = action == NativeAction1011Code ? 2 : 4;
            int result = 1;

            for (int distance = 1; distance <= range; distance++)
            {
                GetNativeDirectionOffset(direction: m_btDirection,
                    out int offsetX, out int offsetY);
                short x = unchecked((short)(m_nCurrX + offsetX * distance));
                short y = unchecked((short)(m_nCurrY + offsetY * distance));
                var target = m_PEnvir?.GetMovingObject(x, y, true)
                    as TBaseObject;
                if (target == null || !IsProperTarget(target))
                    continue;

                int targetDamage = rawDamage;
                if (action == NativeAction1012Code &&
                    m_Abil.Level > target.m_Abil.Level)
                {
                    targetDamage = unchecked(targetDamage + 50);
                }

                ApplyNativeDirectMagicEffect(target,
                    NativeAction1011Code, false,
                    MagicDamageContext.Capture(
                        m_MagicArr[SpellsDef.SKILL_CROSSMOON]),
                    0, targetDamage);
                // The outer proper-target success fixes the worker result at 2,
                // regardless of the direct carrier's returned damage.
                result = 2;
            }

            TrainNativePhysicalMagic(
                m_MagicArr[SpellsDef.SKILL_CROSSMOON], 3);
            return result;
        }

        private int RunNativeBasicAttackFallback(TBaseObject target,
            int basePower)
        {
            int result = 1;
            if (target == null || !IsProperTarget(target))
                return result;

            if (M2Share.RandomNumber.Random(target.m_wSpeedPoint) >=
                m_btHitPoint)
            {
                return result;
            }

            result = 2;
            if (basePower <= 0)
                return result;

            int bonus = 0;
            TUserMagic levelMagic = GetSunSwordFallbackMagic();
            if (levelMagic != null &&
                NativeEffectiveMagicLevel(levelMagic) == 4 &&
                target.HasNativeActiveState(20))
            {
                bonus = basePower / 7;
                if (bonus > 0)
                    target.DamageHealth(bonus);
            }

            int mainApplied = ApplyNativeDirectMagicEffect(target,
                NativeBasicAttackAction, true,
                MagicDamageContext.Capture(GetSunSwordFallbackMagic()),
                0, basePower);
            if (mainApplied <= 0 && bonus > 0)
            {
                target.SendDelayMsg(Grobal2.RM_STRUCK,
                    Grobal2.RM_10101, unchecked((short)bonus), bonus, 0,
                    ObjectId, string.Empty, 200);
            }

            TUserMagic trainingMagic = GetSunSwordFallbackMagic();
            if (trainingMagic != null)
            {
                TrainNativePhysicalMagic(trainingMagic,
                    M2Share.RandomNumber.Random(3));
            }
            ConsumeNativeSkill151StrikeAfterMainDamage(mainApplied);
            return result;
        }

        private void TrainNativePhysicalMagic(TUserMagic magic,
            int trainingPoints)
        {
            if (magic?.MagicInfo == null)
                return;

            if (this is TPlayObject player)
            {
                player.TrainNativeMagicProducer(magic, trainingPoints);
                return;
            }

            if (this is HeroObject)
            {
                // Hero VMT+0x3C is the same sub_76AD30 used by TPlayer.
                TrainNativeHeroPhysicalMagic(magic, trainingPoints);
            }
        }

        private bool TrainNativeHeroPhysicalMagic(TUserMagic magic,
            int trainingPoints)
        {
            if (magic?.MagicInfo == null ||
                magic.MagicInfo.TrainLevel == null ||
                magic.btLevel >= magic.MagicInfo.TrainLevel.Length ||
                m_Abil.Level < magic.MagicInfo.TrainLevel[magic.btLevel])
            {
                return false;
            }

            int awardedPoints = m_boFastTrain
                ? unchecked(trainingPoints * 3)
                : trainingPoints;
            magic.nTranPoint = unchecked(magic.nTranPoint + awardedPoints);

            bool crossedThreshold = false;
            bool leveled = false;
            int requiredTraining = GetNativeHeroPhysicalRequiredTraining(
                magic);
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
                    GetNativeHeroPhysicalRequiredTraining(magic);
            }

            if (crossedThreshold)
            {
                RecalcAbilitys();
                SendMsg(this, Grobal2.RM_ABILITY, 0, 0, 0, 0,
                    string.Empty);
            }
            QueueNativeHeroPhysicalTrainingSnapshot(magic,
                leveled ? 800 : 3000);
            return true;
        }

        private static int GetNativeHeroPhysicalRequiredTraining(
            TUserMagic magic)
        {
            return magic?.MagicInfo?.MaxTrain != null &&
                magic.btLevel < magic.MagicInfo.MaxTrain.Length
                    ? magic.MagicInfo.MaxTrain[magic.btLevel]
                    : -1;
        }

        private void QueueNativeHeroPhysicalTrainingSnapshot(
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

        private void RunNativePhysicalAttackCommonTail(
            TBaseObject initialTarget, int basePower)
        {
            if (m_wNativePhysicalTailRate > 0)
            {
                int addition = (int)Math.Truncate(
                    basePower / 100.0d * m_wNativePhysicalTailRate);
                m_nNativePhysicalTailAccumulator = unchecked(
                    m_nNativePhysicalTailAccumulator + addition);
                if (m_nNativePhysicalTailAccumulator > 2)
                {
                    ApplyNativePhysicalLandingDamage(unchecked(
                        -m_nNativePhysicalTailAccumulator));
                    m_nNativePhysicalTailAccumulator = 0;
                }
            }

            if (IsNativeHumanKind())
            {
                DoDamageWeapon(M2Share.RandomNumber.Random(5) + 2 -
                    m_AddAbil.btWeaponStrong);
            }
            ApplyNativeMagicHitHealing();
            SetTargetCreat(initialTarget);
        }

        private static void GetNativeDirectionOffset(byte direction,
            out int x, out int y)
        {
            switch (direction & 7)
            {
                case Grobal2.DR_UP:
                    x = 0; y = -1; return;
                case Grobal2.DR_UPRIGHT:
                    x = 1; y = -1; return;
                case Grobal2.DR_RIGHT:
                    x = 1; y = 0; return;
                case Grobal2.DR_DOWNRIGHT:
                    x = 1; y = 1; return;
                case Grobal2.DR_DOWN:
                    x = 0; y = 1; return;
                case Grobal2.DR_DOWNLEFT:
                    x = -1; y = 1; return;
                case Grobal2.DR_LEFT:
                    x = -1; y = 0; return;
                default:
                    x = -1; y = -1; return;
            }
        }
    }
}
