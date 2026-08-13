using System.Reflection;
using System.Text;
using GameSvr;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var root = Path.Combine(Path.GetTempPath(),
    "lyomir-dynroom-event-activation-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var manager = new EventManager();
    var target = NewEnvironment("same-name", 6, 6);
    var targetLease = Reserve(target, "same-name", out var targetRooms);
    var sameNameOther = NewDynamicEnvironment("same-name", 6, 6, 2);
    var targetPreexisting = AddManagedEvent(manager, target, 4, 4, 77);
    var otherPreexisting = AddManagedEvent(manager, sameNameOther, 4, 4, 77);
    WriteDescriptor(root, "Success", """
        257 999999 1,1|5,1|6,1
        2 -2 2,2
        4 2147483647 3,3
        5 -2147483648 3,4
        """);

    var adapter = new NativeDynamicRoomEventActivationAdapter(manager, target);
    Assert(adapter.TryActivate(targetLease, _ => true,
            root, "Success", out var diagnostics),
        "valid state-2 activation failed: " + string.Join(" | ", diagnostics));
    Equal((byte)1, adapter.F9, "F9 after state-2 activation");
    Assert(adapter.HasActivationEvents,
        "state-2 activation did not set HasActivationEvents");
    Equal(5, adapter.ActivationEventCount,
        "state-2 activation event count");
    Equal(5, adapter.MountedActivationEventCount,
        "state-2 mounted event count");
    Assert(ReferenceEquals(target, adapter.Environment),
        "adapter did not retain exact target environment identity");

    var targetActivationEvents = ActiveEvents(manager)
        .Where(value => ReferenceEquals(value.m_Envir, target)
                        && value.m_nEventType is >= 1 and <= 5)
        .ToArray();
    Equal(5, targetActivationEvents.Length,
        "attached target activation events");
    Equal(2, targetActivationEvents.Count(value => value.m_nEventType == 1),
        "low-byte event type or coordinate filtering");
    Assert(targetActivationEvents.All(value => value.m_nX < target.wWidth
                                               && value.m_nY < target.wHeight),
        "out-of-bound coordinate was attached");
    Equal(NativeDynamicRoomEventActivationAdapter.MaximumDurationMilliseconds,
        GetDuration(targetActivationEvents.First(value => value.m_nEventType == 1)),
        "duration upper bound");
    Equal(-2_000,
        GetDuration(targetActivationEvents.Single(value => value.m_nEventType == 2)),
        "negative duration preservation");
    Equal(-1_000,
        GetDuration(targetActivationEvents.Single(value => value.m_nEventType == 4)),
        "Int32 MaxValue duration multiplication wrap");
    Equal(0,
        GetDuration(targetActivationEvents.Single(value => value.m_nEventType == 5)),
        "Int32 MinValue duration multiplication wrap");
    Assert(!otherPreexisting.m_boClosed,
        "activation changed an equal-looking environment instance");

    var secondManager = new EventManager();
    var sameEnvironmentAdapter =
        new NativeDynamicRoomEventActivationAdapter(secondManager, target);
    Assert(sameEnvironmentAdapter.TryActivate(targetLease, _ => true,
            root, "Success", out diagnostics),
        "shared per-environment F9 rejected an idempotent activation");
    Equal(0, ActiveEvents(secondManager).Count,
        "second adapter duplicated one physical environment's events");
    Equal(adapter.ActivationEventCount,
        sameEnvironmentAdapter.ActivationEventCount,
        "per-environment activation count was not shared");

    Assert(!sameEnvironmentAdapter.TryFinalizeActivation(targetLease,
            out var closedCount),
        "state-2 environment accepted activation event cleanup");
    Assert(sameEnvironmentAdapter.HasActivationEvents,
        "rejected state-2 cleanup cleared per-environment F9");
    Assert(!targetPreexisting.m_boClosed,
        "rejected state-2 cleanup closed a target event");

    SetDynamicRoomState(target, 1);
    Assert(sameEnvironmentAdapter.TryFinalizeActivation(targetLease,
            out closedCount),
        "state-1 finalize hook could not clean activation events");
    Equal(5, closedCount,
        "state-1 exact cleanup did not close all activation events");
    Assert(!adapter.HasActivationEvents && adapter.F9 == 0
           && adapter.ActivationEventCount == 0
           && adapter.MountedActivationEventCount == 0,
        "state-1 finalize retained shared activation state");
    Assert(!targetPreexisting.m_boClosed,
        "state-1 exact cleanup closed a preexisting target event");
    Assert(!otherPreexisting.m_boClosed,
        "state-1 finalize matched an equal-looking environment instance");

    SetDynamicRoomState(target, 0);
    var afterFinalize = AddManagedEvent(manager, target, 3, 2, 88);
    Assert(adapter.TryFinalizeActivation(targetLease, out closedCount),
        "unmarked state-0 finalize was treated as a failure");
    Equal(0, closedCount, "unmarked state-0 finalize close count");
    Assert(!afterFinalize.m_boClosed,
        "unmarked finalize closed a target event");

    WriteDescriptor(root, "NoValidCoordinates", "1 2 6,1|1,6\r\n");
    var emptyTarget = NewEnvironment("empty", 6, 6);
    var emptyLease = Reserve(emptyTarget, "empty", out _);
    var emptyAdapter = new NativeDynamicRoomEventActivationAdapter(
        new EventManager(), emptyTarget);
    Assert(emptyAdapter.TryActivate(emptyLease, _ => true,
            root, "NoValidCoordinates",
            out diagnostics),
        "zero-event descriptor processing failed");
    Assert(!emptyAdapter.HasActivationEvents && emptyAdapter.F9 == 0,
        "zero-event activation set F9");
    Assert(diagnostics.Any(value => value.Contains("outside",
            StringComparison.Ordinal)),
        "out-of-bound coordinate was not diagnosed");
    SetDynamicRoomState(emptyTarget, 1);
    Assert(emptyAdapter.TryFinalizeActivation(emptyLease, out closedCount),
        "zero-event state-1 finalize was treated as a failure");
    Equal(0, closedCount, "zero-event state-1 finalize close count");

    var missingTarget = NewEnvironment("missing", 6, 6);
    var missingLease = Reserve(missingTarget, "missing", out _);
    var missingAdapter = new NativeDynamicRoomEventActivationAdapter(
        new EventManager(), missingTarget);
    Assert(missingAdapter.TryActivate(missingLease, _ => true,
            root, "MissingFile", out diagnostics),
        "missing optional descriptor file failed room activation");
    Assert(!missingAdapter.HasActivationEvents,
        "missing descriptor file set F9");

    var wrongStateTarget = NewEnvironment("wrong-state", 6, 6);
    var wrongStateLease = Reserve(wrongStateTarget, "wrong-state", out _);
    SetDynamicRoomState(wrongStateTarget, 0);
    var wrongStateAdapter = new NativeDynamicRoomEventActivationAdapter(
        new EventManager(), wrongStateTarget);
    Assert(!wrongStateAdapter.TryActivate(wrongStateLease, _ => true,
            root, "Success", out diagnostics),
        "non-state-2 environment accepted event activation");
    Assert(!wrongStateAdapter.HasActivationEvents,
        "rejected state boundary set F9");

    var wallManager = new EventManager();
    var wallTarget = NewEnvironment("wall", 5, 5);
    var wallLease = Reserve(wallTarget, "wall", out _);
    wallTarget.SetMapXYFlag(2, 2, false);
    var wallPreexisting = AddManagedEvent(wallManager, wallTarget, 3, 3, 90);
    WriteDescriptor(root, "Wall", "3 2 1,1|2,2\r\n");
    var wallAdapter = new NativeDynamicRoomEventActivationAdapter(
        wallManager, wallTarget);
    Assert(wallAdapter.TryActivate(wallLease, _ => true,
            root, "Wall", out diagnostics),
        "one failed map attachment failed the whole activation");
    Assert(wallAdapter.HasActivationEvents && wallAdapter.F9 == 1,
        "registered wall event did not set per-environment F9");
    Equal(2, wallAdapter.ActivationEventCount,
        "wall activation manager event count");
    Equal(1, wallAdapter.MountedActivationEventCount,
        "wall activation mounted event count");
    Assert(wallTarget.GetEvent(2, 2) == null,
        "failed wall event was reported as map-mounted");
    var wallEvents = ActiveEvents(wallManager)
        .Where(value => value.m_nEventType == 3).ToArray();
    Equal(2, wallEvents.Length,
        "wall failure did not retain the native manager event");
    Equal(0, GetDuration(wallEvents.Single(value => value.m_nX == 2)),
        "failed wall event duration");
    Assert(diagnostics.Any(value => value.Contains("zero duration",
            StringComparison.Ordinal)),
        "failed map attachment was not diagnosed");
    Assert(!wallAdapter.TryFinalizeActivation(wallLease, out closedCount),
        "wall events were closed in state 2");
    SetDynamicRoomState(wallTarget, 1);
    Assert(wallAdapter.TryFinalizeActivation(wallLease, out closedCount),
        "state-1 wall cleanup failed");
    Equal(2, closedCount,
        "state-1 wall cleanup did not close both activation events");
    Assert(!wallPreexisting.m_boClosed,
        "state-1 wall cleanup closed a preexisting event");

    long managerTick = 1_000;
    var integratedRooms = new NativeDynamicRoomManager(() => managerTick);
    var integratedTarget = NewEnvironment("integrated", 5, 5);
    var integratedEventManager = new EventManager();
    var integratedAdapter = new NativeDynamicRoomEventActivationAdapter(
        integratedEventManager, integratedTarget);
    NativeDynamicRoomActivationLease integratedLease = null;
    var finalizeHookCount = 0;
    var integratedClosedCount = -1;
    Assert(integratedRooms.RegisterIdleRoom("Integrated", 4,
        integratedTarget, 0, null, _ => true, lease =>
        {
            finalizeHookCount++;
            Assert(ReferenceEquals(lease, integratedLease),
                "manager finalize hook changed activation lease identity");
            Assert(ReferenceEquals(lease.Environment, integratedTarget),
                "manager finalize hook changed environment identity");
            Equal(1, GetDynamicRoomState(lease.Environment),
                "manager finalize hook did not run in state 1");
            return integratedAdapter.TryFinalizeActivation(lease,
                out integratedClosedCount);
        }, false),
        "manager integration room registration failed");
    Assert(integratedRooms.TryReserveIdleRoomLease("Integrated", null,
            out integratedLease) && integratedLease.Index == 1,
        "manager integration room was not activated");
    WriteDescriptor(root, "Integrated", "7 5 1,1\r\n");
    Assert(integratedAdapter.TryActivate(integratedLease,
            integratedRooms.TryMarkActivationEventsCreated,
            root, "Integrated", out diagnostics),
        "manager integration event activation failed");
    var alreadyClosedEvent = ActiveEvents(integratedEventManager)
        .Single(value => value.m_nEventType == 7);
    alreadyClosedEvent.Close();
    // EventManager.Run only walks the active list when more than 250 ms have passed since
    // its last pass, and the constructor seeds that stamp with GetTickCount(). A Run() issued
    // microseconds after construction is therefore a no-op and the migration below never
    // ran. Age the stamp so the pass this case is about actually executes.
    AgeEventManagerRunTick(integratedEventManager, 251);
    integratedEventManager.Run();
    Assert(ClosedEvents(integratedEventManager).Contains(alreadyClosedEvent),
        "closed activation event did not reach the manager closed list");
    Assert(integratedAdapter.HasActivationEvents,
        "ordinary event close incorrectly cleared adapter F9");

    managerTick += 120_001;
    integratedRooms.Run();
    Equal(1, GetDynamicRoomState(integratedTarget),
        "manager integration room did not enter state 1");
    Equal(0, finalizeHookCount,
        "manager event finalize ran before the closing interval");
    managerTick += 600_001;
    integratedRooms.Run();
    Equal(1, finalizeHookCount,
        "manager did not invoke the state-1 adapter finalize hook");
    Equal(0, integratedClosedCount,
        "already closed manager event changed the exact close count");
    Equal(0, GetDynamicRoomState(integratedTarget),
        "zero-close successful finalize did not release state 0");
    Assert(!integratedAdapter.HasActivationEvents,
        "manager finalize retained adapter F9");
    Assert(!ClosedEvents(integratedEventManager).Contains(alreadyClosedEvent),
        "exact finalize retained its event in the manager closed list");
    Assert(integratedRooms.TryReserveIdleRoom("Integrated", null,
            out var integratedIndex)
           && integratedIndex != integratedLease.Index,
        "manager did not reuse the successfully finalized room");

    var exceptionManager = new EventManager();
    var exceptionTarget = NewEnvironment("exception", 5, 5);
    var exceptionLease = Reserve(exceptionTarget, "exception", out _);
    InstallThrowOnceCellList(exceptionTarget, 1, 1);
    WriteDescriptor(root, "Exception", "6 2 1,1\r\n");
    var exceptionAdapter = new NativeDynamicRoomEventActivationAdapter(
        exceptionManager, exceptionTarget);
    Assert(!exceptionAdapter.TryActivate(exceptionLease, _ => true,
            root, "Exception", out diagnostics),
        "map verification exception reported processing success");
    Assert(!exceptionAdapter.HasActivationEvents
           && exceptionAdapter.ActivationEventCount == 0,
        "map verification exception retained F9 state");
    Assert(exceptionTarget.GetEvent(1, 1) == null,
        "map verification exception leaked the constructed event");
    Equal(0, ActiveEvents(exceptionManager).Count,
        "map verification exception registered an event");

    var loaderFailureTarget = NewEnvironment("loader-failure", 5, 5);
    var loaderFailureLease = Reserve(loaderFailureTarget,
        "loader-failure", out _);
    var loaderFailureAdapter = new NativeDynamicRoomEventActivationAdapter(
        new EventManager(), loaderFailureTarget);
    Assert(!loaderFailureAdapter.TryActivate(loaderFailureLease, _ => true,
            null, "Failure", out diagnostics),
        "descriptor loader failure reported processing success");
    Assert(!loaderFailureAdapter.HasActivationEvents
           && diagnostics.Count > 0,
        "descriptor loader failure was not fail-closed");

    WriteDescriptor(root, "CommitFailure", "8 5 1,1\r\n");
    var commitTarget = NewEnvironment("commit-failure", 5, 5);
    var commitLease = Reserve(commitTarget, "commit-failure", out var commitRooms);
    var commitManager = new EventManager();
    var commitPreexisting = AddManagedEvent(commitManager, commitTarget, 2, 2, 91);
    var commitAdapter = new NativeDynamicRoomEventActivationAdapter(
        commitManager, commitTarget);
    Assert(!commitAdapter.TryActivate(commitLease, _ =>
        {
            Assert(!commitRooms.TryAbortReservedRoomLease(commitLease),
                "activation guard allowed abort after event attachment");
            return false;
        },
            root, "CommitFailure", out diagnostics),
        "false event commit reported activation success");
    Assert(!commitAdapter.HasActivationEvents,
        "successful rollback retained adapter F9");
    Assert(!commitPreexisting.m_boClosed,
        "exact rollback closed a preexisting event");
    Equal(1, ActiveEvents(commitManager).Count,
        "false commit leaked its staged event");
    Assert(commitRooms.TryAbortReservedRoomLease(commitLease),
        "fully rolled back lease could not abort");

    var throwingCommitTarget = NewEnvironment("throwing-commit", 5, 5);
    var throwingCommitLease = Reserve(throwingCommitTarget,
        "throwing-commit", out _);
    var throwingCommitManager = new EventManager();
    var throwingCommitAdapter = new NativeDynamicRoomEventActivationAdapter(
        throwingCommitManager, throwingCommitTarget);
    Assert(!throwingCommitAdapter.TryActivate(throwingCommitLease,
            _ => throw new InvalidOperationException("audit commit failure"),
            root, "CommitFailure", out diagnostics),
        "throwing event commit reported activation success");
    Assert(!throwingCommitAdapter.HasActivationEvents
           && ActiveEvents(throwingCommitManager).Count == 0,
        "throwing commit did not roll back exactly");

    long transitionTick = 10_000;
    var transitionRooms = new NativeDynamicRoomManager(() => transitionTick);
    var transitionTarget = NewEnvironment("transition", 5, 5);
    Assert(transitionRooms.RegisterIdleRoom("transition", 0, transitionTarget),
        "transition room registration failed");
    Assert(transitionRooms.TryReserveIdleRoomLease("transition", null,
            out var transitionLease), "transition lease reservation failed");
    var transitionEvents = new EventManager();
    var transitionAdapter = new NativeDynamicRoomEventActivationAdapter(
        transitionEvents, transitionTarget);
    Assert(!transitionAdapter.TryActivate(transitionLease, lease =>
        {
            transitionTick += 120_001;
            transitionRooms.Run();
            return transitionRooms.TryMarkActivationEventsCreated(lease);
        }, root, "CommitFailure", out diagnostics),
        "state-1 transition between attach and mark committed events");
    Assert(!transitionAdapter.HasActivationEvents
           && ActiveEvents(transitionEvents).Count == 0,
        "attach-to-mark transition leaked events");

    long abaTick = 20_000;
    var abaTarget = NewEnvironment("aba", 5, 5);
    var abaEvents = new EventManager();
    var abaAdapter = new NativeDynamicRoomEventActivationAdapter(
        abaEvents, abaTarget);
    var abaRooms = new NativeDynamicRoomManager(() => abaTick);
    Assert(abaRooms.RegisterIdleRoom("aba", 0, abaTarget, 0, null, _ => true,
        lease => abaAdapter.TryFinalizeActivation(lease, out _), false),
        "ABA room registration failed");
    Assert(abaRooms.TryReserveIdleRoomLease("aba", null, out var leaseA),
        "ABA activation A failed");
    WriteDescriptor(root, "AbaA", "11 5 1,1\r\n");
    Assert(abaAdapter.TryActivate(leaseA,
            abaRooms.TryMarkActivationEventsCreated,
            root, "AbaA", out diagnostics), "ABA event A activation failed");
    abaTick += 120_001;
    abaRooms.Run();
    abaTick += 600_001;
    abaRooms.Run();
    Assert(abaRooms.TryReserveIdleRoomLease("aba", null, out var leaseB),
        "ABA activation B failed");
    WriteDescriptor(root, "AbaB", "12 5 2,2\r\n");
    Assert(abaAdapter.TryActivate(leaseB,
            abaRooms.TryMarkActivationEventsCreated,
            root, "AbaB", out diagnostics), "ABA event B activation failed");
    abaTick += 120_001;
    abaRooms.Run();
    var eventB = ActiveEvents(abaEvents).Single(value => value.m_nEventType == 12);
    Assert(!abaAdapter.TryFinalizeActivation(leaseA, out _),
        "stale activation A finalized reused activation B");
    Assert(!eventB.m_boClosed,
        "stale activation A closed activation B's event");
    Assert(abaAdapter.TryFinalizeActivation(leaseB, out closedCount)
           && closedCount == 1 && eventB.m_boClosed,
        "exact activation B could not finalize its event");

    WriteDescriptor(root, "AddFailure", "13 5 1,1|2,2\r\n");
    var addFailureTarget = NewEnvironment("add-failure", 5, 5);
    var addFailureLease = Reserve(addFailureTarget, "add-failure",
        out var addFailureRooms);
    var addFailureEvents = new EventManager();
    var addFailurePreexisting = AddManagedEvent(addFailureEvents,
        addFailureTarget, 1, 1, 93);
    InstallThrowAfterAddEventList(addFailureEvents);
    var addFailureAdapter = new NativeDynamicRoomEventActivationAdapter(
        addFailureEvents, addFailureTarget);
    Assert(!addFailureAdapter.TryActivate(addFailureLease, _ => true,
            root, "AddFailure", out diagnostics),
        "event-manager add-after-publication failure reported success");
    Assert(!addFailureAdapter.HasActivationEvents
           && !addFailurePreexisting.m_boClosed,
        "add failure retained F9 or closed a same-cell preexisting event");
    Equal(1, ActiveEvents(addFailureEvents).Count,
        "add failure did not discard only its exact staged references");
    Assert(ReferenceEquals(addFailureTarget.GetEvent(1, 1),
               addFailurePreexisting)
           && addFailureTarget.GetEvent(2, 2) == null,
        "add failure leaked a staged map object");
    Assert(addFailureRooms.TryAbortReservedRoomLease(addFailureLease),
        "fully compensated add failure retained its lease guard");

    WriteDescriptor(root, "Mutated", "14 5 1,1\r\n");
    var mutatedTarget = NewEnvironment("mutated", 5, 5);
    var mutatedLease = Reserve(mutatedTarget, "mutated", out _);
    var mutatedEvents = new EventManager();
    var mutatedPreexisting = AddManagedEvent(mutatedEvents,
        mutatedTarget, 1, 1, 94);
    var mutatedAdapter = new NativeDynamicRoomEventActivationAdapter(
        mutatedEvents, mutatedTarget);
    Assert(mutatedAdapter.TryActivate(mutatedLease, _ => true,
            root, "Mutated", out diagnostics),
        "mutated-state fixture activation failed");
    var mutatedEvent = ActiveEvents(mutatedEvents)
        .Single(value => value.m_nEventType == 14);
    mutatedEvent.m_nX = 4;
    mutatedEvent.m_nY = 4;
    mutatedEvent.m_Envir = null;
    mutatedEvent.m_boClosed = true;
    mutatedEvent.m_boActive = true;
    SetDynamicRoomState(mutatedTarget, 1);
    Assert(mutatedAdapter.TryFinalizeActivation(mutatedLease,
            out closedCount) && closedCount == 0,
        "closed/coordinate mutation defeated exact staged cleanup");
    Assert(mutatedEvent.m_boClosed && !mutatedEvent.m_boActive
           && !ActiveEvents(mutatedEvents).Contains(mutatedEvent),
        "mutated exact event remained active or manager-published");
    Assert(!mutatedPreexisting.m_boClosed
           && ReferenceEquals(mutatedTarget.GetEvent(1, 1),
               mutatedPreexisting),
        "mutated cleanup removed a same-cell preexisting event");

    long retainedTick = 30_000;
    var retainedTarget = NewEnvironment("retained", 5, 5);
    var retainedEvents = new EventManager();
    var retainedAdapter = new NativeDynamicRoomEventActivationAdapter(
        retainedEvents, retainedTarget);
    var retainedRooms = new NativeDynamicRoomManager(() => retainedTick);
    Assert(retainedRooms.RegisterIdleRoom("retained", 0, retainedTarget,
        0, null, _ => true,
        lease => retainedAdapter.TryFinalizeActivation(lease, out _), false),
        "retained rollback room registration failed");
    Assert(retainedRooms.TryReserveIdleRoomLease("retained", null,
            out var retainedLease), "retained rollback lease failed");
    var retainedPreexisting = AddManagedEvent(retainedEvents,
        retainedTarget, 1, 1, 95);
    var retainedCell = InstallToggleThrowCellList(retainedTarget, 1, 1);
    WriteDescriptor(root, "Retained", "15 5 1,1\r\n");
    Event retainedEvent = null;
    Assert(!retainedAdapter.TryActivate(retainedLease, lease =>
        {
            retainedEvent = ActiveEvents(retainedEvents)
                .Single(value => value.m_nEventType == 15);
            Assert(!retainedRooms.TryAbortReservedRoomLease(lease),
                "pending exact rollback guard allowed activation abort");
            retainedCell.ThrowOnAccess = true;
            return false;
        }, root, "Retained", out diagnostics),
        "injected DeleteFromMap failure reported activation success");
    Assert(retainedAdapter.HasActivationEvents
           && retainedAdapter.ActivationEventCount == 1,
        "failed rollback did not retain its exact staged reference");
    Assert(!retainedRooms.TryAbortReservedRoomLease(retainedLease)
           && !retainedRooms.TryReserveIdleRoom("retained", null, out _),
        "unresolved exact rollback exposed a reusable room");
    Assert(!retainedPreexisting.m_boClosed,
        "failed compensation closed a same-cell preexisting event");
    retainedCell.ThrowOnAccess = false;
    retainedTick += 120_001;
    retainedRooms.Run();
    retainedTick += 600_001;
    retainedRooms.Run();
    Assert(retainedEvent.m_boClosed
           && !ActiveEvents(retainedEvents).Contains(retainedEvent)
           && !retainedAdapter.HasActivationEvents,
        "state-1 retry did not consume the retained exact reference");
    Assert(!retainedPreexisting.m_boClosed
           && ReferenceEquals(retainedTarget.GetEvent(1, 1),
               retainedPreexisting),
        "state-1 retry changed a same-cell preexisting event");
    Assert(retainedRooms.TryReserveIdleRoom("retained", null, out _),
        "retained rollback was not cleaned before reuse");
}
finally
{
    Directory.Delete(root, true);
}

