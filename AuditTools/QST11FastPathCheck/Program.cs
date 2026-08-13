// QST-11 — the group-0 V fast path.
//
// Native truth (flat_image.bin, ImageBase 0x400000):
//
//   GetV = sub_6DF1E4
//     0x6DF1F1  C7 45 FC FF FF FF FF  mov [ebp-4], -1     ; miss seed
//     0x6DF203  85 F6                 test esi, esi       ; group
//     0x6DF205  75 14                 jne 0x6DF21B        ; != 0 -> keyed path
//     0x6DF209  4A                    dec edx             ; index - 1
//     0x6DF20A  83 EA 64              sub edx, 0x64
//     0x6DF20D  73 0C                 jae 0x6DF21B        ; unsigned >= 100 -> keyed
//     0x6DF20F  8B 84 83 08 08 00 00  mov eax, [ebx+eax*4+0x808]   ; INLINE READ
//     0x6DF216  89 45 FC              mov [ebp-4], eax    ; overwrites the -1
//
//   SetV = sub_6DF238
//     0x6DF299  85 FF                 test edi, edi       ; group
//     0x6DF29B  75 16                 jne 0x6DF2B3        ; != 0 -> keyed path
//     0x6DF29F  4A                    dec edx
//     0x6DF2A0  83 EA 64              sub edx, 0x64
//     0x6DF2A3  73 0E                 jae 0x6DF2B3
//     0x6DF2A8  89 84 B3 08 08 00 00  mov [ebx+esi*4+0x808], eax   ; INLINE WRITE
//     0x6DF2AF  B0 01                 mov al, 1           ; success
//
//   GetS/SetS (sub_6DF1B4 / sub_6DF248) have no group-0 branch at all; they open
//   by rejecting either argument being non-positive, so group 0 never reaches an
//   inline region on the S bank:
//     GetS 0x6DF1BE test ecx,ecx / 0x6DF1C0 jle    SetS 0x6DF251 test edi,edi / jle
//     GetS 0x6DF1C2 test edx,edx / 0x6DF1C4 jle    SetS 0x6DF255 test esi,esi / jle
//
// So the contract is: V + group 0 + index 1..100 lands in an inline 4-byte array
// at obj+0x808 and never in the keyed dictionary; everything else is either the
// keyed path or a rejection.
//
// This used to be five grep assertions over PasApiBridge.cs that pinned the C#
// spelling `if (group == 0 && char.ToUpperInvariant(type) == 'V')` and the field
// name `m_nVal[index]`. Those describe one particular way of writing the port,
// not the exe, and they went red when the field was renamed to m_ScriptVGroup0
// even though the behaviour became MORE faithful. The checks below drive the
// bridge instead, so any implementation that honours the exe passes.

using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.UserEngine = new UserEngine();
M2Share.ObjectManager = new ObjectManager();
M2Share.MapManager = new MapManager();

var failures = 0;

void Check(bool condition, string label)
{
    if (condition)
    {
        Console.WriteLine($"  PASS: {label}");
        return;
    }

    Console.WriteLine($"  FAIL: {label}");
    failures++;
}

static TPlayObject NewPlayer() => new();

var player = NewPlayer();
var bridge = new PasApiBridge { CurrentPlayer = player };

Console.WriteLine("QST-11-A1: group-0 V reads come from the inline slots, not the dictionary");
player.m_ScriptVGroup0[7] = 4242;
// A keyed entry can never legitimately sit here (a flat key below 1000 requires
// group 0, and group-0 writes go inline), so this is a decoy: if the read is
// served from the dictionary it returns 999 instead of 4242.
player.m_ScriptVVars[7] = 999;
Check(bridge.GetPlayerVar('V', 0, 7).AsInt() == 4242,
    "GetV(V,0,7) returned the inline slot (0x6DF20F), not m_ScriptVVars");

Console.WriteLine("QST-11-A2: group-0 V writes land inline and leave the dictionary alone");
var wrote = bridge.SetPlayerVar('V', 0, 9, PasValue.FromInt(1234));
Check(wrote, "SetV(V,0,9) reported success (0x6DF2AF `mov al,1`)");
Check(player.m_ScriptVGroup0[9] == 1234,
    "SetV(V,0,9) stored into the inline slot (0x6DF2A8)");
Check(!player.m_ScriptVVars.ContainsKey(9) && !player.m_ScriptVVars.ContainsKey(0 * 1000 + 9),
    "SetV(V,0,9) did not also write the keyed dictionary");

