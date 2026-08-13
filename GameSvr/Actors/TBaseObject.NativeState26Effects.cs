using SystemModule;

namespace GameSvr
{
    internal sealed class NativeMagicEffectMessagePayload
    {
        internal NativeMagicEffectMessagePayload(TBaseObject target,
            MagicDamageContext context, int rawDamage, ushort skillId,
            short x, short y, byte range, bool arg0, byte flags)
        {
            Target = target;
            Context = context ?? MagicDamageContext.Empty;
            RawDamage = rawDamage;
            SkillId = skillId;
            X = x;
            Y = y;
            Range = range;
            Arg0 = arg0;
            Flags = flags;
        }

        internal TBaseObject Target { get; }
        internal MagicDamageContext Context { get; }
        internal int RawDamage { get; }
        internal ushort SkillId { get; }
        internal short X { get; }
        internal short Y { get; }
        internal ushort Range { get; }
        internal bool Arg0 { get; }
        internal byte Flags { get; }
    }

    public partial class TBaseObject
    {
        internal int m_nNativeMagicHitHealAmount;
        internal int m_nNativeMagicHitHealChance;
        internal int m_nNativeOneShotMagicDamage;

        internal void QueueNativeMagicEffect(ushort dispatchCategory,
            TBaseObject target, int rawDamage, ushort skillId, short x,
            short y, byte range, bool arg0, byte flags,
            MagicDamageContext context, int delayMilliseconds)
        {
            var payload = new NativeMagicEffectMessagePayload(target, context,
                rawDamage, skillId, x, y, range, arg0, flags);
            SendDelayMsg(this, Grobal2.RM_NATIVE_MAGIC_EFFECT,
                dispatchCategory, 0, 0, 0, string.Empty,
                delayMilliseconds, payload);
        }

        internal int ApplyNativeDirectMagicEffect(TBaseObject target,
            ushort skillId, bool arg0, MagicDamageContext context, byte flags,
            int rawDamage)
        {
            if (!IsNativeMagicEffectTarget(target))
                return 0;

            int damage = target.ResolveFullMagicDamage(this, skillId, arg0,
                context ?? MagicDamageContext.Empty, 4, flags, rawDamage);
            if (damage > 0)
            {
                ConsumeNativeOneShotMagicDamage(skillId);
            }
            if (arg0)
            {
                TryApplyNativeState26Direct(target);
            }
            return damage;
        }

        internal static int GetNativeState26ContestRange(ushort strength,
            ushort resistance, int baseRange)
        {
            return strength > resistance
                ? baseRange
                : baseRange + resistance - strength;
        }

        internal static int ScaleNativeSingleMagicDamage(int rawDamage,
            byte targetRaceServer)
        {
            if (targetRaceServer < Grobal2.RC_ANIMAL)
                return rawDamage;

            return (int)Math.Truncate(rawDamage * 1.2d);
        }

        internal static int GetNativeMagicHitChance(ushort sourceType74,
            ushort targetAntiMagic)
        {
            int chance = 100 * (sourceType74 + 10) /
                (targetAntiMagic + 10);
            return Math.Clamp(chance, 30, 95);
        }

        protected bool NativeMagicHitApplies(TBaseObject target)
        {
            if (target == null || target.m_btRaceServer == Grobal2.RC_GUARD)
                return false;

            int chance = GetNativeMagicHitChance(m_wNativeType74MagicHit,
                target.m_nAntiMagic);
            return M2Share.RandomNumber.Random(100) < chance;
        }

        internal void TryApplyNativeState26Direct(TBaseObject target)
        {
            TryApplyNativeState26ByContest(target,
                m_boNativeState26DirectStrong,
                m_boNativeState26DirectWeak, 5, 15);
        }

        internal void TryApplyNativeState26Single(TBaseObject target)
        {
            TryApplyNativeState26ByContest(target,
                m_boNativeState26SingleStrong,
                m_boNativeState26SingleWeak, 7, 21);
        }

