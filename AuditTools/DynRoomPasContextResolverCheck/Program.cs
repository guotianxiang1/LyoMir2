using System.Collections;
using System.Reflection;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.UserEngine = new UserEngine();
M2Share.ObjectManager = new ObjectManager();
M2Share.MapManager = new MapManager();

var root = Path.Combine(Path.GetTempPath(), "dynroom-pas-context-"
    + Guid.NewGuid().ToString("N"));
var dynamicRoot = Path.Combine(root, "DynRoomScripts");
var legacyRoot = Path.Combine(root, "PsNpcscripts");
Directory.CreateDirectory(dynamicRoot);
Directory.CreateDirectory(legacyRoot);

try
{
    var exactPath = WriteScript(dynamicRoot, "Exact", 100);
    var dynamicFallbackPath = WriteScript(legacyRoot, "DynamicNpc", 900);
    _ = WriteScript(legacyRoot, "LegacyNpc", 200);
    _ = WriteScript(legacyRoot, "MissingDynamic", 800);

    var definition = Definition("ContextRoom");
    var environment = new Envirnoment();
    var owner = new NativeDynamicRoomLeaseOwner();
    Assert(owner.TryRegisterDefinitionModel(definition),
        "dynamic definition registration failed");
    Assert(owner.TryAppendEnvironment(definition.RoomName, environment),
        "dynamic environment registration failed");
    Assert(owner.TryActivate(definition.RoomName, environment,
            out var leaseA), "activation A failed");

    var routes = new NativeDynamicRoomPasScriptRouteTable(dynamicRoot);
    var dynamicNpc = NewNpc("DynamicNpc", "D000", environment);
    var handleA = routes.Register(dynamicNpc, leaseA,
        Plan(definition, exactPath, true));
    var host = new PasScriptHost(root, routes);
    M2Share.PasEngine = host;
    var player = NewPlayer("ContextPlayer", environment);
    var item = new TUserItem();

    var exact = host.ResolveNpcScript(dynamicNpc);
    Equal(NpcPasScriptResolutionKind.ExactDynamic, exact.Kind,
        "dynamic NPC was not centrally classified exact");
    Equal(Path.GetFullPath(exactPath), exact.ScriptPath,
        "central resolver changed the exact path");
    Assert(ReferenceEquals(handleA, exact.DynamicBindingHandle),
        "central resolver lost the exact binding handle");
    Equal(0, host.CallLabel(dynamicFallbackPath, "@main", player,
            dynamicNpc).AsInt(),
        "direct public label call bypassed the expected dynamic handle");
    Assert(!host.TryCallNpcInputDialog(dynamicNpc, 7, "A", true,
            player, out _),
        "dynamic input executed without a bound interaction handle");
    Assert(!host.TryCallNpcItemProcedure(dynamicNpc, "CommitItem",
            player, item, out _),
        "dynamic item commit executed without a bound interaction handle");
    Assert(!host.TryCallNpcProcedure(dynamicNpc,
            new[] { "_Callback", "Callback" }, player, out _),
        "dynamic callback executed without a bound interaction handle");
    Equal(105, host.GetLimitValue(dynamicNpc, player),
        "current exact dynamic limit route failed");

    Assert(host.TryCallNpcLabel(dynamicNpc, "@main", player,
            out var value, out var found) && found,
        "dynamic main label did not bind its interaction handle");
    Equal(101, value.AsInt(), "dynamic label used a basename fallback");
    Assert(host.TryCallNpcProcedure(dynamicNpc,
            new[] { "_Callback", "Callback" }, player, out value),
        "dynamic callback did not use the bound exact route");
    Equal(102, value.AsInt(), "dynamic callback used the wrong script");
    Assert(host.TryCallNpcInputDialog(dynamicNpc, 7, "A", true,
            player, out value), "dynamic input did not use the bound exact route");
    Equal(103, value.AsInt(), "dynamic input used the wrong script");
    Assert(host.TryCallNpcItemProcedure(dynamicNpc, "CommitItem",
            player, item, out value),
        "dynamic item commit did not use the bound exact route");
    Equal(104, value.AsInt(), "dynamic item commit used the wrong script");
    Assert(host.TryCaptureNpcInteraction(player, dynamicNpc,
            out var asyncCallbackA, out var capturedKind)
           && capturedKind == NpcPasScriptResolutionKind.ExactDynamic,
        "activation A callback token was not captured exactly");
    var stateA = GetNpcState(host, dynamicNpc);
    AssertStateIdentity(host, dynamicNpc, handleA, null);

    Assert(owner.TrySetLeaseState(leaseA, 1)
           && owner.TrySetLeaseState(leaseA, 0),
        "activation A teardown failed");
    Equal(NpcPasScriptResolutionKind.DynamicUnavailable,
        host.ResolveNpcScript(dynamicNpc).Kind,
        "stale dynamic identity fell through to legacy");
    AssertDynamicInteractionUnavailable(host, dynamicNpc, player, item,
        "stale A");
    Equal(0, host.GetLimitValue(dynamicNpc, player),
        "stale dynamic limit fell through to legacy");
    var pendingBeforeUnavailable = host.PendingCallCount;
    host.ScheduleCall(exactPath, "DeferredProbe", player, dynamicNpc, 0);
    Equal(pendingBeforeUnavailable, host.PendingCallCount,
        "DynamicUnavailable deferred call entered the queue");

    Assert(owner.TryActivate(definition.RoomName, environment,
            out var leaseB), "activation B failed");
    var handleB = routes.Register(dynamicNpc, leaseB,
        Plan(definition, exactPath, true));
    Equal(NpcPasScriptResolutionKind.ExactDynamic,
        host.ResolveNpcScript(dynamicNpc).Kind,
        "activation B did not become exact-current");
    AssertDynamicInteractionUnavailable(host, dynamicNpc, player, item,
        "old A interaction against B");
    Equal(105, host.GetLimitValue(dynamicNpc, player),
        "fresh B limit preflight failed");
    Assert(host.TryCallNpcLabel(dynamicNpc, "@main", player,
            out value, out found) && found,
        "fresh B label did not rebind the interaction");
    Equal(101, value.AsInt(), "fresh B label used the fallback script");
    Assert(host.TryCallNpcInputDialog(dynamicNpc, 7, "B", true,
            player, out value) && value.AsInt() == 103,
        "fresh B interaction did not execute after explicit rebind");
    Assert(!host.TryCallNpcProcedure(asyncCallbackA,
            new[] { "_Callback", "Callback" }, out _),
        "asynchronous callback token A executed against activation B");
    Assert(host.TryCaptureNpcInteraction(player, dynamicNpc,
            out var asyncCallbackB, out capturedKind)
           && capturedKind == NpcPasScriptResolutionKind.ExactDynamic
           && host.TryCallNpcProcedure(asyncCallbackB,
               new[] { "_Callback", "Callback" }, out value)
           && value.AsInt() == 102,
        "fresh activation B callback token did not execute exactly");
    AssertStateIdentity(host, dynamicNpc, handleB, stateA);
    var pendingBeforeNoRuntime = host.PendingCallCount;
    host.ScheduleCall(exactPath, "DeferredProbe", player, dynamicNpc, 0);
    Equal(pendingBeforeNoRuntime, host.PendingCallCount,
        "dynamic deferred call entered a host without a runtime gate");

    var missingNpc = NewNpc("MissingDynamic", "D000", environment);
    routes.Register(missingNpc, leaseB,
        Plan(definition, Path.Combine(dynamicRoot, "Missing.pas"), false));
    Equal(NpcPasScriptResolutionKind.DynamicUnavailable,
        host.ResolveNpcScript(missingNpc).Kind,
        "missing dynamic route fell through to matching legacy basename");
    Assert(!host.TryCallNpcLabel(missingNpc, "@main", player,
            out _, out found) && !found,
        "missing dynamic label executed its legacy basename");

    var legacyEnvironment = new Envirnoment();
    var legacyNpc = NewNpc("LegacyNpc", "L000", legacyEnvironment);
    var legacy = host.ResolveNpcScript(legacyNpc);
    Equal(NpcPasScriptResolutionKind.Legacy, legacy.Kind,
        "ordinary NPC was classified dynamic");
    Assert(legacy.ScriptPaths.Count > 0,
        "ordinary NPC lost legacy script discovery");
    Assert(host.TryCallNpcProcedure(legacyNpc,
            new[] { "_Callback", "Callback" }, player, out value)
           && value.AsInt() == 202,
        "ordinary callback behavior changed");
    Assert(host.TryCallNpcInputDialog(legacyNpc, 7, "legacy", true,
            player, out value) && value.AsInt() == 203,
        "ordinary input behavior changed");
    Assert(host.TryCallNpcItemProcedure(legacyNpc, "CommitItem", player,
            item, out value) && value.AsInt() == 204,
        "ordinary item behavior changed");
    Assert(host.TryCallNpcLabel(legacyNpc, "@main", player,
            out value, out found) && found && value.AsInt() == 201,
        "ordinary label behavior changed");
    Equal(205, host.GetLimitValue(legacyNpc, player),
        "ordinary limit behavior changed");
    host.ScheduleCall(legacy.ScriptPaths[0], "DeferredProbe", player,
        legacyNpc, 0);
    Equal(1, host.ProcessDeferredCalls(),
        "ordinary deferred procedure behavior changed");

    RunDeferredExactAbaCheck(root, dynamicRoot, exactPath);
    AssertSourceConnections();
    Console.WriteLine(
        "DynRoomPasContextResolverCheck PASS resolution=3 entries=label+callback+input+item+limit fallback=closed interaction-ABA=expected-handle callback-token=ABA-closed deferred=A/B+actor-exact npc-state=lease+reference runtime-gate=deferred startup=connected");
}
finally
{
    Directory.Delete(root, true);
}

