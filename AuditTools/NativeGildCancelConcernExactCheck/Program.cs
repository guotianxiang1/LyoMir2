using GameSvr.Services;

var tests = new (string Name, Action Run)[]
{
    ("role strategy", RoleStrategy),
    ("manager return gates", ManagerReturnGates),
    ("success order and command", SuccessOrderAndCommand),
    ("duplicate cancellation", DuplicateCancellation),
    ("asynchronous SQL failure", AsynchronousSqlFailure),
    ("dormant production boundary", DormantProductionBoundary),
    ("native evidence anchors", NativeEvidenceAnchors)
};

foreach (var test in tests) test.Run();
Console.WriteLine(
    $"NativeGildCancelConcernExactCheck PASS tests={tests.Length} " +
    "ident=4578 results=555/5/12/25/1000/0 " +
    "order=memory-delete>async-sql failure=no-rollback");
return;

static void RoleStrategy()
{
    foreach (var role in Enum.GetValues<NativeSelfSocialRole>()
                 .Where(value => value != NativeSelfSocialRole.GildOwner))
    {
        var host = new FakeHost();
        var writes = new RecordingQueue(host.Events);
        Equal(555, NativeGildCancelConcernTransaction.Execute(role, host,
                writes, 10, 200), "role result " + role);
        Equal(0, host.Events.Count, "role side effects " + role);
        Equal(0, writes.Commands.Count, "role writes " + role);
    }
}

static void ManagerReturnGates()
{
    var missingActor = new FakeHost { ActorExists = false };
    Equal(5, Execute(missingActor), "missing actor");
    Sequence(new[] { "actor:10" }, missingActor.Events,
        "missing actor order");

    var missingGild = new FakeHost { ActorGildId = 0 };
    Equal(12, Execute(missingGild), "missing actor Gild");
    Sequence(new[] { "actor:10" }, missingGild.Events,
        "missing Gild order");

    var missingTarget = new FakeHost { TargetExists = false };
    Equal(25, Execute(missingTarget), "missing target Gild");
    Sequence(new[] { "actor:10", "target:200" }, missingTarget.Events,
        "missing target order");

    var missingConcern = new FakeHost { ConcernExists = false };
    Equal(1000, Execute(missingConcern), "missing concern");
    Sequence(new[] { "actor:10", "target:200", "remove:100:200" },
        missingConcern.Events, "missing concern order");
}

static void SuccessOrderAndCommand()
{
    var host = new FakeHost();
    var writes = new RecordingQueue(host.Events);
    Equal(0, NativeGildCancelConcernTransaction.Execute(
            NativeSelfSocialRole.GildOwner, host, writes, 10, 200),
        "success result");
    Sequence(new[]
    {
        "actor:10", "target:200", "remove:100:200", "enqueue:100:200"
    }, host.Events, "success order");
    Equal(1, writes.Commands.Count, "success command count");
    Equal(100L, writes.Commands[0].GildId, "source Gild ID");
    Equal(200L, writes.Commands[0].DestinationGildId,
        "destination Gild ID");
    Equal("delete from gamedata.gildconcern where GildID = %d and " +
          "DstGildID = %d;", NativeGildConcernDeleteCommand.LegacySqlTemplate,
        "legacy SQL template");
}

static void DuplicateCancellation()
{
    var host = new FakeHost();
    var writes = new RecordingQueue(host.Events);
    Equal(0, NativeGildCancelConcernTransaction.Execute(
            NativeSelfSocialRole.GildOwner, host, writes, 10, 200),
        "first cancellation");
    Equal(1000, NativeGildCancelConcernTransaction.Execute(
            NativeSelfSocialRole.GildOwner, host, writes, 10, 200),
        "duplicate cancellation");
    Equal(1, writes.Commands.Count, "duplicate queued a second delete");
}

static void AsynchronousSqlFailure()
{
    var executor = new FakeExecutor { FailFirst = true };
    var queue = new NativeGildConcernLegacyDeleteQueue(executor);
    queue.Enqueue(new NativeGildConcernDeleteCommand(100, 200));
    queue.Enqueue(new NativeGildConcernDeleteCommand(100, 300));

    Equal(2, queue.PendingCount, "pending before worker");
    Require(queue.ProcessNext(), "failed SQL command was not consumed");
    Require(queue.ProcessNext(), "SQL failure stopped the FIFO");
    Require(!queue.ProcessNext(), "empty FIFO reported work");
    Equal(0, queue.PendingCount, "worker did not drain");
    Sequence(new[]
    {
        "execute:100:200", "sql-failed:100:200:database rejected delete",
        "execute:100:300"
    }, executor.Events, "failure log and continuation");
}

