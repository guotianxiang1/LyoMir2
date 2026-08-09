using System.Text;

namespace GameSvr
{
    public sealed class NativeNickPrizeManager
    {
        public const int PoolCount = 4;
        public const int EntriesPerPool = 25;
        public const int CycleSize = 1000;

        private readonly IReadOnlyList<Prize>[] _pools;
        private readonly Func<int, int> _random;
        private readonly object _syncRoot = new();
        private int _cyclePosition;
        private int _winningThreshold;

        private NativeNickPrizeManager(IReadOnlyList<Prize>[] pools,
            Func<int, int> random)
        {
            _pools = pools;
            _random = random;
            _winningThreshold = NextRandom(CycleSize) + 1;
        }

        public int CyclePosition
        {
            get
            {
                lock (_syncRoot) return _cyclePosition;
            }
        }

        public int WinningThreshold
        {
            get
            {
                lock (_syncRoot) return _winningThreshold;
            }
        }

        public IReadOnlyList<Prize> GetPool(int index)
        {
            if ((uint)index >= PoolCount)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _pools[index];
        }

        public static bool TryLoad(string fileName, out NativeNickPrizeManager manager,
            out string error)
        {
            return TryLoad(fileName, maximum => M2Share.RandomNumber.Random(maximum),
                out manager, out error);
        }

        public static bool TryLoad(string fileName, Func<int, int> random,
            out NativeNickPrizeManager manager, out string error)
        {
            manager = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                error = "SearchNormalPrizeNew.Txt path is empty";
                return false;
            }
            if (random == null)
            {
                error = "native prize random source is null";
                return false;
            }
            if (!File.Exists(fileName))
            {
                error = $"file not found: {fileName}";
                return false;
            }

            string[] lines;
            try
            {
                var gbk = Encoding.GetEncoding(936, EncoderFallback.ExceptionFallback,
                    DecoderFallback.ExceptionFallback);
                lines = File.ReadAllLines(fileName, gbk);
            }
            catch (Exception ex) when (ex is IOException ||
                                       ex is UnauthorizedAccessException ||
                                       ex is DecoderFallbackException)
            {
                error = ex.Message;
                return false;
            }

            var expectedCount = PoolCount * EntriesPerPool;
            if (lines.Length != expectedCount)
            {
                error = $"expected {expectedCount} prize rows, found {lines.Length}";
                return false;
            }

            var pools = new IReadOnlyList<Prize>[PoolCount];
            for (var poolIndex = 0; poolIndex < PoolCount; poolIndex++)
            {
                var pool = new Prize[EntriesPerPool];
                for (var entryIndex = 0; entryIndex < EntriesPerPool; entryIndex++)
                {
                    var lineIndex = poolIndex * EntriesPerPool + entryIndex;
                    if (!TryParsePrize(lines[lineIndex], out var prize, out error))
                    {
                        error = $"row {lineIndex + 1}: {error}";
                        return false;
                    }
                    pool[entryIndex] = prize;
                }
                pools[poolIndex] = pool;
            }

            try
            {
                manager = new NativeNickPrizeManager(pools, random);
                return true;
            }
            catch (InvalidOperationException ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public bool TrySelect(int useType, out Prize prize, out bool specialPool)
        {
            prize = null;
            specialPool = false;
            var cost = useType switch
            {
                1 => 1,
                2 => 10,
                3 => 100,
                _ => 0
            };
            if (cost == 0) return false;

            lock (_syncRoot)
            {
                specialPool = AdvanceCycle(cost);
                var poolIndex = specialPool ? 0 : useType;
                var pool = _pools[poolIndex];
                prize = pool[NextRandom(pool.Count)];
                return true;
            }
        }

        private bool AdvanceCycle(int amount)
        {
            var previous = _cyclePosition;
            _cyclePosition = unchecked(_cyclePosition + amount);
            var crossed = previous < _winningThreshold &&
                          _cyclePosition >= _winningThreshold;
            if (_cyclePosition > CycleSize)
            {
                _cyclePosition = 0;
                _winningThreshold = NextRandom(CycleSize) + 1;
            }
            return crossed;
        }

        private int NextRandom(int maximum)
        {
            var value = _random(maximum);
            if ((uint)value >= (uint)maximum)
                throw new InvalidOperationException(
                    $"native prize random returned {value} outside 0..{maximum - 1}");
            return value;
        }

        private static bool TryParsePrize(string line, out Prize prize, out string error)
        {
            prize = null;
            error = string.Empty;
            if (string.IsNullOrEmpty(line))
            {
                error = "empty prize row";
                return false;
            }

            var separator = line.LastIndexOf(':');
            if (separator <= 0 || separator == line.Length - 1)
            {
                error = "expected item-name:count";
                return false;
            }

            var itemName = line.Substring(0, separator).Trim();
            var countText = line.Substring(separator + 1).Trim();
            if (itemName.Length == 0 || !int.TryParse(countText, out var count) || count <= 0)
            {
                error = "invalid item name or count";
                return false;
            }

            prize = new Prize(itemName, count);
            return true;
        }

        public sealed class Prize
        {
            internal Prize(string itemName, int count)
            {
                ItemName = itemName;
                Count = count;
            }

            public string ItemName { get; }
            public int Count { get; }
            public string Source => ItemName + ":" + Count;
        }
    }
}
