using GameSvr;
using System.Reflection;

PrepareRuntimeConfig();
M2Share.ObjectManager = new ObjectManager();
var npcObjectIds = new HashSet<int>();
var tempRoot = Path.Combine(Path.GetTempPath(), "dynroom-pas-route-"
    + Guid.NewGuid().ToString("N"));
var scriptRoot = Path.Combine(tempRoot, "DynRoomScripts");
var commonRoot = Path.Combine(tempRoot, "CommonScripts");
Directory.CreateDirectory(scriptRoot);
Directory.CreateDirectory(commonRoot);

try
{
    var exactPath = Path.Combine(scriptRoot, "Exact.pas");
    var sharedPath = Path.Combine(scriptRoot, "Shared.pas");
    File.WriteAllText(exactPath, "program Mir2; begin end.");
    File.WriteAllText(sharedPath, "program Mir2; begin end.");

    var owner = new NativeDynamicRoomLeaseOwner();
    var environment = new Envirnoment();
    var routeDefinition = Definition("RouteRoom");
    var wrongDefinition = Definition("OtherRoom");
    var sameNameWrongDefinition = Definition("RouteRoom");
    Assert(owner.TryRegisterDefinitionModel(routeDefinition),
        "route definition registration failed");
    Assert(owner.TryAppendEnvironment("RouteRoom", environment),
        "route environment registration failed");
    Assert(owner.TryActivate("RouteRoom", environment, out var firstLease),
        "first route activation failed");
    var wrongEnvironment = new Envirnoment();

    var table = new NativeDynamicRoomPasScriptRouteTable(scriptRoot);
    Equal(Path.GetFullPath(scriptRoot), table.ScriptRoot,
        "script root was not normalized");

    var legacyNpc = NewNpc(environment);
    Equal(NativeDynamicRoomPasScriptRouteState.NotDynamic,
        table.Resolve(legacyNpc, legacyNpc.ObjectId, null, out var routePath),
        "unregistered NPC did not retain the legacy route option");
    Equal(null, routePath, "legacy route exposed a dynamic path");
    Equal(NativeDynamicRoomPasScriptRouteState.DynamicUnavailableOrStale,
        table.Resolve(legacyNpc, legacyNpc.ObjectId, firstLease, out routePath),
        "unregistered NPC with dynamic activation context fell through to legacy");

    var exactNpc = NewNpc(environment);
    var exactPlan = Plan(routeDefinition,
        Path.Combine(scriptRoot, "nested", "..", "Exact.pas"), true);
    var exactHandle = table.Register(exactNpc, firstLease, exactPlan);
    Assert(ReferenceEquals(exactNpc, exactHandle.Npc),
        "binding did not retain exact NPC identity");
    Equal(exactNpc.ObjectId, exactHandle.NpcObjectId,
        "binding did not retain NPC ObjectId");
    Assert(ReferenceEquals(firstLease, exactHandle.ActivationLease),
        "binding did not retain exact activation lease");
    Assert(ReferenceEquals(routeDefinition, firstLease.Definition),
        "activation lease did not retain exact definition identity");
    Equal(firstLease.Index, exactHandle.ActivationGeneration,
        "binding did not expose the lease generation");
    Equal(Path.GetFullPath(exactPath), exactHandle.ScriptPath,
        "binding path was not canonicalized");
    Assert(exactHandle.HasCanonicalScriptPath,
        "canonical in-root PAS path was rejected");
    Assert(exactHandle.DefinitionMatchesActivation
           && exactHandle.BoundToLeaseEnvironment
           && exactHandle.BoundToCurrentActivation,
        "exact binding did not retain its definition/environment/lease identity");
    Equal(NativeDynamicRoomPasScriptRouteState.ExactCurrent,
        table.Resolve(exactNpc, exactNpc.ObjectId, firstLease, out routePath),
        "current exact route did not resolve");
    Equal(Path.GetFullPath(exactPath), routePath,
        "exact route changed its canonical path");
    Equal(NativeDynamicRoomPasScriptRouteState.ExactCurrent,
        table.ResolveCurrent(exactNpc, out var resolvedHandle,
            out var resolvedPath),
        "current route could not be resolved without a caller-supplied lease");
    Assert(ReferenceEquals(exactHandle, resolvedHandle),
        "current route did not return its exact binding handle");
    Equal(Path.GetFullPath(exactPath), resolvedPath,
        "current route returned a different exact path");
    Assert(table.ValidateExpected(exactNpc, resolvedHandle,
            out var validatedPath),
        "fresh expected handle did not revalidate");
    Equal(resolvedPath, validatedPath,
        "expected-handle validation changed the exact path");

    exactNpc.m_sCharName = "SharedRouteName";
    var sameNameOrdinaryNpc = NewNpc(environment);
    sameNameOrdinaryNpc.m_sCharName = exactNpc.m_sCharName;
    Equal(NativeDynamicRoomPasScriptRouteState.NotDynamic,
        table.ResolveCurrent(sameNameOrdinaryNpc, out _, out _),
        "same-name ordinary NPC was classified as dynamic");

    exactNpc.m_boGhost = true;
    Equal(NativeDynamicRoomPasScriptRouteState.DynamicUnavailableOrStale,
        table.ResolveCurrent(exactNpc, out _, out _),
        "ghost dynamic NPC retained an exact route");
    Assert(!table.ValidateExpected(exactNpc, exactHandle, out _),
        "ghost dynamic NPC passed expected-handle validation");
    exactNpc.m_boGhost = false;

    var sameIdReplacement = NewNpc(wrongEnvironment);
    Assert(M2Share.ObjectManager.Remove(exactNpc.ObjectId, exactNpc),
        "same-ID replacement fixture could not remove the original NPC");
    M2Share.ObjectManager.Add(exactNpc.ObjectId, sameIdReplacement);
    Equal(NativeDynamicRoomPasScriptRouteState.DynamicUnavailableOrStale,
        table.ResolveCurrent(exactNpc, out _, out _),
        "replaced ObjectManager identity retained an exact route");
    Assert(!table.ValidateExpected(exactNpc, exactHandle, out _),
        "replaced ObjectManager identity passed expected-handle validation");
    Assert(M2Share.ObjectManager.Remove(exactNpc.ObjectId,
            sameIdReplacement),
        "same-ID replacement fixture could not remove the replacement");
    M2Share.ObjectManager.Add(exactNpc.ObjectId, exactNpc);
    Assert(table.ValidateExpected(exactNpc, exactHandle, out _),
        "exact route did not recover after restoring ObjectManager identity");

    Equal(NativeDynamicRoomPasScriptRouteState.DynamicUnavailableOrStale,
        table.Resolve(exactNpc, exactNpc.ObjectId + 1, firstLease, out routePath),
        "ObjectId mismatch fell through to legacy routing");
    exactNpc.m_PEnvir = wrongEnvironment;
    Equal(NativeDynamicRoomPasScriptRouteState.DynamicUnavailableOrStale,
        table.Resolve(exactNpc, exactNpc.ObjectId, firstLease, out routePath),
        "NPC outside the lease environment retained an exact route");
    exactNpc.m_PEnvir = environment;
    Equal(NativeDynamicRoomPasScriptRouteState.ExactCurrent,
        table.Resolve(exactNpc, exactNpc.ObjectId, firstLease, out routePath),
        "exact route did not recover after restoring its physical environment");

    var replacementReference = NewNpc(wrongEnvironment);
    Equal(NativeDynamicRoomPasScriptRouteState.DynamicUnavailableOrStale,
        table.Resolve(replacementReference, exactNpc.ObjectId, firstLease,
            out routePath),
        "same ObjectId with a different NPC reference fell through to legacy");
    Equal(NativeDynamicRoomPasScriptRouteState.NotDynamic,
        table.Resolve(replacementReference, replacementReference.ObjectId,
            null, out routePath),
        "unrelated NPC was classified as dynamic");

    var wrongEnvironmentNpc = NewNpc(wrongEnvironment);
    var wrongEnvironmentHandle = table.Register(wrongEnvironmentNpc,
        firstLease, exactPlan);
    Assert(!wrongEnvironmentHandle.BoundToLeaseEnvironment,
        "cross-environment binding was marked exact");
    wrongEnvironmentNpc.m_PEnvir = environment;
    Equal(NativeDynamicRoomPasScriptRouteState.DynamicUnavailableOrStale,
        table.Resolve(wrongEnvironmentNpc, wrongEnvironmentNpc.ObjectId,
            firstLease, out _),
        "cross-environment binding became exact after a later move");

    var wrongDefinitionNpc = NewNpc(environment);
    var wrongDefinitionHandle = table.Register(wrongDefinitionNpc,
        firstLease, Plan(wrongDefinition, exactPath, true));
    Assert(!wrongDefinitionHandle.DefinitionMatchesActivation,
        "wrong dynamic-room definition was marked exact");
    Equal(NativeDynamicRoomPasScriptRouteState.DynamicUnavailableOrStale,
        table.Resolve(wrongDefinitionNpc, wrongDefinitionNpc.ObjectId,
            firstLease, out _),
        "wrong dynamic-room definition fell through or resolved exactly");

    var wrongDefinitionIdentityNpc = NewNpc(environment);
    var wrongDefinitionIdentityHandle = table.Register(
        wrongDefinitionIdentityNpc, firstLease,
        Plan(sameNameWrongDefinition, exactPath, true));
    Assert(!wrongDefinitionIdentityHandle.DefinitionMatchesActivation,
        "same-name definition with different identity was marked exact");
    Equal(NativeDynamicRoomPasScriptRouteState.DynamicUnavailableOrStale,
        table.Resolve(wrongDefinitionIdentityNpc,
            wrongDefinitionIdentityNpc.ObjectId, firstLease, out _),
        "same-name wrong definition identity resolved exactly");

    var sharedNpcA = NewNpc(environment);
    var sharedNpcB = NewNpc(environment);
    var sharedHandleA = table.Register(sharedNpcA, firstLease,
        Plan(routeDefinition, sharedPath, true));
    var sharedHandleB = table.Register(sharedNpcB, firstLease,
        Plan(routeDefinition, sharedPath, true));
    Equal(NativeDynamicRoomPasScriptRouteState.ExactCurrent,
        table.Resolve(sharedNpcA, sharedNpcA.ObjectId, firstLease, out _),
        "first same-path NPC did not resolve independently");
    Equal(NativeDynamicRoomPasScriptRouteState.ExactCurrent,
        table.Resolve(sharedNpcB, sharedNpcB.ObjectId, firstLease, out _),
        "second same-path NPC did not resolve independently");
    Assert(table.Unregister(sharedHandleA),
        "first same-path NPC unregister failed");
    Equal(NativeDynamicRoomPasScriptRouteState.DynamicUnavailableOrStale,
        table.Resolve(sharedNpcA, sharedNpcA.ObjectId, firstLease, out _),
        "released same-path NPC fell through to legacy");
    Equal(NativeDynamicRoomPasScriptRouteState.DynamicUnavailableOrStale,
        table.ResolveCurrent(sharedNpcA, out var releasedHandle, out _),
        "released handle was not retained as a fail-closed tombstone");
    Equal(null, releasedHandle,
        "released tombstone exposed an execution handle");
    Assert(!table.ValidateExpected(sharedNpcA, sharedHandleA, out _),
        "released tombstone passed expected-handle validation");
    Equal(NativeDynamicRoomPasScriptRouteState.ExactCurrent,
        table.Resolve(sharedNpcB, sharedNpcB.ObjectId, firstLease, out _),
        "one unregister affected another NPC sharing the path");
    Assert(ReferenceEquals(sharedHandleB.Npc, sharedNpcB),
        "same-path binding lost its NPC identity");

    var missingNpc = NewNpc(environment);
    var missingPath = Path.Combine(scriptRoot, "Missing.pas");
    var missingHandle = table.Register(missingNpc, firstLease,
        Plan(routeDefinition, missingPath, false));
    Assert(missingHandle.HasCanonicalScriptPath
           && !missingHandle.PlannedScriptPresent,
        "missing script was not retained as a canonical placeholder");
    Equal(NativeDynamicRoomPasScriptRouteState.DynamicUnavailableOrStale,
        table.Resolve(missingNpc, missingNpc.ObjectId, firstLease, out routePath),
        "missing dynamic script fell through to legacy");
    Equal(NativeDynamicRoomPasScriptRouteState.DynamicUnavailableOrStale,
        table.ResolveCurrent(missingNpc, out var missingResolvedHandle, out _),
        "missing dynamic script was not fail-closed in current resolution");
    Equal(null, missingResolvedHandle,
        "missing dynamic script exposed an execution handle");
    Assert(!table.ValidateExpected(missingNpc, missingHandle, out _),
        "missing dynamic script passed expected-handle validation");
    File.WriteAllText(missingPath, "program Mir2; begin end.");
    Equal(NativeDynamicRoomPasScriptRouteState.DynamicUnavailableOrStale,
        table.Resolve(missingNpc, missingNpc.ObjectId, firstLease, out routePath),
        "missing placeholder became live without a fresh binding");

    var outsidePath = Path.Combine(tempRoot, "Outside.pas");
    File.WriteAllText(outsidePath, "program Mir2; begin end.");
    var escapingNpc = NewNpc(environment);
    var escapingHandle = table.Register(escapingNpc, firstLease,
        Plan(routeDefinition, outsidePath, true));
    Assert(!escapingHandle.HasCanonicalScriptPath
           && escapingHandle.ScriptPath == null,
        "path outside DynRoomScripts was retained");
    Equal(NativeDynamicRoomPasScriptRouteState.DynamicUnavailableOrStale,
        table.Resolve(escapingNpc, escapingNpc.ObjectId, firstLease, out _),
        "escaping dynamic path fell through to legacy");

    var textPath = Path.Combine(scriptRoot, "Wrong.txt");
    File.WriteAllText(textPath, "not pas");
    var wrongExtensionNpc = NewNpc(environment);
    var wrongExtensionHandle = table.Register(wrongExtensionNpc, firstLease,
        Plan(routeDefinition, textPath, true));
    Assert(!wrongExtensionHandle.HasCanonicalScriptPath,
        "non-PAS dynamic route was accepted");
    Equal(NativeDynamicRoomPasScriptRouteState.DynamicUnavailableOrStale,
        table.Resolve(wrongExtensionNpc, wrongExtensionNpc.ObjectId, firstLease,
            out _),
        "non-PAS dynamic route fell through to legacy");

    var relativeNpc = NewNpc(environment);
    var relativeHandle = table.Register(relativeNpc, firstLease,
        Plan(routeDefinition, "Exact.pas", true));
    Assert(!relativeHandle.HasCanonicalScriptPath,
        "relative dynamic route was accepted");
    Equal(NativeDynamicRoomPasScriptRouteState.DynamicUnavailableOrStale,
        table.Resolve(relativeNpc, relativeNpc.ObjectId, firstLease, out _),
        "relative dynamic route fell through to legacy");

    var fallbackPath = Path.Combine(scriptRoot, "Fallback.pas");
    var commonFallbackPath = Path.Combine(commonRoot, "Fallback.pas");
    File.WriteAllText(fallbackPath, "program Mir2; begin end.");
    File.WriteAllText(commonFallbackPath, "program Common; begin end.");
    var fallbackNpc = NewNpc(environment);
    var fallbackHandle = table.Register(fallbackNpc, firstLease,
        Plan(routeDefinition, fallbackPath, true));
    File.Delete(fallbackPath);
    Equal(NativeDynamicRoomPasScriptRouteState.DynamicUnavailableOrStale,
        table.Resolve(fallbackNpc, fallbackNpc.ObjectId, firstLease,
            out routePath),
        "deleted exact script used a basename fallback");
    Equal(null, routePath,
        "deleted exact script exposed the CommonScripts fallback path");
    Assert(!table.ValidateExpected(fallbackNpc, fallbackHandle, out _),
        "deleted exact script passed expected-handle validation");

    var reusedNpc = NewNpc(environment);
    var oldHandle = table.Register(reusedNpc, firstLease, exactPlan);
    Assert(owner.TrySetLeaseState(firstLease, 1),
        "first route activation did not enter state 1");
    Equal(NativeDynamicRoomPasScriptRouteState.DynamicUnavailableOrStale,
        table.Resolve(reusedNpc, reusedNpc.ObjectId, firstLease, out _),
        "state-1 lease retained an exact PAS route");
    Equal(NativeDynamicRoomPasScriptRouteState.DynamicUnavailableOrStale,
        table.Resolve(exactNpc, exactNpc.ObjectId, firstLease, out _),
        "another state-1 binding retained an exact PAS route");
    Assert(owner.TrySetLeaseState(firstLease, 0),
        "first route activation did not return to state 0");
    Equal(NativeDynamicRoomPasScriptRouteState.DynamicUnavailableOrStale,
        table.Resolve(reusedNpc, reusedNpc.ObjectId, firstLease, out _),
        "state-0 lease retained an exact PAS route");
    SetActivationIndex(owner, firstLease.Index - 1);
    Assert(owner.TryActivate("RouteRoom", environment, out var secondLease),
        "second route activation failed");
    Equal(firstLease.Index, secondLease.Index,
        "ABA fixture did not reuse the numeric generation");
    Assert(!ReferenceEquals(firstLease, secondLease),
        "ABA fixture reused the activation lease object");
    var currentHandle = table.Register(reusedNpc, secondLease, exactPlan);

    Equal(NativeDynamicRoomPasScriptRouteState.DynamicUnavailableOrStale,
        table.Resolve(reusedNpc, reusedNpc.ObjectId, firstLease, out _),
        "stale lease with the same numeric generation resolved");
    Equal(NativeDynamicRoomPasScriptRouteState.ExactCurrent,
        table.Resolve(reusedNpc, reusedNpc.ObjectId, secondLease, out _),
        "fresh lease did not resolve after physical NPC reuse");
    Equal(NativeDynamicRoomPasScriptRouteState.ExactCurrent,
        table.ResolveCurrent(reusedNpc, out var reusedResolvedHandle,
            out var reusedResolvedPath),
        "fresh lease did not resolve through its route-owned lease");
    Assert(ReferenceEquals(currentHandle, reusedResolvedHandle),
        "fresh A/B resolution returned the wrong handle");
    Assert(table.ValidateExpected(reusedNpc, reusedResolvedHandle,
            out var reusedValidatedPath),
        "fresh A/B handle did not revalidate");
    Equal(reusedResolvedPath, reusedValidatedPath,
        "fresh A/B handle changed its exact path");
    Assert(!table.ValidateExpected(reusedNpc, oldHandle, out _),
        "old A handle revalidated against B activation");
    var delayedOldHandle = table.Register(reusedNpc, firstLease, exactPlan);
    Assert(!table.Unregister(delayedOldHandle),
        "delayed old registration became current");
    Equal(NativeDynamicRoomPasScriptRouteState.ExactCurrent,
        table.Resolve(reusedNpc, reusedNpc.ObjectId, secondLease, out _),
        "delayed old registration displaced the current generation");
    Assert(!table.Unregister(oldHandle),
        "old binding removed the current generation");
    Equal(NativeDynamicRoomPasScriptRouteState.ExactCurrent,
        table.Resolve(reusedNpc, reusedNpc.ObjectId, secondLease, out _),
        "old unregister invalidated the current generation");
    Equal(NativeDynamicRoomPasScriptRouteState.ExactCurrent,
        table.ResolveCurrent(reusedNpc, out var preTeardownHandle, out _),
        "pre-teardown route could not produce an expected handle");
    Assert(owner.TrySetLeaseState(secondLease, 1),
        "second route activation did not enter teardown state");
    Assert(!table.ValidateExpected(reusedNpc, preTeardownHandle, out _),
        "resolve-then-teardown expected handle remained valid");
    Assert(table.Unregister(currentHandle),
        "current generation unregister failed");
    Assert(!table.Unregister(currentHandle),
        "current generation unregister was not idempotent");
    Equal(NativeDynamicRoomPasScriptRouteState.DynamicUnavailableOrStale,
        table.Resolve(reusedNpc, reusedNpc.ObjectId, secondLease, out _),
        "released current generation fell through to legacy");

    RunRegistrationAbaBarrierCheck(scriptRoot, exactPath);
    AssertRouteLockBoundaries();

    Console.WriteLine(
        "DynRoomPasScriptRouteCheck PASS states=3 exact-reference+id generation=lease-strict expected-handle=revalidated register-ABA=barrier locks=external paths=exact-only missing=tombstone startup=connected");
}
finally
{
    Directory.Delete(tempRoot, true);
}

