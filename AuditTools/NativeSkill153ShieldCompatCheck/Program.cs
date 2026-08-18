using System.Reflection;
using GameSvr;
using SystemModule;

PrepareRuntimeConfig();
InitializeRuntime();
VerifyEffectiveLevels();
VerifyActivationAndTimeout();
VerifyCooldown();
VerifyReductionAndExhaustion();
VerifyPhysicalResolverConsumption();
VerifyPlayerEntry();
VerifyHeroUnsupportedEntry();
VerifySourceContracts();

Console.WriteLine(
    "PASS native-skill153-shield level=1/2/3->2/4/8 " +
    "cooldown=30s window=strict>10s state=59/657 " +
    "reducer=truncate-high-main-stat*2.5 job3=zero-fail-closed " +
    "consumers=physical-positive-only carrier=independent-of-type27 " +
    "entry=player-native hero-unsupported-native-side-effects");
return;

static void VerifyEffectiveLevels()
{
    Equal((byte)0, EffectiveLevel(null), "null magic level");
    Equal((byte)1, EffectiveLevel(Magic(1, 3)), "level one");
    Equal((byte)2, EffectiveLevel(Magic(3, 2)), "train cap");

    var bonusCap = Magic(2, 3);
    SetMagicBonus(bonusCap, 3);
    Equal((byte)3, EffectiveLevel(bonusCap), "bonus train cap");

    var wrapped = Magic(byte.MaxValue, 3);
    SetMagicBonus(wrapped, 2);
    Equal((byte)1, EffectiveLevel(wrapped), "bonus byte wrap");
}

static void VerifyActivationAndTimeout()
{
    const int start = unchecked((int)0xFFFFFF00);
    foreach (var sample in new[]
             {
                 (Level: (byte)1, Charges: (ushort)2),
                 (Level: (byte)2, Charges: (ushort)4),
                 (Level: (byte)3, Charges: (ushort)8)
             })
    {
        var actor = NewActor();
        Assert(Activate(actor, Magic(sample.Level, 3), start),
            $"level {sample.Level} activation");
        Equal(sample.Charges, Charges(actor),
            $"level {sample.Level} charges");
        Assert(actor.HasNativeActiveState(59),
            $"level {sample.Level} state59");
        Equal(1, CountMessages(actor, Grobal2.RM_CHARSTATUSCHANGED),
            $"level {sample.Level} status packet");
        Equal(0, CountMessages(actor, Grobal2.RM_SYSMESSAGE),
            $"level {sample.Level} unexpected success text");
        var status = actor.m_MsgList.Single(message =>
            message.wIdent == Grobal2.RM_CHARSTATUSCHANGED);
        Assert(status.Payload is byte[] body && body.Length == 16,
            "status body length");
    }

    Equal(657, Grobal2.SM_CHARSTATUSCHANGED,
        "SM_CHARSTATUSCHANGED protocol");

    var timeoutActor = NewActor();
    Assert(Activate(timeoutActor, Magic(1, 3), start),
        "timeout activation");
    timeoutActor.m_MsgList.Clear();
    Process(timeoutActor, unchecked(start + 10_000));
    Equal((ushort)2, Charges(timeoutActor), "strict 10s boundary");
    Assert(timeoutActor.HasNativeActiveState(59),
        "strict 10s state");
    Equal(0, timeoutActor.m_MsgList.Count,
        "strict 10s emitted message");

    Process(timeoutActor, unchecked(start + 10_001));
    Equal((ushort)0, Charges(timeoutActor), "10s+1 timeout charges");
    Assert(!timeoutActor.HasNativeActiveState(59),
        "10s+1 timeout state");
    Equal(1, CountMessages(timeoutActor, Grobal2.RM_CHARSTATUSCHANGED),
        "timeout status packet");
    Equal(1, CountMessages(timeoutActor, Grobal2.RM_SYSMESSAGE),
        "timeout text count");
    Equal("无极盾状态消失",
        timeoutActor.m_MsgList.Single(message =>
            message.wIdent == Grobal2.RM_SYSMESSAGE).Buff,
        "timeout text");
}

