using GameSvr;

// Contract check for the dormant CM_1017 item-stack MERGE model (GameSvr/Services/NativeItemMerge.cs),
// locked against the Hex-Rays contract of native sub_6D5E50 (dispatch sub_6D7D68 case 1017). This is a
// DIVERGENCE model: the live C# is a stale fixed ack; native 1017 consolidates duplicate stacks in
// use-item slot 9. See staging/cm_1017_item_merge_20260801.md.

try
{
    VerifyConstants();
    VerifyGateLadder();
    VerifyMergeAndClamp();
    VerifyEarlyBreakWhenFull();

    System.Console.WriteLine(
        "PASS NativeItemMergeCheck ident=1017 core=sub_6D5E50 gate=wParam9 slot=9 +100/stack " +
        "count=+0x26 max=+0x28 recog=+0x18 list=+0x508 clamp+dual-send");
    return 0;
}
catch (System.Exception ex)
{
    System.Console.Error.WriteLine($"NativeItemMergeCheck FAIL: {ex.Message}");
    return 1;
}

static void Assert(bool cond, string msg)
{
    if (!cond) throw new System.Exception(msg);
}

static void Equal<T>(T actual, T expected, string label)
{
    if (!Equals(actual, expected))
        throw new System.Exception($"{label}: expected {expected}, got {actual}");
}

static NativeItemMergeCandidate Mergeable(bool m) => new NativeItemMergeCandidate { Mergeable = m };

static void VerifyConstants()
{
    Equal(NativeItemMerge.Ident, 1017, "ident");
    Equal(NativeItemMerge.WParamGate, 9, "wParam gate");
    Equal(NativeItemMerge.UseItemSlot, 9, "use-item slot");
    Equal(NativeItemMerge.MergeIncrement, 100, "merge increment");
    Equal(NativeItemMerge.CoreEa, 0x006D5E50u, "core ea");
    Equal(NativeItemMerge.DispatchCaseEa, 0x006D900Au, "dispatch case ea");
    // field offsets — the decimal-in-Hex-Rays values, in hex; match the established item map.
    Equal(NativeItemMerge.ItemRecogOffset, 0x18, "recog offset");
    Equal(NativeItemMerge.ItemStdDefOffset, 0x1C, "std-def offset");
    Equal(NativeItemMerge.ItemCountOffset, 0x26, "count (Dura) offset");
    Equal(NativeItemMerge.ItemMaxOffset, 0x28, "max (DuraMax) offset");
    Equal(NativeItemMerge.ItemMergeGateOffset, 0x34, "merge gate offset");
    Equal(NativeItemMerge.SelfItemListOffset, 0x508, "item-list offset");
    Equal(NativeItemMerge.StdModeOffset, 0x14, "std mode offset");
    Equal(NativeItemMerge.StdShapeOffset, 0x15, "std shape offset");
}

static void VerifyGateLadder()
{
    // wParam != 9 -> immediate no-op (before any target lookup).
    var w = NativeItemMerge.Evaluate(new NativeItemMergeContext { WParam = 0, TargetPresent = true });
    Assert(w.Result == NativeItemMergeResult.WParamNotNine && !w.MutatesTarget
        && !w.SendsRemovedSet && !w.SendsStackUpdate, "wParam!=9 no-op");

    // target slot empty.
    var t = NativeItemMerge.Evaluate(new NativeItemMergeContext { WParam = 9, TargetPresent = false });
    Assert(t.Result == NativeItemMergeResult.TargetSlotEmpty && !t.MutatesTarget, "target empty");

    // Recog mismatch.
    var r = NativeItemMerge.Evaluate(new NativeItemMergeContext
    { WParam = 9, TargetPresent = true, RequestRecog = 5, TargetRecog = 6, KindGuardPassed = true });
    Assert(r.Result == NativeItemMergeResult.RecogMismatch && !r.MutatesTarget, "recog mismatch");

    // kind guard fails (Recog matches).
    var g = NativeItemMerge.Evaluate(new NativeItemMergeContext
    { WParam = 9, TargetPresent = true, RequestRecog = 7, TargetRecog = 7, KindGuardPassed = false });
    Assert(g.Result == NativeItemMergeResult.KindGuardFailed && !g.MutatesTarget, "kind guard fail");

    // guards pass but nothing mergeable -> no send, no mutation.
    var none = NativeItemMerge.Evaluate(new NativeItemMergeContext
    {
        WParam = 9, TargetPresent = true, RequestRecog = 1, TargetRecog = 1, KindGuardPassed = true,
        TargetCount = 0, TargetMax = 1000,
        Candidates = new[] { Mergeable(false), Mergeable(false) },
    });
    Assert(none.Result == NativeItemMergeResult.NoMergeableStacks && none.MergedCount == 0
        && !none.SendsRemovedSet && !none.SendsStackUpdate, "no mergeable -> no send");
}

static void VerifyMergeAndClamp()
{
    // two mergeable stacks, room to grow: +100 each, no clamp.
    var m = NativeItemMerge.Evaluate(new NativeItemMergeContext
    {
        WParam = 9, TargetPresent = true, RequestRecog = 1, TargetRecog = 1, KindGuardPassed = true,
        TargetCount = 10, TargetMax = 1000,
        Candidates = new[] { Mergeable(true), Mergeable(false), Mergeable(true) },
    });
    Equal(m.Result, NativeItemMergeResult.Merged, "merged result");
    Equal(m.MergedCount, 2, "merged count");
    Equal(m.CountAdded, 200, "count added (2*100)");
    Equal(m.FinalCount, 210, "final count 10+200");
    Assert(m.MutatesTarget && m.SendsRemovedSet && m.SendsStackUpdate, "merged -> dual send");

    // clamp: count 50, max 100, one merge (+100 -> 150) clamps to 100.
    var c = NativeItemMerge.Evaluate(new NativeItemMergeContext
    {
        WParam = 9, TargetPresent = true, RequestRecog = 1, TargetRecog = 1, KindGuardPassed = true,
        TargetCount = 50, TargetMax = 100,
        Candidates = new[] { Mergeable(true) },
    });
    Equal(c.Result, NativeItemMergeResult.Merged, "clamp result");
    Equal(c.MergedCount, 1, "clamp merged count");
    Equal(c.FinalCount, 100, "clamp final count -> max");
}

static void VerifyEarlyBreakWhenFull()
{
    // target already full (count >= max): the top-of-loop break stops before any merge -> no-op result.
    var full = NativeItemMerge.Evaluate(new NativeItemMergeContext
    {
        WParam = 9, TargetPresent = true, RequestRecog = 1, TargetRecog = 1, KindGuardPassed = true,
        TargetCount = 100, TargetMax = 100,
        Candidates = new[] { Mergeable(true), Mergeable(true), Mergeable(true) },
    });
    Assert(full.Result == NativeItemMergeResult.NoMergeableStacks && full.MergedCount == 0
        && !full.SendsStackUpdate, "already-full -> early break, 0 merged");

    // mid-scan fill: count 900, max 1000 -> only the first mergeable (+100 -> 1000) fits; the next
    // iteration breaks (max <= count).
    var mid = NativeItemMerge.Evaluate(new NativeItemMergeContext
    {
        WParam = 9, TargetPresent = true, RequestRecog = 1, TargetRecog = 1, KindGuardPassed = true,
        TargetCount = 900, TargetMax = 1000,
        Candidates = new[] { Mergeable(true), Mergeable(true), Mergeable(true) },
    });
    Equal(mid.MergedCount, 1, "mid-fill merges exactly one");
    Equal(mid.FinalCount, 1000, "mid-fill final count == max");
}
