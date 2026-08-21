// NativeGmPlayerAttrCommandsCheck
//
// Pins GameSvr/Services/NativeGmPlayerAttrCommands.cs — the dormant model of the
// PLAYER-ATTR / TREND GM ("@") command family (family 10, 33 records) inside the
// M2Server dispatcher sub_622820 @0x00622820 — against the reversed binary facts
// (registry name/idx/perm/handler/no-op-sink + the per-case branch ladders/SysMsg).
//
// Excludes ChgBodyLuck(92)/ChgHideState(102) — those are modeled in
// NativeGmPlayerAdminCommands.cs (verified there). The whole Trend cluster
// (GetTrendV/SetTrendV/ClearTrendData/ClearAllTrendData) is a native no-op.
//
// Evidence: staging/gm_player_attr_commands_20260801.md (gm-fields census) +
// staging/update_clothes_4637_ida_work/{disp_decomp.txt, big622820.txt,
// padmin_out.txt} over m2full.i64 (SHA256 5540f43b…, base 0x00400000).

using System.Reflection;
using GameSvr;
using GameSvr.CommandSystem;

PrepareRuntimeFiles();

int checks = 0;
void Equal<T>(T expected, T actual, string label)
{
    checks++;
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"FAIL {label}: expected [{expected}], actual [{actual}]");
}

// ---------------------------------------------------------------------------
// 1) Dispatch + SysMsg + inline constants
// ---------------------------------------------------------------------------
Equal(0x00622820u, NativeGmPlayerAttrCommands.DispatcherEa, "sub_622820");
Equal(0x00622B1Cu, NativeGmPlayerAttrCommands.JumpTableEa, "jpt_622B15 base");
Equal(0x0062B648u, NativeGmPlayerAttrCommands.DefaultCaseEa, "def_622B15 sink");
Equal(0x0062B64Cu, NativeGmPlayerAttrCommands.EmptyBodyCaseEa, "loc_62B64C sink");
Equal(0x0062B648u, NativePlayerAttrCommand.DefaultHandler, "record DefaultHandler");
Equal(0x0062B64Cu, NativePlayerAttrCommand.EmptyBodyHandler, "record EmptyBodyHandler");
Equal(0xFFDB, NativeGmPlayerAttrCommands.SysMsgGmReply, "ident GM reply");
Equal(0x38FF, NativeGmPlayerAttrCommands.SysMsgUsage, "ident usage");
Equal(0x00652784u, NativeGmPlayerAttrCommands.FindPlayerEa, "FindPlayer sub_652784");
Equal(0x007D611Cu, NativeGmPlayerAttrCommands.QuizLevelGlobalEa, "QuizLevel cap off_7D611C");
Equal(30, NativeGmPlayerAttrCommands.QuizLevelMax, "QuizLevel cap 30");
Equal(1400, NativeGmPlayerAttrCommands.DmgShareFieldOffset, "ChgDmgShare target[+1400]");
Equal(0x84, NativeGmPlayerAttrCommands.DieVtblOffset, "Die vtbl+0x84");
Equal(0x8C, NativeGmPlayerAttrCommands.RecalcVtblOffset, "recalc vtbl+0x8C");

