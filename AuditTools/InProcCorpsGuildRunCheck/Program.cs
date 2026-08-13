using System.Reflection;
using System.Text;
using GameSvr;
using GameSvr.Services;
using SystemModule;

// In-process isolated CORPS + GUILD lifecycle harness (machine-safety FIRST: SINGLE process, NO network
// stack, NO DBSvr, NO MySQL, NO background engine threads; strictly serial; Environment.Exit at the end).
// Same technique as InProcEngineRunCheck / InProcSocialRunCheck / InProcHeroRunCheck: bypass GameApp.Initialize
// and the 30s DBSvr gate, and drive the REAL engine lifecycle end-to-end, capturing REAL in-memory state
// mutations (not model stubs).
//
// UNLIKE the social harness's 关系 SKIP (relation had NO in-memory implementation — its only state was the
// MySQL row), the Corps/Gild domain is a RICH in-memory service: NativeCorpsService holds the authoritative
// runtime state (corps rosters, gild membership, relations, concern sets, applications) in-process, with the
// MySQL layer behind the INativeCorpsStore / INativeGildStore INTERFACES. This harness constructs the REAL
// NativeCorpsService from a fake in-memory store and drives the REAL production dispatch entries:
//   * TPlayObject.TryHandleNativeCorpsCoreProtocol / TryHandleNativeCorpsAdminProtocol  (corps ops)
//   * TPlayObject.TryHandleNativeGuildCoreProtocol / TryHandleNativeGuildRelationProtocol (gild ops)
// Each op's log line states WHICH real dispatch entry drove it; every Assert checks a REAL in-memory mutation
// on the real service (roster / officer / gild membership / relation / concern / union flag).
//
// FAKE STORES ARE FAITHFUL, NOT A CHEAT: the write ops are fail-safe / no-rollback BY DESIGN — the real
// production code applies the in-memory mutation even when the store fails; the in-memory state is the
// authoritative runtime truth (MySQL is async persistence reloaded on restart). A no-op store therefore
// reproduces the exact runtime outcome. This is the same evidence-FORM that took combat / social / hero to L4.
//
// VALUE SPLIT: CORPS = the NEWEST evidence — a real member-lifecycle isolation-run
// (RequestJoin->AcceptRequest->member added; DirectAddMember; AppointVice; Dismiss; TransferCaptain; Exit).
// GILD = consolidate the op-level wiring runs into ONE lifecycle (create->concern->war->break->leadership->
// vice->exit->union-flag).
//
// L4->L5 BOUNDARY (open + named, same as every InProc harness): no real SQL round-trip and no client
// transport are exercised. SKIP-NOT-FAKE: the async-queue write ops (SetCorpsNotice / SetGildNotice /
// SetRecruitCondition, which spawn a background NativeSocialPersistenceQueue worker) are deliberately OUT OF
// SCOPE to honor single-thread machine-safety — logged, not faked. The synchronous write ops below fully
// exercise the in-memory lifecycle.
//
// Evidence goes to stdout and inproc_corpsguild_evidence.txt next to the executable.

int rc = 0;
var evidence = new List<string>();
void Log(string s) { evidence.Add(s); Console.WriteLine("  " + s); }
void Assert(bool cond, string msg) { if (!cond) throw new Exception("ASSERT FAILED: " + msg); }

// cached reflection handles for the REAL production dispatch entry points (private on TPlayObject)
MethodInfo Dispatch(string name) =>
    typeof(TPlayObject).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new MissingMethodException("TPlayObject." + name);
var miCorpsCore = Dispatch("TryHandleNativeCorpsCoreProtocol");
var miCorpsAdmin = Dispatch("TryHandleNativeCorpsAdminProtocol");
var miGuildCore = Dispatch("TryHandleNativeGuildCoreProtocol");
var miGuildRelation = Dispatch("TryHandleNativeGuildRelationProtocol");

try
{
    PrepareConfig();
    BootSingletons();
    Log("BOOT singletons: g_Config/UserEngine/ObjectManager/MapManager/StartPointList/SafeZoneList/"
        + "ProcessMsgCriticalSection constructed (no GameApp.Initialize, no DBSvr gate, no network, no threads)");

    CheckDecodePath();
    RunCorpsLifecycle();
    RunGildLifecycle();
    CheckGildRequestAcceptRefuse();

    Console.WriteLine(
        "PASS InProcCorpsGuildRunCheck corps=REAL-dispatch(RequestJoin->AcceptRequest->DirectAdd->AppointVice"
        + "->Dismiss->TransferCaptain->Exit) gild=REAL-dispatch(create->concern->war->break->dismiss-corps->"
        + "vice->exit->union-flag) request-ledger=REAL(join-accept add-to-gild / union-accept DELETE3+INSERT1 /"
        + " union-refuse DELETE3) decode-path=REAL(GetNativeCorpsBody 6-bit id+name) store=fake(no-MySQL,"
        + "fail-safe) asserts=real-in-memory-mutations single-process no-network no-DBSvr no-MySQL");
}
catch (Exception ex)
{
    Console.Error.WriteLine("FAIL InProcCorpsGuildRunCheck: " + ex);
    rc = 1;
}

try { File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "inproc_corpsguild_evidence.txt"), evidence); }
catch { /* evidence file is best-effort */ }

Environment.Exit(rc);

