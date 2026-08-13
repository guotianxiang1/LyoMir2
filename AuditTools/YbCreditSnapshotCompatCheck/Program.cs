using System.Reflection;
using GameSvr;
using SystemModule;
using SystemModule.Packet;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.ObjectManager = new ObjectManager();
M2Share.ProcessMsgCriticalSection ??= new object();

var tests = new (string Name, Action Run)[]
{
    ("independent snapshot state", CheckIndependentSnapshotState),
    ("initial and refresh query ids", CheckRequestQueryIds),
    ("initial retry admission", CheckInitialRetryAdmission),
    ("refresh admission and wraparound", CheckRefreshAdmission),
    ("positive increase message order", CheckIncreaseMessageOrder),
    ("response Param deal gate", CheckResponseParamGate),
    ("native deal packet order", CheckDealPacketOrder)
};

foreach (var test in tests)
{
    try
    {
        test.Run();
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException($"{test.Name}: {ex.Message}", ex);
    }
}

Console.WriteLine(
    $"YbCreditSnapshotCompatCheck PASS tests={tests.Length} " +
    "response=1103 refresh=10054 deals=3009,3010");
return;

static void CheckRequestQueryIds()
{
    var identity = new YbDbLegacy77Identity
    {
        Field0 = "ptid",
        Field11 = "ptid",
        RoleName = "角色",
        Field48 = "192.0.2.1"
    };
    Assert(YbDbCreditProtocol.TryCreateInitialRequest(identity, 0x3456, true,
        out var initial, out var initialError), initialError);
    Equal(0, initial.QueryId, "initial QueryId");
    Equal(0x13456, initial.Param, "initial Param");

    Assert(YbDbCreditProtocol.TryCreateRefreshRequest(identity, 0x3456, false,
        out var refresh, out var refreshError), refreshError);
    Equal(1, refresh.QueryId, "refresh QueryId");
    Equal(0x3456, refresh.Param, "refresh Param");
}

static void CheckInitialRetryAdmission()
{
    var player = NewPlayer();
    player.bo6AB = true;
    player.BeginNativeYbCreditLoad(1_000);
    Assert(!player.m_boNativeYbAccountLoaded,
        "disconnected reconnect login marked credit loaded");
    Equal(1_000u, player.m_dwNativeYbInitialRetryTick,
        "initial retry tick");
    Equal(1_000u, player.m_dwNativeYbRefreshTick,
        "initial refresh tick");

    player.RunNativeYbCreditLoad(15_999);
    Equal(1_000u, player.m_dwNativeYbInitialRetryTick,
        "sub-15-second retry changed tick");
    player.RunNativeYbCreditLoad(16_000);
    Equal(16_000u, player.m_dwNativeYbInitialRetryTick,
        "15-second disconnected retry did not advance tick");
    Assert(!player.m_boNativeYbAccountLoaded,
        "disconnected periodic retry marked credit loaded");

    player.m_boNativeYbAccountLoaded = true;
    player.RunNativeYbCreditLoad(31_000);
    Equal(31_000u, player.m_dwNativeYbInitialRetryTick,
        "loaded periodic pass did not advance tick");
    Assert(player.m_boNativeYbAccountLoaded,
        "loaded periodic pass cleared loaded state");
}

static void CheckIndependentSnapshotState()
{
    var player = NewPlayer();
    player.m_nGameGold = 7;
    player.m_CreditCard.Loaded = true;
    player.m_CreditCard.Value = 11;
    player.m_CreditCard.Value2 = 13;
    player.m_CreditCard.UsedValue = 17;
    player.m_dwNativeYbRefreshTick = 1234;

    player.ApplyNativeYb1103Snapshot(101, 202, 303, 404, false);

    Equal(101, player.m_nGameGold, "current yuanbao");
    Equal(202, player.m_nNativeYbTotalConsumed, "total consumed");
    Equal(303, player.m_nNativeYbRemainingSeconds, "remaining seconds");
    Equal(404, player.m_nNativeYbDividendConsumed, "dividend consumed");
    Assert(player.m_boNativeYbAccountLoaded, "snapshot did not set loaded");
    Equal(1234u, player.m_dwNativeYbRefreshTick,
        "snapshot changed refresh tick");
    Assert(!player.m_boNativeYbDealOpened,
        "Param!=1 opened the YB deal state");

    Assert(player.m_CreditCard.Loaded, "snapshot changed CreditCard.Loaded");
    Equal(11, player.m_CreditCard.Value, "CreditCard.Value");
    Equal(13, player.m_CreditCard.Value2, "CreditCard.Value2");
    Equal(17, player.m_CreditCard.UsedValue, "CreditCard.UsedValue");

    Equal(1, player.m_MsgList.Count, "snapshot message count");
    Equal(Grobal2.RM_LINGFU_CHANGED, player.m_MsgList[0].wIdent,
        "snapshot refresh ident");
}

