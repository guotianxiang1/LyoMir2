using System;
using System.Collections.Generic;

namespace GameSvr.Services
{
    // Dormant STATE models for the three DEFERRED gild social items that gild-wiring left dormant
    // (staging/gild_wiring_applied_20260731.md §6/§8). Address-level evidence, image base 0x00400000,
    // is written up in staging/gild_deferred_items_20260801.md. This is a NEW file: it is NOT wired,
    // touches no live dispatch, and performs no writes.
    //
    // It supplies the missing in-memory STATE (concern set, union flag) + the by-name resolver bit that
    // the existing result-code transactions need before gild-wiring can route, with the gated
    // INativeGildStore pattern:
    //   CM_GILD_CONCERN_GILD_ID   4576  sub_6F6610 -> gild_owner[+0x5C] sub_703ED4   (SM 4576)
    //   CM_GILD_CONCERN_GILD_NAME 4586  sub_6F6654 -> sub_5E76F0 then sub_703ED4     (SM 4576)
    //   CM_GILD_CANCLE_CONCERN    4578  sub_6F68AC -> gild_owner[+0x60] sub_703C54   (SM 4578)
    //   CM_GILD_ENABLE_UNION      4581  sub_6F6BB4 -> owner/vice[+0x58] sub_704EAC   (SM 4581)
    //   CM_GILD_DECLARE_WAR_NAME  4585  sub_6F6958 -> sub_5E76F0 then sub_703F74     (SM 4579)
    //
    // Reuses GameSvr.NativeGildRole (declared in NativeGildViceTransaction.cs).

    // ------------------------------------------------------------------------------------------------
    // 1. CONCERN SET (关注列表) — the in-memory TList at gild+44 (0x2C).
    //    TList: Count at list+8; Get=sub_424D4C; Add=sub_424AB8; Delete=sub_424B30; Contains=sub_706A5C.
    //    Siblings on the same gild: gild+48 (relation list), gild+52 (hostile mirror) — not modeled here.
    // ------------------------------------------------------------------------------------------------

    public enum NativeGildConcernAddOutcome
    {
        // sub_70675C returned 0 (destination was absent): it was appended -> handler enqueues INSERT, code 0.
        Added,
        // sub_70675C returned != 0 (destination already present): no append -> handler code 1000.
        AlreadyPresent,
    }

    public enum NativeGildConcernRemoveOutcome
    {
        // sub_70678C returned 1 (found + TList.Delete): handler enqueues DELETE, code 0.
        Removed,
        // sub_70678C returned 0 (not found): handler code 1000.
        NotPresent,
    }

    /// <summary>
    /// Equivalent of one gild's concern TList (native gild+44). Mirrors the three native helpers:
    /// Contains (sub_706A5C), dedup add (sub_70675C), find+remove (sub_70678C). Ordered like the native
    /// TList (append on add); membership is by destination gild id.
    /// </summary>
    public sealed class NativeGildConcernSet
    {
        public const int NativeFieldOffset = 44; // gild+0x2C

        private readonly List<long> _dstGildIds = new();

        public int Count => _dstGildIds.Count;

        public IReadOnlyList<long> DestinationGildIds => _dstGildIds;

        /// <summary>sub_706A5C: linear membership scan over 0..Count-1.</summary>
        public bool Contains(long dstGildId) => _dstGildIds.Contains(dstGildId);

        /// <summary>
        /// sub_70675C add_concern: dedup insert. Returns AlreadyPresent when the destination is already
        /// in the list (native returns the "present" flag, which the handler maps to 1000); otherwise it
        /// is appended and Added is returned (handler code 0 + INSERT gildconcern).
        /// </summary>
        public NativeGildConcernAddOutcome TryAdd(long dstGildId)
        {
            if (Contains(dstGildId)) return NativeGildConcernAddOutcome.AlreadyPresent;
            _dstGildIds.Add(dstGildId);
            return NativeGildConcernAddOutcome.Added;
        }

        /// <summary>
        /// sub_70678C: find and remove. Returns Removed when the destination was present (handler code 0 +
        /// DELETE gildconcern); otherwise NotPresent (handler code 1000).
        /// </summary>
        public NativeGildConcernRemoveOutcome TryRemove(long dstGildId)
        {
            return _dstGildIds.Remove(dstGildId)
                ? NativeGildConcernRemoveOutcome.Removed
                : NativeGildConcernRemoveOutcome.NotPresent;
        }

        /// <summary>
        /// Snapshot seed from the loader SELECT (0x5E8890: Idx, GildID, DstGildID from gamedata.gildconcern),
        /// which gild-wiring must add before wiring. Idempotent (native list is a set in practice).
        /// </summary>
        public void SeedFromLoad(long dstGildId)
        {
            if (!Contains(dstGildId)) _dstGildIds.Add(dstGildId);
        }
    }

