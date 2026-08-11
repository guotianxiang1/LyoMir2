using SystemModule;

namespace GameSvr.Services
{
    // ================================================================================================
    // The STALL (摆摊) 3-minute maintenance tick — native sub_61BFB8 @0x0061BFB8.
    //
    // This was the largest functional hole left in the stall subsystem: without it booths NEVER expire,
    // never auto-pause when their last item sells, and are never evicted from the manager, so a booth
    // opened once stayed browsable and buyable forever. NativeStallManager.Snapshot() existed with no
    // consumer; this class is that consumer.
    //
    // Byte-exact tick gate (staging/_stall/out_61BECC.txt:83-102):
    //   0061BFBE  call sub_408340                 ; now = GetTickCount
    //   0061BFC7  sub  eax, [ebx+0x30]            ; elapsed = now - mgr[+0x30]   (LAST-TICK stamp)
    //   0061BFCA  cmp  eax, 0x2BF20               ; 180000 ms = 3 minutes
    //   0061BFCF  jb   0x61BFE9                   ; below => do nothing this pass
    //   0061BFD1  mov  [ebx+0x30], edx            ; stamp FIRST, before any work
    //   0061BFD6  call sub_61C974                 ; (1) in-memory sweep
    //   0061BFDD  call sub_61CD8C                 ; (2) return unsold items from closed booths
    //   0061BFE4  call sub_61CB48                 ; (3) mail out the expired-item payouts
    // `jb` means the comparison is unsigned and the boundary is `elapsed >= 180000` — a tick at exactly
    // 180000 ms DOES run. The stamp is written BEFORE the three calls, so a slow sweep never compounds.
    //
    // Sweep sub_61C974 @0x0061C974, exact per-booth order (staging/_stall/out_61C974.txt, iterating the
    // mgr+0x1C booth hash via sub_49F2D8/sub_49F330/sub_49F338):
    //   0061C9AD  call sub_61F3C0                 ; expired?
    //     if EXPIRED:
    //   0061C9B6  cmp byte [ebx+0x40], 1          ; running?
    //   0061C9BE    call sub_61E02C               ;   -> close: return every listed item to the owner
    //   0061C9C3  mov byte [ebx+0x40], 3          ; status = 3 (CLOSED — a FOURTH status value)
    //   0061C9C7  mov byte [ebx+0x30], 0          ; the BOOTH's own +0x30, not the manager's tick stamp
    //   0061C9CD  call sub_61FEAC                 ; persist the header
    //   0061C9E6  call sub_49F3F0                 ; REMOVE from the hash (keyed by sub_40C988(+0x0C,+0x08))
    //   0061C9ED  call sub_61F9D8                 ; DELETE FROM stallmsglst WHERE stallid = ...
    //   0061C9F4  call sub_404690                 ; free the booth object
    //     if NOT expired:
    //   0061C9FD  call sub_61EE88                 ; item count
    //   0061CA02  test eax,eax / jne              ; only when it is ZERO
    //   0061CA06  cmp byte [ebx+0x40], 1          ; and the booth is running
    //   0061CA0E    call sub_61E02C               ;   -> auto-pause an emptied booth
    //
    // Expiry predicate sub_61F3C0 -> sub_61F3D8 @0x0061F3D8 (byte-exact, ida_mail_yb_async_exact.txt:
    // 65771-65836). sub_61F3D8 returns the SECONDS of paid time left:
    //   0061F426  imul eax, 3Ch                   ; rec[+0x34] * 60
    //   0061F429  imul eax, 3Ch                   ;              * 60   => 3600 * paidHours
    //   0061F42D  sub  eax, edx                   ; minus elapsed-since-rec[+0x20] (the create date)
    // and short-circuits to 0 (0061F432 `xor eax,eax`) when the date comparison says the window is over.
    // So "expired" is exactly `remaining <= 0`, computed from the booth's OWN createDate + paid hours —
    // the same formula QueryRemainingSeconds already uses for the 4418 browse header, reused here rather
    // than re-derived. sub_61F3C0's own 24 bytes are not in any dump, so this class does NOT invent extra
    // conditions for it; it is modeled as the documented `sub_61F3D8() == 0` wrapper and nothing more.
    //
    // CONSERVATION: this class moves no money. Items move only through the seam
    // NativeStallItemMove.ReturnAllItems (stall -> owner, one list to the other in the same call), which
    // is the same audited seam PAUSE uses. A booth whose owner is offline keeps its items in the record
    // and the DB row, exactly like native — the owner reclaims them via the existing recovery path, so
    // nothing is destroyed when a booth expires while its owner is away.
    // ================================================================================================
    public static class NativeStallExpiryTick
    {
        /// <summary>sub_61BFB8 @0x0061BFCA: <c>cmp eax, 0x2BF20</c> — 180000 ms = 3 minutes.</summary>
        public const int IntervalMs = 0x2BF20;

