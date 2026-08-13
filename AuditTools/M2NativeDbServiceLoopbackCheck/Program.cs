using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using DBSvr.Core;
using GameSvr;
using GameSvr.Services;
using SystemModule;
using SystemModule.Packet;
using SystemModule.Sockets;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "!Setup.txt"),
    "[Server]" + Environment.NewLine);
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "Command.conf"),
    "[Command]" + Environment.NewLine);
// M2Share's static ctor also builds ExpsConfig from ..\Share\PlayerUpgradeExp.ini
// (M2Share.cs:1690); without it IniFile.Load throws and no assertion runs.
var shareDirectory = Path.Combine(Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "..")), "Share");
Directory.CreateDirectory(shareDirectory);
File.WriteAllText(Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
    "[PlayerLevelExp]" + Environment.NewLine);
File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"),
    "[Integer]" + Environment.NewLine);

try
{
    await VerifyAsync();
    Console.WriteLine(
        "PASS m2-native-db-loopback registration=reconnect-first " +
        "fifo=registration-before-pending heartbeat=003C " +
        "human=0050-push/0150-one-way stream=fragmented " +
         "type2=static-0065/0066/0067/0068/006C/006E->108->generic-0069/0074 " +
         "receive=preterminal-type1/type3-discard/five-valid-fixed-batch/" +
         "type3-00C9-noop/disconnect-race-preserved-tail-reset/stale-socket-rejected " +
         "offline-gold=fail-closed");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine("M2NativeDbServiceLoopbackCheck FAIL: " + exception);
    return 1;
}

