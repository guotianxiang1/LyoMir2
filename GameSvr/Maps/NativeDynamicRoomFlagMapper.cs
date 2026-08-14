using SystemModule;

namespace GameSvr
{
    public static class NativeDynamicRoomFlagMapper
    {
        public static TMapFlag CreateMapFlag(IEnumerable<string> flags)
        {
            // MOVE-17 @0x6BBFDC: absent RUNFLAG token stores 0 on [Envir+0xB0].
            var mapFlag = new TMapFlag { boRUNFLAG = false };
            foreach (var rawFlag in flags ?? Array.Empty<string>())
            {
                Apply(mapFlag, rawFlag);
            }

            return mapFlag;
        }

        public static bool CanMap(string rawFlag)
        {
            var flag = Normalize(rawFlag);
            return flag.Equals("FIGHT", StringComparison.OrdinalIgnoreCase)
                || flag.Equals("DARK", StringComparison.OrdinalIgnoreCase)
                || flag.Equals("NORECALL", StringComparison.OrdinalIgnoreCase)
                || flag.Equals("NOPOSITIONMOVE", StringComparison.OrdinalIgnoreCase)
                || flag.Equals("NORANDOMMOVE", StringComparison.OrdinalIgnoreCase)
                || flag.StartsWith("NORECONNECT", StringComparison.OrdinalIgnoreCase);
        }

        private static void Apply(TMapFlag mapFlag, string rawFlag)
        {
            var flag = Normalize(rawFlag);
            if (flag.Length == 0) return;

            if (flag.Equals("FIGHT", StringComparison.OrdinalIgnoreCase))
            {
                mapFlag.boFightZone = true;
                return;
            }

            if (flag.Equals("DARK", StringComparison.OrdinalIgnoreCase))
            {
                mapFlag.boDarkness = true;
                return;
            }

            if (flag.Equals("NORECALL", StringComparison.OrdinalIgnoreCase))
            {
                mapFlag.boNORECALL = true;
                return;
            }

            if (flag.Equals("NOPOSITIONMOVE", StringComparison.OrdinalIgnoreCase))
            {
                mapFlag.boNOPOSITIONMOVE = true;
                return;
            }

            if (flag.Equals("NORANDOMMOVE", StringComparison.OrdinalIgnoreCase))
            {
                mapFlag.boNORANDOMMOVE = true;
                return;
            }

            if (flag.StartsWith("NORECONNECT", StringComparison.OrdinalIgnoreCase))
            {
                mapFlag.boNORECONNECT = true;
                mapFlag.sNoReConnectMap = ExtractParenthesizedValue(flag);
            }
        }

        private static string Normalize(string rawFlag)
        {
            return rawFlag?.Trim() ?? string.Empty;
        }

        private static string ExtractParenthesizedValue(string value)
        {
            var start = value.IndexOf('(');
            var end = value.LastIndexOf(')');
            return start >= 0 && end > start
                ? value.Substring(start + 1, end - start - 1)
                : string.Empty;
        }
    }
}
