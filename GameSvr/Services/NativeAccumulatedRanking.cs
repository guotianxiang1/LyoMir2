using System.Text;

namespace GameSvr
{
    /// <summary>
    /// Accumulated ranking banner formatter (sub_722FC4 @0x00722FC4).
    /// Builds "开启/关闭 累积排行" header from board flags + entry count.
    /// </summary>
    public static class NativeAccumulatedRanking
    {
        public static string BuildBanner(bool enabled, bool showDetail,
            int entryCount, int pageSize)
        {
            var state = enabled ? "开启" : "关闭";
            var builder = new StringBuilder();
            builder.Append(state).Append(" 累积排行");
            if (!showDetail || entryCount <= 0)
                return builder.ToString();

            var pages = (entryCount + Math.Max(1, pageSize) - 1) /
                        Math.Max(1, pageSize);
            builder.Append(" (").Append(entryCount).Append(" 条 / ")
                .Append(pages).Append(" 页)");
            return builder.ToString();
        }
    }
}
