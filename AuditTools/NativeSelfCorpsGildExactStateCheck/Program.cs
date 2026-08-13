using System.Text;
using GameSvr.Services;

var tests = new (string Name, Action Run)[]
{
    ("Corps return gates and byte validation", CorpsReturnGates),
    ("Corps success publication and messages", CorpsSuccessOrder),
    ("Corps online lookup race", CorpsOnlineLookupRace),
    ("Gild role and dynamic gates", GildPreGates),
    ("Gild duplicate refresh", GildDuplicateOrder),
    ("Gild success publication and messages", GildSuccessOrder),
    ("Gild has no Corps name validation", GildDoesNotValidateName),
    ("independent FIFO partial failures", IndependentFifoFailures),
    ("exact state machine dormant; PAS create live-gated", DormantProductionBoundary),
    ("native evidence anchors", NativeEvidenceAnchors)
};

foreach (var test in tests) test.Run();
Console.WriteLine(
    $"NativeSelfCorpsGildExactStateCheck PASS tests={tests.Length} " +
    "corps-writes=Corps>CorpsMember " +
    "gild-writes=GildMember>Gild fifo=independent partial=true " +
    "exact-state-machine=dormant/unwired pas-create=live-gated " +
    "missing=legacy-store-enqueue-api");
return;

static void CorpsReturnGates()
{
    var actor = Actor();

    var pointer = new FakeHost { HasCorpsPointer = true };
    Equal(3, CreateCorps(pointer, actor, Array.Empty<byte>()),
        "existing pointer must win before name validation");
    Sequence(new[] { "status:4524:3", "refresh" }, pointer.Events,
        "pointer events");

    foreach (var value in new byte[]
             {
                 0, 32, 33, 47, 58, 60, 62, 63, 92, 124
             })
    {
        var host = new FakeHost();
        Equal(1, CreateCorps(host, actor, new[] { value }),
            "invalid Corps byte " + value);
        Sequence(new[] { "status:4524:1", "refresh" }, host.Events,
            "invalid events " + value);
    }

    var gbkTrail = new FakeHost();
    Equal(1, CreateCorps(gbkTrail, actor, new byte[] { 0x81, 0x5C }),
        "validation must inspect raw GBK trail bytes");

    var duplicate = new FakeHost { CorpsDuplicate = true };
    Equal(2, CreateCorps(duplicate, actor, Bytes("aBc")),
        "Corps duplicate result");
    Sequence(Bytes("ABC"), duplicate.LastCorpsLookup,
        "ASCII-only normalized Corps lookup");
    Sequence(new[] { "status:4524:2", "refresh" }, duplicate.Events,
        "duplicate events");

    var indexed = new FakeHost { MemberIndexed = true };
    Equal(3, CreateCorps(indexed, actor, Bytes("INDEXED")),
        "member index result");
    Sequence(new[] { "status:4524:3", "refresh" }, indexed.Events,
        "indexed events");
}

static void CorpsSuccessOrder()
{
    var host = new FakeHost();
    var queue = new RecordingQueue(host.Events);
    var result = NativeSelfCorpsGildExactStateMachine.CreateSelfCorps(
        host, queue, Actor(), Bytes("Alpha"));

    Equal(0, result, "Corps success result");
    Sequence(new[]
    {
        "allocate:corps", "publish:corps", "enqueue:InsertCorps",
        "assign:owner-corps", "add:owner-member",
        "enqueue:InsertCorpsMember", "publish:member-index", "bind:corps",
        "send:player-corps", "broadcast:corps", "status:4524:0", "refresh"
    }, host.Events, "Corps success order");
    Sequence(new[]
    {
        NativeSelfSocialWriteKind.InsertCorps,
        NativeSelfSocialWriteKind.InsertCorpsMember
    }, queue.Commands.Select(command => command.Kind).ToArray(),
        "Corps enqueue order");
}

static void CorpsOnlineLookupRace()
{
    var host = new FakeHost { BindOnlineCorps = false };
    Equal(0, CreateCorps(host, Actor(), Bytes("Race")),
        "accepted Corps must stay successful after online lookup race");
    Require(!host.Events.Contains("send:player-corps"),
        "online lookup race sent player Corps");
    Require(!host.Events.Contains("broadcast:corps"),
        "online lookup race broadcast Corps");
    Require(host.Events.TakeLast(2).SequenceEqual(
        new[] { "status:4524:0", "refresh" }),
        "online lookup race completion messages");
}

