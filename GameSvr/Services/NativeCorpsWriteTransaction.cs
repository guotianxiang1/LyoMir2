namespace GameSvr
{
    // Dormant model of the unmodeled Corps (team-guild) write ops 4522-4532 (4533 direct-add and 4538
    // exit are already covered elsewhere; the reads 4501/4520/4521/4525/4531 are excluded, see below).
    // Corps uses the SAME sub_6ADA3C @0x006ADA3C role dispatch as Gild (roles no_corps / member /
    // corps_vice_owner / corps_owner / gild_vice_owner / gild_owner), just different vtable slots.
    // Hex-Rays + raw disassembly verified against M2Server (image base 0x00400000). The live handlers
    // already run in native M2; this C# model captures the exact result ladder + per-role dispatch,
    // performs no writes, and is not wired.
    //
    // Enum<->VMT role map (codebase convention, per NativeGildRequestUnionTransaction.cs):
    //   NativeGildRole.NoCorps=no_corps  Member=member  Corps=corps_vice_owner
    //   GildMember=corps_owner  GildVice=gild_vice_owner  GildOwner=gild_owner
    //
    // Each handler does: result = sub_6ADA3C(caller).[vtbl+SLOT](args); then SendDefMessage(SM=op,
    // wParam=result) via player.[vtbl+0x250]. A role whose SLOT resolves to a `mov eax,0x22B; ret` stub
    // yields 555. Dispatcher sub_6D7D68 forwards each SM to the handler below.
    //
    // Handler / slot / gate / per-role dispatch (555 = stub; else the real method the role reaches):
    //  4522 request_join    sub_6ADBB4 @0x006ADBB4  slot +0x10  gate sub_5EA444(target)==0 -> 7
    //        NoCorps -> sub_701C58 @0x00701C58 (real: precondition-fail 9, else sub_6A4F80 add 0/err)
    //        Member/Corps/GildMember/GildVice/GildOwner -> sub_702C58 = 555
    //  4523 cancel_join     sub_6ADAF8 @0x006ADAF8  slot +0x14  gate sub_6A52A0(caller)==0 -> 10
    //        ALL roles -> sub_7019F0 @0x007019F0 (real: re-check 10, else request.[+0x28] result)
    //  4524 create          sub_6ADD08 @0x006ADD08  slot +0x08  gate a1[698]!=0 (already in a corps) -> 3
    //        NoCorps -> sub_701A74 @0x00701A74 (real: create-manager result). Every other role is gated
    //        to 3 first, so its +0x08 stub sub_7029FC (555) is unreachable.
    //  4526 set_member_title sub_6F53CC @0x006F53CC slot +0x20  precondition msg-len >= 24 (else no reply)
    //        NoCorps/Member -> sub_701A2C = 555
    //        Corps/GildMember/GildVice/GildOwner -> sub_701D88 @0x00701D88 (real: 18 / 555 / ... / 0)
    //  4527 dismiss_member  sub_6F5464 @0x006F5464  slot +0x34
    //        NoCorps/Member -> sub_701BF8 = 555
    //        Corps/GildMember/GildVice/GildOwner -> sub_70253C @0x0070253C (real: 555 / 19 / 18 / ... / 0)
    //  4528 transfer_captain sub_6F54A8 @0x006F54A8 slot +0x1C
    //        NoCorps/Member/Corps -> sub_701D28 = 555
    //        GildMember/GildVice/GildOwner -> sub_703784 @0x00703784 (real: 22 / 40 / 5 / ... / 0)
    //  4529 appoint_vice    sub_6F54EC @0x006F54EC  slot +0x2C
    //        NoCorps/Member/Corps -> sub_7019CC = 555
    //        GildMember/GildVice/GildOwner -> sub_702C60 @0x00702C60 (real: 555 / 5 / 18 / 31 / 21 / ... / 0)
    //  4530 stepdown        sub_6F5530 @0x006F5530  slot +0x38
    //        Corps (corps_vice_owner) ONLY -> sub_70209C @0x0070209C (real: 5 / 1000 / ... / 0)
    //        NoCorps/Member -> sub_701A68 = 555; GildMember/GildVice/GildOwner -> sub_702F80 = 555
    //        (i.e. only the corps vice-captain can resign the vice slot; the captain has no vice slot -> 555)
    //  4532 set_recruit     sub_6F5608 @0x006F5608  slot +0x28  precondition msg-len >= 60 (else no reply)
    //        NoCorps/Member -> sub_701A50 = 555
    //        Corps/GildMember/GildVice/GildOwner -> sub_702034 @0x00702034 (real: caller-key-absent 555 / no-player 5 / 0)
    //
    // The real management methods perform the corps mutation + enqueue the DB command + refresh/broadcast
    // and return 0 on success or a method-specific error; that terminal value is polymorphic and is
    // abstracted here as StrategyResult (0 = success), matching the other dormant models. The exact
    // role->(555 | real) ladder and the handler gates (7 / 10 / 3) above are modelled precisely.
    //
    // Excluded reads in this range (no role-strategy write ladder; data replies via player.[vtbl+0x254]):
    //   4501 player_corps sub_6F071C (5 if not in a corps), 4520 list sub_6AE108, 4521 query_join
    //   sub_6AED90, 4525 member_list sub_6F51D8 (5 if target not found), 4531 get_recruit sub_6F5574
    //   (5 if target not found).
    //
    // Reuses NativeGildRole (declared in NativeGildViceTransaction.cs).

    public enum NativeCorpsWriteOp
    {
        RequestJoin = 4522,     // sub_6ADBB4, slot +0x10
        CancelJoin = 4523,      // sub_6ADAF8, slot +0x14
        Create = 4524,          // sub_6ADD08, slot +0x08
        SetMemberTitle = 4526,  // sub_6F53CC, slot +0x20
        DismissMember = 4527,   // sub_6F5464, slot +0x34
        TransferCaptain = 4528, // sub_6F54A8, slot +0x1C
        AppointVice = 4529,     // sub_6F54EC, slot +0x2C
        StepDown = 4530,        // sub_6F5530, slot +0x38
        SetRecruit = 4532,      // sub_6F5608, slot +0x28
    }

    public sealed class NativeCorpsWriteContext
    {
        /// <summary>Caller's resolved role (sub_6ADA3C).</summary>
        public NativeGildRole Role { get; init; }

        /// <summary>4522 only: target corps resolved (sub_5EA444 != 0). False -> 7.</summary>
        public bool TargetFound { get; init; }

        /// <summary>4523 only: caller has a pending join request (sub_6A52A0 != 0). False -> 10.</summary>
        public bool HasPendingRequest { get; init; }

        /// <summary>4524 only: caller already belongs to a corps (a1[698] != 0, equivalent to Role != NoCorps). True -> 3.</summary>
        public bool HasCorpsMembership { get; init; }

        /// <summary>Terminal result of the permitted real management method (0 = success). Polymorphic; abstracted as input.</summary>
        public int StrategyResult { get; init; }
    }

    public static class NativeCorpsWriteTransaction
    {
        public const int NoPermission = 555;   // 0x22B stub return
        public const int Success = 0;

        // handler gate codes
        public const int RequestTargetNotFound = 7;  // 4522 sub_5EA444 == 0
        public const int NoPendingRequest = 10;      // 4523 sub_6A52A0 == 0
        public const int AlreadyInCorps = 3;         // 4524 a1[698] != 0

        public const int VtblSendDefMessage = 0x250; // all nine reply via player.[vtbl+0x250]

        // per-op strategy vtable slots (invoked on the sub_6ADA3C role object)
        public const int SlotCreate = 0x08;
        public const int SlotRequestJoin = 0x10;
        public const int SlotCancelJoin = 0x14;
        public const int SlotTransferCaptain = 0x1C;
        public const int SlotSetMemberTitle = 0x20;
        public const int SlotSetRecruit = 0x28;
        public const int SlotAppointVice = 0x2C;
        public const int SlotDismissMember = 0x34;
        public const int SlotStepDown = 0x38;

        /// <summary>Raw result code that goes verbatim into SendDefMessage wParam (SM = the op number).</summary>
        public static int Evaluate(NativeCorpsWriteOp op, NativeCorpsWriteContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            return op switch
            {
                NativeCorpsWriteOp.RequestJoin => RequestJoin(context),
                NativeCorpsWriteOp.CancelJoin => CancelJoin(context),
                NativeCorpsWriteOp.Create => Create(context),
                NativeCorpsWriteOp.SetMemberTitle => ManagerOp(context),
                NativeCorpsWriteOp.DismissMember => ManagerOp(context),
                NativeCorpsWriteOp.TransferCaptain => CaptainOp(context),
                NativeCorpsWriteOp.AppointVice => CaptainOp(context),
                NativeCorpsWriteOp.StepDown => StepDown(context),
                NativeCorpsWriteOp.SetRecruit => ManagerOp(context),
                _ => throw new ArgumentOutOfRangeException(nameof(op)),
            };
        }

        /// <summary>Member-management roles: corps vice-captain + corps captain + gild officers
        /// (Corps/GildMember/GildVice/GildOwner). NoCorps/Member get 555 at these slots.</summary>
        public static bool IsManager(NativeGildRole r) =>
            r == NativeGildRole.Corps || r == NativeGildRole.GildMember ||
            r == NativeGildRole.GildVice || r == NativeGildRole.GildOwner;

        /// <summary>Captain roles: corps captain + gild officers (GildMember/GildVice/GildOwner).
        /// NoCorps/Member/Corps get 555 at these slots.</summary>
        public static bool IsCaptain(NativeGildRole r) =>
            r == NativeGildRole.GildMember || r == NativeGildRole.GildVice ||
            r == NativeGildRole.GildOwner;

        // 4522: sub_5EA444 gate -> 7; only NoCorps reaches sub_701C58, every in-corps role hits sub_702C58 (555).
        private static int RequestJoin(NativeCorpsWriteContext c)
        {
            if (!c.TargetFound) return RequestTargetNotFound;
            return c.Role == NativeGildRole.NoCorps ? c.StrategyResult : NoPermission;
        }

        // 4523: sub_6A52A0 gate -> 10; every role dispatches to the shared sub_7019F0.
        private static int CancelJoin(NativeCorpsWriteContext c)
        {
            if (!c.HasPendingRequest) return NoPendingRequest;
            return c.StrategyResult;
        }

        // 4524: already-in-corps gate -> 3; only NoCorps reaches sub_701A74 (others gated before their 555 stub).
        private static int Create(NativeCorpsWriteContext c)
        {
            if (c.HasCorpsMembership) return AlreadyInCorps;
            return c.StrategyResult;
        }

        // 4526 / 4527 / 4532: NoCorps/Member -> 555; Corps/GildMember/GildVice/GildOwner -> real method.
        private static int ManagerOp(NativeCorpsWriteContext c) =>
            IsManager(c.Role) ? c.StrategyResult : NoPermission;

        // 4528 / 4529: NoCorps/Member/Corps -> 555; GildMember/GildVice/GildOwner -> real method.
        private static int CaptainOp(NativeCorpsWriteContext c) =>
            IsCaptain(c.Role) ? c.StrategyResult : NoPermission;

        // 4530: ONLY Corps (corps_vice_owner) reaches sub_70209C; every other role, INCLUDING the
        // captains (GildMember/GildVice/GildOwner -> sub_702F80 stub), returns 555.
        private static int StepDown(NativeCorpsWriteContext c) =>
            c.Role == NativeGildRole.Corps ? c.StrategyResult : NoPermission;
    }
}