        internal void TryApplyNativeState26AfterPhysicalDamage(
            TBaseObject target, int damage)
        {
            if (target == null || damage <= 0)
                return;

            if (m_boNativeState26DirectStrong &&
                M2Share.RandomNumber.Random(target.m_wEffectResistance + 5) == 0)
            {
                target.TryApplyNativeState26(5);
                return;
            }

            if (m_boNativeState26DirectWeak &&
                M2Share.RandomNumber.Random(target.m_wEffectResistance + 15) == 0)
            {
                target.TryApplyNativeState26(3);
            }
        }

        private void TryApplyNativeState26ByContest(TBaseObject target,
            bool strong, bool weak, int strongRange, int weakRange)
        {
            if (target == null)
                return;

            if (strong && NativeState26ContestApplies(target, strongRange))
            {
                target.TryApplyNativeState26(unchecked((ushort)(
                    m_Abil.Level + 5)));
                return;
            }

            if (weak && NativeState26ContestApplies(target, weakRange))
            {
                target.TryApplyNativeState26(unchecked((ushort)(
                    m_Abil.Level + 3)));
            }
        }

        private bool NativeState26ContestApplies(TBaseObject target,
            int baseRange)
        {
            int range = GetNativeState26ContestRange(m_wEffectStrength,
                target.m_wEffectResistance, baseRange);
            return M2Share.RandomNumber.Random(range) < 1;
        }

        private void ProcessNativeMagicEffectMessage(TProcessMessage message)
        {
            if (message?.Payload is not NativeMagicEffectMessagePayload payload)
                return;

            switch (message.wParam)
            {
                case 1:
                case 5:
                    ApplyNativeSingleMagicEffect(payload,
                        unchecked((byte)message.wParam));
                    break;
                case 2:
                    ApplyNativeLineMagicEffect(payload);
                    break;
                case 3:
                    ApplyNativeAreaMagicEffect(payload);
                    break;
            }
        }

        private void ApplyNativeSingleMagicEffect(
            NativeMagicEffectMessagePayload payload, byte category)
        {
            TBaseObject target = payload.Target;
            if (!IsNativeMagicEffectTarget(target) ||
                GetNativeChebyshevDistance(target.m_nCurrX,
                    target.m_nCurrY, payload.X, payload.Y) > payload.Range)
            {
                return;
            }

            int rawDamage = ScaleNativeSingleMagicDamage(payload.RawDamage,
                target.m_btRaceServer);
            int damage = target.ResolveFullMagicDamage(this, payload.SkillId,
                payload.Arg0, payload.Context, category, payload.Flags,
                rawDamage);
            if (damage > 0)
            {
                ApplyNativeMagicHitHealing();
                SendNativeMagicDamageFeedback(target, damage);
                ConsumeNativeOneShotMagicDamage(payload.SkillId);
            }
            if (payload.Arg0)
            {
                TryApplyNativeState26Single(target);
            }
        }

        private void ApplyNativeLineMagicEffect(
            NativeMagicEffectMessagePayload payload)
        {
            if (m_PEnvir == null || payload.Range == 0)
                return;

            byte direction = M2Share.GetNextDirection(m_nCurrX, m_nCurrY,
                payload.X, payload.Y);
            short x = 0;
            short y = 0;
            m_PEnvir.GetNextPosition(m_nCurrX, m_nCurrY, direction, 1,
                ref x, ref y);
            short endX = 0;
            short endY = 0;
            m_PEnvir.GetNextPosition(m_nCurrX, m_nCurrY, direction,
                payload.Range, ref endX, ref endY);

            int positiveCount = 0;
            for (int remaining = payload.Range; remaining > 0; remaining--)
            {
                var target = m_PEnvir.GetMovingObject(x, y, true)
                    as TBaseObject;
                if (IsNativeMagicEffectTarget(target) &&
                    NativeMagicHitApplies(target))
                {
                    int damage = target.ResolveFullMagicDamage(this,
                        payload.SkillId, false, payload.Context, 2,
                        payload.Flags, payload.RawDamage);
                    if (damage > 0)
                    {
                        positiveCount++;
                        SendNativeMagicDamageFeedback(target, damage);
                    }
                }

                direction = M2Share.GetNextDirection(x, y, endX, endY);
                if (!m_PEnvir.GetNextPosition(x, y, direction, 1,
                        ref x, ref y))
                    break;
            }

            if (positiveCount > 0)
            {
                ApplyNativeMagicHitHealing();
                ConsumeNativeOneShotMagicDamage(payload.SkillId);
            }
        }

