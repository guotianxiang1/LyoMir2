using System.Reflection;
using GameSvr;
using SystemModule;

PrepareRuntimeConfig();
InitializeRuntime();
CheckConstants();
CheckInviteAcceptAndPacket();
CheckRejectRestoresFlags();
CheckStaleMapObjectRejected();
CheckMapCellScanUsesCallerWindow();

Console.WriteLine("NativeHorsePairProtocolCheck PASS " +
    "4109=state51/pair-ready/range/gender+3417 " +
    "4110=reject-restore+accept-state52+reciprocal-partner+3418/68B");

static void CheckConstants()
{
    Equal(4109, Grobal2.CM_YAOQING_SHANGMA, "CM_YAOQING_SHANGMA");
    Equal(4110, Grobal2.CM_INVITE_HORSE, "CM_INVITE_HORSE");
    Equal(3417, Grobal2.SM_INVITE_HORSE, "SM_INVITE_HORSE");
    Equal(3418, Grobal2.SM_SHANGMA_OK2, "SM_SHANGMA_OK2");
}

static void CheckInviteAcceptAndPacket()
{
    var map = NewMap();
    var driver = Place(map, NewPlayer("pair-driver"), 5, 5);
    var passenger = Place(map, NewPlayer("pair-passenger"), 6, 5);
    var observer = Place(map, NewPlayer("pair-observer"), 5, 7);
    driver.SetNativeActiveState(51);
    driver.m_boOnHorse = true;
    driver.m_btHorseType = 7;
    driver.m_btDirection = Grobal2.DR_RIGHT;
    passenger.SetNativeActiveState(23);
    SetField(driver, "m_boNativeHorsePairReady", true);
    var mount = EquipMount(driver, 7);
    mount.wIndex = 0;

    Assert(driver.Operate(Invite(passenger.ObjectId)), "4109 dispatch");
    var invite = passenger.m_DefMsg;
    Packet(invite, 3417, driver.ObjectId, 0, 0, 0, "3417 invite");
    Assert(!GetBool(driver, "m_boNativeHorsePairReady"),
        "4109 driver pair-ready clear");
    Assert(GetBool(passenger, "m_boNativeHorsePassengerActive"),
        "4109 passenger active set");

    // sub_6BBEE4 uses sub_779CD8, which relocates the existing node without
    // checking the target terrain byte.
    map.SetMapXYFlag(driver.m_nCurrX, driver.m_nCurrY, false);
    Assert(passenger.Operate(Response(driver.ObjectId, 1)),
        "4110 accept dispatch");
    Assert(passenger.HasNativeActiveState(52), "4110 passenger state52");
    Equal(null, FindTimedNode(passenger, 52),
        "4110 passenger state52 must not create timed node");
    Assert(!passenger.HasNativeActiveState(23),
        "4110 overlap removes movement state23");
    Assert(passenger.m_boOnHorse, "4110 passenger horse feature flag");
    Equal(driver, GetPartner(passenger), "4110 passenger partner");
    Equal(passenger, GetPartner(driver), "4110 driver partner");
    Equal((byte)7, passenger.m_btHorseType, "4110 passenger horse type");
    Equal((byte)7, driver.m_btHorseType, "4110 driver horse type");
    Equal(driver.m_btDirection, passenger.m_btDirection,
        "4110 passenger direction");
    Equal(driver.m_nCurrX, passenger.m_nCurrX, "4110 overlap x");
    Equal(driver.m_nCurrY, passenger.m_nCurrY, "4110 overlap y");
    Assert(GetBool(driver, "m_boNativeHorsePairReady"),
        "4110 driver pair-ready restore");
    Assert(!GetBool(passenger, "m_boNativeHorsePassengerActive"),
        "4110 passenger active clear");

    var packets = TakeAll(observer, Grobal2.RM_NATIVE_SHANGMA_OK2);
    Equal(2, packets.Length, "3418 pair packet count");
    Assert(packets.Any(message => message.BaseObject == driver.ObjectId),
        "3418 driver source");
    Assert(packets.Any(message => message.BaseObject == passenger.ObjectId),
        "3418 passenger source");
    foreach (var message in packets)
    {
        Equal(1, message.wParam, "3418 param");
        Equal(68, message.nParam1, "3418 length header");
        Equal(HUtil32.MakeWord(7, 0), message.nParam3, "3418 series");
        var body = message.Payload as byte[];
        Assert(body != null && body.Length == 68, "3418 body length");
        Equal(driver.ObjectId, BitConverter.ToInt32(body, 0),
            "3418 driver id");
        Equal(passenger.ObjectId, BitConverter.ToInt32(body, 4),
            "3418 passenger id");
        Equal((int)driver.m_nCurrX, BitConverter.ToInt32(body, 8),
            "3418 x");
        Equal((int)driver.m_nCurrY, BitConverter.ToInt32(body, 12),
            "3418 y");
        Equal(driver.m_btDirection, body[16], "3418 direction");
        var owner = message.BaseObject == driver.ObjectId
            ? driver
            : passenger;
        var ownerName = HUtil32.GbkEncoding.GetBytes(owner.GetShowName());
        var nameLength = Math.Min(40, ownerName.Length);
        Equal((byte)nameLength, body[17], "3418 name length");
        Assert(body.AsSpan(18, nameLength).SequenceEqual(
            ownerName.AsSpan(0, nameLength)), "3418 owner name");
    }
}

