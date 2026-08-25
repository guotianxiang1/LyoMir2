using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
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
    ("native route context is stable", NativeRouteContextIsStable),
    ("DB mixed text and native-control stream", DbMixedStream),
    ("DB native-control parser resynchronizes", DbNativeControlResynchronizes),
    ("DB controls use native session id", DbControlsUseNativeSessionId)
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

static void DbMixedStream()
{
    var firstText = Encoding.ASCII.GetBytes("%101/#first!$");
    var open = BuildDbControl(0x2425, 77, 11);
    var routeClear = BuildDbControl(0x2425, 0, 12);
    var context = BuildDbControl(0x2425, -999, 21);
    var reserved = BuildDbControl(0x2425, 0x11223344, 19,
        new byte[] { 0x77, 0xBB, 0xAA, 0x33, (byte)'%', (byte)'$' });
    var secondText = new byte[]
    {
        (byte)'%', (byte)'1', (byte)'0', (byte)'2', (byte)'/',
        0x77, 0xBB, 0xAA, 0x33, (byte)'$'
    };
    var stream = Join(firstText, open, routeClear, context, reserved,
        secondText);

    for (var split = 0; split <= stream.Length; split++)
    {
        var parser = new DbServerGatewayFrameParser();
        var parsed = new List<DbServerGatewayFrame>();
        True(parser.TryAppend(stream, 0, split, out var first, out var error),
            error);
        parsed.AddRange(first);
        True(parser.TryAppend(stream, split, stream.Length - split,
            out var second, out error), error);
        parsed.AddRange(second);
        AssertDbMixedFrames(parsed, firstText, open, routeClear, context,
            reserved, secondText);
        Equal(0, parser.BufferedLength,
            $"DB mixed parser final buffer at split {split}");
    }

    var byteParser = new DbServerGatewayFrameParser();
    var byteParsed = new List<DbServerGatewayFrame>();
    for (var i = 0; i < stream.Length; i++)
    {
        True(byteParser.TryAppend(stream, i, 1, out var frames,
            out var error), error);
        byteParsed.AddRange(frames);
    }
    AssertDbMixedFrames(byteParsed, firstText, open, routeClear, context,
        reserved, secondText);
    Equal(0, byteParser.BufferedLength, "byte-split DB parser final buffer");
}

static void AssertDbMixedFrames(List<DbServerGatewayFrame> parsed,
    byte[] firstText, byte[] open, byte[] routeClear, byte[] context,
    byte[] reserved, byte[] secondText)
{
    Equal(6, parsed.Count, "DB mixed frame count");
    Equal(DbServerGatewayFrameKind.PercentDollar, parsed[0].Kind,
        "first DB frame kind");
    BytesEqual(firstText, parsed[0].Data, "first DB text frame");
    Equal(DbServerGatewayFrameKind.NativeControl, parsed[1].Kind,
        "DB open-control kind");
    Equal((ushort)11, parsed[1].Command, "DB open-control command");
    Equal(77, parsed[1].Parameter, "DB open-control parameter");
    BytesEqual(open, parsed[1].Data, "DB open-control bytes");
    Equal((ushort)12, parsed[2].Command, "DB route-clear command");
    Equal(0x2425u, parsed[2].ConnectionId, "DB route-clear session id");
    BytesEqual(routeClear, parsed[2].Data, "DB route-clear bytes");
    Equal((ushort)21, parsed[3].Command, "DB context-control command");
    Equal(-999, parsed[3].Parameter, "DB context-control parameter");
    BytesEqual(context, parsed[3].Data, "DB context-control bytes");
    Equal((ushort)19, parsed[4].Command, "DB reserved-control command");
    BytesEqual(reserved, parsed[4].Data, "DB reserved-control bytes");
    BytesEqual(new byte[] { 0x77, 0xBB, 0xAA, 0x33, (byte)'%', (byte)'$' },
        parsed[4].Payload, "DB reserved-control payload");
    Equal(DbServerGatewayFrameKind.PercentDollar, parsed[5].Kind,
        "second DB frame kind");
    BytesEqual(secondText, parsed[5].Data,
        "text body containing native magic");
}

