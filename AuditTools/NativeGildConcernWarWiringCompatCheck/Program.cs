using System.Reflection;
using System.Text;
using GameSvr;
using GameSvr.Services;
using SystemModule;

// NativeGildConcernWarWiringCompatCheck — proves the gated live routing of the
// Gild concern/union/war family WITHOUT a live database. Wired:
//   4574 break-union    -> BreakUnion       -> TryDeleteGildRelation
//   4579 declare-war-id  -> DeclareWar(gold) -> TryInsertGildRelation(rel=2)
//   4576 add-concern-id  -> NativeGildConcernLadder -> TryInsertGildConcern
//   4586 add-concern-name-> resolve name + 4576 ladder (reply SM 4576)
//   4578 cancel-concern  -> NativeGildConcernLadder -> TryDeleteGildConcern
//   4581 enable-union    -> NativeGildUnionFlagLadder -> session flag + TrySaveGild
//   4585 declare-war-name-> resolve name + 4579 ladder (reply SM 4579)
// Exit/vice writes (4583/4587/4588) live in NativeGildExitViceWiringCompatCheck.
//
// Every scenario drives the real dispatch entry
// TPlayObject.TryHandleNativeGuildRelationProtocol against a NativeCorpsService
// built from a fake INativeCorpsStore snapshot + a fake INativeGildStore; each
// fixture ISOLATES ONE ladder gate (the gap-A lesson). It asserts the correct
// store method + args, the exact reversed result code, gold deduction only on
// success, session-flag change-detection, fail-safe swallow with NO rollback,
// the no-store -> 1000 fallback.

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
    // GoldChanged() -> SendUpdateMsg enters this critical section; production
    // sets it in GameApp and ~100 audits initialize it the same way.
    M2Share.ProcessMsgCriticalSection = new object();

    BreakUnionSucceedsDeletesAndMutates();
    BreakUnionNotAlliedRejectedNoStore();
    BreakUnionTargetNotFoundRejectedNoStore();
    BreakUnionNonPresidentRejectedNoStore();
    BreakUnionStoreExceptionSwallowedNoRollback();
    BreakUnionNoGildStoreFallsBackToFailClosed1000();

    DeclareWarSucceedsInsertsDeductsGold();
    DeclareWarInsufficientGoldRejectedNoDeductNoStore();
    DeclareWarSelfRejectedNoDeduct();
    DeclareWarAlreadyAlliedRejectedNoDeduct();
    DeclareWarAlreadyAtWarRejectedNoDeduct();
    DeclareWarNonPresidentRejectedNoDeduct();
    DeclareWarTargetNotFoundRejectedNoDeduct();
    DeclareWarNoGildStoreFallsBackToFailClosed1000();

    AddConcernSucceedsInsertsAndDedups();
    AddConcernSelfRejectedNoStore();
    AddConcernDuplicateRejectedNoStore();
    AddConcernTargetNotFoundRejectedNoStore();
    AddConcernNonPresidentRejectedNoStore();
    AddConcernStoreExceptionSwallowedNoRollback();
    AddConcernNoGildStoreFallsBackToFailClosed1000();

    CancelConcernSucceedsDeletesAndMutates();
    CancelConcernNotPresentRejectedNoStore();
    CancelConcernTargetNotFoundRejectedNoStore();
    CancelConcernNonPresidentRejectedNoStore();
    CancelConcernNoGildStoreFallsBackToFailClosed1000();

    AddConcernByNameSucceedsAndRepliesSm4576();
    AddConcernByNameUnresolvedRejectedNoStore();
    AddConcernByNameNoGildStoreFallsBackToFailClosed1000();

    EnableUnionOwnerTogglesAndSavesOnChangeOnly();
    EnableUnionNonPresidentRejectedNoStore();
    EnableUnionNoGildStoreFallsBackToFailClosed1000();

    DeclareWarByNameSucceedsInsertsDeductsRepliesSm4579();
    DeclareWarByNameUnresolvedRejectedNoDeductNoStore();
    DeclareWarByNameNoGildStoreFallsBackToFailClosed1000();

    Console.WriteLine(
        "PASS NativeGildConcernWarWiringCompatCheck wired=" +
        "4574/4579/4576/4586/4578/4581/4585 " +
        "store=DeleteGildRelation/InsertGildRelation(2)/Insert+DeleteGildConcern" +
        "/TrySaveGild(flag) gold-gate=30000 name=registry(SM4576/4579) " +
        "fail-safe=no-rollback no-store=1000");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(
        "NativeGildConcernWarWiringCompatCheck FAIL: " + ex);
    return 1;
}

