// Native walk/run/heading/occupancy/teleport-cooldown contracts.
//
// Walk/run handlers (0x6D9BD0 / 0x6D9CE4 / 0x6D9D99) contain no GetTickCount
// compare and no 600 ms immediate. +0x6C is written on arrival only
// (0x6BBD50 / 0x6BC097 / 0x6BC1AF) and is read by motaebo's unsigned 500 ms
// jbe, not by the walk/run path. C#'s dwWalkIntervalTime/dwRunIntervalTime
// 600 ms gate is MOVE-20 INVENTED and is deliberately not asserted as native.
//
// Heading: walk/run call sub_764A90 (sign buckets). Skill 68 calls
// sub_764BC4 (ratio). Mixing them flattens off-axis headings.
// Occupancy: walk/run use MoveToMovingObject / IsNativeCellBlocking.
// GetMovObjCount (0x778858) is only on skill 168/266, then mover 6A 01.

using System.Reflection;
using GameSvr;
using SystemModule;

PrepareRuntimeConfig();
InitializeRuntime();

CheckSignHeadingIsSub764A90();
CheckRatioHeadingDisagreesOnOffAxis();
CheckMotaeboAndUserMoveUnsignedWrap();
CheckGetMovObjCountCountsObModeWalkDoesNot();
CheckThroughOccupancyDefaultRange();
CheckThroughOccupancyPredicate();
CheckThroughOccupancyTickTransition();
CheckDuplicateOccupancyPushGates();
CheckNativeCellOrderEligibility();
CheckNativeAddToMapChain();
CheckNativeMoveNodeTransfer();
CheckNativeRelocateNodeTransfer();
CheckWalkRunFailDoesNotKick();

Console.WriteLine(
    "NativeMoveGateCheck PASS " +
    "heading=764A90-sign/764BC4-ratio-disagree " +
    "wrap=motaebo-500/usermove-10000-unsigned " +
    "occupancy=GetMovObjCount-counts-ObMode " +
    "through-predicate=default9/grant/nothrough/polygon/redhome/startpoint " +
    "through-tick=change-only-2821 " +
    "duplicate-push=uint-boundaries/through/order/state33/state34 " +
    "cell-order=oldest/death-boundaries/pickup " +
    "addtomap=head/dedupe/scavenge/alive-seconds/no-five-cap " +
    "move-node=identity/type/stamp/atomicity " +
    "relocate-node=unchecked/blocked/same-cell/missing " +
    "overspeed=walk/run-no-kick");

static void CheckSignHeadingIsSub764A90()
{
    Equal(Grobal2.DR_UP, M2Share.GetNextDirection(5, 5, 5, 5),
        "same cell: 0x764BB6 xor eax,eax = DR_UP");
    Equal(Grobal2.DR_UP, M2Share.GetNextDirection(5, 5, 5, 4),
        "due north");
    Equal(Grobal2.DR_RIGHT, M2Share.GetNextDirection(5, 5, 6, 5),
        "due east");
    Equal(Grobal2.DR_DOWNRIGHT, M2Share.GetNextDirection(5, 5, 6, 6),
        "south-east diagonal");

    // MOVE-47: Y suppressor upper bound is `jge` (strict < dy+1).
    // sx far, sy == dy+1 must KEEP flagy, not flatten to horizontal.
    Equal(Grobal2.DR_UPRIGHT, M2Share.GetNextDirection(0, 2, 10, 1),
        "Y suppressor must not flatten sy==dy+1 into DR_RIGHT");
}

static void CheckRatioHeadingDisagreesOnOffAxis()
{
    Equal(Grobal2.DR_UP, M2Share.GetNextDirectionByRatio(5, 5, 5, 5),
        "ratio same cell: 0x764BDC xor eax,eax = DR_UP");

    // deltaX=10, deltaY=sy-ty=0-1=-1 → ratio ≈ -0.01, dx>0 → DR_RIGHT.
    // Sign helper on the same points is DR_DOWNRIGHT.
    Equal(Grobal2.DR_RIGHT, M2Share.GetNextDirectionByRatio(0, 0, 10, 1),
        "ratio shallow east: 0x764C40 jbe skip / mov al,2");
    Equal(Grobal2.DR_DOWNRIGHT, M2Share.GetNextDirection(0, 0, 10, 1),
        "sign helper on the same vector is dir 3, not 2");
    Assert(M2Share.GetNextDirection(0, 0, 10, 1) !=
           M2Share.GetNextDirectionByRatio(0, 0, 10, 1),
        "the two helpers must disagree on this off-axis vector");
}

static void CheckMotaeboAndUserMoveUnsignedWrap()
{
    Assert(TPlayObject.IsNativeMotaeboTimingReady(10, 10 - 4501, 20),
        "motaebo 500 ms gate wraps unsigned (jbe at 0x6BC954)");
    Assert(TPlayObject.HasNativeUserMoveCooldownElapsed(10, 20),
        "UserMove wrap: now=10 prev=20 unsigned elapsed exceeds 10000U");
    Assert(!TPlayObject.HasNativeUserMoveCooldownElapsed(20, 9),
        "UserMove 10000 ms: elapsed 11 is not > 10000");
    Assert(TPlayObject.HasNativeUserMoveCooldownElapsed(10011, 10),
        "UserMove 10000 ms: 10011-10 = 10001 > 10000U");
    Assert(!TPlayObject.HasNativeUserMoveCooldownElapsed(10010, 10),
        "UserMove 10000 ms: equal 10000 is NOT elapsed (`>` at 0x6CE452)");
}

static void CheckGetMovObjCountCountsObModeWalkDoesNot()
{
    var environment = NewMap();
    var occupant = NewObject(environment, Grobal2.RC_PLAYOBJECT, 2, 2);
    occupant.bo2B9 = true;
    occupant.m_boObMode = true;
    Place(environment, occupant);

    Equal(1, environment.GetNativeMovObjCount(2, 2),
        "0x778858 does not exclude ObMode; skill 168/266 still see the body");
    Equal(0, environment.GetXYObjCount(2, 2),
        "walk/run occupancy uses IsNativeCellBlocking, which grants ObMode pass-through");
}

