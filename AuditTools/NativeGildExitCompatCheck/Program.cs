using GameSvr;

// Contract check for the 4583 Gild exit dormant model (handler sub_6F6BF8 gates + strategy sub_703418).

try
{
    VerifyConstants();
    VerifyHandlerGates();
    VerifyStrategyLadder();

    Console.WriteLine(
        "PASS NativeGildExitCompatCheck 4583 gates=38/12/28/29 strategy=5/12/18/1000/0 dormant=true");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"NativeGildExitCompatCheck FAIL: {ex.Message}");
    return 1;
}

static void Assert(bool cond, string msg)
{
    if (!cond)
        throw new Exception(msg);
}

static int Eval(NativeGildExitContext c) => NativeGildExitTransaction.Evaluate(c);

// A fully valid exit reaching success.
static NativeGildExitContext Ok() => new()
{
    CanLeave = true, HasGildMembership = true, InFightZone = false, CastleWarBlocked = false,
    HasPlayer = true, HasGild = true, ValidMember = true, RemoveOk = true,
};

static void VerifyConstants()
{
    Assert(NativeGildExitTransaction.Ident == 4583, "ident");
    Assert(NativeGildExitTransaction.VtblStrategy == 0x44, "vtbl strategy");
    Assert(NativeGildExitTransaction.VtblSendDefMessage == 0x250, "vtbl send");
}

static void VerifyHandlerGates()
{
    var c = Ok(); c = new NativeGildExitContext
    {
        CanLeave = false, HasGildMembership = true, HasPlayer = true, HasGild = true,
        ValidMember = true, RemoveOk = true,
    };
    Assert(Eval(c) == 38, "not allowed -> 38");

    Assert(Eval(new NativeGildExitContext { CanLeave = true, HasGildMembership = false }) == 12,
        "no membership -> 12");
    Assert(Eval(new NativeGildExitContext { CanLeave = true, HasGildMembership = true, InFightZone = true }) == 28,
        "fight zone -> 28");
    Assert(Eval(new NativeGildExitContext
    {
        CanLeave = true, HasGildMembership = true, InFightZone = false, CastleWarBlocked = true,
    }) == 29, "castle war -> 29");
}

static void VerifyStrategyLadder()
{
    // gates passed; strategy ladder.
    Assert(Eval(new NativeGildExitContext
    {
        CanLeave = true, HasGildMembership = true, HasPlayer = false,
    }) == 5, "strategy no player -> 5");
    Assert(Eval(new NativeGildExitContext
    {
        CanLeave = true, HasGildMembership = true, HasPlayer = true, HasGild = false,
    }) == 12, "strategy no gild -> 12");
    Assert(Eval(new NativeGildExitContext
    {
        CanLeave = true, HasGildMembership = true, HasPlayer = true, HasGild = true, ValidMember = false,
    }) == 18, "strategy not member -> 18");
    Assert(Eval(new NativeGildExitContext
    {
        CanLeave = true, HasGildMembership = true, HasPlayer = true, HasGild = true,
        ValidMember = true, RemoveOk = false,
    }) == 1000, "strategy remove failed -> 1000");
    Assert(Eval(Ok()) == 0, "success -> 0");
}
