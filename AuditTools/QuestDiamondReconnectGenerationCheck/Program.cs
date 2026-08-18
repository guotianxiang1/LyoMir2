using System.Buffers.Binary;
using System.Text;
using GameSvr;
using SystemModule.Packet;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

TestCompleteFrameSurvivesReconnectAndAcksCurrentConnection();
TestDisconnectedDrainDoesNotRetryAck();
TestPartialFrameSurvivesReconnect();
TestStaleSocketCallbacksAreRejectedOnlyAtIngress();
TestFifoHasNoGenerationOrDuplicateFilter();
TestProductionRemainsDormant();

Console.WriteLine(
    "PASS QuestDiamondReconnectGeneration 1122 parsed-frame=process-lifetime " +
    "partial-tail=retained stale-socket=ingress-rejected " +
    "ACK=current-connection disconnected=no-retry FIFO=no-dedupe runtime=dormant");
return;

static void TestCompleteFrameSurvivesReconnectAndAcksCurrentConnection()
{
    var owner = new NativeQuestDiamondYbDbReconnectOwner();
    var firstGeneration = owner.BeginConnection();
    Assert(owner.Append(firstGeneration, Encode(Frame(7001, 1, 1))),
        "first connection rejected its completion");
    Equal(1, owner.PendingFrameCount, "queued completion count");
    Assert(owner.EndConnection(firstGeneration),
        "current disconnect was rejected");
    Equal(1, owner.PendingFrameCount,
        "disconnect cleared a complete native frame");

    var secondGeneration = owner.BeginConnection();
    Assert(secondGeneration != firstGeneration,
        "reconnect reused its generation token");
    Assert(owner.TryDequeue(out var completion),
        "reconnect lost the old complete frame");

    var host = new FakeHost(owner);
    var disposition = NativeQuestDiamondCompletionStateMachine.Process(
        completion, host);
    Equal(NativeQuestDiamondCompletionDisposition.PositiveSuccessAck,
        disposition, "reconnected completion disposition");
    Equal(2, host.Target.DiamondCache,
        "old completion did not mutate the current role");
    Equal(1, host.AckAttempts, "ACK attempt count");
    Equal(secondGeneration, host.AckGenerations.Single().Value,
        "old completion ACK did not use the current connection");
    Equal((ushort)105, host.LastAck.Ident, "reconnected ACK ident");
}

static void TestDisconnectedDrainDoesNotRetryAck()
{
    var owner = new NativeQuestDiamondYbDbReconnectOwner();
    var generation = owner.BeginConnection();
    Assert(owner.Append(generation, Encode(Frame(7002, 2, 1))),
        "completion append failed");
    Assert(owner.EndConnection(generation), "disconnect failed");
    Assert(owner.TryDequeue(out var completion),
        "disconnected drain lost completion");

    var host = new FakeHost(owner);
    var disposition = NativeQuestDiamondCompletionStateMachine.Process(
        completion, host);
    Equal(NativeQuestDiamondCompletionDisposition.PositiveSuccessAck,
        disposition, "disconnected completion disposition");
    Equal(3, host.Target.DiamondCache,
        "disconnected completion did not perform local grant");
    Equal(1, host.AckAttempts, "disconnected ACK attempt count");
    Assert(host.AckGenerations.Single() == null,
        "disconnected ACK unexpectedly captured a connection");

    _ = owner.BeginConnection();
    Equal(0, owner.PendingFrameCount,
        "consumed completion was replayed after reconnect");
    Equal(1, host.AckAttempts, "failed ACK was retried after reconnect");
}

static void TestPartialFrameSurvivesReconnect()
{
    var owner = new NativeQuestDiamondYbDbReconnectOwner();
    var encoded = Encode(Frame(7003, 3, 1));
    const int split = 19;

    var firstGeneration = owner.BeginConnection();
    Assert(owner.Append(firstGeneration, encoded.AsSpan(0, split)),
        "partial prefix append failed");
    Equal(split, owner.BufferedLength, "partial prefix length");
    Equal(0, owner.PendingFrameCount,
        "partial prefix produced a complete frame");

    Assert(owner.EndConnection(firstGeneration),
        "partial-frame disconnect failed");
    Equal(split, owner.BufferedLength,
        "disconnect cleared the native parser tail");

    var secondGeneration = owner.BeginConnection();
    Equal(split, owner.BufferedLength,
        "reconnect cleared the native parser tail");
    Assert(owner.Append(secondGeneration, encoded.AsSpan(split)),
        "reconnected suffix append failed");
    Equal(0, owner.BufferedLength, "completed parser tail length");
    Equal(1, owner.PendingFrameCount,
        "cross-reconnect frame was not completed");
    Assert(owner.TryDequeue(out var decoded),
        "cross-reconnect frame was not dequeued");
    Equal(7003, decoded.QueryId, "cross-reconnect result code");
    Equal((ushort)1122, decoded.Ident, "cross-reconnect ident");
}