static void CheckThroughOccupancyTickTransition()
{
    var environment = NewMap();
    var player = PlacePlayer(environment, NewPlayer("through-tick"), 5, 5);
    TPlayObject.NativeSafeZoneThroughRange = 0;

    environment.Flag.boSAFE = true;
    player.RunThroughOccupancyTick();
    Equal(true, player.m_boThroughOccupancyCache,
        "0x6B30A3 writes true when sub_768454 changes false->true");
    Equal(1, player.SentPackets.Count,
        "0x6B309C only sends on a changed cache value");
    AssertTransitionPacket(player.SentPackets[0], 1, "true transition");

    player.RunThroughOccupancyTick();
    Equal(1, player.SentPackets.Count,
        "unchanged true cache remains silent");

    environment.Flag.boSAFE = false;
    player.RunThroughOccupancyTick();
    Equal(false, player.m_boThroughOccupancyCache,
        "0x6B30A3 writes false when sub_768454 changes true->false");
    Equal(2, player.SentPackets.Count,
        "false transition sends exactly once");
    AssertTransitionPacket(player.SentPackets[1], 0, "false transition");

    player.RunThroughOccupancyTick();
    Equal(2, player.SentPackets.Count,
        "unchanged false cache remains silent");
}

static void CheckThroughOccupancyDefaultRange()
{
    Equal(9, TPlayObject.NativeSafeZoneThroughRange,
        "0x6FC6DF initializes *off_7D6970 to 9 before the GM command can override it");
}

static void CheckDuplicateOccupancyPushGates()
{
    var player = NewPlayer("duplicate-push");

    player.m_dwCheckDupObjTick = 1000;
    Equal(false, player.BeginDuplicateOccupancyPoll(4000),
        "outer duplicate poll rejects exactly 3000 elapsed");
    Equal(1000, player.m_dwCheckDupObjTick,
        "rejected outer poll does not update its timestamp");
    Equal(true, player.BeginDuplicateOccupancyPoll(4001),
        "outer duplicate poll admits 3001 elapsed");
    Equal(4001, player.m_dwCheckDupObjTick,
        "accepted outer poll stores the same Run tick");

    var wrapStart = unchecked((int)0xFFFFFF00u);
    player.m_dwCheckDupObjTick = wrapStart;
    Equal(false, player.BeginDuplicateOccupancyPoll(
            unchecked(wrapStart + 3000)),
        "outer duplicate poll preserves the uint boundary across wrap");
    Equal(true, player.BeginDuplicateOccupancyPoll(
            unchecked(wrapStart + 3001)),
        "outer duplicate poll admits 3001 across wrap");

    player.bo2F0 = false;
    player.m_dwDupObjTick = 123;
    Equal(0u, player.UpdateDuplicateOccupancyLatch(5000, 2),
        "first duplicate observation starts elapsed at zero");
    Equal(true, player.bo2F0,
        "first duplicate observation sets the latch");
    Equal(5000, player.m_dwDupObjTick,
        "first duplicate observation stores the same Run tick");
    Equal(4001u, player.UpdateDuplicateOccupancyLatch(9001, 3),
        "continued duplicate occupancy advances from the original tick");
    Equal(5000, player.m_dwDupObjTick,
        "continued duplicate occupancy does not refresh the latch tick");
    Equal(4002u, player.UpdateDuplicateOccupancyLatch(9002, 1),
        "count below two still computes elapsed after clearing the latch");
    Equal(false, player.bo2F0,
        "count below two clears the duplicate latch");

    player.bo2F0 = true;

    Equal(false, player.CanAutoPushDuplicate(3, 3000u),
        "count>=3 requires unsigned elapsed strictly greater than 3000");
    Equal(true, player.CanAutoPushDuplicate(3, 3001u),
        "count>=3 admits elapsed 3001");
    Equal(false, player.CanAutoPushDuplicate(2, 10000u),
        "count==2 requires unsigned elapsed strictly greater than 10000");
    Equal(true, player.CanAutoPushDuplicate(2, 10001u),
        "count==2 admits elapsed 10001");
    Equal(true, player.CanAutoPushDuplicate(2, 19999u),
        "duplicate push window includes elapsed 19999");
    Equal(false, player.CanAutoPushDuplicate(2, 20000u),
        "0x6B323F jae closes the push window at exactly 20000");
    Equal(false, player.CanAutoPushDuplicate(1, 15000u),
        "a single counted object cannot enter the push branch");

    player.m_boThroughOccupancyCache = true;
    Equal(false, player.CanAutoPushDuplicate(3, 3001u),
        "0x6B3206 nonzero pass-through cache skips auto-push");
    player.m_boThroughOccupancyCache = false;

    player.SetNativeActiveState(0x33);
    Equal(false, player.CanAutoPushDuplicate(3, 3001u),
        "body state 0x33 blocks auto-push");
    player.ClearNativeActiveState(0x33);
    player.SetNativeActiveState(0x34);
    Equal(false, player.CanAutoPushDuplicate(3, 3001u),
        "body state 0x34 blocks auto-push");
    player.ClearNativeActiveState(0x34);

    player.bo2F0 = false;
    Equal(false, player.CanAutoPushDuplicate(3, 3001u),
        "cleared duplicate latch cannot enter auto-push");
}

