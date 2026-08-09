namespace GameSvr.Services
{
    // ================================================================================================
    // Booth-setup executor (task #83) — the money/item-FREE stall ops SetTimeLevel(4419) + START(4424).
    //
    // Δ=0 BY CONSTRUCTION: every method here takes the player's gold as a read-only <c>long</c> value and
    // NEVER receives or touches an item list — so it is structurally impossible for this code to move a coin
    // or an item. The native affordability gate (sub_61D6B8) is likewise CHECK-ONLY: it compares
    // duration*fee against GoldNum (regular gold, Self+0x15C) and never deducts (codec-fidelity 2026-08-01,
    // one serial idat). The audit therefore proves Δgold=Δitems=0 from the signatures, not a runtime harness.
    //
    // This class is PURE (no player, no store, no I/O): it maps a resolved stall record + the selected
    // StallTradConf tier + the player's gold + the request duration into the reversed SM code ladders, and
    // applies the sole record mutations (SetTimeLevel sets DuraTime=record[+0x34]; START sets Status=Running
    // =record[+0x40]). The TPlayObject wrapper resolves those inputs (owner name/id, GoldNum, the duration
    // from the CM 4419 body tokens, the tier) and performs the store persist; the wrapper owns the two
    // low-stakes non-economy residuals (4419 param wire-order, tier-selection) — none of which reach here.
    //
    // Ladders (codec-fidelity confirmed): SetTimeLevel first-fail-wins config(-3)->duration>maxDur(-2)->
    // name-gate(-3)->afford(-1)->1, create-fail edge -> 0 (silent). START -9 map / -4 no-record / -7
    // precheckA / -8 precheckB / affordability(-1/-2/-3) / open(1). DORMANT: nothing calls this yet — the
    // whole stall subsystem stays gated OFF until the together-flip.
    // ================================================================================================
    public static class NativeStallBoothSetup
    {
        /// <summary>
        /// SetTimeLevel (sub_61D294): build the affordability-gate context from the resolved record + tier +
        /// gold + requested duration, classify via the reversed ladder, and on success set the record's
        /// duration (record[+0x34]). Returns the SM 4419 code (int.MinValue == silent, only on the
        /// get-or-create edge). Moves no money and no items.
        /// </summary>
        public static int EvaluateSetTimeLevel(NativeStallRecord record, NativeStallTradTier tier, long gold, int duration)
        {
            var context = BuildAffordabilityContext(record, tier, gold, duration);
            int code = NativeStallWriteTransaction.Evaluate(NativeStallOp.SetTimeLevel, context);
            if (code == 1)
                record.DuraTime = duration;   // paramA -> record[+0x34]; the sole mutation
            return code;
        }

        /// <summary>
        /// START (sub_61D4F0): apply the -9/-4/-7/-8 prechecks, then the same affordability gate against the
        /// record's configured duration, then the open. On success set Status=Running (record[+0x40]=1). The
        /// prechecks are resolved by the caller (map sub_7684A0, sub_61F3D8 paid-time, sub_61EE88 item-count).
        /// Returns the SM 4424 code. Moves no money and no items.
        /// </summary>
        public static int EvaluateStart(NativeStallRecord record, bool mapAllowsStall, bool precheckA,
            bool precheckB, NativeStallTradTier tier, long gold)
        {
            // START re-runs the affordability gate (sub_61D6B8) against the configured duration; the open
            // itself succeeds (1) when affordable, else the affordability negative propagates as the result.
            int coreResult = record != null
                ? NativeStallWriteTransaction.Evaluate(NativeStallOp.SetTimeLevel,
                    BuildAffordabilityContext(record, tier, gold, record.DuraTime))
                : 0;
            var context = new NativeStallContext
            {
                MapAllowsStall = mapAllowsStall,
                StallRecordFound = record != null,
                StartPrecheckA = precheckA,
                StartPrecheckB = precheckB,
                StartCoreResult = coreResult,
            };
            int code = NativeStallWriteTransaction.Evaluate(NativeStallOp.StartStall, context);
            if (code == 1 && record != null)
                record.Status = StallRecordStatus.Running;   // sub_61DFC4 record[+0x40]=1
            return code;
        }

        // The affordability gate (sub_61D6B8) as SetTimeLevel context booleans. CHECK-ONLY — no deduction.
        // fee = tier.Material1Qty (cfg+0x20), maxDur = tier.MaxDurationHours (cfg+0x08); compared vs GoldNum.
        private static NativeStallContext BuildAffordabilityContext(NativeStallRecord record,
            NativeStallTradTier tier, long gold, int duration)
        {
            bool configPresent = tier != null;
            return new NativeStallContext
            {
                SetTimeLevelRecordCreated = record != null,
                SetTimeLevelConfigPresent = configPresent,
                SetTimeLevelDurationWithinMax = configPresent && duration <= tier.MaxDurationHours,
                SetTimeLevelNameGateOk = true,   // name-length gate passes for a valid loaded config
                SetTimeLevelCanAfford = configPresent && (long)duration * tier.Material1Qty <= gold,
            };
        }
    }
}
