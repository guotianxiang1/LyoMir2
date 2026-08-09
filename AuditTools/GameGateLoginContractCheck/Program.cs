using System.Text;
using SystemModule;
using SystemModule.Packet;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

VerifyStartPlayStatePrecedesClientWrite();
VerifyLoginNoticeOkIsSingleSuccessfulSend();
VerifyLoginCertificationRecogIsZero();
VerifyMarkerDataCommand23PreservesRawBody();
VerifyGameDataUsesSupportedEnvelopeCommand();
VerifyLoginFlushKeepsFramesOrdered();
VerifyGameDataFrameLimit();

Console.WriteLine("GameGateLoginContractCheck PASS checks=7");

static void VerifyStartPlayStatePrecedesClientWrite()
{
    var relayDown = GateServerSection("async Task RelayDown()", "async Task RelayGameSvr()");
    var updatePlayer = Position(relayDown,
        "UpdatePlayerState(session, decodedHeader, body);");
    var startPlayState = Position(relayDown,
        "if (decodedHeader.Ident == SM_STARTPLAY)", updatePlayer);
    var clear = Position(relayDown, "bufferedGameFrames.Clear();", startPlayState);
    var wait = Position(relayDown, "waitClientMainReady = true;", clear);
    var notReady = Position(relayDown, "clientMainReady = false;", wait);
    var injectionAvailable = Position(relayDown, "injectedNoticeOk = false;", notReady);
    var clientWrite = Position(relayDown,
        "await WriteClientMobileFrame(frame);", injectionAvailable);

    InOrder("SM_STARTPLAY state must be armed before the frame reaches the client",
        updatePlayer, startPlayState, clear, wait, notReady, injectionAvailable, clientWrite);
}

static void VerifyLoginNoticeOkIsSingleSuccessfulSend()
{
    var source = GateServerSource();
    var helper = Section(source,
        "async Task<bool> SendLoginNoticeOkOnce", "async Task QueryCharactersAfterSoftClose");

    var acquireLock = Position(helper,
        "await loginNoticeWriteLock.WaitAsync(cts.Token);");
    var duplicateCheck = Position(helper,
        "if (injectedNoticeOk) return false;", acquireLock);
    var gameWrite = Position(helper, "await WriteGameSvr(frame, cts.Token);", duplicateCheck);
    var commitSuccess = Position(helper, "injectedNoticeOk = true;", gameWrite);
    var releaseLock = Position(helper,
        "loginNoticeWriteLock.Release();", commitSuccess);
    InOrder("login certification must serialize duplicate check, write, and commit",
        acquireLock, duplicateCheck, gameWrite, commitSuccess, releaseLock);

    Equal(1, Count(source,
            "SendLoginNoticeOkOnce(pkt.ToBytes(), \"client 1018\")"),
        "client 1018 send path count");
    Equal(1, Count(source,
            "SendLoginNoticeOkOnce(pkt.ToBytes(), \"SM_STARTPLAY injection\")"),
        "SM_STARTPLAY injection send path count");
}

static void VerifyLoginCertificationRecogIsZero()
{
    var relayUp = GateServerSection("async Task RelayUp()", "static byte[] Enc6Body");
    var client1018 = Position(relayUp, "if (fwdIdent == 1018)");
    var createPacket = Position(relayUp,
        "var cp = CreateGameSvrClientPacket(mf.Inner, fwdIdent);", client1018);
    var zeroRecog = Position(relayUp, "cp.Recog = 0;", createPacket);
    var serializePacket = Position(relayUp,
        "Buffer.BlockCopy(cp.GetBuffer()", zeroRecog);
    InOrder("client 1018 certification must serialize Recog=0",
        createPacket, zeroRecog, serializePacket);

    var relayDown = GateServerSection("async Task RelayDown()", "async Task RelayGameSvr()");
    var injection = Position(relayDown,
        "SendLoginNoticeOkOnce(pkt.ToBytes(), \"SM_STARTPLAY injection\")");
    var injectionBlockStart = relayDown.LastIndexOf(
        "if (decodedHeader.Ident == SM_STARTPLAY)", injection,
        StringComparison.Ordinal);
    True(injectionBlockStart >= 0,
        "SM_STARTPLAY certification block was not found");
    var injectionBlock = relayDown.Substring(injectionBlockStart,
        injection - injectionBlockStart);
    Require(injectionBlock,
        "new ClientPacket { Recog = 0, Ident = 1018",
        "injected login certification does not force Recog=0");
}

static void VerifyMarkerDataCommand23PreservesRawBody()
{
    var rawBody = new byte[]
    {
        0x00, 0xFF, 0x01, 0x7F, 0x80, 0x23, 0x40, 0x5E, 0xC3, 0x28
    };
    var inner = new MobileCodec.InnerHeader
    {
        Recog = unchecked((int)0x89ABCDEF),
        Ident = 1018,
        Param = 0x1234,
        Tag = 0x5678,
        Series = 0x9ABC
    };

    var wire = MobileCodec.WriteFrame(inner, rawBody, 0x10203040,
        MobileCodec.MARKER_DATA);
    True(MobileCodec.TryReadFrame(wire, 0, wire.Length,
            out var parsed, out var consumed),
        "MARKER_DATA sample did not parse");
    Equal(wire.Length, consumed, "MARKER_DATA consumed length");
    Equal(MobileCodec.MARKER_DATA, parsed.Header.Marker,
        "MARKER_DATA marker");
    Equal((byte)0x17, parsed.Header.Cmd,
        "MARKER_DATA overlapping Header.Cmd byte");
    BytesEqual(rawBody, parsed.Body,
        "Header.Cmd=23 ordinary MARKER_DATA body must remain raw");

    var relayUp = GateServerSection("async Task RelayUp()", "static byte[] Enc6Body");
    NotContains(relayUp, "Decode6BitBufDirect",
        "RelayUp must not 6-bit decode ordinary MARKER_DATA bodies");
    NotContains(relayUp, "mf.Header.Cmd == 23",
        "RelayUp must not interpret MARKER_DATA's overlapping Cmd byte as an encoding flag");
}

