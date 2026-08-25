using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using GameGate.Core;
using SystemModule;
using SystemModule.Packet;

await VerifyOpenWaitsForRegistration();
await VerifyTerminationQueueReleasesRouteLock();

using var dbListener = new TcpListener(IPAddress.Loopback, 0);
using var gameListener = new TcpListener(IPAddress.Loopback, 0);
dbListener.Start();
gameListener.Start();
var dbPort = ((IPEndPoint)dbListener.LocalEndpoint).Port;
var gamePort = ((IPEndPoint)gameListener.LocalEndpoint).Port;

using var hub = new SharedBackendHub(new GateConfig
{
    BackendIP = "127.0.0.1",
    BackendPort2 = dbPort,
    GameBackendIP = "127.0.0.1",
    BackendPort = gamePort,
    GatePort = 7100,
    GateIndex = 7
}, (_, _) => { });
hub.Start();

var dbAccept = dbListener.AcceptTcpClientAsync();
var gameAccept = gameListener.AcceptTcpClientAsync();
var routeOpen = hub.OpenRouteAsync(9001, 2359, "223.160.203.135", 1,
    () => { }, CancellationToken.None);
using var dbPeer = await dbAccept.WaitAsync(TimeSpan.FromSeconds(3));
using var gamePeer = await gameAccept.WaitAsync(TimeSpan.FromSeconds(3));
var dbStream = dbPeer.GetStream();
var gameStream = gamePeer.GetStream();

var register = await ReadDbFrame(dbStream);
Equal(NativeGameGateDbProtocol.RegisterRequest, register.Ident,
    "first DB command");
Equal(7100, register.QueryId, "DB registration gate port");
Equal(0, register.Param, "DB registration parameter");
Equal(0, register.Payload.Length, "DB registration payload");
var gameRegistration = await ReadGameFrame(gameStream);
Equal(NativeGameGateCommands.GateRegistrationRequest,
    gameRegistration.Cmd, "Game registration command");
await WriteGameFrame(gameStream, new InternalPacket77
{
    Magic = InternalPacket77.MAGIC,
    ConnID = 7,
    SeqID = 0,
    Cmd = 15,
    Payload = Array.Empty<byte>()
});

await WriteDbFrameFragmented(dbStream, new YbDbLegacy77Frame(0x102, -7,
    NativeGameGateDbProtocol.RegisterResponse, new byte[] { 0xAA }));
var open = await ReadDbFrame(dbStream);
Equal(NativeGameGateDbProtocol.OpenRequest, open.Ident,
    "DB open command");
Equal(2359, open.QueryId, "DB open session");
Equal(NativeGameGateDbProtocol.ComposeRouteId(2, 2359), open.Param,
    "DB open stable route id");
BytesEqual(Encoding.ASCII.GetBytes("223.160.203.135\0"), open.Payload,
    "DB open IP payload");

await WriteDbFrameFragmented(dbStream, new YbDbLegacy77Frame(2359, 77,
    NativeGameGateDbProtocol.OpenResponse, new byte[] { 0x55 }));
var gameOpen = await ReadUntilGameCommand(gameStream, Grobal2.GM_OPEN);
Equal((uint)2359, gameOpen.ConnID, "Game open session");
var route = await routeOpen.WaitAsync(TimeSpan.FromSeconds(3));
Check(route != null, "logical route did not open");
Equal((uint)NativeGameGateDbProtocol.ComposeRouteId(2, 2359),
    route.DbRouteId, "route retained DB route id");
Equal(77, Volatile.Read(ref route.NativeDbOpenContext),
    "route retained DB open context");

var loginPrompt = await route.DbResponses.Reader.ReadAsync().AsTask()
    .WaitAsync(TimeSpan.FromSeconds(3));
Check(Encoding.ASCII.GetString(loginPrompt).StartsWith("%2359/#",
          StringComparison.Ordinal),
    "open response did not queue the login prompt");

var requestHeader = new ClientPacket
{
    Recog = 17,
    Ident = 4017,
    Param = 1,
    Tag = 2,
    Series = 3
};
var requestBody = Convert.FromHexString("C1FAC9F1");
Check(await hub.SendDbAsync(route, requestHeader, requestBody),
    "native DB DATA send failed");
var data = await ReadDbFrame(dbStream);
Equal(NativeGameGateDbProtocol.DataRequest, data.Ident,
    "DB data command");
Equal(2359, data.QueryId, "DB data session");
Equal(open.Param, data.Param, "DB data route id");
Equal(12 + requestBody.Length + 1, data.Payload.Length,
    "DB data payload length");
Equal((byte)0, data.Payload[^1], "DB data wrapper terminator");
Equal(17, BinaryPrimitives.ReadInt32LittleEndian(data.Payload),
    "DB data recog");
Equal((ushort)4017,
    BinaryPrimitives.ReadUInt16LittleEndian(data.Payload.AsSpan(4)),
    "DB data ident");
BytesEqual(requestBody,
    data.Payload.AsSpan(ClientPacket.PackSize, requestBody.Length).ToArray(),
    "DB data body");