static async Task VerifyAsync()
{
    VerifyOfflineGoldRejected();

    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();

    DBService service = null;
    TcpClient firstClient = null;
    TcpClient secondClient = null;
    try
    {
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        M2Share.nServerIndex = 1;
        M2Share.g_Config = new GameSvrConfig
        {
            sDBAddr = IPAddress.Loopback.ToString(),
            nDBPort = port
        };
        M2Share.LogSystem = new MirLog();
        M2Share.UserEngine = null;
        var pasEnvir = Path.Combine(AppContext.BaseDirectory,
            "LoopbackEnvir");
        Directory.CreateDirectory(pasEnvir);
        M2Share.PasEngine = new GameSvr.PasEngine.PasScriptHost(pasEnvir);

        service = new DBService();
        M2Share.DataServer = service;
        var queued = CreateControlLikeType1(0x7F01);
        Assert(service.SendNativeFrame(queued), "offline native FIFO rejected a valid frame");
        Assert(service.PendingNativeSendCount == 1,
            "offline native FIFO count");
        service.Start();

        firstClient = await AcceptAsync(listener);
        var firstStream = firstClient.GetStream();
        var registration = await ReadFrameAsync(firstStream);
        AssertControl(registration, 0x003D, 0, 2, "first registration");
        var firstGeneration = GetPrivateInt(service, "_connectionGeneration");

        var pending = await ReadFrameAsync(firstStream);
        Assert(pending.Type == 1, "offline pending frame type");
        Assert(ReadCommand(pending) == 0x7F01,
            "registration was not sent before the pending FIFO frame");
        Assert(service.PendingNativeSendCount == 0,
            "pending FIFO was not drained after registration");

        SetPrivateLong(service, "_nextHeartbeatAt", 0);
        service.Pulse();
        var heartbeat = await ReadFrameAsync(firstStream);
        AssertControl(heartbeat, 0x003C, 0, 0, "heartbeat");

        var monsterPush = CreateMonsterPush(completed: true);
        var preTerminalType3 = new byte[0x40];
        BinaryPrimitives.WriteUInt16LittleEndian(preTerminalType3, 0x00C9);
        for (var index = 0; index < 6; index++)
            await firstStream.WriteAsync(CreateControlLikeType1(
                unchecked((ushort)(0x7F00 + index))));
        await firstStream.WriteAsync(Encode(new LegacyDbServerFrame(
            3, 0, preTerminalType3)));
        await firstStream.WriteAsync(Encode(new LegacyDbServerFrame(
            2, 0, CreateType2ControlPayload(
                NativeType2FieldHeroSnapshotState.Command, 0, 0))));
        await firstStream.WriteAsync(Encode(new LegacyDbServerFrame(
            2, 0, CreateType2ControlPayload(
                NativeType2SecondaryRankingState.ClearCommand, 0, 0))));
        await firstStream.WriteAsync(Encode(new LegacyDbServerFrame(
            2, 0, CreateSecondaryRankingPayload(3, 0, 0x31))));
        await firstStream.WriteAsync(monsterPush);
        await firstStream.FlushAsync();
        await WaitUntilAsync(
            () => GetMonsterSnapshot(service).Records.Count == 1,
            "startup Type2 was not consumed directly by the socket callback");
        service.Pulse();
        var monsterSnapshot = GetMonsterSnapshot(service);
        Assert(monsterSnapshot.Records.Count == 1,
            "native Type2 0x0067 was not dispatched to the monster snapshot");
        Assert(!GetFieldHeroSnapshot(service).Completed
            && GetReceivedFrameCount(service) == 0
            && GetSecondaryRankings(service).TotalRecordCount == 0,
            "pre-terminal native frames escaped the direct Type2 phase");
        Assert(BinaryPrimitives.ReadInt32LittleEndian(
                monsterSnapshot.Records[0].CopyNativeFields().AsSpan(0x20, 4))
            == 0x12345678, "native Type2 0x0067 dword HP");
        Assert(service.NativeMonsterDefinitionsPublished
            && service.MonsterRuntimeCatalog.Ready
            && service.MonsterRuntimeCatalog.Definitions.Count == 1,
            "native Type2 0x0067 terminal publication");

        var humanMagicPush = CreateMagicPush(
            NativeType2MagicSnapshotState.HumanMagicCommand, 62, true);
        var heroMagicPush = CreateMagicPush(
            NativeType2MagicSnapshotState.HeroMagicCommand, 69, true);
        await firstStream.WriteAsync(humanMagicPush);
        await firstStream.WriteAsync(heroMagicPush);
        await firstStream.FlushAsync();
        for (var index = 0; index < 10; index++)
        {
            service.Pulse();
            await Task.Delay(10);
        }
        var magicSnapshot = GetMagicSnapshot(service);
        Assert(magicSnapshot.HumanCompleted && magicSnapshot.HeroCompleted
            && magicSnapshot.HumanRecords.Count == 1
            && magicSnapshot.HeroRecords.Count == 1,
            "native Type2 0x0065/0x0066 were not dispatched to the magic snapshot");
        Assert(magicSnapshot.HumanRecords[0].CopyRecord()[0x1A] == 100
            && magicSnapshot.HeroRecords[0].CopyRecord()[0x1A] == 99,
            "native Type2 magic record corrections");

        var endpointPrefixPayload = CreateEndpointSlotsPayload(2,
            unchecked((int)0x76543210), 0x70);
        await firstStream.WriteAsync(Encode(new LegacyDbServerFrame(2, 0,
            endpointPrefixPayload)));
        await firstStream.FlushAsync();
        for (var index = 0; index < 10; index++)
        {
            service.Pulse();
            await Task.Delay(10);
        }
        var endpointSlots = GetEndpointSlots(service);
        var firstEndpointSlot = CopyEndpointRecord(endpointPrefixPayload, 1);
        var secondEndpointSlot = CopyEndpointRecord(endpointPrefixPayload, 2);
        Assert(endpointSlots.CopySlot(1).AsSpan().SequenceEqual(firstEndpointSlot)
            && endpointSlots.CopySlot(2).AsSpan().SequenceEqual(secondEndpointSlot),
            "native Type2 0x006E was not dispatched to opaque endpoint slots");

        var endpointReplacementPayload = CreateEndpointSlotsPayload(1, -7, 0xA0);
        await firstStream.WriteAsync(Encode(new LegacyDbServerFrame(2, 0,
            endpointReplacementPayload)));
        await firstStream.FlushAsync();
        for (var index = 0; index < 10; index++)
        {
            service.Pulse();
            await Task.Delay(10);
        }
        Assert(endpointSlots.CopySlot(1).AsSpan().SequenceEqual(
                CopyEndpointRecord(endpointReplacementPayload, 1))
            && endpointSlots.CopySlot(2).AsSpan().SequenceEqual(secondEndpointSlot)
            && endpointSlots.CopySlot(0).All(value => value == 0),
            "native Type2 0x006E prefix overwrite or slot zero behavior");

        VerifyStdItemNumericExceptionEscapes(service);

        var stdItemFirstPush = Encode(new LegacyDbServerFrame(2, 0,
            CreateStdItemPayload(1, "LoopbackStdItem",
                "攻击下限:42", completed: false)));
        await firstStream.WriteAsync(stdItemFirstPush);
        await firstStream.FlushAsync();
        await WaitUntilAsync(() => GetStdItemSnapshot(service).Records.Count == 1,
            "native Type2 0x0068 non-terminal record was not consumed");
        Assert(!service.NativeStdItemDefinitionsPublished
            && !service.StdItemRuntimeCatalog.Ready
            && service.StdItemRuntimeCatalog.Count == 0,
            "native Type2 0x0068 published before its terminal packet");
        Assert(!service.TryWaitForNativeDefinitionInitialization(20,
                out var incompleteStdItemError)
            && incompleteStdItemError.Contains("标准物品",
                StringComparison.Ordinal),
            "native Type2 0x0068 incomplete wait gate");

        var stdItemTerminalPush = Encode(new LegacyDbServerFrame(2, 0,
            CreateStdItemPayload(2, "LoopbackTerm", "",
                completed: true)));
        await firstStream.WriteAsync(stdItemTerminalPush);
        await firstStream.FlushAsync();
        await WaitUntilAsync(() => service.NativeStdItemDefinitionsPublished,
            "native Type2 0x0068 terminal catalog was not published");
        var stdItemSnapshot = GetStdItemSnapshot(service);
        Assert(stdItemSnapshot.InitialNativeListCount
            == NativeType2StdItemSnapshotState.VerifiedOriginalStartupListCount,
            "native Type2 0x0068 startup baseline");
        Assert(stdItemSnapshot.Completed && stdItemSnapshot.Records.Count == 2,
            "native Type2 0x0068 was not dispatched to the standard-item snapshot");
        Assert(stdItemSnapshot.Records[0].WireIndex == 1
            && stdItemSnapshot.Records[1].WireIndex == 2
            && stdItemSnapshot.ExpectedWireIndex == 3,
            "native Type2 0x0068 first record index");
        Assert(BinaryPrimitives.ReadUInt16LittleEndian(
                stdItemSnapshot.Records[0].CopyExtensionSlots().AsSpan(2, 2))
            == 42, "native Type2 0x0068 extension slot");
        Assert(service.StdItemRuntimeCatalog.Ready
            && service.StdItemRuntimeCatalog.Count == 3
            && service.StdItemRuntimeCatalog.Items[0].Name == "金币"
            && service.StdItemRuntimeCatalog.Items[1].Name ==
                "LoopbackStdItem"
            && service.StdItemRuntimeCatalog.Items[2].Name ==
                "LoopbackTerm",
            "native Type2 0x0068 atomic production catalog");

        var fieldHeroPush = CreateFieldHeroPush();
        await firstStream.WriteAsync(fieldHeroPush);
        await firstStream.FlushAsync();
        for (var index = 0; index < 10; index++)
        {
            service.Pulse();
            await Task.Delay(10);
        }
        var fieldHeroSnapshot = GetFieldHeroSnapshot(service);
        Assert(fieldHeroSnapshot.Completed && fieldHeroSnapshot.Records.Count == 1,
            "native Type2 0x006C was not dispatched to the field hero snapshot");
        Assert(fieldHeroSnapshot.Records[0].CopyWireBody()[0x138] == 0x5A,
            "native Type2 0x006C raw body");
        Assert(service.TryWaitForNativeDefinitionInitialization(1_000,
                out var nativeDefinitionError),
            "native static definition wait gate: " + nativeDefinitionError);
        VerifyMonsterProductionPublication(service.MonsterRuntimeCatalog);

        var rankingBody = CreateSecondaryRankingPayload(3,
            unchecked((int)0x87654321), 0x51);
        await firstStream.WriteAsync(Encode(new LegacyDbServerFrame(2, 0,
            CreateType2ControlPayload(
                NativeType2SecondaryRankingState.ClearCommand, 0, 0))));
        await firstStream.WriteAsync(Encode(new LegacyDbServerFrame(
            2, 0, rankingBody)));
        await firstStream.WriteAsync(Encode(new LegacyDbServerFrame(2, 0,
            CreateType2ControlPayload(
                NativeType2SecondaryRankingState.RecordCommand,
                NativeType2SecondaryRankingState.FinalizeCategory,
                0x12345678))));
        await firstStream.WriteAsync(CreateMonsterPush(
            unchecked((int)0xDEADBEEF)));
        await firstStream.FlushAsync();
        for (var index = 0; index < 10; index++)
        {
            service.Pulse();
            await Task.Delay(10);
        }
        var secondaryRankings = GetSecondaryRankings(service);
        Assert(secondaryRankings.TotalRecordCount == 1
            && secondaryRankings.GetBucket(3).Count == 1
            && secondaryRankings.GetBucket(3)[0].CopyBody().AsSpan()
                .SequenceEqual(rankingBody.AsSpan(
                    NativeType2SecondaryRankingState.HeaderSize))
            && secondaryRankings.LastFinalizeValue == 0x5678
            && secondaryRankings.Level999OrHigherCount == 7,
            "post-terminal native Type2 0x0074/0x0069 routing");
        Assert(monsterSnapshot.Records.Count == 1
            && BinaryPrimitives.ReadInt32LittleEndian(
                monsterSnapshot.Records[0].CopyNativeFields().AsSpan(0x20, 4))
                == 0x12345678,
            "post-terminal static Type2 frame re-entered the startup receiver");

        VerifyReceiveQueueInvariants(service);

        const string account = "loopback_account";
        const string character = "LoopbackHero";
        var humanPush = CreateHumanPush(account, character);
        await firstStream.WriteAsync(humanPush.AsMemory(0, 7));
        await firstStream.FlushAsync();
        await Task.Delay(10);
        await firstStream.WriteAsync(humanPush.AsMemory(7));
        await firstStream.FlushAsync();

        for (var index = 0; index < 30; index++)
        {
            service.Pulse();
            await Task.Delay(10);
        }

        THumDataInfo human = null;
        NativeHumanLoadData nativeLoad = null;
        Assert(HumDataService.LoadHumRcdFromDB(account, character,
            "127.0.0.1", ref human, 0, out nativeLoad),
            "native 0x0050 push was not available to the human loader");
        Assert(human?.Data != null, "native 0x0050 decoded no human record");
        Assert(human.Data.nGold == 0x12345678, "native 0x0050 raw human data mismatch");
        Assert(nativeLoad?.SessionSuffix?.Length
            == NativeHumanDbCodec.SessionSuffixSize,
            "native 0x0050 session suffix was dropped by the human loader");
        Assert(nativeLoad.SessionSuffix[0] == 0xA1
            && nativeLoad.SessionSuffix[0x92] == 0xB2,
            "native 0x0050 session suffix bytes");

        human.Data.nGold = unchecked((int)0x89ABCDEF);
        Assert(HumDataService.SaveHumRcdToDB(account, character, 3, 0, 0,
            human), "native 0x0150 save did not enter the FIFO");
        var save = await ReadFrameAsync(firstStream);
        Assert(save.Type == 1 && ReadCommand(save) == 0x0150,
            "native human save command");
        Assert(BinaryPrimitives.ReadUInt16LittleEndian(save.Payload.AsSpan(2, 2)) == 3,
            "native human save mode");
        Assert(BinaryPrimitives.ReadInt32LittleEndian(save.Payload.AsSpan(
            NativeHumanDbCodec.NativeDataOffset + 0x44, 4))
            == unchecked((int)0x89ABCDEF), "native human save raw mutation");
        Assert(service.FlushPendingSendsAndWait(100),
            "native send FIFO did not report drained after synchronous save");

        var disconnectedSocket = VerifyDisconnectReceiveRace(service,
            secondaryRankings);
        firstClient.Close();
        SetPrivateLong(service, "_nextReconnectAt", 0);
        service.Pulse();

        secondClient = await AcceptAsync(listener);
        var reconnectRegistration = await ReadFrameAsync(secondClient.GetStream());
        AssertControl(reconnectRegistration, 0x003D, 0, 2,
            "reconnect registration");
        var reconnectGeneration = GetPrivateInt(service, "_connectionGeneration");
        Assert(reconnectGeneration > firstGeneration,
            "reconnect did not advance native DB generation");
        VerifyStaleSocketRejected(service, disconnectedSocket,
            secondaryRankings);
        var preservedMagicSnapshot = GetMagicSnapshot(service);
        Assert(preservedMagicSnapshot.HumanCompleted
            && preservedMagicSnapshot.HeroCompleted
            && preservedMagicSnapshot.HumanRecords.Count == 1
            && preservedMagicSnapshot.HeroRecords.Count == 1,
            "reconnect incorrectly reset persistent Type2 magic definitions");
        var preservedMonsterSnapshot = GetMonsterSnapshot(service);
        Assert(preservedMonsterSnapshot.Completed
            && preservedMonsterSnapshot.Records.Count == 1
            && BinaryPrimitives.ReadInt32LittleEndian(
                preservedMonsterSnapshot.Records[0].CopyNativeFields()
                    .AsSpan(0x20, 4)) == 0x12345678,
            "reconnect incorrectly reset persistent Type2 monster definitions");
        Assert(service.NativeMonsterDefinitionsPublished
            && service.MonsterRuntimeCatalog.Definitions.Count == 1,
            "reconnect incorrectly reset published Type2 monster catalog");
        var preservedFieldHeroSnapshot = GetFieldHeroSnapshot(service);
        Assert(preservedFieldHeroSnapshot.Completed
            && preservedFieldHeroSnapshot.Records.Count == 1
            && preservedFieldHeroSnapshot.Records[0].CopyWireBody()[0x138] == 0x5A,
            "reconnect incorrectly reset persistent Type2 field hero definitions");
        var preservedStdItemSnapshot = GetStdItemSnapshot(service);
        Assert(preservedStdItemSnapshot.Completed
            && preservedStdItemSnapshot.Records.Count == 2
            && preservedStdItemSnapshot.InitialNativeListCount
                == NativeType2StdItemSnapshotState.VerifiedOriginalStartupListCount
            && preservedStdItemSnapshot.ExpectedWireIndex == 3,
            "reconnect incorrectly reset persistent Type2 standard-item definitions");
        var preservedStdItemCatalog = service.StdItemRuntimeCatalog;
        Assert(service.NativeStdItemDefinitionsPublished
            && preservedStdItemCatalog.Ready
            && preservedStdItemCatalog.Count == 3,
            "reconnect incorrectly reset published Type2 standard-item catalog");
        var secondStream = secondClient.GetStream();
        var reconnectEndpointPayload = CreateEndpointSlotsPayload(1,
            unchecked((int)0x11223344), 0xD0);
        var reconnectRankingPayload = CreateSecondaryRankingPayload(4,
            unchecked((int)0x10203040), 0x61);
        await secondStream.WriteAsync(humanMagicPush);
        await secondStream.WriteAsync(fieldHeroPush);
        await secondStream.WriteAsync(stdItemTerminalPush);
        await secondStream.WriteAsync(Encode(new LegacyDbServerFrame(2, 0,
            reconnectEndpointPayload)));
        await secondStream.WriteAsync(Encode(new LegacyDbServerFrame(2, 0,
            CreateType2ControlPayload(
                NativeType2SecondaryRankingState.ClearCommand, 0, 0))));
        await secondStream.WriteAsync(Encode(new LegacyDbServerFrame(
            2, 0, reconnectRankingPayload)));
        await secondStream.WriteAsync(Encode(new LegacyDbServerFrame(2, 0,
            CreateType2ControlPayload(
                NativeType2SecondaryRankingState.RecordCommand,
                NativeType2SecondaryRankingState.FinalizeCategory,
                unchecked((int)0xABCDC0DE)))));
        await secondStream.FlushAsync();
        for (var index = 0; index < 10; index++)
        {
            service.Pulse();
            await Task.Delay(10);
        }
        Assert(preservedMagicSnapshot.HumanRecords.Count == 1,
            "completed native Type2 magic stream accepted a reconnect duplicate");
        Assert(preservedFieldHeroSnapshot.Records.Count == 1,
            "completed native Type2 field hero stream accepted a reconnect duplicate");
        Assert(preservedStdItemSnapshot.Records.Count == 2
            && preservedStdItemSnapshot.ExpectedWireIndex == 3
            && ReferenceEquals(preservedStdItemCatalog,
                service.StdItemRuntimeCatalog)
            && service.StdItemRuntimeCatalog.Count == 3,
            "completed native Type2 standard-item stream accepted a reconnect duplicate");
        var preservedEndpointSlots = GetEndpointSlots(service);
        Assert(preservedEndpointSlots.CopySlot(1).AsSpan().SequenceEqual(
                CopyEndpointRecord(endpointReplacementPayload, 1))
            && preservedEndpointSlots.CopySlot(2).AsSpan().SequenceEqual(secondEndpointSlot),
            "reconnect re-entered static Type2 endpoint initialization");
        Assert(secondaryRankings.TotalRecordCount == 1
            && secondaryRankings.GetBucket(3).Count == 0
            && secondaryRankings.GetBucket(4).Count == 1
            && secondaryRankings.GetBucket(4)[0].CopyBody().AsSpan()
                .SequenceEqual(reconnectRankingPayload.AsSpan(
                    NativeType2SecondaryRankingState.HeaderSize))
            && secondaryRankings.LastFinalizeValue == 0xC0DE
            && secondaryRankings.Level999OrHigherCount == 0,
            "reconnect did not preserve the generic Type2 phase");
    }
    finally
    {
        service?.Dispose();
        firstClient?.Dispose();
        secondClient?.Dispose();
        listener.Stop();
        M2Share.DataServer = null;
        M2Share.UserEngine = null;
        M2Share.g_Config = null;
        M2Share.LogSystem = null;
    }
}

