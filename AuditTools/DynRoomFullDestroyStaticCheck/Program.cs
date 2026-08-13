var repoRoot = FindRepoRoot();
var stagingRoot = FindStagingRoot(repoRoot);

var lifecyclePath = Path.Combine(stagingRoot, "pas-finish", "ida-dynroom-lifecycle.txt");
var managerPath = Path.Combine(repoRoot, "GameSvr", "Maps", "NativeDynamicRoomManager.cs");
var leaseOwnerPath = Path.Combine(repoRoot, "GameSvr", "Maps", "NativeDynamicRoomLeaseOwner.cs");
var environmentPath = Path.Combine(repoRoot, "GameSvr", "Maps", "Envirnoment.cs");
var pasHostPath = Path.Combine(repoRoot, "GameSvr", "ScriptSystem", "PasEngine", "PasScriptHost.cs");
var pasBridgePath = Path.Combine(repoRoot, "GameSvr", "ScriptSystem", "PasEngine", "PasApiBridge.cs");
var objectManagerPath = Path.Combine(repoRoot, "GameSvr", "UsrSystem", "ObjectManager.cs");
var baseObjectPath = Path.Combine(repoRoot, "GameSvr", "Actors", "TBaseObject.Base.cs");
var localDbPath = Path.Combine(repoRoot, "GameSvr", "LocalDB.cs");
var memoryImagePath = FindNativeImage(stagingRoot);
var memoryImage = File.ReadAllBytes(memoryImagePath);

var lifecycle = Read(lifecyclePath);
var manager = Read(managerPath);
var leaseOwner = Read(leaseOwnerPath);
var environment = Read(environmentPath);
var pasHost = Read(pasHostPath);
var pasBridge = Read(pasBridgePath);
var objectManager = Read(objectManagerPath);
var baseObject = Read(baseObjectPath);
var localDb = Read(localDbPath);

var fullDestroy = Slice(lifecycle, "FUNCTION 0x005FDD08-0x005FDEF1 sub_5FDD08", "XREFS TO 0x005FDD08");
AssertOrdered(fullDestroy,
    "cmp     byte ptr [eax+0F1h], 0",
    "cmp     byte ptr [eax+0F9h], 0",
    "call    sub_71897C",
    "mov     byte ptr [eax+0F9h], 0",
    "call    sub_774C88",
    "call    sub_779DF4",
    "call    sub_5FE170",
    "mov     ebx, [eax+0A4h]",
    "call    dword ptr [edx+7Ch]",
    "call    sub_768060",
    "call    sub_5FE1C4",
    "mov     [eax+0A4h], edx",
    "mov     eax, [eax+0E0h]",
    "call    dword ptr [edx+7Ch]",
    "call    sub_768060",
    "call    sub_5FE1C4",
    "call    dword ptr [edx+8]",
    "mov     byte ptr [eax+91h], 0",
    "mov     byte ptr [eax+0F1h], 1",
    "call    sub_5FE1B4");

var stateTransition = Slice(lifecycle, "FUNCTION 0x005FE08C-0x005FE126 sub_5FE08C", "XREFS TO 0x005FE08C");
AssertContains(stateTransition, "call    sub_779DF4",
    "state-1 transition must clear exact environment actors");
AssertContains(stateTransition, "call    sub_71897C",
    "state-0 transition must unregister dynamic script/context hook");
AssertContains(stateTransition, "call    sub_5FE940",
    "state-2 transition must register dynamic script/context hook");

var lifecycleRun = Slice(lifecycle,
    "FUNCTION 0x005FD3E0-0x005FD447 sub_5FD3E0",
    "XREF FUNCTION 0x005FD448");
AssertContains(lifecycleRun, "cmp     eax, 927C0h",
    "closing-to-idle transition must retain the native ten-minute delay");
AssertContains(lifecycleRun, "cmp     esi, 1D4C0h",
    "empty-active transition must retain the native two-minute floor");

var idleRetirement = Slice(lifecycle,
    "FUNCTION 0x005FD764-0x005FD78F sub_5FD764",
    "FIELD USER 0x005FDC10");
AssertOrdered(idleRetirement,
    "cmp     byte ptr [ebx+0F0h], 0",
    "sub     eax, [ebx+0E8h]",
    "cmp     eax, 36EE80h");

var definitionRetirement = Slice(lifecycle,
    "FUNCTION 0x005FE1D4-0x005FE22C sub_5FE1D4",
    "XREF FUNCTION 0x005FE5CC");
AssertOrdered(definitionRetirement,
    "call    sub_5FD764",
    "call    sub_5FDD08",
    "call    sub_424B30");

// These wrappers and queues were absent from the text export. Lock their exact
// bytes from the already captured, offline image; the test never starts M2.
AssertBytesAtVa(memoryImage, 0x005FE1B4,
    "558BEC8B4030E889ECFFFF5DC3");
