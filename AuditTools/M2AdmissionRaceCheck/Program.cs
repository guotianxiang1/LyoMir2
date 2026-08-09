using System.Diagnostics;
using GameSvr;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.g_Config.UserIDSection = new object();

using var accountService = new AccountService();
M2Share.g_Config.sIDSocketRecvText =
    "(100/race-user/42001/0/5/127.0.0.1)";
accountService.Run();

var payMode = 0;
var payment = 0;
var admitted = accountService.GetAdmission(
    "race-user", "127.0.0.1", 42001, ref payMode, ref payment);
Assert(admitted != null, "existing admission was rejected");
Equal(42001, admitted.nSessionID, "admission session id");
Equal(5, payMode, "admission pay mode");
Equal(1, payment, "admission payment mapping");

payMode = -1;
payment = -1;
var missing = accountService.GetAdmission(
    "missing-user", "127.0.0.1", 42002, ref payMode, ref payment);
Assert(missing == null, "missing admission must fail immediately");
Equal(0, payMode, "missing admission pay mode reset");
Equal(0, payment, "missing admission payment reset");

VerifyGateServiceContract();
VerifyAccountServiceContract();
VerifyDbAdmissionContract();
VerifyDuplicateLoginKickContract();
Console.WriteLine("M2AdmissionRaceCheck PASS mode=synchronous-single-lookup");

static void VerifyGateServiceContract()
{
    var root = FindRepositoryRoot();
    var source = File.ReadAllText(Path.Combine(
        root, "GameSvr", "GameGate", "GateService.cs"));
    Require(source, "IdSrvClient.Instance.GetAdmission(sAccount,",
        "GateService does not query admission synchronously");
    Require(source, "sessInfo != null && nPayMent > 0",
        "GateService does not validate the synchronous payment result");
    Reject(source, "AdmissionWaitMilliseconds",
        "GateService retained the guessed 100ms wait");
    Reject(source, "WaitForAdmissionAsync",
        "GateService retained asynchronous admission");
    Reject(source, "_pendingCertifications",
        "GateService retained pending certification state");
    Reject(source, "CompleteClientCertificationAsync",
        "GateService retained the asynchronous continuation");
}

static void VerifyAccountServiceContract()
{
    var root = FindRepositoryRoot();
    var source = File.ReadAllText(Path.Combine(
        root, "GameSvr", "Services", "AccountService.cs"));
    Require(source, "public TSessInfo GetAdmission(",
        "AccountService synchronous admission API is missing");
    Require(source, "lock (_sessionLock)",
        "AccountService admission list is not protected by its owner lock");
    Reject(source, "WaitForAdmissionAsync",
        "AccountService retained the guessed wait API");
    Reject(source, "_sessionChanged",
        "AccountService retained asynchronous admission notification state");
    Reject(source, "TaskCompletionSource",
        "AccountService retained asynchronous admission notification objects");
}
static void VerifyDbAdmissionContract()
{
    var root = FindRepositoryRoot();
    var source = File.ReadAllText(Path.Combine(
        root, "DBSvr", "Services", "UserSocService.cs"));
    Require(source, "private const int MobileAdmissionPaymentState = 0;",
        "DBS mobile admission payment state must use the M2-supported unpaid value");
    Require(source, "private const int MobileAdmissionPayMode = 5;",
        "DBS mobile admission pay mode must use the original default mode");
    Require(source,
        "$\"{ptid2}/{userInfo.nSessionID}/{MobileAdmissionPaymentState}/\"",
        "DBS mobile admission payload does not use the supported payment state");
    Require(source,
        "boDataOK = _loginService.TrySendSocketMsg(Grobal2.SS_OPENSESSION,",
        "DBS must not acknowledge character selection before M2 receives admission");
}

static void VerifyDuplicateLoginKickContract()
{
    var root = FindRepositoryRoot();
    var gateService = File.ReadAllText(Path.Combine(
        root, "GameSvr", "GameGate", "GateService.cs"));
    Require(gateService,
        "!string.Equals(candidate.sAccount, account,",
        "duplicate-login cleanup does not require the stale account");
    Require(gateService, "|| candidate.nSessionID != sessionId",
        "duplicate-login cleanup does not require the stale session id");
    Require(gateService,
        "lock (runSocketSection)",
        "duplicate-login cleanup is not protected by the owning GateService lock");

    var gateManager = File.ReadAllText(Path.Combine(
        root, "GameSvr", "GameGate", "GateManager.cs"));
    Require(gateManager,
        "gateService.KickUser(sAccount, nSessionID, payMode);",
        "GateManager does not delegate cleanup to the owning GateService");
    Assert(!gateManager.Contains(
            "gateUserInfo.sAccount == sAccount || gateUserInfo.nSessionID == nSessionID",
            StringComparison.Ordinal),
        "duplicate-login cleanup can still match the newly admitted session by account alone");
}

static string FindRepositoryRoot()
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        var directory = new DirectoryInfo(start);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName,
                    "GameSvr", "GameSvr.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
    }
    throw new DirectoryNotFoundException(
        "repository root containing GameSvr/GameSvr.csproj was not found");
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

static void Require(string source, string value, string message) =>
    Assert(source.Contains(value, StringComparison.Ordinal), message);
static void Reject(string source, string value, string message) =>
    Assert(!source.Contains(value, StringComparison.Ordinal), message);

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{message}: expected={expected} actual={actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
