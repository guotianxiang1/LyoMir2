using System.Reflection;
using GameSvr;
using SystemModule;

PrepareRuntimeConfig();
M2Share.ProcessHumanCriticalSection = new object();

var root = Path.Combine(Path.GetTempPath(), "dynroom-materializer-"
    + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    CommitAndExactDestroy(root);
    FailedCommitCompensates(root);
    PreparedRollbackIsInvisible(root);
}
finally
{
    Directory.Delete(root, true);
}

Console.WriteLine("DynRoomPhysicalNpcMaterializerCheck PASS "
    + "actor=deferred controller=hidden configured=map+dynamic-registry "
    + "commit=compensated destroy=exact+idempotent ABA=replacement-preserved");
return;

static void CommitAndExactDestroy(string scriptRoot)
{
    var context = CreateContext(scriptRoot, "CommitRoom", 2, 3);
    Assert(context.Materializer.TryPrepare(context.Definition,
            context.Environment, 7, context.Bindings, out var journal,
            out var diagnostic), diagnostic ?? "prepare failed");

    var controller = (Merchant)journal.Npcs.Single(entry =>
        entry.Binding.Role ==
        NativeDynamicRoomDynamicNpcScriptRole.HiddenController).Npc;
    var configured = (Merchant)journal.Npcs.Single(entry =>
        entry.Binding.Role ==
        NativeDynamicRoomDynamicNpcScriptRole.ConfiguredVisible).Npc;

    Assert(M2Share.ObjectManager.Get(controller.ObjectId) == null
           && M2Share.ObjectManager.Get(configured.ObjectId) == null,
        "prepared actors escaped deferred registration");
    Equal(0, MovingReferenceCount(context.Environment, configured),
        "prepared configured NPC map publication");
    Assert(!M2Share.UserEngine.ContainsRegisteredNpcExact(configured),
        "prepared configured NPC registry publication");
    Assert(controller.m_boIsHide && !controller.m_boIsQuest
           && controller.m_nCurrX == 0 && controller.m_nCurrY == 0,
        "hidden controller fields");
    Assert(!configured.m_boIsHide && configured.m_boIsQuest
           && configured.m_sScript == "Guide"
           && configured.m_sCharName == "RoomGuide"
           && configured.m_nCurrX == 2 && configured.m_nCurrY == 3
           && configured.m_nFlag == 4 && configured.m_wAppr == 5,
        "configured NPC fields");

    Assert(journal.TryCommit() && journal.TryCommit()
           && journal.IsCommitted && !journal.HasUnresolvedPublication,
        "journal commit was not exact and idempotent");
    Assert(ReferenceEquals(M2Share.ObjectManager.Get(controller.ObjectId),
               controller)
           && ReferenceEquals(M2Share.ObjectManager.Get(configured.ObjectId),
               configured),
        "committed actors missing from ObjectManager");
    Equal(0, MovingReferenceCount(context.Environment, controller),
        "hidden controller was placed in map cells");
    Equal(1, MovingReferenceCount(context.Environment, configured),
        "configured NPC map publication");
    Assert(!M2Share.UserEngine.ContainsRegisteredNpcExact(controller)
           && M2Share.UserEngine.IsDynamicRoomQuestNpcExact(configured),
        "controller/configured global registry roles");

    Assert(journal.TryClaimOwnership(new object()),
        "committed journal ownership claim");
    var replacement = CreateSameIdReplacement(configured,
        context.Environment);
    Assert(journal.TryDestroyExact() && journal.TryDestroyExact()
           && journal.IsDestroyed && !journal.HasUnresolvedPublication,
        "journal exact destroy was not idempotent");
    Assert(controller.m_boGhost && configured.m_boGhost,
        "destroyed NPCs were not ghosted");
    Equal(0, MovingReferenceCount(context.Environment, configured),
        "destroyed configured NPC remained in map cells");
    Assert(!M2Share.UserEngine.ContainsRegisteredNpcExact(configured),
        "destroyed configured NPC remained in registry");
    Assert(ReferenceEquals(M2Share.ObjectManager.Get(replacement.ObjectId),
            replacement),
        "same-ID replacement was removed by exact destroy");
    M2Share.ObjectManager.Remove(replacement.ObjectId, replacement);
}

static void FailedCommitCompensates(string scriptRoot)
{
    var context = CreateContext(scriptRoot, "FailureRoom", 30, 30);
    Assert(context.Materializer.TryPrepare(context.Definition,
            context.Environment, 7, context.Bindings, out var journal,
            out var diagnostic), diagnostic ?? "failure prepare failed");
    var actors = journal.Npcs.Select(entry => entry.Npc).ToArray();

    Assert(!journal.TryCommit() && !journal.IsCommitted
           && !journal.HasUnresolvedPublication,
        "out-of-map commit was not fully compensated");
    foreach (var actor in actors)
    {
        Assert(M2Share.ObjectManager.Get(actor.ObjectId) == null,
            "failed commit leaked ObjectManager actor");
        Equal(0, MovingReferenceCount(context.Environment, actor),
            "failed commit leaked map actor");
        Assert(!M2Share.UserEngine.ContainsRegisteredNpcExact(actor),
            "failed commit leaked registry actor");
    }
}

