using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using GameSvr;
using GameSvr.Services;
using SystemModule;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var gbk = Encoding.GetEncoding(936);

CheckGoldenRecord();
CheckAntiFatigueDropLog();
CheckSecondaryGoldenRecord();
CheckByteTruncation();
CheckAggregationAndRetention();
CheckSecondaryAggregation();
CheckDisconnectedDrop();
CheckReconnectTiming();
CheckSecondaryTiming();
CheckUdpLoopback();
CheckSecondaryUdpLoopback();
CheckIndependentUdpChannels();

Console.WriteLine("NativeGameDataLogProtocolCheck PASS");

void CheckGoldenRecord()
{
    var record = new NativeGameDataLogRecord(0x3C, "3", 0x1234, 0xABCD,
        "角色甲", "白野猪", unchecked((int)0x89ABCDEF), -7, "元宝寄售");
    var raw = NativeGameDataLogCodec.Encode(record);

    Equal(0xC4, raw.Length, "record size");
    BytesEqual(new byte[] { 0x77, 0xBB, 0xAA, 0x33, 1, 0, 0xBC, 0 },
        raw.AsSpan(0, 8), "header");
    CheckShortString(raw, 0x08, 20, gbk.GetBytes("3"), "map");
    Equal((byte)0x3C, raw[0x1D], "log type");
    AllZero(raw.AsSpan(0x1E, 2), "map/type alignment");
    Equal(0x1234U, BinaryPrimitives.ReadUInt32LittleEndian(
        raw.AsSpan(0x20, 4)), "X zero extension");
    Equal(0xABCDU, BinaryPrimitives.ReadUInt32LittleEndian(
        raw.AsSpan(0x24, 4)), "Y zero extension");
    CheckShortString(raw, 0x28, 20, gbk.GetBytes("角色甲"), "character");
    CheckShortString(raw, 0x3D, 20, gbk.GetBytes("白野猪"), "item");
    AllZero(raw.AsSpan(0x52, 2), "item alignment");
    Equal(unchecked((int)0x89ABCDEF), BinaryPrimitives.ReadInt32LittleEndian(
        raw.AsSpan(0x54, 4)), "MakeIndex");
    Equal(-7, BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(0x58, 4)),
        "quantity");
    CheckShortString(raw, 0x5C, 100,
        new byte[] { 0xD4, 0xAA, 0xB1, 0xA6, 0xBC, 0xC4, 0xCA, 0xDB },
        "reason raw CP936");
    AllZero(raw.AsSpan(0xC1, 3), "tail alignment");
}

