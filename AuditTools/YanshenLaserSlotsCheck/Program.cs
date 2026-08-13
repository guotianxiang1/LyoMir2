// 钉住眼神 A7「激光两槽」的原生契约，重点是槽②那条反直觉的 fallback。
//
// 证据来源（全部可复跑）：
//   眼神脱壳转储  D:/loym2/staging/yanshen208_strparam_runtime_dump_20260719/
//                 yanshen2_0_8_dll.memory.bin       基址 0x10000000
//   M2Server 镜像 D:/loym2/staging/_reunpack_work/flat_image.bin  基址 0x400000
//
// 宿主激光体 sub_76E994 的尾巴（flat_image.bin 亲验）：
//   0076EA0F E830140000 call 0x76FE44   ; 产生光束
//   0076EA14 B803000000 mov  eax,3      ; ← 桩体覆盖这 5 字节
//   0076EA19 E82E51C9FF call 0x403B4C   ; Random(eax)
//   0076EA1E 8BC8 41    mov ecx,eax / inc ecx      ; Random(N)+1
//   0076EA27 FF533C     call [ebx+0x3C]            ; TrainSkill
//
// 安装点 0x100D95BA（builder 0x10032FD0，patch=target=0x76EA14，resume=0x76EA19），
// 107 个模板元素按「一 dword 装一字节」展开成 111 字节，逐条重建后是：
//   +000 cmp [ebp+4],0x6EDA54   jne fallback   ; 只认 0x6EDA4E `call [edi+0x120]` 那一次
//   +00D cmp ebx,0x410000       jb  fallback
//   +019 cmp [ebx],0x6AC8C8     jne fallback   ; 只对 TPlayObject
//   +025 push esi / push edx
//   +027 mov esi,[ebx+0x804]                   ; 原生 S 银行
//   +02D cmp esi,0x410000       jb  pop→fallback
//   +039 mov edx,[esi+0x288] / cmp edx,0x43A(1082) / jne pop→fallback
//   +04B mov edx,[esi+0x28C] / cmp edx,0       / jle pop→fallback
//   +05A mov eax,edx / pop / jmp resume         ; eax = S(1,82)
//   +065 fallback: mov eax,1                    ; ← 注意不是还原 mov eax,3
//
// 陷阱：所有 fallback 都落到 `mov eax,1`。开关一开，键取不到或值 ≤0 时训练点恒为
// Random(1)+1 = 1，比原生的 Random(3)+1 更差 —— 开了就再也回不去 3。
//
// 门 `[ebp+4]==0x6EDA54` 等价于「这条法术是激光」：0x6ED6FF 的
// `jmp dword [eax*4+0x6ED706]` 以 wMagicID 直接作下标（0x6ED6E3
// `movzx eax,word [eax+0x10]`，无偏移），下标 10 的表项 0x6ED72E 就是 0x6EDA41，
// 而 SpellsDef.SKILL_SHOOTLIGHTEN == 10，且 0x6EDA41 在全镜像只作为这一个表项出现。
// 所以 C# 把 laserTrainRandomArg 只在 SKILL_SHOOTLIGHTEN 段改写（MagicManager.cs），
// 与宿主「只认那一个调用点」严格同义。
using System.Text;
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

// ── A. 原生常量 ─────────────────────────────────────────────────────────────
Assert(YanshenLaserSlots.NativeTrainRandom == 3,
    $"槽② 原生 Random 实参应为 3（0x76EA14 `B8 03 00 00 00`），实为 {YanshenLaserSlots.NativeTrainRandom}");
Assert(YanshenLaserSlots.NativeBeamArg0 == 1,
    $"槽① 原生 arg0 应为 1（0x76EA07 `6A 01`），实为 {YanshenLaserSlots.NativeBeamArg0}");
// 键编码：C# 的 group*1000+index 必须等于桩体 +03F 的 `cmp edx,0x43A`。
Assert(1 * 1000 + 82 == 0x43A,
    "S(1,82) 的键编码必须等于桩体比对的 0x43A(1082)");
Assert(1 * 1000 + 81 == 0x439,
    "S(1,81) 的键编码必须等于 0x439(1081)");

// ── B. 插件缺席 = 保持原生 ──────────────────────────────────────────────────
M2Share.PluginManager = null;
var dormant = new TPlayObject { m_sCharName = "惰性激光角色" };
dormant.SetScriptVar('S', 1, 82, 7);
dormant.SetScriptVar('S', 1, 81, 9);
Assert(YanshenLaserSlots.TrainRandomArg(dormant) == 3,
    "插件缺席时槽②必须保持原生 3（桩体根本没装）");
Assert(YanshenLaserSlots.TrainRandomArg(null) == 3,
    "player 为 null 时槽②必须保持原生 3");
Assert(YanshenLaserSlots.BeamArg0(dormant) == 1,
    "插件缺席时槽①必须保持原生 1");
Assert(YanshenLaserSlots.BeamArg0(null) == 1,
    "player 为 null 时槽①必须保持原生 1");

