using GameSvr.Services;

var checks = 0;

CheckNameCanonicalization();
CheckInitialProbeSuccess();
CheckFallbackProbeSuccess();
CheckStepAndMarginThresholds();
CheckWrapAndRandomOrder();
CheckThresholdEqualityAndOddBounds();
CheckExhaustionAndFinalRandomConsumption();
CheckThirtyFirstFallbackSuccessStopsBeforeAdvance();
CheckOneByOneMapStillConsumesRandomZero();
CheckNullDelegates();

Console.WriteLine($"PASS NativeFieldHeroFactoryPreflightCheck {checks} checks");

void CheckNameCanonicalization()
{
    EqualBytes(new byte[] { (byte)'a', (byte)'z', (byte)'0' },
        NativeFieldHeroFactoryPreflight.CanonicalizeLookupName(
            new byte[] { (byte)'A', (byte)'Z', (byte)'0' }),
        "ASCII uppercase folds to lowercase");
    EqualBytes(new byte[] { 0x40, (byte)'a', (byte)'z', 0x5B },
        NativeFieldHeroFactoryPreflight.CanonicalizeLookupName(
            new byte[] { 0x40, (byte)'A', (byte)'Z', 0x5B }),
        "fold boundaries exclude bytes adjacent to A..Z");
    EqualBytes(new byte[] { 0x81, (byte)'a', 0x7F, 0x80 },
        NativeFieldHeroFactoryPreflight.CanonicalizeLookupName(
            new byte[] { 0x81, (byte)'A', 0x7F, 0x80 }),
        "GBK trail byte is folded without decoding");
    EqualBytes(Array.Empty<byte>(),
        NativeFieldHeroFactoryPreflight.CanonicalizeLookupName(
            ReadOnlySpan<byte>.Empty),
        "empty name remains empty");
}

void CheckInitialProbeSuccess()
{
    var probes = new List<(int X, int Y, bool Ignore)>();
    var randomCalls = 0;
    var success = NativeFieldHeroFactoryPreflight.TryResolvePlacement(
        100, 100, 12, 34,
        (x, y, ignore) =>
        {
            probes.Add((x, y, ignore));
            return true;
        },
        _ =>
        {
            randomCalls++;
            return 0;
        },
        out var x, out var y);

    Check(success, "initial probe succeeds");
    Equal(12, x, "initial success X");
    Equal(34, y, "initial success Y");
    Equal(1, probes.Count, "initial success probe count");
    Check(probes[0] == (12, 34, true),
        "initial success uses ignore-occupants probe");
    Equal(0, randomCalls, "initial success consumes no random draw");
}

void CheckFallbackProbeSuccess()
{
    var probes = new List<(int X, int Y, bool Ignore)>();
    var success = NativeFieldHeroFactoryPreflight.TryResolvePlacement(
        100, 100, 10, 20,
        (x, y, ignore) =>
        {
            probes.Add((x, y, ignore));
            return probes.Count == 3;
        },
        _ => throw new InvalidOperationException("unexpected random draw"),
        out var x, out var y);

    Check(success, "second fallback probe succeeds");
    Equal(13, x, "width >= 50 uses step 3");
    Equal(20, y, "non-wrap fallback preserves Y");
    Equal(3, probes.Count, "initial plus two fallback probes");
    Check(probes[0] == (10, 20, true), "initial fallback flag");
    Check(probes[1] == (10, 20, false), "first fallback flag");
    Check(probes[2] == (13, 20, false), "second fallback point");
}

void CheckStepAndMarginThresholds()
{
    CheckOneStep(49, 29, 10, 10, 12, "width 49 step 2");
    CheckOneStep(50, 29, 10, 10, 13, "width 50 step 3");
    CheckWrapMargin(100, 29, 99, 5, 2, "height 29 margin 2");
    CheckWrapMargin(100, 30, 99, 5, 20, "height 30 margin 20");
    CheckWrapMargin(200, 249, 199, 5, 20, "height 249 margin 20");
    CheckWrapMargin(200, 250, 199, 5, 50, "height 250 margin 50");
}

