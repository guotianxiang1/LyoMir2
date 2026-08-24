using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using GameGate.Core;
using GameSvr;
using SystemModule;
using SystemModule.Packages;
using SystemModule.Packet;

PrepareRuntimeConfig();
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
M2Share.g_Config = new GameSvrConfig { nCheckBlock = 0 };

try
{
    Require(!SharedBackendHub.IsHeartbeatExpired(1000, 0, pending: true),
        "heartbeat with no send timestamp was treated as expired");
    Require(!SharedBackendHub.IsHeartbeatExpired(60999, 1000, pending: true),
        "heartbeat expired before the native 60-second deadline");
    Require(SharedBackendHub.IsHeartbeatExpired(61000, 1000, pending: true),
        "heartbeat did not expire at the native 60-second deadline");
    Require(!SharedBackendHub.IsHeartbeatExpired(61000, 1000, pending: false),
        "non-pending heartbeat was treated as expired");
    Console.WriteLine("PASS native heartbeat timeout gate");

    VerifyNativeM2BodyLimit();
    Console.WriteLine("PASS native M2 body limit 0x3000");

    await DelayedRegistrationGatesOpenAsync();
    Console.WriteLine("PASS delayed native registration gates OPEN");

    await using var fixture = await GateM2Fixture.CreateAsync();
    await WaitUntilAsync(() => fixture.Hub.RegisteredGateIndex == 1,
        "SharedBackendHub did not complete the automatic native registration");
    var registrationReply = await fixture.M2ToHub.ReadAsync(
        packet => packet.Cmd == NativeGameGateCommands.M2RegistrationReply);
    Equal((uint)1, registrationReply.ConnID,
        "native registration reply gate index");
    Equal((uint)0, registrationReply.SeqID,
        "native registration reply route context");
    Equal(0, registrationReply.Payload.Length,
        "native registration reply payload");
    Console.WriteLine("PASS native gate registration 5 -> 15");

    var reboundRoute = new SharedBackendRoute
    {
        Handle = 7,
        NativeSessionId = 7,
        GateIndex = 1,
        ConnId = 7,
        SessionGeneration = 1,
        ClientIp = "127.0.0.1",
        DbOpenFrame = Array.Empty<byte>(),
        Abort = () => { }
    };
    Require(reboundRoute.TryBindNativeGateIndex(5),
        "route did not accept the assigned native gate index");
    Equal(SharedBackendRoute.ComposeRouteId(5, 7), reboundRoute.RouteId,
        "assigned native gate index route context");
    Require(reboundRoute.BindNativeRouteContext(7,
            SharedBackendRoute.ComposeRouteId(5, 7)),
        "route did not accept native Cmd11 binding");
    Require(!reboundRoute.TryBindNativeGateIndex(6),
        "Cmd15 silently changed an already Cmd11-bound route");
    Console.WriteLine("PASS native gate index rebind guard");

    var abortCount = 0;
    var route = await fixture.Hub.OpenRouteAsync(101, 101, "127.0.0.1", 1,
        () => Interlocked.Increment(ref abortCount), CancellationToken.None)
        .WaitAsync(TimeSpan.FromSeconds(5));
    Require(route != null, "SharedBackendHub did not open the logical route");

    var open = await fixture.HubToM2.ReadAsync(
        packet => packet.ConnID == 101 && packet.Cmd == Grobal2.GM_OPEN);
    Equal("127.0.0.1", HUtil32.GetString(open.Payload, 0, open.Payload.Length),
        "GM_OPEN client IP");
    var serverUserIndex = await route!.GameResponses.Reader.ReadAsync().AsTask()
        .WaitAsync(TimeSpan.FromSeconds(5));
    Equal((ushort)Grobal2.GM_SERVERUSERINDEX, serverUserIndex.Cmd,
        "M2 open response command");
    Equal(1, BitConverter.ToInt32(serverUserIndex.Payload, 0),
        "M2 allocated user index");
    await WaitUntilAsync(() => fixture.GateInfo.nUserCount == 1,
        "M2 gate user count did not publish after OPEN");
    Console.WriteLine("PASS real OPEN -> GM_SERVERUSERINDEX");

    Require(await fixture.Hub.SendGameHeartbeatOnceAsync(CancellationToken.None),
        "SharedBackendHub did not send its heartbeat");
    var heartbeat = await fixture.HubToM2.ReadAsync(
        packet => packet.ConnID == 0
                 && packet.Cmd == NativeGameGateCommands.GateKeepAliveRequest);
    Equal(0, heartbeat.Payload.Length, "native keepalive payload");
    var heartbeatReply = await fixture.M2ToHub.ReadAsync(
        packet => packet.ConnID == 0
                 && packet.Cmd == NativeGameGateCommands.M2KeepAliveReply);
    Equal(0, heartbeatReply.Payload.Length, "native keepalive reply payload");
    await WaitUntilAsync(() => !fixture.Hub.GameHeartbeatPending,
        "SharedBackendHub did not consume native keepalive reply");
    Console.WriteLine("PASS native heartbeat 3 -> 13");

    var m2Heartbeat = InternalPacket77.Ack(0, 0x77,
        NativeGameGateCommands.GateKeepAliveRequest).ToBytes();
    await fixture.SendFromM2Async(m2Heartbeat);
    var gateHeartbeatReply = await fixture.HubToM2.ReadAsync(
        packet => packet.ConnID == 0
                  && packet.SeqID == 0x77
                  && packet.Cmd == NativeGameGateCommands.M2KeepAliveReply);
    Equal(0, gateHeartbeatReply.Payload.Length,
        "M2-initiated heartbeat reply payload");
    Console.WriteLine("PASS M2-initiated heartbeat 3 -> 13");

    Volatile.Write(ref fixture.GateInfo.nSendChecked, 1);
    Volatile.Write(ref fixture.GateInfo.nSendBlockCount, 321);
    fixture.GateService.SendCheck(Grobal2.GM_RECEIVE_OK);
    var flowRequest = await fixture.M2ToHub.ReadAsync(
        packet => packet.ConnID == 0 && packet.Cmd == Grobal2.GM_RECEIVE_OK);
    Equal((ushort)InternalPacket77.HEADER_SIZE, flowRequest.FrameLen,
        "GM_RECEIVE_OK request frame length");
    var flowReply = await fixture.HubToM2.ReadAsync(
        packet => packet.ConnID == 0 && packet.Cmd == Grobal2.GM_RECEIVE_OK);
    Equal(0, flowReply.Payload.Length, "GM_RECEIVE_OK echo payload");
    Equal((ushort)InternalPacket77.HEADER_SIZE, flowReply.FrameLen,
        "GM_RECEIVE_OK reply frame length");
    await WaitUntilAsync(
        () => Volatile.Read(ref fixture.GateInfo.nSendChecked) == 0
              && Volatile.Read(ref fixture.GateInfo.nSendBlockCount) == 0,
        "GateService did not consume the echoed GM_RECEIVE_OK");
    Console.WriteLine("PASS real ConnID=0 GM_RECEIVE_OK echo loop");

    // GateService compares against nCheckBlock*10 bytes, and every frame this fixture
    // queues is HEADER_SIZE+1 = 17 bytes. 4 (=40 bytes) lets two frames through before the
    // probe, so the send/probe/send/probe/send sequence the asserts below describe never
    // happens and the third frame is what stalls. 1 (=10 bytes) is the value that makes each
    // individual frame trip the per-frame gate, which is the scenario under test.
    // NOTE: the threshold arithmetic itself is unanchored — "CheckBlock" is not in 战神's
    // !Setup.txt key table (0x794560..: ServerIndex/Server/ServerName/GMSuperCode/
    // TestServer/DBAddr/DBPort/GCAddr/GCPort/YBDBAddr/LogServerAddr/LogServerPort/BaseDir/
    // Share/GuildDir/...), so nothing here pins the *10.
    M2Share.g_Config.nCheckBlock = 1;
    Require(fixture.GateService.HandleSendBuffer(
        CreateGateBuffer(route.ConnId, 0xA1)), "flow frame A was not accepted");
    Require(fixture.GateService.HandleSendBuffer(
        CreateGateBuffer(route.ConnId, 0xB2)), "flow frame B was not accepted");
    Require(fixture.GateService.HandleSendBuffer(
        CreateGateBuffer(route.ConnId, 0xC3)), "flow frame C was not accepted");

    await fixture.M2ToHub.ReadAsync(packet => IsMarker(packet, route.ConnId, 0xA1));
    var firstProbe = await fixture.M2ToHub.ReadAsync(
        packet => packet.ConnID == 0 && packet.Cmd == Grobal2.GM_RECEIVE_OK);
    Equal((ushort)InternalPacket77.HEADER_SIZE, firstProbe.FrameLen,
        "first flow probe frame length");
    await fixture.M2ToHub.ReadAsync(packet => IsMarker(packet, route.ConnId, 0xB2));
    var secondProbe = await fixture.M2ToHub.ReadAsync(
        packet => packet.ConnID == 0 && packet.Cmd == Grobal2.GM_RECEIVE_OK);
    Equal((ushort)InternalPacket77.HEADER_SIZE, secondProbe.FrameLen,
        "second flow probe frame length");
    await fixture.M2ToHub.ReadAsync(packet => IsMarker(packet, route.ConnId, 0xC3));
    await WaitUntilAsync(
        () => Volatile.Read(ref fixture.GateInfo.nSendChecked) == 0,
        "flow queue did not resume after its second acknowledgement");
    M2Share.g_Config.nCheckBlock = 0;
    Console.WriteLine("PASS M2 flow control preserves queued frame order");

    var focusRecog = unchecked((int)0x6A5B4C3D);
    var focusPayload = new byte[37];
    BitConverter.GetBytes(focusRecog).CopyTo(focusPayload, 0);
    BitConverter.GetBytes((ushort)2222).CopyTo(focusPayload, 4);
    for (var index = ClientPacket.PackSize; index < focusPayload.Length; index++)
        focusPayload[index] = unchecked((byte)(index * 7));
    var focusUpdate = new InternalPacket77
    {
        Magic = InternalPacket77.MAGIC,
        ConnID = 0,
        SeqID = 0,
        Cmd = LegacyGateType24Cache.MessageType,
        Payload = focusPayload
    };
    Require(fixture.GateService.HandleSendBuffer(CreateGateCommandBuffer(
            focusUpdate.ConnID, focusUpdate.Cmd, focusUpdate.Payload)),
        "native type-24 frame was not queued by M2");
    await WaitUntilAsync(
        () => fixture.Hub.TryGetNativeFocusItem(focusRecog, out _),
        "native type-24 frame did not reach the shared GameGate cache");
    Require(fixture.Hub.TryGetNativeFocusItem(focusRecog, out var cachedFocus),
        "native type-24 cache lookup failed after dispatch");
    Require(focusPayload.SequenceEqual(cachedFocus),
        "native type-24 dispatch changed cached client bytes");
    Console.WriteLine("PASS native type 24 -> shared focus-item cache exact bytes");

    // Native M2 -> GameGate DATA is type 14 and carries the stable route key
    // at +0x08.  Keep the legacy type-5 flow checks above, then exercise the
    // native wire dialect explicitly so a tick/sequence regression cannot hide
    // behind the C# compatibility alias.
    var nativePayload = new byte[ClientPacket.PackSize + 1];
    BitConverter.GetBytes(101).CopyTo(nativePayload, 0);
    BitConverter.GetBytes((ushort)Grobal2.SM_NEWMAP).CopyTo(nativePayload, 4);
    Require(fixture.GateService.HandleSendBuffer(CreateGateCommandBuffer(
            route.ConnId, NativeGameGateCommands.M2ClientData, nativePayload)),
        "native type-14 DATA frame was not queued by M2");
    var nativeData = await fixture.M2ToHub.ReadAsync(
        packet => packet.ConnID == route.ConnId
                  && packet.Cmd == NativeGameGateCommands.M2ClientData);
    Equal(SharedBackendRoute.ComposeRouteId(1, checked((ushort)route.ConnId)),
        nativeData.SeqID, "native type-14 stable route context");
    Equal(nativePayload.Length, nativeData.Payload.Length,
        "native type-14 payload length");
    Console.WriteLine("PASS native type 14 DATA uses stable route context");

    var reconnectAccept = fixture.GameListener.AcceptTcpClientAsync();
    fixture.DisconnectFirstGameConnection();
    await WaitUntilAsync(() => Volatile.Read(ref abortCount) == 1,
        "dead M2 connection did not abort the logical route");
    Require(route.IsGameInvalidated, "dead M2 route was not invalidated");
    Require(!fixture.Hub.TryGetRoute(route.ConnId, route.SessionGeneration, out _),
        "invalidated M2 route remained routable");

    using var replacementPeer = await reconnectAccept.WaitAsync(TimeSpan.FromSeconds(5));
    await RequireNoOpenReplayAsync(replacementPeer.GetStream(), route.ConnId,
        TimeSpan.FromMilliseconds(900));
    Console.WriteLine("PASS reconnect does not replay stale GM_OPEN");

    await fixture.Hub.CloseRouteAsync(route);
    Console.WriteLine("C# GameGate/GameSvr control-plane integration checks passed.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("FAIL C# GameGate/GameSvr integration: " + ex);
    return 1;
}

static async Task RequireNoOpenReplayAsync(NetworkStream stream, uint staleConnId,
    TimeSpan observationWindow)
{
    var parser = new InternalPacket77FrameParser(maximumFrameLength: 0x8000);
    var buffer = new byte[8192];
    using var timeout = new CancellationTokenSource(observationWindow);
    try
    {
        while (true)
        {
            var count = await stream.ReadAsync(buffer, timeout.Token);
            if (count <= 0) return;
            if (!parser.TryAppend(buffer, 0, count, out var packets, out var error))
                throw new InvalidDataException("replacement M2 frame: " + error);
            Require(!packets.Any(packet => packet.ConnID == staleConnId
                                           && packet.Cmd == Grobal2.GM_OPEN),
                "stale GM_OPEN was replayed on the replacement M2 connection");
        }
    }
    catch (OperationCanceledException) when (timeout.IsCancellationRequested)
    {
    }
}

static async Task WaitUntilAsync(Func<bool> predicate, string failure)
{
    var deadline = Environment.TickCount64 + 5000;
    while (Environment.TickCount64 < deadline)
    {
        if (predicate()) return;
        await Task.Delay(10);
    }
    throw new TimeoutException(failure);
}

static byte[] CreateGateBuffer(uint connId, byte marker)
{
    return CreateGateCommandBuffer(connId, Grobal2.GM_DATA, new[] { marker });
}

static async Task DelayedRegistrationGatesOpenAsync()
{
    var dbListener = new TcpListener(IPAddress.Loopback, 0);
    var gameListener = new TcpListener(IPAddress.Loopback, 0);
    dbListener.Start();
    gameListener.Start();
    var dbAccept = dbListener.AcceptTcpClientAsync();
    var gameAccept = gameListener.AcceptTcpClientAsync();
    using var hub = new SharedBackendHub(new GateConfig
    {
        BackendIP = "127.0.0.1",
        BackendPort2 = ((IPEndPoint)dbListener.LocalEndpoint).Port,
        GameBackendIP = "127.0.0.1",
        BackendPort = ((IPEndPoint)gameListener.LocalEndpoint).Port,
        GateIndex = 1
    }, (_, _) => { });
    hub.Start();
    using var dbPeer = await dbAccept.WaitAsync(TimeSpan.FromSeconds(5));
    using var gamePeer = await gameAccept.WaitAsync(TimeSpan.FromSeconds(5));
    try
    {
        var registration = await ReadInternalFrameAsync(gamePeer.GetStream());
        Equal(NativeGameGateCommands.GateRegistrationRequest,
            registration.Cmd, "delayed registration request command");
        Equal(0u, registration.ConnID, "delayed registration request conn");
        Equal(1u, registration.SeqID, "delayed registration requested index");

        var openTask = hub.OpenRouteAsync(123, 123, "127.0.0.1", 1,
            () => { }, CancellationToken.None);
        var gameStream = gamePeer.GetStream();
        await RequireNoDataAsync(gameStream,
            "OPEN was emitted before Cmd15 registration");

        var reply = InternalPacket77.Ack(5, 0,
            NativeGameGateCommands.M2RegistrationReply).ToBytes();
        await gameStream.WriteAsync(reply);
        var route = await openTask.WaitAsync(TimeSpan.FromSeconds(5));
        Require(route != null, "route did not open after Cmd15 registration");
        Equal(5, hub.RegisteredGateIndex,
            "M2-assigned gate index was not retained");

        var open = await ReadInternalFrameAsync(gameStream);
        Equal(Grobal2.GM_OPEN, open.Cmd,
            "delayed registration OPEN command");
        Equal(SharedBackendRoute.ComposeRouteId(5, 123), open.SeqID,
            "delayed registration OPEN route context");
        await RequireNoDataAsync(gameStream,
            "delayed registration emitted duplicate OPEN");
    }
    finally
    {
        await hub.StopAsync();
        dbListener.Stop();
        gameListener.Stop();
    }
}

static async Task<InternalPacket77> ReadInternalFrameAsync(NetworkStream stream)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var header = new byte[InternalPacket77.HEADER_SIZE];
    await ReadExactlyAsync(stream, header, timeout.Token);
    var bodyLength = BitConverter.ToUInt16(header, 14);
    var frame = new byte[InternalPacket77.HEADER_SIZE + bodyLength];
    Buffer.BlockCopy(header, 0, frame, 0, header.Length);
    if (bodyLength > 0)
        await ReadExactlyAsync(stream,
            frame.AsMemory(InternalPacket77.HEADER_SIZE, bodyLength),
            timeout.Token);
    return InternalPacket77.FromBytes(frame, 0, frame.Length)
        ?? throw new InvalidDataException("internal frame decode failed");
}

