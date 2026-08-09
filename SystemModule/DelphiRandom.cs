using System.Runtime.InteropServices;

namespace SystemModule
{
    /// <summary>
    /// Dormant process-global owner for Delphi's System.Random sequence.
    /// Runtime callers must not use this owner until gameplay is restored to
    /// the original single execution thread and its call order is closed.
    /// </summary>
    public static class DelphiRandom
    {
        private const string Kernel32 = "kernel32.dll";
        public const uint Multiplier = 0x08088405u;
        private const double UInt32Unit = 1.0 / 4294967296.0;
        private static readonly object SyncRoot = new object();
        private static uint _seed;

        public static uint Seed
        {
            get
            {
                lock (SyncRoot)
                {
                    return _seed;
                }
            }
            set
            {
                lock (SyncRoot)
                {
                    _seed = value;
                }
            }
        }

        public static void Randomize()
        {
            lock (SyncRoot)
            {
                if (QueryPerformanceCounter(out long counter))
                    _seed = SelectRandomizeSeed(true, counter, 0u);
                else
                    _seed = SelectRandomizeSeed(false, 0L, GetTickCount());
            }
        }

        public static int Random(int range)
        {
            lock (SyncRoot)
            {
                var nextSeed = Advance();
                var product = unchecked((ulong)(uint)range * nextSeed);
                return unchecked((int)(uint)(product >> 32));
            }
        }

        public static double NextDouble()
        {
            lock (SyncRoot)
            {
                return Advance() * UInt32Unit;
            }
        }

        private static uint SelectRandomizeSeed(bool counterAvailable,
            long counter, uint tickCount)
        {
            return counterAvailable ? unchecked((uint)counter) : tickCount;
        }

        private static uint Advance()
        {
            _seed = unchecked(_seed * Multiplier + 1u);
            return _seed;
        }

        [DllImport(Kernel32, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryPerformanceCounter(out long counter);

        [DllImport(Kernel32, ExactSpelling = true)]
        private static extern uint GetTickCount();
    }
}
