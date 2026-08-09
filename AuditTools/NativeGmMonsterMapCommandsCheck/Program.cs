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
Equal(NativeMonsterMapOutcome.Executed,
    NativeGmMonsterMapCommands.Evaluate("SetNoKillMapLv", 5, null).Outcome,
    "SetNoKillMapLv perm 5 -> Executed");
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
Delegate("MapCellFree", 5, "sub_77BEB4");
Delegate("SetNoKillMapLv", 5, "sub_6CDBBC");

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
Report("ReloadMonAtt", 4, 0x38FF, true);
Report("reshuaMonScript", 5, 0xFFDB, true);
Report("ReloadMonitemsTreeCfg", 4, 0xFFDB, true);

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
// 8) ThroughRange (136): n<=50 sets the global + confirms; n>50 silent
// ---------------------------------------------------------------------------
var tr30 = NativeGmMonsterMapCommands.Evaluate("ThroughRange", 4, new[] { "30" });
Equal(NativeMonsterMapOutcome.ExecutedWithGmMessage, tr30.Outcome, "ThroughRange 30 -> ExecutedWithGmMessage");
Equal("value-le-50", tr30.Branch, "ThroughRange 30 branch");
Equal(0x38FF, tr30.NativeSysMsgIdent, "ThroughRange 30 ident");
Equal(false, tr30.CoreBodyDeferred, "ThroughRange inline (not deferred)");
Equal(NativeMonsterMapOutcome.ExecutedWithGmMessage,
    NativeGmMonsterMapCommands.Evaluate("ThroughRange", 4, new[] { "" }).Outcome,
    "ThroughRange empty -> default 0 <= 50 -> ExecutedWithGmMessage");
Equal(NativeMonsterMapOutcome.RejectedSilently,
    NativeGmMonsterMapCommands.Evaluate("ThroughRange", 4, new[] { "51" }).Outcome,
    "ThroughRange 51 -> RejectedSilently");

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

// ---------------------------------------------------------------------------
// 11) SpiderWebTest (340): lasttime/codetime/effect -> 0xFCFF; else silent
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
// 12) AutoMove (233): both coords valid -> Executed; a -1 coord -> silent
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
// 13) setRecoverFactor (375): both args -> Executed; missing -> silent
// ---------------------------------------------------------------------------
var srf = NativeGmMonsterMapCommands.Evaluate("setRecoverFactor", 4, new[] { "10", "20" });
Equal(NativeMonsterMapOutcome.Executed, srf.Outcome, "setRecoverFactor both -> Executed");
Equal("sub_62ECE0", srf.NativeCore, "setRecoverFactor core");
Equal(NativeMonsterMapOutcome.RejectedSilently,
    NativeGmMonsterMapCommands.Evaluate("setRecoverFactor", 4, new[] { "10" }).Outcome,
    "setRecoverFactor missing mp -> RejectedSilently");

// ---------------------------------------------------------------------------
// 14) LoadMonGen (529): mongen reload; mon found/not-found/idx0; unknown silent
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
// 15) TempSetMapParam (577): usage / map-missing / add / remove / unsupported / fail
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
// 16) BreakLvCtrl (309): every reporting path uses 0xFFDB (coarse but true)
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