// ---- 4574 break-union ------------------------------------------------------

static void BreakUnionSucceedsDeletesAndMutates()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    var packet = DriveBreakUnion(service, 1, 400);

    Equal(0, PacketResult(packet), "4574 break-union success code");
    Equal(0, packet.Body.Length, "4574 empty body");
    Equal(1, store.Calls.Count, "4574 exactly one store call");
    Equal("delrel:200:400", store.Calls[0],
        "4574 TryDeleteGildRelation normalized args");

    var again = DriveBreakUnion(service, 1, 400);
    Equal(27, PacketResult(again),
        "4574 relation removed in-memory (repeat => not allied 27)");
}

static void BreakUnionNotAlliedRejectedNoStore()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    Equal(27, PacketResult(DriveBreakUnion(service, 1, 600)),
        "4574 not-allied reject code");
    Equal(0, store.Calls.Count, "4574 not-allied no store call");
}

static void BreakUnionTargetNotFoundRejectedNoStore()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    Equal(25, PacketResult(DriveBreakUnion(service, 1, 999)),
        "4574 target-not-found reject code");
    Equal(0, store.Calls.Count, "4574 target-not-found no store call");
}

static void BreakUnionNonPresidentRejectedNoStore()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    // Player 2 is a plain member of corps 100 (gild 200) => role GildMember.
    Equal(555, PacketResult(DriveBreakUnion(service, 2, 400)),
        "4574 non-president reject code");
    Equal(0, store.Calls.Count, "4574 non-president no store call");
}

static void BreakUnionStoreExceptionSwallowedNoRollback()
{
    var store = new FakeGildStore { Throw = true };
    var service = BuildService(store);
    Equal(0, PacketResult(DriveBreakUnion(service, 1, 400)),
        "4574 store-throw still returns success code");
    Equal(1, store.Calls.Count, "4574 store-throw attempted the DELETE once");
    Equal(27, PacketResult(DriveBreakUnion(service, 1, 400)),
        "4574 store-throw did NOT roll back the in-memory removal");
    Require(GildById(service, 200) != null,
        "4574 store-throw left the Gild read path intact");
}

static void BreakUnionNoGildStoreFallsBackToFailClosed1000()
{
    var service = BuildService(null);
    Require(!service.SupportsGildWrites,
        "service without a Gild store must report SupportsGildWrites=false");
    var packet = DriveBreakUnion(service, 1, 400);
    Equal(NativeCorpsService.UnknownError, PacketResult(packet),
        "4574 no-store fallback result is UnknownError(1000)");
    Equal(0, packet.Body.Length, "4574 no-store fallback empty body");
}

// ---- 4579 declare-war-by-id ------------------------------------------------

static void DeclareWarSucceedsInsertsDeductsGold()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    var result = DriveDeclareWar(service, 1, 600, 40000);

    Equal(0, result.Result, "4579 declare-war success code");
    Equal(0, result.BodyLength, "4579 empty body");
    Equal(10000, result.GoldAfter, "4579 deducted exactly 30000 gold");
    Equal(1, store.Calls.Count, "4579 exactly one store call");
    Equal("insrel:200:600:2", store.Calls[0],
        "4579 TryInsertGildRelation(relation=2) normalized args");

    var again = DriveDeclareWar(service, 1, 600, 40000);
    Equal(15, again.Result,
        "4579 war relation published in-memory (repeat => already at war 15)");
    Equal(40000, again.GoldAfter, "4579 no deduction on a rejected repeat");
}

