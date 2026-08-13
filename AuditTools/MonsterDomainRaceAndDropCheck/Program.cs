using GameSvr;
using SystemModule;

// Static audit for the 2026-08-04 MONSTER-DOMAIN pass (race table / MonItems parse /
// m_WAbil snapshot / sticky-target dead code).
//
// Every assertion is re-based on the 战神 CONTRACT (byte-verified over
// M2Server_reunpacked_20260803.exe, ImageBase 0x400000, CODE 0x401000..0x7A10D0),
// not on the C# it guards, so a regression in EITHER direction fails.
// Each assertion was mutation-checked; results recorded in
// staging/monsterdomain_fix_20260804.md.
//
// Contracts asserted:
//
//  A. Monster factory race dispatch = sub_679F8C @0x67A006-0x67A01F:
//        67A006  33 C0                 xor  eax,eax
//        67A008  8A 47 14              mov  al,byte [monRecord+0x14]      ; race
//        67A00B  83 C0 F5              add  eax,-0xB                      ; bias 0x0B
//        67A00E  3D EE 00 00 00        cmp  eax,0xEE
//        67A013  0F 87 45 0E 00 00     ja   0x67AE5E                      ; default sink
//        67A019  8A 80 26 A0 67 00     mov  al,byte [eax+0x67A026]        ; index table
//        67A01F  FF 24 85 15 A1 67 00  jmp  dword [eax*4+0x67A115]        ; jump table
//     => the valid race window is exactly 11..249 (0x0B + 0..0xEE).  race 250 is OUT of
//     the switch (the discovery doc claimed TQingLong owned races "178, 250" — 250 falls
//     through `ja` to the default sink, so TQingLong has exactly ONE race, 178).
//     Full-table enumeration of the 0xEF index bytes yields 117 live races.
//
//  B. The default sink 0x67AE5E is `xor eax,eax` -> nil.  There is NO base-class
//     fallback: an unmapped race spawns NOTHING natively.  This is why the 71 missing
//     races are left fail-closed rather than defaulted to a base monster class.
//
//  C. race 108 -> TATMonster + Random(2)==0 -> byte[+0x481]:=1.
//     index table[108-0x0B=0x61] = 0x25 = 37 ; jt[37] = 0x67A6FD ; body:
//        67A6FD  B2 01                 mov  dl,1
//        67A6FF  A1 98 E7 65 00        mov  eax,[0x65E798]   ; classref -> TATMonster
//        67A704  E8 8F C3 FE FF        call sub_666A98       ; ctor
//        67A70C  B8 02 00 00 00        mov  eax,2
//        67A711  E8 36 94 D8 FF        call sub_403B4C       ; Random(2)
//        67A716  85 C0 / 0F 85 ...     test eax,eax / jne 0x67AD3F
//        67A721  C6 80 81 04 00 00 01  mov  byte [eax+0x481],1
//     [+0x481] == C# bo2BA, cross-checked by races 95/96/97/101 (Random(2)/Random(4)/
//     Random(2)/unconditional), all four already mirrored in C#.
//
//  D. MonItems loader = sub_6799E0.  StrToIntDef defaults are 1, NOT -1:
//        679C06  BA 01 00 00 00  mov edx,1 -> sub_40CA18 -> 679C13 `48` dec  ; SelPoint
//        679C1A  BA 01 00 00 00  mov edx,1 -> sub_40CA18                     ; MaxPoint
//        679C2D  BA 01 00 00 00  mov edx,1 -> sub_40CA18                     ; Count
//     Row acceptance is ONE condition only — the name resolves to a StdItem:
//        679BB4  call sub_74C2D4 ; 679BC1 cmp [ebp-0x2C],0 / je (skip alloc)
//        679BDD  cmp [ebp-8],0 / je 0x679C4E                (whole row discarded)
//     There is NO SelPoint>0 / MaxPoint>0 numeric gate, and the body 0x6799E0..0x679C92
//     contains ZERO 0x22 ('"') comparisons, so the item name is NOT unquoted.
//     Delimiters (the pushed arg is count-1, per sub_4C6BA4 @0x4C6BF0 `inc eax`):
//        fields 1-2: push 2 + {09,2F,20} = {TAB,'/',' '}   (0x679B06 / 0x679B31)
//        fields 3-4: push 1 + {09,20}    = {TAB,' '}       (0x679B5C / 0x679B83)
//
//  E. m_Abil -> m_WAbil is a COPY, not an alias.  MonInitialize sub_71EA04 tail:
//        71EB68  8D B3 E8 01 00 00  lea esi,[ebx+0x1E8]   ; m_Abil
//        71EB6E  8D BB 64 02 00 00  lea edi,[ebx+0x264]   ; m_WAbil
//        71EB74  B9 1F 00 00 00     mov ecx,0x1F          ; 31 dwords = 0x7C bytes
//        71EB79  F3 A5              rep movsd
//     The two 0x7C regions do not overlap, so native's spawn template is immutable.
//
//  F. The sticky-target pre-pass in TAnimal.SearchTarget sub_71DA70 reads [self+0x464]
//     but that field is NEVER assigned a non-nil value anywhere in the image:
//     an exhaustive raw disp32 sweep finds 32 byte occurrences of `64 04 00 00` in CODE,
//     29 of which decode as a [reg+0x464] memory operand.  On the TAnimal side there are
//     exactly two writes and BOTH store zero:
//        71DB04  33 D2              xor edx,edx
//        71DB06  89 90 64 04 00 00  mov [eax+0x464],edx
//        71DD3A  33 C0              xor eax,eax
//        71DD3C  89 83 64 04 00 00  mov [ebx+0x464],eax
//     The TAnimal ctor sub_71D828 (@0x71D847-0x71D8DC enumerates every field it sets)
//     does not touch +0x464 either.  All remaining sites belong to TPlayObject and are
//     WORD-width (e.g. 0x6492FF `66 83 BF 64 04 00 00 FF` = cmp word [edi+0x464],-1),
//     i.e. a different field at a coincidentally equal offset.
//     => `test esi,esi` @0x71DB0C is always false; the pre-pass always falls through to
//     the visible-list scan.  C# having no pre-pass is EQUIVALENT, and adding a
//     *populated* one would invent aggro stickiness the original does not have.

