using System.Text;
using SystemModule;
using SystemModule.Packet;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

VerifyStartPlayStatePrecedesClientWrite();
VerifyGameSvrCertificationIsSingleSuccessfulSend();
VerifyInternalCertificationRecogIsZero();
VerifyMarkerDataCommand23PreservesRawBody();
VerifyLoginSessionUsesOuterDataIndex();
VerifyGameDataUsesSupportedEnvelopeCommand();
VerifyLoginFlushKeepsFramesOrdered();
VerifyTwoPhaseLoginRelease();
VerifyGameSvrNoticeStateMachine();
VerifyGameDataFrameLimit();

Console.WriteLine("GameGateLoginContractCheck PASS checks=10");

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
    var certificationAvailable = Position(relayDown,
        "sentGameSvrCertification = false;", notReady);
    var clientWrite = Position(relayDown,
        "await WriteClientMobileFrame(frame, allocateDataIndex: true);", certificationAvailable);

    InOrder("SM_STARTPLAY state must be armed before the frame reaches the client",
        updatePlayer, startPlayState, clear, wait, notReady,
        certificationAvailable, clientWrite);
}

static void VerifyGameSvrCertificationIsSingleSuccessfulSend()
{
    var source = GateServerSource();
    var helper = Section(source,
        "async Task<bool> SendGameSvrCertificationOnce",
        "async Task QueryCharactersAfterSoftClose");

    var acquireLock = Position(helper,
        "await loginNoticeWriteLock.WaitAsync(cts.Token);");
    var duplicateCheck = Position(helper,
        "if (sentGameSvrCertification) return false;", acquireLock);
    var gameWrite = Position(helper, "await WriteGameSvr(frame, cts.Token);", duplicateCheck);
    var commitSuccess = Position(helper,
        "sentGameSvrCertification = true;", gameWrite);
    var releaseLock = Position(helper,
        "loginNoticeWriteLock.Release();", commitSuccess);
    InOrder("GameSvr certification must serialize duplicate check, write, and commit",
        acquireLock, duplicateCheck, gameWrite, commitSuccess, releaseLock);

    Equal(1, Count(source,
            "SendGameSvrCertificationOnce(pkt.ToBytes(),"),
        "SM_STARTPLAY GameSvr certification path count");

    var relayUp = GateServerSection("async Task RelayUp()", "async Task RelayDown()");
    NotContains(relayUp, "SendGameSvrCertificationOnce",
        "the real client 1018 must not be suppressed as a duplicate certification");
}

static void VerifyInternalCertificationRecogIsZero()
{
    var relayDown = GateServerSection("async Task RelayDown()", "async Task RelayGameSvr()");
    var clientWrite = Position(relayDown,
        "await WriteClientMobileFrame(frame, allocateDataIndex: true);");
    var certPlain = Position(relayDown, "var certPlain =", clientWrite);
    var createPacket = Position(relayDown, "var cp = new ClientPacket", certPlain);
    var zeroRecog = Position(relayDown, "Recog = 0", createPacket);
    var ident1018 = Position(relayDown, "Ident = 1018", zeroRecog);
    var serializePacket = Position(relayDown,
        "Buffer.BlockCopy(cp.GetBuffer()", ident1018);
    var sendCertification = Position(relayDown,
        "SendGameSvrCertificationOnce(pkt.ToBytes(),", serializePacket);
    InOrder("SM_STARTPLAY certification must follow client 525 and use Recog=0/Ident=1018",
        clientWrite, certPlain, createPacket, zeroRecog, ident1018,
        serializePacket, sendCertification);
    NotContains(relayDown, "FlushBufferedGameFrames",
        "the internal SM_STARTPLAY certification must not release scene frames");
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

    var relayUp = GateServerSection("async Task RelayUp()", "async Task RelayDown()");
    NotContains(relayUp, "Decode6BitBufDirect",
        "RelayUp must not 6-bit decode ordinary MARKER_DATA bodies");
    NotContains(relayUp, "mf.Header.Cmd == 23",
        "RelayUp must not interpret MARKER_DATA's overlapping Cmd byte as an encoding flag");
}

