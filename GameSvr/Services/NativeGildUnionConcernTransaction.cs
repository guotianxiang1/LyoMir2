namespace GameSvr
{
    // Dormant model of three more Gild write ops (role-strategy dispatched via sub_6ADA3C, then
    // SendDefMessage(ident, wParam=result, 0,0,0)). Hex-Rays/disasm verified; fail-closed in C#
    // (live handlers return 1000 pending the Gild store). Reuses NativeGildRole.
    //
    //   4574 break_union  -> gild_owner[+0x68] sub_703CEC @0x00703CEC; other roles 555.
    //       no player 5; no gild 12; other gild not found 25; not allied 27; relation not removable 1000; else 0.
    //   4576 add_concern  -> gild_owner[+0x5C] sub_703ED4 @0x00703ED4; other roles 555. (4586 shares this, SM 4576.)
    //       no player 5; no gild 12; target not found 25; target is own gild 19; already present (duplicate) 1000; else 0.
    //   4581 enable_union -> gild_owner/gild_vice[+0x58] sub_704EAC @0x00704EAC; other roles 555.
    //       no player 5; no gild 12; else 0 (UPDATE Gild only when the flag byte [gild+0x28] changes).

    public enum NativeGildUnionConcernOp
    {
        BreakUnion = 4574,
        AddConcern = 4576,
        EnableUnion = 4581,
    }

    public sealed class NativeGildUnionConcernContext
    {
        public NativeGildRole Role { get; init; }
        public bool HasPlayer { get; init; } = true;
        public bool HasGild { get; init; }
        // 4574 / 4576
        public bool OtherGildFound { get; init; }       // sub_5E76D4 target gild
        // 4574
        public bool Allied { get; init; }               // sub_5E7890 == 1
        public bool RelationRemovable { get; init; }     // sub_5E90A4
        // 4576 (semantic labels corrected per gild_deferred_items_20260801.md §1.3)
        public bool TargetIsSelf { get; init; } // 19: target gild == caller's own gild (self-concern)
        public bool ConcernAdded { get; init; } // else 1000: destination already present (duplicate)
        // 4581
        public bool FlagChanged { get; init; }           // bl != [gild+0x28] (UPDATE emitted; result 0 either way)
    }

    public static class NativeGildUnionConcernTransaction
    {
        public const int VtblBreakUnion = 0x68;
        public const int VtblAddConcern = 0x5C;
        public const int VtblEnableUnion = 0x58;
        public const int VtblSendDefMessage = 0x250;

        public const int NoGild = 5;
        public const int GildEmpty = 12;
        public const int TargetNotFound = 25;
        public const int NotAllied = 27;
        public const int TargetIsSelfCode = 19; // add: target == caller's own gild (self-concern)
        public const int WriteFailed = 1000;     // break: relation not removable; add: already present (duplicate)
        public const int NoPermission = 555;
        public const int Success = 0;

        public static int Evaluate(NativeGildUnionConcernOp op, NativeGildUnionConcernContext c)
        {
            switch (op)
            {
                case NativeGildUnionConcernOp.BreakUnion: return BreakUnion(c);
                case NativeGildUnionConcernOp.AddConcern: return AddConcern(c);
                case NativeGildUnionConcernOp.EnableUnion: return EnableUnion(c);
                default: return NoPermission;
            }
        }

        // 4574: president only (vice[+0x68] is a 555 stub).
        private static int BreakUnion(NativeGildUnionConcernContext c)
        {
            if (c.Role != NativeGildRole.GildOwner) return NoPermission;
            if (!c.HasPlayer) return NoGild;
            if (!c.HasGild) return GildEmpty;
            if (!c.OtherGildFound) return TargetNotFound;
            if (!c.Allied) return NotAllied;
            return c.RelationRemovable ? Success : WriteFailed;
        }

        // 4576: president only (vice[+0x5C] is a 555 stub). Order/codes verified;
        // 19 = target is the caller's OWN gild (self-concern), 1000 = destination
        // already in the concern set (duplicate) — gild_deferred_items §1.3.
        private static int AddConcern(NativeGildUnionConcernContext c)
        {
            if (c.Role != NativeGildRole.GildOwner) return NoPermission;
            if (!c.HasPlayer) return NoGild;
            if (!c.HasGild) return GildEmpty;
            if (!c.OtherGildFound) return TargetNotFound;
            if (c.TargetIsSelf) return TargetIsSelfCode;
            return c.ConcernAdded ? Success : WriteFailed;
        }

        // 4581: president or vice ([+0x58] is sub_704EAC for both); result is 0 once player+gild exist
        // (a DB UPDATE is only emitted when the flag byte actually changes).
        private static int EnableUnion(NativeGildUnionConcernContext c)
        {
            if (c.Role != NativeGildRole.GildOwner && c.Role != NativeGildRole.GildVice)
                return NoPermission;
            if (!c.HasPlayer) return NoGild;
            if (!c.HasGild) return GildEmpty;
            return Success;
        }
    }
}
