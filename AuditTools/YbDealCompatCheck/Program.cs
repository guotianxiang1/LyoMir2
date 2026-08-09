using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using GameSvr;
using GameSvr.PasEngine;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.ObjectManager = new ObjectManager();
M2Share.LogMsgCriticalSection = new object();
M2Share.LogStringList = new System.Collections.ArrayList();

var player = new TPlayObject { m_nGameGold = 1234 };
var npc = new NormNpc();
var bridge = new PasApiBridge
{
    CurrentPlayer = player,
    CurrentNpc = npc
};
var playerArg = PasValue.FromObject(player);

foreach (var first in new[] { true, false })
{
    var callArgs = new List<PasValue> { playerArg, PasValue.FromBool(first) };
    Assert(!bridge.CallNpcMethod("YBDealDialogShowMode", callArgs, out _),
        $"YBDealDialogShowMode({first}) exposed an incomplete native transaction");
    Assert(!bridge.CallNpcFunc("YBDealDialogShowMode", callArgs, out var npcFuncResult),
        $"YBDealDialogShowMode({first}) exposed an NPC function alias");
    Equal(PasValueType.Nil, npcFuncResult.Type,
        "rejected NPC function result must remain Nil");
    Assert(!bridge.CallPlayerMethod("YBDealDialogShowMode", callArgs),
        $"YBDealDialogShowMode({first}) exposed a player method alias");
    Assert(!bridge.CallPlayerFunc("YBDealDialogShowMode", callArgs,
            out var playerFuncResult),
        $"YBDealDialogShowMode({first}) exposed a player function alias");
    Equal(PasValueType.Nil, playerFuncResult.Type,
        "rejected player function result must remain Nil");
}
Assert(player.m_nGameGold == 1234,
    "YBDealDialogShowMode changed character-local GameGold");
Equal(0, player.m_MsgList.Count,
    "YBDealDialogShowMode emitted a substitute client message");
Equal(0, M2Share.LogStringList.Count,
    "YBDealDialogShowMode emitted a substitute game log");

var root = FindRepositoryRoot();
var bridgeSource = File.ReadAllText(Path.Combine(root, "GameSvr", "ScriptSystem", "PasEngine",
    "PasApiBridge.cs"));
var ybDbSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Services",
    "YbDbClient.cs"));
var ybCreditSource = File.ReadAllText(Path.Combine(root, "GameSvr", "Players",
    "TPlayObject.NativeYbCredit.cs"));
var marker = "case \"ybdealdialogshowmode\":";
Equal(1, Count(bridgeSource, marker), "YBDealDialogShowMode dispatch count");
var markerOffset = bridgeSource.IndexOf(marker, StringComparison.Ordinal);
var nextCase = bridgeSource.IndexOf("case \"", markerOffset + marker.Length, StringComparison.Ordinal);
var dispatch = bridgeSource.Substring(markerOffset, nextCase - markerOffset);
Require(dispatch, "RejectUnsupportedNativeApi(out result)",
    "YBDealDialogShowMode dispatch is not fail-closed");
foreach (var forbidden in new[]
         {
             "MallManager", "m_nGameGold", "m_ScriptVVars", "m_ScriptSVars",
             "RM_SENDGOODSLIST", "SM_SENDGOODSLIST", "SendMsg(", "SendSocket(",
             "YbDbClient", "NativeYb", "BuildNativeYbDealPackets", "MakeDefaultMsg",
             "4446", "3001", "3009", "3010", "SellItems", "YBDealHis",
             "M2_YB_Deal_SetInfo"
         })
{
    Reject(dispatch, forbidden, $"non-native substitute in YBDealDialogShowMode: {forbidden}");
}

var npcMethodStart = bridgeSource.IndexOf("public bool CallNpcMethod(",
    StringComparison.Ordinal);
var npcFuncStart = bridgeSource.IndexOf("public bool CallNpcFunc(",
    StringComparison.Ordinal);
Assert(npcMethodStart >= 0 && markerOffset > npcMethodStart && markerOffset < npcFuncStart,
    "YBDealDialogShowMode must remain on the NPC procedure surface");

foreach (var externalIdent in Enumerable.Range(310, 18))
{
    RejectWord(ybDbSource, externalIdent,
        $"YBDB external request {externalIdent} was partially exposed");
    RejectWord(ybDbSource, externalIdent + 1000,
        $"YBDB external response {externalIdent + 1000} was partially exposed");
}
foreach (var forbidden in new[]
         {
             "RequestYbDeal", "ProcessYbDeal", "SellItems", "YBDealHis",
             "M2_YB_Deal_SetInfo"
         })
{
    Reject(ybDbSource, forbidden,
        $"partial external YBDeal authority in YbDbClient: {forbidden}");
}
Require(ybCreditSource, "NativeYbDealOpenIdent = 3009",
    "closed 1103 one-shot YBDeal open message missing");
Require(ybCreditSource, "NativeYbDealProtectIdent = 3010",
    "closed 1103 one-shot YBDeal protection message missing");
Reject(ybCreditSource, "YBDealDialogShowMode",
    "1103 credit snapshot was substituted for the PAS wrapper");
foreach (var externalIdent in Enumerable.Range(310, 18))
    RejectWord(ybCreditSource, externalIdent,
        $"credit snapshot contains partial YBDeal request {externalIdent}");

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var envir = FindProductionEnvir();
var strictGbk = Encoding.GetEncoding(936, EncoderFallback.ExceptionFallback,
    DecoderFallback.ExceptionFallback);