static void VerifyCooldown()
{
    const int start = 100_000;
    var actor = NewActor();
    Assert(Activate(actor, Magic(3, 3), start), "cooldown activation");
    actor.m_MsgList.Clear();

    Assert(!Activate(actor, Magic(0, 3), start + 1),
        "invalid level during cooldown");
    Equal(0, actor.m_MsgList.Count,
        "invalid level reached cooldown message");

    Assert(!Activate(actor, Magic(3, 3), start + 1),
        "cooldown rejection");
    Equal("还需要29秒才能释放该技能",
        actor.m_MsgList.Single(message =>
            message.wIdent == Grobal2.RM_SYSMESSAGE).Buff,
        "cooldown truncated seconds");
    Equal((ushort)8, Charges(actor), "cooldown changed charges");

    actor.m_MsgList.Clear();
    Assert(!Activate(actor, Magic(3, 3), start + 29_999),
        "one millisecond cooldown rejection");
    Equal("还需要0秒才能释放该技能",
        actor.m_MsgList.Single(message =>
            message.wIdent == Grobal2.RM_SYSMESSAGE).Buff,
        "cooldown zero-second truncation");

    actor.m_MsgList.Clear();
    Assert(Activate(actor, Magic(2, 3), start + 30_000),
        "30s cooldown completion");
    Equal((ushort)4, Charges(actor), "cooldown recast charges");
    Equal(1, CountMessages(actor, Grobal2.RM_CHARSTATUSCHANGED),
        "cooldown recast status packet");
    Equal(0, CountMessages(actor, Grobal2.RM_SYSMESSAGE),
        "cooldown recast text");
}

static void VerifyReductionAndExhaustion()
{
    var actor = NewActor(M2Share.jWarr);
    actor.m_WAbil.DC = HUtil32.MakeLong(0, 101);
    actor.m_WAbil.MC = HUtil32.MakeLong(0, 3);
    actor.m_WAbil.SC = HUtil32.MakeLong(0, 5);
    Assert(Activate(actor, Magic(2, 3), 20_000),
        "reducer activation");
    actor.m_MsgList.Clear();

    Equal(252, Reduction(actor), "warrior truncation");
    Equal(48, Consume(actor, 300), "warrior reduced damage");
    Equal((ushort)3, Charges(actor), "first consumed charge");
    Equal(0, actor.m_MsgList.Count, "non-final charge message");

    Consume(actor, 300);
    Consume(actor, 300);
    Consume(actor, 300);
    Equal((ushort)0, Charges(actor), "exhausted charges");
    Assert(!actor.HasNativeActiveState(59), "exhausted state59");
    Equal(1, CountMessages(actor, Grobal2.RM_CHARSTATUSCHANGED),
        "exhausted status packet");
    Equal(0, CountMessages(actor, Grobal2.RM_SYSMESSAGE),
        "exhausted emitted timeout text");

    var wizard = NewActor(M2Share.jWizard);
    wizard.m_WAbil.MC = HUtil32.MakeLong(0, 3);
    Equal(7, Reduction(wizard), "wizard main stat");

    var taoist = NewActor(M2Share.jTaos);
    taoist.m_WAbil.SC = HUtil32.MakeLong(0, 5);
    Equal(12, Reduction(taoist), "taoist main stat");

    var job3 = NewActor(3);
    job3.m_WAbil.DC = HUtil32.MakeLong(0, (int)ushort.MaxValue);
    job3.m_WAbil.MC = job3.m_WAbil.DC;
    job3.m_WAbil.SC = job3.m_WAbil.DC;
    Equal(0, Reduction(job3), "job3 fail-closed reduction");
    Assert(Activate(job3, Magic(1, 3), 30_000),
        "job3 activation");
    job3.m_MsgList.Clear();
    Equal(77, Consume(job3, 77), "job3 identity damage");
    Equal((ushort)1, Charges(job3), "job3 charge consumption");

    var rawResolver = NewActor(M2Share.jWarr);
    rawResolver.m_WAbil.DC = HUtil32.MakeLong(0, 3);
    Assert(Activate(rawResolver, Magic(1, 3), 35_000),
        "raw resolver activation");
    rawResolver.ClearNativeActiveState(59);
    rawResolver.m_MsgList.Clear();
    Equal(-7, Consume(rawResolver, 0),
        "zero general damage still reduces");
    Equal((ushort)1, Charges(rawResolver),
        "zero general damage did not consume charge");
    Equal(-8, Consume(rawResolver, -1),
        "negative general damage still reduces");
    Equal((ushort)0, Charges(rawResolver),
        "negative general damage did not consume charge");
    Equal(1, CountMessages(rawResolver, Grobal2.RM_CHARSTATUSCHANGED),
        "bit-off exhaustion status packet");
}

