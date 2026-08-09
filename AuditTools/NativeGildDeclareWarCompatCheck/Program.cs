using GameSvr;

// Contract check for the dormant Gild declare-war model (4579 declare_war_id / 4585 declare_war_name),
// locked against sub_6F68F0, sub_6F6958, the sub_6ADA3C role dispatch, sub_703F74 (gild_owner +0x6C),
// sub_701BD8 (non-owner +0x6C -> 555), sub_5E76F0 (4585 name guard) and sub_5E6E60 (save_relation,
// Relation=2). Both ops reply with SM 4579.

try
{
    VerifyConstants();
    VerifyGoldGatePrecedesRole();
    VerifyRoleGate();
    VerifyOwnerLadder();
    VerifyRelationHelperPassThrough();
    VerifyNamePathGuard();
    VerifyGoldDeductionOutcome();

    Console.WriteLine(
        "PASS NativeGildDeclareWarCompatCheck 4579/4585=declare_war " +
        "ladder(36/555/5/12/25/19/32/15/helper/0) gold-gate-before-role reply=4579 " +
        "relation=INSERT(2) gold=-30000 name-guard=12 role-dispatch=sub_6ADA3C dormant=true");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"NativeGildDeclareWarCompatCheck FAIL: {ex.Message}");
    return 1;
}

static void Assert(bool cond, string msg)
{
    if (!cond)
        throw new Exception(msg);
}

static int Id(NativeGildDeclareWarContext c) =>
    NativeGildDeclareWarTransaction.Evaluate(NativeGildDeclareWarOp.DeclareWarId, c);

static int Name(NativeGildDeclareWarContext c) =>
    NativeGildDeclareWarTransaction.Evaluate(NativeGildDeclareWarOp.DeclareWarName, c);

// A fully-successful gild_owner context (all guards satisfied, no relation conflict, helper succeeds).
static NativeGildDeclareWarContext Success() => new()
{
    Role = NativeGildRole.GildOwner,
    HasGold = true,
    HasGild = true,
    TargetGildFound = true,
    TargetIsSelf = false,
    RelationState = 0,
    RelationHelperResult = 0,
};

static void VerifyConstants()
{
    Assert(NativeGildDeclareWarTransaction.ReplySmId == 4579, "reply SM 4579");
    Assert(NativeGildDeclareWarTransaction.VtblStrategy == 0x6C, "vtbl strategy +0x6C");
    Assert(NativeGildDeclareWarTransaction.VtblSendDefMessage == 0x250, "vtbl send");
    Assert(NativeGildDeclareWarTransaction.GoldCost == 30000, "gold cost 30000");
    Assert(NativeGildDeclareWarTransaction.GoldThreshold == 30000, "gold threshold 30000");
    Assert(NativeGildDeclareWarTransaction.RelationType == 2, "relation type 2");
    Assert((int)NativeGildDeclareWarOp.DeclareWarId == 4579, "ident 4579");
    Assert((int)NativeGildDeclareWarOp.DeclareWarName == 4585, "ident 4585");
    Assert(NativeGildDeclareWarTransaction.GoldInsufficient == 36, "code 36");
    Assert(NativeGildDeclareWarTransaction.NameUnresolved == 12, "code 12 (name)");
    Assert(NativeGildDeclareWarTransaction.NoPermission == 555, "code 555");
    Assert(NativeGildDeclareWarTransaction.NoPlayer == 5, "code 5");
    Assert(NativeGildDeclareWarTransaction.NoGild == 12, "code 12 (no gild)");
    Assert(NativeGildDeclareWarTransaction.TargetNotFound == 25, "code 25");
    Assert(NativeGildDeclareWarTransaction.TargetIsSelfCode == 19, "code 19");
    Assert(NativeGildDeclareWarTransaction.RelationBusyState1 == 32, "code 32");
    Assert(NativeGildDeclareWarTransaction.RelationBusyState2 == 15, "code 15");
    Assert(NativeGildDeclareWarTransaction.Success == 0, "code 0");
}

static void VerifyGoldGatePrecedesRole()
{
    // Both handlers test gold before dispatching the strategy: insufficient gold -> 36, never 555.
    Assert(Id(new NativeGildDeclareWarContext { Role = NativeGildRole.GildOwner, HasGold = false }) == 36,
        "id owner no-gold -> 36");
    Assert(Name(new NativeGildDeclareWarContext { Role = NativeGildRole.GildOwner, HasGold = false }) == 36,
        "name owner no-gold -> 36");
    // A non-owner with insufficient gold still gets 36 (gold gate runs first), not 555.
    Assert(Id(new NativeGildDeclareWarContext { Role = NativeGildRole.Member, HasGold = false }) == 36,
        "id non-owner no-gold -> 36 (not 555)");
    Assert(Name(new NativeGildDeclareWarContext { Role = NativeGildRole.GildVice, HasGold = false }) == 36,
        "name non-owner no-gold -> 36 (not 555)");
}

