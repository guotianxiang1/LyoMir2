using System.Reflection;
using GameSvr;
using SystemModule;

PrepareRuntimeConfig();
InitializeRuntime();

CheckConstants();
CheckRun3StateGate();
CheckRun3NativeGateMatrix();
CheckRun3OverweightSwitchGate();
CheckMapRunPermissionParser();
CheckRun3ExactSuccessAndBroadcast();
CheckRun3NoHideSideEffect();
CheckRun3OnlyChecksFirstStep();
CheckRun3BlockedFirstStep();
CheckRun3CommittedMismatch();
CheckRun3FallbackStateMismatch();
CheckRun3FallbackAdjustedTargetSuccess();
CheckRun3FallbackBlocked();
CheckRun3FallbackZeroBoundary();
CheckRun3RecoveryStepWrap();
CheckSharedHealthSpellRecovery();
CheckRun3HorsePartnerSuccess();
CheckRun3HorsePartnerBlockedTerrainStillMoves();
CheckRun3HorsePartnerMissingSourceOnlyTurns();
CheckPushedHorsePartnerFollowsWithoutTail();
CheckHorseSilentGates();
CheckHorseMissingMount();
CheckHorseDelayGate();
CheckHorseSuccess();
CheckHorseZeroTypeMutation();
CheckHorseDismountSilentGates();
CheckHorseDismountExactPacket();
CheckHorseRiderDownBidirectionalCleanup();
CheckHorseDriverDismountCascadesPassenger();
CheckHorseDisappearCleansPairing();

Console.WriteLine(
    "NativeRun3HorseProtocolCheck PASS " +
    "4108-main=state51+absent(45/29/1/26/24/62)+forced0+bit7/map-CANRUN/weight+server-direction+first-step-only+3416 " +
    "state67/13=one-step-fallback+adjusted-target+health-10+3410 " +
    "partner=state51/non-null+exact-relocate+blocked-terrain+missing-source+push-step-tail " +
    "629=zeros 630=0/x/y/dir run3=recovery-60/10/shared-sbyte+no-hide-side-effect " +
    "4106=state52/state51/pending/delay+no-node+status16B+3555/header-only+3413/10B " +
    "4107/4111=silent-gates+state51/52-clear+bidirectional-partner+3414/3419/51B+adjacent-drop");

static void CheckConstants()
{
    Equal(4106, Grobal2.CM_SHANGMA_OK, "CM_SHANGMA_OK");
    Equal(4107, Grobal2.CM_XIAMA, "CM_XIAMA");
    Equal(4108, Grobal2.CM_RUN3, "CM_RUN3");
    Equal(4111, Grobal2.CM_RIDER_DOWN, "CM_RIDER_DOWN");
    Equal(3413, Grobal2.SM_SHANGMA_OK, "SM_SHANGMA_OK");
    Equal(3414, Grobal2.SM_XIAMA_OK, "SM_XIAMA_OK");
    Equal(3416, Grobal2.SM_RUN3, "SM_RUN3");
    Equal(3419, Grobal2.SM_XIAMA_2, "SM_XIAMA_2");
}

static void CheckRun3StateGate()
{
    var map = NewMap();
    var player = Place(map, NewPlayer("run3-state-gate"), 5, 5);
    player.m_boCanRun = true;
    player.m_btDirection = Grobal2.DR_LEFT;
    player.m_nHealthTick = 90;
    player.m_nSpellTick = 40;

    Assert(player.Operate(Run3Message(8, 5, Grobal2.DR_UP)),
        "4108 state gate dispatch");
    Packet(player.m_DefMsg, Grobal2.SM_ACT_FAIL, 0, 5, 5,
        Grobal2.DR_LEFT, "4108 state gate 630");
    Position(player, 5, 5, "4108 state gate position");
    Equal(90, player.m_nHealthTick, "4108 state gate health tick");
    Equal(40, player.m_nSpellTick, "4108 state gate spell tick");
    Equal(0, CountMessages(player, Grobal2.RM_RUN3),
        "4108 state gate broadcast");
}

static void CheckRun3ExactSuccessAndBroadcast()
{
    var map = NewMap();
    var player = Place(map, NewPlayer("run3-exact"), 5, 5);
    var observer = Place(map, NewPlayer("run3-observer"), 5, 6);
    EnableRun3(player);
    Assert(player.SetNativeActiveState(23),
        "4108 exact movement-state set");
    player.m_nHealthTick = 100;
    player.m_nSpellTick = 5;
    player.m_sbHealthSpellRecoveryStep = 3;
    player.m_dwMoveCount = 7;
    player.m_dwMoveCountA = 8;
    player.m_dwActionTick = 0;

    Assert(player.Operate(Run3Message(8, 5, Grobal2.DR_LEFT)),
        "4108 exact dispatch");
    Packet(player.m_DefMsg, Grobal2.SM_ACT_GOOD, 0, 0, 0, 0,
        "4108 exact 629");
    Position(player, 8, 5, "4108 exact position");
    Equal((byte)Grobal2.DR_RIGHT, player.m_btDirection,
        "4108 ignored client direction");
    Equal(40, player.m_nHealthTick, "4108 exact health tick penalty");
    Equal(0, player.m_nSpellTick, "4108 exact spell tick clamped");
    Equal((sbyte)2, player.m_sbHealthSpellRecoveryStep,
        "4108 exact shared recovery penalty");
    Assert(!player.HasNativeActiveState(23),
        "4108 exact movement-state removal");
    Equal(0, player.m_dwMoveCount, "4108 exact move count reset");
    Equal(0, player.m_dwMoveCountA, "4108 exact move count A reset");
    Assert(player.m_dwActionTick != 0, "4108 exact action tick updated");

    var queued = TakeQueuedMessage(observer, Grobal2.RM_RUN3,
        "4108 observer RM_RUN3");
    Equal(player.ObjectId, queued.BaseObject, "4108 RM source");
    Equal(Grobal2.DR_RIGHT, queued.wParam, "4108 RM direction");
    Equal(8, queued.nParam1, "4108 RM x");
    Equal(5, queued.nParam2, "4108 RM y");
    Equal(0, queued.nParam3, "4108 RM tail");
    Assert(observer.Operate(queued), "4108 observer RM dispatch");
    Packet(observer.m_DefMsg, Grobal2.SM_RUN3, player.ObjectId, 8, 5,
        HUtil32.MakeWord(Grobal2.DR_RIGHT, player.m_nLight),
        "4108 observer 3416");
}

