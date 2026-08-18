using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Actor-owned core for the TEquipContainer stored at TFieldHero+0x63C.
    /// Aggregate storage and its deep per-item/set bodies, item construction,
    /// and persistence cloning remain outside this dormant slice.
    /// </summary>
    public sealed class NativeFieldHeroEquipmentContainer
    {
        public const int SlotCount = 16;

        private readonly TUserItem[] _items = new TUserItem[SlotCount];
        private readonly int[] _scatter = new int[SlotCount];

        internal NativeFieldHeroEquipmentContainer(TFieldHero owner)
        {
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        public TFieldHero Owner { get; }

        public TUserItem Get(int slot) =>
            IsValidSlot(slot) ? _items[slot] : null;

        public bool Attach(int slot, TUserItem item)
        {
            if (!IsValidSlot(slot) || item == null) return false;
            _items[slot] = item;
            return true;
        }

        public TUserItem Detach(int slot)
        {
            if (!IsValidSlot(slot)) return null;
            var item = _items[slot];
            _items[slot] = null;
            return item;
        }

        /// <summary>
        /// Models the valid-slot portion of sub_6090E8: the separate actor
        /// scatter cell is written after the attach attempt, even if the item
        /// pointer is null. Native Fill callers only provide slots 0..13.
        /// </summary>
        public bool AttachFromFill(int slot, TUserItem item, int scatter)
        {
            if (!IsValidSlot(slot)) return false;
            var attached = Attach(slot, item);
            _scatter[slot] = scatter;
            return attached;
        }

        /// <summary>
        /// Complete valid-slot side-effect order of sub_6090E8: attach, write
        /// scatter, rebuild actor feature with both stack arguments zero, store
        /// it, then dispatch the actor VMT+0x68 notification.
        /// </summary>
        public bool AttachFromFillAndRefresh(int slot, TUserItem item,
            int scatter, byte gender, byte actorHair,
            Func<TUserItem, GoodItem> resolveStandardItem,
            Action<uint> storePackedFeature,
            Action notifyFeatureChanged)
        {
            if (!IsValidSlot(slot)) return false;
            if (resolveStandardItem == null)
                throw new ArgumentNullException(nameof(resolveStandardItem));
            if (storePackedFeature == null)
                throw new ArgumentNullException(nameof(storePackedFeature));
            if (notifyFeatureChanged == null)
                throw new ArgumentNullException(nameof(notifyFeatureChanged));

            var attached = Attach(slot, item);
            _scatter[slot] = scatter;
            var packed = BuildPackedFeature(gender, actorHair, 0,
                resolveStandardItem);
            storePackedFeature(packed);
            notifyFeatureChanged();
            return attached;
        }

        public int GetScatter(int slot)
        {
            if (!IsValidSlot(slot))
                throw new ArgumentOutOfRangeException(nameof(slot));
            return _scatter[slot];
        }

        /// <summary>
        /// Exact low-byte projection of sub_75F374. The resolver supplies the
        /// GoodItem pointer held by each attached TUserItem.
        /// </summary>
        public uint BuildPackedFeature(byte gender, byte actorHair,
            byte lowByte, Func<TUserItem, GoodItem> resolveStandardItem)
        {
            if (resolveStandardItem == null)
                throw new ArgumentNullException(nameof(resolveStandardItem));

            var dress = ResolveAttached(0, resolveStandardItem);
            var weapon = ResolveAttached(1, resolveStandardItem);

            GoodItem head = null;
            if (_items[13] != null)
                head = ResolveAttached(13, resolveStandardItem);
            else if (_items[4] != null)
                head = ResolveAttached(4, resolveStandardItem);

            var headCode = head == null
                ? (byte)0
                : unchecked((byte)head.Outlook);
            if (headCode == 0) headCode = actorHair;

            var dressByte = AppearanceByte(dress?.Shape ?? 0, gender);
            var weaponByte = AppearanceByte(weapon?.Shape ?? 0, gender);
            var headByte = AppearanceByte(headCode, gender);
            return lowByte
                   | ((uint)weaponByte << 8)
                   | ((uint)headByte << 16)
                   | ((uint)dressByte << 24);
        }

        private GoodItem ResolveAttached(int slot,
            Func<TUserItem, GoodItem> resolveStandardItem)
        {
            var item = _items[slot];
            if (item == null) return null;
            return resolveStandardItem(item) ?? throw new InvalidDataException(
                $"FieldHero equipment slot {slot} has no GoodItem binding.");
        }

        private static byte AppearanceByte(byte value, byte gender) =>
            unchecked((byte)(value * 2 + gender));

        private static bool IsValidSlot(int slot) =>
            unchecked((uint)slot) < SlotCount;
    }
}
