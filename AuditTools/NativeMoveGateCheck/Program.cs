// Native walk/run/heading/occupancy/teleport-cooldown contracts.
//
// Walk/run handlers (0x6D9BD0 / 0x6D9CE4 / 0x6D9D99) contain no GetTickCount
// compare and no 600 ms immediate. +0x6C is written on arrival only
// (0x6BBD50 / 0x6BC097 / 0x6BC1AF) and is read by motaebo's unsigned 500 ms
// jbe, not by the walk/run path. C#'s dwWalkIntervalTime/dwRunIntervalTime
// 600 ms gate is MOVE-20 INVENTED and is deliberately not asserted as native.
//
// Heading: walk/run call sub_764A90 (sign buckets). Skill 68 calls
// sub_764BC4 (ratio). Mixing them flattens off-axis headings.
// Occupancy: walk/run use MoveToMovingObject / IsNativeCellBlocking.
// GetMovObjCount (0x778858) is only on skill 168/266, then mover 6A 01.

using System.Reflection;
using GameSvr;
using SystemModule;

PrepareRuntimeConfig();
InitializeRuntime();

CheckSignHeadingIsSub764A90();
CheckRatioHeadingDisagreesOnOffAxis();
CheckMotaeboAndUserMoveUnsignedWrap();
CheckGetMovObjCountCountsObModeWalkDoesNot();
CheckWalkRunFailDoesNotKick();

Console.WriteLine(
    "NativeMoveGateCheck PASS " +
    "heading=764A90-sign/764BC4-ratio-disagree " +
    "wrap=motaebo-500/usermove-10000-unsigned " +
    "occupancy=GetMovObjCount-counts-ObMode " +
    "overspeed=walk/run-no-kick");

static void CheckSignHeadingIsSub764A90()
{
    Equal(Grobal2.DR_UP, M2Share.GetNextDirection(5, 5, 5, 5),
        "same cell: 0x764BB6 xor eax,eax = DR_UP");
    Equal(Grobal2.DR_UP, M2Share.GetNextDirection(5, 5, 5, 4),
        "due north");
    Equal(Grobal2.DR_RIGHT, M2Share.GetNextDirection(5, 5, 6, 5),
        "due east");
    Equal(Grobal2.DR_DOWNRIGHT, M2Share.GetNextDirection(5, 5, 6, 6),
        "south-east diagonal");

    // MOVE-47: Y suppressor upper bound is `jge` (strict < dy+1).
    // sx far, sy == dy+1 must KEEP flagy, not flatten to horizontal.
    Equal(Grobal2.DR_UPRIGHT, M2Share.GetNextDirection(0, 2, 10, 1),
        "Y suppressor must not flatten sy==dy+1 into DR_RIGHT");
}

static void CheckRatioHeadingDisagreesOnOffAxis()
{
    Equal(Grobal2.DR_UP, M2Share.GetNextDirectionByRatio(5, 5, 5, 5),
        "ratio same cell: 0x764BDC xor eax,eax = DR_UP");

    // deltaX=10, deltaY=sy-ty=0-1=-1 → ratio ≈ -0.01, dx>0 → DR_RIGHT.
    // Sign helper on the same points is DR_DOWNRIGHT.
    Equal(Grobal2.DR_RIGHT, M2Share.GetNextDirectionByRatio(0, 0, 10, 1),
        "ratio shallow east: 0x764C40 jbe skip / mov al,2");
    Equal(Grobal2.DR_DOWNRIGHT, M2Share.GetNextDirection(0, 0, 10, 1),
        "sign helper on the same vector is dir 3, not 2");
    Assert(M2Share.GetNextDirection(0, 0, 10, 1) !=
           M2Share.GetNextDirectionByRatio(0, 0, 10, 1),
        "the two helpers must disagree on this off-axis vector");
}

static void CheckMotaeboAndUserMoveUnsignedWrap()
{
    Assert(TPlayObject.IsNativeMotaeboTimingReady(10, 10 - 4501, 20),
        "motaebo 500 ms gate wraps unsigned (jbe at 0x6BC954)");
    Assert(TPlayObject.HasNativeUserMoveCooldownElapsed(10, 20),
        "UserMove wrap: now=10 prev=20 unsigned elapsed exceeds 10000U");
    Assert(!TPlayObject.HasNativeUserMoveCooldownElapsed(20, 9),
        "UserMove 10000 ms: elapsed 11 is not > 10000");
    Assert(TPlayObject.HasNativeUserMoveCooldownElapsed(10011, 10),
        "UserMove 10000 ms: 10011-10 = 10001 > 10000U");
    Assert(!TPlayObject.HasNativeUserMoveCooldownElapsed(10010, 10),
        "UserMove 10000 ms: equal 10000 is NOT elapsed (`>` at 0x6CE452)");
}

static void CheckGetMovObjCountCountsObModeWalkDoesNot()
{
    var environment = NewMap();
    var occupant = NewObject(environment, Grobal2.RC_PLAYOBJECT, 2, 2);
    occupant.bo2B9 = true;
    occupant.m_boObMode = true;
    Place(environment, occupant);

    Equal(1, environment.GetNativeMovObjCount(2, 2),
        "0x778858 does not exclude ObMode; skill 168/266 still see the body");
    Equal(0, environment.GetXYObjCount(2, 2),
        "walk/run occupancy uses IsNativeCellBlocking, which grants ObMode pass-through");
}