static void CheckRun3NativeGateMatrix()
{
    foreach (var blockedState in new byte[] { 45, 29, 1, 26, 24, 62 })
    {
        var map = NewMap();
        var player = Place(map,
            NewPlayer("run3-state-" + blockedState), 5, 5);
        EnableRun3(player);
        Assert(player.SetNativeActiveState(blockedState),
            $"4108 blocked state {blockedState} set");
        player.m_btDirection = Grobal2.DR_LEFT;

        Assert(player.Operate(Run3Message(8, 5, Grobal2.DR_UP)),
            $"4108 blocked state {blockedState} dispatch");
        Packet(player.m_DefMsg, Grobal2.SM_ACT_FAIL, 0, 5, 5,
            Grobal2.DR_LEFT, $"4108 blocked state {blockedState} 630");
        Position(player, 5, 5,
            $"4108 blocked state {blockedState} position");
        Equal(0, CountMessages(player, Grobal2.RM_RUN3),
            $"4108 blocked state {blockedState} broadcast");
    }

    var forcedMap = NewMap();
    var forced = Place(forcedMap, NewPlayer("run3-forced-move"), 5, 5);
    EnableRun3(forced);
    forced.m_btDirection = Grobal2.DR_LEFT;
    forced.m_nNativeForcedMoveRemaining = 1;
    Assert(forced.Operate(Run3Message(8, 5, Grobal2.DR_UP)),
        "4108 forced-move gate dispatch");
    Packet(forced.m_DefMsg, Grobal2.SM_ACT_FAIL, 0, 5, 5,
        Grobal2.DR_LEFT, "4108 forced-move gate 630");
    Position(forced, 5, 5, "4108 forced-move gate position");
    Equal(0, CountMessages(forced, Grobal2.RM_RUN3),
        "4108 forced-move gate broadcast");

    var deadMap = NewMap();
    var dead = Place(deadMap, NewPlayer("run3-death"), 5, 5);
    EnableRun3(dead);
    dead.m_btDirection = Grobal2.DR_LEFT;
    dead.m_boDeath = true;
    Assert(dead.Operate(Run3Message(8, 5, Grobal2.DR_UP)),
        "4108 death gate dispatch");
    Packet(dead.m_DefMsg, Grobal2.SM_ACT_FAIL, 0, 5, 5,
        Grobal2.DR_LEFT, "4108 death gate 630");
    Position(dead, 5, 5, "4108 death gate position");
    Equal(0, CountMessages(dead, Grobal2.RM_RUN3),
        "4108 death gate broadcast");
}

static void CheckRun3OverweightSwitchGate()
{
    var originalSwitches = M2Share.ServerSwitches;
    try
    {
        M2Share.ServerSwitches = Switches(0x80);

        var blockedMap = NewMap();
        blockedMap.NativeCanRunWhileOverweight = false;
        var blocked = Place(blockedMap,
            NewPlayer("run3-overweight-blocked"), 5, 5);
        EnableRun3(blocked);
        blocked.m_WAbil.Weight = 10;
        blocked.m_WAbil.MaxWeight = 10;
        Assert(blocked.Operate(Run3Message(8, 5, Grobal2.DR_LEFT)),
            "4108 overweight fallback dispatch");
        Packet(blocked.m_DefMsg, Grobal2.SM_ACT_FAIL, 0, 6, 6,
            Grobal2.DR_DOWNRIGHT, "4108 overweight fallback 630");

        var underweightMap = NewMap();
        underweightMap.NativeCanRunWhileOverweight = false;
        var underweight = Place(underweightMap,
            NewPlayer("run3-underweight-main"), 5, 5);
        EnableRun3(underweight);
        underweight.m_WAbil.Weight = 9;
        underweight.m_WAbil.MaxWeight = 10;
        Assert(underweight.Operate(Run3Message(8, 5, Grobal2.DR_LEFT)),
            "4108 underweight main dispatch");
        Packet(underweight.m_DefMsg, Grobal2.SM_ACT_GOOD, 0, 0, 0, 0,
            "4108 underweight main 629");
        Position(underweight, 8, 5, "4108 underweight main position");

        var allowedMap = NewMap();
        allowedMap.NativeCanRunWhileOverweight = true;
        var allowed = Place(allowedMap,
            NewPlayer("run3-overweight-map-allowed"), 5, 5);
        EnableRun3(allowed);
        allowed.m_WAbil.Weight = 10;
        allowed.m_WAbil.MaxWeight = 10;
        Assert(allowed.Operate(Run3Message(8, 5, Grobal2.DR_LEFT)),
            "4108 map CANRUN dispatch");
        Packet(allowed.m_DefMsg, Grobal2.SM_ACT_GOOD, 0, 0, 0, 0,
            "4108 map CANRUN 629");

        M2Share.ServerSwitches = Switches(0);
        var disabledMap = NewMap();
        disabledMap.NativeCanRunWhileOverweight = false;
        var disabled = Place(disabledMap,
            NewPlayer("run3-overweight-switch-disabled"), 5, 5);
        EnableRun3(disabled);
        disabled.m_WAbil.Weight = 10;
        disabled.m_WAbil.MaxWeight = 10;
        Assert(disabled.Operate(Run3Message(8, 5, Grobal2.DR_LEFT)),
            "4108 switch-disabled dispatch");
        Packet(disabled.m_DefMsg, Grobal2.SM_ACT_GOOD, 0, 0, 0, 0,
            "4108 switch-disabled 629");
    }
    finally
    {
        M2Share.ServerSwitches = originalSwitches;
    }
}

static void CheckMapRunPermissionParser()
{
    var directory = Path.Combine(Path.GetTempPath(),
        "lyom2-run3-" + Guid.NewGuid().ToString("N"));
    var monItems = Path.Combine(directory, "MonItems");
    Directory.CreateDirectory(monItems);
    var fileName = Path.Combine(monItems, "MapDropItem_test.txt");
    try
    {
        File.WriteAllText(fileName,
            "; ignored" + Environment.NewLine +
            "[group, NORUN]" + Environment.NewLine,
            HUtil32.GbkEncoding);
        Assert(NativeMapRunPermission.TryLoad(directory, "test",
                out var canRun, out var error),
            "4108 NORUN parser " + error);
        Assert(!canRun, "4108 NORUN parser value");

        File.AppendAllText(fileName,
            "[group CANRUN]" + Environment.NewLine,
            HUtil32.GbkEncoding);
        Assert(NativeMapRunPermission.TryLoad(directory, "test",
                out canRun, out error),
            "4108 CANRUN parser " + error);
        Assert(canRun, "4108 CANRUN parser value");

        Assert(NativeMapRunPermission.TryLoad(directory, "missing",
                out canRun, out error),
            "4108 missing map parser " + error);
        Assert(canRun, "4108 missing map defaults CANRUN");
    }
    finally
    {
        Directory.Delete(directory, true);
    }
}