var responseBody = Convert.FromHexString("04C1FAC9F100");
var response = LegacyGateDataCodec.CreateResponse(2359, 9, 4012,
    1, 2, 3, responseBody);
var legacy = Encoding.ASCII.GetBytes("%2359/#legacy!$");
Check(YbDbLegacy77Codec.TryEncode(response, out var responseWire,
    out var responseError), responseError);
await dbStream.WriteAsync(responseWire.Concat(legacy).ToArray());
var normalized = await route.DbResponses.Reader.ReadAsync().AsTask()
    .WaitAsync(TimeSpan.FromSeconds(3));
var normalizedText = Encoding.ASCII.GetString(normalized);
Check(normalizedText.StartsWith("%2359/#", StringComparison.Ordinal)
      && normalizedText.EndsWith("!$", StringComparison.Ordinal),
    "native DB response was not routed through the down adapter");
var normalizedPayload = normalizedText.Substring("%2359/#".Length,
    normalizedText.Length - "%2359/#".Length - "!$".Length);
var normalizedHeader = EDcode.DecodePacket(
    normalizedPayload[..Grobal2.DEFBLOCKSIZE]);
Check(normalizedHeader != null, "native DB response header did not decode");
Equal(9, normalizedHeader.Recog, "native DB response recog");
Equal((ushort)4012, MobileCmdMap.ToClient(normalizedHeader.Ident),
    "native DB response final client ident");
Equal((ushort)1, normalizedHeader.Param, "native DB response param");
Equal((ushort)2, normalizedHeader.Tag, "native DB response tag");
Equal((ushort)3, normalizedHeader.Series, "native DB response series");
var normalizedBodyText = normalizedPayload[Grobal2.DEFBLOCKSIZE..];
var normalizedBody = Misc.Decode6BitBufDirect(
    HUtil32.GetBytes(normalizedBodyText), normalizedBodyText.Length);
BytesEqual(responseBody, normalizedBody, "native DB response body");

var legacyRouted = await route.DbResponses.Reader.ReadAsync().AsTask()
    .WaitAsync(TimeSpan.FromSeconds(3));
BytesEqual(legacy, legacyRouted, "legacy DB response compatibility");

await WriteDbFrame(dbStream, new YbDbLegacy77Frame(2359, 0, 12,
    Array.Empty<byte>()));
var termination = await route.DbResponses.Reader.ReadAsync().AsTask()
    .WaitAsync(TimeSpan.FromSeconds(3));
Equal(0, termination.Length, "native DB termination sentinel");
Check(!await hub.SendDbAsync(route, requestHeader, requestBody),
    "native DB DATA was accepted after command 12");
await ExpectNoDbBytes(dbStream, TimeSpan.FromMilliseconds(150),
    "native DB DATA followed command 12");

await hub.CloseRouteAsync(route);
var close = await ReadDbFrame(dbStream);
Equal(NativeGameGateDbProtocol.CloseRequest, close.Ident,
    "DB close command");
Equal(2359, close.QueryId, "DB close session");
Equal(77, close.Param, "DB close backend context");
Equal(0, close.Payload.Length, "DB close payload");

Console.WriteLine("GameGateNativeDbIntegrationCheck PASS "
    + "order=3/13/1/11/4/14/12/6 split+sticky=covered "
    + "legacy-receive=preserved");

static async Task VerifyOpenWaitsForRegistration()
{
    using var dbListener = new TcpListener(IPAddress.Loopback, 0);
    dbListener.Start();
    var dbPort = ((IPEndPoint)dbListener.LocalEndpoint).Port;
    using var gamePortLease = new TcpListener(IPAddress.Loopback, 0);
    gamePortLease.Start();
    var unavailableGamePort =
        ((IPEndPoint)gamePortLease.LocalEndpoint).Port;
    gamePortLease.Stop();

    using var hub = new SharedBackendHub(new GateConfig
    {
        BackendIP = "127.0.0.1",
        BackendPort2 = dbPort,
        GameBackendIP = "127.0.0.1",
        BackendPort = unavailableGamePort,
        GatePort = 7100,
        GateIndex = 1
    }, (_, _) => { });
    hub.Start();
    using var cancellation = new CancellationTokenSource();
    var accepting = dbListener.AcceptTcpClientAsync();
    var opening = hub.OpenRouteAsync(7001, 701, "127.0.0.1", 1,
        () => { }, cancellation.Token);
    using var peer = await accepting.WaitAsync(TimeSpan.FromSeconds(3));
    var stream = peer.GetStream();
    var registration = await ReadDbFrame(stream);
    Equal(NativeGameGateDbProtocol.RegisterRequest, registration.Ident,
        "registration-wait first DB command");
    await ExpectNoDbBytes(stream, TimeSpan.FromMilliseconds(250),
        "DB OPEN preceded command 13");
    cancellation.Cancel();
    var route = await opening.WaitAsync(TimeSpan.FromSeconds(3));
    Check(route == null, "canceled unregistered route unexpectedly opened");
    Check(!hub.TryGetRoute(701, 1, out _),
        "canceled unregistered route leaked in the route table");
    await hub.StopAsync();
}

