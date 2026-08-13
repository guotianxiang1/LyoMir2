// 钉住眼神 盘古3「装备提升人物爆率 / _A值 / _B值」三键的原生契约。
//
// 证据来源（全部可复跑）：
//   眼神脱壳转储  D:/loym2/staging/yanshen208_strparam_runtime_dump_20260719/
//                 yanshen2_0_8_dll.memory.bin       基址 0x10000000
//   M2Server 镜像 D:/loym2/staging/_reunpack_work/flat_image.bin  基址 0x400000
//
// 宿主被覆盖处（怪物死亡散落主函数 sub_71FA20 的段2，自有掉落表循环）：
//   0071FD34 8B45E4          mov eax,[ebp-0x1C]   ; 当前 TMonItem
//   0071FD37 8B4014          mov eax,[eax+0x14]   ; MaxPoint        ← 补丁起点
//   0071FD3A F76DD4          imul dword [ebp-0x2C]; × 防沉迷倍率
//   0071FD3D E80A3ECEFF      call 0x403B4C        ; Random(eax)     ← resume
//   0071FD45 3B4210          cmp eax,[edx+0x10]   ; SelPoint
//   0071FD48 0F8F51010000    jg 0x71FE9F          ; 不掉
//
// 安装点 0x100B9F9E（builder 0x10032FD0，start=size=0x71FD37、end/resume=0x71FD3D，
// arr=[ebp-0x1F8]、n=0x2E=46）。46 个 dword 按「一 dword 装一字节」展开成 50 字节：
//   +000 8B 40 14 / F7 6D D4                回放 MaxPoint × 倍率
//   +006 81 7D F8 00 00 41 00  cmp [ebp-8],0x410000  ; 凶手是不是真对象
//   +00D 0F 82 1A 00 00 00     jb +0x2D              ; 不是 → 原样返回
//   +013 B9 <A> / F7 E9        mov ecx,A / imul ecx  ; 只留 eax（低 32 位）
//   +01A 8B 55 F8 / 8B 92 A4 02 00 00                ; edx = 凶手 CC 下限
//   +023 B9 <B> / 01 D1        mov ecx,B / add ecx,edx
//   +02A 99 / F7 F9            cdq / idiv ecx        ; 32 位有符号截断除
//   +02D E9 → 0x71FD3D
// A/B 是运行期 atoi 出来后逐字节填的（0x100B9E6A / 0x100B9E7B call 0x1022DC49），
// A → [ebp-0x1A8..-0x19C]、B → [ebp-0x168..-0x15C]，其余 38 个 dword 由 9 条
// movaps 从 .rdata 常量取（0x102D37E0 / 29E0 / 2210 / 1E70 / 3280 / 2550 / 3140 /
// 32A0 / 37F0），末尾 [ebp-0x148]=0xF9、[ebp-0x144]=0xE9。
//
// 陷阱一：**算术是 32 位的**。`F7 E9 imul ecx` 只吃 eax，前一条 imul 的高半 edx 被丢；
//   紧接着 `8B 55 F8` 又把 edx 覆盖成凶手指针；最后 `99 cdq` 从 eax 重新符号扩展。
//   写成 64 位中间积会在 MaxPoint×倍率×A 溢出 int32 时给出完全不同的分母。
// 陷阱二：**+0x2A4 是 CC 下限（刺术下限），不是新字段**。职业端点选择器 sub_76CD8C
//   按 byte[self+0x72] 分四支，0x76CDEA `mov edx,[eax+0x2A4]` 就是 job 3 那一支
//   （job0 +0x28C DC / job1 +0x294 MC / job2 +0x29C SC），同样按 dword 读。
//   C# 权威是 m_NativeCoreWorkingAbility.CCLow（TBaseObject.NativeSkill66Or67.cs
//   的 id 68 分支 0x74449F 已经这么用）。
// 陷阱三：**关闭 = 宿主原样**。0x100BA0AE 经 0x10033340 把原 6 字节
//   8B 40 14 F7 6D D4 写回，所以开关关时分母就是 MaxPoint × 倍率。
using System.Text;
using System.Text.RegularExpressions;
using GameSvr;
using GameSvr.Plugins;
using SystemModule;

PrepareRuntimeConfig();
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
M2Share.LogSystem = new MirLog();
M2Share.UserEngine = new UserEngine();
M2Share.ObjectManager = new ObjectManager();
M2Share.ProcessMsgCriticalSection = new object();
M2Share.ProcessHumanCriticalSection = new object();

var failures = new List<string>();

void Assert(bool condition, string message)
{
    if (!condition) failures.Add(message);
}

void Equal(int expected, int actual, string what)
{
    Assert(expected == actual, $"{what}：期望 {expected}，实为 {actual}");
}

// ── A. 纯算术：桩体 +0x013..+0x02C 逐指令 ───────────────────────────────────
// 出厂 A=B=10、凶手无 CC ⇒ 恒等，这正是原生「没穿 CC 装备就没有变化」。
Equal(1000, YanshenEquipDropBoost.NativeDenominator(1000, 10, 10, 0),
    "A=B=10 且 CC=0 时分母应恒等");
