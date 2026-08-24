using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Reflection;
using GameGate.Core;
using SystemModule;
using SystemModule.Packet;

var tests = new (string Name, Action Run)[]
{
    ("native header and payload round-trip", NativeHeaderAndPayloadRoundTrip),
    ("zero-target type19", ZeroTargetType19),
    ("byte-wise fragmented parser", ByteWiseFragmentedParser),
    ("mixed sticky stream", MixedStickyStream),
    ("malformed count resynchronizes", MalformedCountResynchronizes),
    ("duplicate target ids are preserved", DuplicateIdsPreserved),
    ("short client packet is consumed without relay", ShortClientPacketConsumed),
    ("exclusive outer frame boundary", OuterFrameLengthBoundary),
    ("type18 relay length boundary", Type18RelayLengthBoundary),
    ("type19 relay length boundary", Type19RelayLengthBoundary),
    ("hub targets only listed routes", HubTargetsOnlyListedRoutes),
    ("hub rejects handle/index aliases", HubRejectsHandleAndIndexAliases),
    ("session allocator starts at 1000", SessionAllocatorStartsAt1000),
    ("hub skips closed routes", HubSkipsClosedRoutes),
    ("native route context is stable", NativeRouteContextIsStable)
};

foreach (var test in tests)
    test.Run();

Console.WriteLine($"GameGateLegacyType19CompatCheck PASS tests={tests.Length} " +
                  "header=16 count=dword ids=word body=opaque split=bytewise");

static void NativeHeaderAndPayloadRoundTrip()
{
    var ids = new ushort[] { 7, 0x1234, 0xFFFF };
    var body = BuildClientPayload(0x77, 0x00, 0xBB, 0xAA, 0x12, 0x34, 0x00);
    var packet = new LegacyGateType19
    {
        IgnoredConnectionId = 0xAABBCCDD,
        SessionIds = ids,
        ClientPayload = body
    };
    var wire = packet.ToBytes();

    Equal(16 + ids.Length * 2 + body.Length, wire.Length, "wire length");
    Equal(InternalPacket77.MAGIC,
        BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(0, 4)), "magic");
    Equal(0xAABBCCDDu,
        BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(4, 4)), "connection");
    Equal(3u,
        BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(8, 4)), "dword count");
    Equal((ushort)19,
        BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(12, 2)), "type");
    Equal((ushort)(ids.Length * 2 + body.Length),
        BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(14, 2)), "body length");
    Equal((ushort)7,
        BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(16, 2)), "first id");
    Equal((ushort)0x1234,
        BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(18, 2)), "second id");

    var parsed = LegacyGateType19.FromBytes(wire, 0, wire.Length);
    NotNull(parsed, "decode");
    Equal(ids.Length, parsed.SessionIds.Length, "decoded id count");
    for (var i = 0; i < ids.Length; i++)
        Equal(ids[i], parsed.SessionIds[i], $"decoded id {i}");
    BytesEqual(body, parsed.ToClientPayload(), "opaque body");
    BytesEqual(wire, parsed.ToBytes(), "wire round-trip");
}

static void ZeroTargetType19()
{
    var body = BuildClientPayload(1, 2, 3);
    var wire = new LegacyGateType19
    {
        IgnoredConnectionId = 1,
        SessionIds = Array.Empty<ushort>(),
        ClientPayload = body
    }.ToBytes();
    var frames = ParseOnce(wire);
    Equal(1, frames.Count, "zero-target frame count");
    NotNull(frames[0].LegacyType19, "zero-target classification");
    Equal(0, frames[0].LegacyType19.SessionIds.Length, "zero-target ids");
    BytesEqual(body, frames[0].LegacyType19.ClientPayload, "zero-target body");
}