static void VerifyPhysicalResolverConsumption()
{
    var actor = NewActor(M2Share.jWarr);
    actor.m_WAbil.AC = 0;
    actor.m_WAbil.DC = HUtil32.MakeLong(0, 101);
    Assert(Activate(actor, Magic(1, 3), 40_000),
        "physical activation");
    actor.m_MsgList.Clear();

    // The charge is consumed in StruckDamage (sub_73F9FC @0x73FB5F), NOT in
    // the armour getter: sub_767958 ends `call sub_76FFE8` + `ret 4`
    // (@0x7679A9-0x7679B2) and never reads word [+0x3FC].
    Equal(0, actor.GetHitStruckDamage(null, 0),
        "zero physical damage");
    Equal((ushort)2, Charges(actor),
        "armour getter must not consume a charge");
    Equal(200, actor.GetHitStruckDamage(null, 200),
        "armour getter must pass damage through unshielded");
    Equal((ushort)2, Charges(actor),
        "armour getter consumed a charge on a positive hit");

    // trunc(HiWord(DC)=101 * 2.5) = 252 (native @0x73FB78 fmul 2.5 then
    // sub_403580 = truncating fistp). 200-252 = -52 <= 0, so native returns
    // at the @0x73FBA6 `jle` gate WITHOUT landing — HP is untouched — and the
    // charge is still spent (@0x73FB85 `dec` runs before the gate).
    actor.m_WAbil.HP = 500;
    actor.m_WAbil.MaxHP = 500;
    actor.StruckDamage(200);
    Equal(500, actor.m_WAbil.HP,
        "over-absorbed hit must not land (0x73FBA6 jle is a return gate)");
    Equal((ushort)1, Charges(actor),
        "StruckDamage did not consume a charge");
}

static void VerifyPlayerEntry()
{
    M2Share.g_Config.nMagicAttackRage = 8;
    var manager = new MagicManager();
    var magic = Magic(1, 3);
    magic.MagicInfo.btEffect = 73;
    magic.MagicInfo.btEffectType = 2;

    var player = NewPlayer(100, 100);
    Assert(manager.DoSpell(player, magic, 109, 109, null),
        "player native nine-cell cast");
    Assert(Charges(player) == 2 && player.HasNativeActiveState(59),
        "player entry did not activate shield");
    int spell = IndexOfMessage(player, Grobal2.RM_SPELL);
    int status = IndexOfMessage(player, Grobal2.RM_CHARSTATUSCHANGED);
    int fire = IndexOfMessage(player, Grobal2.RM_MAGICFIRE);
    Assert(spell >= 0 && status > spell && fire > status,
        "player spell/activation/fire ordering");

    player.m_MsgList.Clear();
    Assert(!manager.DoSpell(player, magic, 109, 109, null),
        "player cooldown cast unexpectedly succeeded");
    Equal(1, CountMessages(player, Grobal2.RM_SPELL),
        "player cooldown RM_SPELL count");
    Equal(1, CountMessages(player, Grobal2.RM_SYSMESSAGE),
        "player cooldown hint count");
    Equal(0, CountMessages(player, Grobal2.RM_MAGICFIRE),
        "player cooldown emitted RM_MAGICFIRE");

    var outOfRange = NewPlayer(100, 100);
    Assert(!manager.DoSpell(outOfRange, Magic(1, 3), 110, 100, null),
        "player ten-cell cast unexpectedly succeeded");
    Equal(0, outOfRange.m_MsgList.Count,
        "player range failure emitted message");

    var legacyPlayer = NewPlayer(100, 100);
    legacyPlayer.m_nSoftVersionDateEx = 0;
    legacyPlayer.m_dwClientTick = 0;
    Assert(manager.DoSpell(legacyPlayer, Magic(1, 3), 109, 109, null),
        "legacy client gate blocked native skill153");
    Equal(1, CountMessages(legacyPlayer, Grobal2.RM_MAGICFIRE),
        "legacy client skill153 fire count");
}

