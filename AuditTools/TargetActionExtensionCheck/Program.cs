var root = FindRepositoryRoot();
var gameRoot = Path.Combine(root, "GameSvr");
var sources = Directory.GetFiles(gameRoot, "*.cs", SearchOption.AllDirectories)
    .Where(path => !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
        && !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
    .ToArray();

var forbidden = new[]
{
    "RM_HORSERUN", "SM_HORSERUN",
    "RM_SLAVE_BORN", "SM_SLAVE_BORN",
    "RM_SLAVE_VANISH", "SM_SLAVE_VANISH",
    "RM_FIREON", "SM_FIREON",
    "RM_SWORDHIT_ON",
    "RM_LNGHITONOFF", "SM_LNGHITONOFF",
    "RM_WIDEHITONOFF", "SM_WIDEHITONOFF",
    "RM_SHOWBODY_EFFECT", "RM_BIGMONMAGIC", "RM_NPCWALK",
    "RM_HUNDREDHIT", "RM_SQUARE_HIT", "RM_HORIZONHIT"
};

foreach (var file in sources)
{
    var source = File.ReadAllText(file);
    foreach (var symbol in forbidden)
    {
        if (source.Contains("Grobal2." + symbol, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"non-target action symbol remains active: {symbol} in {Path.GetRelativePath(root, file)}");
    }
}

var playerSource = File.ReadAllText(Path.Combine(gameRoot, "Players", "TPlayObject.cs"));
Require(playerSource.Contains("if (Walk(Grobal2.RM_RUN))", StringComparison.Ordinal),
    "horse run no longer routes through native RM_RUN");

// The multi-line pattern below is written with '\n'; the checkout is CRLF, so
// match against a normalized copy rather than the raw bytes.
var messageSource = File.ReadAllText(Path.Combine(gameRoot, "Players",
    "TPlayObject.Message.cs")).Replace("\r\n", "\n");
Require(messageSource.Contains("case Grobal2.RM_RUN:", StringComparison.Ordinal),
    "native RM_RUN dispatcher is missing");
Require(messageSource.Contains("case Grobal2.RM_TURN:", StringComparison.Ordinal),
    "native visibility RM_TURN dispatcher is missing");
Require(messageSource.Contains("case Grobal2.RM_DISAPPEAR:", StringComparison.Ordinal),
    "native RM_DISAPPEAR dispatcher is missing");
foreach (var symbol in new[] { "RM_WWJATTACK", "RM_WSJATTACK", "RM_WTJATTACK" })
{
    Require(messageSource.Contains("case Grobal2." + symbol + ":",
            StringComparison.Ordinal),
        $"native joint-attack dispatcher is missing: {symbol}");
}
Require(messageSource.Contains("ProcessMsg.nParam1,\n                            ProcessMsg.nParam2, ProcessMsg.wParam)",
        StringComparison.Ordinal),
    "joint-attack direction/x/y fields are not preserved");

var globalSource = File.ReadAllText(Path.Combine(root, "SystemModule", "Grobal2.cs"));
var expectedConstants = new[]
{
    "public const int SM_COMMON_INFORMATION = 2821;",
    "public const int SM_SAFE_ZONE_INFO = 4230;",
    "public const int SM_DRINKEXP_STATUS = 2818;",
    "public const int SM_DRINK_STATUS = 2816;",
    "public const int SM_DRINK_DRUG_STATUS = 2817;",
    "public const int CM_SWORD_HIT = 3002;",
    "public const int SM_SWORD_HIT = 2;",
    "public const int SM_SWORDHIT_ON = 2819;",
    "public const int SM_PHYSICAL_ATT = 1230;",
    "public const int SM_WWJATTACK = 60;",
    "public const int SM_WSJATTACK = 61;",
    "public const int SM_WTJATTACK = 62;",
    "public const int RM_WWJATTACK = 10017;",
    "public const int RM_WSJATTACK = 10018;",
    "public const int RM_WTJATTACK = 10019;"
};
foreach (var declaration in expectedConstants)
{
    Require(globalSource.Contains(declaration, StringComparison.Ordinal),
        $"target protocol constant is missing: {declaration}");
}
var removedRmSymbols = new[]
{
    "RM_SHOWBODY_EFFECT", "RM_BIGMONMAGIC", "RM_NPCWALK",
    "RM_HUNDREDHIT", "RM_SQUARE_HIT", "RM_HORIZONHIT"
};
foreach (var symbol in removedRmSymbols)
{
    Require(!globalSource.Contains("public const int " + symbol + " =", StringComparison.Ordinal),
        $"non-target RM constant remains declared: {symbol}");
}
foreach (var symbol in new[]
         {
             "RM_UNITEHIT", "SM_UNITEHIT0", "SM_UNITEHIT1", "SM_UNITEHIT2"
         })
{
    Require(!globalSource.Contains("public const int " + symbol + " =",
            StringComparison.Ordinal),
        $"invented joint-attack constant remains declared: {symbol}");
}

var allSource = string.Join('\n', sources.Select(File.ReadAllText));
// 长刺(+LNG)/烈火(+UFIR) 的「文本状态包」是臆造物，已被换成原生的 ident 包。
// 底本证据：
//   "+UFIR" / "+FIR" 在 flat_image.bin 里 0 命中；
//   "+LNG" 只有 1 处 0x011A86D0，落在 CODE 段之外且 dword 引用数为 0（死数据）。
// 真正的发包：
//   0x006B224A c6 86 94 00 00 00 01  mov byte [esi+0x94],1
//   0x006B2251 6a 01 / 6a 00 x3      push 1,0,0,0
//   0x006B2259 33 c9                 xor ecx,ecx
//   0x006B225B 66 ba 70 02           mov dx,0x270   (SM_THRUSTING)
//   0x006B2263 ff 93 50 02 00 00     call [ebx+0x250]
//   0x006B2F33 c6 80 96 00 00 00 00  mov byte [eax+0x96],0
//   0x006B2F42 b9 01 00 00 00        mov ecx,1
//   0x006B2F47 66 ba 72 02           mov dx,0x272   (SM_FIREHITSKILL)
//   0x006B2F50 ff 93 50 02 00 00     call [ebx+0x250]
Require(!allSource.Contains("SendSocket(\"+LNG\")", StringComparison.Ordinal),
    "invented long-hit text state packet is back");
Require(!allSource.Contains("SendSocket(\"+UFIR\")", StringComparison.Ordinal),
    "invented fire-hit text state packet is back");
Require(allSource.Contains("Grobal2.SM_THRUSTING", StringComparison.Ordinal),
    "native SM_THRUSTING(0x270) long-hit state packet was removed");
Require(allSource.Contains("Grobal2.SM_FIREHITSKILL", StringComparison.Ordinal),
    "native SM_FIREHITSKILL(0x272) fire-hit state packet was removed");
// 半月(+WID/+UWID) 的两条串在底本里同样 0 命中，但 SKILL_REDBANWOL(56) 这条臂
// 是否该整条删掉（TPlayObject.Attack.cs 的注释说 56 落在默认臂 0x6BCCA6、原生
// 什么都不发）尚未定案，这里先钉住现状，见 docs/audit_triage_C_20260814.md。
Require(allSource.Contains("SendSocket(\"+WID\")", StringComparison.Ordinal),
    "wide-hit text state path changed without a decision on skill 56");

Console.WriteLine(
    "TargetActionExtensionCheck PASS horse=RM_RUN slave=TURN/DISAPPEAR " +
    "combat=text-state joint=10017/18/19->60/61/62 non-target-active=0");
return 0;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static string FindRepositoryRoot() => AuditRepoRoot.Resolve();
