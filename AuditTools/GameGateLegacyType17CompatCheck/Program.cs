using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using GameGate.Core;
using SystemModule.Packet;

var tests = new (string Name, Action Run)[]
{
    ("native header round-trip", NativeHeaderRoundTrip),
    ("forward rewrite changes only type", ForwardRewriteChangesOnlyType),
    ("byte-wise fragmented parser", ByteWiseFragmentedParser),
    ("mixed sticky stream", MixedStickyStream),
    ("outer frame boundary", OuterFrameBoundary),
    ("forward frame boundary", ForwardFrameBoundary),
    ("broadcast excludes source", BroadcastExcludesSource),
    ("positive target includes every matching gate", PositiveTargetIncludesMatches),
    ("positive self target is retained", PositiveSelfTarget),
    ("negative target is consumed", NegativeTargetIsConsumed)
};

foreach (var test in tests)
    test.Run();

await CrossHubLoopbackRouting();

Console.WriteLine($"GameGateLegacyType17CompatCheck PASS tests={tests.Length + 1} " +
                  "header=16 type=17->7 outer<0x10000 relay<0x8000");

static void NativeHeaderRoundTrip()
{
    var packet = new LegacyGateType17
    {
        ConnectionId = 0xA1B2C3D4,
        TargetGate = 0x11223344,
        Payload = new byte[] { 0x77, 0xBB, 0xAA, 0x33, 0x00, 0xFF }
    };
    var wire = packet.ToBytes();

    Equal(22, wire.Length, "wire length");
    Equal(InternalPacket77.MAGIC,
        BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(0, 4)), "magic");
    Equal(packet.ConnectionId,
        BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(4, 4)), "field4");
    Equal(packet.TargetGate,
        BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(8, 4)), "field8");
    Equal(LegacyGateType17.MessageType,
        BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(12, 2)), "type");
    Equal((ushort)packet.Payload.Length,
        BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(14, 2)), "body length");

    var parsed = LegacyGateType17.FromBytes(wire, 0, wire.Length);
    NotNull(parsed, "decode");
    Equal(packet.ConnectionId, parsed.ConnectionId, "decoded field4");
    Equal(packet.TargetGate, parsed.TargetGate, "decoded field8");
    BytesEqual(packet.Payload, parsed.Payload, "decoded payload");
    BytesEqual(wire, parsed.ToBytes(), "wire round-trip");
}

static void ForwardRewriteChangesOnlyType()
{
    var packet = new LegacyGateType17
    {
        ConnectionId = 0xDEADBEEF,
        TargetGate = 3,
        Payload = Enumerable.Range(0, 31).Select(i => (byte)(i * 7)).ToArray()
    };
    var source = packet.ToBytes();
    var forwarded = packet.ToForwardedBytes();
    Equal(source.Length, forwarded.Length, "forward length");
    for (var i = 0; i < source.Length; i++)
    {
        if (i is 12 or 13) continue;
        Equal(source[i], forwarded[i], $"preserved byte {i}");
    }
    Equal(LegacyGateType17.ForwardedMessageType,
        BinaryPrimitives.ReadUInt16LittleEndian(forwarded.AsSpan(12, 2)),
        "forwarded type");
}

static void ByteWiseFragmentedParser()
{
    var wire = new LegacyGateType17
    {
        ConnectionId = 7,
        TargetGate = 2,
        Payload = new byte[] { 1, 2, 3, 4, 5 }
    }.ToBytes();
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
    NotNull(frames[0].LegacyType17, "fragment classification");
    Null(frames[0].Internal77, "fragment generic classification");
}

static void MixedStickyStream()
{
    var type17 = new LegacyGateType17
    {
        ConnectionId = 8,
        TargetGate = 0,
        Payload = new byte[] { 9, 10 }
    }.ToBytes();
    var ordinary = new InternalPacket77
    {
        ConnID = 99,
        SeqID = 100,
        Cmd = 0x4321,
        Payload = new byte[] { 5 }
    }.ToBytes();
    var parser = new GameGateServerFrameParser();
    var joined = type17.Concat(ordinary).ToArray();
    True(parser.TryAppend(joined, 0, joined.Length, out var frames, out var error),
        error);
    Equal(2, frames.Count, "sticky frame count");
    NotNull(frames[0].LegacyType17, "sticky type17");
    NotNull(frames[1].Internal77, "sticky ordinary");
    Equal(99u, frames[1].Internal77.ConnID, "sticky ordinary field4");
}

static void OuterFrameBoundary()
{
    var accepted = new LegacyGateType17
    {
        Payload = new byte[LegacyGateType17.MaximumFrameLengthExclusive
                           - LegacyGateType17.HeaderSize - 1]
    };
    var wire = accepted.ToBytes();
    Equal(0xFFFF, wire.Length, "largest outer frame");
    NotNull(LegacyGateType17.FromBytes(wire, 0, wire.Length),
        "largest outer decode");

    var rejected = new LegacyGateType17
    {
        Payload = new byte[LegacyGateType17.MaximumFrameLengthExclusive
                           - LegacyGateType17.HeaderSize]
    };
    Throws<InvalidOperationException>(() => rejected.ToBytes(),
        "0x10000 outer frame accepted");
}