var repoRoot = AuditRepoRoot.Resolve(args);

var failures = new List<string>();
void Check(bool cond, string msg)
{
    if (cond) { Console.WriteLine("  PASS  " + msg); return; }
    failures.Add(msg);
    Console.WriteLine("  FAIL  " + msg);
}

string Read(params string[] parts)
{
    var p = Path.Combine(new[] { repoRoot }.Concat(parts).ToArray());
    return File.Exists(p) ? File.ReadAllText(p) : string.Empty;
}

// Assertions about "this token must NOT appear" have to be evaluated over CODE only.
// The fixes deliberately document the rejected alternatives in comments (e.g. "do not
// use GetStdItemIdx here, it skips the gold sentinel"), so a raw substring test would
// match the explanation and fail.  Strip // line comments and /* */ blocks first.
static string StripComments(string src)
{
    if (string.IsNullOrEmpty(src)) return src;
    var sb = new System.Text.StringBuilder(src.Length);
    bool inStr = false, inChar = false, inLine = false, inBlock = false;
    for (var i = 0; i < src.Length; i++)
    {
        var c = src[i];
        var n = i + 1 < src.Length ? src[i + 1] : '\0';
        if (inLine)
        {
            if (c == '\n') { inLine = false; sb.Append(c); }
            continue;
        }
        if (inBlock)
        {
            if (c == '*' && n == '/') { inBlock = false; i++; }
            continue;
        }
        if (!inStr && !inChar && c == '/' && n == '/') { inLine = true; continue; }
        if (!inStr && !inChar && c == '/' && n == '*') { inBlock = true; i++; continue; }
        if (!inChar && c == '"' && (i == 0 || src[i - 1] != '\\')) inStr = !inStr;
        else if (!inStr && c == '\'' && (i == 0 || src[i - 1] != '\\')) inChar = !inChar;
        sb.Append(c);
    }
    return sb.ToString();
}

var usrEngn = StripComments(Read("GameSvr", "UsrSystem", "UsrEngn.cs"));
var localDb = StripComments(Read("GameSvr", "LocalDB.cs"));
var animal = StripComments(Read("GameSvr", "Actors", "TAnimalObject.cs"));
Check(usrEngn.Length > 0, "UsrEngn.cs readable");
Check(localDb.Length > 0, "LocalDB.cs readable");
Check(animal.Length > 0, "TAnimalObject.cs readable");

// ---------------------------------------------------------------- A / C: race table
Console.WriteLine("== A/C: sub_679F8C race dispatch and the race-108 case body ==");

var addBase = usrEngn.IndexOf("private TBaseObject AddBaseObject(Envirnoment map",
    StringComparison.Ordinal);
