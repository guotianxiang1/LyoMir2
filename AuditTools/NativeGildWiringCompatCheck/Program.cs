using System.Reflection;
using System.Text;
using GameSvr;
using GameSvr.Services;
using SystemModule;

// NativeGildWiringCompatCheck — proves the live routing of the president-only
// Gild leadership write ops (4567 dismiss-corps / 4568 transfer-president /
// 4569 appoint-vice) WITHOUT a live database. It drives each op end-to-end
// through the real dispatch entry TPlayObject.TryHandleNativeGuildCoreProtocol
// against a NativeCorpsService built from a fake INativeCorpsStore snapshot and
// a fake INativeGildStore, and asserts:
//   (1) the correct INativeGildStore method is called with the correct args,
//   (2) the exact reversed result code is returned to the client,
//   (3) a store exception is swallowed fail-safe (op still returns, the change
//       is NOT rolled back, and the read path is unaffected),
//   (4) the in-memory mutation happens on Success and NOT on a rejected code,
//   (5) with NO Gild store configured the branch falls back to the original
//       fail-closed response (Param = UnknownError(1000), empty body) and the
//       store is never touched — the invariant NativeCorpsProtocolCheck asserts.
//
// The wire target for all three ops is a CORPS id (reversed sub_7046A8:
// sub_705660(caller)==a1 compares the caller's corps id to the arg, and
// sub_7063E8(a1) tests gild membership of that corps). Result ladders match the
// pure classifier NativeGildLeadershipTransaction verified by
// NativeGildLeadershipCompatCheck.

try
{
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    PrepareRuntimeConfig();
    M2Share.g_Config = new GameSvrConfig();
    M2Share.UserEngine = new UserEngine();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.MapManager = new MapManager();
    M2Share.StartPointList = new List<TStartPoint>();
    M2Share.SafeZoneList = new List<TSafeZoneArea>();

    TransferToMemberSucceedsAndSaves();
    TransferToNonMemberRejectedNoStore();
    AppointViceSucceedsAndSaves();
    AppointViceWhenOccupiedRejectedNoStore();
    DismissMemberSucceedsAndDeletes();
    DismissLeadershipRejectedNoStore();
    DismissOwnCorpsSelfRejectedNoStore();
    DismissWrongGildRejectedNoStore();
    DismissMissingTargetRejectedNoStore();
    NonPresidentRejectedForAllOps();
    ViceCanDismissButNotTransferOrAppoint();
    StoreExceptionIsSwallowedFailSafeNoRollback();
    StoreFailureBooleanIsSwallowedFailSafe();
    NoGildStoreFallsBackToFailClosed1000();

    Console.WriteLine(
        "PASS NativeGildWiringCompatCheck ops=4567/4568/4569 target=corpsId " +
        "gated=INativeGildStore fail-safe=no-rollback no-store=1000 " +
        "dismiss=president-or-vice");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("NativeGildWiringCompatCheck FAIL: " + ex);
    return 1;
}

// ---- Scenarios -------------------------------------------------------------

// 4568 transfer to a member corps => code 0, TrySaveGild(OwnerCorpsID), mutate.
static void TransferToMemberSucceedsAndSaves()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    var packet = DriveGuildCore(service, 1,
        Grobal2.CM_GILD_TRANSFER_PRESIDENT, 101);

    Equal(0, PacketResult(packet), "4568 transfer success code");
    Equal(0, packet.Body.Length, "4568 transfer empty body");
    Equal(1, store.Calls.Count, "4568 exactly one store call");
    Equal("save:200:101:0", store.Calls[0], "4568 TrySaveGild args");
    Require(NoticeMatchesSeed(store.LastSaveNotice),
        "4568 TrySaveGild carried the Gild notice bytes");
    Equal(101L, GildOwnerCorps(service, 200),
        "4568 in-memory OwnerCorpsId mutated");
}

// 4568 transfer to a corps that is not a Gild member => 18, no store, no mutate.
static void TransferToNonMemberRejectedNoStore()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    var packet = DriveGuildCore(service, 1,
        Grobal2.CM_GILD_TRANSFER_PRESIDENT, 103);

    Equal(18, PacketResult(packet), "4568 non-member reject code");
    Equal(0, store.Calls.Count, "4568 non-member no store call");
    Equal(100L, GildOwnerCorps(service, 200),
        "4568 non-member OwnerCorpsId unchanged");
}

