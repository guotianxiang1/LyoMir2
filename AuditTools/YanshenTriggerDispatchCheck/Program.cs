// 钉住眼神「触发」族派发子系统的原生契约。
//
// 证据来源（全部可复跑）：
//   眼神脱壳转储  D:/loym2/staging/yanshen208_strparam_runtime_dump_20260719/
//                 yanshen2_0_8_dll.memory.bin       基址 0x10000000
//   M2Server 镜像 D:/loym2/staging/_reunpack_work/flat_image.bin  基址 0x400000
//   生产配置      D:/光头卧龙/mud2.0/Mir200/Gs1/config.json（380 键，GBK）
//
// 这个工具断言三类事实：
//   A. 注册表里每个触发点的挂载 VA / 续跑 VA / 脚本标签 / 派发槽 / 参数个数 /
//      是否顶掉原生动作 —— 任何一处被人「顺手改一下」都会 FAIL。
//   B. 插件缺席时整层完全惰性：Armed=false、派发计数器纹丝不动、两个召唤门
//      返回 false（= 原生造宠照跑）。
//   C. 已接通触发点的门与参数顺序，包括 0x71F058 那段下标搜索的两个边角。
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

// ── A. 注册表 = 原生事实 ────────────────────────────────────────────────────
// 每一行都来自 staging/_ys_swc/q06.txt（trampoline 解码）与 0x10033450 标签回填
// 现场的逐个反汇编。
var expected = new (string Key, string Label, uint Builder, uint[] Targets, uint[] Resumes,
    YanshenTriggerDispatch.Slot Slot, int Params, YanshenTriggerDispatch.HostAction Action,
    bool Wired)[]
{
    ("召唤神兽触发", "@SummonShinsu", 0x10032FD0, new uint[] { 0x006EDC5E }, new uint[] { 0x006EDC63 },
        YanshenTriggerDispatch.Slot.Plain, 0, YanshenTriggerDispatch.HostAction.Replace, true),
    ("召唤骷髅触发", "@SummonSkele", 0x10032FD0, new uint[] { 0x006EDB44 }, new uint[] { 0x006EDB49 },
        YanshenTriggerDispatch.Slot.Plain, 0, YanshenTriggerDispatch.HostAction.Replace, true),
    ("BB杀怪触发", "@BBupr", 0x10032CC0, new uint[] { 0x0071F467 }, new uint[] { 0x0071F46C },
        YanshenTriggerDispatch.Slot.WithParams, 2, YanshenTriggerDispatch.HostAction.Notify, true),
    ("BB死亡触发", "@BBKill", 0x10032CC0, new uint[] { 0x0076631C }, new uint[] { 0x00766321 },
        YanshenTriggerDispatch.Slot.WithParams, 1, YanshenTriggerDispatch.HostAction.Notify, true),
    ("英雄穿戴触发", "@HeroEquiepchange", 0x10032CC0,
        new uint[] { 0x0075F08C, 0x0075EA31 }, new uint[] { 0x0075F093, 0x0075EA37 },
        YanshenTriggerDispatch.Slot.WithParams, 6, YanshenTriggerDispatch.HostAction.Notify, false),
    ("新穿戴触发", "@MyEquiepchange", 0x10032CC0,
        new uint[] { 0x0075F085, 0x0075EA37 }, new uint[] { 0x0075F08C, 0x0075EA3C },
        YanshenTriggerDispatch.Slot.WithParams, 6, YanshenTriggerDispatch.HostAction.Notify, false),
    ("上线触发", "@initys", 0x10032CC0, new uint[] { 0x006548BD }, new uint[] { 0x006548C2 },
        YanshenTriggerDispatch.Slot.Plain, 0, YanshenTriggerDispatch.HostAction.Notify, false),
    ("死亡触发", "@OnDie", 0x10032FD0, new uint[] { 0x006C09B5 }, new uint[] { 0x006C09BA },
        YanshenTriggerDispatch.Slot.Plain, 0, YanshenTriggerDispatch.HostAction.Notify, true),
    ("回城按钮触发", "@OnBackButton", 0x10032FD0, new uint[] { 0x006DBB80 }, new uint[] { 0x006DBB85 },
        YanshenTriggerDispatch.Slot.Plain, 0, YanshenTriggerDispatch.HostAction.Replace, false),
    ("挖矿触发", "@OnDig", 0x10032FD0, new uint[] { 0x006EC111 }, new uint[] { 0x006EC116 },
        YanshenTriggerDispatch.Slot.Plain, 0, YanshenTriggerDispatch.HostAction.Notify, true),
    ("心灵启示触发", "@Revelation", 0x10032FD0, new uint[] { 0x006EDC2B }, new uint[] { 0x006EDC30 },
        YanshenTriggerDispatch.Slot.WithParams, 2, YanshenTriggerDispatch.HostAction.Replace, false),
    ("复活触发脚本", "@OnDia", 0x10032CC0, new uint[] { 0x0073C484 }, new uint[] { 0x0073C48A },
        YanshenTriggerDispatch.Slot.Plain, 0, YanshenTriggerDispatch.HostAction.Notify, false),
    ("被击杀触发", "@MyKill", 0x10032CC0, new uint[] { 0x00766624 }, new uint[] { 0x00766629 },
        YanshenTriggerDispatch.Slot.WithParams, 2, YanshenTriggerDispatch.HostAction.Notify, false),
    ("捡物触发", "@pickpre", 0x10032CC0, new uint[] { 0x006B770C }, new uint[] { 0x006B7711 },
        YanshenTriggerDispatch.Slot.WithParams, 2, YanshenTriggerDispatch.HostAction.Notify, false),
    ("攻击触发", "@MyAttack", 0x10032CC0, new uint[] { 0x0076E35D }, new uint[] { 0x0076E362 },
        YanshenTriggerDispatch.Slot.WithParams, 4, YanshenTriggerDispatch.HostAction.Notify, false),
    ("魔法攻击触发", "@MyMagicAttack", 0x10032CC0, new uint[] { 0x0076DE84 }, new uint[] { 0x0076DE8A },
        YanshenTriggerDispatch.Slot.WithParams, 5, YanshenTriggerDispatch.HostAction.Notify, false),
    ("盘古穿戴触发", "@ChangeEquip", 0x10032FD0,
        new uint[] { 0x006D8E35, 0x006D8E4D }, new uint[] { 0x006D8E3A, 0x006D8E52 },
        YanshenTriggerDispatch.Slot.Plain, 0, YanshenTriggerDispatch.HostAction.Notify, true),
    ("盘古魔法攻击触发", "@MagicAttack", 0x10032FD0,
        new uint[] { 0x0076E1AF, 0x0076DEC0 }, new uint[] { 0x0076E1B6, 0x0076DEC7 },
        YanshenTriggerDispatch.Slot.WithParams, 3, YanshenTriggerDispatch.HostAction.Notify, false),
    ("刀刀切割", "@Cutting", 0x10032CC0, new uint[] { 0x00767BAE }, new uint[] { 0x00767BB4 },
        YanshenTriggerDispatch.Slot.Plain, 0, YanshenTriggerDispatch.HostAction.Notify, false),
    ("新倍攻和暴击", "@baoji", 0x10032CC0, new uint[] { 0x0076C88B }, new uint[] { 0x0076C890 },
        YanshenTriggerDispatch.Slot.Plain, 0, YanshenTriggerDispatch.HostAction.Notify, true),
    ("英雄倍攻和暴击", "@Herobaoji", 0x10032CC0, new uint[] { 0x0076C816 }, new uint[] { 0x0076C81D },
        YanshenTriggerDispatch.Slot.Plain, 0, YanshenTriggerDispatch.HostAction.Notify, false),
};

