using GameSvr;

// Contract check for the 4627 cancel-request dormant model (handler sub_6ADB60 + strategy sub_703754).

try
{
    VerifyConstants();
    VerifyTopLevel();
    VerifyDelegateAndClearUi();

    Console.WriteLine(
        "PASS NativeGildCancelJoinCompatCheck 4627 nopending=5 notfound=10 " +
        "else=subtype-delegate clear-ui=always dormant=true");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"NativeGildCancelJoinCompatCheck FAIL: {ex.Message}");
    return 1;
}

static void Assert(bool cond, string msg)
{
    if (!cond)
        throw new Exception(msg);
}

static void VerifyConstants()
{
    Assert(NativeGildCancelJoinTransaction.Ident == 4627, "ident");
    Assert(NativeGildCancelJoinTransaction.VtblStrategy == 0x70, "vtbl strategy");
    Assert(NativeGildCancelJoinTransaction.VtblSubtypeCancel == 0x1C, "vtbl subtype");
    Assert(NativeGildCancelJoinTransaction.VtblSendDefMessage == 0x250, "vtbl send");
    Assert(NativeGildCancelJoinTransaction.NoPending == 5, "code 5");
    Assert(NativeGildCancelJoinTransaction.RequestNotFound == 10, "code 10");
}

static void VerifyTopLevel()
{
    // no pending -> 5 (request lookup not even reached).
    var noPending = NativeGildCancelJoinTransaction.Evaluate(new NativeGildCancelJoinContext
    {
        HasPending = false, RequestResolved = true, SubtypeCancelResult = 0,
    });
    Assert(noPending.Result == 5, "no pending -> 5");

    // pending but request object not resolved -> 10.
    var notFound = NativeGildCancelJoinTransaction.Evaluate(new NativeGildCancelJoinContext
    {
        HasPending = true, RequestResolved = false, SubtypeCancelResult = 12,
    });
    Assert(notFound.Result == 10, "request not found -> 10");
}

static void VerifyDelegateAndClearUi()
{
    // resolved -> the polymorphic subtype cancel result is forwarded verbatim.
    foreach (int subtype in new[] { 0, 12, 555 })
    {
        var o = NativeGildCancelJoinTransaction.Evaluate(new NativeGildCancelJoinContext
        {
            HasPending = true, RequestResolved = true, SubtypeCancelResult = subtype,
        });
        Assert(o.Result == subtype, $"delegate subtype {subtype}");
        Assert(o.DispatchWParam == subtype, $"wParam {subtype}");
    }

    // pending-request UI is cleared on every path.
    foreach (var c in new[]
    {
        new NativeGildCancelJoinContext { HasPending = false },
        new NativeGildCancelJoinContext { HasPending = true, RequestResolved = false },
        new NativeGildCancelJoinContext { HasPending = true, RequestResolved = true, SubtypeCancelResult = 0 },
    })
        Assert(NativeGildCancelJoinTransaction.Evaluate(c).ClearsPendingUi, "clears pending UI");
}