// 4569 appoint an in-gild corps as vice => 0, TrySaveGild(ViceOwnerID), mutate.
static void AppointViceSucceedsAndSaves()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    var packet = DriveGuildCore(service, 1,
        Grobal2.CM_GILD_APPOINT_VICE_PRESIDENT, 101);

    Equal(0, PacketResult(packet), "4569 appoint success code");
    Equal(1, store.Calls.Count, "4569 exactly one store call");
    Equal("save:200:100:101", store.Calls[0], "4569 TrySaveGild args");
    Equal(101L, GildViceCorps(service, 200),
        "4569 in-memory ViceOwnerId mutated");
    Equal(100L, GildOwnerCorps(service, 200),
        "4569 OwnerCorpsId untouched");
}

// 4569 when the vice slot is already occupied => 21, no store, no mutate.
static void AppointViceWhenOccupiedRejectedNoStore()
{
    var store = new FakeGildStore();
    var service = BuildService(store, viceOwnerCorpsId: 101);
    var packet = DriveGuildCore(service, 1,
        Grobal2.CM_GILD_APPOINT_VICE_PRESIDENT, 102);

    Equal(21, PacketResult(packet), "4569 vice-occupied reject code");
    Equal(0, store.Calls.Count, "4569 vice-occupied no store call");
    Equal(101L, GildViceCorps(service, 200),
        "4569 vice-occupied ViceOwnerId unchanged");
}

// 4567 dismiss an ordinary member corps => 0, TryDeleteGildMember, mutate.
static void DismissMemberSucceedsAndDeletes()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    var packet = DriveGuildCore(service, 1,
        Grobal2.CM_GILD_DISMISS_CORPS, 102);

    Equal(0, PacketResult(packet), "4567 dismiss success code");
    Equal(1, store.Calls.Count, "4567 exactly one store call");
    Equal("delmember:200:102", store.Calls[0],
        "4567 TryDeleteGildMember args");
    Require(!GildContainsCorps(service, 200, 102),
        "4567 corps removed from Gild.CorpsIds");
    Require(!service.TryGetGildForCorps(102, out _),
        "4567 corps->gild reverse index cleared");
}

// 4567 dismissing a LEADERSHIP corps that is NOT the caller's own (the vice
// corps) => 555 (TargetIsLeadership), no store. The 555 leadership-reject path is
// only reachable via the vice corps: dismissing the OWNER corps hits the
// self-check first (=> 19), because the president owns it (see next test).
static void DismissLeadershipRejectedNoStore()
{
    var store = new FakeGildStore();
    var service = BuildService(store, viceOwnerCorpsId: 101);
    var packet = DriveGuildCore(service, 1,
        Grobal2.CM_GILD_DISMISS_CORPS, 101);

    Equal(555, PacketResult(packet), "4567 leadership(vice) reject code");
    Equal(0, store.Calls.Count, "4567 leadership no store call");
    Require(GildContainsCorps(service, 200, 101),
        "4567 vice corps still in Gild");
}

// 4567 the president dismissing their OWN owner corps => 19 (TargetIsSelf), which
// per the reversed ladder (sub_704AF8: .../19 自己/555 会长副会长/...) fires
// BEFORE the leadership check. No store call, owner corps stays.
static void DismissOwnCorpsSelfRejectedNoStore()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    var packet = DriveGuildCore(service, 1,
        Grobal2.CM_GILD_DISMISS_CORPS, 100);

    Equal(19, PacketResult(packet), "4567 self (own owner corps) reject code");
    Equal(0, store.Calls.Count, "4567 self no store call");
    Require(GildContainsCorps(service, 200, 100),
        "4567 own owner corps still in Gild");
}

// 4567 dismissing a corps that belongs to no/another Gild => 22, no store.
static void DismissWrongGildRejectedNoStore()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    var packet = DriveGuildCore(service, 1,
        Grobal2.CM_GILD_DISMISS_CORPS, 103);

    Equal(22, PacketResult(packet), "4567 wrong-gild reject code");
    Equal(0, store.Calls.Count, "4567 wrong-gild no store call");
}

// 4567 dismissing a corps id that does not exist => 7, no store.
static void DismissMissingTargetRejectedNoStore()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    var packet = DriveGuildCore(service, 1,
        Grobal2.CM_GILD_DISMISS_CORPS, 999);

    Equal(7, PacketResult(packet), "4567 missing-target reject code");
    Equal(0, store.Calls.Count, "4567 missing-target no store call");
}