static void VerifyOfflineGoldRejected()
{
    var engine = new TFrontEngine();
    var method = typeof(TFrontEngine).GetMethod("ChangeUserGoldInDB",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(method != null, "missing native offline gold handler");
    var arguments = new object[]
    {
        new TGoldChangeInfo { sGetGoldUser = "OfflineGoldTarget", nGold = 1 },
        null
    };
    var result = method.Invoke(engine, arguments);
    Assert(string.Equals(result?.ToString(), "Rejected", StringComparison.Ordinal),
        "native offline gold request was not rejected");
    Assert(arguments[1] is string reason
           && reason.Contains("不支持M2主动读取离线人物档案", StringComparison.Ordinal),
        "native offline gold rejection reason");
}

static void VerifyReceiveQueueInvariants(DBService service)
{
    var generation = GetPrivateInt(service, "_connectionGeneration");
    Assert(generation > 0, "native DB connection generation was not established");
    var magicCount = GetMagicSnapshot(service).HumanRecords.Count;
    var monsterCount = GetMonsterSnapshot(service).Records.Count;
    var stdItemCount = GetStdItemSnapshot(service).Records.Count;
    var fieldHeroCount = GetFieldHeroSnapshot(service).Records.Count;

    for (var index = 0; index < 5; index++)
        EnqueueReceivedFrame(service,
            new LegacyDbServerFrame(1, 0, new byte[0x47]));
    EnqueueReceivedFrame(service,
        new LegacyDbServerFrame(2, 0, new byte[12]));
    service.Pulse();
    Assert(GetReceivedFrameCount(service) == 0,
        "short Type1 frames consumed the five-valid-frame budget");

    var type3Payload = new byte[0x40];
    BinaryPrimitives.WriteUInt16LittleEndian(type3Payload, 0x00C9);
    EnqueueReceivedFrame(service,
        new LegacyDbServerFrame(3, 0, type3Payload));
    service.Pulse();
    Assert(GetReceivedFrameCount(service) == 0
        && GetMagicSnapshot(service).HumanRecords.Count == magicCount
        && GetMonsterSnapshot(service).Records.Count == monsterCount
        && GetStdItemSnapshot(service).Records.Count == stdItemCount
        && GetFieldHeroSnapshot(service).Records.Count == fieldHeroCount,
        "native Type3 0x00C9 did not remain a no-op");

    for (var index = 0; index < 6; index++)
        EnqueueReceivedFrame(service, CreateControlLikeType1Frame(0x7F10));
    EnqueueReceivedFrame(service,
        new LegacyDbServerFrame(2, 0, new byte[12]));
    service.Pulse();
    Assert(GetReceivedFrameCount(service) == 2,
        "fifth valid Type1 did not stop the remaining native FIFO");
    EnqueueReceivedFrame(service,
        new LegacyDbServerFrame(3, 0, type3Payload));
    service.Pulse();
    Assert(GetReceivedFrameCount(service) == 1,
        "new receive data entered the active fixed batch");
    service.Pulse();
    Assert(GetReceivedFrameCount(service) == 0,
        "next fixed receive batch was not resumed");

    var rankingState = GetSecondaryRankings(service);
    var priorRankingCount = rankingState.TotalRecordCount;
    var preservedPayload = CreateSecondaryRankingPayload(1,
        unchecked((int)0x76543210), 0xA1);
    EnqueueReceivedFrame(service,
        new LegacyDbServerFrame(2, 0, preservedPayload));
    service.Pulse();
    Assert(rankingState.TotalRecordCount == priorRankingCount + 1
        && rankingState.GetBucket(1).Last().CopyBody().AsSpan()
            .SequenceEqual(preservedPayload.AsSpan(
                NativeType2SecondaryRankingState.HeaderSize)),
        "native receive batch did not dispatch its Type2 frame");
}

static Socket VerifyDisconnectReceiveRace(DBService service,
    NativeType2SecondaryRankingState rankingState)
{
    Assert(service.Connected, "disconnect race requires a live DB connection");
    Assert(GetReceivedFrameCount(service) == 0
           && GetParserBufferedLength(service) == 0,
        "disconnect race requires an empty receive queue and parser");

    var clientSocket = GetClientSocket(service);
    var activeSocket = GetCurrentClientSocket(clientSocket);
    var parserLock = GetPrivateField(service, "_parserLock");
    var type2Lock = GetPrivateField(service, "_type2Lock");
    var generationBeforeDisconnect = GetPrivateInt(service,
        "_connectionGeneration");
    var totalBefore = rankingState.TotalRecordCount;
    var bucketBefore = rankingState.GetBucket(2).Count;
    var firstPayload = CreateSecondaryRankingPayload(2,
        unchecked((int)0x55667788), 0x91);
    var secondPayload = CreateSecondaryRankingPayload(2,
        unchecked((int)0x66778899), 0xA2);
    var firstFrame = Encode(new LegacyDbServerFrame(2, 0, firstPayload));
    var secondFrame = Encode(new LegacyDbServerFrame(2, 0, secondPayload));
    var parserTailFrame = CreateControlLikeType1(0x7F22);
    var datagram = new byte[firstFrame.Length + secondFrame.Length + 7];
    firstFrame.CopyTo(datagram, 0);
    secondFrame.CopyTo(datagram, firstFrame.Length);
    parserTailFrame.AsSpan(0, 7).CopyTo(datagram.AsSpan(
        firstFrame.Length + secondFrame.Length));

    Task receiveTask = null;
    Task disconnectTask = null;
    Monitor.Enter(type2Lock);
    try
    {
        receiveTask = Task.Run(() => InvokeDbSocketRead(service, clientSocket,
            activeSocket, datagram));
        Assert(SpinWait.SpinUntil(
                () => IsLockHeldByAnotherThread(parserLock), 5_000),
            "receive callback did not enter the parser before disconnect");

        disconnectTask = Task.Run(() => clientSocket.Disconnect(activeSocket));
        Assert(SpinWait.SpinUntil(
                () => !clientSocket.IsCurrentConnection(activeSocket), 5_000),
            "disconnect race did not clear the active socket state");
        Assert(!disconnectTask.IsCompleted,
            "disconnect callback did not contend with the in-flight parser");
        Assert(GetPrivateInt(service, "_connectionGeneration")
               == generationBeforeDisconnect,
            "disconnect advanced generation before the in-flight append completed");
    }
    finally
    {
        Monitor.Exit(type2Lock);
    }

    Assert(Task.WaitAll(new[] { receiveTask, disconnectTask }, 5_000),
        "disconnect receive race did not finish");
    Assert(GetPrivateInt(service, "_connectionGeneration")
           > generationBeforeDisconnect,
        "disconnect did not advance the native DB generation");
    Assert(GetReceivedFrameCount(service) == 2,
        "disconnect discarded a complete frame from one parser append");
    Assert(GetParserBufferedLength(service) == 0,
        "disconnect retained the incomplete native stream tail");

    service.Pulse();
    var bucket = rankingState.GetBucket(2);
    Assert(rankingState.TotalRecordCount == totalBefore + 2
           && bucket.Count == bucketBefore + 2
           && bucket[bucketBefore].CopyBody().AsSpan().SequenceEqual(
               firstPayload.AsSpan(NativeType2SecondaryRankingState.HeaderSize))
           && bucket[bucketBefore + 1].CopyBody().AsSpan().SequenceEqual(
               secondPayload.AsSpan(NativeType2SecondaryRankingState.HeaderSize)),
        "disconnect did not preserve both complete frames in parser order");
    return activeSocket;
}

static void VerifyStaleSocketRejected(DBService service, Socket staleSocket,
    NativeType2SecondaryRankingState rankingState)
{
    Assert(service.Connected && staleSocket != null,
        "stale socket check requires a reconnected DB service");
    Assert(GetReceivedFrameCount(service) == 0,
        "stale socket check requires an empty receive queue");
    var totalBefore = rankingState.TotalRecordCount;
    var stalePayload = CreateSecondaryRankingPayload(1,
        unchecked((int)0x778899AA), 0xB3);
    InvokeDbSocketRead(service, GetClientSocket(service), staleSocket,
        Encode(new LegacyDbServerFrame(2, 0, stalePayload)));
    service.Pulse();
    Assert(GetReceivedFrameCount(service) == 0
           && rankingState.TotalRecordCount == totalBefore,
        "an old socket callback entered the reconnected DB generation");
}

static bool IsLockHeldByAnotherThread(object sync)
{
    if (!Monitor.TryEnter(sync)) return true;
    Monitor.Exit(sync);
    return false;
}

static object GetPrivateField(DBService service, string fieldName)
{
    var field = typeof(DBService).GetField(fieldName,
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(field != null, "missing DBService field " + fieldName);
    return field.GetValue(service);
}

static IClientScoket GetClientSocket(DBService service)
{
    var socket = GetPrivateField(service, "_clientScoket") as IClientScoket;
    Assert(socket != null, "invalid DBService client socket");
    return socket;
}

static Socket GetCurrentClientSocket(IClientScoket clientSocket)
{
    var field = typeof(IClientScoket).GetField("cli",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(field != null, "missing native DB active socket field");
    var socket = field.GetValue(clientSocket) as Socket;
    Assert(socket != null && clientSocket.IsCurrentConnection(socket),
        "invalid native DB active socket");
    return socket;
}

static void InvokeDbSocketRead(DBService service, IClientScoket sender,
    Socket socket, byte[] datagram)
{
    var method = typeof(DBService).GetMethod("DBSocketRead",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(method != null, "missing DBService receive callback");
    method.Invoke(service,
        new object[] { sender, new DSCClientDataInEventArgs(socket, datagram) });
}

static byte[] CreateControlLikeType1(ushort command)
{
    return Encode(CreateControlLikeType1Frame(command));
}

static LegacyDbServerFrame CreateControlLikeType1Frame(ushort command)
{
    var payload = new byte[0x48];
    BinaryPrimitives.WriteUInt16LittleEndian(payload, command);
    return new LegacyDbServerFrame(1, 0, payload);
}

static byte[] CreateHumanPush(string account, string character)
{
    var raw = new byte[NativeHumanDataCodec.DataRecordSize];
    WriteShortString(raw, 0x00, 15, character);
    WriteShortString(raw, 0x10, 15, "3");
    WriteShortString(raw, 0x20, 20, account);
    raw[0x3E] = 1;
    raw[0x3F] = 1;
    raw[0x40] = 2;
    BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(0x3C, 2), 77);
    BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0x44, 4), 0x12345678);

    var payload = new byte[NativeHumanDbCodec.ScriptDataOffset];
    BinaryPrimitives.WriteUInt16LittleEndian(payload, NativeHumanDbCodec.LoadCommand);
    WriteShortString(payload, NativeHumanDbCodec.AccountOffset, 20, account);
    WriteShortString(payload, NativeHumanDbCodec.CharacterOffset, 15, character);
    raw.CopyTo(payload, NativeHumanDbCodec.NativeDataOffset);
    payload[NativeHumanDbCodec.SessionSuffixOffset] = 0xA1;
    payload[NativeHumanDbCodec.SessionSuffixOffset + 0x92] = 0xB2;
    return Encode(new LegacyDbServerFrame(1, 0, payload));
}

static byte[] CreateMonsterPush(int hp = 0x12345678,
    bool completed = false)
{
    var payload = new byte[NativeType2MonsterSnapshotState.HeaderSize
                           + NativeType2MonsterSnapshotState.MinimumBodySize];
    BinaryPrimitives.WriteUInt16LittleEndian(payload,
        NativeType2MonsterSnapshotState.Command);
    if (completed)
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), 1);
    var body = payload.AsSpan(NativeType2MonsterSnapshotState.HeaderSize);
    var name = Encoding.ASCII.GetBytes("LoopbackMonster");
    body[0x04] = (byte)name.Length;
    name.CopyTo(body.Slice(0x05));
    body[0x14] = 86;
    BinaryPrimitives.WriteInt32LittleEndian(body.Slice(0x20, 4), hp);
    BinaryPrimitives.WriteInt32LittleEndian(body.Slice(0x24, 4),
        0x23456789);
    BinaryPrimitives.WriteUInt16LittleEndian(body.Slice(0x38, 2), 99);
    BinaryPrimitives.WriteUInt16LittleEndian(body.Slice(0x3E, 2), 77);
    return Encode(new LegacyDbServerFrame(2, 0, payload));
}

