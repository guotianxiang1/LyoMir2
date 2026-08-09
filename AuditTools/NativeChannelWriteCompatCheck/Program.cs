using GameSvr;

using Op = GameSvr.NativeChannelWriteOp;
using Role = GameSvr.NativeChannelRole;

// Contract check for the dormant native channel write model 4447-4452, locked against sub_6F74EC
// role dispatch (not-in-channel / member / owner) and the core ladders sub_6A77C8 (create),
// sub_6A7C90 (enter), sub_6A7D4C (exit), sub_6A7DD8 (mode), sub_6A7E60 (kick), sub_6A7F18 (mute).

try
{
    VerifyConstants();
    VerifyCreate();
    VerifyEnter();
    VerifyExit();
    VerifyChangeMode();
    VerifyChangeMute();
    VerifyKickOut();

    Console.WriteLine(
        "PASS NativeChannelWriteCompatCheck 4447=create(-99/-5/-6/-3/-4/-1/-2/0) " +
        "4448=enter(-99/-9/-7/-30/-99/-8/-10/0) 4449=exit(-13/-99/-11/-12/0) " +
        "4450=mode(-14/-99/-15/-16/0) 4451=mute(-27/-99/-24/-23/-22/-25/-26/0) " +
        "4452=kick(-27/-99/-18/-20/-17/-19/-21/0) dormant=true");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"NativeChannelWriteCompatCheck FAIL: {ex.Message}");
    return 1;
}

static void Assert(bool cond, string msg)
{
    if (!cond)
        throw new Exception(msg);
}

static int Eval(Op op, NativeChannelWriteContext c) =>
    NativeChannelWriteTransaction.Evaluate(op, c);

static void VerifyConstants()
{
    Assert(NativeChannelWriteTransaction.VtblCreate == 0x00, "slot create");
    Assert(NativeChannelWriteTransaction.VtblEnter == 0x04, "slot enter");
    Assert(NativeChannelWriteTransaction.VtblExit == 0x08, "slot exit");
    Assert(NativeChannelWriteTransaction.VtblChangeMode == 0x0C, "slot mode");
    Assert(NativeChannelWriteTransaction.VtblKickOut == 0x10, "slot kick");
    Assert(NativeChannelWriteTransaction.VtblChangeMute == 0x14, "slot mute");
    Assert(NativeChannelWriteTransaction.VtblSendDefMessage == 0x250, "reply slot");
    Assert((int)Op.Create == 4447 && (int)Op.Enter == 4448 && (int)Op.Exit == 4449
        && (int)Op.ChangeMode == 4450 && (int)Op.ChangeMute == 4451 && (int)Op.KickOut == 4452,
        "op idents");
    Assert((int)Role.NotInChannel == 0 && (int)Role.Member == 1 && (int)Role.Owner == 2, "roles");
}

// ---- 4447 create ----
static NativeChannelWriteContext CreateCtx(
    Role role = Role.NotInChannel, bool payloadValid = true, bool pwdRequired = false,
    bool pwdProvided = false, bool typeValid = true, bool level35 = true,
    bool nameExists = false, bool atMax = false) =>
    new NativeChannelWriteContext
    {
        Role = role, CreatePayloadValid = payloadValid, CreatePasswordRequired = pwdRequired,
        CreatePasswordProvided = pwdProvided, CreateTypeValid = typeValid,
        ActorLevelAtLeast35 = level35, ChannelNameExists = nameExists, ChannelsAtMax = atMax,
    };

static void VerifyCreate()
{
    Assert(Eval(Op.Create, CreateCtx(payloadValid: false)) == -99, "create payload<25 -> -99");
    Assert(Eval(Op.Create, CreateCtx(pwdRequired: true, pwdProvided: false)) == -5, "create pwd missing -> -5");
    Assert(Eval(Op.Create, CreateCtx(typeValid: false)) == -6, "create bad type -> -6");
    Assert(Eval(Op.Create, CreateCtx(level35: false)) == -3, "create level<35 -> -3");
    Assert(Eval(Op.Create, CreateCtx(role: Role.Member)) == -4, "create in-channel(member) -> -4");
    Assert(Eval(Op.Create, CreateCtx(role: Role.Owner)) == -4, "create in-channel(owner) -> -4");
    Assert(Eval(Op.Create, CreateCtx(nameExists: true)) == -1, "create name exists -> -1");
    Assert(Eval(Op.Create, CreateCtx(atMax: true)) == -2, "create >=50 -> -2");
    Assert(Eval(Op.Create, CreateCtx()) == 0, "create success -> 0");
    // password required and provided passes the -5 gate.
    Assert(Eval(Op.Create, CreateCtx(pwdRequired: true, pwdProvided: true)) == 0, "create pwd ok -> 0");
}

// ---- 4448 enter ----
static NativeChannelWriteContext EnterCtx(
    byte type = 0, bool scoped = true, bool found = true, bool closed = false,
    bool online = true, bool pwdOk = true, bool full = false) =>
    new NativeChannelWriteContext
    {
        EnterType = type, ScopedMembershipResolved = scoped, EnterChannelFound = found,
        EnterChannelClosed = closed, EnterActorOnline = online, EnterPasswordOk = pwdOk,
        EnterChannelFull = full,
    };