static async Task ReadExactlyAsync(NetworkStream stream, Memory<byte> destination,
    CancellationToken cancellationToken)
{
    var offset = 0;
    while (offset < destination.Length)
    {
        var count = await stream.ReadAsync(destination[offset..],
            cancellationToken);
        if (count <= 0) throw new EndOfStreamException();
        offset += count;
    }
}

static async Task RequireNoDataAsync(NetworkStream stream, string failure)
{
    var deadline = Environment.TickCount64 + 300;
    while (Environment.TickCount64 < deadline)
    {
        if (stream.DataAvailable) throw new InvalidOperationException(failure);
        await Task.Delay(10);
    }
}

static byte[] CreateGateCommandBuffer(uint connId, ushort command, byte[] payload)
{
    var header = new PacketHeader
    {
        PacketCode = Grobal2.RUNGATECODE,
        Socket = unchecked((int)connId),
        Ident = command,
        PackLength = payload.Length
    };
    var buffer = new byte[4 + PacketHeader.PacketSize + payload.Length];
    BitConverter.GetBytes(PacketHeader.PacketSize + payload.Length).CopyTo(buffer, 0);
    header.GetBuffer().CopyTo(buffer, 4);
    if (payload.Length > 0)
        payload.CopyTo(buffer, 4 + PacketHeader.PacketSize);
    return buffer;
}

