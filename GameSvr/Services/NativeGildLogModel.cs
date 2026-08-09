namespace GameSvr.Services
{
    // Dormant model of the per-gild ACTIVITY LOG that backs CM_GILD_QUERY_LOG (4582). NOT wired; performs
    // no writes. Reversed from sub_720B6C @0x00720B6C (the log query/build), image base 0x00400000.
    //
    // sub_720B6C(logManager, type=a2, pageSize=n0x1E, gildKeyLo=a4, gildKeyHi=a5):
    //   log = sub_49F98C(a4, a5);                 // the gild's log object, keyed by gild id in a manager index
    //   if (!log || (log.Count == 30 && pageSize >= 30)) {   // absent, or the capped 30-window needs a refill
    //       if (log) count-code = 30; else { log = new TList; count-code = 0; }
    //       // (re)seed from the DB result set: while (sub_724BE8() > 0) append {type=word, id=double@+8, text@+16}
    //       sub_49F650(logManager+8, log, a4, a5);           // register the gild's log
    //   }
    //   // filter by type and emit newest-first: for i = log.Count-1 .. 0
    //   //   if (log[i].type == a2) -> 64-byte record { [0..7] = log[i] id (8 bytes @entry+8),
    //   //                                              [8..63] = log[i] text (56-byte short string, cap 55) }
    //
    // So the record is a 64-byte {id8, text56} — byte-identical to the CORPS log
    // (NativeCorpsWireCodec.EncodeLogs / NativeCorpsService.GetLogPage). The log is PER-GILD (keyed by gild
    // id), TYPE-filtered, and LOADED from a gild-log DB table then appended on gild events — the same shape
    // as the corps log, but corps logs live on NativeCorpsService._logs keyed by (corpsId, type); there is
    // no per-gild log store or event population today.
    //
    // 4582 wiring status (assessment for team-lead):
    //   - BACKEND (this structure) is dump-clear: 64-byte records, per-gild, type-filtered, newest-first.
    //   - BLOCKERS: (1) the send-frame handler sub_6F5C68 is SEH-obfuscated in the dump (the Param/Tag/Series
    //       mapping of the log reply is not byte-confirmed); (2) no per-gild log is loaded or populated, so
    //       the log is structurally empty and a faithful reply would be code 30 (no logs) for a gild member.
    //   - RECOMMENDATION: keep the live 4582 handler a FAITHFUL STUB (current behavior) + flag, per the
    //       "faithful-stub if opaque" guidance. Wiring it needs a gild-log store + event population design
    //       AND a byte-confirm of sub_6F5C68's reply frame — a separate sub-task, not part of the request
    //       ledger. Records themselves would reuse NativeCorpsWireCodec.EncodeLogs (already 64-byte).

    public enum NativeGildLogCountCode
    {
        Fresh = 0,  // no prior log object -> a new (empty) list was created
        Capped = 30, // an existing 30-entry window that the query treats as "full / needs refill"
    }

    public static class NativeGildLogModel
    {
        public const int RecordSize = 64;            // == NativeCorpsWireCodec.LogDescSize
        public const int TextShortStringCap = 55;    // sub_4039E4(..., 0x37)
        public const int WindowCap = 30;             // the log's capped window size in sub_720B6C
        public const int CmQueryLog = 4582;          // CM_GILD_QUERY_LOG / SM_GILD_QUERY_LOG
        public const int NoGild = 12;                // caller has no gild
        public const int EmptyLog = 30;              // gild has no (matching) log entries

        // True while the per-gild activity log is unmodeled (no store / no event population): the faithful
        // reply for a gild member is EmptyLog(30); with no gild it is NoGild(12). The live handler stays a
        // stub until a gild-log store + sub_6F5C68 send-frame confirmation land.
        public const bool BackendPopulated = false;
    }
}
