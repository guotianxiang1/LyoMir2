using SystemModule;

namespace GameSvr
{
    public partial class TBaseObject
    {
        internal bool m_boNativeHealthSpellDirty;
        internal byte m_btNativeMagicDamageReductionPercent;
        internal bool m_boNativeFullMagicShield;
        internal bool m_boNativeHalfMagicShield;

        internal virtual int ResolveFullMagicDamage(TBaseObject source, int skillId,
            bool arg0, MagicDamageContext context, byte category, int flags,
            int rawDamage)
        {
            if (HasNativeActiveState(52) || HasNativeActiveState(55))
                return 0;

            ResetNativeMagicTrace();
            if (IsNativeUnclosedSpecialMagicSkill(skillId))
            {
                return ResolveNativeSpecialMagicDirectLanding(source,
                    skillId, arg0, context, category, flags, rawDamage);
            }

            bool firstClassifier = IsNativeMagicFirstClassifier(skillId);
            bool secondClassifier = IsNativeMagicSecondClassifier(skillId);
            bool thirdClassifier = IsNativeMagicThirdClassifier(skillId);
            int effectiveFlags = source == null
                ? flags
                : source.ApplyNativeMagicBreakthrough(flags);
            int damage = rawDamage;

            if (source != null)
            {
                if (category != 4 && skillId is not 6 and not 22 and not 127)
                {
                    damage = unchecked(damage +
                        source.m_nNativeFlatMagicDamageIncrease);
                }

                if (!secondClassifier &&
                    source.m_wNativeBaseMagicDamagePercent != 0)
                {
                    int bonus = unchecked(damage *
                        source.m_wNativeBaseMagicDamagePercent) / 100;
                    damage = unchecked(damage + bonus);
                }

                damage = source.ApplyNativeState56MagicBonus(damage,
                    category);
                damage = source.ApplyNativeState16InitialMagicBonus(damage,
                    skillId, category);
            }

            // The still-unmapped target VMT+0x100 hook remains identity.
            damage = ResolveNativeMagicDefence(skillId, category,
                effectiveFlags, damage);
            damage = ApplyNativeFixedMagicReductions(damage);
            damage = ApplyNativeState83MagicReduction(damage);
            if (source != null)
            {
                damage = source.ApplyNativeMagicAwakening(skillId, arg0,
                    damage);
            }

            if (damage > 0)
            {
                int targetMidEntryDamage = damage;
                damage = ApplyNativeTargetMidMagicStates(damage);

                // Closed portions of the human target VMT+0xF4 override.
                if (this is TPlayObject or HeroObject)
                {
                    if ((effectiveFlags & 0x04) == 0)
                    {
                        damage = ApplyNativeHumanHqReduction(damage);
                        damage = ApplyNativeHumanMagicPercentReduction(
                            damage);
                    }

                    ushort combinedBreakLevel = 0;
                    int breakExtra = 0;
                    int breakBonus = ApplyNativeHumanMagicBreakContest(
                        source, targetMidEntryDamage, damage, skillId,
                        ref combinedBreakLevel, ref breakExtra);
                    if (breakExtra > 0)
                        damage = unchecked(damage + breakExtra);
                    // 0x7461D3..0x7461F3 only bypasses an identity hook for
                    // flags 4/8. It does not gate the additions below.
                    if (breakBonus > 0)
                        damage = unchecked(damage + breakBonus);
                    if (source != null)
                    {
                        // 0x746221..0x746245: +0x3F0 applies after the
                        // returned break bonus for every flag combination.
                        damage = source.ApplyNativeSkill152OneShotBonus(
                            skillId, damage);
                        // 0x746247..0x74628A, after the returned bonus.
                        damage = source.ApplyNativeSkill151BurstDamage(
                            damage, skillId);
                        // 0x74628C..0x7462B6 follows 151 and immediately
                        // precedes the state-16 cap at 0x7462B8.
                        damage = source.ApplyNativeSkill154BurstDamage(
                            damage, skillId);
                    }
                    if ((effectiveFlags & 0x04) == 0)
                    {
                        damage = ApplyNativeState16MagicDamageCap(skillId,
                            effectiveFlags, damage);
                    }
                }
            }
            damage = ApplyNativeState16LevelContest(source, skillId,
                damage);

            if ((effectiveFlags & 0x04) != 0)
            {
                if (arg0 && source is TPlayObject or HeroObject)
                    damage = source.ApplyNativeSkill307Damage(damage);
                RecordNativeBreakthroughFlagTrace(effectiveFlags, damage);
            }

            if (damage < 0)
                damage = 0;

            // Source VMT+0xFC and sub_76FFD0 both contribute constant zero.
            damage = ApplyNativeSkill153ShieldToMagicDamage(damage);

            if (!firstClassifier && category == 4)
            {
                damage = ApplyNativeFastnessNearHitReduction(damage);
            }
            if (skillId == SpellsDef.SKILL_EARTHFIRE &&
                category == 1)
            {
                damage = ApplyNativeFastnessHqReduction(damage);
            }
            damage = ApplyNativeGeneralFastnessReduction(skillId, category,
                firstClassifier, secondClassifier, thirdClassifier, damage);
            RecordNativePostTableFlagTrace(effectiveFlags, damage);

            if (damage <= 0)
                return 0;

            if (source != null && source.m_btNativeDamageIncreasePercent != 0 &&
                !secondClassifier && !firstClassifier)
            {
                int bonus = unchecked(damage *
                    source.m_btNativeDamageIncreasePercent) / 100;
                damage = unchecked(damage + bonus);
            }

            _ = context;
            if (source != null)
            {
                if (arg0 && source is TPlayObject or HeroObject)
                {
                    _ = source.TryApplyNativeSkill306(this);
                    damage = source.ApplyNativeSkill308LowHealthDamage(this,
                        damage);
                }
                damage = unchecked(damage +
                    source.GetNativeSkill190DamageBonus(this,
                        arg0 ? (byte)1 : (byte)0, skillId));
            }

            damage = ApplyNativeMagicCritical(source, damage);
            // sub_100795C0 序：crit → 英雄千分比免伤(0x1007A8A7) → 切割(0x1007AEAD)。
            // 207 同流水线在 0x1006D3EF/0x1006DA87；宿主入口仍不可证(0x10F2D759@Themida)。
            damage = Plugins.YanshenPage1PostDamage.ApplyHeroPermilleReduction(
                this, damage);
            damage = Plugins.YanshenPage1PostDamage.ApplySpellCutting(
                source, this, damage, skillId);
            // sub_100795C0：切割臂之后、返伤害之前 — 0x1007AAC9 攻击吸血 /
            // 0x1007AAEB 火墙不吸血（眼神2第2页，cfg+0x1A4）。
            Plugins.YanshenPage2ExtBehaviors.ApplyMagicDamageVamp(
                source, this, damage, skillId);
            damage = ApplyStandardEarthFireSuperForce(source, damage);

            int result = ApplyStandardEarthFireLanding(damage);
            if (result > 0 && m_btRaceServer != Grobal2.RC_PLAYOBJECT &&
                category != 4)
            {
                OnStandardEarthFireDamageApplied(result, source);
            }
            return result;
        }