static void VerifyLoginSessionUsesOuterDataIndex()
{
    const uint selectedSession = 1011;
    const int clientVersion = 131532307;

    var connectWire = MobileCodec.WriteConnect(selectedSession);
    True(MobileCodec.TryReadFrame(connectWire, 0, connectWire.Length,
            out var connect, out var connectConsumed),
        "BaiZhu LM_GET_ENCRYPT/connect frame did not parse");
    Equal(connectWire.Length, connectConsumed, "connect consumed length");
    Equal(MobileCodec.MARKER_CONNECT, connect.Header.Marker,
        "LM_GET_ENCRYPT marker");
    Equal(selectedSession, connect.Header.Seq,
        "LoginGate session must travel in outer dataIndex");

    var authWire = MobileCodec.WriteFrame(new MobileCodec.InnerHeader
    {
        Recog = clientVersion,
        Ident = 4004,
        Param = 2
    }, Array.Empty<byte>(), 0x1234);
    True(MobileCodec.TryReadFrame(authWire, 0, authWire.Length,
            out var auth, out _), "BaiZhu CM_LOGIN_AUTH frame did not parse");
    Equal(clientVersion, auth.Inner.Recog,
        "CM_LOGIN_AUTH Recog is the client version");
    True(unchecked((uint)auth.Inner.Recog) != connect.Header.Seq,
        "client version and LoginGate session must remain distinct");

    var relayUp = GateServerSection("async Task RelayUp()", "async Task RelayDown()");
    var connectBranch = Position(relayUp,
        "if (mf.Header.Marker == MobileCodec.MARKER_CONNECT)");
    var captureSession = Position(relayUp,
        "session.DBSessionId = unchecked((int)mf.Header.Seq);", connectBranch);
    var captureTigerOffset = Position(relayUp,
        "session.TigerKeyOffset = mf.Header.Seq;", captureSession);
    var sendLogin = Position(relayUp,
        "await WriteClientMobileFrame(siFrame, allocateDataIndex: true);",
        captureTigerOffset);
    InOrder("outer dataIndex must arm both DB and Tiger state before SM_LOGIN",
        connectBranch, captureSession, captureTigerOffset, sendLogin);

    NotContains(relayUp, "session.DBSessionId = mf.Inner.Recog",
        "CM_LOGIN_AUTH version must not overwrite the LoginGate session");
    var chooseDbRecog = Position(relayUp,
        "var dbRecog = fwdIdent == 4004");
    var chooseSession = Position(relayUp, "? session.DBSessionId", chooseDbRecog);
    var preserveOtherRecog = Position(relayUp, ": mf.Inner.Recog;", chooseSession);
    var serializeDbRecog = Position(relayUp,
        "var headerMsg = new ClientPacket { Recog = dbRecog", preserveOtherRecog);
    InOrder("only 4004 must replace client version with the routed DB session",
        chooseDbRecog, chooseSession, preserveOtherRecog, serializeDbRecog);
}

static void VerifyGameDataUsesSupportedEnvelopeCommand()
{
    var source = GateServerSource();
    Require(source, "Cmd = NativeGameGateCommands.GateClientData",
        "GateServer client DATA must use native Gate->M2 command 4");
    NotContains(source, "Cmd = Grobal2.GM_DATA",
        "GateServer must not put the legacy GM_DATA=5 value on native DATA frames");
    NotContains(source, "(ushort)0x275",
        "GateServer must not send an unsupported speed command as an outer M2 command");
    NotContains(source, "(ushort)0x276",
        "GateServer must not send an unsupported warning command as an outer M2 command");
}

static void VerifyLoginFlushKeepsFramesOrdered()
{
    var relayUp = GateServerSection("async Task RelayUp()", "async Task RelayDown()");
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
    var sendBatch = Position(flush,
        "await WriteClientMobileFrame(frameInfo.Frame, allocateDataIndex: true);",
        takeBatch);
    InOrder("login flush must commit live mode only on an empty batch",
        loop, empty, ready, stopBuffering, exit, takeBatch, sendBatch);
    Equal(1, Count(flush, "waitClientMainReady = false;"),
        "login flush live-mode commit count");
}