static void VerifyMonsterProductionPublication(
    NativeType2MonsterRuntimeCatalog catalog)
{
    var temporaryRoot = Path.Combine(Path.GetTempPath(),
        "loym2-monster-0067-" + Guid.NewGuid().ToString("N"));
    var previousConfigPath = M2Share.sConfigPath;
    var previousEnvirDirectory = M2Share.g_Config.sEnvirDir;
    var previousLocalDb = M2Share.LocalDB;
    var previousUserEngine = M2Share.UserEngine;
    var previousObjectManager = M2Share.ObjectManager;
    try
    {
        var monItemsDirectory = Path.Combine(temporaryRoot, "Envir",
            "MonItems");
        Directory.CreateDirectory(monItemsDirectory);
        File.WriteAllText(Path.Combine(monItemsDirectory,
                "LoopbackMonster.txt"),
            "1/1   LoopbackDrop   2" + Environment.NewLine);
        M2Share.sConfigPath = temporaryRoot;
        M2Share.g_Config.sEnvirDir = "Envir";
        M2Share.LocalDB = new LocalDB();
        M2Share.UserEngine = new UserEngine();
        M2Share.ObjectManager = new ObjectManager();
        // LoadMonitems keeps a row only when its item name resolves in the
        // standard-item table -- native sub_6799E0 @0x679BB4 `call sub_74C2D4`,
        // @0x679BC1 `cmp [ebp-0x2C],0 / je 0x679BDD` (no record allocated) and
        // @0x679BDD `cmp [ebp-8],0 / je 0x679C4E` (row skipped). A fresh
        // UserEngine has an empty table, so LoopbackDrop has to be registered or
        // the MonItems row below is legitimately dropped.
        M2Share.UserEngine.StdItemList.Add(new GoodItem { Name = "LoopbackDrop" });

        Assert(M2Share.UserEngine.TryPublishNativeMonsterDefinitions(
                catalog, out var error),
            "UserEngine native monster publication: " + error);
        Assert(M2Share.UserEngine.NativeMonsterDefinitionsPublished
            && M2Share.UserEngine.MonsterList.Count == 1,
            "UserEngine native monster publication state");
        var published = M2Share.UserEngine.MonsterList[0];
        Assert(published.sName == "LoopbackMonster"
            && published.wHP == 0x12345678
            && published.wMP == 0x23456789
            && published.wWalkSpeed == 99
            && published.wAttackSpeed == 77,
            "UserEngine native monster exact fields");
        Assert(published.ItemList?.Count == 1
            && published.ItemList[0].ItemName == "LoopbackDrop",
            "UserEngine native monster MonItems attachment");
        Assert(!M2Share.UserEngine.TryPublishNativeMonsterDefinitions(
                catalog, out _),
            "UserEngine accepted a second native monster publication");

        var initialize = typeof(UserEngine).GetMethod("MonInitialize",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert(initialize != null, "missing UserEngine.MonInitialize");
        var actor = new Monster();
        initialize.Invoke(M2Share.UserEngine,
            new object[] { actor, "LoopbackMonster" });
        Assert(actor.m_btRaceServer == 86
            && actor.m_Abil.HP == 0x12345678
            && actor.m_Abil.MaxHP == 0x12345678
            && actor.m_Abil.MP == 0x23456789
            && actor.m_Abil.MaxMP == 0x23456789
            && actor.m_btMonsterWeapon == 0,
            "native monster actor HP/MP initialization");

        var caseVariant = new Monster();
        var before = caseVariant.m_Abil.HP;
        initialize.Invoke(M2Share.UserEngine,
            new object[] { caseVariant, "loopbackmonster" });
        Assert(caseVariant.m_Abil.HP == before,
            "native monster lookup was not exact-case");
    }
    finally
    {
        M2Share.sConfigPath = previousConfigPath;
        M2Share.g_Config.sEnvirDir = previousEnvirDirectory;
        M2Share.LocalDB = previousLocalDb;
        M2Share.UserEngine = previousUserEngine;
        M2Share.ObjectManager = previousObjectManager;
        if (Directory.Exists(temporaryRoot))
            Directory.Delete(temporaryRoot, recursive: true);
    }
}

static byte[] CreateType2ControlPayload(ushort command, int param1, int param2)
{
    var payload = new byte[NativeType2SecondaryRankingState.HeaderSize];
    BinaryPrimitives.WriteUInt16LittleEndian(payload, command);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), param1);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), param2);
    return payload;
}

