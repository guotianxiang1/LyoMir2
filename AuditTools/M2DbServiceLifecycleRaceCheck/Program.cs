using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using DBSvr.Core;
using GameSvr;
using GameSvr.Services;
using SystemModule;
using SystemModule.Packet;
using SystemModule.Sockets;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "!Setup.txt"),
    "[Server]" + Environment.NewLine);
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "Command.conf"),
    "[Command]" + Environment.NewLine);
// M2Share's static ctor also builds ExpsConfig from ..\Share\PlayerUpgradeExp.ini
// (M2Share.cs:1690); without it IniFile.Load throws and no assertion runs.
var shareDirectory = Path.Combine(Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "..")), "Share");
Directory.CreateDirectory(shareDirectory);
File.WriteAllText(Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
    "[PlayerLevelExp]" + Environment.NewLine);
File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"),
    "[Integer]" + Environment.NewLine);

try
{
    VerifyErrorEventArgsConstructorCompatibility();
    var failures = new List<Exception>();
    await CaptureFailureAsync("registration-first",
        VerifyRegistrationPrecedesReadyTrafficAsync, failures);
    await CaptureFailureAsync("dispose-terminal",
        VerifyDisposeIsTerminalAsync, failures);
    await CaptureFailureAsync("stale-disconnect",
        VerifyDelayedDisconnectCannotClobberReplacementAsync, failures);
    if (failures.Count != 0) throw new AggregateException(failures);
    Console.WriteLine(
        "PASS m2-dbservice-lifecycle registration=003D-first " +
        "error-args=legacy+socket-compatible " +
        "dispose=terminal error/disconnect/read=socket-scoped " +
        "stop-read=rejected parser=replacement-tail-preserved");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine("M2DbServiceLifecycleRaceCheck FAIL: " + exception);
    return 1;
}

static async Task CaptureFailureAsync(string name, Func<Task> check,
    List<Exception> failures)
{
    Trace("start " + name);
    try
    {
        await Task.Run(check).WaitAsync(TimeSpan.FromSeconds(20));
        Trace("pass " + name);
    }
    catch (Exception exception)
    {
        Trace("fail " + name + ": " + exception.GetType().Name);
        failures.Add(exception);
    }
}

static void VerifyErrorEventArgsConstructorCompatibility()
{
    var exception = new SocketException((int)SocketError.ConnectionReset);
    var legacy = new DSCClientErrorEventArgs("127.0.0.1", 6000,
        (int)SocketError.ConnectionReset, exception);
    Assert(legacy.socket == null
           && legacy.RemoteAddress == "127.0.0.1"
           && legacy.RemotePort == 6000
           && legacy.ErrorCode == SocketError.ConnectionReset
           && ReferenceEquals(legacy.exception, exception),
        "legacy DSCClientErrorEventArgs constructor changed");

    using var socket = new Socket(AddressFamily.InterNetwork,
        SocketType.Stream, ProtocolType.Tcp);
    var scoped = new DSCClientErrorEventArgs(socket, "127.0.0.1", 6001,
        (int)SocketError.TimedOut, exception);
    Assert(ReferenceEquals(scoped.socket, socket)
           && scoped.RemotePort == 6001
           && scoped.ErrorCode == SocketError.TimedOut,
        "socket-scoped DSCClientErrorEventArgs constructor lost identity");
}

static async Task VerifyRegistrationPrecedesReadyTrafficAsync()
{
    Trace("registration listener");
    using var listener = StartListener(out var port);
    ConfigureM2(port);
    using var service = new DBService();
    var pendingPayload = new byte[12];
    BinaryPrimitives.WriteUInt16LittleEndian(pendingPayload, 0x7F01);
    var pendingWire = Encode(new LegacyDbServerFrame(1, 0, pendingPayload));
    Assert(service.SendNativeFrame(pendingWire)
           && service.PendingNativeSendCount == 1,
        "pre-connect traffic was not retained in FIFO");

    service.Start();
    using var peer = await listener.AcceptTcpClientAsync()
        .WaitAsync(TimeSpan.FromSeconds(3));
    Trace("registration peer-accepted");
    var stream = peer.GetStream();
    var registration = await ReadFrameAsync(stream);
    Trace("registration frame-read");
    Assert(registration.Type == 2
           && ReadCommand(registration) == 0x003D
           && BinaryPrimitives.ReadInt32LittleEndian(
               registration.Payload.AsSpan(4, 4)) == 0
           && BinaryPrimitives.ReadInt32LittleEndian(
               registration.Payload.AsSpan(8, 4)) == 2,
        "first DB frame was not native 0x003D registration");
    await WaitUntilAsync(() => service.Connected,
        "DB connection was not marked ready after registration send");

    var pending = await ReadFrameAsync(stream);
    Assert(pending.Type == 1 && ReadCommand(pending) == 0x7F01,
        "queued traffic did not follow registration");
    Assert(service.PendingNativeSendCount == 0,
        "queued traffic was not drained after registration");
    Trace("registration complete");
}

