using System.Text;
using System.Text.RegularExpressions;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

PrepareRuntimeConfig();
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
M2Share.g_Config = new GameSvrConfig();
M2Share.UserEngine = new UserEngine();
M2Share.ObjectManager = new ObjectManager();
M2Share.MapManager = new MapManager();

var player = new TPlayObject();
foreach (var key in new[]
         {
             10006, 10007, 12001, 12002, 12003, 19002,
             21001, 21002, 21003, 21004, 21005,
             21006, 21007, 21008, 21009, 21010,
             23001, 23002
         })
{
    player.m_ScriptVVars[key] = key + 1;
}
player.m_ScriptSVars[12001] = 991;
var beforeV = player.m_ScriptVVars.OrderBy(item => item.Key).ToArray();
var beforeS = player.m_ScriptSVars.OrderBy(item => item.Key).ToArray();
var bridge = new PasApiBridge
{
    CurrentPlayer = player,
    CurrentNpc = new NormNpc()
};

foreach (var property in new[]
         {
             "GuildPoint", "DominateLevel",
             "TenYearImpress", "GetTrustByWine"
         })
{
    CheckClosedPropertyRead(bridge, property);
}
Assert(!bridge.SetPlayerProperty("DominateLevel", PasValue.FromInt(9)),
    "DominateLevel property write did not fail closed");

// JiaYouPoint 不是 shadow：它是原生的**只读**发布属性。Delphi TPropInfo
// （Name 在记录末尾）@0x006ACDBE:
//   PropType   0x006ACDBE  94 10 40 00
//   GetProc    0x006ACDC2  f0 0a 00 ff  -> FF = 直接字段，偏移 0x0AF0
//   SetProc    0x006ACDC6  00 00 00 00  -> nil，只读
//   StoredProc 0x006ACDCA  01 00 00 00
//   NameIndex  0x006ACDD6  19 00
//   Name       0x006ACDD8  0b "JiaYouPoint"
player.m_dwJiaYouPoint = 1234;
Assert(bridge.GetPlayerProperty("JiaYouPoint", out var jiaYouRead),
    "JiaYouPoint property read must serve the native +0xAF0 field");
Assert(jiaYouRead.AsInt() == 1234,
    "JiaYouPoint property read returned the wrong value");
Assert(!bridge.SetPlayerProperty("JiaYouPoint", PasValue.FromInt(7)),
    "read-only JiaYouPoint acquired a write path");
Assert(player.m_dwJiaYouPoint == 1234,
    "a rejected JiaYouPoint write still mutated the field");
player.m_dwJiaYouPoint = 0;

foreach (var method in new[]
         {
             "AddGuildPoint", "SetVExpToBeConverted"
         })
{
    CheckClosedPlayerMethod(bridge, method,
        new List<PasValue> { PasValue.FromInt(7) });
}

// DecJiaYouPoint 是 JiaYouPoint 只读属性的专用变更器，原生 sub_6F28E8：
//   0x006F28ED  85 db              test ebx,ebx
//   0x006F28EF  7e 30              jle 0x6F2921        ; point<=0 直接返回
//   0x006F28F6  8b 81 f0 0a 00 00  mov eax,[ecx+0xAF0]
//   0x006F2909  73 10              jae 0x6F291B        ; 余额>=point 走减法
//   0x006F2913  89 81 f0 0a 00 00  mov [ecx+0xAF0],eax ; 否则夹到 0
//   0x006F291B  29 99 f0 0a 00 00  sub [ecx+0xAF0],ebx
// 名字本身也在底本里：0x007301E1 "procedure DecJiaYouPoint(point: Integer);"
// (FF FF FF FF 29 00 00 00)、0x00733653 "DecJiaYouPoint" (…0E 00 00 00)。
player.m_dwJiaYouPoint = 10;
Assert(bridge.CallPlayerMethod("DecJiaYouPoint",
        new List<PasValue> { PasValue.FromInt(0) }),
    "DecJiaYouPoint must accept its native ABI");
Equal(10u, player.m_dwJiaYouPoint, "DecJiaYouPoint point<=0 must be a no-op");
Assert(bridge.CallPlayerMethod("DecJiaYouPoint",
        new List<PasValue> { PasValue.FromInt(4) }),
    "DecJiaYouPoint must accept its native ABI");
Equal(6u, player.m_dwJiaYouPoint, "DecJiaYouPoint subtraction");
Assert(bridge.CallPlayerMethod("DecJiaYouPoint",
        new List<PasValue> { PasValue.FromInt(100) }),
    "DecJiaYouPoint must accept its native ABI");
