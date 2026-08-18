using SystemModule;

namespace GameSvr
{
    public partial class TBaseObject
    {
        internal const uint NativeLimitBagItemDropWorkerAddress = 0x00748D48;
        internal const int NativeLimitBagItemDropScatterRange = 2;
        internal const int NativeLimitBagItemDropDeleteBufferBytes = 200;
        internal const int NativeLimitBagItemDropDeleteBufferCount = 50;

        internal void NativeLimitBagItemDropItems()
        {
            NativeLimitBagItemDropItems(M2Share.RandomNumber.Random);
        }

        internal void NativeLimitBagItemDropItems(Func<int, int> random)
        {
            NativeLimitBagItemDropItems(random, item =>
                DropItemDown(item, NativeLimitBagItemDropScatterRange,
                    true, null, this));
        }

        internal void NativeLimitBagItemDropItems(Func<int, int> random,
            Func<TUserItem, bool> placeItem)
        {
            ArgumentNullException.ThrowIfNull(random);
            ArgumentNullException.ThrowIfNull(placeItem);

            var deletedClientIds = new List<int>();
            // 0x748D80..0x748E0C: reverse TList scan. The stock bag invariant
            // supplies non-null item instances, matching the native dereference.
            for (var index = m_ItemList.Count - 1; index >= 0; index--)
            {
                var item = m_ItemList[index];
                var itemName = M2Share.UserEngine.GetStdItemName(item.wIndex);
                if (!m_PEnvir.NativeLimitBagItemDrops.TryGet(itemName,
                        out var rule))
                {
                    continue;
                }

                // sub_403B4C is called even for Ranger == 0; that draw returns
                // zero but still advances Delphi's process-global seed.
                if (random(rule.Ranger) >= rule.Rnd)
                    continue;

                if (!placeItem(item))
                    continue;

                deletedClientIds.Add(item.ClientItemID);
                m_ItemList.RemoveAt(index);
            }

            if (deletedClientIds.Count == 0)
                return;

            QueueNativeDeletedItems(deletedClientIds);
        }
    }
}
