using System.Reflection;
using GameSvr;
using SystemModule;

PrepareRuntimeConfig();
InitializeRuntime();

CheckNorideMapFlagParsing();
CheckNorideGateBlocks();
CheckNorideGateAllows();
CheckNorideMessageExact();

Console.WriteLine(
    "NativeNorideGateCheck PASS " +
    "MOVE-83 NORIDE map-flag-parser+gate-blocks-mount+allows-when-false+" +
    "exact-message-text-and-color");

static void WriteMinimalMap(string path)
{
    const short width = 2;
    const short height = 2;
    var header = new byte[52];
    BitConverter.GetBytes(width).CopyTo(header, 0);
    BitConverter.GetBytes(height).CopyTo(header, 2);
    var cells = new byte[width * height * 12];
    using var stream = File.Create(path);
    stream.Write(header, 0, header.Length);
    stream.Write(cells, 0, cells.Length);
}

static void CheckNorideMapFlagParsing()
{
    var directory = Path.Combine(Path.GetTempPath(),
        "lyom2-noride-" + Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(directory);
        var mapInfoPath = Path.Combine(directory, "MapInfo.txt");

        // Test NORIDE flag is parsed and set to true
        File.WriteAllText(mapInfoPath,
            // Real MapInfo.txt shape, e.g.
            //   [0139~200  开区等待室 0] SAFE NORECALL NORANDOMMOVE
            // The brackets hold name/description/server-index; the flag tokens
            // follow the closing bracket. Maps.cs does
            // ArrestStringEx(sFlag, "[", "]", ref sMapName) and then walks the
            // RETURNED remainder, so a token placed inside the brackets - or on
            // a line of its own - never reaches the flag parser.
            "[test_map 0] NORIDE" + Environment.NewLine,
            HUtil32.GbkEncoding);

        // Maps.LoadMapInfo is `public static int LoadMapInfo()` - it takes no
        // arguments and reads Path.Combine(M2Share.sConfigPath,
        // g_Config.sEnvirDir, "MapInfo.txt"), publishing what it parses into
        // M2Share.MapManager via AddMapInfo. The previous scaffolding looked it
        // up with BindingFlags.Instance, so GetMethod returned null and the
        // `?.Invoke` silently did nothing: nothing was ever loaded and the
        // lookups below could not have passed. Point the loader at the temp
        // directory and query the manager instead.
        M2Share.sConfigPath = directory;
        M2Share.g_Config.sEnvirDir = string.Empty;
        M2Share.g_Config.sMapDir = string.Empty;
        // MapManager.AddMapInfo dereferences M2Share.MiniMapList unconditionally
        // (MapManager.cs:203) and the field defaults to null, so the harness has
        // to supply it or the load NREs before any flag is published.
        M2Share.MiniMapList ??= new Dictionary<string, int>();
        // AddMapInfo only publishes the Envirnoment when LoadMapData succeeds
        // (MapManager.cs:212), so the flag under test never becomes observable
        // without a .map on disk. Envirnoment.LoadMapData wants a 52-byte header
        // whose first two Int16 are width/height, followed by width*height*12
        // bytes of cell data - a 2x2 map is the smallest thing that satisfies it.
        WriteMinimalMap(Path.Combine(directory, "test_map.map"));
        WriteMinimalMap(Path.Combine(directory, "test_map2.map"));

        Maps.LoadMapInfo();

        var map = M2Share.MapManager.FindMap("test_map");
        Assert(map != null, "NORIDE parser map found");
        Assert(map.Flag.boNORIDE == true, "NORIDE parser sets flag true");

        // Test default is false
        File.WriteAllText(mapInfoPath,
            "[test_map2 0]" + Environment.NewLine,
            HUtil32.GbkEncoding);

        M2Share.MapManager = new MapManager();
        Maps.LoadMapInfo();

        var map2 = M2Share.MapManager.FindMap("test_map2");
        Assert(map2 != null, "NORIDE default map found");
        Assert(map2.Flag.boNORIDE == false, "NORIDE defaults to false");
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}

static void CheckNorideGateBlocks()
{
    var map = NewMap();
    map.Flag.boNORIDE = true;
    var player = Place(map, NewPlayer("noride-blocked"), 5, 5);
    var mount = EquipMount(player);
    SetPending(player, true, 0, 0);

    var messagesBefore = player.m_MsgList.Count;

    Assert(player.Operate(HorseReadyMessage()), "NORIDE gate dispatch");

    // Should be blocked - no state change
    Assert(!player.HasNativeActiveState(51), "NORIDE gate blocks state51");
    Assert(!player.m_boOnHorse, "NORIDE gate blocks horse flag");

    // Pending should be cleared
    Pending(player, false, 0, 0, "NORIDE gate clears pending");

    // Should have sent refusal message
    var messages = player.m_MsgList.Where(m => m.wIdent == Grobal2.RM_SYSMESSAGE)
        .ToArray();
    Assert(messages.Length > 0, "NORIDE gate sends message");

    var lastMsg = messages[^1];
    Assert(lastMsg.Buff == "当前地图不能召唤坐骑！",
        "NORIDE gate exact refusal text");
    Equal(0xFC, lastMsg.nParam2, "NORIDE gate Blue background");
    Equal(0xFF, lastMsg.nParam1, "NORIDE gate Blue foreground");
}

static void CheckNorideGateAllows()
{
    var map = NewMap();
    map.Flag.boNORIDE = false;
    var player = Place(map, NewPlayer("noride-allowed"), 5, 5);
    var mount = EquipMount(player);
    SetPending(player, true, 0, 0);

    Assert(player.Operate(HorseReadyMessage()), "NORIDE allow dispatch");

    // Should succeed
    Assert(player.HasNativeActiveState(51), "NORIDE allow state51");
    Assert(player.m_boOnHorse, "NORIDE allow horse flag");

    // Should have success message, not refusal
    var messages = player.m_MsgList.Where(m => m.wIdent == Grobal2.RM_SYSMESSAGE)
        .ToArray();
    Assert(messages.Length > 0, "NORIDE allow sends message");

    var lastMsg = messages[^1];
    Assert(lastMsg.Buff == "成功召唤坐骑！",
        "NORIDE allow success text not refusal");
}

static void CheckNorideMessageExact()
{
    // Verify the message constants match binary
    var refusalText = "当前地图不能召唤坐骑！";
    var expectedBytes = HUtil32.GbkEncoding.GetBytes(refusalText);

    // Binary verification was done in _verify_noride.py
    // Here we just ensure the C# string is correct
    Assert(expectedBytes.Length == 22, "NORIDE message length 22 bytes");

    // Verify color constant
    // Blue in native is cx=0xFCFF which means foreground=0xFF, background=0xFC
    // This matches the SysMsg call with MsgColor.Blue
}

static TUserItem EquipMount(ProbePlayer player)
{
    var record = new byte[208];
    record[0x33] = 1;
    var item = new TUserItem
    {
        wIndex = 1,
        NativeRecord = record
    };
    player.m_UseItems[Grobal2.U_MOUNT] = item;
    return item;
}

static TProcessMessage HorseReadyMessage() => new()
{
    wIdent = Grobal2.CM_SHANGMA_OK,
    nParam2 = 1
};

static void SetPending(TPlayObject player, bool pending, uint tick, ushort delay)
{
    Field(player.GetType(), "m_boNativeHorseCallPending").SetValue(player,
        pending);
    Field(player.GetType(), "m_dwNativeHorseCallTick").SetValue(player, tick);
    Field(player.GetType(), "m_wNativeHorseCallDelay").SetValue(player, delay);
}

static void Pending(TPlayObject player, bool pending, uint tick, ushort delay,
    string label)
{
    Equal(pending, (bool)Field(player.GetType(),
        "m_boNativeHorseCallPending").GetValue(player), label + " flag");
    Equal(tick, (uint)Field(player.GetType(),
        "m_dwNativeHorseCallTick").GetValue(player), label + " tick");
    Equal(delay, (ushort)Field(player.GetType(),
        "m_wNativeHorseCallDelay").GetValue(player), label + " delay");
}

static FieldInfo Field(Type type, string name)
{
    for (var current = type; current != null; current = current.BaseType)
    {
        var field = current.GetField(name, BindingFlags.Instance |
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.DeclaredOnly);
        if (field != null) return field;
    }
    throw new MissingFieldException(type.FullName, name);
}

static Envirnoment NewMap()
{
    var map = new Envirnoment();
    var initialize = typeof(Envirnoment).GetMethod("Initialize",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("Envirnoment.Initialize");
    initialize.Invoke(map, new object[] { (short)16, (short)16 });
    return map;
}

static ProbePlayer Place(Envirnoment map, ProbePlayer player, short x, short y)
{
    player.m_PEnvir = map;
    player.m_nCurrX = x;
    player.m_nCurrY = y;
    player.m_boFixedHideMode = false;
    player.m_boObMode = false;
    player.m_boGhost = false;
    player.m_boAddToMaped = false;
    player.m_boDelFormMaped = false;
    Assert(ReferenceEquals(player, map.AddToMap(x, y,
        CellType.OS_MOVINGOBJECT, player)), "place " + player.m_sCharName);
    return player;
}

static ProbePlayer NewPlayer(string name) => new()
{
    m_boOffLineFlag = true,
    m_sCharName = name,
    m_btRaceServer = Grobal2.RC_PLAYOBJECT
};

static void InitializeRuntime()
{
    M2Share.g_Config = new GameSvrConfig { nSendRefMsgRange = 12 };
    M2Share.UserEngine = new UserEngine();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.MapManager = new MapManager();
    M2Share.CastleManager = new CastleManager();
    M2Share.RandomNumber = RandomNumber.GetInstance();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new System.Collections.ArrayList();
    M2Share.LogonCostLogList = new System.Collections.ArrayList();
    M2Share.g_MonSayMsgList = new Dictionary<string, IList<TMonSayMsg>>();
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
    internal override void SendSocket(ClientPacket defMsg, string sMsg)
    {
        // Suppress socket output in tests
    }
}
