namespace GameSvr
{
    // ------------------------------------------------------------------------------------------------
    // Dormant MODEL of the original M2Server in-memory STALL (摆摊) manager subsystem — the runtime
    // active-stall state that the stall write handlers read via the resolver sub_49F5F4. Reversed from
    // the exclusive idat manager pass (staging/update_clothes_4637_ida_work/stall_mgr_out.txt +
    // stall_exec_out.txt). This ONLY describes field offsets / structure / the manager sub contracts so a
    // future live hook can populate <see cref="NativeStallContext"/> from the real objects WITHOUT
    // fabrication. It is NOT wired: no allocation, no lookup, no mutation happens here — the live path
    // stays RejectUnavailableStallRequest until the manager is built + validated full-stack.
    //
    // BACKING STORE (sub_49F5F4 -> sub_49F2E4): a name-keyed OPEN hash. The manager cores prime a thread
    //   key from the player's name (sub_40C988(player[+0x588], player[+0x58C])) then call sub_49F5F4,
    //   which is `entry = *sub_49F2E4(hash, key); return entry ? *(entry+0x14) : 0`. sub_49F2E4 is the
    //   generic hash probe: bucket = [hash+8] + 4*(sub_49F35C(key) % [hash+4]); walk entries comparing the
    //   key at [entry+0x10]; the payload (the stall RECORD) is at [entry+0x14]. 0 => the owner has no
    //   active stall (StallRecordFound=false). The hash itself is a manager global (allocated by
    //   sub_49F23C, capacity 1023); its exact EA was not needed to pin the structure and is the only
    //   remaining 1-symbol follow-up.
    //
    // STALL RECORD layout (ctor sub_61ED04, confirmed by the executors):
    //   +0x04 (4)    dword   — DB stall idx companion / flags (0 at ctor)
    //   +0x08 (8)    dword   — owner name ptr  (a4)      [key half 1]
    //   +0x0C (12)   dword   — owner name len  (a5)      [key half 2]  (Delphi AnsiString pair)
    //   +0x18 (24)   dword   — DB stall idx (0 until INSERT stall returns LAST_INSERT_ID)
    //   +0x20 (32)   double  — createdate (sub_40F0A4 now)   [set at ctor / when status==0 in sub_61FEAC]
    //   +0x28 (40)   double  — modifydate (sub_40F0A4 now)   [refreshed by sub_61FEAC]
    //   +0x30 (48)   byte    — 1 at ctor (level? default)
    //   +0x34 (52)   dword   — 0 at ctor (a persisted field, written by UpdateStall)
    //   +0x38 (56)   byte    — 0 at ctor
    //   +0x40 (64)   byte    — STATUS: 0 initial / 1 running / 2 paused-closed  (THE StallRunning gate)
    //   +0x3C (60)   ptr     — items TList (sub_404660)
    //   +0x44 (68)   dword   — -1 at ctor (posx or a coord)
    //   +0x48 (72)   dword   — -1 at ctor (posy or a coord)
    //   +0x50 (80)   ptr     — per-record item HASH (sub_49F23C cap 1023; item-by-idx)
    //   +0x54 (84)   ptr     — orders/messages TList (sub_404660; buyer-order objects hang here)
    //
    // BUYER-ORDER in-memory object (sub_620F58, alloc sub_402FA0(264) = 264 bytes):
    //   +0x00 idx (from INSERT buyer_order) · +0x08/+0x0C buyer name · +0x04 seller/ctx ·
    //   +0x1F (31)  the 208-byte (0xD0) item struct (qmemcpy from src) — SAME 208-byte record as srvData ·
    //   +0xF0 (240) uprice · +0xF4 (244) moneytype · +0xF8 (248) count(byte) · +0xFC (252) total ·
    //   +0x100/+0x101 flags. Appended to record[+0x54] on INSERT success (sub_620B2C -> idx>0).
    //
    // FINALIZE LEAVES (previously abstract passthroughs — now concrete):
    //   PauseClose = sub_61A36C (checks global table unk_7B464C[41] via sub_779190/sub_7199CC; ALWAYS
    //     returns 1) THEN caller (sub_61E02C) sets record[+0x40]=2 and calls sub_61FEAC.
    //   sub_61FEAC (@0x0061FEAC) IS the stall-header persist: refresh modifydate[+0x28] (and createdate
    //     [+0x20] when status==0), then ExecuteScript (sub_724E48) the `UPDATE %s.stall SET stallname=%s,
    //     level=%d,... status=%d,... WHERE ownerid=%d and idx=%d` (@0x0062009C) — i.e. EXACTLY
    //     NativeStallMySqlStore.BuildUpdateStall. Returns 1 on success / -1 on SQL fail. So
    //     PauseCloseResult resolves to 1 (persist ok) / -1 (persist fail).
    //   BuyFinalize (moneytype 0) = sub_61E0C8: resolve item (sub_61EE34), stackable split (item[+0x14]==7,
    //     count item[+0x26]) via sub_7882B8/sub_768BE0, give to buyer (vtbl+584), decrement seller stock
    //     (record[+0xF0]-=qty, item[+0x25 word]-=qty), money transfer sub_61A6B0, record buyitem_detail
    //     (sub_6210B0), sub_62099C; on full-sell delete the item (sub_61DF24) + persist (sub_61FEAC).
    //     Returns -5 default / >0 on success.
    // ------------------------------------------------------------------------------------------------

