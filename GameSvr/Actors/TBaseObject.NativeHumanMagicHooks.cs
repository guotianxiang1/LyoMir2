using SystemModule;

namespace GameSvr
{
    public partial class TBaseObject
    {
        private const int NativeHumanMagicEffectMessage = 10501;
        private const int NativeSkill190Id = 190;
        private const int NativeSkill306Id = 306;
        private const int NativeSkill307Id = 307;
        private const int NativeSkill308Id = 308;
        private const byte NativeSkill306State = 102;
        private const uint NativeSkill306CooldownMilliseconds = 120_000;

        private static readonly int[] NativeSkill306Durations =
            { 0, 5_000, 8_000, 10_000 };
        private static readonly int[] NativeSkill307Percents =
            { 0, 5, 10, 15 };
        private static readonly int[] NativeSkill308Percents =
            { 0, 5, 10, 15 };
        private static readonly int[] NativeState16DamageCaps =
            { 0, 800, 800, 600, 400, 200, 200, 515 };

        // Independent native carriers. Persistence/equipment projection remains
        // detached until its owning binary paths are closed.
        internal byte m_btNativeHumanHqEnabled;
        internal ushort m_wNativeHumanHqChance;
        internal int m_nNativeJob3BaseAbilityMax;
        internal int m_dwNativeSkill306ProcTick;

        internal static int GetNativeHumanMagicEffectiveLevel(
            TUserMagic magic)
        {
            if (magic?.MagicInfo == null)
                return 0;

            return Math.Min(
                unchecked((byte)(magic.btLevel + magic.NativeLevelBonus)),
                magic.MagicInfo.btTrainLv);
        }

        internal int ApplyNativeSkill307Damage(int damage)
        {
            TUserMagic magic = GetMagicInfo(NativeSkill307Id);
            int level = GetNativeHumanMagicEffectiveLevel(magic);
            if ((uint)(level - 1) >= 3u)
                return damage;

            return ScaleNativeHumanMagicDamage(damage,
                NativeSkill307Percents[level]);
        }

        internal bool TryApplyNativeSkill306(TBaseObject target)
        {
            return TryApplyNativeSkill306Core(target, false, 0, 0);
        }

        internal bool TryApplyNativeSkill306(TBaseObject target,
            int currentTick, int successfulProcTick)
        {
            return TryApplyNativeSkill306Core(target, true, currentTick,
                successfulProcTick);
        }

        private bool TryApplyNativeSkill306Core(TBaseObject target,
            bool useProvidedTicks, int currentTick, int successfulProcTick)
        {
            TUserMagic magic = GetMagicInfo(NativeSkill306Id);
            int level = GetNativeHumanMagicEffectiveLevel(magic);
            if ((uint)(level - 1) >= 3u)
                return false;

            int now = useProvidedTicks
                ? currentTick
                : HUtil32.GetTickCount();
            if (unchecked((uint)(now - m_dwNativeSkill306ProcTick)) <
                NativeSkill306CooldownMilliseconds)
            {
                return true;
            }

            int roll = M2Share.RandomNumber.Random(100);
            int chance = m_btJob switch
            {
                0 => 12,
                1 => 20,
                2 => 20,
                3 => 8,
                _ => 0
            };
            if (roll >= chance)
                return true;

            target.AddTimedAbilityInternal(NativeSkill306State, 0,
                NativeSkill306Durations[level], 0);
            m_dwNativeSkill306ProcTick = useProvidedTicks
                ? successfulProcTick
                : HUtil32.GetTickCount();
            return true;
        }

        internal int ApplyNativeSkill308LowHealthDamage(TBaseObject target,
            int damage)
        {
            TUserMagic magic = GetMagicInfo(NativeSkill308Id);
            int level = GetNativeHumanMagicEffectiveLevel(magic);
            if ((uint)(level - 1) >= 3u || target == null ||
                !IsProperTarget(target))
            {
                return damage;
            }

            int healthPercent = unchecked((int)Math.Truncate(
                target.m_WAbil.HP / (double)target.m_WAbil.MaxHP * 100.0d));
            if (healthPercent >= 30)
                return damage;

            return ScaleNativeHumanMagicDamage(damage,
                NativeSkill308Percents[level]);
        }

