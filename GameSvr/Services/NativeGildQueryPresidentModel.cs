using System.Collections.Generic;

namespace GameSvr.Services
{
    // Dormant classification model for CM_GILD_QUERY_PRESIDENT (4566 / 0x11D6) — the one CM_* opcode the
    // coverage census found with no GameSvr handler. Evidence: staging/gild_query_president_4566_20260801.md.
    // Image base 0x00400000. NEW file; not wired; performs no writes.
    //
    // FINDING: 4566 is a native NO-OP. The client-message router forwards the whole gild/social family
    // (0x11A5..0x11D6, 0x11ED..0x1225) into the gild dispatcher, whose switch on the ident
    // (ida_corps_dispatch_export.txt switch@285, entered for wIdent > 0x116F, cases up to 0x122B) has NO
    // case for 0x11D6 and hits `default: break`. So the server recognizes the ident, reads nothing, and
    // sends no reply. SM_GILD_QUERY_PRESIDENT (also 4566) is defined but never emitted.
    //
    // Therefore the C# absence of a 4566 handler is FAITHFUL; adding one would be an over-implementation
    // divergence (a reply the original never sends). This model records that conclusion in a checkable
    // form and is the deliverable in lieu of a handler.

    public enum NativeGildDispatchOutcome
    {
        HandledLeaf,   // ident has a case in the gild dispatcher -> a leaf handler runs
        UnhandledNoOp, // ident is within the dispatcher window but has no case -> default: break (no-op)
    }

    public static class NativeGildQueryPresidentModel
    {
        public const int CmIdent = 4566;               // 0x11D6 CM_GILD_QUERY_PRESIDENT
        public const int SmIdent = 4566;               // SM_GILD_QUERY_PRESIDENT (declared, never emitted)
        public const int DispatcherGuardAbove = 0x116F; // gild switch is entered when wIdent > 0x116F
        public const int DispatcherHighestCase = 0x122B; // highest case in that switch

        // No SM reply is produced and no request body is read: the default arm is a bare `break`.
        public const bool NativeSendsReply = false;
        public const bool NativeReadsRequestBody = false;

        // Handled cases reversed from ida_corps_dispatch_export.txt switch@285 (the gild-family block
        // around 4566, verified by direct read). The two IMMEDIATE neighbors of 0x11D6 — 0x11D5 (4565)
        // and 0x11D7 (4567) — are included to prove 4566 reaches this switch yet has no case of its own.
        public static readonly IReadOnlySet<int> HandledGildFamilyIdents = new HashSet<int>
        {
            0x11D0, 0x11D2, 0x11D3, 0x11D4, 0x11D5, 0x11D7, 0x11D8, 0x11D9, 0x11DA, 0x11DB, 0x11DC,
            0x11DD, 0x11DE, 0x11DF, 0x11E0, 0x11E1, 0x11E2, 0x11E3, 0x11E4, 0x11E5, 0x11E6, 0x11E7,
            0x11E8, 0x11E9, 0x11EA, 0x11EB, 0x11EC,
        };

        /// <summary>True when the ident lands in the gild dispatcher's covered window (so it is at least
        /// recognized and reaches switch@285).</summary>
        public static bool IsWithinDispatcherWindow(int ident) =>
            ident > DispatcherGuardAbove && ident <= DispatcherHighestCase;

        /// <summary>True when the ident has a real leaf case in the gild dispatcher.</summary>
        public static bool IsHandledLeaf(int ident) => HandledGildFamilyIdents.Contains(ident);

        /// <summary>
        /// Native dispatch outcome for 4566: it is within the window (recognized) but absent from the
        /// case set, so it falls to `default: break` — UnhandledNoOp.
        /// </summary>
        public static NativeGildDispatchOutcome ClassifyQueryPresident() =>
            IsWithinDispatcherWindow(CmIdent) && !IsHandledLeaf(CmIdent)
                ? NativeGildDispatchOutcome.UnhandledNoOp
                : NativeGildDispatchOutcome.HandledLeaf;

        /// <summary>
        /// The C# server has no 4566 handler. Because the native path is a no-op, that absence is
        /// faithful; a handler would emit a reply the original never sends.
        /// </summary>
        public static bool IsFaithfulNoHandler() =>
            ClassifyQueryPresident() == NativeGildDispatchOutcome.UnhandledNoOp;
    }
}