static void VerifyEnter()
{
    Assert(Eval(Op.Enter, EnterCtx(type: 5)) == -99, "enter type>=5 -> -99");
    Assert(Eval(Op.Enter, EnterCtx(type: 3, scoped: false)) == -9, "enter scoped no-membership -> -9");
    Assert(Eval(Op.Enter, EnterCtx(found: false)) == -7, "enter channel not found -> -7");
    Assert(Eval(Op.Enter, EnterCtx(closed: true)) == -30, "enter closed -> -30");
    Assert(Eval(Op.Enter, EnterCtx(online: false)) == -99, "enter actor offline -> -99");
    Assert(Eval(Op.Enter, EnterCtx(pwdOk: false)) == -8, "enter bad password -> -8");
    Assert(Eval(Op.Enter, EnterCtx(full: true)) == -10, "enter full -> -10");
    Assert(Eval(Op.Enter, EnterCtx()) == 0, "enter type0 success -> 0");
    Assert(Eval(Op.Enter, EnterCtx(type: 2, scoped: true)) == 0, "enter scoped resolved -> core 0");
}

// ---- 4449 exit ----
static NativeChannelWriteContext ExitCtx(
    Role role = Role.Member, bool online = true, bool inChannel = true,
    bool channelExists = true, bool isMember = true) =>
    new NativeChannelWriteContext
    {
        Role = role, ExitActorOnline = online, ExitInAChannel = inChannel,
        ExitChannelExists = channelExists, ExitIsMember = isMember,
    };

static void VerifyExit()
{
    Assert(Eval(Op.Exit, ExitCtx(role: Role.NotInChannel)) == -13, "exit not-in-channel -> -13");
    Assert(Eval(Op.Exit, ExitCtx(online: false)) == -99, "exit actor gone -> -99");
    Assert(Eval(Op.Exit, ExitCtx(inChannel: false)) == -13, "exit core not-in-channel -> -13");
    Assert(Eval(Op.Exit, ExitCtx(channelExists: false)) == -11, "exit channel gone -> -11");
    Assert(Eval(Op.Exit, ExitCtx(isMember: false)) == -12, "exit not member -> -12");
    Assert(Eval(Op.Exit, ExitCtx()) == 0, "exit success -> 0");
    Assert(Eval(Op.Exit, ExitCtx(role: Role.Owner)) == 0, "exit owner success -> 0");
}

// ---- 4450 change-mode ----
static NativeChannelWriteContext ModeCtx(
    Role role = Role.Owner, bool online = true, bool match = true,
    bool channelExists = true, bool isOwner = true) =>
    new NativeChannelWriteContext
    {
        Role = role, ModeActorOnline = online, ModeChannelMatch = match,
        ModeChannelExists = channelExists, ModeIsOwner = isOwner,
    };

static void VerifyChangeMode()
{
    Assert(Eval(Op.ChangeMode, ModeCtx(role: Role.NotInChannel)) == -14, "mode not-in-channel -> -14");
    Assert(Eval(Op.ChangeMode, ModeCtx(role: Role.Member)) == -14, "mode member -> -14");
    Assert(Eval(Op.ChangeMode, ModeCtx(online: false)) == -99, "mode actor gone -> -99");
    Assert(Eval(Op.ChangeMode, ModeCtx(match: false)) == -15, "mode channel mismatch -> -15");
    Assert(Eval(Op.ChangeMode, ModeCtx(channelExists: false)) == -16, "mode channel gone -> -16");
    Assert(Eval(Op.ChangeMode, ModeCtx(isOwner: false)) == -14, "mode core not owner -> -14");
    Assert(Eval(Op.ChangeMode, ModeCtx()) == 0, "mode success -> 0");
}

// ---- 4451 mute / 4452 kick (shared shape) ----
static NativeChannelWriteContext OpCtx(
    bool targetResolved = true, bool online = true, bool match = true,
    bool channelExists = true, bool isOwner = true, bool targetMember = true,
    bool targetOwner = false) =>
    new NativeChannelWriteContext
    {
        TargetResolved = targetResolved, OpActorOnline = online, OpChannelMatch = match,
        OpChannelExists = channelExists, OpIsOwner = isOwner, TargetIsMember = targetMember,
        TargetIsOwner = targetOwner,
    };

static void VerifyChangeMute()
{
    Assert(Eval(Op.ChangeMute, OpCtx(targetResolved: false)) == -27, "mute no target -> -27");
    Assert(Eval(Op.ChangeMute, OpCtx(online: false)) == -99, "mute actor gone -> -99");
    Assert(Eval(Op.ChangeMute, OpCtx(match: false)) == -24, "mute channel mismatch -> -24");
    Assert(Eval(Op.ChangeMute, OpCtx(channelExists: false)) == -23, "mute channel gone -> -23");
    Assert(Eval(Op.ChangeMute, OpCtx(isOwner: false)) == -22, "mute not owner -> -22");
    Assert(Eval(Op.ChangeMute, OpCtx(targetMember: false)) == -25, "mute target not member -> -25");
    Assert(Eval(Op.ChangeMute, OpCtx(targetOwner: true)) == -26, "mute target is owner -> -26");
    Assert(Eval(Op.ChangeMute, OpCtx()) == 0, "mute success -> 0");
}

static void VerifyKickOut()
{
    Assert(Eval(Op.KickOut, OpCtx(targetResolved: false)) == -27, "kick no target -> -27");
    Assert(Eval(Op.KickOut, OpCtx(online: false)) == -99, "kick actor gone -> -99");
    Assert(Eval(Op.KickOut, OpCtx(match: false)) == -18, "kick channel mismatch -> -18");
    Assert(Eval(Op.KickOut, OpCtx(channelExists: false)) == -20, "kick channel gone -> -20");
    Assert(Eval(Op.KickOut, OpCtx(isOwner: false)) == -17, "kick not owner -> -17");
    Assert(Eval(Op.KickOut, OpCtx(targetMember: false)) == -19, "kick target not member -> -19");
    Assert(Eval(Op.KickOut, OpCtx(targetOwner: true)) == -21, "kick target is owner -> -21");
    Assert(Eval(Op.KickOut, OpCtx()) == 0, "kick success -> 0");
}
