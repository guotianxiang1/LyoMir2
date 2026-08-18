using SystemModule;

namespace GameSvr.Services
{
    /// <summary>
    /// Dormant equipment-loop core from sub_60B154. The caller owns item
    /// construction and the exact VMT+0x28, DL=1 initializer.
    /// </summary>
    public static class NativeFieldHeroFillEquipmentCore
    {
        public static void Fill(
            IReadOnlyList<NativeType2FieldHeroRuntimeEquipmentBinding>
                equipment,
            NativeFieldHeroEquipmentContainer target,
            byte gender,
            byte actorHair,
            Func<GoodItem, TUserItem> createItem,
            Func<TUserItem, GoodItem> resolveStandardItem,
            Func<TUserItem, GoodItem, bool> isEquipment,
            Action<TUserItem, GoodItem> initializeDl1,
            Action<uint> storePackedFeature,
            Action notifyFeatureChanged,
            Action<string> failureLogger)
        {
            if (equipment == null)
                throw new ArgumentNullException(nameof(equipment));
            if (equipment.Count !=
                NativeType2FieldHeroDefinition.EquipmentSlotCount)
            {
                throw new InvalidDataException(
                    "FieldHero Fill requires exactly 14 equipment slots.");
            }
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (createItem == null)
                throw new ArgumentNullException(nameof(createItem));
            if (resolveStandardItem == null)
                throw new ArgumentNullException(nameof(resolveStandardItem));
            if (isEquipment == null)
                throw new ArgumentNullException(nameof(isEquipment));
            if (initializeDl1 == null)
                throw new ArgumentNullException(nameof(initializeDl1));
            if (storePackedFeature == null)
                throw new ArgumentNullException(nameof(storePackedFeature));
            if (notifyFeatureChanged == null)
                throw new ArgumentNullException(nameof(notifyFeatureChanged));
            if (failureLogger == null)
                throw new ArgumentNullException(nameof(failureLogger));

            for (var slot = 0; slot < equipment.Count; slot++)
            {
                var binding = equipment[slot] ?? throw new InvalidDataException(
                    $"FieldHero equipment slot {slot} has no binding.");
                if (binding.IsEmpty) continue;

                var item = binding.Item == null
                    ? null
                    : createItem(binding.Item);
                if (item == null)
                {
                    LogFailure(binding, failureLogger);
                    continue;
                }

                var standardItem = resolveStandardItem(item);
                if (standardItem == null ||
                    !isEquipment(item, standardItem))
                {
                    LogFailure(binding, failureLogger);
                    continue;
                }

                initializeDl1(item, standardItem);
                // sub_60B154 ignores sub_6090E8's Boolean return. Slots 0..13
                // are valid and item is non-null, so this direct attach succeeds.
                target.AttachFromFillAndRefresh(slot, item,
                    binding.Definition.Scatter, gender, actorHair,
                    resolveStandardItem, storePackedFeature,
                    notifyFeatureChanged);
            }
        }

        private static void LogFailure(
            NativeType2FieldHeroRuntimeEquipmentBinding binding,
            Action<string> failureLogger)
        {
            failureLogger(
                NativeType2FieldHeroRuntimeCatalogAdapter
                    .MissingEquipmentLogPrefix
                + binding.Definition.Name
                + NativeType2FieldHeroRuntimeCatalogAdapter
                    .MissingEquipmentLogSuffix);
        }
    }
}