        /// <summary>
        /// sub_61C974 @0x0061C9C3: <c>mov byte [ebx+0x40], 3</c>. A FOURTH status the C# enum never had a
        /// name for (Initial/Running/PausedClosed = 0/1/2) — it is what the startup recovery scan matches on
        /// (<c>WHERE IsEnabled=0 AND status=3</c>), so an expired booth must be written as 3, not 2, or its
        /// unsold items would never be picked up by <see cref="NativeStallRecovery.ReturnPendingPayouts"/>.
        /// </summary>
        public const int ExpiredClosedStatus = 3;

        // mgr[+0x30] — the last-tick stamp. Static because the manager instance is a process singleton
        // (NativeStallManagerHost.Manager), matching the native service singleton at 0x007DC264.
        private static int _lastTick;
        private static readonly object Sync = new();

        /// <summary>
        /// Run one maintenance pass if at least <see cref="IntervalMs"/> has elapsed. Cheap and safe to call
        /// from the main loop every iteration — the interval gate is the first thing it does. Returns the
        /// number of booths EXPIRED (0 when the gate was not open). Never throws: a failure inside the sweep
        /// is logged and swallowed, because this runs on the game loop and must not take the server down.
        /// </summary>
        public static int Run()
        {
            var manager = NativeStallManagerHost.Manager;
            if (manager == null || !NativeStallWriteGate.Enabled)
                return 0;

            // Interval gate + stamp, under the lock so two loop threads cannot both open the window.
            // Native stamps BEFORE doing the work (0x0061BFD1 precedes the three calls); same here.
            lock (Sync)
            {
                var now = HUtil32.GetTickCount();
                // `jb` => unsigned below keeps waiting, so the sweep runs at elapsed >= IntervalMs.
                // Unsigned subtraction makes the 32-bit tick wrap harmless (same as the native sub).
                if (_lastTick != 0 && unchecked((uint)(now - _lastTick)) < IntervalMs)
                    return 0;
                _lastTick = now;
            }

            try
            {
                return SweepBooths(manager);
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage("原生摆摊到期检查失败: " + ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// sub_61C974: the in-memory sweep. Expired booths are closed (items returned), marked status 3,
        /// persisted, evicted from the manager and their stallmsglst rows dropped; a still-live booth that
        /// has run out of items is auto-paused. Steps (2)/(3) of the native tick — sub_61CD8C/sub_61CB48 —
        /// are the closed-booth item return, which is already implemented as
        /// <see cref="NativeStallRecovery.ReturnPendingPayouts"/> and runs at startup; it is NOT duplicated
        /// here, because re-running it per tick against the same rows would depend on the IsBoSended flag
        /// for idempotency rather than on ordering, and the startup pass already covers the crash case.
        /// </summary>
        private static int SweepBooths(NativeStallManager manager)
        {
            var store = NativeStallWriteGate.Store;
            var expired = 0;

            // Snapshot first: the sweep evicts records, and native iterates its own hash with an explicit
            // cursor (sub_49F2D8/sub_49F330/sub_49F338) rather than mutating a live enumerator.
            foreach (var booth in manager.Snapshot())
            {
                if (booth == null) continue;

                if (IsExpired(booth))
                {
                    // 0061C9B6/0061C9BE: a RUNNING booth is closed first, which returns its listed items.
                    if (booth.Status == StallRecordStatus.Running)
                        CloseAndReturnItems(booth, store);

                    booth.Status = (StallRecordStatus)ExpiredClosedStatus;  // 0061C9C3 status = 3
                    booth.IsEnabled = 0;                                    // the isEnabled=0 half of the
                                                                            // recovery predicate
                    PersistHeader(booth, store);                            // 0061C9CD sub_61FEAC

                    manager.Remove(booth.OwnerName);                        // 0061C9E6 sub_49F3F0 evict
                    manager.ClearStallMessageQuota(booth.DbIdx);            // 0061C9ED sub_61F9D8 (memory)
                    store?.TryDeleteStallMsg(booth.DbIdx, out _);           // 0061C9ED sub_61F9D8 (DB)
                    expired++;
                    continue;                                               // 0061C9F4 booth object freed
                }

                // 0061C9FD..0061CA0E: not expired — auto-pause a running booth whose items are all gone.
                if (booth.Items.Count == 0 && booth.Status == StallRecordStatus.Running)
                {
                    booth.Status = StallRecordStatus.PausedClosed;          // sub_61E02C rec[+0x40] = 2
                    PersistHeader(booth, store);
                }
            }
            return expired;
        }

        /// <summary>
        /// sub_61F3C0 -> sub_61F3D8: expired when the paid window has run out, i.e. remaining seconds
        /// <c>3600 * DuraTime - elapsed-since-CreateDate</c> is not positive. A booth with no paid time
        /// configured (DuraTime &lt;= 0) has nothing to expire and is left alone — native's
        /// <c>3600 * 0 - elapsed</c> would be negative, but such a record has never been started (START
        /// requires remaining paid time via its -7 rung), so treating it as "not expired" keeps an
        /// unstarted booth from being swept out from under its owner.
        /// </summary>
        internal static bool IsExpired(NativeStallRecord booth)
        {
            if (booth == null || booth.DuraTime <= 0)
                return false;
            var remaining = 3600.0 * booth.DuraTime - (DateTime.Now - booth.CreateDate).TotalSeconds;
            return remaining <= 0;
        }

        // sub_61E02C close: hand every listed item back through the audited item-move seam. The owner may
        // be offline, in which case the items stay in the record + the DB rows (native behaviour — the
        // booth is paid up front and the owner reclaims later); nothing is destroyed either way.
        private static void CloseAndReturnItems(NativeStallRecord booth, INativeStallStore store)
        {
            var owner = FindOnlineOwner(booth.OwnerId);
            if (owner?.m_ItemList == null)
                return;                       // offline: rows persist for the recovery path, items kept

            NativeStallItemMove.ReturnAllItems(owner.m_ItemList, booth, out var removed);
            for (var i = 0; i < removed.Count; i++)
            {
                owner.SendAddItem(removed[i].Item);
                store?.TryDeleteStallItem(removed[i].DbIdx, out _);
            }
            if (removed.Count > 0)
                owner.WeightChanged();
        }

        private static TPlayObject FindOnlineOwner(long ownerId)
        {
            if (ownerId == 0) return null;
            var players = M2Share.UserEngine?.PlayObjects;
            if (players == null) return null;
            foreach (var player in players)
            {
                if (player == null || player.m_boGhost) continue;
                if (player.GetCachedNativeUserId() == ownerId) return player;
            }
            return null;
        }

        // sub_61FEAC: UPDATE the stall header (an expired booth always has a DbIdx — it was INSERTed when
        // it opened — so this is the UPDATE arm; a record that was never persisted has nothing to write).
        private static void PersistHeader(NativeStallRecord booth, INativeStallStore store)
        {
            if (store == null || booth.DbIdx == 0) return;
            store.TryUpdateStall(booth.ToHeaderRow(), booth.OwnerId, booth.DbIdx, out _);
        }
    }
}