static NativeServerSwitchStore Switches(byte byte2)
{
    var switches = new byte[NativeServerSwitchStore.SwitchByteCount];
    switches[2] = byte2;
    return NativeServerSwitchStore.FromSnapshot("run3-switches.bin", switches);
}

static void CheckRun3OnlyChecksFirstStep()
{
    var map = NewMap();
    var player = Place(map, NewPlayer("run3-middle-pass"), 5, 5);
    Place(map, NewPlayer("run3-middle-blocker"), 7, 5);
    EnableRun3(player);

    Assert(player.Operate(Run3Message(8, 5, Grobal2.DR_UP)),
        "4108 middle-cell dispatch");
    Packet(player.m_DefMsg, Grobal2.SM_ACT_GOOD, 0, 0, 0, 0,
        "4108 middle-cell 629");
    Position(player, 8, 5, "4108 middle-cell position");
}

static void CheckRun3NoHideSideEffect()
{
    var map = NewMap();
    var player = Place(map, NewPlayer("run3-hide-state"), 5, 5);
    EnableRun3(player);
    player.m_boTransparent = true;
    player.m_boHideMode = true;
    player.m_wStatusTimeArr[Grobal2.STATE_TRANSPARENT] = 0;

    Assert(player.Operate(Run3Message(8, 5, Grobal2.DR_UP)),
        "4108 hide-state dispatch");
    Packet(player.m_DefMsg, Grobal2.SM_ACT_GOOD, 0, 0, 0, 0,
        "4108 hide-state 629");
    Equal((ushort)0,
        player.m_wStatusTimeArr[Grobal2.STATE_TRANSPARENT],
        "4108 transparent status unchanged");
}

static void CheckRun3BlockedFirstStep()
{
    var map = NewMap();
    var player = Place(map, NewPlayer("run3-first-blocked"), 5, 5);
    Place(map, NewPlayer("run3-first-blocker"), 6, 5);
    EnableRun3(player);
    player.m_nHealthTick = 100;
    player.m_nSpellTick = 50;
    player.m_sbHealthSpellRecoveryStep = 4;

    Assert(player.Operate(Run3Message(8, 5, Grobal2.DR_UP)),
        "4108 first-cell dispatch");
    Packet(player.m_DefMsg, Grobal2.SM_ACT_FAIL, 0, 5, 5,
        Grobal2.DR_RIGHT, "4108 first-cell 630");
    Position(player, 5, 5, "4108 first-cell position");
    Equal(100, player.m_nHealthTick, "4108 first-cell health tick");
    Equal(50, player.m_nSpellTick, "4108 first-cell spell tick");
    Equal((sbyte)4, player.m_sbHealthSpellRecoveryStep,
        "4108 first-cell shared recovery");
}

static void CheckRun3CommittedMismatch()
{
    var map = NewMap();
    var player = Place(map, NewPlayer("run3-mismatch"), 5, 5);
    EnableRun3(player);
    player.m_nHealthTick = 80;
    player.m_nSpellTick = 30;
    player.m_sbHealthSpellRecoveryStep = 2;
    player.m_dwMoveCount = 7;
    player.m_dwMoveCountA = 8;
    player.m_dwActionTick = 1234;

    Assert(player.Operate(Run3Message(9, 5, Grobal2.DR_UP)),
        "4108 mismatch dispatch");
    Packet(player.m_DefMsg, Grobal2.SM_ACT_FAIL, 0, 8, 5,
        Grobal2.DR_RIGHT, "4108 committed mismatch 630");
    Position(player, 8, 5, "4108 mismatch committed position");
    Equal(20, player.m_nHealthTick, "4108 mismatch health tick penalty");
    Equal(20, player.m_nSpellTick, "4108 mismatch spell tick penalty");
    Equal((sbyte)1, player.m_sbHealthSpellRecoveryStep,
        "4108 mismatch shared recovery penalty");
    Equal(7, player.m_dwMoveCount, "4108 mismatch move count");
    Equal(8, player.m_dwMoveCountA, "4108 mismatch move count A");
    Equal(1234, player.m_dwActionTick, "4108 mismatch action tick");
    Equal(1, CountMessages(player, Grobal2.RM_RUN3),
        "4108 mismatch broadcast");
}

static void CheckRun3FallbackStateMismatch()
{
    foreach (var fallbackState in new byte[] { 67, 13 })
    {
        var map = NewMap();
        var player = Place(map,
            NewPlayer("run3-fallback-" + fallbackState), 5, 5);
        var observer = Place(map,
            NewPlayer("run3-fallback-observer-" + fallbackState), 5, 6);
        EnableRun3(player);
        Assert(player.SetNativeActiveState(fallbackState),
            $"4108 fallback state {fallbackState} set");
        Assert(player.SetNativeActiveState(23),
            $"4108 fallback state {fallbackState} movement-state set");
        player.m_nHealthTick = 100;
        player.m_nSpellTick = 30;
        player.m_sbHealthSpellRecoveryStep = 4;

        Assert(player.Operate(Run3Message(8, 5, Grobal2.DR_LEFT)),
            $"4108 fallback state {fallbackState} dispatch");
        Packet(player.m_DefMsg, Grobal2.SM_ACT_FAIL, 0, 6, 6,
            Grobal2.DR_DOWNRIGHT,
            $"4108 fallback state {fallbackState} committed 630");
        Position(player, 6, 6,
            $"4108 fallback state {fallbackState} position");
        Equal(90, player.m_nHealthTick,
            $"4108 fallback state {fallbackState} health tick");
        Equal(30, player.m_nSpellTick,
            $"4108 fallback state {fallbackState} spell tick");
        Equal((sbyte)4, player.m_sbHealthSpellRecoveryStep,
            $"4108 fallback state {fallbackState} shared recovery");
        Assert(!player.HasNativeActiveState(23),
            $"4108 fallback state {fallbackState} movement-state removal");
        Equal(0, CountMessages(observer, Grobal2.RM_RUN3),
            $"4108 fallback state {fallbackState} no run3 broadcast");
        Equal(1, CountMessages(observer, Grobal2.RM_WALK),
            $"4108 fallback state {fallbackState} walk broadcast");
    }
}

