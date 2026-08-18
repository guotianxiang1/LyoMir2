using System.Collections;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using GameSvr;
using GameSvr.Configs;
using GameSvr.Services;
using SystemModule;
using SystemModule.Packet;
using SystemModule.Sockets;

internal static class Program
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public static async Task Main()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(),
            "YbDbLoopbackCheck-" + Guid.NewGuid().ToString("N"));
        var bootstrapFiles = PrepareBootstrapFiles();
        GameSvrConfig previousConfig = null;
        string previousRootPath = null;
        var previousServerIndex = 0;
        UserEngine previousUserEngine = null;
        ObjectManager previousObjectManager = null;
        NativeCreditCardService previousCreditCardService = null;
        var previousYbDoubleForge = false;
        var m2Initialized = false;
        YbDbClient client = null;
        TcpListener listener = null;
        CancellationTokenSource pulseCancellation = null;
        Task pulseTask = null;

        try
        {
            previousConfig = M2Share.g_Config;
            previousRootPath = M2Share.sRootPath;
            previousServerIndex = M2Share.nServerIndex;
            previousUserEngine = M2Share.UserEngine;
            previousObjectManager = M2Share.ObjectManager;
            previousCreditCardService = M2Share.CreditCardService;
            previousYbDoubleForge = M2Share.g_boYbDoubleForge;
            m2Initialized = true;

            const int serverIndex = 6;
            const int areaId = 180;
            const int groupId = 1;

            var shareDirectory = Path.Combine(tempRoot, "Share");
            Directory.CreateDirectory(shareDirectory);
            await File.WriteAllTextAsync(Path.Combine(shareDirectory, "serverinfo.ini"),
                $"[Setup]{Environment.NewLine}AreaID={areaId}{Environment.NewLine}" +
                $"GroupID={groupId}{Environment.NewLine}");

            M2Share.g_Config = new GameSvrConfig
            {
                sYBDBAddr = IPAddress.Loopback.ToString(),
                sDBAddr = "192.0.2.1",
                sBaseDir = "Share"
            };
            M2Share.sRootPath = tempRoot;
            M2Share.nServerIndex = serverIndex;
            M2Share.UserEngine = new UserEngine();
            M2Share.ObjectManager = new ObjectManager();
            M2Share.CreditCardService = NativeCreditCardService.Disabled;
            M2Share.g_boYbDoubleForge = false;
            M2Share.LogMsgCriticalSection ??= new object();
            M2Share.ProcessMsgCriticalSection ??= new object();
            M2Share.ProcessHumanCriticalSection ??= new object();
            M2Share.LogStringList ??= new ArrayList();
            M2Share.LogStringList.Clear();

            listener = new TcpListener(IPAddress.Loopback, YbDbClient.ServicePort);
            try
            {
                listener.Start(1);
            }
            catch (SocketException ex)
            {
                throw new InvalidOperationException(
                    $"TCP {YbDbClient.ServicePort} is unavailable for loopback testing", ex);
            }

            var acceptTask = listener.AcceptTcpClientAsync();
            client = YbDbClient.Instance;
            client.Start();
            client.Start();
            pulseCancellation = new CancellationTokenSource();
            pulseTask = RunPulseAsync(client, pulseCancellation.Token);

            using var serverConnection = await acceptTask.WaitAsync(Timeout);
            serverConnection.NoDelay = true;
            using var stream = serverConnection.GetStream();

            var handshakes = await ReadFramesAsync(stream, expectedCount: 3);
            AssertFrame(handshakes[0], queryId: 0, param: serverIndex + 1,
                ident: 100, Array.Empty<byte>(), "first handshake");
            AssertFrame(handshakes[1], queryId: areaId, param: groupId,
                ident: 400, Array.Empty<byte>(), "second handshake");
            AssertFrame(handshakes[2], queryId: 0, param: 1,
                ident: 108, Array.Empty<byte>(), "single forge-mode handshake");

            await VerifyItemMovementSmsOutboundAsync(client, stream);
            await VerifyForgeModeResponseAsync(client, stream, queryId: 2,
                expectedDoubleForge: true);

            await VerifyLingFuAccountingAsync(client, stream);
            await VerifyCreditRefreshAsync(client, stream);
            await VerifyCreditRelogAndIndependent1104Async(client, stream);
            await VerifyCreditEpochCapAsync(client, stream);
            await VerifyFragmentedAndCoalescedInputAsync(client, stream);
            await VerifyUnboundedResponseQueueAsync(client, stream);
            await VerifyUncappedOutboundFlushAsync(client, stream);

            var firstClientSocket = GetPrivateField<Socket>(client, "_currentSocket");
            var firstGeneration = GetPrivateField<long>(client,
                "_connectionGeneration");

            client.Stop();
            client.Stop();
            await WaitUntilAsync(() => !client.Connected,
                "YbDbClient remained connected after Stop");
            await VerifyEofAsync(stream, "first server EOF after repeated Stop");

            var secondAcceptTask = listener.AcceptTcpClientAsync();
            client.Start();
            client.Start();
            using var secondServerConnection = await secondAcceptTask.WaitAsync(Timeout);
            secondServerConnection.NoDelay = true;
            using var secondStream = secondServerConnection.GetStream();

            var secondHandshakes = await ReadFramesAsync(secondStream, expectedCount: 3);
            AssertFrame(secondHandshakes[0], queryId: 0, param: serverIndex + 1,
                ident: 100, Array.Empty<byte>(), "restarted first handshake");
            AssertFrame(secondHandshakes[1], queryId: areaId, param: groupId,
                ident: 400, Array.Empty<byte>(), "restarted second handshake");
            AssertFrame(secondHandshakes[2], queryId: 0, param: 2,
                ident: 108, Array.Empty<byte>(), "preserved double forge-mode handshake");
            await VerifyForgeModeResponseAsync(client, secondStream, queryId: -77,
                expectedDoubleForge: false);
            await VerifyStaleSocketCallbacksAsync(client, firstClientSocket,
                firstGeneration, secondStream);

            client.Stop();
            client.Stop();
            await WaitUntilAsync(() => !client.Connected,
                "YbDbClient remained connected after restarted Stop");
            await VerifyEofAsync(secondStream,
                "second server EOF after repeated Stop");

            Console.WriteLine(
                "YbDbLoopbackCheck PASS handshake=100,400,108/1,2 " +
                "item-sms=135/120-e2e " +
                "forge=1108/2,non2 " +
                "declf=132/108+1132/64 credit=103/64+1103/32 " +
                "relog=103-fifo+independent-1104 " +
                "credit-silent=32 credit-epochs=16 " +
                "fragmented=7-byte coalesced=1 restart=1 " +
                "responses=5000 outbound=100 stale-callbacks=isolated stop=eof");
        }
        finally
        {
            pulseCancellation?.Cancel();
            if (pulseTask != null)
            {
                try { await pulseTask; }
                catch (OperationCanceledException) { }
            }
            pulseCancellation?.Dispose();
            client?.Stop();
            listener?.Stop();
            if (m2Initialized)
            {
                M2Share.g_Config = previousConfig;
                M2Share.sRootPath = previousRootPath;
                M2Share.nServerIndex = previousServerIndex;
                M2Share.UserEngine = previousUserEngine;
                M2Share.ObjectManager = previousObjectManager;
                M2Share.CreditCardService = previousCreditCardService;
                M2Share.g_boYbDoubleForge = previousYbDoubleForge;
            }
            try
            {
                if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // The process exits immediately; cleanup failure does not affect protocol evidence.
            }
            RestoreBootstrapFiles(bootstrapFiles);
        }
    }

    private static async Task RunPulseAsync(YbDbClient client,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            client.Pulse();
            await Task.Delay(10, cancellationToken);
        }
    }

    private static async Task VerifyForgeModeResponseAsync(YbDbClient client,
        NetworkStream stream, int queryId, bool expectedDoubleForge)
    {
        var response = new YbDbLegacy77Frame(queryId, int.MinValue,
            YbDbForgeModeProtocol.ResponseIdent, new byte[] { 0xAA, 0x55 });
        await stream.WriteAsync(Encode(response));
        await WaitUntilAsync(
            () => GetQueueCount(GetPrivateField<object>(client, "_responses")) == 1,
            "YbDbClient did not queue the 1108 forge-mode response");
        client.ProcessCompletions();
        Equal(expectedDoubleForge, M2Share.g_boYbDoubleForge,
            $"1108 QueryId {queryId} runtime forge-mode bit");
        Equal(0, GetQueueCount(GetPrivateField<object>(client, "_responses")),
            "1108 completion queue was not drained");
    }

    private static async Task VerifyItemMovementSmsOutboundAsync(
        YbDbClient client, NetworkStream stream)
    {
        const int payloadSize = 0x78;
        if (InvokePrivateResult<bool>(client,
                "TryEnqueueNativeItemMovementSms", new byte[payloadSize - 1]))
            throw new InvalidOperationException(
                "item-movement SMS accepted a non-0x78 payload");

        var oldServerName = M2Share.g_Config.sServerName;
        try
        {
            M2Share.g_Config.sServerName = "sms-server";
            var owner = new TPlayObject
            {
                m_boOffLineFlag = true,
                m_sUserID = "owner-ptid",
                m_sCharName = "owner-role"
            };
            var actor = new HeroObject
            {
                m_Master = owner,
                m_sMapName = "sms-map",
                m_sCharName = "hero-actor",
                m_nCurrX = 12,
                m_nCurrY = 34
            };
            var stateField = typeof(TBaseObject).GetField(
                "m_boNativeItemMovementSmsEnabled",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (stateField == null)
                throw new InvalidOperationException(
                    "item-movement SMS actor state field was not found");
            stateField.SetValue(actor, true);

            var stdItem = new GoodItem
            {
                Name = "sms-item",
                NativeReserved02 = 0x0200
            };
            var item = new TUserItem { MakeIndex = 0x10203040 };
            var notifyMethod = typeof(TBaseObject).GetMethod(
                "TryNotifyNativeItemMovementSms",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (notifyMethod == null)
                throw new InvalidOperationException(
                    "item-movement SMS actor helper was not found");

            M2Share.LogStringList.Clear();
            if (notifyMethod.Invoke(actor,
                    new object[] { owner, stdItem, item, (byte)0 }) is not true)
                throw new InvalidOperationException(
                    "connected actor item-movement SMS was not enqueued");
            var smsLogs = M2Share.LogStringList.OfType<string>()
                .Where(value => value.StartsWith("153\t",
                    StringComparison.Ordinal))
                .ToArray();
            Equal(1, smsLogs.Length, "item-movement SMS prior log count");
            Equal("153\tsms-map\t12\t34\thero-actor\tsms-item\t270544960\t0\t短信提醒",
                smsLogs[0], "item-movement SMS prior actor log");

            var payload = new byte[payloadSize];
            WriteFixedAsciiCString(payload, 0x00, 0x10, "sms-server");
            WriteFixedAsciiCString(payload, 0x10, 0x20, "owner-ptid");
            WriteFixedAsciiCString(payload, 0x30, 0x20, "owner-role");
            WriteFixedAsciiCString(payload, 0x50, 0x20, "sms-item");
            BinaryPrimitives.WriteInt32LittleEndian(
                payload.AsSpan(0x70, 4), item.MakeIndex);

            var frame = (await ReadFramesAsync(stream, 1))[0];
            AssertFrame(frame, queryId: 0, param: 0, ident: 0x87,
                payload, "item-movement SMS");
        }
        finally
        {
            M2Share.g_Config.sServerName = oldServerName;
            M2Share.LogStringList.Clear();
        }
    }

    private static void WriteFixedAsciiCString(byte[] destination, int offset,
        int capacity, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        if (bytes.Length >= capacity)
            throw new InvalidOperationException(
                "item-movement SMS ASCII fixture exceeds its native field");
        bytes.CopyTo(destination, offset);
    }

    private static List<(string Path, byte[] Original)> PrepareBootstrapFiles()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var rootDirectory = Path.GetFullPath(Path.Combine(baseDirectory, ".."));
        var files = new (string Path, string Content)[]
        {
            (Path.Combine(baseDirectory, "!Setup.txt"),
                "[Server]\r\nServerIndex=0\r\n"),
            (Path.Combine(baseDirectory, "Command.conf"),
                "[Command]\r\nDate=@Date\r\n"),
            (Path.Combine(rootDirectory, "Share", "PlayerUpgradeExp.ini"),
                "[PlayerLevelExp]\r\nLEVEL_1=100\r\n"),
            (Path.Combine(rootDirectory, "Share", "ServerData.ini"),
                "[Integer]\r\nGlobalVal0=0\r\n")
        };
        var snapshots = new List<(string Path, byte[] Original)>(files.Length);
        foreach (var file in files)
        {
            var original = File.Exists(file.Path)
                ? File.ReadAllBytes(file.Path)
                : null;
            snapshots.Add((file.Path, original));
            if (original is { Length: > 0 }) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(file.Path)!);
            File.WriteAllText(file.Path, file.Content);
        }
        return snapshots;
    }

    private static void RestoreBootstrapFiles(
        IEnumerable<(string Path, byte[] Original)> files)
    {
        foreach (var file in files.Reverse())
        {
            try
            {
                if (file.Original == null)
                    File.Delete(file.Path);
                else
                    File.WriteAllBytes(file.Path, file.Original);
            }
            catch
            {
                // Build output cleanup is best effort and does not alter the source tree.
            }
        }
    }

    private static async Task<List<YbDbLegacy77Frame>> ReadFramesAsync(
        NetworkStream stream, int expectedCount)
    {
        var parser = new YbDbLegacy77StreamParser();
        var frames = new List<YbDbLegacy77Frame>();
        var buffer = new byte[128];

        using var cancellation = new CancellationTokenSource(Timeout);
        while (frames.Count < expectedCount)
        {
            var count = await stream.ReadAsync(buffer, cancellation.Token);
            if (count == 0)
                throw new InvalidOperationException(
                    $"YBDB connection closed after {frames.Count} handshake frames");
            parser.Append(buffer.AsSpan(0, count), frames.Add);
        }

        Equal(expectedCount, frames.Count, "handshake frame count");
        Equal(0, parser.BufferedLength, "handshake parser trailing bytes");
        return frames;
    }

    private static async Task VerifyLingFuAccountingAsync(
        YbDbClient client, NetworkStream stream)
    {
        var player = new TPlayObject
        {
            m_boOffLineFlag = true,
            m_sLoginAccount = "登录ABC123",
            m_sUserID = "账号ABC1234567890",
            m_sCharName = "回环角色",
            m_sIPaddr = "192.0.2.55",
            m_sMapName = "loop-map",
            m_btJob = 2,
            m_nPayMent = 0x134,
            m_nPayMode = 0x156,
            m_nLingFu = 1000
        };
        player.m_Abil.Level = 0x123;
        AddOnlinePlayer(player);
        for (var reason = 0; reason < 10; reason++)
        {
            if (!player.DecNativeLingFu(reason, reason + 1))
                throw new InvalidOperationException(
                    $"could not seed DecLF reason bucket {reason}");
        }

        if (!client.RequestLingFuAccounting(player))
            throw new InvalidOperationException("132 accounting request was rejected");
        var request = (await ReadFramesAsync(stream, 1))[0];
        Equal(0, request.QueryId, "132 request QueryId");
        Equal(0, request.Param, "132 request Param");
        Equal((ushort)132, request.Ident, "132 request Ident");
        Equal(108, request.Payload.Length, "132 request payload length");

        if (!YbDbLegacy77Codec.TryDecodeIdentity(request.Payload.AsSpan(0, 64),
                out var identity, out var identityError))
            throw new InvalidOperationException(
                "132 identity could not be decoded: " + identityError);
        Equal("账号ABC123", identity.Field0,
            "132 ShortString[10] narrow PTID");
        Equal("账号ABC1234567890", identity.Field11,
            "132 ShortString[20] full PTID");
        Equal(player.m_sCharName, identity.RoleName, "132 role name");
        Equal(player.m_sIPaddr, identity.Field48, "132 IP address");
        Equal((byte)2, request.Payload[64], "132 job byte");
        Equal((byte)0x23, request.Payload[65], "132 level low byte");
        Equal((byte)0x34, request.Payload[66], "132 payment low byte");
        Equal((byte)0x56, request.Payload[67], "132 pay-mode low byte");
        for (var reason = 0; reason < 10; reason++)
        {
            Equal(reason + 1, BinaryPrimitives.ReadInt32LittleEndian(
                    request.Payload.AsSpan(68 + reason * sizeof(int), sizeof(int))),
                $"132 reason bucket {reason}");
        }
        var buckets = ReadNativeLingFuReasonBuckets(player, out var hasBuckets);
        if (!hasBuckets || buckets[0] != 1 || buckets[9] != 10)
            throw new InvalidOperationException("132 request cleared or changed reason buckets");

        var ackPayload = request.Payload.AsSpan(0, 64).ToArray();
        await SendAccountingAckAsync(client, stream,
            new YbDbLegacy77Frame(0, 77, 1132, ackPayload));
        ReadNativeLingFuReasonBuckets(player, out hasBuckets);
        if (!hasBuckets)
            throw new InvalidOperationException("failed 1132 status cleared reason buckets");

        await SendAccountingAckAsync(client, stream,
            new YbDbLegacy77Frame(1, 88, 1132, ackPayload[..63]));
        ReadNativeLingFuReasonBuckets(player, out hasBuckets);
        if (!hasBuckets)
            throw new InvalidOperationException("short 1132 payload cleared reason buckets");

        if (!player.DecNativeLingFu(0, 7))
            throw new InvalidOperationException("could not add post-request bucket usage");
        Equal(8, ReadNativeLingFuReasonBuckets(player, out hasBuckets)[0],
            "post-request reason bucket");
        await SendAccountingAckAsync(client, stream,
            new YbDbLegacy77Frame(1, int.MinValue, 1132, ackPayload));
        ReadNativeLingFuReasonBuckets(player, out hasBuckets);
        if (hasBuckets)
            throw new InvalidOperationException(
                "successful 1132 did not clear all current reason buckets");

        if (!player.DecNativeLingFu(2, 3))
            throw new InvalidOperationException("could not seed old-ACK race fixture");
        await SendAccountingAckAsync(client, stream,
            new YbDbLegacy77Frame(1, int.MaxValue, 1132, ackPayload));
        ReadNativeLingFuReasonBuckets(player, out hasBuckets);
        if (hasBuckets)
            throw new InvalidOperationException(
                "a repeated old 1132 did not clear newly accumulated buckets");
        if (client.RequestLingFuAccounting(player))
            throw new InvalidOperationException("zero-sum buckets emitted a 132 request");
    }

    private static async Task SendAccountingAckAsync(YbDbClient client,
        NetworkStream stream, YbDbLegacy77Frame frame)
    {
        await stream.WriteAsync(Encode(frame));
        await WaitUntilAsync(
            () => GetQueueCount(GetPrivateField<object>(client, "_responses")) == 1,
            "YbDbClient did not queue the 1132 accounting response");
        client.ProcessCompletions();
        Equal(0, GetQueueCount(GetPrivateField<object>(client, "_responses")),
            "1132 completion queue was not drained");
    }

    private static async Task VerifyCreditRefreshAsync(YbDbClient client,
        NetworkStream stream)
    {
        var player = new TPlayObject
        {
            m_boOffLineFlag = true,
            m_sLoginAccount = "login103",
            m_sUserID = "ptid-103",
            m_sCharName = "元宝角色",
            m_sIPaddr = "192.0.2.103",
            m_nPayMent = 0x23456,
            m_boNativeFirstUsedGiftQualified = true,
            m_nGameGold = 9
        };
        player.m_CreditCard.Loaded = true;
        player.m_CreditCard.Value = 71;
        player.m_CreditCard.Value2 = 72;
        player.m_CreditCard.UsedValue = 73;
        AddOnlinePlayer(player);

        player.BeginNativeYbCreditLoad(12_345);
        var request = (await ReadFramesAsync(stream, 1))[0];
        Equal(0, request.QueryId, "login 103 request QueryId");
        Equal(0x13456, request.Param, "103 request payment/qualification Param");
        Equal((ushort)103, request.Ident, "103 request Ident");
        Equal(64, request.Payload.Length, "103 request payload length");
        Equal(12_345u, player.m_dwNativeYbInitialRetryTick,
            "login 103 retry tick");
        Equal(12_345u, player.m_dwNativeYbRefreshTick,
            "login 103 refresh tick");
        if (player.m_boNativeYbAccountLoaded)
            throw new InvalidOperationException(
                "normal login marked credit loaded before 1103");

        const int silentRetryCount = 32;
        for (var index = 1; index <= silentRetryCount; index++)
            player.RunNativeYbCreditLoad(
                unchecked((uint)(12_345 + index * 15_000)));
        var retries = await ReadFramesAsync(stream, silentRetryCount);
        foreach (var retry in retries)
            Equal(0, retry.QueryId, "silent-service retry QueryId");
        var pendingShape = GetPendingCreditShape(client, player.m_sCharName);
        Equal(1, pendingShape.EpochCount,
            "silent-service retries allocated identity epochs");
        Equal(silentRetryCount + 1, pendingShape.OutstandingCount,
            "silent-service retry outstanding count");
        if (!YbDbLegacy77Codec.TryDecodeIdentity(request.Payload,
                out var identity, out var identityError))
            throw new InvalidOperationException(
                "103 identity could not be decoded: " + identityError);
        Equal("ptid-103", identity.Field0, "103 narrow PTID Field0");
        Equal("ptid-103", identity.Field11, "103 full PTID Field11");
        Equal("元宝角色", identity.RoleName, "103 role name");
        Equal("192.0.2.103", identity.Field48, "103 IP");

        var responsePayload = BuildCreditPayload(player.m_sCharName,
            101, 202, 303, 404);
        await SendCreditResponseAsync(client, stream,
            new YbDbLegacy77Frame(99, 1, 1103, responsePayload[..31]));
        Equal(9, player.m_nGameGold,
            "short 1103 changed current yuanbao");
        Equal(0, player.m_MsgList.Count,
            "short 1103 queued a capital refresh");

        await SendCreditResponseAsync(client, stream,
            new YbDbLegacy77Frame(99, 0, 1103, responsePayload));
        Equal(101, player.m_nGameGold, "1103 current yuanbao");
        Equal(202, player.m_nNativeYbTotalConsumed, "1103 total consumed");
        Equal(303, player.m_nNativeYbRemainingSeconds,
            "1103 remaining seconds");
        Equal(404, player.m_nNativeYbDividendConsumed,
            "1103 dividend consumed");
        if (!player.m_boNativeYbAccountLoaded)
            throw new InvalidOperationException("1103 did not set loaded state");
        if (player.m_boNativeYbDealOpened)
            throw new InvalidOperationException("Param!=1 opened YB deal state");
        Equal(1, player.m_MsgList.Count(entry =>
                entry.wIdent == Grobal2.RM_LINGFU_CHANGED),
            "1103 capital refresh count");
        Equal(71, player.m_CreditCard.Value, "1103 CreditCard.Value isolation");
        Equal(72, player.m_CreditCard.Value2, "1103 CreditCard.Value2 isolation");
        Equal(73, player.m_CreditCard.UsedValue,
            "1103 CreditCard.UsedValue isolation");

        var reconnectPlayer = new TPlayObject
        {
            m_boOffLineFlag = true,
            bo6AB = true,
            m_sLoginAccount = "reconnect103",
            m_sUserID = "reconnect-ptid",
            m_sCharName = "换服元宝角色",
            m_sIPaddr = "192.0.2.104",
            m_nPayMent = 3,
            m_nGameGold = 5
        };
        AddOnlinePlayer(reconnectPlayer);
        reconnectPlayer.BeginNativeYbCreditLoad(13_579);
        if (!reconnectPlayer.m_boNativeYbAccountLoaded)
            throw new InvalidOperationException(
                "connected reconnect login did not use the loaded shortcut");
        var reconnectRequest = (await ReadFramesAsync(stream, 1))[0];
        Equal(0, reconnectRequest.QueryId, "reconnect login 103 QueryId");
        Equal(3, reconnectRequest.Param, "reconnect login 103 Param");
        await SendCreditResponseAsync(client, stream,
            new YbDbLegacy77Frame(0, 0, 1103,
                BuildCreditPayload(reconnectPlayer.m_sCharName, 5, 6, 7, 8)));
        Equal(6, reconnectPlayer.m_nNativeYbTotalConsumed,
            "reconnect 1103 total consumed");
        RemoveOnlinePlayer(reconnectPlayer);

        player.m_boNativeFirstUsedGiftQualified = false;
        player.m_btFirstUsedGiftStage = 2;
        if (!client.RequestCredit(player))
            throw new InvalidOperationException("second 103 request was rejected");
        var unqualified = (await ReadFramesAsync(stream, 1))[0];
        Equal(1, unqualified.QueryId, "manual refresh 103 QueryId");
        Equal(0x3456, unqualified.Param,
            "persistent gift stage leaked into 103 qualification bit");
        await SendCreditResponseAsync(client, stream,
            new YbDbLegacy77Frame(0, 1, 1103,
                BuildCreditPayload(player.m_sCharName, 111, 222, 333, 444)));
        if (!player.m_boNativeYbDealOpened)
            throw new InvalidOperationException(
                "Param==1 did not consume the YB deal one-shot state");
        Equal(2, player.m_MsgList.Count(entry =>
                entry.wIdent == Grobal2.RM_LINGFU_CHANGED),
            "second 1103 capital refresh count");

        if (!client.RequestCredit(player))
            throw new InvalidOperationException("identity fixture request was rejected");
        _ = await ReadFramesAsync(stream, 1);
        player.m_sUserID = "changed-ptid";
        await SendCreditResponseAsync(client, stream,
            new YbDbLegacy77Frame(0, 0, 1103,
                BuildCreditPayload(player.m_sCharName, 999, 999, 999, 999)));
        Equal(111, player.m_nGameGold,
            "account-mismatched 1103 changed current yuanbao");
        player.m_sUserID = "ptid-103";

        if (!client.RequestCredit(player))
            throw new InvalidOperationException("relog fixture request was rejected");
        _ = await ReadFramesAsync(stream, 1);
        RemoveOnlinePlayer(player);
        var replacement = new TPlayObject
        {
            m_boOffLineFlag = true,
            m_sLoginAccount = player.m_sLoginAccount,
            m_sUserID = player.m_sUserID,
            m_sCharName = player.m_sCharName,
            m_sIPaddr = player.m_sIPaddr,
            m_nGameGold = 17
        };
        AddOnlinePlayer(replacement);
        await SendCreditResponseAsync(client, stream,
            new YbDbLegacy77Frame(0, 0, 1103,
                BuildCreditPayload(replacement.m_sCharName, 888, 1, 2, 3)));
        Equal(17, replacement.m_nGameGold,
            "stale-object 1103 changed the replacement player");
        Equal(0, replacement.m_MsgList.Count,
            "stale-object 1103 refreshed the replacement player");
    }

    private static async Task SendCreditResponseAsync(YbDbClient client,
        NetworkStream stream, YbDbLegacy77Frame frame)
    {
        await stream.WriteAsync(Encode(frame));
        await WaitUntilAsync(
            () => GetQueueCount(GetPrivateField<object>(client, "_responses")) == 1,
            "YbDbClient did not queue the 1103 response");
        client.ProcessCompletions();
        Equal(0, GetQueueCount(GetPrivateField<object>(client, "_responses")),
            "1103 completion queue was not drained");
    }

    private static async Task VerifyCreditRelogAndIndependent1104Async(
        YbDbClient client,
        NetworkStream stream)
    {
        const string roleName = "离线重登";
        var oldPlayer = new TPlayObject
        {
            m_boOffLineFlag = true,
            m_sLoginAccount = "logout-login",
            m_sUserID = "ptid-104",
            m_sCharName = roleName,
            m_sIPaddr = "192.0.2.104",
            m_nGameGold = 7
        };
        AddOnlinePlayer(oldPlayer);

        if (!client.RequestInitialCredit(oldPlayer))
            throw new InvalidOperationException("old login 103 was rejected");
        var oldLogin = (await ReadFramesAsync(stream, 1))[0];
        Equal((ushort)103, oldLogin.Ident, "old login request Ident");

        RemoveOnlinePlayer(oldPlayer);
        var replacement = new TPlayObject
        {
            m_boOffLineFlag = true,
            m_sLoginAccount = oldPlayer.m_sLoginAccount,
            m_sUserID = oldPlayer.m_sUserID,
            m_sCharName = oldPlayer.m_sCharName,
            m_sIPaddr = oldPlayer.m_sIPaddr,
            m_nGameGold = 17
        };
        AddOnlinePlayer(replacement);
        if (!client.RequestInitialCredit(replacement))
            throw new InvalidOperationException("replacement login 103 was rejected");

        var replacementLogin = (await ReadFramesAsync(stream, 1))[0];
        Equal((ushort)103, replacementLogin.Ident,
            "replacement login request Ident");

        var pendingBefore1104 = GetPendingCreditShape(client, roleName);
        Equal(2, pendingBefore1104.EpochCount,
            "relog pending identity epoch count before 1104");
        await stream.WriteAsync(Encode(new YbDbLegacy77Frame(
            1, 0, 1104, new byte[28])));
        await WaitUntilAsync(
            () => GetQueueCount(GetPrivateField<object>(client, "_responses")) == 1,
            "YbDbClient did not queue the independent 1104 frame");
        client.ProcessCompletions();
        var pendingAfter1104 = GetPendingCreditShape(client, roleName);
        Equal(2, pendingAfter1104.EpochCount,
            "1104 consumed a pending 103 identity epoch");
        Equal(17, replacement.m_nGameGold,
            "1104 changed replacement yuanbao");

        await SendCreditResponseAsync(client, stream,
            new YbDbLegacy77Frame(0, 0, 1103,
                BuildCreditPayload(roleName, 888, 1, 2, 3)));
        Equal(17, replacement.m_nGameGold,
            "old 1103 changed the replacement player");

        await SendCreditResponseAsync(client, stream,
            new YbDbLegacy77Frame(0, 0, 1103,
                BuildCreditPayload(roleName, 222, 4, 5, 6)));
        Equal(222, replacement.m_nGameGold,
            "replacement 1103 did not apply after the old tombstone");
        RemoveOnlinePlayer(replacement);
    }

    private static async Task VerifyCreditEpochCapAsync(YbDbClient client,
        NetworkStream stream)
    {
        const string roleName = "上限角色";
        const int epochCap = 16;
        var players = new List<TPlayObject>(epochCap + 1);
        for (var index = 0; index <= epochCap; index++)
        {
            players.Add(new TPlayObject
            {
                m_boOffLineFlag = true,
                m_sLoginAccount = "legacy-login",
                m_sUserID = $"cap-ptid-{index:D2}",
                m_sCharName = roleName,
                m_sIPaddr = "192.0.2.105",
                m_nPayMent = index
            });
        }

        for (var index = 0; index < epochCap; index++)
        {
            if (!client.RequestInitialCredit(players[index]))
                throw new InvalidOperationException(
                    $"credit identity epoch {index} was rejected below the cap");
        }

        var accepted = await ReadFramesAsync(stream, epochCap);
        foreach (var frame in accepted)
        {
            Equal(0, frame.QueryId, "identity-epoch request QueryId");
            Equal((ushort)103, frame.Ident, "identity-epoch request Ident");
        }
        await WaitUntilAsync(
            () => GetQueueCount(GetPrivateField<object>(client, "_outbound")) == 0,
            "accepted credit identity epochs did not leave the outbound queue");

        var pendingShape = GetPendingCreditShape(client, roleName);
        Equal(epochCap, pendingShape.EpochCount,
            "pending credit identity epoch cap");
        Equal(1, pendingShape.OutstandingCount,
            "first capped credit identity outstanding count");

        if (client.RequestInitialCredit(players[epochCap]))
            throw new InvalidOperationException(
                "credit identity epoch above the cap was accepted");
        pendingShape = GetPendingCreditShape(client, roleName);
        Equal(epochCap, pendingShape.EpochCount,
            "rejected credit identity changed the epoch count");
        Equal(0, GetQueueCount(GetPrivateField<object>(client, "_outbound")),
            "rejected credit identity entered the outbound queue");
    }

    private static byte[] BuildCreditPayload(string roleName, int currentYuanbao,
        int totalConsumed, int remainingSeconds, int dividendConsumed)
    {
        var roleBytes = HUtil32.GbkEncoding.GetBytes(roleName ?? string.Empty);
        if (roleBytes.Length > 15)
            throw new InvalidOperationException("credit fixture role exceeds 15 GBK bytes");
        var payload = new byte[32];
        payload[0] = (byte)roleBytes.Length;
        roleBytes.CopyTo(payload, 1);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(16, 4),
            currentYuanbao);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(20, 4),
            totalConsumed);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(24, 4),
            remainingSeconds);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(28, 4),
            dividendConsumed);
        return payload;
    }

    private static async Task VerifyFragmentedAndCoalescedInputAsync(
        YbDbClient client, NetworkStream stream)
    {
        var parser = GetPrivateField<YbDbLegacy77StreamParser>(client, "_parser");
        var parserLock = GetPrivateField<object>(client, "_parserLock");
        var responses = GetPrivateField<object>(client, "_responses");

        Equal(0, GetQueueCount(responses), "initial response queue count");
        Equal(0, GetBufferedLength(parser, parserLock),
            "initial client parser buffered bytes");

        var fragmentedFrame = Encode(new YbDbLegacy77Frame(
            0, 0, 1100, Array.Empty<byte>()));
        var responsePayload = EncodeIdentity("LoopbackRole");
        var coalescedFrame = Encode(new YbDbLegacy77Frame(
            5, 0, 1303, responsePayload));

        const int splitAt = 7;
        await stream.WriteAsync(fragmentedFrame.AsMemory(0, splitAt));
        await WaitUntilAsync(
            () => GetBufferedLength(parser, parserLock) == splitAt,
            "YbDbClient did not retain the fragmented frame prefix");
        Equal(0, GetQueueCount(responses),
            "a fragmented prefix must not produce a response frame");

        var combined = new byte[fragmentedFrame.Length - splitAt + coalescedFrame.Length];
        fragmentedFrame.AsSpan(splitAt).CopyTo(combined);
        coalescedFrame.CopyTo(combined.AsSpan(fragmentedFrame.Length - splitAt));
        await stream.WriteAsync(combined);

        await WaitUntilAsync(() => GetQueueCount(responses) == 2,
            "YbDbClient did not decode fragmented/coalesced response frames");
        Equal(0, GetBufferedLength(parser, parserLock),
            "client parser trailing bytes after combined write");

        var decoded = GetQueuedFrames(responses);
        Equal(2, decoded.Length, "decoded response count");
        AssertFrame(decoded[0], 0, 0, 1100,
            Array.Empty<byte>(), "fragmented response");
        AssertFrame(decoded[1], 5, 0, 1303,
            responsePayload, "coalesced response");
    }

    private static async Task VerifyStaleSocketCallbacksAsync(YbDbClient client,
        Socket staleSocket, long staleGeneration, NetworkStream currentStream)
    {
        var parser = GetPrivateField<YbDbLegacy77StreamParser>(client, "_parser");
        var parserLock = GetPrivateField<object>(client, "_parserLock");
        var responses = GetPrivateField<object>(client, "_responses");
        var currentSocket = GetPrivateField<Socket>(client, "_currentSocket");
        var currentGeneration = GetPrivateField<long>(client,
            "_connectionGeneration");

        if (ReferenceEquals(staleSocket, currentSocket))
            throw new InvalidOperationException(
                "restart reused the stale client socket instance");
        if (currentGeneration <= staleGeneration)
            throw new InvalidOperationException(
                "connection generation did not advance after restart");
        Equal(0, GetQueueCount(responses),
            "restart must clear the previous response queue");
        Equal(0, GetBufferedLength(parser, parserLock),
            "restart must clear the previous parser buffer");

        var staleData = Encode(new YbDbLegacy77Frame(
            0, 0, 1100, Array.Empty<byte>()));
        InvokePrivate(client, "SocketRead", client,
            new DSCClientDataInEventArgs(staleSocket, staleData));
        AssertCurrentSessionUnchanged(client, currentSocket, currentGeneration,
            responses, parser, parserLock, "stale read callback");

        var staleArgs = new DSCClientConnectedEventArgs { socket = staleSocket };
        InvokePrivate(client, "SocketDisconnected", client, staleArgs);
        AssertCurrentSessionUnchanged(client, currentSocket, currentGeneration,
            responses, parser, parserLock, "stale disconnect callback");

        InvokePrivate(client, "SocketConnected", client, staleArgs);
        AssertCurrentSessionUnchanged(client, currentSocket, currentGeneration,
            responses, parser, parserLock, "stale connect callback");

        await currentStream.WriteAsync(staleData);
        await WaitUntilAsync(() => GetQueueCount(responses) == 1,
            "current socket stopped receiving after stale callbacks");
        var liveFrames = GetQueuedFrames(responses);
        Equal(1, liveFrames.Length, "current response count after stale callbacks");
        AssertFrame(liveFrames[0], 0, 0, 1100, Array.Empty<byte>(),
            "current response after stale callbacks");
    }

    private static async Task VerifyUnboundedResponseQueueAsync(
        YbDbClient client, NetworkStream stream)
    {
        InvokePrivate(client, "ClearResponses");
        var responses = GetPrivateField<object>(client, "_responses");
        Equal(0, GetQueueCount(responses), "response queue before load test");

        const int frameCount = 5_000;
        var frame = Encode(new YbDbLegacy77Frame(
            0, 0, 1100, Array.Empty<byte>()));
        var data = new byte[frame.Length * frameCount];
        for (var index = 0; index < frameCount; index++)
            frame.CopyTo(data.AsSpan(index * frame.Length));
        await stream.WriteAsync(data);

        await WaitUntilAsync(
            () => GetQueueCount(GetPrivateField<object>(client, "_responses"))
                == frameCount,
            "YbDbClient did not retain all 5000 completed response frames");
        InvokePrivate(client, "ClearResponses");
    }

    private static async Task VerifyUncappedOutboundFlushAsync(
        YbDbClient client, NetworkStream stream)
    {
        var socket = GetPrivateField<Socket>(client, "_currentSocket");
        var generation = GetPrivateField<long>(client, "_connectionGeneration");
        const int frameCount = 100;
        for (var index = 0; index < frameCount; index++)
        {
            var queued = InvokePrivateResult<bool>(client, "EnqueueFrame",
                index, index + 1, (ushort)777, new byte[] { (byte)index },
                socket, generation);
            if (!queued)
                throw new InvalidOperationException(
                    $"outbound frame {index} was not accepted");
        }

        var frames = await ReadFramesAsync(stream, frameCount);
        for (var index = 0; index < frameCount; index++)
            AssertFrame(frames[index], index, index + 1, 777,
                new byte[] { (byte)index }, $"outbound frame {index}");
    }

    private static void AssertCurrentSessionUnchanged(YbDbClient client,
        Socket expectedSocket, long expectedGeneration, object responses,
        YbDbLegacy77StreamParser parser, object parserLock, string name)
    {
        if (!client.Connected)
            throw new InvalidOperationException(name + " disconnected the current session");
        if (!ReferenceEquals(expectedSocket,
                GetPrivateField<Socket>(client, "_currentSocket")))
            throw new InvalidOperationException(name + " replaced the current socket");
        Equal(expectedGeneration,
            GetPrivateField<long>(client, "_connectionGeneration"),
            name + " connection generation");
        Equal(0, GetQueueCount(responses), name + " response queue count");
        Equal(0, GetBufferedLength(parser, parserLock),
            name + " parser buffered bytes");
    }

    private static async Task VerifyEofAsync(NetworkStream stream, string name)
    {
        var eofBuffer = new byte[1];
        var eofLength = await stream.ReadAsync(eofBuffer, 0, eofBuffer.Length)
            .WaitAsync(Timeout);
        Equal(0, eofLength, name);
    }

    private static byte[] EncodeIdentity(string roleName)
    {
        var identity = new YbDbLegacy77Identity
        {
            Field0 = "account",
            Field11 = "account",
            RoleName = roleName,
            Field48 = "127.0.0.1"
        };
        if (!YbDbLegacy77Codec.TryEncodeIdentity(identity,
                out var data, out var error))
            throw new InvalidOperationException(
                "could not encode test identity: " + error);
        return data;
    }

    private static void AddOnlinePlayer(TPlayObject player)
    {
        var field = typeof(UserEngine).GetField("m_PlayObjectList",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (field?.GetValue(M2Share.UserEngine) is not IList<TPlayObject> players)
            throw new InvalidOperationException(
                "UserEngine player list has an unexpected type");
        players.Add(player);
    }

    private static void RemoveOnlinePlayer(TPlayObject player)
    {
        var field = typeof(UserEngine).GetField("m_PlayObjectList",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (field?.GetValue(M2Share.UserEngine) is not IList<TPlayObject> players)
            throw new InvalidOperationException(
                "UserEngine player list has an unexpected type");
        if (!players.Remove(player))
            throw new InvalidOperationException("online player fixture was not present");
    }

    private static int[] ReadNativeLingFuReasonBuckets(TPlayObject player,
        out bool hasBuckets)
    {
        var method = typeof(TPlayObject).GetMethod(
            "TryGetNativeLingFuReasonBuckets",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null)
            throw new InvalidOperationException(
                "native LingFu reason-bucket snapshot method is missing");
        object[] parameters = { null };
        hasBuckets = (bool)method.Invoke(player, parameters);
        return parameters[0] as int[] ?? Array.Empty<int>();
    }

    private static byte[] Encode(YbDbLegacy77Frame frame)
    {
        if (!YbDbLegacy77Codec.TryEncode(frame, out var data, out var error))
            throw new InvalidOperationException("could not encode test frame: " + error);
        return data;
    }

    private static int GetBufferedLength(YbDbLegacy77StreamParser parser,
        object parserLock)
    {
        lock (parserLock) return parser.BufferedLength;
    }

    private static int GetQueueCount(object queue)
    {
        var property = queue.GetType().GetProperty("Count",
            BindingFlags.Instance | BindingFlags.Public);
        if (property?.GetValue(queue) is not int count)
            throw new InvalidOperationException(
                "response queue does not expose an integer Count");
        return count;
    }

    private static (int EpochCount, int OutstandingCount) GetPendingCreditShape(
        YbDbClient client, string roleName)
    {
        var requests = GetPrivateField<object>(client, "_creditRequests");
        if (requests is not IDictionary dictionary || !dictionary.Contains(roleName))
            throw new InvalidOperationException(
                "pending credit role was not retained for silent retries");

        var epochs = dictionary[roleName];
        var epochCount = GetQueueCount(epochs);
        if (epochs is not IEnumerable enumerable)
            throw new InvalidOperationException(
                "pending credit epochs are not enumerable");
        var firstEpoch = enumerable.Cast<object>().FirstOrDefault();
        var outstandingProperty = firstEpoch?.GetType().GetProperty(
            "OutstandingCount", BindingFlags.Instance | BindingFlags.Public);
        if (outstandingProperty?.GetValue(firstEpoch) is not int outstandingCount)
            throw new InvalidOperationException(
                "pending credit epoch has no outstanding count");
        return (epochCount, outstandingCount);
    }

    private static YbDbLegacy77Frame[] GetQueuedFrames(object queue)
    {
        if (queue is not IEnumerable enumerable)
            throw new InvalidOperationException("response queue is not enumerable");

        var frames = new List<YbDbLegacy77Frame>();
        foreach (var queued in enumerable)
        {
            if (queued == null)
                throw new InvalidOperationException("response queue contains null");
            var property = queued.GetType().GetProperty("Frame",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property?.GetValue(queued) is not YbDbLegacy77Frame frame)
                throw new InvalidOperationException(
                    "response queue element does not expose a YBDB Frame");
            frames.Add(frame);
        }
        return frames.ToArray();
    }

    private static void InvokePrivate(object instance, string methodName,
        params object[] arguments)
    {
        var method = instance.GetType().GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null)
            throw new InvalidOperationException(
                $"required private method {methodName} was not found");
        try
        {
            method.Invoke(instance, arguments);
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw new InvalidOperationException(
                $"private method {methodName} failed", ex.InnerException);
        }
    }

    private static T InvokePrivateResult<T>(object instance, string methodName,
        params object[] arguments)
    {
        var method = instance.GetType().GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null)
            throw new InvalidOperationException(
                $"required private method {methodName} was not found");
        try
        {
            if (method.Invoke(instance, arguments) is T result) return result;
            throw new InvalidOperationException(
                $"private method {methodName} returned an unexpected value");
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw new InvalidOperationException(
                $"private method {methodName} failed", ex.InnerException);
        }
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (field?.GetValue(instance) is not T value)
            throw new InvalidOperationException(
                $"required private field {fieldName} was not found");
        return value;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string message)
    {
        var deadline = Environment.TickCount64 + (long)Timeout.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (condition()) return;
            await Task.Delay(10);
        }
        throw new TimeoutException(message);
    }

    private static void AssertFrame(YbDbLegacy77Frame actual,
        int queryId, int param, ushort ident, byte[] payload, string name)
    {
        Equal(queryId, actual.QueryId, name + " QueryId");
        Equal(param, actual.Param, name + " Param");
        Equal(ident, actual.Ident, name + " Ident");
        if (!actual.Payload.AsSpan().SequenceEqual(payload))
            throw new InvalidOperationException(name + " payload mismatch");
    }

    private static void Equal<T>(T expected, T actual, string name)
        where T : IEquatable<T>
    {
        if (!expected.Equals(actual))
            throw new InvalidOperationException(
                $"{name}: expected {expected}, got {actual}");
    }
}