void CheckAntiFatigueDropLog()
{
    // Keep the actor probe entirely in this audit's isolated output directory.  The
    // production gate is private because it is an implementation detail of Die();
    // reflection lets this test exercise the real predicate without adding a test hook.
    PrepareNativeActorBootstrap();
    var gate = typeof(TBaseObject).GetMethod(
        "NativeAfterScatterItemsBlocked",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("TBaseObject.NativeAfterScatterItemsBlocked");

    var player = new TPlayObject
    {
        m_sMapName = "anti-map",
        m_nCurrX = 7,
        m_nCurrY = 8,
        m_sCharName = "anti-player"
    };
    M2Share.LogStringList.Clear();
    var expected = $"162\tanti-map\t7\t8\tanti-player\t被防沉迷\t{0x0076ADF2}\t1\t怪物爆出被防沉迷";

    bool Invoke(TBaseObject actor) => (bool)gate.Invoke(null, new object[] { actor });
    void ResetPlayer()
    {
        player.m_btNativeFatigueTier = 0;
        player.m_btNativeCheatPenaltyTier = 0;
        player.ClearNativeActiveState(25);
        M2Share.LogStringList.Clear();
    }
    void ExpectBlocked(string label, Action<TPlayObject> arm)
    {
        ResetPlayer();
        arm(player);
        Require(Invoke(player), label + " gate must block");
        Equal(1, M2Share.LogStringList.Count, label + " emits exactly one log");
        Equal(expected, (string)M2Share.LogStringList[0], label + " exact log fields");
    }

    ExpectBlocked("fatigue tier 3", p => p.m_btNativeFatigueTier = 3);
    ExpectBlocked("cheat tier 3", p => p.m_btNativeCheatPenaltyTier = 3);
    ExpectBlocked("active state 25", p => p.SetNativeActiveState(25));
    ExpectBlocked("multiple blockers", p =>
    {
        p.m_btNativeFatigueTier = 3;
        p.m_btNativeCheatPenaltyTier = 3;
        p.SetNativeActiveState(25);
    });

    ResetPlayer();
    Require(!Invoke(player), "unblocked player must pass the gate");
    Equal(0, M2Share.LogStringList.Count, "unblocked player emits no log");

    var animal = new AnimalObject();
    animal.SetNativeActiveState(25);
    M2Share.LogStringList.Clear();
    Require(!Invoke(animal), "non-player race must bypass the anti-fatigue gate");
    Equal(0, M2Share.LogStringList.Count, "non-player emits no log");
    M2Share.LogStringList.Clear();
    Require(!Invoke(null), "null killer must bypass the anti-fatigue gate");
    Equal(0, M2Share.LogStringList.Count, "null killer emits no log");

    var codec = NativeGameDataLogCodec.Encode(new NativeGameDataLogRecord(
        0xA2, "anti-map", 7, 8, "anti-player", "被防沉迷",
        0x0076ADF2, 1, "怪物爆出被防沉迷"));
    Equal((byte)0xA2, codec[0x1D], "anti-fatigue codec action");
    Equal(0x0076ADF2, BinaryPrimitives.ReadInt32LittleEndian(
        codec.AsSpan(0x54, 4)), "anti-fatigue codec MakeIndex");
    Equal(1, BinaryPrimitives.ReadInt32LittleEndian(
        codec.AsSpan(0x58, 4)), "anti-fatigue codec quantity");
}

void PrepareNativeActorBootstrap()
{
    var baseDir = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(baseDir, "!Setup.txt"), "[Server]\r\n");
    File.WriteAllText(Path.Combine(baseDir, "String.ini"), "[String]\r\n");
    File.WriteAllText(Path.Combine(baseDir, "Command.conf"), "[Command]\r\n");
    var share = Path.GetFullPath(Path.Combine(baseDir, "..", "Share"));
    Directory.CreateDirectory(share);
    File.WriteAllText(Path.Combine(share, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]\r\nLEVEL_1=50\r\n");
    _ = M2Share.g_Config;
    M2Share.ObjectManager = new ObjectManager();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new System.Collections.ArrayList();
}

void CheckByteTruncation()
{
    const string map = "A一二三四五六七八九十";
    var encodedMap = gbk.GetBytes(map);
    Equal(21, encodedMap.Length, "map fixture byte count");

    var reason = string.Concat(Enumerable.Repeat("中", 51));
    var encodedReason = gbk.GetBytes(reason);
    var raw = NativeGameDataLogCodec.Encode(
        new NativeGameDataLogRecord(1, map, 0, 0, string.Empty,
            string.Empty, 0, 0, reason));

    Equal((byte)20, raw[0x08], "map byte length truncation");
    BytesEqual(encodedMap.AsSpan(0, 20), raw.AsSpan(0x09, 20),
        "map byte truncation may split CP936 code unit");
    Equal((byte)100, raw[0x5C], "reason byte length truncation");
    BytesEqual(encodedReason.AsSpan(0, 100), raw.AsSpan(0x5D, 100),
        "reason byte truncation");
}

void CheckSecondaryGoldenRecord()
{
    var fixedFileInfo = Enumerable.Range(0,
        NativeSecondaryGameDataLogCodec.FixedFileInfoSize)
        .Select(value => (byte)value).ToArray();
    var raw = NativeSecondaryGameDataLogCodec.Encode(fixedFileInfo,
        unchecked((int)0x89ABCDEF));

    Equal(0x44, raw.Length, "secondary record size");
    BytesEqual(new byte[] { 0x22, 0xFF, 0x22, 0xFF, 0x46, 0x04, 0x3C, 0x00 },
        raw.AsSpan(0, 8), "secondary header");
    BytesEqual(fixedFileInfo, raw.AsSpan(0x08, 0x34),
        "secondary VS_FIXEDFILEINFO");
    Equal(unchecked((int)0x89ABCDEF),
        BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(0x3C, 4)),
        "secondary ServerIndex");
    Equal(0, BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(0x40, 4)),
        "secondary fixed zero");
    Equal(20_000, NativeSecondaryGameDataLogService.NativePort,
        "secondary native port");
    Equal(NativeSecondaryGameDataLogCodec.FixedFileInfoSize,
        NativeExecutableFixedFileInfo.ReadCurrentProcess().Length,
        "current process fixed-file-info size");
    var gameServerFixedInfo = NativeExecutableFixedFileInfo.Read(
        Path.Combine(AppContext.BaseDirectory, "GameSvr.exe"));
    Equal(0xFEEF04BDU, BinaryPrimitives.ReadUInt32LittleEndian(
        gameServerFixedInfo.AsSpan(0, 4)),
        "GameSvr apphost VS_FIXEDFILEINFO signature");
    AllZero(NativeExecutableFixedFileInfo.Read(null),
        "missing executable fixed-file-info fallback");
}

