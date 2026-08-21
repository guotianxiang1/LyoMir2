// NativeGmMonsterMapCommandsCheck
//
// Pins GameSvr/Services/NativeGmMonsterMapCommands.cs — the dormant model of the
// MONSTER / MAP / NPC GM ("@") command family inside the M2Server dispatcher
// sub_622820 @0x00622820 — against the reversed binary facts (registry: name /
// dispatchIndex / requiredPerm / handler / no-op, and the per-case branch ladders
// and SysMsg idents proven by the shims).
//
// This family EXTENDS the world-admin family but is DISJOINT from its 12 modeled
// world commands (KickOut/CallMan/Shuag/CallMob/MonClear/ReShuaNpc/SetSysTime/
// MapDropItem/LockTimeChg/CreateCampMon/SetMapState/kickOutBlackRoom) — none of
// those are re-checked here.
//
// Evidence: staging/update_clothes_4637_ida_work/{disp_decomp.txt, big622820.txt,
// world_scan_out.txt, world_scan_lo_out.txt, all_strings.txt} over m2full.i64
// (SHA256 5540f43b…c049670b14e, image base 0x00400000).

using GameSvr;
using GameSvr.PasEngine;
using System.Reflection;
using SystemModule;

int checks = 0;

// SINGLE generic assertion helper (top-level statements cannot overload a local
// function, so every fact — int / uint / bool / string / enum — flows through here).
void Equal<T>(T expected, T actual, string label)
{
    checks++;
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"FAIL {label}: expected [{expected}], actual [{actual}]");
}

// ---------------------------------------------------------------------------
// 1) Dispatch + SysMsg constants
// ---------------------------------------------------------------------------
Equal(0x00622820u, NativeGmMonsterMapCommands.DispatcherEa, "sub_622820");
Equal(0x00621F28u, NativeGmMonsterMapCommands.IndexLookupEa, "sub_621F28");
Equal(0x00622B1Cu, NativeGmMonsterMapCommands.JumpTableEa, "jpt_622B15 base");
Equal(0x00622B1Cu, NativeMonsterMapCommand.JumpTableBase, "jpt_622B15 base (record)");
Equal(750, NativeGmMonsterMapCommands.SwitchMaxIndex, "cmp esi,0x2EE");
Equal(0x0062B648u, NativeGmMonsterMapCommands.DefaultCaseEa, "def_622B15 (silent no-op)");
Equal(0x0062B648u, NativeMonsterMapCommand.DefaultHandler, "def_622B15 (record)");
Equal(0xD4, NativeGmMonsterMapCommands.SysMsgVtableOffset, "SysMsg vtable slot +0xD4");
Equal(0xFFDB, NativeGmMonsterMapCommands.SysMsgGmReply, "SysMsg ident: GM reply (-37)");
Equal(0x38FF, NativeGmMonsterMapCommands.SysMsgUsage, "SysMsg ident: usage (14591)");
Equal(0xFCFF, NativeGmMonsterMapCommands.SysMsgNotice, "SysMsg ident: notice (-769)");
Equal(-1, NativeGmMonsterMapCommands.NoSysMsg, "no SysMsg sentinel");

// inline global/data addresses + constants proven by the shims
Equal(0x007D6970u, NativeGmMonsterMapCommands.ThroughRangeGlobalEa, "ThroughRange global off_7D6970");
Equal(0x32, NativeGmMonsterMapCommands.ThroughRangeMax, "ThroughRange max 50");
Equal(0x007D6EC8u, NativeGmMonsterMapCommands.FountSwitchGlobalEa, "SetFountSwitch global off_7D6EC8");
Equal(0x007D6364u, NativeGmMonsterMapCommands.SpiderLastTimeGlobalEa, "SpiderWebTest lasttime off_7D6364");
Equal(0x007D6D14u, NativeGmMonsterMapCommands.SpiderCodeTimeGlobalEa, "SpiderWebTest codetime off_7D6D14");
Equal(0x007D6F54u, NativeGmMonsterMapCommands.SpiderEffectGlobalEa, "SpiderWebTest effect off_7D6F54");
Equal(0x007D6830u, NativeGmMonsterMapCommands.CritGlobalEa, "BreakLvCtrl global crit off_7D6830");
Equal(0xB8, NativeGmMonsterMapCommands.MapCritByteOffset, "BreakLvCtrl map crit BYTE +184");
Equal(0xBA, NativeGmMonsterMapCommands.MapCritWordOffset, "BreakLvCtrl map crit WORD +186");
Equal(1, NativeGmMonsterMapCommands.TempSetMapParamSuccess, "TempSetMapParam success==1");
Equal(100, NativeGmMonsterMapCommands.TempSetMapParamUnsupported, "TempSetMapParam unsupported==100");

// core subroutine addresses the shims delegate to (deferred bodies) + parse helper
Equal(0x0040CA18u, NativeGmMonsterMapCommands.StrToIntWithDefaultEa, "sub_40CA18 str->int");
Equal(0x006CE400u, NativeGmMonsterMapCommands.GowgoCoreEa, "gowgo core sub_6CE400");
Equal(0x006BE4D0u, NativeGmMonsterMapCommands.DingdianCoreEa, "dingdianyidong core sub_6BE4D0");
Equal(0x006BEC4Cu, NativeGmMonsterMapCommands.MonXinxiCoreEa, "MonXinxi core sub_6BEC4C");
Equal(0x006BED0Cu, NativeGmMonsterMapCommands.HumNumCoreEa, "HumNum core sub_6BED0C");
Equal(0x006DEF10u, NativeGmMonsterMapCommands.MapIdxCoreEa, "MAP idx sub_6DEF10");
Equal(0x00779DDCu, NativeGmMonsterMapCommands.MonNumberCoreEa, "MonNumber core sub_779DDC");
Equal(0x0067D484u, NativeGmMonsterMapCommands.ReloadMonAttCoreEa, "ReloadMonAtt core sub_67D484");
Equal(0x0074EBB4u, NativeGmMonsterMapCommands.ReloadNpcPrizeCoreEa, "ReloadNpcPrize core sub_74EBB4");
Equal(0x0062E58Cu, NativeGmMonsterMapCommands.RangeShuagCoreEa, "RangeShuag core sub_62E58C");
Equal(0x0062EA7Cu, NativeGmMonsterMapCommands.NpcHitCoreEa, "NpcHit core sub_62EA7C");
Equal(0x006D3024u, NativeGmMonsterMapCommands.AutoMoveCoreEa, "AutoMove core sub_6D3024");
Equal(0x006CDD48u, NativeGmMonsterMapCommands.LockInPlayersCoreEa, "LockInPlayers core sub_6CDD48");
Equal(0x00696228u, NativeGmMonsterMapCommands.GetMapEa, "GetMap sub_696228");
Equal(0x006962D0u, NativeGmMonsterMapCommands.GetMap2Ea, "GetMap sub_6962D0");
Equal(0x0062ECE0u, NativeGmMonsterMapCommands.SetRecoverFactorCoreEa, "setRecoverFactor core sub_62ECE0");
Equal(0x006CDBBCu, NativeGmMonsterMapCommands.SetNoKillMapLvCoreEa, "SetNoKillMapLv core sub_6CDBBC");
Equal(0x0077BEB4u, NativeGmMonsterMapCommands.MapCellFreeCoreEa, "MapCellFree core sub_77BEB4");
Equal(0x0067DC40u, NativeGmMonsterMapCommands.ReshuaMonScriptCoreEa, "reshuaMonScript core sub_67DC40");
Equal(0x0067B35Cu, NativeGmMonsterMapCommands.LoadMonGenCoreEa, "LoadMonGen core sub_67B35C");
Equal(0x00679954u, NativeGmMonsterMapCommands.LoadMonFindEa, "LoadMonGen find sub_679954");
Equal(0x0067AEC0u, NativeGmMonsterMapCommands.ReloadMonitemsTreeCoreEa, "ReloadMonitemsTreeCfg core sub_67AEC0");
Equal(0x00774D24u, NativeGmMonsterMapCommands.TempSetMapParamCoreEa, "TempSetMapParam core sub_774D24");

