using SystemModule.Packet;

var root = FindRepositoryRoot();
var tests = new (string Name, Action Run)[]
{
    ("104 exact identity request", CheckExactRequest),
    ("native save route matrix", CheckSaveRouteMatrix),
    ("104/1104 no-ack boundary", () => CheckNoAckBoundary(root)),
    ("runtime remains fail-closed", () => CheckRuntimeFailClosed(root))
};

foreach (var test in tests)
{
    try
    {
        test.Run();
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException($"{test.Name}: {ex.Message}", ex);
    }
}

Console.WriteLine(
    $"YbDbLogoutProtocolCompatCheck PASS tests={tests.Length} " +
    "request=104/Q0/P0/64B/80B response=none runtime=fail-closed");
return;

static void CheckExactRequest()
{
    var identity = new YbDbLegacy77Identity
    {
        Field0 = "ptid-123456789",
        Field11 = "ptid-123456789",
        RoleName = "离线角色",
        Field48 = "192.0.2.104"
    };
    True(YbDbLogoutProtocol.TryCreateRequest(identity,
        out var request, out var error), error);
    Equal(0, request.QueryId, "104 QueryId");
    Equal(0, request.Param, "104 Param");
    Equal((ushort)104, request.Ident, "104 Ident");
    Equal(YbDbLogoutProtocol.PayloadSize, request.Payload.Length,
        "104 payload length");

    True(YbDbLegacy77Codec.TryDecodeIdentity(request.Payload,
        out var decoded, out error), error);
    Equal("ptid-12345", decoded.Field0, "104 narrow PTID");
    Equal("ptid-123456789", decoded.Field11, "104 full PTID");
    Equal("离线角色", decoded.RoleName, "104 GBK role");
    Equal("192.0.2.104", decoded.Field48, "104 IP");

    True(YbDbLegacy77Codec.TryEncode(request,
        out var bytes, out error), error);
    Equal(YbDbLogoutProtocol.FrameSize, bytes.Length, "104 wire length");
    Equal("77BBAA33000000000000000068004000",
        Convert.ToHexString(bytes.AsSpan(0, 16)), "104 wire header");
    False(YbDbLogoutProtocol.TryCreateRequest(null,
        out _, out _), "null 104 identity encoded");
}

static void CheckSaveRouteMatrix()
{
    var periodic = YbDbLogoutProtocol.EvaluateSaveInvocation(
        alreadySaved: false, transferTimeLow: 0, transferTimeHigh: 0,
        saveType: 0);
    AssertEntered(periodic, sendsLogout: false, "periodic saveType 0");

    foreach (var saveType in new[] { 1, 2, 3 })
    {
        var final = YbDbLogoutProtocol.EvaluateSaveInvocation(
            alreadySaved: false, transferTimeLow: 0, transferTimeHigh: 0,
            saveType);
        AssertEntered(final, sendsLogout: true,
            $"final saveType {saveType}");
    }

    var skipped = YbDbLogoutProtocol.EvaluateSaveInvocation(
        alreadySaved: true, transferTimeLow: 0, transferTimeHigh: 0,
        saveType: 3);
    False(skipped.EntersSaveBody, "already-saved route entered");
    False(skipped.ReportsLingFuAccounting,
        "already-saved route reported 132");
    False(skipped.SendsLogoutRequest, "already-saved route sent 104");
    False(skipped.QueuesHumanSave, "already-saved route queued save");

    var transferLow = YbDbLogoutProtocol.EvaluateSaveInvocation(
        alreadySaved: true, transferTimeLow: 1, transferTimeHigh: 0,
        saveType: 0);
    AssertEntered(transferLow, sendsLogout: false,
        "transfer-low saveType 0");
    var transferHigh = YbDbLogoutProtocol.EvaluateSaveInvocation(
        alreadySaved: true, transferTimeLow: 0, transferTimeHigh: 1,
        saveType: 2);
    AssertEntered(transferHigh, sendsLogout: true,
        "transfer-high saveType 2");
}

