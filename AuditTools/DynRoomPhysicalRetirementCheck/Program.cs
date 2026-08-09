using GameSvr;
using System.Reflection;

long tick = 1_000;
var manager = new NativeDynamicRoomManager(() => tick);
var definitionA = Definition("RetireExact", "definition A");
var definitionB = Definition("RetireExact", "definition B");
var environmentA = new Envirnoment();
var physicalA = Physical(definitionA, environmentA, 7);
var entered = new ManualResetEventSlim();
var release = new ManualResetEventSlim();
var attempts = 0;
INativeDynamicRoomPhysicalRetirementPermit permitA = null;

Assert(manager.RegisterIdleRoom(definitionA, 7, environmentA,
        0, null, null, null),
    "definition-backed room A registration failed");
Assert(!manager.TryAttachPhysicalOwnership(environmentA, definitionB, 7,
        physicalA, _ => true),
    "different definition identity attached to room A");
Assert(!manager.TryAttachPhysicalOwnership(environmentA, definitionA, 8,
        physicalA, _ => true),
    "different physical identity attached to room A");
Assert(manager.TryAttachPhysicalOwnership(environmentA, definitionA, 7,
        physicalA, permit =>
        {
            attempts++;
            permitA ??= permit;
            Assert(ReferenceEquals(permitA, permit),
                "physical retirement retry minted a different permit");
            Assert(ReferenceEquals(permit.PhysicalOwnership, physicalA)
                   && ReferenceEquals(permit.Definition, definitionA)
                   && ReferenceEquals(permit.Environment, environmentA)
                   && permit.PhysicalInstanceId == 7
                   && permit.IsRetiredExact,
                "retirement callback did not receive exact isolated identity");

            if (attempts == 1)
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(5));
                return false;
            }
            if (attempts == 2)
                throw new InvalidOperationException("expected retry fault");
            if (attempts == 3)
                return true;
            MarkDestroyed(physicalA);
            return true;
        }), "exact physical ownership attachment failed");
Assert(!manager.TryAttachPhysicalOwnership(environmentA, definitionA, 7,
        physicalA, _ => true),
    "physical ownership attachment was replaceable");

var otherPool = new Envirnoment();
Assert(manager.RegisterIdleRoom(Definition("OtherPool", "other"), 7,
        otherPool, 0, null, null, null),
    "same physical ID in another definition pool was rejected");

tick += 3_600_000;
manager.Run();
Equal(0, attempts, "exact one-hour boundary retired too early");
SetActivationEventsCreated(manager, environmentA, true);
tick += 1;
manager.Run();
Equal(0, attempts, "state-0 F9 room entered physical retirement");
SetActivationEventsCreated(manager, environmentA, false);

Task firstRun = null;
try
{
    firstRun = Task.Run(manager.Run);
    Assert(entered.Wait(TimeSpan.FromSeconds(5)),
        "first retirement callback did not reach the barrier");

    var reserve = Task.Run(() => manager.TryReserveIdleRoomLease(
        definitionA.RoomName, null, out _));
    Assert(reserve.Wait(TimeSpan.FromSeconds(1)) && !reserve.Result,
        "retirement callback held the manager lock or allowed a new lease");
    Assert(!LeaseOwner(manager).TryActivate(definitionA.RoomName,
            environmentA, out _),
        "lease owner activated an isolated retiring environment");
}
finally
{
    release.Set();
    firstRun?.Wait(TimeSpan.FromSeconds(5));
}
Assert(firstRun?.IsCompletedSuccessfully == true,
    "first retirement callback did not finish");
Assert(permitA.IsRetiredExact && Blocked(environmentA),
    "failed retirement did not remain exactly isolated");

manager.Run();
Equal(2, attempts, "throwing retirement attempt was not executed");
Assert(permitA.IsRetiredExact && Blocked(environmentA),
    "throwing retirement attempt released isolation");

manager.Run();
Equal(3, attempts, "lying retirement callback count");
Assert(permitA.IsRetiredExact && Blocked(environmentA),
    "lying successful callback removed a published physical room");

manager.Run();
Equal(4, attempts, "verified physical retirement retry count");
Assert(!permitA.IsRetiredExact,
    "completed permit remained valid for a future physical instance");
Assert(!LeaseOwner(manager).TryActivate(definitionA.RoomName,
        environmentA, out _),
    "retired environment remained registered in the lease owner");
Assert(manager.TryReserveIdleRoomLease("OtherPool", null,
        out var otherLease)
       && ReferenceEquals(otherLease.Environment, otherPool),
    "same physical ID in another pool was retired");
Assert(manager.TryAbortReservedRoomLease(otherLease),
    "other-pool verification lease did not abort");