Assert(YanshenTriggerDispatch.Registry.Count == expected.Length,
    $"注册表条目数 {YanshenTriggerDispatch.Registry.Count} != 原生解出的 {expected.Length}");

foreach (var e in expected)
{
    var d = YanshenTriggerDispatch.Find(e.Key);
    if (d == null)
    {
        failures.Add($"注册表缺少 {e.Key}");
        continue;
    }
    Assert(d.ScriptLabel == e.Label,
        $"{e.Key} 脚本标签应为 {e.Label}，实为 {d.ScriptLabel}");
    Assert(d.Builder == e.Builder,
        $"{e.Key} 安装器应为 {e.Builder:X8}，实为 {d.Builder:X8}");
    Assert(d.HostTargets != null && d.HostTargets.SequenceEqual(e.Targets),
        $"{e.Key} 宿主挂载点应为 [{string.Join(",", e.Targets.Select(v => v.ToString("X6")))}]");
    Assert(d.HostResumes != null && d.HostResumes.SequenceEqual(e.Resumes),
        $"{e.Key} 续跑点应为 [{string.Join(",", e.Resumes.Select(v => v.ToString("X6")))}]");
    Assert(d.DispatchSlot == e.Slot, $"{e.Key} 派发槽应为 {e.Slot}");
    Assert(d.ParamCount == e.Params, $"{e.Key} 参数个数应为 {e.Params}，实为 {d.ParamCount}");
    Assert(d.Action == e.Action, $"{e.Key} 宿主动作语义应为 {e.Action}");
    Assert(d.Wired == e.Wired, $"{e.Key} Wired 应为 {e.Wired}");
    // 挂载点与续跑点必须落在 M2Server 的代码区间，且续跑严格在挂载之后
    // （trampoline 覆盖的是 [target, resume) 这段字节）。
    for (var i = 0; i < d.HostTargets.Length; i++)
    {
        Assert(d.HostTargets[i] >= 0x00401000 && d.HostTargets[i] < 0x00800000,
            $"{e.Key} 挂载点 {d.HostTargets[i]:X6} 不在宿主代码区");
        var overwritten = d.HostResumes[i] - d.HostTargets[i];
        Assert(overwritten >= 5 && overwritten <= 16,
            $"{e.Key} 覆盖长度 {overwritten} 不合理（jmp rel32 至少 5 字节）");
    }
}

