using GameSvr;
using SystemModule;

// Static + policy audit for the 2026-08-04 revive (复活) subsystem port.
//
// Every assertion is re-based on the 战神 CONTRACT (byte-verified over
// M2Server_reunpacked_20260803, CODE 0x401000..0x7A10D0), not on the C# it guards, so a
// regression in either direction fails.  Each one was mutation-checked: the fix was
// reverted / perturbed and the named assertion observed to FAIL (results recorded in
// staging/revive_fix_20260804.md).
//
// Contract asserted: sub_7436F8 = the revive handler.  Ownership is byte-established:
// it is VMT slot +0x08 in all ten classes that reference it (THumanKind@0x73BC34,
// TPlayer@0x6AC8C8, THeroAct@0x685630, TGdMsgGMAgent@0x62EF8C, TWarHero@0x685968,
// TTaosHero@0x685CA0, TMagHero@0x685FD8, TSecWarHero@0x5F55A8, TSecTaosHero@0x5F58E4,
// TSecMagHero@0x5F5C24) and NO class overrides it; it has zero E8 rel32 direct callers.
// The VMT base convention (classname ptr at base-0x2C, instance size at base-0x28) was
// validated first against the sibling-known +0x84 Die split and the +0x21C empty-leaf
// split that NativeDeathDropPolicy documents.
//
//   743726  cmp byte [Envir+0x72],0 / jne 0x7439BC   ; NoRelive      -> return FALSE
//   743730  cmp byte [Envir+0x7F],0 / jne 0x74390B   ; NOEQUIPRELIVE -> TAIL, NOT a return
//   74373A  cmp byte [self+0x1B8],0  / je  0x7437C9  ; no equip revive -> PATH 2
//   743747  test [self+0x450],eax / je 0x74375E      ; a zero stamp always passes
//   743756  cmp edx,0xEA60 / jb 0x7437C9             ; 60000 ms; NOT elapsed -> PATH 2
//   7437CB  call sub_746084 / je 0x74390B            ; ineligible -> TAIL
//   7437DC  call sub_772960 (dl=0x30) / jne 0x743893 ; state 48 live -> CD msg, NO revive
//   743834  [vmt+0x1A8](dl=0x30, ecx=sub_74609C())   ; arm the tiered CD
//   74390F  [vmt+0x1A8](dl=0x37, ecx=2, push 1)      ; state 55, 2 SECONDS, value 1
//   743921  test bl,bl / jne                          ; AUTORELIVE only if nothing revived
//   743952  cmp byte [Envir+0x7D],0 / je              ; RELIVEBACK
//   743958  cmp byte [self+0x178],0 / jne             ; race must be 0 (RC_PLAYOBJECT)
//
// UNITS (the load-bearing fact).  The CD numbers are SECONDS, proven through the chain:
// [vmt+0x1A8] = sub_76B478 for EVERY class in the hierarchy, and its body does
// `imul ecx, eax, 0x3E8` @0x76B48C before forwarding to [vmt+0x1EC] = sub_7730D0, which
// stores the product in the state record's +0x02 field.  Both readers divide by 1000 again
// for display (sub_7436F8 @0x7438B6, sub_73C208 @0x73C5D9), which would render 0 for every
// tier if the field held raw seconds.
//
// The five-tier table sub_74609C, verbatim:
//   74609C mov al,[eax+0x1DD] ; 7460A2/A6/AA/AE dec al + je ; 7460B2 jmp default
//   7460B4 mov eax,0x96 (150) | 7460BA 0x78 (120) | 7460C0 0x5A (90)
//   7460C6 0x3C (60)          | 7460CC 0x12C (300, the DEFAULT arm)
// Tier 0 reaches the default arm because `dec al` on 0 yields 0xFF, matching no `je`.

var failures = new List<string>();
void Check(bool cond, string msg)
{
    if (cond) { Console.WriteLine("  PASS  " + msg); return; }
    failures.Add(msg);
    Console.WriteLine("  FAIL  " + msg);
}

Console.WriteLine("== A: sub_74609C — the five-tier cooldown table, in SECONDS ==");

// Values AND units.  A port that stored milliseconds, or that shifted the tiers by one,
// fails here.
Check(CooldownSeconds(1) == 150, "0x7460B4 mov eax,0x96: tier 1 => 150 s");
Check(CooldownSeconds(2) == 120, "0x7460BA mov eax,0x78: tier 2 => 120 s");
Check(CooldownSeconds(3) == 90, "0x7460C0 mov eax,0x5A: tier 3 => 90 s");
Check(CooldownSeconds(4) == 60, "0x7460C6 mov eax,0x3C: tier 4 => 60 s");
// The default arm is reached by tier 0 AND by every value above 4 — 0x7460B2 is an
// unconditional jmp, so there is no wrap-around and no clamp into the 1..4 band.
Check(CooldownSeconds(0) == 300,
    "0x7460B2 jmp 0x7460CC: tier 0 falls to the DEFAULT 300 s (dec al on 0 = 0xFF, no je hits)");