// ---------------------------------------------------------------------------
// 2) Registry (Name, Idx, Perm, Handler, Implemented, EmptyBodySink)
// ---------------------------------------------------------------------------
var expected = new (string Name, int Idx, int Perm, uint Handler, bool Impl, bool EmptyBody)[]
{
    ("LookFor",           52,  2, 0x00623BBAu, true,  false),
    ("UpLvZx",            57,  3, 0x0062415Du, true,  false),
    ("OutSay",            62,  2, 0x00624290u, true,  false),
    ("ShifangSay",        63,  2, 0x006242A3u, true,  false),
    ("LookOutSay",        64,  3, 0x006242B5u, true,  false),
    ("ChgSelfHair",       93,  4, 0x00624FA2u, true,  false),
    ("ChgSwTo",           107, 4, 0x0062513Fu, true,  false),
    ("GowLihun",          122, 4, 0x0062527Au, true,  false),
    ("GowJiehun",         123, 4, 0x0062528Au, true,  false),
    ("GowStuTec",         124, 4, 0x0062529Du, true,  false),
    ("LeaveTech",         125, 4, 0x006252B0u, true,  false),
    ("ClearRelation",     126, 4, 0x006252C0u, true,  false),
    ("Cattle",            155, 4, 0x006256D4u, true,  false),
    ("QuizLevel",         179, 4, 0x00625901u, true,  false),
    ("Upgradedata",       210, 5, 0x00625E79u, true,  false),
    ("UpExpdata",         211, 5, 0x00625E9Au, true,  false),
    ("ChgWangshi",        224, 5, 0x0062617Bu, true,  false),
    ("clearMulExp",       279, 4, 0x00626C0Du, true,  false),
    ("showMulExp",        280, 4, 0x00626C83u, true,  false),
    ("RangeHumCount",     313, 4, 0x00627680u, true,  false),
    ("Die",               358, 5, 0x00627FD5u, true,  false),
    ("ChgDmgShare",       359, 5, 0x00628008u, true,  false),
    ("OpenZhuZaiShenYou", 425, 4, 0x00628A94u, true,  false),
    ("ClearAllState",     575, 5, 0x006297EBu, true,  false),
    ("ZongpaiTest",       285, 5, 0x0062B648u, false, false), // def_622B15
    ("AddImpress",        330, 5, 0x0062B648u, false, false),
    ("SetDominateLv",     365, 5, 0x0062B64Cu, false, true),  // loc_62B64C
    ("ChgDragonState",    366, 4, 0x0062B648u, false, false),
    ("GetTrendV",         390, 3, 0x0062B64Cu, false, true),
    ("SetTrendV",         391, 4, 0x0062B64Cu, false, true),
    ("Show",              397, 3, 0x0062B648u, false, false),
    ("ClearTrendData",    398, 4, 0x0062B64Cu, false, true),
    ("ClearAllTrendData", 400, 4, 0x0062B648u, false, false),
};

Equal(33, expected.Length, "family-10 size");
Equal(expected.Length, NativeGmPlayerAttrCommands.All.Count, "modeled count");

int impl = 0, noop = 0, empty = 0, def = 0;
foreach (var e in expected)
{
    var c = NativeGmPlayerAttrCommands.Find(e.Name);
    Equal(true, c != null, $"registry has {e.Name}");
    Equal(e.Idx, c.DispatchIndex, $"{e.Name}.Idx");
    Equal(e.Perm, c.RequiredPerm, $"{e.Name}.Perm");
    Equal(e.Handler, c.HandlerAddress, $"{e.Name}.Handler");
    Equal(e.Impl, c.Implemented, $"{e.Name}.Implemented");
    Equal(NativePlayerAttrCommand.JumpTableBase + (uint)e.Idx * 4, c.JumpSlotAddress, $"{e.Name}.JumpSlot");
    if (e.Impl) { impl++; Equal(NativeNoOpSink.None, c.Sink, $"{e.Name}.Sink None"); }
    else
    {
        noop++;
        Equal(e.EmptyBody ? NativeNoOpSink.EmptyBody : NativeNoOpSink.DefaultCase, c.Sink, $"{e.Name}.Sink");
        if (e.EmptyBody) empty++; else def++;
    }
}
Equal(24, impl, "impl count");
Equal(9, noop, "no-op count");
Equal(4, empty, "loc_62B64C empty-body count");
Equal(5, def, "def_622B15 default count");

// ---------------------------------------------------------------------------
// 3) Unknown + permission ladder
// ---------------------------------------------------------------------------
Equal(NativePlayerAttrOutcome.UnknownCommand,
    NativeGmPlayerAttrCommands.Evaluate("Nope", 10, null).Outcome, "unknown");
Equal(NativePlayerAttrOutcome.PermissionRejected,
    NativeGmPlayerAttrCommands.Evaluate("LookFor", 1, new[] { "bob" }).Outcome, "LookFor perm1<2");
Equal(NativePlayerAttrOutcome.PermissionRejected,
    NativeGmPlayerAttrCommands.Evaluate("ChgWangshi", 4, new[] { "a", "b" }).Outcome, "ChgWangshi perm4<5");

