using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using GameGate.Core;
using SystemModule.Packet;

var tests = new (string Name, Action Run)[]
{
    ("raw frame is preserved", RawFrameIsPreserved),
    ("byte-wise fragmented parser", ByteWiseFragmentedParser),
    ("mixed sticky stream", MixedStickyStream),
    ("echo length boundary", EchoLengthBoundary),
    ("outer frame boundary", OuterFrameBoundary),
    ("oversized frame drops sticky buffer", OversizedFrameDropsStickyBuffer)
};

foreach (var test in tests)
    test.Run();

await SameLinkEchoAndNoClientDelivery();

Console.WriteLine($"GameGateLegacyType20CompatCheck PASS tests={tests.Length + 1} " +
                  "header=16 type=20 echo=same-link raw total<0x8000");

static void RawFrameIsPreserved()
{
    var wire = BuildType20(173, 0xA1B2C3D4, 0x10203040);
    wire[32] = 0x77;
    wire[33] = 0xBB;
    wire[34] = 0xAA;
    wire[35] = 0x33;
    wire[71] = 0;
    wire[72] = 0xFF;

    var parsed = LegacyGateType20.FromBytes(wire, 0, wire.Length);
    NotNull(parsed, "decode");
    Equal(0xA1B2C3D4u, parsed.ConnectionId, "connection id");
    Equal(0x10203040u, parsed.Context, "context");
    Equal(wire.Length, parsed.TotalLength, "total length");
    Equal((ushort)(wire.Length - LegacyGateType20.HeaderSize),
        parsed.PayloadLength, "payload length");
    BytesEqual(wire, parsed.ToBytes(), "round-trip bytes");

    var firstCopy = parsed.ToBytes();
    firstCopy[4] ^= 0xFF;
    BytesEqual(wire, parsed.ToBytes(), "wire escaped through returned clone");
}

static void ByteWiseFragmentedParser()
{
    var wire = BuildType20(79, 7, 8);
    var parser = new GameGateServerFrameParser();
    var frames = new List<GameGateServerFrame>();
    for (var i = 0; i < wire.Length; i++)
    {
        True(parser.TryAppend(wire, i, 1, out var batch, out var error), error);
        frames.AddRange(batch);
        Equal(i == wire.Length - 1 ? 1 : 0, frames.Count,
            $"fragment count at {i}");
    }

    Equal(0, parser.BufferedLength, "fragment final buffer");
    NotNull(frames[0].LegacyType20, "fragment classification");
    Null(frames[0].Internal77, "fragment generic classification");
    BytesEqual(wire, frames[0].LegacyType20.ToBytes(), "fragment bytes");
}

static void MixedStickyStream()
{
    var type20 = BuildType20(0x8000, 11, 12);
    var ordinary = new InternalPacket77
    {
        ConnID = 99,
        SeqID = 100,
        Cmd = 0x4321,
        Payload = new byte[] { 5, 6, 7 }
    }.ToBytes();
    var parser = new GameGateServerFrameParser();
    var joined = Join(type20, ordinary);

    True(parser.TryAppend(joined, 0, joined.Length, out var frames, out var error),
        error);
    Equal(2, frames.Count, "sticky frame count");
    NotNull(frames[0].LegacyType20, "sticky type20");
    False(frames[0].LegacyType20.CanEcho, "0x8000 sticky frame echoed");
    NotNull(frames[1].Internal77, "sticky ordinary");
    Equal(99u, frames[1].Internal77.ConnID, "sticky ordinary connection");
    Equal(0, parser.BufferedLength, "sticky final buffer");
}

static void EchoLengthBoundary()
{
    var accepted = LegacyGateType20.FromBytes(
        BuildType20(0x7FFF), 0, 0x7FFF);
    var consumed = LegacyGateType20.FromBytes(
        BuildType20(0x8000), 0, 0x8000);

    NotNull(accepted, "0x7FFF decode");
    NotNull(consumed, "0x8000 decode");
    True(accepted.CanEcho, "0x7FFF rejected");
    False(consumed.CanEcho, "0x8000 echoed");
}

