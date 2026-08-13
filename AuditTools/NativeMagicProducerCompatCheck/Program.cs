using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using GameSvr;
using SystemModule;

try
{
    PrepareRuntimeConfig();
    InitializeRuntime();

    VerifyFormulaAndRandomOrder();
    VerifyProducerQueueAndRandomOrder();
    VerifySkill39DelayedPush();
    VerifyTrainingNotifications();
    VerifySourceContracts();
    VerifyWiringContracts();

    Console.WriteLine(
        "PASS NativeMagicProducerCompatCheck skills=1/5/11/35/39 " +
        "formula=mp+effective-level+raw-mc+luck-only " +
        "rng=random0-consumed+native-order queue=10177/600ms " +
        "training=3s+switch-flush+800ms-levelup push=10417/700ms " +
        "wiring=mp+1/5/11/35/39+message10417");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        $"NativeMagicProducerCompatCheck FAIL: {exception}");
    return 1;
}

static void VerifyFormulaAndRandomOrder()
{
    var mpMagic = Magic(1, level: 2);
    mpMagic.MagicInfo.btDefSpell = 5;
    mpMagic.MagicInfo.wSpell = 5;
    Equal((ushort)9, Invoke<ushort>(null,
        "GetNativeMagicProducerMpCost", mpMagic),
        "native MP cost must use double quarter and raw level");

    var effectiveMagic = Magic(1, level: 250, trainLevel: 20);
    SetMagicLevelBonus(effectiveMagic, 10);
    Equal(4, Invoke<int>(null,
        "GetNativeMagicProducerEffectiveLevel", effectiveMagic),
        "effective level must byte-wrap before clamp");
    effectiveMagic.MagicInfo.btTrainLv = 3;
    Equal(3, Invoke<int>(null,
        "GetNativeMagicProducerEffectiveLevel", effectiveMagic),
        "effective level train cap");

    var zeroRangeMagic = Magic(1, level: 1);
    zeroRangeMagic.MagicInfo.wPower = 8;
    zeroRangeMagic.MagicInfo.wMaxPower = 8;
    zeroRangeMagic.MagicInfo.btDefPower = 3;
    zeroRangeMagic.MagicInfo.btDefMaxPower = 3;
    var random = UseRandom(101, 202);
    Equal(7, Invoke<int>(null,
        "CalculateNativeMagicProducerSkillPower", zeroRangeMagic),
        "raw skill value with zero ranges");
    EqualSequence(new int?[] { null, null }, random.MaxValues,
        "Random(0) must consume both skill-value RNG calls");

    var rangedMagic = Magic(1, level: 2);
    rangedMagic.MagicInfo.wPower = 8;
    rangedMagic.MagicInfo.wMaxPower = 11;
    rangedMagic.MagicInfo.btDefPower = 2;
    rangedMagic.MagicInfo.btDefMaxPower = 4;
    random = UseRandom(2, 1);
    Equal(11, Invoke<int>(null,
        "CalculateNativeMagicProducerSkillPower", rangedMagic),
        "raw skill value formula");
    EqualSequence(new int?[] { 3, 2 }, random.MaxValues,
        "skill-value RNG order must be power then default power");

    // ---- divisor regression pins (staging/spellpower_formula_exact_20260803.md) --
    // Native sub_4C8658 divides by the float32 literal 4.0 at [0x4C86B8]; it never
    // reads btTrainLv. The discarded `(btTrainLv + 1)` form coincides with 4.0 only
    // when btTrainLv == 3, so every case below uses btTrainLv != 3 to make the two
    // forms disagree — a re-introduced train-level divisor cannot pass these.
    var offCapMagic = Magic(1, level: 3, trainLevel: 5);
    offCapMagic.MagicInfo.wPower = 10;
    offCapMagic.MagicInfo.wMaxPower = 10;
    offCapMagic.MagicInfo.btDefPower = 0;
    offCapMagic.MagicInfo.btDefMaxPower = 0;
    UseRandom(0, 0);
    // Round((3 + 1) * 10 / 4.0) = 10. The (btTrainLv+1)=6 form would yield 7.
    Equal(10, Invoke<int>(null,
        "CalculateNativeMagicProducerSkillPower", offCapMagic),
        "skill power must divide by literal 4.0, not btTrainLv + 1");

    // Banker's rounding: sub_403574 is `fistp qword` = round-half-to-even.
    // Round((0 + 1) * 10 / 4.0) = Round(2.5) = 2, not 3.
    var halfMagic = Magic(1, level: 0, trainLevel: 5);
    halfMagic.MagicInfo.wPower = 10;
    halfMagic.MagicInfo.wMaxPower = 10;
    halfMagic.MagicInfo.btDefPower = 0;
    halfMagic.MagicInfo.btDefMaxPower = 0;
    UseRandom(0, 0);
    Equal(2, Invoke<int>(null,
        "CalculateNativeMagicProducerSkillPower", halfMagic),
        "skill power must round half to even (fistp qword)");

    // Native sub_4C870C: default-power roll FIRST, then EFFECTIVE level over 4.0.
    var scaledMagic = Magic(1, level: 2, trainLevel: 5);
    scaledMagic.MagicInfo.btDefPower = 1;
    scaledMagic.MagicInfo.btDefMaxPower = 4;
    SetMagicLevelBonus(scaledMagic, 1);
    random = UseRandom(2);
    // effLevel = min(2 + 1, 5) = 3 -> Round((3 + 1) * 20 / 4.0) + 1 + 2 = 23.
    Equal(23, Invoke<int>(null,
        "CalculateNativeMagicProducerScaledPower", scaledMagic, 20),
        "scaled power uses effective level over literal 4.0");
    EqualSequence(new int?[] { 3 }, random.MaxValues,
        "scaled power draws the default-power roll first");

    // Native sub_4C8764: Round(nInt * (2*effLevel + 6) / 12.0) + defPower term.
    var type13Magic = Magic(1, level: 2, trainLevel: 5);
    type13Magic.MagicInfo.btDefPower = 1;
    type13Magic.MagicInfo.btDefMaxPower = 4;
    SetMagicLevelBonus(type13Magic, 1);
    random = UseRandom(2);
    // effLevel = 3 -> Round(30 * (2*3 + 6) / 12.0) + 1 + 2 = 30 + 3 = 33.
    Equal(33, Invoke<int>(null,
        "CalculateNativeMagicProducer13Power", type13Magic, 30),
        "type13 power uses (2*effLevel + 6) / 12.0");
    EqualSequence(new int?[] { 3 }, random.MaxValues,
        "type13 draws the default-power roll first");

    // effective level is capped by btTrainLv (sub_4C896C), so raising btLevel past
    // the cap must not raise the power any further.
    var cappedType13 = Magic(1, level: 9, trainLevel: 3);
    cappedType13.MagicInfo.btDefPower = 0;
    cappedType13.MagicInfo.btDefMaxPower = 0;
    UseRandom(0);
    // effLevel = min(9, 3) = 3 -> Round(30 * 12 / 12.0) = 30.
    Equal(30, Invoke<int>(null,
        "CalculateNativeMagicProducer13Power", cappedType13, 30),
        "type13 effective level respects the train cap");

    var player = NewPlayer();
    player.m_nLuck = 1;
    random = UseRandom(0);
    Equal(14, Invoke<int>(player, "NativeLuckOnlyRoll", 10, 4),
        "positive luck forced maximum");
    EqualSequence(new int?[] { 9 }, random.MaxValues,
        "positive luck forced maximum RNG sequence");

    random = UseRandom(1, 3);
    Equal(13, Invoke<int>(player, "NativeLuckOnlyRoll", 10, 4),
        "positive luck normal spread");
    EqualSequence(new int?[] { 9, 5 }, random.MaxValues,
        "positive luck normal RNG sequence");

    player.m_nLuck = 0;
    random = UseRandom(4);
    Equal(14, Invoke<int>(player, "NativeLuckOnlyRoll", 10, 4),
        "zero luck spread");
    EqualSequence(new int?[] { 5 }, random.MaxValues,
        "zero luck RNG sequence");

    player.m_nLuck = -1;
    random = UseRandom(4, 0);
    Equal(10, Invoke<int>(player, "NativeLuckOnlyRoll", 10, 4),
        "negative luck forced minimum");
    EqualSequence(new int?[] { 5, 9 }, random.MaxValues,
        "negative luck spread then curse RNG sequence");

    player.m_nLuck = -10;
    random = UseRandom(0, 777);
    Equal(10, Invoke<int>(player, "NativeLuckOnlyRoll", 10, 0),
        "negative luck zero-range chance");
    EqualSequence(new int?[] { 1, null }, random.MaxValues,
        "zero-range curse chance must consume parameterless RNG");
}