static void CheckNativeCellOrderEligibility()
{
    const int now = 100_000;

    var ordered = NewMap();
    var oldest = PlacePlayer(ordered, NewPlayer("order-oldest"), 5, 5);
    var newest = PlacePlayer(ordered, NewPlayer("order-newest"), 5, 5);
    Equal(true, ordered.NativeIsOldestEligiblePlayerInCellAtTick(
            oldest, 5, 5, now),
        "sub_77BD34 accepts the oldest eligible player in head order");
    Equal(false, ordered.NativeIsOldestEligiblePlayerInCellAtTick(
            newest, 5, 5, now),
        "sub_77BD34 rejects a newer player with an older live player behind it");

    oldest.bo2F0 = true;
    newest.bo2F0 = true;
    Equal(false, oldest.ShouldAutoPushDuplicate(3, 3001u, now),
        "0x6B3260 skips auto-push for the oldest eligible player");
    Equal(true, newest.ShouldAutoPushDuplicate(3, 3001u, now),
        "0x6B3260 allows a newer eligible player into the push tail");

    var nonPlayerOrder = NewMap();
    var olderMonster = NewObject(nonPlayerOrder, Grobal2.RC_ANIMAL, 5, 5);
    Place(nonPlayerOrder, olderMonster);
    var afterMonster = PlacePlayer(nonPlayerOrder,
        NewPlayer("order-after-monster"), 5, 5);
    Equal(true, nonPlayerOrder.NativeIsOldestEligiblePlayerInCellAtTick(
            afterMonster, 5, 5, now),
        "only race-server zero candidates participate in sub_77BD34");

    var absent = NewPlayer("order-absent");
    absent.m_PEnvir = ordered;
    absent.m_nCurrX = 5;
    absent.m_nCurrY = 5;
    Equal(false, ordered.NativeIsOldestEligiblePlayerInCellAtTick(
            absent, 5, 5, now),
        "a subject absent from the cell chain is rejected");
    Equal(false, ordered.NativeIsOldestEligiblePlayerInCellAtTick(
            oldest, -1, 5, now),
        "negative X is rejected");
    Equal(false, ordered.NativeIsOldestEligiblePlayerInCellAtTick(
            oldest, 5, -1, now),
        "negative Y is rejected");
    Equal(false, ordered.NativeIsOldestEligiblePlayerInCellAtTick(
            oldest, ordered.wWidth - 1, 5, now),
        "the native predicate excludes the last column");
    Equal(false, ordered.NativeIsOldestEligiblePlayerInCellAtTick(
            oldest, 5, ordered.wHeight - 1, now),
        "the native predicate excludes the last row");

    var positiveEdge = NewMap();
    var edgePlayer = PlacePlayer(positiveEdge,
        NewPlayer("order-positive-edge"),
        (short)(positiveEdge.wWidth - 2),
        (short)(positiveEdge.wHeight - 2));
    Equal(true, positiveEdge.NativeIsOldestEligiblePlayerInCellAtTick(
            edgePlayer, positiveEdge.wWidth - 2,
            positiveEdge.wHeight - 2, now),
        "width-2/height-2 is the last accepted native coordinate");

    var nonMovingTargetMap = NewMap();
    var nonMovingTarget = NewPlayer("order-nonmoving-target");
    nonMovingTarget.m_PEnvir = nonMovingTargetMap;
    nonMovingTarget.m_nCurrX = 9;
    nonMovingTarget.m_nCurrY = 9;
    Assert(ReferenceEquals(nonMovingTarget,
            nonMovingTargetMap.AddToMap(9, 9,
                CellType.OS_ITEMOBJECT, nonMovingTarget)),
        "non-moving target node placement");
    Equal(true, nonMovingTargetMap.NativeIsOldestEligiblePlayerInCellAtTick(
            nonMovingTarget, 9, 9, now),
        "sub_77BD34 compares the target payload before the node tag");

    var subjectBoundary = NewMap();
    var subject = PlacePlayer(subjectBoundary,
        NewPlayer("order-dead-subject"), 6, 6);
    subject.m_boDeath = true;
    subject.m_dwDeathTick = now - 9999;
    Equal(true, subjectBoundary.NativeIsOldestEligiblePlayerInCellAtTick(
            subject, 6, 6, now),
        "dead subject remains eligible at age 9999");
    subject.m_dwDeathTick = now - 10000;
    Equal(true, subjectBoundary.NativeIsOldestEligiblePlayerInCellAtTick(
            subject, 6, 6, now),
        "dead subject remains eligible at exactly 10000 (unsigned ja)");
    subject.m_dwDeathTick = now - 10001;
    Equal(false, subjectBoundary.NativeIsOldestEligiblePlayerInCellAtTick(
            subject, 6, 6, now),
        "dead subject is rejected at age 10001");
    subject.m_dwDeathTick = 200;
    Equal(false, subjectBoundary.NativeIsOldestEligiblePlayerInCellAtTick(
            subject, 6, 6, 100),
        "dead subject age uses unsigned subtraction across tick underflow");

    var candidateBoundary = NewMap();
    var olderCandidate = PlacePlayer(candidateBoundary,
        NewPlayer("order-dead-candidate"), 7, 7);
    var candidateSubject = PlacePlayer(candidateBoundary,
        NewPlayer("order-candidate-subject"), 7, 7);
    olderCandidate.m_boDeath = true;
    olderCandidate.m_dwDeathTick = now - 9999;
    Equal(false, candidateBoundary.NativeIsOldestEligiblePlayerInCellAtTick(
            candidateSubject, 7, 7, now),
        "dead older candidate still blocks at age 9999");
    olderCandidate.m_dwDeathTick = now - 10000;
    Equal(true, candidateBoundary.NativeIsOldestEligiblePlayerInCellAtTick(
            candidateSubject, 7, 7, now),
        "dead older candidate is ignored at exactly 10000 (unsigned jae)");
    olderCandidate.m_dwDeathTick = now - 10001;
    Equal(true, candidateBoundary.NativeIsOldestEligiblePlayerInCellAtTick(
            candidateSubject, 7, 7, now),
        "dead older candidate remains ignored at age 10001");
    olderCandidate.m_dwDeathTick = 200;
    Equal(true, candidateBoundary.NativeIsOldestEligiblePlayerInCellAtTick(
            candidateSubject, 7, 7, 100),
        "dead candidate age uses unsigned subtraction across tick underflow");
    olderCandidate.m_boDeath = false;
    olderCandidate.m_boGhost = true;
    Equal(false, candidateBoundary.NativeIsOldestEligiblePlayerInCellAtTick(
            candidateSubject, 7, 7, now),
        "sub_77BD34 reads death byte +0x74, not ghost byte +0x73");

    var pickupEnvironment = NewMap();
    _ = PlacePlayer(pickupEnvironment, NewPlayer("pickup-older"), 8, 8);
    var picker = PlacePlayer(pickupEnvironment,
        NewPlayer("pickup-newer"), 8, 8);
    var expiredOrderOwner = NewObject(pickupEnvironment,
        Grobal2.RC_ANIMAL, 1, 1);
    var mapItem = new MapItem
    {
        Name = "order-probe",
        OfBaseObject = expiredOrderOwner,
        CanPickUpTick = unchecked(HUtil32.GetTickCount() - 120001)
    };
    Assert(ReferenceEquals(mapItem, pickupEnvironment.AddToMap(
            8, 8, CellType.OS_ITEMOBJECT, mapItem)),
        "pickup order probe item placement");
    var pickup = typeof(TPlayObject).GetMethod("ClientPickUpItem",
        BindingFlags.Instance | BindingFlags.NonPublic, null,
        Type.EmptyTypes, null)!;
    var messageCount = picker.m_MsgList.Count;
    Equal(false, (bool)pickup.Invoke(picker, null)!,
        "regular pickup rejects a newer player in the same cell");
    Equal(messageCount + 1, picker.m_MsgList.Count,
        "pickup order rejection emits exactly one message");
    var rejection = picker.m_MsgList[^1];
    Equal(Grobal2.RM_SYSMESSAGE, rejection.wIdent,
        "pickup order rejection packet ident");
    Equal("一定时间范围内，不能拾取。", rejection.Buff,
        "pickup order rejection text at 0x6B7A8C");
    Equal(0xFF, rejection.nParam1,
        "pickup order rejection foreground byte from cx=0x38FF");
    Equal(0x38, rejection.nParam2,
        "pickup order rejection background byte from cx=0x38FF");
    Assert(ReferenceEquals(mapItem, pickupEnvironment.GetItem(8, 8)),
        "pickup order rejection leaves the item on the map");
    Assert(mapItem.OfBaseObject == null,
        "sub_6B794C clears an expired owner before the cell-order predicate");

    var ownerEnvironment = NewMap();
    var ownerPicker = PlacePlayer(ownerEnvironment,
        NewPlayer("pickup-owner-reject"), 10, 10);
    var ownerAllowed = typeof(TPlayObject).GetMethod(
        "ClientPickUpItem_IsOwnerAllowed",
        BindingFlags.Instance | BindingFlags.NonPublic, null,
        new[] { typeof(TBaseObject) }, null)!;
    var ownerExpired = typeof(TPlayObject).GetMethod(
        "ClientPickUpItem_IsOwnerExpiredAtTick",
        BindingFlags.Static | BindingFlags.NonPublic, null,
        new[] { typeof(MapItem), typeof(int) }, null)!;
    bool IsOwnerAllowed(TBaseObject owner) =>
        (bool)ownerAllowed.Invoke(ownerPicker, new object[] { owner })!;
    bool IsOwnerExpired(MapItem item, int currentTick) =>
        (bool)ownerExpired.Invoke(null, new object[] { item, currentTick })!;

    var expiryProbe = new MapItem { CanPickUpTick = 1000 };
    Equal(false, IsOwnerExpired(expiryProbe, 121000),
        "owner remains protected at exactly 120000 ms");
    Equal(true, IsOwnerExpired(expiryProbe, 121001),
        "owner expires at 120001 ms");
    expiryProbe.CanPickUpTick = 200;
    Equal(true, IsOwnerExpired(expiryProbe, 100),
        "owner expiry uses unsigned subtraction across tick underflow");

    var selfPet = NewObject(ownerEnvironment,
        Grobal2.RC_ANIMAL, 1, 1);
    selfPet.m_Master = ownerPicker;
    var teammate = NewPlayer("pickup-teammate");
    ownerPicker.m_GroupOwner = ownerPicker;
    ownerPicker.m_GroupMembers.Add(ownerPicker);
    ownerPicker.m_GroupMembers.Add(teammate);
    teammate.m_GroupOwner = ownerPicker;
    var teammatePet = NewObject(ownerEnvironment,
        Grobal2.RC_ANIMAL, 1, 1);
    teammatePet.m_Master = teammate;
    var teammateNameClone = NewPlayer("PICKUP-TEAMMATE");
    var spouse = NewPlayer("pickup-spouse");
    ownerPicker.m_boMarried = true;
    ownerPicker.m_sDearName = spouse.m_sCharName;
    var spousePet = NewObject(ownerEnvironment,
        Grobal2.RC_ANIMAL, 1, 1);
    spousePet.m_Master = spouse;
    var foreignPlayer = NewPlayer("pickup-foreign");
    var foreignPet = NewObject(ownerEnvironment,
        Grobal2.RC_ANIMAL, 1, 1);
    foreignPet.m_Master = foreignPlayer;
    var ownerlessMonster = NewObject(ownerEnvironment,
        Grobal2.RC_ANIMAL, 1, 1);

    Assert(IsOwnerAllowed(null) && IsOwnerAllowed(ownerPicker) &&
           IsOwnerAllowed(selfPet),
        "owner predicate must accept null, self and self pet");
    Assert(IsOwnerAllowed(teammate) && IsOwnerAllowed(teammatePet) &&
           IsOwnerAllowed(teammateNameClone),
        "owner predicate must accept teammate names and teammate pets");
    Assert(IsOwnerAllowed(spouse) && IsOwnerAllowed(spousePet),
        "owner predicate must accept spouse and spouse pet");
    Assert(!IsOwnerAllowed(foreignPlayer) &&
           !IsOwnerAllowed(foreignPet) &&
           !IsOwnerAllowed(ownerlessMonster),
        "owner predicate must reject unrelated owners");

    var foreignOwner = NewObject(ownerEnvironment,
        Grobal2.RC_ANIMAL, 1, 1);
    var ownedItem = new MapItem
    {
        Name = "owner-probe",
        OfBaseObject = foreignOwner,
        CanPickUpTick = HUtil32.GetTickCount()
    };
    Assert(ReferenceEquals(ownedItem, ownerEnvironment.AddToMap(
            10, 10, CellType.OS_ITEMOBJECT, ownedItem)),
        "owned pickup probe item placement");
    messageCount = ownerPicker.m_MsgList.Count;
    Equal(false, (bool)pickup.Invoke(ownerPicker, null)!,
        "regular pickup rejects a foreign-owned item");
    Equal(messageCount + 1, ownerPicker.m_MsgList.Count,
        "owner rejection emits exactly one message");
    rejection = ownerPicker.m_MsgList[^1];
    Equal("一定时间范围内，不能拾取。", rejection.Buff,
        "owner/group rejection shares the native 0x6B7A8C text");
    Equal(0xFF, rejection.nParam1,
        "owner rejection foreground byte from cx=0x38FF");
    Equal(0x38, rejection.nParam2,
        "owner rejection background byte from cx=0x38FF");
    Assert(ReferenceEquals(ownedItem, ownerEnvironment.GetItem(10, 10)),
        "owner rejection leaves the item on the map");

    ownerPicker.m_nGold = 0;
    ownerPicker.m_nGoldMax = 100;
    var selfOwnedItem = new MapItem
    {
        Name = Grobal2.sSTRING_GOLDNAME,
        Count = 1,
        OfBaseObject = ownerPicker,
        CanPickUpTick = HUtil32.GetTickCount()
    };
    Assert(ReferenceEquals(selfOwnedItem, ownerEnvironment.AddToMap(
            10, 10, CellType.OS_ITEMOBJECT, selfOwnedItem)),
        "self-owned pickup probe item placement");
    pickup.Invoke(ownerPicker, null);
    Equal(1, ownerPicker.m_nGold,
        "regular pickup accepts an item directly owned by self");
    Assert(ownerEnvironment.GetItem(10, 10) != selfOwnedItem,
        "regular self-owned item was not consumed");

    var rangeEnvironment = NewMap();
    rangeEnvironment.Flag.boPICKUP = true;
    _ = PlacePlayer(rangeEnvironment,
        NewPlayer("pickup-range-older"), 11, 11);
    var rangePicker = PlacePlayer(rangeEnvironment,
        NewPlayer("pickup-range-newer"), 11, 11);
    rangePicker.m_nGold = 0;
    rangePicker.m_nGoldMax = 100;
    var rangeTeammate = NewPlayer("pickup-range-teammate");
    rangePicker.m_GroupOwner = rangePicker;
    rangePicker.m_GroupMembers.Add(rangePicker);
    rangePicker.m_GroupMembers.Add(rangeTeammate);
    rangeTeammate.m_GroupOwner = rangePicker;
    var rangeSpouse = NewPlayer("pickup-range-spouse");
    rangePicker.m_boMarried = true;
    rangePicker.m_sDearName = rangeSpouse.m_sCharName;
    var teammateRangeItem = new MapItem
    {
        Name = Grobal2.sSTRING_GOLDNAME,
        Count = 2,
        OfBaseObject = rangeTeammate,
        CanPickUpTick = HUtil32.GetTickCount()
    };
    var spouseRangeItem = new MapItem
    {
        Name = Grobal2.sSTRING_GOLDNAME,
        Count = 4,
        OfBaseObject = rangeSpouse,
        CanPickUpTick = HUtil32.GetTickCount()
    };
    var rangeItem = new MapItem
    {
        Name = Grobal2.sSTRING_GOLDNAME,
        Count = 1,
        OfBaseObject = rangePicker,
        CanPickUpTick = HUtil32.GetTickCount()
    };
    Assert(ReferenceEquals(teammateRangeItem, rangeEnvironment.AddToMap(
            9, 9, CellType.OS_ITEMOBJECT, teammateRangeItem)),
        "range teammate-owned probe item placement");
    Assert(ReferenceEquals(spouseRangeItem, rangeEnvironment.AddToMap(
            9, 10, CellType.OS_ITEMOBJECT, spouseRangeItem)),
        "range spouse-owned probe item placement");
    Assert(ReferenceEquals(rangeItem, rangeEnvironment.AddToMap(
            11, 11, CellType.OS_ITEMOBJECT, rangeItem)),
        "range pickup bypass probe item placement");
    var pickupRange = typeof(TPlayObject).GetMethod("ClientPickUpRange",
        BindingFlags.Instance | BindingFlags.NonPublic)!;
    pickupRange.Invoke(rangePicker, null);
    Equal(1, rangePicker.m_nGold,
        "range pickup accepts direct self owner and bypasses sub_77BD34");
    Assert(rangeEnvironment.GetItem(11, 11) == null,
        "range pickup bypass probe item was not consumed");
    Assert(ReferenceEquals(teammateRangeItem,
               rangeEnvironment.GetItem(9, 9)) &&
           ReferenceEquals(spouseRangeItem,
               rangeEnvironment.GetItem(9, 10)),
        "range pickup must not inherit ordinary teammate/spouse permissions");

    var blinkEnvironment = NewMap();
    var blinkPicker = PlacePlayer(blinkEnvironment,
        NewPlayer("pickup-blink-lock"), 12, 12);
    var blinkItem = new MapItem { Name = "blink-lock-probe" };
    Assert(ReferenceEquals(blinkItem, blinkEnvironment.AddToMap(
            12, 12, CellType.OS_ITEMOBJECT, blinkItem)),
        "blink-lock pickup probe item placement");
    var pickupCore = typeof(TPlayObject).GetMethod("ClientPickUpItem",
        BindingFlags.Instance | BindingFlags.NonPublic, null,
        new[] { typeof(MapItem), typeof(int), typeof(int) }, null)!;
    blinkPicker.m_dwNativeBlinkLandTick = HUtil32.GetTickCount();
    blinkPicker.m_nNativeBlinkLandX = 12;
    blinkPicker.m_nNativeBlinkLandY = 12;
    messageCount = blinkPicker.m_MsgList.Count;
    Equal(false, (bool)pickupCore.Invoke(blinkPicker,
            new object[] { blinkItem, 12, 12 })!,
        "shared pickup core enforces the native blink lock");
    Equal(messageCount + 1, blinkPicker.m_MsgList.Count,
        "blink-lock rejection emits exactly one message");
    rejection = blinkPicker.m_MsgList[^1];
    Equal("一定时间范围内，不能拾取。", rejection.Buff,
        "shared core retains the 0x6B7800 blink-lock text");
    Equal(0xFF, rejection.nParam1,
        "blink-lock foreground byte from cx=0x38FF");
    Equal(0x38, rejection.nParam2,
        "blink-lock background byte from cx=0x38FF");
    Assert(ReferenceEquals(blinkItem, blinkEnvironment.GetItem(12, 12)),
        "blink-lock rejection leaves the item on the map");
}