// ===================== decode-path proof (shared GetNativeCorpsBody 6-bit decode) =====================
// Proves the GetNativeCorpsBody fix end-to-end with NO real client: a 6-bit-ENCODED wire body (exactly
// what GateService delivers as Payload) must decode to the exact binary int64 id + GBK name; the pre-fix
// raw-Payload read returned garbage. The id has a HIGH byte (0x85) to expose the GBK-sMsg corruption that
// "read sMsg" would have caused. Every corps+gild body-op inherits this single helper (verified: it is the
// sole .Payload reader), so this one proof covers the family (ids via TryReadId, names via TryDecodeRawText).
void CheckDecodePath()
{
    const long knownId = 0x0000008500000005L;
    var encodedId = EDcode.EncodeBuffer(NativeCorpsWireCodec.EncodeId(knownId)); // 6-bit-encoded wire body

    // FIXED path: the REAL production helper decodes, then TryReadId reads binary int64 @0.
    Assert(NativeCorpsWireCodec.TryReadId(InvokeGetNativeCorpsBody(encodedId), out var idFixed)
           && idFixed == knownId,
        "decode-path: fixed GetNativeCorpsBody + TryReadId recovers the exact int64 id");

    // PRE-FIX path: reading the raw ENCODED Payload directly does NOT recover the id (proves the bug).
    Assert(!(NativeCorpsWireCodec.TryReadId(encodedId, out var idOld) && idOld == knownId),
        "decode-path: pre-fix raw-Payload read does NOT recover the id (was garbage)");

    // NAME op through the SAME single helper: a GBK name survives via TryDecodeRawText on the decoded slice.
    const string knownName = "行会甲";
    var encodedName = EDcode.EncodeBuffer(HUtil32.GbkEncoding.GetBytes(knownName));
    Assert(NativeCorpsWireCodec.TryDecodeRawText(InvokeGetNativeCorpsBody(encodedName), out var nameFixed)
           && nameFixed == knownName,
        "decode-path: fixed GetNativeCorpsBody + TryDecodeRawText recovers the exact GBK name");
    Assert(!(NativeCorpsWireCodec.TryDecodeRawText(encodedName, out var nameOld) && nameOld == knownName),
        "decode-path: pre-fix raw-Payload name read does NOT recover the name");

    // --- RELATION family: the SAME shared helper feeds the relation name codec (TryDecodeName). ---
    // Non-ASCII + ASCII name; under a raw (undecoded) read the encoded bytes decode to gibberish.
    const string relName = "测试A";
    var encodedRel = EDcode.EncodeBuffer(HUtil32.GbkEncoding.GetBytes(relName));
    Assert(NativeRelationWireCodec.TryDecodeName(InvokeGetNativeCorpsBody(encodedRel), out var relFixed)
           && relFixed == relName,
        "decode-path[relation]: shared decode + TryDecodeName recovers the exact name");
    Assert(!(NativeRelationWireCodec.TryDecodeName(encodedRel, out var relOld) && relOld == relName),
        "decode-path[relation]: pre-fix raw-Payload name read does NOT recover the name");

    // --- GROUP family: the SAME shared helper feeds the 16-byte record codec (TryReadNativeGroupShortString). ---
    const string grpName = "队友乙";
    var grpNameBytes = HUtil32.GbkEncoding.GetBytes(grpName);
    var grpRecord = new byte[16];
    grpRecord[0] = (byte)grpNameBytes.Length;
    Buffer.BlockCopy(grpNameBytes, 0, grpRecord, 1, grpNameBytes.Length);
    var encodedGrp = EDcode.EncodeBuffer(grpRecord);
    Assert(TPlayObject.TryReadNativeGroupShortString(InvokeGetNativeCorpsBody(encodedGrp), 0, 15, out var grpFixed)
           && grpFixed == grpName,
        "decode-path[group]: shared decode + TryReadNativeGroupShortString recovers the exact name");
    Assert(!(TPlayObject.TryReadNativeGroupShortString(encodedGrp, 0, 15, out var grpOld) && grpOld == grpName),
        "decode-path[group]: pre-fix raw-Payload record read does NOT recover the name");

    Log($"DECODE-PATH [shared DecodeNativeSocialBody] int64 0x{knownId:X16}->TryReadId, GBK '{knownName}'->TryDecodeRawText, "
        + $"relation '{relName}'->TryDecodeName, group '{grpName}'->TryReadNativeGroupShortString: all decode EXACT; "
        + "pre-fix raw-Payload reads all = garbage (corps/gild + relation + group inherit the one helper)");
}

