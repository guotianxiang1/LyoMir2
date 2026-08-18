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

        /// <summary>
        /// Native <c>sub_76E268</c> — the DIRECT carrier, one of the four
        /// wrappers that call a target's <c>VMT+0x104</c>
        /// (<c>TCreature 0x76470C</c> = <c>sub_76CFC4</c> =
        /// <c>TBaseObject.ReceiveAttackDamge</c>, which its own exception
        /// string @<c>0x76DDE4</c> names). Its three siblings are
        /// <c>sub_76DE1C</c> (single, category 1/5), <c>sub_76DF5C</c> (line,
        /// 2) and <c>sub_76E0B4</c> (area, 3); this one is told apart by the
        /// state-26 contest ranges <c>0x76E2D2 mov edx,5</c> /
        /// <c>0x76E313 mov edx,0xF</c>, against 7 / 0x15 in all three others
        /// (<c>0x76DED6</c>/<c>0x76DF19</c>, <c>0x76E1C5</c>/<c>0x76E206</c>).
        /// <para>
        /// Argument roles re-read from the push site
        /// <c>0x76E291-0x76E2A9</c>: <c>push edi</c> (rawDamage),
        /// <c>push [ebp+0xC]</c> (flags), <c>push 4</c> (category),
        /// <c>push [ebp-4]</c> (the TUserMagic), <c>push [ebp+8]</c> (arg0),
        /// then <c>ecx = [ebp+0x14]</c> (the wIdent), <c>edx</c> = attacker,
        /// <c>eax</c> = target.
        /// </para>
        /// </summary>
        internal int ApplyNativeDirectMagicEffect(TBaseObject target,
            ushort skillId, bool arg0, MagicDamageContext context, byte flags,
            int rawDamage)
        {
            // 0x76E284 `call sub_767498` / 0x76E28B `je 0x76E36E` — a
            // rejected target returns the [ebp-8] that was zeroed at
            // 0x76E27A, i.e. 0.
            if (!IsNativeMagicEffectTarget(target))
                return 0;

            int damage = target.ResolveFullMagicDamage(this, skillId, arg0,
                context ?? MagicDamageContext.Empty, 4, flags, rawDamage);

            // 0x76E2B2 `cmp byte [ebp+8],0 / je 0x76E357`: the state block
            // hangs off arg0 and runs BEFORE the delayed struck message.
            if (arg0)
            {
                TryApplyNativeState26Direct(target);
                // 0x76E33C `80 BB DB 01 00 00 00` would then add internal
                // state 0x1D (29) for cx = 2 through the target's VMT+0xC8.
                // NOT ported: that compare is the ONLY disp32 memory
                // reference to +0x1DB anywhere in the image, so nothing
                // ever raises the byte and the arm cannot be reached in
                // this build.
            }

            // 0x76E357 `cmp [ebp-8],0 / jle 0x76E36E`, then 0x76E35D
            // `push 0xC8` and sub_76B4F8(eax = target, edx = attacker,
            // ecx = damage, delay = 200). sub_76B4F8 @0x76B506 loads
            // edx = 0x2724 (RM_STRUCK, the sentinel BaseObject) and
            // cx = 0x2775 (RM_10101) before sub_766060, and its six pushes
            // are wParam = damage, nParam1 = damage, nParam2 = 0,
            // nParam3 = the attacker, sMsg = nil, delay.
            if (damage > 0)
            {
                // 眼神「攻击触发」的 trampoline 顶掉的正是 0x76E35D 的 `push 0xC8`，
                // 桩体在尾部把它重放后才 jmp 0x76E362，所以派发落在这里、
                // SendDelayMsg 之前。
                Plugins.YanshenTriggerDispatch.FireMyAttack(this, target, damage);
                target.SendDelayMsg(Grobal2.RM_STRUCK, Grobal2.RM_10101,
                    unchecked((short)damage), damage, 0, ObjectId,
                    string.Empty, 200);
            }

            // sub_76E268 does NOT call sub_772468, the one-shot magic damage
            // reset: that call only exists in the other three carriers
            // (0x76DEB1 in the single one and the matching spots in the
            // line/area bodies). The ConsumeNativeOneShotMagicDamage that
            // used to stand here had no counterpart in this body.
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
            // 0x76DE84 `mov esi,eax` 是「魔法攻击触发」的挂载点：桩体把这条重放在
            // 开头、把 `test esi,esi / jle` 重放在结尾，所以派发夹在算完伤害与
            // 判正之间 —— 伤害非正时也发。
            Plugins.YanshenTriggerDispatch.FireMyMagicAttack(this, target, damage,
                payload.SkillId);
            if (damage > 0)
            {
                ApplyNativeMagicHitHealing();
                SendNativeMagicDamageFeedback(target, damage);
                ConsumeNativeOneShotMagicDamage(payload.SkillId);
            }
            if (payload.Arg0)
            {
                // 0x76DEC0 `cmp byte [ebx+0x1B6],0` 是「盘古魔法攻击触发」的第二个
                // 挂载点，桩体在尾部重放它，故派发在 state-26 之前。
                Plugins.YanshenTriggerDispatch.FirePanguMagicAttack(this, target,
                    payload.SkillId);
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
                    // 0x76E1AF `cmp byte [esi+0x1B6],0` —— 与 0x76DEC0 同一开关的
                    // 第一个挂载点，同样在 state-26 之前重放着那条 cmp。
                    Plugins.YanshenTriggerDispatch.FirePanguMagicAttack(this, target,
                        payload.SkillId);
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
