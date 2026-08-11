using SystemModule;
using GameSvr.Services;

namespace GameSvr
{
    // ================================================================================================
    // Stall (摆摊) CM WRITE ops — gated fail-safe routing to the reversed stall executors + the injected
    // NativeStallMySqlStore / in-memory NativeStallManager, mirroring the gild write pattern.
    //
    // STATE (2026-08-02, task #83 wire layer complete): WIRED here (DORMANT until the flip — see the gate
    // below): the money/item-FREE booth-setup pair SetTimeLevel(4419)/START(4424) via NativeStallBoothSetup
    // (Δgold=Δitems=0 BY CONSTRUCTION; the affordability gate READS GoldNum, never deducts), the item-side
    // ADD(4421)/DEL(4422)/PAUSE(4425) via NativeStallItemMove (bag<->stall moves — items-out==items-in /
    // total-Dura preserved BY CONSTRUCTION; ADD's StdMode==7 split is Dura-conserving, no dup/loss), the
    // BUY(4426) finalize, and the 4418 browse READ. DORMANT because every route requires the master switch
    // NativeStallWriteGate.Enabled (SupportsStallWrites && Store) — OFF by default even though GameApp PRIMES
    // the store + manager (for DB hydration/recovery) — so every op falls back to RejectUnavailableStallRequest
    // (guards green). ENDGAME (per team-lead, user "match the original"): the FAITHFUL ops FLIP LIVE together
    // when the reviewer sets SupportsStallWrites=true.
    //
    // Wire layer (byte-exact, staging/stall_cm_wire_formats_20260802.md): all per-op field extraction lives in
    // NativeStallWireCodec (the single source of truth, shared with AuditTools/NativeStallWireIntegrationCheck).
    // Verified wire -> TProcessMessage mapping (ProcessUserMessage DEFAULT case): Recog->nParam1, Param->nParam2,
    // Tag->nParam3, Series->wParam, encoded body->Payload (decode via Misc.Decode6BitBufDirect; NEVER the lossy
    // sMsg). Owner identity on the wire = 64-bit CharID = body[0](lo)|body[4](hi), resolved through the manager's
    // by-CharID index. START's map-allows gate (MapAllowsStall = sub_7684A0 -> UserEngine position check) is the
    // one remaining flagged non-economy default (Δ=0, dormant).
    // ================================================================================================
    public partial class TPlayObject
    {
        /// <summary>
        /// Routes a stall WRITE op. Only the money/item-free booth-setup pair (SetTimeLevel/START) is wired,
        /// and only when BOTH the store and the manager are injected (prod default = dormant → reject
        /// fallback). Every other op returns false so the caller keeps <c>RejectUnavailableStallRequest</c>.
        /// </summary>
        private bool TryRouteNativeStallWrite(NativeStallOp op, TProcessMessage msg, short responseIdent)
        {
            if (!NativeStallWriteGate.Enabled)
                return false;                                   // store not injected => dormant
            var manager = NativeStallManagerHost.Manager;
            if (manager == null)
                return false;                                   // manager not injected => dormant

            switch (op)
            {
                case NativeStallOp.SetTimeLevel:
                case NativeStallOp.StartStall:
                    return TryExecuteNativeStallBoothSetup(op, msg, responseIdent, manager);
                case NativeStallOp.DelItem:
                    return TryExecuteNativeStallDel(msg, responseIdent, manager);
                case NativeStallOp.PauseStall:
                    return TryExecuteNativeStallPause(responseIdent, manager);
                case NativeStallOp.AddItem:
                    return TryExecuteNativeStallAdd(msg, responseIdent, manager);
                case NativeStallOp.BuyItem:
                    return TryExecuteNativeStallBuy(msg, responseIdent, manager);
                default:
                    return false;                               // other ops fail-closed (own leaves pending)
            }
        }