static void CheckWalkRunFailDoesNotKick()
{
    var walk = FreePlayer("walk-fail", 5, 5, Grobal2.DR_LEFT);
    walk.m_nNativeForcedMoveRemaining = 5;
    walk.m_boEmergencyClose = false;
    Assert(walk.Operate(Message(Grobal2.CM_WALK, 5, 4, 0)),
        "3011 dispatch while locked");
    Equal(false, walk.m_boEmergencyClose,
        "walk 0x276 correction must not set EmergencyClose");
    Equal(unchecked((ushort)Grobal2.SM_ACT_FAIL), walk.m_DefMsg.Ident,
        "walk fail ident 0x276");
    Equal(0, walk.m_DefMsg.Recog, "walk fail Recog = 0");
    Equal(unchecked((ushort)5), walk.m_DefMsg.Param, "walk fail Param = CurrX");
    Equal(unchecked((ushort)5), walk.m_DefMsg.Tag, "walk fail Tag = CurrY");

    var sit = FreePlayer("sit-overspeed", 5, 5, Grobal2.DR_LEFT);
    sit.m_boEmergencyClose = false;
    M2Share.g_Config.boSpeedHackCheck = false;
    M2Share.g_Config.boKickOverSpeed = true;
    M2Share.g_Config.nMaxSitDonwMsgCount = 0;
    M2Share.g_Config.dwTurnIntervalTime = 10000;
    sit.m_dwTurnTick = HUtil32.GetTickCount();
    sit.m_nOverSpeedCount = 99;
    Assert(sit.Operate(Message(Grobal2.CM_SITDOWN, 5, 5, Grobal2.DR_UP)),
        "3012 overspeed dispatch");
    Equal(false, sit.m_boEmergencyClose,
        "pose overflow must not kick (MOVE-22 / 0x6D9C8B)");
}

static TProcessMessage Message(int ident, int x, int y, int direction) => new()
{
    wIdent = ident,
    wParam = direction,
    nParam1 = x,
    nParam2 = y
};

static ProbePlayer FreePlayer(string name, short x, short y, int direction)
{
    var player = PlacePlayer(NewMap(), NewPlayer(name), x, y);
    player.m_boCanWalk = true;
    player.m_boCanRun = true;
    player.m_btDirection = (byte)direction;
    player.m_nNativeForcedMoveRemaining = 0;
    return player;
}

static ProbePlayer PlacePlayer(Envirnoment environment, ProbePlayer player,
    short x, short y)
{
    player.m_PEnvir = environment;
    player.m_sMapName = environment.sMapName;
    player.m_nCurrX = x;
    player.m_nCurrY = y;
    player.m_boOffLineFlag = true;
    Place(environment, player);
    return player;
}

static ProbePlayer NewPlayer(string name) => new()
{
    m_boOffLineFlag = true,
    m_sCharName = name,
    m_btRaceServer = Grobal2.RC_PLAYOBJECT,
    m_btAttatckMode = M2Share.HAM_ALL
};

static TBaseObject NewObject(Envirnoment environment, byte race, short x, short y)
{
    return new TBaseObject
    {
        m_PEnvir = environment,
        m_btRaceServer = race,
        m_nCurrX = x,
        m_nCurrY = y,
        bo2B9 = true,
        // SPWN-56 的有效性谓词（原生 sub_765D64）要求 Length(CName)>0，否则
        // 该 actor 会在格子链扫描时被判失效并摘链，GetMovObjCount 会返回 0。
        // 原生 actor 一律带名字，无名 actor 是夹具特有的失真态。
        m_sCharName = "probe-" + race + "-" + x + "-" + y
    };
}

static Envirnoment NewMap()
{
    var environment = new Envirnoment
    {
        sMapName = "gate-" + Guid.NewGuid().ToString("N")[..8],
        m_sMapFileName = "gate-file"
    };
    typeof(Envirnoment).GetMethod("Initialize",
        BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(environment, new object[] { (short)20, (short)20 });
    for (short x = 0; x < environment.wWidth; x++)
    {
        for (short y = 0; y < environment.wHeight; y++)
            environment.SetMapXYFlag(x, y, true);
    }
    return environment;
}

static void Place(Envirnoment environment, TBaseObject actor)
{
    actor.m_boAddToMaped = false;
    actor.m_boDelFormMaped = false;
    Assert(ReferenceEquals(actor, environment.AddToMap(actor.m_nCurrX,
        actor.m_nCurrY, CellType.OS_MOVINGOBJECT, actor)), "place actor");
}

static void InitializeRuntime()
{
    M2Share.g_Config = new GameSvrConfig { nSendRefMsgRange = 12 };
    M2Share.UserEngine = new UserEngine();
    M2Share.MagicManager = new MagicManager();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.MapManager = new MapManager();
    M2Share.CastleManager = new CastleManager();
    M2Share.RandomNumber = RandomNumber.GetInstance();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new System.Collections.ArrayList();
    M2Share.g_MonSayMsgList = new Dictionary<string, IList<TMonSayMsg>>();
}

static void PrepareRuntimeConfig()
{
    string runtimeDirectory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(runtimeDirectory, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "String.ini"),
        "[String]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "Command.conf"),
        "[Command]" + Environment.NewLine);
    string robotDirectory = Path.Combine(runtimeDirectory, "RobotIni");
    Directory.CreateDirectory(robotDirectory);
    File.WriteAllText(Path.Combine(robotDirectory, "默认.txt"),
        "[Info]" + Environment.NewLine);
    string shareDirectory = Path.Combine(Path.GetFullPath(
        Path.Combine(runtimeDirectory, "..")), "Share");
    Directory.CreateDirectory(shareDirectory);
    File.WriteAllText(Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"),
        "[Integer]" + Environment.NewLine);
    Directory.SetCurrentDirectory(runtimeDirectory);
}

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{label}: expected={expected}, actual={actual}");
}

static void Assert(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
}

sealed class ProbePlayer : TPlayObject
{
    internal override void SendSocket(ClientPacket defMsg, string message)
    {
    }
}
