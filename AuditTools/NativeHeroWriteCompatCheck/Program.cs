using GameSvr;

using Outcome = GameSvr.NativeHeroCreateClientOutcome;
using DeleteAction = GameSvr.NativeHeroDeleteEntryAction;

// Contract check for the dormant native HERO lifecycle WRITE model (create + delete), locked against
// the disassembly: M2 sub_6C9C00 (local validate), DBServer sub_59AD4C/sub_58B830 (create rule),
// M2 sub_6535C0 (0x53 client map), M2 sub_6BF5AC (delete entry), DBServer sub_58D5B0 (delete rule).
// The class is dormant (pure functions, no writes); this harness only exercises its ladders.

try
{
    VerifyConstants();
    VerifySharedPredicates();
    VerifyCreateLocal();
    VerifyCreateDbRule();
    VerifyCreateResponse();
    VerifyDeleteEntry();
    VerifyDeleteDbRule();

    Console.WriteLine(
        "PASS NativeHeroWriteCompatCheck "
        + "create-local(sub_6C9C00)=-4/-1/-2/-3/0 "
        + "create-db(sub_58B830)=-1/-5/-2/-3/-4/-6/code "
        + "create-0x53(sub_6535C0)=offline/success+bts/typeerr/-1/-2/-3/-4/generic "
        + "delete-entry(sub_6BF5AC)=gate(HaveValidHero&&!spawned)->send+clear0xFC "
        + "delete-db(sub_58D5B0)=0/1/2/3 delete-0x59=ignored dormant=true");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"NativeHeroWriteCompatCheck FAIL: {ex.Message}");
    return 1;
}

static void Assert(bool cond, string msg)
{
    if (!cond) throw new Exception(msg);
}

static void EqI(int expected, int actual, string msg)
{
    if (expected != actual) throw new Exception($"{msg}: expected {expected} got {actual}");
}

void VerifyConstants()
{
    EqI(int.MinValue, NativeHeroWriteTransaction.NoResponse, "NoResponse sentinel");
    EqI(0x162, NativeHeroWriteTransaction.DbCreateRequestOpcode, "create request opcode");
    EqI(0x163, NativeHeroWriteTransaction.DbDeleteRequestOpcode, "delete request opcode");
    EqI(0x53, NativeHeroWriteTransaction.DbCreateResponseOpcode, "create response opcode");
    EqI(0x59, NativeHeroWriteTransaction.DbDeleteResponseOpcode, "delete response opcode");
    EqI(0x2732, NativeHeroWriteTransaction.InternalBuildHeroMessage, "internal SM_BUILDHERO msg (10034)");
    EqI(0xB7D, NativeHeroWriteTransaction.HeroStateByteOffset, "hero state byte offset");
    Assert(NativeHeroWriteTransaction.DeleteResponseIgnored, "0x59 delete response must be ignored");
}

void VerifySharedPredicates()
{
    // HaveValidHero sub_6D6894: (state & 0x03) != 0 — owned but not spawned still counts.
    Assert(!NativeHeroWriteTransaction.HaveValidHero(0x00), "state 0 -> no hero");
    Assert(NativeHeroWriteTransaction.HaveValidHero(0x01), "state bit0 -> hero");
    Assert(NativeHeroWriteTransaction.HaveValidHero(0x02), "state bit1 -> hero");
    Assert(NativeHeroWriteTransaction.HaveValidHero(0x03), "state bit0|bit1 -> hero");
    Assert(!NativeHeroWriteTransaction.HaveValidHero(0x04), "state bit2 only -> no hero");
    Assert(!NativeHeroWriteTransaction.HaveValidHero(0x08), "state bit3 only -> no hero");

    // Job = (code-1)%3, Sex = (code-1)/3 across the whole valid range.
    (int code, int job, int sex)[] cs =
    {
        (1, 0, 0), (2, 1, 0), (3, 2, 0), (4, 0, 1), (5, 1, 1), (6, 2, 1),
    };
    foreach (var (code, job, sex) in cs)
    {
        EqI(job, NativeHeroWriteTransaction.CreateJob(code), $"CreateJob code {code}");
        EqI(sex, NativeHeroWriteTransaction.CreateSex(code), $"CreateSex code {code}");
    }
}