static void DeclareWarInsufficientGoldRejectedNoDeductNoStore()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    var result = DriveDeclareWar(service, 1, 600, 29999);

    Equal(36, result.Result, "4579 insufficient-gold reject code");
    Equal(29999, result.GoldAfter, "4579 insufficient gold not touched");
    Equal(0, store.Calls.Count, "4579 insufficient-gold no store call");
}

static void DeclareWarSelfRejectedNoDeduct()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    var result = DriveDeclareWar(service, 1, 200, 40000);
    Equal(19, result.Result, "4579 self-target reject code");
    Equal(40000, result.GoldAfter, "4579 self-target no deduction");
    Equal(0, store.Calls.Count, "4579 self-target no store call");
}

static void DeclareWarAlreadyAlliedRejectedNoDeduct()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    var result = DriveDeclareWar(service, 1, 400, 40000);
    Equal(32, result.Result, "4579 already-allied reject code");
    Equal(40000, result.GoldAfter, "4579 already-allied no deduction");
    Equal(0, store.Calls.Count, "4579 already-allied no store call");
}

static void DeclareWarAlreadyAtWarRejectedNoDeduct()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    var result = DriveDeclareWar(service, 1, 700, 40000);
    Equal(15, result.Result, "4579 already-at-war reject code");
    Equal(40000, result.GoldAfter, "4579 already-at-war no deduction");
    Equal(0, store.Calls.Count, "4579 already-at-war no store call");
}

static void DeclareWarNonPresidentRejectedNoDeduct()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    var result = DriveDeclareWar(service, 2, 600, 40000);
    Equal(555, result.Result, "4579 non-president reject code");
    Equal(40000, result.GoldAfter, "4579 non-president no deduction");
    Equal(0, store.Calls.Count, "4579 non-president no store call");
}

static void DeclareWarTargetNotFoundRejectedNoDeduct()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    var result = DriveDeclareWar(service, 1, 999, 40000);
    Equal(25, result.Result, "4579 target-not-found reject code");
    Equal(40000, result.GoldAfter, "4579 target-not-found no deduction");
    Equal(0, store.Calls.Count, "4579 target-not-found no store call");
}

static void DeclareWarNoGildStoreFallsBackToFailClosed1000()
{
    var service = BuildService(null);
    var result = DriveDeclareWar(service, 1, 600, 40000);
    Equal(NativeCorpsService.UnknownError, result.Result,
        "4579 no-store fallback result is UnknownError(1000)");
    Equal(0, result.BodyLength, "4579 no-store fallback empty body");
    Equal(40000, result.GoldAfter, "4579 no-store fallback gold untouched");
}

// ---- 4576 add-concern-by-id ------------------------------------------------

static void AddConcernSucceedsInsertsAndDedups()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    var packet = DriveConcern(service, 1, Grobal2.CM_GILD_CONCERN_GILD_ID, 600);

    Equal(0, PacketResult(packet), "4576 add-concern success code");
    Equal(Grobal2.SM_GILD_CONCERN_GILD_ID, PacketIdent(packet),
        "4576 reply SM 4576");
    Equal(1, store.Calls.Count, "4576 exactly one store call");
    Equal("insconcern:200:600", store.Calls[0],
        "4576 TryInsertGildConcern args");

    var again = DriveConcern(service, 1, Grobal2.CM_GILD_CONCERN_GILD_ID, 600);
    Equal(1000, PacketResult(again),
        "4576 destination added in-memory (repeat => duplicate 1000)");
}

static void AddConcernSelfRejectedNoStore()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    Equal(19, PacketResult(
            DriveConcern(service, 1, Grobal2.CM_GILD_CONCERN_GILD_ID, 200)),
        "4576 self-concern reject code");
    Equal(0, store.Calls.Count, "4576 self-concern no store call");
}

