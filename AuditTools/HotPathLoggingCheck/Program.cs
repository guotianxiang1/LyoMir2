using System.Text.RegularExpressions;

var root = FindRepositoryRoot();

var gameApp = Read("GameSvr/GameApp.cs");
var gameServer = Read("GameSvr/GameServer.cs");
var userEngine = Read("GameSvr/UsrSystem/UsrEngn.cs");
var gateService = Read("GameSvr/GameGate/GateService.cs");
var gateManager = Read("GameSvr/GameGate/GateManager.cs");
var mallProtocol = Read("GameSvr/Players/TPlayObject.Mall.cs");
var playerOperate = Read("GameSvr/Players/TPlayObject.Operate.cs");
var mallManager = Read("GameSvr/Mall/MallManager.cs");
var gameGate = Read("GameGate-CS/Core/GateServer.cs");
var gameGateForm = Read("GameGate-CS/Forms/MainForm.cs");
var loginGate = Read("LoginGate/Program.cs");
var dbLogin = Read("DBSvr/Services/LoginSocService.cs");
var dbGame = Read("DBSvr/Services/GameSocService.cs");
var dbUser = Read("DBSvr/Services/UserSocService.cs");

RequireConditional(gameGate, "GAMEGATE_PACKET_TRACE", "Trace");
RequireConditional(gateService, "GAMESVR_PACKET_TRACE", "PacketTrace");
RequireConditional(gateManager, "GAMESVR_PACKET_TRACE", "PacketTrace");
RequireConditional(dbLogin, "DBSVR_PROTOCOL_TRACE", "ProtocolTrace");
RequireConditional(dbGame, "DBSVR_PROTOCOL_TRACE", "FileLog");
RequireConditional(dbUser, "DBSVR_PROTOCOL_TRACE", "Log");

var traceSymbols = new[]
{
    "GAMEGATE_PACKET_TRACE",
    "LOGINGATE_PACKET_TRACE",
    "GAMESVR_PACKET_TRACE",
    "GAMESVR_DIAGNOSTICS",
    "DBSVR_PROTOCOL_TRACE"
};
foreach (var project in Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
             .Where(path => !IsGeneratedPath(path)))
{
    var projectText = File.ReadAllText(project);
    foreach (var symbol in traceSymbols)
    {
        if (Regex.IsMatch(projectText,
                $@"<DefineConstants>[^<]*\b{Regex.Escape(symbol)}\b[^<]*</DefineConstants>",
                RegexOptions.IgnoreCase))
        {
            Fail($"packet trace symbol enabled by project: {Relative(project)} -> {symbol}");
        }
    }
}

AssertAbsent(gameApp, "gateservice_diag.log", "GameSvr startup diagnostic file");
AssertAbsent(gameGateForm, "gategate.log", "GameGate GUI diagnostic file");
AssertAbsent(gameServer, "[Stats]", "periodic M2 statistics output");
AssertAbsent(userEngine, "[OpenMake]", "per-character creation output");
AssertAbsent(userEngine, "[OpenBindGate]", "per-character gate binding output");
AssertAbsent(mallProtocol, "MainOutMessage", "per-query mall output");
AssertAbsent(playerOperate, "MainOutMessage(\"Fail\")", "per-packet user-set failure output");
AssertAbsent(playerOperate, "MainOutMessage(format(\"OK:", "per-packet user-set success output");
AssertAbsent(mallManager, "LogPurchase(player, mallItem, quantity, totalPrice);\r\n                M2Share.MainOutMessage",
    "per-purchase console output");
AssertAbsent(mallManager, "LogPurchase(player, mallItem, quantity, totalPrice);\n                M2Share.MainOutMessage",
    "per-purchase console output");
AssertAbsent(dbUser, "DBShare.MainOutMessage($\"[AwardPlayer] PTID=", "per-character award output");
AssertAbsent(gameGate, "_speed.OnViolation +=", "per-violation GUI output");
AssertAbsent(gameGate, "var hex = string.Join", "eager packet hex formatting");

foreach (var category in new[] { "SEND", "DOWN", "CRYPT", "HWID", "RECOVERY", "TURNPACK", "SPEED", "OPEN", "DISCONNECT" })
{
    AssertAbsent(gameGate, $"Log(\"{category}\"", $"unconditional GameGate {category} output");
}

