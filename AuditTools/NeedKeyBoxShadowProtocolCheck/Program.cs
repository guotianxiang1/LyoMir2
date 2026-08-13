using System.Text.RegularExpressions;

var root = FindRepositoryRoot();
var bridge = Read(root, "GameSvr", "ScriptSystem", "PasEngine", "PasApiBridge.cs");
// The IDA transcript is the only thing pinning these constants to the exe, so a
// missing one is red, not a skip: skipping would silently retire the native
// evidence half of this check and leave the shape assertions grading themselves.
var staging = FindAncestorStaging()
    ?? throw new DirectoryNotFoundException(
        "staging/ was not found by walking ancestors of the repository root; "
        + "NeedKeyBox native evidence cannot be verified");
var idaPath = Path.Combine(staging, "ida_needkeybox_exact_20260720.txt");
if (!File.Exists(idaPath))
    throw new FileNotFoundException(
        "NeedKeyBox native evidence transcript is missing", idaPath);
var ida = File.ReadAllText(idaPath);
var needKeyBox = Read(root, "GameSvr", "Players", "TPlayObject.NativeNeedKeyBox.cs");

TestShadowContracts(needKeyBox);
TestNativeEvidence(ida);
TestBridgeFailClosed(bridge);
// Operator content, not repository content: its absence says nothing about the
// port, so it degrades to a reported gap rather than to red. It is reported in
// the summary line so a run that skipped it cannot read as a full pass.
var productionScriptPath = Path.Combine(
    "D:\\lyom2Release\\mud2.0\\Mir200\\Envir",
    "PsNpcscripts", "神秘宝藏-hero1.pas");
var callSurface = "call-surface=not-checked(production script absent)";
if (File.Exists(productionScriptPath))
{
    TestProductionCallSurface(File.ReadAllText(productionScriptPath));
    callSurface = "call-surface=verified";
}

Console.WriteLine(
    "PASS NeedKeyBox shadow protocol ui=950/216 " +
    "yb=125/10000/0/0/1 procedure=native-open function=fail-closed " +
    callSurface);
return;

static void TestShadowContracts(string needKeyBox)
{
    Require(needKeyBox, "NativeNeedKeyBoxOpenMessage = 950",
        "valued-box UI message id is not 950");
    Require(needKeyBox, "NativeNeedKeyBoxWireBodySize = 216",
        "valued-box UI body size is not 216");
    Require(needKeyBox, "NativeNeedKeyBoxYuanbaoIdent = 125",
        "NeedKeyBox2 YBDB ident is not 125");
    Require(needKeyBox, "NativeNeedKeyBoxYuanbaoSelector = 10000",
        "NeedKeyBox2 YBDB selector is not 10000");
    Require(needKeyBox, "NativeNeedKeyBoxYuanbaoAmount = 1",
        "NeedKeyBox2 YBDB amount is not 1");
}

static void TestNativeEvidence(string ida)
{
    Require(ida,
        "BASELINE_SHA256=CC505716AEB2FDB09C96B805D06C1DDDCD70DB0F331EF42AE1338C71766B452F",
        "NeedKeyBox native baseline hash changed");
    Require(ida, "STATIC_ONLY=1", "NeedKeyBox evidence was not marked static-only");

    var uiConstructor = Slice(ida,
        "=== TARGET 00601368 valued-box reward/UI constructor ===",
        "=== TARGET 006015EC valued-box random selector ===");
    Require(uiConstructor, "sub_403B2C(buf_, 216, 0)",
        "valued-box UI body zero-fill size is not 216");
    Require(uiConstructor, "LOWORD(v15) = 950",
        "valued-box UI message id 950 evidence missing");
    Require(uiConstructor, "216,",
        "valued-box UI send size 216 evidence missing");
    Require(uiConstructor, "mov     dx, 3B6h",
        "valued-box UI disassembly id 0x3B6 evidence missing");
    Require(uiConstructor, "push    0D8h",
        "valued-box UI disassembly body length 0xD8 evidence missing");

    var ybStateMachine = Slice(ida,
        "=== TARGET 006D50E4 OpenNeedKeyBox2 YB request/state machine ===",
        "=== TARGET 0073CFC8 bag item lookup by StdIndex ===");
    Require(ybStateMachine, "push    1",
        "NeedKeyBox2 YB request amount 1 evidence missing");
    Require(ybStateMachine, "push    0",
        "NeedKeyBox2 YB request zero parameter evidence missing");
    Require(ybStateMachine, "mov     ecx, 2710h",
        "NeedKeyBox2 YB selector 10000 evidence missing");
    Require(ybStateMachine, "mov     dx, 7Dh",
        "NeedKeyBox2 YB ident 125 evidence missing");
    Require(ybStateMachine, "call    sub_6D3694",
        "NeedKeyBox2 YB request submission call evidence missing");
}

