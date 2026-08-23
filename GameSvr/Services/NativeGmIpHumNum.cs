using System.Text;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Native GM command 160, case 0x006256B6 -> sub_6E3498.
    /// The dispatcher parses one integer threshold (default 5), then the core
    /// counts every entry in the native online-player list by its IP address.
    /// </summary>
    public static class NativeGmIpHumNum
    {
        public const int DefaultThreshold = 5;
        public const string NoMatchMessage = "没有满足的IP";

        public static int ParseThreshold(string text)
        {
            return NativeGmIpHackFlag.ParseDays(text, DefaultThreshold);
        }

        /// <summary>
        /// Preserve the native TStringList insertion/lookup contract: a key is
        /// created on its first occurrence and its object value is incremented
        /// for later occurrences. The native list is sorted before Find, so
        /// output is ordinal IP order rather than online-list order.
        /// </summary>
        public static IReadOnlyList<KeyValuePair<string, int>> CountByIp(
            IEnumerable<TPlayObject> players)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            if (players != null)
            {
                foreach (var player in players)
                {
                    var ip = player?.m_sIPaddr ?? string.Empty;
                    counts.TryGetValue(ip, out var count);
                    counts[ip] = count + 1;
                }
            }

            return counts.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToArray();
        }

        public static string BuildMessage(
            IEnumerable<KeyValuePair<string, int>> counts, int threshold)
        {
            var output = new StringBuilder();
            if (counts != null)
            {
                foreach (var pair in counts)
                {
                    if (pair.Value < threshold)
                        continue;
                    output.Append(pair.Key)
                        .Append(" : ")
                        .Append(pair.Value)
                        .Append('\r');
                }
            }

            return output.Length == 0 ? NoMatchMessage : output.ToString();
        }
    }
}