void VerifyCreateLocal()
{
    // Degenerate silent guard: only heroType in {-2,-3} skips the internal SM_BUILDHERO emit.
    Assert(!NativeHeroWriteTransaction.CreateLocalSendsBuildHero(-2), "-2 silent (no build-hero)");
    Assert(!NativeHeroWriteTransaction.CreateLocalSendsBuildHero(-3), "-3 silent (no build-hero)");
    foreach (var t in new[] { 0, 1, 2, 3, -1, -4 })
        Assert(NativeHeroWriteTransaction.CreateLocalSendsBuildHero(t), $"heroType {t} emits build-hero");

    // heroType neither 1 nor 2 -> default -4.
    EqI(-4, Local(0, 3, 0, 8, false), "type 0 -> -4");
    EqI(-4, Local(3, 3, 0, 8, false), "type 3 -> -4");

    // type 1 ladder: state -> -1, code -> -2, name -> -3, clean -> 0.
    EqI(-1, Local(1, 3, 0x01, 8, false), "type1 state bit0 -> -1");
    EqI(-1, Local(1, 3, 0x04, 8, false), "type1 state bit2 -> -1");
    EqI(-1, Local(1, 3, 0x05, 8, false), "type1 state bit0|bit2 -> -1");
    EqI(0, Local(1, 3, 0x02, 8, false), "type1 state bit1 does NOT block type1");
    EqI(-2, Local(1, 0, 0x00, 8, false), "type1 code 0 -> -2");
    EqI(-2, Local(1, 7, 0x00, 8, false), "type1 code 7 -> -2");
    EqI(-3, Local(1, 3, 0x00, 3, false), "type1 name len 3 -> -3");
    EqI(-3, Local(1, 3, 0x00, 15, false), "type1 name len 15 -> -3");
    EqI(-3, Local(1, 3, 0x00, 8, true), "type1 forbidden first char -> -3");
    EqI(0, Local(1, 1, 0x00, 4, false), "type1 clean min len 4 -> 0");
    EqI(0, Local(1, 6, 0x00, 14, false), "type1 clean max len 14 -> 0");

    // type 2 ladder: HaveValidHero||bit3 -> -1, code -> -2, else 0. Name is NOT validated.
    EqI(-1, Local(2, 3, 0x01, 0, false), "type2 existing type1 (bit0) -> -1");
    EqI(-1, Local(2, 3, 0x02, 0, false), "type2 existing type2 (bit1) -> -1");
    EqI(-1, Local(2, 3, 0x08, 0, false), "type2 state bit3 -> -1");
    EqI(-2, Local(2, 0, 0x00, 0, false), "type2 code 0 -> -2");
    EqI(-2, Local(2, 7, 0x00, 0, false), "type2 code 7 -> -2");
    EqI(0, Local(2, 6, 0x00, 0, true), "type2 clean ignores name -> 0");
    Assert(!NativeHeroWriteTransaction.HaveValidHero(0x04),
        "type2 state bit2 alone is not HaveValidHero");
    EqI(0, Local(2, 6, 0x04, 0, false), "type2 state bit2 alone does not block -> 0");
}

void VerifyCreateDbRule()
{
    // Precedence: name filter (-1) beats everything, including a bad range.
    EqI(-1, Db(true, 0, 3, false, false, 0, false, false), "db name filter -> -1 (over range)");

    // range (-5): code out of 1..6 or heroType out of 1..2.
    EqI(-5, Db(false, 0, 1, false, false, 0, false, true), "db code 0 -> -5");
    EqI(-5, Db(false, 7, 1, false, false, 0, false, true), "db code 7 -> -5");
    EqI(-5, Db(false, 1, 0, false, false, 0, false, true), "db heroType 0 -> -5");
    EqI(-5, Db(false, 1, 3, false, false, 0, false, true), "db heroType 3 -> -5");

    // -2 global dup, -3 index dup (in that order).
    EqI(-2, Db(false, 1, 1, true, true, 5, true, true), "db global dup -> -2 (before index dup)");
    EqI(-3, Db(false, 1, 1, false, true, 5, true, true), "db index dup -> -3");

    // -4 capacity/same-type conflict.
    EqI(-4, Db(false, 1, 1, false, false, 2, false, true), "db 2 active non-consigned -> -4");
    EqI(-4, Db(false, 1, 1, false, false, 0, true, true), "db same-type active exists -> -4");
    EqI(1, Db(false, 1, 1, false, false, 1, false, true), "db 1 active non-consigned still allowed");

    // -6 persistence fail vs. success returns the code (1..6).
    EqI(-6, Db(false, 1, 1, false, false, 0, false, false), "db persist fail -> -6");
    EqI(1, Db(false, 1, 1, false, false, 0, false, true), "db success type1 code1 -> 1");
    EqI(6, Db(false, 6, 2, false, false, 0, false, true), "db success code6 -> 6");
    EqI(4, Db(false, 4, 2, false, false, 0, false, true), "db success code4 -> 4");
}

