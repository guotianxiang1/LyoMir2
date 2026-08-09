using GameSvr;
using System.Reflection;

var owner = new NativeDynamicRoomLeaseOwner();
var alphaFirst = new Envirnoment();
var alphaExcluded = new Envirnoment();
var alphaThird = new Envirnoment();
var alphaLate = new Envirnoment();
var betaFirst = new Envirnoment();

Assert(!owner.TryRegisterDefinition(null), "null definition registered");
Assert(!owner.TryRegisterDefinition(string.Empty), "empty definition registered");
Assert(owner.TryRegisterDefinition("Alpha"), "Alpha definition registration failed");
Assert(owner.TryRegisterDefinition("Beta"), "Beta definition registration failed");
Assert(!owner.TryRegisterDefinition("Alpha"), "duplicate definition registered");
Assert(!owner.TryAppendEnvironment("Missing", new Envirnoment()),
    "environment appended to missing definition");
Assert(!owner.TryAppendEnvironment("Alpha", null), "null environment appended");

Assert(owner.TryAppendEnvironment("Alpha", alphaFirst),
    "first Alpha environment append failed");
Assert(owner.TryAppendEnvironment("Alpha", alphaExcluded, true),
    "excluded Alpha environment append failed");
Assert(owner.TryAppendEnvironment("Alpha", alphaThird),
    "third Alpha environment append failed");
Assert(owner.TryAppendEnvironment("Beta", betaFirst),
    "first Beta environment append failed");
Assert(!owner.TryAppendEnvironment("Alpha", alphaFirst),
    "duplicate environment appended to one definition");
Assert(!owner.TryAppendEnvironment("Beta", alphaFirst),
    "one physical environment appended to two definitions");
Equal(-1, GetOwnedLeaseIndex(owner, alphaFirst),
    "idle registration did not start with lease index -1");

Assert(owner.TrySetBlocked(alphaFirst, true), "first Alpha block failed");
Assert(owner.TryFindBaseReusableEnvironment("Alpha", out var reusable)
       && ReferenceEquals(reusable, alphaThird),
    "base selection did not skip blocked and excluded environments");
Assert(owner.TrySetBlocked(alphaFirst, false), "first Alpha unblock failed");
Assert(owner.TryFindBaseReusableEnvironment("Alpha", out reusable)
       && ReferenceEquals(reusable, alphaFirst),
    "base selection did not preserve definition append order");
Assert(!owner.TryFindBaseReusableEnvironment("Missing", out _),
    "missing definition returned a reusable environment");

Assert(owner.TryActivate("Alpha", alphaThird, out var alphaFirstLease),
    "first Alpha activation failed");
Equal(1, alphaFirstLease.Index, "first manager-wide activation index");
Assert(ReferenceEquals(alphaFirstLease.Environment, alphaThird),
    "lease did not retain physical environment identity");
Assert(owner.TryGetActiveEnvironment("Alpha", alphaFirstLease.Index,
        out var active) && ReferenceEquals(active, alphaThird),
    "state-2 lease lookup failed");
Assert(!owner.TryGetActiveEnvironment("Beta", alphaFirstLease.Index, out _),
    "lease escaped its definition");
Assert(!owner.TryGetActiveEnvironment("Alpha", alphaFirstLease.Index + 1, out _),
    "wrong lease index resolved an active environment");
Assert(!owner.TryActivate("Alpha", alphaThird, out _),
    "active physical environment received a second lease");
Assert(!owner.TryActivate("Beta", alphaThird, out _),
    "environment activated through the wrong definition");

Assert(owner.TryActivate("Beta", betaFirst, out var betaLease),
    "Beta activation failed");
Equal(2, betaLease.Index, "activation index was not manager-wide");

Assert(owner.TryAppendEnvironment("Alpha", alphaLate),
    "definition did not grow after activation");
Assert(owner.TryActivate("Alpha", alphaFirst, out var alphaSecondLease),
    "second Alpha activation failed");
Equal(3, alphaSecondLease.Index, "third manager-wide activation index");
Assert(owner.TryFindBaseReusableEnvironment("Alpha", out reusable)
       && ReferenceEquals(reusable, alphaLate),
    "grown definition did not retain ordered base selection");

Assert(!owner.TrySetLeaseState(alphaFirstLease, 0),
    "active lease skipped the required state-1 transition");
Assert(owner.TrySetLeaseState(alphaFirstLease, 1),
    "active lease did not enter state 1");
Assert(!owner.TryGetActiveEnvironment("Alpha", alphaFirstLease.Index, out _),
    "state-1 environment remained visible as active");
Assert(!owner.TrySetLeaseState(alphaFirstLease, 1),
    "state-1 lease accepted a self-transition");
Assert(owner.TrySetLeaseState(alphaFirstLease, 0),
    "state-1 lease did not return to state 0");
Equal(alphaFirstLease.Index, GetOwnedLeaseIndex(owner, alphaThird),
    "state 0 did not retain the last lease index");
