using GameSvr;
using System.Reflection;

long tick = 1_000;
var manager = new NativeDynamicRoomManager(() => tick);
var alpha = new Envirnoment();
var beta = new Envirnoment();
var beginCount = 0;
var finalizeCount = 0;
var eventCloseCount = 0;
NativeDynamicRoomActivationLease expectedAlphaCleanupLease = null;

Assert(manager.RegisterIdleRoom("Alpha", 7, alpha, 0, lease =>
{
    Assert(ReferenceEquals(lease, expectedAlphaCleanupLease),
        "begin cleanup did not receive the exact Alpha lease");
    beginCount++;
    return true;
}, lease =>
{
    Assert(ReferenceEquals(lease, expectedAlphaCleanupLease),
        "finalize cleanup did not receive the exact Alpha lease");
    finalizeCount++;
    return true;
}, lease =>
{
    Assert(ReferenceEquals(lease, expectedAlphaCleanupLease),
        "event cleanup did not receive the exact Alpha lease");
    eventCloseCount++;
    return true;
}), "Alpha physical room registration failed");
Assert(manager.RegisterIdleRoom("Beta", 7, beta),
    "same physical ID in another pool was rejected");
Equal(7, alpha.DynamicRoomPhysicalInstanceId,
    "Alpha physical instance ID");
Equal(-1, alpha.DynamicRoomIndex, "idle Alpha lease index");

Assert(manager.TryReserveIdleRoomLease("Alpha", null, out var alphaFirst),
    "first Alpha activation failed");
Assert(alphaFirst.Definition == null,
    "legacy string registration produced a definition-backed lease");
Equal(1, alphaFirst.Index, "first manager-wide lease index");
Equal(alphaFirst.Index, alpha.DynamicRoomIndex,
    "environment did not expose the active lease index");
Assert(!manager.RegisterIdleRoom("Alpha", 7, new Envirnoment()),
    "duplicate physical ID was compared against the changing lease index");

Assert(manager.TryReserveIdleRoomLease("Beta", null, out var betaFirst),
    "first Beta activation failed");
Equal(2, betaFirst.Index, "lease index was not manager-wide across pools");
Assert(manager.TryAbortReservedRoomLease(betaFirst),
    "current unpublished Beta lease did not abort");
Equal(betaFirst.Index, beta.DynamicRoomIndex,
    "abort cleared the last lease index");
Assert(manager.TryReserveIdleRoomLease("Beta", null, out var betaSecond),
    "aborted Beta room was not immediately reusable");
Equal(3, betaSecond.Index, "aborted room did not receive a fresh lease");
Assert(!manager.TryAbortReservedRoomLease(betaFirst),
    "stale Beta token aborted a newer activation");
Assert(manager.TryGetActiveRoom("Beta", betaSecond.Index, out var active)
       && ReferenceEquals(active, beta),
    "stale abort changed the current Beta activation");
Assert(manager.TryAbortReservedRoomLease(betaSecond),
    "current second Beta lease did not abort");

Assert(manager.TryAbortReservedRoomLease(alphaFirst),
    "current unpublished Alpha lease did not abort");
Equal(0, beginCount, "abort ran begin cleanup");
Equal(0, finalizeCount, "abort ran finalize cleanup");
Equal(0, eventCloseCount, "abort ran event cleanup");
Assert(manager.TryReserveIdleRoomLease("Alpha", null, out var alphaSecond),
    "aborted Alpha physical room was not immediately reusable");
expectedAlphaCleanupLease = alphaSecond;
Equal(4, alphaSecond.Index, "reused Alpha room did not receive a fresh lease");
Assert(alphaSecond.Index != alphaFirst.Index,
    "physical room reuse retained the old lease index");
Assert(!manager.TryGetActiveRoom("Alpha", alphaFirst.Index, out _),
    "old Alpha index resolved after physical room reuse");
Assert(manager.TryGetActiveRoom("Alpha", alphaSecond.Index, out active)
       && ReferenceEquals(active, alpha),
    "fresh Alpha index did not resolve the reused physical room");
