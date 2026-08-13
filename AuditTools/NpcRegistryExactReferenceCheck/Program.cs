using System.Collections.Concurrent;
using System.Reflection;
using GameSvr;

PrepareRuntimeConfig();
AuditProductionAccesses();

var previousObjectManager = M2Share.ObjectManager;
var previousProcessLock = M2Share.ProcessHumanCriticalSection;
var trackedObjects = new List<(int RegisteredId, TBaseObject Actor)>();
M2Share.ObjectManager = new ObjectManager();
M2Share.ProcessHumanCriticalSection = new object();

try
{
    var engine = new UserEngine();
    CheckExactIdentity(engine, trackedObjects);
    CheckDynamicRoomQuestClassification(engine, trackedObjects);
    CheckCursorMaintenance(engine, trackedObjects);
    CheckSharedProcessBoundary(engine, trackedObjects);
    CheckConcurrentSnapshots(engine, trackedObjects);
}
finally
{
    foreach (var tracked in trackedObjects)
        M2Share.ObjectManager.Remove(tracked.RegisteredId, tracked.Actor);
    M2Share.ObjectManager = previousObjectManager;
    M2Share.ProcessHumanCriticalSection = previousProcessLock;
}

Console.WriteLine("NpcRegistryExactReferenceCheck PASS "
    + "identity=exact same-id-replacement=preserved duplicates=purged "
    + "dynamic=reload-isolated cursor=maintained sync=process-human snapshots=concurrent");