static void GildPreGates()
{
    foreach (var role in new[]
             {
                 NativeSelfSocialRole.NoCorps,
                 NativeSelfSocialRole.Member,
                 NativeSelfSocialRole.CorpsViceOwner
             })
    {
        var host = new FakeHost { Role = role };
        Equal(555, CreateGild(host, Actor(), Bytes("G")),
            "Gild role result " + role);
        Sequence(new[] { "status:4564:555" }, host.Events,
            "Gild role events " + role);
    }

    var offline = new FakeHost { ActorOnline = false };
    Equal(4, CreateGild(offline, Actor(), Bytes("G")), "offline result");
    Sequence(new[] { "status:4564:4" }, offline.Events, "offline events");

    var stale = new FakeHost { HasDynamicCorps = false };
    Equal(5, CreateGild(stale, Actor(), Bytes("G")), "stale Corps result");
    Sequence(new[] { "status:4564:5" }, stale.Events, "stale events");

    foreach (var role in new[]
             {
                 NativeSelfSocialRole.CorpsOwner,
                 NativeSelfSocialRole.GildViceOwner,
                 NativeSelfSocialRole.GildOwner
             })
    {
        var host = new FakeHost { Role = role, ExistingGild = true };
        Equal(6, CreateGild(host, Actor(), Bytes("G")),
            "existing Gild result " + role);
        Sequence(new[] { "status:4564:6" }, host.Events,
            "existing Gild events " + role);
    }
}

static void GildDuplicateOrder()
{
    var host = new FakeHost { GildDuplicate = true };
    Equal(2, CreateGild(host, Actor(), Bytes("gIlD")),
        "Gild duplicate result");
    Sequence(Bytes("GILD"), host.LastGildLookup,
        "ASCII-only normalized Gild lookup");
    Sequence(new[]
    {
        "send:player-gild", "refresh", "status:4564:2"
    }, host.Events, "Gild duplicate events");
}

static void GildSuccessOrder()
{
    var host = new FakeHost();
    var queue = new RecordingQueue(host.Events);
    var result = NativeSelfCorpsGildExactStateMachine.CreateSelfGild(
        host, queue, Actor(), Bytes("Guild"));

    Equal(0, result, "Gild success result");
    Sequence(new[]
    {
        "allocate:gild", "publish:gild", "enqueue:InsertGildMember",
        "enqueue:InsertGild", "send:player-gild", "refresh",
        "broadcast:gild", "status:4564:0"
    }, host.Events, "Gild success order");
    Sequence(new[]
    {
        NativeSelfSocialWriteKind.InsertGildMember,
        NativeSelfSocialWriteKind.InsertGild
    }, queue.Commands.Select(command => command.Kind).ToArray(),
        "Gild enqueue order");
}

static void GildDoesNotValidateName()
{
    foreach (var name in new[]
             {
                 Array.Empty<byte>(),
                 new byte[] { 0 },
                 new byte[] { 0x81, 0x5C }
             })
    {
        var host = new FakeHost();
        Equal(0, CreateGild(host, Actor(), name),
            "Gild manager invented name result 1");
        Require(host.Events.Contains("status:4564:0"),
            "Gild raw name success status");
    }
}

