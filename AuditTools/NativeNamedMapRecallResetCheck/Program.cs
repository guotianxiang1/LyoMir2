using System.Reflection;

try
{
    var root = AuditRepoRoot.Resolve();
    var sourcePath = Path.Combine(root, "GameSvr", "Players", "TPlayObject.cs");
    var source = File.ReadAllText(sourcePath);
    var methodStart = source.IndexOf("private void BaseObjectMove", StringComparison.Ordinal);
    Assert(methodStart >= 0, "BaseObjectMove declaration missing");

    var methodEnd = source.IndexOf("\n        internal void ChangeServerMakeSlave", methodStart,
        StringComparison.Ordinal);
    Assert(methodEnd > methodStart, "BaseObjectMove boundary missing");
    var method = source.Substring(methodStart, methodEnd - methodStart);

    var resolve = method.IndexOf(
        "var targetEnvironment = M2Share.MapManager.FindMap(sMap);",
        StringComparison.Ordinal);
    Assert(resolve >= 0, "named-map target resolution missing");
    var nullGuard = method.IndexOf(
        "targetEnvironment == null", resolve, StringComparison.Ordinal);
    Assert(nullGuard > resolve, "unresolved map must not mutate recall state");
    var nullReturn = method.IndexOf("return;", nullGuard,
        StringComparison.Ordinal);
    Assert(nullReturn > nullGuard, "unresolved map must return silently");
    var clear = method.IndexOf("m_boTimeRecall = false", nullGuard,
        StringComparison.Ordinal);
    Assert(clear > nullGuard, "cross-map recall clear missing");
    var dispatch = FirstDispatch(method);
    Assert(dispatch > clear,
        "recall clear must precede named-map movement dispatch");
    Assert(!method.Contains("var envir = m_PEnvir;", StringComparison.Ordinal),
        "post-move self comparison was reintroduced");
    Assert(!method.Contains("if (envir != m_PEnvir", StringComparison.Ordinal),
        "dead post-move environment guard was reintroduced");
    Assert(method.Contains("SpaceMove(targetEnvironment, sX, sY, 0);",
        StringComparison.Ordinal),
        "named-map move must pass both requested axes through the native path");
    Assert(!method.Contains("if (sX != 0 && sY != 0)",
        StringComparison.Ordinal),
        "single-axis coordinate sentinel was collapsed by an AND gate");

    var staging = Path.GetFullPath(Path.Combine(root, "..", "..", "staging"));
    var native = File.ReadAllText(Path.Combine(staging,
        "ida_checkauthen_deep_20260716.txt"));
    Assert(native.Contains("006CE1EC  C6 86 A8 0B 00 00 00",
        StringComparison.Ordinal), "native BA8 clear evidence missing");
    Assert(native.Contains("006CE204  FF 93 C0 01 00 00",
        StringComparison.Ordinal), "native VMT dispatch evidence missing");
    Assert(native.Contains("006CE1F3", StringComparison.Ordinal),
        "native named-map coordinate dispatch evidence missing");

    var baseline = File.ReadAllText(Path.Combine(staging,
        "clean_m2_baseline_audit.md"));
    Assert(baseline.Contains("SHA256", StringComparison.Ordinal)
        && baseline.Contains("M2Server.exe", StringComparison.Ordinal),
        "M2 baseline identity evidence missing");

    // The managed field is behavior-mapped, not a claimed raw offset layout.
    var field = typeof(GameSvr.TPlayObject).GetField("m_boTimeRecall",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    Assert(field != null && field.FieldType == typeof(bool),
        "managed timed-recall field missing");

    Console.WriteLine(
        "PASS NativeNamedMapRecallResetCheck native=0x6CE1EC/0x6CE204 " +
        "clear-before-dispatch unresolved-fail-closed field=m_boTimeRecall");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"NativeNamedMapRecallResetCheck FAIL: {exception.Message}");
    return 1;
}

static int FirstDispatch(string method)
{
    var space = method.IndexOf("SpaceMove(sMap", StringComparison.Ordinal);
    var resolvedSpace = method.IndexOf("SpaceMove(targetEnvironment",
        StringComparison.Ordinal);
    var random = method.IndexOf("MapRandomMove(sMap", StringComparison.Ordinal);
    if (resolvedSpace >= 0)
        space = space < 0 ? resolvedSpace : Math.Min(space, resolvedSpace);
    if (space < 0) return random;
    if (random < 0) return space;
    return Math.Min(space, random);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
