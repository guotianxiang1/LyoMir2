using System.Reflection;
using GameSvr;
using SystemModule;

PrepareRuntimeConfig();
DefaultConstructionStillPublishesImmediately();
DeferredConstructionPublishesOnlyOnExactCommit();
NestedOutOfOrderDisposePreservesTheScopeStack();
CrossThreadDisposePreservesTheOwnerScope();
UncommittedAndFailedConstructionLeaveNoActor();
DuplicateCommitFailsWithoutReplacingTheOwner();
CommittedRollbackRemovesTheExactActor();
CommittedRollbackIsExactAcrossReplacement();
CommittedRollbackRejectsSameReferenceRepublication();

Console.WriteLine("DeferredObjectRegistrationCheck PASS "
    + "default=immediate deferred=exact constructor-failure=clean "
    + "scope=owner-thread+lifo collision=closed rollback=publication-exact");

static void DefaultConstructionStillPublishesImmediately()
{
    M2Share.ObjectManager = new ObjectManager();
    var actor = new TBaseObject();

    Assert(ReferenceEquals(M2Share.ObjectManager.Get(actor.ObjectId), actor),
        "default construction was no longer published immediately");
}

static void DeferredConstructionPublishesOnlyOnExactCommit()
{
    M2Share.ObjectManager = new ObjectManager();
    var ordinary = new TBaseObject();
    using var registration = M2Share.ObjectManager.BeginDeferredRegistration();
    var deferred = new TBaseObject();

    Assert(M2Share.ObjectManager.Get(deferred.ObjectId) == null,
        "deferred actor was visible before commit");
    Assert(!registration.TryCommit(ordinary),
        "registration committed a different actor");
    Assert(registration.TryCommit(deferred),
        "exact deferred commit failed");
    Assert(ReferenceEquals(M2Share.ObjectManager.Get(deferred.ObjectId),
            deferred),
        "committed deferred actor was not published exactly");
    Assert(!registration.TryCommit(deferred),
        "deferred registration committed twice");
}

static void NestedOutOfOrderDisposePreservesTheScopeStack()
{
    M2Share.ObjectManager = new ObjectManager();
    var outer = M2Share.ObjectManager.BeginDeferredRegistration();
    var inner = M2Share.ObjectManager.BeginDeferredRegistration();

    Exception outOfOrderFailure = null;
    try
    {
        outer.Dispose();
    }
    catch (Exception ex)
    {
        outOfOrderFailure = ex;
    }

    Assert(outOfOrderFailure is InvalidOperationException,
        "out-of-order disposal did not fail closed");

    var innerActor = new TBaseObject();
    Assert(M2Share.ObjectManager.Get(innerActor.ObjectId) == null,
        "out-of-order disposal disturbed the inner scope");
    Assert(inner.TryRollback(innerActor),
        "inner scope was not recoverable after out-of-order disposal");
    inner.Dispose();

    var outerActor = new TBaseObject();
    Assert(M2Share.ObjectManager.Get(outerActor.ObjectId) == null,
        "inner completion did not restore the outer scope");
    Assert(outer.TryRollback(outerActor),
        "outer scope was not recoverable after inner completion");
    outer.Dispose();

    var ordinary = new TBaseObject();
    Assert(ReferenceEquals(M2Share.ObjectManager.Get(ordinary.ObjectId),
            ordinary),
        "scope recovery poisoned the next ordinary construction");
}

static void CrossThreadDisposePreservesTheOwnerScope()
{
    M2Share.ObjectManager = new ObjectManager();
    var registration = M2Share.ObjectManager.BeginDeferredRegistration();
    Exception foreignThreadFailure = null;
    var foreignThread = new Thread(() =>
    {
        try
        {
            registration.Dispose();
        }
        catch (Exception ex)
        {
            foreignThreadFailure = ex;
        }
    });
    foreignThread.Start();
    foreignThread.Join();

    Assert(foreignThreadFailure is InvalidOperationException,
        "cross-thread disposal did not fail closed");

    registration.Dispose();
    var ordinary = new TBaseObject();
    Assert(ReferenceEquals(M2Share.ObjectManager.Get(ordinary.ObjectId),
            ordinary),
        "owner-thread recovery poisoned the next ordinary construction");
}

