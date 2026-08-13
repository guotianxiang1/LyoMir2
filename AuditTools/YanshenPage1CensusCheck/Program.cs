using System.Text;

// 眼神2(第1页) 整页裁决的回归网。守的是 docs/ys_b1_yanshen2_page1_20260814.md 的三条结论：
//
//  A. 这一页 34 个键一条 M2Server 补丁都不打。判据是补丁标签图谱：插件每条 apply/revert
//     臂都以 call 0x100F018C(labelObject, "<键名>(已启动)|(未启动)") 收尾，407 个安装点
//     （trampoline builder 0x10032CC0 ×71 / 0x10032FD0 ×30、裸 memcpy 0x10033340 ×306）
//     收敛成 107 个特性标签，本页的键一个都不在其中。
//
//  B. 20 个键在原版 2.0.8 里没有任何行为 —— 整个 45 MB 镜像（含只有 delayed 转储才有的
//     16 MB Themida 远端区 0x10400000..0x11400000）里，除了 JSON 加载器 sub_100D6220 写、
//     序列化器 sub_10004140 读回、GUI 提交函数读勾选框之外，没有第四处读点。
//     C# 侧同样只有 YanshenApi 访问器、无引擎消费者 ⇒ 已经 1:1。
//     **本检查会在有人给这 20 个键写引擎实现时失败**：那需要先拿出原版字节证据，
//     不能凭面板绿字臆造。改判请连同 docs/ys_page1_census.tsv 一起更新。
//
//  C. 9 个键有且仅有一个插件侧消费者，语义已解但宿主挂载点不可证（sub_100795C0 在两份
//     转储里 0 个静态引用，唯一调用点 0x10F2D759 落在 Themida 远端混淆区），故 fail-closed。
//
// 数据来自 tools/ys_page1_census.py 的产物 docs/ys_page1_census.tsv（可复跑）。
// 本工具只读仓库文件，不引 GameSvr，跑一次不到一秒。

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "docs", "ys_page1_census.tsv")))
        {
            return dir.FullName;
        }
        dir = dir.Parent;
    }
    throw new InvalidOperationException("docs/ys_page1_census.tsv not found above " + AppContext.BaseDirectory);
}

// 有插件侧消费者的 9 个键 -> 消费者 VA（转储 A/B 一致）。
var expectedConsumers = new Dictionary<string, string>
{
    ["冰咆哮切割"] = "0x1007af12",
    ["火墙切割"] = "0x1007af78",
    ["烈火切割"] = "0x1007afdd",
    ["雷电术切割"] = "0x1007b043",
    ["火符切割"] = "0x1007b0a6",
    ["英雄千分比免伤"] = "0x1007a8a7",
    ["主号高级暴击"] = "0x10079fb1",
    ["高级英雄倍功暴击"] = "0x1007a014",
    ["主号分身术a"] = "0x1006953f",
};

// 原版本身无行为的 20 个键 -> 配置单例字段偏移（序列化器 sub_10004140 实测）。
var inertFields = new Dictionary<string, string>
{
    ["技能触发脚本"] = "0x508",
    ["英雄自动开盾"] = "0x668",
    ["装备转生穿戴判定a"] = "0x66c",
    ["诱惑之光触发脚本a"] = "0x670",
    ["烈火固定增伤"] = "0x678",
    ["冰咆哮固定增伤"] = "0x67c",
    ["火墙固定增伤"] = "0x680",
    ["火符固定增伤"] = "0x684",
    ["技能等级突破"] = "0x69c",
    ["宝宝自动叛变"] = "0x6a0",
    ["新呼唤宝宝"] = "0x6a4",
    ["技能等级突破_最大值"] = "0x6a8",
    ["嗜血术范围"] = "0x808",
    ["主号施法速度"] = "0x83c",
    ["装备多职业"] = "0x844",
    ["角色多阵营"] = "0x848",
    ["战队职业限制"] = "0x84c",
    ["穿戴触发_plus"] = "0x860",
    ["切换暴击报文"] = "0x864",
    ["主号全局法速"] = "0x8cc",
};