static void DormantProductionBoundary()
{
    var root = FindRepositoryRoot();
    var protocolPath = Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.NativeGuildRelationTailProtocol.cs");
    var protocol = File.ReadAllText(protocolPath);
    var branch = Slice(protocol, "case Grobal2.CM_GILD_CANCLE_CONCERN:",
        "case Grobal2.CM_GILD_DECLARE_WAR:");
    Require(branch.Contains("SendUnsupportedNativeGuildIdOperation",
            StringComparison.Ordinal),
        "4578 was opened without the live concern catalog/store adapter");

    var helperName = nameof(NativeGildCancelConcernTransaction);
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
            "dormant transaction is wired by " + sourcePath);
    }
}

static void NativeEvidenceAnchors()
{
    var evidencePath = FindEvidenceFile(
        Path.Combine("staging", "ida_gild_write_inventory_20260731.txt"));
    var evidence = File.ReadAllText(evidencePath);
    foreach (var anchor in new[]
             {
                 "FUNCTION 4578_cancel_concern 006F68AC-006F68F0",
                 "FUNCTION 4578_owner_cancel_concern 00703C54-00703CEA",
                 "00703C6F: mov     esi, 5",
                 "00703C7D: mov     esi, 0Ch",
                 "00703C9C: mov     esi, 19h",
                 "00703CDD: mov     esi, 3E8h",
                 "00703CA7: call    sub_70678C",
                 "00703CD6: call    sub_5E639C",
                 "005E9CC3: mov     eax, offset aDeleteFromGame_8",
                 "005E9CF2: mov     edx, [ebp+var_18]"
             })
    {
        Require(evidence.Contains(anchor, StringComparison.Ordinal),
            "native evidence missing: " + anchor);
    }
}

static int Execute(FakeHost host) =>
    NativeGildCancelConcernTransaction.Execute(
        NativeSelfSocialRole.GildOwner, host,
        new RecordingQueue(host.Events), 10, 200);

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

    static string FindEvidenceFile(string relativePath)
    {
        var probed = new List<string>();
        foreach (var start in new[]
                 {
                     FindRepositoryRoot(), Environment.CurrentDirectory,
                     AppContext.BaseDirectory
                 })
        {
            for (var directory = new DirectoryInfo(start); directory != null;
                 directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, relativePath);
                if (File.Exists(candidate)) return candidate;
                probed.Add(candidate);
            }
        }
        throw new InvalidOperationException(
            relativePath + " not found; probed: " + string.Join("; ", probed));
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

sealed class FakeHost : INativeGildCancelConcernHost
{
    internal bool ActorExists { get; init; } = true;
    internal long ActorGildId { get; init; } = 100;
    internal bool TargetExists { get; init; } = true;
    internal bool ConcernExists { get; set; } = true;
    internal List<string> Events { get; } = new();

    public bool TryGetActor(long actorId, out NativeGildConcernActor actor)
    {
        Events.Add("actor:" + actorId);
        actor = ActorExists
            ? new NativeGildConcernActor(actorId, ActorGildId)
            : null;
        return ActorExists;
    }

    public bool GildExists(long gildId)
    {
        Events.Add("target:" + gildId);
        return TargetExists;
    }

    public bool RemoveConcern(long gildId, long destinationGildId)
    {
        Events.Add($"remove:{gildId}:{destinationGildId}");
        if (!ConcernExists) return false;
        ConcernExists = false;
        return true;
    }
}

sealed class RecordingQueue : INativeGildConcernDeleteQueue
{
    private readonly List<string> _events;

    internal RecordingQueue(List<string> events) => _events = events;
    internal List<NativeGildConcernDeleteCommand> Commands { get; } = new();

    public void Enqueue(NativeGildConcernDeleteCommand command)
    {
        Commands.Add(command);
        _events.Add($"enqueue:{command.GildId}:{command.DestinationGildId}");
    }
}

sealed class FakeExecutor : INativeGildConcernDeleteExecutor
{
    internal bool FailFirst { get; init; }
    internal List<string> Events { get; } = new();

    public bool TryExecute(NativeGildConcernDeleteCommand command,
        out string error)
    {
        Events.Add($"execute:{command.GildId}:{command.DestinationGildId}");
        if (FailFirst && command.DestinationGildId == 200)
        {
            error = "database rejected delete";
            return false;
        }
        error = string.Empty;
        return true;
    }

    public void ReportSqlFailure(NativeGildConcernDeleteCommand command,
        string error) => Events.Add(
        $"sql-failed:{command.GildId}:{command.DestinationGildId}:{error}");
}
