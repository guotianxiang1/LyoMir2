using System.Collections.Generic;

namespace DBSvr.Core
{
    public sealed class NativeHeroSaveStateTracker
    {
        private readonly object _sync = new();
        private readonly Dictionary<int, NativeHeroSaveState> _states = new();

        public NativeHeroSaveState SnapshotForSave(int heroIndex,
            bool isDelete, int heroType, int consignation, ushort saveMode)
        {
            lock (_sync)
            {
                if (!_states.TryGetValue(heroIndex, out var state))
                    state = new NativeHeroSaveState(
                        isDelete, heroType, consignation);
                if (saveMode == 4)
                    state = state.WithConsignation(1);
                if (saveMode == 5 && state.HeroType == 2)
                    state = state.WithDelete(true);
                _states[heroIndex] = state;
                return state;
            }
        }

        public void ClearConsignation(int heroIndex)
        {
            lock (_sync)
            {
                if (_states.TryGetValue(heroIndex, out var state))
                    _states[heroIndex] = state.WithConsignation(0);
            }
        }

        public void Remove(int heroIndex)
        {
            lock (_sync) _states.Remove(heroIndex);
        }
    }

    public readonly struct NativeHeroSaveState
    {
        public NativeHeroSaveState(bool isDelete, int heroType, int consignation)
        {
            IsDelete = isDelete;
            HeroType = heroType;
            Consignation = consignation;
        }

        public bool IsDelete { get; }
        public int HeroType { get; }
        public int Consignation { get; }
        public NativeHeroSaveState WithDelete(bool value) =>
            new(value, HeroType, Consignation);
        public NativeHeroSaveState WithConsignation(int value) =>
            new(IsDelete, HeroType, value);
    }
}