Assert(!owner.TryGetActiveEnvironment("Alpha", alphaFirstLease.Index, out _),
    "state-0 environment remained visible as active");
Assert(owner.TryFindBaseReusableEnvironment("Alpha", out reusable)
       && ReferenceEquals(reusable, alphaThird),
    "reusable physical environment was not selected in append order");

Assert(owner.TryActivate("Alpha", alphaThird, out var alphaReusedLease),
    "physical environment reuse activation failed");
Equal(4, alphaReusedLease.Index, "reused environment kept its old lease index");
Assert(alphaReusedLease.Index != alphaFirstLease.Index,
    "reused physical environment did not receive a fresh lease");
Assert(!owner.TryGetActiveEnvironment("Alpha", alphaFirstLease.Index, out _),
    "stale lease resolved after physical environment reuse");
Assert(owner.TryGetActiveEnvironment("Alpha", alphaReusedLease.Index,
        out active) && ReferenceEquals(active, alphaThird),
    "fresh lease did not resolve the reused physical environment");
Assert(!owner.TrySetLeaseState(alphaFirstLease, 0),
    "stale lease changed the reused environment state");

Assert(owner.TrySetBlocked(alphaThird, true), "active environment block failed");
Assert(owner.TrySetExcludedFromBaseReuse(alphaThird, true),
    "active environment exclusion failed");
Assert(owner.TryGetActiveEnvironment("Alpha", alphaReusedLease.Index,
        out active) && ReferenceEquals(active, alphaThird),
    "active lookup used base blocked/excluded filters");
Assert(!owner.TrySetLeaseState(alphaReusedLease, 0),
    "reused active lease skipped state 1");
Assert(owner.TrySetLeaseState(alphaReusedLease, 1),
    "reused lease did not enter state 1");
Assert(owner.TrySetLeaseState(alphaReusedLease, 0),
    "reused state-1 lease did not return to state 0");
Assert(owner.TryFindBaseReusableEnvironment("Alpha", out reusable)
       && ReferenceEquals(reusable, alphaLate),
    "base selection reused a blocked/excluded environment");

Assert(!owner.TrySetBlocked(new Envirnoment(), true),
    "unowned environment block succeeded");
Assert(!owner.TrySetExcludedFromBaseReuse(new Envirnoment(), true),
    "unowned environment exclusion succeeded");
Assert(!owner.TrySetLeaseState(betaLease, 2),
    "state 2 was assigned without issuing a new lease");

var abortOwner = new NativeDynamicRoomLeaseOwner();
var abortEnvironment = new Envirnoment();
Assert(abortOwner.TryRegisterDefinition("Abort")
       && abortOwner.TryAppendEnvironment("Abort", abortEnvironment),
    "abort fixture registration failed");
Assert(abortOwner.TryActivate("Abort", abortEnvironment, out var abortedLease),
    "abort fixture activation failed");
Assert(abortOwner.TryAbortActivation(abortedLease),
    "current state-2 lease did not abort");
Assert(!abortOwner.TryAbortActivation(abortedLease),
    "already aborted lease aborted twice");
Assert(abortOwner.TryActivate("Abort", abortEnvironment, out var postAbortLease),
    "aborted environment was not immediately reusable");
Assert(!abortOwner.TryAbortActivation(abortedLease),
    "stale lease aborted a newer activation");
Assert(abortOwner.TryGetActiveEnvironment("Abort", postAbortLease.Index,
        out active) && ReferenceEquals(active, abortEnvironment),
    "stale abort changed the newer activation");
Assert(abortOwner.TryAbortActivation(postAbortLease),
    "current post-abort lease was rejected");

var maxWrapOwner = new NativeDynamicRoomLeaseOwner();
var maxWrapEnvironment = new Envirnoment();
Assert(maxWrapOwner.TryRegisterDefinition("MaxWrap"),
    "max-wrap definition registration failed");
Assert(maxWrapOwner.TryAppendEnvironment("MaxWrap", maxWrapEnvironment),
    "max-wrap environment append failed");
SetActivationIndex(maxWrapOwner, int.MaxValue);
Assert(maxWrapOwner.TryActivate("MaxWrap", maxWrapEnvironment,
        out var maxWrapLease),
    "max-wrap activation failed");
Equal(int.MinValue, maxWrapLease.Index,
    "activation index did not wrap MaxValue to MinValue");
Assert(maxWrapOwner.TryGetActiveEnvironment("MaxWrap", int.MinValue, out active)
       && ReferenceEquals(active, maxWrapEnvironment),
    "negative wrapped lease index was not active");

var zeroWrapOwner = new NativeDynamicRoomLeaseOwner();
var zeroWrapEnvironment = new Envirnoment();
Assert(zeroWrapOwner.TryRegisterDefinition("ZeroWrap"),
    "zero-wrap definition registration failed");
Assert(zeroWrapOwner.TryAppendEnvironment("ZeroWrap", zeroWrapEnvironment),
    "zero-wrap environment append failed");
