namespace GameSvr
{
    // Dormant model of Gild vice-leader write ops 4587 (self stepdown) and 4588 (president dismiss
    // vice). Hex-Rays verified. Handlers sub_6F7968 (4587) / sub_6F79A4 (4588) resolve the caller's
    // role strategy via sub_6ADA3C @0x006ADA3C, invoke a vtable slot, then
    // SendDefMessage(ident, wParam=result, 0,0,0) via player.[vtbl+0x250].
    //
    //   role dispatch sub_6ADA3C: player -> {no_corps | member | corps | gild_member | gild_vice |
    //     gild_owner} strategy object based on membership offsets.
    //   4587 -> strategy[+0x78]: gild_owner/gild_vice -> sub_704CC0 @0x00704CC0; others -> 555.
    //     sub_704CC0: no player 5; no gild 12; gild has no vice OR caller is not the vice 555; else 0
    //       (clear vice pointer + make-save-gild UPDATE command + refresh/broadcast).
    //   4588 -> strategy[+0x74]: gild_owner -> sub_704228 @0x00704228; gild_vice -> sub_701C1C (555);
    //     others -> 555. sub_704228: no player 5; no gild 12; caller not president 555;
    //     target not found 22; target not a vice 22; else 0 (clear target vice + UPDATE + refresh).
    //
    // WIRED (gated on SupportsGildWrites): NativeCorpsService.ApplyGildVice builds this context from
    // live state and, on Success, clears the gild vice pointer + make-save-gild (TrySaveGild
    // ViceGuild=0) fail-safe; with no Gild store the live handlers keep the original fail-closed 1000.
    // This models the exact result ladder and role dispatch.

    public enum NativeGildRole
    {
        NoCorps,
        Member,
        Corps,
        GildMember,
        GildVice,
        GildOwner,
    }

    public enum NativeGildViceOp
    {
        SelfStepDown = 4587,
        PresidentDismiss = 4588,
    }

    public sealed class NativeGildViceContext
    {
        /// <summary>Caller's resolved Gild role (sub_6ADA3C).</summary>
        public NativeGildRole Role { get; init; }
        /// <summary>Player object present (sub_5EC030); defensive, always true in the live path.</summary>
        public bool HasPlayer { get; init; } = true;
        /// <summary>Caller has a Gild (player[4] != 0).</summary>
        public bool HasGild { get; init; }
        /// <summary>4587: the Gild has a vice pointer (gild[8] != 0).</summary>
        public bool GildHasVice { get; init; }
        /// <summary>4587: the current vice is the caller (*(vice+24) == callerId).</summary>
        public bool CallerIsTheVice { get; init; }
        /// <summary>4588: the caller is the Gild president (*(president+24) == callerId).</summary>
        public bool CallerIsPresident { get; init; }
        /// <summary>4588: dismiss target resolved (sub_5EA444 != null).</summary>
        public bool TargetFound { get; init; }
        /// <summary>4588: target is a valid vice (sub_7063E8 >= 0).</summary>
        public bool TargetIsVice { get; init; }
    }

    public static class NativeGildViceTransaction
    {
        public const int VtblSelfStepDown = 0x78;    // 4587 strategy slot (120)
        public const int VtblDismiss = 0x74;         // 4588 strategy slot (116)
        public const int VtblSendDefMessage = 0x250; // player SendDefMessage

        public const int NoGild = 5;
        public const int GildEmpty = 12;
        public const int NoPermission = 555;
        public const int TargetInvalid = 22;
        public const int Success = 0;

        /// <summary>Raw result code (goes verbatim into SendDefMessage wParam).</summary>
        public static int Evaluate(NativeGildViceOp op, NativeGildViceContext context)
        {
            return op == NativeGildViceOp.SelfStepDown
                ? SelfStepDown(context)
                : PresidentDismiss(context);
        }

        // 4587 self vice-stepdown: only the gild_owner/gild_vice strategies reach sub_704CC0; that
        // method only succeeds when the caller actually is the current vice.
        private static int SelfStepDown(NativeGildViceContext c)
        {
            if (c.Role != NativeGildRole.GildOwner && c.Role != NativeGildRole.GildVice)
                return NoPermission;
            if (!c.HasPlayer) return NoGild;
            if (!c.HasGild) return GildEmpty;
            if (!c.GildHasVice || !c.CallerIsTheVice) return NoPermission;
            return Success;
        }

        // 4588 president dismiss vice: only the gild_owner strategy reaches sub_704228; gild_vice
        // maps to sub_701C1C (555); all other roles return 555.
        private static int PresidentDismiss(NativeGildViceContext c)
        {
            if (c.Role != NativeGildRole.GildOwner)
                return NoPermission;
            if (!c.HasPlayer) return NoGild;
            if (!c.HasGild) return GildEmpty;
            if (!c.CallerIsPresident) return NoPermission;
            if (!c.TargetFound) return TargetInvalid;
            if (!c.TargetIsVice) return TargetInvalid;
            return Success;
        }
    }
}
