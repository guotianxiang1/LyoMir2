// NativeGmMoveLeitaiCommandsCheck
//
// Pins GameSvr/Services/NativeGmMoveLeitaiCommands.cs — the dormant model of the
// MOVE/TELEPORT/DYNROOM (family 08) + LEITAI/YABIAO/CROSS-SERVER (family 11) GM
// ("@") command families inside the M2Server dispatcher sub_622820 @0x00622820 —
// against the reversed binary facts (registry name/idx/perm/handler/no-op-sink and
// the per-case branch ladders + SysMsg idents).
//
// Evidence: staging/update_clothes_4637_ida_work/{disp_decomp.txt, big622820.txt,
// world_scan_out.txt, world_scan_lo_out.txt} over m2full.i64
// (SHA256 5540f43b…c049670b14e, image base 0x00400000).

using GameSvr;

int checks = 0;
void Equal<T>(T expected, T actual, string label)
{
    checks++;
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"FAIL {label}: expected [{expected}], actual [{actual}]");
}

// ---------------------------------------------------------------------------
// 1) Dispatch + SysMsg + sink constants
// ---------------------------------------------------------------------------
Equal(0x00622820u, NativeGmMoveLeitaiCommands.DispatcherEa, "sub_622820");
Equal(0x00621F28u, NativeGmMoveLeitaiCommands.IndexLookupEa, "sub_621F28");
Equal(0x00622B1Cu, NativeGmMoveLeitaiCommands.JumpTableEa, "jpt_622B15 base");
Equal(750, NativeGmMoveLeitaiCommands.SwitchMaxIndex, "cmp esi,0x2EE");
Equal(0x0062B648u, NativeGmMoveLeitaiCommands.DefaultCaseEa, "def_622B15 sink");
Equal(0x0062B64Cu, NativeGmMoveLeitaiCommands.EmptyBodyCaseEa, "loc_62B64C empty-body sink");
Equal(0x0062B648u, NativeMoveLeitaiCommand.DefaultHandler, "record DefaultHandler");
Equal(0x0062B64Cu, NativeMoveLeitaiCommand.EmptyBodyHandler, "record EmptyBodyHandler");
Equal(0xFFDB, NativeGmMoveLeitaiCommands.SysMsgGmReply, "ident GM reply");
Equal(0x38FF, NativeGmMoveLeitaiCommands.SysMsgUsage, "ident usage");
Equal(0xFCFF, NativeGmMoveLeitaiCommands.SysMsgNotice, "ident notice");
Equal(2977, NativeGmMoveLeitaiCommands.AllowTeamSelfFlagIndex, "AllowTeam self[+2977]");
Equal(0x007D6728u, NativeGmMoveLeitaiCommands.DynRoomEnabledGlobalEa, "ReloadDynRoomConf gate off_7D6728");

// core EAs (deferred bodies)
Equal(0x006CE56Cu, NativeGmMoveLeitaiCommands.SearchingCoreEa, "Searching sub_6CE56C");
Equal(0x006DF088u, NativeGmMoveLeitaiCommands.FlyToDynRoomLeCoreEa, "flyToDynRoom<=0 sub_6DF088");
Equal(0x006DF020u, NativeGmMoveLeitaiCommands.FlyToDynRoomGtCoreEa, "flyToDynRoom>0 sub_6DF020");
Equal(0x006C32D0u, NativeGmMoveLeitaiCommands.AddGroupMemberCoreEa, "AddGroupMember sub_6C32D0");
Equal(0x00652784u, NativeGmMoveLeitaiCommands.FindPlayerEa, "FindPlayer sub_652784");
Equal(0x005FBA58u, NativeGmMoveLeitaiCommands.ReloadDynRoomCoreEa, "ReloadDynRoomConf sub_5FBA58");
Equal(0x00713094u, NativeGmMoveLeitaiCommands.UnlockAllTranserCoreEa, "unlockAllTranser sub_713094");
Equal(0x007130E8u, NativeGmMoveLeitaiCommands.UnlockTranserCoreEa, "unlockTranser sub_7130E8");
Equal(0x006FAD74u, NativeGmMoveLeitaiCommands.CloseYaBiaoCoreEa, "close-yabiao sub_6FAD74");