AssertBytesAtVa(memoryImage, 0x005FE1C4,
    "558BEC8B4030E86DEBFFFF5DC3");
AssertBytesAtVa(memoryImage, 0x005FCD3C,
    "558BEC5356578BFA8BF085FF742FB80C000000E84C62E0FF8BD8893BE8E3B5E0FF89430433C0894308837E2C007505895E2CEB068B4630895808895E305F5E5B5DC3");
AssertBytesAtVa(memoryImage, 0x005FCE48,
    "558BEC5356578BFA8BF085FF742FB80C000000E84061E0FF8BD8893BE8D7B4E0FF89430433C0894308837E34007505895E34EB068B4638895808895E385F5E5B5DC3");
AssertBytesAtVa(memoryImage, 0x005FCE0A,
    "3DE0930400");
AssertBytesAtVa(memoryImage, 0x005FCEA6,
    "3D407E0500");

AssertContains(environment,
    "DynamicRoomManagerOwner?.NotifyPlayerRemoved(this);",
    "C# player removal must continue to notify the dynamic room manager");
AssertContains(manager, "BeginClosingCleanup { get; init; }",
    "C# manager must keep state-1 cleanup behind an explicit delegate");
AssertContains(manager, "FinalizeIdleCleanup { get; init; }",
    "C# manager must keep final idle cleanup behind an explicit delegate");
AssertOrdered(manager,
    "BeginClosingLocked(registration, now);",
    "PrepareForReuse(closingRoom, closingLease);");
AssertContains(manager,
    "registration.Environment.DynamicRoomBlocked = !prepared;",
    "rooms without successful cleanup must remain blocked");
AssertNotContains(manager, "ClearNpcState(",
    "manager currently must not pretend to own room-level PasEngine cleanup");
AssertNotContains(manager, "RemoveObject(",
    "manager currently must not pretend to perform actor full clear");
AssertContains(manager,
    "private const long IdleRetirementMilliseconds = 60 * 60 * 1000;",
    "manager must retain the native one-hour state-0 retirement delay");
AssertContains(manager, "TryAttachPhysicalOwnership(",
    "manager must require explicit exact physical ownership before retirement");
AssertContains(manager, "RetirePhysicalRoom(work.Registration, work.Permit);",
    "manager run loop must dispatch the complete physical retirement transaction");
AssertContains(manager, "registration.Environment.DynamicRoomBlocked = true;",
    "physical retirement failure must remain isolated from reuse");
AssertContains(manager, "registration.PhysicalOwnership.IsDestroyed",
    "manager must verify physical destruction instead of trusting the hook result");
AssertContains(leaseOwner, "TryBeginPhysicalRetirement(",
    "lease owner must atomically exclude a retiring physical environment");
AssertContains(leaseOwner, "TryCompletePhysicalRetirement(",
    "lease owner must remove the exact retired environment and final definition");

AssertContains(pasHost, "public void ClearNpcState(int npcId)",
    "PasEngine exposes only per-NPC state clearing today");
AssertContains(pasHost, "public int CancelDeferredCallsForObject(int objectId)",
    "PasEngine must expose the existing per-object deferred-call cancellation primitive");
AssertContains(objectManager,
    "internal TBaseObject[] SnapshotEnvironmentObjects(Envirnoment environment)",
    "ObjectManager must retain exact-environment object enumeration");
AssertContains(objectManager, "M2Share.PasEngine?.ClearMonsterScriptState(actorId);",
    "ObjectManager removal must retain monster-script state cleanup");
AssertContains(baseObject, "public virtual void MakeGhost()",
    "actor ghost lifecycle primitive is required by room teardown");
AssertContains(pasBridge, "private static void ClearEnvironmentMonsters(",
    "existing monster-only cleanup must remain visible as a non-equivalent primitive");
AssertNotContains(manager, "ClearEnvironmentMonsters(",
    "dynamic manager must not use the filtered monster-only helper as full teardown");
AssertContains(localDb, "M2Share.PasEngine?.LoadMapQuestMap();",
    "LocalDB map quest path is global load/reload, not room destroy");

var observed = new List<string>();
var model = new FullDestroyModel(observed);
model.Destroy();
model.Destroy();
AssertEqual("UnregisterScriptContext,ClearCellPayloads,ClearOwnedActors,PostActorCleanup," +
    "DestroyController,QueueControllerRetirement,DestroyDynamicNpcs," +
    "QueueDynamicNpcRetirement,ClearDynamicNpcList,MarkInactive,MarkDeleted," +
    "QueueEnvironmentRetirement,RemoveDefinitionInstanceSlot",
    string.Join(",", observed),
    "pure model destroy order or idempotence changed");

Console.WriteLine("DynRoomFullDestroyStaticCheck PASS");

