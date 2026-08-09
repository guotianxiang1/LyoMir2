using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using SystemModule;

Equal(0u, DelphiRandom.Seed, "native image seed");
TestSeedAccess();
TestIntegerTruthTable();
TestIntegerSequence();
TestBoundaryBehavior();
TestFloatingTruthTable();
TestRandomizeTruthTable();
TestConcurrentLinearization();
TestDormantRuntimeGate();

Console.WriteLine(
    "PASS DelphiRandom dormant LCG=08088405 range0=advances " +
    "negative=uint-bits float=seed/2^32 concurrent=linearized " +
    "image-seed=0 randomize=qpc-low32/fallback-gettickcount " +
    "runtime=unwired live-delphirandom=0 old-access>=453 pas-shared=4 yb-shared=1 " +
    "new-random=0");

static void TestSeedAccess()
{
    DelphiRandom.Seed = 0xDEADBEEFu;
    Equal(0xDEADBEEFu, DelphiRandom.Seed, "explicit seed round trip");
}

static void TestIntegerTruthTable()
{
    var rows = new[]
    {
        (Seed: 0x00000000u, Range: 0, Next: 0x00000001u, Result: 0),
        (Seed: 0x00000000u, Range: 1, Next: 0x00000001u, Result: 0),
        (Seed: 0x12345678u, Range: 91_200, Next: 0xCB5B9059u,
            Result: 0x00011AFE),
        (Seed: 0x89ABCDEFu, Range: 800, Next: 0x2E0241ACu,
            Result: 0x0000008F),
        (Seed: 0xFFFFFFFFu, Range: int.MinValue, Next: 0xF7F77BFCu,
            Result: 0x7BFBBDFE),
        (Seed: 0xCAFEBABEu, Range: -1, Next: 0x15339DB7u,
            Result: 0x15339DB6)
    };

    foreach (var row in rows)
    {
        DelphiRandom.Seed = row.Seed;
        Equal(row.Result, DelphiRandom.Random(row.Range),
            $"integer truth seed={row.Seed:X8} range={row.Range}");
        Equal(row.Next, DelphiRandom.Seed,
            $"integer truth next seed={row.Seed:X8} range={row.Range}");
    }
}

static void TestIntegerSequence()
{
    const uint initial = 0x12345678u;
    DelphiRandom.Seed = initial;

    var expectedSeed = initial;
    var ranges = new[] { 91_200, 800, 1, 0, -1, int.MinValue, int.MaxValue };
    foreach (var range in ranges)
    {
        var expected = NextReference(ref expectedSeed, range);
        var actual = DelphiRandom.Random(range);
        Equal(expected, actual, $"Random({range})");
        Equal(expectedSeed, DelphiRandom.Seed,
            $"seed after Random({range})");
    }
}

static void TestBoundaryBehavior()
{
    DelphiRandom.Seed = 0;
    Equal(0, DelphiRandom.Random(0), "Random(0) result");
    Equal(1u, DelphiRandom.Seed, "Random(0) advances seed");

    var expectedSeed = 1u;
    var expectedOne = NextReference(ref expectedSeed, 1);
    Equal(expectedOne, DelphiRandom.Random(1), "Random(1) result");
    Equal(expectedSeed, DelphiRandom.Seed, "Random(1) advances seed");

    DelphiRandom.Seed = 0xCAFEBABEu;
    expectedSeed = 0xCAFEBABEu;
    var expectedNegative = NextReference(ref expectedSeed, -1);
    Equal(expectedNegative, DelphiRandom.Random(-1),
        "negative range uses UInt32 bits");
    Equal(expectedSeed, DelphiRandom.Seed, "negative range seed");

    DelphiRandom.Seed = uint.MaxValue;
    expectedSeed = uint.MaxValue;
    var expectedOverflow = NextReference(ref expectedSeed, int.MinValue);
    Equal(expectedOverflow, DelphiRandom.Random(int.MinValue),
        "seed/range overflow wraps");
    Equal(expectedSeed, DelphiRandom.Seed, "overflow seed");
}

static void TestFloatingTruthTable()
{
    var rows = new[]
    {
        (Seed: 0x00000000u, Next: 0x00000001u,
            Bits: 0x3DF0000000000000L),
        (Seed: 0x12345678u, Next: 0xCB5B9059u,
            Bits: 0x3FE96B720B200000L),
        (Seed: 0x89ABCDEFu, Next: 0x2E0241ACu,
            Bits: 0x3FC70120D6000000L),
        (Seed: 0xFFFFFFFFu, Next: 0xF7F77BFCu,
            Bits: 0x3FEEFEEF7F800000L),
        (Seed: 0xCAFEBABEu, Next: 0x15339DB7u,
            Bits: 0x3FB5339DB7000000L)
    };

    foreach (var row in rows)
    {
        DelphiRandom.Seed = row.Seed;
        var actual = DelphiRandom.NextDouble();
        Equal(row.Bits, BitConverter.DoubleToInt64Bits(actual),
            $"NextDouble bits seed={row.Seed:X8}");
        Equal(row.Next, DelphiRandom.Seed,
            $"NextDouble next seed={row.Seed:X8}");
    }
}