        /// <summary>
        /// Faithful booth-setup executor for SetTimeLevel(4419) + START(4424): resolve the owner + gold +
        /// tier, run the reversed ladder via <see cref="NativeStallBoothSetup"/> (which applies the record
        /// mutations), persist the header on success, and send the SM code. Moves no money and no items —
        /// the affordability gate only READS GoldNum.
        /// </summary>
        private bool TryExecuteNativeStallBoothSetup(NativeStallOp op, TProcessMessage msg,
            short responseIdent, NativeStallManager manager)
        {
            var tier = SelectBoothTier();
            var record = manager.GetOrCreate(m_sCharName, GetCachedNativeUserId());
            long gold = m_nGold;

            int code;
            if (op == NativeStallOp.SetTimeLevel)
            {
                int duration = NativeStallWireCodec.DecodeSetTimeLevelDuration(msg);
                code = NativeStallBoothSetup.EvaluateSetTimeLevel(record, tier, gold, duration);
            }
            else // StartStall
            {
                bool mapAllows = MapAllowsStall();
                bool precheckA = StartHasRemainingPaidTime(record);
                bool precheckB = record.Items.Count > 0;                    // sub_61EE88 item-list count
                code = NativeStallBoothSetup.EvaluateStart(record, mapAllows, precheckA, precheckB, tier, gold);
            }

            if (code == 1)
                PersistStallHeader(record, NativeStallWriteGate.Store);      // INSERT(first)->UPDATE(subsequent)

            if (code != NativeStallWriteTransaction.NoResponse)
                SendDefMessage(responseIdent, code, 0, 0, 0, "");
            return true;
        }

        // DEL 4422 (sub_61BECC): return one listed item to the bag by makeindex, de-list it, persist, and
        // auto-pause a running booth that is now empty (sub_61E02C). Conservation is the seam's (item moved
        // stall->bag BEFORE de-list); this wraps the side-effects. Moves an item, no money.
        private bool TryExecuteNativeStallDel(TProcessMessage msg, short responseIdent, NativeStallManager manager)
        {
            var record = manager.TryGetRecord(m_sCharName);
            if (record == null)
            {
                SendDefMessage(responseIdent, -1, 0, 0, 0, "");         // no caller stall
                return true;
            }
            int clientItemId = NativeStallWireCodec.DecodeStallItemClientId(msg);
            int code = NativeStallItemMove.TryDelItem(m_ItemList, record, clientItemId, out var removed);
            if (code == 1)
            {
                WeightChanged();
                SendAddItem(removed.Item);                             // the item is back in the bag (client)
                var store = NativeStallWriteGate.Store;
                store?.TryDeleteStallItem(removed.DbIdx, out _);       // drop the de-listed stallitem row
                if (record.Status == StallRecordStatus.Running && record.Items.Count == 0)
                    record.Status = StallRecordStatus.PausedClosed;    // auto-pause a now-empty running booth
                PersistStallHeader(record, store);                     // itemcnt (+ status) -> UpdateStall
            }
            SendDefMessage(responseIdent, code, 0, 0, 0, "");
            return true;
        }

        // PAUSE 4425 (sub_61A36C close): return ALL listed items to the bag, mark the booth paused
        // (rec[+0x40]=2), persist. Returns 1 on success / -1 (no caller stall). Moves items, no money.
        // FLAGGED (pre-flip): codec-fidelity confirms the header UpdateStall (sub_61FEAC) but is thin on the
        // stallitem-row persist; DeleteStallItem per returned row is the modeled "de-listed => row removed".
        private bool TryExecuteNativeStallPause(short responseIdent, NativeStallManager manager)
        {
            var record = manager.TryGetRecord(m_sCharName);
            if (record == null)
            {
                SendDefMessage(responseIdent, -1, 0, 0, 0, "");
                return true;
            }
            NativeStallItemMove.ReturnAllItems(m_ItemList, record, out var removed);
            var store = NativeStallWriteGate.Store;
            for (var i = 0; i < removed.Count; i++)
            {
                SendAddItem(removed[i].Item);
                store?.TryDeleteStallItem(removed[i].DbIdx, out _);
            }
            if (removed.Count > 0)
                WeightChanged();
            record.Status = StallRecordStatus.PausedClosed;            // sub_61E02C rec[+0x40]=2
            PersistStallHeader(record, store);                         // status + itemcnt -> UpdateStall
            SendDefMessage(responseIdent, 1, 0, 0, 0, "");             // close success (sub_61A36C returns 1)
            return true;
        }

