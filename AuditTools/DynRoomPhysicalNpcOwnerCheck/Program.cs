using GameSvr;
using System.Reflection;

PrepareRuntimeConfig();
M2Share.ObjectManager = new ObjectManager();

var root = Path.Combine(Path.GetTempPath(), "dynroom-physical-npc-"
    + Guid.NewGuid().ToString("N"));
var scripts = Path.Combine(root, "DynRoomScripts");
Directory.CreateDirectory(scripts);

try
{
    RunPhysicalAndActivationLifetimeCheck(scripts);
}
finally
{
    Directory.Delete(root, true);
}

Console.WriteLine("DynRoomPhysicalNpcOwnerCheck PASS "
    + "physical=environment+instance activation=exact-lease "
    + "state1=routes-only retirement=manager-permit "
    + "retry=idempotent ABA=closed journal=model startup=connected");

static void RunPhysicalAndActivationLifetimeCheck(string scripts)
{
    const string roomName = "OwnerRoom";
    var configuredDefinition = new NativeDynamicRoomConfiguredNpcDefinition(
        "Guide", 2, 3, "Room Guide", 4, 5, 10);
    var definition = new NativeDynamicRoomDefinition(roomName, "opaque", 1,
        roomName, "D000", "1", "1", Array.Empty<string>(),
        new[] { configuredDefinition }, 1);
    var controllerPath = WriteScript(scripts, "DNpc_" + roomName + ".pas");
    var configuredPath = WriteScript(scripts,
        configuredDefinition.ScriptName + "-" + roomName + ".pas");
    var controllerBinding = Binding(definition,
        NativeDynamicRoomDynamicNpcScriptRole.HiddenController, null,
        controllerPath);
    var configuredBinding = Binding(definition,
        NativeDynamicRoomDynamicNpcScriptRole.ConfiguredVisible,
        configuredDefinition, configuredPath);
    var planned = new[] { controllerBinding, configuredBinding };

    long tick = 1_000;
    var routes = new NativeDynamicRoomPasScriptRouteTable(scripts);
    var owner = new NativeDynamicRoomNpcOwner(routes);
    var manager = new NativeDynamicRoomManager(() => tick);
    var environment = new Envirnoment { sMapName = roomName };
    Assert(manager.RegisterIdleRoom(definition, 7, environment, 0,
            owner.TryRetireActivationBinding, _ => true, null),
        "physical room registration failed");

    var controllerA = Npc(environment, roomName);
    var configuredA = Npc(environment, configuredDefinition.NpcName);
    var journalA = new TestJournal(definition, environment, 7,
        new[]
        {
            new NativeDynamicRoomMaterializedNpc(controllerA,
                controllerBinding),
            new NativeDynamicRoomMaterializedNpc(configuredA,
                configuredBinding)
        }, committed: false);

    Assert(!owner.TryAdoptCommittedPublication(definition, environment, 7,
            planned, journalA, out _),
        "uncommitted physical publication was adopted");
    Assert(journalA.TryCommit(), "test publication did not commit");
    Assert(!owner.TryAdoptCommittedPublication(definition, environment, 8,
            planned, journalA, out _),
        "wrong physical instance ID was adopted");
    var competingOwner = new NativeDynamicRoomNpcOwner(routes);
    journalA.FailOwnershipValidationOnce = true;
    Assert(!competingOwner.TryAdoptCommittedPublication(definition,
            environment, 7, planned, journalA, out _),
        "faulted adoption unexpectedly retained the journal");
    Assert(owner.TryAdoptCommittedPublication(definition, environment, 7,
            planned, journalA, out var physicalA),
        "committed exact publication was not adopted");
    Assert(ReferenceEquals(physicalA.Controller, controllerA)
           && physicalA.ConfiguredNpcs.Count == 1
           && ReferenceEquals(physicalA.ConfiguredNpcs[0], configuredA)
           && owner.ContainsPhysicalNpcExact(physicalA, controllerA)
           && owner.ContainsPhysicalNpcExact(physicalA, configuredA),
        "physical controller/configured ownership was not exact");
    Assert(!competingOwner.TryAdoptCommittedPublication(definition,
            environment, 7, planned, journalA, out _),
        "same journal was transferred to two physical owners");
    Assert(!owner.TryAdoptCommittedPublication(definition, environment, 7,
            planned, journalA, out _),
        "same physical environment was adopted twice");
    var fullDestroyAttempts = 0;
    var stableRetirementPermit = true;
    INativeDynamicRoomPhysicalRetirementPermit firstRetirementPermit = null;
    Assert(manager.TryAttachPhysicalOwnership(environment, definition, 7,
            physicalA, permit =>
            {
                fullDestroyAttempts++;
                if (firstRetirementPermit == null)
                    firstRetirementPermit = permit;
                else
                    stableRetirementPermit &= ReferenceEquals(
                        firstRetirementPermit, permit);
                var destroyed = owner.TryFullDestroy(physicalA, permit);
                return destroyed && fullDestroyAttempts != 2;
            }),
        "physical ownership was not attached to manager retirement");

    Assert(manager.TryReserveIdleRoomLease(roomName, null, out var leaseA),
        "activation lease A was not reserved");
    var registrationsA = Registrations(
        (controllerA, controllerBinding),
        (configuredA, configuredBinding));
    var spoofedControllerBinding = Binding(definition,
        NativeDynamicRoomDynamicNpcScriptRole.HiddenController, null,
        controllerPath);
    var spoofedHandles = new[]
    {
        routes.Register(controllerA, leaseA, spoofedControllerBinding),
        routes.Register(configuredA, leaseA, configuredBinding)
    };
    Assert(!owner.TryAttachActivationBinding(physicalA, leaseA,
            registrationsA, spoofedHandles, out _),
        "route handle accepted a different exact planner binding");
    var handlesA = RegisterRoutes(routes, leaseA, registrationsA);
    Assert(!owner.TryAttachActivationBinding(physicalA, leaseA,
            registrationsA, new[] { handlesA[0] }, out _),
        "partial physical NPC route set was accepted");
    Assert(owner.TryAttachActivationBinding(physicalA, leaseA,
            registrationsA, handlesA, out var activationA),
        "activation A route ownership was not attached");
    Assert(owner.TryAttachActivationBinding(physicalA, leaseA,
            registrationsA, handlesA, out var activationARetry)
           && ReferenceEquals(activationA, activationARetry),
        "exact activation attach retry was not idempotent");
    Assert(owner.TryAttachActivationBinding(physicalA, leaseA,
            registrationsA.Reverse().ToArray(),
            handlesA.Reverse().ToArray(), out var reorderedRetry)
           && ReferenceEquals(activationA, reorderedRetry),
        "same exact route set was not order-independent and idempotent");
    Assert(!owner.TryRetireActivationBinding(leaseA)
           && routes.ValidateExpected(controllerA, handlesA[0], out _),
        "state-2 activation routes were retired before closing");

    tick += 120_001;
    manager.Run();
    Equal(1, DynamicState(environment),
        "manager did not enter state 1");
    Assert(activationA.IsRetired && journalA.DestroyCalls == 0,
        "state 1 did not retire routes independently of physical NPCs");
    Equal(0, fullDestroyAttempts,
        "physical destroy ran during activation A cleanup");
    AssertExactPublished(controllerA, "controller A was destroyed at state 1");
    AssertExactPublished(configuredA,
        "configured NPC A was destroyed at state 1");

    tick += 600_001;
    manager.Run();
    Equal(0, DynamicState(environment),
        "manager did not return physical room to state 0");
    Assert(manager.TryReserveIdleRoomLease(roomName, null, out var leaseB),
        "activation lease B was not reserved");
    var registrationsB = Registrations(
        (controllerA, controllerBinding),
        (configuredA, configuredBinding));
    var handlesB = RegisterRoutes(routes, leaseB, registrationsB);
    Assert(owner.TryAttachActivationBinding(physicalA, leaseB,
            registrationsB, handlesB, out var activationB),
        "activation B route ownership was not attached");
    Assert(!owner.TryRetireActivationBinding(leaseA),
        "stale activation A retired activation B");
    Assert(routes.ValidateExpected(controllerA, handlesB[0], out _)
           && routes.ValidateExpected(configuredA, handlesB[1], out _)
           && !activationB.IsRetired,
        "stale activation A disturbed activation B routes");
    Equal(0, fullDestroyAttempts,
        "physical destroy ran while activation B was active");

    tick += 120_001;
    manager.Run();
    Equal(1, DynamicState(environment),
        "activation B did not enter state 1");
    Assert(activationB.IsRetired && fullDestroyAttempts == 0,
        "state 1 destroyed physical NPCs instead of only bindings");
    AssertExactPublished(controllerA,
        "controller A disappeared before full destroy");
    AssertExactPublished(configuredA,
        "configured NPC A disappeared before full destroy");

    tick += 600_001;
    manager.Run();
    Equal(0, DynamicState(environment),
        "activation B did not finish closing");
    Assert(!owner.TryFullDestroy(physicalA, null),
        "physical room was destroyed without an exact retirement permit");
    journalA.ThrowAfterDestroyOnce = true;
    journalA.ThrowDestroyedReadsRemaining = 1;
    tick += 3_600_001;
    manager.Run();
    Assert(fullDestroyAttempts == 1 && physicalA.DestroyPending
           && !physicalA.IsDestroyed && journalA.DestroyCalls == 1
           && firstRetirementPermit?.IsRetiredExact == true,
        "faulted manager retirement did not remain exact and retryable");
    Assert(M2Share.ObjectManager.Get(controllerA.ObjectId) == null
           && M2Share.ObjectManager.Get(configuredA.ObjectId) == null,
        "destroy-then-throw journal did not complete physical removal");
    Assert(!manager.TryReserveIdleRoomLease(roomName, null, out _),
        "retiring physical room accepted a new activation");

    manager.Run();
    Assert(fullDestroyAttempts == 2 && physicalA.IsDestroyed
           && journalA.DestroyCalls == 1
           && firstRetirementPermit.IsRetiredExact
           && M2Share.ObjectManager.Get(controllerA.ObjectId) == null
           && M2Share.ObjectManager.Get(configuredA.ObjectId) == null
           && !owner.TryGetPhysicalOwnership(environment, 7, out _),
        "owner destroy succeeded without retaining manager retry identity");

    manager.Run();
    Assert(fullDestroyAttempts == 3 && stableRetirementPermit
           && !firstRetirementPermit.IsRetiredExact
           && !manager.TryReserveIdleRoomLease(roomName, null, out _)
           && !owner.TryFullDestroy(physicalA, firstRetirementPermit),
        "idempotent manager retirement did not invalidate the exact permit");
}

