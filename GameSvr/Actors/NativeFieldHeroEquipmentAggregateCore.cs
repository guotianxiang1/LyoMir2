using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Dormant outer orchestration of TEquipContainer.RecalcAbilitys
    /// (sub_75EE78). The per-item, set-bonus and secondary-block bodies remain
    /// explicit callbacks; this core closes their exact order and gates.
    /// </summary>
    public static class NativeFieldHeroEquipmentAggregateCore
    {
        public const uint ResetFunction = 0x0075F4F8;
        public const uint ItemDurabilityFunction = 0x007845A0;
        public const uint ApplyItemFunction = 0x0075EE04;
        public const uint SetBonusFunction = 0x0075F548;
        public const uint SecondaryRebuildFunction = 0x00758AC0;

        public const int AggregateBlockOffset = 0x48;
        public const int TailWordOffset = 0x50;
        public const int TailDwordOffset = 0x98;
        public const int SecondaryBlockOffset = 0x1F8;
        public const int SecondaryFirstGateOffset = 0x1F9;
        public const int SecondarySecondGateOffset = 0x1FA;

        public static void Recalculate(
            NativeFieldHeroEquipmentContainer container,
            Action resetAggregate,
            Func<TUserItem, ushort> getDurability,
            Action<int, TUserItem> applyItem,
            Action applySetBonuses,
            Action rebuildSecondary,
            Func<int, byte> readByte,
            Action<int, int> addInt32,
            Action<int, ushort> addUInt16)
        {
            ArgumentNullException.ThrowIfNull(container);
            ArgumentNullException.ThrowIfNull(resetAggregate);
            ArgumentNullException.ThrowIfNull(getDurability);
            ArgumentNullException.ThrowIfNull(applyItem);
            ArgumentNullException.ThrowIfNull(applySetBonuses);
            ArgumentNullException.ThrowIfNull(rebuildSecondary);
            ArgumentNullException.ThrowIfNull(readByte);
            ArgumentNullException.ThrowIfNull(addInt32);
            ArgumentNullException.ThrowIfNull(addUInt16);

            resetAggregate();
            for (var slot = 0;
                 slot < NativeFieldHeroEquipmentContainer.SlotCount;
                 slot++)
            {
                var item = container.Get(slot);
                if (item == null || getDurability(item) == 0) continue;
                applyItem(slot, item);
            }

            applySetBonuses();
            rebuildSecondary();
            if (readByte(SecondaryFirstGateOffset) == 7)
            {
                addInt32(TailDwordOffset, 50);
            }
            if (readByte(SecondarySecondGateOffset) == 7)
            {
                addUInt16(TailWordOffset, 2);
            }
        }
    }
}