        // ADD 4421 (sub_61BC7C): list a bag item onto the stall (whole, or a Dura-conserving split for a
        // StdMode==7 stackable). Conservation is the seam's (NativeStallItemMove.TryAddItem); this resolves
        // the item + guard + stackability, finalizes the split item's ids, persists (stallitem + srvData +
        // header), and sends. Moves an item into the stall; no money moves (uprice/moneytype = listing price).
        private bool TryExecuteNativeStallAdd(TProcessMessage msg, short responseIdent, NativeStallManager manager)
        {
            var record = manager.TryGetRecord(m_sCharName);
            if (record == null)
            {
                SendDefMessage(responseIdent, -2, 0, 0, 0, "");         // no caller stall
                return true;
            }
            if (!NativeStallWireCodec.TryDecodeAddItemRequest(msg, out var clientItemId, out var uprice,
                    out var moneyType, out var count))
            {
                // Malformed ADD: the decoded body is < 4 bytes for the uprice dword (spec §5.2 length guard).
                // Faithful add-failed reject; move nothing.
                SendDefMessage(responseIdent, -1, 0, 0, 0, "");
                return true;
            }
            var item = FindClientItemIn(m_ItemList, clientItemId, false);
            if (item == null)
            {
                SendDefMessage(responseIdent, -3, 0, 0, 0, "");         // item not in the bag
                return true;
            }
            if (TryGetNativePileCompatibility(item, out var gate) && gate != 0)
            {
                SendDefMessage(responseIdent, -5, 0, 0, 0, "");         // bound/locked (btValue[10..11]!=0, sub_784710)
                return true;
            }
            bool isStackable = M2Share.UserEngine.GetStdItem(item.wIndex)?.StdMode == 7;   // native stall-stackable
            int code = NativeStallItemMove.TryAddItem(m_ItemList, record, item, isStackable, count, uprice, moneyType,
                out var added, out var wasSplit);
            if (code == 1)
            {
                if (wasSplit)
                    added.Item.MakeIndex = M2Share.GetItemNumber();     // the split item needs a fresh MakeIndex
                // The split item (ClientItemID==0) gets a FRESH ClientItemID here (= native sub_73CED0's
                // player item-id counter++); the WHOLE item keeps its own (codec-fidelity 2026-08-01). This
                // fresh id becomes the stall item's key (obj+252) — the split must NOT inherit the source's.
                EnsureClientItemId(added.Item);
                WeightChanged();
                PersistAddedStallItem(record, added, uprice, moneyType, count);
                if (wasSplit)
                    SendDefMessage(Grobal2.SM_BAGITEMDURACHG, clientItemId, item.Dura, item.DuraMax, 0, "");
                else
                    SendDefMessage(Grobal2.SM_DELITEM, clientItemId, 0, 0, 1, "");   // the whole stack left the bag
                // FLAGGED (pre-flip): the stall-side "item listed" update (SM_UPT_ADD 4428) needs the sub_61DA00
                // per-item wire format (codec-fidelity is dumping it). Deferred until that byte-exact lands.
            }
            SendDefMessage(responseIdent, code, 0, 0, 0, "");
            return true;
        }