static NativeDynamicRoomPasScriptBindingHandle[] RegisterRoutes(
    NativeDynamicRoomPasScriptRouteTable routes,
    NativeDynamicRoomActivationLease lease,
    IReadOnlyList<NativeDynamicRoomPasRouteRegistration> registrations)
{
    var handles = new NativeDynamicRoomPasScriptBindingHandle[
        registrations.Count];
    for (var index = 0; index < registrations.Count; index++)
    {
        handles[index] = routes.Register(registrations[index].Npc, lease,
            registrations[index].Binding);
        Assert(routes.ValidateExpected(registrations[index].Npc,
                handles[index], out _),
            "test route was not exact-current");
    }
    return handles;
}

static NativeDynamicRoomPasRouteRegistration[] Registrations(
    params (NormNpc Npc,
        NativeDynamicRoomDynamicNpcScriptBinding Binding)[] registrations)
{
    return registrations.Select(registration =>
            new NativeDynamicRoomPasRouteRegistration(registration.Npc,
                registration.Binding))
        .ToArray();
}

static NativeDynamicRoomDynamicNpcScriptBinding Binding(
    NativeDynamicRoomDefinition definition,
    NativeDynamicRoomDynamicNpcScriptRole role,
    NativeDynamicRoomConfiguredNpcDefinition configuredNpc,
    string path)
{
    return new NativeDynamicRoomDynamicNpcScriptBinding(definition, role,
        configuredNpc, Path.GetFileName(path), Path.GetFullPath(path), true,
        1, "program Mir2;");
}

