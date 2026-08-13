using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using GameSvr;
using SystemModule;

try
{
    var countMethod = typeof(RobotPlayObject).GetMethod(
        "GetDirBaseObjectsCount",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(RobotPlayObject).FullName,
            "GetDirBaseObjectsCount");

    VerifyAllDirections(countMethod);
    VerifyNativeFiltering(countMethod);
    VerifyRangeEdges(countMethod);
    VerifySelectionContract();

    Console.WriteLine(
        "PASS RobotDirectionObjectCountCheck directions=8 range=inclusive " +
        "self=counted moving=all alive-only no-target-filter no-dedupe " +
        "selection=skill10-fallback9 thresholds=player>0/monster>1");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        $"RobotDirectionObjectCountCheck FAIL: {exception}");
    return 1;
}

static void VerifyAllDirections(MethodInfo countMethod)
{
    var directions = new[]
    {
        (Grobal2.DR_UP, 0, -1),
        (Grobal2.DR_UPRIGHT, 1, -1),
        (Grobal2.DR_RIGHT, 1, 0),
        (Grobal2.DR_DOWNRIGHT, 1, 1),
        (Grobal2.DR_DOWN, 0, 1),
        (Grobal2.DR_DOWNLEFT, -1, 1),
        (Grobal2.DR_LEFT, -1, 0),
        (Grobal2.DR_UPLEFT, -1, -1)
    };

    foreach (var (direction, deltaX, deltaY) in directions)
    {
        var environment = NewEnvironment(30, 30);
        var robot = NewRobot(environment, 12, 12);
        AddMoving(environment, 12 + deltaX * 5, 12 + deltaY * 5,
            NewActor());
        AddMoving(environment, 12 + deltaX * 6, 12 + deltaY * 6,
            NewActor());

        Equal(2, Count(countMethod, robot, direction, 5),
            $"direction {direction} did not include exactly steps 0..5");
    }
}

static void VerifyNativeFiltering(MethodInfo countMethod)
{
    var environment = NewEnvironment(20, 20);
    var robot = NewRobot(environment, 8, 8);
    var duplicate = NewActor();
    AddMoving(environment, 9, 8, duplicate);
    AddMoving(environment, 9, 8, duplicate);
    AddMoving(environment, 9, 8, NewActor(fixedHidden: true));
    AddMoving(environment, 9, 8,
        NewActor(race: Grobal2.RC_PLAYOBJECT));
    AddMoving(environment, 10, 8, NewActor(death: true));
    AddMoving(environment, 11, 8, NewActor(ghost: true));

    Equal(5, Count(countMethod, robot, Grobal2.DR_RIGHT, 3),
        "native moving-object filters or duplicate semantics changed");
}

static void VerifyRangeEdges(MethodInfo countMethod)
{
    var environment = NewEnvironment(12, 12);
    var robot = NewRobot(environment, 5, 5);

    Equal(1, Count(countMethod, robot, Grobal2.DR_UP, 0),
        "range zero did not count the actor's own cell");
    Equal(0, Count(countMethod, robot, Grobal2.DR_UP, -1),
        "negative range must scan no cells");
    Equal(0, Count(countMethod, robot, 8, 5),
        "invalid direction must scan no cells");
}

static void VerifySelectionContract()
{
    var root = FindRepositoryRoot();
    var source = File.ReadAllText(Path.Combine(root, "GameSvr", "RobotPlay",
        "RobotPlayObject.cs"));

    Require(Regex.IsMatch(source,
            @"GetDirBaseObjectsCount\(m_btDirection,\s*5\)\s*>\s*0"),
        "player/master linear-skill threshold is not > 0");
    Require(Regex.IsMatch(source,
            @"GetDirBaseObjectsCount\(m_btDirection,\s*5\)\s*>\s*1"),
        "ordinary-target linear-skill threshold is not > 1");
    Require(source.Contains("AllowUseMagic(10)", StringComparison.Ordinal)
            && source.Contains("else if (AllowUseMagic(9))",
                StringComparison.Ordinal),
        "skill 10 to skill 9 fallback was removed");
    Require(Regex.IsMatch(source,
            @"m_SkillUseTick\[10\]\s*>\s*5000"),
        "linear-skill 5000 ms cooldown was removed");
}

static RobotPlayObject NewRobot(Envirnoment environment, int x, int y)
{
    var robot = (RobotPlayObject)RuntimeHelpers.GetUninitializedObject(
        typeof(RobotPlayObject));
    robot.m_PEnvir = environment;
    robot.m_nCurrX = checked((short)x);
    robot.m_nCurrY = checked((short)y);
    AddMoving(environment, x, y, robot);
    return robot;
}

static TBaseObject NewActor(bool death = false, bool ghost = false,
    bool fixedHidden = false, byte race = Grobal2.RC_ANIMAL)
{
    var actor = (TBaseObject)RuntimeHelpers.GetUninitializedObject(
        typeof(TBaseObject));
    actor.m_boDeath = death;
    actor.m_boGhost = ghost;
    actor.m_boFixedHideMode = fixedHidden;
    actor.m_btRaceServer = race;
    return actor;
}

static void AddMoving(Envirnoment environment, int x, int y,
    TBaseObject actor)
{
    var cell = EnsureObjectList(environment, x, y);
    cell.Add(new CellObject
    {
        CellType = CellType.OS_MOVINGOBJECT,
        CellObj = actor
    });
}

static IList<CellObject> EnsureObjectList(Envirnoment environment,
    int x, int y)
{
    MapCellinfo cell = default;
    Require(environment.GetMapCellInfo(x, y, ref cell),
        $"map cell is outside test environment: {x},{y}");
    if (cell.ObjList == null)
    {
        Require(environment.AddToMapItemEvent(x, y, CellType.OS_EVENTOBJECT,
                new object()) != null,
            $"could not allocate map cell list: {x},{y}");
        Require(environment.GetMapCellInfo(x, y, ref cell)
                && cell.ObjList != null,
            $"allocated map cell list was not published: {x},{y}");
    }
    return cell.ObjList;
}

static Envirnoment NewEnvironment(short width, short height)
{
    var environment = new Envirnoment();
    var initialize = typeof(Envirnoment).GetMethod("Initialize",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(Envirnoment).FullName,
            "Initialize");
    initialize.Invoke(environment, new object[] { width, height });
    return environment;
}

static int Count(MethodInfo method, RobotPlayObject robot, int direction,
    int range) => (int)(method.Invoke(robot, new object[] { direction, range })
    ?? throw new InvalidOperationException("count method returned null"));

static void Equal(int expected, int actual, string message)
{
    if (expected != actual)
        throw new InvalidOperationException(
            $"{message}: expected={expected}, actual={actual}");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static string FindRepositoryRoot()
{
    return AuditRepoRoot.Resolve();
}