// A caller who is not the Gild president gets 555 (role gate) for every op and
// never touches the store.
static void NonPresidentRejectedForAllOps()
{
    foreach (var ident in new[]
             {
                 Grobal2.CM_GILD_DISMISS_CORPS,
                 Grobal2.CM_GILD_TRANSFER_PRESIDENT,
                 Grobal2.CM_GILD_APPOINT_VICE_PRESIDENT
             })
    {
        var store = new FakeGildStore();
        var service = BuildService(store);
        // Player 2 owns corps 101, an ordinary member corps => role Corps.
        var packet = DriveGuildCore(service, 2, ident, 102);
        Equal(555, PacketResult(packet), ident + " non-president reject code");
        Equal(0, store.Calls.Count, ident + " non-president no store call");
    }
}

// A Gild VICE may dismiss an ordinary member corps (4567's +0x54 slot sub_704AF8
// is shared by the gild_owner AND gild_vice strategies) but still may NOT
// transfer-president (4568) or appoint-vice (4569) — those slots are 555 stubs.
static void ViceCanDismissButNotTransferOrAppoint()
{
    // Player 2 owns corps 101; making 101 the Gild's vice corps makes player 2
    // the Gild vice (position 3).
    var store = new FakeGildStore();
    var service = BuildService(store, viceOwnerCorpsId: 101);
    var dismiss = DriveGuildCore(service, 2, Grobal2.CM_GILD_DISMISS_CORPS, 102);
    Equal(0, PacketResult(dismiss), "vice dismiss success code");
    Equal(1, store.Calls.Count, "vice dismiss exactly one store call");
    Equal("delmember:200:102", store.Calls[0],
        "vice dismiss TryDeleteGildMember args");
    Require(!GildContainsCorps(service, 200, 102),
        "vice dismiss removed the corps in-memory");

    var store2 = new FakeGildStore();
    var service2 = BuildService(store2, viceOwnerCorpsId: 101);
    Equal(555, PacketResult(DriveGuildCore(service2, 2,
            Grobal2.CM_GILD_TRANSFER_PRESIDENT, 102)),
        "vice transfer-president still rejected 555");
    Equal(555, PacketResult(DriveGuildCore(service2, 2,
            Grobal2.CM_GILD_APPOINT_VICE_PRESIDENT, 102)),
        "vice appoint-vice still rejected 555");
    Equal(0, store2.Calls.Count, "vice transfer/appoint no store call");
}

// (3) A store EXCEPTION on the write is swallowed: the op still returns the
// success code, the in-memory removal is NOT rolled back, and the read path is
// unaffected — matching the reversed original (only [SQL Failed] is logged).
static void StoreExceptionIsSwallowedFailSafeNoRollback()
{
    var store = new FakeGildStore { Throw = true };
    var service = BuildService(store);
    var packet = DriveGuildCore(service, 1,
        Grobal2.CM_GILD_DISMISS_CORPS, 102);

    Equal(0, PacketResult(packet), "store-throw still returns success code");
    Equal(1, store.Calls.Count, "store-throw attempted the write once");
    Require(!GildContainsCorps(service, 200, 102),
        "store-throw did NOT roll back the in-memory removal");
    Require(GildById(service, 200) != null,
        "store-throw left the Gild read path intact");
}

// A store FALSE (SQL failed, no exception) is likewise swallowed fail-safe.
static void StoreFailureBooleanIsSwallowedFailSafe()
{
    var store = new FakeGildStore { Fail = true };
    var service = BuildService(store);
    var packet = DriveGuildCore(service, 1,
        Grobal2.CM_GILD_TRANSFER_PRESIDENT, 101);

    Equal(0, PacketResult(packet), "store-false still returns success code");
    Equal(101L, GildOwnerCorps(service, 200),
        "store-false did NOT roll back the in-memory transfer");
}

// (5) With no Gild store configured, an op that WOULD succeed instead produces
// the original fail-closed response (Param=1000, empty body) and mutates
// nothing — exactly the ABI NativeCorpsProtocolCheck asserts stays dormant.
static void NoGildStoreFallsBackToFailClosed1000()
{
    var service = BuildService(null);
    Require(!service.SupportsGildWrites,
        "service without a Gild store must report SupportsGildWrites=false");
    var packet = DriveGuildCore(service, 1,
        Grobal2.CM_GILD_DISMISS_CORPS, 102);

    Equal(NativeCorpsService.UnknownError, PacketResult(packet),
        "no-store fallback result is UnknownError(1000)");
    Equal(0, packet.Body.Length, "no-store fallback empty body");
    Require(GildContainsCorps(service, 200, 102),
        "no-store fallback mutated nothing");
}

// ---- Helpers ---------------------------------------------------------------

