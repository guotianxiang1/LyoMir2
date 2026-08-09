using GameSvr;

// Contract check for the dormant Corps write model (4522-4532), locked against the sub_6ADA3C role
// dispatch and the per-op strategy slots. 555 = a `mov eax,0x22B; ret` stub; permitted roles reach a
// real management method whose terminal result is abstracted as StrategyResult. Enum<->VMT map per
// NativeGildRequestUnionTransaction.cs (Corps=corps_vice_owner, GildMember=corps_owner).

try
{
    VerifyConstants();
    VerifyRequestJoin();
    VerifyCancelJoin();
    VerifyCreate();
    VerifyManagerOps();
    VerifyCaptainOps();
    VerifyStepDown();
    VerifyStrategyPassthrough();

    Console.WriteLine(
        "PASS NativeCorpsWriteCompatCheck ops=4522/4523/4524/4526/4527/4528/4529/4530/4532 " +
        "gates(7/10/3) manager>=Corps captain>=GildMember stepdown=Corps-only " +
        "request-join=NoCorps-only reply=0x250 dormant=true");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"NativeCorpsWriteCompatCheck FAIL: {ex.Message}");
    return 1;
}

static void Assert(bool cond, string msg)
{
    if (!cond)
        throw new Exception(msg);
}

static int Eval(NativeCorpsWriteOp op, NativeCorpsWriteContext c) =>
    NativeCorpsWriteTransaction.Evaluate(op, c);

static IEnumerable<NativeGildRole> AllRoles() => Enum.GetValues<NativeGildRole>();

static void VerifyConstants()
{
    Assert(NativeCorpsWriteTransaction.NoPermission == 555, "555");
    Assert(NativeCorpsWriteTransaction.Success == 0, "0");
    Assert(NativeCorpsWriteTransaction.RequestTargetNotFound == 7, "7");
    Assert(NativeCorpsWriteTransaction.NoPendingRequest == 10, "10");
    Assert(NativeCorpsWriteTransaction.AlreadyInCorps == 3, "3");
    Assert(NativeCorpsWriteTransaction.VtblSendDefMessage == 0x250, "reply 0x250");
    Assert(NativeCorpsWriteTransaction.SlotCreate == 0x08, "slot create");
    Assert(NativeCorpsWriteTransaction.SlotRequestJoin == 0x10, "slot request");
    Assert(NativeCorpsWriteTransaction.SlotCancelJoin == 0x14, "slot cancel");
    Assert(NativeCorpsWriteTransaction.SlotTransferCaptain == 0x1C, "slot transfer");
    Assert(NativeCorpsWriteTransaction.SlotSetMemberTitle == 0x20, "slot title");
    Assert(NativeCorpsWriteTransaction.SlotSetRecruit == 0x28, "slot recruit");
    Assert(NativeCorpsWriteTransaction.SlotAppointVice == 0x2C, "slot appoint");
    Assert(NativeCorpsWriteTransaction.SlotDismissMember == 0x34, "slot dismiss");
    Assert(NativeCorpsWriteTransaction.SlotStepDown == 0x38, "slot stepdown");
    Assert((int)NativeCorpsWriteOp.RequestJoin == 4522 && (int)NativeCorpsWriteOp.CancelJoin == 4523
        && (int)NativeCorpsWriteOp.Create == 4524 && (int)NativeCorpsWriteOp.SetMemberTitle == 4526
        && (int)NativeCorpsWriteOp.DismissMember == 4527 && (int)NativeCorpsWriteOp.TransferCaptain == 4528
        && (int)NativeCorpsWriteOp.AppointVice == 4529 && (int)NativeCorpsWriteOp.StepDown == 4530
        && (int)NativeCorpsWriteOp.SetRecruit == 4532, "op idents");
}

static void VerifyRequestJoin()
{
    // Gate sub_5EA444 -> 7 for any role.
    foreach (var role in AllRoles())
        Assert(Eval(NativeCorpsWriteOp.RequestJoin,
            new NativeCorpsWriteContext { Role = role, TargetFound = false }) == 7,
            $"request-join no-target ({role}) -> 7");

    // Only NoCorps reaches sub_701C58; every in-corps role hits sub_702C58 (555).
    Assert(Eval(NativeCorpsWriteOp.RequestJoin,
        new NativeCorpsWriteContext { Role = NativeGildRole.NoCorps, TargetFound = true, StrategyResult = 0 }) == 0,
        "request-join NoCorps -> strategy(0)");
    foreach (var role in new[] { NativeGildRole.Member, NativeGildRole.Corps, NativeGildRole.GildMember,
                                 NativeGildRole.GildVice, NativeGildRole.GildOwner })
        Assert(Eval(NativeCorpsWriteOp.RequestJoin,
            new NativeCorpsWriteContext { Role = role, TargetFound = true, StrategyResult = 0 }) == 555,
            $"request-join {role} -> 555");
}

static void VerifyCancelJoin()
{
    // Gate sub_6A52A0 -> 10 for any role; otherwise the shared sub_7019F0 result.
    foreach (var role in AllRoles())
    {
        Assert(Eval(NativeCorpsWriteOp.CancelJoin,
            new NativeCorpsWriteContext { Role = role, HasPendingRequest = false }) == 10,
            $"cancel-join no-request ({role}) -> 10");
        Assert(Eval(NativeCorpsWriteOp.CancelJoin,
            new NativeCorpsWriteContext { Role = role, HasPendingRequest = true, StrategyResult = 0 }) == 0,
            $"cancel-join {role} -> strategy(0)");
    }
}

