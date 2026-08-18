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
M2Share.MapManager = new MapManager();
M2Share.nServerIndex = 0;

// MOVE-52 - the player space-move arm loads the internal idents as immediates;
// the non-player arm used by this monster fixture follows the native base-object
// path and publishes only its positional show message. Keep the constants here
// as a source-contract check, but do not require the player-only notifications
// from a non-player relocation:
//   006BD3AA  66 B9 85 27  mov cx, 0x2785   ; 10117 -> 006BD3B2 call 0x765E68
//   006BD3D3  66 B9 86 27  mov cx, 0x2786   ; 10118 -> 006BD3DB call 0x765F6C
Equal(10117, Grobal2.RM_NATIVE_CLEAROBJECTS, "0x6BD3AA mov cx,0x2785");
Equal(10118, Grobal2.RM_NATIVE_CHANGEMAP, "0x6BD3D3 mov cx,0x2786");

const string sharedMapName = "MasterRelocationShared";
var registeredEnvironment = NewEnvironment(sharedMapName,
    "registered-instance", 0);
var exactEnvironment = NewEnvironment(sharedMapName,
    "master-owned-instance", 0);
RegisterMap(M2Share.MapManager, registeredEnvironment);

var master = NewActor(exactEnvironment, Grobal2.RC_PLAYOBJECT, 5, 5);
master.m_btDirection = Grobal2.DR_RIGHT;
Place(exactEnvironment, master);
var source = NewEnvironment("MasterRelocationSource", "source-instance", 0);
var actor = NewActor(source, Grobal2.RC_MONSTER, 3, 3);
actor.m_boObMode = true;
Place(source, actor);

Assert(!NativeDynamicRoomMasterRelocation.TryRelocate(null),
    "null actor was accepted");
AssertRejected(actor, source, "missing master");

actor.m_Master = master;
master.m_SlaveList.Add(actor);
actor.m_boDeath = true;
AssertRejected(actor, source, "dead actor");
actor.m_boDeath = false;
actor.m_boGhost = true;
AssertRejected(actor, source, "ghost actor");
actor.m_boGhost = false;
master.m_boDeath = true;
AssertRejected(actor, source, "dead master");
master.m_boDeath = false;
master.m_boGhost = true;
AssertRejected(actor, source, "ghost master");
master.m_boGhost = false;
var savedMasterEnvironment = master.m_PEnvir;
master.m_PEnvir = null;
AssertRejected(actor, source, "master without environment");
master.m_PEnvir = savedMasterEnvironment;

var messageStart = actor.m_MsgList.Count;
Assert(NativeDynamicRoomMasterRelocation.TryRelocate(actor),
    "eligible master-owned actor was not relocated");
Assert(ReferenceEquals(exactEnvironment, actor.m_PEnvir),
    "relocation did not retain the master's exact environment reference");
Assert(!CellContains(registeredEnvironment, actor),
    "same-name registered environment received the actor");
Assert(CellContains(exactEnvironment, actor),
    "master's exact environment did not receive the actor");
Equal((short)6, actor.m_nCurrX, "master-front X");
Equal((short)5, actor.m_nCurrY, "master-front Y");
Equal(0, source.MonCount, "successful relocation source count");
Equal(1, exactEnvironment.MonCount,
    "successful relocation target monster count");
Equal(0, registeredEnvironment.MonCount,
    "same-name registered environment monster count");
Assert(ReferenceEquals(master, actor.m_Master)
       && master.m_SlaveList.Count(item => ReferenceEquals(item, actor)) == 1,
    "successful relocation changed master ownership");
AssertMessageSequence(actor, messageStart,
    Grobal2.RM_SPACEMOVE_SHOW2);

var blockedSource = NewEnvironment("MasterRelocationBlockedSource",
    "blocked-source", 0);
var blockedTarget = NewEnvironment("MasterRelocationBlockedTarget",
    "blocked-target", 0);
BlockAllCells(blockedTarget);
var blockedMaster = NewActor(blockedTarget, Grobal2.RC_PLAYOBJECT, 4, 4);
var blockedActor = NewActor(blockedSource, Grobal2.RC_MONSTER, 2, 2);
blockedActor.m_Master = blockedMaster;
Place(blockedSource, blockedActor);
var blockedMessages = blockedActor.m_MsgList.Count;
Assert(!NativeDynamicRoomMasterRelocation.TryRelocate(blockedActor),
    "all-blocked target reported success");
AssertUnchanged(blockedActor, blockedSource, 2, 2,
    blockedMessages, "all-blocked rollback");
Assert(!CellContains(blockedTarget, blockedActor),
    "all-blocked target retained the actor");

var remoteSource = NewEnvironment("MasterRelocationRemoteSource",
    "remote-source", 0);
var remoteTarget = NewEnvironment("MasterRelocationRemoteTarget",
    "remote-target", 1);
var remoteMaster = NewActor(remoteTarget, Grobal2.RC_PLAYOBJECT, 4, 4);
var remoteActor = NewActor(remoteSource, Grobal2.RC_MONSTER, 2, 2);
remoteActor.m_Master = remoteMaster;
Place(remoteSource, remoteActor);
var remoteMessages = remoteActor.m_MsgList.Count;
Assert(!NativeDynamicRoomMasterRelocation.TryRelocate(remoteActor),
    "remote target reported success");
