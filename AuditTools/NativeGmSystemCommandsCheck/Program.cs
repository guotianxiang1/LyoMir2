// NativeGmSystemCommandsCheck
//
// Pins GameSvr/Services/NativeGmSystemCommands.cs — the dormant model of the
// SYSTEM / SERVER / GM-ADMIN GM ("@") command family inside the M2Server dispatcher
// sub_622820 @0x00622820 — against the reversed binary facts (registry: name /
// dispatchIndex / requiredPerm / case-branch handler / no-op sink, and each
// implemented case's branch ladder).
//
// Evidence: staging/update_clothes_4637_ida_work/{disp_decomp.txt, big622820.txt}
// over m2full.i64 (SHA256 5540f43b…c049670b14e, image base 0x00400000), plus the
// census in staging/gm_full_inventory_20260731.md. The switch case number ==
// dispatchIndex was verified (case 27 -> 0x00623A42, case 723 -> 0x0062A985, etc.).

using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

PrepareRuntimeFiles();

int checks = 0;

// SINGLE generic assertion helper (top-level statements cannot overload a local
// function, so every fact — int / uint / bool / string / enum — flows through here).
void Equal<T>(T expected, T actual, string label)
{
    checks++;
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"FAIL {label}: expected [{expected}], actual [{actual}]");
}

// Reset every injectable hook to its fail-closed default between branch tests.
void ResetHooks()
{
    NativeGmSystemCommands.RestHasTargets = false;
    NativeGmSystemCommands.RestBlockedHere = false;
    NativeGmSystemCommands.WolongActive = false;
    NativeGmSystemCommands.WolongReloadOk = true;
    NativeGmSystemCommands.ValidFuncReloadOk = true;
    NativeGmSystemCommands.SetApFeatureEnabled = false;
    NativeGmSystemCommands.SetApModeEnabled = false;
    NativeGmSystemCommands.SetApTargetExistsHook = null;
    NativeGmSystemCommands.LogSwitchExistsHook = null;
    NativeGmSystemCommands.ScriptParamValueValidHook = null;
}

// ---------------------------------------------------------------------------
// 1) Dispatch constants
// ---------------------------------------------------------------------------
Equal(0x00622B1Cu, NativeSystemAdminCommand.JumpTableBase, "jpt_622B15 base");
Equal(0x0062B648u, NativeSystemAdminCommand.DefaultHandler, "def_622B15 (handled=0 no-op)");
Equal(0x0062B64Cu, NativeSystemAdminCommand.EmptyBodyHandler, "loc_62B64C (empty-case no-op)");
Equal(0xFFDB, NativeGmSystemCommands.SysMsgGmReply, "SysMsg ident: GM reply (cx=-37)");
Equal(0x38FF, NativeGmSystemCommands.SysMsgUsage, "SysMsg ident: usage/notice (cx=14591)");
Equal(0x277D, NativeGmSystemCommands.EffectSkyRocket, "SendEffect ident: SkyRocket");
Equal(0x2905, NativeGmSystemCommands.EffectBodyEffect, "SendEffect ident: TestBodyEffect");

