// NativeKillExpDecayCheck -- locks TBaseObject.CalcGetExp to 战神 sub_6C02A4.
//
// Why this audit exists. CalcGetExp had THREE divergences at once, and each one
// was invisible to the other audits because nothing asserted on this function:
//
//   1. THRESHOLD 18, NOT 10. 0x6C02C7 `lea edx,[edi+0x12]` and 0x6C02E5
//      `add edi,0x12`. The value 10 belongs to the TWIN function sub_728124
//      (0x728138 `lea eax,[esi+0xA]`), which is the GROUP-split level-gap
//      penalty. The two functions are otherwise the same shape and both divide
//      by float 15.0 (0x6C0314 and 0x728180 are both 15.0), so landing on the
//      wrong one is easy and silent. With 10, every kill whose level gap fell
//      in 10..17 computed a penalty that native does not apply at all.
//
//   2. THE BYPASS IS A BALANCE, NOT A CONFIG BOOL. 0x6C02B7
//      `cmp dword [ebx+0xBD0],0` / `jg`. obj+0xBD0 is credited in seconds by
//      TAntiDecExpProp.Use = sub_7865B4 (0x7865EE `imul esi,eax,0xE10`,
//      0x7865F4 `add [edi+0xBD0],esi`, refused above 0x7A1200 at 0x7865D2).
//      The old code read g_Config.boHighLevelKillMonFixExp, whose config key
//      'HighLevelKillMonFixExp' has ZERO hits in the whole image while the
//      same search method finds 'TDoubleExpProp' twice (positive control).
//      A global bool cannot express "this character has N seconds left".
//
//   3. NON-POSITIVE INPUT RETURNS 0, NOT 1. 0x6C02B3 `test esi,esi` /
//      `jle 0x6C0308` -> `xor eax,eax`. The old code had no zero guard, so a
//      non-positive input fell through to the `result <= 0 -> 1` floor and
//      returned 1 -- inventing a point of experience.
//
// Attribution note: sub_6C0148 (the caller) is a TPlayer virtual, slot +0xB0.
// All 593 SelfPtr-verified VMTs in 0x600000..0x7E0000 were scanned and only
// TPlayer and TGdMsgGMAgent hold it. That is why the +0xBD0 balance is exposed
// through a virtual that defaults to 0 on non-players rather than being read
// off TBaseObject directly.

using System.Reflection;
using GameSvr;
using SystemModule;

// TPlayObject's constructor chain touches M2Share's static initialiser, which
// loads ini files from AppContext.BaseDirectory. Same prep the other audits do.
PrepareRuntimeConfig();
InitializeRuntime();

var assertions = 0;
var failures = new List<string>();

CheckThresholdIs18();
CheckThresholdIsNot10();
CheckDivisorIs15();
CheckNonPositiveInputReturnsZero();
CheckFloorIsOne();
CheckBalanceBypass();
CheckBypassIsBalanceNotConfigBool();
CheckNonPlayerBalanceIsZero();
CheckBoundaryAtExactThreshold();
CheckGroupTwinStillUses10();

if (failures.Count > 0)
{
    Console.WriteLine($"NativeKillExpDecayCheck FAIL ({failures.Count} of {assertions})");
    foreach (var failure in failures) Console.WriteLine("  " + failure);
    return 1;
}

Console.WriteLine($"NativeKillExpDecayCheck PASS ({assertions} assertions)");
Console.WriteLine("  sub_6C02A4 threshold +0x12=18 (0x6C02C7/0x6C02E5), divisor float 15.0 (0x6C0314)");
Console.WriteLine("  bypass = obj+0xBD0 SECONDS balance > 0 (0x6C02B7), credited by sub_7865B4 (0x7865F4)");
Console.WriteLine("  nExp <= 0 -> 0 (0x6C02B3 jle -> 0x6C0308 xor eax,eax), else floor 1 (0x6C0301)");
Console.WriteLine("  twin sub_728124 keeps threshold +0xA=10 (0x728138) for the GROUP path");
return 0;

// ---------------------------------------------------------------- assertions

