using System.IO;
using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    public partial class TBaseObject
    {
        internal const int NativeBreakContestWindowMilliseconds = 60_000;
        internal const ushort NativeBreakContestMaximumPool = 5_000;

        private static readonly int[] NativeBreakContestGainByJob =
            { 100, 50, 80, 100 };
        private static readonly float[] NativeBreakContestScaleByJob =
            { 1.0f, 0.4f, 0.8f, 1.0f };

        internal int m_dwNativeBreakContestPoolTick;
        internal ushort m_wNativeBreakContestPool;
        internal ushort m_wNativeBreakPower;
        internal ushort m_wNativeCrazyPower;
        internal ushort m_wNativeHumanMagicPercentReduction;

        private void ProjectNativeBreakContestAbilities()
        {
            m_wNativeBreakPower = m_AddAbil.NativeBreakPower;
            m_wNativeCrazyPower = m_AddAbil.NativeCrazyPower;
            m_wNativeHumanMagicPercentReduction = unchecked((ushort)
                m_AddAbil.NativeHumanMagicPercentReductionRaw);
        }

        internal int ApplyNativeHumanMagicPercentReduction(int damage)
        {
            int percent = unchecked((short)
                m_wNativeHumanMagicPercentReduction);
            if (percent <= 0)
                return damage;

            int reduction = unchecked(percent * damage) / 100;
            if (reduction > 20_000)
                reduction = 20_000;
            return unchecked(damage - reduction);
        }

        internal int ApplyNativeHumanMagicBreakContest(TBaseObject source,
            int originalDamage, int currentDamage, int skillId,
            ref ushort combinedLevel, ref int extra)
        {
            int bonus = ApplyNativeBreakContest(source, originalDamage,
                ref combinedLevel, ref extra, currentDamage,
                source?.m_wNativeBreakPower ?? 0,
                source?.m_wNativeCrazyPower ?? 0,
                source?.m_PEnvir?.BreakLevel ?? 0,
                source?.m_PEnvir?.CrazyBreakLevel ?? 0,
                NativeGlobalBreakSettings.ProcBaseChance,
                NativeGlobalBreakSettings.BreakLevel,
                NativeGlobalBreakSettings.CrazyBreakLevel);

            if (skillId is 22 or 127)
            {
                extra = 0;
                return 0;
            }
            return bonus;
        }

        internal int ApplyNativeBreakContest(TBaseObject source,
            int originalDamage, ref ushort combinedLevel, ref int extra,
            int currentDamage, ushort sourceBreakLevel,
            ushort sourceCrazyBreakLevel, byte mapBreakLevel,
            ushort mapCrazyBreakLevel, int globalBaseChance,
            int globalBreakLevel, int globalCrazyBreakLevel)
        {
            return ApplyNativeBreakContestCore(source, originalDamage,
                ref combinedLevel, ref extra, currentDamage,
                sourceBreakLevel, sourceCrazyBreakLevel, mapBreakLevel,
                mapCrazyBreakLevel, globalBaseChance, globalBreakLevel,
                globalCrazyBreakLevel, false, 0);
        }

        internal int ApplyNativeBreakContest(TBaseObject source,
            int originalDamage, ref ushort combinedLevel, ref int extra,
            int currentDamage, ushort sourceBreakLevel,
            ushort sourceCrazyBreakLevel, byte mapBreakLevel,
            ushort mapCrazyBreakLevel, int globalBaseChance,
            int globalBreakLevel, int globalCrazyBreakLevel,
            int currentTick)
        {
            return ApplyNativeBreakContestCore(source, originalDamage,
                ref combinedLevel, ref extra, currentDamage,
                sourceBreakLevel, sourceCrazyBreakLevel, mapBreakLevel,
                mapCrazyBreakLevel, globalBaseChance, globalBreakLevel,
                globalCrazyBreakLevel, true, currentTick);
        }

        private int ApplyNativeBreakContestCore(TBaseObject source,
            int originalDamage, ref ushort combinedLevel, ref int extra,
            int currentDamage, ushort sourceBreakLevel,
            ushort sourceCrazyBreakLevel, byte mapBreakLevel,
            ushort mapCrazyBreakLevel, int globalBaseChance,
            int globalBreakLevel, int globalCrazyBreakLevel,
            bool useProvidedTick, int currentTick)
        {
            combinedLevel = 0;
            extra = 0;
            if (source == null || source.m_btRaceServer !=
                Grobal2.RC_PLAYOBJECT && source.m_btRaceServer !=
                Grobal2.RC_HEROOBJECT)
            {
                return 0;
            }

            int targetLevel = m_Abil.Level;
            int sourceLevel = source.m_Abil.Level;
            int levelSum = unchecked(targetLevel + sourceLevel);
            int levelDifference = Math.Abs(sourceLevel - targetLevel);

            int breakLevel = sourceBreakLevel;
            int crazyBreakLevel = sourceCrazyBreakLevel;
            if (mapBreakLevel > 0)
                breakLevel = unchecked(breakLevel + mapBreakLevel);
            if (mapCrazyBreakLevel > 0)
                crazyBreakLevel = unchecked(crazyBreakLevel +
                    mapCrazyBreakLevel);
            if (globalBreakLevel > 0)
                breakLevel = unchecked(breakLevel + globalBreakLevel);
            if (globalCrazyBreakLevel > 0)
                crazyBreakLevel = unchecked(crazyBreakLevel +
                    globalCrazyBreakLevel);

            if (breakLevel < 1 && crazyBreakLevel < 1)
                return 0;

            combinedLevel = unchecked((ushort)(breakLevel +
                crazyBreakLevel));
            m_dwNativeBreakContestPoolTick = useProvidedTick
                ? currentTick
                : HUtil32.GetTickCount();

            int gain = 0;
            if (currentDamage > 0)
            {
                int product = unchecked(
                    NativeBreakContestGainByJob[source.m_btJob] *
                    currentDamage);
                gain = unchecked((int)Math.Truncate(product / 5000.0d) +
                    10);
                gain = Math.Min(gain, 35);
            }
            else
            {
                int threshold = originalDamage switch
                {
                    > 200 => 100,
                    > 150 => 80,
                    > 100 => 60,
                    > 50 => 40,
                    > 0 => 20,
                    _ => -1
                };
                if (M2Share.RandomNumber.Random(100) <= threshold)
                    gain = 10;
            }

            if (gain > 0)
            {
                int pooled = unchecked(m_wNativeBreakContestPool + gain);
                m_wNativeBreakContestPool = pooled >
                    NativeBreakContestMaximumPool
                    ? NativeBreakContestMaximumPool
                    : unchecked((ushort)(m_wNativeBreakContestPool +
                        unchecked((ushort)gain)));
            }

            if (m_wNativeBreakContestPool == 0 || currentDamage <= 0)
                return 0;

            float poolFactor = (float)((1.0d -
                Math.Min(levelDifference, 800) * 0.001d) *
                (m_wNativeBreakContestPool / 1000.0d));
            if (poolFactor < 1.0f)
                poolFactor = 1.0f;

            int roundedPoolFactor = RoundNativeBreakContest(poolFactor);
            int randomMaximum = unchecked(roundedPoolFactor * 2);
            if (randomMaximum > 10)
                randomMaximum = 10;
            else if (randomMaximum < roundedPoolFactor)
                randomMaximum = roundedPoolFactor;

            int randomAddition = M2Share.RandomNumber.Random(
                randomMaximum - roundedPoolFactor + 1);
            poolFactor = (float)(poolFactor + randomAddition);
            if (poolFactor < 1.0f)
                poolFactor = 1.0f;
            else if (poolFactor > 10.0f)
                poolFactor = 10.0f;

            if (breakLevel <= 0)
                return 0;

            int chance = unchecked(RoundNativeBreakContest(
                (1.0d + (targetLevel - sourceLevel) * 0.002d) *
                (m_wNativeBreakContestPool / 20.0d)) +
                globalBaseChance);
            if (chance > 100)
                chance = 100;
            else if (globalBaseChance > chance)
                chance = globalBaseChance;

            if (M2Share.RandomNumber.Random(100) > chance)
                return 0;

            float levelRatio = (float)(levelSum /
                (double)(levelSum + levelDifference + 100));
            int workingMaximum = GetNativeBreakContestWorkingMaximum();
            double strength = workingMaximum / 2.0d +
                targetLevel / 100.0d + unchecked(breakLevel * 2);
            double scale = levelRatio *
                NativeBreakContestScaleByJob[source.m_btJob];
            scale *= poolFactor;
            return RoundNativeBreakContest(strength * scale);
        }

        internal void ProcessNativeBreakContestPool(int currentTick)
        {
            if (unchecked((uint)(currentTick -
                m_dwNativeBreakContestPoolTick)) <=
                NativeBreakContestWindowMilliseconds)
            {
                return;
            }

            m_wNativeBreakContestPool = 0;
            m_dwNativeBreakContestPoolTick = currentTick;
        }

        internal ushort GetNativeBreakContestRemainingMilliseconds(
            int currentTick)
        {
            int elapsed = unchecked(currentTick -
                m_dwNativeBreakContestPoolTick);
            int remaining = unchecked(
                NativeBreakContestWindowMilliseconds - elapsed);
            return remaining > 0 ? unchecked((ushort)remaining) : (ushort)0;
        }

        internal void WriteNativeBreakContestPool(BinaryWriter writer,
            int currentTick)
        {
            writer.Write(GetNativeBreakContestRemainingMilliseconds(
                currentTick));
            writer.Write(m_wNativeBreakContestPool);
        }

        internal void ReadNativeBreakContestPool(BinaryReader reader,
            int currentTick)
        {
            ushort remaining = reader.ReadUInt16();
            ushort pool = reader.ReadUInt16();
            RestoreNativeBreakContestPool(remaining, pool, currentTick);
        }

        internal void RestoreNativeBreakContestPool(ushort remaining,
            ushort pool, int currentTick)
        {
            if (remaining > NativeBreakContestWindowMilliseconds)
                remaining = 0;

            m_dwNativeBreakContestPoolTick = unchecked(currentTick -
                NativeBreakContestWindowMilliseconds + remaining);
            m_wNativeBreakContestPool = pool;
        }

        private int GetNativeBreakContestWorkingMaximum()
        {
            return m_btJob switch
            {
                0 => HUtil32.HiWord(m_WAbil.DC),
                1 => HUtil32.HiWord(m_WAbil.MC),
                2 => HUtil32.HiWord(m_WAbil.SC),
                3 => m_NativeCoreWorkingAbility.CCHigh,
                _ => 0
            };
        }

        private static int RoundNativeBreakContest(double value)
        {
            return unchecked((int)Math.Round(value,
                MidpointRounding.ToEven));
        }
    }
}