static void CheckRun3FallbackAdjustedTargetSuccess()
{
    var map = NewMap();
    var player = Place(map, NewPlayer("run3-fallback-success"), 5, 5);
    EnableRun3(player);
    Assert(player.SetNativeActiveState(67),
        "4108 fallback adjusted-target state set");
    player.m_nHealthTick = 70;
    player.m_dwMoveCount = 7;
    player.m_dwMoveCountA = 8;

    Assert(player.Operate(Run3Message(7, 4, Grobal2.DR_LEFT)),
        "4108 fallback adjusted-target dispatch");
    Packet(player.m_DefMsg, Grobal2.SM_ACT_GOOD, 0, 0, 0, 0,
        "4108 fallback adjusted-target 629");
    Position(player, 6, 5, "4108 fallback adjusted-target position");
    Equal(60, player.m_nHealthTick,
        "4108 fallback adjusted-target health tick");
    Equal(0, player.m_dwMoveCount,
        "4108 fallback adjusted-target move count");
    Equal(0, player.m_dwMoveCountA,
        "4108 fallback adjusted-target move count A");
}

static void CheckRun3FallbackBlocked()
{
    var map = NewMap();
    var player = Place(map, NewPlayer("run3-fallback-blocked"), 5, 5);
    Place(map, NewPlayer("run3-fallback-blocker"), 6, 6);
    EnableRun3(player);
    Assert(player.SetNativeActiveState(13),
        "4108 fallback blocked state set");
    player.m_nHealthTick = 70;

    Assert(player.Operate(Run3Message(8, 5, Grobal2.DR_LEFT)),
        "4108 fallback blocked dispatch");
    Packet(player.m_DefMsg, Grobal2.SM_ACT_FAIL, 0, 5, 5,
        Grobal2.DR_DOWNRIGHT, "4108 fallback blocked 630");
    Position(player, 5, 5, "4108 fallback blocked position");
    Equal(70, player.m_nHealthTick, "4108 fallback blocked health tick");
}

static void CheckRun3FallbackZeroBoundary()
{
    var map = NewMap();
    var player = Place(map, NewPlayer("run3-fallback-zero"), 1, 5);
    EnableRun3(player);
    Assert(player.SetNativeActiveState(67),
        "4108 fallback zero-boundary state set");
    player.m_nHealthTick = 70;

    Assert(player.Operate(Run3Message(-1, 4, Grobal2.DR_RIGHT)),
        "4108 fallback zero-boundary dispatch");
    Packet(player.m_DefMsg, Grobal2.SM_ACT_FAIL, 0, 1, 5,
        Grobal2.DR_LEFT, "4108 fallback zero-boundary 630");
    Position(player, 1, 5, "4108 fallback zero-boundary position");
    Equal(70, player.m_nHealthTick,
        "4108 fallback zero-boundary health tick");
}

static void CheckRun3RecoveryStepWrap()
{
    var map = NewMap();
    var player = Place(map, NewPlayer("run3-recovery-wrap"), 5, 5);
    EnableRun3(player);
    player.m_sbHealthSpellRecoveryStep = sbyte.MinValue;

    Assert(player.Operate(Run3Message(8, 5, Grobal2.DR_UP)),
        "4108 recovery wrap dispatch");
    Equal((sbyte)127, player.m_sbHealthSpellRecoveryStep,
        "4108 recovery signed-byte wrap");
}

static void CheckSharedHealthSpellRecovery()
{
    var actor = new TBaseObject();
    actor.m_Abil.Level = 20;
    actor.m_WAbil.Level = 20;
    actor.m_WAbil.HP = 50;
    actor.m_WAbil.MP = 40;
    actor.m_WAbil.MaxHP = 100;
    actor.m_WAbil.MaxMP = 100;
    actor.m_nIncHealth = 20;
    actor.m_nIncSpell = 20;
    actor.m_nIncHealing = 0;
    actor.m_sbHealthSpellRecoveryStep = 3;
    var now = HUtil32.GetTickCount();
    actor.m_dwHPMPTick = now;
    actor.m_dwIncHealthSpellTick = unchecked(now - 1000);

    actor.Run();

    Equal(17, actor.m_nIncHealth, "shared recovery health remainder");
    Equal(17, actor.m_nIncSpell, "shared recovery spell remainder");
    Equal(53, actor.m_WAbil.HP, "shared recovery health amount");
    Equal(43, actor.m_WAbil.MP, "shared recovery spell amount");
    Equal((sbyte)12, actor.m_sbHealthSpellRecoveryStep,
        "shared recovery Level/10+10 reset");
}

static void CheckRun3HorsePartnerSuccess()
{
    var map = NewMap();
    var driver = Place(map, NewPlayer("run3-partner-driver"), 5, 5);
    var partner = Place(map, NewPlayer("run3-partner-passenger"), 5, 6);
    var observer = Place(map, NewPlayer("run3-partner-observer"), 5, 7);
    EnableRun3(driver);
    partner.SetNativeActiveState(52);
    partner.SetNativeActiveState(23);
    partner.m_btDirection = Grobal2.DR_LEFT;
    SetHorsePartner(driver, partner);

    Assert(driver.Operate(Run3Message(8, 5, Grobal2.DR_UP)),
        "4108 partner success dispatch");
    Position(driver, 8, 5, "4108 partner driver position");
    Position(partner, 8, 5, "4108 partner passenger position");
    Equal((byte)Grobal2.DR_RIGHT, partner.m_btDirection,
        "4108 partner direction");
    Assert(!partner.HasNativeActiveState(23),
        "4108 partner state23 removal");
    Equal(2, CountCellActor(map, 8, 5, driver, partner),
        "4108 partner shared cell registration");
    Equal(1, CountMessages(observer, Grobal2.RM_RUN3),
        "4108 partner no independent run broadcast");
}

static void CheckRun3HorsePartnerBlockedTerrainStillMoves()
{
    var driverMap = NewMap();
    var partnerMap = NewMap();
    partnerMap.SetMapXYFlag(8, 5, false);
    var driver = Place(driverMap, NewPlayer("run3-partner-blocked-driver"),
        5, 5);
    var partner = Place(partnerMap,
        NewPlayer("run3-partner-blocked-passenger"), 5, 5);
    EnableRun3(driver);
    partner.SetNativeActiveState(52);
    partner.SetNativeActiveState(23);
    partner.m_btDirection = Grobal2.DR_LEFT;
    SetHorsePartner(driver, partner);

    Assert(driver.Operate(Run3Message(8, 5, Grobal2.DR_UP)),
        "4108 blocked-terrain partner dispatch");
    Position(driver, 8, 5, "4108 blocked-terrain driver position");
    Position(partner, 8, 5, "4108 blocked-terrain passenger position");
    Equal((byte)Grobal2.DR_RIGHT, partner.m_btDirection,
        "4108 blocked-terrain partner direction");
    Assert(!partner.HasNativeActiveState(23),
        "4108 blocked-terrain partner state23 removal");
    Equal(0, CountCellActor(partnerMap, 5, 5, partner),
        "4108 blocked-terrain old registration removed");
    Equal(1, CountCellActor(partnerMap, 8, 5, partner),
        "4108 blocked-terrain target registration");
    Equal(0, CountMessages(partner, Grobal2.RM_RUN3),
        "4108 blocked-terrain partner no run broadcast");
}