static void TestStaleSocketCallbacksAreRejectedOnlyAtIngress()
{
    var owner = new NativeQuestDiamondYbDbReconnectOwner();
    var firstGeneration = owner.BeginConnection();
    Assert(owner.EndConnection(firstGeneration), "first disconnect failed");
    var secondGeneration = owner.BeginConnection();

    Assert(!owner.Append(firstGeneration, Encode(Frame(7004, 1, 1))),
        "stale socket bytes entered the current parser");
    Assert(!owner.EndConnection(firstGeneration),
        "late old disconnect closed the replacement connection");
    Assert(owner.Connected, "replacement connection was closed");
    Equal(secondGeneration, owner.CurrentGeneration,
        "replacement generation changed after stale callbacks");
    Equal(0, owner.PendingFrameCount,
        "stale socket callback queued a frame");

    Assert(owner.Append(secondGeneration, Encode(Frame(7005, 1, 1))),
        "current socket bytes were rejected");
    Equal(1, owner.PendingFrameCount,
        "current socket frame was not queued");
}

static void TestFifoHasNoGenerationOrDuplicateFilter()
{
    var owner = new NativeQuestDiamondYbDbReconnectOwner();
    var generation = owner.BeginConnection();
    var first = Encode(Frame(7006, 1, 1));
    var second = Encode(Frame(7006, 1, 1));
    var batch = new byte[first.Length + second.Length];
    first.CopyTo(batch, 0);
    second.CopyTo(batch, first.Length);

    Assert(owner.Append(generation, batch), "coalesced append failed");
    Equal(2, owner.PendingFrameCount,
        "duplicate completion was filtered");
    Assert(owner.EndConnection(generation), "FIFO disconnect failed");
    _ = owner.BeginConnection();

    Assert(owner.TryDequeue(out var firstFrame), "first FIFO frame missing");
    Assert(owner.TryDequeue(out var secondFrame), "second FIFO frame missing");
    Equal(firstFrame.QueryId, secondFrame.QueryId,
        "duplicate FIFO result mismatch");
    Equal(0, owner.PendingFrameCount, "FIFO did not drain exactly twice");
}

static void TestProductionRemainsDormant()
{
    var root = FindRepositoryRoot();
    var owner = File.ReadAllText(Path.Combine(root, "GameSvr", "Services",
        "NativeQuestDiamondYbDbReconnectOwner.cs"));
    var stateMachine = File.ReadAllText(Path.Combine(root, "GameSvr", "Services",
        "NativeQuestDiamondCompletionStateMachine.cs"));
    var client = File.ReadAllText(Path.Combine(root, "GameSvr", "Services",
        "YbDbClient.cs"));
    var bridge = File.ReadAllText(Path.Combine(root, "GameSvr", "ScriptSystem",
        "PasEngine", "PasApiBridge.cs"));
    var dbService = File.ReadAllText(Path.Combine(root, "GameSvr", "Services",
        "DBService.cs"));

    Require(owner, "generation != _currentGeneration",
        "owner lost stale ingress rejection");
    Reject(owner, "_frames.Clear",
        "owner clears complete frames during reconnect");
    Reject(owner, "_parser.Reset",
        "owner clears the native partial tail during reconnect");
    Reject(owner, "PendingCredit", "owner gained request correlation");
    Reject(owner, "ObjectId", "owner gained player correlation");

    Reject(client, "NativeQuestDiamondYbDbReconnectOwner",
        "dormant owner was wired into YbDbClient");
    Reject(bridge, "NativeQuestDiamondYbDbReconnectOwner",
        "dormant owner was wired into PAS");
    Reject(stateMachine, "NativeQuestDiamondYbDbReconnectOwner",
        "transaction state machine gained transport ownership");

    Require(dbService, "new ReceivedNativeFrame(frame)",
        "DBService no longer drops connection provenance after parse");
    var receivedFrame = Slice(dbService,
        "private readonly struct ReceivedNativeFrame",
        "public void Dispose()");
    Reject(receivedFrame, "Generation",
        "DBService parsed-frame owner gained generation correlation");

    var completions = Slice(client, "public void ProcessCompletions()",
        "private void SocketConnected(");
    Require(completions,
        "if (!IsCurrentSessionLocked(_currentSocket, queued.Generation))",
        "generic YbDbClient generation conflict unexpectedly disappeared");
}