static void CheckExactIdentity(UserEngine engine,
    List<(int RegisteredId, TBaseObject Actor)> tracked)
{
    var original = Track(new Merchant(), tracked);
    var replacement = Track(new Merchant(), tracked);
    var replacementRegisteredId = replacement.ObjectId;
    var objectId = typeof(TBaseObject).GetField(nameof(TBaseObject.ObjectId),
        BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException("TBaseObject.ObjectId is missing");
    objectId.SetValue(replacement, original.ObjectId);
    Equal(original.ObjectId, replacement.ObjectId,
        "same-ID replacement setup failed");

    Assert(engine.TryAddMerchantExact(original), "initial merchant add failed");
    Assert(!engine.TryAddMerchantExact(original),
        "duplicate merchant reference was accepted");
    Assert(engine.TryAddMerchantExact(replacement),
        "same-ID replacement reference was rejected");
    Assert(engine.ContainsRegisteredNpcExact(original),
        "registered original merchant was not found");

    var isolatedSnapshot = engine.SnapshotMerchants();
    Equal(2, isolatedSnapshot.Length, "merchant snapshot count");
    isolatedSnapshot[0] = null;
    Equal(2, engine.SnapshotMerchants().Length,
        "caller mutated the live merchant registry through a snapshot");

    Assert(engine.TryRemoveMerchantExact(original),
        "exact merchant removal failed");
    var remaining = engine.SnapshotMerchants();
    Equal(1, remaining.Length, "replacement count after original removal");
    Assert(ReferenceEquals(replacement, remaining[0]),
        "removing an original removed its same-ID replacement");
    Assert(!engine.TryRemoveMerchantExact(original),
        "repeated exact removal succeeded");

    Assert(engine.TryAddMerchantExact(original),
        "same-ID survivor merchant re-registration failed");
    Assert(engine.TryAddQuestNpcExact(original),
        "same-ID survivor quest registration failed");
    engine.m_MerchantList.Add(replacement);
    engine.QuestNPCList.Add(replacement);
    engine.QuestNPCList.Add(replacement);
    Assert(engine.TryRemoveRegisteredNpcEverywhereExact(replacement),
        "cross-registry exact removal failed");
    Assert(!engine.SnapshotMerchants().Any(candidate =>
            ReferenceEquals(candidate, replacement))
        && !engine.SnapshotQuestNpcs().Any(candidate =>
            ReferenceEquals(candidate, replacement)),
        "cross-registry exact removal left a duplicate reference");
    Assert(engine.SnapshotMerchants().Any(candidate =>
            ReferenceEquals(candidate, original))
        && engine.SnapshotQuestNpcs().Any(candidate =>
            ReferenceEquals(candidate, original)),
        "cross-registry exact removal removed a same-ID survivor");
    engine.TryRemoveRegisteredNpcEverywhereExact(original);

    var questOriginal = Track(new NormNpc(), tracked);
    var questReplacement = Track(new NormNpc(), tracked);
    objectId.SetValue(questReplacement, questOriginal.ObjectId);
    Assert(engine.TryAddQuestNpcExact(questOriginal),
        "initial quest NPC add failed");
    Assert(engine.TryAddQuestNpcExact(questReplacement),
        "same-ID quest NPC replacement was rejected");
    Assert(engine.TryRemoveQuestNpcExact(questOriginal),
        "exact quest NPC removal failed");
    var questRemaining = engine.SnapshotQuestNpcs();
    Equal(1, questRemaining.Length, "quest replacement count");
    Assert(ReferenceEquals(questReplacement, questRemaining[0]),
        "removing a quest NPC removed its same-ID replacement");
    engine.TryRemoveQuestNpcExact(questReplacement);

    Equal(replacementRegisteredId, tracked[1].RegisteredId,
        "tracked ObjectManager key changed with the test ObjectId");
}

static void CheckCursorMaintenance(UserEngine engine,
    List<(int RegisteredId, TBaseObject Actor)> tracked)
{
    var first = Track(new Merchant(), tracked);
    var second = Track(new Merchant(), tracked);
    var third = Track(new Merchant(), tracked);
    engine.TryAddMerchantExact(first);
    engine.TryAddMerchantExact(second);
    engine.TryAddMerchantExact(third);

    var merchantPosition = RequiredField("nMerchantPosition");
    merchantPosition.SetValue(engine, 2);
    engine.TryRemoveMerchantExact(first);
    Equal(1, (int)merchantPosition.GetValue(engine),
        "merchant cursor did not follow a preceding removal");
    engine.TryRemoveMerchantExact(second);
    Equal(0, (int)merchantPosition.GetValue(engine),
        "merchant cursor did not follow a second preceding removal");
    engine.TryRemoveMerchantExact(third);
    Equal(0, (int)merchantPosition.GetValue(engine),
        "empty merchant registry cursor was not reset");

    var questFirst = Track(new NormNpc(), tracked);
    var questSecond = Track(new NormNpc(), tracked);
    var questThird = Track(new NormNpc(), tracked);
    engine.TryAddQuestNpcExact(questFirst);
    engine.TryAddQuestNpcExact(questSecond);
    engine.TryAddQuestNpcExact(questThird);

    var npcPosition = RequiredField("nNpcPosition");
    npcPosition.SetValue(engine, 2);
    engine.TryRemoveQuestNpcExact(questFirst);
    Equal(1, (int)npcPosition.GetValue(engine),
        "quest NPC cursor did not follow a preceding removal");
    engine.TryRemoveRegisteredNpcEverywhereExact(questSecond);
    Equal(0, (int)npcPosition.GetValue(engine),
        "cross-registry removal did not maintain the quest NPC cursor");
    engine.TryRemoveQuestNpcExact(questThird);
}

static void CheckDynamicRoomQuestClassification(UserEngine engine,
    List<(int RegisteredId, TBaseObject Actor)> tracked)
{
    var ordinary = Track(new NormNpc(), tracked);
    var dynamicNpc = Track(new Merchant(), tracked);

    Assert(engine.TryAddQuestNpcExact(ordinary),
        "ordinary quest NPC add failed");
    Assert(engine.TryAddDynamicRoomQuestNpcExact(dynamicNpc),
        "dynamic-room quest NPC add failed");
    Assert(engine.IsDynamicRoomQuestNpcExact(dynamicNpc)
           && !engine.IsDynamicRoomQuestNpcExact(ordinary),
        "dynamic-room quest classification was not exact-reference based");
    var reloadable = engine.SnapshotReloadableQuestNpcs();
    Assert(reloadable.Any(candidate => ReferenceEquals(candidate, ordinary))
           && !reloadable.Any(candidate => ReferenceEquals(candidate,
               dynamicNpc)),
        "ordinary reload snapshot included a dynamic-room quest NPC");

    Assert(engine.TryRemoveQuestNpcExact(dynamicNpc)
           && !engine.IsDynamicRoomQuestNpcExact(dynamicNpc),
        "dynamic-room classification survived exact registry removal");
    engine.TryRemoveQuestNpcExact(ordinary);
}

static void CheckSharedProcessBoundary(UserEngine engine,
    List<(int RegisteredId, TBaseObject Actor)> tracked)
{
    var merchant = Track(new Merchant(), tracked);
    var questNpc = Track(new NormNpc(), tracked);
    var processLock = M2Share.ProcessHumanCriticalSection;
    using var ready = new CountdownEvent(4);
    Task[] blocked;

    Monitor.Enter(processLock);
    try
    {
        blocked = new Task[]
        {
            Task.Run(() => { ready.Signal(); engine.TryAddMerchantExact(merchant); }),
            Task.Run(() => { ready.Signal(); engine.TryAddQuestNpcExact(questNpc); }),
            Task.Run(() => { ready.Signal(); engine.SnapshotMerchants(); }),
            Task.Run(() => { ready.Signal(); engine.SnapshotNpcRegistry(
                out _, out _); })
        };
        Assert(ready.Wait(TimeSpan.FromSeconds(2)),
            "registry boundary probes did not start");
        Thread.Sleep(40);
        Assert(blocked.All(task => !task.IsCompleted),
            "an NPC registry operation bypassed ProcessHumanCriticalSection");
    }
    finally
    {
        Monitor.Exit(processLock);
    }

    Assert(Task.WaitAll(blocked, TimeSpan.FromSeconds(5)),
        "registry operations did not resume after the process lock was released");
    engine.TryRemoveMerchantExact(merchant);
    engine.TryRemoveQuestNpcExact(questNpc);
}

static void CheckConcurrentSnapshots(UserEngine engine,
    List<(int RegisteredId, TBaseObject Actor)> tracked)
{
    var merchants = Enumerable.Range(0, 8)
        .Select(_ => Track(new Merchant(), tracked)).ToArray();
    var questNpcs = Enumerable.Range(0, 8)
        .Select(_ => Track(new NormNpc(), tracked)).ToArray();
    var failures = new ConcurrentQueue<Exception>();
    using var start = new ManualResetEventSlim(false);

    var tasks = new List<Task>
    {
        Guarded(() => ToggleMerchants(engine, merchants, start), failures),
        Guarded(() => ToggleQuestNpcs(engine, questNpcs, start), failures)
    };
    for (var reader = 0; reader < 4; reader++)
    {
        tasks.Add(Guarded(() => ReadSnapshots(engine, start), failures));
    }

    start.Set();
    Assert(Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(10)),
        "concurrent registry audit timed out");
    if (failures.TryDequeue(out var failure))
        throw new InvalidOperationException(
            "concurrent registry operation failed", failure);

    foreach (var merchant in merchants)
        engine.TryRemoveMerchantExact(merchant);
    foreach (var questNpc in questNpcs)
        engine.TryRemoveQuestNpcExact(questNpc);
}