// 「顶掉原生动作」的四条：两个召唤 + 心灵启示 + 回城按钮。原生桩体都没有重放被覆盖的 call。
// 回城按钮触发（0x6DBB80）本轮亲验并入：其 33 字节桩体不重放 `E8 …… call 0x6F926C`（真实回城处理器），
// 与两个召唤逐字节同形，故按 Replace 归类（原注册表记 Notify 系未核 replay 的笔误）。
var replacing = YanshenTriggerDispatch.Registry
    .Where(d => d.Action == YanshenTriggerDispatch.HostAction.Replace)
    .Select(d => d.ConfigKey).OrderBy(k => k, StringComparer.Ordinal).ToArray();
Assert(replacing.SequenceEqual(new[] { "召唤神兽触发", "召唤骷髅触发", "心灵启示触发", "回城按钮触发" }
        .OrderBy(k => k, StringComparer.Ordinal)),
    "只有 召唤神兽触发/召唤骷髅触发/心灵启示触发/回城按钮触发 四条会顶掉原生动作，实际为 "
    + string.Join("/", replacing));

// ── B. 插件缺席 = 完全惰性 ──────────────────────────────────────────────────
M2Share.PluginManager = null;
M2Share.g_FunctionNPC = null;
var baseline = YanshenTriggerDispatch.DispatchCount;

Assert(!YanshenTriggerDispatch.Armed, "PluginManager 为 null 时 Armed 必须为 false");

var dormantPlayer = new TPlayObject { m_sCharName = "惰性验证角色" };
var dormantSlave = new TBaseObject { m_sCharName = "惰性验证宝宝", m_Master = dormantPlayer };
dormantPlayer.m_SlaveList.Add(dormantSlave);

Assert(!YanshenTriggerDispatch.FireSummonShinsu(dormantPlayer),
    "插件缺席时 FireSummonShinsu 必须返回 false（原生神兽照常产生）");
Assert(!YanshenTriggerDispatch.FireSummonSkele(dormantPlayer),
    "插件缺席时 FireSummonSkele 必须返回 false（原生骷髅照常产生）");
YanshenTriggerDispatch.FireSlaveGainExp(dormantSlave);
YanshenTriggerDispatch.FireSlaveDie(dormantSlave);
// 本轮接通的三个纯通知触发，插件缺席时同样必须零派发。
YanshenTriggerDispatch.FireOnDie(dormantPlayer);
YanshenTriggerDispatch.FireOnDig(dormantPlayer);
YanshenTriggerDispatch.FireChangeEquip(dormantPlayer);
Assert(YanshenTriggerDispatch.DispatchCount == baseline,
    $"插件缺席时派发计数器必须不动，却从 {baseline} 变成 {YanshenTriggerDispatch.DispatchCount}");

// ── C. 插件在场：开关 0 仍然静默，开关 1 才发射 ─────────────────────────────
var tempRoot = Path.Combine(Path.GetTempPath(),
    "loym2-ys-trigger-" + Guid.NewGuid().ToString("N"));
