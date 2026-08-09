using System;
using System.Collections.Generic;

namespace SystemModule
{
    // RandSeed cutover (task #78) — step 2: the diagnostic TRACE SINK for the live
    // RandomNumber facade. DEFAULT OFF: when Enabled is false, RandomNumber's draw
    // path is byte-identical to today (the facade only calls Record inside an
    // `if (RngTraceSink.Enabled)` guard, so no value ever changes).
    //
    // Pre-facade-swap (facade still uses .NET System.Random) the sink captures the
    // per-call SEQUENCE (ordinal, owner, api, args, result) — enough to validate the
    // call ORDER across the threading merge (cutover steps 4-5). SeedBefore/SeedAfter
    // are recorded as 0 until the facade is swapped to DelphiRandom (step 6), at which
    // point the swapped facade passes the real DelphiRandom.Seed before/after and the
    // unbroken-chain property (harness AssertUnbrokenChain) becomes observable live.
    //
    // Diagnostic only — turned on in a controlled fixed-seed run, off in production.
    public enum RngTraceApi
    {
        ParamlessAdvance, // RandomNumber.Random()            -> .NET Next()   (native Random(0))
        Random,           // RandomNumber.Random(value)       -> .NET Next(value)
        RandomMinMax,     // RandomNumber.Random(min,max)     -> .NET Next(min,max)
        GetRandomNumber   // RandomNumber.GetRandomNumber(a,b)-> .NET Next(a,b+1)
    }

    /// <summary>One recorded live draw. Owner = the ambient phase tag (from the map);
    /// SeedBefore/SeedAfter populate only after the facade is swapped to DelphiRandom.</summary>
    public readonly struct RngDraw
    {
        public RngDraw(long ordinal, string owner, RngTraceApi api,
            long arg0, long arg1, long result, uint seedBefore, uint seedAfter)
        {
            Ordinal = ordinal;
            Owner = owner;
            Api = api;
            Arg0 = arg0;
            Arg1 = arg1;
            Result = result;
            SeedBefore = seedBefore;
            SeedAfter = seedAfter;
        }

        public long Ordinal { get; }
        public string Owner { get; }
        public RngTraceApi Api { get; }
        public long Arg0 { get; }
        public long Arg1 { get; }
        public long Result { get; }
        public uint SeedBefore { get; }
        public uint SeedAfter { get; }
    }

    public static class RngTraceSink
    {
        /// <summary>Master gate. Default OFF => zero behavior change (byte-identical facade).</summary>
        public static bool Enabled { get; set; }

        /// <summary>Ambient owner/phase tag set by the tick phases in the single-owner run
        /// (e.g. "D1/Monsters"). Empty until the merge tags each phase.</summary>
        public static string CurrentOwner { get; set; } = string.Empty;

        private static long _ordinal;
        private static readonly List<RngDraw> _log = new();

        public static IReadOnlyList<RngDraw> Log => _log;
        public static long Count => _ordinal;

        public static void Reset()
        {
            _ordinal = 0;
            _log.Clear();
        }

        /// <summary>Called by the facade AFTER each draw, only when Enabled. seedBefore/seedAfter
        /// are 0 on the .NET facade and real on the swapped DelphiRandom facade (step 6).</summary>
        public static void Record(RngTraceApi api, long arg0, long arg1, long result,
            uint seedBefore, uint seedAfter)
        {
            _log.Add(new RngDraw(_ordinal++, CurrentOwner, api, arg0, arg1, result, seedBefore, seedAfter));
        }
    }
}
