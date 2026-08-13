using System;
using System.Collections.Generic;

namespace GameSvr.Services
{
    // In-memory TRoleRequest pending-request registry — the STATEFUL ledger that the existing
    // NativeGildRequest* result-code models (Join/Union/Response/Subtype) lacked. This holds only the
    // transient in-memory request state (create / dedup / lookup / remove); it performs NO
    // membership/relation writes. The accept-side mutations (sub_706264 add-corps-to-gild, save_relation)
    // stay in NativeCorpsService and are wired in the follow-up increment under the accept-path
    // conservation check. Fail-closed by construction: nothing here mutates gild/corps membership.
    //
    // Dump evidence (image base 0x00400000):
    //   sub_6A4FB8 (add-inner, the request-manager container @0x006A4FB8) — the manager owns a primary
    //     id-index [manager+0x1C] keyed by request field [2,3], a secondary id-index [manager+0x20]
    //     keyed by [4,5] (populated only iff sub_707454 is true), and a timestamp-ordered TList
    //     [manager+0x28] whose sort key is request+0x28 (an OLE-date double, ascending / oldest-first).
    //     Add returns 8 when EITHER index key already resolves (sub_49F98C), inserting nothing; else it
    //     registers both index keys (sub_49F650), inserts into the ordered list at the first slot whose
    //     stored time is greater, and returns 0.
    //   sub_7073C4 (TUnionGildRequest ctor @0x007073C4) — object layout: [8,12]=[2,3] request id (freshly
    //     allocated via sub_5E665C when the caller supplies 0/0 — the same composite-id allocator as
    //     GildID, i.e. NativeGildIdAllocator), [16,20]=[4,5] secondary key, [24,28]=[6,7] target key,
    //     [+0x20]=managed name, [40,44]=[+0x28] timestamp double.
    //   Request TYPE (req.[vtbl+0x00]()): 0 TJoinCorpsRequest / 1 TJoinGildRequest / 2 TUnionGildRequest
    //     (NativeGildRequestSubtypeTransaction). sub_6A5284 (accept/refuse lookup) == sub_49F98C: resolves
    //     a pending request by its [2,3] id — the identity the client's 4611/4572 body carries.
    //   sub_6A5190 (remove) / sub_6A5070 (back-link clear) — teardown on accept/refuse.
    //
    // Residual to dump-confirm before the create/accept WIRING (all dumped functions — not idat-blocked):
    //   sub_707454 (which subtypes populate the secondary index), sub_706290 (create publish/broadcast),
    //   and the exact create call-site field args in sub_703624 (4560) / sub_704494 (4573). Until then
    //   this ledger stays unwired and no request is ever created, so the live reads (4570/4571) keep
    //   returning the faithful empty page.

    public enum NativeGildRequestKind
    {
        JoinCorps = 0, // TJoinCorpsRequest, type() sub_7077C0 -> 0
        JoinGild = 1,  // TJoinGildRequest,  type() sub_708000 -> 1
        Union = 2,     // TUnionGildRequest, type() sub_708398 -> 2
    }

    // One pending request object (the TRoleRequest subclass instance). Field names use the reversed
    // dword-index semantics ([2,3]/[4,5]/[6,7]); the per-subtype meaning of the secondary/target keys is
    // filled by the create wiring (from sub_703624 / sub_704494) and is intentionally not baked in here.
    public sealed class NativeGildPendingRequest
    {
        /// <summary>[2,3] the APPLICANT's CharID (the requesting player). Kept for the record's requester
        /// display; the accept/refuse lookup key is <see cref="UniqueId"/> (NOT this), per team-lead's
        /// codec-fidelity xref of the global reqmgr hash.</summary>
        public long RequestId { get; init; }

