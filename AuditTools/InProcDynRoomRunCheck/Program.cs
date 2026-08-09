using System.Reflection;
using GameSvr;
using SystemModule;

// In-process isolated-engine DYNAMIC-ROOM harness (machine-safety FIRST: SINGLE process, NO network stack,
// NO DBSvr, NO MySQL, NO background engine threads; strictly serial; deterministic injected clock;
// Environment.Exit at the end). Same technique as InProcEngineRunCheck / InProcHeroRunCheck /
// InProcMailRunCheck: construct the engine pieces directly (bypassing GameApp.Initialize / StartEngine and
// the 30s DBSvr native-def gate) and drive the REAL dynamic-room lifecycle state machine end-to-end,
// capturing the real in-memory state mutations (not model stubs).
//
// What this proves is ISOLABLE and RUNS on real methods: the native dynamic-room LIFECYCLE STATE MACHINE
// (魔王岭/天关/活动簇 room instancing) — the exact "回收/重启" machinery the domain's L3 hold names — driven
// through the REAL NativeDynamicRoomManager + a REAL Envirnoment, with a deterministic injected clock:
//   * REGISTER : NativeDynamicRoomManager.RegisterIdleRoom -> Envirnoment.ConfigureDynamicRoom
//                (IsDynamicRoom=true, DynamicRoomState=0). Real in-memory room registry mutation.
//   * ACTIVATE : NativeDynamicRoomManager.TryReserveIdleRoom -> lease + DynamicRoomState 0->2, lease index
//                assigned; TryGetActiveRoom resolves the exact active Envirnoment (no static name lookup).
//   * ENTER    : the REAL Envirnoment.AddToMap(OS_MOVINGOBJECT, player) -> AddDynamicRoomPlayer ->
//                DynamicRoomPlayerCount 0->1 (a real player carries CountsAsPlayerPresence == RC_PLAYOBJECT).
//   * LEAVE    : the REAL Envirnoment.RemoveMovingObjectEverywhereExact(player, notify:true) ->
//                DynamicRoomPlayerCount 1->0 -> NotifyPlayerRemoved -> manager begins closing: State 2->1.
//   * RECYCLE  : advance the injected clock past the native ClosingMilliseconds, then the REAL
//                NativeDynamicRoomManager.Run() finalizes the idle room: State 1->0, lease released.
//   * REUSE    : a second TryReserveIdleRoom re-activates the recycled physical room (State 0->2), proving
//                the recycled room returns to the idle pool (the "重启" reuse path).
//
// SKIP'd (documented, not faked — see RunSkips): everything the full production owner NativeDynamicRoomService
// layers on top and that hard-requires the full stack — file-backed definitions (PsDynNpc.txt + .map files),
// NPC materialization/ownership, the event-activation adapter + PAS script routes, the map teleport with NPC
// binding, physical retirement full-destroy (needs NPC ownership), the 9-class FieldHero AI (dormant by
// design: TFieldHero.Run/Initialize are sealed overrides that THROW ProductionNoGoReason), and the
// process-wide native RNG owner sequence. The manager's cleanup HOOKS are service-provided, so this harness
// passes null hooks: the STATE TRANSITIONS run for real; the NPC/item/event cleanup body is SKIP'd.
//
// Evidence goes to stdout and inproc_dynroom_evidence.txt next to the executable.

int rc = 0;
var evidence = new List<string>();
void Log(string s) { evidence.Add(s); Console.WriteLine("  " + s); }
void Assert(bool cond, string msg) { if (!cond) throw new Exception("ASSERT FAILED: " + msg); }