        private static bool IsNativeUnclosedSpecialMagicSkill(int skillId)
        {
            ushort id = unchecked((ushort)skillId);
            return id is >= 282 and <= 285 or >= 287 and <= 290;
        }

        private int ResolveNativeSpecialMagicDirectLanding(
            TBaseObject source, int skillId, bool arg0,
            MagicDamageContext context, byte category, int flags,
            int rawDamage)
        {
            // sub_76CF84 sends these IDs directly to target VMT+0x1B0.
            // ID 282's VMT+0x1EC pre-hook and the positive 287-290 post-hook
            // remain isolated until their dynamic implementations are closed.
            _ = source;
            _ = skillId;
            _ = arg0;
            _ = context;
            _ = category;
            _ = flags;
            return ApplyStandardEarthFireLanding(rawDamage);
        }

        private int ApplyNativeMagicCritical(TBaseObject source, int damage)
        {
            if (source == null || source.m_sNativeCriticalChance < 0 ||
                m_sNativeAntiCriticalChance < 0 ||
                m_sNativeCriticalDamageReduction < 0)
            {
                return damage;
            }

            int criticalChance = Math.Min((int)source.m_sNativeCriticalChance,
                10000);
            int antiCriticalChance = Math.Min(
                (int)m_sNativeAntiCriticalChance, 10000);
            int criticalReduction = Math.Min(
                (int)m_sNativeCriticalDamageReduction, 10000);
            int threshold = RoundNativeX87(
                (100.0d - antiCriticalChance / 100.0d) *
                (criticalChance / 100.0d));

            if (M2Share.RandomNumber.Random(10000) > threshold)
                return damage;

            double increase = source.m_nNativeCriticalDamageIncrease / 10000.0d;
            double multiplier = 1.5d + increase;
            multiplier -= criticalReduction * 0.00005d;
            multiplier -= increase * criticalReduction / 10000.0d;
            return RoundNativeX87(damage * multiplier);
        }

