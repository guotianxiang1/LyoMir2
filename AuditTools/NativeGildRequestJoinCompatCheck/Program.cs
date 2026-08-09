using GameSvr;

// Contract check for the dormant Gild join-request model (4560 request_join), locked against
// sub_6F5958 (handler, target gate 12 + reply SM 4560 via vtbl+0x254), the sub_6ADA3C role dispatch,
// sub_701D04 (NoCorps/Member/Corps +0x40 -> 555 stub) and sub_703624 (GildMember/GildVice/GildOwner
// +0x40 -> ladder 5/6/8/0). Enum<->VMT map per NativeGildRequestUnionTransaction.cs.

try
{
    VerifyConstants();
    VerifyTargetGatePrecedesRole();
    VerifyRoleGate();
    VerifyCaptainLadder();
    VerifyOfficersReachLadder();
    VerifyPendingRequestOutcome();

    Console.WriteLine(
        "PASS NativeGildRequestJoinCompatCheck 4560=request_join " +
        "target-gate=12 role(NoCorps/Member/Corps)=555 " +
        "captain-ladder(5/6/8/manager/0) reply=4560 pending=in-memory dormant=true");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"NativeGildRequestJoinCompatCheck FAIL: {ex.Message}");
    return 1;
}

static void Assert(bool cond, string msg)
{
    if (!cond)
        throw new Exception(msg);
}

static int Eval(NativeGildRequestJoinContext c) =>
    NativeGildRequestJoinTransaction.Evaluate(c);

// A fully-successful GildMember (corps captain, no gild yet, no duplicate, manager succeeds) context.
static NativeGildRequestJoinContext Success() => new()
{
    Role = NativeGildRole.GildMember,
    TargetGildFound = true,
    HasPlayer = true,
    HasGild = false,
    DuplicateRequest = false,
    ManagerResult = 0,
};

static void VerifyConstants()
{
    Assert(NativeGildRequestJoinTransaction.ReplySmId == 4560, "reply SM 4560");
    Assert(NativeGildRequestJoinTransaction.VtblStrategy == 0x40, "vtbl strategy +0x40");
    Assert(NativeGildRequestJoinTransaction.VtblBufferedSend == 0x254, "vtbl buffered send");
    Assert(NativeGildRequestJoinTransaction.TargetNotFound == 12, "code 12");
    Assert(NativeGildRequestJoinTransaction.NoPermission == 555, "code 555");
    Assert(NativeGildRequestJoinTransaction.NoPlayer == 5, "code 5");
    Assert(NativeGildRequestJoinTransaction.AlreadyInGild == 6, "code 6");
    Assert(NativeGildRequestJoinTransaction.DuplicatePending == 8, "code 8");
    Assert(NativeGildRequestJoinTransaction.Success == 0, "code 0");
}

static void VerifyTargetGatePrecedesRole()
{
    // sub_5E76D4 gate runs before the role dispatch: target not found -> 12 for EVERY role,
    // including both the 555 roles and the ladder roles.
    foreach (var role in Enum.GetValues<NativeGildRole>())
        Assert(Eval(new NativeGildRequestJoinContext { Role = role, TargetGildFound = false }) == 12,
            $"target not found ({role}) -> 12");
}

static void VerifyRoleGate()
{
    // With the target found, the three non-captain roles hit sub_701D04 -> 555.
    foreach (var role in new[] { NativeGildRole.NoCorps, NativeGildRole.Member, NativeGildRole.Corps })
        Assert(Eval(new NativeGildRequestJoinContext
        {
            Role = role, TargetGildFound = true, HasPlayer = true, HasGild = false,
        }) == 555, $"role {role} -> 555");
}

static void VerifyCaptainLadder()
{
    // GildMember (corps_owner) reaches sub_703624 and walks the full ladder.
    Assert(Eval(new NativeGildRequestJoinContext
    {
        Role = NativeGildRole.GildMember, TargetGildFound = true, HasPlayer = false,
    }) == 5, "captain no player/not-self -> 5");

    Assert(Eval(new NativeGildRequestJoinContext
    {
        Role = NativeGildRole.GildMember, TargetGildFound = true, HasPlayer = true, HasGild = true,
    }) == 6, "captain already in gild -> 6");

    Assert(Eval(new NativeGildRequestJoinContext
    {
        Role = NativeGildRole.GildMember, TargetGildFound = true, HasPlayer = true, HasGild = false,
        DuplicateRequest = true,
    }) == 8, "captain duplicate request -> 8");

    Assert(Eval(Success()) == 0, "captain success -> 0");

    // The manager-add tail is polymorphic: a non-zero sub_6A4F80 result is returned verbatim.
    Assert(Eval(new NativeGildRequestJoinContext
    {
        Role = NativeGildRole.GildMember, TargetGildFound = true, HasPlayer = true, HasGild = false,
        DuplicateRequest = false, ManagerResult = 9,
    }) == 9, "captain manager error passthrough -> 9");
}

static void VerifyOfficersReachLadder()
{
    // GildVice/GildOwner also route to sub_703624 (not the 555 stub); in the live path they already
    // have a gild, so they hit the "already in a gild" -> 6 branch.
    Assert(Eval(new NativeGildRequestJoinContext
    {
        Role = NativeGildRole.GildVice, TargetGildFound = true, HasPlayer = true, HasGild = true,
    }) == 6, "gild vice already in gild -> 6 (reached ladder)");

    Assert(Eval(new NativeGildRequestJoinContext
    {
        Role = NativeGildRole.GildOwner, TargetGildFound = true, HasPlayer = true, HasGild = true,
    }) == 6, "gild owner already in gild -> 6 (reached ladder)");

    // Prove they are past the 555 stub: an officer with no gild would proceed to the manager tail.
    Assert(Eval(new NativeGildRequestJoinContext
    {
        Role = NativeGildRole.GildOwner, TargetGildFound = true, HasPlayer = true, HasGild = false,
        DuplicateRequest = false, ManagerResult = 0,
    }) == 0, "gild owner no-gild reaches manager -> 0 (not 555)");
}

static void VerifyPendingRequestOutcome()
{
    // The pending join request is created (and published) iff the final code is 0.
    Assert(NativeGildRequestJoinTransaction.CreatesPendingRequest(0), "pending on 0");
    Assert(!NativeGildRequestJoinTransaction.CreatesPendingRequest(8), "no pending on 8");
    Assert(!NativeGildRequestJoinTransaction.CreatesPendingRequest(6), "no pending on 6");
    Assert(!NativeGildRequestJoinTransaction.CreatesPendingRequest(555), "no pending on 555");
}
