using GameSvr;
using System.Reflection;
using System.Text;

PrepareRuntimeConfig();
M2Share.ObjectManager = new ObjectManager();
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var root = Path.Combine(Path.GetTempPath(), "dynroom-runtime-"
    + Guid.NewGuid().ToString("N"));
var scriptRoot = Path.Combine(root, "DynRoomScripts");
Directory.CreateDirectory(scriptRoot);

try
{
    RunCommittedLifecycleCheck(root, scriptRoot);
    RunPasExecutionGateCheck(root, scriptRoot);
    RunRouteRollbackAndAbaCheck(root, scriptRoot);
    RunEventLoaderRollbackCheck(root, scriptRoot);
    RunMissingScriptTombstoneCheck(root, scriptRoot);
    RunZeroEventPublicAbortCheck(root, scriptRoot);
    RunForeignLeaseRejectionCheck(root, scriptRoot);
}
finally
{
    Directory.Delete(root, true);
}

Console.WriteLine("DynRoomRuntimeTransactionCheck PASS "
    + "commit=route+event rollback=reverse ABA=exact "
    + "missing=tombstone lifecycle=gate PAS=serialized-exact "
    + "pending=notify-then-run abort=committed-denied "
    + "cross-room-reserve=allowed startup=connected");

static void RunCommittedLifecycleCheck(string eventRoot, string scriptRoot)
{
    const string roomName = "RuntimeSuccess";
    var definition = Definition(roomName);
    var environment = Environment(roomName);
    var scriptPath = WriteScript(scriptRoot, roomName);
    WriteDescriptor(eventRoot, roomName, "1 5 1,1\r\n");
    long tick = 1_000;
    var manager = new NativeDynamicRoomManager(() => tick);
    var routes = new NativeDynamicRoomPasScriptRouteTable(scriptRoot);
    var runtime = new NativeDynamicRoomRuntime(manager, routes, eventRoot);
    var eventManager = new EventManager();
    var adapter = new NativeDynamicRoomEventActivationAdapter(eventManager,
        environment);
    Assert(manager.RegisterIdleRoom(definition, 0, environment, 0,
            runtime.TryBeginClosingCleanup,
            runtime.TryFinalizeIdleCleanup,
            runtime.TryCloseActivationEvents),
        "runtime success room registration failed");
    Assert(runtime.TryReserveIdleRoomLease(roomName, null, out var lease),
        "runtime success lease reservation failed");
    Assert(ReferenceEquals(lease.Definition, definition),
        "runtime success lease lost definition identity");
    var npc = Npc(environment);
    var registration = Route(npc, Binding(definition, scriptPath, true));

    Assert(runtime.TryCommitReservedActivation(lease, adapter,
            new[] { registration }, out var diagnostics),
        "runtime success activation failed: " + string.Join(" | ", diagnostics));
    Assert(runtime.TryCommitReservedActivation(lease, adapter,
            new[] { Route(npc, registration.Binding) }, out _),
        "exact activation retry was not idempotent");
    Assert(!runtime.TryCommitReservedActivation(lease, adapter,
            Array.Empty<NativeDynamicRoomPasRouteRegistration>(), out _),
        "different route batch was accepted as an idempotent retry");
    Equal(NativeDynamicRoomPasScriptRouteState.ExactCurrent,
        routes.ResolveCurrent(npc, out var handle, out var exactPath),
        "committed runtime route was not exact-current");
    Assert(routes.ValidateExpected(npc, handle, out var validatedPath)
           && exactPath == validatedPath && adapter.HasActivationEvents,
        "routes and F9 were not visible after the same commit");
    var activationEvent = ActiveEvents(eventManager).Single();

    tick += 120_001;
    runtime.Run();
    Equal(1, DynamicState(environment),
        "runtime did not enter closing state");
    Assert(!routes.ValidateExpected(npc, handle, out _),
        "closing cleanup retained an active PAS route");
    tick += 600_001;
    runtime.Run();
    Equal(0, DynamicState(environment),
        "runtime did not return the room to idle");
    Assert(activationEvent.m_boClosed && !adapter.HasActivationEvents,
        "runtime lifecycle did not close its exact activation event");
    Assert(runtime.TryReserveIdleRoomLease(roomName, null, out _),
        "runtime lifecycle did not make the physical room reusable");
}

