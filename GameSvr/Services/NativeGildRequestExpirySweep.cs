namespace GameSvr.Services
{
    /// <summary>
    /// GILD-10 — the once-per-day pending-request expiry sweep for the social-org request families
    /// (join-corps / join-gild / UNION alliance requests).
    ///
    /// Native origin: the purge body is sub_6A5D6C @0x006A5D6C; this type reproduces its TIME-OF-DAY
    /// GATE, which is the reason the sweep is not a plain interval timer:
    ///
    ///   0x006A5DA1  mov eax, 0x006A5FD8     ; literal 'hh:mm'
    ///   0x006A5DAD  call &lt;FormatDateTime/compare&gt;
    ///   0x006A5DAE  mov edx, 0x006A5FE8     ; literal '03:03'
    ///   0x006A5DB8  jnz  +0x1E3             ; not 03:03 -> return, doing nothing
    ///
    /// i.e. the server formats "now" as hh:mm and only sweeps while that string equals "03:03".
    /// Because the containing tick runs far more often than once a minute, native would re-enter the
    /// body many times inside that single minute; the <see cref="LastSweptDate"/> latch here keeps the
    /// observable effect identical (all expired entries are gone after 03:03) while doing the walk once.
    /// The 3-day threshold itself lives with the ledger
    /// (<see cref="NativeGildRequestLedger.ExpiryDays"/>, float32 3.0 @0x006A5FF0).
    ///
    /// The native tally line is emitted only when something was actually dropped
    /// (0x006A5F5E: <c>cmp [ebp-8],0</c> / <c>jle</c>), using the literals at 0x006A5FFC and 0x006A6040.
    /// </summary>
    internal static class NativeGildRequestExpirySweep
    {
        /// <summary>Native gate literal at 0x006A5FE8 — the only minute the purge runs.</summary>
        internal const string GateTimeOfDay = "03:03";

        /// <summary>Native format literal at 0x006A5FD8.</summary>
        internal const string GateFormat = "HH:mm";

        // Head/tail of the native tally line (0x006A5FFC / 0x006A6040). Kept as the exact reversed text.
        private const string TallyPrefix =
            "\r\n *** *** 删除 加入战队、加入行会、行会联盟 的过期请求 : ";
        private const string TallySuffix = "条 *** ***";

        private static readonly object SyncRoot = new();
        private static DateTime _lastSweptDate = DateTime.MinValue;

        /// <summary>The date whose 03:03 window has already been swept (test/diagnostic visibility).</summary>
        internal static DateTime LastSweptDate
        {
            get { lock (SyncRoot) return _lastSweptDate; }
        }

        internal static void ResetForTests()
        {
            lock (SyncRoot) _lastSweptDate = DateTime.MinValue;
        }

        /// <summary>
        /// Native gate predicate: true iff FormatDateTime('hh:mm', now) == '03:03'.
        /// </summary>
        internal static bool IsGateOpen(DateTime now) =>
            now.ToString(GateFormat) == GateTimeOfDay;

        /// <summary>
        /// Tick entry point. Returns the number of expired requests dropped this call (0 when the
        /// 03:03 gate is shut, when today's window was already swept, or when nothing had expired).
        /// </summary>
        internal static int Run(NativeCorpsService service, DateTime now)
        {
            if (service == null || !service.IsAvailable) return 0;
            if (!IsGateOpen(now)) return 0;

            lock (SyncRoot)
            {
                if (_lastSweptDate == now.Date) return 0;
                _lastSweptDate = now.Date;
            }

            var removed = service.PurgeExpiredRequests(now);
            // Native logs the tally ONLY when at least one request was removed.
            if (removed > 0)
                M2Share.MainOutMessage(TallyPrefix + removed + TallySuffix);
            return removed;
        }
    }
}
