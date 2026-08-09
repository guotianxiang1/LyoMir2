using System.Collections.Generic;

namespace GameSvr
{
    // Dormant, evidence-backed model of the native 白猪商城 / "SeeShop" (元宝 premium mall) client
    // WRITE-family handlers CM_REQSEESHOP(1046) / CM_RENEWSEESHOP(1047) / CM_DOSHOP(1048) and the
    // local goods-DELIVERY core. Hex-Rays verified against M2Server (image base 0x00400000). This is
    // the reversed ground-truth ladder used to verify the live TPlayObject.Mall / MallManager
    // re-implementation; it is NOT wired and performs no writes.
    //
    // Native command dispatch (sub_6D7D68 @0x006D7D68):
    //   case 0x416 (1046 CM_REQSEESHOP)   -> sub_63A254(*(DWORD*)req, player)     // render 812/815
    //   case 0x417 (1047 CM_RENEWSEESHOP) -> sub_63A32C(*(DWORD*)req, player)     // render 813/814
    //   case 0x418 (1048 CM_DOSHOP)       -> if(msgLen!=0) sub_6CB7E4(req[3]!=0, body) // confirm only
    //
    // SM idents (Grobal2): SM_SHOPITEMS=812 SM_RESHOPITEMS_OK=813 SM_RESHOPITEMS_FAIL=814
    //   SM_FIRSTSHOP=815 SM_DOSHOP_FAIL=816. The confirm state machine emits the INTERNAL server-side
    //   extended ident 10035 (via sub_765E68); the native outbound translator sub_6B3EAC case 10035
    //   @0x006B4B84 maps it to the on-wire logical client message SM_LOCKEQUIP=689 (param = action).
    //   This model documents 10035 because that is what the shop/confirm layer (sub_6C7D88) emits; 689
    //   is the wire form after translation. (Cross-ref: staging/ENG-Donatediam-10035-confirmation.)
    //
    // ---- 1046 REQSEESHOP  sub_63A254 @0x0063A254  (READ / render; sent-mask gated; no mutation) ----
    //   type = req[0] (ecx). If type >= 8 -> return, send nothing.
    //   sent-mask bit = (2 << type) inside player[+0x0B87]. If already set -> return, send nothing.
    //   if per-type list ptr shop[+36*type+84] != 0  -> send SM_SHOPITEMS(812), payload 180*count.
    //   if hot list ptr shop[+0x158] != 0            -> send SM_FIRSTSHOP(815), 900-byte slots.
    //   then set sent-mask bit player[+0x0B87] |= (2 << type). No currency/item mutation.
    //
    // ---- 1047 RENEWSEESHOP  sub_63A32C @0x0063A32C  (READ / render; NOT sent-mask gated) ----
    //   type = req[0] (ecx). If type >= 8 -> return, send nothing.
    //   if per-type list ptr != 0 -> send SM_RESHOPITEMS_OK(813) with payload; else SM_RESHOPITEMS_FAIL(814).
    //
    // ---- 1048 DOSHOP  sub_6CB7E4 @0x006CB7E4  (CONFIRM REQUEST ONLY; performs NO write) ----
    //   dispatch gate: message length != 0 (else handler is not called).
    //   sub_6C7D88(player, 1) @0x006C7D88 (the shared client-confirmation state machine, also used by
    //     Donatediam / ident 10035): if player[+1809] (confirm-pending flag) != 0 it sends message
    //     ident 10035 (wait/confirm dialog) and returns 0 -> DoShop shows nothing further; if the flag
    //     is 0 it returns 1 and DoShop builds+sends an NPC confirm dialog (option code -4) via
    //     sub_636BD8. Neither branch deducts currency or grants an item.
    //
    // ---- DoShop goods DELIVERY  sub_6CC420 @0x006CC420  (LOCAL grant; emits SM_DOSHOP_FAIL 816) ----
    //   Invoked post-confirmation by the targeted-by-name path sub_637FCC @0x00637FCC and the direct
    //   path sub_6D7180 @0x006D7180 (neither debits any currency; the delivery is pure grant). The
    //   result code v31 defaults to 1 (success). For each ';'-delimited "name:count" token in the
    //   goods spec string:
    //     - name == "灵符" (dword_6CC768) -> player[+0x0BD8] += count   // LOCAL lingfu balance credit
    //     - else -> build std item (sub_74DE54) and add to bag via player-vtbl(+0x248); if the bag add
    //               fails -> v31 = -5 and the grant loop aborts.
    //   Final: if v31 < 0 -> SendDefMessage(816, wParam=v31); success (v31 == 1) sends nothing.
    //   The only negative code the delivery produces is -5 (bag add failed). Delivery never DEBITS.
    //
    // ---- LOCAL vs EXTERNALLY-BLOCKED ----
    //   * 1046 / 1047 render and the sub_6CC420 delivery ladder are LOCAL and modeled here.
    //   * The confirm state machine (sub_6C7D88, ident 10035) is the write gate and modeled here.
    //   * The authoritative 元宝/金刚石 payment SETTLEMENT that funds a SeeShop purchase is the YBDB
    //     chain (YBDeal / MakeItemUseDiam / ReqItemByGoldID / QuestDiamond 1122) already reversed as
    //     externally 6108-blocked / NO-GO. This model does not settle payment; NoGoPaymentSettlement()
    //     fails closed and is the boundary marker for that chain (do NOT model / fake it here).