// 五个「切割」臂的原生常量：magicId -> (配置字段, S 坐标, tag 槽, 值槽)。
// 共同前置：攻击方类指针 == 0x6AC8C8 (TPlayObject)，银行 = [attacker+0x804]，
// [bank+0x180]==0x419 (S 键 1049) 且 [bank+0x184]==0x522 (1314)，槽值 > 0，
// 净效果 damage += 槽值（加法，无上限）。
var cuttingArms = new (int MagicId, string Key, string Field, int SIndex, string TagSlot, string ValueSlot)[]
{
    (33, "冰咆哮切割", "0x688", 116, "0x398", "0x39c"),
    (22, "火墙切割", "0x698", 117, "0x3a0", "0x3a4"),
    (1007, "烈火切割", "0x690", 118, "0x3a8", "0x3ac"),
    (11, "雷电术切割", "0x68c", 119, "0x3b0", "0x3b4"),
    (13, "火符切割", "0x694", 120, "0x3b8", "0x3bc"),
};

// 只对键名做字面量扫描时，这些文件是「标签面」而不是「行为面」，与 tools/ys_gui_matrix.py
// 的 role_of() 保持同一口径。
var labelOnlyRoles = new[]
{
    "GameSvr/Plugins/YanshenReplicaConfigForm.cs",
    "GameSvr/Plugins/YanshenConfigForm.cs",
    "GameSvr/Plugins/YanshenFixedReplicaPanels.cs",
    "GameSvr/Plugins/YanshenLegacy23ReplicaPanels.cs",
    "GameSvr/Plugins/YanshenConfig12ReplicaPanels.cs",
    "GameSvr/Plugins/YanshenReplicaSpecialPanels.cs",
    "GameSvr/Plugins/PluginConfigPanel.cs",
    "GameSvr/Plugins/PluginManagerForm.cs",
    "GameSvr/Plugins/PluginManager.cs",
    "GameSvr/Plugins/PluginHttpServer.cs",
    "GameSvr/Plugins/YanshenApi.cs",
    "GameSvr/MainForm.cs",
    "GameGate-CS/Forms/GgAcExactFeatureSettingsPage.cs",
};

Console.OutputEncoding = Encoding.UTF8;
var repo = FindRepoRoot();
var failures = new List<string>();

var rows = new Dictionary<string, string[]>();
foreach (var line in File.ReadAllLines(Path.Combine(repo, "docs", "ys_page1_census.tsv"), Encoding.UTF8).Skip(1))
{
    if (line.Length == 0)
    {
        continue;
    }
    var cells = line.Split('\t');
    rows[cells[0]] = cells;
}

// --- A. 一条补丁都不打
foreach (var (key, cells) in rows)
{
    if (cells[4] != "no")
    {
        failures.Add($"{key}: census says it owns a patch label; §2.1 claims the page owns none");
    }
    if (cells[2] == "?")
    {
        failures.Add($"{key}: config field offset unresolved");
    }
}

// --- B/C. 消费者集合必须与裁决一致，且两份转储一致
foreach (var (key, cells) in rows)
{
    var plain = cells[5];
    var delayed = cells[6];
    if (plain != delayed)
    {
        failures.Add($"{key}: consumer set differs between the two dumps ({plain} vs {delayed})");
    }
    var known = expectedConsumers.TryGetValue(key, out var va);
    if (known && !delayed.Contains(va, StringComparison.OrdinalIgnoreCase))
    {
        failures.Add($"{key}: expected consumer {va}, census has {delayed}");
    }
    if (inertFields.ContainsKey(key) && delayed != "-")
    {
        failures.Add($"{key}: expected no consumer anywhere, census has {delayed}");
    }
}

foreach (var (key, field) in inertFields)
{
    if (!rows.TryGetValue(key, out var cells))
    {
        failures.Add($"{key}: missing from the census");
        continue;
    }
    if (!string.Equals(cells[2].TrimStart('0', 'x').TrimStart('0'),
                       field.TrimStart('0', 'x').TrimStart('0'), StringComparison.OrdinalIgnoreCase))
    {
        failures.Add($"{key}: config field {cells[2]}, expected {field}");
    }
}