// ---------------------------------------------------------------------------
// 4) No-ops (9) -> SilentNoOp + reused NativeGmDefaultNoOp
// ---------------------------------------------------------------------------
foreach (var n in new[] { "ZongpaiTest", "AddImpress", "SetDominateLv", "ChgDragonState",
                          "GetTrendV", "SetTrendV", "Show", "ClearTrendData", "ClearAllTrendData" })
{
    Equal(NativePlayerAttrOutcome.SilentNoOp,
        NativeGmPlayerAttrCommands.Evaluate(n, 10, new[] { "a", "b" }).Outcome, $"{n} -> SilentNoOp");
    var d = NativeGmPlayerAttrCommands.EvaluateUnimplemented(n);
    Equal(true, d.Recognized, $"{n} NoOp.Recognized");
    Equal(false, d.MutatesState, $"{n} NoOp.MutatesState");
    Equal(false, d.SendsResponse, $"{n} NoOp.SendsResponse");
}

// ---------------------------------------------------------------------------
// 5) Unconditional reports
// ---------------------------------------------------------------------------
var ulz = NativeGmPlayerAttrCommands.Evaluate("UpLvZx", 3, new[] { "8" });
Equal(NativePlayerAttrOutcome.ExecutedWithGmMessage, ulz.Outcome, "UpLvZx -> msg");
Equal(0x38FF, ulz.NativeSysMsgIdent, "UpLvZx ident 0x38FF");
Equal(0xFFDB, NativeGmPlayerAttrCommands.Evaluate("LookOutSay", 3, null).NativeSysMsgIdent, "LookOutSay ident 0xFFDB");
Equal(0xFFDB, NativeGmPlayerAttrCommands.Evaluate("RangeHumCount", 4, new[] { "10" }).NativeSysMsgIdent, "RangeHumCount ident 0xFFDB");
var qz = NativeGmPlayerAttrCommands.Evaluate("QuizLevel", 4, new[] { "20" });
Equal(NativePlayerAttrOutcome.ExecutedWithGmMessage, qz.Outcome, "QuizLevel -> msg");
Equal("set-cap", qz.Branch, "QuizLevel branch");
Equal(false, qz.CoreBodyDeferred, "QuizLevel inline (not deferred)");

// ---------------------------------------------------------------------------
// 6) Pure delegations
// ---------------------------------------------------------------------------
void Delegate(string name, int perm, string core, string[] args)
{
    var r = NativeGmPlayerAttrCommands.Evaluate(name, perm, args);
    Equal(NativePlayerAttrOutcome.Executed, r.Outcome, $"{name} -> Executed");
    Equal("delegate", r.Branch, $"{name} branch");
    Equal(core, r.NativeCore, $"{name} core");
    Equal(NativeGmPlayerAttrCommands.NoSysMsg, r.NativeSysMsgIdent, $"{name} no shim SysMsg");
}
Delegate("LookFor", 2, "sub_6BE5DC", new[] { "bob" });
Delegate("OutSay", 2, "sub_6BF260", new[] { "bob", "5" });
Delegate("ShifangSay", 2, "sub_6BF340", new[] { "bob" });
Delegate("ChgSelfHair", 4, "sub_6D77DC", new[] { "3" });
Delegate("ChgSwTo", 4, "sub_6C2148", new[] { "bob", "100" });
Delegate("GowLihun", 4, "sub_6C51BC", new[] { "bob" });
Delegate("GowJiehun", 4, "sub_6C5568", new[] { "a", "b" });
Delegate("GowStuTec", 4, "sub_6C57B4", new[] { "a", "b" });
// LeaveTech left the delegation set on 2026-08-08: sub_6C5E08's body is reversed
// and wired (LeaveTechCommand.cs). Its ladder is asserted in section 6b below.
Delegate("ClearRelation", 4, "sub_6C61D8", new[] { "bob", "all" });
Delegate("ChgWangshi", 5, "sub_6D20D0", new[] { "a", "b" });
Delegate("Upgradedata", 5, "sub_6C6F40", new[] { "bob", "50" });
Delegate("UpExpdata", 5, "sub_6C70CC", new[] { "bob", "999" });
Delegate("OpenZhuZaiShenYou", 4, "sub_6BF658", new[] { "1" });

