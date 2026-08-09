using GameSvr;

using Op = GameSvr.NativeCorpsWriteUpperOp;

// Contract check for the dormant upper-range Corps write ops 4501/4535/4536/4537/4539/4540, locked
// against sub_6F071C, sub_6AF8B8 (+0x00), sub_6AF9AC (+0x04), sub_6F5E1C, sub_6F5884 (+0x24 sub_701F48),
// and sub_6F5AA4 (+0x30: sub_70273C corps / sub_703114 gild), all via the sub_6ADA3C role dispatch.

try
{
    VerifyConstants();
    VerifyMemberListRefresh();
    VerifyBatchDispatch();
    VerifyQueryLog();
    VerifyNotice();
    VerifyDismissVice();

    Console.WriteLine(
        "PASS NativeCorpsWriteUpperCompatCheck 4501=5/0 4535/4536=555|dispatch 4537=5/30/0 " +
        "4539=set(5/555/5/24/0)|get(5/0) 4540=corps(555/5/1000/0)|gild(18/555/5/5/18/1000/0) dormant=true");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"NativeCorpsWriteUpperCompatCheck FAIL: {ex.Message}");
    return 1;
}

static void Assert(bool cond, string msg)
{
    if (!cond)
        throw new Exception(msg);
}

static int Eval(Op op, NativeCorpsWriteUpperContext c) =>
    NativeCorpsWriteUpperTransaction.Evaluate(op, c);

static void VerifyConstants()
{
    Assert(NativeCorpsWriteUpperTransaction.VtblAccept == 0x00, "vtbl accept +0x00");
    Assert(NativeCorpsWriteUpperTransaction.VtblRefuse == 0x04, "vtbl refuse +0x04");
    Assert(NativeCorpsWriteUpperTransaction.VtblNoticeSet == 0x24, "vtbl notice +0x24");
    Assert(NativeCorpsWriteUpperTransaction.VtblDismissVice == 0x30, "vtbl dismiss +0x30");
    Assert(NativeCorpsWriteUpperTransaction.VtblSendDefMessage == 0x250, "send +0x250");
    Assert(NativeCorpsWriteUpperTransaction.VtblSendBuffer == 0x254, "send buffer +0x254");
    Assert((int)Op.MemberListRefresh == 4501, "ident 4501");
    Assert((int)Op.AcceptRequest == 4535, "ident 4535");
    Assert((int)Op.RefuseRequest == 4536, "ident 4536");
    Assert((int)Op.QueryLog == 4537, "ident 4537");
    Assert((int)Op.Notice == 4539, "ident 4539");
    Assert((int)Op.DismissVice == 4540, "ident 4540");
    Assert(NativeCorpsWriteUpperTransaction.NoPermission == 555, "555");
    Assert(NativeCorpsWriteUpperTransaction.NoCorpOrPlayer == 5, "5");
    Assert(NativeCorpsWriteUpperTransaction.NoLogs == 30, "30");
    Assert(NativeCorpsWriteUpperTransaction.NoticeTooLongCode == 24, "24");
    Assert(NativeCorpsWriteUpperTransaction.NotDismissable == 1000, "1000");
    Assert(NativeCorpsWriteUpperTransaction.ActorInvalid == 18, "18");
    Assert(NativeCorpsWriteUpperTransaction.Success == 0, "0");
}

static void VerifyMemberListRefresh()
{
    // 4501 sub_6F071C: no corp 5 / has corp 0.
    Assert(Eval(Op.MemberListRefresh, new NativeCorpsWriteUpperContext { HasCorp = false }) == 5,
        "4501 no corp -> 5");
    Assert(Eval(Op.MemberListRefresh, new NativeCorpsWriteUpperContext { HasCorp = true }) == 0,
        "4501 has corp -> 0");
}