static void PreparedRollbackIsInvisible(string scriptRoot)
{
    var context = CreateContext(scriptRoot, "RollbackRoom", 1, 1);
    Assert(context.Materializer.TryPrepare(context.Definition,
            context.Environment, 7, context.Bindings, out var journal,
            out var diagnostic), diagnostic ?? "rollback prepare failed");
    var actors = journal.Npcs.Select(entry => entry.Npc).ToArray();
    Assert(journal.TryRollback() && journal.TryRollback(),
        "prepared rollback was not idempotent");
    foreach (var actor in actors)
    {
        Assert(M2Share.ObjectManager.Get(actor.ObjectId) == null
               && !M2Share.UserEngine.ContainsRegisteredNpcExact(actor),
            "prepared rollback published an actor");
        Equal(0, MovingReferenceCount(context.Environment, actor),
            "prepared rollback published a map actor");
    }
}

static TestContext CreateContext(string scriptRoot, string roomName,
    int x, int y)
{
    M2Share.ObjectManager = new ObjectManager();
    M2Share.UserEngine = new UserEngine();
    var configuredDefinition = new NativeDynamicRoomConfiguredNpcDefinition(
        "Guide", x, y, "RoomGuide", 4, 5, 2);
    var definition = new NativeDynamicRoomDefinition(roomName, "opaque", 1,
        roomName, "D000", "1", "1", Array.Empty<string>(),
        new[] { configuredDefinition }, 1);
    var environment = NewEnvironment(roomName);
    var manager = new NativeDynamicRoomManager();
    Assert(manager.RegisterIdleRoom(definition, 7, environment, 0,
            null, null, null), "room registration failed");

    var controllerPath = Path.GetFullPath(Path.Combine(scriptRoot,
        $"DNpc_{roomName}.pas"));
    var configuredPath = Path.GetFullPath(Path.Combine(scriptRoot,
        $"Guide-{roomName}.pas"));
    var bindings = new[]
    {
        new NativeDynamicRoomDynamicNpcScriptBinding(definition,
            NativeDynamicRoomDynamicNpcScriptRole.HiddenController, null,
            Path.GetFileName(controllerPath), controllerPath, false, 0,
            string.Empty),
        new NativeDynamicRoomDynamicNpcScriptBinding(definition,
            NativeDynamicRoomDynamicNpcScriptRole.ConfiguredVisible,
            configuredDefinition, Path.GetFileName(configuredPath),
            configuredPath, false, 0, string.Empty)
    };
    return new TestContext(definition, environment, bindings,
        new NativeDynamicRoomNpcMaterializer(M2Share.ObjectManager,
            M2Share.UserEngine));
}

static Envirnoment NewEnvironment(string mapName)
{
    var environment = new Envirnoment
    {
        sMapName = mapName,
        m_sMapFileName = mapName
    };
    typeof(Envirnoment).GetMethod("Initialize",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(environment, new object[] { (short)10, (short)10 });
    typeof(Envirnoment).GetMethod("ConfigureDormantDynamicRoom",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(environment, new object[] { mapName });
    return environment;
}

static Merchant CreateSameIdReplacement(Merchant original,
    Envirnoment environment)
{
    Assert(M2Share.ObjectManager.Remove(original.ObjectId, original),
        "original actor could not be displaced for ABA setup");
    var sequenceField = typeof(HUtil32).GetField("_sequence",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    var sequence = (long)sequenceField.GetValue(null)!;
    try
    {
        sequenceField.SetValue(null, (long)original.ObjectId - 1);
        return new Merchant
        {
            m_PEnvir = environment,
            m_sMapName = environment.sMapName,
            m_sCharName = "Replacement"
        };
    }
    finally
    {
        sequenceField.SetValue(null, sequence);
    }
}

static int MovingReferenceCount(Envirnoment environment, TBaseObject actor)
{
    var count = 0;
    for (var x = 0; x < environment.wWidth; x++)
    for (var y = 0; y < environment.wHeight; y++)
    {
        var found = false;
        var cell = environment.GetMapCellInfo(x, y, ref found);
        if (!found || cell.ObjList == null) continue;
        count += cell.ObjList.Count(entry =>
            entry.CellType == CellType.OS_MOVINGOBJECT
            && ReferenceEquals(entry.CellObj, actor));
    }
    return count;
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

internal sealed record TestContext(
    NativeDynamicRoomDefinition Definition,
    Envirnoment Environment,
    IReadOnlyList<NativeDynamicRoomDynamicNpcScriptBinding> Bindings,
    NativeDynamicRoomNpcMaterializer Materializer);
