using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Text;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

PrepareRuntimeConfig();
ConstructionFailureLeavesNoSideEffects();
InitializeFailureRollsBackMapPublication();
OrdinaryFactoryInitializeFailureRollsBackMapPublication();
CertificateFailureRollsBackAllPublications();
ExactCoordinateMutationFailureUsesPublicationToken();
NormalSuccessPublishesExactlyOneMonsterCount();
ExactSpawnWithoutGeneratorBucketKeepsExistingBehavior();
OrdinaryFactorySuccessPublishesExactlyOneMonsterCount();
OnInitializeFailureKeepsCommittedMonster();
GeneratedMonsterSuccessPublishesOwnership();
GeneratedMonsterCertificateFailureRollsBackOwnership();
GeneratedMonsterCoordinateMutationUsesPublicationToken();
GeneratedMonsterReplacementSurvivesRollback();
GeneratedMonsterOnInitializeFailureKeepsCommittedOwnership();
AssertPostCommitSourceOrdering();
AssertExpectedOwnerRemovalSourceOrdering();

Console.WriteLine(
    "ExactEnvironmentMonsterSpawnTransactionCheck PASS exact=transactional generator=success+certificate-rollback+postcommit-oninitialize");
return;

static void ConstructionFailureLeavesNoSideEffects()
{
    var fixture = NewFixture();
    var sentinel = new TBaseObject();
    var sequenceField = typeof(HUtil32).GetField("_sequence",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    var originalSequence = (long)sequenceField.GetValue(null)!;
    var objectsBefore = ObjectCount();

    TBaseObject spawned;
    try
    {
        sequenceField.SetValue(null, (long)sentinel.ObjectId - 1);
        spawned = fixture.Engine.RegenMonsterByName(fixture.Environment, 3, 3,
            fixture.MonsterName);
    }
    finally
    {
        sequenceField.SetValue(null, originalSequence);
    }

    Assert(spawned == null, "constructor failure returned an actor");
    Equal(objectsBefore, ObjectCount(), "constructor failure object index");
    Assert(ReferenceEquals(sentinel,
            M2Share.ObjectManager.Get(sentinel.ObjectId)),
        "constructor failure replaced the existing object ID");
    AssertCleanMapAndCertificate(fixture, "constructor failure");
}

static void InitializeFailureRollsBackMapPublication()
{
    var fixture = NewFixture();
    var objectsBefore = ObjectCount();
    M2Share.g_MonSayMsgList = null;
    TBaseObject spawned;
    try
    {
        spawned = fixture.Engine.RegenMonsterByName(fixture.Environment, 4, 4,
            fixture.MonsterName);
    }
    finally
    {
        M2Share.g_MonSayMsgList = new Dictionary<string, IList<TMonSayMsg>>();
    }

    Assert(spawned == null, "Initialize failure returned an actor");
    Equal(objectsBefore, ObjectCount(), "Initialize failure object index");
    AssertCleanMapAndCertificate(fixture, "Initialize failure");
}

static void OrdinaryFactoryInitializeFailureRollsBackMapPublication()
{
    var fixture = NewFixture();
    var objectsBefore = ObjectCount();
    M2Share.g_MonSayMsgList = null;
    TBaseObject spawned;
    try
    {
        spawned = InvokeOrdinaryAddBaseObject(fixture, 4, 5);
    }
    finally
    {
        M2Share.g_MonSayMsgList = new Dictionary<string, IList<TMonSayMsg>>();
    }

    Assert(spawned == null,
        "ordinary AddBaseObject Initialize failure returned an actor");
    Equal(objectsBefore, ObjectCount(),
        "ordinary AddBaseObject Initialize failure object index");
    AssertCleanMapAndCertificate(fixture,
        "ordinary AddBaseObject Initialize failure");
}

static void CertificateFailureRollsBackAllPublications()
{
    var failingList = new MutatingThrowList();
    var fixture = NewFixture(failingList);
    var objectsBefore = ObjectCount();

    var spawned = fixture.Engine.RegenMonsterByName(fixture.Environment, 5, 5,
        fixture.MonsterName);

    Assert(spawned == null, "certificate failure returned an actor");
    Equal(objectsBefore, ObjectCount(), "certificate failure object index");
    Equal(0, failingList.Count,
        "certificate failure retained its post-mutation actor");
    AssertCleanMapAndCertificate(fixture, "certificate failure");
}

static void ExactCoordinateMutationFailureUsesPublicationToken()
{
    var failingList = new CoordinateMutatingThrowList();
    var fixture = NewFixture(failingList);
    var objectsBefore = ObjectCount();

    var spawned = fixture.Engine.RegenMonsterByName(fixture.Environment, 5, 5,
        fixture.MonsterName);

    Assert(spawned == null,
        "coordinate-mutating certificate failure returned an actor");
    var attempted = failingList.LastAttempted;
    Assert(attempted != null,
        "coordinate-mutating certificate failure did not publish a certificate");
    Equal(objectsBefore, ObjectCount(),
        "coordinate-mutating certificate failure object index");
    Equal(0, CellReferenceCount(fixture.Environment, attempted),
        "coordinate-mutating certificate failure retained the published cell");
    AssertCleanMapAndCertificate(fixture,
        "coordinate-mutating certificate failure");
}

static void NormalSuccessPublishesExactlyOneMonsterCount()
{
    var fixture = NewFixture();
    var objectsBefore = ObjectCount();

    var spawned = fixture.Engine.RegenMonsterByName(fixture.Environment, 5, 6,
        fixture.MonsterName);

    Assert(spawned != null, "normal spawn did not return an actor");
    Equal(objectsBefore + 1, ObjectCount(), "normal spawn object index");
    Equal(1, fixture.Environment.MonCount,
        "normal spawn published more than one monster count");
    Equal(1, fixture.Bucket.CertList.Count,
        "normal spawn certificate list");
    Equal(1, fixture.Bucket.CertCount, "normal spawn certificate count");
    Assert(CellContains(fixture.Environment, spawned),
        "normal spawn did not publish its map cell");
    Equal(1, CellReferenceCount(fixture.Environment, spawned),
        "normal spawn map-cell reference count");
    Equal(1, CellObjectCount(fixture.Environment),
        "normal spawn total map-cell count");
    Assert(spawned.m_boAddToMaped && !spawned.m_boDelFormMaped,
        "normal spawn map-registration flags are inconsistent");
}

static void ExactSpawnWithoutGeneratorBucketKeepsExistingBehavior()
{
    var fixture = NewFixture();
    fixture.Engine.m_MonGenList.Clear();
    var objectsBefore = ObjectCount();

    var spawned = fixture.Engine.RegenMonsterByName(fixture.Environment, 5, 6,
        fixture.MonsterName);

    Assert(spawned != null,
        "exact spawn without a generator bucket returned null");
    Equal(objectsBefore + 1, ObjectCount(),
        "exact spawn without a generator bucket object index");
    Equal(1, fixture.Environment.MonCount,
        "exact spawn without a generator bucket monster count");
    Equal(1, CellReferenceCount(fixture.Environment, spawned),
        "exact spawn without a generator bucket map-cell reference count");
    Equal(0, fixture.Bucket.CertList.Count,
        "detached bucket received a certificate");
    Equal(0, fixture.Bucket.CertCount,
        "detached bucket certificate count changed");
}

static void OrdinaryFactorySuccessPublishesExactlyOneMonsterCount()
{
    var fixture = NewFixture();
    var objectsBefore = ObjectCount();

    var spawned = InvokeOrdinaryAddBaseObject(fixture, 5, 7);

    Assert(spawned != null, "ordinary AddBaseObject did not return an actor");
    Equal(objectsBefore + 1, ObjectCount(),
        "ordinary AddBaseObject object index");
    Equal(1, fixture.Environment.MonCount,
        "ordinary AddBaseObject published more than one monster count");
    Equal(0, fixture.Bucket.CertList.Count,
        "ordinary AddBaseObject changed the script certificate list");
    Equal(0, fixture.Bucket.CertCount,
        "ordinary AddBaseObject changed the script certificate count");
    Assert(CellContains(fixture.Environment, spawned),
        "ordinary AddBaseObject did not publish its map cell");
    Equal(1, CellReferenceCount(fixture.Environment, spawned),
        "ordinary AddBaseObject map-cell reference count");
    Assert(spawned.m_boAddToMaped && !spawned.m_boDelFormMaped,
        "ordinary AddBaseObject map-registration flags are inconsistent");
}

static void OnInitializeFailureKeepsCommittedMonster()
{
    var fixture = NewFixture();
    var scriptRoot = Path.Combine(Path.GetTempPath(),
        "loym2-monster-spawn-transaction-" + Guid.NewGuid().ToString("N"));
    try
    {
        M2Share.PasEngine = CreateFailingMonsterHost(scriptRoot,
            fixture.MonsterName);
        var objectsBefore = ObjectCount();

        var spawned = fixture.Engine.RegenMonsterByName(fixture.Environment, 6,
            6, fixture.MonsterName);

        Assert(spawned != null,
            "OnInitialize failure invalidated a committed monster");
        Equal(objectsBefore + 1, ObjectCount(),
            "OnInitialize failure object index");
        Equal(1, fixture.Environment.MonCount,
            "OnInitialize failure monster count");
        Equal(1, fixture.Bucket.CertList.Count,
            "OnInitialize failure certificate list");
        Equal(1, fixture.Bucket.CertCount,
            "OnInitialize failure certificate count");
        Assert(ReferenceEquals(spawned, fixture.Bucket.CertList[0]),
            "OnInitialize did not preserve the committed certificate identity");
        Assert(CellContains(fixture.Environment, spawned),
            "OnInitialize failure removed the committed map cell");
        Equal(1, CellReferenceCount(fixture.Environment, spawned),
            "OnInitialize failure map-cell reference count");
        Assert(M2Share.PasEngine.TryInitializeMonsterScript(spawned),
            "post-commit OnInitialize was not recorded exactly once");
    }
    finally
    {
        M2Share.PasEngine = null;
        if (Directory.Exists(scriptRoot))
            Directory.Delete(scriptRoot, true);
    }
}

static void GeneratedMonsterSuccessPublishesOwnership()
{
    var fixture = NewFixture();
    var objectsBefore = ObjectCount();

    Assert(InvokeRegenMonsters(fixture, 1),
        "RegenMonsters success changed its return value");

    Equal(objectsBefore + 1, ObjectCount(),
        "RegenMonsters success object index");
    Equal(1, fixture.Environment.MonCount,
        "RegenMonsters success monster count");
    Equal(1, fixture.Bucket.nActiveCount,
        "RegenMonsters success active count");
    Equal(1, fixture.Bucket.CertList.Count,
        "RegenMonsters success certificate list");
    Equal(1, fixture.Bucket.CertCount,
        "RegenMonsters success certificate count");
    var spawned = fixture.Bucket.CertList[0];
    Assert(spawned.m_boCanReAlive,
        "RegenMonsters success did not enable resurrection");
    Assert(ReferenceEquals(fixture.Bucket, spawned.m_pMonGen),
        "RegenMonsters success did not publish generator ownership");
    Assert(CellContains(fixture.Environment, spawned),
        "RegenMonsters success did not publish its map cell");
    Equal(1, CellReferenceCount(fixture.Environment, spawned),
        "RegenMonsters success map-cell reference count");
    Equal(1, CellObjectCount(fixture.Environment),
        "RegenMonsters success total map-cell count");
}

static void GeneratedMonsterCertificateFailureRollsBackOwnership()
{
    var failingList = new MutatingThrowList();
    var fixture = NewFixture(failingList);
    fixture.Bucket.nActiveCount = 3;
    fixture.Bucket.CertCount = 4;
    var objectsBefore = ObjectCount();

    Assert(InvokeRegenMonsters(fixture, 1),
        "RegenMonsters certificate failure changed its return value");

    var attempted = failingList.LastAttempted;
    Assert(attempted != null,
        "RegenMonsters certificate failure did not reach list publication");
    Equal(objectsBefore, ObjectCount(),
        "RegenMonsters certificate failure object index");
    Equal(0, fixture.Environment.MonCount,
        "RegenMonsters certificate failure monster count");
    Equal(0, CellObjectCount(fixture.Environment),
        "RegenMonsters certificate failure map cells");
    Equal(3, fixture.Bucket.nActiveCount,
        "RegenMonsters certificate failure active count");
    Equal(1, failingList.Count,
        "RegenMonsters certificate failure lost its native slot");
    Assert(failingList[0] == null,
        "RegenMonsters certificate failure retained a certificate");
    Equal(4, fixture.Bucket.CertCount,
        "RegenMonsters certificate failure certificate count");
    Assert(!attempted.m_boCanReAlive && attempted.m_pMonGen == null,
        "RegenMonsters certificate failure retained actor ownership fields");
    Assert(M2Share.ObjectManager.Get(attempted.ObjectId) == null,
        "RegenMonsters certificate failure retained the actor index");
}

static void GeneratedMonsterCoordinateMutationUsesPublicationToken()
{
    var failingList = new CoordinateMutatingThrowList();
    var fixture = NewFixture(failingList);
    var objectsBefore = ObjectCount();

    Assert(InvokeRegenMonsters(fixture, 1),
        "coordinate-mutating RegenMonsters failure changed its return value");

    var attempted = failingList.LastAttempted;
    Assert(attempted != null,
        "coordinate-mutating RegenMonsters failure did not reach publication");
    Equal(objectsBefore, ObjectCount(),
        "coordinate-mutating RegenMonsters failure object index");
    Equal(0, fixture.Environment.MonCount,
        "coordinate-mutating RegenMonsters failure monster count");
    Equal(0, CellReferenceCount(fixture.Environment, attempted),
        "coordinate-mutating RegenMonsters failure retained the original cell");
    Equal(0, CellObjectCount(fixture.Environment),
        "coordinate-mutating RegenMonsters failure map cells");
    Equal(0, fixture.Bucket.nActiveCount,
        "coordinate-mutating RegenMonsters failure active count");
    Equal(0, fixture.Bucket.CertCount,
        "coordinate-mutating RegenMonsters failure certificate count");
    Equal(1, failingList.Count,
        "coordinate-mutating RegenMonsters failure lost its native slot");
    Assert(failingList[0] == null,
        "coordinate-mutating RegenMonsters failure retained a certificate");
    Assert(!attempted.m_boCanReAlive && attempted.m_pMonGen == null,
        "coordinate-mutating RegenMonsters failure retained ownership fields");
}

static void GeneratedMonsterReplacementSurvivesRollback()
{
    var failingList = new ReplacingThrowList();
    var fixture = NewFixture(failingList);
    var scriptRoot = Path.Combine(Path.GetTempPath(),
        "loym2-generator-replacement-" + Guid.NewGuid().ToString("N"));
    try
    {
        M2Share.PasEngine = CreateFailingMonsterHost(scriptRoot,
            fixture.MonsterName);
        var objectsBefore = ObjectCount();

        Assert(InvokeRegenMonsters(fixture, 1),
            "same-ID replacement failure changed RegenMonsters return value");

        var attempted = failingList.LastAttempted;
        var replacement = failingList.Replacement;
        Assert(attempted != null && replacement != null,
            "same-ID replacement failure did not complete its test mutation");
        Equal(attempted.ObjectId, replacement.ObjectId,
            "replacement did not reuse the attempted actor ID");
        Equal(objectsBefore + 1, ObjectCount(),
            "same-ID replacement object index");
        Assert(ReferenceEquals(replacement,
                M2Share.ObjectManager.Get(attempted.ObjectId)),
            "rollback removed the same-ID replacement actor");
        Assert(MonsterScriptStateContains(M2Share.PasEngine,
                attempted.ObjectId),
            "rollback cleared PAS state owned by the same-ID replacement");
        Equal(0, fixture.Environment.MonCount,
            "same-ID replacement rollback monster count");
        Equal(0, CellReferenceCount(fixture.Environment, attempted),
            "same-ID replacement rollback retained the original map cell");
        Equal(0, fixture.Bucket.nActiveCount,
            "same-ID replacement rollback active count");
        Equal(0, fixture.Bucket.CertCount,
            "same-ID replacement rollback certificate count");
        Equal(1, failingList.Count,
            "same-ID replacement rollback lost its native slot");
        Assert(failingList[0] == null,
            "same-ID replacement rollback retained a certificate");
        Assert(!attempted.m_boCanReAlive && attempted.m_pMonGen == null,
            "same-ID replacement rollback retained original ownership fields");
    }
    finally
    {
        M2Share.PasEngine = null;
        if (Directory.Exists(scriptRoot))
            Directory.Delete(scriptRoot, true);
    }
}

static void GeneratedMonsterOnInitializeFailureKeepsCommittedOwnership()
{
    var fixture = NewFixture();
    var scriptRoot = Path.Combine(Path.GetTempPath(),
        "loym2-generator-transaction-" + Guid.NewGuid().ToString("N"));
    try
    {
        M2Share.PasEngine = CreateFailingMonsterHost(scriptRoot,
            fixture.MonsterName);
        var objectsBefore = ObjectCount();

        Assert(InvokeRegenMonsters(fixture, 1),
            "RegenMonsters OnInitialize failure changed its return value");

        Equal(objectsBefore + 1, ObjectCount(),
            "RegenMonsters OnInitialize failure object index");
        Equal(1, fixture.Environment.MonCount,
            "RegenMonsters OnInitialize failure monster count");
        Equal(1, fixture.Bucket.nActiveCount,
            "RegenMonsters OnInitialize failure active count");
        Equal(1, fixture.Bucket.CertList.Count,
            "RegenMonsters OnInitialize failure certificate list");
        Equal(1, fixture.Bucket.CertCount,
            "RegenMonsters OnInitialize failure certificate count");
        var spawned = fixture.Bucket.CertList[0];
        Assert(ReferenceEquals(spawned, fixture.Bucket.CertList.Single()),
            "RegenMonsters OnInitialize did not preserve certificate identity");
        Assert(spawned.m_boCanReAlive &&
               ReferenceEquals(fixture.Bucket, spawned.m_pMonGen),
            "RegenMonsters OnInitialize failure lost generator ownership");
        Assert(CellContains(fixture.Environment, spawned),
            "RegenMonsters OnInitialize failure removed the map cell");
        Equal(1, CellReferenceCount(fixture.Environment, spawned),
            "RegenMonsters OnInitialize failure map-cell reference count");
        Assert(M2Share.PasEngine.TryInitializeMonsterScript(spawned),
            "generator OnInitialize was not marked initialized exactly once");
    }
    finally
    {
        M2Share.PasEngine = null;
        if (Directory.Exists(scriptRoot))
            Directory.Delete(scriptRoot, true);
    }
}

static void AssertPostCommitSourceOrdering()
{
    var root = FindRepositoryRoot();
    var source = File.ReadAllText(Path.Combine(root, "GameSvr", "UsrSystem",
        "UsrEngn.cs"));
    var methodStart = source.IndexOf(
        "public TBaseObject RegenMonsterByName(Envirnoment environment",
        StringComparison.Ordinal);
    var methodEnd = source.IndexOf("public void Run()", methodStart,
        StringComparison.Ordinal);
    Assert(methodStart >= 0 && methodEnd > methodStart,
        "exact-environment spawn method source was not found");
    var method = source.Substring(methodStart, methodEnd - methodStart);
    var factory = method.IndexOf("AddBaseObject(environment",
        StringComparison.Ordinal);
    var factoryEnd = method.IndexOf("if (baseObject == null)",
        StringComparison.Ordinal);
    var deferredInitialization = method.IndexOf("false);",
        factory, StringComparison.Ordinal);
    var mapPublication = method.IndexOf(
        "CaptureMapPublication(baseObject)", StringComparison.Ordinal);
    var certificate = method.IndexOf(
        "certificateOwner.CertList.Add(baseObject)", StringComparison.Ordinal);
    var certificateCount = method.IndexOf(
        "certificateOwner.CertCount = certificateCountBefore + 1",
        StringComparison.Ordinal);
    var commit = method.IndexOf("committed = true;", StringComparison.Ordinal);
    var initialize = method.IndexOf(
        "TryInitializeMonsterScript(baseObject)", StringComparison.Ordinal);
    var tokenRollback = method.IndexOf(
        "RollbackUnpublishedMonster(baseObject, mapPublication)",
        StringComparison.Ordinal);
    Assert(factory >= 0 && deferredInitialization > factory &&
           deferredInitialization < factoryEnd,
        "exact-environment spawn did not defer factory script initialization");
    Assert(mapPublication > factoryEnd && certificate > mapPublication,
        "exact-environment spawn did not capture its map publication before callbacks");
    Assert(certificateCount > certificate && commit > certificateCount &&
           initialize > commit,
        "exact-environment OnInitialize is not after certificate commit");
    Assert(tokenRollback > certificate,
        "exact-environment certificate rollback does not use its map token");

    var generatorStart = source.IndexOf(
        "private TBaseObject CreateGeneratedMonster", StringComparison.Ordinal);
    var generatorEnd = source.IndexOf("private bool RegenMonsters",
        generatorStart, StringComparison.Ordinal);
    Assert(generatorStart >= 0 && generatorEnd > generatorStart,
        "generated-monster transaction helper source was not found");
    var generator = source.Substring(generatorStart,
        generatorEnd - generatorStart);
    var generatorFactory = generator.IndexOf("AddBaseObject(monGen.sMapName",
        StringComparison.Ordinal);
    var generatorFactoryEnd = generator.IndexOf("if (cert == null)",
        StringComparison.Ordinal);
    var generatorDeferred = generator.IndexOf("false);", generatorFactory,
        StringComparison.Ordinal);
    var generatorMapPublication = generator.IndexOf(
        "CaptureMapPublication(cert)", StringComparison.Ordinal);
    var generatorCertificate = generator.IndexOf("monGen.CertList.Add(cert)",
        StringComparison.Ordinal);
    var generatorCertificateCount = generator.IndexOf(
        "monGen.CertCount = certificateCountBefore + 1",
        StringComparison.Ordinal);
    var generatorInitialize = generator.IndexOf(
        "TryInitializeMonsterScript(cert)", StringComparison.Ordinal);
    var generatorTokenRollback = generator.IndexOf(
        "RollbackUnpublishedMonster(cert, mapPublication)",
        StringComparison.Ordinal);
    Assert(generatorFactory >= 0 && generatorDeferred > generatorFactory &&
           generatorDeferred < generatorFactoryEnd,
        "generated-monster factory did not defer script initialization");
    Assert(generatorMapPublication > generatorFactoryEnd &&
           generatorCertificate > generatorMapPublication,
        "generated-monster transaction did not capture its map publication before callbacks");
    Assert(generatorCertificateCount > generatorCertificate &&
           generatorInitialize > generatorCertificateCount,
        "generated-monster OnInitialize is not after ownership commit");
    Assert(generatorTokenRollback > generatorCertificate,
        "generated-monster rollback does not use its map token");
}

static void AssertExpectedOwnerRemovalSourceOrdering()
{
    var root = FindRepositoryRoot();
    var objectManager = File.ReadAllText(Path.Combine(root, "GameSvr",
        "UsrSystem", "ObjectManager.cs"));
    var overloadStart = objectManager.IndexOf(
        "public bool Remove(int actorId, TBaseObject expectedActor)",
        StringComparison.Ordinal);
    // End marker used to be "public void ClearObject()". That method was the non-native
    // global id->object ghost sweep and was DELETED (2026-08-03): 战神 has no such scan —
    // detection lives in the per-type ProcessMon loops (sub_67C150 loop2 @0x67C46F /
    // loop3 @0x67C614) and the free is one central FIFO drained at 0x67C1BD
    // `cmp eax,0x493E0` (300000ms). The marker is now the comment that replaced it.
    var overloadEnd = objectManager.IndexOf("// 已删除非原生的全局 ghost 扫描 ClearObject()",
        overloadStart, StringComparison.Ordinal);
    Assert(overloadStart >= 0 && overloadEnd > overloadStart,
        "expected-owner ObjectManager.Remove overload was not found");
    var overload = objectManager.Substring(overloadStart,
        overloadEnd - overloadStart);
    var conditionalRemove = overload.IndexOf(
        "ICollection<KeyValuePair<int, TBaseObject>>", StringComparison.Ordinal);
    var removalGuard = overload.IndexOf("if (!removed) return false;",
        StringComparison.Ordinal);
    var cancelDeferred = overload.IndexOf("CancelDeferredCallsForObject",
        StringComparison.Ordinal);
    var clearState = overload.IndexOf("ClearMonsterScriptState",
        StringComparison.Ordinal);
    Assert(conditionalRemove >= 0 && removalGuard > conditionalRemove &&
           cancelDeferred > removalGuard && clearState > removalGuard,
        "expected-owner removal clears PAS state before a successful conditional remove");

    // The non-native global ghost sweep must stay gone, and the three per-type reap points
    // that it used to backstop must each route through the identity-checked Remove overload
    // (which is what preserves the PAS cancellations == native vmt+0x7C).
    Assert(!objectManager.Contains("public void ClearObject()",
            StringComparison.Ordinal),
        "the non-native global ghost sweep ObjectManager.ClearObject() was reintroduced");

    var userEngine = File.ReadAllText(Path.Combine(root, "GameSvr",
        "UsrSystem", "UsrEngn.cs"));
    Assert(userEngine.Contains(
            "M2Share.ObjectManager?.Remove(baseObject.ObjectId, baseObject);",
            StringComparison.Ordinal),
        "monster spawn rollback does not use expected-owner removal");
    Assert(userEngine.Contains(
            "M2Share.ObjectManager.Remove(ghostMonster.ObjectId, ghostMonster);",
            StringComparison.Ordinal),
        "MonGen ghost reap (UsrEngn ProcessMonsters, native sub_67C150 loop2) does not "
        + "remove the actor from the global registry -- every dead monster would leak");
    Assert(userEngine.Contains(
            "M2Share.ObjectManager.Remove(NPC.ObjectId, NPC);",
            StringComparison.Ordinal),
        "quest-NPC ghost reap (UsrEngn ProcessNpcs, native sub_67C150 loop3) does not "
        + "remove the actor from the global registry");
    Assert(userEngine.Contains(
            "M2Share.ObjectManager.Remove(merchantNpc.ObjectId, merchantNpc);",
            StringComparison.Ordinal),
        "merchant ghost reap (UsrEngn ProcessMerchants, native sub_67C150 loop3) does not "
        + "remove the actor from the global registry");
    // Native's deferred-free FIFO drains at 0x493E0 == 300000ms == 5*60*1000.
    // The production port keeps the value in a named constant rather than
    // repeating the arithmetic at the comparison site.
    Assert(userEngine.Contains(
            "private const int NativeMonFreeDelay = 5 * 60 * 1000",
            StringComparison.Ordinal)
           && userEngine.Contains("<= NativeMonFreeDelay",
               StringComparison.Ordinal),
        "the MonGen ghost reap no longer uses the native 5-minute (0x493E0) timeout");

    var gameServer = File.ReadAllText(Path.Combine(root, "GameSvr",
        "GameServer.cs"));
    Assert(!gameServer.Contains("ObjectManager.ClearObject();",
            StringComparison.Ordinal),
        "GameServer still invokes the deleted non-native global ghost sweep");
}

static Fixture NewFixture(IList<TBaseObject> certificateList = null)
{
    M2Share.g_Config = new GameSvrConfig { boMonSayMsg = false };
    M2Share.ObjectManager = new ObjectManager();
    M2Share.MapManager = new MapManager();
    M2Share.UserEngine = new UserEngine();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.g_MonSayMsgList = new Dictionary<string, IList<TMonSayMsg>>();
    M2Share.PasEngine = null;
    M2Share.g_dwZenLimit = 10000;

    const string monsterName = "TransactionOma";
    var environment = NewEnvironment("TransactionMap", "transaction-instance");
    RegisterMap(M2Share.MapManager, environment);
    M2Share.UserEngine.MonsterList.Add(NewMonsterInfo(monsterName));
    var bucket = new MonGenInfo
    {
        sMapName = environment.sMapName,
        sMonName = monsterName,
        nX = 6,
        nY = 6,
        nRange = 0,
        // nCount doubles as the certificate-list capacity (native [gen+0x24] is both
        // the target population and the length of the slot array at [gen+0x3C]).  The
        // fixture left it at 0, so the capacity test rejected every spawn before it
        // reached the transaction under test and the success case could never publish.
        nCount = 1,
        nRace = M2Share.MONSTER_OMA,
        nMissionGenRate = 0,
        Envir = environment,
        CertList = certificateList ?? new List<TBaseObject>()
    };
    M2Share.UserEngine.m_MonGenList.Add(bucket);
    return new Fixture(M2Share.UserEngine, environment, bucket, monsterName);
}

static TBaseObject InvokeOrdinaryAddBaseObject(Fixture fixture, short x, short y)
{
    // exactPosition is LIVE (NOT dead code): the magic-tower runtime spawns at
    // UsrEngn.cs:1979/2164 pass exactPosition=true (exact-tile-or-fail placement).
    // This audit exercises the ORDINARY path with exactPosition=false (search-and-nudge,
    // identical to RegenMonsterByName) — its contract is transaction rollback, unchanged.
    // The exactPosition=true semantic (does 战神's magic-tower spawn place at exact coord,
    // no nudge?) is pending Tier-1 confirmation — see staging/idat_batch_queue_20260803.md.
    // The overload grew a trailing ignoreCellBlockers flag; reflection does not apply
    // optional-parameter defaults, so the old 7-type lookup found nothing and the whole
    // audit died on MissingMethodException. Pass ignoreCellBlockers=false, which is the
    // default and keeps this the ordinary search-and-nudge path described above.
    var method = typeof(UserEngine).GetMethod("AddBaseObject",
        BindingFlags.Instance | BindingFlags.NonPublic, null,
        new[]
        {
            typeof(Envirnoment), typeof(short), typeof(short), typeof(int),
            typeof(string), typeof(bool), typeof(bool), typeof(bool)
        }, null) ?? throw new MissingMethodException("AddBaseObject");
    return (TBaseObject)method.Invoke(fixture.Engine, new object[]
    {
        fixture.Environment, x, y, M2Share.MONSTER_OMA, fixture.MonsterName,
        true, false, false
    });
}

static bool InvokeRegenMonsters(Fixture fixture, int count)
{
    var method = typeof(UserEngine).GetMethod("RegenMonsters",
        BindingFlags.Instance | BindingFlags.NonPublic, null,
        new[] { typeof(MonGenInfo), typeof(int) }, null)
        ?? throw new MissingMethodException("RegenMonsters");
    return (bool)method.Invoke(fixture.Engine,
        new object[] { fixture.Bucket, count })!;
}

static void RegisterMap(MapManager manager, Envirnoment environment)
{
    var field = typeof(MapManager).GetField("m_MapList",
        BindingFlags.Instance | BindingFlags.NonPublic)!;
    var maps = (IDictionary<string, Envirnoment>)field.GetValue(manager)!;
    maps.Add(environment.sMapName, environment);
}

static PasScriptHost CreateFailingMonsterHost(string root, string monsterName)
{
    var envirPath = Directory.CreateDirectory(Path.Combine(root, "Envir"))
        .FullName;
    var scripts = Directory.CreateDirectory(Path.Combine(envirPath, "MonScript"))
        .FullName;
    File.WriteAllText(Path.Combine(envirPath, "monScript.txt"),
        monsterName + Environment.NewLine, new UTF8Encoding(false));
    File.WriteAllText(Path.Combine(scripts, monsterName + ".pas"), """
        program TransactionMonster;

        procedure OnInitialize;
        begin
          raise 'expected OnInitialize failure';
        end;

        begin
        end.
        """, new UTF8Encoding(false));
    return new PasScriptHost(envirPath);
}

static Envirnoment NewEnvironment(string mapName, string mapFileName)
{
    var environment = new Envirnoment
    {
        sMapName = mapName,
        m_sMapFileName = mapFileName
    };
    typeof(Envirnoment).GetMethod("Initialize",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(environment, new object[] { (short)12, (short)12 });
    return environment;
}

static TMonInfo NewMonsterInfo(string name) => new()
{
    ItemList = new List<TMonItem>(),
    sName = name,
    btRace = (byte)M2Share.MONSTER_OMA,
    wLevel = 1,
    wHP = 100,
    wWalkSpeed = 1000,
    wWalkStep = 1,
    wWalkWait = 1000,
    wAttackSpeed = 1000
};

static void AssertCleanMapAndCertificate(Fixture fixture, string stage)
{
    Equal(0, fixture.Environment.MonCount, stage + " monster count");
    Equal(0, CellObjectCount(fixture.Environment), stage + " map cells");
    Equal(0, fixture.Bucket.CertList.Count, stage + " certificate list");
    Equal(0, fixture.Bucket.CertCount, stage + " certificate count");
}

static bool CellContains(Envirnoment environment, TBaseObject actor)
{
    var found = false;
    var cell = environment.GetMapCellInfo(actor.m_nCurrX, actor.m_nCurrY,
        ref found);
    return found && cell.ObjList != null && cell.ObjList.Any(item =>
        item.CellType == CellType.OS_MOVINGOBJECT
        && ReferenceEquals(item.CellObj, actor));
}

static int CellObjectCount(Envirnoment environment)
{
    var count = 0;
    for (var x = 0; x < environment.wWidth; x++)
    for (var y = 0; y < environment.wHeight; y++)
    {
        var found = false;
        count += environment.GetMapCellInfo(x, y, ref found).Count;
    }
    return count;
}

static int CellReferenceCount(Envirnoment environment, TBaseObject actor)
{
    var count = 0;
    for (var x = 0; x < environment.wWidth; x++)
    for (var y = 0; y < environment.wHeight; y++)
    {
        var found = false;
        var cell = environment.GetMapCellInfo(x, y, ref found);
        if (!found || cell.ObjList == null) continue;
        count += cell.ObjList.Count(item =>
            item.CellType == CellType.OS_MOVINGOBJECT &&
            ReferenceEquals(item.CellObj, actor));
    }
    return count;
}

static int ObjectCount()
{
    var actors = typeof(ObjectManager).GetField("_actors",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(M2Share.ObjectManager)!;
    return (int)actors.GetType().GetProperty("Count")!.GetValue(actors)!;
}

static bool MonsterScriptStateContains(PasScriptHost host, int objectId)
{
    var states = typeof(PasScriptHost).GetField("_monsterStates",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(host)!;
    return (bool)states.GetType().GetMethod("ContainsKey")!
        .Invoke(states, new object[] { objectId })!;
}

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory != null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "GameSvr", "UsrSystem",
                "UsrEngn.cs")))
            return directory.FullName;
        directory = directory.Parent;
    }
    throw new DirectoryNotFoundException("repository root was not found");
}

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
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

