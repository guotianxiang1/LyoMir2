using GameSvr.Services;
using Ctx44 = GameSvr.Services.NativeTimedAbilityCombatConsumer.Type44UnionContext;
using Ctx46 = GameSvr.Services.NativeTimedAbilityCombatConsumer.Type46EndpointContext;
using Ctx3 = GameSvr.Services.NativeTimedAbilityCombatConsumer.Job3ResolverContext;
using Ctx74 = GameSvr.Services.NativeTimedAbilityCombatConsumer.Type74ContestContext;
using Outcome = GameSvr.Services.NativeTimedAbilityCombatConsumer.Job3ResolverOutcome;

// Contract check for NativeTimedAbilityCombatConsumer (PAS timed ability combat consumers
// 44/46/74), locked against sub_741764 / sub_76CD8C+sub_76CD5C / job-3 resolvers / sub_7744B4.

try
{
    VerifyDormant();
    VerifyType44Union();
    VerifyType46Endpoint();
    VerifyJob3Resolvers();
    VerifyType74Contest();
    VerifyOwnerInventory();

    Console.WriteLine(
        "PASS NativeTimedAbilityCombatConsumerCompatCheck type44=union(coeff.1/.4/.2/.2+flat+pct) " +
        "type46=endpoint(job0-3,mode/low>=high/low+rnd) job3=1024/260(1.8)/264(2.4)/268(3.0) " +
        "type74=contest(race11/null/clamp30-95>rnd) owners=15 dormant=true");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"NativeTimedAbilityCombatConsumerCompatCheck FAIL: {ex.Message}");
    return 1;
}

static void Assert(bool cond, string msg)
{
    if (!cond)
        throw new Exception(msg);
}

static void VerifyDormant()
{
    Assert(NativeTimedAbilityCombatConsumer.DormantNotWiredIntoLiveCombat, "dormant flag");
    Assert(NativeTimedAbilityCombatConsumer.Type74MagicHitOffset == 0x272, "type74 off");
    Assert(NativeTimedAbilityCombatConsumer.UnionSelectorOffset == 0x400, "union off");
    Assert(NativeTimedAbilityCombatConsumer.JobEndpointCcLowOffset == 0x2A4, "cc low off");
}

static void VerifyType44Union()
{
    // Gate passes (lvl>=4, otherJob==0), selfJob=1 -> coeff 0.40. dmg=100 -> drain=40, mana 100->60.
    // union table null -> skip; flat 5 -> 95; percent 10 -> 95*90/100 = 85.
    var r = NativeTimedAbilityCombatConsumer.ApplyType44UnionReceiver(new Ctx44(
        selfJob: 1, otherJob: 0, effectiveMagicLevel: 4, incomingDamage: 100,
        unionSelector: 0, unionTable: null, flatReduction: 5, percentReduction: 10, selfMana: 100));
    Assert(r.ManaSideEffectApplied, "44 gate pass");
    Assert(r.NewSelfMana == 60, $"44 mana drain (got {r.NewSelfMana})");
    Assert(r.FinalDamage == 85, $"44 final dmg (got {r.FinalDamage})");

    // Coefficients per self job (dmg=100, mana=1000 so drain visible).
    (byte job, int drain)[] coeff = { (0, 10), (1, 40), (2, 20), (3, 20), (4, 0) };
    foreach (var (job, drain) in coeff)
    {
        var c = NativeTimedAbilityCombatConsumer.ApplyType44UnionReceiver(new Ctx44(
            job, 0, 4, 100, 0, null, 0, 0, 1000));
        Assert(c.NewSelfMana == 1000 - drain, $"44 coeff job {job} drain {drain} (got {1000 - c.NewSelfMana})");
    }

    // Gate fails when magic level < 4: no mana effect, damage still flat+percent reduced.
    var ng = NativeTimedAbilityCombatConsumer.ApplyType44UnionReceiver(new Ctx44(
        1, 0, 3, 100, 0, null, 5, 10, 100));
    Assert(!ng.ManaSideEffectApplied && ng.NewSelfMana == 100, "44 gate fail lvl<4");
    Assert(ng.FinalDamage == 85, "44 gate fail still reduces");

    // Gate fails when attacker job != 0.
    var nj = NativeTimedAbilityCombatConsumer.ApplyType44UnionReceiver(new Ctx44(
        1, 2, 4, 100, 0, null, 0, 0, 100));
    Assert(!nj.ManaSideEffectApplied, "44 gate fail otherJob!=0");
}

static void VerifyType46Endpoint()
{
    // job > 3 -> 0 with no RNG use.
    Assert(NativeTimedAbilityCombatConsumer.SelectType46JobEndpoint(
        new Ctx46(4, 1, 9, 2, 8, 3, 7, 4, 6, 0), _ => throw new Exception("no rng")) == 0, "46 job>3 -> 0");

    // mode != 0 -> high endpoint (per job).
    Assert(NativeTimedAbilityCombatConsumer.SelectType46JobEndpoint(
        new Ctx46(0, 1, 9, 0, 0, 0, 0, 0, 0, 1), _ => throw new Exception("no rng")) == 9, "46 job0 mode!=0 -> dcHigh");
    Assert(NativeTimedAbilityCombatConsumer.SelectType46JobEndpoint(
        new Ctx46(3, 0, 0, 0, 0, 0, 0, 4, 66, 1), _ => throw new Exception("no rng")) == 66, "46 job3 mode!=0 -> ccHigh");

    // mode == 0, low >= high -> low (no RNG).
    Assert(NativeTimedAbilityCombatConsumer.SelectType46JobEndpoint(
        new Ctx46(1, 0, 0, 50, 20, 0, 0, 0, 0, 0), _ => throw new Exception("no rng")) == 50, "46 low>=high -> low");

    // mode == 0, low < high -> low + Random(high-low); RNG consumed with (high-low).
    int seen = -1;
    int res = NativeTimedAbilityCombatConsumer.SelectType46JobEndpoint(
        new Ctx46(1, 0, 0, 20, 50, 0, 0, 0, 0, 0), n => { seen = n; return 7; });
    Assert(seen == 30 && res == 27, $"46 low+rnd(high-low) (n={seen} res={res})");
}

