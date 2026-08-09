using GameSvr;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.ObjectManager = new ObjectManager();
M2Share.LogSystem = new MirLog();

long tick = 10_000;
var rooms = new NativeDynamicRoomManager(() => tick);
var first = new Envirnoment { sMapName = "first" };
var second = new Envirnoment { sMapName = "second" };

Assert(!rooms.RegisterIdleRoom(null, 1, first), "null room name registered");
Assert(!rooms.RegisterIdleRoom("", 1, first), "empty room name registered");
Assert(!rooms.RegisterIdleRoom("Trial", -1, first), "negative index registered");
Assert(!rooms.RegisterIdleRoom("Trial", 1, null), "null environment registered");
Assert(!rooms.RegisterIdleRoom("Trial", 1, first, -1, null),
    "negative minimum active time registered");
Assert(rooms.RegisterIdleRoom("Trial", 4, first), "first room registration failed");
Assert(rooms.RegisterIdleRoom("Trial", 9, second), "second room registration failed");
Assert(!rooms.RegisterIdleRoom("Trial", 9, new Envirnoment()),
    "duplicate physical instance ID registered in one pool");
Assert(!rooms.RegisterIdleRoom("Other", 1, first),
    "one environment registered in two pools");
Equal(4, first.DynamicRoomPhysicalInstanceId, "first physical instance ID");
Equal(9, second.DynamicRoomPhysicalInstanceId, "second physical instance ID");
Equal(-1, first.DynamicRoomIndex, "idle room had an activation lease");

Assert(!rooms.TryGetActiveRoom("Trial", 4, out _),
    "idle room was visible as active");
Assert(rooms.TryReserveIdleRoom("Trial", null, out var index) && index == 1,
    "first idle room was not selected in definition order");
Assert(rooms.TryGetActiveRoom("Trial", index, out var active)
       && ReferenceEquals(active, first),
    "active lookup did not return the reserved environment");
Assert(!rooms.TryGetActiveRoom("Trial", 9, out _),
    "unreserved room was visible as active");
Assert(rooms.TryReserveIdleRoom("Trial", null, out index) && index == 2,
    "second idle room was not selected after first activation");
Assert(!rooms.TryReserveIdleRoom("Trial", null, out _),
    "active rooms were selected as idle");
Assert(!rooms.TryReserveIdleRoom("Missing", null, out _),
    "missing definition reserved a room");
Assert(!rooms.TryGetActiveRoom("Trial", -1, out _),
    "negative active index was accepted");

long lifecycleTick = 100_000;
var lifecycleRooms = new NativeDynamicRoomManager(() => lifecycleTick);
var reusable = NewEnvironment();
var prepareCount = 0;
Assert(lifecycleRooms.RegisterIdleRoom("Lifecycle", 7, reusable, 2, _ =>
{
    prepareCount++;
    return true;
}), "lifecycle room registration failed");
Assert(lifecycleRooms.TryReserveIdleRoom("Lifecycle", null, out index) && index == 1,
    "lifecycle room was not reserved");
var lifecycleFirstIndex = index;
Assert(lifecycleRooms.TryGetActiveRoom("Lifecycle", lifecycleFirstIndex, out _),
    "reserved lifecycle room was not active");

var occupant = new TPlayObject
{
    m_PEnvir = reusable,
    m_nCurrX = 1,
    m_nCurrY = 1
};
Assert(ReferenceEquals(occupant, reusable.AddToMap(1, 1,
    CellType.OS_MOVINGOBJECT, occupant)), "lifecycle occupant was not added to the map");
Equal(1, reusable.DynamicRoomPlayerCount,
    "lifecycle occupant did not enter dynamic physical occupancy");
lifecycleTick += 120_000;
Equal(1, reusable.DeleteFromMap(1, 1, CellType.OS_MOVINGOBJECT, occupant),
    "lifecycle occupant was not removed from the map");