Assert(!manager.TryAbortReservedRoomLease(alphaFirst),
    "stale Alpha token aborted the reused physical room");
Assert(!manager.TryMarkActivationEventsCreated(alphaFirst),
    "stale Alpha token marked the current activation F9");
Assert(manager.TryMarkActivationEventsCreated(alphaSecond),
    "current Alpha token could not mark activation F9");
Assert(!manager.TryAbortReservedRoomLease(alphaSecond),
    "F9 activation was accepted by pre-publication abort");

tick += 120_001;
manager.Run();
Assert(!manager.TryGetActiveRoom("Alpha", alphaSecond.Index, out _),
    "state-1 lease remained visible as active");
Assert(!manager.TryMarkActivationEventsCreated(alphaSecond),
    "state-1 lease retained event-marker authority");
Assert(!manager.TryAbortReservedRoomLease(alphaSecond),
    "state-1 lease retained abort authority");
Equal(1, beginCount, "state-2 to state-1 begin cleanup count");

tick += 600_001;
manager.Run();
Equal(1, finalizeCount, "state-1 to state-0 finalize cleanup count");
Equal(1, eventCloseCount, "state-1 to state-0 event cleanup count");
Equal(alphaSecond.Index, alpha.DynamicRoomIndex,
    "state 0 cleared the last lease index");
Equal(7, alpha.DynamicRoomPhysicalInstanceId,
    "physical instance ID changed across lifecycle cleanup");
Assert(manager.TryReserveIdleRoomLease("Alpha", null, out var alphaThird),
    "cleaned Alpha physical room was not reusable");
Equal(5, alphaThird.Index, "second Alpha reuse did not advance the lease index");
Assert(!manager.TryAbortReservedRoomLease(alphaSecond),
    "old lifecycle token aborted the third Alpha activation");
Assert(manager.TryAbortReservedRoomLease(alphaThird),
    "current third Alpha activation did not abort");

long modelTick = 30_000;
var modelManager = new NativeDynamicRoomManager(() => modelTick);
var modelDefinition = Definition("Model");
var sameNameDifferentDefinition = Definition("Model");
var modelFirstRoom = new Envirnoment();
var modelSecondRoom = new Envirnoment();
NativeDynamicRoomActivationLease expectedModelCleanupLease = null;
var modelBeginCount = 0;
Assert(!modelManager.RegisterIdleRoom(
        (NativeDynamicRoomDefinition)null, 1, new Envirnoment(),
        0, null, null, null),
    "null definition model registered a room");
Assert(modelManager.RegisterIdleRoom(modelDefinition, 10, modelFirstRoom,
    0, lease =>
    {
        Assert(ReferenceEquals(lease, expectedModelCleanupLease),
            "model begin hook did not receive its exact lease");
        Assert(ReferenceEquals(lease.Definition, modelDefinition),
            "model begin hook lease changed definition identity");
        modelBeginCount++;
        return true;
    }, null, null), "model full-hook registration failed");
Assert(modelManager.RegisterIdleRoom(modelDefinition, 11, modelSecondRoom,
        0, null, null, null),
    "same definition reference could not append a second environment");
Assert(!modelManager.RegisterIdleRoom(sameNameDifferentDefinition, 12,
        new Envirnoment(), 0, null, null, null),
    "same-name different definition object appended to the model pool");
Assert(!modelManager.RegisterIdleRoom("Model", 12, new Envirnoment()),
    "legacy string registration erased model pool identity");
Assert(modelManager.TryReserveIdleRoomLease("Model", null,
        out var modelFirstLease),
    "first definition-backed activation failed");
expectedModelCleanupLease = modelFirstLease;
Assert(ReferenceEquals(modelFirstLease.Definition, modelDefinition),
    "first model lease did not retain authoritative definition identity");
modelTick += 120_001;
modelManager.Run();
Equal(1, modelBeginCount, "model full-hook begin cleanup count");
Assert(modelManager.TryReserveIdleRoomLease("Model", null,
        out var modelSecondLease),
    "second definition-backed activation failed");
