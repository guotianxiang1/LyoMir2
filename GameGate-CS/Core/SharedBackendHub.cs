using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading.Channels;
using SystemModule;
using SystemModule.Packet;

namespace GameGate.Core;

internal sealed class SharedBackendRoute
{
    private int _closed;
    private int _aborted;
    private int _dbInvalidated;
    private int _gameInvalidated;
    private int _dbTerminationPending;
    private int _gateIndex = 1;
    private uint _nativeRouteContext;
    private readonly object _dbOpenStateLock = new();
    private long _dbOpenAcknowledgedGeneration = -1;
    private TaskCompletionSource<bool>? _dbOpenCompletion;
    private uint _dbRouteId;

    public required int Handle { get; init; }
    public ushort NativeSessionId { get; init; }
    public int GateIndex
    {
        get => Volatile.Read(ref _gateIndex);
        init => _gateIndex = value;
    }
    public required uint ConnId { get; init; }
    public required long SessionGeneration { get; init; }
    public required string ClientIp { get; init; }
    public required byte[] DbOpenFrame { get; init; }
    public required Action Abort { get; init; }
    public long DbConnectionGeneration;
    public long GameConnectionGeneration;
    public int NativePlayerRecog;
    public int NativeServerUserIndex;
    public int NativeDbOpenContext;
    public int NativeDbControlContext;
    public readonly SemaphoreSlim DbOpenLock = new(1, 1);
    public readonly SemaphoreSlim DbSendCloseLock = new(1, 1);
    public readonly SemaphoreSlim GameOpenLock = new(1, 1);
    public readonly Channel<byte[]> DbResponses = Channel.CreateBounded<byte[]>(
        new BoundedChannelOptions(256)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });
    public readonly Channel<InternalPacket77> GameResponses = Channel.CreateBounded<InternalPacket77>(
        new BoundedChannelOptions(1024)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });

    public bool IsClosed => Volatile.Read(ref _closed) != 0;
    public bool IsDbInvalidated => Volatile.Read(ref _dbInvalidated) != 0;
    public bool IsGameInvalidated => Volatile.Read(ref _gameInvalidated) != 0;
    public bool IsInvalidated => IsDbInvalidated || IsGameInvalidated;
    public bool IsDbTerminationPending =>
        Volatile.Read(ref _dbTerminationPending) != 0;
    public uint DbRouteId => Volatile.Read(ref _dbRouteId);

    public bool TryClose() => Interlocked.Exchange(ref _closed, 1) == 0;

    /// <summary>
    /// Stable native route context carried at frame +0x08.
    ///
    /// The native M2 lookup key is not a per-frame sequence.  It is composed
    /// once from the registered gate number and the WORD session key:
    /// <c>(gateIndex &lt;&lt; 17) | sessionWord</c>.  Keep the historical method
    /// name because callers use it for every outgoing frame, but always return
    /// the same value for this route.
    /// </summary>
    public uint RouteId
    {
        get
        {
            var bound = Volatile.Read(ref _nativeRouteContext);
            return bound != 0
                ? bound
                : ComposeRouteId(GateIndex, NativeSessionId);
        }
    }

    public uint NextSequence() => RouteId;

    /// <summary>
    /// Binds the native gate slot returned by the M2 registration handshake.
    /// A route context already learned from Cmd=11 remains authoritative for
    /// this route; newly-created routes use the returned slot directly.
    /// </summary>
    public bool TryBindNativeGateIndex(int gateIndex)
    {
        if (gateIndex is < NativeGameGateCommands.MinGateIndex
            or > NativeGameGateCommands.MaxGateIndex)
            return false;

        if (Volatile.Read(ref _nativeRouteContext) != 0)
            return false;
        Volatile.Write(ref _gateIndex, gateIndex);
        return true;
    }

    public static uint ComposeRouteId(int gateIndex, ushort sessionId)
    {
        return NativeGameGateCommands.ComposeRouteId(gateIndex, sessionId);
    }

    /// <summary>
    /// Atomically accepts the route context advertised by M2 for this WORD
    /// session.  RunGate resolves the route by the frame +0x04 word and the
    /// native receiver stores frame +0x08 as an opaque value.  The M2 sender
    /// normally composes that value as (gateIndex &lt;&lt; 17) | sessionWord, but
    /// GameGate does not validate that shape here.  A conflicting rebinding is
    /// rejected so a stale frame cannot move the route after it is established.
    /// </summary>
    public bool BindNativeRouteContext(ushort sessionWord, uint routeId)
    {
        if (sessionWord != NativeSessionId || routeId == 0)
            return false;

        var current = Volatile.Read(ref _nativeRouteContext);
        if (current == routeId) return true;
        if (current != 0) return false;

        return Interlocked.CompareExchange(ref _nativeRouteContext, routeId, 0) == 0;
    }

    // Kept for callers that already resolved the session from ConnID.  New
    // receive paths should pass both wire fields to preserve the native lookup.
    public bool BindNativeRouteContext(uint routeId) =>
        BindNativeRouteContext(NativeSessionId, routeId);

    public void AbortOnce()
    {
        if (Interlocked.Exchange(ref _aborted, 1) != 0) return;
        try { Abort(); } catch { }
    }

    public void InvalidateGameRoute()
    {
        if (Interlocked.Exchange(ref _gameInvalidated, 1) != 0) return;
        Volatile.Write(ref NativePlayerRecog, 0);
        Volatile.Write(ref NativeServerUserIndex, 0);
        AbortOnce();
    }

    public void InvalidateDbRoute()
    {
        if (Interlocked.Exchange(ref _dbInvalidated, 1) != 0) return;
        TaskCompletionSource<bool>? completion;
        lock (_dbOpenStateLock)
        {
            completion = _dbOpenCompletion;
            _dbOpenCompletion = null;
            _dbOpenAcknowledgedGeneration = -1;
            Volatile.Write(ref _dbRouteId, 0);
            Volatile.Write(ref NativeDbOpenContext, 0);
        }
        completion?.TrySetResult(false);
        AbortOnce();
    }

    public bool TryBeginDbOpen(long generation, uint routeId,
        out Task<bool> completion, out bool shouldSend)
    {
        lock (_dbOpenStateLock)
        {
            if (_dbOpenAcknowledgedGeneration == generation
                && Volatile.Read(ref _dbRouteId) == routeId)
            {
                completion = Task.FromResult(true);
                shouldSend = false;
                return true;
            }
            if (DbConnectionGeneration == generation
                && _dbOpenCompletion != null)
            {
                completion = _dbOpenCompletion.Task;
                shouldSend = false;
                return true;
            }

            DbConnectionGeneration = generation;
            _dbOpenAcknowledgedGeneration = -1;
            Volatile.Write(ref _dbRouteId, routeId);
            Volatile.Write(ref NativeDbOpenContext, 0);
            _dbOpenCompletion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            completion = _dbOpenCompletion.Task;
            shouldSend = true;
            return true;
        }
    }

    public bool CompleteDbOpen(long generation, bool accepted,
        int backendContext)
    {
        TaskCompletionSource<bool>? completion;
        lock (_dbOpenStateLock)
        {
            if (DbConnectionGeneration != generation
                || _dbOpenCompletion == null)
                return false;
            completion = _dbOpenCompletion;
            if (accepted)
            {
                _dbOpenAcknowledgedGeneration = generation;
                Volatile.Write(ref NativeDbOpenContext, backendContext);
            }
        }
        return completion.TrySetResult(accepted);
    }

    public bool IsDbOpen(long generation)
    {
        lock (_dbOpenStateLock)
            return _dbOpenAcknowledgedGeneration == generation
                && DbConnectionGeneration == generation;
    }

    public async ValueTask<bool> QueueDbTerminationAsync(
        CancellationToken cancellationToken)
    {
        await DbSendCloseLock.WaitAsync(cancellationToken);
        var firstTermination = false;
        try
        {
            firstTermination = Interlocked.Exchange(
                ref _dbTerminationPending, 1) == 0;
        }
        finally { DbSendCloseLock.Release(); }
        if (!firstTermination) return true;

        try
        {
            await DbResponses.Writer.WriteAsync(Array.Empty<byte>(),
                cancellationToken);
            return true;
        }
        catch (ChannelClosedException)
        {
            AbortOnce();
            return false;
        }
    }
}

