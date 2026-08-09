// ProvenanceGuardCheck —— 溯源闸:禁止把 ref-MIR2/GameOfMir 的结论当成"原版(战神)"事实写进代码。
//
// 为什么需要这道闸(真实代价):
//   本项目最初是照着 staging/ref-MIR2/GameOfMir 的 Delphi 源写的 —— 那是【另一个 Mir2 分支,不是战神】。
//   它的结论被以"原版 ObjBase.pas:15594 ..."这种措辞固化进注释,读者(包括后来的 AI 与人)会当成战神事实,
//   于是同一个错误被反复放大。已造成的真实错误至少四起:
//     1) 仓库容量按 ref 的 `< 39` 夹回 → 战神实为 4 页 192 格,差点让玩家丢仓库物品(用户当场拦下)。
//     2) 组队经验按 ref 改成浮点 → 战神是整数、先乘后除、且有 `+1`,方向反了。
//     3) 有扫描行主张 IncGold 上限应为 g_Config.nHumanMaxGold → 战神封顶用逐角色 [+0x68C],照改会【制造】背离。
//     4) DoSpell_GetPower 按 ref 改成除以 (btTrainLv+1) → 战神 sub_4C8658 用硬编码 4.0。
//
// 规则(判据优先级):
//   * Tier-1 = 战神二进制证据:反汇编行、sub_XXXXXX / 0xXXXXXX 地址、staging/ida_*、*_exact_*、
//     DBSvr/Core/Native*Codec.cs 的精确偏移、断言原生字节契约且 PASS 的审计。
//   * ref-MIR2 只能作为【算术形态线索】(`/` 还是 `div`、乘除顺序、是否 Round),
//     【绝不能】作为"原版有没有某功能 / 某上限是多少 / 某分支存不存在"的依据。
//
// 本闸做什么:
//   凡代码注释引用 ref 源文件名(ObjBase.pas 等)时,必须在同一注释块内显式标注来源非战神
//   (出现 PROVENANCE_MARKERS 之一),否则 FAIL。这样 ref 线索仍可保留,但再也无法冒充战神事实。

using System.Text;
using System.Text.RegularExpressions;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var root = args.Length > 0
    ? Path.GetFullPath(args[0])
    : FindRepositoryRoot();

// ref-MIR2 / GameOfMir 分支的 Delphi 源文件名。引用它们本身不是错,
// 错的是不标注来源、让读者以为是战神。
var refSourceFiles = new[]
{
    "ObjBase.pas", "ObjNpc.pas", "UsrEngn.pas", "Castle.pas",
    "Magic.pas", "ObjMon.pas", "ObjPlay.pas", "Guild.pas",
    "ref-MIR2", "GameOfMir", "ref-MirServer",
};

// 只要注释块内出现其中任一标记,即视为已如实标注来源。
var provenanceMarkers = new[]
{
    "非战神", "不是战神", "GameOfMir 参考分支", "参考分支(非战神)",
    "ref-only", "REF-ONLY", "UNVERIFIED", "未经战神",
    "仅算术形态", "算术形态线索",
};

// 已用战神证据独立验证过的注释可以豁免:注释块内同时给出 sub_XXXXXX 或 0xXXXXXX 地址,
// 说明结论有战神二进制支撑,ref 只是并列引用。
var nativeEvidence = new Regex(@"sub_[0-9A-Fa-f]{6}|0x00[0-9A-Fa-f]{5,6}|@0x[0-9A-Fa-f]{6}",
    RegexOptions.Compiled);

var scanRoots = new[] { "GameSvr", "SystemModule", "LoginGate", "GameGate-CS" };
var violations = new List<string>();
var annotated = 0;
var nativeBacked = 0;
var filesScanned = 0;

foreach (var scanRoot in scanRoots)
{
    var dir = Path.Combine(root, scanRoot);
    if (!Directory.Exists(dir)) continue;

    foreach (var path in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
    {
        filesScanned++;
        var lines = File.ReadAllLines(path);
        for (var i = 0; i < lines.Length; i++)
        {
            var hit = refSourceFiles.FirstOrDefault(f =>
                lines[i].Contains(f, StringComparison.Ordinal));
            if (hit == null) continue;

            // 取该行前后各 6 行作为"注释块"上下文 —— 标注可能在块首。
            var lo = Math.Max(0, i - 6);
            var hi = Math.Min(lines.Length - 1, i + 6);
            var block = string.Join('\n', lines[lo..(hi + 1)]);

            if (provenanceMarkers.Any(m => block.Contains(m, StringComparison.Ordinal)))
            {
                annotated++;
                continue;
            }
            if (nativeEvidence.IsMatch(block))
            {
                nativeBacked++;
                continue;
            }

            violations.Add(
                $"{Path.GetRelativePath(root, path)}:{i + 1} 引用 ref 源 '{hit}' " +
                $"但未标注来源非战神,也未给出战神 EA(sub_XXXXXX/0xXXXXXX)。\n" +
                $"      → 加标注(如「来源=GameOfMir 参考分支,非战神,仅算术形态线索」)" +
                $"或补战神反汇编地址。\n      行内容: {lines[i].Trim()}");
        }
    }
}

if (violations.Count > 0)
{
    throw new InvalidOperationException(
        $"ProvenanceGuardCheck FAIL —— {violations.Count} 处把 ref-MIR2/GameOfMir 结论当作\"原版\"事实:\n\n"
        + string.Join("\n\n", violations)
        + "\n\n判据:ref-MIR2 不是战神,只能作算术形态线索;"
        + "\"原版有没有/上限多少/分支存不存在\"必须引战神二进制证据。");
}

// 空扫描不是通过。若 filesScanned 为 0(例如传了错误的根目录),旧版仍打印
// "PASS files=0",看起来是绿的却什么都没检查 —— 2026-08-04 一个子代理正是这样
// 报了 files=0 的假绿。守卫必须先证明自己确实扫到了东西。
if (filesScanned == 0)
{
    throw new InvalidOperationException(
        "ProvenanceGuardCheck FAIL —— 扫描到 0 个源文件,这是空跑不是通过。" +
        "请确认传入的仓库根目录正确(应含 GameSvr/ 等源码树)。");
}

Console.WriteLine(
    $"ProvenanceGuardCheck PASS files={filesScanned} refCitations={annotated + nativeBacked} " +
    $"annotated={annotated} nativeBacked={nativeBacked} unprovenanced=0");
return;

static string FindRepositoryRoot()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir != null)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, "GameSvr")))
            return dir.FullName;
        dir = dir.Parent;
    }
    throw new DirectoryNotFoundException("找不到仓库根(含 GameSvr 的目录)");
}