// non-public real Envirnoment entry points driven by reflection (same idiom as the sibling harnesses)
var miInitialize = typeof(Envirnoment).GetMethod("Initialize",
    BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new MissingMethodException("Envirnoment.Initialize");
var piDynState = typeof(Envirnoment).GetProperty("DynamicRoomState",
    BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new MissingMemberException("Envirnoment.DynamicRoomState");
var miRemoveEverywhere = typeof(Envirnoment).GetMethod("RemoveMovingObjectEverywhereExact",
    BindingFlags.Instance | BindingFlags.NonPublic, null,
    new[] { typeof(TBaseObject), typeof(bool) }, null)
    ?? throw new MissingMethodException("Envirnoment.RemoveMovingObjectEverywhereExact");

int DynState(Envirnoment room) => (int)piDynState.GetValue(room);
int RemoveEverywhere(Envirnoment room, TBaseObject actor, bool notify) =>
    (int)miRemoveEverywhere.Invoke(room, new object[] { actor, notify });

// deterministic injected clock: the manager reads this closure for every timestamp/tick decision, so the
// whole lifecycle is driven with no real time and no background threads.
long clock = 1_000;
const string RoomName = "测试动态房";
const long ClosingMs = 10 * 60 * 1000;   // NativeDynamicRoomManager.ClosingMilliseconds (state 1->0 gate)

try
{
    PrepareConfig();
    BootSingletons();
    Log("BOOT singletons: g_Config/RandomNumber/ObjectManager/UserEngine/MapManager constructed "
        + "(no GameApp.Initialize, no DBSvr gate, no network, no background threads)");

    var manager = new NativeDynamicRoomManager(() => clock);   // injected deterministic clock
    var room = CreateDynRoomEnvironment(48, 48, "dynroom-harness");
    Log($"MANAGER new NativeDynamicRoomManager(injected clock); ROOM Envirnoment built in-memory "
        + $"'{room.sMapName}' {room.wWidth}x{room.wHeight} (real Envirnoment.Initialize, no runtime attached "
        + "-> real no-runtime *Core lifecycle path)");

    RunDynRoomLifecycle(manager, room);
    RunSkips();

    Console.WriteLine(
        "PASS InProcDynRoomRunCheck register=REAL(RegisterIdleRoom->ConfigureDynamicRoom,State=0) "
        + "activate=REAL(TryReserveIdleRoom->State 0->2,lease) enter=REAL(AddToMap->DynamicRoomPlayerCount 0->1) "
        + "leave=REAL(RemoveMovingObjectEverywhereExact->count 1->0->NotifyPlayerRemoved->State 2->1) "
        + "recycle=REAL(Run()->State 1->0,lease released) reuse=REAL(re-reserve->State 0->2) "
        + "service/NPC/events/FieldHero-AI/global-RNG=SKIP single-process no-network no-DBSvr no-MySQL");
}
catch (Exception ex)
{
    Console.Error.WriteLine("FAIL InProcDynRoomRunCheck: " + ex);
    rc = 1;
}

try { File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "inproc_dynroom_evidence.txt"), evidence); }
catch { /* evidence file is best-effort */ }

// Hard-exit so no lingering engine state can keep the process alive.
Environment.Exit(rc);

// ===================== dynamic-room lifecycle =====================