static void CheckRun3HorsePartnerMissingSourceOnlyTurns()
{
    var driverMap = NewMap();
    var partnerMap = NewMap();
    var driver = Place(driverMap, NewPlayer("run3-partner-missing-driver"),
        5, 5);
    var partner = NewPlayer("run3-partner-missing-passenger");
    partner.m_PEnvir = partnerMap;
    partner.m_nCurrX = 5;
    partner.m_nCurrY = 5;
    EnableRun3(driver);
    partner.SetNativeActiveState(52);
    partner.SetNativeActiveState(23);
    partner.m_btDirection = Grobal2.DR_LEFT;
    SetHorsePartner(driver, partner);

    Assert(driver.Operate(Run3Message(8, 5, Grobal2.DR_UP)),
        "4108 missing-source partner dispatch");
    Position(driver, 8, 5, "4108 missing-source driver position");
    Position(partner, 5, 5, "4108 missing-source passenger position");
    Equal((byte)Grobal2.DR_RIGHT, partner.m_btDirection,
        "4108 missing-source direction is written before relocation");
    Assert(partner.HasNativeActiveState(23),
        "4108 missing-source state23 retained");
    Equal(0, CountCellActor(partnerMap, 8, 5, partner),
        "4108 missing-source target remains unchanged");
}

static void CheckPushedHorsePartnerFollowsWithoutTail()
{
    var map = NewMap();
    var driver = Place(map, NewPlayer("push-partner-driver"), 5, 5);
    var partner = Place(map, NewPlayer("push-partner-passenger"), 5, 6);
    var observer = Place(map, NewPlayer("push-partner-observer"), 5, 7);
    driver.SetNativeActiveState(51);
    partner.SetNativeActiveState(52);
    partner.SetNativeActiveState(23);
    SetHorsePartner(driver, partner);

    Equal(2, driver.CharPushed(Grobal2.DR_RIGHT, 2),
        "mounted driver push count");
    Position(driver, 7, 5, "pushed driver position");
    Position(partner, 7, 5, "pushed passenger position");
    Equal((byte)Grobal2.DR_LEFT, driver.m_btDirection,
        "pushed driver back direction");
    Equal((byte)Grobal2.DR_LEFT, partner.m_btDirection,
        "pushed passenger back direction");
    Assert(partner.HasNativeActiveState(23),
        "sub_6BBF4C must not run the sub_6BBEE4 state23 tail");
    Equal(2, CountCellActor(map, 7, 5, driver, partner),
        "pushed pair shared cell registration");
    Equal(2, CountMessages(observer, Grobal2.RM_PUSH),
        "passenger must not emit an independent push broadcast");
}

static void CheckHorseSilentGates()
{
    foreach (var state in new[] { 52, 51 })
    {
        var map = NewMap();
        var player = Place(map, NewPlayer("horse-state-" + state), 5, 5);
        EquipMount(player, 7);
        SetPending(player, true, 0x11223344, 0x5566);
        player.SetNativeActiveState(state);
        var before = player.m_MsgList.Count;

        Assert(player.Operate(HorseReadyMessage()),
            $"4106 state{state} dispatch");
        Equal(before, player.m_MsgList.Count,
            $"4106 state{state} silence");
        Pending(player, true, 0x11223344, 0x5566,
            $"4106 state{state} pending");
        Assert(!player.m_boOnHorse, $"4106 state{state} horse flag");
    }

    var noPendingMap = NewMap();
    var noPending = Place(noPendingMap, NewPlayer("horse-no-pending"), 5, 5);
    EquipMount(noPending, 7);
    Assert(noPending.Operate(HorseReadyMessage()), "4106 no-pending dispatch");
    Equal(0, noPending.m_MsgList.Count, "4106 no-pending silence");
    Assert(!noPending.HasNativeActiveState(51),
        "4106 no-pending state");
}

static void CheckHorseMissingMount()
{
    var player = NewPlayer("horse-missing-mount");
    SetPending(player, true, 10, 20);

    Assert(player.Operate(HorseReadyMessage()), "4106 missing-mount dispatch");
    Pending(player, false, 0, 0, "4106 missing-mount pending clear");
    Assert(!player.HasNativeActiveState(51),
        "4106 missing-mount state");
    Assert(!player.m_boOnHorse, "4106 missing-mount horse flag");
    var message = LastMessage(player, Grobal2.RM_SYSMESSAGE,
        "4106 missing-mount message");
    Equal(0xFF, message.nParam1, "4106 missing-mount foreground");
    Equal(0xFC, message.nParam2, "4106 missing-mount background");
    Equal("您无主宰者马牌,无法召唤坐骑！", message.Buff,
        "4106 missing-mount text");
}

static void CheckHorseDelayGate()
{
    var map = NewMap();
    var player = Place(map, NewPlayer("horse-delay"), 5, 5);
    EquipMount(player, 7);
    var now = unchecked((uint)HUtil32.GetTickCount());
    SetPending(player, true, now, ushort.MaxValue);

    Assert(player.Operate(HorseReadyMessage()), "4106 delay dispatch");
    Pending(player, true, now, ushort.MaxValue,
        "4106 delay pending retained");
    Assert(!player.HasNativeActiveState(51), "4106 delay state");
    Assert(!player.m_boOnHorse, "4106 delay horse flag");
    Equal(0, player.m_MsgList.Count, "4106 delay silence");
}

