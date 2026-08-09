using System.Reflection;
using System.Text;
using GameSvr;
using GameSvr.Services;
using SystemModule;

// NativeGildCreateWiringCompatCheck — proves the gated live routing of the last
// Gild WRITE op, 4564 create-gild, WITHOUT a live database:
//   CM_GILD_CREATE -> NativeGildCreateContract ladder (555/4/5/6/2/0) ->
//   NativeGildIdAllocator (composite GildID) -> INativeGildStore
//   TryInsertGildMember THEN TryCreateGild (fail-safe, no rollback).
//
// Each scenario drives the real dispatch entry
// TPlayObject.TryHandleNativeGuildCoreProtocol (reflection) against a
// NativeCorpsService built from a fake INativeCorpsStore snapshot + a fake
// INativeGildStore. It asserts the exact reversed result code, the composite-id
// allocation + in-memory publication, the two INSERTs in order sharing one
// GildID, fail-safe swallow with NO rollback, and the no-store -> original
// fail-closed 1000 fallback (which keeps NativeCorpsProtocolCheck green).

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
    M2Share.ProcessMsgCriticalSection = new object();

    CreateSuccessAllocatesInsertsMemberThenGild();
    CreateDuplicateNameRejectedNoStore();
    CreateRoleDeniedPlainMemberNoStore();
    CreateAlreadyInGildRejectedNoStore();
    CreateNoStoreFallbackReturns1000();
    CreateStoreExceptionSwallowedNoRollback();
    CreateAllocatorProducesDistinctIds();

    Console.WriteLine(
        "PASS NativeGildCreateWiringCompatCheck wired=4564 " +
        "ladder=555/4/5/6/2/0 id=composite(NativeGildIdAllocator) " +
        "store=TryInsertGildMember+TryCreateGild(vice=0) " +
        "refresh=player-gild(4500)+role(4628)-before-status@result{0,2} " +
        "fail-safe=no-rollback no-store=1000");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(
        "NativeGildCreateWiringCompatCheck FAIL: " + ex);
    return 1;
}

// A corps owner with no gild (player 9 / corps 900) creates "新行会": allocate a
// composite GildID, publish the in-memory gild + registry, then INSERT
// gildmember(id,900) THEN INSERT Gild(id,...) sharing that one id.
static void CreateSuccessAllocatesInsertsMemberThenGild()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    var packets = DriveCore(service, 9, Grobal2.CM_GILD_CREATE, GbkName("新行会"));

    // Native sub_702F8C sends the player-gild snapshot (SM_PLAYER_GILD 4500) then
    // the social-role refresh (4628) from inside the +0x3C strategy BEFORE the
    // wrapper's SM_GILD_CREATE status — status is emitted last. The success-only
    // broadcast (sub_76C4C0/…) is inter-server and not a creator-facing packet, so
    // it is not replicated. Disasm: staging/ida_gild_create_refresh_disasm_20260803.txt.
    Equal(3, packets.Count, "4564 success packet count (player-gild + role + status)");
    Equal(Grobal2.SM_PLAYER_GILD, packets[0].Header.Ident,
        "4564 success first packet = player-gild snapshot (4500)");
    Equal(0, packets[0].Header.Param, "4564 success player-gild reports has-gild (0)");
    Require(packets[0].Body.Length > 0,
        "4564 success player-gild snapshot must carry the new gild body");
    Equal(4628, packets[1].Header.Ident,
        "4564 success second packet = social-role refresh (4628)");
    Equal(0, packets[1].Body.Length, "4628 role refresh body is empty");
    var packet = packets[2];
    Equal(Grobal2.SM_GILD_CREATE, packet.Header.Ident, "4564 reply ident");
    Equal(0, packet.Header.Param, "4564 create success code");

    Require(service.TryGetGildForCorps(900, out var gild),
        "4564 owner corps not bound to the new gild");
    Equal("新行会", gild.Name, "4564 new gild name");
    Equal(900L, gild.OwnerCorpsId, "4564 owner corps id");
    Equal(0L, gild.ViceOwnerId, "4564 new gild vice owner must be 0");
    Require(gild.CorpsIds.Contains(900), "4564 owner corps not a member");
    Require(gild.Id != 0, "4564 allocated GildID must be non-zero");

    Equal(2, store.Calls.Count, "4564 exactly two store writes");
    Equal($"insmember:{gild.Id}:900", store.Calls[0],
        "4564 first write = INSERT gildmember(gildId, ownerCorps)");
    Equal($"create:{gild.Id}", store.Calls[1],
        "4564 second write = INSERT Gild(gildId, ...)");
}