// ---------------------------------------------------------------------------
// 2) Registry facts (Name, Idx, Perm, Handler, Implemented, EmptyBodySink)
// ---------------------------------------------------------------------------
var expected = new (string Name, int Idx, int Perm, uint Handler, bool Impl, bool EmptyBody)[]
{
    ("Searching",         30,  0, 0x00623B4Eu, true,  false),
    ("AllowTeam",         256, 3, 0x006264F4u, true,  false),
    ("flyToDynRoom",      257, 3, 0x00626503u, true,  false),
    ("AddGroupMember",    497, 5, 0x00629307u, true,  false),
    ("ReloadDynRoomConf", 579, 4, 0x00629ACDu, true,  false),
    ("unlockAllTranser",  470, 4, 0x00628C5Eu, true,  false),
    ("unlockTranser",     471, 4, 0x00628C8Bu, true,  false),
    ("SetGsTaskVersion",  477, 3, 0x0062B59Cu, true,  false),
    ("testCreateDynRoom", 369, 5, 0x0062B64Cu, false, true),   // loc_62B64C
    ("SetLTState",        261, 5, 0x0062B64Cu, false, true),
    ("SetLTLimit",        266, 5, 0x0062B64Cu, false, true),
    ("queryTAScore",      448, 3, 0x0062B64Cu, false, true),
    ("ReloadLeitaiBlock", 563, 4, 0x0062B64Cu, false, true),
    ("CloseYaBiao",       509, 3, 0x0062B648u, false, false),  // def_622B15
    ("ReloadTransDuobao", 580, 4, 0x0062B648u, false, false),
};

Equal(15, expected.Length, "expected family size (08+11)");
Equal(expected.Length, NativeGmMoveLeitaiCommands.All.Count, "modeled command count");

int impl = 0, noop = 0, empty = 0, def = 0;
foreach (var e in expected)
{
    var c = NativeGmMoveLeitaiCommands.Find(e.Name);
    Equal(true, c != null, $"registry has {e.Name}");
    Equal(e.Idx, c.DispatchIndex, $"{e.Name}.Idx");
    Equal(e.Perm, c.RequiredPerm, $"{e.Name}.Perm");
    Equal(e.Handler, c.HandlerAddress, $"{e.Name}.Handler");
    Equal(e.Impl, c.Implemented, $"{e.Name}.Implemented");
    Equal(NativeMoveLeitaiCommand.JumpTableBase + (uint)e.Idx * 4, c.JumpSlotAddress, $"{e.Name}.JumpSlot");
    if (e.Impl) { impl++; Equal(NativeNoOpSink.None, c.Sink, $"{e.Name}.Sink None"); }
    else
    {
        noop++;
        var wantSink = e.EmptyBody ? NativeNoOpSink.EmptyBody : NativeNoOpSink.DefaultCase;
        Equal(wantSink, c.Sink, $"{e.Name}.Sink");
        if (e.EmptyBody) empty++; else def++;
    }
}
Equal(8, impl, "implemented count");
Equal(7, noop, "no-op count");
Equal(5, empty, "loc_62B64C empty-body sink count");
Equal(2, def, "def_622B15 default sink count");

// ---------------------------------------------------------------------------
// 3) Unknown + permission ladder
// ---------------------------------------------------------------------------
Equal(NativeMoveLeitaiOutcome.UnknownCommand,
    NativeGmMoveLeitaiCommands.Evaluate("Nope", 10, null).Outcome, "unknown -> UnknownCommand");