// Invokes the REAL private static GetNativeCorpsBody against a Payload = the 6-bit-encoded wire body.
byte[] InvokeGetNativeCorpsBody(byte[] encodedPayload)
{
    var m = typeof(TPlayObject).GetMethod("GetNativeCorpsBody",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("TPlayObject.GetNativeCorpsBody");
    return (byte[])m.Invoke(null, new object[]
        { new TProcessMessage { Payload = encodedPayload } });
}

// ===================== CORPS lifecycle (real member-lifecycle isolation-run) =====================

void RunCorpsLifecycle()
{
    var snapshot = new NativeCorpsDataSnapshot();
    var corps = new NativeCorpsSnapshot
    {
        Id = 100, CreateTime = new DateTime(2020, 1, 2), Name = "测试战队", OwnerId = 1
    };
    corps.Members.Add(new NativeCorpsMemberSnapshot
    { MemberId = 1, Name = "队长", Level = 50, LastLoginTime = new DateTime(2020, 1, 2) });
    corps.Members.Add(new NativeCorpsMemberSnapshot
    { MemberId = 2, Name = "老成员", Level = 50, LastLoginTime = new DateTime(2020, 1, 2) });
    snapshot.CorpsById.Add(100, corps);

    var service = BuildService(snapshot);
    Log("CORPS service built from fake INativeCorpsStore snapshot (corps 100 '测试战队' owner=p1, member=p2, "
        + "NOT in a gild); no MySQL");

    // 1. RequestJoin — applicant p10 requests to join corps 100 (real CM_CORPS_REQUEST_JOIN dispatch)
    var r1 = Drive(service, miCorpsCore, 10, Grobal2.CM_CORPS_REQUEST_JOIN, NativeCorpsWireCodec.EncodeId(100));
    bool applied = service.TryGetApplicationCorps(10, out var appCorps) && appCorps.Id == 100;
    Log($"CORPS RequestJoin [real CM_CORPS_REQUEST_JOIN dispatch] p10 -> corps100: result={FirstResult(r1)}; "
        + $"application recorded on service={applied}");
    Assert(FirstResult(r1) == 0 && applied, "real RequestJoin recorded the application in the live service");

    // 2. AcceptRequest — owner p1 accepts applicant p10 (real CM_CORPS_ACCEPT_REQUEST dispatch)
    var r2 = Drive(service, miCorpsAdmin, 1, Grobal2.CM_CORPS_ACCEPT_REQUEST,
        NativeCorpsWireCodec.EncodeId(10), nParam2: 1);
    bool joined = service.TryGetPlayerCorps(10, out var c10) && c10.Id == 100;
    Log($"CORPS AcceptRequest [real CM_CORPS_ACCEPT_REQUEST dispatch] p1 accepts p10: result={FirstResult(r2)}; "
        + $"p10 now a member={joined} (corps.Members={corps.Members.Count})");
    Assert(FirstResult(r2) == 0 && joined, "real AcceptRequest added p10 to corps.Members + member index");

    // 3. DirectAddMember — owner p1 directly adds p11 (real CM_CORPS_DIRECT_ADD_MEMBER dispatch, resolver target)
    var target11 = new TPlayObject { m_boOffLineFlag = true, m_sCharName = "直邀成员", m_boAllowGuild = true };
    target11.LoadNativeMailRecipientId(11);
    var r3 = Drive(service, miCorpsAdmin, 1, Grobal2.CM_CORPS_DIRECT_ADD_MEMBER,
        Array.Empty<byte>(), resolver: () => target11);
    bool direct = service.TryGetPlayerCorps(11, out var c11) && c11.Id == 100;
    Log($"CORPS DirectAddMember [real CM_CORPS_DIRECT_ADD_MEMBER dispatch] p1 adds p11: result={FirstResult(r3)}; "
        + $"p11 now a member={direct}");
    Assert(FirstResult(r3) == 0 && direct, "real DirectAddMember added p11 to the corps");

    // 4. AppointViceCaptain — owner p1 appoints member p2 as vice (real CM_CORPS_APPOINT_VICE_CAPTAIN dispatch)
    var r4 = Drive(service, miCorpsAdmin, 1, Grobal2.CM_CORPS_APPOINT_VICE_CAPTAIN, NativeCorpsWireCodec.EncodeId(2));
    Log($"CORPS AppointViceCaptain [real CM_CORPS_APPOINT_VICE_CAPTAIN dispatch] p1 -> p2: result={FirstResult(r4)}; "
        + $"corps.ViceOwner1Id={corps.ViceOwner1Id}");
    Assert(FirstResult(r4) == 0 && corps.ViceOwner1Id == 2, "real AppointViceCaptain set corps.ViceOwner1Id=p2");

    // 5. DismissMember — owner p1 dismisses ordinary member p11 (real CM_CORPS_DISMISS_MEMBER dispatch)
    var r5 = Drive(service, miCorpsAdmin, 1, Grobal2.CM_CORPS_DISMISS_MEMBER, NativeCorpsWireCodec.EncodeId(11));
    bool dismissed = !service.TryGetPlayerCorps(11, out _);
    Log($"CORPS DismissMember [real CM_CORPS_DISMISS_MEMBER dispatch] p1 dismisses p11: result={FirstResult(r5)}; "
        + $"p11 removed={dismissed}");
    Assert(FirstResult(r5) == 0 && dismissed, "real DismissMember removed p11 from the corps");

    // 6. TransferCaptain — owner p1 transfers captaincy to vice p2 (real CM_CORPS_TRANSFER_CAPTAIN dispatch)
    var r6 = Drive(service, miCorpsAdmin, 1, Grobal2.CM_CORPS_TRANSFER_CAPTAIN, NativeCorpsWireCodec.EncodeId(2));
    Log($"CORPS TransferCaptain [real CM_CORPS_TRANSFER_CAPTAIN dispatch] p1 -> p2: result={FirstResult(r6)}; "
        + $"corps.OwnerId={corps.OwnerId} (former vice slot cleared={corps.ViceOwner1Id})");
    Assert(FirstResult(r6) == 0 && corps.OwnerId == 2, "real TransferCaptain set corps.OwnerId=p2");

    // 7. Exit — former owner p1 (now a plain member) leaves the corps (real CM_CORPS_EXIT dispatch).
    //    Driven in a safe zone: sub_6F57A4 refuses with 37 outside one (see Drive's inSafeZone note).
    var r7 = Drive(service, miCorpsAdmin, 1, Grobal2.CM_CORPS_EXIT, Array.Empty<byte>(), inSafeZone: true);
    bool exited = !service.TryGetPlayerCorps(1, out _);
    Log($"CORPS Exit [real CM_CORPS_EXIT dispatch] p1 leaves: result={FirstResult(r7)}; p1 removed={exited} "
        + $"(corps.Members={corps.Members.Count})");
    Assert(FirstResult(r7) == 0 && exited, "real Exit removed p1 from corps.Members + member index");
}

// ===================== GILD lifecycle (consolidated op-level lifecycle run) =====================

void RunGildLifecycle()
{
    var snapshot = new NativeCorpsDataSnapshot();
    AddCorps(snapshot, 100, 1, "会长战队");    // gild 200 president corps (captain p1)
    AddCorps(snapshot, 300, 3, "副会战队");    // gild 200 vice corps (captain p3)
    AddCorps(snapshot, 500, 5, "成员战队甲");  // gild 200 member corps (captain p5)
    AddCorps(snapshot, 550, 6, "成员战队乙");  // gild 200 member corps (captain p6)
    AddCorps(snapshot, 900, 9, "散人战队");    // NO gild -> founds a new gild (captain p9)
    AddCorps(snapshot, 410, 41, "盟友战队");   // gild 400 owner corps (captain p41)
    AddCorps(snapshot, 610, 61, "路人战队");   // gild 600 owner corps (captain p61)

    AddGild(snapshot, 200, 100, "本方行会", vice: 300, members: new long[] { 100, 300, 500, 550 });
    AddGild(snapshot, 400, 410, "盟友行会", vice: 0, members: new long[] { 410 });
    AddGild(snapshot, 600, 610, "路人行会", vice: 0, members: new long[] { 610 });
    snapshot.GildRelations.Add(
        NativeCorpsDataSnapshot.GildRelationKey(200, 400),
        (NativeCorpsService.GildUnion, DateTime.MinValue));   // 200<->400 allied

    var service = BuildService(snapshot);
    Log("GILD service built from fake stores (gild 200 owner-corps=100/vice-corps=300/members{500,550}; allied "
        + "gild 400; neutral gild 600; gild-less corps 900); no MySQL");

    // 1. Create (4564) — p9/corps900 founds a new gild (real CM_GILD_CREATE via guild-core dispatch)
    var g1 = Drive(service, miGuildCore, 9, Grobal2.CM_GILD_CREATE, HUtil32.GbkEncoding.GetBytes("新兴行会"));
    bool created = service.TryGetGildForCorps(900, out var newGild) && newGild != null;
    Log($"GILD Create [real CM_GILD_CREATE dispatch] p9/corps900 founds '新兴行会': result={FirstResult(g1)}; "
        + $"new gild id={(created ? newGild.Id : 0)}, corps900->gild index published={created}");
    Assert(FirstResult(g1) == 0 && created, "real ApplyGildCreate published a new gild + corps->gild index");

    // 2. AddConcern (4576) — president p1 concerns neutral gild 600 (real relation dispatch); the repeat
    //    reading 1000 (duplicate) proves the in-memory concern set was mutated by the first add.
    var g2 = Drive(service, miGuildRelation, 1, Grobal2.CM_GILD_CONCERN_GILD_ID, NativeCorpsWireCodec.EncodeId(600));
    var g2b = Drive(service, miGuildRelation, 1, Grobal2.CM_GILD_CONCERN_GILD_ID, NativeCorpsWireCodec.EncodeId(600));
    Log($"GILD AddConcern [real CM_GILD_CONCERN_GILD_ID dispatch] p1 concerns gild600: first={FirstResult(g2)}, "
        + $"repeat={FirstResult(g2b)} (repeat=1000 duplicate proves the in-memory concern set now holds 600)");
    Assert(FirstResult(g2) == 0 && FirstResult(g2b) == 1000,
        "real AddConcern mutated the in-memory concern set (repeat reads it back as duplicate 1000)");

    // 3. DeclareWar (4579) — president p1 (gold 40000) declares war on gild 600 (real relation dispatch)
    byte relWarBefore = GildRelation(service, 1, 61);
    var g3 = Drive(service, miGuildRelation, 1, Grobal2.CM_GILD_DECLARE_WAR,
        NativeCorpsWireCodec.EncodeId(600), gold: 40000);
    byte relWarAfter = GildRelation(service, 1, 61);
    Log($"GILD DeclareWar [real CM_GILD_DECLARE_WAR dispatch] p1 -> gild600: result={FirstResult(g3)}; "
        + $"gold 40000->{g3.GoldAfter}; combat gild-relation(p1,p61) {relWarBefore}->{relWarAfter} (2=war)");
    Assert(FirstResult(g3) == 0 && g3.GoldAfter == 10000 && relWarAfter == NativeCorpsService.GildHostile,
        "real DeclareWar published the war relation (observed via GetCombatRelation) + deducted 30000 gold");

    // 4. BreakUnion (4574) — president p1 breaks the seeded union with gild 400 (real relation dispatch)
    byte relAllyBefore = GildRelation(service, 1, 41);
    var g4 = Drive(service, miGuildRelation, 1, Grobal2.CM_GILD_BREAK_UNION, NativeCorpsWireCodec.EncodeId(400));
    byte relAllyAfter = GildRelation(service, 1, 41);
    Log($"GILD BreakUnion [real CM_GILD_BREAK_UNION dispatch] p1 -> gild400: result={FirstResult(g4)}; "
        + $"combat gild-relation(p1,p41) {relAllyBefore}->{relAllyAfter} (0=none)");
    Assert(FirstResult(g4) == 0 && relAllyBefore == NativeCorpsService.GildUnion && relAllyAfter == 0,
        "real BreakUnion removed the union relation (observed via GetCombatRelation)");

    // 5. Leadership dismiss-corps (4567) — president p1 dismisses member corps 550 (real guild-core dispatch)
    var g5 = Drive(service, miGuildCore, 1, Grobal2.CM_GILD_DISMISS_CORPS, NativeCorpsWireCodec.EncodeId(550));
    bool dismissed550 = !GildById(service, 200).CorpsIds.Contains(550) && !service.TryGetGildForCorps(550, out _);
    Log($"GILD DismissCorps [real CM_GILD_DISMISS_CORPS dispatch] p1 dismisses corps550: result={FirstResult(g5)}; "
        + $"corps550 removed from gild + reverse index={dismissed550} (gild.CorpsIds={GildById(service, 200).CorpsIds.Count})");
    Assert(FirstResult(g5) == 0 && dismissed550, "real leadership DismissCorps removed corps550 from the gild");

    // 6. Vice dismiss (4588) — president p1 dismisses vice corps 300 (real relation dispatch)
    var g6 = Drive(service, miGuildRelation, 1, Grobal2.CM_GILD_DISMISS_VICECAPTAIN, NativeCorpsWireCodec.EncodeId(300));
    bool viceCleared = GildById(service, 200).ViceOwnerId == 0;
    Log($"GILD DismissVice [real CM_GILD_DISMISS_VICECAPTAIN dispatch] p1 dismisses vice corps300: "
        + $"result={FirstResult(g6)}; gild.ViceOwnerId={GildById(service, 200).ViceOwnerId}");
    Assert(FirstResult(g6) == 0 && viceCleared, "real vice-dismiss cleared gild.ViceOwnerId");

    // 7. Exit (4583) — member corps 500 (p5) leaves the gild (real relation dispatch).
    //    Driven in a safe zone: sub_6F6BF8 refuses with 38 outside one (see Drive's inSafeZone note).
    var g7 = Drive(service, miGuildRelation, 5, Grobal2.CM_GILD_EXIT, Array.Empty<byte>(), inSafeZone: true);
    bool exited500 = !GildById(service, 200).CorpsIds.Contains(500) && !service.TryGetGildForCorps(500, out _);
    Log($"GILD Exit [real CM_GILD_EXIT dispatch] p5/corps500 leaves gild200: result={FirstResult(g7)}; "
        + $"corps500 removed from gild + reverse index={exited500}");
    Assert(FirstResult(g7) == 0 && exited500, "real gild Exit removed corps500 from the gild");

    // 8. EnableUnion (4581) — president p1 flips the session union flag (real relation dispatch)
    var g8 = Drive(service, miGuildRelation, 1, Grobal2.CM_GILD_ENABLE_UNION, new[] { (byte)1 });
    bool unionOn = GildById(service, 200).UnionEnabled;
    Log($"GILD EnableUnion [real CM_GILD_ENABLE_UNION dispatch] p1: result={FirstResult(g8)}; "
        + $"gild.UnionEnabled={unionOn} (session flag, no gamedata column)");
    Assert(FirstResult(g8) == 0 && unionOn, "real EnableUnion set the in-memory session union flag");

    Log("GILD SKIP (not faked, machine-safety): SetCorpsNotice/SetGildNotice/SetRecruitCondition enqueue to the "
        + "async NativeSocialPersistenceQueue (Task.Run background worker) — out of scope for single-thread "
        + "isolation; the synchronous write ops above fully exercise the in-memory corps/gild lifecycle.");
}

// ===================== helpers =====================

// GILD request-ledger accept/refuse (dormant 2b model). Drives the not-yet-CM-wired ApplyGildRequestJoin/
// Union + ApplyGildAcceptRequest/ApplyGildRefuseRequest DIRECTLY on the REAL service (the live 4611/4572
// hook is HELD). Verifies the risky membership/relation WRITES: JOIN accept adds the applicant corps to the
// president's gild (sub_706264); UNION accept = DELETE-3 + INSERT Relation-1 on the canonical pair
// (sub_708168 save_relation); UNION refuse = DELETE-3 only (sub_708004), no re-insert; both consume the
// pending request. A TrackingGildStore records the relation INSERT/DELETE so the pending-Relation-3
// lifecycle is observable (FakeGildStore is a pure no-op).
void CheckGildRequestAcceptRefuse()
{
    NativeCorpsService NewService(out TrackingGildStore store)
    {
        var snap = new NativeCorpsDataSnapshot();
        AddCorps(snap, 100, 1, "会长战队");      // gild 200 owner corps (president p1)
        AddCorps(snap, 410, 41, "盟友会长战队");  // gild 400 owner corps (president p41)
        AddCorps(snap, 900, 9, "散人战队");       // applicant corps (captain p9), NOT in a gild
        AddGild(snap, 200, 100, "本方行会", 0, new long[] { 100 });
        AddGild(snap, 400, 410, "盟友行会", 0, new long[] { 410 });
        store = new TrackingGildStore();
        if (!NativeCorpsService.TryCreate(new FakeCorpsStore(snap), out var svc,
                out var err, store))
            throw new Exception("NativeCorpsService.TryCreate failed: " + err);
        return svc;
    }

    // 1. JOIN accept -> add the applicant corps to the president's gild (in-memory membership write).
    // The accept/refuse KEY is the request's generated UniqueId (the 4611/4572 CM body carries ONLY that,
    // NOT the applicant CharID); each fresh NewService's first request gets UniqueId=1 (monotonic). Q3's
    // 4570/4571 listing will expose the id for robust capture; the deterministic value suffices here.
    const long firstReqId = 1;
    var s1 = NewService(out _);
    Assert(s1.ApplyGildRequestJoin(9, 200) == 0, "join request created");
    var joinResult = s1.ApplyGildAcceptRequest(1, firstReqId);
    var joined = GildById(s1, 200).CorpsIds.Contains(900)
        && s1.TryGetGildForCorps(900, out var jg) && jg.Id == 200;
    Assert(joinResult == 0 && joined,
        "JOIN accept added applicant corps900 to gild200 (sub_706264 add-to-gild)");
    Assert(s1.ApplyGildAcceptRequest(1, firstReqId) == 10,
        "join request consumed on accept (re-accept = RequestNotFound 10)");
    Log($"LEDGER JOIN accept [ApplyGildAcceptRequest] p1 accepts p9: result={joinResult}; "
        + $"corps900 in gild200={joined}");

    // 2. UNION accept -> DELETE pending Relation-3 + INSERT Relation-1 (in-memory union + persisted pair).
    var s2 = NewService(out var store2);
    Assert(s2.ApplyGildEnableUnion(41, true) == 0,
        "gild400 (target) enables union — session flag, set at runtime");
    Assert(s2.ApplyGildRequestUnion(1, "盟友行会") == 0, "union request created (Relation-3 pending)");
    // save_relation 0x5E6F19 `33C9 xor ecx,ecx` / 0x5E6F1B `8ACB mov cl,bl` / 0x5E6F23 `call 0x49F9C8`
    // stores the raw type, so a pending 3 IS in the relation map; the loader agrees
    // (0x5E8D83 `2C04 sub al,4` admits 0..3, 0x5E8EB5 adds unconditionally).
    Assert(GildRelation(s2, 1, 41) == NativeCorpsService.GildPendingUnion,
        "pending union publishes Relation-3 into the in-memory relation map");
    var unionResult = s2.ApplyGildAcceptRequest(41, firstReqId);
    Assert(unionResult == 0 && GildRelation(s2, 1, 41) == NativeCorpsService.GildUnion,
        "UNION accept: DELETE-3 + INSERT-1 -> in-memory union relation");
    Assert(store2.Deletes.Contains((200L, 400L))
        && store2.Inserts.Contains((200L, 400L, 1)),
        "UNION accept persisted DELETE then INSERT Relation-1 on the canonical (min,max) pair");
    Log($"LEDGER UNION accept [ApplyGildAcceptRequest] p41 accepts p1: result={unionResult}; "
        + $"relation(200,400)=union={GildRelation(s2, 1, 41) == NativeCorpsService.GildUnion}");

    // 3. UNION refuse -> DELETE Relation-3 only (no re-insert), request consumed.
    var s3 = NewService(out var store3);
    Assert(s3.ApplyGildEnableUnion(41, true) == 0,
        "gild400 (target) enables union — session flag, set at runtime");
    Assert(s3.ApplyGildRequestUnion(1, "盟友行会") == 0, "union request created (refuse setup)");
    var refuseResult = s3.ApplyGildRefuseRequest(41, firstReqId);
    Assert(refuseResult == 0 && store3.Deletes.Contains((200L, 400L))
        && !store3.Inserts.Any(insert =>
            insert.Relation == NativeCorpsService.GildUnion),
        "UNION refuse: DELETE-3 only (no Relation-1 re-insert)");
    Assert(GildRelation(s3, 1, 41) == 0, "union refuse leaves no in-memory relation");
    Assert(s3.ApplyGildRefuseRequest(41, firstReqId) == 10,
        "union request consumed on refuse (re-refuse = RequestNotFound 10)");
    Log($"LEDGER UNION refuse [ApplyGildRefuseRequest] p41 refuses p1: result={refuseResult}; "
        + "Relation-3 deleted + record consumed");

    // 4. RequestNotFound ladder.
    var s4 = NewService(out _);
    Assert(s4.ApplyGildAcceptRequest(1, 99999) == 10,
        "accept non-existent applicant -> RequestNotFound 10");

    // 5. 4570 record ABI (sub_70839C): requester at +0x00, the UNIQUE-ID at +0x08 — closes the masking gap
    //    (the accept/refuse key IS what the 4570/4571 listing echoes, so the id round-trip is real).
    var s5 = NewService(out _);
    Assert(s5.ApplyGildRequestJoin(9, 200) == 0, "join request for record-ABI");
    var recPage = s5.GetGildJoinRequestPage(1, 0, 32, out var recResult);
    Assert(recResult == 0 && recPage.Count == 1, "4570 page returns the pending join request");
    var recBytes = NativeCorpsWireCodec.EncodeGildRequestSummaries(recPage);
    Assert(recBytes.Length == 56, "4570 record is 56 bytes");
    Assert(BitConverter.ToInt64(recBytes, 0) == 900,
        "4570 record +0x00 = requester (applicant corps900)");
    Assert(BitConverter.ToInt64(recBytes, 8) == firstReqId,
        "4570 record +0x08 = the UNIQUE request id (the accept/refuse key the client echoes back)");
    Log($"LEDGER 4570 record ABI: requester@+0x00={BitConverter.ToInt64(recBytes, 0)}, "
        + $"uniqueId@+0x08={BitConverter.ToInt64(recBytes, 8)} (56-byte sub_70839C)");

    // 6. 4570 wire->field MAPPING (default ProcessUserMessage case: pageSize = Tag = nParam3, pageIndex =
    //    Series = wParam). Drive the REAL handler with DISTINCT fields so a wrong field is CAUGHT: wParam=0
    //    (page index), nParam3=32 (page size), nParam2=99 (decoy Param). With one pending request the reply
    //    must echo pageIndex 0 in Param and 1 record in Tag; reading page index/size from any other field
    //    (e.g. the earlier wrong swap pageSize=nParam2) breaks it (empty page / wrong Param).
    var s6 = NewService(out _);
    Assert(s6.ApplyGildRequestJoin(9, 200) == 0, "4570 mapping setup request");
    var mapPackets = new List<(ClientPacket Header, byte[] Body)>();
    var mapPlayer = new TPlayObject { m_boOffLineFlag = true, m_sCharName = "op1" };
    mapPlayer.LoadNativeMailRecipientId(1);   // president p1 owns gild200
    mapPlayer.SetNativeCorpsServiceForTests(s6,
        (header, body) => mapPackets.Add((header, body)), null);
    Assert((bool)miGuildRelation.Invoke(mapPlayer, new object[]
    {
        new TProcessMessage
        {
            wIdent = Grobal2.CM_GILD_QUERY_REQUEST_JOIN_LIST,
            wParam = 0, nParam3 = 8, nParam2 = 99
        }
    }), "4570 was not claimed by the relation dispatch");
    Assert(mapPackets.Count == 1 && mapPackets[0].Header.Param == 0
        && mapPackets[0].Header.Tag == 1,
        "4570 reads pageIndex=wParam (Series) + pageSize=nParam3 (Tag): page 0 -> exactly 1 record");
    Log($"LEDGER 4570 mapping: pageIndex=wParam / pageSize=nParam3 -> Param(page)="
        + $"{mapPackets[0].Header.Param}, Tag(records)={mapPackets[0].Header.Tag}");
}

NativeCorpsService BuildService(NativeCorpsDataSnapshot snapshot)
{
    if (!NativeCorpsService.TryCreate(new FakeCorpsStore(snapshot), out var service, out var error,
            new FakeGildStore()))
        throw new Exception("NativeCorpsService.TryCreate failed: " + error);
    return service;
}

// Drives ONE real dispatch entry against the shared live service with a fresh offline operator player.
// Returns the packets the dispatcher emitted (via the test sink) plus the operator's gold after the call.
// inSafeZone: both EXIT ops are gated on sub_76858C (InSafeZone) in the original and reply with a
// refusal code instead of leaving when it is false -- corps 4538 sub_6F57A4 (006F57AE e8d92d0700
// call 0x76858C / 006F57B3 84c0 test al,al / 006F57B5 750a jne / 006F57B7 be25000000 mov esi,0x25 => 37)
// and gild 4583 sub_6F6BF8 (006F6C02 e885190700 call 0x76858C / 006F6C07 84c0 / 006F6C09 7507 jne /
// 006F6C0B be26000000 mov esi,0x26 => 38), both verified byte-for-byte against flat_image.bin. A bare
// offline TPlayObject has m_PEnvir == null, which InSafeZone maps to FALSE (TBaseObject.cs:5455), so the
// leaver must be given a boSAFE map to reach the removal path those steps assert on -- the same fixture
// shape NativeCorpsProtocolCheck.BuildExitPlayer already uses. No other op in this harness reads it.
(List<(ClientPacket Header, byte[] Body)> Packets, int GoldAfter) Drive(
    NativeCorpsService service, MethodInfo dispatch, long operatorId, int ident,
    byte[] payload, int nParam2 = 0, int gold = 0, Func<TPlayObject> resolver = null,
    bool inSafeZone = false)
{
    var packets = new List<(ClientPacket Header, byte[] Body)>();
    var player = new TPlayObject
    {
        m_boOffLineFlag = true, m_sCharName = "op" + operatorId, m_nGold = gold
    };
    if (inSafeZone)
    {
        // boFightZone/boFight3Zone stay false so the 28 gate cannot fire.
        var environment = new Envirnoment { sMapName = "CORPSGUILD_EXIT_TEST" };
        environment.Flag.boSAFE = true;
        player.m_PEnvir = environment;
    }
    player.LoadNativeMailRecipientId(operatorId);
    player.SetNativeCorpsServiceForTests(service, (header, body) => packets.Add((header, body)), resolver);

    var handled = (bool)dispatch.Invoke(player, new object[]
    {
        // PRODUCTION-faithful wire body: Payload = the 6-bit-ENCODED body (GateService
        // delivers encoded; the handler decodes via GetNativeCorpsBody). Feeding raw-binary
        // Payloads earlier MASKED the decode bug — encode here so the decode path is exercised.
        new TProcessMessage
        {
            wIdent = ident,
            Payload = EDcode.EncodeBuffer(payload ?? Array.Empty<byte>()),
            nParam2 = nParam2
        }
    });
    if (!handled) throw new Exception($"ident {ident} was not claimed by {dispatch.Name}");
    return (packets, player.m_nGold);
}

int FirstResult((List<(ClientPacket Header, byte[] Body)> Packets, int GoldAfter) driven) =>
    driven.Packets.Count > 0 ? driven.Packets[0].Header.Param : int.MinValue;

NativeGildSnapshot GildById(NativeCorpsService service, long gildId)
{
    foreach (var gild in service.SnapshotGilds())
        if (gild.Id == gildId)
            return gild;
    throw new Exception("gild not found in live service: " + gildId);
}

byte GildRelation(NativeCorpsService service, long selfPlayerId, long targetPlayerId)
{
    service.GetCombatRelation(selfPlayerId, targetPlayerId, out _, out _, out _, out _, out _, out _,
        out var gildRelation);
    return gildRelation;
}

void AddCorps(NativeCorpsDataSnapshot snapshot, long id, long ownerId, string name)
{
    var corps = new NativeCorpsSnapshot
    { Id = id, CreateTime = new DateTime(2020, 1, 2), Name = name, OwnerId = ownerId };
    corps.Members.Add(new NativeCorpsMemberSnapshot
    { MemberId = ownerId, Name = "队长" + ownerId, Level = 50, LastLoginTime = new DateTime(2020, 1, 2) });
    snapshot.CorpsById.Add(id, corps);
}

void AddGild(NativeCorpsDataSnapshot snapshot, long id, long ownerCorpsId, string name, long vice, long[] members)
{
    var gild = new NativeGildSnapshot
    {
        Id = id, CreateTime = new DateTime(2020, 1, 2), Name = name,
        OwnerCorpsId = ownerCorpsId, ViceOwnerId = vice, Notice = HUtil32.GbkEncoding.GetBytes("行会公告")
    };
    foreach (var corpsId in members) gild.CorpsIds.Add(corpsId);
    snapshot.GildById.Add(id, gild);
}

void PrepareConfig()
{
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);   // GBK (code page 936) for gild names
    var baseDir = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(baseDir, "!Setup.txt"), "[Server]\r\n");
    File.WriteAllText(Path.Combine(baseDir, "String.ini"), "[String]\r\n");
    File.WriteAllText(Path.Combine(baseDir, "Command.conf"), "[Command]\r\n");
    var share = Path.GetFullPath(Path.Combine(baseDir, "..", "Share"));
    Directory.CreateDirectory(share);
    File.WriteAllText(Path.Combine(share, "PlayerUpgradeExp.ini"), "[PlayerLevelExp]\r\nLEVEL_1=50\r\n");
    File.WriteAllText(Path.Combine(share, "ServerData.ini"), "[Integer]\r\n");
}

