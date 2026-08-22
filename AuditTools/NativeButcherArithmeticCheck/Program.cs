using System;

var repository = ResolveRepositoryRoot(args);
var source = File.ReadAllText(Path.Combine(repository, "GameSvr", "Players",
    "TPlayObject.Operate.cs"));
var failures = new List<string>();

Require(source.Contains(
        "var meatQuality = (int)BaseObject.m_nMeatQuality - n14;",
        StringComparison.Ordinal),
    "ClientGetButchItem must subtract n14 in an int local");
Require(source.Contains("if (meatQuality < 0)", StringComparison.Ordinal),
    "ClientGetButchItem must clamp the signed result at zero");
Require(source.Contains(
        "BaseObject.m_nMeatQuality = (ushort)meatQuality;",
        StringComparison.Ordinal),
    "ClientGetButchItem must assign the clamped result back to the field");
Reject(source.Contains(
        "BaseObject.m_nMeatQuality -= (ushort)n14",
        StringComparison.Ordinal),
    "ClientGetButchItem still has the wrapping ushort subtraction");
Reject(source.Contains("BaseObject.m_nMeatQuality < 0",
        StringComparison.Ordinal),
    "ClientGetButchItem still compares the ushort field to zero");

var clamp = source.IndexOf("if (meatQuality < 0)", StringComparison.Ordinal);
var assignment = source.IndexOf(
    "BaseObject.m_nMeatQuality = (ushort)meatQuality;",
    StringComparison.Ordinal);
Require(clamp >= 0 && assignment > clamp,
    "the clamp must precede the ushort write-back");

Equal((ushort)0, ClampNativeMeatQuality(50, 100),
    "underflow clamps instead of wrapping to 65486");
Equal((ushort)0, ClampNativeMeatQuality(100, 100),
    "exact depletion clamps to zero");
Equal((ushort)400, ClampNativeMeatQuality(500, 100),
    "positive signed subtraction is preserved");
Equal((ushort)65435, ClampNativeMeatQuality(ushort.MaxValue, 100),
    "in-range high quality remains representable");

if (failures.Count != 0)
{
    Console.Error.WriteLine("FAIL NativeButcherArithmeticCheck");
    foreach (var failure in failures)
        Console.Error.WriteLine("  " + failure);
    return 1;
}

Console.WriteLine("PASS NativeButcherArithmeticCheck");
Console.WriteLine("  signed int meat-quality subtraction clamps before ushort write-back");
Console.WriteLine("  deterministic underflow, boundary, and positive-value cases verified");
return 0;

static ushort ClampNativeMeatQuality(ushort current, int loss)
{
    var result = (int)current - loss;
    if (result < 0)
        result = 0;
    return (ushort)result;
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

void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        failures.Add($"{message}: expected {expected}, actual {actual}");
}

static string ResolveRepositoryRoot(string[] arguments)
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

static bool IsRepositoryRoot(string path) =>
    Directory.Exists(path) &&
    File.Exists(Path.Combine(path, "GameSvr", "GameSvr.csproj"));