static string FindRepoRoot()
{
    var current = AppContext.BaseDirectory;
    while (!string.IsNullOrEmpty(current))
    {
        if (Directory.Exists(Path.Combine(current, "GameSvr")) &&
            Directory.Exists(Path.Combine(current, "AuditTools")))
            return current;

        current = Directory.GetParent(current)?.FullName;
    }

    throw new InvalidOperationException("repo root not found");
}

// staging/ is a sibling of the repository, but the repository is routinely checked out
// through `git worktree` several levels deeper, so the parent of the repo root is not a fixed
// anchor: computing repoRoot/../staging threw FileNotFoundException before a single
// disassembly marker was compared.
static string FindStagingRoot(string repoRoot)
{
    var probed = new List<string>();
    for (var directory = new DirectoryInfo(repoRoot);
         directory != null; directory = directory.Parent)
    {
        var candidate = Path.Combine(directory.FullName, "staging");
        if (File.Exists(Path.Combine(candidate, "pas-finish", "ida-dynroom-lifecycle.txt")))
            return candidate;
        probed.Add(candidate);
    }
    throw new InvalidOperationException(
        "staging/pas-finish/ida-dynroom-lifecycle.txt not found; probed: "
        + string.Join("; ", probed));
}

// The questinfo runtime dump this used to pin is gone from staging. The six
// VAs it locked (0x5FE1B4 / 0x5FE1C4 / 0x5FCD3C / 0x5FCE48 / 0x5FCE0A /
// 0x5FCEA6) match the canonical unpack image byte-for-byte, so that is the
// authority. Prefer it, fall back to the dump if a checkout still has it.
static string FindNativeImage(string stagingRoot)
{
    var probed = new List<string>();
    foreach (var relative in new[]
             {
                 Path.Combine("_reunpack_work", "flat_image.bin"),
                 Path.Combine("questinfo_runtime_dump", "M2Server_exe.memory.bin")
             })
    {
        var candidate = Path.Combine(stagingRoot, relative);
        if (File.Exists(candidate)) return candidate;
        probed.Add(candidate);
    }
    throw new InvalidOperationException(
        "native M2 image not found; probed: " + string.Join("; ", probed));
}

static string Read(string path)
{
    if (!File.Exists(path)) throw new FileNotFoundException(path);
    return File.ReadAllText(path);
}

static void AssertBytesAtVa(byte[] image, int virtualAddress, string expectedHex)
{
    const int imageBase = 0x00400000;
    var expected = Convert.FromHexString(expectedHex);
    var offset = virtualAddress - imageBase;
    if (offset < 0 || offset + expected.Length > image.Length)
        throw new InvalidOperationException($"image range unavailable at 0x{virtualAddress:X8}");
    if (!image.AsSpan(offset, expected.Length).SequenceEqual(expected))
        throw new InvalidOperationException($"offline image bytes changed at 0x{virtualAddress:X8}");
}

static string Slice(string text, string start, string end)
{
    var startIndex = text.IndexOf(start, StringComparison.Ordinal);
    if (startIndex < 0) throw new InvalidOperationException($"slice start not found: {start}");
    var endIndex = text.IndexOf(end, startIndex, StringComparison.Ordinal);
    if (endIndex < 0) endIndex = text.Length;
    return text.Substring(startIndex, endIndex - startIndex);
}

static void AssertContains(string text, string needle, string message)
{
    if (!text.Contains(needle, StringComparison.Ordinal))
        throw new InvalidOperationException(message);
}

static void AssertNotContains(string text, string needle, string message)
{
    if (text.Contains(needle, StringComparison.Ordinal))
        throw new InvalidOperationException(message);
}

static void AssertOrdered(string text, params string[] needles)
{
    var cursor = 0;
    foreach (var needle in needles)
    {
        var index = text.IndexOf(needle, cursor, StringComparison.Ordinal);
        if (index < 0)
            throw new InvalidOperationException($"ordered marker not found after {cursor}: {needle}");
        cursor = index + needle.Length;
    }
}

static void AssertEqual(string expected, string actual, string message)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
        throw new InvalidOperationException($"{message}: expected '{expected}', actual '{actual}'");
}

internal sealed class FullDestroyModel
{
    private readonly List<string> _events;
    private bool _destroyed;

    public FullDestroyModel(List<string> events)
    {
        _events = events;
    }

    public void Destroy()
    {
        if (_destroyed) return;

        _events.Add("UnregisterScriptContext");
        _events.Add("ClearCellPayloads");
        _events.Add("ClearOwnedActors");
        _events.Add("PostActorCleanup");
        _events.Add("DestroyController");
        _events.Add("QueueControllerRetirement");
        _events.Add("DestroyDynamicNpcs");
        _events.Add("QueueDynamicNpcRetirement");
        _events.Add("ClearDynamicNpcList");
        _events.Add("MarkInactive");
        _events.Add("MarkDeleted");
        _events.Add("QueueEnvironmentRetirement");
        _events.Add("RemoveDefinitionInstanceSlot");
        _destroyed = true;
    }
}