static void DbNativeControlResynchronizes()
{
    var variableRouteClear = BuildDbControl(1001, 123, 12,
        new byte[] { 0xAA, 0xBB });
    var reserved = BuildDbControl(1001, -7, 20,
        new byte[] { 1, 2, 3 });
    var stream = Join(new byte[] { 0x01, 0x77, 0x00, 0x02 },
        variableRouteClear, new byte[] { 0x77, 0xBB, 0x01 }, reserved);
    var parser = new DbServerGatewayFrameParser();
    True(parser.TryAppend(stream, 0, stream.Length, out var frames,
        out var error), error);
    Equal(2, frames.Count, "resynchronized DB control frame count");
    Equal((ushort)12, frames[0].Command,
        "variable route-clear command");
    Equal(123, frames[0].Parameter, "variable route-clear parameter");
    BytesEqual(new byte[] { 0xAA, 0xBB }, frames[0].Payload,
        "variable route-clear payload");
    Equal((ushort)20, frames[1].Command, "reserved DB command");
    Equal(-7, frames[1].Parameter, "reserved DB parameter");
    Equal(0, parser.BufferedLength, "resynchronized DB parser buffer");

    var partial = BuildDbControl(1002, 0, 21);
    parser = new DbServerGatewayFrameParser();
    True(parser.TryAppend(partial, 0, 2, out frames, out error), error);
    Equal(0, frames.Count, "partial native magic emitted a frame");
    Equal(2, parser.BufferedLength, "partial native magic was discarded");
    True(parser.TryAppend(partial, 2, partial.Length - 2, out frames,
        out error), error);
    Equal(1, frames.Count, "completed native control was not emitted");
    Equal((ushort)21, frames[0].Command,
        "completed native control command");

    var oversized = new byte[YbDbLegacy77Codec.HeaderSize];
    BinaryPrimitives.WriteUInt32LittleEndian(oversized,
        YbDbLegacy77Codec.FrameMagic);
    BinaryPrimitives.WriteUInt16LittleEndian(oversized.AsSpan(14, 2),
        ushort.MaxValue);
    var afterOversized = BuildDbControl(1003, 0, 17);
    var recovered = Join(oversized, afterOversized);
    parser = new DbServerGatewayFrameParser();
    True(parser.TryAppend(recovered, 0, recovered.Length, out frames,
        out error), error);
    Equal(0, frames.Count,
        "oversized native envelope did not discard the receive buffer");
    Equal(0, parser.BufferedLength,
        "oversized native envelope left buffered bytes");
    True(parser.TryAppend(afterOversized, 0, afterOversized.Length,
        out frames, out error), error);
    Equal(1, frames.Count,
        "parser did not resume after an oversized receive buffer");
    Equal((ushort)17, frames[0].Command,
        "post-reset native command");
}