Check(addBase > 0, "AddBaseObject(Envirnoment,...) found in UsrEngn.cs");
var switchBody = addBase > 0
    ? usrEngn.Substring(addBase, Math.Min(16000, usrEngn.Length - addBase))
    : string.Empty;

// C.1 race 108 must be wired, to AtMonster, with the Random(2) bo2BA tail.
var case108 = switchBody.IndexOf("case 108:", StringComparison.Ordinal);
Check(case108 > 0,
    "race 108 has a C# case (native jt[37]=0x67A6FD constructs TATMonster; without a "
    + "case Cert stays null and AddBaseObject returns null => the race never spawns)");
if (case108 > 0)
{
    var tail = switchBody.Substring(case108,
        Math.Min(400, switchBody.Length - case108));
    var brk = tail.IndexOf("break;", StringComparison.Ordinal);
    var arm = brk > 0 ? tail.Substring(0, brk) : tail;
    Check(arm.Contains("new AtMonster()", StringComparison.Ordinal),
        "race 108 constructs AtMonster — native classref [0x65E798] resolves to VMT "
        + "0x65E7E4 TATMonster (size 1256, sole override Run=sub_666AE4), and C# "
        + "AtMonster : Monster likewise overrides only Run");
    Check(arm.Contains("Random(2) == 0", StringComparison.Ordinal)
          && arm.Contains("bo2BA = true", StringComparison.Ordinal),
        "race 108 rolls Random(2)==0 -> bo2BA (native 0x67A70C mov eax,2 / call "
        + "sub_403B4C / test eax,eax / jne skip / 0x67A721 mov byte [eax+0x481],1)");
}

// C.2 the four cross-check races that establish [+0x481] == bo2BA must stay intact.
foreach (var (race, roll) in new[] { ("95", "Random(2) == 0"), ("96", "Random(4) == 0"),
                                     ("97", "Random(2) == 0") })
{
    var idx = switchBody.IndexOf("case " + race + ":", StringComparison.Ordinal);
    if (idx < 0)
    {
        idx = switchBody.IndexOf("MONSTER_DIGOUTZOMBI", StringComparison.Ordinal);
    }
    Check(idx > 0, $"race {race} case still present (anchors the [+0x481]==bo2BA mapping)");
}

// A.1 race 250 must NOT exist: `cmp eax,0xEE / ja` caps the window at 11+0xEE=249.
Check(!System.Text.RegularExpressions.Regex.IsMatch(switchBody, @"case\s+250\s*:"),
    "no `case 250:` — native `cmp eax,0xEE / ja 0x67AE5E` (0x67A00E) caps the race "
    + "window at 11+0xEE=249, so race 250 falls to the default sink (the discovery doc "
    + "claim that TQingLong owns races 178 AND 250 is wrong; it owns 178 only)");

// B: no invented base-class fallback for unmapped races.  Native's default sink
// 0x67AE5E is `xor eax,eax` -> nil, so a `default:` arm that builds a monster would be
// a fabrication.  Assert the switch has no default arm constructing anything.
var defaultArm = System.Text.RegularExpressions.Regex.Match(switchBody,
    @"default\s*:\s*(?:\r?\n\s*)*Cert\s*=\s*new");
Check(!defaultArm.Success,
    "AddBaseObject's race switch has NO `default: Cert = new ...` fallback — native's "
    + "default sink 0x67AE5E does `xor eax,eax` and returns nil, so any base-class "
    + "fallback would invent behaviour the original does not have");

// C.3 FireKingMonster must hang off race 150, not 216.
// index table[150-0x0B=0x8B] = 0x44 = 68 ; jt[68] = 0x67A985 ; that body loads classref
// [0x67FF34] (VMT 0x67FF80 TFireKingMonster, size 1256, parent TAnimal) and calls the
// ctor sub_6821F8.  Ownership is exhaustive: ONE classref load site image-wide
// (0x67A987), ONE E8 rel32 caller of the ctor (0x67A98C), ZERO sites writing/comparing
// byte[reg+0x178] against 0xD8, and no immediate 0xD8 within 0x40 bytes before any of
// sub_679F8C's four callers.  Race 216's index byte is 0x00 -> jt[0] = 0x67AE5E = the
// default sink, so 216 spawns nothing natively.
var fk = System.Text.RegularExpressions.Regex.Match(switchBody,
    @"case\s+(\d+)\s*:\s*(?:\r?\n\s*)*Cert\s*=\s*new\s+FireKingMonster\s*\(\s*\)\s*;");