var tempRoot = Path.Combine(Path.GetTempPath(),
    "loym2-ys-laser-" + Guid.NewGuid().ToString("N"));
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
        // 生产里 IsInitialized 由脚本执行 initys 置位；这里直接置位，好让本用例
        // 检验的是【开关】而不是初始化状态。
        pm.GetPlugin("YanshenCompat").IsInitialized = true;
        return pm;
    }

    // ── C. 开关为 0：桩体不安装，一切保持原生 ───────────────────────────────
    M2Share.PluginManager = Load("{\"激光命中概率\":0,\"激光范围及系数\":0}");
    var off = new TPlayObject { m_sCharName = "关闭态" };
    off.SetScriptVar('S', 1, 82, 7);
    off.SetScriptVar('S', 1, 81, 9);
    Assert(YanshenLaserSlots.TrainRandomArg(off) == 3,
        "激光命中概率=0 时必须保持原生 Random(3)+1");
    Assert(YanshenLaserSlots.BeamArg0(off) == 1,
        "激光范围及系数=0 时槽① 必须保持原生 1");

    // ── D. 开关为 1：命中取 S，未命中一律落 1（不还原 3）───────────────────
    M2Share.PluginManager = Load("{\"激光命中概率\":1,\"激光范围及系数\":1}");

    var hit = new TPlayObject { m_sCharName = "命中" };
    hit.SetScriptVar('S', 1, 82, 7);
    Assert(YanshenLaserSlots.TrainRandomArg(hit) == 7,
        "开关为 1 且 S(1,82)=7 时应取 7（桩体 +05A `mov eax,edx`）");

    // 这三条就是「开了就回不去 Random(3)」——桩体 +065 是 `mov eax,1`，不是 `mov eax,3`。
    var zero = new TPlayObject { m_sCharName = "值为零" };
    zero.SetScriptVar('S', 1, 82, 0);
    Assert(YanshenLaserSlots.TrainRandomArg(zero) == 1,
        "S(1,82)=0 时必须落 1（桩体 +051 `cmp edx,0 / jle` → +065 `mov eax,1`），不得还原成 3");

    var negative = new TPlayObject { m_sCharName = "值为负" };
    negative.SetScriptVar('S', 1, 82, -5);
    Assert(YanshenLaserSlots.TrainRandomArg(negative) == 1,
        "S(1,82)<0 时必须落 1，不得还原成 3");

    var missing = new TPlayObject { m_sCharName = "无键" };
    Assert(YanshenLaserSlots.TrainRandomArg(missing) == 1,
        "缺 S(1,82) 键时必须落 1（桩体 +03F tag 不等 → +065），不得还原成 3");

    // 槽①：同一把尺子，但 0x76FEA7 `8A 45 08` 只取低 8 位。
    var beam = new TPlayObject { m_sCharName = "光束" };
    beam.SetScriptVar('S', 1, 81, 9);
    Assert(YanshenLaserSlots.BeamArg0(beam) == 9,
        "开关为 1 且 S(1,81)=9 时槽① 应取 9");
    var beamWide = new TPlayObject { m_sCharName = "光束截断" };
    beamWide.SetScriptVar('S', 1, 81, 0x1FF);
    Assert(YanshenLaserSlots.BeamArg0(beamWide) == 0xFF,
        "槽① 只取低 8 位（0x76FEA7 `8A 45 08 mov al,[ebp+8]`）");
    var beamZero = new TPlayObject { m_sCharName = "光束零" };
    beamZero.SetScriptVar('S', 1, 81, 0);
    Assert(YanshenLaserSlots.BeamArg0(beamZero) == 1,
        "槽① 值 ≤0 时回落原生 1（与槽② 不同：这里 fallback 就是原生 push 1）");

    // ── E. 两个开关互不串味 ────────────────────────────────────────────────
    M2Share.PluginManager = Load("{\"激光命中概率\":1,\"激光范围及系数\":0}");
    var mixed = new TPlayObject { m_sCharName = "只开命中" };
    mixed.SetScriptVar('S', 1, 82, 4);
    mixed.SetScriptVar('S', 1, 81, 9);
    Assert(YanshenLaserSlots.TrainRandomArg(mixed) == 4,
        "只开 激光命中概率 时槽② 应生效");
    Assert(YanshenLaserSlots.BeamArg0(mixed) == 1,
        "激光范围及系数 仍为 0 时槽① 必须保持原生 1");
}
finally
{
    M2Share.PluginManager = null;
    try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { }
}

if (failures.Count > 0)
{
    Console.WriteLine($"FAIL ({failures.Count})");
    foreach (var f in failures) Console.WriteLine("  - " + f);
    throw new InvalidOperationException(
        $"YanshenLaserSlotsCheck: {failures.Count} 条契约不成立");
}

Console.WriteLine("PASS YanshenLaserSlotsCheck");
Console.WriteLine("  槽② S(1,82) 已接线（MagicManager 激光段），fallback 恒为 1；"
    + "槽① S(1,81) 读取器已钉住但故意不接");

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