// Player 5 / corps 500 (no gild) tries the existing name "已有行会" -> dup 2.
static void CreateDuplicateNameRejectedNoStore()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    var packets = DriveCore(service, 5, Grobal2.CM_GILD_CREATE, GbkName("已有行会"));

    // Native runs the refresh for the whole AddGild-reached path — success (0) AND
    // the duplicate-name reject (2) — only the success-only broadcast differs. So
    // dup-name emits 3 packets too: player-gild(4500), role(4628), status(4564:2).
    // The player-gild snapshot reports has-corps-no-gild (12) here (sub_6F07CC n5).
    Equal(3, packets.Count, "4564 duplicate packet count (player-gild + role + status)");
    Equal(Grobal2.SM_PLAYER_GILD, packets[0].Header.Ident,
        "4564 duplicate first packet = player-gild snapshot (4500)");
    Equal(12, packets[0].Header.Param,
        "4564 duplicate player-gild reports has-corps-no-gild (12)");
    Equal(4628, packets[1].Header.Ident,
        "4564 duplicate second packet = social-role refresh (4628)");
    var packet = packets[2];
    Equal(Grobal2.SM_GILD_CREATE, packet.Header.Ident, "4564 duplicate reply ident");
    Equal(2, packet.Header.Param, "4564 duplicate name -> 2");
    Equal(0, store.Calls.Count, "4564 duplicate name touched the store");
    Require(!service.TryGetGildForCorps(500, out _),
        "4564 duplicate name bound a gild");
}

// Player 2 is a plain member of corps 100 (role Member) -> 555 role stub.
static void CreateRoleDeniedPlainMemberNoStore()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    var packets = DriveCore(service, 2, Grobal2.CM_GILD_CREATE, GbkName("越权行会"));

    // 555 is a native +0x3C role stub that returns before AddGild -> no refresh.
    Equal(1, packets.Count, "4564 role-denied emits status only (no refresh)");
    Equal(NativeCorpsService.PermissionDenied, packets[0].Header.Param,
        "4564 plain member -> 555");
    Equal(0, store.Calls.Count, "4564 role-denied touched the store");
}

// Player 1 already owns gild 200 (corps 100 in a gild): role passes but the
// create body's already-in-gild gate -> 6.
static void CreateAlreadyInGildRejectedNoStore()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    var packets = DriveCore(service, 1, Grobal2.CM_GILD_CREATE, GbkName("第二行会"));

    // 6 (corps already in a gild) is a native pre-gate returning before AddGild ->
    // no refresh.
    Equal(1, packets.Count, "4564 already-in-gild emits status only (no refresh)");
    Equal(6, packets[0].Header.Param, "4564 corps already in gild -> 6");
    Equal(0, store.Calls.Count, "4564 already-in-gild touched the store");
}

static void CreateNoStoreFallbackReturns1000()
{
    var service = BuildService(null);
    var packets = DriveCore(service, 9, Grobal2.CM_GILD_CREATE, GbkName("新行会"));
    // No store -> !SupportsGildWrites early return: status only, no refresh.
    Equal(1, packets.Count, "4564 no-store emits status only");
    Equal(Grobal2.SM_GILD_CREATE, packets[0].Header.Ident,
        "4564 no-store reply ident");
    Equal(NativeCorpsService.UnknownError, packets[0].Header.Param,
        "4564 no-store fallback is 1000");
}

static void CreateStoreExceptionSwallowedNoRollback()
{
    var store = new FakeGildStore { Throw = true };
    var service = BuildService(store);
    var packets = DriveCore(service, 9, Grobal2.CM_GILD_CREATE, GbkName("新行会"));

    // Fail-safe success (result 0) still refreshes then sends status = 3 packets;
    // the refresh reads in-memory state and never touches the throwing store.
    Equal(3, packets.Count, "4564 fail-safe success packet count");
    Equal(Grobal2.SM_PLAYER_GILD, packets[0].Header.Ident,
        "4564 fail-safe first packet = player-gild refresh");
    Equal(4628, packets[1].Header.Ident,
        "4564 fail-safe second packet = role refresh");
    var packet = packets[2];
    Equal(Grobal2.SM_GILD_CREATE, packet.Header.Ident, "4564 fail-safe status ident");
    Equal(0, packet.Header.Param, "4564 fail-safe still reports success");
    Require(service.TryGetGildForCorps(900, out var gild),
        "4564 fail-safe rolled back the in-memory gild");
    Equal(2, store.Calls.Count,
        "4564 fail-safe attempted both INSERTs before swallowing");
    Equal($"insmember:{gild.Id}:900", store.Calls[0], "4564 fail-safe member");
    Equal($"create:{gild.Id}", store.Calls[1], "4564 fail-safe gild");
}