static void ForwardFrameBoundary()
{
    var accepted = new LegacyGateType17
    {
        Payload = new byte[LegacyGateType17.MaximumForwardedFrameLengthExclusive
                           - LegacyGateType17.HeaderSize - 1]
    };
    True(accepted.CanForward, "0x7FFF frame rejected");
    Equal(0x7FFF, accepted.TotalLength, "largest forwarded total");

    var consumed = new LegacyGateType17
    {
        Payload = new byte[LegacyGateType17.MaximumForwardedFrameLengthExclusive
                           - LegacyGateType17.HeaderSize]
    };
    False(consumed.CanForward, "0x8000 frame forwarded");
}

static void BroadcastExcludesSource()
{
    var packet = new LegacyGateType17 { TargetGate = 0 };
    False(packet.ShouldForwardTo(1, true), "broadcast reached source");
    True(packet.ShouldForwardTo(0, false), "broadcast skipped unassigned peer");
    True(packet.ShouldForwardTo(7, false), "broadcast skipped assigned peer");
}

static void PositiveTargetIncludesMatches()
{
    var packet = new LegacyGateType17 { TargetGate = 7 };
    True(packet.ShouldForwardTo(7, false), "first matching peer skipped");
    True(packet.ShouldForwardTo(7, false), "duplicate matching peer skipped");
    False(packet.ShouldForwardTo(6, false), "wrong peer selected");
}

static void PositiveSelfTarget()
{
    var packet = new LegacyGateType17 { TargetGate = 3 };
    True(packet.ShouldForwardTo(3, true), "positive self target excluded");
}

static void NegativeTargetIsConsumed()
{
    var packet = new LegacyGateType17 { TargetGate = 0xFFFFFFFF };
    False(packet.ShouldForwardTo(0xFF, false), "negative target matched byte");
    False(packet.ShouldForwardTo(1, true), "negative target matched source");
}

static async Task CrossHubLoopbackRouting()
{
    var firstListener = new TcpListener(IPAddress.Loopback, 0);
    var secondListener = new TcpListener(IPAddress.Loopback, 0);
    firstListener.Start();
    secondListener.Start();
    var firstPort = ((IPEndPoint)firstListener.LocalEndpoint).Port;
    var secondPort = ((IPEndPoint)secondListener.LocalEndpoint).Port;
    using var firstHub = new SharedBackendHub(new GateConfig
    {
        GameBackendIP = "127.0.0.1",
        BackendPort = firstPort,
        BackendIP = "127.0.0.1",
        BackendPort2 = 1
    }, (_, _) => { });
    using var secondHub = new SharedBackendHub(new GateConfig
    {
        GameBackendIP = "127.0.0.1",
        BackendPort = secondPort,
        BackendIP = "127.0.0.1",
        BackendPort2 = 1
    }, (_, _) => { });

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
        await firstPeer.GetStream().WriteAsync(BuildRegistrationReply(1));
        await secondPeer.GetStream().WriteAsync(BuildRegistrationReply(2));
        await WaitUntilAsync(() => firstHub.RegisteredGateIndex == 1
                                   && secondHub.RegisteredGateIndex == 2,
            "registration replies were not applied");

        var source = new LegacyGateType17
        {
            ConnectionId = 0xCAFEBABE,
            TargetGate = 2,
            Payload = new byte[] { 0x41, 0x77, 0xBB, 0xAA, 0x33, 0x42 }
        };
        await firstPeer.GetStream().WriteAsync(source.ToBytes());
        var forwarded = await ReadFrameAsync(secondPeer.GetStream());
        BytesEqual(source.ToForwardedBytes(), forwarded,
            "cross-hub forwarded frame");
        await RequireNoFrameAsync(firstPeer.GetStream(),
            "targeted frame echoed to source hub");
    }
    finally
    {
        await firstHub.StopAsync();
        await secondHub.StopAsync();
        firstListener.Stop();
        secondListener.Stop();
    }
}

static byte[] BuildRegistrationReply(byte gateIndex) => new InternalPacket77
{
    ConnID = gateIndex,
    SeqID = 0,
    Cmd = 15,
    Payload = Array.Empty<byte>()
}.ToBytes();

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

static async Task RequireNoFrameAsync(NetworkStream stream, string label)
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
    {
        await Task.Delay(10, timeout.Token);
    }
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

static void Throws<T>(Action action, string label) where T : Exception
{
    try
    {
        action();
    }
    catch (T)
    {
        return;
    }
    throw new InvalidOperationException(label);
}