Equal(0u, player.m_dwJiaYouPoint, "DecJiaYouPoint underflow must clamp at 0");

foreach (var function in new[]
         {
             "GetVExpToBeConverted", "IncVExpToBeConverted",
             "DecVExpToBeConverted", "ChgTenYearImpress",
             "GetLianTiLv", "GetQiangTiLv", "GetTiPoLv",
             "GetQiangTiPhase", "GetTiPoPhase",
             "GetLianTiLv_hero", "GetQiangTiLv_hero", "GetTiPoLv_hero",
             "GetQiangTiPhase_hero", "GetTiPoPhase_hero"
         })
{
    CheckClosedPlayerFunction(bridge, function,
        new List<PasValue> { PasValue.FromInt(7) });
}

foreach (var global in new[]
         {
             "UseGuildPoint", "GetSomeGuildPoint", "SetWineTreat",
             "GetTreatWine", "ConvertVExp"
         })
{
    CheckClosedStandalone(bridge, global,
        new List<PasValue>
        {
            PasValue.FromObject(player), PasValue.FromInt(7),
            PasValue.FromInt(2)
        });
}

CheckWrongAbiRoutes(bridge);
Assert(beforeV.SequenceEqual(player.m_ScriptVVars.OrderBy(item => item.Key)),
    "a fail-closed P1/P2/P3 API changed V variables");
Assert(beforeS.SequenceEqual(player.m_ScriptSVars.OrderBy(item => item.Key)),
    "a fail-closed P1/P2/P3 API changed S variables");

var root = FindRepositoryRoot();
var source = File.ReadAllText(Path.Combine(root, "GameSvr", "ScriptSystem",
    "PasEngine", "PasApiBridge.cs"));
CheckSourceOwnership(source);
CheckGenericVariableRoutes(source);

var production = FindProductionEnvir(root);
var matrix = ScanProductionGbk(production);
Equal(1_349, matrix.FileCount, "production PAS file count");
foreach (var name in GetNativeIdentifiers())
    Equal(0, matrix.IdentifierCounts[name], "production native identifier " + name);

var expectedSlots = new Dictionary<string, int>(StringComparer.Ordinal)
{
    ["V10:6"] = 0, ["V10:7"] = 0, ["V12:1"] = 14,
    ["V19:2"] = 3,
    ["V21:1"] = 10, ["V21:2"] = 4, ["V21:3"] = 0,
    ["V21:4"] = 2, ["V21:5"] = 0, ["V21:6"] = 3,
    ["V21:7"] = 3, ["V21:8"] = 0, ["V21:9"] = 0,
    ["V21:10"] = 0,
    ["V23:1"] = 10, ["V23:2"] = 8
};
foreach (var expected in expectedSlots)
    Equal(expected.Value, matrix.SlotCounts[expected.Key],
        "production fixed-slot count " + expected.Key);

Console.WriteLine(
    "PASS P1/P2/P3 native shadows=closed ABI=locked V/S=unchanged " +
    $"GBK-PAS={matrix.FileCount} strict-errors={matrix.StrictGbkErrors} " +
    "native-calls=0 fixed-slots=verified generic-V/S=present");
return;

static string[] GetNativeIdentifiers() =>
new[]
{
    "GuildPoint", "AddGuildPoint", "UseGuildPoint", "GetSomeGuildPoint",
    "JiaYouPoint", "DecJiaYouPoint", "DominateLevel",
    "TenYearImpress", "ChgTenYearImpress", "GetTrustByWine",
    "SetWineTreat", "GetTreatWine", "GetVExpToBeConverted",
    "SetVExpToBeConverted", "IncVExpToBeConverted",
    "DecVExpToBeConverted", "ConvertVExp", "GetLianTiLv",
    "GetQiangTiLv", "GetTiPoLv", "GetQiangTiPhase", "GetTiPoPhase",
    "GetLianTiLv_hero", "GetQiangTiLv_hero", "GetTiPoLv_hero",
    "GetQiangTiPhase_hero", "GetTiPoPhase_hero"
};

static void CheckClosedPropertyRead(PasApiBridge bridge, string name)
{
    Assert(!bridge.GetPlayerProperty(name, out var result),
        name + " property read did not fail closed");
    AssertNil(result, name + " property read");
}

