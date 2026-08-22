using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Native @IPOutSay core (case 158, sub_6D4CA4).
    /// </summary>
    public static class NativeGmIpOutSay
    {
        public const int DefaultSeconds = 300;
        public const int UsageColorWord = 0x38FF;
        public const int ReplyColorWord = 0xFFDB;
        public const string UsageMessage =
            "使用说明：@IpOutSay + IP地址 + 时间(秒)";

        /// <summary>
        /// sub_40CA18's integer parser, with the native IPOutSay default.
        /// </summary>
        public static int ParseSeconds(string text)
        {
            return NativeGmIpHackFlag.ParseDays(text, DefaultSeconds);
        }

        /// <summary>
        /// sub_653CC4: compare the native IP bytes and skip ghost players.
        /// </summary>
        public static List<TPlayObject> FindMatches(
            IEnumerable<TPlayObject> players, string ip)
        {
            return NativeGmIpHackFlag.FindMatches(players, ip);
        }

        public static string BuildReply(string ip, int count, int seconds)
        {
            return "禁止IP：" + (ip ?? string.Empty) + " 共：" + count +
                   " 个用户聊天：" + seconds + "秒";
        }

        // Alias kept descriptive for callers that treat the final SysMsg as a
        // native receipt rather than a generic reply.
        public static string BuildMessage(string ip, int count, int seconds)
        {
            return BuildReply(ip, count, seconds);
        }
    }
}
