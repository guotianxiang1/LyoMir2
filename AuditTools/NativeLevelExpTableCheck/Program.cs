// NativeLevelExpTableCheck — locks PlayerUpgradeExp.ini loading and GetLevelExp
// to 战神 sub_6AFCC8 / sub_6884C0.
//
// Native:
//   Player 0x6AFCC8: index > Count -> 0xFD51DA80 (0x6AFCF5 B8 80 DA 51 FD)
//   Hero   0x6884C0: level < 1 or > 183 -> same sentinel (0x688520 BE 80 DA 51 FD)
//   INI section is [PlayerLevelExp] only (string at 0x651530). [PlayerLevelExpRate]
//   is 0 hits ASCII/GBK/UTF-16LE. Production LEVEL_80..99 = 4250000000 = 0xFD51DA80,
//   which int.TryParse cannot read; the old loader fell through to Rate=38.
//   Level-up VMT+0x240 is sub_6BDBA0, and it re-reads the already-incremented
//   level from the object (0x6BDBC5 movzx edx,word [ebx+0x278]) rather than the
//   edx the caller passed. sub_6BA140 is NOT that slot: it has zero dword
//   references anywhere in the image.

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
CheckHasLevelUpLooksUpCurrentLevel();

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
    Console.WriteLine("  HasLevelUp looks up the incremented level (VMT+0x240 = sub_6BDBA0 @0x6BDBC5)");
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

// 这条断言原来钉的是「HasLevelUp 必须查前一等级」，依据是 sub_6BA140。归属搞错了：
// sub_6BA140（及孪生 sub_6BA7BC）在全镜像 dword 引用数为 0，不在任何 VMT 里，写的
// 还是 [edi+0x1E8+0x5C]=+0x244 的另一套布局。真正装在 VMT+0x240 的是 sub_6BDBA0
// （dword 引用只有 0x0062F1CC 与 0x006ACB08，减 0x240 得 0x0062EF8C / 0x006AC8C8，
// 两张都是 145/145 全代码指针的 VMT），它**忽略** edx，直接重读已自增的等级：
//   0x006BDBC5  0f b7 93 78 02 00 00  movzx edx,word [ebx+0x278]
//   0x006BDBCE  e8 f5 20 ff ff        call 0x6AFCC8   (GetLevelExp)
//   0x006BDBD3  89 83 c0 02 00 00     mov [ebx+0x2C0],eax  (MaxExp)
// 0x006C0543 的 `dec edx` 只影响转发给 sub_73EE14 的第二个参数。
void CheckHasLevelUpLooksUpCurrentLevel()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepoRoot(), "GameSvr", "Actors", "TBaseObject.cs"));
    var idx = source.IndexOf("public void HasLevelUp(int nLevel)", StringComparison.Ordinal);
    True(idx >= 0, "HasLevelUp is still on TBaseObject");
    var body = source.Substring(idx, 1600);
    True(body.Contains("m_Abil.MaxExp = GetLevelExp(m_Abil.Level);"),
        "HasLevelUp must look up the already-incremented level (0x6BDBC5 movzx edx,[ebx+0x278])");
    True(!body.Contains("GetLevelExp(nLevel)"),
        "HasLevelUp must not look up the caller's previous level (sub_6BA140 is not the VMT+0x240 slot)");
}

string WriteIni(string contents)
{
    var path = Path.Combine(Path.GetTempPath(),
        "NativeLevelExpTableCheck-" + Guid.NewGuid().ToString("N") + ".ini");
    File.WriteAllText(path, contents, Encoding.ASCII);
    return path;
}

// 用共享助手而非就地实现：AuditRepoRoot.Resolve() 依次试 argv[0]、
// M2_REPO_ROOT / LYOMIR_REPO_ROOT 环境变量，再从 [CallerFilePath] 目录、
// CurrentDirectory、AppContext.BaseDirectory 三处向上走。它是"从 CWD 与
// BaseDirectory 两处起走"的超集，且 [CallerFilePath] 对"共享构建把 exe
// 落在工作树之外"这一情形最稳。
string FindRepoRoot() => AuditRepoRoot.Resolve();

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
    // 用 ??= 而非 = ：不覆盖已存在的实例。此前该工具就是死在这个空引用上，
    // 崩在第一条断言之前，所以它钉死的错误契约一直没被发现。
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
