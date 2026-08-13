using GameSvr;
using SystemModule;

var defaults = new TMapFlag();
Equal((byte)0, defaults.BreakLevel, "default BREAKLEVEL");
Equal((ushort)0, defaults.CrazyBreakLevel, "default CRAZYBREAKLEVEL");
Equal(typeof(byte), typeof(TMapFlag).GetField(nameof(TMapFlag.BreakLevel))?.FieldType,
    "BREAKLEVEL storage type");
Equal(typeof(ushort), typeof(TMapFlag).GetField(nameof(TMapFlag.CrazyBreakLevel))?.FieldType,
    "CRAZYBREAKLEVEL storage type");

var mixedCase = ParseFlags("bReAkLeVeL(257),\tCrAzYbReAkLeVeL(65537)");
Equal((byte)1, mixedCase.BreakLevel, "case-insensitive BREAKLEVEL");
Equal((ushort)1, mixedCase.CrazyBreakLevel,
    "case-insensitive CRAZYBREAKLEVEL");

var delimited = ParseFlags("UNKNOWN BREAKLEVEL(23),CRAZYBREAKLEVEL(4097)\tSAFE");
Equal((byte)23, delimited.BreakLevel, "space/comma tokenization");
Equal((ushort)4097, delimited.CrazyBreakLevel, "tab tokenization");

CheckByteBoundary(255, 255);
CheckByteBoundary(256, 0);
CheckByteBoundary(511, 255);
CheckByteBoundary(-1, 255);
CheckWordBoundary(65535, 65535);
CheckWordBoundary(65536, 0);
CheckWordBoundary(65537, 1);
CheckWordBoundary(-1, 65535);

var invalid = ParseFlags("BREAKLEVEL(no-number) CRAZYBREAKLEVEL");
Equal((byte)0, invalid.BreakLevel, "invalid BREAKLEVEL converts to zero");
Equal((ushort)0, invalid.CrazyBreakLevel,
    "missing CRAZYBREAKLEVEL argument converts to zero");

var repeated = ParseFlags(
    "BREAKLEVEL(9) CRAZYBREAKLEVEL(10) BREAKLEVEL(11) CRAZYBREAKLEVEL(12)");
Equal((byte)11, repeated.BreakLevel, "last BREAKLEVEL token wins");
Equal((ushort)12, repeated.CrazyBreakLevel,
    "last CRAZYBREAKLEVEL token wins");

var environment = new Envirnoment { Flag = repeated };
Equal((byte)11, environment.BreakLevel, "environment BREAKLEVEL projection");
Equal((ushort)12, environment.CrazyBreakLevel,
    "environment CRAZYBREAKLEVEL projection");
environment.Flag = ParseFlags(string.Empty);
Equal((byte)0, environment.BreakLevel, "reload resets BREAKLEVEL");
Equal((ushort)0, environment.CrazyBreakLevel,
    "reload resets CRAZYBREAKLEVEL");

var untouched = new TMapFlag { BreakLevel = 7, CrazyBreakLevel = 8 };
Assert(!NativeMapBreakLevelFlagParser.TryApply(untouched, "UNKNOWN(99)"),
    "unknown token was accepted");
Equal((byte)7, untouched.BreakLevel, "unknown token changed BREAKLEVEL");
Equal((ushort)8, untouched.CrazyBreakLevel,
    "unknown token changed CRAZYBREAKLEVEL");

var mapsSource = File.ReadAllText(Path.Combine(FindRepositoryRoot(),
    "GameSvr", "Maps", "Maps.cs"));
Assert(mapsSource.Contains(
        "NativeMapBreakLevelFlagParser.TryApply(MapFlag, s34)",
        StringComparison.Ordinal),
    "MapInfo token loop is not wired to the native break-level parser");

Console.WriteLine("MapBreakLevelCompatCheck PASS");

static TMapFlag ParseFlags(string flags)
{
    var mapFlag = new TMapFlag();
    var remaining = flags ?? string.Empty;
    while (remaining.Length > 0)
    {
        var token = string.Empty;
        remaining = HUtil32.GetValidStr3(remaining, ref token,
            new[] { " ", ",", "\t" });
        if (token.Length == 0)
        {
            break;
        }

        NativeMapBreakLevelFlagParser.TryApply(mapFlag, token);
    }

    return mapFlag;
}

static void CheckByteBoundary(int input, int expected)
{
    var flag = ParseFlags($"BREAKLEVEL({input})");
    Equal(unchecked((byte)expected), flag.BreakLevel,
        $"BREAKLEVEL boundary {input}");
}

static void CheckWordBoundary(int input, int expected)
{
    var flag = ParseFlags($"CRAZYBREAKLEVEL({input})");
    Equal(unchecked((ushort)expected), flag.CrazyBreakLevel,
        $"CRAZYBREAKLEVEL boundary {input}");
}

static string FindRepositoryRoot()
{
    return AuditRepoRoot.Resolve();
}

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            $"{message}: expected={expected} actual={actual}");
    }
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