static void VerifyProducerQueueAndRandomOrder()
{
    var player = NewPlayer();
    player.m_WAbil.MC = HUtil32.MakeLong(10, 10);
    player.m_nCurrX = 10;
    player.m_nCurrY = 11;

    var target = NewTarget(race: 50, x: 21, y: 22);
    var magic = Magic(11, level: 0, trainLevel: 3);
    var random = UseRandom(0, 111, 222, 0);
    int before = HUtil32.GetTickCount();
    Assert(Invoke<bool>(player, "TryProduceNativeMagic11", magic, target),
        "skill11 producer rejected valid target");
    EqualSequence(new int?[] { 100, null, null, 2 }, random.MaxValues,
        "skill11 admission/power/luck RNG order");

    var message = SingleMessage(player, Grobal2.RM_NATIVE_MAGIC_EFFECT);
    Equal(1, message.wParam, "message10177 category");
    AssertDelay(message, before, 600, "message10177 delay");
    EqualPayload(message.Payload, "Target", target);
    EqualPayload(message.Payload, "RawDamage", 10);
    EqualPayload(message.Payload, "SkillId", (ushort)11);
    EqualPayload(message.Payload, "X", (short)21);
    EqualPayload(message.Payload, "Y", (short)22);
    EqualPayload(message.Payload, "Range", (ushort)2);
    EqualPayload(message.Payload, "Arg0", true);
    EqualPayload(message.Payload, "Flags", (byte)0);

    player.m_MsgList.Clear();
    target.m_btRaceServer = Grobal2.RC_GUARD;
    random = UseRandom();
    Assert(!Invoke<bool>(player, "TryProduceNativeMagic11", magic, target),
        "skill11 admitted guard target");
    Equal(0, random.MaxValues.Count,
        "guard rejection consumed RNG");
    Equal(0, player.m_MsgList.Count,
        "guard rejection queued an effect");
}