void BootSingletons()
{
    // Exactly the singleton set the gild wiring checks prove sufficient for these dispatch paths; the
    // declare-war gold deduction (GoldChanged -> SendUpdateMsg) enters ProcessMsgCriticalSection.
    M2Share.g_Config = new GameSvrConfig();
    M2Share.RandomNumber = RandomNumber.GetInstance();
    M2Share.UserEngine = new UserEngine();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.MapManager = new MapManager();
    M2Share.StartPointList = new List<TStartPoint>();
    M2Share.SafeZoneList = new List<TSafeZoneArea>();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new System.Collections.ArrayList();
}

// ===================== fakes (verbatim from the gild wiring checks: no MySQL) =====================

// Minimal in-memory INativeCorpsStore: TryLoad returns the hand-built snapshot; every write returns true
// (no MySQL). Faithful because the corps-member ops require the store bool to proceed and the gild ops are
// fail-safe/no-rollback — a true no-op reproduces the authoritative in-memory outcome.
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

// In-memory INativeGildStore: every write returns true (no MySQL). The gild write ops mutate in-memory first
// then push here fail-safe (no rollback), so a true no-op is faithful to the runtime outcome.
sealed class FakeGildStore : INativeGildStore
{
    public bool TrySaveGild(long gildId, long ownerCorpsId, long viceOwnerId, byte[] notice, out string error)
    { error = string.Empty; return true; }

    public bool TryDeleteGildMember(long gildId, long corpsId, out string error)
    { error = string.Empty; return true; }

    public bool TryCreateGild(long gildId, string name, long ownerCorpsId, long viceOwnerId, out string error)
    { error = string.Empty; return true; }

    public bool TryInsertGildMember(long gildId, long corpsId, out string error)
    { error = string.Empty; return true; }

    public bool TryInsertGildRelation(long gildId1, long gildId2, int relation, DateTime createTime, out string error)
    { error = string.Empty; return true; }

    public bool TryDeleteGildRelation(long gildId1, long gildId2, out string error)
    { error = string.Empty; return true; }

    public bool TryInsertGildConcern(long gildId, long destinationGildId, out string error)
    { error = string.Empty; return true; }

    public bool TryDeleteGildConcern(long gildId, long destinationGildId, out string error)
    { error = string.Empty; return true; }
}

// INativeGildStore that RECORDS the relation INSERT/DELETE calls (still a fail-safe no-op otherwise), so the
// pending-Relation-3 lifecycle (union request INSERT-3 -> accept DELETE-3+INSERT-1 / refuse DELETE-3)
// is observable in CheckGildRequestAcceptRefuse. All other writes return true like FakeGildStore.
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