static void VerifyHeroUnsupportedEntry()
{
    var environment = new Envirnoment();
    var hero = NewHero(environment, 100, 100);
    var target = NewActor();
    target.m_PEnvir = environment;
    target.m_nCurrX = 109;
    target.m_nCurrY = 109;
    hero.m_TargetCret = target;

    var magic = Magic(2, 1);
    magic.MagicInfo.btDefSpell = 5;
    magic.MagicInfo.wSpell = 8;
    magic.MagicInfo.btEffect = 74;
    magic.MagicInfo.btEffectType = 3;
    Equal((ushort)11, InvokeHeroSpellPoint(hero, magic),
        "hero native MP formula");

    SetHeroMagicTick(hero, 1234);
    Assert(!InvokeHeroRelease(hero, magic, 9000),
        "native hero skill153 unexpectedly succeeded");
    Equal(89, hero.m_WAbil.MP, "native hero skill153 MP debit");
    Equal((ushort)0, Charges(hero),
        "native hero skill153 activated shield charges");
    Assert(!hero.HasNativeActiveState(59),
        "native hero skill153 activated state59");
    Equal(1234, GetHeroMagicTick(hero),
        "unsupported hero cast updated success tick");
    Equal(1, CountMessages(hero, Grobal2.RM_SPELL),
        "unsupported hero RM_SPELL count");
    Equal(0, CountMessages(hero, Grobal2.RM_MAGICFIRE),
        "unsupported hero emitted RM_MAGICFIRE");
    var spell = hero.m_MsgList.Single(message =>
        message.wIdent == Grobal2.RM_SPELL);
    Equal(HUtil32.MakeWord(74, 3), spell.wParam,
        "unsupported hero spell effect/type");
    Equal(109, spell.nParam1, "unsupported hero target X");
    Equal(109, spell.nParam2, "unsupported hero target Y");
    Equal(153, spell.nParam3, "unsupported hero magic id");
    Assert(spell.Payload != null, "unsupported hero native spell payload");

    var outOfRange = NewHero(environment, 100, 100);
    var distantTarget = NewActor();
    distantTarget.m_PEnvir = environment;
    distantTarget.m_nCurrX = 110;
    distantTarget.m_nCurrY = 100;
    outOfRange.m_TargetCret = distantTarget;
    Assert(!InvokeHeroRelease(outOfRange, magic, 9000),
        "hero ten-cell cast unexpectedly succeeded");
    Equal(100, outOfRange.m_WAbil.MP,
        "hero range failure debited MP");
    Equal(0, outOfRange.m_MsgList.Count,
        "hero range failure emitted RM_SPELL");

    var targetless = NewHero(environment, 100, 100);
    targetless.m_btJob = M2Share.jWizard;
    targetless.m_HeroMagicList.Add(magic);
    var master = NewPlayer(100, 100);
    master.m_sCharName = $"skill153-{Guid.NewGuid():N}";
    SetHeroCommand(master.m_sCharName, 153);
    SetHeroMagicTick(targetless, 0);
    InvokeHeroTryCast(targetless, master, 9000);
    Equal(89, targetless.m_WAbil.MP,
        "targetless hero skill153 MP debit");
    Equal(1, CountMessages(targetless, Grobal2.RM_SPELL),
        "targetless hero RM_SPELL count");
    Equal(0, CountMessages(targetless, Grobal2.RM_MAGICFIRE),
        "targetless hero emitted RM_MAGICFIRE");
    var targetlessSpell = targetless.m_MsgList.Single(message =>
        message.wIdent == Grobal2.RM_SPELL);
    Equal(100, targetlessSpell.nParam1, "targetless hero self X");
    Equal(100, targetlessSpell.nParam2, "targetless hero self Y");
}

