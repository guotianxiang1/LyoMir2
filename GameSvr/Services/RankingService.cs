using System;
using System.Collections.Generic;
using SystemModule;

namespace GameSvr.Services
{
    /// <summary>
    /// Simple in-memory ranking system. Tracks top players by level, DC, MC, SC,
    /// PK points, and online duration. Updated periodically from the game loop.
    /// </summary>
    public class RankingService
    {
        private readonly List<RankEntry> m_LevelRanking = new List<RankEntry>();
        private readonly List<RankEntry> m_CombatRanking = new List<RankEntry>();
        private readonly object m_Lock = new object();

        private int m_dwLastUpdateTick;
        private const int UPDATE_INTERVAL = 10000; // 10 seconds

        public int MaxRankEntries { get; set; } = 100;

        public RankingService()
        {
            m_dwLastUpdateTick = HUtil32.GetTickCount();
        }

        /// <summary>Periodic tick — rebuild rankings if enough time has passed.</summary>
        public void Run()
        {
            if ((HUtil32.GetTickCount() - m_dwLastUpdateTick) < UPDATE_INTERVAL)
                return;

            m_dwLastUpdateTick = HUtil32.GetTickCount();
            RebuildRankings();
        }

        /// <summary>Force an immediate ranking rebuild.</summary>
        public void RebuildRankings()
        {
            var players = new List<TPlayObject>();
            foreach (var p in M2Share.UserEngine.PlayObjects)
            {
                if (p != null && !p.m_boDeath && !p.m_boGhost)
                    players.Add(p);
            }

            lock (m_Lock)
            {
                m_LevelRanking.Clear();
                m_CombatRanking.Clear();

                // Level ranking
                players.Sort((a, b) =>
                {
                    int cmp = b.m_Abil.Level.CompareTo(a.m_Abil.Level);
                    if (cmp != 0) return cmp;
                    return b.m_Abil.Exp.CompareTo(a.m_Abil.Exp);
                });
                for (int i = 0; i < Math.Min(players.Count, MaxRankEntries); i++)
                {
                    m_LevelRanking.Add(new RankEntry
                    {
                        Rank = i + 1,
                        CharName = players[i].m_sCharName,
                        Level = players[i].m_Abil.Level,
                        Value = players[i].m_Abil.Exp,
                        Job = players[i].m_btJob
                    });
                }

                // Combat power ranking (DC + MC + SC)
                players.Sort((a, b) =>
                {
                    int combatA = GetCombatPower(a);
                    int combatB = GetCombatPower(b);
                    return combatB.CompareTo(combatA);
                });
                for (int i = 0; i < Math.Min(players.Count, MaxRankEntries); i++)
                {
                    m_CombatRanking.Add(new RankEntry
                    {
                        Rank = i + 1,
                        CharName = players[i].m_sCharName,
                        Level = players[i].m_Abil.Level,
                        Value = GetCombatPower(players[i]),
                        Job = players[i].m_btJob
                    });
                }
            }
        }

        /// <summary>Get the top N players by level ranking.</summary>
        public List<RankEntry> GetTopLevelPlayers(int count = 10)
        {
            lock (m_Lock)
            {
                var result = new List<RankEntry>();
                for (int i = 0; i < Math.Min(count, m_LevelRanking.Count); i++)
                {
                    result.Add(m_LevelRanking[i]);
                }
                return result;
            }
        }

        /// <summary>Get the top N players by combat power ranking.</summary>
        public List<RankEntry> GetTopCombatPlayers(int count = 10)
        {
            lock (m_Lock)
            {
                var result = new List<RankEntry>();
                for (int i = 0; i < Math.Min(count, m_CombatRanking.Count); i++)
                {
                    result.Add(m_CombatRanking[i]);
                }
                return result;
            }
        }

        /// <summary>Get a specific player's rank by level.</summary>
        public int GetPlayerLevelRank(string charName)
        {
            lock (m_Lock)
            {
                foreach (var entry in m_LevelRanking)
                {
                    if (string.Compare(entry.CharName, charName, StringComparison.OrdinalIgnoreCase) == 0)
                        return entry.Rank;
                }
            }
            return -1;
        }

        /// <summary>Get a specific player's rank by combat power.</summary>
        public int GetPlayerCombatRank(string charName)
        {
            lock (m_Lock)
            {
                foreach (var entry in m_CombatRanking)
                {
                    if (string.Compare(entry.CharName, charName, StringComparison.OrdinalIgnoreCase) == 0)
                        return entry.Rank;
                }
            }
            return -1;
        }

        private static int GetCombatPower(TPlayObject player)
        {
            return HUtil32.LoWord(player.m_WAbil.DC) + HUtil32.HiWord(player.m_WAbil.DC)
                 + HUtil32.LoWord(player.m_WAbil.MC) + HUtil32.HiWord(player.m_WAbil.MC)
                 + HUtil32.LoWord(player.m_WAbil.SC) + HUtil32.HiWord(player.m_WAbil.SC);
        }

        /// <summary>Single ranking entry.</summary>
        public class RankEntry
        {
            public int Rank;
            public string CharName;
            public ushort Level;
            public int Value;     // Exp for level ranking, combat power for combat ranking
            public byte Job;

            public override string ToString()
            {
                return $"#{Rank} {CharName} Lv.{Level} Val={Value} Job={Job}";
            }
        }
    }
}