static byte[] CreateSecondaryRankingPayload(int category, int param2, byte seed)
{
    Assert(category is >= 0 and < NativeType2SecondaryRankingState.BucketCount,
        "loopback secondary ranking category");
    var packetSize = category is >= 4 and <= 7 ? 0x124 : 0xB4;
    var payload = new byte[packetSize];
    BinaryPrimitives.WriteUInt16LittleEndian(payload,
        NativeType2SecondaryRankingState.RecordCommand);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), category);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), param2);
    for (var index = NativeType2SecondaryRankingState.HeaderSize;
         index < payload.Length; index++)
        payload[index] = unchecked((byte)(seed + index
            - NativeType2SecondaryRankingState.HeaderSize));
    return payload;
}

static byte[] CreateMagicPush(ushort command, ushort magicId, bool completed)
{
    var payload = new byte[NativeType2MagicSnapshotState.PacketSize];
    BinaryPrimitives.WriteUInt16LittleEndian(payload, command);
    if (completed)
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(
        NativeType2MagicSnapshotState.HeaderSize + 0x10, 2), magicId);
    payload[NativeType2MagicSnapshotState.HeaderSize + 0x1A] = 17;
    return Encode(new LegacyDbServerFrame(2, 0, payload));
}

static byte[] CreateFieldHeroPush()
{
    var payload = new byte[NativeType2FieldHeroSnapshotState.PacketSize];
    BinaryPrimitives.WriteUInt16LittleEndian(payload,
        NativeType2FieldHeroSnapshotState.Command);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), 1);
    var body = payload.AsSpan(NativeType2FieldHeroSnapshotState.HeaderSize);
    var name = Encoding.ASCII.GetBytes("LoopFieldHero");
    body[0] = (byte)name.Length;
    name.CopyTo(body.Slice(1));
    body[0x138] = 0x5A;
    return Encode(new LegacyDbServerFrame(2, 0, payload));
}

