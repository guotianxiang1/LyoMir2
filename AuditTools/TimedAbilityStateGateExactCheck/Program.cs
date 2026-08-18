using System.Buffers.Binary;
using System.Reflection;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

PrepareRuntimeConfig();
InitializeRuntime();

if (args.Contains("--state25-only", StringComparer.OrdinalIgnoreCase))
{
    CheckPlayerState25Override();
    CheckSourceOrdering();
    Console.WriteLine("PASS player-state25 gained/lost text+color+type+player-gate+before-3555");
    return;
}

CheckNativeStateCarrier();
CheckState20AndLegacyWordAuthority();
CheckSupportedPasMapping();
CheckGateProducerClosure();
CheckGlobalGateHasNoSideEffects();
CheckValueGate();
CheckPlayerType45SideEffect();
CheckPlayerState25Override();
CheckExpiryRemovalAndExit();
CheckType12NativeDoubling();
CheckSourceOrdering();

    Console.WriteLine(
    "PASS timed-ability state=0..111+16B-LE state20=timed-carrier legacy21..31=authority " +
    "mapping=27->59+43->75+44->76+45->77+61->93+62->94+64->96+68->100 gate52=zero-side-effects gate16=45/53-only " +
    "player45=visible-3415+657 hero45=no-hook player-state25=gain/lost-exact expiry=two-phase-oldest-first " +
    "exit=transient-clear type12=native-double producers16/52=NO-GO(scope-closed)");
return;

static void CheckNativeStateCarrier()
{
    foreach (var state in new[] { 0, 19, 20, 31, 32, 63, 64, 95, 96, 111 })
    {
        var actor = new TBaseObject();
        Assert(actor.SetNativeActiveState(state), $"state {state} set");
        Assert(actor.HasNativeActiveState(state), $"state {state} read");
        Assert(!actor.SetNativeActiveState(state), $"state {state} duplicate set");
        Assert(actor.ClearNativeActiveState(state), $"state {state} clear");
        Assert(!actor.HasNativeActiveState(state), $"state {state} cleared read");
    }

    var bounds = new TBaseObject();
    Assert(!bounds.SetNativeActiveState(-1), "state -1 rejected");
    Assert(!bounds.SetNativeActiveState(112), "state 112 rejected");
    Assert(!bounds.ClearNativeActiveState(-1), "clear -1 rejected");
    Assert(!bounds.ClearNativeActiveState(112), "clear 112 rejected");
    Assert(!bounds.HasNativeActiveState(-1), "read -1 rejected");
    Assert(!bounds.HasNativeActiveState(112), "read 112 rejected");

    var wire = new TBaseObject();
    foreach (var state in new[]
             {
                 0, 16, 19, 20, 31,
                 32, 45, 52, 63,
                 64, 95,
                 96, 106, 111
             })
    {
        Assert(wire.SetNativeActiveState(state), $"wire state {state}");
    }

    var body = wire.GetBodyStateBuffer();
    Equal(16, body.Length, "body-state byte length");
    Equal(0x80190001u,
        BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(0, 4)),
        "body-state little-endian word 0");
    Equal(0x80102001u,
        BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(4, 4)),
        "body-state little-endian word 1");
    Equal(0x80000001u,
        BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(8, 4)),
        "body-state little-endian word 2");
    Equal(0x00008401u,
        BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(12, 4)),
        "body-state little-endian word 3");
}

static void CheckState20AndLegacyWordAuthority()
{
    var actor = new TBaseObject();
    Assert(actor.SetNativeActiveState(16), "persistent low state set");
    Assert(actor.SetNativeActiveState(32), "second word state set");

    Assert(actor.SetNativeActiveState(20), "state20 timed carrier set");
    actor.m_nCharStatus = actor.GetCharStatus();
    Assert(actor.HasNativeActiveState(20),
        "state20 timed carrier lost during status rebuild");

    // Legacy slot 11 IS native state 20 (slot i = state 31 - i). Writing the
    // slot must land on the one authority, not on a parallel one.
    actor.m_wStatusTimeArr[11] = 1;
    actor.m_nCharStatus = actor.GetCharStatus();
    Assert(actor.HasNativeActiveState(20),
        "slot11 write did not reach native state 20");
    Equal((ushort)1, actor.m_wStatusTimeArr[11],
        "slot11 read-back after write");

    // ...and clearing the state must actually clear it. This block used to
    // assert the opposite - that GetCharStatus would resurrect the bit from the
    // legacy overlay on the next rebuild - which is the 4.18 dual authority
    // written down as a contract. Native has no second carrier to resurrect
    // from: RemoveState @0x7731F2 does `btr [esi+0x168]` before it even walks
    // the list, and FindState @0x773BB1 gates on `bt` first, so a record whose
    // bit is clear is invisible.
    Assert(actor.ClearNativeActiveState(20), "state20 carrier clear");
    actor.m_nCharStatus = actor.GetCharStatus();
    Assert(!actor.HasNativeActiveState(20),
        "GetCharStatus resurrected a cleared state - the legacy overlay is back");
    Equal((ushort)0, actor.m_wStatusTimeArr[11],
        "slot11 still reports time for a state whose bit was cleared");

    // Native keeps one flat bitset at [obj+0x168] and reaches it through three
    // primitives that share a single domain guard and split ownership nowhere:
    //   test   0x772960  80 FA 6F              cmp dl, 0x6F
    //          0x772963  77 0A                 ja
    //          0x772965  83 E2 7F              and edx, 0x7F
    //          0x772968  0F A3 90 68 01 00 00  bt  dword [eax+0x168], edx
    //   set    0x772993  80 FB 6F / 77 0A / 83 E3 7F
    //          0x77299B  0F AB 9E 68 01 00 00  bts dword [esi+0x168], ebx
    //   clear  0x7729B1  80 FB 6F / 77 0A / 83 E3 7F
    //          0x7729B9  0F B3 9E 68 01 00 00  btr dword [esi+0x168], ebx
    // Sweeping the function turns up no comparison against 20 or 21 at all, so a
    // state above 20 is exactly as durable as one below it and must survive the
    // rebuild. This assertion used to demand the opposite, pinning a
    // `stateIndex <= 20` fork that native does not have; it now fails if that
    // fork comes back.
    Assert(actor.SetNativeActiveState(21), "raw legacy state 21 set");
    actor.m_nCharStatus = actor.GetCharStatus();
    Assert(actor.HasNativeActiveState(21),
        "state 21 dropped by the rebuild - the invented stateIndex<=20 fork is back");
    Assert(actor.HasNativeActiveState(16),
        "legacy rebuild lost state 0..19 authority");
    Assert(actor.HasNativeActiveState(32),
        "legacy rebuild changed another state word");
}

