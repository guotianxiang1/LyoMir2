using System.Text;
using System.Text.RegularExpressions;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var root = FindRepositoryRoot();
var productionScript = args.Length > 0
    ? Path.GetFullPath(args[0])
    : @"D:\lyom2Release\mud2.0\Mir200\Envir\PsNpcscripts\比奇国王-0122.pas";
var evidencePath = args.Length > 1
    ? Path.GetFullPath(args[1])
    : @"D:\loym2\staging\ida_self_corps_gild_exact_20260720.txt";

var bridge = File.ReadAllText(Path.Combine(root, "GameSvr", "ScriptSystem", "PasEngine", "PasApiBridge.cs"));
var creationCases = Slice(bridge, "case \"createselfcorps\":", "case \"addaccountstoragecnt\":");
Require(creationCases.Contains("case \"createselfgild\":", StringComparison.Ordinal),
    "CreateSelfCorps/CreateSelfGild must share the audited creation branch");
// The PAS cases now dispatch the LIVE corps/gild create (name = script arg),
// gated on SupportsGildWrites inside TPlayObject; both entry points must be wired.
Require(creationCases.Contains("TryCreateNativeCorpsFromScript", StringComparison.Ordinal),
    "createselfcorps must dispatch the live corps create entry point");
Require(creationCases.Contains("TryCreateNativeGildFromScript", StringComparison.Ordinal),
    "createselfgild must dispatch the live gild create entry point");
// ...and still fail closed (no packet, unsupported result) when no store is
// configured — the additive wiring must never regress the store-absent path.
Require(creationCases.Contains("return RejectUnsupportedNativeApi(out result);", StringComparison.Ordinal),
    "native corps/gild creation must stay fail-closed without a store");
foreach (var forbidden in new[] { "GuildManager", "AddGuild", "ExecuteScript", "gamedata.Gild", "gamedata.Corps" })
{
    Require(!creationCases.Contains(forbidden, StringComparison.Ordinal),
        $"non-native shortcut found in creation branch: {forbidden}");
}

// The live dispatch is gated on SupportsGildWrites and REUSES the exact CM-side
// service write (no duplicated write logic); with no store each entry returns
// false so the bridge falls back to RejectUnsupportedNativeApi (today's behavior).
var corpsProtocol = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
    "TPlayObject.NativeCorpsProtocol.cs"));
Require(corpsProtocol.Contains("internal bool TryCreateNativeCorpsFromScript(string name, out int result)", StringComparison.Ordinal),
    "corps script entry point signature missing");
Require(corpsProtocol.Contains("if (!service.SupportsGildWrites) return false;", StringComparison.Ordinal),
    "corps script entry must gate on SupportsGildWrites and fail closed without a store");
Require(corpsProtocol.Contains("service.ApplyCorpsCreate(CaptureNativeCorpsActor(),", StringComparison.Ordinal),
    "corps script entry must reuse the CM-side ApplyCorpsCreate write");
var guildProtocol = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
    "TPlayObject.NativeGuildCoreProtocol.cs"));
Require(guildProtocol.Contains("internal bool TryCreateNativeGildFromScript(string name, out int result)", StringComparison.Ordinal),
    "gild script entry point signature missing");
Require(guildProtocol.Contains("if (!service.SupportsGildWrites) return false;", StringComparison.Ordinal),
    "gild script entry must gate on SupportsGildWrites and fail closed without a store");
Require(guildProtocol.Contains("service.ApplyGildCreate(GetCachedNativeUserId(),", StringComparison.Ordinal),
    "gild script entry must reuse the CM-side ApplyGildCreate write");

var protocols = File.ReadAllText(Path.Combine(root, "SystemModule", "Grobal2.cs"));
RequireConstant(protocols, "CM_CORPS_CREATE", 4524);
RequireConstant(protocols, "SM_CORPS_CREATE", 4524);
RequireConstant(protocols, "CM_GILD_CREATE", 4564);
RequireConstant(protocols, "SM_GILD_CREATE", 4564);

var gbk = Encoding.GetEncoding(936);
var script = gbk.GetString(File.ReadAllBytes(productionScript));
Require(Count(script, "This_Player.CreateSelfCorps(") == 1,
    "production GBK scripts must contain exactly one CreateSelfCorps call");
Require(Count(script, "This_Player.CreateSelfGild(") == 1,
    "production GBK scripts must contain exactly one CreateSelfGild call");
foreach (var returnCode in new[] { 0, 1, 2, 3, 4, 5, 6, 555, 1000 })
{
    Require(Regex.Matches(script, $@"(?m)^\s*{returnCode}\s*:").Count >= 2,
        $"production script is missing corps/gild handling for return code {returnCode}");
}

var evidence = gbk.GetString(File.ReadAllBytes(evidencePath));
Require(evidence.Contains(
        "BASELINE_SHA256=CC505716AEB2FDB09C96B805D06C1DDDCD70DB0F331EF42AE1338C71766B452F",
        StringComparison.Ordinal),
    "static evidence baseline hash mismatch");
Require(evidence.Contains("FUNCTION CreateSelfCorps 006ADD08-006ADDA8", StringComparison.Ordinal),
    "CreateSelfCorps static body evidence missing");
Require(evidence.Contains("FUNCTION CreateSelfGild 006ADDA8-006ADDF0", StringComparison.Ordinal),
    "CreateSelfGild static body evidence missing");
foreach (var nativeTable in new[] { "gamedata.Gild", "gamedata.Corps", "gamedata.CorpsMember" })
{
    Require(evidence.Contains(nativeTable, StringComparison.Ordinal),
        $"native storage evidence missing: {nativeTable}");
}

Console.WriteLine(
    "SelfCorpsGildStaticCheck PASS " +
    "abi=Integer protocols=4524/4564 scripts=1+1 storage=Corps/CorpsMember/Gild " +
    "runtime=live-gated(SupportsGildWrites)/fail-closed-without-store");

static string Slice(string source, string startMarker, string endMarker)
{
    var start = source.IndexOf(startMarker, StringComparison.Ordinal);
    Require(start >= 0, $"missing source marker: {startMarker}");
    var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
    Require(end > start, $"missing source marker: {endMarker}");
    return source[start..end];
}

static void RequireConstant(string source, string name, int expected)
{
    var match = Regex.Match(source, $@"public\s+const\s+int\s+{Regex.Escape(name)}\s*=\s*(\d+)\s*;");
    Require(match.Success, $"missing protocol constant {name}");
    Require(int.Parse(match.Groups[1].Value) == expected,
        $"{name}: expected {expected}, got {match.Groups[1].Value}");
}

static int Count(string source, string value)
{
    var count = 0;
    for (var offset = 0; (offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0; offset += value.Length)
    {
        count++;
    }
    return count;
}

static string FindRepositoryRoot()
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameSvr", "GameSvr.csproj")))
            {
                return directory.FullName;
            }
        }
    }
    throw new InvalidOperationException("repository root not found");
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
