using System.Collections;
using System.Reflection;
using GameSvr;
using SystemModule;

PrepareRuntimeConfig();
PrepareRuntimeState();
var sourceRoot = FindSourceRoot();
CheckSourceContract(sourceRoot);
var definitions = PrepareDefinitions();
CheckCopyDoesNotRandomize(definitions.Helmet);
CheckUnknownModeDispatch(definitions);
CheckConfiguredBagAndEquipment(definitions);
CheckAutoRepairPath(definitions.Helmet);
CheckMonsterDropDoesNotRandomize(definitions.WrongShape);
CheckMonsterDropRunsNativePlus28(definitions.Helmet);

Console.WriteLine("PASS RobotUnknownItemInitCheck " +
    "paths=init-bag+init-equipped+15s-repair " +
    "guard=modes15/19/20/21/22/23/24/26+shape130/131/132 " +
    "dispatch=15+22/23+24/26 marker=btValue8 " +
    "no-dispatch=19/20/21 copy=plain repair=single-instance " +
    "mondrop=no-unknown-roll+dura=sub_783EFC(20+Random(80))%");

static void PrepareRuntimeState()
{
    var robotIniDirectory = Path.Combine(Directory.GetCurrentDirectory(),
        "RobotIni");
    Directory.CreateDirectory(robotIniDirectory);
    File.WriteAllText(Path.Combine(robotIniDirectory, "默认.txt"),
        "[Info]" + Environment.NewLine);

    M2Share.g_Config = new GameSvrConfig
    {
        boAutoRepairItem = true,
        boAutoPickUpItem = false,
        boHPAutoMoveMap = false,
        boRenewHealth = false
    };
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new ArrayList();
    M2Share.RandomNumber = RandomNumber.GetInstance();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.MapManager = new MapManager();
    M2Share.CastleManager = new CastleManager();
    M2Share.EventManager = new EventManager();
    M2Share.g_MonSayMsgList = new Dictionary<string, IList<TMonSayMsg>>();
    M2Share.UserEngine = new UserEngine();
}

static DefinitionSet PrepareDefinitions()
{
    M2Share.UserEngine.StdItemList.Clear();
    var set = new DefinitionSet
    {
        Helmet = AddDefinition("AuditUnknownHelmet", 15, 130),
        Ring22 = AddDefinition("AuditUnknownRing22", 22, 131),
        Ring23 = AddDefinition("AuditUnknownRing23", 23, 132),
        Necklace24 = AddDefinition("AuditUnknownNecklace24", 24, 130),
        Necklace26 = AddDefinition("AuditUnknownNecklace26", 26, 131),
        Mode19 = AddDefinition("AuditUnknownMode19", 19, 130),
        Mode20 = AddDefinition("AuditUnknownMode20", 20, 131),
        Mode21 = AddDefinition("AuditUnknownMode21", 21, 132),
        WrongShape = AddDefinition("AuditUnknownWrongShape", 15, 129),
        WrongMode = AddDefinition("AuditUnknownWrongMode", 14, 130)
    };
    return set;
}

static GoodItem AddDefinition(string name, byte stdMode, byte shape)
{
    var definition = new GoodItem
    {
        Name = name,
        StdMode = stdMode,
        Shape = shape,
        ItemType = GoodType.ITEM_ACCESSORY,
        DuraMax = 1000
    };
    M2Share.UserEngine.StdItemList.Add(definition);
    return definition;
}

static void CheckCopyDoesNotRandomize(GoodItem definition)
{
    TUserItem item = null;
    Assert(M2Share.UserEngine.CopyToUserItemFromName(definition.Name,
            ref item),
        "CopyToUserItemFromName rejected the fixture item");
    Assert(item != null && item.btValue.All(value => value == 0),
        "shared CopyToUserItemFromName randomized an unknown item");
}

