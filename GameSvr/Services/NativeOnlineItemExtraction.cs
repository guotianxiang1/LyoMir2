using SystemModule;

namespace GameSvr.Services
{
    internal static class NativeOnlineItemExtraction
    {
        internal static bool TryExtract(TBaseObject owner, string requesterName,
            int makeIndex, out TUserItem item)
        {
            item = null;
            if (owner == null)
                return false;

            if (TryExtractEquipment(owner, requesterName, makeIndex, out item)
                || TryExtractBag(owner, requesterName, makeIndex, out item))
                return true;

            return owner is TPlayObject player
                   && TryExtractStorage(player, requesterName, makeIndex,
                       out item);
        }

        private static bool TryExtractEquipment(TBaseObject owner,
            string requesterName, int makeIndex, out TUserItem item)
        {
            item = null;
            var equipment = owner.m_UseItems;
            if (equipment == null)
                return false;

            var count = Math.Min(Grobal2.HUMAN_EQUIPPED_ITEM_COUNT,
                equipment.Length);
            for (var slot = 0; slot < count; slot++)
            {
                var candidate = equipment[slot];
                if (!Matches(candidate, makeIndex))
                    continue;

                equipment[slot] = null;
                if (AffectsFeature(slot))
                    owner.FeatureChanged();
                owner.RecalcAbilitys();
                owner.SendMsg(owner, Grobal2.RM_ABILITY, 0, 0, 0, 0,
                    string.Empty);
                WriteExtractionLog(owner, requesterName, candidate);
                NotifyEquipmentRemoved(owner, candidate);
                item = candidate;
                return true;
            }

            return false;
        }

        private static bool TryExtractBag(TBaseObject owner,
            string requesterName, int makeIndex, out TUserItem item)
        {
            item = null;
            var bag = owner.m_ItemList;
            if (bag == null)
                return false;

            for (var index = 0; index < bag.Count; index++)
            {
                var candidate = bag[index];
                if (!Matches(candidate, makeIndex))
                    continue;

                bag.RemoveAt(index);
                WriteExtractionLog(owner, requesterName, candidate);
                NotifyBagRemoved(owner, candidate);
                item = candidate;
                return true;
            }

            return false;
        }

        private static bool TryExtractStorage(TPlayObject player,
            string requesterName, int makeIndex, out TUserItem item)
        {
            item = null;
            var storage = player.m_StorageItemList;
            if (storage == null)
                return false;

            for (var index = 0; index < storage.Count; index++)
            {
                var candidate = storage[index];
                if (!Matches(candidate, makeIndex))
                    continue;

                storage.RemoveAt(index);
                WriteExtractionLog(player, requesterName, candidate);
                player.SendDelItems(candidate);
                item = candidate;
                return true;
            }

            return false;
        }

        private static bool Matches(TUserItem item, int makeIndex)
        {
            return item != null && item.MakeIndex == makeIndex;
        }

        private static bool AffectsFeature(int slot)
        {
            return slot == Grobal2.U_DRESS || slot == Grobal2.U_WEAPON
                   || slot == Grobal2.U_HELMET || slot == Grobal2.U_MASK;
        }

        private static void NotifyEquipmentRemoved(TBaseObject owner,
            TUserItem item)
        {
            if (owner is TPlayObject player)
            {
                player.SendDelItems(item);
                return;
            }

            if (owner is HeroObject hero)
                SendToBoundMaster(hero, Grobal2.SM_DELITEM, item, false);
        }

        private static void NotifyBagRemoved(TBaseObject owner,
            TUserItem item)
        {
            if (owner is TPlayObject player)
            {
                player.SendDelItems(item);
                return;
            }

            if (owner is HeroObject hero)
                SendToBoundMaster(hero, Grobal2.SM_HERO_DELITEM, item, true);
        }

        private static void SendToBoundMaster(HeroObject hero, short message,
            TUserItem item, bool requireDefinition)
        {
            if (hero.m_Master is not TPlayObject master || master.m_boGhost)
                return;
            if (requireDefinition
                && M2Share.UserEngine?.GetStdItem(item.wIndex) == null)
                return;

            master.SendDefMessage(message, master.EnsureClientItemId(item),
                0, 0, 1, string.Empty);
        }

        private static void WriteExtractionLog(TBaseObject owner,
            string requesterName, TUserItem item)
        {
            M2Share.AddGameDataLog(string.Join('\t', "8",
                owner.m_sMapName ?? string.Empty,
                owner.m_nCurrX,
                owner.m_nCurrY,
                owner.m_sCharName ?? string.Empty,
                ItmUnit.GetItemName(item),
                unchecked((uint)item.MakeIndex),
                1,
                requesterName ?? string.Empty));
        }
    }
}
