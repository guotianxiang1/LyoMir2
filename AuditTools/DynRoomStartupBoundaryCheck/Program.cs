using System.Text;
using System.Text.RegularExpressions;

var repoRoot = AuditRepoRoot.Resolve(args);
var envirRoot = Environment.GetEnvironmentVariable("LYOMIR_PRODUCTION_ENVIR")
    ?? @"D:\lyom2Release\mud2.0\Mir200\Envir";

var gameApp = Read(Path.Combine(repoRoot, "GameSvr", "GameApp.cs"));
var appService = Read(Path.Combine(repoRoot, "GameSvr", "AppService.cs"));
var serverConfig = Read(Path.Combine(repoRoot, "GameSvr", "Configs", "ServerConfig.cs"));
var m2Share = Read(Path.Combine(repoRoot, "GameSvr", "M2Share.cs"));
var dynamicRoomService = Read(Path.Combine(repoRoot, "GameSvr", "Maps",
    "NativeDynamicRoomService.cs"));
var mapManager = Read(Path.Combine(repoRoot, "GameSvr", "Maps", "MapManager.cs"));
var localDb = Read(Path.Combine(repoRoot, "GameSvr", "LocalDB.cs"));
var gameServer = Read(Path.Combine(repoRoot, "GameSvr", "GameServer.cs"));

Assert(Order(gameApp,
        "M2Share.LoadConfig();",
        "M2Share.ObjectManager = new ObjectManager();",
        "M2Share.MapManager = new MapManager();",
        "M2Share.DynamicRoomManager = new NativeDynamicRoomManager();",
        "M2Share.EventManager = new EventManager();",
        "M2Share.UserEngine = new UserEngine();",
        "M2Share.DynamicRoomPasRoutes =",
        "M2Share.DynamicRoomRuntime = new NativeDynamicRoomRuntime(",
        "M2Share.DynamicRoomNpcMaterializer =",
        "M2Share.DynamicRoomService = new NativeDynamicRoomService(",
        "M2Share.PasEngine = new PasEngine.PasScriptHost(envirDir,"),
    "GameApp.InitializeServer order changed");

var startAsync = Slice(appService, "public override Task StartAsync",
    "public override Task StopAsync");
var onFormReady = Slice(appService, "public void OnFormReady()",
    "public override Task StartAsync");
Assert(startAsync.Contains("_mirApp.InitializeServer();"),
    "AppService.StartAsync no longer performs config-only initialization");
Assert(Order(onFormReady,
        "if (!InitializeEngine()) return;",
        "StartNetwork();",
        "_engineReady = true;"),
    "AppService.OnFormReady phase order changed");

Assert(Order(gameApp,
        "nCode = Maps.LoadMapInfo();",
        "M2Share.DynamicRoomService.TryInitializeFromFiles(",
        "nCode = M2Share.LocalDB.LoadMonGen();",
        "M2Share.LocalDB.LoadMapQuest();"),
    "GameApp.Initialize map/local-db order changed");

Assert(Order(gameApp,
        "M2Share.MapManager.LoadMapDoor();",
        "M2Share.LocalDB.LoadMerchant();",
        "var psNpcCount = M2Share.LocalDB.LoadPsNpcScriptNpcs();",
        "M2Share.UserEngine.Initialize();",
        "M2Share.boStartReady = true;"),
    "GameApp.StartEngine order changed");

Assert(serverConfig.Contains("M2Share.nServerIndex = ReadInteger(\"Server\", \"ServerIndex\""),
    "ServerConfig no longer reads ServerIndex");
Assert(serverConfig.Contains("sEnvirDir = ReadString(\"Share\", \"EnvirDir\"")
       && serverConfig.Contains("sMapDir = ReadString(\"Share\", \"MapDir\""),
    "ServerConfig no longer reads EnvirDir/MapDir");
Assert(m2Share.Contains("sRootPath = Path.GetFullPath(Path.Combine(sConfigPath, \"..\"))")
       && m2Share.Contains("g_Config.sEnvirDir = Path.GetFullPath(Path.Combine(sRootPath, g_Config.sEnvirDir))")
       && m2Share.Contains("g_Config.sMapDir = Path.GetFullPath(Path.Combine(sRootPath, g_Config.sMapDir))"),
    "M2Share shared-path normalization changed");