// ---------------------------------------------------------------------------
// 6b) LeaveTech (125) -- sub_6C5E08 reversed ladder.
//   6C5E13  je 0x6C5E76               nil name   -> silent, no message
//   6C5E27  je 0x6C5E63               not found  -> ONE line, GM only
//   6C5E29  cmp [ebx+0xB95],0 / je    not student-> the SAME line, GM only
//   6C5E36  call sub_6C5EC8 (edx=0)   success    -> line to target AND GM
// Every line uses cx=0xFFDB (the Green pair) -- the failure is NOT red.
// ---------------------------------------------------------------------------
{
    var lt = NativeGmPlayerAttrCommands.All.Single(c => c.Name == "LeaveTech");
    Equal(125, lt.DispatchIndex, "LeaveTech dispatch index");
    Equal(4, lt.RequiredPerm, "LeaveTech permission");
    Equal("sub_6C5E08", lt.NativeCore, "LeaveTech core");
    Equal(false, lt.CoreBodyDeferred, "LeaveTech body no longer deferred");

    var savedFound = NativeGmPlayerAttrCommands.TargetPlayerFound;
    var savedStudent = NativeGmPlayerAttrCommands.TargetIsStudent;
    try
    {
        // nil name -> 0x6C5E76, silent
        NativeGmPlayerAttrCommands.TargetPlayerFound = true;
        NativeGmPlayerAttrCommands.TargetIsStudent = true;
        var nil = NativeGmPlayerAttrCommands.Evaluate("LeaveTech", 4, new[] { "" });
        Equal(NativePlayerAttrOutcome.RejectedSilently, nil.Outcome, "LeaveTech nil name silent");
        Equal(NativeGmPlayerAttrCommands.NoSysMsg, nil.NativeSysMsgIdent, "LeaveTech nil name no message");

        // not found -> 0x6C5E63
        NativeGmPlayerAttrCommands.TargetPlayerFound = false;
        var miss = NativeGmPlayerAttrCommands.Evaluate("LeaveTech", 4, new[] { "bob" });
        Equal(NativePlayerAttrOutcome.RejectedWithGmMessage, miss.Outcome, "LeaveTech miss rejected");
        Equal("not-found", miss.Branch, "LeaveTech miss branch");
        Equal(NativeGmPlayerAttrCommands.SysMsgGmReply, miss.NativeSysMsgIdent, "LeaveTech miss 0xFFDB");

        // found but not a student -> the SAME 0x6C5E63 line
        NativeGmPlayerAttrCommands.TargetPlayerFound = true;
        NativeGmPlayerAttrCommands.TargetIsStudent = false;
        var notStu = NativeGmPlayerAttrCommands.Evaluate("LeaveTech", 4, new[] { "bob" });
        Equal(NativePlayerAttrOutcome.RejectedWithGmMessage, notStu.Outcome, "LeaveTech non-student rejected");
        Equal("not-student", notStu.Branch, "LeaveTech non-student branch");
        Equal(miss.NativeSysMsgIdent, notStu.NativeSysMsgIdent,
            "LeaveTech both misses share one ident");

        // success -> sub_6C5EC8 mode 0
        NativeGmPlayerAttrCommands.TargetIsStudent = true;
        var ok = NativeGmPlayerAttrCommands.Evaluate("LeaveTech", 4, new[] { "bob" });
        Equal(NativePlayerAttrOutcome.ExecutedWithGmMessage, ok.Outcome, "LeaveTech success outcome");
        Equal("dissolve", ok.Branch, "LeaveTech success branch");
        Equal("sub_6C5EC8", ok.NativeCore, "LeaveTech success routes to the shared dissolve core");
        Equal(NativeGmPlayerAttrCommands.SysMsgGmReply, ok.NativeSysMsgIdent, "LeaveTech success 0xFFDB");
    }
    finally
    {
        NativeGmPlayerAttrCommands.TargetPlayerFound = savedFound;
        NativeGmPlayerAttrCommands.TargetIsStudent = savedStudent;
    }
}

