using GameSvr;

// Contract check for the dormant Gild request-object SUBTYPE accept/refuse ladders, locked against
// the request-class VMTs: TJoinCorpsRequest (sub_707468/sub_7077C4), TJoinGildRequest
// (sub_707D9C / sub_708520), TUnionGildRequest (sub_708168 / sub_708004), and the type()
// discriminators sub_7077C0->0 / sub_708000->1 / sub_708398->2.

using Sub = GameSvr.NativeGildRequestSubtype;
using Op = GameSvr.NativeGildRequestSubtypeOp;

try
{
    VerifyConstants();
    VerifyJoinGildAccept();
    VerifyJoinGildRefuse();
    VerifyUnionAccept();
    VerifyUnionRefuse();
    VerifyJoinCorpsAccept();
    VerifyJoinCorpsRefuse();

    Console.WriteLine(
        "PASS NativeGildRequestSubtypeCompatCheck join-accept=555/12/13/5/6/1000/0 " +
        "join-refuse=555/12/5/0 union-accept=555/12/12/555/save union-refuse=555/12/5/0 " +
        "corp-accept=555/5/16/555/0 corp-refuse=555/0 type=0/1/2 dormant=true");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"NativeGildRequestSubtypeCompatCheck FAIL: {ex.Message}");
    return 1;
}

static void Assert(bool cond, string msg)
{
    if (!cond)
        throw new Exception(msg);
}

static int Eval(Sub s, Op op, NativeGildRequestSubtypeContext c) =>
    NativeGildRequestSubtypeTransaction.Evaluate(s, op, c);

static void VerifyConstants()
{
    Assert(NativeGildRequestSubtypeTransaction.VtblType == 0x00, "vtbl type +0x00");
    Assert(NativeGildRequestSubtypeTransaction.VtblAccept == 0x14, "vtbl accept +0x14");
    Assert(NativeGildRequestSubtypeTransaction.VtblRefuse == 0x18, "vtbl refuse +0x18");
    Assert((int)Op.Accept == 0x14 && (int)Op.Refuse == 0x18, "op slots");
    Assert((int)Sub.JoinCorps == 0 && (int)Sub.JoinGild == 1 && (int)Sub.Union == 2, "type discriminators 0/1/2");
    Assert(NativeGildRequestSubtypeTransaction.TypeJoinCorps == 0
        && NativeGildRequestSubtypeTransaction.TypeJoinGild == 1
        && NativeGildRequestSubtypeTransaction.TypeUnion == 2, "type constants");
    Assert(NativeGildRequestSubtypeTransaction.NoPermission == 555, "555");
    Assert(NativeGildRequestSubtypeTransaction.NoGild == 12, "12");
    Assert(NativeGildRequestSubtypeTransaction.MemberLimit == 13, "13");
    Assert(NativeGildRequestSubtypeTransaction.NoApplicantOrCorp == 5, "5");
    Assert(NativeGildRequestSubtypeTransaction.ApplicantInGild == 6, "6");
    Assert(NativeGildRequestSubtypeTransaction.WriteFailed == 1000, "1000");
    Assert(NativeGildRequestSubtypeTransaction.CorpFullCode == 16, "16");
    Assert(NativeGildRequestSubtypeTransaction.Success == 0, "0");
}

// A fully-successful join-gild accept context; individual tests flip one field.
static NativeGildRequestSubtypeContext JoinGildOk() => new NativeGildRequestSubtypeContext
{
    RequestPresent = true,
    GildFound = true,
    GildMemberLimitReached = false,
    ApplicantFound = true,
    ApplicantAlreadyInGild = false,
    AddToGildOk = true,
};

static void VerifyJoinGildAccept()
{
    // sub_707D9C: 555 / 12 / 13 / 5 / 6 / 1000 / 0, in branch order.
    Assert(Eval(Sub.JoinGild, Op.Accept, new NativeGildRequestSubtypeContext { RequestPresent = false }) == 555,
        "jg accept no request -> 555");

    var c = JoinGildOk();
    Assert(Eval(Sub.JoinGild, Op.Accept, new NativeGildRequestSubtypeContext
    { RequestPresent = true, GildFound = false }) == 12, "jg accept no gild -> 12");
    Assert(Eval(Sub.JoinGild, Op.Accept, new NativeGildRequestSubtypeContext
    { RequestPresent = true, GildFound = true, GildMemberLimitReached = true }) == 13, "jg accept limit -> 13");
    Assert(Eval(Sub.JoinGild, Op.Accept, new NativeGildRequestSubtypeContext
    { RequestPresent = true, GildFound = true, ApplicantFound = false }) == 5, "jg accept no applicant -> 5");
    Assert(Eval(Sub.JoinGild, Op.Accept, new NativeGildRequestSubtypeContext
    { RequestPresent = true, GildFound = true, ApplicantFound = true, ApplicantAlreadyInGild = true }) == 6,
        "jg accept applicant already in gild -> 6");
    Assert(Eval(Sub.JoinGild, Op.Accept, new NativeGildRequestSubtypeContext
    { RequestPresent = true, GildFound = true, ApplicantFound = true, AddToGildOk = false }) == 1000,
        "jg accept add fail -> 1000");
    Assert(Eval(Sub.JoinGild, Op.Accept, c) == 0, "jg accept success -> 0");
}