/// <summary>
/// Original GameGate topology: one shared DBSvr socket and one shared M2 socket.
/// Native logical client session keys are carried in DB 0x33AABB77 envelopes
/// and InternalPacket77.ConnID; the OS socket handle is retained only for
/// local diagnostics and lifecycle ownership.
/// </summary>
internal sealed class SharedBackendHub : IDisposable
{
    private static readonly ConcurrentDictionary<SharedBackendHub, byte>
        NativeRunGates = new();

    private readonly GateConfig _config;
    private readonly Action<string, string> _log;
    private readonly ConcurrentDictionary<uint, SharedBackendRoute> _routes = new();
    private readonly LegacyGateType24Cache _focusItemCache = new();
    private readonly SemaphoreSlim _dbConnectLock = new(1, 1);
    private readonly SemaphoreSlim _gameConnectLock = new(1, 1);
    private readonly SemaphoreSlim _dbWriteLock = new(1, 1);
    private readonly SemaphoreSlim _gameWriteLock = new(1, 1);
    private readonly object _dbStateLock = new();
    private readonly object _gameStateLock = new();
    private CancellationTokenSource? _stop;
    private Task? _dbDispatcher;
    private Task? _gameDispatcher;
    private Task? _heartbeat;
    private TcpClient? _dbClient;
    private NetworkStream? _dbStream;
    private TcpClient? _gameClient;
    private NetworkStream? _gameStream;
    private long _dbGeneration;
    private long _gameGeneration;
    private int _started;
    private long _lastDbErrorTick;
    private long _lastGameErrorTick;
    private long _nextDbConnectTick;
    private long _nextGameConnectTick;
    private int _reconnects;
    private int _heartbeatPending;
    private long _heartbeatSentTick;
    private int _registeredDbGateId;
    private TaskCompletionSource<int>? _dbRegistrationCompletion;
    private long _dbRegistrationGeneration;
    private int _registeredGateIndex;
    private TaskCompletionSource<int>? _gameRegistrationCompletion;
    private long _gameRegistrationGeneration;
    private int _requestedGateIndex;
    private long _lastUnknownGameTypeTick;
    private const int NativeRegistrationTimeoutMilliseconds = 5000;
    private const long NativeHeartbeatTimeoutMilliseconds = 60000;
    private const ushort NativeKeepAliveRequest =
        NativeGameGateCommands.GateKeepAliveRequest;

    // 2.08 战神 GameGate (M2 -> GameGate) command values.  Grobal2.GM_* remains the
    // historical C# GameSvr dialect; these constants are deliberately kept
    // local to the wire adapter so both peers can be supported while the
    // native command meanings are restored.
    private const ushort NativeRouteBind = 11;
    private const ushort NativeRouteClear = 12;
    private const ushort NativeKeepAliveReply = 13;
    private const ushort NativeClientData = 14;
    private const ushort NativeGateRegistrationReply = 15;
    private const ushort NativeSilentReserved16 = 16;
    private const ushort NativeCrossGateBroadcast = 17;
    private const ushort NativeTargetedMulticast = 19;
    private const ushort NativeSilentReserved21 = 21;
    private const ushort NativeSilentReserved22 = 22;
    private const ushort NativeSilentReserved23 = 23;

    public SharedBackendHub(GateConfig config, Action<string, string> log)
    {
        _config = config;
        _log = log;
    }

    public bool DBConnected { get; private set; }
    public bool GameConnected { get; private set; }
    public int Reconnects => Volatile.Read(ref _reconnects);
    public int RegisteredDbGateId => Volatile.Read(ref _registeredDbGateId);
    public int RegisteredGateIndex => Volatile.Read(ref _registeredGateIndex);
    public bool GameHeartbeatPending => Volatile.Read(ref _heartbeatPending) != 0;

