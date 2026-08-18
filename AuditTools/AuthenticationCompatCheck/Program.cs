using System.Reflection;
using GameSvr;
using GameSvr.PasEngine;
using SystemModule;

PrepareRuntimeConfig();
M2Share.g_Config = new GameSvrConfig();
M2Share.ObjectManager = new ObjectManager();
M2Share.ProcessMsgCriticalSection = new object();
M2Share.LogMsgCriticalSection = new object();
M2Share.g_MonSayMsgList = new Dictionary<string, IList<TMonSayMsg>>();

var player = new TPlayObject();
var bridge = new PasApiBridge { CurrentPlayer = player };
var engine = new UserEngine();
var getHumData = typeof(UserEngine).GetMethod("GetHumData",
    BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new MissingMethodException(typeof(UserEngine).FullName, "GetHumData");
var setStatus = typeof(TPlayObject).GetMethod(
    "SetNativeAuthenticationStatus",
    BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new MissingMethodException("SetNativeAuthenticationStatus");
var applyLimits = typeof(TPlayObject).GetMethod(
    "ApplyNativeAuthenticationLimits",
    BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new MissingMethodException("ApplyNativeAuthenticationLimits");
var buildStatusMessage = typeof(TPlayObject).GetMethod(
    "BuildNativeAuthenticationStatusMessage",
    BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new MissingMethodException("BuildNativeAuthenticationStatusMessage");
var resolveLoadedStorageCapacity = typeof(NativeAuthenticationManager).GetMethod(
    "ResolveLoadedStorageCapacity",
    BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new MissingMethodException("ResolveLoadedStorageCapacity");

SetStatus(0x1F, 0x80, 0x04);
Verify(true, 1, 100, "level 1 low-five aggregate");
Verify(true, 1, 0, "level 1 bit 0");
Verify(true, 1, 4, "level 1 bit 4");
Verify(false, 1, 5, "level 1 bit 5");
Verify(true, 2, 7, "level 2 bit 7");
Verify(false, 2, 100, "level 2 aggregate");
Verify(true, 3, 2, "level 3 help count bit 2");
Verify(false, 0, 0, "level below range");
Verify(false, 4, 0, "level above range");
Verify(false, 1, -1, "negative order");
Verify(false, 1, 8, "order above bit range");
Verify(false, 1, 99, "unsupported aggregate order");
Verify(false, 1, 100, "aggregate requires all five bits", 0x1E, 0, 0);

VerifyLimits(false, 0, 0, 50_000_000, 192,
    "authentication disabled uses native full limits");
VerifyLimits(true, 0x01, 0, 50_000_000, 24,
    "Status1 bit 0 grants only gold limit");
VerifyLimits(true, 0, 0x08, 2_000_000, 192,
    "Status2 bit 3 grants only storage limit");
VerifyLimits(true, 0x09, 0, 50_000_000, 192,
    "Status1 grants both limits");

foreach (var baseline in new[] { 24, 48, 192 })
{
    foreach (var persisted in new[] { 0, 24, 48, 49, 192, 193, 65535 })
    {
        VerifyStorageRestore(baseline, persisted,
            persisted > TPlayObject.STORAGE_PAGE_SIZE
                ? persisted
                : baseline);
    }
}

foreach (var authenticationCapacity in new[] { 24, 192 })
{
    foreach (var persisted in new[] { 0, 24, 48, 49, 192, 193, 65535 })
    {
        // Before the late authentication query, GetHumData leaves the ctor's
        // 48 for stored values <=48 and installs the raw WORD otherwise.
        var loadedCapacity = persisted > TPlayObject.STORAGE_PAGE_SIZE
            ? persisted
            : TPlayObject.STORAGE_PAGE_SIZE;
        var resolved = (int)resolveLoadedStorageCapacity.Invoke(null,
            new object[] { loadedCapacity, authenticationCapacity });
        Equal(persisted > TPlayObject.STORAGE_PAGE_SIZE
                ? persisted
                : authenticationCapacity,
            resolved,
            $"login storage order auth={authenticationCapacity} persisted={persisted}");
    }
}

Equal(4636, Grobal2.SM_PLAYER_AUTHEN, "SM_PLAYER_AUTHEN protocol constant");
VerifyStatusMessage(0x1F, Grobal2.RC_PLAYOBJECT, 0,
    "authenticated player");
VerifyStatusMessage(0, Grobal2.RC_PLAYOBJECT, -1,
    "unauthenticated player");
M2Share.g_Config.boAuthOpen = false;
VerifyStatusMessage(0x1F, Grobal2.RC_PLAYOBJECT, 0,
    "authentication result does not depend on boAuthOpen");
VerifyStatusMessage(0x1F, Grobal2.RC_HEROOBJECT, -1, "hero race");
VerifyStatusMessage(0x1F, Grobal2.RC_MONSTER, -1, "monster race");

player.m_nGold = 1_999_999;
player.m_nGoldMax = 2_000_000;
Assert(player.IncGold(1), "IncGold accepts amount at player limit");
Assert(!player.IncGold(1), "IncGold rejects amount above player limit");
Assert(!player.IncGold(0), "IncGold rejects zero");
Assert(!player.IncGold(-1), "IncGold rejects negative amount");
player.m_nGold = 1;
Assert(player.IncGold(int.MaxValue),
    "IncGold must preserve native Int32 positive overflow behavior");
Equal(int.MinValue, player.m_nGold,
    "IncGold native Int32 overflow result");

Assert(bridge.CallPlayerFunc("HelpOtherAuthen", new List<PasValue>(),
        out var helpOtherResult),
    "HelpOtherAuthen zero-argument function dispatch");
Equal(0, helpOtherResult.AsInt(),
    "HelpOtherAuthen unloaded identity result");
Assert(!bridge.CallPlayerFunc("HelpOtherAuthen",
        new List<PasValue> { PasValue.FromInt(0) }, out var rejectedHelpOther),
    "HelpOtherAuthen function arguments must remain fail-closed");
Equal(PasValueType.Nil, rejectedHelpOther.Type,
    "rejected HelpOtherAuthen result must remain Nil");
Assert(!bridge.CallPlayerMethod("HelpOtherAuthen", new List<PasValue>()),
    "HelpOtherAuthen procedure form must remain fail-closed");

VerifySourceContract();
Console.WriteLine("AuthenticationCompatCheck PASS");
return;

void SetStatus(byte status1, byte status2, byte status3) =>
    setStatus.Invoke(player, new object[] { status1, status2, status3 });

void VerifyLimits(bool opened, byte status1, byte status2,
    int expectedGoldMax, int expectedStorage, string scenario)
{
    M2Share.g_Config.boAuthOpen = opened;
    SetStatus(status1, status2, 0);
    applyLimits.Invoke(player, null);
    Equal(expectedGoldMax, player.m_nGoldMax, scenario + " gold");
    Equal(expectedStorage, player.m_nStorageSpaceCount, scenario + " storage");
}

void VerifyStorageRestore(int baseline, int persisted, int expected)
{
    var restoredPlayer = new TPlayObject
    {
        m_nStorageSpaceCount = baseline
    };
    var record = new THumDataInfo();
    record.Header.dCreateDate = new DateTime(2020, 1, 1).ToOADate();
    record.Data.sCharName = "StorageRestoreAudit";
    record.Data.sCurMap = "0";
    record.Data.StorageSpaceCount = persisted;

    try
    {
        getHumData.Invoke(engine, new object[] { restoredPlayer, record });
    }
    catch (TargetInvocationException ex) when (ex.InnerException != null)
    {
        throw ex.InnerException;
    }

    Equal(expected, restoredPlayer.m_nStorageSpaceCount,
        $"storage restore baseline={baseline} persisted={persisted}");

    var savedRecord = new THumDataInfo();
    restoredPlayer.MakeSaveRcd(ref savedRecord);
    Equal(expected, savedRecord.Data.StorageSpaceCount,
        $"storage save baseline={baseline} persisted={persisted}");
}

void Verify(bool expected, int level, int order, string scenario,
    byte? status1 = null, byte? status2 = null, byte? status3 = null)
{
    if (status1.HasValue)
        SetStatus(status1.Value, status2 ?? 0, status3 ?? 0);
    Assert(bridge.CallPlayerFunc("CheckAuthen",
        new List<PasValue> { PasValue.FromInt(level), PasValue.FromInt(order) },
        out var result), scenario + " dispatch");
    Equal(expected, result.AsBool(), scenario);
}

void VerifyStatusMessage(byte status1, int raceServer, int expectedRecog,
    string scenario)
{
    SetStatus(status1, 0, 0);
    player.m_btRaceServer = (byte)raceServer;
    var message = (SystemModule.ClientPacket)buildStatusMessage.Invoke(player, null);
    Assert(message != null, scenario + " message missing");
    Equal(Grobal2.SM_PLAYER_AUTHEN, (int)message.Ident, scenario + " ident");
    Equal(expectedRecog, message.Recog, scenario + " recog");
    Equal((ushort)0, message.Param, scenario + " param");
    Equal((ushort)0, message.Tag, scenario + " tag");
    Equal((ushort)0, message.Series, scenario + " series");
}

static void VerifySourceContract()
{
    var root = FindRepositoryRoot();
    var playerSource = File.ReadAllText(Path.Combine(
        root, "GameSvr", "Players", "TPlayObject.cs"));
    var managerSource = File.ReadAllText(Path.Combine(
        root, "GameSvr", "Services", "NativeAuthenticationManager.cs"));
    var loginSource = File.ReadAllText(Path.Combine(
        root, "GameSvr", "Players", "TPlayObject.Base.cs"));
    var bridgeSource = File.ReadAllText(Path.Combine(
        root, "GameSvr", "ScriptSystem", "PasEngine", "PasApiBridge.cs"));

    var checkStart = playerSource.IndexOf(
        "internal bool CheckNativeAuthentication", StringComparison.Ordinal);
    var checkEnd = playerSource.IndexOf("internal void ApplyQuestInfo",
        checkStart, StringComparison.Ordinal);
    Assert(checkStart >= 0 && checkEnd > checkStart,
        "native authentication check body missing");
    var checkBody = playerSource.Substring(checkStart, checkEnd - checkStart);
    Assert(!checkBody.Contains("boAuthOpen", StringComparison.Ordinal),
        "CheckAuthen incorrectly depends on boAuthOpen");
    Require(managerSource, "a.PlayerId = i.UserId",
        "PlayerId must map to user_index.UserId");
    Require(managerSource, "h.PTID = i.PTID and h.HelpOther = 1",
        "third status byte must come from PTID HelpOther count");
    Require(managerSource, "ReadInt64(reader, 2) > 0 ? (byte)1 : (byte)0",
        "HelpOther count must normalize to a boolean byte");
    Require(managerSource, "Coalesce(a.Status1, 0)",
        "Status1 login load missing");
    Require(managerSource, "Coalesce(a.Status2, 0)",
        "Status2 login load missing");
    Require(loginSource, "AuthenticationManager?.TryLoad(this);",
        "login authentication load missing");
    Require(managerSource, "player.ApplyNativeAuthenticationLimits();",
        "login authentication limits are not applied");
    Require(managerSource,
        "player.m_nStorageSpaceCount = ResolveLoadedStorageCapacity(",
        "persisted storage capacity is not restored after authentication");
    Require(managerSource,
        "loadedStorageCapacity, player.m_nStorageSpaceCount);",
        "persisted storage capacity arguments are not preserved");
    var clearAt = managerSource.IndexOf(
        "player.SetNativeAuthenticationStatus(0, 0, 0);",
        StringComparison.Ordinal);
    var initialApplyAt = managerSource.IndexOf(
        "player.ApplyNativeAuthenticationLimits();", clearAt,
        StringComparison.Ordinal);
    var schemaAt = managerSource.IndexOf("EnsureSchema();", initialApplyAt,
        StringComparison.Ordinal);
    Assert(clearAt >= 0 && initialApplyAt > clearAt && schemaAt > initialApplyAt,
        "zero authentication limits must apply before a query can fail or return no row");
    var finallyAt = managerSource.IndexOf("finally", initialApplyAt,
        StringComparison.Ordinal);
    var restoreStorageAt = managerSource.IndexOf(
        "ResolveLoadedStorageCapacity(", finallyAt, StringComparison.Ordinal);
    Assert(finallyAt > initialApplyAt && restoreStorageAt > finallyAt,
        "stored capacity >48 must override authentication on every query exit");
    var loadAt = loginSource.IndexOf("AuthenticationManager?.TryLoad(this);",
        StringComparison.Ordinal);
    var tryModeAt = loginSource.IndexOf("if (m_nPayMent == 1)",
        StringComparison.Ordinal);
    var onLoginAt = loginSource.IndexOf(
        "TryCallScriptLabel(\"onLogin\"", StringComparison.Ordinal);
    Assert(loadAt >= 0 && tryModeAt > loadAt && onLoginAt > tryModeAt,
        "authentication must load before try mode and onLogin");
    var statusSendAt = loginSource.IndexOf(
        "SendNativeAuthenticationStatus();", StringComparison.Ordinal);
    var honorLoadAt = loginSource.IndexOf(
        "M2Share.HonorValueManager?.TryLoad(this);", StringComparison.Ordinal);
    Assert(statusSendAt > tryModeAt && honorLoadAt > statusSendAt &&
        onLoginAt > honorLoadAt,
        "authentication status must send after authentication/try-mode handling and immediately before HonorValue load");
    var statusCallEnd = statusSendAt +
        "SendNativeAuthenticationStatus();".Length;
    var authToHonor = loginSource.Substring(statusCallEnd,
        honorLoadAt - statusCallEnd);
    Assert(string.IsNullOrWhiteSpace(authToHonor),
        "authentication status must be immediately before HonorValue load");
    Equal(1, CountOccurrences(loginSource,
        "SendNativeAuthenticationStatus();"),
        "login must send authentication status exactly once");
    Require(playerSource, "m_btRaceServer == Grobal2.RC_PLAYOBJECT &&",
        "authentication check must gate non-player races");
    Require(playerSource, "CheckNativeAuthentication(1, 100) ? 0 : -1",
        "authentication status Recog mapping mismatch");
    Require(playerSource,
        "SendDefMessage((short)message.Ident, message.Recog,",
        "authentication status must use the default message sender");
    Require(playerSource,
        "message.Param, message.Tag, message.Series, string.Empty);",
        "authentication status must not append a message body");
    var activeAt = bridgeSource.LastIndexOf("case \"activeauthen\":",
        StringComparison.OrdinalIgnoreCase);
    var activeEnd = bridgeSource.IndexOf("case \"checkauthen\":",
        activeAt, StringComparison.OrdinalIgnoreCase);
    Assert(activeAt >= 0 && activeEnd > activeAt,
        "ActiveAuthen function dispatch missing");
    var activeBody = bridgeSource.Substring(activeAt, activeEnd - activeAt);
    Require(activeBody, "args.Count != 2",
        "ActiveAuthen must require exactly two arguments");
    Require(activeBody, "args[0].AsInt() != 1",
        "ActiveAuthen must reject non-native levels");
    Require(activeBody, "args[1].AsInt() != 100",
        "ActiveAuthen must reject non-aggregate orders");
    Require(activeBody, "return false;",
        "ActiveAuthen unsupported tuples must remain fail-closed");
    Require(activeBody, "PasValue.FromInt(",
        "ActiveAuthen must return an integer result");
    Require(activeBody, "CurrentPlayer.ActiveNativeAuthentication100()",
        "ActiveAuthen must return the native integer result");

    var helpMethodAt = bridgeSource.IndexOf("case \"helpotherauthen\":",
        StringComparison.OrdinalIgnoreCase);
    var authByHelpedAt = bridgeSource.IndexOf("case \"authbyhelped\":",
        helpMethodAt, StringComparison.OrdinalIgnoreCase);
    Assert(helpMethodAt >= 0 && authByHelpedAt > helpMethodAt,
        "HelpOtherAuthen procedure dispatch missing");
    Require(bridgeSource.Substring(helpMethodAt,
            authByHelpedAt - helpMethodAt),
        "RejectUnsupportedNativeApi()",
        "HelpOtherAuthen procedure form must remain fail-closed");
    var helpFunctionAt = bridgeSource.LastIndexOf(
        "case \"helpotherauthen\":", StringComparison.OrdinalIgnoreCase);
    var checkAuthenAt = bridgeSource.IndexOf("case \"checkauthen\":",
        helpFunctionAt, StringComparison.OrdinalIgnoreCase);
    Assert(helpFunctionAt > helpMethodAt && checkAuthenAt > helpFunctionAt,
        "HelpOtherAuthen function dispatch missing");
    var helpFunctionBody = bridgeSource.Substring(helpFunctionAt,
        checkAuthenAt - helpFunctionAt);
    Require(helpFunctionBody, "args.Count != 0",
        "HelpOtherAuthen must require exactly zero arguments");
    Require(helpFunctionBody, "RejectUnsupportedNativeApi(out result)",
        "HelpOtherAuthen arguments must remain fail-closed");
    Require(helpFunctionBody, "PasValue.FromInt(",
        "HelpOtherAuthen must return an integer result");
    Require(helpFunctionBody,
        "CurrentPlayer.HelpOtherNativeAuthentication()",
        "HelpOtherAuthen must use the native synchronous path");
}

static int CountOccurrences(string source, string value)
{
    var count = 0;
    for (var index = 0; (index = source.IndexOf(value, index,
             StringComparison.Ordinal)) >= 0; index += value.Length)
        count++;
    return count;
}

static void Require(string source, string value, string message) =>
    Assert(source.Contains(value, StringComparison.OrdinalIgnoreCase), message);

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
    throw new DirectoryNotFoundException("GameSvr repository root not found");
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
        throw new InvalidOperationException(
            $"{message}: expected={expected} actual={actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