void CheckAggregationAndRetention()
{
    var buffer = new NativeGameDataLogBuffer();
    for (var i = 0; i < 21; i++)
    {
        var record = new byte[NativeGameDataLogCodec.RecordSize];
        Array.Fill(record, (byte)i);
        buffer.Enqueue(record);
        record[0] = 0xFF;
    }

    Require(buffer.TryGetDatagram(out var first, out var firstCount),
        "first aggregate exists");
    Equal(20 * NativeGameDataLogCodec.RecordSize, firstCount,
        "20 records fit under 4096");
    Equal(1, buffer.QueuedRecordCount, "record 21 remains queued");
    for (var i = 0; i < 20; i++)
        Equal((byte)i, first[i * NativeGameDataLogCodec.RecordSize],
            $"FIFO record {i}");
    var firstSnapshot = first.AsSpan(0, firstCount).ToArray();

    Require(buffer.TryGetDatagram(out var retained, out var retainedCount),
        "uncommitted aggregate is retained");
    Require(ReferenceEquals(first, retained),
        "soft-error retry reuses native-style staging buffer");
    Equal(firstCount, retainedCount, "soft-error retained byte count");
    BytesEqual(firstSnapshot, retained.AsSpan(0, retainedCount),
        "soft-error retention model");
    Require(NativeGameDataLogService.IsSoftSendError(SocketError.WouldBlock),
        "WSAEWOULDBLOCK retains");
    Require(NativeGameDataLogService.IsSoftSendError(SocketError.Interrupted),
        "WSAEINTR retains");
    Require(!NativeGameDataLogService.IsSoftSendError(
        SocketError.ConnectionReset), "hard socket error reconnects");

    buffer.CommitSent(firstCount);
    Require(buffer.TryGetDatagram(out var second, out var secondCount),
        "record 21 aggregate exists");
    Equal(NativeGameDataLogCodec.RecordSize, secondCount,
        "record 21 deferred to next datagram");
    Equal((byte)20, second[0], "record 21 FIFO value");
    buffer.CommitSent(100);
    Require(buffer.TryGetDatagram(out var partial, out var partialCount),
        "partial send remainder");
    Equal(NativeGameDataLogCodec.RecordSize - 100, partialCount,
        "partial send shifts pending bytes");
    buffer.CommitSent(partialCount);
    Equal(0, buffer.PendingByteCount, "aggregate drained");
}

void CheckDisconnectedDrop()
{
    using var service = new NativeGameDataLogService();
    Require(!service.TryEnqueue(new NativeGameDataLogRecord(1, "3", 1, 2,
        "角色", "物品", 3, 4, "原因")), "disconnected log is dropped");
    Equal(0, service.QueuedRecordCount, "drop does not enter FIFO");
    Equal(IPAddress.Broadcast,
        NativeGameDataLogService.ResolveNativeIpv4("not-an-ip"),
        "inet_addr invalid-text result is retained as INADDR_NONE");
}