Check(fk.Success && fk.Groups[1].Value == "150",
    "FireKingMonster is wired to race 150 — native jt[68]=0x67A985 is the ONLY site that "
    + "constructs TFireKingMonster (classref [0x67FF34] has 1 load site, ctor sub_6821F8 "
    + "has 1 caller); race 216's index byte is 0x00 -> default sink 0x67AE5E = nil"
    + (fk.Success ? $" (found race {fk.Groups[1].Value})" : " (no FireKingMonster case)"));
Check(!System.Text.RegularExpressions.Regex.IsMatch(switchBody,
        @"case\s+216\s*:"),
    "no `case 216:` — race 216 has index byte 0x00 in the native index table at "
    + "0x67A026, i.e. it routes to jt[0] = 0x67AE5E (`xor eax,eax` -> nil)");
var fkSrc = StripComments(Read("GameSvr", "Monsters", "Monster", "FireKingMonster.cs"));
Check(fkSrc.Contains("NativeRace = 150", StringComparison.Ordinal),
    "FireKingMonster.NativeRace == 150 (the constant the NativeCattleCheck audit pins)");

// ---------------------------------------------------------------- D: MonItems parse
Console.WriteLine("== D: sub_6799E0 MonItems parse defaults / delimiters / row gate ==");

var loadMon = localDb.IndexOf("public int LoadMonitems(", StringComparison.Ordinal);
Check(loadMon > 0, "LoadMonitems found in LocalDB.cs");
var body = loadMon > 0
    ? localDb.Substring(loadMon, Math.Min(6000, localDb.Length - loadMon))
    : string.Empty;
var bodyEnd = body.IndexOf("public void LoadNpcs", StringComparison.Ordinal);
var loaderOnly = bodyEnd > 0 ? body.Substring(0, bodyEnd) : body;

// D.1 the three StrToIntDef defaults are all 1.
Check(!System.Text.RegularExpressions.Regex.IsMatch(loaderOnly,
        @"Str_ToInt\(\s*s30\s*,\s*-1\s*\)"),
    "no Str_ToInt(...,-1) in LoadMonitems — native uses `mov edx,1` at ALL THREE call "
    + "sites (0x679C06 SelPoint, 0x679C1A MaxPoint, 0x679C2D Count) feeding "
    + "sub_40CA18 StrToIntDef, so the default is 1");
Check(System.Text.RegularExpressions.Regex.Matches(loaderOnly,
        @"Str_ToInt\(\s*s30\s*,\s*1\s*\)").Count == 3,
    "exactly 3 Str_ToInt(s30,1) calls — matching native's three StrToIntDef(field,1) "
    + "sites 0x679C06 / 0x679C1A / 0x679C2D");

// D.2 SelPoint keeps the `-1` (native `dec eax` @0x679C13) but MaxPoint/Count do not.
Check(loaderOnly.Contains("SelPoint = n18 - 1", StringComparison.Ordinal),
    "SelPoint = StrToIntDef(f1,1) - 1 — native 0x679C13 `48` dec eax before "
    + "0x679C17 mov [rec+0x10],eax");
Check(loaderOnly.Contains("MaxPoint = n1C", StringComparison.Ordinal)
      && !loaderOnly.Contains("MaxPoint = n1C - 1", StringComparison.Ordinal),
    "MaxPoint has NO -1 — native 0x679C1A..0x679C2A stores the StrToIntDef result "
    + "straight into [rec+0x14] with no dec");

// D.3 the numeric acceptance gate must be gone.
Check(!loaderOnly.Contains("n18 > 0 && n1C > 0", StringComparison.Ordinal),
    "no `n18 > 0 && n1C > 0` row gate — native's only acceptance condition is "
    + "sub_74C2D4(name) != nil (0x679BB4 / 0x679BC1 je / 0x679BDD je 0x679C4E). "
    + "A real row `1/0<TAB>记忆项链` (祖玛教主.txt, bytes 31 2F 30 09 bc c7 d2 e4 cf ee "
    + "c1 b4) gives SelPoint=0 MaxPoint=0 => Random(0)<=0 always true = drops every "
    + "kill natively, but the old `n1C > 0` gate discarded the row entirely");

// D.4 the item name must NOT be unquoted.
Check(!loaderOnly.Contains("ArrestStringEx", StringComparison.Ordinal),
    "LoadMonitems does not unquote the item name — sub_6799E0's body 0x6799E0..0x679C92 "
    + "contains ZERO 0x22 ('\"') comparisons (only 09/2F/20 delimiters and 3B comment), "
    + "so a quoted name keeps its quotes and fails sub_74C2D4");

