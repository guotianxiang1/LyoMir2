using System.Reflection;
using System.Runtime.CompilerServices;
using GameSvr;
using SystemModule;

try
{
    PrepareRuntimeFiles();
    Equal(4633, Grobal2.CM_CLICK_BACKHOME, "CM_CLICK_BACKHOME constant");
    Equal(10330, Grobal2.RM_SPACEMOVE_FIRE, "RM_SPACEMOVE_FIRE constant");

    M2Share.g_Config = new GameSvrConfig();
    M2Share.ProcessMsgCriticalSection = new object();
    Equal(200, M2Share.g_Config.nPKPunishPoint,
        "PKPunishPoint default");

    var player = NewPlayer();
    AssertRejected(player, flag => flag.boBLACKROOM = true,
        "BLACKROOM", false);
    AssertRejected(NewPlayer(), flag => flag.boLIMITITEMMOVE = true,
        "LimitItemMove", false);
    AssertRejected(NewPlayer(), flag => flag.boFOXMAP = true,
        "FOXMAP", true);
    AssertFoxMapRuntimeMessage();

    // 0x6D9E33 mov dl,0x33 / 0x6D9E38 call sub_772960 / 0x6D9E3F je (allow) /
    // 0x6D9E48 cmp dword [eax+0x3c0],0 / 0x6D9E4F jne (reject): BOTH halves needed.
    player = NewPlayer();
    player.SetNativeActiveState(0x33);
    SetMountPartner(player, NewPlayer());
    AssertRejected(player, _ => { }, "state 0x33 with mount partner", false);

    // Solo mount (state 0x33, no partner) falls through the je at 0x6D9E3F -> allowed.
    player = NewPlayer();
    player.SetNativeActiveState(0x33);
    SetMountPartner(player, null);
    AssertDestination(player, "0", 289, 618,
        "state 0x33 without mount partner");

    // A partner pointer WITHOUT state 0x33 must not block: the cmp at 0x6D9E48 is
    // only reached when sub_772960(0x33) returned true.
    player = NewPlayer();
    SetMountPartner(player, NewPlayer());
    AssertDestination(player, "0", 289, 618,
        "mount partner without state 0x33");

    player = NewPlayer();
    player.SetNativeActiveState(0x34);
    AssertRejected(player, _ => { }, "state 0x34", false);

    player = NewPlayer();
    player.m_nPkPoint = 199;
    AssertDestination(player, "0", 289, 618, "normal home");
    player.m_nPkPoint = 200;
    AssertDestination(player, "3", 845, 674, "red home threshold");

    ApplyLimitItemMoveSourceContract();
    VerifySourceContract();

    Console.WriteLine(
        "PASS NativeBackHomeDispatchCheck command=4633 guards=BLACKROOM/" +
        "LimitItemMove/0x33/0x34/FOXMAP destinations=normal/red order=10330/move");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"NativeBackHomeDispatchCheck FAIL: {exception}");
    return 1;
}

static TPlayObject NewPlayer()
{
    var player = (TPlayObject)RuntimeHelpers.GetUninitializedObject(
        typeof(TPlayObject));
    player.m_PEnvir = new Envirnoment { Flag = new TMapFlag() };
    player.m_sHomeMap = "0";
    player.m_nHomeX = 289;
    player.m_nHomeY = 618;
    player.m_MsgList = new List<SendMessage>();
    return player;
}