Console.WriteLine("QST-11-A3: an untouched inline slot reads 0, not the -1 miss value");
// 0x6DF20F unconditionally overwrites the -1 seeded at 0x6DF1F1, so a fresh
// character reads 0. Serving these from the dictionary would return -1 and
// invert every downstream `= 0` quest test.
Check(new PasApiBridge { CurrentPlayer = NewPlayer() }
        .GetPlayerVar('V', 0, 50).AsInt() == 0,
    "GetV(V,0,50) on a fresh character returned 0");

Console.WriteLine("QST-11-A4: the inline window is index 1..100 (`sub edx,0x64` / `jae`)");
var bounds = NewPlayer();
var boundsBridge = new PasApiBridge { CurrentPlayer = bounds };
Check(boundsBridge.GetPlayerVar('V', 0, 1).AsInt() == 0, "index 1 is inside the window");
Check(boundsBridge.GetPlayerVar('V', 0, 100).AsInt() == 0, "index 100 is inside the window");
Check(boundsBridge.GetPlayerVar('V', 0, 0).AsInt() == -1, "index 0 is rejected with -1");
Check(boundsBridge.GetPlayerVar('V', 0, 101).AsInt() == -1, "index 101 is rejected with -1");
Check(!boundsBridge.SetPlayerVar('V', 0, 0, PasValue.FromInt(5)),
    "SetV at index 0 is refused");
Check(!boundsBridge.SetPlayerVar('V', 0, 101, PasValue.FromInt(5)),
    "SetV at index 101 is refused");
Check(bounds.m_ScriptVVars.Count == 0 && bounds.m_ScriptSVars.Count == 0,
    "a refused out-of-window access wrote nothing anywhere");

Console.WriteLine("QST-11-A5: the S bank has no group-0 inline region");
var sBank = NewPlayer();
var sBridge = new PasApiBridge { CurrentPlayer = sBank };
Check(sBridge.GetPlayerVar('S', 0, 5).AsInt() == -1,
    "GetS(S,0,5) is rejected (0x6DF1BE/0x6DF1C2 `test`/`jle`)");
Check(!sBridge.SetPlayerVar('S', 0, 5, PasValue.FromInt(7)),
    "SetS(S,0,5) is refused (0x6DF251/0x6DF255 `test`/`jle`)");
Check(sBank.m_ScriptSVars.Count == 0,
    "a group-0 S access did not fall through to the keyed dictionary");

Console.WriteLine("QST-11-A6: the keyed path is still reachable for group > 0");
var keyed = NewPlayer();
var keyedBridge = new PasApiBridge { CurrentPlayer = keyed };
Check(keyedBridge.SetPlayerVar('V', 3, 4, PasValue.FromInt(77)),
    "SetV(V,3,4) succeeded on the keyed path");
Check(keyed.m_ScriptVVars.TryGetValue(3 * 1000 + 4, out var keyedValue) && keyedValue == 77,
    "SetV(V,3,4) filed under group*1000+index (sub_6E42CC `imul eax,edx,0x3E8`)");
Check(keyedBridge.GetPlayerVar('V', 3, 4).AsInt() == 77, "GetV(V,3,4) read it back");
Check(keyedBridge.GetPlayerVar('V', 3, 5).AsInt() == -1,
    "a keyed miss returns the -1 seeded at 0x6DF1F1");

Console.WriteLine("QST-11-A7: the native EAs stay documented at the implementation");
var basePath = FindRepositoryRoot();
var targetFile = Path.Combine(basePath, "GameSvr", "ScriptSystem", "PasEngine",
    "PasApiBridge.cs");
if (!File.Exists(targetFile))
{
    Console.WriteLine($"  FAIL: File not found: {targetFile}");
    failures++;
}
else
{
    var content = File.ReadAllText(targetFile);
    Check(content.Contains("0x6DF20F", StringComparison.Ordinal),
        "inline read EA 0x6DF20F is cited");
    Check(content.Contains("0x6DF2A8", StringComparison.Ordinal),
        "inline write EA 0x6DF2A8 is cited");
}

Console.WriteLine();
if (failures == 0)
{
    Console.WriteLine("QST-11 AUDIT PASS: group-0 V inline window verified end to end");
    return 0;
}

Console.WriteLine($"QST-11 AUDIT FAIL: {failures} assertion(s) failed");
return 1;

static string FindRepositoryRoot()
{
    foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
    {
        for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "GameSvr", "GameSvr.csproj")))
                return dir.FullName;
        }
    }

    throw new DirectoryNotFoundException("repository root not found");
}

static void PrepareRuntimeConfig()
{
    // M2Share's static constructor resolves !Setup.txt against
    // AppContext.BaseDirectory and IniFile.Load throws when it is absent, so the
    // first `new TPlayObject()` would abort the run before any assertion.
    var runtimeDirectory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(runtimeDirectory, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
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
