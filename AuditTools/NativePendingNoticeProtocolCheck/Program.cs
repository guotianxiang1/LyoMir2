using System.Reflection;
using System.Text;
using GameSvr;
using GameSvr.Services;
using SystemModule;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.UserEngine = new UserEngine();
M2Share.ObjectManager = new ObjectManager();
M2Share.MapManager = new MapManager();
M2Share.StartPointList = new List<TStartPoint>();
M2Share.SafeZoneList = new List<TSafeZoneArea>();
M2Share.ProcessMsgCriticalSection = new object();

try
{
    CheckCodec();
    CheckLedgerOrderAndDrain();
    CheckCorpsRefuseQueuesType1();
    CheckJoinGildRefuseQueuesType2();
    CheckUnionRefuseQueuesType3();
    CheckLoginDrainEmitsAndConsumes();
    Console.WriteLine(
        "PASS NativePendingNoticeProtocolCheck SM4612=17-byte " +
        "type+ShortString15 empty-send=true refuse-tags=1/2/3 " +
        "fifo=preserved login-drain=atomic");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("NativePendingNoticeProtocolCheck FAIL: " + ex);
    return 1;
}

static void CheckCodec()
{
    Equal(0, NativeCorpsWireCodec.EncodePendingNotices(null).Length,
        "null notice list is an empty body");
    Equal(0, NativeCorpsWireCodec.EncodePendingNotices(
        Array.Empty<NativeGildOfflineNotice>()).Length,
        "empty notice list is an empty body");

    var notices = new[]
    {
        new NativeGildOfflineNotice
        {
            NoticeType = NativeGildOfflineNotice.JoinGildRefuseType,
            Text = "行会甲"
        },
        new NativeGildOfflineNotice
        {
            NoticeType = NativeGildOfflineNotice.UnionRefuseType,
            Text = "0123456789"
        }
    };
    var body = NativeCorpsWireCodec.EncodePendingNotices(notices);
    Equal(34, body.Length, "two records are count*17");
    Equal(2, body[0], "first type byte");
    Equal(6, body[1], "GBK byte length for 行会甲");
    BytesEqual(HUtil32.GbkEncoding.GetBytes("行会甲"),
        body.AsSpan(2, body[1]).ToArray(), "first GBK payload");
    Equal(0, body[1 + 1 + body[1]],
        "first short-string slot is zero padded");
    Equal(3, body[17], "second type byte");
    Equal(10, body[18], "second short-string length");
    for (var i = 29; i < 34; i++)
        Equal(0, body[i], "record padding is zero");
}

static void CheckLedgerOrderAndDrain()
{
    var ledger = new NativeGildRequestLedger();
    ledger.EnqueueNotice(7, new NativeGildOfflineNotice
    {
        NoticeType = 1, Text = "a"
    });
    ledger.EnqueueNotice(7, new NativeGildOfflineNotice
    {
        NoticeType = 2, Text = "b"
    });
    Equal(2, ledger.PendingNoticeCount(7), "ledger count before drain");
    var taken = ledger.TakeNotices(7);
    Equal(2, taken.Count, "ledger drains all records");
    Equal(1, taken[0].NoticeType, "ledger FIFO first");
    Equal(2, taken[1].NoticeType, "ledger FIFO second");
    Equal(0, ledger.PendingNoticeCount(7), "ledger count after drain");
    Equal(0, ledger.TakeNotices(7).Count, "second drain is empty");
}

static void CheckJoinGildRefuseQueuesType2()
{
    var service = NewService();
    Equal(0, service.ApplyGildRequestJoin(9, 200),
        "join-gild request creation");
    Equal(0, service.ApplyGildRefuseRequest(1, 1),
        "join-gild refusal succeeds");
    Equal(1, service.PendingNoticeCount(9),
        "join-gild refusal queues applicant notice");
    var body = service.TakePendingNoticesBody(9);
    Equal(17, body.Length, "join-gild notice body length");
    Equal(NativeGildOfflineNotice.JoinGildRefuseType, body[0],
        "join-gild refusal tag is native 2");
    Equal("本方行会", ReadShort(body.AsSpan(1, 16)),
        "join-gild refusal text is target gild name");
}

static void CheckCorpsRefuseQueuesType1()
{
    var service = NewService();
    Equal(0, service.RequestJoin(
            new NativeCorpsActor(77, "申请人", 50, 0, 0), 100),
        "corps join request creation");
    Equal(0, service.RefuseRequest(1, 77),
        "corps join refusal succeeds");
    var body = service.TakePendingNoticesBody(77);
    Equal(17, body.Length, "corps notice body length");
    Equal(NativeGildOfflineNotice.JoinCorpsRefuseType, body[0],
        "corps refusal tag is native 1");
    Equal("本方战队", ReadShort(body.AsSpan(1, 16)),
        "corps refusal text is target corps name");
}

static void CheckUnionRefuseQueuesType3()
{
    var service = NewService();
    Equal(0, service.ApplyGildRequestUnion(1, "盟友行会"),
        "union request creation");
    Equal(0, service.ApplyGildRefuseRequest(41, 1),
        "union refusal succeeds");
    var body = service.TakePendingNoticesBody(1);
    Equal(17, body.Length, "union notice body length");
    Equal(NativeGildOfflineNotice.UnionRefuseType, body[0],
        "union refusal tag is native 3");
    Equal("盟友行会", ReadShort(body.AsSpan(1, 16)),
        "union refusal text is target gild name");
}