static void CheckSupportedPasMapping()
{
    var mapping = new (int Script, int Internal)[]
    {
        (0, 32), (1, 33), (2, 34), (4, 36), (5, 37),
        (6, 38), (7, 39), (8, 40), (9, 41), (12, 44),
        (13, 45), (17, 49), (27, 59), (43, 75), (44, 76), (45, 77),
        (59, 91), (60, 92),
        (61, 93), (62, 94), (64, 96), (68, 100)
    };

    foreach (var (script, internalType) in mapping)
    {
        var player = NewPlayer($"mapping-{script}");
        var bridge = new PasApiBridge { CurrentPlayer = player };
        Assert(bridge.CallPlayerMethod("AddPlayerAbil", Values(script, 7, 30)),
            $"supported PAS type {script} dispatch");
        Assert(player.HasTimedAbility(script),
            $"supported PAS type {script} node");
        Assert(player.HasNativeActiveState(internalType),
            $"script {script} -> internal {internalType}");
        Equal(1, CountTimedNodes(player), $"script {script} node count");
        Assert(player.RemoveTimedAbility(script),
            $"supported PAS type {script} remove");
        Assert(!player.HasNativeActiveState(internalType),
            $"script {script} state removed");
    }

    var supported = mapping.Select(entry => entry.Script).ToHashSet();
    var nativeTypes = Enumerable.Range(0, 29)
        .Concat(Enumerable.Range(43, 4))
        .Concat(Enumerable.Range(58, 5))
        .Concat(Enumerable.Range(64, 6))
        .Append(74);
    foreach (var script in nativeTypes.Where(type => !supported.Contains(type)))
    {
        var player = NewPlayer($"closed-{script}");
        var bridge = new PasApiBridge { CurrentPlayer = player };
        Assert(!bridge.CallPlayerMethod("AddPlayerAbil", Values(script, 7, 30)),
            $"unsupported native PAS type {script} remained closed");
        Equal(0, CountTimedNodes(player),
            $"unsupported native PAS type {script} node count");
        Assert(player.GetBodyStateBuffer().All(value => value == 0),
            $"unsupported native PAS type {script} state mutation");
    }
}

static void CheckGateProducerClosure()
{
    var player = NewPlayer("gate-producer-closure");
    var bridge = new PasApiBridge { CurrentPlayer = player };

    Assert(!bridge.CallPlayerMethod("AddPlayerAbil", Values(20, 5, 30)),
        "script20 unexpectedly opened internal52 producer");
    Assert(!player.HasNativeActiveState(52),
        "closed script20 produced state52");
    Equal(0, CountTimedNodes(player),
        "closed script20 produced a timed node");

    player.AddTimedAbility(-16, 5, 30);
    Assert(!player.HasNativeActiveState(16),
        "negative script type unexpectedly produced internal16");
    Equal(0, CountTimedNodes(player),
        "negative script type produced an internal16 node");

    // Current PAS types map to internal 32+, so the value-16 gate and state52
    // blocker are core-compatible but intentionally have no gameplay producer.
}

static void CheckGlobalGateHasNoSideEffects()
{
    var player = NewPlayer("global-gate");
    player.AddTimedAbility(0, 10, 30);
    Assert(player.SetNativeActiveState(52), "global gate state set");
    SetField(player, "m_boNativeHorseCallPending", true);
    SetField(player, "m_dwNativeHorseCallTick", 123u);
    SetField(player, "m_wNativeHorseCallDelay", (ushort)456);
    player.m_DefMsg = null;

    var body = player.GetBodyStateBuffer();
    var nodes = TimedNodeSnapshot(player);
    var processTick = GetField<int>(player, "m_TimedAbilityProcessTick");
    var dirty = GetField<bool>(player, "m_boAbilityRecalcPending");
    var queued = player.m_MsgList.Count;

    player.AddTimedAbility(0, 5, 99);
    player.AddTimedAbility(0, 10, 99);
    player.AddTimedAbility(0, 99, 99);
    player.AddTimedAbility(1, 99, 99);
    player.AddTimedAbility(13, 99, 99);

    Assert(body.SequenceEqual(player.GetBodyStateBuffer()),
        "state52 blocked call changed body state");
    Equal(nodes, TimedNodeSnapshot(player),
        "state52 blocked call changed node list");
    Equal(processTick, GetField<int>(player, "m_TimedAbilityProcessTick"),
        "state52 blocked call changed process tick");
    Equal(dirty, GetField<bool>(player, "m_boAbilityRecalcPending"),
        "state52 blocked call changed dirty flag");
    Equal(queued, player.m_MsgList.Count,
        "state52 blocked call queued a message");
    Assert(player.m_DefMsg == null,
        "state52 blocked call emitted a client header");
    Assert(GetField<bool>(player, "m_boNativeHorseCallPending"),
        "state52 blocked call cleared player pending state");
    Equal(123u, GetField<uint>(player, "m_dwNativeHorseCallTick"),
        "state52 blocked call changed pending tick");
    Equal((ushort)456,
        GetField<ushort>(player, "m_wNativeHorseCallDelay"),
        "state52 blocked call changed pending delay");

    foreach (var internalType in new byte[] { 32, 45, 52, 53, 91, 111 })
        Assert(!CanAdd(player, internalType),
            $"state52 did not block internal {internalType}");

    Assert(player.ClearNativeActiveState(52), "global gate state clear");
    player.AddTimedAbility(1, 1, 1);
    Assert(player.HasNativeActiveState(33),
        "cleared global gate did not admit supported type");
}