// 面板绿字「[刺术下限]每加1点，提升爆率约10%」：B=10 时 CC=1 → 分母 ×10/11。
Equal(909, YanshenEquipDropBoost.NativeDenominator(1000, 10, 10, 1),
    "CC=1 时分母应为 10000/11（截断）");
Equal(666, YanshenEquipDropBoost.NativeDenominator(1000, 10, 10, 5),
    "CC=5 时分母应为 10000/15（截断）");
// idiv 向零截断，不是向下取整。
Equal(23, YanshenEquipDropBoost.NativeDenominator(7, 10, 3, 0),
    "70/3 必须向零截断为 23");
Equal(-23, YanshenEquipDropBoost.NativeDenominator(-7, 10, 3, 0),
    "-70/3 必须向零截断为 -23（idiv），不得向下取整为 -24");
// A 也可以放大分母（等于压低爆率），B 可以缩小分母。
Equal(4000, YanshenEquipDropBoost.NativeDenominator(1000, 20, 5, 0),
    "A=20 B=5 时分母应为 1000*20/5");
// atoi 允许 A 为 0：分母 0 ⇒ Random(0)=0 ⇒ 恒掉。这是原生行为，不是缺陷。
Equal(0, YanshenEquipDropBoost.NativeDenominator(1000, 0, 10, 0),
    "A=0 时分母应为 0（Random(0)=0 ⇒ 恒掉）");

// 32 位回绕：0x20000000 × 10 = 0x1_40000000，低 32 位是 0x40000000。
// 若误写成 64 位中间积，这里会得到 536870912 而不是 107374182。
Equal(107374182, YanshenEquipDropBoost.NativeDenominator(0x20000000, 10, 10, 0),
    "两次乘法必须只保留低 32 位（F7 E9 imul ecx 丢弃前一条 imul 的 edx）");
Equal(0, YanshenEquipDropBoost.NativeDenominator(0x10000000, 16, 1, 0),
    "0x10000000×16 的低 32 位是 0，分母应为 0");

// B + CC == 0 时原生 #DE。C# 抛 DivideByZeroException —— 同类行为，不加闸门。
var threw = false;
try { YanshenEquipDropBoost.NativeDenominator(1000, 10, 0, 0); }
catch (DivideByZeroException) { threw = true; }
Assert(threw, "B+CC==0 必须抛 DivideByZeroException（原生 idiv 0 → #DE），不得静默兜底");

// ── B. 插件缺席 = 桩体没装 = 宿主原样 ───────────────────────────────────────
M2Share.PluginManager = null;
var dormant = new TPlayObject { m_sCharName = "惰性凶手" };
dormant.m_NativeCoreWorkingAbility.CCLow = 7;
Equal(1000, YanshenEquipDropBoost.Denominator(1000, dormant),
    "插件缺席时分母必须保持 MaxPoint×倍率");
Equal(1000, YanshenEquipDropBoost.Denominator(1000, null),
    "插件缺席且凶手为空时分母必须保持 MaxPoint×倍率");

var tempRoot = Path.Combine(Path.GetTempPath(),
    "loym2-ys-dropboost-" + Guid.NewGuid().ToString("N"));
