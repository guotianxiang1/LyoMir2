// NativeGmWorldAdminCommandsCheck
//
// Pins GameSvr/Services/NativeGmWorldAdminCommands.cs — the dormant model of the
// WORLD / OTHER-ENTITY GM ("@") command family inside the M2Server dispatcher
// sub_622820 @0x00622820 — against the reversed binary facts (registry: name /
// dispatchIndex / requiredPerm / handler / no-op, and the per-case branch ladder).
//
// Evidence: staging/update_clothes_4637_ida_work/{big622820.txt, world_scan_out.txt,
// world_scan_lo_out.txt} over m2full.i64
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
// 1) Dispatch constants
// ---------------------------------------------------------------------------
Equal(0x00622B1Cu, NativeWorldAdminCommand.JumpTableBase, "jpt_622B15 base");
Equal(0x0062B648u, NativeWorldAdminCommand.DefaultHandler, "def_622B15 (silent no-op)");
Equal(0xFFDB, NativeGmWorldAdminCommands.SysMsgGmReply, "SysMsg ident: GM reply");
Equal(0x38FF, NativeGmWorldAdminCommands.SysMsgUsage, "SysMsg ident: usage/refusal");

// ---------------------------------------------------------------------------
// 2) Registry facts — name / index / perm / handler / implemented / jump slot,
//    exactly as decoded from the command records + jump table.
//    (Name, DispatchIndex, RequiredPerm, HandlerAddress, Implemented)
// ---------------------------------------------------------------------------
var expected = new (string Name, int Idx, int Perm, uint Handler, bool Impl)[]
{
    ("KickOut",           59,  2, 0x0062424Eu, true),
    ("CallMan",           72,  3, 0x00624C94u, true),
    ("Shuag",             82,  4, 0x00624D93u, true),
    ("CallMob",           83,  4, 0x00624DA6u, true),
    ("MonClear",          181, 4, 0x00625A57u, true),
    ("ReShuaNpc",         207, 3, 0x00625DDFu, true),
    ("SetSysTime",        268, 5, 0x006267DFu, true),
    ("MapDropItem",       306, 4, 0x00627077u, true),
    ("LockTimeChg",       329, 5, 0x0062790Du, true),
    ("CreateCampMon",     339, 4, 0x00627EDDu, true),
    ("SetMapState",       343, 4, 0x00627EEDu, true),
    ("kickOutBlackRoom",  490, 4, 0x0062923Cu, true),
    ("ChgOutlooksByMap",  342, 4, 0x0062B648u, false), // registered -> def_622B15 no-op
    ("ChgDreamCastleWar", 533, 5, 0x0062B648u, false), // registered -> def_622B15 no-op
};

Equal(expected.Length, NativeGmWorldAdminCommands.All.Count, "modeled command count");

foreach (var e in expected)
{
    var c = NativeGmWorldAdminCommands.Find(e.Name);
    Equal(true, c != null, $"registry has {e.Name}");
    Equal(e.Name, c.Name, $"{e.Name}.Name");
    Equal(e.Idx, c.DispatchIndex, $"{e.Name}.DispatchIndex");
    Equal(e.Perm, c.RequiredPerm, $"{e.Name}.RequiredPerm");
    Equal(e.Handler, c.HandlerAddress, $"{e.Name}.HandlerAddress");
    Equal(e.Impl, c.Implemented, $"{e.Name}.Implemented");
    // JumpSlot = base + index*4 (matches ptr@ column of the record scan).
    Equal(NativeWorldAdminCommand.JumpTableBase + (uint)e.Idx * 4,
        c.JumpSlotAddress, $"{e.Name}.JumpSlotAddress");
}

// Spot-check the exact jump-slot addresses from the scan output.
Equal(0x00622C08u, NativeGmWorldAdminCommands.Find("KickOut").JumpSlotAddress, "KickOut ptr@");
Equal(0x006232C4u, NativeGmWorldAdminCommands.Find("kickOutBlackRoom").JumpSlotAddress, "kickOutBlackRoom ptr@");
Equal(0x00623078u, NativeGmWorldAdminCommands.Find("SetMapState").JumpSlotAddress, "SetMapState ptr@");