var callPattern = new Regex(
    @"This_NPC\s*\.\s*YBDealDialogShowMode\s*\(\s*This_Player\s*,\s*(true|false)\s*\)",
    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
var calls = new List<(string File, bool First)>();
var sourceFiles = new List<(string File, string Source, string Hash)>();
foreach (var path in Directory.EnumerateFiles(envir, "*.pas", SearchOption.AllDirectories))
{
    var bytes = File.ReadAllBytes(path);
    if (!Encoding.ASCII.GetString(bytes).Contains("YBDealDialogShowMode",
            StringComparison.OrdinalIgnoreCase))
        continue;
    var source = strictGbk.GetString(bytes);
    sourceFiles.Add((Path.GetRelativePath(envir, path), source,
        Convert.ToHexString(SHA256.HashData(bytes))));
    foreach (Match match in callPattern.Matches(source))
        calls.Add((Path.GetRelativePath(envir, path),
            bool.Parse(match.Groups[1].Value)));
}

Equal(10, calls.Count, "production PAS YBDealDialogShowMode call count");
Equal(5, calls.Select(call => call.File).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
    "production PAS YBDealDialogShowMode file count");
Equal(5, calls.Count(call => call.First), "production PAS first-page call count");
Equal(5, calls.Count(call => !call.First), "production PAS list-page call count");
Equal(5, sourceFiles.Count, "production PAS source file count");
Equal(1, sourceFiles.Select(file => file.Hash).Distinct(StringComparer.Ordinal).Count(),
    "production PAS copies diverged");
Equal("F62ACDDB68866D505DE144D4126B7E804CF27819F20E91FF9243563470D6025C",
    sourceFiles[0].Hash, "production PAS source hash");
var expectedNames = new[]
{
    "PsNpcscripts/\u5143\u5B9D\u4EA4\u6613-0.pas",
    "PsNpcscripts/\u5143\u5B9D\u4EA4\u6613-0139~13.pas",
    "PsNpcscripts/\u5143\u5B9D\u4EA4\u6613-3.pas",
    "PsNpcscripts/\u5143\u5B9D\u4EA4\u6613-6.pas",
    "PsNpcscripts/\u5143\u5B9D\u4EA4\u6613-GA0.pas"
};
Equal(string.Join("|", expectedNames.OrderBy(name => name, StringComparer.Ordinal)),
    string.Join("|", sourceFiles.Select(file => file.File.Replace('\\', '/'))
        .OrderBy(name => name, StringComparer.Ordinal)),
    "production PAS source names");
var setModePattern = new Regex(
    @"This_Player\s*\.\s*SetV\s*\(\s*11\s*,\s*10\s*,\s*888\s*\)",
    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
var checkModePattern = new Regex(
    @"This_Player\s*\.\s*GetV\s*\(\s*11\s*,\s*10\s*\)\s*<>\s*888",
    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
Equal(5, sourceFiles.Sum(file => setModePattern.Matches(file.Source).Count),
    "production PAS SetV mode-gate count");
Equal(5, sourceFiles.Sum(file => checkModePattern.Matches(file.Source).Count),
    "production PAS GetV mode-gate count");

Console.WriteLine(
    $"PASS calls={calls.Count} files=5 true=5 false=5 mode=fail-closed localYB=unchanged substitutes=0");
return;

static string FindProductionEnvir()
{
    var candidates = new[]
    {
        Environment.GetEnvironmentVariable("LYOMIR_ENVIR"),
        @"D:\lyom2Release\mud2.0\Mir200\Envir"
    };
    foreach (var candidate in candidates)
    {
        if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
            return candidate;
    }
    throw new DirectoryNotFoundException(
        "Production Envir was not found. Set LYOMIR_ENVIR to the Mir200/Envir directory.");
}

static int Count(string source, string value)
{
    var count = 0;
    for (var offset = 0;;)
    {
        var index = source.IndexOf(value, offset, StringComparison.Ordinal);
        if (index < 0) return count;
        count++;
        offset = index + value.Length;
    }
}

static string FindRepositoryRoot()
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        var directory = new DirectoryInfo(start);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameSvr", "GameSvr.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
    }
    throw new DirectoryNotFoundException(
        "Repository root containing GameSvr/GameSvr.csproj was not found.");
}

static void PrepareRuntimeConfig()
{
    var runtimeDirectory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(runtimeDirectory, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "String.ini"),
        "[String]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "Command.conf"),
        "[Command]" + Environment.NewLine);

    var shareDirectory = Path.Combine(Path.GetFullPath(
        Path.Combine(runtimeDirectory, "..")), "Share");
    Directory.CreateDirectory(shareDirectory);
    File.WriteAllText(Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"),
        "[Integer]" + Environment.NewLine);
}

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
}

static void Require(string source, string value, string message)
{
    if (!source.Contains(value, StringComparison.Ordinal))
        throw new InvalidOperationException(message);
}

static void Reject(string source, string value, string message)
{
    if (source.Contains(value, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException(message);
}

static void RejectWord(string source, int value, string message)
{
    if (Regex.IsMatch(source, $@"\b{value}\b", RegexOptions.CultureInvariant))
        throw new InvalidOperationException(message);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