        internal int ApplyNativePhysicalCritical(TBaseObject source, int damage)
        {
            if (source == null || source.m_sNativeCriticalChance < 0 ||
                m_sNativeAntiCriticalChance < 0 ||
                m_sNativeCriticalDamageReduction < 0)
            {
                return damage;
            }

            int criticalChance = Math.Min((int)source.m_sNativeCriticalChance,
                10000);
            int antiCriticalChance = Math.Min(
                (int)m_sNativeAntiCriticalChance, 10000);
            int criticalReduction = Math.Min(
                (int)m_sNativeCriticalDamageReduction, 10000);
            int threshold = RoundNativeX87(
                (100.0d - antiCriticalChance / 100.0d) *
                (criticalChance / 100.0d));

            if (M2Share.RandomNumber.Random(10000) > threshold)
                return damage;

            double increase = source.m_nNativeCriticalDamageIncrease / 10000.0d;
            double multiplier = 1.5d + increase;
            multiplier -= criticalReduction * 0.00005d;
            multiplier -= increase * criticalReduction / 10000.0d;
            return RoundNativeX87(damage * multiplier);
        }

        private static int RoundNativeX87(double value) =>
            unchecked((int)(long)Math.Round(value, MidpointRounding.ToEven));

        private int ResolveStandardEarthFireDefence(int damage)
        {
            return ResolveNativeMagicDefence(SpellsDef.SKILL_EARTHFIRE, 1,
                0, damage);
        }