static void VerifyBatchDispatch()
{
    // 4535/4536: no_corps/member -> 555; other roles forward the per-item subtype dispatch result.
    foreach (var op in new[] { Op.AcceptRequest, Op.RefuseRequest })
    {
        Assert(Eval(op, new NativeCorpsWriteUpperContext { Role = NativeGildRole.NoCorps }) == 555,
            $"{op} no_corps -> 555");
        Assert(Eval(op, new NativeCorpsWriteUpperContext { Role = NativeGildRole.Member }) == 555,
            $"{op} member -> 555");
        // corps/gild roles: dispatch result forwarded verbatim (10 not-found / 23 wrong-type / subtype).
        Assert(Eval(op, new NativeCorpsWriteUpperContext
        { Role = NativeGildRole.Corps, SubtypeDispatchResult = 10 }) == 10, $"{op} req-not-found -> 10");
        Assert(Eval(op, new NativeCorpsWriteUpperContext
        { Role = NativeGildRole.GildMember, SubtypeDispatchResult = 23 }) == 23, $"{op} wrong-type -> 23");
        Assert(Eval(op, new NativeCorpsWriteUpperContext
        { Role = NativeGildRole.GildOwner, SubtypeDispatchResult = 0 }) == 0, $"{op} subtype success -> 0");
        Assert(Eval(op, new NativeCorpsWriteUpperContext
        { Role = NativeGildRole.GildVice, SubtypeDispatchResult = 13 }) == 13, $"{op} subtype code -> passthrough");
    }
}

static void VerifyQueryLog()
{
    // 4537 sub_6F5E1C: no corp 5 / page empty 30 / page available 0.
    Assert(Eval(Op.QueryLog, new NativeCorpsWriteUpperContext { HasCorp = false }) == 5,
        "4537 no corp -> 5");
    Assert(Eval(Op.QueryLog, new NativeCorpsWriteUpperContext { HasCorp = true, LogPageAvailable = false }) == 30,
        "4537 empty page -> 30");
    Assert(Eval(Op.QueryLog, new NativeCorpsWriteUpperContext { HasCorp = true, LogPageAvailable = true }) == 0,
        "4537 page available -> 0");
}

static void VerifyNotice()
{
    // 4539 sub_6F5884. GET mode (no text): no corp 5 / has corp 0.
    Assert(Eval(Op.Notice, new NativeCorpsWriteUpperContext { NoticeSetMode = false, HasCorp = false }) == 5,
        "4539 get no corp -> 5");
    Assert(Eval(Op.Notice, new NativeCorpsWriteUpperContext { NoticeSetMode = false, HasCorp = true }) == 0,
        "4539 get has corp -> 0");

    // SET mode: no corp 5 (before role dispatch).
    Assert(Eval(Op.Notice, new NativeCorpsWriteUpperContext { NoticeSetMode = true, HasCorp = false }) == 5,
        "4539 set no corp -> 5");
    // SET + has corp + no_corps/member role -> 555 (sub_701A38 stub).
    Assert(Eval(Op.Notice, new NativeCorpsWriteUpperContext
    { NoticeSetMode = true, HasCorp = true, Role = NativeGildRole.Member }) == 555, "4539 set member -> 555");
    // SET + corps/gild role -> sub_701F48: actor not found 5.
    Assert(Eval(Op.Notice, new NativeCorpsWriteUpperContext
    { NoticeSetMode = true, HasCorp = true, Role = NativeGildRole.Corps, NoticeActorFound = false }) == 5,
        "4539 set actor-not-found -> 5");
    // too long 24.
    Assert(Eval(Op.Notice, new NativeCorpsWriteUpperContext
    { NoticeSetMode = true, HasCorp = true, Role = NativeGildRole.GildOwner,
      NoticeActorFound = true, NoticeTooLong = true }) == 24, "4539 set too-long -> 24");
    // success 0.
    Assert(Eval(Op.Notice, new NativeCorpsWriteUpperContext
    { NoticeSetMode = true, HasCorp = true, Role = NativeGildRole.GildOwner,
      NoticeActorFound = true, NoticeTooLong = false }) == 0, "4539 set success -> 0");
}