static void CheckValueGate()
{
    var stateOnly = new TBaseObject();
    Assert(stateOnly.SetNativeActiveState(16), "state16 without node set");
    stateOnly.AddTimedAbility(13, 1, 10);
    Assert(stateOnly.HasNativeActiveState(45),
        "state16 without a node did not read value zero");

    var below = new TBaseObject();
    InjectNativeTimedNode(below, 16, 4);
    Assert(below.SetNativeActiveState(16), "state16 value4 set");
    Assert(CanAdd(below, 45), "state16 value4 blocked internal45");
    Assert(CanAdd(below, 53), "state16 value4 blocked internal53");

    var blocked = NewPlayer("value-gate");
    InjectNativeTimedNode(blocked, 16, 5);
    Assert(blocked.SetNativeActiveState(16), "state16 value5 set");
    SetField(blocked, "m_boNativeHorseCallPending", true);
    SetField(blocked, "m_dwNativeHorseCallTick", 7u);
    SetField(blocked, "m_wNativeHorseCallDelay", (ushort)8);
    blocked.m_DefMsg = null;
    var before = TimedNodeSnapshot(blocked);
    blocked.AddTimedAbility(13, 1, 10);
    Equal(before, TimedNodeSnapshot(blocked),
        "state16 value5 admitted internal45");
    Assert(!blocked.HasNativeActiveState(45),
        "state16 value5 set internal45 bit");
    Assert(GetField<bool>(blocked, "m_boNativeHorseCallPending"),
        "state16 value5 cleared player pending state");
    Assert(blocked.m_DefMsg == null,
        "state16 value5 emitted player side effect");

    Assert(!CanAdd(blocked, 45), "state16 value5 did not block internal45");
    Assert(!CanAdd(blocked, 53), "state16 value5 did not block internal53");
    Assert(CanAdd(blocked, 32), "state16 value5 blocked internal32");
    Assert(CanAdd(blocked, 52), "state16 value5 blocked internal52");
    Assert(CanAdd(blocked, 111), "state16 value5 blocked internal111");
    Assert(!CanAdd(blocked, 112), "out-of-range internal112 admitted");

    blocked.AddTimedAbility(0, 2, 10);
    Assert(blocked.HasNativeActiveState(32),
        "state16 value5 blocked unrelated supported type");
}