void RunDynRoomLifecycle(NativeDynamicRoomManager manager, Envirnoment room)
{
    // ---- 1. REGISTER an idle physical room into the manager pool (real in-memory registry) ------------
    // The 3-arg overload passes null cleanup hooks + minimumActiveMinutes=0, so the state machine runs
    // without the service-provided NPC/event cleanup (those are the documented SKIP).
    bool registered = manager.RegisterIdleRoom(RoomName, physicalInstanceId: 0, room);
    Log($"REGISTER RegisterIdleRoom('{RoomName}',0): registered={registered}; IsDynamicRoom={room.IsDynamicRoom} "
        + $"DynamicRoomState={DynState(room)} DynamicRoomName='{room.DynamicRoomName}'");
    Assert(registered, "real RegisterIdleRoom registered the physical room into the manager pool");
    Assert(room.IsDynamicRoom, "real ConfigureDynamicRoom flagged the Envirnoment as a dynamic room");
    Assert(DynState(room) == 0, "a freshly registered room is idle (DynamicRoomState=0)");

    // ---- 2. ACTIVATE: reserve the idle room -> real lease + DynamicRoomState 0->2 ---------------------
    var entrant = NewPlayer("dynroom-entrant", room, 10, 10);
    bool reserved = manager.TryReserveIdleRoom(RoomName, entrant, out int roomIndex);
    bool activeResolves = manager.TryGetActiveRoom(RoomName, roomIndex, out var activeRoom);
    Log($"ACTIVATE TryReserveIdleRoom('{RoomName}'): reserved={reserved} roomIndex={roomIndex}; "
        + $"DynamicRoomState 0->{DynState(room)}; TryGetActiveRoom resolves-exact={activeResolves && ReferenceEquals(activeRoom, room)}");
    Assert(reserved && roomIndex >= 0, "real TryReserveIdleRoom activated an idle room and returned a lease index");
    Assert(DynState(room) == 2, "real activation moved the room to active (DynamicRoomState=2)");
    Assert(activeResolves && ReferenceEquals(activeRoom, room),
        "real TryGetActiveRoom resolves the exact active Envirnoment by lease index (no static name lookup)");

    // ---- 3. ENTER: a real player enters the active room via the real map add -------------------------
    int count0 = room.DynamicRoomPlayerCount;
    var added = room.AddToMap(10, 10, CellType.OS_MOVINGOBJECT, entrant);   // real AddDynamicRoomPlayer
    int count1 = room.DynamicRoomPlayerCount;
    Log($"ENTER AddToMap(OS_MOVINGOBJECT '{entrant.m_sCharName}'): added={added != null} onMap={entrant.m_boAddToMaped}; "
        + $"DynamicRoomPlayerCount {count0}->{count1} (real AddDynamicRoomPlayer, CountsAsPlayerPresence=RC_PLAYOBJECT)");
    Assert(added != null && entrant.m_boAddToMaped, "real AddToMap placed the player on the room map cell");
    Assert(count1 == count0 + 1, "real map add incremented the room's live player membership");

    // ---- 4. LEAVE: the real removal notifies the manager -> begin closing (State 2->1) ---------------
    clock += 5 * 60 * 1000;   // advance past minimumActiveMinutes(0); ActiveTick was stamped at reserve
    int removed = RemoveEverywhere(room, entrant, notify: true);   // real leave -> NotifyPlayerRemoved
    int count2 = room.DynamicRoomPlayerCount;
    Log($"LEAVE RemoveMovingObjectEverywhereExact(notify): removedCells={removed}; "
        + $"DynamicRoomPlayerCount {count1}->{count2}; DynamicRoomState 2->{DynState(room)} "
        + "(real NotifyPlayerRemoved -> BeginClosing)");
    Assert(count2 == 0, "real removal cleared the room's live player membership");
    Assert(DynState(room) == 1, "empty active room past its minimum began closing (DynamicRoomState=1)");

    // ---- 5. RECYCLE: advance the clock past ClosingMilliseconds; real Run() finalizes to idle --------
    clock += ClosingMs + 1;
    manager.Run();   // real lifecycle tick (no runtime attached -> RunCore)
    Log($"RECYCLE Run() after +{ClosingMs + 1}ms: DynamicRoomState 1->{DynState(room)}; "
        + "IsDynamicRoom=" + room.IsDynamicRoom + " (real FinalizeIdleCleanup released the lease)");
    Assert(DynState(room) == 0, "real Run() finalized the idle room back to the free pool (DynamicRoomState=0)");
    Assert(room.DynamicRoomPlayerCount == 0, "recycled room holds no players");

    // ---- 6. REUSE: the recycled physical room re-activates from the idle pool -------------------------
    bool reReserved = manager.TryReserveIdleRoom(RoomName, null, out int roomIndex2);
    Log($"REUSE TryReserveIdleRoom('{RoomName}') again: reReserved={reReserved} roomIndex={roomIndex2}; "
        + $"DynamicRoomState 0->{DynState(room)} (recycled room returned to the pool and re-activated)");
    Assert(reReserved && DynState(room) == 2,
        "real recycled room returned to the idle pool and re-activated (proves the reuse/重启 path)");
}

