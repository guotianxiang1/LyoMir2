namespace GameSvr
{
    // Dormant model of the Gild alliance-request write op 4573 (request_union). Hex-Rays/disasm
    // verified against M2Server (image base 0x00400000). The live 45xx handlers still fail-closed to
    // 1000 in C# pending the Gild store (relation persistence + DB command FIFO); this models the
    // exact result ladder and role dispatch only, performs no writes, and is not wired.
    //
    // Handler sub_6F6390 @0x006F6390 (4573 request_union):
    //   n12 = 12;                                  // pre-initialised default
    //   if ( sub_5E76F0() ) {                      // precondition gate @0x005E76F0 (see note below)
    //       v = sub_706914(caller);                //   current-gild handle @0x00706914
    //       strat = sub_6ADA3C(caller);            //   role strategy @0x006ADA3C
    //       n12 = (*(strat + 0x64))(target, own);  //   invoke vtable slot +0x64 (100 decimal)
    //   }
    //   SendDefMessage(4573, wParam=n12, 0,0,0) via player.[vtbl+0x250] (offset 592).
    //
    // Role strategy slot +0x64 (from the six VMTs @0x007014C4..0x007018EC):
    //   gild_owner   -> sub_704494 @0x00704494  (the real ladder, below)
    //   every other role (no_corps / member / corps=corps_vice_owner / gild_member=corps_owner /
    //     gild_vice=gild_vice_owner) -> sub_701D10 @0x00701D10 which is a `return 555;` stub.
    //   => only the president (GildOwner) reaches the ladder; all other roles return 555.
    //
    // sub_5E76F0 precondition (@0x005E76F0): builds a temp via sub_40BC50 then returns sub_49F5F4().
    //   Its internal (a name/context lookup) is abstracted here as an input; when it is false the
    //   handler sends the pre-initialised n12 = 12 for ANY role (same numeric code as the inner
    //   no-gild branch in sub_704494). Defaults to met so the common path reaches role dispatch.
    //
    // gild_owner ladder sub_704494 @0x00704494 (__stdcall, args = target-gild id, caller id):
    //   v4 = sub_5EC030(caller)            @0x005EC030  -> !v4                         : 5
    //   v6 = *(v4 + 4)  (player gild ptr)                -> !v6                         : 12
    //   v7 = sub_5E76D4(target)            @0x005E76D4  -> !v7                         : 25
    //                                                     -> v7 == v6 (ally with self) : 19
    //   *(v7 + 0x28) union-allowed flag                 -> flag == 0                   : 34
    //   rel = sub_5E7890(target, own)      @0x005E7890  -> rel == 1 (already allied)   : 15
    //                                                     -> rel == 2 (currently at war): 33
    //   // rel is 0 or >=3: create the request.
    //   sub_5E6E60(...)                    @0x005E6E60  -> save_relation: INSERT GildRelation(Relation=3)
    //                                                        (return IGNORED; runs BEFORE the dup check)
    //   if sub_7065B0(...)                 @0x007065B0  -> duplicate pending request    : 8
    //   n19 = sub_6A4F80(...)              @0x006A4F80  -> request-manager add result; 0 => success+publish
    //
    // NOTE (documented, not a bug in the model): sub_5E6E60 enqueues the Relation=3 INSERT command
    // before sub_7065B0 is consulted, so an observable return of 8 can still leave a DB row queued.
    // We model the observable top-level return code (8); the DB side-effect is out of scope here.
    //
    // sub_6A4F80 delegates to sub_6A4FB8 (request-object add) and returns its code (0 on success),
    // so the create-request tail is polymorphic: modelled as the ManagerResult input (0 = success).
    //
    // Reuses NativeGildRole (declared in NativeGildViceTransaction.cs).

    public sealed class NativeGildRequestUnionContext
    {
        /// <summary>Caller's resolved Gild role (sub_6ADA3C). Only GildOwner reaches sub_704494.</summary>
        public NativeGildRole Role { get; init; }

        /// <summary>sub_5E76F0 precondition gate; when false the handler returns the default 12 for any role.</summary>
        public bool PreconditionMet { get; init; } = true;

        /// <summary>Player object present (sub_5EC030 != null); defensive, always true on the live path.</summary>
        public bool HasPlayer { get; init; } = true;

        /// <summary>Caller has a Gild (player[1] i.e. *(obj+4) != 0).</summary>
        public bool HasGild { get; init; }

        /// <summary>Target Gild resolved (sub_5E76D4 != null).</summary>
        public bool TargetGildFound { get; init; }

        /// <summary>Target Gild is the caller's own Gild (v7 == v6): cannot ally with self.</summary>
        public bool TargetIsOwnGild { get; init; }

        /// <summary>Target Gild's union-allowed flag byte *(target+0x28) is set.</summary>
        public bool TargetAllowsUnion { get; init; }

        /// <summary>Existing relation type between the two gilds (sub_5E7890 raw return):
        /// 1 = allied/union (-&gt;15), 2 = war (-&gt;33), anything else (0 or &gt;=3) proceeds to create.</summary>
        public int ExistingRelation { get; init; }

        /// <summary>A pending union request already exists (sub_7065B0). Checked AFTER the INSERT; -&gt; 8.</summary>
        public bool DuplicatePending { get; init; }

        /// <summary>Request-manager add result (sub_6A4F80 -&gt; sub_6A4FB8), polymorphic. 0 = success.</summary>
        public int ManagerResult { get; init; }
    }

    public static class NativeGildRequestUnionTransaction
    {
        public const int Ident = 4573;               // request_union protocol ident
        public const int VtblRequestUnion = 0x64;    // role-strategy slot invoked by sub_6F6390 (100)
        public const int VtblSendDefMessage = 0x250; // player SendDefMessage (offset 592)

        // Relation-type inputs from sub_5E7890.
        public const int RelationAllied = 1;         // -> AlreadyAllied (15)
        public const int RelationWar = 2;            // -> AtWar (33)

        // Result-code ladder (verbatim SendDefMessage wParam).
        public const int GateDefault = 12;           // sub_5E76F0 false -> pre-initialised n12
        public const int NoPermission = 555;         // non-owner strategy slot (sub_701D10 stub)
        public const int NoPlayer = 5;               // sub_5EC030 == null
        public const int GildEmpty = 12;             // caller has no gild (player[1] == 0)
        public const int TargetNotFound = 25;        // sub_5E76D4 == null
        public const int TargetIsSelf = 19;          // target gild == own gild
        public const int TargetDisallowsUnion = 34;  // target union-allowed flag clear
        public const int AlreadyAllied = 15;         // sub_5E7890 == 1
        public const int AtWar = 33;                 // sub_5E7890 == 2
        public const int DuplicatePendingRequest = 8;// sub_7065B0 (post-INSERT)
        public const int Success = 0;                // sub_6A4F80 == 0

        /// <summary>Raw result code sent to the client (SendDefMessage wParam) for op 4573.</summary>
        public static int Evaluate(NativeGildRequestUnionContext c)
        {
            // sub_6F6390: default 12, then gate, then role-strategy dispatch of slot +0x64.
            if (!c.PreconditionMet)
                return GateDefault;

            // Slot +0x64: only gild_owner -> sub_704494; every other role -> sub_701D10 (555).
            if (c.Role != NativeGildRole.GildOwner)
                return NoPermission;

            return GildOwner(c);
        }

        // gild_owner slot +0x64 = sub_704494 @0x00704494.
        private static int GildOwner(NativeGildRequestUnionContext c)
        {
            if (!c.HasPlayer) return NoPlayer;                       // 5
            if (!c.HasGild) return GildEmpty;                        // 12
            if (!c.TargetGildFound) return TargetNotFound;           // 25
            if (c.TargetIsOwnGild) return TargetIsSelf;              // 19
            if (!c.TargetAllowsUnion) return TargetDisallowsUnion;   // 34
            if (c.ExistingRelation == RelationAllied) return AlreadyAllied; // 15
            if (c.ExistingRelation == RelationWar) return AtWar;     // 33

            // Create-request path. sub_5E6E60 enqueues the Relation=3 INSERT here (its return is
            // discarded), BEFORE the duplicate-pending probe below -- an 8 can still queue a row.
            if (c.DuplicatePending) return DuplicatePendingRequest;  // 8

            // Polymorphic request-manager result (0 = success + publish).
            return c.ManagerResult;
        }
    }
}