    // ------------------------------------------------------------------------------------------------
    // 2. CONCERN result-code ladders (4576 / 4578 / 4586).
    //    Exact bodies: sub_703ED4 (add by id), sub_6F6654 (add by name), sub_703C54 (cancel).
    //    NOTE (fidelity correction vs NativeGildUnionConcernTransaction.AddConcern): the code order
    //    25 -> 19 -> 1000 is correct there, but the field NAMES are mislabeled — 19 is "target == own
    //    gild" (self-concern), and 1000 is "already present" (duplicate), NOT a precheck / add failure.
    // ------------------------------------------------------------------------------------------------

    public enum NativeGildConcernOp
    {
        AddConcernById = 4576,   // sub_6F6610 -> sub_703ED4
        AddConcernByName = 4586, // sub_6F6654 -> sub_5E76F0 then sub_703ED4 (replies SM 4576)
        CancelConcern = 4578,    // sub_6F68AC -> sub_703C54
    }

    public sealed class NativeGildConcernContext
    {
        /// <summary>Caller's resolved gild role (sub_6ADA3C). Concern is president-only; vice/others hit a 555 stub.</summary>
        public NativeGildRole Role { get; init; }

        /// <summary>4586 only: sub_5E76F0 resolved the target gild BY NAME. Ignored by 4576/4578; defaults true.</summary>
        public bool NameResolved { get; init; } = true;

        /// <summary>Caller player object resolved (sub_5EC030 != 0).</summary>
        public bool PlayerResolved { get; init; } = true;

        /// <summary>Caller has a gild (player[+4] != 0).</summary>
        public bool HasGild { get; init; }

        /// <summary>Target gild resolved (sub_5E76D4 by id inside the strategy != 0).</summary>
        public bool TargetGildFound { get; init; }

        /// <summary>Add only: target gild == caller's own gild (sub_703ED4 v7==v6 -> 19).</summary>
        public bool TargetIsSelf { get; init; }

        /// <summary>Add only: sub_70675C reported the destination already in the concern set.</summary>
        public bool ConcernAlreadyPresent { get; init; }

        /// <summary>Cancel only: sub_70678C found the destination in the concern set (removable).</summary>
        public bool ConcernPresentForRemove { get; init; }
    }

    public static class NativeGildConcernLadder
    {
        public const int NoPermission = 555;   // non-owner role -> strategy stub (sub_701A5C / sub_7019E4)
        public const int NoPlayer = 5;          // sub_5EC030 == 0
        public const int NoGild = 12;           // player[+4] == 0
        public const int NameUnresolved = 12;   // 4586: sub_5E76F0 == 0 (same 12 as NoGild)
        public const int TargetNotFound = 25;   // sub_5E76D4 == 0
        public const int TargetIsSelfCode = 19; // add: target == own gild
        public const int WriteBlocked = 1000;   // add: duplicate (sub_70675C true) ; cancel: absent (sub_70678C false)
        public const int Success = 0;