static void IndependentFifoFailures()
{
    var corps = new NativeSelfSocialCorps(10, DateTime.UnixEpoch,
        Bytes("C"), 20);
    var gild = new NativeSelfSocialGild(30, DateTime.UnixEpoch,
        Bytes("G"), 10);
    var executor = new FakeExecutor
    {
        Fail = NativeSelfSocialWriteKind.InsertGildMember
    };
    var queue = new NativeSelfCorpsGildLegacyWriteQueue(executor);
    queue.Enqueue(NativeSelfSocialLegacyWriteCommand.InsertGildMember(
        gild, corps.Id));
    queue.Enqueue(NativeSelfSocialLegacyWriteCommand.InsertGild(gild));

    Equal(2, queue.PendingCount, "queued independent Gild writes");
    Require(queue.ProcessNext(), "failed first item not processed");
    Require(queue.ProcessNext(), "second item blocked by first failure");
    Require(!queue.ProcessNext(), "empty FIFO reported work");
    Equal(0, queue.PendingCount, "FIFO did not drain");
    Sequence(new[]
    {
        "execute:InsertGildMember", "failure:InsertGildMember:sql failed",
        "execute:InsertGild"
    }, executor.Events, "first write failure and partial persistence");

    var throwing = new FakeExecutor
    {
        Throw = NativeSelfSocialWriteKind.InsertCorps,
        ThrowWhileReporting = true
    };
    var corpsQueue = new NativeSelfCorpsGildLegacyWriteQueue(throwing);
    corpsQueue.Enqueue(NativeSelfSocialLegacyWriteCommand.InsertCorps(corps));
    corpsQueue.Enqueue(NativeSelfSocialLegacyWriteCommand.InsertCorpsMember(
        corps, Actor()));
    Require(corpsQueue.ProcessNext(), "exception item not consumed");
    Require(corpsQueue.ProcessNext(), "reporter exception stopped FIFO");
    Sequence(new[]
    {
        "execute:InsertCorps", "failure:InsertCorps:executor failed",
        "execute:InsertCorpsMember"
    }, throwing.Events, "executor/reporter exception order");
}

static void DormantProductionBoundary()
{
    var root = FindRepositoryRoot();
    var bridgePath = Path.Combine(root, "GameSvr", "ScriptSystem",
        "PasEngine", "PasApiBridge.cs");
    var bridge = File.ReadAllText(bridgePath);
    var cases = Slice(bridge, "case \"createselfcorps\":",
        "case \"addaccountstoragecnt\":");
    Require(cases.Contains("case \"createselfgild\":",
            StringComparison.Ordinal),
        "CreateSelfCorps/CreateSelfGild must share the creation branch");
    // The PAS cases now dispatch the LIVE corps/gild create (via the CM-side
    // NativeCorpsService.ApplyCorpsCreate/ApplyGildCreate, NOT the dormant exact
    // state machine below) — gated on SupportsGildWrites inside TPlayObject.
    Require(cases.Contains("TryCreateNativeCorpsFromScript",
                StringComparison.Ordinal)
            && cases.Contains("TryCreateNativeGildFromScript",
                StringComparison.Ordinal),
        "PAS creation entries must dispatch the live gated create");
    // ...and still fail closed (no packet) when no store is configured, so the
    // additive wiring never regresses the store-absent path.
    Require(cases.Contains("return RejectUnsupportedNativeApi(out result);",
            StringComparison.Ordinal),
        "PAS creation must stay fail-closed without a store");

    var helperName = nameof(NativeSelfCorpsGildExactStateMachine);
    var helperPath = Path.Combine(root, "GameSvr", "Services",
        helperName + ".cs");
    foreach (var sourcePath in Directory.EnumerateFiles(
                 Path.Combine(root, "GameSvr"), "*.cs",
                 SearchOption.AllDirectories))
    {
        if (StringComparer.OrdinalIgnoreCase.Equals(
                Path.GetFullPath(sourcePath), Path.GetFullPath(helperPath)))
            continue;
        Require(!File.ReadAllText(sourcePath).Contains(helperName,
                StringComparison.Ordinal),
            "dormant state machine is wired by " + sourcePath);
    }

    var store = File.ReadAllText(Path.Combine(root, "GameSvr", "Services",
        "NativeCorpsStore.cs"));
    var storeInterface = Slice(store, "interface INativeCorpsStore",
        "internal sealed class NativeCorpsMySqlStore");
    Require(!storeInterface.Contains("Enqueue", StringComparison.Ordinal) &&
            !storeInterface.Contains("LegacyWrite", StringComparison.Ordinal),
        "existing store unexpectedly claims exact legacy enqueue support");
}