try
{
    var envirPath = Directory.CreateDirectory(Path.Combine(tempRoot, "Envir")).FullName;

    // 全部触发键写 0：这就是生产 config.json 里 召唤神兽触发/召唤骷髅触发/BB杀怪触发/
    // BB死亡触发 的实际取值。
    File.WriteAllText(Path.Combine(tempRoot, "config.json"),
        "{\"召唤神兽触发\":0,\"召唤骷髅触发\":0,\"BB杀怪触发\":0,\"BB死亡触发\":0,"
        + "\"死亡触发\":0,\"挖矿触发\":0,\"盘古穿戴触发\":0}",
        HUtil32.GbkEncoding);
    var pmOff = new PluginManager(envirPath);
    pmOff.RegisterBuiltinPlugins();
    Assert(pmOff.LoadPlugin("YanshenCompat"), "YanshenCompat 未能加载（开关全 0 场景）");
    // 生产里 IsInitialized 由脚本执行 initys 置位（PasApiBridge.Yanshen.cs:257）。
    // 这里直接置位，好让本用例检验的是【开关】而不是初始化状态。
    pmOff.GetPlugin("YanshenCompat").IsInitialized = true;
    M2Share.PluginManager = pmOff;

    Assert(YanshenTriggerDispatch.Armed, "插件 Running 时 Armed 必须为 true");
    var beforeOff = YanshenTriggerDispatch.DispatchCount;
    Assert(!YanshenTriggerDispatch.FireSummonShinsu(dormantPlayer),
        "召唤神兽触发=0 时必须返回 false");
    Assert(!YanshenTriggerDispatch.FireSummonSkele(dormantPlayer),
        "召唤骷髅触发=0 时必须返回 false");
    YanshenTriggerDispatch.FireSlaveGainExp(dormantSlave);
    YanshenTriggerDispatch.FireSlaveDie(dormantSlave);
    YanshenTriggerDispatch.FireOnDie(dormantPlayer);
    YanshenTriggerDispatch.FireOnDig(dormantPlayer);
    YanshenTriggerDispatch.FireChangeEquip(dormantPlayer);
    Assert(YanshenTriggerDispatch.DispatchCount == beforeOff,
        "开关为 0 时派发计数器必须不动");

    // 开关打开
    File.WriteAllText(Path.Combine(tempRoot, "config.json"),
        "{\"召唤神兽触发\":1,\"召唤骷髅触发\":1,\"BB杀怪触发\":1,\"BB死亡触发\":1,"
        + "\"死亡触发\":1,\"挖矿触发\":1,\"盘古穿戴触发\":1}",
        HUtil32.GbkEncoding);
    var pmOn = new PluginManager(envirPath);
    pmOn.RegisterBuiltinPlugins();
    Assert(pmOn.LoadPlugin("YanshenCompat"), "YanshenCompat 未能加载（开关全 1 场景）");
    pmOn.GetPlugin("YanshenCompat").IsInitialized = true;
    M2Share.PluginManager = pmOn;

    var beforeOn = YanshenTriggerDispatch.DispatchCount;
    Assert(YanshenTriggerDispatch.FireSummonShinsu(dormantPlayer),
        "召唤神兽触发=1 时必须返回 true（原生 call 0x76EE7C 被顶掉）");
    Assert(YanshenTriggerDispatch.LastDispatchedLabel == "@SummonShinsu",
        "召唤神兽触发发出的标签必须是 @SummonShinsu");
    Assert(YanshenTriggerDispatch.FireSummonSkele(dormantPlayer),
        "召唤骷髅触发=1 时必须返回 true（原生 call 0x76EDFC 被顶掉）");
    Assert(YanshenTriggerDispatch.LastDispatchedLabel == "@SummonSkele",
        "召唤骷髅触发发出的标签必须是 @SummonSkele");

    YanshenTriggerDispatch.FireSlaveGainExp(dormantSlave);
    Assert(YanshenTriggerDispatch.LastDispatchedLabel == "@BBupr",
        "BB杀怪触发发出的标签必须是 @BBupr");
    YanshenTriggerDispatch.FireSlaveDie(dormantSlave);
    Assert(YanshenTriggerDispatch.LastDispatchedLabel == "@BBKill",
        "BB死亡触发发出的标签必须是 @BBKill");
    Assert(YanshenTriggerDispatch.DispatchCount == beforeOn + 4,
        $"开关全开时应恰好派发 4 次，实为 {YanshenTriggerDispatch.DispatchCount - beforeOn}");

    // 本轮接通的三个纯通知触发：死亡 / 挖矿 / 盘古穿戴。都走 Plain 槽、This_Player=self，
    // 开关为 1 时各发一次，标签分别为 @OnDie / @OnDig / @ChangeEquip。
    var beforeNotify = YanshenTriggerDispatch.DispatchCount;
    YanshenTriggerDispatch.FireOnDie(dormantPlayer);
    Assert(YanshenTriggerDispatch.LastDispatchedLabel == "@OnDie",
        "死亡触发发出的标签必须是 @OnDie");
    YanshenTriggerDispatch.FireOnDig(dormantPlayer);
    Assert(YanshenTriggerDispatch.LastDispatchedLabel == "@OnDig",
        "挖矿触发发出的标签必须是 @OnDig");
    YanshenTriggerDispatch.FireChangeEquip(dormantPlayer);
    Assert(YanshenTriggerDispatch.LastDispatchedLabel == "@ChangeEquip",
        "盘古穿戴触发发出的标签必须是 @ChangeEquip");
    Assert(YanshenTriggerDispatch.DispatchCount == beforeNotify + 3,
        $"死亡/挖矿/盘古穿戴 开关全开时应恰好派发 3 次，实为 "
        + $"{YanshenTriggerDispatch.DispatchCount - beforeNotify}");

    // 门：玩家自己死不算 BB死亡；无主的怪也不算。
    var beforeGate = YanshenTriggerDispatch.DispatchCount;
    YanshenTriggerDispatch.FireSlaveDie(dormantPlayer);          // [eax]==0x6AC8C8 -> bail
    YanshenTriggerDispatch.FireSlaveDie(new TBaseObject());      // [eax+0x38C]==0 -> bail
    YanshenTriggerDispatch.FireSlaveGainExp(new TBaseObject());  // 无主 -> bail
    Assert(YanshenTriggerDispatch.DispatchCount == beforeGate,
        "玩家 / 无主对象必须被四道门挡下（原生 0x76631D 与 0x766329）");
}
finally
{
    M2Share.PluginManager = null;
    try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { }
}