static void CheckNativeAddToMapChain()
{
    var environment = NewMap();
    var stale = NewObject(environment, Grobal2.RC_PLAYOBJECT, 4, 4);
    stale.m_sCharName = string.Empty;
    Place(environment, stale);

    var first = NewObject(environment, Grobal2.RC_PLAYOBJECT, 4, 4);
    first.m_sCharName = "chain-first";
    Place(environment, first);

    var found = false;
    var cell = environment.GetMapCellInfo(4, 4, ref found);
    Assert(found && cell.Count == 1
           && ReferenceEquals(cell.ObjList[0].CellObj, first),
        "AddToMap must unlink a stale actor before insertion");

    var second = NewObject(environment, Grobal2.RC_PLAYOBJECT, 4, 4);
    second.m_sCharName = "chain-second";
    Place(environment, second);
    cell = environment.GetMapCellInfo(4, 4, ref found);
    Assert(cell.Count == 2
           && ReferenceEquals(cell.ObjList[0].CellObj, second)
           && ReferenceEquals(cell.ObjList[1].CellObj, first),
        "0x777A9C AddToMap insertion must preserve native head order");

    var beforeRefresh = HUtil32.GetTickCount();
    var duplicate = environment.AddToMap(4, 4,
        CellType.OS_MOVINGOBJECT, first);
    var afterRefresh = HUtil32.GetTickCount();
    cell = environment.GetMapCellInfo(4, 4, ref found);
    var refreshed = cell.ObjList[1].dwAddTime;
    Assert(duplicate == null && cell.Count == 2
           && unchecked((uint)(refreshed - beforeRefresh)) <=
              unchecked((uint)(afterRefresh - beforeRefresh)),
        "duplicate AddToMap must refresh time, return nil and not insert");

    var expiring = NewObject(environment, Grobal2.RC_ANIMAL, 6, 6);
    expiring.m_sCharName = "alive-seconds";
    var beforeAlive = HUtil32.GetTickCount();
    var added = environment.AddToMap(6, 6,
        CellType.OS_MOVINGOBJECT, expiring, 1);
    var afterAlive = HUtil32.GetTickCount();
    cell = environment.GetMapCellInfo(6, 6, ref found);
    var aliveStamp = cell.ObjList[0].dwAddTime;
    var lower = unchecked(beforeAlive - 599000);
    var upper = unchecked(afterAlive - 599000);
    Assert(ReferenceEquals(added, expiring)
           && unchecked((uint)(aliveStamp - lower)) <=
              unchecked((uint)(upper - lower)),
        "alive-seconds timestamp must be now + seconds*1000 - 600000");

    var itemEnvironment = NewMap();
    var sameCellItems = new List<MapItem>();
    for (var i = 0; i < 6; i++)
    {
        var item = new MapItem { Name = "same-cell-item-" + i };
        sameCellItems.Add(item);
        Assert(ReferenceEquals(item, itemEnvironment.AddToMap(8, 8,
                CellType.OS_ITEMOBJECT, item)),
            "sub_7776EC has no five-item cap; same-cell item " + i
            + " must be inserted");
    }
    cell = itemEnvironment.GetMapCellInfo(8, 8, ref found);
    Assert(found && cell.Count == 6
           && ReferenceEquals(cell.ObjList[0].CellObj, sameCellItems[5])
           && ReferenceEquals(cell.ObjList[5].CellObj, sameCellItems[0]),
        "six ordinary items must remain in native head-insertion order");
}