static void CheckPlayerType45SideEffect()
{
    // 只数 3415，不数整条队列：state 45 的 gained 臂本来就要对自己发一句
    // SysMsg，而且它不经地图 ——
    //   0x00741E38  66 b9 ff 38        mov cx,0x38FF
    //   0x00741E3C  ba 88 2e 74 00     mov edx,0x742E88
    //                                  (refcnt FF FF FF FF / len 0C /
    //                                   c4 e3 b1 bb b6 a8 c9 ed c1 cb a3 a1
    //                                   = "你被定身了！")
    //   0x00741E45  ff 93 d4 00 00 00  call [ebx+0xD4]
    // 该臂是后来补齐的，整队列计数于是把它误判成 3415 广播。
    var detached = NewPlayer("type45-detached");
    SetField(detached, "m_boNativeHorseCallPending", true);
    var detachedMessages = CountMessages(detached,
        Grobal2.RM_NATIVE_HORSE_CALL_STOP);
    detached.AddTimedAbility(13, 1, 10);
    Assert(!GetField<bool>(detached, "m_boNativeHorseCallPending"),
        "detached internal45 did not clear pending flag");
    Equal(detachedMessages, CountMessages(detached,
            Grobal2.RM_NATIVE_HORSE_CALL_STOP),
        "detached internal45 broadcast 3415 without an environment");

    var map = NewMap(64, 64);
    var player = Place(map, NewPlayer("type45-source"), 20, 20);
    var observer = Place(map, NewPlayer("type45-observer"), 21, 20);
    var outsider = Place(map, NewPlayer("type45-outsider"), 55, 55);

    var ghost = Place(map, NewPlayer("type45-ghost"), 22, 20);
    ghost.m_boGhost = true;
    SetField(ghost, "m_boNativeHorseCallPending", true);
    var ghostMessages = CountMessages(ghost,
        Grobal2.RM_NATIVE_HORSE_CALL_STOP);
    ghost.AddTimedAbility(13, 1, 10);
    Assert(!GetField<bool>(ghost, "m_boNativeHorseCallPending"),
        "ghost internal45 did not clear pending flag");
    Equal(ghostMessages, CountMessages(ghost,
            Grobal2.RM_NATIVE_HORSE_CALL_STOP),
        "ghost internal45 broadcast 3415");

    SetField(player, "m_boNativeHorseCallPending", true);
    SetField(player, "m_dwNativeHorseCallTick", 0x11223344u);
    SetField(player, "m_wNativeHorseCallDelay", (ushort)0x5566);
    var playerHorseMessages = CountMessages(player,
        Grobal2.RM_NATIVE_HORSE_CALL_STOP);
    var observerHorseMessages = CountMessages(observer,
        Grobal2.RM_NATIVE_HORSE_CALL_STOP);
    var outsiderHorseMessages = CountMessages(outsider,
        Grobal2.RM_NATIVE_HORSE_CALL_STOP);
    var playerStatusMessages = CountMessages(player,
        Grobal2.RM_CHARSTATUSCHANGED);
    var observerStatusMessages = CountMessages(observer,
        Grobal2.RM_CHARSTATUSCHANGED);
    var outsiderStatusMessages = CountMessages(outsider,
        Grobal2.RM_CHARSTATUSCHANGED);
    var visibleBeforeBroadcast = new List<TBaseObject>();
    var sourceCellResolved = false;
    var sourceCell = map.GetMapCellInfo(player.m_nCurrX, player.m_nCurrY,
        ref sourceCellResolved);
    Assert(player.GetMapBaseObjects(map, player.m_nCurrX, player.m_nCurrY,
            M2Share.g_Config.nSendRefMsgRange, visibleBeforeBroadcast),
        "type45 visible-object scan failed");
    Assert(visibleBeforeBroadcast.Contains(player)
           && visibleBeforeBroadcast.Contains(observer)
           && !visibleBeforeBroadcast.Contains(outsider),
        "type45 map placement did not match visible range: "
        + string.Join(',', visibleBeforeBroadcast.Select(value => value.m_sCharName))
        + $"; range={M2Share.g_Config.nSendRefMsgRange}; cell={sourceCellResolved}/{sourceCell.Count}; "
        + $"sourceGhost={player.m_boGhost}; sourceDeath={player.m_boDeath}; "
        + $"observerGhost={observer.m_boGhost}; observerDeath={observer.m_boDeath}");
    Equal((byte)Grobal2.RC_PLAYOBJECT, observer.m_btRaceServer,
        "type45 observer race");

    player.AddTimedAbility(13, 10, 10);
    Assert(!GetField<bool>(player, "m_boNativeHorseCallPending"),
        "new internal45 did not clear pending flag");
    Equal(0u, GetField<uint>(player, "m_dwNativeHorseCallTick"),
        "new internal45 did not clear pending tick");
    Equal((ushort)0,
        GetField<ushort>(player, "m_wNativeHorseCallDelay"),
        "new internal45 did not clear pending delay");
    Assert(player.HasNativeActiveState(45), "new internal45 bit not set");
    var visibleCache = GetField<List<TBaseObject>>(player,
        "m_VisibleHumanList");
    Assert(visibleCache.Contains(observer),
        "new internal45 visible cache omitted observer: "
        + string.Join(',', visibleCache.Select(value => value.m_sCharName)));
    Equal(playerHorseMessages + 1, CountMessages(player,
            Grobal2.RM_NATIVE_HORSE_CALL_STOP),
        "new internal45 did not include source in 3415 broadcast");
    Equal(observerHorseMessages + 1, CountMessages(observer,
            Grobal2.RM_NATIVE_HORSE_CALL_STOP),
        "new internal45 did not include visible observer in 3415 broadcast");
    Equal(outsiderHorseMessages, CountMessages(outsider,
            Grobal2.RM_NATIVE_HORSE_CALL_STOP),
        "new internal45 broadcast reached non-visible player");
    Equal(playerStatusMessages + 1, CountMessages(player,
            Grobal2.RM_CHARSTATUSCHANGED),
        "new internal45 did not send source status 657");
    Equal(observerStatusMessages + 1, CountMessages(observer,
            Grobal2.RM_CHARSTATUSCHANGED),
        "new internal45 did not send observer status 657");
    Equal(outsiderStatusMessages, CountMessages(outsider,
            Grobal2.RM_CHARSTATUSCHANGED),
        "new internal45 status 657 reached non-visible player");
    CheckQueuedHorseStop(LastMessage(player,
        Grobal2.RM_NATIVE_HORSE_CALL_STOP), player, "source");
    CheckQueuedHorseStop(LastMessage(observer,
        Grobal2.RM_NATIVE_HORSE_CALL_STOP), player, "observer");

    player.m_DefMsg = null;
    var sourceMessage = TakeQueuedMessage(player,
        Grobal2.RM_NATIVE_HORSE_CALL_STOP, "source queued 3415 take");
    Assert(player.Operate(sourceMessage), "source 3415 RM dispatch");
    CheckPacket(player.m_DefMsg, Grobal2.SM_NATIVE_HORSE_CALL_STOP,
        player.ObjectId, 0, 0, 0, "source 3415 header");

    observer.m_DefMsg = null;
    var observerMessage = TakeQueuedMessage(observer,
        Grobal2.RM_NATIVE_HORSE_CALL_STOP, "observer queued 3415 take");
    Assert(observer.Operate(observerMessage), "observer 3415 RM dispatch");
    CheckPacket(observer.m_DefMsg, Grobal2.SM_NATIVE_HORSE_CALL_STOP,
        player.ObjectId, 0, 0, 0, "observer 3415 header");

    SetField(player, "m_boNativeHorseCallPending", true);
    SetField(player, "m_dwNativeHorseCallTick", 21u);
    SetField(player, "m_wNativeHorseCallDelay", (ushort)22);
    var playerHorseAfterNew = CountMessages(player,
        Grobal2.RM_NATIVE_HORSE_CALL_STOP);
    var observerHorseAfterNew = CountMessages(observer,
        Grobal2.RM_NATIVE_HORSE_CALL_STOP);
    var playerStatusAfterNew = CountMessages(player,
        Grobal2.RM_CHARSTATUSCHANGED);
    var observerStatusAfterNew = CountMessages(observer,
        Grobal2.RM_CHARSTATUSCHANGED);
    player.AddTimedAbility(13, 9, 20);
    Assert(GetField<bool>(player, "m_boNativeHorseCallPending"),
        "lower internal45 refresh cleared pending state");
    Equal(playerHorseAfterNew, CountMessages(player,
            Grobal2.RM_NATIVE_HORSE_CALL_STOP),
        "lower internal45 refresh broadcast 3415");
    Equal(playerStatusAfterNew + 1, CountMessages(player,
            Grobal2.RM_CHARSTATUSCHANGED),
        "lower internal45 refresh did not send status 657");
    player.AddTimedAbility(13, 10, 20);
    Assert(GetField<bool>(player, "m_boNativeHorseCallPending"),
        "equal internal45 refresh cleared pending state");
    Equal(playerHorseAfterNew, CountMessages(player,
            Grobal2.RM_NATIVE_HORSE_CALL_STOP),
        "equal internal45 refresh broadcast 3415");
    Equal(playerStatusAfterNew + 2, CountMessages(player,
            Grobal2.RM_CHARSTATUSCHANGED),
        "equal internal45 refresh did not send status 657");

    player.AddTimedAbility(13, 11, 20);
    Assert(!GetField<bool>(player, "m_boNativeHorseCallPending"),
        "higher internal45 did not clear pending state");
    Equal(playerHorseAfterNew + 1, CountMessages(player,
            Grobal2.RM_NATIVE_HORSE_CALL_STOP),
        "higher internal45 did not broadcast one 3415");
    Equal(observerHorseAfterNew + 1, CountMessages(observer,
            Grobal2.RM_NATIVE_HORSE_CALL_STOP),
        "higher internal45 did not broadcast 3415 to observer");
    Equal(playerStatusAfterNew + 3, CountMessages(player,
            Grobal2.RM_CHARSTATUSCHANGED),
        "higher internal45 did not send source status 657");
    Equal(observerStatusAfterNew + 3, CountMessages(observer,
            Grobal2.RM_CHARSTATUSCHANGED),
        "internal45 refresh status 657 count for observer");
    var playerHorseAfterHigher = CountMessages(player,
        Grobal2.RM_NATIVE_HORSE_CALL_STOP);
    var observerHorseAfterHigher = CountMessages(observer,
        Grobal2.RM_NATIVE_HORSE_CALL_STOP);
    var playerStatusAfterHigher = CountMessages(player,
        Grobal2.RM_CHARSTATUSCHANGED);
    var observerStatusAfterHigher = CountMessages(observer,
        Grobal2.RM_CHARSTATUSCHANGED);
    player.AddTimedAbility(13, 12, 20);
    Equal(playerHorseAfterHigher, CountMessages(player,
            Grobal2.RM_NATIVE_HORSE_CALL_STOP),
        "pending=false higher internal45 broadcast 3415");
    Equal(observerHorseAfterHigher, CountMessages(observer,
            Grobal2.RM_NATIVE_HORSE_CALL_STOP),
        "pending=false higher internal45 broadcast 3415 to observer");
    Equal(playerStatusAfterHigher + 1, CountMessages(player,
            Grobal2.RM_CHARSTATUSCHANGED),
        "pending=false higher internal45 did not send source status 657");
    Equal(observerStatusAfterHigher + 1, CountMessages(observer,
            Grobal2.RM_CHARSTATUSCHANGED),
        "pending=false higher internal45 did not send observer status 657");

    var hero = new HeroObject();
    var heroMessages = hero.m_MsgList.Count;
    hero.AddTimedAbility(13, 1, 10);
    Assert(hero.HasNativeActiveState(45), "hero internal45 bit not set");
    Equal(heroMessages, hero.m_MsgList.Count,
        "hero internal45 acquired player-only side effect");
}

