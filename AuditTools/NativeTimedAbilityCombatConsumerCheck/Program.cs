using System.Reflection;
using GameSvr;
using GameSvr.Services;
using static GameSvr.Services.NativeTimedAbilityCombatConsumer;

// Audit for the dormant NativeTimedAbilityCombatConsumer model. It proves the
// modeled type 44/46/74 combat consumers reproduce the exact branches and
// constants reversed from M2Server_unpacked_fixed.exe, and that types 46/74
// remain fail-closed. Pure computation; no runtime, no production wiring.

CheckDormancyAndFailClosed();
CheckType44UnionReceiver();
CheckType46JobEndpointSelector();
CheckType46Job3Resolvers();
CheckType74MagicHitContest();
CheckOwnerEnumerations();

Console.WriteLine(
    "PASS timed-combat-consumer " +
    "type44=sub_741764(mana@2B4[0.10/0.40/0.20/0.20]+union@791980+flat@154+pct@167) " +
    "type46=sub_76CD8C/sub_76CD5C+job3{1024=base,260=*1.8,264=*2.4,268=*3.0}@0.2/lvl " +
    "type74=sub_7744B4(clamp(100*(src+10)/(tgt+10),30,95)>Random(100);race11/null=miss) " +
    "failclosed=46+74-not-admitted owners=type74:15/19+type46:6/8");
return;