Check(CooldownSeconds(5) == 300, "0x7460CC mov eax,0x12C: tier 5 => the default 300 s");
Check(CooldownSeconds(255) == 300, "0x7460CC: tier 255 => the default 300 s, no wrap");
// Monotonicity is a property of the native table, not an assumption: 150 > 120 > 90 > 60.
// A transposed pair (e.g. tiers 1 and 4 swapped) would still pass a naive "all five
// present" test, so assert the ORDER too.
Check(CooldownSeconds(1) > CooldownSeconds(2) &&
      CooldownSeconds(2) > CooldownSeconds(3) &&
      CooldownSeconds(3) > CooldownSeconds(4),
    "0x7460B4/BA/C0/C6: tiers 1..4 are strictly DECREASING (150>120>90>60)");
// And the default must be the LARGEST, not merely different — the unmodelled-tier case
// must be the most punishing, never the cheapest.
Check(CooldownSeconds(0) > CooldownSeconds(1),
    "0x7460CC: the default arm (300 s) exceeds every explicit tier — fail-closed by value");

Console.WriteLine("== B: the post-revive invulnerability window ==");

// 0x743911 `mov cx,2` is the DURATION (sub_76B478 @0x76B48C multiplies ecx by 1000);
// 0x74390F `push 1` is the VALUE (sub_7730D0 @0x77310C-0x77310F stores [ebp+8] into the
// record's byte 0).  The discovery doc had these swapped, so assert both separately.
Check(PostReviveSeconds() == 2, "0x743911 mov cx,2: the window is 2 SECONDS");
Check(PostReviveValue() == 1, "0x74390F push 1: the state's VALUE is 1, not 2");
Check(PostReviveState() == 0x37, "0x743915 mov dl,0x37: the window is state 55");
Check(SecondPathState() == 0x30, "0x7437D8 mov dl,0x30: the second path's CD is state 48");
// Both state numbers must be inside sub_772960's addressable band (`cmp dl,0x6F` = 111),
// or HasNativeActiveState would silently answer false forever.
Check(PostReviveState() <= TBaseObject.NativeActiveStateMax &&
      SecondPathState() <= TBaseObject.NativeActiveStateMax,
    "sub_772960 @0x772960 cmp dl,0x6F: states 48 and 55 are within the 111 bound");
Check(TBaseObject.NativeActiveStateMax == 111,
    "sub_772960 @0x772960 cmp dl,0x6F: the state bound is 111");

// The window is granted for BOTH item paths and for neither non-revive leaf.
Check(GrantsWindow("EquipRevive"), "0x74390D test bl,bl: the equip path grants the window");
Check(GrantsWindow("SecondPathRevive"),
    "0x74390D test bl,bl: the second path grants the window");
Check(!GrantsWindow("NoRevive"), "0x74390D je 0x743921: no revive => no window");
// The cooldown leaf is the dangerous one: it DOES reach the tail, but with bl still 0, so
// it must NOT grant invulnerability.  A port that treated "eligible" as "revived" would
// hand out a free 2 s immunity on every tick while the CD ran.
Check(!GrantsWindow("SecondPathOnCooldown"),
    "0x7437E3 jne 0x743893 leaves bl=0: the CD-message leaf grants NO window");

Console.WriteLine("== C: the equipment-revive cooldown is a hard-coded 60 000 ms ==");

Check(EquipCooldownMs() == 0xEA60,
    "0x743756 cmp edx,0xEA60: the equip-revive CD is 60000 ms, hard-coded not config-read");

// 0x743747 `test eax,eax` / `je 0x74375E` — an unstamped creature revives immediately.
Check(Resolve(new TMapFlag(), equip: true, lastTick: 0, tick: 0) == "EquipRevive",
    "0x74374F je 0x74375E: a zero last-revive stamp always passes the CD gate");
// The comparison is `jb` on an UNSIGNED difference: exactly 60000 passes, 59999 does not.
Check(Resolve(new TMapFlag(), equip: true, lastTick: 1000, tick: 1000 + 60000)
        == "EquipRevive",
    "0x74375C jb: a difference of exactly 60000 is NOT below the bound, so it passes");
Check(Resolve(new TMapFlag(), equip: true, lastTick: 1000, tick: 1000 + 59999)
        == "NoRevive",
    "0x74375C jb 0x7437C9: 59999 ms is still on cooldown => falls through to PATH 2");
// The fall-through is the subtle part: a cooled-down path 1 does NOT return, it continues
// into path 2 (which is itself blocked, hence NoRevive).  A port that returned early would
// pass the line above for the wrong reason, so also prove path 2 is what it reached: with
// path 2's cooldown state already live the answer must become the CD-message leaf.
Check(Resolve(new TMapFlag(), equip: true, lastTick: 1000, tick: 1000 + 59999,
        secondFlag: true, cooldownActive: true) == "SecondPathOnCooldown",
    "0x74375C jb 0x7437C9: an on-cooldown PATH 1 falls THROUGH into PATH 2, not out");