void CheckSecondaryAggregation()
{
    var buffer = new NativeGameDataLogBuffer();
    for (var i = 0; i < 61; i++)
    {
        var record = new byte[NativeSecondaryGameDataLogCodec.RecordSize];
        record[0] = (byte)i;
        buffer.Enqueue(record);
    }

    Require(buffer.TryGetDatagram(out var first, out var firstCount),
        "secondary first aggregate exists");
    Equal(60 * NativeSecondaryGameDataLogCodec.RecordSize, firstCount,
        "60 secondary records fit under 4096");
    Equal(1, buffer.QueuedRecordCount,
        "secondary record 61 remains queued");
    for (var i = 0; i < 60; i++)
        Equal((byte)i,
            first[i * NativeSecondaryGameDataLogCodec.RecordSize],
            $"secondary FIFO record {i}");

    buffer.CommitSent(firstCount);
    Require(buffer.TryGetDatagram(out var second, out var secondCount),
        "secondary record 61 aggregate exists");
    Equal(NativeSecondaryGameDataLogCodec.RecordSize, secondCount,
        "secondary record 61 deferred to next datagram");
    Equal((byte)60, second[0], "secondary record 61 FIFO value");
}

void CheckReconnectTiming()
{
    Require(!NativeGameDataLogService.IsReconnectDue(29_999U, 0U),
        "reconnect is not due before 30000 ms");
    Require(NativeGameDataLogService.IsReconnectDue(30_000U, 0U),
        "reconnect is due at 30000 ms");
    Require(!NativeGameDataLogService.IsReconnectDue(0x10U, 0xFFFFFFF0U),
        "wrapped reconnect delta below threshold");
    Require(NativeGameDataLogService.IsReconnectDue(0x20U,
        unchecked(0x20U - 30_000U)), "wrapped reconnect delta reaches threshold");
}

void CheckSecondaryTiming()
{
    Require(!NativeSecondaryGameDataLogService.IsExecuteDue(999U, 0U),
        "secondary outer gate is not due before 1000 ms");
    Require(NativeSecondaryGameDataLogService.IsExecuteDue(1_000U, 0U),
        "secondary outer gate is due at 1000 ms");
    Require(!NativeSecondaryGameDataLogService.IsReportDue(9_999U, 0U),
        "secondary report is not due before 10000 ms");
    Require(NativeSecondaryGameDataLogService.IsReportDue(10_000U, 0U),
        "secondary report is due at 10000 ms");
    Require(!NativeSecondaryGameDataLogService.IsReportDue(
        0x10U, 0xFFFFFFF0U), "secondary wrapped report below threshold");
    Require(NativeSecondaryGameDataLogService.IsReportDue(0x20U,
        unchecked(0x20U - 10_000U)),
        "secondary wrapped report reaches threshold");
}

void CheckUdpLoopback()
{
    using var receiver = new Socket(AddressFamily.InterNetwork,
        SocketType.Dgram, ProtocolType.Udp);
    receiver.Bind(new IPEndPoint(IPAddress.Loopback, 0));
    receiver.ReceiveTimeout = 3_000;
    var port = ((IPEndPoint)receiver.LocalEndPoint).Port;
    var expectedRecord = new NativeGameDataLogRecord(0x3C, "3", 10, 11,
        "角色", "白野猪", 123, 1, "元宝寄售");
    var expected = NativeGameDataLogCodec.Encode(expectedRecord);

    using var service = new NativeGameDataLogService();
    service.Start(IPAddress.Loopback.ToString(), port);
    Require(SpinWait.SpinUntil(() => service.Connected, 1_000),
        "UDP service starts");
    Require(service.TryEnqueue(expectedRecord), "connected enqueue succeeds");

    var actual = new byte[NativeGameDataLogBuffer.AggregateCapacity];
    EndPoint sender = new IPEndPoint(IPAddress.Any, 0);
    var count = receiver.ReceiveFrom(actual, ref sender);
    Equal(expected.Length, count, "UDP datagram length");
    BytesEqual(expected, actual.AsSpan(0, count), "UDP loopback payload");
    Require(SpinWait.SpinUntil(() => service.PendingByteCount == 0, 1_000),
        "sent bytes committed");
    service.Stop();
    Require(!service.Connected, "stop closes socket");
    Require(!service.WorkerRunning, "stop waits for worker exit");
    Equal(0, service.QueuedRecordCount, "stop clears FIFO");
    Equal(0, service.PendingByteCount, "stop clears staging");

    service.Start(IPAddress.Loopback.ToString(), port);
    Require(SpinWait.SpinUntil(() => service.Connected, 1_000),
        "service restarts after stop");
    Require(service.TryEnqueue(expectedRecord), "restart enqueue succeeds");
    count = receiver.ReceiveFrom(actual, ref sender);
    Equal(expected.Length, count, "restart UDP datagram length");
    BytesEqual(expected, actual.AsSpan(0, count),
        "restart UDP loopback payload");
    service.Stop();
}