static void TestRandomizeTruthTable()
{
    var rows = new[]
    {
        (Available: true, Counter: 0L, Tick: 0x11223344u,
            Expected: 0x00000000u),
        (Available: true, Counter: 0x0123456789ABCDEFL,
            Tick: 0x11223344u, Expected: 0x89ABCDEFu),
        (Available: true, Counter: -1L, Tick: 0x11223344u,
            Expected: 0xFFFFFFFFu),
        (Available: false, Counter: 0x0123456789ABCDEFL,
            Tick: 0x11223344u, Expected: 0x11223344u),
        (Available: false, Counter: -1L, Tick: 0xFFFFFFFFu,
            Expected: 0xFFFFFFFFu)
    };

    foreach (var row in rows)
        Equal(row.Expected, SelectRandomizeSeed(row.Available, row.Counter,
                row.Tick),
            $"Randomize truth available={row.Available} " +
            $"counter={row.Counter:X16} tick={row.Tick:X8}");

    var qpc = NativeMethod("QueryPerformanceCounter");
    var tick = NativeMethod("GetTickCount");
    Equal("kernel32.dll", qpc.GetCustomAttribute<DllImportAttribute>()!.Value,
        "QueryPerformanceCounter import library");
    Equal("kernel32.dll", tick.GetCustomAttribute<DllImportAttribute>()!.Value,
        "GetTickCount import library");
}

static void TestConcurrentLinearization()
{
    const uint initial = 0x31415926u;
    const int calls = 4096;
    DelphiRandom.Seed = initial;

    var actual = new ConcurrentBag<int>();
    Parallel.For(0, calls, _ => actual.Add(DelphiRandom.Random(-1)));

    var expectedSeed = initial;
    var expected = new HashSet<int>();
    for (var index = 0; index < calls; index++)
        expected.Add(NextReference(ref expectedSeed, -1));

    Equal(calls, actual.Count, "concurrent result count");
    Equal(calls, expected.Count, "reference sequence uniqueness");
    Equal(expectedSeed, DelphiRandom.Seed, "concurrent final seed");
    Assert(expected.SetEquals(actual),
        "concurrent calls did not linearize to one shared sequence");
}

static void TestDormantRuntimeGate()
{
    var root = FindRepositoryRoot();
    var gameRoot = Path.Combine(root, "GameSvr");
    var gameSources = Directory.GetFiles(gameRoot, "*.cs",
        SearchOption.AllDirectories)
        .Where(path => !HasGeneratedSegment(path))
        .ToArray();
    var allGameText = string.Join('\n', gameSources.Select(File.ReadAllText));

    // The dormant RandSeed models legitimately reference DelphiRandom (seed-injected, NOT wired
    // to the live RandomNumber facade); NativeQuestDiamondProtocol only has a local method named
    // NextDelphiRandom. The true invariant is that the LIVE gameplay path must not delegate to
    // DelphiRandom — checked here (excluding these four) + at the RandomNumber.cs Reject below.
    var dormantOrLocal = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "NativePasRandomContract.cs",        // dormant PAS random/randomrange contract
        "NativeUpdateClothesTransaction.cs", // dormant 4637 Randomize+Random(800) model
        "NativeYuanbaoContextId.cs",         // dormant YBDB-gated 30-step context-id model
        "NativeQuestDiamondProtocol.cs",     // local NextDelphiRandom method NAME only
    };
    var liveText = string.Join('\n',
        gameSources.Where(p => !dormantOrLocal.Contains(Path.GetFileName(p)))
            .Select(File.ReadAllText));

    Equal(0, Regex.Matches(liveText, @"\bDelphiRandom\b",
        RegexOptions.CultureInvariant).Count,
        "dormant owner leaked into LIVE GameSvr runtime (dormant/local models excluded)");

    var oldInvocations = Regex.Matches(allGameText,
        @"(?<![A-Za-z0-9_])(?:M2Share\.)?RandomNumber\.Random",
        RegexOptions.CultureInvariant).Count;
    // Floor, not an exact pin: the facade must be used at least this many times. Catches a
    // DROP (a regression that removes gameplay draws) while tolerating additions from unrelated
    // feature work (the exact-count pin silently drifted 430->453 and false-red'd otherwise).
    // The load-bearing invariants are the live-path DelphiRandom exclusion (above) + the
    // RandomNumber.cs DelphiRandom Reject (below).
    Assert(oldInvocations >= 453,
        $"legacy RandomNumber access floor: expected >= 453, actual {oldInvocations} " +
        "(a DROP below the floor means gameplay draws were removed = regression)");

    var pas = File.ReadAllText(Path.Combine(gameRoot, "ScriptSystem",
        "PasEngine", "PasInterpreter.cs"));
    Equal(4, Regex.Matches(pas, @"Random\.Shared\.",
        RegexOptions.CultureInvariant).Count,
        "PAS separate Random.Shared gate");

    var yuanbao = File.ReadAllText(Path.Combine(gameRoot, "Services",
        "NativeYuanbaoManager.cs"));
    Equal(1, Regex.Matches(yuanbao, @"Random\.Shared\.",
        RegexOptions.CultureInvariant).Count,
        "yuanbao context-id Random.Shared gate");

    var newRandom = Regex.Matches(allGameText,
        @"new\s+System\.Random\s*\(",
        RegexOptions.CultureInvariant).Count;
    Equal(0, newRandom, "separate new System.Random gate");

    var randomNumber = File.ReadAllText(Path.Combine(root, "SystemModule",
        "RandomNumber.cs"));
    Require(randomNumber, "private static Random random",
        "legacy System.Random owner changed before execution-domain closure");
    Reject(randomNumber, "DelphiRandom",
        "legacy RandomNumber was wired to dormant Delphi owner");

    var delphiRandom = File.ReadAllText(Path.Combine(root, "SystemModule",
        "DelphiRandom.cs"));
    Require(delphiRandom, "private static uint _seed;",
        "dormant owner no longer starts from the native image seed zero");
    Require(delphiRandom, "public static void Randomize()",
        "closed native Randomize API is missing");
    Require(delphiRandom, "QueryPerformanceCounter(out long counter)",
        "Randomize no longer uses QueryPerformanceCounter BOOL/Int64 contract");
    Require(delphiRandom, "SelectRandomizeSeed(true, counter, 0u)",
        "Randomize no longer selects the QPC low DWORD on success");
    Require(delphiRandom,
        "SelectRandomizeSeed(false, 0L, GetTickCount())",
        "Randomize no longer falls back to GetTickCount on QPC failure");
    Reject(delphiRandom, "Stopwatch",
        "Randomize substituted a managed timestamp for the native APIs");

    var userEngine = File.ReadAllText(Path.Combine(gameRoot, "UsrSystem",
        "UsrEngn.cs"));
    Require(userEngine,
        "new Thread(PrcocessData)",
        "UserEngine main thread topology changed without re-audit");
    Require(userEngine,
        "new Thread(ProcessAiPlayObjectData)",
        "AI thread topology changed without re-audit");
    var aiLoop = Slice(userEngine, "private void ProcessAiPlayObjectData()",
        "private void ProcessPlayObjectData()");
    var mainLoop = Slice(userEngine, "private void PrcocessData()",
        "public string GetHomeInfo(");
    Require(aiLoop,
        "EnterCriticalSection(M2Share.ProcessHumanCriticalSection)",
        "AI loop no longer enters the shared gameplay lock");
    Require(mainLoop,
        "EnterCriticalSection(M2Share.ProcessHumanCriticalSection)",
        "UserEngine loop no longer enters the shared gameplay lock");
}