Equal(0, reusable.DynamicRoomPlayerCount,
    "lifecycle occupant did not leave dynamic physical occupancy");
Assert(prepareCount == 0, "minimum active exact boundary closed the room");
Assert(lifecycleRooms.TryGetActiveRoom("Lifecycle", lifecycleFirstIndex, out _),
    "room left active state at the minimum active exact boundary");

lifecycleTick++;
lifecycleRooms.Run();
Assert(prepareCount == 1, "empty room did not begin cleanup one millisecond after the active floor");
Assert(!lifecycleRooms.TryGetActiveRoom("Lifecycle", lifecycleFirstIndex, out _),
    "closing room remained visible as active");
Assert(!lifecycleRooms.TryReserveIdleRoom("Lifecycle", null, out _),
    "closing room was reused before closing cooldown");

lifecycleTick += 600_000;
lifecycleRooms.Run();
Assert(!lifecycleRooms.TryReserveIdleRoom("Lifecycle", null, out _),
    "prepared room returned to idle at the native closing boundary");
lifecycleTick++;
lifecycleRooms.Run();
Assert(lifecycleRooms.TryReserveIdleRoom("Lifecycle", null, out index)
       && index != lifecycleFirstIndex,
    "prepared room did not return to idle after closing cooldown");
Equal(7, reusable.DynamicRoomPhysicalInstanceId,
    "physical instance ID changed after reuse");

long emptyTick = 1_000_000;
var emptyRooms = new NativeDynamicRoomManager(() => emptyTick);
var empty = new Envirnoment();
var emptyPrepareCount = 0;
Assert(emptyRooms.RegisterIdleRoom("Empty", 3, empty, 0, _ =>
{
    emptyPrepareCount++;
    return true;
}), "empty room registration failed");
Assert(emptyRooms.TryReserveIdleRoom("Empty", null, out index) && index == 1,
    "empty room was not reserved");
emptyTick += 119_999;
emptyRooms.Run();
Assert(emptyRooms.TryGetActiveRoom("Empty", index, out _),
    "empty active room closed before native empty-reservation floor");
emptyTick += 2;
emptyRooms.Run();
Assert(emptyPrepareCount == 1, "empty active room did not enter cleanup after floor");
Assert(!emptyRooms.TryGetActiveRoom("Empty", index, out _),
    "empty active room remained active after cleanup began");

long quarantineTick = 2_000_000;
var quarantineRooms = new NativeDynamicRoomManager(() => quarantineTick);
var quarantine = new Envirnoment();
Assert(quarantineRooms.RegisterIdleRoom("Quarantine", 2, quarantine),
    "quarantine room registration failed");
Assert(quarantineRooms.TryReserveIdleRoom("Quarantine", null, out index) && index == 1,
    "quarantine room was not reserved");
var quarantineFirstIndex = index;
quarantineTick += 120_001;
quarantineRooms.Run();
quarantineTick += 600_001;
quarantineRooms.Run();
Assert(quarantineRooms.TryReserveIdleRoom("Quarantine", null, out index)
       && index != quarantineFirstIndex,
    "room without cleanup delegates remained permanently blocked");

long failingTick = 3_000_000;
var failingRooms = new NativeDynamicRoomManager(() => failingTick);
var failing = new Envirnoment();
Assert(failingRooms.RegisterIdleRoom("Failing", 5, failing, 0, _ => false),
    "failing room registration failed");
Assert(failingRooms.TryReserveIdleRoom("Failing", null, out index) && index == 1,
    "failing room was not reserved");
failingTick += 120_001;
failingRooms.Run();
failingTick += 600_001;
failingRooms.Run();
Assert(!failingRooms.TryReserveIdleRoom("Failing", null, out _),
    "room with failed cleanup delegate was reused");

long throwingTick = 4_000_000;
var throwingRooms = new NativeDynamicRoomManager(() => throwingTick);
var throwing = new Envirnoment();
Assert(throwingRooms.RegisterIdleRoom("Throwing", 6, throwing, 0,
    _ => throw new InvalidOperationException("audit cleanup failure")),
    "throwing room registration failed");
