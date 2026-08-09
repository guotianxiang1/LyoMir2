using System;
using System.Collections.Generic;

namespace DBSvr.Core
{
    /// <summary>
    /// Mirrors the transient hero-index byte at native record offset +0x0C.
    /// It is intentionally separate from persisted hero state.
    /// </summary>
    public sealed class NativeHeroAttachmentStateTracker
    {
        private readonly object _sync = new();
        private readonly Dictionary<int, byte> _slotPlusOne = new();

        public void MarkLoaded(int heroIndex, int requestedSlot)
        {
            if (heroIndex <= 0) return;
            var slot = unchecked((byte)requestedSlot);
            lock (_sync)
                _slotPlusOne[heroIndex] = unchecked((byte)(slot + 1));
        }

        public bool TryGetSlotPlusOne(int heroIndex, out byte slotPlusOne)
        {
            lock (_sync)
                return _slotPlusOne.TryGetValue(heroIndex, out slotPlusOne);
        }

        public void ClearForDetach(string masterName, bool specialHeroBranch,
            byte mode, IEnumerable<HeroIndexInfo> heroes)
        {
            if (string.IsNullOrEmpty(masterName) || heroes == null) return;

            var current = new Dictionary<int, HeroIndexInfo>();
            var hasSpecialHero = false;
            foreach (var hero in heroes)
            {
                if (hero == null || hero.Idx <= 0
                    || !string.Equals(hero.MasterName, masterName,
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                current[hero.Idx] = hero;
                if (!hero.IsDelete && unchecked((byte)hero.Job) == byte.MaxValue)
                    hasSpecialHero = true;
            }

            lock (_sync)
            {
                foreach (var pair in current)
                {
                    if (!_slotPlusOne.ContainsKey(pair.Key)
                        || !ShouldClear(pair.Value, specialHeroBranch,
                            mode, hasSpecialHero))
                        continue;
                    _slotPlusOne[pair.Key] = 0;
                }
            }
        }

        public void Remove(int heroIndex)
        {
            if (heroIndex <= 0) return;
            lock (_sync) _slotPlusOne.Remove(heroIndex);
        }

        private static bool ShouldClear(HeroIndexInfo hero,
            bool specialHeroBranch, byte mode, bool hasSpecialHero)
        {
            if (hero.IsDelete) return false;
            var isSpecialHero = unchecked((byte)hero.Job) == byte.MaxValue;
            if (specialHeroBranch) return isSpecialHero;
            return !isSpecialHero && mode < 3
                   && (hero.Consignation == 0 || hasSpecialHero);
        }
    }
}
