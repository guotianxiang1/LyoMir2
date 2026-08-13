// NativeLevelExpTableCheck — locks PlayerUpgradeExp.ini loading and GetLevelExp
// to 战神 sub_6AFCC8 / sub_6884C0.
//
// Native:
//   Player 0x6AFCC8: index > Count -> 0xFD51DA80 (0x6AFCF5 B8 80 DA 51 FD)
//   Hero   0x6884C0: level < 1 or > 183 -> same sentinel (0x688520 BE 80 DA 51 FD)
//   INI section is [PlayerLevelExp] only (string at 0x651530). [PlayerLevelExpRate]
//   is 0 hits ASCII/GBK/UTF-16LE. Production LEVEL_80..99 = 4250000000 = 0xFD51DA80,
//   which int.TryParse cannot read; the old loader fell through to Rate=38.
//   Level-up VMT+0x240 (0x6BA15C) looks up edx = previous level, not current.

using System.Text;
using GameSvr;
using GameSvr.Configs;
using SystemModule;

var assertions = 0;
var failures = new List<string>();

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();

CheckLargeIniValuesLoadAsUnsignedDwords();
CheckRateSectionIsIgnored();
CheckGetLevelExpOobReturnsSentinel();
CheckGetLevelExpInRangeReturnsLoadedValue();
CheckHasLevelUpLooksUpPreviousLevel();

if (failures.Count > 0)
{
    Console.WriteLine($"NativeLevelExpTableCheck FAIL ({failures.Count} of {assertions})");
    foreach (var failure in failures) Console.WriteLine("  " + failure);
    return 1;
}

Console.WriteLine($"NativeLevelExpTableCheck PASS ({assertions} assertions)");
Console.WriteLine("  [PlayerLevelExp] LEVEL_N parsed as uint32 (4250000000 = 0xFD51DA80)");
Console.WriteLine("  [PlayerLevelExpRate] is not consumed (native 0 hits)");
Console.WriteLine("  GetLevelExp OOB -> 0xFD51DA80 (0x6AFCF5 / 0x688520)");
Console.WriteLine("  HasLevelUp looks up previous level (0x6C0543 dec edx / 0x6BA15C)");
return 0;

void CheckLargeIniValuesLoadAsUnsignedDwords()
{
    var path = WriteIni(
        "[PlayerLevelExp]" + Environment.NewLine +
        "LEVEL_1=100" + Environment.NewLine +
        "LEVEL_80=4250000000" + Environment.NewLine +
        "LEVEL_99=4250000000" + Environment.NewLine +
        "[PlayerLevelExpRate]" + Environment.NewLine +
        "LEVEL_1=20" + Environment.NewLine +
        "LEVEL_80=38" + Environment.NewLine);
    var loader = new ExpsConfig(path);
    loader.LoadConfig();
    Equal(100, M2Share.g_Config.dwNeedExps[1],
        "LEVEL_1=100 loads as 100 (not Rate 20)");
    Equal(unchecked((int)4250000000u), M2Share.g_Config.dwNeedExps[80],
        "LEVEL_80=4250000000 loads as 0xFD51DA80, not Rate 38");
    Equal(unchecked((int)4250000000u), M2Share.g_Config.dwNeedExps[99],
        "LEVEL_99=4250000000 loads as 0xFD51DA80");
    Equal(99, M2Share.g_Config.nNeedExpMaxLevel,
        "nNeedExpMaxLevel tracks the highest LEVEL_N key");
}

void CheckRateSectionIsIgnored()
{
    M2Share.g_Config = new GameSvrConfig();
    var path = WriteIni(
        "[PlayerLevelExpRate]" + Environment.NewLine +
        "LEVEL_1=20" + Environment.NewLine +
        "LEVEL_2=22" + Environment.NewLine);
    var loader = new ExpsConfig(path);
    loader.LoadConfig();
    Equal(0, M2Share.g_Config.dwNeedExps[1],
        "Rate-only INI must not write dwNeedExps[1]");
    Equal(0, M2Share.g_Config.nNeedExpMaxLevel,
        "Rate-only INI leaves nNeedExpMaxLevel at 0");
}

void CheckGetLevelExpOobReturnsSentinel()
{
    M2Share.g_Config = new GameSvrConfig();
    M2Share.g_Config.dwNeedExps[1] = 100;
    M2Share.g_Config.nNeedExpMaxLevel = 1;
    var actor = new TBaseObject();
    Equal(TBaseObject.NativeNeedExpSentinel, actor.GetLevelExp(2),
        "level > Count returns 0xFD51DA80 (0x6AFCDD ja / 0x6AFCF5)");
    Equal(TBaseObject.NativeNeedExpSentinel, actor.GetLevelExp(-1),
        "negative level returns the same sentinel");
    Equal(TBaseObject.NativeNeedExpSentinel, actor.GetLevelExp(1000),
        "index past dwNeedExps length returns the sentinel, not the last slot");
}

void CheckGetLevelExpInRangeReturnsLoadedValue()
{
    M2Share.g_Config = new GameSvrConfig();
    M2Share.g_Config.dwNeedExps[77] = 34567890;
    M2Share.g_Config.nNeedExpMaxLevel = 77;
    var actor = new TBaseObject();
    Equal(34567890, actor.GetLevelExp(77),
        "in-range lookup returns the loaded dword");
    // Tests that poke dwNeedExps[n] without bumping nNeedExpMaxLevel still work
    // when the slot is non-zero (HeroRuntimeCodecCheck pattern).
    M2Share.g_Config.nNeedExpMaxLevel = 0;
    Equal(34567890, actor.GetLevelExp(77),
        "non-zero poked slot is returned even if nNeedExpMaxLevel is 0");
}

void CheckHasLevelUpLooksUpPreviousLevel()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepoRoot(), "GameSvr", "Actors", "TBaseObject.cs"));
    var idx = source.IndexOf("public void HasLevelUp(int nLevel)", StringComparison.Ordinal);
    True(idx >= 0, "HasLevelUp is still on TBaseObject");
    var body = source.Substring(idx, 500);
    True(body.Contains("GetLevelExp(nLevel)"),
        "HasLevelUp must call GetLevelExp(nLevel) = previous (0x6BA15C edx)");
    True(!body.Contains("GetLevelExp(m_Abil.Level)"),
        "HasLevelUp must not look up the already-incremented level");
}

string WriteIni(string contents)
{
    var path = Path.Combine(Path.GetTempPath(),
        "NativeLevelExpTableCheck-" + Guid.NewGuid().ToString("N") + ".ini");
    File.WriteAllText(path, contents, Encoding.ASCII);
    return path;
}

string FindRepoRoot()
{
    // The shared build drops the exe outside the worktree, so the walk has to start
    // from the working directory too, not just the runtime directory.
    foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
    {
        var dir = new DirectoryInfo(start);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "GameSvr", "Actors", "TBaseObject.cs")))
                return dir.FullName;
            dir = dir.Parent;
        }
    }
    return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
}

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
    // TBaseObject.cs:907 ends every construction with
    // M2Share.ObjectManager.RegisterConstructed(this); production sets that field
    // in GameApp.cs:564 long before the first actor exists, so an in-process
    // harness has to stand one up itself (same as YanshenMonsterAttrCheck).
    M2Share.ObjectManager ??= new ObjectManager();
}

void Equal<T>(T expected, T actual, string name)
{
    assertions++;
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        failures.Add($"{name}: expected={expected}, actual={actual}");
}

void True(bool condition, string name)
{
    assertions++;
    if (!condition) failures.Add(name);
}
