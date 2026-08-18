using System;
using System.Buffers.Binary;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        private const uint NativeExperienceChunkLimit = 0xFFB43480u;
        private const uint NativeNaturalHeroLevel200Cap = 0xFD51DA7Fu;

        internal void RefreshNativeHeroIntimacy()
        {
            var accumulated = (int)Math.Round(m_dNativeHeroIntimacy,
                MidpointRounding.ToEven);
            m_nNativeHeroIntimacyCurrent = Math.Min(
                m_nNativeHeroIntimacyBase + accumulated, 1000);
        }

        internal void AddNativeHeroExperienceAccumulator(int amount, int slot)
        {
            if (amount == 0) return;
            if (m_NativeHeroExperienceAccumulator == null ||
                m_NativeHeroExperienceAccumulator.Length != 24)
                m_NativeHeroExperienceAccumulator = new byte[24];

            var index = slot & 0x7F;
            if (index >= 4) return;
            var counts = m_NativeHeroExperienceAccumulator.AsSpan(0, 8);
            var values = m_NativeHeroExperienceAccumulator.AsSpan(8, 16);
            var countOffset = index * 2;
            var valueOffset = index * 4;
            var value = BinaryPrimitives.ReadUInt32LittleEndian(
                values.Slice(valueOffset, 4));
            var count = BinaryPrimitives.ReadUInt16LittleEndian(
                counts.Slice(countOffset, 2));

            const uint unit = 1_000_000_000;
            if (value >= unit)
            {
                value -= unit;
                count++;
            }
            count = Math.Min(count, (ushort)2000);

            var added = unchecked((uint)amount);
            var remaining = unit - value;
            if (remaining < added)
            {
                value = unchecked(added - remaining);
                count++;
            }
            else
            {
                value = unchecked(value + added);
            }

            BinaryPrimitives.WriteUInt16LittleEndian(
                counts.Slice(countOffset, 2), count);
            BinaryPrimitives.WriteUInt32LittleEndian(
                values.Slice(valueOffset, 4), value);
        }

        internal void QueueNativeGloryFealty(int combinedFealty)
        {
            var glory = Math.Max(m_nNativeHeroIntimacyCurrent, 0);
            SendMsg(this, Grobal2.RM_GLORYFEALTY, 0, glory, combinedFealty,
                0, string.Empty);
        }

        internal void GrantNativeScriptExperience(int amount)
        {
            GrantNativePlayerExperience(amount, true, true, 0);
        }

        internal void GrantNativePlayerExperience(int amount, bool shareWithHero,
            bool countAsFightExperience, int experienceMode)
        {
            if (amount < 0) return;

            var requested = unchecked((uint)amount);
            var current = unchecked((uint)m_Abil.Exp);
            var accepted = requested;
            if (current > unchecked(current + requested))
            {
                accepted = unchecked(NativeExperienceChunkLimit - current);
                var remainder = unchecked(requested - accepted);
                SendMsg(this, Grobal2.RM_NATIVE_EXP_CONTINUE, experienceMode,
                    unchecked((int)remainder), shareWithHero ? 1 : 0,
                    countAsFightExperience ? 1 : 0, string.Empty);
            }

            m_Abil.Exp = unchecked((int)(current + accepted));
            if (countAsFightExperience)
                m_dwFightExp = unchecked((int)((uint)m_dwFightExp + accepted));

            var hero = m_HeroObject;
            if (shareWithHero && hero != null && !hero.m_boDeath)
            {
                var heroExperience = unchecked((int)accepted) / 100 *
                                     (M2Share.RandomNumber.Random(5) + 8);
                GrantNativeHeroExperience(hero, heroExperience, countAsFightExperience, false);
            }

            AccumulateNativeSwitchExperience(accepted);

            // EXP-09: Native 6C04DB-6C04E8: after Exp+=, level>=999 zeroes exp before
            // RM_WINEXP and the level-up loop, preventing overflow accumulation at cap.
            if (m_Abil.Level >= 999)
                m_Abil.Exp = 0;

            SendMsg(this, Grobal2.RM_WINEXP, experienceMode, unchecked((int)accepted),
                0, 0, string.Empty);

            while (m_Abil.MaxExp != 0 &&
                   unchecked((uint)m_Abil.MaxExp) <= unchecked((uint)m_Abil.Exp))
            {
                m_Abil.Exp = unchecked((int)((uint)m_Abil.Exp - (uint)m_Abil.MaxExp));
                if (m_Abil.Level >= 999)
                {
                    SysMsg("您的等级已到达上限，不会再获得经验值",
                        MsgColor.Red, MsgType.Hint);
                    continue;
                }

                var previousLevel = m_Abil.Level;
                m_Abil.Level++;
                HasLevelUp(previousLevel);
                IncHealthSpell(20000, 20000);
            }
        }

        private void AccumulateNativeSwitchExperience(uint awardedExperience)
        {
            if (m_wNativeSwitchOffsetD38 != 0)
                return;
            m_nNativeSwitchOffsetD40 = unchecked((int)(
                (uint)m_nNativeSwitchOffsetD40 + awardedExperience));
        }

        internal void GrantNativeHeroExperience(HeroObject hero, int amount,
            bool countAsFightExperience, bool directMode)
        {
            if (hero == null || amount == 0) return;
            var type2Eligible = hero.HeroType != 2 ||
                                (uint)hero.m_Abil.Level + 3u <= (ushort)m_nForceLv;
            if (directMode && (hero.m_Abil.Level >= 999 || !type2Eligible))
            {
                hero.SendMsg(hero, Grobal2.RM_WINEXP, 0, amount, 0, 0, string.Empty);
                return;
            }

            var requested = unchecked((uint)amount);
            var current = unchecked((uint)hero.m_Abil.Exp);
            var accepted = requested;
            if (current > unchecked(current + unchecked(requested * 2u)))
            {
                accepted = unchecked((NativeExperienceChunkLimit - current) >> 1);
                var remainder = unchecked(requested - accepted);
                hero.SendMsg(hero, Grobal2.RM_NATIVE_EXP_CONTINUE, 0,
                    unchecked((int)remainder), directMode ? 1 : 0,
                    countAsFightExperience ? 1 : 0, string.Empty);
            }

            if (accepted == 0) return;

            if (countAsFightExperience)
                hero.m_dwFightExp = unchecked((int)((uint)hero.m_dwFightExp + accepted));

            if (hero.HeroType == 1)
                AddNativeHeroExperienceAccumulator(unchecked((int)accepted), 0);

            var applied = accepted;
            if (hero.HeroType == 2)
            {
                if (!type2Eligible)
                {
                    // 0x687815 jbe 0x687854 not taken -> 0x687817..0x68784F, which is
                    // NOT a bare bail-out: it hints the OWNER before zeroing the result.
                    //   0x687817  8B 83 8C 06 00 00     mov  eax,[hero+0x68C]   ; owner
                    //   0x68781D  80 B8 98 18 00 00 00  cmp  byte [owner+0x1898],0
                    //   0x687824  74 25                 je   0x68784B           ; hint off
                    //   0x687826  B8 64 00 00 00        mov  eax,100
                    //   0x68782B  E8 1C C3 D7 FF        call 0x403B4C           ; Random(100)
                    //   0x687830  85 C0 / 75 17         test eax,eax / jne 0x68784B
                    //   0x687834  66 B9 FF FC           mov  cx,0xFCFF          ; BColor 0xFC = Blue
                    //   0x687838  BA 1C 7A 68 00        mov  edx,0x687A1C
                    //   0x687845  FF 93 D4 00 00 00     call [owner_vmt+0xD4]
                    //   0x68784B  C6 45 FE 00           mov  byte [ebp-2],0     ; result := false
                    // Literal 0x687A1C is a declen-60 Delphi string, read byte-for-byte.
                    // The `je` precedes the Random call, so a suppressed hint consumes no
                    // RNG draw - the && short-circuit reproduces that call order exactly.
                    if (m_boNativeHeroCapHintEnabled &&
                        M2Share.RandomNumber.Random(100) == 0)
                    {
                        SysMsg("您的副将英雄等级受限于主将等级，主将等级提升后方能继续修炼！",
                            MsgColor.Blue, MsgType.Hint);
                    }
                    return;
                }
                applied = unchecked(accepted * 2u);
            }

            if (!directMode &&
                (hero.m_Abil.Level > 200 ||
                 (hero.m_Abil.Level == 200 &&
                  unchecked((uint)hero.m_Abil.Exp) >= NativeNaturalHeroLevel200Cap)))
                return;

            hero.SendMsg(hero, Grobal2.RM_WINEXP, 0, unchecked((int)applied),
                0, 0, string.Empty);
            hero.m_Abil.Exp = unchecked((int)((uint)hero.m_Abil.Exp + applied));
            if (!directMode && hero.m_Abil.Level == 200 &&
                unchecked((uint)hero.m_Abil.Exp) > NativeNaturalHeroLevel200Cap)
                hero.m_Abil.Exp = unchecked((int)NativeNaturalHeroLevel200Cap);

            while (hero.m_Abil.MaxExp != 0 &&
                   unchecked((uint)hero.m_Abil.MaxExp) <= unchecked((uint)hero.m_Abil.Exp))
            {
                var previousLevel = hero.m_Abil.Level;
                hero.m_Abil.Exp = unchecked((int)((uint)hero.m_Abil.Exp -
                                                   (uint)hero.m_Abil.MaxExp));
                if (directMode || hero.m_Abil.Level < 200)
                {
                    hero.m_Abil.Level = unchecked((ushort)(hero.m_Abil.Level + 1));
                    if (hero.HeroType == 1)
                    {
                        m_nForceLv = (m_nForceLv & unchecked((int)0xFFFF0000)) |
                                     hero.m_Abil.Level;
                    }
                }

                hero.HeroLevel = hero.m_Abil.Level;
                // 0x687930 calls [vtbl+0x240] before 0x687936 re-reads the threshold, and that
                // slot (0x6BDBD3) rewrites it from the level table -- without this the loop
                // keeps subtracting the stale threshold and grants one level per 100 exp.
                hero.m_Abil.MaxExp = hero.GetLevelExp(hero.m_Abil.Level);
                hero.RecalcLevelAbilitys();
                hero.RecalcAbilitys();
                hero.SendMsg(hero, Grobal2.RM_LEVELUP, 0, hero.m_Abil.Exp,
                    previousLevel, 0, string.Empty);
            }

            if (applied != 0 &&
                hero.m_nForceLv < NativeHeroForceTable.MaximumLevel)
            {
                var force = unchecked((uint)hero.m_nForceExp + applied);
                if (force >= unchecked((uint)hero.m_nMaxForceExp))
                {
                    do
                    {
                        var threshold = unchecked((uint)hero.m_nMaxForceExp);
                        if (force < threshold)
                            break;
                        force = unchecked(force - threshold);
                        hero.m_nForceLv = unchecked(hero.m_nForceLv + 1);
                        hero.m_nMaxForceExp = NativeHeroForceTable.GetThreshold(
                            hero.m_nForceLv);
                    }
                    while (hero.m_nForceLv < NativeHeroForceTable.MaximumLevel);

                    hero.RefreshNativeForceState();
                }
                hero.m_nForceExp = unchecked((int)force);
            }
        }
    }
}
