using GameSvr;

// PKD-11 —— 地面掉落物的归属过期契约。
//
// 战神 sub_783988（唯一调用点 0x77A476，在地图格 tick sub_77A178 里）逐字节：
//
//   783988  83 B8 F4 00 00 00 00  cmp dword [item+0xF4],0
//   78398F  75 09                 jne 0x78399A
//   783991  83 B8 F8 00 00 00 00  cmp dword [item+0xF8],0
//   783998  74 4D                 je  0x7839E7      ; 两个归属槽都空 -> 直接返回
//   78399A  2B 50 08              sub edx,[item+8]  ; edx = now - 落地 tick
//   78399D  81 FA C0 D4 01 00     cmp edx,0x1D4C0   ; 120000 ms
//   7839A3  76 12                 jbe 0x7839B7      ; <= 120000 -> 保留（严格 > 才过期）
//   7839A5  33 D2 / 89 90 F4 ..   [item+0xF4] := 0
//   7839AD  33 D2 / 89 90 F8 ..   [item+0xF8] := 0  ; 两槽同时清零 -> 变成公共物
//   7839B5  EB 30                 jmp 0x7839E7
//   7839B7  8B 90 F4 00 00 00     mov edx,[item+0xF4]
//   7839BD  85 D2 / 74 0E         test edx,edx / je 0x7839CF
//   7839C1  80 7A 73 00           cmp byte [edx+0x73],0     ; owner.m_boGhost
//   7839C5  74 08                 je  0x7839CF
//   7839C7  33 D2 / 89 90 F4 ..   [item+0xF4] := 0
//   7839CF..7839E1                对 [item+0xF8] 重复同一段
//
// 两条契约：
//  1. 过期窗口是 120000 ms，且是**严格大于**（`jbe` 跳过 => <= 保留）。
//  2. 归属人提前作废的判据是 **m_boGhost**（+0x73），不是 m_boDeath（+0x74）。
//     +0x73 全镜像唯一写入点 0x7680EF，在 MakeGhost sub_768060 里，且从不写 0；
//     +0x74 的写入点之一是 0x766323，TCreature.Die sub_76631C 的第一条语句。
//     写成 m_boDeath 会把归属提前作废整整一个尸体周期（原生尸体 60 秒才变幽灵），
//     击杀者一死，脚下战利品立刻变公共。
//
// 落地侧的补充事实（供交叉阅读，不在此断言）：玩家死亡的两个 worker
// sub_73FC70 / sub_740078 调 DropItemDown sub_7688A0 时，第二个栈参恒为 0
// （0x73FEDF `6A 00`、0x740229 `6A 00`），而 [vmt+0x2C] = sub_7839E8 @0x783A62
// `mov [ebx+0xF4],esi` 正是拿这个参数当归属人 —— 所以**玩家死亡爆的东西原生没有
// 归属人，落地即公共**；只有怪物掉落才带归属。

var failures = new List<string>();
void Check(bool cond, string msg)
{
    if (cond) { Console.WriteLine("  PASS  " + msg); return; }
    failures.Add(msg);
    Console.WriteLine("  FAIL  " + msg);
}

// 构造 GameSvrConfig 会拉起 M2Share 的静态构造器（它要读配置文件），
// 沿用 InProcItemConservationCheck / DeathDropPolicyCheck 的最小引导。
PrepareConfig();

var cfg = new GameSvrConfig();
Check(cfg.dwFloorItemCanPickUpTime == 120000,
    "0x78399D cmp edx,0x1D4C0: 归属保留窗口 = 120000 ms");

foreach (var rel in new[] { "GameSvr/Players/TPlayObject.Base.cs",
                            "GameSvr/RobotPlay/RobotPlayObject.Base.cs" })
{
    var src = ReadRepoFile(rel);
    var expiry = src.IndexOf("MapItem.CanPickUpTick", StringComparison.Ordinal);
    Check(expiry > 0, $"{rel}: 找得到地面物归属过期段");
    if (expiry <= 0) continue;
    // 窗口收到本段结构末尾（下一个 OS_EVENTOBJECT 分支）为止，而不是数字符数：
    // 两处实现都在过期段里加了证据注释，1600 字符已经够不着 m_boGhost 那一行。
    var windowEnd = src.IndexOf("CellType.OS_EVENTOBJECT", expiry, StringComparison.Ordinal);
    if (windowEnd < 0) windowEnd = Math.Min(src.Length, expiry + 4000);
    var window = src[expiry..windowEnd];

    Check(window.Contains("> M2Share.g_Config.dwFloorItemCanPickUpTime", StringComparison.Ordinal),
        $"{rel}: 0x7839A3 jbe => 严格大于才清归属，写成 >= 会早清一 tick");

    // 主断言：判据必须是 m_boGhost。
    Check(window.Contains(".m_boGhost", StringComparison.Ordinal),
        $"{rel}: 0x7839C1 cmp byte [owner+0x73],0 => 归属作废判据是 m_boGhost");
    // 反向锁：m_boDeath(+0x74) 是另一个字段，出现在这一段里就是回归。
    Check(!window.Contains(".m_boDeath", StringComparison.Ordinal),
        $"{rel}: +0x74 m_boDeath 不参与归属过期（这一段出现 m_boDeath 即为回归）");
}

Console.WriteLine();
if (failures.Count == 0)
{
    Console.WriteLine("NativeFloorItemOwnerExpiryCheck: PASS");
    return 0;
}
Console.WriteLine($"NativeFloorItemOwnerExpiryCheck: FAIL ({failures.Count})");
foreach (var f in failures) Console.WriteLine("  - " + f);
return 1;

// 最小配置引导（只写进本审计自己的 bin 目录）。
static void PrepareConfig()
{
    var baseDir = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(baseDir, "!Setup.txt"), "[Server]\r\n");
    File.WriteAllText(Path.Combine(baseDir, "String.ini"), "[String]\r\n");
    File.WriteAllText(Path.Combine(baseDir, "Command.conf"), "[Command]\r\n");
}

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
