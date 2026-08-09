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
    private int _sequence;

    public required int Handle { get; init; }
    public required uint ConnId { get; init; }
    public required long SessionGeneration { get; init; }
    public required string ClientIp { get; init; }
    public required byte[] DbOpenFrame { get; init; }
    public required Action Abort { get; init; }
    public long DbConnectionGeneration;
    public long GameConnectionGeneration;
    public int NativePlayerRecog;
    public int NativeServerUserIndex;
    public readonly SemaphoreSlim DbOpenLock = new(1, 1);
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

    public bool TryClose() => Interlocked.Exchange(ref _closed, 1) == 0;

    public uint NextSequence()
    {
        var low = unchecked((uint)Interlocked.Increment(ref _sequence)) & 0xFFFF;
        return ((ConnId & 0xFFFF) << 16) | low;
    }

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
        AbortOnce();
    }
}

/// <summary>
/// Original GameGate topology: one shared DBSvr socket and one shared M2 socket.
/// Logical client handles are carried in %.../ text routes and InternalPacket77.ConnID.
/// </summary>
internal sealed class SharedBackendHub : IDisposable
{
    private readonly GateConfig _config;
    private readonly Action<string, string> _log;
    private readonly ConcurrentDictionary<uint, SharedBackendRoute> _routes = new();
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
    private int _heartbeatSequence;

    public SharedBackendHub(GateConfig config, Action<string, string> log)
    {
        _config = config;
        _log = log;
    }