Equal(NativeMoveLeitaiOutcome.PermissionRejected,
    NativeGmMoveLeitaiCommands.Evaluate("SetGsTaskVersion", 2, new[] { "x" }).Outcome, "SetGsTaskVersion perm2<3");
Equal(NativeMoveLeitaiOutcome.PermissionRejected,
    NativeGmMoveLeitaiCommands.Evaluate("AddGroupMember", 4, new[] { "bob" }).Outcome, "AddGroupMember perm4<5");

// ---------------------------------------------------------------------------
// 4) No-ops -> SilentNoOp + reused NativeGmDefaultNoOp
// ---------------------------------------------------------------------------
foreach (var n in new[] { "testCreateDynRoom", "SetLTState", "SetLTLimit", "queryTAScore",
                          "ReloadLeitaiBlock", "CloseYaBiao", "ReloadTransDuobao" })
{
    Equal(NativeMoveLeitaiOutcome.SilentNoOp,
        NativeGmMoveLeitaiCommands.Evaluate(n, 10, new[] { "a", "b" }).Outcome, $"{n} -> SilentNoOp");
    var d = NativeGmMoveLeitaiCommands.EvaluateUnimplemented(n);
    Equal(true, d.Recognized, $"{n} NoOp.Recognized");
    Equal(false, d.MutatesState, $"{n} NoOp.MutatesState");
    Equal(false, d.SendsResponse, $"{n} NoOp.SendsResponse");
}

// ---------------------------------------------------------------------------
// 5) Family-08 branch ladders
// ---------------------------------------------------------------------------
NativeGmMoveLeitaiCommands.SearchingGatePasses = true;
var srch = NativeGmMoveLeitaiCommands.Evaluate("Searching", 0, new[] { "bob" });
Equal(NativeMoveLeitaiOutcome.Executed, srch.Outcome, "Searching gate ok -> Executed");
Equal("sub_6CE56C", srch.NativeCore, "Searching core");
NativeGmMoveLeitaiCommands.SearchingGatePasses = false;
Equal(NativeMoveLeitaiOutcome.RejectedSilently,
    NativeGmMoveLeitaiCommands.Evaluate("Searching", 0, new[] { "bob" }).Outcome, "Searching gate blocked -> silent");
NativeGmMoveLeitaiCommands.SearchingGatePasses = true;

var at = NativeGmMoveLeitaiCommands.Evaluate("AllowTeam", 3, null);
Equal(NativeMoveLeitaiOutcome.Executed, at.Outcome, "AllowTeam -> Executed");
Equal("set-flag", at.Branch, "AllowTeam branch");
Equal(NativeGmMoveLeitaiCommands.NoSysMsg, at.NativeSysMsgIdent, "AllowTeam no SysMsg");

Equal("room-le-0", NativeGmMoveLeitaiCommands.Evaluate("flyToDynRoom", 3, new[] { "0" }).Branch, "flyToDynRoom <=0");
Equal("sub_6DF088", NativeGmMoveLeitaiCommands.Evaluate("flyToDynRoom", 3, new[] { "0" }).NativeCore, "flyToDynRoom <=0 core");
Equal("room-gt-0", NativeGmMoveLeitaiCommands.Evaluate("flyToDynRoom", 3, new[] { "5" }).Branch, "flyToDynRoom >0");
Equal("sub_6DF020", NativeGmMoveLeitaiCommands.Evaluate("flyToDynRoom", 3, new[] { "5" }).NativeCore, "flyToDynRoom >0 core");