static byte[] CreateEndpointSlotsPayload(int count, int param1, byte seed)
{
    Assert(count is >= 1 and <= NativeType2EndpointSlotState.SlotCount,
        "loopback endpoint slot count");
    var payload = new byte[NativeType2EndpointSlotState.HeaderSize
                           + count * NativeType2EndpointSlotState.SlotSize];
    BinaryPrimitives.WriteUInt16LittleEndian(payload,
        NativeType2EndpointSlotState.Command);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), param1);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), count);
    for (var index = NativeType2EndpointSlotState.HeaderSize;
         index < payload.Length; index++)
        payload[index] = unchecked((byte)(seed + index -
            NativeType2EndpointSlotState.HeaderSize));
    return payload;
}

static byte[] CopyEndpointRecord(byte[] payload, int slot)
{
    return payload.AsSpan(NativeType2EndpointSlotState.HeaderSize
        + (slot - 1) * NativeType2EndpointSlotState.SlotSize,
        NativeType2EndpointSlotState.SlotSize).ToArray();
}

static byte[] CreateStdItemPayload(ushort wireIndex, string name,
    string itemExtAbil, bool completed)
{
    var payload = new byte[NativeType2StdItemSnapshotState.PacketSize];
    BinaryPrimitives.WriteUInt16LittleEndian(payload,
        NativeType2StdItemSnapshotState.Command);
    if (completed)
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), 1);

    var body = payload.AsSpan(NativeType2StdItemSnapshotState.HeaderSize);
    BinaryPrimitives.WriteUInt16LittleEndian(body, wireIndex);
    WriteShortString(payload, NativeType2StdItemSnapshotState.HeaderSize + 0x04,
        15, name);
    WriteShortString(payload, NativeType2StdItemSnapshotState.HeaderSize + 0x5C,
        200, itemExtAbil);
    return payload;
}