static void VerifySkill39DelayedPush()
{
    var player = NewPlayer();
    player.m_Abil.Level = 10;
    player.m_WAbil.MC = HUtil32.MakeLong(10, 10);
    player.m_nCurrX = 10;
    player.m_nCurrY = 10;

    var target = NewTarget(race: 50, x: 12, y: 10);
    target.m_Abil.Level = 1;
    var magic = Magic(39, level: 3, trainLevel: 3,
        maximumTrain: int.MaxValue);
    var random = UseRandom(0, 10, 20, 0, 0, 30);
    int before = HUtil32.GetTickCount();
    Assert(Invoke<bool>(player, "TryProduceNativeMagic39", magic, target),
        "skill39 producer rejected valid target");
    EqualSequence(new int?[] { 100, null, null, 2, 3, 100 },
        random.MaxValues,
        "skill39 hit/power/luck/training/push RNG order");

    AssertDelay(SingleMessage(player, Grobal2.RM_NATIVE_MAGIC_EFFECT),
        before, 600, "skill39 damage delay");
    AssertDelay(SingleMessage(player, Grobal2.RM_MAGIC_LVEXP),
        before, 3000, "skill39 training delay");
    var push = SingleMessage(player, 10417);
    AssertDelay(push, before, 700, "skill39 push delay");
    Assert(push.Payload != null, "skill39 push payload missing");

    player.m_boDeath = true;
    var process = new TProcessMessage
    {
        wIdent = push.wIdent,
        wParam = push.wParam,
        nParam1 = push.nParam1,
        nParam2 = push.nParam2,
        nParam3 = push.nParam3,
        Payload = push.Payload
    };
    Assert(Invoke<bool>(player,
            "TryHandleNativeMagicProducerMessage", process),
        "message10417 was not recognized while source was dead");
    process.wIdent = 10416;
    Assert(!Invoke<bool>(player,
            "TryHandleNativeMagicProducerMessage", process),
        "non-10417 message was claimed by producer handler");
}

static void VerifyTrainingNotifications()
{
    var player = NewPlayer();
    player.m_Abil.Level = 99;
    var first = Magic(1, level: 0, trainLevel: 3,
        maximumTrain: 100);
    var second = Magic(11, level: 0, trainLevel: 3,
        maximumTrain: 10);

    int before = HUtil32.GetTickCount();
    Invoke<object>(player, "TrainNativeMagicProducer", first, 1);
    Invoke<object>(player, "TrainNativeMagicProducer", first, 2);
    var messages = MagicTrainingMessages(player);
    Equal(1, messages.Length,
        "same-skill training did not debounce");
    Equal(3, messages[0].nParam3,
        "same-skill training did not retain latest points");
    AssertDelay(messages[0], before, 3000,
        "same-skill training delay");

    int switchBefore = HUtil32.GetTickCount();
    Invoke<object>(player, "TrainNativeMagicProducer", second, 1);
    messages = MagicTrainingMessages(player);
    Equal(2, messages.Length, "skill-switch training message count");
    var flushed = messages.Single(message => message.nParam1 == 1);
    Assert(!flushed.boLateDelivery && flushed.dwDeliveryTime == 0,
        "previous skill was not flushed immediately");
    var pending = messages.Single(message => message.nParam1 == 11);
    AssertDelay(pending, switchBefore, 3000,
        "new skill debounce delay");

    int levelBefore = HUtil32.GetTickCount();
    Invoke<object>(player, "TrainNativeMagicProducer", second, 9);
    messages = MagicTrainingMessages(player);
    Equal(2, messages.Length,
        "level-up did not replace the pending snapshot");
    pending = messages.Single(message => message.nParam1 == 11);
    Equal(1, pending.nParam2, "level-up snapshot level");
    Equal(0, pending.nParam3, "level-up snapshot points");
    AssertDelay(pending, levelBefore, 800,
        "level-up replacement delay");
    Equal(1, player.RecalcCalls,
        "level-up ability recalculation count");
    Equal(1, player.m_MsgList.Count(message =>
        message.wIdent == Grobal2.RM_ABILITY),
        "level-up ability notification count");
}