static void VerifyDismissVice()
{
    // 4540 sub_6F5AA4, slot +0x30.
    // no_corps / member -> sub_701C04 stub 555.
    Assert(Eval(Op.DismissVice, new NativeCorpsWriteUpperContext { Role = NativeGildRole.NoCorps }) == 555,
        "4540 no_corps -> 555");
    Assert(Eval(Op.DismissVice, new NativeCorpsWriteUpperContext { Role = NativeGildRole.Member }) == 555,
        "4540 member -> 555");

    // corps (corps_vice_owner) -> sub_70273C self vice-stepdown: target!=self 555 / no player 5 /
    // not-a-vice 1000 / vice 0.
    Assert(Eval(Op.DismissVice, new NativeCorpsWriteUpperContext
    { Role = NativeGildRole.Corps, DismissTargetIsSelf = false }) == 555, "4540 corps not-self -> 555");
    Assert(Eval(Op.DismissVice, new NativeCorpsWriteUpperContext
    { Role = NativeGildRole.Corps, DismissTargetIsSelf = true, DismissTargetFound = false }) == 5,
        "4540 corps no player -> 5");
    Assert(Eval(Op.DismissVice, new NativeCorpsWriteUpperContext
    { Role = NativeGildRole.Corps, DismissTargetIsSelf = true, DismissTargetFound = true,
      DismissTargetIsVice = false }) == 1000, "4540 corps not-a-vice -> 1000");
    Assert(Eval(Op.DismissVice, new NativeCorpsWriteUpperContext
    { Role = NativeGildRole.Corps, DismissTargetIsSelf = true, DismissTargetFound = true,
      DismissTargetIsVice = true }) == 0, "4540 corps vice -> 0");

    // gild_member / gild_vice / gild_owner -> sub_703114 president dismiss vice.
    foreach (var role in new[] { NativeGildRole.GildMember, NativeGildRole.GildVice, NativeGildRole.GildOwner })
    {
        Assert(Eval(Op.DismissVice, new NativeCorpsWriteUpperContext
        { Role = role, DismissActorValid = false }) == 18, $"4540 {role} actor-invalid -> 18");
        Assert(Eval(Op.DismissVice, new NativeCorpsWriteUpperContext
        { Role = role, DismissActorValid = true, DismissTargetIsSelf = true }) == 555,
            $"4540 {role} self -> 555");
        Assert(Eval(Op.DismissVice, new NativeCorpsWriteUpperContext
        { Role = role, DismissActorValid = true, DismissTargetIsSelf = false, DismissTargetFound = false }) == 5,
            $"4540 {role} no player -> 5");
        Assert(Eval(Op.DismissVice, new NativeCorpsWriteUpperContext
        { Role = role, DismissActorValid = true, DismissTargetIsSelf = false, DismissTargetFound = true,
          DismissTargetIsOwner = false }) == 5, $"4540 {role} not-owner -> 5");
        Assert(Eval(Op.DismissVice, new NativeCorpsWriteUpperContext
        { Role = role, DismissActorValid = true, DismissTargetIsSelf = false, DismissTargetFound = true,
          DismissTargetIsOwner = true, DismissAuthorized = false }) == 18, $"4540 {role} not-authorized -> 18");
        Assert(Eval(Op.DismissVice, new NativeCorpsWriteUpperContext
        { Role = role, DismissActorValid = true, DismissTargetIsSelf = false, DismissTargetFound = true,
          DismissTargetIsOwner = true, DismissAuthorized = true, DismissTargetIsVice = false }) == 1000,
            $"4540 {role} not-a-vice -> 1000");
        Assert(Eval(Op.DismissVice, new NativeCorpsWriteUpperContext
        { Role = role, DismissActorValid = true, DismissTargetIsSelf = false, DismissTargetFound = true,
          DismissTargetIsOwner = true, DismissAuthorized = true, DismissTargetIsVice = true }) == 0,
            $"4540 {role} vice -> 0");
    }
}