static void CheckRejectRestoresFlags()
{
    var map = NewMap();
    var driver = Place(map, NewPlayer("reject-driver"), 5, 5);
    var passenger = Place(map, NewPlayer("reject-passenger"), 6, 5);
    driver.SetNativeActiveState(51);
    driver.m_boOnHorse = true;
    SetField(driver, "m_boNativeHorsePairReady", true);
    EquipMount(driver, 7);

    Assert(driver.Operate(Invite(passenger.ObjectId)), "4109 reject invite");
    Assert(passenger.Operate(Response(driver.ObjectId, 0)),
        "4110 reject dispatch");
    Assert(GetBool(driver, "m_boNativeHorsePairReady"),
        "4110 reject pair-ready restore");
    Assert(!GetBool(passenger, "m_boNativeHorsePassengerActive"),
        "4110 reject passenger active clear");
    Equal(null, GetPartner(driver), "4110 reject driver partner");
    Equal(null, GetPartner(passenger), "4110 reject passenger partner");
    Assert(!passenger.HasNativeActiveState(52),
        "4110 reject no state52");
    var rejection = driver.m_MsgList.Last(message =>
        message.wIdent == Grobal2.RM_SYSMESSAGE);
    Equal(0xFF, rejection.nParam1, "4110 reject foreground");
    Equal(0xFC, rejection.nParam2, "4110 reject background");
    Equal("对方拒绝上马邀请", rejection.Buff,
        "4110 reject exact CP936 text");
}

static void CheckStaleMapObjectRejected()
{
    var map = NewMap();
    var driver = Place(map, NewPlayer("stale-driver"), 5, 5);
    var passenger = Place(map, NewPlayer("stale-passenger"), 6, 5);
    driver.SetNativeActiveState(51);
    driver.m_boOnHorse = true;
    SetField(driver, "m_boNativeHorsePairReady", true);
    Equal(1, map.DeleteFromMap(passenger.m_nCurrX, passenger.m_nCurrY,
        CellType.OS_MOVINGOBJECT, passenger), "4109 stale map removal");

    Assert(driver.Operate(Invite(passenger.ObjectId)),
        "4109 stale target dispatch");
    Assert(GetBool(driver, "m_boNativeHorsePairReady"),
        "4109 stale target keeps pair-ready");
    Assert(!GetBool(passenger, "m_boNativeHorsePassengerActive"),
        "4109 stale target does not activate passenger");
    Assert(passenger.m_DefMsg == null ||
        passenger.m_DefMsg.Ident != Grobal2.SM_INVITE_HORSE,
        "4109 stale target no 3417");
}

static void CheckMapCellScanUsesCallerWindow()
{
    var map = NewMap();
    var driver = Place(map, NewPlayer("scan-driver"), 5, 5);
    var passenger = Place(map, NewPlayer("scan-passenger"), 8, 5);
    driver.SetNativeActiveState(51);
    driver.m_boOnHorse = true;
    SetField(driver, "m_boNativeHorsePairReady", true);

    // Native sub_76CA7C scans the caller's radius-4 cells for the exact
    // pointer; it does not assume the target is in its reported coord cell.
    passenger.m_nCurrX = 6;
    passenger.m_nCurrY = 5;

    Assert(driver.Operate(Invite(passenger.ObjectId)),
        "4109 caller-window scan dispatch");
    Packet(passenger.m_DefMsg, 3417, driver.ObjectId, 0, 0, 0,
        "4109 caller-window scan invite");
    Assert(!GetBool(driver, "m_boNativeHorsePairReady"),
        "4109 caller-window scan clears pair-ready");
    Assert(GetBool(passenger, "m_boNativeHorsePassengerActive"),
        "4109 caller-window scan activates passenger");
}