static void VerifySourceContracts()
{
    string root = FindRepoRoot();
    string path = Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.NativeMagicProducers.cs");
    string source = File.ReadAllText(path);

    string mpCost = ExtractMethodBody(source,
        "GetNativeMagicProducerMpCost");
    Contains(mpCost, "wSpell / 4.0", "MP cost double quarter");
    Contains(mpCost, "magic.btLevel + 1", "MP cost raw level");
    Reject(mpCost, "btTrainLv + 1", "MP cost train level leak");

    string effective = ExtractMethodBody(source,
        "GetNativeMagicProducerEffectiveLevel");
    Contains(effective, "unchecked((byte)",
        "effective level byte wrap");
    Contains(effective, "Math.Min", "effective level clamp");

    string skillPower = ExtractMethodBody(source,
        "CalculateNativeMagicProducerSkillPower");
    int firstRandom = RequiredIndex(skillPower,
        "NextNativeMagicProducerRandom", "skill power first RNG");
    int secondRandom = skillPower.IndexOf("NextNativeMagicProducerRandom",
        firstRandom + 1, StringComparison.Ordinal);
    Assert(secondRandom > firstRandom,
        "skill power second RNG is missing");
    Contains(skillPower, "magic.btLevel + 1",
        "skill power raw level");
    Reject(skillPower, "GetNativeMagicProducerEffectiveLevel",
        "skill power used effective level");
    // Native sub_4C8658 @0x4C8696 is `fdiv dword ptr [0x4C86B8]`, and [0x4C86B8]
    // is a 4-byte float32 holding 00 00 80 40 = 4.0f. The divisor is that literal,
    // NOT (btTrainLv + 1) — btTrainLv (+0x1A) is not read anywhere in the body and
    // serves only as a level cap elsewhere in native. Pin both halves so no future
    // pass reintroduces the train-level divisor.
    // See staging/spellpower_formula_exact_20260803.md.
    Contains(skillPower, "4.0d", "skill power literal 4.0 divisor");
    Reject(skillPower, "btTrainLv", "skill power train level divisor leak");

    // Native sub_4C870C: same 4.0 divisor, but the default-power roll is drawn
    // FIRST (@0x4C8725, before the level term) and the level factor is the
    // EFFECTIVE level (sub_4C896C @0x4C872D), not the raw btLevel.
    string scaledPower = ExtractMethodBody(source,
        "CalculateNativeMagicProducerScaledPower");
    Contains(scaledPower, "4.0d", "scaled power literal 4.0 divisor");
    Contains(scaledPower, "GetNativeMagicProducerEffectiveLevel",
        "scaled power effective level");
    Reject(scaledPower, "btTrainLv", "scaled power train level divisor leak");
    Assert(scaledPower.IndexOf("NextNativeMagicProducerRandom",
               StringComparison.Ordinal) <
           RequiredIndex(scaledPower, "GetNativeMagicProducerEffectiveLevel",
               "scaled power level term"),
        "scaled power default-power roll must precede the level term");

    // Native sub_4C8764: Round(nInt * (2*effLevel + 6) / 12.0f) plus the
    // default-power term, with [0x4C87BC] = float32 12.0f (raw 00 00 40 41) and the
    // default-power roll drawn FIRST (@0x4C877D). No btTrainLv divisor and no
    // nInt/3 split exist in the native body.
    string power13 = ExtractMethodBody(source,
        "CalculateNativeMagicProducer13Power");
    Contains(power13, "12.0d", "type13 literal 12.0 divisor");
    Contains(power13, "GetNativeMagicProducerEffectiveLevel",
        "type13 effective level");
    Reject(power13, "btTrainLv", "type13 train level divisor leak");
    Reject(power13, "3.0", "type13 reintroduced the nInt/3 split");
    Assert(power13.IndexOf("NextNativeMagicProducerRandom",
               StringComparison.Ordinal) <
           RequiredIndex(power13, "GetNativeMagicProducerEffectiveLevel",
               "type13 level term"),
        "type13 default-power roll must precede the level term");

    string nextRandom = ExtractMethodBody(source,
        "NextNativeMagicProducerRandom");
    Assert(RequiredIndex(nextRandom, ".Random(range)",
               "positive-range RNG") <
           RequiredIndex(nextRandom, ".Random()",
               "zero-range RNG consumption"),
        "zero-range RNG branch order");

    string admission = ExtractMethodBody(source,
        "TryAdmitNativeMagicProducerTarget");
    int properTarget = RequiredIndex(admission, "IsProperTarget(",
        "producer proper-target gate");
    int guardTarget = RequiredIndex(admission, "RC_GUARD",
        "producer guard gate");
    int hitRoll = RequiredIndex(admission,
        "NextNativeMagicProducerRandom(100)", "producer type74 roll");
    int lineOfSight = RequiredIndex(admission, "MagCanHitTarget(",
        "producer line-of-sight gate");
    Assert(properTarget < guardTarget && guardTarget < hitRoll &&
           hitRoll < lineOfSight,
        "producer proper/guard/type74/LOS order");

    string oneOrFive = ExtractMethodBody(source,
        "TryProduceNativeMagic1Or5");
    Contains(oneOrFive,
        "TryAdmitNativeMagicProducerTarget(target, true)",
        "skills1/5 line-of-sight admission");

    foreach (string methodName in new[]
    {
        "TryProduceNativeMagic11",
        "TryProduceNativeMagic35",
        "TryProduceNativeMagic39"
    })
    {
        string body = ExtractMethodBody(source, methodName);
        Contains(body, "TryAdmitNativeMagicProducerTarget(target, false)",
            methodName + " no-LOS admission");
        Reject(body, "MagCanHitTarget(",
            methodName + " unexpectedly gained direct LOS");
    }

    string eleven = ExtractMethodBody(source, "TryProduceNativeMagic11");
    Contains(eleven, "rawDamage * 2", "skill11 human-kind multiplier");
    Contains(eleven, "1.5", "skill11 undead multiplier");

    string thirtyFive = ExtractMethodBody(source,
        "TryProduceNativeMagic35");
    Contains(thirtyFive, "/ 4", "skill35 level4 second roll");
    Contains(thirtyFive, "1.25", "skill35 player multiplier");
    Contains(thirtyFive, "1.1", "skill35 level5 multiplier");
    Contains(thirtyFive, "1.2", "skill35 level6 multiplier");

    string thirtyNine = ExtractMethodBody(source,
        "TryProduceNativeMagic39");
    int queue = RequiredIndex(thirtyNine,
        "QueueNativeMagicProducerEffect", "skill39 damage queue");
    int train = RequiredIndex(thirtyNine,
        "TrainNativeMagicProducer", "skill39 training");
    int pushChance = thirtyNine.LastIndexOf(
        "NextNativeMagicProducerRandom(100)",
        StringComparison.Ordinal);
    Assert(queue < train && train < pushChance,
        "skill39 queue/training/push RNG order");

    Contains(source, "QueueNativeMagicEffect(",
        "common native magic queue call");
    Contains(source, "MagicDamageContext.Capture(magic)",
        "queue damage context snapshot");
    Contains(source, "600", "native magic effect delay");
    Contains(source, "10417", "native delayed push ident");
    Contains(source, "700", "native delayed push delay");

    string training = ExtractMethodBody(source,
        "TrainNativeMagicProducer");
    Contains(training, "m_boFastTrain", "fast training gate");
    Contains(training, "while", "multi-level training loop");
    Contains(training, "3000", "training debounce delay");
    Contains(training, "800", "level-up replacement delay");

    string handler = ExtractMethodBody(source,
        "TryHandleNativeMagicProducerMessage");
    Contains(handler, "m_boDeath", "delayed push source death gate");
    Contains(handler, "m_boGhost", "delayed push target ghost gate");
    Contains(handler, "CharPushed", "delayed push delivery");

    foreach (string forbidden in new[]
    {
        "GetAttackPower(",
        "m_nPowerRate",
        "m_boPowerItem",
        "m_boAutoChangeColor",
        "m_boFixColor"
    })
    {
        Reject(source, forbidden,
            "non-native attack modifier leaked into producer: " + forbidden);
    }
}