static void VerifySourceContracts()
{
    string root = FindRepositoryRoot();
    string shield = File.ReadAllText(Path.Combine(root, "GameSvr", "Actors",
        "TBaseObject.NativeSkill153Shield.cs"));
    string timed = File.ReadAllText(Path.Combine(root, "GameSvr", "Actors",
        "TBaseObject.TimedAbility.cs"));
    string baseRun = File.ReadAllText(Path.Combine(root, "GameSvr", "Actors",
        "TBaseObject.Base.cs"));
    string spells = File.ReadAllText(Path.Combine(root, "GameSvr", "Spells",
        "SpellsDef.cs"));
    string manager = File.ReadAllText(Path.Combine(root, "GameSvr", "Spells",
        "MagicManager.cs"));
    string player = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.cs"));
    string hero = File.ReadAllText(Path.Combine(root, "GameSvr", "Actors",
        "HeroObject.cs"));

    Require(shield, "NativeSkill153ShieldState = 59",
        "state59 constant");
    Require(shield, "elapsed <= NativeSkill153ShieldWindowMilliseconds",
        "strict timeout predicate");
    Require(shield, "Math.Truncate(high * 2.5d)",
        "native reducer rounding");
    Reject(shield, "AddTimedAbility", "skill153 reused timed ability");
    Reject(shield, "TimedAbilityNode", "skill153 reused timed node");
    Require(baseRun, "ProcessNativeSkill153Shield(dwRunTick);",
        "base actor lifecycle hook");
    Require(timed, "case 59:", "script type27 dormant carrier");
    Require(timed, "ApplyTimedJobAttack(value);",
        "script type27 semantics unexpectedly changed");

    Require(spells, "public const int SKILL_153 = 153;",
        "skill153 definition");
    // Re-based 2026-08-04: this used to pin `SpellsDef.SKILL_153 ? 9 :`, i.e. a
    // SKILL_153-only escape hatch out of g_Config.nMagicAttackRage. Native
    // sub_6ED62C @0x6ED676-0x6ED67E applies a HARDCODED literal 9 to EVERY
    // spell — `call sub_78FE88; cmp eax,9; jg 0x6EE0C7` — and reads no config
    // at all, so the special case was itself the divergence (every other spell
    // reached only 8 tiles). The gate is now unconditional, which is strictly
    // stronger: it also forbids reintroducing the config read.
    Require(manager, "const int magicAttackRange = 9;",
        "player native fixed nine-cell gate");
    Reject(manager, "M2Share.g_Config.nMagicAttackRage",
        "spell range must not read config (native literal 9)");
    Reject(manager, "SpellsDef.SKILL_153 ? 9 :",
        "skill153 range special case must stay deleted");
    Require(manager,
        "UserMagic.MagicInfo.wMagicID != SpellsDef.SKILL_153",
        "legacy client skill153 bypass");
    int playerSpell = manager.IndexOf("SendNativeSpell(PlayObject",
        StringComparison.Ordinal);
    int playerCase = manager.IndexOf("case SpellsDef.SKILL_153:",
        StringComparison.Ordinal);
    int playerActivation = manager.IndexOf(
        "TryActivateNativeSkill153Shield(", playerCase,
        StringComparison.Ordinal);
    int playerFire = manager.IndexOf("SendNativeMagicFire(PlayObject",
        playerActivation, StringComparison.Ordinal);
    Assert(playerSpell >= 0 && playerCase > playerSpell &&
        playerActivation > playerCase && playerFire > playerActivation,
        "player spell/activation/fire source ordering");
    string player153Case = manager.Substring(playerCase,
        manager.IndexOf("break;", playerCase, StringComparison.Ordinal) -
        playerCase);
    Reject(player153Case, "boTrain = true",
        "player skill153 incorrectly trains");
    int playerDebit = player.IndexOf("DamageSpell(nSpellPoint);",
        StringComparison.Ordinal);
    int playerDispatch = player.IndexOf(
        "M2Share.MagicManager.DoSpell(this, UserMagic",
        StringComparison.Ordinal);
    Assert(playerDebit >= 0 && playerDispatch > playerDebit,
        "player MP debit does not precede native dispatch");

    Require(hero,
        "return TPlayObject.GetNativeMagicProducerMpCost(userMagic);",
        "hero skill153 native MP formula");
    Require(hero, "targetlessNativeSkill153",
        "targetless hero skill153 command gate");
    Require(hero, "TryCastSkill(master, dwCurTick);",
        "idle hero skill153 command entry");
    int heroRange = hero.IndexOf(
        "Math.Max(Math.Abs(m_nCurrX - spellTarget.m_nCurrX)",
        StringComparison.Ordinal);
    int heroCost = hero.IndexOf("var spellPoint = GetHeroSpellPoint(userMagic);",
        heroRange, StringComparison.Ordinal);
    int heroDebit = hero.IndexOf("m_WAbil.MP -= spellPoint;",
        heroCost, StringComparison.Ordinal);
    int heroUnsupported = hero.IndexOf(
        "if (nativeUnsupportedSkill153)", heroDebit,
        StringComparison.Ordinal);
    int heroSpell = hero.IndexOf("MagicManager.SendNativeSpell", heroUnsupported,
        StringComparison.Ordinal);
    int heroFailure = hero.IndexOf("return false;", heroSpell,
        StringComparison.Ordinal);
    int heroSuccessTick = hero.IndexOf("m_dwHeroMagicTick = dwCurTick;",
        heroFailure, StringComparison.Ordinal);
    Assert(heroRange >= 0 && heroCost > heroRange &&
        heroDebit > heroCost && heroUnsupported > heroDebit &&
        heroSpell > heroUnsupported && heroFailure > heroSpell &&
        heroSuccessTick > heroFailure,
        "hero range/MP/spell/failure/tick source ordering");
    string heroUnsupportedBlock = hero.Substring(heroUnsupported,
        heroSuccessTick - heroUnsupported);
    Reject(heroUnsupportedBlock, "RM_MAGICFIRE",
        "unsupported hero emitted fire animation");
    Reject(hero, "TryActivateNativeSkill153Shield(userMagic",
        "hero incorrectly activates player skill153 helper");
}