static void VerifyRoleGate()
{
    // With gold present, every non gild_owner role hits sub_701BD8 at +0x6C -> 555.
    foreach (var role in new[] { NativeGildRole.NoCorps, NativeGildRole.Member, NativeGildRole.Corps,
                                 NativeGildRole.GildMember, NativeGildRole.GildVice })
    {
        Assert(Id(new NativeGildDeclareWarContext { Role = role, HasGold = true }) == 555,
            $"id role {role} -> 555");
        Assert(Name(new NativeGildDeclareWarContext { Role = role, HasGold = true }) == 555,
            $"name role {role} -> 555");
    }
}

static void VerifyOwnerLadder()
{
    // gild_owner +0x6C = sub_703F74, walked top to bottom (each branch reached by satisfying the prior).
    Assert(Id(new NativeGildDeclareWarContext
    {
        Role = NativeGildRole.GildOwner, HasGold = true, CallerKeyPresent = false,
    }) == 555, "owner caller-key absent -> 555");

    Assert(Id(new NativeGildDeclareWarContext
    {
        Role = NativeGildRole.GildOwner, HasGold = true, PlayerResolved = false,
    }) == 5, "owner no player -> 5");

    Assert(Id(new NativeGildDeclareWarContext
    {
        Role = NativeGildRole.GildOwner, HasGold = true, HasGild = false,
    }) == 12, "owner no gild -> 12");

    Assert(Id(new NativeGildDeclareWarContext
    {
        Role = NativeGildRole.GildOwner, HasGold = true, HasGild = true, TargetGildFound = false,
    }) == 25, "owner target not found -> 25");

    Assert(Id(new NativeGildDeclareWarContext
    {
        Role = NativeGildRole.GildOwner, HasGold = true, HasGild = true, TargetGildFound = true,
        TargetIsSelf = true,
    }) == 19, "owner target is self -> 19");

    Assert(Id(new NativeGildDeclareWarContext
    {
        Role = NativeGildRole.GildOwner, HasGold = true, HasGild = true, TargetGildFound = true,
        TargetIsSelf = false, RelationState = 1,
    }) == 32, "owner relation state 1 -> 32");

    Assert(Id(new NativeGildDeclareWarContext
    {
        Role = NativeGildRole.GildOwner, HasGold = true, HasGild = true, TargetGildFound = true,
        TargetIsSelf = false, RelationState = 2,
    }) == 15, "owner relation state 2 -> 15");

    // state 0 with a successful helper -> INSERT GildRelation(Relation=2) + success 0.
    Assert(Id(Success()) == 0, "owner success (id) -> 0");
    Assert(Name(Success()) == 0, "owner success (name) -> 0");
}

static void VerifyRelationHelperPassThrough()
{
    // The save_relation helper (sub_5E6E60) result is polymorphic and passed through verbatim.
    // state 0 or >= 3 reaches the helper; a non-zero helper result (e.g. 15) is returned as-is.
    Assert(Id(new NativeGildDeclareWarContext
    {
        Role = NativeGildRole.GildOwner, HasGold = true, HasGild = true, TargetGildFound = true,
        TargetIsSelf = false, RelationState = 0, RelationHelperResult = 15,
    }) == 15, "helper 15 passthrough (state 0)");

    Assert(Id(new NativeGildDeclareWarContext
    {
        Role = NativeGildRole.GildOwner, HasGold = true, HasGild = true, TargetGildFound = true,
        TargetIsSelf = false, RelationState = 3, RelationHelperResult = 0,
    }) == 0, "state 3 reaches helper -> 0");
}

static void VerifyNamePathGuard()
{
    // 4585 only: sub_5E76F0 guard precedes the gold gate; false -> 12 even with gold + full success ctx.
    var okButNameFails = new NativeGildDeclareWarContext
    {
        Role = NativeGildRole.GildOwner, HasGold = true, HasGild = true, TargetGildFound = true,
        TargetIsSelf = false, RelationState = 0, RelationHelperResult = 0, NameResolved = false,
    };
    Assert(Name(okButNameFails) == 12, "name guard false -> 12");
    // Guard runs before gold: name false + no gold -> still 12, not 36.
    Assert(Name(new NativeGildDeclareWarContext
    {
        Role = NativeGildRole.GildOwner, HasGold = false, NameResolved = false,
    }) == 12, "name guard precedes gold -> 12");
    // 4579 ignores NameResolved: same context succeeds on the id path.
    Assert(Id(okButNameFails) == 0, "id ignores name guard -> 0");
}

static void VerifyGoldDeductionOutcome()
{
    // The handler deducts 30000 gold iff the final code is 0; the same success path enqueues the INSERT.
    Assert(NativeGildDeclareWarTransaction.DeductsGold(0), "deduct on 0");
    Assert(!NativeGildDeclareWarTransaction.DeductsGold(36), "no deduct on 36");
    Assert(!NativeGildDeclareWarTransaction.DeductsGold(555), "no deduct on 555");
    Assert(!NativeGildDeclareWarTransaction.DeductsGold(15), "no deduct on 15");
    Assert(NativeGildDeclareWarTransaction.InsertsRelation(0), "insert on 0");
    Assert(!NativeGildDeclareWarTransaction.InsertsRelation(15), "no insert on 15");
}
