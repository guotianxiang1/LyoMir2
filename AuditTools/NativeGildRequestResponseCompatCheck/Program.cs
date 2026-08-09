using GameSvr;

// Contract check for the 4611 accept / 4572 refuse observable dispatch model
// (sub_7039A0 / sub_70443C -> sub_704930 / sub_704E54).

try
{
    VerifyConstants();
    VerifyRequestNotFound();
    VerifyDelegate();

    Console.WriteLine(
        "PASS NativeGildRequestResponseCompatCheck 4611/4572 notfound=10 " +
        "type-dispatch=join/union/other subtype=abstract-delegate dormant=true");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"NativeGildRequestResponseCompatCheck FAIL: {ex.Message}");
    return 1;
}

static void Assert(bool cond, string msg)
{
    if (!cond)
        throw new Exception(msg);
}

static void VerifyConstants()
{
    Assert(NativeGildRequestResponseTransaction.VtblAccept == 0x00, "vtbl accept");
    Assert(NativeGildRequestResponseTransaction.VtblRefuse == 0x04, "vtbl refuse");
    Assert(NativeGildRequestResponseTransaction.VtblSubtypeAccept == 0x14, "subtype accept");
    Assert(NativeGildRequestResponseTransaction.VtblSubtypeRefuse == 0x18, "subtype refuse");
    Assert(NativeGildRequestResponseTransaction.VtblSendBuffer == 0x254, "vtbl send");
    Assert(NativeGildRequestResponseTransaction.RequestNotFound == 10, "code 10");
    Assert((int)NativeGildRequestResponseOp.AcceptRequest == 4611, "ident 4611");
    Assert((int)NativeGildRequestResponseOp.RefuseRequest == 4572, "ident 4572");
    Assert(NativeGildRequestResponseTransaction.SubtypeSlot(NativeGildRequestResponseOp.AcceptRequest) == 0x14,
        "accept subtype slot");
    Assert(NativeGildRequestResponseTransaction.SubtypeSlot(NativeGildRequestResponseOp.RefuseRequest) == 0x18,
        "refuse subtype slot");
}

static void VerifyRequestNotFound()
{
    foreach (var op in new[] { NativeGildRequestResponseOp.AcceptRequest, NativeGildRequestResponseOp.RefuseRequest })
    {
        // no request object -> 10
        Assert(NativeGildRequestResponseTransaction.Evaluate(op, new NativeGildRequestResponseContext
        {
            RequestFound = false, Type = NativeGildRequestType.Join, SubtypeResult = 0,
        }) == 10, $"{op} not found -> 10");
        // resolved but type None -> 10
        Assert(NativeGildRequestResponseTransaction.Evaluate(op, new NativeGildRequestResponseContext
        {
            RequestFound = true, Type = NativeGildRequestType.None, SubtypeResult = 5,
        }) == 10, $"{op} type none -> 10");
    }
}

static void VerifyDelegate()
{
    // resolved request of any concrete type forwards the subtype method's result verbatim.
    foreach (var op in new[] { NativeGildRequestResponseOp.AcceptRequest, NativeGildRequestResponseOp.RefuseRequest })
        foreach (var type in new[] { NativeGildRequestType.Join, NativeGildRequestType.Union, NativeGildRequestType.Other })
            foreach (int subtype in new[] { 0, 5, 12, 13, 555, 1000 })
                Assert(NativeGildRequestResponseTransaction.Evaluate(op, new NativeGildRequestResponseContext
                {
                    RequestFound = true, Type = type, SubtypeResult = subtype,
                }) == subtype, $"{op} {type} delegate {subtype}");
}