static void VerifyCreate()
{
    // Already-in-corps gate -> 3 (true for every non-NoCorps role); NoCorps reaches sub_701A74.
    foreach (var role in AllRoles())
        Assert(Eval(NativeCorpsWriteOp.Create,
            new NativeCorpsWriteContext { Role = role, HasCorpsMembership = true }) == 3,
            $"create already-in-corps ({role}) -> 3");
    Assert(Eval(NativeCorpsWriteOp.Create,
        new NativeCorpsWriteContext { Role = NativeGildRole.NoCorps, HasCorpsMembership = false, StrategyResult = 0 }) == 0,
        "create NoCorps -> strategy(0)");
}

static void VerifyManagerOps()
{
    // 4526 / 4527 / 4532: NoCorps/Member -> 555; Corps/GildMember/GildVice/GildOwner -> real.
    foreach (var op in new[] { NativeCorpsWriteOp.SetMemberTitle, NativeCorpsWriteOp.DismissMember,
                               NativeCorpsWriteOp.SetRecruit })
    {
        foreach (var role in new[] { NativeGildRole.NoCorps, NativeGildRole.Member })
            Assert(Eval(op, new NativeCorpsWriteContext { Role = role, StrategyResult = 0 }) == 555,
                $"{op} {role} -> 555");
        foreach (var role in new[] { NativeGildRole.Corps, NativeGildRole.GildMember,
                                     NativeGildRole.GildVice, NativeGildRole.GildOwner })
            Assert(Eval(op, new NativeCorpsWriteContext { Role = role, StrategyResult = 0 }) == 0,
                $"{op} {role} -> strategy(0)");
    }
    Assert(NativeCorpsWriteTransaction.IsManager(NativeGildRole.Corps), "IsManager Corps");
    Assert(!NativeCorpsWriteTransaction.IsManager(NativeGildRole.Member), "!IsManager Member");
}

static void VerifyCaptainOps()
{
    // 4528 / 4529: NoCorps/Member/Corps -> 555; GildMember/GildVice/GildOwner -> real.
    foreach (var op in new[] { NativeCorpsWriteOp.TransferCaptain, NativeCorpsWriteOp.AppointVice })
    {
        foreach (var role in new[] { NativeGildRole.NoCorps, NativeGildRole.Member, NativeGildRole.Corps })
            Assert(Eval(op, new NativeCorpsWriteContext { Role = role, StrategyResult = 0 }) == 555,
                $"{op} {role} -> 555");
        foreach (var role in new[] { NativeGildRole.GildMember, NativeGildRole.GildVice, NativeGildRole.GildOwner })
            Assert(Eval(op, new NativeCorpsWriteContext { Role = role, StrategyResult = 0 }) == 0,
                $"{op} {role} -> strategy(0)");
    }
    Assert(NativeCorpsWriteTransaction.IsCaptain(NativeGildRole.GildMember), "IsCaptain GildMember");
    Assert(!NativeCorpsWriteTransaction.IsCaptain(NativeGildRole.Corps), "!IsCaptain Corps");
}

static void VerifyStepDown()
{
    // ONLY Corps (corps_vice_owner) reaches sub_70209C; every other role, including the captains
    // (sub_702F80 stub), returns 555.
    Assert(Eval(NativeCorpsWriteOp.StepDown,
        new NativeCorpsWriteContext { Role = NativeGildRole.Corps, StrategyResult = 0 }) == 0,
        "stepdown Corps -> strategy(0)");
    foreach (var role in new[] { NativeGildRole.NoCorps, NativeGildRole.Member, NativeGildRole.GildMember,
                                 NativeGildRole.GildVice, NativeGildRole.GildOwner })
        Assert(Eval(NativeCorpsWriteOp.StepDown,
            new NativeCorpsWriteContext { Role = role, StrategyResult = 0 }) == 555,
            $"stepdown {role} -> 555");
}

static void VerifyStrategyPassthrough()
{
    // The permitted real method's terminal code is returned verbatim (polymorphic sub-result).
    Assert(Eval(NativeCorpsWriteOp.SetMemberTitle,
        new NativeCorpsWriteContext { Role = NativeGildRole.Corps, StrategyResult = 18 }) == 18,
        "title strategy 18 passthrough");
    Assert(Eval(NativeCorpsWriteOp.AppointVice,
        new NativeCorpsWriteContext { Role = NativeGildRole.GildOwner, StrategyResult = 31 }) == 31,
        "appoint strategy 31 passthrough");
    Assert(Eval(NativeCorpsWriteOp.RequestJoin,
        new NativeCorpsWriteContext { Role = NativeGildRole.NoCorps, TargetFound = true, StrategyResult = 9 }) == 9,
        "request-join strategy 9 passthrough");
    Assert(Eval(NativeCorpsWriteOp.StepDown,
        new NativeCorpsWriteContext { Role = NativeGildRole.Corps, StrategyResult = 1000 }) == 1000,
        "stepdown strategy 1000 passthrough");
}
