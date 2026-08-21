using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Native fourth-job ordinary attack: client CM_HIT -> action 0x400 ->
    /// <c>sub_77136C</c>. All addresses below are from the 2.08 image.
    /// </summary>
    public partial class TBaseObject
    {
        internal const int NativeAction1024Code = 0x400;

        private static readonly int[] NativeAction1024RepeatChance =
            { 0, 30, 38, 46, 54, 62, 70, 78, 86, 100 };
        private static readonly int[] NativeAction1024FreshRootChance =
            { 0, 10, 15, 20, 25, 30, 35, 40, 45, 50 };
        private static readonly int[] NativeAction1024FreshRootDuration =
            { 0, 2000, 2000, 2000, 3000, 3000, 3000, 3000, 3000, 4000 };
        private static readonly int[] NativeAction1024RootedChance =
            { 0, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        private static readonly int[] NativeAction1024RootedDuration =
            { 0, 1000, 1000, 1000, 2000, 2000, 2000, 2000, 2000, 3000 };
        private static readonly int[] NativeAction1024Skill260ManaCost =
            { 25, 25, 30, 35, 40 };

        /// <summary>
        /// Dispatcher arm <c>0x770BF6..0x770C05</c>. Its frame-magic and
        /// physical-tail power locals both start at zero. A non-job-3 caller
        /// returns worker result zero and enters the dispatcher's ordinary
        /// fallback, matching the shared <c>sub_7707A8</c> tail.
        /// </summary>
        internal int RunNativeAction1024()
        {
            TBaseObject initialTarget = GetPoseCreate();
            TUserMagic frameMagic = null;
            int tailPower = 0;
            int result = RunNativeAction1024Swing(initialTarget);
            int frameAction = NativeAction1024Code;

            if (result == 0)
            {
                frameMagic = GetSunSwordFallbackMagic();
                tailPower = GetAttackPower(HUtil32.LoWord(m_WAbil.DC),
                    HUtil32.HiWord(m_WAbil.DC) -
                    HUtil32.LoWord(m_WAbil.DC));
                result = RunNativeBasicAttackFallback(initialTarget,
                    tailPower);
                frameAction = 1000;
            }

            if (result == 2)
                RunNativePhysicalAttackCommonTail(initialTarget, tailPower);

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

        /// <summary>
        /// <c>sub_77136C @0x77136C..0x771461</c>. Result 0 means wrong job,
        /// 1 means job accepted but no valid landing, and 2 means the direct
        /// carrier path ran. The two carriers deliberately share one CC roll.
        /// </summary>
        internal int RunNativeAction1024Swing(TBaseObject target)
        {
            int result = 0;
            if (m_btJob != 3)
                return result;

            result = 1;
            if (target == null || !IsProperTarget(target))
                return result;

            // sub_772578 returns sourceHit <= Random(targetSpeed).
            if (M2Share.RandomNumber.Random(target.m_wSpeedPoint) >=
                m_btHitPoint)
            {
                return result;
            }

            result = 2;
            int attackPower = GetAttackPower(
                m_NativeCoreWorkingAbility.CCLow,
                m_NativeCoreWorkingAbility.CCHigh -
                m_NativeCoreWorkingAbility.CCLow);

            ApplyNativeDirectMagicEffect(target, NativeAction1024Code,
                true, MagicDamageContext.Empty, 0, attackPower);
            if (RollNativeAction1024RepeatHit())
            {
                ApplyNativeDirectMagicEffect(target, NativeAction1024Code,
                    true, MagicDamageContext.Empty, 0, attackPower);
            }

            TryApplyNativeAction1024Root(target);
            TryApplyNativeAction1024Skill260State();
            TryApplyNativeAction1024Poison(target);
            TrainNativePhysicalMagic(GetMagicInfo(263), 3);
            ConsumeNativeSkill154StrikeAfterPositiveAttackPower(attackPower);
            return result;
        }

        /// <summary><c>sub_7712B0</c>, table <c>0x7D4C14</c>.</summary>
        private bool RollNativeAction1024RepeatHit()
        {
            int level = GetNativeTimedAbilityValue(0x47);
            return level > 0 && level < NativeAction1024RepeatChance.Length &&
                   M2Share.RandomNumber.Random(100) <
                   NativeAction1024RepeatChance[level];
        }

        /// <summary><c>sub_7712E8</c>, four tables at
        /// <c>0x7D4C3C/64/8C/B4</c>.</summary>
        private void TryApplyNativeAction1024Root(TBaseObject target)
        {
            int level = GetNativeTimedAbilityValue(0x47);
            if (level <= 0 || level >= NativeAction1024FreshRootChance.Length)
                return;

            bool rooted = target.HasNativeActiveState(0x2D);
            int chance = rooted
                ? NativeAction1024RootedChance[level]
                : NativeAction1024FreshRootChance[level];
            if (M2Share.RandomNumber.Random(100) >= chance)
                return;

            int duration = rooted
                ? NativeAction1024RootedDuration[level]
                : NativeAction1024FreshRootDuration[level];
            target.AddTimedAbilityInternal(0x2D, 1, duration, 0);
        }

        /// <summary><c>sub_77110C</c>: skill 260's post-hit mana/state arm.</summary>
        private void TryApplyNativeAction1024Skill260State()
        {
            TUserMagic magic = GetMagicInfo(260);
            if (magic == null)
                return;

            int effectiveLevel = NativeEffectiveMagicLevel(magic);
            int tableIndex = Math.Min(effectiveLevel, 4);
            int spellPoint = NativeAction1024Skill260ManaCost[tableIndex];
            if (spellPoint > m_WAbil.MP)
                return;

            int chance;
            if (HasNativeActiveState(0x46))
            {
                chance = 100;
                if (GetMagicInfo(264) == null)
                    RemoveTimedAbilityInternal(0x46);
            }
            else
            {
                chance = unchecked((effectiveLevel + 1) * 10);
            }

            if (M2Share.RandomNumber.Random(100) >= chance)
                return;

            TrainNativePhysicalMagic(magic,
                M2Share.RandomNumber.Random(3) + 1);
            DamageSpell(unchecked((ushort)spellPoint));
            HealthSpellChanged();
            // 0x7711F2 reloads effective level after VMT+0x3C training;
            // the awarded points can cross a level boundary.
            effectiveLevel = NativeEffectiveMagicLevel(magic);
            // 0x7711EB pushes value 1, then flag 0. AddState reads the
            // latter at [ebp+8] and the former at [ebp+0xC].
            AddTimedAbilityInternal(0x41, 1,
                unchecked((effectiveLevel + 1) * 2000), 0);
        }

        /// <summary><c>sub_77121C</c>: state 0x42 procs state 0x43.</summary>
        private void TryApplyNativeAction1024Poison(TBaseObject target)
        {
            if (!TryGetNativeTimedAbilityValue(0x42, out int rawLevel))
                return;

            int level = unchecked((ushort)rawLevel);
            int tier = unchecked(level + 1);
            if (M2Share.RandomNumber.Random(100) < unchecked(tier * 5))
            {
                target.AddTimedAbilityInternal(0x43, 1,
                    unchecked(tier * 1000), 0);
            }
        }
    }
}
