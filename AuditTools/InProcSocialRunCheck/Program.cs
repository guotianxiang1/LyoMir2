using System.Reflection;
using GameSvr;
using GameSvr.Services;
using SystemModule;

// In-process isolated-engine SOCIAL harness (machine-safety FIRST: SINGLE process, NO network stack,
// NO DBSvr, NO MySQL, NO background engine threads; strictly serial; Environment.Exit at the end).
// Same technique as InProcEngineRunCheck: construct the M2Share engine singletons directly (bypassing
// GameApp.Initialize / StartEngine and the 30s DBSvr native-definition gate) and drive the REAL engine
// social flows, capturing the real in-memory state mutations (not model stubs).
//
// Domains:
//   TEAM / GROUP : the real TPlayObject.ClientCreateGroup + ClientAddGroupMember pipeline, resolving
//                  members through the real UserEngine.GetPlayObject and mutating real
//                  m_GroupMembers / m_GroupOwner state (JoinGroup). RUNS.
//   CHANNEL      : the real NativeChannelManager.CreatePublic + Enter + QueryById on the shared
//                  in-memory manager, mutating the real channel membership set. RUNS.
//   RELATION     : SKIPPED (documented, not faked). The only relation store is NativeRelationMySqlStore;
//                  INativeRelationStore and NativeRelationService are internal with no in-memory
//                  implementation, so a REAL relation mutation requires MySQL, which machine-safety
//                  forbids in-process. The front-half name validation is reflection-invokable but the
//                  actual add mutates only via MySQL, so no genuine relation run is claimed.
//   CORPS        : SKIPPED (documented, not faked). NativeCorpsStore is MySQL-backed (OpenConnection
//                  for TryLoad and every write); there is no in-memory corps store, so a REAL corps
//                  create/join hard-requires the DB store.
//
// Evidence goes to stdout and inproc_social_evidence.txt next to the executable.

int rc = 0;
var evidence = new List<string>();
void Log(string s) { evidence.Add(s); Console.WriteLine("  " + s); }
void Assert(bool cond, string msg) { if (!cond) throw new Exception("ASSERT FAILED: " + msg); }

// non-public real entry points driven by reflection
var miCreateGroup = typeof(TPlayObject).GetMethod("ClientCreateGroup",
    BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(string) }, null)
    ?? throw new MissingMethodException("TPlayObject.ClientCreateGroup");
var miAddGroupMember = typeof(TPlayObject).GetMethod("ClientAddGroupMember",
    BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(string) }, null)
    ?? throw new MissingMethodException("TPlayObject.ClientAddGroupMember");

try
{
    PrepareConfig();
    BootSingletons();
    Log("BOOT singletons: g_Config/RandomNumber/ObjectManager/UserEngine/MapManager constructed "
        + "(no GameApp.Initialize, no DBSvr gate, no network, no background threads)");

    var map = CreateBlankMap(48, 48, "social-harness-map");
    Log($"MAP built in-memory '{map.sMapName}' {map.wWidth}x{map.wHeight} (real Envirnoment.Initialize)");

    RunGroup(map);
    RunChannel();
    RunRelationSkip();
    RunCorpsSkip();

    Console.WriteLine(
        "PASS InProcSocialRunCheck team=REAL(ClientCreateGroup+ClientAddGroupMember->m_GroupMembers) "
        + "channel=REAL(NativeChannelManager.CreatePublic+Enter->members) relation=SKIP(mysql-only-store) "
        + "corps=SKIP(mysql-backed-store) single-process no-network no-DBSvr no-MySQL");
}
catch (Exception ex)
{
    Console.Error.WriteLine("FAIL InProcSocialRunCheck: " + ex);
    rc = 1;
}

try { File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "inproc_social_evidence.txt"), evidence); }
catch { /* evidence file is best-effort */ }

// Hard-exit so no lingering engine state can keep the process alive.
Environment.Exit(rc);

// ===================== flows =====================

void RunGroup(Envirnoment map)
{
    // Three real TPlayObject are registered in the real UserEngine player list so the real
    // GetPlayObject(name) lookup inside ClientCreateGroup/ClientAddGroupMember resolves them. Offline
    // flag keeps SendSocket a no-op (early return) while m_boGhost=false keeps GetPlayObject resolving.
    var leader = NewPlayer("group-leader", map);
    var m1 = NewPlayer("group-m1", map);
    var m2 = NewPlayer("group-m2", map);
    RegisterInEngine(leader, m1, m2);
    m1.m_boAllowGroup = true;
    m2.m_boAllowGroup = true;

    Assert(ReferenceEquals(M2Share.UserEngine.GetPlayObject("group-m1"), m1),
        "real UserEngine.GetPlayObject resolves a registered player by name");

    // real create: the leader forms a group with m1
    miCreateGroup.Invoke(leader, new object[] { "group-m1" });
    bool createdOk = leader.m_GroupMembers != null && leader.m_GroupMembers.Count == 2
        && leader.m_GroupMembers.Contains(m1)
        && ReferenceEquals(m1.m_GroupOwner, leader)
        && ReferenceEquals(leader.m_GroupOwner, leader);
    Log($"TEAM ClientCreateGroup(leader,m1): m_GroupMembers={leader.m_GroupMembers?.Count} "
        + $"leader.m_GroupOwner={(ReferenceEquals(leader.m_GroupOwner, leader) ? "leader" : "?")} "
        + $"m1.m_GroupOwner={(ReferenceEquals(m1.m_GroupOwner, leader) ? "leader" : "?")}");
    Assert(createdOk, "real ClientCreateGroup formed a 2-member group with m1.m_GroupOwner=leader");

    // real add: the leader adds m2 to the existing group
    miAddGroupMember.Invoke(leader, new object[] { "group-m2" });
    bool addedOk = leader.m_GroupMembers.Count == 3 && leader.m_GroupMembers.Contains(m2)
        && ReferenceEquals(m2.m_GroupOwner, leader);
    Log($"TEAM ClientAddGroupMember(leader,m2): m_GroupMembers={leader.m_GroupMembers.Count} "
        + $"m2.m_GroupOwner={(ReferenceEquals(m2.m_GroupOwner, leader) ? "leader" : "?")} "
        + $"roster=[{string.Join(",", leader.m_GroupMembers.Select(p => p.m_sCharName))}]");
    Assert(addedOk, "real ClientAddGroupMember added m2 (m_GroupMembers=3, m2.m_GroupOwner=leader)");
}