static void AssertFoxMapRuntimeMessage()
{
    var player = NewPlayer();
    player.m_PEnvir.Flag.boFOXMAP = true;
    var method = typeof(TPlayObject).GetMethod("ClientClickBackHome",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(TPlayObject).FullName,
            "ClientClickBackHome");
    method.Invoke(player, null);
    Equal(1, player.m_MsgList.Count, "FOXMAP message count");
    Equal(Grobal2.RM_SYSMESSAGE, player.m_MsgList[0].wIdent,
        "FOXMAP message ident");
    Equal("在这里无法使用", player.m_MsgList[0].Buff,
        "FOXMAP exact message");
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

static void AssertRejected(TPlayObject player, Action<TMapFlag> configure,
    string label, bool expectedFoxMessage)
{
    configure(player.m_PEnvir.Flag);
    var result = Resolve(player);
    Equal(false, result.Allowed, label + " allowed");
    Equal(expectedFoxMessage, result.ShowFoxMapMessage,
        label + " FOXMAP message");
}

static void AssertDestination(TPlayObject player, string mapName, short x,
    short y, string label)
{
    var result = Resolve(player);
    Equal(true, result.Allowed, label + " allowed");
    Equal(mapName, result.MapName, label + " map");
    Equal(x, result.X, label + " X");
    Equal(y, result.Y, label + " Y");
    Equal(false, result.ShowFoxMapMessage, label + " FOXMAP message");
}

static (bool Allowed, string MapName, short X, short Y,
    bool ShowFoxMapMessage) Resolve(TPlayObject player)
{
    var method = typeof(TPlayObject).GetMethod(
        "TryResolveClientClickBackHome",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(TPlayObject).FullName,
            "TryResolveClientClickBackHome");
    object[] arguments = { null, (short)0, (short)0, false };
    var allowed = (bool)method.Invoke(player, arguments);
    return (allowed, (string)arguments[0], (short)arguments[1],
        (short)arguments[2], (bool)arguments[3]);
}

// 战神 [obj+0x3C0] is the two-seat mount PARTNER POINTER, not an int carrier: all 9
// writers live in the horse cluster (0x6EE398 / 0x6EE560 / 0x6EE8A0 / 0x6EEAA7 /
// 0x6EED51 / 0x6EEDF4 / 0x74BCD5) and readers dereference it as an actor -- 0x6C5A99
// `mov eax,[ebx+0x3c0]` then `lea edx,[eax+0x106]` copies the partner's NAME as a
// ShortString. This helper used to poke the unrelated int m_nNativeUnionActivationCarrier,
// which made the whole `state 0x33 && [+0x3C0] != 0` leg untestable (and, in the product
// code, dead). m_NativeHorsePartner is the field the horse subsystem actually maintains.
static void SetMountPartner(TPlayObject player, TPlayObject partner)
{
    var field = typeof(TPlayObject).GetField(
        "m_NativeHorsePartner",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(TPlayObject).FullName,
            "m_NativeHorsePartner");
    field.SetValue(player, partner);
}

static void ApplyLimitItemMoveSourceContract()
{
    var root = FindRepositoryRoot();
    var source = File.ReadAllText(Path.Combine(root, "GameSvr", "Maps",
        "Maps.cs"));
    // Native parser B @0x7769D2: mov ecx,0xD / mov edx,0x776F2C ("LimitItemMove")
    // / call 0x4C6E94 (CompareLStr). Same shape at parser A 0x775A42.
    // A full-string Equals is the wrong contract: native is a length-13 prefix.
    var token = source.IndexOf(
        "HUtil32.CompareLStr(s34, \"LimitItemMove\", \"LimitItemMove\".Length)",
        StringComparison.Ordinal);
    Assert(token >= 0, "LimitItemMove parser branch missing");
    var end = source.IndexOf("continue;", token, StringComparison.Ordinal);
    Assert(end > token, "LimitItemMove parser branch has no terminator");
    var branch = source.Substring(token, end - token);
    Assert(branch.Contains("MapFlag.boLIMITITEMMOVE = true;",
        StringComparison.Ordinal), "LimitItemMove independent flag missing");
    Assert(branch.Contains("MapFlag.boNORECALL = true;",
        StringComparison.Ordinal), "LimitItemMove NORECALL side effect missing");
    Assert(branch.Contains("MapFlag.boNORANDOMMOVE = true;",
        StringComparison.Ordinal),
        "LimitItemMove NORANDOMMOVE side effect missing");
    Assert(branch.Contains("MapFlag.boNOPOSITIONMOVE = true;",
        StringComparison.Ordinal),
        "LimitItemMove NOPOSITIONMOVE side effect missing");
}

static void VerifySourceContract()
{
    var root = FindRepositoryRoot();
    var dispatch = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.Message.cs"));
    Assert(dispatch.Contains("case Grobal2.CM_CLICK_BACKHOME:",
        StringComparison.Ordinal), "4633 dispatch branch missing");
    Assert(dispatch.Contains("ClientClickBackHome();", StringComparison.Ordinal),
        "4633 handler call missing");

    var handler = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
        "TPlayObject.NativeBackHome.cs"));
    Assert(handler.Contains("SysMsg(\"在这里无法使用\", MsgColor.Red, MsgType.Hint)",
        StringComparison.Ordinal), "FOXMAP exact message missing");
    var send = handler.IndexOf("SendRefMsg(Grobal2.RM_SPACEMOVE_FIRE",
        StringComparison.Ordinal);
    var move = handler.IndexOf("BaseObjectMove(mapName, x, y)",
        StringComparison.Ordinal);
    Assert(send >= 0 && move > send,
        "RM_SPACEMOVE_FIRE must precede the map move");

    var config = File.ReadAllText(Path.Combine(root, "GameSvr", "Configs",
        "ServerConfig.cs"));
    Assert(config.Contains("ReadInteger(\"Setup\", \"PKPunishPoint\"",
        StringComparison.Ordinal), "PKPunishPoint setup loading missing");
}

static string FindRepositoryRoot()
{
    return AuditRepoRoot.Resolve();
}

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{label}: expected={expected}, actual={actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