static void VerifyWiringContracts()
{
    string root = FindRepoRoot();
    string playerSource = File.ReadAllText(Path.Combine(root, "GameSvr",
        "Players", "TPlayObject.cs"));
    string managerSource = File.ReadAllText(Path.Combine(root, "GameSvr",
        "Spells", "MagicManager.cs"));
    string messageSource = File.ReadAllText(Path.Combine(root, "GameSvr",
        "Players", "TPlayObject.Message.cs"));

    string getSpellPoint = ExtractMethodBody(playerSource, "GetSpellPoint");
    Contains(getSpellPoint, "return GetNativeMagicProducerMpCost(UserMagic);",
        "GetSpellPoint native MP delegation");
    // Native sub_4C8888 is the ONLY MP-cost producer, so every C# MP-cost site must
    // delegate to the single byte-audited helper rather than restate the formula.
    // TPlayObject.cs legitimately holds TWO delegations:
    //   1) GetSpellPoint            (the player cast path)
    //   2) EncodeClientMagic.NeedMp (native encoder fn 0x4C8498 writes sub_4C8888's
    //      return straight into the packet: @0x4C850C call / @0x4C8511
    //      mov word ptr [ebx+0x18],ax)
    // Raised from 1 to 2 when the NeedMp site was migrated off its own
    // `(btTrainLv + 1)` copy of the formula. The assertion still bites: it pins the
    // count exactly, so a third restatement or a removed delegation both fail. The
    // real anti-duplication guard is the Reject(...HUtil32.Round) pair below plus the
    // whole-file rejection of the legacy divisor.
    Equal(2, Count(playerSource, "GetNativeMagicProducerMpCost("),
        "native MP helper call count in TPlayObject.cs");
    Reject(getSpellPoint, "btTrainLv", "GetSpellPoint legacy train divisor");
    Reject(getSpellPoint, "HUtil32.Round", "GetSpellPoint duplicate formula");
    // No site in TPlayObject.cs may reconstruct the MP cost by hand any more.
    Reject(playerSource, "magic.btTrainLv + 1",
        "TPlayObject legacy MP divisor");
    // Same for the two other former restatements: the shared TBaseObject.GetMagicSpell
    // (native: the three 半月/烈火半月/圆月 branches @0x6BC832/0x6BC8AE/0x6BC96F all call
    // sub_4C8888 and use AX directly) and HeroObject.GetHeroSpellPoint.
    string baseObjectSource = File.ReadAllText(Path.Combine(root, "GameSvr",
        "Actors", "TBaseObject.cs"));
    string getMagicSpell = ExtractMethodBody(baseObjectSource, "GetMagicSpell");
    Contains(getMagicSpell,
        "TPlayObject.GetNativeMagicProducerMpCost(UserMagic)",
        "GetMagicSpell native MP delegation");
    Reject(getMagicSpell, "btTrainLv", "GetMagicSpell legacy train divisor");
    Reject(getMagicSpell, "HUtil32.Round", "GetMagicSpell duplicate formula");
    // Native folds btDefSpell in at @0x4C88BA/@0x4C88BD, so the AttackDir callers must
    // NOT add it again on top of GetMagicSpell.
    string attackSource = File.ReadAllText(Path.Combine(root, "GameSvr",
        "Actors", "TBaseObject.Attack.cs"));
    Reject(attackSource, "btDefSpell + GetMagicSpell",
        "AttackDir double-adds btDefSpell over sub_4C8888");
    string heroSource = File.ReadAllText(Path.Combine(root, "GameSvr",
        "Actors", "HeroObject.cs"));
    string heroSpellPoint = ExtractMethodBody(heroSource, "GetHeroSpellPoint");
    Contains(heroSpellPoint,
        "TPlayObject.GetNativeMagicProducerMpCost(userMagic)",
        "GetHeroSpellPoint native MP delegation");
    Reject(heroSpellPoint, "btTrainLv", "GetHeroSpellPoint legacy train divisor");
    Reject(heroSpellPoint, "HUtil32.Round", "GetHeroSpellPoint duplicate formula");

    string doSpell = ExtractMethodBody(managerSource, "DoSpell");
    string magicSwitch = ExtractControlBlock(doSpell,
        "switch (UserMagic.MagicInfo.wMagicID)",
        "magic dispatcher switch");
    string oneAndFive = ExtractCaseRange(magicSwitch,
        "SpellsDef.SKILL_FIREBALL", "SpellsDef.SKILL_HEALLING");
    string eleven = ExtractCaseRange(magicSwitch,
        "SpellsDef.SKILL_LIGHTENING", "SpellsDef.SKILL_FIRECHARM");
    string thirtyFive = ExtractCaseRange(magicSwitch,
        "SpellsDef.SKILL_WINDTEBO", "SpellsDef.SKILL_MABE");
    string thirtyNine = ExtractCaseRange(magicSwitch,
        "SpellsDef.SKILL_GROUPDEDING", "SpellsDef.SKILL_43");

    Contains(oneAndFive, "case SpellsDef.SKILL_FIREBALL:",
        "skill1 dispatcher label");
    Contains(oneAndFive, "case SpellsDef.SKILL_FIREBALL2:",
        "skill5 dispatcher label");
    Equal(1, Count(oneAndFive, "TryProduceNativeMagic1Or5("),
        "skills1/5 producer call count");
    Reject(oneAndFive, "TargeTBaseObject = null",
        "skills1/5 producer failure cleared target");

    VerifyNullingProducerBranch(eleven,
        "TryProduceNativeMagic11", "skill11");
    VerifyNullingProducerBranch(thirtyFive,
        "TryProduceNativeMagic35", "skill35");

    Equal(1, Count(thirtyNine, "TryProduceNativeMagic39("),
        "skill39 producer call count");
    Assert(Regex.IsMatch(thirtyNine,
            @"(?m)^\s*PlayObject\.TryProduceNativeMagic39\("),
        "skill39 producer return was not ignored");
    Reject(thirtyNine, "!PlayObject.TryProduceNativeMagic39",
        "skill39 producer return controls dispatch");
    Reject(thirtyNine, "TargeTBaseObject = null",
        "skill39 producer failure cleared target");

    foreach (var branch in new[]
    {
        (Name: "skills1/5", Source: oneAndFive),
        (Name: "skill11", Source: eleven),
        (Name: "skill35", Source: thirtyFive),
        (Name: "skill39", Source: thirtyNine)
    })
    {
        foreach (string forbidden in new[]
        {
            "boTrain",
            "MagWindTebo(",
            "MagGroupDeDing(",
            "HealthSpellChanged(",
            "SendRefMsg(",
            "SendDelayMsg(",
            "RM_DELAYMAGIC",
            "RM_MAGICFIRE",
            "boSpellFire =",
            "boSpellFail ="
        })
        {
            Reject(branch.Source, forbidden,
                branch.Name + " retained forbidden legacy wiring: " +
                forbidden);
        }
    }

    string operate = ExtractMethodBody(messageSource, "Operate");
    string messageSwitch = ExtractControlBlock(operate,
        "switch (ProcessMsg.wIdent)", "player message switch");
    string pushCase = ExtractCaseRange(messageSwitch,
        "NativeMagicProducerPushIdent", "Grobal2.RM_LINGFU_CHANGED");
    Equal(1, Count(messageSource,
        "case NativeMagicProducerPushIdent:"),
        "message10417 case count");
    Equal(1, Count(messageSource,
        "TryHandleNativeMagicProducerMessage(ProcessMsg)"),
        "message10417 handler call count");
    Contains(pushCase, "TryHandleNativeMagicProducerMessage(ProcessMsg);",
        "message10417 handler delegation");
    Contains(pushCase, "break;", "message10417 case break");
}