        /// <summary>[8,C] the per-request GENERATED unique 64-bit id (native sub_5E665C monotonic @apply) —
        /// THE accept/refuse lookup key in the GLOBAL request registry (reqmgr[+0x1C], off_7D727C). The
        /// 4570/4571 listing record echoes it at +0x08; the client sends it back in the 4611/4572 CM body
        /// (an 8-byte id, nothing else). For the dormant model it is a monotonic counter; the byte-exact
        /// sub_5E665C composite formula is a later fidelity detail.</summary>
        public long UniqueId { get; init; }

        /// <summary>[4,5] the secondary/dedup key (manager secondary index, when <see cref="UsesSecondaryKey"/>).</summary>
        public long SecondaryKey { get; init; }

        /// <summary>[6,7] the target key (the accepting/target gild for join &amp; union requests).</summary>
        public long TargetKey { get; init; }

        public NativeGildRequestKind Kind { get; init; }

        /// <summary>request+0x28 timestamp (OLE date). The manager's ascending sort key.</summary>
        public DateTime CreatedTime { get; init; }

        /// <summary>sub_707454: whether this request participates in the manager's secondary index
        /// (and thus the secondary-key dedup). Set by the create wiring per subtype; defaults true so a
        /// requester cannot stack duplicate pending requests until the predicate is dump-confirmed.</summary>
        public bool UsesSecondaryKey { get; init; } = true;
    }

    /// <summary>
    /// The pending-request manager. Reproduces sub_6A4FB8's container contract PER-GUILD (codec-fidelity:
    /// each guild owns its own request container at guild[+0x1C], keyed by the applicant's CharID; an
    /// applicant may hold one pending request per TARGET guild). Dual per-guild id-index dedup (-&gt; 8) +
    /// a timestamp-ordered list. Thread-safe; transient (never persisted — runtime-only, no gamedata
    /// table). The target guild is request.TargetKey ([6,7]).
    /// </summary>
    public sealed class NativeGildRequestLedger
    {
        /// <summary>sub_6A4FB8 return when an index key already resolves.</summary>
        public const int DuplicateCode = 8;

        private readonly object _sync = new();
        // Per-(target guild, applicant CharID) primary index (native guild[+0x1C]).
        private readonly Dictionary<(long TargetGuild, long ApplicantCharId),
            NativeGildPendingRequest> _byApplicant = new();
        // Per-(target guild, secondary key) dedup index (native guild[+0x20]).
        private readonly Dictionary<(long TargetGuild, long SecondaryKey),
            NativeGildPendingRequest> _bySecondary = new();
        // Global insertion-ordered list; per-guild views come from Snapshot's filter.
        private readonly List<NativeGildPendingRequest> _ordered = new();     // [+0x28]
        // GLOBAL request-manager index (native reqmgr[+0x1C], off_7D727C): the SINGLE cross-guild hash the
        // accept/refuse strategies resolve (sub_6A5284), keyed by the per-request UNIQUE id — the only thing
        // the 4611/4572 CM body carries. Distinct from the per-guild dedup/query indices above.
        private readonly Dictionary<long, NativeGildPendingRequest> _byUniqueId = new();
        // Monotonic unique-id source (abstract; native sub_5E665C = time + counter + server tag).
        private long _nextUniqueId = 1;

        // sub_6A4F80 -> sub_6A4FB8. Dedup WITHIN the target guild's container on the primary (applicant
        // CharID) index and (iff UsesSecondaryKey) the secondary index; on a clash return 8 with NO insert.
        // Otherwise register both keys and insert into the ordered list at the first slot whose stored time
        // is greater (ascending/oldest-first). Returns 0.
        public int Add(NativeGildPendingRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            lock (_sync)
            {
                if (_byApplicant.ContainsKey(
                        (request.TargetKey, request.RequestId)))
                    return DuplicateCode;
                if (request.UsesSecondaryKey
                    && _bySecondary.ContainsKey(
                        (request.TargetKey, request.SecondaryKey)))
                    return DuplicateCode;

                if (request.UsesSecondaryKey)
                    _bySecondary[(request.TargetKey, request.SecondaryKey)] =
                        request;
                _byApplicant[(request.TargetKey, request.RequestId)] = request;
                _byUniqueId[request.UniqueId] = request;
                InsertOrderedLocked(request);
                return 0;
            }
        }