static void CheckClosedPlayerMethod(PasApiBridge bridge, string name,
    List<PasValue> args)
{
    Assert(!bridge.CallPlayerMethod(name, args),
        name + " player method did not fail closed");
}

static void CheckClosedPlayerFunction(PasApiBridge bridge, string name,
    List<PasValue> args)
{
    Assert(!bridge.CallPlayerFunc(name, args, out var result),
        name + " player function did not fail closed");
    AssertNil(result, name + " player function");
}

static void CheckClosedStandalone(PasApiBridge bridge, string name,
    List<PasValue> args)
{
    Assert(!bridge.CallStandaloneFunction(name, args, out var result),
        name + " standalone API did not fail closed");
    AssertNil(result, name + " standalone API");
}

static void CheckWrongAbiRoutes(PasApiBridge bridge)
{
    foreach (var name in new[]
             {
                 "GuildPoint", "JiaYouPoint", "DominateLevel",
                 "TenYearImpress", "GetTrustByWine", "SetVExpToBeConverted",
                 "AddGuildPoint", "DecJiaYouPoint"
             })
    {
        Assert(!bridge.CallPlayerFunc(name, new List<PasValue>(), out var result),
            name + " acquired a non-native player-function route");
        AssertNil(result, name + " wrong player-function route");
    }

    foreach (var name in new[]
             {
                 "GetVExpToBeConverted", "IncVExpToBeConverted",
                 "DecVExpToBeConverted", "ChgTenYearImpress",
                 "GetLianTiLv", "GetQiangTiLv", "GetTiPoLv",
                 "GetQiangTiPhase", "GetTiPoPhase"
             })
    {
        Assert(!bridge.CallPlayerMethod(name, new List<PasValue>()),
            name + " acquired a non-native player-method route");
    }

    foreach (var name in new[]
             {
                 "UseGuildPoint", "GetSomeGuildPoint", "SetWineTreat",
                 "GetTreatWine", "ConvertVExp", "AddGuildPoint"
             })
    {
        Assert(!bridge.CallNpcMethod(name, new List<PasValue>(), out var methodResult),
            name + " acquired a non-native NPC-method route");
        AssertNil(methodResult, name + " wrong NPC-method route");
        Assert(!bridge.CallNpcFunc(name, new List<PasValue>(), out var funcResult),
            name + " acquired a non-native NPC-function route");
        AssertNil(funcResult, name + " wrong NPC-function route");
    }

    foreach (var alias in new[]
             {
                 "GetTiLv", "GetTipLv", "GetTipPhase",
                 "GetTiLv_hero", "GetTipLv_hero", "GetTipPhase_hero",
                 "GetTenYearImpress"
             })
    {
        Assert(!bridge.CallPlayerFunc(alias, new List<PasValue>(), out var result),
            alias + " non-native alias is still exposed");
        AssertNil(result, alias + " non-native alias");
    }
}

