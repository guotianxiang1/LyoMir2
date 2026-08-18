using System.Reflection;
using System.Text;
using GameSvr;
using GameSvr.Services;
using SystemModule;

// NativeGildExitViceWiringCompatCheck — proves the gated live routing of the
// exit/vice Gild WRITE ops WITHOUT a live database:
//   4583 exit            -> NativeGildExitTransaction  -> TryDeleteGildMember
//   4587 vice self-step  -> NativeGildViceTransaction  -> TrySaveGild(vice=0)
//   4588 dismiss vice    -> NativeGildViceTransaction  -> TrySaveGild(vice=0)
// (4564 create is wired separately — see NativeGildCreateWiringCompatCheck.)
//
// Each scenario drives the real dispatch entry
// TPlayObject.TryHandleNativeGuildRelationProtocol
// against a NativeCorpsService built from a fake INativeCorpsStore snapshot + a
// fake INativeGildStore. It asserts the exact reversed result code, the correct
// store method + args, the in-memory mutation, fail-safe swallow with NO
// rollback, and the no-store -> original fail-closed ABI. The 4583 handler zone
// gates (safe-zone / fight-zone / castle-war) are supplied to
// NativeCorpsService.ApplyGildExit directly for the 38/28/29 rungs; a bare
// audit player (null environment -> InSafeZone()==true) exercises the gates-pass
// path end-to-end through the dispatcher.

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

    ExitSuccessDeletesMemberViaDispatcher();
    ExitNotInGildReturns12ViaDispatcher();
    ExitZoneGatesRejectViaService();
    ExitNoStoreFallbackReturns1000();
    ExitStoreExceptionSwallowedNoRollback();

    ViceStepDownSuccessSavesGild();
    ViceStepDownOwnerNotViceRejectedNoStore();
    ViceStepDownPlainMemberRejectedNoStore();
    ViceStepDownNoStoreFallbackReturns1000();
    ViceStepDownStoreExceptionSwallowedNoRollback();

    DismissViceSuccessSavesGild();
    DismissViceNotPresidentRejectedNoStore();
    DismissViceTargetNotFoundRejectedNoStore();
    DismissViceTargetNotViceRejectedNoStore();
    DismissViceNoStoreFallbackReturns1000();

    Console.WriteLine(
        "PASS NativeGildExitViceWiringCompatCheck wired=4583/4587/4588 " +
        "store=DeleteGildMember/TrySaveGild(vice=0) exit-gates=38/28/29 " +
        "strategy=5/12/18/1000/0 vice=555/22/0 fail-safe=no-rollback " +
        "no-store=1000");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(
        "NativeGildExitViceWiringCompatCheck FAIL: " + ex);
    return 1;
}

// ---- 4583 exit -------------------------------------------------------------

// Member corps 500 (player 5) leaves gild 200: gates all pass on a bare player,
// strategy removes the corps + DELETE gamedata.gildmember(200,500).
static void ExitSuccessDeletesMemberViaDispatcher()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    var packet = DriveRelation(service, 5, Grobal2.CM_GILD_EXIT,
        Array.Empty<byte>());

    Equal(Grobal2.SM_GILD_EXIT, packet.Header.Ident, "4583 reply ident");
    Equal(0, packet.Header.Param, "4583 exit success code");
    Equal(1, store.Calls.Count, "4583 exactly one store call");
    Equal("delmember:200:500", store.Calls[0], "4583 DELETE gildmember args");
    Require(!GildById(service, 200).CorpsIds.Contains(500),
        "4583 leaver corps still in gild");
}

// Corps 900 (player 9) is not in any gild -> handler in-a-gild gate -> 12,
// store untouched.
static void ExitNotInGildReturns12ViaDispatcher()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    var packet = DriveRelation(service, 9, Grobal2.CM_GILD_EXIT,
        Array.Empty<byte>());

    Equal(12, packet.Header.Param, "4583 not-in-gild code");
    Equal(0, store.Calls.Count, "4583 not-in-gild touched the store");
}