Assert(throwingRooms.TryReserveIdleRoom("Throwing", null, out index) && index == 1,
    "throwing room was not reserved");
throwingTick += 120_001;
throwingRooms.Run();
throwingTick += 600_001;
throwingRooms.Run();
Assert(!throwingRooms.TryReserveIdleRoom("Throwing", null, out _),
    "room with throwing cleanup delegate was reused");

long retryTick = 4_500_000;
var retryRooms = new NativeDynamicRoomManager(() => retryTick);
var retry = new Envirnoment();
var retryBeginCount = 0;
var retryFinalizeCount = 0;
Assert(retryRooms.RegisterIdleRoom("Retry", 10, retry, 0, _ =>
{
    retryBeginCount++;
    return retryBeginCount > 1;
}, _ =>
{
    retryFinalizeCount++;
    return retryFinalizeCount > 1;
}, null), "retry room registration failed");
Assert(retryRooms.TryReserveIdleRoom("Retry", null, out index) && index == 1,
    "retry room was not reserved");
retryTick += 120_001;
retryRooms.Run();
Equal(1, retryBeginCount, "failed begin cleanup was not attempted");
Assert(!retryRooms.TryReserveIdleRoom("Retry", null, out _),
    "failed begin cleanup exposed the room as idle");
retryRooms.Run();
Equal(2, retryBeginCount, "failed begin cleanup was not retried");
Equal(0, retryFinalizeCount, "finalize ran before the closing delay");
retryTick += 600_000;
retryRooms.Run();
Equal(0, retryFinalizeCount, "finalize ran at the exact closing boundary");
retryTick++;
retryRooms.Run();
Equal(1, retryFinalizeCount, "finalize did not run after the closing boundary");
Assert(!retryRooms.TryReserveIdleRoom("Retry", null, out _),
    "failed finalize cleanup exposed the room as idle");
retryTick++;
retryRooms.Run();
Equal(2, retryFinalizeCount, "failed finalize cleanup was not retried");
Assert(retryRooms.TryReserveIdleRoom("Retry", null, out index) && index == 2,
    "successfully retried room did not return to idle");

long phasedTick = 5_000_000;
var phasedRooms = new NativeDynamicRoomManager(() => phasedTick);
var phased = new Envirnoment();
var phasedOrder = new List<string>();
var phasedBeginCount = 0;
var phasedFinalizeCount = 0;
var phasedEventCount = 0;
NativeDynamicRoomActivationLease phasedLease = null;
long firstIdleTick = -1;
Assert(phasedRooms.RegisterIdleRoom("Phased", 11, phased, 0, lease =>
{
    Assert(ReferenceEquals(lease, phasedLease),
        "begin cleanup did not receive the exact activation lease");
    Assert(ReferenceEquals(lease.Environment, phased),
        "begin cleanup changed the environment identity");
    phasedBeginCount++;
    phasedOrder.Add("begin");
    Equal(1, GetDynamicRoomState(phased), "begin cleanup did not run in state 1");
    return true;
}, lease =>
{
    Assert(ReferenceEquals(lease, phasedLease),
        "finalize cleanup did not receive the exact activation lease");
    Assert(ReferenceEquals(lease.Environment, phased),
        "finalize cleanup changed the environment identity");
    phasedFinalizeCount++;
    phasedOrder.Add("finalize");
    Equal(1, GetDynamicRoomState(phased), "finalize exposed state 0 before completion");
    Assert(!phasedRooms.TryReserveIdleRoom("Phased", null, out _),
        "finalize callback observed a reusable room");
    return true;
}, lease =>
{
    Assert(ReferenceEquals(lease, phasedLease),
        "event cleanup did not receive the exact activation lease");
    phasedEventCount++;
    phasedOrder.Add("events");
    Equal(1, GetDynamicRoomState(phased),
        "event cleanup exposed state 0 before all cleanup succeeded");
    var idleTick = GetIdleTick(phasedRooms, phased);
    if (firstIdleTick < 0)
        firstIdleTick = idleTick;
    Equal(firstIdleTick, idleTick, "event retry rewrote the finalized idle timestamp");
    Equal(phasedTick - (phasedEventCount > 1 ? 1 : 0), idleTick,
        "idle timestamp was not recorded before event cleanup");
    return phasedEventCount > 1;
}), "phased room registration failed");
Assert(!phasedRooms.TryMarkActivationEventsCreated(null),
    "null lease accepted an activation event marker");