static void VerifyJoinGildRefuse()
{
    // sub_708520: 555 / 12 / 5 / 0.
    Assert(Eval(Sub.JoinGild, Op.Refuse, new NativeGildRequestSubtypeContext { RequestPresent = false }) == 555,
        "jg refuse no request -> 555");
    Assert(Eval(Sub.JoinGild, Op.Refuse, new NativeGildRequestSubtypeContext
    { RequestPresent = true, GildFound = false }) == 12, "jg refuse no gild -> 12");
    Assert(Eval(Sub.JoinGild, Op.Refuse, new NativeGildRequestSubtypeContext
    { RequestPresent = true, GildFound = true, GildHasOwnerCorp = false }) == 5, "jg refuse no owner-corp -> 5");
    Assert(Eval(Sub.JoinGild, Op.Refuse, new NativeGildRequestSubtypeContext
    { RequestPresent = true, GildFound = true, GildHasOwnerCorp = true }) == 0, "jg refuse success -> 0");
}

static void VerifyUnionAccept()
{
    // sub_708168: 555 / 12 / 12 / 555 / <save_relation>.
    Assert(Eval(Sub.Union, Op.Accept, new NativeGildRequestSubtypeContext { RequestPresent = false }) == 555,
        "union accept no request -> 555");
    Assert(Eval(Sub.Union, Op.Accept, new NativeGildRequestSubtypeContext
    { RequestPresent = true, GildFound = false }) == 12, "union accept gild A missing -> 12");
    Assert(Eval(Sub.Union, Op.Accept, new NativeGildRequestSubtypeContext
    { RequestPresent = true, GildFound = true, OtherGildFound = false }) == 12, "union accept gild B missing -> 12");
    Assert(Eval(Sub.Union, Op.Accept, new NativeGildRequestSubtypeContext
    { RequestPresent = true, GildFound = true, OtherGildFound = true, AcceptorIsGildOwner = false }) == 555,
        "union accept not owner -> 555");
    // save_relation result forwarded verbatim.
    Assert(Eval(Sub.Union, Op.Accept, new NativeGildRequestSubtypeContext
    { RequestPresent = true, GildFound = true, OtherGildFound = true, AcceptorIsGildOwner = true,
      SaveRelationResult = 0 }) == 0, "union accept save ok -> 0");
    Assert(Eval(Sub.Union, Op.Accept, new NativeGildRequestSubtypeContext
    { RequestPresent = true, GildFound = true, OtherGildFound = true, AcceptorIsGildOwner = true,
      SaveRelationResult = 15 }) == 15, "union accept save-relation code -> passthrough");
}

static void VerifyUnionRefuse()
{
    // sub_708004: 555 / 12 / 5 / 0.
    Assert(Eval(Sub.Union, Op.Refuse, new NativeGildRequestSubtypeContext { RequestPresent = false }) == 555,
        "union refuse no request -> 555");
    Assert(Eval(Sub.Union, Op.Refuse, new NativeGildRequestSubtypeContext
    { RequestPresent = true, GildFound = false }) == 12, "union refuse no gild -> 12");
    Assert(Eval(Sub.Union, Op.Refuse, new NativeGildRequestSubtypeContext
    { RequestPresent = true, GildFound = true, GildHasOwnerCorp = false }) == 5, "union refuse no owner-corp -> 5");
    Assert(Eval(Sub.Union, Op.Refuse, new NativeGildRequestSubtypeContext
    { RequestPresent = true, GildFound = true, GildHasOwnerCorp = true }) == 0, "union refuse success -> 0");
}

static void VerifyJoinCorpsAccept()
{
    // sub_707468: 555 / 5 / 16 / 555 / 0.
    Assert(Eval(Sub.JoinCorps, Op.Accept, new NativeGildRequestSubtypeContext { RequestPresent = false }) == 555,
        "corp accept no request -> 555");
    Assert(Eval(Sub.JoinCorps, Op.Accept, new NativeGildRequestSubtypeContext
    { RequestPresent = true, CorpFound = false }) == 5, "corp accept no corp -> 5");
    Assert(Eval(Sub.JoinCorps, Op.Accept, new NativeGildRequestSubtypeContext
    { RequestPresent = true, CorpFound = true, CorpFull = true }) == 16, "corp accept full -> 16");
    Assert(Eval(Sub.JoinCorps, Op.Accept, new NativeGildRequestSubtypeContext
    { RequestPresent = true, CorpFound = true, CorpFull = false, AcceptorIsCorpLeader = false }) == 555,
        "corp accept not leader -> 555");
    Assert(Eval(Sub.JoinCorps, Op.Accept, new NativeGildRequestSubtypeContext
    { RequestPresent = true, CorpFound = true, CorpFull = false, AcceptorIsCorpLeader = true }) == 0,
        "corp accept success -> 0");
}

static void VerifyJoinCorpsRefuse()
{
    // sub_7077C4: 555 / 0.
    Assert(Eval(Sub.JoinCorps, Op.Refuse, new NativeGildRequestSubtypeContext { RequestPresent = false }) == 555,
        "corp refuse no request -> 555");
    Assert(Eval(Sub.JoinCorps, Op.Refuse, new NativeGildRequestSubtypeContext
    { RequestPresent = true, CorpFound = false }) == 555, "corp refuse no corp -> 555");
    Assert(Eval(Sub.JoinCorps, Op.Refuse, new NativeGildRequestSubtypeContext
    { RequestPresent = true, CorpFound = true, RefuserIsCorpLeader = false }) == 555, "corp refuse not leader -> 555");
    Assert(Eval(Sub.JoinCorps, Op.Refuse, new NativeGildRequestSubtypeContext
    { RequestPresent = true, CorpFound = true, RefuserIsCorpLeader = true }) == 0, "corp refuse leader -> 0");
}
