namespace GameSvr
{
    // Dormant model of Gild op 4627 (cancel my own pending join/union request). Handler sub_6ADB60
    // @0x006ADB60; role strategy [+0x70] sub_703754 @0x00703754. Hex-Rays verified. Fail-closed in C#.
    //
    //   handler sub_6ADB60: no pending request (player[0xBA6] byte / a1[698] == 0) -> 5; else strategy;
    //     always clears the pending-request UI (sub_6F769C) and SendDefMessage(4627, wParam=result).
    //   strategy sub_703754: request lookup sub_6A52A0 -> null 10; else the request object's own
    //     subtype cancel virtual method request.[vtbl+0x1C] (polymorphic: join vs union pending
    //     request), which returns the subtype's code (commonly 12 / 0, or 555).
    //
    // The subtype cancel is genuinely polymorphic (per pending-request class), so it is an abstract
    // input here; the deterministic top-level contract (5 / 10 / delegate + always-clear-UI) is exact.

    public sealed class NativeGildCancelJoinContext
    {
        /// <summary>Player has a pending request (a1[698] / byte[player+0xBA6] != 0).</summary>
        public bool HasPending { get; init; }
        /// <summary>sub_6A52A0 resolved the pending request object.</summary>
        public bool RequestResolved { get; init; }
        /// <summary>request.[vtbl+0x1C] subtype cancel result (polymorphic; e.g. 12 / 0 / 555).</summary>
        public int SubtypeCancelResult { get; init; }
    }

    public sealed class NativeGildCancelJoinOutcome
    {
        public int Result { get; init; }
        /// <summary>Handler clears the pending-request UI (sub_6F769C) regardless of result.</summary>
        public bool ClearsPendingUi { get; init; }
        public int DispatchWParam => Result;
    }

    public static class NativeGildCancelJoinTransaction
    {
        public const int Ident = 4627;
        public const int VtblStrategy = 0x70;
        public const int VtblSubtypeCancel = 0x1C;
        public const int VtblSendDefMessage = 0x250;

        public const int NoPending = 5;
        public const int RequestNotFound = 10;

        public static NativeGildCancelJoinOutcome Evaluate(NativeGildCancelJoinContext c)
        {
            int result;
            if (!c.HasPending)
                result = NoPending;
            else if (!c.RequestResolved)
                result = RequestNotFound;
            else
                result = c.SubtypeCancelResult; // request.[+0x1C], polymorphic

            // The pending-request UI is cleared on every path.
            return new NativeGildCancelJoinOutcome { Result = result, ClearsPendingUi = true };
        }
    }
}