        /// <summary>
        /// Native <c>sub_7678F4</c> — the ONE armour roll shared by the
        /// physical (<c>sub_767958</c>, AC <c>+0x27C</c>/<c>+0x280</c>) and the
        /// magic (<c>sub_7679B8</c>, MAC <c>+0x284</c>/<c>+0x288</c>) entries.
        /// <para>
        /// @0x767900 <c>cmp byte [ebx+0x178],0; jne</c> — non-player race takes
        /// the plain roll. @0x767909 <c>cmp dword [ebx+0x164],0; jle</c> —
        /// LuckNum (RTTI <c>LuckNum</c> = <c>+0x164</c>) must be positive.
        /// @0x767912 <c>mov edx,[ebx+0x164]; mov eax,5; call sub_4C700C</c>
        /// (= min) then <c>mov eax,6; sub eax,edx; call sub_403B4C</c> (=
        /// <c>Random(6-min(5,luck))</c>); on 0 → <c>sub_4C7004</c> (= max) of
        /// the two ends, i.e. at luck &gt;= 5 <c>Random(1)</c> is always 0 so a
        /// lucky defender ALWAYS rolls maximum armour.
        /// @0x76793F plain path: <c>max(hi-lo,0)+1</c> then <c>Random</c> and
        /// <c>add eax,esi</c>.
        /// </para>
        /// <para>
        /// Yanshen 2.0.8 「修复卡防御」 is a code patch on this very byte, not a
        /// runtime test: gate <c>0x100AAE6C cmp [edi+0xAE0],0</c> (page slot of
        /// 「修复卡防御」, loader <c>0x100AFC5C</c>), enable arm
        /// <c>0x100AAE81 mov byte [ebp-0x10AD],0xEB</c> →
        /// <c>0x100AAEBD call 0x10033340(buf,1,0x767910,0x767910)</c> = a 1-byte
        /// memcpy turning <c>7E</c> (jle) into <c>EB</c> (jmp); the disable arm
        /// <c>0x100AAF13 mov byte [ebp-0x10D5],0x7E</c> /
        /// <c>0x100AAF41</c> writes the original back. Because the jump becomes
        /// unconditional, <c>+0x164</c> is never loaded and
        /// <c>sub_403B4C</c> (Random) is never entered — so the enabled state
        /// must not consume an RNG draw either.
        /// </para>
        /// </summary>
        internal int RollNativeDefenceValue(int defenceRange)
        {
            int lowDefence = HUtil32.LoWord(defenceRange);
            int highDefence = HUtil32.HiWord(defenceRange);
            if (!NativeFixDefenceLockActive() &&
                m_btRaceServer == Grobal2.RC_PLAYOBJECT &&
                m_nBodyLuckLevel > 0 &&
                M2Share.RandomNumber.Random(6 -
                    Math.Min(5, m_nBodyLuckLevel)) == 0)
            {
                return Math.Max(lowDefence, highDefence);
            }

            int defenceSpan = Math.Max(0, highDefence - lowDefence) + 1;
            return lowDefence + M2Share.RandomNumber.Random(defenceSpan);
        }

        // 「修复卡防御」 rewrites host code, so it is not scoped to an actor:
        // whoever reaches 0x00767910 runs the rewritten branch. Only the plugin
        // manager is consulted, same shape as NativeOneSwordOverrideActive().
        private static bool NativeFixDefenceLockActive()
        {
            var api = new Plugins.YanshenApi(null, null, M2Share.PluginManager);
            return api.IsFixDefense();
        }

        /// <summary>
        /// Native <c>sub_76FFE8</c> — the bubble-defence post-processor both
        /// armour entries tail-call (<c>0x7679A9</c> / <c>0x767A09</c>).
        /// <para>
        /// @0x76FFF5 <c>call sub_76FFD4</c> gate: skill id <c>0xDD</c> (221) or
        /// <c>0x7F</c> (127) returns 0 and the whole step is skipped.
        /// @0x770000 <c>mov dl,7; call sub_772960</c> → <c>lea eax,[ebx+ebx*2];
        /// idiv 10</c> = <c>dmg*3/10</c>. Otherwise @0x77001C reads the type-20
        /// timed node; @0x770028 requires <c>dmg&gt;0</c> and a live node;
        /// @0x770038 <c>cmp ecx,4</c> → the same <c>*3/10</c>, else
        /// <c>(lvl+2)*dmg</c> <c>shl 3</c> <c>idiv 100</c>. Both non-state-7
        /// branches then @0x770064 <c>sub dword [eax+2],0xBB8</c> (= 3000).
        /// </para>
        /// </summary>
        internal int ApplyNativeBubbleDefence(int skillId, int damage)
        {
            ushort nativeSkillId = unchecked((ushort)skillId);
            if (nativeSkillId is 127 or 221)
                return damage;

            if (HasNativeActiveState(7))
            {
                return unchecked(damage * 3) / 10;
            }

            if (damage > 0 &&
                TryGetNativeTimedAbilityValue(20, out int bubbleLevel))
            {
                if (bubbleLevel == 4)
                {
                    damage = unchecked(damage * 3) / 10;
                }
                else
                {
                    int scaled = unchecked((bubbleLevel + 2) * damage);
                    damage = unchecked(scaled << 3) / 100;
                }
                ReduceNativeTimedAbilityRemaining(20, 3000);
            }

            return damage;
        }

