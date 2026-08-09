using GameSvr;

// Contract check for the WIRED 4611/4572 composition (NativeGildRequestResponseWiredTransaction):
// outer role cascade (sub_7039A0/sub_704930/sub_701D40 accept, sub_70443C/sub_704E54/sub_7029B4 refuse)
// -> request type -> the REAL subtype ladders in NativeGildRequestSubtypeTransaction, with no abstract
// SubtypeResult. Asserts the composed accept (4611) and refuse (4572) codes across all three request
// types plus the outer 10 / 23 / 555 outcomes.
//
// request type() discriminator values used below: 0 = JoinCorps, 1 = JoinGild, 2 = Union.

try
{
    VerifyConstantsAndEntryLevels();
    VerifyOuterCodes();
    VerifyAcceptComposition();
    VerifyRefuseComposition();
    VerifyRoleCascade();

    Console.WriteLine(
        "PASS NativeGildRequestResponseWiredCompatCheck 4611=accept 4572=refuse " +
        "outer(10/23/555) accept(union:555/12/12/555/rel, joingild:555/12/13/5/6/1000/0, " +
        "joincorps:555/5/16/555/0) refuse(union&joingild:555/12/5/0, joincorps:555/0) " +
        "cascade=owner>=vice>=corp de-abstracted=true dormant=true");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"NativeGildRequestResponseWiredCompatCheck FAIL: {ex.Message}");
    return 1;
}

static void Assert(bool cond, string msg)
{
    if (!cond)
        throw new Exception(msg);
}

// president (gild_owner) full-chain accept / refuse of a given request type (0/1/2).
static int Acc(int type, NativeGildRequestSubtypeContext c) =>
    NativeGildRequestResponseWiredTransaction.Evaluate(
        NativeGildRequestSubtypeOp.Accept, NativeGildRole.GildOwner, type, true, c);

static int Ref(int type, NativeGildRequestSubtypeContext c) =>
    NativeGildRequestResponseWiredTransaction.Evaluate(
        NativeGildRequestSubtypeOp.Refuse, NativeGildRole.GildOwner, type, true, c);

static void VerifyConstantsAndEntryLevels()
{
    Assert(NativeGildRequestResponseWiredTransaction.RequestNotFound == 10, "10");
    Assert(NativeGildRequestResponseWiredTransaction.WrongType == 23, "23");
    Assert(NativeGildRequestResponseWiredTransaction.NoPermission == 555, "555");
    Assert(NativeGildRequestResponseWiredTransaction.TypeJoinCorps == 0
        && NativeGildRequestResponseWiredTransaction.TypeJoinGild == 1
        && NativeGildRequestResponseWiredTransaction.TypeUnion == 2, "type consts");
    Assert(NativeGildRequestResponseWiredTransaction.EntryLevel(NativeGildRole.NoCorps) == 0
        && NativeGildRequestResponseWiredTransaction.EntryLevel(NativeGildRole.Member) == 0, "level 0");
    Assert(NativeGildRequestResponseWiredTransaction.EntryLevel(NativeGildRole.Corps) == 1
        && NativeGildRequestResponseWiredTransaction.EntryLevel(NativeGildRole.GildMember) == 1, "level 1");
    Assert(NativeGildRequestResponseWiredTransaction.EntryLevel(NativeGildRole.GildVice) == 2, "level 2");
    Assert(NativeGildRequestResponseWiredTransaction.EntryLevel(NativeGildRole.GildOwner) == 3, "level 3");
}

static void VerifyOuterCodes()
{
    var ok = new NativeGildRequestSubtypeContext
    {
        RequestPresent = true, GildFound = true, GildHasOwnerCorp = true,
    };
    // request not found -> 10 (any resolving role).
    Assert(NativeGildRequestResponseWiredTransaction.Evaluate(
        NativeGildRequestSubtypeOp.Accept, NativeGildRole.GildOwner, 1, false, ok) == 10,
        "not-found -> 10");
    Assert(NativeGildRequestResponseWiredTransaction.Evaluate(
        NativeGildRequestSubtypeOp.Refuse, NativeGildRole.GildOwner, 1, false, ok) == 10,
        "refuse not-found -> 10");
    // no_corps / member strategy stub -> 555 regardless of found/type.
    Assert(NativeGildRequestResponseWiredTransaction.Evaluate(
        NativeGildRequestSubtypeOp.Accept, NativeGildRole.NoCorps, 1, true, ok) == 555, "no_corps -> 555");
    Assert(NativeGildRequestResponseWiredTransaction.Evaluate(
        NativeGildRequestSubtypeOp.Accept, NativeGildRole.Member, 0, false, ok) == 555, "member -> 555");
    // president with an out-of-range type -> 23 (defensive corp-layer else).
    Assert(Acc(3, ok) == 23, "type 3 -> 23");
    // president convenience overload (role implied gild_owner) reaches the subtype.
    Assert(NativeGildRequestResponseWiredTransaction.Evaluate(
        NativeGildRequestSubtypeOp.Refuse, 1, true, ok) == 0, "convenience refuse joingild -> 0");
}

