namespace GameSvr
{
    // Dormant model of the UPPER-range Corps write ops 4501 / 4535 / 4536 / 4537 / 4539 / 4540
    // (4534 is a pure paginated read with no result code; 4538 exit is modeled elsewhere by GPT).
    // Hex-Rays verified against M2Server (image base 0x00400000). Corps ops use the SAME sub_6ADA3C
    // role dispatch as Gild (reuses NativeGildRole); the role-strategy VMT slots differ per op.
    // Fail-closed / not wired: the live handlers still return 1000 pending the Corps store; this
    // captures the exact observable result-code ladders only, performs no writes.
    //
    //   4501 member-list refresh   sub_6F071C @0x006F071C : no corp 5; else 0 (SM 4501, 64-byte list).
    //   4534 query_requests        sub_6F56A0 @0x006F56A0 : paginated read, NO result code (sends the
    //        pending-request page + count); not modeled here (no ladder). Documented for completeness.
    //   4535 accept_request (batch) sub_6AF8B8 @0x006AF8B8 : for each of n6 requests, result =
    //        role-strategy[+0x00](req) -> SendDefMessage(4535, wParam=result, reqId). Slot +0x00 is
    //        sub_7019C0 (555) for no_corps/member; sub_701D40/sub_704930/sub_7039A0 (the request-
    //        subtype ACCEPT dispatch: req-not-found 10, wrong-type 23, else subtype accept ladder)
    //        for corps/gild roles. Identical per-item contract to op 4611.
    //   4536 refuse_request (batch) sub_6AF9AC @0x006AF9AC : same as 4535 but role-strategy[+0x04]
    //        (the request-subtype REFUSE dispatch). Identical per-item contract to op 4572.
    //   4537 query_log             sub_6F5E1C @0x006F5E1C : no corp 5; page empty/out-of-range 30; else 0.
    //   4539 notice                sub_6F5884 @0x006F5884 : SET mode (text present) -> no corp 5, else
    //        role-strategy[+0x24] (sub_701A38=555 for no_corps/member; sub_701F48 for corps/gild:
    //        actor-not-found 5, notice >500 chars 24, else 0). GET mode (no text) -> no corp 5, else 0.
    //   4540 dismiss_vice          sub_6F5AA4 @0x006F5AA4 : role-strategy[+0x30]. no_corps/member ->
    //        sub_701C04 (555). corps (corps_vice_owner) -> sub_70273C (self vice-stepdown:
    //        target!=self 555, no player 5, target-not-a-vice 1000, else 0). gild_member/gild_vice/
    //        gild_owner -> sub_703114 (president dismiss vice: actor-invalid 18, target==self 555,
    //        no player 5, target-not-owner 5, not-authorized 18, target-not-a-vice 1000, else 0).
    //
    // The 4535/4536 per-item subtype dispatch (10/23/subtype accept-refuse ladder) is modeled by
    // NativeGildRequestResponseTransaction + NativeGildRequestSubtypeTransaction; here it is an
    // abstract SubtypeDispatchResult input, and only the Corps top-level role gate is added.
    // Reuses NativeGildRole (declared in NativeGildViceTransaction.cs).

    public enum NativeCorpsWriteUpperOp
    {
        MemberListRefresh = 4501,
        AcceptRequest = 4535,
        RefuseRequest = 4536,
        QueryLog = 4537,
        Notice = 4539,
        DismissVice = 4540,
    }

    public sealed class NativeCorpsWriteUpperContext
    {
        /// <summary>Caller's resolved role (sub_6ADA3C).</summary>
        public NativeGildRole Role { get; init; }
        /// <summary>Caller has a corp/gild membership object (player[698] i.e. a1[698] != 0).</summary>
        public bool HasCorp { get; init; }

        // 4535 / 4536: the per-item role-strategy[+0x00]/[+0x04] result for corps/gild roles
        // (request-not-found 10 / wrong-type 23 / subtype accept-refuse ladder). Abstract input.
        public int SubtypeDispatchResult { get; init; }

        // 4537 query_log
        /// <summary>Requested log page has entries (not empty / not out of range).</summary>
        public bool LogPageAvailable { get; init; }

        // 4539 notice
        /// <summary>SET mode: the message carried notice text (v21 != 0). false = GET mode.</summary>
        public bool NoticeSetMode { get; init; }
        /// <summary>SET path: the actor player resolved (sub_5EC030 in sub_701F48).</summary>
        public bool NoticeActorFound { get; init; }
        /// <summary>SET path: notice text length &gt; 500 -> 24.</summary>
        public bool NoticeTooLong { get; init; }

        // 4540 dismiss_vice
        /// <summary>Dismiss target == caller (a5 == a4). Corps path requires it; gild path forbids it.</summary>
        public bool DismissTargetIsSelf { get; init; }
        /// <summary>Gild path: caller handle valid (a4 != 0).</summary>
        public bool DismissActorValid { get; init; }
        /// <summary>Dismiss target player resolved (sub_5EC030).</summary>
        public bool DismissTargetFound { get; init; }
        /// <summary>Gild path: target is a corp owner (*(target+24) == target) -> else 5.</summary>
        public bool DismissTargetIsOwner { get; init; }
        /// <summary>Gild path: caller authorized (sub_7055B0) -> else 18.</summary>
        public bool DismissAuthorized { get; init; }
        /// <summary>Target actually occupied a vice slot and was cleared -> 0; else 1000.</summary>
        public bool DismissTargetIsVice { get; init; }
    }