static void VerifyNullingProducerBranch(string branch, string producer,
    string label)
{
    string call = "!PlayObject." + producer + "(";
    int callIndex = RequiredIndex(branch, call,
        label + " negated producer call");
    int clearIndex = RequiredIndex(branch, "TargeTBaseObject = null;",
        label + " failure target clear");
    Assert(callIndex < clearIndex,
        label + " target clear precedes producer failure");
    Equal(1, Count(branch, producer + "("),
        label + " producer call count");
    Equal(1, Count(branch, "TargeTBaseObject = null;"),
        label + " target clear count");
}

static ProducerPlayerProbe NewPlayer()
{
    var player = (ProducerPlayerProbe)RuntimeHelpers.GetUninitializedObject(
        typeof(ProducerPlayerProbe));
    player.m_btRaceServer = Grobal2.RC_PLAYOBJECT;
    player.m_Abil = new TAbility();
    player.m_WAbil = new TAbility();
    player.m_MsgList = new List<SendMessage>();
    player.m_MagicList = new List<TUserMagic>();
    return player;
}

static TBaseObject NewTarget(byte race, short x, short y)
{
    var target = (TBaseObject)RuntimeHelpers.GetUninitializedObject(
        typeof(TBaseObject));
    target.m_btRaceServer = race;
    target.m_nCurrX = x;
    target.m_nCurrY = y;
    target.m_Abil = new TAbility();
    target.m_WAbil = new TAbility();
    target.m_MsgList = new List<SendMessage>();
    return target;
}