// Two successful creates on one service allocate distinct non-zero GildIDs (the
// allocator's sequence byte advances even within the same timestamp tick).
static void CreateAllocatorProducesDistinctIds()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    DriveCore(service, 9, Grobal2.CM_GILD_CREATE, GbkName("行会甲"));
    DriveCore(service, 5, Grobal2.CM_GILD_CREATE, GbkName("行会乙"));

    Require(service.TryGetGildForCorps(900, out var gild9), "create 9 missing");
    Require(service.TryGetGildForCorps(500, out var gild5), "create 5 missing");
    Require(gild9.Id != 0 && gild5.Id != 0, "allocated ids must be non-zero");
    Require(gild9.Id != gild5.Id, "allocator produced a duplicate GildID");
}

// ---- Helpers ---------------------------------------------------------------

static NativeCorpsService BuildService(FakeGildStore gildStore)
{
    var snapshot = BuildSnapshot();
    var corpsStore = new FakeCorpsStore(snapshot);
    Require(NativeCorpsService.TryCreate(corpsStore, out var service,
            out var error, gildStore),
        "service creation failed: " + error);
    return service;
}

// Gild 200 "已有行会" has owner corps 100 (captain player 1, plain member player
// 2). Corps 500 (captain player 5) and corps 900 (captain player 9) are NOT in
// any gild — both are eligible CorpsOwner creators.
static NativeCorpsDataSnapshot BuildSnapshot()
{
    var snapshot = new NativeCorpsDataSnapshot();
    var president = AddCorps(snapshot, 100, 1, "会长战队");
    president.Members.Add(new NativeCorpsMemberSnapshot
    {
        MemberId = 2,
        Name = "普通成员",
        Level = 50,
        LastLoginTime = new DateTime(2020, 1, 2)
    });
    AddCorps(snapshot, 500, 5, "自由战队甲");
    AddCorps(snapshot, 900, 9, "自由战队乙");

    var gild = new NativeGildSnapshot
    {
        Id = 200,
        CreateTime = new DateTime(2020, 1, 2),
        Name = "已有行会",
        OwnerCorpsId = 100,
        ViceOwnerId = 0,
        Notice = HUtil32.GbkEncoding.GetBytes("行会公告")
    };
    gild.CorpsIds.Add(100);
    snapshot.GildById.Add(gild.Id, gild);
    return snapshot;
}

static NativeCorpsSnapshot AddCorps(NativeCorpsDataSnapshot snapshot, long id,
    long ownerId, string name)
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
    return corps;
}

static byte[] GbkName(string name) => HUtil32.GbkEncoding.GetBytes(name);

static List<(ClientPacket Header, byte[] Body)> DriveCore(
    NativeCorpsService service, long operatorId, int ident, byte[] payload)
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
        // Production-faithful wire body: the 6-bit-ENCODED body (GateService delivers encoded;
        // the handler decodes via GetNativeCorpsBody = the #90 fix). Feeding raw here would mangle.
        new TProcessMessage { wIdent = ident, Payload = SystemModule.EDcode.EncodeBuffer(payload ?? Array.Empty<byte>()) }
    });

    Require(handled, ident + " was not claimed by the core dispatcher");
    // NOTE: no fixed packet-count assertion here — native sends the player-gild +
    // social-role refresh BEFORE the SM_GILD_CREATE status on the AddGild-reached
    // path (result 0 or 2), so success/duplicate emit 3 packets while the
    // pre-gate rejects (555/6/1000) emit 1. Each scenario asserts its own count.
    return packets;
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

sealed class FakeGildStore : INativeGildStore
{
    internal List<string> Calls { get; } = new();
    internal bool Fail { get; init; }
    internal bool Throw { get; init; }

    public bool TryDeleteGildRelation(long gildId1, long gildId2,
        out string error)
    {
        Calls.Add($"delrel:{gildId1}:{gildId2}");
        return Complete("GildRelation delete", out error);
    }

    public bool TryInsertGildRelation(long gildId1, long gildId2, int relation,
        DateTime createTime, out string error)
    {
        Calls.Add($"insrel:{gildId1}:{gildId2}:{relation}");
        return Complete("GildRelation insert", out error);
    }

    public bool TrySaveGild(long gildId, long ownerCorpsId, long viceOwnerId,
        byte[] notice, out string error)
    {
        Calls.Add($"save:{gildId}:{ownerCorpsId}:{viceOwnerId}");
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
