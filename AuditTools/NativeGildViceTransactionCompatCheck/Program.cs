using GameSvr;

// Contract check for the dormant Gild vice-op model (4587 self stepdown / 4588 president dismiss),
// locked against sub_704CC0, sub_704228, sub_701C1C and the sub_6ADA3C role dispatch.

try
{
    VerifyConstants();
    VerifySelfStepDownRoleGate();
    VerifySelfStepDownLadder();
    VerifyDismissRoleGate();
    VerifyDismissLadder();

    Console.WriteLine(
        "PASS NativeGildViceTransactionCompatCheck 4587=self(5/12/555/0,vice-only) " +
        "4588=dismiss(5/12/555/22/0,owner-only) role-dispatch=sub_6ADA3C dormant=true");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"NativeGildViceTransactionCompatCheck FAIL: {ex.Message}");
    return 1;
}

static void Assert(bool cond, string msg)
{
    if (!cond)
        throw new Exception(msg);
}

static int Self(NativeGildViceContext c) =>
    NativeGildViceTransaction.Evaluate(NativeGildViceOp.SelfStepDown, c);

static int Dismiss(NativeGildViceContext c) =>
    NativeGildViceTransaction.Evaluate(NativeGildViceOp.PresidentDismiss, c);

static void VerifyConstants()
{
    Assert(NativeGildViceTransaction.VtblSelfStepDown == 0x78, "vtbl 4587");
    Assert(NativeGildViceTransaction.VtblDismiss == 0x74, "vtbl 4588");
    Assert(NativeGildViceTransaction.VtblSendDefMessage == 0x250, "vtbl send");
    Assert((int)NativeGildViceOp.SelfStepDown == 4587, "ident 4587");
    Assert((int)NativeGildViceOp.PresidentDismiss == 4588, "ident 4588");
    Assert(NativeGildViceTransaction.NoGild == 5 && NativeGildViceTransaction.GildEmpty == 12
        && NativeGildViceTransaction.NoPermission == 555 && NativeGildViceTransaction.TargetInvalid == 22
        && NativeGildViceTransaction.Success == 0, "codes");
}

static void VerifySelfStepDownRoleGate()
{
    // Only gild_owner/gild_vice strategies reach sub_704CC0; all other roles return 555 at +0x78.
    foreach (var role in new[] { NativeGildRole.NoCorps, NativeGildRole.Member, NativeGildRole.Corps,
                                 NativeGildRole.GildMember })
        Assert(Self(new NativeGildViceContext
        {
            Role = role, HasGild = true, GildHasVice = true, CallerIsTheVice = true,
        }) == 555, $"self role {role} must be 555");
}

static void VerifySelfStepDownLadder()
{
    // Reaches sub_704CC0 via gild_vice; ladder no-gild 5 / empty 12 / not-the-vice 555 / success 0.
    Assert(Self(new NativeGildViceContext { Role = NativeGildRole.GildVice, HasPlayer = false }) == 5,
        "self no player -> 5");
    Assert(Self(new NativeGildViceContext { Role = NativeGildRole.GildVice, HasGild = false }) == 12,
        "self no gild -> 12");
    Assert(Self(new NativeGildViceContext
    {
        Role = NativeGildRole.GildVice, HasGild = true, GildHasVice = false, CallerIsTheVice = false,
    }) == 555, "self no vice -> 555");
    // gild_owner reaches sub_704CC0 but is not the vice -> 555.
    Assert(Self(new NativeGildViceContext
    {
        Role = NativeGildRole.GildOwner, HasGild = true, GildHasVice = true, CallerIsTheVice = false,
    }) == 555, "self owner-not-vice -> 555");
    // the actual vice succeeds.
    Assert(Self(new NativeGildViceContext
    {
        Role = NativeGildRole.GildVice, HasGild = true, GildHasVice = true, CallerIsTheVice = true,
    }) == 0, "self vice -> 0");
}

static void VerifyDismissRoleGate()
{
    // Only gild_owner reaches sub_704228; gild_vice -> sub_701C1C (555); others -> 555.
    foreach (var role in new[] { NativeGildRole.NoCorps, NativeGildRole.Member, NativeGildRole.Corps,
                                 NativeGildRole.GildMember, NativeGildRole.GildVice })
        Assert(Dismiss(new NativeGildViceContext
        {
            Role = role, HasGild = true, CallerIsPresident = true, TargetFound = true, TargetIsVice = true,
        }) == 555, $"dismiss role {role} must be 555");
}

static void VerifyDismissLadder()
{
    Assert(Dismiss(new NativeGildViceContext { Role = NativeGildRole.GildOwner, HasPlayer = false }) == 5,
        "dismiss no player -> 5");
    Assert(Dismiss(new NativeGildViceContext { Role = NativeGildRole.GildOwner, HasGild = false }) == 12,
        "dismiss no gild -> 12");
    Assert(Dismiss(new NativeGildViceContext
    {
        Role = NativeGildRole.GildOwner, HasGild = true, CallerIsPresident = false,
    }) == 555, "dismiss not president -> 555");
    Assert(Dismiss(new NativeGildViceContext
    {
        Role = NativeGildRole.GildOwner, HasGild = true, CallerIsPresident = true, TargetFound = false,
    }) == 22, "dismiss target not found -> 22");
    Assert(Dismiss(new NativeGildViceContext
    {
        Role = NativeGildRole.GildOwner, HasGild = true, CallerIsPresident = true,
        TargetFound = true, TargetIsVice = false,
    }) == 22, "dismiss target not vice -> 22");
    Assert(Dismiss(new NativeGildViceContext
    {
        Role = NativeGildRole.GildOwner, HasGild = true, CallerIsPresident = true,
        TargetFound = true, TargetIsVice = true,
    }) == 0, "dismiss success -> 0");
}
