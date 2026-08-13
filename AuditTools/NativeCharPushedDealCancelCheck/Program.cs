// NativeCharPushedDealCancelCheck (TRADE-49) — pins the fact that an open
// player trade is cancelled by a SUCCESSFUL PUSH (CharPushed), and that the
// cancel is gated on the player actually having been displaced.
//
// Native truth (verified 2026-08-10 by capstone reads of
// D:/loym2/staging/_reunpack_work/flat_image.bin, ImageBase 0x400000,
// backing M2Server_reunpacked_20260803.exe — the canonical image):
//
//   TPlayer.CharPushed = sub_6BFD1C, VMT 0x6AC8C8 slot +0xA4. It OVERRIDES the
//   shared base body sub_76834C, which sits at +0xA4 of THumanKind (0x73BC34),
//   TCreature (0x764608) and the hero/npc classes. Full body, 28 instructions:
//
//     6BFD22  mov edi,ecx / mov esi,edx / mov ebx,eax
//     6BFD28  mov dl,0x34 ; call sub_772960 ; test al,al ; jne 0x6BFD39
//     6BFD35    xor esi,esi ; jmp 0x6BFD51     ; state 0x34 -> 0 steps, NO cancel
//     6BFD3F  call sub_76834C                  ; inherited CharPushed, eax=steps
//     6BFD44  mov esi,eax
//     6BFD46  test esi,esi ; jle 0x6BFD51      ; 0 steps => SKIP the cancel
//     6BFD4C  mov eax,ebx ; call sub_6C43C4    ; DealCancel
//     6BFD51  mov eax,esi ; ret                ; returns the step count
//
//   So the gate is `steps > 0`, i.e. the trade SURVIVES a push that was fully
//   blocked by terrain/occupancy. It is NOT gated on damage.
//
//   Census of all 11 direct `call sub_6C43C4` sites in CODE
//   (staging/_cs_dealcancel_xrefs.py, each re-decoded at the site so no
//   mid-instruction byte match is counted):
//     0x6B2C8B in sub_6B2C7C  (save-timer wrapper, TPlayer.Run @0x6B1C17)
//     0x6B2EB2 in sub_6B2D38  (logout)
//     0x6BFD4C in sub_6BFD1C  (THIS one — the push)
//     0x6C43B4 in sub_6C4348  (op 1027)
//     0x6C4409 in sub_6C43C4  (its own recursion into the counterparty)
//     0x6C446D in sub_6C4454  (op 1029)
//     0x6C4633 / 0x6C4689 / 0x6C46DA / 0x6C49BA in sub_6C4580 (op 1030 commit)
//     0x6D9192 in sub_6D7D68  (op 1028 dispatch)
//   StruckDamage (sub_73F9FC human / sub_767A18 creature) does NOT appear —
//   the "cancel on damage taken" reading is FALSE for this binary.
//
// The C# port therefore hangs the cancel off CharPushed's `result > 0` tail in
// TBaseObject.cs, restricted to RC_PLAYOBJECT (only TPlayObject owns
// m_boDealing / m_DealCreat, mirroring the TPlayer-only override).

using System.Reflection;
using System.Runtime.CompilerServices;
using GameSvr;

int failures = 0;

void Check(bool condition, string label)
{
    if (condition)
    {
        return;
    }
    Console.WriteLine($"  FAIL: {label}");
    failures++;
}

// ------------------------------------------------------------ source pinning
// The behaviour lives on a non-virtual instance method reached through several
// magic/GM paths, and the deal state is private to TPlayObject, so the wiring
// is pinned at the source level. A mutation of either the call or its gate has
// to break one of these assertions.
string root = FindRepositoryRoot();
string actors = File.ReadAllText(Path.Combine(root, "GameSvr", "Actors",
    "TBaseObject.cs"));

int pushedAt = actors.IndexOf("public int CharPushed(byte nDir, int nPushCount)",
    StringComparison.Ordinal);
Check(pushedAt >= 0, "CharPushed(byte,int) is present in TBaseObject.cs");

// Slice from CharPushed to the next member so "inside CharPushed" is a real
// claim rather than a whole-file substring hit.
int pushedEnd = actors.IndexOf("public int MagPassThroughMagic",
    pushedAt < 0 ? 0 : pushedAt, StringComparison.Ordinal);
Check(pushedEnd > pushedAt, "CharPushed body boundary located");

string body = pushedAt >= 0 && pushedEnd > pushedAt
    ? actors[pushedAt..pushedEnd]
    : string.Empty;