        // BUY 4426 (sub_6E7A04 -> sub_61C8E0 -> sub_61E8EC -> sub_61E0C8): the LIVE-CAPABLE type-0 (gold)
        // finalize. Resolve the seller stall by the buy-request OWNER CharID (sub_40C988/sub_49F5F4, via the
        // manager's by-CharID index) + the listed item (clientItemId = Recog), then run the reversed gate
        // ladder + the conservation-safe ALL-OR-NOTHING finalize (NativeStallBuyExecutor). On a type-0 success
        // the executor has already (a) seated the item into the buyer bag (AddItemToBag) and (b) credited the
        // seller by a MailType-4 settlement mail (MONEY ONLY — the item copy is omitted so the buyer's item is
        // never duped); this wrapper then applies the buyer gold-out, finalizes the delivered item ids, removes
        // a whole-sold booth row, persists the ledgers (best-effort), and replies. type-1 (balance/元宝) is an
        // external/async boundary -> faithful dormant reject (no in-process debit invented). DORMANT overall
        // until NativeStallWriteGate flips. Wire (byte-exact, spec §2.4426): ClientItemID = Recog (nParam1),
        // count = Series (wParam), seller CharID = decoded body[0]/body[4] — decoded by NativeStallWireCodec.
        private bool TryExecuteNativeStallBuy(TProcessMessage msg, short responseIdent, NativeStallManager manager)
        {
            if (!NativeStallWireCodec.TryDecodeBuyRequest(msg, out var ownerId, out var clientItemId,
                    out var count))
            {
                // Malformed BUY: the decoded body is < 8 bytes for the seller CharID (spec §5.2). Native reads
                // a 0/0 key -> resolves no stall -> faithful -5 (target inactive). Reject, mutate nothing.
                SendDefMessage(responseIdent, NativeStallBuyExecutor.TargetInactive, 0, 0, 0, "");
                return true;
            }

            var stall = manager.TryGetRecordById(ownerId);                                    // sub_40C988/sub_49F5F4
            bool targetActive = stall != null && stall.Status == StallRecordStatus.Running;   // sub_61C8E0 *(rec+0x40)==1
            var stallItem = FindStallItem(stall, clientItemId);                                // sub_61EE34
            bool isStackable = stallItem?.Item != null
                && M2Share.UserEngine.GetStdItem(stallItem.Item.wIndex)?.StdMode == 7;         // std +0x14 == 7

            long sellerId = stall?.OwnerId ?? ownerId;
            string sellerNm = stall?.OwnerName ?? string.Empty;
            long buyerId = GetCachedNativeUserId();

            var outcome = NativeStallBuyExecutor.Execute(
                stallItem, isStackable, count, m_nGold,
                buyEnabled: true, targetStallActive: targetActive,
                // seat the bought item FIRST (bag-full => the executor aborts, changing nothing).
                seatIntoBuyerBag: AddItemToBag,
                // credit the seller (settlement mail) as a HARD precondition: total <= m_nGold <= int.MaxValue
                // on this path, so the (int) cast is safe. A false return un-seats + aborts (no money created).
                creditSellerMoney: total => NativeMailStore.TryInsertSettlementMail(
                    buyerId, m_sCharName, sellerId, sellerNm,
                    "摆摊售出", $"您寄售的物品已售出，获得 {total} 金币。",
                    0, (int)total, out _),
                // mail-failure rollback: pull the just-seated item back out of the bag.
                unseatFromBuyerBag: item =>
                {
                    m_ItemList.Remove(item);
                    WeightChanged();
                });

            if (outcome.Succeeded)
            {
                // buyer gold-out ([BUYER+0x15C] -= total). The item is already in the bag + the seller is
                // already mailed; this in-memory field write cannot fail. Conservation: buyer -total == seller +total.
                m_nGold += (int)outcome.BuyerGoldDelta;   // BuyerGoldDelta is negative
                GoldChanged();

                // finalize the delivered item's ids: a split item is brand-new (fresh MakeIndex); every seated
                // item gets a fresh buyer-session ClientItemID.
                if (outcome.PartialSplit)
                    outcome.SeatedItem.MakeIndex = M2Share.GetItemNumber();
                ReassignClientItemId(outcome.SeatedItem);

                // drop the whole-sold row from the seller's in-memory booth (partial keeps the decremented row).
                if (outcome.WholeSold)
                    stall.Items.Remove(stallItem);

                // best-effort ledgers + stall-row persistence (fail-safe — NOT the money path).
                PersistBuyResult(stall, stallItem, outcome, buyerId, sellerId, sellerNm);

                SendAddItem(outcome.SeatedItem);
                // sub_6E7DB8 (SM 4429) — the stall-item stock refresh, pushed from INSIDE the native finalize
                // at 0x0061E44A (partial) and 0x0061E478 (whole), i.e. AFTER the ledgers, BEFORE the ack.
                SendNativeStallStockRefresh(outcome.PartialSplit ? stallItem.ItemCount : 0);
                SendDefMessage(responseIdent, NativeStallBuyExecutor.Success, 0, 0, 0, "");
                return true;
            }

            // Reject (incl. the type-1 external/async dormant boundary): reply the reversed code, mutate nothing.
            SendDefMessage(responseIdent, outcome.Code, 0, 0, 0, "");
            return true;
        }

