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
                case NativeStallOp.SetName:
                    return TryExecuteNativeStallSetName(msg, responseIdent, manager);
                case NativeStallOp.MessageStall:
                    return TryExecuteNativeStallMessage(msg, responseIdent, manager);
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
            // 眼神 关闭摆摊 memcpys a single C3 over sub_6E7C38's prologue (0x100AD0EE payload,
            // 0x100AD113 push 0x6E7C38 / len 1 -> 0x10033340), so START returns before the map
            // gate, before the ladder and before any reply. Bail ahead of GetOrCreate so no
            // record is created either: Δgold = Δitems = Δrecords = 0.
            if (op == NativeStallOp.StartStall
                && new Plugins.YanshenApi(this, null, M2Share.PluginManager).IsStallClosed())
                return true;

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

        // SetName 4420 (sub_6E7984 -> sub_61D3E0 @0x0061D3E0). Native body (out_stallmgr.txt:392-456), in
        // order: sub_40C988(self[+0x588],[+0x58C]) + sub_49F5F4 resolve; when NOT found it CREATES the record
        // (sub_61ED04 + sub_49F128 insert) — so SetName is a get-or-create op, exactly like SetTimeLevel, and
        // the "no record" rung is unreachable in practice. Then, first-fail-wins:
        //   rec[+0x40]==1 (running)        -> -3   (a live booth cannot be renamed)
        //   sub_4057D0(name) > 30          -> -1   (length is in BYTES: Delphi StrLen on the GBK AnsiString)
        //   name empty (v16 == 0)          -> -2
        //   else sub_62159C(commit name) + sub_61F48C(persist) -> 1
        // Wire: the name is the WHOLE decoded body as a GBK C-string (spec §2.4420) — NOT the lossy sMsg.
        // Moves no money and no items: the only mutation is the record's StallName + the header persist.
        private bool TryExecuteNativeStallSetName(TProcessMessage msg, short responseIdent,
            NativeStallManager manager)
        {
            // sub_61D3E0 resolves-or-CREATES (sub_61ED04) before the ladder, so use GetOrCreate — not
            // TryGetRecord, which would add a non-native "no stall" reject.
            var record = manager.GetOrCreate(m_sCharName, GetCachedNativeUserId());
            string name = NativeStallWireCodec.DecodeSetStallName(msg);

            // Native measures the Delphi AnsiString length = GBK BYTE count, not UTF-16 chars: a 16-char
            // Chinese name is 32 bytes and IS rejected. Counting chars here would wrongly accept it.
            int nameBytes = string.IsNullOrEmpty(name) ? 0 : HUtil32.GetBytes(name).Length;

            int code = NativeStallWriteTransaction.Evaluate(NativeStallOp.SetName, new NativeStallContext
            {
                StallRecordFound = true,                                    // get-or-create always yields one
                StallRunning = record.Status == StallRecordStatus.Running,  // rec[+0x40]==1 -> -3
                NameTooLong = nameBytes > NativeStallSetNameMaxBytes,       // sub_4057D0 > 30 -> -1
                NameEmpty = nameBytes == 0,                                 // -2
            });

            if (code == 1)
            {
                record.StallName = name;                                    // sub_62159C commit
                PersistStallHeader(record, NativeStallWriteGate.Store);     // sub_61F48C persist
            }
            if (code != NativeStallWriteTransaction.NoResponse)
                SendDefMessage(responseIdent, code, 0, 0, 0, "");
            return true;
        }

        /// <summary>sub_61D3E0 @0x0061D492: <c>sub_4057D0(name) &lt;= 30</c> — a Delphi AnsiString BYTE length.</summary>
        internal const int NativeStallSetNameMaxBytes = 30;

        // MessageStall 4467 (sub_6E7A64 -> sub_61C80C @0x0061C80C). Leave a 留言 on someone's booth.
        // Native body (out_stallmgr.txt:498-546), in order:
        //   v7 = -1; sub_40C988(a6,a7) + sub_49F5F4  -> TARGET booth by the wire CharID; not found => -1
        //   v7 = -2; sub_61FCE4(sender)  -> the per-sender quota gate; false => -2
        //   sub_61A690(..., rec[+0x08], rec[+0x0C]) -> deliver; v7 = 1
        // The wrapper additionally requires a decoded body >= 0x40 (64) bytes BEFORE calling the core, and a
        // short body is a SILENT drop (NoResponse), not a reject — spec §2.4467.
        //
        // sub_61FCE4 @0x0061FCE4 (stall_exec_out.txt:1482-1553) is the quota ledger, keyed by the SENDER's
        // CharID over the manager's second table (mgr+0x24, the stallmsglst side):
        //   entry found  -> if (entry[8] < 3) { sub_61F7E8(entry[0], ++entry[8]); return true }  else return false
        //   entry absent -> allocate 48 bytes, seed CharID(+0x08/+0x0C), cnt(+0x20)=1, createdate(+0x28)=now,
        //                   INSERT (sub_61F5C4) and register; returns true (the a2=1 preload)
        // So each sender may leave at most 3 messages, the 4th returns -2. The counter is per SENDER and is
        // cleared only by the expiry sweep's DELETE (sub_61F9D8) — it does not decay.
        //
        // Delivery sub_61A690 @0x0061A690 is a 29-byte thunk: `return sub_7095F0(a3,a4,a2,6,a5,a6)` — the
        // generic mail send with mailType 6 (NativeMailStore.IsSupportedTag accepts 1/4/5/6). Money-free and
        // item-free: no gold, no attachment, no stock change anywhere on this path.
        private bool TryExecuteNativeStallMessage(TProcessMessage msg, short responseIdent,
            NativeStallManager manager)
        {
            // Wrapper guard (sub_6E7A64): body < 64 decoded bytes => SILENT drop, no SM at all.
            var body = NativeStallWireCodec.DecodeBody(msg?.Payload);
            if (body.Length < NativeStallMessageMinBodyBytes)
                return true;                                     // handled, deliberately silent

            if (!NativeStallWireCodec.TryDecodeMessageStall(msg, out var ownerId, out var text))
                return true;                                     // malformed CharID => silent, same as above

            var target = manager.TryGetRecordById(ownerId);       // sub_40C988 + sub_49F5F4 on the TARGET
            long senderId = GetCachedNativeUserId();

            // sub_61FCE4 quota gate. Consumed ONLY when a target exists, because native evaluates it after
            // the target lookup returns non-null — reordering would burn a sender's quota on a dead booth.
            bool allowed = target != null &&
                manager.TryConsumeStallMessageQuota(senderId, m_sCharName, target, out _);

            int code = NativeStallWriteTransaction.Evaluate(NativeStallOp.MessageStall, new NativeStallContext
            {
                MessagePayloadValid = true,                       // the >= 64 guard already passed above
                StallRecordFound = target != null,                // else -1
                MessageAllowed = allowed,                         // sub_61FCE4 -> else -2
            });

            if (code == 1)
                DeliverNativeStallMessage(target, text);          // sub_61A690 -> sub_7095F0(..., 6, ...)

            if (code != NativeStallWriteTransaction.NoResponse)
                SendDefMessage(responseIdent, code, 0, 0, 0, "");
            return true;
        }

        /// <summary>sub_6E7A64: the CM 4467 body must decode to at least 0x40 bytes or the handler is silent.</summary>
        internal const int NativeStallMessageMinBodyBytes = 0x40;

        // sub_61A690 -> sub_7095F0(recvId, recvName, body, 6, ...): deliver the 留言 to the booth owner as a
        // mailType-6 message addressed by the booth record's owner CharID (rec+0x08/+0x0C), plus a live notice
        // when the owner happens to be online. Fail-safe (best-effort), matching the native store posture:
        // the ledger row + the SM 1 have already been committed, and native never rolls those back on a send
        // failure. Money-free / item-free: moneyType and moneyCount are 0 and no attachment is created.
        private void DeliverNativeStallMessage(NativeStallRecord target, string text)
        {
            if (target == null) return;
            var body = text ?? string.Empty;
            NativeMailStore.CreateMoneyOrderBestEffort(new NativeMailRecord
            {
                SenderId = GetCachedNativeUserId(),
                Sender = m_sCharName,
                Title = "摊位留言",
                Context = body,
                MailType = NativeStallMessageMailType,   // sub_61A690's literal `6`
                MailStatus = 1,                          // UNREAD (loadable)
                AttachStatus = 3,                        // nothing to claim (money/attachment free)
                MoneyType = 0,
                MoneyCount = 0,
                AttachCount = 0,
            }, target.OwnerName);
        }

        /// <summary>sub_61A690 @0x0061A6A3: <c>sub_7095F0(a3, a4, a2, <b>6</b>, a5, a6)</c> — the mail tag.</summary>
        internal const byte NativeStallMessageMailType = 6;

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
                // sub_61BECC @0x61BF43: after the stall-item lookup succeeds,
                // edx=[ebp-8] (the DelItem ecx item-id) / eax=[ebp-4] (player)
                // call 0x6E7D94 -> SendDefMessage(4427, Recog=itemId, 0,0,0,"").
                SendDefMessage(Grobal2.SM_UPT_DEL_STALLITEM, clientItemId, 0, 0, 0, "");
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
        //  * The ident is 4429 (SM_UPT_OTHER_DEL_STALLITEM) at BOTH call sites of sub_6E7DB8. That is true of
        //    THIS sender only — the earlier claim that "there is NO 4428 send anywhere in the image" is wrong.
        //    4428 is sent by a different function, sub_6E7DE0 @0x006E7DE0:
        //        006E7DEA  85C9          test ecx,ecx
        //        006E7DEC  7E18          jle 0x6E7E06        ; length<=0 -> send nothing
        //        006E7DEE  6A00 6A00 6A00              ; Param=0, Tag=0, Series=0
        //        006E7DF4  57            push edi            ; Buf = arg2
        //        006E7DF5  51            push ecx            ; Len = arg3
        //        006E7DF6  66BA4C11      mov dx,0x114C       ; = 4428
        //        006E7DFC  33C9          xor ecx,ecx         ; nRecog = 0
        //        006E7E00  FF9354020000  call [ebx+0x254]
        //    reached from 0x0061DF03 in sub_61DDF8, itself called once from 0x0061BE56. The prior scan looked
        //    for the decimal text "4428" instead of the encoded immediate 4C 11, which is why it found nothing.
        //    4428 is therefore MISSING here, not invented; see staging/m_sm2_impl_20260813.md.
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
        /// <summary>
        /// START 的 -9 闸：sub_6E7C38 → sub_6E78D4（地图名）→ sub_7684A0（位置）。
        /// 盘古1 的 土城摆摊 / 指定地图编号摆摊 / 限制摆摊 三键改的就是这两跳。
        /// </summary>
        private bool MapAllowsStall()
        {
            if (!Plugins.YanshenPangu1Patches.MapMatchesStallPolicy(this))
                return false;
            if (!Plugins.YanshenPangu1Patches.StallLimitPermits(this))
                return false;
            if (Plugins.YanshenPangu1Patches.BypassStallPositionGate(this))
                return true;
            return true; // sub_7684A0 未反汇至 C#，默认放行（与改前一致）
        }

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