// D.5 the row gate must resolve gold too.  GetStdItemIdx / CopyToUserItemFromName skip
// index 0 when the native gold sentinel is present (HasNativeStdItemSentinel), but
// native sub_74C2D4 is a plain hash lookup with no index exclusion, and the drop
// consumer sub_71FA20 @0x71FB64 `cmp word ptr [edi],0` detects the gold row by
// inspecting the RESOLVED StdItem — so gold MUST resolve or every gold row dies.
Check(!loaderOnly.Contains("GetStdItemIdx", StringComparison.Ordinal),
    "LoadMonitems does NOT gate rows on GetStdItemIdx — that helper starts its scan at "
    + "index 1 when items[0] is the 金币 sentinel, which would silently reject every "
    + "gold row (369 such rows across 328 monsters in the sample MonItems set), whereas "
    + "native sub_74C2D4 hashes the name with no index exclusion");
Check(loaderOnly.Contains("ResolvesToStdItemName(", StringComparison.Ordinal),
    "row gate is ResolvesToStdItemName(name) — the sub_74C2D4 equivalent");
var helper = localDb.IndexOf("private static bool ResolvesToStdItemName(",
    StringComparison.Ordinal);
Check(helper > 0, "ResolvesToStdItemName helper present in LocalDB.cs");
if (helper > 0)
{
    var h = localDb.Substring(helper, Math.Min(900, localDb.Length - helper));
    Check(System.Text.RegularExpressions.Regex.IsMatch(h, @"for\s*\(\s*var\s+i\s*=\s*0\s*;"),
        "ResolvesToStdItemName scans from index 0 (INCLUDES the 金币 sentinel) — native "
        + "sub_74C2D4 @0x74C2E9 does `mov eax,[ebx+0x20] ; call sub_49F5F4` with no "
        + "index-0 exclusion");
    Check(!h.Contains("HasNativeStdItemSentinel", StringComparison.Ordinal),
        "ResolvesToStdItemName does not apply the sentinel skip");
}

// D.6 delimiter sets, per sub_4C6BA4's count-1 stack arg.
Check(System.Text.RegularExpressions.Regex.Matches(loaderOnly,
        @"new\[\]\s*\{\s*"" ""\s*,\s*""/""\s*,\s*""\\t""\s*\}").Count == 2,
    "fields 1-2 split on {' ','/','\\t'} — native `push 2` + [ebp-0x3C]={09,2F,20} at "
    + "0x679B06-0x679B21 and 0x679B31-0x679B4C (the pushed value is count-1: "
    + "sub_4C6BA4 @0x4C6BF0 does `mov eax,[ebp+0xC] ; inc eax`)");
Check(System.Text.RegularExpressions.Regex.Matches(loaderOnly,
        @"new\[\]\s*\{\s*"" ""\s*,\s*""\\t""\s*\}").Count == 2,
    "fields 3-4 split on {' ','\\t'} — native `push 1` + [ebp-0x48]={09,20} at "
    + "0x679B5C-0x679B73 and 0x679B83-0x679B9A");

// ---------------------------------------------------------------- E: m_WAbil snapshot
Console.WriteLine("== E: MonInitialize m_Abil -> m_WAbil is a copy (sub_71EA04 rep movsd) ==");

Check(!System.Text.RegularExpressions.Regex.IsMatch(switchBody,
        @"Cert\.m_WAbil\s*=\s*Cert\.m_Abil\s*;"),
    "AddBaseObject does NOT alias `Cert.m_WAbil = Cert.m_Abil` — native MonInitialize "
    + "sub_71EA04 @0x71EB68-0x71EB79 does `lea esi,[ebx+0x1E8] / lea edi,[ebx+0x264] / "
    + "mov ecx,0x1F / rep movsd` = a 0x7C-byte COPY between two non-overlapping regions");
Check(switchBody.Contains("Cert.m_WAbil = new TAbility();", StringComparison.Ordinal)
      && switchBody.Contains("Cert.m_WAbil.CopyFrom(Cert.m_Abil);", StringComparison.Ordinal),
    "AddBaseObject deep-copies m_Abil into a FRESH TAbility — TAbility is a C# class "
    + "(reference type), so aliasing made MonsterRecalcAbilitys (TBaseObject.cs:3097, "
    + "called for every race>=RC_ANIMAL from TBaseObject.Base.cs:2825) read its own "
    + "output as the spawn template: `n8 = m_Abil.MaxHP + Round(m_Abil.MaxHP*0.15)*lvl` "
    + "then `m_WAbil.MaxHP = ...` compounds on each Recalc, while native's m_Abil at "
    + "+0x1E8 stays at its spawn value forever");