        // sub_6A52A0 (join) / sub_7065B0 (union): pre-add duplicate probe on the requester's secondary key
        // WITHIN the target guild. Equivalent to the secondary-index clash sub_6A4FB8 reports as 8.
        public bool HasPendingForSecondaryKey(long targetGuild,
            long secondaryKey)
        {
            lock (_sync)
                return _bySecondary.ContainsKey((targetGuild, secondaryKey));
        }

        // sub_6A5284 == sub_49F98C: accept/refuse lookup by the APPLICANT's CharID within the president's
        // (target) guild container. LEGACY — superseded by TryGetByUniqueId (the real global-registry key is
        // the unique id, not the applicant CharID); retained only for the per-guild dedup/query path.
        public bool TryGetByApplicant(long targetGuild, long applicantCharId,
            out NativeGildPendingRequest request)
        {
            lock (_sync)
                return _byApplicant.TryGetValue(
                    (targetGuild, applicantCharId), out request);
        }

        // Allocate the next per-request unique id (native sub_5E665C, @apply). Monotonic abstract; the
        // byte-exact composite formula is a later fidelity detail.
        public long NextUniqueId()
        {
            lock (_sync) return _nextUniqueId++;
        }

        // sub_6A5284: THE accept/refuse lookup — resolve a pending request by its unique id in the GLOBAL
        // registry. The 4611/4572 CM body carries ONLY this id; the president is taken from the connection.
        public bool TryGetByUniqueId(long uniqueId,
            out NativeGildPendingRequest request)
        {
            lock (_sync) return _byUniqueId.TryGetValue(uniqueId, out request);
        }

        // sub_6A5190 (+ sub_6A5070): remove the resolved request from EVERY container by its unique id
        // (global registry + per-guild dedup/order indices). Returns true when it was present.
        public bool RemoveByUniqueId(long uniqueId)
        {
            lock (_sync)
            {
                if (!_byUniqueId.TryGetValue(uniqueId, out var request))
                    return false;
                _byUniqueId.Remove(uniqueId);
                _byApplicant.Remove((request.TargetKey, request.RequestId));
                if (request.UsesSecondaryKey)
                    _bySecondary.Remove(
                        (request.TargetKey, request.SecondaryKey));
                _ordered.Remove(request);
                return true;
            }
        }

        // sub_6A5190 (+ sub_6A5070 back-link clear): remove the resolved request from every container
        // (the inverse of Add). Returns true when a matching request was present.
        public bool Remove(long targetGuild, long applicantCharId)
        {
            lock (_sync)
            {
                if (!_byApplicant.TryGetValue((targetGuild, applicantCharId),
                        out var request))
                    return false;
                _byApplicant.Remove((targetGuild, applicantCharId));
                if (request.UsesSecondaryKey)
                    _bySecondary.Remove(
                        (targetGuild, request.SecondaryKey));
                _ordered.Remove(request);
                return true;
            }
        }

        /// <summary>
        /// Native expiry threshold, in DAYS, applied to every pending request (join-corps, join-gild and
        /// union alike). Tier-1: sub_6A5D6C @0x006A5D6C loads the request timestamp with
        /// <c>dd 40 28</c> = <c>fld qword [eax+0x28]</c>, spills it (<c>dd 5d dc</c>), calls Now, then
        /// <c>d8 25 f0 5f 6a 00</c> = <c>fsub dword [0x006A5FF0]</c> where the float32 at 0x006A5FF0 is
        /// <c>00 00 40 40</c> = 3.0, and finally <c>dc 5d dc</c> = <c>fcomp qword [ebp-0x24]</c> +
        /// <c>df e0</c> / <c>9e</c> / <c>0f 86</c> (jbe = skip). So an entry expires iff
        /// <c>(Now - 3.0) &gt; request.CreatedTime</c>, strictly greater.
        /// </summary>
        public const double ExpiryDays = 3.0;

