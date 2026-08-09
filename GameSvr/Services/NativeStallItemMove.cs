using System.Collections.Generic;
using SystemModule;

namespace GameSvr.Services
{
    // ================================================================================================
    // Conservation-provable stall item-side MOVES — DEL(4422) + PAUSE(4425) whole-item returns + ADD(4421)
    // list/split. Conservation BY CONSTRUCTION: every item removed from one list is added to the other in the
    // SAME call. DEL/PAUSE: add-to-bag precedes de-list, and native's return-to-bag (sub_73CEA8) is unbounded
    // (no bag-full check) so it always returns the item. ADD: whole move, or a Dura-conserving StdMode==7
    // split (source keeps Dura-count, a new item of exactly count is listed) — total Dura preserved, no
    // dup/loss. Native: DEL sub_61BECC, PAUSE sub_61A36C, ADD sub_61BC7C.
    //
    // This is PURE (operates on the two lists + the item, no player/store/I/O) so the conservation is
    // auditable in isolation. The TPlayObject wrapper resolves m_ItemList + the record, then does the
    // side-effects (id-assign, WeightChanged, client sends, stallitem persist, auto-pause). DORMANT until flip.
    // ================================================================================================
    public static class NativeStallItemMove
    {
        /// <summary>
        /// DEL 4422 (sub_61BECC): return one listed item to the bag by ClientItemID (item+0x18 — the stall's
        /// stored key, set from srcItem+0x18 at ADD time; the same key ADD/BUY/CM_1017 use, NOT the record
        /// MakeIndex). Returns 1 on success / -1 (no record or item not found). Native sub_73CEA8 has NO
        /// bag-full check — the bag TList is unbounded and the return ALWAYS succeeds (codec-fidelity), so
        /// there is no bag-full -1; add-to-bag still precedes de-list (conservation: moved exactly once).
        /// </summary>
        public static int TryDelItem(IList<TUserItem> bag, NativeStallRecord stall, int clientItemId,
            out NativeStallItem removed)
        {
            removed = null;
            if (bag == null || stall == null)
                return -1;
            NativeStallItem found = null;
            foreach (var si in stall.Items)
            {
                if (si?.Item != null && si.Item.ClientItemID == clientItemId)   // stall key = item+0x18 (sub_61DF24)
                {
                    found = si;
                    break;
                }
            }
            if (found == null)
                return -1;                       // item not listed (the only -1 — native return-to-bag is unbounded)
            bag.Add(found.Item);                 // return to bag (unconditional, matches native sub_73CEA8) FIRST
            stall.Items.Remove(found);           // then de-list — moved exactly once, never duped/lost
            removed = found;                      // hand the de-listed row to the caller (persist + send)
            return 1;
        }

        /// <summary>
        /// PAUSE-close 4425 (sub_61A36C): return EVERY listed item to the bag — native's return-to-bag
        /// (sub_73CEA8) is unbounded, no bag-full check. Returns the count returned; the caller sets the booth
        /// status to paused + persists.
        /// </summary>
        public static int ReturnAllItems(IList<TUserItem> bag, NativeStallRecord stall,
            out List<NativeStallItem> removed)
        {
            removed = new List<NativeStallItem>();
            if (bag == null || stall == null)
                return 0;
            for (var i = stall.Items.Count - 1; i >= 0; i--)
            {
                var si = stall.Items[i];
                if (si?.Item == null)
                {
                    stall.Items.RemoveAt(i);     // drop a malformed empty slot
                    continue;
                }
                bag.Add(si.Item);                // return to bag (unconditional, matches native sub_73CEA8)
                stall.Items.RemoveAt(i);
                removed.Add(si);                 // hand the de-listed rows to the caller (persist + send)
            }
            return removed.Count;
        }

        /// <summary>
        /// ADD 4421 (sub_61BC7C → sub_61DCF0 construct+insert): list a bag item onto the stall. Given the
        /// resolved bag item + its stackability (StdMode==7 — resolved by the caller so the seam stays pure),
        /// this lists the WHOLE item, or (stackable, count &lt; Dura) SPLITS a Dura-conserving portion: the bag
        /// item keeps Dura-count and a NEW item of exactly count is listed, so TOTAL Dura is preserved (no
        /// dup/loss). Returns 1 / -4 (count mismatch). The caller does find(-3)/guard(-5)/persist/id-assign;
        /// there is NO price gate (native always inserts). On a split the new item's MakeIndex is left 0 for
        /// the caller to assign (M2Share.GetItemNumber) + its ClientItemID via EnsureClientItemId.
        /// </summary>
        public static int TryAddItem(IList<TUserItem> bag, NativeStallRecord stall, TUserItem item, bool isStackable,
            int count, int uprice, int moneyType, out NativeStallItem added, out bool wasSplit)
        {
            added = null;
            wasSplit = false;
            if (bag == null || stall == null || item == null)
                return -4;

            TUserItem listed;
            if (isStackable)
            {
                if (count == item.Dura)
                {
                    bag.Remove(item);                            // list the whole stack
                    listed = item;
                }
                else if (count > 0 && count < item.Dura)
                {
                    item.Dura = (ushort)(item.Dura - count);     // seller keeps the remainder (stays in the bag)
                    listed = new TUserItem
                    {
                        MakeIndex = 0,                           // caller assigns (M2Share.GetItemNumber)
                        wIndex = item.wIndex,
                        Dura = (ushort)count,                    // exactly the split-off count
                        DuraMax = item.DuraMax,
                    };
                    listed.btValue[10] = item.btValue[10];       // carry the pile-compat/bind word (btValue[10..11])
                    listed.btValue[11] = item.btValue[11];
                    wasSplit = true;
                }
                else
                {
                    return -4;                                   // count <= 0 or > available
                }
            }
            else
            {
                if (count != 1)
                    return -4;                                   // a non-stackable item lists exactly 1
                bag.Remove(item);
                listed = item;
            }

            added = new NativeStallItem
            {
                Item = listed,
                UnitPrice = uprice,
                MoneyType = moneyType,
                ItemCount = count,
                IsSold = false,
                IsGetMoney = false,
                IsBoSended = false,
            };
            stall.Items.Add(added);
            return 1;
        }
    }
}
