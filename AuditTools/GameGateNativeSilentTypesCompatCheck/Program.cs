using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using GameGate.Core;
using SystemModule.Packet;

var nativeSilentTypes = new ushort[] { 16, 21, 22, 23 };
foreach (var type in nativeSilentTypes)
    True(SharedBackendHub.IsNativeSilentConsumeType(type),
        $"native silent type {type} was not classified");

foreach (var activeType in new ushort[] { 11, 12, 13, 14, 15, 17, 18, 19, 20, 24 })
    False(SharedBackendHub.IsNativeSilentConsumeType(activeType),
        $"active native type {activeType} was classified silent");

    await SilentFramesAreConsumedWithoutSideEffects(nativeSilentTypes);
    await RegistrationUsesOnlyNativeHeaderField();

    Console.WriteLine("GameGateNativeSilentTypesCompatCheck PASS checks=17 " +
                      "types=16,21,22,23 action=consume-only sticky=preserved registration=header-only");

static async Task SilentFramesAreConsumedWithoutSideEffects(
    IReadOnlyList<ushort> nativeSilentTypes)
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    using var hub = new SharedBackendHub(new GateConfig
    {
        GameBackendIP = "127.0.0.1",
        BackendPort = port,
        BackendIP = "127.0.0.1",
        BackendPort2 = 1
    }, (_, _) => { });

    var accept = listener.AcceptTcpClientAsync();
    hub.Start();
    using var peer = await accept.WaitAsync(TimeSpan.FromSeconds(5));
    try
    {
        await WaitUntilAsync(() => hub.GameConnected,
            "backend link did not become active");
        await ConsumeRegistrationAsync(peer.GetStream());

        const uint connectionId = 0x1234;
        var route = AddRoute(hub, connectionId);
        var frames = nativeSilentTypes.Select((type, index) =>
            new InternalPacket77
            {
                ConnID = connectionId,
                SeqID = unchecked(0xA0B00000u + (uint)index),
                Cmd = type,
                Payload = new byte[]
                {
                    (byte)type, 0x77, 0xBB, 0xAA, 0x33, 0, 0xFF
                }
            }.ToBytes()).ToList();
        frames.Add(new InternalPacket77
        {
            ConnID = 5,
            SeqID = 0,
            Cmd = 15,
            Payload = Array.Empty<byte>()
        }.ToBytes());

        var sticky = Join(frames);
        await WriteFragmentedAsync(peer.GetStream(), sticky);
        await WaitUntilAsync(() => hub.RegisteredGateIndex == 5,
            "sticky registration tail was not parsed");

        False(route.GameResponses.Reader.TryRead(out _),
            "silent native frame reached a client route");
        False(route.IsClosed || route.IsInvalidated,
            "silent native frame changed route lifecycle");
        await RequireNoDataAsync(peer.GetStream(),
            "silent native frame produced a backend response");
    }
    finally
    {
        await hub.StopAsync();
        listener.Stop();
    }
}

static async Task RegistrationUsesOnlyNativeHeaderField()
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    using var hub = new SharedBackendHub(new GateConfig
    {
        GameBackendIP = "127.0.0.1",
        BackendPort = port,
        BackendIP = "127.0.0.1",
        BackendPort2 = 1
    }, (_, _) => { });

    var accept = listener.AcceptTcpClientAsync();
    hub.Start();
    using var peer = await accept.WaitAsync(TimeSpan.FromSeconds(5));
    try
    {
        await WaitUntilAsync(() => hub.GameConnected,
            "registration backend link did not become active");
        await ConsumeRegistrationAsync(peer.GetStream());

        // Native RunGate type 15 stores only byte(frame+4).  A body byte must
        // not become a fallback registration value when that header byte is 0.
        var packet = new InternalPacket77
        {
            ConnID = 0,
            SeqID = 0x55,
            Cmd = 15,
            Payload = new byte[] { 7 }
        };
        await peer.GetStream().WriteAsync(packet.ToBytes());
        await Task.Delay(100);
        True(hub.RegisteredGateIndex == 0,
            "type-15 body byte was incorrectly used as gate index");
    }
    finally
    {
        await hub.StopAsync();
        listener.Stop();
    }
}

static async Task ConsumeRegistrationAsync(NetworkStream stream)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var header = new byte[InternalPacket77.HEADER_SIZE];
    await ReadExactlyAsync(stream, header, timeout.Token);
    var bodyLength = BitConverter.ToUInt16(header, 14);
    var bytes = new byte[InternalPacket77.HEADER_SIZE + bodyLength];
    header.CopyTo(bytes, 0);
    if (bodyLength > 0)
        await ReadExactlyAsync(stream,
            bytes.AsMemory(InternalPacket77.HEADER_SIZE, bodyLength),
            timeout.Token);
    var packet = InternalPacket77.FromBytes(bytes, 0, bytes.Length)
        ?? throw new InvalidDataException("registration frame decode failed");
    True(packet.Cmd == NativeGameGateCommands.GateRegistrationRequest,
        "native gate registration command");
    True(packet.ConnID == 0, "native gate registration connection id");
    True(packet.Payload.Length == 0, "native gate registration payload");
}

static async Task ReadExactlyAsync(NetworkStream stream, Memory<byte> destination,
    CancellationToken cancellationToken)
{
    var offset = 0;
    while (offset < destination.Length)
    {
        var read = await stream.ReadAsync(destination[offset..],
            cancellationToken);
        if (read <= 0) throw new EndOfStreamException();
        offset += read;
    }
}

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
        // The test injects an already-open logical route.  Cmd=15 below is
        // only the parser tail; it must not synthesize a second GM_OPEN.
        GameConnectionGeneration = 1,
        ClientIp = "127.0.0.1",
        DbOpenFrame = Array.Empty<byte>(),
        Abort = () => { }
    };
    True(routeMap.TryAdd(connectionId, route), "route seed failed");
    return route;
}

static async Task WriteFragmentedAsync(NetworkStream stream, byte[] source)
{
    var widths = new[] { 1, 2, 3, 5, 8, 13 };
    var offset = 0;
    var widthIndex = 0;
    while (offset < source.Length)
    {
        var count = Math.Min(widths[widthIndex++ % widths.Length],
            source.Length - offset);
        await stream.WriteAsync(source.AsMemory(offset, count));
        offset += count;
    }
}

static byte[] Join(IEnumerable<byte[]> arrays)
{
    var parts = arrays.ToArray();
    var result = new byte[parts.Sum(part => part.Length)];
    var offset = 0;
    foreach (var part in parts)
    {
        part.CopyTo(result, offset);
        offset += part.Length;
    }
    return result;
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

static void True(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
}

static void False(bool condition, string label)
{
    if (condition) throw new InvalidOperationException(label);
}
