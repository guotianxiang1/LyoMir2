using GameSvr;
using SystemModule;

// PKD-08 / PKD-09 —— 可攻击性判定链的契约审计。
//
// 所有断言都钉在战神字节上（M2Server flat_image.bin，ImageBase 0x400000），而不是钉在
// 它守护的 C# 上，所以两个方向的回归都会 FAIL。
//
//  A. sub_767498 —— 全引擎唯一的通用可攻击性入口（全镜像 E8 直调 169 处），
//     九道硬门之后才调虚槽 [vmt+0x20]（= C# IsAttackTarget）：
//        7674A3  je   ; target = nil
//        7674AE  jne  ; sub_772DA8 = `8A 40 74 C3` = m_boDeath 取值器
//        7674B4  jne  ; [target+0x73] = m_boGhost（唯一写入点 0x7680EF，在 MakeGhost 里；
//                     ;   m_boDeath 是 +0x74，两份旧 discovery 文档把这两个写反了）
//        7674B8  je   ; self = target
//        7674C1  jne  ; [target+0x2E0] = m_boAdminMode
//        7674CA  jne  ; [target+0x2E5] = m_boStoneMode
//        7674D8  jne  ; [target+0x128] != [self+0x128]  不同地图
//        7674E5  jne  ; sub_772960(dl=0x34) 状态 52
//        7674F1  jae  ; add al,0x10 / sub al,2  => 种族 240/241 被拒
//
//  B. sub_7671F0（TCreature 的 [vmt+0x20]）主人分支收尾的两道门：
//        76736F  8B 45 FC              mov eax,[ebp-4]      ; 责任玩家 self.[vmt+0xB4]()
//        767372  3B B0 B0 0B 00 00     cmp esi,[eax+0xBB0]  ; 主人的英雄
//        767378  75 02 / 33 DB         jne / xor ebx,ebx
//        76737C  3B B7 8C 03 00 00     cmp esi,[edi+0x38C]  ; self.m_Master
//        767382  75 02 / 33 DB         jne / xor ebx,ebx
//     以及它前面那道已经存在、不许被挪走的主人休息位门：
//        767337  80 B8 C7 04 00 00 00  cmp byte [ebp-4 -> +0x4C7],0 / 74 02 / 33 DB
//
// A 段是**行为驱动**的：真的构造两个 AnimalObject，先建立一个为真的阳性对照
// （m_boNastyMode 走 0x7671F0 的 [+0x2EB] 等价支），再逐门把它翻成假。
// 阳性对照本身就是断言，所以任何一门写死成 return false 都会被 A0 抓到，
// 不会出现「全部 PASS 其实是空跑」的假绿。

var failures = new List<string>();
void Check(bool cond, string msg)
{
    if (cond) { Console.WriteLine("  PASS  " + msg); return; }
    failures.Add(msg);
    Console.WriteLine("  FAIL  " + msg);
}

Console.WriteLine("== A: sub_767498 的九道前置门（行为驱动）==");

// 与 InProcItemConservationCheck / DeathDropPolicyCheck 相同的最小引导：
// 只让 M2Share 的静态构造器跑起来，不起网络、不连库、不开后台线程。
PrepareConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.RandomNumber = RandomNumber.GetInstance();
M2Share.ObjectManager = new ObjectManager();

AnimalObject NewActor()
{
    var a = new AnimalObject();
    a.m_btRaceServer = Grobal2.RC_ANIMAL;   // 0x764E5F mov byte [esi+0x178],0x32
    return a;
}

// 阳性对照。攻击方开 NastyMode 后 IsAttackTarget 对同族目标返回 true
// （对应 0x767469 `cmp byte [edi+0x2EB],0 / jne -> bl:=1` 那条恒真支），
// 于是九道门里任何一门生效都能被单独观测到。两个对象的 m_PEnvir 都是 null，
// 所以 0x7674D8 的同图门在对照里是通过的。
var attacker = NewActor();
attacker.m_boNastyMode = true;
var target = NewActor();
Check(attacker.IsProperTarget(target),
    "A0 阳性对照：NastyMode 攻击方对同图存活同族目标必须可攻击（否则下面八条全是空跑）");