NormNpc NewNpc(Envirnoment environment)
{
    var npc = new NormNpc { m_PEnvir = environment };
    Assert(npc.ObjectId > 0 && npcObjectIds.Add(npc.ObjectId),
        "NPC ObjectId was not positive and unique");
    Assert(ReferenceEquals(npc, M2Share.ObjectManager.Get(npc.ObjectId)),
        "NPC constructor did not publish its ObjectId");
    return npc;
}

static NativeDynamicRoomDefinition Definition(string roomName)
{
    return new NativeDynamicRoomDefinition(roomName, 1, 1,
        "route check", "D000", "metadata", "metadata",
        Array.Empty<string>(),
        Array.Empty<NativeDynamicRoomConfiguredNpcDefinition>(), 1);
}

static NativeDynamicRoomDynamicNpcScriptBinding Plan(
    NativeDynamicRoomDefinition definition, string scriptPath, bool hasScript)
{
    return new NativeDynamicRoomDynamicNpcScriptBinding(
        definition, NativeDynamicRoomDynamicNpcScriptRole.HiddenController, null,
        Path.GetFileName(scriptPath), scriptPath, hasScript, 0, string.Empty);
}

static void SetActivationIndex(NativeDynamicRoomLeaseOwner owner, int value)
{
    typeof(NativeDynamicRoomLeaseOwner)
        .GetField("_activationIndex", BindingFlags.Instance | BindingFlags.NonPublic)!
        .SetValue(owner, value);
}

