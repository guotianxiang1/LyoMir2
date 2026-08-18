using System.Reflection;
using GameSvr;
using SystemModule;

PrepareRuntimeConfig();
InitializeRuntime();

CheckConstantAndTimingBoundaries();
CheckState45ProducerGate();
CheckRobotUsesNativeProducerGate();
CheckEligibilityMatrix();
CheckProductionStartAndMpTransaction();
CheckEffectiveLevelControlsSequence();
CheckTrainingLevelBoundary();
CheckNativeTrainingNotificationBatching();
CheckFixedStepTables();
CheckDedicatedDamageResolver();
CheckFirstCollisionDamageOccursOnce();
CheckLevelThreeSecondActorPush();
CheckFailureClearsSequence();

Console.WriteLine(
    "NativeMotaeboForcedMoveCompatCheck PASS skill27=4500/500+MP " +
    "steps=3/5 immediate+12309/250ms decrement-first failure-clear " +
    "effective-level+training<= dedicated-damage=AC/state17/state7/state20 " +
    "VMT-B8=level/stick/death/ghost/admin/stone/map/state52/race/target " +
    "collision=first-only second-actor=level3 " +
    "training=3s-pending+switch-flush+10123-fields");

static void CheckConstantAndTimingBoundaries()
{
    Equal(12309, Grobal2.RM_NATIVE_MOOTEBO_CONTINUE, "12309 constant");
    const int now = 100000;
    Assert(!TPlayObject.IsNativeMotaeboTimingReady(now,
        now - 4500, now - 501), "4500 boundary rejected");
    Assert(!TPlayObject.IsNativeMotaeboTimingReady(now,
        now - 4501, now - 500), "500 boundary rejected");
    Assert(TPlayObject.IsNativeMotaeboTimingReady(now,
        now - 4501, now - 501), "4501/501 accepted");

    // 0x6BC94C `2B 46 6C / 3D F4 01 00 00 / 0F 86 jbe` is unsigned.
    // now=10, lastWalk=20: unsigned elapsed wraps past 500 and must pass;
    // a signed subtract would yield -10 and wrongly refuse.
    Assert(TPlayObject.IsNativeMotaeboTimingReady(10, 10 - 4501, 20),
        "unsigned wrap: last walk tick 20, now 10 must pass 500 ms");
}

static void CheckState45ProducerGate()
{
    var player = Place(NewMap(), NewPlayer("producer-state45"), 5, 5);
    var magic = Magic(0, 10);
    player.m_WAbil.MP = 20;
    player.m_WAbil.MaxMP = 20;
    int now = HUtil32.GetTickCount();
    int oldTick = unchecked(now - 10000);
    player.m_dwDoMotaeboTick = oldTick;
        player.m_dwActionTick = unchecked(now - 10000);
    Assert(player.SetNativeActiveState(45), "state45 setup");

    Assert(!player.TryStartNativeMotaeboForcedMove(magic,
        Grobal2.DR_RIGHT), "state45 producer rejected");
    Equal(oldTick, player.m_dwDoMotaeboTick,
        "state45 rejected before cooldown write");
    Equal((ushort)20, player.m_WAbil.MP, "state45 MP preserved");
    Equal(0, player.m_nNativeForcedMoveRemaining,
        "state45 no sequence");
}