static bool IsMarker(InternalPacket77 packet, uint connId, byte marker) =>
    packet.ConnID == connId && packet.Cmd == NativeGameGateCommands.M2ClientData
    && packet.Payload is { Length: 1 } && packet.Payload[0] == marker;

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

static void VerifyNativeM2BodyLimit()
{
    var valid = new InternalPacket77
    {
        Magic = InternalPacket77.MAGIC,
        ConnID = 1,
        SeqID = 2,
        Cmd = NativeGameGateCommands.GateClientData,
        Payload = new byte[NativeGameGateCommands.NativeM2MaximumBodyLength]
    }.ToBytes();
    Require(SharedBackendHub.TryValidateNativeM2Frame(valid, out var validError),
        "native maximum body rejected: " + validError);

    var parser = new InternalPacket77FrameParser();
    Require(parser.TryAppend(valid, 0, valid.Length, out var validFrames,
            out var parserError),
        "native maximum body parser error: " + parserError);
    Equal(1, validFrames.Count, "native maximum body parser count");
    Equal(NativeGameGateCommands.NativeM2MaximumBodyLength,
        validFrames[0].Payload.Length, "native maximum body length");

    var oversized = new InternalPacket77
    {
        Magic = InternalPacket77.MAGIC,
        ConnID = 3,
        SeqID = 4,
        Cmd = NativeGameGateCommands.GateClientData,
        Payload = new byte[NativeGameGateCommands.NativeM2MaximumBodyLength + 1]
    }.ToBytes();
    Require(!SharedBackendHub.TryValidateNativeM2Frame(oversized,
            out var oversizedError),
        "native oversized body was accepted: " + oversizedError);

    var tail = InternalPacket77.Ack(5, 6, NativeGameGateCommands.GateKeepAliveRequest)
        .ToBytes();
    var joined = new byte[oversized.Length + tail.Length];
    Buffer.BlockCopy(oversized, 0, joined, 0, oversized.Length);
    Buffer.BlockCopy(tail, 0, joined, oversized.Length, tail.Length);
    Require(parser.TryAppend(joined, 0, joined.Length, out var droppedFrames,
            out parserError),
        "native oversized buffer parser error: " + parserError);
    Equal(0, droppedFrames.Count, "native oversized buffer frame count");
    Equal(0, parser.BufferedLength, "native oversized buffer reset");
}