static void CheckNativeMoveNodeTransfer()
{
    var found = false;

    var environment = NewMap();
    var mover = NewObject(environment, Grobal2.RC_PLAYOBJECT, 3, 3);
    Place(environment, mover);
    var source = environment.GetMapCellInfo(3, 3, ref found);
    var movedNode = source.ObjList[0];
    var originalType = movedNode.CellType;
    var originalStamp = movedNode.dwAddTime;
    var destinationMarker = new object();
    Assert(ReferenceEquals(destinationMarker, environment.AddToMap(4, 3,
            CellType.OS_EVENTOBJECT, destinationMarker)),
        "move destination marker placement");

    Equal(1, environment.MoveToMovingObject(3, 3, mover, 4, 3, true),
        "ordinary node-preserving move");
    source = environment.GetMapCellInfo(3, 3, ref found);
    var destination = environment.GetMapCellInfo(4, 3, ref found);
    Assert((source.ObjList == null || source.ObjList.Count == 0)
           && destination.Count == 2
           && ReferenceEquals(destination.ObjList[0], movedNode)
           && ReferenceEquals(destination.ObjList[1].CellObj,
               destinationMarker)
           && movedNode.CellType == originalType
           && ReferenceEquals(movedNode.CellObj, mover)
           && movedNode.dwAddTime == originalStamp,
        "0x779A66..0x779A92 must move the same node to the target head");

    var runEnvironment = NewMap();
    var runMover = NewObject(runEnvironment, Grobal2.RC_ANIMAL, 6, 6);
    Assert(ReferenceEquals(runMover, runEnvironment.AddToMap(6, 6,
            CellType.OS_MOVINGOBJECT, runMover, 1)),
        "finite-life run mover placement");
    source = runEnvironment.GetMapCellInfo(6, 6, ref found);
    var runNode = source.ObjList[0];
    var finiteLifeStamp = runNode.dwAddTime;
    Equal(1, runEnvironment.MoveToMovingObjectForRun(
            6, 6, runMover, 7, 6, true),
        "finite-life run node move");
    destination = runEnvironment.GetMapCellInfo(7, 6, ref found);
    Assert(ReferenceEquals(destination.ObjList[0], runNode)
           && runNode.dwAddTime == finiteLifeStamp,
        "run movement must not refresh an AddToMap lifetime stamp");

    var sentinelEnvironment = NewMap();
    var sentinelMover = NewObject(sentinelEnvironment,
        Grobal2.RC_PLAYOBJECT, 9, 9);
    Place(sentinelEnvironment, sentinelMover);
    source = sentinelEnvironment.GetMapCellInfo(9, 9, ref found);
    var sentinelNode = source.ObjList[0];
    sentinelNode.CellType = CellType.OS_EVENTOBJECT;
    sentinelNode.dwAddTime = 123456;
    var duplicateNode = new CellObject
    {
        CellType = CellType.OS_MOVINGOBJECT,
        CellObj = sentinelMover,
        dwAddTime = 654321
    };
    source.ObjList.Add(duplicateNode);
    Equal(1, sentinelEnvironment.MoveToMovingObject(
            9, 9, sentinelMover, 10, 9, true),
        "source lookup without a node-tag gate");
    source = sentinelEnvironment.GetMapCellInfo(9, 9, ref found);
    destination = sentinelEnvironment.GetMapCellInfo(10, 9, ref found);
    Assert(source.Count == 1
           && ReferenceEquals(source.ObjList[0], duplicateNode)
           && ReferenceEquals(destination.ObjList[0], sentinelNode)
           && sentinelNode.CellType == CellType.OS_EVENTOBJECT
           && sentinelNode.dwAddTime == 123456,
        "only the first object-identity match moves with type/stamp intact");

    var blockedEnvironment = NewMap();
    var blockedMover = NewObject(blockedEnvironment,
        Grobal2.RC_PLAYOBJECT, 11, 11);
    Place(blockedEnvironment, blockedMover);
    source = blockedEnvironment.GetMapCellInfo(11, 11, ref found);
    var blockedNode = source.ObjList[0];
    var blockedStamp = blockedNode.dwAddTime;
    blockedEnvironment.SetMapXYFlag(12, 11, false);
    Equal(-1, blockedEnvironment.MoveToMovingObject(
            11, 11, blockedMover, 12, 11, false),
        "blocked target result");
    source = blockedEnvironment.GetMapCellInfo(11, 11, ref found);
    destination = blockedEnvironment.GetMapCellInfo(12, 11, ref found);
    Assert(source.Count == 1
           && ReferenceEquals(source.ObjList[0], blockedNode)
           && blockedNode.dwAddTime == blockedStamp
           && (destination.ObjList == null || destination.ObjList.Count == 0),
        "target rejection must leave both cell chains unchanged");

    var missingEnvironment = NewMap();
    var missingMover = NewObject(missingEnvironment,
        Grobal2.RC_PLAYOBJECT, 13, 13);
    var missingMarker = new object();
    Assert(ReferenceEquals(missingMarker, missingEnvironment.AddToMap(14, 13,
            CellType.OS_EVENTOBJECT, missingMarker)),
        "missing-source destination marker placement");
    destination = missingEnvironment.GetMapCellInfo(14, 13, ref found);
    var missingMarkerNode = destination.ObjList[0];
    Equal(0, missingEnvironment.MoveToMovingObject(
            13, 13, missingMover, 14, 13, true),
        "missing source result");
    destination = missingEnvironment.GetMapCellInfo(14, 13, ref found);
    Assert(destination.Count == 1
           && ReferenceEquals(destination.ObjList[0], missingMarkerNode),
        "missing source must not add or replace a target node");
}

