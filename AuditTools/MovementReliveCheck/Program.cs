using System.Reflection;
using System.Runtime.Loader;
using System.Text;

var repositoryRoot = args.Length > 0 ? Path.GetFullPath(args[0]) : FindRepositoryRoot();
var gameDirectory = args.Length > 2 ? Path.GetFullPath(args[2]) : FindGameSvrBuild();
var clientRoot = args.Length > 1 ? Path.GetFullPath(args[1]) : FindClientRoot(repositoryRoot);
if (repositoryRoot == null || gameDirectory == null || clientRoot == null)
{
    Console.Error.WriteLine("INCOMPLETE: repository root=" + (repositoryRoot ?? "<not found>")
        + " client root=" + (clientRoot ?? "<not found>")
        + " GameSvr build=" + (gameDirectory ?? "<not found>") + ". "
        + "Usage: MovementReliveCheck [repository root] [plaintext client root] [GameSvr build]");
    Environment.Exit(2);
}

PrepareRuntimeConfig();


var coreGround = Read(Path.Combine(clientRoot, "core", "mir2.scenes.main.ground_hk.lua"));
var core64Ground = Read(Path.Combine(clientRoot, "core64", "mir2.scenes.main.ground_hk.lua"));
Assert(coreGround == core64Ground, "core/core64 ground_hk implementations differ");

var failStart = coreGround.IndexOf("elseif SM_ACT_FAIL == ident2 then", StringComparison.Ordinal);
var failEnd = coreGround.IndexOf("elseif SM_FIREON == ident2 then", failStart, StringComparison.Ordinal);
Assert(failStart >= 0 && failEnd > failStart, "SM_ACT_FAIL handler not found");
var failHandler = coreGround[failStart..failEnd];
Assert(failHandler.IndexOf("self.player:executeFail(x2, y2, dir2)", StringComparison.Ordinal) <
       failHandler.IndexOf("autoFindPath:research()", StringComparison.Ordinal),
    "path research runs before authoritative coordinate correction");
Assert(failHandler.Contains("autoFindPath.points", StringComparison.Ordinal) &&
       failHandler.Contains("#autoFindPath.points > 0", StringComparison.Ordinal),
    "SM_ACT_FAIL research is not limited to an active path");
Assert(failHandler.Contains("not autoRat.enableRat", StringComparison.Ordinal),
    "SM_ACT_FAIL research can alter auto-rat strategy");
Assert(failHandler.Contains("actFailResearchTime", StringComparison.Ordinal) &&
       failHandler.Contains(">= 0.25", StringComparison.Ordinal),
    "SM_ACT_FAIL research is not throttled");
Assert(Count(failHandler, "autoFindPath:research()") == 1,
    "SM_ACT_FAIL must issue at most one path research call");

var commandSource = Read(Path.Combine(repositoryRoot, "GameSvr", "Command", "Commands",
    "SetNoKillMapLvCommand.cs"));
Assert(commandSource.Contains("[GameCommand(\"SetNoKillMapLv\"", StringComparison.Ordinal) &&
       commandSource.Contains("\"等级\", 10)]", StringComparison.Ordinal),
    "SetNoKillMapLv GM permission contract changed");
Assert(commandSource.Contains("public override string Handle", StringComparison.Ordinal) &&
       commandSource.Contains("NativeCommandFailure.Report", StringComparison.Ordinal),
    "SetNoKillMapLv did not preserve the GM path while adding the client callback");
Assert(commandSource.Contains("playObject.m_boDeath", StringComparison.Ordinal) &&
       commandSource.Contains("PluginState.Running", StringComparison.Ordinal) &&
       commandSource.Contains("IsInitialized", StringComparison.Ordinal) &&
       commandSource.Contains("SetNoKillMapLv脚本触发", StringComparison.Ordinal),
    "ordinary-player callback gates are incomplete");
Assert(commandSource.Contains("TryCallProcedure", StringComparison.Ordinal) &&
       !commandSource.Contains("AutoReLive", StringComparison.Ordinal) &&
       !commandSource.Contains("ScriptRequestSubYBNum", StringComparison.Ordinal),
    "command bypasses RunQuest policy or hard-codes revival payment");

AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var dependency = Path.Combine(gameDirectory, $"{name.Name}.dll");
    return File.Exists(dependency)
        ? AssemblyLoadContext.Default.LoadFromAssemblyPath(dependency)
        : null;
};

var systemModule = AssemblyLoadContext.Default.LoadFromAssemblyPath(
    Path.Combine(gameDirectory, "SystemModule.dll"));
var gameSvr = AssemblyLoadContext.Default.LoadFromAssemblyPath(
    Path.Combine(gameDirectory, "GameSvr.dll"));