static byte[] Encode(LegacyDbServerFrame frame)
{
    Assert(LegacyDbServerFrameCodec.TryEncode(frame, out var wire, out var error),
        "frame encoding: " + error);
    return wire;
}

static async Task<TcpClient> AcceptAsync(TcpListener listener)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    return await listener.AcceptTcpClientAsync(timeout.Token);
}

static async Task<LegacyDbServerFrame> ReadFrameAsync(NetworkStream stream)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var header = new byte[LegacyDbServerFrameCodec.HeaderSize];
    await ReadExactlyAsync(stream, header, timeout.Token);
    var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8, 4));
    Assert(payloadLength >= 0 && payloadLength <= 0x1FFFF - header.Length,
        "received invalid native payload length");
    var wire = new byte[header.Length + payloadLength];
    header.CopyTo(wire, 0);
    if (payloadLength > 0)
        await ReadExactlyAsync(stream, wire.AsMemory(header.Length, payloadLength),
            timeout.Token);
    Assert(LegacyDbServerFrameCodec.TryDecode(wire, out var frame, out var error),
        "received native frame: " + error);
    return frame;
}

static async Task ReadExactlyAsync(Stream stream, Memory<byte> target,
    CancellationToken cancellationToken)
{
    var offset = 0;
    while (offset < target.Length)
    {
        var read = await stream.ReadAsync(target[offset..], cancellationToken);
        if (read == 0) throw new EndOfStreamException("native stream closed");
        offset += read;
    }
}

static async Task WaitUntilAsync(Func<bool> condition, string message)
{
    var deadline = Environment.TickCount64 + 5_000;
    while (!condition())
    {
        if (Environment.TickCount64 >= deadline)
            throw new TimeoutException(message);
        await Task.Delay(10);
    }
}

