using System;
using System.Collections.Generic;
using System.Text;

namespace GameSvr.Features.Rewards
{
    /// <summary>
    /// Challenge ranking system - paginated NPC dialog builder.
    /// Native VA: sub_648148 @0x00648148
    /// Formats two-column leaderboard pages with prev/next navigation links.
    /// </summary>
    public static class ChallengeRanking
    {
        #region Configuration Constants

        // Config file paths
        private const string ConfigFileName = "Paihang.ini";
        private const string ConfigDirectory = "config";

        // Display constants (from native implementation)
        private const int DefaultPageSize = 10;
        private const int NameColumnWidth = 16;
        private const int ScoreColumnWidth = 13;

        #endregion

        #region Display Templates

        internal const string EmptyMessage =
            "暂无玩家参与挑战\\ \\";

        internal const string HeaderLine =
            " 角色名            成绩         角色名           成绩 \\";

        #endregion

        #region Core Methods

        /// <summary>
        /// Build a paginated ranking page with two-column layout.
        /// Native VA: sub_648148 @0x00648148
        /// </summary>
        /// <param name="entries">Ranked entries to display</param>
        /// <param name="pageIndex">1-based page index</param>
        /// <param name="pageSize">Entries per page (default 10)</param>
        /// <returns>Formatted dialog string with navigation links</returns>
        public static string BuildPage(IReadOnlyList<RankingEntry> entries,
            int pageIndex, int pageSize = DefaultPageSize)
        {
            if (entries == null || entries.Count == 0)
                return EmptyMessage;

            pageSize = Math.Max(1, pageSize);
            var totalPages = (entries.Count + pageSize - 1) / pageSize;
            pageIndex = Math.Clamp(pageIndex, 1, totalPages);
            var start = (pageIndex - 1) * pageSize;

            var builder = new StringBuilder();
            builder.Append(HeaderLine);

            // Build rows in two-column format
            var rowCount = Math.Min(pageSize, entries.Count - start);
            for (var row = 0; row < rowCount; row += 2)
            {
                builder.Append('\\');
                AppendColumn(builder, entries[start + row]);

                // Add second column if available
                if (row + 1 < rowCount)
                    AppendColumn(builder, entries[start + row + 1]);
            }

            // Add navigation links
            builder.Append('\\');
            if (pageIndex > 1)
            {
                builder.Append("<上一页/@ChallengeRankPage")
                    .Append(pageIndex - 1)
                    .Append(">  ");
            }
            if (pageIndex < totalPages)
            {
                builder.Append(" <下一页/@ChallengeRankPage")
                    .Append(pageIndex + 1)
                    .Append(">");
            }

            return builder.ToString();
        }

        /// <summary>
        /// Load ranking configuration from INI file.
        /// Native VA: sub_74FEB0 @0x0074FEB0 (loader for Paihang.ini)
        /// </summary>
        /// <param name="configPath">Path to config directory</param>
        /// <param name="config">Loaded configuration</param>
        /// <param name="error">Error message if failed</param>
        /// <returns>True if successful</returns>
        public static bool TryLoadConfig(string configPath,
            out RankingConfig config, out string error)
        {
            config = null;
            error = string.Empty;

            // TODO: Implement INI parsing
            // Native loads 8 prize pools from config\Paihang.ini
            // Format: [高配置] section, [奖品] pools (1-8)

            return false;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Append a single ranking entry column.
        /// </summary>
        private static void AppendColumn(StringBuilder builder, RankingEntry entry)
        {
            builder.Append(PadField(entry.PlayerName, NameColumnWidth))
                .Append(PadField(entry.Score.ToString(), ScoreColumnWidth));
        }

        /// <summary>
        /// Pad field to fixed width with spaces.
        /// </summary>
        private static string PadField(string text, int width)
        {
            text ??= string.Empty;
            if (text.Length >= width)
                return text;
            return text + new string(' ', width - text.Length);
        }

        #endregion

        #region Data Structures

        /// <summary>
        /// Represents a single ranking entry.
        /// </summary>
        public class RankingEntry
        {
            public string PlayerName { get; set; }
            public int Score { get; set; }
            public int Rank { get; set; }
        }

        /// <summary>
        /// Configuration for challenge ranking system.
        /// Native memory: UserEngine+0x2AC (8 prize pools)
        /// </summary>
        public class RankingConfig
        {
            public int PoolCount { get; set; }
            public Dictionary<int, PrizePool> PrizePools { get; set; }

            public RankingConfig()
            {
                PrizePools = new Dictionary<int, PrizePool>();
            }
        }

        /// <summary>
        /// Prize pool for threshold-based rewards.
        /// </summary>
        public class PrizePool
        {
            public int PoolId { get; set; }
            public List<PrizeThreshold> Thresholds { get; set; }

            public PrizePool()
            {
                Thresholds = new List<PrizeThreshold>();
            }
        }

        /// <summary>
        /// Single threshold with prize definition.
        /// </summary>
        public class PrizeThreshold
        {
            public int Threshold { get; set; }
            public string PrizeDescriptor { get; set; }
        }

        #endregion
    }
}