// A last physical environment retires both its pool and exact definition owner.
// The same room name can therefore be registered with a new definition identity.
Assert(manager.RegisterIdleRoom(definitionB, 7, environmentA,
        0, null, null, null),
    "last-environment retirement did not remove the exact definition");
var physicalB = Physical(definitionB, environmentA, 7);
INativeDynamicRoomPhysicalRetirementPermit permitB = null;
Assert(manager.TryAttachPhysicalOwnership(environmentA, definitionB, 7,
        physicalB, permit =>
        {
            permitB = permit;
            MarkDestroyed(physicalB);
            return true;
        }), "ABA room B ownership attachment failed");
tick += 3_600_001;
manager.Run();
Assert(permitB != null && !ReferenceEquals(permitA, permitB)
       && !permitA.IsRetiredExact && !permitB.IsRetiredExact,
    "old physical permit A remained valid across registration ABA");

// Retiring one of two environments keeps the exact definition owner alive.
long sharedTick = 5_000;
var shared = new NativeDynamicRoomManager(() => sharedTick);
var sharedDefinition = Definition("Shared", "shared A");
var wrongSharedDefinition = Definition("Shared", "shared B");
var sharedA = new Envirnoment();
var sharedB = new Envirnoment();
Assert(shared.RegisterIdleRoom(sharedDefinition, 1, sharedA,
        0, null, null, null)
       && shared.RegisterIdleRoom(sharedDefinition, 2, sharedB,
           0, null, null, null),
    "shared definition environments did not register");
var sharedPhysicalA = Physical(sharedDefinition, sharedA, 1);
Assert(shared.TryAttachPhysicalOwnership(sharedA, sharedDefinition, 1,
        sharedPhysicalA, _ =>
        {
            MarkDestroyed(sharedPhysicalA);
            return true;
        }),
    "shared environment A ownership did not attach");
sharedTick += 3_600_001;
shared.Run();
Assert(!shared.RegisterIdleRoom(wrongSharedDefinition, 3,
        new Envirnoment(), 0, null, null, null),
    "retiring one environment removed a still-owned definition");
Assert(shared.RegisterIdleRoom(sharedDefinition, 3,
        new Envirnoment(), 0, null, null, null),
    "remaining exact definition stopped accepting environments");

Console.WriteLine("DynRoomPhysicalRetirementCheck PASS "
    + "idle=1h permit=exact retry=isolated callback=lock-free "
    + "pool=exact definition=last-only ABA=closed");

static NativeDynamicRoomDefinition Definition(string roomName,
    string description)
{
    return new NativeDynamicRoomDefinition(roomName, "opaque", 0,
        description, "D000", "1", "1", Array.Empty<string>(),
        Array.Empty<NativeDynamicRoomConfiguredNpcDefinition>(), 1);
}

static NativeDynamicRoomPhysicalNpcOwnership Physical(
    NativeDynamicRoomDefinition definition, Envirnoment environment,
    int physicalInstanceId)
{
    var constructor = typeof(NativeDynamicRoomPhysicalNpcOwnership)
        .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
        .Single();
    return (NativeDynamicRoomPhysicalNpcOwnership)constructor.Invoke(
        new object[]
        {
            null, new object(), definition, environment, physicalInstanceId,
            null, Array.Empty<NormNpc>()
        });
}

static NativeDynamicRoomLeaseOwner LeaseOwner(
    NativeDynamicRoomManager manager)
{
    return (NativeDynamicRoomLeaseOwner)typeof(NativeDynamicRoomManager)
        .GetField("_leaseOwner", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(manager)!;
}

static void MarkDestroyed(
    NativeDynamicRoomPhysicalNpcOwnership ownership)
{
    typeof(NativeDynamicRoomPhysicalNpcOwnership)
        .GetProperty("DestroyPending")!.GetSetMethod(true)!
        .Invoke(ownership, new object[] { false });
    typeof(NativeDynamicRoomPhysicalNpcOwnership)
        .GetProperty("IsDestroyed")!.GetSetMethod(true)!
        .Invoke(ownership, new object[] { true });
}

static void SetActivationEventsCreated(NativeDynamicRoomManager manager,
    Envirnoment environment, bool value)
{
    var registrations = typeof(NativeDynamicRoomManager)
        .GetField("_registeredRooms",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(manager)!;
    var registration = registrations.GetType().GetProperty("Item")!
        .GetValue(registrations, new object[] { environment })!;
    registration.GetType().GetProperty("ActivationEventsCreated")!
        .SetValue(registration, value);
}

static bool Blocked(Envirnoment environment)
{
    return (bool)typeof(Envirnoment).GetProperty("DynamicRoomBlocked",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(environment)!;
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
