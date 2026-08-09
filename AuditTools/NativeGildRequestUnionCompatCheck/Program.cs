using GameSvr;

// Contract check for the dormant Gild request_union (4573) model, locked against sub_6F6390,
// the sub_5E76F0 precondition gate, the sub_6ADA3C role dispatch, the sub_701D10 (555) non-owner
// stub, and the gild_owner ladder sub_704494 (5/12/25/19/34/15/33/8/0 + manager passthrough).

try
{
    VerifyConstants();
    VerifyGate();
    VerifyRoleGate();
    VerifyOwnerLadder();

    Console.WriteLine(
        "PASS NativeGildRequestUnionCompatCheck 4573=request_union gate=sub_5E76F0(->12) " +
        "role-dispatch=sub_6ADA3C owner-only=sub_704494(5/12/25/19/34/15/33/8/0) " +
        "non-owner=555 dormant=true");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"NativeGildRequestUnionCompatCheck FAIL: {ex.Message}");
    return 1;
}

static void Assert(bool cond, string msg)
{
    if (!cond)
        throw new Exception(msg);
}

static int Eval(NativeGildRequestUnionContext c) =>
    NativeGildRequestUnionTransaction.Evaluate(c);

// Factory whose defaults are the fully-successful GildOwner case (Ctx() => 0); each test overrides
// exactly one field. Written this way because the context is a plain class (no `with` expression).
static NativeGildRequestUnionContext Ctx(
    NativeGildRole role = NativeGildRole.GildOwner,
    bool preconditionMet = true,
    bool hasPlayer = true,
    bool hasGild = true,
    bool targetGildFound = true,
    bool targetIsOwnGild = false,
    bool targetAllowsUnion = true,
    int existingRelation = 0,
    bool duplicatePending = false,
    int managerResult = 0) =>
    new NativeGildRequestUnionContext
    {
        Role = role,
        PreconditionMet = preconditionMet,
        HasPlayer = hasPlayer,
        HasGild = hasGild,
        TargetGildFound = targetGildFound,
        TargetIsOwnGild = targetIsOwnGild,
        TargetAllowsUnion = targetAllowsUnion,
        ExistingRelation = existingRelation,
        DuplicatePending = duplicatePending,
        ManagerResult = managerResult,
    };

static void VerifyConstants()
{
    Assert(NativeGildRequestUnionTransaction.Ident == 4573, "ident 4573");
    Assert(NativeGildRequestUnionTransaction.VtblRequestUnion == 0x64, "vtbl slot +0x64");
    Assert(NativeGildRequestUnionTransaction.VtblSendDefMessage == 0x250, "vtbl send +0x250");
    Assert(NativeGildRequestUnionTransaction.RelationAllied == 1, "relation allied 1");
    Assert(NativeGildRequestUnionTransaction.RelationWar == 2, "relation war 2");
    Assert(NativeGildRequestUnionTransaction.GateDefault == 12, "gate default 12");
    Assert(NativeGildRequestUnionTransaction.NoPermission == 555, "no permission 555");
    Assert(NativeGildRequestUnionTransaction.NoPlayer == 5, "no player 5");
    Assert(NativeGildRequestUnionTransaction.GildEmpty == 12, "gild empty 12");
    Assert(NativeGildRequestUnionTransaction.TargetNotFound == 25, "target not found 25");
    Assert(NativeGildRequestUnionTransaction.TargetIsSelf == 19, "target self 19");
    Assert(NativeGildRequestUnionTransaction.TargetDisallowsUnion == 34, "target disallows 34");
    Assert(NativeGildRequestUnionTransaction.AlreadyAllied == 15, "already allied 15");
    Assert(NativeGildRequestUnionTransaction.AtWar == 33, "at war 33");
    Assert(NativeGildRequestUnionTransaction.DuplicatePendingRequest == 8, "duplicate pending 8");
    Assert(NativeGildRequestUnionTransaction.Success == 0, "success 0");
}

static void VerifyGate()
{
    // sub_5E76F0 false -> handler returns the pre-initialised n12 = 12 for ANY role, short-circuiting
    // both the role gate and the ladder. Owner with an otherwise-successful context still yields 12.
    Assert(Eval(Ctx(preconditionMet: false)) == 12, "gate false (owner) -> 12");
    Assert(Eval(Ctx(role: NativeGildRole.Member, preconditionMet: false)) == 12, "gate false (member) -> 12");
}

static void VerifyRoleGate()
{
    // Slot +0x64 is sub_701D10 (return 555) for every non-owner role; only gild_owner -> sub_704494.
    // Use an otherwise-successful context to prove the role gate wins before the ladder runs.
    foreach (var role in new[]
    {
        NativeGildRole.NoCorps, NativeGildRole.Member, NativeGildRole.Corps,
        NativeGildRole.GildMember, NativeGildRole.GildVice,
    })
    {
        Assert(Eval(Ctx(role: role)) == 555, $"role {role} -> 555");
    }

    // gild_owner reaches the ladder (not 555 for a success context).
    Assert(Eval(Ctx(role: NativeGildRole.GildOwner)) == 0, "gild_owner reaches ladder -> 0");
}

static void VerifyOwnerLadder()
{
    // sub_704494, in branch order.
    Assert(Eval(Ctx(hasPlayer: false)) == 5, "no player -> 5");
    Assert(Eval(Ctx(hasGild: false)) == 12, "no gild -> 12");
    Assert(Eval(Ctx(targetGildFound: false)) == 25, "target not found -> 25");
    Assert(Eval(Ctx(targetIsOwnGild: true)) == 19, "target self -> 19");
    Assert(Eval(Ctx(targetAllowsUnion: false)) == 34, "target disallows union -> 34");
    Assert(Eval(Ctx(existingRelation: 1)) == 15, "existing relation allied -> 15");
    Assert(Eval(Ctx(existingRelation: 2)) == 33, "existing relation war -> 33");

    // Create-request path (relation neither 1 nor 2). A relation value of 0 or >=3 proceeds.
    Assert(Eval(Ctx(existingRelation: 3, duplicatePending: true)) == 8, "duplicate pending (rel=3) -> 8");
    Assert(Eval(Ctx(duplicatePending: true)) == 8, "duplicate pending (rel=0) -> 8");

    // Manager result passes through verbatim: 0 = success, non-zero propagates (polymorphic tail).
    Assert(Eval(Ctx()) == 0, "manager success -> 0");
    Assert(Eval(Ctx(managerResult: 1000)) == 1000, "manager non-zero -> passthrough");
}
