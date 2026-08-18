using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Sockets;
using DBSvr.Core;
using GameSvr.Services;
using SystemModule;
using SystemModule.Packet;
using SystemModule.Sockets;

namespace GameSvr
{
    public class DBService : IDisposable
    {
        private const int ReconnectIntervalMilliseconds = 10_000;
        private const int HeartbeatIntervalMilliseconds = 10_000;
        private const int MaximumType1PerPulse = 5;

        private readonly IClientScoket _clientScoket;
        private readonly LegacyDbServerStreamParser _frameParser = new();
        private readonly object _connectionLock = new();
        private readonly object _parserLock = new();
        private readonly object _receiveLock = new();
        private Queue<ReceivedNativeFrame> _pendingReceivedFrames = new();
        private Queue<ReceivedNativeFrame> _workingReceivedFrames = new();
        private readonly ConcurrentQueue<byte[]> _pendingSends = new();
        private readonly object _sendLock = new();
        private readonly object _type2Lock = new();
        private readonly NativeType2MagicSnapshotState _magicSnapshot = new();
        private readonly NativeType2MagicRuntimeCatalog _magicRuntimeCatalog =
            new();
        private readonly ManualResetEventSlim _magicDefinitionsPublished =
            new(false);
        private readonly ManualResetEventSlim _staticInitializationCompleted =
            new(false);
        private readonly NativeType2MonsterSnapshotState _monsterSnapshot = new();
        private readonly NativeType2MonsterRuntimeCatalog _monsterRuntimeCatalog =
            new();
        private readonly ManualResetEventSlim _monsterDefinitionsPublished =
            new(false);
        private readonly NativeType2StdItemSnapshotState _stdItemSnapshot =
            NativeType2StdItemSnapshotState.CreateForVerifiedOriginalStartup();
        private readonly NativeType2StdItemStaticCatalog _stdItemRuntimeCatalog =
            new();
        private readonly ManualResetEventSlim _stdItemDefinitionsPublished =
            new(false);
        private readonly NativeType2FieldHeroSnapshotState _fieldHeroSnapshot = new();
        private readonly NativeType2FieldHeroStaticCatalog
            _fieldHeroRuntimeCatalog = new();
        private readonly NativeType2FieldHeroRuntimeCatalogAdapter
            _fieldHeroSpawnRuntimeCatalog = new();
        private INativeFieldHeroMonItemsSource _fieldHeroMonItemsSource =
            NativeFieldHeroEmptyMonItemsSource.Instance;
        private readonly NativeType2EndpointSlotState _endpointSlots = new();
        private readonly NativeType2SecondaryRankingState _secondaryRankings =
            new();

        private long _nextReconnectAt;
        private long _nextHeartbeatAt;
        private int _connectionGeneration;
        private int _started;
        private int _magicPublicationCommitted;
        private int _monsterPublicationCommitted;
        private int _stdItemPublicationCommitted;
        private int _fieldHeroPublicationCommitted;
        private int _stopping;
        private int _disposed;
        private Socket _activeConnectionSocket;
        private Socket _readyConnectionSocket;
        private string _magicPublicationFailure;
        private string _monsterPublicationFailure;
        private string _stdItemPublicationFailure;
        private string _fieldHeroPublicationFailure;

        public DBService()
        {
            _clientScoket = new IClientScoket();
            _clientScoket.OnConnected += DbScoketConnected;
            _clientScoket.OnDisconnected += DbScoketDisconnected;
            _clientScoket.ReceivedDatagram += DBSocketRead;
            _clientScoket.OnError += DBSocketError;
            _fieldHeroSnapshot.SetCompletionCallback(
                PublishFieldHeroDefinitionsWhenCompleted);
        }

        public bool Connected => TryGetReadyConnection(out _);

        public int PendingNativeSendCount => _pendingSends.Count;

        public bool TryGetSecondaryRankingPage(int category, int requestedPage,
            out int correctedPage, out int bodyLength, out byte[] body)
        {
            correctedPage = requestedPage;
            lock (_type2Lock)
            {
                return _secondaryRankings.TryCopyPage(category,
                    ref correctedPage, out bodyLength, out body);
            }
        }