// ---------------------------------------------------------------------------
// 2) Registry facts — name / index / perm / handler / implemented / jump slot,
//    exactly as decoded from the command records + jump table.
//    (Name, DispatchIndex, RequiredPerm, HandlerAddress, Implemented)
// ---------------------------------------------------------------------------
var expected = new (string Name, int Idx, int Perm, uint Handler, bool Impl)[]
{
    // ---- 23 implemented ----
    ("gowgo",                 29,  0, 0x00623B37u, true),
    ("dingdianyidong",        51,  3, 0x00623BA3u, true),
    ("MonXinxi",              53,  3, 0x00624098u, true),
    ("HumNum",                54,  3, 0x006240A5u, true),
    ("MAP",                   58,  3, 0x006241CAu, true),
    ("MonNumber",             73,  3, 0x00624CA4u, true),
    ("ReloadMonAtt",          111, 4, 0x0062517Cu, true),
    ("ThroughRange",          136, 4, 0x006252D3u, true),
    ("ReloadNpcPrize",        159, 4, 0x006258BFu, true),
    ("RangeShuag",            165, 4, 0x00625CC7u, true),
    ("NpcHit",                182, 4, 0x00625AF8u, true),
    ("AutoMove",              233, 5, 0x006262CFu, true),
    ("LockInPlayers",         258, 3, 0x00626540u, true),
    ("SetFountSwitch",        307, 4, 0x0062723Eu, true),
    ("BreakLvCtrl",           309, 4, 0x00627322u, true),
    ("SpiderWebTest",         340, 5, 0x00627D8Du, true),
    ("setRecoverFactor",      375, 4, 0x0062826Fu, true),
    ("SetNoKillMapLv",        392, 5, 0x006286BCu, true),
    ("MapCellFree",           454, 5, 0x00628B3Eu, true),
    ("reshuaMonScript",       476, 5, 0x006291EFu, true),
    ("LoadMonGen",            529, 3, 0x006295D4u, true),
    ("ReloadMonitemsTreeCfg", 576, 4, 0x00624002u, true),
    ("TempSetMapParam",       577, 5, 0x006298E6u, true),
    // ---- 4 registered no-ops (handler == def_622B15) ----
    ("ReloadLinkEx",          239, 4, 0x0062B648u, false),
    ("sendProc",              338, 5, 0x0062B648u, false),
    ("CellInfo",              474, 4, 0x0062B648u, false),
    ("reloadbossmon",         521, 4, 0x0062B648u, false),
};

Equal(27, expected.Length, "expected family size");
Equal(expected.Length, NativeGmMonsterMapCommands.All.Count, "modeled command count");

int implCount = 0, noopCount = 0;
foreach (var e in expected)
{
    var c = NativeGmMonsterMapCommands.Find(e.Name);
    Equal(true, c != null, $"registry has {e.Name}");
    Equal(e.Name, c.Name, $"{e.Name}.Name");
    Equal(e.Idx, c.DispatchIndex, $"{e.Name}.DispatchIndex");
    Equal(e.Perm, c.RequiredPerm, $"{e.Name}.RequiredPerm");
    Equal(e.Handler, c.HandlerAddress, $"{e.Name}.HandlerAddress");
    Equal(e.Impl, c.Implemented, $"{e.Name}.Implemented");
    // JumpSlot = base + index*4.
    Equal(NativeMonsterMapCommand.JumpTableBase + (uint)e.Idx * 4,
        c.JumpSlotAddress, $"{e.Name}.JumpSlotAddress");
    if (e.Impl) implCount++; else noopCount++;
}
Equal(23, implCount, "implemented count");
Equal(4, noopCount, "no-op count");

// Spot-check two exact jump-slot addresses (base + idx*4).
Equal(0x00622B90u, NativeGmMonsterMapCommands.Find("gowgo").JumpSlotAddress, "gowgo ptr@");
Equal(0x00623420u, NativeGmMonsterMapCommands.Find("TempSetMapParam").JumpSlotAddress, "TempSetMapParam ptr@");

// ---------------------------------------------------------------------------
// 3) Unknown token + permission ladder
// ---------------------------------------------------------------------------
Equal(NativeMonsterMapOutcome.UnknownCommand,
    NativeGmMonsterMapCommands.Evaluate("NoSuchThing", 10, null).Outcome,
    "non-family token -> UnknownCommand");

// SetNoKillMapLv needs perm 5: perm 4 is treated as unknown (sub_621F28 returns 0).
Equal(NativeMonsterMapOutcome.PermissionRejected,
    NativeGmMonsterMapCommands.Evaluate("SetNoKillMapLv", 4, null).Outcome,
    "SetNoKillMapLv perm 4 < 5 -> PermissionRejected");
Equal(NativeMonsterMapOutcome.RejectedSilently,
    NativeGmMonsterMapCommands.Evaluate("SetNoKillMapLv", 5, null).Outcome,
    "SetNoKillMapLv perm 5 missing arg -> parse rejected");
// gowgo needs only perm 0 — even a perm-0 caller proceeds.
Equal(NativeMonsterMapOutcome.Executed,
    NativeGmMonsterMapCommands.Evaluate("gowgo", 0, new[] { "1", "2" }).Outcome,
    "gowgo perm 0 -> Executed");

// ---------------------------------------------------------------------------
// 4) The 4 registered no-ops: Evaluate -> SilentNoOp, and the reused
//    NativeGmDefaultNoOp contract via EvaluateUnimplemented.
// ---------------------------------------------------------------------------
foreach (var noop in new[] { "ReloadLinkEx", "sendProc", "CellInfo", "reloadbossmon" })
{
    Equal(NativeMonsterMapOutcome.SilentNoOp,
        NativeGmMonsterMapCommands.Evaluate(noop, 10, new[] { "x" }).Outcome,
        $"{noop} -> SilentNoOp");
    var d = NativeGmMonsterMapCommands.EvaluateUnimplemented(noop);
    Equal(true, d.Recognized, $"{noop} NoOp.Recognized");
    Equal(true, d.DispatchesToDefaultCase, $"{noop} NoOp.DispatchesToDefaultCase");
    Equal(false, d.MutatesState, $"{noop} NoOp.MutatesState");
    Equal(false, d.SendsResponse, $"{noop} NoOp.SendsResponse");
}

// ---------------------------------------------------------------------------
// 5) Pure delegations (single branch, no shim SysMsg) -> Executed + core.
// ---------------------------------------------------------------------------
void Delegate(string name, int perm, string core)
{
    var r = NativeGmMonsterMapCommands.Evaluate(name, perm, new[] { "a", "b", "c" });
    Equal(NativeMonsterMapOutcome.Executed, r.Outcome, $"{name} -> Executed");
    Equal("delegate", r.Branch, $"{name} branch");
    Equal(core, r.NativeCore, $"{name} core");
    Equal(NativeGmMonsterMapCommands.NoSysMsg, r.NativeSysMsgIdent, $"{name} no shim SysMsg");
    Equal(true, r.CoreBodyDeferred, $"{name} CoreBodyDeferred");
}
Delegate("gowgo", 0, "sub_6CE400");
Delegate("dingdianyidong", 3, "sub_6BE4D0");
Delegate("MonXinxi", 3, "sub_6BEC4C");
Delegate("RangeShuag", 4, "sub_62E58C");
Delegate("NpcHit", 4, "sub_62EA7C");
Delegate("LockInPlayers", 3, "sub_6CDD48");

var mapCellFreeModel = NativeGmMonsterMapCommands.Evaluate(
    "MapCellFree", 5, new[] { "ignored", "text" });
Equal(NativeMonsterMapOutcome.Executed, mapCellFreeModel.Outcome,
    "MapCellFree -> Executed");
Equal("attributes-walk", mapCellFreeModel.Branch,
    "MapCellFree recovered branch");
Equal("sub_77BEB4", mapCellFreeModel.NativeCore,
    "MapCellFree recovered core");
Equal(NativeGmMonsterMapCommands.NoSysMsg,
    mapCellFreeModel.NativeSysMsgIdent, "MapCellFree no SysMsg");
Equal(false, mapCellFreeModel.CoreBodyDeferred,
    "MapCellFree recovered core is not deferred");
Equal(true, mapCellFreeModel.Detail.Contains(
        "Object chains and skill flags are unchanged", StringComparison.Ordinal),
    "MapCellFree model pins untouched cell state");

// ---------------------------------------------------------------------------
// 6) Unconditional-report commands -> ExecutedWithGmMessage with the exact ident.
// ---------------------------------------------------------------------------
void Report(string name, int perm, int ident, bool deferred)
{
    var r = NativeGmMonsterMapCommands.Evaluate(name, perm, null);
    Equal(NativeMonsterMapOutcome.ExecutedWithGmMessage, r.Outcome, $"{name} -> ExecutedWithGmMessage");
    Equal(ident, r.NativeSysMsgIdent, $"{name} SysMsg ident");
    Equal(deferred, r.CoreBodyDeferred, $"{name} CoreBodyDeferred");
}
Report("HumNum", 3, 0xFFDB, true);
Report("MAP", 3, 0x38FF, false);
Report("ReloadMonAtt", 4, 0x38FF, false);
Report("reshuaMonScript", 5, 0xFFDB, false);
Report("ReloadMonitemsTreeCfg", 4, 0xFFDB, true);