static void CheckUnknownModeDispatch(DefinitionSet definitions)
{
    foreach (var definition in new[]
             {
                 definitions.Helmet, definitions.Ring22, definitions.Ring23,
                 definitions.Necklace24, definitions.Necklace26
             })
    {
        var item = NewItem(definition);
        definition.RandomUpgradeUnknownItem(item);
        Equal((byte)1, item.btValue[8],
            definition.Name + " unknown marker");
    }

    foreach (var definition in new[]
             {
                 definitions.Mode19, definitions.Mode20, definitions.Mode21
             })
    {
        var item = NewItem(definition);
        var dura = item.Dura;
        var duraMax = item.DuraMax;
        definition.RandomUpgradeUnknownItem(item);
        Assert(item.btValue.All(value => value == 0)
               && item.Dura == dura && item.DuraMax == duraMax,
            definition.Name + " should have no native unknown dispatch");
    }
}

static void CheckConfiguredBagAndEquipment(DefinitionSet definitions)
{
    var tempRoot = Path.Combine(Path.GetTempPath(),
        "loym2-robot-unknown-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempRoot);
    try
    {
        var configPath = Path.Combine(tempRoot, "robot.ini");
        File.WriteAllText(configPath,
            "[Info]" + Environment.NewLine +
            "Level=1" + Environment.NewLine +
            "InitItems=" + string.Join(',', new[]
            {
                definitions.Helmet.Name, definitions.Mode19.Name,
                definitions.WrongShape.Name, definitions.WrongMode.Name
            }) + Environment.NewLine +
            "[UseItems]" + Environment.NewLine +
            "UseItems0=" + Environment.NewLine +
            "UseItems1=" + Environment.NewLine +
            "UseItems2=" + Environment.NewLine +
            "UseItems3=" + definitions.Necklace24.Name + Environment.NewLine +
            "UseItems4=" + Environment.NewLine +
            "UseItems5=" + Environment.NewLine +
            "UseItems6=" + Environment.NewLine +
            "UseItems7=" + definitions.Ring22.Name + Environment.NewLine +
            "UseItems8=" + Environment.NewLine);

        var robot = NewRobot();
        new AIObjectConf(configPath).LoadConfig(robot);

        Equal((byte)1, FindBagItem(robot, definitions.Helmet).btValue[8],
            "InitItems helmet marker");
        Assert(FindBagItem(robot, definitions.Mode19).btValue.All(
                value => value == 0),
            "InitItems mode 19 unexpectedly dispatched");
        Assert(FindBagItem(robot, definitions.WrongShape).btValue.All(
                value => value == 0),
            "InitItems wrong shape bypassed the native guard");
        Assert(FindBagItem(robot, definitions.WrongMode).btValue.All(
                value => value == 0),
            "InitItems wrong mode bypassed the native guard");
        Equal((byte)1,
            robot.m_UseItems[Grobal2.U_NECKLACE].btValue[8],
            "UseItems necklace marker");
        Equal((byte)1, robot.m_UseItems[Grobal2.U_RINGL].btValue[8],
            "UseItems ring marker");
    }
    finally
    {
        Directory.Delete(tempRoot, true);
    }
}

static void CheckAutoRepairPath(GoodItem helmet)
{
    var gateRobot = NewRobot();
    gateRobot.m_UseItemNames[Grobal2.U_HELMET] = helmet.Name;
    gateRobot.m_dwAutoRepairItemTick = HUtil32.GetTickCount();
    ForceWalkCycle(gateRobot);
    gateRobot.Run();
    Equal((ushort)0, gateRobot.m_UseItems[Grobal2.U_HELMET].wIndex,
        "auto repair ignored the 15 second gate");

    var robot = NewRobot();
    robot.m_UseItemNames[Grobal2.U_HELMET] = helmet.Name;
    robot.m_dwAutoRepairItemTick = HUtil32.GetTickCount() - 15_001;
    ForceWalkCycle(robot);
    robot.Run();
    var repaired = robot.m_UseItems[Grobal2.U_HELMET];
    Assert(repaired != null && repaired.wIndex > 0,
        "auto repair did not create the missing item");
    Equal((byte)1, repaired.btValue[8],
        "auto repair did not initialize unknown attributes");
    var makeIndex = repaired.MakeIndex;
    var values = repaired.btValue.ToArray();

    robot.m_dwAutoRepairItemTick = HUtil32.GetTickCount() - 15_001;
    ResetRunGates(robot);
    ForceWalkCycle(robot);
    robot.Run();
    Assert(ReferenceEquals(repaired,
            robot.m_UseItems[Grobal2.U_HELMET])
           && robot.m_UseItems[Grobal2.U_HELMET].MakeIndex == makeIndex
           && robot.m_UseItems[Grobal2.U_HELMET].btValue.SequenceEqual(values),
        "auto repair randomized an equipped item more than once");
}

static RobotPlayObject NewRobot()
{
    var environment = new Envirnoment
    {
        sMapName = "RobotUnknownAudit-" + Guid.NewGuid().ToString("N"),
        m_sMapFileName = "RobotUnknownAudit",
        nServerIndex = 0
    };
    typeof(Envirnoment).GetMethod("Initialize",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(environment, new object[] { (short)16, (short)16 });
    var robot = new RobotPlayObject
    {
        m_boAI = true,
        m_boAIStart = false,
        m_boDeath = false,
        m_boGhost = false,
        m_boFixedHideMode = false,
        m_boStoneMode = false,
        m_ManagedEnvir = environment,
        m_PEnvir = environment,
        m_nWalkSpeed = 0,
        m_dwThinkTick = HUtil32.GetTickCount(),
        m_nCurrX = 1,
        m_nCurrY = 1
    };
    robot.m_WAbil.MaxHP = 100;
    robot.m_WAbil.HP = 100;
    robot.m_WAbil.MaxMP = 100;
    robot.m_WAbil.MP = 100;
    robot.m_boAddToMaped = false;
    robot.m_boDelFormMaped = false;
    Assert(ReferenceEquals(robot, environment.AddToMap(1, 1,
            CellType.OS_MOVINGOBJECT, robot)),
        "could not publish the audit robot");
    for (var index = 0; index < robot.m_UseItems.Length; index++)
        robot.m_UseItems[index] = new TUserItem();
    return robot;
}

static void ForceWalkCycle(RobotPlayObject robot)
{
    robot.m_dwWalkTick = HUtil32.GetTickCount() - 1_000;
    robot.m_dwThinkTick = HUtil32.GetTickCount();
}

static void ResetRunGates(RobotPlayObject robot)
{
    robot.m_boAI = true;
    robot.m_boAIStart = false;
    robot.m_boDeath = false;
    robot.m_boGhost = false;
    robot.m_boFixedHideMode = false;
    robot.m_boStoneMode = false;
    robot.m_wStatusTimeArr[Grobal2.POISON_STONE] = 0;
    robot.m_TargetCret = null;
    robot.m_ManagedEnvir = robot.m_PEnvir;
    robot.m_WAbil.MaxHP = 100;
    robot.m_WAbil.HP = 100;
    robot.m_WAbil.MaxMP = 100;
    robot.m_WAbil.MP = 100;
}

static TUserItem NewItem(GoodItem definition)
{
    TUserItem item = null;
    Assert(M2Share.UserEngine.CopyToUserItemFromName(definition.Name,
            ref item),
        "could not copy fixture item " + definition.Name);
    return item;
}

static TUserItem FindBagItem(RobotPlayObject robot, GoodItem definition)
{
    var item = robot.m_ItemList.SingleOrDefault(candidate =>
        ReferenceEquals(M2Share.UserEngine.GetStdItem(candidate.wIndex),
            definition));
    return item ?? throw new InvalidOperationException(
        "bag item missing: " + definition.Name);
}

static void CheckSourceContract(string sourceRoot)
{
    var configSource = File.ReadAllText(Path.Combine(sourceRoot,
        "GameSvr", "Configs", "AIObjectConf.cs"));
    var robotSource = File.ReadAllText(Path.Combine(sourceRoot,
        "GameSvr", "RobotPlay", "RobotPlayObject.Base.cs"));
    var engineSource = File.ReadAllText(Path.Combine(sourceRoot,
        "GameSvr", "UsrSystem", "UsrEngn.cs"));

    Equal(2, Count(configSource,
            "StdItem.RandomUpgradeUnknownItem(UserItem);"),
        "AIObjectConf unknown initialization call count");
    Equal(1, Count(robotSource,
            "StdItem.RandomUpgradeUnknownItem(UserItem);"),
        "RobotPlayObject repair initialization call count");
    Assert(robotSource.Contains(
            "HUtil32.GetTickCount() - m_dwAutoRepairItemTick > 15000"),
        "RobotPlayObject 15 second native repair gate is missing");

    var copyStart = engineSource.IndexOf(
        "public bool CopyToUserItemFromName", StringComparison.Ordinal);
    var copyEnd = engineSource.IndexOf(
        "public void ProcessUserMessage", copyStart,
        StringComparison.Ordinal);
    Assert(copyStart >= 0 && copyEnd > copyStart,
        "CopyToUserItemFromName source boundary is missing");
    Assert(!engineSource[copyStart..copyEnd].Contains(
            "RandomUpgradeUnknownItem", StringComparison.Ordinal),
        "shared item copy path now randomizes unknown attributes");

    var dropStart = engineSource.IndexOf("public void MonGetRandomItems",
        StringComparison.Ordinal);
    Assert(dropStart >= 0 && copyStart > dropStart
           && Count(engineSource[dropStart..copyStart],
               "RandomUpgradeUnknownItem(UserItem);") == 0,
        "monster drop path reacquired an unknown randomization the original has no call site for");
}

// The only per-item initialisation the original's drop loop performs is the freshly
// built object's virtual +0x28, invoked once with edx=0 at 0x71FDA2, and for the base
// item class that slot is sub_783EFC:
//   00783F05  B8 50 00 00 00  mov   eax, 0x50             ; 80
//   00783F0A  E8 3D FC C7 FF  call  0x403B4C              ; Random
//   00783F0F  83 C0 14        add   eax, 0x14             ; +20
//   00783F18  0F B7 43 28     movzx eax, word [ebx+0x28]  ; DuraMax
//   00783F22  D8 35 38 3F 78  fdiv  dword [0x783F38]      ; 100.0
//   00783F28  DE C9           fmulp st(1)
//   00783F2A  E8 45 F6 C7 FF  call  0x403574              ; @ROUND
//   00783F2F  66 89 43 26     mov   word [ebx+0x26], ax   ; Dura
// The drop function itself has exactly three Random call sites (0x71FB76, 0x71FD3D,
// 0x71FD6B, all inside the two gold branches) and the factory sub_74C338 has none, so
// the stock-Mir2 `StdMode/Shape -> RandomUpgradeUnknownItem` gate has no counterpart
// here. A monster drop must leave btValue untouched however unknown-looking the
// template is, and must set Dura from that one Random(80) band.
// Shape 130/131/132 does NOT take the plain Dura80 arm: THelmet's +0x28 is
// sub_7611C8 and it dispatches those three shapes to [vmt+0x08] instead.
//   007611D1  8b 46 1c        mov eax,[esi+0x1C]     ; StdItem
//   007611D4  8a 40 15        mov al,[eax+0x15]      ; Shape
//   007611D7  04 7e           add al,0x7E
//   007611D9  2c 03           sub al,3
//   007611DB  73 0c           jae 0x7611E9           ; Shape>=133 -> normal
//   007611DD..E1              call [vmt+0x08]        ; 130/131/132 -> unknown body
//   007611ED  e8 0a 2d 02 00  call 0x783EFC          ; normal: Dura80
//   007611FA  b8 0a 00 00 00  mov eax,0xA / call 0x403B4C   ; Random(10) gate
//   0076120F  f6 40 02 40     test byte [eax+2],0x40 ; extra-attr flag
//   00761213  0f 84 1b 01 00 00 je 0x761334
// The fixture therefore has to be a NON-unknown shape for "btValue stays 0 and
// Dura comes from the one Random(80) band" to be the native contract at all.
// (StdMode 15 is THelmet for every shape — factory sub_74C338 / case 15.)
static void CheckMonsterDropDoesNotRandomize(GoodItem definition)
{
    const string monsterName = "AuditDropMonster";
    M2Share.UserEngine.MonsterList.Add(new TMonInfo
    {
        sName = monsterName,
        ItemList = new List<TMonItem>
        {
            // high32(1 * seed) is 0 for every seed, so 0 <= SelPoint always drops.
            new TMonItem
            {
                ItemName = definition.Name, MaxPoint = 1, SelPoint = 1, Count = 1
            }
        }
    });

    var monster = new TBaseObject { m_sCharName = monsterName };
    M2Share.UserEngine.MonGetRandomItems(monster);

    Equal(1, monster.m_ItemList.Count, "monster drop produced no item");
    var dropped = monster.m_ItemList[0];
    Assert(dropped.btValue.All(value => value == 0),
        "monster drop path randomized unknown attributes");
    Equal(definition.DuraMax, dropped.DuraMax, "monster drop DuraMax");
    Assert(dropped.Dura >= definition.DuraMax / 100.0 * 20
           && dropped.Dura <= definition.DuraMax / 100.0 * 99,
        $"monster drop Dura {dropped.Dura} outside the sub_783EFC 20..99% band");
}

// The stock-Mir2 marker must still never appear on a drop, even for the shapes
// that DO take the native unknown body: RandomUpgradeUnknownItem stamps
// btValue[8] = 1 (asserted by CheckUnknownModeDispatch), and THelmet's
// +0x08 body @0x761338 writes slots 0..7 only. So btValue[8] == 0 is the exact
// separator between "native +0x28 ran" and "the stock gate got reintroduced".
static void CheckMonsterDropRunsNativePlus28(GoodItem definition)
{
    const string monsterName = "AuditPlus28Monster";
    M2Share.UserEngine.MonsterList.Add(new TMonInfo
    {
        sName = monsterName,
        ItemList = new List<TMonItem>
        {
            new TMonItem
            {
                ItemName = definition.Name, MaxPoint = 1, SelPoint = 1, Count = 1
            }
        }
    });

    var monster = new TBaseObject { m_sCharName = monsterName };
    M2Share.UserEngine.MonGetRandomItems(monster);

    Equal(1, monster.m_ItemList.Count, "unknown-shape drop produced no item");
    var dropped = monster.m_ItemList[0];
    Equal((byte)0, dropped.btValue[8],
        "monster drop stamped the stock RandomUpgradeUnknownItem marker");
}

static int Count(string source, string value)
{
    var count = 0;
    var offset = 0;
    while ((offset = source.IndexOf(value, offset,
               StringComparison.Ordinal)) >= 0)
    {
        count++;
        offset += value.Length;
    }
    return count;
}

static string FindSourceRoot()
{
    foreach (var origin in new[]
             {
                 Environment.GetEnvironmentVariable("LYOMIR_SOURCE_ROOT"),
                 Directory.GetCurrentDirectory(), AppContext.BaseDirectory
             })
    {
        if (string.IsNullOrWhiteSpace(origin)) continue;
        var directory = new DirectoryInfo(origin);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameSvr",
                    "GameSvr.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
    }
    throw new InvalidOperationException("source root was not found");
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

sealed class DefinitionSet
{
    public GoodItem Helmet { get; init; }
    public GoodItem Ring22 { get; init; }
    public GoodItem Ring23 { get; init; }
    public GoodItem Necklace24 { get; init; }
    public GoodItem Necklace26 { get; init; }
    public GoodItem Mode19 { get; init; }
    public GoodItem Mode20 { get; init; }
    public GoodItem Mode21 { get; init; }
    public GoodItem WrongShape { get; init; }
    public GoodItem WrongMode { get; init; }
}