void RunChannel()
{
    // The real shared NativeChannelManager runs CreatePublic (owner needs Level>=35) + Enter, mutating
    // the real in-memory channel membership set; QueryById reads the real snapshot back.
    var mgr = NativeChannelManager.Shared;
    var owner = new NativeChannelActor(90001, "chan-owner", 40, true);
    var member = new NativeChannelActor(90002, "chan-member", 25, true);

    var create = mgr.CreatePublic(owner,
        new NativeChannelCreateRequest("harness-channel", false, 0, 20));
    Assert(create.Code == 0 && create.ChannelId > 0, "real CreatePublic returned success + channel id");
    int chId = create.ChannelId;

    var enterOwner = mgr.Enter(owner, chId, 0);
    var enterMember = mgr.Enter(member, chId, 0);
    Assert(enterOwner.Code == 0, "real Enter(owner) succeeded");
    Assert(enterMember.Code == 0, "real Enter(member) succeeded");

    var q = mgr.QueryById(chId);
    Assert(q.Code == 0 && q.Snapshot != null, "real QueryById returned the channel snapshot");
    bool ownerFlagged = q.Snapshot.Members.Any(m => m.Identity == 90001 && m.IsOwner);
    bool memberPresent = q.Snapshot.Members.Any(m => m.Identity == 90002);
    Log($"CHANNEL CreatePublic id={chId} code={create.Code}; Enter owner={enterOwner.Code} "
        + $"member={enterMember.Code}; snapshot MemberCount={q.Snapshot.MemberCount} "
        + $"owner-flagged={ownerFlagged} member-present={memberPresent} name='{q.Snapshot.Name}'");
    Assert(q.Snapshot.MemberCount == 2 && ownerFlagged && memberPresent,
        "real channel holds 2 members (owner flagged) via real in-memory mutation");
}

void RunRelationSkip()
{
    Log("RELATION SKIP: only NativeRelationMySqlStore exists; INativeRelationStore + NativeRelationService "
        + "are internal with no in-memory implementation, so a REAL relation add mutates only via MySQL "
        + "(forbidden in-process). No genuine relation run claimed — not faked.");
}

void RunCorpsSkip()
{
    Log("CORPS SKIP: NativeCorpsStore is MySQL-backed (OpenConnection for TryLoad + all writes); no "
        + "in-memory corps store exists, so a REAL corps create/join hard-requires the DB store. Not faked.");
}

// ===================== helpers =====================

void PrepareConfig()
{
    // The M2Share static ctor loads its string/config .ini files on first access; write minimal ones
    // (same set the InProcEngineRunCheck template uses) so construction does not throw.
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
    M2Share.g_Config.nGroupMembersMax = 10;   // real default; ClientAddGroupMember capacity gate
    M2Share.g_Config.sGroupMsgPreFix = "";     // SendGroupText prefix
}

Envirnoment CreateBlankMap(short w, short h, string name)
{
    var map = new Envirnoment { sMapName = name, sMapDesc = name, m_sMapFileName = name };
    var init = typeof(Envirnoment).GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("Envirnoment.Initialize");
    init.Invoke(map, new object[] { w, h });
    map.Flag = new TMapFlag();
    var mapListField = typeof(MapManager).GetField("m_MapList", BindingFlags.Instance | BindingFlags.NonPublic);
    if (mapListField?.GetValue(M2Share.MapManager) is System.Collections.IDictionary dict && !dict.Contains(name))
        dict.Add(name, map);
    return map;
}

TPlayObject NewPlayer(string name, Envirnoment map)
{
    var p = new TPlayObject
    {
        m_boOffLineFlag = true, m_boGhost = false, m_boDeath = false,
        m_sCharName = name, m_sMapName = map.sMapName, m_PEnvir = map
    };
    p.m_Abil.Level = 30;
    return p;
}

void RegisterInEngine(params TPlayObject[] players)
{
    var listField = typeof(UserEngine).GetField("m_PlayObjectList", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("UserEngine.m_PlayObjectList");
    var list = (System.Collections.IList)listField.GetValue(M2Share.UserEngine);
    foreach (var p in players) list.Add(p);
}