var reshuaRecord = NativeGmMonsterMapCommands.Find("reshuaMonScript");
Equal(true, reshuaRecord.EffectSummary.Contains(
        "开始刷新怪物脚本", StringComparison.Ordinal),
    "reshuaMonScript exact start message modeled");
Equal(true, reshuaRecord.EffectSummary.Contains(
        "刷新怪物脚本结束", StringComparison.Ordinal),
    "reshuaMonScript exact end message modeled");
Equal(true, reshuaRecord.EffectSummary.Contains(
        "without rereading monScript.txt", StringComparison.Ordinal),
    "reshuaMonScript model preserves loaded mapping");
var reshuaModel = NativeGmMonsterMapCommands.Evaluate(
    "reshuaMonScript", 5, new[] { "ignored", "text" });
Equal("reload", reshuaModel.Branch, "reshuaMonScript recovered branch");
Equal("sub_67DC40", reshuaModel.NativeCore,
    "reshuaMonScript recovered core");
Equal(false, reshuaModel.CoreBodyDeferred,
    "reshuaMonScript recovered core is not deferred");

// ---------------------------------------------------------------------------
// 7) MonNumber (73): empty map == current (always), named map may miss
// ---------------------------------------------------------------------------
NativeGmMonsterMapCommands.MapExistsHook = _ => false;
var mnCur = NativeGmMonsterMapCommands.Evaluate("MonNumber", 3, new[] { "" });
Equal(NativeMonsterMapOutcome.ExecutedWithGmMessage, mnCur.Outcome, "MonNumber empty -> current map -> report");
Equal(0xFFDB, mnCur.NativeSysMsgIdent, "MonNumber count ident");
Equal("sub_779DDC", mnCur.NativeCore, "MonNumber count core");
Equal(NativeMonsterMapOutcome.RejectedWithGmMessage,
    NativeGmMonsterMapCommands.Evaluate("MonNumber", 3, new[] { "Ghost" }).Outcome,
    "MonNumber missing named map -> RejectedWithGmMessage");
Equal(0x38FF,
    NativeGmMonsterMapCommands.Evaluate("MonNumber", 3, new[] { "Ghost" }).NativeSysMsgIdent,
    "MonNumber missing map ident 0x38FF");
NativeGmMonsterMapCommands.MapExistsHook = _ => true;
Equal(NativeMonsterMapOutcome.ExecutedWithGmMessage,
    NativeGmMonsterMapCommands.Evaluate("MonNumber", 3, new[] { "0" }).Outcome,
    "MonNumber existing named map -> report");
NativeGmMonsterMapCommands.MapExistsHook = null;

// ---------------------------------------------------------------------------
// 8) ThroughRange (136): 0..50 sets the global + confirms; otherwise silent
// ---------------------------------------------------------------------------
var tr30 = NativeGmMonsterMapCommands.Evaluate("ThroughRange", 4, new[] { "30" });
Equal(NativeMonsterMapOutcome.ExecutedWithGmMessage, tr30.Outcome, "ThroughRange 30 -> ExecutedWithGmMessage");
Equal("value-0-to-50", tr30.Branch, "ThroughRange 30 branch");
Equal(0x38FF, tr30.NativeSysMsgIdent, "ThroughRange 30 ident");
Equal(false, tr30.CoreBodyDeferred, "ThroughRange inline (not deferred)");
Equal(NativeMonsterMapOutcome.ExecutedWithGmMessage,
    NativeGmMonsterMapCommands.Evaluate("ThroughRange", 4, new[] { "" }).Outcome,
    "ThroughRange empty -> default 0 <= 50 -> ExecutedWithGmMessage");
Equal(NativeMonsterMapOutcome.RejectedSilently,
    NativeGmMonsterMapCommands.Evaluate("ThroughRange", 4, new[] { "51" }).Outcome,
    "ThroughRange 51 -> RejectedSilently");
Equal(NativeMonsterMapOutcome.RejectedSilently,
    NativeGmMonsterMapCommands.Evaluate("ThroughRange", 4, new[] { "-1" }).Outcome,
    "ThroughRange -1 -> RejectedSilently");

PrepareRuntimeConfig();
var oldThroughRange = TPlayObject.NativeSafeZoneThroughRange;
var oldProcessMsgCriticalSection = M2Share.ProcessMsgCriticalSection;
var oldObjectManager = M2Share.ObjectManager;
var oldRandomNumber = M2Share.RandomNumber;
try
{
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.RandomNumber = RandomNumber.GetInstance();
    var player = new TPlayObject();
    var command = new ThroughRangeCommand();

    TPlayObject.NativeSafeZoneThroughRange = 17;
    command.ThroughRange(new[] { "-1" }, player);
    Equal(17, TPlayObject.NativeSafeZoneThroughRange,
        "live ThroughRange -1 preserves global");
    Equal(0, player.m_MsgList.Count,
        "live ThroughRange -1 is silent");

    command.ThroughRange(new[] { "0" }, player);
    Equal(0, TPlayObject.NativeSafeZoneThroughRange,
        "live ThroughRange 0 writes global");
    Equal(1, player.m_MsgList.Count,
        "live ThroughRange 0 queues one SysMsg");
    var zeroMessage = player.m_MsgList[0];
    Equal(Grobal2.RM_SYSMESSAGE, zeroMessage.wIdent,
        "live ThroughRange SysMsg ident");
    Equal(0, zeroMessage.wParam, "live ThroughRange wParam");
    Equal(0xFF, zeroMessage.nParam1,
        "live ThroughRange SysMsg foreground");
    Equal(0x38, zeroMessage.nParam2,
        "live ThroughRange SysMsg background");
    Equal(0, zeroMessage.nParam3, "live ThroughRange nParam3");
    Equal("设置本服务器的安全区穿人范围为: 0", zeroMessage.Buff,
        "live ThroughRange exact reply");

    player.m_MsgList.Clear();
    command.ThroughRange(new[] { "50" }, player);
    Equal(50, TPlayObject.NativeSafeZoneThroughRange,
        "live ThroughRange 50 writes global");
    Equal(1, player.m_MsgList.Count,
        "live ThroughRange 50 queues one SysMsg");

    player.m_MsgList.Clear();
    command.ThroughRange(new[] { "51" }, player);
    Equal(50, TPlayObject.NativeSafeZoneThroughRange,
        "live ThroughRange 51 preserves global");
    Equal(0, player.m_MsgList.Count,
        "live ThroughRange 51 is silent");

    command.ThroughRange(Array.Empty<string>(), player);
    Equal(0, TPlayObject.NativeSafeZoneThroughRange,
        "live ThroughRange missing arg defaults to zero");
    Equal("设置本服务器的安全区穿人范围为: ",
        player.m_MsgList[0].Buff,
        "live ThroughRange missing arg preserves raw empty reply");

    player.m_MsgList.Clear();
    command.ThroughRange(new[] { "abc" }, player);
    Equal(0, TPlayObject.NativeSafeZoneThroughRange,
        "live ThroughRange non-numeric arg defaults to zero");
    Equal("设置本服务器的安全区穿人范围为: abc",
        player.m_MsgList[0].Buff,
        "live ThroughRange non-numeric reply preserves raw text");
}
finally
{
    TPlayObject.NativeSafeZoneThroughRange = oldThroughRange;
    M2Share.ProcessMsgCriticalSection = oldProcessMsgCriticalSection;
    M2Share.ObjectManager = oldObjectManager;
    M2Share.RandomNumber = oldRandomNumber;
}

// ---------------------------------------------------------------------------
// 9) ReloadNpcPrize (159): success 0xFFDB / fail 0x38FF (reload runs on both)
// ---------------------------------------------------------------------------
NativeGmMonsterMapCommands.ReloadNpcPrizeSucceeds = true;
var npOk = NativeGmMonsterMapCommands.Evaluate("ReloadNpcPrize", 4, null);
Equal(NativeMonsterMapOutcome.ExecutedWithGmMessage, npOk.Outcome, "ReloadNpcPrize ok -> ExecutedWithGmMessage");
Equal("success", npOk.Branch, "ReloadNpcPrize ok branch");
Equal(0xFFDB, npOk.NativeSysMsgIdent, "ReloadNpcPrize ok ident");
NativeGmMonsterMapCommands.ReloadNpcPrizeSucceeds = false;
var npFail = NativeGmMonsterMapCommands.Evaluate("ReloadNpcPrize", 4, null);
Equal("fail", npFail.Branch, "ReloadNpcPrize fail branch");
Equal(0x38FF, npFail.NativeSysMsgIdent, "ReloadNpcPrize fail ident");
NativeGmMonsterMapCommands.ReloadNpcPrizeSucceeds = true;

