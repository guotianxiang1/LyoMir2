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
M2Share.LogStringList = new System.Collections.ArrayList();
M2Share.g_MonSayMsgList = new Dictionary<string, IList<TMonSayMsg>>();
M2Share.MapManager = new MapManager();
M2Share.nServerIndex = 0;

var bounds = typeof(TBaseObject).GetMethod("WalkToInBounds",
    BindingFlags.Instance | BindingFlags.NonPublic);
var dirValid = typeof(TBaseObject).GetMethod("WalkToDirectionIsValid",
    BindingFlags.Instance | BindingFlags.NonPublic);

// The three native movers must be three distinct C# bodies (MOVE-40).
Assert(bounds.IsVirtual, "WalkToInBounds must be virtual to split by kind");
var playerBounds = typeof(TPlayObject).GetMethod("WalkToInBounds",
    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
var heroBounds = typeof(HeroObject).GetMethod("WalkToInBounds",
    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
var animalBounds = typeof(AnimalObject).GetMethod("WalkToInBounds",
    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
Assert(playerBounds != null, "TPlayObject must re-declare the human bounds");
Assert(heroBounds != null, "HeroObject must re-declare the human bounds");
Assert(animalBounds != null, "AnimalObject must declare the monster bounds");

var map = NewEnvironment("Move64Witness", "move64-witness", 30, 30);

// Base = the TCreature/TPsNpc slot 0x767568, whose whole body is
// "xor eax,eax; ret" -> never moves.
var creature = new TBaseObject { m_PEnvir = map, m_nCurrX = 5, m_nCurrY = 5 };
Assert(!Invoke(bounds, creature, 5, 6),
    "TCreature/TPsNpc mover must never move (0x767568 xor eax,eax)");
Assert(!Invoke(bounds, creature, 1, 1),
    "TCreature/TPsNpc mover must refuse every cell");

// Human ladder: 0x741276 jle / 0x741284 jge  =>  x > 0 && x < Width
var player = new TPlayObject { m_PEnvir = map, m_nCurrX = 1, m_nCurrY = 1 };
Assert(!Invoke(bounds, player, 0, 5), "player must be refused column 0");
Assert(!Invoke(bounds, player, 5, 0), "player must be refused row 0");
Assert(Invoke(bounds, player, 1, 1), "player must accept (1,1)");
Assert(Invoke(bounds, player, 29, 29),
    "player must accept Width-1 (the old shared test rejected it)");
Assert(!Invoke(bounds, player, 30, 5), "player must be refused x == Width");

var hero = new HeroObject { m_PEnvir = map };
Assert(!Invoke(bounds, hero, 0, 5), "hero follows the human ladder at column 0");
Assert(Invoke(bounds, hero, 29, 29), "hero accepts Width-1");

// Monster ladder: 0x71F141 jl / 0x71F14F jg  =>  x >= 0 && x <= Width
var monster = new Monster { m_PEnvir = map, m_nCurrX = 1, m_nCurrY = 1 };
Assert(Invoke(bounds, monster, 0, 5), "monster must reach column 0");
Assert(Invoke(bounds, monster, 5, 0), "monster must reach row 0");
Assert(Invoke(bounds, monster, 30, 5), "monster may attempt x == Width");
Assert(Invoke(bounds, monster, 5, 30), "monster may attempt y == Height");
Assert(!Invoke(bounds, monster, -1, 5), "monster refused x < 0");
Assert(!Invoke(bounds, monster, 31, 5), "monster refused x > Width");

// The x == Width attempt the monster is allowed to make must be rejected by
// MoveToMovingObject (native step 2, 0x779825) BEFORE the source unlink, so the
// actor is never orphaned.
var edge = new Monster { m_PEnvir = map, m_nCurrX = 29, m_nCurrY = 29 };
Place(map, edge);
Equal(-1, map.MoveToMovingObject(29, 29, edge, 30, 29, true),
    "out-of-bounds destination must fail");
Equal(1, GetCellCount(map, 29, 29),
    "failed out-of-bounds move must leave the actor in its source cell");

// MOVE-35(b): sub_7797CC has exactly one TRUE store, and it sits inside the branch
// that matched the actor in the SOURCE cell list:
//   00779A4C  83 7D EC 00  cmp dword [ebp-0x14], 0   ; source list head
//   00779A50  74 5B        je  0x779AAD              ; empty -> FALSE
//   00779A61  3B 45 0C     cmp eax, [ebp+0xC]        ; is this node the actor?
//   00779A64  75 35        jne 0x779A9B              ; no -> next node
//   00779A95  C6 45 F7 01  mov byte [ebp-9], 1       ; the only TRUE
//   00779AAD  33 C0        xor eax, eax              ; ran off the end -> FALSE
// So a move whose source cell does not hold the actor is FALSE, and must not publish
// it at the destination either: doing that is how a cell acquires a registration for
// an actor that lives somewhere else.
var orphan = new Monster { m_PEnvir = map, m_nCurrX = 10, m_nCurrY = 11 };
Equal(0, map.MoveToMovingObject(10, 11, orphan, 11, 11, true),
    "moving out of a cell the actor was never in must fail");
Equal(0, GetCellCount(map, 11, 11),
    "failed source unlink must not publish the actor at the destination");

// dir validation: 0x74123E sub 8 / jb, 0x71F115 sub 8 / jae
Assert(Invoke1(dirValid, player, (byte)7), "dir 7 valid");
Assert(!Invoke1(dirValid, player, (byte)8), "dir 8 rejected");
Assert(!Invoke1(dirValid, monster, (byte)8), "monster dir 8 rejected");

// A dir > 7 previously fell through the switch leaving (0,0); with the monster's
// looser bound that would have teleported it to the corner.
var stray = new Monster { m_PEnvir = map, m_nCurrX = 10, m_nCurrY = 10 };
Place(map, stray);
Assert(!stray.WalkTo(9, false), "out-of-range dir must not move the monster");
Equal(10, stray.m_nCurrX, "stray X unchanged");
Equal(10, stray.m_nCurrY, "stray Y unchanged");

// MOVE-37 stays faithful: a refused move still changes facing.
var facing = new TPlayObject { m_PEnvir = map, m_nCurrX = 1, m_nCurrY = 1 };
Place(map, facing);
facing.m_btDirection = Grobal2.DR_DOWN;
facing.WalkTo(Grobal2.DR_UPLEFT, false);
Equal(Grobal2.DR_UPLEFT, facing.m_btDirection,
    "refused move must still commit facing (MOVE-37)");
Equal(1, facing.m_nCurrX, "player did not step into column 0");
Equal(1, facing.m_nCurrY, "player did not step into row 0");

Console.WriteLine("MOVE64_WITNESS PASS");

static bool Invoke(MethodInfo m, TBaseObject actor, int x, int y)
{
    return (bool)m.Invoke(actor, new object[] { (short)x, (short)y });
}

static bool Invoke1(MethodInfo m, TBaseObject actor, byte dir)
{
    return (bool)m.Invoke(actor, new object[] { dir });
}

static int GetCellCount(Envirnoment environment, int x, int y)
{
    var found = false;
    var cell = environment.GetMapCellInfo(x, y, ref found);
    return found && cell.ObjList != null ? cell.Count : 0;
}

static void Place(Envirnoment environment, TBaseObject actor)
{
    actor.m_boAddToMaped = false;
    actor.m_boDelFormMaped = false;
    environment.AddToMap(actor.m_nCurrX, actor.m_nCurrY,
        CellType.OS_MOVINGOBJECT, actor);
}

static Envirnoment NewEnvironment(string name, string file, short w, short h)
{
    var environment = new Envirnoment { sMapName = name, m_sMapFileName = file };
    typeof(Envirnoment).GetMethod("Initialize",
        BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(environment, new object[] { w, h });
    // true == CellAttribute.Walk; the whole map must be walkable for this witness.
    for (var x = 0; x < w; x++)
        for (var y = 0; y < h; y++)
            environment.SetMapXYFlag(x, y, true);
    return environment;
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        Console.WriteLine("MOVE64_WITNESS FAIL " + message);
        Environment.Exit(1);
    }
}

static void Equal(int expected, int actual, string message)
{
    if (expected != actual)
    {
        Console.WriteLine("MOVE64_WITNESS FAIL " + message +
            " expected=" + expected + " actual=" + actual);
        Environment.Exit(1);
    }
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
