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

            // MFLG-27 FIX: Add missing LIMITPLAYERLEVEL flag
            if (HUtil32.CompareLStr(rawFlag, "LIMITPLAYERLEVEL", "LIMITPLAYERLEVEL".Length))
            {
                var value = string.Empty;
                HUtil32.ArrestStringEx(rawFlag, '(', ')', ref value);
                mapFlag.LimitPlayerLevel = unchecked((ushort)HUtil32.Str_ToInt(value, 0));
                return true;
            }

            // MFLG-27 FIX: Add missing LIMITHEROLEVEL flag
            if (HUtil32.CompareLStr(rawFlag, "LIMITHEROLEVEL", "LIMITHEROLEVEL".Length))
            {
                var value = string.Empty;
                HUtil32.ArrestStringEx(rawFlag, '(', ')', ref value);
                mapFlag.LimitHeroLevel = unchecked((ushort)HUtil32.Str_ToInt(value, 0));
                return true;
            }

            return false;
        }
    }
}