void RunSkips()
{
    Log("SERVICE SKIP: NativeDynamicRoomService (the production owner) hard-requires the full stack — "
        + "file-backed defs (PsDynNpc.txt + .map validation), NPC materialization/ownership, the event-"
        + "activation adapter + PAS script routes, and the teleport-with-binding. The manager LIFECYCLE it "
        + "delegates to is driven live above; the service scaffolding is SKIP'd. Not faked.");
    Log("CLEANUP-HOOK SKIP: the manager's BeginClosingCleanup/FinalizeIdleCleanup/CloseActivationEvents and "
        + "physical FullDestroy hooks are service-provided (NPC/item/event teardown). This harness passes null "
        + "hooks, so the STATE TRANSITIONS (2->1->0, lease release, reuse) run for real while the cleanup body "
        + "is SKIP'd — the documented boundary, not a fake run.");
    Log("FIELDHERO-AI SKIP: the 9-class FieldHero (战神) AI is dormant BY DESIGN — TFieldHero.Run()/Initialize() "
        + "are sealed overrides that THROW ProductionNoGoReason (process-wide native RNG owner + magic/equipment "
        + "executors + full Run orchestration not connected); its ctors are internal and need a WantWarMon spawn "
        + "plan. Only TFieldWarHero.CalculateNativeAbility (pure nine-classes math) runs, already covered by "
        + "InProcEngineRunCheck.RunFieldHero. No live FieldHero AI/attack RUN is possible without faking past the "
        + "sealed NO-GO. GLOBAL-RNG-SEQUENCE SKIP: the room lifecycle uses the injected clock, not the process "
        + "RNG owner, so it is independent of that still-open gate.");
}

// ===================== helpers (shared idiom with the sibling harnesses) ========================

Envirnoment CreateDynRoomEnvironment(short w, short h, string name)
{
    // A real, file-less Envirnoment: private Envirnoment.Initialize(short,short) allocates all cell arrays;
    // default CellAttribute (0) is walkable. Not registered in MapManager — a dynamic room is owned by the
    // manager, not the global named-map registry.
    var room = new Envirnoment { sMapName = name, sMapDesc = name, m_sMapFileName = name };
    miInitialize.Invoke(room, new object[] { w, h });
    room.Flag = new TMapFlag();
    return room;
}

TPlayObject NewPlayer(string name, Envirnoment room, short x, short y)
{
    // Offline keeps any SendSocket a no-op; ghost=false + the ctor's RC_PLAYOBJECT keep CountsAsPlayerPresence
    // true so the room membership counter tracks this player. Not added to any map here — the ENTER step
    // drives the real AddToMap explicitly so its mutation is asserted.
    var p = new TPlayObject
    {
        m_boOffLineFlag = true, m_boGhost = false, m_boDeath = false,
        m_sCharName = name, m_sMapName = room.sMapName, m_PEnvir = room,
        m_nCurrX = x, m_nCurrY = y
    };
    p.m_Abil.Level = 30;
    return p;
}

void PrepareConfig()
{
    var baseDir = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(baseDir, "!Setup.txt"), "[Server]\r\n");
    File.WriteAllText(Path.Combine(baseDir, "String.ini"), "[String]\r\n");
    File.WriteAllText(Path.Combine(baseDir, "Command.conf"), "[Command]\r\n");
    var share = Path.GetFullPath(Path.Combine(baseDir, "..", "Share"));
    Directory.CreateDirectory(share);
    File.WriteAllText(Path.Combine(share, "PlayerUpgradeExp.ini"), "[PlayerLevelExp]\r\nLEVEL_1=50\r\n");
}

void BootSingletons()
{
    M2Share.g_Config = new GameSvrConfig();
    M2Share.RandomNumber = RandomNumber.GetInstance();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.UserEngine = new UserEngine();
    M2Share.MapManager = new MapManager();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new System.Collections.ArrayList();
}
