namespace GameSvr
{
    // Dormant model of the Gild join-request write op 4560 (request_join): a corps captain asking to
    // enroll their corps into a target gild. Hex-Rays + raw disassembly verified against M2Server
    // (image base 0x00400000). The live 45xx handlers still fail-closed to 1000 in C# pending the Gild
    // store; this models the exact result ladder + per-role dispatch only, performs no writes, is not wired.
    //
    // Handler sub_6F5958 @0x006F5958:
    //   n12 = 12;
    //   if ( sub_5E76D4(target) )                          // @0x005E76D4 target gild lookup
    //   {
    //       strat = sub_6ADA3C(caller);                    // @0x006ADA3C role dispatch
    //       n12 = (*(strat + 0x40))(callerKeyLo, callerKeyHi);  // vtable slot +0x40 (64); edx = targetGild
    //   }
    //   player.[vtbl+0x254]( SM=0x11D0=4560, len=8, &targetBody, 0,0, n12 );   // buffered reply (offset 596)
    //   => target gild not found -> 12 for ANY role (checked before role dispatch).
    //
    // Per-role strategy slot +0x40 (six VMTs @0x007014C4..0x007018EC). Enum<->VMT map is the codebase
    // convention pinned in NativeGildRequestUnionTransaction.cs (corps = corps_vice_owner,
    // gild_member = corps_owner):
    //   NoCorps (no_corps) / Member (member) / Corps (corps_vice_owner)
    //        -> sub_701D04 @0x00701D04 = `return 555;` stub.                     -> 555
    //   GildMember (corps_owner) / GildVice (gild_vice_owner) / GildOwner (gild_owner)
    //        -> sub_703624 @0x00703624 (the real ladder below).
    //   => only a corps captain (GildMember) or a gild officer (GildVice/GildOwner) reaches sub_703624.
    //      In the live path the corps captain has no gild yet (create path) while GildVice/GildOwner are
    //      already in a gild, so they hit the "already in a gild" -> 6 branch inside sub_703624.
    //
    // sub_703624 @0x00703624 (__userpurge; caller key in a3):
    //   n5 = 5;
    //   v5 = sub_5EC030(callerKey)              @0x005EC030          // resolve player object
    //   if ( !v5 || *(v5+0x18) != callerKey )   n5 stays 5;          // not resolved / not-self -> 5
    //   else if ( *(v5+4) )                     n5 = 6;              // caller already in a gild
    //   else {
    //       strat.[vtbl+0x70](corpsKey);                            // side-effect: clear stale pending (return unused)
    //       if ( sub_6A52A0(corpsKey) )         n5 = 8;             // @0x006A52A0 duplicate pending request
    //       else {
    //           sub_7073C4(...);                                    // build request object
    //           n5 = sub_6A4F80()               @0x006A4F80;        // request-manager add; 0 => success
    //           if ( !n5 ) { sub_706290(); sub_70570C(); sub_6F769C(); }  // publish/refresh/broadcast
    //       }
    //   }
    //   return n5;
    //
    // sub_6A4F80 delegates to the request-manager add and returns its code (0 on success); its tail is
    // polymorphic, modelled here as ManagerResult (0 = success). No DB write: 4560 creates only an
    // in-memory pending join request (per the native write inventory). Reuses NativeGildRole (declared in
    // NativeGildViceTransaction.cs).

    public sealed class NativeGildRequestJoinContext
    {
        /// <summary>Caller's resolved Gild role (sub_6ADA3C). Only GildMember/GildVice/GildOwner reach sub_703624.</summary>
        public NativeGildRole Role { get; init; }

        /// <summary>Handler gate: target gild resolved (sub_5E76D4 != 0). False -> 12 for any role.</summary>
        public bool TargetGildFound { get; init; }

        /// <summary>Player object resolved (sub_5EC030) AND is the caller (player[+0x18] == callerKey). Defensive; defaults true.</summary>
        public bool HasPlayer { get; init; } = true;

        /// <summary>Caller already belongs to a Gild (playerObject[+4] != 0) -> "already in a gild" (6).</summary>
        public bool HasGild { get; init; }

        /// <summary>A duplicate pending join request already exists (sub_6A52A0 != 0) -> 8.</summary>
        public bool DuplicateRequest { get; init; }

        /// <summary>Request-manager add result (sub_6A4F80). 0 = success (creates pending + publishes). Polymorphic; abstracted as an input.</summary>
        public int ManagerResult { get; init; }
    }

    public static class NativeGildRequestJoinTransaction
    {
        public const int ReplySmId = 4560;         // 0x11D0 buffered reply ident
        public const int VtblStrategy = 0x40;      // per-role strategy slot invoked by the handler (64)
        public const int VtblBufferedSend = 0x254; // player buffered send (offset 596) carrying &targetBody

        public const int TargetNotFound = 12;      // 0x0C  sub_5E76D4 == 0 (handler gate)
        public const int NoPermission = 555;       // 0x22B non-captain role (sub_701D04 stub)
        public const int NoPlayer = 5;             // sub_5EC030 == 0 or caller not self
        public const int AlreadyInGild = 6;        // playerObject[+4] != 0
        public const int DuplicatePending = 8;     // sub_6A52A0 != 0
        public const int Success = 0;              // sub_6A4F80 == 0 (pending request created)

        /// <summary>Raw result code that goes verbatim into the SM 4560 buffered reply.</summary>
        public static int Evaluate(NativeGildRequestJoinContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            // Handler gate: sub_5E76D4 target lookup precedes role dispatch. Not found -> 12 for any role.
            if (!context.TargetGildFound)
                return TargetNotFound;

            return Strategy(context);
        }

        /// <summary>True when the success path created a pending join request (and published/refreshed).</summary>
        public static bool CreatesPendingRequest(int result) => result == Success;

        // +0x40 dispatch: NoCorps/Member/Corps(=corps_vice_owner) -> sub_701D04 (555); the three
        // captain/officer roles -> sub_703624.
        private static int Strategy(NativeGildRequestJoinContext c)
        {
            if (c.Role != NativeGildRole.GildMember &&
                c.Role != NativeGildRole.GildVice &&
                c.Role != NativeGildRole.GildOwner)
                return NoPermission;

            return CaptainStrategy(c);
        }

        // sub_703624 @0x00703624, shared by GildMember (corps_owner) / GildVice / GildOwner.
        private static int CaptainStrategy(NativeGildRequestJoinContext c)
        {
            if (!c.HasPlayer) return NoPlayer;              // 5  (not resolved / not-self)
            if (c.HasGild) return AlreadyInGild;            // 6  (playerObject[+4] != 0)
            if (c.DuplicateRequest) return DuplicatePending; // 8  (sub_6A52A0 != 0)
            return c.ManagerResult;                         // 0 = success (+ publish), else manager error
        }
    }
}
