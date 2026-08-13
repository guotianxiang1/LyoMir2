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

// 闸门先自证:块级取窗必须仍然抓得住"块内确实没有战神 EA"的真违规。
// 见文件末尾 SelfTest —— 任一反例失灵就直接退出,不允许带病扫描。
SelfTest(provenanceMarkers, nativeEvidence);

var scanRoots = new[] { "GameSvr", "SystemModule", "LoginGate", "GameGate-CS" };
var violations = new List<string>();
var annotated = 0;
var nativeBacked = 0;
var blockExtended = 0;
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

            var block = ContextBlock(lines, i);
            if (provenanceMarkers.Any(m => block.Contains(m, StringComparison.Ordinal)))
            {
                annotated++;
                continue;
            }
            if (nativeEvidence.IsMatch(block))
            {
                nativeBacked++;
                if (!nativeEvidence.IsMatch(NeighbourhoodOnly(lines, i))) blockExtended++;
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

// blockExtended = 只因把窗口从 ±6 行放宽到整个注释块才判为合规的条数。
// 这是本闸唯一被放宽的口子,显式计数,便于复核它有没有悄悄膨胀。
Console.WriteLine(
    $"ProvenanceGuardCheck PASS files={filesScanned} refCitations={annotated + nativeBacked} " +
    $"annotated={annotated} nativeBacked={nativeBacked} blockExtended={blockExtended} " +
    "unprovenanced=0");
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

// 本闸头部写的判据是"必须在【同一注释块】内标注/给证",旧实现拿 ±6 行邻域近似它。
// 真实注释块远比 13 行长,近似会差一行就误报:TBaseObject.Base.cs 的 MakeGhost 文档块
// 从 1518 行铺到 1555 行,战神 EA(sub_768060 / sub_7681B4 / 0x7680E9 ...)密集列在块首,
// 两条 Delphi 引用是块尾"三条独立旁证"的第 (3) 条;引用行 1545 的 ±6 窗口是 1539..1551,
// 恰好把最近的 sub_67C150(1538 行)挡在外面。这里改成按注释块取窗,落实原本的判据。
//
// 取【并集】而不是直接替换邻域:邻域是旧口径的下限,保留它才能保证凡是旧实现抓得到的
// 违规现在依然抓得到(引用写在代码行上、证据在相邻代码里的情形也不会新报)。
static string ContextBlock(string[] lines, int index)
{
    var (blockLo, blockHi) = CommentBlockRange(lines, index);
    var lo = Math.Min(blockLo, Math.Max(0, index - 6));
    var hi = Math.Max(blockHi, Math.Min(lines.Length - 1, index + 6));
    return string.Join('\n', lines[lo..(hi + 1)]);
}

static string NeighbourhoodOnly(string[] lines, int index)
{
    var lo = Math.Max(0, index - 6);
    var hi = Math.Min(lines.Length - 1, index + 6);
    return string.Join('\n', lines[lo..(hi + 1)]);
}

// 注释块 = 以该行为中心、上下连续且【同类】的注释行。代码行、空行、以及从 /// 切换到 //
// 都会截断,所以块不会越过一段代码去够到隔壁注释里的地址。引用行本身不是注释时返回单行,
// 由 ContextBlock 退回旧邻域口径。
static (int Lo, int Hi) CommentBlockRange(string[] lines, int index)
{
    var kind = CommentKind(lines[index]);
    if (kind == CommentLineKind.NotComment) return (index, index);

    var lo = index;
    while (lo - 1 >= 0 && CommentKind(lines[lo - 1]) == kind) lo--;
    var hi = index;
    while (hi + 1 < lines.Length && CommentKind(lines[hi + 1]) == kind) hi++;
    return (lo, hi);
}

static CommentLineKind CommentKind(string line)
{
    var trimmed = line.TrimStart();
    if (trimmed.StartsWith("///", StringComparison.Ordinal)) return CommentLineKind.Doc;
    if (trimmed.StartsWith("//", StringComparison.Ordinal)) return CommentLineKind.Line;
    return CommentLineKind.NotComment;
}

// 块级取窗放宽了判据,所以必须证明它没被放宽到失去意义。四个反例每次运行都跑:
// 块内无 EA 要照样报;隔着代码的另一个块里的 EA 不算数;旧邻域口径不能退化。
static void SelfTest(string[] markers, Regex nativeEvidence)
{
    // (1) 真误报:EA 在同一 /// 块块首,距引用行 12 行 —— 旧邻域够不到,块级应判合规。
    var farEvidenceSameBlock = new[]
    {
        "        /// <summary>",
        "        /// 战神 TCreature.MarkDelete sub_768060",
        "        /// filler 1", "        /// filler 2", "        /// filler 3",
        "        /// filler 4", "        /// filler 5", "        /// filler 6",
        "        /// filler 7", "        /// filler 8", "        /// filler 9",
        "        /// 三条独立旁证之 (3):",
        "        /// staging/ref-MirServer-Delphi/EM2Engine/ObjBase.pas:18605",
        "        /// </summary>",
    };
    Expect(nativeEvidence.IsMatch(ContextBlock(farEvidenceSameBlock, 12)),
        "块内 12 行开外的 sub_XXXXXX 应当算数");
    Expect(!nativeEvidence.IsMatch(NeighbourhoodOnly(farEvidenceSameBlock, 12)),
        "该反例必须是旧邻域口径抓不到的,否则它证明不了任何事");

    // (2) 反例甲:整个 /// 块里既无 EA 也无来源标注 —— 必须仍然是违规。
    var noEvidenceAnywhere = new[]
    {
        "        /// <summary>",
        "        /// 仓库容量上限", "        /// filler 1", "        /// filler 2",
        "        /// filler 3", "        /// filler 4", "        /// filler 5",
        "        /// filler 6", "        /// filler 7", "        /// filler 8",
        "        /// filler 9", "        /// 依据:",
        "        /// staging/ref-MIR2/GameOfMir/M2Server/ObjBase.pas:15594",
        "        /// </summary>",
    };
    var block2 = ContextBlock(noEvidenceAnywhere, 12);
    Expect(!nativeEvidence.IsMatch(block2) &&
           !markers.Any(m => block2.Contains(m, StringComparison.Ordinal)),
        "块内无 EA 无标注的引用必须仍判违规");

    // (3) 反例乙:EA 在【另一个】注释块里,中间隔着代码行 —— 不得被够到。
    var evidenceInNeighbourBlock = new[]
    {
        "        // 战神 sub_4C8658 硬编码 4.0",
        "        private const double Power = 4.0;",
        "",
        "        /// <summary>",
        "        /// filler 1", "        /// filler 2", "        /// filler 3",
        "        /// filler 4", "        /// filler 5", "        /// filler 6",
        "        /// filler 7", "        /// filler 8",
        "        /// staging/ref-MIR2/GameOfMir/M2Server/ObjBase.pas:15594",
        "        /// </summary>",
    };
    Expect(!nativeEvidence.IsMatch(ContextBlock(evidenceInNeighbourBlock, 12)),
        "隔着代码行的另一个注释块里的 EA 不得算数");

    // (4) 旧邻域口径是下限,不能因为改块级就退化:引用写在代码行(字符串字面量)上时,
    //     相邻代码里的 EA 仍应算数。
    var citationOnCodeLine = new[]
    {
        "        var note = \"see ObjBase.pas:15594\";",
        "        // sub_4C8658",
    };
    Expect(nativeEvidence.IsMatch(ContextBlock(citationOnCodeLine, 0)),
        "非注释行的引用必须仍按 ±6 行邻域取证");
}

static void Expect(bool condition, string what)
{
    if (!condition)
        throw new InvalidOperationException(
            $"ProvenanceGuardCheck 自检失败 —— {what}。取窗逻辑已失效,拒绝扫描。");
}

internal enum CommentLineKind
{
    NotComment,
    Doc,
    Line,
}