// ── D. 0x71F058 下标搜索的两个边角 ──────────────────────────────────────────
// 原生：mov ecx,[list+8] / dec ecx / ... / cmp ecx,0 / jl bail。
//   Count==0 -> ecx 减到 -1 -> jl 生效 -> 整条事件跳过。
//   自顶向下扫到 0 仍未命中 -> 循环靠 jg 落空退出，ecx 停在 0，jl 不成立 -> 照发，序号按 0 算。
{
    var owner = new TPlayObject { m_sCharName = "序号边角" };
    var a = new TBaseObject { m_Master = owner };
    var b = new TBaseObject { m_Master = owner };
    var stranger = new TBaseObject { m_Master = owner };

    Assert(YanshenTriggerDispatch.ResolveNativeSlaveOrdinal(owner, a) == -1,
        "空 m_SlaveList 必须返回 -1（原生 ecx=-1 走 jl bail）");

    owner.m_SlaveList.Add(a);
    owner.m_SlaveList.Add(b);
    Assert(YanshenTriggerDispatch.ResolveNativeSlaveOrdinal(owner, b) == 1,
        "末位宠物的下标应为 1（脚本收到 2）");
    Assert(YanshenTriggerDispatch.ResolveNativeSlaveOrdinal(owner, a) == 0,
        "首位宠物的下标应为 0（脚本收到 1）");
    Assert(YanshenTriggerDispatch.ResolveNativeSlaveOrdinal(owner, stranger) == 0,
        "不在表里的对象原生也不 bail：循环停在 ecx=0，脚本收到 1");
}

if (failures.Count > 0)
{
    Console.WriteLine($"FAIL ({failures.Count})");
    foreach (var f in failures) Console.WriteLine("  - " + f);
    throw new InvalidOperationException(
        $"YanshenTriggerDispatchCheck: {failures.Count} 条契约不成立");
}

Console.WriteLine("PASS YanshenTriggerDispatchCheck");
Console.WriteLine($"  注册表 {YanshenTriggerDispatch.Registry.Count} 个触发点，"
    + $"已接通 {YanshenTriggerDispatch.Registry.Where(d => d.Wired).ToArray().Length} 个");

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
