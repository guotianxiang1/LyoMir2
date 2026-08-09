using SystemModule;

namespace GameSvr.Services
{
    // ================================================================================================
    // Conservation-provable type-0 (GOLD) stall BUY finalize — the executable core of CM_BUY_STALLITEM
    // (4426). Reversed in staging/stall_buy_executor_20260801.md: dispatch sub_6E7A04 -> sub_61C8E0 ->
    // sub_61E8EC (gate) -> sub_61E0C8 (type0 finalize). This class is the PURE, auditable seam: it holds
    // NO player, NO store and NO I/O. All coin/item motion is expressed as (a) a single `total` split into
    // equal-and-opposite deltas and (b) two IO delegates the caller supplies (seat-into-bag, credit-seller),
    // so the whole transaction is CONSERVATION-SAFE and ALL-OR-NOTHING BY CONSTRUCTION and is exercised in
    // isolation by AuditTools/NativeStallBuyConservationCheck.
    //
    // Money conservation (reversed, no tax/fee anywhere — the only arithmetic is count*uprice):
    //   buyer  -total  (gold, [BUYER+0x15C], DecGold sub_6C7D64)   ==   seller  +total  (settlement mail
    //   mail+0x54, MailType 4, online-direct/offline-mailbox).  See §3/§5b/§7 of the spec.
    // Item conservation:  out-of-stall == into-buyer.  Whole stack / non-stackable => the booth item itself
    //   is seated and the stall row removed (isSold).  Stackable partial => a NEW item of exactly `count`
    //   is split off (Dura-conserving: the booth keeps the remainder) — never a dup/loss (§5a).
    //
    // ALL-OR-NOTHING ordering (the heart of the safety contract):
    //   1. gates + affordability (NO mutation).
    //   2. seat the item into the buyer bag FIRST — if the bag is full, change NOTHING (no gold, no stall,
    //      no mail).  (Native sub_61E0C8 default -5 = "could not seat".)
    //   3. credit the seller (settlement-mail INSERT) as a HARD precondition — if it fails, UN-SEAT the item
    //      and abort, committing NOTHING.  (This is stricter than the native fail-safe, which never rolls
    //      back a failed settlement-mail SQL; the stricter posture can only make the failure path safer — it
    //      never creates or destroys money — and never changes the success path.  Documented divergence.)
    //   4. commit — set the equal/opposite deltas from the single `total`, mutate the stall stock in place.
    //   The buyer gold deduction (m_nGold += BuyerGoldDelta) and the DB bookkeeping are applied by the
    //   caller AFTER a Success; neither can fail in a way that breaks conservation (gold is an in-memory
    //   field; the settlement mail — the seller's actual credit — already succeeded in step 3).
    //
    // type-1 (BALANCE/元宝, moneytype==1): the debit is carried out by an EXTERNAL server-group the native
    // request sub_711DA8 dispatches to (the spec's §4/§8 GAP — not an in-process mutation). We do NOT
    // fabricate a balance debit; we return the faithful external-boundary reject and touch nothing.
    // ================================================================================================
    public static class NativeStallBuyExecutor
    {
        public const int Success = 1;

        // Reject rungs (exact, from spec §1) — the sub_61E8EC gate ladder + the sub_61E0C8 finalize default.
        public const int Disabled = -1;            // sub_7481F4 feature gate closed
        public const int TargetInactive = -5;      // sub_61C8E0 target stall not found / not running
        public const int ItemGone = -4;            // sub_61EE34 could not resolve the stallitem
        public const int InsufficientBalance = -2; // type1 gate: [BUYER+0x760] < total
        public const int InsufficientGold = -3;    // type0 gate + the DecGold re-check: [BUYER+0x15C] < total
        public const int BadQty = -6;              // stackable stock < count, or non-stackable count != 1
        public const int SeatFailed = -5;          // sub_61E0C8 default: buyer bag full / item vanished
        // Seller settlement-mail INSERT failed -> conservation-safe abort (un-seat). Reuses the finalize -5.
        public const int SellerCreditFailed = -5;
        // type1 balance is an external/async boundary we cannot complete in-process -> faithful dormant reject.
        public const int BalanceExternalDormant = -5;