static TBaseObject NewActor(int job = M2Share.jWarr)
{
    var actor = new TBaseObject
    {
        m_btJob = unchecked((byte)job),
        m_PEnvir = new Envirnoment(),
        m_boObMode = true
    };
    actor.m_MsgList.Clear();
    return actor;
}

static TPlayObject NewPlayer(int x, int y)
{
    var player = new TPlayObject
    {
        m_nCurrX = unchecked((short)x),
        m_nCurrY = unchecked((short)y),
        m_PEnvir = new Envirnoment(),
        m_boObMode = true,
        m_nSoftVersionDateEx = 1,
        m_dwClientTick = 1
    };
    player.m_MsgList.Clear();
    return player;
}

static HeroObject NewHero(Envirnoment environment, int x, int y)
{
    var hero = new HeroObject
    {
        m_nCurrX = unchecked((short)x),
        m_nCurrY = unchecked((short)y),
        m_PEnvir = environment,
        m_boObMode = true
    };
    hero.m_WAbil.MP = 100;
    hero.m_MsgList.Clear();
    return hero;
}

static TUserMagic Magic(byte level, byte trainLevel)
{
    return new TUserMagic
    {
        btLevel = level,
        wMagIdx = 153,
        MagicInfo = new TMagic
        {
            wMagicID = 153,
            btTrainLv = trainLevel
        }
    };
}

static void SetMagicBonus(TUserMagic magic, byte bonus)
{
    typeof(TUserMagic).GetField("NativeLevelBonus",
        BindingFlags.Instance | BindingFlags.NonPublic)!
        .SetValue(magic, bonus);
}

static byte EffectiveLevel(TUserMagic magic) =>
    (byte)InvokeStatic("GetNativeSkill153ShieldEffectiveLevel", magic);

static bool Activate(TBaseObject actor, TUserMagic magic, int now) =>
    (bool)Invoke(actor, "TryActivateNativeSkill153Shield",
        new[] { typeof(TUserMagic), typeof(int) }, magic, now);

static void Process(TBaseObject actor, int now) =>
    Invoke(actor, "ProcessNativeSkill153Shield", new[] { typeof(int) }, now);

static int Consume(TBaseObject actor, int damage) =>
    (int)Invoke(actor, "ConsumeNativeSkill153ShieldCharge",
        new[] { typeof(int) }, damage);

static int Reduction(TBaseObject actor) =>
    (int)Invoke(actor, "GetNativeSkill153ShieldReduction", Type.EmptyTypes);

static ushort Charges(TBaseObject actor) =>
    (ushort)(typeof(TBaseObject).GetField(
        "m_wNativeSkill153ShieldCharges",
        BindingFlags.Instance | BindingFlags.NonPublic) ??
        throw new MissingFieldException("m_wNativeSkill153ShieldCharges"))
        .GetValue(actor)!;

static object Invoke(TBaseObject actor, string name, Type[] types,
    params object[] arguments)
{
    var method = typeof(TBaseObject).GetMethod(name,
        BindingFlags.Instance | BindingFlags.NonPublic, null, types, null) ??
        throw new MissingMethodException(name);
    return method.Invoke(actor, arguments)!;
}