static void CheckRobotUsesNativeProducerGate()
{
    var map = NewMap();
    var robot = Place(map, new RobotPlayObject
    {
        m_boOffLineFlag = true,
        m_sCharName = "robot-producer",
        m_btRaceServer = Grobal2.RC_PLAYOBJECT,
        m_btAttatckMode = M2Share.HAM_ALL,
        m_boCanSpell = true
    }, 5, 5);
    var target = Place(map, NewActor("robot-target"), 6, 5);
    robot.m_Abil.Level = 20;
    target.m_Abil.Level = 1;
    robot.m_WAbil.MP = 20;
    robot.m_WAbil.MaxMP = 20;
    var magic = Magic(0, 10);
    magic.MagicInfo.sMagicName = "Motaebo";
    int now = HUtil32.GetTickCount();
    int oldTick = unchecked(now - 4000);
    robot.m_dwDoMotaeboTick = oldTick;
    robot.m_dwActionTick = unchecked(now - 10000);

    var useSpell = typeof(RobotPlayObject).GetMethod("UseSpell",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("RobotPlayObject.UseSpell");
    Assert((bool)useSpell.Invoke(robot,
        new object[] { magic, (short)6, (short)5, target }),
        "robot skill27 dispatch");
    Equal(oldTick, robot.m_dwDoMotaeboTick,
        "robot obeys 4500 cooldown");
    Equal((ushort)20, robot.m_WAbil.MP, "robot cooldown preserves MP");
    Equal(0, robot.m_nNativeForcedMoveRemaining,
        "robot cooldown no sequence");
}

static void CheckEligibilityMatrix()
{
    var map = NewMap();
    var player = Place(map, NewPlayer("eligibility-player"), 5, 5);
    var target = Place(map, NewActor("eligibility-target"), 6, 5);
    player.m_Abil.Level = 20;
    target.m_Abil.Level = 19;
    Assert(player.CanNativeMotaeboPush(target, 0),
        "eligible lower-level target");

    target.m_Abil.Level = 20;
    Assert(!player.CanNativeMotaeboPush(target, 0),
        "equal-level target rejected");
    target.m_Abil.Level = 19;
    target.m_boStickMode = true;
    Assert(!player.CanNativeMotaeboPush(target, 0),
        "stick target rejected");
    target.m_boStickMode = false;
    target.m_boDeath = true;
    Assert(!player.CanNativeMotaeboPush(target, 0),
        "dead target rejected");
    target.m_boDeath = false;
    target.m_boGhost = true;
    Assert(!player.CanNativeMotaeboPush(target, 0),
        "ghost target rejected");
    target.m_boGhost = false;
    target.m_boAdminMode = true;
    Assert(!player.CanNativeMotaeboPush(target, 0),
        "admin target rejected");
    target.m_boAdminMode = false;
    target.m_boStoneMode = true;
    Assert(!player.CanNativeMotaeboPush(target, 0),
        "stone target rejected");
    target.m_boStoneMode = false;
    Assert(target.SetNativeActiveState(52), "state52 setup");
    Assert(!player.CanNativeMotaeboPush(target, 0),
        "state52 target rejected");
    Assert(target.ClearNativeActiveState(52), "state52 clear");
    target.m_btRaceServer = 240;
    Assert(!player.CanNativeMotaeboPush(target, 0),
        "special race target rejected");
}

static void CheckProductionStartAndMpTransaction()
{
    var map = NewMap();
    var player = Place(map, NewPlayer("producer-success"), 5, 5);
    var magic = Magic(0, 10);
    player.m_Abil.Level = 20;
    player.m_WAbil.MP = 20;
    player.m_WAbil.MaxMP = 20;
    int now = HUtil32.GetTickCount();
    player.m_dwDoMotaeboTick = unchecked(now - 10000);
        player.m_dwActionTick = unchecked(now - 10000);

    Assert(player.TryStartNativeMotaeboForcedMove(magic,
        Grobal2.DR_RIGHT), "producer accepted");
    Equal((ushort)10, player.m_WAbil.MP, "producer MP consumed");
    Position(player, 6, 5, "producer immediate step");
    Equal(2, player.m_nNativeForcedMoveRemaining,
        "producer remaining after immediate step");
    CheckContinuation(player, magic, Grobal2.DR_RIGHT, 0,
        "producer continuation");

    var denied = Place(NewMap(), NewPlayer("producer-no-mp"), 5, 5);
    denied.m_WAbil.MP = 9;
    denied.m_WAbil.MaxMP = 9;
    now = HUtil32.GetTickCount();
    denied.m_dwDoMotaeboTick = unchecked(now - 10000);
    denied.m_dwActionTick = unchecked(now - 10000);
    Assert(!denied.TryStartNativeMotaeboForcedMove(magic,
        Grobal2.DR_LEFT), "insufficient MP rejected");
    Equal((ushort)9, denied.m_WAbil.MP, "insufficient MP preserved");
    Equal((byte)Grobal2.DR_LEFT, denied.m_btDirection,
        "direction stored before MP gate");
    Equal(0, denied.m_nNativeForcedMoveRemaining,
        "insufficient MP no sequence");
}

static void CheckEffectiveLevelControlsSequence()
{
    var promoted = Place(NewMap(), NewPlayer("effective-promoted"), 3, 5);
    var promotedMagic = Magic(2);
    promotedMagic.NativeLevelBonus = 1;
    promoted.m_WAbil.MP = 20;
    promoted.m_WAbil.MaxMP = 20;
    int now = HUtil32.GetTickCount();
    promoted.m_dwDoMotaeboTick = unchecked(now - 10000);
    promoted.m_dwActionTick = unchecked(now - 10000);

    Assert(promoted.TryStartNativeMotaeboForcedMove(promotedMagic,
        Grobal2.DR_RIGHT), "effective level promoted start");
    Equal(4, promoted.m_nNativeForcedMoveRemaining,
        "effective level 3 selects five steps");
    CheckContinuation(promoted, promotedMagic, Grobal2.DR_RIGHT, 3,
        "effective level promoted continuation");

    var capped = Place(NewMap(), NewPlayer("effective-capped"), 3, 5);
    var cappedMagic = Magic(3);
    cappedMagic.MagicInfo.btTrainLv = 2;
    capped.m_WAbil.MP = 20;
    capped.m_WAbil.MaxMP = 20;
    now = HUtil32.GetTickCount();
    capped.m_dwDoMotaeboTick = unchecked(now - 10000);
    capped.m_dwActionTick = unchecked(now - 10000);

    Assert(capped.TryStartNativeMotaeboForcedMove(cappedMagic,
        Grobal2.DR_RIGHT), "effective level capped start");
    Equal(2, capped.m_nNativeForcedMoveRemaining,
        "effective level 2 selects three steps");
    CheckContinuation(capped, cappedMagic, Grobal2.DR_RIGHT, 2,
        "effective level capped continuation");
}

static void CheckTrainingLevelBoundary()
{
    var player = NewPlayer("training-boundary");
    player.m_Abil.Level = 20;
    var magic = Magic(0);
    magic.MagicInfo.TrainLevel[0] = 20;
    Assert(player.CanTrainNativeMotaebo(magic),
        "training equal level accepted");

    magic.MagicInfo.TrainLevel[0] = 21;
    Assert(!player.CanTrainNativeMotaebo(magic),
        "training level above player rejected");
    magic.btLevel = 3;
    magic.MagicInfo.TrainLevel[3] = 0;
    Assert(!player.CanTrainNativeMotaebo(magic),
        "training maximum magic level rejected");
}

static void CheckNativeTrainingNotificationBatching()
{
    const int now = 100000;
    var player = NewPlayer("training-notification");
    player.m_Abil.Level = 20;
    player.m_WAbil.HP = 100;
    player.m_WAbil.MaxHP = 100;
    var magic = Magic(0);
    magic.NativeLevelBonus = 1;
    magic.MagicInfo.TrainLevel[0] = 20;
    magic.MagicInfo.MaxTrain[0] = 100;

    Assert(player.TrainNativeMotaeboMagic(magic, 1, now),
        "training notification accepted");
    Equal(0, MagicProgressMessages(player).Length,
        "training notification initially pending");
    player.RunNativeMagicTraining(now + 2999);
    Equal(0, MagicProgressMessages(player).Length,
        "training notification 2999ms pending");
    player.RunNativeMagicTraining(now + 3000);
    var progress = SingleMagicProgress(player);
    Assert(!progress.boLateDelivery,
        "training notification is immediate runtime message");
    Equal(SpellsDef.SKILL_MOOTEBO, progress.wParam,
        "training notification wParam magic id");
    Equal(1, progress.nParam1,
        "training notification effective level");
    Equal(1, progress.nParam2,
        "training notification points");
    Equal(100, progress.nParam3,
        "training notification required points body");

    Assert(player.Operate(ToProcess(progress)),
        "training notification runtime dispatch");
    Equal(Grobal2.SM_MAGIC_LVEXP, player.m_DefMsg.Ident,
        "training notification client ident");
    Equal(SpellsDef.SKILL_MOOTEBO, player.m_DefMsg.Recog,
        "training notification client recog");
    Equal((ushort)1, player.m_DefMsg.Param,
        "training notification client level");
    Equal((ushort)1, player.m_DefMsg.Tag,
        "training notification client points low");
    Equal((ushort)0, player.m_DefMsg.Series,
        "training notification client points high");

    var debounce = NewPlayer("training-debounce");
    debounce.m_Abil.Level = 20;
    debounce.m_WAbil.HP = 100;
    var debounceMagic = Magic(0);
    debounceMagic.MagicInfo.MaxTrain[0] = 100;
    Assert(debounce.TrainNativeMotaeboMagic(debounceMagic, 1, now),
        "training debounce first award");
    Assert(debounce.TrainNativeMotaeboMagic(debounceMagic, 1,
        now + 2000), "training debounce second award");
    debounce.RunNativeMagicTraining(now + 3000);
    Equal(0, MagicProgressMessages(debounce).Length,
        "training debounce resets three-second tick");
    debounce.RunNativeMagicTraining(now + 5000);
    progress = SingleMagicProgress(debounce);
    Equal(2, progress.nParam2, "training debounce merged points");

    var multilevel = NewPlayer("training-multilevel");
    multilevel.m_Abil.Level = 20;
    multilevel.m_WAbil.HP = 100;
    var multilevelMagic = Magic(0);
    multilevelMagic.NativeLevelBonus = 1;
    multilevelMagic.MagicInfo.MaxTrain[0] = 2;
    multilevelMagic.MagicInfo.MaxTrain[1] = 2;
    multilevelMagic.MagicInfo.MaxTrain[2] = 100;
    Assert(multilevel.TrainNativeMotaeboMagic(multilevelMagic, 5, now),
        "training multilevel award");
    Equal((byte)2, multilevelMagic.btLevel,
        "training multilevel level");
    Equal(1, multilevelMagic.nTranPoint,
        "training multilevel remainder");
    Equal(1, multilevel.m_MsgList.Count(message =>
            message.wIdent == Grobal2.RM_ABILITY),
        "training multilevel ability snapshot");
    multilevel.RunNativeMagicTraining(now + 3000);
    progress = SingleMagicProgress(multilevel);
    Equal(3, progress.nParam1,
        "training multilevel effective level cap");
    Equal(100, progress.nParam3,
        "training multilevel next requirement");

    var fast = NewPlayer("training-fast");
    fast.m_Abil.Level = 20;
    fast.m_WAbil.HP = 100;
    fast.m_boFastTrain = true;
    var fastMagic = Magic(0);
    fastMagic.MagicInfo.MaxTrain[0] = 100;
    Assert(fast.TrainNativeMotaeboMagic(fastMagic, 1, now),
        "training fast award");
    Equal(3, fastMagic.nTranPoint, "training fast triples points");

    var switched = NewPlayer("training-switch");
    switched.m_Abil.Level = 20;
    switched.m_WAbil.HP = 100;
    var firstMagic = Magic(0);
    firstMagic.MagicInfo.MaxTrain[0] = 100;
    var secondMagic = Magic(0);
    secondMagic.MagicInfo.wMagicID = 28;
    secondMagic.MagicInfo.MaxTrain[0] = 200;
    Assert(switched.TrainNativeMotaeboMagic(firstMagic, 1, now),
        "training switch first pending");
    Assert(switched.TrainNativeMotaeboMagic(secondMagic, 2, now + 1),
        "training switch second pending");
    progress = SingleMagicProgress(switched);
    Equal(SpellsDef.SKILL_MOOTEBO, progress.wParam,
        "training switch flushes old magic id");
    Equal(1, progress.nParam2,
        "training switch flushes old magic points");
    Assert(ReferenceEquals(secondMagic,
            switched.m_NativeMagicTrainingPending),
        "training switch retains new pending magic");

    var ineligible = NewPlayer("training-ineligible");
    ineligible.m_Abil.Level = 19;
    ineligible.m_WAbil.HP = 100;
    var ineligibleMagic = Magic(0);
    ineligibleMagic.MagicInfo.TrainLevel[0] = 20;
    Assert(!ineligible.TrainNativeMotaeboMagic(ineligibleMagic, 3, now),
        "training ineligible rejected");
    Equal(0, ineligibleMagic.nTranPoint,
        "training ineligible preserves points");
    Assert(ineligible.m_NativeMagicTrainingPending == null,
        "training ineligible creates no pending notification");
}

static SendMessage[] MagicProgressMessages(TPlayObject player) =>
    player.m_MsgList.Where(message =>
        message.wIdent == Grobal2.RM_MAGIC_LVEXP).ToArray();

static SendMessage SingleMagicProgress(TPlayObject player)
{
    var messages = MagicProgressMessages(player);
    Equal(1, messages.Length, "single training notification");
    return messages[0];
}

static void CheckFixedStepTables()
{
    foreach (var item in new[]
             {
                 (Level: 0, Steps: 3), (Level: 2, Steps: 3),
                 (Level: 3, Steps: 5), (Level: 4, Steps: 5)
             })
    {
        var player = Place(NewMap(),
            NewPlayer("steps-" + item.Level), 3, 5);
        var magic = Magic(item.Level);
        Assert(player.StartNativeMotaeboForcedMoveStep(
            Grobal2.DR_RIGHT, item.Level, magic),
            $"level {item.Level} immediate step");
        Equal(item.Steps - 1, player.m_nNativeForcedMoveRemaining,
            $"level {item.Level} immediate decrement");

        int moves = 1;
        while (player.m_nNativeForcedMoveRemaining > 0)
        {
            var queued = CheckContinuation(player, magic,
                Grobal2.DR_RIGHT, item.Level,
                $"level {item.Level} continuation {moves}");
            player.m_MsgList.Remove(queued);
            Assert(player.Operate(ToProcess(queued)),
                $"level {item.Level} continuation dispatch {moves}");
            moves++;
        }

        Equal(item.Steps, moves, $"level {item.Level} total steps");
        Position(player, 3 + item.Steps, 5,
            $"level {item.Level} final position");
        Equal(0, CountContinuations(player),
            $"level {item.Level} final queue empty");
    }
}

static void CheckDedicatedDamageResolver()
{
    var armored = NewActor("damage-armored");
    armored.m_WAbil.AC = HUtil32.MakeLong(10, 10);
    Equal(5, armored.ResolveNativeMotaeboDamage(15),
        "damage subtracts fixed AC");
    Equal(0, armored.ResolveNativeMotaeboDamage(5),
        "damage clamps below AC to zero");

    var state17 = NewActor("damage-state17");
    state17.m_WAbil.AC = HUtil32.MakeLong(10, 10);
    Assert(state17.SetNativeActiveState(17), "damage state17 setup");
    Equal(15, state17.ResolveNativeMotaeboDamage(15),
        "state17 skips AC");

    var state7 = NewActor("damage-state7");
    state7.m_WAbil.AC = 0;
    Assert(state7.SetNativeActiveState(7), "damage state7 setup");
    Equal(3, state7.ResolveNativeMotaeboDamage(11),
        "state7 integer three tenths");

    var bubble4 = NewActor("damage-state20-level4");
    bubble4.m_WAbil.AC = 0;
    Assert(bubble4.AddNativeBubbleTimedAbility(4, 10),
        "damage state20 level4 setup");
    int before = bubble4.GetNativeTimedAbilityRemainingMilliseconds(20);
    Equal(3, bubble4.ResolveNativeMotaeboDamage(11),
        "state20 level4 integer three tenths");
    AssertRemainingReducedByThreeSeconds(bubble4, before,
        "state20 level4 duration");

    var bubble2 = NewActor("damage-state20-level2");
    bubble2.m_WAbil.AC = 0;
    Assert(bubble2.AddNativeBubbleTimedAbility(2, 10),
        "damage state20 level2 setup");
    before = bubble2.GetNativeTimedAbilityRemainingMilliseconds(20);
    Equal(32, bubble2.ResolveNativeMotaeboDamage(100),
        "state20 level2 integer formula");
    AssertRemainingReducedByThreeSeconds(bubble2, before,
        "state20 level2 duration");

    var precedence = NewActor("damage-state7-precedence");
    precedence.m_WAbil.AC = 0;
    Assert(precedence.SetNativeActiveState(7),
        "damage precedence state7 setup");
    Assert(precedence.AddNativeBubbleTimedAbility(2, 10),
        "damage precedence state20 setup");
    before = precedence.GetNativeTimedAbilityRemainingMilliseconds(20);
    Equal(30, precedence.ResolveNativeMotaeboDamage(100),
        "state7 precedes state20");
    int after = precedence.GetNativeTimedAbilityRemainingMilliseconds(20);
    Assert(before - after >= 0 && before - after <= 50,
        "state7 does not consume state20 duration");

    var isolated = NewActor("damage-isolated");
    isolated.m_WAbil.AC = 0;
    isolated.m_nNativeMonsterSuperForceMask = 1;
    isolated.m_nNativeMonsterSuperForceReductionPercent = 99;
    isolated.m_wNativeSkill153ShieldCharges = 2;
    Equal(100, isolated.ResolveNativeMotaeboDamage(100),
        "dedicated resolver ignores generic superforce and skill153");
    Equal((ushort)2, isolated.m_wNativeSkill153ShieldCharges,
        "dedicated resolver preserves skill153 charges");
}

static void AssertRemainingReducedByThreeSeconds(TBaseObject actor,
    int before, string label)
{
    int after = actor.GetNativeTimedAbilityRemainingMilliseconds(20);
    int reduction = before - after;
    Assert(reduction >= 3000 && reduction <= 3050,
        $"{label}: expected reduction 3000..3050, actual={reduction}");
}

static void CheckFirstCollisionDamageOccursOnce()
{
    var map = NewMap();
    var player = Place(map, NewPlayer("collision-player"), 5, 5);
    var target = Place(map, NewActor("collision-target"), 6, 5);
    player.m_Abil.Level = 20;
    target.m_Abil.Level = 1;
    target.m_WAbil.HP = 100;
    target.m_WAbil.MaxHP = 100;
    var magic = Magic(0);

    Assert(player.StartNativeMotaeboForcedMoveStep(
        Grobal2.DR_RIGHT, 0, magic), "collision immediate step");
    Assert(target.m_WAbil.HP < 100, "collision first-step damage");
    int hpAfterFirst = target.m_WAbil.HP;
    Position(player, 6, 5, "collision player first position");
    Position(target, 7, 5, "collision target first position");

    var queued = CheckContinuation(player, magic, Grobal2.DR_RIGHT, 0,
        "collision continuation");
    player.m_MsgList.Remove(queued);
    Assert(player.Operate(ToProcess(queued)),
        "collision continuation dispatch");
    Equal(hpAfterFirst, target.m_WAbil.HP,
        "continuation collision does not damage again");
    Position(player, 7, 5, "collision player second position");
    Position(target, 8, 5, "collision target second position");
}

static void CheckLevelThreeSecondActorPush()
{
    var map = NewMap();
    var player = Place(map, NewPlayer("second-player"), 5, 5);
    var first = Place(map, NewActor("second-first"), 6, 5);
    var second = Place(map, NewActor("second-second"), 7, 5);
    player.m_Abil.Level = 20;
    first.m_Abil.Level = 1;
    second.m_Abil.Level = 1;

    Assert(player.StartNativeMotaeboForcedMoveStep(
        Grobal2.DR_RIGHT, 3, Magic(3)), "level3 double push");
    Position(player, 6, 5, "level3 player position");
    Position(first, 7, 5, "level3 first actor position");
    Position(second, 8, 5, "level3 second actor position");
    Equal(4, player.m_nNativeForcedMoveRemaining,
        "level3 remaining after immediate step");
}

static void CheckFailureClearsSequence()
{
    var map = NewMap();
    var player = Place(map, NewPlayer("failure-player"), 5, 5);
    var target = Place(map, NewActor("failure-target"), 6, 5);
    player.m_Abil.Level = 20;
    target.m_Abil.Level = 20;
    player.m_WAbil.HP = 100;
    player.m_WAbil.MaxHP = 100;

    Assert(!player.StartNativeMotaeboForcedMoveStep(
        Grobal2.DR_RIGHT, 3, Magic(3)), "ineligible front fails");
    Equal(0, player.m_nNativeForcedMoveRemaining,
        "failure clears sequence");
    Equal(0, CountContinuations(player), "failure queues no continuation");
    Assert(player.m_WAbil.HP < 100, "failure self damage");
    Position(player, 5, 5, "failure player position");
    Position(target, 6, 5, "failure target position");
}

static SendMessage CheckContinuation(TPlayObject player, TUserMagic magic,
    byte direction, int level, string label)
{
    var messages = player.m_MsgList.Where(message =>
        message.wIdent == Grobal2.RM_NATIVE_MOOTEBO_CONTINUE).ToArray();
    Equal(1, messages.Length, label + " count");
    var message = messages[0];
    Equal((int)direction, message.wParam, label + " direction");
    Equal(level, message.nParam1, label + " level");
    Equal(0, message.nParam2, label + " nParam2");
    Equal(0, message.nParam3, label + " nParam3");
    Assert(message.boLateDelivery, label + " delayed");
    Assert(ReferenceEquals(player, message.BaseObject), label + " owner");
    Assert(ReferenceEquals(magic, message.Payload), label + " payload");
    int delay = unchecked(message.dwDeliveryTime - HUtil32.GetTickCount());
    Assert(delay >= 0 && delay <= 250, label + " 250ms delay");
    return message;
}

static int CountContinuations(TPlayObject player) =>
    player.m_MsgList.Count(message =>
        message.wIdent == Grobal2.RM_NATIVE_MOOTEBO_CONTINUE);

static TProcessMessage ToProcess(SendMessage message) => new()
{
    wIdent = message.wIdent,
    wParam = message.wParam,
    nParam1 = message.nParam1,
    nParam2 = message.nParam2,
    nParam3 = message.nParam3,
    BaseObject = message.BaseObject?.ObjectId ?? 0,
    boLateDelivery = true,
    Payload = message.Payload
};

static TUserMagic Magic(int level, byte baseSpell = 0) => new()
{
    btLevel = unchecked((byte)level),
    wMagIdx = SpellsDef.SKILL_MOOTEBO,
    MagicInfo = new TMagic
    {
        wMagicID = SpellsDef.SKILL_MOOTEBO,
        btDefSpell = baseSpell,
        btTrainLv = 3
    }
};

static Envirnoment NewMap()
{
    var map = new Envirnoment();
    var initialize = typeof(Envirnoment).GetMethod("Initialize",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("Envirnoment.Initialize");
    initialize.Invoke(map, new object[] { (short)24, (short)16 });
    map.sMapName = "MotaeboForcedMove";
    return map;
}

static T Place<T>(Envirnoment map, T actor, short x, short y)
    where T : TBaseObject
{
    actor.m_PEnvir = map;
    actor.m_nCurrX = x;
    actor.m_nCurrY = y;
    actor.m_boFixedHideMode = false;
    actor.m_boObMode = false;
    actor.m_boGhost = false;
    actor.m_boAddToMaped = false;
    actor.m_boDelFormMaped = false;
    Assert(ReferenceEquals(actor, map.AddToMap(x, y,
        CellType.OS_MOVINGOBJECT, actor)), "place " + actor.m_sCharName);
    return actor;
}

static ProbePlayer NewPlayer(string name) => new()
{
    m_boOffLineFlag = true,
    m_sCharName = name,
    m_btRaceServer = Grobal2.RC_PLAYOBJECT,
    m_btAttatckMode = M2Share.HAM_ALL
};

static TBaseObject NewActor(string name) => new()
{
    m_sCharName = name,
    m_btRaceServer = Grobal2.RC_ANIMAL
};

static void Position(TBaseObject actor, int x, int y, string label)
{
    Equal((short)x, actor.m_nCurrX, label + " x");
    Equal((short)y, actor.m_nCurrY, label + " y");
}

static void InitializeRuntime()
{
    M2Share.g_Config = new GameSvrConfig { nSendRefMsgRange = 12 };
    M2Share.UserEngine = new UserEngine();
    M2Share.MagicManager = new MagicManager();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.MapManager = new MapManager();
    M2Share.CastleManager = new CastleManager();
    M2Share.RandomNumber = RandomNumber.GetInstance();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new System.Collections.ArrayList();
    M2Share.g_MonSayMsgList = new Dictionary<string, IList<TMonSayMsg>>();
}

static void PrepareRuntimeConfig()
{
    string runtimeDirectory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(runtimeDirectory, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "String.ini"),
        "[String]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "Command.conf"),
        "[Command]" + Environment.NewLine);
    string robotDirectory = Path.Combine(runtimeDirectory, "RobotIni");
    Directory.CreateDirectory(robotDirectory);
    File.WriteAllText(Path.Combine(robotDirectory, "默认.txt"),
        "[Info]" + Environment.NewLine);
    string shareDirectory = Path.Combine(Path.GetFullPath(
        Path.Combine(runtimeDirectory, "..")), "Share");
    Directory.CreateDirectory(shareDirectory);
    File.WriteAllText(Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"),
        "[Integer]" + Environment.NewLine);
    Directory.SetCurrentDirectory(runtimeDirectory);
}

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{label}: expected={expected}, actual={actual}");
}

static void Assert(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
}

sealed class ProbePlayer : TPlayObject
{
    internal override void SendSocket(ClientPacket defMsg, string message)
    {
    }
}