static void UncommittedAndFailedConstructionLeaveNoActor()
{
    M2Share.ObjectManager = new ObjectManager();
    TBaseObject abandoned;
    using (M2Share.ObjectManager.BeginDeferredRegistration())
    {
        abandoned = new TBaseObject();
        Assert(M2Share.ObjectManager.Get(abandoned.ObjectId) == null,
            "uncommitted actor was published");
    }
    Assert(M2Share.ObjectManager.Get(abandoned.ObjectId) == null,
        "disposing an uncommitted registration leaked the actor");

    var threw = false;
    try
    {
        using var registration =
            M2Share.ObjectManager.BeginDeferredRegistration();
        _ = new ThrowingActor();
    }
    catch (ConstructionFailure)
    {
        threw = true;
    }

    Assert(threw, "derived construction failure fixture did not throw");
    Assert(ThrowingActor.LastObjectId > 0
           && M2Share.ObjectManager.Get(ThrowingActor.LastObjectId) == null,
        "derived construction failure leaked a half-built actor");
}

static void DuplicateCommitFailsWithoutReplacingTheOwner()
{
    M2Share.ObjectManager = new ObjectManager();
    var owner = new TBaseObject();
    var originalSequence = ReadSequence();
    try
    {
        WriteSequence(owner.ObjectId - 1L);
        using var registration =
            M2Share.ObjectManager.BeginDeferredRegistration();
        var duplicate = new TBaseObject();

        Equal(owner.ObjectId, duplicate.ObjectId,
            "duplicate fixture did not reuse the actor ID");
        Assert(!registration.TryCommit(duplicate),
            "duplicate deferred actor replaced the current owner");
        Assert(ReferenceEquals(M2Share.ObjectManager.Get(owner.ObjectId),
                owner),
            "failed commit disturbed the current owner");
        Assert(registration.TryRollback(duplicate),
            "pending duplicate could not be rolled back");
    }
    finally
    {
        WriteSequence(originalSequence);
    }
}

static void CommittedRollbackIsExactAcrossReplacement()
{
    M2Share.ObjectManager = new ObjectManager();
    using var registration = M2Share.ObjectManager.BeginDeferredRegistration();
    var actor = new TBaseObject();
    Assert(registration.TryCommit(actor),
        "rollback fixture commit failed");
    Assert(M2Share.ObjectManager.Remove(actor.ObjectId, actor),
        "rollback fixture could not remove its exact actor");

    var originalSequence = ReadSequence();
    TBaseObject replacement;
    try
    {
        WriteSequence(actor.ObjectId - 1L);
        replacement = new TBaseObject();
    }
    finally
    {
        WriteSequence(originalSequence);
    }

    Equal(actor.ObjectId, replacement.ObjectId,
        "replacement fixture did not reuse the actor ID");
    Assert(!registration.TryRollback(actor),
        "stale registration reported removing a replacement");
    Assert(ReferenceEquals(M2Share.ObjectManager.Get(actor.ObjectId),
            replacement),
        "stale rollback removed the replacement actor");
}

static void CommittedRollbackRemovesTheExactActor()
{
    M2Share.ObjectManager = new ObjectManager();
    using var registration = M2Share.ObjectManager.BeginDeferredRegistration();
    var actor = new TBaseObject();

    Assert(registration.TryCommit(actor),
        "exact rollback fixture commit failed");
    Assert(registration.TryRollback(actor),
        "committed exact actor could not be rolled back");
    Assert(M2Share.ObjectManager.Get(actor.ObjectId) == null,
        "successful rollback retained the exact actor");
    Assert(!registration.TryRollback(actor),
        "exact actor was rolled back twice");
}

static void CommittedRollbackRejectsSameReferenceRepublication()
{
    M2Share.ObjectManager = new ObjectManager();
    using var registration = M2Share.ObjectManager.BeginDeferredRegistration();
    var actor = new TBaseObject();

    Assert(registration.TryCommit(actor),
        "same-reference ABA fixture commit failed");
    Assert(M2Share.ObjectManager.Remove(actor.ObjectId, actor),
        "same-reference ABA fixture exact remove failed");
    M2Share.ObjectManager.Add(actor.ObjectId, actor);

    Assert(!registration.TryRollback(actor),
        "stale registration removed a republished actor reference");
    Assert(ReferenceEquals(M2Share.ObjectManager.Get(actor.ObjectId), actor),
        "stale registration disturbed the new publication generation");
}

static long ReadSequence()
{
    return (long)typeof(HUtil32).GetField("_sequence",
        BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
}

static void WriteSequence(long value)
{
    typeof(HUtil32).GetField("_sequence",
            BindingFlags.Static | BindingFlags.NonPublic)!
        .SetValue(null, value);
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

sealed class ThrowingActor : TBaseObject
{
    public static int LastObjectId { get; private set; }

    public ThrowingActor()
    {
        LastObjectId = ObjectId;
        throw new ConstructionFailure();
    }
}

sealed class ConstructionFailure : Exception
{
}