static void TestBridgeFailClosed(string bridge)
{
    var npcMethods = Slice(bridge, "public bool CallNpcMethod",
        "public bool CallNpcFunc");
    var npcFunctions = Slice(bridge, "public bool CallNpcFunc",
        "public bool CallStandaloneFunction");

    var procedureCase = Slice(npcMethods,
        "case \"openneedkeybox\":",
        "case \"openluckbox\":");
    Equal(1, Count(procedureCase, "case \"openneedkeybox\":"),
        "OpenNeedKeyBox procedure dispatch count");
    Equal(1, Count(procedureCase, "case \"openneedkeybox2\":"),
        "OpenNeedKeyBox2 procedure dispatch count");
    Require(procedureCase, "TryOpenNativeNeedKeyBox(true, out _)",
        "OpenNeedKeyBox procedure no longer calls the native opener");
    Require(procedureCase, "TrySubmitNativeNeedKeyBoxYuanbao(",
        "OpenNeedKeyBox2 procedure no longer submits the native YB request");
    Require(procedureCase, "CurrentNpc",
        "OpenNeedKeyBox2 procedure dropped the NPC argument");
    Reject(procedureCase, "RejectUnsupportedNativeApi",
        "NeedKeyBox procedure dispatch remained fail-closed");
    Reject(procedureCase, "BuildValuedBoxUiBody",
        "NeedKeyBox procedure dispatch started using the shadow encoder");

    var functionCases = Regex.Matches(npcFunctions,
        "case \"openneedkeybox\":\\s*case \"openneedkeybox2\":\\s*return RejectUnsupportedNativeApi\\(out result\\);",
        RegexOptions.CultureInvariant);
    Assert(functionCases.Count >= 1,
        "NeedKeyBox function dispatch is no longer fail-closed");
}

static void TestProductionCallSurface(string script)
{
    Equal(1, Count(script, "OpenNeedKeyBox(This_Player)"),
        "production OpenNeedKeyBox call count");
    Equal(1, Count(script, "OpenNeedKeyBox2(This_Player)"),
        "production OpenNeedKeyBox2 call count");
    Reject(script, "OpenNeedKeyBox(",
        "production script gained an unexpected OpenNeedKeyBox call",
        allowedOccurrences: 1);
    Reject(script, "OpenNeedKeyBox2(",
        "production script gained an unexpected OpenNeedKeyBox2 call",
        allowedOccurrences: 1);
}

static string Read(string root, params string[] parts) =>
    File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));

static string Slice(string source, string startMarker, string endMarker)
{
    var start = source.IndexOf(startMarker, StringComparison.Ordinal);
    Assert(start >= 0, $"missing marker: {startMarker}");
    var end = source.IndexOf(endMarker, start + startMarker.Length,
        StringComparison.Ordinal);
    Assert(end > start, $"missing marker: {endMarker}");
    return source[start..end];
}

static void Require(string source, string value, string message)
{
    Assert(source.Contains(value, StringComparison.Ordinal), message);
}

static void Reject(string source, string value, string message,
    int allowedOccurrences = 0)
{
    var actual = Count(source, value);
    Assert(actual == allowedOccurrences,
        $"{message}: expected {allowedOccurrences}, actual {actual}");
}

static int Count(string source, string value) =>
    Regex.Matches(source, Regex.Escape(value),
        RegexOptions.CultureInvariant).Count;

static void Equal(int expected, int actual, string message)
{
    if (expected != actual)
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static string FindAncestorStaging()
{
    var dir = new DirectoryInfo(FindRepositoryRoot());
    while (dir != null)
    {
        var staging = Path.Combine(dir.FullName, "staging");
        if (Directory.Exists(staging))
            return staging;
        dir = dir.Parent;
    }
    return null;
}

static string FindRepositoryRoot()
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