        public NativeType2MagicRuntimeCatalog MagicRuntimeCatalog =>
            _magicRuntimeCatalog;

        public bool NativeMagicDefinitionsPublished =>
            Volatile.Read(ref _magicPublicationCommitted) != 0;

        public NativeType2MonsterRuntimeCatalog MonsterRuntimeCatalog =>
            _monsterRuntimeCatalog;

        public bool NativeMonsterDefinitionsPublished =>
            Volatile.Read(ref _monsterPublicationCommitted) != 0;

        public NativeType2StdItemStaticCatalog StdItemRuntimeCatalog =>
            _stdItemRuntimeCatalog;

        public bool NativeStdItemDefinitionsPublished =>
            Volatile.Read(ref _stdItemPublicationCommitted) != 0;

        public NativeType2FieldHeroStaticCatalog FieldHeroRuntimeCatalog =>
            _fieldHeroRuntimeCatalog;

        public NativeType2FieldHeroRuntimeCatalogAdapter
            FieldHeroSpawnRuntimeCatalog => _fieldHeroSpawnRuntimeCatalog;

        public bool NativeFieldHeroDefinitionsPublished =>
            Volatile.Read(ref _fieldHeroPublicationCommitted) != 0;

        internal void ConfigureFieldHeroMonItemsSource(
            INativeFieldHeroMonItemsSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            lock (_type2Lock)
            {
                if (_fieldHeroSnapshot.Completed
                    || Volatile.Read(ref _fieldHeroPublicationCommitted) != 0)
                {
                    throw new InvalidOperationException(
                        "FieldHero MonItems source must be configured before " +
                        "the Type2 snapshot completes.");
                }
                _fieldHeroMonItemsSource = source;
            }
        }

        public bool StaticInitializationCompleted =>
            _staticInitializationCompleted.IsSet;

        public void Start()
        {
            lock (_connectionLock)
            {
                if (Volatile.Read(ref _disposed) != 0) return;
                if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
                    return;
                try
                {
                    Volatile.Write(ref _stopping, 0);
                    _nextReconnectAt = 0;
                    ConnectIfDue(Environment.TickCount64);
                }
                catch
                {
                    Volatile.Write(ref _stopping, 1);
                    Interlocked.Exchange(ref _started, 0);
                    throw;
                }
            }
        }

        public void Stop()
        {
            lock (_connectionLock)
            {
                Volatile.Write(ref _stopping, 1);
                if (Interlocked.Exchange(ref _started, 0) == 0)
                    return;
                _clientScoket.Disconnect();
            }
        }