        /// <summary>Raw result code that goes verbatim into SendDefMessage wParam.</summary>
        public static int Evaluate(NativeGildConcernOp op, NativeGildConcernContext c)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));

            // 4586 name path: sub_5E76F0 gild-name resolution runs (inside the SEH frame) BEFORE the role
            // strategy is dispatched; an unresolved name leaves the result at its 12 init.
            if (op == NativeGildConcernOp.AddConcernByName && !c.NameResolved)
                return NameUnresolved;

            if (c.Role != NativeGildRole.GildOwner) return NoPermission;
            if (!c.PlayerResolved) return NoPlayer;
            if (!c.HasGild) return NoGild;
            if (!c.TargetGildFound) return TargetNotFound;

            if (op == NativeGildConcernOp.CancelConcern)
                return c.ConcernPresentForRemove ? Success : WriteBlocked;

            // add (by id or by name): self check, then dedup.
            if (c.TargetIsSelf) return TargetIsSelfCode;
            return c.ConcernAlreadyPresent ? WriteBlocked : Success;
        }

        /// <summary>SM ident placed in the reply. 4576 AND 4586 both reply with SM 4576; 4578 replies SM 4578.</summary>
        public static int ReplySmId(NativeGildConcernOp op) =>
            op == NativeGildConcernOp.CancelConcern ? 4578 : 4576;

        /// <summary>True when the success path enqueues INSERT gamedata.gildconcern (add ops only).</summary>
        public static bool EnqueuesInsert(NativeGildConcernOp op, int result) =>
            result == Success && op != NativeGildConcernOp.CancelConcern;

        /// <summary>True when the success path enqueues DELETE gamedata.gildconcern (cancel op only).</summary>
        public static bool EnqueuesDelete(NativeGildConcernOp op, int result) =>
            result == Success && op == NativeGildConcernOp.CancelConcern;
    }

    // ------------------------------------------------------------------------------------------------
    // 3. UNION-ENABLE flag (4581) — in-memory byte at gild+40 (0x28), NO gamedata.Gild column.
    //    Proven by: DDL 0x5E79EC {ID,CreateTime,GildName,OwnerCorpsID,ViceOwnerID,GildNotice}; loader
    //    SELECT 0x5E8598 (no flag column); make_save_gild sub_5E926C does not serialize +40.
    //    sub_704EAC only re-emits the STANDARD 3-column UPDATE (0x5E9568) when the byte changes — a
    //    no-op side effect for the flag. The flag is session-only and resets to default on restart.
    // ------------------------------------------------------------------------------------------------

    public enum NativeGildUnionFlagWrite
    {
        NoChange, // requested == current: sub_704EAC skips the save entirely.
        Resave,   // requested != current: flag updated + standard 3-column TrySaveGild re-emitted.
    }

    /// <summary>
    /// The 4581 union-enable flag cell. Set() mirrors sub_704EAC's "write only when changed" body and
    /// reports whether a (standard, flag-less) Gild UPDATE must be re-emitted. There is no persistent
    /// column, so nothing loads or saves this value; a ported NativeGildSnapshot needs only this bool.
    /// </summary>
    public sealed class NativeGildUnionFlagCell
    {
        public const int NativeFieldOffset = 40;       // gild+0x28
        public const bool HasPersistentColumn = false; // no gamedata.Gild column exists

        // The gild constructor sub_7062D0 seeds the byte TRUE at 0x70633A `C6 47 28 01
        // mov byte [edi+0x28],1` (EDI is the instance, loaded at 0x7062E6 `mov edi,eax`).
        // Since there is no column, every gild starts each session accepting unions.
        private bool _enabled = true;

        public bool Enabled => _enabled;

        public NativeGildUnionFlagWrite Set(bool value)
        {
            if (_enabled == value) return NativeGildUnionFlagWrite.NoChange;
            _enabled = value;
            return NativeGildUnionFlagWrite.Resave;
        }
    }

    public static class NativeGildUnionFlagLadder
    {
        public const int NoPermission = 555; // role is neither owner nor vice
        public const int NoPlayer = 5;        // sub_5EC030 == 0
        public const int NoGild = 12;         // player[+4] == 0
        public const int Success = 0;         // reached the flag cell (whether or not it changed)

        /// <summary>
        /// 4581 is reachable by BOTH president and vice (both strategy[+0x58] slots are sub_704EAC).
        /// Mirrors NativeGildUnionConcernTransaction.EnableUnion but records the owner+vice gate here so
        /// the union item is assertable alongside its state cell.
        /// </summary>
        public static int Evaluate(NativeGildRole role, bool playerResolved, bool hasGild)
        {
            if (role != NativeGildRole.GildOwner && role != NativeGildRole.GildVice)
                return NoPermission;
            if (!playerResolved) return NoPlayer;
            if (!hasGild) return NoGild;
            return Success;
        }
    }

    // ------------------------------------------------------------------------------------------------
    // 4. BY-NAME resolver (4573 request-union / 4585 declare-war / 4586 concern) — sub_5E76F0.
    //    sub_5E76F0(gild_manager = *off_7D5A58, name): uppercases the name (sub_40BC50, ASCII a-z->A-Z)
    //    then looks it up in the gild-manager's gild-name index (gild_manager+0x24) via sub_49F5F4;
    //    returns the matching GILD (node+20) or null. It is a GILD-NAME -> GILD lookup against the full
    //    in-memory gild registry (all gilds loaded from gamedata.Gild) — NOT a player lookup, NOT
    //    online-restricted. sub_706914(resolvedGild) = *(gild+24) = that gild's own id (the target key).
    //    Proven: off_7D5A58 is labeled gild_manager; sub_5E752C (AddGild) uses the SAME sub_49F5F4 to
    //    reject a duplicate gild name (error 2). An unresolved name yields handler code 12.
    // ------------------------------------------------------------------------------------------------

    public sealed class NativeGildNameResolver
    {
        private readonly IReadOnlyDictionary<string, long> _gildIdByUpperName;

        public NativeGildNameResolver(IReadOnlyDictionary<string, long> gildIdByUpperName)
        {
            _gildIdByUpperName = gildIdByUpperName ??
                                 throw new ArgumentNullException(nameof(gildIdByUpperName));
        }

        /// <summary>
        /// sub_40BC50: byte-wise ASCII uppercase (0x61..0x7A -> subtract 0x20). Applied here per char;
        /// production wiring must uppercase the GBK/latin1_bin name BYTES the same way so the registry
        /// key matches exactly for multi-byte names.
        /// </summary>
        public static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name)) return name ?? string.Empty;
            var chars = name.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
                if (chars[i] >= 'a' && chars[i] <= 'z')
                    chars[i] = (char)(chars[i] - 32);
            return new string(chars);
        }

        /// <summary>
        /// sub_5E76F0: resolves the target gild id from a gild NAME. Returns false (native null -> handler
        /// code 12) when no gild in the registry carries that (normalized) name.
        /// </summary>
        public bool TryResolve(string name, out long targetGildId)
        {
            targetGildId = 0;
            return name != null &&
                   _gildIdByUpperName.TryGetValue(Normalize(name), out targetGildId);
        }
    }
}