static int NextReference(ref uint seed, int range)
{
    seed = unchecked(seed * 0x08088405u + 1u);
    return unchecked((int)(uint)(((ulong)(uint)range * seed) >> 32));
}

static uint SelectRandomizeSeed(bool counterAvailable, long counter,
    uint tickCount)
{
    var method = typeof(DelphiRandom).GetMethod("SelectRandomizeSeed",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(DelphiRandom).FullName,
            "SelectRandomizeSeed");
    return (uint)(method.Invoke(null,
        new object[] { counterAvailable, counter, tickCount })
        ?? throw new InvalidOperationException(
            "SelectRandomizeSeed returned null"));
}

static MethodInfo NativeMethod(string name)
{
    return typeof(DelphiRandom).GetMethod(name,
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(DelphiRandom).FullName, name);
}

static bool HasGeneratedSegment(string path)
{
    var relative = Path.GetRelativePath(FindRepositoryRoot(), path);
    return relative.Split(Path.DirectorySeparatorChar)
        .Any(segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
}

static string FindRepositoryRoot()
{
    foreach (var start in new[]
             {
                 Environment.CurrentDirectory,
                 AppContext.BaseDirectory
             })
    {
        var current = new DirectoryInfo(start);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "GameSvr", "M2Share.cs"))
                && File.Exists(Path.Combine(current.FullName, "SystemModule",
                    "RandomNumber.cs")))
                return current.FullName;
            current = current.Parent;
        }
    }
    throw new DirectoryNotFoundException("repository root not found");
}

static void Require(string source, string marker, string message)
{
    Assert(source.Contains(marker, StringComparison.Ordinal), message);
}

static void Reject(string source, string marker, string message)
{
    Assert(!source.Contains(marker, StringComparison.Ordinal), message);
}

static string Slice(string source, string startMarker, string endMarker)
{
    var start = source.IndexOf(startMarker, StringComparison.Ordinal);
    if (start < 0) throw new InvalidOperationException(
        $"slice start marker not found: {startMarker}");
    var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
    if (end < 0) throw new InvalidOperationException(
        $"slice end marker not found: {endMarker}");
    return source.Substring(start, end - start);
}

static void Equal<T>(T expected, T actual, string message)
    where T : IEquatable<T>
{
    if (!expected.Equals(actual))
        throw new InvalidOperationException(
            $"{message}: expected={expected} actual={actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
