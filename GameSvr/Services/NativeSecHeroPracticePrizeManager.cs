namespace GameSvr
{
    public sealed class NativeSecHeroPracticePrizeManager
    {
        public readonly struct Prize
        {
            public Prize(string kind, int amount, int threshold)
            {
                Kind = kind ?? string.Empty;
                Amount = amount;
                Threshold = threshold;
            }

            public string Kind { get; }
            public int Amount { get; }
            public int Threshold { get; }
        }

        private const int TierCount = 3;
        private const int RandomRange = 1000;
        private readonly IReadOnlyList<Prize>[] _pools;
        private readonly Func<int, int> _random;

        internal NativeSecHeroPracticePrizeManager(
            IReadOnlyList<Prize>[] pools, Func<int, int> random)
        {
            if (pools == null || pools.Length != TierCount + 1)
                throw new ArgumentException("副将修炼奖池必须按档位 1..3 提供。",
                    nameof(pools));
            _pools = pools;
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        internal static NativeSecHeroPracticePrizeManager CreateEmpty(
            Func<int, int> random)
        {
            return new NativeSecHeroPracticePrizeManager(
                new IReadOnlyList<Prize>[]
                {
                    null,
                    new List<Prize>(1000),
                    new List<Prize>(1000),
                    new List<Prize>(1000)
                }, random);
        }

        public bool TrySelect(int tier, out Prize prize)
        {
            prize = default;
            if ((uint)(tier - 1) >= TierCount)
                return false;

            var pool = _pools[tier];
            if (pool == null || pool.Count == 0)
                return false;

            var roll = _random(RandomRange);
            for (var i = 0; i < pool.Count; i++)
            {
                if (roll > pool[i].Threshold)
                    continue;
                prize = pool[i];
                return true;
            }
            return false;
        }
    }
}