sealed record Fixture(UserEngine Engine, Envirnoment Environment,
    MonGenInfo Bucket, string MonsterName);

sealed class MutatingThrowList : Collection<TBaseObject>
{
    public TBaseObject LastAttempted { get; private set; }

    protected override void InsertItem(int index, TBaseObject item)
    {
        LastAttempted = item;
        base.InsertItem(index, item);
        throw new InvalidOperationException("expected certificate publication failure");
    }
}

sealed class CoordinateMutatingThrowList : Collection<TBaseObject>
{
    public TBaseObject LastAttempted { get; private set; }

    protected override void InsertItem(int index, TBaseObject item)
    {
        LastAttempted = item;
        base.InsertItem(index, item);
        item.m_nCurrX += 2;
        item.m_nCurrY += 2;
        item.m_boAddToMaped = false;
        item.m_boDelFormMaped = true;
        item.m_btRaceServer = 0;
        throw new InvalidOperationException(
            "expected coordinate-mutating certificate failure");
    }
}

sealed class ReplacingThrowList : Collection<TBaseObject>
{
    public TBaseObject LastAttempted { get; private set; }
    public TBaseObject Replacement { get; private set; }

    protected override void InsertItem(int index, TBaseObject item)
    {
        LastAttempted = item;
        base.InsertItem(index, item);

        var actors = (ConcurrentDictionary<int, TBaseObject>)typeof(ObjectManager)
            .GetField("_actors", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(M2Share.ObjectManager)!;
        if (!actors.TryRemove(item.ObjectId, out var removed) ||
            !ReferenceEquals(removed, item))
            throw new InvalidOperationException(
                "could not replace the attempted actor index");

        var sequenceField = typeof(HUtil32).GetField("_sequence",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var originalSequence = (long)sequenceField.GetValue(null)!;
        try
        {
            sequenceField.SetValue(null, (long)item.ObjectId - 1);
            Replacement = new TBaseObject
            {
                m_sCharName = item.m_sCharName
            };
        }
        finally
        {
            sequenceField.SetValue(null, originalSequence);
        }
        if (Replacement.ObjectId != item.ObjectId ||
            !ReferenceEquals(actors[item.ObjectId], Replacement))
            throw new InvalidOperationException(
                "could not publish the true same-ID replacement actor");

        M2Share.PasEngine?.TryInitializeMonsterScript(Replacement);

        throw new InvalidOperationException(
            "expected same-ID replacement certificate failure");
    }
}