void CheckSecondaryUdpLoopback()
{
    using var receiver = new Socket(AddressFamily.InterNetwork,
        SocketType.Dgram, ProtocolType.Udp);
    receiver.Bind(new IPEndPoint(IPAddress.Loopback, 0));
    receiver.ReceiveTimeout = 3_000;
    var port = ((IPEndPoint)receiver.LocalEndPoint).Port;
    var fixedFileInfo = Enumerable.Range(0,
        NativeSecondaryGameDataLogCodec.FixedFileInfoSize)
        .Select(value => (byte)(0x80 + value)).ToArray();
    var expected = NativeSecondaryGameDataLogCodec.Encode(fixedFileInfo, 7);

    using var service = new NativeSecondaryGameDataLogService(
        fixedFileInfo, port);
    service.Start(IPAddress.Loopback.ToString());
    Require(SpinWait.SpinUntil(() => service.Connected, 1_000),
        "secondary UDP service starts independently");

    for (uint tick = 1_000; tick < 10_000; tick += 1_000)
        Require(!service.Run(tick, 7),
            $"secondary report suppressed at {tick} ms");
    Require(service.Run(10_000, 7),
        "secondary report enqueues at 10000 ms");
    Equal(10_000U, service.LastReportTick,
        "secondary report tick updates before enqueue");

    var actual = new byte[NativeGameDataLogBuffer.AggregateCapacity];
    EndPoint sender = new IPEndPoint(IPAddress.Any, 0);
    var count = receiver.ReceiveFrom(actual, ref sender);
    Equal(expected.Length, count, "secondary UDP datagram length");
    BytesEqual(expected, actual.AsSpan(0, count),
        "secondary UDP loopback payload");
    Require(SpinWait.SpinUntil(() => service.PendingByteCount == 0, 1_000),
        "secondary sent bytes committed");

    service.Stop();
    Require(!service.Connected, "secondary stop closes socket");
    Require(!service.WorkerRunning,
        "secondary stop waits for worker exit");
    Equal(0, service.QueuedRecordCount, "secondary stop clears FIFO");
    Equal(0, service.PendingByteCount,
        "secondary stop clears staging");
}

