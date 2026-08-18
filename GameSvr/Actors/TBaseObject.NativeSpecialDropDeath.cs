using SystemModule;

namespace GameSvr
{
    public partial class TBaseObject
    {
        internal const uint NativeSpecialDropDeathWorkerAddress = 0x00740300;
        internal const int NativeSpecialDropDeathScatterRange = 2;
        internal const int NativeSpecialDropDeleteMessage = 0x27A4;

        /// <summary>
        /// Replays <c>sub_740300</c>, the exclusive <c>ONLYDROPSPEC</c>
        /// player/hero death worker.
        /// </summary>
        internal void NativeSpecialDropBagItems()
        {
            NativeSpecialDropBagItems(M2Share.RandomNumber.Random);
        }

        internal void NativeSpecialDropBagItems(Func<int, int> random)
        {
            ArgumentNullException.ThrowIfNull(random);

            var deletedClientIds = new List<int>();
            var isPlayerRace = m_btRaceServer == Grobal2.RC_PLAYOBJECT;

            // 0x74033E..0x7404AC scans [self+0x508] from Count-1 to zero.
            for (var index = m_ItemList.Count - 1; index >= 0; index--)
            {
                var item = m_ItemList[index];
                if (item == null || item.wIndex == 0)
                    continue;

                var stdItem = M2Share.UserEngine?.GetStdItem(item.wIndex);
                // 0x74036A is Delphi `is TSpecialDropItem`. In this image the
                // class factory has one exact route: decimal StdMode 96.
                if (stdItem == null
                    || stdItem.StdMode != NativeSpecialDropItemRollCore.SpecialDropStdMode)
                {
                    continue;
                }

                // 0x74037F calls sub_78BCBC only after the class test.
                if (!NativeSpecialDropItemRollCore.IsSelected(item, random))
                    continue;

                // 0x74038C: race 54 (hero) jumps directly to ground placement.
                // For a player, the first order-4 query chooses destroy vs ground.
                var authenticated = !isPlayerRace
                    || NativeItemDropDestroyAuthenticated();
                if (NativeItemDropDestroy.ShouldDestroy(isPlayerRace,
                        authenticated, item))
                {
                    // 0x7403BA..0x7403CE: a mode-5 rejection retains the item.
                    if (NativeItemDropDestroy.CheckTransferPermission(item,
                            stdItem,
                            NativeItemDropDestroy.TransferModeDrop) != 0)
                    {
                        continue;
                    }

                    deletedClientIds.Add(item.ClientItemID);
                    m_ItemList.RemoveAt(index);                 // 0x7403E9

                    // Native deliberately performs a fresh authentication query
                    // for the log reason. A changed result is therefore observable.
                    var logAuthenticated = NativeItemDropDestroyAuthenticated();
                    var reason = NativeItemDropDestroy.BuildDestroyNotice(
                        logAuthenticated, item,
                        NativeItemDropDestroy.DeathBagUnverifiedNotice,
                        NativeItemDropDestroy.DeathBagGiftNotice) ?? string.Empty;
                    var quantity = stdItem.StdMode == 7 ? item.Dura : 1;
                    M2Share.AddNativeGameDataLog(this,
                        NativeItemDropDestroy.DestroyNoticeKind,
                        ItmUnit.GetItemName(item), item.MakeIndex, quantity,
                        reason);
                    Dispose(item);                              // 0x74046A
                    continue;
                }

                // 0x740471..0x7404A5: fixed radius 2; failed placement retains
                // the original bag entry and produces no deletion notification.
                if (!DropItemDown(item, NativeSpecialDropDeathScatterRange,
                        true, null, this))
                {
                    continue;
                }

                deletedClientIds.Add(item.ClientItemID);
                m_ItemList.RemoveAt(index);                     // 0x7404A0
            }

            if (deletedClientIds.Count == 0)
                return;

            // 0x7404B2..0x7404DA emits one 0x27A4 batch in the same order in
            // which selected items were removed. Hero dispatch uses count*4 raw
            // ClientItemID bytes. Both player and hero dispatchers consume the
            // same native count*4 RM body; only the outgoing SM ident differs.
            QueueNativeDeletedItems(deletedClientIds);
        }
    }
}