static void CheckPlayerState25Override()
{
    const int duration = 70_000_999;
    const string gainedText = "反外挂惩罚4464秒";
    const string lostText = "反外挂惩罚时间结束";

    var player = NewPlayer("state25-player");
    Assert(AddInternalTimedAbility(player, 25, 7, duration),
        "player state25 add");
    var gained = player.m_MsgList.Single(message =>
        message.wIdent == Grobal2.RM_SYSMESSAGE && message.Buff == gainedText);
    Equal(0, gained.wParam, "state25 gained wParam");
    Equal(0xFF, gained.nParam1, "state25 gained color");
    Equal(0x38, gained.nParam2, "state25 gained type");
    Equal(0, gained.nParam3, "state25 gained nParam3");

    Assert(RemoveInternalTimedAbility(player, 25), "player state25 remove");
    var lost = player.m_MsgList.Single(message =>
        message.wIdent == Grobal2.RM_SYSMESSAGE && message.Buff == lostText);
    Equal(0, lost.wParam, "state25 lost wParam");
    Equal(0xDB, lost.nParam1, "state25 lost color");
    Equal(0xFF, lost.nParam2, "state25 lost type");
    Equal(0, lost.nParam3, "state25 lost nParam3");

    var hero = new HeroObject();
    Assert(AddInternalTimedAbility(hero, 25, 7, duration), "hero state25 add");
    Assert(hero.m_MsgList.All(message =>
            message.Buff != gainedText && message.Buff != lostText),
        "hero received TPlayObject-only state25 text");
    Assert(RemoveInternalTimedAbility(hero, 25), "hero state25 remove");
    Assert(hero.m_MsgList.All(message =>
            message.Buff != gainedText && message.Buff != lostText),
        "hero received TPlayObject-only state25 lost text");
}

