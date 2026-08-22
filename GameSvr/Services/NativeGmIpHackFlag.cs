using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Native GM command 154, case 0x006256A3 -> sub_6D45C8.
    ///
    /// The core treats its first forwarded string as the IP and its second as
    /// the optional day token.  A missing IP emits the usage text; a missing
    /// day token queries matching online players.  A supplied day token
    /// changes the anti-cheat fields on every non-ghost match and writes one
    /// type-0x1D game-data record per match.
    /// </summary>
    public static class NativeGmIpHackFlag
    {
        public const string UsageMessage =
            "设置标志：@IPHackFlag <IP地址> <天数> （天数=0清除）";

        public const string QueryPlayersSuffix = " 的玩家有：";
        public const string NoPlayersSuffix = " 没有玩家";
        public const string SetNoPlayersSuffix = "\u3000下没有玩家";
        public const string SetPenaltyLogItem = "设置外挂惩罚";
        public const string ClearPenaltyLogItem = "清除外挂惩罚";
        public const string ClearSummaryPrefix = "清除";
        public const string SetSummaryPrefix = "设置";
        public const string PenaltySummaryMiddle = "\u3000下的玩家外挂惩罚 ";
        public const string ClearSummaryMiddle = "\u3000下的玩家外挂惩罚：";
        public const string DaysSuffix = " 天：";
        public const byte PenaltyLogType = 0x1D;
        public const byte PenaltyTier = 3;

        /// <summary>
        /// Matches the native sub_40BD78 AnsiString comparison: byte length
        /// comparison with ASCII a-z folded to A-Z, and no Unicode casing.
        /// </summary>
        public static bool NativeAnsiEquals(string left, string right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null)
                return false;

            var leftBytes = HUtil32.GbkEncoding.GetBytes(left);
            var rightBytes = HUtil32.GbkEncoding.GetBytes(right);
            if (leftBytes.Length != rightBytes.Length)
                return false;

            for (var index = 0; index < leftBytes.Length; index++)
            {
                if (FoldAscii(leftBytes[index]) != FoldAscii(rightBytes[index]))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Native sub_653CC4 filters only ghost==0 and compares the player's
        /// +0xB33 IP field.  ReadyRun is deliberately not a gate here.
        /// </summary>
        public static List<TPlayObject> FindMatches(
            IEnumerable<TPlayObject> players, string ip)
        {
            var matches = new List<TPlayObject>();
            if (players == null)
                return matches;

            foreach (var player in players)
            {
                if (player == null || player.m_boGhost)
                    continue;
                if (NativeAnsiEquals(player.m_sIPaddr, ip))
                    matches.Add(player);
            }
            return matches;
        }

        /// <summary>
        /// Native per-player display uses +0xAF4 (account) and +0x106
        /// (character name), followed by two spaces.
        /// </summary>
        public static string FormatPlayerEntry(string account, string character)
        {
            return (account ?? string.Empty) + "(" +
                   (character ?? string.Empty) + ")  ";
        }

        public static string BuildPlayerList(IEnumerable<TPlayObject> players)
        {
            if (players == null)
                return string.Empty;

            var result = new System.Text.StringBuilder();
            foreach (var player in players)
            {
                if (player == null)
                    continue;
                result.Append(FormatPlayerEntry(player.m_sUserID,
                    player.m_sCharName));
            }
            return result.ToString();
        }

        public static string BuildQueryMessage(string ip, string playerList)
        {
            ip ??= string.Empty;
            return string.IsNullOrEmpty(playerList)
                ? ip + NoPlayersSuffix
                : ip + QueryPlayersSuffix + playerList;
        }

        public static string BuildSetMessage(string ip, string daysText,
            int parsedDays, string playerList)
        {
            ip ??= string.Empty;
            if (string.IsNullOrEmpty(playerList))
                return ip + SetNoPlayersSuffix;

            if (parsedDays == 0)
                return ClearSummaryPrefix + ip + ClearSummaryMiddle +
                       playerList;

            return SetSummaryPrefix + ip + PenaltySummaryMiddle +
                   (daysText ?? string.Empty) + DaysSuffix + playerList;
        }

        public static int ComputeExpiryDay(int currentDay, int penaltyDays)
        {
            return unchecked(currentDay + 7 - penaltyDays);
        }

        /// <summary>
        /// Exact useful subset of sub_40CA18/sub_403DCC used by IPHackFlag.
        /// The parser accepts leading ASCII spaces, an optional sign, decimal
        /// input, and the native $, x, X, 0x, and 0X hexadecimal forms.  Any
        /// malformed or overflowed input returns the supplied default (zero
        /// for the command).
        /// </summary>
        public static int ParseDays(string text, int defaultValue = 0)
        {
            if (text == null)
                return defaultValue;

            var bytes = HUtil32.GbkEncoding.GetBytes(text);
            var length = Array.IndexOf(bytes, (byte)0);
            if (length < 0)
                length = bytes.Length;
            var index = 0;
            while (index < length && bytes[index] == 0x20)
                index++;
            if (index >= length)
                return defaultValue;

            // Native stores the minus-sign count in CH, so it wraps modulo
            // 256.  A single (modulo-256) minus selects the negation path;
            // plus signs are consumed but do not affect CH.
            byte minusCount = 0;
            while (index < length &&
                   (bytes[index] == (byte)'-' || bytes[index] == (byte)'+'))
            {
                if (bytes[index] == (byte)'-')
                    minusCount = unchecked((byte)(minusCount + 1));
                index++;
            }
            if (index >= length)
                return defaultValue;

            var radix = 10;
            var consumedLeadingZero = false;
            if (bytes[index] == (byte)'$' || bytes[index] == (byte)'x' ||
                bytes[index] == (byte)'X')
            {
                radix = 16;
                index++;
            }
            else if (bytes[index] == (byte)'0')
            {
                consumedLeadingZero = true;
                index++;
                if (index < length &&
                    (bytes[index] == (byte)'x' || bytes[index] == (byte)'X'))
                {
                    radix = 16;
                    index++;
                }
                else if (index >= length)
                {
                    // sub_403DCC accepts a lone zero (with either sign).
                    return 0;
                }
            }

            if (radix == 16)
                return ParseHex(bytes, index, length, minusCount, defaultValue);

            var value = 0;
            var sawDigit = consumedLeadingZero;
            while (index < length)
            {
                var digit = bytes[index++];
                if (digit < (byte)'0' || digit > (byte)'9')
                    return defaultValue;
                sawDigit = true;
                // Native uses unsigned `cmp value, 0x0CCCCCCC` before the
                // unchecked multiply/add.
                if ((uint)value > 0x0CCCCCCC)
                    return defaultValue;
                value = unchecked(value * 10 + digit - (byte)'0');
            }

            if (!sawDigit)
                return defaultValue;
            if (minusCount != 1)
                return value < 0 ? defaultValue : value;

            value = unchecked(-value);
            // Native accepts a negative result when it is <= 0 or has the
            // sign bit set (including the INT_MIN edge case).
            return value > 0 ? defaultValue : value;
        }

        private static int ParseHex(byte[] bytes, int index, int length,
            int minusCount, int defaultValue)
        {
            var value = 0;
            var sawDigit = false;
            while (index < length)
            {
                var digit = HexDigit(bytes[index++]);
                if (digit < 0)
                    return defaultValue;
                sawDigit = true;
                // Native uses unsigned `cmp value, 0x0FFFFFFF` before the
                // unchecked shift/add.
                if ((uint)value > 0x0FFFFFFF)
                    return defaultValue;
                value = unchecked((value << 4) + digit);
            }

            if (!sawDigit)
                return defaultValue;
            if (minusCount != 1)
                return value;

            // The native hexadecimal path negates without the decimal
            // sign-range check; preserve its unchecked result exactly.
            return unchecked(-value);
        }

        private static int HexDigit(byte value)
        {
            if (value >= (byte)'0' && value <= (byte)'9')
                return value - (byte)'0';
            if (value >= (byte)'a' && value <= (byte)'f')
                return value - (byte)'a' + 10;
            if (value >= (byte)'A' && value <= (byte)'F')
                return value - (byte)'A' + 10;
            return -1;
        }

        private static byte FoldAscii(byte value)
        {
            return value >= (byte)'a' && value <= (byte)'z'
                ? unchecked((byte)(value - ('a' - 'A')))
                : value;
        }
    }
}