AssertUnchanged(remoteActor, remoteSource, 2, 2,
    remoteMessages, "remote target rejection");

var detachedSource = NewEnvironment("MasterRelocationDetachedSource",
    "detached-source", 0);
var detachedTarget = NewEnvironment("MasterRelocationDetachedTarget",
    "detached-target", 0);
var detachedMaster = NewActor(detachedTarget, Grobal2.RC_PLAYOBJECT, 4, 4);
detachedMaster.m_btDirection = Grobal2.DR_RIGHT;
Place(detachedTarget, detachedMaster);
var detachedActor = NewActor(detachedSource, Grobal2.RC_MONSTER, 2, 2);
detachedActor.m_Master = detachedMaster;
var detachedMessages = detachedActor.m_MsgList.Count;
Assert(NativeDynamicRoomMasterRelocation.TryRelocate(detachedActor),
    "source-detached actor was not relocated");
Equal(detachedMessages, detachedActor.m_MsgList.Count,
    "source-detached relocation unexpectedly queued a private message");
Assert(ReferenceEquals(detachedTarget, detachedActor.m_PEnvir),
    "source-detached relocation retained the wrong environment identity");
Assert(CellContains(detachedTarget, detachedActor),
    "source-detached relocation did not register the target actor");

Console.WriteLine("DynRoomMasterRelocationCheck PASS "
    + "eligibility=closed exact-owner=ok front-cell=bounded transaction=rollback "
    + "messages=native-10117/10118");
return;

static void AssertRejected(TBaseObject actor, Envirnoment source, string label)
{
    var messages = actor.m_MsgList.Count;
    Assert(!NativeDynamicRoomMasterRelocation.TryRelocate(actor),
        label + " reported success");
    AssertUnchanged(actor, source, 3, 3, messages, label);
}

static void AssertUnchanged(TBaseObject actor, Envirnoment source,
    short x, short y, int messageCount, string label)
{
    Assert(ReferenceEquals(source, actor.m_PEnvir),
        label + " changed environment identity");
    Equal(x, actor.m_nCurrX, label + " changed X");
    Equal(y, actor.m_nCurrY, label + " changed Y");
    Assert(CellContains(source, actor), label + " detached the source actor");
    Equal(messageCount, actor.m_MsgList.Count,
        label + " queued movement messages");
}

static TBaseObject NewActor(Envirnoment environment, byte race,
    short x, short y) =>
    new()
    {
        m_PEnvir = environment,
        m_sCharName = $"audit-{environment.sMapName}-{race}-{x}-{y}",
        m_sMapName = environment.sMapName,
        m_sMapFileName = environment.m_sMapFileName,
        m_btRaceServer = race,
        m_nCurrX = x,
        m_nCurrY = y
    };

static Envirnoment NewEnvironment(string mapName, string mapFileName,
    int serverIndex)
{
    var environment = new Envirnoment
    {
        sMapName = mapName,
        m_sMapFileName = mapFileName,
        nServerIndex = serverIndex
    };
    typeof(Envirnoment).GetMethod("Initialize",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(environment, new object[] { (short)12, (short)12 });
    return environment;
}

static void Place(Envirnoment environment, TBaseObject actor)
{
    actor.m_boAddToMaped = false;
    actor.m_boDelFormMaped = false;
    Assert(ReferenceEquals(actor, environment.AddToMap(actor.m_nCurrX,
        actor.m_nCurrY, CellType.OS_MOVINGOBJECT, actor)), "place actor");
}

static void RegisterMap(MapManager manager, Envirnoment environment)
{
    var maps = (IDictionary<string, Envirnoment>)typeof(MapManager)
        .GetField("m_MapList", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(manager)!;
    maps.Add(environment.sMapName, environment);
}

static bool CellContains(Envirnoment environment, TBaseObject actor)
{
    for (var x = 0; x < environment.wWidth; x++)
    for (var y = 0; y < environment.wHeight; y++)
    {
        var found = false;
        var cell = environment.GetMapCellInfo(x, y, ref found);
        if (found && cell.ObjList != null && cell.ObjList.Any(item =>
                item.CellType == CellType.OS_MOVINGOBJECT
                && ReferenceEquals(item.CellObj, actor)))
            return true;
    }
    return false;
}

static void BlockAllCells(Envirnoment environment)
{
    for (var x = 0; x < environment.wWidth; x++)
    for (var y = 0; y < environment.wHeight; y++)
        environment.SetMapXYFlag(x, y, false);
}

static void AssertMessageSequence(TBaseObject actor, int start,
    params int[] expected)
{
    var actual = actor.m_MsgList.Skip(start)
        .Select(message => message.wIdent).ToArray();
    if (!actual.SequenceEqual(expected))
    {
        throw new InvalidOperationException(
            $"message order: expected {string.Join(',', expected)}, "
            + $"actual {string.Join(',', actual)}");
    }
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