static void AddConcernDuplicateRejectedNoStore()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    // 200 already concerns 400 in the seed.
    Equal(1000, PacketResult(
            DriveConcern(service, 1, Grobal2.CM_GILD_CONCERN_GILD_ID, 400)),
        "4576 duplicate reject code");
    Equal(0, store.Calls.Count, "4576 duplicate no store call");
}

static void AddConcernTargetNotFoundRejectedNoStore()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    Equal(25, PacketResult(
            DriveConcern(service, 1, Grobal2.CM_GILD_CONCERN_GILD_ID, 999)),
        "4576 target-not-found reject code");
    Equal(0, store.Calls.Count, "4576 target-not-found no store call");
}

static void AddConcernNonPresidentRejectedNoStore()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    // Concern is owner-only (vice +0x5C is a 555 stub); player 2 is a member.
    Equal(555, PacketResult(
            DriveConcern(service, 2, Grobal2.CM_GILD_CONCERN_GILD_ID, 600)),
        "4576 non-president reject code");
    Equal(0, store.Calls.Count, "4576 non-president no store call");
}

static void AddConcernStoreExceptionSwallowedNoRollback()
{
    var store = new FakeGildStore { Throw = true };
    var service = BuildService(store);
    Equal(0, PacketResult(
            DriveConcern(service, 1, Grobal2.CM_GILD_CONCERN_GILD_ID, 600)),
        "4576 store-throw still returns success code");
    Equal(1, store.Calls.Count, "4576 store-throw attempted the INSERT once");
    Equal(1000, PacketResult(
            DriveConcern(service, 1, Grobal2.CM_GILD_CONCERN_GILD_ID, 600)),
        "4576 store-throw did NOT roll back the in-memory add (repeat => 1000)");
}

static void AddConcernNoGildStoreFallsBackToFailClosed1000()
{
    var service = BuildService(null);
    var packet = DriveConcern(service, 1, Grobal2.CM_GILD_CONCERN_GILD_ID, 600);
    Equal(NativeCorpsService.UnknownError, PacketResult(packet),
        "4576 no-store fallback result is UnknownError(1000)");
    Equal(0, packet.Body.Length, "4576 no-store fallback empty body");
}

// ---- 4578 cancel-concern ---------------------------------------------------

static void CancelConcernSucceedsDeletesAndMutates()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    // 200 concerns 400 in the seed.
    var packet = DriveConcern(service, 1, Grobal2.CM_GILD_CANCLE_CONCERN, 400);

    Equal(0, PacketResult(packet), "4578 cancel-concern success code");
    Equal(1, store.Calls.Count, "4578 exactly one store call");
    Equal("delconcern:200:400", store.Calls[0],
        "4578 TryDeleteGildConcern args");

    var again = DriveConcern(service, 1, Grobal2.CM_GILD_CANCLE_CONCERN, 400);
    Equal(1000, PacketResult(again),
        "4578 destination removed in-memory (repeat => not present 1000)");
}

static void CancelConcernNotPresentRejectedNoStore()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    // 200 does not concern 600.
    Equal(1000, PacketResult(
            DriveConcern(service, 1, Grobal2.CM_GILD_CANCLE_CONCERN, 600)),
        "4578 not-present reject code (no self-check)");
    Equal(0, store.Calls.Count, "4578 not-present no store call");
}

static void CancelConcernTargetNotFoundRejectedNoStore()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    Equal(25, PacketResult(
            DriveConcern(service, 1, Grobal2.CM_GILD_CANCLE_CONCERN, 999)),
        "4578 target-not-found reject code");
    Equal(0, store.Calls.Count, "4578 target-not-found no store call");
}

static void CancelConcernNonPresidentRejectedNoStore()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    Equal(555, PacketResult(
            DriveConcern(service, 2, Grobal2.CM_GILD_CANCLE_CONCERN, 400)),
        "4578 non-president reject code");
    Equal(0, store.Calls.Count, "4578 non-president no store call");
}

