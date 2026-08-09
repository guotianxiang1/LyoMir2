using GameSvr;

// Contract check for 4567 dismiss_corps / 4568 transfer_president / 4569 appoint_vice dormant models
// (sub_704AF8 / sub_7046A8 / sub_7039F8), president-only via sub_6ADA3C dispatch.

try
{
    VerifyConstants();
    VerifyRoleGate();
    VerifyTransfer();
    VerifyAppointVice();
    VerifyDismissCorps();

    Console.WriteLine(
        "PASS NativeGildLeadershipCompatCheck 4568=transfer(18/5/555/12/19/18/0) " +
        "4569=appoint(18/5/555/12/21/19/25/22/0) 4567=dismiss(5/12/7/22/19/555/18/1000/0) " +
        "roles: transfer/appoint=president-only, dismiss=president-or-vice");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"NativeGildLeadershipCompatCheck FAIL: {ex.Message}");
    return 1;
}

static void Assert(bool cond, string msg)
{
    if (!cond)
        throw new Exception(msg);
}

static int Eval(NativeGildLeadershipOp op, NativeGildLeadershipContext c) =>
    NativeGildLeadershipTransaction.Evaluate(op, c);

static void VerifyConstants()
{
    Assert(NativeGildLeadershipTransaction.VtblTransfer == 0x48, "vtbl 4568");
    Assert(NativeGildLeadershipTransaction.VtblAppointVice == 0x50, "vtbl 4569");
    Assert(NativeGildLeadershipTransaction.VtblDismissCorps == 0x54, "vtbl 4567");
    Assert((int)NativeGildLeadershipOp.TransferPresident == 4568, "ident 4568");
    Assert((int)NativeGildLeadershipOp.AppointVice == 4569, "ident 4569");
    Assert((int)NativeGildLeadershipOp.DismissCorps == 4567, "ident 4567");
}

static void VerifyRoleGate()
{
    // transfer(4568)/appoint(4569) are president-only; dismiss(4567) is president
    // OR vice (its +0x54 slot sub_704AF8 is shared by both gild strategies).
    // Every OTHER role returns 555 for all three.
    foreach (var role in new[] { NativeGildRole.NoCorps, NativeGildRole.Member, NativeGildRole.Corps,
                                 NativeGildRole.GildMember, NativeGildRole.GildVice })
    {
        var full = new NativeGildLeadershipContext
        {
            Role = role, IsPresident = true, HasGild = true, TargetFound = true, TargetSameGild = true,
            TargetIsMember = true, TargetRemovable = true, RemoveOk = true,
        };
        Assert(Eval(NativeGildLeadershipOp.TransferPresident, full) == 555, $"4568 role {role}");
        Assert(Eval(NativeGildLeadershipOp.AppointVice, full) == 555, $"4569 role {role}");
        var expectedDismiss = role == NativeGildRole.GildVice ? 0 : 555;
        Assert(Eval(NativeGildLeadershipOp.DismissCorps, full) == expectedDismiss, $"4567 role {role}");
    }
}

static NativeGildLeadershipContext Owner() => new()
{
    Role = NativeGildRole.GildOwner, ValidArgs = true, HasPlayer = true, IsPresident = true,
    HasGild = true, TargetFound = true, TargetSameGild = true, TargetIsMember = true,
    TargetRemovable = true, RemoveOk = true,
};

static void VerifyTransfer()
{
    var op = NativeGildLeadershipOp.TransferPresident;
    Assert(Eval(op, new NativeGildLeadershipContext { Role = NativeGildRole.GildOwner, ValidArgs = false }) == 18,
        "4568 bad args");
    Assert(Eval(op, new NativeGildLeadershipContext { Role = NativeGildRole.GildOwner, HasPlayer = false }) == 5,
        "4568 no player");
    Assert(Eval(op, new NativeGildLeadershipContext { Role = NativeGildRole.GildOwner, IsPresident = false }) == 555,
        "4568 not president");
    var g = Owner(); Assert(Eval(op, Without(g, hasGild: false)) == 12, "4568 no gild");
    Assert(Eval(op, With(Owner(), targetSelf: true)) == 19, "4568 target self");
    Assert(Eval(op, With(Owner(), targetMember: false)) == 18, "4568 target not member");
    Assert(Eval(op, Owner()) == 0, "4568 success");
}