        // sub_6E7DB8 @0x006E7DB8 (38 bytes, stall_money2_out.txt:731-743) — the post-BUY stock refresh:
        //     v3 = a2; LOWORD(a2) = 4429;
        //     (*(vtbl + 592))(v3, a2, 0, 0, 0, a3);
        // vtbl+592 = 0x250 = the simple SendDefMessage slot, so the frame is
        // SendDefMessage(recog = a2-as-passed, ident = 4429, 0, 0, 0, sMsg = a3).
        //
        // Two facts worth stating because prose elsewhere gets them wrong:
        //  * The ident is 4429 (SM_UPT_OTHER_DEL_STALLITEM) at BOTH call sites — there is NO 4428 send
        //    anywhere in the image (whole-dump scan for "4428" finds only unrelated addresses). The
        //    "4428/4429 pushes" phrasing in the manager spec §7 is wrong; 4428's constant is declared by the
        //    client and never sent by this server.
        //  * The RECIPIENT is the BUYER, not the seller and not nearby viewers. Disasm at 0x0061E447 loads
        //    `eax, [ebp+var_4]`, and var_4 is the same object used as the AddItemToBag receiver at
        //    0x0061E22D..0x0061E232 (`mov eax,[ebp+var_4]; mov edi,[eax]; call dword ptr [edi+248h]`), which
        //    is by definition the buyer. (Hex-Rays' arg naming was not trusted here — this is read off the
        //    instructions.)
        //
        // The payload is the REMAINING stock: `mov ecx,[eax+0F0h]` (stallitem itemcount) on the partial path
        // at 0x0061E43E, and a literal 0 on the whole-sold path at 0x0061E472 (`sub_6E7DB8(0, a6)`), telling
        // the client the listing is gone. Display-only: no money, no items, no state.
        private void SendNativeStallStockRefresh(int remainingCount) =>
            SendDefMessage(Grobal2.SM_UPT_OTHER_DEL_STALLITEM, remainingCount, 0, 0, 0, "");

        // Resolve a listed booth item by its stall key (item ClientItemID). null => the executor returns -4.
        private static NativeStallItem FindStallItem(NativeStallRecord stall, int clientItemId)
        {
            if (stall == null)
                return null;
            foreach (var si in stall.Items)
                if (si?.Item != null && si.Item.ClientItemID == clientItemId)
                    return si;
            return null;
        }

        // Best-effort BUY persistence (fail-safe / no rollback, matching the native store pattern — this is
        // BOOKKEEPING, not the money path: the money already moved via m_nGold + the settlement mail). The
        // buyer_order ledger is INSERTed already-settled (boDecMoney=1); buyitem_detail is the buyer receipt;
        // the stallitem row is UPDATEd (partial, decremented) or DELETEd (whole) and the stall header itemcnt
        // is refreshed on a whole sale. buyer_order is never read back by the server (§6), so its exact
        // `status` byte (§8.3) does not affect conservation.
        private void PersistBuyResult(NativeStallRecord stall, NativeStallItem stallItem,
            NativeStallBuyOutcome outcome, long buyerId, long sellerId, string sellerName)
        {
            var store = NativeStallWriteGate.Store;
            if (store == null || stall == null || stallItem == null)
                return;
            int uprice = (int)outcome.UnitPrice;
            int total = (int)outcome.Total;

            store.TryInsertBuyerOrder(buyerId, m_sCharName, sellerId, sellerName, uprice, outcome.MoneyType,
                outcome.Count, total, 1, 1, out _);                                   // audit ledger (settled)
            store.TryInsertBuyItemDetail(buyerId, m_sCharName, sellerId, sellerName, uprice, outcome.MoneyType,
                outcome.Count, out _);                                                // buyer receipt

            if (outcome.PartialSplit)
            {
                store.TryUpdateStallItem(uprice, outcome.MoneyType, stallItem.ItemCount, 0, 0, DateTime.Now,
                    stall.DbIdx, stallItem.DbIdx, out _);                             // decremented stock UPDATE
            }
            else // WholeSold
            {
                store.TryDeleteStallItem(stallItem.DbIdx, out _);                     // row removed
                PersistStallHeader(stall, store);                                     // itemcnt -> UpdateStall
            }
        }