// LoginGate no longer has a Trace method to guard: Program.cs is now a 95-line entry point
// and the accept/read loops live in LoginGate/Core. Requiring a [Conditional] Trace in a file
// that no longer owns a socket also made the five AssertAbsent probes below vacuous -- they
// were scanning the wrong file and would have passed no matter what the loops logged. Pin the
// stronger property instead: the three socket services emit nothing at all, per session or
// per packet, so there is no output left to guard with a symbol.
foreach (var socketService in new[]
         {
             "LoginGate/Core/ClientSelectionService.cs",
             "LoginGate/Core/NativeDbServerService.cs",
             "LoginGate/Core/PigCompatibilityService.cs"
         })
{
    var source = Read(socketService);
    foreach (var emitter in new[]
             {
                 "Console.Write", "Console.Error", "WriteLog(", "LogReceived",
                 "File.AppendAllText", "Debug.WriteLine", "Trace.WriteLine"
             })
    {
        AssertAbsent(source, emitter,
            $"unconditional LoginGate socket-loop output in {socketService}: {emitter}");
    }
}
AssertAbsent(loginGate, "Console.Write", "LoginGate entry-point console output");
AssertAbsent(loginGate, "File.AppendAllText", "LoginGate entry-point file append");
// The lifecycle logger that survives must stay off the socket path: two start/stop lines only.
var loginGateServer = Read("LoginGate/Core/LoginGateServer.cs");
var lifecycleLogs = Regex.Matches(loginGateServer, @"WriteLog\(""INFO""").Count;
if (lifecycleLogs != 2)
    Fail($"LoginGate lifecycle logging changed: expected 2 start/stop lines, found {lifecycleLogs}");

var auditedRoots = new[] { "GameSvr", "DBSvr", "GameGate-CS", "LoginGate" };
var auditedFiles = 0;
foreach (var sourceRoot in auditedRoots)
{
    foreach (var file in Directory.EnumerateFiles(Path.Combine(root, sourceRoot), "*.cs", SearchOption.AllDirectories))
    {
        if (IsGeneratedPath(file) || IsExcludedBusinessWriter(file)) continue;
        auditedFiles++;
        var source = File.ReadAllText(file);
        // What this forbids is open-append-close per event. A single long-lived append stream
        // opened with FileOptions.Asynchronous and fed from a bounded channel is the approved
        // shape, so match on the per-call helper and on synchronous append streams only.
        if (source.Contains("File.AppendAllText", StringComparison.Ordinal)
            || (source.Contains("FileMode.Append", StringComparison.Ordinal)
                && !source.Contains("FileOptions.Asynchronous", StringComparison.Ordinal)))
        {
            Fail($"synchronous append remains outside an approved business writer: {Relative(file)}");
        }
    }
}

Console.WriteLine($"PASS files={auditedFiles} traceSymbols=disabled hotOutputs=removed appendWrites=0");
return;

string Read(string relativePath) => File.ReadAllText(Path.Combine(root,
    relativePath.Replace('/', Path.DirectorySeparatorChar)));

void RequireConditional(string source, string symbol, string method)
{
    var pattern = $@"\[\s*(?:System\.Diagnostics\.)?Conditional\(\""{Regex.Escape(symbol)}\""\)\s*\]\s*" +
                  $@"(?:(?:private|internal|public)\s+)?(?:static\s+)?void\s+{Regex.Escape(method)}\s*\(";
    if (!Regex.IsMatch(source, pattern, RegexOptions.CultureInvariant))
        Fail($"{method} must be compile-time guarded by {symbol}");
}

static void AssertAbsent(string source, string value, string description)
{
    if (source.Contains(value, StringComparison.Ordinal))
        Fail(description + " remains enabled");
}

static bool IsGeneratedPath(string path) =>
    path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
    || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
    || path.Contains($"{Path.DirectorySeparatorChar}staging{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

static bool IsExcludedBusinessWriter(string path)
{
    var normalized = path.Replace('\\', '/');
    return normalized.EndsWith("GameSvr/ScriptSystem/PasEngine/PasApiBridge.cs", StringComparison.OrdinalIgnoreCase)
           || normalized.EndsWith("GameSvr/Players/TPlayObject.Message.cs", StringComparison.OrdinalIgnoreCase)
           // The two GameGate GUI writers reproduce the gateway's own procMsgLog artifacts
           // (网关<date>.log and 聊天<date_hour>.log, both present in the production
           // GateServer/GameGate2/procMsgLog directory). They run on the UI thread off the
           // log channel, and every per-packet category that could make that channel hot --
           // SEND / DOWN / CRYPT / HWID / RECOVERY / TURNPACK / SPEED / OPEN / DISCONNECT --
           // is asserted absent above, so what reaches them is connect-rate at worst.
           || normalized.EndsWith("GameGate-CS/Forms/ClassicMainForm.cs", StringComparison.OrdinalIgnoreCase)
           || normalized.EndsWith("GameGate-CS/Forms/GgAcManagementPages.cs", StringComparison.OrdinalIgnoreCase);
}

string Relative(string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

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
    throw new DirectoryNotFoundException("Repository root containing GameSvr/GameSvr.csproj was not found.");
}

static void Fail(string message) => throw new InvalidOperationException(message);