static void RunRegistrationAbaBarrierCheck(string scriptRoot,
    string exactPath)
{
    var owner = new NativeDynamicRoomLeaseOwner();
    var definition = Definition("ConcurrentRouteRoom");
    var environment = new Envirnoment();
    Assert(owner.TryRegisterDefinitionModel(definition),
        "concurrent route fixture definition failed");
    Assert(owner.TryAppendEnvironment(definition.RoomName, environment),
        "concurrent route fixture environment failed");
    Assert(owner.TryActivate(definition.RoomName, environment,
            out var leaseA),
        "concurrent route fixture activation A failed");

    var npc = new NormNpc { m_PEnvir = environment };
    Assert(ReferenceEquals(npc, M2Share.ObjectManager.Get(npc.ObjectId)),
        "concurrent route NPC was not published");
    var table = new NativeDynamicRoomPasScriptRouteTable(scriptRoot);
    var plan = Plan(definition, exactPath, true);
    NativeDynamicRoomActivationLease leaseB = null;
    NativeDynamicRoomPasScriptBindingHandle handleA = null;
    NativeDynamicRoomPasScriptBindingHandle handleB = null;
    Exception errorA = null;
    Exception errorB = null;

    using var aPassedInitialCheck = new ManualResetEventSlim();
    using var allowAPublish = new ManualResetEventSlim();
    using var aPublished = new ManualResetEventSlim();
    using var allowAPostCheck = new ManualResetEventSlim();
    using var bPublished = new ManualResetEventSlim();
    using var allowBPostCheck = new ManualResetEventSlim();
    using var bCompleted = new ManualResetEventSlim();

    Action<NativeDynamicRoomPasScriptBindingHandle, bool> checkpoint =
        (handle, published) =>
        {
            if (ReferenceEquals(handle.ActivationLease, leaseA))
            {
                if (!published)
                {
                    aPassedInitialCheck.Set();
                    allowAPublish.Wait();
                }
                else
                {
                    aPublished.Set();
                    allowAPostCheck.Wait();
                }
            }
            else if (ReferenceEquals(handle.ActivationLease, leaseB)
                     && published)
            {
                bPublished.Set();
                allowBPostCheck.Wait();
            }
        };
    var checkpointField = typeof(NativeDynamicRoomPasScriptRouteTable)
        .GetField("_registrationCheckpointForTests",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
    checkpointField.SetValue(table, checkpoint);

    var threadA = new Thread(() =>
    {
        try
        {
            handleA = table.Register(npc, leaseA, plan);
        }
        catch (Exception ex)
        {
            errorA = ex;
        }
    }) { IsBackground = true };
    var threadB = new Thread(() =>
    {
        try
        {
            handleB = table.Register(npc, leaseB, plan);
        }
        catch (Exception ex)
        {
            errorB = ex;
        }
        finally
        {
            bCompleted.Set();
        }
    }) { IsBackground = true };
    var threadBStarted = false;

    try
    {
        threadA.Start();
        Assert(aPassedInitialCheck.Wait(TimeSpan.FromSeconds(5)),
            "registration A did not pass its initial active check");
        Assert(owner.TrySetLeaseState(leaseA, 1)
               && owner.TrySetLeaseState(leaseA, 0),
            "concurrent route fixture could not retire activation A");
        SetActivationIndex(owner, leaseA.Index - 1);
        Assert(owner.TryActivate(definition.RoomName, environment,
                out leaseB),
            "concurrent route fixture activation B failed");
        Equal(leaseA.Index, leaseB.Index,
            "concurrent ABA fixture did not reuse the numeric index");

        threadB.Start();
        threadBStarted = true;
        Assert(bPublished.Wait(TimeSpan.FromSeconds(5)),
            "registration B did not publish before its post-check");
        allowAPublish.Set();
        Assert(aPublished.Wait(TimeSpan.FromSeconds(5)),
            "registration A did not overwrite B before its post-check");
        allowBPostCheck.Set();
        Assert(bCompleted.Wait(TimeSpan.FromSeconds(5)),
            "registration B did not complete its displaced post-check");
        allowAPostCheck.Set();
    }
    finally
    {
        allowAPublish.Set();
        allowBPostCheck.Set();
        allowAPostCheck.Set();
        threadA.Join(TimeSpan.FromSeconds(5));
        if (threadBStarted) threadB.Join(TimeSpan.FromSeconds(5));
        checkpointField.SetValue(table, null);
    }

    if (errorA != null)
        throw new InvalidOperationException("registration A failed", errorA);
    if (errorB != null)
        throw new InvalidOperationException("registration B failed", errorB);
    Assert(handleA != null && handleB != null,
        "concurrent registration did not return both handles");
    Equal(NativeDynamicRoomPasScriptRouteState.ExactCurrent,
        table.ResolveCurrent(npc, out var resolvedHandle, out _),
        "A rollback did not restore displaced current route B");
    Assert(ReferenceEquals(handleB, resolvedHandle)
           && table.ValidateExpected(npc, handleB, out _),
        "restored current route B did not revalidate");
    Assert(!table.ValidateExpected(npc, handleA, out _),
        "stale registration A revalidated after rollback");
}

static void AssertRouteLockBoundaries()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    string sourcePath = null;
    while (directory != null)
    {
        var candidate = Path.Combine(directory.FullName, "GameSvr", "Maps",
            "NativeDynamicRoomPasScriptRouteTable.cs");
        if (File.Exists(candidate))
        {
            sourcePath = candidate;
            break;
        }
        directory = directory.Parent;
    }

    Assert(sourcePath != null, "route-table source file was not found");
    var source = File.ReadAllText(sourcePath);
    var lockBodies = ExtractRouteLockBodies(source).ToArray();
    Assert(lockBodies.Length > 0, "route-table source contains no route locks");
    foreach (var lockBody in lockBodies)
    {
        Assert(!lockBody.Contains("IsCurrentActive(",
                StringComparison.Ordinal)
               && !lockBody.Contains("File.Exists(",
                   StringComparison.Ordinal)
               && !lockBody.Contains("ValidateOutsideRouteLock(",
                   StringComparison.Ordinal)
               && !lockBody.Contains("M2Share.ObjectManager",
                   StringComparison.Ordinal)
               && !lockBody.Contains("_registrationCheckpointForTests",
                   StringComparison.Ordinal),
            "route lock contains external validation or callback work");
    }
}

static IEnumerable<string> ExtractRouteLockBodies(string source)
{
    const string marker = "lock (_syncRoot)";
    var searchIndex = 0;
    while (true)
    {
        var lockIndex = source.IndexOf(marker, searchIndex,
            StringComparison.Ordinal);
        if (lockIndex < 0) yield break;
        var openBrace = source.IndexOf('{', lockIndex + marker.Length);
        Assert(openBrace >= 0, "route lock has no opening brace");

        var depth = 0;
        var closeBrace = -1;
        for (var index = openBrace; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            else if (source[index] == '}' && --depth == 0)
            {
                closeBrace = index;
                break;
            }
        }

        Assert(closeBrace >= 0, "route lock has no closing brace");
        yield return source.Substring(openBrace,
            closeBrace - openBrace + 1);
        searchIndex = closeBrace + 1;
    }
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