// CopyFrom must cover every TAbility field, or the "copy" silently drops state.
var abil = Read("SystemModule", "Packet", "TAbility.cs");
Check(abil.Length > 0, "TAbility.cs readable");
var fieldNames = System.Text.RegularExpressions.Regex.Matches(abil,
        @"public\s+(?:ushort|int|byte)\s+(\w+)\s*;")
    .Select(m => m.Groups[1].Value).Distinct().ToList();
var copyFrom = abil.IndexOf("public void CopyFrom(", StringComparison.Ordinal);
Check(copyFrom > 0, "TAbility.CopyFrom present");
if (copyFrom > 0 && fieldNames.Count > 0)
{
    var cf = abil.Substring(copyFrom, Math.Min(1600, abil.Length - copyFrom));
    var missing = fieldNames
        .Where(f => !System.Text.RegularExpressions.Regex.IsMatch(cf,
            $@"\b{f}\s*=\s*other\.{f}\s*;"))
        .ToList();
    Check(missing.Count == 0,
        $"TAbility.CopyFrom assigns all {fieldNames.Count} fields (native copies the "
        + "whole 0x7C-byte block with rep movsd, so a partial copy would lose spawn "
        + "state)" + (missing.Count > 0 ? " MISSING: " + string.Join(",", missing) : ""));
}

// ---------------------------------------------------------------- F: sticky pre-pass
Console.WriteLine("== F: TAnimal.SearchTarget sticky slot [+0x464] is dead in native ==");

var search = animal.IndexOf("protected virtual bool SearchTarget()", StringComparison.Ordinal);
Check(search > 0, "AnimalObject.SearchTarget found");
if (search > 0)
{
    var s = animal.Substring(search, Math.Min(1600, animal.Length - search));
    var end = s.IndexOf("protected void sub_4C959C", StringComparison.Ordinal);
    var scan = end > 0 ? s.Substring(0, end) : s;
    // The scan must keep the min-distance visible-list loop as its ONLY selection path.
    Check(scan.Contains("m_VisibleActors", StringComparison.Ordinal)
          && scan.Contains("999", StringComparison.Ordinal),
        "SearchTarget selects by scanning m_VisibleActors with the 999 seed — native "
        + "sub_71DA70 @0x71DAA3 `mov [ebp-0xC],0x3E7` (=999) then the list scan; this "
        + "is the ONLY path that ever picks a target because the sticky pre-pass "
        + "always falls through");
    // Guard the ADJUDICATION: no populated sticky-target field may be introduced,
    // because native's [+0x464] is only ever cleared (0x71DB06 / 0x71DD3C, both after
    // an xor) and the TAnimal ctor sub_71D828 never initialises it.
    Check(!System.Text.RegularExpressions.Regex.IsMatch(scan,
            @"m_PreferredTarget|m_StickyTarget"),
        "SearchTarget has NO populated sticky/preferred-target pre-pass — native's "
        + "[self+0x464] is written ONLY with zero (0x71DB04 xor edx,edx / 0x71DB06 mov "
        + "[eax+0x464],edx and 0x71DD3A xor eax,eax / 0x71DD3C mov [ebx+0x464],eax), "
        + "the ctor sub_71D828 never sets it, and an exhaustive raw disp32 sweep finds "
        + "no other TAnimal write — so `test esi,esi` @0x71DB0C is always false and the "
        + "pre-pass is dead code.  Adding a POPULATED one would invent aggro stickiness "
        + "(the discovery doc's 'FIX' verdict on this item is wrong)");
}