Console.WriteLine("== D: the four revive map gates ==");

// Parsed at all — these five tokens were absent from Maps.cs entirely.
Check(ParseToken("NoRelive")?.boNoRelive == true,
    "sub_774D98 @0x775A1A token / @0x775A28 mov byte [ebx+0x72],1: NoRelive parsed");
Check(ParseToken("RELIVEBACK")?.boRELIVEBACK == true,
    "sub_774D98 @0x77552A token / @0x775538 mov byte [ebx+0x7D],1: RELIVEBACK parsed");
Check(ParseToken("AUTORELIVE")?.boAUTORELIVE == true,
    "sub_774D98 @0x775686 token / @0x775694 mov byte [ebx+0x7E],1: AUTORELIVE parsed");
Check(ParseToken("NOEQUIPRELIVE")?.boNOEQUIPRELIVE == true,
    "sub_774D98 @0x7756BA token / @0x7756C8 mov byte [ebx+0x7F],1: NOEQUIPRELIVE parsed");
// sub_4C6E94 is a case-folding comparator and native's own literal is mixed-case
// ("NoRelive"), so the parse must not be case-sensitive.
Check(ParseToken("norelive")?.boNoRelive == true,
    "sub_4C6E94 is case-insensitive: lower-case NoRelive still parses");
// Each token must set ONLY its own field — the four offsets 0x72/0x7D/0x7E/0x7F are
// distinct bytes, and a copy-paste slip would alias two gates together.
var onlyNoRelive = ParseToken("NoRelive");
Check(onlyNoRelive != null && onlyNoRelive.boNoRelive &&
      !onlyNoRelive.boRELIVEBACK && !onlyNoRelive.boAUTORELIVE &&
      !onlyNoRelive.boNOEQUIPRELIVE,
    "0x72 vs 0x7D/0x7E/0x7F are distinct bytes: NoRelive sets no other gate");
var onlyNoEquip = ParseToken("NOEQUIPRELIVE");
Check(onlyNoEquip != null && onlyNoEquip.boNOEQUIPRELIVE && !onlyNoEquip.boNoRelive,
    "0x7F vs 0x72: NOEQUIPRELIVE does not set NoRelive");

// Behaviour.  NoRelive kills the whole handler even for an otherwise-valid equip revive.
Check(Resolve(new TMapFlag { boNoRelive = true }, equip: true, lastTick: 0, tick: 0)
        == "NoRevive",
    "0x74372A jne 0x7439BC: NoRelive returns FALSE even with a ready equip revive");
// NoRelive also outranks path 2, because 0x743726 is the FIRST gate.
Check(Resolve(new TMapFlag { boNoRelive = true }, equip: false, lastTick: 0, tick: 0,
        secondFlag: true) == "NoRevive",
    "0x743726 precedes 0x7437CB: NoRelive outranks PATH 2 eligibility");
// NOEQUIPRELIVE suppresses BOTH item paths...
Check(Resolve(new TMapFlag { boNOEQUIPRELIVE = true }, equip: true, lastTick: 0, tick: 0)
        == "NoRevive",
    "0x743734 jne 0x74390B: NOEQUIPRELIVE suppresses the equipment path");
Check(Resolve(new TMapFlag { boNOEQUIPRELIVE = true }, equip: false, lastTick: 0, tick: 0,
        secondFlag: true) == "NoRevive",
    "0x743734 jne 0x74390B: NOEQUIPRELIVE also skips PATH 2 (it jumps past both)");
// ...and NoRelive is tested BEFORE NOEQUIPRELIVE, so the pair cannot be reordered.
Check(Resolve(new TMapFlag { boNoRelive = true, boNOEQUIPRELIVE = true },
        equip: true, lastTick: 0, tick: 0) == "NoRevive",
    "0x743726 precedes 0x743730: NoRelive is the first gate");

Console.WriteLine("== E: PATH 2 eligibility (sub_746084) and its fail-closed state ==");

// 746084 cmp byte [eax+0x1D1],0 / jne -> TRUE ; 74608D cmp byte [eax+0x1DD],0 / ja -> TRUE
Check(!Eligible(false, 0), "0x746096 xor eax,eax: neither flag nor tier => FALSE");
Check(Eligible(true, 0), "0x74608B jne 0x746099: the [+0x1D1] flag alone => TRUE");
Check(Eligible(false, 1), "0x746094 ja 0x746099: a tier of 1 alone => TRUE");
// `ja` is an UNSIGNED above, so any non-zero tier qualifies, including values past 4.
Check(Eligible(false, 255), "0x746094 ja: tier 255 is still > 0 => TRUE");