try
{
    var envirPath = Directory.CreateDirectory(Path.Combine(tempRoot, "Envir")).FullName;

    PluginManager Load(string json)
    {
        File.WriteAllText(Path.Combine(tempRoot, "config.json"), json, HUtil32.GbkEncoding);
        var pm = new PluginManager(envirPath);
        pm.RegisterBuiltinPlugins();
        if (!pm.LoadPlugin("YanshenCompat"))
            throw new InvalidOperationException("YanshenCompat 未能加载");
        pm.GetPlugin("YanshenCompat").IsInitialized = true;
        return pm;
    }

    TPlayObject Killer(int ccLow)
    {
        var killer = new TPlayObject { m_sCharName = "凶手" };
        killer.m_NativeCoreWorkingAbility.CCLow = ccLow;
        return killer;
    }

    // ── C. 开关为 0：0x100BA0AE 写回原 6 字节，一切保持原生 ──────────────────
    M2Share.PluginManager = Load("{\"装备提升人物爆率\":0," +
        "\"装备提升人物爆率_A值\":\"20\",\"装备提升人物爆率_B值\":\"5\"}");
    Equal(1000, YanshenEquipDropBoost.Denominator(1000, Killer(3)),
        "开关为 0 时必须保持 MaxPoint×倍率（0x100B9E4A cmp [edi+0x660],0 / je）");

    // ── D. 开关为 1：出厂 A=B=10 ────────────────────────────────────────────
    M2Share.PluginManager = Load("{\"装备提升人物爆率\":1}");
    Equal(1000, YanshenEquipDropBoost.Denominator(1000, Killer(0)),
        "缺 A/B 键时取页面构造函数出厂值 10/10，CC=0 ⇒ 恒等");
    Equal(769, YanshenEquipDropBoost.Denominator(1000, Killer(3)),
        "CC=3 时分母应为 10000/13（凶手 +0x2A4 = m_NativeCoreWorkingAbility.CCLow）");
    // 桩体 +0x006 `cmp [ebp-8],0x410000` / +0x00D `jb +0x2D`。
    Equal(1000, YanshenEquipDropBoost.Denominator(1000, null),
        "凶手为空时整段跳过（桩体 +0x006/+0x00D）");

    // ── E. A/B 走 CRT atoi，不是 atof、也不是 locale 解析 ───────────────────
    M2Share.PluginManager = Load("{\"装备提升人物爆率\":1," +
        "\"装备提升人物爆率_A值\":\"20\",\"装备提升人物爆率_B值\":\"5\"}");
    Equal(4000, YanshenEquipDropBoost.Denominator(1000, Killer(0)),
        "A=20 B=5 CC=0 时分母应为 4000");
    Equal(2000, YanshenEquipDropBoost.Denominator(1000, Killer(5)),
        "A=20 B=5 CC=5 时分母应为 20000/10");

    M2Share.PluginManager = Load("{\"装备提升人物爆率\":1," +
        "\"装备提升人物爆率_A值\":\"3.9\"}");
    Equal(300, YanshenEquipDropBoost.Denominator(1000, Killer(0)),
        "atoi(\"3.9\") 必须是 3（0x1022DC49 是 atoi 不是 atof）");

    M2Share.PluginManager = Load("{\"装备提升人物爆率\":1," +
        "\"装备提升人物爆率_A值\":\"\"}");
    Equal(0, YanshenEquipDropBoost.Denominator(1000, Killer(0)),
        "空串 A 走 atoi 得 0，分母 0；不得回落到出厂 10（那是【缺键】才有的回落）");

    var api = new YanshenApi(null, null,
        Load("{\"装备提升人物爆率\":1,\"装备提升人物爆率_A值\":\"7\"," +
             "\"装备提升人物爆率_B值\":\"11\"}"));
    Equal(7, api.BoostDropRateA(), "BoostDropRateA 应按 atoi 读");
    Equal(11, api.BoostDropRateB(), "BoostDropRateB 应按 atoi 读");
    Assert(api.IsBoostDropRate(), "开关为 1 时 IsBoostDropRate 应为真");
}
finally
{
    M2Share.PluginManager = null;
    try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { }
}

// ── F. 接线锚点仍在：UsrEngn.cs 的段2 掷点行 ────────────────────────────────
// 0x71FD37 的 C# 唯一对应表达式是 MonGetRandomItems 里那一行 Random 调用。
// 它只允许两种形态：未接线的原生式，或经本 helper 的接线式。任何第三种形态
// （比如把表达式拆开、把 penalty 挪走）都会让本轮报告里给出的插桩点失效。
var sourceRoot = FindSourceRoot();
var engineSource = File.ReadAllText(Path.Combine(sourceRoot,
    "GameSvr", "UsrSystem", "UsrEngn.cs"));
// 空白/换行无关：接线时怎么折行都行，认的是表达式形状。
var stock = Regex.IsMatch(engineSource,
    @"M2Share\.RandomNumber\.Random\(\s*MonItem\.MaxPoint\s*\*\s*penalty\s*\)");
var wired = Regex.IsMatch(engineSource,
    @"YanshenEquipDropBoost\.Denominator\(\s*MonItem\.MaxPoint\s*\*\s*penalty\s*,\s*killer\s*\)");
Assert(wired || stock,
    "UsrEngn.MonGetRandomItems 的段2 掷点行既不是原生式也不是接线式；" +
    "0x71FD37 的插桩锚点已失效");
Assert(!(wired && stock),
    "UsrEngn.MonGetRandomItems 同时出现原生式与接线式掷点行，掉落分母出现双权威");

if (failures.Count > 0)
{
    Console.WriteLine($"FAIL ({failures.Count})");
    foreach (var f in failures) Console.WriteLine("  - " + f);
    throw new InvalidOperationException(
        $"YanshenEquipDropBoostCheck: {failures.Count} 条契约不成立");
}

Console.WriteLine("PASS YanshenEquipDropBoostCheck");
Console.WriteLine("  分母 = (MaxPoint×倍率×A) / (B + 凶手 CC下限)，32 位乘、idiv 截断；"
    + "凶手为空或开关关时恒等");
Console.WriteLine(wired
    ? "  UsrEngn.MonGetRandomItems 已接线（0x71FD37）"
    : "  UsrEngn.MonGetRandomItems 尚未接线（禁改文件，插桩点见 "
      + "docs/ys_equip_dropboost_20260814.md）");
return 0;

static void PrepareRuntimeConfig()
{
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

static string FindSourceRoot()
{
    foreach (var origin in new[]
             {
                 Environment.GetEnvironmentVariable("LYOMIR_SOURCE_ROOT"),
                 Directory.GetCurrentDirectory(), AppContext.BaseDirectory
             })
    {
        if (string.IsNullOrWhiteSpace(origin)) continue;
        var directory = new DirectoryInfo(origin);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameSvr",
                    "GameSvr.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
    }
    throw new InvalidOperationException("source root was not found");
}