static void OuterFrameBoundary()
{
    var wire = BuildType20(0xFFFF);
    var packet = LegacyGateType20.FromBytes(wire, 0, wire.Length);
    NotNull(packet, "0xFFFF decode");
    Equal(0xFFFF, packet.TotalLength, "largest outer total");
    False(packet.CanEcho, "0xFFFF echoed");

    var parser = new GameGateServerFrameParser(maximumBufferedLength: 0x20000,
        maximumInternalFrameLength: ushort.MaxValue);
    True(parser.TryAppend(wire, 0, wire.Length, out var frames, out var error), error);
    Equal(1, frames.Count, "0xFFFF parser count");
    NotNull(frames[0].LegacyType20, "0xFFFF parser classification");
    Equal(0, parser.BufferedLength, "0xFFFF parser buffer");
}

static void OversizedFrameDropsStickyBuffer()
{
    var oversized = BuildType20(0x10000);
    var tail = new InternalPacket77
    {
        ConnID = 77,
        SeqID = 88,
        Cmd = 99,
        Payload = new byte[] { 1 }
    }.ToBytes();
    var joined = Join(oversized, tail);
    var parser = new GameGateServerFrameParser(maximumBufferedLength: 0x20000);

    True(parser.TryAppend(joined, 0, joined.Length, out var frames, out var error),
        error);
    Equal(0, frames.Count, "oversized buffer emitted a sticky tail");
    Equal(0, parser.BufferedLength, "oversized buffer was not dropped");
}

static async Task SameLinkEchoAndNoClientDelivery()
{
    var firstListener = new TcpListener(IPAddress.Loopback, 0);
    var secondListener = new TcpListener(IPAddress.Loopback, 0);
    firstListener.Start();
    secondListener.Start();
    var firstPort = ((IPEndPoint)firstListener.LocalEndpoint).Port;
    var secondPort = ((IPEndPoint)secondListener.LocalEndpoint).Port;
    using var firstHub = CreateNetworkHub(firstPort);
    using var secondHub = CreateNetworkHub(secondPort);

    var firstAccept = firstListener.AcceptTcpClientAsync();
    var secondAccept = secondListener.AcceptTcpClientAsync();
    firstHub.Start();
    secondHub.Start();
    using var firstPeer = await firstAccept.WaitAsync(TimeSpan.FromSeconds(5));
    using var secondPeer = await secondAccept.WaitAsync(TimeSpan.FromSeconds(5));
    try
    {
        await ConsumeRegistrationAsync(firstPeer.GetStream());
        await ConsumeRegistrationAsync(secondPeer.GetStream());
        await WaitUntilAsync(() => firstHub.GameConnected && secondHub.GameConnected,
            "backend links did not become active");

        const uint connectionId = 0x1234;
        var route = AddRoute(firstHub, connectionId);
        var echoedSource = BuildType20(0x7FFF, connectionId, 0x55667788);
        await firstPeer.GetStream().WriteAsync(echoedSource);
        var echoed = await ReadFrameAsync(firstPeer.GetStream());

        BytesEqual(echoedSource, echoed, "same-link echo bytes");
        await RequireNoDataAsync(secondPeer.GetStream(),
            "type20 crossed into another RunGate link");
        False(route.GameResponses.Reader.TryRead(out _),
            "0x7FFF type20 reached a client route");

        var consumed = BuildType20(0x8000, connectionId, 0x99AABBCC);
        var registration = BuildRegistrationReply(3);
        await firstPeer.GetStream().WriteAsync(Join(consumed, registration));
        await WaitUntilAsync(() => firstHub.RegisteredGateIndex == 3,
            "sticky frame after 0x8000 type20 was not consumed");
        await RequireNoDataAsync(firstPeer.GetStream(),
            "0x8000 type20 was echoed");
        False(route.GameResponses.Reader.TryRead(out _),
            "0x8000 type20 reached a client route");
    }
    finally
    {
        await firstHub.StopAsync();
        await secondHub.StopAsync();
        firstListener.Stop();
        secondListener.Stop();
    }
}

static SharedBackendHub CreateNetworkHub(int gamePort) => new(new GateConfig
{
    GameBackendIP = "127.0.0.1",
    BackendPort = gamePort,
    BackendIP = "127.0.0.1",
    BackendPort2 = 1
}, (_, _) => { });