static TUserMagic Magic(ushort id, byte level,
    byte trainLevel = 3, int maximumTrain = 1000)
{
    return new TUserMagic
    {
        wMagIdx = id,
        btLevel = level,
        MagicInfo = new TMagic
        {
            wMagicID = id,
            btTrainLv = trainLevel,
            TrainLevel = new byte[] { 0, 0, 0, 0 },
            MaxTrain = new[]
            {
                maximumTrain, maximumTrain, maximumTrain, maximumTrain
            }
        }
    };
}

static void SetMagicLevelBonus(TUserMagic magic, byte bonus)
{
    var field = typeof(TUserMagic).GetField("NativeLevelBonus",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(TUserMagic).FullName,
            "NativeLevelBonus");
    field.SetValue(magic, bonus);
}

static T Invoke<T>(object target, string name, params object[] arguments)
{
    var types = arguments.Select(argument => argument.GetType()).ToArray();
    var method = typeof(TPlayObject).GetMethod(name,
        BindingFlags.Instance | BindingFlags.Static |
        BindingFlags.Public | BindingFlags.NonPublic,
        binder: null, types: types, modifiers: null)
        ?? throw new MissingMethodException(typeof(TPlayObject).FullName, name);
    try
    {
        object result = method.Invoke(method.IsStatic ? null : target,
            arguments);
        if (typeof(T) == typeof(object)) return (T)result;
        return (T)(result ?? throw new InvalidOperationException(
            name + " returned null"));
    }
    catch (TargetInvocationException exception)
        when (exception.InnerException != null)
    {
        throw exception.InnerException;
    }
}

static SendMessage SingleMessage(TBaseObject actor, int ident)
{
    return actor.m_MsgList.Single(message => message.wIdent == ident);
}

static SendMessage[] MagicTrainingMessages(TBaseObject actor)
{
    return actor.m_MsgList
        .Where(message => message.wIdent == Grobal2.RM_MAGIC_LVEXP)
        .ToArray();
}