    public bool DBConnected { get; private set; }
    public bool GameConnected { get; private set; }
    public int Reconnects => Volatile.Read(ref _reconnects);

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        _stop = new CancellationTokenSource();
        _dbDispatcher = Task.Run(() => DBDispatcherLoop(_stop.Token));
        _gameDispatcher = Task.Run(() => GameDispatcherLoop(_stop.Token));
        _heartbeat = Task.Run(() => HeartbeatLoop(_stop.Token));
    }

    public async Task<SharedBackendRoute?> OpenRouteAsync(int handle, string clientIp,
        long sessionGeneration, Action abort, CancellationToken cancellationToken)
    {
        var connId = unchecked((uint)handle);
        var route = new SharedBackendRoute
        {
            Handle = handle,
            ConnId = connId,
            SessionGeneration = sessionGeneration,
            ClientIp = clientIp,
            DbOpenFrame = HUtil32.GetBytes($"%O{handle}/{clientIp}/{clientIp}$"),
            Abort = abort
        };
        if (!_routes.TryAdd(connId, route)) return null;

        if (!await EnsureDbRouteOpenAsync(route, cancellationToken))
        {
            route.TryClose();
            RemoveRoute(route);
            return null;
        }

        await EnsureGameRouteOpenAsync(route, cancellationToken);
        return route;
    }

    public async Task<bool> SendDbAsync(SharedBackendRoute route, byte[] frame,
        CancellationToken cancellationToken = default)
    {
        if (route == null || route.IsClosed || route.IsInvalidated) return false;
        if (!await EnsureDbRouteOpenAsync(route, cancellationToken)) return false;
        if (!TryGetDbState(out var stream, out _)) return false;
        try
        {
            await WriteDbCoreAsync(stream, frame, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            InvalidateDb(stream, ex.Message);
            return false;
        }
    }

    public async Task<bool> SendGameAsync(SharedBackendRoute route, byte[] frame,
        CancellationToken cancellationToken = default)
    {
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

        if (TryGetDbState(out var dbStream, out var dbGeneration)
            && route.DbConnectionGeneration == dbGeneration)
        {
            try
            {
                await WriteDbCoreAsync(dbStream, HUtil32.GetBytes($"%X{route.Handle}$"),
                    CancellationToken.None);
            }
            catch { }
        }

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
        var parser = new PercentDollarFrameParser();
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
                foreach (var route in _routes.Values)
                    await EnsureDbRouteOpenAsync(route, cancellationToken);

                while (!cancellationToken.IsCancellationRequested
                       && IsCurrentDbStream(stream, generation))
                {
                    var count = await stream.ReadAsync(buffer, cancellationToken);
                    if (count <= 0) throw new IOException("DBSvr closed the shared connection");
                    if (!parser.TryAppend(buffer, 0, count, out var frames, out var error))
                        throw new InvalidDataException(error);
                    foreach (var frame in frames) DispatchDbFrame(frame);
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
                    await EnsureGameRouteOpenAsync(route, cancellationToken);

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
                        else if (frame.LegacyType18 != null)
                            TryDispatchLegacyType18(frame.LegacyType18);
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
                await SendGameHeartbeatOnceAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    internal async Task<bool> SendGameHeartbeatOnceAsync(CancellationToken cancellationToken)
    {
        if (!await EnsureGameConnectedAsync(cancellationToken)) return false;
        if (!TryGetGameState(out var stream, out _)) return false;
        try
        {
            var heartbeat = CreateGameControl(0,
                unchecked((uint)Interlocked.Increment(ref _heartbeatSequence)),
                Grobal2.GM_CHECKCLIENT, Array.Empty<byte>());
            await WriteGameCoreAsync(stream, heartbeat.ToBytes(), cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            InvalidateGame(stream, ex.Message);
            return false;
        }
    }

    private async Task<bool> EnsureDbRouteOpenAsync(SharedBackendRoute route,
        CancellationToken cancellationToken)
    {
        if (route.IsClosed || route.IsInvalidated
            || !await EnsureDbConnectedAsync(cancellationToken)) return false;
        await route.DbOpenLock.WaitAsync(cancellationToken);
        try
        {
            if (route.IsClosed || route.IsInvalidated
                || !TryGetDbState(out var stream, out var generation)) return false;
            if (route.DbConnectionGeneration == generation) return true;
            await WriteDbCoreAsync(stream, route.DbOpenFrame, cancellationToken);
            route.DbConnectionGeneration = generation;
            return true;
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            if (TryGetDbState(out var stream, out _)) InvalidateDb(stream, ex.Message);
            return false;
        }
        finally { route.DbOpenLock.Release(); }
    }

    private async Task<bool> EnsureGameRouteOpenAsync(SharedBackendRoute route,
        CancellationToken cancellationToken)
    {
        if (route.IsClosed || route.IsInvalidated
            || !await EnsureGameConnectedAsync(cancellationToken)) return false;
        await route.GameOpenLock.WaitAsync(cancellationToken);
        try
        {
            if (route.IsClosed || route.IsInvalidated
                || !TryGetGameState(out var stream, out var generation)) return false;
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
                }
                Volatile.Write(ref _nextDbConnectTick, 0);
                Interlocked.Increment(ref _reconnects);
                _log("INFO", $"DBSvr shared connection {_config.BackendIP}:{_config.BackendPort2}");
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
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
                }
                Volatile.Write(ref _nextGameConnectTick, 0);
                Interlocked.Increment(ref _reconnects);
                _log("INFO", $"GameSvr shared connection {_config.GameBackendIP}:{_config.BackendPort}");
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
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

    private void DispatchDbFrame(byte[] frame)
    {
        var slash = Array.IndexOf(frame, (byte)'/');
        if (slash <= 1) return;
        var start = 1;
        if (start < slash && frame[start] != (byte)'-' && !char.IsDigit((char)frame[start])) start++;
        if (!int.TryParse(System.Text.Encoding.ASCII.GetString(frame, start, slash - start),
                out var handle)) return;
        if (!_routes.TryGetValue(unchecked((uint)handle), out var route) || route.IsClosed
            || route.IsInvalidated) return;
        if (!route.DbResponses.Writer.TryWrite(frame)) route.AbortOnce();
    }

    private async Task DispatchGamePacketAsync(NetworkStream stream, long generation,
        InternalPacket77 packet, CancellationToken cancellationToken)
    {
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
        if (!_routes.TryGetValue(packet.ConnID, out var route) || route.IsClosed
            || route.IsInvalidated) return;
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

    internal bool TryDispatchLegacyType18(LegacyGateType18 packet)
    {
        if (packet == null) return false;

        var payload = packet.ToClientPayload();
        // The native relay adds its own 12-byte transport header and drops a
        // client payload when the resulting block is not below 0x8000 bytes.
        if (payload.Length + LegacyGateType18.ClientRelayHeaderSize
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
        lock (_dbStateLock)
        {
            if (expected != null && !ReferenceEquals(expected, _dbStream)) return;
            client = _dbClient;
            invalidatedGeneration = _dbGeneration;
            _dbClient = null;
            _dbStream = null;
            DBConnected = false;
        }
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
        lock (_gameStateLock)
        {
            if (expected != null && !ReferenceEquals(expected, _gameStream)) return;
            client = _gameClient;
            invalidatedGeneration = _gameGeneration;
            _gameClient = null;
            _gameStream = null;
            GameConnected = false;
        }
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