static void ToggleMerchants(UserEngine engine, Merchant[] merchants,
    ManualResetEventSlim start)
{
    start.Wait();
    for (var round = 0; round < 2_000; round++)
    {
        var merchant = merchants[round % merchants.Length];
        Assert(engine.TryAddMerchantExact(merchant),
            "concurrent merchant add failed");
        Assert(!engine.TryAddMerchantExact(merchant),
            "concurrent duplicate merchant add succeeded");
        Assert(engine.TryRemoveMerchantExact(merchant),
            "concurrent merchant removal failed");
    }
}

static void ToggleQuestNpcs(UserEngine engine, NormNpc[] questNpcs,
    ManualResetEventSlim start)
{
    start.Wait();
    for (var round = 0; round < 2_000; round++)
    {
        var questNpc = questNpcs[round % questNpcs.Length];
        Assert(engine.TryAddQuestNpcExact(questNpc),
            "concurrent quest NPC add failed");
        Assert(!engine.TryAddQuestNpcExact(questNpc),
            "concurrent duplicate quest NPC add succeeded");
        Assert(engine.TryRemoveQuestNpcExact(questNpc),
            "concurrent quest NPC removal failed");
    }
}

static void ReadSnapshots(UserEngine engine, ManualResetEventSlim start)
{
    start.Wait();
    for (var round = 0; round < 4_000; round++)
    {
        engine.SnapshotNpcRegistry(out var merchants, out var questNpcs);
        AssertNoDuplicateReferences(merchants, "merchant snapshot");
        AssertNoDuplicateReferences(questNpcs, "quest NPC snapshot");
        AssertNoDuplicateReferences(engine.SnapshotMerchants(),
            "standalone merchant snapshot");
        AssertNoDuplicateReferences(engine.SnapshotQuestNpcs(),
            "standalone quest NPC snapshot");
    }
}

