using System.Reflection;
using System.Runtime.CompilerServices;
using GameSvr;
using GameSvr.CommandSystem;
using SystemModule;

try
{
    PrepareRuntimeFiles();
    M2Share.g_Config = new GameSvrConfig { boTestServer = false };
    M2Share.g_Config.boShowPreFixMsg = false;
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.MapManager = new MapManager();

    var current = AddMap("CURRENT");
    var target = AddMap("TARGET");
    var player = NewPlayer(5, current);
    var command = CreateCommand();

    Equal("TempSetMapParam", command.GameCommand.Name, "command name");
    Equal(5, command.GameCommand.nPermissionMin, "native permission");

    command.Handle("TARGET PICKUP 1", player);
    Require(target.Flag.boPICKUP, "state 1 did not enable target PICKUP");
    Require(!current.Flag.boPICKUP,
        "command changed the player's current map instead of the named map");
    AssertLastMessage(player, "增加地图属性=PICKUP，操作成功",
        M2Share.g_Config.btBlueMsgFColor,
        M2Share.g_Config.btBlueMsgBColor, "enable success");

    player.m_MsgList.Clear();
    command.Handle("target PiCkUp 0", player);
    Require(!target.Flag.boPICKUP,
        "state 0 did not disable target PICKUP case-insensitively");
    AssertLastMessage(player, "取消地图属性=PiCkUp，操作成功",
        M2Share.g_Config.btBlueMsgFColor,
        M2Share.g_Config.btBlueMsgBColor, "disable success");

    target.Flag.boPICKUP = true;
    var messageCount = player.m_MsgList.Count;
    command.Handle("TARGET PICKUP -1", player);
    Require(target.Flag.boPICKUP, "negative state changed PICKUP");
    Equal(messageCount, player.m_MsgList.Count,
        "negative state must be silent like the native wrapper");
    command.Handle("TARGET PICKUP 2", player);
    Require(target.Flag.boPICKUP, "state 2 changed PICKUP");
    Equal(messageCount, player.m_MsgList.Count,
        "state 2 must be silent like the native wrapper");

    player.m_MsgList.Clear();
    command.Handle("TARGET CHECKQUEST 0", player);
    Require(target.Flag.boPICKUP,
        "unsupported attribute changed the implemented PICKUP flag");
    AssertLastMessage(player,
        "该GM命令目前不支持此地图属性=CHECKQUEST",
        M2Share.g_Config.btRedMsgFColor,
        M2Share.g_Config.btRedMsgBColor, "unsupported attribute");

    player.m_MsgList.Clear();
    command.Handle("MISSING PICKUP 0", player);
    Require(target.Flag.boPICKUP, "missing map changed target PICKUP");
    AssertLastMessage(player, "没找到地图 MISSING",
        M2Share.g_Config.btRedMsgFColor,
        M2Share.g_Config.btRedMsgBColor, "missing map");

    player.m_MsgList.Clear();
    command.Handle("TARGET PICKUP", player);
    AssertLastMessage(player,
        "命令格式：@TempSetMapParam 地图名 属性 [1|0] " +
        "1表示增加属性，0表示取消属性",
        M2Share.g_Config.btBlueMsgFColor,
        M2Share.g_Config.btBlueMsgBColor, "missing parameter help");

    var denied = NewPlayer(4, current);
    target.Flag.boPICKUP = false;
    var denial = command.Handle("TARGET PICKUP 1", denied);
    // 0x00622AB9 `cmp bl,3` / `jb 0x622B09`（<3 静默），0x00622AC4 `68 68 b7 62 00`
    // push 0x62B768("该命令需要", refcnt FF FF FF FF len 0A) -> 0x00622AD4 IntToStr ->
    // 0x00622ADF `68 7c b7 62 00` push 0x62B77C("级GM才能使用", len 0C) -> 0x00622AEF
    // LStrCatN(edx=3)。"权限不够!!!" 在底本里 0 命中。
    Equal("该命令需要5级GM才能使用", denial,
        "permission 4 production denial");
    Require(!target.Flag.boPICKUP,
        "permission 4 changed PICKUP on a production server");

    VerifySourceBoundary();
    Console.WriteLine(
        "PASS NativeTempSetMapParamPickupCheck case=577 ABI=map/attribute/state " +
        "target=named-map pickup=runtime-toggle permission=5");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        $"NativeTempSetMapParamPickupCheck FAIL: {exception}");
    return 1;
}

static TempSetMapParamCommand CreateCommand()
{
    var type = typeof(TempSetMapParamCommand);
    var attribute = type.GetCustomAttribute<GameCommandAttribute>() ??
                    throw new InvalidOperationException(
                        "TempSetMapParam GameCommand attribute missing");
    var method = type.GetMethod(nameof(TempSetMapParamCommand.TempSetMapParam)) ??
                 throw new MissingMethodException(type.FullName,
                     nameof(TempSetMapParamCommand.TempSetMapParam));
    var command = new TempSetMapParamCommand();
    command.Register(attribute, method);
    return command;
}

static TPlayObject NewPlayer(byte permission, Envirnoment environment)
{
    var player = (TPlayObject)RuntimeHelpers.GetUninitializedObject(
        typeof(TPlayObject));
    player.m_btPermission = permission;
    player.m_sMapName = environment.sMapName;
    player.m_PEnvir = environment;
    player.m_MsgList = new List<SendMessage>();
    return player;
}

static Envirnoment AddMap(string name)
{
    var field = typeof(MapManager).GetField("m_MapList",
        BindingFlags.Instance | BindingFlags.NonPublic) ??
                throw new MissingFieldException(typeof(MapManager).FullName,
                    "m_MapList");
    var maps = (Dictionary<string, Envirnoment>)field.GetValue(
        M2Share.MapManager);
    var environment = new Envirnoment { sMapName = name };
    maps.Add(name, environment);
    return environment;
}

static void AssertLastMessage(TPlayObject player, string expectedText,
    byte expectedForeground, byte expectedBackground, string scenario)
{
    Require(player.m_MsgList.Count > 0, scenario + " message missing");
    var message = player.m_MsgList[^1];
    Equal(Grobal2.RM_SYSMESSAGE, message.wIdent,
        scenario + " message ident");
    Equal(expectedText, message.Buff, scenario + " message text");
    Equal(expectedForeground, unchecked((byte)message.nParam1),
        scenario + " foreground");
    Equal(expectedBackground, unchecked((byte)message.nParam2),
        scenario + " background");
}

static void VerifySourceBoundary()
{
    var root = FindRepositoryRoot();
    var source = File.ReadAllText(Path.Combine(root, "GameSvr", "Command",
        "Commands", "TempSetMapParamCommand.cs"));
    Require(source.Contains("environment.Flag.boPICKUP = state == 1;",
            StringComparison.Ordinal),
        "command does not mutate the canonical runtime PICKUP flag");
    foreach (var forbidden in new[]
             {
                 "lyom2Release", "WriteAll", "File.Move", "Directory.Move",
                 "PasApiBridge"
             })
    {
        Require(!source.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
            "command crossed its runtime-only boundary: " + forbidden);
    }
}

static void PrepareRuntimeFiles()
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

static string FindRepositoryRoot()
{
    return AuditRepoRoot.Resolve();
}

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{message}: expected={expected}, actual={actual}");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