static void CheckLoginDrainEmitsAndConsumes()
{
    var service = NewService();
    service.QueuePendingNotice(77, 2, "登录通知");
    var player = new ProbePlayer { m_boOffLineFlag = true };
    player.LoadNativeMailRecipientId(77);
    player.SetNativeCorpsServiceForTests(service);

    var method = typeof(TPlayObject).GetMethod(
        "SendNativePendingNoticesOnLogon",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new Exception("SM4612 login sender missing");
    method.Invoke(player, null);
    Equal(1, player.BinaryPackets.Count,
        "login emits one SM4612 frame");
    Equal(Grobal2.SM_PENDING_NOTICE, player.BinaryPackets[0].Header.Ident,
        "login frame ident");
    Equal(17, player.BinaryPackets[0].Body.Length,
        "login frame carries queued record");
    Equal(0, service.PendingNoticeCount(77),
        "login drains queue atomically");

    method.Invoke(player, null);
    Equal(2, player.BinaryPackets.Count,
        "empty login still emits native always-send frame");
    Equal(0, player.BinaryPackets[1].Body.Length,
        "second login frame is empty");
}

static NativeCorpsService NewService()
{
    var snapshot = new NativeCorpsDataSnapshot();
    AddCorps(snapshot, 100, 1, "本方战队");
    AddCorps(snapshot, 410, 41, "盟友战队");
    AddCorps(snapshot, 900, 9, "申请战队");
    AddGild(snapshot, 200, 100, "本方行会", 100);
    AddGild(snapshot, 400, 410, "盟友行会", 410);
    if (!NativeCorpsService.TryCreate(new FakeCorpsStore(snapshot),
            out var service, out var error, new FakeGildStore()))
        throw new Exception(error);
    return service;
}

static void AddCorps(NativeCorpsDataSnapshot snapshot, long id,
    long ownerId, string name)
{
    var corps = new NativeCorpsSnapshot
    {
        Id = id, Name = name, OwnerId = ownerId,
        CreateTime = new DateTime(2020, 1, 1)
    };
    corps.Members.Add(new NativeCorpsMemberSnapshot
    {
        MemberId = ownerId, Name = "玩家" + ownerId,
        Level = 50, LastLoginTime = corps.CreateTime
    });
    snapshot.CorpsById.Add(id, corps);
}

static void AddGild(NativeCorpsDataSnapshot snapshot, long id,
    long ownerCorpsId, string name, long memberCorpsId)
{
    var gild = new NativeGildSnapshot
    {
        Id = id, Name = name, OwnerCorpsId = ownerCorpsId,
        CreateTime = new DateTime(2020, 1, 1)
    };
    gild.CorpsIds.Add(memberCorpsId);
    snapshot.GildById.Add(id, gild);
}

static string ReadShort(ReadOnlySpan<byte> slot)
{
    if (slot.Length < 16 || slot[0] > 15)
        throw new Exception("invalid ShortString slot");
    return HUtil32.GbkEncoding.GetString(slot.Slice(1, slot[0]));
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
        throw new Exception($"{message}: expected {expected}, got {actual}");
}

static void BytesEqual(byte[] expected, byte[] actual, string message)
{
    if (!expected.AsSpan().SequenceEqual(actual))
        throw new Exception(message);
}

sealed class ProbePlayer : TPlayObject
{
    internal readonly List<(ClientPacket Header, byte[] Body)> BinaryPackets = new();

    internal override void SendSocket(ClientPacket defMsg, byte[] rawBody)
    {
        BinaryPackets.Add((defMsg, rawBody ?? Array.Empty<byte>()));
    }
}

sealed class FakeCorpsStore : INativeCorpsStore
{
    private readonly NativeCorpsDataSnapshot _snapshot;
    internal FakeCorpsStore(NativeCorpsDataSnapshot snapshot) => _snapshot = snapshot;
    public bool TryLoad(out NativeCorpsDataSnapshot snapshot, out string error)
    { snapshot = _snapshot; error = string.Empty; return true; }
    public bool TryInsertMember(long id, NativeCorpsMemberSnapshot member, out string error)
    { error = string.Empty; return true; }
    public bool TryDeleteMember(long id, out string error)
    { error = string.Empty; return true; }
    public bool TryExitMember(long id, NativeCorpsSnapshot corps, bool update, out string error)
    { error = string.Empty; return true; }
    public bool TryUpdateMemberTitle(long id, string title, out string error)
    { error = string.Empty; return true; }
    public bool TryUpdateCorps(NativeCorpsSnapshot corps, out string error)
    { error = string.Empty; return true; }
    public bool TryUpdateGild(NativeGildSnapshot gild, out string error)
    { error = string.Empty; return true; }
}

sealed class FakeGildStore : INativeGildStore
{
    public bool TryCreateGild(long id, string name, long owner, long vice, out string error)
    { error = string.Empty; return true; }
    public bool TrySaveGild(long id, long owner, long vice, byte[] notice, out string error)
    { error = string.Empty; return true; }
    public bool TryInsertGildMember(long gild, long corps, out string error)
    { error = string.Empty; return true; }
    public bool TryDeleteGildMember(long gild, long corps, out string error)
    { error = string.Empty; return true; }
    public bool TryInsertGildRelation(long a, long b, int relation, DateTime time, out string error)
    { error = string.Empty; return true; }
    public bool TryDeleteGildRelation(long a, long b, out string error)
    { error = string.Empty; return true; }
    public bool TryInsertGildConcern(long a, long b, out string error)
    { error = string.Empty; return true; }
    public bool TryDeleteGildConcern(long a, long b, out string error)
    { error = string.Empty; return true; }
}