// An eligible creature with a live state 48 gets the message leaf, NOT a revive.  This is
// the anti-exploit assertion: it must be impossible to re-revive while the CD runs.
Check(Resolve(new TMapFlag(), equip: false, lastTick: 0, tick: 0, secondFlag: true,
        cooldownActive: true) == "SecondPathOnCooldown",
    "0x7437E3 jne 0x743893: state 48 live => CD message, NO revive");
Check(Resolve(new TMapFlag(), equip: false, lastTick: 0, tick: 0, secondFlag: true,
        cooldownActive: false) == "SecondPathRevive",
    "0x7437E9 fallthrough: eligible with no live CD => the second path revives");
// The equipment aggregate now supplies both path-2 fields. Keep the test pinned to
// the instance fields rather than the old fail-closed constants.
var reviveSource = ReadRepoFile("GameSvr/Actors/TBaseObject.NativeRevive.cs");
Check(reviveSource.Contains("m_btNativeSecondPathFlag != 0", StringComparison.Ordinal) &&
      reviveSource.Contains("m_btNativeSecondPathTier", StringComparison.Ordinal),
    "[self+0x1D1]/[self+0x1DD] are read from the rebuilt equipment aggregate");
var agg2Source = ReadRepoFile("GameSvr/Actors/NativeEquipAgg2Revive.cs");
Check(agg2Source.Contains("0x73D63D", StringComparison.Ordinal) &&
      agg2Source.Contains("0x76235F", StringComparison.Ordinal) &&
      agg2Source.Contains("0x7627CF", StringComparison.Ordinal),
    "the aggregate rebuild cites the block copy, flag writer, and tier writer");

Console.WriteLine("== F: the TAIL — AUTORELIVE and RELIVEBACK ==");

// The seconds->ms adapter must exist and must be a *1000, not a raw pass-through.
Check(reviveSource.Contains("truncated * 1000", StringComparison.Ordinal),
    "0x76B48C imul ecx,eax,0x3E8: the C# adapter multiplies SECONDS by 1000");
// 0x76B489 movzx eax,di — the duration is truncated to 16 bits before the multiply.
Check(reviveSource.Contains("(ushort)seconds", StringComparison.Ordinal),
    "0x76B489 movzx eax,di: the duration is truncated to 16 bits first");
// 0x743921 `test bl,bl` / `jne` — AUTORELIVE must be gated on NOT having revived.
Check(reviveSource.Contains("!revived && flag != null && flag.boAUTORELIVE",
        StringComparison.Ordinal),
    "0x743921 test bl,bl / jne 0x743948: AUTORELIVE runs only when nothing revived yet");
// 0x743958 `cmp byte [esi+0x178],0` / `jne` — RELIVEBACK is players-only.
Check(reviveSource.Contains("m_btRaceServer == Grobal2.RC_PLAYOBJECT", StringComparison.Ordinal),
    "0x743958 cmp byte [esi+0x178],0 / jne: RELIVEBACK is restricted to race 0");
// 0x743961 `mov eax,5` + 0x743971 `sub edx,2`: a -2..+2 jitter on each axis.
Check(ReliveJitterSpan() == 5 && ReliveJitterBias() == 2,
    "0x743961 mov eax,5 / 0x743971 sub edx,2: RELIVEBACK jitter is Random(5) - 2");
// The relocation must be gated on a SUCCESSFUL revive (0x74394A je 0x7439BC), otherwise a
// dead player would be teleported.
Check(reviveSource.Contains("revived && flag != null && flag.boRELIVEBACK",
        StringComparison.Ordinal),
    "0x743948 test bl,bl / je 0x7439BC: RELIVEBACK only fires after a successful revive");

Console.WriteLine("== G: the tick actually calls the ladder ==");

// The old 6-line stub inlined its own CD check in the tick.  Assert that the tick now
// delegates and that the stub's config-driven bound is gone from that block, because the
// native bound is a hard-coded 60000 and not g_Config.dwRevivalTime.
var tickSource = ReadRepoFile("GameSvr/Actors/TBaseObject.Base.cs");
Check(tickSource.Contains("TryNativeRevive();", StringComparison.Ordinal),
    "the HP==0 tick branch delegates to the sub_7436F8 port");
Check(!tickSource.Contains("M2Share.g_Config.dwRevivalTime", StringComparison.Ordinal),
    "0x743756 is an immediate 0xEA60: the config-driven revival bound is gone from the tick");
// Die() must still follow, and still be conditional on HP having stayed 0.
// The 200-character window used to be measured on the raw text, so the
// byte-evidence comment block that now sits between the two calls (眼神
// @MyKill 的桩体说明) pushed Die() out of range. Strip comments and
// whitespace first so the window measures code, not prose.
var tickCode = System.Text.RegularExpressions.Regex.Replace(
    tickSource, @"//[^\n]*", string.Empty);