// ---------------------------------------------------------------------------
// 2) Registry facts — name / index / perm / case-branch handler / implemented,
//    exactly as decoded from the command records + jump table + switch bodies.
//    (Name, DispatchIndex, RequiredPerm, HandlerAddress, Implemented)
// ---------------------------------------------------------------------------
var expected = new (string Name, int Idx, int Perm, uint Handler, bool Impl)[]
{
    // --- 25 implemented ---
    ("Rest",               27,  0, 0x00623A42u, true),
    ("GsZx",               55,  3, 0x006240E7u, true),
    ("AllZx",              56,  3, 0x00624122u, true),
    ("UpGrade",            68,  2, 0x00624A50u, true),
    ("SkyRocket",          70,  3, 0x00624BABu, true),
    ("supergm",            119, 4, 0x00625253u, true),
    ("ReloadSkyPrize",     121, 4, 0x0062526Du, true),
    ("SkyIncome",          138, 4, 0x006253DDu, true),
    ("Reshuawolong",       156, 4, 0x00625846u, true),
    ("ServerInfo",         194, 3, 0x00625B08u, true),
    ("DoNewRes",           205, 5, 0x00625D89u, true),
    ("ReLoadGmFile",       206, 5, 0x00625DA4u, true),
    ("ReLoadTask",         234, 4, 0x00625DEFu, true),
    ("GetV",               235, 3, 0x00625DFCu, true),
    ("SetV",               237, 5, 0x00625E2Au, true),
    ("GetSysTime",         273, 4, 0x00626AF3u, true),
    ("getg",               282, 4, 0x00626D21u, true),
    ("setg",               283, 4, 0x00626DC0u, true),
    ("TestBodyEffect",     296, 5, 0x00626ED3u, true),
    ("LoadValidFunc",      350, 4, 0x006278A2u, true),
    ("reloadTaskDispatch", 459, 4, 0x00628C39u, true),
    ("GMPower",            475, 5, 0x0062919Du, true),
    ("LogSwitch",          482, 4, 0x00628CFDu, true),
    ("LogQueueSwitch",     578, 4, 0x00624027u, true),
    ("SetAP",              723, 2, 0x0062A985u, true),
    // --- 8 def_622B15 (0x0062B648) no-ops ---
    ("ReloadQuest",        150, 4, 0x0062B648u, false),
    ("SetAllGM",           335, 5, 0x0062B648u, false),
    ("ScriptTest",         337, 5, 0x0062B648u, false),
    ("chgMenPaiName",      370, 5, 0x0062B648u, false),
    ("setMenPaiPopularity",371, 5, 0x0062B648u, false),
    ("ReloadTBBConfig",    380, 4, 0x0062B648u, false),
    ("FileOperate",        483, 5, 0x0062B648u, false),
    ("lookzhenqi",         550, 4, 0x0062B648u, false),
    // --- 1 loc_62B64C (0x0062B64C) empty-case no-op ---
    ("reloadrabbit",       532, 4, 0x0062B64Cu, false),
};

Equal(34, expected.Length, "family-12 command count (25 impl + 9 no-op)");
Equal(expected.Length, NativeGmSystemCommands.All.Count, "modeled command count");

int implCount = 0, noopCount = 0;
foreach (var e in expected)
{
    var c = NativeGmSystemCommands.Find(e.Name);
    Equal(true, c != null, $"registry has {e.Name}");
    Equal(e.Name, c.Name, $"{e.Name}.Name");
    Equal(e.Idx, c.DispatchIndex, $"{e.Name}.DispatchIndex");
    Equal(e.Perm, c.RequiredPerm, $"{e.Name}.RequiredPerm");
    Equal(e.Handler, c.HandlerAddress, $"{e.Name}.HandlerAddress");
    Equal(e.Impl, c.Implemented, $"{e.Name}.Implemented");
    Equal(NativeSystemAdminCommand.JumpTableBase + (uint)e.Idx * 4,
        c.JumpSlotAddress, $"{e.Name}.JumpSlotAddress");
    if (e.Impl) implCount++; else noopCount++;
}
Equal(25, implCount, "implemented count");
Equal(9, noopCount, "no-op count");

// Spot-check exact jump-slot addresses (base + idx*4).
Equal(0x00622B88u, NativeGmSystemCommands.Find("Rest").JumpSlotAddress, "Rest ptr@");
Equal(0x00623668u, NativeGmSystemCommands.Find("SetAP").JumpSlotAddress, "SetAP ptr@");

// ---------------------------------------------------------------------------
// 3) Unknown token and permission ladder
// ---------------------------------------------------------------------------
Equal(NativeSystemAdminOutcome.UnknownCommand,
    NativeGmSystemCommands.Evaluate("NoSuchThing", 10, null).Outcome,
    "non-family token -> UnknownCommand");

// GsZx needs perm 3: perm 2 is treated as unknown (sub_621F28 returns 0).
Equal(NativeSystemAdminOutcome.PermissionRejected,
    NativeGmSystemCommands.Evaluate("GsZx", 2, null).Outcome,
    "GsZx perm 2 < 3 -> PermissionRejected");
Equal(NativeSystemAdminOutcome.ExecutedWithGmMessage,
    NativeGmSystemCommands.Evaluate("GsZx", 3, null).Outcome,
    "GsZx perm 3 -> ExecutedWithGmMessage");