// ---------------------------------------------------------------------------
// 3) Unknown token and permission ladder
// ---------------------------------------------------------------------------
Equal(NativeWorldAdminOutcome.UnknownCommand,
    NativeGmWorldAdminCommands.Evaluate("NoSuchThing", 10, null).Outcome,
    "non-family token -> UnknownCommand");

// KickOut needs perm 2: perm 1 is treated as unknown (sub_621F28 returns 0).
Equal(NativeWorldAdminOutcome.PermissionRejected,
    NativeGmWorldAdminCommands.Evaluate("KickOut", 1, new[] { "someone" }).Outcome,
    "under-privileged -> PermissionRejected");
Equal(NativeWorldAdminOutcome.Executed,
    NativeGmWorldAdminCommands.Evaluate("KickOut", 2, new[] { "someone" }).Outcome,
    "sufficient perm -> Executed");
Equal("sub_6BEDDC",
    NativeGmWorldAdminCommands.Evaluate("KickOut", 2, new[] { "someone" }).NativeHelper,
    "KickOut delegates to sub_6BEDDC");

// SetSysTime needs perm 5.
Equal(NativeWorldAdminOutcome.PermissionRejected,
    NativeGmWorldAdminCommands.Evaluate("SetSysTime", 4, null).Outcome,
    "SetSysTime perm 4 < 5 -> PermissionRejected");

// ---------------------------------------------------------------------------
// 4) No-ops: registered commands whose handler is def_622B15
// ---------------------------------------------------------------------------
Equal(NativeWorldAdminOutcome.SilentNoOp,
    NativeGmWorldAdminCommands.Evaluate("ChgOutlooksByMap", 10, new[] { "0", "1", "60" }).Outcome,
    "ChgOutlooksByMap -> SilentNoOp");
Equal(NativeWorldAdminOutcome.SilentNoOp,
    NativeGmWorldAdminCommands.Evaluate("ChgDreamCastleWar", 10, null).Outcome,
    "ChgDreamCastleWar -> SilentNoOp");

// ---------------------------------------------------------------------------
// 5) Single-branch delegations (no GM-visible guard)
// ---------------------------------------------------------------------------
Equal("sub_6BF458", NativeGmWorldAdminCommands.Evaluate("CallMan", 3, new[] { "bob" }).NativeHelper, "CallMan -> sub_6BF458");
Equal("sub_6BE470", NativeGmWorldAdminCommands.Evaluate("Shuag", 4, new[] { "Zuma", "5" }).NativeHelper, "Shuag -> sub_6BE470");
Equal("sub_6BFC20", NativeGmWorldAdminCommands.Evaluate("CallMob", 4, new[] { "Zuma", "5", "3" }).NativeHelper, "CallMob -> sub_6BFC20");
Equal("sub_6C6DE8", NativeGmWorldAdminCommands.Evaluate("ReShuaNpc", 3, new[] { "all" }).NativeHelper, "ReShuaNpc -> sub_6C6DE8");
Equal("sub_6EB6B8", NativeGmWorldAdminCommands.Evaluate("CreateCampMon", 4, new[] { "Zuma,1,100,100,5,3,100,100" }).NativeHelper, "CreateCampMon -> sub_6EB6B8");
Equal(NativeWorldAdminOutcome.Executed, NativeGmWorldAdminCommands.Evaluate("LockTimeChg", 5, null).Outcome, "LockTimeChg -> Executed (toggles byte_7DC270)");

