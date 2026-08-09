namespace GameSvr
{
    // Dormant model of the Gild pending-request SUBTYPE accept/refuse virtual methods -- the real
    // result ladders that NativeGildRequestResponseTransaction previously carried as an abstract
    // SubtypeResult input. Hex-Rays + VMT-data verified (m2full.i64, image base 0x00400000).
    //
    // Outer dispatch (already modeled in NativeGildRequestResponseTransaction): the 4611/4572
    // handlers resolve the pending request via sub_6A5284 (null -> 10), read req.[vtbl+0x00]() to get
    // the request TYPE, and invoke req.[vtbl+0x14] (accept) / req.[vtbl+0x18] (refuse) on the concrete
    // subclass. Role gating (555 for non-presidents, 23 for wrong-type at the corp layer, etc.) is
    // also upstream. These subtype methods are role-independent, so NativeGildRole is intentionally
    // not referenced here (it stays owned by the vice/leadership models; not redefined).
    //
    // Request class hierarchy (classptr / type() value / accept +0x14 / refuse +0x18):
    //   TJoinCorpsRequest 0x007071AC  type sub_7077C0->0  accept sub_707468  refuse sub_7077C4
    //   TGildRequest      0x00707230  (abstract base)                        refuse sub_708520
    //   TJoinGildRequest  0x007072B0  type sub_708000->1  accept sub_707D9C  refuse sub_708520 (inherited)
    //   TUnionGildRequest 0x00707334  type sub_708398->2  accept sub_708168  refuse sub_708004
    //
    // Ladders (see staging/gild_request_subtype_codes_20260731.md for full branch evidence):
    //   JoinGild.accept  sub_707D9C : 555 / 12 / 13 / 5 / 6 / 1000 / 0   (matches GPT 4611 join accept)
    //   JoinGild.refuse  sub_708520 : 555 / 12 / 5 / 0                   (matches GPT 4572 refuse)
    //   Union.accept     sub_708168 : 555 / 12 / 12 / 555 / <save_relation sub_5E6E60, 0=ok>
    //   Union.refuse     sub_708004 : 555 / 12 / 5 / 0
    //   JoinCorps.accept sub_707468 : 555 / 5 / 16 / 555 / 0
    //   JoinCorps.refuse sub_7077C4 : 555 / 0
    //
    // Fail-closed/dormant: performs no writes and is not wired; the live 4611/4572 handlers still
    // return 1000 pending the Gild store.

    public enum NativeGildRequestSubtype
    {
        JoinCorps = 0, // TJoinCorpsRequest, type() sub_7077C0 -> 0 (handled by corp strategies / cascade)
        JoinGild = 1,  // TJoinGildRequest,  type() sub_708000 -> 1 (the "join" of type()==1)
        Union = 2,     // TUnionGildRequest, type() sub_708398 -> 2 (the "union" of type()==2)
    }

    public enum NativeGildRequestSubtypeOp
    {
        Accept = 0x14, // req.[vtbl+0x14]
        Refuse = 0x18, // req.[vtbl+0x18]
    }

    public sealed class NativeGildRequestSubtypeContext
    {
        /// <summary>The request handle passed to the subtype method is non-zero (else 555).</summary>
        public bool RequestPresent { get; init; }

        // --- gild resolution: join-gild + union (sub_5E76D4(req[6],req[7])) ---
        /// <summary>Accepting/refusing gild resolved.</summary>
        public bool GildFound { get; init; }
        /// <summary>Refuse (join-gild + union): the gild has an owner-corp (*(gild+4) != 0).</summary>
        public bool GildHasOwnerCorp { get; init; }

        // --- JoinGild.accept (sub_707D9C) ---
        /// <summary>Gild member limit reached (sub_7065FC) -> 13.</summary>
        public bool GildMemberLimitReached { get; init; }
        /// <summary>Applicant resolved (sub_5EA444(req[4],req[5])).</summary>
        public bool ApplicantFound { get; init; }
        /// <summary>Applicant already belongs to a gild (*(applicant+4) != 0) -> 6.</summary>
        public bool ApplicantAlreadyInGild { get; init; }
        /// <summary>Add-to-gild succeeded (sub_706264) -> 0, else 1000.</summary>
        public bool AddToGildOk { get; init; }

        // --- Union.accept (sub_708168) ---
        /// <summary>Second gild resolved (sub_5E76D4(req[4],req[5])).</summary>
        public bool OtherGildFound { get; init; }
        /// <summary>Acceptor is the gild owner (*(*(gild+4)+24) == acceptor) -> else 555.</summary>
        public bool AcceptorIsGildOwner { get; init; }
        /// <summary>save_relation result (sub_5E6E60); 0 = success, else 12/14/15 surface verbatim.</summary>
        public int SaveRelationResult { get; init; }

        // --- JoinCorps.accept (sub_707468) ---
        /// <summary>Target corp resolved (sub_5EA444).</summary>
        public bool CorpFound { get; init; }
        /// <summary>Corp full (sub_705690) -> 16.</summary>
        public bool CorpFull { get; init; }
        /// <summary>Acceptor is a corp leader (req[3]/[4]/[5] == acceptor) -> else 555.</summary>
        public bool AcceptorIsCorpLeader { get; init; }

        // --- JoinCorps.refuse (sub_7077C4) ---
        /// <summary>Refuser is a corp leader (req[3]/[4]/[5] == refuser, with CorpFound) -> else 555.</summary>
        public bool RefuserIsCorpLeader { get; init; }
    }

    public static class NativeGildRequestSubtypeTransaction
    {
        public const int VtblType = 0x00;   // req.type()
        public const int VtblAccept = 0x14; // req accept
        public const int VtblRefuse = 0x18; // req refuse

        // Result codes.
        public const int NoPermission = 555;    // absent request / not owner / not leader / wrong owner
        public const int NoGild = 12;           // gild lookup failed
        public const int MemberLimit = 13;      // join-gild accept: member limit reached
        public const int NoApplicantOrCorp = 5; // applicant / corp / owner-corp missing (all code 5)
        public const int ApplicantInGild = 6;   // join-gild accept: applicant already in a gild
        public const int WriteFailed = 1000;    // join-gild accept: add failed
        public const int CorpFullCode = 16;     // corp accept: corp full
        public const int Success = 0;

        // Type discriminator values (req.[vtbl+0x00]()).
        public const int TypeJoinCorps = 0;
        public const int TypeJoinGild = 1;
        public const int TypeUnion = 2;

        /// <summary>Raw result code produced by the request subtype's accept/refuse method.</summary>
        public static int Evaluate(
            NativeGildRequestSubtype subtype,
            NativeGildRequestSubtypeOp op,
            NativeGildRequestSubtypeContext c)
        {
            switch (subtype)
            {
                case NativeGildRequestSubtype.JoinCorps:
                    return op == NativeGildRequestSubtypeOp.Accept ? JoinCorpsAccept(c) : JoinCorpsRefuse(c);
                case NativeGildRequestSubtype.JoinGild:
                    return op == NativeGildRequestSubtypeOp.Accept ? JoinGildAccept(c) : JoinGildRefuse(c);
                case NativeGildRequestSubtype.Union:
                    return op == NativeGildRequestSubtypeOp.Accept ? UnionAccept(c) : UnionRefuse(c);
                default:
                    return NoPermission;
            }
        }

        // True iff the subtype's ACCEPT ladder passes every PRE-MUTATION gate and reaches the terminal
        // write (JoinGild add-to-gild sub_706264 / Union save_relation sub_5E6E60 / JoinCorps add-to-corp).
        // The service performs the write ONLY when this holds, then feeds AddToGildOk / SaveRelationResult
        // back to Evaluate for the terminal code. Single source: mirrors each accept ladder's gates above
        // exactly (up to, but not including, the mutation-dependent terminal branch).
        public static bool AcceptReachesMutation(
            NativeGildRequestSubtype subtype, NativeGildRequestSubtypeContext c)
        {
            switch (subtype)
            {
                case NativeGildRequestSubtype.JoinGild: // sub_707D9C up to the sub_706264 add
                    return c.RequestPresent && c.GildFound
                        && !c.GildMemberLimitReached && c.ApplicantFound
                        && !c.ApplicantAlreadyInGild;
                case NativeGildRequestSubtype.Union:    // sub_708168 up to sub_5E6E60 save_relation
                    return c.RequestPresent && c.GildFound && c.OtherGildFound
                        && c.AcceptorIsGildOwner;
                case NativeGildRequestSubtype.JoinCorps: // sub_707468 up to the corp add
                    return c.RequestPresent && c.CorpFound && !c.CorpFull
                        && c.AcceptorIsCorpLeader;
                default:
                    return false;
            }
        }

        // type 1 accept, sub_707D9C @0x00707D9C: 555 / 12 / 13 / 5 / 6 / 1000 / 0.
        private static int JoinGildAccept(NativeGildRequestSubtypeContext c)
        {
            if (!c.RequestPresent) return NoPermission;              // 555
            if (!c.GildFound) return NoGild;                         // 12
            if (c.GildMemberLimitReached) return MemberLimit;        // 13
            if (!c.ApplicantFound) return NoApplicantOrCorp;         // 5
            if (c.ApplicantAlreadyInGild) return ApplicantInGild;    // 6
            return c.AddToGildOk ? Success : WriteFailed;            // 0 / 1000
        }

        // type 1 refuse, sub_708520 @0x00708520 (TGildRequest, inherited by TJoinGildRequest): 555 / 12 / 5 / 0.
        private static int JoinGildRefuse(NativeGildRequestSubtypeContext c)
        {
            if (!c.RequestPresent) return NoPermission;    // 555
            if (!c.GildFound) return NoGild;               // 12
            if (!c.GildHasOwnerCorp) return NoApplicantOrCorp; // 5 (gild has no owner-corp)
            return Success;                                // 0
        }

        // type 2 accept, sub_708168 @0x00708168: 555 / 12 / 12 / 555 / <save_relation>.
        private static int UnionAccept(NativeGildRequestSubtypeContext c)
        {
            if (!c.RequestPresent) return NoPermission;    // 555
            if (!c.GildFound) return NoGild;               // 12 (gild A)
            if (!c.OtherGildFound) return NoGild;          // 12 (gild B)
            if (!c.AcceptorIsGildOwner) return NoPermission; // 555
            return c.SaveRelationResult;                   // sub_5E6E60 (0 = success)
        }

        // type 2 refuse, sub_708004 @0x00708004: 555 / 12 / 5 / 0.
        private static int UnionRefuse(NativeGildRequestSubtypeContext c)
        {
            if (!c.RequestPresent) return NoPermission;    // 555
            if (!c.GildFound) return NoGild;               // 12
            if (!c.GildHasOwnerCorp) return NoApplicantOrCorp; // 5
            return Success;                                // 0
        }

        // type 0 accept, sub_707468 @0x00707468: 555 / 5 / 16 / 555 / 0.
        private static int JoinCorpsAccept(NativeGildRequestSubtypeContext c)
        {
            if (!c.RequestPresent) return NoPermission;    // 555
            if (!c.CorpFound) return NoApplicantOrCorp;    // 5
            if (c.CorpFull) return CorpFullCode;           // 16
            if (!c.AcceptorIsCorpLeader) return NoPermission; // 555
            return Success;                                // 0
        }

        // type 0 refuse, sub_7077C4 @0x007077C4: 555 / 0 (default 555; a corp leader refusing yields 0).
        private static int JoinCorpsRefuse(NativeGildRequestSubtypeContext c)
        {
            if (!c.RequestPresent) return NoPermission;    // 555
            if (!c.CorpFound || !c.RefuserIsCorpLeader) return NoPermission; // 555
            return Success;                                // 0
        }
    }
}