tickCode = string.Concat(tickCode.Where(value => !char.IsWhiteSpace(value)));
var reviveCall = tickCode.IndexOf("TryNativeRevive();", StringComparison.Ordinal);
var afterRevive = reviveCall > 0 ? tickCode.Substring(reviveCall) : "";
var dieIdx = afterRevive.IndexOf("Die();", StringComparison.Ordinal);
Check(dieIdx > 0 && dieIdx < 200 &&
      afterRevive.StartsWith("TryNativeRevive();if(m_WAbil.HP==0){",
          StringComparison.Ordinal),
    "the revive attempt precedes Die(), which stays gated on HP still being 0");

Console.WriteLine("== H: the two scheduled-message idents (were mislabelled as a colour) ==");

// sub_766060 @0x766069 `mov word [ebp-6],cx` then @0x76608E `mov word [ebx],ax` proves cx
// is the queued record's IDENT field, not a SysMsg colour.  0x27B1 is the DELAYED REVIVE
// (5 send sites incl. GM @Relive @0x625A43 and PAS dorelive @0x6E13E9; exactly one dispatch
// site, `cmp eax,0x27B1` @0x766AA6) and 0x27B0 the immediate NOTICE (@0x6E1403).
Check(NativeGmPlayerAdminCommands.DelayedReviveIdent == 0x27B1,
    "0x625A43 / 0x6E13E9 mov cx,0x27B1: the delayed-revive ident is 10161");
Check(NativeGmPlayerAdminCommands.ImmediateNoticeIdent == 0x27B0,
    "0x6E1403 mov cx,0x27B0: the immediate-notice ident is 10160");
// The old name asserted 0x27B1 was a colour.  Guard against it coming back, and against
// the two idents being conflated (the pair is one apart, so a copy-paste swaps easily).
Check(NativeGmPlayerAdminCommands.DelayedReviveIdent !=
      NativeGmPlayerAdminCommands.ImmediateNoticeIdent,
    "0x27B1 (revive) and 0x27B0 (notice) are DISTINCT idents, not interchangeable");
var gmSource = ReadRepoFile("GameSvr/Services/NativeGmPlayerAdminCommands.cs");
// The mislabel is gone as a DECLARATION.  It legitimately still appears in the explanatory
// comment that records the correction, so assert on the declaration form, not the bare word.
Check(!gmSource.Contains("ColorReliveNotice =", StringComparison.Ordinal),
    "sub_766060 @0x76608E stores cx as an ident: the 'ColorReliveNotice' constant is gone");
// 0x27B1 must NOT be grouped with the real SysMsg colours, which are the cx immediates of
// the message helpers (0xFFDB / 0x38FF / 0xFCFF).
Check(NativeGmPlayerAdminCommands.ColorConfirm == 0xFFDB &&
      NativeGmPlayerAdminCommands.ColorRed == 0x38FF &&
      NativeGmPlayerAdminCommands.ColorSetPkEmpty == 0xFCFF,
    "the genuine SysMsg colours 0xFFDB / 0x38FF / 0xFCFF are unchanged");

// dorelive stays fail-closed, and the reason must be recorded at the call site: native
// ident 10160 collides with C#'s pre-existing RM_USERSAVEITEM.
var pasSource = ReadRepoFile("GameSvr/ScriptSystem/PasEngine/PasApiBridge.cs");
var doreliveIdx = pasSource.IndexOf("case \"dorelive\":", StringComparison.Ordinal);
Check(doreliveIdx > 0, "the dorelive PAS case is present");
// Bound the block by the NEXT `case "` rather than a fixed character count: the comment
// body is ~1.4 KB and a fixed window silently truncated past the `return`, which made these
// assertions pass/fail for the wrong reason.
var doreliveEnd = pasSource.IndexOf("case \"", doreliveIdx + 16, StringComparison.Ordinal);
var doreliveBlock = doreliveIdx > 0
    ? pasSource[doreliveIdx..(doreliveEnd > doreliveIdx ? doreliveEnd : pasSource.Length)]
    : "";
Check(doreliveBlock.Contains("RejectUnsupportedNativeApi()", StringComparison.Ordinal),
    "dorelive stays fail-closed while the 10160 ident collision is unresolved");
Check(doreliveBlock.Contains("sub_6E13C8", StringComparison.Ordinal) &&
      doreliveBlock.Contains("0x766FB4", StringComparison.Ordinal) &&
      doreliveBlock.Contains("RM_USERSAVEITEM", StringComparison.Ordinal),
    "the dorelive rejection cites sub_6E13C8, the 0x766FB4 handler, and the collision");
// The collision is real, not hypothetical: assert C# still maps 10160 elsewhere, so the
// day that changes this guard fires and dorelive can be revisited.
Check(SystemModule.Grobal2.RM_USERSAVEITEM == 10160,
    "RM_USERSAVEITEM still occupies ident 10160 (the blocker for dorelive)");