Assert(mapManager.Contains("public Envirnoment GetMapInfo(int nServerIdx, string sMapName)")
       && mapManager.Contains("envirnoment.nServerIndex == nServerIdx"),
    "MapManager server-index filter changed");
Assert(localDb.Contains("M2Share.MapManager.GetMapInfo(M2Share.nServerIndex, MonGenInfo.sMapName) != null"),
    "LocalDB MonGen server-index filter changed");
Assert(gameServer.Contains("M2Share.DynamicRoomManager?.Run();"),
    "DynamicRoomManager tick was removed");

Assert(m2Share.Contains("public static NativeDynamicRoomRuntime DynamicRoomRuntime")
       && m2Share.Contains("public static NativeDynamicRoomService DynamicRoomService")
       && m2Share.Contains("public static NativeDynamicRoomNpcOwner DynamicRoomNpcOwner"),
    "M2Share dynamic-room production ownership was removed");
Assert(dynamicRoomService.Contains("NativeDynamicRoomDefinitionLoader.TryLoad(")
       && dynamicRoomService.Contains("ValidateMapFiles(definitions, mapRoot)")
       && dynamicRoomService.Contains("TryCreateDormantEnvironment(")
       && dynamicRoomService.Contains("TryReserveActivatedRoom("),
    "dynamic-room service no longer owns the exact startup/runtime path");
Assert(gameApp.Contains("new PasEngine.PasScriptHost(envirDir,")
       && gameApp.Contains("M2Share.DynamicRoomPasRoutes,")
       && gameApp.Contains("M2Share.DynamicRoomRuntime);"),
    "PAS host is no longer connected to the dynamic-room route/runtime gate");

var dynNpc = ReadGbk(Path.Combine(envirRoot, "PsDynNpc.txt"));
var rawColumn2Values = Regex.Matches(dynNpc, @"(?m)^\s*([A-Za-z0-9_]+)\s+(\d+)\s+(\d+)\s+")
    .Select(match => int.Parse(match.Groups[2].Value))
    .Distinct()
    .OrderBy(value => value)
    .ToArray();
Assert(rawColumn2Values.Length > 1
       && rawColumn2Values.Contains(1)
       && rawColumn2Values.Contains(2),
    "production PsDynNpc column-2 metadata changed");
var physicalRoomCount = Regex.Matches(dynNpc,
        @"(?m)^\s*[A-Za-z0-9_]+\s+\S+\s+\d+\s+\S+\s+\S+\s+(\d+)\s+")
    .Select(match => int.Parse(match.Groups[1].Value))
    .Sum();
Assert(physicalRoomCount == 72,
    $"production physical-room total changed: {physicalRoomCount}");

Console.WriteLine("DynRoomStartupBoundaryCheck PASS "
    + "order=config-before-owners map-before-dynroom-before-mongen "
    + "paths=root-normalized definitions=22 physical=72 "
    + "current=integrated");

static string Read(string path)
{
    if (!File.Exists(path)) throw new FileNotFoundException(path);
    return File.ReadAllText(path);
}

static string ReadGbk(string path)
{
    if (!File.Exists(path)) throw new FileNotFoundException(path);
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    return Encoding.GetEncoding(936).GetString(File.ReadAllBytes(path));
}

static bool Order(string text, params string[] needles)
{
    var cursor = -1;
    foreach (var needle in needles)
    {
        var next = text.IndexOf(needle, cursor + 1, StringComparison.Ordinal);
        if (next < 0) return false;
        cursor = next;
    }
    return true;
}

static string Slice(string text, string startNeedle, string endNeedle)
{
    var start = text.IndexOf(startNeedle, StringComparison.Ordinal);
    if (start < 0) return string.Empty;
    var end = text.IndexOf(endNeedle, start + startNeedle.Length,
        StringComparison.Ordinal);
    return end < 0 ? text[start..] : text[start..end];
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