void CheckOneStep(int width, int height, int startX, int startY,
    int expectedX, string description)
{
    var calls = 0;
    var success = NativeFieldHeroFactoryPreflight.TryResolvePlacement(
        width, height, startX, startY,
        (_, _, _) => ++calls == 3,
        _ => throw new InvalidOperationException("unexpected random draw"),
        out var x, out var y);
    Check(success, description + " succeeds");
    Equal(expectedX, x, description + " X");
    Equal(startY, y, description + " Y");
}

void CheckWrapMargin(int width, int height, int startX, int startY,
    int expectedMargin, string description)
{
    var calls = 0;
    var bounds = new List<int>();
    var success = NativeFieldHeroFactoryPreflight.TryResolvePlacement(
        width, height, startX, startY,
        (_, _, _) => ++calls == 3,
        bound =>
        {
            bounds.Add(bound);
            return 0;
        },
        out var x, out var y);
    Check(success, description + " succeeds");
    Equal(expectedMargin, x, description + " X");
    Equal(startY + (width < 50 ? 2 : 3), y,
        description + " advances Y");
    Equal(1, bounds.Count, description + " random count");
    Equal(width / 2, bounds[0], description + " X random bound");
}

void CheckWrapAndRandomOrder()
{
    var calls = 0;
    var bounds = new List<int>();
    var values = new Queue<int>(new[] { 7, 11 });
    var success = NativeFieldHeroFactoryPreflight.TryResolvePlacement(
        100, 100, 99, 99,
        (_, _, _) => ++calls == 3,
        bound =>
        {
            bounds.Add(bound);
            return values.Dequeue();
        },
        out var x, out var y);

    Check(success, "wrapped fallback succeeds");
    Equal(27, x, "wrap X is margin plus draw");
    Equal(31, y, "wrap Y is margin plus draw");
    Check(bounds.SequenceEqual(new[] { 50, 50 }),
        "wrap draws X before Y with half-dimension bounds");
}

void CheckThresholdEqualityAndOddBounds()
{
    var calls = 0;
    var belowBounds = new List<int>();
    var below = NativeFieldHeroFactoryPreflight.TryResolvePlacement(
        51, 31, 29, 9,
        (_, _, _) => ++calls == 3,
        bound =>
        {
            belowBounds.Add(bound);
            return 0;
        },
        out var belowX, out var belowY);
    Check(below, "one below X threshold succeeds");
    Equal(32, belowX, "one below X threshold advances by step 3");
    Equal(9, belowY, "one below X threshold preserves Y");
    Equal(0, belowBounds.Count, "one below threshold consumes no RNG");

    calls = 0;
    var equalXBounds = new List<int>();
    var equalX = NativeFieldHeroFactoryPreflight.TryResolvePlacement(
        51, 31, 30, 9,
        (_, _, _) => ++calls == 3,
        bound =>
        {
            equalXBounds.Add(bound);
            return 1;
        },
        out var equalXX, out var equalXY);
    Check(equalX, "equal X threshold succeeds after wrap");
    Equal(21, equalXX, "equal X threshold replays X from margin");
    Equal(12, equalXY, "Y below threshold advances by step 3");
    Check(equalXBounds.SequenceEqual(new[] { 25 }),
        "odd width uses truncated width/2 bound");

    calls = 0;
    var equalYBounds = new List<int>();
    var equalY = NativeFieldHeroFactoryPreflight.TryResolvePlacement(
        51, 31, 30, 10,
        (_, _, _) => ++calls == 3,
        bound =>
        {
            equalYBounds.Add(bound);
            return 1;
        },
        out var equalYX, out var equalYY);
    Check(equalY, "equal Y threshold succeeds after replay");
    Equal(21, equalYX, "equal Y threshold wrapped X");
    Equal(21, equalYY, "equal Y threshold replays Y from margin");
    Check(equalYBounds.SequenceEqual(new[] { 25, 15 }),
        "odd dimensions draw truncated X then Y half-bounds");
}