// The player-object zone gates map 1:1 onto the reversed handler ladder: not in
// a safe zone -> 38, fight zone -> 28, castle war -> 29. No removal, no store.
static void ExitZoneGatesRejectViaService()
{
    var store = new FakeGildStore();
    var service = BuildService(store);

    Equal(38, service.ApplyGildExit(5, canLeave: false, inFightZone: false,
        castleWarBlocked: false), "4583 not-allowed-to-leave -> 38");
    Equal(28, service.ApplyGildExit(5, canLeave: true, inFightZone: true,
        castleWarBlocked: false), "4583 fight zone -> 28");
    Equal(29, service.ApplyGildExit(5, canLeave: true, inFightZone: false,
        castleWarBlocked: true), "4583 castle war -> 29");

    Equal(0, store.Calls.Count, "4583 zone rejects touched the store");
    Require(GildById(service, 200).CorpsIds.Contains(500),
        "4583 zone reject mutated the gild");
}

static void ExitNoStoreFallbackReturns1000()
{
    var service = BuildService(null);
    var packet = DriveRelation(service, 5, Grobal2.CM_GILD_EXIT,
        Array.Empty<byte>());
    Equal(Grobal2.SM_GILD_EXIT, packet.Header.Ident,
        "4583 no-store reply ident");
    Equal(NativeCorpsService.UnknownError, packet.Header.Param,
        "4583 no-store fallback is 1000");
}

static void ExitStoreExceptionSwallowedNoRollback()
{
    var store = new FakeGildStore { Throw = true };
    var service = BuildService(store);
    var packet = DriveRelation(service, 5, Grobal2.CM_GILD_EXIT,
        Array.Empty<byte>());

    Equal(0, packet.Header.Param, "4583 fail-safe still reports success");
    Equal(1, store.Calls.Count, "4583 fail-safe attempted the DELETE");
    Require(!GildById(service, 200).CorpsIds.Contains(500),
        "4583 fail-safe rolled back the in-memory removal");
}

// ---- 4587 vice self-stepdown -----------------------------------------------

// The actual vice (player 3, captain of vice corps 300) steps down: vice pointer
// cleared + make-save-gild UPDATE (ViceGuild=0).
static void ViceStepDownSuccessSavesGild()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    var packet = DriveRelation(service, 3,
        Grobal2.CM_GILD_VICECAPTAIN_STEPDOWN, Array.Empty<byte>());

    Equal(Grobal2.SM_GILD_VICECAPTAIN_STEPDOWN, packet.Header.Ident,
        "4587 reply ident");
    Equal(0, packet.Header.Param, "4587 self-stepdown success code");
    Equal(1, store.Calls.Count, "4587 exactly one store call");
    Equal("save:200:100:0", store.Calls[0], "4587 make-save-gild vice=0 args");
    Equal(0, GildById(service, 200).ViceOwnerId, "4587 vice pointer not cleared");
}

// The president (player 1) reaches the vice strategy but is not the vice -> 555.
static void ViceStepDownOwnerNotViceRejectedNoStore()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    var packet = DriveRelation(service, 1,
        Grobal2.CM_GILD_VICECAPTAIN_STEPDOWN, Array.Empty<byte>());

    Equal(NativeCorpsService.PermissionDenied, packet.Header.Param,
        "4587 owner-not-vice -> 555");
    Equal(0, store.Calls.Count, "4587 owner-not-vice touched the store");
    Equal(300, GildById(service, 200).ViceOwnerId, "4587 reject cleared vice");
}

// A plain gild-member corps captain (player 5) never reaches the strategy -> 555.
static void ViceStepDownPlainMemberRejectedNoStore()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    var packet = DriveRelation(service, 5,
        Grobal2.CM_GILD_VICECAPTAIN_STEPDOWN, Array.Empty<byte>());

    Equal(NativeCorpsService.PermissionDenied, packet.Header.Param,
        "4587 plain member -> 555");
    Equal(0, store.Calls.Count, "4587 plain member touched the store");
}