        /// <summary>
        /// sub_6A5D6C's purge loop: walk the timestamp-ordered list BACKWARDS (native
        /// <c>8b 46 28 / 8b 58 08 / 4b</c> = load list, take Count, dec to index; loop bottom
        /// <c>83 fb ff</c> = cmp ebx,-1) and drop every entry older than <see cref="ExpiryDays"/>.
        /// Removal goes through the same teardown as accept/refuse (native sub_6A60A4 + sub_6A5070),
        /// i.e. every index plus the global registry. Returns the removed requests, oldest-first, so the
        /// caller can report the native count and apply any per-subtype side effects.
        ///
        /// The subtraction is done in OLE-date space exactly as native does it (the x87 <c>fsub</c> is on
        /// the TDateTime double), not via <c>AddDays</c>.
        /// </summary>
        public IReadOnlyList<NativeGildPendingRequest> RemoveExpired(DateTime now)
        {
            var deadline = DateTime.FromOADate(now.ToOADate() - ExpiryDays);
            var removed = new List<NativeGildPendingRequest>();
            lock (_sync)
            {
                for (var index = _ordered.Count - 1; index >= 0; index--)
                {
                    var request = _ordered[index];
                    // Native: jbe (deadline <= CreatedTime) -> not expired, skip.
                    if (deadline <= request.CreatedTime) continue;
                    _ordered.RemoveAt(index);
                    _byUniqueId.Remove(request.UniqueId);
                    _byApplicant.Remove((request.TargetKey, request.RequestId));
                    if (request.UsesSecondaryKey)
                        _bySecondary.Remove(
                            (request.TargetKey, request.SecondaryKey));
                    removed.Add(request);
                }
            }
            removed.Reverse();
            return removed;
        }

        // Timestamp-ordered snapshot (native TList order) matching the given predicate, e.g. all pending
        // requests targeting a given gild. Feeds the 4570/4571 paginated reads. Pagination is applied by
        // the caller (matching the native handler's page*size / take semantics).
        public IReadOnlyList<NativeGildPendingRequest> Snapshot(
            Func<NativeGildPendingRequest, bool> filter)
        {
            lock (_sync)
            {
                var result = new List<NativeGildPendingRequest>(_ordered.Count);
                foreach (var request in _ordered)
                    if (filter == null || filter(request))
                        result.Add(request);
                return result;
            }
        }

        // sub_6A4FB8's ordered insert: find the first entry whose time is greater than the new one and
        // insert before it; otherwise append. Keeps the list ascending by CreatedTime (oldest first).
        private void InsertOrderedLocked(NativeGildPendingRequest request)
        {
            for (var index = 0; index < _ordered.Count; index++)
            {
                if (_ordered[index].CreatedTime > request.CreatedTime)
                {
                    _ordered.Insert(index, request);
                    return;
                }
            }
            _ordered.Add(request);
        }
    }