        /// <summary>
        /// Run the reversed BUY gate ladder and, for a type-0 (gold) sale that passes, perform the atomic
        /// all-or-nothing finalize. Mutates ONLY the resolved <paramref name="stallItem"/> stock (and only on
        /// the commit path); the coin motion is returned as equal/opposite deltas for the caller to apply.
        /// </summary>
        /// <param name="stallItem">The resolved target item (by clientItemId). null/no-item => -4.</param>
        /// <param name="isStackable">StdMode==7 (the caller resolves it so this seam stays pure).</param>
        /// <param name="count">Units requested (from the opcode).</param>
        /// <param name="buyerGold">Buyer gold ([BUYER+0x15C]) — read-only; the deduction is returned.</param>
        /// <param name="buyEnabled">sub_7481F4 feature gate.</param>
        /// <param name="targetStallActive">sub_61C8E0: the seller stall was found and is running.</param>
        /// <param name="seatIntoBuyerBag">AddItemToBag — returns false when the bag is full (the abort pivot).</param>
        /// <param name="creditSellerMoney">Settlement-mail INSERT for <c>total</c> — false => abort+un-seat.</param>
        /// <param name="unseatFromBuyerBag">Removes a just-seated item from the bag (the mail-failure rollback).</param>
        public static NativeStallBuyOutcome Execute(
            NativeStallItem stallItem, bool isStackable, int count, long buyerGold,
            bool buyEnabled, bool targetStallActive,
            Func<TUserItem, bool> seatIntoBuyerBag,
            Func<long, bool> creditSellerMoney,
            Action<TUserItem> unseatFromBuyerBag)
        {
            var outcome = new NativeStallBuyOutcome();

            // ---- Gate ladder (sub_61E8EC), first-fail-wins, exactly matching NativeStallWriteTransaction ----
            if (!buyEnabled) { outcome.Code = Disabled; return outcome; }
            if (!targetStallActive) { outcome.Code = TargetInactive; return outcome; }
            if (stallItem?.Item == null) { outcome.Code = ItemGone; return outcome; }

            long uprice = stallItem.UnitPrice;
            int moneyType = stallItem.MoneyType;
            // total is computed in 64-bit: OVERFLOW-SAFE. Native does a 32-bit imul with no overflow guard;
            // a wrapped-negative total there would slip the gate. Widening to long only ever REJECTS an
            // unaffordable buy (an int-gold buyer can never satisfy total > int.MaxValue) — it never lets a
            // bad deduction through, so it is strictly conservation-preserving, never a divergence in favour
            // of the player.
            long total = uprice * (long)count;
            outcome.UnitPrice = uprice;
            outcome.Count = count;
            outcome.MoneyType = moneyType;
            outcome.Total = total;

            // money gate (CHECK-ONLY here): type1 -> -2, type0 -> -3.
            if (buyerGold < total)
            {
                outcome.Code = moneyType == 1 ? InsufficientBalance : InsufficientGold;
                return outcome;
            }

            // qty gate (-6): stackable needs stock >= count (> 0); non-stackable lists/sells exactly 1.
            bool qtyValid = isStackable ? (count > 0 && stallItem.ItemCount >= count) : (count == 1);
            if (!qtyValid) { outcome.Code = BadQty; return outcome; }

            // type1 (balance): the debit is external/async (sub_711DA8) — NOT an in-process mutation. Faithful
            // dormant reject; we invent no balance debit. (§4/§8.)
            if (moneyType == 1)
            {
                outcome.IsBalanceDormant = true;
                outcome.Code = BalanceExternalDormant;
                return outcome;
            }

            // ================= type-0 GOLD finalize (sub_61E0C8) — atomic, all-or-nothing =================

            // DecGold (sub_6C7D64) re-checks affordability before the single gold mutation. Re-check here so a
            // stale gate can never over-spend.
            if (buyerGold < total) { outcome.Code = InsufficientGold; return outcome; }

            bool partial = isStackable && count < stallItem.ItemCount;
            TUserItem toSeat = partial ? SplitForBuyer(stallItem.Item, count) : stallItem.Item;

            // (1) Seat into the buyer bag FIRST. Bag full => -5 and NOTHING has changed (no split committed to
            //     the stall, no gold, no mail).
            if (seatIntoBuyerBag == null || !seatIntoBuyerBag(toSeat))
            {
                outcome.Code = SeatFailed;
                return outcome;
            }

            // (2) Credit the seller (settlement-mail INSERT) as a hard precondition. On failure, UN-SEAT the
            //     item and abort — the buyer keeps their gold, the stall keeps its stock, no money is created.
            if (creditSellerMoney == null || !creditSellerMoney(total))
            {
                unseatFromBuyerBag?.Invoke(toSeat);
                outcome.Code = SellerCreditFailed;
                return outcome;
            }

            // (3) Commit — the ONE place the equal/opposite money deltas are set (conservation by construction).
            outcome.BuyerGoldDelta = -total;   // caller applies to m_nGold ([BUYER+0x15C] -= total)
            outcome.SellerMailMoney = total;   // already credited via the settlement mail (mail+0x54)
            outcome.SeatedItem = toSeat;
            if (partial)
            {
                // Stackable partial: the booth keeps the remainder (Dura-conserving) — live stack (+0x26) and
                // stallitem.itemcount (+0xF0) both drop by count. buyer(count) + stall(orig-count) == orig.
                stallItem.Item.Dura = (ushort)(stallItem.Item.Dura - count);
                stallItem.ItemCount -= count;
                outcome.PartialSplit = true;
            }
            else
            {
                // Whole stack / non-stackable: isSold=1 (+0x101); the caller removes the row + deletes it.
                stallItem.IsSold = true;
                outcome.WholeSold = true;
            }

            outcome.Code = Success;
            return outcome;
        }