NativeGmMoveLeitaiCommands.GmHasGroup = false;
Equal("no-group", NativeGmMoveLeitaiCommands.Evaluate("AddGroupMember", 5, new[] { "bob" }).Branch, "AddGroupMember no-group");
NativeGmMoveLeitaiCommands.GmHasGroup = true;
NativeGmMoveLeitaiCommands.AddGroupTargetFound = false;
Equal("player-not-found", NativeGmMoveLeitaiCommands.Evaluate("AddGroupMember", 5, new[] { "bob" }).Branch, "AddGroupMember not-found");
NativeGmMoveLeitaiCommands.AddGroupTargetFound = true;
var agm = NativeGmMoveLeitaiCommands.Evaluate("AddGroupMember", 5, new[] { "bob" });
Equal(NativeMoveLeitaiOutcome.Executed, agm.Outcome, "AddGroupMember add -> Executed");
Equal("sub_6C32D0", agm.NativeCore, "AddGroupMember core");

NativeGmMoveLeitaiCommands.DynRoomSystemEnabled = false;
Equal("system-off", NativeGmMoveLeitaiCommands.Evaluate("ReloadDynRoomConf", 4, new[] { "R1" }).Branch, "ReloadDynRoomConf off");
NativeGmMoveLeitaiCommands.DynRoomSystemEnabled = true;
Equal("no-arg", NativeGmMoveLeitaiCommands.Evaluate("ReloadDynRoomConf", 4, new[] { "" }).Branch, "ReloadDynRoomConf no-arg");
var rdr = NativeGmMoveLeitaiCommands.Evaluate("ReloadDynRoomConf", 4, new[] { "R1" });
Equal(NativeMoveLeitaiOutcome.ExecutedWithGmMessage, rdr.Outcome, "ReloadDynRoomConf reload -> msg");
Equal(0xFCFF, rdr.NativeSysMsgIdent, "ReloadDynRoomConf ident 0xFCFF");

// ---------------------------------------------------------------------------
// 6) Family-11 branch ladders
// ---------------------------------------------------------------------------
var ua = NativeGmMoveLeitaiCommands.Evaluate("unlockAllTranser", 4, null);
Equal(NativeMoveLeitaiOutcome.ExecutedWithGmMessage, ua.Outcome, "unlockAllTranser -> msg");
Equal(0xFFDB, ua.NativeSysMsgIdent, "unlockAllTranser ident 0xFFDB");
Equal("sub_713094", ua.NativeCore, "unlockAllTranser core");

Equal("usage", NativeGmMoveLeitaiCommands.Evaluate("unlockTranser", 4, new[] { "" }).Branch, "unlockTranser no-arg usage");
Equal(0x38FF, NativeGmMoveLeitaiCommands.Evaluate("unlockTranser", 4, new[] { "" }).NativeSysMsgIdent, "unlockTranser usage ident");
var ut = NativeGmMoveLeitaiCommands.Evaluate("unlockTranser", 4, new[] { "bob" });
Equal(NativeMoveLeitaiOutcome.ExecutedWithGmMessage, ut.Outcome, "unlockTranser one -> msg");
Equal(0xFFDB, ut.NativeSysMsgIdent, "unlockTranser one ident 0xFFDB");

Equal(NativeMoveLeitaiOutcome.RejectedSilently,
    NativeGmMoveLeitaiCommands.Evaluate("SetGsTaskVersion", 3, new[] { "" }).Outcome, "SetGsTaskVersion no-arg silent");
var sg = NativeGmMoveLeitaiCommands.Evaluate("SetGsTaskVersion", 3, new[] { "x" });
Equal(NativeMoveLeitaiOutcome.Executed, sg.Outcome, "SetGsTaskVersion arg -> Executed");
Equal("close-yabiao", sg.Branch, "SetGsTaskVersion branch");
Equal("sub_6FAD74", sg.NativeCore, "SetGsTaskVersion core (close yabiao)");

Console.WriteLine($"PASS NativeGmMoveLeitaiCommandsCheck ({checks} checks): "
    + $"{NativeGmMoveLeitaiCommands.All.Count} move/leitai GM commands modeled "
    + $"({impl} implemented, {noop} no-op = {empty} loc_62B64C + {def} def_622B15) — "
    + "registry, permission ladder, branch ladders, SysMsg idents, dual no-op sinks.");
return 0;