static NativeCorpsService BuildService(FakeGildStore gildStore,
    long viceOwnerCorpsId = 0)
{
    var snapshot = BuildSnapshot(viceOwnerCorpsId);
    var corpsStore = new FakeCorpsStore(snapshot);
    Require(NativeCorpsService.TryCreate(corpsStore, out var service,
            out var error, gildStore),
        "service creation failed: " + error);
    return service;
}

// Gild 200 owns corps 100 (president = player 1). Corps 101 (player 2) and 102
// (player 3) are ordinary Gild members; corps 103 (player 4) belongs to no
// Gild. Optional vice slot pre-set for the vice-occupied scenario.
static NativeCorpsDataSnapshot BuildSnapshot(long viceOwnerCorpsId)
{
    var snapshot = new NativeCorpsDataSnapshot();
    AddCorps(snapshot, 100, 1, "会长战队");
    AddCorps(snapshot, 101, 2, "成员战队甲");
    AddCorps(snapshot, 102, 3, "成员战队乙");
    AddCorps(snapshot, 103, 4, "无会战队");

    var gild = new NativeGildSnapshot
    {
        Id = 200,
        CreateTime = new DateTime(2020, 1, 2),
        Name = "行会甲",
        OwnerCorpsId = 100,
        ViceOwnerId = viceOwnerCorpsId,
        Notice = SeedGildNotice()
    };
    gild.CorpsIds.Add(100);
    gild.CorpsIds.Add(101);
    gild.CorpsIds.Add(102);
    snapshot.GildById.Add(gild.Id, gild);
    return snapshot;
}

static void AddCorps(NativeCorpsDataSnapshot snapshot, long id, long ownerId,
    string name)
{
    var corps = new NativeCorpsSnapshot
    {
        Id = id,
        CreateTime = new DateTime(2020, 1, 2),
        Name = name,
        OwnerId = ownerId
    };
    corps.Members.Add(new NativeCorpsMemberSnapshot
    {
        MemberId = ownerId,
        Name = "队长" + ownerId,
        Level = 50,
        LastLoginTime = new DateTime(2020, 1, 2)
    });
    snapshot.CorpsById.Add(id, corps);
}

static byte[] SeedGildNotice() => HUtil32.GbkEncoding.GetBytes("行会公告");

static bool NoticeMatchesSeed(byte[] notice) =>
    notice != null && notice.AsSpan().SequenceEqual(SeedGildNotice());