// Rest is perm 0: available to everyone (with the fail-closed no-targets branch).
Equal(NativeSystemAdminOutcome.RejectedSilently,
    NativeGmSystemCommands.Evaluate("Rest", 0, null).Outcome,
    "Rest perm 0 reachable; no targets -> RejectedSilently");
// SetAP is perm 2.
Equal(NativeSystemAdminOutcome.PermissionRejected,
    NativeGmSystemCommands.Evaluate("SetAP", 1, new[] { "bob" }).Outcome,
    "SetAP perm 1 < 2 -> PermissionRejected");

// ---------------------------------------------------------------------------
// 4) No-ops: both shared sinks
// ---------------------------------------------------------------------------
Equal(NativeSystemAdminOutcome.SilentNoOp,
    NativeGmSystemCommands.Evaluate("ReloadQuest", 10, null).Outcome,
    "ReloadQuest (def_622B15) -> SilentNoOp");
Equal(NativeSystemAdminOutcome.SilentNoOp,
    NativeGmSystemCommands.Evaluate("FileOperate", 10, new[] { "up", "BaseDir" }).Outcome,
    "FileOperate (def_622B15) -> SilentNoOp");
Equal(NativeSystemAdminOutcome.SilentNoOp,
    NativeGmSystemCommands.Evaluate("reloadrabbit", 10, null).Outcome,
    "reloadrabbit (loc_62B64C empty case) -> SilentNoOp");

// ---------------------------------------------------------------------------
// 5) Simple single-path commands: outcome + SysMsg ident
// ---------------------------------------------------------------------------
var supergm = NativeGmSystemCommands.Evaluate("supergm", 4, null);
Equal(NativeSystemAdminOutcome.Executed, supergm.Outcome, "supergm -> Executed");
Equal("sub_6D782C", supergm.NativeCore, "supergm -> sub_6D782C");
Equal(NativeGmSystemCommands.NoSysMsg, supergm.NativeSysMsgIdent, "supergm no inline SysMsg");

Equal("sub_65411C", NativeGmSystemCommands.Evaluate("AllZx", 3, null).NativeCore, "AllZx -> sub_65411C");
Equal(0xFFDB, NativeGmSystemCommands.Evaluate("GetSysTime", 4, null).NativeSysMsgIdent, "GetSysTime -> GM reply");
Equal(0xFFDB, NativeGmSystemCommands.Evaluate("ReLoadGmFile", 5, null).NativeSysMsgIdent, "ReLoadGmFile -> GM reply");
// SkyIncome and GMPower use the usage ident (0x38FF) on their success path.
Equal(0x38FF, NativeGmSystemCommands.Evaluate("SkyIncome", 4, null).NativeSysMsgIdent, "SkyIncome -> usage ident");
Equal(NativeSystemAdminOutcome.ExecutedWithGmMessage,
    NativeGmSystemCommands.Evaluate("GMPower", 5, null).Outcome, "GMPower -> ExecutedWithGmMessage");
Equal(0x38FF, NativeGmSystemCommands.Evaluate("GMPower", 5, null).NativeSysMsgIdent, "GMPower -> usage ident");
// Visual verbs fire SendEffect, not a text SysMsg.
Equal(NativeSystemAdminOutcome.Executed, NativeGmSystemCommands.Evaluate("SkyRocket", 3, new[] { "1" }).Outcome, "SkyRocket -> Executed");
Equal(NativeGmSystemCommands.NoSysMsg, NativeGmSystemCommands.Evaluate("TestBodyEffect", 5, new[] { "1" }).NativeSysMsgIdent, "TestBodyEffect no text SysMsg");
// Quest-field get/set delegate silently at the case level.
Equal("sub_6CD574", NativeGmSystemCommands.Evaluate("GetV", 3, new[] { "bob", "1" }).NativeCore, "GetV -> sub_6CD574");
Equal("sub_6CD85C", NativeGmSystemCommands.Evaluate("SetV", 5, new[] { "bob", "1", "2", "9" }).NativeCore, "SetV -> sub_6CD85C");

// ---------------------------------------------------------------------------
// 6) Rest (case 27) branch ladder
// ---------------------------------------------------------------------------
ResetHooks();
Equal(NativeSystemAdminOutcome.RejectedSilently,
    NativeGmSystemCommands.Evaluate("Rest", 0, null).Outcome, "Rest no targets -> RejectedSilently");