// --- B 的反臆造闸门：这 20 个键不得出现在任何引擎行为文件里
var labelSet = new HashSet<string>(labelOnlyRoles, StringComparer.OrdinalIgnoreCase);
var skipDirs = new HashSet<string>(new[] { ".git", "bin", "obj", "node_modules", ".vs", ".claude" },
                                   StringComparer.OrdinalIgnoreCase);

IEnumerable<string> Sources(string root)
{
    var stack = new Stack<string>();
    stack.Push(root);
    while (stack.Count > 0)
    {
        var dir = stack.Pop();
        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            if (!skipDirs.Contains(Path.GetFileName(sub)))
            {
                stack.Push(sub);
            }
        }
        foreach (var f in Directory.EnumerateFiles(dir, "*.cs"))
        {
            yield return f;
        }
    }
}

// 键名字面量只住在 YanshenApi.cs 与面板里；引擎代码是通过访问器读开关的（盘古3 的
// 中毒时间上限就是这样，行为文件里只有注释提到键名）。所以反臆造闸门要盯的是
// **访问器名**，口径与 tools/ys_key_reachability.py 的播种步骤一致。
var apiPath = Path.Combine(repo, "GameSvr", "Plugins", "YanshenApi.cs");
var apiText = File.ReadAllText(apiPath, Encoding.UTF8);
var memberDecl = new System.Text.RegularExpressions.Regex(
    @"^[ \t]*(?:(?:public|private|internal|protected|static|readonly|override|virtual|sealed|abstract|async|unsafe|extern|partial|new)\s+)*"
    + @"[\w<>\[\],?\.]+\s+(\w+)\s*[\(\{]|^[ \t]*(?:(?:public|private|internal|protected|static|readonly)\s+)+[\w<>\[\],?\.]+\s+(\w+)\s*=>",
    System.Text.RegularExpressions.RegexOptions.Multiline);
var notMembers = new HashSet<string>(new[]
{
    "if", "for", "foreach", "while", "switch", "catch", "using", "return",
    "lock", "fixed", "do", "else", "throw", "yield", "get", "set",
});
var memberStarts = new List<(int Pos, string Name)>();
foreach (System.Text.RegularExpressions.Match m in memberDecl.Matches(apiText))
{
    var name = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
    if (!notMembers.Contains(name))
    {
        memberStarts.Add((m.Index, name));
    }
}
memberStarts.Sort((a, b) => a.Pos.CompareTo(b.Pos));

string MemberAt(int pos)
{
    var lo = 0;
    var hi = memberStarts.Count - 1;
    var best = -1;
    while (lo <= hi)
    {
        var mid = (lo + hi) / 2;
        if (memberStarts[mid].Pos <= pos)
        {
            best = mid;
            lo = mid + 1;
        }
        else
        {
            hi = mid - 1;
        }
    }
    return best >= 0 ? memberStarts[best].Name : null;
}

// _keyMap 是纯别名管道，把 379 个键名全列一遍，证明不了任何事（同 ys_gui_matrix.py
// 的 keymap_span），必须从访问器归属里排掉，否则整块会被算到它前面那个方法头上。
var keyMapStart = apiText.IndexOf("_keyMap = new(", StringComparison.Ordinal);
var keyMapEnd = keyMapStart < 0 ? -1 : apiText.IndexOf("\n        };", keyMapStart, StringComparison.Ordinal);
if (keyMapStart < 0 || keyMapEnd < 0)
{
    failures.Add("YanshenApi._keyMap span not found; accessor attribution would be wrong");
}
// 生命周期样板不是访问器，误挂上去会把半个仓库判成臆造。
var genericNames = new HashSet<string>(new[] { "Dispose", "ToString", "Equals", "GetHashCode" },
                                       StringComparer.Ordinal);