void CheckExhaustionAndFinalRandomConsumption()
{
    var probes = 0;
    var bounds = new List<int>();
    var success = NativeFieldHeroFactoryPreflight.TryResolvePlacement(
        4, 4, 3, 3,
        (_, _, _) =>
        {
            probes++;
            return false;
        },
        bound =>
        {
            bounds.Add(bound);
            return 0;
        },
        out var x, out var y);

    Check(!success, "all probes fail");
    Equal(1 + NativeFieldHeroFactoryPreflight.FallbackProbeCount,
        probes, "one initial plus 31 fallback probes");
    Equal(NativeFieldHeroFactoryPreflight.FallbackProbeCount * 2,
        bounds.Count, "final failed probe still consumes X and Y draws");
    Check(bounds.All(bound => bound == 2),
        "tiny-map random bounds remain half dimensions");
    Equal(2, x, "exhausted X reflects final draw");
    Equal(2, y, "exhausted Y reflects final draw");
}

void CheckThirtyFirstFallbackSuccessStopsBeforeAdvance()
{
    var probes = 0;
    var randomCalls = 0;
    var success = NativeFieldHeroFactoryPreflight.TryResolvePlacement(
        1000, 100, 0, 0,
        (_, _, _) => ++probes == 32,
        _ =>
        {
            randomCalls++;
            return 0;
        },
        out var x, out var y);

    Check(success, "31st fallback probe succeeds");
    Equal(32, probes, "31st success probe count");
    Equal(90, x, "31st success returns pre-advance X");
    Equal(0, y, "31st success preserves Y");
    Equal(0, randomCalls, "31st success performs no post-success RNG");
}

void CheckOneByOneMapStillConsumesRandomZero()
{
    var bounds = new List<int>();
    var success = NativeFieldHeroFactoryPreflight.TryResolvePlacement(
        1, 1, 0, 0,
        (_, _, _) => false,
        bound =>
        {
            bounds.Add(bound);
            return 0;
        },
        out var x, out var y);

    Check(!success, "1x1 map exhausts placement");
    Equal(NativeFieldHeroFactoryPreflight.FallbackProbeCount * 2,
        bounds.Count, "1x1 map consumes both draws after every failure");
    Check(bounds.All(bound => bound == 0),
        "1x1 map calls Random(0) for both axes");
    Equal(2, x, "1x1 exhausted X keeps margin plus Random(0)");
    Equal(2, y, "1x1 exhausted Y keeps margin plus Random(0)");
}

void CheckNullDelegates()
{
    ExpectThrows<ArgumentNullException>(() =>
        NativeFieldHeroFactoryPreflight.TryResolvePlacement(
            100, 100, 1, 1, null, _ => 0, out _, out _),
        "null probe rejected");
    ExpectThrows<ArgumentNullException>(() =>
        NativeFieldHeroFactoryPreflight.TryResolvePlacement(
            100, 100, 1, 1, (_, _, _) => true, null, out _, out _),
        "null random rejected");
}

void EqualBytes(byte[] expected, byte[] actual, string description)
{
    checks++;
    if (!expected.AsSpan().SequenceEqual(actual))
        throw new InvalidOperationException(description);
}

void Equal<T>(T expected, T actual, string description)
{
    checks++;
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            $"{description}: expected {expected}, actual {actual}");
    }
}

void ExpectThrows<T>(Action action, string description)
    where T : Exception
{
    checks++;
    try
    {
        action();
    }
    catch (T)
    {
        return;
    }
    throw new InvalidOperationException(description);
}

void Check(bool condition, string description)
{
    checks++;
    if (!condition) throw new InvalidOperationException(description);
}
