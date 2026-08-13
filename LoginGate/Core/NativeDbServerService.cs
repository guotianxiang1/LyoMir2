using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using SystemModule.Packet;

namespace LoginGate.Core;

internal sealed class NativeDbServerService
{
    private const int MaximumBackendConnections = 128;
    private const int MaximumConcurrentAuthentications = 64;
    private readonly LoginGateConfig _config;
    private readonly ILoginTicketAuthenticator _authenticator;
    private readonly LoginGateCounters _counters;
    private readonly Action<string, string> _log;
    private readonly Action _stateChanged;
    private readonly ConcurrentDictionary<long, TcpClient> _clients = new();
    private readonly ConcurrentDictionary<long, Task> _sessionTasks = new();
    private readonly ConcurrentDictionary<long, LoginGateBackendState> _backends = new();
    private readonly ConcurrentDictionary<long, NativeConnectionState> _liveConnections = new();
    private readonly ConcurrentDictionary<uint, PendingSelect> _pendingSelects = new();
    private readonly SemaphoreSlim _connectionSlots = new(
        MaximumBackendConnections, MaximumBackendConnections);
    private readonly SemaphoreSlim _authenticationSlots = new(
        MaximumConcurrentAuthentications, MaximumConcurrentAuthentications);
    private readonly object _lifecycle = new();
    private CancellationTokenSource? _stop;
    private TcpListener? _listener;
    private Task? _acceptTask;
    private long _nextConnectionId;
    private int _nextSessionId = 999;

    public NativeDbServerService(LoginGateConfig config,
        ILoginTicketAuthenticator authenticator, LoginGateCounters counters,
        Action<string, string> log, Action stateChanged)
    {
        _config = config;
        _authenticator = authenticator;
        _counters = counters;
        _log = log;
        _stateChanged = stateChanged;
    }

    public int BoundPort { get; private set; }
    public int RegisteredCount => _backends.Count;

    public IReadOnlyList<LoginGateBackendSnapshot> GetBackends() =>
        _backends.Values.Select(backend => backend.Snapshot())
            .OrderBy(backend => backend.ConnectionId).ToArray();

    public LoginGateBackendSnapshot? FindRoute(LoginGateArea area, LoginGateGroup group)
    {
        var routes = GetBackends().Where(route => route.RouteReady);
        if (!string.IsNullOrWhiteSpace(group.DbServerName))
        {
            routes = routes.Where(route => route.ServerName.Equals(
                group.DbServerName, StringComparison.OrdinalIgnoreCase));
        }

        return routes.FirstOrDefault(route =>
            route.GroupIndex == group.Index && route.AreaIndex == area.AreaIdx);
    }

