using System.Text;

// 眼神2(第2页) + 扩展页 · docs/ys_b1_yanshen2_page2ext_20260814.md 回归网。

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "docs", "ys_page2ext_census.tsv")))
            return dir.FullName;
        dir = dir.Parent;
    }
    throw new InvalidOperationException("docs/ys_page2ext_census.tsv not found");
}

var root = FindRepoRoot();
var censusPath = Path.Combine(root, "docs", "ys_page2ext_census.tsv");
var behaviorsPath = Path.Combine(root, "GameSvr", "Plugins", "YanshenPage2ExtBehaviors.cs");
var magicPath = Path.Combine(root, "GameSvr", "Actors", "TBaseObject.NativeMagicDamage.cs");

var inertZero = new HashSet<string>(StringComparer.Ordinal)
{
    "多元伤害", "怪物爆率A_值", "怪物爆率B_值", "怪物爆率K_值", "新怪物爆率",
    "道士合击系数", "道士合击系数_数值1", "道士合击系数_数值2", "道士合击系数_数值3",
    "道士合击系数_数值4", "道士合击系数_数值5",
    "雷电术自定义伤害", "雷电术自定义伤害_系数A", "雷电术自定义伤害_系数B",
    "伤害触发脚本_plus", "英雄魔法攻击触发", "高级魔法攻击触发",
    "宝宝叛变属性a", "宠物吸血a", "投保报文", "护身触发报文a", "护身触发概率a",
    "星耀专属切割a", "星耀倍功与暴击a", "星耀攻击反伤a", "格位刺杀免伤a",
    "概率格挡a", "自定义召唤怪物a", "英雄修装备a", "装备投保",
};

var blockedLogic = new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["英雄野蛮"] = "0x10067d92",
    ["英雄物理攻击触发"] = "0x10068035",
    ["高级物理攻击触发"] = "0x10067f16",
    ["千分比经验倍数"] = "0x1006a99d",
    ["麻痹中不被麻痹a"] = "0x1009029d",
};

var blockedHostPatches = new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["获取玩家对象函数"] = "0x646f40",
};

var doneConsumers = new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["火墙不吸血"] = "0x1007aaeb",
};

var lines = File.ReadAllLines(censusPath, Encoding.UTF8);
var seen = new HashSet<string>(StringComparer.Ordinal);
foreach (var line in lines.Skip(1))
{
    if (string.IsNullOrWhiteSpace(line)) continue;
    var cols = line.Split('\t');
    if (cols.Length < 7) continue;
    seen.Add(cols[0]);
    var key = cols[0];
    var verdict = cols[6];
    if (inertZero.Contains(key) && verdict != "zero" && verdict != "gui_only")
        throw new InvalidOperationException(key + ": expected zero/gui_only, got " + verdict);
    if (blockedLogic.ContainsKey(key) && !cols[4].Contains(blockedLogic[key], StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException(key + ": missing consumer " + blockedLogic[key]);
}

var patchVas = File.ReadAllText(
    Path.Combine(root, "docs", "ys_patch_target_vas.tsv"), Encoding.UTF8);
foreach (var kv in blockedHostPatches)
{
    if (!patchVas.Contains(kv.Value, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException(kv.Key + ": missing host patch " + kv.Value);
}

foreach (var key in inertZero)
    if (!seen.Contains(key))
        throw new InvalidOperationException("census missing inert key " + key);

var behaviors = File.ReadAllText(behaviorsPath, Encoding.UTF8);
if (!behaviors.Contains("0x1007AAEB", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("YanshenPage2ExtBehaviors missing 0x1007AAEB evidence");
if (!behaviors.Contains("0x1007AAC9", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("YanshenPage2ExtBehaviors missing 0x1007AAC9 evidence");

var magic = File.ReadAllText(magicPath, Encoding.UTF8);
if (!magic.Contains("YanshenPage2ExtBehaviors.ApplyMagicDamageVamp", StringComparison.Ordinal))
    throw new InvalidOperationException("ResolveFullMagicDamage not wired to ApplyMagicDamageVamp");

Console.WriteLine("PASS page2+ext census: inert=" + inertZero.Count
    + " blockedLogic=" + blockedLogic.Count
    + " blockedHost=" + blockedHostPatches.Count
    + " done=" + doneConsumers.Count);