static void ViceStepDownNoStoreFallbackReturns1000()
{
    var service = BuildService(null);
    var packet = DriveRelation(service, 3,
        Grobal2.CM_GILD_VICECAPTAIN_STEPDOWN, Array.Empty<byte>());
    Equal(Grobal2.SM_GILD_VICECAPTAIN_STEPDOWN, packet.Header.Ident,
        "4587 no-store reply ident");
    Equal(NativeCorpsService.UnknownError, packet.Header.Param,
        "4587 no-store fallback is 1000");
}

static void ViceStepDownStoreExceptionSwallowedNoRollback()
{
    var store = new FakeGildStore { Throw = true };
    var service = BuildService(store);
    var packet = DriveRelation(service, 3,
        Grobal2.CM_GILD_VICECAPTAIN_STEPDOWN, Array.Empty<byte>());

    Equal(0, packet.Header.Param, "4587 fail-safe still reports success");
    Equal(1, store.Calls.Count, "4587 fail-safe attempted the UPDATE");
    Equal(0, GildById(service, 200).ViceOwnerId,
        "4587 fail-safe rolled back the vice clear");
}

// ---- 4588 president dismiss vice -------------------------------------------

// The president (player 1) dismisses the vice corps 300: vice pointer cleared +
// make-save-gild UPDATE.
static void DismissViceSuccessSavesGild()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    var packet = DriveRelation(service, 1,
        Grobal2.CM_GILD_DISMISS_VICECAPTAIN,
        NativeCorpsWireCodec.EncodeId(300));

    Equal(Grobal2.SM_GILD_DISMISS_VICECAPTAIN, packet.Header.Ident,
        "4588 reply ident");
    Equal(0, packet.Header.Param, "4588 dismiss success code");
    Equal(1, store.Calls.Count, "4588 exactly one store call");
    Equal("save:200:100:0", store.Calls[0], "4588 make-save-gild vice=0 args");
    Equal(0, GildById(service, 200).ViceOwnerId, "4588 vice pointer not cleared");
}

// The vice (player 3) maps to the 555 stub, not the owner strategy.
static void DismissViceNotPresidentRejectedNoStore()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    var packet = DriveRelation(service, 3,
        Grobal2.CM_GILD_DISMISS_VICECAPTAIN,
        NativeCorpsWireCodec.EncodeId(300));

    Equal(NativeCorpsService.PermissionDenied, packet.Header.Param,
        "4588 non-president -> 555");
    Equal(0, store.Calls.Count, "4588 non-president touched the store");
    Equal(300, GildById(service, 200).ViceOwnerId, "4588 reject cleared vice");
}

static void DismissViceTargetNotFoundRejectedNoStore()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    var packet = DriveRelation(service, 1,
        Grobal2.CM_GILD_DISMISS_VICECAPTAIN,
        NativeCorpsWireCodec.EncodeId(999));

    Equal(22, packet.Header.Param, "4588 target-not-found -> 22");
    Equal(0, store.Calls.Count, "4588 target-not-found touched the store");
}

// Member corps 500 exists but is not the gild's vice -> 22.
static void DismissViceTargetNotViceRejectedNoStore()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    var packet = DriveRelation(service, 1,
        Grobal2.CM_GILD_DISMISS_VICECAPTAIN,
        NativeCorpsWireCodec.EncodeId(500));

    Equal(22, packet.Header.Param, "4588 target-not-vice -> 22");
    Equal(0, store.Calls.Count, "4588 target-not-vice touched the store");
    Equal(300, GildById(service, 200).ViceOwnerId, "4588 reject cleared vice");
}