// ---------------------------------------------------------------------------
// 10) SetFountSwitch (307): open/close set the byte + 0x38FF; else usage 0x38FF
// ---------------------------------------------------------------------------
var fsOpen = NativeGmMonsterMapCommands.Evaluate("SetFountSwitch", 4, new[] { "open" });
Equal(NativeMonsterMapOutcome.ExecutedWithGmMessage, fsOpen.Outcome, "SetFountSwitch open -> ExecutedWithGmMessage");
Equal("open", fsOpen.Branch, "SetFountSwitch open branch");
Equal(0x38FF, fsOpen.NativeSysMsgIdent, "SetFountSwitch open ident");
Equal("close", NativeGmMonsterMapCommands.Evaluate("SetFountSwitch", 4, new[] { "close" }).Branch, "SetFountSwitch close branch");
var fsBad = NativeGmMonsterMapCommands.Evaluate("SetFountSwitch", 4, new[] { "bogus" });
Equal(NativeMonsterMapOutcome.RejectedWithGmMessage, fsBad.Outcome, "SetFountSwitch bogus -> RejectedWithGmMessage");
Equal("usage", fsBad.Branch, "SetFountSwitch bogus branch");
Equal("usage",
    NativeGmMonsterMapCommands.Evaluate("SetFountSwitch", 4,
        new[] { "OPEN" }).Branch,
    "SetFountSwitch token comparison is case-sensitive");

var oldFountSwitch = M2Share.NativeFountSwitch;
var oldFountProcessMsgCriticalSection = M2Share.ProcessMsgCriticalSection;
var oldFountObjectManager = M2Share.ObjectManager;
var oldFountRandomNumber = M2Share.RandomNumber;
try
{
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.RandomNumber = RandomNumber.GetInstance();
    var player = new TPlayObject();
    var command = new SetFountSwitchCommand();

    Equal((byte)0, M2Share.NativeFountSwitch,
        "live SetFountSwitch defaults closed");

    command.SetFountSwitch(new[] { "open" }, player);
    Equal((byte)1, M2Share.NativeFountSwitch,
        "live SetFountSwitch open writes one");
    Equal(1, player.m_MsgList.Count,
        "live SetFountSwitch open queues one SysMsg");
    var openMessage = player.m_MsgList[0];
    Equal(Grobal2.RM_SYSMESSAGE, openMessage.wIdent,
        "live SetFountSwitch open SysMsg ident");
    Equal(0, openMessage.wParam,
        "live SetFountSwitch open wParam");
    Equal(0xFF, openMessage.nParam1,
        "live SetFountSwitch open foreground");
    Equal(0x38, openMessage.nParam2,
        "live SetFountSwitch open background");
    Equal(0, openMessage.nParam3,
        "live SetFountSwitch open nParam3");
    Equal("GM可控泉水已打开", openMessage.Buff,
        "live SetFountSwitch open exact reply");

    player.m_MsgList.Clear();
    command.SetFountSwitch(new[] { "close" }, player);
    Equal((byte)0, M2Share.NativeFountSwitch,
        "live SetFountSwitch close writes zero");
    Equal(1, player.m_MsgList.Count,
        "live SetFountSwitch close queues one SysMsg");
    Equal("GM可控泉水已关闭", player.m_MsgList[0].Buff,
        "live SetFountSwitch close exact reply");

    M2Share.NativeFountSwitch = 1;
    player.m_MsgList.Clear();
    command.SetFountSwitch(new[] { "bogus" }, player);
    Equal((byte)1, M2Share.NativeFountSwitch,
        "live SetFountSwitch invalid preserves byte");
    Equal(1, player.m_MsgList.Count,
        "live SetFountSwitch invalid queues usage SysMsg");
    Equal("参数open表示打开，参数close表示关闭，GM可控泉水默认关闭",
        player.m_MsgList[0].Buff,
        "live SetFountSwitch invalid exact usage reply");

    player.m_MsgList.Clear();
    command.SetFountSwitch(Array.Empty<string>(), player);
    Equal((byte)1, M2Share.NativeFountSwitch,
        "live SetFountSwitch missing arg preserves byte");
    Equal("参数open表示打开，参数close表示关闭，GM可控泉水默认关闭",
        player.m_MsgList[0].Buff,
        "live SetFountSwitch missing arg exact usage reply");

    player.m_MsgList.Clear();
    command.SetFountSwitch(new[] { "OPEN" }, player);
    Equal((byte)1, M2Share.NativeFountSwitch,
        "live SetFountSwitch uppercase is not native open token");
    Equal("参数open表示打开，参数close表示关闭，GM可控泉水默认关闭",
        player.m_MsgList[0].Buff,
        "live SetFountSwitch uppercase exact usage reply");

    player.m_MsgList.Clear();
    command.SetFountSwitch(new[] { "close" }, null);
    Equal((byte)1, M2Share.NativeFountSwitch,
        "live SetFountSwitch null player preserves byte");
    Equal(0, player.m_MsgList.Count,
        "live SetFountSwitch null player cannot queue message");
}
finally
{
    M2Share.NativeFountSwitch = oldFountSwitch;
    M2Share.ProcessMsgCriticalSection = oldFountProcessMsgCriticalSection;
    M2Share.ObjectManager = oldFountObjectManager;
    M2Share.RandomNumber = oldFountRandomNumber;
}

// ---------------------------------------------------------------------------
// 11) SetNoKillMapLv (392): current-map UserNoKill gate + WORD level cap
// ---------------------------------------------------------------------------
NativeGmMonsterMapCommands.SetNoKillMapLvMapEnabled = false;
var noKillRejected = NativeGmMonsterMapCommands.Evaluate(
    "SetNoKillMapLv", 5, new[] { "100" });
Equal(NativeMonsterMapOutcome.RejectedWithGmMessage,
    noKillRejected.Outcome,
    "SetNoKillMapLv non-UserNoKill map -> RejectedWithGmMessage");
Equal("map-not-user-no-kill", noKillRejected.Branch,
    "SetNoKillMapLv non-UserNoKill branch");
Equal(0xFFDB, noKillRejected.NativeSysMsgIdent,
    "SetNoKillMapLv non-UserNoKill ident");
Equal(false, noKillRejected.CoreBodyDeferred,
    "SetNoKillMapLv recovered core is not deferred");

NativeGmMonsterMapCommands.SetNoKillMapLvMapEnabled = true;
var noKillStored = NativeGmMonsterMapCommands.Evaluate(
    "SetNoKillMapLv", 5, new[] { "65536" });
Equal(NativeMonsterMapOutcome.ExecutedWithGmMessage,
    noKillStored.Outcome,
    "SetNoKillMapLv UserNoKill map -> ExecutedWithGmMessage");
Equal("stored-word-0", noKillStored.Branch,
    "SetNoKillMapLv stores low WORD");
Equal(0xFFDB, noKillStored.NativeSysMsgIdent,
    "SetNoKillMapLv success ident");
Equal("stored-word-65535",
    NativeGmMonsterMapCommands.Evaluate(
        "SetNoKillMapLv", 5, new[] { "-1" }).Branch,
    "SetNoKillMapLv negative value wraps to WORD");
Equal("stored-word-0",
    NativeGmMonsterMapCommands.Evaluate(
        "SetNoKillMapLv", 5, new[] { "$10000" }).Branch,
    "SetNoKillMapLv Delphi hexadecimal parsing");
Equal(NativeMonsterMapOutcome.RejectedSilently,
    NativeGmMonsterMapCommands.Evaluate(
        "SetNoKillMapLv", 5, new[] { "invalid" }).Outcome,
    "SetNoKillMapLv invalid strict integer is silent/no-write in live port");