        // Persist a newly-listed stall item: INSERT the scalar row (capturing its idx), stream the 208-byte
        // srvData (yanshen dropped, faithful), then UPDATE the stall header (itemcnt). Fail-safe.
        private static void PersistAddedStallItem(NativeStallRecord record, NativeStallItem added, int uprice,
            int moneyType, int count)
        {
            var store = NativeStallWriteGate.Store;
            if (store == null)
                return;
            if (store.TryInsertStallItem(record.DbIdx, record.OwnerId, uprice, moneyType, 0, 0, DateTime.Now,
                    count, record.OwnerName, out var itemIdx, out _))
            {
                added.DbIdx = itemIdx;
                if (NativeStallItemRecordCodec.TryEncode(added.Item, out var srvData, out _))
                    store.WriteItemSrvData(itemIdx, srvData, out _);
            }
            PersistStallHeader(record, store);                          // itemcnt -> UpdateStall
        }

        // ADD 4421 / DEL 4422 / SetTimeLevel 4419 request wire decoders now live in NativeStallWireCodec
        // (the single byte-exact source, shared with AuditTools/NativeStallWireIntegrationCheck):
        //   ADD:   ClientItemID = Recog (nParam1) / uprice = decoded body[0] (dword) / moneytype = Tag (nParam3)
        //          / count = Param (nParam2)   [TryDecodeAddItemRequest, >= 4 body bytes required]
        //   DEL:   ClientItemID = Recog (nParam1)   [DecodeStallItemClientId]
        //   4419:  duration/time-level = Param (nParam2)   [DecodeSetTimeLevelDuration]

        // The StallTradConf tier table is loaded ONCE (native loads it at stall-subsystem init sub_61D0B8),
        // then reused — not re-read per op. Lazy = thread-safe, evaluated on first live use.
        private static readonly Lazy<IReadOnlyList<NativeStallTradTier>> BoothTiers =
            new(() => NativeStallTradConf.Load(M2Share.g_Config?.sEnvirDir));

        // sub_61D730 selects the tier the affordability gate reads (no arg — a single/current tier). FLAGGED
        // (codec-fidelity pre-flip): modeled as tier[0]. Non-economy (Δ=0 — the fee is never deducted).
        private static NativeStallTradTier SelectBoothTier()
        {
            var tiers = BoothTiers.Value;
            return tiers.Count > 0 ? tiers[0] : null;
        }

        // START's map-allows-stall gate (sub_7684A0 -> UserEngine sub_696D7C(map[+0x44], x, y), the -9 rung):
        // a POSITION-AWARE engine "can a stall be placed at this map+tile?" check, NOT a single Environment
        // .Flag bool (codec-fidelity 2026-08-01). FLAGGED (pre-flip): default true until sub_696D7C's exact
        // predicate is reversed. Non-economy (Δ=0, dormant).
        private bool MapAllowsStall() => true;

        // START precheck A (sub_61F3D8, the -7 rung): the booth still has paid time left =
        // 3600 * DuraTime(hours) - elapsed-since-CreateDate > 0.
        private static bool StartHasRemainingPaidTime(NativeStallRecord record) =>
            record.DuraTime > 0 &&
            (DateTime.Now - record.CreateDate).TotalSeconds < 3600.0 * record.DuraTime;

        // Header persist (sub_61F48C): first write INSERTs (capturing LAST_INSERT_ID into DbIdx = rec+0x18),
        // subsequent writes UPDATE. Fail-safe (the native only logs on SQL failure, no rollback).
        private static void PersistStallHeader(NativeStallRecord record, INativeStallStore store)
        {
            if (store == null)
                return;
            var row = record.ToHeaderRow();
            if (record.DbIdx == 0)
            {
                if (store.TryInsertStall(row, out var newIdx, out _))
                    record.DbIdx = newIdx;
            }
            else
            {
                store.TryUpdateStall(row, record.OwnerId, record.DbIdx, out _);
            }
        }