static void CheckHorseSuccess()
{
    var map = NewMap();
    var player = Place(map, NewPlayer("horse-success"), 5, 5);
    var observer = Place(map, NewPlayer("horse-success-observer"), 5, 6);
    var mount = EquipMount(player, 7);
    SetPending(player, true, 0, 0);

    Assert(player.Operate(HorseReadyMessage()), "4106 success dispatch");
    Assert(player.HasNativeActiveState(51), "4106 success state51");
    Assert(player.m_boOnHorse, "4106 success horse flag");
    Equal((byte)7, player.m_btHorseType, "4106 success horse type");
    Assert(GetHorsePairReady(player), "4106 pair-ready state");
    Equal((byte)7, mount.NativeRecord[0x33],
        "4106 success native byte");
    Pending(player, false, 0, 0, "4106 success pending clear");
    CheckNoMountedTimedNode(player);

    var status = LastMessage(player, Grobal2.RM_CHARSTATUSCHANGED,
        "4106 state status source");
    var statusBody = status.Payload as byte[];
    Assert(statusBody != null, "4106 state status body");
    Assert(statusBody.SequenceEqual(player.GetBodyStateBuffer()),
        "4106 state status exact 16-byte body");
    Equal(16, statusBody.Length, "4106 state status body length");
    Equal(1, CountMessages(observer, Grobal2.RM_CHARSTATUSCHANGED),
        "4106 state status observer");

    var timedPacket = player.SocketMessages.Single(entry =>
        entry.Packet.Ident == 3555);
    Packet(timedPacket.Packet, 3555, 0, 51, 0, 0,
        "4106 header-only state packet");
    Equal(string.Empty, timedPacket.Body,
        "4106 header-only state packet body");

    var system = LastMessage(player, Grobal2.RM_SYSMESSAGE,
        "4106 success message");
    Equal(0xFF, system.nParam1, "4106 success foreground");
    Equal(0xFC, system.nParam2, "4106 success background");
    Equal("成功召唤坐骑！", system.Buff,
        "4106 success text");
    Equal(1, CountMessages(player, Grobal2.RM_FEATURECHANGED),
        "4106 success feature source");
    Equal(1, CountMessages(observer, Grobal2.RM_FEATURECHANGED),
        "4106 success feature observer");

    var queued = TakeQueuedMessage(player, Grobal2.RM_SHANGMA_OK,
        "4106 source RM_SHANGMA_OK");
    Equal(1, queued.wParam, "4106 RM param");
    Equal(0, queued.nParam1, "4106 RM nParam1");
    Equal(0, queued.nParam2, "4106 RM nParam2");
    Equal(0, queued.nParam3, "4106 RM nParam3");
    var body = queued.Payload as byte[];
    Assert(body != null, "4106 RM body");
    Equal(10, body.Length, "4106 RM body length");
    Equal((byte)7, body[8], "4106 RM horse low byte");
    Equal((byte)0, body[9], "4106 RM horse high byte");
    Assert(player.Operate(queued), "4106 source RM dispatch");
    Packet(player.m_DefMsg, Grobal2.SM_SHANGMA_OK, player.ObjectId,
        1, 10, 0, "4106 source 3413");
}

static void CheckHorseZeroTypeMutation()
{
    var map = NewMap();
    var player = Place(map, NewPlayer("horse-zero-type"), 5, 5);
    var mount = EquipMount(player, 0);
    SetPending(player, true, 0, 0);

    Assert(player.Operate(HorseReadyMessage()), "4106 zero-type dispatch");
    Equal((byte)1, mount.NativeRecord[0x33],
        "4106 zero-type native mutation");
    Equal((byte)0, player.m_btHorseType,
        "4106 zero-type return-before-mutation");
    Assert(player.m_boOnHorse, "4106 zero-type horse flag");
}

static void CheckHorseDismountSilentGates()
{
    var map = NewMap();
    var player = Place(map, NewPlayer("horse-dismount-silent"), 5, 5);
    var before = player.m_MsgList.Count;

    Assert(player.Operate(HorseDismountMessage()),
        "4107 no-state dispatch");
    Equal(before, player.m_MsgList.Count, "4107 no-state silence");

    Assert(player.Operate(HorseRiderDownMessage()),
        "4111 no-state dispatch");
    Equal(before, player.m_MsgList.Count, "4111 no-state silence");
}

static void CheckHorseDismountExactPacket()
{
    var map = NewMap();
    var player = Place(map, NewPlayer("horse-dismount"), 5, 5);
    var observer = Place(map, NewPlayer("horse-dismount-observer"), 5, 6);
    Assert(player.SetNativeActiveState(51), "4107 state51 set");
    player.m_boOnHorse = true;
    player.m_btHorseType = 7;
    SetPending(player, true, 0x11223344, 0x5566);

    Assert(player.Operate(HorseDismountMessage()), "4107 dispatch");
    Assert(!player.HasNativeActiveState(51), "4107 state51 clear");
    Assert(!player.m_boOnHorse, "4107 horse flag clear");
    Equal((byte)0, player.m_btHorseType, "4107 horse type clear");
    Pending(player, false, 0, 0, "4107 pending clear");

    var queued = TakeQueuedMessage(observer, Grobal2.RM_NATIVE_XIAMA_OK,
        "4107 observer RM");
    Equal(player.ObjectId, queued.BaseObject, "4107 RM source");
    Equal(1, queued.wParam, "4107 RM param");
    Equal(51, queued.nParam1, "4107 RM payload length");
    Equal(1, queued.nParam3, "4107 RM series");
    var body = queued.Payload as byte[];
    Assert(body != null, "4107 body");
    CheckHorseDismountBody(body, player.GetShowName(), "4107 body");
    Assert(observer.Operate(queued), "4107 observer RM dispatch");
    Packet(observer.m_DefMsg, Grobal2.SM_XIAMA_OK, player.ObjectId,
        1, 51, 1, "4107 source 3414");
}

