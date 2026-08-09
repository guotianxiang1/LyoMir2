using SystemModule;

namespace GameSvr
{
    public static class NativeMapBreakLevelFlagParser
    {
        public static bool TryApply(TMapFlag mapFlag, string rawFlag)
        {
            if (string.IsNullOrEmpty(rawFlag))
            {
                return false;
            }

            if (HUtil32.CompareLStr(rawFlag, "BREAKLEVEL", "BREAKLEVEL".Length))
            {
                var value = string.Empty;
                HUtil32.ArrestStringEx(rawFlag, '(', ')', ref value);
                mapFlag.BreakLevel = unchecked((byte)HUtil32.Str_ToInt(value, 0));
                return true;
            }

            if (HUtil32.CompareLStr(rawFlag, "CRAZYBREAKLEVEL", "CRAZYBREAKLEVEL".Length))
            {
                var value = string.Empty;
                HUtil32.ArrestStringEx(rawFlag, '(', ')', ref value);
                mapFlag.CrazyBreakLevel = unchecked((ushort)HUtil32.Str_ToInt(value, 0));
                return true;
            }

            return false;
        }
    }
}