var oldNoKillProcessMsgCriticalSection = M2Share.ProcessMsgCriticalSection;
var oldNoKillObjectManager = M2Share.ObjectManager;
var oldNoKillRandomNumber = M2Share.RandomNumber;
try
{
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.RandomNumber = RandomNumber.GetInstance();

    var mapFlag = new TMapFlag();
    var player = new TPlayObject
    {
        m_btPermission = 5,
        m_PEnvir = new Envirnoment { Flag = mapFlag }
    };
    var command = new SetNoKillMapLvCommand();
    command.Register(
        new GameSvr.CommandSystem.GameCommandAttribute(
            "SetNoKillMapLv", "设置安全地图等级上限", "等级", 5),
        typeof(SetNoKillMapLvCommand).GetMethod("SetNoKillMapLv")!);

    mapFlag.UserNoKillLevelCap = 77;
    command.SetNoKillMapLv(new[] { "100" }, player);
    Equal((ushort)77, mapFlag.UserNoKillLevelCap,
        "live SetNoKillMapLv non-UserNoKill map preserves cap");
    Equal(1, player.m_MsgList.Count,
        "live SetNoKillMapLv non-UserNoKill queues one SysMsg");
    Equal("该地图无法设定此命令", player.m_MsgList[0].Buff,
        "live SetNoKillMapLv non-UserNoKill exact reply");
    Equal(0xDB, player.m_MsgList[0].nParam1,
        "live SetNoKillMapLv failure foreground 0xDB");
    Equal(0xFF, player.m_MsgList[0].nParam2,
        "live SetNoKillMapLv failure background 0xFF");

    mapFlag.boUserNoKill = true;
    player.m_MsgList.Clear();
    command.SetNoKillMapLv(new[] { "100" }, player);
    Equal((ushort)100, mapFlag.UserNoKillLevelCap,
        "live SetNoKillMapLv writes level cap");
    Equal(1, player.m_MsgList.Count,
        "live SetNoKillMapLv success queues one SysMsg");
    Equal("已成功设定等级上限为100级", player.m_MsgList[0].Buff,
        "live SetNoKillMapLv exact success reply");
    Equal(0xDB, player.m_MsgList[0].nParam1,
        "live SetNoKillMapLv success foreground 0xDB");
    Equal(0xFF, player.m_MsgList[0].nParam2,
        "live SetNoKillMapLv success background 0xFF");

    foreach (var (raw, expectedCap) in new[]
             {
                 ("0", (ushort)0),
                 ("-1", ushort.MaxValue),
                 ("65535", ushort.MaxValue),
                 ("65536", (ushort)0)
             })
    {
        player.m_MsgList.Clear();
        command.SetNoKillMapLv(new[] { raw }, player);
        Equal(expectedCap, mapFlag.UserNoKillLevelCap,
            $"live SetNoKillMapLv {raw} WORD store");
        Equal($"已成功设定等级上限为{expectedCap}级", player.m_MsgList[0].Buff,
            $"live SetNoKillMapLv {raw} reports stored WORD");
    }

    mapFlag.UserNoKillLevelCap = 321;
    player.m_MsgList.Clear();
    command.SetNoKillMapLv(Array.Empty<string>(), player);
    Equal((ushort)321, mapFlag.UserNoKillLevelCap,
        "live SetNoKillMapLv missing arg preserves cap");
    Equal(0, player.m_MsgList.Count,
        "live SetNoKillMapLv missing arg is silent");

    command.SetNoKillMapLv(new[] { "not-an-integer" }, player);
    Equal((ushort)321, mapFlag.UserNoKillLevelCap,
        "live SetNoKillMapLv invalid integer preserves cap");
    Equal(0, player.m_MsgList.Count,
        "live SetNoKillMapLv invalid integer is silent");

    player.m_PEnvir = null;
    command.SetNoKillMapLv(new[] { "9" }, player);
    Equal((ushort)321, mapFlag.UserNoKillLevelCap,
        "live SetNoKillMapLv null map preserves prior cap");
    Equal(0, player.m_MsgList.Count,
        "live SetNoKillMapLv null map is silent");

    player.m_PEnvir = new Envirnoment { Flag = mapFlag };
    player.m_btPermission = 4;
    Equal(M2Share.g_sGameCommandPermissionTooLow, command.Handle("9", player),
        "live SetNoKillMapLv permission 4 preserves callback gate");
    Equal((ushort)321, mapFlag.UserNoKillLevelCap,
        "live SetNoKillMapLv permission 4 preserves cap");

    player.m_btPermission = 5;
    Equal<string>(null, command.Handle("9", player),
        "live SetNoKillMapLv permission 5 executes GM path");
    Equal((ushort)9, mapFlag.UserNoKillLevelCap,
        "live SetNoKillMapLv permission 5 writes cap");
}
finally
{
    M2Share.ProcessMsgCriticalSection = oldNoKillProcessMsgCriticalSection;
    M2Share.ObjectManager = oldNoKillObjectManager;
    M2Share.RandomNumber = oldNoKillRandomNumber;
}

// ---------------------------------------------------------------------------
// 12) MapCellFree (454): current map, attributes only, no arguments or SysMsg
// ---------------------------------------------------------------------------
var mapCellFreeEnvironment = new Envirnoment { sMapName = "map-cell-free" };
var environmentType = typeof(Envirnoment);
environmentType.GetMethod("Initialize",
        BindingFlags.Instance | BindingFlags.NonPublic)!
    .Invoke(mapCellFreeEnvironment, new object[] { (short)3, (short)2 });

var attributes = (CellAttribute[])environmentType.GetField("MapCellAttributes",
    BindingFlags.Instance | BindingFlags.NonPublic)!
    .GetValue(mapCellFreeEnvironment)!;
var skillFlags = (byte[])environmentType.GetField("MapCellSkillFlags",
    BindingFlags.Instance | BindingFlags.NonPublic)!
    .GetValue(mapCellFreeEnvironment)!;
var objectLists = (IList<CellObject>[])environmentType.GetField(
        "MapCellObjectLists", BindingFlags.Instance | BindingFlags.NonPublic)!
    .GetValue(mapCellFreeEnvironment)!;

for (var i = 0; i < attributes.Length; i++)
    attributes[i] = i % 2 == 0 ? CellAttribute.HighWall : CellAttribute.LowWall;
skillFlags[3] = 0x5A;
var preservedObject = new CellObject
{
    CellType = CellType.OS_ITEMOBJECT,
    CellObj = new object(),
    dwAddTime = 1234
};
var preservedObjectList = new List<CellObject> { preservedObject };
objectLists[3] = preservedObjectList;
var preservedObjectListsArray = objectLists;

