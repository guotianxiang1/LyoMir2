namespace GameSvr
{
    // Dormant WIRED composition of the Gild request-response ops 4611 (accept) / 4572 (refuse),
    // end-to-end with NO abstraction: outer role-gated cascade -> request TYPE -> the REAL subtype
    // accept/refuse ladders in NativeGildRequestSubtypeTransaction. Hex-Rays verified (m2full.i64,
    // image base 0x00400000). This is the de-abstracted counterpart of
    // NativeGildRequestResponseTransaction (which keeps the subtype result as an abstract input); that
    // file is left untouched. Reuses NativeGildRequestSubtype / NativeGildRequestSubtypeOp /
    // NativeGildRequestSubtypeContext and NativeGildRole; no enum is redefined.
    //
    // 4611 accept handler sub_6F62F0 -> role strategy slot +0x00; 4572 refuse sub_6F6340 -> slot +0x04.
    // The role's strategy method is a cascade that resolves the pending request (sub_6A5284; null -> 10),
    // reads req.type() (req.[vtbl+0x00]: TJoinCorpsRequest->0, TJoinGildRequest->1, TUnionGildRequest->2)
    // and invokes req.[vtbl+0x14] (accept) / req.[vtbl+0x18] (refuse) on the concrete subclass. The reply
    // goes out via player.[vtbl+0x254].
    //
    // Role-strategy entry cascade (accept fns; refuse is the exact mirror via +0x04):
    //   no_corps / member                      -> sub_7019C0            : `return 555` (no request lookup)
    //   corps / gild_member (corps_owner)      -> sub_701D40 @0x00701D40: null 10; type()==0 -> req.accept; else 23
    //   gild_vice (gild_vice_owner)            -> sub_704930 @0x00704930: null 10; type()==1 -> req.accept; else sub_701D40
    //   gild_owner                             -> sub_7039A0 @0x007039A0: null 10; type()==2 -> req.accept; else sub_704930
    //   (refuse mirror: sub_70443C -> sub_704E54 -> sub_7029B4, using req.[vtbl+0x18])
    //
    // So the entry layer (role) bounds the highest request type reachable:
    //   gild_owner  reaches Union(2), JoinGild(1), JoinCorps(0)
    //   gild_vice   reaches            JoinGild(1), JoinCorps(0)   (a Union request -> 23)
    //   corps       reaches                         JoinCorps(0)   (a JoinGild/Union request -> 23 = the
    //                                                               "corps-vs-gild mismatch")
    //   no_corps/member -> 555 always.
    // A request whose type() is none of 0/1/2 also lands on the corp layer's else -> 23 (defensive).
    //
    // Composed final ladders (outer code, then the subtype ladder verbatim):
    //   request not found            -> 10
    //   entry too low for the type   -> 23
    //   role no_corps/member         -> 555
    //   accept type 2 Union    sub_708168 : 555 / 12 / 12 / 555 / <save_relation sub_5E6E60>
    //   accept type 1 JoinGild sub_707D9C : 555 / 12 / 13 / 5 / 6 / 1000 / 0
    //   accept type 0 JoinCorps sub_707468: 555 / 5 / 16 / 555 / 0
    //   refuse type 2 Union    sub_708004 : 555 / 12 / 5 / 0
    //   refuse type 1 JoinGild sub_708520 : 555 / 12 / 5 / 0
    //   refuse type 0 JoinCorps sub_7077C4: 555 / 0
    // (Only the Union-accept save_relation tail remains a NativeGildRequestSubtypeContext.SaveRelationResult
    // input, per the subtype model; everything else is a concrete branch.)

    public static class NativeGildRequestResponseWiredTransaction
    {
        public const int RequestNotFound = 10;  // sub_6A5284 == null at any resolving layer
        public const int WrongType = 23;         // 0x17, corp-layer else (type unreachable from this entry)
        public const int NoPermission = 555;     // no_corps/member strategy stub sub_7019C0

        // Reuse the subtype model's type discriminator constants.
        public const int TypeJoinCorps = NativeGildRequestSubtypeTransaction.TypeJoinCorps; // 0
        public const int TypeJoinGild = NativeGildRequestSubtypeTransaction.TypeJoinGild;   // 1
        public const int TypeUnion = NativeGildRequestSubtypeTransaction.TypeUnion;         // 2

        /// <summary>
        /// Entry layer of a role's accept(+0x00)/refuse(+0x04) strategy in the cascade:
        /// 0 = sub_7019C0 stub (555, no request lookup), 1 = corp sub_701D40, 2 = gild-vice sub_704930,
        /// 3 = gild-owner sub_7039A0. Level N reaches request type N and everything below it.
        /// </summary>
        public static int EntryLevel(NativeGildRole role) => role switch
        {
            NativeGildRole.NoCorps => 0,
            NativeGildRole.Member => 0,
            NativeGildRole.Corps => 1,       // corps_vice_owner
            NativeGildRole.GildMember => 1,  // corps_owner
            NativeGildRole.GildVice => 2,    // gild_vice_owner
            NativeGildRole.GildOwner => 3,   // gild_owner
            _ => 0,
        };

        /// <summary>
        /// Full role-gated composition. <paramref name="requestType"/> is the raw req.type() value
        /// (0 JoinCorps / 1 JoinGild / 2 Union; any other value -&gt; 23). Returns the REAL final code.
        /// </summary>
        public static int Evaluate(
            NativeGildRequestSubtypeOp op,
            NativeGildRole role,
            int requestType,
            bool requestFound,
            NativeGildRequestSubtypeContext subtypeContext)
        {
            int level = EntryLevel(role);
            if (level == 0) return NoPermission;        // sub_7019C0 stub: 555 before any lookup
            if (!requestFound) return RequestNotFound;  // sub_6A5284 == null -> 10

            // Cascade union(2)@owner -> join-gild(1)@vice -> join-corps(0)@corp; unreachable type -> 23.
            if (level >= 3 && requestType == TypeUnion)
                return Subtype(NativeGildRequestSubtype.Union, op, subtypeContext);
            if (level >= 2 && requestType == TypeJoinGild)
                return Subtype(NativeGildRequestSubtype.JoinGild, op, subtypeContext);
            if (level >= 1 && requestType == TypeJoinCorps)
                return Subtype(NativeGildRequestSubtype.JoinCorps, op, subtypeContext);
            return WrongType;                           // 23
        }

        /// <summary>
        /// True iff the full role-gated ACCEPT reaches the subtype's terminal write: the outer cascade
        /// routes to the subtype (role entry-level high enough + request found) AND the subtype's
        /// pre-mutation gates pass. The service performs the write only then, then feeds AddToGildOk /
        /// SaveRelationResult and calls <see cref="Evaluate(NativeGildRequestSubtypeOp,NativeGildRole,int,bool,NativeGildRequestSubtypeContext)"/>
        /// for the terminal code. Mirrors the routing above; no gate duplication in the service.
        /// </summary>
        public static bool AcceptReachesMutation(
            NativeGildRole role,
            int requestType,
            bool requestFound,
            NativeGildRequestSubtypeContext subtypeContext)
        {
            int level = EntryLevel(role);
            if (level == 0 || !requestFound) return false;
            NativeGildRequestSubtype subtype;
            if (level >= 3 && requestType == TypeUnion)
                subtype = NativeGildRequestSubtype.Union;
            else if (level >= 2 && requestType == TypeJoinGild)
                subtype = NativeGildRequestSubtype.JoinGild;
            else if (level >= 1 && requestType == TypeJoinCorps)
                subtype = NativeGildRequestSubtype.JoinCorps;
            else
                return false;                              // WrongType-23: no mutation
            return NativeGildRequestSubtypeTransaction.AcceptReachesMutation(
                subtype, subtypeContext);
        }

        /// <summary>
        /// President (gild_owner) full-chain convenience matching the (op, requestType, subtype-context)
        /// shape: the maximal cascade sub_7039A0 / sub_70443C that can reach every request type.
        /// </summary>
        public static int Evaluate(
            NativeGildRequestSubtypeOp op,
            int requestType,
            bool requestFound,
            NativeGildRequestSubtypeContext subtypeContext) =>
            Evaluate(op, NativeGildRole.GildOwner, requestType, requestFound, subtypeContext);

        private static int Subtype(
            NativeGildRequestSubtype subtype,
            NativeGildRequestSubtypeOp op,
            NativeGildRequestSubtypeContext c) =>
            NativeGildRequestSubtypeTransaction.Evaluate(subtype, op, c);
    }
}