Check(!attacker.IsProperTarget(null),
    "A1 0x7674A1 test esi,esi / 74 4E je: target = nil -> 不可攻击");

Check(!attacker.IsProperTarget(attacker),
    "A2 0x7674B6 3B FE cmp edi,esi / 74 39 je: self = target -> 不可攻击");

var deadTarget = NewActor();
deadTarget.m_boDeath = true;
Check(!attacker.IsProperTarget(deadTarget),
    "A3 0x7674A5 call sub_772DA8 (= mov al,[eax+0x74]) / 75 43 jne: 目标已死 -> 不可攻击");

var ghostTarget = NewActor();
ghostTarget.m_boGhost = true;
Check(!attacker.IsProperTarget(ghostTarget),
    "A4 0x7674B0 cmp byte [esi+0x73],0 / 75 3D jne: 目标是幽灵 -> 不可攻击（+0x73 是 m_boGhost，不是 m_boDeath）");

var adminTarget = NewActor();
adminTarget.m_boAdminMode = true;
Check(!attacker.IsProperTarget(adminTarget),
    "A5 0x7674BA cmp byte [esi+0x2E0],0 / 75 30 jne: 目标处于管理员模式 -> 不可攻击");

var stoneTarget = NewActor();
stoneTarget.m_boStoneMode = true;
Check(!attacker.IsProperTarget(stoneTarget),
    "A6 0x7674C3 cmp byte [esi+0x2E5],0 / 75 27 jne: 目标处于石化模式 -> 不可攻击");

// 0x7674E7 add al,0x10 / 0x7674EF sub al,2 / 0x7674F1 jae —— Delphi 的 `x in [240,241]`。
// dec/sub 之后看借位：只有 (race-0xF0) <u 2 的两个值落进 xor eax,eax。
var race240 = NewActor();
race240.m_btRaceServer = 240;
Check(!attacker.IsProperTarget(race240),
    "A7 0x7674F1 jae: 种族 240 -> 不可攻击");
var race241 = NewActor();
race241.m_btRaceServer = 241;
Check(!attacker.IsProperTarget(race241),
    "A8 0x7674F1 jae: 种族 241 -> 不可攻击");
// 反向锁：范围只有两个值，239 与 242 必须仍可攻击，否则说明有人把它读成了「>= 240」。
var race239 = NewActor();
race239.m_btRaceServer = 239;
Check(attacker.IsProperTarget(race239),
    "A9 0x7674EF sub al,2: 种族 239 不在拒绝集内（区间只有 240/241，不是 >= 240）");
var race242 = NewActor();
race242.m_btRaceServer = 242;
Check(attacker.IsProperTarget(race242),
    "A10 0x7674EF sub al,2: 种族 242 不在拒绝集内");

Console.WriteLine();
Console.WriteLine("== B: 源码契约（同图门 / 状态 52 门 / 主人族三道门）==");

var gateSource = ReadRepoFile("GameSvr/Actors/TBaseObject.NativeProperTargetGate.cs");
var actorSource = ReadRepoFile("GameSvr/Actors/TBaseObject.cs");

// 同图门与状态 52 门无法在不构造 Envirnoment / 状态表的前提下行为驱动，
// 这里钉住它们确实存在于唯一入口里，并连同 EA 一起写进失败文本。
Check(gateSource.Contains("BaseObject.m_PEnvir != m_PEnvir", StringComparison.Ordinal),
    "B1 0x7674CC/0x7674D2 cmp [esi+0x128],[edi+0x128] / 75 19 jne: 不同地图 -> 不可攻击");