NativeGmSystemCommands.RestHasTargets = true;
NativeGmSystemCommands.RestBlockedHere = true;
var restBlocked = NativeGmSystemCommands.Evaluate("Rest", 0, null);
Equal(NativeSystemAdminOutcome.RejectedWithGmMessage, restBlocked.Outcome, "Rest map-blocked -> RejectedWithGmMessage");
Equal(0xFFDB, restBlocked.NativeSysMsgIdent, "Rest map-blocked ident");
NativeGmSystemCommands.RestBlockedHere = false;
var restToggle = NativeGmSystemCommands.Evaluate("Rest", 0, null);
Equal(NativeSystemAdminOutcome.ExecutedWithGmMessage, restToggle.Outcome, "Rest toggle -> ExecutedWithGmMessage");
Equal("toggle", restToggle.Branch, "Rest toggle branch");
ResetHooks();

// ---------------------------------------------------------------------------
// 7) Reshuawolong (case 156) branch ladder
// ---------------------------------------------------------------------------
ResetHooks();
Equal(NativeSystemAdminOutcome.RejectedSilently,
    NativeGmSystemCommands.Evaluate("Reshuawolong", 4, null).Outcome, "Reshuawolong inactive -> RejectedSilently");
NativeGmSystemCommands.WolongActive = true;
Equal(NativeSystemAdminOutcome.ExecutedWithGmMessage,
    NativeGmSystemCommands.Evaluate("Reshuawolong", 4, null).Outcome, "Reshuawolong active+ok -> ExecutedWithGmMessage");
NativeGmSystemCommands.WolongReloadOk = false;
var wolongAlt = NativeGmSystemCommands.Evaluate("Reshuawolong", 4, null);
Equal(NativeSystemAdminOutcome.RejectedWithGmMessage, wolongAlt.Outcome, "Reshuawolong active+alt -> RejectedWithGmMessage");
Equal(0x38FF, wolongAlt.NativeSysMsgIdent, "Reshuawolong alt ident");
ResetHooks();

// ---------------------------------------------------------------------------
// 8) ServerInfo (case 194) 6-way sub-token dispatch
// ---------------------------------------------------------------------------
Equal("sub_718894", NativeGmSystemCommands.Evaluate("ServerInfo", 3, new[] { "Event" }).NativeCore, "ServerInfo Event -> sub_718894");
Equal("sub_64BB40", NativeGmSystemCommands.Evaluate("ServerInfo", 3, new[] { "Npc" }).NativeCore, "ServerInfo Npc -> sub_64BB40");
Equal("sub_67D9E4", NativeGmSystemCommands.Evaluate("ServerInfo", 3, new[] { "Monster" }).NativeCore, "ServerInfo Monster -> sub_67D9E4");
Equal("sub_5FCBDC", NativeGmSystemCommands.Evaluate("ServerInfo", 3, new[] { "DynMap" }).NativeCore, "ServerInfo DynMap -> sub_5FCBDC");
Equal("visiblecache", NativeGmSystemCommands.Evaluate("ServerInfo", 3, new[] { "VisibleCache" }).Branch, "ServerInfo VisibleCache branch");
var srvUnknown = NativeGmSystemCommands.Evaluate("ServerInfo", 3, new[] { "bogus" });
Equal(NativeSystemAdminOutcome.RejectedWithGmMessage, srvUnknown.Outcome, "ServerInfo unknown -> RejectedWithGmMessage");
Equal(0x38FF, srvUnknown.NativeSysMsgIdent, "ServerInfo unknown ident");

// ---------------------------------------------------------------------------
// 9) getg / setg (cases 282 / 283) arg guards
// ---------------------------------------------------------------------------
ResetHooks();
Equal(NativeSystemAdminOutcome.ExecutedWithGmMessage,
    NativeGmSystemCommands.Evaluate("getg", 4, new[] { "5", "0" }).Outcome, "getg valid args -> ExecutedWithGmMessage");
Equal(NativeSystemAdminOutcome.RejectedSilently,
    NativeGmSystemCommands.Evaluate("getg", 4, new[] { "5" }).Outcome, "getg missing index -> RejectedSilently");
