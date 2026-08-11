using SystemModule;

namespace GameSvr
{
    public partial class TBaseObject
    {
        internal const int NativeMagicState16 = 16;
        internal const int NativeMagicState30 = 30;
        internal const int NativeMagicState53 = 53;
        internal const int NativeMagicState56 = 56;
        internal const int NativeMagicState83 = 83;
        internal const int NativeMagicState16SkillId = 115;

        private static readonly int[] s_nativeState16PrimaryBonus =
        {
            500, 600, 600, 700, 700, 700, 700
        };

        private static readonly int[] s_nativeState16SecondaryBonus =
        {
            300, 400, 400, 500, 500, 500, 500
        };

        private static readonly int[] s_nativeState16LowContest =
        {
            15, 25, 35, 45, 55, 65,
            75, 75, 85, 85, 95, 95
        };

        private static readonly int[] s_nativeState16MidContest =
        {
            10, 18, 25, 33, 40, 48,
            55, 55, 60, 60, 65, 65
        };

        private static readonly int[] s_nativeState16HighContest =
        {
            3, 5, 7, 9, 11, 13,
            15, 15, 17, 17, 19, 19
        };

        internal int m_nNativeMagicTraceDamage;
        internal string m_sNativeMagicTracePrefix = string.Empty;

        internal int ApplyNativeState56MagicBonus(int damage, byte category)
        {
            if (!TryGetNativeTimedAbilityValue(NativeMagicState56,
                    out int value))
            {
                return damage;
            }

            int bonus = category == 3 ? value / 2 : value;
            return unchecked(damage + bonus);
        }

        internal int GetNativeState16EffectiveLevel()
        {
            TUserMagic magic = GetMagicInfo(NativeMagicState16SkillId);
            if (magic?.MagicInfo == null)
                return 1;

            return Math.Min(
                unchecked((byte)(magic.btLevel + magic.NativeLevelBonus)),
                magic.MagicInfo.btTrainLv);
        }

        internal static int GetNativeState16InitialMagicBonus(int skillId,
            byte category, int effectiveLevel)
        {
            ushort nativeSkillId = unchecked((ushort)skillId);
            if (nativeSkillId is 6 or 22 or 127)
                return 0;

            bool primary = category is 1 or 4 or 5;
            bool secondary = category is 2 or 3;
            if (!primary && !secondary)
                return 0;

            if (effectiveLevel is >= 1 and <= 7)
            {
                return primary
                    ? s_nativeState16PrimaryBonus[effectiveLevel - 1]
                    : s_nativeState16SecondaryBonus[effectiveLevel - 1];
            }

            return primary ? 300 : 100;
        }

        internal int ApplyNativeState16InitialMagicBonus(int damage,
            int skillId, byte category)
        {
            if (!HasNativeActiveState(NativeMagicState16))
                return damage;

            int bonus = GetNativeState16InitialMagicBonus(skillId, category,
                GetNativeState16EffectiveLevel());
            return unchecked(damage + bonus);
        }

        internal static int GetNativeState16ContestDenominator(
            int effectiveLevel, int levelGapBin)
        {
            if ((uint)levelGapBin >= 12)
                return 0;

            int[] table = effectiveLevel switch
            {
                1 or 2 => s_nativeState16LowContest,
                3 or 4 => s_nativeState16MidContest,
                5 or 6 => s_nativeState16HighContest,
                _ => null
            };
            return table?[levelGapBin] ?? 0;
        }

        internal int ApplyNativeState16LevelContest(TBaseObject source,
            int skillId, int damage)
        {
            return ApplyNativeState16LevelContest(source, skillId, damage,
                M2Share.RandomNumber.Random);
        }

        internal int ApplyNativeState16LevelContest(TBaseObject source,
            int skillId, int damage,
            Func<int, int> random)
        {
            ushort nativeSkillId = unchecked((ushort)skillId);
            if (source?.m_Abil == null || m_Abil == null ||
                nativeSkillId is 22 or 127 ||
                !source.HasNativeActiveState(NativeMagicState16))
            {
                return damage;
            }

            int levelGap = Math.Abs((int)source.m_Abil.Level -
                m_Abil.Level);
            if (levelGap >= 600)
                return damage;

            int denominator = GetNativeState16ContestDenominator(
                source.GetNativeState16EffectiveLevel(), levelGap / 50);
            if (denominator == 0 || random(denominator) != 0)
                return damage;

            int bonus = source.GetNativeState16EffectiveLevel() == 1
                ? 8000
                : 10000;
            damage = unchecked(damage + bonus);
            RecordNativeState16CriticalTrace(damage);

            if (HasNativeActiveState(NativeMagicState16))
            {
                int cancellation =
                    source.GetNativeState16EffectiveLevel() == 1
                        ? 8000
                        : 10000;
                damage = unchecked(damage - cancellation);
            }

            return damage;
        }

        internal int ApplyNativeState83MagicReduction(int damage)
        {
            if (damage <= 0 ||
                !TryGetNativeTimedAbilityValue(NativeMagicState83,
                    out int value) || value <= 0)
            {
                return damage;
            }

            return value >= damage ? 0 : unchecked(damage - value);
        }

        internal int ApplyNativeTargetMidMagicStates(int damage)
        {
            if (HasNativeActiveState(NativeMagicState53))
                return RoundNativeX87(damage * 1.3d);

            if (!HasNativeActiveState(NativeMagicState30))
                return damage;

            int value = GetNativeRedPoisonLevel();
            double multiplier = value == 4 ? 1.25d : 1.2d;
            return RoundNativeX87(damage * multiplier);
        }

