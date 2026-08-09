using System.Reflection;
using System.Text;
using GameSvr;
using GameSvr.PasEngine;
using GameSvr.Plugins;
using SystemModule;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
PrepareRuntimeConfig();

var failures = new List<string>();
var tempRoot = Path.Combine(Path.GetTempPath(),
    "loym2-yanshen208-api-" + Guid.NewGuid().ToString("N"));

try
{
    InitializeGameState();
    var manager = LoadYanshenPlugin(tempRoot);
    M2Share.PluginManager = manager;

    var owner = NewPlayer("yanshen208-owner");
    AddOnlinePlayer(owner);
    var bagItem = new TUserItem
    {
        MakeIndex = 820801,
        wIndex = 1,
        Dura = 100,
        DuraMax = 100
    };
    owner.m_ItemList.Add(bagItem);

    var bodyItem = new TUserItem
    {
        MakeIndex = 820815,
        wIndex = 1,
        Dura = 88,
        DuraMax = 99
    };
    Require(owner.m_UseItems.Length >= 16,
        $"runtime player equipment slots: expected at least 16, actual {owner.m_UseItems.Length}");
    owner.m_UseItems[15] = bodyItem;

    var hero = new HeroObject();
    hero.m_Abil.WearWeight = 111;
    hero.m_WAbil.WearWeight = 321;
    owner.m_HeroObject = hero;

    var monster = new Monster
    {
        m_sCharName = "yanshen208-monster",
        m_nRunTime = 777
    };
    var npc = new NormNpc
    {
        m_sCharName = "yanshen208-npc",
        m_nWalkSpeed = 1234,
        m_nNextHitTime = 2345
    };

    var bridge = new PasApiBridge();
    Require(bridge.CurrentPlayer == null,
        "PasApiBridge unexpectedly started with a current player");
    var interpreter = CreateInterpreter(bridge);
    interpreter.SetGlobal("Owner", PasValue.FromObject(owner));
    interpreter.SetGlobal("ItemId", PasValue.FromInt(bagItem.MakeIndex));

    Check("CurrentPlayer=null globals", () =>
    {
        Require(bridge.CurrentPlayer == null,
            "global probes acquired a current player");
        Equal(208, RunInt(interpreter, "ProbeGlobalInt"), "YSSetG/YSGetG result");
        Equal("yanshen-2.08", RunString(interpreter, "ProbeGlobalString"),
            "YSSetStr/YSGetStr result");
        Require(bridge.CurrentPlayer == null,
            "global probes leaked a current player into the bridge");
    });

    Check("2.08 item object surface", () =>
    {
        Equal(20803, RunInt(interpreter, "ProbeOutlook"),
            "Ys_GetItemDBData(Player,itemid,3) Outlook");
        Equal(bagItem.MakeIndex, RunInt(interpreter, "ProbeItemRoundTrip"),
            "YSGetItem -> YSGetItemID");
        Equal(1, RunInt(interpreter, "ProbeBind"), "YSBindItem return value");
        Equal((byte)1, bagItem.Bind, "YSBindItem owner item Bind");
        Equal((byte)1, bagItem.btValue[8], "YSBindItem owner bind marker");
        Require(ReferenceEquals(owner.m_ItemList.Single(), bagItem),
            "YSBindItem replaced or moved the real owner item");
    });

    Check("monster speed fields and NPC rejection", () =>
    {
        interpreter.SetGlobal("RoleId", PasValue.FromInt(monster.ObjectId));
        Equal(1, RunInt(interpreter, "ProbeChangeRole"),
            "YSChangeRole monster result");
        Equal(444, monster.m_nWalkSpeed, "YS mapped to m_nWalkSpeed");
        Equal(555, monster.m_nNextHitTime, "GS mapped to m_nNextHitTime");
        Equal(777, monster.m_nRunTime, "YS must not overwrite m_nRunTime");

        var npcState = (npc.m_nWalkSpeed, npc.m_nNextHitTime,
            npc.m_WAbil.HP, npc.m_WAbil.MaxHP);
        interpreter.SetGlobal("RoleId", PasValue.FromInt(npc.ObjectId));
        Equal(0, RunInt(interpreter, "ProbeChangeRole"),
            "YSChangeRole NPC result");
        Equal(npcState, (npc.m_nWalkSpeed, npc.m_nNextHitTime,
                npc.m_WAbil.HP, npc.m_WAbil.MaxHP),
            "YSChangeRole changed NPC state");
    });

    Check("slot 15, live hero weight, and save record", () =>
    {
        Equal(bodyItem.MakeIndex, RunInt(interpreter, "ProbeBodySlot15"),
            "YSGetBodyItem(Player,15)");
        Equal(321, RunInt(interpreter, "ProbeHeroWearWeight"),
            "YSGetHeroShuXing live m_WAbil wear weight");

        var record = new THumDataInfo();
        owner.MakeSaveRcd(ref record);
        Equal(16, record.Data.HumItems.Length, "MakeSaveRcd equipment slot count");
        Require(record.Data.HumItems[15] != null,
            "MakeSaveRcd dropped equipment slot 15");
        Equal(bodyItem.MakeIndex, record.Data.HumItems[15].MakeIndex,
            "MakeSaveRcd slot 15 MakeIndex");
        Equal(bodyItem.wIndex, record.Data.HumItems[15].wIndex,
            "MakeSaveRcd slot 15 standard item index");
    });
}
finally
{
    M2Share.PluginManager = null;
    try { Directory.Delete(tempRoot, recursive: true); } catch { }
}