static void CheckSourceOwnership(string source)
{
    var propertyReads = Slice(source, "public bool GetPlayerProperty",
        "public bool SetPlayerProperty");
    var propertyWrites = Slice(source, "public bool SetPlayerProperty",
        "public bool GetNpcProperty");
    var methods = Slice(source, "public bool CallPlayerMethod",
        "public bool CallPlayerFunc");
    var functions = Slice(source, "public bool CallPlayerFunc",
        "public bool CallNpcMethod");
    var npcMethods = Slice(source, "public bool CallNpcMethod",
        "public bool CallNpcFunc");
    var npcFunctions = Slice(source, "public bool CallNpcFunc",
        "public bool CallStandaloneFunction");
    var standalone = Slice(source, "public bool CallStandaloneFunction",
        "public bool TryCallThisPlayerFunc");

    RequireCases(propertyReads, "guildpoint", "jiayoupoint", "dominatelevel",
        "tenyearimpress", "gettrustbywine");
    RequireCases(propertyWrites, "dominatelevel");
    RequireCases(methods, "addguildpoint", "decjiayoupoint",
        "setvexptobeconverted");
    RequireCases(functions, "getvexptobeconverted", "incvexptobeconverted",
        "decvexptobeconverted", "chgtenyearimpress", "getliantilv",
        "getqiangtilv", "gettipolv", "getqiangtiphase", "gettipophase",
        "getliantilv_hero", "getqiangtilv_hero", "gettipolv_hero",
        "getqiangtiphase_hero", "gettipophase_hero");
    // UseGuildPoint / GetSomeGuildPoint / SetWineTreat / GetTreatWine 是 NPC 面
    // 的注册项，不是独立全局函数：注册运 0x0073472D..0x00735099 共 201 条
    // `ba <declStr> / 8b c3 / e8 -> 0x00510FFC`，运头就是 My_X / My_Y / NPCSay /
    // CreateMon / ClearMon 这批 NPC API。四条各自的记录与声明串：
    //   0x00734ABD -> 0x00736608 "function UseGuildPoint(Player: TPlayer) : Integer;"
    //   0x00734AC9 -> 0x00736644 "function GetSomeGuildPoint(Player: TPlayer) : Integer;"
    //   0x00734E65 -> 0x007379F0 "procedure SetWineTreat(wtType: Byte; boDesk: Boolean);"
    //   0x00734E71 -> 0x00737A30 "function GetTreatWine(Hum: TPlayer): Integer;"
    // ConvertVExp 在底本里 0 命中，故留在 standalone。
    RequireCases(npcFunctions, "useguildpoint", "getsomeguildpoint",
        "setwinetreat", "gettreatwine");
    RequireCases(standalone, "convertvexp");

    ForbidCases(methods, "useguildpoint", "getsomeguildpoint",
        "incvexptobeconverted", "decvexptobeconverted", "convertvexp",
        "chgtenyearimpress");
    ForbidCases(functions, "getsomeguildpoint", "dominatelevel",
        "gettenyearimpress", "gettiplv", "gettipphase",
        "gettiplv_hero", "gettipphase_hero");
    ForbidCases(npcMethods, "addguildpoint", "setwinetreat", "gettreatwine");
    ForbidCases(npcFunctions, "addguildpoint", "convertvexp");

    foreach (var name in new[]
             {
                 "guildpoint", "jiayoupoint", "tenyearimpress",
                 "gettrustbywine", "addguildpoint", "decjiayoupoint",
                 "setvexptobeconverted", "getvexptobeconverted",
                 "incvexptobeconverted", "decvexptobeconverted",
                 "chgtenyearimpress", "useguildpoint", "getsomeguildpoint",
                 "setwinetreat", "gettreatwine", "convertvexp"
             })
    {
        CheckCaseCount(source, name, 1);
    }
    CheckCaseCount(source, "dominatelevel", 2);
    foreach (var name in new[]
             {
                 "getliantilv", "getqiangtilv", "gettipolv",
                 "getqiangtiphase", "gettipophase", "getliantilv_hero",
                 "getqiangtilv_hero", "gettipolv_hero",
                 "getqiangtiphase_hero", "gettipophase_hero"
             })
        CheckCaseCount(source, name, 1);

    foreach (var forbidden in new[]
             {
                 "GetPlayerVar('V', 10, 6", "SetPlayerVar('V', 10, 6",
                 "GetPlayerVar('V', 10, 7", "SetPlayerVar('V', 10, 7",
                 "GetPlayerVar('V', 12, 1", "SetPlayerVar('V', 12, 1",
                 "GetPlayerVar('V', 19, 2", "SetPlayerVar('V', 19, 2",
                 "GetPlayerVar('V', 21,", "SetPlayerVar('V', 21,",
                 "GetPlayerVar('V', 23, 1", "SetPlayerVar('V', 23, 1",
                 "GetPlayerVar('V', 23, 2", "SetPlayerVar('V', 23, 2"
             })
    {
        Assert(!source.Contains(forbidden, StringComparison.Ordinal),
            "fixed native shadow remains: " + forbidden);
    }
}

static void CheckGenericVariableRoutes(string source)
{
    var methods = Slice(source, "public bool CallPlayerMethod",
        "public bool CallPlayerFunc");
    var functions = Slice(source, "public bool CallPlayerFunc",
        "public bool CallNpcMethod");
    foreach (var name in new[] { "setv", "sets" })
        RequireCaseWith(methods, name, "SetPlayerVar(");
    foreach (var name in new[] { "getv", "setv", "gets", "sets" })
        RequireCaseWith(functions, name,
            name.StartsWith("get", StringComparison.Ordinal) ? "GetPlayerVar(" : "SetPlayerVar(");
}