        /// <summary>
        /// The physical-side twin of <see cref="ApplyNativeTargetMidMagicStates"/>
        /// — native <c>StruckDamage</c> = <c>sub_73F9FC</c> @0x73FA40-0x73FADE.
        /// <para>
        /// @0x73FA40 <c>mov dl,0x35; call sub_772960; test al,al; je 0x73FA77</c>
        /// — internal state <c>0x35</c> (53) scales by <c>tbyte 0x73FBC8</c> =
        /// <b>1.3</b>. Native is an <b>else-if</b>: only when 53 is absent does
        /// @0x73FA77 test <c>mov dl,0x1E</c> (30); its level comes from
        /// <c>sub_773BEC(0x1E)</c> and @0x73FA8D <c>cmp eax,4</c> picks
        /// <c>float 0x73FBD4</c> = <b>1.25</b>, else <c>tbyte 0x73FBD8</c> =
        /// <b>1.2</b>. Every scale goes through <c>sub_403574</c>
        /// (<c>fistp qword</c> = x87 round-half-to-even), matching
        /// <c>RoundNativeX87</c>.
        /// </para>
        /// <para>
        /// The two state ids and the three constants are IDENTICAL to the magic
        /// entry <c>sub_76CFC4</c>, so this shares that verified helper; the only
        /// difference is that <c>StruckDamage</c> scales the durability roll
        /// (<c>[ebp-8]</c>) with the same multiplier <b>before</b> the damage
        /// (<c>esi</c>), which the <c>ref</c> parameter reproduces in order.
        /// </para>
        /// <para>
        /// <b>STATUS-LAYER DEPENDENCY (state 30 / 0x1E only).</b> State 53 is in
        /// <c>m_nCharStatus2</c> and is fully reachable today, so the ×1.3 tier
        /// is LIVE. State 30 is NOT reachable today, but the precise reason is a
        /// SPLIT AUTHORITY — corrected 2026-08-03 after two contradictory audits:
        /// <list type="bullet">
        /// <item><c>HasNativeActiveState(30)</c> reads <c>GetBodyStateWord(0)</c>
        /// = the RAW field <c>m_nCharStatus</c>. Red poison never writes bit 30
        /// there (it writes <c>m_wStatusTimeArr[POISON_DAMAGEARMOR=1]</c>), so
        /// this predicate stays false.</item>
        /// <item><c>GetCharStatus()</c> is a DIFFERENT method: it composes the
        /// legacy array into the wire word via <c>0x80000000 &gt;&gt; i</c>, i.e.
        /// legacy slot 1 → bit 30. So the CLIENT-VISIBLE word DOES carry bit 30
        /// while the SERVER-SIDE predicate field does not — the two words
        /// disagree for indices 20..31.</item>
        /// </list>
        /// So the earlier "GetCharStatus rebuilds the span, therefore it cannot
        /// latch" wording was wrong in its mechanism (that method is not on this
        /// read path), and the counter-claim "red poison sets bit 30, so ×1.2
        /// fires today" was wrong in its conclusion (it sets the wire bit, not
        /// the predicate field). Net effect is unchanged: the ×1.25/×1.2 tier is
        /// written to the native contract but stays inert until the status layer
        /// gives indices 20..31 a single authority that both words derive from.
        /// Natively there is no such split: state 30 is 中毒, handler 0x741DDE
        /// (shared with 31), and its LEVEL is list-backed — record <c>+0x01</c>=id,
        /// <c>+0x0A</c>=level, <c>+0x0E</c>=next, head <c>[self+0xDC]</c> — with
        /// <c>level==4</c> selecting ×1.25 over ×1.2. A correct fix must therefore
        /// make the LEVEL persistent, not merely the bit.
        /// Deliberately NOT worked around here: inventing a second authority for
        /// state 30 in the damage layer would be a fresh divergence.
        /// </para>
        /// </summary>
        internal void ApplyNativeStruckAmplifyStates(ref int durabilityRoll,
            ref int damage)
        {
            double multiplier;
            if (HasNativeActiveState(NativeMagicState53))
            {
                multiplier = 1.3d;
            }
            else if (HasNativeActiveState(NativeMagicState30))
            {
                int value = GetNativeRedPoisonLevel();
                multiplier = value == 4 ? 1.25d : 1.2d;
            }
            else
            {
                return;
            }

            durabilityRoll = RoundNativeX87(durabilityRoll * multiplier);
            damage = RoundNativeX87(damage * multiplier);
        }

        internal int ApplyNativeSkill153ShieldToMagicDamage(int damage)
        {
            return ConsumeNativeSkill153ShieldCharge(damage);
        }

        internal void ResetNativeMagicTrace()
        {
            m_nNativeMagicTraceDamage = 0;
            m_sNativeMagicTracePrefix = string.Empty;
        }

        private void RecordNativeState16CriticalTrace(int damage)
        {
            m_nNativeMagicTraceDamage = unchecked(-damage);
            m_sNativeMagicTracePrefix = "致命一击 -";
        }

        internal void RecordNativeBreakthroughFlagTrace(int flags,
            int damage)
        {
            if ((flags & 0x04) == 0)
                return;

            m_nNativeMagicTraceDamage = damage;
            m_sNativeMagicTracePrefix = "击破 -";
        }

        internal void RecordNativePostTableFlagTrace(int flags, int damage)
        {
            if ((flags & 0x20) != 0)
            {
                m_nNativeMagicTraceDamage = damage;
                m_sNativeMagicTracePrefix = "狂击 -";
            }
            else if ((flags & 0x10) != 0)
            {
                m_nNativeMagicTraceDamage = damage;
                m_sNativeMagicTracePrefix = "暴袭 -";
            }
        }
    }
}