static void EqualPayload(object payload, string name, object expected)
{
    Assert(payload != null, "native magic payload missing");
    var property = payload.GetType().GetProperty(name,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new MissingMemberException(payload.GetType().FullName, name);
    Equal(expected, property.GetValue(payload),
        "native magic payload " + name);
}

static void AssertDelay(SendMessage message, int before, int expected,
    string label)
{
    Assert(message.boLateDelivery, label + " was not delayed");
    int delay = unchecked(message.dwDeliveryTime - before);
    Assert(delay >= expected && delay < expected + 250,
        $"{label}: expected {expected}ms, actual {delay}ms");
}

// The recorder rides M2Share.RandomNumber, the field the server assigns at
// startup. It used to ride RandomNumber's private `random` field, which
// POIS-26 removed when the facade moved onto the Delphi LCG sub_403B4C
// (@0x403B4C imul [0x7A2008],0x08088405 / inc / mul / take EDX); GetField then
// returned null and the tool's own catch reported that MissingFieldException
// as its failure, so none of the producer draw ledgers were being evaluated.
// The expected values, bounds and ordinals below are unchanged.
static FixedRandom UseRandom(params int[] values)
{
    var random = new FixedRandom(values);
    M2Share.RandomNumber = random;
    return random;
}

static string ExtractMethodBody(string source, string methodName)
{
    var declaration = Regex.Match(source,
        @"(?:public|private|protected|internal)\s+" +
        @"(?:(?:static|virtual|override|sealed|async|new)\s+)*" +
        @"[A-Za-z_][A-Za-z0-9_<>,\[\]?\.]*\s+" +
        Regex.Escape(methodName) + @"\s*\(",
        RegexOptions.CultureInvariant);
    Assert(declaration.Success,
        "source method was not found: " + methodName);
    int open = source.IndexOf('{',
        declaration.Index + declaration.Length);
    Assert(open >= 0, "source method has no body: " + methodName);
    return ExtractBraceBlock(source, open,
        "source method body: " + methodName);
}

static string ExtractControlBlock(string source, string marker,
    string label)
{
    int markerIndex = RequiredIndex(source, marker, label);
    int open = source.IndexOf('{', markerIndex);
    Assert(open >= 0, label + " opening brace is missing");
    return ExtractBraceBlock(source, open, label);
}

static string ExtractBraceBlock(string source, int open, string label)
{
    int depth = 0;
    for (int index = open; index < source.Length; index++)
    {
        if (source[index] == '{') depth++;
        else if (source[index] == '}' && --depth == 0)
            return source.Substring(open, index - open + 1);
    }
    throw new InvalidOperationException(label + " is incomplete");
}

static string ExtractCaseRange(string switchBody, string firstCase,
    string nextCase)
{
    string firstMarker = "case " + firstCase + ":";
    string nextMarker = "case " + nextCase + ":";
    int start = RequiredIndex(switchBody, firstMarker,
        firstCase + " case");
    int end = switchBody.IndexOf(nextMarker, start + firstMarker.Length,
        StringComparison.Ordinal);
    Assert(end > start, nextCase + " following case is missing");
    return switchBody.Substring(start, end - start);
}

static int RequiredIndex(string source, string value, string label)
{
    int index = source.IndexOf(value, StringComparison.Ordinal);
    if (index < 0) throw new InvalidOperationException(label + " is missing");
    return index;
}

static int Count(string source, string value)
{
    int count = 0;
    int index = 0;
    while ((index = source.IndexOf(value, index,
        StringComparison.Ordinal)) >= 0)
    {
        count++;
        index += value.Length;
    }
    return count;
}

static string FindRepoRoot()
{
    foreach (string start in new[]
    {
        Environment.CurrentDirectory,
        AppContext.BaseDirectory
    })
    {
        var directory = new DirectoryInfo(start);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName,
                    "GameSvr", "GameSvr.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
    }
    throw new DirectoryNotFoundException("GameSvr/GameSvr.csproj");
}

static void InitializeRuntime()
{
    M2Share.g_Config = new GameSvrConfig();
    M2Share.UserEngine = new UserEngine();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.MapManager = new MapManager();
    M2Share.RandomNumber = RandomNumber.GetInstance();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new System.Collections.ArrayList();
}

static void PrepareRuntimeConfig()
{
    string runtime = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(runtime, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtime, "String.ini"),
        "[String]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtime, "Command.conf"),
        "[Command]" + Environment.NewLine);
    string share = Path.Combine(Path.GetFullPath(
        Path.Combine(runtime, "..")), "Share");
    Directory.CreateDirectory(share);
    File.WriteAllText(Path.Combine(share, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(share, "ServerData.ini"),
        "[Integer]" + Environment.NewLine);
}

static void Contains(string source, string value, string label) =>
    Assert(source.Contains(value, StringComparison.Ordinal),
        label + " is missing");

static void Reject(string source, string value, string label) =>
    Assert(!source.Contains(value, StringComparison.Ordinal), label);

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{label}: expected={expected}, actual={actual}");
}

static void EqualSequence<T>(IReadOnlyList<T> expected,
    IReadOnlyList<T> actual, string label)
{
    if (!expected.SequenceEqual(actual))
        throw new InvalidOperationException(
            $"{label}: expected=[{string.Join(',', expected)}], " +
            $"actual=[{string.Join(',', actual)}]");
}

static void Assert(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
}

sealed class ProducerPlayerProbe : TPlayObject
{
    internal int RecalcCalls { get; private set; }

    public override bool IsProperTarget(TBaseObject target) => target != null;

    public override void RecalcAbilitys()
    {
        RecalcCalls++;
    }
}

sealed class FixedRandom : RandomNumber
{
    private readonly Queue<int> _values;

    internal FixedRandom(IEnumerable<int> values)
    {
        _values = new Queue<int>(values);
    }

    internal List<int?> MaxValues { get; } = new();

    // NextNativeMagicProducerRandom(0) takes the deliberate seed advance at
    // TPlayObject.NativeMagicProducers.cs `_ = M2Share.RandomNumber.Random()`,
    // which is the original Random(0) (sub_403B4C still steps RandSeed and
    // returns 0). A null ordinal marks it apart from a bounded draw.
    public override int Random()
    {
        MaxValues.Add(null);
        return Dequeue();
    }

    public override int Random(int Value)
    {
        MaxValues.Add(Value);
        int value = Dequeue();
        if (Value <= 0 || value < 0 || value >= Value)
            throw new ArgumentOutOfRangeException(nameof(value));
        return value;
    }

    // The producers reach neither the min/max draw nor the inclusive
    // GetRandomNumber entry; either arriving here is an unrecorded call.
    public override int Random(int minValue, int maxValue) =>
        throw new InvalidOperationException("unexpected Random(min,max) draw");

    public override int GetRandomNumber(int minValue, int maxValue) =>
        throw new InvalidOperationException("unexpected GetRandomNumber draw");

    private int Dequeue()
    {
        if (_values.Count == 0)
            throw new InvalidOperationException("unexpected RNG call");
        return _values.Dequeue();
    }
}
