using System.Text;

var root = ResolveRepositoryRoot(args);
var failures = new List<string>();

CheckCastleTax(root);
CheckCastleMemberPrice(root);
CheckRepairArithmetic(root);
CheckStruckDamagePoisonPath(root);

if (failures.Count != 0)
{
    Console.Error.WriteLine("FAIL NativeEconomyArithmeticCheck");
    foreach (var failure in failures)
        Console.Error.WriteLine("  " + failure);
    return 1;
}

Console.WriteLine("PASS NativeEconomyArithmeticCheck");
Console.WriteLine("  castle tax/member rates use floating /100.0");
Console.WriteLine("  repair quote and execution share the native x87 divide/multiply helper");
Console.WriteLine("  StruckDamage uses the native 1.3/1.25/1.2 poison-state path; stale integer config scaling is absent");
return 0;

void CheckCastleTax(string repository)
{
    var source = Read(repository, "GameSvr", "Castle", "UserCastle.cs");
    var block = Slice(source, "public void IncRateGold(", "public int WithDrawalGolds(");
    var code = StripComments(block);
    Require(code.Contains("nCastleTaxRate / 100.0", StringComparison.Ordinal),
        "IncRateGold lost the floating castle-tax divisor (/100.0)");
    Reject(code.Contains("nCastleTaxRate / 100)", StringComparison.Ordinal),
        "IncRateGold contains the integer-dividing castle-tax form (/100)");
}

void CheckCastleMemberPrice(string repository)
{
    var source = Read(repository, "GameSvr", "Npcs", "Merchant.cs");
    var block = Slice(source, "private int GetUserPrice(", "private double GetUserItemPrice(");
    var code = StripComments(block);
    Require(code.Contains("nCastleMemberPriceRate / 100.0", StringComparison.Ordinal),
        "GetUserPrice lost the floating castle-member divisor (/100.0)");
    Reject(code.Contains("nCastleMemberPriceRate / 100)", StringComparison.Ordinal),
        "GetUserPrice contains the integer-dividing castle-member form (/100)");
}

void CheckRepairArithmetic(string repository)
{
    var source = Read(repository, "GameSvr", "Npcs", "Merchant.cs");
    var helper = Slice(source, "private int GetNativeRepairPrice(",
        "public void ClientQueryRepairCost(");
    var code = StripComments(helper);
    Require(code.Contains("RoundX87DivideThenMultiply(", StringComparison.Ordinal),
        "repair execution no longer uses the native staged x87 divide/multiply helper");
    Require(code.Contains("nPrice / 3, UserItem.DuraMax", StringComparison.Ordinal),
        "repair execution lost the native integer /3 followed by floating /DuraMax stage");
    Reject(code.Contains("HUtil32.Round(((nPrice / 3) /", StringComparison.Ordinal),
        "repair execution reverted to the integer-dividing /DuraMax expression");
}

void CheckStruckDamagePoisonPath(string repository)
{
    var source = Read(repository, "GameSvr", "Actors", "TBaseObject.cs");
    var struck = Slice(source, "public void StruckDamage(int nDamage, TBaseObject attacker)",
        "public virtual string GeTBaseObjectInfo(");
    var struckCode = StripComments(struck);
    Require(struckCode.Contains("ApplyNativeStruckAmplifyStates(ref nDam, ref nDamage)",
            StringComparison.Ordinal),
        "StruckDamage is missing the native poison-state amplification stage");
    Reject(struckCode.Contains("g_Config.nPosionDamagarmor / 10)", StringComparison.Ordinal),
        "StruckDamage contains the stale integer-dividing poison armor multiplier");

    var helper = Read(repository, "GameSvr", "Actors", "TBaseObject.NativeMagicMidStates.cs");
    Require(helper.Contains("multiplier = value == 4 ? 1.25d : 1.2d", StringComparison.Ordinal),
        "native red-poison level scaling (1.25/1.2) is missing");
    Require(helper.Contains("damage = RoundNativeX87(damage * multiplier)", StringComparison.Ordinal),
        "native poison amplification lost x87 round-half-to-even arithmetic");
}

string Read(string repository, params string[] parts) =>
    File.ReadAllText(Path.Combine(new[] { repository }.Concat(parts).ToArray()),
        Encoding.UTF8);

string Slice(string source, string startMarker, string endMarker)
{
    var start = source.IndexOf(startMarker, StringComparison.Ordinal);
    if (start < 0)
    {
        failures.Add("missing source marker: " + startMarker);
        return string.Empty;
    }

    var end = source.IndexOf(endMarker, start + startMarker.Length,
        StringComparison.Ordinal);
    if (end < 0)
    {
        failures.Add("missing source marker: " + endMarker);
        return source[start..];
    }

    return source[start..end];
}

string StripComments(string source)
{
    var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace('\r', '\n').Split('\n');
    var output = new StringBuilder();
    var inBlock = false;
    foreach (var line in lines)
    {
        var text = line;
        if (inBlock)
        {
            var close = text.IndexOf("*/", StringComparison.Ordinal);
            if (close < 0)
                continue;
            text = text[(close + 2)..];
            inBlock = false;
        }

        while (true)
        {
            var open = text.IndexOf("/*", StringComparison.Ordinal);
            var lineComment = text.IndexOf("//", StringComparison.Ordinal);
            if (lineComment >= 0 && (open < 0 || lineComment < open))
            {
                text = text[..lineComment];
                break;
            }
            if (open < 0)
                break;
            var close = text.IndexOf("*/", open + 2, StringComparison.Ordinal);
            if (close < 0)
            {
                text = text[..open];
                inBlock = true;
                break;
            }
            text = text[..open] + text[(close + 2)..];
        }
        output.AppendLine(text);
    }
    return output.ToString();
}

void Require(bool condition, string message)
{
    if (!condition)
        failures.Add(message);
}

void Reject(bool condition, string message)
{
    if (condition)
        failures.Add(message);
}

string ResolveRepositoryRoot(string[] arguments)
{
    if (arguments.Length > 0 && IsRepositoryRoot(arguments[0]))
        return Path.GetFullPath(arguments[0]);

    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory != null)
    {
        if (IsRepositoryRoot(directory.FullName))
            return directory.FullName;
        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException(
        "repository root not found; pass the LyoMir2 root as the first argument");
}

bool IsRepositoryRoot(string path) =>
    Directory.Exists(path) &&
    File.Exists(Path.Combine(path, "GameSvr", "GameSvr.csproj")) &&
    File.Exists(Path.Combine(path, "SystemModule", "SystemModule.csproj"));