static void CheckNativeRelocateNodeTransfer()
{
    var found = false;
    var environment = NewMap();
    var mover = NewObject(environment, Grobal2.RC_PLAYOBJECT, 3, 3);
    Place(environment, mover);
    var source = environment.GetMapCellInfo(3, 3, ref found);
    var movedNode = source.ObjList[0];
    movedNode.CellType = CellType.OS_EVENTOBJECT;
    movedNode.dwAddTime = 123456;

    var blocker = NewObject(environment, Grobal2.RC_PLAYOBJECT, 4, 3);
    Place(environment, blocker);
    var destination = environment.GetMapCellInfo(4, 3, ref found);
    var blockerNode = destination.ObjList[0];
    environment.SetMapXYFlag(4, 3, false);

    Assert(environment.NativeRelocateMovingObjectNodeExact(
            3, 3, mover, 4, 3),
        "sub_779CD8 must ignore terrain and occupied targets");
    source = environment.GetMapCellInfo(3, 3, ref found);
    destination = environment.GetMapCellInfo(4, 3, ref found);
    Assert((source.ObjList == null || source.ObjList.Count == 0)
           && destination.Count == 2
           && ReferenceEquals(destination.ObjList[0], movedNode)
           && ReferenceEquals(destination.ObjList[1], blockerNode)
           && movedNode.CellType == CellType.OS_EVENTOBJECT
           && movedNode.dwAddTime == 123456,
        "sub_779CD8 must preserve and head-insert the same node");

    var sameCellEnvironment = NewMap();
    var sameCellMover = NewObject(sameCellEnvironment,
        Grobal2.RC_PLAYOBJECT, 6, 6);
    Place(sameCellEnvironment, sameCellMover);
    var sameCell = sameCellEnvironment.GetMapCellInfo(6, 6, ref found);
    var sameCellNode = sameCell.ObjList[0];
    var marker = new object();
    Assert(ReferenceEquals(marker, sameCellEnvironment.AddToMap(6, 6,
            CellType.OS_EVENTOBJECT, marker)),
        "same-cell relocation marker placement");
    Assert(sameCellEnvironment.NativeRelocateMovingObjectNodeExact(
            6, 6, sameCellMover, 6, 6),
        "same-cell relocation result");
    sameCell = sameCellEnvironment.GetMapCellInfo(6, 6, ref found);
    Assert(sameCell.Count == 2
           && ReferenceEquals(sameCell.ObjList[0], sameCellNode)
           && ReferenceEquals(sameCell.ObjList[1].CellObj, marker),
        "same-cell relocation must promote the matched node to the head");

    var missingEnvironment = NewMap();
    var missingMover = NewObject(missingEnvironment,
        Grobal2.RC_PLAYOBJECT, 8, 8);
    var missingMarker = new object();
    Assert(ReferenceEquals(missingMarker, missingEnvironment.AddToMap(9, 8,
            CellType.OS_EVENTOBJECT, missingMarker)),
        "relocation missing-source marker placement");
    destination = missingEnvironment.GetMapCellInfo(9, 8, ref found);
    var missingMarkerNode = destination.ObjList[0];
    Assert(!missingEnvironment.NativeRelocateMovingObjectNodeExact(
            8, 8, missingMover, 9, 8),
        "relocation missing source result");
    destination = missingEnvironment.GetMapCellInfo(9, 8, ref found);
    Assert(destination.Count == 1
           && ReferenceEquals(destination.ObjList[0], missingMarkerNode),
        "relocation missing source must leave the target chain unchanged");
}