static void AssertDynamicInteractionUnavailable(PasScriptHost host,
    NormNpc npc, TPlayObject player, TUserItem item, string phase)
{
    Assert(!host.TryCallNpcProcedure(npc,
            new[] { "_Callback", "Callback" }, player, out _),
        phase + " callback executed");
    Assert(!host.TryCallNpcInputDialog(npc, 7, phase, true, player,
            out _), phase + " input executed");
    Assert(!host.TryCallNpcItemProcedure(npc, "CommitItem", player,
            item, out _), phase + " item commit executed");
}

static void RunDeferredExactAbaCheck(string eventRoot, string scriptRoot,
    string exactPath)
{
    const string roomName = "DeferredContextRoom";
    var definition = Definition(roomName);
    var environment = new Envirnoment { sMapName = roomName };
    typeof(Envirnoment).GetMethod("Initialize",
        BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(environment, new object[] { (short)6, (short)6 });
    File.WriteAllText(Path.Combine(eventRoot,
            NativeDynamicRoomEventDescriptorLoader.BuildFileName(roomName)),
        "1 5 1,1" + Environment.NewLine);

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
        "deferred runtime room registration failed");
    Assert(runtime.TryReserveIdleRoomLease(roomName, null, out var leaseA),
        "deferred activation A reservation failed");
    var npc = NewNpc("DeferredNpc", roomName, environment);
    Assert(runtime.TryCommitReservedActivation(leaseA, adapter,
            new[]
            {
                new NativeDynamicRoomPasRouteRegistration(npc,
                    Plan(definition, exactPath, true))
            }, out var diagnostics),
        "deferred activation A commit failed: "
        + string.Join(" | ", diagnostics));
    var deferredHost = new PasScriptHost(eventRoot, routes, runtime);
    var player = NewPlayer("DeferredPlayer", environment);

    deferredHost.ScheduleCall(exactPath, "DeferredProbe", player, npc, 0);
    Equal(1, deferredHost.PendingCallCount,
        "activation A deferred call was not queued");
    tick += 120_001;
    runtime.Run();
    tick += 600_001;
    runtime.Run();
    Assert(runtime.TryReserveIdleRoomLease(roomName, null, out var leaseB),
        "deferred activation B reservation failed");
    Assert(runtime.TryCommitReservedActivation(leaseB, adapter,
            new[]
            {
                new NativeDynamicRoomPasRouteRegistration(npc,
                    Plan(definition, exactPath, true))
            }, out diagnostics),
        "deferred activation B commit failed: "
        + string.Join(" | ", diagnostics));
    Equal(0, deferredHost.ProcessDeferredCalls(),
        "queued activation A procedure executed against activation B");
    Equal(0, deferredHost.PendingCallCount,
        "retired activation A call remained queued");

    deferredHost.ScheduleCall(exactPath, "DeferredProbe", player, npc, 0);
    Equal(1, deferredHost.ProcessDeferredCalls(),
        "fresh activation B deferred procedure did not execute");

    deferredHost.ScheduleCall(exactPath, "DeferredProbe", player, npc, 0);
    var playerId = player.ObjectId;
    var replacement = NewPlayer("ForeignDeferredPlayer", environment);
    var replacementOwnId = replacement.ObjectId;
    Assert(M2Share.ObjectManager.Remove(replacementOwnId, replacement),
        "foreign player fixture could not remove its own publication");
    Assert(M2Share.ObjectManager.Remove(playerId, player),
        "foreign player fixture could not remove expected player");
    M2Share.ObjectManager.Add(playerId, replacement);
    Equal(0, deferredHost.ProcessDeferredCalls(),
        "same-ID foreign player executed a deferred procedure");
    Assert(M2Share.ObjectManager.Remove(playerId, replacement),
        "foreign player fixture could not remove replacement");
    M2Share.ObjectManager.Add(playerId, player);

    deferredHost.ScheduleCall(exactPath, "DeferredProbe", player, npc, 0);
    player.m_boGhost = true;
    Equal(0, deferredHost.ProcessDeferredCalls(),
        "ghost player executed a deferred procedure");
    player.m_boGhost = false;
}