        private int ResolveNativeMagicDefence(int skillId, byte category,
            int flags, int damage)
        {
            if ((flags & 1) != 0)
                return damage;

            // Internal state 17 skips only the MAC roll. It is not immunity.
            // Player body luck can force the high end of the MAC range.
            if (!HasNativeActiveState(17))
            {
                int defenceRange = category == 4
                    ? m_WAbil.AC
                    : m_WAbil.MAC;
                damage = Math.Max(0, unchecked(damage -
                    RollNativeDefenceValue(defenceRange)));
            }

            return ApplyNativeBubbleDefence(skillId, damage);
        }

        private int ApplyStandardEarthFireSuperForce(TBaseObject source,
            int damage)
        {
            return ApplyNativeMonsterSuperForceReduction(source, damage);
        }

        private int ApplyStandardEarthFireLanding(int damage)
        {
            if (HasNativeActiveState(63))
                damage = unchecked(damage - damage / 2);

            int reductionPercent = m_btNativeMagicDamageReductionPercent;
            if (reductionPercent > 0 && reductionPercent < 100)
            {
                int reduction = unchecked(damage * reductionPercent) / 100;
                damage = unchecked(damage - reduction);
            }

            if (damage > 0 && m_WAbil.MP > 0)
            {
                if (m_boNativeFullMagicShield)
                {
                    int absorbed = Math.Min(damage, m_WAbil.MP);
                    damage -= absorbed;
                    m_WAbil.MP -= absorbed;
                }
                else if (m_boMagicShield)
                {
                    int shieldCost = (int)Math.Round(damage * 1.5d,
                        MidpointRounding.ToEven);
                    if (m_WAbil.MP >= shieldCost)
                    {
                        m_WAbil.MP -= shieldCost;
                        shieldCost = 0;
                    }
                    else
                    {
                        shieldCost -= m_WAbil.MP;
                        m_WAbil.MP = 0;
                    }

                    damage = (int)Math.Round(shieldCost / 1.5d,
                        MidpointRounding.ToEven);
                }
                else if (m_boNativeHalfMagicShield)
                {
                    int absorbed = Math.Min(damage >> 1, m_WAbil.MP);
                    damage -= absorbed;
                    m_WAbil.MP -= absorbed;
                }
            }

            if (damage > 0)
            {
                m_WAbil.HP = Math.Max(0,
                    unchecked(m_WAbil.HP - damage));
            }
            else
            {
                m_WAbil.HP = Math.Min(m_WAbil.MaxHP,
                    unchecked(m_WAbil.HP - damage));
            }

            // sub_7693E8 only sets target+0x99. The native player loop flushes
            // that bit on its 500ms cadence; immediate HealthSpellChanged()
            // would expose an extra packet and broadcast here.
            m_boNativeHealthSpellDirty = true;
            return Math.Max(damage, 1);
        }

        protected virtual void OnStandardEarthFireDamageApplied(int applied,
            TBaseObject source)
        {
            SendMsg(this, Grobal2.RM_STRUCK, unchecked((ushort)applied),
                applied, 0, source?.ObjectId ?? 0, string.Empty);
        }
    }

    public partial class TPlayObject
    {
        protected override void OnStandardEarthFireDamageApplied(int applied,
            TBaseObject source)
        {
            // Native TPlayObject's VMT+0x28 implementation is a no-op.
        }
    }
}