// ---------------------------------------------------------------------------
// 7) Find-player guarded ladders
// ---------------------------------------------------------------------------
NativeGmPlayerAttrCommands.TargetPlayerFound = true;
foreach (var n in new[] { "clearMulExp", "showMulExp" })
{
    var r = NativeGmPlayerAttrCommands.Evaluate(n, 4, new[] { "bob" });
    Equal(NativePlayerAttrOutcome.ExecutedWithGmMessage, r.Outcome, $"{n} found -> msg");
    Equal("found", r.Branch, $"{n} found branch");
    Equal(0xFFDB, r.NativeSysMsgIdent, $"{n} found ident 0xFFDB");
}
NativeGmPlayerAttrCommands.TargetPlayerFound = false;
var cmx = NativeGmPlayerAttrCommands.Evaluate("clearMulExp", 4, new[] { "ghost" });
Equal(NativePlayerAttrOutcome.RejectedWithGmMessage, cmx.Outcome, "clearMulExp not-found -> reject msg");
Equal(0xFFDB, cmx.NativeSysMsgIdent, "clearMulExp not-found ident 0xFFDB");
NativeGmPlayerAttrCommands.TargetPlayerFound = true;

// ChgDmgShare
var cds = NativeGmPlayerAttrCommands.Evaluate("ChgDmgShare", 5, new[] { "bob", "5" });
Equal(NativePlayerAttrOutcome.ExecutedWithGmMessage, cds.Outcome, "ChgDmgShare set -> msg");
Equal(0xFFDB, cds.NativeSysMsgIdent, "ChgDmgShare ident 0xFFDB");
Equal(NativePlayerAttrOutcome.RejectedSilently,
    NativeGmPlayerAttrCommands.Evaluate("ChgDmgShare", 5, new[] { "bob", "-1" }).Outcome, "ChgDmgShare val<0 silent");
NativeGmPlayerAttrCommands.TargetPlayerFound = false;
Equal(NativePlayerAttrOutcome.RejectedSilently,
    NativeGmPlayerAttrCommands.Evaluate("ChgDmgShare", 5, new[] { "ghost", "5" }).Outcome, "ChgDmgShare not-found silent");
NativeGmPlayerAttrCommands.TargetPlayerFound = true;

// Die model
var dieSelf = NativeGmPlayerAttrCommands.Evaluate("Die", 5, null);
Equal("self", dieSelf.Branch, "Die self");
Equal(false, dieSelf.CoreBodyDeferred, "Die self body wired");
var dieTarget = NativeGmPlayerAttrCommands.Evaluate("Die", 5, new[] { "bob" });
Equal("target", dieTarget.Branch, "Die target found");
Equal(false, dieTarget.CoreBodyDeferred, "Die target body wired");
NativeGmPlayerAttrCommands.TargetPlayerFound = false;
Equal(NativePlayerAttrOutcome.RejectedSilently,
    NativeGmPlayerAttrCommands.Evaluate("Die", 5, new[] { "ghost" }).Outcome, "Die not-found silent");
NativeGmPlayerAttrCommands.TargetPlayerFound = true;

// Die runtime: case 358 reads only token 1, uses native sub_652784 readiness
// gates, dispatches vtbl+0x84 and never emits a command-body message.
M2Share.g_Config = new GameSvrConfig
{
    boTestServer = false
};
M2Share.ObjectManager = new ObjectManager();
M2Share.ProcessMsgCriticalSection = new object();
M2Share.UserEngine = new UserEngine();

var gm = new DieProbePlayer
{
    m_sCharName = "GameMaster",
    m_btPermission = 5,
    m_boReadyRun = true
};
var readyTarget = new DieProbePlayer
{
    m_sCharName = "ReadyTarget",
    m_boReadyRun = true
};
var ghostTarget = new DieProbePlayer
{
    m_sCharName = "GhostTarget",
    m_boReadyRun = true,
    m_boGhost = true
};
var notReadyTarget = new DieProbePlayer
{
    m_sCharName = "NotReadyTarget",
    m_boReadyRun = false
};
AddOnline(readyTarget, ghostTarget, notReadyTarget);

var dieAttribute = typeof(DieCommand).GetCustomAttribute<GameCommandAttribute>();
var dieMethod = typeof(DieCommand).GetMethod(nameof(DieCommand.Die));
Equal(true, dieAttribute != null, "Die runtime command attribute");
Equal(5, (int)dieAttribute.nPermissionMin, "Die runtime permission");
Equal(true, dieMethod != null, "Die runtime command method");
var dieCommand = new DieCommand();
dieCommand.Register(dieAttribute, dieMethod);

Equal<string>(null, dieCommand.Handle(string.Empty, gm), "Die self return silent");
Equal(1, gm.DieCalls, "Die self virtual call");
Equal(0, gm.m_MsgList.Count, "Die self queued no message");