static void VerifyGameDataUsesSupportedEnvelopeCommand()
{
    var source = GateServerSource();
    NotContains(source, "(ushort)0x275",
        "GateServer must not send an unsupported speed command as an outer M2 command");
    NotContains(source, "(ushort)0x276",
        "GateServer must not send an unsupported warning command as an outer M2 command");
}

static void VerifyLoginFlushKeepsFramesOrdered()
{
    var relayUp = GateServerSection("async Task RelayUp()", "static byte[] Enc6Body");
    NotContains(relayUp, "clientMainReady = true;",
        "client 1018 must not switch live delivery before buffered frames are flushed");

    var flush = GateServerSection("async Task FlushBufferedGameFrames",
        "async Task RelayUp()");
    var loop = Position(flush, "while (true)");
    var empty = Position(flush, "if (bufferedGameFrames.Count == 0)", loop);
    var ready = Position(flush, "clientMainReady = true;", empty);
    var stopBuffering = Position(flush, "waitClientMainReady = false;", ready);
    var exit = Position(flush, "return;", stopBuffering);
    var takeBatch = Position(flush, "frames = bufferedGameFrames.ToList();", exit);
    var sendBatch = Position(flush, "await WriteClientMobileFrame(frameInfo.Frame);",
        takeBatch);
    InOrder("login flush must commit live mode only on an empty batch",
        loop, empty, ready, stopBuffering, exit, takeBatch, sendBatch);
    Equal(1, Count(flush, "waitClientMainReady = false;"),
        "login flush live-mode commit count");
}

static void VerifyGameDataFrameLimit()
{
    Equal(0x8000, InternalPacket77.MAX_FRAME_SIZE,
        "internal M2 maximum frame size");
    Equal(InternalPacket77.MAX_FRAME_SIZE - InternalPacket77.HEADER_SIZE,
        InternalPacket77.MAX_PAYLOAD_SIZE, "internal M2 maximum payload size");

    var maximumPayload = new byte[InternalPacket77.MAX_PAYLOAD_SIZE];
    var maximumFrame = new InternalPacket77
    {
        Magic = InternalPacket77.MAGIC,
        ConnID = 1,
        SeqID = 2,
        FrameLen = (ushort)InternalPacket77.MAX_FRAME_SIZE,
        Cmd = Grobal2.GM_DATA,
        Field20 = (uint)maximumPayload.Length,
        Payload = maximumPayload
    }.ToBytes();
    Equal(InternalPacket77.MAX_FRAME_SIZE, maximumFrame.Length,
        "maximum M2 frame wire length");
    var parser = new InternalPacket77FrameParser(
        maximumFrameLength: InternalPacket77.MAX_FRAME_SIZE);
    True(parser.TryAppend(maximumFrame, 0, maximumFrame.Length,
            out var packets, out var error),
        "maximum M2 frame parser rejected the boundary: " + error);
    Equal(1, packets.Count, "maximum M2 frame parser count");

    var source = GateServerSource();
    Require(source, "if (payload.Length > InternalPacket77.MAX_PAYLOAD_SIZE)",
        "GGCS game-data factory does not reject oversized M2 payloads");
    Equal(3, Count(source, "CreateGameDataPacket(route.ConnId,"),
        "all GGCS game-data construction paths must use the bounded factory");
}

static string GateServerSection(string start, string end) =>
    Section(GateServerSource(), start, end);

static string GateServerSource()
{
    var root = FindRepositoryRoot();
    return File.ReadAllText(Path.Combine(root,
        "GameGate-CS", "Core", "GateServer.cs"));
}

static string Section(string source, string start, string end)
{
    var startIndex = Position(source, start);
    var endIndex = Position(source, end, startIndex + start.Length);
    return source.Substring(startIndex, endIndex - startIndex);
}

static int Position(string source, string value, int startIndex = 0)
{
    var index = source.IndexOf(value, startIndex, StringComparison.Ordinal);
    if (index < 0)
        throw new InvalidOperationException($"source contract missing: {value}");
    return index;
}

static int Count(string source, string value)
{
    var count = 0;
    var offset = 0;
    while ((offset = source.IndexOf(value, offset,
               StringComparison.Ordinal)) >= 0)
    {
        count++;
        offset += value.Length;
    }
    return count;
}

static string FindRepositoryRoot()
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        var directory = new DirectoryInfo(start);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName,
                    "GameGate-CS", "GameGate.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
    }

    throw new DirectoryNotFoundException(
        "repository root containing GameGate-CS/GameGate.csproj was not found");
}

static void InOrder(string message, params int[] positions)
{
    for (var i = 1; i < positions.Length; i++)
    {
        if (positions[i - 1] >= positions[i])
            throw new InvalidOperationException(message);
    }
}

static void Require(string source, string value, string message) =>
    True(source.Contains(value, StringComparison.Ordinal), message);

static void NotContains(string source, string value, string message) =>
    True(!source.Contains(value, StringComparison.Ordinal), message);

static void BytesEqual(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual,
    string message)
{
    if (!expected.SequenceEqual(actual))
        throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{message}: expected={expected} actual={actual}");
}

static void True(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
