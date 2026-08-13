using System.Reflection;
using System.Text;
using GameSvr;
using GameSvr.Services;
using SystemModule;

// NativeGildAcceptWiringCompatCheck — proves the newly WIRED live routing of
// CM_GILD_ACCEPT_REQUEST (4611) through TPlayObject.TryHandleNativeGuildTailProtocol,
// WITHOUT a live database. 4611 was a dead UNGATED stub (SendUnsupportedNativeGuildDecision,
// always Param=1000) until the user greenlit matching native's functional accept; this
// asserts the wire reaches the REAL NativeCorpsService.ApplyGildAcceptRequest for BOTH
// subtype routes (JOIN add-to-gild / UNION DELETE-3+INSERT-1) + the no-store fail-closed
// fallback + the request consume, THROUGH the handler.
//
// SCOPE: the full accept/refuse LADDER (every role/type gate, the mutations, gold) is
// covered service-directly by InProcCorpsGuildRunCheck.CheckGildRequestAcceptRefuse. This
// is the HANDLER-WIRE proof only: routing + gate (SupportsGildWrites) + reply frame
// (SM 4611, Param=result, echoed id) + the president-from-connection / unique-id-from-body
// contract + the wrong-guild guard + ledger consume. Same posture + fakes as the sibling
// NativeGildConcernWarWiringCompatCheck.
//
// PRODUCTION-FAITHFUL WIRE: the CM body is the 6-bit-ENCODED unique request id (the client
// echoes it from the 4570/4571 listing); the handler decodes via GetNativeCorpsBody (the
// #90 decode fix). The president identity comes from the CONNECTION (GetCachedNativeUserId),
// NOT the body. Feeding raw (unencoded) ids here would mangle under the decode fix — the
// payload is EDcode.EncodeBuffer'd exactly like InProcCorpsGuildRunCheck's Drive.

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
    // The send sink is synchronous (SetNativeCorpsServiceForTests); the critical section is
    // initialized like ~100 audits + production (GameApp) for any SendUpdateMsg path.
    M2Share.ProcessMsgCriticalSection = new object();

    AcceptJoinSucceedsAddsMemberAndConsumes();
    AcceptUnionSucceedsSwapsRelationAndConsumes();
    AcceptRequestNotFoundRejectedNoStore();
    AcceptWrongGuildPresidentRejectedNoMutation();
    AcceptNoGildStoreFallsBackToFailClosed1000();

    Console.WriteLine(
        "PASS NativeGildAcceptWiringCompatCheck wired=4611 " +
        "join=add-to-gild(sub_706264) union=DELETE-3+INSERT-1(sub_708168) " +
        "key=uniqueId(body) president=connection guard=callerGild==TargetKey " +
        "consume=ledger.Remove reply=SM4611/Param=result/echo-id no-store=1000");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("NativeGildAcceptWiringCompatCheck FAIL: " + ex);
    return 1;
}

// 1. JOIN accept through the 4611 handler adds the applicant corps to the president's gild
//    (in-memory membership write) + consumes the request.
static void AcceptJoinSucceedsAddsMemberAndConsumes()
{
    var service = NewService(out _);
    Require(service.ApplyGildRequestJoin(9, 200) == 0,
        "join request seeded (applicant corps900 -> gild200, uniqueId=1)");

    var packet = DriveAccept(service, 1, 1);   // president p1 of gild200 accepts uniqueId 1
    Equal(Grobal2.SM_GILD_ACCEPT_REQUEST, packet.Header.Ident, "4611 reply ident SM 4611");
    Equal(0, packet.Header.Param, "4611 JOIN accept success code via the handler");
    BytesEqual(NativeCorpsWireCodec.EncodeId(1), packet.Body,
        "4611 reply echoes the unique request id");
    Require(GildById(service, 200).CorpsIds.Contains(900)
            && service.TryGetGildForCorps(900, out var gild) && gild.Id == 200,
        "4611 JOIN accept added applicant corps900 to gild200 (sub_706264 add-to-gild)");

    var again = DriveAccept(service, 1, 1);
    Equal(10, again.Header.Param,
        "4611 request consumed on accept (re-accept => RequestNotFound 10)");
}