// Strip comment lines: a commented-out call must not satisfy the gate.
string live = string.Join('\n', body.Split('\n')
    .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

Check(live.Contains("DealCancel()", StringComparison.Ordinal),
    "CharPushed body contains a LIVE (non-comment) DealCancel() call");
Check(live.Contains("result > 0", StringComparison.Ordinal),
    "the cancel is gated on result > 0 (native 0x6BFD46 `test esi,esi; jle`)");
Check(live.Contains("Grobal2.RC_PLAYOBJECT", StringComparison.Ordinal),
    "the cancel is restricted to RC_PLAYOBJECT (native: TPlayer-only override)");

// The cancel must come AFTER the direction restore and BEFORE `return result`,
// matching native order (sub_76834C returns, THEN 0x6BFD4C fires).
int cancelIdx = live.IndexOf("DealCancel()", StringComparison.Ordinal);
int restoreIdx = live.IndexOf("m_btDirection = olddir", StringComparison.Ordinal);
int returnIdx = live.LastIndexOf("return result", StringComparison.Ordinal);
Check(restoreIdx >= 0 && cancelIdx > restoreIdx,
    "cancel fires after the inherited push work (direction restore)");
Check(returnIdx >= 0 && cancelIdx < returnIdx,
    "cancel fires before `return result`");

// Native returns the step count unchanged (0x6BFD51 `mov eax,esi`); the cancel
// must not clobber it. `int result = 0;` is the legitimate initializer, so only
// a RE-assignment after the cancel would be a divergence.
string afterCancel = cancelIdx >= 0 ? live[cancelIdx..] : string.Empty;
Check(!afterCancel.Contains("result = 0", StringComparison.Ordinal) &&
      !afterCancel.Contains("result=0", StringComparison.Ordinal),
    "CharPushed still returns the native step count (cancel does not clobber it)");

// ------------------------------------------------------------- reachability
// DealCancel must be reachable from TBaseObject (native reaches it from the
// TPlayer override). `private` would make the wiring above uncompilable, so
// assert the accessibility the port depends on.
MethodInfo dealCancel = typeof(TPlayObject).GetMethod("DealCancel",
    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
Check(dealCancel != null, "TPlayObject.DealCancel is resolvable by reflection");
Check(dealCancel != null && dealCancel.GetParameters().Length == 0,
    "DealCancel takes no parameters (native sub_6C43C4 is eax-only)");
Check(dealCancel != null && !dealCancel.IsPrivate,
    "DealCancel is visible to TBaseObject (assembly-internal, not private)");

// TPlayObject owns the deal state the guard implies; if these move, the
// RC_PLAYOBJECT restriction above stops being the right shape.
Check(typeof(TPlayObject).GetField("m_boDealing",
        BindingFlags.Instance | BindingFlags.NonPublic |
        BindingFlags.Public) != null,
    "m_boDealing lives on TPlayObject (justifies the RC_PLAYOBJECT gate)");
Check(typeof(TPlayObject).GetField("m_DealCreat",
        BindingFlags.Instance | BindingFlags.NonPublic |
        BindingFlags.Public) != null,
    "m_DealCreat lives on TPlayObject");

// -------------------------------------------------------- negative control
// The false reading this task started from: StruckDamage must NOT cancel.
int struckAt = actors.IndexOf("public void StruckDamage(int nDamage, TBaseObject attacker)",
    StringComparison.Ordinal);
Check(struckAt >= 0, "StruckDamage(int,TBaseObject) is present");
int struckEnd = actors.IndexOf("public virtual string GeTBaseObjectInfo",
    struckAt < 0 ? 0 : struckAt, StringComparison.Ordinal);
Check(struckEnd > struckAt, "StruckDamage body boundary located");
string struckBody = struckAt >= 0 && struckEnd > struckAt
    ? actors[struckAt..struckEnd]
    : string.Empty;
string struckLive = string.Join('\n', struckBody.Split('\n')
    .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
Check(!struckLive.Contains("DealCancel", StringComparison.Ordinal),
    "StruckDamage does NOT call DealCancel (sub_73F9FC/sub_767A18 have no such call)");

Console.WriteLine(failures == 0
    ? "NativeCharPushedDealCancelCheck: PASS"
    : $"NativeCharPushedDealCancelCheck: FAIL ({failures})");
return failures == 0 ? 0 : 1;

// Anchor on the compile-time source path, not AppContext.BaseDirectory:
// the build output lives under bin/Debug/netX.0/ inside the AuditTools
// project, and on some configurations that tree is redirected outside the
// repository, so walking up from it silently fails to find LyoMir2.sln.
static string FindRepositoryRoot([CallerFilePath] string callerFilePath = "")
{
    var dir = new DirectoryInfo(Path.GetDirectoryName(callerFilePath)!);
    while (dir != null &&
           !File.Exists(Path.Combine(dir.FullName, "LyoMir2.sln")))
    {
        dir = dir.Parent;
    }
    if (dir == null)
    {
        throw new InvalidOperationException("repository root not found");
    }
    return dir.FullName;
}