static void RunPasExecutionGateCheck(string eventRoot, string scriptRoot)
{
    const string roomName = "RuntimePasGate";
    var definition = Definition(roomName);
    var environment = Environment(roomName);
    var siblingEnvironment = Environment(roomName + "Sibling");
    var scriptPath = WriteScript(scriptRoot, roomName);
    WriteDescriptor(eventRoot, roomName, "5 5 1,1\r\n");
    long tick = 1_000;
    var manager = new NativeDynamicRoomManager(() => tick);
    var routes = new NativeDynamicRoomPasScriptRouteTable(scriptRoot);
    var runtime = new NativeDynamicRoomRuntime(manager, routes, eventRoot);
    var eventManager = new EventManager();
    var adapter = new NativeDynamicRoomEventActivationAdapter(eventManager,
        environment);
    Assert(manager.RegisterIdleRoom(definition, 0, environment, 0,
            runtime.TryBeginClosingCleanup,
            runtime.TryFinalizeIdleCleanup,
            runtime.TryCloseActivationEvents),
        "PAS gate room registration failed");
    Assert(manager.RegisterIdleRoom(definition, 1, siblingEnvironment, 0,
            runtime.TryBeginClosingCleanup,
            runtime.TryFinalizeIdleCleanup,
            runtime.TryCloseActivationEvents),
        "PAS gate sibling room registration failed");
    Assert(runtime.TryReserveIdleRoomLease(roomName, null, out var leaseA),
        "PAS gate lease A reservation failed");
    var npc = Npc(environment);
    var binding = Binding(definition, scriptPath, true);
    Assert(runtime.TryCommitReservedActivation(leaseA, adapter,
            new[] { Route(npc, binding) }, out var diagnostics),
        "PAS gate activation A failed: " + string.Join(" | ", diagnostics));
    Equal(NativeDynamicRoomPasScriptRouteState.ExactCurrent,
        routes.ResolveCurrent(npc, out var handleA, out _),
        "PAS gate activation A route was not current");

    using var callbackEntered = new ManualResetEventSlim();
    using var allowCallbackReturn = new ManualResetEventSlim();
    using var runBlockedOnGate = new ManualResetEventSlim();
    using var runReturned = new ManualResetEventSlim();
    var mutationWaitCheckpoint = typeof(NativeDynamicRoomRuntime)
        .GetField("_mutationWaitCheckpointForTests",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
    mutationWaitCheckpoint.SetValue(runtime,
        (Action)(() => runBlockedOnGate.Set()));
    Exception pasFailure = null;
    Exception runFailure = null;
    var pasResult = false;
    var sameThreadRunDeferred = false;
    var pasThread = new Thread(() =>
    {
        try
        {
            pasResult = runtime.TryExecuteExpectedPas(npc, handleA, path =>
            {
                Equal(scriptPath, path,
                    "PAS gate callback received a non-exact path");
                manager.NotifyPlayerRemoved(environment);
                manager.NotifyPlayerRemoved(environment);
                manager.Run();
                Equal(2, DynamicState(environment),
                    "same-thread pending lifecycle ran inside PAS callback");
                Assert(runtime.TryReserveIdleRoomLease(roomName, null,
                           out var siblingLease)
                       && ReferenceEquals(siblingLease.Environment,
                           siblingEnvironment),
                    "same-thread PAS could not reserve a different idle room");
                Assert(manager.TryAbortReservedRoomLease(siblingLease),
                    "same-thread PAS could not abort its different-room reservation");
                sameThreadRunDeferred = true;
                callbackEntered.Set();
                Assert(allowCallbackReturn.Wait(TimeSpan.FromSeconds(5)),
                    "PAS gate callback was not released");
                return true;
            });
        }
        catch (Exception ex)
        {
            pasFailure = ex;
        }
    }) { IsBackground = true };
    var runThread = new Thread(() =>
    {
        try
        {
            manager.Run();
        }
        catch (Exception ex)
        {
            runFailure = ex;
        }
        finally
        {
            runReturned.Set();
        }
    }) { IsBackground = true };
    var runStarted = false;

    try
    {
        tick += 120_001;
        pasThread.Start();
        Assert(callbackEntered.Wait(TimeSpan.FromSeconds(5)),
            "PAS gate callback did not enter");
        runThread.Start();
        runStarted = true;
        Assert(runBlockedOnGate.Wait(TimeSpan.FromSeconds(5)),
            "Run did not contend on the active PAS execution gate");
        Assert(!runReturned.IsSet,
            "Run crossed the PAS execution gate before callback return");
        Equal(2, DynamicState(environment),
            "room entered closing state while PAS callback held the gate");
    }
    finally
    {
        allowCallbackReturn.Set();
        pasThread.Join(TimeSpan.FromSeconds(5));
        if (runStarted) runThread.Join(TimeSpan.FromSeconds(5));
        mutationWaitCheckpoint.SetValue(runtime, null);
    }

    if (pasFailure != null)
        throw new InvalidOperationException("PAS gate callback failed", pasFailure);
    if (runFailure != null)
        throw new InvalidOperationException("PAS gate teardown failed", runFailure);
    Assert(!pasThread.IsAlive && !runThread.IsAlive
           && sameThreadRunDeferred
           && pasResult && runReturned.IsSet,
        "PAS gate callback or deferred teardown did not complete");
    Equal(1, DynamicState(environment),
        "room did not enter closing state after PAS callback returned");
    Assert(!runtime.TryExecuteExpectedPas(npc, handleA, _ => true),
        "closing activation A PAS handle remained executable");

    tick += 600_001;
    runtime.Run();
    Equal(0, DynamicState(environment),
        "PAS gate activation A did not return to idle");
    Assert(runtime.TryReserveIdleRoomLease(roomName, null, out var leaseB),
        "PAS gate lease B reservation failed");
    Assert(runtime.TryCommitReservedActivation(leaseB, adapter,
            new[] { Route(npc, binding) }, out diagnostics),
        "PAS gate activation B failed: " + string.Join(" | ", diagnostics));
    Equal(NativeDynamicRoomPasScriptRouteState.ExactCurrent,
        routes.ResolveCurrent(npc, out var handleB, out _),
        "PAS gate activation B route was not current");
    var exceptionObserved = false;
    try
    {
        runtime.TryExecuteExpectedPas(npc, handleB,
            _ => throw new ApplicationException("expected PAS failure"));
    }
    catch (ApplicationException ex) when (ex.Message == "expected PAS failure")
    {
        exceptionObserved = true;
    }
    Assert(exceptionObserved
           && runtime.TryExecuteExpectedPas(npc, handleB, _ => true),
        "PAS callback exception leaked execution accounting or gate state");
    var staleExecuted = false;
    Assert(!runtime.TryExecuteExpectedPas(npc, handleA, _ =>
           {
               staleExecuted = true;
               return true;
           }) && !staleExecuted,
        "stale activation A handle executed against activation B");

    var foreignRoutes = new NativeDynamicRoomPasScriptRouteTable(scriptRoot);
    var foreignHandle = foreignRoutes.Register(npc, leaseB, binding);
    var foreignExecuted = false;
    Assert(!runtime.TryExecuteExpectedPas(npc, foreignHandle, _ =>
           {
               foreignExecuted = true;
               return true;
           }) && !foreignExecuted,
        "foreign route-table handle executed in the committed session");
    Assert(runtime.TryExecuteExpectedPas(npc, handleB, path =>
            path == scriptPath
            && runtime.TryExecuteExpectedPas(npc, handleB,
                nestedPath => nestedPath == path)),
        "exact activation B handle did not execute through the reentrant gate");
}

static void RunRouteRollbackAndAbaCheck(string eventRoot, string scriptRoot)
{
    const string roomName = "RuntimeRouteRollback";
    var definition = Definition(roomName);
    var wrongDefinition = Definition(roomName);
    var environment = Environment(roomName);
    var scriptPath = WriteScript(scriptRoot, roomName);
    WriteDescriptor(eventRoot, roomName, "2 5 1,1\r\n");
    var manager = new NativeDynamicRoomManager();
    var routes = new NativeDynamicRoomPasScriptRouteTable(scriptRoot);
    var runtime = new NativeDynamicRoomRuntime(manager, routes, eventRoot);
    var eventManager = new EventManager();
    var adapter = new NativeDynamicRoomEventActivationAdapter(eventManager,
        environment);
    Assert(manager.RegisterIdleRoom(definition, 0, environment, 0,
            runtime.TryBeginClosingCleanup,
            runtime.TryFinalizeIdleCleanup,
            runtime.TryCloseActivationEvents),
        "route rollback room registration failed");
    Assert(runtime.TryReserveIdleRoomLease(roomName, null, out var leaseA),
        "route rollback lease A failed");
    var firstNpc = Npc(environment);
    var secondNpc = Npc(environment);
    var first = Route(firstNpc, Binding(definition, scriptPath, true));
    var wrongSecond = Route(secondNpc,
        Binding(wrongDefinition, scriptPath, true));

    Assert(!runtime.TryCommitReservedActivation(leaseA, adapter,
            new[] { first, wrongSecond }, out _),
        "wrong second route committed a partial activation");
    Equal(NativeDynamicRoomPasScriptRouteState.DynamicUnavailableOrStale,
        routes.ResolveCurrent(firstNpc, out _, out _),
        "first route survived reverse rollback");
    Assert(!adapter.HasActivationEvents,
        "route failure created activation events");
    Assert(runtime.TryReserveIdleRoomLease(roomName, null, out var leaseB),
        "route failure did not abort and reuse the exact room");
    Assert(!runtime.TryBeginClosingCleanup(leaseA),
        "stale A cleanup was accepted while B had no session");

    var current = Route(firstNpc, Binding(definition, scriptPath, true));
    Assert(runtime.TryCommitReservedActivation(leaseB, adapter,
            new[] { current }, out var diagnostics),
        "activation B failed after A rollback: " + string.Join(" | ", diagnostics));
    Equal(NativeDynamicRoomPasScriptRouteState.ExactCurrent,
        routes.ResolveCurrent(firstNpc, out var handleB, out _),
        "activation B route was not current");
    Assert(!runtime.TryBeginClosingCleanup(leaseB),
        "state-2 B cleanup retired an active route");
    Assert(!runtime.TryBeginClosingCleanup(leaseA),
        "stale activation A cleanup was accepted over B");
    Assert(routes.ValidateExpected(firstNpc, handleB, out _),
        "stale activation A cleanup removed B's route");
}

static void RunEventLoaderRollbackCheck(string eventRoot, string scriptRoot)
{
    const string roomName = "RuntimeMissingEvent";
    var definition = Definition(roomName);
    var environment = Environment(roomName);
    var scriptPath = WriteScript(scriptRoot, roomName);
    WriteDescriptor(eventRoot, roomName, "4 5 1,1\r\n");
    var manager = new NativeDynamicRoomManager();
    var routes = new NativeDynamicRoomPasScriptRouteTable(scriptRoot);
    var runtime = new NativeDynamicRoomRuntime(manager, routes, eventRoot);
    var eventManager = new EventManager();
    var adapter = new NativeDynamicRoomEventActivationAdapter(eventManager,
        environment);
    Assert(manager.RegisterIdleRoom(definition, 0, environment, 0,
            runtime.TryBeginClosingCleanup,
            runtime.TryFinalizeIdleCleanup,
            runtime.TryCloseActivationEvents),
        "missing-event room registration failed");
    Assert(runtime.TryReserveIdleRoomLease(roomName, null, out var lease),
        "missing-event lease reservation failed");
    var preexisting = new Event(environment, 2, 2, 99, int.MaxValue, true);
    eventManager.AddEvent(preexisting);
    var npc = Npc(environment);
    var descriptorPath = Path.Combine(eventRoot,
        NativeDynamicRoomEventDescriptorLoader.BuildFileName(roomName));

    IReadOnlyList<string> diagnostics;
    using (new FileStream(descriptorPath, FileMode.Open, FileAccess.ReadWrite,
               FileShare.None))
    {
        Assert(!runtime.TryCommitReservedActivation(lease, adapter,
                new[] { Route(npc, Binding(definition, scriptPath, true)) },
                out diagnostics),
            "unreadable event descriptor committed an activation");
    }
    Assert(diagnostics.Count > 0 && !preexisting.m_boClosed,
        "event loader rollback closed a preexisting event or lost diagnostics");
    Equal(NativeDynamicRoomPasScriptRouteState.DynamicUnavailableOrStale,
        routes.ResolveCurrent(npc, out _, out _),
        "event loader failure retained its PAS route");
    Assert(runtime.TryReserveIdleRoomLease(roomName, null, out _),
        "event loader failure did not release the room");
}

static void RunMissingScriptTombstoneCheck(string eventRoot, string scriptRoot)
{
    const string roomName = "RuntimeTombstone";
    var definition = Definition(roomName);
    var environment = Environment(roomName);
    var missingPath = Path.Combine(scriptRoot, "Missing-" + roomName + ".pas");
    WriteDescriptor(eventRoot, roomName, "3 5 1,1\r\n");
    var manager = new NativeDynamicRoomManager();
    var routes = new NativeDynamicRoomPasScriptRouteTable(scriptRoot);
    var runtime = new NativeDynamicRoomRuntime(manager, routes, eventRoot);
    var eventManager = new EventManager();
    var adapter = new NativeDynamicRoomEventActivationAdapter(eventManager,
        environment);
    Assert(manager.RegisterIdleRoom(definition, 0, environment, 0,
            runtime.TryBeginClosingCleanup,
            runtime.TryFinalizeIdleCleanup,
            runtime.TryCloseActivationEvents),
        "tombstone room registration failed");
    Assert(runtime.TryReserveIdleRoomLease(roomName, null, out var lease),
        "tombstone lease reservation failed");
    var npc = Npc(environment);

    Assert(runtime.TryCommitReservedActivation(lease, adapter,
            new[] { Route(npc, Binding(definition, missingPath, false)) },
            out var diagnostics),
        "legal missing-script tombstone rejected activation: "
        + string.Join(" | ", diagnostics));
    Equal(NativeDynamicRoomPasScriptRouteState.DynamicUnavailableOrStale,
        routes.ResolveCurrent(npc, out _, out _),
        "missing-script tombstone exposed a PAS fallback");
    var tombstoneHandle = RegisteredRouteHandle(routes, npc);
    var tombstoneExecuted = false;
    Assert(!runtime.TryExecuteExpectedPas(npc, tombstoneHandle, _ =>
           {
               tombstoneExecuted = true;
               return true;
           }) && !tombstoneExecuted,
        "missing-script tombstone entered PAS execution");
    Assert(adapter.HasActivationEvents,
        "tombstone activation did not commit its independent event state");
}

static void RunZeroEventPublicAbortCheck(string eventRoot, string scriptRoot)
{
    const string roomName = "RuntimeZeroEventAbort";
    var definition = Definition(roomName);
    var environment = Environment(roomName);
    WriteDescriptor(eventRoot, roomName, string.Empty);
    var manager = new NativeDynamicRoomManager();
    var routes = new NativeDynamicRoomPasScriptRouteTable(scriptRoot);
    var runtime = new NativeDynamicRoomRuntime(manager, routes, eventRoot);
    var adapter = new NativeDynamicRoomEventActivationAdapter(
        new EventManager(), environment);
    Assert(manager.RegisterIdleRoom(definition, 0, environment, 0,
            runtime.TryBeginClosingCleanup,
            runtime.TryFinalizeIdleCleanup,
            runtime.TryCloseActivationEvents),
        "zero-event abort room registration failed");
    Assert(runtime.TryReserveIdleRoomLease(roomName, null, out var lease),
        "zero-event abort lease reservation failed");
    Assert(runtime.TryCommitReservedActivation(lease, adapter,
            Array.Empty<NativeDynamicRoomPasRouteRegistration>(),
            out var diagnostics),
        "zero-event activation failed: " + string.Join(" | ", diagnostics));
    Assert(!adapter.HasActivationEvents,
        "zero-event activation unexpectedly attached an event");
    Assert(!manager.TryAbortReservedRoomLease(lease),
        "manager public abort bypassed a committed zero-event session");
    Equal(2, DynamicState(environment),
        "rejected public abort changed the committed room state");
}

static void RunForeignLeaseRejectionCheck(string eventRoot, string scriptRoot)
{
    const string roomName = "RuntimeForeignLease";
    var definition = Definition(roomName);
    var environment = Environment(roomName);
    var manager = new NativeDynamicRoomManager();
    var routes = new NativeDynamicRoomPasScriptRouteTable(scriptRoot);
    var runtime = new NativeDynamicRoomRuntime(manager, routes, eventRoot);
    var adapter = new NativeDynamicRoomEventActivationAdapter(
        new EventManager(), environment);
    Assert(!runtime.TryBeginClosingCleanup(null)
           && !runtime.TryFinalizeIdleCleanup(null)
           && !runtime.TryCloseActivationEvents(null),
        "runtime cleanup accepted a null lease");
    Assert(manager.RegisterIdleRoom(definition, 0, environment, 0,
            runtime.TryBeginClosingCleanup,
            runtime.TryFinalizeIdleCleanup,
            runtime.TryCloseActivationEvents),
        "foreign-lease room registration failed");
    Assert(runtime.TryReserveIdleRoomLease(roomName, null,
            out var managerLease),
        "manager lease reservation failed");

    var foreignOwner = new NativeDynamicRoomLeaseOwner();
    Assert(foreignOwner.TryRegisterDefinitionModel(definition)
           && foreignOwner.TryAppendEnvironment(roomName, environment),
        "foreign lease owner fixture failed");
    Assert(foreignOwner.TryActivate(roomName, environment,
            out var foreignLease),
        "foreign lease activation fixture failed");
    Assert(!runtime.TryCommitReservedActivation(foreignLease, adapter,
            Array.Empty<NativeDynamicRoomPasRouteRegistration>(), out _),
        "zero-event activation accepted a foreign manager lease");
    Assert(manager.TryGetActiveRoom(roomName, managerLease.Index,
            out var active)
           && ReferenceEquals(active, environment),
        "foreign lease attempt disturbed the manager lease");
    Assert(manager.TryAbortReservedRoomLease(managerLease),
        "foreign lease rejection left the manager lease unabortable");
}

static NativeDynamicRoomDefinition Definition(string roomName)
{
    return new NativeDynamicRoomDefinition(roomName, 1, 1,
        roomName, "D000", "raw", "raw", Array.Empty<string>(),
        Array.Empty<NativeDynamicRoomConfiguredNpcDefinition>(), 1);
}

static Envirnoment Environment(string name)
{
    var environment = new Envirnoment { sMapName = name };
    typeof(Envirnoment).GetMethod("Initialize",
        BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(environment, new object[] { (short)6, (short)6 });
    return environment;
}

static NormNpc Npc(Envirnoment environment)
{
    var npc = new NormNpc { m_PEnvir = environment };
    Assert(npc.ObjectId > 0
           && ReferenceEquals(M2Share.ObjectManager.Get(npc.ObjectId), npc),
        "runtime NPC was not published exactly");
    return npc;
}

static NativeDynamicRoomPasRouteRegistration Route(NormNpc npc,
    NativeDynamicRoomDynamicNpcScriptBinding binding)
{
    return new NativeDynamicRoomPasRouteRegistration(npc, binding);
}

static NativeDynamicRoomPasScriptBindingHandle RegisteredRouteHandle(
    NativeDynamicRoomPasScriptRouteTable routes, NormNpc npc)
{
    var byNpc = (Dictionary<NormNpc,
        NativeDynamicRoomPasScriptBindingHandle>)typeof(
            NativeDynamicRoomPasScriptRouteTable)
        .GetField("_routesByNpc",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(routes)!;
    Assert(byNpc.TryGetValue(npc, out var handle),
        "runtime route handle was not registered");
    return handle;
}

static NativeDynamicRoomDynamicNpcScriptBinding Binding(
    NativeDynamicRoomDefinition definition, string path, bool hasScript)
{
    return new NativeDynamicRoomDynamicNpcScriptBinding(definition,
        NativeDynamicRoomDynamicNpcScriptRole.HiddenController, null,
        Path.GetFileName(path), Path.GetFullPath(path), hasScript,
        hasScript ? 1 : 0, string.Empty);
}

static string WriteScript(string directory, string roomName)
{
    var path = Path.Combine(directory, roomName + ".pas");
    File.WriteAllText(path, "program Mir2; begin end.");
    return path;
}

static void WriteDescriptor(string directory, string roomName, string contents)
{
    File.WriteAllBytes(Path.Combine(directory,
            NativeDynamicRoomEventDescriptorLoader.BuildFileName(roomName)),
        Encoding.GetEncoding(936).GetBytes(contents));
}

static IReadOnlyList<Event> ActiveEvents(EventManager manager)
{
    return (IReadOnlyList<Event>)typeof(EventManager)
        .GetField("_eventList", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(manager)!;
}

static int DynamicState(Envirnoment environment)
{
    return (int)typeof(Envirnoment)
        .GetProperty("DynamicRoomState",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(environment)!;
}

static void PrepareRuntimeConfig()
{
    var runtimeDirectory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(runtimeDirectory, "!Setup.txt"),
        "[Server]" + System.Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "String.ini"),
        "[String]" + System.Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "Command.conf"),
        "[Command]" + System.Environment.NewLine);
    var shareDirectory = Path.Combine(Path.GetFullPath(
        Path.Combine(runtimeDirectory, "..")), "Share");
    Directory.CreateDirectory(shareDirectory);
    File.WriteAllText(Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + System.Environment.NewLine);
    File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"),
        "[Integer]" + System.Environment.NewLine);
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