Equal(NativeSystemAdminOutcome.RejectedSilently,
    NativeGmSystemCommands.Evaluate("getg", 4, new[] { "x", "0" }).Outcome, "getg non-numeric id -> RejectedSilently");

Equal(NativeSystemAdminOutcome.ExecutedWithGmMessage,
    NativeGmSystemCommands.Evaluate("setg", 4, new[] { "5", "0", "9" }).Outcome, "setg valid args -> ExecutedWithGmMessage");
Equal(NativeSystemAdminOutcome.RejectedSilently,
    NativeGmSystemCommands.Evaluate("setg", 4, new[] { "5", "0" }).Outcome, "setg missing value(-2) -> RejectedSilently");
NativeGmSystemCommands.ScriptParamValueValidHook = _ => false;
Equal(NativeSystemAdminOutcome.RejectedSilently,
    NativeGmSystemCommands.Evaluate("setg", 4, new[] { "5", "0", "9" }).Outcome, "setg value rejected by sub_699310 -> RejectedSilently");
ResetHooks();

// ---------------------------------------------------------------------------
// 10) LoadValidFunc (case 350) reload result
// ---------------------------------------------------------------------------
ResetHooks();
Equal(NativeSystemAdminOutcome.ExecutedWithGmMessage,
    NativeGmSystemCommands.Evaluate("LoadValidFunc", 4, null).Outcome, "LoadValidFunc ok -> ExecutedWithGmMessage");
NativeGmSystemCommands.ValidFuncReloadOk = false;
var lvfFail = NativeGmSystemCommands.Evaluate("LoadValidFunc", 4, null);
Equal(NativeSystemAdminOutcome.RejectedWithGmMessage, lvfFail.Outcome, "LoadValidFunc fail -> RejectedWithGmMessage");
Equal(0x38FF, lvfFail.NativeSysMsgIdent, "LoadValidFunc fail ident");
ResetHooks();

var validFuncRoot = Path.Combine(Path.GetTempPath(),
    "native-valid-func-check-" + Guid.NewGuid().ToString("N"));
