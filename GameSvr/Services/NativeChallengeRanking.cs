using System.Text;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Challenge ranking paginated NPC dialog (sub_648148 @0x00648148).
    /// Formats two-column leaderboard pages with prev/next links.
    /// </summary>
    public static class NativeChallengeRanking
    {
        internal const string EmptyMessage =
            "暂无玩家参与挑战\\ \\";

        internal const string HeaderLine =
            " 角色名            成绩         角色名           成绩 \\";

        public static string BuildPage(IReadOnlyList<NativeGloryRankBoard.Entry> entries,
            int pageIndex, int pageSize = 10)
        {
            if (entries == null || entries.Count == 0)
                return EmptyMessage;

            pageSize = Math.Max(1, pageSize);
            var totalPages = (entries.Count + pageSize - 1) / pageSize;
            pageIndex = Math.Clamp(pageIndex, 1, totalPages);
            var start = (pageIndex - 1) * pageSize;

            var builder = new StringBuilder();
            builder.Append(HeaderLine);
            var rowCount = Math.Min(pageSize, entries.Count - start);
            for (var row = 0; row < rowCount; row += 2)
            {
                builder.Append('\\');
                AppendColumn(builder, entries[start + row]);
                if (row + 1 < rowCount)
                    AppendColumn(builder, entries[start + row + 1]);
            }

            builder.Append('\\');
            if (pageIndex > 1)
                builder.Append("<上一页/@ChallengeRankPage")
                    .Append(pageIndex - 1).Append(">  ");
            if (pageIndex < totalPages)
                builder.Append(" <下一页/@ChallengeRankPage")
                    .Append(pageIndex + 1).Append(">");
            return builder.ToString();
        }

        private static void AppendColumn(StringBuilder builder,
            NativeGloryRankBoard.Entry entry)
        {
            builder.Append(PadField(entry.Name, 16))
                .Append(PadField(entry.Score.ToString(), 13));
        }

        private static string PadField(string text, int width)
        {
            text ??= string.Empty;
            if (text.Length >= width)
                return text;
            return text + new string(' ', width - text.Length);
        }
    }
}