static YbDbLegacy77Frame Frame(int result, int first, int second)
{
    var payload = new byte[32];
    var role = Encoding.GetEncoding(936).GetBytes("测试角色");
    payload[0] = (byte)role.Length;
    role.CopyTo(payload, 1);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(16, 4), first);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(20, 4), second);
    return new YbDbLegacy77Frame(result, 0, 1122, payload);
}

static byte[] Encode(YbDbLegacy77Frame frame)
{
    Assert(YbDbLegacy77Codec.TryEncode(frame, out var data, out var error),
        "frame encode failed: " + error);
    return data;
}

static string FindRepositoryRoot()
    => AuditRepoRoot.Resolve();

static string Slice(string source, string start, string end)
{
    var startIndex = source.IndexOf(start, StringComparison.Ordinal);
    Assert(startIndex >= 0, "slice start missing: " + start);
    var endIndex = source.IndexOf(end, startIndex + start.Length,
        StringComparison.Ordinal);
    Assert(endIndex > startIndex, "slice end missing: " + end);
    return source[startIndex..endIndex];
}

static void Require(string source, string value, string message)
{
    Assert(source.Contains(value, StringComparison.Ordinal), message);
}

static void Reject(string source, string value, string message)
{
    Assert(!source.Contains(value, StringComparison.Ordinal), message);
}

static void Equal<T>(T expected, T actual, string label)
{
    Assert(EqualityComparer<T>.Default.Equals(expected, actual),
        $"{label}: expected={expected}, actual={actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class FakeHost : INativeQuestDiamondCompletionHost
{
    private readonly NativeQuestDiamondYbDbReconnectOwner _owner;

    public FakeHost(NativeQuestDiamondYbDbReconnectOwner owner)
    {
        _owner = owner;
    }

    public FakeTarget Target { get; } = new();
    public int AckAttempts { get; private set; }
    public List<long?> AckGenerations { get; } = new();
    public YbDbLegacy77Frame LastAck { get; private set; }

    public INativeQuestDiamondCompletionTarget FindCurrentRole(string roleName) =>
        Target;

    public int NextNativeRandom(int range) => 0;

    public bool TrySelectBountyGbk(out byte[] descriptor)
    {
        descriptor = null;
        return false;
    }

    public bool EnqueueAck(YbDbLegacy77Frame frame)
    {
        AckAttempts++;
        LastAck = frame;
        if (_owner.TryCaptureCurrentSendGeneration(out var generation))
        {
            AckGenerations.Add(generation);
            return true;
        }
        AckGenerations.Add(null);
        return false;
    }

    public void ReportGiveException(Exception exception)
    {
        throw new InvalidOperationException("unexpected give exception", exception);
    }
}

sealed class FakeTarget : INativeQuestDiamondCompletionTarget
{
    public ushort Level => 1;
    public bool IsDead => false;
    public bool IsReadyRun => true;
    public bool HasNpc => false;
    public int DiamondCache { get; private set; }

    public void AddDiamondCacheUnchecked(int amount) =>
        DiamondCache = unchecked(DiamondCache + amount);

    public void GrantExperience(int amount, bool shareWithHero,
        bool countAsFightExperience, int experienceMode)
    {
    }

    public bool ExecuteRewardTokenGbk(ReadOnlyMemory<byte> descriptor) => true;
    public void ShowFailureDialog(string text) { }
    public void ShowNpcSuccessDialog(string text) { }
    public void RefreshCapital() { }
    public void WriteGameLog(int type, string itemName, string reason,
        int count, string detail) { }
}