static void CheckExpiryRemovalAndExit()
{
    var expiry = new TBaseObject();
    var tick = HUtil32.GetTickCount();
    expiry.ProcessTimedAbilities(tick);
    expiry.AddTimedAbility(0, 1, 1);
    Assert(expiry.HasNativeActiveState(32), "expiry setup bit");
    expiry.ProcessTimedAbilities(unchecked(tick + 1500));
    Assert(!expiry.HasNativeActiveState(32), "expired state bit not cleared");
    Equal(0, CountTimedNodes(expiry), "expired node not unlinked");

    var batch = new ExpiryBatchProbe();
    batch.ProcessTimedAbilities(20_000);
    batch.AddTimedAbility(0, 1, 0);
    batch.AddTimedAbility(43, 1, 0);
    SetTimedNodeLastTicks(batch, 20_000);
    batch.ProcessTimedAbilities(20_500);
    Equal(2, batch.Removals.Count, "two-phase expiry callback count");
    Equal((byte)32, batch.Removals[0].InternalType,
        "two-phase expiry oldest callback");
    Equal((byte)75, batch.Removals[1].InternalType,
        "two-phase expiry newest callback");
    Assert(batch.Removals.All(entry =>
            !entry.Type0Present && !entry.Type43Present),
        "two-phase expiry callback observed an attached expired node");

    var removed = new TBaseObject();
    removed.AddTimedAbility(1, 1, 10);
    Assert(removed.RemoveTimedAbility(1), "explicit remove result");
    Assert(!removed.HasNativeActiveState(33), "removed state bit not cleared");
    Equal(0, CountTimedNodes(removed), "removed node not unlinked");

    var exiting = new TBaseObject();
    exiting.AddTimedAbility(0, 1, 10);
    exiting.AddTimedAbility(17, 1, 10);
    exiting.Disappear();
    Assert(!exiting.HasNativeActiveState(32), "exit retained state32");
    Assert(!exiting.HasNativeActiveState(49), "exit retained state49");
    Equal(0, CountTimedNodes(exiting), "exit retained timed nodes");
    Equal(0, GetField<int>(exiting, "m_TimedAbilityProcessTick"),
        "exit retained process tick");
    Assert(!GetField<bool>(exiting, "m_boAbilityRecalcPending"),
        "exit retained dirty flag");

    var hero = new HeroObject();
    hero.AddTimedAbility(0, 1, 10);
    typeof(HeroObject).GetMethod("ReleaseRuntimeReferences",
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?.Invoke(hero, null);
    Assert(!hero.HasNativeActiveState(32), "hero release retained state32");
    Equal(0, CountTimedNodes(hero), "hero release retained timed nodes");
}

static void CheckType12NativeDoubling()
{
    var player = NewPlayer("type12-double");
    player.m_Abil.MaxWeight = 123;
    player.m_Abil.MaxWearWeight = 234;
    player.m_Abil.MaxHandWeight = 345;
    player.RecalcAbilitys();
    player.AddTimedAbility(12, 999, 10);
    player.ConsumePendingRecalcForCheck();
    Equal(unchecked((ushort)(123 + 123)), player.m_WAbil.MaxWeight,
        "type12 MaxWeight native doubling");
    Equal(unchecked((ushort)(234 + 234)), player.m_WAbil.MaxWearWeight,
        "type12 MaxWearWeight native doubling");
    Equal(unchecked((ushort)(345 + 345)), player.m_WAbil.MaxHandWeight,
        "type12 MaxHandWeight native doubling");
}

static void CheckSourceOrdering()
{
    var root = FindRepoRoot();
    var timed = File.ReadAllText(Path.Combine(root, "GameSvr", "Actors",
        "TBaseObject.TimedAbility.cs"));
    // AddTimedAbilityInternal 已由 private 改成 internal（原生 AddState 是虚槽
    // VMT+0x1EC @0x7730D0，直接调用点遍布引擎，子类需要能直接调），标记串因此
    // 不能再钉可见性。
    var add = Between(timed, "bool AddTimedAbilityInternal(",
        "internal bool AddNativeBubbleTimedAbility");
    Before(add, "CanAddNativeTimedAbility(internalType)",
        "FindTimedAbilityInternal(internalType)", "gate before node lookup");
    Before(add, "CanAddNativeTimedAbility(internalType)",
        "node.Flag = newNodeFlag", "gate before refresh mutation");
    Before(add, "CanAddNativeTimedAbility(internalType)",
        "m_TimedAbilityHead = node", "gate before list mutation");
    Before(add, "CancelNativeType51PendingForTimedAbility()",
        "SetNativeActiveState(node.InternalType)",
        "player internal45 side effect before bit set");
    Before(add, "SetNativeActiveState(node.InternalType)",
        "MarkAbilityRecalcPending()", "bit set before dirty flag");
    Before(add, "SetNativeActiveState(node.InternalType)",
        "SendTimedAbilityState(node, false)", "bit set before state message");

    var changed = Between(add, "if (abilityChanged)",
        "if (abilityChanged && RequiresTimedAbilityRecalc");
    Contains(changed, "CancelNativeType51PendingForTimedAbility()",
        "player hook guarded by new/higher change");
    Contains(changed, "SetNativeActiveState(node.InternalType)",
        "state bit guarded by new/higher change");

    var process = Between(timed, "public void ProcessTimedAbilities",
        "public bool RemoveTimedAbility");
    Before(process, "ClearNativeActiveState(node.InternalType)",
        "node.Next = expiredHead", "expiry clear before temporary-chain link");
    Before(process, "node.Next = expiredHead", "node = expiredHead;",
        "expiry detach pass before callback pass");
    Before(process, "node = expiredHead;", "SendTimedAbilityState(node, true)",
        "all expired nodes detached before the first callback");

    var remove = Between(timed, "public bool RemoveTimedAbility",
        "public bool HasTimedAbility");
    Before(remove, "ClearNativeActiveState(internalType)",
        "m_TimedAbilityHead = node.Next", "remove clear before unlink");
    Before(remove, "ClearNativeActiveState(internalType)",
        "SendTimedAbilityState(node, true)", "remove clear before callback");

    var stateSend = Between(timed, "private void SendTimedAbilityState(",
        "protected virtual void SendTimedAbilityClientState");
    Contains(stateSend, "this is TPlayObject && node.InternalType == 25",
        "player-only state25 override");
    var state25Index = stateSend.IndexOf(
        "this is TPlayObject && node.InternalType == 25", StringComparison.Ordinal);
    var finalClientStateIndex = stateSend.LastIndexOf(
        "SendTimedAbilityClientState(node.InternalType", StringComparison.Ordinal);
    Assert(finalClientStateIndex > state25Index,
        "state25 override before 3555 record");

    var type12 = Between(timed, "case 12:", "case 59:");
    var compactType12 = Compact(type12);
    Contains(compactType12,
        "AddTimedWord(m_WAbil.MaxWeight,m_WAbil.MaxWeight,ushort.MaxValue)",
        "type12 MaxWeight doubles current capacity");
    Contains(compactType12,
        "AddTimedWord(m_WAbil.MaxWearWeight,m_WAbil.MaxWearWeight,ushort.MaxValue)",
        "type12 MaxWearWeight doubles current capacity");
    Contains(compactType12,
        "AddTimedWord(m_WAbil.MaxHandWeight,m_WAbil.MaxHandWeight,ushort.MaxValue)",
        "type12 MaxHandWeight doubles current capacity");

    var playerTimed = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.NativeTimedAbility.cs"));
    Before(playerTimed, "m_boNativeHorseCallPending = false",
        "m_dwNativeHorseCallTick = 0", "player45 flag before tick clear");
    Before(playerTimed, "m_dwNativeHorseCallTick = 0",
        "m_wNativeHorseCallDelay = 0", "player45 tick before delay clear");
    Before(playerTimed, "m_wNativeHorseCallDelay = 0",
        "SendRefMsg(", "player45 tuple clear before 3415 broadcast");
    Contains(playerTimed, "if (!m_boGhost && m_PEnvir != null)",
        "player45 native sender validity gate");
    NotContains(playerTimed, "SendDefMessage(",
        "player45 bypassed visible broadcast");
    NotContains(playerTimed, "SendDelayMsg(",
        "player45 used delayed internal message");

    var baseSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Actors",
        "TBaseObject.cs"));
    var setBody = Between(baseSource, "public bool SetBodyState",
        "public void AbilCopyToWAbil");
    // The `stateIndex <= 20` fork this used to require is invented. Native reaches
    // one flat bitset at [obj+0x168] through three primitives that share a single
    // domain guard and split ownership nowhere - 0x772960 / 0x772993 / 0x7729B1 all
    // do `cmp ..,0x6F` then `and ..,0x7F` before bt / bts / btr - and the function
    // compares against 20 or 21 at no point. Demanding the fork here preserved an
    // ownership split native does not have, so the inverse is what needs guarding.
    // Keep the `if (` prefix: the region carries a comment explaining why the fork
    // was removed, and the bare phrase matches that prose too.
    NotContains(setBody, "if (stateIndex <= 20)",
        "the invented stateIndex<=20 ownership split is back");
    NotContains(setBody, "m_wStatusTimeArr[",
        "native helper mutated legacy timer authority");
    var disappear = Between(baseSource, "public virtual void Disappear()",
        "public void FeatureChanged()");
    Contains(disappear, "ClearTimedAbilitiesOnExit()",
        "base exit timed-state cleanup");

    var heroSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Actors",
        "HeroObject.cs"));
    var heroRelease = Between(heroSource, "internal void ReleaseRuntimeReferences()",
        "private static int NativeGridDistance");
    Contains(heroRelease, "ClearTimedAbilitiesOnExit()",
        "hero release timed-state cleanup");

    var messageSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.Message.cs"));
    var horseCase = Between(messageSource,
        "case Grobal2.RM_NATIVE_HORSE_CALL_STOP:", "case Grobal2.RM_41:");
    Contains(horseCase, "Grobal2.SM_NATIVE_HORSE_CALL_STOP",
        "3415 RM to SM mapping");
    Contains(horseCase, "ProcessMsg.BaseObject, 0, 0, 0",
        "3415 source object header");
}