void CheckThresholdIs18()
{
    // 0x6C02C7 lea edx,[edi+0x12]. Killer level exactly 17 above the victim is
    // still BELOW nLevel+18, so no penalty applies and exp passes through.
    var player = NewPlayer(level: 117);
    Equal(1000, player.CalcGetExp(100, 1000),
        "gap 17 < 18 must pass exp through unchanged (0x6C02CE)");

    // At a gap of 18 the penalty engages: 1000 - Round(1000/15.0 * 0) = 1000,
    // but the multiplier term is (level - (nLevel+18)) == 0, so still 1000.
    // Use gap 19 for a visible deduction: Round(1000/15.0 * 1) = 67.
    var deducting = NewPlayer(level: 119);
    Equal(1000 - HUtil32.Round(1000 / 15.0 * 1), deducting.CalcGetExp(100, 1000),
        "gap 19 deducts exactly one 15th step (0x6C02D2..0x6C02FB)");

    // The assertions above are satisfied by a threshold of 19 as well, because
    // each one only pins the deduction RELATIVE to its own gap. Pin the
    // absolute step count so the threshold cannot slide: at level 123 vs
    // victim 100 native multiplies by (123 - 118) = 5. A threshold of 19 would
    // multiply by 4 and a threshold of 17 by 6.
    //
    // Keep the gap SMALL. My first attempt used level 150, where the deduction
    // (32 steps of 1/15) far exceeds nExp, so native's floor-1 fires at
    // 0x6C0301 and every candidate threshold collapses to the same answer of 1
    // -- the assertion was unfalsifiable and failed on the correct code.
    var wideGap = NewPlayer(level: 123);
    var expectedFiveSteps = 1000 - HUtil32.Round(1000 / 15.0 * 5);
    True(expectedFiveSteps > 0,
        "the probe gap must stay above the floor-1 clamp to be falsifiable");
    Equal(expectedFiveSteps, wideGap.CalcGetExp(100, 1000),
        "gap 23 deducts exactly 5 steps -- pins the threshold absolutely");
    NotEqual(1000 - HUtil32.Round(1000 / 15.0 * 4), wideGap.CalcGetExp(100, 1000),
        "4 steps would mean threshold 19");
    NotEqual(1000 - HUtil32.Round(1000 / 15.0 * 6), wideGap.CalcGetExp(100, 1000),
        "6 steps would mean threshold 17");

    // Same idea on the pass-through side: only a threshold of exactly 18 makes
    // gap 17 free AND gap 18 the first penalised gap.
    Equal(1000, NewPlayer(level: 117).CalcGetExp(100, 1000),
        "gap 17 free (threshold not 17)");
    var atNineteen = NewPlayer(level: 119).CalcGetExp(100, 1000);
    NotEqual(1000, atNineteen,
        "gap 19 must already deduct -- 1000 here would mean threshold 19+");
}

void CheckThresholdIsNot10()
{
    // This is the regression guard. With the old constant 10, a killer 12
    // levels above its victim lost exp. Native does not touch it until 18.
    var player = NewPlayer(level: 112);
    Equal(1000, player.CalcGetExp(100, 1000),
        "gap 12 must NOT deduct -- 10 is the twin sub_728124's constant (0x728138)");

    var wouldHaveDeducted = 1000 - HUtil32.Round(1000 / 15.0 * (112 - (100 + 10)));
    NotEqual(wouldHaveDeducted, player.CalcGetExp(100, 1000),
        "gap 12 must not match the threshold-10 formula");
}

void CheckDivisorIs15()
{
    // 0x6C02D8 fdiv [0x6C0314], and 0x6C0314 holds float 15.0 (raw 00 00 70 41).
    var player = NewPlayer(level: 130);
    var expected = 1000 - HUtil32.Round(1000 / 15.0 * (130 - (100 + 18)));
    Equal(expected, player.CalcGetExp(100, 1000),
        "divisor is float 15.0 at 0x6C0314");
}

void CheckNonPositiveInputReturnsZero()
{
    // 0x6C02B3 test esi,esi / jle 0x6C0308 -> xor eax,eax. Native returns 0.
    // The pre-fix code returned 1 here via the floor.
    var player = NewPlayer(level: 100);
    Equal(0, player.CalcGetExp(100, 0), "nExp == 0 -> 0 (0x6C0308)");
    Equal(0, player.CalcGetExp(100, -5), "nExp < 0 -> 0 (0x6C02B3 jle)");
    NotEqual(1, player.CalcGetExp(100, 0),
        "nExp == 0 must NOT fall through to the floor-1 path (0x6C0301)");
}

void CheckFloorIsOne()
{
    // 0x6C02FD test eax,eax / jg, else 0x6C0301 mov eax,1. A penalty large
    // enough to wipe the exp still yields 1 -- but only for POSITIVE input.
    var player = NewPlayer(level: 400);
    Equal(1, player.CalcGetExp(1, 10),
        "an over-large penalty floors at 1, not 0 (0x6C0301)");
}

void CheckBalanceBypass()
{
    // 0x6C02B7 cmp dword [ebx+0xBD0],0 / jg 0x6C02CE -- skip the whole penalty.
    var player = NewPlayer(level: 400);
    SetBalance(player, 3600);
    Equal(1000, player.CalcGetExp(100, 1000),
        "a positive obj+0xBD0 balance bypasses the penalty entirely (0x6C02B7)");

    // The test is `jg`, i.e. STRICTLY greater than zero.
    SetBalance(player, 0);
    NotEqual(1000, player.CalcGetExp(100, 1000),
        "a zero balance must NOT bypass (jg is strict)");
}