Assert(ReferenceEquals(modelSecondLease.Definition, modelDefinition),
    "second model lease did not retain authoritative definition identity");

long staleBeginTick = 10_000;
var staleBeginManager = new NativeDynamicRoomManager(() => staleBeginTick);
var staleBeginRoom = new Envirnoment();
var staleBeginEntered = new ManualResetEventSlim();
var releaseStaleBegin = new ManualResetEventSlim();
NativeDynamicRoomActivationLease staleBeginA = null;
NativeDynamicRoomActivationLease staleBeginB = null;
var beginBCount = 0;
Assert(staleBeginManager.RegisterIdleRoom("StaleBegin", 0, staleBeginRoom,
    0, lease =>
    {
        if (ReferenceEquals(lease, staleBeginA))
        {
            staleBeginEntered.Set();
            releaseStaleBegin.Wait(TimeSpan.FromSeconds(5));
            return true;
        }

        Assert(ReferenceEquals(lease, staleBeginB),
            "begin cleanup received neither activation A nor B");
        beginBCount++;
        return false;
    }, lease => ReferenceEquals(lease, staleBeginB), null),
    "stale-begin room registration failed");
Assert(staleBeginManager.TryReserveIdleRoomLease("StaleBegin", null,
        out staleBeginA), "stale-begin activation A failed");
staleBeginTick += 120_001;
Task staleBeginRun = null;
try
{
    staleBeginRun = Task.Run(staleBeginManager.Run);
    Assert(staleBeginEntered.Wait(TimeSpan.FromSeconds(5)),
        "activation A begin callback did not block at the test barrier");
    Assert(Task.Run(() => staleBeginManager.TryGetActiveRoom(
            "StaleBegin", staleBeginA.Index, out _))
        .Wait(TimeSpan.FromSeconds(1)),
        "begin callback ran while holding the manager lock");

    var staleBeginOwner = GetLeaseOwner(staleBeginManager);
    Assert(staleBeginOwner.TrySetLeaseState(staleBeginA, 0),
        "test could not retire activation A in the lease owner");
    SetDynamicRoomLifecycle(staleBeginRoom, 0, false);
    Assert(staleBeginManager.TryReserveIdleRoomLease("StaleBegin", null,
            out staleBeginB), "stale-begin activation B failed");
    staleBeginTick += 120_001;
    staleBeginManager.Run();
    Equal(1, beginBCount, "activation B begin cleanup count");
    Equal(1, GetDynamicRoomState(staleBeginRoom),
        "activation B did not enter state 1");
    Assert(GetDynamicRoomBlocked(staleBeginRoom),
        "failed activation B begin cleanup was not blocked");
}
finally
{
    releaseStaleBegin.Set();
    staleBeginRun?.Wait(TimeSpan.FromSeconds(5));
}
Assert(staleBeginRun?.IsCompletedSuccessfully == true,
    "activation A begin cleanup did not finish after barrier release");
Equal(1, GetDynamicRoomState(staleBeginRoom),
    "stale activation A begin completion released activation B");
Assert(GetDynamicRoomBlocked(staleBeginRoom),
    "stale activation A begin completion marked activation B complete");

long staleFinalizeTick = 20_000;
var staleFinalizeManager = new NativeDynamicRoomManager(() => staleFinalizeTick);
var staleFinalizeRoom = new Envirnoment();
var staleFinalizeEntered = new ManualResetEventSlim();
var releaseStaleFinalize = new ManualResetEventSlim();
NativeDynamicRoomActivationLease staleFinalizeA = null;
NativeDynamicRoomActivationLease staleFinalizeB = null;
var finalizeBCount = 0;
Assert(staleFinalizeManager.RegisterIdleRoom("StaleFinalize", 0,
    staleFinalizeRoom, 0, lease =>
    {
        Assert(ReferenceEquals(lease, staleFinalizeA)
               || ReferenceEquals(lease, staleFinalizeB),
            "begin cleanup received an unknown finalize-test lease");
        return true;
    }, lease =>
    {
        if (ReferenceEquals(lease, staleFinalizeA))
        {
            staleFinalizeEntered.Set();
            releaseStaleFinalize.Wait(TimeSpan.FromSeconds(5));
            return true;
        }

        Assert(ReferenceEquals(lease, staleFinalizeB),
            "finalize cleanup received neither activation A nor B");
        finalizeBCount++;
        return true;
    }, null), "stale-finalize room registration failed");