static void DbControlsUseNativeSessionId()
{
    using var hub = CreateHub(out var routes,
        (1001u, 9001, 11), (1002u, 9002, 22));
    Volatile.Write(ref routes[1001].NativePlayerRecog, 101);
    Volatile.Write(ref routes[1002].NativePlayerRecog, 102);

    var open = ParseDbControl(BuildDbControl(1001, 77, 11));
    True(DispatchDbControl(hub, open), "native DB open control");
    Equal(0, Volatile.Read(ref routes[1001].NativePlayerRecog),
        "DB open did not clear route player recog");
    Equal(0, Volatile.Read(ref routes[1001].NativeServerUserIndex),
        "DB open did not clear route server-user index");
    Equal(77, Volatile.Read(ref routes[1001].NativeDbRouteContext),
        "DB open result was not stored");
    True(routes[1001].DbResponses.Reader.TryRead(out var loginPrompt),
        "DB open did not queue the native 4003 prompt");
    var expectedPrompt = EDcode.EncodeMessage(
        Grobal2.MakeDefaultMsg(Grobal2.SM_LOGIN, 0, 0, 0, 0));
    while (expectedPrompt.Length < Grobal2.DEFBLOCKSIZE)
        expectedPrompt += "0";
    BytesEqual(HUtil32.GetBytes($"%1001/#{expectedPrompt}!$"), loginPrompt,
        "native DB open 4003 prompt");
    Equal(102, Volatile.Read(ref routes[1002].NativePlayerRecog),
        "DB open changed unrelated route player recog");
    Equal(22, Volatile.Read(ref routes[1002].NativeServerUserIndex),
        "DB open changed unrelated route server-user index");

    Volatile.Write(ref routes[1002].NativePlayerRecog, 202);
    var context = ParseDbControl(BuildDbControl(1002, -999, 21));
    True(DispatchDbControl(hub, context), "native DB context control");
    Equal(0, Volatile.Read(ref routes[1002].NativePlayerRecog),
        "DB context did not clear player recog");
    Equal(0, Volatile.Read(ref routes[1002].NativeServerUserIndex),
        "DB context did not clear server-user index");
    Equal(-999, Volatile.Read(ref routes[1002].NativeDbRouteContext),
        "DB context sentinel was not stored");

    context = ParseDbControl(BuildDbControl(1001, 88, 21));
    True(DispatchDbControl(hub, context),
        "native DB context overwrite control");
    Equal(88, Volatile.Read(ref routes[1001].NativeDbRouteContext),
        "commands 11 and 21 did not overwrite the same native field");

    Volatile.Write(ref routes[1001].NativePlayerRecog, 301);
    Volatile.Write(ref routes[1001].NativeServerUserIndex, 31);
    var prior = Encoding.ASCII.GetBytes("%1001/#prior!$");
    True(routes[1001].DbResponses.Writer.TryWrite(prior),
        "route-clear prior response setup");
    var routeClear = ParseDbControl(BuildDbControl(1001, 0, 12));
    True(DispatchDbControl(hub, routeClear), "native session route clear");
    Equal(301, Volatile.Read(ref routes[1001].NativePlayerRecog),
        "DB route clear changed M2 player-recog state");
    Equal(31, Volatile.Read(ref routes[1001].NativeServerUserIndex),
        "DB route clear changed M2 server-user state");
    True(routes[1001].IsDbTerminationPending,
        "DB route clear did not set the termination flag");
    True(routes[1001].DbResponses.Reader.TryRead(out var queuedPrior),
        "route-clear lost the prior 4040-equivalent response");
    BytesEqual(prior, queuedPrior,
        "route-clear reordered the prior client response");
    True(routes[1001].DbResponses.Reader.TryRead(out var terminate)
         && terminate.Length == 0,
        "route-clear did not queue the terminal sentinel last");
    False(DispatchDbControl(hub, ParseDbControl(
            BuildDbControl(9001, 0, 12))),
        "OS handle alias cleared a route");
    False(routes[1001].IsClosed,
        "DB dispatcher closed the route before queued responses drained");
    Equal(0, DrainQueued(routes[1001]).Count,
        "DB route clear produced an ACK or game packet");

    var unknown = ParseDbControl(BuildDbControl(1001, 0, 19));
    True(DispatchDbControl(hub, unknown),
        "valid reserved DB control was treated as a link failure");

    var saturatedPayload = new byte[] { 0x5A };
    for (var i = 0; i < 256; i++)
        True(routes[1002].DbResponses.Writer.TryWrite(saturatedPayload),
            $"DB response saturation setup {i}");
    var saturatedClear = ParseDbControl(BuildDbControl(1002, 0, 12));
    var pendingClear = hub.DispatchDbControlAsync(saturatedClear).AsTask();
    False(pendingClear.IsCompleted,
        "full DB response queue caused immediate route termination");
    True(routes[1002].DbResponses.Reader.TryRead(out var firstQueued),
        "full DB response queue could not drain its first response");
    BytesEqual(saturatedPayload, firstQueued,
        "route termination overtook a saturated response queue");
    True(pendingClear.GetAwaiter().GetResult(),
        "route termination sentinel was not queued after capacity returned");
    var remainingResponses = 0;
    var sawTerminal = false;
    while (routes[1002].DbResponses.Reader.TryRead(out var queued))
    {
        if (queued.Length == 0) sawTerminal = true;
        else remainingResponses++;
    }
    Equal(255, remainingResponses,
        "saturated route lost queued responses before termination");
    True(sawTerminal,
        "saturated route did not place termination after queued responses");
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

static DbServerGatewayFrame ParseDbControl(byte[] wire)
{
    var parser = new DbServerGatewayFrameParser();
    True(parser.TryAppend(wire, 0, wire.Length, out var frames,
        out var error), error);
    Equal(1, frames.Count, "single DB control parse count");
    Equal(DbServerGatewayFrameKind.NativeControl, frames[0].Kind,
        "single DB control kind");
    return frames[0];
}

static bool DispatchDbControl(SharedBackendHub hub,
    DbServerGatewayFrame frame) =>
    hub.DispatchDbControlAsync(frame).AsTask().GetAwaiter().GetResult();

static byte[] BuildDbControl(uint connectionId, int parameter,
    ushort command, byte[] payload = null)
{
    payload ??= Array.Empty<byte>();
    var frame = new byte[YbDbLegacy77Codec.HeaderSize + payload.Length];
    BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4),
        YbDbLegacy77Codec.FrameMagic);
    BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4, 4), connectionId);
    BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(8, 4), parameter);
    BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(12, 2), command);
    BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(14, 2),
        checked((ushort)payload.Length));
    payload.CopyTo(frame, YbDbLegacy77Codec.HeaderSize);
    return frame;
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