static void VerifyAcceptComposition()
{
    // --- type 1 JoinGild accept: 555 / 12 / 13 / 5 / 6 / 1000 / 0 ---
    Assert(Acc(1, new NativeGildRequestSubtypeContext { RequestPresent = false }) == 555, "gild-acc 555");
    Assert(Acc(1, new NativeGildRequestSubtypeContext { RequestPresent = true, GildFound = false }) == 12, "gild-acc 12");
    Assert(Acc(1, new NativeGildRequestSubtypeContext { RequestPresent = true, GildFound = true, GildMemberLimitReached = true }) == 13, "gild-acc 13");
    Assert(Acc(1, new NativeGildRequestSubtypeContext { RequestPresent = true, GildFound = true, ApplicantFound = false }) == 5, "gild-acc 5");
    Assert(Acc(1, new NativeGildRequestSubtypeContext { RequestPresent = true, GildFound = true, ApplicantFound = true, ApplicantAlreadyInGild = true }) == 6, "gild-acc 6");
    Assert(Acc(1, new NativeGildRequestSubtypeContext { RequestPresent = true, GildFound = true, ApplicantFound = true, AddToGildOk = false }) == 1000, "gild-acc 1000");
    Assert(Acc(1, new NativeGildRequestSubtypeContext { RequestPresent = true, GildFound = true, ApplicantFound = true, AddToGildOk = true }) == 0, "gild-acc 0");

    // --- type 2 Union accept: 555 / 12 / 12 / 555 / <save_relation> ---
    Assert(Acc(2, new NativeGildRequestSubtypeContext { RequestPresent = false }) == 555, "union-acc 555");
    Assert(Acc(2, new NativeGildRequestSubtypeContext { RequestPresent = true, GildFound = false }) == 12, "union-acc 12a");
    Assert(Acc(2, new NativeGildRequestSubtypeContext { RequestPresent = true, GildFound = true, OtherGildFound = false }) == 12, "union-acc 12b");
    Assert(Acc(2, new NativeGildRequestSubtypeContext { RequestPresent = true, GildFound = true, OtherGildFound = true, AcceptorIsGildOwner = false }) == 555, "union-acc not-owner 555");
    Assert(Acc(2, new NativeGildRequestSubtypeContext { RequestPresent = true, GildFound = true, OtherGildFound = true, AcceptorIsGildOwner = true, SaveRelationResult = 0 }) == 0, "union-acc save 0");
    Assert(Acc(2, new NativeGildRequestSubtypeContext { RequestPresent = true, GildFound = true, OtherGildFound = true, AcceptorIsGildOwner = true, SaveRelationResult = 15 }) == 15, "union-acc save 15 passthrough");

    // --- type 0 JoinCorps accept: 555 / 5 / 16 / 555 / 0 ---
    Assert(Acc(0, new NativeGildRequestSubtypeContext { RequestPresent = false }) == 555, "corp-acc 555");
    Assert(Acc(0, new NativeGildRequestSubtypeContext { RequestPresent = true, CorpFound = false }) == 5, "corp-acc 5");
    Assert(Acc(0, new NativeGildRequestSubtypeContext { RequestPresent = true, CorpFound = true, CorpFull = true }) == 16, "corp-acc 16");
    Assert(Acc(0, new NativeGildRequestSubtypeContext { RequestPresent = true, CorpFound = true, CorpFull = false, AcceptorIsCorpLeader = false }) == 555, "corp-acc not-leader 555");
    Assert(Acc(0, new NativeGildRequestSubtypeContext { RequestPresent = true, CorpFound = true, CorpFull = false, AcceptorIsCorpLeader = true }) == 0, "corp-acc 0");
}

