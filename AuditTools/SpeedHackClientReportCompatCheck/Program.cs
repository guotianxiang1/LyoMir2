using System.Text.RegularExpressions;

var root = AuditRepoRoot.Resolve(args);
var globalPath = Path.Combine(root, "SystemModule", "Grobal2.cs");
var messagePath = Path.Combine(root, "GameSvr", "Players",
    "TPlayObject.Message.cs");
if (!File.Exists(globalPath) || !File.Exists(messagePath))
{
    Console.WriteLine("SKIP: Grobal2.cs / TPlayObject.Message.cs not found under " +
                      root);
    return 0;
}

var globals = File.ReadAllText(globalPath);
Check(Regex.IsMatch(globals,
        @"public\s+const\s+int\s+CM_SPEEDHACKUSER\s*=\s*1042\s*;",
        RegexOptions.CultureInvariant),
    "CM_SPEEDHACKUSER must remain 1042/0x412");

var source = File.ReadAllText(messagePath);
var start = source.IndexOf("case Grobal2.CM_SPEEDHACKUSER:",
    StringComparison.Ordinal);
var end = source.IndexOf("case Grobal2.CM_ADJUST_BONUS:", start,
    StringComparison.Ordinal);
Check(start >= 0 && end > start,
    "CM_SPEEDHACKUSER branch boundaries are missing");
var branch = source[start..end];
const string warning =
    "[Warning]: [使用加速外挂程序(客户端)] ";
var expected = Regex.Escape("case Grobal2.CM_SPEEDHACKUSER:")
               + @"\s*" + Regex.Escape(
                   "M2Share.MainOutMessage(\"" + warning + "\");")
               + @"\s*break;\s*";
Check(Regex.IsMatch(branch, "^" + expected + "$",
        RegexOptions.CultureInvariant),
    "1042 must only emit the exact native warning and break");
Check(warning.EndsWith(' '), "native warning lost its trailing space");

Console.WriteLine(
    "PASS SpeedHackClientReportCompatCheck command=1042 " +
    "effect=single-main-log state=unchanged response=none");
return 0;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