static void CheckThroughOccupancyPredicate()
{
    M2Share.SafeZoneList = new List<TSafeZoneArea>();
    M2Share.StartPointList = new List<TStartPoint>();

    var environment = NewMap();
    var player = PlacePlayer(environment, NewPlayer("through-predicate"), 5, 5);
    TPlayObject.NativeSafeZoneThroughRange = 5;

    environment.Flag.boNOTHROUGH = true;
    Equal(false, player.ComputeThroughOccupancy(),
        "NOTHROUGH denies ordinary pass-through");
    player.m_boObMode = true;
    Equal(true, player.ComputeThroughOccupancy(),
        "native pass-through grant overrides NOTHROUGH");
    player.m_boObMode = false;
    environment.Flag.boNOTHROUGH = false;

    var area = new TSafeZoneArea { MapName = environment.sMapName };
    area.Points.Add((2, 2));
    area.Points.Add((8, 2));
    area.Points.Add((8, 8));
    area.Points.Add((2, 8));
    M2Share.SafeZoneList.Add(area);
    player.m_nCurrX = 2;
    player.m_nCurrY = 5;
    Equal(true, player.ComputeThroughOccupancy(),
        "four-point polygon includes a vertical boundary");
    Equal(true, area.Contains(environment.sMapName, 5, 2),
        "four-point polygon includes a horizontal boundary");
    Equal(true, area.Contains(environment.sMapName, 2, 2),
        "four-point polygon includes a vertex");
    Equal(false, area.Contains(environment.sMapName, 9, 5),
        "four-point polygon excludes outside point");

    var triangle = new TSafeZoneArea { MapName = environment.sMapName };
    triangle.Points.Add((1, 1));
    triangle.Points.Add((5, 1));
    triangle.Points.Add((1, 5));
    Equal(false, triangle.Contains(environment.sMapName, 2, 2),
        "native polygon contract requires exactly four points");

    var wideArea = new TSafeZoneArea { MapName = environment.sMapName };
    wideArea.Points.Add((int.MinValue, 0));
    wideArea.Points.Add((int.MaxValue, 0));
    wideArea.Points.Add((int.MaxValue, 2));
    wideArea.Points.Add((int.MinValue, 2));
    Equal(true, wideArea.Contains(environment.sMapName, 0, 1),
        "polygon cross product widens before coordinate subtraction");

    M2Share.SafeZoneList.Clear();
    environment.sMapName = "3";
    player.m_sMapName = "3";
    player.m_nCurrX = 850;
    player.m_nCurrY = 679;
    M2Share.g_Config.sRedHomeMap = "configured-away";
    M2Share.g_Config.nRedHomeX = 1;
    M2Share.g_Config.nRedHomeY = 1;
    Equal(true, player.ComputeThroughOccupancy(),
        "RedHome uses native hard literal 3/845/674 and includes range edge");

    environment.sMapName = "start-map";
    player.m_sMapName = "start-map";
    player.m_nCurrX = 25;
    player.m_nCurrY = 10;
    var startPoint = new TStartPoint
    {
        m_sMapName = "start-map",
        m_nCurrX = 10,
        m_nCurrY = 10,
        m_nRange = 0x10014
    };
    M2Share.StartPointList.Add(startPoint);
    Equal(true, player.ComputeThroughOccupancy(),
        "start-point low WORD radius 20 overrides global radius 5");

    startPoint.m_nRange = 0;
    Equal(false, player.ComputeThroughOccupancy(),
        "zero start-point radius falls back to global radius 5");

    player.m_nCurrX = 32767;
    player.m_nCurrY = 0;
    startPoint.m_nCurrX = unchecked((short)0x8000);
    startPoint.m_nCurrY = 0;
    startPoint.m_nRange = 1;
    Equal(true, player.ComputeThroughOccupancy(),
        "start-point coordinates compare as low WORD values");

    TPlayObject.NativeSafeZoneThroughRange = 0;
    startPoint.m_nRange = 20;
    Equal(false, player.ComputeThroughOccupancy(),
        "global range <= 0 skips RedHome and start-point arms");
}