    // Per-subtype field-role map for the request object, reversed from the ctors (sub_7073C4 shared by
    // JoinGild+Union; sub_70545C for JoinCorps) and the accept bodies (sub_707D9C JoinGild @0x00707D9C;
    // sub_708168 Union; sub_707468 JoinCorps). This is the seam the create wiring uses to populate a
    // request and the accept/refuse wiring uses to resolve the mutation targets. Fields are the dword
    // indices [2,3] / [4,5] / [6,7] on the request object.
    //
    //   JoinGild (type 1, sub_707D9C accept):
    //     [2,3] = request id (sub_5E665C-allocated) — the accept/refuse LOOKUP key (idat: sub_6A5284).
    //     [4,5] = applicant CORPS id  (accept resolves it via sub_5EA444; add-to-gild target).
    //     [6,7] = accepting GILD id   (accept resolves it via sub_5E76D4).
    //   Union (type 2, sub_708168 accept):
    //     [2,3] = request id.
    //     [4,5] = requesting GILD id (gild B; sub_5E76D4).
    //     [6,7] = accepting  GILD id (gild A; sub_5E76D4; owner check *(gild+4)+24 == acceptor).
    //   JoinCorps (type 0, sub_70545C object / sub_707468 accept) — a distinct, larger object:
    //     applicant at +0x10/+0x14 ([4,5]); target corp at +0x18/+0x1C ([6,7]); corp-leader ids at the
    //     managed slots; corp accept adds the applicant to the corp (existing NativeCorpsService.AcceptRequest
    //     covers the corp-membership half). Reached via the gild-owner accept cascade.
    //
    // Secondary (dedup) key = [4,5] (the applicant/requester identity), per sub_6A4FB8's secondary index.
    public static class NativeGildRequestFieldMap
    {
        // Create-side semantics, locked from the create ladders (sub_703624 4560 / sub_704494 4573):
        //   4560 JoinGild: dedup probe sub_6A52A0 keys on the caller's CORPS id (so SecondaryKey [4,5] =
        //     caller corps id); TargetKey [6,7] = the target GILD (sub_706914). Ladder 555(non-captain
        //     upstream) / 5(no player) / 6(caller already in a gild) / 8(sub_6A52A0 duplicate) / 0.
        //   4573 Union: dedup probe sub_7065B0 keys on the caller's OWN GILD id (SecondaryKey [4,5] = own
        //     gild); TargetKey [6,7] = the target GILD (sub_5E76D4(client-supplied id)). Ladder
        //     555(non-owner upstream) / 5 / 12 / 25 / 19(ally-self) / 34(target union flag clear) /
        //     15(already allied) / 33(at war) / 8(sub_7065B0 duplicate) / 0. NOTE: on the create path
        //     sub_5E6E60 runs BEFORE the dup probe and its return is discarded. Type-3 does NOT enter the
        //     union/hostile lists (the 0x5E6F45/0x5E6F49 `FECB dec bl` + `je` dispatch reaches an append
        //     only for 1 and 2), so it stays out of QUERY_UNION/HOSTILE — but it DOES enter the relation
        //     map itself (0x5E6F1B `8ACB mov cl,bl` / 0x5E6F23 `call 0x49F9C8`), where declare-war's
        //     0x5E6F0D gate later sees it.
        public static NativeGildRequestKind KindFor(int typeDiscriminator) =>
            typeDiscriminator switch
            {
                0 => NativeGildRequestKind.JoinCorps,
                1 => NativeGildRequestKind.JoinGild,
                2 => NativeGildRequestKind.Union,
                _ => NativeGildRequestKind.JoinGild,
            };
    }

    // Offline pending-notice record for the request-response notify (sub_6A52BC @0x006A52BC): when the
    // applicant/requester is OFFLINE, a 17-byte record {[0] = notice-type byte, [1..16] = message text as
    // a 16-byte short string, capacity 15} is appended to a per-recipient TList held in the request
    // manager's +0x24 index (keyed by the recipient's id). When the recipient is ONLINE the server sends
    // SM 4612 directly instead. Delivered on the recipient's next login. Dump-confirmed; the live wiring
    // reuses the existing offline-mail-style delivery. NO writes here — this only records the layout.
    public sealed class NativeGildOfflineNotice
    {
        public const int RecordSize = 17;      // sub_402FA0(17)
        public const int TextShortStringCap = 15; // sub_4039E4(..., 0x0F)
        public const int OnlineNoticeSmId = 4612; // SM 4612 when the recipient is online

        public byte NoticeType { get; init; }
        public string Text { get; init; } = string.Empty;
    }
}