Check(gateSource.Contains("HasNativeActiveState(", StringComparison.Ordinal)
        && gateSource.Contains("NativeProperTargetBlockedState = 52", StringComparison.Ordinal),
    "B2 0x7674DA mov dl,0x34 / call sub_772960 / 75 0C jne: 目标带状态 52 -> 不可攻击");

// 九道门必须挂在 IsProperTarget 这个唯一入口上，而不是再次散回调用点。
var preGateCall = actorSource.IndexOf("NativeProperTargetPreGate(BaseObject)", StringComparison.Ordinal);
var isAttackCall = actorSource.IndexOf("bool result = IsAttackTarget(BaseObject);", StringComparison.Ordinal);
Check(preGateCall > 0 && isAttackCall > 0 && preGateCall < isAttackCall,
    "B3 0x7674F7 FF 51 20 call [ecx+0x20]: 九道门必须排在虚槽 IsAttackTarget 之前");

// PKD-09 的两道门。
Check(actorSource.Contains("masterOfSlave.m_HeroObject", StringComparison.Ordinal),
    "B4 0x767372 cmp esi,[master+0xBB0] / 767378 jne / 76737A xor ebx,ebx: 宠物不打主人的英雄");
Check(actorSource.Contains("ReferenceEquals(BaseObject, m_Master)", StringComparison.Ordinal),
    "B5 0x76737C cmp esi,[edi+0x38C] / 767382 jne / 767384 xor ebx,ebx: 宠物不打主人本人");

// 反向锁：主人休息位那道门原本就在，位置在英雄门之前，不许被这次改动挪走或删掉。
var slaveRelax = actorSource.IndexOf("m_Master.m_boSlaveRelax", StringComparison.Ordinal);
var heroGate = actorSource.IndexOf("masterOfSlave.m_HeroObject", StringComparison.Ordinal);
Check(slaveRelax > 0 && heroGate > 0 && slaveRelax < heroGate,
    "B6 0x767337 [+0x4C7] 主人休息位门必须仍在，且排在 0x767372 英雄门之前");

Console.WriteLine();
if (failures.Count == 0)
{
    Console.WriteLine("NativeProperTargetGateCheck: PASS");
    return 0;
}
Console.WriteLine($"NativeProperTargetGateCheck: FAIL ({failures.Count})");
foreach (var f in failures) Console.WriteLine("  - " + f);
return 1;

static string ReadRepoFile(string relative)
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LyoMir2.sln")))
    {
        dir = dir.Parent;
    }
    if (dir == null)
    {
        throw new InvalidOperationException("找不到仓库根 (LyoMir2.sln)");
    }
    return File.ReadAllText(Path.Combine(dir.FullName, relative));
}

// 最小配置引导（沿用 AuditTools/InProcItemConservationCheck 的写法，只写进本审计自己的
// bin 目录）。
static void PrepareConfig()
{
    var baseDir = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(baseDir, "!Setup.txt"), "[Server]\r\n");
    File.WriteAllText(Path.Combine(baseDir, "String.ini"), "[String]\r\n");
    File.WriteAllText(Path.Combine(baseDir, "Command.conf"), "[Command]\r\n");
    // M2Share's static ctor also builds ExpsConfig from ..\Share\PlayerUpgradeExp.ini.
    // Leaving those out only looked harmless: IniFile.Load creates a 0-byte file and
    // returns when the file is missing (IniFile.cs:203-206), so the FIRST run passed
    // and every run after it threw on the now-present-but-empty file (IniFile.cs:281,
    // ConfigCount <= 0). Write them the way every other in-process audit does.
    var shareDir = Path.GetFullPath(Path.Combine(baseDir, "..", "Share"));
    Directory.CreateDirectory(shareDir);
    File.WriteAllText(Path.Combine(shareDir, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]\r\n");
    File.WriteAllText(Path.Combine(shareDir, "ServerData.ini"), "[Integer]\r\n");
}