var shareType = gameSvr.GetType("GameSvr.M2Share", throwOnError: true)!;
var objectManagerType = gameSvr.GetType("GameSvr.ObjectManager", throwOnError: true)!;
var userEngineType = gameSvr.GetType("GameSvr.UserEngine", throwOnError: true)!;
var playerType = gameSvr.GetType("GameSvr.TPlayObject", throwOnError: true)!;
var baseObjectType = gameSvr.GetType("GameSvr.TBaseObject", throwOnError: true)!;
var pluginManagerType = gameSvr.GetType("GameSvr.Plugins.PluginManager", throwOnError: true)!;
var scriptHostType = gameSvr.GetType("GameSvr.PasEngine.PasScriptHost", throwOnError: true)!;
var commandType = gameSvr.GetType("GameSvr.SetNoKillMapLvCommand", throwOnError: true)!;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var tempRoot = Path.Combine(Path.GetTempPath(), "loym2-movement-relive-" + Guid.NewGuid().ToString("N"));
try
{
    var envir = Directory.CreateDirectory(Path.Combine(tempRoot, "Envir")).FullName;
    var mapQuest = Directory.CreateDirectory(Path.Combine(envir, "PsMapQuest")).FullName;
    Directory.CreateDirectory(Path.Combine(envir, "CommonScripts"));
    var runQuest = Path.Combine(mapQuest, "RunQuest.pas");
    var script =
        "procedure initys; begin end; " +
        "procedure SetNoKillMapLv(ID: Integer); begin {\u6d4b\u8bd5 GBK} " +
        "This_Player.AddGold(ID); end; begin end.";
    File.WriteAllText(runQuest, script, Encoding.GetEncoding("GBK"));
    File.WriteAllText(Path.Combine(tempRoot, "config.json"),
        "{\"SetNoKillMapLv\u811a\u672c\u89e6\u53d1\":0}", Encoding.GetEncoding("GBK"));

    var pluginManager = Activator.CreateInstance(pluginManagerType, new object[] { envir, null })!;
    pluginManagerType.GetMethod("RegisterBuiltinPlugins")!.Invoke(pluginManager, null);
    Assert((bool)pluginManagerType.GetMethod("LoadPlugin")!
        .Invoke(pluginManager, new object[] { "YanshenCompat" })!,
        "YanshenCompat did not enter Running state");
    SetStaticMember(shareType, "PluginManager", pluginManager);

    var scriptHost = Activator.CreateInstance(scriptHostType, envir)!;
    SetStaticMember(shareType, "PasEngine", scriptHost);
    SetStaticMember(shareType, "ObjectManager", Activator.CreateInstance(objectManagerType)!);
    SetStaticMember(shareType, "ProcessMsgCriticalSection", new object());
    SetStaticMember(shareType, "LogMsgCriticalSection", new object());
    SetStaticMember(shareType, "UserEngine", Activator.CreateInstance(userEngineType)!);

    var player = Activator.CreateInstance(playerType)!;
    SetMember(playerType, player, "m_boOffLineFlag", true);
    SetMember(baseObjectType, player, "m_btPermission", (byte)0);
    SetMember(baseObjectType, player, "m_boDeath", true);

    var command = Activator.CreateInstance(commandType)!;
    var attribute = commandType.GetCustomAttributes(inherit: true)
        .Single(item => item.GetType().FullName == "GameSvr.CommandSystem.GameCommandAttribute");
    var callback = commandType.GetMethod("SetNoKillMapLv", BindingFlags.Instance | BindingFlags.Public)!;
    commandType.BaseType!.GetMethod("Register")!.Invoke(command, new[] { attribute, callback });
    var handle = commandType.GetMethod("Handle", BindingFlags.Instance | BindingFlags.Public)!;

    string Invoke(string value) => (string)handle.Invoke(command, new object[] { value, player });
    int Gold() => (int)GetMember(baseObjectType, player, "m_nGold");

    Assert(!string.IsNullOrEmpty(Invoke("7")) && Gold() == 0,
        "disabled feature switch executed RunQuest");

    pluginManagerType.GetMethod("SetNativeConfigValue")!.Invoke(pluginManager,
        new object[] { "SetNoKillMapLv脚本触发", 1 });
    Assert(!string.IsNullOrEmpty(Invoke("7")) && Gold() == 0,
        "running but uninitialized plugin executed the revival callback");
    Assert((bool)scriptHostType.GetMethod("TryInitializeYanshen")!
        .Invoke(scriptHost, new[] { player })!,
        "RunQuest.initys did not initialize YanshenCompat");
    SetMember(baseObjectType, player, "m_boDeath", false);
    Assert(!string.IsNullOrEmpty(Invoke("7")) && Gold() == 0,
        "living ordinary player executed the revival callback");

    SetMember(baseObjectType, player, "m_boDeath", true);
    foreach (var invalid in new[] { "", "0", "-1", "+1", "1 2", "1.0", "abc" })
    {
        Assert(!string.IsNullOrEmpty(Invoke(invalid)) && Gold() == 0,
            $"invalid callback ID was accepted: '{invalid}'");
    }

    Assert(Invoke("7") == string.Empty && Gold() == 7,
        "valid dead-player callback did not execute GBK RunQuest.SetNoKillMapLv(ID)");

    File.Delete(runQuest);
    Assert(!string.IsNullOrEmpty(Invoke("8")) && Gold() == 7,
        "missing RunQuest was reported as callback success");

    SetMember(baseObjectType, player, "m_btPermission", (byte)10);
    Assert(string.IsNullOrEmpty(Invoke("9")) && Gold() == 7,
        "GM command was redirected through the ordinary-player callback");
}
finally
{
    try { Directory.Delete(tempRoot, recursive: true); } catch { }
}