var inertAccessors = new Dictionary<string, HashSet<string>>();
foreach (var key in inertFields.Keys)
{
    var needle = '"' + key + '"';
    var set = new HashSet<string>(StringComparer.Ordinal);
    for (var i = apiText.IndexOf(needle, StringComparison.Ordinal); i >= 0;
         i = apiText.IndexOf(needle, i + 1, StringComparison.Ordinal))
    {
        if (keyMapStart >= 0 && i >= keyMapStart && i < keyMapEnd)
        {
            continue;
        }
        var member = MemberAt(i);
        if (member != null && !genericNames.Contains(member))
        {
            set.Add(member);
        }
    }
    inertAccessors[key] = set;
    if (set.Count == 0)
    {
        failures.Add($"{key}: no YanshenApi accessor holds this key literal -- "
                     + "the anti-fabrication gate has nothing to watch");
    }
}
var watched = new Dictionary<string, string>(StringComparer.Ordinal);
foreach (var (key, set) in inertAccessors)
{
    foreach (var m in set)
    {
        watched[m] = key;
    }
}
var allApiMembers = new HashSet<string>(memberStarts.Select(m => m.Name), StringComparer.Ordinal);
var liveApiMembers = new HashSet<string>(StringComparer.Ordinal);
var identifier = new System.Text.RegularExpressions.Regex(@"\b([A-Za-z_]\w{2,})\b");

var invented = new List<string>();
var scanned = 0;
foreach (var file in Sources(repo))
{
    scanned++;
    var rel = Path.GetRelativePath(repo, file).Replace('\\', '/');
    if (labelSet.Contains(rel) || rel.StartsWith("AuditTools/", StringComparison.OrdinalIgnoreCase)
        || rel.StartsWith("GameSvr.Tests/", StringComparison.OrdinalIgnoreCase)
        || rel.StartsWith("ProtocolRegressionCheck/", StringComparison.OrdinalIgnoreCase))
    {
        continue;
    }
    string text;
    try
    {
        text = File.ReadAllText(file, Encoding.UTF8);
    }
    catch (IOException)
    {
        continue;
    }
    foreach (var key in inertFields.Keys)
    {
        if (text.Contains('"' + key + '"', StringComparison.Ordinal))
        {
            invented.Add($"{rel} names the literal \"{key}\"");
        }
    }
    foreach (System.Text.RegularExpressions.Match m in identifier.Matches(text))
    {
        var name = m.Groups[1].Value;
        if (allApiMembers.Contains(name))
        {
            liveApiMembers.Add(name);
        }
        if (watched.TryGetValue(name, out var owner))
        {
            invented.Add($"{rel} calls YanshenApi.{name} (accessor of \"{owner}\")");
        }
    }
}
foreach (var hit in invented.Distinct())
{
    failures.Add("anti-fabrication: " + hit
                 + " -- the original never reads this key; bring native bytes before implementing it");
}
// 自检：若引擎侧一个 YanshenApi 成员都点不到，说明扫描面塌了，上面那条
// 「20 个键零命中」的断言就没有意义。生产基线上活着的成员是几十上百个。
const int MinLiveApiMembers = 20;
if (liveApiMembers.Count < MinLiveApiMembers)
{
    failures.Add($"scanner self-test: only {liveApiMembers.Count} YanshenApi members are named by "
                 + $"engine sources (expected >= {MinLiveApiMembers}) -- the anti-fabrication gate "
                 + "is not actually looking at anything");
}

Console.WriteLine($"census rows: {rows.Count}   patch labels on this page: {rows.Values.Count(c => c[4] != "no")}");
Console.WriteLine($"keys with a plugin-side consumer: {expectedConsumers.Count} (fail-closed, host mount point unprovable)");
Console.WriteLine($"keys inert in the original: {inertFields.Count} (already 1:1, must stay unimplemented)");
Console.WriteLine($"source files scanned: {scanned}   watched accessors: {watched.Count}"
                  + $"   live YanshenApi members named by engine sources: {liveApiMembers.Count}");
Console.WriteLine("cutting arms (magicId -> S slot), pinned for whoever unblocks sub_100795C0:");
foreach (var arm in cuttingArms)
{
    Console.WriteLine($"  id {arm.MagicId,4}  {arm.Key,-8} cfg+{arm.Field}  S(1,{arm.SIndex})  tag [bank+{arm.TagSlot}] value [bank+{arm.ValueSlot}]");
}

if (failures.Count > 0)
{
    Console.WriteLine();
    foreach (var f in failures)
    {
        Console.WriteLine("FAIL " + f);
    }
    Console.WriteLine($"YanshenPage1CensusCheck FAIL ({failures.Count})");
    return 1;
}

Console.WriteLine("YanshenPage1CensusCheck PASS page=34keys patches=0 consumers=9 inert=20 fabrication=none");
return 0;