static void CheckQueuedHorseStop(SendMessage message, TPlayObject source,
    string label)
{
    Equal(Grobal2.RM_NATIVE_HORSE_CALL_STOP, message.wIdent,
        label + " queued RM ident");
    Assert(ReferenceEquals(source, message.BaseObject),
        label + " queued RM source");
    Equal(0, message.wParam, label + " queued RM wParam");
    Equal(0, message.nParam1, label + " queued RM nParam1");
    Equal(0, message.nParam2, label + " queued RM nParam2");
    Equal(0, message.nParam3, label + " queued RM nParam3");
}

static int CountMessages(TBaseObject actor, int ident) =>
    actor.m_MsgList.Count(message => message.wIdent == ident);

static SendMessage LastMessage(TBaseObject actor, int ident) =>
    actor.m_MsgList.Last(message => message.wIdent == ident);

static TProcessMessage TakeQueuedMessage(ProbePlayer player, int ident,
    string label)
{
    TProcessMessage message = null;
    while (player.TryTake(ref message))
    {
        if (message.wIdent == ident)
            return message;
    }

    throw new InvalidOperationException(label);
}

static void CheckPacket(ClientPacket packet, int ident, int recog, int param,
    int tag, int series, string label)
{
    Assert(packet != null, label + " packet");
    Equal(unchecked((ushort)ident), packet.Ident, label + " ident");
    Equal(recog, packet.Recog, label + " recog");
    Equal(unchecked((ushort)param), packet.Param, label + " param");
    Equal(unchecked((ushort)tag), packet.Tag, label + " tag");
    Equal(unchecked((ushort)series), packet.Series, label + " series");
}