static void VerifyRefuseComposition()
{
    // --- type 1 JoinGild refuse: 555 / 12 / 5 / 0 ---
    Assert(Ref(1, new NativeGildRequestSubtypeContext { RequestPresent = false }) == 555, "gild-ref 555");
    Assert(Ref(1, new NativeGildRequestSubtypeContext { RequestPresent = true, GildFound = false }) == 12, "gild-ref 12");
    Assert(Ref(1, new NativeGildRequestSubtypeContext { RequestPresent = true, GildFound = true, GildHasOwnerCorp = false }) == 5, "gild-ref 5");
    Assert(Ref(1, new NativeGildRequestSubtypeContext { RequestPresent = true, GildFound = true, GildHasOwnerCorp = true }) == 0, "gild-ref 0");

    // --- type 2 Union refuse: 555 / 12 / 5 / 0 ---
    Assert(Ref(2, new NativeGildRequestSubtypeContext { RequestPresent = false }) == 555, "union-ref 555");
    Assert(Ref(2, new NativeGildRequestSubtypeContext { RequestPresent = true, GildFound = false }) == 12, "union-ref 12");
    Assert(Ref(2, new NativeGildRequestSubtypeContext { RequestPresent = true, GildFound = true, GildHasOwnerCorp = false }) == 5, "union-ref 5");
    Assert(Ref(2, new NativeGildRequestSubtypeContext { RequestPresent = true, GildFound = true, GildHasOwnerCorp = true }) == 0, "union-ref 0");

    // --- type 0 JoinCorps refuse: 555 / 0 ---
    Assert(Ref(0, new NativeGildRequestSubtypeContext { RequestPresent = false }) == 555, "corp-ref 555a");
    Assert(Ref(0, new NativeGildRequestSubtypeContext { RequestPresent = true, CorpFound = true, RefuserIsCorpLeader = false }) == 555, "corp-ref not-leader 555");
    Assert(Ref(0, new NativeGildRequestSubtypeContext { RequestPresent = true, CorpFound = true, RefuserIsCorpLeader = true }) == 0, "corp-ref 0");
}

static void VerifyRoleCascade()
{
    var ok = new NativeGildRequestSubtypeContext
    {
        // a JoinCorps-success context AND a JoinGild-success context in one (fields don't overlap).
        RequestPresent = true, GildFound = true, ApplicantFound = true, AddToGildOk = true,
        CorpFound = true, AcceptorIsCorpLeader = true,
    };
    int A(NativeGildRole r, int t) => NativeGildRequestResponseWiredTransaction.Evaluate(
        NativeGildRequestSubtypeOp.Accept, r, t, true, ok);

    // corps entry (level 1) reaches only JoinCorps; JoinGild / Union -> 23 (corps-vs-gild mismatch).
    Assert(A(NativeGildRole.Corps, 0) == 0, "corps + joincorps -> 0");
    Assert(A(NativeGildRole.Corps, 1) == 23, "corps + joingild -> 23");
    Assert(A(NativeGildRole.Corps, 2) == 23, "corps + union -> 23");
    Assert(A(NativeGildRole.GildMember, 1) == 23, "gild_member(corps_owner) + joingild -> 23");

    // gild_vice entry (level 2) reaches JoinGild + JoinCorps; Union -> 23.
    Assert(A(NativeGildRole.GildVice, 1) == 0, "vice + joingild -> 0");
    Assert(A(NativeGildRole.GildVice, 0) == 0, "vice + joincorps -> 0");
    Assert(A(NativeGildRole.GildVice, 2) == 23, "vice + union -> 23");

    // gild_owner entry (level 3) reaches every type (union needs the owner + save-relation ctx).
    var unionOk = new NativeGildRequestSubtypeContext
    {
        RequestPresent = true, GildFound = true, OtherGildFound = true,
        AcceptorIsGildOwner = true, SaveRelationResult = 0,
    };
    Assert(NativeGildRequestResponseWiredTransaction.Evaluate(
        NativeGildRequestSubtypeOp.Accept, NativeGildRole.GildOwner, 2, true, unionOk) == 0,
        "owner + union -> 0");
    Assert(A(NativeGildRole.GildOwner, 1) == 0, "owner + joingild -> 0");
    Assert(A(NativeGildRole.GildOwner, 0) == 0, "owner + joincorps -> 0");
}