static async Task VerifyDisposeIsTerminalAsync()
{
    Trace("dispose listener");
    using var listener = StartListener(out var port);
    ConfigureM2(port);
    var service = new DBService();
    var client = GetClientSocket(service);
    var ranking = GetPrivateField(service, "_secondaryRankings")
        as NativeType2SecondaryRankingState;
    Assert(ranking != null, "missing secondary ranking state");
    var beforeCount = ranking.TotalRecordCount;
    using var rejectedSocket = new Socket(AddressFamily.InterNetwork,
        SocketType.Stream, ProtocolType.Tcp);
    service.Dispose();

    try
    {
        InvokeDbSocketRead(service, client, rejectedSocket,
            CreateRankingRecord());
        service.Start();
        service.CheckConnected();
        service.Pulse();
        Assert(ranking.TotalRecordCount == beforeCount,
            "Dispose allowed a native read to reach routing");

        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(750));
        try
        {
            using var unexpected = await listener.AcceptTcpClientAsync(timeout.Token);
            throw new InvalidOperationException(
                "DBService established a connection after Dispose");
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
        }
    }
    finally
    {
        client.Disconnect();
    }
}

static async Task VerifyDelayedDisconnectCannotClobberReplacementAsync()
{
    Trace("stale listener");
    using var listener = StartListener(out var port);
    ConfigureM2(port);
    using var service = new DBService();
    var client = GetClientSocket(service);
    var firstConnectEntered = NewSignal<Socket>();
    var secondConnectEntered = NewSignal<Socket>();
    var staleDisconnectDelivered = NewSignal<Socket>();
    using var releaseFirstConnect = new ManualResetEventSlim(false);
    var connectCount = 0;

    void Connected(object _, DSCClientConnectedEventArgs args)
    {
        var count = Interlocked.Increment(ref connectCount);
        if (count == 1)
        {
            firstConnectEntered.TrySetResult(args.socket);
            releaseFirstConnect.Wait(TimeSpan.FromSeconds(5));
            return;
        }
        if (count == 2) secondConnectEntered.TrySetResult(args.socket);
    }

    void Disconnected(object _, DSCClientConnectedEventArgs args) =>
        staleDisconnectDelivered.TrySetResult(args.socket);

    client.OnConnected += Connected;
    client.OnDisconnected += Disconnected;
    TcpClient firstPeer = null;
    TcpClient secondPeer = null;
    try
    {
        service.Start();
        firstPeer = await listener.AcceptTcpClientAsync()
            .WaitAsync(TimeSpan.FromSeconds(3));
        Trace("stale first-peer");
        var firstSocket = await firstConnectEntered.Task
            .WaitAsync(TimeSpan.FromSeconds(3));
        Assert(firstSocket != null && client.IsCurrentConnection(firstSocket),
            "first DB socket was not current");

        client.Disconnect(firstSocket);
        Assert(!client.IsCurrentConnection(firstSocket),
            "first DB socket was not detached before delayed callback");
        SetPrivateLong(service, "_nextReconnectAt", 0);
        service.CheckConnected();

        secondPeer = await listener.AcceptTcpClientAsync()
            .WaitAsync(TimeSpan.FromSeconds(3));
        Trace("stale second-peer");
        var secondSocket = await secondConnectEntered.Task
            .WaitAsync(TimeSpan.FromSeconds(3));
        Assert(secondSocket != null && client.IsCurrentConnection(secondSocket)
               && service.Connected,
            "replacement DB socket was not current and connected");

        CompleteStaticInitialization(service, client, secondSocket);
        var ranking = GetPrivateField(service, "_secondaryRankings")
            as NativeType2SecondaryRankingState;
        Assert(ranking != null, "missing secondary ranking state");
        var beforeCount = ranking.TotalRecordCount;
        var generationBefore = GetPrivateInt(service, "_connectionGeneration");
        var rankingWire = CreateRankingRecord();

        const long reconnectSentinel = 0x123456789;
        SetPrivateLong(service, "_nextReconnectAt", reconnectSentinel);
        InvokeDbSocketError(service, client,
            new DSCClientErrorEventArgs(firstSocket,
                IPAddress.Loopback.ToString(), 0,
                (int)SocketError.ConnectionReset,
                new SocketException((int)SocketError.ConnectionReset)));
        Assert(GetPrivateLong(service, "_nextReconnectAt")
               == reconnectSentinel
               && service.Connected
               && GetPrivateInt(service, "_connectionGeneration")
               == generationBefore,
            "stale first-socket error changed replacement state");

        InvokeDbSocketRead(service, client, firstSocket, rankingWire);
        service.Pulse();
        Assert(ranking.TotalRecordCount == beforeCount,
            "stale first-socket read reached replacement routing");

        var split = rankingWire.Length - 3;
        InvokeDbSocketRead(service, client, secondSocket,
            rankingWire.AsSpan(0, split).ToArray());

        releaseFirstConnect.Set();
        Trace("stale first-connect-released");
        var disconnectedSocket = await staleDisconnectDelivered.Task
            .WaitAsync(TimeSpan.FromSeconds(3));
        Assert(ReferenceEquals(disconnectedSocket, firstSocket),
            "delayed callback did not identify the first socket");
        Assert(service.Connected && client.IsCurrentConnection(secondSocket),
            "delayed first disconnect clobbered replacement connection state");
        Assert(GetPrivateInt(service, "_connectionGeneration") == generationBefore,
            "delayed first disconnect advanced replacement generation");

        InvokeDbSocketRead(service, client, secondSocket,
            rankingWire.AsSpan(split).ToArray());
        service.Pulse();
        Assert(ranking.TotalRecordCount == beforeCount + 1,
            "delayed first disconnect reset replacement parser tail");

        var countBeforeStopRead = ranking.TotalRecordCount;
        service.Stop();
        InvokeDbSocketRead(service, client, secondSocket, rankingWire);
        service.Pulse();
        Assert(!service.Connected
               && ranking.TotalRecordCount == countBeforeStopRead,
            "Stop allowed a native read to reach routing");
    }
    finally
    {
        releaseFirstConnect.Set();
        client.OnConnected -= Connected;
        client.OnDisconnected -= Disconnected;
        firstPeer?.Dispose();
        secondPeer?.Dispose();
    }
}