Console.WriteLine("== I: item+0x104 is an instance bitmap rebuilt by equipped-item recalc ==");

Check(ComputeClass104(new GoodItem { StdMode = 22, Shape = 114 }) == 0x01,
    "TRing Shape 114 sets bit0");
Check(ComputeClass104(new GoodItem { StdMode = 24, Shape = 114 }) == 0x01,
    "TArmRing Shape 114 sets bit0");
Check(ComputeClass104(new GoodItem { StdMode = 30, Shape = 201 }) == 0x02,
    "TRWeapon Shape 201 sets bit1");
Check(ComputeClass104(new GoodItem { StdMode = 22, Shape = 137 }) == 0x02,
    "TRing Shape 137 sets bit1");
Check(ComputeClass104(new GoodItem { StdMode = 24, Shape = 210 }) == 0x02,
    "TArmRing Shape 210 sets bit1");
Check(ComputeClass104(new GoodItem { StdMode = 10, Shape = 39, Mac = 1 }) == 0x02 &&
      ComputeClass104(new GoodItem { StdMode = 11, Shape = 41, Mac = 1 }) == 0x02,
    "TClothes descendants Shape 39..41 with Mac 1 set bit1");
Check(ComputeClass104(new GoodItem { StdMode = 10, Shape = 39, Mac = 0 }) == 0,
    "the clothes writer requires Mac exactly 1");

var ext45 = ItemWithExtension(0x45, 0);
var extFe = ItemWithExtension(0xFE, 0x0102);
var oldFalseIds = ItemWithExtension(0x3E, 1);
oldFalseIds.NativeItemExtAbilIdents[1] = 0x50;
oldFalseIds.NativeItemExtAbilValues[1] = 1;
Check(ComputeClass104(ext45) == 0x04, "extension ident 0x45 sets bit2");
Check(ComputeClass104(extFe) == 0x02,
    "extension ident 0xFE uses the low value byte and sets bit1 for subtype 2");
Check(ComputeClass104(oldFalseIds) == 0,
    "0x3E and 0x50 are Shape-branch constants, not extension idents");
Check(ComputeClass104(new GoodItem
      { StdMode = 22, Shape = 114, NativeItemExtAbilParsed = false }) == 0x01,
    "the synthetic extension parse flag cannot suppress class/Shape writers");

var runtimeItem = new TUserItem { NativeClass104 = 0x01 };
Check(MatchesClass104(runtimeItem, 0) && !MatchesClass104(runtimeItem, 1),
    "revive mode 0 reads instance bit0 only");
runtimeItem.NativeClass104 = 0x06;
Check(!MatchesClass104(runtimeItem, 0) && MatchesClass104(runtimeItem, 1),
    "revive mode 1 reads instance bit1|bit2 only");

runtimeItem.NativeItemPlus100 = 0xA0;
runtimeItem.NativeItemPlus101 = 0xA1;
runtimeItem.NativeItemPlus102 = 0xA2;
runtimeItem.NativeItemPlus103 = 0xA3;
runtimeItem.NativeClass104 = 0xFF;
RefreshClass104(runtimeItem, new GoodItem { StdMode = 30, Shape = 201 });
Check(runtimeItem.NativeItemPlus100 == 0xA0 && runtimeItem.NativeItemPlus101 == 0xA1,
    "equipped refresh preserves item+0x100/+0x101");
Check(runtimeItem.NativeItemPlus102 == 0 && runtimeItem.NativeItemPlus103 == 0,
    "0x75EE12 clears the complete word at item+0x102");
Check(runtimeItem.NativeClass104 == 0x02,
    "0x75EE20 clears +0x104 before rebuilding, rather than accumulating, its bits");
var runtimeCopy = new TUserItem(runtimeItem);
Check(runtimeCopy.NativeItemPlus100 == 0xA0 && runtimeCopy.NativeItemPlus101 == 0xA1 &&
      runtimeCopy.NativeItemPlus102 == 0 && runtimeCopy.NativeItemPlus103 == 0 &&
      runtimeCopy.NativeClass104 == 0x02,
    "the item copy constructor preserves all runtime bytes +0x100..+0x104");

var class104Source = ReadRepoFile("GameSvr/Items/NativeItemClass104.cs");
var clear102 = class104Source.IndexOf("item.NativeItemPlus102 = 0;", StringComparison.Ordinal);
var clear103 = class104Source.IndexOf("item.NativeItemPlus103 = 0;", StringComparison.Ordinal);
var clear104 = class104Source.IndexOf("item.NativeClass104 = 0;", StringComparison.Ordinal);
var rebuild104 = class104Source.IndexOf(
    "item.NativeClass104 = ComputeClass104Bits(stdItem);", StringComparison.Ordinal);
Check(clear102 >= 0 && clear102 < clear103 && clear103 < clear104 && clear104 < rebuild104,
    "managed refresh preserves the native clear-word, clear-byte, rebuild order");