    public enum NativeShopWriteOp
    {
        ReqSeeShop = 1046,   // CM_REQSEESHOP   -> sub_63A254
        RenewSeeShop = 1047, // CM_RENEWSEESHOP -> sub_63A32C
        DoShop = 1048,       // CM_DOSHOP       -> sub_6CB7E4
    }

    /// <summary>SM idents a native shop handler may emit (Grobal2 native numbering).</summary>
    public enum NativeShopEmit
    {
        None = 0,
        ShopItems = 812,        // SM_SHOPITEMS
        ReShopItemsOk = 813,    // SM_RESHOPITEMS_OK
        ReShopItemsFail = 814,  // SM_RESHOPITEMS_FAIL
        FirstShop = 815,        // SM_FIRSTSHOP
        DoShopFail = 816,       // SM_DOSHOP_FAIL
        ConfirmDialog = 10035,  // internal ext ident sub_6C7D88 emits; on-wire SM_LOCKEQUIP=689 after sub_6B3EAC
    }

    /// <summary>Outcome of the CM_DOSHOP(1048) request handler sub_6CB7E4 (no write path).</summary>
    public enum NativeDoShopRequestOutcome
    {
        NoResponse = 0,       // dispatch gate: message length == 0
        ConfirmPending = 1,   // sub_6C7D88: player[+1809] != 0 -> send ident 10035, no dialog
        ShowConfirmDialog = 2 // sub_6C7D88 returned 1 -> build+send NPC confirm dialog (option -4)
    }

    /// <summary>One "name:count" token from a DoShop goods-spec string.</summary>
    public readonly struct NativeShopGoodsToken
    {
        public NativeShopGoodsToken(string name, int count, bool bagHasRoom)
        {
            Name = name;
            Count = count;
            BagHasRoom = bagHasRoom;
        }

        public string Name { get; }
        public int Count { get; }
        /// <summary>Whether the bag can accept this physical grant (ignored for the lingfu token).</summary>
        public bool BagHasRoom { get; }
    }

    // ---- READ / render contexts ----

    public sealed class NativeReqSeeShopContext
    {
        public int RequestedType { get; init; }             // req[0] (ecx)
        public bool SentMaskAlreadySet { get; init; }        // player[+0x0B87] & (2 << type)
        public bool PerTypeListPresent { get; init; }        // shop[+36*type+84] != 0 -> 812
        public bool HotListPresent { get; init; }            // shop[+0x158] != 0     -> 815
    }

    public sealed class NativeRenewSeeShopContext
    {
        public int RequestedType { get; init; }             // req[0] (ecx)
        public bool PerTypeListPresent { get; init; }        // list != 0 -> 813 else 814
    }

    public sealed class NativeDoShopRequestContext
    {
        public bool MessageHasBody { get; init; } = true;    // dispatch gate: msg length != 0
        public bool ConfirmPending { get; init; }            // sub_6C7D88: player[+1809] != 0
    }

    // ---- result structs ----

    /// <summary>What sub_63A254 (REQSEESHOP) emitted.</summary>
    public readonly struct NativeReqSeeShopResult
    {
        public NativeReqSeeShopResult(bool sentShopItems, bool sentFirstShop, bool sentMaskSet, string noOpReason)
        {
            SentShopItems = sentShopItems;
            SentFirstShop = sentFirstShop;
            SentMaskSet = sentMaskSet;
            NoOpReason = noOpReason;
        }

        public bool SentShopItems { get; }   // SM_SHOPITEMS(812)
        public bool SentFirstShop { get; }    // SM_FIRSTSHOP(815)
        public bool SentMaskSet { get; }      // player[+0x0B87] |= (2 << type) executed
        public string NoOpReason { get; }     // non-null when the handler returned without sending
        public bool IsNoOp => !SentShopItems && !SentFirstShop && !SentMaskSet;
    }

    /// <summary>What sub_6CC420 (delivery) did.</summary>
    public readonly struct NativeShopDeliveryResult
    {
        public NativeShopDeliveryResult(int resultCode, bool sentDoShopFail, int lingfuCredited, int itemsGranted)
        {
            ResultCode = resultCode;
            SentDoShopFail = sentDoShopFail;
            LingfuCredited = lingfuCredited;
            ItemsGranted = itemsGranted;
        }

        public int ResultCode { get; }        // v31: 1 success / -5 bag add failed
        public bool SentDoShopFail { get; }    // SM_DOSHOP_FAIL(816) sent iff ResultCode < 0
        public int LingfuCredited { get; }     // total credited to player[+0x0BD8]
        public int ItemsGranted { get; }       // physical bag grants completed
    }