static void CheckNoAckBoundary(string repositoryRoot)
{
    var route = YbDbLogoutProtocol.EvaluateSaveInvocation(
        alreadySaved: false, transferTimeLow: 0, transferTimeHigh: 0,
        saveType: 3);
    False(route.BlocksSaveOnLogoutFailure,
        "104 failure blocks character save");
    False(route.RegistersPendingRequest, "104 registers pending state");
    False(route.WaitsForAcknowledgement, "104 waits for 1104 ACK");
    False(route.ProducesUiMessage, "104 produces UI output");
    False(route.MutatesAccountOrGameLog,
        "104 mutates account or game log");

    var source = File.ReadAllText(Path.Combine(repositoryRoot, "SystemModule",
        "Packet", "YbDbLogoutProtocol.cs"));
    Reject(source, "1104", "104 codec treats 1104 as an ACK");
    Reject(source, "ResponseIdent", "104 codec invents a response Ident");
}

static void CheckRuntimeFailClosed(string repositoryRoot)
{
    var service = Read(repositoryRoot, "GameSvr", "Services", "YbDbClient.cs");
    Reject(service, "RequestPlayerLogout",
        "104 sender is live without authoritative 6108 closure");
    Reject(service, "YbDbLogoutProtocol",
        "dormant 104 codec is wired into YbDbClient");
    Reject(service, "1104", "YbDbClient consumes independent 1104 as logout ACK");

    var player = Read(repositoryRoot, "GameSvr", "Players",
        "TPlayObject.NativeYbCredit.cs");
    Reject(player, "TryBeginNativeYbLogout", "player carries live 104 state");
    Reject(player, "_nativeYbLogoutAttempted", "player carries live 104 state");

    var userEngine = Read(repositoryRoot, "GameSvr", "UsrSystem", "UsrEngn.cs");
    Reject(userEngine, "RequestPlayerLogout", "character save emits 104");
    Reject(userEngine, "SaveFinalHumanRcd",
        "unsupported final-save 104 route is live");
    Require(userEngine, "RequestLingFuAccounting(PlayObject)",
        "existing 132 accounting was removed with 104");

    var server = Read(repositoryRoot, "GameSvr", "GameServer.cs");
    // Receiver included so an unrelated local helper of the same name cannot
    // satisfy it; the arity is deliberately left open, because which overload
    // carries the native save mode is a separate contract from which routine
    // shutdown runs, and this assertion only guards the latter.
    Require(server, "M2Share.UserEngine.SaveHumanRcd(player",
        "shutdown no longer snapshots players");
    Reject(server, "SaveFinalHumanRcd",
        "shutdown emits unsupported 104");
}

static void AssertEntered(YbDbLogoutProtocol.SaveRouteDecision route,
    bool sendsLogout, string name)
{
    True(route.EntersSaveBody, name + " did not enter save body");
    True(route.ReportsLingFuAccounting, name + " did not report 132");
    Equal(sendsLogout, route.SendsLogoutRequest, name + " 104 decision");
    True(route.QueuesHumanSave, name + " did not queue character save");
}

static string Read(string repositoryRoot, params string[] parts)
{
    return File.ReadAllText(Path.Combine(new[] { repositoryRoot }.Concat(parts)
        .ToArray()));
}

static string FindRepositoryRoot()
{
    foreach (var start in new[]
             {
                 Environment.CurrentDirectory,
                 AppContext.BaseDirectory
             })
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
    throw new DirectoryNotFoundException("LyoMir2 repository root not found");
}
static void Require(string text, string value, string message)
{
    if (!text.Contains(value, StringComparison.Ordinal))
        throw new InvalidOperationException(message);
}

static void Reject(string text, string value, string message)
{
    if (text.Contains(value, StringComparison.Ordinal))
        throw new InvalidOperationException(message);
}

static void True(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
}

static void False(bool value, string message)
{
    if (value) throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{name}: expected={expected}, actual={actual}");
}