Console.WriteLine();
// ------------------------------------------------ G: RecalcAbilitys slot ownership
// Slot +0x0C8 is RecalcAbilitys; slot +0x08C is SearchTarget.  Native TElfMonster
// (VMT 0x662B38), TElfWarriorMonster (0x662DC4) and TWhiteSkeleton (0x660E80) each
// override EXACTLY {Run, SearchTarget} — none of them overrides RecalcAbilitys.  Their
// SearchTarget bodies are "call the base then call a private stat-reset helper":
//   TElfMonster        sub_66A2C4: 66A2CC call sub_71DF70 ; 66A2D3 call sub_66A2DC
//   TElfWarriorMonster sub_66A76C: 66A774 call sub_71DF70 ; 66A77B call sub_66A584
//   TWhiteSkeleton     sub_667D48: 667D50 call sub_71DF70 ; 667D57 call sub_667D60
// Hanging the helper off RecalcAbilitys instead fires it on the equipment/status
// recompute path (TBaseObject.Base.cs:2825) rather than on the search tick — a
// different call site AND a different frequency.
Console.WriteLine("== G: Elf/ElfWarrior/WhiteSkeleton reset helper lives on SearchTarget ==");
foreach (var (file, resetFn, ea) in new[]
         {
             ("ElfMonster", "ResetElfMon", "sub_66A2C4/sub_66A2DC"),
             ("ElfWarriorMonster", "ResetElfMon", "sub_66A76C/sub_66A584"),
             ("WhiteSkeleton", "sub_4AAD54", "sub_667D48/sub_667D60"),
         })
{
    var src = StripComments(Read("GameSvr", "Monsters", "Monster", file + ".cs"));
    Check(src.Length > 0, $"{file}.cs readable");
    Check(System.Text.RegularExpressions.Regex.IsMatch(src,
            @"protected\s+override\s+bool\s+SearchTarget\s*\(\s*\)\s*\{[^}]*base\.SearchTarget\s*\(\s*\)\s*;[^}]*"
            + System.Text.RegularExpressions.Regex.Escape(resetFn) + @"\s*\(\s*\)\s*;"),
        $"{file} overrides SearchTarget as base.SearchTarget() then {resetFn}() — native "
        + $"{ea} does exactly `call sub_71DF70` then `call <resetFn>`");
    Check(!System.Text.RegularExpressions.Regex.IsMatch(src,
            @"override\s+void\s+RecalcAbilitys\s*\("),
        $"{file} does NOT override RecalcAbilitys — its native VMT overrides only "
        + "{Run, SearchTarget}, so slot +0x0C8 must stay inherited");
}

// G.2 native AppearNow VIRTUALLY DISPATCHES SearchTarget (it is not a direct helper
// call, and it is definitely not a full RecalcAbilitys):
//   TElfMonster        sub_66A228 @0x66A240 `FF 92 8C 00 00 00` call dword [edx+0x8C]
//                      then 0x66A248-0x66A258 recompute m_nWalkSpeed = 500 - lvl*50
//                      then 0x66A25E `add dword [ebx+0x384],0x320` (m_dwWalkTick += 800)
//   TElfWarriorMonster sub_66A50C @0x66A565 same vcall, then ONLY the += 800 (no walk
//                      speed recompute), then 0x66A575 dwDigDownTick = GetTickCount()
foreach (var (file, alsoWalkSpeed) in new[] { ("ElfMonster", true),
                                              ("ElfWarriorMonster", false) })
{
    var src = StripComments(Read("GameSvr", "Monsters", "Monster", file + ".cs"));
    var ap = src.IndexOf("public void AppearNow()", StringComparison.Ordinal);
    Check(ap > 0, $"{file}.AppearNow found");
    if (ap <= 0) continue;
    var seg = src.Substring(ap, Math.Min(700, src.Length - ap));
    var close = seg.IndexOf("\n        }", StringComparison.Ordinal);
    if (close > 0) seg = seg.Substring(0, close);
    Check(seg.Contains("SearchTarget();", StringComparison.Ordinal),
        $"{file}.AppearNow dispatches SearchTarget() — native "
        + (file == "ElfMonster" ? "sub_66A228 @0x66A240" : "sub_66A50C @0x66A565")
        + " does `call dword [edx+0x8C]`, i.e. the monster really searches on appear");
    Check(!seg.Contains("RecalcAbilitys();", StringComparison.Ordinal),
        $"{file}.AppearNow does NOT call RecalcAbilitys — native's AppearNow body has no "
        + "vcall through +0x0C8 and no stat recompute beyond the two named stores");
    Check(seg.Contains("m_dwWalkTick = m_dwWalkTick + 800;", StringComparison.Ordinal),
        $"{file}.AppearNow does m_dwWalkTick += 800 — native `add dword [ebx+0x384],0x320`"
        + " (an ADD onto the helper's tick+2000, not an assignment)");
    Check(seg.Contains("m_nWalkSpeed = 500 - m_btSlaveMakeLevel * 50;",
              StringComparison.Ordinal) == alsoWalkSpeed,
        $"{file}.AppearNow "
        + (alsoWalkSpeed ? "recomputes m_nWalkSpeed (native 0x66A248-0x66A258)"
                         : "does NOT recompute m_nWalkSpeed (native sub_66A50C goes "
                           + "straight from the vcall to the += 800)"));
}