        public bool TryWaitForNativeDefinitionInitialization(
            int timeoutMilliseconds, out string error)
        {
            error = string.Empty;
            var deadline = Environment.TickCount64
                           + Math.Max(0, timeoutMilliseconds);
            if (!WaitUntil(_magicDefinitionsPublished, deadline))
            {
                error = "等待原生人物/英雄技能定义超时";
                return false;
            }

            var publicationFailure = Volatile.Read(
                ref _magicPublicationFailure);
            if (!string.IsNullOrEmpty(publicationFailure))
            {
                error = publicationFailure;
                return false;
            }
            if (!_magicRuntimeCatalog.Ready
                || _magicRuntimeCatalog.HumanDefinitions.Count == 0
                || _magicRuntimeCatalog.HeroDefinitions.Count == 0)
            {
                error = "原生人物/英雄技能定义未形成有效双表快照";
                return false;
            }
            if (!WaitUntil(_monsterDefinitionsPublished, deadline))
            {
                error = "等待原生怪物定义超时";
                return false;
            }

            var monsterPublicationFailure = Volatile.Read(
                ref _monsterPublicationFailure);
            if (!string.IsNullOrEmpty(monsterPublicationFailure))
            {
                error = monsterPublicationFailure;
                return false;
            }
            if (!_monsterRuntimeCatalog.Ready
                || _monsterRuntimeCatalog.Definitions.Count == 0)
            {
                error = "原生怪物定义未形成有效终态快照";
                return false;
            }
            if (!WaitUntil(_stdItemDefinitionsPublished, deadline))
            {
                error = "等待原生标准物品定义超时";
                return false;
            }

            var stdItemPublicationFailure = Volatile.Read(
                ref _stdItemPublicationFailure);
            if (!string.IsNullOrEmpty(stdItemPublicationFailure))
            {
                error = stdItemPublicationFailure;
                return false;
            }
            if (!_stdItemRuntimeCatalog.Ready
                || _stdItemRuntimeCatalog.Count == 0)
            {
                error = "原生标准物品定义未形成有效终态快照";
                return false;
            }
            if (!WaitUntil(_staticInitializationCompleted, deadline))
            {
                error = "等待原生 Type2 静态初始化切换超时";
                return false;
            }
            var fieldHeroPublicationFailure = Volatile.Read(
                ref _fieldHeroPublicationFailure);
            if (!string.IsNullOrEmpty(fieldHeroPublicationFailure))
            {
                error = fieldHeroPublicationFailure;
                return false;
            }
            if (!_fieldHeroRuntimeCatalog.Ready
                || !_fieldHeroSpawnRuntimeCatalog.Ready
                || Volatile.Read(ref _fieldHeroPublicationCommitted) == 0)
            {
                error = "原生 FieldHero 定义未形成有效终态快照";
                return false;
            }
            if (!_fieldHeroSnapshot.Completed)
            {
                error = "原生 Type2 静态初始化未完成 one-shot 切换";
                return false;
            }
            return true;
        }

        public void Pulse()
        {
            ProcessReceivedFrames();

            var now = Environment.TickCount64;
            if (TryGetReadyConnection(out var activeSocket))
            {
                if (now >= Volatile.Read(ref _nextHeartbeatAt))
                {
                    SendHeartbeat(activeSocket);
                    Volatile.Write(ref _nextHeartbeatAt,
                        now + HeartbeatIntervalMilliseconds);
                }
                FlushPendingSends(activeSocket);
                return;
            }

            ConnectIfDue(now);
        }

        public void CheckConnected()
        {
            ConnectIfDue(Environment.TickCount64);
        }

        public bool FlushPendingSendsAndWait(int timeoutMilliseconds)
        {
            var deadline = Environment.TickCount64 + Math.Max(0, timeoutMilliseconds);
            do
            {
                Pulse();
                if (_pendingSends.IsEmpty) return true;
                Thread.Sleep(10);
            } while (Environment.TickCount64 < deadline);

            return _pendingSends.IsEmpty;
        }

        public bool SendNativeFrame(byte[] nativeFrame)
        {
            if (nativeFrame == null)
            {
                M2Share.ErrorMessage("[RunDB] 原生发送帧拒绝: frame is null");
                return false;
            }
            if (!LegacyDbServerFrameCodec.TryDecode(nativeFrame,
                    out _, out var error))
            {
                M2Share.ErrorMessage("[RunDB] 原生发送帧拒绝: " + error);
                return false;
            }

            _pendingSends.Enqueue((byte[])nativeFrame.Clone());
            FlushPendingSends();
            return true;
        }

        public bool SendRawRequest(int queryId, ServerMessagePacket message,
            byte[] nativeFrame) => SendNativeFrame(nativeFrame);

        [Obsolete("The original DBServer wire does not use RequestServerPacket requests.")]
        public bool SendRequest<T>(int queryId, ServerMessagePacket packet,
            T request) where T : CmdPacket
        {
            M2Share.ErrorMessage(
                $"[RunDB] 已拒绝私有#协议请求 ident={packet?.Ident ?? 0} qid={queryId}");
            return false;
        }

        public int NextQueryId()
        {
            while (true)
            {
                var current = Volatile.Read(ref M2Share.g_Config.nDBQueryID);
                var next = current <= 0 || current >= int.MaxValue - 1
                    ? 1 : current + 1;
                if (Interlocked.CompareExchange(
                        ref M2Share.g_Config.nDBQueryID, next, current) == current)
                    return next;
            }
        }

