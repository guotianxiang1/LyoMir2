namespace GameSvr
{
    // Dormant model of three Gild leadership write ops (transfer/appoint are
    // president-only; dismiss is president-OR-vice), role-strategy dispatched via
    // sub_6ADA3C then SendDefMessage(ident, wParam=result, 0,0,0). Hex-Rays verified; fail-closed in
    // C# (live handlers return 1000 pending the Gild store). Reuses NativeGildRole.
    //
    //   4568 transfer_president -> gild_owner[+0x48] sub_7046A8 @0x007046A8; other roles 555.
    //     bad args 18; no player 5; not president 555; no gild 12; target is self 19;
    //     target not a member 18; else 0 (transfer + clear vice if target was vice + UPDATE Gild).
    //   4569 appoint_vice -> gild_owner[+0x50] sub_7039F8 @0x007039F8; other roles 555.
    //     bad args 18; no player 5; not president 555; no gild 12; vice slot occupied 21;
    //     target is self 19; target not found 25; target not in this gild 22; else 0 (UPDATE Gild).
    //   4567 dismiss_corps -> gild_owner/gild_vice[+0x54] sub_704AF8 @0x00704AF8; other roles 555.
    //     (the +0x54 slot is shared by BOTH gild_owner and gild_vice strategies; all other roles' +0x54
    //      is a 555 stub, so a Gild vice may also dismiss a corps.)
    //     no player 5; no gild 12; target not found 7; target not in this gild 22; target is self 19;
    //     target is president/vice 555; target not removable 18; remove failed 1000; else 0 (DELETE member).

    public enum NativeGildLeadershipOp
    {
        DismissCorps = 4567,
        TransferPresident = 4568,
        AppointVice = 4569,
    }

    public sealed class NativeGildLeadershipContext
    {
        public NativeGildRole Role { get; init; }
        public bool ValidArgs { get; init; } = true;   // 4568/4569: a3 && (a2 || a1)
        public bool HasPlayer { get; init; } = true;
        public bool IsPresident { get; init; }          // *(player+0x18) == caller
        public bool HasGild { get; init; }
        public bool ViceOccupied { get; init; }          // 4569: gild[8] != 0
        public bool TargetIsSelf { get; init; }
        public bool TargetFound { get; init; }           // sub_5EA444 != null
        public bool TargetSameGild { get; init; }        // target gild == caller gild
        public bool TargetIsMember { get; init; }        // 4568: sub_7063E8 >= 0
        public bool TargetIsLeadership { get; init; }    // 4567: target == president or vice
        public bool TargetRemovable { get; init; }       // 4567: sub_7063E8 >= 0
        public bool RemoveOk { get; init; }              // 4567: sub_706464
    }

    public static class NativeGildLeadershipTransaction
    {
        public const int VtblTransfer = 0x48;
        public const int VtblAppointVice = 0x50;
        public const int VtblDismissCorps = 0x54;
        public const int VtblSendDefMessage = 0x250;

        public const int BadArgs = 18;
        public const int NoPlayer = 5;
        public const int NoPermission = 555;
        public const int GildEmpty = 12;
        public const int ViceSlotOccupied = 21;
        public const int TargetIsSelfCode = 19;
        public const int TargetNotFound25 = 25;
        public const int TargetNotFound7 = 7;
        public const int WrongGild = 22;
        public const int NotMember = 18;
        public const int WriteFailed = 1000;
        public const int Success = 0;

        public static int Evaluate(NativeGildLeadershipOp op, NativeGildLeadershipContext c)
        {
            switch (op)
            {
                case NativeGildLeadershipOp.TransferPresident: return Transfer(c);
                case NativeGildLeadershipOp.AppointVice: return AppointVice(c);
                case NativeGildLeadershipOp.DismissCorps: return DismissCorps(c);
                default: return NoPermission;
            }
        }

        private static int Transfer(NativeGildLeadershipContext c)
        {
            if (c.Role != NativeGildRole.GildOwner) return NoPermission;
            if (!c.ValidArgs) return BadArgs;
            if (!c.HasPlayer) return NoPlayer;
            if (!c.IsPresident) return NoPermission;
            if (!c.HasGild) return GildEmpty;
            if (c.TargetIsSelf) return TargetIsSelfCode;
            return c.TargetIsMember ? Success : NotMember;
        }

        private static int AppointVice(NativeGildLeadershipContext c)
        {
            if (c.Role != NativeGildRole.GildOwner) return NoPermission;
            if (!c.ValidArgs) return BadArgs;
            if (!c.HasPlayer) return NoPlayer;
            if (!c.IsPresident) return NoPermission;
            if (!c.HasGild) return GildEmpty;
            if (c.ViceOccupied) return ViceSlotOccupied;
            if (c.TargetIsSelf) return TargetIsSelfCode;
            if (!c.TargetFound) return TargetNotFound25;
            return c.TargetSameGild ? Success : WrongGild;
        }

        private static int DismissCorps(NativeGildLeadershipContext c)
        {
            // Shared +0x54 slot sub_704AF8 is reached by BOTH the gild_owner and
            // gild_vice strategies (every other role's +0x54 is a 555 stub), so a
            // Gild vice may dismiss a corps; the rest of the ladder is identical.
            if (c.Role != NativeGildRole.GildOwner
                && c.Role != NativeGildRole.GildVice) return NoPermission;
            if (!c.HasPlayer) return NoPlayer;
            if (!c.HasGild) return GildEmpty;
            if (!c.TargetFound) return TargetNotFound7;
            if (!c.TargetSameGild) return WrongGild;
            if (c.TargetIsSelf) return TargetIsSelfCode;
            if (c.TargetIsLeadership) return NoPermission;
            if (!c.TargetRemovable) return NotMember;
            return c.RemoveOk ? Success : WriteFailed;
        }
    }
}