static object InvokeStatic(string name, params object[] arguments)
{
    var method = typeof(TBaseObject).GetMethod(name,
        BindingFlags.Static | BindingFlags.NonPublic) ??
        throw new MissingMethodException(name);
    return method.Invoke(null, arguments)!;
}

static int CountMessages(TBaseObject actor, int ident) =>
    actor.m_MsgList.Count(message => message.wIdent == ident);

static int IndexOfMessage(TBaseObject actor, int ident)
{
    for (var i = 0; i < actor.m_MsgList.Count; i++)
    {
        if (actor.m_MsgList[i].wIdent == ident)
            return i;
    }
    return -1;
}

static ushort InvokeHeroSpellPoint(HeroObject hero, TUserMagic magic) =>
    (ushort)(typeof(HeroObject).GetMethod("GetHeroSpellPoint",
        BindingFlags.Instance | BindingFlags.NonPublic) ??
        throw new MissingMethodException("GetHeroSpellPoint"))
        .Invoke(hero, new object[] { magic })!;

static bool InvokeHeroRelease(HeroObject hero, TUserMagic magic, int now) =>
    (bool)(typeof(HeroObject).GetMethod("TryReleaseHeroMagic",
        BindingFlags.Instance | BindingFlags.NonPublic) ??
        throw new MissingMethodException("TryReleaseHeroMagic"))
        .Invoke(hero, new object[] { magic, now })!;

static void InvokeHeroTryCast(HeroObject hero, TPlayObject master, int now) =>
    (typeof(HeroObject).GetMethod("TryCastSkill",
        BindingFlags.Instance | BindingFlags.NonPublic) ??
        throw new MissingMethodException("TryCastSkill"))
        .Invoke(hero, new object[] { master, now });

static void SetHeroCommand(string playerName, int magicId)
{
    var state = typeof(HeroObject).Assembly.GetType(
        "GameSvr.Plugins.YanshenHeroCastState") ??
        throw new TypeLoadException("YanshenHeroCastState");
    var set = state.GetMethod("Set",
        BindingFlags.Static | BindingFlags.NonPublic) ??
        throw new MissingMethodException("YanshenHeroCastState.Set");
    set.Invoke(null, new object[] { playerName, magicId, 0 });
}

static FieldInfo HeroMagicTickField() =>
    typeof(HeroObject).GetField("m_dwHeroMagicTick",
        BindingFlags.Instance | BindingFlags.NonPublic) ??
    throw new MissingFieldException("m_dwHeroMagicTick");

static void SetHeroMagicTick(HeroObject hero, int value) =>
    HeroMagicTickField().SetValue(hero, value);

static int GetHeroMagicTick(HeroObject hero) =>
    (int)HeroMagicTickField().GetValue(hero)!;

static void InitializeRuntime()
{
    M2Share.g_Config = new GameSvrConfig();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new System.Collections.ArrayList();
}

static void PrepareRuntimeConfig()
{
    string directory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(directory, "!Setup.txt"), "[Server]");
    File.WriteAllText(Path.Combine(directory, "String.ini"), "[String]");
    File.WriteAllText(Path.Combine(directory, "Command.conf"), "[Command]");
    string share = Path.GetFullPath(Path.Combine(directory, "..", "Share"));
    Directory.CreateDirectory(share);
    File.WriteAllText(Path.Combine(share, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]");
    File.WriteAllText(Path.Combine(share, "ServerData.ini"), "[Integer]");
}

static string FindRepositoryRoot()
{
    foreach (string start in new[]
             { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        for (var directory = new DirectoryInfo(start); directory != null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName,
                    "GameSvr", "GameSvr.csproj")))
            {
                return directory.FullName;
            }
        }
    }
    throw new DirectoryNotFoundException("GameSvr/GameSvr.csproj");
}

static void Require(string source, string value, string label) =>
    Assert(source.Contains(value, StringComparison.Ordinal), label);

static void Reject(string source, string value, string label) =>
    Assert(!source.Contains(value, StringComparison.Ordinal), label);

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            $"{label}: expected={expected}, actual={actual}");
    }
}

static void Assert(bool condition, string label)
{
    if (!condition)
        throw new InvalidOperationException(label);
}
