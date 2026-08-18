using SystemModule;

namespace GameSvr.Services
{
    /// <summary>
    /// Pure model of the actor+0x474 own-table arm in sub_71FA20.
    /// Production death dispatch remains fail-closed until the complete
    /// FieldHero Die and surrounding drop sequence are connected.
    /// </summary>
    public static class NativeFieldHeroOwnDropCore
    {
        public static int Consume(
            IReadOnlyList<NativeFieldHeroRuntimeDropBinding> dropItems,
            int fatigueMultiplier,
            int initialGold,
            Func<int, int> denominatorTransform,
            Func<int, int> random,
            Func<GoodItem, TUserItem> createItem,
            Action<TUserItem, GoodItem> initializeItem,
            Func<TUserItem, bool> placeItem,
            Action<TUserItem> releaseItem,
            Action<TUserItem, GoodItem> recordSuccessfulDrop)
        {
            if (dropItems == null)
                throw new ArgumentNullException(nameof(dropItems));
            if (denominatorTransform == null)
                throw new ArgumentNullException(nameof(denominatorTransform));
            if (random == null)
                throw new ArgumentNullException(nameof(random));
            if (createItem == null)
                throw new ArgumentNullException(nameof(createItem));
            if (initializeItem == null)
                throw new ArgumentNullException(nameof(initializeItem));
            if (placeItem == null)
                throw new ArgumentNullException(nameof(placeItem));
            if (releaseItem == null)
                throw new ArgumentNullException(nameof(releaseItem));
            if (recordSuccessfulDrop == null)
                throw new ArgumentNullException(nameof(recordSuccessfulDrop));

            var gold = initialGold;
            for (var index = 0; index < dropItems.Count; index++)
            {
                var binding = dropItems[index];
                if (binding == null) continue;
                var denominator = unchecked(
                    binding.MaximumPoint * fatigueMultiplier);
                denominator = denominatorTransform(denominator);
                if (random(denominator) > binding.SelectionPoint) continue;

                if (binding.IsGold)
                {
                    gold = unchecked(gold + random(binding.Count)
                        + binding.Count / 2);
                    continue;
                }

                var item = createItem(binding.Item);
                if (item == null) continue;

                initializeItem(item, binding.Item);
                if (!placeItem(item))
                {
                    releaseItem(item);
                    continue;
                }

                recordSuccessfulDrop(item, binding.Item);
            }

            return gold;
        }
    }
}