Assert(staleFinalizeManager.TryReserveIdleRoomLease("StaleFinalize", null,
        out staleFinalizeA), "stale-finalize activation A failed");
staleFinalizeTick += 120_001;
staleFinalizeManager.Run();
staleFinalizeTick += 600_001;
Task staleFinalizeRun = null;
try
{
    staleFinalizeRun = Task.Run(staleFinalizeManager.Run);
    Assert(staleFinalizeEntered.Wait(TimeSpan.FromSeconds(5)),
        "activation A finalize callback did not block at the test barrier");
    Assert(Task.Run(() => staleFinalizeManager.TryGetActiveRoom(
            "StaleFinalize", staleFinalizeA.Index, out _))
        .Wait(TimeSpan.FromSeconds(1)),
        "finalize callback ran while holding the manager lock");

    var staleFinalizeOwner = GetLeaseOwner(staleFinalizeManager);
    Assert(staleFinalizeOwner.TrySetLeaseState(staleFinalizeA, 0),
        "test could not retire finalize activation A in the lease owner");
    SetDynamicRoomLifecycle(staleFinalizeRoom, 0, false);
    Assert(staleFinalizeManager.TryReserveIdleRoomLease("StaleFinalize", null,
            out staleFinalizeB), "stale-finalize activation B failed");
    staleFinalizeTick += 120_001;
    staleFinalizeManager.Run();
    Equal(1, GetDynamicRoomState(staleFinalizeRoom),
        "finalize activation B did not enter state 1");
}
finally
{
    releaseStaleFinalize.Set();
    staleFinalizeRun?.Wait(TimeSpan.FromSeconds(5));
}
Assert(staleFinalizeRun?.IsCompletedSuccessfully == true,
    "activation A finalize cleanup did not finish after barrier release");
Equal(1, GetDynamicRoomState(staleFinalizeRoom),
    "stale activation A finalize completion released activation B");
Equal(0, finalizeBCount,
    "activation B finalized before its own closing interval");
staleFinalizeTick += 600_001;
staleFinalizeManager.Run();
Equal(1, finalizeBCount,
    "activation B finalize hook did not receive its exact lease");
Equal(0, GetDynamicRoomState(staleFinalizeRoom),
    "activation B did not complete its own cleanup");

Console.WriteLine("DynRoomManagerLeaseIntegrationCheck PASS "
    + "physical=stable lease=fresh cleanup=exact stale-writeback=closed");

static NativeDynamicRoomLeaseOwner GetLeaseOwner(
    NativeDynamicRoomManager manager)
{
    return (NativeDynamicRoomLeaseOwner)typeof(NativeDynamicRoomManager)
        .GetField("_leaseOwner", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(manager)!;
}

static int GetDynamicRoomState(Envirnoment environment)
{
    return (int)typeof(Envirnoment).GetProperty("DynamicRoomState",
        BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(environment)!;
}

static bool GetDynamicRoomBlocked(Envirnoment environment)
{
    return (bool)typeof(Envirnoment).GetProperty("DynamicRoomBlocked",
        BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(environment)!;
}

static void SetDynamicRoomLifecycle(Envirnoment environment, int state,
    bool blocked)
{
    typeof(Envirnoment).GetProperty("DynamicRoomState",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .SetValue(environment, state);
    typeof(Envirnoment).GetProperty("DynamicRoomBlocked",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .SetValue(environment, blocked);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{message}: expected={expected} actual={actual}");
}

static NativeDynamicRoomDefinition Definition(string roomName)
{
    return new NativeDynamicRoomDefinition(roomName, 1, 1,
        "manager lease integration", "D000", "1", "1",
        Array.Empty<string>(),
        Array.Empty<NativeDynamicRoomConfiguredNpcDefinition>(), 1);
}