        private void ApplyNativeAreaMagicEffect(
            NativeMagicEffectMessagePayload payload)
        {
            if (m_PEnvir == null)
                return;

            var targets = new List<TBaseObject>();
            GetMapBaseObjects(m_PEnvir, payload.X, payload.Y,
                payload.Range, targets);
            int positiveCount = 0;
            for (int index = 0; index < targets.Count; index++)
            {
                TBaseObject target = targets[index];
                if (target == null || !target.bo2B9 ||
                    !IsNativeMagicEffectTarget(target))
                    continue;

                bool isCenter = target.m_nCurrX == payload.X &&
                    target.m_nCurrY == payload.Y;
                int damage = target.ResolveFullMagicDamage(this,
                    payload.SkillId, isCenter, payload.Context, 3,
                    payload.Flags, payload.RawDamage);
                if (damage > 0)
                {
                    positiveCount++;
                    SendNativeMagicDamageFeedback(target, damage);
                }
                if (isCenter)
                {
                    TryApplyNativeState26Single(target);
                }
            }

            if (positiveCount > 0)
            {
                ApplyNativeMagicHitHealing();
                ConsumeNativeOneShotMagicDamage(payload.SkillId);
            }
        }

        private void ApplyNativeMagicHitHealing()
        {
            if (m_btRaceServer != Grobal2.RC_PLAYOBJECT &&
                m_btRaceServer != Grobal2.RC_HEROOBJECT ||
                m_nNativeMagicHitHealAmount <= 0)
            {
                return;
            }

            int chance = Math.Max(80, m_nNativeMagicHitHealChance);
            if (chance <= M2Share.RandomNumber.Random(100))
                return;

            int firstRange = (int)Math.Truncate(
                m_nNativeMagicHitHealAmount * 0.45d) + 1;
            int secondRange = (int)Math.Truncate(
                m_nNativeMagicHitHealAmount * 0.45d) + 1;
            int thirdRange = M2Share.RandomNumber.Random(2) == 1
                ? m_nNativeMagicHitHealAmount - firstRange - secondRange + 1
                : 0;
            int health = NextNativeMagicHealingRandom(firstRange) +
                NextNativeMagicHealingRandom(secondRange) +
                NextNativeMagicHealingRandom(thirdRange);
            if (HasNativeActiveState(102))
                health /= 2;
            IncHealthSpell(health, 0);
        }

        private static int NextNativeMagicHealingRandom(int range)
        {
            if (range > 0)
                return M2Share.RandomNumber.Random(range);

            // Delphi Random(0) still advances RandSeed and returns zero.
            _ = M2Share.RandomNumber.Random();
            return 0;
        }

        private void ConsumeNativeOneShotMagicDamage(ushort skillId)
        {
            if (IsNativeSkill152DamageSkill(skillId))
                m_nNativeOneShotMagicDamage = 0;
        }

        private bool IsNativeMagicEffectTarget(TBaseObject target)
        {
            return target != null && !ReferenceEquals(target, this) &&
                ReferenceEquals(m_PEnvir, target.m_PEnvir) &&
                !target.m_boDeath && !target.m_boGhost &&
                !target.m_boAdminMode && !target.m_boStoneMode &&
                !target.HasNativeActiveState(52) &&
                target.m_btRaceServer != 240 &&
                target.m_btRaceServer != 241 &&
                IsProperTarget(target);
        }

        private static int GetNativeChebyshevDistance(int x1, int y1,
            int x2, int y2)
        {
            return Math.Max(Math.Abs(x1 - x2), Math.Abs(y1 - y2));
        }

        private void SendNativeMagicDamageFeedback(TBaseObject target,
            int damage)
        {
            if (damage > 0)
            {
                target.SendRefMsg(Grobal2.RM_STRUCK_MAG, damage, 0, 0,
                    ObjectId, string.Empty);
            }
        }
    }
}