Console.WriteLine("DynRoomEventActivationCheck PASS "
    + "state=2->1->0 exact-environment duration=x86 wall=zero "
    + "F9=shared guard=exact-staged-ref");

static NativeDynamicRoomActivationLease Reserve(Envirnoment environment,
    string roomName, out NativeDynamicRoomManager manager)
{
    manager = new NativeDynamicRoomManager();
    Assert(manager.RegisterIdleRoom(roomName, 0, environment),
        $"{roomName} registration failed");
    Assert(manager.TryReserveIdleRoomLease(roomName, null, out var lease),
        $"{roomName} reservation failed");
    return lease;
}

static Envirnoment NewDynamicEnvironment(string name, short width,
    short height, int state)
{
    var environment = NewEnvironment(name, width, height);
    typeof(Envirnoment).GetMethod("ConfigureDormantDynamicRoom",
        BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(environment, new object[] { name });
    SetDynamicRoomState(environment, state);
    return environment;
}

static Envirnoment NewEnvironment(string name, short width, short height)
{
    var environment = new Envirnoment { sMapName = name };
    typeof(Envirnoment).GetMethod("Initialize", BindingFlags.Instance |
        BindingFlags.NonPublic)!.Invoke(environment, new object[] { width, height });
    return environment;
}

static void SetDynamicRoomState(Envirnoment environment, int state)
{
    typeof(Envirnoment).GetProperty("DynamicRoomState",
        BindingFlags.Instance | BindingFlags.NonPublic)!
        .SetValue(environment, state);
}

static int GetDynamicRoomState(Envirnoment environment)
{
    return (int)typeof(Envirnoment).GetProperty("DynamicRoomState",
        BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(environment)!;
}

static Event AddManagedEvent(EventManager manager, Envirnoment environment,
    int x, int y, int type)
{
    var result = new Event(environment, x, y, type, int.MaxValue, true);
    Assert(ReferenceEquals(environment.GetEvent(x, y), result),
        "preexisting fixture event was not attached");
    manager.AddEvent(result);
    return result;
}

static IReadOnlyList<Event> ActiveEvents(EventManager manager)
{
    return (IReadOnlyList<Event>)typeof(EventManager)
        .GetField("_eventList", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(manager)!;
}

static IReadOnlyList<Event> ClosedEvents(EventManager manager)
{
    return (IReadOnlyList<Event>)typeof(EventManager)
        .GetField("_closedEventList",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(manager)!;
}

static void AgeEventManagerRunTick(EventManager manager, int milliseconds)
{
    var field = typeof(EventManager).GetField("_runTick",
        BindingFlags.Instance | BindingFlags.NonPublic)!;
    field.SetValue(manager, unchecked((int)field.GetValue(manager)! - milliseconds));
}

static int GetDuration(Event value)
{
    return (int)typeof(Event).GetField("m_dwContinueTime",
        BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
}

static void InstallThrowOnceCellList(Envirnoment environment, int x, int y)
{
    var cells = (IList<CellObject>[])typeof(Envirnoment)
        .GetField("MapCellObjectLists",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(environment)!;
    cells[x * environment.wHeight + y] = new ThrowOnceOnReadCellList();
}

static void InstallThrowAfterAddEventList(EventManager manager)
{
    var field = typeof(EventManager).GetField("_eventList",
        BindingFlags.Instance | BindingFlags.NonPublic)!;
    field.SetValue(manager, new ThrowAfterAddEventList(ActiveEvents(manager)));
}

static ToggleThrowCellList InstallToggleThrowCellList(
    Envirnoment environment, int x, int y)
{
    var cells = (IList<CellObject>[])typeof(Envirnoment)
        .GetField("MapCellObjectLists",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(environment)!;
    var index = x * environment.wHeight + y;
    var result = new ToggleThrowCellList(cells[index]);
    cells[index] = result;
    return result;
}

static void WriteDescriptor(string directory, string roomName, string contents)
{
    File.WriteAllBytes(Path.Combine(directory,
            NativeDynamicRoomEventDescriptorLoader.BuildFileName(roomName)),
        Encoding.GetEncoding(936).GetBytes(contents));
}

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{message}: expected={expected} actual={actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class ThrowOnceOnReadCellList : IList<CellObject>
{
    private readonly List<CellObject> _items = new();
    private bool _throwOnRead = true;

    public CellObject this[int index]
    {
        get
        {
            if (_throwOnRead)
            {
                _throwOnRead = false;
                throw new InvalidOperationException("injected cell read failure");
            }
            return _items[index];
        }
        set => _items[index] = value;
    }

    public int Count => _items.Count;
    public bool IsReadOnly => false;
    public void Add(CellObject item) => _items.Add(item);
    public void Clear() => _items.Clear();
    public bool Contains(CellObject item) => _items.Contains(item);
    public void CopyTo(CellObject[] array, int arrayIndex) =>
        _items.CopyTo(array, arrayIndex);
    public IEnumerator<CellObject> GetEnumerator() => _items.GetEnumerator();
    public int IndexOf(CellObject item) => _items.IndexOf(item);
    public void Insert(int index, CellObject item) => _items.Insert(index, item);
    public bool Remove(CellObject item) => _items.Remove(item);
    public void RemoveAt(int index) => _items.RemoveAt(index);
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        GetEnumerator();
}

sealed class ThrowAfterAddEventList : IList<Event>, IReadOnlyList<Event>
{
    private readonly List<Event> _items;
    private bool _throwOnNextAdd = true;

    public ThrowAfterAddEventList(IEnumerable<Event> items)
    {
        _items = new List<Event>(items);
    }

    public Event this[int index]
    {
        get => _items[index];
        set => _items[index] = value;
    }

    public int Count => _items.Count;
    public bool IsReadOnly => false;
    public void Add(Event item)
    {
        _items.Add(item);
        if (!_throwOnNextAdd) return;
        _throwOnNextAdd = false;
        throw new InvalidOperationException("injected add-after-publication failure");
    }
    public void Clear() => _items.Clear();
    public bool Contains(Event item) => _items.Contains(item);
    public void CopyTo(Event[] array, int arrayIndex) =>
        _items.CopyTo(array, arrayIndex);
    public IEnumerator<Event> GetEnumerator() => _items.GetEnumerator();
    public int IndexOf(Event item) => _items.IndexOf(item);
    public void Insert(int index, Event item) => _items.Insert(index, item);
    public bool Remove(Event item) => _items.Remove(item);
    public void RemoveAt(int index) => _items.RemoveAt(index);
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        GetEnumerator();
}

sealed class ToggleThrowCellList : IList<CellObject>
{
    private readonly List<CellObject> _items;

    public ToggleThrowCellList(IEnumerable<CellObject> items)
    {
        _items = items == null
            ? new List<CellObject>()
            : new List<CellObject>(items);
    }

    public bool ThrowOnAccess { get; set; }

    public CellObject this[int index]
    {
        get
        {
            ThrowIfEnabled();
            return _items[index];
        }
        set
        {
            ThrowIfEnabled();
            _items[index] = value;
        }
    }

    public int Count
    {
        get
        {
            ThrowIfEnabled();
            return _items.Count;
        }
    }

    public bool IsReadOnly => false;
    public void Add(CellObject item)
    {
        ThrowIfEnabled();
        _items.Add(item);
    }
    public void Clear()
    {
        ThrowIfEnabled();
        _items.Clear();
    }
    public bool Contains(CellObject item)
    {
        ThrowIfEnabled();
        return _items.Contains(item);
    }
    public void CopyTo(CellObject[] array, int arrayIndex)
    {
        ThrowIfEnabled();
        _items.CopyTo(array, arrayIndex);
    }
    public IEnumerator<CellObject> GetEnumerator()
    {
        ThrowIfEnabled();
        return _items.GetEnumerator();
    }
    public int IndexOf(CellObject item)
    {
        ThrowIfEnabled();
        return _items.IndexOf(item);
    }
    public void Insert(int index, CellObject item)
    {
        ThrowIfEnabled();
        _items.Insert(index, item);
    }
    public bool Remove(CellObject item)
    {
        ThrowIfEnabled();
        return _items.Remove(item);
    }
    public void RemoveAt(int index)
    {
        ThrowIfEnabled();
        _items.RemoveAt(index);
    }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        GetEnumerator();

    private void ThrowIfEnabled()
    {
        if (ThrowOnAccess)
            throw new InvalidOperationException("injected cell access failure");
    }
}
