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
TestGameplayFacadeIsTheLcg();
TestDormantRuntimeGate();

Console.WriteLine(
    "PASS DelphiRandom LCG=08088405 range0=advances " +
    "negative=uint-bits float=seed/2^32 concurrent=linearized " +
    "image-seed=0 randomize=qpc-low32/fallback-gettickcount " +
    "facade=sub_403B4C(v)/(0)/min+(max-min)/min+(max-min+1) " +
    "direct-delphirandom-in-gamesvr=0(+appservice-seed-only) facade-access>=453 " +
    "pas-shared=0(via-contract) yb-shared=0(via-contextid) " +
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

// POIS-26 put the gameplay facade ON the LCG. The load-bearing fact is not which
// field RandomNumber declares, it is that every draw M2Share.RandomNumber hands out
// is the one sub_403B4C @0x00403B4C would have produced from the same seed:
//   nextSeed = seed * 0x08088405 + 1;  result = high32((uint32)bound * nextSeed)
// Every drop rate, poison chance and crit roll in the data files is tuned on that
// generator's low-order bias, so an approximately-uniform substitute shifts all of
// them. Pinning the sequence catches a silent swap back to System.Random, which no
// amount of source-shape matching can do once the field is renamed.
static void TestGameplayFacadeIsTheLcg()
{
    var facade = RandomNumber.GetInstance();
    var seed = 0x12345678u;
    DelphiRandom.Seed = seed;

    foreach (var bound in new[] { 91_200, 800, 1, 0, -1, int.MinValue })
    {
        var expected = NextReference(ref seed, bound);
        Equal(expected, facade.Random(bound), $"facade Random({bound})");
        Equal(seed, DelphiRandom.Seed, $"facade Random({bound}) shared seed");
    }

    // Parameterless Random() is the original Random(0): it advances the seed and
    // returns 0. Random.Next(0) returned 0 WITHOUT advancing, so the deliberate
    // advance sites were silently no-ops.
    var advance = NextReference(ref seed, 0);
    Equal(advance, facade.Random(), "facade Random() advance result");
    Equal(seed, DelphiRandom.Seed, "facade Random() did not advance the seed");

    var halfOpen = NextReference(ref seed, 700 - 100);
    Equal(100 + halfOpen, facade.Random(100, 700),
        "facade Random(min,max) half-open contract");
    Equal(seed, DelphiRandom.Seed, "facade Random(min,max) shared seed");

    var inclusive = NextReference(ref seed, 700 - 100 + 1);
    Equal(100 + inclusive, facade.GetRandomNumber(100, 700),
        "facade GetRandomNumber(min,max) inclusive contract");
    Equal(seed, DelphiRandom.Seed, "facade GetRandomNumber shared seed");

    // Negative bounds participate as the UInt32 bit pattern; System.Random threw.
    DelphiRandom.Seed = 0xCAFEBABEu;
    seed = 0xCAFEBABEu;
    var negative = NextReference(ref seed, -1);
    Equal(negative, facade.Random(-1), "facade negative bound uses UInt32 bits");
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

    // Since POIS-26 the LCG is the live generator, so this is no longer a "keep it
    // dormant" check: it is single-authority (rule 4.18). Gameplay draws go through
    // M2Share.RandomNumber, which is the one place that owns the mapping onto
    // sub_403B4C; a GameSvr call site reaching into DelphiRandom directly would be a
    // second authority over the shared seed. The four files below are exempt for
    // reasons unrelated to gameplay draws: three are seed-injected models that never
    // touch the shared seed, and NativeQuestDiamondProtocol only has a local method
    // whose NAME contains the string.
    var dormantOrLocal = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "NativePasRandomContract.cs",        // PAS random/randomrange, now live on the shared seed
        "NativeUpdateClothesTransaction.cs", // dormant 4637 Randomize+Random(800) model
        "NativeYuanbaoContextId.cs",         // dormant YBDB-gated 30-step context-id model
        "NativeQuestDiamondProtocol.cs",     // local NextDelphiRandom method NAME only
        "AppService.cs",                     // startup seeding, asserted exactly below
    };
    var liveText = string.Join('\n',
        gameSources.Where(p => !dormantOrLocal.Contains(Path.GetFileName(p)))
            .Select(File.ReadAllText));

    Equal(0, Regex.Matches(liveText, @"\bDelphiRandom\b",
        RegexOptions.CultureInvariant).Count,
        "dormant owner leaked into LIVE GameSvr runtime (dormant/local models excluded)");

    // AppService is the one live file allowed to name DelphiRandom, because native
    // seeds the generator at startup and something has to model that. TMainThread.Create
    // does it before Execute enters the game loop:
    //   0x00792C5A  E8 4D 08 C7 FF     call 0x004034AC        ; Randomize
    //   0x004034AC  83 C4 F8 / 54      add esp,-8 / push esp   ; function entry
    //   0x004034B0  E8 BF E0 FF FF     call 0x00401574        ; time source
    //   0x004034BC  A3 08 20 7A 00     mov [0x007A2008], eax  ; the seed global
    // (0x004034BC is the store inside that function, not its entry - an earlier report
    // cited it as the call target, which is what made the two accounts look inconsistent.)
    //
    // A blanket exemption would let a real draw hide in this file, so the allowance is
    // exact: one reference, and it has to be the seeding call.
    var appService = gameSources.FirstOrDefault(
        p => string.Equals(Path.GetFileName(p), "AppService.cs", StringComparison.OrdinalIgnoreCase));
    Assert(appService != null, "AppService.cs not found for the startup-seeding check");
    var appServiceText = File.ReadAllText(appService!);
    Equal(1, Regex.Matches(appServiceText, @"\bDelphiRandom\b",
        RegexOptions.CultureInvariant).Count,
        "AppService may name DelphiRandom exactly once, for the startup seed");
    Equal(1, Regex.Matches(appServiceText, @"\bDelphiRandom\.Randomize\s*\(\s*\)",
        RegexOptions.CultureInvariant).Count,
        "AppService's single DelphiRandom use must be Randomize(), not a draw");

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

    // The PAS builtins used to draw from Random.Shared, a generator of their own. They
    // now go through NativePasRandomContract onto the shared RandSeed, which is what
    // native does - the script random is the engine random, not a second stream. This
    // assertion used to require the four Random.Shared sites and so pinned the less
    // faithful shape; it now requires them gone and the contract in their place.
    var pas = File.ReadAllText(Path.Combine(gameRoot, "ScriptSystem",
        "PasEngine", "PasInterpreter.cs"));
    Equal(0, Regex.Matches(pas, @"Random\.Shared\.",
        RegexOptions.CultureInvariant).Count,
        "PAS builtins must not own a second generator");
    Assert(Regex.IsMatch(pas, @"NativePasRandomContract\.\s*Random\s*\("),
        "PAS random builtin no longer routes through NativePasRandomContract");
    Assert(Regex.IsMatch(pas, @"NativePasRandomContract\.\s*RandomRange\s*\("),
        "PAS randomrange builtin no longer routes through NativePasRandomContract");

    // Same move as the PAS builtins: the context id used to be drawn from Random.Shared
    // inline and now comes from NativeYuanbaoContextId on the shared seed. Requiring the
    // old inline draw pinned the second generator, so the requirement is inverted and
    // paired with a positive check that the generator is still reached.
    var yuanbao = File.ReadAllText(Path.Combine(gameRoot, "Services",
        "NativeYuanbaoManager.cs"));
    Equal(0, Regex.Matches(yuanbao, @"Random\.Shared\.",
        RegexOptions.CultureInvariant).Count,
        "yuanbao manager must not own a second generator");
    Assert(Regex.IsMatch(yuanbao, @"NativeYuanbaoContextId\.\s*Generate\s*\(\s*\)"),
        "yuanbao context id no longer routes through NativeYuanbaoContextId");

    var newRandom = Regex.Matches(allGameText,
        @"new\s+System\.Random\s*\(",
        RegexOptions.CultureInvariant).Count;
    Equal(0, newRandom, "separate new System.Random gate");

    var randomNumber = File.ReadAllText(Path.Combine(root, "SystemModule",
        "RandomNumber.cs"));
    Require(randomNumber, "DelphiRandomNumberFacade",
        "gameplay facade stopped delegating to the native LCG");
    Reject(randomNumber, "private static Random random",
        "gameplay facade took its own System.Random back");
    Reject(randomNumber, "random.Next",
        "gameplay facade drew from System.Random instead of sub_403B4C");

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
    // An anti-regression gate must read CODE. RandomNumber.cs documents the cutover by
    // naming the field it no longer owns ("the `private static Random random` field this
    // class no longer owns"), and matching that sentence reported the removal as a
    // reappearance.
    Assert(!StripComments(source).Contains(marker, StringComparison.Ordinal), message);
}

static string StripComments(string source)
{
    return string.Join("\n", source.Split('\n').Select(line =>
    {
        var slashes = line.IndexOf("//", StringComparison.Ordinal);
        return slashes >= 0 ? line[..slashes] : line;
    }));
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