static void CheckDormancyAndFailClosed()
{
    Assert(DormantNotWiredIntoLiveCombat, "model not marked dormant");

    // Types 46 and 74 must not be admitted by the timed-ability gate; type 44
    // is admitted (its carrier/receiver are modeled) but the live combat
    // pipeline still does not call this reference model.
    var supported = typeof(TBaseObject).GetMethod(
        "IsSupportedTimedAbilityType",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("IsSupportedTimedAbilityType");
    bool IsSupported(int type) =>
        (bool)(supported.Invoke(null, new object[] { type }) ?? false);

    Assert(IsSupported(44), "type44 should stay admitted");
    Assert(!IsSupported(46), "type46 must remain fail-closed");
    Assert(!IsSupported(74), "type74 must remain fail-closed");

    // Native offsets pinned by the disassembly.
    Equal(0x72, SelfJobOffset, "job offset");
    Equal(0x178, RaceOffset, "race offset");
    Equal(0x270, AntiMagicOffset, "anti-magic offset");
    Equal(0x272, Type74MagicHitOffset, "type74 carrier offset");
    Equal(0x400, UnionSelectorOffset, "union selector offset");
    Equal(0x2B4, UnionManaOffset, "union mana offset");
    Equal(0x4C0, UnionFixedRecordOffset, "union fixed record offset");
    Equal(0x154, UnionFlatReductionOffset, "union flat offset");
    Equal(0x167, UnionPercentReductionOffset, "union percent offset");
    Equal(0x2A4, JobEndpointCcLowOffset, "CC low offset");
    Equal(0x2A8, JobEndpointCcHighOffset, "CC high offset");
    Equal(0x3E0, Job3ChargeOffset, "job3 charge offset");
}

static void CheckType44UnionReceiver()
{
    NativeFastnessTable table = LoadUnionTable(
        "1 0.25 300" + Environment.NewLine +
        "3 0.50 100" + Environment.NewLine +
        "-2 0.10 200");
    Equal(3, table.MaximumPositiveKey, "union table max key");
    Equal(750, table.ApplyReduction(1_000, 1), "union selector 1 reduction");
    Equal(900, table.ApplyReduction(1_000, 99),
        "union capped selector reduction");

    // A: gate on (effLevel 5, otherJob 0), self job 2 -> 0.20 coefficient.
    var a = ApplyType44UnionReceiver(new Type44UnionContext(
        selfJob: 2, otherJob: 0, effectiveMagicLevel: 5, incomingDamage: 1_000,
        unionSelector: 1, unionTable: table, flatReduction: 50,
        percentReduction: 20, selfMana: 1_000));
    Equal(560, a.FinalDamage, "A: table then flat then percent");
    Equal(800, a.NewSelfMana, "A: job2 mana drain 200");
    Assert(a.ManaSideEffectApplied, "A: mana side effect should fire");

    // B: capped selector (99 > max 3), self job 1 -> 0.40.
    var b = ApplyType44UnionReceiver(new Type44UnionContext(
        selfJob: 1, otherJob: 0, effectiveMagicLevel: 10, incomingDamage: 1_000,
        unionSelector: 99, unionTable: table, flatReduction: 50,
        percentReduction: 20, selfMana: 1_000));
    Equal(680, b.FinalDamage, "B: capped selector damage");
    Equal(600, b.NewSelfMana, "B: job1 mana drain 400");

    // C: gate off via otherJob != 0. Damage identical to A, no mana change.
    var c = ApplyType44UnionReceiver(new Type44UnionContext(
        selfJob: 2, otherJob: 1, effectiveMagicLevel: 5, incomingDamage: 1_000,
        unionSelector: 1, unionTable: table, flatReduction: 50,
        percentReduction: 20, selfMana: 1_000));
    Equal(560, c.FinalDamage, "C: damage unaffected by closed mana gate");
    Equal(1_000, c.NewSelfMana, "C: mana untouched when otherJob != 0");
    Assert(!c.ManaSideEffectApplied, "C: mana side effect must not fire");

    // D: gate off via effLevel < 4.
    var d = ApplyType44UnionReceiver(new Type44UnionContext(
        selfJob: 2, otherJob: 0, effectiveMagicLevel: 3, incomingDamage: 1_000,
        unionSelector: 1, unionTable: table, flatReduction: 50,
        percentReduction: 20, selfMana: 1_000));
    Equal(1_000, d.NewSelfMana, "D: mana untouched when effLevel < 4");
    Assert(!d.ManaSideEffectApplied, "D: mana side effect must not fire");

    // E: mana floors at zero (job1 drain 400 on mana 100).
    var e = ApplyType44UnionReceiver(new Type44UnionContext(
        selfJob: 1, otherJob: 0, effectiveMagicLevel: 4, incomingDamage: 1_000,
        unionSelector: 0, unionTable: null, flatReduction: 0,
        percentReduction: 0, selfMana: 100));
    Equal(0, e.NewSelfMana, "E: mana clamps to zero");

    // F: job 3 shares the job-2 coefficient (0.20).
    var f = ApplyType44UnionReceiver(new Type44UnionContext(
        selfJob: 3, otherJob: 0, effectiveMagicLevel: 4, incomingDamage: 1_000,
        unionSelector: 0, unionTable: null, flatReduction: 0,
        percentReduction: 0, selfMana: 1_000));
    Equal(800, f.NewSelfMana, "F: job3 uses 0.20 like job2");

    // G: banker's rounding (round-half-to-even), 0.10 coefficient.
    //    5 * 0.10 = 0.5 -> ToEven -> 0 (AwayFromZero would give 1).
    var g = ApplyType44UnionReceiver(new Type44UnionContext(
        selfJob: 0, otherJob: 0, effectiveMagicLevel: 4, incomingDamage: 5,
        unionSelector: 0, unionTable: null, flatReduction: 0,
        percentReduction: 0, selfMana: 100));
    Equal(100, g.NewSelfMana, "G: Round(0.5) must be 0 (banker's)");
    //    25 * 0.10 = 2.5 -> ToEven -> 2 (AwayFromZero would give 3).
    var g2 = ApplyType44UnionReceiver(new Type44UnionContext(
        selfJob: 0, otherJob: 0, effectiveMagicLevel: 4, incomingDamage: 25,
        unionSelector: 0, unionTable: null, flatReduction: 0,
        percentReduction: 0, selfMana: 100));
    Equal(98, g2.NewSelfMana, "G2: Round(2.5) must be 2 (banker's)");

    // H: percent step uses signed 32-bit imul/idiv wrap.
    //    unchecked(30_000_000 * 80) = -1_894_967_296; / 100 = -18_949_672.
    var h = ApplyType44UnionReceiver(new Type44UnionContext(
        selfJob: 9, otherJob: 9, effectiveMagicLevel: 0,
        incomingDamage: 30_000_000, unionSelector: 0, unionTable: null,
        flatReduction: 0, percentReduction: 20, selfMana: 0));
    Equal(-18_949_672, h.FinalDamage, "H: signed 32-bit percent wrap");

    // I: identity (percent 0, flat 0, no table, gate closed).
    var i = ApplyType44UnionReceiver(new Type44UnionContext(
        selfJob: 9, otherJob: 9, effectiveMagicLevel: 0, incomingDamage: 750,
        unionSelector: 0, unionTable: null, flatReduction: 0,
        percentReduction: 0, selfMana: 0));
    Equal(750, i.FinalDamage, "I: identity pass-through");
}

static void CheckType46JobEndpointSelector()
{
    // Endpoint pairs per job (low/high). Distinct values so the selection is
    // observable.
    Type46EndpointContext Ctx(byte job, byte mode) => new(
        job: job,
        dcLow: 10, dcHigh: 20, mcLow: 40, mcHigh: 55,
        scLow: 70, scHigh: 88, ccLow: 100, ccHigh: 150, modeByte: mode);

    // job 0, mode 0, low < high -> low + Random(high - low).
    var rng = new ScriptedRandom(3);
    Equal(13, SelectType46JobEndpoint(Ctx(0, 0), rng.Next),
        "job0 mode0 random roll");
    Equal(10, rng.LastArg, "job0 random bound = high-low");
    Equal(1, rng.Calls, "job0 mode0 consumed one RNG");

    // job 0, mode nonzero -> high, no RNG.
    Equal(20, SelectType46JobEndpoint(Ctx(0, 1), ThrowingRandom),
        "job0 mode1 high endpoint");

    // job 0, mode 0, inverted (low >= high) -> low, no RNG.
    var inverted = new Type46EndpointContext(0, 30, 20, 0, 0, 0, 0, 0, 0, 0);
    Equal(30, SelectType46JobEndpoint(inverted, ThrowingRandom),
        "job0 inverted returns low without RNG");

    // Each job picks the right pair (mode nonzero -> high).
    Equal(55, SelectType46JobEndpoint(Ctx(1, 1), ThrowingRandom), "job1 MC high");
    Equal(88, SelectType46JobEndpoint(Ctx(2, 1), ThrowingRandom), "job2 SC high");
    Equal(150, SelectType46JobEndpoint(Ctx(3, 1), ThrowingRandom), "job3 CC high");

    // job 3, mode 0 -> CC low + Random(CChigh - CClow).
    var rng3 = new ScriptedRandom(7);
    Equal(107, SelectType46JobEndpoint(Ctx(3, 0), rng3.Next), "job3 CC random");
    Equal(50, rng3.LastArg, "job3 random bound = CChigh-CClow");

    // job > 3 -> 0, no RNG.
    Equal(0, SelectType46JobEndpoint(Ctx(4, 0), ThrowingRandom),
        "job4 returns 0 without RNG");
}

static void CheckType46Job3Resolvers()
{
    Job3ResolverContext Ctx(byte job, int level, int power, bool state,
        bool eligible, bool blocked, bool targetNull) => new(
        selfJob: job, ccLow: 50, ccHigh: 90, effectiveMagicLevel: level,
        attackPower: power, hasRequiredState: state,
        mainTargetHitEligible: eligible, blocked: blocked,
        targetIsNull: targetNull);

    // 1024: job 3, eligible, not blocked, power 200 -> delivered, charge.
    var r1024 = ResolveJob3Attack1024(Ctx(3, 0, 200, false, true, false, false));
    Equal((int)Job3ResolverOutcome.Delivered, (int)r1024.Outcome, "1024 delivered");
    Equal(200, r1024.Power, "1024 power = base");
    Assert(r1024.ConsumesCharge, "1024 consumes charge when power > 0");

    var r1024z = ResolveJob3Attack1024(Ctx(3, 0, 0, false, true, false, false));
    Assert(!r1024z.ConsumesCharge, "1024 no charge when power = 0");

    Assert(ResolveJob3Attack1024(Ctx(2, 0, 200, false, true, false, false))
        .Outcome == Job3ResolverOutcome.NotJob3, "1024 requires job 3");
    Assert(ResolveJob3Attack1024(Ctx(3, 0, 200, false, true, true, false))
        .Outcome == Job3ResolverOutcome.NotHitEligible, "1024 blocked gate");
    Assert(ResolveJob3Attack1024(Ctx(3, 0, 200, false, false, false, false))
        .Outcome == Job3ResolverOutcome.NotHitEligible, "1024 hit gate");

    // 260: no state -> fallback; state + eligible -> Round((0.2L+1.8)*base).
    Assert(ResolveJob3Attack260(Ctx(3, 5, 100, false, true, false, false))
        .Outcome == Job3ResolverOutcome.FallbackTo1024, "260 falls back w/o state");
    Assert(ResolveJob3Attack260(Ctx(3, 5, 100, true, false, false, false))
        .Outcome == Job3ResolverOutcome.NotHitEligible, "260 hit gate");
    Equal(280, ResolveJob3Attack260(Ctx(3, 5, 100, true, true, false, false)).Power,
        "260 power = Round((0.2*5+1.8)*100)");
    Equal(180, ResolveJob3Attack260(Ctx(3, 0, 100, true, true, false, false)).Power,
        "260 power at level 0 = Round(1.8*100)");

    // 264: needs state; Round((0.2L+2.4)*base).
    Assert(ResolveJob3Attack264(Ctx(3, 5, 100, false, true, false, false))
        .Outcome == Job3ResolverOutcome.StateAbsent, "264 requires state 68");
    Equal(340, ResolveJob3Attack264(Ctx(3, 5, 100, true, true, false, false)).Power,
        "264 power = Round((0.2*5+2.4)*100)");

    // 268: needs non-null target and state; Round((0.2L+3.0)*base).
    Assert(ResolveJob3Attack268(Ctx(3, 5, 100, true, true, false, true))
        .Outcome == Job3ResolverOutcome.StateAbsent, "268 requires non-null target");
    Assert(ResolveJob3Attack268(Ctx(3, 5, 100, false, true, false, false))
        .Outcome == Job3ResolverOutcome.StateAbsent, "268 requires state 69");
    Equal(400, ResolveJob3Attack268(Ctx(3, 5, 100, true, true, false, false)).Power,
        "268 power = Round((0.2*5+3.0)*100)");
}

static void CheckType74MagicHitContest()
{
    // race 11 -> miss, RNG must not be consumed.
    Assert(!Type74MagicHitContest(
        new Type74ContestContext(11, false, 9999, 0), ThrowingRandom),
        "type74 race 11 always misses");
    // null source -> miss, no RNG.
    Assert(!Type74MagicHitContest(
        new Type74ContestContext(0, true, 9999, 0), ThrowingRandom),
        "type74 null source always misses");

    // chance = 100*(90+10)/(10+10) = 500 -> clamp 95.
    var rHit = new ScriptedRandom(94);
    Assert(Type74MagicHitContest(
        new Type74ContestContext(0, false, 90, 10), rHit.Next),
        "type74 chance 95 beats Random 94");
    Equal(100, rHit.LastArg, "type74 RNG bound = 100");
    Assert(!Type74MagicHitContest(
        new Type74ContestContext(0, false, 90, 10), new ScriptedRandom(95).Next),
        "type74 chance 95 does not beat Random 95");

    // chance = 100*(0+10)/(0+10) = 100 -> clamp 95.
    Assert(!Type74MagicHitContest(
        new Type74ContestContext(0, false, 0, 0), new ScriptedRandom(95).Next),
        "type74 upper clamp = 95");

    // chance = 100*(0+10)/(1000+10) = 0 -> clamp 30.
    Assert(Type74MagicHitContest(
        new Type74ContestContext(0, false, 0, 1_000), new ScriptedRandom(29).Next),
        "type74 lower clamp = 30 beats Random 29");
    Assert(!Type74MagicHitContest(
        new Type74ContestContext(0, false, 0, 1_000), new ScriptedRandom(30).Next),
        "type74 lower clamp 30 does not beat Random 30");

    // chance = 100*(20+10)/(90+10) = 30 (exact floor, integer division).
    Assert(Type74MagicHitContest(
        new Type74ContestContext(0, false, 20, 90), new ScriptedRandom(29).Next),
        "type74 integer-division chance 30");
}

static void CheckOwnerEnumerations()
{
    Equal(15, Type74ContestOwners.Length, "type74 unique owner count");
    int type74CallSites = Type74ContestOwners.Sum(o => o.CallSites.Length);
    Equal(19, type74CallSites, "type74 total call-site count");
    Equal(15, Type74ContestOwners.Select(o => o.Owner).Distinct().Count(),
        "type74 owners must be unique");

    Equal(6, Type46EndpointConsumers.Length, "type46 endpoint owner count");
    int type46CallSites = Type46EndpointConsumers.Sum(o => o.CallSites.Length);
    Equal(8, type46CallSites, "type46 total sub_76CD8C call-site count");
    Equal(6, Type46EndpointConsumers.Select(o => o.Owner).Distinct().Count(),
        "type46 endpoint owners must be unique");
}

static NativeFastnessTable LoadUnionTable(string contents)
{
    string path = Path.Combine(Path.GetTempPath(),
        $"m2-combat-union-{Guid.NewGuid():N}.txt");
    try
    {
        File.WriteAllText(path, contents);
        var table = new NativeFastnessTable();
        Assert(table.Load(path), "union table fixture load");
        return table;
    }
    finally
    {
        File.Delete(path);
    }
}

static int ThrowingRandom(int bound) =>
    throw new InvalidOperationException(
        "RNG consumed on a path that must not roll");

static void Equal(int expected, int actual, string message)
{
    if (expected != actual)
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed class ScriptedRandom
{
    private readonly int[] _values;
    private int _index;

    public ScriptedRandom(params int[] values) => _values = values;

    public int LastArg { get; private set; }
    public int Calls { get; private set; }

    public int Next(int bound)
    {
        LastArg = bound;
        Calls++;
        int value = _values[Math.Min(_index, _values.Length - 1)];
        _index++;
        if (value >= bound)
            throw new InvalidOperationException(
                $"scripted RNG {value} is out of range for bound {bound}");
        return value;
    }
}