static ProductionMatrix ScanProductionGbk(string directory)
{
    var gbk = Encoding.GetEncoding(936);
    var strictGbk = Encoding.GetEncoding(936, EncoderFallback.ExceptionFallback,
        DecoderFallback.ExceptionFallback);
    var nativeIdentifiers = GetNativeIdentifiers();
    var identifierCounts = nativeIdentifiers.ToDictionary(name => name, _ => 0,
        StringComparer.OrdinalIgnoreCase);
    var slots = new[]
    {
        "10:6", "10:7", "12:1", "19:2", "21:1", "21:2", "21:3",
        "21:4", "21:5", "21:6", "21:7", "21:8", "21:9", "21:10",
        "23:1", "23:2"
    };
    var slotCounts = slots.ToDictionary(slot => "V" + slot, _ => 0,
        StringComparer.Ordinal);
    var files = Directory.GetFiles(directory, "*.pas", SearchOption.AllDirectories);
    var strictErrors = 0;

    foreach (var file in files)
    {
        var bytes = File.ReadAllBytes(file);
        try { _ = strictGbk.GetString(bytes); }
        catch (DecoderFallbackException) { strictErrors++; }
        var text = gbk.GetString(bytes);
        foreach (var name in nativeIdentifiers)
            identifierCounts[name] += Regex.Matches(text,
                $@"(?i)(?<![A-Za-z0-9_]){Regex.Escape(name)}(?![A-Za-z0-9_])",
                RegexOptions.CultureInvariant).Count;
        foreach (var slot in slots)
        {
            var parts = slot.Split(':');
            slotCounts["V" + slot] += Regex.Matches(text,
                $@"(?i)(?<![A-Za-z0-9_])(?:GetV|SetV)\s*\(\s*{parts[0]}\s*,\s*{parts[1]}(?=\s*[,\)])",
                RegexOptions.CultureInvariant).Count;
        }
    }

    return new ProductionMatrix(files.Length, strictErrors, identifierCounts,
        slotCounts);
}

static string FindProductionEnvir(string repositoryRoot)
{
    var candidates = new[]
    {
        Environment.GetEnvironmentVariable("LYOM2_PRODUCTION_ENVIR"),
        @"D:\lyom2Release\mud2.0\Mir200\Envir",
        Path.GetFullPath(Path.Combine(repositoryRoot, "..", "staging",
            "pas-include-context-20260714", "Envir"))
    };
    return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) &&
        Directory.Exists(path)) ?? throw new DirectoryNotFoundException(
        "production GBK Mir200/Envir corpus not found");
}

static void RequireCases(string region, params string[] names)
{
    foreach (var name in names)
        Require(region, $"case \"{name}\":", "missing ABI case: " + name);
}

static void ForbidCases(string region, params string[] names)
{
    foreach (var name in names)
        Assert(!region.Contains($"case \"{name}\":", StringComparison.Ordinal),
            "non-native ABI case remains: " + name);
}

static void RequireCaseWith(string region, string name, string value)
{
    var marker = $"case \"{name}\":";
    var start = region.IndexOf(marker, StringComparison.Ordinal);
    Assert(start >= 0, "missing generic V/S route: " + name);
    var next = region.IndexOf("case \"", start + marker.Length,
        StringComparison.Ordinal);
    var body = next < 0 ? region[start..] : region[start..next];
    Require(body, value, name + " generic V/S helper was removed");
}

static void CheckCaseCount(string source, string name, int expected)
{
    var actual = Regex.Matches(source, $"case \\\"{Regex.Escape(name)}\\\":",
        RegexOptions.CultureInvariant).Count;
    Equal(expected, actual, name + " case count");
}

static string Slice(string source, string startMarker, string endMarker)
{
    var start = source.IndexOf(startMarker, StringComparison.Ordinal);
    Assert(start >= 0, "missing marker: " + startMarker);
    var end = source.IndexOf(endMarker, start + startMarker.Length,
        StringComparison.Ordinal);
    Assert(end > start, "missing marker: " + endMarker);
    return source[start..end];
}

static void Require(string source, string value, string message) =>
    Assert(source.Contains(value, StringComparison.Ordinal), message);

static void AssertNil(PasValue value, string message) =>
    Assert(value.Type == PasValueType.Nil, message + " did not return Nil");

static void Equal<T>(T expected, T actual, string message) where T : IEquatable<T>
{
    if (!expected.Equals(actual))
        throw new InvalidOperationException(
            $"{message}: expected {expected}, actual {actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static string FindRepositoryRoot()
{
    return AuditRepoRoot.Resolve();
}

static void PrepareRuntimeConfig()
{
    var runtimeDirectory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(runtimeDirectory, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
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

sealed record ProductionMatrix(int FileCount, int StrictGbkErrors,
    Dictionary<string, int> IdentifierCounts,
    Dictionary<string, int> SlotCounts);