static (ClientPacket Header, byte[] Body) DriveGuildCore(
    NativeCorpsService service, long operatorId, int ident, long targetCorpsId)
{
    var packets = new List<(ClientPacket Header, byte[] Body)>();
    var player = new TPlayObject
    {
        m_boOffLineFlag = true,
        m_sCharName = "会长"
    };
    player.LoadNativeMailRecipientId(operatorId);
    player.SetNativeCorpsServiceForTests(service,
        (header, body) => packets.Add((header, body)));

    var method = typeof(TPlayObject).GetMethod(
        "TryHandleNativeGuildCoreProtocol",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(
            "TryHandleNativeGuildCoreProtocol missing");
    var handled = (bool)method.Invoke(player, new object[]
    {
        new TProcessMessage
        {
            wIdent = ident,
            // Production-faithful wire body: the 6-bit-ENCODED id (GateService delivers encoded;
            // the handler decodes via GetNativeCorpsBody = the #90 fix). Feeding raw would mangle.
            Payload = SystemModule.EDcode.EncodeBuffer(NativeCorpsWireCodec.EncodeId(targetCorpsId))
        }
    });

    Require(handled, ident + " was not claimed by the dispatcher");
    var statusIdent = ident switch
    {
        Grobal2.CM_GILD_DISMISS_CORPS => Grobal2.SM_GILD_DISMISS_CORPS,
        Grobal2.CM_GILD_TRANSFER_PRESIDENT => Grobal2.SM_GILD_TRANSFER_PRESIDENT,
        Grobal2.CM_GILD_APPOINT_VICE_PRESIDENT => Grobal2.SM_GILD_APPOINT_VICE_PRESIDENT,
        _ => throw new InvalidOperationException("unknown Gild leadership ident: " + ident)
    };
    var statusPackets = packets
        .Where(packet => packet.Header.Ident == statusIdent)
        .ToArray();
    Equal(1, statusPackets.Length,
        ident + " must emit exactly one status packet");
    return statusPackets[0];
}

static int PacketResult((ClientPacket Header, byte[] Body) packet) =>
    packet.Header.Param;

static NativeGildSnapshot GildById(NativeCorpsService service, long gildId)
{
    foreach (var gild in service.SnapshotGilds())
        if (gild.Id == gildId)
            return gild;
    return null;
}

static long GildOwnerCorps(NativeCorpsService service, long gildId) =>
    GildById(service, gildId)?.OwnerCorpsId ?? -1;

static long GildViceCorps(NativeCorpsService service, long gildId) =>
    GildById(service, gildId)?.ViceOwnerId ?? -1;

static bool GildContainsCorps(NativeCorpsService service, long gildId,
    long corpsId) => GildById(service, gildId)?.CorpsIds.Contains(corpsId)
                     ?? false;

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

// ---- Fakes -----------------------------------------------------------------

// Minimal INativeCorpsStore: only TryLoad is exercised (the leadership ops
// write via INativeGildStore, never this store). The rest are inert.
sealed class FakeCorpsStore : INativeCorpsStore
{
    private readonly NativeCorpsDataSnapshot _snapshot;

    internal FakeCorpsStore(NativeCorpsDataSnapshot snapshot) =>
        _snapshot = snapshot;

    public bool TryLoad(out NativeCorpsDataSnapshot snapshot, out string error)
    {
        snapshot = _snapshot;
        error = string.Empty;
        return true;
    }

    public bool TryInsertMember(long corpsId, NativeCorpsMemberSnapshot member,
        out string error)
    {
        error = string.Empty;
        return true;
    }

    public bool TryDeleteMember(long memberId, out string error)
    {
        error = string.Empty;
        return true;
    }

    public bool TryExitMember(long memberId, NativeCorpsSnapshot corps,
        bool updateCorps, out string error)
    {
        error = string.Empty;
        return true;
    }

    public bool TryUpdateMemberTitle(long memberId, string title,
        out string error)
    {
        error = string.Empty;
        return true;
    }

    public bool TryUpdateCorps(NativeCorpsSnapshot corps, out string error)
    {
        error = string.Empty;
        return true;
    }

    public bool TryUpdateGild(NativeGildSnapshot gild, out string error)
    {
        error = string.Empty;
        return true;
    }
}

// Records every INativeGildStore call; can be told to fail (false) or throw so
// the fail-safe/no-rollback contract can be asserted deterministically.
sealed class FakeGildStore : INativeGildStore
{
    internal List<string> Calls { get; } = new();
    internal bool Fail { get; init; }
    internal bool Throw { get; init; }
    internal byte[] LastSaveNotice { get; private set; } = Array.Empty<byte>();

    public bool TrySaveGild(long gildId, long ownerCorpsId, long viceOwnerId,
        byte[] notice, out string error)
    {
        Calls.Add($"save:{gildId}:{ownerCorpsId}:{viceOwnerId}");
        LastSaveNotice = notice ?? Array.Empty<byte>();
        return Complete("Gild save", out error);
    }

    public bool TryDeleteGildMember(long gildId, long corpsId, out string error)
    {
        Calls.Add($"delmember:{gildId}:{corpsId}");
        return Complete("GildMember delete", out error);
    }

    public bool TryCreateGild(long gildId, string name, long ownerCorpsId,
        long viceOwnerId, out string error)
    {
        Calls.Add($"create:{gildId}");
        return Complete("Gild create", out error);
    }

    public bool TryInsertGildMember(long gildId, long corpsId, out string error)
    {
        Calls.Add($"insmember:{gildId}:{corpsId}");
        return Complete("GildMember insert", out error);
    }

    public bool TryInsertGildRelation(long gildId1, long gildId2, int relation,
        DateTime createTime, out string error)
    {
        Calls.Add($"insrel:{gildId1}:{gildId2}:{relation}");
        return Complete("GildRelation insert", out error);
    }

    public bool TryDeleteGildRelation(long gildId1, long gildId2,
        out string error)
    {
        Calls.Add($"delrel:{gildId1}:{gildId2}");
        return Complete("GildRelation delete", out error);
    }

    public bool TryInsertGildConcern(long gildId, long destinationGildId,
        out string error)
    {
        Calls.Add($"insconcern:{gildId}:{destinationGildId}");
        return Complete("gildconcern insert", out error);
    }

    public bool TryDeleteGildConcern(long gildId, long destinationGildId,
        out string error)
    {
        Calls.Add($"delconcern:{gildId}:{destinationGildId}");
        return Complete("gildconcern delete", out error);
    }

    private bool Complete(string label, out string error)
    {
        if (Throw)
            throw new InvalidOperationException("fake gild store threw on "
                + label);
        error = Fail ? label + " rejected by fake store" : string.Empty;
        return !Fail;
    }
}