static Task Guarded(Action action, ConcurrentQueue<Exception> failures) =>
    Task.Run(() =>
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            failures.Enqueue(ex);
        }
    });

static void AssertNoDuplicateReferences<T>(T[] snapshot, string name)
    where T : class
{
    for (var i = 0; i < snapshot.Length; i++)
    {
        Assert(snapshot[i] != null, name + " contains null");
        for (var j = i + 1; j < snapshot.Length; j++)
        {
            Assert(!ReferenceEquals(snapshot[i], snapshot[j]),
                name + " contains a duplicate reference");
        }
    }
}

static T Track<T>(T actor,
    List<(int RegisteredId, TBaseObject Actor)> tracked) where T : TBaseObject
{
    tracked.Add((actor.ObjectId, actor));
    return actor;
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

static FieldInfo RequiredField(string name) =>
    typeof(UserEngine).GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("UserEngine field is missing: " + name);

static void AuditProductionAccesses()
{
    var root = FindRepositoryRoot();
    var gameSvr = Path.Combine(root, "GameSvr");
    var userEnginePath = Path.Combine(gameSvr, "UsrSystem", "UsrEngn.cs");
    var registryPath = Path.Combine(gameSvr, "UsrSystem",
        "UserEngine.NpcRegistry.cs");

    foreach (var path in Directory.EnumerateFiles(gameSvr, "*.cs",
                 SearchOption.AllDirectories))
    {
        if (path.EndsWith(".orig.cs", StringComparison.OrdinalIgnoreCase)
            || IsGeneratedPath(path)
            || path.Equals(userEnginePath, StringComparison.OrdinalIgnoreCase)
            || path.Equals(registryPath, StringComparison.OrdinalIgnoreCase))
            continue;

        var source = File.ReadAllText(path);
        Assert(!source.Contains("m_MerchantList", StringComparison.Ordinal)
            && !source.Contains("QuestNPCList", StringComparison.Ordinal),
            "production code bypasses the NPC registry API: "
            + Path.GetRelativePath(root, path));
    }

    var userEngineLines = File.ReadAllLines(userEnginePath);
    var userEngine = string.Join(Environment.NewLine, userEngineLines);
    var allowedDirectLines = new HashSet<string>(StringComparer.Ordinal)
    {
        "public IList<Merchant> m_MerchantList;",
        "public IList<NormNpc> QuestNPCList;",
        "m_MerchantList = new List<Merchant>();",
        "QuestNPCList = new List<NormNpc>();",
        "for (var i = nMerchantPosition; i < m_MerchantList.Count; i++)",
        "var merchantNpc = m_MerchantList[i];",
        "for (var i = nNpcPosition; i < QuestNPCList.Count; i++)",
        "NPC = QuestNPCList[i];"
    };
    // 注释行不是访问。UsrEngn.cs 里有两行字节证据注释提到这两个表
    // （NULL 槽 + sub_67D8F0 五分钟延迟释放 FIFO + vtable+0x7C 钩子的说明），
    // 原来的逐行 Contains 把它们数成了直接访问，计数从 8 变 10。
    var directLines = userEngineLines
        .Select(line => line.Trim())
        .Where(line => !line.StartsWith("//", StringComparison.Ordinal)
            && !line.StartsWith("*", StringComparison.Ordinal))
        .Where(line => line.Contains("m_MerchantList", StringComparison.Ordinal)
            || line.Contains("QuestNPCList", StringComparison.Ordinal))
        .ToArray();
    Equal(allowedDirectLines.Count, directLines.Length,
        "unexpected UserEngine direct NPC registry access count");
    foreach (var line in directLines)
    {
        Assert(allowedDirectLines.Contains(line),
            "UserEngine directly accesses an NPC registry outside the cursor loops: "
            + line);
    }
    Require(userEngine,
        "for (var i = nMerchantPosition; i < m_MerchantList.Count; i++)");
    Require(userEngine, "var merchantNpc = m_MerchantList[i];");
    Require(userEngine,
        "for (var i = nNpcPosition; i < QuestNPCList.Count; i++)");
    Require(userEngine, "NPC = QuestNPCList[i];");
    Assert(!userEngine.Contains("m_MerchantList.Add(", StringComparison.Ordinal)
        && !userEngine.Contains("m_MerchantList.Remove", StringComparison.Ordinal)
        && !userEngine.Contains("QuestNPCList.Add(", StringComparison.Ordinal)
        && !userEngine.Contains("QuestNPCList.Remove", StringComparison.Ordinal),
        "UserEngine mutates an NPC list outside the exact registry API");

    var processData = Slice(userEngine, "private void PrcocessData()",
        "public void ClearItemList()");
    RequireInOrder(processData,
        "HUtil32.EnterCriticalSection(M2Share.ProcessHumanCriticalSection);",
        "ProcessMerchants();",
        "ProcessNpcs();",
        "HUtil32.LeaveCriticalSection(M2Share.ProcessHumanCriticalSection);");

    var registry = File.ReadAllText(registryPath);
    Require(registry,
        "Volatile.Read(ref M2Share.ProcessHumanCriticalSection)");
    Require(registry, "TryAddMerchantExact");
    Require(registry, "TryRemoveMerchantExact");
    Require(registry, "TryAddQuestNpcExact");
    Require(registry, "TryRemoveQuestNpcExact");
    Require(registry, "TryRemoveRegisteredNpcEverywhereExact");
    Require(registry, "ReferenceEquals(list[i], expected)");
    Require(registry, "if (i < processPosition) processPosition--;");
    Assert(!registry.Contains("ObjectId", StringComparison.Ordinal),
        "exact NPC registry operations compare object IDs");
}

static bool IsGeneratedPath(string path)
{
    var normalized = path.Replace('\\', '/');
    return normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
        || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
}

static string Slice(string source, string startToken, string endToken)
{
    var start = source.IndexOf(startToken, StringComparison.Ordinal);
    var end = source.IndexOf(endToken, start, StringComparison.Ordinal);
    Assert(start >= 0 && end > start,
        "source slice tokens are missing: " + startToken);
    return source[start..end];
}

static void Require(string source, string token) =>
    Assert(source.Contains(token, StringComparison.Ordinal),
        "required source token is missing: " + token);

static void RequireInOrder(string source, params string[] tokens)
{
    var offset = 0;
    foreach (var token in tokens)
    {
        var index = source.IndexOf(token, offset, StringComparison.Ordinal);
        Assert(index >= 0, "source token is missing or out of order: " + token);
        offset = index + token.Length;
    }
}

static string FindRepositoryRoot() => AuditRepoRoot.Resolve();

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