static SharedBackendRoute AddRoute(SharedBackendHub hub, uint connectionId)
{
    var routeMap = (ConcurrentDictionary<uint, SharedBackendRoute>)
        typeof(SharedBackendHub).GetField("_routes",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hub)!;
    var route = new SharedBackendRoute
    {
        Handle = 9001,
        NativeSessionId = unchecked((ushort)connectionId),
        ConnId = connectionId,
        SessionGeneration = 1,
        // This helper seeds an already-open route; the later Cmd=15 is a
        // parser-tail registration frame and must not synthesize GM_OPEN.
        GameConnectionGeneration = 1,
        ClientIp = "127.0.0.1",
        DbOpenFrame = Array.Empty<byte>(),
        Abort = () => { }
    };
    True(routeMap.TryAdd(connectionId, route), "route seed failed");
    return route;
}

static byte[] BuildType20(int totalLength, uint connectionId = 0x01020304,
    uint context = 0xA0B0C0D0)
{
    if (totalLength < LegacyGateType20.HeaderSize
        || totalLength > LegacyGateType20.MaximumFrameLengthExclusive)
        throw new ArgumentOutOfRangeException(nameof(totalLength));

    var result = new byte[totalLength];
    for (var i = LegacyGateType20.HeaderSize; i < result.Length; i++)
        result[i] = unchecked((byte)(i * 37 + 11));
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0, 4),
        LegacyGateType20.MagicValue);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4, 4), connectionId);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8, 4), context);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(12, 2),
        LegacyGateType20.MessageType);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(14, 2),
        checked((ushort)(totalLength - LegacyGateType20.HeaderSize)));
    return result;
}

static byte[] BuildRegistrationReply(byte gateIndex) => new InternalPacket77
{
    ConnID = gateIndex,
    SeqID = 0,
    Cmd = 15,
    Payload = Array.Empty<byte>()
}.ToBytes();

static byte[] Join(params byte[][] arrays)
{
    var result = new byte[arrays.Sum(array => array.Length)];
    var offset = 0;
    foreach (var array in arrays)
    {
        array.CopyTo(result, offset);
        offset += array.Length;
    }
    return result;
}

static async Task<byte[]> ReadFrameAsync(NetworkStream stream)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var header = new byte[InternalPacket77.HEADER_SIZE];
    await ReadExactlyAsync(stream, header, timeout.Token);
    var bodyLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(14, 2));
    var result = new byte[header.Length + bodyLength];
    header.CopyTo(result, 0);
    if (bodyLength != 0)
        await ReadExactlyAsync(stream,
            result.AsMemory(InternalPacket77.HEADER_SIZE, bodyLength), timeout.Token);
    return result;
}

static async Task ConsumeRegistrationAsync(NetworkStream stream)
{
    var bytes = await ReadFrameAsync(stream);
    var packet = InternalPacket77.FromBytes(bytes, 0, bytes.Length)
        ?? throw new InvalidDataException("registration frame decode failed");
    Equal((ushort)NativeGameGateCommands.GateRegistrationRequest, packet.Cmd,
        "native gate registration command");
    Equal((uint)0, packet.ConnID, "native gate registration connection id");
    Equal(0, packet.Payload.Length, "native gate registration payload");
}

static async Task ReadExactlyAsync(NetworkStream stream, Memory<byte> destination,
    CancellationToken cancellationToken)
{
    var offset = 0;
    while (offset < destination.Length)
    {
        var read = await stream.ReadAsync(destination[offset..], cancellationToken);
        if (read == 0) throw new IOException("backend stream closed during frame read");
        offset += read;
    }
}

static async Task RequireNoDataAsync(NetworkStream stream, string label)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
    var one = new byte[1];
    try
    {
        var read = await stream.ReadAsync(one, timeout.Token);
        if (read != 0) throw new InvalidOperationException(label);
    }
    catch (OperationCanceledException) when (timeout.IsCancellationRequested)
    {
    }
}

static async Task WaitUntilAsync(Func<bool> predicate, string label)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    while (!predicate())
        await Task.Delay(10, timeout.Token);
}

static void BytesEqual(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual,
    string label)
{
    if (!expected.SequenceEqual(actual))
        throw new InvalidOperationException($"{label}: byte sequence differs");
}

static void True(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
}

static void False(bool condition, string label)
{
    if (condition) throw new InvalidOperationException(label);
}

static void NotNull(object value, string label)
{
    if (value == null) throw new InvalidOperationException($"{label}: null");
}

static void Null(object value, string label)
{
    if (value != null) throw new InvalidOperationException($"{label}: not null");
}

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{label}: expected {expected}, got {actual}");
}