Assert(phasedRooms.TryReserveIdleRoomLease("Phased", null,
        out phasedLease) && phasedLease.Index == 1,
    "phased room was not reserved");
Assert(phasedRooms.TryMarkActivationEventsCreated(phasedLease),
    "active room rejected an activation event marker");
Assert(phasedRooms.TryMarkActivationEventsCreated(phasedLease),
    "duplicate activation event marker was not idempotent");
phasedTick += 120_001;
phasedRooms.Run();
Equal(1, phasedBeginCount, "phased begin cleanup count changed");
Equal(0, phasedFinalizeCount, "phased finalize ran during begin cleanup");
Equal(0, phasedEventCount, "events were closed at state 2 to state 1");
Assert(!phasedRooms.TryMarkActivationEventsCreated(phasedLease),
    "closing room accepted a stale activation event marker");
phasedTick += 600_001;
phasedRooms.Run();
Equal(1, phasedFinalizeCount, "phased finalize cleanup did not run");
Equal(1, phasedEventCount, "activation events were not closed after finalize");
Assert(!phasedRooms.TryReserveIdleRoom("Phased", null, out _),
    "failed event cleanup exposed state 0");
phasedTick++;
phasedRooms.Run();
Equal(1, phasedFinalizeCount, "successful finalize was repeated for event retry");
Equal(2, phasedEventCount, "failed event cleanup was not retried");
Equal("begin,finalize,events,events", string.Join(',', phasedOrder),
    "two-phase cleanup order changed");
Equal(0, GetDynamicRoomState(phased), "fully cleaned room did not enter state 0");
Assert(!phasedRooms.TryMarkActivationEventsCreated(phasedLease),
    "idle room retained the activation event marker");
Assert(phasedRooms.TryReserveIdleRoomLease("Phased", null,
        out var phasedSecondLease)
       && phasedSecondLease.Index != phasedLease.Index,
    "fully cleaned phased room was not reusable");
Assert(!phasedRooms.TryMarkActivationEventsCreated(phasedLease),
    "old lease marked events on a reused physical room");
phasedLease = phasedSecondLease;
phasedTick += 120_001;
phasedRooms.Run();
phasedTick += 600_001;
phasedRooms.Run();
Equal(2, phasedBeginCount, "second activation did not run begin cleanup");
Equal(2, phasedFinalizeCount, "second activation did not run finalize cleanup");
Equal(2, phasedEventCount, "F9 event marker leaked into the next activation");

long f8Tick = 7_000_000;
var f8Rooms = new NativeDynamicRoomManager(() => f8Tick);
var f8 = new Envirnoment();
var f8BeginCount = 0;
var f8FinalizeCount = 0;
Assert(f8Rooms.RegisterIdleRoom("F8Disabled", 12, f8, 0, _ =>
{
    f8BeginCount++;
    return false;
}, _ =>
{
    f8FinalizeCount++;
    return true;
}, null, false), "F8-disabled room registration failed");
Assert(f8Rooms.TryReserveIdleRoom("F8Disabled", null, out index) && index == 1,
    "F8-disabled room was not reserved");