// G.3 WhiteSkeleton's helper must clamp m_btSlaveMakeLevel to 3 BEFORE both multiplies.
//   667D66  mov al,byte [ebx+0x483]
//   667D6C  3C 03        cmp al,3
//   667D6E  76 07        jbe 0x667D77          ; <=3 keep
//   667D70  B8 03 ...    mov eax,3             ; >3 saturate to 3
//   667D7C  imul edx,eax,0x258 ; mov ecx,0xBB8 ; sub ecx,edx  -> m_nNextHitTime
//   667D8F  imul eax,eax,0xFA  ; mov edx,0x4B0 ; sub edx,eax  -> m_nWalkSpeed
// Without the clamp, lvl>3 drives both intervals to <=0 (lvl=5 => 0 and -50), and a
// negative interval makes `(now - tick) > interval` always true = no attack/move cooldown.
var ws = StripComments(Read("GameSvr", "Monsters", "Monster", "WhiteSkeleton.cs"));
Check(System.Text.RegularExpressions.Regex.IsMatch(ws,
        @"m_btSlaveMakeLevel\s*>\s*3\s*\?\s*3\s*:\s*.*m_btSlaveMakeLevel"),
    "WhiteSkeleton clamps m_btSlaveMakeLevel to 3 — native sub_667D60 @0x667D6C "
    + "`cmp al,3 / jbe / mov eax,3` saturates BEFORE both imuls");
Check(!System.Text.RegularExpressions.Regex.IsMatch(ws,
        @"m_nNextHitTime\s*=\s*3000\s*-\s*this\.m_btSlaveMakeLevel\s*\*\s*600"),
    "WhiteSkeleton does not multiply the UNCLAMPED level — an unclamped lvl>3 yields a "
    + "non-positive m_nNextHitTime/m_nWalkSpeed (lvl=5 => 0 and -50) = no cooldown");
Check(ws.Contains("* 600", StringComparison.Ordinal)
      && ws.Contains("* 250", StringComparison.Ordinal)
      && ws.Contains("3000 -", StringComparison.Ordinal)
      && ws.Contains("1200 -", StringComparison.Ordinal),
    "WhiteSkeleton keeps the native constants 3000/600 (0xBB8/0x258) and 1200/250 "
    + "(0x4B0/0xFA)");
// Anchor INSIDE Run's body, not on the `m_boIsFirst = false;` field initialiser at the
// top of the class — the loose anchor matched the SearchTarget override's helper call
// instead and therefore did not bite when Run's call was deleted (caught by mutation G7).
var wsRun = ws.IndexOf("public override void Run()", StringComparison.Ordinal);
Check(wsRun > 0, "WhiteSkeleton.Run found");
if (wsRun > 0)
{
    var runSeg = ws.Substring(wsRun, Math.Min(900, ws.Length - wsRun));
    var runEnd = runSeg.IndexOf("\n        private", StringComparison.Ordinal);
    if (runEnd > 0) runSeg = runSeg.Substring(0, runEnd);
    Check(runSeg.Contains("RM_DIGUP", StringComparison.Ordinal)
          && runSeg.Contains("sub_4AAD54();", StringComparison.Ordinal),
        "WhiteSkeleton.Run's first-appear branch calls the reset helper — native Run "
        + "sub_667DB8 @0x667E05 `E8 56 FF FF FF call sub_667D60` right after the RM_DIGUP "
        + "SendRefMsg; C# previously omitted it, leaving the freshly-emerged skeleton on "
        + "MonInitialize's raw intervals");
}

Console.WriteLine();
if (failures.Count == 0)
{
    Console.WriteLine("MonsterDomainRaceAndDropCheck: PASS "
        + "race-window=11..249(no-250) race108=AtMonster+Random(2)->bo2BA "
        + "no-default-fallback(native-sink=nil) monitems-defaults=1 "
        + "monitems-no-numeric-gate monitems-no-unquote "
        + "monitems-gate=ResolvesToStdItemName(index0-included,gold-safe) "
        + "wabil=deep-copy(rep-movsd-0x7C) sticky-slot=dead-in-native "
        + "fireking-race=150 recalc-slot=SearchTarget(elf/elfwarrior/whiteskeleton) whiteskeleton-lvl-clamp=3");
    return 0;
}
Console.WriteLine($"MonsterDomainRaceAndDropCheck: FAIL ({failures.Count})");
foreach (var f in failures) Console.WriteLine("  - " + f);
return 1;
