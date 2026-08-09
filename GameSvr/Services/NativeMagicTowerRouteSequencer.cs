namespace GameSvr.Services
{
    public readonly struct NativeMagicTowerRouteEntry
    {
        public NativeMagicTowerRouteEntry(int sequence, byte specialRoute)
        {
            Sequence = sequence;
            SpecialRoute = specialRoute;
        }

        public int Sequence { get; }
        public byte SpecialRoute { get; }
    }

    public readonly struct NativeMagicTowerRouteSnapshot
    {
        public NativeMagicTowerRouteSnapshot(int totalEntries, int sequence,
            int threshold, int paidEntries, int freeEntries)
        {
            TotalEntries = totalEntries;
            Sequence = sequence;
            Threshold = threshold;
            PaidEntries = paidEntries;
            FreeEntries = freeEntries;
        }

        public int TotalEntries { get; }
        public int Sequence { get; }
        public int Threshold { get; }
        public int PaidEntries { get; }
        public int FreeEntries { get; }
    }

    public sealed class NativeMagicTowerRouteSequencer
    {
        private readonly object _syncRoot = new();
        private readonly Func<int, int> _random;
        private int _totalEntries;
        private int _sequence;
        private int _threshold;
        private int _paidEntries;
        private int _freeEntries;

        public NativeMagicTowerRouteSequencer(Func<int, int> random)
            : this(random, 0, 0, 0, 0, 0)
        {
        }

        public NativeMagicTowerRouteSequencer(Func<int, int> random,
            int totalEntries, int sequence, int threshold, int paidEntries,
            int freeEntries)
        {
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _totalEntries = totalEntries;
            _sequence = sequence;
            _threshold = threshold;
            _paidEntries = paidEntries;
            _freeEntries = freeEntries;
        }

        public NativeMagicTowerRouteEntry Enter(bool freeEntry)
        {
            lock (_syncRoot)
            {
                _totalEntries = unchecked(_totalEntries + 1);
                _sequence = unchecked(_sequence + 1);
                if (_sequence > 10_000)
                    _sequence = 1;

                if (freeEntry)
                    _freeEntries = unchecked(_freeEntries + 1);
                else
                    _paidEntries = unchecked(_paidEntries + 1);

                return ResolveCurrentRoute();
            }
        }

        public NativeMagicTowerRouteEntry ResolveCurrent()
        {
            lock (_syncRoot)
            {
                return ResolveCurrentRoute();
            }
        }

        private NativeMagicTowerRouteEntry ResolveCurrentRoute()
        {
            byte specialRoute = 0;
            if (_sequence == _threshold)
            {
                specialRoute = 1;
                var nextGap = unchecked((ushort)(_random(200) + 200));
                _threshold = unchecked(_threshold + nextGap);
                if (_threshold >= 10_000)
                    _threshold = nextGap;
            }

            specialRoute = _sequence switch
            {
                2_500 => 2,
                5_000 => 3,
                7_500 => 4,
                10_000 => 5,
                _ => specialRoute
            };
            return new NativeMagicTowerRouteEntry(_sequence, specialRoute);
        }

        public NativeMagicTowerRouteSnapshot Snapshot()
        {
            lock (_syncRoot)
            {
                return new NativeMagicTowerRouteSnapshot(_totalEntries,
                    _sequence, _threshold, _paidEntries, _freeEntries);
            }
        }
    }
}