// ---------------------------------------------------------------------------
// 6) MapDropItem (case 306) branch ladder
// ---------------------------------------------------------------------------
var open = NativeGmWorldAdminCommands.Evaluate("MapDropItem", 4, new[] { "open" });
Equal(NativeWorldAdminOutcome.Executed, open.Outcome, "MapDropItem open -> Executed");
Equal("open", open.Branch, "MapDropItem open branch");
Equal("sub_62E8F0", open.NativeHelper, "MapDropItem open helper");
Equal(NativeGmWorldAdminCommands.NoSysMsg, open.NativeSysMsgIdent, "MapDropItem open has no inline GM msg");

Equal("close", NativeGmWorldAdminCommands.Evaluate("MapDropItem", 4, new[] { "close" }).Branch, "MapDropItem close branch");

Equal(NativeWorldAdminOutcome.RejectedSilently,
    NativeGmWorldAdminCommands.Evaluate("MapDropItem", 4, new[] { "loaddyn" }).Outcome,
    "MapDropItem loaddyn without room -> RejectedSilently");
var loaddyn = NativeGmWorldAdminCommands.Evaluate("MapDropItem", 4, new[] { "loaddyn", "Room1" });
Equal(NativeWorldAdminOutcome.ExecutedWithGmMessage, loaddyn.Outcome, "MapDropItem loaddyn <room> -> ExecutedWithGmMessage");
Equal(0xFFDB, loaddyn.NativeSysMsgIdent, "MapDropItem loaddyn SysMsg ident");

Equal(NativeWorldAdminOutcome.ExecutedWithGmMessage,
    NativeGmWorldAdminCommands.Evaluate("MapDropItem", 4, new[] { "worddrop" }).Outcome,
    "MapDropItem worddrop -> ExecutedWithGmMessage");
Equal(NativeWorldAdminOutcome.RejectedSilently,
    NativeGmWorldAdminCommands.Evaluate("MapDropItem", 4, new[] { "load" }).Outcome,
    "MapDropItem load without map -> RejectedSilently");
Equal(NativeWorldAdminOutcome.RejectedSilently,
    NativeGmWorldAdminCommands.Evaluate("MapDropItem", 4, new[] { "bogus" }).Outcome,
    "MapDropItem unknown op -> RejectedSilently");

// ---------------------------------------------------------------------------
// 7) SetMapState (case 343) branch ladder
// ---------------------------------------------------------------------------
Equal(NativeWorldAdminOutcome.RejectedSilently,
    NativeGmWorldAdminCommands.Evaluate("SetMapState", 4, new[] { "" }).Outcome,
    "SetMapState empty map -> RejectedSilently");
var fight = NativeGmWorldAdminCommands.Evaluate("SetMapState", 4, new[] { "0", "fight" });
Equal(NativeWorldAdminOutcome.ExecutedWithGmMessage, fight.Outcome, "SetMapState fight -> ExecutedWithGmMessage");
Equal("fight", fight.Branch, "SetMapState fight branch");
Equal(0xFFDB, fight.NativeSysMsgIdent, "SetMapState fight SysMsg ident");
Equal("safe", NativeGmWorldAdminCommands.Evaluate("SetMapState", 4, new[] { "0", "Safe" }).Branch, "SetMapState Safe branch");
Equal("normal", NativeGmWorldAdminCommands.Evaluate("SetMapState", 4, new[] { "0", "Normal" }).Branch, "SetMapState Normal branch");
Equal(NativeWorldAdminOutcome.RejectedSilently,
    NativeGmWorldAdminCommands.Evaluate("SetMapState", 4, new[] { "0", "chaos" }).Outcome,
    "SetMapState unknown state -> RejectedSilently");

// ---------------------------------------------------------------------------
// 8) SetSysTime (case 268) gated by the world time-lock flag byte_7DC270
// ---------------------------------------------------------------------------
NativeGmWorldAdminCommands.WorldTimeLocked = false;
var setTime = NativeGmWorldAdminCommands.Evaluate("SetSysTime", 5, new[] { "2026/07/31", "12:00:00" });
Equal(NativeWorldAdminOutcome.ExecutedWithGmMessage, setTime.Outcome, "SetSysTime unlocked -> ExecutedWithGmMessage");
Equal(0xFFDB, setTime.NativeSysMsgIdent, "SetSysTime unlocked SysMsg ident");

