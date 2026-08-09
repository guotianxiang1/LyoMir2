using System.Collections.Generic;

namespace GameSvr
{
    // ------------------------------------------------------------------------------------------------
    // Dormant model of CM_1017 (0x3F9) — the item-stack MERGE / consolidation handler.
    //
    // DIVERGENCE being modeled: the live C# case is a stale fixed ack
    //   SendDefMessage(1, 0, 0, 0, 0, "")   @ TPlayObject.Message.cs (inherited verbatim from the
    // GameOfMir ancestor ObjBase.pas:4774 / upstream LyoMir2). In THIS binary, native case 1017 is a
    // real handler that consolidates duplicate stacks in use-item slot 9. See
    // staging/cm_needs_modeling_3opcodes_20260801.md (contract) and cm_1017_item_merge_20260801.md.
    //
    // Native evidence (unpacked M2Server.exe, image base 0x00400000, Hex-Rays):
    //   dispatch : sub_6D7D68 case 1017 @0x006D900A -> sub_6D5E50(Self, Recog=msg.Recog[+0], wParam=msg.wParam[+6])
    //   core     : sub_6D5E50 @0x006D5E50 (006D5E50-006D5FC8)
    //   target   : sub_75EC20(9, ..)  -> the use-item in slot 9 (v5)
    //   guard    : sub_404828(item, off_75E628) -> item-kind bool
    //   list     : *(Self + 0x508)     -> main item TList (scanned high index -> low, sub_424D4C = TList.Get)
    //   senders  : sub_765F6C(0, 4*n, removed[], 0,0, n, 0)  (removed/merged set)
    //              sub_765E68(0,0,0, max, count, 9)          (updated target stack)
    //
    // Exact field offsets (Hex-Rays shows them in DECIMAL; hex below — they match the established item
    // map: id/Recog +0x18, std-def ptr +0x1C, Dura +0x26; see gm-skill-equip evidence):
    //   item +0x18 (24)  Recog / id            (request must equal target's; merged stack's id is recorded)
    //   item +0x1C (28)  std-item def pointer   (shape pair-match reads def[+0x00] and def[+0x1E])
    //   item +0x26 (38)  Dura == STACK COUNT    (target += 100 per merged stack, then clamped)
    //   item +0x28 (40)  DuraMax == STACK CAP   (clamp target count to this; early-break when count>=max)
    //   item +0x34 (52)  merge gate WORD        (must be 0 for a candidate to merge)
    //   std +0x14 (20) StdMode, std +0x15 (21) Shape : special exclusion (StdMode==2 && Shape==10 && count!=0)
    //   std +0x1E (30)  shape word (reciprocal pair-match with the other item's def[+0x00])
    //
    // Control flow: gate wParam==9 -> resolve slot-9 target -> Recog match -> kind guard -> scan the item
    // list; for each candidate, FIRST break if the target is already full (max <= count), else if the
    // candidate is mergeable add 100 to the target count and record its id. After the scan, if >=1 merged:
    // send the removed set, clamp count to max, send the updated stack. Any earlier gate failing => silent
    // return (no send, no mutation).
    //
    // DORMANT: not wired. This captures the exact ladder/offsets so a future gated wiring (replacing the
    // stale ack) can reproduce it. Per-candidate "mergeable" is supplied as a precondition (the model does
    // not re-derive std-shape internals), mirroring the other Native* dormant models.
    // ------------------------------------------------------------------------------------------------

    public enum NativeItemMergeResult
    {
        /// <summary>wParam != 9 — gate fails; the core returns immediately (no-op).</summary>
        WParamNotNine,
        /// <summary>No use-item present in slot 9 (sub_75EC20 null).</summary>
        TargetSlotEmpty,
        /// <summary>Request Recog != target item[+0x18].</summary>
        RecogMismatch,
        /// <summary>Item-kind guard sub_404828 returned false.</summary>
        KindGuardFailed,
        /// <summary>Scan ran but no candidate was mergeable — no mutation, no send.</summary>
        NoMergeableStacks,
        /// <summary>>=1 stack merged into the target — count raised (+100 each) &amp; clamped, updates sent.</summary>
        Merged,
    }

    /// <summary>One candidate stack in the scanned item list (precondition snapshot).</summary>
    public sealed class NativeItemMergeCandidate
    {
        /// <summary>
        /// True when the candidate passes the native inner test: reciprocal std-shape pair-match AND
        /// NOT (std.StdMode==2 &amp;&amp; std.Shape==10 &amp;&amp; count!=0) AND item[+0x34]==0.
        /// </summary>
        public bool Mergeable { get; init; }
    }

    /// <summary>Side-effect-free precondition snapshot for one CM_1017 request.</summary>
    public sealed class NativeItemMergeContext
    {
        /// <summary>msg.wParam (word @ msg+6). Only 9 acts.</summary>
        public int WParam { get; init; }
        /// <summary>Whether a use-item exists in slot 9 (the merge target).</summary>
        public bool TargetPresent { get; init; }
        /// <summary>msg.Recog (int @ msg+0) sent by the client.</summary>
        public int RequestRecog { get; init; }
        /// <summary>target item[+0x18] — must equal RequestRecog.</summary>
        public int TargetRecog { get; init; }
        /// <summary>sub_404828 item-kind guard result.</summary>
        public bool KindGuardPassed { get; init; }
        /// <summary>target count (Dura, item[+0x26]) before merging.</summary>
        public int TargetCount { get; init; }
        /// <summary>target cap (DuraMax, item[+0x28]).</summary>
        public int TargetMax { get; init; }
        /// <summary>Candidate stacks in native scan order (list high index -> low).</summary>
        public IReadOnlyList<NativeItemMergeCandidate> Candidates { get; init; }
    }