// 2. UNION accept through the 4611 handler: DELETE pending Relation-3 + INSERT Relation-1 on
//    the canonical (min,max) pair + in-memory union + consume.
static void AcceptUnionSucceedsSwapsRelationAndConsumes()
{
    var service = NewService(out var store);
    Require(service.ApplyGildEnableUnion(41, true) == 0,
        "target gild400 enables union (session flag)");
    Require(service.ApplyGildRequestUnion(1, "盟友行会") == 0,
        "union request seeded (gild200 -> gild400, Relation-3 pending, uniqueId=1)");
    // save_relation 0x5E6F1B `8ACB mov cl,bl` / 0x5E6F23 `call 0x49F9C8` stores the raw type, so
    // the pending 3 sits in the relation map until accept's 0x70821C delete_relation clears it.
    Require(GildRelation(service, 1, 41) == NativeCorpsService.GildPendingUnion,
        "pending union Relation-3 is published in-memory before accept");

    var packet = DriveAccept(service, 41, 1);   // president p41 of gild400 accepts
    Equal(0, packet.Header.Param, "4611 UNION accept success code via the handler");
    Require(GildRelation(service, 1, 41) == NativeCorpsService.GildUnion,
        "4611 UNION accept -> in-memory union relation (DELETE-3 + INSERT-1)");
    Require(store.Deletes.Contains((200L, 400L))
            && store.Inserts.Contains((200L, 400L, 1)),
        "4611 UNION accept persisted DELETE then INSERT Relation-1 on the canonical pair");

    var again = DriveAccept(service, 41, 1);
    Equal(10, again.Header.Param,
        "4611 union request consumed on accept (re-accept => 10)");
}

// 3. Accept a non-existent unique id -> RequestNotFound, no store write.
static void AcceptRequestNotFoundRejectedNoStore()
{
    var service = NewService(out var store);
    var packet = DriveAccept(service, 1, 99999);
    Equal(10, packet.Header.Param, "4611 unknown id => RequestNotFound 10");
    Equal(0, store.Deletes.Count + store.Inserts.Count, "4611 not-found no store write");
}

// 4. A president who does NOT own the target gild cannot accept its request: the unique id is
//    global, but the lookup requires callerGild.Id == request.TargetKey (a president only ever
//    holds ids from their OWN gild's 4570/4571 listing).
static void AcceptWrongGuildPresidentRejectedNoMutation()
{
    var service = NewService(out _);
    Require(service.ApplyGildRequestJoin(9, 200) == 0,
        "join request to gild200 seeded (uniqueId=1)");
    // p41 is president of gild400, NOT gild200 -> callerGild.Id(400) != TargetKey(200).
    var packet = DriveAccept(service, 41, 1);
    Equal(10, packet.Header.Param, "4611 wrong-guild president => RequestNotFound 10");
    Require(!GildById(service, 200).CorpsIds.Contains(900),
        "4611 wrong-guild accept did not add the applicant (no cross-guild mutation)");
    // The rightful president can still accept afterwards (the request was NOT consumed).
    Equal(0, DriveAccept(service, 1, 1).Header.Param,
        "4611 rightful president p1 still accepts the un-consumed request");
}

// 5. No gild store -> SupportsGildWrites=false -> the exact original fail-closed stub
//    response (SendUnsupportedNativeGuildDecision: SM 4611, Param=1000, id echoed). Proves the
//    gate keeps store-less deployments faithful (no accidental live write path).
static void AcceptNoGildStoreFallsBackToFailClosed1000()
{
    var service = NewServiceNoStore();
    Require(!service.SupportsGildWrites,
        "service without a gild store must report SupportsGildWrites=false");
    var packet = DriveAccept(service, 1, 1);
    Equal(Grobal2.SM_GILD_ACCEPT_REQUEST, packet.Header.Ident, "4611 no-store reply ident");
    Equal(NativeCorpsService.UnknownError, packet.Header.Param,
        "4611 no-store fallback result is UnknownError(1000)");
    BytesEqual(NativeCorpsWireCodec.EncodeId(1), packet.Body,
        "4611 no-store fallback still echoes the id");
}