static async Task VerifyTerminationQueueReleasesRouteLock()
{
    var route = new SharedBackendRoute
    {
        Handle = 1,
        NativeSessionId = 1,
        GateIndex = 1,
        ConnId = 1,
        SessionGeneration = 1,
        ClientIp = "127.0.0.1",
        DbOpenFrame = Array.Empty<byte>(),
        Abort = () => { }
    };
    for (var i = 0; i < 256; i++)
        Check(route.DbResponses.Writer.TryWrite(new byte[] { 1 }),
            "failed to saturate DB response channel");

    var pending = route.QueueDbTerminationAsync(CancellationToken.None)
        .AsTask();
    await Task.Delay(50);
    Check(!pending.IsCompleted,
        "saturated DB termination write did not wait");
    Check(await route.DbSendCloseLock.WaitAsync(
            TimeSpan.FromMilliseconds(250)),
        "saturated DB termination held the route send/close lock");
    route.DbSendCloseLock.Release();
    route.DbResponses.Writer.TryComplete();
    Check(!await pending.WaitAsync(TimeSpan.FromSeconds(3)),
        "closed DB response channel accepted termination sentinel");
}

static async Task<YbDbLegacy77Frame> ReadDbFrame(NetworkStream stream)
{
    var header = new byte[YbDbLegacy77Codec.HeaderSize];
    await ReadExactly(stream, header);
    Equal(YbDbLegacy77Codec.FrameMagic,
        BinaryPrimitives.ReadUInt32LittleEndian(header), "DB frame magic");
    var payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(
        header.AsSpan(14, 2));
    var wire = new byte[header.Length + payloadLength];
    header.CopyTo(wire, 0);
    if (payloadLength > 0)
        await ReadExactly(stream, wire.AsMemory(header.Length,
            payloadLength));
    Check(YbDbLegacy77Codec.TryDecode(wire, out var frame,
        out var error), error);
    return frame;
}

static async Task WriteDbFrame(NetworkStream stream,
    YbDbLegacy77Frame frame)
{
    Check(YbDbLegacy77Codec.TryEncode(frame, out var wire,
        out var error), error);
    await stream.WriteAsync(wire);
}

static async Task WriteDbFrameFragmented(NetworkStream stream,
    YbDbLegacy77Frame frame)
{
    Check(YbDbLegacy77Codec.TryEncode(frame, out var wire,
        out var error), error);
    for (var i = 0; i < wire.Length; i++)
        await stream.WriteAsync(wire.AsMemory(i, 1));
}

static async Task<InternalPacket77> ReadGameFrame(NetworkStream stream)
{
    var header = new byte[InternalPacket77.HEADER_SIZE];
    await ReadExactly(stream, header);
    Equal(InternalPacket77.MAGIC,
        BinaryPrimitives.ReadUInt32LittleEndian(header),
        "Game frame magic");
    var bodyLength = BinaryPrimitives.ReadUInt16LittleEndian(
        header.AsSpan(14, 2));
    var wire = new byte[header.Length + bodyLength];
    header.CopyTo(wire, 0);
    if (bodyLength > 0)
        await ReadExactly(stream, wire.AsMemory(header.Length,
            bodyLength));
    var packet = InternalPacket77.FromBytes(wire, 0, wire.Length);
    Check(packet != null, "Game frame did not parse");
    return packet;
}

static async Task<InternalPacket77> ReadUntilGameCommand(NetworkStream stream,
    ushort command)
{
    while (true)
    {
        var frame = await ReadGameFrame(stream);
        if (frame.Cmd == command) return frame;
    }
}

static async Task WriteGameFrame(NetworkStream stream,
    InternalPacket77 packet)
{
    packet.FrameLen = checked((ushort)(InternalPacket77.HEADER_SIZE
        + (packet.Payload?.Length ?? 0)));
    packet.Field20 = checked((uint)(packet.Payload?.Length ?? 0));
    await stream.WriteAsync(packet.ToBytes());
}

static async Task ReadExactly(NetworkStream stream, Memory<byte> buffer)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    var offset = 0;
    while (offset < buffer.Length)
    {
        var count = await stream.ReadAsync(buffer[offset..], timeout.Token);
        if (count <= 0) throw new EndOfStreamException();
        offset += count;
    }
}

static async Task ExpectNoDbBytes(NetworkStream stream, TimeSpan duration,
    string label)
{
    using var timeout = new CancellationTokenSource(duration);
    var probe = new byte[1];
    try
    {
        var count = await stream.ReadAsync(probe, timeout.Token);
        if (count != 0) throw new InvalidOperationException(label);
        throw new EndOfStreamException(label + ": stream closed");
    }
    catch (OperationCanceledException) when (timeout.IsCancellationRequested)
    {
    }
}

static void Check(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
}

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{label}: expected={expected}, actual={actual}");
}

static void BytesEqual(byte[] expected, byte[] actual, string label)
{
    if (!expected.SequenceEqual(actual))
        throw new InvalidOperationException(
            $"{label}: expected={Convert.ToHexString(expected)}, "
            + $"actual={Convert.ToHexString(actual)}");
}