static void NativeEvidenceAnchors()
{
    var staging = FindAncestorStaging();
    var managersPath = staging == null
        ? null
        : Path.Combine(staging, "ida_self_corps_gild_managers_20260731.txt");
    var constantsPath = staging == null
        ? null
        : Path.Combine(staging, "ida_self_corps_gild_constants_20260731.txt");
    if (managersPath == null || !File.Exists(managersPath)
        || constantsPath == null || !File.Exists(constantsPath))
    {
        Console.WriteLine(
            "SKIP NativeSelfCorpsGildExactStateCheck: IDA evidence files "
            + "ida_self_corps_gild_{managers,constants}_20260731.txt were not "
            + "found by walking ancestors for staging/.");
        Environment.Exit(2);
    }

    var managers = File.ReadAllText(managersPath);
    foreach (var anchor in new[]
             {
                 "FUNCTION CreateCorpsManager 005EA28C-005EA3C8",
                 "005EA353: call    sub_5E639C",
                 "005EA385: call    sub_5E639C",
                 "FUNCTION CreateGildManager 005E752C-005E762D",
                 "005E75DF: call    sub_5E639C",
                 "005E75FB: call    sub_5E639C",
                 "FUNCTION Depth1 005E639C-005E63B3"
             })
    {
        Require(managers.Contains(anchor, StringComparison.Ordinal),
            "native manager evidence missing: " + anchor);
    }

    var constants = File.ReadAllText(constantsPath);
    Require(constants.Contains(
            "INVALID_ASCII_BITMAP_004C70F0=ff ff ff ff ff ff 00 d4 00 00 00 10 00 00 00 10",
            StringComparison.Ordinal),
        "Corps name bitmap evidence missing");
}

static string FindAncestorStaging()
{
    var dir = new DirectoryInfo(FindRepositoryRoot());
    while (dir != null)
    {
        var staging = Path.Combine(dir.FullName, "staging");
        if (Directory.Exists(staging))
            return staging;
        dir = dir.Parent;
    }
    return null;
}

static int CreateCorps(FakeHost host, NativeSelfSocialActor actor,
    byte[] name) => NativeSelfCorpsGildExactStateMachine.CreateSelfCorps(
    host, new RecordingQueue(host.Events), actor, name);

static int CreateGild(FakeHost host, NativeSelfSocialActor actor,
    byte[] name) => NativeSelfCorpsGildExactStateMachine.CreateSelfGild(
    host, new RecordingQueue(host.Events), actor, name);

static NativeSelfSocialActor Actor() => new(20, "Actor", 80, 0, 1);
static byte[] Bytes(string value) => Encoding.ASCII.GetBytes(value);

static string Slice(string source, string startMarker, string endMarker)
{
    var start = source.IndexOf(startMarker, StringComparison.Ordinal);
    Require(start >= 0, "missing source marker: " + startMarker);
    var end = source.IndexOf(endMarker, start + startMarker.Length,
        StringComparison.Ordinal);
    Require(end > start, "missing source marker: " + endMarker);
    return source[start..end];
}

static string FindRepositoryRoot()
{
    foreach (var start in new[]
             {
                 Environment.CurrentDirectory, AppContext.BaseDirectory
             })
    {
        for (var directory = new DirectoryInfo(start); directory != null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameSvr",
                    "GameSvr.csproj")))
                return directory.FullName;
        }
    }
    throw new InvalidOperationException("repository root not found");
}

static void Equal<T>(T expected, T actual, string context)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{context}: expected {expected}, got {actual}");
}