static void CheckRefreshAdmission()
{
    var player = NewPlayer();
    player.m_dwNativeYbRefreshTick = 100;
    Assert(!player.TryBeginNativeYbCreditRefresh(10_100),
        "unloaded account admitted refresh");
    Equal(100u, player.m_dwNativeYbRefreshTick,
        "rejected unloaded refresh changed tick");

    player.m_boNativeYbAccountLoaded = true;
    Assert(!player.TryBeginNativeYbCreditRefresh(10_099),
        "sub-10-second refresh was admitted");
    Assert(player.m_boNativeYbAccountLoaded,
        "throttled refresh cleared loaded state");

    Assert(player.TryBeginNativeYbCreditRefresh(10_100),
        "10-second boundary refresh was rejected");
    Assert(!player.m_boNativeYbAccountLoaded,
        "admitted refresh did not clear loaded state");
    Equal(10_100u, player.m_dwNativeYbRefreshTick,
        "admitted refresh tick");

    player.m_boNativeYbAccountLoaded = true;
    player.m_dwNativeYbRefreshTick = uint.MaxValue - 4_000;
    Assert(player.TryBeginNativeYbCreditRefresh(6_000),
        "UInt32 tick-wrap refresh was rejected");
    Equal(6_000u, player.m_dwNativeYbRefreshTick,
        "tick-wrap admission tick");
}

static void CheckIncreaseMessageOrder()
{
    var player = NewPlayer();
    player.m_boNativeYbAccountLoaded = true;
    player.m_nGameGold = 40;

    player.ApplyNativeYb1103Snapshot(55, 1, 2, 3, false);

    Equal(2, player.m_MsgList.Count, "increase message count");
    var increase = player.m_MsgList[0];
    Equal(Grobal2.RM_SYSMESSAGE, increase.wIdent,
        "increase message ident");
    Equal(0xFF, increase.nParam1, "increase foreground");
    Equal(0x38, increase.nParam2, "increase background");
    Equal("15 个元宝增加", increase.Buff, "increase text");
    Equal(Grobal2.RM_LINGFU_CHANGED, player.m_MsgList[1].wIdent,
        "10054 did not follow the increase hint");
}

static void CheckResponseParamGate()
{
    var take = RequiredMethod("TakeNativeYbDealPackets",
        BindingFlags.Instance | BindingFlags.NonPublic);
    var player = NewPlayer();

    Equal(0, InvokePackets(take, player, false).Length,
        "Param!=1 produced deal packets");
    Assert(!player.m_boNativeYbDealOpened,
        "Param!=1 consumed the one-shot state");

    Equal(2, InvokePackets(take, player, true).Length,
        "first Param==1 packet count");
    Assert(player.m_boNativeYbDealOpened,
        "first Param==1 did not consume one-shot state");
    Equal(0, InvokePackets(take, player, true).Length,
        "repeated Param==1 produced deal packets");

    var applyPlayer = NewPlayer();
    applyPlayer.m_boOffLineFlag = true;
    applyPlayer.ApplyNativeYb1103Snapshot(1, 2, 3, 4, true);
    Assert(applyPlayer.m_boNativeYbDealOpened,
        "Apply1103 did not execute Param==1 gate");
    Equal(1, applyPlayer.m_MsgList.Count,
        "Param==1 changed the 10054 queue count");
    Equal(Grobal2.RM_LINGFU_CHANGED, applyPlayer.m_MsgList[0].wIdent,
        "Param==1 did not queue 10054 before deal dispatch");
}

static void CheckDealPacketOrder()
{
    var build = RequiredMethod("BuildNativeYbDealPackets",
        BindingFlags.Static | BindingFlags.NonPublic);
    var packets = (ClientPacket[])build.Invoke(null, new object[] { (ushort)0x1234 })!;

    Equal(2, packets.Length, "deal packet count");
    AssertPacket(packets[0], 3009, 0, 0, 0, 0, "open deal packet");
    AssertPacket(packets[1], 3010, 0, 0x1234, 0, 0,
        "protect packet");
}

static TPlayObject NewPlayer()
{
    var player = new TPlayObject();
    player.m_MsgList.Clear();
    Equal((ushort)100, player.m_wNativeYbDealProtect,
        "native default deal protection");
    return player;
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

static MethodInfo RequiredMethod(string name, BindingFlags flags)
{
    return typeof(TPlayObject).GetMethod(name, flags)
           ?? throw new InvalidOperationException($"missing method {name}");
}

static ClientPacket[] InvokePackets(MethodInfo method, TPlayObject player,
    bool responseParamIsOne)
{
    return (ClientPacket[])method.Invoke(player,
        new object[] { responseParamIsOne })!;
}

static void AssertPacket(ClientPacket packet, int ident, int recog, int param,
    int tag, int series, string label)
{
    Equal((ushort)ident, packet.Ident, label + " Ident");
    Equal(recog, packet.Recog, label + " Recog");
    Equal((ushort)param, packet.Param, label + " Param");
    Equal((ushort)tag, packet.Tag, label + " Tag");
    Equal((ushort)series, packet.Series, label + " Series");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{label}: expected={expected}, actual={actual}");
}
