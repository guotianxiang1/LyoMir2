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
// What matters here is the roll's shape, not its exact text: MaxPoint is the denominator
// handed to Random, SelPoint is the threshold, and the comparison is inclusive `<=`. Pinning
// the literal made the audit fail the moment the anti-addiction tier-2 factor (obj+0x1828,
// set to 1/2/3 at 0x6D2631 / 0x6D2628 / 0x6D2617) was folded into the denominator, and again
// when the 眼神 equip-drop-boost trampoline wrapped it. The denominator therefore has to be
// matched with balanced parentheses instead of a flat `[^)]*`. Native (sub_71FBxx loop):
//   0071FD34 8B 45 E4           mov eax,[ebp-0x1C]      ; MonItem
//   0071FD37 8B 40 14           mov eax,[eax+0x14]      ; MonItem.MaxPoint
//   0071FD3A F7 6D D4           imul dword [ebp-0x2C]   ; x fatigue factor
//   0071FD3D E8 0A 3E CE FF     call 0x403B4C           ; Random(eax)
//   0071FD42 8B 55 E4           mov edx,[ebp-0x1C]
//   0071FD45 3B 42 10           cmp eax,[edx+0x10]      ; MonItem.SelPoint
//   0071FD48 0F 8F 51 01 00 00  jg  0x71FE9F            ; keep only Random(..) <= SelPoint
AssertNativeDropRoll(userEngine);

Console.WriteLine(
    "ChgMonItemPercentStaticCheck PASS dispatch=fail-closed " +
    "substitute=none native-drop-shape=verified");
return;

// The drop gate must remain `M2Share.RandomNumber.Random(<expr mentioning MonItem.MaxPoint>)
// <= MonItem.SelPoint`. Anything else — a different RNG, a denominator that stopped being
// derived from MaxPoint, an exclusive `<`, or a different threshold — is a regression.
void AssertNativeDropRoll(string source)
{
    const string call = "M2Share.RandomNumber.Random(";
    for (var at = source.IndexOf(call, StringComparison.Ordinal); at >= 0;
         at = source.IndexOf(call, at + call.Length, StringComparison.Ordinal))
    {
        var open = at + call.Length - 1;
        var depth = 0;
        var close = -1;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '(') depth++;
            else if (source[i] == ')' && --depth == 0) { close = i; break; }
        }
        if (close < 0) continue;

        var denominator = source[(open + 1)..close];
        if (!denominator.Contains("MonItem.MaxPoint", StringComparison.Ordinal))
            continue;
        if (Regex.IsMatch(source[(close + 1)..], @"\A\s*<=\s*MonItem\.SelPoint\b"))
            return;
    }

    Assert(false,
        "native monster drop selection changed: the roll must stay "
        + "M2Share.RandomNumber.Random(<MaxPoint-derived denominator>) <= MonItem.SelPoint");
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