if (failures.Count != 0)
{
    Console.Error.WriteLine($"FAIL yanshen-2.08 API surface checks={failures.Count}");
    foreach (var failure in failures)
        Console.Error.WriteLine(" - " + failure);
    Environment.ExitCode = 1;
    return;
}

Console.WriteLine(
    "PASS yanshen-2.08 interpreter=no-player item=outlook+object+bind " +
    "role=speed+npc-reject equipment=slot15 hero=live-weight save=slot15");
return;

void Check(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine("PASS " + name);
    }
    catch (Exception ex)
    {
        failures.Add($"{name}: {ex}");
    }
}

static PasInterpreter CreateInterpreter(PasApiBridge bridge)
{
    const string source = """
        program Yanshen208ApiProbe;
        var
          Owner: TPlayer;
          ItemId: Integer;
          RoleId: Integer;

        procedure initys;
        begin
        end;

        function ProbeGlobalInt: Integer;
        begin
          YSSetG('yanshen208-api-int', 208);
          Result := YSGetG('yanshen208-api-int');
        end;

        function ProbeGlobalString: string;
        begin
          YSSetStr('yanshen208-api-str', 'yanshen-2.08');
          Result := YSGetStr('yanshen208-api-str');
        end;

        function ProbeOutlook: Integer;
        begin
          Result := Ys_GetItemDBData(Owner, ItemId, 3);
        end;

        function ProbeItemRoundTrip: Integer;
        begin
          Result := YSGetItemID(YSGetItem(ItemId));
        end;

        function ProbeBind: Integer;
        begin
          Result := YSBindItem(ItemId, 1);
        end;

        function ProbeChangeRole: Integer;
        begin
          Result := YSChangeRole(RoleId, 1200, 11, 22, 33, 444, 555);
        end;

        function ProbeBodySlot15: Integer;
        begin
          Result := YSGetItemID(YSGetBodyItem(Owner, 15));
        end;

        function ProbeHeroWearWeight: Integer;
        begin
          Result := YSGetHeroShuXing(Owner, 22);
        end;

        begin
        end.
        """;
    var repositoryRoot = FindRepositoryRoot();
    var initializerPath = Path.Combine(repositoryRoot, "Envir", "PsMapQuest", "RunQuest.pas");
    var program = new PasParser(new PasLexer(source, initializerPath), repositoryRoot).Parse();
    var interpreter = new PasInterpreter(program, bridge);
    interpreter.ExecuteProcedure("initys");
    return interpreter;
}

static int RunInt(PasInterpreter interpreter, string procedure) =>
    interpreter.ExecuteProcedure(procedure).AsInt();

static string RunString(PasInterpreter interpreter, string procedure) =>
    interpreter.ExecuteProcedure(procedure).AsString();

static PluginManager LoadYanshenPlugin(string root)
{
    var runtime = Directory.CreateDirectory(Path.Combine(root, "GS1")).FullName;
    var envir = Directory.CreateDirectory(Path.Combine(root, "Envir")).FullName;
    File.WriteAllText(Path.Combine(runtime, "config.json"),
        "{\"眼神特殊函数\":1}", Encoding.GetEncoding(936));
    var manager = new PluginManager(envir, runtime);
    manager.RegisterBuiltinPlugins();
    Require(manager.LoadPlugin("YanshenCompat"),
        "YanshenCompat did not enter Running state");
    return manager;
}

static void InitializeGameState()
{
    M2Share.g_Config = new GameSvrConfig { nCheckBlock = 0 };
    M2Share.ObjectManager = new ObjectManager();
    M2Share.UserEngine = new UserEngine();
    M2Share.UserEngine.StdItemList.Add(new GoodItem
    {
        Name = "yanshen208-item",
        Outlook = 20803,
        DuraMax = 100,
        Weight = 1
    });
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.LogSystem = new MirLog();
}

static TPlayObject NewPlayer(string name) => new()
{
    m_sCharName = name,
    m_boObMode = true,
    m_boOffLineFlag = true
};

static void AddOnlinePlayer(TPlayObject player)
{
    var field = typeof(UserEngine).GetField("m_PlayObjectList",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Require(field != null, "UserEngine player list reflection target missing");
    var players = field.GetValue(M2Share.UserEngine) as IList<TPlayObject>;
    Require(players != null, "UserEngine player list has an unexpected type");
    players.Add(player);
}

static string FindRepositoryRoot()
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        var directory = new DirectoryInfo(start);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameSvr", "GameSvr.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
    }

    throw new DirectoryNotFoundException(
        "repository root containing GameSvr/GameSvr.csproj was not found");
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

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