static void CheckHorseRiderDownBidirectionalCleanup()
{
    var map = NewMap();
    var driver = Place(map, NewPlayer("horse-rider-driver"), 5, 5);
    var passenger = Place(map, NewPlayer("horse-rider-passenger"), 5, 5);
    var observer = Place(map, NewPlayer("horse-rider-observer"), 5, 7);
    Assert(driver.SetNativeActiveState(51), "4111 driver state51 set");
    Assert(passenger.SetNativeActiveState(52),
        "4111 passenger state52 set");
    driver.m_boOnHorse = true;
    driver.m_btHorseType = 7;
    passenger.m_boOnHorse = true;
    passenger.m_btHorseType = 7;
    passenger.m_btDirection = Grobal2.DR_RIGHT;
    passenger.m_nHealthTick = 90;
    passenger.m_nSpellTick = 40;
    SetHorsePairing(driver, passenger, false, true);

    Assert(passenger.Operate(HorseRiderDownMessage()), "4111 dispatch");
    Assert(!passenger.HasNativeActiveState(52),
        "4111 passenger state52 clear");
    Assert(driver.HasNativeActiveState(51),
        "4111 driver state51 retained");
    Assert(!passenger.m_boOnHorse, "4111 passenger horse flag clear");
    Assert(driver.m_boOnHorse, "4111 driver horse flag retained");
    Equal(null, GetHorsePartner(driver), "4111 driver partner clear");
    Equal(null, GetHorsePartner(passenger), "4111 passenger partner clear");
    Assert(GetHorsePairReady(driver), "4111 driver pair-ready restore");
    Assert(!GetHorsePassengerActive(passenger),
        "4111 passenger active clear");
    Position(passenger, 6, 5, "4111 passenger adjacent drop");
    Equal(90, passenger.m_nHealthTick, "4111 no health recovery cost");
    Equal(40, passenger.m_nSpellTick, "4111 no spell recovery cost");
    Equal(1, CountMessages(observer, Grobal2.RM_WALK),
        "4111 one movement broadcast");

    var riderPackets = TakeAllQueuedMessages(observer,
        Grobal2.RM_NATIVE_XIAMA_2);
    Equal(2, riderPackets.Length, "4111 passenger+driver packets");
    Assert(riderPackets.Any(entry => entry.BaseObject == passenger.ObjectId),
        "4111 passenger packet source");
    Assert(riderPackets.Any(entry => entry.BaseObject == driver.ObjectId),
        "4111 driver packet source");
    foreach (var packet in riderPackets)
    {
        Equal(51, (packet.Payload as byte[])?.Length ?? 0,
            "4111 body length");
    }
}

static void CheckHorseDriverDismountCascadesPassenger()
{
    var map = NewMap();
    var driver = Place(map, NewPlayer("horse-cascade-driver"), 5, 5);
    var passenger = Place(map, NewPlayer("horse-cascade-passenger"), 5, 5);
    var observer = Place(map, NewPlayer("horse-cascade-observer"), 5, 7);
    Assert(driver.SetNativeActiveState(51), "4107 cascade state51 set");
    Assert(passenger.SetNativeActiveState(52),
        "4107 cascade state52 set");
    driver.m_boOnHorse = true;
    driver.m_btHorseType = 7;
    passenger.m_boOnHorse = true;
    passenger.m_btHorseType = 7;
    SetHorsePairing(driver, passenger, false, true);

    Assert(driver.Operate(HorseDismountMessage()), "4107 cascade dispatch");
    Assert(!driver.HasNativeActiveState(51),
        "4107 cascade driver state clear");
    Assert(!passenger.HasNativeActiveState(52),
        "4107 cascade passenger state clear");
    Equal(null, GetHorsePartner(driver),
        "4107 cascade driver partner clear");
    Equal(null, GetHorsePartner(passenger),
        "4107 cascade passenger partner clear");
    Assert(!GetHorsePairReady(driver),
        "4107 cascade does not restore pair-ready");
    Equal(1, CountMessages(observer, Grobal2.RM_NATIVE_XIAMA_OK),
        "4107 cascade driver 3414 source");
    Equal(1, CountMessages(observer, Grobal2.RM_NATIVE_XIAMA_2),
        "4107 cascade passenger-only 3419 source");
}

static void CheckHorseDisappearCleansPairing()
{
    var map = NewMap();
    var driver = Place(map, NewPlayer("horse-exit-driver"), 5, 5);
    var passenger = Place(map, NewPlayer("horse-exit-passenger"), 5, 5);
    Assert(driver.SetNativeActiveState(51), "horse exit state51 set");
    Assert(passenger.SetNativeActiveState(52), "horse exit state52 set");
    driver.m_boOnHorse = true;
    passenger.m_boOnHorse = true;
    SetHorsePairing(driver, passenger, false, true);

    driver.Disappear();

    Assert(!driver.HasNativeActiveState(51), "horse exit state51 clear");
    Assert(!passenger.HasNativeActiveState(52), "horse exit state52 clear");
    Equal(null, GetHorsePartner(driver), "horse exit driver partner clear");
    Equal(null, GetHorsePartner(passenger),
        "horse exit passenger partner clear");
}

static void CheckHorseDismountBody(byte[] body, string expectedName,
    string label)
{
    Equal(51, body.Length, label + " length");
    Equal((ushort)0, BitConverter.ToUInt16(body, 8),
        label + " horse feature clear");
    var name = HUtil32.GbkEncoding.GetBytes(expectedName);
    var length = Math.Min(40, name.Length);
    Equal((byte)length, body[10], label + " name length");
    Assert(body.AsSpan(11, length).SequenceEqual(name.AsSpan(0, length)),
        label + " name bytes");
}

static void EnableRun3(ProbePlayer player)
{
    Assert(player.SetNativeActiveState(51), "run3 state51 set");
}

static TUserItem EquipMount(ProbePlayer player, byte mountType)
{
    var record = new byte[208];
    record[0x33] = mountType;
    var item = new TUserItem
    {
        wIndex = 1,
        NativeRecord = record
    };
    player.m_UseItems[Grobal2.U_MOUNT] = item;
    return item;
}

static TProcessMessage Run3Message(int x, int y, int clientDirection) => new()
{
    wIdent = Grobal2.CM_RUN3,
    wParam = clientDirection,
    nParam1 = x,
    nParam2 = y
};

static TProcessMessage HorseReadyMessage() => new()
{
    wIdent = Grobal2.CM_SHANGMA_OK,
    nParam2 = 1
};

static TProcessMessage HorseDismountMessage() => new()
{
    wIdent = Grobal2.CM_XIAMA
};

static TProcessMessage HorseRiderDownMessage() => new()
{
    wIdent = Grobal2.CM_RIDER_DOWN
};

static void CheckNoMountedTimedNode(TBaseObject actor)
{
    var head = Field(typeof(TBaseObject), "m_TimedAbilityHead").GetValue(actor);
    Assert(head == null, "4106 must not create a timed node");
}

static void SetPending(TPlayObject player, bool pending, uint tick, ushort delay)
{
    Field(player.GetType(), "m_boNativeHorseCallPending").SetValue(player,
        pending);
    Field(player.GetType(), "m_dwNativeHorseCallTick").SetValue(player, tick);
    Field(player.GetType(), "m_wNativeHorseCallDelay").SetValue(player, delay);
}

static void SetHorsePartner(TPlayObject driver, TPlayObject partner)
{
    Field(driver.GetType(), "m_NativeHorsePartner").SetValue(driver,
        partner);
}

