using System.Text.RegularExpressions;

var root = FindRepositoryRoot();
var bridge = File.ReadAllText(Path.Combine(root, "GameSvr", "ScriptSystem",
    "PasEngine", "PasApiBridge.cs"));
var interpreter = File.ReadAllText(Path.Combine(root, "GameSvr", "ScriptSystem",
    "PasEngine", "PasInterpreter.cs"));
var monItem = File.ReadAllText(Path.Combine(root, "SystemModule", "Data",
    "TMonItem.cs"));
var localDb = File.ReadAllText(Path.Combine(root, "GameSvr", "LocalDB.cs"));
var userEngine = File.ReadAllText(Path.Combine(root, "GameSvr", "UsrSystem",
    "UsrEngn.cs"));

var standalone = Slice(bridge, "public bool CallStandaloneFunction",
    "public bool TryCallThisPlayerFunc");
var caseCount = Regex.Matches(standalone, "case \\\"chgmonitempercent\\\":",
    RegexOptions.CultureInvariant).Count;
Assert(caseCount == 1,
    $"ChgMonItemPercent dispatch count mismatch: expected 1, actual {caseCount}");

var rejectedCase = Slice(standalone, "case \"chgmonitempercent\":",
    "case \"serversay\":");
Require(rejectedCase, "return RejectUnsupportedNativeApi(out result);",
    "ChgMonItemPercent is no longer fail-closed");
foreach (var forbidden in new[]
         {
             "MonsterList", "ItemList", "MaxPoint", "SelPoint", "SetGlobalVar",
             "SetPlayerVar", "GetDropRateConfig", "ApplyMyJsonConfig", "File.Write"
         })
{
    Assert(!rejectedCase.Contains(forbidden, StringComparison.Ordinal),
        $"ChgMonItemPercent acquired an unproved substitute: {forbidden}");
}

var builtins = Slice(interpreter, "_builtinFuncs =", "// Initialize global constants");
Assert(!builtins.Contains("ChgMonItemPercent", StringComparison.OrdinalIgnoreCase),
    "ChgMonItemPercent was added as a permissive interpreter builtin");
var executeCall = Slice(interpreter, "private PasValue ExecuteCall",
    "private bool TryInvokeGlobalAt");
Require(executeCall, "throw new PasRuntimeException",
    "unresolved global calls no longer fail closed at runtime");

foreach (var field in new[] { "MaxPoint", "SelPoint", "ItemName", "Count" })
    Require(monItem, field, $"native TMonItem field disappeared: {field}");
Require(localDb, "SelPoint = n18 - 1", "native MonItems numerator mapping changed");
Require(localDb, "MaxPoint = n1C", "native MonItems denominator mapping changed");
Require(userEngine, "Random(MonItem.MaxPoint) <= MonItem.SelPoint",
    "native monster drop selection changed");

Console.WriteLine(
    "ChgMonItemPercentStaticCheck PASS dispatch=fail-closed " +
    "substitute=none native-drop-shape=verified");
return;

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
    foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
    {
        var current = new DirectoryInfo(start);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "GameSvr", "GameSvr.csproj")))
                return current.FullName;
            current = current.Parent;
        }
    }

    throw new DirectoryNotFoundException("repository root not found");
}
