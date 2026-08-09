using System.Collections;
using System.Reflection;
using GameSvr;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.CastleManager = new CastleManager();
M2Share.ObjectManager = new ObjectManager();
M2Share.LogSystem = new MirLog();
M2Share.ProcessMsgCriticalSection = new object();
M2Share.LogMsgCriticalSection = new object();
M2Share.LogStringList = new ArrayList();
M2Share.g_MonSayMsgList = new Dictionary<string, IList<TMonSayMsg>>();

var exactRemove = typeof(Envirnoment).GetMethod(
    "RemoveMovingObjectEverywhereExact",
    BindingFlags.Instance | BindingFlags.NonPublic, null,
    new[] { typeof(TBaseObject), typeof(bool) }, null)!;

MonsterRemovalDoesNotTrustCoordinatesOrObjectId(exactRemove);
PlayerRemovalSettlesPresenceExactlyOnce(exactRemove);

Console.WriteLine(
    "ExactEnvironmentRemoveEverywhereCheck PASS exact-reference=all-cells counts=once presence=once");
return;

static void MonsterRemovalDoesNotTrustCoordinatesOrObjectId(
    MethodInfo exactRemove)
{
    var environment = NewEnvironment("ExactRemoveMonster");
    var actor = NewActor(environment, Grobal2.RC_ANIMAL, 2, 2);
    Place(environment, actor);
    AddDuplicate(environment, actor, 2, 2);
    AddDuplicate(environment, actor, 4, 4);

    Equal(3, MovingReferenceCount(environment, actor),
        "monster duplicate setup");
    Equal(1, environment.MonCount,
        "monster duplicate setup registration count");

    var replacement = CreateSameIdReplacement(actor, environment, 6, 6);
    Place(environment, replacement);
    Equal(actor.ObjectId, replacement.ObjectId,
        "replacement ObjectId setup");
    Equal(2, environment.MonCount,
        "same-ID replacement setup monster count");

    actor.m_nCurrX = 9;
    actor.m_nCurrY = 9;
    var removed = InvokeExactRemove(exactRemove, environment, actor);

    Equal(3, removed, "removed monster cell references");
    Equal(0, MovingReferenceCount(environment, actor),
        "removed actor remained in a cell");
    Equal(1, MovingReferenceCount(environment, replacement),
        "same-ID replacement was removed");
    Assert(ReferenceEquals(replacement,
            M2Share.ObjectManager.Get(actor.ObjectId)),
        "same-ID replacement lost object-index ownership");
    Equal(1, environment.MonCount,
        "monster registration was not settled exactly once");
    Assert(!actor.m_boAddToMaped && actor.m_boDelFormMaped,
        "removed monster registration flags");
    Assert(replacement.m_boAddToMaped && !replacement.m_boDelFormMaped,
        "same-ID replacement registration flags changed");

    Equal(0, InvokeExactRemove(exactRemove, environment, actor),
        "idempotent monster removal result");
    Equal(1, environment.MonCount,
        "repeated monster removal underflowed the count");
    Equal(1, MovingReferenceCount(environment, replacement),
        "repeated removal changed the same-ID replacement");
    Assert(ReferenceEquals(replacement,
            M2Share.ObjectManager.Get(actor.ObjectId)),
        "repeated removal changed same-ID object-index ownership");
}

static void PlayerRemovalSettlesPresenceExactlyOnce(MethodInfo exactRemove)
{
    var environment = NewEnvironment("ExactRemovePlayer", true);
    var player = NewActor(environment, Grobal2.RC_PLAYOBJECT, 1, 1);
    Place(environment, player);
    AddDuplicate(environment, player, 1, 1);
    AddDuplicate(environment, player, 3, 5);

    Equal(3, MovingReferenceCount(environment, player),
        "player duplicate setup");
    Equal(1, environment.HumCount, "player human-count setup");
    Equal(1, environment.DynamicRoomPlayerCount,
        "player presence setup");

    player.m_nCurrX = 8;
    player.m_nCurrY = 8;
    Equal(3, InvokeExactRemove(exactRemove, environment, player),
        "removed player cell references");
    Equal(0, MovingReferenceCount(environment, player),
        "removed player remained in a cell");
    Equal(0, environment.HumCount,
        "player human count was not settled once");
    Equal(0, environment.DynamicRoomPlayerCount,
        "dynamic-room player presence was not removed");
    Assert(!player.m_boAddToMaped && player.m_boDelFormMaped,
        "removed player registration flags");

    Equal(0, InvokeExactRemove(exactRemove, environment, player),
        "idempotent player removal result");
    Equal(0, environment.HumCount,
        "repeated player removal underflowed the human count");
    Equal(0, environment.DynamicRoomPlayerCount,
        "repeated player removal changed dynamic-room presence");
}

static TBaseObject CreateSameIdReplacement(TBaseObject actor,
    Envirnoment environment, short x, short y)
{
    Assert(M2Share.ObjectManager.Remove(actor.ObjectId, actor),
        "could not remove the original actor from the object index");

    var sequenceField = typeof(HUtil32).GetField("_sequence",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    var originalSequence = (long)sequenceField.GetValue(null)!;
    try
    {
        sequenceField.SetValue(null, (long)actor.ObjectId - 1);
        return NewActor(environment, Grobal2.RC_ANIMAL, x, y);
    }
    finally
    {
        sequenceField.SetValue(null, originalSequence);
    }
}

static int InvokeExactRemove(MethodInfo exactRemove,
    Envirnoment environment, TBaseObject actor) =>
    (int)exactRemove.Invoke(environment, new object[] { actor, true })!;

static Envirnoment NewEnvironment(string mapName, bool dynamicRoom = false)
{
    var environment = new Envirnoment
    {
        sMapName = mapName,
        m_sMapFileName = mapName
    };
    typeof(Envirnoment).GetMethod("Initialize",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(environment, new object[] { (short)10, (short)10 });
    if (dynamicRoom)
        typeof(Envirnoment).GetMethod("ConfigureDormantDynamicRoom",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(environment, new object[] { mapName });
    return environment;
}

static TBaseObject NewActor(Envirnoment environment, byte race,
    short x, short y) =>
    new()
    {
        m_PEnvir = environment,
        m_sMapName = environment.sMapName,
        m_sMapFileName = environment.m_sMapFileName,
        m_btRaceServer = race,
        m_nCurrX = x,
        m_nCurrY = y
    };

static void Place(Envirnoment environment, TBaseObject actor)
{
    actor.m_boAddToMaped = false;
    actor.m_boDelFormMaped = false;
    Assert(ReferenceEquals(actor, environment.AddToMap(actor.m_nCurrX,
        actor.m_nCurrY, CellType.OS_MOVINGOBJECT, actor)),
        "place actor");
}

static void AddDuplicate(Envirnoment environment, TBaseObject actor,
    int x, int y)
{
    Assert(ReferenceEquals(actor, environment.AddToMap(x, y,
        CellType.OS_MOVINGOBJECT, actor)), "add duplicate actor cell");
}

static int MovingReferenceCount(Envirnoment environment,
    TBaseObject actor)
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

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{label}: expected {expected}, actual {actual}");
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