// ---- helpers ---------------------------------------------------------------

// Drive the REAL 4611 dispatch entry (TryHandleNativeGuildTailProtocol) with a fresh offline
// operator player, returning the single packet the handler emitted via the test sink.
static (ClientPacket Header, byte[] Body) DriveAccept(
    NativeCorpsService service, long operatorId, long uniqueRequestId)
{
    var packets = new List<(ClientPacket Header, byte[] Body)>();
    var player = new TPlayObject
    {
        m_boOffLineFlag = true,
        m_sCharName = "op" + operatorId
    };
    player.LoadNativeMailRecipientId(operatorId);           // GetCachedNativeUserId() == operatorId
    player.SetNativeCorpsServiceForTests(service,
        (header, body) => packets.Add((header, body)));

    var method = typeof(TPlayObject).GetMethod("TryHandleNativeGuildTailProtocol",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("TryHandleNativeGuildTailProtocol missing");
    var handled = (bool)method.Invoke(player, new object[]
    {
        new TProcessMessage
        {
            wIdent = Grobal2.CM_GILD_ACCEPT_REQUEST,
            // PRODUCTION-faithful wire body: the 6-bit-ENCODED unique request id (GateService
            // delivers encoded; the handler decodes via GetNativeCorpsBody = the #90 fix).
            Payload = EDcode.EncodeBuffer(NativeCorpsWireCodec.EncodeId(uniqueRequestId))
        }
    });
    Require(handled, "4611 was not claimed by TryHandleNativeGuildTailProtocol");
    Equal(1, packets.Count, "4611 must emit exactly one packet");
    return packets[0];
}

// Gild 200 (owner corps 100, president p1) and gild 400 (owner corps 410, president p41) exist;
// corps 900 (captain p9) is NOT in a gild (the join applicant). A TrackingGildStore records the
// relation INSERT/DELETE so the union DELETE-3 + INSERT-1 lifecycle is observable.
static NativeCorpsService NewService(out TrackingGildStore store)
{
    var snapshot = new NativeCorpsDataSnapshot();
    AddCorps(snapshot, 100, 1, "会长战队");       // gild 200 owner corps
    AddCorps(snapshot, 410, 41, "盟友会长战队");   // gild 400 owner corps
    AddCorps(snapshot, 900, 9, "散人战队");        // applicant corps (NOT in a gild)
    AddGild(snapshot, 200, 100, "本方行会", new long[] { 100 });
    AddGild(snapshot, 400, 410, "盟友行会", new long[] { 410 });
    store = new TrackingGildStore();
    if (!NativeCorpsService.TryCreate(new FakeCorpsStore(snapshot), out var service,
            out var error, store))
        throw new Exception("NativeCorpsService.TryCreate failed: " + error);
    return service;
}

// A service with NO gild store -> SupportsGildWrites=false (the store-less deployment).
static NativeCorpsService NewServiceNoStore()
{
    var snapshot = new NativeCorpsDataSnapshot();
    AddCorps(snapshot, 100, 1, "会长战队");
    AddGild(snapshot, 200, 100, "本方行会", new long[] { 100 });
    if (!NativeCorpsService.TryCreate(new FakeCorpsStore(snapshot), out var service,
            out var error, null))
        throw new Exception("NativeCorpsService.TryCreate failed: " + error);
    return service;
}

static void AddCorps(NativeCorpsDataSnapshot snapshot, long id, long ownerId, string name)
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

static void AddGild(NativeCorpsDataSnapshot snapshot, long id, long ownerCorpsId,
    string name, long[] members)
{
    var gild = new NativeGildSnapshot
    {
        Id = id,
        CreateTime = new DateTime(2020, 1, 2),
        Name = name,
        OwnerCorpsId = ownerCorpsId,
        Notice = HUtil32.GbkEncoding.GetBytes("行会公告")
    };
    foreach (var corpsId in members) gild.CorpsIds.Add(corpsId);
    snapshot.GildById.Add(id, gild);
}

static NativeGildSnapshot GildById(NativeCorpsService service, long gildId)
{
    foreach (var gild in service.SnapshotGilds())
        if (gild.Id == gildId)
            return gild;
    throw new Exception("gild not found in live service: " + gildId);
}

static byte GildRelation(NativeCorpsService service, long selfPlayerId, long targetPlayerId)
{
    service.GetCombatRelation(selfPlayerId, targetPlayerId, out _, out _, out _, out _,
        out _, out _, out var gildRelation);
    return gildRelation;
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
        throw new InvalidOperationException(
            $"{message}: expected={expected}, actual={actual}");
}

static void BytesEqual(byte[] expected, byte[] actual, string message)
{
    if (!expected.AsSpan().SequenceEqual(actual))
        throw new InvalidOperationException(
            $"{message}: expected={Convert.ToHexString(expected)}, " +
            $"actual={Convert.ToHexString(actual ?? Array.Empty<byte>())}");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

// ---- fakes (verbatim from the gild wiring checks: no MySQL) -----------------

sealed class FakeCorpsStore : INativeCorpsStore
{
    private readonly NativeCorpsDataSnapshot _snapshot;

    internal FakeCorpsStore(NativeCorpsDataSnapshot snapshot) => _snapshot = snapshot;

    public bool TryLoad(out NativeCorpsDataSnapshot snapshot, out string error)
    { snapshot = _snapshot; error = string.Empty; return true; }

    public bool TryInsertMember(long corpsId, NativeCorpsMemberSnapshot member, out string error)
    { error = string.Empty; return true; }

    public bool TryDeleteMember(long memberId, out string error)
    { error = string.Empty; return true; }

    public bool TryExitMember(long memberId, NativeCorpsSnapshot corps, bool updateCorps, out string error)
    { error = string.Empty; return true; }

    public bool TryUpdateMemberTitle(long memberId, string title, out string error)
    { error = string.Empty; return true; }

    public bool TryUpdateCorps(NativeCorpsSnapshot corps, out string error)
    { error = string.Empty; return true; }

    public bool TryUpdateGild(NativeGildSnapshot gild, out string error)
    { error = string.Empty; return true; }
}

// Records the relation INSERT/DELETE (fail-safe no-op otherwise) so the union
// DELETE-3 + INSERT-1 lifecycle is observable; every other write returns true.
sealed class TrackingGildStore : INativeGildStore
{
    internal readonly List<(long GildId1, long GildId2)> Deletes = new();
    internal readonly List<(long GildId1, long GildId2, int Relation)> Inserts = new();

    public bool TrySaveGild(long gildId, long ownerCorpsId, long viceOwnerId, byte[] notice, out string error)
    { error = string.Empty; return true; }

    public bool TryDeleteGildMember(long gildId, long corpsId, out string error)
    { error = string.Empty; return true; }

    public bool TryCreateGild(long gildId, string name, long ownerCorpsId, long viceOwnerId, out string error)
    { error = string.Empty; return true; }

    public bool TryInsertGildMember(long gildId, long corpsId, out string error)
    { error = string.Empty; return true; }

    public bool TryInsertGildRelation(long gildId1, long gildId2, int relation, DateTime createTime, out string error)
    { Inserts.Add((gildId1, gildId2, relation)); error = string.Empty; return true; }

    public bool TryDeleteGildRelation(long gildId1, long gildId2, out string error)
    { Deletes.Add((gildId1, gildId2)); error = string.Empty; return true; }

    public bool TryInsertGildConcern(long gildId, long destinationGildId, out string error)
    { error = string.Empty; return true; }

    public bool TryDeleteGildConcern(long gildId, long destinationGildId, out string error)
    { error = string.Empty; return true; }
}