        // ================================================================================================
        // 4418 QueryStall (sub_6E7B2C -> sub_61BA80 -> sub_61DA00): the buyer's browse READ. DORMANT until the
        // subsystem flip (returns false -> the caller keeps the existing RejectUnavailableStallRequest, behaviour
        // unchanged). Gated on NativeStallWriteGate.Enabled — the SAME master switch as the write ops — even
        // though the manager is PRIMED in production (GameApp hydrates it): the whole stall subsystem (reads +
        // writes) goes live together only when the reviewer flips SupportsStallWrites=true. When live: decode the
        // target owner CharID (body[0]/body[4]; 0 or == self => own stall), resolve the record by CharID, assign
        // each item's echo id, and send the byte-exact browse payload (88-byte header + 16*itemCount scalar
        // array + per-item blob) built by NativeStallWireCodec.
        // ================================================================================================
        private bool TryHandleNativeStallQuery(TProcessMessage msg, short responseIdent)
        {
            if (!NativeStallWriteGate.Enabled)
                return false;                                       // subsystem dormant => caller keeps -3 reject
            var manager = NativeStallManagerHost.Manager;
            if (manager == null)
                return false;                                       // manager not injected => dormant

            if (!NativeStallWireCodec.TryDecodeQueryOwner(msg, out var ownerId))
            {
                // Malformed request (decoded body < 8 bytes for the owner CharID, spec §5.2) -> status -1, no list.
                SendQueryStallStatus(responseIdent, -1, isSelf: false, running: false);
                return true;
            }

            long selfId = GetCachedNativeUserId();
            bool isSelf = ownerId == 0 || ownerId == selfId;
            var target = isSelf ? manager.TryGetRecord(m_sCharName) : manager.TryGetRecordById(ownerId);
            if (target == null)
            {
                SendQueryStallStatus(responseIdent, -1, isSelf, running: false);   // target has no stall
                return true;
            }

            // Native echoes/assigns each listed item's ClientItemID (item+0xFC): assign here so the browse
            // response and the BUY echo (Recog) agree on the key.
            foreach (var si in target.Items)
                if (si?.Item != null) EnsureClientItemId(si.Item);

            bool running = target.Status == StallRecordStatus.Running;
            int remaining = QueryRemainingSeconds(target, running);
            bool online = isSelf || IsNativeStallOwnerOnline(target.OwnerId);

            var payload = NativeStallWireCodec.BuildQueryResponse(target, remaining, online,
                it => EncodeClientItemRecord(it));

            // SM 4418 framed with the 12-byte status block: result (1 = found & serialized) in Recog; the
            // status words are best-effort (FLAGGED pre-flip: exact Param/Tag/Series packing of the status
            // block). Sent via the send-with-body variant (vtbl+0x254).
            SendSocket(Grobal2.MakeDefaultMsg(responseIdent, 1, 0, isSelf ? 0 : 1, running ? 1 : 0), payload);
            return true;
        }

        // Empty/failed browse: SM 4418 with the status result in Recog and no body.
        private void SendQueryStallStatus(short responseIdent, int result, bool isSelf, bool running)
        {
            SendSocket(Grobal2.MakeDefaultMsg(responseIdent, result, 0, isSelf ? 0 : 1, running ? 1 : 0),
                Array.Empty<byte>());
        }

        // sub_61F3D8 (browse header 0x44): seconds of paid time left = 3600 * DuraTime - elapsed-since-CreateDate;
        // 0 when the booth is not running (stall+0x40 == 0).
        private static int QueryRemainingSeconds(NativeStallRecord record, bool running)
        {
            if (!running || record.DuraTime <= 0) return 0;
            var left = 3600.0 * record.DuraTime - (DateTime.Now - record.CreateDate).TotalSeconds;
            return left > 0 ? (int)left : 0;
        }

        // sub_708C4C(ownerId) (browse header 0x50): is the stall owner online? Cosmetic flag. FLAGGED (pre-flip):
        // the exact predicate is a UserEngine online-by-CharID probe; modeled here as a scan of the live players.
        private static bool IsNativeStallOwnerOnline(long ownerId)
        {
            if (ownerId == 0) return false;
            var players = M2Share.UserEngine?.PlayObjects;
            if (players == null) return false;
            foreach (var player in players)
            {
                if (player == null || player.m_boGhost) continue;
                if (player.GetCachedNativeUserId() == ownerId) return true;
            }
            return false;
        }
    }
}