static TcpListener StartListener(out int port)
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    port = ((IPEndPoint)listener.LocalEndpoint).Port;
    return listener;
}

static void ConfigureM2(int port)
{
    M2Share.nServerIndex = 1;
    M2Share.g_Config = new GameSvrConfig
    {
        sDBAddr = IPAddress.Loopback.ToString(),
        nDBPort = port
    };
    M2Share.LogSystem = new MirLog();
    M2Share.UserEngine = null;
}

static void CompleteStaticInitialization(DBService service,
    IClientScoket client, Socket socket)
{
    var payload = new byte[NativeType2FieldHeroSnapshotState.HeaderSize];
    BinaryPrimitives.WriteUInt16LittleEndian(payload,
        NativeType2FieldHeroSnapshotState.Command);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), 1);
    InvokeDbSocketRead(service, client, socket, Encode(
        new LegacyDbServerFrame(2, 0, payload)));
    Assert(service.StaticInitializationCompleted,
        "test setup did not complete static Type2 initialization");
}

static byte[] CreateRankingRecord()
{
    var payload = new byte[NativeType2SecondaryRankingState.HeaderSize + 7];
    BinaryPrimitives.WriteUInt16LittleEndian(payload,
        NativeType2SecondaryRankingState.RecordCommand);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), 1);
    payload.AsSpan(NativeType2SecondaryRankingState.HeaderSize).Fill(0x5A);
    return Encode(new LegacyDbServerFrame(2, 0, payload));
}