static void VerifyJob3Resolvers()
{
    // 1024: job != 3 -> NotJob3.
    Assert(NativeTimedAbilityCombatConsumer.ResolveJob3Attack1024(
        new Ctx3(1, 10, 30, 4, 100, true, true, false, false)).Outcome == Outcome.NotJob3, "1024 !job3");
    // 1024: job3 eligible not blocked -> Delivered power=base, consumes charge when power>0.
    var d1024 = NativeTimedAbilityCombatConsumer.ResolveJob3Attack1024(
        new Ctx3(3, 10, 30, 4, 100, true, true, false, false));
    Assert(d1024.Outcome == Outcome.Delivered && d1024.Power == 100 && d1024.ConsumesCharge, "1024 delivered");
    // 1024: blocked -> NotHitEligible.
    Assert(NativeTimedAbilityCombatConsumer.ResolveJob3Attack1024(
        new Ctx3(3, 10, 30, 4, 100, true, true, true, false)).Outcome == Outcome.NotHitEligible, "1024 blocked");

    // 260: no state -> FallbackTo1024; with state -> Round((0.2*lvl+1.8)*base).
    Assert(NativeTimedAbilityCombatConsumer.ResolveJob3Attack260(
        new Ctx3(3, 10, 30, 4, 100, false, true, false, false)).Outcome == Outcome.FallbackTo1024, "260 fallback");
    var d260 = NativeTimedAbilityCombatConsumer.ResolveJob3Attack260(
        new Ctx3(3, 10, 30, 4, 100, true, true, false, false));
    Assert(d260.Outcome == Outcome.Delivered && d260.Power == 260, $"260 power (got {d260.Power})"); // (0.8+1.8)*100

    // 264: no state -> StateAbsent; with state -> Round((0.2*lvl+2.4)*base).
    Assert(NativeTimedAbilityCombatConsumer.ResolveJob3Attack264(
        new Ctx3(3, 10, 30, 4, 100, false, true, false, false)).Outcome == Outcome.StateAbsent, "264 absent");
    Assert(NativeTimedAbilityCombatConsumer.ResolveJob3Attack264(
        new Ctx3(3, 10, 30, 4, 100, true, true, false, false)).Power == 320, "264 power"); // (0.8+2.4)*100

    // 268: null target or no state -> StateAbsent; else Round((0.2*lvl+3.0)*base).
    Assert(NativeTimedAbilityCombatConsumer.ResolveJob3Attack268(
        new Ctx3(3, 10, 30, 4, 100, true, true, false, true)).Outcome == Outcome.StateAbsent, "268 null target");
    Assert(NativeTimedAbilityCombatConsumer.ResolveJob3Attack268(
        new Ctx3(3, 10, 30, 4, 100, true, true, false, false)).Power == 380, "268 power"); // (0.8+3.0)*100
}

static void VerifyType74Contest()
{
    // race 11 -> always false (no RNG).
    Assert(!NativeTimedAbilityCombatConsumer.Type74MagicHitContest(
        new Ctx74(11, false, 50, 40), _ => throw new Exception("no rng")), "74 race11 false");
    // source null -> false (no RNG).
    Assert(!NativeTimedAbilityCombatConsumer.Type74MagicHitContest(
        new Ctx74(1, true, 50, 40), _ => throw new Exception("no rng")), "74 null false");

    // chance = clamp(100*(hit+10)/(anti+10), 30, 95). hit=50,anti=40 -> 100*60/50=120 -> 95.
    Assert(NativeTimedAbilityCombatConsumer.Type74MagicHitContest(
        new Ctx74(1, false, 50, 40), _ => 94), "74 chance95 > 94");
    Assert(!NativeTimedAbilityCombatConsumer.Type74MagicHitContest(
        new Ctx74(1, false, 50, 40), _ => 95), "74 chance95 not > 95");
    // low clamp: hit=0,anti=100 -> 100*10/110=9 -> clamp 30.
    Assert(NativeTimedAbilityCombatConsumer.Type74MagicHitContest(
        new Ctx74(1, false, 0, 100), _ => 29), "74 clamp30 > 29");
    Assert(!NativeTimedAbilityCombatConsumer.Type74MagicHitContest(
        new Ctx74(1, false, 0, 100), _ => 30), "74 clamp30 not > 30");
}

static void VerifyOwnerInventory()
{
    // The reversed type-74 contest gates 15 unique owner functions; type-46 endpoint has 6 consumers.
    Assert(NativeTimedAbilityCombatConsumer.Type74ContestOwners.Length == 15, "74 owners=15");
    Assert(NativeTimedAbilityCombatConsumer.Type46EndpointConsumers.Length == 6, "46 consumers=6");
}