    public sealed class NativeItemMergeOutcome
    {
        public NativeItemMergeResult Result { get; init; }
        /// <summary>Number of stacks merged (v12).</summary>
        public int MergedCount { get; init; }
        /// <summary>Raw count added before clamp (MergedCount * 100).</summary>
        public int CountAdded { get; init; }
        /// <summary>Target count after += and clamp-to-max.</summary>
        public int FinalCount { get; init; }
        /// <summary>sub_765F6C removed-set send (only when MergedCount &gt; 0).</summary>
        public bool SendsRemovedSet { get; init; }
        /// <summary>sub_765E68(...,9) updated-stack send (only when MergedCount &gt; 0).</summary>
        public bool SendsStackUpdate { get; init; }
        /// <summary>Whether the target item's count was mutated.</summary>
        public bool MutatesTarget => MergedCount > 0;
    }

    public static class NativeItemMerge
    {
        public const int Ident = 1017;                 // 0x3F9
        public const int WParamGate = 9;               // n9 == 9
        public const int UseItemSlot = 9;              // sub_75EC20(9, ..)
        public const int MergeIncrement = 100;         // *(target+0x26) += 100 per merged stack

        // handler / dispatch / sender addresses
        public const uint DispatchCaseEa = 0x006D900A; // sub_6D7D68 case 1017
        public const uint CoreEa = 0x006D5E50;         // sub_6D5E50
        public const uint TargetFinderEa = 0x0075EC20; // sub_75EC20 (use-item slot)
        public const uint KindGuardEa = 0x00404828;    // sub_404828
        public const uint RemovedSetSenderEa = 0x00765F6C; // sub_765F6C
        public const uint StackUpdateSenderEa = 0x00765E68; // sub_765E68

        // field offsets (hex; Hex-Rays showed decimal)
        public const int MsgRecogOffset = 0x00;        // msg.Recog
        public const int MsgWParamOffset = 0x06;       // msg.wParam
        public const int ItemRecogOffset = 0x18;       // item id / Recog (24)
        public const int ItemStdDefOffset = 0x1C;      // std-item def pointer (28)
        public const int ItemCountOffset = 0x26;       // Dura == stack count (38)
        public const int ItemMaxOffset = 0x28;         // DuraMax == stack cap (40)
        public const int ItemMergeGateOffset = 0x34;   // must be 0 (52)
        public const int StdShapePairOffset = 0x1E;    // std def shape word (30)
        public const int StdModeOffset = 0x14;         // std StdMode (20)
        public const int StdShapeOffset = 0x15;        // std Shape (21)
        public const int SelfItemListOffset = 0x508;   // main item TList (1288)

        // native "special" exclusion literals
        public const int SpecialStdMode = 2;
        public const int SpecialShape = 10;

        public static NativeItemMergeOutcome Evaluate(NativeItemMergeContext context)
        {
            if (context == null || context.WParam != WParamGate)
                return NoOp(NativeItemMergeResult.WParamNotNine);
            if (!context.TargetPresent)
                return NoOp(NativeItemMergeResult.TargetSlotEmpty);
            if (context.RequestRecog != context.TargetRecog)
                return NoOp(NativeItemMergeResult.RecogMismatch);
            if (!context.KindGuardPassed)
                return NoOp(NativeItemMergeResult.KindGuardFailed);

            int count = context.TargetCount;
            int max = context.TargetMax;
            int merged = 0;

            var candidates = context.Candidates ?? System.Array.Empty<NativeItemMergeCandidate>();
            foreach (var cand in candidates)
            {
                // native checks the "target full" break at the TOP of each iteration.
                if (max <= count)
                    break;
                if (cand != null && cand.Mergeable)
                {
                    count += MergeIncrement;
                    merged++;
                }
            }

            if (merged == 0)
            {
                return new NativeItemMergeOutcome
                {
                    Result = NativeItemMergeResult.NoMergeableStacks,
                    MergedCount = 0,
                    CountAdded = 0,
                    FinalCount = count,
                    SendsRemovedSet = false,
                    SendsStackUpdate = false,
                };
            }

            int finalCount = count;
            if (max < finalCount) // clamp count to max after the merge
                finalCount = max;

            return new NativeItemMergeOutcome
            {
                Result = NativeItemMergeResult.Merged,
                MergedCount = merged,
                CountAdded = merged * MergeIncrement,
                FinalCount = finalCount,
                SendsRemovedSet = true,
                SendsStackUpdate = true,
            };
        }

        private static NativeItemMergeOutcome NoOp(NativeItemMergeResult result) =>
            new NativeItemMergeOutcome
            {
                Result = result,
                MergedCount = 0,
                CountAdded = 0,
                FinalCount = 0,
                SendsRemovedSet = false,
                SendsStackUpdate = false,
            };
    }
}