var oldConfigPath = M2Share.sConfigPath;
var oldConfig = M2Share.g_Config;
var oldProcessMsgCriticalSection = M2Share.ProcessMsgCriticalSection;
var oldObjectManager = M2Share.ObjectManager;
try
{
    Equal(-1, NativeValidScriptFunctionRegistry.Reload(validFuncRoot),
        "LoadValidFunc missing file -> -1");

    var configDirectory = Path.Combine(validFuncRoot, "Config");
    Directory.CreateDirectory(configDirectory);
    var validFuncFile = Path.Combine(configDirectory, "validScriptFunc.txt");
    File.WriteAllText(validFuncFile, "zeta\r\nAlpha\r\n\r\n中文函数\r\n",
        HUtil32.GbkEncoding);
    Equal(4, NativeValidScriptFunctionRegistry.Reload(validFuncRoot),
        "LoadValidFunc GBK line count");
    Equal(true, NativeValidScriptFunctionRegistry.Find("ALPHA"),
        "LoadValidFunc case-insensitive Find");
    Equal(true, NativeValidScriptFunctionRegistry.Find("中文函数"),
        "LoadValidFunc GBK Find");
    Equal(true, NativeValidScriptFunctionRegistry.Find(string.Empty),
        "LoadValidFunc empty-line Find");
    Equal(false, NativeValidScriptFunctionRegistry.Find("missing"),
        "LoadValidFunc missing Find");
    var sorted = NativeValidScriptFunctionRegistry.Snapshot();
    for (var index = 1; index < sorted.Count; index++)
    {
        Equal(true, NativeValidScriptFunctionRegistry.Compare(
                sorted[index - 1], sorted[index]) <= 0,
            "LoadValidFunc native Sort order " + index);
    }

    File.WriteAllBytes(validFuncFile, Array.Empty<byte>());
    Equal(0, NativeValidScriptFunctionRegistry.Reload(validFuncRoot),
        "LoadValidFunc empty file succeeds");
    Equal(0, NativeValidScriptFunctionRegistry.Snapshot().Count,
        "LoadValidFunc empty file clears old list");

    File.WriteAllText(validFuncFile, "Duplicate\r\nduplicate\r\n",
        HUtil32.GbkEncoding);
    Equal(2, NativeValidScriptFunctionRegistry.Reload(validFuncRoot),
        "LoadValidFunc duplicate rows retained");
    Equal(2, NativeValidScriptFunctionRegistry.Snapshot().Count,
        "LoadValidFunc duplicate count");
    Equal(true, NativeValidScriptFunctionRegistry.Find("DUPLICATE"),
        "LoadValidFunc duplicate Find");

    File.WriteAllText(validFuncFile, "OnlyOne\r\n", HUtil32.GbkEncoding);
    Equal(1, NativeValidScriptFunctionRegistry.Reload(validFuncRoot),
        "LoadValidFunc clears before reload");
    Equal(false, NativeValidScriptFunctionRegistry.Find("Alpha"),
        "LoadValidFunc old row removed");
    Equal(true, NativeValidScriptFunctionRegistry.Find("onlyone"),
        "LoadValidFunc replacement row Find");

    File.Delete(validFuncFile);
    Equal(-1, NativeValidScriptFunctionRegistry.Reload(validFuncRoot),
        "LoadValidFunc missing reload result");
    Equal(true, NativeValidScriptFunctionRegistry.Find("ONLYONE"),
        "LoadValidFunc missing file preserves old list");

    File.WriteAllText(validFuncFile, "Locked\r\n", HUtil32.GbkEncoding);
    using (var locked = new FileStream(validFuncFile, FileMode.Open,
               FileAccess.ReadWrite, FileShare.None))
    {
        var readFailed = false;
        try
        {
            NativeValidScriptFunctionRegistry.Reload(validFuncRoot);
        }
        catch (IOException)
        {
            readFailed = true;
        }
        Equal(true, readFailed,
            "LoadValidFunc read exception propagates");
        Equal(0, NativeValidScriptFunctionRegistry.Snapshot().Count,
            "LoadValidFunc read exception leaves cleared list");
    }

    M2Share.sConfigPath = validFuncRoot;
    M2Share.g_Config = new GameSvrConfig
    {
        boShowPreFixMsg = true,
        sHintMsgPreFix = "configured-prefix:",
        btGreenMsgFColor = 1,
        btGreenMsgBColor = 2,
        btRedMsgFColor = 3,
        btRedMsgBColor = 4
    };
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ObjectManager = new ObjectManager();
    var player = new TPlayObject();
    var command = new LoadValidFuncCommand();

    File.WriteAllText(validFuncFile, "Alpha\r\nBeta\r\n",
        HUtil32.GbkEncoding);
    command.LoadValidFunc(player);
    Equal(1, player.m_MsgList.Count,
        "LoadValidFunc success message count");
    var successMessage = player.m_MsgList[0];
    Equal(Grobal2.RM_SYSMESSAGE, successMessage.wIdent,
        "LoadValidFunc success ident");
    Equal(0xDB, successMessage.nParam1,
        "LoadValidFunc success foreground");
    Equal(0xFF, successMessage.nParam2,
        "LoadValidFunc success background");
    Equal("载入脚本安全函数列表成功，共2个函数", successMessage.Buff,
        "LoadValidFunc success text");

    player.m_MsgList.Clear();
    File.Delete(validFuncFile);
    command.LoadValidFunc(player);
    Equal(1, player.m_MsgList.Count,
        "LoadValidFunc failure message count");
    var failureMessage = player.m_MsgList[0];
    Equal(Grobal2.RM_SYSMESSAGE, failureMessage.wIdent,
        "LoadValidFunc failure ident");
    Equal(0xFF, failureMessage.nParam1,
        "LoadValidFunc failure foreground");
    Equal(0x38, failureMessage.nParam2,
        "LoadValidFunc failure background");
    Equal("载入脚本安全函数列表失败", failureMessage.Buff,
        "LoadValidFunc failure text");
}
finally
{
    M2Share.sConfigPath = oldConfigPath;
    M2Share.g_Config = oldConfig;
    M2Share.ProcessMsgCriticalSection = oldProcessMsgCriticalSection;
    M2Share.ObjectManager = oldObjectManager;
    if (Directory.Exists(validFuncRoot))
        Directory.Delete(validFuncRoot, true);
}