    public static class NativeCorpsWriteUpperTransaction
    {
        public const int VtblAccept = 0x00;      // 4535 strategy slot
        public const int VtblRefuse = 0x04;      // 4536 strategy slot
        public const int VtblNoticeSet = 0x24;   // 4539 set-notice strategy slot (36)
        public const int VtblDismissVice = 0x30; // 4540 strategy slot (48)
        public const int VtblSendDefMessage = 0x250;
        public const int VtblSendBuffer = 0x254; // buffered SendDefMessage (4501/4534/4535/4536/4537/4539)

        public const int NoPermission = 555;  // role stub / self-only / owner-mismatch gate
        public const int NoCorpOrPlayer = 5;  // no corp membership / player-or-target not found
        public const int NoLogs = 30;         // query_log: empty / out-of-range page
        public const int NoticeTooLongCode = 24;
        public const int NotDismissable = 1000; // dismiss target was not a vice
        public const int ActorInvalid = 18;   // gild dismiss: actor invalid / not authorized
        public const int Success = 0;

        /// <summary>Raw result code sent to the client for the given Corps upper-range op.</summary>
        public static int Evaluate(NativeCorpsWriteUpperOp op, NativeCorpsWriteUpperContext c)
        {
            switch (op)
            {
                case NativeCorpsWriteUpperOp.MemberListRefresh: return MemberListRefresh(c);
                case NativeCorpsWriteUpperOp.AcceptRequest:     return BatchRequestDispatch(c);
                case NativeCorpsWriteUpperOp.RefuseRequest:     return BatchRequestDispatch(c);
                case NativeCorpsWriteUpperOp.QueryLog:          return QueryLog(c);
                case NativeCorpsWriteUpperOp.Notice:            return Notice(c);
                case NativeCorpsWriteUpperOp.DismissVice:       return DismissVice(c);
                default: return NoPermission;
            }
        }

        // 4501 sub_6F071C: no corp -> 5; else 0 (sends the member list).
        private static int MemberListRefresh(NativeCorpsWriteUpperContext c) =>
            c.HasCorp ? Success : NoCorpOrPlayer;

        // 4535 (+0x00) / 4536 (+0x04): batch; per-item = role-strategy result. no_corps/member map to
        // the sub_7019C0/sub_701C4C 555 stubs; corps/gild roles run the request-subtype dispatch,
        // whose 10/23/subtype ladder is supplied as SubtypeDispatchResult.
        private static int BatchRequestDispatch(NativeCorpsWriteUpperContext c)
        {
            if (c.Role == NativeGildRole.NoCorps || c.Role == NativeGildRole.Member)
                return NoPermission; // 555
            return c.SubtypeDispatchResult;
        }

        // 4537 sub_6F5E1C: no corp -> 5; page empty/out-of-range -> 30; else 0.
        private static int QueryLog(NativeCorpsWriteUpperContext c)
        {
            if (!c.HasCorp) return NoCorpOrPlayer;    // 5
            return c.LogPageAvailable ? Success : NoLogs; // 0 / 30
        }

        // 4539 sub_6F5884: SET -> no corp 5 / role gate 555 / sub_701F48 (5/24/0); GET -> no corp 5 / 0.
        private static int Notice(NativeCorpsWriteUpperContext c)
        {
            if (!c.HasCorp) return NoCorpOrPlayer; // 5 (both modes)
            if (!c.NoticeSetMode) return Success;  // GET mode with corp -> 0

            // SET mode, has corp: role-strategy[+0x24].
            if (c.Role == NativeGildRole.NoCorps || c.Role == NativeGildRole.Member)
                return NoPermission;               // 555 (sub_701A38 stub)
            // sub_701F48
            if (!c.NoticeActorFound) return NoCorpOrPlayer; // 5
            if (c.NoticeTooLong) return NoticeTooLongCode;  // 24
            return Success;                                 // 0
        }

        // 4540 sub_6F5AA4: role-strategy[+0x30].
        private static int DismissVice(NativeCorpsWriteUpperContext c)
        {
            switch (c.Role)
            {
                case NativeGildRole.NoCorps:
                case NativeGildRole.Member:
                    return NoPermission; // sub_701C04 stub -> 555

                case NativeGildRole.Corps:
                    return CorpsSelfStepdown(c); // sub_70273C

                default: // GildMember / GildVice / GildOwner
                    return GildDismissVice(c);   // sub_703114
            }
        }

        // corps (corps_vice_owner) slot +0x30 = sub_70273C: a self vice-stepdown.
        private static int CorpsSelfStepdown(NativeCorpsWriteUpperContext c)
        {
            if (!c.DismissTargetIsSelf) return NoPermission;   // target != self -> 555
            if (!c.DismissTargetFound) return NoCorpOrPlayer;  // player not found -> 5
            return c.DismissTargetIsVice ? Success : NotDismissable; // 0 / 1000
        }

        // gild roles slot +0x30 = sub_703114: a president dismissing another vice.
        private static int GildDismissVice(NativeCorpsWriteUpperContext c)
        {
            if (!c.DismissActorValid) return ActorInvalid;     // a4 == 0 -> 18
            if (c.DismissTargetIsSelf) return NoPermission;    // a5 == a4 -> 555
            if (!c.DismissTargetFound) return NoCorpOrPlayer;  // player not found -> 5
            if (!c.DismissTargetIsOwner) return NoCorpOrPlayer;// *(target+24) != target -> 5
            if (!c.DismissAuthorized) return ActorInvalid;     // sub_7055B0 false -> 18
            return c.DismissTargetIsVice ? Success : NotDismissable; // 0 / 1000
        }
    }
}