static void ByteWiseFragmentedParser()
{
    var wire = new LegacyGateType19
    {
        SessionIds = new ushort[] { 11, 22 },
        ClientPayload = BuildClientPayload(0x41, 0x77, 0xBB, 0xAA, 0x42)
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
    Equal((ushort)22, frames[0].LegacyType19.SessionIds[1], "fragment id");
}

static void MixedStickyStream()
{
    var type19 = new LegacyGateType19
    {
        SessionIds = new ushort[] { 4 },
        ClientPayload = BuildClientPayload(9, 8)
    }.ToBytes();
    var internalFrame = new InternalPacket77
    {
        Magic = InternalPacket77.MAGIC,
        ConnID = 99,
        SeqID = 100,
        Cmd = 0x4321,
        Payload = new byte[] { 5 }
    }.ToBytes();
    var frames = ParseOnce(Join(type19, internalFrame));
    Equal(2, frames.Count, "mixed count");
    NotNull(frames[0].LegacyType19, "mixed type19");
    NotNull(frames[1].Internal77, "mixed internal");
    Equal(99u, frames[1].Internal77.ConnID, "mixed internal conn");
}

static void MalformedCountResynchronizes()
{
    var malformed = new byte[18];
    BinaryPrimitives.WriteUInt32LittleEndian(malformed.AsSpan(0, 4),
        InternalPacket77.MAGIC);
    BinaryPrimitives.WriteUInt32LittleEndian(malformed.AsSpan(8, 4), 3);
    BinaryPrimitives.WriteUInt16LittleEndian(malformed.AsSpan(12, 2), 19);
    BinaryPrimitives.WriteUInt16LittleEndian(malformed.AsSpan(14, 2), 2);
    var valid = new InternalPacket77
    {
        Magic = InternalPacket77.MAGIC,
        ConnID = 0x55,
        SeqID = 0x66,
        Cmd = 0x77,
        Payload = Array.Empty<byte>()
    }.ToBytes();

    var frames = ParseOnce(Join(malformed, valid));
    Equal(1, frames.Count, "malformed recovery count");
    NotNull(frames[0].Internal77, "malformed recovery type");
    Equal(0x55u, frames[0].Internal77.ConnID, "malformed recovery conn");
}

static void DuplicateIdsPreserved()
{
    var wire = new LegacyGateType19
    {
        SessionIds = new ushort[] { 12, 12, 13 },
        ClientPayload = BuildClientPayload(0xAA)
    }.ToBytes();
    var parsed = LegacyGateType19.FromBytes(wire, 0, wire.Length);
    NotNull(parsed, "duplicate decode");
    Equal(3, parsed.SessionIds.Length, "duplicate count");
    Equal((ushort)12, parsed.SessionIds[0], "duplicate first");
    Equal((ushort)12, parsed.SessionIds[1], "duplicate second");
}

static void ShortClientPacketConsumed()
{
    var packet = new LegacyGateType19
    {
        SessionIds = new ushort[] { 8 },
        ClientPayload = new byte[] { 0xFE }
    };
    var wire = packet.ToBytes();
    var frames = ParseOnce(wire);
    Equal(1, frames.Count, "short packet frame count");
    NotNull(frames[0].LegacyType19, "short packet classification");
    BytesEqual(packet.ClientPayload, frames[0].LegacyType19.ClientPayload,
        "short packet payload");

    using var hub = CreateHub(out var routes, (8u, 8, 0));
    False(hub.TryDispatchLegacyType19(frames[0].LegacyType19),
        "short packet produced relay");
    Equal(0, DrainQueued(routes[8]).Count, "short packet route queue");
}

static void OuterFrameLengthBoundary()
{
    var accepted = new LegacyGateType19
    {
        SessionIds = new ushort[] { 1 },
        ClientPayload = new byte[
            LegacyGateType19.MaximumFrameLengthExclusive
            - LegacyGateType19.HeaderSize - sizeof(ushort) - 1]
    };
    var acceptedWire = accepted.ToBytes();
    Equal(0xFFFF, acceptedWire.Length, "largest accepted total length");
    NotNull(LegacyGateType19.FromBytes(acceptedWire, 0, acceptedWire.Length),
        "largest accepted decode");
    var acceptedFrames = ParseOnce(acceptedWire,
        maximumInternalFrameLength: ushort.MaxValue);
    Equal(1, acceptedFrames.Count, "largest accepted parser count");
    NotNull(acceptedFrames[0].LegacyType19,
        "largest accepted parser classification");

    var rejected = new LegacyGateType19
    {
        SessionIds = new ushort[] { 1 },
        ClientPayload = new byte[
            LegacyGateType19.MaximumFrameLengthExclusive
            - LegacyGateType19.HeaderSize - sizeof(ushort)]
    };
    var didReject = false;
    try
    {
        _ = rejected.ToBytes();
    }
    catch (InvalidOperationException)
    {
        didReject = true;
    }
    True(didReject, "exclusive maximum encoded");

    var rejectedHeader = new byte[LegacyGateType19.HeaderSize];
    BinaryPrimitives.WriteUInt32LittleEndian(rejectedHeader.AsSpan(0, 4),
        InternalPacket77.MAGIC);
    BinaryPrimitives.WriteUInt16LittleEndian(rejectedHeader.AsSpan(12, 2),
        LegacyGateType19.MessageType);
    BinaryPrimitives.WriteUInt16LittleEndian(rejectedHeader.AsSpan(14, 2),
        checked((ushort)(LegacyGateType19.MaximumFrameLengthExclusive
                         - LegacyGateType19.HeaderSize)));
    var tail = new InternalPacket77
    {
        Magic = InternalPacket77.MAGIC,
        ConnID = 91,
        SeqID = 92,
        Cmd = 93,
        Payload = Array.Empty<byte>()
    }.ToBytes();
    var rejectedFrames = ParseOnce(Join(rejectedHeader, tail));
    Equal(0, rejectedFrames.Count, "0x10000 drops current receive buffer");
}

static void Type18RelayLengthBoundary()
{
    using var hub = CreateHub(out var routes, (4001u, 4001, 0));
    Volatile.Write(ref routes[4001].NativePlayerRecog, 1);
    Volatile.Write(ref routes[4001].NativeServerUserIndex, 1);

    var accepted = new LegacyGateType18
    {
        AppendTextTerminator = false,
        TextBytes = new byte[0x7FF3 - LegacyGateType18.ClientPacketSize]
    };
    var acceptedFrames = ParseOnce(accepted.ToBytes());
    Equal(0x8003, accepted.ToBytes().Length, "type18 largest relay outer total");
    True(hub.TryDispatchLegacyType18(acceptedFrames[0].LegacyType18),
        "type18 largest relay dropped");
    Equal(1, DrainQueued(routes[4001]).Count, "type18 largest relay queue");

    var rejected = new LegacyGateType18
    {
        AppendTextTerminator = false,
        TextBytes = new byte[0x7FF4 - LegacyGateType18.ClientPacketSize]
    };
    var rejectedFrames = ParseOnce(rejected.ToBytes());
    Equal(0x8004, rejected.ToBytes().Length, "type18 rejected relay outer total");
    False(hub.TryDispatchLegacyType18(rejectedFrames[0].LegacyType18),
        "type18 0x8000 relay accepted");
    Equal(0, DrainQueued(routes[4001]).Count, "type18 rejected relay queue");
}

static void Type19RelayLengthBoundary()
{
    using var hub = CreateHub(out var routes, (4002u, 4002, 0));
    var accepted = new LegacyGateType19
    {
        SessionIds = new ushort[] { 4002 },
        ClientPayload = new byte[0x7FF3]
    };
    var acceptedFrames = ParseOnce(accepted.ToBytes());
    True(hub.TryDispatchLegacyType19(acceptedFrames[0].LegacyType19),
        "type19 largest relay dropped");
    Equal(1, DrainQueued(routes[4002]).Count, "type19 largest relay queue");

    var rejected = new LegacyGateType19
    {
        SessionIds = new ushort[] { 4002 },
        ClientPayload = new byte[0x7FF4]
    };
    var rejectedFrames = ParseOnce(rejected.ToBytes());
    False(hub.TryDispatchLegacyType19(rejectedFrames[0].LegacyType19),
        "type19 0x8000 relay accepted");
    Equal(0, DrainQueued(routes[4002]).Count, "type19 rejected relay queue");
}

static void HubTargetsOnlyListedRoutes()
{
    using var hub = CreateHub(out var routes,
        (1001u, 1001, 0), (1002u, 1002, 0), (1003u, 1003, 0));
    var packet = new LegacyGateType19
    {
        SessionIds = new ushort[] { 1001, 1003 },
        ClientPayload = BuildClientPayload(4, 5, 6)
    };

    True(hub.TryDispatchLegacyType19(packet), "hub dispatch result");
    var first = DrainQueued(routes[1001]);
    var second = DrainQueued(routes[1002]);
    var third = DrainQueued(routes[1003]);
    Equal(1, first.Count, "listed route 1001 count");
    Equal(0, second.Count, "unlisted route 1002 count");
    Equal(1, third.Count, "listed route 1003 count");
    var routed = first[0];
    Equal(1001u, routed.ConnID, "routed conn id");
    Equal((ushort)Grobal2.GM_DATA, routed.Cmd, "routed command");
    BytesEqual(packet.ClientPayload, routed.Payload, "routed payload");
}

static void HubRejectsHandleAndIndexAliases()
{
    using var hub = CreateHub(out var routes, (2001u, 2001, 77));
    var packet = new LegacyGateType19
    {
        SessionIds = new ushort[] { 77 },
        ClientPayload = BuildClientPayload(9)
    };

    False(hub.TryDispatchLegacyType19(packet), "handle/index alias dispatched");
    var queued = DrainQueued(routes[2001]);
    Equal(0, queued.Count, "handle/index alias queue");
}

static void SessionAllocatorStartsAt1000()
{
    var sessions = new SessionManager(3);
    var first = sessions.Acquire();
    var second = sessions.Acquire();
    NotNull(first, "first native session");
    NotNull(second, "second native session");
    Equal(SessionManager.NativeSessionIdStart, first.NativeSessionId,
        "native session start");
    Equal((ushort)(SessionManager.NativeSessionIdStart + 1), second.NativeSessionId,
        "native session increment");
    True(first.NativeSessionId != second.NativeSessionId,
        "active native session collision");
    True(sessions.Release(first.SessionId, first.Generation),
        "native session release");
}

static void HubSkipsClosedRoutes()
{
    using var hub = CreateHub(out var routes, (3001u, 3001, 0));
    True(routes[3001].TryClose(), "close route");
    var packet = new LegacyGateType19
    {
        SessionIds = new ushort[] { 3001 },
        ClientPayload = BuildClientPayload(1)
    };

    False(hub.TryDispatchLegacyType19(packet), "closed route dispatched");
    Equal(0, DrainQueued(routes[3001]).Count, "closed route queue");
}

static void NativeRouteContextIsStable()
{
    var route = new SharedBackendRoute
    {
        Handle = 9001,
        NativeSessionId = 1001,
        GateIndex = 1,
        ConnId = 1001,
        SessionGeneration = 1,
        ClientIp = "127.0.0.1",
        DbOpenFrame = Array.Empty<byte>(),
        Abort = () => { }
    };

    Equal(0x20000u | 1001u, route.NextSequence(),
        "native gate/session route id");
    Equal(route.NextSequence(), route.NextSequence(),
        "route id changed between frames");
    const uint opaqueContext = 0xDEADBEEFu;
    True(route.BindNativeRouteContext(opaqueContext),
        "native type11 opaque context bind");
    Equal(opaqueContext, route.RouteId,
        "bound native route context");
    True(route.BindNativeRouteContext(opaqueContext),
        "same native type11 context replay");
    False(route.BindNativeRouteContext(0x20000u | 1001u),
        "conflicting native type11 context rebind");
    False(route.BindNativeRouteContext(0), "zero route context accepted");
    False(SharedBackendRoute.ComposeRouteId(1, 1001) ==
          SharedBackendRoute.ComposeRouteId(2, 1001),
        "gate index omitted from route id");
    try
    {
        _ = SharedBackendRoute.ComposeRouteId(0, 1001);
        throw new InvalidOperationException("gate index 0 accepted");
    }
    catch (ArgumentOutOfRangeException) { }
}

static SharedBackendHub CreateHub(out Dictionary<uint, SharedBackendRoute> routes,
    params (uint ConnId, int Handle, int NativeIndex)[] definitions)
{
    var hub = new SharedBackendHub(new GateConfig(), (_, _) => { });
    var routeMap = (ConcurrentDictionary<uint, SharedBackendRoute>)
        typeof(SharedBackendHub).GetField("_routes",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hub)!;
    routes = new Dictionary<uint, SharedBackendRoute>();
    foreach (var definition in definitions)
    {
        var route = new SharedBackendRoute
        {
            Handle = definition.Handle,
            NativeSessionId = unchecked((ushort)definition.ConnId),
            ConnId = definition.ConnId,
            SessionGeneration = 1,
            ClientIp = "127.0.0.1",
            DbOpenFrame = Array.Empty<byte>(),
            Abort = () => { }
        };
        Volatile.Write(ref route.NativeServerUserIndex, definition.NativeIndex);
        routeMap[definition.ConnId] = route;
        routes[definition.ConnId] = route;
    }
    return hub;
}

static List<InternalPacket77> DrainQueued(SharedBackendRoute route)
{
    var packets = new List<InternalPacket77>();
    while (route.GameResponses.Reader.TryRead(out var packet))
        packets.Add(packet);
    return packets;
}

static List<GameGateServerFrame> ParseOnce(byte[] data,
    int maximumInternalFrameLength = GameGateServerFrameParser.NativeMaximumFrameLength)
{
    var parser = new GameGateServerFrameParser(
        maximumInternalFrameLength: maximumInternalFrameLength);
    True(parser.TryAppend(data, 0, data.Length, out var frames, out var error), error);
    Equal(0, parser.BufferedLength, "parser final buffer");
    return frames;
}

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

static byte[] BuildClientPayload(params byte[] bytes)
{
    var result = new byte[Math.Max(LegacyGateType19.ClientPacketSize,
        bytes?.Length ?? 0)];
    if (bytes != null) bytes.CopyTo(result, 0);
    return result;
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

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{label}: expected {expected}, got {actual}");
}