Equal<string>(null, dieCommand.Handle("rEaDyTaRgEt ignored", gm),
    "Die target return silent");
Equal(1, readyTarget.DieCalls, "Die target virtual call");
Equal(1, gm.DieCalls, "Die target did not kill caller");
Equal(0, gm.m_MsgList.Count, "Die target queued no caller message");
Equal(0, readyTarget.m_MsgList.Count, "Die target queued no target message");

Equal<string>(null, dieCommand.Handle("MissingTarget", gm),
    "Die missing target silent");
Equal<string>(null, dieCommand.Handle("GhostTarget", gm),
    "Die ghost target silent");
Equal<string>(null, dieCommand.Handle("NotReadyTarget", gm),
    "Die not-ready target silent");
Equal(0, ghostTarget.DieCalls, "Die ghost target rejected");
Equal(0, notReadyTarget.DieCalls, "Die not-ready target rejected");
Equal(0, gm.m_MsgList.Count, "Die rejected paths queued no message");

gm.m_btPermission = 4;
Equal("该命令需要5级GM才能使用", dieCommand.Handle(string.Empty, gm),
    "Die permission 4 rejected by normal gate");
Equal(1, gm.DieCalls, "Die permission rejection skipped body");
gm.m_btPermission = 5;
Equal<string>(null, dieCommand.Handle(string.Empty, gm),
    "Die permission 5 executes silently");
Equal(2, gm.DieCalls, "Die permission 5 virtual call");

dieCommand.Die(null, null);
Equal(2, gm.DieCalls, "Die null player silent");

// ClearAllState
Equal(NativePlayerAttrOutcome.Executed, NativeGmPlayerAttrCommands.Evaluate("ClearAllState", 5, new[] { "bob" }).Outcome, "ClearAllState found");
NativeGmPlayerAttrCommands.TargetPlayerFound = false;
Equal(NativePlayerAttrOutcome.RejectedSilently, NativeGmPlayerAttrCommands.Evaluate("ClearAllState", 5, new[] { "ghost" }).Outcome, "ClearAllState not-found silent");
NativeGmPlayerAttrCommands.TargetPlayerFound = true;

// Cattle
Equal("openbox", NativeGmPlayerAttrCommands.Evaluate("Cattle", 4, new[] { "OpenBox", "2" }).Branch, "Cattle OpenBox");
var catAdd = NativeGmPlayerAttrCommands.Evaluate("Cattle", 4, new[] { "Add", "bob", "100" });
Equal(NativePlayerAttrOutcome.ExecutedWithGmMessage, catAdd.Outcome, "Cattle Add found -> msg");
Equal(0xFFDB, catAdd.NativeSysMsgIdent, "Cattle Add ident 0xFFDB");
NativeGmPlayerAttrCommands.TargetPlayerFound = false;
Equal(0x38FF, NativeGmPlayerAttrCommands.Evaluate("Cattle", 4, new[] { "Add", "ghost", "100" }).NativeSysMsgIdent, "Cattle Add not-found ident 0x38FF");
NativeGmPlayerAttrCommands.TargetPlayerFound = true;

Console.WriteLine($"PASS NativeGmPlayerAttrCommandsCheck ({checks} checks): "
    + $"{NativeGmPlayerAttrCommands.All.Count} player-attr/trend (10) GM commands modeled "
    + $"({impl} implemented, {noop} no-op = {empty} loc_62B64C + {def} def_622B15) — "
    + "registry, permission ladder, find-player + report ladders, dual no-op sinks. "
    + "OutSay/ShifangSay/LookOutSay back onto NativeGmDenyListCommands (mute codec).");
return 0;

void AddOnline(params TPlayObject[] players)
{
    var field = typeof(UserEngine).GetField("m_PlayObjectList",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(UserEngine).FullName,
            "m_PlayObjectList");
    if (field.GetValue(M2Share.UserEngine) is not IList<TPlayObject> online)
        throw new InvalidOperationException("unexpected online-player list");
    foreach (var player in players)
        online.Add(player);
}

void PrepareRuntimeFiles()
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

sealed class DieProbePlayer : TPlayObject
{
    public int DieCalls { get; private set; }

    public override void Die()
    {
        DieCalls++;
    }
}