f8Tick += 120_001;
f8Rooms.Run();
Equal(0, f8BeginCount, "F8-disabled room ran begin cleanup");
f8Tick += 600_001;
f8Rooms.Run();
Equal(1, f8FinalizeCount, "F8-disabled room skipped finalize cleanup");
Assert(f8Rooms.TryReserveIdleRoom("F8Disabled", null, out index) && index == 2,
    "F8-disabled room did not return to idle");

long missingEventHookTick = 8_000_000;
var missingEventHookRooms = new NativeDynamicRoomManager(() => missingEventHookTick);
var missingEventHook = new Envirnoment();
var missingEventFinalizeCount = 0;
Assert(missingEventHookRooms.RegisterIdleRoom("MissingEventHook", 13,
    missingEventHook, 0, null, _ =>
    {
        missingEventFinalizeCount++;
        return true;
    }, null), "missing-event-hook room registration failed");
Assert(missingEventHookRooms.TryReserveIdleRoomLease("MissingEventHook", null,
        out var missingEventHookLease)
       && missingEventHookLease.Index == 1,
    "missing-event-hook room was not reserved");
Assert(missingEventHookRooms.TryMarkActivationEventsCreated(
        missingEventHookLease),
    "missing-event-hook room rejected its event marker");
missingEventHookTick += 120_001;
missingEventHookRooms.Run();
missingEventHookTick += 600_001;
missingEventHookRooms.Run();
Equal(1, missingEventFinalizeCount, "missing event hook skipped finalize cleanup");
Assert(!missingEventHookRooms.TryReserveIdleRoom("MissingEventHook", null, out _),
    "F9 room without an event cleanup hook was reused");
missingEventHookTick++;
missingEventHookRooms.Run();
Equal(1, missingEventFinalizeCount,
    "missing event hook caused successful finalize cleanup to repeat");

long unavailableLoggerTick = 9_000_000;
var unavailableLoggerRooms = new NativeDynamicRoomManager(() => unavailableLoggerTick);
var unavailableLogger = new Envirnoment();
var unavailableLoggerBeginCount = 0;
var unavailableLoggerFinalizeCount = 0;
var unavailableLoggerEventCount = 0;
var savedLogSystem = M2Share.LogSystem;
try
{
    M2Share.LogSystem = null;
    Assert(unavailableLoggerRooms.RegisterIdleRoom("UnavailableLogger", 14,
        unavailableLogger, 0, _ =>
        {
            unavailableLoggerBeginCount++;
            if (unavailableLoggerBeginCount == 1)
                throw new InvalidOperationException("begin cleanup failure");
            return true;
        }, _ =>
        {
            unavailableLoggerFinalizeCount++;
            if (unavailableLoggerFinalizeCount == 1)
                throw new InvalidOperationException("finalize cleanup failure");
            return true;
        }, _ =>
        {
            unavailableLoggerEventCount++;
            if (unavailableLoggerEventCount == 1)
                throw new InvalidOperationException("event cleanup failure");
            return true;
        }), "unavailable-logger room registration failed");
    Assert(unavailableLoggerRooms.TryReserveIdleRoomLease("UnavailableLogger", null,
            out var unavailableLoggerLease)
           && unavailableLoggerLease.Index == 1,
        "unavailable-logger room was not reserved");
    Assert(unavailableLoggerRooms.TryMarkActivationEventsCreated(
            unavailableLoggerLease),
        "unavailable-logger room rejected its event marker");

    unavailableLoggerTick += 120_001;
    unavailableLoggerRooms.Run();
    Equal(1, unavailableLoggerBeginCount,
        "throwing begin cleanup was not attempted without a logger");
    unavailableLoggerRooms.Run();
    Equal(2, unavailableLoggerBeginCount,
        "logger failure left begin cleanup permanently in progress");

    unavailableLoggerTick += 600_001;
    unavailableLoggerRooms.Run();
    Equal(1, unavailableLoggerFinalizeCount,
        "throwing finalize cleanup was not attempted without a logger");
    unavailableLoggerRooms.Run();
    Equal(2, unavailableLoggerFinalizeCount,
        "logger failure left finalize cleanup permanently in progress");
    Equal(1, unavailableLoggerEventCount,
        "throwing event cleanup was not attempted after finalize retry");
    Assert(!unavailableLoggerRooms.TryReserveIdleRoom("UnavailableLogger", null, out _),
        "throwing event cleanup exposed the room as idle");
    unavailableLoggerRooms.Run();
    Equal(2, unavailableLoggerFinalizeCount,
        "event retry repeated successful finalize cleanup");
    Equal(2, unavailableLoggerEventCount,
        "logger failure left event cleanup permanently in progress");
    Assert(unavailableLoggerRooms.TryReserveIdleRoom("UnavailableLogger", null,
        out index) && index == 2,
        "room did not recover after callback and logger failures");
}
finally
{
    M2Share.LogSystem = savedLogSystem;
}