static void VerifyAppointVice()
{
    var op = NativeGildLeadershipOp.AppointVice;
    Assert(Eval(op, new NativeGildLeadershipContext { Role = NativeGildRole.GildOwner, ValidArgs = false }) == 18,
        "4569 bad args");
    Assert(Eval(op, new NativeGildLeadershipContext { Role = NativeGildRole.GildOwner, HasPlayer = false }) == 5,
        "4569 no player");
    Assert(Eval(op, new NativeGildLeadershipContext { Role = NativeGildRole.GildOwner, IsPresident = false }) == 555,
        "4569 not president");
    Assert(Eval(op, Without(Owner(), hasGild: false)) == 12, "4569 no gild");
    Assert(Eval(op, With(Owner(), viceOccupied: true)) == 21, "4569 vice occupied");
    Assert(Eval(op, With(Owner(), targetSelf: true)) == 19, "4569 target self");
    Assert(Eval(op, With(Owner(), targetFound: false)) == 25, "4569 target not found");
    Assert(Eval(op, With(Owner(), targetSameGild: false)) == 22, "4569 wrong gild");
    Assert(Eval(op, Owner()) == 0, "4569 success");
}

static void VerifyDismissCorps()
{
    var op = NativeGildLeadershipOp.DismissCorps;
    Assert(Eval(op, new NativeGildLeadershipContext { Role = NativeGildRole.GildOwner, HasPlayer = false }) == 5,
        "4567 no player");
    Assert(Eval(op, Without(Owner(), hasGild: false)) == 12, "4567 no gild");
    Assert(Eval(op, With(Owner(), targetFound: false)) == 7, "4567 target not found");
    Assert(Eval(op, With(Owner(), targetSameGild: false)) == 22, "4567 wrong gild");
    Assert(Eval(op, With(Owner(), targetSelf: true)) == 19, "4567 target self");
    Assert(Eval(op, With(Owner(), targetLeadership: true)) == 555, "4567 leadership");
    Assert(Eval(op, With(Owner(), targetRemovable: false)) == 18, "4567 not removable");
    Assert(Eval(op, With(Owner(), removeOk: false)) == 1000, "4567 remove failed");
    Assert(Eval(op, Owner()) == 0, "4567 success");
}

static NativeGildLeadershipContext Without(NativeGildLeadershipContext c, bool hasGild) => new()
{
    Role = c.Role, ValidArgs = c.ValidArgs, HasPlayer = c.HasPlayer, IsPresident = c.IsPresident,
    HasGild = hasGild, ViceOccupied = c.ViceOccupied, TargetIsSelf = c.TargetIsSelf,
    TargetFound = c.TargetFound, TargetSameGild = c.TargetSameGild, TargetIsMember = c.TargetIsMember,
    TargetIsLeadership = c.TargetIsLeadership, TargetRemovable = c.TargetRemovable, RemoveOk = c.RemoveOk,
};

static NativeGildLeadershipContext With(NativeGildLeadershipContext c, bool targetSelf = false,
    bool targetMember = true, bool viceOccupied = false, bool targetFound = true, bool targetSameGild = true,
    bool targetLeadership = false, bool targetRemovable = true, bool removeOk = true) => new()
{
    Role = c.Role, ValidArgs = c.ValidArgs, HasPlayer = c.HasPlayer, IsPresident = c.IsPresident,
    HasGild = c.HasGild, ViceOccupied = viceOccupied, TargetIsSelf = targetSelf,
    TargetFound = targetFound, TargetSameGild = targetSameGild, TargetIsMember = targetMember,
    TargetIsLeadership = targetLeadership, TargetRemovable = targetRemovable, RemoveOk = removeOk,
};