        // Buyer's item for a stackable PARTIAL buy: a NEW item of exactly `count` (native sub_7882B8 split-off).
        // Full deep copy of the booth item's attributes with fresh ids + count and a rebuilt NativeRecord, so
        // the buyer receives a complete, self-consistent item while the booth keeps the remainder. Total Dura
        // is conserved: count (buyer) + (orig - count) (stall) == orig.
        private static TUserItem SplitForBuyer(TUserItem source, int count) =>
            new TUserItem(source)
            {
                MakeIndex = 0,        // caller assigns a fresh MakeIndex (M2Share.GetItemNumber)
                ClientItemID = 0,     // caller assigns a fresh ClientItemID (EnsureClientItemId)
                Dura = (ushort)count, // exactly the bought count
                NativeRecord = null,  // rebuilt from the scalar fields (the source's 208 bytes hold the OLD count)
            };
    }

    /// <summary>Result of <see cref="NativeStallBuyExecutor.Execute"/> — the plan the wrapper applies.</summary>
    public sealed class NativeStallBuyOutcome
    {
        /// <summary>SM_BUY_STALLITEM code: 1 success, else the reversed reject rung.</summary>
        public int Code { get; set; }

        public bool Succeeded => Code == NativeStallBuyExecutor.Success;

        /// <summary>true when the reject is the type-1 external/async balance boundary (dormant, no debit).</summary>
        public bool IsBalanceDormant { get; set; }

        public long UnitPrice { get; set; }
        public int Count { get; set; }
        public int MoneyType { get; set; }

        /// <summary>count * uprice (64-bit, overflow-safe).</summary>
        public long Total { get; set; }

        /// <summary>Gold applied to the buyer: -Total on success, 0 otherwise.</summary>
        public long BuyerGoldDelta { get; set; }

        /// <summary>Money credited to the seller (via the settlement mail): +Total on success, 0 otherwise.</summary>
        public long SellerMailMoney { get; set; }

        /// <summary>The item delivered to the buyer (whole booth item, or the fresh split item).</summary>
        public TUserItem SeatedItem { get; set; }

        /// <summary>Whole stack / non-stackable sale: the stall row is flagged sold and must be removed/deleted.</summary>
        public bool WholeSold { get; set; }

        /// <summary>Stackable partial sale: the stall stock was decremented in place (row UPDATE).</summary>
        public bool PartialSplit { get; set; }
    }
}