var codecBase = new TUserItem
{
    MakeIndex = 0x10203040,
    wIndex = 30,
    Dura = 100,
    DuraMax = 200,
    ys1 = 7
};
var codecRuntimeVariant = new TUserItem(codecBase)
{
    NativeItemPlus100 = 0x11,
    NativeItemPlus101 = 0x22,
    NativeItemPlus102 = 0x33,
    NativeItemPlus103 = 0x44,
    NativeClass104 = 0x55
};
Check(codecBase.GetBuffer().SequenceEqual(codecRuntimeVariant.GetBuffer()),
    "the 147-byte manual item packet excludes +0x100..+0x104 runtime state");
Check(SerializeProtobuf(codecBase).SequenceEqual(SerializeProtobuf(codecRuntimeVariant)),
    "protobuf excludes +0x100..+0x104 runtime state");
Check(EncodeEyeSidecar(codecBase).SequenceEqual(EncodeEyeSidecar(codecRuntimeVariant)),
    "the YS27 sidecar excludes +0x100..+0x104 runtime state");

codecBase.ys1 = 0;
codecRuntimeVariant.ys1 = 0;
Check(EncodeLegacy208(codecBase) == EncodeLegacy208(codecRuntimeVariant),
    "the native 208-byte record excludes +0x100..+0x104 runtime state");

var recalcSource = ReadRepoFile("GameSvr/Actors/TBaseObject.Base.cs");
var duraGate = recalcSource.IndexOf(
    "(m_UseItems[i].wIndex <= 0) || (m_UseItems[i].Dura <= 0)",
    StringComparison.Ordinal);
var refreshCall = recalcSource.IndexOf(
    "NativeItemClass104.RefreshEquippedInstance(m_UseItems[i], StdItem)",
    StringComparison.Ordinal);
Check(duraGate >= 0 && refreshCall > duraGate,
    "RecalcAbilitys refreshes +0x104 only after the positive-Dura equipped-item gate");
var reviveDuraSource = ReadRepoFile("GameSvr/Actors/TBaseObject.NativeReviveDurability.cs");
Check(reviveDuraSource.Contains(
        "MatchesReviveDurabilityTarget(userItem, mode)", StringComparison.Ordinal) &&
      !reviveDuraSource.Contains(
        "MatchesReviveDurabilityTarget(stdItem, mode)", StringComparison.Ordinal),
    "the revive debit reads the retained instance byte, not a template recomputation");

Console.WriteLine();
if (failures.Count > 0)
{
    Console.WriteLine($"FAILED: {failures.Count} assertion(s)");
    foreach (var f in failures) Console.WriteLine("  - " + f);
    return 1;
}
Console.WriteLine("ReviveSubsystemCheck: ALL PASS");
return 0;

// ---- reflection helpers: NativeRevivePolicy is internal to GameSvr ----

static Type PolicyType()
{
    return typeof(TBaseObject).Assembly.GetType("GameSvr.NativeRevivePolicy")
        ?? throw new MissingMemberException("GameSvr.NativeRevivePolicy");
}

static object Const(string name)
{
    var f = PolicyType().GetField(name,
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic |
        System.Reflection.BindingFlags.Public)
        ?? throw new MissingFieldException("NativeRevivePolicy." + name);
    return f.GetRawConstantValue();
}

static int EquipCooldownMs() => (int)Const("EquipReviveCooldownMilliseconds");
static byte PostReviveState() => (byte)Const("PostReviveStateType");
static int PostReviveSeconds() => (int)Const("PostReviveStateSeconds");
static int PostReviveValue() => (int)Const("PostReviveStateValue");
static byte SecondPathState() => (byte)Const("SecondPathCooldownStateType");
static int ReliveJitterSpan() => (int)Const("ReliveBackJitterSpan");
static int ReliveJitterBias() => (int)Const("ReliveBackJitterBias");

static int CooldownSeconds(byte tier)
{
    var m = PolicyType().GetMethod("GetCooldownSecondsForTier",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic |
        System.Reflection.BindingFlags.Public)
        ?? throw new MissingMethodException("GetCooldownSecondsForTier");
    return (int)m.Invoke(null, new object[] { tier });
}

static bool Eligible(bool flag, byte tier)
{
    var m = PolicyType().GetMethod("IsSecondPathEligible",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic |
        System.Reflection.BindingFlags.Public)
        ?? throw new MissingMethodException("IsSecondPathEligible");
    return (bool)m.Invoke(null, new object[] { flag, tier });
}