    /// <summary>Value of the stall record status byte at record[+0x40].</summary>
    public enum StallRecordStatus
    {
        /// <summary>0 — freshly constructed, not yet started.</summary>
        Initial = 0,
        /// <summary>1 — running/active (the StallRunning gate; blocks SetName, gates BuyTargetStallActive).</summary>
        Running = 1,
        /// <summary>2 — paused/closed (set by the pause/close path sub_61E02C before persist).</summary>
        PausedClosed = 2,
    }

    public static class NativeStallManagerModel
    {
        // ---- manager subroutine addresses (image base 0x00400000) ----
        public const uint ResolveRecordEa = 0x0049F5F4;   // sub_49F5F4: entry? *(entry+0x14) : 0
        public const uint HashProbeEa = 0x0049F2E4;        // sub_49F2E4: generic open-hash probe
        public const uint HashCodeEa = 0x0049F35C;         // sub_49F35C: key hash
        public const uint HashAllocEa = 0x0049F23C;        // sub_49F23C(cap): open-hash allocator
        public const int HashDefaultCapacity = 1023;       // sub_49F23C(1023, ...) at record ctor
        public const uint RecordCtorEa = 0x0061ED04;       // sub_61ED04: stall record initializer
        public const uint ThreadKeyPrimeEa = 0x0040C988;   // sub_40C988(player[+0x588], player[+0x58C])
        public const uint BuyOrderOrchestratorEa = 0x00620F58; // sub_620F58: build+insert buyer_order in-mem
        public const uint BuyFinalizeEa = 0x0061E0C8;      // sub_61E0C8: moneytype-0 purchase finalize
        public const uint PauseCloseCheckEa = 0x0061A36C;  // sub_61A36C: pre-close global check (returns 1)
        public const uint StallHeaderPersistEa = 0x0061FEAC; // sub_61FEAC: UPDATE gamedata.stall (== BuildUpdateStall)
        public const uint ExecuteScriptEa = 0x00724E48;    // sub_724E48: TMySQLDB.ExecuteScript (direct MySQL)

        // ---- open-hash object layout (the backing-store container) ----
        public const int HashBucketCountOffset = 4;   // [hash+4]  bucket count (modulus)
        public const int HashBucketsPtrOffset = 8;    // [hash+8]  bucket array base
        public const int HashEntryKeyOffset = 0x10;   // [entry+0x10] key compared by sub_49F2E4
        public const int HashEntryPayloadOffset = 0x14; // [entry+0x14] payload = the stall record

        // ---- stall record field offsets ----
        public const int RecOwnerNamePtrOffset = 0x08;
        public const int RecOwnerNameLenOffset = 0x0C;
        public const int RecDbIdxOffset = 0x18;
        public const int RecCreateDateOffset = 0x20;   // double
        public const int RecModifyDateOffset = 0x28;   // double
        public const int RecStatusOffset = 0x40;       // byte: StallRecordStatus
        public const int RecItemsListOffset = 0x3C;    // TList
        public const int RecItemHashOffset = 0x50;     // per-record item hash (cap 1023)
        public const int RecOrdersListOffset = 0x54;   // TList (buyer-order objects)

        // ---- buyer-order in-memory object (264 bytes) ----
        public const int BuyOrderObjectSize = 264;
        public const int BuyOrderItemStructOffset = 0x1F;   // +31: the 208-byte item struct
        public const int BuyOrderUpriceOffset = 0xF0;       // 240
        public const int BuyOrderMoneyTypeOffset = 0xF4;    // 244
        public const int BuyOrderCountOffset = 0xF8;        // 248 (byte)
        public const int BuyOrderTotalOffset = 0xFC;        // 252
        public const int ItemStructSize = 208;             // 0xD0 — same as the srvData BLOB

        // ---- item (within a stall record) field offsets used by the buy path ----
        public const int ItemStdModeOffset = 0x14;         // ==7 => stackable
        public const int ItemMakeIdOffset = 0x18;          // [item+24]
        public const int ItemCountOffset = 0x26;           // word count/dura
        public const uint PauseCloseGlobalTableEa = 0x007B464C; // unk_7B464C (sub_61A36C checks [table]+..)

        /// <summary>sub_61A36C always returns 1 (a side global check, no gating effect).</summary>
        public const int PauseCloseCheckResult = 1;

        /// <summary>
        /// Context-population contract for the FUTURE live hook (documented, not executed here). Given a
        /// resolved stall record pointer (0 = none) and the running state, this is how each
        /// <see cref="NativeStallContext"/> field must be derived from the real manager objects — so the
        /// hook populates the context faithfully instead of guessing:
        ///   FeatureEnabled      = sub_6E78D4() (stall system flag)
        ///   StallRecordFound    = ResolveRecord(ownerName) != 0
        ///   StallRunning        = record[+0x40] == (byte)StallRecordStatus.Running
        ///   NameTooLong/Empty   = from the SET_STALL_NAME arg (len>30 / empty)
        ///   Add* / Del* / Buy*  = from the target item in record[+0x50] item-hash / record[+0x3C] list,
        ///                         BuyMoneyType = stallItem[+0xF4], BuyerHasEnoughMoney = buyer balance
        ///                         ([+0x760] type1 / [+0x15C] type0) >= count*unitPrice(stallItem[+0xF8]),
        ///                         BuyQtyValid = item[+0x14]==7 ? item[+0x26]>=qty : qty==1
        ///   BuyFinalizeResult   = sub_61E0C8 (type0) / 0 (type1 async)
        ///   PauseCloseResult    = sub_61FEAC persist (1 ok / -1 fail); StartCoreResult = sub_61D4F0 tail.
        /// This method is a pure DESCRIPTOR — it takes no live objects and performs no work.
        /// </summary>
        public static bool DescribesFaithfulPopulation => true;
    }
}