    internal bool TryGetNativeFocusItem(int recog, out byte[] payload) =>
        _focusItemCache.TryGet(recog, out payload);

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        NativeRunGates.TryAdd(this, 0);
        _stop = new CancellationTokenSource();
        _dbDispatcher = Task.Run(() => DBDispatcherLoop(_stop.Token));
        _gameDispatcher = Task.Run(() => GameDispatcherLoop(_stop.Token));
        _heartbeat = Task.Run(() => HeartbeatLoop(_stop.Token));
    }

    public async Task<SharedBackendRoute?> OpenRouteAsync(int handle,
        ushort nativeSessionId, string clientIp, long sessionGeneration,
        Action abort, CancellationToken cancellationToken)
    {
        var connId = (uint)nativeSessionId;
        var route = new SharedBackendRoute
        {
            Handle = handle,
            NativeSessionId = nativeSessionId,
            GateIndex = _config.GateIndex,
            ConnId = connId,
            SessionGeneration = sessionGeneration,
            ClientIp = clientIp,
            DbOpenFrame = Array.Empty<byte>(),
            Abort = abort
        };
        if (!_routes.TryAdd(connId, route)) return null;
        try
        {
            if (!await EnsureDbRouteOpenAsync(route, cancellationToken))
            {
                route.TryClose();
                RemoveRoute(route);
                return null;
            }
            if (!await EnsureGameRouteOpenAsync(route, cancellationToken))
            {
                await CloseRouteAsync(route);
                return null;
            }
        }
        catch
        {
            await CloseRouteAsync(route);
            throw;
        }
        return route;
    }

    public async Task<bool> SendDbAsync(SharedBackendRoute route,
        ClientPacket message, byte[] body,
        CancellationToken cancellationToken = default)
    {
        if (route == null || route.IsClosed || route.IsInvalidated
            || route.IsDbTerminationPending) return false;
        if (!await EnsureDbRouteOpenAsync(route, cancellationToken)) return false;
        await route.DbSendCloseLock.WaitAsync(cancellationToken);
        NetworkStream? stream = null;
        try
        {
            if (route.IsClosed || route.IsInvalidated
                || route.IsDbTerminationPending
                || !TryGetDbState(out stream, out var generation)
                || !route.IsDbOpen(generation)) return false;
            if (!NativeGameGateDbProtocol.TryCreateData(
                    route.NativeSessionId, route.DbRouteId, message, body,
                    out var frame, out var error))
            {
                _log("WARN", "drop Gate->DB frame: " + error);
                return false;
            }
            await WriteDbCoreAsync(stream, frame, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            if (stream != null) InvalidateDb(stream, ex.Message);
            return false;
        }
        finally { route.DbSendCloseLock.Release(); }
    }

    public async Task<bool> SendGameAsync(SharedBackendRoute route, byte[] frame,
        CancellationToken cancellationToken = default)
    {
        if (!TryValidateNativeM2Frame(frame, out var frameError))
        {
            _log("WARN", $"drop Gate->M2 frame: {frameError}");
            return false;
        }
        if (route == null || route.IsClosed || route.IsInvalidated) return false;
        if (!await EnsureGameRouteOpenAsync(route, cancellationToken)) return false;
        if (!TryGetGameState(out var stream, out _)) return false;
        try
        {
            await WriteGameCoreAsync(stream, frame, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            InvalidateGame(stream, ex.Message);
            return false;
        }
    }

    public bool TryGetRoute(uint connId, long sessionGeneration, out SharedBackendRoute route)
    {
        if (_routes.TryGetValue(connId, out route!) && !route.IsClosed
            && !route.IsInvalidated
            && route.SessionGeneration == sessionGeneration)
            return true;
        route = null!;
        return false;
    }

    public async Task CloseRouteAsync(SharedBackendRoute? route)
    {
        if (route == null || !route.TryClose()) return;

        await route.DbSendCloseLock.WaitAsync(CancellationToken.None);
        try
        {
            if (TryGetDbState(out var dbStream, out var dbGeneration)
                && route.IsDbOpen(dbGeneration))
            {
                try
                {
                    if (NativeGameGateDbProtocol.TryCreateClose(
                            route.NativeSessionId,
                            Volatile.Read(ref route.NativeDbOpenContext),
                            out var closeDb, out _))
                        await WriteDbCoreAsync(dbStream, closeDb,
                            CancellationToken.None);
                }
                catch { }
            }
        }
        finally { route.DbSendCloseLock.Release(); }

        if (TryGetGameState(out var gameStream, out var gameGeneration)
            && route.GameConnectionGeneration == gameGeneration)
        {
            try
            {
                var close = CreateGameControl(route.ConnId, route.NextSequence(),
                    Grobal2.GM_CLOSE, Array.Empty<byte>());
                using var timeout = new CancellationTokenSource(1000);
                await WriteGameCoreAsync(gameStream, close.ToBytes(), timeout.Token);
            }
            catch { }
        }

        RemoveRoute(route);
    }

    public async Task StopAsync()
    {
        NativeRunGates.TryRemove(this, out _);
        var stop = _stop;
        if (stop == null) return;
        stop.Cancel();
        InvalidateDb(null, null);
        InvalidateGame(null, null);
        foreach (var route in _routes.Values)
        {
            route.TryClose();
            route.DbResponses.Writer.TryComplete();
            route.GameResponses.Writer.TryComplete();
        }
        _routes.Clear();
        var tasks = new[] { _dbDispatcher, _gameDispatcher, _heartbeat }
            .Where(task => task != null).Cast<Task>().ToArray();
        try { await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
        stop.Dispose();
        _stop = null;
    }

    private async Task DBDispatcherLoop(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var parser = new DbServerGatewayFrameParser();
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!await EnsureDbConnectedAsync(cancellationToken))
            {
                await DelayRetry(cancellationToken);
                continue;
            }
            if (!TryGetDbState(out var stream, out var generation)) continue;
            parser.Reset();
            try
            {
                while (!cancellationToken.IsCancellationRequested
                       && IsCurrentDbStream(stream, generation))
                {
                    var count = await stream.ReadAsync(buffer, cancellationToken);
                    if (count <= 0) throw new IOException("DBSvr closed the shared connection");
                    if (!parser.TryAppend(buffer, 0, count, out var frames, out var error))
                        throw new InvalidDataException(error);
                    foreach (var frame in frames)
                    {
                        if (frame.Kind == DbServerGatewayFrameKind.NativeControl)
                            await DispatchDbControlAsync(frame,
                                stream, generation, cancellationToken);
                        else
                            DispatchDbFrame(frame.Data, stream, generation);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                InvalidateDb(stream, ex.Message);
                await DelayRetry(cancellationToken);
            }
        }
    }

    private async Task GameDispatcherLoop(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var parser = new GameGateServerFrameParser();
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!await EnsureGameConnectedAsync(cancellationToken))
            {
                await DelayRetry(cancellationToken);
                continue;
            }
            if (!TryGetGameState(out var stream, out var generation)) continue;
            parser.Reset();
            try
            {
                foreach (var route in _routes.Values)
                    await EnsureGameRouteOpenAsync(route, cancellationToken,
                        waitForRegistration: false);

                while (!cancellationToken.IsCancellationRequested
                       && IsCurrentGameStream(stream, generation))
                {
                    var count = await stream.ReadAsync(buffer, cancellationToken);
                    if (count <= 0) throw new IOException("GameSvr closed the shared connection");
                    if (!parser.TryAppend(buffer, 0, count, out var frames, out var error))
                        throw new InvalidDataException(error);
                    foreach (var frame in frames)
                    {
                        if (frame.Internal77 != null)
                            await DispatchGamePacketAsync(stream, generation, frame.Internal77,
                                cancellationToken);
                        else if (frame.LegacyType17 != null)
                            await DispatchLegacyType17Async(frame.LegacyType17,
                                cancellationToken);
                        else if (frame.LegacyType18 != null)
                            TryDispatchLegacyType18(frame.LegacyType18);
                        else if (frame.LegacyType19 != null)
                            TryDispatchLegacyType19(frame.LegacyType19);
                        else if (frame.LegacyType20 != null)
                            await EchoLegacyType20Async(stream, generation,
                                frame.LegacyType20, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                InvalidateGame(stream, ex.Message);
                await DelayRetry(cancellationToken);
            }
        }
    }

    private async Task HeartbeatLoop(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (Volatile.Read(ref _heartbeatPending) != 0)
                {
                    var sentTick = Interlocked.Read(ref _heartbeatSentTick);
                    if (IsHeartbeatExpired(Environment.TickCount64, sentTick,
                        pending: true))
                    {
                        if (TryGetGameState(out var staleStream, out _))
                            InvalidateGame(staleStream, "native heartbeat timeout");
                    }
                    // One outstanding native heartbeat is enough.  Do not
                    // refresh its timestamp by sending another request.
                    continue;
                }
                await SendGameHeartbeatOnceAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    internal static bool IsHeartbeatExpired(long nowTick, long sentTick,
        bool pending)
    {
        return pending && sentTick > 0 && nowTick >= sentTick
            && nowTick - sentTick >= NativeHeartbeatTimeoutMilliseconds;
    }

    internal async Task<bool> SendGameHeartbeatOnceAsync(CancellationToken cancellationToken)
    {
        if (!await EnsureGameConnectedAsync(cancellationToken)) return false;
        if (!TryGetGameState(out var stream, out var generation)) return false;
        // Native GameGate establishes its 1..32 slot before control traffic;
        // do not let the periodic heartbeat race the one-shot Cmd=5 handshake.
        if (!await WaitForGameRegistrationAsync(generation, cancellationToken))
            return false;
        if (Interlocked.CompareExchange(ref _heartbeatPending, 1, 0) != 0)
            return false;
        try
        {
            // Native 2.08 emits a bare 16-byte keepalive: both routing words
            // are zero.  The heartbeat is a liveness probe, not a session
            // sequence, so do not put a locally generated counter on wire.
            var heartbeat = CreateGameControl(0, 0,
                NativeKeepAliveRequest, Array.Empty<byte>());
            // Publish the deadline before the write.  A very fast peer can
            // answer while WriteAsync is still completing; writing the tick
            // afterwards would resurrect a request that the reply cleared.
            Interlocked.Exchange(ref _heartbeatSentTick, Environment.TickCount64);
            await WriteGameCoreAsync(stream, heartbeat.ToBytes(), cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            Volatile.Write(ref _heartbeatPending, 0);
            Interlocked.Exchange(ref _heartbeatSentTick, 0);
            InvalidateGame(stream, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Validates the direction-specific Gate -> M2 envelope before any route
    /// or socket work.  The native M2 receiver accepts BodyLen through 0x3000;
    /// it does not truncate a larger frame, so the managed adapter must drop
    /// it before writing the shared stream.
    /// </summary>
    internal static bool TryValidateNativeM2Frame(byte[]? frame,
        out string error)
    {
        error = string.Empty;
        if (frame == null)
        {
            error = "frame is null";
            return false;
        }
        if (frame.Length < InternalPacket77.HEADER_SIZE)
        {
            error = $"frame is shorter than {InternalPacket77.HEADER_SIZE} bytes";
            return false;
        }
        if (BitConverter.ToUInt32(frame, 0) != InternalPacket77.MAGIC)
        {
            error = "invalid 77BBAA33 magic";
            return false;
        }

        var bodyLength = BitConverter.ToUInt16(frame, 14);
        var expectedLength = InternalPacket77.HEADER_SIZE + bodyLength;
        if (expectedLength != frame.Length)
        {
            error = $"declared body {bodyLength} does not match frame length "
                    + frame.Length;
            return false;
        }
        if (bodyLength > NativeGameGateCommands.NativeM2MaximumBodyLength)
        {
            error = $"body {bodyLength} exceeds native limit "
                    + NativeGameGateCommands.NativeM2MaximumBodyLength;
            return false;
        }
        return true;
    }

    private async Task<bool> EnsureDbRouteOpenAsync(SharedBackendRoute route,
        CancellationToken cancellationToken, bool waitForRegistration = true,
        bool waitForOpenReply = true)
    {
        if (route.IsClosed || route.IsInvalidated
            || !await EnsureDbConnectedAsync(cancellationToken)) return false;

        if (!TryGetDbState(out _, out var initialGeneration)) return false;
        if (!IsDbRegistrationReady(initialGeneration))
        {
            if (!waitForRegistration
                || !await WaitForDbRegistrationAsync(initialGeneration,
                    cancellationToken))
                return false;
        }

        Task<bool>? openCompletion = null;
        await route.DbOpenLock.WaitAsync(cancellationToken);
        try
        {
            if (route.IsClosed || route.IsInvalidated
                || !TryGetDbState(out var stream, out var generation)
                || !IsDbRegistrationReady(generation)) return false;

            var gateId = RegisteredDbGateId;
            var routeId = unchecked((uint)
                NativeGameGateDbProtocol.ComposeRouteId(gateId,
                    route.NativeSessionId));
            route.TryBeginDbOpen(generation, routeId,
                out openCompletion, out var shouldSend);
            if (shouldSend)
            {
                if (!NativeGameGateDbProtocol.TryCreateOpen(
                        route.NativeSessionId, gateId, route.ClientIp,
                        out var open, out var error))
                    throw new InvalidDataException(error);
                await WriteDbCoreAsync(stream, open, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException
                                   or ObjectDisposedException
                                   or InvalidDataException)
        {
            if (TryGetDbState(out var stream, out _))
                InvalidateDb(stream, ex.Message);
            return false;
        }
        finally { route.DbOpenLock.Release(); }

        if (!waitForOpenReply) return true;
        if (openCompletion == null) return false;
        try
        {
            return await openCompletion.WaitAsync(
                TimeSpan.FromMilliseconds(NativeRegistrationTimeoutMilliseconds),
                cancellationToken);
        }
        catch (TimeoutException)
        {
            if (TryGetDbState(out var staleStream, out var generation)
                && route.DbConnectionGeneration == generation)
                InvalidateDb(staleStream, "native DB route-open timeout");
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private bool IsDbRegistrationReady(long generation)
    {
        lock (_dbStateLock)
        {
            return DBConnected && generation == _dbGeneration
                && _dbRegistrationGeneration == generation
                && NativeGameGateDbProtocol.IsValidAssignedGateId(
                    _registeredDbGateId);
        }
    }

    private async Task<bool> WaitForDbRegistrationAsync(long generation,
        CancellationToken cancellationToken)
    {
        Task<int>? completion;
        lock (_dbStateLock)
        {
            if (!DBConnected || generation != _dbGeneration
                || _dbRegistrationGeneration != generation)
                return false;
            if (NativeGameGateDbProtocol.IsValidAssignedGateId(
                    _registeredDbGateId)) return true;
            completion = _dbRegistrationCompletion?.Task;
        }
        if (completion == null) return false;

        try
        {
            var assigned = await completion.WaitAsync(
                TimeSpan.FromMilliseconds(NativeRegistrationTimeoutMilliseconds),
                cancellationToken);
            return NativeGameGateDbProtocol.IsValidAssignedGateId(assigned)
                && IsDbRegistrationReady(generation);
        }
        catch (TimeoutException)
        {
            if (TryGetDbState(out var staleStream, out var currentGeneration)
                && currentGeneration == generation)
                InvalidateDb(staleStream, "native DB registration timeout");
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private async Task<bool> EnsureGameRouteOpenAsync(SharedBackendRoute route,
        CancellationToken cancellationToken, bool waitForRegistration = true)
    {
        if (route.IsClosed || route.IsInvalidated
            || !await EnsureGameConnectedAsync(cancellationToken)) return false;

        if (!TryGetGameState(out _, out var initialGeneration))
            return false;
        if (!IsGameRegistrationReady(initialGeneration))
        {
            if (!waitForRegistration
                || !await WaitForGameRegistrationAsync(initialGeneration,
                    cancellationToken))
                return false;
        }

        await route.GameOpenLock.WaitAsync(cancellationToken);
        try
        {
            if (route.IsClosed || route.IsInvalidated
                || !TryGetGameState(out var stream, out var generation)) return false;
            if (!IsGameRegistrationReady(generation))
            {
                // The connection may have been replaced while the caller
                // waited outside the route lock.  Never emit OPEN on an
                // unregistered generation; the next caller will await it.
                return false;
            }
            route.TryBindNativeGateIndex(RegisteredGateIndex);
            if (route.GameConnectionGeneration == generation) return true;
            Volatile.Write(ref route.NativePlayerRecog, 0);
            Volatile.Write(ref route.NativeServerUserIndex, 0);
            var open = CreateGameControl(route.ConnId, route.NextSequence(),
                Grobal2.GM_OPEN, HUtil32.GetBytes(route.ClientIp));
            await WriteGameCoreAsync(stream, open.ToBytes(), cancellationToken);
            route.GameConnectionGeneration = generation;
            return true;
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            if (TryGetGameState(out var stream, out _)) InvalidateGame(stream, ex.Message);
            return false;
        }
        finally { route.GameOpenLock.Release(); }
    }

    private bool IsGameRegistrationReady(long generation)
    {
        lock (_gameStateLock)
        {
            return GameConnected && generation == _gameGeneration
                && _gameRegistrationGeneration == generation
                && IsValidNativeGateIndex(_registeredGateIndex);
        }
    }

    private async Task<bool> WaitForGameRegistrationAsync(long generation,
        CancellationToken cancellationToken)
    {
        Task<int>? completion;
        lock (_gameStateLock)
        {
            if (!GameConnected || generation != _gameGeneration
                || _gameRegistrationGeneration != generation)
                return false;
            if (IsValidNativeGateIndex(_registeredGateIndex)) return true;
            completion = _gameRegistrationCompletion?.Task;
        }
        if (completion == null) return false;

        try
        {
            var assigned = await completion.WaitAsync(
                TimeSpan.FromMilliseconds(NativeRegistrationTimeoutMilliseconds),
                cancellationToken);
            return IsValidNativeGateIndex(assigned)
                && IsGameRegistrationReady(generation);
        }
        catch (TimeoutException)
        {
            if (TryGetGameState(out var staleStream, out var currentGeneration)
                && currentGeneration == generation)
                InvalidateGame(staleStream, "native gate registration timeout");
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private async Task<bool> EnsureDbConnectedAsync(CancellationToken cancellationToken)
    {
        if (TryGetDbState(out _, out _)) return true;
        if (Environment.TickCount64 < Volatile.Read(ref _nextDbConnectTick)) return false;
        await _dbConnectLock.WaitAsync(cancellationToken);
        try
        {
            if (TryGetDbState(out _, out _)) return true;
            if (Environment.TickCount64 < Volatile.Read(ref _nextDbConnectTick)) return false;
            try
            {
                var client = new TcpClient { NoDelay = true };
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(3000);
                await client.ConnectAsync(_config.BackendIP,
                    _config.BackendPort2 > 0 ? _config.BackendPort2 : 5100, timeout.Token);
                lock (_dbStateLock)
                {
                    _dbClient = client;
                    _dbStream = client.GetStream();
                    _dbGeneration++;
                    DBConnected = true;
                    _dbRegistrationGeneration = _dbGeneration;
                    _dbRegistrationCompletion =
                        new TaskCompletionSource<int>(
                            TaskCreationOptions.RunContinuationsAsynchronously);
                }
                Volatile.Write(ref _registeredDbGateId, 0);
                Volatile.Write(ref _nextDbConnectTick, 0);
                Interlocked.Increment(ref _reconnects);
                _log("INFO", $"DBSvr shared connection {_config.BackendIP}:{_config.BackendPort2}");

                if (!NativeGameGateDbProtocol.TryCreateRegistration(
                        _config.GatePort, out var registration,
                        out var registrationError))
                    throw new InvalidDataException(registrationError);
                await WriteDbCoreAsync(client.GetStream(), registration,
                    timeout.Token);
                _log("TRACE", $"DBSvr gate registration port={_config.GatePort}");
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                if (TryGetDbState(out var connectedStream, out _))
                    InvalidateDb(connectedStream, ex.Message);
                Volatile.Write(ref _nextDbConnectTick, Environment.TickCount64 + 1000);
                LogBackendError(ref _lastDbErrorTick, "DBSvr", ex.Message);
                return false;
            }
        }
        finally { _dbConnectLock.Release(); }
    }

    private async Task<bool> EnsureGameConnectedAsync(CancellationToken cancellationToken)
    {
        if (TryGetGameState(out _, out _)) return true;
        if (Environment.TickCount64 < Volatile.Read(ref _nextGameConnectTick)) return false;
        await _gameConnectLock.WaitAsync(cancellationToken);
        try
        {
            if (TryGetGameState(out _, out _)) return true;
            if (Environment.TickCount64 < Volatile.Read(ref _nextGameConnectTick)) return false;
            try
            {
                var client = new TcpClient { NoDelay = true };
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(3000);
                await client.ConnectAsync(_config.GameBackendIP, _config.BackendPort, timeout.Token);
                lock (_gameStateLock)
                {
                    _gameClient = client;
                    _gameStream = client.GetStream();
                    _gameGeneration++;
                    GameConnected = true;
                    _gameRegistrationGeneration = _gameGeneration;
                    _requestedGateIndex = Math.Clamp(_config.GateIndex,
                        NativeGameGateCommands.MinGateIndex,
                        NativeGameGateCommands.MaxGateIndex);
                    _gameRegistrationCompletion =
                        new TaskCompletionSource<int>(
                            TaskCreationOptions.RunContinuationsAsynchronously);
                }
                Volatile.Write(ref _registeredGateIndex, 0);
                Volatile.Write(ref _heartbeatPending, 0);
                Interlocked.Exchange(ref _heartbeatSentTick, 0);
                Volatile.Write(ref _nextGameConnectTick, 0);
                Interlocked.Increment(ref _reconnects);
                _log("INFO", $"GameSvr shared connection {_config.GameBackendIP}:{_config.BackendPort}");

                // Native M2 does not infer a gate number from the TCP handle.
                // It consumes a one-shot type-5 registration frame and reads
                // byte[+0x08].  Send the exact 16-byte control frame before
                // opening any client route; the dispatcher records the type-15
                // reply when M2 assigns the slot.
                var gateIndex = Volatile.Read(ref _requestedGateIndex);
                var registration = CreateGameControl(0, (uint)gateIndex,
                    NativeGameGateCommands.GateRegistrationRequest,
                    Array.Empty<byte>());
                await WriteGameCoreAsync(client.GetStream(),
                    registration.ToBytes(), timeout.Token);
                _log("TRACE", $"GameSvr gate registration requested index={gateIndex}");
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                if (TryGetGameState(out var connectedStream, out _))
                    InvalidateGame(connectedStream, ex.Message);
                Volatile.Write(ref _nextGameConnectTick, Environment.TickCount64 + 1000);
                LogBackendError(ref _lastGameErrorTick, "GameSvr", ex.Message);
                return false;
            }
        }
        finally { _gameConnectLock.Release(); }
    }

    private async Task WriteDbCoreAsync(NetworkStream stream, byte[] frame,
        CancellationToken cancellationToken)
    {
        await _dbWriteLock.WaitAsync(cancellationToken);
        try
        {
            if (!ReferenceEquals(stream, _dbStream)) throw new IOException("stale DBSvr stream");
            await stream.WriteAsync(frame, cancellationToken);
        }
        finally { _dbWriteLock.Release(); }
    }

    private async Task WriteGameCoreAsync(NetworkStream stream, byte[] frame,
        CancellationToken cancellationToken)
    {
        await _gameWriteLock.WaitAsync(cancellationToken);
        try
        {
            if (!ReferenceEquals(stream, _gameStream)) throw new IOException("stale GameSvr stream");
            await stream.WriteAsync(frame, cancellationToken);
        }
        finally { _gameWriteLock.Release(); }
    }

    private void DispatchDbFrame(byte[] frame, NetworkStream stream,
        long generation)
    {
        if (!IsCurrentDbStream(stream, generation)) return;
        var slash = Array.IndexOf(frame, (byte)'/');
        if (slash <= 1) return;
        var start = 1;
        if (start < slash && frame[start] != (byte)'-' && !char.IsDigit((char)frame[start])) start++;
        if (!int.TryParse(System.Text.Encoding.ASCII.GetString(frame, start, slash - start),
                out var handle)) return;
        if (!_routes.TryGetValue(unchecked((uint)handle), out var route) || route.IsClosed
            || route.IsInvalidated
            || route.DbConnectionGeneration != generation
            || !route.IsDbOpen(generation)) return;
        if (!route.DbResponses.Writer.TryWrite(frame)) route.AbortOnce();
    }

    private async Task DispatchGamePacketAsync(NetworkStream stream, long generation,
        InternalPacket77 packet, CancellationToken cancellationToken)
    {
        // Native RunGate binds the stable +0x08 context before it starts
        // delivering client data.  The +0x04 word is still the lookup key.
        if (packet.Cmd == NativeRouteBind)
        {
            if (TryGetIncomingRoute(packet.ConnID, out var boundRoute))
            {
                boundRoute.BindNativeRouteContext(
                    unchecked((ushort)packet.ConnID), packet.SeqID);
                _log("TRACE", $"M2 route bind session={boundRoute.NativeSessionId} " +
                    $"route=0x{packet.SeqID:X8}");
            }
            else
            {
                LogUnknownGameType(packet, "route-bind-miss");
            }
            return;
        }

        if (packet.Cmd == NativeRouteClear)
        {
            if (TryGetIncomingRoute(packet.ConnID, out var clearedRoute))
                ClearNativeRouteState(clearedRoute);
            return;
        }

        if (packet.Cmd == NativeKeepAliveReply && packet.ConnID == 0
            && IsCurrentGameStream(stream, generation))
        {
            Volatile.Write(ref _heartbeatPending, 0);
            Interlocked.Exchange(ref _heartbeatSentTick, 0);
            return;
        }

        // M2 may initiate the same native liveness exchange.  RunGate answers
        // a type-3 request with a bare type-13 frame on ConnID=0; ignoring it
        // leaves the server-side watchdog waiting forever.
        if (packet.Cmd == NativeKeepAliveRequest && packet.ConnID == 0
            && IsCurrentGameStream(stream, generation))
        {
            // The native M2 response is also a bare type-13 frame with both
            // routing words cleared; it does not echo an arbitrary request
            // sequence from the control packet.
            var reply = CreateGameControl(0, 0,
                NativeKeepAliveReply, Array.Empty<byte>());
            await WriteGameCoreAsync(stream, reply.ToBytes(), cancellationToken);
            return;
        }

        if (packet.Cmd == NativeGateRegistrationReply)
        {
            var candidate = (int)(packet.ConnID & 0xFF);
            if (!IsValidNativeGateIndex(candidate))
            {
                LogUnknownGameType(packet, "invalid-gate-registration");
                return;
            }
            if (!IsCurrentGameStream(stream, generation)) return;

            TaskCompletionSource<int>? completion;
            lock (_gameStateLock)
            {
                if (!GameConnected || generation != _gameGeneration)
                    return;
                Volatile.Write(ref _registeredGateIndex, candidate);
                completion = _gameRegistrationCompletion;
            }
            foreach (var candidateRoute in _routes.Values)
            {
                if (!candidateRoute.IsClosed && !candidateRoute.IsInvalidated)
                    candidateRoute.TryBindNativeGateIndex(candidate);
            }
            completion?.TrySetResult(candidate);
            _log("TRACE", $"GameSvr gate registration accepted index={candidate}");

            // The dispatcher deliberately does not wait for Cmd=15 (doing so
            // would stop it from reading the reply).  Flush routes that were
            // created while the one-shot registration was in flight now.
            foreach (var pendingRoute in _routes.Values)
            {
                if (pendingRoute.IsClosed || pendingRoute.IsInvalidated
                    || pendingRoute.GameConnectionGeneration == generation)
                    continue;
                await EnsureGameRouteOpenAsync(pendingRoute, cancellationToken,
                    waitForRegistration: false);
            }
            return;
        }

        if (packet.Cmd == LegacyGateType24Cache.MessageType)
        {
            if (!_focusItemCache.TryStore(packet.Payload))
                LogUnknownGameType(packet, "invalid-focus-item-cache-payload");
            return;
        }

        if (IsNativeSilentConsumeType(packet.Cmd))
        {
            // Native 2019 RunGate jump table 0x47FE48 maps 16 and 21..23
            // directly to 0x47FFF1.  That target only advances by the complete
            // frame length and continues parsing; it performs no route lookup,
            // client delivery, state mutation, log, or backend write.
            return;
        }

        if (packet.ConnID == 0)
        {
            if (packet.Cmd == Grobal2.GM_RECEIVE_OK
                && IsCurrentGameStream(stream, generation))
            {
                var acknowledgement = CreateGameControl(0, packet.SeqID,
                    Grobal2.GM_RECEIVE_OK, Array.Empty<byte>());
                await WriteGameCoreAsync(stream, acknowledgement.ToBytes(), cancellationToken);
            }
            return;
        }

        if (!TryGetIncomingRoute(packet.ConnID, out var route))
        {
            LogUnknownGameType(packet, "route-miss");
            return;
        }

        // Native 0x0E is the single-client data path.  Normalize it to the
        // internal client-data command consumed by GateServer, while leaving
        // the original wire fields (connection/context/payload) intact.
        if (packet.Cmd == NativeClientData)
        {
            packet = new InternalPacket77
            {
                Magic = packet.Magic,
                ConnID = packet.ConnID,
                SeqID = packet.SeqID,
                FrameLen = packet.FrameLen,
                Cmd = Grobal2.GM_DATA,
                Payload = packet.Payload ?? Array.Empty<byte>()
            };
        }
        if (packet.Cmd == Grobal2.GM_SERVERUSERINDEX
            && packet.Payload is { Length: >= sizeof(int) })
        {
            var serverUserIndex = BitConverter.ToInt32(packet.Payload, 0);
            if (serverUserIndex < 0) serverUserIndex = 0;
            Volatile.Write(ref route.NativeServerUserIndex, serverUserIndex);
            Volatile.Write(ref route.NativePlayerRecog, 0);
        }
        else if (packet.Cmd == Grobal2.GM_DATA
                 && packet.Payload is { Length: >= ClientPacket.PackSize }
                 && BitConverter.ToUInt16(packet.Payload, sizeof(int)) == Grobal2.SM_NEWMAP)
        {
            Volatile.Write(ref route.NativePlayerRecog,
                BitConverter.ToInt32(packet.Payload, 0));
        }
        if (!route.GameResponses.Writer.TryWrite(packet)) route.AbortOnce();
    }

    internal static bool IsNativeSilentConsumeType(ushort command) =>
        command == NativeSilentReserved16
        || command is >= NativeSilentReserved21 and <= NativeSilentReserved23;

    private bool TryGetIncomingRoute(uint wireSession,
        out SharedBackendRoute route)
    {
        if (_routes.TryGetValue(wireSession, out route!) && !route.IsClosed
            && !route.IsInvalidated)
            return true;

        // A few native producers pass the composed route context back in the
        // lookup field.  Accept that form without weakening the primary WORD
        // session-key namespace.
        if (wireSession > ushort.MaxValue
            && _routes.TryGetValue(wireSession & ushort.MaxValue, out route!)
            && !route.IsClosed && !route.IsInvalidated
            && route.RouteId == wireSession)
            return true;

        route = null!;
        return false;
    }

    internal async ValueTask<bool> DispatchDbControlAsync(
        DbServerGatewayFrame frame,
        CancellationToken cancellationToken = default)
    {
        TryGetDbState(out var stream, out var generation);
        return await DispatchDbControlAsync(frame, stream, generation,
            cancellationToken);
    }

    private async ValueTask<bool> DispatchDbControlAsync(
        DbServerGatewayFrame frame, NetworkStream? stream, long generation,
        CancellationToken cancellationToken)
    {
        if (frame == null
            || frame.Kind != DbServerGatewayFrameKind.NativeControl)
            return false;

        var nativeFrame = new YbDbLegacy77Frame(
            unchecked((int)frame.ConnectionId), frame.Parameter,
            frame.Command, frame.Payload);

        if (frame.Command == NativeGameGateDbProtocol.RegisterResponse)
        {
            if (!NativeGameGateDbProtocol.TryDecodeRegistrationResponse(
                    nativeFrame, out var assignedGateId))
                return true;

            TaskCompletionSource<int>? registrationCompletion;
            lock (_dbStateLock)
            {
                if (stream != null
                    && (!DBConnected || !ReferenceEquals(stream, _dbStream)
                        || generation != _dbGeneration
                        || _dbRegistrationGeneration != generation))
                    return false;
                if (Volatile.Read(ref _registeredDbGateId) == 0)
                    Volatile.Write(ref _registeredDbGateId, assignedGateId);
                else
                    assignedGateId = Volatile.Read(ref _registeredDbGateId);
                registrationCompletion = _dbRegistrationCompletion;
            }
            registrationCompletion?.TrySetResult(assignedGateId);

            // The sole DB reader must not wait for command 11 here. Emit each
            // pending OPEN in order, then return to the read loop for replies.
            if (stream != null)
                foreach (var pendingRoute in _routes.Values)
                {
                    if (!pendingRoute.IsClosed && !pendingRoute.IsInvalidated)
                        await EnsureDbRouteOpenAsync(pendingRoute,
                            cancellationToken, waitForRegistration: false,
                            waitForOpenReply: false);
                }
            return true;
        }

        if (stream != null && !IsCurrentDbStream(stream, generation))
            return false;

        // Unknown native controls are valid envelopes and are consumed by the
        // original switch default without damaging the shared DB connection.
        if (frame.Command is not NativeGameGateDbProtocol.OpenResponse
            and not 12 and not NativeGameGateDbProtocol.DataResponse
            and not NativeGameGateDbProtocol.CloseResponse and not 21)
            return true;

        if (frame.Command == NativeGameGateDbProtocol.CloseResponse)
            return true;

        if (!_routes.TryGetValue(frame.ConnectionId, out var route)
            || route.IsClosed || route.IsInvalidated)
            return false;
        if (route.DbConnectionGeneration != generation)
            return false;
        if (stream != null
            && (frame.Command is 12
                or NativeGameGateDbProtocol.DataResponse)
            && !route.IsDbOpen(generation))
            return false;

        switch (frame.Command)
        {
            case NativeGameGateDbProtocol.OpenResponse:
                if (!NativeGameGateDbProtocol.IsOpenResponse(nativeFrame,
                        out var openResult)) return true;
                var acceptedOpen = IsAcceptedDbControlResult(openResult);
                var firstCompletion = route.CompleteDbOpen(generation,
                    acceptedOpen, openResult);
                if (!firstCompletion) return true;
                ClearNativeRouteState(route);
                if (!acceptedOpen) return true;
                if (route.DbResponses.Writer.TryWrite(
                        CreateNativeDbLoginPrompt(route.NativeSessionId)))
                    return true;
                route.AbortOnce();
                return false;
            case 12:
                // SelGate sets its close flag here. The consumer observes the
                // sentinel after every earlier queued response (notably 4040),
                // then the ordinary client cleanup closes the route.
                return await route.QueueDbTerminationAsync(cancellationToken);
            case NativeGameGateDbProtocol.DataResponse:
                if (!LegacyGateDataCodec.TryDecodeResponse(nativeFrame,
                        out var dataMessage, out _)) return true;
                if (route.DbResponses.Writer.TryWrite(
                        CreateLegacyDbResponseFrame(route.NativeSessionId,
                            dataMessage))) return true;
                route.AbortOnce();
                return false;
            case 21:
                ClearNativeRouteState(route);
                if (IsAcceptedDbControlResult(frame.Parameter))
                    Volatile.Write(ref route.NativeDbControlContext,
                        frame.Parameter);
                return true;
            default:
                return true;
        }
    }

    private static bool IsAcceptedDbControlResult(int result) =>
        result >= 0 || result == -999;

    private static byte[] CreateNativeDbLoginPrompt(ushort connectionId)
    {
        var message = Grobal2.MakeDefaultMsg(Grobal2.SM_LOGIN, 0, 0, 0, 0);
        var encoded = EDcode.EncodeMessage(message);
        while (encoded.Length < Grobal2.DEFBLOCKSIZE) encoded += "0";
        return HUtil32.GetBytes($"%{connectionId}/#{encoded}!$");
    }

    private static byte[] CreateLegacyDbResponseFrame(ushort connectionId,
        LegacyGateDataMessage message)
    {
        var header = new ClientPacket
        {
            Recog = message.Recog,
            // Native DB replies already carry the client command. Convert it
            // back once because GateServer's legacy down-adapter applies the
            // established ToClient mapping after decoding this frame.
            Ident = MobileCmdMap.ToServer(message.Ident),
            Param = message.Param,
            Tag = message.Tag,
            Series = message.Series
        };
        var encoded = EDcode.EncodeMessage(header);
        while (encoded.Length < Grobal2.DEFBLOCKSIZE) encoded += "0";
        if (message.Body.Length > 0)
        {
            var body = new byte[message.Body.Length * 2 + 4];
            var bodyLength = Misc.Encode6BitBufDirect(message.Body,
                message.Body.Length, body);
            if (bodyLength > 0)
                encoded += HUtil32.GetString(body, 0, bodyLength);
        }
        return HUtil32.GetBytes($"%{connectionId}/#{encoded}!$");
    }

    private static void ClearNativeRouteState(SharedBackendRoute route)
    {
        Volatile.Write(ref route.NativePlayerRecog, 0);
        Volatile.Write(ref route.NativeServerUserIndex, 0);
    }

    private void LogUnknownGameType(InternalPacket77 packet, string reason)
    {
        var now = Environment.TickCount64;
        var previous = Interlocked.Read(ref _lastUnknownGameTypeTick);
        if (previous != 0 && now - previous < 30000) return;
        Interlocked.Exchange(ref _lastUnknownGameTypeTick, now);
        _log("TRACE", $"M2 frame consumed reason={reason} type={packet.Cmd} " +
            $"conn=0x{packet.ConnID:X8} route=0x{packet.SeqID:X8} " +
            $"body={packet.Payload?.Length ?? 0}");
    }

    internal async Task<int> DispatchLegacyType17Async(LegacyGateType17 packet,
        CancellationToken cancellationToken)
    {
        if (packet == null || !packet.CanForward) return 0;

        var frame = packet.ToForwardedBytes();
        var forwarded = 0;
        foreach (var candidate in NativeRunGates.Keys)
        {
            if (!packet.ShouldForwardTo(
                    unchecked((byte)candidate.RegisteredGateIndex),
                    ReferenceEquals(candidate, this)))
                continue;
            if (await candidate.TryWriteNativeCrossGateAsync(frame, cancellationToken))
                forwarded++;
        }
        return forwarded;
    }

    internal async Task<bool> EchoLegacyType20Async(NetworkStream stream,
        long generation, LegacyGateType20 packet,
        CancellationToken cancellationToken)
    {
        if (packet == null || !packet.CanEcho
            || !IsCurrentGameStream(stream, generation))
            return false;

        await WriteGameCoreAsync(stream, packet.ToBytes(), cancellationToken);
        return true;
    }

    private async Task<bool> TryWriteNativeCrossGateAsync(byte[] frame,
        CancellationToken cancellationToken)
    {
        if (!TryGetGameState(out var stream, out _)) return false;
        try
        {
            await WriteGameCoreAsync(stream, frame, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is IOException or SocketException
                                   or ObjectDisposedException)
        {
            InvalidateGame(stream, ex.Message);
            return false;
        }
    }

    internal bool TryDispatchLegacyType18(LegacyGateType18 packet)
    {
        if (packet == null) return false;

        var payload = packet.ToClientPayload();
        // The native relay adds its own 12-byte transport header and drops a
        // client payload when the resulting block is not below 0x8000 bytes.
        if (payload.Length < LegacyGateType18.ClientPacketSize
            || payload.Length + LegacyGateType18.ClientRelayHeaderSize
            >= LegacyGateType18.MaximumClientRelayLengthExclusive)
            return false;
        var dispatched = false;
        foreach (var route in _routes.Values)
        {
            if (route.IsClosed
                || route.IsInvalidated
                || Volatile.Read(ref route.NativePlayerRecog) == 0)
                continue;

            var serverUserIndex = Volatile.Read(ref route.NativeServerUserIndex);
            if (serverUserIndex <= 0
                || packet.FilterUserIndex != 0
                && packet.FilterUserIndex != unchecked((uint)serverUserIndex))
                continue;

            var routed = new InternalPacket77
            {
                Magic = InternalPacket77.MAGIC,
                ConnID = route.ConnId,
                SeqID = route.NextSequence(),
                FrameLen = checked((ushort)(InternalPacket77.HEADER_SIZE + payload.Length)),
                Cmd = Grobal2.GM_DATA,
                Field20 = checked((uint)payload.Length),
                Payload = payload
            };
            if (!route.GameResponses.Writer.TryWrite(routed))
            {
                route.AbortOnce();
                continue;
            }
            dispatched = true;
        }
        return dispatched;
    }

    internal bool TryDispatchLegacyType19(LegacyGateType19 packet)
    {
        if (packet == null) return false;

        var payload = packet.ToClientPayload();
        // The native handler passes the remainder to the same client relay
        // routine used by type 18.  That routine rejects a block whose own
        // 12-byte relay header would reach 0x8000 bytes.
        if (payload.Length < LegacyGateType19.ClientPacketSize
            || payload.Length + LegacyGateType19.ClientRelayHeaderSize
            >= LegacyGateType19.MaximumClientRelayLengthExclusive)
            return false;

        var dispatched = false;
        var ids = packet.SessionIds ?? Array.Empty<ushort>();
        foreach (var id in ids)
        {
            if (!TryGetLegacyType19Route(id, out var route))
                continue;

            var routed = new InternalPacket77
            {
                Magic = InternalPacket77.MAGIC,
                ConnID = route.ConnId,
                SeqID = route.NextSequence(),
                FrameLen = checked((ushort)(InternalPacket77.HEADER_SIZE + payload.Length)),
                Cmd = Grobal2.GM_DATA,
                Field20 = checked((uint)payload.Length),
                Payload = payload
            };
            if (!route.GameResponses.Writer.TryWrite(routed))
            {
                route.AbortOnce();
                continue;
            }
            dispatched = true;
        }
        return dispatched;
    }

    private bool TryGetLegacyType19Route(ushort sessionId,
        out SharedBackendRoute route)
    {
        // Type 19 contains the native GameGate-generated WORD session key.
        // It is a separate namespace from both the OS handle and M2's server
        // user index; never use either as a fallback because equal numeric
        // values must not cause cross-route delivery.
        var key = (uint)sessionId;
        if (_routes.TryGetValue(key, out route!)
            && route.NativeSessionId == sessionId
            && !route.IsClosed && !route.IsInvalidated)
            return true;

        route = null!;
        return false;
    }

    private void RemoveRoute(SharedBackendRoute route)
    {
        if (_routes.TryGetValue(route.ConnId, out var current) && ReferenceEquals(current, route))
            _routes.TryRemove(route.ConnId, out _);
        route.DbResponses.Writer.TryComplete();
        route.GameResponses.Writer.TryComplete();
    }

    private bool TryGetDbState(out NetworkStream stream, out long generation)
    {
        lock (_dbStateLock)
        {
            stream = _dbStream!;
            generation = _dbGeneration;
            return DBConnected && stream != null;
        }
    }

    private bool TryGetGameState(out NetworkStream stream, out long generation)
    {
        lock (_gameStateLock)
        {
            stream = _gameStream!;
            generation = _gameGeneration;
            return GameConnected && stream != null;
        }
    }

    private bool IsCurrentDbStream(NetworkStream stream, long generation)
    {
        lock (_dbStateLock)
            return DBConnected && ReferenceEquals(stream, _dbStream) && generation == _dbGeneration;
    }

    private bool IsCurrentGameStream(NetworkStream stream, long generation)
    {
        lock (_gameStateLock)
            return GameConnected && ReferenceEquals(stream, _gameStream) && generation == _gameGeneration;
    }

    private void InvalidateDb(NetworkStream? expected, string? reason)
    {
        TcpClient? client;
        long invalidatedGeneration;
        TaskCompletionSource<int>? registrationCompletion;
        lock (_dbStateLock)
        {
            if (expected != null && !ReferenceEquals(expected, _dbStream)) return;
            client = _dbClient;
            invalidatedGeneration = _dbGeneration;
            _dbClient = null;
            _dbStream = null;
            DBConnected = false;
            registrationCompletion = _dbRegistrationCompletion;
            _dbRegistrationCompletion = null;
            _dbRegistrationGeneration = 0;
        }
        Volatile.Write(ref _registeredDbGateId, 0);
        registrationCompletion?.TrySetResult(0);
        try { client?.Dispose(); } catch { }
        if (client != null)
        {
            foreach (var route in _routes.Values)
            {
                if (route.DbConnectionGeneration == invalidatedGeneration)
                    route.InvalidateDbRoute();
            }
        }
        if (!string.IsNullOrEmpty(reason)) LogBackendError(ref _lastDbErrorTick, "DBSvr", reason);
    }

    private void InvalidateGame(NetworkStream? expected, string? reason)
    {
        TcpClient? client;
        long invalidatedGeneration;
        TaskCompletionSource<int>? registrationCompletion;
        lock (_gameStateLock)
        {
            if (expected != null && !ReferenceEquals(expected, _gameStream)) return;
            client = _gameClient;
            invalidatedGeneration = _gameGeneration;
            _gameClient = null;
            _gameStream = null;
            GameConnected = false;
            registrationCompletion = _gameRegistrationCompletion;
            _gameRegistrationCompletion = null;
            _gameRegistrationGeneration = 0;
        }
        Volatile.Write(ref _registeredGateIndex, 0);
        Volatile.Write(ref _heartbeatPending, 0);
        Interlocked.Exchange(ref _heartbeatSentTick, 0);
        registrationCompletion?.TrySetResult(0);
        try { client?.Dispose(); } catch { }
        if (client != null)
        {
            foreach (var route in _routes.Values)
            {
                if (route.GameConnectionGeneration == invalidatedGeneration)
                    route.InvalidateGameRoute();
            }
        }
        if (!string.IsNullOrEmpty(reason)) LogBackendError(ref _lastGameErrorTick, "GameSvr", reason);
    }

    private static bool IsValidNativeGateIndex(int gateIndex) =>
        gateIndex is >= NativeGameGateCommands.MinGateIndex
            and <= NativeGameGateCommands.MaxGateIndex;

    private void LogBackendError(ref long lastTick, string backend, string error)
    {
        var now = Environment.TickCount64;
        var previous = Interlocked.Read(ref lastTick);
        if (previous != 0 && now - previous < 30000) return;
        Interlocked.Exchange(ref lastTick, now);
        _log("WARN", $"{backend} shared connection: {error}");
    }

    private static InternalPacket77 CreateGameControl(uint connId, uint sequence,
        ushort command, byte[] payload)
    {
        return new InternalPacket77
        {
            Magic = InternalPacket77.MAGIC,
            ConnID = connId,
            SeqID = sequence,
            FrameLen = (ushort)(InternalPacket77.HEADER_SIZE + payload.Length),
            Cmd = command,
            Field16 = unchecked((uint)Environment.TickCount),
            Field20 = (uint)payload.Length,
            Payload = payload
        };
    }

    private static async Task DelayRetry(CancellationToken cancellationToken)
    {
        try { await Task.Delay(500, cancellationToken); }
        catch (OperationCanceledException) { }
    }

    public void Dispose()
    {
        try { StopAsync().GetAwaiter().GetResult(); } catch { }
    }
}