    public static class NativeShopWriteTransaction
    {
        /// <summary>Native lingfu (灵符) goods-token name; delivered as a balance credit, not a bag item.</summary>
        public const string LingfuGoodsName = "灵符";

        /// <summary>player[+0x0BD8] local lingfu balance field (decimal 3032). Credit-only in delivery.</summary>
        public const int LingfuBalanceOffset = 0x0BD8;

        /// <summary>player[+0x0B87] per-type sent-mask byte used by REQSEESHOP.</summary>
        public const int SentMaskOffset = 0x0B87;

        /// <summary>player[+0x711] (decimal 1809) confirm-pending flag consulted by sub_6C7D88 / sub_6CC918.</summary>
        public const int ConfirmPendingOffset = 1809;

        public const int VtblSendDefMessage = 0x250; // SendDefMessage slot used for 816
        public const int DeliverySuccess = 1;
        public const int DeliveryBagFull = -5;
        public const int MaxShopType = 8;             // type must be < 8

        // ---- 1046 REQSEESHOP sub_63A254 (read/render, sent-mask gated) ----
        public static NativeReqSeeShopResult RenderReqSeeShop(NativeReqSeeShopContext c)
        {
            if (c.RequestedType < 0 || c.RequestedType >= MaxShopType)
                return new NativeReqSeeShopResult(false, false, false, "type>=8");
            if (c.SentMaskAlreadySet)
                return new NativeReqSeeShopResult(false, false, false, "sent-mask already set");

            var sentShopItems = c.PerTypeListPresent; // 812 only when the per-type list is non-empty
            var sentFirstShop = c.HotListPresent;      // 815 only when the hot list is non-empty
            // The sent-mask bit is set unconditionally once past the two gates above.
            return new NativeReqSeeShopResult(sentShopItems, sentFirstShop, true, null);
        }

        // ---- 1047 RENEWSEESHOP sub_63A32C (read/render, not sent-mask gated) ----
        public static NativeShopEmit RenderRenewSeeShop(NativeRenewSeeShopContext c)
        {
            if (c.RequestedType < 0 || c.RequestedType >= MaxShopType)
                return NativeShopEmit.None;
            return c.PerTypeListPresent ? NativeShopEmit.ReShopItemsOk : NativeShopEmit.ReShopItemsFail;
        }

        // ---- 1048 DOSHOP request sub_6CB7E4 (confirm-dialog only, no write) ----
        public static NativeDoShopRequestOutcome RequestDoShop(NativeDoShopRequestContext c)
        {
            if (!c.MessageHasBody)
                return NativeDoShopRequestOutcome.NoResponse; // dispatch gate msgLen==0
            // sub_6C7D88(player, 1): pending flag decides confirm vs dialog.
            return c.ConfirmPending
                ? NativeDoShopRequestOutcome.ConfirmPending    // sends ident 10035
                : NativeDoShopRequestOutcome.ShowConfirmDialog; // shows the NPC confirm dialog
        }

        /// <summary>
        /// sub_6C7D88 confirm gate. Returns true when the caller may proceed (pending flag clear);
        /// returns false and reports that ident 10035 would be sent when a confirm is pending.
        /// </summary>
        public static bool ConfirmGate(bool confirmPending, out NativeShopEmit emitted)
        {
            if (confirmPending)
            {
                emitted = NativeShopEmit.ConfirmDialog; // ident 10035
                return false;
            }
            emitted = NativeShopEmit.None;
            return true;
        }

        // ---- DoShop delivery sub_6CC420 (LOCAL grant; emits 816 on failure) ----
        public static NativeShopDeliveryResult DeliverGoods(IEnumerable<NativeShopGoodsToken> tokens)
        {
            var result = DeliverySuccess; // v31 = 1
            var lingfu = 0;
            var items = 0;

            if (tokens != null)
            {
                foreach (var token in tokens)
                {
                    if (result < 0)
                        break; // grant loop aborts once a bag add fails

                    var count = token.Count > 0 ? token.Count : 1; // sub_40CA18 default 1
                    if (token.Name == LingfuGoodsName)
                    {
                        lingfu += count; // player[+0x0BD8] += count
                        continue;
                    }

                    if (!token.BagHasRoom)
                    {
                        result = DeliveryBagFull; // v31 = -5
                        break;
                    }
                    items += count;
                }
            }

            var sentFail = result < 0; // SendDefMessage(816, wParam=v31) iff v31 < 0
            return new NativeShopDeliveryResult(result, sentFail, lingfu, items);
        }

        /// <summary>
        /// The SeeShop authoritative 元宝/金刚石 payment settlement is the YBDB 6108-blocked chain
        /// (YBDeal / MakeItemUseDiam / ReqItemByGoldID / QuestDiamond 1122). It is intentionally not
        /// modeled here; this fails closed so any attempt to settle payment through this model is a
        /// no-op that surfaces the blocked boundary rather than fabricating a debit.
        /// </summary>
        public static bool NoGoPaymentSettlement() => false;
    }
}