        internal int GetNativeSkill190DamageBonus(TBaseObject target,
            byte arg0, int resolverSkillId)
        {
            // Native lookup precedes the resolver-skill exclusion check.
            TUserMagic magic = GetMagicInfo(NativeSkill190Id);
            if (IsNativeSkill190ExcludedResolverSkill(resolverSkillId) ||
                magic == null)
            {
                return 0;
            }

            int divisor = resolverSkillId switch
            {
                234 => 26,
                235 or 236 => 9,
                _ => m_btJob switch
                {
                    0 => 8,
                    1 => 6,
                    2 => 6,
                    3 => 8,
                    _ => int.MaxValue
                }
            };

            bool proc = M2Share.RandomNumber.Random(divisor) == 0;
            if ((arg0 & (proc ? 1 : 0)) == 0 || target == null ||
                !IsNativeHumanMagicSource())
            {
                return 0;
            }

            int stat = m_btJob switch
            {
                0 => HUtil32.HiWord(m_WAbil.DC),
                1 => HUtil32.HiWord(m_WAbil.MC),
                2 => HUtil32.HiWord(m_WAbil.SC),
                3 => m_nNativeJob3BaseAbilityMax,
                _ => 0
            };
            int bonus = unchecked((int)Math.Truncate(stat * 1.5d));
            if (bonus > 0)
                target.SendNativeHumanMagicEffect(52);
            return bonus;
        }

        internal int ApplyNativeHumanHqReduction(int damage)
        {
            return this is HeroObject
                ? ApplyNativeHeroHqReduction(damage)
                : ApplyNativePlayerHqReduction(damage);
        }

        private int ApplyNativeHeroHqReduction(int damage)
        {
            int level = m_Abil.Level;
            if (m_btNativeHumanHqEnabled == 0 || level < 34)
                return damage;

            int baseChance = level >= 39 ? 15 : 10;
            if (M2Share.RandomNumber.Random(100) >=
                m_wNativeHumanHqChance + baseChance)
            {
                return damage;
            }

            int percent = Math.Min((level - 34) * 5 + 30, 70);
            int reduction = unchecked(damage * percent) / 100;
            int result = unchecked(damage - reduction);
            SendNativeHumanMagicEffect(11);
            return result;
        }

        private int ApplyNativePlayerHqReduction(int damage)
        {
            if (m_btNativeHumanHqEnabled == 0)
                return damage;

            if (M2Share.RandomNumber.Random(100) >=
                m_wNativeHumanHqChance + 10)
            {
                return damage;
            }

            int reduction = unchecked(damage *
                GetNativeHqReductionPercent()) / 100;
            int result = unchecked(damage - reduction);
            SendNativeHumanMagicEffect(11);
            return result;
        }

        /// <summary>
        /// The level-driven 护体神盾 reduction percentage. Native computes it
        /// identically in the magic entry <c>sub_6EC394</c>
        /// (@0x6EC3BE-0x6EC3ED) and in the physical block/parry proc inside
        /// <c>StruckDamage</c> = <c>sub_73F9FC</c> (@0x73FAF8-0x73FB27):
        /// <c>mov ax,[ebx+0x278]; cmp ax,0x2F; jae</c> → below level 47 the
        /// constant <c>0x1E</c> (30); at or above,
        /// <c>sub eax,0x2F; sar eax,1</c> (round-to-zero /2)
        /// <c>lea ecx,[eax+eax*4]</c> (×5) <c>add ecx,0x28</c> (+40), then
        /// <c>cmp ecx,0x46; jle</c> caps at <c>0x46</c> (70).
        /// <c>+0x278</c> is the level word (== <c>m_Abil.Level</c>).
        /// The two entries differ ONLY in their proc chance: the magic one rolls
        /// <c>Random(100) &lt; [+0x47C]+10</c>, the physical one a fixed
        /// <c>Random(100) &lt; 10</c>.
        /// </summary>
        internal int GetNativeHqReductionPercent()
        {
            int level = m_Abil.Level;
            return level < 47
                ? 30
                : Math.Min(((level - 47) / 2) * 5 + 40, 70);
        }

