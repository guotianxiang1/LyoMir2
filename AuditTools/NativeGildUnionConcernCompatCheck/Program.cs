using GameSvr;

// Contract check for 4574 break_union / 4576 add_concern / 4581 enable_union dormant models
// (sub_703CEC / sub_703ED4 / sub_704EAC), with sub_6ADA3C role dispatch.

try
{
    VerifyConstants();
    VerifyBreakUnion();
    VerifyAddConcern();
    VerifyEnableUnion();

    Console.WriteLine(
        "PASS NativeGildUnionConcernCompatCheck 4574=break(5/12/25/27/1000/0,owner) " +
        "4576=concern(5/12/25/19/1000/0,owner) 4581=enable(5/12/0,owner+vice) dormant=true");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"NativeGildUnionConcernCompatCheck FAIL: {ex.Message}");
    return 1;
}

static void Assert(bool cond, string msg)
{
    if (!cond)
        throw new Exception(msg);
}

static int Eval(NativeGildUnionConcernOp op, NativeGildUnionConcernContext c) =>
    NativeGildUnionConcernTransaction.Evaluate(op, c);

static void VerifyConstants()
{
    Assert(NativeGildUnionConcernTransaction.VtblBreakUnion == 0x68, "vtbl 4574");
    Assert(NativeGildUnionConcernTransaction.VtblAddConcern == 0x5C, "vtbl 4576");
    Assert(NativeGildUnionConcernTransaction.VtblEnableUnion == 0x58, "vtbl 4581");
    Assert((int)NativeGildUnionConcernOp.BreakUnion == 4574, "ident 4574");
    Assert((int)NativeGildUnionConcernOp.AddConcern == 4576, "ident 4576");
    Assert((int)NativeGildUnionConcernOp.EnableUnion == 4581, "ident 4581");
}

static NativeGildUnionConcernContext Base(NativeGildRole role) => new()
{
    Role = role, HasGild = true, OtherGildFound = true, Allied = true, RelationRemovable = true,
    ConcernAdded = true, FlagChanged = true,
};

static void VerifyBreakUnion()
{
    var op = NativeGildUnionConcernOp.BreakUnion;
    // owner-only
    foreach (var role in new[] { NativeGildRole.NoCorps, NativeGildRole.Member, NativeGildRole.Corps,
                                 NativeGildRole.GildMember, NativeGildRole.GildVice })
        Assert(Eval(op, Base(role)) == 555, $"4574 role {role} -> 555");

    var o = NativeGildRole.GildOwner;
    Assert(Eval(op, new NativeGildUnionConcernContext { Role = o, HasPlayer = false }) == 5, "4574 no player");
    Assert(Eval(op, new NativeGildUnionConcernContext { Role = o, HasGild = false }) == 12, "4574 no gild");
    Assert(Eval(op, new NativeGildUnionConcernContext { Role = o, HasGild = true, OtherGildFound = false }) == 25,
        "4574 no target");
    Assert(Eval(op, new NativeGildUnionConcernContext { Role = o, HasGild = true, OtherGildFound = true, Allied = false }) == 27,
        "4574 not allied");
    Assert(Eval(op, new NativeGildUnionConcernContext
    {
        Role = o, HasGild = true, OtherGildFound = true, Allied = true, RelationRemovable = false,
    }) == 1000, "4574 relation not removable");
    Assert(Eval(op, Base(o)) == 0, "4574 success");
}

static void VerifyAddConcern()
{
    var op = NativeGildUnionConcernOp.AddConcern;
    foreach (var role in new[] { NativeGildRole.GildVice, NativeGildRole.GildMember, NativeGildRole.Member })
        Assert(Eval(op, Base(role)) == 555, $"4576 role {role} -> 555");

    var o = NativeGildRole.GildOwner;
    Assert(Eval(op, new NativeGildUnionConcernContext { Role = o, HasPlayer = false }) == 5, "4576 no player");
    Assert(Eval(op, new NativeGildUnionConcernContext { Role = o, HasGild = false }) == 12, "4576 no gild");
    Assert(Eval(op, new NativeGildUnionConcernContext { Role = o, HasGild = true, OtherGildFound = false }) == 25,
        "4576 no target");
    Assert(Eval(op, new NativeGildUnionConcernContext
    {
        Role = o, HasGild = true, OtherGildFound = true, TargetIsSelf = true,
    }) == 19, "4576 self-concern");
    Assert(Eval(op, new NativeGildUnionConcernContext
    {
        Role = o, HasGild = true, OtherGildFound = true, ConcernAdded = false,
    }) == 1000, "4576 duplicate");
    Assert(Eval(op, Base(o)) == 0, "4576 success");
}

static void VerifyEnableUnion()
{
    var op = NativeGildUnionConcernOp.EnableUnion;
    // owner and vice allowed; others 555.
    foreach (var role in new[] { NativeGildRole.NoCorps, NativeGildRole.Member, NativeGildRole.Corps,
                                 NativeGildRole.GildMember })
        Assert(Eval(op, Base(role)) == 555, $"4581 role {role} -> 555");

    foreach (var role in new[] { NativeGildRole.GildOwner, NativeGildRole.GildVice })
    {
        Assert(Eval(op, new NativeGildUnionConcernContext { Role = role, HasPlayer = false }) == 5, $"4581 {role} no player");
        Assert(Eval(op, new NativeGildUnionConcernContext { Role = role, HasGild = false }) == 12, $"4581 {role} no gild");
        // result 0 whether or not the flag changed (only the DB UPDATE is conditional).
        Assert(Eval(op, new NativeGildUnionConcernContext { Role = role, HasGild = true, FlagChanged = true }) == 0,
            $"4581 {role} changed");
        Assert(Eval(op, new NativeGildUnionConcernContext { Role = role, HasGild = true, FlagChanged = false }) == 0,
            $"4581 {role} unchanged");
    }
}