NativeGmWorldAdminCommands.WorldTimeLocked = true;
var lockedTime = NativeGmWorldAdminCommands.Evaluate("SetSysTime", 5, new[] { "2026/07/31", "12:00:00" });
Equal(NativeWorldAdminOutcome.RejectedWithGmMessage, lockedTime.Outcome, "SetSysTime locked -> RejectedWithGmMessage");
Equal("time-locked", lockedTime.Branch, "SetSysTime locked branch");
Equal(0x38FF, lockedTime.NativeSysMsgIdent, "SetSysTime locked usage SysMsg ident");
NativeGmWorldAdminCommands.WorldTimeLocked = false;

// ---------------------------------------------------------------------------
// 9) MonClear (case 181): empty map == current map (always), named map may miss
// ---------------------------------------------------------------------------
NativeGmWorldAdminCommands.MapExistsHook = _ => false;
Equal(NativeWorldAdminOutcome.ExecutedWithGmMessage,
    NativeGmWorldAdminCommands.Evaluate("MonClear", 4, new[] { "", "0" }).Outcome,
    "MonClear empty map -> current map -> ExecutedWithGmMessage");
Equal(NativeWorldAdminOutcome.RejectedSilently,
    NativeGmWorldAdminCommands.Evaluate("MonClear", 4, new[] { "Ghost", "0" }).Outcome,
    "MonClear missing named map -> RejectedSilently");
NativeGmWorldAdminCommands.MapExistsHook = _ => true;
var clear = NativeGmWorldAdminCommands.Evaluate("MonClear", 4, new[] { "0", "1" });
Equal(NativeWorldAdminOutcome.ExecutedWithGmMessage, clear.Outcome, "MonClear existing map -> ExecutedWithGmMessage");
Equal("sub_779DF4", clear.NativeHelper, "MonClear helper");
Equal(0xFFDB, clear.NativeSysMsgIdent, "MonClear report SysMsg ident");

// ---------------------------------------------------------------------------
// 10) kickOutBlackRoom (case 490): needs an existing map with black-room flag set
// ---------------------------------------------------------------------------
NativeGmWorldAdminCommands.MapExistsHook = _ => false;
NativeGmWorldAdminCommands.MapIsBlackRoomHook = _ => false;
Equal(NativeWorldAdminOutcome.RejectedSilently,
    NativeGmWorldAdminCommands.Evaluate("kickOutBlackRoom", 4, new[] { "BlackRoom" }).Outcome,
    "kickOutBlackRoom missing map -> RejectedSilently");
NativeGmWorldAdminCommands.MapExistsHook = _ => true;
NativeGmWorldAdminCommands.MapIsBlackRoomHook = _ => false;
Equal("not-blackroom",
    NativeGmWorldAdminCommands.Evaluate("kickOutBlackRoom", 4, new[] { "NormalMap" }).Branch,
    "kickOutBlackRoom on non-blackroom map -> not-blackroom");
NativeGmWorldAdminCommands.MapIsBlackRoomHook = _ => true;
var kob = NativeGmWorldAdminCommands.Evaluate("kickOutBlackRoom", 4, new[] { "BlackRoom" });
Equal(NativeWorldAdminOutcome.Executed, kob.Outcome, "kickOutBlackRoom on blackroom map -> Executed");
Equal("sub_77BF14", kob.NativeHelper, "kickOutBlackRoom helper");
NativeGmWorldAdminCommands.MapExistsHook = null;
NativeGmWorldAdminCommands.MapIsBlackRoomHook = null;

Console.WriteLine($"PASS NativeGmWorldAdminCommandsCheck ({checks} checks): "
    + $"{NativeGmWorldAdminCommands.All.Count} world/other-entity GM commands modeled "
    + "(registry facts, permission ladder, branch ladders, silent no-ops).");
return 0;