// ---------------------------------------------------------------------------
// 11) LogSwitch (case 482) query / set multiplexer
// ---------------------------------------------------------------------------
ResetHooks();
var logAll = NativeGmSystemCommands.Evaluate("LogSwitch", 4, new[] { "_all_" });
Equal(NativeSystemAdminOutcome.ExecutedWithGmMessage, logAll.Outcome, "LogSwitch _all_ -> ExecutedWithGmMessage");
Equal(0x38FF, logAll.NativeSysMsgIdent, "LogSwitch _all_ ident");
Equal(NativeSystemAdminOutcome.RejectedSilently,
    NativeGmSystemCommands.Evaluate("LogSwitch", 4, new[] { "SomeSwitch", "open" }).Outcome,
    "LogSwitch unknown switch -> RejectedSilently");
NativeGmSystemCommands.LogSwitchExistsHook = _ => true;
Equal("open", NativeGmSystemCommands.Evaluate("LogSwitch", 4, new[] { "SomeSwitch", "open" }).Branch, "LogSwitch known+open branch");
Equal("close", NativeGmSystemCommands.Evaluate("LogSwitch", 4, new[] { "SomeSwitch", "Close" }).Branch, "LogSwitch known+close branch");
Equal(NativeSystemAdminOutcome.RejectedSilently,
    NativeGmSystemCommands.Evaluate("LogSwitch", 4, new[] { "SomeSwitch", "bogus" }).Outcome,
    "LogSwitch known+bad-verb -> RejectedSilently");
ResetHooks();

// ---------------------------------------------------------------------------
// 12) LogQueueSwitch (case 578) 0/1 literal
// ---------------------------------------------------------------------------
Equal(NativeSystemAdminOutcome.Executed,
    NativeGmSystemCommands.Evaluate("LogQueueSwitch", 4, new[] { "1" }).Outcome, "LogQueueSwitch 1 -> Executed");
Equal("sub_7130E8", NativeGmSystemCommands.Evaluate("LogQueueSwitch", 4, new[] { "0" }).NativeCore, "LogQueueSwitch -> sub_7130E8");
Equal(NativeSystemAdminOutcome.RejectedSilently,
    NativeGmSystemCommands.Evaluate("LogQueueSwitch", 4, new[] { "2" }).Outcome, "LogQueueSwitch bad arg -> RejectedSilently");

// ---------------------------------------------------------------------------
// 13) SetAP (case 723) two-enable + target ladder
// ---------------------------------------------------------------------------
ResetHooks();
Equal(NativeSystemAdminOutcome.RejectedWithGmMessage,
    NativeGmSystemCommands.Evaluate("SetAP", 2, new[] { "bob" }).Outcome, "SetAP feature off -> RejectedWithGmMessage");
NativeGmSystemCommands.SetApFeatureEnabled = true;
NativeGmSystemCommands.SetApTargetExistsHook = _ => false;
Equal("target-missing",
    NativeGmSystemCommands.Evaluate("SetAP", 2, new[] { "ghost" }).Branch, "SetAP feature on + target missing");
NativeGmSystemCommands.SetApTargetExistsHook = _ => true;
Equal("mode-off",
    NativeGmSystemCommands.Evaluate("SetAP", 2, new[] { "bob" }).Branch, "SetAP feature on + target + mode off");
NativeGmSystemCommands.SetApModeEnabled = true;
var setApOk = NativeGmSystemCommands.Evaluate("SetAP", 2, new[] { "bob" });
Equal(NativeSystemAdminOutcome.ExecutedWithGmMessage, setApOk.Outcome, "SetAP all enabled -> ExecutedWithGmMessage");
Equal("sub_6F9220", setApOk.NativeCore, "SetAP -> sub_6F9220");
Equal(0xFFDB, setApOk.NativeSysMsgIdent, "SetAP apply ident");
ResetHooks();

Console.WriteLine($"PASS NativeGmSystemCommandsCheck ({checks} checks): "
    + $"{NativeGmSystemCommands.All.Count} system/server/GM-admin GM commands modeled "
    + "(25 implemented + 9 no-op; registry facts, permission ladder, branch ladders, dual no-op sinks).");
return 0;

static void PrepareRuntimeFiles()
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