SetActivationIndex(zeroWrapOwner, -1);
Assert(zeroWrapOwner.TryActivate("ZeroWrap", zeroWrapEnvironment,
        out var zeroWrapLease),
    "zero-wrap activation failed");
Equal(0, zeroWrapLease.Index, "activation index did not wrap -1 to zero");
Assert(zeroWrapOwner.TryGetActiveEnvironment("ZeroWrap", 0, out active)
       && ReferenceEquals(active, zeroWrapEnvironment),
    "zero lease index was not active after native wrap");

var abaOwner = new NativeDynamicRoomLeaseOwner();
var abaEnvironment = new Envirnoment();
Assert(abaOwner.TryRegisterDefinition("Aba"),
    "ABA definition registration failed");
Assert(abaOwner.TryAppendEnvironment("Aba", abaEnvironment),
    "ABA environment append failed");
Assert(abaOwner.TryActivate("Aba", abaEnvironment, out var staleAbaLease),
    "first ABA activation failed");
Assert(abaOwner.TrySetLeaseState(staleAbaLease, 1),
    "first ABA lease did not enter state 1");
Assert(abaOwner.TrySetLeaseState(staleAbaLease, 0),
    "first ABA lease did not enter state 0");
Assert(!abaOwner.TrySetLeaseState(staleAbaLease, 1),
    "state-0 transition did not clear the current lease");
SetActivationIndex(abaOwner, 0);
Assert(abaOwner.TryActivate("Aba", abaEnvironment, out var currentAbaLease),
    "second ABA activation failed");
Equal(staleAbaLease.Index, currentAbaLease.Index,
    "ABA fixture did not reproduce the same numeric lease index");
Assert(!ReferenceEquals(staleAbaLease, currentAbaLease),
    "new activation reused the old lease object");
Assert(!abaOwner.TrySetLeaseState(staleAbaLease, 1),
    "stale ABA lease changed the current activation");
Assert(abaOwner.TrySetLeaseState(currentAbaLease, 1),
    "current ABA lease was not accepted");

var sharedEnvironment = new Envirnoment();
var leftOwner = new NativeDynamicRoomLeaseOwner();
var rightOwner = new NativeDynamicRoomLeaseOwner();
Assert(leftOwner.TryRegisterDefinition("Shared")
       && rightOwner.TryRegisterDefinition("Shared"),
    "cross-owner definitions were not registered");
Assert(leftOwner.TryAppendEnvironment("Shared", sharedEnvironment)
       && rightOwner.TryAppendEnvironment("Shared", sharedEnvironment),
    "cross-owner shared environment fixture failed");
Assert(leftOwner.TryActivate("Shared", sharedEnvironment, out var leftLease),
    "left cross-owner lease was not activated");
Assert(rightOwner.TryActivate("Shared", sharedEnvironment, out var rightLease),
    "right cross-owner lease was not activated");
Equal(leftLease.Index, rightLease.Index,
    "cross-owner fixture did not produce the same numeric index");
Assert(!rightOwner.TrySetLeaseState(leftLease, 1),
    "right owner accepted the left owner's lease");
Assert(!leftOwner.TrySetLeaseState(rightLease, 1),
    "left owner accepted the right owner's lease");
Assert(leftOwner.TrySetLeaseState(leftLease, 1),
    "left owner rejected its own current lease");
Assert(rightOwner.TrySetLeaseState(rightLease, 1),
    "right owner rejected its own current lease");

var publicMemberNames = typeof(NativeDynamicRoomLeaseOwner)
    .GetMembers(BindingFlags.Instance | BindingFlags.Public)
    .Select(member => member.Name)
    .ToArray();
Assert(!publicMemberNames.Any(name => name.Contains("Slot", StringComparison.Ordinal)
        || name.Contains("Capacity", StringComparison.Ordinal)),
    "owner exposed unproved slot/capacity semantics");
Assert(!publicMemberNames.Any(name => name.Contains("AcquireOrCreate",
        StringComparison.Ordinal) || name.Contains("Type100", StringComparison.Ordinal)),
    "owner absorbed an unproved acquisition strategy");

Console.WriteLine("DynRoomLeaseOwnerCheck PASS");

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

static void SetActivationIndex(NativeDynamicRoomLeaseOwner owner, int value)
{
    typeof(NativeDynamicRoomLeaseOwner)
        .GetField("_activationIndex", BindingFlags.Instance | BindingFlags.NonPublic)!
        .SetValue(owner, value);
}

static int GetOwnedLeaseIndex(NativeDynamicRoomLeaseOwner owner,
    Envirnoment environment)
{
    var environments = typeof(NativeDynamicRoomLeaseOwner)
        .GetField("_environments", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(owner)!;
    var owned = environments.GetType().GetProperty("Item")!
        .GetValue(environments, new object[] { environment })!;
    return (int)owned.GetType().GetProperty("LeaseIndex",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
        .GetValue(owned)!;
}