static void DismissViceNoStoreFallbackReturns1000()
{
    var service = BuildService(null);
    var packet = DriveRelation(service, 1,
        Grobal2.CM_GILD_DISMISS_VICECAPTAIN,
        NativeCorpsWireCodec.EncodeId(300));
    Equal(Grobal2.SM_GILD_DISMISS_VICECAPTAIN, packet.Header.Ident,
        "4588 no-store reply ident");
    Equal(NativeCorpsService.UnknownError, packet.Header.Param,
        "4588 no-store fallback is 1000");
    Equal(0, packet.Body.Length, "4588 no-store fallback body is empty");
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

// Gild 200 has president corps 100 (captain player 1, plain member player 2),
// vice corps 300 (captain player 3) and member corps 500 (captain player 5).
// Corps 900 (captain player 9) is not in any gild.
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
    AddCorps(snapshot, 300, 3, "副会战队");
    AddCorps(snapshot, 500, 5, "成员战队");
    AddCorps(snapshot, 900, 9, "无会战队");

    var gild = new NativeGildSnapshot
    {
        Id = 200,
        CreateTime = new DateTime(2020, 1, 2),
        Name = "本方行会",
        OwnerCorpsId = 100,
        ViceOwnerId = 300,
        Notice = HUtil32.GbkEncoding.GetBytes("行会公告")
    };
    gild.CorpsIds.Add(100);
    gild.CorpsIds.Add(300);
    gild.CorpsIds.Add(500);
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

static (ClientPacket Header, byte[] Body) DriveRelation(
    NativeCorpsService service, long operatorId, int ident, byte[] payload) =>
    Drive(service, operatorId, ident, payload,
        "TryHandleNativeGuildRelationProtocol");

static (ClientPacket Header, byte[] Body) Drive(NativeCorpsService service,
    long operatorId, int ident, byte[] payload, string dispatcher)
{
    var packets = new List<(ClientPacket Header, byte[] Body)>();
    // 4583's first handler gate is the safe-zone probe, so a player standing on
    // no map at all can only ever come back as NotAllowed(38) and the success
    // ladder below it is never reached. Production leavers stand in town; model
    // that with a boSAFE map. The three zone rejections are still pinned
    // directly through service.ApplyGildExit in ExitZoneGatesRejectViaService.
    var player = new TPlayObject
    {
        m_boOffLineFlag = true,
        m_sCharName = "会长",
        m_PEnvir = new Envirnoment { sMapName = "0", Flag = { boSAFE = true } }
    };
    player.LoadNativeMailRecipientId(operatorId);
    player.SetNativeCorpsServiceForTests(service,
        (header, body) => packets.Add((header, body)));

    var method = typeof(TPlayObject).GetMethod(dispatcher,
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(dispatcher + " missing");
    var handled = (bool)method.Invoke(player, new object[]
    {
        // Production-faithful wire body: the 6-bit-ENCODED body (GateService delivers encoded;
        // the handler decodes via GetNativeCorpsBody = the #90 fix). Feeding raw here would mangle.
        new TProcessMessage { wIdent = ident, Payload = SystemModule.EDcode.EncodeBuffer(payload ?? Array.Empty<byte>()) }
    });

    Require(handled, ident + " was not claimed by " + dispatcher);
    var statusIdent = ident switch
    {
        Grobal2.CM_GILD_EXIT => Grobal2.SM_GILD_EXIT,
        Grobal2.CM_GILD_VICECAPTAIN_STEPDOWN => Grobal2.SM_GILD_VICECAPTAIN_STEPDOWN,
        Grobal2.CM_GILD_DISMISS_VICECAPTAIN => Grobal2.SM_GILD_DISMISS_VICECAPTAIN,
        _ => throw new InvalidOperationException("unknown Gild relation ident: " + ident)
    };
    var statusPackets = packets
        .Where(packet => packet.Header.Ident == statusIdent)
        .ToArray();
    Equal(1, statusPackets.Length,
        ident + " must emit exactly one status packet");
    return statusPackets[0];
}

static NativeGildSnapshot GildById(NativeCorpsService service, long gildId)
{
    foreach (var gild in service.SnapshotGilds())
        if (gild.Id == gildId)
            return gild;
    throw new InvalidOperationException("gild not found: " + gildId);
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
