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
    await using var fixture = await GateM2Fixture.CreateAsync();
    var abortCount = 0;
    var route = await fixture.Hub.OpenRouteAsync(101, "127.0.0.1", 1,
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
    Equal(1, fixture.GateInfo.nUserCount, "M2 gate user count");
    Console.WriteLine("PASS real OPEN -> GM_SERVERUSERINDEX");

    Require(await fixture.Hub.SendGameHeartbeatOnceAsync(CancellationToken.None),
        "SharedBackendHub did not send its heartbeat");
    var heartbeat = await fixture.HubToM2.ReadAsync(
        packet => packet.ConnID == 0 && packet.Cmd == Grobal2.GM_CHECKCLIENT);
    Equal(0, heartbeat.Payload.Length, "GM_CHECKCLIENT payload");
    await WaitUntilAsync(() => Volatile.Read(ref fixture.GateInfo.boSendKeepAlive),
        "GateService did not mark GM_CHECKCLIENT for acknowledgement");

    // This is the exact GateManager.Run branch that consumes GateService's state.
    Volatile.Write(ref fixture.GateInfo.boSendKeepAlive, false);
    fixture.GateService.SendCheck(Grobal2.GM_CHECKSERVER);
    var heartbeatReply = await fixture.M2ToHub.ReadAsync(
        packet => packet.ConnID == 0 && packet.Cmd == Grobal2.GM_CHECKSERVER);
    Equal(0, heartbeatReply.Payload.Length, "GM_CHECKSERVER payload");
    Console.WriteLine("PASS real heartbeat 4 -> M2 state -> response 3");

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

    fixture.GateService.SendCompactAck();
    var compactAck = await fixture.M2ToHub.ReadAsync(
        packet => packet.ConnID == 0 && packet.Cmd == 0x0C);
    Equal(InternalPacket77.ACK_FRAME_LEN, compactAck.FrameLen,
        "compact ACK frame length");
    Console.WriteLine(
        $"PASS M2 compact ACK stays {InternalPacket77.ACK_FRAME_LEN} bytes");

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
    var header = new PacketHeader
    {
        PacketCode = Grobal2.RUNGATECODE,
        Socket = unchecked((int)connId),
        Ident = Grobal2.GM_DATA,
        PackLength = 1
    };
    var buffer = new byte[4 + PacketHeader.PacketSize + 1];
    BitConverter.GetBytes(PacketHeader.PacketSize + 1).CopyTo(buffer, 0);
    header.GetBuffer().CopyTo(buffer, 4);
    buffer[^1] = marker;
    return buffer;
}

static bool IsMarker(InternalPacket77 packet, uint connId, byte marker) =>
    packet.ConnID == connId && packet.Cmd == Grobal2.GM_DATA
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