var oldMapCellFreeProcessMsgCriticalSection = M2Share.ProcessMsgCriticalSection;
var oldMapCellFreeObjectManager = M2Share.ObjectManager;
var oldMapCellFreeRandomNumber = M2Share.RandomNumber;
try
{
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.RandomNumber = RandomNumber.GetInstance();

    var mapCellFreePlayer = new TPlayObject
    {
        m_btPermission = 5,
        m_PEnvir = mapCellFreeEnvironment
    };
    var mapCellFreeCommand = new MapCellFreeCommand();
    var mapCellFreeRegistration = typeof(MapCellFreeCommand)
        .GetCustomAttribute<GameSvr.CommandSystem.GameCommandAttribute>()!;
    mapCellFreeCommand.Register(mapCellFreeRegistration,
        typeof(MapCellFreeCommand).GetMethod("MapCellFree")!);

    mapCellFreeCommand.MapCellFree(mapCellFreePlayer);
    var objectListsAfterDirectCall = (IList<CellObject>[])environmentType.GetField(
            "MapCellObjectLists", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(mapCellFreeEnvironment)!;
    Equal(true, attributes.All(attribute => attribute == CellAttribute.Walk),
        "live MapCellFree sets every terrain attribute to Walk");
    Equal((byte)0x5A, skillFlags[3],
        "live MapCellFree preserves skill flag");
    Equal(true, ReferenceEquals(preservedObjectListsArray, objectListsAfterDirectCall),
        "live MapCellFree preserves object-list array");
    Equal(true, ReferenceEquals(preservedObjectList, objectListsAfterDirectCall[3]),
        "live MapCellFree preserves cell object-list reference");
    Equal(1, objectListsAfterDirectCall[3].Count,
        "live MapCellFree preserves cell object count");
    Equal(true, ReferenceEquals(preservedObject, objectListsAfterDirectCall[3][0]),
        "live MapCellFree preserves cell object");
    Equal(0, mapCellFreePlayer.m_MsgList.Count,
        "live MapCellFree sends no SysMsg");

    var oldMapCellFreeConfig = M2Share.g_Config;
    var mapCellFreeConfig = oldMapCellFreeConfig ?? new GameSvrConfig();
    var oldMapCellFreeTestServer = mapCellFreeConfig.boTestServer;
    try
    {
        M2Share.g_Config = mapCellFreeConfig;
        mapCellFreeConfig.boTestServer = false;

        Array.Fill(attributes, CellAttribute.HighWall);
        mapCellFreePlayer.m_btPermission = 4;
        Equal("该命令需要5级GM才能使用",
            mapCellFreeCommand.Handle("ignored text", mapCellFreePlayer),
            "live MapCellFree permission 4 is rejected by normal gate");
        Equal(true, attributes.All(attribute => attribute == CellAttribute.HighWall),
            "live MapCellFree permission 4 preserves terrain attributes");

        mapCellFreePlayer.m_btPermission = 5;
        Equal<string>(null,
            mapCellFreeCommand.Handle("ignored extra parameters", mapCellFreePlayer),
            "live MapCellFree permission 5 executes and ignores text");
        Equal(true, attributes.All(attribute => attribute == CellAttribute.Walk),
            "live MapCellFree permission 5 clears terrain attributes");
    }
    finally
    {
        mapCellFreeConfig.boTestServer = oldMapCellFreeTestServer;
        M2Share.g_Config = oldMapCellFreeConfig;
    }

    mapCellFreePlayer.m_PEnvir = null;
    mapCellFreeCommand.MapCellFree(mapCellFreePlayer);
    mapCellFreeCommand.MapCellFree(null);
    Equal(0, mapCellFreePlayer.m_MsgList.Count,
        "live MapCellFree null player/map stays silent");

    var uninitializedMap = new Envirnoment();
    mapCellFreePlayer.m_PEnvir = uninitializedMap;
    mapCellFreeCommand.MapCellFree(mapCellFreePlayer);
    Equal<object>(null, environmentType.GetField("MapCellAttributes",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(uninitializedMap),
        "live MapCellFree uninitialized map safely remains uninitialized");
    Equal(0, mapCellFreePlayer.m_MsgList.Count,
        "live MapCellFree uninitialized map stays silent");
}
finally
{
    M2Share.ProcessMsgCriticalSection = oldMapCellFreeProcessMsgCriticalSection;
    M2Share.ObjectManager = oldMapCellFreeObjectManager;
    M2Share.RandomNumber = oldMapCellFreeRandomNumber;
}

// ---------------------------------------------------------------------------
// 13) reshuaMonScript (476): replace active scripts only, with exact messages
// ---------------------------------------------------------------------------
var monsterScriptRoot = Path.Combine(Path.GetTempPath(),
    "loym2-reshua-mon-script-" + Guid.NewGuid().ToString("N"));
var monsterScriptDirectory = Path.Combine(monsterScriptRoot, "MonScript");
var commonScriptDirectory = Path.Combine(monsterScriptRoot, "CommonScripts");
var monsterScriptPath = Path.Combine(monsterScriptDirectory, "ReloadProbe.pas");
var unrelatedScriptPath = Path.Combine(commonScriptDirectory, "Unrelated.pas");
Directory.CreateDirectory(monsterScriptDirectory);
Directory.CreateDirectory(commonScriptDirectory);
File.WriteAllText(Path.Combine(monsterScriptRoot, "monScript.txt"),
    "ReloadProbe" + Environment.NewLine);
File.WriteAllText(monsterScriptPath, """
    program ReloadProbe;

    var InitCount: Integer;

    procedure OnInitialize;
    begin
      InitCount := InitCount + 1;
    end;

    begin
    end.
    """);
File.WriteAllText(unrelatedScriptPath, """
    program Unrelated;
    begin
    end.
    """);

var oldReloadProcessMsgCriticalSection = M2Share.ProcessMsgCriticalSection;
var oldReloadObjectManager = M2Share.ObjectManager;
var oldReloadRandomNumber = M2Share.RandomNumber;
var oldPasEngine = M2Share.PasEngine;
var oldReloadConfig = M2Share.g_Config;
var reloadConfig = oldReloadConfig ?? new GameSvrConfig();
var oldReloadTestServer = reloadConfig.boTestServer;
var oldReloadPrefix = reloadConfig.boShowPreFixMsg;
var oldReloadGreenForeground = reloadConfig.btGreenMsgFColor;
var oldReloadGreenBackground = reloadConfig.btGreenMsgBColor;
try
{
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.RandomNumber = RandomNumber.GetInstance();
    M2Share.g_Config = reloadConfig;
    reloadConfig.boTestServer = false;
    reloadConfig.boShowPreFixMsg = false;
    reloadConfig.btGreenMsgFColor = 0xDB;
    reloadConfig.btGreenMsgBColor = 0xFF;

    var host = new PasScriptHost(monsterScriptRoot);
    M2Share.PasEngine = host;
    var animal = new Monster { m_sCharName = "ReloadProbe" };
    var secondAnimal = new Monster { m_sCharName = "ReloadProbe" };
    Equal(true, host.TryInitializeMonsterScript(animal),
        "live reshuaMonScript creates initial active script state");
    Equal(true, host.TryInitializeMonsterScript(secondAnimal),
        "live reshuaMonScript creates second same-path active state");

    var firstState = GetMonsterScriptState(host, animal.ObjectId);
    var secondState = GetMonsterScriptState(host, secondAnimal.ObjectId);
    Equal(true, firstState != null,
        "live reshuaMonScript initial state is indexed");
    Equal(true, secondState != null,
        "live reshuaMonScript second state is indexed");
    var firstProgram = ReadPrivateField<PasProgram>(firstState, "Program");
    var firstInterpreter = ReadPrivateField<PasInterpreter>(firstState,
        "Interpreter");
    var secondInterpreter = ReadPrivateField<PasInterpreter>(secondState,
        "Interpreter");
    Equal(true, ReferenceEquals(firstProgram,
            ReadPrivateField<PasProgram>(secondState, "Program")),
        "live reshuaMonScript same-path states share parsed program");
    Equal(false, ReferenceEquals(firstInterpreter, secondInterpreter),
        "live reshuaMonScript same-path states have independent interpreters");
    Equal(true, ReadPrivateField<bool>(firstState, "Initialized"),
        "live reshuaMonScript initial state initialized");
    Equal(1, ReadPasGlobalInt(firstInterpreter, "InitCount"),
        "live reshuaMonScript initial OnInitialize ran once");

    var unrelatedProgram = LoadProgram(host, unrelatedScriptPath);
    var monsterPathsField = typeof(PasScriptHost).GetField(
        "_monsterScriptPaths", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var monsterPaths = monsterPathsField.GetValue(host)!;
    var monsterPathCount = (int)monsterPaths.GetType().GetProperty("Count")!
        .GetValue(monsterPaths)!;
    var monsterScriptsLoadedField = typeof(PasScriptHost).GetField(
        "_monsterScriptsLoaded", BindingFlags.Instance | BindingFlags.NonPublic)!;

    // Make a reread observable: native sub_67DC40 must ignore this emptied file.
    File.WriteAllText(Path.Combine(monsterScriptRoot, "monScript.txt"), string.Empty);
    monsterScriptsLoadedField.SetValue(host, false);

    var command = new ReshuaMonScriptCommand();
    var registration = typeof(ReshuaMonScriptCommand)
        .GetCustomAttribute<GameSvr.CommandSystem.GameCommandAttribute>()!;
    command.Register(registration,
        typeof(ReshuaMonScriptCommand).GetMethod("ReshuaMonScript")!);
    var player = new TPlayObject { m_btPermission = 4 };

    Equal("该命令需要5级GM才能使用",
        command.Handle("ignored extra parameters", player),
        "live reshuaMonScript permission 4 is rejected by normal gate");
    Equal(true, ReferenceEquals(firstState,
            GetMonsterScriptState(host, animal.ObjectId)),
        "live reshuaMonScript permission 4 preserves script state");
    Equal(true, ReferenceEquals(secondState,
            GetMonsterScriptState(host, secondAnimal.ObjectId)),
        "live reshuaMonScript permission 4 preserves second script state");
    Equal(0, player.m_MsgList.Count,
        "live reshuaMonScript permission 4 queues no SysMsg");

    player.m_btPermission = 5;
    Equal<string>(null, command.Handle("ignored extra parameters", player),
        "live reshuaMonScript permission 5 executes and ignores text");
    Equal(2, player.m_MsgList.Count,
        "live reshuaMonScript queues exactly two SysMsg records");
    Equal("开始刷新怪物脚本", player.m_MsgList[0].Buff,
        "live reshuaMonScript exact start message");
    Equal("刷新怪物脚本结束", player.m_MsgList[1].Buff,
        "live reshuaMonScript exact end message");
    foreach (var message in player.m_MsgList)
    {
        Equal(Grobal2.RM_SYSMESSAGE, message.wIdent,
            "live reshuaMonScript SysMsg ident");
        Equal(0xDB, message.nParam1,
            "live reshuaMonScript green foreground");
        Equal(0xFF, message.nParam2,
            "live reshuaMonScript green background");
    }

    var reloadedState = GetMonsterScriptState(host, animal.ObjectId);
    var secondReloadedState = GetMonsterScriptState(host, secondAnimal.ObjectId);
    Equal(true, reloadedState != null && !ReferenceEquals(firstState, reloadedState),
        "live reshuaMonScript replaces the active state object");
    Equal(true, secondReloadedState != null &&
                !ReferenceEquals(secondState, secondReloadedState),
        "live reshuaMonScript replaces second active state object");
    var reloadedProgram = ReadPrivateField<PasProgram>(reloadedState, "Program");
    var reloadedInterpreter = ReadPrivateField<PasInterpreter>(reloadedState,
        "Interpreter");
    var secondReloadedInterpreter = ReadPrivateField<PasInterpreter>(
        secondReloadedState, "Interpreter");
    Equal(false, ReferenceEquals(firstProgram, reloadedProgram),
        "live reshuaMonScript reparses unchanged script bytes");
    Equal(false, ReferenceEquals(firstInterpreter, reloadedInterpreter),
        "live reshuaMonScript replaces interpreter state");
    Equal(false, ReferenceEquals(secondInterpreter, secondReloadedInterpreter),
        "live reshuaMonScript replaces second interpreter state");
    Equal(true, ReferenceEquals(reloadedProgram,
            ReadPrivateField<PasProgram>(secondReloadedState, "Program")),
        "live reshuaMonScript reloads a shared path once per command");
    Equal(false, ReferenceEquals(reloadedInterpreter,
            secondReloadedInterpreter),
        "live reshuaMonScript replacements keep per-monster interpreters");
    Equal(true, ReadPrivateField<bool>(reloadedState, "Initialized"),
        "live reshuaMonScript initializes replacement state");
    Equal(1, ReadPasGlobalInt(reloadedInterpreter, "InitCount"),
        "live reshuaMonScript reruns OnInitialize on replacement");
    Equal(1, ReadPasGlobalInt(secondReloadedInterpreter, "InitCount"),
        "live reshuaMonScript reruns OnInitialize on second replacement");
    Equal(true, ReferenceEquals(animal,
            ReadPrivateField<TBaseObject>(reloadedState, "Animal")),
        "live reshuaMonScript preserves owning monster identity");
    Equal(true, ReferenceEquals(unrelatedProgram,
            LoadProgram(host, unrelatedScriptPath)),
        "live reshuaMonScript preserves unrelated PAS cache entries");
    Equal(true, ReferenceEquals(monsterPaths, monsterPathsField.GetValue(host)),
        "live reshuaMonScript preserves monster mapping dictionary");
    Equal(monsterPathCount, (int)monsterPaths.GetType().GetProperty("Count")!
            .GetValue(monsterPaths)!,
        "live reshuaMonScript preserves monster mapping entries");
    Equal(false, (bool)monsterScriptsLoadedField.GetValue(host)!,
        "live reshuaMonScript does not reread monScript.txt");

    player.m_MsgList.Clear();
    File.Delete(monsterScriptPath);
    command.ReshuaMonScript(player);
    Equal(2, player.m_MsgList.Count,
        "live reshuaMonScript failure still brackets reload with two messages");
    Equal("开始刷新怪物脚本", player.m_MsgList[0].Buff,
        "live reshuaMonScript failure exact start message");
    Equal("刷新怪物脚本结束", player.m_MsgList[1].Buff,
        "live reshuaMonScript failure exact end message");
    Equal<object>(null, GetMonsterScriptState(host, animal.ObjectId),
        "live reshuaMonScript missing file removes old active state");
    Equal<object>(null, GetMonsterScriptState(host, secondAnimal.ObjectId),
        "live reshuaMonScript missing file removes second active state");
    Equal(true, ReferenceEquals(unrelatedProgram,
            LoadProgram(host, unrelatedScriptPath)),
        "live reshuaMonScript failure preserves unrelated PAS cache");

    command.ReshuaMonScript(null);
    Equal(2, player.m_MsgList.Count,
        "live reshuaMonScript null player is silent");
}
finally
{
    reloadConfig.boTestServer = oldReloadTestServer;
    reloadConfig.boShowPreFixMsg = oldReloadPrefix;
    reloadConfig.btGreenMsgFColor = oldReloadGreenForeground;
    reloadConfig.btGreenMsgBColor = oldReloadGreenBackground;
    M2Share.g_Config = oldReloadConfig;
    M2Share.PasEngine = oldPasEngine;
    M2Share.ProcessMsgCriticalSection = oldReloadProcessMsgCriticalSection;
    M2Share.ObjectManager = oldReloadObjectManager;
    M2Share.RandomNumber = oldReloadRandomNumber;
    if (Directory.Exists(monsterScriptRoot))
        Directory.Delete(monsterScriptRoot, true);
}

// ---------------------------------------------------------------------------
// 14) SpiderWebTest (340): lasttime/codetime/effect -> 0xFCFF; else silent
// ---------------------------------------------------------------------------
foreach (var (sub, br) in new[] { ("lasttime", "lasttime"), ("codetime", "codetime"), ("effect", "effect") })
{
    var r = NativeGmMonsterMapCommands.Evaluate("SpiderWebTest", 5, new[] { sub, "5" });
    Equal(NativeMonsterMapOutcome.ExecutedWithGmMessage, r.Outcome, $"SpiderWebTest {sub} -> ExecutedWithGmMessage");
    Equal(br, r.Branch, $"SpiderWebTest {sub} branch");
    Equal(0xFCFF, r.NativeSysMsgIdent, $"SpiderWebTest {sub} ident 0xFCFF");
    Equal(false, r.CoreBodyDeferred, $"SpiderWebTest {sub} inline");
}
Equal(NativeMonsterMapOutcome.RejectedSilently,
    NativeGmMonsterMapCommands.Evaluate("SpiderWebTest", 5, new[] { "bogus" }).Outcome,
    "SpiderWebTest bogus -> RejectedSilently");

// ---------------------------------------------------------------------------
// 15) AutoMove (233): both coords valid -> Executed; a -1 coord -> silent
// ---------------------------------------------------------------------------
var amOk = NativeGmMonsterMapCommands.Evaluate("AutoMove", 5, new[] { "MapA", "100", "200" });
Equal(NativeMonsterMapOutcome.Executed, amOk.Outcome, "AutoMove valid -> Executed");
Equal("coords-ok", amOk.Branch, "AutoMove valid branch");
Equal("sub_6D3024", amOk.NativeCore, "AutoMove core");
Equal(NativeGmMonsterMapCommands.NoSysMsg, amOk.NativeSysMsgIdent, "AutoMove no SysMsg");
Equal(NativeMonsterMapOutcome.RejectedSilently,
    NativeGmMonsterMapCommands.Evaluate("AutoMove", 5, new[] { "MapA", "100" }).Outcome,
    "AutoMove missing Y -> RejectedSilently");
Equal(NativeMonsterMapOutcome.RejectedSilently,
    NativeGmMonsterMapCommands.Evaluate("AutoMove", 5, new[] { "MapA", "x", "y" }).Outcome,
    "AutoMove non-numeric coords -> RejectedSilently");

// ---------------------------------------------------------------------------
// 16) setRecoverFactor (375): both args -> Executed; missing -> silent
// ---------------------------------------------------------------------------
var srf = NativeGmMonsterMapCommands.Evaluate("setRecoverFactor", 4, new[] { "10", "20" });
Equal(NativeMonsterMapOutcome.Executed, srf.Outcome, "setRecoverFactor both -> Executed");
Equal("sub_62ECE0", srf.NativeCore, "setRecoverFactor core");
Equal(NativeMonsterMapOutcome.RejectedSilently,
    NativeGmMonsterMapCommands.Evaluate("setRecoverFactor", 4, new[] { "10" }).Outcome,
    "setRecoverFactor missing mp -> RejectedSilently");

// ---------------------------------------------------------------------------
// 17) LoadMonGen (529): mongen reload; mon found/not-found/idx0; unknown silent
// ---------------------------------------------------------------------------
var lmg = NativeGmMonsterMapCommands.Evaluate("LoadMonGen", 3, new[] { "mongen" });
Equal(NativeMonsterMapOutcome.ExecutedWithGmMessage, lmg.Outcome, "LoadMonGen mongen -> ExecutedWithGmMessage");
Equal("mongen", lmg.Branch, "LoadMonGen mongen branch");
Equal(0xFFDB, lmg.NativeSysMsgIdent, "LoadMonGen mongen ident");
Equal("sub_67B35C", lmg.NativeCore, "LoadMonGen mongen core");
NativeGmMonsterMapCommands.LoadMonGenMonIndexHook = _ => 1;
Equal("mon-found",
    NativeGmMonsterMapCommands.Evaluate("LoadMonGen", 3, new[] { "mon", "Zuma" }).Branch,
    "LoadMonGen mon idx1 -> mon-found");
NativeGmMonsterMapCommands.LoadMonGenMonIndexHook = _ => -1;
Equal("mon-not-found",
    NativeGmMonsterMapCommands.Evaluate("LoadMonGen", 3, new[] { "mon", "Ghost" }).Branch,
    "LoadMonGen mon idx-1 -> mon-not-found");
NativeGmMonsterMapCommands.LoadMonGenMonIndexHook = _ => 0;
Equal(NativeMonsterMapOutcome.RejectedSilently,
    NativeGmMonsterMapCommands.Evaluate("LoadMonGen", 3, new[] { "mon", "Slot0" }).Outcome,
    "LoadMonGen mon idx0 -> RejectedSilently (only idx==1 reports found)");
NativeGmMonsterMapCommands.LoadMonGenMonIndexHook = null;
Equal(NativeMonsterMapOutcome.RejectedSilently,
    NativeGmMonsterMapCommands.Evaluate("LoadMonGen", 3, new[] { "bogus" }).Outcome,
    "LoadMonGen unknown sub -> RejectedSilently");

// ---------------------------------------------------------------------------
// 18) TempSetMapParam (577): usage / map-missing / add / remove / unsupported / fail
// ---------------------------------------------------------------------------
var tspUsage = NativeGmMonsterMapCommands.Evaluate("TempSetMapParam", 5, System.Array.Empty<string>());
Equal(NativeMonsterMapOutcome.RejectedWithGmMessage, tspUsage.Outcome, "TempSetMapParam no args -> RejectedWithGmMessage");
Equal("usage", tspUsage.Branch, "TempSetMapParam usage branch");
Equal(0xFCFF, tspUsage.NativeSysMsgIdent, "TempSetMapParam usage ident 0xFCFF");

NativeGmMonsterMapCommands.MapExistsHook = _ => false;
var tspMiss = NativeGmMonsterMapCommands.Evaluate("TempSetMapParam", 5, new[] { "MapA", "attr", "1" });
Equal(NativeMonsterMapOutcome.RejectedWithGmMessage, tspMiss.Outcome, "TempSetMapParam missing map -> RejectedWithGmMessage");
Equal(0x38FF, tspMiss.NativeSysMsgIdent, "TempSetMapParam missing map ident 0x38FF");

NativeGmMonsterMapCommands.MapExistsHook = _ => true;
NativeGmMonsterMapCommands.TempSetMapParamStatus = 1;
var tspAdd = NativeGmMonsterMapCommands.Evaluate("TempSetMapParam", 5, new[] { "MapA", "attr", "1" });
Equal(NativeMonsterMapOutcome.ExecutedWithGmMessage, tspAdd.Outcome, "TempSetMapParam add -> ExecutedWithGmMessage");
Equal("added", tspAdd.Branch, "TempSetMapParam add branch");
Equal(0xFCFF, tspAdd.NativeSysMsgIdent, "TempSetMapParam add ident 0xFCFF");
Equal("removed",
    NativeGmMonsterMapCommands.Evaluate("TempSetMapParam", 5, new[] { "MapA", "attr", "0" }).Branch,
    "TempSetMapParam remove branch");
NativeGmMonsterMapCommands.TempSetMapParamStatus = 100;
var tspUnsup = NativeGmMonsterMapCommands.Evaluate("TempSetMapParam", 5, new[] { "MapA", "attr", "1" });
Equal(NativeMonsterMapOutcome.RejectedWithGmMessage, tspUnsup.Outcome, "TempSetMapParam unsupported -> RejectedWithGmMessage");
Equal("unsupported", tspUnsup.Branch, "TempSetMapParam unsupported branch");
Equal(0x38FF, tspUnsup.NativeSysMsgIdent, "TempSetMapParam unsupported ident 0x38FF");
NativeGmMonsterMapCommands.TempSetMapParamStatus = 5;
Equal("fail",
    NativeGmMonsterMapCommands.Evaluate("TempSetMapParam", 5, new[] { "MapA", "attr", "1" }).Branch,
    "TempSetMapParam other-status -> fail branch");
NativeGmMonsterMapCommands.TempSetMapParamStatus = NativeGmMonsterMapCommands.TempSetMapParamSuccess;
NativeGmMonsterMapCommands.MapExistsHook = null;

// ---------------------------------------------------------------------------
// 19) BreakLvCtrl (309): every reporting path uses 0xFFDB (coarse but true)
// ---------------------------------------------------------------------------
var blcReport = NativeGmMonsterMapCommands.Evaluate("BreakLvCtrl", 4, System.Array.Empty<string>());
Equal(NativeMonsterMapOutcome.ExecutedWithGmMessage, blcReport.Outcome, "BreakLvCtrl no arg -> ExecutedWithGmMessage");
Equal("report", blcReport.Branch, "BreakLvCtrl no arg branch");
Equal(0xFFDB, blcReport.NativeSysMsgIdent, "BreakLvCtrl report ident 0xFFDB");
var blcSet = NativeGmMonsterMapCommands.Evaluate("BreakLvCtrl", 4, new[] { "someop" });
Equal("set-or-query", blcSet.Branch, "BreakLvCtrl arg branch");
Equal(0xFFDB, blcSet.NativeSysMsgIdent, "BreakLvCtrl set/query ident 0xFFDB");

// ---------------------------------------------------------------------------
Console.WriteLine($"PASS NativeGmMonsterMapCommandsCheck ({checks} checks): "
    + $"{NativeGmMonsterMapCommands.All.Count} monster/map/npc GM commands modeled "
    + $"({implCount} implemented, {noopCount} registered no-op) — "
    + "registry facts, permission ladder, branch ladders, SysMsg idents, silent no-ops.");
return 0;

static void PrepareRuntimeConfig()
{
    var runtimeDirectory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(runtimeDirectory, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "String.ini"),
        "[String]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "Command.conf"),
        "[Command]" + Environment.NewLine);

    var robotDirectory = Path.Combine(runtimeDirectory, "RobotIni");
    Directory.CreateDirectory(robotDirectory);
    File.WriteAllText(Path.Combine(robotDirectory, "默认.txt"),
        "[Info]" + Environment.NewLine);

    var shareDirectory = Path.Combine(Path.GetFullPath(
        Path.Combine(runtimeDirectory, "..")), "Share");
    Directory.CreateDirectory(shareDirectory);
    File.WriteAllText(Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"),
        "[Integer]" + Environment.NewLine);
    Directory.SetCurrentDirectory(runtimeDirectory);
}

static object GetMonsterScriptState(PasScriptHost host, int objectId)
{
    var states = typeof(PasScriptHost).GetField("_monsterStates",
            BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(host)!;
    var arguments = new object[] { objectId, null };
    var found = (bool)states.GetType().GetMethod("TryGetValue")!
        .Invoke(states, arguments)!;
    return found ? arguments[1] : null;
}

static T ReadPrivateField<T>(object instance, string fieldName)
{
    return (T)instance.GetType().GetField(fieldName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
        .GetValue(instance)!;
}

static int ReadPasGlobalInt(PasInterpreter interpreter, string variableName)
{
    var globals = (System.Collections.IDictionary)typeof(PasInterpreter)
        .GetField("_globals", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(interpreter)!;
    return ((PasValue)globals[variableName]!).AsInt();
}

static PasProgram LoadProgram(PasScriptHost host, string scriptPath)
{
    var method = typeof(PasScriptHost).GetMethod("GetOrLoadProgram",
        BindingFlags.Instance | BindingFlags.NonPublic, null,
        new[] { typeof(string) }, null)!;
    return (PasProgram)method.Invoke(host, new object[] { scriptPath })!;
}