static byte[] Encode(LegacyDbServerFrame frame)
{
    Assert(LegacyDbServerFrameCodec.TryEncode(frame, out var wire, out var error),
        "frame encode failed: " + error);
    return wire;
}

static ushort ReadCommand(LegacyDbServerFrame frame)
{
    Assert(frame.Payload != null && frame.Payload.Length >= 2,
        "native frame has no command");
    return BinaryPrimitives.ReadUInt16LittleEndian(frame.Payload);
}

static async Task<LegacyDbServerFrame> ReadFrameAsync(NetworkStream stream)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    var header = new byte[LegacyDbServerFrameCodec.HeaderSize];
    await ReadExactlyAsync(stream, header, timeout.Token);
    var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(
        header.AsSpan(8, 4));
    Assert(payloadLength >= 0
           && payloadLength <= 0x1FFFF - header.Length,
        "received invalid native payload length");
    var wire = new byte[header.Length + payloadLength];
    header.CopyTo(wire, 0);
    if (payloadLength != 0)
        await ReadExactlyAsync(stream,
            wire.AsMemory(header.Length, payloadLength), timeout.Token);
    Assert(LegacyDbServerFrameCodec.TryDecode(wire,
            out var frame, out var error),
        "received invalid native frame: " + error);
    return frame;
}

static async Task ReadExactlyAsync(Stream stream, Memory<byte> target,
    CancellationToken cancellationToken)
{
    var offset = 0;
    while (offset < target.Length)
    {
        var read = await stream.ReadAsync(target[offset..],
            cancellationToken);
        if (read == 0) throw new EndOfStreamException("native stream closed");
        offset += read;
    }
}

static async Task WaitUntilAsync(Func<bool> condition, string message)
{
    var deadline = Environment.TickCount64 + 3_000;
    while (!condition())
    {
        if (Environment.TickCount64 >= deadline)
            throw new TimeoutException(message);
        await Task.Delay(10);
    }
}

static IClientScoket GetClientSocket(DBService service) =>
    GetPrivateField(service, "_clientScoket") as IClientScoket
    ?? throw new InvalidOperationException("invalid DBService client socket");

static object GetPrivateField(object instance, string fieldName)
{
    var field = instance.GetType().GetField(fieldName,
        BindingFlags.Instance | BindingFlags.NonPublic);
    return field?.GetValue(instance)
        ?? throw new MissingFieldException(instance.GetType().Name, fieldName);
}

static int GetPrivateInt(object instance, string fieldName) =>
    (int)GetPrivateField(instance, fieldName);

static long GetPrivateLong(object instance, string fieldName) =>
    (long)GetPrivateField(instance, fieldName);

static void SetPrivateLong(object instance, string fieldName, long value)
{
    var field = instance.GetType().GetField(fieldName,
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(instance.GetType().Name, fieldName);
    field.SetValue(instance, value);
}

static void InvokeDbSocketRead(DBService service, IClientScoket sender,
    Socket socket, byte[] datagram)
{
    var method = typeof(DBService).GetMethod("DBSocketRead",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(nameof(DBService), "DBSocketRead");
    method.Invoke(service,
        new object[] { sender, new DSCClientDataInEventArgs(socket, datagram) });
}

static void InvokeDbSocketError(DBService service, IClientScoket sender,
    DSCClientErrorEventArgs error)
{
    var method = typeof(DBService).GetMethod("DBSocketError",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(nameof(DBService), "DBSocketError");
    method.Invoke(service, new object[] { sender, error });
}

static TaskCompletionSource<T> NewSignal<T>() => new(
    TaskCreationOptions.RunContinuationsAsynchronously);

static void Trace(string stage)
{
    if (string.Equals(Environment.GetEnvironmentVariable("M2_LIFECYCLE_TRACE"),
            "1", StringComparison.Ordinal))
        Console.Error.WriteLine("[M2DbServiceLifecycleRaceCheck] " + stage);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
