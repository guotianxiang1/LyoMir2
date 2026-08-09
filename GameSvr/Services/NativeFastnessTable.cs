namespace GameSvr.Services
{
    public sealed class NativeFastnessTable
    {
        private readonly NativeFastnessHqTable _table = new();

        public int Count => _table.Count;
        public int MaximumPositiveKey => _table.MaximumPositiveKey;

        public bool Load(string fileName) => _table.Load(fileName);

        public bool TryResolve(int selector, out double ratio,
            out int limit) =>
            _table.TryResolve(selector, out ratio, out limit);

        public int CalculateReduction(int damage, int selector) =>
            _table.CalculateReduction(damage, selector);

        public int ApplyReduction(int damage, int selector) =>
            _table.ApplyReduction(damage, selector);
    }
}
