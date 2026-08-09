using System.Text.RegularExpressions;

var root = FindRepositoryRoot();
var bridgePath = Path.Combine(root, "GameSvr", "ScriptSystem", "PasEngine",
    "PasApiBridge.cs");
var bridge = File.ReadAllText(bridgePath);
var methods = Slice(bridge, "public bool CallNpcMethod",
    "public bool CallNpcFunc");
var functions = Slice(bridge, "public bool CallNpcFunc",
    "public bool CallStandaloneFunction");

CheckCaseCount(bridge, "clientaskopenyb", 2);
CheckCaseCount(bridge, "clientaskybduanzao", 2);
CheckCaseCount(bridge, "buywinefromnpc", 2);
CheckCaseCount(bridge, "buylfbag", 1);

CheckFailClosed(methods, "clientaskopenyb");
CheckFailClosed(methods, "clientaskybduanzao");
CheckFailClosed(methods, "buywinefromnpc");
CheckFailClosed(methods, "buylfbag");
CheckFailClosed(functions, "clientaskopenyb");
CheckFailClosed(functions, "clientaskybduanzao");
CheckFailClosed(functions, "buywinefromnpc");

Assert(!functions.Contains("case \"buylfbag\":", StringComparison.Ordinal),
    "BuyLfBag acquired a non-native function dispatch");

Console.WriteLine(
    "YbDbPasSubstituteFailClosedCheck PASS cases=7 " +
    "procedures=OpenYB+DuanZao+Wine+LfBag substitutes=none");
return;

void CheckCaseCount(string source, string name, int expected)
{
    var count = Regex.Matches(source, $"case \\\"{Regex.Escape(name)}\\\":",
        RegexOptions.CultureInvariant).Count;
    Assert(count == expected,
        $"{name} case count mismatch: expected {expected}, actual {count}");
}

void CheckFailClosed(string region, string name)
{
    var body = ExtractCase(region, name);
    Require(body, "return RejectUnsupportedNativeApi(out result);",
        $"{name} is not fail-closed");

    foreach (var forbidden in new[]
             {
                 "SendMsg(", "GotoLable_GiveItem(", "GetPlayerVar(",
                 "SetPlayerVar(", "SysMsg(", "@openyb", "@ybduanzao"
             })
    {
        Assert(!body.Contains(forbidden, StringComparison.Ordinal),
            $"{name} retains local substitute: {forbidden}");
    }
}

string ExtractCase(string region, string name)
{
    var marker = $"case \"{name}\":";
    var start = region.IndexOf(marker, StringComparison.Ordinal);
    Assert(start >= 0, $"missing case: {name}");
    var next = region.IndexOf("case \"", start + marker.Length,
        StringComparison.Ordinal);
    return next < 0 ? region[start..] : region[start..next];
}

string Slice(string source, string startMarker, string endMarker)
{
    var start = source.IndexOf(startMarker, StringComparison.Ordinal);
    Assert(start >= 0, $"missing marker: {startMarker}");
    var end = source.IndexOf(endMarker, start + startMarker.Length,
        StringComparison.Ordinal);
    Assert(end > start, $"missing marker: {endMarker}");
    return source[start..end];
}

void Require(string source, string value, string message)
{
    Assert(source.Contains(value, StringComparison.Ordinal), message);
}

void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

string FindRepositoryRoot()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current != null)
    {
        if (File.Exists(Path.Combine(current.FullName, "GameSvr", "GameSvr.csproj")))
            return current.FullName;
        current = current.Parent;
    }

    current = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (current != null)
    {
        if (File.Exists(Path.Combine(current.FullName, "GameSvr", "GameSvr.csproj")))
            return current.FullName;
        current = current.Parent;
    }

    throw new DirectoryNotFoundException("repository root not found");
}