static void SetHorsePairing(TPlayObject driver, TPlayObject passenger,
    bool pairReady, bool passengerActive)
{
    Field(driver.GetType(), "m_NativeHorsePartner").SetValue(driver,
        passenger);
    Field(passenger.GetType(), "m_NativeHorsePartner").SetValue(passenger,
        driver);
    Field(driver.GetType(), "m_boNativeHorsePairReady").SetValue(driver,
        pairReady);
    Field(passenger.GetType(), "m_boNativeHorsePassengerActive").SetValue(
        passenger, passengerActive);
}

static TPlayObject GetHorsePartner(TPlayObject player) =>
    (TPlayObject)Field(player.GetType(), "m_NativeHorsePartner")
        .GetValue(player);

static bool GetHorsePairReady(TPlayObject player) =>
    (bool)Field(player.GetType(), "m_boNativeHorsePairReady")
        .GetValue(player);

static bool GetHorsePassengerActive(TPlayObject player) =>
    (bool)Field(player.GetType(), "m_boNativeHorsePassengerActive")
        .GetValue(player);

static void Pending(TPlayObject player, bool pending, uint tick, ushort delay,
    string label)
{
    Equal(pending, (bool)Field(player.GetType(),
        "m_boNativeHorseCallPending").GetValue(player), label + " flag");
    Equal(tick, (uint)Field(player.GetType(),
        "m_dwNativeHorseCallTick").GetValue(player), label + " tick");
    Equal(delay, (ushort)Field(player.GetType(),
        "m_wNativeHorseCallDelay").GetValue(player), label + " delay");
}

static FieldInfo Field(Type type, string name)
{
    for (var current = type; current != null; current = current.BaseType)
    {
        var field = current.GetField(name, BindingFlags.Instance |
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.DeclaredOnly);
        if (field != null) return field;
    }
    throw new MissingFieldException(type.FullName, name);
}

static int CountMessages(TBaseObject actor, int ident) =>
    actor.m_MsgList.Count(message => message.wIdent == ident);

static int CountCellActor(Envirnoment map, int x, int y,
    params TBaseObject[] actors)
{
    var success = false;
    var cell = map.GetMapCellInfo(x, y, ref success);
    if (!success || cell.ObjList == null) return 0;
    return actors.Count(actor => cell.ObjList.Any(entry =>
        entry.CellType == CellType.OS_MOVINGOBJECT &&
        ReferenceEquals(entry.CellObj, actor)));
}

static SendMessage LastMessage(TBaseObject actor, int ident, string label)
{
    var messages = actor.m_MsgList.Where(message => message.wIdent == ident)
        .ToArray();
    Assert(messages.Length > 0, label);
    return messages[^1];
}

static TProcessMessage TakeQueuedMessage(ProbePlayer player, int ident,
    string label)
{
    TProcessMessage message = null;
    while (player.TryTake(ref message))
    {
        if (message.wIdent == ident) return message;
    }
    throw new InvalidOperationException(label);
}

static TProcessMessage[] TakeAllQueuedMessages(ProbePlayer player, int ident)
{
    var result = new List<TProcessMessage>();
    TProcessMessage message = null;
    while (player.TryTake(ref message))
    {
        if (message.wIdent == ident) result.Add(message);
    }
    return result.ToArray();
}

static void Packet(ClientPacket packet, int ident, int recog, int param,
    int tag, int series, string label)
{
    Assert(packet != null, label + " packet");
    Equal(unchecked((ushort)ident), packet.Ident, label + " ident");
    Equal(recog, packet.Recog, label + " recog");
    Equal(unchecked((ushort)param), packet.Param, label + " param");
    Equal(unchecked((ushort)tag), packet.Tag, label + " tag");
    Equal(unchecked((ushort)series), packet.Series, label + " series");
}

static void Position(TBaseObject actor, int x, int y, string label)
{
    Equal((short)x, actor.m_nCurrX, label + " x");
    Equal((short)y, actor.m_nCurrY, label + " y");
}

static Envirnoment NewMap()
{
    // sMapName 必须非空：SPWN-56 的有效性谓词第三项对应原生
    // 0x765D85 `cmp dword [eax+0x44],0`（PEnvir.MapName <> ''），
    // 空名地图上的 actor 会在首次视野扫描时被判失效摘链。
    // 生产地图一律经 Maps.cs:77（拒绝空名）或动态房间工厂
    // （sMapName = definition.RoomName）建立，裸 new 是夹具特有的失真态。
    var map = new Envirnoment { sMapName = "0" };
    var initialize = typeof(Envirnoment).GetMethod("Initialize",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("Envirnoment.Initialize");
    initialize.Invoke(map, new object[] { (short)16, (short)16 });
    return map;
}

static ProbePlayer Place(Envirnoment map, ProbePlayer player, short x, short y)
{
    player.m_PEnvir = map;
    player.m_nCurrX = x;
    player.m_nCurrY = y;
    player.m_boFixedHideMode = false;
    player.m_boObMode = false;
    player.m_boGhost = false;
    player.m_boAddToMaped = false;
    player.m_boDelFormMaped = false;
    Assert(ReferenceEquals(player, map.AddToMap(x, y,
        CellType.OS_MOVINGOBJECT, player)), "place " + player.m_sCharName);
    return player;
}

static ProbePlayer NewPlayer(string name) => new()
{
    m_boOffLineFlag = true,
    m_sCharName = name,
    m_btRaceServer = Grobal2.RC_PLAYOBJECT
};

static void InitializeRuntime()
{
    M2Share.g_Config = new GameSvrConfig { nSendRefMsgRange = 12 };
    M2Share.UserEngine = new UserEngine();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.MapManager = new MapManager();
    M2Share.CastleManager = new CastleManager();
    M2Share.RandomNumber = RandomNumber.GetInstance();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new System.Collections.ArrayList();
    M2Share.LogonCostLogList = new System.Collections.ArrayList();
    M2Share.g_MonSayMsgList = new Dictionary<string, IList<TMonSayMsg>>();
}

static void PrepareRuntimeConfig()
{
    var runtimeDirectory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(runtimeDirectory, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "String.ini"),
        "[String]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "Command.conf"),
        "[Command]" + Environment.NewLine);
    var shareDirectory = Path.Combine(Path.GetFullPath(
        Path.Combine(runtimeDirectory, "..")), "Share");
    Directory.CreateDirectory(shareDirectory);
    File.WriteAllText(Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"),
        "[Integer]" + Environment.NewLine);
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
    internal List<(ClientPacket Packet, string Body)> SocketMessages { get; } =
        new();

    public bool TryTake(ref TProcessMessage message) => GetMessage(ref message);

    internal override void SendSocket(ClientPacket defMsg, string message)
    {
        SocketMessages.Add((defMsg, message));
    }
}