Console.WriteLine(
    "PASS act-fail=active-path-only throttle=250ms auto-rat=isolated relive=gbk-script-gated fail-closed gm=permission10");

static string Read(string path) => File.ReadAllText(path);

static int Count(string text, string value)
{
    var count = 0;
    var offset = 0;
    while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
    {
        count++;
        offset += value.Length;
    }
    return count;
}

static object GetMember(Type type, object instance, string name)
{
    var member = FindMember(type, name);
    return member switch
    {
        FieldInfo field => field.GetValue(instance),
        PropertyInfo property => property.GetValue(instance),
        _ => throw new MissingMemberException(type.FullName, name)
    };
}

static void SetMember(Type type, object instance, string name, object value)
{
    var member = FindMember(type, name);
    switch (member)
    {
        case FieldInfo field:
            field.SetValue(instance, value);
            break;
        case PropertyInfo property:
            property.SetValue(instance, value);
            break;
        default:
            throw new MissingMemberException(type.FullName, name);
    }
}

static void SetStaticMember(Type type, string name, object value)
{
    var member = FindMember(type, name, BindingFlags.Static);
    switch (member)
    {
        case FieldInfo field:
            field.SetValue(null, value);
            break;
        case PropertyInfo property:
            property.SetValue(null, value);
            break;
        default:
            throw new MissingMemberException(type.FullName, name);
    }
}

static MemberInfo FindMember(Type type, string name,
    BindingFlags scope = BindingFlags.Instance)
{
    for (var current = type; current != null; current = current.BaseType)
    {
        var member = current.GetMember(name,
            scope | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .FirstOrDefault();
        if (member != null) return member;
    }
    throw new MissingMemberException(type.FullName, name);
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

// run_audits.py invokes every audit with no arguments, so a tool that hard-requires
// a GameSvr build directory reported FAIL without evaluating a single assertion.
// Falling back to the checkout's own build output keeps the assertions exactly as
// they were; when no build exists the tool exits 2 (INCOMPLETE) rather than
// pretending to have checked anything.
static string FindRepositoryRoot()
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        var current = new DirectoryInfo(start);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "GameSvr", "GameSvr.csproj")))
                return current.FullName;
            current = current.Parent;
        }
    }
    return null;
}

// The extracted client tree lives beside the repository rather than inside it, and the
// repository is routinely checked out through `git worktree` several levels deeper, so
// the anchor has to be searched for instead of computed from the root.
static string FindClientRoot(string repositoryRoot)
{
    const string clientTree = "白猪G2.5_0518_lua_plain_readable_20260710_014719";
    for (var directory = repositoryRoot == null ? null : new DirectoryInfo(repositoryRoot);
         directory != null; directory = directory.Parent)
    {
        var candidate = Path.Combine(directory.FullName, clientTree);
        if (File.Exists(Path.Combine(candidate, "core", "mir2.scenes.main.ground_hk.lua")))
            return candidate;
    }
    return null;
}

static string FindGameSvrBuild()
{
    var repositoryRoot = FindRepositoryRoot();
    if (repositoryRoot == null)
        return null;
    var binRoot = Path.Combine(repositoryRoot, "GameSvr", "bin");
    if (!Directory.Exists(binRoot))
        return null;
    var debug = $"{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}";
    foreach (var candidate in Directory
                 .EnumerateFiles(binRoot, "GameSvr.dll", SearchOption.AllDirectories)
                 // run_audits.py builds -c Debug, so prefer that configuration and
                 // then the freshest output within it.
                 .OrderByDescending(path => path.Contains(debug, StringComparison.OrdinalIgnoreCase))
                 .ThenByDescending(File.GetLastWriteTimeUtc))
    {
        var directory = Path.GetDirectoryName(candidate);
        if (directory != null && File.Exists(Path.Combine(directory, "SystemModule.dll")))
            return directory;
    }
    return null;
}