static void SetPrivateLong(DBService service, string fieldName, long value)
{
    var field = typeof(DBService).GetField(fieldName,
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(field != null, "missing DBService field " + fieldName);
    field.SetValue(service, value);
}

static int GetPrivateInt(DBService service, string fieldName)
{
    var field = typeof(DBService).GetField(fieldName,
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(field != null, "missing DBService field " + fieldName);
    return (int)field.GetValue(service);
}

static void EnqueueReceivedFrame(DBService service, LegacyDbServerFrame frame)
{
    var queueField = typeof(DBService).GetField("_pendingReceivedFrames",
        BindingFlags.Instance | BindingFlags.NonPublic);
    var frameType = typeof(DBService).GetNestedType("ReceivedNativeFrame",
        BindingFlags.NonPublic);
    Assert(queueField != null && frameType != null,
        "missing DBService native receive queue internals");
    var received = Activator.CreateInstance(frameType,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        binder: null, args: new object[] { frame }, culture: null);
    Assert(received != null, "unable to create DBService received frame");
    var queue = queueField.GetValue(service);
    var enqueue = queue.GetType().GetMethod("Enqueue");
    Assert(enqueue != null, "missing DBService receive queue Enqueue");
    enqueue.Invoke(queue, new[] { received });
}

static int GetReceivedFrameCount(DBService service)
{
    return GetQueueCount(service, "_pendingReceivedFrames")
           + GetQueueCount(service, "_workingReceivedFrames");
}

static int GetQueueCount(DBService service, string fieldName)
{
    var queueField = typeof(DBService).GetField(fieldName,
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(queueField != null,
        "missing DBService native receive queue " + fieldName);
    var queue = queueField.GetValue(service);
    var count = queue.GetType().GetProperty("Count");
    Assert(count != null, "missing DBService receive queue Count");
    return (int)count.GetValue(queue);
}

static int GetParserBufferedLength(DBService service)
{
    var parserField = typeof(DBService).GetField("_frameParser",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(parserField != null, "missing DBService native frame parser");
    var parser = parserField.GetValue(service) as LegacyDbServerStreamParser;
    Assert(parser != null, "invalid DBService native frame parser");
    return parser.BufferedLength;
}

static NativeType2MonsterSnapshotState GetMonsterSnapshot(DBService service)
{
    var field = typeof(DBService).GetField("_monsterSnapshot",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(field != null, "missing DBService native monster snapshot");
    var snapshot = field.GetValue(service) as NativeType2MonsterSnapshotState;
    Assert(snapshot != null, "invalid DBService native monster snapshot");
    return snapshot;
}

static NativeType2MagicSnapshotState GetMagicSnapshot(DBService service)
{
    var field = typeof(DBService).GetField("_magicSnapshot",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(field != null, "missing DBService native magic snapshot");
    var snapshot = field.GetValue(service) as NativeType2MagicSnapshotState;
    Assert(snapshot != null, "invalid DBService native magic snapshot");
    return snapshot;
}

static NativeType2EndpointSlotState GetEndpointSlots(DBService service)
{
    var field = typeof(DBService).GetField("_endpointSlots",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(field != null, "missing DBService native endpoint slots");
    var slots = field.GetValue(service) as NativeType2EndpointSlotState;
    Assert(slots != null, "invalid DBService native endpoint slots");
    return slots;
}

static NativeType2FieldHeroSnapshotState GetFieldHeroSnapshot(DBService service)
{
    var field = typeof(DBService).GetField("_fieldHeroSnapshot",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(field != null, "missing DBService native field hero snapshot");
    var snapshot = field.GetValue(service) as NativeType2FieldHeroSnapshotState;
    Assert(snapshot != null, "invalid DBService native field hero snapshot");
    return snapshot;
}

static NativeType2StdItemSnapshotState GetStdItemSnapshot(DBService service)
{
    var field = typeof(DBService).GetField("_stdItemSnapshot",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(field != null, "missing DBService native standard-item snapshot");
    var snapshot = field.GetValue(service) as NativeType2StdItemSnapshotState;
    Assert(snapshot != null, "invalid DBService native standard-item snapshot");
    return snapshot;
}

static NativeType2SecondaryRankingState GetSecondaryRankings(DBService service)
{
    var field = typeof(DBService).GetField("_secondaryRankings",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(field != null, "missing DBService native secondary rankings");
    var state = field.GetValue(service) as NativeType2SecondaryRankingState;
    Assert(state != null, "invalid DBService native secondary rankings");
    return state;
}

static void VerifyStdItemNumericExceptionEscapes(DBService service)
{
    var method = typeof(DBService).GetMethod("ConsumeStaticInitializationFrame",
        BindingFlags.Instance | BindingFlags.NonPublic);
    Assert(method != null, "missing native static Type2 admission method");
    try
    {
        method.Invoke(service, new object[]
        {
            new LegacyDbServerFrame(2, 0,
                CreateStdItemPayload(1, "BadStdItem", "攻击下限:",
                    completed: false))
        });
        throw new InvalidOperationException(
            "native Type2 0x0068 numeric conversion exception was swallowed");
    }
    catch (TargetInvocationException exception)
        when (exception.InnerException is NativeType2StdItemNumericException)
    {
    }

    var snapshot = GetStdItemSnapshot(service);
    Assert(!snapshot.Completed && snapshot.Records.Count == 0
        && snapshot.ExpectedWireIndex == 1,
        "native Type2 0x0068 numeric failure changed snapshot state");
}

static ushort ReadCommand(LegacyDbServerFrame frame)
{
    Assert(frame.Payload.Length >= 2, "native command payload is truncated");
    return BinaryPrimitives.ReadUInt16LittleEndian(frame.Payload);
}

static void AssertControl(LegacyDbServerFrame frame, ushort command,
    int param1, int param2, string description)
{
    Assert(frame.Type == 2 && frame.Payload.Length == 12,
        description + " type/length");
    Assert(ReadCommand(frame) == command, description + " command");
    Assert(BinaryPrimitives.ReadInt32LittleEndian(frame.Payload.AsSpan(4, 4))
        == param1, description + " Param1");
    Assert(BinaryPrimitives.ReadInt32LittleEndian(frame.Payload.AsSpan(8, 4))
        == param2, description + " Param2");
}

static void WriteShortString(byte[] destination, int offset, int capacity,
    string value)
{
    var bytes = Encoding.GetEncoding(936).GetBytes(value);
    Assert(bytes.Length <= capacity, "loopback fixture string capacity");
    destination[offset] = (byte)bytes.Length;
    bytes.CopyTo(destination, offset + 1);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