void VerifyCreateResponse()
{
    // Player offline -> no-op regardless of result.
    EqI((int)Outcome.PlayerOffline, Resp(1, 1, false), "resp offline");
    EqI((int)Outcome.PlayerOffline, Resp(-1, 1, false), "resp offline (fail result)");

    // result > 0 success + bit, or type error when heroType not 1/2.
    EqI((int)Outcome.Success, Resp(1, 1, true), "resp success type1");
    EqI((int)Outcome.Success, Resp(6, 2, true), "resp success type2");
    EqI((int)Outcome.TypeError, Resp(1, 0, true), "resp result>0 type0 -> type error");
    EqI((int)Outcome.TypeError, Resp(1, 3, true), "resp result>0 type3 -> type error");

    // failure code map.
    EqI((int)Outcome.IllegalName, Resp(-1, 1, true), "resp -1 illegal name");
    EqI((int)Outcome.DuplicateName, Resp(-2, 1, true), "resp -2 duplicate name");
    EqI((int)Outcome.NameInUse, Resp(-3, 1, true), "resp -3 name in use");
    EqI((int)Outcome.AlreadyHaveHero, Resp(-4, 1, true), "resp -4 already have hero");
    EqI((int)Outcome.GenericFail, Resp(0, 1, true), "resp 0 generic fail");
    EqI((int)Outcome.GenericFail, Resp(-5, 1, true), "resp -5 generic fail");
    EqI((int)Outcome.GenericFail, Resp(-6, 1, true), "resp -6 generic fail");

    // Only Success sets the state bit.
    foreach (Outcome o in Enum.GetValues<Outcome>())
        Assert(NativeHeroWriteTransaction.CreateOutcomeSetsStateBit(o) == (o == Outcome.Success),
            $"only Success sets bit ({o})");
}

void VerifyDeleteEntry()
{
    // gate = HaveValidHero(state) && !spawned.
    EqI((int)DeleteAction.Ignored, Entry(0x00, false), "entry no hero -> ignored");
    EqI((int)DeleteAction.Ignored, Entry(0x01, true), "entry spawned -> ignored");
    EqI((int)DeleteAction.SendRequestAndClearBits, Entry(0x01, false), "entry bit0 unspawned -> send");
    EqI((int)DeleteAction.SendRequestAndClearBits, Entry(0x02, false), "entry bit1 unspawned -> send");
    EqI((int)DeleteAction.SendRequestAndClearBits, Entry(0x03, false), "entry bit0|bit1 unspawned -> send");
    EqI((int)DeleteAction.Ignored, Entry(0x04, false), "entry bit2-only unspawned -> ignored");

    // clear = state &= 0xFC (bits 0,1 only; 2,3 preserved).
    EqI(0x0C, NativeHeroWriteTransaction.ApplyDeleteBitClear(0x0F), "clear 0x0F -> 0x0C");
    EqI(0x00, NativeHeroWriteTransaction.ApplyDeleteBitClear(0x03), "clear 0x03 -> 0x00");
    EqI(0xFC, NativeHeroWriteTransaction.ApplyDeleteBitClear(0xFF), "clear 0xFF -> 0xFC");
}

void VerifyDeleteDbRule()
{
    // sub_58D5B0 ladder in order: not found(0) / flag(2) / no candidate(0) / already deleted(3) / mark(1).
    EqI(0, Del(false, false, false, false), "del container not found -> 0");
    EqI(2, Del(true, true, true, false), "del container flag set -> 2 (before selection)");
    EqI(0, Del(true, false, false, false), "del no candidate -> 0");
    EqI(3, Del(true, false, true, true), "del already deleted -> 3");
    EqI(1, Del(true, false, true, false), "del mark + queue -> 1");
}

// --- thin adapters ---------------------------------------------------------------------------------

int Local(int type, int code, byte state, int nameLen, bool forbidden) =>
    NativeHeroWriteTransaction.EvaluateCreateLocal(new NativeHeroCreateLocalContext
    {
        HeroType = type, Code = code, StateByte = state,
        NameGbkLength = nameLen, FirstCharForbidden = forbidden,
    });

int Db(bool nameFilter, int code, int type, bool globalDup, bool indexDup,
    int activeCount, bool sameType, bool persisted) =>
    NativeHeroWriteTransaction.EvaluateCreateDbRule(new NativeHeroCreateDbContext
    {
        HeroType = type, Code = code, NameRejectedByFilter = nameFilter,
        GlobalNameDuplicate = globalDup, HeroIndexDuplicate = indexDup,
        ActiveNonConsignedCount = activeCount, SameHeroTypeActiveExists = sameType,
        PersistSucceeded = persisted,
    });

int Resp(int result, int type, bool online) =>
    (int)NativeHeroWriteTransaction.EvaluateCreateResponse(new NativeHeroCreateResponseContext
    {
        Result = result, HeroType = type, PlayerOnline = online,
    });

int Entry(byte state, bool spawned) =>
    (int)NativeHeroWriteTransaction.EvaluateDeleteEntry(new NativeHeroDeleteEntryContext
    {
        StateByte = state, HeroSpawned = spawned,
    });

int Del(bool found, bool flag, bool selected, bool alreadyDeleted) =>
    NativeHeroWriteTransaction.EvaluateDeleteDbRule(new NativeHeroDeleteDbContext
    {
        MasterContainerFound = found, ContainerFlagSet = flag,
        SelectedRecordFound = selected, SelectedRecordAlreadyDeleted = alreadyDeleted,
    });