static void AssertTransitionPacket(ClientPacket packet, ushort expectedTag,
    string label)
{
    Equal(unchecked((ushort)Grobal2.SM_COMMON_INFORMATION), packet.Ident,
        label + " ident");
    Equal(0, packet.Recog, label + " Recog");
    Equal(unchecked((ushort)6), packet.Param, label + " Param");
    Equal(expectedTag, packet.Tag, label + " Tag");
    Equal(unchecked((ushort)0), packet.Series, label + " Series");
}

static void CheckWalkRunFailDoesNotKick()
{
    var walk = FreePlayer("walk-fail", 5, 5, Grobal2.DR_LEFT);
    walk.m_nNativeForcedMoveRemaining = 5;
    walk.m_boEmergencyClose = false;
    Assert(walk.Operate(Message(Grobal2.CM_WALK, 5, 4, 0)),
        "3011 dispatch while locked");
    Equal(false, walk.m_boEmergencyClose,
        "walk 0x276 correction must not set EmergencyClose");
    Equal(unchecked((ushort)Grobal2.SM_ACT_FAIL), walk.m_DefMsg.Ident,
        "walk fail ident 0x276");
    Equal(0, walk.m_DefMsg.Recog, "walk fail Recog = 0");
    Equal(unchecked((ushort)5), walk.m_DefMsg.Param, "walk fail Param = CurrX");
    Equal(unchecked((ushort)5), walk.m_DefMsg.Tag, "walk fail Tag = CurrY");

    var sit = FreePlayer("sit-overspeed", 5, 5, Grobal2.DR_LEFT);
    sit.m_boEmergencyClose = false;
    M2Share.g_Config.boSpeedHackCheck = false;
    M2Share.g_Config.boKickOverSpeed = true;
    M2Share.g_Config.nMaxSitDonwMsgCount = 0;
    M2Share.g_Config.dwTurnIntervalTime = 10000;
    sit.m_dwTurnTick = HUtil32.GetTickCount();
    sit.m_nOverSpeedCount = 99;
    Assert(sit.Operate(Message(Grobal2.CM_SITDOWN, 5, 5, Grobal2.DR_UP)),
        "3012 overspeed dispatch");
    Equal(false, sit.m_boEmergencyClose,
        "pose overflow must not kick (MOVE-22 / 0x6D9C8B)");
}

static TProcessMessage Message(int ident, int x, int y, int direction) => new()
{
    wIdent = ident,
    wParam = direction,
    nParam1 = x,
    nParam2 = y
};

static ProbePlayer FreePlayer(string name, short x, short y, int direction)
{
    var player = PlacePlayer(NewMap(), NewPlayer(name), x, y);
    player.m_boCanWalk = true;
    player.m_boCanRun = true;
    player.m_btDirection = (byte)direction;
    player.m_nNativeForcedMoveRemaining = 0;
    return player;
}

static ProbePlayer PlacePlayer(Envirnoment environment, ProbePlayer player,
    short x, short y)
{
    player.m_PEnvir = environment;
    player.m_sMapName = environment.sMapName;
    player.m_nCurrX = x;
    player.m_nCurrY = y;
    player.m_boOffLineFlag = true;
    Place(environment, player);
    return player;
}

static ProbePlayer NewPlayer(string name) => new()
{
    m_boOffLineFlag = true,
    m_sCharName = name,
    m_btRaceServer = Grobal2.RC_PLAYOBJECT,
    m_btAttatckMode = M2Share.HAM_ALL
};

static TBaseObject NewObject(Envirnoment environment, byte race, short x, short y)
{
    return new TBaseObject
    {
        m_PEnvir = environment,
        m_btRaceServer = race,
        m_nCurrX = x,
        m_nCurrY = y,
        bo2B9 = true,
        // SPWN-56 的有效性谓词（原生 sub_765D64）要求 Length(CName)>0，否则
        // 该 actor 会在格子链扫描时被判失效并摘链，GetMovObjCount 会返回 0。
        // 原生 actor 一律带名字，无名 actor 是夹具特有的失真态。
        m_sCharName = "probe-" + race + "-" + x + "-" + y
    };
}

static Envirnoment NewMap()
{
    var environment = new Envirnoment
    {
        sMapName = "gate-" + Guid.NewGuid().ToString("N")[..8],
        m_sMapFileName = "gate-file"
    };
    typeof(Envirnoment).GetMethod("Initialize",
        BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(environment, new object[] { (short)20, (short)20 });
    for (short x = 0; x < environment.wWidth; x++)
    {
        for (short y = 0; y < environment.wHeight; y++)
            environment.SetMapXYFlag(x, y, true);
    }
    return environment;
}

static void Place(Envirnoment environment, TBaseObject actor)
{
    actor.m_boAddToMaped = false;
    actor.m_boDelFormMaped = false;
    Assert(ReferenceEquals(actor, environment.AddToMap(actor.m_nCurrX,
        actor.m_nCurrY, CellType.OS_MOVINGOBJECT, actor)), "place actor");
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
    M2Share.StartPointList = new List<TStartPoint>();
    M2Share.SafeZoneList = new List<TSafeZoneArea>();
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
    public List<ClientPacket> SentPackets { get; } = new();

    public void RunThroughOccupancyTick()
    {
        NativeTickThroughOccupancyTransition();
    }

    public bool ComputeThroughOccupancy()
    {
        return NativeComputeThroughOccupancy();
    }

    public bool CanAutoPushDuplicate(int objectCount, uint elapsed)
    {
        return NativeCanAutoPushDuplicateOccupancy(objectCount, elapsed);
    }

    public bool BeginDuplicateOccupancyPoll(int currentTick)
    {
        return NativeBeginDuplicateOccupancyPoll(currentTick);
    }

    public uint UpdateDuplicateOccupancyLatch(
        int currentTick, int objectCount)
    {
        return NativeUpdateDuplicateOccupancyLatch(currentTick, objectCount);
    }

    public bool ShouldAutoPushDuplicate(
        int objectCount, uint elapsed, int currentTick)
    {
        return NativeShouldAutoPushDuplicateOccupancyAtTick(
            objectCount, elapsed, currentTick);
    }

    internal override void SendSocket(ClientPacket defMsg, string message)
    {
        SentPackets.Add(defMsg);
    }
}