static void VerifyTwoPhaseLoginRelease()
{
    var relayDown = GateServerSection("async Task RelayDown()", "async Task RelayGameSvr()");
    Require(relayDown, "SendGameSvrCertificationOnce(pkt.ToBytes(),",
        "SM_STARTPLAY must create the GameSvr player before the notice phase");

    var relayUp = GateServerSection("async Task RelayUp()", "async Task RelayDown()");
    var createPacket = Position(relayUp,
        "var cp = CreateGameSvrClientPacket(mf.Inner, fwdIdent);");
    var allocateBody = Position(relayUp,
        "var gsBody = new byte[ClientPacket.PackSize + bodyToSend.Length];",
        createPacket);
    var copyBody = Position(relayUp,
        "Buffer.BlockCopy(bodyToSend, 0, gsBody, ClientPacket.PackSize, bodyToSend.Length);",
        allocateBody);
    var createGameFrame = Position(relayUp,
        "var pkt = CreateGameDataPacket(route.ConnId,", copyBody);
    var client1018 = Position(relayUp, "if (fwdIdent == 1018)", createGameFrame);
    var forward1018 = Position(relayUp,
        "await WriteGameSvr(pkt.ToBytes(), cts.Token);", client1018);
    var flush = Position(relayUp,
        "FlushBufferedGameFrames(\"after client 1018\")", forward1018);
    InOrder("the real client 1018 body must be forwarded before scene release",
        createPacket, allocateBody, copyBody, createGameFrame,
        client1018, forward1018, flush);
    NotContains(relayUp, "cp.Recog = 0;",
        "the real client 1018 header must not be rewritten as internal certification");
    NotContains(relayUp, "certPlain",
        "the real client 1018 body must not be replaced by an EDcode certification string");

    var relayGame = GateServerSection("async Task RelayGameSvr()",
        "var relayTasks = new[]");
    var bufferDecision = Position(relayGame,
        "shouldBuffer = waitClientMainReady && !clientMainReady");
    var allowNotice = Position(relayGame,
        "ident != SM_SENDNOTICE", bufferDecision);
    var allowClientConfig = Position(relayGame,
        "ident != SM_CLIENT_CONF", allowNotice);
    var enqueueBuffered = Position(relayGame,
        "if (shouldBuffer)", allowClientConfig);
    InOrder("658 and 2953 must bypass the pre-1018 scene buffer",
        bufferDecision, allowNotice, allowClientConfig, enqueueBuffered);
    NotContains(relayGame, "injected empty mapinfo",
        "GameGate must not append an empty SM_MAPINFO_EX after GameSvr's real packet");
}

static void VerifyGameSvrNoticeStateMachine()
{
    var playerBase = RepositorySource("GameSvr", "Players", "TPlayObject.Base.cs");
    var runNotice = Section(playerBase, "public void RunNotice()",
        "private byte[] GetMobileAbility()");
    var sendNotice = Position(runNotice, "SendNotice();");
    var sendClientConfig = Position(runNotice, "SendNativeClientConfig();", sendNotice);
    var markNoticeSent = Position(runNotice, "m_boSendNotice = true;", sendClientConfig);
    InOrder("GameSvr must send 658 then 2953 exactly once without entering the game",
        sendNotice, sendClientConfig, markNoticeSent);
    NotContains(runNotice, "m_boLoginNoticeOK = true",
        "sending 658/2953 must not enter the game before the real client 1018");

    var versionGate = RepositorySource("GameSvr", "Players",
        "TPlayObject.NativeClientVersionGate.cs");
    var first1018 = Section(versionGate,
        "internal bool ShouldDispatchNativeClientMessage",
        "internal void InitializeNativeClientVersionRunGate");
    var require1018 = Position(first1018,
        "if (packet.Ident != Grobal2.CM_LOGINNOTICEOK)");
    var completeHandshake = Position(first1018,
        "m_boNativeClientVersionHandshakeDone = true;", require1018);
    var allowLogin = Position(first1018,
        "m_boLoginNoticeOK = true;", completeHandshake);
    var consume1018 = Position(first1018, "return false;", allowLogin);
    InOrder("only the first real 1018 may complete the notice gate",
        require1018, completeHandshake, allowLogin, consume1018);

    var userEngine = RepositorySource("GameSvr", "UsrSystem", "UsrEngn.cs");
    Equal(2, Count(userEngine, "if (!PlayObject.m_boLoginNoticeOK)"),
        "GameSvr login-notice wait-loop count");
    Equal(2, Count(userEngine, "PlayObject.RunNotice();"),
        "GameSvr notice loop count");
    Equal(2, Count(userEngine, "PlayObject.UserLogon();"),
        "GameSvr post-1018 UserLogon path count");
    Equal(0, Count(userEngine, "PlayObject.SendNativeClientConfig();"),
        "2953 must be emitted alongside 658, not separately on every engine tick");
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
    Require(source,
        "if (payload.Length > NativeGameGateCommands.NativeM2MaximumBodyLength)",
        "GGCS game-data factory does not reject oversized native M2 payloads");
    var gateService = RepositorySource("GameSvr", "GameGate", "GateService.cs");
    Require(gateService,
        "maximumFrameLength: InternalPacket77FrameParser.NativeMaximumFrameLength",
        "GameSvr GateService does not use the native M2 receive frame limit");
    Equal(3, Count(source, "CreateGameDataPacket(route.ConnId,"),
        "all GGCS game-data construction paths must use the bounded factory");
}

static string GateServerSection(string start, string end) =>
    Section(GateServerSource(), start, end);

static string GateServerSource()
{
    return RepositorySource("GameGate-CS", "Core", "GateServer.cs");
}

static string RepositorySource(params string[] components) =>
    File.ReadAllText(Path.Combine(new[] { FindRepositoryRoot() }
        .Concat(components).ToArray()));

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