void CheckIndependentUdpChannels()
{
    using var primaryReceiver = new Socket(AddressFamily.InterNetwork,
        SocketType.Dgram, ProtocolType.Udp);
    using var secondaryReceiver = new Socket(AddressFamily.InterNetwork,
        SocketType.Dgram, ProtocolType.Udp);
    primaryReceiver.Bind(new IPEndPoint(IPAddress.Loopback, 0));
    secondaryReceiver.Bind(new IPEndPoint(IPAddress.Loopback, 0));
    primaryReceiver.ReceiveTimeout = 3_000;
    secondaryReceiver.ReceiveTimeout = 3_000;
    var primaryPort = ((IPEndPoint)primaryReceiver.LocalEndPoint).Port;
    var secondaryPort = ((IPEndPoint)secondaryReceiver.LocalEndPoint).Port;
    var primaryRecord = new NativeGameDataLogRecord(9, "3", 1, 2,
        "角色", "物品", 3, 1, "商人");
    var primaryExpected = NativeGameDataLogCodec.Encode(primaryRecord);
    var fixedFileInfo = Enumerable.Range(0,
        NativeSecondaryGameDataLogCodec.FixedFileInfoSize)
        .Select(value => (byte)(0x40 + value)).ToArray();
    var secondaryExpected = NativeSecondaryGameDataLogCodec.Encode(
        fixedFileInfo, 12);

    using var primary = new NativeGameDataLogService();
    using var secondary = new NativeSecondaryGameDataLogService(
        fixedFileInfo, secondaryPort);
    primary.Start(IPAddress.Loopback.ToString(), primaryPort);
    secondary.Start(IPAddress.Loopback.ToString());
    Require(SpinWait.SpinUntil(
            () => primary.Connected && secondary.Connected, 1_000),
        "both UDP channels connect independently");

    Require(primary.TryEnqueue(primaryRecord),
        "primary channel enqueue succeeds while both run");
    for (uint tick = 1_000; tick <= 9_000; tick += 1_000)
        Require(!secondary.Run(tick, 12),
            $"secondary dual-channel report suppressed at {tick} ms");
    Require(secondary.Run(10_000, 12),
        "secondary dual-channel report enqueues at 10000 ms");

    var primaryActual = new byte[NativeGameDataLogBuffer.AggregateCapacity];
    var secondaryActual = new byte[NativeGameDataLogBuffer.AggregateCapacity];
    EndPoint primarySender = new IPEndPoint(IPAddress.Any, 0);
    EndPoint secondarySender = new IPEndPoint(IPAddress.Any, 0);
    var primaryCount = primaryReceiver.ReceiveFrom(primaryActual,
        ref primarySender);
    var secondaryCount = secondaryReceiver.ReceiveFrom(secondaryActual,
        ref secondarySender);
    Equal(primaryExpected.Length, primaryCount,
        "primary independent datagram length");
    BytesEqual(primaryExpected, primaryActual.AsSpan(0, primaryCount),
        "primary independent datagram payload");
    Equal(secondaryExpected.Length, secondaryCount,
        "secondary independent datagram length");
    BytesEqual(secondaryExpected, secondaryActual.AsSpan(0, secondaryCount),
        "secondary independent datagram payload");

    primary.Stop();
    Require(!primary.Connected && secondary.Connected,
        "stopping primary leaves secondary connected");
    for (uint tick = 11_000; tick <= 19_000; tick += 1_000)
        Require(!secondary.Run(tick, 12),
            $"secondary next report suppressed at {tick} ms");
    Require(secondary.Run(20_000, 12),
        "secondary still sends after primary stops");
    secondaryCount = secondaryReceiver.ReceiveFrom(secondaryActual,
        ref secondarySender);
    Equal(secondaryExpected.Length, secondaryCount,
        "secondary post-primary-stop datagram length");
    BytesEqual(secondaryExpected,
        secondaryActual.AsSpan(0, secondaryCount),
        "secondary post-primary-stop datagram payload");
    secondary.Stop();
}

void CheckShortString(byte[] raw, int offset, int capacity,
    ReadOnlySpan<byte> expected, string label)
{
    Equal((byte)expected.Length, raw[offset], label + " length");
    BytesEqual(expected, raw.AsSpan(offset + 1, expected.Length),
        label + " bytes");
    AllZero(raw.AsSpan(offset + 1 + expected.Length,
        capacity - expected.Length), label + " zero padding");
}

void AllZero(ReadOnlySpan<byte> actual, string label)
{
    for (var i = 0; i < actual.Length; i++)
        if (actual[i] != 0)
            throw new InvalidOperationException(
                $"{label}: nonzero byte {actual[i]:X2} at {i}");
}

void BytesEqual(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual,
    string label)
{
    if (!expected.SequenceEqual(actual))
        throw new InvalidOperationException(
            $"{label}: expected {Convert.ToHexString(expected)}, "
            + $"actual {Convert.ToHexString(actual)}");
}

void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{label}: expected {expected}, actual {actual}");
}

void Require(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
}