static void AssertStateIdentity(PasScriptHost host, NormNpc npc,
    NativeDynamicRoomPasScriptBindingHandle expectedHandle,
    object previousState)
{
    var state = GetNpcState(host, npc);
    Assert(state != null, "NPC interpreter state was not created");
    Assert(ReferenceEquals(npc, state.GetType().GetField("Npc")!.GetValue(state)),
        "NPC state lost exact object identity");
    Assert(ReferenceEquals(expectedHandle, state.GetType()
            .GetField("DynamicBindingHandle")!.GetValue(state)),
        "NPC state lost exact activation handle identity");
    if (previousState != null)
        Assert(!ReferenceEquals(previousState, state),
            "activation B reused activation A interpreter state");
}

static object GetNpcState(PasScriptHost host, NormNpc npc)
{
    var states = typeof(PasScriptHost).GetField("_npcStates",
        BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
    var values = (IEnumerable)states.GetType().GetProperty("Values")!
        .GetValue(states)!;
    foreach (var state in values)
    {
        if (ReferenceEquals(npc,
                state!.GetType().GetField("Npc")!.GetValue(state)))
            return state;
    }
    return null;
}

static NormNpc NewNpc(string name, string mapName, Envirnoment environment)
{
    var npc = new NormNpc
    {
        m_sCharName = name,
        m_sMapName = mapName,
        m_PEnvir = environment
    };
    Assert(ReferenceEquals(npc, M2Share.ObjectManager.Get(npc.ObjectId)),
        "NPC constructor did not publish exact ObjectManager identity");
    return npc;
}

static TPlayObject NewPlayer(string name, Envirnoment environment)
{
    var player = new TPlayObject
    {
        m_sCharName = name,
        m_sMapName = "D000",
        m_PEnvir = environment,
        m_boOffLineFlag = true
    };
    Assert(player.ObjectId > 0
           && ReferenceEquals(M2Share.ObjectManager.Get(player.ObjectId),
               player),
        "player was not published with exact ObjectManager identity");
    return player;
}

static NativeDynamicRoomDefinition Definition(string roomName)
{
    return new NativeDynamicRoomDefinition(roomName, 1, 1,
        "PAS context audit", "D000", "metadata", "metadata",
        Array.Empty<string>(),
        Array.Empty<NativeDynamicRoomConfiguredNpcDefinition>(), 1);
}

static NativeDynamicRoomDynamicNpcScriptBinding Plan(
    NativeDynamicRoomDefinition definition, string scriptPath, bool hasScript)
{
    return new NativeDynamicRoomDynamicNpcScriptBinding(definition,
        NativeDynamicRoomDynamicNpcScriptRole.HiddenController, null,
        Path.GetFileName(scriptPath), scriptPath, hasScript, 0,
        string.Empty);
}

static string WriteScript(string directory, string name, int basis)
{
    var path = Path.Combine(directory, name + ".pas");
    File.WriteAllText(path, $$"""
        program NpcContextProbe;

        function _main: Integer;
        begin
          Result := {{basis + 1}};
        end;

        function _Callback: Integer;
        begin
          Result := {{basis + 2}};
        end;

        function P7: Integer;
        begin
          Result := {{basis + 3}};
        end;

        function CommitItem: Integer;
        begin
          Result := {{basis + 4}};
        end;

        function _GetLimitValue: Integer;
        begin
          Result := {{basis + 5}};
        end;

        procedure DeferredProbe;
        begin
        end;

        begin
        end.
        """);
    return path;
}

static void AssertSourceConnections()
{
    var root = FindRepositoryRoot();
    var host = File.ReadAllText(Path.Combine(root, "GameSvr",
        "ScriptSystem", "PasEngine", "PasScriptHost.cs"));
    var npc = File.ReadAllText(Path.Combine(root, "GameSvr", "Npcs",
        "NormNpc.GotoLable.cs"));
    var player = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.Operate.cs"));
    var bridge = File.ReadAllText(Path.Combine(root, "GameSvr",
        "ScriptSystem", "PasEngine", "PasApiBridge.cs"));
    var hero = File.ReadAllText(Path.Combine(root, "GameSvr", "Services",
        "HeroDataService.cs"));
    foreach (var marker in new[]
             {
                 "ResolveNpcScript(NormNpc npc)",
                 "TryCallNpcLabel(", "TryCallNpcProcedure(",
                 "TryCallNpcInputDialog(", "TryCallNpcItemProcedure(",
                 "TryCaptureNpcInteraction(",
                 "ValidateExpected(npc,", "expectedDynamicBinding",
                 "DynamicBindingHandle", "ReferenceEquals(existing.Npc, npc)",
                 "runtime connection must also hold its reentrant activation gate",
                 "NativeDynamicRoomRuntime dynamicRoomRuntime)",
                 "public TPlayObject Player;", "public NormNpc Npc;",
                 "call.DynamicBindingHandle = dynamicBindingHandle;",
                 "_dynamicRoomRuntime.TryExecuteExpectedPas(",
                 "TryExecuteDynamicDeferredProcedure(call,",
                 "ReferenceEquals(objectManager.Get(call.PlayerId),",
                 "ReferenceEquals(objectManager.Get(call.NpcId),"
             })
        Assert(host.Contains(marker, StringComparison.Ordinal),
            "host source contract missing: " + marker);
    Assert(host.Contains(": this(envirPath, null, null)",
               StringComparison.Ordinal)
           && host.Contains(": this(envirPath, dynamicNpcRoutes, null)",
               StringComparison.Ordinal),
        "legacy PasScriptHost constructors no longer fail closed through the runtime overload");

    var schedule = ExtractSourceBlock(host, "private void ScheduleCallCore(");
    var scheduleResolution = schedule.IndexOf("TryResolveDeferredRoute(",
        StringComparison.Ordinal);
    var scheduleLock = schedule.IndexOf("lock (_deferredLock)",
        StringComparison.Ordinal);
    Assert(scheduleResolution >= 0 && scheduleLock > scheduleResolution,
        "deferred route resolution moved under the deferred queue lock");

    var process = ExtractSourceBlock(host, "public int ProcessDeferredCalls()");
    var queueLock = ExtractSourceBlock(process, "lock (_deferredLock)");
    foreach (var forbidden in new[]
             {
                 "ObjectManager", "ResolveNpcScript(",
                 "TryExecuteExpectedPas(", "CallProcedure(",
                 "TryExecuteDynamicDeferredProcedure(", "GetOrLoadProgram("
             })
        Assert(!queueLock.Contains(forbidden, StringComparison.Ordinal),
            "deferred queue lock performs external work: " + forbidden);

    var dynamicExecution = ExtractSourceBlock(host,
        "private bool TryExecuteDynamicDeferredProcedure(");
    Assert(dynamicExecution.Contains("TryInvokeWithInterpreter(",
               StringComparison.Ordinal)
           && !dynamicExecution.Contains("ResolveCurrent(",
               StringComparison.Ordinal)
           && !dynamicExecution.Contains("ResolveNpcScript(",
               StringComparison.Ordinal),
        "dynamic deferred execution does not stay on its saved expected handle");
    Assert(!npc.Contains("FindScriptFile(", StringComparison.Ordinal)
           && npc.Contains("TryCallNpcLabel(", StringComparison.Ordinal)
           && npc.Contains("TryCallNpcProcedure(", StringComparison.Ordinal),
        "NormNpc retained a basename bypass");
    Assert(player.Contains("TryCallNpcInputDialog(",
            StringComparison.Ordinal),
        "input response did not use the central NPC context route");
    Assert(bridge.Contains("ScriptHost.ResolveNpcScript(npc)",
            StringComparison.Ordinal)
           && bridge.Contains("if (scriptPath != null)",
               StringComparison.Ordinal),
        "PasApiBridge deferred routes retained their basename resolver");
    Assert(hero.Contains("CallbackPasInteraction",
            StringComparison.Ordinal)
           && hero.Contains("TryCaptureNpcInteraction",
               StringComparison.Ordinal),
        "asynchronous NPC callback did not retain its expected interaction");
}

static string ExtractSourceBlock(string source, string anchor)
{
    var anchorIndex = source.IndexOf(anchor, StringComparison.Ordinal);
    Assert(anchorIndex >= 0, "source block anchor missing: " + anchor);
    var openBrace = source.IndexOf('{', anchorIndex);
    Assert(openBrace >= 0, "source block opening brace missing: " + anchor);

    var depth = 0;
    for (var index = openBrace; index < source.Length; index++)
    {
        if (source[index] == '{') depth++;
        else if (source[index] == '}' && --depth == 0)
            return source.Substring(openBrace, index - openBrace + 1);
    }
    throw new InvalidOperationException(
        "source block closing brace missing: " + anchor);
}

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory != null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "GameSvr",
                "GameSvr.csproj")))
            return directory.FullName;
        directory = directory.Parent;
    }
    throw new InvalidOperationException("repository root was not found");
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