static bool GrantsWindow(string outcomeName)
{
    var t = PolicyType();
    var enumType = t.GetNestedType("Outcome",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
        ?? throw new MissingMemberException("NativeRevivePolicy.Outcome");
    var value = Enum.Parse(enumType, outcomeName);
    var m = t.GetMethod("GrantsPostReviveWindow",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic |
        System.Reflection.BindingFlags.Public)
        ?? throw new MissingMethodException("GrantsPostReviveWindow");
    return (bool)m.Invoke(null, new[] { value });
}

static string Resolve(TMapFlag flag, bool equip, int lastTick, int tick,
    bool secondFlag = false, byte tier = 0, bool cooldownActive = false)
{
    var policy = PolicyType();
    var m = policy.GetMethod("Resolve",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic |
        System.Reflection.BindingFlags.Public)
        ?? throw new MissingMethodException("NativeRevivePolicy.Resolve");
    // Resolve grew a trailing equipReviveCooldownMs parameter. Reflection does not apply
    // optional-parameter defaults, so a 7-argument Invoke threw
    // TargetParameterCountException and killed the run half way through. Pass the
    // product's own default (0xEA60 = 60000, the hard-coded CD pinned above at 0x743756)
    // so this stays the same case the assertions were written for.
    var cooldownField = policy.GetField("EquipReviveCooldownMilliseconds",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic |
        System.Reflection.BindingFlags.Public)
        ?? throw new MissingFieldException("NativeRevivePolicy.EquipReviveCooldownMilliseconds");
    return m.Invoke(null, new object[]
        { flag, equip, lastTick, tick, secondFlag, tier, cooldownActive,
          cooldownField.GetValue(null) }).ToString();
}

static TMapFlag ParseToken(string token)
{
    var flag = new TMapFlag();
    var m = typeof(Maps).GetMethod("TryApplySceneFlag",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic |
        System.Reflection.BindingFlags.Public)
        ?? throw new MissingMethodException("Maps.TryApplySceneFlag");
    return (bool)m.Invoke(null, new object[] { flag, token }) ? flag : null;
}

static Type Class104Type()
{
    return typeof(TBaseObject).Assembly.GetType("GameSvr.NativeItemClass104")
        ?? throw new MissingMemberException("GameSvr.NativeItemClass104");
}

static byte ComputeClass104(GoodItem item)
{
    var method = Class104Type().GetMethod("ComputeClass104Bits",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic |
        System.Reflection.BindingFlags.Public)
        ?? throw new MissingMethodException("NativeItemClass104.ComputeClass104Bits");
    return (byte)method.Invoke(null, new object[] { item });
}

static bool MatchesClass104(TUserItem item, int mode)
{
    var method = Class104Type().GetMethod("MatchesReviveDurabilityTarget",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic |
        System.Reflection.BindingFlags.Public)
        ?? throw new MissingMethodException(
            "NativeItemClass104.MatchesReviveDurabilityTarget");
    return (bool)method.Invoke(null, new object[] { item, mode });
}

static void RefreshClass104(TUserItem item, GoodItem stdItem)
{
    var method = Class104Type().GetMethod("RefreshEquippedInstance",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic |
        System.Reflection.BindingFlags.Public)
        ?? throw new MissingMethodException("NativeItemClass104.RefreshEquippedInstance");
    method.Invoke(null, new object[] { item, stdItem });
}

static byte[] SerializeProtobuf(TUserItem item)
{
    using var stream = new MemoryStream();
    ProtoBuf.Serializer.Serialize(stream, item);
    return stream.ToArray();
}

static byte[] EncodeEyeSidecar(TUserItem item)
{
    if (!YanshenItemSidecarCodec.TryEncode(new[] { item }, Array.Empty<TUserItem>(),
            Array.Empty<TUserItem>(), out var payload, out var error))
    {
        throw new InvalidOperationException("YS27 encode failed: " + error);
    }
    return payload;
}

static string EncodeLegacy208(TUserItem item)
{
    var type = typeof(TBaseObject).Assembly.GetType("GameSvr.LegacyUserItem208Codec")
        ?? throw new MissingMemberException("GameSvr.LegacyUserItem208Codec");
    var method = type.GetMethod("TryEncode",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic |
        System.Reflection.BindingFlags.Public)
        ?? throw new MissingMethodException("LegacyUserItem208Codec.TryEncode");
    var args = new object[] { item, null, null };
    if (!(bool)method.Invoke(null, args))
    {
        throw new InvalidOperationException("208-byte encode failed: " + args[2]);
    }
    return (string)args[1];
}

static GoodItem ItemWithExtension(ushort ident, ushort value)
{
    var item = new GoodItem();
    item.NativeItemExtAbilIdents[0] = ident;
    item.NativeItemExtAbilValues[0] = value;
    return item;
}

static string ReadRepoFile(string relative)
{
    var dir = AppContext.BaseDirectory;
    for (var i = 0; i < 12 && dir != null; i++)
    {
        var candidate = Path.Combine(dir, relative.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(candidate)) return File.ReadAllText(candidate);
        dir = Path.GetDirectoryName(dir);
    }
    throw new FileNotFoundException("could not locate " + relative + " above " + AppContext.BaseDirectory);
}