// The 4578 no-store fallback is the INLINE SendUnsupportedNativeGuildIdOperation
// that NativeGildCancelConcernExactCheck's static boundary requires.
static void CancelConcernNoGildStoreFallsBackToFailClosed1000()
{
    var service = BuildService(null);
    var packet = DriveConcern(service, 1, Grobal2.CM_GILD_CANCLE_CONCERN, 400);
    Equal(NativeCorpsService.UnknownError, PacketResult(packet),
        "4578 no-store fallback result is UnknownError(1000)");
    Equal(0, packet.Body.Length, "4578 no-store fallback empty body");
}

// ---- 4586 add-concern-by-name ----------------------------------------------

static void AddConcernByNameSucceedsAndRepliesSm4576()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    // "路人行会" is gild 600 (no prior concern).
    var packet = DriveName(service, 1, Grobal2.CM_GILD_CONCERN_GILD_NAME,
        "路人行会");

    Equal(0, PacketResult(packet), "4586 add-concern-by-name success code");
    Equal(Grobal2.SM_GILD_CONCERN_GILD_ID, PacketIdent(packet),
        "4586 replies SM 4576");
    Equal(1, store.Calls.Count, "4586 exactly one store call");
    Equal("insconcern:200:600", store.Calls[0],
        "4586 resolved name -> TryInsertGildConcern(200,600)");
}

static void AddConcernByNameUnresolvedRejectedNoStore()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    Equal(12, PacketResult(
            DriveName(service, 1, Grobal2.CM_GILD_CONCERN_GILD_NAME,
                "不存在的行会")),
        "4586 unresolved name reject code");
    Equal(0, store.Calls.Count, "4586 unresolved name no store call");
}

static void AddConcernByNameNoGildStoreFallsBackToFailClosed1000()
{
    var service = BuildService(null);
    Equal(NativeCorpsService.UnknownError, PacketResult(
            DriveName(service, 1, Grobal2.CM_GILD_CONCERN_GILD_NAME,
                "路人行会")),
        "4586 no-store fallback result is UnknownError(1000)");
}

// ---- 4581 enable-union -----------------------------------------------------

static void EnableUnionOwnerTogglesAndSavesOnChangeOnly()
{
    var store = new FakeGildStore();
    var service = BuildService(store);

    // Toggle OFF (default is now TRUE per native 0x70633A C6 47 28 01) => change => standard 3-column Gild UPDATE.
    Equal(0, PacketResult(DriveEnableUnion(service, 1, false)),
        "4581 disable success code");
    Equal(1, store.Calls.Count, "4581 change => exactly one Gild save");
    Equal("save:200:100:0", store.Calls[0],
        "4581 standard 3-column TrySaveGild (flag has no column)");
    Require(!GildById(service, 200).UnionEnabled,
        "4581 in-memory session flag set false");

    // Toggle OFF again => no change => sub_704EAC skips the save.
    Equal(0, PacketResult(DriveEnableUnion(service, 1, false)),
        "4581 no-change success code");
    Equal(1, store.Calls.Count, "4581 no-change => no additional save");
}

static void EnableUnionNonPresidentRejectedNoStore()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    // owner+vice only; player 2 is a plain member. (Owner/vice ladder for all
    // roles is covered exhaustively by NativeGildConcernStateCompatCheck.)
    Equal(555, PacketResult(DriveEnableUnion(service, 2, true)),
        "4581 non-president/-vice reject code");
    Equal(0, store.Calls.Count, "4581 rejected no store call");
    Require(GildById(service, 200).UnionEnabled, "4581 rejected flag unchanged (default true)");
}

static void EnableUnionNoGildStoreFallsBackToFailClosed1000()
{
    var service = BuildService(null);
    Equal(NativeCorpsService.UnknownError,
        PacketResult(DriveEnableUnion(service, 1, true)),
        "4581 no-store fallback result is UnknownError(1000)");
}

// ---- 4585 declare-war-by-name ----------------------------------------------