static TProcessMessage Invite(int targetId) => new()
{
    wIdent = Grobal2.CM_YAOQING_SHANGMA,
    nParam1 = targetId
};

static TProcessMessage Response(int driverId, int accept) => new()
{
    wIdent = Grobal2.CM_INVITE_HORSE,
    wParam = accept,
    nParam1 = driverId
};

static TUserItem EquipMount(TPlayObject player, byte type)
{
    var record = new byte[208];
    record[0x33] = type;
    player.m_UseItems[Grobal2.U_MOUNT] = new TUserItem
    {
        wIndex = 1,
        NativeRecord = record
    };
    return player.m_UseItems[Grobal2.U_MOUNT];
}

static object FindTimedNode(TBaseObject actor, byte internalType)
{
    var node = GetField(actor, "m_TimedAbilityHead");
    while (node != null)
    {
        if ((byte)Field(node.GetType(), "InternalType").GetValue(node) ==
            internalType)
        {
            return node;
        }
        node = Field(node.GetType(), "Next").GetValue(node);
    }
    return null;
}

static TProcessMessage[] TakeAll(ProbePlayer player, int ident)
{
    var result = new List<TProcessMessage>();
    TProcessMessage message = null;
    while (player.TryTake(ref message))
    {
        if (message.wIdent == ident) result.Add(message);
    }
    return result.ToArray();
}

static TPlayObject GetPartner(TPlayObject player) =>
    (TPlayObject)GetField(player, "m_NativeHorsePartner");

static bool GetBool(TPlayObject player, string name) =>
    (bool)GetField(player, name);

static object GetField(object owner, string name) =>
    Field(owner.GetType(), name).GetValue(owner);

static void SetField(object owner, string name, object value) =>
    Field(owner.GetType(), name).SetValue(owner, value);

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

static void Packet(ClientPacket packet, int ident, int recog, int param,
    int tag, int series, string label)
{
    Equal(unchecked((ushort)ident), packet.Ident, label + " ident");
    Equal(recog, packet.Recog, label + " recog");
    Equal(unchecked((ushort)param), packet.Param, label + " param");
    Equal(unchecked((ushort)tag), packet.Tag, label + " tag");
    Equal(unchecked((ushort)series), packet.Series, label + " series");
}

static Envirnoment NewMap()
{
    // sMapName 必须非空：SPWN-56 的有效性谓词第三项对应原生
    // 0x765D85 `cmp dword [eax+0x44],0`（PEnvir.MapName <> ''），
    // 空名地图上的 actor 会在首次视野扫描时被判失效摘链。
    // 生产地图一律经 Maps.cs:77（拒绝空名）或动态房间工厂
    // （sMapName = definition.RoomName）建立，裸 new 是夹具特有的失真态。
    var map = new Envirnoment { sMapName = "0" };
    var initialize = typeof(Envirnoment).GetMethod("Initialize",
        BindingFlags.Instance | BindingFlags.NonPublic);
    initialize.Invoke(map, new object[] { (short)16, (short)16 });
    return map;
}

static ProbePlayer Place(Envirnoment map, ProbePlayer player, short x,
    short y)
{
    player.m_PEnvir = map;
    player.m_nCurrX = x;
    player.m_nCurrY = y;
    player.m_boFixedHideMode = false;
    player.m_boObMode = false;
    player.m_boGhost = false;
    Assert(ReferenceEquals(player, map.AddToMap(x, y,
        CellType.OS_MOVINGOBJECT, player)), "place " + player.m_sCharName);
    return player;
}

static ProbePlayer NewPlayer(string name) => new()
{
    m_boOffLineFlag = true,
    m_sCharName = name,
    m_btRaceServer = Grobal2.RC_PLAYOBJECT,
    m_btGender = PlayGender.Man
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
    public bool TryTake(ref TProcessMessage message) => GetMessage(ref message);
}