static void Equal<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{name}: expected={expected}, actual={actual}");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class PacketTap
{
    private readonly InternalPacket77FrameParser _parser = new(maximumFrameLength: 0x8000);
    private readonly Channel<InternalPacket77> _packets =
        Channel.CreateUnbounded<InternalPacket77>();

    public void Observe(byte[] buffer, int count)
    {
        if (!_parser.TryAppend(buffer, 0, count, out var packets, out var error))
            throw new InvalidDataException("tap frame: " + error);
        foreach (var packet in packets) _packets.Writer.TryWrite(packet);
    }

    public async Task<InternalPacket77> ReadAsync(Func<InternalPacket77, bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (await _packets.Reader.WaitToReadAsync(timeout.Token))
        {
            while (_packets.Reader.TryRead(out var packet))
            {
                if (predicate(packet)) return packet;
            }
        }
        throw new TimeoutException("expected InternalPacket77 was not observed");
    }
}

sealed class GateM2Fixture : IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TcpListener _dbListener;
    private readonly TcpListener _serviceListener;
    private readonly TcpClient _dbPeer;
    private readonly TcpClient _firstGamePeer;
    private readonly TcpClient _bridgeM2Peer;
    private readonly Socket _m2Socket;
    private readonly Task[] _workers;

    private GateM2Fixture(TcpListener dbListener, TcpListener gameListener,
        TcpListener serviceListener, TcpClient dbPeer, TcpClient firstGamePeer,
        TcpClient bridgeM2Peer, Socket m2Socket, SharedBackendHub hub,
        GateService gateService, TGateInfo gateInfo, PacketTap hubToM2,
        PacketTap m2ToHub)
    {
        _dbListener = dbListener;
        GameListener = gameListener;
        _serviceListener = serviceListener;
        _dbPeer = dbPeer;
        _firstGamePeer = firstGamePeer;
        _bridgeM2Peer = bridgeM2Peer;
        _m2Socket = m2Socket;
        Hub = hub;
        GateService = gateService;
        GateInfo = gateInfo;
        HubToM2 = hubToM2;
        M2ToHub = m2ToHub;

        GateService.StartQueueService();
        _workers =
        [
            PumpAsync(_firstGamePeer.GetStream(), _bridgeM2Peer.GetStream(),
                HubToM2, _lifetime.Token),
            PumpAsync(_bridgeM2Peer.GetStream(), _firstGamePeer.GetStream(),
                M2ToHub, _lifetime.Token),
            ReceiveM2Async(_m2Socket, GateService, _lifetime.Token),
            DrainAsync(_dbPeer.GetStream(), _lifetime.Token)
        ];
    }

    public SharedBackendHub Hub { get; }
    public GateService GateService { get; }
    public TGateInfo GateInfo { get; }
    public TcpListener GameListener { get; }
    public PacketTap HubToM2 { get; }
    public PacketTap M2ToHub { get; }

    public static async Task<GateM2Fixture> CreateAsync()
    {
        var dbListener = new TcpListener(IPAddress.Loopback, 0);
        var gameListener = new TcpListener(IPAddress.Loopback, 0);
        var serviceListener = new TcpListener(IPAddress.Loopback, 0);
        dbListener.Start();
        gameListener.Start();
        serviceListener.Start();

        var dbAccept = dbListener.AcceptTcpClientAsync();
        var gameAccept = gameListener.AcceptTcpClientAsync();
        var hub = new SharedBackendHub(new GateConfig
        {
            BackendIP = "127.0.0.1",
            BackendPort2 = ((IPEndPoint)dbListener.LocalEndpoint).Port,
            GameBackendIP = "127.0.0.1",
            BackendPort = ((IPEndPoint)gameListener.LocalEndpoint).Port
        }, (_, _) => { });
        hub.Start();

        var dbPeer = await dbAccept.WaitAsync(TimeSpan.FromSeconds(5));
        var firstGamePeer = await gameAccept.WaitAsync(TimeSpan.FromSeconds(5));

        var serviceAccept = serviceListener.AcceptSocketAsync();
        var bridgeM2Peer = new TcpClient { NoDelay = true };
        await bridgeM2Peer.ConnectAsync(IPAddress.Loopback,
            ((IPEndPoint)serviceListener.LocalEndpoint).Port);
        var m2Socket = await serviceAccept.WaitAsync(TimeSpan.FromSeconds(5));
        m2Socket.NoDelay = true;

        var gateInfo = new TGateInfo
        {
            boUsed = true,
            Socket = m2Socket,
            SocketId = 1,
            UserList = new List<TGateUserInfo>()
        };
        var gateService = new GateService(1, gateInfo);
        return new GateM2Fixture(dbListener, gameListener, serviceListener,
            dbPeer, firstGamePeer, bridgeM2Peer, m2Socket, hub, gateService,
            gateInfo, new PacketTap(), new PacketTap());
    }

    public void DisconnectFirstGameConnection() => _firstGamePeer.Dispose();

    public async Task SendFromM2Async(byte[] frame)
    {
        await _m2Socket.SendAsync(frame, SocketFlags.None);
    }

    private static async Task PumpAsync(NetworkStream source, NetworkStream destination,
        PacketTap tap, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var count = await source.ReadAsync(buffer, cancellationToken);
                if (count <= 0) return;
                tap.Observe(buffer, count);
                await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException
                                   or IOException or SocketException
                                   or ObjectDisposedException)
        {
        }
    }

    private static async Task ReceiveM2Async(Socket socket, GateService service,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var count = await socket.ReceiveAsync(buffer, SocketFlags.None,
                    cancellationToken);
                if (count <= 0) return;
                service.HandleReceiveBuffer(count, buffer);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException
                                   or IOException or SocketException
                                   or ObjectDisposedException)
        {
        }
    }

    private static async Task DrainAsync(NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[1024];
        try
        {
            while (await stream.ReadAsync(buffer, cancellationToken) > 0)
            {
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException
                                   or IOException or SocketException
                                   or ObjectDisposedException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { await Hub.StopAsync(); } catch { }
        _lifetime.Cancel();
        GateService.Stop();
        try { _firstGamePeer.Dispose(); } catch { }
        try { _bridgeM2Peer.Dispose(); } catch { }
        try { _m2Socket.Dispose(); } catch { }
        try { _dbPeer.Dispose(); } catch { }
        _dbListener.Stop();
        GameListener.Stop();
        _serviceListener.Stop();
        try { await Task.WhenAll(_workers).WaitAsync(TimeSpan.FromSeconds(3)); } catch { }
        _lifetime.Dispose();
        Hub.Dispose();
    }
}