        /// <summary>
        /// Native physical block/parry proc — <c>sub_73F9FC</c>
        /// @0x73FAE0-0x73FB53, run inside <c>StruckDamage</c> after the
        /// damage-amplify states and before the durability worker.
        /// <para>
        /// @0x73FAE0 <c>cmp byte [ebx+0x4C8],0; je</c> — the same 护体神盾
        /// carrier the magic entry gates on (== <c>m_btNativeHumanHqEnabled</c>).
        /// @0x73FAE9 <c>mov eax,0x64; call sub_403B4C; cmp eax,0xA; jge</c> —
        /// a FIXED 10 % chance (unlike <c>sub_6EC394</c>, this path adds no
        /// <c>[+0x47C]</c> chance bonus). Percent then comes from the shared
        /// level formula (30 % below level 47, rising to a 70 % cap).
        /// @0x73FB29 <c>imul/idiv 100; sub esi,eax</c> reduces the damage and
        /// @0x73FB37 broadcasts ident <c>0x2905</c> with payload <c>0xB</c> (11)
        /// through VMT <c>+0xD8</c> — the identical send the magic path uses.
        /// </para>
        /// </summary>
        /// <returns>
        /// True when the proc fired. Native only re-tests
        /// <c>test esi,esi; jle</c> (@0x73FB51) on the taken branch, so the
        /// caller must apply its non-positive early return only in that case.
        /// </returns>
        internal bool TryApplyNativePhysicalBlockProc(ref int damage)
        {
            if (m_btNativeHumanHqEnabled == 0)
                return false;

            if (M2Share.RandomNumber.Random(100) >= 10)
                return false;

            int reduction = unchecked(damage *
                GetNativeHqReductionPercent()) / 100;
            damage = unchecked(damage - reduction);
            SendNativeHumanMagicEffect(11);
            return true;
        }

        internal int ApplyNativeState16MagicDamageCap(int resolverSkillId,
            int flags, int damage)
        {
            bool active = TryGetNativeTimedAbilityValue(16, out int value);
            return CalculateNativeState16MagicDamageCap(resolverSkillId,
                flags, damage, active, value);
        }

        internal static int CalculateNativeState16MagicDamageCap(
            int resolverSkillId, int flags, int damage, bool active,
            int stateValue)
        {
            if (!active || (flags & 0x10) != 0 ||
                (uint)(stateValue - 1) >= 7u ||
                resolverSkillId == 127 && stateValue < 5)
            {
                return damage;
            }

            int cap = NativeState16DamageCaps[stateValue];
            return damage > cap ? cap : damage;
        }

        internal static bool IsNativeSkill190ExcludedResolverSkill(
            int resolverSkillId)
        {
            return resolverSkillId is 6 or 22 or 116 or 117 or 118 or
                125 or 126 or 127 or 190 or 270;
        }

        private bool IsNativeHumanMagicSource()
        {
            return this is TPlayObject || this is HeroObject;
        }

        private static int ScaleNativeHumanMagicDamage(int damage,
            int percent)
        {
            return unchecked((int)Math.Truncate(
                damage * (1.0d + percent / 100.0d)));
        }

        /// <summary>
        /// The effect number is nParam1, not the opaque payload slot. Every
        /// native emitter of ident 0x2905 pushes it FIRST and zeroes ecx:
        /// <c>0x73FB37 6A 0B</c> (physical block, 11),
        /// <c>0x6EC3FD 6A 0B</c> (the magic 护体神盾 entry, 11),
        /// <c>0x7445CA 6A 1A</c> (the 65..68 crit, 26),
        /// <c>0x674E74 6A 16</c> (AttackIceTower, 22),
        /// <c>0x717F54 6A 20</c> (OnceDamageTrapEvent, 32) — all followed by
        /// `6A 00` x4, `33 C9 xor ecx,ecx` and `66 BA 05 29 mov dx,0x2905`.
        /// SendRefMsg's stack params are pushed left to right (calibrated on
        /// TBigHeartMon @0x68105C, see AttackIceTower.cs), so push #1 is
        /// nParam1. Passing it as the trailing `object payload` instead left
        /// nParam1 at 0 and boxed the number where nothing reads it.
        /// </summary>
        private void SendNativeHumanMagicEffect(int payload)
        {
            SendRefMsg(NativeHumanMagicEffectMessage, 0, payload, 0, 0,
                string.Empty);
        }
    }
}