static NormNpc Npc(Envirnoment environment, string name)
{
    var npc = new NormNpc
    {
        m_PEnvir = environment,
        m_sMapName = environment.sMapName,
        m_sCharName = name
    };
    AssertExactPublished(npc, "test NPC was not published exactly");
    return npc;
}

static string WriteScript(string directory, string fileName)
{
    var path = Path.Combine(directory, fileName);
    File.WriteAllText(path, "program Mir2; begin end.");
    return path;
}

static void AssertExactPublished(NormNpc npc, string message)
{
    Assert(npc != null && npc.ObjectId > 0
           && ReferenceEquals(M2Share.ObjectManager.Get(npc.ObjectId), npc)
           && !npc.m_boGhost, message);
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

internal sealed class TestJournal
    : INativeDynamicRoomNpcMaterializationJournal
{
    private object _ownershipClaim;
    private bool _isDestroyed;

    public TestJournal(NativeDynamicRoomDefinition definition,
        Envirnoment environment, int physicalInstanceId,
        IReadOnlyList<NativeDynamicRoomMaterializedNpc> npcs,
        bool committed)
    {
        Definition = definition;
        Environment = environment;
        PhysicalInstanceId = physicalInstanceId;
        Npcs = npcs;
        IsCommitted = committed;
    }

    public NativeDynamicRoomDefinition Definition { get; }
    public Envirnoment Environment { get; }
    public int PhysicalInstanceId { get; }
    public IReadOnlyList<NativeDynamicRoomMaterializedNpc> Npcs { get; }
    public bool IsCommitted { get; private set; }
    public bool IsDestroyed
    {
        get
        {
            if (_isDestroyed && ThrowDestroyedReadsRemaining > 0)
            {
                ThrowDestroyedReadsRemaining--;
                throw new InvalidOperationException(
                    "injected destroyed-state read fault");
            }
            return _isDestroyed;
        }
    }
    public bool HasUnresolvedPublication { get; private set; }
    public int DestroyCalls { get; private set; }
    public bool ThrowAfterDestroyOnce { get; set; }
    public int ThrowDestroyedReadsRemaining { get; set; }
    public bool FailOwnershipValidationOnce { get; set; }

    public bool TryClaimOwnership(object ownerCapability)
    {
        return ownerCapability != null
               && Interlocked.CompareExchange(ref _ownershipClaim,
                   ownerCapability, null) == null;
    }

    public bool IsOwnershipClaimedBy(object ownerCapability)
    {
        if (FailOwnershipValidationOnce)
        {
            FailOwnershipValidationOnce = false;
            return false;
        }
        return ownerCapability != null
               && ReferenceEquals(Volatile.Read(ref _ownershipClaim),
                   ownerCapability);
    }

    public bool TryReleaseOwnershipClaim(object ownerCapability)
    {
        return ownerCapability != null
               && ReferenceEquals(Interlocked.CompareExchange(
                   ref _ownershipClaim, null, ownerCapability),
                   ownerCapability);
    }

    public bool TryCommit()
    {
        if (IsCommitted || IsDestroyed || HasUnresolvedPublication)
            return false;
        IsCommitted = true;
        return true;
    }

    public bool TryRollback()
    {
        if (IsCommitted || IsDestroyed) return false;
        _isDestroyed = true;
        return true;
    }

    public bool TryDestroyExact()
    {
        DestroyCalls++;
        if (!IsCommitted || IsDestroyed) return false;
        HasUnresolvedPublication = false;
        foreach (var materialized in Npcs)
        {
            if (!M2Share.ObjectManager.Remove(materialized.Npc.ObjectId,
                    materialized.Npc))
            {
                HasUnresolvedPublication = true;
                return false;
            }
            materialized.Npc.m_boGhost = true;
        }
        _isDestroyed = true;
        if (ThrowAfterDestroyOnce)
        {
            ThrowAfterDestroyOnce = false;
            throw new InvalidOperationException(
                "injected destroy-after-commit fault");
        }
        return true;
    }
}