        private void ConnectIfDue(long now)
        {
            lock (_connectionLock)
            {
                if (Volatile.Read(ref _disposed) != 0
                    || Volatile.Read(ref _started) == 0
                    || Volatile.Read(ref _stopping) != 0
                    || _clientScoket.IsConnected || _clientScoket.IsBusy
                    || now < Volatile.Read(ref _nextReconnectAt))
                    return;

                Volatile.Write(ref _nextReconnectAt,
                    now + ReconnectIntervalMilliseconds);
                _clientScoket.Connect(M2Share.g_Config.sDBAddr,
                    M2Share.g_Config.nDBPort);
            }
        }

        private void DbScoketDisconnected(object sender,
            DSCClientConnectedEventArgs e)
        {
            lock (_connectionLock)
            {
                if (e?.socket == null
                    || !ReferenceEquals(_activeConnectionSocket, e.socket))
                    return;
                _activeConnectionSocket = null;
                _readyConnectionSocket = null;
                AdvanceConnectionGenerationAndResetInboundState();
                if (Volatile.Read(ref _disposed) == 0
                    && Volatile.Read(ref _started) != 0
                    && Volatile.Read(ref _stopping) == 0)
                {
                    Volatile.Write(ref _nextReconnectAt,
                        Environment.TickCount64 + ReconnectIntervalMilliseconds);
                }
            }
            HumDataService.NotifyDisconnected();
            HeroDataService.NotifyDisconnected();
            M2Share.ErrorMessage("数据库服务器[" + e.RemoteAddress + ':'
                                 + e.RemotePort + "]断开连接...");
        }

        private void DbScoketConnected(object sender,
            DSCClientConnectedEventArgs e)
        {
            var socket = e?.socket;
            lock (_connectionLock)
            {
                if (Volatile.Read(ref _disposed) != 0
                    || Volatile.Read(ref _started) == 0
                    || Volatile.Read(ref _stopping) != 0
                    || socket == null
                    || !_clientScoket.IsCurrentConnection(socket))
                {
                    _clientScoket.Disconnect(socket);
                    return;
                }
                _activeConnectionSocket = socket;
                _readyConnectionSocket = null;
                AdvanceConnectionGenerationAndResetInboundState();
                Volatile.Write(ref _nextHeartbeatAt,
                    Environment.TickCount64 + HeartbeatIntervalMilliseconds);
            }

            // Dynamic observation of the original GS1 proves that every TCP
            // connection starts with 0x003D and Param2 = ServerIndex + 1.
            if (!SendRegistration(socket))
            {
                _clientScoket.Disconnect(socket);
                return;
            }
            if (!TryMarkConnectionReady(socket))
            {
                _clientScoket.Disconnect(socket);
                return;
            }

            FlushPendingSends(socket);
            if (!IsActiveConnection(socket)) return;
            M2Share.MainOutMessage("数据库服务器[" + e.RemoteAddress + ':'
                                   + e.RemotePort + "]连接成功...",
                messageColor: ConsoleColor.Green);
        }

        private void DBSocketError(object sender, DSCClientErrorEventArgs e)
        {
            lock (_connectionLock)
            {
                if (Volatile.Read(ref _disposed) != 0
                    || Volatile.Read(ref _started) == 0
                    || Volatile.Read(ref _stopping) != 0
                    || e?.socket == null
                    || !_clientScoket.IsCurrentSocket(e.socket))
                    return;
                Volatile.Write(ref _nextReconnectAt,
                    Environment.TickCount64 + ReconnectIntervalMilliseconds);
            }
            switch (e.ErrorCode)
            {
                case System.Net.Sockets.SocketError.ConnectionRefused:
                    M2Share.ErrorMessage("数据库服务器[" + M2Share.g_Config.sDBAddr
                        + ":" + M2Share.g_Config.nDBPort + "]拒绝链接...");
                    break;
                case System.Net.Sockets.SocketError.ConnectionReset:
                    M2Share.ErrorMessage("数据库服务器[" + M2Share.g_Config.sDBAddr
                        + ":" + M2Share.g_Config.nDBPort + "]关闭连接...");
                    break;
                case System.Net.Sockets.SocketError.TimedOut:
                    M2Share.ErrorMessage("数据库服务器[" + M2Share.g_Config.sDBAddr
                        + ":" + M2Share.g_Config.nDBPort + "]链接超时...");
                    break;
            }
        }

