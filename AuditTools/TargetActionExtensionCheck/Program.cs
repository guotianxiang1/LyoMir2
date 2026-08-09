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

var messageSource = File.ReadAllText(Path.Combine(gameRoot, "Players", "TPlayObject.Message.cs"));
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
Require(allSource.Contains("SendSocket(\"+LNG\")", StringComparison.Ordinal),
    "native long-hit text state path was removed");
Require(allSource.Contains("SendSocket(\"+WID\")", StringComparison.Ordinal),
    "native wide-hit text state path was removed");
Require(allSource.Contains("SendSocket(\"+UFIR\")", StringComparison.Ordinal),
    "native fire-hit text state path was removed");

Console.WriteLine(
    "TargetActionExtensionCheck PASS horse=RM_RUN slave=TURN/DISAPPEAR " +
    "combat=text-state joint=10017/18/19->60/61/62 non-target-active=0");
return 0;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static string FindRepositoryRoot()
{
    foreach (var start in new[]
             {
                 AppContext.BaseDirectory,
                 Environment.CurrentDirectory
             })
    {
        var current = new DirectoryInfo(start);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "GameSvr", "Players",
                    "TPlayObject.cs"))
                && File.Exists(Path.Combine(current.FullName, "SystemModule",
                    "Grobal2.cs")))
                return current.FullName;
            current = current.Parent;
        }
    }
    throw new DirectoryNotFoundException("repository root not found");
}