static void DeclareWarByNameSucceedsInsertsDeductsRepliesSm4579()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    // "路人行会" is gild 600 (no relation); gold 40000.
    var result = DriveDeclareWarName(service, 1, "路人行会", 40000);

    Equal(0, result.Result, "4585 declare-war-by-name success code");
    Equal(Grobal2.SM_GILD_DECLARE_WAR, result.Ident, "4585 replies SM 4579");
    Equal(10000, result.GoldAfter, "4585 deducted exactly 30000 gold");
    Equal(1, store.Calls.Count, "4585 exactly one store call");
    Equal("insrel:200:600:2", store.Calls[0],
        "4585 resolved name -> TryInsertGildRelation(200,600,2)");
}

static void DeclareWarByNameUnresolvedRejectedNoDeductNoStore()
{
    var store = new FakeGildStore();
    var service = BuildService(store);
    // Name guard (12) precedes the gold gate, so gold stays untouched.
    var result = DriveDeclareWarName(service, 1, "不存在的行会", 40000);
    Equal(12, result.Result, "4585 unresolved name reject code");
    Equal(40000, result.GoldAfter, "4585 unresolved name no deduction");
    Equal(0, store.Calls.Count, "4585 unresolved name no store call");
}

static void DeclareWarByNameNoGildStoreFallsBackToFailClosed1000()
{
    var service = BuildService(null);
    var result = DriveDeclareWarName(service, 1, "路人行会", 40000);
    Equal(NativeCorpsService.UnknownError, result.Result,
        "4585 no-store fallback result is UnknownError(1000)");
    Equal(40000, result.GoldAfter, "4585 no-store fallback gold untouched");
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

// Gild 200 (owner corps 100, president player 1, plain member player 2) is
// ALLIED (union) with gild 400, already AT WAR with gild 700, has NO relation
// with gild 600, and CONCERNS gild 400. Gild names drive the by-name resolvers.
static NativeCorpsDataSnapshot BuildSnapshot()
{
    var snapshot = new NativeCorpsDataSnapshot();
    var homeCorps = AddCorps(snapshot, 100, 1, "会长战队");
    homeCorps.Members.Add(new NativeCorpsMemberSnapshot
    {
        MemberId = 2,
        Name = "普通成员",
        Level = 50,
        LastLoginTime = new DateTime(2020, 1, 2)
    });
    AddCorps(snapshot, 300, 3, "盟友战队");
    AddCorps(snapshot, 500, 5, "路人战队");
    AddCorps(snapshot, 800, 8, "宿敌战队");

    AddGild(snapshot, 200, 100, "本方行会");
    AddGild(snapshot, 400, 300, "盟友行会");
    AddGild(snapshot, 600, 500, "路人行会");
    AddGild(snapshot, 700, 800, "宿敌行会");

    snapshot.GildRelations.Add(
        NativeCorpsDataSnapshot.GildRelationKey(200, 400),
        (NativeCorpsService.GildUnion, DateTime.MinValue));
    // DateTime.MinValue reads as "declared infinitely long ago": any test added here
    // that calls ExpireGildWars will see this war as already expired.
    snapshot.GildRelations.Add(
        NativeCorpsDataSnapshot.GildRelationKey(200, 700),
        (NativeCorpsService.GildHostile, DateTime.MinValue));
    snapshot.GildConcerns[200] = new List<long> { 400 };
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

static void AddGild(NativeCorpsDataSnapshot snapshot, long id,
    long ownerCorpsId, string name)
{
    var gild = new NativeGildSnapshot
    {
        Id = id,
        CreateTime = new DateTime(2020, 1, 2),
        Name = name,
        OwnerCorpsId = ownerCorpsId,
        Notice = HUtil32.GbkEncoding.GetBytes("行会公告")
    };
    gild.CorpsIds.Add(ownerCorpsId);
    snapshot.GildById.Add(id, gild);
}

static (ClientPacket Header, byte[] Body) DriveBreakUnion(
    NativeCorpsService service, long operatorId, long targetGildId) =>
    DriveRelation(service, operatorId, Grobal2.CM_GILD_BREAK_UNION,
        NativeCorpsWireCodec.EncodeId(targetGildId));

static (ClientPacket Header, byte[] Body) DriveConcern(
    NativeCorpsService service, long operatorId, int ident,
    long targetGildId) =>
    DriveRelation(service, operatorId, ident,
        NativeCorpsWireCodec.EncodeId(targetGildId));

static (ClientPacket Header, byte[] Body) DriveName(
    NativeCorpsService service, long operatorId, int ident, string name) =>
    DriveRelation(service, operatorId, ident,
        HUtil32.GbkEncoding.GetBytes(name));

static (ClientPacket Header, byte[] Body) DriveEnableUnion(
    NativeCorpsService service, long operatorId, bool enabled) =>
    DriveRelation(service, operatorId, Grobal2.CM_GILD_ENABLE_UNION,
        new[] { enabled ? (byte)1 : (byte)0 });

static (int Result, int BodyLength, int GoldAfter) DriveDeclareWar(
    NativeCorpsService service, long operatorId, long targetGildId, int gold)
{
    var packet = DriveWithGold(service, operatorId, Grobal2.CM_GILD_DECLARE_WAR,
        NativeCorpsWireCodec.EncodeId(targetGildId), gold, out var goldAfter);
    return (packet.Header.Param, packet.Body.Length, goldAfter);
}

static (int Result, int BodyLength, int GoldAfter, int Ident)
    DriveDeclareWarName(NativeCorpsService service, long operatorId,
        string name, int gold)
{
    var packet = DriveWithGold(service, operatorId,
        Grobal2.CM_GILD_DECLARE_WAR_NAME, HUtil32.GbkEncoding.GetBytes(name),
        gold, out var goldAfter);
    return (packet.Header.Param, packet.Body.Length, goldAfter,
        packet.Header.Ident);
}

static (ClientPacket Header, byte[] Body) DriveWithGold(
    NativeCorpsService service, long operatorId, int ident, byte[] payload,
    int gold, out int goldAfter)
{
    var packets = new List<(ClientPacket Header, byte[] Body)>();
    var player = new TPlayObject
    {
        m_boOffLineFlag = true,
        m_sCharName = "会长",
        m_nGold = gold
    };
    player.LoadNativeMailRecipientId(operatorId);
    player.SetNativeCorpsServiceForTests(service,
        (header, body) => packets.Add((header, body)));

    Invoke(player, ident, payload);

    Equal(1, packets.Count, ident + " must emit exactly one packet");
    goldAfter = player.m_nGold;
    return packets[0];
}

static (ClientPacket Header, byte[] Body) DriveRelation(
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

    Invoke(player, ident, payload);

    Equal(1, packets.Count, ident + " must emit exactly one packet");
    return packets[0];
}

static void Invoke(TPlayObject player, int ident, byte[] payload)
{
    var method = typeof(TPlayObject).GetMethod(
        "TryHandleNativeGuildRelationProtocol",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(
            "TryHandleNativeGuildRelationProtocol missing");
    var handled = (bool)method.Invoke(player, new object[]
    {
        // Production-faithful wire body: the 6-bit-ENCODED body (GateService delivers encoded;
        // the handler decodes via GetNativeCorpsBody = the #90 fix). Feeding raw here would mangle.
        new TProcessMessage { wIdent = ident, Payload = SystemModule.EDcode.EncodeBuffer(payload ?? Array.Empty<byte>()) }
    });
    Require(handled, ident + " was not claimed by the dispatcher");
}

static int PacketResult((ClientPacket Header, byte[] Body) packet) =>
    packet.Header.Param;

static int PacketIdent((ClientPacket Header, byte[] Body) packet) =>
    packet.Header.Ident;

static NativeGildSnapshot GildById(NativeCorpsService service, long gildId)
{
    foreach (var gild in service.SnapshotGilds())
        if (gild.Id == gildId)
            return gild;
    return null;
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