void CheckBypassIsBalanceNotConfigBool()
{
    // The old code keyed the bypass off g_Config.boHighLevelKillMonFixExp.
    // Setting that config must now have NO effect: the key string
    // 'HighLevelKillMonFixExp' has zero hits in the 战神 image, validated
    // against 'TDoubleExpProp' which the same method finds twice.
    var before = M2Share.g_Config.boHighLevelKillMonFixExp;
    try
    {
        var player = NewPlayer(level: 400);
        SetBalance(player, 0);

        M2Share.g_Config.boHighLevelKillMonFixExp = false;
        var withFlagOff = player.CalcGetExp(100, 1000);
        M2Share.g_Config.boHighLevelKillMonFixExp = true;
        var withFlagOn = player.CalcGetExp(100, 1000);

        Equal(withFlagOff, withFlagOn,
            "boHighLevelKillMonFixExp must not influence CalcGetExp (non-native key)");
        NotEqual(1000, withFlagOn,
            "the config flag must not resurrect the bypass");
    }
    finally
    {
        M2Share.g_Config.boHighLevelKillMonFixExp = before;
    }
}

void CheckNonPlayerBalanceIsZero()
{
    // sub_6C0148 is a TPlayer virtual (slot +0xB0; 593 VMTs scanned, only
    // TPlayer and TGdMsgGMAgent). Non-players therefore expose 0 and always
    // take the penalty path.
    var monster = new TBaseObject();
    Equal(0, ReadBalance(monster),
        "TBaseObject.NativeFixedExpBalanceSeconds defaults to 0");

    var player = NewPlayer(level: 100);
    SetBalance(player, 7200);
    Equal(7200, ReadBalance(player),
        "TPlayObject overrides the balance with the obj+0xBD0 field");
}

void CheckBoundaryAtExactThreshold()
{
    // At level == nLevel + 18 the `jge` at 0x6C02CC takes the penalty branch,
    // but the multiplier (level - (nLevel+18)) is 0, so the deduction is
    // Round(nExp/15.0 * 0) == 0 and the result equals nExp. Same value as the
    // pass-through branch, reached by a different path -- assert the value so a
    // future off-by-one in either direction is caught.
    var player = NewPlayer(level: 118);
    Equal(1000, player.CalcGetExp(100, 1000),
        "gap exactly 18 deducts zero (0x6C02CC jge, multiplier 0)");
}

void CheckGroupTwinStillUses10()
{
    // Guard against "fixing" the twin too. sub_728124 @0x728138
    // `lea eax,[esi+0xA]` genuinely uses 10 for the group-split path. The two
    // constants differing is CORRECT, not a drift.
    var method = typeof(TPlayObject).GetMethod(
        "NativeGroupExpLevelGapAdjust",
        BindingFlags.NonPublic | BindingFlags.Static);
    True(method != null,
        "TPlayObject.NativeGroupExpLevelGapAdjust must still exist");

    // selfLevel 100, otherLevel 110 -> otherLevel >= selfLevel + 10, so the
    // group twin DOES penalise a gap of 10, unlike the kill path.
    var penalised = (int)method.Invoke(null, new object[] { 100, 110, 1000 });
    Equal(1000, penalised,
        "group twin at gap exactly 10 deducts zero but takes the penalty branch");

    var deducted = (int)method.Invoke(null, new object[] { 100, 111, 1000 });
    Equal(1000 - HUtil32.Round(1000 / 15.0 * 1), deducted,
        "group twin uses threshold 10 (0x728138), unlike CalcGetExp's 18");
}

// ------------------------------------------------------------------ plumbing

void PrepareRuntimeConfig()
{
    var directory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(directory, "!Setup.txt"), "[Server]");
    File.WriteAllText(Path.Combine(directory, "String.ini"), "[String]");
    File.WriteAllText(Path.Combine(directory, "Command.conf"), "[Command]");
    var share = Path.GetFullPath(Path.Combine(directory, "..", "Share"));
    Directory.CreateDirectory(share);
    File.WriteAllText(Path.Combine(share, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]");
    File.WriteAllText(Path.Combine(share, "ServerData.ini"), "[Integer]");
}

void InitializeRuntime()
{
    M2Share.g_Config = new GameSvrConfig();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.ProcessMsgCriticalSection = new object();
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new System.Collections.ArrayList();
}

TPlayObject NewPlayer(int level)
{
    var player = new TPlayObject();
    player.m_Abil.Level = (ushort)level;
    player.m_boOffLineFlag = true;
    SetBalance(player, 0);
    return player;
}

void SetBalance(TPlayObject player, int seconds)
{
    typeof(TPlayObject)
        .GetField("m_nNativeTrueSightSeconds",
            BindingFlags.Public | BindingFlags.Instance)
        .SetValue(player, seconds);
}

int ReadBalance(TBaseObject actor)
{
    var property = typeof(TBaseObject).GetProperty(
        "NativeFixedExpBalanceSeconds",
        BindingFlags.NonPublic | BindingFlags.Instance);
    return (int)property.GetValue(actor);
}

void Equal<T>(T expected, T actual, string name)
{
    assertions++;
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        failures.Add($"{name}: expected={expected}, actual={actual}");
}

void NotEqual<T>(T unexpected, T actual, string name)
{
    assertions++;
    if (EqualityComparer<T>.Default.Equals(unexpected, actual))
        failures.Add($"{name}: must not equal {unexpected}");
}

void True(bool condition, string name)
{
    assertions++;
    if (!condition) failures.Add(name);
}