long occupiedClosingTick = 5_000_000;
var occupiedClosingRooms = new NativeDynamicRoomManager(() => occupiedClosingTick);
var occupiedClosing = NewEnvironment();
var occupiedClosingPlayer = new TPlayObject
{
    m_PEnvir = occupiedClosing,
    m_nCurrX = 1,
    m_nCurrY = 1
};
Assert(occupiedClosingRooms.RegisterIdleRoom("OccupiedClosing", 8, occupiedClosing,
    0, room => ReferenceEquals(occupiedClosingPlayer,
        room.AddToMap(1, 1, CellType.OS_MOVINGOBJECT, occupiedClosingPlayer))),
    "occupied-closing room registration failed");
Assert(occupiedClosingRooms.TryReserveIdleRoom("OccupiedClosing", null, out index)
       && index == 1, "occupied-closing room was not reserved");
occupiedClosingTick += 120_001;
occupiedClosingRooms.Run();
Equal(1, occupiedClosing.DynamicRoomPlayerCount,
    "closing callback did not record physical player occupancy");
occupiedClosingTick += 600_001;
occupiedClosingRooms.Run();
Assert(!occupiedClosingRooms.TryReserveIdleRoom("OccupiedClosing", null, out _),
    "occupied closing room was reused");
occupiedClosing.DeleteFromMap(1, 1, CellType.OS_MOVINGOBJECT, occupiedClosingPlayer);
Equal(0, occupiedClosing.DynamicRoomPlayerCount,
    "closing-room player removal did not clear physical occupancy");
occupiedClosingRooms.Run();
Assert(occupiedClosingRooms.TryReserveIdleRoom("OccupiedClosing", null, out index)
       && index == 2, "empty closing room did not become reusable");

Console.WriteLine("DynRoomManagerLifecycleCheck PASS");

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
}

static Envirnoment NewEnvironment()
{
    var environment = new Envirnoment();
    typeof(Envirnoment).GetMethod("Initialize", System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic)!.Invoke(environment,
        new object[] { (short)10, (short)10 });
    return environment;
}

static int GetDynamicRoomState(Envirnoment environment)
{
    var property = typeof(Envirnoment).GetProperty("DynamicRoomState",
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic);
    if (property == null)
        throw new InvalidOperationException("DynamicRoomState property is missing");
    return (int)property.GetValue(environment)!;
}

static long GetIdleTick(NativeDynamicRoomManager manager, Envirnoment environment)
{
    var registrationsField = typeof(NativeDynamicRoomManager).GetField("_registeredRooms",
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic);
    if (registrationsField?.GetValue(manager) is not System.Collections.IDictionary registrations)
        throw new InvalidOperationException("dynamic room registrations are unavailable");
    var registration = registrations[environment]
        ?? throw new InvalidOperationException("dynamic room registration is missing");
    var idleTickProperty = registration.GetType().GetProperty("IdleTick");
    if (idleTickProperty == null)
        throw new InvalidOperationException("IdleTick property is missing");
    return (long)idleTickProperty.GetValue(registration)!;
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