static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual,
    string context)
{
    var expectedArray = expected.ToArray();
    var actualArray = actual.ToArray();
    if (!expectedArray.SequenceEqual(actualArray))
        throw new InvalidOperationException(
            $"{context}: expected [{string.Join(",", expectedArray)}], " +
            $"got [{string.Join(",", actualArray)}]");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class RecordingQueue : INativeSelfCorpsGildLegacyWriteQueue
{
    private readonly List<string> _events;

    internal RecordingQueue(List<string> events) => _events = events;
    internal List<NativeSelfSocialLegacyWriteCommand> Commands { get; } = new();

    public void Enqueue(NativeSelfSocialLegacyWriteCommand command)
    {
        Commands.Add(command);
        _events.Add("enqueue:" + command.Kind);
    }
}

sealed class FakeExecutor : INativeSelfCorpsGildLegacyWriteExecutor
{
    internal NativeSelfSocialWriteKind? Fail { get; init; }
    internal NativeSelfSocialWriteKind? Throw { get; init; }
    internal bool ThrowWhileReporting { get; init; }
    internal List<string> Events { get; } = new();

    public bool TryExecute(NativeSelfSocialLegacyWriteCommand command,
        out string error)
    {
        Events.Add("execute:" + command.Kind);
        if (command.Kind == Throw)
            throw new InvalidOperationException("executor failed");
        if (command.Kind == Fail)
        {
            error = "sql failed";
            return false;
        }
        error = string.Empty;
        return true;
    }

    public void ReportFailure(NativeSelfSocialLegacyWriteCommand command,
        string error)
    {
        Events.Add($"failure:{command.Kind}:{error}");
        if (ThrowWhileReporting)
            throw new InvalidOperationException("logger failed");
    }
}

sealed class FakeHost : INativeSelfCorpsGildExactHost
{
    internal bool HasCorpsPointer { get; init; }
    internal bool MemberIndexed { get; init; }
    internal bool CorpsDuplicate { get; init; }
    internal bool GildDuplicate { get; init; }
    internal NativeSelfSocialRole Role { get; init; } =
        NativeSelfSocialRole.CorpsOwner;
    internal bool ActorOnline { get; init; } = true;
    internal bool HasDynamicCorps { get; init; } = true;
    internal bool ExistingGild { get; init; }
    internal bool BindOnlineCorps { get; init; } = true;
    internal byte[] LastCorpsLookup { get; private set; } = Array.Empty<byte>();
    internal byte[] LastGildLookup { get; private set; } = Array.Empty<byte>();
    internal List<string> Events { get; } = new();

    public bool HasPlayerCorpsPointer(NativeSelfSocialActor actor) =>
        HasCorpsPointer;
    public bool IsMemberIndexed(NativeSelfSocialActor actor) => MemberIndexed;

    public bool CorpsNameExists(ReadOnlyMemory<byte> normalizedNameGbk)
    {
        LastCorpsLookup = normalizedNameGbk.ToArray();
        return CorpsDuplicate;
    }

    public bool GildNameExists(ReadOnlyMemory<byte> normalizedNameGbk)
    {
        LastGildLookup = normalizedNameGbk.ToArray();
        return GildDuplicate;
    }

    public NativeSelfSocialRole GetRole(NativeSelfSocialActor actor) => Role;
    public bool IsActorOnline(NativeSelfSocialActor actor) => ActorOnline;

    public bool TryGetDynamicCorpsId(NativeSelfSocialActor actor,
        out long corpsId)
    {
        corpsId = HasDynamicCorps ? 10 : 0;
        return HasDynamicCorps;
    }

    public bool CorpsHasGild(long corpsId) => ExistingGild;

    public NativeSelfSocialCorps AllocateCorps(ReadOnlyMemory<byte> nameGbk,
        NativeSelfSocialActor owner)
    {
        Events.Add("allocate:corps");
        return new NativeSelfSocialCorps(10, DateTime.UnixEpoch, nameGbk,
            owner.Id);
    }

    public NativeSelfSocialGild AllocateGild(ReadOnlyMemory<byte> nameGbk,
        long ownerCorpsId)
    {
        Events.Add("allocate:gild");
        return new NativeSelfSocialGild(30, DateTime.UnixEpoch, nameGbk,
            ownerCorpsId);
    }

    public void PublishCorps(NativeSelfSocialCorps corps) =>
        Events.Add("publish:corps");
    public void AssignOwnerMemberCorps(NativeSelfSocialActor owner,
        NativeSelfSocialCorps corps) => Events.Add("assign:owner-corps");
    public void AddOwnerMemberToCorps(NativeSelfSocialCorps corps,
        NativeSelfSocialActor owner) => Events.Add("add:owner-member");
    public void PublishMemberIndex(NativeSelfSocialActor owner,
        NativeSelfSocialCorps corps) => Events.Add("publish:member-index");

    public bool TryBindOnlinePlayerCorps(NativeSelfSocialActor owner,
        NativeSelfSocialCorps corps)
    {
        Events.Add("bind:corps");
        return BindOnlineCorps;
    }

    public void PublishGild(NativeSelfSocialGild gild, long ownerCorpsId) =>
        Events.Add("publish:gild");
    public void SendPlayerCorps(NativeSelfSocialActor actor) =>
        Events.Add("send:player-corps");
    public void BroadcastCorpsCreated(NativeSelfSocialCorps corps) =>
        Events.Add("broadcast:corps");
    public void SendPlayerGild(NativeSelfSocialActor actor) =>
        Events.Add("send:player-gild");
    public void BroadcastGildCreated(NativeSelfSocialGild gild) =>
        Events.Add("broadcast:gild");
    public void SendCreateStatus(int ident, int result) =>
        Events.Add($"status:{ident}:{result}");
    public void SendSocialRoleRefresh(NativeSelfSocialActor actor) =>
        Events.Add("refresh");
}