    /// <summary>
    /// uGateListen.pas:197 ContinueSelectServer → uMainThread.pas:278-285
    /// allocates ciSessionID and sends GDM_SELECT_SERVER (1001). The 2001
    /// reply is the only source of GameGate IP/port for the client jump.
    /// </summary>
    public async Task<SelectServerResult> RequestSelectServerAsync(
        LoginGateArea area, LoginGateGroup group, int encodeIndex,
        ushort clientSocketHandle, CancellationToken cancellationToken)
    {
        var backend = FindRegisteredBackend(area, group);
        if (backend == null)
            return SelectServerResult.Fail(3);

        if (!_liveConnections.TryGetValue(backend.ConnectionId, out var connection)
            || connection.Stream == null)
            return SelectServerResult.Fail(3);

        var sessionId = NextSessionId();
        if (_config.SecondZone)
            sessionId ^= LoginGateWireProtocol.NativeSecondZoneSessionXor;

        if (!LoginGateWireProtocol.TryCreateSelectGroupInfo(
                sessionId, encodeIndex, clientSocketHandle,
                checked((ushort)area.AreaIdx), checked((byte)group.Index),
                area.Suffix, out var payload, out var buildError))
            throw new InvalidDataException(buildError);

        var pending = new PendingSelect(connection.ConnectionId, clientSocketHandle,
            new TaskCompletionSource<NativeLoginGateProbeRoute?>(
                TaskCreationOptions.RunContinuationsAsynchronously));
        if (!_pendingSelects.TryAdd(sessionId, pending))
            return SelectServerResult.Fail(2);

        try
        {
            if (!LoginGateWireProtocol.TryCreateNativeProbeRequest(payload,
                    out var request, out var error))
                throw new InvalidDataException(error);
            await SendFrameAsync(connection, connection.Stream, request,
                cancellationToken).ConfigureAwait(false);

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, connection.Token);
            var completed = await pending.Completion.Task.WaitAsync(linked.Token)
                .ConfigureAwait(false);
            if (completed == null)
                return SelectServerResult.Fail(3);
            if (completed.SessionId == 0)
                return SelectServerResult.Fail(
                    completed.ErrorType == 0 ? (byte)3 : completed.ErrorType);
            return SelectServerResult.Ok(completed);
        }
        catch (OperationCanceledException)
        {
            return SelectServerResult.Fail(3);
        }
        finally
        {
            _pendingSelects.TryRemove(sessionId, out _);
        }
    }

    private LoginGateBackendSnapshot? FindRegisteredBackend(
        LoginGateArea area, LoginGateGroup group)
    {
        var routes = GetBackends();
        if (!string.IsNullOrWhiteSpace(group.DbServerName))
        {
            return routes.FirstOrDefault(route => route.ServerName.Equals(
                group.DbServerName, StringComparison.OrdinalIgnoreCase));
        }

        return routes.FirstOrDefault(route =>
            route.GroupIndex == group.Index && route.AreaIndex == area.AreaIdx);
    }

    private uint NextSessionId()
    {
        while (true)
        {
            var next = unchecked((uint)Interlocked.Increment(ref _nextSessionId));
            if (next >= 1000) return next;
            Interlocked.CompareExchange(ref _nextSessionId, 1000, (int)next);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_lifecycle)
        {
            if (_listener != null) return Task.CompletedTask;
            _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _listener = new TcpListener(IPAddress.Any, _config.DBServerListen);
            _listener.Server.NoDelay = true;
            _listener.Start(MaximumBackendConnections);
            BoundPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _acceptTask = AcceptLoopAsync(_listener, _stop.Token);
        }
        _log("INFO", $"DBServer Native77 监听端口：{BoundPort}");
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        Task? acceptTask;
        CancellationTokenSource? stop;
        lock (_lifecycle)
        {
            if (_listener == null) return;
            stop = _stop;
            acceptTask = _acceptTask;
            _stop = null;
            _acceptTask = null;
            try { stop?.Cancel(); } catch { }
            try { _listener.Stop(); } catch { }
            _listener = null;
            BoundPort = 0;
        }

        foreach (var client in _clients.Values)
        {
            try { client.Dispose(); } catch { }
        }
        if (acceptTask != null)
        {
            try { await acceptTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (SocketException) when (stop?.IsCancellationRequested == true) { }
        }

        var tasks = _sessionTasks.Values.ToArray();
        if (tasks.Length != 0)
        {
            try { await Task.WhenAll(tasks).ConfigureAwait(false); }
            catch { }
        }
        foreach (var pending in _pendingSelects.Values)
            pending.Completion.TrySetResult(null);
        _pendingSelects.Clear();
        _liveConnections.Clear();
        _backends.Clear();
        stop?.Dispose();
        _stateChanged();
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await _connectionSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
            TcpClient? client = null;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken)
                    .ConfigureAwait(false);
                client.NoDelay = true;
                if (!IsAllowedBackend(client))
                {
                    _log("WARN", "拒绝未列入 DBServerIP 的 Native77 连接");
                    client.Dispose();
                    _connectionSlots.Release();
                    continue;
                }

                var connectionId = Interlocked.Increment(ref _nextConnectionId);
                _clients[connectionId] = client;
                var task = HandleConnectionAsync(connectionId, client, cancellationToken);
                _sessionTasks[connectionId] = task;
                _ = task.ContinueWith(completedTask =>
                {
                    _sessionTasks.TryRemove(connectionId, out _);
                    if (_clients.TryRemove(connectionId, out var completedClient))
                    {
                        try { completedClient.Dispose(); } catch { }
                    }
                    _liveConnections.TryRemove(connectionId, out _);
                    FailPendingSelects(connectionId);
                    _backends.TryRemove(connectionId, out _);
                    _connectionSlots.Release();
                    _stateChanged();
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                client = null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                client?.Dispose();
                _connectionSlots.Release();
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                client?.Dispose();
                _connectionSlots.Release();
                break;
            }
            catch
            {
                client?.Dispose();
                _connectionSlots.Release();
                throw;
            }
        }
    }

    private async Task HandleConnectionAsync(long connectionId, TcpClient client,
        CancellationToken serverCancellation)
    {
        var remote = client.Client.RemoteEndPoint?.ToString() ?? "?";
        var state = new NativeConnectionState(connectionId, serverCancellation);
        var parser = new YbDbLegacy77StreamParser();
        var buffer = new byte[8192];
        var stream = client.GetStream();
        state.Stream = stream;
        _liveConnections[connectionId] = state;
        _log("CONNECT", $"DBServer 已连接：{remote}");

        try
        {
            while (!state.Token.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer, state.Token)
                    .ConfigureAwait(false);
                if (read == 0) break;
                var frames = new List<YbDbLegacy77Frame>();
                parser.Append(buffer.AsSpan(0, read), frames.Add);
                foreach (var frame in frames)
                {
                    await ProcessFrameAsync(state, stream, frame, state.Token)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (state.Token.IsCancellationRequested) { }
        catch (IOException) { }
        catch (SocketException) { }
        catch (Exception ex)
        {
            _counters.FrameRejected();
            _log("ERROR", $"DBServer Native77 数据错误：{ex.Message}");
        }
        finally
        {
            await state.StopAuthenticationsAsync().ConfigureAwait(false);
            state.Dispose();
            _log("INFO", $"DBServer 已断开：{remote}");
        }
    }

    private async Task ProcessFrameAsync(NativeConnectionState connection,
        NetworkStream stream, YbDbLegacy77Frame frame, CancellationToken cancellationToken)
    {
        switch (frame.Ident)
        {
            case LoginGateWireProtocol.NativeRegistrationIdent:
                if (!LoginGateWireProtocol.TryParseNativeRegistration(frame,
                        out var registration, out var registrationError))
                    throw new InvalidDataException(registrationError);

                var backend = _backends.GetOrAdd(connection.ConnectionId,
                    id => new LoginGateBackendState(id));
                backend.ApplyRegistration(registration);
                connection.Registered = true;
                await SendFrameAsync(connection, stream,
                    LoginGateWireProtocol.CreateNativeRegistrationAck(), cancellationToken)
                    .ConfigureAwait(false);
                // uDBListen.pas:306-312: a 2000 is answered with GDM_PING (1000)
                // only. GDM_SELECT_SERVER (1001) is issued later from
                // RequestSelectServer (uDBListen.pas:231), never from the
                // heartbeat. Sending a random 1001 here invented ghost sessions.
                _log("INFO", $"DBServer 注册：{registration.ServerName}，在线 {registration.OnlineCount}");
                _stateChanged();
                return;

            case LoginGateWireProtocol.NativeProbeResponseIdent:
                if (!LoginGateWireProtocol.TryParseNativeProbeResponse(frame,
                        out var route, out var routeError))
                {
                    _log("WARN", $"忽略畸形 DGM_SELECT_SERVER(2001)：{routeError}");
                    return;
                }
                // Success replies keep ciSessionID; failure zeros it and LoginGate
                // finds the client by wSocketHandle (uMainThread.pas:194-196).
                if (!TryTakePendingSelect(route, connection.ConnectionId, out var pending)
                    || pending == null)
                {
                    _log("WARN", $"无对应选服请求的 2001，Session={route.SessionId}，不断连");
                    return;
                }
                if (_backends.TryGetValue(connection.ConnectionId, out var routeBackend)
                    && route.SessionId != 0)
                    routeBackend.ApplyRoute(route);
                pending.Completion.TrySetResult(route);
                _log("INFO", $"选服应答：{new IPAddress(route.Ipv4AddressBytes)}:{route.Port} " +
                             $"Area={route.AreaIndex} Group={route.GroupIndex} " +
                             $"Session={route.SessionId}");
                _stateChanged();
                return;

            case LoginGateWireProtocol.NativeAuthRequestIdent:
                if (!connection.Registered)
                    throw new InvalidDataException("Native77 auth arrived before registration");
                if (!LoginGateWireProtocol.TryParseNativeAuthRequest(frame,
                        out var request, out var requestError))
                    throw new InvalidDataException(requestError);
                connection.TrackAuthentication(
                    AuthenticateAsync(connection, stream, request, cancellationToken),
                    exception => _log("ERROR", $"并发认证任务错误：{exception.GetType().Name}"));
                return;

            case LoginGateWireProtocol.NativeDirectStaticAuthIdent:
            case LoginGateWireProtocol.NativeDirectECardAuthIdent:
                if (frame.Payload.Length == LoginGateWireProtocol.NativeStatusAuthInfoSize)
                    await ReplyModuleNotReadyAuthAsync(connection, stream, frame,
                        cancellationToken).ConfigureAwait(false);
                return;

            case LoginGateWireProtocol.NativeDirectDynAuthIdent:
                if (frame.Payload.Length == LoginGateWireProtocol.NativeOldDynamicAuthInfoSize
                    || frame.Payload.Length == LoginGateWireProtocol.NativeDynamicAuthInfoSize)
                    await ReplyModuleNotReadyAuthAsync(connection, stream, frame,
                        cancellationToken).ConfigureAwait(false);
                return;

            case LoginGateWireProtocol.NativeType2EnabledIdent:
            case LoginGateWireProtocol.NativeType2DisabledIdent:
                if (!LoginGateWireProtocol.TryParseNativeType2Control(frame,
                        out var enabled, out var controlError))
                    throw new InvalidDataException(controlError);
                if (_backends.TryGetValue(connection.ConnectionId, out var controlBackend))
                    controlBackend.SetType2Enabled(enabled);
                _stateChanged();
                return;

            default:
                _counters.FrameRejected();
                _log("WARN", $"忽略未知 Native77 标识：{frame.Ident}");
                return;
        }
    }

    /// <summary>
    /// SDK module is unloaded in this build (uSDKAuth.pas:816-890 commented
    /// out). DirectStaticAuth / DirectDynAuth / DirectECardAuth still answer
    /// with 1004 / nResult = LC_AUTH_TIMEOUT (-2) via PushAuthHead
    /// (uSDKAuth.pas:607-608). C# used to ignore 2011/2012/2013, which would
    /// hang a real DBServer waiting for the reply.
    /// </summary>
    private async Task ReplyModuleNotReadyAuthAsync(NativeConnectionState connection,
        NetworkStream stream, YbDbLegacy77Frame frame, CancellationToken cancellationToken)
    {
        if (frame.Payload.Length < LoginGateWireProtocol.NativeAuthResponseShortPayloadSize)
            return;
        var authType = frame.Payload[0];
        var gateIndex = frame.Payload[1];
        var queryId = BinaryPrimitives.ReadInt32LittleEndian(frame.Payload.AsSpan(2, 4));
        if (!LoginGateWireProtocol.TryCreateNativeAuthFailure(
                authType, gateIndex, queryId,
                LoginGateWireProtocol.NativeLcAuthTimeout, null,
                out var response, out var error))
        {
            _log("WARN", $"无法构造 1004：{error}");
            return;
        }
        await SendFrameAsync(connection, stream, response, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task AuthenticateAsync(NativeConnectionState connection,
        NetworkStream stream, NativeLoginGateAuthRequest request,
        CancellationToken cancellationToken)
    {
        // uSDKAuth.pas:590 stamps AddTick when the request is queued, and the
        // sweeper at :759 measures from there, so the 20 s budget has to cover the
        // wait for a slot too -- native has no such cap and never delays the clock.
        var deadline = Environment.TickCount64
                       + (long)LoginGateWireProtocol.NativeAuthTimeout.TotalMilliseconds;
        await _authenticationSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
        _counters.AuthenticationStarted();
        _stateChanged();
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            var authentication = InvokeAuthenticatorAsync(request, budget.Token);
            var remaining = deadline - Environment.TickCount64;
            var timedOut = remaining <= 0;
            if (!timedOut)
            {
                // Race a timer rather than relying on the authenticator to honour the
                // token: uSDKAuth.pas:747 runs the sweep on the main loop, independent
                // of whatever the vendor SDK is doing, and always answers within 20 s.
                using var timer = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
                var elapsed = Task.Delay(TimeSpan.FromMilliseconds(remaining), timer.Token);
                if (await Task.WhenAny(authentication, elapsed).ConfigureAwait(false)
                    == authentication)
                    timer.Cancel();
                else
                    timedOut = true;
            }
            cancellationToken.ThrowIfCancellationRequested();

            LoginTicketAuthenticationResult result;
            if (timedOut)
            {
                budget.Cancel();
                _log("WARN", $"票据认证超时：QueryId={request.QueryId}");
                result = LoginTicketAuthenticationResult.Rejected("native auth timeout");
            }
            else
            {
                result = await authentication.ConfigureAwait(false);
            }

            YbDbLegacy77Frame response;
            string error;
            if (result.Success && TryCreateSuccessTail(result.Account, out var tail, out error))
            {
                // GateIdx (+1) is echoed, not chosen: PushAuthHead stores the whole
                // request head (uSDKAuth.pas:591) and the reply ships that copy.
                if (!LoginGateWireProtocol.TryCreateNativeAuthResponse124(
                        LoginGateWireProtocol.NativeAuthTypeLoginCenter,
                        request.ProtocolVersion, request.QueryId, tail,
                        out response, out error))
                    throw new InvalidDataException(error);
                _log("INFO", $"票据认证成功：QueryId={request.QueryId}");
            }
            else
            {
                // 12 bytes, wAuthType still atLoginCenterAuth, and a real nResult:
                // uSDKAuth.pas:1624 sends the stored head verbatim, so +0 stays 6 and
                // +8 carries the LC code. 0 there would read as LC_AUTH_SUCCESS.
                // The sweeper distinguishes itself with LC_AUTH_TIMEOUT (:762).
                if (!LoginGateWireProtocol.TryCreateNativeAuthFailure(
                        LoginGateWireProtocol.NativeAuthTypeLoginCenter,
                        request.ProtocolVersion, request.QueryId,
                        timedOut
                            ? LoginGateWireProtocol.NativeLcAuthTimeout
                            : LoginGateWireProtocol.NativeLcAuthFailed,
                        null, out response, out error))
                    throw new InvalidDataException(error);
                if (!timedOut)
                    _log("WARN", $"票据认证失败：QueryId={request.QueryId}");
            }
            await SendFrameAsync(connection, stream, response, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _counters.AuthenticationFinished();
            _authenticationSlots.Release();
            _stateChanged();
        }
    }

    private async Task<LoginTicketAuthenticationResult> InvokeAuthenticatorAsync(
        NativeLoginGateAuthRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await _authenticator.AuthenticateAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return LoginTicketAuthenticationResult.Rejected("authentication cancelled");
        }
        catch (Exception ex)
        {
            _log("ERROR", $"本地票据校验器错误：{ex.GetType().Name}");
            return LoginTicketAuthenticationResult.Rejected("authentication failed");
        }
    }

    private static bool TryCreateSuccessTail(string account, out byte[] tail,
        out string error)
    {
        tail = new byte[LoginGateWireProtocol.NativeAuthResponseFullPayloadSize
                        - LoginGateWireProtocol.NativeAuthResponseShortPayloadSize];
        error = string.Empty;
        var value = account ?? string.Empty;
        if (value.Any(character => character > 0x7f))
        {
            error = "native account is not ASCII";
            return false;
        }
        var bytes = Encoding.ASCII.GetBytes(value);
        if (bytes.Length == 0 || bytes.Length > 20)
        {
            error = "native account must contain 1 to 20 ASCII bytes";
            return false;
        }
        bytes.CopyTo(tail, 0);
        return true;
    }

    private async Task SendFrameAsync(NativeConnectionState connection,
        NetworkStream stream, YbDbLegacy77Frame frame, CancellationToken cancellationToken)
    {
        if (!YbDbLegacy77Codec.TryEncode(frame, out var wire, out var error))
            throw new InvalidDataException(error);
        await connection.SendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await stream.WriteAsync(wire, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            connection.SendLock.Release();
        }
    }

    private bool IsAllowedBackend(TcpClient client)
    {
        if (client.Client.RemoteEndPoint is not IPEndPoint remote) return false;
        var remoteAddress = remote.Address.IsIPv4MappedToIPv6
            ? remote.Address.MapToIPv4()
            : remote.Address;
        return _config.DbServerAddresses.Any(entry =>
            LoginGateConfig.TryParseIpv4(entry.Address, out var allowed)
            && allowed.Equals(remoteAddress));
    }

    private bool TryTakePendingSelect(NativeLoginGateProbeRoute route, long connectionId,
        out PendingSelect? pending)
    {
        pending = null;
        if (route.SessionId != 0)
            return _pendingSelects.TryRemove(route.SessionId, out pending);

        foreach (var pair in _pendingSelects)
        {
            if (pair.Value.ConnectionId != connectionId
                || pair.Value.SocketHandle != route.SocketHandle)
                continue;
            if (_pendingSelects.TryRemove(pair.Key, out pending))
                return true;
        }
        return false;
    }

    private void FailPendingSelects(long connectionId)
    {
        foreach (var pair in _pendingSelects)
        {
            if (pair.Value.ConnectionId != connectionId) continue;
            if (_pendingSelects.TryRemove(pair.Key, out var pending))
                pending.Completion.TrySetResult(null);
        }
    }

    private sealed class NativeConnectionState : IDisposable
    {
        private readonly CancellationTokenSource _stop;
        private readonly ConcurrentDictionary<long, Task> _authenticationTasks = new();
        private long _nextAuthenticationId;

        public NativeConnectionState(long connectionId, CancellationToken serverCancellation)
        {
            ConnectionId = connectionId;
            _stop = CancellationTokenSource.CreateLinkedTokenSource(serverCancellation);
        }

        public long ConnectionId { get; }
        public NetworkStream? Stream { get; set; }
        public CancellationToken Token => _stop.Token;
        public bool Registered { get; set; }
        public SemaphoreSlim SendLock { get; } = new(1, 1);

        public void TrackAuthentication(Task task, Action<Exception> onFailure)
        {
            var taskId = Interlocked.Increment(ref _nextAuthenticationId);
            _authenticationTasks[taskId] = task;
            _ = task.ContinueWith(completedTask =>
            {
                _authenticationTasks.TryRemove(taskId, out _);
                if (completedTask.IsFaulted &&
                    completedTask.Exception?.GetBaseException() is { } exception)
                {
                    onFailure(exception);
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        public async Task StopAuthenticationsAsync()
        {
            _stop.Cancel();
            var tasks = _authenticationTasks.Values.ToArray();
            if (tasks.Length == 0) return;
            try { await Task.WhenAll(tasks).ConfigureAwait(false); }
            catch { }
        }

        public void Dispose()
        {
            _stop.Dispose();
            SendLock.Dispose();
        }
    }

    private sealed class PendingSelect
    {
        public PendingSelect(long connectionId, ushort socketHandle,
            TaskCompletionSource<NativeLoginGateProbeRoute?> completion)
        {
            ConnectionId = connectionId;
            SocketHandle = socketHandle;
            Completion = completion;
        }

        public long ConnectionId { get; }
        public ushort SocketHandle { get; }
        public TaskCompletionSource<NativeLoginGateProbeRoute?> Completion { get; }
    }
}
