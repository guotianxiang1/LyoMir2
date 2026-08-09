namespace GameSvr.Services
{
    public delegate bool NativeServerIntParamReader(int index, out int value);

    public static class NativeGlobalBreakSettings
    {
        public const int ProcBaseChanceIndex = 1;
        public const int MaxEquipmentBreakLevelIndex = 2;
        public const int BreakLevelIndex = 3;
        public const int CrazyBreakLevelIndex = 4;

        public const int DefaultProcBaseChance = 15;
        public const int DefaultMaxEquipmentBreakLevel = 100;
        public const int DefaultBreakLevel = 0;
        public const int DefaultCrazyBreakLevel = 0;

        private static readonly object ReloadSync = new();
        private static int _procBaseChance = DefaultProcBaseChance;
        private static int _maxEquipmentBreakLevel =
            DefaultMaxEquipmentBreakLevel;
        private static int _breakLevel = DefaultBreakLevel;
        private static int _crazyBreakLevel = DefaultCrazyBreakLevel;

        public static int ProcBaseChance =>
            Volatile.Read(ref _procBaseChance);

        public static int MaxEquipmentBreakLevel =>
            Volatile.Read(ref _maxEquipmentBreakLevel);

        public static int BreakLevel => Volatile.Read(ref _breakLevel);

        public static int CrazyBreakLevel =>
            Volatile.Read(ref _crazyBreakLevel);

        public static int GetSlot(int index) => index switch
        {
            ProcBaseChanceIndex => ProcBaseChance,
            MaxEquipmentBreakLevelIndex => MaxEquipmentBreakLevel,
            BreakLevelIndex => BreakLevel,
            CrazyBreakLevelIndex => CrazyBreakLevel,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };

        public static void SetSlot(int index, int value)
        {
            lock (ReloadSync)
            {
                SetSlotCore(index, value);
            }
        }

        public static void Reset()
        {
            lock (ReloadSync)
            {
                ResetCore();
            }
        }

        public static void ResetAndLoad(NativeServerIntParamReader reader)
        {
            lock (ReloadSync)
            {
                ResetCore();
                if (reader == null) return;

                for (var index = ProcBaseChanceIndex;
                     index <= CrazyBreakLevelIndex; index++)
                {
                    try
                    {
                        if (reader(index, out var value))
                            SetSlotCore(index, value);
                    }
                    catch (Exception)
                    {
                        // Native initialization keeps this slot's default and
                        // continues with the remaining indices.
                    }
                }
            }
        }

        private static void ResetCore()
        {
            Volatile.Write(ref _procBaseChance, DefaultProcBaseChance);
            Volatile.Write(ref _maxEquipmentBreakLevel,
                DefaultMaxEquipmentBreakLevel);
            Volatile.Write(ref _breakLevel, DefaultBreakLevel);
            Volatile.Write(ref _crazyBreakLevel, DefaultCrazyBreakLevel);
        }

        private static void SetSlotCore(int index, int value)
        {
            switch (index)
            {
                case ProcBaseChanceIndex:
                    Volatile.Write(ref _procBaseChance, value);
                    break;
                case MaxEquipmentBreakLevelIndex:
                    Volatile.Write(ref _maxEquipmentBreakLevel, value);
                    break;
                case BreakLevelIndex:
                    Volatile.Write(ref _breakLevel, value);
                    break;
                case CrazyBreakLevelIndex:
                    Volatile.Write(ref _crazyBreakLevel, value);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
    }
}