        private void DBSocketRead(object sender, DSCClientDataInEventArgs e)
        {
            if (e.Buff == null || e.BuffLen <= 0 || e.Socket == null) return;
            try
            {
                lock (_connectionLock)
                {
                    if (!IsActiveConnectionNoLock(e.Socket)) return;
                    lock (_parserLock)
                    {
                        if (!IsActiveConnectionNoLock(e.Socket)) return;
                        _frameParser.Append(e.Buff.AsSpan(0, e.BuffLen), frame =>
                        {
                            if (ConsumeStaticInitializationFrame(frame))
                                return;
                            lock (_receiveLock)
                            {
                                _pendingReceivedFrames.Enqueue(
                                    new ReceivedNativeFrame(frame));
                            }
                        });
                    }
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException
                                       || ex is ArgumentException)
            {
                M2Share.ErrorMessage(
                    "数据库服务器返回非法原生数据流: " + ex.Message);
                _clientScoket.Disconnect(e.Socket);
            }
        }

        private bool ConsumeStaticInitializationFrame(LegacyDbServerFrame frame)
        {
            lock (_type2Lock)
            {
                if (_fieldHeroSnapshot.Completed) return false;

                // While the native one-shot callback at receiver+0x58 exists,
                // outer Type1/3 frames are discarded and only Type2 reaches
                // sub_7138D4 directly from the socket callback.
                if (frame.Type != 2 || frame.Payload == null
                    || frame.Payload.Length < 0x0C)
                    return true;

                _magicSnapshot.Consume(frame.Payload);
                PublishMagicDefinitionsOnceWhenReady();
                _monsterSnapshot.Consume(frame.Payload);
                PublishMonsterDefinitionsOnceWhenReady();
                _stdItemSnapshot.Consume(frame.Payload);
                PublishStdItemDefinitionsOnceWhenReady();
                _fieldHeroSnapshot.Consume(frame.Payload);
                _endpointSlots.Consume(frame.Payload);
                return true;
            }
        }

        private void PublishMagicDefinitionsOnceWhenReady()
        {
            if (!_magicSnapshot.HumanCompleted
                || !_magicSnapshot.HeroCompleted
                || Volatile.Read(ref _magicPublicationCommitted) != 0
                || _magicDefinitionsPublished.IsSet)
                return;

            try
            {
                _magicRuntimeCatalog.Publish(_magicSnapshot);
                Volatile.Write(ref _magicPublicationCommitted, 1);
            }
            catch (Exception ex) when (ex is InvalidDataException
                                       || ex is ArgumentException)
            {
                Volatile.Write(ref _magicPublicationFailure,
                    "原生人物/英雄技能定义校验失败: " + ex.Message);
            }
            finally
            {
                _magicDefinitionsPublished.Set();
            }
        }

        private void PublishMonsterDefinitionsOnceWhenReady()
        {
            if (!_monsterSnapshot.Completed
                || Volatile.Read(ref _monsterPublicationCommitted) != 0
                || _monsterDefinitionsPublished.IsSet)
                return;

            try
            {
                _monsterRuntimeCatalog.Publish(_monsterSnapshot);
                Volatile.Write(ref _monsterPublicationCommitted, 1);
            }
            catch (Exception ex) when (ex is InvalidDataException
                                       || ex is ArgumentException)
            {
                Volatile.Write(ref _monsterPublicationFailure,
                    "原生怪物定义校验失败: " + ex.Message);
            }
            finally
            {
                _monsterDefinitionsPublished.Set();
            }
        }

        private void PublishStdItemDefinitionsOnceWhenReady()
        {
            if (!_stdItemSnapshot.Completed
                || Volatile.Read(ref _stdItemPublicationCommitted) != 0
                || _stdItemDefinitionsPublished.IsSet)
                return;

            try
            {
                if (M2Share.PasEngine == null)
                    throw new InvalidOperationException(
                        "Pascal脚本引擎尚未初始化");
                _stdItemRuntimeCatalog.Publish(_stdItemSnapshot,
                    new NativeType2StdItemProductionNeedIdentifyResolver(),
                    new NativeType2StdItemProductionScriptBinder(
                        M2Share.PasEngine));
                foreach (var log in _stdItemRuntimeCatalog.Logs)
                    M2Share.MainOutMessage(log);
                Volatile.Write(ref _stdItemPublicationCommitted, 1);
            }
            catch (Exception ex) when (ex is InvalidDataException
                                       || ex is ArgumentException
                                       || ex is InvalidOperationException)
            {
                Volatile.Write(ref _stdItemPublicationFailure,
                    "原生标准物品定义校验失败: " + ex.Message);
            }
            finally
            {
                _stdItemDefinitionsPublished.Set();
            }
        }

        private void PublishFieldHeroDefinitionsWhenCompleted(
            NativeType2FieldHeroSnapshotState snapshot)
        {
            try
            {
                _fieldHeroRuntimeCatalog.Publish(snapshot,
                    _stdItemRuntimeCatalog);
                _fieldHeroSpawnRuntimeCatalog.Publish(
                    _fieldHeroRuntimeCatalog, _stdItemRuntimeCatalog,
                    _fieldHeroMonItemsSource);
                Volatile.Write(ref _fieldHeroPublicationCommitted, 1);
            }
            catch (Exception ex) when (ex is InvalidDataException
                                       || ex is ArgumentException
                                       || ex is InvalidOperationException)
            {
                Volatile.Write(ref _fieldHeroPublicationFailure,
                    "原生 FieldHero 定义校验失败: " + ex.Message);
            }
            finally
            {
                _staticInitializationCompleted.Set();
            }
        }

        private bool WaitUntil(ManualResetEventSlim signal,
            long deadline)
        {
            while (!signal.IsSet)
            {
                var remaining = deadline - Environment.TickCount64;
                if (remaining <= 0) return false;
                CheckConnected();
                signal.Wait((int)Math.Min(50, remaining));
            }
            return true;
        }

        private void ProcessReceivedFrames()
        {
            lock (_receiveLock)
            {
                if (_workingReceivedFrames.Count == 0)
                {
                    if (_pendingReceivedFrames.Count == 0) return;
                    (_workingReceivedFrames, _pendingReceivedFrames) =
                        (_pendingReceivedFrames, _workingReceivedFrames);
                }
            }

            var type1Processed = 0;
            while (true)
            {
                ReceivedNativeFrame received;
                if (type1Processed >= MaximumType1PerPulse) return;
                lock (_receiveLock)
                {
                    if (_workingReceivedFrames.Count == 0) return;
                    received = _workingReceivedFrames.Dequeue();
                }

                var validType1 = received.Frame.Type == 1
                    && received.Frame.Payload?.Length >= 0x48;
                ProcessNativeFrame(received.Frame);
                if (validType1) type1Processed++;
            }
        }

        private void ProcessNativeFrame(LegacyDbServerFrame frame)
        {
            var minimumPayload = frame.Type switch
            {
                1 => 0x48,
                2 => 0x0C,
                3 => 0x40,
                _ => int.MaxValue
            };
            if (frame.Payload == null || frame.Payload.Length < minimumPayload)
                return;

            switch (frame.Type)
            {
                case 1:
                    ProcessNativeType1(frame);
                    break;
                case 2:
                    lock (_type2Lock)
                    {
                        if (!NativeType2StdItemRuntimeProduction.TryConsume(
                                frame.Payload))
                            _secondaryRankings.Consume(frame.Payload);
                    }
                    break;
                case 3:
                    // Original sub_713A98 only reads the leading word and
                    // releases the frame; it has no Type3 command dispatch.
                    break;
            }
        }

        private static void ProcessNativeType1(LegacyDbServerFrame frame)
        {
            var command = BinaryPrimitives.ReadUInt16LittleEndian(frame.Payload);
            if (command == NativeForceDisconnectClient.ResponseCommand)
            {
                NativeForceDisconnectClient.ProcessResponse(frame);
                return;
            }

            if (command == NativeWhitelistReloadClient.ResponseCommand)
            {
                NativeWhitelistReloadClient.ProcessResponse(frame);
                return;
            }

            if (command is NativeType1YbTransactionAck
                    .BagInjectionResponseCommand
                or NativeType1YbTransactionAck.AwardPlayerResponseCommand)
            {
                NativeType1YbTransactionAck.TryProcessResponse(frame);
                return;
            }

            if (command == 0x0050)
            {
                HumDataService.AddNativeLoadFrame(frame);
                return;
            }

            if (command == NativeItemExtractionProtocol.ResponseCommand)
            {
                NativeItemExtractionClient.ProcessResponse(frame);
                return;
            }

            if (command == NativeItemInjectionProtocol.MailResponseCommand)
            {
                NativeItemInjectionClient.ProcessResponse(frame);
                return;
            }

            if (command is NativeHeroDbFrameCodec.ConsignedListResponseCommand
                or NativeHeroDbFrameCodec.RestoreConsignedResponseCommand
                or NativeHeroDbFrameCodec.BuildThreeSlotResponseCommand)
            {
                NativeHeroAuxiliaryResponseClient.ProcessResponse(frame);
                return;
            }

            if (command is NativeAccountStorageClient.LoadResponseCommand
                or NativeAccountStorageClient.SaveResponseCommand)
            {
                NativeAccountStorageClient.ProcessResponse(frame);
                return;
            }

            if (command is NativeHeroDbFrameCodec.LoadResponseCommand
                or NativeHeroDbFrameCodec.CreateResponseCommand
                or NativeHeroDbFrameCodec.RenameResponseCommand)
            {
                if (!LegacyDbServerFrameCodec.TryEncode(frame,
                        out var wire, out var error))
                {
                    M2Share.ErrorMessage("[HeroDB] 原生响应重组失败: " + error);
                    return;
                }
                HeroDataService.TryAddNativeResponse(wire);
            }
        }

        private bool SendRegistration(Socket expectedSocket)
        {
            var serverType = M2Share.nServerIndex + 1;
            if (serverType is <= 0 or > byte.MaxValue)
            {
                M2Share.ErrorMessage(
                    $"[RunDB] ServerIndex {M2Share.nServerIndex} 无法编码原生登记类型");
                return false;
            }
            return SendControlFrameDirect(0x003D, 0, serverType,
                expectedSocket, requireReadyConnection: false);
        }

        private void SendHeartbeat(Socket expectedSocket)
        {
            var userCount = M2Share.UserEngine == null
                ? 0 : M2Share.UserEngine.PlayObjects.Count();
            SendControlFrameDirect(0x003C, 0, userCount, expectedSocket);
        }

        private bool SendControlFrameDirect(ushort command,
            int param1, int param2, Socket expectedSocket = null,
            bool requireReadyConnection = true)
        {
            var payload = new byte[12];
            BinaryPrimitives.WriteUInt16LittleEndian(payload, command);
            // Original bytes 2..3 are uninitialized and ignored by DBServer.
            // Keep them deterministically zero instead of leaking process memory.
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), param1);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), param2);
            if (!LegacyDbServerFrameCodec.TryEncode(
                    new LegacyDbServerFrame(2, 0, payload),
                    out var wire, out var error))
            {
                M2Share.ErrorMessage("[RunDB] Type2控制帧编码失败: " + error);
                return false;
            }

            if (expectedSocket == null
                && !TryGetReadyConnection(out expectedSocket))
                return false;
            lock (_sendLock)
            {
                if (requireReadyConnection
                        ? !IsReadyConnection(expectedSocket)
                        : !IsActiveConnection(expectedSocket))
                    return false;
                _clientScoket.Send(wire, expectedSocket);
                return requireReadyConnection
                    ? IsReadyConnection(expectedSocket)
                    : IsActiveConnection(expectedSocket);
            }
        }

        private void FlushPendingSends(Socket expectedSocket = null)
        {
            if (expectedSocket == null
                && !TryGetReadyConnection(out expectedSocket))
                return;
            if (!IsReadyConnection(expectedSocket)) return;
            lock (_sendLock)
            {
                while (IsReadyConnection(expectedSocket)
                       && _pendingSends.TryPeek(out var frame))
                {
                    _clientScoket.Send(frame, expectedSocket);
                    if (!IsReadyConnection(expectedSocket)) return;
                    _pendingSends.TryDequeue(out _);
                }
            }
        }

        private bool TryGetReadyConnection(out Socket socket)
        {
            lock (_connectionLock)
            {
                socket = _readyConnectionSocket;
                return IsReadyConnectionNoLock(socket);
            }
        }

        private bool IsActiveConnection(Socket socket)
        {
            lock (_connectionLock)
                return IsActiveConnectionNoLock(socket);
        }

        private bool IsActiveConnectionNoLock(Socket socket)
        {
            return Volatile.Read(ref _disposed) == 0
                   && Volatile.Read(ref _started) != 0
                   && Volatile.Read(ref _stopping) == 0
                   && socket != null
                   && ReferenceEquals(_activeConnectionSocket, socket)
                   && _clientScoket.IsCurrentConnection(socket);
        }

        private bool IsReadyConnection(Socket socket)
        {
            lock (_connectionLock)
                return IsReadyConnectionNoLock(socket);
        }

        private bool IsReadyConnectionNoLock(Socket socket)
        {
            return IsActiveConnectionNoLock(socket)
                   && ReferenceEquals(_readyConnectionSocket, socket);
        }

        private bool TryMarkConnectionReady(Socket socket)
        {
            lock (_connectionLock)
            {
                if (!IsActiveConnectionNoLock(socket)) return false;
                _readyConnectionSocket = socket;
                return true;
            }
        }

        private void AdvanceConnectionGenerationAndResetInboundState()
        {
            lock (_connectionLock)
            {
                lock (_parserLock)
                {
                    Interlocked.Increment(ref _connectionGeneration);
                    _frameParser.Reset();
                }
                // Parsed frames have process lifetime in the original receive
                // lists. Only the incomplete TCP tail is connection-scoped.
                // The original Type2 receiver and all of its static-definition
                // state are process-lifetime objects. A TCP reconnect
                // re-registers the same receiver; it does not recreate records
                // or clear the 101/102/103/104/108 completion bits. Type2 110
                // endpoint slots share that lifetime as well.
            }
        }

        private readonly struct ReceivedNativeFrame
        {
            public readonly LegacyDbServerFrame Frame;

            public ReceivedNativeFrame(LegacyDbServerFrame frame)
            {
                Frame = frame;
            }
        }

        public void Dispose()
        {
            lock (_connectionLock)
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
                Volatile.Write(ref _stopping, 1);
                Interlocked.Exchange(ref _started, 0);
                _activeConnectionSocket = null;
                _readyConnectionSocket = null;
                _clientScoket.OnConnected -= DbScoketConnected;
                _clientScoket.OnDisconnected -= DbScoketDisconnected;
                _clientScoket.ReceivedDatagram -= DBSocketRead;
                _clientScoket.OnError -= DBSocketError;
                _clientScoket.Disconnect();
            }
            _magicDefinitionsPublished.Dispose();
            _monsterDefinitionsPublished.Dispose();
            _stdItemDefinitionsPublished.Dispose();
            _staticInitializationCompleted.Dispose();
        }
    }
}