static Envirnoment NewMap(short width, short height)
{
    var map = new Envirnoment { sMapName = "timed-ability-audit" };
    var initialize = typeof(Envirnoment).GetMethod("Initialize",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("Envirnoment.Initialize");
    initialize.Invoke(map, new object[] { width, height });
    return map;
}

static ProbePlayer Place(Envirnoment map, ProbePlayer player, short x, short y)
{
    player.m_PEnvir = map;
    player.m_nCurrX = x;
    player.m_nCurrY = y;
    player.m_boFixedHideMode = false;
    // MoveToMovingObject is a move, not a placement. It now refuses when the actor
    // is not already registered in the source cell, and this fixture never added
    // the player to the map at all, so the self-move it used to perform returned 0.
    // AddToMap is the placement primitive and echoes the actor back on success.
    Assert(ReferenceEquals(player, map.AddToMap(x, y, CellType.OS_MOVINGOBJECT, player)),
        $"place {player.m_sCharName}");
    return player;
}

static ProbePlayer NewPlayer(string name) => new()
{
    m_boOffLineFlag = true,
    m_sCharName = name
};

static bool CanAdd(TBaseObject actor, byte internalType)
{
    var method = typeof(TBaseObject).GetMethod("CanAddNativeTimedAbility",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("CanAddNativeTimedAbility");
    return (bool)(method.Invoke(actor, new object[] { internalType }) ?? false);
}

static bool AddInternalTimedAbility(TBaseObject actor, byte internalType,
    int value, int duration)
{
    var method = typeof(TBaseObject).GetMethod("AddTimedAbilityInternal",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("AddTimedAbilityInternal");
    return (bool)(method.Invoke(actor,
        new object[] { internalType, value, duration, (byte)0 }) ?? false);
}

static bool RemoveInternalTimedAbility(TBaseObject actor, byte internalType)
{
    var method = typeof(TBaseObject).GetMethod("RemoveTimedAbilityInternal",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("RemoveTimedAbilityInternal");
    return (bool)(method.Invoke(actor, new object[] { internalType }) ?? false);
}

static void InjectNativeTimedNode(TBaseObject actor, byte internalType, int value)
{
    var nodeType = typeof(TBaseObject).GetNestedType("TimedAbilityNode",
        BindingFlags.NonPublic) ?? throw new MissingMemberException("TimedAbilityNode");
    var node = Activator.CreateInstance(nodeType, nonPublic: true)
        ?? throw new InvalidOperationException("timed node allocation failed");
    NodeField(nodeType, "InternalType").SetValue(node, internalType);
    NodeField(nodeType, "RemainingMilliseconds").SetValue(node, -1);
    NodeField(nodeType, "LastTick").SetValue(node, HUtil32.GetTickCount());
    NodeField(nodeType, "Value").SetValue(node, value);
    NodeField(nodeType, "Next").SetValue(node, GetField<object>(actor,
        "m_TimedAbilityHead"));
    SetField(actor, "m_TimedAbilityHead", node);
}

static void SetTimedNodeLastTicks(TBaseObject actor, int tick)
{
    var node = GetField<object>(actor, "m_TimedAbilityHead");
    while (node != null)
    {
        var type = node.GetType();
        NodeField(type, "LastTick").SetValue(node, tick);
        node = NodeField(type, "Next").GetValue(node);
    }
}

static int CountTimedNodes(TBaseObject actor)
{
    var count = 0;
    var node = GetField<object>(actor, "m_TimedAbilityHead");
    while (node != null)
    {
        count++;
        node = NodeField(node.GetType(), "Next").GetValue(node);
    }
    return count;
}

static string TimedNodeSnapshot(TBaseObject actor)
{
    var values = new List<string>();
    var node = GetField<object>(actor, "m_TimedAbilityHead");
    while (node != null)
    {
        var type = node.GetType();
        values.Add(string.Join(':',
            NodeField(type, "Flag").GetValue(node),
            NodeField(type, "InternalType").GetValue(node),
            NodeField(type, "RemainingMilliseconds").GetValue(node),
            NodeField(type, "LastTick").GetValue(node),
            NodeField(type, "Value").GetValue(node)));
        node = NodeField(type, "Next").GetValue(node);
    }
    return string.Join('|', values);
}

static FieldInfo NodeField(Type type, string name) =>
    type.GetField(name, BindingFlags.Instance | BindingFlags.Public |
                        BindingFlags.NonPublic)
    ?? throw new MissingFieldException(type.FullName, name);

static FieldInfo FindField(Type type, string name)
{
    for (var current = type; current != null; current = current.BaseType)
    {
        var field = current.GetField(name, BindingFlags.Instance |
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.DeclaredOnly);
        if (field != null)
            return field;
    }
    throw new MissingFieldException(type.FullName, name);
}

static T GetField<T>(object target, string name)
{
    var value = FindField(target.GetType(), name).GetValue(target);
    return value == null ? default : (T)value;
}

static void SetField(object target, string name, object value) =>
    FindField(target.GetType(), name).SetValue(target, value);

static List<PasValue> Values(params int[] values) =>
    values.Select(PasValue.FromInt).ToList();

static string Between(string source, string startText, string endText)
{
    var start = source.IndexOf(startText, StringComparison.Ordinal);
    var end = source.IndexOf(endText, start + Math.Max(startText.Length, 0),
        StringComparison.Ordinal);
    Assert(start >= 0 && end > start, startText + " source block");
    return source[start..end];
}

static void Before(string source, string first, string second, string label)
{
    var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
    var secondIndex = source.IndexOf(second, StringComparison.Ordinal);
    Assert(firstIndex >= 0 && secondIndex > firstIndex, label);
}

static void Contains(string source, string value, string label) =>
    Assert(source.Contains(value, StringComparison.Ordinal), label);

static void NotContains(string source, string value, string label) =>
    Assert(!source.Contains(value, StringComparison.Ordinal), label);

static string Compact(string source) =>
    string.Concat(source.Where(value => !char.IsWhiteSpace(value)));

static string FindRepoRoot() => AuditRepoRoot.Resolve();

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
    if (!condition)
        throw new InvalidOperationException(label);
}

sealed class ProbePlayer : TPlayObject
{
    public void ConsumePendingRecalcForCheck() => ConsumeAbilityRecalcPending();

    public bool TryTake(ref TProcessMessage message) => GetMessage(ref message);
}

sealed class ExpiryBatchProbe : TPlayObject
{
    public List<(byte InternalType, bool Type0Present, bool Type43Present)>
        Removals { get; } = new();

    protected override void SendTimedAbilityClientState(byte internalType,
        int remainingMilliseconds, int value, bool removed)
    {
        if (removed)
        {
            Removals.Add((internalType, HasTimedAbility(0),
                HasTimedAbility(43)));
        }
    }
}
